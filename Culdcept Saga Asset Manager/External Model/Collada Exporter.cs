using Assimp;
using CuldceptSagaExplorer.AssetManager.RSM;
using System;
using System.Collections.Generic;
using System.IO;

namespace CuldceptSagaExplorer.AssetManager.ExternalModel
{
    public static class ColladaExporter
    {
        public static event ProgressCallback OnProgress;

        public static bool ExportToCollada(string RSMFilePath, string OutputPath)
        {
            CuldceptModel LoadedModel = CuldceptModel.Load(RSMFilePath);

            string OutputDir = Path.GetDirectoryName(OutputPath);
            TextureExporter.ExportTexturesToDisk(LoadedModel.TextureMap, OutputDir);

            Scene ExportedScene = new Scene();
            ExportedScene.RootNode = new Node("RootNode");

            // 1. Add Materials
            if (LoadedModel.Document.ListMaterial.Count == 0)
            {
                ExportedScene.Materials.Add(new Material { Name = "DefaultMaterial" });
            }
            else
            {
                foreach (RsmMaterial RsmMat in LoadedModel.Document.ListMaterial)
                {
                    Material ExportedMaterial = new Material { Name = RsmMat.Name };

                    // Clean material name (e.g. "a\npc_032_04.png" -> "npc_032_04.png")
                    string CleanName = Path.GetFileName(RsmMat.Name);
                    if (!CleanName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        CleanName += ".png";
                    }

                    // 1. Assign Diffuse Texture Slot for Assimp to write COLLADA <library_images>
                    TextureSlot TexSlot = new TextureSlot(
                            filePath: CleanName,
                            typeSemantic: TextureType.Diffuse,
                            texIndex: 0,
                            mapping: TextureMapping.FromUV,
                            uvIndex: 0,
                            blendFactor: 1.0f,
                            texOp: TextureOperation.Multiply,
                            wrapModeU: TextureWrapMode.Wrap,
                            wrapModeV: TextureWrapMode.Wrap,
                            flags: 0
                        );

                    ExportedMaterial.AddMaterialTexture(ref TexSlot);

                    // 2. Attach Custom Properties
                    ExportedMaterial.AddProperty(new MaterialProperty("UvScrollX", RsmMat.UvScroll.X));
                    ExportedMaterial.AddProperty(new MaterialProperty("UvScrollY", RsmMat.UvScroll.Y));
                    ExportedMaterial.AddProperty(new MaterialProperty("RenderMode", (int)RsmMat.RenderMode));

                    ExportedScene.Materials.Add(ExportedMaterial);
                }
            }

            // 2. Build Meshes
            foreach (KeyValuePair<int, RsmMesh> MeshByNode in LoadedModel.Document.DicMeshByNodeIndex)
            {
                int NodeIndex = MeshByNode.Key;
                RsmMesh SourceMesh = MeshByNode.Value;

                foreach (RsmMeshGroup Group in SourceMesh.ListMeshGroup)
                {
                    Mesh AssimpMesh = new Mesh($"Mesh_{NodeIndex}_Group_{Group.MaterialIndex}", PrimitiveType.Triangle);
                    AssimpMesh.MaterialIndex = Group.MaterialIndex < ExportedScene.Materials.Count ? Group.MaterialIndex : 0;

                    // Temporary unrolled lists for this group
                    List<Vector3D> UnrolledVertices = new List<Vector3D>();
                    List<Vector3D> UnrolledNormals = new List<Vector3D>();
                    List<Vector3D> UnrolledUvs = new List<Vector3D>();

                    // Unroll face corners into 1:1 aligned attribute entries
                    for (int CornerIndex = 0; CornerIndex < Group.ArrayCorner.Length; CornerIndex += 3)
                    {
                        Face Triangle = new Face();

                        for (int CornerOffset = 0; CornerOffset < 3; CornerOffset++)
                        {
                            RsmCorner Corner = Group.ArrayCorner[CornerIndex + CornerOffset];

                            // 1. Position
                            System.Numerics.Vector3 Position = SourceMesh.ListPosition[Corner.PositionId];
                            UnrolledVertices.Add(new Vector3D(Position.X, Position.Y, Position.Z));

                            // 2. Normal
                            if (Corner.NormalId < SourceMesh.ListNormal.Count)
                            {
                                System.Numerics.Vector3 Normal = SourceMesh.ListNormal[Corner.NormalId];
                                UnrolledNormals.Add(new Vector3D(Normal.X, Normal.Y, Normal.Z));
                            }
                            else
                            {
                                UnrolledNormals.Add(new Vector3D(0, 1, 0));
                            }

                            // 3. UV
                            if (Corner.UvId < SourceMesh.ListUV.Count)
                            {
                                System.Numerics.Vector2 Uv = SourceMesh.ListUV[Corner.UvId];
                                UnrolledUvs.Add(new Vector3D(Uv.X, 1.0f - Uv.Y, 0)); // Flip V for standard 3D space
                            }
                            else
                            {
                                UnrolledUvs.Add(new Vector3D(0, 0, 0));
                            }

                            // Index within this unrolled mesh
                            Triangle.Indices.Add(UnrolledVertices.Count - 1);
                        }

                        AssimpMesh.Faces.Add(Triangle);
                    }

                    // Copy aligned arrays directly into Assimp Mesh
                    AssimpMesh.Vertices.AddRange(UnrolledVertices);
                    AssimpMesh.Normals.AddRange(UnrolledNormals);
                    AssimpMesh.TextureCoordinateChannels[0].AddRange(UnrolledUvs);

                    ExportedScene.Meshes.Add(AssimpMesh);

                    // Attach Mesh to Node
                    Node MeshNode = new Node($"Node_{NodeIndex}");
                    MeshNode.MeshIndices.Add(ExportedScene.Meshes.Count - 1);
                    ExportedScene.RootNode.Children.Add(MeshNode);
                }
            }

            // 3. Export to COLLADA (.dae)
            using (AssimpContext Context = new AssimpContext())
            {
                if (!string.IsNullOrEmpty(OutputDir))
                {
                    Directory.CreateDirectory(OutputDir);
                }

                return Context.ExportFile(ExportedScene, OutputPath, "collada");
            }
        }

        public static void ExtractAll(string ExtractionDirectory, string ExportDirectory)
        {
            foreach (string ActiveSubFolder in Directory.GetDirectories(ExtractionDirectory))
            {
                string ActiveSubFolderName = Path.GetFileNameWithoutExtension(ActiveSubFolder);

                foreach (string ActiveFilePath in Directory.GetFiles(ActiveSubFolder, "*.rsm"))
                {
                    OnProgress($"Extracting {ActiveFilePath}");
                    ExportToCollada(ActiveFilePath, Path.Combine(ExportDirectory, ActiveSubFolderName, Path.GetFileNameWithoutExtension(ActiveFilePath) + ".dae"));
                }
            }
        }

        public delegate void ProgressCallback(string Message);
    }
}
