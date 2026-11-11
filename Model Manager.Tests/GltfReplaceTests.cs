using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.ExternalModel;
using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.AssetManager.RST;
using NUnit.Framework;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    public class GltfReplaceTests
    {
        [Test]
        public void ReplacementAdoptsTheNewTextureName()
        {
            string Dir = Path.Combine(Path.GetTempPath(), "rsm_replace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            try
            {
                string RstPath = Path.Combine(Dir, "slot.rst");
                string RsmPath = Path.Combine(Dir, "slot.rsm");
                string GlbPath = WriteTexturedTriangle(Dir, "bomber.png", 2, 2);

                File.WriteAllBytes(RstPath, RstWriter.WriteRst(new List<RstRecord>
                {
                    new RstRecord
                    {
                        Name = "npc_old.png",
                        Width = 4,
                        Height = 4,
                        ArgbPixels = new byte[4 * 4 * 4],
                        Tail = 40086
                    }
                }));

                RsmExtractor.ReplaceModelGltf(RsmPath, RstPath, GlbPath);

                RstRecord Record = RstReader.ReadRst(File.ReadAllBytes(RstPath))[0];
                Assert.That(Record.Width, Is.EqualTo(2));
                Assert.That(Record.Height, Is.EqualTo(2));
                Assert.That(Record.NameKey, Is.EqualTo(RstReader.RstNameKey("bomber.png")));
                Assert.That(Record.NameKey2, Is.EqualTo(RstReader.RstSecondNameKey("bomber.png")));
                Assert.That(Record.HeaderWord, Is.EqualTo(RstReader.RstDimensionHash("bomber.png", 2, 2)));
                Assert.That(Record.Tail, Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(Dir, true);
            }
        }

        [Test]
        public void SameNameKeepsItsSecondKeyWhenTheSizeChanges()
        {
            string Dir = Path.Combine(Path.GetTempPath(), "rsm_replace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            try
            {
                string RstPath = Path.Combine(Dir, "slot.rst");
                string RsmPath = Path.Combine(Dir, "slot.rsm");
                string GlbPath = WriteTexturedTriangle(Dir, "bomber.png", 2, 2);
                ushort KeptKey2 = (ushort)(RstReader.RstSecondNameKey("bomber.png") + 1);

                File.WriteAllBytes(RstPath, RstWriter.WriteRst(new List<RstRecord>
                {
                    new RstRecord
                    {
                        Name = "bomber.png",
                        Width = 4,
                        Height = 4,
                        ArgbPixels = new byte[4 * 4 * 4],
                        Tail = 40086,
                        NameKey2 = KeptKey2,
                        UseStoredNameKey2 = true
                    }
                }));

                RsmExtractor.ReplaceModelGltf(RsmPath, RstPath, GlbPath);

                RstRecord Record = RstReader.ReadRst(File.ReadAllBytes(RstPath))[0];
                Assert.That(Record.NameKey, Is.EqualTo(RstReader.RstNameKey("bomber.png")));
                Assert.That(Record.NameKey2, Is.EqualTo(KeptKey2));
                Assert.That(Record.HeaderWord, Is.EqualTo(RstReader.RstDimensionHash("bomber.png", 2, 2)));
                Assert.That(Record.Tail, Is.EqualTo(40086));
            }
            finally
            {
                Directory.Delete(Dir, true);
            }
        }

        [Test]
        public void BombermanTexturesMatchTheirMaterialNames()
        {
            string GlbPath = @"D:\CuldceptSaga\Error Model\Bomberman.glb";
            string OriginalRst = @"D:\CuldceptSaga\RSP_unpacked\DATA00\DATA00_0413.rst";
            if (!File.Exists(GlbPath) || !File.Exists(OriginalRst))
            {
                Assert.Ignore("Bomberman.glb or the original Najaran RST is not on disk.");
            }

            string Dir = Path.Combine(Path.GetTempPath(), "rsm_bomber_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            try
            {
                string RstPath = Path.Combine(Dir, "DATA00_0413.rst");
                string RsmPath = Path.Combine(Dir, "DATA00_0412.rsm");
                File.Copy(OriginalRst, RstPath);

                RsmExtractor.ReplaceModelGltf(RsmPath, RstPath, GlbPath);

                RsmDocument Model = ReadModel(RsmPath);
                List<RstRecord> Records = RstReader.ReadRst(File.ReadAllBytes(RstPath));
                Assert.That(Records, Has.Count.EqualTo(Model.ListMaterial.Count));
                Assert.That(Model.ListNode[0].SiblingIndex, Is.EqualTo((short)1));

                for (int MaterialIndex = 0; MaterialIndex < Model.ListMaterial.Count; MaterialIndex++)
                {
                    string Name = Model.ListMaterial[MaterialIndex].Name;
                    RstRecord Record = Records[MaterialIndex];
                    Assert.That(Record.NameKey, Is.EqualTo(RstReader.RstNameKey(Name)), Name);
                    Assert.That(Record.NameKey2, Is.EqualTo(RstReader.RstSecondNameKey(Name)), Name);
                    Assert.That(Record.HeaderWord, Is.EqualTo(RstReader.RstDimensionHash(Name, Record.Width, Record.Height)), Name);
                    Assert.That(Record.Width, Is.EqualTo(Model.ListMaterial[MaterialIndex].TextureWidth), Name);
                    Assert.That(Record.Height, Is.EqualTo(Model.ListMaterial[MaterialIndex].TextureHeight), Name);
                }
            }
            finally
            {
                Directory.Delete(Dir, true);
            }
        }

        private static RsmDocument ReadModel(string ModelPath)
        {
            using FileStream RsmStream = File.OpenRead(ModelPath);
            using BigEndianBinaryReader Reader = new BigEndianBinaryReader(RsmStream);
            return RSMReader.ParseRsm(Reader);
        }

        private static string WriteTexturedTriangle(string Dir, string MaterialName, int Width, int Height)
        {
            byte[] Argb = new byte[Width * Height * 4];
            for (int ByteIndex = 0; ByteIndex < Argb.Length; ByteIndex += 4)
            {
                Argb[ByteIndex] = 255;
                Argb[ByteIndex + 1] = 20;
                Argb[ByteIndex + 2] = 40;
                Argb[ByteIndex + 3] = 60;
            }

            MaterialBuilder Material = new MaterialBuilder(MaterialName).WithDoubleSide(true);
            Material.UseChannel(KnownChannel.BaseColor)
                .UseTexture()
                .WithPrimaryImage(RstReader.ConvertArgbToPng((ushort)Width, (ushort)Height, Argb));

            MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Mesh = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(MaterialName);
            Mesh.UsePrimitive(Material).AddTriangle(
                new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                    new VertexPositionNormal(Vector3.Zero, Vector3.UnitY),
                    new VertexColor1Texture1(Vector4.One, Vector2.Zero)),
                new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                    new VertexPositionNormal(Vector3.UnitX, Vector3.UnitY),
                    new VertexColor1Texture1(Vector4.One, Vector2.Zero)),
                new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                    new VertexPositionNormal(Vector3.UnitY, Vector3.UnitY),
                    new VertexColor1Texture1(Vector4.One, Vector2.Zero)));

            SceneBuilder Scene = new SceneBuilder();
            Scene.AddRigidMesh(Mesh, new NodeBuilder("Body"));
            string ModelPath = Path.Combine(Dir, "replace.glb");
            Scene.ToGltf2().SaveGLB(ModelPath);
            return ModelPath;
        }
    }
}
