using CuldceptSagaExplorer.AssetManager.RSA;
using CuldceptSagaExplorer.AssetManager.RSM;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CuldceptSagaExplorer.AssetManager.ExternalModel
{
    using VertexSkinned = VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>;
    using VertexStatic = VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>;

    public static class GltfExporter
    {

        private static Dictionary<int, List<VertexTrack>> ReadVertexTracks(List<RsaClip> ListClip, RsmDocument CuldceptModel)
        {
            Dictionary<int, List<VertexTrack>> DicVertexTrackByNodeIndex = new Dictionary<int, List<VertexTrack>>();

            for (int C = 0; C < ListClip.Count; C++)
            {
                RsaClip ActiveClip = ListClip[C];

                for (int N = 0; N < ActiveClip.ListChannel.Count; N++)
                {
                    RsaChannel ActiveClipChannel = ActiveClip.ListChannel[N];

                    if (ActiveClipChannel.Kind != RsaChannelKind.VertexFrames)
                    {
                        continue;
                    }

                    if (!CuldceptModel.DicMeshByNodeIndex.TryGetValue(N, out RsmMesh ExistingMesh))
                    {
                        continue;
                    }
                    if (ExistingMesh.IsSkinned)
                    {
                        continue;
                    }
                    if (ActiveClipChannel.VertexCount != ExistingMesh.ListPosition.Count)
                    {
                        continue;
                    }
                    if (ActiveClipChannel.NormalCount != 0 && ActiveClipChannel.NormalCount != ExistingMesh.ListNormal.Count)
                    {
                        continue;
                    }

                    VertexTrack NewVertexTrack = new VertexTrack
                    {
                        ClipIndex = C,
                        Frames = ActiveClipChannel.ListVertexFrame,
                        FrameNumbers = ActiveClipChannel.ListVertexFrame.Select(F => F.Frame).ToArray()
                    };

                    if (!DicVertexTrackByNodeIndex.TryGetValue(N, out List<VertexTrack> ListVertexTrack))
                    {
                        DicVertexTrackByNodeIndex[N] = ListVertexTrack = new List<VertexTrack>();
                    }

                    ListVertexTrack.Add(NewVertexTrack);
                }
            }

            return DicVertexTrackByNodeIndex;
        }

        private static List<UVScroll> CollectUvScrolls(List<RsmMaterial> ListMaterial, float FPS)
        {
            List<UVScroll> ListUVScroll = new List<UVScroll>();

            if (ListMaterial == null)
            {
                return ListUVScroll;
            }

            for (int M = 0; M < ListMaterial.Count; M++)
            {
                RsmMaterial ActiveMaterial = ListMaterial[M];

                float FinalUVX = ActiveMaterial.UvScroll.X * FPS;
                float FinalUVY = ActiveMaterial.UvScroll.Y * FPS;

                if (Math.Abs(FinalUVX) > 1e-8f || Math.Abs(FinalUVY) > 1e-8f)
                {
                    ListUVScroll.Add(new UVScroll(M, FinalUVX, FinalUVY));
                }
            }

            return ListUVScroll;
        }

        public static ModelRoot CreateGlbFromRSM(RsmDocument CuldceptModel, Dictionary<string, List<byte[]>> DicTexturesByMaterialName = null, List<RsaClip> ListAnimation = null, float FPS = 60.0f, bool ReflectZ = true, bool PreserveUnusedMaterials = false, bool CreateAnimationChannels = true, bool PreserveVertexFrameTopology = false)
        {
            SceneBuilder NewModel = BuildRsmScene(CuldceptModel, DicTexturesByMaterialName, ListAnimation, FPS, ReflectZ,
                CreateAnimationChannels: CreateAnimationChannels, PreserveVertexFrameTopology: PreserveVertexFrameTopology);
            ModelRoot ConvertedModel = CompleteRsmModel(NewModel.ToGltf2(), CuldceptModel, DicTexturesByMaterialName, FPS, PreserveUnusedMaterials);
            if (PreserveVertexFrameTopology)
            {
                CompleteRsmVertexMeshes(ConvertedModel, CuldceptModel, ListAnimation, ReflectZ);
            }

            return ConvertedModel;
        }

        internal static SceneBuilder BuildRsmScene(RsmDocument CuldceptModel, Dictionary<string, List<byte[]>> DicTexturesByMaterialName,
            List<RsaClip> ListAnimation, float FPS, bool ReflectZ, SceneBuilder Destination = null,
            NodeBuilder ObjectRoot = null, string NamePrefix = "", bool PreserveMaterialSlots = false, bool CreateAnimationChannels = true,
            bool PreserveVertexFrameTopology = false)
        {
            if (PreserveVertexFrameTopology && CreateAnimationChannels)
            {
                throw new ArgumentException("Topology-preserving RSE meshes require externally authored animation channels.", nameof(CreateAnimationChannels));
            }

            SceneBuilder NewModel = Destination ?? new SceneBuilder();
            if (ObjectRoot != null)
            {
                NewModel.AddNode(ObjectRoot);
            }

            int ListNodeCount = CuldceptModel.ListNode.Count;

            // 1. Build Node Hierarchy
            NodeBuilder[] ArrayNodeBuilder = new NodeBuilder[ListNodeCount];
            for (int N = 0; N < ListNodeCount; N++)
            {
                RsmNode ActiveRsmNode = CuldceptModel.ListNode[N];
                Vector3 Translation = ActiveRsmNode.Translation;
                Quaternion Rotation;

                if (ReflectZ)
                {
                    Translation.Z = -Translation.Z;
                    Rotation = Quaternion.Normalize(new Quaternion(
                        ActiveRsmNode.Rotation.X,
                        ActiveRsmNode.Rotation.Y,
                        -ActiveRsmNode.Rotation.Z,
                        ActiveRsmNode.Rotation.W));
                }
                else
                {
                    Rotation = Quaternion.Normalize(new Quaternion(
                        -ActiveRsmNode.Rotation.X,
                        -ActiveRsmNode.Rotation.Y,
                        -ActiveRsmNode.Rotation.Z,
                        ActiveRsmNode.Rotation.W));
                }

                ArrayNodeBuilder[N] = new NodeBuilder($"{NamePrefix}node_{N}");
                ArrayNodeBuilder[N].WithLocalTranslation(Translation)
                               .WithLocalRotation(Rotation)
                               .WithLocalScale(ActiveRsmNode.Scale);
            }

            for (int N = 0; N < ListNodeCount; N++)
            {
                int ParentIndex = CuldceptModel.ArrayParentIndex[N];
                if (ParentIndex >= 0)
                {
                    ArrayNodeBuilder[ParentIndex].AddNode(ArrayNodeBuilder[N]);
                }
                else
                {
                    if (ObjectRoot == null)
                    {
                        NewModel.AddNode(ArrayNodeBuilder[N]);
                    }
                    else
                    {
                        ObjectRoot.AddNode(ArrayNodeBuilder[N]);
                    }
                }
            }

            // 2. Prepare Joint Array
            (NodeBuilder Node, Matrix4x4 InverseBindMatrix)[] ArraySkinJoint = new (NodeBuilder Node, Matrix4x4 InverseBindMatrix)[ListNodeCount];
            for (int NodeIndex = 0; NodeIndex < ListNodeCount; NodeIndex++)
            {
                Matrix4x4 WorldMatrix = CuldceptModel.ArrayWorldMatrix[NodeIndex];
                if (ReflectZ)
                {
                    WorldMatrix = ReflectMatrixZ(WorldMatrix);
                }

                Matrix4x4.Invert(WorldMatrix, out Matrix4x4 InvBindMatrix);
                ArraySkinJoint[NodeIndex] = (ArrayNodeBuilder[NodeIndex], InvBindMatrix);
            }

            // 3. Build Materials
            List<MaterialBuilder> ListMaterial = BuildMaterials(CuldceptModel.ListMaterial, DicTexturesByMaterialName);
            if (NamePrefix.Length > 0 || PreserveMaterialSlots)
            {
                for (int Index = 0; Index < ListMaterial.Count; Index++)
                {
                    ListMaterial[Index].Name = PreserveMaterialSlots
                        ? RsmMaterialName(CuldceptModel.ListMaterial, Index, NamePrefix, true)
                        : NamePrefix + ListMaterial[Index].Name;
                }
            }

            // 4. Process Meshes
            bool FlipWinding = ShouldFlipWinding(CuldceptModel, ReflectZ);

            Dictionary<int, List<VertexTrack>> DicVertexTrackByNodeIndex = ReadVertexTracks(ListAnimation, CuldceptModel);

            // Keep track of rigid instances so we can attach morph weight curves later
            Dictionary<int, InstanceBuilder> DicRigidInstancesByNodeIndex = new Dictionary<int, InstanceBuilder>();

            foreach (KeyValuePair<int, RsmMesh> ActiveNode in CuldceptModel.DicMeshByNodeIndex)
            {
                int NodeIndex = ActiveNode.Key;
                RsmMesh ActiveMesh = ActiveNode.Value;
                if (PreserveVertexFrameTopology && DicVertexTrackByNodeIndex.ContainsKey(NodeIndex))
                {
                    continue;
                }

                // Empty geometry blocks (an all-zero geometry header) render nothing; skip them
                // so they don't produce a primitive-less glTF mesh.
                if (ActiveMesh == null || (ActiveMesh.ListMeshGroup == null || ActiveMesh.ListMeshGroup.Count == 0))
                {
                    continue;
                }

                if (ActiveMesh.IsSkinned)
                {
                    MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4> MeshBuilder = BuildSkinnedMesh($"{NamePrefix}mesh_{NodeIndex}", ActiveMesh, CuldceptModel, ListMaterial, FlipWinding, ReflectZ);

                    if (MeshBuilder != null)
                    {
                        NewModel.AddSkinnedMesh(MeshBuilder, ArraySkinJoint);
                    }
                }
                else
                {
                    DicVertexTrackByNodeIndex.TryGetValue(NodeIndex, out List<VertexTrack> ListVertexTrack);
                    MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> MeshBuilder = BuildStaticMesh($"{NamePrefix}mesh_{NodeIndex}", ActiveMesh, ListMaterial, FlipWinding, ReflectZ, ListVertexTrack);

                    if (MeshBuilder != null)
                    {
                        InstanceBuilder NewInstanceBuilder = NewModel.AddRigidMesh(MeshBuilder, ArrayNodeBuilder[NodeIndex]);
                        DicRigidInstancesByNodeIndex[NodeIndex] = NewInstanceBuilder;
                    }
                    if (ListVertexTrack != null && ListVertexTrack.Count > 0)
                    {
                        int Expected = ListVertexTrack.Sum(Track => Track.Frames.Count);
                    }
                }
            }

            // 5. Attach Animation Keyframes
            if (CreateAnimationChannels && ListAnimation != null && ListAnimation.Count > 0)
            {
                for (int A = 0; A < ListAnimation.Count; A++)
                {
                    RsaClip ActiveAnimation = ListAnimation[A];

                    if (ActiveAnimation == null)
                    {
                        continue;
                    }

                    string ClipName = string.IsNullOrEmpty(ActiveAnimation.Name) ? "Animation" : ActiveAnimation.Name;
                    float ClipStartFrame = ActiveAnimation.GetStartFrame();

                    for (int C = 0; C < ActiveAnimation.ListChannel.Count && C < ListNodeCount; C++)
                    {
                        RsaChannel ActiveChannel = ActiveAnimation.ListChannel[C];

                        // ----- Skeletal animation (unchanged) -----
                        if (ActiveChannel.Kind == RsaChannelKind.TransformKeys && ActiveChannel.ListKeyframe.Count > 0)
                        {
                            NodeBuilder ActiveNodeBuilder = ArrayNodeBuilder[C];
                            Quaternion PreviousRotation = Quaternion.Identity;
                            bool IsFirst = true;

                            foreach (RsaTransformKeyframe ActiveKeyFrame in ActiveChannel.ListKeyframe)
                            {
                                float TimeInSeconds = (ActiveKeyFrame.Frame - ClipStartFrame) / FPS;

                                Vector3 Translation = ActiveKeyFrame.Translation;
                                Quaternion Rotation = ActiveKeyFrame.Rotation;

                                if (ReflectZ)
                                {
                                    Translation.Z = -Translation.Z;
                                    Rotation = Quaternion.Normalize(new Quaternion(Rotation.X, Rotation.Y, -Rotation.Z, Rotation.W));
                                }
                                else
                                {
                                    Rotation = Quaternion.Normalize(new Quaternion(-Rotation.X, -Rotation.Y, -Rotation.Z, Rotation.W));
                                }

                                if (!IsFirst && Quaternion.Dot(Rotation, PreviousRotation) < 0)
                                {
                                    Rotation = new Quaternion(-Rotation.X, -Rotation.Y, -Rotation.Z, -Rotation.W);
                                }

                                PreviousRotation = Rotation;
                                IsFirst = false;

                                ActiveNodeBuilder.UseTranslation(ClipName).WithPoint(TimeInSeconds, Translation);
                                ActiveNodeBuilder.UseRotation(ClipName).WithPoint(TimeInSeconds, Rotation);
                                ActiveNodeBuilder.UseScale(ClipName).WithPoint(TimeInSeconds, ActiveKeyFrame.Scale);
                            }
                        }
                        // ----- Morph / vertex animation -----
                        else if (ActiveChannel.Kind == RsaChannelKind.VertexFrames
                            && DicRigidInstancesByNodeIndex.TryGetValue(C, out InstanceBuilder RigidInstance)
                            && DicVertexTrackByNodeIndex.TryGetValue(C, out List<VertexTrack> NodeTracks))
                        {
                            int TotalTargets = NodeTracks.Sum(Track => Track.Frames.Count);
                            if (TotalTargets == 0)
                            {
                                continue;
                            }

                            // Collect all keyframes for this clip first, then sort by time
                            List<(float Time, float[] Weights)> Keyframes = new List<(float Time, float[] Weights)>();

                            int FirstTargetIndex = 0;
                            foreach (VertexTrack Track in NodeTracks)
                            {
                                if (Track.ClipIndex != A)
                                {
                                    FirstTargetIndex += Track.Frames.Count;
                                    continue;
                                }

                                for (int F = 0; F < Track.Frames.Count; F++)
                                {
                                    float Time = (Track.FrameNumbers[F] - ClipStartFrame) / FPS;
                                    float[] Weights = new float[TotalTargets];
                                    Weights[FirstTargetIndex + F] = 1.0f;
                                    Keyframes.Add((Time, Weights));
                                }
                                FirstTargetIndex += Track.Frames.Count;
                            }

                            if (Keyframes.Count == 0)
                            {
                                continue;
                            }

                            // Sort and remove duplicate times (keep last)
                            Keyframes = Keyframes
                                .GroupBy(Keyframe => Keyframe.Time)
                                .Select(KeyframeGroup => KeyframeGroup.Last())
                                .OrderBy(Keyframe => Keyframe.Time)
                                .ToList();

                            SharpGLTF.Animations.AnimatableProperty<ArraySegment<float>> MorphProp = RigidInstance.Content.UseMorphing();

                            // Explicitly set the rest-pose weights to all zeros
                            MorphProp.SetValue(new float[TotalTargets]);   // important!

                            SharpGLTF.Animations.CurveBuilder<ArraySegment<float>> TrackBuilder = MorphProp.UseTrackBuilder(ClipName);

                            foreach ((float Time, float[] Weights) in Keyframes)
                            {
                                TrackBuilder.SetPoint(Time, Weights);
                            }
                        }
                    }
                }
            }

            return NewModel;
        }

        private static ModelRoot CompleteRsmModel(ModelRoot ConvertedModel, RsmDocument CuldceptModel,
            Dictionary<string, List<byte[]>> DicTexturesByMaterialName, float FPS, bool PreserveUnusedMaterials = false)
        {
            // Attach original RSM node index to every logical node that still has the name
            foreach (Node ActiveNode in ConvertedModel.LogicalNodes)
            {
                if (ActiveNode.Name != null && ActiveNode.Name.StartsWith("node_")
                    && int.TryParse(ActiveNode.Name.AsSpan(5), out int NodeIndex)
                    && NodeIndex >= 0 && NodeIndex < CuldceptModel.ListNode.Count)
                {
                    JsonObject Extras = ActiveNode.Extras as JsonObject ?? new JsonObject();
                    JsonObject Rsm = Extras["rsm"] as JsonObject ?? new JsonObject();
                    Rsm["nodeIndex"] = NodeIndex;
                    if (CuldceptModel.ListNode[NodeIndex].GeometryPadding != 0)
                    {
                        Rsm["geometryPadding"] = CuldceptModel.ListNode[NodeIndex].GeometryPadding;
                    }
                    Extras["rsm"] = Rsm;
                    ActiveNode.Extras = Extras;
                }
            }

            // Same for meshes
            foreach (Mesh ActiveLogicalMesh in ConvertedModel.LogicalMeshes)
            {
                if (ActiveLogicalMesh.Name != null && ActiveLogicalMesh.Name.StartsWith("mesh_")
                    && int.TryParse(ActiveLogicalMesh.Name.AsSpan(5), out int NodeIndex))
                {
                    JsonObject Extras = ActiveLogicalMesh.Extras as JsonObject ?? new JsonObject();
                    Extras["rsmNodeIndex"] = NodeIndex;

                    // Skinned meshes carry a verbatim snapshot so the GLB round-trips losslessly.
                    if (CuldceptModel.DicMeshByNodeIndex.TryGetValue(NodeIndex, out RsmMesh SourceMesh) && SourceMesh.IsSkinned)
                    {
                        Extras[RsmMeshExtras.Key] = RsmMeshExtras.ToJson(SourceMesh);
                    }

                    ActiveLogicalMesh.Extras = Extras;
                }
            }

            CompleteRsmMaterials(ConvertedModel, CuldceptModel, DicTexturesByMaterialName, FPS, PreserveUnusedMaterials: PreserveUnusedMaterials);
            return CompleteRsmMeshMetadata(ConvertedModel, CuldceptModel);
        }

        internal static void CompleteRsmMaterials(ModelRoot ConvertedModel, RsmDocument CuldceptModel,
            Dictionary<string, List<byte[]>> DicTexturesByMaterialName, float FPS, string NamePrefix = "", bool CreateUvAnimation = true,
            bool PreserveMaterialSlots = false, bool PreserveUnusedMaterials = false)
        {
            // 6a. Attach Material Extras + SetTransform (lookup by name)
            for (int M = 0; M < CuldceptModel.ListMaterial.Count; M++)
            {
                RsmMaterial ActiveMaterial = CuldceptModel.ListMaterial[M];

                // Find the corresponding logical material by name
                Material ActiveLogicalMaterial = ConvertedModel.LogicalMaterials.FirstOrDefault(LogicalMaterial =>
                    LogicalMaterial.Name == RsmMaterialName(CuldceptModel.ListMaterial, M, NamePrefix, PreserveMaterialSlots));

                if (ActiveLogicalMaterial == null)
                {
                    if (!PreserveUnusedMaterials)
                    {
                        continue;
                    }
                    // RSE commands address slots even when no surviving primitive uses them.
                    ActiveLogicalMaterial = ConvertedModel.CreateMaterial(
                        RsmMaterialName(CuldceptModel.ListMaterial, M, NamePrefix, PreserveMaterialSlots));
                    ActiveLogicalMaterial.InitializePBRMetallicRoughness();
                    ActiveLogicalMaterial.DoubleSided = true;
                    if (DicTexturesByMaterialName != null
                        && DicTexturesByMaterialName.TryGetValue(ActiveMaterial.Name.ToLowerInvariant(), out List<byte[]> Frames)
                        && Frames.Count > 0)
                    {
                        Image Image = ConvertedModel.CreateImage();
                        Image.Name = NamePrefix + ActiveMaterial.Name;
                        Image.Content = Frames[0];
                        ActiveLogicalMaterial.FindChannel("BaseColor").Value.SetTexture(0, Image);
                    }
                }

                MaterialChannel? ActiveChannel = ActiveLogicalMaterial.FindChannel("BaseColor");
                if (ActiveChannel.HasValue && ActiveChannel.Value.Texture != null)
                {
                    Texture Texture = ActiveChannel.Value.Texture;
                    if (string.IsNullOrWhiteSpace(Texture.Name) || string.Equals(Texture.Name, "texture", StringComparison.OrdinalIgnoreCase))
                    {
                        Texture.Name = NamePrefix + ActiveMaterial.Name;
                    }

                    if (Texture.PrimaryImage != null && (string.IsNullOrWhiteSpace(Texture.PrimaryImage.Name)
                        || string.Equals(Texture.PrimaryImage.Name, "image", StringComparison.OrdinalIgnoreCase)))
                    {
                        Texture.PrimaryImage.Name = NamePrefix + ActiveMaterial.Name;
                    }

                    Vector2 ScrollOffset = new Vector2(ActiveMaterial.UvScroll.X, ActiveMaterial.UvScroll.Y);
                    ActiveChannel.Value.SetTransform(ScrollOffset, Vector2.One, 0f);
                }

                // extras (still safe to attach by the logical material we found)
                ActiveLogicalMaterial.Extras = CreateMaterialExtras(ActiveMaterial);
            }

            // ----- .ifl animated textures -----
            const float DefaultIflFps = 15.0f;

            if (DicTexturesByMaterialName != null)
            {
                for (int MaterialIndex = 0; MaterialIndex < CuldceptModel.ListMaterial.Count; MaterialIndex++)
                {
                    RsmMaterial ActiveMaterial = CuldceptModel.ListMaterial[MaterialIndex];
                    string MaterialName = ActiveMaterial.Name.ToLower();

                    if (!MaterialName.EndsWith(".ifl"))
                    {
                        continue;
                    }
                    if (!DicTexturesByMaterialName.TryGetValue(MaterialName, out List<byte[]> ListTexture) || ListTexture.Count <= 1)
                    {
                        continue;
                    }

                    Material ActiveLogicalMaterial = ConvertedModel.LogicalMaterials.FirstOrDefault(LogicalMaterial =>
                        LogicalMaterial.Name == RsmMaterialName(CuldceptModel.ListMaterial, MaterialIndex, NamePrefix, PreserveMaterialSlots));

                    if (ActiveLogicalMaterial == null)
                    {
                        continue;
                    }

                    MaterialChannel? ActiveMaterialChannel = ActiveLogicalMaterial.FindChannel("BaseColor");
                    if (!ActiveMaterialChannel.HasValue || ActiveMaterialChannel.Value.Texture == null)
                    {
                        continue;
                    }

                    List<int> ListFrameIndex = new List<int> { ActiveMaterialChannel.Value.Texture.LogicalIndex };

                    // Add frames 1..N as new images + textures
                    for (int T = 1; T < ListTexture.Count; T++)
                    {
                        // 1. Image
                        Image NewImage = ConvertedModel.CreateImage();
                        NewImage.Name = $"{NamePrefix}{ActiveMaterial.Name}#{T}";
                        NewImage.Content = ListTexture[T];   // byte[] or MemoryImage – both work in 1.0.x

                        // 2. Texture (SharpGLTF 1.0.7)
                        //    CreateTexture() is not always public; use the factory that exists:
                        Texture ActiveTexture = ConvertedModel.LogicalTextures.FirstOrDefault(CandidateTexture => CandidateTexture.PrimaryImage == NewImage);

                        if (ActiveTexture == null)
                        {
                            // Fallback that works on 1.0.7 when CreateTexture is missing:
                            // create a temporary material that references the image, then read the texture back
                            Material TmpMat = ConvertedModel.CreateMaterial();
                            TmpMat.Name = $"__ifl_tmp_{NamePrefix}{ActiveMaterial.Name}_{T}";
                            TmpMat.InitializePBRMetallicRoughness();

                            MaterialChannel? ColorChannel = TmpMat.FindChannel("BaseColor");
                            if (ColorChannel.HasValue)
                            {
                                ColorChannel.Value.SetTexture(0, NewImage);   // this creates the Texture
                                ActiveTexture = ColorChannel.Value.Texture;
                            }
                            // We don't need the temp material afterwards; leaving it is harmless
                        }

                        if (ActiveTexture != null)
                        {
                            ActiveTexture.Name = NewImage.Name;
                            ListFrameIndex.Add(ActiveTexture.LogicalIndex);
                        }
                    }

                    if (ListFrameIndex.Count > 1)
                    {
                        // Merge into existing extras (keep the rsm block you already wrote)
                        JsonObject Extras;
                        if (ActiveLogicalMaterial.Extras is JsonObject Existing)
                        {
                            Extras = Existing;
                        }
                        else if (ActiveLogicalMaterial.Extras != null)
                        {
                            Extras = JsonNode.Parse(ActiveLogicalMaterial.Extras.ToJsonString()) as JsonObject ?? new JsonObject();
                        }
                        else
                        {
                            Extras = new JsonObject();
                        }

                        Extras["ifl"] = new JsonObject
                        {
                            ["frames"] = new JsonArray(
                                ListFrameIndex.Select(FrameIndex => (JsonNode)JsonValue.Create(FrameIndex)).ToArray()),
                            ["framesPerSecond"] = DefaultIflFps
                        };

                        ActiveLogicalMaterial.Extras = Extras;
                    }
                }
            }

            // ---------- Animated UV scroll ----------
            List<UVScroll> ListUVScroll = CollectUvScrolls(CuldceptModel.ListMaterial, FPS);

            if (CreateUvAnimation && ListUVScroll.Count > 0)
            {
                float MaxUV = ListUVScroll.Max(Scroll => Math.Max(Math.Abs(Scroll.ScrollU), Math.Abs(Scroll.ScrollV)));
                float Duration = 1.0f / Math.Max(MaxUV, 1e-6f);

                Animation UVAnimation = null;

                // First, dump every logical material so we can see the real order
                for (int M = 0; M < ConvertedModel.LogicalMaterials.Count; M++)
                {
                    Material ActiveMaterial = ConvertedModel.LogicalMaterials[M];
                    MaterialChannel? ActiveChannel = ActiveMaterial.FindChannel("BaseColor");
                    bool HasTexture = ActiveChannel.HasValue && ActiveChannel.Value.Texture != null;
                    bool HasTextureTransform = ActiveChannel.HasValue && ActiveChannel.Value.TextureTransform != null;
                }

                foreach (UVScroll ActiveUVScroll in ListUVScroll)
                {
                    Material UVMaterial = ConvertedModel.LogicalMaterials
                        .FirstOrDefault(M => M.Name == RsmMaterialName(CuldceptModel.ListMaterial, ActiveUVScroll.MaterialIndex,
                            NamePrefix, PreserveMaterialSlots));

                    if (UVMaterial == null)
                    {
                        continue;
                    }

                    // get the real index in LogicalMaterials
                    int UVMaterialIndex = -1;
                    for (int M = 0; M < ConvertedModel.LogicalMaterials.Count; M++)
                    {
                        if (ConvertedModel.LogicalMaterials[M] == UVMaterial)
                        {
                            UVMaterialIndex = M;
                            break;
                        }
                    }
                    if (UVMaterialIndex < 0)
                    {
                        continue;
                    }

                    MaterialChannel? BaseColor = UVMaterial.FindChannel("BaseColor");
                    if (!BaseColor.HasValue || BaseColor.Value.Texture == null)
                    {
                        continue;
                    }
                    if (BaseColor.Value.TextureTransform == null)
                    {
                        continue;
                    }

                    Dictionary<float, Vector2> OffsetHack = new Dictionary<float, Vector2>
                    {
                        [0f] = Vector2.Zero,
                        [Duration] = new Vector2(ActiveUVScroll.ScrollU * Duration, ActiveUVScroll.ScrollV * Duration)
                    };

                    if (UVAnimation == null)
                    {
                        UVAnimation = ConvertedModel.CreateAnimation("UVScroll");
                    }

                    string PointerPath = $"/materials/{UVMaterialIndex}/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset";
                    UVAnimation.DangerousCreatePointerChannel(PointerPath, OffsetHack, linear: true);
                }
            }

        }

        private static ModelRoot CompleteRsmMeshMetadata(ModelRoot ConvertedModel, RsmDocument CuldceptModel)
        {
            // 6b. Attach Primitive Group Extras
            foreach (KeyValuePair<int, RsmMesh> ActiveMeshPair in CuldceptModel.DicMeshByNodeIndex)
            {
                Mesh ActiveLogicalMesh = ConvertedModel.LogicalMeshes.FirstOrDefault(Mesh => Mesh.Name == $"mesh_{ActiveMeshPair.Key}");
                if (ActiveLogicalMesh != null)
                {
                    RsmMesh ActiveMesh = ActiveMeshPair.Value;

                    for (int G = 0; G < ActiveMesh.ListMeshGroup.Count && G < ActiveLogicalMesh.Primitives.Count; G++)
                    {
                        RsmMeshGroup ActiveMeshGroup = ActiveMesh.ListMeshGroup[G];
                        Dictionary<string, object> DicRsmExtrasByName = new Dictionary<string, object>
                        {
                            ["rsmGroup"] = new Dictionary<string, object>
                            {
                                ["vertexFormat"] = ActiveMeshGroup.VertexFormat,
                                ["primitiveType"] = ActiveMeshGroup.PrimitiveType,
                                ["unknown10"] = ActiveMeshGroup.Unknown10
                            }
                        };

                        ActiveLogicalMesh.Primitives[G].Extras = JsonNode.Parse(JsonSerializer.Serialize(DicRsmExtrasByName));
                    }
                }
            }

            // NOTE: node extras (rsm.nodeIndex + rsm.geometryPadding) are already attached above by
            // node name; a second pass here used to overwrite them and drop nodeIndex, which the
            // importer relies on to map bones. Removed to keep the node round trip lossless.

            // Preserve the full material list: the game allows duplicate material names, which
            // SharpGLTF collapses into one material, so store the raw list at scene level.
            if (ConvertedModel.DefaultScene != null)
            {
                JsonObject SceneExtras = ConvertedModel.DefaultScene.Extras as JsonObject ?? new JsonObject();
                SceneExtras[RsmMaterialExtras.Key] = RsmMaterialExtras.ToJson(CuldceptModel.ListMaterial);
                ConvertedModel.DefaultScene.Extras = SceneExtras;
            }

            return ConvertedModel;
        }

        internal static string RsmMaterialName(List<RsmMaterial> Materials, int Slot, string Prefix, bool PreserveSlots)
        {
            return PreserveSlots ? $"{Prefix}material_{Slot}_{Materials[Slot].Name}" : Prefix + Materials[Slot].Name;
        }

        internal static void CompleteRsmVertexMeshes(ModelRoot Model, RsmDocument Document, List<RsaClip> Clips,
            bool ReflectZ, string Prefix = "", bool PreserveMaterialSlots = false)
        {
            if (Clips == null)
            {
                return;
            }
            // MeshBuilder drops collapsed triangles and welds equal bind vertices before morph targets exist.
            Dictionary<int, List<VertexTrack>> VertexTracks = ReadVertexTracks(Clips, Document);
            bool FlipWinding = ShouldFlipWinding(Document, ReflectZ, VertexTracks);
            foreach (KeyValuePair<int, List<VertexTrack>> Entry in VertexTracks)
            {
                RsmMesh Source = Document.DicMeshByNodeIndex[Entry.Key];
                Node Node = Model.LogicalNodes.Single(Node => Node.Name == $"{Prefix}node_{Entry.Key}");
                Mesh Mesh = Model.CreateMesh($"{Prefix}mesh_{Entry.Key}");
                Node.Mesh = Mesh;
                foreach (RsmMeshGroup Group in Source.ListMeshGroup)
                {
                    if (Group.ArrayCorner.Length == 0)
                    {
                        continue;
                    }

                    if (Group.PrimitiveType != RsmMeshGroup.TriangleList || Group.ArrayCorner.Length % 3 != 0
                        || Group.MaterialIndex >= Document.ListMaterial.Count)
                    {
                        throw new InvalidDataException("Vertex-frame meshes require complete triangle groups and valid material slots.");
                    }

                    RsmCorner[] Corners = Group.ArrayCorner;
                    MeshPrimitive Primitive = Mesh.CreatePrimitive();
                    Primitive.Material = Model.LogicalMaterials.Single(Material =>
                        Material.Name == RsmMaterialName(Document.ListMaterial, Group.MaterialIndex, Prefix, PreserveMaterialSlots));
                    Vector3[] Positions = Corners.Select(Corner => GetPosition(Source.ListPosition, Corner.PositionId, ReflectZ)).ToArray();
                    Vector3[] Normals = Corners.Select(Corner => GetNormal(Source.ListNormal, Corner.NormalId, ReflectZ)).ToArray();
                    Primitive.SetVertexAccessor("POSITION", Vectors(Positions));
                    Primitive.SetVertexAccessor("NORMAL", Vectors(Normals));
                    Primitive.SetVertexAccessor("TEXCOORD_0", Floats(Corners.Length, DimensionType.VEC2, Corners.SelectMany(Corner =>
                    {
                        Vector2 Uv = GetUv(Source.ListUV, Corner.UvId);
                        return new[] { Uv.X, Uv.Y };
                    })));
                    Primitive.SetVertexAccessor("COLOR_0", Floats(Corners.Length, DimensionType.VEC4, Corners.SelectMany(Corner =>
                    {
                        Vector4 Color = GetColor(Source.ListColor, Corner.ColorId);
                        return new[] { Color.X, Color.Y, Color.Z, Color.W };
                    })));
                    using MemoryStream IndexStream = new MemoryStream();
                    using (BinaryWriter Writer = new BinaryWriter(IndexStream, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        for (uint Index = 0; Index < Corners.Length; Index += 3)
                        {
                            Writer.Write(Index);
                            Writer.Write(Index + (FlipWinding ? 2u : 1u));
                            Writer.Write(Index + (FlipWinding ? 1u : 2u));
                        }
                    }
                    Accessor Indices = Model.CreateAccessor();
                    Indices.SetIndexData(Model.UseBufferView(IndexStream.ToArray()), 0, Corners.Length, IndexEncodingType.UNSIGNED_INT);
                    Primitive.SetIndexAccessor(Indices);
                    int TargetIndex = 0;
                    foreach (RsaVertexFrame Frame in Entry.Value.SelectMany(Track => Track.Frames))
                    {
                        Vector3[] PositionDeltas = Corners.Select((Corner, Index) =>
                            GetPosition(Frame.ArrayPosition, Corner.PositionId, ReflectZ) - Positions[Index]).ToArray();
                        Dictionary<string, Accessor> Targets = new Dictionary<string, Accessor>
                        {
                            ["POSITION"] = Vectors(PositionDeltas)
                        };
                        if (Frame.ArrayNormal.Length > 0)
                        {
                            Targets["NORMAL"] = Vectors(Corners.Select((Corner, Index) =>
                                GetNormal(Frame.ArrayNormal, Corner.NormalId, ReflectZ) - Normals[Index]).ToArray());
                        }

                        Primitive.SetMorphTargetAccessors(TargetIndex++, Targets);
                    }
                    Primitive.Extras = new JsonObject
                    {
                        ["rsmGroup"] = new JsonObject
                        {
                            ["vertexFormat"] = Group.VertexFormat,
                            ["primitiveType"] = Group.PrimitiveType,
                            ["unknown10"] = Group.Unknown10
                        }
                    };
                }
                Mesh.SetMorphWeights(new float[Entry.Value.Sum(Track => Track.Frames.Count)]);
            }

            Accessor Vectors(Vector3[] Values)
            {
                return Floats(Values.Length, DimensionType.VEC3,
                Values.SelectMany(Value => new[] { Value.X, Value.Y, Value.Z }));
            }

            Accessor Floats(int Count, DimensionType Dimensions, IEnumerable<float> Values)
            {
                using MemoryStream Stream = new MemoryStream();
                using (BinaryWriter Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    foreach (float Value in Values)
                    {
                        Writer.Write(Value);
                    }
                }

                Accessor Accessor = Model.CreateAccessor();
                Accessor.SetVertexData(Model.UseBufferView(Stream.ToArray()), 0, Count,
                    new SharpGLTF.Memory.AttributeFormat(Dimensions, EncodingType.FLOAT));
                return Accessor;
            }
        }

        internal static JsonObject CreateMaterialExtras(RsmMaterial Material)
        {
            return new JsonObject()
            {
                ["rsm"] = new JsonObject
                {
                    ["textureSize"] = new JsonArray(Material.TextureWidth, Material.TextureHeight),
                    ["renderMode"] = Material.RenderMode,
                    ["opacity"] = Material.Opacity,
                    ["selfIllumination"] = Material.SelfIllumination,
                    ["uvScrollPerFrame"] = new JsonArray(Material.UvScroll.X, Material.UvScroll.Y)
                }
            };
        }

        private static List<MaterialBuilder> BuildMaterials(List<RsmMaterial> ListMaterial, Dictionary<string, List<byte[]>> DicTexturesByMaterialName)  // name → list of glTF texture indices
        {
            List<MaterialBuilder> ListBuiltMaterial = new List<MaterialBuilder>();

            // We need a side-channel to remember which LogicalTexture indices belong to each .ifl
            // Because MaterialBuilder doesn't expose the final texture index until after ToGltf2(),
            // the cleanest approach with SharpGLTF is:
            //   1. Create every frame as its own MaterialBuilder / image
            //   2. After ToGltf2(), look up the textures by name and write extras.ifl
            //
            // Simpler practical approach used below:
            //   - Primary material uses frame 0
            //   - Extra frames are added as additional images on the same material via a naming convention
            //   - After ToGltf2 we resolve the real indices and write extras.ifl

            if (ListMaterial == null || ListMaterial.Count == 0)
            {
                ListBuiltMaterial.Add(new MaterialBuilder("default")
                    .WithDoubleSide(true)
                    .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, Vector4.One));

                return ListBuiltMaterial;
            }

            foreach (RsmMaterial ActiveMaterial in ListMaterial)
            {
                MaterialBuilder ActiveMaterialBuilder = new MaterialBuilder(ActiveMaterial.Name)
                    .WithDoubleSide(true)
                    .WithMetallicRoughnessShader()
                    .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, Vector4.One);

                if (DicTexturesByMaterialName != null && !string.IsNullOrEmpty(ActiveMaterial.Name))
                {
                    string ActiveMaterialName = ActiveMaterial.Name.ToLower();

                    if (DicTexturesByMaterialName.TryGetValue(ActiveMaterialName, out List<byte[]> ListFrame) && ListFrame.Count > 0)
                    {
                        // Primary (frame 0)
                        ChannelBuilder ActiveChannel = ActiveMaterialBuilder.UseChannel(KnownChannel.BaseColor);
                        ActiveChannel.UseTexture().WithPrimaryImage(ListFrame[0]);

                        // Remember extra frames so we can attach them after ToGltf2
                        if (ActiveMaterialName.EndsWith(".ifl") && ListFrame.Count > 1)
                        {
                            // Store the raw PNGs temporarily on the material via a well-known extras key
                            // (we will turn them into proper textures + extras.ifl after ToGltf2)
                            ActiveMaterialBuilder.Extras = new JsonObject
                            {
                                ["iflPendingFrames"] = new JsonArray(
                                    ListFrame.Skip(1).Select(Frame => JsonValue.Create(Convert.ToBase64String(Frame))).ToArray())
                            };
                        }
                    }
                }

                ListBuiltMaterial.Add(ActiveMaterialBuilder);
            }

            return ListBuiltMaterial;
        }

        private static MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>
            BuildStaticMesh(string MeshName, RsmMesh ActiveMesh, List<MaterialBuilder> ListMaterialBuilder, bool FlipWinding, bool ReflectZ, List<VertexTrack> ListVertexTrack)
        {
            MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> MeshBuilder = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(MeshName);

            // ----- 1. Build unique corners (position, normal, uv[, colour]) -----
            // Key = (posId, nrmId, uvId, colId)  – colour is optional
            Dictionary<(ushort, ushort, ushort, ushort), int> DicUniqueCornerByIndex = new Dictionary<(ushort, ushort, ushort, ushort), int>();
            List<Vector3> ListPosition = new List<Vector3>();
            List<Vector3> ListNormal = new List<Vector3>();
            List<Vector2> ListUV = new List<Vector2>();
            List<Vector4> ListColor = new List<Vector4>();
            bool HasColor = ActiveMesh.ListColor != null && ActiveMesh.ListColor.Count > 0;

            // We also keep the original position / normal ids so morph deltas are easy
            List<ushort> ListPositionID = new List<ushort>();
            List<ushort> ListNormalID = new List<ushort>();

            int GetOrAddVertex(RsmCorner ActiveCorner)
            {
                ushort ColorID = HasColor ? ActiveCorner.ColorId : (ushort)0xFFFF;
                (ushort PositionId, ushort NormalId, ushort UvId, ushort ColorID) Key = (ActiveCorner.PositionId, ActiveCorner.NormalId, ActiveCorner.UvId, ColorID);

                if (DicUniqueCornerByIndex.TryGetValue(Key, out int VertexIndex))
                {
                    return VertexIndex;
                }

                VertexIndex = ListPosition.Count;
                DicUniqueCornerByIndex[Key] = VertexIndex;

                Vector3 Position = GetPosition(ActiveMesh.ListPosition, ActiveCorner.PositionId, ReflectZ);
                Vector3 Normal = GetNormal(ActiveMesh.ListNormal, ActiveCorner.NormalId, ReflectZ);
                Vector2 UV = GetUv(ActiveMesh.ListUV, ActiveCorner.UvId);
                Vector4 Color = HasColor ? GetColor(ActiveMesh.ListColor, ActiveCorner.ColorId) : Vector4.One;

                ListPosition.Add(Position);
                ListNormal.Add(Normal);
                ListUV.Add(UV);
                ListColor.Add(Color);
                ListPositionID.Add(ActiveCorner.PositionId);
                ListNormalID.Add(ActiveCorner.NormalId);

                return VertexIndex;
            }

            // ----- 2. Emit triangles per group -----
            foreach (RsmMeshGroup ActiveMeshGroup in ActiveMesh.ListMeshGroup)
            {
                MaterialBuilder ActiveMaterialBuilder = (ActiveMeshGroup.MaterialIndex < ListMaterialBuilder.Count)
                    ? ListMaterialBuilder[ActiveMeshGroup.MaterialIndex]
                    : ListMaterialBuilder[0];

                PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexColor1Texture1, VertexEmpty> NewPrimitive = MeshBuilder.UsePrimitive(ActiveMaterialBuilder);

                for (int C = 0; C < ActiveMeshGroup.ArrayCorner.Length; C += 3)
                {
                    RsmCorner ActiveCorner1 = ActiveMeshGroup.ArrayCorner[C];
                    RsmCorner ActiveCorner2 = ActiveMeshGroup.ArrayCorner[C + 1];
                    RsmCorner ActiveCorner3 = ActiveMeshGroup.ArrayCorner[C + 2];

                    if (FlipWinding)
                    {
                        (ActiveCorner2, ActiveCorner3) = (ActiveCorner3, ActiveCorner2);
                    }

                    int VertexIndex1 = GetOrAddVertex(ActiveCorner1);
                    int VertexIndex2 = GetOrAddVertex(ActiveCorner2);
                    int VertexIndex3 = GetOrAddVertex(ActiveCorner3);

                    // Build the three VertexBuilders
                    VertexStatic Vertex1 = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                        new VertexPositionNormal(ListPosition[VertexIndex1], ListNormal[VertexIndex1]),
                        new VertexColor1Texture1(ListColor[VertexIndex1], ListUV[VertexIndex1]));

                    VertexStatic Vertex2 = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                        new VertexPositionNormal(ListPosition[VertexIndex2], ListNormal[VertexIndex2]),
                        new VertexColor1Texture1(ListColor[VertexIndex2], ListUV[VertexIndex2]));

                    VertexStatic Vertex3 = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                        new VertexPositionNormal(ListPosition[VertexIndex3], ListNormal[VertexIndex3]),
                        new VertexColor1Texture1(ListColor[VertexIndex3], ListUV[VertexIndex3]));

                    NewPrimitive.AddTriangle(Vertex1, Vertex2, Vertex3);
                }
            }

            // ----- 3. Morph targets -----
            if (ListVertexTrack != null && ListVertexTrack.Count > 0)
            {
                int MorphTargetIndex = 0;
                bool HasNormalTargets = ListVertexTrack.Any(T => T.Frames.Any(F => F.ArrayNormal != null && F.ArrayNormal.Length > 0));

                foreach (VertexTrack ActiveTrack in ListVertexTrack)
                {
                    for (int F = 0; F < ActiveTrack.Frames.Count; F++)
                    {
                        RsaVertexFrame ActiveFrame = ActiveTrack.Frames[F];
                        MorphTargetBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexColor1Texture1> NewMorphTarget = MeshBuilder.UseMorphTarget(MorphTargetIndex++);

                        bool AnyDelta = false;

                        for (int VertexIndex = 0; VertexIndex < ListPosition.Count; VertexIndex++)
                        {
                            ushort PositionID = ListPositionID[VertexIndex];
                            ushort NormalID = ListNormalID[VertexIndex];

                            Vector3 DeltaPosition = ActiveFrame.ArrayPosition[PositionID] - ActiveMesh.ListPosition[PositionID];

                            if (ReflectZ)
                            {
                                DeltaPosition.Z = -DeltaPosition.Z;
                            }

                            Vector3 DeltaNormal = Vector3.Zero;
                            if (HasNormalTargets && ActiveFrame.ArrayNormal != null && NormalID < ActiveFrame.ArrayNormal.Length)
                            {
                                Vector3 FrameNrm = ActiveFrame.ArrayNormal[NormalID];
                                if (ReflectZ)
                                {
                                    FrameNrm.Z = -FrameNrm.Z;
                                }
                                DeltaNormal = FrameNrm - ListNormal[VertexIndex];
                            }

                            if (DeltaPosition.LengthSquared() > 1e-12f || DeltaNormal.LengthSquared() > 1e-12f)
                            {
                                AnyDelta = true;
                            }

                            VertexPositionNormal MeshVertex = new VertexPositionNormal(ListPosition[VertexIndex], ListNormal[VertexIndex]);
                            NewMorphTarget.SetVertexDelta(MeshVertex, (DeltaPosition, DeltaNormal));
                        }

                        // Force the target to stay alive even if every delta was zero
                        if (!AnyDelta && ListPosition.Count > 0)
                        {
                            VertexPositionNormal MeshVertex = new VertexPositionNormal(ListPosition[0], ListNormal[0]);
                            NewMorphTarget.SetVertexDelta(MeshVertex, (new Vector3(1e-6f, 0, 0), Vector3.Zero));
                        }
                    }
                }
            }

            return MeshBuilder;
        }

        private static MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4> BuildSkinnedMesh(string MeshName, RsmMesh ActiveMesh, RsmDocument ActiveModel, List<MaterialBuilder> ListMaterial,
            bool FlipWinding, bool ReflectZ)
        {
            MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4> MeshBuilder = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>(MeshName);

            SkinVertex SkinData = ExtractSkinVertices(ActiveMesh, ActiveModel.ArrayWorldMatrix, ActiveModel.ListNode.Count);
            JointWeight NewJointWeight = BuildJointWeights(ActiveMesh, ActiveModel.ListNode.Count);

            foreach (RsmMeshGroup ActiveGroup in ActiveMesh.ListMeshGroup)
            {
                MaterialBuilder ActiveMaterial;
                if (ActiveGroup.MaterialIndex < ListMaterial.Count)
                {
                    ActiveMaterial = ListMaterial[ActiveGroup.MaterialIndex];
                }
                else
                {
                    ActiveMaterial = ListMaterial[0];
                }

                PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexColor1Texture1, VertexJoints4> NewPrimitive = MeshBuilder.UsePrimitive(ActiveMaterial);

                for (int C = 0; C < ActiveGroup.ArrayCorner.Length; C += 3)
                {
                    RsmCorner ActiveCorner1 = ActiveGroup.ArrayCorner[C];
                    RsmCorner ActiveCorner2 = ActiveGroup.ArrayCorner[C + 1];
                    RsmCorner ActiveCorner3 = ActiveGroup.ArrayCorner[C + 2];

                    if (FlipWinding)
                    {
                        (ActiveCorner2, ActiveCorner3) = (ActiveCorner3, ActiveCorner2);
                    }

                    NewPrimitive.AddTriangle(
                        CreateSkinnedVertex(ActiveMesh, SkinData, NewJointWeight.ArrayJoint, NewJointWeight.ArrayWeight, ActiveCorner1, ReflectZ),
                        CreateSkinnedVertex(ActiveMesh, SkinData, NewJointWeight.ArrayJoint, NewJointWeight.ArrayWeight, ActiveCorner2, ReflectZ),
                        CreateSkinnedVertex(ActiveMesh, SkinData, NewJointWeight.ArrayJoint, NewJointWeight.ArrayWeight, ActiveCorner3, ReflectZ)
                    );
                }
            }

            return MeshBuilder;
        }

        public static SkinVertex ExtractSkinVertices(RsmMesh ActiveMesh, Matrix4x4[] ArrayWorldMatrix, int ListNodeCount)
        {
            int ListPositionCount = ActiveMesh.ListPosition.Count;
            int ListNormalCount = ActiveMesh.ListNormal.Count;

            Vector3[] ArrayBindPosition = new Vector3[ListPositionCount];
            Vector3[] ArrayBindNormal = new Vector3[ListNormalCount];

            for (int I = 0; I < ActiveMesh.ListSkinPositionInfluenceGroup.Count; I++)
            {
                RsmInfluenceGroup ActiveInfluenceGroup = ActiveMesh.ListSkinPositionInfluenceGroup[I];
                Vector3 ActiveBindPosition = Vector3.Zero;

                foreach (RsmInfluence ActiveInfluence in ActiveInfluenceGroup.ListInfluence)
                {
                    if (ActiveInfluence.VertexId < ListPositionCount && ActiveInfluence.BoneIndex < ListNodeCount)
                    {
                        Vector3 Transformed = Vector3.Transform(ActiveMesh.ListPosition[ActiveInfluence.VertexId], ArrayWorldMatrix[ActiveInfluence.BoneIndex]);
                        ActiveBindPosition += Transformed * ActiveInfluence.Weight;
                    }
                }

                ushort TargetVertexId = ActiveInfluenceGroup.ListInfluence[0].VertexId;
                if (TargetVertexId < ListPositionCount)
                {
                    ArrayBindPosition[TargetVertexId] = ActiveBindPosition;
                }
            }

            for (int I = 0; I < ActiveMesh.ListSkinNormalInfluenceGroup.Count; I++)
            {
                RsmInfluenceGroup ActiveInfluenceGroup = ActiveMesh.ListSkinNormalInfluenceGroup[I];
                Vector3 ActiveBindNormal = Vector3.Zero;

                foreach (RsmInfluence ActiveInfluence in ActiveInfluenceGroup.ListInfluence)
                {
                    if (ActiveInfluence.VertexId < ListNormalCount && ActiveInfluence.BoneIndex < ListNodeCount)
                    {
                        Vector3 Transformed = Vector3.TransformNormal(ActiveMesh.ListNormal[ActiveInfluence.VertexId], ArrayWorldMatrix[ActiveInfluence.BoneIndex]);
                        ActiveBindNormal += Transformed * ActiveInfluence.Weight;
                    }
                }

                ushort TargetNormalId = ActiveInfluenceGroup.ListInfluence[0].VertexId;
                if (TargetNormalId < ListNormalCount)
                {
                    ArrayBindNormal[TargetNormalId] = Vector3.Normalize(ActiveBindNormal);
                }
            }

            return new SkinVertex(ArrayBindPosition, ArrayBindNormal);
        }

        private static VertexStatic CreateStaticVertex(RsmMesh ActiveMesh, RsmCorner Corner, bool ReflectZ)
        {
            Vector3 Position = GetPosition(ActiveMesh.ListPosition, Corner.PositionId, ReflectZ);
            Vector3 Normal = GetNormal(ActiveMesh.ListNormal, Corner.NormalId, ReflectZ);
            Vector4 Color = GetColor(ActiveMesh.ListColor, Corner.ColorId);
            Vector2 Uv = GetUv(ActiveMesh.ListUV, Corner.UvId);

            return new VertexStatic(
                new VertexPositionNormal(Position, Normal),
                new VertexColor1Texture1(Color, Uv)
            );
        }

        private static VertexSkinned CreateSkinnedVertex(RsmMesh ActiveMesh, SkinVertex SkinData, Vector4[] ArrayJoint, Vector4[] ArrayWeight, RsmCorner Corner, bool ReflectZ)
        {
            Vector3 Position = GetPosition(SkinData.ArrayBindPosition, Corner.PositionId, ReflectZ);
            Vector3 Normal = GetNormal(SkinData.ArrayBindNormal, Corner.NormalId, ReflectZ);
            Vector4 Color = GetColor(ActiveMesh.ListColor, Corner.ColorId);
            Vector2 UV = GetUv(ActiveMesh.ListUV, Corner.UvId);

            Vector4 Joint = (Corner.PositionId < ArrayJoint.Length) ? ArrayJoint[Corner.PositionId] : Vector4.Zero;
            Vector4 Weight = (Corner.PositionId < ArrayWeight.Length) ? ArrayWeight[Corner.PositionId] : new Vector4(1, 0, 0, 0);

            return new VertexSkinned(
                new VertexPositionNormal(Position, Normal),
                new VertexColor1Texture1(Color, UV),
                new VertexJoints4(
                    ((int)Joint.X, Weight.X),
                    ((int)Joint.Y, Weight.Y),
                    ((int)Joint.Z, Weight.Z),
                    ((int)Joint.W, Weight.W)
                )
            );
        }

        private static JointWeight BuildJointWeights(RsmMesh ActiveMesh, int NodeCount)
        {
            int VertexCount = ActiveMesh.ListPosition.Count;
            Vector4[] ArrayJoint = new Vector4[VertexCount];
            Vector4[] ArrayWeight = new Vector4[VertexCount];

            if (ActiveMesh.ListSkinPositionInfluenceGroup == null)
            {
                return new JointWeight(ArrayJoint, ArrayWeight);
            }

            for (int G = 0; G < ActiveMesh.ListSkinPositionInfluenceGroup.Count; G++)
            {
                RsmInfluenceGroup ActiveInfluenceGroup = ActiveMesh.ListSkinPositionInfluenceGroup[G];

                if (ActiveInfluenceGroup.ListInfluence.Count == 0)
                {
                    continue;
                }

                ushort VertexId = ActiveInfluenceGroup.ListInfluence[0].VertexId;
                if (VertexId >= VertexCount)
                {
                    continue;
                }

                List<RsmInfluence> ListInfluence = new List<RsmInfluence>(ActiveInfluenceGroup.ListInfluence);
                ListInfluence.Sort((Left, Right) => Right.Weight.CompareTo(Left.Weight));

                int ListInfluenceCount = Math.Min(ListInfluence.Count, 4);
                float TotalWeight = 0;
                for (int I = 0; I < ListInfluenceCount; I++)
                {
                    TotalWeight += ListInfluence[I].Weight;
                }

                if (TotalWeight <= 0.0001f)
                {
                    TotalWeight = 1.0f;
                }

                float[] ArrayJointComponent = new float[4];
                float[] ArrayWeightComponent = new float[4];

                for (int I = 0; I < ListInfluenceCount; I++)
                {
                    if (ListInfluence[I].BoneIndex < NodeCount)
                    {
                        ArrayJointComponent[I] = ListInfluence[I].BoneIndex;
                        ArrayWeightComponent[I] = ListInfluence[I].Weight / TotalWeight;
                    }
                }

                ArrayJoint[VertexId] = new Vector4(ArrayJointComponent[0], ArrayJointComponent[1], ArrayJointComponent[2], ArrayJointComponent[3]);
                ArrayWeight[VertexId] = new Vector4(ArrayWeightComponent[0], ArrayWeightComponent[1], ArrayWeightComponent[2], ArrayWeightComponent[3]);
            }

            return new JointWeight(ArrayJoint, ArrayWeight);
        }

        private static bool ShouldFlipWinding(RsmDocument ActiveModel, bool ReflectZ,
            Dictionary<int, List<VertexTrack>> VertexTracks = null)
        {
            float Score = 0.0f;

            foreach (KeyValuePair<int, RsmMesh> ActiveMeshPair in ActiveModel.DicMeshByNodeIndex)
            {
                RsmMesh ActiveMesh = ActiveMeshPair.Value;
                float MeshScore = WindingScore(ActiveMesh, ActiveMesh.ListPosition, ActiveMesh.ListNormal, ReflectZ);
                if (MeshScore == 0 && VertexTracks != null && VertexTracks.TryGetValue(ActiveMeshPair.Key, out List<VertexTrack> Tracks))
                {
                    foreach (RsaVertexFrame Frame in Tracks.SelectMany(Track => Track.Frames))
                    {
                        MeshScore = WindingScore(ActiveMesh, Frame.ArrayPosition,
                            Frame.ArrayNormal.Length > 0 ? Frame.ArrayNormal : ActiveMesh.ListNormal, ReflectZ);
                        if (MeshScore != 0)
                        {
                            break;
                        }
                    }
                }
                Score += MeshScore;
            }

            return Score < 0.0f;
        }

        private static float WindingScore(RsmMesh ActiveMesh, IReadOnlyList<Vector3> ListPosition,
            IReadOnlyList<Vector3> ListNormal, bool ReflectZ)
        {
            float Score = 0;
            foreach (RsmMeshGroup ActiveGroup in ActiveMesh.ListMeshGroup)
            {
                for (int C = 0; C < ActiveGroup.ArrayCorner.Length; C += 3)
                {
                    RsmCorner ActiveCorner1 = ActiveGroup.ArrayCorner[C];
                    RsmCorner ActiveCorner2 = ActiveGroup.ArrayCorner[C + 1];
                    RsmCorner ActiveCorner3 = ActiveGroup.ArrayCorner[C + 2];

                    if (ActiveCorner1.PositionId >= ListPosition.Count || ActiveCorner2.PositionId >= ListPosition.Count || ActiveCorner3.PositionId >= ListPosition.Count
                        || ActiveCorner1.NormalId >= ListNormal.Count || ActiveCorner2.NormalId >= ListNormal.Count || ActiveCorner3.NormalId >= ListNormal.Count)
                    {
                        continue;
                    }

                    Vector3 ActivePosition1 = ListPosition[ActiveCorner1.PositionId];
                    Vector3 ActivePosition2 = ListPosition[ActiveCorner2.PositionId];
                    Vector3 ActivePosition3 = ListPosition[ActiveCorner3.PositionId];

                    if (ReflectZ)
                    {
                        ActivePosition1.Z = -ActivePosition1.Z;
                        ActivePosition2.Z = -ActivePosition2.Z;
                        ActivePosition3.Z = -ActivePosition3.Z;
                    }

                    Vector3 FaceNormal = Vector3.Cross(ActivePosition2 - ActivePosition1, ActivePosition3 - ActivePosition1);
                    if (FaceNormal.LengthSquared() < 1e-8f)
                    {
                        continue;
                    }

                    FaceNormal = Vector3.Normalize(FaceNormal);
                    Vector3 StoredNormal = ListNormal[ActiveCorner1.NormalId] + ListNormal[ActiveCorner2.NormalId] + ListNormal[ActiveCorner3.NormalId];

                    if (ReflectZ)
                    {
                        StoredNormal.Z = -StoredNormal.Z;
                    }

                    if (StoredNormal.LengthSquared() > 1e-8f)
                    {
                        StoredNormal = Vector3.Normalize(StoredNormal);
                        Score += Vector3.Dot(FaceNormal, StoredNormal);
                    }
                }
            }

            return Score;
        }

        private static Matrix4x4 ReflectMatrixZ(Matrix4x4 SourceMatrix)
        {
            return new Matrix4x4(
                 SourceMatrix.M11, SourceMatrix.M12, -SourceMatrix.M13, SourceMatrix.M14,
                 SourceMatrix.M21, SourceMatrix.M22, -SourceMatrix.M23, SourceMatrix.M24,
                -SourceMatrix.M31, -SourceMatrix.M32, SourceMatrix.M33, -SourceMatrix.M34,
                 SourceMatrix.M41, SourceMatrix.M42, -SourceMatrix.M43, SourceMatrix.M44
            );
        }

        private static Vector3 GetPosition(List<Vector3> ListPosition, ushort PositionId, bool ReflectZ)
        {
            if (ListPosition == null || ListPosition.Count == 0)
            {
                return Vector3.Zero;
            }

            Vector3 Position = ListPosition[Math.Min((int)PositionId, ListPosition.Count - 1)];
            if (ReflectZ)
            {
                Position.Z = -Position.Z;
            }

            return Position;
        }

        private static Vector3 GetNormal(List<Vector3> ListNormal, ushort NormalId, bool ReflectZ)
        {
            if (ListNormal == null || ListNormal.Count == 0)
            {
                return Vector3.UnitY;
            }

            Vector3 Normal = ListNormal[Math.Min((int)NormalId, ListNormal.Count - 1)];

            if (ReflectZ)
            {
                Normal.Z = -Normal.Z;
            }

            float Length = Normal.Length();
            return Length > 0.0001f ? Normal / Length : Vector3.UnitY;
        }

        private static Vector3 GetPosition(Vector3[] ArrayPosition, ushort PositionId, bool ReflectZ)
        {
            if (ArrayPosition == null || ArrayPosition.Length == 0)
            {
                return Vector3.Zero;
            }

            Vector3 Position = ArrayPosition[Math.Min((int)PositionId, ArrayPosition.Length - 1)];

            if (ReflectZ)
            {
                Position.Z = -Position.Z;
            }

            return Position;
        }

        private static Vector3 GetNormal(Vector3[] ArrayNormal, ushort NormalId, bool ReflectZ)
        {
            if (ArrayNormal == null || ArrayNormal.Length == 0)
            {
                return Vector3.UnitY;
            }

            Vector3 Normal = ArrayNormal[Math.Min((int)NormalId, ArrayNormal.Length - 1)];

            if (ReflectZ)
            {
                Normal.Z = -Normal.Z;
            }

            float Length = Normal.Length();
            return Length > 0.0001f ? Normal / Length : Vector3.UnitY;
        }

        private static Vector4 GetColor(List<byte[]> ListColor, ushort ColorId)
        {
            if (ListColor == null || ColorId >= ListColor.Count || ListColor[(int)ColorId] == null || ListColor[(int)ColorId].Length < 4)
            {
                return Vector4.One;
            }

            byte[] ColorBytes = ListColor[(int)ColorId];
            return new Vector4(ColorBytes[0] / 255.0f, ColorBytes[1] / 255.0f, ColorBytes[2] / 255.0f, ColorBytes[3] / 255.0f);
        }

        private static Vector2 GetUv(List<Vector2> ListUV, ushort UvId)
        {
            return ListUV != null && ListUV.Count > 0 ? ListUV[Math.Min((int)UvId, ListUV.Count - 1)] : Vector2.Zero;
        }

        private sealed class VertexTrack
        {
            public int ClipIndex;
            public List<RsaVertexFrame> Frames;   // already parsed by RsaExtractor
            public float[] FrameNumbers;          // for convenience
        }

        public struct UVScroll
        {
            public int MaterialIndex;
            public float ScrollU;
            public float ScrollV;

            public UVScroll(int MaterialIndex, float ScrollU, float ScrollV)
            {
                this.MaterialIndex = MaterialIndex;
                this.ScrollU = ScrollU;
                this.ScrollV = ScrollV;
            }
        }

        public struct JointWeight
        {
            public Vector4[] ArrayJoint;
            public Vector4[] ArrayWeight;

            public JointWeight(Vector4[] ArrayJoint, Vector4[] ArrayWeight)
            {
                this.ArrayJoint = ArrayJoint;
                this.ArrayWeight = ArrayWeight;
            }
        }

        public struct SkinVertex
        {
            public Vector3[] ArrayBindPosition;
            public Vector3[] ArrayBindNormal;

            public SkinVertex(Vector3[] ArrayBindPosition, Vector3[] ArrayBindNormal)
            {
                this.ArrayBindPosition = ArrayBindPosition;
                this.ArrayBindNormal = ArrayBindNormal;
            }
        }
    }
}
