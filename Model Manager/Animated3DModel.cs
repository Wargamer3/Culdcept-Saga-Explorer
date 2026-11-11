using CuldceptSagaExplorer.GraphicsManager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpGLTF.Animations;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CuldceptSagaExplorer.ModelManager
{
    public class Animated3DModel : IDisposable
    {
        // Render modes 2 and 3 (bit 1) use the source-alpha + one pass: flames and glows.
        private const int RenderModeAdditive = 2;
        private const int RenderModeAdditiveWithAlpha = 3;

        private List<MeshPart> ListPart = new List<MeshPart>();
        private Vector3 Center;
        private float Radius = 1;
        public readonly List<ModelTexture> ListTexture = new List<ModelTexture>();

        public readonly List<string> ListAnimationName = new List<string>();
        private SceneInstance AnimatedScene;
        private readonly List<AnimatedPart> ListAnimatedPart = new List<AnimatedPart>();
        private readonly Dictionary<IDrawableTemplate, IGeometryTransform> DrawableTransforms = new Dictionary<IDrawableTemplate, IGeometryTransform>();
        private readonly Dictionary<int, IMeshDecoder<Material>> MeshDecoders = new Dictionary<int, IMeshDecoder<Material>>();
        private readonly Dictionary<string, Node> BillboardNodes = new Dictionary<string, Node>();
        private Animation[] BillboardAnimations = Array.Empty<Animation>();
        private float[] AnimationDurations = Array.Empty<float>();
        public double AnimationTime;
        private readonly List<UVOffsetTrack> ListUVOffsetTrack = new List<UVOffsetTrack>();
        private bool[] IsUVOnlyAnimation = Array.Empty<bool>();
        private double UVAnimationTime;
        private bool IsRseScene;
        private readonly List<MaterialColorTrack> ListMaterialColorTrack = new List<MaterialColorTrack>();
        private readonly Dictionary<int, RawOpacityTrack> RawOpacityTracks = new Dictionary<int, RawOpacityTrack>();
        private Effect RawOpacityEffect;
        private int DefaultAnimationIndex;

        private Texture2D WhiteTexture;
        private readonly List<AnimatedPart> ListTranslucentPart = new List<AnimatedPart>();
        private readonly List<AnimatedPart> ListAdditivePart = new List<AnimatedPart>();
        // Colors are premultiplied, so source * One equals straight-alpha source * SourceAlpha.
        // Created per model: a custom blend state binds to the first device that uses it.
        private BlendState AdditivePremultiplied;
        // Shadow decals (black texels with alpha) overlap themselves; each pixel is darkened once per part.
        private readonly HashSet<int> ShadowTextureIndices = new HashSet<int>();
        private readonly Dictionary<int, DepthStencilState> ShadowDepthStencilStates = new Dictionary<int, DepthStencilState>();
        private SceneLighting CurrentLighting = SceneLighting.Default;
        private static readonly System.Text.RegularExpressions.Regex BaseColorOffsetPointer = new System.Text.RegularExpressions.Regex(
            @"^/materials/(\d+)/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset$");
        private static readonly Regex BaseColorPointer = new Regex(
            @"^/materials/(\d+)/pbrMetallicRoughness/baseColorFactor$");
        private bool Disposed;

        public bool IsPaused;
        public bool IsLooping = true;

        public SceneLighting Lighting
        {
            get => CurrentLighting;
            set
            {
                CurrentLighting = value ?? SceneLighting.Unlit;
                foreach (AnimatedPart Part in ListAnimatedPart)
                {
                    if (!DrawableTransforms.TryGetValue(Part.Drawable, out IGeometryTransform Transform))
                    {
                        continue;
                    }
                    ApplyLighting(Part, Transform);
                    Part.Buffer.SetData(Part.Vertices, 0, Part.Vertices.Length, SetDataOptions.Discard);
                }
            }
        }

        public int SelectedAnimationIndex = -1;
        public ModelPreviewCamera Camera;

        public string ActiveAnimationName
        {
            get
            {
                if (SelectedAnimationIndex < 0 || SelectedAnimationIndex >= ListAnimationName.Count)
                {
                    return null;
                }

                return ListAnimationName[SelectedAnimationIndex];
            }
        }

        public Animated3DModel(GraphicsDevice GraphicsDevice)
        {
        }

        public Animated3DModel(GraphicsDevice GraphicsDevice, string GlbModelPath)
        {
            ModelRoot LoadedModel = ModelRoot.Load(GlbModelPath);

            Init(GraphicsDevice, LoadedModel);
        }

        public Animated3DModel(GraphicsDevice GraphicsDevice, ModelRoot GLTFModel)
        {
            Init(GraphicsDevice, GLTFModel);
        }

        private static string GetTextureDisplayName(ModelRoot Model, SharpGLTF.Schema2.Texture Texture)
        {
            bool IsEffect = false;
            if (Model.Extras != null && Model.Extras["rse"] != null)
            {
                IsEffect = true;
            }
            if (!string.IsNullOrWhiteSpace(Texture.Name)
                && (!IsEffect || !string.Equals(Texture.Name, "texture", StringComparison.OrdinalIgnoreCase)))
            {
                return Texture.Name;
            }
            string ImageName = null;
            if (Texture.PrimaryImage != null)
            {
                ImageName = Texture.PrimaryImage.Name;
            }
            if (!string.IsNullOrWhiteSpace(ImageName)
                && (!IsEffect || !string.Equals(ImageName, "image", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ImageName, "texture", StringComparison.OrdinalIgnoreCase)))
            {
                return ImageName;
            }

            // Exported creature and cepter GLBs leave the texture and image unnamed. The source
            // file name, such as CR080_Phlogiston.png, is stored on the material that uses it.
            foreach (Material Material in Model.LogicalMaterials.Where(Material => !string.IsNullOrWhiteSpace(Material.Name)))
            {
                MaterialChannel? BaseColor = Material.FindChannel("BaseColor");
                if (BaseColor.HasValue && BaseColor.Value.Texture == Texture)
                {
                    return Material.Name;
                }

                System.Text.Json.Nodes.JsonArray Frames = null;
                if (Material.Extras != null)
                {
                    System.Text.Json.Nodes.JsonNode Ifl = Material.Extras["ifl"];
                    if (Ifl != null)
                    {
                        Frames = Ifl["frames"] as System.Text.Json.Nodes.JsonArray;
                    }
                }

                if (Frames != null)
                {
                    for (int Frame = 0; Frame < Frames.Count; Frame++)
                    {
                        if (Frames[Frame] is System.Text.Json.Nodes.JsonValue Value
                            && Value.TryGetValue<int>(out int Index) && Index == Texture.LogicalIndex)
                        {
                            return $"{Material.Name}#{Frame}";
                        }
                    }
                }
            }
            return $"Texture {Texture.LogicalIndex}";
        }

        private void Init(GraphicsDevice GraphicsDevice, ModelRoot GLTFModel)
        {
            GltfExportMetadata.PrepareForNativeReaders(GLTFModel);
            List<MeshPart> ListPartTemp = new List<MeshPart>();
            Vector3 MinimumPosition = new Vector3(float.MaxValue);
            Vector3 MaximumPosition = new Vector3(float.MinValue);

            try
            {
                WhiteTexture = new Texture2D(GraphicsDevice, 1, 1);
                WhiteTexture.SetData(new[] { Color.White });

                foreach (SharpGLTF.Schema2.Texture ActiveTexture in GLTFModel.LogicalTextures)
                {
                    string Name = GetTextureDisplayName(GLTFModel, ActiveTexture);

                    if (ActiveTexture.PrimaryImage == null)
                    {
                        throw new NotSupportedException($"Texture {ActiveTexture.LogicalIndex} has no supported primary image.");
                    }

                    using MemoryStream Stream = new MemoryStream(ActiveTexture.PrimaryImage.Content.Content.ToArray(), false);
                    bool HasTransparency = false;
                    bool IsBlack = true;

                    Texture2D LoadedTexture = Texture2D.FromStream(GraphicsDevice, Stream, Pixels =>
                    {
                        for (int Alpha = 3; Alpha < Pixels.Length; Alpha += 4)
                        {
                            HasTransparency |= Pixels[Alpha] != 255;
                            IsBlack &= Pixels[Alpha] == 0 || (Pixels[Alpha - 3] | Pixels[Alpha - 2] | Pixels[Alpha - 1]) == 0;
                        }
                        // Premultiplied alpha avoids dark fringes when filtering next to transparent texels.
                        DefaultColorProcessors.PremultiplyAlpha(Pixels);
                    });

                    LoadedTexture.Name = Name;
                    ListTexture.Add(new ModelTexture(ActiveTexture.LogicalIndex, Name, LoadedTexture, HasTransparency));

                    if (HasTransparency && IsBlack)
                    {
                        ShadowTextureIndices.Add(ActiveTexture.LogicalIndex);
                    }
                }

                foreach (Animation ActiveAnimation in GLTFModel.LogicalAnimations)
                {
                    if (string.IsNullOrWhiteSpace(ActiveAnimation.Name))
                    {
                        ListAnimationName.Add($"Animation {ActiveAnimation.LogicalIndex}");
                    }
                    else
                    {
                        ListAnimationName.Add(ActiveAnimation.Name);
                    }
                }

                Scene Scene = GLTFModel.DefaultScene ?? GLTFModel.LogicalScenes.FirstOrDefault();

                if (Scene == null)
                {
                    throw new InvalidDataException("The model has no scene to display.");
                }

                AnimatedScene = SceneTemplate.Create(Scene).CreateInstance();
                BillboardNodes.Clear();
                BillboardAnimations = GLTFModel.LogicalAnimations.ToArray();
                foreach (Node Node in GLTFModel.LogicalNodes)
                {
                    if (Node.Extras is not System.Text.Json.Nodes.JsonObject NodeExtras || NodeExtras["rseBillboard"] == null)
                    {
                        continue;
                    }

                    if (NodeExtras["rseBillboard"] is not System.Text.Json.Nodes.JsonValue Mode
                        || !Mode.TryGetValue<string>(out string Value) || Value != "spherical" && Value != "verticalProjected"
                        || string.IsNullOrEmpty(Node.Name) || Node.VisualParent != null
                        || GLTFModel.LogicalNodes.Count(Other => Other.Name == Node.Name) != 1)
                    {
                        throw new InvalidDataException("RSE billboards require a uniquely named spherical or verticalProjected billboard root.");
                    }

                    BillboardNodes.Add(Node.Name, Node);
                }
                AnimationDurations = GLTFModel.LogicalAnimations.Select(Animation => Animation.Duration).ToArray();
                LoadUVOffsetTracks(GLTFModel);
                LoadMaterialColorTracks(GLTFModel);
                LoadRawOpacityTracks(GraphicsDevice, GLTFModel);
                DefaultAnimationIndex = 0;
                if (GLTFModel.Extras is System.Text.Json.Nodes.JsonObject RootExtras
                    && RootExtras["rse"] is System.Text.Json.Nodes.JsonObject RseExtras)
                {
                    IsRseScene = true;
                    if (RseExtras["animationIndex"] is not System.Text.Json.Nodes.JsonValue AnimationValue
                        || !AnimationValue.TryGetValue(out DefaultAnimationIndex)
                        || DefaultAnimationIndex < 0 || DefaultAnimationIndex >= ListAnimationName.Count)
                    {
                        throw new InvalidDataException("RSE metadata references a missing animation.");
                    }
                }
                RefreshDrawableTransforms();
                foreach (DrawableInstance Drawable in AnimatedScene)
                {
                    if (Drawable.InstanceCount != 1)
                    {
                        throw new NotSupportedException("GPU-instanced meshes are not supported by the animation preview.");
                    }
                    BuildMesh(GraphicsDevice, GLTFModel.LogicalMeshes[Drawable.Template.LogicalMeshIndex],
                        Drawable, ListPartTemp, ref MinimumPosition, ref MaximumPosition);
                }

                bool EmptyEffect = GLTFModel.Extras is System.Text.Json.Nodes.JsonObject Extras
                    && Extras["rse"] is System.Text.Json.Nodes.JsonObject EffectExtras
                    && EffectExtras["empty"] is System.Text.Json.Nodes.JsonValue EmptyValue
                    && EmptyValue.TryGetValue<bool>(out bool Empty) && Empty;
                bool RenderlessEffect = GLTFModel.Extras is System.Text.Json.Nodes.JsonObject RenderlessExtras
                    && RenderlessExtras["rse"] is System.Text.Json.Nodes.JsonObject RenderlessRse
                    && RenderlessRse["renderless"] is System.Text.Json.Nodes.JsonValue RenderlessValue
                    && RenderlessValue.TryGetValue<bool>(out bool Renderless) && Renderless;
                if (EmptyEffect && GLTFModel.LogicalMeshes.Count != 0)
                {
                    throw new InvalidDataException("An empty RSE scene must not contain meshes.");
                }

                if (RenderlessEffect && GLTFModel.LogicalMeshes.Count != 0)
                {
                    throw new InvalidDataException("A renderless RSE scene must not contain meshes.");
                }

                if (ListPartTemp.Count == 0 && !EmptyEffect && !RenderlessEffect)
                {
                    throw new InvalidDataException("The scene contains no triangle geometry.");
                }
                if (EmptyEffect || RenderlessEffect)
                {
                    MinimumPosition = -Vector3.One;
                    MaximumPosition = Vector3.One;
                }

                IncludeEffectAnimationBounds(GLTFModel, ref MinimumPosition, ref MaximumPosition);
                Center = MinimumPosition * 0.5f + MaximumPosition * 0.5f;
                float Radius = Vector3.Distance(Center, MaximumPosition);

                if (!float.IsFinite(Radius))
                {
                    throw new InvalidDataException("The model bounds exceed the preview's supported range.");
                }

                foreach (MeshPart ActivePart in ListPart)
                {
                    ActivePart.Dispose();
                }

                ListPart = ListPartTemp;
                this.Radius = Math.Max(Radius, 0.001f);
                Camera = new ModelPreviewCamera(Center, this.Radius);
                if (ListAnimationName.Count > 0)
                {
                    PlayAnimation(DefaultAnimationIndex);
                }
            }
            catch
            {
                foreach (MeshPart ActivePart in ListPartTemp)
                {
                    ActivePart.Dispose();
                }
                foreach (ModelTexture ActiveTexture in ListTexture)
                {
                    ActiveTexture.Texture.Dispose();
                }

                ListTexture.Clear();
                if (WhiteTexture != null)
                {
                    WhiteTexture.Dispose();
                }
                WhiteTexture = null;
                throw;
            }
        }

        private void IncludeEffectAnimationBounds(ModelRoot Model, ref Vector3 Minimum, ref Vector3 Maximum)
        {
            if (Model.Extras is not System.Text.Json.Nodes.JsonObject Extras
                || Extras["rse"] is not System.Text.Json.Nodes.JsonObject Scene || Scene["particleCount"] == null)
            {
                return;
            }

            if (Scene["particleCount"] is not System.Text.Json.Nodes.JsonValue ParticleCount
                || !ParticleCount.TryGetValue<int>(out int Count) || Count < 0)
            {
                throw new InvalidDataException("RSE particle metadata requires a non-negative particle count.");
            }

            bool HasVertexMotion = Model.LogicalMeshes.Any(Mesh => Mesh.Primitives.Any(Primitive => Primitive.MorphTargetsCount > 0));
            if (Count == 0 && !HasVertexMotion)
            {
                return;
            }

            if (Scene["totalFrames"] is not System.Text.Json.Nodes.JsonValue FrameValue
                || !FrameValue.TryGetValue<int>(out int Frames) || Frames < 1
                || Scene["framesPerSecond"] is not System.Text.Json.Nodes.JsonValue RateValue
                || !RateValue.TryGetValue<float>(out float Rate) || !float.IsFinite(Rate) || Rate <= 0)
            {
                throw new InvalidDataException("RSE particle metadata requires a valid frame count and rate.");
            }

            for (int Frame = 0; Frame < Frames; Frame++)
            {
                AnimatedScene.Armature.SetAnimationFrame(DefaultAnimationIndex, Frame / Rate, false);
                RefreshDrawableTransforms();
                foreach (AnimatedPart Part in ListAnimatedPart)
                {
                    if (!DrawableTransforms.TryGetValue(Part.Drawable, out IGeometryTransform Transform))
                    {
                        throw new InvalidDataException("An effect mesh has no animated transform.");
                    }

                    foreach (int Index in Part.Indices)
                    {
                        System.Numerics.Vector3 Position = Part.Decoder.GetPosition(Index, Transform);
                        Vector3 Point = new Vector3(Position.X, Position.Y, Position.Z);
                        if (Part.BillboardRoot != null)
                        {
                            AffineTransform Pose = Part.BillboardRoot.GetLocalTransform(Model.LogicalAnimations[DefaultAnimationIndex], Frame / Rate);
                            Vector3 Origin = new Vector3(Pose.Translation.X, Pose.Translation.Y, Pose.Translation.Z);
                            Vector3 Extent = new Vector3(Vector3.Distance(Point, Origin));
                            Minimum = Vector3.Min(Minimum, Origin - Extent);
                            Maximum = Vector3.Max(Maximum, Origin + Extent);
                        }
                        else
                        {
                            Minimum = Vector3.Min(Minimum, Point);
                            Maximum = Vector3.Max(Maximum, Point);
                        }
                    }
                }
            }
            AnimatedScene.Armature.SetAnimationFrame(DefaultAnimationIndex, 0, false);
            RefreshDrawableTransforms();
        }

        public void Update(GameTime GameTime)
        {
            if (Disposed || IsPaused)
            {
                return;
            }

            double Elapsed = GameTime.ElapsedGameTime.TotalSeconds;
            UVAnimationTime += Elapsed;
            if (!IsRseScene)
            {
                ApplyTextureFrames();
            }

            if (SelectedAnimationIndex < 0)
            {
                return;
            }

            float Duration = AnimationDurations[SelectedAnimationIndex];
            if (Duration > 0)
            {
                AnimationTime += Elapsed;
                if (AnimationTime >= Duration)
                {
                    AnimationTime = IsLooping ? AnimationTime % Duration : Duration;
                }
            }
            else
            {
                AnimationTime = 0;
            }

            if (IsRseScene)
            {
                ApplyTextureFrames();
            }

            ApplyAnimationFrame();
        }

        private void ApplyTextureFrames()
        {
            foreach (AnimatedPart ActiveAnimatedPart in ListAnimatedPart)
            {
                if (ActiveAnimatedPart.TextureFrames == null)
                {
                    continue;
                }

                int Index;
                if (ActiveAnimatedPart.RseTextureFrameIndices != null)
                {
                    int Frame = (int)Math.Min(Math.Floor(AnimationTime * ActiveAnimatedPart.RseTextureFramesPerSecond),
                        ActiveAnimatedPart.RseTextureFrameIndices.Length - 1);
                    Index = ActiveAnimatedPart.RseTextureFrameIndices[Frame];
                }
                else
                {
                    long Frame = (long)Math.Floor((IsRseScene ? AnimationTime : UVAnimationTime) * ActiveAnimatedPart.TextureFramesPerSecond);
                    Index = (int)(((Frame % ActiveAnimatedPart.TextureFrames.Length) + ActiveAnimatedPart.TextureFrames.Length) % ActiveAnimatedPart.TextureFrames.Length);
                }
                ActiveAnimatedPart.Part.Texture = ActiveAnimatedPart.TextureFrames[Index];
            }
        }

        public double AnimationDuration(string Name)
        {
            int Index = ListAnimationName.IndexOf(Name);
            if (Index < 0)
            {
                throw new ArgumentException("The glTF file has no animation named \"" + Name + "\".", nameof(Name));
            }

            return AnimationDurations[Index];
        }

        public void SetAnimationSeconds(double Seconds)
        {
            if (!double.IsFinite(Seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(Seconds));
            }

            if (SelectedAnimationIndex < 0)
            {
                UVAnimationTime = Math.Max(0, Seconds);
                ApplyTextureFrames();
                return;
            }

            float Duration = AnimationDurations[SelectedAnimationIndex];
            AnimationTime = Duration > 0 ? Math.Min(Math.Max(0, Seconds), Duration) : 0;
            UVAnimationTime = AnimationTime;
            ApplyTextureFrames();
            ApplyAnimationFrame();
        }

        public void PlayAnimation(string Name)
        {
            int Index = ListAnimationName.IndexOf(Name);
            if (Index < 0)
            {
                throw new ArgumentException("The glTF file has no animation named \"" + Name + "\".", nameof(Name));
            }

            PlayAnimation(Index);
        }

        public void PlayAnimation(int Index)
        {
            if (Index < 0 || Index >= ListAnimationName.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(Index));
            }

            if (Index == SelectedAnimationIndex)
            {
                return;
            }

            SelectedAnimationIndex = Index;
            AnimationTime = 0;
            if (IsRseScene)
            {
                ApplyTextureFrames();
            }

            if (IsUVOnlyAnimation[Index])
            {
                UVAnimationTime = 0;
            }

            ApplyAnimationFrame();
        }

        public void RestartAnimation()
        {
            AnimationTime = 0;
            UVAnimationTime = 0;
            ApplyTextureFrames();
            if (SelectedAnimationIndex >= 0)
            {
                ApplyAnimationFrame();
            }
        }

        private void LoadMaterialColorTracks(ModelRoot Model)
        {
            foreach (Animation ActiveAnimation in Model.LogicalAnimations)
            {
                foreach (AnimationChannel ActiveChannel in ActiveAnimation.Channels)
                {
                    if (ActiveChannel.TargetNodePath != PropertyPath.pointer)
                    {
                        continue;
                    }

                    Match PointerMatch = BaseColorPointer.Match(ActiveChannel.TargetPointerPath ?? "");
                    if (!PointerMatch.Success)
                    {
                        continue;
                    }

                    if (!int.TryParse(PointerMatch.Groups[1].Value, out int MaterialIndex)
                        || MaterialIndex >= Model.LogicalMaterials.Count)
                    {
                        throw new InvalidDataException("A material animation references a missing material.");
                    }
                    IAnimationSampler<System.Numerics.Vector4> Sampler = ActiveChannel.GetSamplerOrNull<System.Numerics.Vector4>();
                    if (Sampler == null)
                    {
                        throw new InvalidDataException("A base-color animation must contain VEC4 samples.");
                    }
                    ListMaterialColorTrack.Add(new MaterialColorTrack
                    {
                        AnimationIndex = ActiveAnimation.LogicalIndex,
                        MaterialIndex = MaterialIndex,
                        Sampler = Sampler.CreateCurveSampler(true)
                    });
                    IsUVOnlyAnimation[ActiveAnimation.LogicalIndex] = false;
                }
            }
        }

        private System.Numerics.Vector4 SampleMaterialColor(AnimatedPart Part)
        {
            foreach (MaterialColorTrack Track in ListMaterialColorTrack)
            {
                if (Track.AnimationIndex != SelectedAnimationIndex || Track.MaterialIndex != Part.MaterialIndex)
                {
                    continue;
                }

                System.Numerics.Vector4 Value = Track.Sampler.GetPoint((float)AnimationTime);
                if (!float.IsFinite(Value.X) || !float.IsFinite(Value.Y)
                    || !float.IsFinite(Value.Z) || !float.IsFinite(Value.W)
                    || Value.X < 0 || Value.X > 1 || Value.Y < 0 || Value.Y > 1
                    || Value.Z < 0 || Value.Z > 1 || Value.W < 0 || Value.W > 1)
                {
                    throw new InvalidDataException($"Material {Track.MaterialIndex} at {AnimationTime:R}s has invalid animated color/opacity {Value}.");
                }
                // RSE MOPA replaces the slot's initial RSM opacity rather than multiplying it.
                if (Part.RawOpacity != null && Part.RawOpacity.AnimationIndex == SelectedAnimationIndex)
                {
                    Value.W = Part.RawOpacity.Sample((float)AnimationTime);
                }

                return Value;
            }
            System.Numerics.Vector4 Color = Part.MaterialColor;
            Color.W *= Part.MaterialOpacity;
            return Color;
        }

        private void LoadRawOpacityTracks(GraphicsDevice Device, ModelRoot Model)
        {
            foreach (Material Material in Model.LogicalMaterials)
            {
                if (Material.Extras is not System.Text.Json.Nodes.JsonObject Extras
                    || !Extras.ContainsKey("rseRawOpacity"))
                {
                    continue;
                }

                if (Extras["rseRawOpacity"] is not System.Text.Json.Nodes.JsonObject Raw
                    || Raw["version"] is not System.Text.Json.Nodes.JsonValue Version || !Version.TryGetValue<int>(out int V) || V != 1
                    || Raw["animationIndex"] is not System.Text.Json.Nodes.JsonValue AnimationValue
                    || !AnimationValue.TryGetValue<int>(out int AnimationIndex) || AnimationIndex < 0 || AnimationIndex >= Model.LogicalAnimations.Count
                    || Raw["interpolation"] is not System.Text.Json.Nodes.JsonValue ModeValue
                    || !ModeValue.TryGetValue<string>(out string Mode) || Mode != "LINEAR" && Mode != "STEP"
                    || Raw["times"] is not System.Text.Json.Nodes.JsonArray Times
                    || Raw["values"] is not System.Text.Json.Nodes.JsonArray Values
                    || Times.Count < 2 || Times.Count != Values.Count)
                {
                    throw new InvalidDataException($"Material {Material.LogicalIndex} has malformed rseRawOpacity metadata.");
                }

                RawOpacityTrack Track = new RawOpacityTrack
                {
                    AnimationIndex = AnimationIndex,
                    Linear = Mode == "LINEAR",
                    Times = new float[Times.Count],
                    Values = new float[Values.Count]
                };
                for (int Index = 0; Index < Times.Count; Index++)
                {
                    if (Times[Index] is not System.Text.Json.Nodes.JsonValue TimeValue || !TimeValue.TryGetValue<float>(out float Time)
                        || Values[Index] is not System.Text.Json.Nodes.JsonValue OpacityValue || !OpacityValue.TryGetValue<float>(out float Opacity)
                        || !float.IsFinite(Time) || !float.IsFinite(Opacity) || Time < 0
                        || Index > 0 && Time <= Track.Times[Index - 1])
                    {
                        throw new InvalidDataException($"Material {Material.LogicalIndex} has invalid raw opacity sample {Index}.");
                    }

                    Track.Times[Index] = Time;
                    Track.Values[Index] = Opacity;
                }
                if (Track.Times[0] != 0 || Math.Abs(Track.Times[^1] - AnimationDurations[AnimationIndex]) > 1e-5
                    || !ListMaterialColorTrack.Any(Color => Color.MaterialIndex == Material.LogicalIndex && Color.AnimationIndex == AnimationIndex))
                {
                    throw new InvalidDataException($"Material {Material.LogicalIndex} raw opacity requires a full-duration bounded fallback track.");
                }

                RawOpacityTracks.Add(Material.LogicalIndex, Track);
            }
            if (RawOpacityTracks.Count == 0)
            {
                return;
            }

            using Stream Stream = typeof(Animated3DModel).Assembly.GetManifestResourceStream(
                "CuldceptSagaExplorer.ModelManager.Shaders.RseRawOpacity.mgfxo")
                ?? throw new InvalidOperationException("The RseRawOpacity.mgfxo resource is missing.");
            using MemoryStream Memory = new MemoryStream();
            Stream.CopyTo(Memory);
            RawOpacityEffect = new Effect(Device, Memory.ToArray());
        }

        private void LoadUVOffsetTracks(ModelRoot LoadedModel)
        {
            ListUVOffsetTrack.Clear();
            IsUVOnlyAnimation = new bool[LoadedModel.LogicalAnimations.Count];

            foreach (Animation ActiveAnimation in LoadedModel.LogicalAnimations)
            {
                bool HasNodeChannel = false;
                int TrackCount = 0;

                foreach (AnimationChannel ActiveChannel in ActiveAnimation.Channels)
                {
                    if (ActiveChannel.TargetNodePath != PropertyPath.pointer)
                    {
                        HasNodeChannel = true;
                        continue;
                    }

                    Match Match = BaseColorOffsetPointer.Match(ActiveChannel.TargetPointerPath ?? string.Empty);

                    if (!Match.Success || !int.TryParse(Match.Groups[1].Value, out int MaterialIndex))
                    {
                        continue;
                    }

                    IAnimationSampler<System.Numerics.Vector2> Sampler = ActiveChannel.GetSamplerOrNull<System.Numerics.Vector2>();
                    if (Sampler == null)
                    {
                        continue;
                    }

                    IEnumerable<float> ListSamplerKey = Sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE
                        ? Sampler.GetCubicKeys().Select(Key => Key.Key)
                        : Sampler.GetLinearKeys().Select(Key => Key.Key);

                    float Duration = ListSamplerKey.DefaultIfEmpty(0).Max();
                    if (!float.IsFinite(Duration))
                    {
                        Duration = 0;
                    }

                    ICurveSampler<System.Numerics.Vector2> Curve = Sampler.CreateCurveSampler(true);
                    System.Numerics.Vector2 LoopDelta = Curve.GetPoint(Duration) - Curve.GetPoint(0);

                    ListUVOffsetTrack.Add(new UVOffsetTrack
                    {
                        AnimationIndex = ActiveAnimation.LogicalIndex,
                        MaterialIndex = MaterialIndex,
                        Duration = Duration,
                        LoopDelta = float.IsFinite(LoopDelta.X) && float.IsFinite(LoopDelta.Y)
                            ? new Vector2(LoopDelta.X, LoopDelta.Y) : Vector2.Zero,
                        Sampler = Curve
                    });

                    TrackCount++;
                }
                IsUVOnlyAnimation[ActiveAnimation.LogicalIndex] = TrackCount > 0 && !HasNodeChannel;
            }
        }

        // UV-only clips (e.g. "UVScroll") run on their own unbounded timer so textures keep scrolling
        // under any selected body animation; UV tracks inside the selected body clip follow its time.
        private bool TryGetAnimatedUVOffset(int MaterialIndex, out Vector2 Offset)
        {
            Vector2? AmbientOffset = null;
            foreach (UVOffsetTrack Track in ListUVOffsetTrack)
            {
                if (Track.MaterialIndex != MaterialIndex)
                {
                    continue;
                }
                bool IsUVOnly = IsUVOnlyAnimation[Track.AnimationIndex];
                if (Track.AnimationIndex == SelectedAnimationIndex)
                {
                    Offset = SampleUVOffset(Track, IsUVOnly ? UVAnimationTime : AnimationTime);
                    return true;
                }
                if (AmbientOffset == null && IsUVOnly)
                {
                    AmbientOffset = SampleUVOffset(Track, UVAnimationTime);
                }
            }
            Offset = AmbientOffset ?? Vector2.Zero;
            return AmbientOffset.HasValue;
        }

        // Each completed loop adds the clip's end-start offset, so scrolling continues instead of
        // snapping back to the first key. The result is wrapped to [0, 1) to keep float precision.
        private static Vector2 SampleUVOffset(UVOffsetTrack Track, double Time)
        {
            double Loops = 0;
            if (Track.Duration > 0)
            {
                Loops = Math.Floor(Time / Track.Duration);
                Time -= Loops * Track.Duration;
            }
            else
            {
                Time = 0;
            }
            System.Numerics.Vector2 Value = Track.Sampler.GetPoint((float)Time);
            if (!float.IsFinite(Value.X) || !float.IsFinite(Value.Y))
            {
                throw new InvalidDataException("A texture offset animation has a non-finite value.");
            }
            double X = -Value.X + -Loops * Track.LoopDelta.X;
            double Y = Value.Y + Loops * Track.LoopDelta.Y;
            return new Vector2((float)(X - Math.Floor(X)), (float)(Y - Math.Floor(Y)));
        }

        private static Vector2 Fraction(Vector2 Value)
        {
            return new Vector2(Value.X - MathF.Floor(Value.X), Value.Y - MathF.Floor(Value.Y));
        }

        private void ApplyUVTransform(AnimatedPart Part)
        {
            Vector2 Offset = TryGetAnimatedUVOffset(Part.MaterialIndex, out Vector2 AnimatedOffset)
                ? AnimatedOffset : Part.UVOffset;
            // Wrapping keeps precision; the preview samples textures with LinearWrap.
            Offset = Fraction(Offset);
            float Cos = MathF.Cos(Part.UVRotation);
            float Sin = MathF.Sin(Part.UVRotation);
            for (int Index = 0; Index < Part.Vertices.Length; Index++)
            {
                Vector2 Uv = Part.SourceUV[Index] * Part.UVScale;
                Part.Vertices[Index].TextureCoordinate = new Vector2(
                    Cos * Uv.X + Sin * Uv.Y + Offset.X,
                    -Sin * Uv.X + Cos * Uv.Y + Offset.Y);
            }
        }

        private void RefreshDrawableTransforms()
        {
            DrawableTransforms.Clear();
            foreach (DrawableInstance Drawable in AnimatedScene)
            {
                DrawableTransforms.Add(Drawable.Template, Drawable.Transform);
            }
        }

        private void ApplyAnimationFrame(Matrix? BillboardView = null)
        {
            AnimatedScene.Armature.SetAnimationFrame(SelectedAnimationIndex, (float)AnimationTime, false);
            if (BillboardView.HasValue)
            {
                if (!BillboardView.Value.Decompose(out _, out Quaternion ViewRotation, out _))
                {
                    throw new InvalidDataException("The view transform cannot be decomposed for RSE billboards.");
                }

                System.Numerics.Quaternion Facing = new System.Numerics.Quaternion(-ViewRotation.X, -ViewRotation.Y, -ViewRotation.Z, ViewRotation.W);
                foreach (Node Root in BillboardNodes.Values)
                {
                    System.Numerics.Matrix4x4 Basis = System.Numerics.Matrix4x4.CreateFromQuaternion(Facing);
                    if (Root.Extras["rseBillboard"].GetValue<string>() == "verticalProjected")
                    {
                        // The game projects the camera basis without renormalizing it.
                        Basis.M12 = Basis.M21 = Basis.M23 = Basis.M32 = 0;
                        Basis.M22 = 1;
                    }
                    AffineTransform Pose = SelectedAnimationIndex >= 0
                        ? Root.GetLocalTransform(BillboardAnimations[SelectedAnimationIndex], (float)AnimationTime)
                        : Root.LocalTransform;
                    System.Numerics.Matrix4x4 Matrix = System.Numerics.Matrix4x4.CreateScale(Pose.Scale)
                        * System.Numerics.Matrix4x4.CreateFromQuaternion(Pose.Rotation)
                        * Basis
                        * System.Numerics.Matrix4x4.CreateTranslation(Pose.Translation);
                    AnimatedScene.Armature.SetLocalMatrix(Root.Name, Matrix);
                }
            }
            RefreshDrawableTransforms();
            foreach (AnimatedPart Part in ListAnimatedPart)
            {
                if (!DrawableTransforms.TryGetValue(Part.Drawable, out IGeometryTransform Transform))
                {
                    continue;
                }
                for (int Index = 0; Index < Part.Vertices.Length; Index++)
                {
                    System.Numerics.Vector3 Position = Part.Decoder.GetPosition(Part.Indices[Index], Transform);
                    if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
                    {
                        throw new InvalidDataException("An animated vertex has a non-finite position.");
                    }
                    Part.Vertices[Index].Position = new Vector3(Position.X, Position.Y, Position.Z);
                }
                ApplyLighting(Part, Transform);
                if (Part.IsTranslucent)
                {
                    ComputeBounds(Part);
                }
                if (Part.SourceUV != null)
                {
                    ApplyUVTransform(Part);
                }
                Part.Buffer.SetData(Part.Vertices, 0,
                    Part.Vertices.Length, SetDataOptions.Discard);
            }
        }

        private void BuildMesh(GraphicsDevice GraphicsDevice, Mesh GltfMesh, DrawableInstance Drawable,
            List<MeshPart> ListPart, ref Vector3 MinimumPosition, ref Vector3 MaximumPosition)
        {
            if (GltfMesh != null)
            {
                HashSet<Node> BillboardRoots = new HashSet<Node>();
                if (BillboardNodes.Count > 0)
                {
                    foreach (Node MeshNode in GltfMesh.LogicalParent.LogicalNodes.Where(Node => Node.Mesh == GltfMesh))
                    {
                        Node Root = MeshNode;
                        while (Root.VisualParent != null)
                        {
                            Root = Root.VisualParent;
                        }

                        BillboardRoots.Add(Root.Name != null && BillboardNodes.ContainsKey(Root.Name) ? Root : null);
                    }
                    if (BillboardRoots.Count > 1)
                    {
                        throw new NotSupportedException("A mesh shared across different billboard roots must be exported as separate instances.");
                    }
                }
                if (!MeshDecoders.TryGetValue(GltfMesh.LogicalIndex, out IMeshDecoder<Material> MeshDecoder))
                {
                    MeshDecoder = GltfMesh.Decode();
                    MeshDecoders.Add(GltfMesh.LogicalIndex, MeshDecoder);
                }
                IGeometryTransform Transform = Drawable.Transform;
                int PrimitiveIndex = 0;
                foreach (MeshPrimitive ActiveMeshPrimitive in GltfMesh.Primitives)
                {
                    IMeshPrimitiveDecoder<Material> Decoder = MeshDecoder.Primitives[PrimitiveIndex++];
                    SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> ArrayVertexPosition = null;
                    Accessor PositionAccessor = ActiveMeshPrimitive.GetVertexAccessor("POSITION");
                    if (PositionAccessor != null)
                    {
                        ArrayVertexPosition = PositionAccessor.AsVector3Array();
                    }
                    if (ArrayVertexPosition == null)
                    {
                        throw new InvalidDataException("A mesh primitive is missing POSITION.");
                    }

                    (int A, int B, int C)[] ArrayTriangle = ActiveMeshPrimitive.GetTriangleIndices().ToArray();

                    if (ArrayTriangle.Length == 0)
                    {
                        throw new NotSupportedException("The preview supports triangle, triangle-strip and triangle-fan geometry only.");
                    }

                    SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector4> ArrayVertexColor = null;
                    Accessor ColorAccessor = ActiveMeshPrimitive.GetVertexAccessor("COLOR_0");
                    if (ColorAccessor != null)
                    {
                        ArrayVertexColor = ColorAccessor.AsColorArray();
                    }

                    MaterialChannel? TextureChannel = null;
                    if (ActiveMeshPrimitive.Material != null)
                    {
                        TextureChannel = ActiveMeshPrimitive.Material.FindChannel("BaseColor");
                    }

                    System.Numerics.Vector4 ColorMultiplier = System.Numerics.Vector4.One;
                    if (TextureChannel.HasValue)
                    {
                        ColorMultiplier = TextureChannel.Value.Color;
                    }

                    TextureTransform TextureTransform = null;
                    if (TextureChannel.HasValue && TextureChannel.Value.Texture != null)
                    {
                        TextureTransform = TextureChannel.Value.TextureTransform;
                    }

                    int TextureCoordinate = 0;
                    if (TextureTransform != null && TextureTransform.TextureCoordinateOverride.HasValue)
                    {
                        TextureCoordinate = TextureTransform.TextureCoordinateOverride.Value;
                    }
                    else if (TextureChannel.HasValue)
                    {
                        TextureCoordinate = TextureChannel.Value.TextureCoordinate;
                    }

                    SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector2> ArrayUV = null;
                    if (TextureChannel.HasValue && TextureChannel.Value.Texture != null)
                    {
                        Accessor UvAccessor = ActiveMeshPrimitive.GetVertexAccessor("TEXCOORD_" + TextureCoordinate);
                        if (UvAccessor != null)
                        {
                            ArrayUV = UvAccessor.AsVector2Array();
                        }
                    }

                    if (TextureChannel.HasValue && TextureChannel.Value.Texture != null && ArrayUV == null)
                    {
                        throw new InvalidDataException("A base-color texture is missing its texture coordinates.");
                    }

                    float MaterialOpacity = GetRsmFloat(ActiveMeshPrimitive.Material, "opacity", 1);
                    VertexPositionColorTexture[] ArrayVertex = new VertexPositionColorTexture[checked(ArrayTriangle.Length * 3)];
                    System.Numerics.Vector4[] ArraySourceVertexColor = new System.Numerics.Vector4[ArrayVertex.Length];
                    int[] SourceIndices = new int[ArrayVertex.Length];
                    int CurrentVertexID = 0;

                    foreach ((int A, int B, int C) ActiveTriangle in ArrayTriangle)
                    {
                        SourceIndices[CurrentVertexID] = ActiveTriangle.Item1;
                        AddVertex(ActiveTriangle.Item1, Decoder, Transform, ArrayVertexColor, System.Numerics.Vector4.One, ArrayUV, ArrayVertex, ArraySourceVertexColor, ref CurrentVertexID);
                        SourceIndices[CurrentVertexID] = ActiveTriangle.Item2;
                        AddVertex(ActiveTriangle.Item2, Decoder, Transform, ArrayVertexColor, System.Numerics.Vector4.One, ArrayUV, ArrayVertex, ArraySourceVertexColor, ref CurrentVertexID);
                        SourceIndices[CurrentVertexID] = ActiveTriangle.Item3;
                        AddVertex(ActiveTriangle.Item3, Decoder, Transform, ArrayVertexColor, System.Numerics.Vector4.One, ArrayUV, ArrayVertex, ArraySourceVertexColor, ref CurrentVertexID);
                    }

                    foreach (VertexPositionColorTexture ActiveVertex in ArrayVertex)
                    {
                        MinimumPosition = Vector3.Min(MinimumPosition, ActiveVertex.Position);
                        MaximumPosition = Vector3.Max(MaximumPosition, ActiveVertex.Position);
                    }

                    MeshPart NewMeshPart = new MeshPart { TriangleCount = ArrayTriangle.Length, OwnsTexture = false };

                    ListPart.Add(NewMeshPart);
                    DynamicVertexBuffer VertexBuffer = new DynamicVertexBuffer(GraphicsDevice, typeof(VertexPositionColorTexture), ArrayVertex.Length, BufferUsage.WriteOnly);
                    NewMeshPart.Vertices = VertexBuffer;

                    NewMeshPart.Vertices.SetData(ArrayVertex);
                    AnimatedPart NewAnimatedPart = new AnimatedPart
                    {
                        Part = NewMeshPart,
                        Buffer = VertexBuffer,
                        Drawable = Drawable.Template,
                        BillboardRoot = BillboardRoots.SingleOrDefault(),
                        Decoder = Decoder,
                        Indices = SourceIndices,
                        Vertices = ArrayVertex,
                        SourceColors = ArraySourceVertexColor,
                        MaterialIndex = -1,
                        MaterialColor = ColorMultiplier,
                        MaterialOpacity = MaterialOpacity,
                        RawOpacity = ActiveMeshPrimitive.Material != null && RawOpacityTracks.TryGetValue(
                            ActiveMeshPrimitive.Material.LogicalIndex, out RawOpacityTrack RawTrack) ? RawTrack : null,
                        HasNormals = ActiveMeshPrimitive.GetVertexAccessor("NORMAL") != null,
                        SelfIllumination = Math.Clamp(GetSelfIllumination(ActiveMeshPrimitive.Material), 0, 1),
                        DrawOrder = ListAnimatedPart.Count,
                        IsAdditive = UsesAdditivePass(ActiveMeshPrimitive.Material),
                        RenderMode = GetRenderMode(ActiveMeshPrimitive.Material),
                        IsShadowDecal = false,
                        IsTranslucent = false
                    };
                    if (ActiveMeshPrimitive.Material != null)
                    {
                        NewAnimatedPart.MaterialIndex = ActiveMeshPrimitive.Material.LogicalIndex;
                    }

                    bool HasBaseColor = TextureChannel.HasValue && TextureChannel.Value.Texture != null;
                    if (HasBaseColor && ShadowTextureIndices.Contains(TextureChannel.Value.Texture.LogicalIndex))
                    {
                        NewAnimatedPart.IsShadowDecal = true;
                    }

                    bool MaterialHasColorTrack = false;
                    if (ActiveMeshPrimitive.Material != null)
                    {
                        int MaterialIndex = ActiveMeshPrimitive.Material.LogicalIndex;
                        MaterialHasColorTrack = ListMaterialColorTrack.Any(Track => Track.MaterialIndex == MaterialIndex);
                    }

                    bool? TextureHasTransparency = null;
                    if (HasBaseColor)
                    {
                        TextureHasTransparency = ListTexture[TextureChannel.Value.Texture.LogicalIndex].HasTransparency;
                    }

                    NewAnimatedPart.IsTranslucent = MaterialHasColorTrack || ColorMultiplier.W < 1
                        || IsBlended(ActiveMeshPrimitive.Material, TextureHasTransparency, ArraySourceVertexColor);
                    ApplyLighting(NewAnimatedPart, Transform);
                    VertexBuffer.SetData(ArrayVertex);
                    if (NewAnimatedPart.IsTranslucent)
                    {
                        ComputeBounds(NewAnimatedPart);
                    }
                    ListAnimatedPart.Add(NewAnimatedPart);
                    if (ArrayUV != null)
                    {
                        NewAnimatedPart.MaterialIndex = ActiveMeshPrimitive.Material.LogicalIndex;
                        NewAnimatedPart.SourceUV = ArrayVertex.Select(Vertex => Vertex.TextureCoordinate).ToArray();
                        if (TextureTransform != null)
                        {
                            System.Numerics.Vector2 Offset = TextureTransform.Offset;
                            System.Numerics.Vector2 Scale = TextureTransform.Scale;
                            float Rotation = TextureTransform.Rotation;
                            if (!float.IsFinite(Offset.X) || !float.IsFinite(Offset.Y) || !float.IsFinite(Scale.X)
                                || !float.IsFinite(Scale.Y) || !float.IsFinite(Rotation))
                            {
                                throw new InvalidDataException("A texture transform has a non-finite value.");
                            }
                            NewAnimatedPart.UVOffset = new Vector2(Offset.X, Offset.Y);
                            NewAnimatedPart.UVScale = new Vector2(Scale.X, Scale.Y);
                            NewAnimatedPart.UVRotation = Rotation;
                        }
                        ApplyUVTransform(NewAnimatedPart);
                        VertexBuffer.SetData(ArrayVertex);
                    }
                    if (HasBaseColor)
                    {
                        NewMeshPart.Texture = ListTexture[TextureChannel.Value.Texture.LogicalIndex].Texture;
                        if (TryGetTextureFrames(ActiveMeshPrimitive.Material, out int[] Frames, out float FramesPerSecond,
                            out int[] RseIndices, out float RseFramesPerSecond))
                        {
                            NewAnimatedPart.TextureFrames = Frames.Select(Index => ListTexture[Index].Texture).ToArray();
                            NewAnimatedPart.TextureFramesPerSecond = FramesPerSecond;
                            NewAnimatedPart.RseTextureFrameIndices = RseIndices;
                            NewAnimatedPart.RseTextureFramesPerSecond = RseFramesPerSecond;
                        }
                    }
                }
            }

        }

        private static void AddVertex(int VertexIndex, IMeshPrimitiveDecoder Decoder, IGeometryTransform Transform,
            SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector4> ArrayVertexColor,
            System.Numerics.Vector4 ColorMultiplier,
            SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector2> ArrayUV,
            VertexPositionColorTexture[] ArrayVertex, System.Numerics.Vector4[] ArraySourceVertexColor, ref int CurrentVertexID)
        {
            System.Numerics.Vector3 VertexPosition = Decoder.GetPosition(VertexIndex, Transform);

            if (!float.IsFinite(VertexPosition.X) || !float.IsFinite(VertexPosition.Y) || !float.IsFinite(VertexPosition.Z))
            {
                throw new InvalidDataException("A vertex has a non-finite position.");
            }

            System.Numerics.Vector4 VertexColor = (ArrayVertexColor == null ? System.Numerics.Vector4.One : ArrayVertexColor[VertexIndex]) * ColorMultiplier;

            System.Numerics.Vector2 UV = System.Numerics.Vector2.Zero;
            if (ArrayUV != null)
            {
                UV = ArrayUV[VertexIndex];
            }

            ArraySourceVertexColor[CurrentVertexID] = VertexColor;

            ArrayVertex[CurrentVertexID++] = new VertexPositionColorTexture(
                new Vector3(VertexPosition.X, VertexPosition.Y, VertexPosition.Z),
                Color.White,
                new Vector2(UV.X, UV.Y));
        }

        // Normals follow skinning and morphing, so this runs again for every animation frame.
        private void ApplyLighting(AnimatedPart Part, IGeometryTransform Transform)
        {
            System.Numerics.Vector4 MaterialColor = SampleMaterialColor(Part);
            if (Part.RawOpacity != null)
            {
                MaterialColor.W = 1;
            }

            for (int V = 0; V < Part.Vertices.Length; V++)
            {
                System.Numerics.Vector4 Source = Part.SourceColors[V] * MaterialColor;

                Vector3 VertexColor = new Vector3(Source.X, Source.Y, Source.Z);

                if (Part.HasNormals)
                {
                    System.Numerics.Vector3 Normal = Part.Decoder.GetNormal(Part.Indices[V], Transform);

                    Vector3 WorldNormal = float.IsFinite(Normal.X) && float.IsFinite(Normal.Y) && float.IsFinite(Normal.Z)
                        ? new Vector3(Normal.X, Normal.Y, Normal.Z) : Vector3.Zero;

                    VertexColor = CurrentLighting.Apply(VertexColor, WorldNormal, Part.SelfIllumination, Part.IsTranslucent || Part.IsAdditive);
                }
                VertexColor = Vector3.Clamp(VertexColor, Vector3.Zero, Vector3.One);
                if (Part.RawOpacity != null)
                {
                    Part.Vertices[V].Color = new Color(VertexColor.X, VertexColor.Y, VertexColor.Z, Source.W);
                    continue;
                }
                if (!Part.IsTranslucent)
                {
                    // The opaque pass keeps only alpha 255, so a zero vertex alpha is discarded.
                    // Writing it as opaque black covers whatever is behind the triangle.
                    Part.Vertices[V].Color = Source.W == 0 ? new Color(0, 0, 0, 0) : new Color(VertexColor.X, VertexColor.Y, VertexColor.Z, 1f);
                    continue;
                }
                Part.Vertices[V].Color = new Color(VertexColor.X * Source.W, VertexColor.Y * Source.W, VertexColor.Z * Source.W, Source.W);
            }
        }

        private static float GetSelfIllumination(Material GltfMaterial)
        {
            return GetRsmFloat(GltfMaterial, "selfIllumination", 0);
        }

        private static float GetRsmFloat(Material GltfMaterial, string Name, float Fallback)
        {
            if (GltfMaterial == null || GltfMaterial.Extras is not System.Text.Json.Nodes.JsonObject Extras
                || Extras["rsm"] is not System.Text.Json.Nodes.JsonObject Rsm
                || Rsm[Name] is not System.Text.Json.Nodes.JsonValue Value
                || !Value.TryGetValue(out float Result) || !float.IsFinite(Result))
            {
                return Fallback;
            }

            return Result;
        }

        // The opaque pass still blends: source alpha, one minus source alpha. Vertex alpha below 1
        // therefore fades the triangle, including render mode 0. Render mode 1 always blends.
        // Files without RSM data fall back to texture or vertex alpha.
        private static bool IsBlended(Material ActiveMaterial, bool? TextureHasTransparency, System.Numerics.Vector4[] VertexColors)
        {
            int Mode = GetRenderMode(ActiveMaterial);
            if (Mode == 1)
            {
                return true;
            }
            if (TextureHasTransparency == true || GetRsmFloat(ActiveMaterial, "opacity", 1) < 1)
            {
                return true;
            }

            return VertexColors.Any(VertexColor => VertexColor.W < 1);
        }

        // Bit 1 of the material render mode selects the source-alpha + one queue (modes 2 and 3).
        private static bool UsesAdditivePass(Material GltfMaterial)
        {
            int Mode = GetRenderMode(GltfMaterial);
            return Mode == RenderModeAdditive || Mode == RenderModeAdditiveWithAlpha;
        }

        // Material extras.ifl = { frames: [texture indices], framesPerSecond } written by rsm2gltf for .ifl lists.
        private bool TryGetTextureFrames(Material GltfMaterial, out int[] Frames, out float FramesPerSecond,
            out int[] RseIndices, out float RseFramesPerSecond)
        {
            Frames = Array.Empty<int>();
            FramesPerSecond = 0;
            RseIndices = null;
            RseFramesPerSecond = 0;
            if (GltfMaterial == null || GltfMaterial.Extras is not System.Text.Json.Nodes.JsonObject Extras
                || Extras["ifl"] is not System.Text.Json.Nodes.JsonObject Ifl
                || Ifl["frames"] is not System.Text.Json.Nodes.JsonArray FrameArray
                || Ifl["framesPerSecond"] is not System.Text.Json.Nodes.JsonValue Rate
                || !Rate.TryGetValue(out FramesPerSecond))
            {
                return false;
            }

            if (!float.IsFinite(FramesPerSecond) || FramesPerSecond < 0)
            {
                throw new InvalidDataException("An animated texture list has an invalid frame rate.");
            }

            Frames = new int[FrameArray.Count];
            for (int Index = 0; Index < Frames.Length; Index++)
            {
                if (FrameArray[Index] is not System.Text.Json.Nodes.JsonValue Value || !Value.TryGetValue(out Frames[Index])
                    || Frames[Index] < 0 || Frames[Index] >= ListTexture.Count)
                {
                    throw new InvalidDataException("An animated texture list references a missing texture.");
                }
            }

            if (Ifl["rseFrameIndices"] != null || Ifl["rseFramesPerSecond"] != null)
            {
                if (!IsRseScene || Ifl["rseFrameIndices"] is not System.Text.Json.Nodes.JsonArray Indices
                    || Indices.Count == 0 || Ifl["rseFramesPerSecond"] is not System.Text.Json.Nodes.JsonValue SceneRate
                    || !SceneRate.TryGetValue(out RseFramesPerSecond) || !float.IsFinite(RseFramesPerSecond)
                    || RseFramesPerSecond <= 0 || Frames.Length < 2 || FramesPerSecond <= 0)
                {
                    throw new InvalidDataException("An RSE texture timeline has invalid frame indices or frame rate.");
                }

                RseIndices = new int[Indices.Count];
                for (int Index = 0; Index < RseIndices.Length; Index++)
                {
                    if (Indices[Index] is not System.Text.Json.Nodes.JsonValue Value || !Value.TryGetValue(out RseIndices[Index])
                        || RseIndices[Index] < 0 || RseIndices[Index] >= Frames.Length)
                    {
                        throw new InvalidDataException("An RSE texture timeline references a missing list frame.");
                    }
                }
            }
            return Frames.Length > 1 && FramesPerSecond > 0;
        }

        private static int GetRenderMode(Material GltfMaterial)
        {
            if (GltfMaterial == null || GltfMaterial.Extras is not System.Text.Json.Nodes.JsonObject Extras
                || Extras["rsm"] is not System.Text.Json.Nodes.JsonObject Rsm
                || Rsm["renderMode"] is not System.Text.Json.Nodes.JsonValue Mode
                || !Mode.TryGetValue(out int Value))
            {
                return -1;
            }
            return Value;
        }

        private static void ComputeBounds(AnimatedPart Part)
        {
            Vector3 Sum = Vector3.Zero;
            Vector3 Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue);
            foreach (VertexPositionColorTexture Vertex in Part.Vertices)
            {
                Sum += Vertex.Position;
                Min = Vector3.Min(Min, Vertex.Position);
                Max = Vector3.Max(Max, Vertex.Position);
            }
            Part.Centroid = Sum / Part.Vertices.Length;
            Part.Bounds = new BoundingBox(Min, Max);
        }

        // Translucent parts whose bounds intersect (e.g. a fountain's water surface, foam ring and spray, which share
        // almost the same centroid) keep the file's draw order, as the game does; a centroid sort would flip them as
        // the camera turns. Separate groups are still sorted back to front.
        private void SortTranslucentParts(Matrix View)
        {
            List<AnimatedPart> Parts = ListTranslucentPart;
            for (int Index = 0; Index < Parts.Count; Index++)
            {
                Parts[Index].Cluster = Index;
            }

            int Find(int Index)
            {
                while (Parts[Index].Cluster != Index)
                {
                    Index = Parts[Index].Cluster = Parts[Parts[Index].Cluster].Cluster;
                }

                return Index;
            }
            for (int First = 0; First < Parts.Count; First++)
            {
                for (int Second = First + 1; Second < Parts.Count; Second++)
                {
                    if (Parts[First].Bounds.Intersects(Parts[Second].Bounds))
                    {
                        int A = Find(First), B = Find(Second);
                        if (A != B)
                        {
                            Parts[Math.Max(A, B)].Cluster = Math.Min(A, B);
                        }
                    }
                }
            }

            Dictionary<int, (Vector3 Sum, int Count)> ClusterDepth = new Dictionary<int, (Vector3 Sum, int Count)>();
            for (int Index = 0; Index < Parts.Count; Index++)
            {
                int Root = Find(Index);
                ClusterDepth.TryGetValue(Root, out (Vector3 Sum, int Count) Entry);
                ClusterDepth[Root] = (Entry.Sum + Parts[Index].Centroid, Entry.Count + 1);
            }
            for (int Index = 0; Index < Parts.Count; Index++)
            {
                (Vector3 Sum, int Count) Entry = ClusterDepth[Parts[Index].Cluster = Find(Index)];
                Parts[Index].ViewDepth = Vector3.Transform(Entry.Sum / Entry.Count, View).Z;
            }
            // View space looks down -Z, so the most negative depth is the farthest group.
            Parts.Sort((First, Second) =>
            {
                int Order = First.ViewDepth.CompareTo(Second.ViewDepth);
                if (Order == 0)
                {
                    Order = First.Cluster.CompareTo(Second.Cluster);
                }
                return Order != 0 ? Order : First.DrawOrder.CompareTo(Second.DrawOrder);
            });
        }

        // Two passes, matching how the game's textures carry alpha (materials are all flagged OPAQUE):
        // 1. Every part, opaque, writing depth, keeping only texels with alpha == 255.
        // 2. Parts whose texture/color has alpha, sorted back to front, blended (premultiplied),
        //    depth-tested but not written, keeping only texels with alpha < 255.
        // 3. Additive parts (render modes 2 and 3), colour added on top, depth-tested but not written.
        public void Draw(GraphicsDevice GraphicsDevice, GameTime GameTime, AlphaTestEffect ModelEffect)
        {
            if (GraphicsDevice == null || ModelEffect == null || Disposed || ListPart.Count == 0 || Camera == null)
            {
                return;
            }

            float AspectRatio = GraphicsDevice.Viewport.AspectRatio;
            Draw(GraphicsDevice, GameTime, ModelEffect, Camera.CreateView(AspectRatio), Camera.CreateProjection(AspectRatio), Matrix.Identity);
        }

        // Draws into the bound target without clearing it. Life Sim weather reads that color and depth afterward.
        public void Draw(GraphicsDevice GraphicsDevice, GameTime GameTime, AlphaTestEffect ModelEffect, Matrix View, Matrix Projection, Matrix World)
        {
            if (GraphicsDevice == null || ModelEffect == null || Disposed || ListPart.Count == 0)
            {
                return;
            }

            BlendState PreviousBlend = GraphicsDevice.BlendState;
            DepthStencilState PreviousDepth = GraphicsDevice.DepthStencilState;
            RasterizerState PreviousRaster = GraphicsDevice.RasterizerState;
            SamplerState PreviousSampler = GraphicsDevice.SamplerStates[0];
            try
            {
                Matrix ModelView = World * View;
                if (BillboardNodes.Count > 0)
                {
                    ApplyAnimationFrame(ModelView);
                }

                ModelEffect.World = World;
                ModelEffect.View = View;
                ModelEffect.Projection = Projection;
                ModelEffect.VertexColorEnabled = true;
                ModelEffect.DiffuseColor = Vector3.One;
                ModelEffect.Alpha = 1;
                ModelEffect.ReferenceAlpha = 255;

                GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

                GraphicsDevice.BlendState = BlendState.Opaque;
                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                ModelEffect.AlphaFunction = CompareFunction.Equal;
                ListTranslucentPart.Clear();
                ListAdditivePart.Clear();
                foreach (AnimatedPart Binding in ListAnimatedPart)
                {
                    if (!DrawableTransforms.TryGetValue(Binding.Drawable, out IGeometryTransform Transform) || !Transform.Visible)
                    {
                        continue;
                    }
                    if (Binding.IsAdditive)
                    {
                        ListAdditivePart.Add(Binding);
                        continue;
                    }
                    DrawPart(GraphicsDevice, ModelEffect, Binding);
                    if (Binding.IsTranslucent)
                    {
                        ListTranslucentPart.Add(Binding);
                    }
                }

                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                if (ListTranslucentPart.Count > 0)
                {
                    SortTranslucentParts(ModelView);
                    GraphicsDevice.BlendState = BlendState.AlphaBlend;
                    ModelEffect.AlphaFunction = CompareFunction.Less;
                    foreach (AnimatedPart Binding in ListTranslucentPart)
                    {
                        GraphicsDevice.DepthStencilState = Binding.IsShadowDecal
                            ? GetShadowDepthStencilState(GraphicsDevice, Binding.DrawOrder % 255 + 1)
                            : DepthStencilState.DepthRead;
                        // The stencil already makes shadow decals order independent.
                        if (Binding.IsShadowDecal)
                        {
                            DrawPart(GraphicsDevice, ModelEffect, Binding);
                        }
                        else
                        {
                            DrawSortedPart(GraphicsDevice, ModelEffect, Binding, ModelView);
                        }
                    }
                    GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                }

                if (ListAdditivePart.Count == 0)
                {
                    return;
                }
                // Addition is order independent, so no sorting is needed.
                if (AdditivePremultiplied == null || AdditivePremultiplied.GraphicsDevice != GraphicsDevice)
                {
                    if (AdditivePremultiplied != null)
                    {
                        AdditivePremultiplied.Dispose();
                    }
                    AdditivePremultiplied = new BlendState
                    {
                        Name = "AdditivePremultiplied",
                        ColorSourceBlend = Blend.One,
                        ColorDestinationBlend = Blend.One,
                        AlphaSourceBlend = Blend.Zero,
                        AlphaDestinationBlend = Blend.One,
                    };
                }
                GraphicsDevice.BlendState = AdditivePremultiplied;
                ModelEffect.AlphaFunction = CompareFunction.Always;
                foreach (AnimatedPart Binding in ListAdditivePart)
                {
                    DrawPart(GraphicsDevice, ModelEffect, Binding);
                }
            }
            finally
            {
                GraphicsDevice.BlendState = PreviousBlend;
                GraphicsDevice.DepthStencilState = PreviousDepth;
                GraphicsDevice.RasterizerState = PreviousRaster;
                GraphicsDevice.SamplerStates[0] = PreviousSampler;
            }
        }

        private DepthStencilState GetShadowDepthStencilState(GraphicsDevice GraphicsDevice, int Reference)
        {
            if (ShadowDepthStencilStates.TryGetValue(Reference, out DepthStencilState State) && State.GraphicsDevice == GraphicsDevice)
            {
                return State;
            }
            if (State != null)
            {
                State.Dispose();
            }
            State = new DepthStencilState
            {
                Name = "ShadowDecal",
                DepthBufferEnable = true,
                DepthBufferWriteEnable = false,
                StencilEnable = true,
                StencilFunction = CompareFunction.NotEqual,
                StencilPass = StencilOperation.Replace,
                ReferenceStencil = Reference,
            };
            ShadowDepthStencilStates[Reference] = State;
            return State;
        }

        // Ordinary cloth (render mode 0) is culled the way the game culls it. The inner belt shell
        // faces the body, and drawing it covers the lit outer shell. Flame cards stay double-sided.
        private static void SetCull(GraphicsDevice GraphicsDevice, AnimatedPart Part)
        {
            GraphicsDevice.RasterizerState = Part.RenderMode == 0
                ? RasterizerState.CullClockwise
                : RasterizerState.CullNone;
        }

        private void DrawPart(GraphicsDevice GraphicsDevice, AlphaTestEffect ModelEffect, AnimatedPart Binding)
        {
            SetCull(GraphicsDevice, Binding);
            MeshPart ActivePart = Binding.Part;
            ModelEffect.Texture = ActivePart.Texture ?? WhiteTexture;
            GraphicsDevice.SetVertexBuffer(ActivePart.Vertices);

            foreach (EffectPass ActivePass in PreparePartEffect(ModelEffect, Binding).CurrentTechnique.Passes)
            {
                ActivePass.Apply();
                GraphicsDevice.DrawPrimitives(Microsoft.Xna.Framework.Graphics.PrimitiveType.TriangleList, 0, ActivePart.TriangleCount);
            }
        }

        private Effect PreparePartEffect(AlphaTestEffect ModelEffect, AnimatedPart Binding)
        {
            if (Binding.RawOpacity == null)
            {
                return ModelEffect;
            }

            RawOpacityEffect.Parameters["WorldViewProjection"].SetValue(ModelEffect.World * ModelEffect.View * ModelEffect.Projection);
            RawOpacityEffect.Parameters["RawOpacity"].SetValue(SampleMaterialColor(Binding).W);
            RawOpacityEffect.Parameters["AlphaPass"].SetValue(ModelEffect.AlphaFunction == CompareFunction.Equal ? 0
                : ModelEffect.AlphaFunction == CompareFunction.Less ? 1 : 2);
            RawOpacityEffect.Parameters["ModelTexture"].SetValue(Binding.Part.Texture ?? WhiteTexture);
            return RawOpacityEffect;
        }

        // Depth writes are off for blended parts, so a closed shell (e.g. a glass bulb) drawn in file order can paint
        // its far side over its near side. Draw the triangles back to front from the current view instead; triangles
        // at equal depth keep the file order.
        private void DrawSortedPart(GraphicsDevice GraphicsDevice, AlphaTestEffect ModelEffect, AnimatedPart Binding, Matrix View)
        {
            SetCull(GraphicsDevice, Binding);
            MeshPart ActivePart = Binding.Part;
            int TriangleCount = ActivePart.TriangleCount;
            if (TriangleCount < 2)
            {
                DrawPart(GraphicsDevice, ModelEffect, Binding);
                return;
            }

            if (Binding.TriangleOrder == null)
            {
                Binding.TriangleOrder = new int[TriangleCount];
                Binding.TriangleDepth = new float[TriangleCount];
                Binding.SortedIndices = new int[TriangleCount * 3];
            }
            VertexPositionColorTexture[] Vertices = Binding.Vertices;
            for (int Triangle = 0; Triangle < TriangleCount; Triangle++)
            {
                int First = Triangle * 3;
                Vector3 Center = Vertices[First].Position + Vertices[First + 1].Position + Vertices[First + 2].Position;
                Binding.TriangleOrder[Triangle] = Triangle;
                Binding.TriangleDepth[Triangle] = Center.X * View.M13 + Center.Y * View.M23 + Center.Z * View.M33;
            }
            // View space looks down -Z, so the most negative depth is the farthest triangle.
            Array.Sort(Binding.TriangleOrder, (A, B) =>
            {
                int Order = Binding.TriangleDepth[A].CompareTo(Binding.TriangleDepth[B]);
                return Order != 0 ? Order : A.CompareTo(B);
            });
            for (int Index = 0; Index < TriangleCount; Index++)
            {
                int First = Binding.TriangleOrder[Index] * 3;
                Binding.SortedIndices[Index * 3] = First;
                Binding.SortedIndices[Index * 3 + 1] = First + 1;
                Binding.SortedIndices[Index * 3 + 2] = First + 2;
            }

            if (ActivePart.Indices == null || ActivePart.Indices.GraphicsDevice != GraphicsDevice)
            {
                if (ActivePart.Indices != null)
                {
                    ActivePart.Indices.Dispose();
                }
                ActivePart.Indices = new DynamicIndexBuffer(GraphicsDevice, IndexElementSize.ThirtyTwoBits,
                    Binding.SortedIndices.Length, BufferUsage.WriteOnly);
            }
            ((DynamicIndexBuffer)ActivePart.Indices).SetData(Binding.SortedIndices, 0, Binding.SortedIndices.Length, SetDataOptions.Discard);

            ModelEffect.Texture = ActivePart.Texture ?? WhiteTexture;
            GraphicsDevice.SetVertexBuffer(ActivePart.Vertices);
            GraphicsDevice.Indices = ActivePart.Indices;
            foreach (EffectPass ActivePass in PreparePartEffect(ModelEffect, Binding).CurrentTechnique.Passes)
            {
                ActivePass.Apply();
                GraphicsDevice.DrawIndexedPrimitives(Microsoft.Xna.Framework.Graphics.PrimitiveType.TriangleList, 0, 0, TriangleCount);
            }
            GraphicsDevice.Indices = null;
        }

        public List<string> GetAnimationNames()
        {
            return new List<string>(ListAnimationName);
        }

        public List<string> GetTextureNames()
        {
            return ListTexture.Select(Texture => Texture.Name).ToList();
        }

        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            Disposed = true;
            UnloadContent();
        }

        public void UnloadContent()
        {
            foreach (MeshPart ActiveParts in ListPart)
            {
                ActiveParts.Dispose();
            }

            ListPart.Clear();
            foreach (ModelTexture Texture in ListTexture)
            {
                Texture.Texture.Dispose();
            }

            ListTexture.Clear();
            if (WhiteTexture != null)
            {
                WhiteTexture.Dispose();
            }
            WhiteTexture = null;
            ListTranslucentPart.Clear();
            ListAdditivePart.Clear();
            if (AdditivePremultiplied != null)
            {
                AdditivePremultiplied.Dispose();
            }
            AdditivePremultiplied = null;
            foreach (DepthStencilState State in ShadowDepthStencilStates.Values)
            {
                State.Dispose();
            }

            ShadowDepthStencilStates.Clear();
            ShadowTextureIndices.Clear();
            ListAnimationName.Clear();
            ListAnimatedPart.Clear();
            DrawableTransforms.Clear();
            MeshDecoders.Clear();
            ListUVOffsetTrack.Clear();
            ListMaterialColorTrack.Clear();
            RawOpacityTracks.Clear();
            if (RawOpacityEffect != null)
            {
                RawOpacityEffect.Dispose();
            }
            RawOpacityEffect = null;
            IsUVOnlyAnimation = Array.Empty<bool>();
            AnimatedScene = null;
            SelectedAnimationIndex = -1;
        }

        private sealed class AnimatedPart
        {
            public MeshPart Part;
            public DynamicVertexBuffer Buffer;
            public IDrawableTemplate Drawable;
            public Node BillboardRoot;
            public IMeshPrimitiveDecoder Decoder;
            public int[] Indices;
            public VertexPositionColorTexture[] Vertices;
            public int MaterialIndex = -1;
            public Vector2[] SourceUV;
            public Vector2 UVOffset;
            public Vector2 UVScale = Vector2.One;
            public float UVRotation;
            public bool IsTranslucent;
            public bool IsAdditive;
            public int RenderMode = -1;
            public bool IsShadowDecal;
            // Unlit straight-alpha vertex colour, before material colour and opacity.
            public System.Numerics.Vector4[] SourceColors;
            public System.Numerics.Vector4 MaterialColor;
            public float MaterialOpacity;
            public RawOpacityTrack RawOpacity;
            public bool HasNormals;
            public float SelfIllumination;
            public int DrawOrder;
            public Vector3 Centroid;
            public BoundingBox Bounds;
            public int Cluster;
            public float ViewDepth;
            // Back-to-front triangle order for translucent parts, rebuilt each frame from the view.
            public int[] TriangleOrder;
            public float[] TriangleDepth;
            public int[] SortedIndices;
            // Ordinary IFL lists use the UV timer; RSE lists can carry baked scene-frame selections.
            public Texture2D[] TextureFrames;
            public float TextureFramesPerSecond;
            public int[] RseTextureFrameIndices;
            public float RseTextureFramesPerSecond;
        }

        private sealed class UVOffsetTrack
        {
            public int AnimationIndex;
            public int MaterialIndex;
            public float Duration;
            public Vector2 LoopDelta;
            public SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector2> Sampler;
        }

        private sealed class MaterialColorTrack
        {
            public int AnimationIndex;
            public int MaterialIndex;
            public ICurveSampler<System.Numerics.Vector4> Sampler;
        }

        private sealed class RawOpacityTrack
        {
            public int AnimationIndex;
            public float[] Times;
            public float[] Values;
            public bool Linear;

            public float Sample(float Time)
            {
                int Index = Array.BinarySearch(Times, Time);
                if (Index >= 0)
                {
                    return Values[Index];
                }

                Index = ~Index;
                if (Index == 0)
                {
                    return Values[0];
                }

                if (Index == Times.Length || !Linear)
                {
                    return Values[Index - 1];
                }

                double Fraction = ((double)Time - Times[Index - 1]) / ((double)Times[Index] - Times[Index - 1]);
                return (float)(Values[Index - 1] * (1 - Fraction) + Values[Index] * Fraction);
            }
        }
    }
}
