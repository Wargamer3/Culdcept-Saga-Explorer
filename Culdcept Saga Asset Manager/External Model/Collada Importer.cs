using Assimp;
using CuldceptSagaExplorer.AssetManager.RSM;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;

namespace CuldceptSagaExplorer.AssetManager.ExternalModel
{
    public static class RsmColladaImporter
    {
        public static CuldceptModel ImportFromCollada(string ColladaFilePath)
        {
            if (!File.Exists(ColladaFilePath))
            {
                throw new FileNotFoundException($"COLLADA file not found: {ColladaFilePath}");
            }

            string ModelFolder = Path.GetDirectoryName(ColladaFilePath);

            CuldceptModel NewModel = new CuldceptModel();
            RsmDocument NewRSMModel = new RsmDocument();

            using (AssimpContext Context = new AssimpContext())
            {
                // Import scene and triangulate faces to ensure 3-corner polygons
                Scene LoadedScene = Context.ImportFile(ColladaFilePath,
                    PostProcessSteps.Triangulate |
                    PostProcessSteps.GenerateNormals |
                    PostProcessSteps.FlipUVs); // Handles V-coordinate flipping from Blender

                if (LoadedScene == null || !LoadedScene.HasMeshes)
                {
                    throw new Exception($"Failed to parse COLLADA file or no meshes found: {ColladaFilePath}");
                }

                // 1. Process Materials & Textures
                for (int MaterialIndex = 0; MaterialIndex < LoadedScene.MaterialCount; MaterialIndex++)
                {
                    Material AssimpMat = LoadedScene.Materials[MaterialIndex];
                    string RawName = AssimpMat.Name ?? $"Material_{MaterialIndex}";
                    string CleanName = Path.GetFileName(RawName);

                    RsmMaterial ImportedMaterial = new RsmMaterial
                    {
                        Name = CleanName,
                        Opacity = 1.0f,     // CRITICAL: Must default to 1.0f (opaque) instead of 0.0f
                        RenderMode = 0,    // Standard opaque / alpha-test mode
                        SelfIllumination = 0.0f,
                        UvScroll = Vector2.Zero
                    };

                    // Get texture bound to material diffuse slot
                    if (AssimpMat.GetMaterialTexture(TextureType.Diffuse, 0, out TextureSlot Slot))
                    {
                        string TextureFileName = Path.GetFileName(Slot.FilePath);
                        string ImagePath = Path.Combine(ModelFolder, TextureFileName);

                        if (File.Exists(ImagePath))
                        {
                            byte[] ImageBytes = File.ReadAllBytes(ImagePath);
                            string Key = CleanName.ToLower();

                            // Read dimensions to store on material record
                            try
                            {
                                using MemoryStream ImageStream = new MemoryStream(ImageBytes);
                                using Bitmap LoadedBitmap = new Bitmap(ImageStream);
                                ImportedMaterial.TextureWidth = (ushort)LoadedBitmap.Width;
                                ImportedMaterial.TextureHeight = (ushort)LoadedBitmap.Height;
                            }
                            catch
                            {
                                ImportedMaterial.TextureWidth = 256;
                                ImportedMaterial.TextureHeight = 256;
                            }

                            if (!NewModel.TextureMap.ContainsKey(Key))
                            {
                                NewModel.TextureMap[Key] = new List<byte[]>();
                            }

                            NewModel.TextureMap[Key].Add(ImageBytes);
                        }
                    }

                    NewRSMModel.ListMaterial.Add(ImportedMaterial);
                }

                if (NewRSMModel.ListMaterial.Count == 0)
                {
                    NewRSMModel.ListMaterial.Add(new RsmMaterial
                    {
                        Name = "default",
                        Opacity = 1.0f,
                        TextureWidth = 256,
                        TextureHeight = 256
                    });
                }

                // 2. Process Node Hierarchy & Geometry
                ProcessNodeHierarchy(LoadedScene.RootNode, LoadedScene, NewRSMModel);
            }

            NewModel.Document = NewRSMModel;
            return NewModel;
        }

        private static int ProcessNodeHierarchy(Node AssimpNode, Scene LoadedScene, RsmDocument ImportedDocument)
        {
            int NodeIndex = ImportedDocument.ListNode.Count;

            // Decompose transform matrix
            AssimpNode.Transform.Decompose(out Vector3D AScale, out Assimp.Quaternion ARot, out Vector3D APos);

            RsmNode ImportedNode = new RsmNode
            {
                Translation = new Vector3(APos.X, APos.Y, APos.Z),
                // Pre-invert XYZ components so RSMReader's matrix logic restores original rotation
                Rotation = new System.Numerics.Quaternion(-ARot.X, -ARot.Y, -ARot.Z, ARot.W),
                Scale = new Vector3(AScale.X, AScale.Y, AScale.Z),
                ChildIndex = -1,
                SiblingIndex = -1,
                GeometryOffset = 0
            };

            ImportedDocument.ListNode.Add(ImportedNode);

            // Process Meshes on this node
            if (AssimpNode.HasMeshes)
            {
                RsmMesh ImportedMesh = BuildRsmMeshForNode(AssimpNode, LoadedScene, ImportedDocument);
                if (ImportedMesh != null)
                {
                    ImportedDocument.DicMeshByNodeIndex[NodeIndex] = ImportedMesh;
                }
            }

            // Recursively process child nodes and update hierarchy links
            int PrevChildIndex = -1;
            for (int ChildOrdinal = 0; ChildOrdinal < AssimpNode.Children.Count; ChildOrdinal++)
            {
                int ChildIndex = ProcessNodeHierarchy(AssimpNode.Children[ChildOrdinal], LoadedScene, ImportedDocument);

                if (ChildOrdinal == 0)
                {
                    ImportedDocument.ListNode[NodeIndex].ChildIndex = (short)ChildIndex;
                }
                else if (PrevChildIndex != -1)
                {
                    ImportedDocument.ListNode[PrevChildIndex].SiblingIndex = (short)ChildIndex;
                }

                PrevChildIndex = ChildIndex;
            }

            return NodeIndex;
        }

        private static RsmMesh BuildRsmMeshForNode(Node AssimpNode, Scene LoadedScene, RsmDocument ImportedDocument)
        {
            RsmMesh ImportedMesh = new RsmMesh();

            Dictionary<Vector3, ushort> PosLookup = new Dictionary<Vector3, ushort>();
            Dictionary<Vector3, ushort> NormLookup = new Dictionary<Vector3, ushort>();
            Dictionary<Vector2, ushort> UvLookup = new Dictionary<Vector2, ushort>();
            Dictionary<uint, ushort> ColorLookup = new Dictionary<uint, ushort>();

            foreach (int MeshIndex in AssimpNode.MeshIndices)
            {
                if (MeshIndex < 0 || MeshIndex >= LoadedScene.MeshCount)
                {
                    continue;
                }

                Mesh AssimpMesh = LoadedScene.Meshes[MeshIndex];

                // Compute VertexFormat bitmask for Xbox 360 GPU shader fetcher
                uint VertexFormat = 0;
                if (AssimpMesh.HasVertices)
                {
                    VertexFormat |= 0x01; // Position
                }
                if (AssimpMesh.HasNormals)
                {
                    VertexFormat |= 0x02; // Normal
                }
                if (AssimpMesh.HasVertexColors(0))
                {
                    VertexFormat |= 0x04; // Color
                }
                if (AssimpMesh.HasTextureCoords(0))
                {
                    VertexFormat |= 0x08; // UV
                }

                RsmMeshGroup Group = new RsmMeshGroup
                {
                    MaterialIndex = (AssimpMesh.MaterialIndex < ImportedDocument.ListMaterial.Count && AssimpMesh.MaterialIndex >= 0)
                        ? (ushort)AssimpMesh.MaterialIndex
                        : (ushort)0,
                    VertexFormat = VertexFormat,  // CRITICAL: Set vertex declaration bitmask (e.g. 0x0B or 0x0F)
                    PrimitiveType = RsmMeshGroup.TriangleList,
                    Unknown10 = 0
                };

                List<RsmCorner> Corners = new List<RsmCorner>();

                foreach (Face AssimpFace in AssimpMesh.Faces)
                {
                    if (AssimpFace.IndexCount != 3)
                    {
                        continue;
                    }

                    for (int CornerOffset = 0; CornerOffset < 3; CornerOffset++)
                    {
                        int VertIndex = AssimpFace.Indices[CornerOffset];
                        RsmCorner Corner = new RsmCorner();

                        // 1. Position
                        Vector3D Pos3D = AssimpMesh.Vertices[VertIndex];
                        Vector3 Pos = new Vector3(Pos3D.X, Pos3D.Y, Pos3D.Z);
                        if (!PosLookup.TryGetValue(Pos, out ushort PosId))
                        {
                            PosId = (ushort)ImportedMesh.ListPosition.Count;
                            ImportedMesh.ListPosition.Add(Pos);
                            PosLookup[Pos] = PosId;
                        }
                        Corner.PositionId = PosId;

                        // 2. Normal
                        if (AssimpMesh.HasNormals)
                        {
                            Vector3D Norm3D = AssimpMesh.Normals[VertIndex];
                            Vector3 Norm = new Vector3(Norm3D.X, Norm3D.Y, Norm3D.Z);
                            if (!NormLookup.TryGetValue(Norm, out ushort NormId))
                            {
                                NormId = (ushort)ImportedMesh.ListNormal.Count;
                                ImportedMesh.ListNormal.Add(Norm);
                                NormLookup[Norm] = NormId;
                            }
                            Corner.NormalId = NormId;
                        }

                        // 3. UV
                        if (AssimpMesh.HasTextureCoords(0))
                        {
                            Vector3D Uv3D = AssimpMesh.TextureCoordinateChannels[0][VertIndex];
                            Vector2 Uv = new Vector2(Uv3D.X, Uv3D.Y);
                            if (!UvLookup.TryGetValue(Uv, out ushort UvId))
                            {
                                UvId = (ushort)ImportedMesh.ListUV.Count;
                                ImportedMesh.ListUV.Add(Uv);
                                UvLookup[Uv] = UvId;
                            }
                            Corner.UvId = UvId;
                        }

                        // 4. Vertex Color
                        if (AssimpMesh.HasVertexColors(0))
                        {
                            Color4D VertexColor = AssimpMesh.VertexColorChannels[0][VertIndex];
                            uint Argb = ColorToUint(VertexColor);
                            if (!ColorLookup.TryGetValue(Argb, out ushort ColorId))
                            {
                                ColorId = (ushort)ImportedMesh.ListColor.Count;
                                ImportedMesh.ListColor.Add(new byte[] {
                            (byte)((Argb >> 16) & 0xFF),
                            (byte)((Argb >> 8) & 0xFF),
                            (byte)(Argb & 0xFF),
                            (byte)((Argb >> 24) & 0xFF)
                        });
                                ColorLookup[Argb] = ColorId;
                            }
                            Corner.ColorId = ColorId;
                        }

                        Corners.Add(Corner);
                    }
                }

                if (Corners.Count >= 3)
                {
                    Group.ArrayCorner = Corners.ToArray();
                    ImportedMesh.ListMeshGroup.Add(Group);
                }
            }

            return ImportedMesh.ListMeshGroup.Count > 0 ? ImportedMesh : null;
        }

        private static uint ColorToUint(Color4D SourceColor)
        {
            byte Alpha = (byte)(Math.Clamp(SourceColor.A, 0f, 1f) * 255);
            byte Red = (byte)(Math.Clamp(SourceColor.R, 0f, 1f) * 255);
            byte Green = (byte)(Math.Clamp(SourceColor.G, 0f, 1f) * 255);
            byte Blue = (byte)(Math.Clamp(SourceColor.B, 0f, 1f) * 255);

            return (uint)((Alpha << 24) | (Red << 16) | (Green << 8) | Blue);
        }
    }
}
