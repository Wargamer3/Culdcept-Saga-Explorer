using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Buffers.Binary;
using System.Collections.Generic;
using CuldceptSagaExplorer.AssetManager.RSA;
using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.AssetManager.RST;
using CuldceptSagaExplorer.AssetManager.ExternalModel;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace CuldceptSagaExplorer.AssetManager.RSE
{
    public static class RseExporter
    {
        public const float DefaultPreviewFramesPerSecond = 60;

        public static ModelRoot Load(string ScenePath, float FramesPerSecond = DefaultPreviewFramesPerSecond, string DependencyDirectory = null)
        {
            if (!float.IsFinite(FramesPerSecond) || FramesPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(FramesPerSecond));
            }

            RseScene Scene = RseReader.Parse(File.ReadAllText(ScenePath), ScenePath);
            string TimelineName = Path.GetFileNameWithoutExtension(ScenePath) + " (RSE)";
            string DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(ScenePath));
            string FallbackDirectory = null;
            if (DependencyDirectory != null)
            {
                FallbackDirectory = Path.GetFullPath(DependencyDirectory);
            }

            if (FallbackDirectory != null && !Directory.Exists(FallbackDirectory))
            {
                throw new DirectoryNotFoundException($"The selected RSE dependency folder does not exist: {FallbackDirectory}");
            }

            DependencyLookup Dependencies = new DependencyLookup(DirectoryPath, FallbackDirectory);
            JsonArray NestedEffects = new JsonArray();
            Dictionary<int, string> ObjectDirectories = new Dictionary<int, string>();
            Dictionary<int, int> ObjectDurations = new Dictionary<int, int>();
            foreach (RseEffectCommand Command in Scene.ListEffectCommand)
            {
                string ChildPath = Dependencies.Resolve(Scene.DicEffect[Command.EffectId], "effect",
                    "The RSE's declared child effect is missing.");
                if (string.Equals(ChildPath, Path.GetFullPath(ScenePath), StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException($"{ScenePath}: recursive nested effects are not yet supported.");
                }

                RseScene Child = RseReader.Parse(File.ReadAllText(ChildPath), ChildPath);
                if (Child.Loop != false || Child.TotalFrames > Scene.TotalFrames
                    || Child.AutoTexture != Scene.AutoTexture || Child.DicObject.Count == 0
                    || Child.DicObject.Values.Any(Object => Object.Kind != RseObjectKind.Model)
                    || Child.ListEffectCommand.Count > 0 || Child.HasSoundCues || Child.DicIgnoredModelDeclaration.Count > 0
                    || Child.ListUnmatchedSetupCommand.Count > 0 || Child.ListRejectedMotionCommand.Count > 0
                    || Child.ListNativeMotionTransition.Count > 0 || Child.ListRawOpacityCommand.Count > 0)
                {
                    throw new NotSupportedException($"{ChildPath}: nested children currently require nonlooping model-only scenes no longer than the parent, with matching AUTORST, no sound cues, no raw-opacity commands, and no ignored/coerced commands.");
                }

                JsonArray ObjectMap = new JsonArray();
                foreach (RseObject Object in Child.DicObject.Values)
                {
                    int OriginalId = Object.Id, NewId = 0;
                    while (Scene.DicObject.ContainsKey(NewId) || Scene.DicIgnoredModelDeclaration.ContainsKey(NewId))
                    {
                        if (NewId == int.MaxValue)
                        {
                            throw new InvalidDataException($"{ScenePath}: no free nested object ID.");
                        }

                        NewId++;
                    }
                    Object.Id = NewId;
                    if (Object.MotionId.HasValue)
                    {
                        int MotionId = 0;
                        while (Scene.DicMotion.ContainsKey(MotionId))
                        {
                            if (MotionId == int.MaxValue)
                            {
                                throw new InvalidDataException($"{ScenePath}: no free nested motion ID.");
                            }

                            MotionId++;
                        }
                        Scene.DicMotion.Add(MotionId, Child.DicMotion[Object.MotionId.Value]);
                        Object.MotionId = MotionId;
                    }
                    Scene.DicObject.Add(NewId, Object);
                    ObjectDirectories.Add(NewId, Path.GetDirectoryName(ChildPath));
                    ObjectDurations.Add(NewId, Child.TotalFrames);
                    ObjectMap.Add(new JsonObject { ["sourceObjectId"] = OriginalId, ["exportObjectId"] = NewId });
                }
                NestedEffects.Add(new JsonObject
                {
                    ["generatorId"] = Command.ObjectId,
                    ["effectId"] = Command.EffectId,
                    ["file"] = Scene.DicEffect[Command.EffectId],
                    ["startFrame"] = Command.Frame,
                    ["totalFrames"] = Child.TotalFrames,
                    ["loop"] = false,
                    ["objects"] = ObjectMap
                });
            }
            Dictionary<int, CuldceptModel> DicPackage = new Dictionary<int, CuldceptModel>();
            Dictionary<CuldceptModel, int> MotionPeriods = new Dictionary<CuldceptModel, int>();
            Dictionary<CuldceptModel, Dictionary<string, int[]>> TextureLists = new Dictionary<CuldceptModel, Dictionary<string, int[]>>();
            foreach (RseObject Object in Scene.DicObject.Values.Where(Object => Object.Kind == RseObjectKind.Model))
            {
                ObjectDirectories.TryGetValue(Object.Id, out string SourceDirectory);
                CuldceptModel Package = LoadPackage(Scene, Dependencies, TextureLists, Object.FileName, SourceDirectory, Object.Id);
                if (Package == null)
                {
                    if (Object.MotionId.HasValue)
                    {
                        string MotionFile = Scene.DicMotion[Object.MotionId.Value];
                        string MotionDirectory = DirectoryPath;
                        if (SourceDirectory != null)
                        {
                            MotionDirectory = SourceDirectory;
                        }

                        string MotionLocal = Path.Combine(MotionDirectory, MotionFile);
                        string MotionFallback = null;
                        if (FallbackDirectory != null)
                        {
                            MotionFallback = Path.Combine(FallbackDirectory, MotionFile);
                        }
                        if (!File.Exists(MotionLocal)
                            && (MotionFallback == null || !File.Exists(MotionFallback)))
                        {
                            Dependencies.NoteAbsent(Object.Id, MotionFile, "motion", MotionLocal, MotionFallback);
                        }
                    }
                    continue;
                }

                if (ObjectDurations.TryGetValue(Object.Id, out int ChildFrames) && ChildFrames < Scene.TotalFrames
                    && (Package.Document.ListMaterial.Count == 0 || Enumerable.Range(0, Package.Document.ListMaterial.Count)
                        .Any(Slot => !Object.DicMaterialOpacity.TryGetValue(Slot, out SortedDictionary<int, float> Keys) || Keys.Last().Value != 0)))
                {
                    throw new NotSupportedException($"{ScenePath}: shorter nested children currently require explicit terminal zero MOPA on every material slot.");
                }

                if (Object.MotionId.HasValue)
                {
                    string MotionFile = Scene.DicMotion[Object.MotionId.Value];
                    string MotionPath = Dependencies.TryResolve(MotionFile, "motion", SourceDirectory, out string MotionLocal, out string MotionFallback);
                    if (MotionPath == null)
                    {
                        Dependencies.NoteAbsent(Object.Id, MotionFile, "motion", MotionLocal, MotionFallback);
                    }
                    else
                    {
                        Package.Animations.Add(LoadMotion(MotionPath, Package, Scene.TotalFrames, TimelineName, Object.MotionLoops, out int Period));
                        MotionPeriods.Add(Package, Period);
                    }
                }
                DicPackage.Add(Object.Id, Package);
            }

            Dictionary<int, CuldceptModel> GeneratorPackages = new Dictionary<int, CuldceptModel>();
            foreach (RseParticleGenerator Generator in Scene.ListParticleGenerator.Where(Generator => Generator.HasBoundModel))
            {
                CuldceptModel Package;
                if (Generator.ModelFileName == null)
                {
                    if (!DicPackage.TryGetValue(Generator.ModelObjectId, out Package))
                    {
                        continue;
                    }
                }
                else
                {
                    Package = LoadPackage(Scene, Dependencies, TextureLists, Generator.ModelFileName, null, Generator.ObjectId);
                    if (Package == null)
                    {
                        continue;
                    }
                }
                GeneratorPackages.Add(Generator.ObjectId, Package);
                if (Generator.ModelFileName == null && MotionPeriods.ContainsKey(Package)
                    && Scene.DicObject[Generator.ModelObjectId].MotionStartFrame != 0)
                {
                    throw new NotSupportedException($"{ScenePath}: particle source motions must currently start at scene frame zero.");
                }
            }

            float Duration = Scene.TotalFrames / FramesPerSecond;
            if (!float.IsFinite(Duration) || Duration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(FramesPerSecond), "The scene duration cannot be represented at this frame rate.");
            }

            ModelRoot Model;
            bool Simple = Scene.DicObject.Count == 1 && Scene.DicObject.Values.Single().Kind == RseObjectKind.Model
                && DicPackage.ContainsKey(Scene.ObjectId)
                && Scene.DicObject[Scene.ObjectId].Position == Vector3.Zero
                && Scene.DicObject[Scene.ObjectId].Rotation == Quaternion.Identity && Scene.DicObject[Scene.ObjectId].Scale == Vector3.One
                && Scene.DicObject[Scene.ObjectId].DicRotation.Count == 0
                && !HasDuplicateMaterials(DicPackage[Scene.ObjectId])
                && DicPackage[Scene.ObjectId].Document.ListMaterial.All(Material => Material.UvScroll == Vector2.Zero);

            bool TimelineOnly = Scene.IsEmpty || DicPackage.Count == 0 && GeneratorPackages.Count == 0;
            if (TimelineOnly)
            {
                Model = ModelRoot.CreateModel();
                Model.DefaultScene = Model.UseScene(0);
                Node Clock = Model.DefaultScene.CreateNode("rse_empty");
                Model.CreateAnimation(TimelineName).CreateScaleChannel(Clock, new SortedDictionary<float, Vector3>
                {
                    [0] = Vector3.One,
                    [Duration] = Vector3.One
                });
            }
            else if (Simple)
            {
                CuldceptModel Package = DicPackage[Scene.ObjectId];
                Model = GltfExporter.CreateGlbFromRSM(Package.Document, Package.TextureMap, Package.Animations, FramesPerSecond,
                    PreserveUnusedMaterials: true, CreateAnimationChannels: false, PreserveVertexFrameTopology: true);
            }
            else
            {
                SceneBuilder Builder = new SceneBuilder();
                foreach (RseObject Object in Scene.DicObject.Values.Where(Object => Object.Kind == RseObjectKind.Model && DicPackage.ContainsKey(Object.Id)))
                {
                    AddObject(Builder, FramesPerSecond, Object.Id, Object, DicPackage[Object.Id], false);
                }

                foreach (RseParticleGenerator Generator in Scene.ListParticleGenerator.Where(Generator => GeneratorPackages.ContainsKey(Generator.ObjectId)))
                {
                    CuldceptModel Package = GeneratorPackages[Generator.ObjectId];
                    foreach (RseObject Particle in Scene.DicObject.Values.Where(Object =>
                        Object.Kind == RseObjectKind.Particle && Object.Id >= Generator.FirstParticle && Object.Id <= Generator.LastParticle))
                    {
                        AddObject(Builder, FramesPerSecond, Particle.Id, Particle, Package, Generator.Spin == 4 || Generator.Spin == 5);
                    }
                }

                Model = Builder.ToGltf2();
                if (Model.DefaultScene == null)
                {
                    Model.DefaultScene = Model.UseScene(0);
                }

                foreach (RseObject Object in Scene.DicObject.Values.Where(Object => Object.Kind == RseObjectKind.Model && DicPackage.ContainsKey(Object.Id)))
                {
                    CompleteMaterials(Model, FramesPerSecond, Object.Id, DicPackage[Object.Id]);
                }
                foreach (RseParticleGenerator Generator in Scene.ListParticleGenerator.Where(Generator => GeneratorPackages.ContainsKey(Generator.ObjectId)))
                {
                    foreach (RseObject Particle in Scene.DicObject.Values.Where(Object => Object.Kind == RseObjectKind.Particle
                        && Object.Id >= Generator.FirstParticle && Object.Id <= Generator.LastParticle))
                    {
                        CompleteMaterials(Model, FramesPerSecond, Particle.Id, GeneratorPackages[Generator.ObjectId]);
                    }
                }

            }
            Animation Timeline = Model.LogicalAnimations.SingleOrDefault(Animation => Animation.Name == TimelineName);
            if (Timeline == null)
            {
                Timeline = Model.CreateAnimation(TimelineName);
            }
            foreach (RseObject Object in Scene.DicObject.Values.Where(Object => Object.Kind == RseObjectKind.Model && DicPackage.ContainsKey(Object.Id)))
            {
                string NamePrefix = "";
                if (!Simple)
                {
                    NamePrefix = Prefix(Object.Id);
                }

                int PlaybackFrameCount = Scene.TotalFrames;
                if (ObjectDurations.TryGetValue(Object.Id, out int ChildPlaybackFrames))
                {
                    PlaybackFrameCount = ChildPlaybackFrames;
                }

                if (Object.MotionId.HasValue && MotionPeriods.ContainsKey(DicPackage[Object.Id]))
                {
                    AddModelMotionTracks(Model, Timeline, DicPackage[Object.Id], Object, NamePrefix,
                        Scene.TotalFrames, FramesPerSecond, MotionPeriods[DicPackage[Object.Id]], PlaybackFrameCount);
                    AddVertexMotionTracks(Model, Timeline, DicPackage[Object.Id], Object, NamePrefix,
                        Scene.TotalFrames, FramesPerSecond, MotionPeriods[DicPackage[Object.Id]], PlaybackFrameCount);
                }
                if (Object.DicRotation.Count > 0)
                {
                    Node Root = Model.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Object.Id}");
                    SortedDictionary<float, Quaternion> Keys = new SortedDictionary<float, Quaternion>();
                    foreach (KeyValuePair<int, Quaternion> Key in Object.DicRotation)
                    {
                        if (!Keys.TryAdd(Key.Key / FramesPerSecond, ReflectRotation(Key.Value)))
                        {
                            throw new InvalidDataException($"{ScenePath}: quaternion key times cannot be represented distinctly.");
                        }
                    }

                    if (!Keys.TryAdd(Duration, Keys.Last().Value))
                    {
                        throw new InvalidDataException($"{ScenePath}: quaternion endpoint cannot be represented distinctly.");
                    }

                    Timeline.CreateRotationChannel(Root, Keys, linear: true);
                }
                AddOpacityTracks(Model, Timeline, DicPackage[Object.Id], Object, NamePrefix,
                    ScenePath, FramesPerSecond, Duration, Scene.TotalFrames);
                AddTextureTracks(Model, DicPackage[Object.Id], TextureLists[DicPackage[Object.Id]],
                    NamePrefix, Scene.TotalFrames, FramesPerSecond);
            }
            foreach (RseParticleGenerator Generator in Scene.ListParticleGenerator.Where(Generator => GeneratorPackages.ContainsKey(Generator.ObjectId)))
            {
                uint RandomState = unchecked((uint)Generator.Seed.GetValueOrDefault());
                // The game consumes shared generator random state in particle declaration order.
                foreach (RseObject Particle in Scene.DicObject.Values.Where(Object =>
                    Object.Kind == RseObjectKind.Particle && Object.Id >= Generator.FirstParticle && Object.Id <= Generator.LastParticle))
                {
                    bool BakedRotation = Generator.Spin == 2 || Generator.Spin == 3;
                    // SPIN=3 consumes velocities before angles; the other seeded modes consume angles first.
                    Vector3 Velocity = Vector3.Zero;
                    if (Generator.Spin == 3)
                    {
                        Velocity = ReadParticleAngles(Generator, BakedRotation, Generator.SpinVelocity, Generator.SpinVelocityRange, ref RandomState);
                    }

                    Vector3 Angles = ReadParticleAngles(Generator, BakedRotation, Generator.Angle, Generator.AngleRange, ref RandomState);
                    if (Generator.Spin == 1 || Generator.Spin == 4 || Generator.Spin == 5)
                    {
                        Velocity = ReadParticleAngles(Generator, BakedRotation, Generator.SpinVelocity, Generator.SpinVelocityRange, ref RandomState);
                    }

                    Angles *= MathF.PI / 180;
                    Velocity *= MathF.PI / 180;
                    Vector3 PoseAngles = Vector3.Zero;
                    SortedDictionary<float, Vector3> PositionKeys = new SortedDictionary<float, Vector3>();
                    SortedDictionary<float, Vector3> ScaleKeys = new SortedDictionary<float, Vector3>();
                    SortedDictionary<float, Quaternion> RotationKeys = new SortedDictionary<float, Quaternion>();
                    SortedDictionary<float, Vector4> OpacityKeys = new SortedDictionary<float, Vector4>();

                    foreach (RseParticleFrame Frame in Particle.ListParticleFrame)
                    {
                        float Time = Frame.Frame / FramesPerSecond;
                        if (!float.IsFinite(Time) || PositionKeys.ContainsKey(Time))
                        {
                            throw new InvalidDataException($"{ScenePath}: particle frame times cannot be represented distinctly.");
                        }

                        PositionKeys.Add(Time, ReflectPosition(Frame.Position));
                        ScaleKeys.Add(Time, Frame.Scale);
                        bool Active = Frame.Age >= 0 && Frame.Lifetime > 0;
                        if (Generator.Spin == 3 && Active
                            || !BakedRotation && Generator.Spin != 0 && Frame.Frame != 0 && Active)
                        {
                            Angles += Velocity;
                        }

                        if (Generator.Spin == 2 || Generator.Spin == 3 && Active)
                        {
                            PoseAngles = Angles + Frame.Rotation;
                        }
                        else if (!BakedRotation)
                        {
                            PoseAngles = Angles;
                        }

                        Matrix4x4 RotationMatrix;
                        if (BakedRotation)
                        {
                            RotationMatrix = Matrix4x4.CreateRotationZ(PoseAngles.Z) * Matrix4x4.CreateRotationX(PoseAngles.X) * Matrix4x4.CreateRotationY(PoseAngles.Y);
                        }
                        else
                        {
                            RotationMatrix = Matrix4x4.CreateRotationY(PoseAngles.Y) * Matrix4x4.CreateRotationX(PoseAngles.X) * Matrix4x4.CreateRotationZ(PoseAngles.Z);
                        }
                        Quaternion Rotation = Quaternion.CreateFromRotationMatrix(RotationMatrix);
                        RotationKeys.Add(Time, new Quaternion(-Rotation.X, -Rotation.Y, Rotation.Z, Rotation.W));
                        OpacityKeys.Add(Time, new Vector4(1, 1, 1, EvaluateParticleOpacity(Generator, Frame)));
                    }
                    if (Duration <= PositionKeys.Last().Key)
                    {
                        throw new InvalidDataException($"{ScenePath}: the particle's final frame and scene end cannot be represented distinctly.");
                    }

                    PositionKeys.Add(Duration, PositionKeys.Last().Value);
                    ScaleKeys.Add(Duration, ScaleKeys.Last().Value);
                    RotationKeys.Add(Duration, RotationKeys.Last().Value);
                    OpacityKeys.Add(Duration, OpacityKeys.Last().Value);
                    Node Root = Model.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Particle.Id}");
                    bool Billboard = Generator.Spin == 4 || Generator.Spin == 5;
                    if (Billboard)
                    {
                        if (Generator.Spin == 4)
                        {
                            Root.Extras["rseBillboard"] = "spherical";
                        }
                        else
                        {
                            Root.Extras["rseBillboard"] = "verticalProjected";
                        }
                    }

                    Quaternion RootRotation = RotationKeys[0];
                    if (Billboard)
                    {
                        RootRotation = Quaternion.Identity;
                    }

                    Root.LocalTransform = new SharpGLTF.Transforms.AffineTransform(ScaleKeys[0], RootRotation, PositionKeys[0]);
                    Timeline.CreateTranslationChannel(Root, PositionKeys, linear: false);
                    Timeline.CreateScaleChannel(Root, ScaleKeys, linear: false);
                    if (Billboard)
                    {
                        Node Spin = Model.LogicalNodes.Single(Node => Node.Name == $"rse_spin_{Particle.Id}");
                        Spin.LocalTransform = new SharpGLTF.Transforms.AffineTransform(Vector3.One, RotationKeys[0], Vector3.Zero);
                        Timeline.CreateRotationChannel(Spin, RotationKeys, linear: false);
                    }
                    else if (Generator.Spin == 1 || BakedRotation)
                    {
                        Timeline.CreateRotationChannel(Root, RotationKeys, linear: false);
                    }
                    CuldceptModel ParticlePackage = GeneratorPackages[Generator.ObjectId];
                    if (ParticlePackage.Animations.Count > 0)
                    {
                        AddParticleMotionTracks(Model, Timeline, ParticlePackage, Particle, Scene.TotalFrames, FramesPerSecond,
                            MotionPeriods[ParticlePackage]);
                        AddVertexMotionTracks(Model, Timeline, ParticlePackage, Particle, Prefix(Particle.Id),
                            Scene.TotalFrames, FramesPerSecond, MotionPeriods[ParticlePackage]);
                    }
                    AddTextureTracks(Model, ParticlePackage, TextureLists[ParticlePackage], Prefix(Particle.Id),
                        Scene.TotalFrames, FramesPerSecond, Particle);
                    for (int Slot = 0; Slot < ParticlePackage.Document.ListMaterial.Count; Slot++)
                    {
                        RsmMaterial SourceMaterial = ParticlePackage.Document.ListMaterial[Slot];
                        Material Material = Model.LogicalMaterials.Single(Material =>
                            Material.Name == MaterialName(ParticlePackage, Slot, Prefix(Particle.Id)));
                        Material.Alpha = AlphaMode.BLEND;
                        Timeline.DangerousCreatePointerChannel(
                            $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor", OpacityKeys, linear: false);
                        MaterialChannel? BaseColor = Material.FindChannel("BaseColor");
                        if (SourceMaterial.UvScroll != Vector2.Zero && BaseColor.HasValue && BaseColor.Value.Texture != null)
                        {
                            SortedDictionary<float, Vector2> OffsetKeys = new SortedDictionary<float, Vector2>();
                            foreach (RseParticleFrame Frame in Particle.ListParticleFrame)
                            {
                                OffsetKeys.Add(Frame.Frame / FramesPerSecond, SourceMaterial.UvScroll * Frame.Age);
                            }

                            OffsetKeys.Add(Duration, OffsetKeys.Last().Value);
                            AddUvTrack(Timeline, Material, OffsetKeys, false);
                        }
                    }
                }
            }
            if (Timeline.Channels.Count == 0)
            {
                Node Clock = Model.DefaultScene.CreateNode("rse_clock");
                Timeline.CreateScaleChannel(Clock, new SortedDictionary<float, Vector3>
                {
                    [0] = Vector3.One,
                    [Duration] = Vector3.One
                });
            }
            JsonNode SceneNumber = null;
            if (Scene.SceneNumber.HasValue)
            {
                SceneNumber = JsonValue.Create(Scene.SceneNumber.Value);
            }

            JsonNode LoopValue = null;
            if (Scene.Loop.HasValue)
            {
                LoopValue = JsonValue.Create(Scene.Loop.Value);
            }

            Model.Extras = new JsonObject
            {
                ["rse"] = new JsonObject
                {
                    ["version"] = 1,
                    ["source"] = Path.GetFileName(ScenePath),
                    ["animationIndex"] = Timeline.LogicalIndex,
                    ["totalFrames"] = Scene.TotalFrames,
                    ["sceneNumber"] = SceneNumber,
                    ["framesPerSecond"] = FramesPerSecond,
                    ["timingAssumed"] = true,
                    ["loop"] = LoopValue,
                    ["previewLoops"] = true,
                    ["empty"] = Scene.IsEmpty,
                    ["renderless"] = !Scene.IsEmpty && (DicPackage.Count == 0 && GeneratorPackages.Count == 0 || !Scene.HasRenderableObjects),
                    ["particleCount"] = Scene.DicObject.Values.Count(Object => Object.Kind == RseObjectKind.Particle),
                    ["dependencies"] = Dependencies.Found,
                    ["absentDependencies"] = Dependencies.Absent,
                    ["nestedEffects"] = NestedEffects,
                    ["rawOpacityCommands"] = new JsonArray(Scene.ListRawOpacityCommand
                        .Select(Command => (JsonNode)new JsonObject
                        {
                            ["objectId"] = Command.ObjectId,
                            ["frame"] = Command.Frame,
                            ["command"] = Command.Command,
                            ["value"] = Command.Value
                        }).ToArray()),
                    ["nativeMotionTransitions"] = new JsonArray(Scene.ListNativeMotionTransition
                        .Select(Command => (JsonNode)new JsonObject
                        {
                            ["objectId"] = Command.ObjectId,
                            ["frame"] = Command.Frame,
                            ["value"] = Command.Value,
                            ["transition"] = Command.Transition
                        }).ToArray()),
                    ["rejectedMotionCommands"] = new JsonArray(Scene.ListRejectedMotionCommand
                        .Select(Command => (JsonNode)new JsonObject
                        {
                            ["frame"] = Command.Frame,
                            ["value"] = Command.Value,
                            ["reason"] = Command.Reason
                        }).ToArray()),
                    ["unmatchedSetupCommands"] = new JsonArray(Scene.ListUnmatchedSetupCommand
                        .Select(Command => (JsonNode)new JsonObject
                        {
                            ["objectId"] = Command.ObjectId,
                            ["frame"] = Command.Frame,
                            ["command"] = Command.Command,
                            ["value"] = Command.Value
                        }).ToArray()),
                    ["ignoredModelDeclarations"] = new JsonArray(Scene.DicIgnoredModelDeclaration
                        .Select(Declaration => (JsonNode)new JsonObject
                        {
                            ["objectId"] = Declaration.Key,
                            ["value"] = Declaration.Value,
                            ["reason"] = "Native object parser does not construct .RAM declarations"
                        }).ToArray()),
                    ["unboundParticleGenerators"] = new JsonArray(Scene.ListParticleGenerator
                        .Where(Generator => !Generator.HasBoundModel)
                        .Select(Generator =>
                        {
                            string Reason = "No OBJ reference";
                            if (Generator.IsModelObjectMissing)
                            {
                                if (Scene.DicIgnoredModelDeclaration.ContainsKey(Generator.ModelObjectId))
                                {
                                    Reason = "Unconstructed model declaration";
                                }
                                else
                                {
                                    Reason = "Undeclared model object";
                                }
                            }

                            return (JsonNode)new JsonObject
                            {
                                ["generatorId"] = Generator.ObjectId,
                                ["firstParticle"] = Generator.FirstParticle,
                                ["lastParticle"] = Generator.LastParticle,
                                ["modelObjectId"] = Generator.ModelObjectId,
                                ["reason"] = Reason
                            };
                        }).ToArray()),
                    ["ignoredUvAnimationDummies"] = new JsonArray(Scene.DicObject.Values
                        .Where(Object => Object.Kind == RseObjectKind.UvAnimationDummy)
                        .Select(Object => (JsonNode)JsonValue.Create(Object.Id)).ToArray()),
                    ["ignoredDummyCommands"] = new JsonArray(Scene.ListIgnoredDummyCommand
                        .Select(Command => (JsonNode)new JsonObject
                        {
                            ["objectId"] = Command.ObjectId,
                            ["frame"] = Command.Frame,
                            ["command"] = Command.Command,
                            ["value"] = Command.Value
                        }).ToArray()),
                    ["ignoredGeneratorMotions"] = new JsonArray(Scene.DicObject.Values
                        .Where(Object => Object.Kind == RseObjectKind.ParticleGenerator && Object.MotionId.HasValue)
                        .Select(Object => (JsonNode)new JsonObject
                        {
                            ["generatorId"] = Object.Id,
                            ["motionId"] = Object.MotionId.Value,
                            ["file"] = Scene.DicMotion[Object.MotionId.Value],
                            ["startFrame"] = Object.MotionStartFrame,
                            ["loop"] = Object.MotionLoops
                        }).ToArray()),
                    ["ignoredParticleCommands"] = new JsonArray(Scene.ListParticleGenerator.Where(Generator => Generator.IgnoredRsmStart.HasValue)
                        .Select(Generator => (JsonNode)new JsonObject
                        {
                            ["generatorId"] = Generator.ObjectId,
                            ["command"] = "RSMSTART",
                            ["value"] = Generator.IgnoredRsmStart.Value
                        }).ToArray()),
                    ["inlineParticleModels"] = new JsonArray(Scene.ListParticleGenerator.Where(Generator => Generator.ModelFileName != null)
                        .Select(Generator => (JsonNode)new JsonObject { ["generatorId"] = Generator.ObjectId, ["file"] = Generator.ModelFileName }).ToArray())
                }
            };
            return Model;
        }

        private static RsaClip LoadMotion(string Path, CuldceptModel Package, int TotalFrames, string TimelineName,
            bool Loop, out int Period)
        {
            if (!File.Exists(Path))
            {
                throw new FileNotFoundException("The RSE's declared motion is missing.", Path);
            }

            byte[] Bytes = File.ReadAllBytes(Path);
            if (Bytes.Length < 24)
            {
                throw new InvalidDataException($"{Path}: truncated RSA header.");
            }

            RsaBytes Source = new RsaBytes(Bytes, RSAReader.IsLittleEndian(Bytes));
            int NodeCount = Source.ReadUInt16(8);
            int TableEnd = 24 + NodeCount * 8;
            if (NodeCount == 0 || TableEnd > Bytes.Length)
            {
                throw new InvalidDataException($"{Path}: truncated or empty RSA channel table.");
            }

            for (int N = 0; N < NodeCount; N++)
            {
                int Entry = 24 + N * 8;
                ushort Kind = Source.ReadUInt16(Entry);
                ushort Count = Source.ReadUInt16(Entry + 2);
                uint Offset = Source.ReadUInt32(Entry + 4);
                if (Count == 0 || Kind != (ushort)RsaChannelKind.TransformKeys && Kind != (ushort)RsaChannelKind.VertexFrames)
                {
                    continue;
                }

                if (Offset < TableEnd || (ulong)Offset + 8 > (ulong)Bytes.Length)
                {
                    throw new InvalidDataException($"{Path}: invalid RSA payload offset in channel {N}.");
                }

                int Size;
                if (Kind == (ushort)RsaChannelKind.TransformKeys)
                {
                    Size = 48;
                }
                else
                {
                    Size = 8 + 12 * (Source.ReadUInt16((int)Offset + 4) + Source.ReadUInt16((int)Offset + 6));
                }
                if ((ulong)Offset + (ulong)Count * (uint)Size > (ulong)Bytes.Length)
                {
                    throw new InvalidDataException($"{Path}: truncated RSA payload in channel {N}.");
                }
            }

            RsaClip Clip = RSAReader.Parse(Bytes, TimelineName);
            if (Clip == null || Clip.NodeCount != Package.Document.ListNode.Count)
            {
                throw new InvalidDataException($"{Path}: invalid RSA or motion node count does not match the declared RSM.");
            }

            float Start = Source.ReadSingle(12);
            float End = Source.ReadSingle(16);
            float Step = Source.ReadSingle(20);
            if (Start != 0 || !float.IsFinite(End) || End <= 0 || Step != 1)
            {
                throw new NotSupportedException($"{Path}: RSE motion currently requires a zero-start RSA with unit frame steps.");
            }

            if (Loop && (End != MathF.Truncate(End) || End >= int.MaxValue))
            {
                throw new NotSupportedException($"{Path}: looping RSE motion requires a positive integral RSA end frame.");
            }

            Period = 0;
            if (Loop)
            {
                Period = checked((int)End);
            }
            bool HasKeys = false;

            for (int N = 0; N < Clip.NodeCount; N++)
            {
                int Entry = 24 + N * 8;
                ushort Kind = Source.ReadUInt16(Entry);
                ushort Count = Source.ReadUInt16(Entry + 2);
                uint Offset = Source.ReadUInt32(Entry + 4);

                RsaChannel Channel = Clip.ListChannel[N];

                if (Kind == 0 && Count == 0)
                {
                    continue;
                }

                if (Kind == (ushort)RsaChannelKind.VertexFrames)
                {
                    if (Count == 0 || (ulong)Offset + 8 > (ulong)Bytes.Length || Channel.ListVertexFrame.Count != Count)
                    {
                        throw new InvalidDataException($"{Path}: truncated or empty RSA vertex channel {N}.");
                    }

                    if (!Package.Document.DicMeshByNodeIndex.TryGetValue(N, out RsmMesh Mesh)
                        || Mesh.ListMeshGroup.Count == 0 || Mesh.IsSkinned)
                    {
                        throw new NotSupportedException($"{Path}: RSA vertex channels require a nonempty rigid mesh at node {N}.");
                    }

                    int FrameSize = 8 + 12 * (Channel.VertexCount + Channel.NormalCount);
                    if ((ulong)Offset + (ulong)Count * (uint)FrameSize > (ulong)Bytes.Length
                        || Channel.VertexCount != Mesh.ListPosition.Count
                        || Channel.NormalCount != 0 && Channel.NormalCount != Mesh.ListNormal.Count)
                    {
                        throw new InvalidDataException($"{Path}: RSA vertex/normal counts do not match mesh node {N}.");
                    }

                    float PreviousVertexFrame = -1;
                    for (int Key = 0; Key < Count; Key++)
                    {
                        RsaVertexFrame VertexFrame = Channel.ListVertexFrame[Key];
                        int FrameOffset = checked((int)Offset + Key * FrameSize);

                        if (!float.IsFinite(VertexFrame.Frame) || VertexFrame.Frame < 0 || VertexFrame.Frame <= PreviousVertexFrame
                            || VertexFrame.Frame >= End || VertexFrame.Frame != MathF.Truncate(VertexFrame.Frame)
                            || VertexFrame.ArrayPosition.Length != Channel.VertexCount || VertexFrame.ArrayNormal.Length != Channel.NormalCount
                            || !VertexFrame.ArrayPosition.All(Finite) || !VertexFrame.ArrayNormal.All(Finite)
                            || Source.ReadUInt16(FrameOffset + 4) != Channel.VertexCount
                            || Source.ReadUInt16(FrameOffset + 6) != Channel.NormalCount)
                        {
                            throw new InvalidDataException($"{Path}: invalid RSA vertex frame in channel {N}.");
                        }

                        PreviousVertexFrame = VertexFrame.Frame;
                    }
                    if (Channel.ListVertexFrame[0].Frame != 0)
                    {
                        throw new NotSupportedException($"{Path}: RSE vertex motion requires a frame-zero RSA key.");
                    }

                    HasKeys = true;
                    continue;
                }

                if (Kind != (ushort)RsaChannelKind.TransformKeys)
                {
                    throw new NotSupportedException($"{Path}: unsupported RSA channel kind {Kind}.");
                }

                if (Count == 0 || (ulong)Offset + (ulong)Count * 48 > (ulong)Bytes.Length
                    || Channel.ListKeyframe.Count != Count)
                {
                    throw new InvalidDataException($"{Path}: truncated or empty RSA transform channel {N}.");
                }

                float PreviousFrame = -1;
                foreach (RsaTransformKeyframe Key in Channel.ListKeyframe)
                {
                    if (!float.IsFinite(Key.Frame) || Key.Frame < 0 || Key.Frame <= PreviousFrame
                        || Key.Frame >= End || !Finite(Key.Translation) || !Finite(Key.Scale)
                        || !float.IsFinite(Key.Rotation.LengthSquared()) || Key.Rotation.LengthSquared() < 1e-12f)
                    {
                        throw new InvalidDataException($"{Path}: invalid RSA transform key in channel {N}.");
                    }

                    PreviousFrame = Key.Frame;
                }
                if (Channel.ListKeyframe[0].Frame != 0)
                {
                    throw new NotSupportedException($"{Path}: RSE motion requires frame-zero RSA keys.");
                }

                RsaTransformKeyframe Last = Channel.ListKeyframe.Last();
                Channel.ListKeyframe.Add(new RsaTransformKeyframe
                {
                    Frame = Math.Max(TotalFrames, PreviousFrame + 1),
                    Translation = Last.Translation,
                    Rotation = Last.Rotation,
                    Scale = Last.Scale
                });
                HasKeys = true;
            }

            if (!HasKeys && Enumerable.Range(0, Clip.NodeCount)
                .Any(Index => Source.ReadUInt16(24 + Index * 8) != 0 || Source.ReadUInt16(26 + Index * 8) != 0))
            {
                throw new InvalidDataException($"{Path}: RSA contains no supported animation channels.");
            }

            return Clip;
        }

        private static bool Finite(Vector3 Value)
        {
            return float.IsFinite(Value.X) && float.IsFinite(Value.Y) && float.IsFinite(Value.Z);
        }

        private static void AddModelMotionTracks(ModelRoot Model, Animation Timeline, CuldceptModel Package,
            RseObject Object, string NamePrefix, int TotalFrames, float FramesPerSecond, int Period, int PlaybackFrames)
        {
            RsaClip Clip = Package.Animations.Single();
            for (int Index = 0; Index < Clip.ListChannel.Count; Index++)
            {
                if (Clip.ListChannel[Index].ListKeyframe.Count == 0)
                {
                    continue;
                }

                Node Node = Model.LogicalNodes.Single(Node => Node.Name == $"{NamePrefix}node_{Index}");
                SortedDictionary<float, Vector3> Positions = new SortedDictionary<float, Vector3>();
                SortedDictionary<float, Quaternion> Rotations = new SortedDictionary<float, Quaternion>();
                SortedDictionary<float, Vector3> Scales = new SortedDictionary<float, Vector3>();
                List<RsaTransformKeyframe> Keys = Clip.ListChannel[Index].ListKeyframe;

                bool Linear = PlaybackFrames == TotalFrames && Object.MotionStartFrame == 0 && Period == 0 && Keys.Last().Frame <= TotalFrames;

                if (Linear)
                {
                    foreach (RsaTransformKeyframe Key in Keys)
                    {
                        AddMotionSample(Positions, Rotations, Scales, FramesPerSecond, Key.Frame, SampleMotion(Keys, Key.Frame));
                    }
                }
                else
                {
                    for (int Frame = 0; Frame <= TotalFrames; Frame++)
                    {
                        int Relative = Math.Min(Frame, PlaybackFrames - 1) - Object.MotionStartFrame;
                        if (Period > 0)
                        {
                            Relative %= Period;
                        }

                        if (Frame < Object.MotionStartFrame)
                        {
                            AddMotionSample(Positions, Rotations, Scales, FramesPerSecond, Frame, Node.LocalTransform);
                        }
                        else
                        {
                            AddMotionSample(Positions, Rotations, Scales, FramesPerSecond, Frame, SampleMotion(Keys, Relative));
                        }
                    }
                }

                Timeline.CreateTranslationChannel(Node, Positions, linear: Linear);
                Timeline.CreateRotationChannel(Node, Rotations, linear: Linear);
                Timeline.CreateScaleChannel(Node, Scales, linear: Linear);
            }
        }

        private static SharpGLTF.Transforms.AffineTransform SampleMotion(IReadOnlyList<RsaTransformKeyframe> Keys, float Frame)
        {
            int Low = 0, High = Keys.Count - 1;
            while (Low + 1 < High)
            {
                int Middle = Low + (High - Low) / 2;
                if (Keys[Middle].Frame <= Frame)
                {
                    Low = Middle;
                }
                else
                {
                    High = Middle;
                }
            }
            RsaTransformKeyframe First = Keys[Low], Last = Keys[High];
            float Fraction = 0;
            if (Last.Frame > First.Frame)
            {
                Fraction = Math.Clamp((Frame - First.Frame) / (Last.Frame - First.Frame), 0, 1);
            }
            return new SharpGLTF.Transforms.AffineTransform(Vector3.Lerp(First.Scale, Last.Scale, Fraction),
                ReflectRotation(Quaternion.Slerp(Quaternion.Normalize(First.Rotation), Quaternion.Normalize(Last.Rotation), Fraction)),
                ReflectPosition(Vector3.Lerp(First.Translation, Last.Translation, Fraction)));
        }

        private static void AddParticleMotionTracks(ModelRoot Model, Animation Timeline, CuldceptModel Package,
            RseObject Particle, int TotalFrames, float FramesPerSecond, int Period)
        {
            RsaClip Clip = Package.Animations.Single();
            for (int Index = 0; Index < Clip.ListChannel.Count; Index++)
            {
                if (Clip.ListChannel[Index].ListKeyframe.Count == 0)
                {
                    continue;
                }

                Node Node = Model.LogicalNodes.Single(Node => Node.Name == $"{Prefix(Particle.Id)}node_{Index}");
                SortedDictionary<float, Vector3> Positions = new SortedDictionary<float, Vector3>();
                SortedDictionary<float, Quaternion> Rotations = new SortedDictionary<float, Quaternion>();
                SortedDictionary<float, Vector3> Scales = new SortedDictionary<float, Vector3>();
                int? BirthFrame = null;
                for (int Frame = 0; Frame < TotalFrames; Frame++)
                {
                    if (IsParticleBirth(Particle, Frame))
                    {
                        BirthFrame = Frame;
                    }

                    int Relative = Frame - BirthFrame.GetValueOrDefault();
                    if (Period > 0)
                    {
                        Relative %= Period;
                    }

                    SharpGLTF.Transforms.AffineTransform Pose = Node.LocalTransform;
                    if (BirthFrame.HasValue)
                    {
                        Pose = SampleMotion(Clip.ListChannel[Index].ListKeyframe, Relative);
                    }
                    float Time = Frame / FramesPerSecond;
                    if (!Positions.TryAdd(Time, Pose.Translation))
                    {
                        throw new InvalidDataException("Particle motion frame times cannot be represented distinctly.");
                    }

                    Rotations.Add(Time, Pose.Rotation);
                    Scales.Add(Time, Pose.Scale);
                }
                float Duration = TotalFrames / FramesPerSecond;
                Positions.Add(Duration, Positions.Last().Value);
                Rotations.Add(Duration, Rotations.Last().Value);
                Scales.Add(Duration, Scales.Last().Value);
                Timeline.CreateTranslationChannel(Node, Positions, linear: false);
                Timeline.CreateRotationChannel(Node, Rotations, linear: false);
                Timeline.CreateScaleChannel(Node, Scales, linear: false);
            }
        }

        private static bool IsParticleBirth(RseObject Particle, int Frame)
        {
            if (Frame >= Particle.ListParticleFrame.Count)
            {
                return false;
            }

            RseParticleFrame Current = Particle.ListParticleFrame[Frame];
            return Current.Age >= 0 && Current.Age < Current.Lifetime && (Frame == 0
                || Particle.ListParticleFrame[Frame - 1].Age == 0 && Particle.ListParticleFrame[Frame - 1].Lifetime == 0);
        }

        private static void AddVertexMotionTracks(ModelRoot Model, Animation Timeline, CuldceptModel Package,
            RseObject Object, string NamePrefix, int TotalFrames, float FramesPerSecond, int Period, int PlaybackFrames = 0)
        {
            if (PlaybackFrames == 0)
            {
                PlaybackFrames = TotalFrames;
            }

            RsaClip Clip = Package.Animations.Single();
            for (int Index = 0; Index < Clip.ListChannel.Count; Index++)
            {
                RsaChannel Channel = Clip.ListChannel[Index];
                if (Channel.Kind != RsaChannelKind.VertexFrames)
                {
                    continue;
                }

                Node Node = Model.LogicalNodes.SingleOrDefault(Candidate =>
                {
                    if (Candidate.Mesh == null)
                    {
                        return false;
                    }

                    return Candidate.Mesh.Name == $"{NamePrefix}mesh_{Index}";
                });
                if (Node == null)
                {
                    throw new InvalidDataException($"RSE vertex mesh {NamePrefix}mesh_{Index} was not preserved.");
                }

                int SourceCorners = Package.Document.DicMeshByNodeIndex[Index].ListMeshGroup.Sum(Group => Group.ArrayCorner.Length);
                int PreservedCorners = 0;
                foreach (MeshPrimitive Primitive in Node.Mesh.Primitives)
                {
                    Accessor IndexAccessor = Primitive.GetIndexAccessor();
                    if (IndexAccessor != null)
                    {
                        PreservedCorners += IndexAccessor.Count;
                    }
                }

                if (PreservedCorners != SourceCorners)
                {
                    throw new InvalidDataException($"RSE vertex mesh {NamePrefix}mesh_{Index} did not preserve every source corner.");
                }

                int Count = Channel.ListVertexFrame.Count;
                if (Node.Mesh.Primitives.Any(Primitive => Primitive.MorphTargetsCount != Count))
                {
                    throw new InvalidDataException($"RSE vertex targets were not preserved for node {Index}.");
                }

                SortedDictionary<float, float[]> Keys = new SortedDictionary<float, float[]>();
                int? BirthFrame = null;
                for (int Frame = 0; Frame < TotalFrames; Frame++)
                {
                    int? Relative = null;
                    if (Object.Kind == RseObjectKind.Particle)
                    {
                        if (IsParticleBirth(Object, Frame))
                        {
                            BirthFrame = Frame;
                        }

                        if (BirthFrame.HasValue)
                        {
                            Relative = Frame - BirthFrame.Value;
                        }
                    }
                    else if (Frame >= Object.MotionStartFrame)
                    {
                        Relative = Math.Min(Frame, PlaybackFrames - 1) - Object.MotionStartFrame;
                    }

                    float[] Weights = new float[Count];
                    if (Relative.HasValue)
                    {
                        if (Period > 0)
                        {
                            Relative %= Period;
                        }
                        // Native vertex lookup selects the first record at or after integer clip time.
                        int Target = Channel.ListVertexFrame.FindIndex(Key => Key.Frame >= Relative.Value);
                        if (Target < 0)
                        {
                            Target = Count - 1;
                        }

                        Weights[Target] = 1;
                    }
                    if (!Keys.TryAdd(Frame / FramesPerSecond, Weights))
                    {
                        throw new InvalidDataException("RSE vertex frame times cannot be represented distinctly.");
                    }
                }
                Keys.Add(TotalFrames / FramesPerSecond, Keys.Last().Value);
                Timeline.CreateMorphChannel(Node, Keys, Count, linear: false);
            }
        }

        private static void AddOpacityTracks(ModelRoot Model, Animation Timeline, CuldceptModel Package,
            RseObject Object, string NamePrefix, string ScenePath, float FramesPerSecond, float Duration, int TotalFrames)
        {
            foreach (int Slot in Object.DicMaterialOpacity.Keys.Concat(Object.DicSetupOpacity.Keys).Concat(Object.DicMaterialUv.Keys)
                .Where(Slot => Slot >= Package.Document.ListMaterial.Count))
            {
                throw new InvalidDataException($"{ScenePath}: object {Object.Id} references missing material slot {Slot}.");
            }

            for (int MaterialIndex = 0; MaterialIndex < Package.Document.ListMaterial.Count; MaterialIndex++)
            {
                RsmMaterial SourceMaterial = Package.Document.ListMaterial[MaterialIndex];
                Material[] ArrayMaterial = Model.LogicalMaterials.Where(Material =>
                    Material.Name == MaterialName(Package, MaterialIndex, NamePrefix)).ToArray();
                if (ArrayMaterial.Length != 1)
                {
                    throw new NotSupportedException($"{ScenePath}: material slot {MaterialIndex} cannot be mapped uniquely to glTF.");
                }
                Material TargetMaterial = ArrayMaterial[0];
                TargetMaterial.Alpha = AlphaMode.BLEND;
                float InitialOpacity = SourceMaterial.Opacity;
                if (Object.DicSetupOpacity.TryGetValue(MaterialIndex, out float SlotOpacity))
                {
                    InitialOpacity = SlotOpacity;
                }
                else if (Object.DicObjectOpacity.Count == 0 && Object.DicSetupOpacity.TryGetValue(-1, out float ObjectOpacity))
                {
                    InitialOpacity = ObjectOpacity;
                }
                if (!float.IsFinite(InitialOpacity))
                {
                    throw new InvalidDataException($"{ScenePath}: material {MaterialIndex} has invalid initial opacity.");
                }
                SortedDictionary<float, Vector4> DicKeys = new SortedDictionary<float, Vector4>
                {
                    [0] = new Vector4(1, 1, 1, InitialOpacity)
                };
                if (Object.DicMaterialOpacity.TryGetValue(MaterialIndex, out SortedDictionary<int, float> DicOpacityKeys))
                {
                    foreach (KeyValuePair<int, float> ActiveKey in DicOpacityKeys)
                    {
                        float KeyTime = ActiveKey.Key / FramesPerSecond;
                        if (!float.IsFinite(KeyTime) || (ActiveKey.Key != 0 && DicKeys.ContainsKey(KeyTime)))
                        {
                            throw new InvalidDataException($"{ScenePath}: opacity key times cannot be represented distinctly.");
                        }
                        DicKeys[KeyTime] = new Vector4(1, 1, 1, ActiveKey.Value);
                    }
                }
                if (Duration <= DicKeys.Last().Key)
                {
                    throw new InvalidDataException($"{ScenePath}: the final frame and scene end cannot be represented distinctly.");
                }
                DicKeys[Duration] = DicKeys.Last().Value;
                bool LinearOpacity = true;
                if (Object.DicObjectOpacity.Count > 0)
                {
                    if (Object.DicMaterialOpacity.ContainsKey(MaterialIndex))
                    {
                        // Products of two linear fades are sampled on the integer game timeline.
                        KeyValuePair<float, float>[] MaterialCurve = DicKeys.Select(Key => new KeyValuePair<float, float>(Key.Key, Key.Value.W)).ToArray();
                        KeyValuePair<float, float>[] ObjectCurve = Object.DicObjectOpacity.Select(Key =>
                            new KeyValuePair<float, float>(Key.Key / FramesPerSecond, Key.Value)).ToArray();
                        DicKeys.Clear();
                        for (int Frame = 0; Frame <= TotalFrames; Frame++)
                        {
                            float Time = Frame / FramesPerSecond;
                            float MaterialOpacity = SampleOpacity(MaterialCurve, Time);
                            float ObjectAlpha = SampleOpacity(ObjectCurve, Time);
                            if (!DicKeys.TryAdd(Time, new Vector4(1, 1, 1, MaterialOpacity * ObjectAlpha)))
                            {
                                throw new InvalidDataException($"{ScenePath}: combined opacity frame times cannot be represented distinctly.");
                            }
                        }
                        LinearOpacity = false;
                    }
                    else
                    {
                        DicKeys.Clear();
                        foreach (KeyValuePair<int, float> Key in Object.DicObjectOpacity)
                        {
                            if (!DicKeys.TryAdd(Key.Key / FramesPerSecond, new Vector4(1, 1, 1, InitialOpacity * Key.Value)))
                            {
                                throw new InvalidDataException($"{ScenePath}: object opacity key times cannot be represented distinctly.");
                            }
                        }
                        DicKeys[Duration] = DicKeys.Last().Value;
                    }
                }
                if (DicKeys.Values.Any(Value => !float.IsFinite(Value.W)))
                {
                    throw new InvalidDataException($"{ScenePath}: combined opacity is not finite.");
                }

                if (DicKeys.Values.Any(Value => Value.W < 0 || Value.W > 1))
                {
                    if (TargetMaterial.Extras == null)
                    {
                        TargetMaterial.Extras = new JsonObject();
                    }

                    string Interpolation = "STEP";
                    if (LinearOpacity)
                    {
                        Interpolation = "LINEAR";
                    }

                    TargetMaterial.Extras["rseRawOpacity"] = new JsonObject
                    {
                        ["version"] = 1,
                        ["animationIndex"] = Timeline.LogicalIndex,
                        ["interpolation"] = Interpolation,
                        ["times"] = new JsonArray(DicKeys.Keys.Select(Time => (JsonNode)JsonValue.Create(Time)).ToArray()),
                        ["values"] = new JsonArray(DicKeys.Values.Select(Value => (JsonNode)JsonValue.Create(Value.W)).ToArray()),
                        ["preview"] = "raw vertex/texture alpha product, then saturated final alpha; native blend state unverified",
                        ["fallback"] = "bounded baseColorFactor approximation; does not preserve raw vertex/texture alpha products"
                    };
                    // Insert saturation crossings so the fallback does not shorten opaque plateaus.
                    if (LinearOpacity)
                    {
                        KeyValuePair<float, Vector4>[] RawKeys = DicKeys.ToArray();
                        for (int Index = 1; Index < RawKeys.Length; Index++)
                        {
                            KeyValuePair<float, Vector4> First = RawKeys[Index - 1];
                            KeyValuePair<float, Vector4> Last = RawKeys[Index];
                            foreach (float Boundary in new[] { 0f, 1f })
                            {
                                float Fraction = (Boundary - First.Value.W) / (Last.Value.W - First.Value.W);
                                if (Fraction > 0 && Fraction < 1)
                                {
                                    float Time = First.Key + (Last.Key - First.Key) * Fraction;
                                    if (Time > First.Key && Time < Last.Key)
                                    {
                                        DicKeys.TryAdd(Time, new Vector4(1, 1, 1, Boundary));
                                    }
                                }
                            }
                        }
                    }
                    foreach (float Time in DicKeys.Keys.ToArray())
                    {
                        DicKeys[Time] = new Vector4(1, 1, 1, Math.Clamp(DicKeys[Time].W, 0, 1));
                    }
                }
                Timeline.DangerousCreatePointerChannel(
                    $"/materials/{TargetMaterial.LogicalIndex}/pbrMetallicRoughness/baseColorFactor", DicKeys, linear: LinearOpacity);
                if (Object.DicMaterialUv.TryGetValue(MaterialIndex, out SortedDictionary<int, Vector2> UvKeys))
                {
                    if (SourceMaterial.UvScroll != Vector2.Zero)
                    {
                        throw new NotSupportedException($"{ScenePath}: UVANIM combined with source UV scrolling is not yet supported.");
                    }

                    MaterialChannel? BaseColor = TargetMaterial.FindChannel("BaseColor");
                    if (!BaseColor.HasValue || BaseColor.Value.Texture == null)
                    {
                        throw new InvalidDataException($"{ScenePath}: UVANIM references untextured material slot {MaterialIndex}.");
                    }

                    SortedDictionary<float, Vector2> OffsetKeys = new SortedDictionary<float, Vector2>();
                    if (UvKeys.First().Key != 0)
                    {
                        KeyValuePair<int, Vector2> First = UvKeys.First();
                        KeyValuePair<int, Vector2> Next = UvKeys.Skip(1).FirstOrDefault();
                        // The native cursor stays on the first segment even before its first key.
                        Vector2 Initial = First.Value;
                        if (UvKeys.Count != 1)
                        {
                            Initial = First.Value + (Next.Value - First.Value) * (-First.Key / (float)(Next.Key - First.Key));
                        }
                        OffsetKeys.Add(0, Initial);
                    }
                    foreach (KeyValuePair<int, Vector2> Key in UvKeys)
                    {
                        if (!OffsetKeys.TryAdd(Key.Key / FramesPerSecond, Key.Value))
                        {
                            throw new InvalidDataException($"{ScenePath}: UVANIM key times cannot be represented distinctly.");
                        }
                    }
                    if (Duration <= OffsetKeys.Last().Key)
                    {
                        throw new InvalidDataException($"{ScenePath}: the final UVANIM key and scene end cannot be represented distinctly.");
                    }

                    OffsetKeys.Add(Duration, OffsetKeys.Last().Value);
                    AddUvTrack(Timeline, TargetMaterial, OffsetKeys, true);
                }
                else if (SourceMaterial.UvScroll != Vector2.Zero)
                {
                    MaterialChannel? ScrollChannel = TargetMaterial.FindChannel("BaseColor");
                    if (ScrollChannel.HasValue && ScrollChannel.Value.Texture != null)
                    {
                        AddUvTrack(Timeline, TargetMaterial, new SortedDictionary<float, Vector2>
                        {
                            [0] = Vector2.Zero,
                            [Duration] = SourceMaterial.UvScroll * (Duration * FramesPerSecond)
                        }, true);
                    }
                }
            }
        }

        private static Dictionary<string, int[]> LoadTextureLists(CuldceptModel Package, string TexturePath)
        {
            Dictionary<string, int[]> Lists = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            if (!Package.TextureMap.Keys.Any(Name => Name.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase)))
            {
                return Lists;
            }

            byte[] Bytes = File.ReadAllBytes(TexturePath);
            List<RstRecord> Records = RstReader.ReadRst(Bytes, out byte[] Footer);
            bool BigEndian = Bytes[0] == 'r';
            int GroupCount = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(10, 2));
            if (BigEndian)
            {
                GroupCount = BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(10, 2));
            }
            Dictionary<(uint Hash, uint Keys), (List<byte[]> Images, int[] Ends)> Groups = new Dictionary<(uint Hash, uint Keys), (List<byte[]> Images, int[] Ends)>();
            Dictionary<int, byte[]> Images = new Dictionary<int, byte[]>();
            int Position = 0;
            for (int Group = 0; Group < GroupCount; Group++)
            {
                if (Footer.Length - Position < 12)
                {
                    throw new InvalidDataException($"{TexturePath}: truncated texture playlist header.");
                }

                (uint, uint) Identity = (ReadTextureWord(Footer, Position, BigEndian), ReadTextureKeys(Footer, Position + 4, BigEndian));
                uint Count = ReadTextureWord(Footer, Position + 8, BigEndian);
                Position += 12;
                if (Count == 0 || Count > (Footer.Length - Position) / 12)
                {
                    throw new InvalidDataException($"{TexturePath}: truncated or empty texture playlist.");
                }

                List<byte[]> Frames = new List<byte[]>();
                int[] Ends = new int[Count];
                for (int Entry = 0; Entry < Ends.Length; Entry++, Position += 12)
                {
                    uint Hash = ReadTextureWord(Footer, Position, BigEndian), NameKeys = ReadTextureKeys(Footer, Position + 4, BigEndian);
                    uint End = ReadTextureWord(Footer, Position + 8, BigEndian);
                    (RstRecord Record, int Index)[] Matches = Records.Select((Record, Index) => (Record, Index)).Where(Item =>
                        Item.Record.HeaderWord == Hash && ((uint)Item.Record.NameKey << 16 | Item.Record.NameKey2) == NameKeys
                        && Item.Record.ArgbPixels != null).ToArray();
                    if (Matches.Length == 0 || Matches.Skip(1).Any(Item =>
                        Item.Record.Width != Matches[0].Record.Width || Item.Record.Height != Matches[0].Record.Height
                        || Item.Record.Flags != Matches[0].Record.Flags
                        || !Item.Record.ArgbPixels.SequenceEqual(Matches[0].Record.ArgbPixels))
                        || End > int.MaxValue || End == 0 || Entry > 0 && End <= Ends[Entry - 1])
                    {
                        throw new InvalidDataException($"{TexturePath}: texture playlist has an ambiguous/missing image or invalid frame boundary.");
                    }

                    int Index = Matches[0].Index;
                    if (!Images.TryGetValue(Index, out byte[] Image))
                    {
                        RstRecord Record = Matches[0].Record;
                        Image = RstReader.ConvertArgbToPng(Record.Width, Record.Height, Record.ArgbPixels);
                        if (Image == null)
                        {
                            throw new InvalidDataException($"{TexturePath}: texture playlist image could not be decoded.");
                        }

                        Images.Add(Index, Image);
                    }
                    Frames.Add(Image);
                    Ends[Entry] = (int)End;
                }
                if (!Groups.TryAdd(Identity, (Frames, Ends)))
                {
                    throw new InvalidDataException($"{TexturePath}: duplicate texture playlist identity.");
                }
            }
            if (Position != Footer.Length)
            {
                throw new InvalidDataException($"{TexturePath}: unexpected bytes after texture playlists.");
            }

            foreach (RsmMaterial Material in Package.Document.ListMaterial.Where(Material =>
                Package.TextureMap.ContainsKey(Material.Name.ToLowerInvariant()) && Material.Name.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase)))
            {
                (uint, uint) Identity = (RstReader.RstDimensionHash(Material.Name, Material.TextureWidth, Material.TextureHeight),
                    (uint)RstReader.RstNameKey(Material.Name) << 16 | RstReader.RstSecondNameKey(Material.Name));
                if (!Groups.TryGetValue(Identity, out (List<byte[]> Images, int[] Ends) List))
                {
                    throw new InvalidDataException($"{TexturePath}: missing texture playlist for {Material.Name}.");
                }

                Package.TextureMap[Material.Name.ToLowerInvariant()] = List.Images;
                Lists[Material.Name] = List.Ends;
            }
            return Lists;
        }

        private static void AddTextureTracks(ModelRoot Model, CuldceptModel Package, Dictionary<string, int[]> Lists,
            string NamePrefix, int TotalFrames, float FramesPerSecond, RseObject Particle = null)
        {
            for (int Slot = 0; Slot < Package.Document.ListMaterial.Count; Slot++)
            {
                if (!Lists.TryGetValue(Package.Document.ListMaterial[Slot].Name, out int[] Ends) || Ends.Length == 1)
                {
                    continue;
                }

                Material Material = Model.LogicalMaterials.Single(Material => Material.Name == MaterialName(Package, Slot, NamePrefix));
                JsonObject Ifl = null;
                if (Material.Extras != null)
                {
                    Ifl = Material.Extras["ifl"] as JsonObject;
                }

                JsonArray Frames = null;
                if (Ifl != null)
                {
                    Frames = Ifl["frames"] as JsonArray;
                }

                if (Ifl == null || Frames == null || Frames.Count != Ends.Length)
                {
                    throw new InvalidDataException($"RSE texture playlist for {Material.Name} was not preserved during export.");
                }

                JsonArray Indices = new JsonArray();
                int Clock = 0;
                int LastIndex = 0;
                for (int Frame = 0; Frame < TotalFrames; Frame++, Clock++)
                {
                    if (Particle != null && IsParticleBirth(Particle, Frame))
                    {
                        Clock = 0;
                    }

                    int Phase = Clock % Ends.Last();
                    int Index = Array.FindIndex(Ends, End => Phase < End);
                    Indices.Add(JsonValue.Create(Index));
                    LastIndex = Index;
                }
                Indices.Add(JsonValue.Create(LastIndex));
                Ifl["rseFrameIndices"] = Indices;
                Ifl["rseFramesPerSecond"] = FramesPerSecond;
            }
        }

        private static float SampleOpacity(IEnumerable<KeyValuePair<float, float>> Keys, float Time)
        {
            KeyValuePair<float, float> Previous = Keys.First();
            foreach (KeyValuePair<float, float> Next in Keys.Skip(1))
            {
                if (Time < Next.Key)
                {
                    return Previous.Value + (Next.Value - Previous.Value) * (Time - Previous.Key) / (Next.Key - Previous.Key);
                }

                Previous = Next;
            }
            return Previous.Value;
        }

        private static void AddUvTrack(Animation Timeline, Material Material, IReadOnlyDictionary<float, Vector2> Keys, bool Linear)
        {
            MaterialChannel Channel = Material.FindChannel("BaseColor").Value;
            if (Channel.TextureTransform == null)
            {
                Channel.SetTransform(Vector2.Zero, Vector2.One, 0, Channel.TextureCoordinate);
            }

            Timeline.DangerousCreatePointerChannel(
                $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset",
                Keys, linear: Linear);
        }

        private static bool HasDuplicateMaterials(CuldceptModel Package)
        {
            return Package.Document.ListMaterial.Select(Material => Material.Name).Distinct().Count() != Package.Document.ListMaterial.Count;
        }

        private static string MaterialName(CuldceptModel Package, int Slot, string NamePrefix)
        {
            return GltfExporter.RsmMaterialName(Package.Document.ListMaterial, Slot, NamePrefix, HasDuplicateMaterials(Package));
        }

        private static string Prefix(int Id)
        {
            return $"rse_{Id}_";
        }

        private static Vector3 ReflectPosition(Vector3 Position)
        {
            return new Vector3(Position.X, Position.Y, -Position.Z);
        }

        private static Quaternion ReflectRotation(Quaternion Rotation)
        {
            return new Quaternion(Rotation.X, Rotation.Y, -Rotation.Z, Rotation.W);
        }

        internal static float EvaluateParticleOpacity(RseParticleGenerator Generator, RseParticleFrame Frame)
        {
            if (Frame.Age < 0 || Frame.Lifetime <= 0)
            {
                return 0;
            }

            float FadeIn = Generator.FadeIn * Frame.Lifetime * 0.01f;
            float FadeOut = Generator.FadeOut * Frame.Lifetime * 0.01f;
            float Percent;
            if (Frame.Age <= FadeIn)
            {
                Percent = Generator.StartOpacity;
                if (FadeIn != 0)
                {
                    Percent = Generator.StartOpacity + (Generator.MiddleOpacity - Generator.StartOpacity) * (Frame.Age / FadeIn);
                }
            }
            else if (Frame.Age <= FadeOut)
            {
                Percent = Generator.MiddleOpacity;
            }
            else
            {
                if (Frame.Lifetime == FadeOut)
                {
                    Percent = Generator.EndOpacity;
                }
                else
                {
                    Percent = Generator.MiddleOpacity + (Generator.EndOpacity - Generator.MiddleOpacity) * ((Frame.Age - FadeOut) / (Frame.Lifetime - FadeOut));
                }
            }

            return Percent * 0.01f;
        }

        private static float CenteredRandom(float Range, ref uint State)
        {
            if (Range == 0)
            {
                return 0;
            }

            uint Value = (0x65ac9365u >> (int)(State & 3)) ^ State;
            State = ((Value >> 1 ^ Value) >> 3) ^ ((((Value & 0x7fffffffu) << 1 ^ Value) & 0x1fffffffu) << 3) ^ Value;
            uint Random = State;
            if (State == uint.MaxValue)
            {
                Random = 0;
            }
            float Magnitude = (float)((double)(Random % (uint)(Range * 100 * 2)) / 100);
            return (float)((double)Magnitude - Range);
        }

        private static CuldceptModel LoadPackage(RseScene Scene, DependencyLookup Dependencies,
            Dictionary<CuldceptModel, Dictionary<string, int[]>> TextureLists,
            string FileName, string SourceDirectory, int? ObjectId)
        {
            string ModelPath = Dependencies.TryResolve(FileName, "model", SourceDirectory, out string LocalPath, out string FallbackPath);
            if (ModelPath == null)
            {
                Dependencies.NoteAbsent(ObjectId, FileName, "model", LocalPath, FallbackPath);
                return null;
            }

            CuldceptModel Package = CuldceptModel.Load(ModelPath, WithAnimations: false, WithTextures: false);
            string TexturePath = null;
            if (Scene.AutoTexture && Package.Document.ListMaterial.Count > 0)
            {
                TexturePath = Dependencies.Resolve(Path.ChangeExtension(FileName, ".RST"), "texture",
                    "AUTORST=1 requires the model's same-name RST.", SourceDirectory);
                Package.TextureMap = CuldceptModel.LoadTextures(ModelPath, TexturePath, Package.Document.ListMaterial, RequireValidRst: true);
            }

            TextureLists.Add(Package, LoadTextureLists(Package, TexturePath));
            return Package;
        }

        private static void CompleteMaterials(ModelRoot Model, float FramesPerSecond, int Id, CuldceptModel Package)
        {
            GltfExporter.CompleteRsmMaterials(Model, Package.Document, Package.TextureMap, FramesPerSecond, Prefix(Id),
                CreateUvAnimation: false, PreserveMaterialSlots: HasDuplicateMaterials(Package), PreserveUnusedMaterials: true);
            GltfExporter.CompleteRsmVertexMeshes(Model, Package.Document, Package.Animations, true, Prefix(Id), HasDuplicateMaterials(Package));
        }

        private static void AddObject(SceneBuilder Builder, float FramesPerSecond, int Id, RseObject Object, CuldceptModel Package, bool Billboard)
        {
            NodeBuilder Root = new NodeBuilder($"rse_object_{Id}");
            Root.WithLocalTranslation(ReflectPosition(Object.Position))
                .WithLocalRotation(ReflectRotation(Object.Rotation))
                .WithLocalScale(Object.Scale);
            Root.Extras = new JsonObject { ["rseObjectId"] = Id };
            NodeBuilder GeometryParent = Root;
            if (Billboard)
            {
                // Sprite rotation precedes the billboard's scale and camera basis.
                GeometryParent = new NodeBuilder($"rse_spin_{Id}");
                Root.AddNode(GeometryParent);
            }

            GltfExporter.BuildRsmScene(Package.Document, Package.TextureMap, Package.Animations,
                FramesPerSecond, true, Builder, GeometryParent, Prefix(Id), PreserveMaterialSlots: HasDuplicateMaterials(Package),
                CreateAnimationChannels: false, PreserveVertexFrameTopology: true);
        }

        private static Vector3 ReadParticleAngles(RseParticleGenerator Generator, bool BakedRotation, Vector3 Base, Vector3 Range, ref uint RandomState)
        {
            if (Generator.Seed.HasValue)
            {
                return new Vector3(
                    Base.X + CenteredRandom(Range.X, ref RandomState),
                    Base.Y + CenteredRandom(Range.Y, ref RandomState),
                    Base.Z + CenteredRandom(Range.Z, ref RandomState));
            }

            if (BakedRotation)
            {
                return Base;
            }

            return Vector3.Zero;
        }

        private static void AddMotionSample(SortedDictionary<float, Vector3> Positions, SortedDictionary<float, Quaternion> Rotations,
            SortedDictionary<float, Vector3> Scales, float FramesPerSecond, float Frame, SharpGLTF.Transforms.AffineTransform Pose)
        {
            float Time = Frame / FramesPerSecond;
            if (!Positions.TryAdd(Time, Pose.Translation))
            {
                throw new InvalidDataException("RSE model motion frame times cannot be represented distinctly.");
            }

            Quaternion Rotation = Pose.Rotation;
            if (Rotations.Count > 0 && Quaternion.Dot(Rotation, Rotations.Last().Value) < 0)
            {
                Rotation = Quaternion.Negate(Rotation);
            }

            Rotations.Add(Time, Rotation);
            Scales.Add(Time, Pose.Scale);
        }

        private static uint ReadTextureWord(byte[] Data, int Offset, bool BigEndian)
        {
            if (BigEndian)
            {
                return BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(Offset, 4));
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(Offset, 4));
        }

        private static uint ReadTextureKeys(byte[] Data, int Offset, bool BigEndian)
        {
            if (BigEndian)
            {
                return ReadTextureWord(Data, Offset, true);
            }

            return (uint)BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(Offset, 2)) << 16
                | BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(Offset + 2, 2));
        }

        private readonly struct RsaBytes
        {
            public readonly byte[] Bytes;
            public readonly bool LittleEndian;

            public RsaBytes(byte[] Bytes, bool LittleEndian)
            {
                this.Bytes = Bytes;
                this.LittleEndian = LittleEndian;
            }

            public ushort ReadUInt16(int Offset)
            {
                if (LittleEndian)
                {
                    return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(Offset, 2));
                }

                return BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(Offset, 2));
            }

            public uint ReadUInt32(int Offset)
            {
                if (LittleEndian)
                {
                    return BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(Offset, 4));
                }

                return BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(Offset, 4));
            }

            public float ReadSingle(int Offset)
            {
                return BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32(Offset)));
            }
        }

        private sealed class DependencyLookup
        {
            public readonly string DirectoryPath;
            public readonly string FallbackDirectory;
            public readonly JsonArray Found = new JsonArray();
            public readonly JsonArray Absent = new JsonArray();

            private readonly Dictionary<string, string> ResolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public DependencyLookup(string DirectoryPath, string FallbackDirectory)
            {
                this.DirectoryPath = DirectoryPath;
                this.FallbackDirectory = FallbackDirectory;
            }

            public string TryResolve(string FileName, string Kind, string SourceDirectory, out string LocalPath, out string FallbackPath)
            {
                string SearchDirectory = DirectoryPath;
                if (SourceDirectory != null)
                {
                    SearchDirectory = SourceDirectory;
                }

                LocalPath = Path.Combine(SearchDirectory, FileName);
                FallbackPath = null;
                if (ResolvedPaths.TryGetValue(LocalPath, out string ExistingPath))
                {
                    return ExistingPath;
                }

                string ResolvedPath = LocalPath;
                bool FromFallback = false;
                if (!File.Exists(LocalPath))
                {
                    if (FallbackDirectory == null)
                    {
                        return null;
                    }

                    FallbackPath = Path.Combine(FallbackDirectory, FileName);
                    if (!File.Exists(FallbackPath))
                    {
                        return null;
                    }

                    ResolvedPath = FallbackPath;
                    FromFallback = true;
                }

                Found.Add(new JsonObject
                {
                    ["file"] = FileName,
                    ["kind"] = Kind,
                    ["path"] = ResolvedPath,
                    ["fromFallback"] = FromFallback
                });
                ResolvedPaths.Add(LocalPath, ResolvedPath);
                return ResolvedPath;
            }

            public string Resolve(string FileName, string Kind, string MissingMessage, string SourceDirectory = null)
            {
                string ResolvedPath = TryResolve(FileName, Kind, SourceDirectory, out string LocalPath, out string FallbackPath);
                if (ResolvedPath != null)
                {
                    return ResolvedPath;
                }

                if (FallbackPath == null)
                {
                    throw new FileNotFoundException(MissingMessage, LocalPath);
                }

                throw new FileNotFoundException($"{MissingMessage} Searched fallback: {FallbackPath}", LocalPath);
            }

            public void NoteAbsent(int? ObjectId, string FileName, string Kind, string LocalPath, string FallbackPath)
            {
                JsonNode ObjectIdValue = null;
                if (ObjectId.HasValue)
                {
                    ObjectIdValue = JsonValue.Create(ObjectId.Value);
                }

                JsonNode FallbackValue = null;
                if (FallbackPath != null)
                {
                    FallbackValue = JsonValue.Create(FallbackPath);
                }

                Absent.Add(new JsonObject
                {
                    ["objectId"] = ObjectIdValue,
                    ["file"] = FileName,
                    ["kind"] = Kind,
                    ["path"] = LocalPath,
                    ["fallback"] = FallbackValue
                });
            }
        }
    }
}
