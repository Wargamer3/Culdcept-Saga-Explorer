using CuldceptSagaExplorer.AssetManager.ExternalModel;
using CuldceptSagaExplorer.AssetManager.RSM;
using NUnit.Framework;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;
using System;
using System.Linq;
using System.Numerics;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    public class MarktPoseTests
    {
        [Test]
        public void ChildWithNoTranslationStaysOnItsParent()
        {
            Matrix4x4[] Local = new[]
            {
                Matrix4x4.CreateTranslation(0, 10, 0),
                Matrix4x4.CreateRotationX(MathF.PI / 2f) * Matrix4x4.CreateTranslation(0, 0, 0)
            };
            Matrix4x4[] World = RSMReader.CalculateWorldMatrices(2, new[] { -1, 0 }, Local);

            Assert.That(World[1].M41, Is.EqualTo(World[0].M41).Within(1e-4), "X");
            Assert.That(World[1].M42, Is.EqualTo(World[0].M42).Within(1e-4), "Y");
            Assert.That(World[1].M43, Is.EqualTo(World[0].M43).Within(1e-4), "Z");
        }

        [Test]
        public void MarktRestPoseMatchesTheSkeleton()
        {
            const string ModelPath = @"D:\CuldceptSaga\RSP_unpacked\DATA06\DATA06_0213.rsm";
            CuldceptModel Package = CuldceptModel.Load(ModelPath);
            ModelRoot Root = GltfExporter.CreateGlbFromRSM(Package.Document, Package.TextureMap, Package.Animations);
            Skin Skin = Root.LogicalSkins[0];
            System.Collections.Generic.IReadOnlyList<Matrix4x4> Ibms = Skin.InverseBindMatrices;

            float WorstJoint = 0;
            for (int JointIndex = 0; JointIndex < Skin.JointsCount; JointIndex++)
            {
                Matrix4x4 Reflected = ReflectZ(Package.Document.ArrayWorldMatrix[JointIndex]);
                WorstJoint = Math.Max(WorstJoint, MatrixError(Skin.Joints[JointIndex].WorldMatrix * Ibms[JointIndex]));
                WorstJoint = Math.Max(WorstJoint, MatrixError(Matrix4x4.Multiply(Skin.Joints[JointIndex].WorldMatrix, Matrix4x4.Invert(Reflected, out Matrix4x4 Inverse) ? Inverse : Matrix4x4.Identity)));
            }

            Assert.That(WorstJoint, Is.LessThan(0.01f), "joint world times inverse bind");

            SceneInstance Scene = SceneTemplate.Create(Root.DefaultScene).CreateInstance();
            float Drift = SkinnedDrift(Root, Scene, "mesh_40");
            Assert.That(Drift, Is.LessThan(0.05f), "rest pose drifted off the bind mesh");

            Scene.Armature.SetAnimationFrame(0, 0f, false);
            Assert.That(SkinnedDrift(Root, Scene, "mesh_40"), Is.LessThan(0.05f), "first clip frame 0");

            Vector3 Min = new Vector3(float.MaxValue);
            Vector3 Max = new Vector3(float.MinValue);
            Scene.Armature.SetAnimationFrame(4, Root.LogicalAnimations[4].Duration * 0.6f, false);
            Measure(Root, Scene, "mesh_40", ref Min, ref Max);
            Vector3 Size = Max - Min;
            Assert.That(Size.Y, Is.InRange(40f, 70f), "animated height");
            Assert.That(Size.X, Is.LessThan(45f), "animated width");
        }

        private static float SkinnedDrift(ModelRoot Root, SceneInstance Scene, string MeshName)
        {
            float Drift = 0;
            foreach (DrawableInstance Drawable in Scene)
            {
                Mesh LogicalMesh = Root.LogicalMeshes[Drawable.Template.LogicalMeshIndex];
                if (LogicalMesh.Name != MeshName)
                {
                    continue;
                }
                IMeshDecoder<Material> Decoded = LogicalMesh.Decode();
                for (int PrimitiveIndex = 0; PrimitiveIndex < Decoded.Primitives.Count; PrimitiveIndex++)
                {
                    IMeshPrimitiveDecoder<Material> Decoder = Decoded.Primitives[PrimitiveIndex];
                    SharpGLTF.Memory.IAccessorArray<Vector3> Positions = LogicalMesh.Primitives[PrimitiveIndex].GetVertexAccessor("POSITION").AsVector3Array();
                    int Step = Math.Max(1, Positions.Count / 500);
                    for (int VertexIndex = 0; VertexIndex < Positions.Count; VertexIndex += Step)
                    {
                        Vector3 Raw = Positions[VertexIndex];
                        Vector3 Skinned = Decoder.GetPosition(VertexIndex, Drawable.Transform);
                        Drift = Math.Max(Drift, Vector3.Distance(Raw, new Vector3(Skinned.X, Skinned.Y, Skinned.Z)));
                    }
                }
            }

            return Drift;
        }

        private static void Measure(ModelRoot Root, SceneInstance Scene, string MeshName, ref Vector3 Min, ref Vector3 Max)
        {
            foreach (DrawableInstance Drawable in Scene)
            {
                Mesh LogicalMesh = Root.LogicalMeshes[Drawable.Template.LogicalMeshIndex];
                if (LogicalMesh.Name != MeshName)
                {
                    continue;
                }
                IMeshDecoder<Material> Decoded = LogicalMesh.Decode();
                for (int PrimitiveIndex = 0; PrimitiveIndex < Decoded.Primitives.Count; PrimitiveIndex++)
                {
                    IMeshPrimitiveDecoder<Material> Decoder = Decoded.Primitives[PrimitiveIndex];
                    int Count = LogicalMesh.Primitives[PrimitiveIndex].GetVertexAccessor("POSITION").Count;
                    int Step = Math.Max(1, Count / 500);
                    for (int VertexIndex = 0; VertexIndex < Count; VertexIndex += Step)
                    {
                        Vector3 Skinned = Decoder.GetPosition(VertexIndex, Drawable.Transform);
                        Vector3 Point = new Vector3(Skinned.X, Skinned.Y, Skinned.Z);
                        Min = Vector3.Min(Min, Point);
                        Max = Vector3.Max(Max, Point);
                    }
                }
            }
        }

        private static Matrix4x4 ReflectZ(Matrix4x4 Matrix)
        {
            return new Matrix4x4(
                 Matrix.M11, Matrix.M12, -Matrix.M13, Matrix.M14,
                 Matrix.M21, Matrix.M22, -Matrix.M23, Matrix.M24,
                -Matrix.M31, -Matrix.M32, Matrix.M33, -Matrix.M34,
                 Matrix.M41, Matrix.M42, -Matrix.M43, Matrix.M44);
        }

        private static float MatrixError(Matrix4x4 Matrix)
        {
            float Error = 0;
            Error = Math.Max(Error, Math.Abs(Matrix.M11 - 1));
            Error = Math.Max(Error, Math.Abs(Matrix.M22 - 1));
            Error = Math.Max(Error, Math.Abs(Matrix.M33 - 1));
            Error = Math.Max(Error, Math.Abs(Matrix.M44 - 1));
            Error = Math.Max(Error, Math.Abs(Matrix.M41));
            Error = Math.Max(Error, Math.Abs(Matrix.M42));
            Error = Math.Max(Error, Math.Abs(Matrix.M43));
            Error = Math.Max(Error, Math.Abs(Matrix.M12));
            Error = Math.Max(Error, Math.Abs(Matrix.M13));
            Error = Math.Max(Error, Math.Abs(Matrix.M21));
            Error = Math.Max(Error, Math.Abs(Matrix.M23));
            Error = Math.Max(Error, Math.Abs(Matrix.M31));
            Error = Math.Max(Error, Math.Abs(Matrix.M32));
            return Error;
        }
    }
}
