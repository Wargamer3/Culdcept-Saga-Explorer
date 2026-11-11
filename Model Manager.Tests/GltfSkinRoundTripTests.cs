using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.ExternalModel;
using CuldceptSagaExplorer.AssetManager.RSA;
using CuldceptSagaExplorer.AssetManager.RSM;
using NUnit.Framework;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    /// <summary>
    /// Verifies skinned meshes survive an RSM -> .glb -> RSM round trip. The rsmMesh snapshot
    /// keeps an unedited export byte-identical. Without that snapshot, JOINTS and WEIGHTS rebuild
    /// the bone-local slots SkinNodeVertices bends.
    /// </summary>
    [TestFixture]
    [Category("GltfSkinRoundTrip")]
    public class GltfSkinRoundTripTests
    {
        public static IEnumerable<TestCaseData> SkinnedRsmFiles()
        {
            string CorpusDirectory = Environment.GetEnvironmentVariable("CULDCEPT_RSM_DIRECTORY")
                ?? @"D:\CuldceptSaga\RSP_unpacked";
            if (!Directory.Exists(CorpusDirectory))
            {
                throw new DirectoryNotFoundException($"RSM corpus directory does not exist: {CorpusDirectory}");
            }

            foreach (string ModelFile in Directory.EnumerateFiles(CorpusDirectory, "*.rsm", SearchOption.AllDirectories)
                         .OrderBy(ModelPath => ModelPath, StringComparer.OrdinalIgnoreCase))
            {
                using (FileStream RsmStream = new FileStream(ModelFile, FileMode.Open, FileAccess.Read))
                using (BigEndianBinaryReader Reader = new BigEndianBinaryReader(RsmStream))
                {
                    RsmDocument Document = RSMReader.ParseRsm(Reader);
                    if (Document.DicMeshByNodeIndex.Values.Any(Mesh => Mesh.IsSkinned))
                    {
                        yield return new TestCaseData(ModelFile)
                            .SetName($"SkinRoundTrip({Path.GetFileName(ModelFile)})");
                    }
                }
            }
        }

        [TestCaseSource(nameof(SkinnedRsmFiles))]
        public void SkinnedMeshesRoundTripLosslessly(string ModelPath)
        {
            RsmDocument Before;
            using (FileStream RsmStream = new FileStream(ModelPath, FileMode.Open, FileAccess.Read))
            using (BigEndianBinaryReader Reader = new BigEndianBinaryReader(RsmStream))
            {
                Before = RSMReader.ParseRsm(Reader);
            }

            ModelRoot Root = GltfExporter.CreateGlbFromRSM(Before, new Dictionary<string, List<byte[]>>(), new List<RsaClip>(), 60.0f, true);

            string TempGlb = Path.Combine(Path.GetTempPath(), "rsm_skin_" + Path.GetFileName(ModelPath) + ".glb");
            Root.SaveGLB(TempGlb);

            try
            {
                CuldceptModel Imported = GltfImporter.ImportFromGltf(TempGlb, UndoReflectZ: true);
                RsmDocument After = Imported.Document;

                Assert.That(After.ListMaterial.Count, Is.EqualTo(Before.ListMaterial.Count), "material count");
                Assert.That(After.ListNode.Count, Is.EqualTo(Before.ListNode.Count), "node count");

                int SkinnedChecked = 0;
                foreach (int NodeIndex in Before.DicMeshByNodeIndex.Keys)
                {
                    if (!Before.DicMeshByNodeIndex[NodeIndex].IsSkinned)
                    {
                        continue;
                    }
                    SkinnedChecked++;
                    Assert.That(After.DicMeshByNodeIndex.ContainsKey(NodeIndex), Is.True, $"skinned mesh node {NodeIndex} missing after round trip");
                    AssertMeshEqual(Before.DicMeshByNodeIndex[NodeIndex], After.DicMeshByNodeIndex[NodeIndex], NodeIndex);
                }

                Assert.That(SkinnedChecked, Is.GreaterThan(0), "expected at least one skinned mesh");
            }
            finally
            {
                File.Delete(TempGlb);
            }
        }

        [Test]
        public void JointsRebuildTheBindPoseWithoutTheSnapshot()
        {
            string CorpusDirectory = Environment.GetEnvironmentVariable("CULDCEPT_RSM_DIRECTORY")
                ?? @"D:\CuldceptSaga\RSP_unpacked";
            AssertRebuiltBind(Path.Combine(CorpusDirectory, "DATA06", "DATA06_0244.rsm"));
            AssertRebuiltBind(Path.Combine(CorpusDirectory, "DATA06", "DATA06_0213.rsm"));
        }

        private static void AssertRebuiltBind(string ModelPath)
        {
            RsmDocument Before;
            using (FileStream RsmStream = new FileStream(ModelPath, FileMode.Open, FileAccess.Read))
            using (BigEndianBinaryReader Reader = new BigEndianBinaryReader(RsmStream))
            {
                Before = RSMReader.ParseRsm(Reader);
            }

            ModelRoot Root = GltfExporter.CreateGlbFromRSM(Before, new Dictionary<string, List<byte[]>>(), new List<RsaClip>(), 60.0f, true);
            foreach (Mesh LogicalMesh in Root.LogicalMeshes)
            {
                if (LogicalMesh.Extras is JsonObject Extras && Extras.Remove(RsmMeshExtras.Key))
                {
                    LogicalMesh.Extras = Extras;
                }
            }

            string TempGlb = Path.Combine(Path.GetTempPath(), "rsm_skin_rebuild_" + Path.GetFileName(ModelPath) + ".glb");
            Root.SaveGLB(TempGlb);
            try
            {
                CuldceptModel Imported = GltfImporter.ImportFromGltf(TempGlb, UndoReflectZ: true);
                byte[] Written;
                using (MemoryStream WriteStream = new MemoryStream())
                {
                    using (BigEndianBinaryWriter Writer = new BigEndianBinaryWriter(WriteStream))
                    {
                        RSMWriter.WriteRsm(Writer, Imported.Document);
                    }

                    Written = WriteStream.ToArray();
                }

                RsmDocument After;
                using (MemoryStream ReadStream = new MemoryStream(Written))
                using (BigEndianBinaryReader Reader = new BigEndianBinaryReader(ReadStream))
                {
                    After = RSMReader.ParseRsm(Reader);
                }

                Assert.That(After.ListNode.Count, Is.EqualTo(Before.ListNode.Count), Path.GetFileName(ModelPath) + " node count");
                float Matrix = MaxMatrixError(Before.ArrayWorldMatrix, After.ArrayWorldMatrix);
                Assert.That(Matrix, Is.LessThan(1e-4f), Path.GetFileName(ModelPath) + " bone worlds " + Matrix.ToString("G6"));

                foreach (int NodeIndex in Before.DicMeshByNodeIndex.Keys)
                {
                    RsmMesh Expected = Before.DicMeshByNodeIndex[NodeIndex];
                    if (!Expected.IsSkinned)
                    {
                        continue;
                    }
                    Assert.That(After.DicMeshByNodeIndex.ContainsKey(NodeIndex), Is.True, Path.GetFileName(ModelPath) + " mesh " + NodeIndex);
                    RsmMesh Actual = After.DicMeshByNodeIndex[NodeIndex];
                    Assert.That(Actual.IsSkinned, Is.True, Path.GetFileName(ModelPath) + " mesh " + NodeIndex + " skin");

                    GltfExporter.SkinVertex ExpectedBind = GltfExporter.ExtractSkinVertices(Expected, Before.ArrayWorldMatrix, Before.ListNode.Count);
                    GltfExporter.SkinVertex ActualBind = GltfExporter.ExtractSkinVertices(Actual, After.ArrayWorldMatrix, After.ListNode.Count);
                    List<Vector3> ExpectedPoints = CornerPoints(Expected, ExpectedBind.ArrayBindPosition);
                    List<Vector3> ActualPoints = CornerPoints(Actual, ActualBind.ArrayBindPosition);
                    float Gap = Math.Max(Hausdorff(ExpectedPoints, ActualPoints), Hausdorff(ActualPoints, ExpectedPoints));
                    Assert.That(Gap, Is.LessThan(1e-3f), Path.GetFileName(ModelPath) + " bind " + Gap.ToString("G6"));

                    float Angle = WorstNormal(Expected, ExpectedBind, Actual, ActualBind);
                    Assert.That(Angle, Is.LessThan(0.05f), Path.GetFileName(ModelPath) + " normal degrees " + Angle.ToString("G6"));

                    AssertWeights(Expected, ExpectedBind.ArrayBindPosition, Actual, ActualBind.ArrayBindPosition, Path.GetFileName(ModelPath));
                }
            }
            finally
            {
                File.Delete(TempGlb);
            }
        }

        private static void AssertWeights(RsmMesh Expected, Vector3[] ExpectedBind, RsmMesh Actual, Vector3[] ActualBind, string Context)
        {
            List<List<(int Bone, float Weight)>> Pool = new List<List<(int Bone, float Weight)>>();
            List<Vector3> PoolPos = new List<Vector3>();
            foreach (RsmInfluenceGroup Group in Actual.ListSkinPositionInfluenceGroup)
            {
                if (Group.ListInfluence.Count == 0)
                {
                    continue;
                }
                int VertexId = Group.ListInfluence[0].VertexId;
                if (VertexId >= ActualBind.Length)
                {
                    continue;
                }
                Pool.Add(Bag(Group));
                PoolPos.Add(ActualBind[VertexId]);
            }

            bool[] Used = new bool[Pool.Count];
            foreach (RsmInfluenceGroup Group in Expected.ListSkinPositionInfluenceGroup)
            {
                if (Group.ListInfluence.Count == 0)
                {
                    continue;
                }
                int VertexId = Group.ListInfluence[0].VertexId;
                if (!Referenced(Expected, VertexId))
                {
                    continue;
                }
                Vector3 Point = ExpectedBind[VertexId];
                List<(int Bone, float Weight)> Want = Bag(Group);
                int Found = -1;
                for (int PoolIndex = 0; PoolIndex < Pool.Count; PoolIndex++)
                {
                    if (Used[PoolIndex] || Vector3.Distance(PoolPos[PoolIndex], Point) > 1e-3f)
                    {
                        continue;
                    }
                    if (!SameBag(Pool[PoolIndex], Want))
                    {
                        continue;
                    }
                    Found = PoolIndex;
                    break;
                }
                Assert.That(Found, Is.GreaterThanOrEqualTo(0), Context + " weights at " + Point);
                if (Found >= 0)
                {
                    Used[Found] = true;
                }
            }
        }

        private static bool Referenced(RsmMesh Mesh, int PositionId)
        {
            foreach (RsmMeshGroup Group in Mesh.ListMeshGroup)
            {
                if (Group.ArrayCorner == null)
                {
                    continue;
                }
                foreach (RsmCorner Corner in Group.ArrayCorner)
                {
                    if (Corner.PositionId == PositionId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static List<(int Bone, float Weight)> Bag(RsmInfluenceGroup Group)
        {
            List<(int Bone, float Weight)> Bag = new List<(int Bone, float Weight)>();
            foreach (RsmInfluence Influence in Group.ListInfluence)
            {
                Bag.Add((Influence.BoneIndex, Influence.Weight));
            }

            Bag.Sort((Left, Right) => Left.Bone != Right.Bone ? Left.Bone.CompareTo(Right.Bone) : Left.Weight.CompareTo(Right.Weight));
            return Bag;
        }

        private static bool SameBag(List<(int Bone, float Weight)> Left, List<(int Bone, float Weight)> Right)
        {
            if (Left.Count != Right.Count)
            {
                return false;
            }
            for (int EntryIndex = 0; EntryIndex < Left.Count; EntryIndex++)
            {
                if (Left[EntryIndex].Bone != Right[EntryIndex].Bone || Math.Abs(Left[EntryIndex].Weight - Right[EntryIndex].Weight) > 1e-4f)
                {
                    return false;
                }
            }
            return true;
        }

        private static float WorstNormal(RsmMesh ExpectedMesh, GltfExporter.SkinVertex Expected, RsmMesh ActualMesh, GltfExporter.SkinVertex Actual)
        {
            List<(Vector3 Pos, Vector3 Normal)> ActualPairs = new List<(Vector3 Pos, Vector3 Normal)>();
            foreach (RsmMeshGroup Group in ActualMesh.ListMeshGroup)
            {
                if (Group.ArrayCorner == null)
                {
                    continue;
                }
                foreach (RsmCorner Corner in Group.ArrayCorner)
                {
                    if (Corner.PositionId >= Actual.ArrayBindPosition.Length || Corner.NormalId >= Actual.ArrayBindNormal.Length)
                    {
                        continue;
                    }
                    ActualPairs.Add((Actual.ArrayBindPosition[Corner.PositionId], Actual.ArrayBindNormal[Corner.NormalId]));
                }
            }

            float Worst = 0f;
            HashSet<(int, int)> Seen = new HashSet<(int, int)>();
            foreach (RsmMeshGroup Group in ExpectedMesh.ListMeshGroup)
            {
                if (Group.ArrayCorner == null)
                {
                    continue;
                }
                foreach (RsmCorner Corner in Group.ArrayCorner)
                {
                    if (!Seen.Add((Corner.PositionId, Corner.NormalId)))
                    {
                        continue;
                    }
                    if (Corner.PositionId >= Expected.ArrayBindPosition.Length || Corner.NormalId >= Expected.ArrayBindNormal.Length)
                    {
                        continue;
                    }
                    Vector3 Point = Expected.ArrayBindPosition[Corner.PositionId];
                    Vector3 Normal = Expected.ArrayBindNormal[Corner.NormalId];
                    float Best = 180f;
                    foreach ((Vector3 Position, Vector3 PairNormal) in ActualPairs)
                    {
                        if (Vector3.Distance(Position, Point) > 1e-3f)
                        {
                            continue;
                        }
                        float Angle = AngleDegrees(Normal, PairNormal);
                        if (Angle < Best)
                        {
                            Best = Angle;
                        }
                    }
                    if (Best > Worst)
                    {
                        Worst = Best;
                    }
                }
            }
            return Worst;
        }

        private static float AngleDegrees(Vector3 Left, Vector3 Right)
        {
            float LeftLength = Left.Length();
            float RightLength = Right.Length();
            if (LeftLength < 1e-8f || RightLength < 1e-8f)
            {
                return 180f;
            }
            float Cosine = Vector3.Dot(Left, Right) / (LeftLength * RightLength);
            if (Cosine > 1f)
            {
                Cosine = 1f;
            }
            if (Cosine < -1f)
            {
                Cosine = -1f;
            }
            return (float)(Math.Acos(Cosine) * 180.0 / Math.PI);
        }

        private static List<Vector3> CornerPoints(RsmMesh Mesh, Vector3[] Bind)
        {
            List<Vector3> Points = new List<Vector3>();
            HashSet<int> Seen = new HashSet<int>();
            foreach (RsmMeshGroup Group in Mesh.ListMeshGroup)
            {
                if (Group.ArrayCorner == null)
                {
                    continue;
                }
                foreach (RsmCorner Corner in Group.ArrayCorner)
                {
                    if (Corner.PositionId >= Bind.Length || !Seen.Add(Corner.PositionId))
                    {
                        continue;
                    }
                    Points.Add(Bind[Corner.PositionId]);
                }
            }
            return Points;
        }

        private static float Hausdorff(List<Vector3> From, List<Vector3> To)
        {
            float Max = 0f;
            foreach (Vector3 Point in From)
            {
                float Best = float.MaxValue;
                foreach (Vector3 Other in To)
                {
                    float Distance = Vector3.Distance(Point, Other);
                    if (Distance < Best)
                    {
                        Best = Distance;
                    }
                }
                if (Best > Max)
                {
                    Max = Best;
                }
            }
            return Max;
        }

        private static float MaxMatrixError(Matrix4x4[] Before, Matrix4x4[] After)
        {
            float Max = 0f;
            int Count = Math.Min(Before.Length, After.Length);
            for (int MatrixIndex = 0; MatrixIndex < Count; MatrixIndex++)
            {
                Matrix4x4 BeforeMatrix = Before[MatrixIndex];
                Matrix4x4 AfterMatrix = After[MatrixIndex];
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M11 - AfterMatrix.M11));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M12 - AfterMatrix.M12));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M13 - AfterMatrix.M13));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M14 - AfterMatrix.M14));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M21 - AfterMatrix.M21));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M22 - AfterMatrix.M22));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M23 - AfterMatrix.M23));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M24 - AfterMatrix.M24));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M31 - AfterMatrix.M31));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M32 - AfterMatrix.M32));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M33 - AfterMatrix.M33));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M34 - AfterMatrix.M34));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M41 - AfterMatrix.M41));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M42 - AfterMatrix.M42));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M43 - AfterMatrix.M43));
                Max = Math.Max(Max, Math.Abs(BeforeMatrix.M44 - AfterMatrix.M44));
            }
            return Max;
        }

        private static void AssertMeshEqual(RsmMesh ExpectedMesh, RsmMesh ActualMesh, int NodeIndex)
        {
            string Context = $"mesh[{NodeIndex}]";
            Assert.That(ActualMesh.IsSkinned, Is.True, $"{Context} IsSkinned lost");
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
                Assert.That(ActualGroups[GroupIndex].ListInfluence.Count, Is.EqualTo(ExpectedGroups[GroupIndex].ListInfluence.Count), $"{Context} group[{GroupIndex}] size");
                for (int InfluenceIndex = 0; InfluenceIndex < ExpectedGroups[GroupIndex].ListInfluence.Count; InfluenceIndex++)
                {
                    Assert.That(ActualGroups[GroupIndex].ListInfluence[InfluenceIndex].VertexId, Is.EqualTo(ExpectedGroups[GroupIndex].ListInfluence[InfluenceIndex].VertexId), $"{Context} group[{GroupIndex}][{InfluenceIndex}] VertexId");
                    Assert.That(ActualGroups[GroupIndex].ListInfluence[InfluenceIndex].BoneIndex, Is.EqualTo(ExpectedGroups[GroupIndex].ListInfluence[InfluenceIndex].BoneIndex), $"{Context} group[{GroupIndex}][{InfluenceIndex}] Bone");
                    Assert.That(ActualGroups[GroupIndex].ListInfluence[InfluenceIndex].Weight, Is.EqualTo(ExpectedGroups[GroupIndex].ListInfluence[InfluenceIndex].Weight), $"{Context} group[{GroupIndex}][{InfluenceIndex}] Weight");
                }
            }
        }
    }
}
