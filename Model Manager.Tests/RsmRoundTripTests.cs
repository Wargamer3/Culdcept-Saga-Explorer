using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.RSM;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    /// <summary>
    /// Verifies that the RSM serializer is a lossless inverse of the parser across the whole
    /// unpacked corpus: ParseRsm -> WriteRsm -> ParseRsm must reproduce the same document.
    /// </summary>
    [TestFixture]
    [Category("RsmRoundTrip")]
    public class RsmRoundTripTests
    {
        [Test]
        public void UppercaseLittleEndianAndLowercaseBigEndianDecodeTheSameCompleteDocument()
        {
            byte[] Fixture(bool LittleEndian)
            {
                using MemoryStream Stream = new MemoryStream();
                using BinaryWriter Writer = new BinaryWriter(Stream);
                void Short(ushort Value)
                {
                    Writer.Write(LittleEndian ? Value : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(Value));
                }

                void Integer(uint Value)
                {
                    Writer.Write(LittleEndian ? Value : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(Value));
                }

                void Float(float Value)
                {
                    Integer(unchecked((uint)BitConverter.SingleToInt32Bits(Value)));
                }

                Writer.Write(System.Text.Encoding.ASCII.GetBytes(LittleEndian ? "RSM\0" : "rsm\0"));
                Short(0); Short(1); Short(1); Short(2);
                Writer.Write(System.Text.Encoding.ASCII.GetBytes("test.png".PadRight(32, '\0')));
                Writer.Write((ushort)128); Writer.Write((ushort)64);
                Integer(0x102); Float(.25f); Float(.75f); Float(.125f); Float(-.25f);
                for (int Node = 0; Node < 2; Node++)
                {
                    Integer(Node == 0 ? 164u : 0u);
                    Short(Node == 0 ? (ushort)1 : ushort.MaxValue); Short(ushort.MaxValue);
                    Float(Node + .5f); Float(2); Float(-3);
                    Float(0); Float(0); Float(0); Float(1);
                    Float(1); Float(2); Float(3);
                }
                foreach (ushort Count in new ushort[] { 4, 4, 1, 3, 4, 4, 1, 0x2356 })
                {
                    Short(Count);
                }

                foreach (float Value in new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })
                {
                    Float(Value);
                }

                foreach (float Value in new float[] { 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 0 })
                {
                    Float(Value);
                }

                Writer.Write(new byte[] { 32, 64, 128, 255 });
                foreach (float Value in new float[] { 0, 0, .5f, 1, 1, .5f })
                {
                    Float(Value);
                }

                for (int Table = 0; Table < 2; Table++)
                {
                    for (int Entry = 0; Entry < 4; Entry++)
                    {
                        Short((ushort)Math.Max(0, Entry - 1));
                        Short((ushort)(Entry % 2));
                        Short(Entry == 0 ? (ushort)2 : Entry == 1 ? (ushort)0 : (ushort)1);
                        Short(0);
                        Float(Entry == 0 ? .25f : Entry == 1 ? .75f : 1f);
                    }
                }
                Integer(15); Short(RsmMeshGroup.TriangleList); Short(0); Short(3); Short(0x1234);
                for (ushort Corner = 0; Corner < 3; Corner++)
                {
                    Short(Corner); Short(Corner); Short(0); Short(Corner);
                }
                return Stream.ToArray();
            }
            RsmDocument Parse(byte[] Bytes)
            {
                using MemoryStream Stream = new MemoryStream(Bytes);
                using BigEndianBinaryReader Reader = new BigEndianBinaryReader(Stream);
                RsmDocument Document = RSMReader.ParseRsm(Reader);
                Assert.That(Stream.CanRead, Is.True, "Parsing must leave the caller's reader open.");
                return Document;
            }
            RsmDocument BigEndian = Parse(Fixture(false)), LittleEndian = Parse(Fixture(true));
            AssertDocumentsEqual(BigEndian, LittleEndian);
            Assert.That(LittleEndian.ListMaterial[0].TextureWidth, Is.EqualTo(128));
            Assert.That(LittleEndian.ListMaterial[0].TextureHeight, Is.EqualTo(64));
            Assert.That(LittleEndian.ListMaterial[0].RenderMode, Is.EqualTo(0x102));
            Assert.That(LittleEndian.ListNode[0].Translation, Is.EqualTo(new Vector3(.5f, 2, -3)));
            Assert.That(LittleEndian.ArrayParentIndex, Is.EqualTo(new[] { -1, 0 }));
            Assert.That(LittleEndian.DicMeshByNodeIndex[0].ListSkinPositionInfluenceGroup[0].ListInfluence.Count, Is.EqualTo(2));
            foreach (bool Little in new[] { false, true })
            {
                byte[] Bytes = Fixture(Little);
                Assert.That(() => Parse(Bytes.Take(Bytes.Length - 1).ToArray()), Throws.TypeOf<EndOfStreamException>());
                Bytes[6] = 2;
                Assert.That(() => Parse(Bytes), Throws.TypeOf<InvalidDataException>());
            }
        }

        [Test]
        public void ShippedUppercaseModelCanBeWrittenAsCanonicalBigEndianWithoutLosingData()
        {
            string Effects = Environment.GetEnvironmentVariable("CULDCEPT_EFFECT_DIRECTORY")
                ?? @"D:\CuldceptSaga\Culdcept Saga Extracted\data\EFFECT";
            using FileStream Stream = File.OpenRead(Path.Combine(Effects, "EM_306_01.RSM"));
            using BigEndianBinaryReader Reader = new BigEndianBinaryReader(Stream);
            RsmDocument Original = RSMReader.ParseRsm(Reader);
            using MemoryStream Output = new MemoryStream();
            using (BigEndianBinaryWriter Writer = new BigEndianBinaryWriter(Output))
            {
                RSMWriter.WriteRsm(Writer, Original);
            }

            using MemoryStream Canonical = new MemoryStream(Output.ToArray());
            using BigEndianBinaryReader CanonicalReader = new BigEndianBinaryReader(Canonical);
            AssertDocumentsEqual(Original, RSMReader.ParseRsm(CanonicalReader));
        }

        public static IEnumerable<TestCaseData> RsmFiles()
        {
            string CorpusDirectory = Environment.GetEnvironmentVariable("CULDCEPT_RSM_DIRECTORY")
                ?? @"D:\CuldceptSaga\RSP_unpacked";
            if (!Directory.Exists(CorpusDirectory))
            {
                throw new DirectoryNotFoundException($"RSM corpus directory does not exist: {CorpusDirectory}");
            }

            string[] Files = Directory.EnumerateFiles(CorpusDirectory, "*.rsm", SearchOption.AllDirectories)
                .OrderBy(ModelPath => ModelPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (Files.Length == 0)
            {
                throw new InvalidDataException($"No .rsm files were found in {CorpusDirectory}.");
            }

            foreach (string ModelFile in Files)
            {
                yield return new TestCaseData(ModelFile)
                    .SetName($"RoundTripLossless({Path.GetFileName(ModelFile)})");
            }
        }

        [TestCaseSource(nameof(RsmFiles))]
        public void RoundTripIsLossless(string ModelPath)
        {
            byte[] Original = File.ReadAllBytes(ModelPath);

            using (MemoryStream OriginalStream = new MemoryStream(Original))
            using (BigEndianBinaryReader Reader = new BigEndianBinaryReader(OriginalStream))
            {
                RsmDocument Before = RSMReader.ParseRsm(Reader);

                byte[] Rewritten;
                using (MemoryStream OutStream = new MemoryStream())
                {
                    using (BigEndianBinaryWriter Writer = new BigEndianBinaryWriter(OutStream))
                    {
                        RSMWriter.WriteRsm(Writer, Before);
                    }

                    Rewritten = OutStream.ToArray();
                }

                Assert.That(Rewritten.Length, Is.EqualTo(Original.Length),
                    "Serialized byte length changed: {0} -> {1}", Original.Length, Rewritten.Length);

                RsmDocument After;
                using (MemoryStream RewrittenStream = new MemoryStream(Rewritten))
                using (BigEndianBinaryReader Reader2 = new BigEndianBinaryReader(RewrittenStream))
                {
                    After = RSMReader.ParseRsm(Reader2);
                }

                AssertDocumentsEqual(Before, After);
            }
        }

        private static void AssertDocumentsEqual(RsmDocument Expected, RsmDocument Actual)
        {
            Assert.That(Actual.ListMaterial.Count, Is.EqualTo(Expected.ListMaterial.Count), "material count");
            for (int MaterialIndex = 0; MaterialIndex < Expected.ListMaterial.Count; MaterialIndex++)
            {
                AssertMaterialEqual(Expected.ListMaterial[MaterialIndex], Actual.ListMaterial[MaterialIndex], MaterialIndex);
            }

            Assert.That(Actual.ListNode.Count, Is.EqualTo(Expected.ListNode.Count), "node count");
            for (int NodeIndex = 0; NodeIndex < Expected.ListNode.Count; NodeIndex++)
            {
                AssertNodeEqual(Expected.ListNode[NodeIndex], Actual.ListNode[NodeIndex], NodeIndex);
            }

            Assert.That(Actual.DicMeshByNodeIndex.Keys, Is.EquivalentTo(Expected.DicMeshByNodeIndex.Keys), "mesh node indices");
            foreach (int NodeIndex in Expected.DicMeshByNodeIndex.Keys)
            {
                AssertMeshEqual(Expected.DicMeshByNodeIndex[NodeIndex], Actual.DicMeshByNodeIndex[NodeIndex], NodeIndex);
            }
        }

        private static void AssertMaterialEqual(RsmMaterial ExpectedMaterial, RsmMaterial ActualMaterial, int MaterialIndex)
        {
            string Context = $"material[{MaterialIndex}]";
            Assert.That(ActualMaterial.Name, Is.EqualTo(ExpectedMaterial.Name), $"{Context} Name");
            Assert.That(ActualMaterial.TextureWidth, Is.EqualTo(ExpectedMaterial.TextureWidth), $"{Context} TextureWidth");
            Assert.That(ActualMaterial.TextureHeight, Is.EqualTo(ExpectedMaterial.TextureHeight), $"{Context} TextureHeight");
            Assert.That(ActualMaterial.RenderMode, Is.EqualTo(ExpectedMaterial.RenderMode), $"{Context} RenderMode");
            Assert.That(ActualMaterial.Opacity, Is.EqualTo(ExpectedMaterial.Opacity), $"{Context} Opacity");
            Assert.That(ActualMaterial.SelfIllumination, Is.EqualTo(ExpectedMaterial.SelfIllumination), $"{Context} SelfIllumination");
            Assert.That(ActualMaterial.UvScroll, Is.EqualTo(ExpectedMaterial.UvScroll), $"{Context} UvScroll");
        }

        private static void AssertNodeEqual(RsmNode ExpectedNode, RsmNode ActualNode, int NodeIndex)
        {
            string Context = $"node[{NodeIndex}]";
            Assert.That(ActualNode.GeometryOffset, Is.EqualTo(ExpectedNode.GeometryOffset), $"{Context} GeometryOffset");
            Assert.That(ActualNode.ChildIndex, Is.EqualTo(ExpectedNode.ChildIndex), $"{Context} Child");
            Assert.That(ActualNode.SiblingIndex, Is.EqualTo(ExpectedNode.SiblingIndex), $"{Context} Sibling");
            Assert.That(ActualNode.Translation, Is.EqualTo(ExpectedNode.Translation), $"{Context} Translation");
            Assert.That(ActualNode.Rotation, Is.EqualTo(ExpectedNode.Rotation), $"{Context} Quaternion");
            Assert.That(ActualNode.Scale, Is.EqualTo(ExpectedNode.Scale), $"{Context} Scale");
            Assert.That(ActualNode.GeometryPadding, Is.EqualTo(ExpectedNode.GeometryPadding), $"{Context} GeometryPadding");
        }

        private static void AssertMeshEqual(RsmMesh ExpectedMesh, RsmMesh ActualMesh, int NodeIndex)
        {
            string Context = $"mesh[{NodeIndex}]";
            Assert.That(ActualMesh.ListPosition.Count, Is.EqualTo(ExpectedMesh.ListPosition.Count), $"{Context} position count");
            for (int PositionIndex = 0; PositionIndex < ExpectedMesh.ListPosition.Count; PositionIndex++)
            {
                Assert.That(ActualMesh.ListPosition[PositionIndex], Is.EqualTo(ExpectedMesh.ListPosition[PositionIndex]), $"{Context} position[{PositionIndex}]");
            }

            Assert.That(ActualMesh.ListNormal.Count, Is.EqualTo(ExpectedMesh.ListNormal.Count), $"{Context} normal count");
            for (int NormalIndex = 0; NormalIndex < ExpectedMesh.ListNormal.Count; NormalIndex++)
            {
                Assert.That(ActualMesh.ListNormal[NormalIndex], Is.EqualTo(ExpectedMesh.ListNormal[NormalIndex]), $"{Context} normal[{NormalIndex}]");
            }

            Assert.That(ActualMesh.ListColor.Count, Is.EqualTo(ExpectedMesh.ListColor.Count), $"{Context} color count");
            for (int ColorIndex = 0; ColorIndex < ExpectedMesh.ListColor.Count; ColorIndex++)
            {
                CollectionAssert.AreEqual(ExpectedMesh.ListColor[ColorIndex], ActualMesh.ListColor[ColorIndex], $"{Context} color[{ColorIndex}]");
            }

            Assert.That(ActualMesh.ListUV.Count, Is.EqualTo(ExpectedMesh.ListUV.Count), $"{Context} uv count");
            for (int UvIndex = 0; UvIndex < ExpectedMesh.ListUV.Count; UvIndex++)
            {
                Assert.That(ActualMesh.ListUV[UvIndex], Is.EqualTo(ExpectedMesh.ListUV[UvIndex]), $"{Context} uv[{UvIndex}]");
            }

            Assert.That(ActualMesh.IsSkinned, Is.EqualTo(ExpectedMesh.IsSkinned), $"{Context} IsSkinned");

            Assert.That(ActualMesh.ListMeshGroup.Count, Is.EqualTo(ExpectedMesh.ListMeshGroup.Count), $"{Context} group count");
            for (int GroupIndex = 0; GroupIndex < ExpectedMesh.ListMeshGroup.Count; GroupIndex++)
            {
                RsmMeshGroup ExpectedGroup = ExpectedMesh.ListMeshGroup[GroupIndex];
                RsmMeshGroup ActualGroup = ActualMesh.ListMeshGroup[GroupIndex];
                string GroupContext = $"{Context} group[{GroupIndex}]";
                Assert.That(ActualGroup.MaterialIndex, Is.EqualTo(ExpectedGroup.MaterialIndex), $"{GroupContext} MaterialIndex");
                Assert.That(ActualGroup.VertexFormat, Is.EqualTo(ExpectedGroup.VertexFormat), $"{GroupContext} VertexFormat");
                Assert.That(ActualGroup.PrimitiveType, Is.EqualTo(ExpectedGroup.PrimitiveType), $"{GroupContext} PrimitiveType");
                Assert.That(ActualGroup.Unknown10, Is.EqualTo(ExpectedGroup.Unknown10), $"{GroupContext} Unknown10");
                Assert.That(ActualGroup.ArrayCorner.Length, Is.EqualTo(ExpectedGroup.ArrayCorner.Length), $"{GroupContext} corner count");
                for (int CornerIndex = 0; CornerIndex < ExpectedGroup.ArrayCorner.Length; CornerIndex++)
                {
                    Assert.That(ActualGroup.ArrayCorner[CornerIndex], Is.EqualTo(ExpectedGroup.ArrayCorner[CornerIndex]), $"{GroupContext} corner[{CornerIndex}]");
                }
            }

            AssertInfluencesEqual(ExpectedMesh.ListSkinPositionInfluenceGroup, ActualMesh.ListSkinPositionInfluenceGroup, $"{Context} position influences");
            AssertInfluencesEqual(ExpectedMesh.ListSkinNormalInfluenceGroup, ActualMesh.ListSkinNormalInfluenceGroup, $"{Context} normal influences");
        }

        private static void AssertInfluencesEqual(List<RsmInfluenceGroup> ExpectedGroups, List<RsmInfluenceGroup> ActualGroups, string Context)
        {
            Assert.That(ActualGroups.Count, Is.EqualTo(ExpectedGroups.Count), $"{Context} group count");
            for (int GroupIndex = 0; GroupIndex < ExpectedGroups.Count; GroupIndex++)
            {
                List<RsmInfluence> ExpectedInfluences = ExpectedGroups[GroupIndex].ListInfluence;
                List<RsmInfluence> ActualInfluences = ActualGroups[GroupIndex].ListInfluence;
                Assert.That(ActualInfluences.Count, Is.EqualTo(ExpectedInfluences.Count), $"{Context} group[{GroupIndex}] influence count");
                for (int InfluenceIndex = 0; InfluenceIndex < ExpectedInfluences.Count; InfluenceIndex++)
                {
                    Assert.That(ActualInfluences[InfluenceIndex].VertexId, Is.EqualTo(ExpectedInfluences[InfluenceIndex].VertexId), $"{Context} group[{GroupIndex}][{InfluenceIndex}] VertexId");
                    Assert.That(ActualInfluences[InfluenceIndex].BoneIndex, Is.EqualTo(ExpectedInfluences[InfluenceIndex].BoneIndex), $"{Context} group[{GroupIndex}][{InfluenceIndex}] Bone");
                    Assert.That(ActualInfluences[InfluenceIndex].Weight, Is.EqualTo(ExpectedInfluences[InfluenceIndex].Weight), $"{Context} group[{GroupIndex}][{InfluenceIndex}] Weight");
                }
            }
        }
    }
}
