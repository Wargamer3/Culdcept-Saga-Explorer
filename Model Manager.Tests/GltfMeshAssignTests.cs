using CuldceptSagaExplorer.AssetManager.ExternalModel;
using CuldceptSagaExplorer.AssetManager.RSM;
using NUnit.Framework;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using System;
using System.IO;
using System.Numerics;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    public class GltfMeshAssignTests
    {
        [Test]
        public void RenamedMeshStaysOnItsNode()
        {
            string ModelPath = Save(Scene =>
            {
                NodeBuilder Node = new NodeBuilder("node_0");
                Scene.AddRigidMesh(Triangle("Body"), Node);
            });

            try
            {
                RsmDocument Document = GltfImporter.ImportFromGltf(ModelPath, UndoReflectZ: true).Document;
                Assert.That(Document.ListNode, Has.Count.EqualTo(1));
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(0), Is.True);
                Assert.That(Document.DicMeshByNodeIndex[0].ListMeshGroup[0].ArrayCorner, Has.Length.EqualTo(3));
            }
            finally
            {
                File.Delete(ModelPath);
            }
        }

        [Test]
        public void NamesWithoutAnUnderscoreStillMatch()
        {
            string ModelPath = Save(Scene =>
            {
                NodeBuilder Node = new NodeBuilder("node0");
                Scene.AddRigidMesh(Triangle("mesh0"), Node);
            });

            try
            {
                RsmDocument Document = GltfImporter.ImportFromGltf(ModelPath, UndoReflectZ: true).Document;
                Assert.That(Document.ListNode, Has.Count.EqualTo(1));
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(0), Is.True);
            }
            finally
            {
                File.Delete(ModelPath);
            }
        }

        [Test]
        public void AddedObjectBecomesAChildNode()
        {
            string ModelPath = Save(Scene =>
            {
                NodeBuilder Body = new NodeBuilder("node_0");
                NodeBuilder Hat = new NodeBuilder("Hat");
                Hat.WithLocalTranslation(new Vector3(1, 2, 3));
                Body.AddNode(Hat);
                Scene.AddRigidMesh(Triangle("Body"), Body);
                Scene.AddRigidMesh(Triangle("HatMesh"), Hat);
            });

            try
            {
                RsmDocument Document = GltfImporter.ImportFromGltf(ModelPath, UndoReflectZ: true).Document;
                Assert.That(Document.ListNode, Has.Count.EqualTo(2));
                Assert.That(Document.ListNode[0].ChildIndex, Is.EqualTo((short)1));
                Assert.That(Document.ListNode[1].SiblingIndex, Is.EqualTo((short)-1));
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(0), Is.True);
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(1), Is.True);
                Assert.That(Document.ListNode[1].Translation.Z, Is.EqualTo(-3f).Within(0.001f));
            }
            finally
            {
                File.Delete(ModelPath);
            }
        }

        [Test]
        public void BlenderFacingSurvivesTheMirror()
        {
            string ModelPath = Save(Scene =>
            {
                NodeBuilder Node = new NodeBuilder("Head");
                Scene.AddRigidMesh(OutwardTriangle("Face"), Node);
            });

            try
            {
                RsmDocument Mirrored = GltfImporter.ImportFromGltf(ModelPath, UndoReflectZ: true).Document;
                RsmCorner[] Flipped = Mirrored.DicMeshByNodeIndex[0].ListMeshGroup[0].ArrayCorner;
                Assert.That(Mirrored.DicMeshByNodeIndex[0].ListPosition[Flipped[1].PositionId].Y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(Mirrored.DicMeshByNodeIndex[0].ListNormal[Flipped[0].NormalId].Z, Is.EqualTo(-1f).Within(0.001f));

                RsmDocument Plain = GltfImporter.ImportFromGltf(ModelPath, UndoReflectZ: false).Document;
                RsmCorner[] Kept = Plain.DicMeshByNodeIndex[0].ListMeshGroup[0].ArrayCorner;
                Assert.That(Plain.DicMeshByNodeIndex[0].ListPosition[Kept[1].PositionId].X, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                File.Delete(ModelPath);
            }
        }

        [Test]
        public void SeparateObjectsStayOnTheRootChain()
        {
            string ModelPath = Save(Scene =>
            {
                NodeBuilder Head = new NodeBuilder("Head");
                NodeBuilder Body = new NodeBuilder("Body");
                Body.WithLocalTranslation(new Vector3(1, 2, 3));
                Scene.AddRigidMesh(Triangle("HeadMesh"), Head);
                Scene.AddRigidMesh(Triangle("BodyMesh"), Body);
            });

            try
            {
                RsmDocument Document = GltfImporter.ImportFromGltf(ModelPath, UndoReflectZ: true).Document;
                Assert.That(Document.ListNode, Has.Count.EqualTo(2));
                Assert.That(Document.ListNode[0].ChildIndex, Is.EqualTo((short)-1));
                Assert.That(Document.ListNode[0].SiblingIndex, Is.EqualTo((short)1));
                Assert.That(Document.ListNode[1].SiblingIndex, Is.EqualTo((short)-1));
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(0), Is.True);
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(1), Is.True);
                Assert.That(Document.ListNode[1].Translation.Z, Is.EqualTo(-3f).Within(0.001f));
            }
            finally
            {
                File.Delete(ModelPath);
            }
        }

        [Test]
        public void MeshExtrasPointAtTheOriginalNode()
        {
            string ModelPath = Save(Scene =>
            {
                NodeBuilder Root = new NodeBuilder("node_0");
                NodeBuilder Hand = new NodeBuilder("node_1");
                Root.AddNode(Hand);
                Scene.AddNode(Root);

                NodeBuilder Carrier = new NodeBuilder("ArmatureMesh");
                Scene.AddRigidMesh(Triangle("Sword"), Carrier);
            });

            try
            {
                ModelRoot Gltf = ModelRoot.Load(ModelPath);
                Mesh Sword = Gltf.LogicalMeshes[0];
                Sword.Name = "Sword";
                Sword.Extras = new System.Text.Json.Nodes.JsonObject
                {
                    ["rsmNodeIndex"] = 1
                };
                Gltf.SaveGLB(ModelPath);

                RsmDocument Document = GltfImporter.ImportFromGltf(ModelPath, UndoReflectZ: true).Document;
                Assert.That(Document.ListNode, Has.Count.EqualTo(2));
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(1), Is.True);
                Assert.That(Document.DicMeshByNodeIndex.ContainsKey(2), Is.False);
            }
            finally
            {
                File.Delete(ModelPath);
            }
        }

        private static string Save(Action<SceneBuilder> Build)
        {
            SceneBuilder Scene = new SceneBuilder();
            Build(Scene);
            string ModelPath = Path.Combine(Path.GetTempPath(), "rsm_mesh_" + Guid.NewGuid().ToString("N") + ".glb");
            Scene.ToGltf2().SaveGLB(ModelPath);
            return ModelPath;
        }

        private static MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> OutwardTriangle(string Name)
        {
            MaterialBuilder Material = new MaterialBuilder("plain");
            MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Mesh = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(Name);
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Primitive = Mesh.UsePrimitive(Material);
            Vector3 Normal = new Vector3(0, 0, 1);
            Primitive.AddTriangle(
                new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                    new VertexPositionNormal(new Vector3(0, 0, 0), Normal),
                    new VertexColor1Texture1(Vector4.One, Vector2.Zero)),
                new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                    new VertexPositionNormal(new Vector3(1, 0, 0), Normal),
                    new VertexColor1Texture1(Vector4.One, Vector2.Zero)),
                new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                    new VertexPositionNormal(new Vector3(0, 1, 0), Normal),
                    new VertexColor1Texture1(Vector4.One, Vector2.Zero)));
            return Mesh;
        }

        private static MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Triangle(string Name)
        {
            MaterialBuilder Material = new MaterialBuilder("plain");
            MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Mesh = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(Name);
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Primitive = Mesh.UsePrimitive(Material);
            Primitive.AddTriangle(
                Vertex(new Vector3(0, 0, 0)),
                Vertex(new Vector3(1, 0, 0)),
                Vertex(new Vector3(0, 1, 0)));
            return Mesh;
        }

        private static VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Vertex(Vector3 Position)
        {
            return new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                new VertexPositionNormal(Position, Vector3.UnitY),
                new VertexColor1Texture1(Vector4.One, Vector2.Zero));
        }
    }
}
