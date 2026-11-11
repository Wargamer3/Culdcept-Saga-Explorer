using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using CuldceptSagaExplorer.AssetManager.RSM;
using SharpGLTF.Schema2;

namespace CuldceptSagaExplorer.AssetManager.ExternalModel
{
    public static class GltfImporter
    {
        /// <summary>
        /// Import a .glb / .gltf produced by GltfExporter. A mesh is matched to the RSM node
        /// it is attached to. extras.rsmNodeIndex and a mesh_N / node_N name are fallbacks.
        /// A mesh on an unrecognized node is appended and parented the same way as in the file.
        /// </summary>
        /// <param name="GltfPath">Path to .glb or .gltf</param>
        /// <param name="UndoReflectZ">
        /// Must match the flag used when the file was exported.
        /// Your exporter defaults to reflectZ = true, so leave this true.
        /// </param>
        public static CuldceptModel ImportFromGltf(string GltfPath, bool UndoReflectZ = true)
        {
            if (!File.Exists(GltfPath))
            {
                throw new FileNotFoundException(GltfPath);
            }

            ModelRoot LoadedRoot = ModelRoot.Load(GltfPath);
            ModelManager.GltfExportMetadata.PrepareForNativeReaders(LoadedRoot);

            CuldceptModel ImportedModel = new CuldceptModel();
            RsmDocument ImportedDocument = new RsmDocument();

            // ------------------------------------------------------------------ materials
            ImportMaterials(LoadedRoot, ImportedDocument, ImportedModel.TextureMap);

            // Known skeleton nodes keep their original indices. node_12 and node12 both count.
            Dictionary<int, Node> NodeMap = new Dictionary<int, Node>();
            foreach (Node GltfNode in LoadedRoot.LogicalNodes)
            {
                if (TryNodeIndex(GltfNode, out int NodeIndex))
                {
                    NodeMap[NodeIndex] = GltfNode;
                }
            }

            int NodeCount = NodeMap.Count > 0 ? NodeMap.Keys.Max() + 1 : 0;
            List<RsmNode> RsmNodes = new List<RsmNode>(NodeCount);
            List<Node> GltfByIndex = new List<Node>(NodeCount);
            for (int NodeIndex = 0; NodeIndex < NodeCount; NodeIndex++)
            {
                RsmNodes.Add(CreateNode());
                GltfByIndex.Add(null);
            }

            foreach ((int NodeIndex, Node GltfNode) in NodeMap)
            {
                GltfByIndex[NodeIndex] = GltfNode;
                CopyTransform(GltfNode, RsmNodes[NodeIndex], UndoReflectZ);
            }

            // Geometry follows the node it is attached to. The mesh_N name is only a fallback,
            // so a renamed datablock still lands on node_0. A new object becomes a new node.
            HashSet<int> UsedMeshNodes = new HashSet<int>();
            foreach (Mesh LogicalMesh in LoadedRoot.LogicalMeshes)
            {
                RsmMesh ImportedMesh = ImportMesh(LoadedRoot, LogicalMesh, ImportedDocument.ListMaterial.Count, UndoReflectZ);
                if (ImportedMesh == null)
                {
                    continue;
                }

                Node Carrier = null;
                foreach (Node GltfNode in LoadedRoot.LogicalNodes)
                {
                    if (GltfNode.Mesh != LogicalMesh)
                    {
                        continue;
                    }
                    if (Carrier == null)
                    {
                        Carrier = GltfNode;
                    }
                    if (TryNodeIndex(GltfNode, out _))
                    {
                        Carrier = GltfNode;
                        break;
                    }
                }

                int NodeIndex = ResolveMeshNode(LogicalMesh, Carrier, RsmNodes, GltfByIndex, UsedMeshNodes, UndoReflectZ);
                if (NodeIndex < 0 || !UsedMeshNodes.Add(NodeIndex))
                {
                    continue;
                }

                ImportedDocument.DicMeshByNodeIndex[NodeIndex] = ImportedMesh;
            }

            RebuildChildSibling(GltfByIndex, RsmNodes);
            LinkLooseRoots(RsmNodes);
            ImportedDocument.ListNode.AddRange(RsmNodes);

            ImportedModel.Document = ImportedDocument;
            return ImportedModel;
        }

        private static int ResolveMeshNode(Mesh LogicalMesh, Node Carrier, List<RsmNode> RsmNodes, List<Node> GltfByIndex, HashSet<int> UsedMeshNodes, bool UndoReflectZ)
        {
            if (Carrier != null
                && TryNodeIndex(Carrier, out int FromNode)
                && FromNode < RsmNodes.Count
                && !UsedMeshNodes.Contains(FromNode))
            {
                return FromNode;
            }

            if (TryMeshIndex(LogicalMesh, out int FromMesh)
                && FromMesh < RsmNodes.Count
                && !UsedMeshNodes.Contains(FromMesh))
            {
                AdoptCarrierIfSlotEmpty(Carrier, FromMesh, RsmNodes, GltfByIndex, UndoReflectZ);
                return FromMesh;
            }

            if (Carrier != null
                && TryParseIndexedName(Carrier.Name, "skinmesh", out int FromSkin)
                && FromSkin < RsmNodes.Count
                && !UsedMeshNodes.Contains(FromSkin))
            {
                return FromSkin;
            }

            if (Carrier == null || GltfByIndex.Contains(Carrier))
            {
                return -1;
            }

            int Appended = RsmNodes.Count;
            RsmNodes.Add(CreateNode());
            GltfByIndex.Add(Carrier);
            CopyTransform(Carrier, RsmNodes[Appended], UndoReflectZ);
            return Appended;
        }

        private static void AdoptCarrierIfSlotEmpty(Node Carrier, int NodeIndex, List<RsmNode> RsmNodes, List<Node> GltfByIndex, bool UndoReflectZ)
        {
            if (Carrier == null || GltfByIndex[NodeIndex] != null || GltfByIndex.Contains(Carrier))
            {
                return;
            }

            GltfByIndex[NodeIndex] = Carrier;
            CopyTransform(Carrier, RsmNodes[NodeIndex], UndoReflectZ);
        }

        private static RsmNode CreateNode()
        {
            return new RsmNode
            {
                ChildIndex = -1,
                SiblingIndex = -1,
                GeometryOffset = 0,
                Scale = Vector3.One,
                Rotation = Quaternion.Identity,
                Translation = Vector3.Zero
            };
        }

        private static void CopyTransform(Node GltfNode, RsmNode TargetNode, bool UndoReflectZ)
        {
            Vector3 Translation = GltfNode.LocalTransform.Translation;
            Quaternion Rotation = GltfNode.LocalTransform.Rotation;
            Vector3 Scale = GltfNode.LocalTransform.Scale;

            if (UndoReflectZ)
            {
                Translation.Z = -Translation.Z;
                // exporter did: (x, y, -z, w)  →  undo by flipping Z again
                Rotation = Quaternion.Normalize(new Quaternion(Rotation.X, Rotation.Y, -Rotation.Z, Rotation.W));
            }
            else
            {
                // exporter stored the inverted game quaternion: (-x,-y,-z,w)
                // game expects the inverted form, so we must re-invert
                Rotation = Quaternion.Normalize(new Quaternion(-Rotation.X, -Rotation.Y, -Rotation.Z, Rotation.W));
            }

            TargetNode.Translation = Translation;
            TargetNode.Rotation = Rotation;
            TargetNode.Scale = Scale;

            if (GltfNode.Extras is JsonObject Extras
                && Extras.TryGetPropertyValue("rsm", out JsonNode RsmNodeJson)
                && RsmNodeJson is JsonObject RsmObj
                && RsmObj.TryGetPropertyValue("geometryPadding", out JsonNode PadNode)
                && TryReadUInt(PadNode, out uint Padding))
            {
                TargetNode.GeometryPadding = Padding;
            }
        }

        private static bool TryNodeIndex(Node GltfNode, out int NodeIndex)
        {
            NodeIndex = -1;
            if (GltfNode != null && GltfNode.Extras is JsonObject Extras
                && Extras.TryGetPropertyValue("rsm", out JsonNode RsmNodeJson)
                && RsmNodeJson is JsonObject Rsm
                && Rsm.TryGetPropertyValue("nodeIndex", out JsonNode IdxNode)
                && TryReadInt(IdxNode, out NodeIndex))
            {
                return true;
            }

            string NodeName = null;
            if (GltfNode != null)
            {
                NodeName = GltfNode.Name;
            }

            return TryParseIndexedName(NodeName, "node", out NodeIndex);
        }

        private static bool TryMeshIndex(Mesh SourceMesh, out int Index)
        {
            Index = -1;
            if (SourceMesh != null && SourceMesh.Extras is JsonObject Extras
                && Extras.TryGetPropertyValue("rsmNodeIndex", out JsonNode IdxNode)
                && TryReadInt(IdxNode, out Index))
            {
                return true;
            }

            string MeshName = null;
            if (SourceMesh != null)
            {
                MeshName = SourceMesh.Name;
            }

            return TryParseIndexedName(MeshName, "mesh", out Index);
        }

        // "node_12", "node12" and "mesh_0" all yield their trailing index. "Cube" does not.
        private static bool TryParseIndexedName(string Name, string Prefix, out int Index)
        {
            Index = -1;
            if (string.IsNullOrEmpty(Name)
                || !Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int Start = Prefix.Length;
            if (Start < Name.Length && Name[Start] == '_')
            {
                Start++;
            }
            if (Start >= Name.Length)
            {
                return false;
            }

            for (int CharacterIndex = Start; CharacterIndex < Name.Length; CharacterIndex++)
            {
                if (Name[CharacterIndex] < '0' || Name[CharacterIndex] > '9')
                {
                    return false;
                }
            }

            return int.TryParse(Name.AsSpan(Start), out Index);
        }

        private static bool TryReadInt(JsonNode SourceNode, out int Value)
        {
            Value = 0;
            if (SourceNode is JsonValue Json)
            {
                if (Json.TryGetValue(out int IntegerValue))
                {
                    Value = IntegerValue;
                    return Value >= 0;
                }
                if (Json.TryGetValue(out long LongValue) && LongValue >= 0 && LongValue <= int.MaxValue)
                {
                    Value = (int)LongValue;
                    return true;
                }
                if (Json.TryGetValue(out double DoubleValue) && DoubleValue >= 0 && DoubleValue <= int.MaxValue && DoubleValue == Math.Floor(DoubleValue))
                {
                    Value = (int)DoubleValue;
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadUInt(JsonNode SourceNode, out uint Value)
        {
            Value = 0;
            if (!TryReadInt(SourceNode, out int Signed))
            {
                return false;
            }
            Value = (uint)Signed;
            return true;
        }

        // --------------------------------------------------------------------- materials + textures

        private static void ImportMaterials(ModelRoot LoadedRoot, RsmDocument ImportedDocument, Dictionary<string, List<byte[]>> TextureMap)
        {
            foreach (Material GltfMaterial in LoadedRoot.LogicalMaterials)
            {
                RsmMaterial ImportedMaterial = new RsmMaterial
                {
                    Name = GltfMaterial.Name ?? "mat",
                    Opacity = 1f,
                    RenderMode = 0,
                    SelfIllumination = 0f,
                    UvScroll = Vector2.Zero,
                    TextureWidth = 256,
                    TextureHeight = 256
                };

                // extras.rsm
                if (GltfMaterial.Extras is JsonObject Extras
                    && Extras.TryGetPropertyValue("rsm", out JsonNode RsmNodeJson)
                    && RsmNodeJson is JsonObject Rsm)
                {
                    if (Rsm.TryGetPropertyValue("renderMode", out JsonNode Value))
                    {
                        ImportedMaterial.RenderMode = Value.GetValue<uint>();
                    }
                    if (Rsm.TryGetPropertyValue("opacity", out Value))
                    {
                        ImportedMaterial.Opacity = Value.GetValue<float>();
                    }
                    if (Rsm.TryGetPropertyValue("selfIllumination", out Value))
                    {
                        ImportedMaterial.SelfIllumination = Value.GetValue<float>();
                    }
                    if (Rsm.TryGetPropertyValue("uvScrollPerFrame", out Value) && Value is JsonArray Arr && Arr.Count >= 2)
                    {
                        ImportedMaterial.UvScroll = new Vector2(Arr[0].GetValue<float>(), Arr[1].GetValue<float>());
                    }
                    if (Rsm.TryGetPropertyValue("textureSize", out Value) && Value is JsonArray Size && Size.Count >= 2)
                    {
                        ImportedMaterial.TextureWidth = (ushort)Size[0].GetValue<int>();
                        ImportedMaterial.TextureHeight = (ushort)Size[1].GetValue<int>();
                    }
                }

                // base-color texture → PNG bytes
                MaterialChannel? Channel = GltfMaterial.FindChannel("BaseColor");
                if (Channel.HasValue && Channel.Value.Texture != null)
                {
                    Image SourceImage = Channel.Value.Texture.PrimaryImage;
                    if (SourceImage != null)
                    {
                        byte[] Png = SourceImage.Content.Content.ToArray();
                        string Key = ImportedMaterial.Name.ToLowerInvariant();
                        if (!TextureMap.ContainsKey(Key))
                        {
                            TextureMap[Key] = new List<byte[]>();
                        }
                        TextureMap[Key].Add(Png);

                        // update size if we can read the PNG header cheaply
                        if (Png.Length > 24 && Png[0] == 0x89 && Png[1] == 0x50)
                        {
                            // IHDR width/height are big-endian at offset 16
                            ImportedMaterial.TextureWidth = (ushort)((Png[16] << 24) | (Png[17] << 16) | (Png[18] << 8) | Png[19]);
                            ImportedMaterial.TextureHeight = (ushort)((Png[20] << 24) | (Png[21] << 16) | (Png[22] << 8) | Png[23]);
                        }
                    }
                }

                // animated IFL frames stored in extras.ifl
                if (GltfMaterial.Extras is JsonObject Extras2
                    && Extras2.TryGetPropertyValue("ifl", out JsonNode IflNode)
                    && IflNode is JsonObject Ifl
                    && Ifl.TryGetPropertyValue("frames", out JsonNode FramesNode)
                    && FramesNode is JsonArray FrameIndices)
                {
                    string Key = ImportedMaterial.Name.ToLowerInvariant();
                    if (!TextureMap.ContainsKey(Key))
                    {
                        TextureMap[Key] = new List<byte[]>();
                    }
                    // first frame already added above; additional frames live as extra images
                    // (your exporter creates temporary materials / images named name#N)
                    // For a first version we keep whatever was already put in the map.
                }

                ImportedDocument.ListMaterial.Add(ImportedMaterial);
            }

            // Overwrite with the verbatim full list when the exporter stored it (the game allows
            // duplicate material names, which the glTF material table cannot represent).
            if (LoadedRoot.DefaultScene != null && LoadedRoot.DefaultScene.Extras is JsonObject SceneExtras
                && SceneExtras.TryGetPropertyValue(RsmMaterialExtras.Key, out JsonNode MatNode))
            {
                List<RsmMaterial> Restored = RsmMaterialExtras.FromJson(MatNode);
                if (Restored.Count > 0)
                {
                    ImportedDocument.ListMaterial = Restored;
                }
            }

            if (ImportedDocument.ListMaterial.Count == 0)
            {
                ImportedDocument.ListMaterial.Add(new RsmMaterial
                {
                    Name = "default",
                    Opacity = 1f,
                    TextureWidth = 256,
                    TextureHeight = 256
                });
            }
        }

        // --------------------------------------------------------------------- hierarchy

        private static void RebuildChildSibling(List<Node> GltfByIndex, List<RsmNode> RsmNodes)
        {
            // For every glTF node, its VisualChildren are the direct children.
            // We turn that into the classic child + sibling linked list.
            Dictionary<Node, int> IndexOf = new Dictionary<Node, int>();
            for (int NodeIndex = 0; NodeIndex < GltfByIndex.Count; NodeIndex++)
            {
                if (GltfByIndex[NodeIndex] != null)
                {
                    IndexOf[GltfByIndex[NodeIndex]] = NodeIndex;
                }
            }

            for (int NodeIndex = 0; NodeIndex < GltfByIndex.Count; NodeIndex++)
            {
                Node GltfNode = GltfByIndex[NodeIndex];
                if (GltfNode == null)
                {
                    continue;
                }

                List<int> Children = GltfNode.VisualChildren
                    .Where(IndexOf.ContainsKey)
                    .Select(Child => IndexOf[Child])
                    .OrderBy(ChildIndex => ChildIndex)
                    .ToList();

                if (Children.Count == 0)
                {
                    continue;
                }

                RsmNodes[NodeIndex].ChildIndex = (short)Children[0];
                for (int ChildOrdinal = 0; ChildOrdinal < Children.Count - 1; ChildOrdinal++)
                {
                    RsmNodes[Children[ChildOrdinal]].SiblingIndex = (short)Children[ChildOrdinal + 1];
                }
            }
        }

        // The game draws from node 0, following child and then sibling. Objects that
        // share the glTF scene root are not children of each other, so chain them.
        private static void LinkLooseRoots(List<RsmNode> RsmNodes)
        {
            int Count = RsmNodes.Count;
            if (Count < 2)
            {
                return;
            }

            int[] Parent = new int[Count];
            Array.Fill(Parent, -1);
            for (int NodeIndex = 0; NodeIndex < Count; NodeIndex++)
            {
                int Child = RsmNodes[NodeIndex].ChildIndex;
                int Guard = 0;
                while (Child >= 0 && Child < Count && Parent[Child] < 0 && Guard++ < Count)
                {
                    Parent[Child] = NodeIndex;
                    Child = RsmNodes[Child].SiblingIndex;
                }
            }

            int Tail = 0;
            HashSet<int> Seen = new HashSet<int> { 0 };
            int Walk = 0;
            while (RsmNodes[Tail].SiblingIndex >= 0 && RsmNodes[Tail].SiblingIndex < Count &&
                   Seen.Add(RsmNodes[Tail].SiblingIndex) && Walk++ < Count)
            {
                Tail = RsmNodes[Tail].SiblingIndex;
            }

            for (int RootIndex = 1; RootIndex < Count; RootIndex++)
            {
                if (Parent[RootIndex] >= 0 || Seen.Contains(RootIndex))
                {
                    continue;
                }

                RsmNodes[Tail].SiblingIndex = (short)RootIndex;
                Tail = RootIndex;
                Seen.Add(RootIndex);
                while (RsmNodes[Tail].SiblingIndex >= 0 && RsmNodes[Tail].SiblingIndex < Count &&
                       Seen.Add(RsmNodes[Tail].SiblingIndex) && Walk++ < Count)
                {
                    Tail = RsmNodes[Tail].SiblingIndex;
                }
            }
        }

        // --------------------------------------------------------------------- mesh geometry

        private static RsmMesh ImportMesh(ModelRoot LoadedRoot, Mesh LogicalMesh, int MaterialCount, bool UndoReflectZ)
        {
            // An unedited export keeps rsmMesh, and that snapshot wins. It is the only copy of the
            // few vertices whose influences are not one rest point. A file without it, including a
            // Blender export, is rebuilt from JOINTS and WEIGHTS below.
            if (LogicalMesh.Extras is JsonObject MeshExtras
                && MeshExtras.TryGetPropertyValue(RsmMeshExtras.Key, out JsonNode Snapshot)
                && Snapshot != null)
            {
                RsmMesh Restored = RsmMeshExtras.FromJson(Snapshot);
                if (Restored != null)
                {
                    return Restored;
                }
            }

            if (HasSkin(LogicalMesh))
            {
                RsmMesh Skinned = ImportSkinnedMesh(LoadedRoot, LogicalMesh, MaterialCount, UndoReflectZ);
                if (Skinned != null)
                {
                    return Skinned;
                }
            }

            RsmMesh ImportedMesh = new RsmMesh();

            // We rebuild the four tables + corner lists.
            // SharpGLTF already gives us triangulated primitives.
            List<Vector3> PosList = new List<Vector3>();
            List<Vector3> NormList = new List<Vector3>();
            List<Vector2> UvList = new List<Vector2>();
            List<byte[]> ColorList = new List<byte[]>();

            Dictionary<Vector3, ushort> PosMap = new Dictionary<Vector3, ushort>();
            Dictionary<Vector3, ushort> NormMap = new Dictionary<Vector3, ushort>();
            Dictionary<Vector2, ushort> UvMap = new Dictionary<Vector2, ushort>();
            Dictionary<uint, ushort> ColorMap = new Dictionary<uint, ushort>();

            foreach (MeshPrimitive Prim in LogicalMesh.Primitives)
            {
                if (Prim.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                {
                    continue;
                }

                SharpGLTF.Memory.IAccessorArray<Vector3> Positions = null;
                Accessor PositionAccessor = Prim.GetVertexAccessor("POSITION");
                if (PositionAccessor != null)
                {
                    Positions = PositionAccessor.AsVector3Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector3> Normals = null;
                Accessor NormalAccessor = Prim.GetVertexAccessor("NORMAL");
                if (NormalAccessor != null)
                {
                    Normals = NormalAccessor.AsVector3Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector2> Uvs = null;
                Accessor UvAccessor = Prim.GetVertexAccessor("TEXCOORD_0");
                if (UvAccessor != null)
                {
                    Uvs = UvAccessor.AsVector2Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector4> Colors = null;
                Accessor ColorAccessor = Prim.GetVertexAccessor("COLOR_0");
                if (ColorAccessor != null)
                {
                    Colors = ColorAccessor.AsColorArray();
                }

                uint[] Indices = null;
                IList<uint> IndexList = Prim.GetIndices();
                if (IndexList != null)
                {
                    Indices = IndexList.ToArray();
                }

                if (Positions == null || Indices == null)
                {
                    continue;
                }

                int VertCount = Positions.Count;

                // Build per-primitive corner list
                List<RsmCorner> Corners = new List<RsmCorner>(Indices.Length);

                for (int CornerIndex = 0; CornerIndex < Indices.Length; CornerIndex++)
                {
                    int VertexIndex = (int)Indices[CornerIndex];
                    if (VertexIndex < 0 || VertexIndex >= VertCount)
                    {
                        continue;
                    }

                    Vector3 Pos = Positions[VertexIndex];
                    if (UndoReflectZ)
                    {
                        Pos.Z = -Pos.Z;
                    }

                    Vector3 Norm = Normals != null && VertexIndex < Normals.Count
                        ? Normals[VertexIndex]
                        : Vector3.UnitY;
                    if (UndoReflectZ)
                    {
                        Norm.Z = -Norm.Z;
                    }
                    Norm = Vector3.Normalize(Norm);

                    Vector2 Uv = Uvs != null && VertexIndex < Uvs.Count ? Uvs[VertexIndex] : Vector2.Zero;

                    // Colour → RGBA bytes
                    byte[] ColBytes = new byte[] { 255, 255, 255, 255 };
                    if (Colors != null && VertexIndex < Colors.Count)
                    {
                        Vector4 VertexColor = Colors[VertexIndex];
                        ColBytes = new byte[]
                        {
                            (byte)Math.Clamp((int)(VertexColor.X * 255f + 0.5f), 0, 255),
                            (byte)Math.Clamp((int)(VertexColor.Y * 255f + 0.5f), 0, 255),
                            (byte)Math.Clamp((int)(VertexColor.Z * 255f + 0.5f), 0, 255),
                            (byte)Math.Clamp((int)(VertexColor.W * 255f + 0.5f), 0, 255)
                        };
                    }

                    ushort PosId = GetOrAdd(PosMap, PosList, Pos);
                    ushort NormId = GetOrAdd(NormMap, NormList, Norm);
                    ushort UvId = GetOrAdd(UvMap, UvList, Uv);
                    ushort ColId = GetOrAddColor(ColorMap, ColorList, ColBytes);

                    Corners.Add(new RsmCorner
                    {
                        PositionId = PosId,
                        NormalId = NormId,
                        ColorId = ColId,
                        UvId = UvId
                    });
                }

                // Vertex format bitmask (same as your Collada importer)
                uint VertexFormat = 0x01; // position always
                if (Normals != null)
                {
                    VertexFormat |= 0x02;
                }
                if (Colors != null)
                {
                    VertexFormat |= 0x04;
                }
                if (Uvs != null)
                {
                    VertexFormat |= 0x08;
                }

                // New triangles are a triangle list. An exported group keeps its stored primitive and the unread ushort.
                ushort GroupPrimitiveType = RsmMeshGroup.TriangleList, Unknown10 = 0;
                if (Prim.Extras is JsonObject PExtras
                    && PExtras.TryGetPropertyValue("rsmGroup", out JsonNode GNode)
                    && GNode is JsonObject GroupObject)
                {
                    if (GroupObject.TryGetPropertyValue("vertexFormat", out JsonNode Vf))
                    {
                        VertexFormat = Vf.GetValue<uint>();
                    }
                    if (GroupObject.TryGetPropertyValue("primitiveType", out JsonNode Primitive) || GroupObject.TryGetPropertyValue("unknown4", out Primitive))
                    {
                        GroupPrimitiveType = Primitive.GetValue<ushort>();
                    }
                    if (GroupObject.TryGetPropertyValue("unknown10", out JsonNode U10))
                    {
                        Unknown10 = U10.GetValue<ushort>();
                    }
                }

                int MatIdx = Prim.Material != null
                    ? LoadedRoot.LogicalMaterials.IndexOf(Prim.Material)
                    : 0;
                if (MatIdx < 0 || MatIdx >= MaterialCount)
                {
                    MatIdx = 0;
                }

                // Flip winding back if the exporter flipped it
                // (exporter exporter decides at export time; for safety we leave order as-is
                //  unless you later store a flag in extras)

                ImportedMesh.ListMeshGroup.Add(new RsmMeshGroup
                {
                    MaterialIndex = (ushort)MatIdx,
                    VertexFormat = VertexFormat,
                    PrimitiveType = GroupPrimitiveType,
                    Unknown10 = Unknown10,
                    ArrayCorner = Corners.ToArray()
                });

            }

            ImportedMesh.ListPosition = PosList;
            ImportedMesh.ListNormal = NormList;
            ImportedMesh.ListUV = UvList;
            ImportedMesh.ListColor = ColorList;
            ImportedMesh.IsSkinned = false;

            // Mirroring Z reverses which side a triangle faces. A Blender file's winding
            // then points inward, and the game culls the front. Files exported from here
            // already disagree in glTF space, so undoing the mirror makes them agree again.
            if (ImportedMesh.ListMeshGroup.Count > 0 && WindingScore(ImportedMesh) < 0f)
            {
                ReverseWinding(ImportedMesh);
            }

            return ImportedMesh.ListMeshGroup.Count > 0 ? ImportedMesh : null;
        }

        private static bool HasSkin(Mesh SourceMesh)
        {
            foreach (MeshPrimitive Prim in SourceMesh.Primitives)
            {
                if (Prim.GetVertexAccessor("JOINTS_0") != null && Prim.GetVertexAccessor("WEIGHTS_0") != null)
                {
                    return true;
                }
            }
            return false;
        }

        // JOINTS_0 indexes the skin's joint list. The game bends bone-local positions, so each
        // influence stores the bind point in that bone's space. The first record is the one the
        // corners index; SkinNodeVertices overwrites it with the blend.
        private static RsmMesh ImportSkinnedMesh(ModelRoot LoadedRoot, Mesh LogicalMesh, int MaterialCount, bool UndoReflectZ)
        {
            Skin GltfSkin = null;
            foreach (Node GltfNode in LoadedRoot.LogicalNodes)
            {
                if (GltfNode.Mesh == LogicalMesh && GltfNode.Skin != null)
                {
                    GltfSkin = GltfNode.Skin;
                    break;
                }
            }
            if (GltfSkin == null || GltfSkin.JointsCount == 0)
            {
                return null;
            }

            int[] BoneOfJoint = new int[GltfSkin.JointsCount];
            Matrix4x4[] InverseBone = new Matrix4x4[GltfSkin.JointsCount];
            Dictionary<Node, Matrix4x4> WorldCache = new Dictionary<Node, Matrix4x4>();
            bool CanBind = true;
            for (int JointIndex = 0; JointIndex < GltfSkin.JointsCount; JointIndex++)
            {
                Node Joint = GltfSkin.Joints[JointIndex];
                if (Joint == null || !TryNodeIndex(Joint, out int Bone)
                    || !Matrix4x4.Invert(GameWorld(Joint, UndoReflectZ, WorldCache), out Matrix4x4 Inverse))
                {
                    CanBind = false;
                    break;
                }
                BoneOfJoint[JointIndex] = Bone;
                InverseBone[JointIndex] = Inverse;
            }

            RsmMesh ImportedMesh = new RsmMesh();
            List<SkinSlot> Slots = new List<SkinSlot>();
            Dictionary<(int Prim, int Vertex), SkinSlot> SlotOf = new Dictionary<(int Prim, int Vertex), SkinSlot>();
            Dictionary<Vector2, ushort> UvMap = new Dictionary<Vector2, ushort>();
            Dictionary<uint, ushort> ColorMap = new Dictionary<uint, ushort>();
            int PrimIndex = 0;

            foreach (MeshPrimitive Prim in LogicalMesh.Primitives)
            {
                int ThisPrim = PrimIndex++;
                if (Prim.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                {
                    continue;
                }

                SharpGLTF.Memory.IAccessorArray<Vector3> Positions = null;
                Accessor PositionAccessor = Prim.GetVertexAccessor("POSITION");
                if (PositionAccessor != null)
                {
                    Positions = PositionAccessor.AsVector3Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector3> Normals = null;
                Accessor NormalAccessor = Prim.GetVertexAccessor("NORMAL");
                if (NormalAccessor != null)
                {
                    Normals = NormalAccessor.AsVector3Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector2> Uvs = null;
                Accessor UvAccessor = Prim.GetVertexAccessor("TEXCOORD_0");
                if (UvAccessor != null)
                {
                    Uvs = UvAccessor.AsVector2Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector4> Colors = null;
                Accessor ColorAccessor = Prim.GetVertexAccessor("COLOR_0");
                if (ColorAccessor != null)
                {
                    Colors = ColorAccessor.AsColorArray();
                }

                SharpGLTF.Memory.IAccessorArray<Vector4> Joints = null;
                Accessor JointAccessor = Prim.GetVertexAccessor("JOINTS_0");
                if (JointAccessor != null)
                {
                    Joints = JointAccessor.AsVector4Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector4> Weights = null;
                Accessor WeightAccessor = Prim.GetVertexAccessor("WEIGHTS_0");
                if (WeightAccessor != null)
                {
                    Weights = WeightAccessor.AsVector4Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector4> Joints1 = null;
                Accessor JointAccessor1 = Prim.GetVertexAccessor("JOINTS_1");
                if (JointAccessor1 != null)
                {
                    Joints1 = JointAccessor1.AsVector4Array();
                }

                SharpGLTF.Memory.IAccessorArray<Vector4> Weights1 = null;
                Accessor WeightAccessor1 = Prim.GetVertexAccessor("WEIGHTS_1");
                if (WeightAccessor1 != null)
                {
                    Weights1 = WeightAccessor1.AsVector4Array();
                }

                uint[] Indices = null;
                IList<uint> IndexList = Prim.GetIndices();
                if (IndexList != null)
                {
                    Indices = IndexList.ToArray();
                }
                if (Positions == null || Indices == null)
                {
                    continue;
                }

                bool SkinnedPrim = CanBind && Joints != null && Weights != null;
                List<RsmCorner> Corners = new List<RsmCorner>(Indices.Length);
                for (int CornerIndex = 0; CornerIndex < Indices.Length; CornerIndex++)
                {
                    int VertexIndex = (int)Indices[CornerIndex];
                    if (VertexIndex < 0 || VertexIndex >= Positions.Count)
                    {
                        continue;
                    }

                    Vector3 Bind = Positions[VertexIndex];
                    if (UndoReflectZ)
                    {
                        Bind.Z = -Bind.Z;
                    }
                    Vector3 Normal = Normals != null && VertexIndex < Normals.Count ? Normals[VertexIndex] : Vector3.UnitY;
                    if (UndoReflectZ)
                    {
                        Normal.Z = -Normal.Z;
                    }
                    if (Normal.LengthSquared() > 1e-12f)
                    {
                        Normal = Vector3.Normalize(Normal);
                    }
                    else
                    {
                        Normal = Vector3.UnitY;
                    }

                    Vector2 Uv = Uvs != null && VertexIndex < Uvs.Count ? Uvs[VertexIndex] : Vector2.Zero;
                    byte[] Col = new byte[] { 255, 255, 255, 255 };
                    if (Colors != null && VertexIndex < Colors.Count)
                    {
                        Vector4 VertexColor = Colors[VertexIndex];
                        Col = new byte[]
                        {
                            (byte)Math.Clamp((int)(VertexColor.X * 255f + 0.5f), 0, 255),
                            (byte)Math.Clamp((int)(VertexColor.Y * 255f + 0.5f), 0, 255),
                            (byte)Math.Clamp((int)(VertexColor.Z * 255f + 0.5f), 0, 255),
                            (byte)Math.Clamp((int)(VertexColor.W * 255f + 0.5f), 0, 255)
                        };
                    }

                    if (!SlotOf.TryGetValue((ThisPrim, VertexIndex), out SkinSlot Slot))
                    {
                        if (ImportedMesh.ListPosition.Count >= ushort.MaxValue || ImportedMesh.ListNormal.Count >= ushort.MaxValue)
                        {
                            return null;
                        }
                        Slot = new SkinSlot
                        {
                            PositionId = (ushort)ImportedMesh.ListPosition.Count,
                            NormalId = (ushort)ImportedMesh.ListNormal.Count,
                            BindPosition = Bind,
                            BindNormal = Normal
                        };
                        ImportedMesh.ListPosition.Add(Bind);
                        ImportedMesh.ListNormal.Add(Normal);
                        if (SkinnedPrim && VertexIndex < Joints.Count && VertexIndex < Weights.Count)
                        {
                            Slot.Weights = CollectWeights(Joints[VertexIndex], Weights[VertexIndex],
                                Joints1 != null && Weights1 != null && VertexIndex < Joints1.Count && VertexIndex < Weights1.Count,
                                Joints1, Weights1, VertexIndex, BoneOfJoint);
                        }
                        SlotOf[(ThisPrim, VertexIndex)] = Slot;
                        Slots.Add(Slot);
                    }

                    Corners.Add(new RsmCorner
                    {
                        PositionId = Slot.PositionId,
                        NormalId = Slot.NormalId,
                        UvId = GetOrAdd(UvMap, ImportedMesh.ListUV, Uv),
                        ColorId = GetOrAddColor(ColorMap, ImportedMesh.ListColor, Col)
                    });
                }

                if (Corners.Count == 0)
                {
                    continue;
                }

                uint VertexFormat = 0x01 | 0x02;
                if (Colors != null)
                {
                    VertexFormat |= 0x04;
                }
                if (Uvs != null)
                {
                    VertexFormat |= 0x08;
                }
                ushort GroupPrimitiveType = RsmMeshGroup.TriangleList;
                ushort Unknown10 = 0;
                if (Prim.Extras is JsonObject PExtras
                    && PExtras.TryGetPropertyValue("rsmGroup", out JsonNode GNode)
                    && GNode is JsonObject GroupObject)
                {
                    if (GroupObject.TryGetPropertyValue("vertexFormat", out JsonNode Vf) && TryReadUInt(Vf, out uint Format))
                    {
                        VertexFormat = Format;
                    }
                    if ((GroupObject.TryGetPropertyValue("primitiveType", out JsonNode Primitive) || GroupObject.TryGetPropertyValue("unknown4", out Primitive))
                        && TryReadUInt(Primitive, out uint PrimKind))
                    {
                        GroupPrimitiveType = (ushort)PrimKind;
                    }
                    if (GroupObject.TryGetPropertyValue("unknown10", out JsonNode U10) && TryReadUInt(U10, out uint Unread))
                    {
                        Unknown10 = (ushort)Unread;
                    }
                }

                int MatIdx = Prim.Material != null ? LoadedRoot.LogicalMaterials.IndexOf(Prim.Material) : 0;
                if (MatIdx < 0 || MatIdx >= MaterialCount)
                {
                    MatIdx = 0;
                }

                ImportedMesh.ListMeshGroup.Add(new RsmMeshGroup
                {
                    MaterialIndex = (ushort)MatIdx,
                    VertexFormat = VertexFormat,
                    PrimitiveType = GroupPrimitiveType,
                    Unknown10 = Unknown10,
                    ArrayCorner = Corners.ToArray()
                });
            }

            if (ImportedMesh.ListMeshGroup.Count == 0)
            {
                return null;
            }

            if (WindingScore(ImportedMesh) < 0f)
            {
                ReverseWinding(ImportedMesh);
            }

            if (!CanBind)
            {
                return ImportedMesh;
            }

            foreach (SkinSlot Slot in Slots)
            {
                if (Slot.Weights == null || Slot.Weights.Count == 0)
                {
                    Slot.Weights = new List<SkinWeight> { new SkinWeight(BoneOfJoint[0], 1f) };
                }

                RsmInfluenceGroup PositionGroup = new RsmInfluenceGroup();
                RsmInfluenceGroup NormalGroup = new RsmInfluenceGroup();
                for (int WeightIndex = 0; WeightIndex < Slot.Weights.Count; WeightIndex++)
                {
                    SkinWeight Influence = Slot.Weights[WeightIndex];
                    Vector3 LocalPos = Vector3.Transform(Slot.BindPosition, InverseBone[JointOf(BoneOfJoint, Influence.Bone)]);
                    Vector3 LocalNrm = Vector3.TransformNormal(Slot.BindNormal, InverseBone[JointOf(BoneOfJoint, Influence.Bone)]);
                    ushort PosId;
                    ushort NrmId;
                    if (WeightIndex == 0)
                    {
                        PosId = Slot.PositionId;
                        NrmId = Slot.NormalId;
                        ImportedMesh.ListPosition[PosId] = LocalPos;
                        ImportedMesh.ListNormal[NrmId] = LocalNrm;
                    }
                    else
                    {
                        if (ImportedMesh.ListPosition.Count >= ushort.MaxValue || ImportedMesh.ListNormal.Count >= ushort.MaxValue)
                        {
                            return null;
                        }
                        PosId = (ushort)ImportedMesh.ListPosition.Count;
                        NrmId = (ushort)ImportedMesh.ListNormal.Count;
                        ImportedMesh.ListPosition.Add(LocalPos);
                        ImportedMesh.ListNormal.Add(LocalNrm);
                    }
                    PositionGroup.ListInfluence.Add(new RsmInfluence { VertexId = PosId, BoneIndex = (ushort)Influence.Bone, Weight = Influence.Weight });
                    NormalGroup.ListInfluence.Add(new RsmInfluence { VertexId = NrmId, BoneIndex = (ushort)Influence.Bone, Weight = Influence.Weight });
                }
                ImportedMesh.ListSkinPositionInfluenceGroup.Add(PositionGroup);
                ImportedMesh.ListSkinNormalInfluenceGroup.Add(NormalGroup);
            }

            ImportedMesh.IsSkinned = ImportedMesh.ListSkinPositionInfluenceGroup.Count > 0;
            return ImportedMesh;
        }

        // inverseBone is indexed by skin joint, and the weight stores the RSM bone. Find the joint
        // whose bone matches. The first match wins; a skeleton does not reuse a node.
        private static int JointOf(int[] BoneOfJoint, int Bone)
        {
            for (int JointIndex = 0; JointIndex < BoneOfJoint.Length; JointIndex++)
            {
                if (BoneOfJoint[JointIndex] == Bone)
                {
                    return JointIndex;
                }
            }
            return 0;
        }

        private static List<SkinWeight> CollectWeights(Vector4 Joints, Vector4 Weights, bool Second,
            IList<Vector4> Joints1, IList<Vector4> Weights1, int Vertex, int[] BoneOfJoint)
        {
            List<SkinWeight> Found = new List<SkinWeight>(8);
            void Add(float Joint, float Weight)
            {
                if (Weight <= 1e-8f)
                {
                    return;
                }
                int Index = (int)MathF.Round(Joint);
                if (Index < 0 || Index >= BoneOfJoint.Length)
                {
                    return;
                }
                int Bone = BoneOfJoint[Index];
                for (int FoundIndex = 0; FoundIndex < Found.Count; FoundIndex++)
                {
                    if (Found[FoundIndex].Bone != Bone)
                    {
                        continue;
                    }
                    Found[FoundIndex] = new SkinWeight(Bone, Found[FoundIndex].Weight + Weight);
                    return;
                }
                Found.Add(new SkinWeight(Bone, Weight));
            }

            Add(Joints.X, Weights.X);
            Add(Joints.Y, Weights.Y);
            Add(Joints.Z, Weights.Z);
            Add(Joints.W, Weights.W);
            if (Second)
            {
                Vector4 SecondJoints = Joints1[Vertex];
                Vector4 SecondWeights = Weights1[Vertex];
                Add(SecondJoints.X, SecondWeights.X);
                Add(SecondJoints.Y, SecondWeights.Y);
                Add(SecondJoints.Z, SecondWeights.Z);
                Add(SecondJoints.W, SecondWeights.W);
            }
            return Found;
        }

        private static Matrix4x4 GameWorld(Node GltfNode, bool UndoReflectZ, Dictionary<Node, Matrix4x4> Cache)
        {
            return GameWorld(GltfNode, UndoReflectZ, Cache, 0);
        }

        private static Matrix4x4 GameWorld(Node GltfNode, bool UndoReflectZ, Dictionary<Node, Matrix4x4> Cache, int Depth)
        {
            if (Cache.TryGetValue(GltfNode, out Matrix4x4 Cached))
            {
                return Cached;
            }
            if (Depth > 256)
            {
                return Matrix4x4.Identity;
            }

            Matrix4x4 Local = GameLocal(GltfNode, UndoReflectZ);
            Node Parent = GltfNode.VisualParent;
            Matrix4x4 World = Local;
            if (Parent != null && TryNodeIndex(Parent, out _))
            {
                World = Local * GameWorld(Parent, UndoReflectZ, Cache, Depth + 1);
            }
            Cache[GltfNode] = World;
            return World;
        }

        // Same local matrix CreateTransformMatrix builds after CopyTransform undoes the export mirror.
        private static Matrix4x4 GameLocal(Node GltfNode, bool UndoReflectZ)
        {
            Vector3 Translation = GltfNode.LocalTransform.Translation;
            Quaternion Rotation = GltfNode.LocalTransform.Rotation;
            Vector3 Scale = GltfNode.LocalTransform.Scale;
            if (UndoReflectZ)
            {
                Translation.Z = -Translation.Z;
                Rotation = new Quaternion(Rotation.X, Rotation.Y, -Rotation.Z, Rotation.W);
            }
            else
            {
                Rotation = new Quaternion(-Rotation.X, -Rotation.Y, -Rotation.Z, Rotation.W);
            }
            return RSMReader.CreateTransformMatrix(Quaternion.Normalize(Rotation), Scale, Translation);
        }

        private static float WindingScore(RsmMesh ImportedMesh)
        {
            float Score = 0f;
            foreach (RsmMeshGroup Group in ImportedMesh.ListMeshGroup)
            {
                RsmCorner[] Corners = Group.ArrayCorner;
                if (Corners == null)
                {
                    continue;
                }
                for (int CornerIndex = 0; CornerIndex + 2 < Corners.Length; CornerIndex += 3)
                {
                    if (!TryFace(ImportedMesh, Corners[CornerIndex], Corners[CornerIndex + 1], Corners[CornerIndex + 2], out Vector3 Face, out Vector3 Stored))
                    {
                        continue;
                    }
                    Score += Vector3.Dot(Face, Stored);
                }
            }
            return Score;
        }

        private static void ReverseWinding(RsmMesh ImportedMesh)
        {
            foreach (RsmMeshGroup Group in ImportedMesh.ListMeshGroup)
            {
                RsmCorner[] Corners = Group.ArrayCorner;
                if (Corners == null)
                {
                    continue;
                }
                for (int CornerIndex = 0; CornerIndex + 2 < Corners.Length; CornerIndex += 3)
                {
                    RsmCorner Swap = Corners[CornerIndex + 1];
                    Corners[CornerIndex + 1] = Corners[CornerIndex + 2];
                    Corners[CornerIndex + 2] = Swap;
                }
            }
        }

        private static bool TryFace(RsmMesh ImportedMesh, RsmCorner FirstCorner, RsmCorner SecondCorner, RsmCorner ThirdCorner, out Vector3 Face, out Vector3 Stored)
        {
            Face = Vector3.Zero;
            Stored = Vector3.Zero;
            if (FirstCorner.PositionId >= ImportedMesh.ListPosition.Count || SecondCorner.PositionId >= ImportedMesh.ListPosition.Count || ThirdCorner.PositionId >= ImportedMesh.ListPosition.Count
                || FirstCorner.NormalId >= ImportedMesh.ListNormal.Count || SecondCorner.NormalId >= ImportedMesh.ListNormal.Count || ThirdCorner.NormalId >= ImportedMesh.ListNormal.Count)
            {
                return false;
            }

            Vector3 P0 = ImportedMesh.ListPosition[FirstCorner.PositionId];
            Vector3 P1 = ImportedMesh.ListPosition[SecondCorner.PositionId];
            Vector3 P2 = ImportedMesh.ListPosition[ThirdCorner.PositionId];
            Face = Vector3.Cross(P1 - P0, P2 - P0);
            if (Face.LengthSquared() < 1e-8f)
            {
                return false;
            }
            Face = Vector3.Normalize(Face);
            Stored = ImportedMesh.ListNormal[FirstCorner.NormalId] + ImportedMesh.ListNormal[SecondCorner.NormalId] + ImportedMesh.ListNormal[ThirdCorner.NormalId];
            if (Stored.LengthSquared() < 1e-8f)
            {
                return false;
            }
            Stored = Vector3.Normalize(Stored);
            return true;
        }

        private static ushort GetOrAdd(Dictionary<Vector3, ushort> Lookup, List<Vector3> Destination, Vector3 Value)
        {
            // Use a small epsilon key so floating-point noise doesn’t explode the table
            // (optional – for exact round-trips you can keep exact equality)
            if (Lookup.TryGetValue(Value, out ushort Id))
            {
                return Id;
            }
            Id = (ushort)Destination.Count;
            Destination.Add(Value);
            Lookup[Value] = Id;
            return Id;
        }

        private static ushort GetOrAdd(Dictionary<Vector2, ushort> Lookup, List<Vector2> Destination, Vector2 Value)
        {
            if (Lookup.TryGetValue(Value, out ushort Id))
            {
                return Id;
            }
            Id = (ushort)Destination.Count;
            Destination.Add(Value);
            Lookup[Value] = Id;
            return Id;
        }

        private static ushort GetOrAddColor(Dictionary<uint, ushort> Lookup, List<byte[]> Destination, byte[] Rgba)
        {
            uint Key = (uint)(Rgba[0] | (Rgba[1] << 8) | (Rgba[2] << 16) | (Rgba[3] << 24));
            if (Lookup.TryGetValue(Key, out ushort Id))
            {
                return Id;
            }
            Id = (ushort)Destination.Count;
            Destination.Add(Rgba);
            Lookup[Key] = Id;
            return Id;
        }

        private static int IndexOf<T>(this IReadOnlyList<T> Self, T ElementToFind)
        {
            int ElementIndex = 0;
            foreach (T Element in Self)
            {
                if (Equals(Element, ElementToFind))
                {
                    return ElementIndex;
                }
                ElementIndex++;
            }
            return -1;
        }

        private sealed class SkinSlot
        {
            public ushort PositionId;
            public ushort NormalId;
            public Vector3 BindPosition;
            public Vector3 BindNormal;
            public List<SkinWeight> Weights;
        }

        private struct SkinWeight
        {
            public int Bone;
            public float Weight;

            public SkinWeight(int Bone, float Weight)
            {
                this.Bone = Bone;
                this.Weight = Weight;
            }
        }
    }
}
