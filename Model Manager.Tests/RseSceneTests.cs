using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.RSA;
using CuldceptSagaExplorer.AssetManager.RSE;
using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.AssetManager.RST;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GltfExporter = global::CuldceptSagaExplorer.AssetManager.ExternalModel.GltfExporter;
using MaterialBuilder = SharpGLTF.Materials.MaterialBuilder;
using VertexPosition = SharpGLTF.Geometry.VertexTypes.VertexPosition;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [Category("RSE")]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class RseSceneTests
    {
        private const string SimpleScene = "[Version]\nVERSION=001\nAUTORST=1\n[SceneInformation]\nTotalFrame=31\nSceneNo=000\n"
            + "[ObjectList]\n0=EB_401.RSM\n[MotionList]\n[Setup]\n[KeyData]\nTIME=0\nMOPA=0,0,0\n"
            + "[KeyData]\nTIME=2\nMOPA=0,0,100\n[KeyData]\nTIME=30\nMOPA=0,0,0\n";
        private const string ParticleScene = "[Version]\nVERSION=1\nAUTORST=1\n[SceneInformation]\nTotalFrame=3\n"
            + "[ObjectList]\n0=EB_401.RSM\n1=DUMMY_PARTICLE_GENERATOR\n10=DUMMY_PARTICLE\n"
            + "[Particle]\nGENERATOR=1\nPARTICLE=10,10\nOBJ=0\nSTART=0\nMIDDLE=100\nEND=0\nFADEIN=10\nFADEOUT=90\nSPIN=0\nSEED=12345\n"
            + "[ParticleData]\nOBJNO=10\nKEY3=0,0,2,1,2,3,0,0,0,1,1,1\n"
            + "KEY3=1,1,2,4,5,6,0,0,0,1,1,1\nKEY3=2,0,0,0,0,0,0,0,0,1,1,1\n";

        private static string EffectDirectory => Environment.GetEnvironmentVariable("CULDCEPT_EFFECT_DIRECTORY")
            ?? @"D:\CuldceptSaga\Culdcept Saga Extracted\data\EFFECT";

        private static string EffectDependencyDirectory => Environment.GetEnvironmentVariable("CULDCEPT_EFFECT_DEPENDENCY_DIRECTORY")
            ?? Path.GetFullPath(Path.Combine(EffectDirectory, "..", "..", "eeprev"));

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void DependenciesResolveEachExactNameLocallyBeforeTheExplicitFallbackAndSurviveReload(
            bool LocalModel, bool LocalTexture, bool LocalMotion)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-dependencies-{Guid.NewGuid():N}");
            string Local = Path.Combine(Folder, "local"), Fallback = Path.Combine(Folder, "fallback");
            Directory.CreateDirectory(Local);
            Directory.CreateDirectory(Fallback);
            try
            {
                (string Extension, bool Local, string Kind)[] Files = new[] { (Extension: ".RSM", Local: LocalModel, Kind: "model"),
                    (Extension: ".RST", Local: LocalTexture, Kind: "texture"),
                    (Extension: ".RSA", Local: LocalMotion, Kind: "motion") };
                foreach ((string Extension, bool Local, string Kind) File in Files)
                {
                    string Name = "EB_401" + File.Extension;
                    System.IO.File.Copy(Path.Combine(EffectDirectory, "EB_414" + File.Extension),
                        Path.Combine(File.Local ? Local : Fallback, Name));
                    if (File.Local)
                    {
                        System.IO.File.WriteAllText(Path.Combine(Fallback, Name), "invalid fallback must not override local data");
                    }
                }
                string Source = Path.Combine(Local, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, SimpleScene.Replace("[MotionList]", "[MotionList]\n0=EB_401.RSA")
                    .Replace("[Setup]", "[KeyData]\nTIME=0\nMOTION=0,0,0,NOLOOP\n[Setup]"));
                RseExporter.Load(Source, DependencyDirectory: Fallback).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonArray Dependencies = (System.Text.Json.Nodes.JsonArray)Root.Extras!["rse"]!["dependencies"]!;
                Assert.That(Dependencies.Count, Is.EqualTo(3));
                foreach ((string Extension, bool Local, string Kind) File in Files)
                {
                    System.Text.Json.Nodes.JsonNode Entry = Dependencies.Single(Entry => Entry!["kind"]!.GetValue<string>() == File.Kind)!;
                    string Name = "EB_401" + File.Extension;
                    Assert.That(Entry["file"]!.GetValue<string>(), Is.EqualTo(Name));
                    Assert.That(Entry["path"]!.GetValue<string>(), Is.EqualTo(Path.Combine(File.Local ? Local : Fallback, Name)));
                    Assert.That(Entry["fromFallback"]!.GetValue<bool>(), Is.EqualTo(!File.Local));
                    Assert.That(System.IO.File.Exists(Path.Combine(Local, Name)), Is.EqualTo(File.Local), "Fallback must not copy assets.");
                }
                Assert.That(Root.LogicalImages, Is.Not.Empty);
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(31f / 60).Within(1e-6));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("missing")]
        [TestCase("nested")]
        [TestCase("similar-name")]
        [TestCase("invalid-local")]
        public void DependencyLookupDoesNotGuessRecurseOrReplaceMalformedLocalFiles(string Mode)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-dependency-failure-{Guid.NewGuid():N}");
            string Local = Path.Combine(Folder, "local"), Fallback = Path.Combine(Folder, "fallback");
            Directory.CreateDirectory(Local);
            Directory.CreateDirectory(Fallback);
            try
            {
                string Source = Path.Combine(Local, "scene.RSE"), LocalModel = Path.Combine(Local, "EB_401.RSM");
                File.WriteAllText(Source, SimpleScene.Replace("AUTORST=1", "AUTORST=0"));
                string Destination = Mode == "nested" ? Path.Combine(Fallback, "nested", "EB_401.RSM")
                    : Path.Combine(Fallback, Mode == "similar-name" ? "EB_401_01.RSM" : "EB_401.RSM");
                if (Mode != "missing")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
                    File.Copy(Path.Combine(EffectDirectory, "EB_401.RSM"), Destination);
                }
                if (Mode == "invalid-local")
                {
                    File.WriteAllText(LocalModel, "bad header");
                    Assert.That(() => RseExporter.Load(Source, DependencyDirectory: Fallback),
                        Throws.TypeOf<InvalidDataException>().With.Message.Contains("Invalid RSM"));
                }
                else
                {
                    ModelRoot Skipped = RseExporter.Load(Source, DependencyDirectory: Fallback);
                    Assert.That(Skipped.LogicalMeshes, Is.Empty);
                    Assert.That(Skipped.Extras!["rse"]!["dependencies"]!.AsArray(), Is.Empty);
                    Assert.That(Skipped.Extras!["rse"]!["absentDependencies"]!.AsArray().Single()!["file"]!.GetValue<string>(),
                        Is.EqualTo("EB_401.RSM"));
                }
                Assert.That(() => RseExporter.Load(Source, DependencyDirectory: Path.Combine(Folder, "absent")),
                    Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains("selected RSE dependency folder"));
                if (Mode != "invalid-local")
                {
                    ModelRoot WithoutFallback = RseExporter.Load(Source);
                    Assert.That(WithoutFallback.LogicalMeshes, Is.Empty);
                    Assert.That(WithoutFallback.Extras!["rse"]!["absentDependencies"]!.AsArray().Single()!["path"]!.GetValue<string>(),
                        Is.EqualTo(LocalModel));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("EB_401.RST", false)]
        [TestCase("EB_401.RST", true)]
        [TestCase("EB_401.RSA", false)]
        public void MalformedLocalTextureOrMotionIsNotReplacedByAValidFallback(string FileName, bool TruncateRecords)
        {
            string DirectoryPath = Path.Combine(Path.GetTempPath(), $"culdcept-invalid-dependency-{Guid.NewGuid():N}");
            string Fallback = Path.Combine(DirectoryPath, "fallback");
            try
            {
                Directory.CreateDirectory(Fallback);
                foreach (string Extension in new[] { ".RSM", ".RST", ".RSA" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_414" + Extension), Path.Combine(Fallback, "EB_401" + Extension));
                }

                byte[] Invalid = TruncateRecords
                    ? File.ReadAllBytes(Path.Combine(Fallback, FileName)).Take(12).ToArray()
                    : new byte[] { 1, 2, 3 };
                File.WriteAllBytes(Path.Combine(DirectoryPath, FileName), Invalid);
                string ScenePath = Path.Combine(DirectoryPath, "scene.RSE");
                File.WriteAllText(ScenePath, SimpleScene.Replace("[MotionList]", "[MotionList]\n0=EB_401.RSA")
                    .Replace("[Setup]", "[KeyData]\nTIME=0\nMOTION=0,0,0,NOLOOP\n[Setup]"));
                Assert.That(() => RseExporter.Load(ScenePath, DependencyDirectory: Fallback),
                    Throws.TypeOf<InvalidDataException>().With.Message.Contains(Path.Combine(DirectoryPath, FileName)));
            }
            finally { Directory.Delete(DirectoryPath, true); }
        }

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(3, 0)]
        public void MateriallessModelsNeedNoTextureAndOnlyDeclaredEmptyRsaChannelsAreNoOps(int Kind, int Count)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-noop-dependency-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                File.Copy(Path.Combine(EffectDirectory, "ES_016.RSM"), Path.Combine(Folder, "empty.RSM"));
                byte[] Motion = File.ReadAllBytes(Path.Combine(EffectDirectory, "ES_016.RSA"));
                BinaryPrimitives.WriteUInt16BigEndian(Motion.AsSpan(24, 2), (ushort)Kind);
                BinaryPrimitives.WriteUInt16BigEndian(Motion.AsSpan(26, 2), (ushort)Count);
                File.WriteAllBytes(Path.Combine(Folder, "empty.RSA"), Motion);
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, "[Version]\nVERSION=1\nAUTORST=1\n[SceneInformation]\nTotalFrame=10\n"
                    + "[ObjectList]\n0=empty.RSM\n[MotionList]\n0=empty.RSA\n[KeyData]\nTIME=0\nMOTION=0,0,0,NOLOOP\n");
                if (Kind != 0 || Count != 0)
                {
                    Assert.That(() => RseExporter.Load(Source), Throws.TypeOf<NotSupportedException>().With.Message.Contains("RSA channel kind"));
                    return;
                }
                RseExporter.Load(Source).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalMeshes, Is.Empty);
                Assert.That(Root.LogicalMaterials, Is.Empty);
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(10f / 60).Within(1e-6));
                System.Text.Json.Nodes.JsonArray Dependencies = (System.Text.Json.Nodes.JsonArray)Root.Extras!["rse"]!["dependencies"]!;
                Assert.That(Dependencies.Select(Entry => Entry!["kind"]!.GetValue<string>()), Is.EquivalentTo(new[] { "model", "motion" }));
                Assert.That(File.Exists(Path.Combine(Folder, "empty.RST")), Is.False);
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("EB_430", "EB_430.RSM")]
        [TestCase("EB_543", "EB_543.RSM")]
        [TestCase("EE_706", "EE_706_05.RSM")]
        [TestCase("EE_707", "EE_707_01.RSA")]
        [TestCase("EE_737", "737_01.RSM")]
        public void AbsentDeclaredFilesAreSkippedWithoutSubstitutingAnotherAsset(string Scene, string MissingFile)
        {
            ModelRoot Root = RseExporter.Load(Path.Combine(EffectDirectory, Scene + ".RSE"),
                DependencyDirectory: EffectDependencyDirectory);
            System.Text.Json.Nodes.JsonNode Metadata = Root.Extras!["rse"]!;
            System.Text.Json.Nodes.JsonArray Absent = Metadata["absentDependencies"]!.AsArray();
            Assert.That(Absent.Any(Entry => Entry!["file"]!.GetValue<string>() == MissingFile), Is.True);
            Assert.That(Absent.Single(Entry => Entry!["file"]!.GetValue<string>() == MissingFile)!["fallback"]!.GetValue<string>(),
                Is.EqualTo(Path.Combine(EffectDependencyDirectory, MissingFile)));
            Assert.That(Metadata["dependencies"]!.AsArray().Any(Entry => Entry!["file"]!.GetValue<string>() == MissingFile), Is.False);
            if (Scene == "EB_430")
            {
                Assert.That(Absent.Select(Entry => Entry!["file"]!.GetValue<string>()),
                    Is.EquivalentTo(new[] { "EB_430.RSM", "EB_430.RSA" }));
                Assert.That(Root.LogicalNodes.Any(Node => Node.Name == "rse_object_49"), Is.False);
                Assert.That(Metadata["dependencies"]!.AsArray().Select(Entry => Entry!["file"]!.GetValue<string>()),
                    Does.Contain("EB_430_01.RSM").And.Contain("EB_430_02.RSM"));
                Assert.That(Root.LogicalMeshes, Is.Not.Empty);
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(62)]
        [TestCase(80)]
        [TestCase(81)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void SceneNumberIsPreservedAsTextBankMetadataWithoutChangingVisualPlayback(int SceneNumber)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-text-bank-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Text = SimpleScene.Replace("SceneNo=000", $"SceneNo={SceneNumber}");
                Assert.That(RseReader.Parse(Text).SceneNumber, Is.EqualTo(SceneNumber));
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.Extras!["rse"]!["sceneNumber"]!.GetValue<int>(), Is.EqualTo(SceneNumber));
                Assert.That(Root.Extras!["rse"]!["totalFrames"]!.GetValue<int>(), Is.EqualTo(31));
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.Duration, Is.EqualTo(31f / 60).Within(1e-6));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Opacity = Timeline.Channels.Single(Channel => Channel.TargetPointerPath != null)
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Opacity.GetPoint(1f / 60).W, Is.EqualTo(.5f).Within(1e-6));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(4, true, 30)]
        [TestCase(4, true, 60)]
        [TestCase(4, false, 60)]
        [TestCase(5, true, 30)]
        [TestCase(5, true, 60)]
        [TestCase(5, false, 60)]
        public void SparseBillboardParticlesKeepThreeAxisSeededSpinAndIndependentScale(int Spin, bool Seeded, int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-sparse-billboard-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Text = ParticleScene.Substring(0, ParticleScene.IndexOf("[ParticleData]", StringComparison.Ordinal))
                    .Replace("TotalFrame=3", "TotalFrame=6")
                    .Replace("10=DUMMY_PARTICLE", $"10=DUMMY_PARTICLE\n{int.MaxValue}=DUMMY_PARTICLE")
                    .Replace("PARTICLE=10,10", $"PARTICLE=9,{int.MaxValue}")
                    .Replace("SPIN=0", $"SPIN={Spin}\nANGX=10,180\nANGY=20,180\nANGZ=30,180\nROTX=1,15\nROTY=2,15\nROTZ=3,15");
                if (!Seeded)
                {
                    Text = Text.Replace("SEED=12345", "#SEED=12345");
                }

                Text += "[ParticleData]\n";
                foreach (int Id in new[] { 10, int.MaxValue })
                {
                    Text += $"OBJNO={Id}\n";
                    foreach (int Frame in Enumerable.Range(0, 6))
                    {
                        string AgeLife = Frame == 0 || Frame == 2 || Frame == 5 ? "-1,0" : Frame == 4 ? "4,2" : "0,2";
                        Text += $"KEY3={Frame},{AgeLife},1,2,3,.2,.3,.4,1.3,.7,1.1\n";
                    }
                }
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.DicObject.Values.Count(Object => Object.Kind == RseObjectKind.Particle), Is.EqualTo(2));
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Root.Extras!["rse"]!["particleCount"]!.GetValue<int>(), Is.EqualTo(2));
                Assert.That(Root.LogicalNodes.Count(Node =>
                {
                    if (Node.Extras == null)
                    {
                        return false;
                    }

                    return Node.Extras["rseBillboard"] != null;
                }), Is.EqualTo(2));
                foreach (int Index in Enumerable.Range(0, 2))
                {
                    int Id = Index == 0 ? 10 : int.MaxValue;
                    Vector3 Angles = Seeded ? new Vector3(10, 20, 30) + (Index == 0
                        ? new Vector3(62.02f, -56.08f, -67.14f) : new Vector3(159.55f, 6.16f, -95.11f)) : Vector3.Zero;
                    Vector3 Velocity = Seeded ? new Vector3(1, 2, 3) + (Index == 0
                        ? new Vector3(5.36f, -14.93f, 9.33f) : new Vector3(-7.11f, 4.9f, -4.03f)) : Vector3.Zero;
                    Angles *= MathF.PI / 180;
                    Velocity *= MathF.PI / 180;
                    Node Particle = Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Id}");
                    Node SpinNode = Root.LogicalNodes.Single(Node => Node.Name == $"rse_spin_{Id}");
                    Assert.That(SpinNode.VisualParent, Is.SameAs(Particle));
                    Assert.That(Particle.Extras!["rseBillboard"]!.GetValue<string>(),
                        Is.EqualTo(Spin == 4 ? "spherical" : "verticalProjected"));
                    foreach (RseParticleFrame Frame in Scene.DicObject[Id].ListParticleFrame)
                    {
                        if (Frame.Frame != 0 && Frame.Age >= 0 && Frame.Lifetime > 0)
                        {
                            Angles += Velocity;
                        }

                        Matrix ExpectedMatrix = Matrix.CreateRotationY(-Angles.Y)
                            * Matrix.CreateRotationX(-Angles.X) * Matrix.CreateRotationZ(Angles.Z);
                        SharpGLTF.Transforms.AffineTransform Pose = SpinNode.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        System.Numerics.Vector3 Point = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(.2f, .5f, .9f), Pose.Rotation);
                        Vector3 Expected = Vector3.Transform(new Vector3(.2f, .5f, .9f), ExpectedMatrix);
                        Assert.That(System.Numerics.Vector3.Distance(Point,
                            new System.Numerics.Vector3(Expected.X, Expected.Y, Expected.Z)), Is.LessThan(1e-5),
                            $"Particle {Id}, frame {Frame.Frame}");
                        SharpGLTF.Transforms.AffineTransform Held = SpinNode.GetLocalTransform(Timeline, (Frame.Frame + .5f) / FramesPerSecond);
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Held.Rotation, Pose.Rotation)), Is.EqualTo(1).Within(1e-6));
                        SharpGLTF.Transforms.AffineTransform ParentPose = Particle.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        Assert.That(ParentPose.Rotation, Is.EqualTo(System.Numerics.Quaternion.Identity));
                        Assert.That(ParentPose.Scale, Is.EqualTo(new System.Numerics.Vector3(1.3f, .7f, 1.1f)));
                        Assert.That(ParentPose.Translation, Is.EqualTo(new System.Numerics.Vector3(1, 2, -3)));
                    }
                    SharpGLTF.Transforms.AffineTransform Endpoint = SpinNode.GetLocalTransform(Timeline, 6f / FramesPerSecond);
                    SharpGLTF.Transforms.AffineTransform LastPose = SpinNode.GetLocalTransform(Timeline, 5f / FramesPerSecond);
                    Assert.That(Endpoint.Rotation, Is.EqualTo(LastPose.Rotation));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void UvAnimationDummyKeysAreExplicitlyPreservedWithoutAffectingTheRealModel(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-uv-dummy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                string Text = SimpleScene.Replace("[ObjectList]", "[ObjectList]\n7=DUMMY_UVANIM")
                    .Replace("[Setup]", "[Setup]\nPOS3=7,1,2,3\nOPACITY=7,0")
                    + "[KeyData]\nTIME=0\nMOPA=7,99,0\nUVANIM=7,99,2,3\n";
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalNodes.Any(Node => Node.Name == "rse_object_7"), Is.False);
                Assert.That(Root.Extras!["rse"]!["ignoredUvAnimationDummies"]![0]!.GetValue<int>(), Is.EqualTo(7));
                System.Text.Json.Nodes.JsonArray Commands = Root.Extras!["rse"]!["ignoredDummyCommands"]!.AsArray();
                Assert.That(Commands.Select(Command => Command!["command"]!.GetValue<string>()),
                    Is.EqualTo(new[] { "POS3", "OPACITY", "MOPA", "UVANIM" }));
                Assert.That(Commands.Select(Command => Command!["frame"]!.GetValue<int>()), Is.EqualTo(new[] { -1, -1, 0, 0 }));
                Assert.That(Commands.Last()!["value"]!.GetValue<string>(), Is.EqualTo("7,99,2,3"));
                File.WriteAllText(Source, SimpleScene);
                ModelRoot Baseline = RseExporter.Load(Source, FramesPerSecond);
                Assert.That(Root.LogicalMaterials.Count, Is.EqualTo(Baseline.LogicalMaterials.Count));
                foreach ((AnimationChannel First, AnimationChannel Second) Pair in Root.LogicalAnimations.Single().Channels.Zip(Baseline.LogicalAnimations.Single().Channels))
                {
                    Assert.That(Pair.First.TargetPointerPath, Is.EqualTo(Pair.Second.TargetPointerPath));
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Actual = Pair.First.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Expected = Pair.Second.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    foreach (float Frame in new[] { 0f, 1f, 2f, 15f, 30f, 31f })
                    {
                        Assert.That(Actual.GetPoint(Frame / FramesPerSecond), Is.EqualTo(Expected.GetPoint(Frame / FramesPerSecond)));
                    }
                }
                Assert.That(Root.LogicalAnimations.Single().Channels.Count, Is.EqualTo(Baseline.LogicalAnimations.Single().Channels.Count));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30, false)]
        [TestCase(60, false)]
        [TestCase(60, true)]
        public void UnboundParticleSourcesKeepValidatedTracksAndClockButCreateNoParticleGeometry(int FramesPerSecond, bool MixedScene)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-unbound-particles-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                string Text = ParticleScene.Replace("OBJ=0\n", "");
                if (!MixedScene)
                {
                    Text = Text.Replace("0=EB_401.RSM\n", "");
                }
                else
                {
                    foreach (string Extension in new[] { ".RSM", ".RST" })
                    {
                        File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                    }
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.ListParticleGenerator.Single().HasModelReference, Is.False);
                Assert.That(Scene.DicObject[10].ListParticleFrame.Count, Is.EqualTo(3));
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalMeshes.Count > 0, Is.EqualTo(MixedScene));
                Assert.That(Root.LogicalNodes.Any(Node => Node.Name == "rse_object_10"), Is.False);
                Assert.That(Root.Extras!["rse"]!["empty"]!.GetValue<bool>(), Is.False);
                Assert.That(Root.Extras!["rse"]!["renderless"]!.GetValue<bool>(), Is.EqualTo(!MixedScene));
                Assert.That(Root.Extras!["rse"]!["particleCount"]!.GetValue<int>(), Is.EqualTo(1));
                System.Text.Json.Nodes.JsonNode Unbound = Root.Extras!["rse"]!["unboundParticleGenerators"]!.AsArray().Single()!;
                Assert.That(Unbound["generatorId"]!.GetValue<int>(), Is.EqualTo(1));
                Assert.That(Unbound["firstParticle"]!.GetValue<int>(), Is.EqualTo(10));
                Assert.That(Unbound["lastParticle"]!.GetValue<int>(), Is.EqualTo(10));
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(3f / FramesPerSecond).Within(1e-6));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("PARTICLE=10,10", "PARTICLE=11,12")));
                Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text.Replace(
                    "KEY3=2,0,0,0,0,0,0,0,0,1,1,1\n", "")));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("GENERATOR=1", "GENERATOR=1\nOBJ=-1")));
                RseParticleGenerator MissingSource = RseReader.Parse(Text.Replace("GENERATOR=1", "GENERATOR=1\nOBJ=99")).ListParticleGenerator.Single();
                Assert.That(MissingSource.ModelObjectId, Is.EqualTo(99));
                Assert.That(MissingSource.HasModelReference, Is.True);
                Assert.That(MissingSource.HasBoundModel, Is.False);
                Assert.That(MissingSource.IsModelObjectMissing, Is.True);
                Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text.Replace("GENERATOR=1", "GENERATOR=1\nOBG=0")));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30, false)]
        [TestCase(60, false)]
        [TestCase(30, true)]
        [TestCase(60, true)]
        public void UndeclaredParticleSourcesRemainUnboundWithoutResolvingAnIncidentalAsset(int FramesPerSecond, bool MixedScene)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-unmatched-source-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                string Text = ParticleScene.Replace("OBJ=0", "OBJ=99");
                if (!MixedScene)
                {
                    Text = Text.Replace("0=EB_401.RSM\n", "");
                }
                else
                {
                    foreach (string Extension in new[] { ".RSM", ".RST" })
                    {
                        File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                    }
                }

                File.WriteAllText(Path.Combine(Folder, "99.RSM"), "Not a declared dependency and must never be loaded.");
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.DicObject.ContainsKey(99), Is.False);
                RseParticleGenerator Generator = Scene.ListParticleGenerator.Single();
                Assert.That(Generator.ModelObjectId, Is.EqualTo(99));
                Assert.That(Generator.IsModelObjectMissing, Is.True);
                Assert.That(Generator.HasModelReference, Is.True);
                Assert.That(Generator.HasBoundModel, Is.False);
                Assert.That(Scene.HasRenderableObjects, Is.EqualTo(MixedScene));
                Assert.That(Scene.DicObject[10].ListParticleFrame.Count, Is.EqualTo(3));
                RseExporter.Load(Source, FramesPerSecond, Folder).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalMeshes.Count > 0, Is.EqualTo(MixedScene));
                Assert.That(Root.LogicalNodes.Any(Node => Node.Name == "rse_object_10"), Is.False);
                System.Text.Json.Nodes.JsonNode Rse = Root.Extras!["rse"]!;
                Assert.That(Rse["renderless"]!.GetValue<bool>(), Is.EqualTo(!MixedScene));
                Assert.That(Rse["dependencies"]!.AsArray().Any(Entry => Entry!["file"]!.GetValue<string>() == "99.RSM"), Is.False);
                System.Text.Json.Nodes.JsonNode Unbound = Rse["unboundParticleGenerators"]!.AsArray().Single()!;
                Assert.That(Unbound["modelObjectId"]!.GetValue<int>(), Is.EqualTo(99));
                Assert.That(Unbound["reason"]!.GetValue<string>(), Is.EqualTo("Undeclared model object"));
                Assert.That(Unbound["firstParticle"]!.GetValue<int>(), Is.EqualTo(10));
                Assert.That(Unbound["lastParticle"]!.GetValue<int>(), Is.EqualTo(10));
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(3f / FramesPerSecond).Within(1e-6));
                Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text.Replace("KEY3=2,0,0,0,0,0,0,0,0,1,1,1\n", "")));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("PARTICLE=10,10", "PARTICLE=11,12")));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("OBJ=99", "OBJ=-1")));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("OBJ=99", "OBJ=1")));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("OBJ=99", "OBJ=10")));
                File.WriteAllText(Source, Text.Replace("OBJ=99", ""));
                ModelRoot Baseline = RseExporter.Load(Source, FramesPerSecond);
                Assert.That(Root.LogicalNodes.Select(Node => Node.LocalMatrix),
                    Is.EqualTo(Baseline.LogicalNodes.Select(Node => Node.LocalMatrix)));
                Assert.That(Root.LogicalMeshes.Count, Is.EqualTo(Baseline.LogicalMeshes.Count));
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(Baseline.LogicalAnimations.Single().Duration));
                File.WriteAllText(Source, Text.Replace("[ObjectList]", "[ObjectList]\n99=missing.RSM"));
                ModelRoot DeclaredMissing = RseExporter.Load(Source);
                Assert.That(DeclaredMissing.LogicalNodes.Any(Node => Node.Name == "rse_object_99"), Is.False);
                Assert.That(DeclaredMissing.Extras!["rse"]!["dependencies"]!.AsArray()
                    .Any(Entry => Entry!["file"]!.GetValue<string>() == "99.RSM"), Is.False);
                Assert.That(DeclaredMissing.Extras!["rse"]!["absentDependencies"]!.AsArray().Single()!["file"]!.GetValue<string>(),
                    Is.EqualTo("missing.RSM"));
                Assert.That(DeclaredMissing.LogicalMeshes.Count, Is.EqualTo(Baseline.LogicalMeshes.Count));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void UnmatchedSetupCommandsPreserveOriginalsWithoutCreatingOrRedirectingObjects(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-unmatched-setup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string[] Commands = { "POS3=99,9,8,7", "QUAT=99,0,1,0,0", "SCL3=99,4,5,6",
                    "OPACITY=99,0", "MOPA=99,200,25" };
                string Text = SimpleScene.Replace("[Setup]", "[Setup]\n" + string.Join("\n", Commands));
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.DicObject.Keys, Is.EqualTo(new[] { 0 }));
                Assert.That(Scene.DicObject[0].Position, Is.EqualTo(System.Numerics.Vector3.Zero));
                Assert.That(Scene.ListUnmatchedSetupCommand.Select(Command => Command.Value),
                    Is.EqualTo(Commands.Select(Command => Command.Substring(Command.IndexOf('=') + 1))));
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonArray Ignored = Root.Extras!["rse"]!["unmatchedSetupCommands"]!.AsArray();
                Assert.That(Ignored.Count, Is.EqualTo(5));
                Assert.That(Ignored.All(Command => Command!["objectId"]!.GetValue<int>() == 99
                    && Command["frame"]!.GetValue<int>() == -1), Is.True);
                Assert.That(Ignored.Select(Command => Command!["command"]!.GetValue<string>()),
                    Is.EqualTo(new[] { "POS3", "QUAT", "SCL3", "OPACITY", "MOPA" }));
                Assert.That(Ignored.Select(Command => Command!["value"]!.GetValue<string>()),
                    Is.EqualTo(Scene.ListUnmatchedSetupCommand.Select(Command => Command.Value)));
                File.WriteAllText(Source, SimpleScene);
                ModelRoot Baseline = RseExporter.Load(Source, FramesPerSecond);
                Assert.That(Root.LogicalNodes.Select(Node => Node.LocalMatrix),
                    Is.EqualTo(Baseline.LogicalNodes.Select(Node => Node.LocalMatrix)));
                Assert.That(Root.LogicalMaterials.Count, Is.EqualTo(Baseline.LogicalMaterials.Count));
                Assert.That(Root.LogicalAnimations.Single().Channels.Count, Is.EqualTo(Baseline.LogicalAnimations.Single().Channels.Count));
                foreach ((AnimationChannel First, AnimationChannel Second) Pair in Root.LogicalAnimations.Single().Channels.Zip(Baseline.LogicalAnimations.Single().Channels))
                {
                    Assert.That(Pair.First.TargetPointerPath, Is.EqualTo(Pair.Second.TargetPointerPath));
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Actual = Pair.First.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Expected = Pair.Second.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    for (float Frame = 0; Frame <= Scene.TotalFrames; Frame += .5f)
                    {
                        Assert.That(Actual.GetPoint(Frame / FramesPerSecond), Is.EqualTo(Expected.GetPoint(Frame / FramesPerSecond)));
                    }
                }
                Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text + "[ObjectList]\n99=EB_401.RSM\n"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("POS3=99,NaN,0,0")]
        [TestCase("POS3=-1,0,0,0")]
        [TestCase("POS3=99,0,0")]
        [TestCase("QUAT=99,0,0,0,0")]
        [TestCase("QUAT=99,0,0,0,Infinity")]
        [TestCase("SCL3=99,1,O,1")]
        [TestCase("OPACITY=99,101")]
        [TestCase("MOPA=99,-1,50")]
        [TestCase("MOPA=99,0,-41")]
        public void UnmatchedSetupStillRejectsMalformedCommands(string Command)
        {
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(
                SimpleScene.Replace("[Setup]", "[Setup]\n" + Command), "unmatched.RSE"));
            Assert.That(Error!.Message, Does.Contain("unmatched.RSE").And.Contain("line"));
        }

        [TestCase(30, false)]
        [TestCase(60, false)]
        [TestCase(30, true)]
        [TestCase(60, true)]
        public void RamDeclarationsRemainUnconstructedEvenWhenMatchingRsmAssetsExist(int FramesPerSecond, bool MixedScene)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-ignored-ram-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                string Text = ParticleScene.Replace("[ObjectList]", "[ObjectList]\n99=ignored.RAM")
                    .Replace("OBJ=0", "OBJ=99").Replace("[ParticleData]", "[Setup]\nOPACITY=99,0\n[ParticleData]");
                if (!MixedScene)
                {
                    Text = Text.Replace("0=EB_401.RSM\n", "");
                }
                else
                {
                    foreach (string Extension in new[] { ".RSM", ".RST" })
                    {
                        File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                    }
                }

                File.WriteAllText(Path.Combine(Folder, "ignored.RAM"), "The native parser must not open this.");
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "ignored" + Extension));
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.DicObject.ContainsKey(99), Is.False);
                Assert.That(Scene.DicIgnoredModelDeclaration[99], Is.EqualTo("ignored.RAM"));
                Assert.That(Scene.IsEmpty, Is.False);
                Assert.That(Scene.HasRenderableObjects, Is.EqualTo(MixedScene));
                Assert.That(Scene.ListParticleGenerator.Single().HasBoundModel, Is.False);
                Assert.That(Scene.ListParticleGenerator.Single().ModelObjectId, Is.EqualTo(99));
                Assert.That(Scene.ListUnmatchedSetupCommand.Single().Value, Is.EqualTo("99,0"));
                RseExporter.Load(Source, FramesPerSecond, Folder).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonNode Metadata = Root.Extras!["rse"]!;
                System.Text.Json.Nodes.JsonNode Declaration = Metadata["ignoredModelDeclarations"]!.AsArray().Single()!;
                Assert.That(Declaration["objectId"]!.GetValue<int>(), Is.EqualTo(99));
                Assert.That(Declaration["value"]!.GetValue<string>(), Is.EqualTo("ignored.RAM"));
                Assert.That(Declaration["reason"]!.GetValue<string>(), Does.Contain("Native object parser"));
                Assert.That(Metadata["unboundParticleGenerators"]![0]!["reason"]!.GetValue<string>(),
                    Is.EqualTo("Unconstructed model declaration"));
                Assert.That(Metadata["dependencies"]!.AsArray().All(Entry =>
                    !Entry!["file"]!.GetValue<string>().StartsWith("ignored", StringComparison.Ordinal)), Is.True);
                Assert.That(Metadata["renderless"]!.GetValue<bool>(), Is.EqualTo(!MixedScene));
                Assert.That(Root.LogicalMeshes.Count > 0, Is.EqualTo(MixedScene));
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(3f / FramesPerSecond).Within(1e-6));
                File.WriteAllText(Source, Text.Replace("99=ignored.RAM\n", ""));
                ModelRoot Baseline = RseExporter.Load(Source, FramesPerSecond);
                Assert.That(Root.LogicalNodes.Select(Node => Node.LocalMatrix),
                    Is.EqualTo(Baseline.LogicalNodes.Select(Node => Node.LocalMatrix)));
                Assert.That(Root.LogicalMaterials.Count, Is.EqualTo(Baseline.LogicalMaterials.Count));
                Assert.That(Root.LogicalAnimations.Single().Channels.Count,
                    Is.EqualTo(Baseline.LogicalAnimations.Single().Channels.Count));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("99=ignored.RAM", "99=ignored.RAM\n99=ignored.RSM")));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("99=ignored.RAM", "99=ignored.RAM\n99=other.RAM")));
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("OPACITY=99,0", "OPACITY=99,101")));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void RamOnlyScenePreservesDeclarationAndDurationWithoutBecomingObjectFree()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-ram-only-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                string Text = "[Version]\nVERSION=1\nAUTORST=1\n[SceneInformation]\nTotalFrame=5\n"
                    + "[ObjectList]\n99=ignored.RAM\n[Setup]\nPOS3=99,1,2,3\n";
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.DicObject, Is.Empty);
                Assert.That(Scene.IsEmpty, Is.False);
                Assert.That(Scene.HasRenderableObjects, Is.False);
                RseExporter.Load(Source).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalMeshes, Is.Empty);
                Assert.That(Root.Extras!["rse"]!["empty"]!.GetValue<bool>(), Is.False);
                Assert.That(Root.Extras!["rse"]!["renderless"]!.GetValue<bool>(), Is.True);
                Assert.That(Root.Extras!["rse"]!["dependencies"]!.AsArray(), Is.Empty);
                Assert.That(Root.Extras!["rse"]!["ignoredModelDeclarations"]!.AsArray().Count, Is.EqualTo(1));
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(5f / 60).Within(1e-6));
                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = Window.Handle,
                        BackBufferWidth = 64,
                        BackBufferHeight = 64,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                using Animated3DModel Model = new Animated3DModel(Device, Root);
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1f / 60)));
                Assert.That(Model.AnimationTime, Is.GreaterThan(0));
                Model.RestartAnimation();
                Assert.That(Model.AnimationTime, Is.Zero);
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("..\\ignored.RAM")]
        [TestCase("folder/ignored.RAM")]
        [TestCase("E:\\ignored.RAM")]
        [TestCase("ignored.RSM.RAM")]
        [TestCase("DUMMY_PARTICLE.RAM")]
        [TestCase("Camera.RAM")]
        [TestCase("ignored.UNKNOWN")]
        public void RamCompatibilityDoesNotAcceptUnsafeOrAmbiguousDeclarations(string Value)
        {
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(
                SimpleScene.Replace("[ObjectList]", "[ObjectList]\n99=" + Value)));
        }

        [TestCase(30, false)]
        [TestCase(60, false)]
        [TestCase(60, true)]
        public void InitialNestedModelEffectsKeepIndependentMotionMaterialsAndExactDependencies(int FramesPerSecond, bool FallbackChild)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-nested-model-{Guid.NewGuid():N}");
            string Local = Path.Combine(Folder, "local"), Fallback = Path.Combine(Folder, "fallback");
            Directory.CreateDirectory(Local);
            Directory.CreateDirectory(Fallback);
            try
            {
                string ChildDirectory = FallbackChild ? Fallback : Local;
                string ChildModelName = FallbackChild ? "EB_401" : "child";
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Local, "EB_401" + Extension));
                    File.Copy(Path.Combine(EffectDirectory, "EB_414" + Extension), Path.Combine(ChildDirectory, ChildModelName + Extension));
                }
                File.Copy(Path.Combine(EffectDirectory, "EB_414.RSA"), Path.Combine(ChildDirectory, ChildModelName + ".RSA"));
                string Parent = SimpleScene.Replace("TotalFrame=31", "TotalFrame=40")
                    .Replace("[ObjectList]", "[ObjectList]\n9=DUMMY_EFFECT_GENERATOR")
                    .Replace("[MotionList]", "[EffectList]\n7=child.RSE\n[MotionList]")
                    .Replace("TIME=0", "TIME=0\nEFFECT=9,7");
                string Child = SimpleScene.Replace("EB_401.RSM", ChildModelName + ".RSM")
                    .Replace("AUTORST=1", "AUTORST=1\nLOOP=0")
                    .Replace("[MotionList]", "[MotionList]\n0=" + ChildModelName + ".RSA")
                    .Replace("[Setup]", "[Setup]\nPOS3=0,1,2,3")
                    .Replace("TIME=2", "TIME=2\nMOTION=0,0,0,NOLOOP");
                int MaterialCount = CuldceptModel.Load(Path.Combine(ChildDirectory, ChildModelName + ".RSM"),
                    WithAnimations: false, WithTextures: false).Document.ListMaterial.Count;
                Child += "[KeyData]\nTIME=30\n" + string.Concat(Enumerable.Range(1, MaterialCount - 1)
                    .Select(Slot => $"MOPA=0,{Slot},0\n"));
                string Source = Path.Combine(Local, "parent.RSE"), ChildPath = Path.Combine(ChildDirectory, "child.RSE");
                string Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Parent);
                File.WriteAllText(ChildPath, Child);
                RseExporter.Load(Source, FramesPerSecond, Fallback).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                ModelRoot Baseline = RseExporter.Load(ChildPath, FramesPerSecond);
                Assert.That(Root.LogicalAnimations.Count, Is.EqualTo(1));
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.Duration, Is.EqualTo(40f / FramesPerSecond).Within(1e-6));
                System.Text.Json.Nodes.JsonNode Rse = Root.Extras!["rse"]!;
                System.Text.Json.Nodes.JsonNode Nested = Rse["nestedEffects"]!.AsArray().Single()!;
                Assert.That(Nested["generatorId"]!.GetValue<int>(), Is.EqualTo(9));
                Assert.That(Nested["effectId"]!.GetValue<int>(), Is.EqualTo(7));
                Assert.That(Nested["startFrame"]!.GetValue<int>(), Is.Zero);
                Assert.That(Nested["totalFrames"]!.GetValue<int>(), Is.EqualTo(31));
                Assert.That(Nested["file"]!.GetValue<string>(), Is.EqualTo("child.RSE"));
                System.Text.Json.Nodes.JsonNode Mapping = Nested["objects"]!.AsArray().Single()!;
                Assert.That(Mapping["sourceObjectId"]!.GetValue<int>(), Is.Zero);
                int Id = Mapping["exportObjectId"]!.GetValue<int>();
                Assert.That(Id, Is.Not.EqualTo(0).And.Not.EqualTo(9));
                System.Text.Json.Nodes.JsonArray Dependencies = Rse["dependencies"]!.AsArray();
                Assert.That(Dependencies.Count, Is.EqualTo(6));
                foreach (string Name in new[] { "child.RSE", ChildModelName + ".RSM", ChildModelName + ".RST", ChildModelName + ".RSA" })
                {
                    System.Text.Json.Nodes.JsonNode Entry = Dependencies.Single(Entry => Entry!["path"]!.GetValue<string>() == Path.Combine(ChildDirectory, Name))!;
                    Assert.That(Entry["file"]!.GetValue<string>(), Is.EqualTo(Name));
                    Assert.That(Entry["path"]!.GetValue<string>(), Is.EqualTo(Path.Combine(ChildDirectory, Name)));
                    Assert.That(Entry["fromFallback"]!.GetValue<bool>(), Is.EqualTo(FallbackChild && Name == "child.RSE"));
                }
                Assert.That(Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Id}").LocalMatrix,
                    Is.EqualTo(Baseline.LogicalNodes.Single(Node => Node.Name == "rse_object_0").LocalMatrix));
                AnimationChannel[] ChildChannels = Baseline.LogicalAnimations.Single().Channels
                    .Where(Channel => Channel.TargetNode != null
                        && Channel.TargetNode.Name.StartsWith("rse_0_node_", StringComparison.Ordinal)
                        && (Channel.TargetNodePath == PropertyPath.translation || Channel.TargetNodePath == PropertyPath.rotation
                            || Channel.TargetNodePath == PropertyPath.scale)).ToArray();
                Assert.That(ChildChannels.Length, Is.GreaterThanOrEqualTo(3), "The fixture must exercise node motion, not only material animation.");
                foreach (AnimationChannel? Channel in ChildChannels)
                {
                    string Target = Channel.TargetNode!.Name.Replace("rse_0_", $"rse_{Id}_");
                    AnimationChannel Actual = Timeline.Channels.Single(Candidate =>
                    {
                        if (Candidate.TargetNode == null)
                        {
                            return false;
                        }

                        return Candidate.TargetNode.Name == Target && Candidate.TargetNodePath == Channel.TargetNodePath;
                    });
                    for (int Frame = 0; Frame < 40; Frame++)
                    {
                        float Time = Math.Min(Frame, 30) / (float)FramesPerSecond;
                        if (Channel.TargetNodePath == PropertyPath.rotation)
                        {
                            System.Numerics.Quaternion Expected = Channel.GetSamplerOrNull<System.Numerics.Quaternion>().CreateCurveSampler(true).GetPoint(Time);
                            System.Numerics.Quaternion Value = Actual.GetSamplerOrNull<System.Numerics.Quaternion>().CreateCurveSampler(true).GetPoint(Frame / (float)FramesPerSecond);
                            Assert.That(Math.Abs(System.Numerics.Quaternion.Dot(Expected, Value)), Is.EqualTo(1).Within(1e-5));
                        }
                        else
                        {
                            System.Numerics.Vector3 Expected = Channel.GetSamplerOrNull<System.Numerics.Vector3>().CreateCurveSampler(true).GetPoint(Time);
                            System.Numerics.Vector3 Value = Actual.GetSamplerOrNull<System.Numerics.Vector3>().CreateCurveSampler(true).GetPoint(Frame / (float)FramesPerSecond);
                            Assert.That(System.Numerics.Vector3.Distance(Expected, Value), Is.LessThan(1e-5));
                        }
                    }
                }
                Material[] ChildMaterials = Root.LogicalMaterials.Where(Material =>
                    Material.Name.StartsWith($"rse_{Id}_", StringComparison.Ordinal)).ToArray();
                Assert.That(ChildMaterials.Length, Is.EqualTo(MaterialCount));
                foreach (Material? Material in ChildMaterials)
                {
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Color = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                        .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    for (int Frame = 30; Frame <= 40; Frame++)
                    {
                        Assert.That(Color.GetPoint(Frame / (float)FramesPerSecond).W, Is.Zero);
                    }
                }
                File.WriteAllText(ChildPath, Child.Replace("MOPA=0,0,0", "MOPA=0,0,50"));
                Assert.That(Assert.Throws<NotSupportedException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback))!.Message,
                    Does.Contain("terminal zero MOPA"));
                File.WriteAllText(ChildPath, Child.Replace("LOOP=0", "LOOP=1"));
                Assert.Throws<NotSupportedException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback));
                File.WriteAllText(ChildPath, Child.Replace("TotalFrame=31", "TotalFrame=50"));
                Assert.Throws<NotSupportedException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback));
                File.WriteAllText(ChildPath, Child.Replace("AUTORST=1", "AUTORST=0"));
                Assert.Throws<NotSupportedException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback));
                File.WriteAllText(ChildPath, Child.Replace("[ObjectList]", "[ObjectList]\n9=DUMMY_EFFECT_GENERATOR")
                    .Replace("[MotionList]", "[EffectList]\n7=parent.RSE\n[MotionList]")
                    .Replace("TIME=0", "TIME=0\nEFFECT=9,7"));
                Assert.Throws<NotSupportedException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback));
                File.WriteAllText(ChildPath, "invalid local child");
                Assert.Throws<InvalidDataException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback));
                File.WriteAllText(Source, Parent.Replace("7=child.RSE", "7=parent.RSE"));
                Assert.That(Assert.Throws<NotSupportedException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback))!.Message,
                    Does.Contain("recursive nested effects"));
                File.WriteAllText(Source, Parent);
                File.Delete(ChildPath);
                Assert.That(Assert.Throws<FileNotFoundException>(() => RseExporter.Load(Source, FramesPerSecond, Fallback))!.Message,
                    Does.Contain("declared child effect"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("EFFECT=9,7", "EFFECT=9,-1", false)]
        [TestCase("EFFECT=9,7", "EFFECT=9,7\nEFFECT=9,7", false)]
        [TestCase("TIME=0", "TIME=1", false)]
        [TestCase("[Setup]", "[Setup]\nPOS3=9,1,0,0", false)]
        [TestCase("[Setup]", "[Setup]\nSCL3=9,2,2,2", false)]
        [TestCase("[Setup]", "[Setup]\nOPACITY=9,50", false)]
        [TestCase("[Setup]", "[Setup]\nMOPA=9,0,50", false)]
        [TestCase("[Setup]", "[Setup]\nUVANIM=9,0,.1,.2", false)]
        [TestCase("TIME=0", "TIME=0\nQUAT=9,0,0,0,1", false)]
        [TestCase("7=child.RSE", "7=..\\child.RSE", false)]
        [TestCase("7=child.RSE", "7=child.RSM", false)]
        [TestCase("EFFECT=9,7", "EFFECT=0,7", true)]
        [TestCase("EFFECT=9,7", "EFFECT=99,7", true)]
        [TestCase("EFFECT=9,7", "EFFECT=9,8", true)]
        [TestCase("7=child.RSE", "7=child.RSE\n7=other.RSE", true)]
        public void NestedEffectsRejectUnverifiedSchedulingAndMalformedReferences(string Before, string After, bool InvalidData)
        {
            string Text = SimpleScene.Replace("[ObjectList]", "[ObjectList]\n9=DUMMY_EFFECT_GENERATOR")
                .Replace("[MotionList]", "[EffectList]\n7=child.RSE\n[MotionList]")
                .Replace("TIME=0", "TIME=0\nEFFECT=9,7").Replace(Before, After);
            if (InvalidData)
            {
                Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text));
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text));
            }
        }

        [Test]
        public void UvDummyDoesNotTurnUnknownDeclarationsOrMalformedKeysIntoValidData()
        {
            string Text = SimpleScene.Replace("[ObjectList]", "[ObjectList]\n7=DUMMY_UVANIM");
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text.Replace("DUMMY_UVANIM", "DUMMY_UNKNOWN")));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text + "[KeyData]\nTIME=0\nMOPA=7,0,101\n"));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text + "[KeyData]\nTIME=31\nMOPA=7,0,0\n"));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text + "[KeyData]\nTIME=0\nUVANIM=7,0,NaN,0\n"));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text + "[KeyData]\nTIME=0\nMOPA=8,0,0\n"));
        }

        [Test]
        public void RenderlessRseMetadataAllowsOnlyExplicitlyMarkedGeometryFreeScenes()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-renderless-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, "[Version]\nVERSION=1\nAUTORST=1\n[SceneInformation]\nTotalFrame=5\n"
                    + "[ObjectList]\n7=DUMMY_UVANIM\n[KeyData]\nTIME=0\nMOPA=7,0,0\n");
                RseExporter.Load(Source).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalMeshes, Is.Empty);
                Assert.That(Root.Extras!["rse"]!["dependencies"]!.AsArray(), Is.Empty);
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(5f / 60).Within(1e-6));
                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = Window.Handle,
                        BackBufferWidth = 64,
                        BackBufferHeight = 64,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                using (Animated3DModel Model = new Animated3DModel(Device, Root))
                {
                    Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1f / 60)));
                    Assert.That(Model.AnimationTime, Is.GreaterThan(0));
                    Model.RestartAnimation();
                    Assert.That(Model.AnimationTime, Is.Zero);
                }
                Root.Extras!["rse"]!["renderless"] = false;
                Assert.Throws<InvalidDataException>(() => new Animated3DModel(Device, Root));
                Root.Extras!["rse"]!.AsObject().Remove("renderless");
                Assert.Throws<InvalidDataException>(() => new Animated3DModel(Device, Root));
                ModelRoot Nonempty = RseExporter.Load(Path.Combine(EffectDirectory, "EB_401.RSE"));
                Nonempty.Extras!["rse"]!["renderless"] = true;
                Assert.That(() => new Animated3DModel(Device, Nonempty),
                    Throws.TypeOf<InvalidDataException>().With.Message.Contains("renderless RSE scene must not contain meshes"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30, -1, true)]
        [TestCase(60, -1, false)]
        [TestCase(30, 0, false)]
        [TestCase(60, 1, true)]
        public void NativeIgnoredGeneratorMotionPreservesMetadataWithoutResolvingOrApplyingRsa(
            int FramesPerSecond, int StartFrame, bool Loop)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-generator-motion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, ParticleScene);
                ModelRoot Baseline = RseExporter.Load(Source, FramesPerSecond);
                string Command = $"MOTION=1,7,0,{(Loop ? "LOOP" : "NOLOOP")}";
                string Block = StartFrame < 0 ? $"[Setup]\n{Command}\n" : $"[KeyData]\nTIME={StartFrame}\n{Command}\n";
                string Text = ParticleScene.Replace("[Particle]", "[MotionList]\n7=unused.RSA\n[Particle]")
                    .Replace("[ParticleData]", Block + "[ParticleData]");
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonNode Ignored = Root.Extras!["rse"]!["ignoredGeneratorMotions"]!.AsArray().Single()!;
                Assert.That(Ignored["generatorId"]!.GetValue<int>(), Is.EqualTo(1));
                Assert.That(Ignored["motionId"]!.GetValue<int>(), Is.EqualTo(7));
                Assert.That(Ignored["file"]!.GetValue<string>(), Is.EqualTo("unused.RSA"));
                Assert.That(Ignored["startFrame"]!.GetValue<int>(), Is.EqualTo(StartFrame));
                Assert.That(Ignored["loop"]!.GetValue<bool>(), Is.EqualTo(Loop));
                Assert.That(Root.Extras!["rse"]!["dependencies"]!.AsArray()
                    .Any(Entry => Entry!["kind"]!.GetValue<string>() == "motion"), Is.False);
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(3f / FramesPerSecond).Within(1e-6));
                foreach (Node? Node in Root.LogicalNodes)
                {
                    Node Original = Baseline.LogicalNodes.Single(Other => Other.Name == Node.Name);
                    for (int HalfFrame = 0; HalfFrame <= 6; HalfFrame++)
                    {
                        float Time = HalfFrame / (2f * FramesPerSecond);
                        System.Numerics.Matrix4x4 Actual = Node.GetLocalTransform(Root.LogicalAnimations.Single(), Time).Matrix;
                        System.Numerics.Matrix4x4 Expected = Original.GetLocalTransform(Baseline.LogicalAnimations.Single(), Time).Matrix;
                        Assert.That(Actual, Is.EqualTo(Expected), $"Node {Node.Name}, time {Time}");
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void GeneratorMotionDoesNotWeakenOtherMotionValidation()
        {
            string Text = ParticleScene.Replace("[Particle]", "[MotionList]\n7=unused.RSA\n[Particle]")
                .Replace("[ParticleData]", "[KeyData]\nTIME=0\nMOTION=1,7,0,NOLOOP\n[ParticleData]");
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text.Replace("MOTION=1", "MOTION=10")));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("MOTION=1,7", "MOTION=1,8")));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("TIME=0", "TIME=3")));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(
                Text.Replace("MOTION=1,7,0,NOLOOP", "MOTION=1,7,0,NOLOOP\nMOTION=1,7,0,NOLOOP")));
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text.Replace("MOTION=1,7,0", "MOTION=1,7,1")));
        }

        [TestCase(30)]
        [TestCase(60)]
        public void NativeIgnoredRsmStartIsExplicitMetadataNotAnOpacityOrTimingCommand(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-rsmstart-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Text = ParticleScene.Replace("START=0", "RSMSTART=50");
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.ListParticleGenerator.Single().IgnoredRsmStart, Is.EqualTo(50));
                Assert.That(Scene.ListParticleGenerator.Single().StartOpacity, Is.Zero);
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonNode? Ignored = Root.Extras!["rse"]!["ignoredParticleCommands"]!.AsArray().Single();
                Assert.That(Ignored!["generatorId"]!.GetValue<int>(), Is.EqualTo(1));
                Assert.That(Ignored["command"]!.GetValue<string>(), Is.EqualTo("RSMSTART"));
                Assert.That(Ignored["value"]!.GetValue<int>(), Is.EqualTo(50));
                Animation Timeline = Root.LogicalAnimations.Single();
                Node Particle = Root.LogicalNodes.Single(Node => Node.Name == "rse_object_10");
                Assert.That(Particle.GetLocalTransform(Timeline, 0).Translation, Is.EqualTo(new System.Numerics.Vector3(1, 2, -3)));
                Assert.That(Timeline.Duration, Is.EqualTo(3f / FramesPerSecond).Within(1e-6));
                Material Material = Root.LogicalMaterials.First(Material => Material.Name.StartsWith("rse_10_", StringComparison.Ordinal));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Opacity = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                    $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Opacity.GetPoint(0).W, Is.Zero);
                Assert.That(Opacity.GetPoint(1f / FramesPerSecond).W, Is.EqualTo(1).Within(1e-6));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void SparseParticleRangesDoNotInventDeclarationsOrWeakenTrackOwnership()
        {
            Assert.That(RseReader.Parse(ParticleScene.Replace("PARTICLE=10,10", "PARTICLE=9,11")).DicObject.Count, Is.EqualTo(3));
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(
                ParticleScene.Replace("PARTICLE=10,10", "PARTICLE=11,12")));
            Assert.That(Error!.Message, Does.Contain("Every particle"));
            Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(
                ParticleScene.Replace("SEED=12345", "RSMSTART=1\nRSMSTART=2")));
            Assert.That(Error!.Message, Does.Contain("Duplicate particle RSMSTART"));
            Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(
                ParticleScene.Replace("SEED=12345", "RSMSTART=NaN")));
            Assert.That(Error!.Message, Does.Contain("Invalid integer"));
        }

        [Test]
        public void KeyDataRetainsItsTimeAndDefaultsToZeroInsteadOfResettingAtEachSection()
        {
            string Text = SimpleScene.Replace("TIME=0\n", "").Replace("TIME=2\n", "TIME=2\nSOUND=7\n")
                .Replace("[KeyData]\nTIME=30", "[KeyData]\nMOPA=0,0,100\nTIME=30");
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.MaterialOpacity[0].Keys, Is.EqualTo(new[] { 0, 2, 30 }));
            Assert.That(Scene.HasSoundCues, Is.True);
            Scene = RseReader.Parse(ParticleScene.Replace("[ParticleData]",
                "[KeyData]\nPOS3=1,1,2,3\nSCL3=1,2,3,4\nQUAT=1,0,0,0,1\n[ParticleData]"));
            Assert.That(Scene.DicObject[1].DicGeneratorPosition[0], Is.EqualTo(new System.Numerics.Vector3(1, 2, 3)));
            Assert.That(Scene.DicObject[1].DicGeneratorScale[0], Is.EqualTo(new System.Numerics.Vector3(2, 3, 4)));
            Assert.That(Scene.DicObject[1].DicRotation[0], Is.EqualTo(System.Numerics.Quaternion.Identity));
        }

        [TestCase(30)]
        [TestCase(60)]
        public void UnusedMaterialSlotsKeepIndependentTexturesOpacityAndUvThroughGlbReload(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-unused-material-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_401.RSM"), false, false);
                RsmMaterial SourceMaterial = Package.Document.ListMaterial[0];
                Package.Document.ListMaterial.Add(new RsmMaterial
                {
                    Name = SourceMaterial.Name,
                    TextureWidth = SourceMaterial.TextureWidth,
                    TextureHeight = SourceMaterial.TextureHeight,
                    Opacity = .3f
                });
                using (FileStream Stream = File.Create(Path.Combine(Folder, "EB_401.RSM")))
                using (BigEndianBinaryWriter Writer = new BigEndianBinaryWriter(Stream))
                {
                    RSMWriter.WriteRsm(Writer, Package.Document);
                }

                File.Copy(Path.Combine(EffectDirectory, "EB_401.RST"), Path.Combine(Folder, "EB_401.RST"));
                foreach (bool Particles in new[] { false, true })
                {
                    string Text = Particles ? ParticleScene + "[KeyData]\nTIME=0\nMOPA=0,1,20\n"
                        + "UVANIM=0,1,.25,-.5\n[KeyData]\nTIME=2\nMOPA=0,1,80\n"
                        : SimpleScene.Replace("MOPA=0,0,0\n", "MOPA=0,0,0\nMOPA=0,1,20\n")
                            .Replace("TIME=2", "TIME=2\nMOPA=0,1,80\nUVANIM=0,1,.25,-.5");
                    string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                    File.WriteAllText(Source, Text);
                    RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                    ModelRoot Root = ModelRoot.Load(Output);
                    Assert.That(Root.LogicalMaterials.Count, Is.EqualTo(Particles ? 4 : 2));
                    Material Unused = Root.LogicalMaterials.Single(Material => Material.Name.StartsWith("rse_0_material_1_", StringComparison.Ordinal));
                    Assert.That(Root.LogicalMeshes.SelectMany(Mesh => Mesh.Primitives).Any(Primitive => Primitive.Material == Unused), Is.False);
                    Assert.That(Unused.FindChannel("BaseColor")!.Value.Texture, Is.Not.Null);
                    Assert.That(Unused.Extras!["rsm"]!["opacity"]!.GetValue<float>(), Is.EqualTo(.3f).Within(1e-6));
                    Animation Timeline = Root.LogicalAnimations.Single();
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Alpha = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Unused.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                        .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    Assert.That(Alpha.GetPoint(0).W, Is.EqualTo(.2f).Within(1e-6));
                    Assert.That(Alpha.GetPoint(1f / FramesPerSecond).W, Is.EqualTo(.5f).Within(1e-6));
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector2> Uv = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Unused.LogicalIndex}/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset")
                        .GetSamplerOrNull<System.Numerics.Vector2>().CreateCurveSampler(true);
                    Assert.That(Uv.GetPoint(0), Is.EqualTo(new System.Numerics.Vector2(.25f, -.5f)));
                }
                ModelRoot Ordinary = GltfExporter.CreateGlbFromRSM(
                    Package.Document, ListAnimation: Package.Animations, FPS: FramesPerSecond);
                Assert.That(Ordinary.LogicalMaterials.Count, Is.EqualTo(1), "Standalone model export must retain its prior unused-slot behavior.");
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void SetupMotionStartsOnNativeMinusOneFrameAndUsesIndependentSceneClock(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_549.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.DicObject[0].MotionStartFrame, Is.EqualTo(-1));
            CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, Scene.DicObject[0].FileName), false, false);
            Package.Animations.Add(RSAReader.Parse(File.ReadAllBytes(Path.Combine(EffectDirectory, Scene.DicMotion[0])), "source"));
            ModelRoot Original = GltfExporter.CreateGlbFromRSM(
                Package.Document, ListAnimation: Package.Animations, FPS: FramesPerSecond);
            ModelRoot Root = RseExporter.Load(Source, FramesPerSecond);
            Animation Timeline = Root.LogicalAnimations.Single();
            foreach ((RsaChannel Value, int Index) Channel in Package.Animations[0].ListChannel.Select((Value, Index) => (Value, Index))
                .Where(Channel => Channel.Value.ListKeyframe.Count > 0))
            {
                Node Node = Root.LogicalNodes.Single(Node => Node.Name == $"node_{Channel.Index}");
                Node OriginalNode = Original.LogicalNodes.Single(Node => Node.Name == $"node_{Channel.Index}");
                foreach (int Frame in new[] { 0, 1, 2, 10, 30 })
                {
                    SharpGLTF.Transforms.AffineTransform Expected = OriginalNode.GetLocalTransform(Original.LogicalAnimations.Single(), (Frame + 1f) / FramesPerSecond);
                    SharpGLTF.Transforms.AffineTransform Actual = Node.GetLocalTransform(Timeline, Frame / (float)FramesPerSecond);
                    Assert.That(System.Numerics.Vector3.Distance(Actual.Translation, Expected.Translation), Is.LessThan(1e-5));
                    Assert.That(System.Numerics.Vector3.Distance(Actual.Scale, Expected.Scale), Is.LessThan(1e-5));
                    Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Actual.Rotation, Expected.Rotation)), Is.EqualTo(1).Within(1e-5));
                    Assert.That(Node.GetLocalTransform(Timeline, (Frame + .5f) / FramesPerSecond), Is.EqualTo(Actual));
                }
            }
        }

        [TestCase(30, false)]
        [TestCase(60, false)]
        [TestCase(30, true)]
        [TestCase(60, true)]
        public void TransformClipsLongerThanTheSceneAreClippedWithoutExtendingItsDuration(int FramesPerSecond, bool LittleEndian)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-clipped-rsa-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST", ".RSA" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_414" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                if (LittleEndian)
                {
                    string Motion = Path.Combine(Folder, "EB_401.RSA");
                    File.WriteAllBytes(Motion, ToLittleEndianMotion(File.ReadAllBytes(Motion)));
                }
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(Folder, "EB_401.RSM"), false, false);
                Package.Animations.Add(RSAReader.Parse(File.ReadAllBytes(Path.Combine(Folder, "EB_401.RSA")), "source"));
                ModelRoot Original = GltfExporter.CreateGlbFromRSM(Package.Document, ListAnimation: Package.Animations, FPS: FramesPerSecond);
                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, "[Version]\nVERSION=1\nAUTORST=1\n[SceneInformation]\nTotalFrame=3\n"
                    + "[ObjectList]\n0=EB_401.RSM\n[MotionList]\n0=EB_401.RSA\n[KeyData]\nTIME=0\nMOTION=0,0,0,NOLOOP\n");
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.Duration, Is.EqualTo(3f / FramesPerSecond).Within(1e-6));
                for (int Index = 0; Index < Package.Animations[0].ListChannel.Count; Index++)
                {
                    if (Package.Animations[0].ListChannel[Index].ListKeyframe.Count == 0)
                    {
                        continue;
                    }

                    Node Node = Root.LogicalNodes.Single(Node => Node.Name == $"node_{Index}" || Node.Name == $"rse_0_node_{Index}");
                    Node OriginalNode = Original.LogicalNodes.Single(Node => Node.Name == $"node_{Index}");
                    foreach (float Frame in new[] { 0f, .5f, 1f, 1.5f, 2f, 2.5f, 3f })
                    {
                        SharpGLTF.Transforms.AffineTransform Expected = OriginalNode.GetLocalTransform(Original.LogicalAnimations.Single(), Math.Min(MathF.Floor(Frame), 2) / FramesPerSecond);
                        SharpGLTF.Transforms.AffineTransform Actual = Node.GetLocalTransform(Timeline, Frame / FramesPerSecond);
                        Assert.That(System.Numerics.Vector3.Distance(Actual.Translation, Expected.Translation), Is.LessThan(1e-5));
                        Assert.That(System.Numerics.Vector3.Distance(Actual.Scale, Expected.Scale), Is.LessThan(1e-5));
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Actual.Rotation, Expected.Rotation)), Is.EqualTo(1).Within(1e-5));
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Key2PosesRetainQuaternionAndScaleButGeneratorSpinOverridesAuthoredOrientation(int FramesPerSecond)
        {
            string Text = ParticleScene.Replace("KEY3=0,0,2,1,2,3,0,0,0,1,1,1", "KEY2=0,0,2,1,2,3,0,0,.6,.8,2,3,4");
            RseScene Scene = RseReader.Parse(Text);
            RseParticleFrame Frame = Scene.DicObject[10].ListParticleFrame[0];
            Assert.That(Frame.QuaternionRotation, Is.EqualTo(new System.Numerics.Quaternion(0, 0, .6f, .8f)));
            Assert.That(Frame.Scale, Is.EqualTo(new System.Numerics.Vector3(2, 3, 4)));
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-key2-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                foreach (int Spin in new[] { 0, 1, 4, 5 })
                {
                    string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                    List<SharpGLTF.Transforms.AffineTransform> Poses = new List<SharpGLTF.Transforms.AffineTransform>();
                    foreach (bool QuaternionKey in new[] { false, true })
                    {
                        string Keys = QuaternionKey ? Text : Text.Replace("KEY2=0,0,2,1,2,3,0,0,.6,.8,2,3,4", "KEY3=0,0,2,1,2,3,0,0,0,2,3,4");
                        File.WriteAllText(Source, Keys.Replace("SPIN=0", $"SPIN={Spin}"));
                        RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                        ModelRoot Root = ModelRoot.Load(Output);
                        Node Node = Root.LogicalNodes.Single(Node => Node.Name == "rse_object_10");
                        Poses.Add(Node.GetLocalTransform(Root.LogicalAnimations.Single(), 0));
                    }
                    Assert.That(Poses[1], Is.EqualTo(Poses[0]), $"SPIN={Spin}");
                    Assert.That(Poses[1].Scale, Is.EqualTo(new System.Numerics.Vector3(2, 3, 4)));
                }
                foreach (int Spin in new[] { 2, 3 })
                {
                    Assert.That(() => RseReader.Parse(Text.Replace("SPIN=0", $"SPIN={Spin}")),
                        Throws.TypeOf<NotSupportedException>().With.Message.Contains("KEY2"));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("0,0,0,0")]
        [TestCase("NaN,0,0,1")]
        public void MalformedKey2QuaternionsFailExplicitly(string Rotation)
        {
            string Text = ParticleScene.Replace("KEY3=0,0,2,1,2,3,0,0,0,1,1,1", $"KEY2=0,0,2,1,2,3,{Rotation},1,1,1");
            Assert.That(() => RseReader.Parse(Text, "key2.RSE"), Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("key2.RSE"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedPlaylistIdentitiesRequireIdenticalImageRecords(bool DifferentPixels)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-duplicate-ifl-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                File.Copy(Path.Combine(EffectDirectory, "ES_020_01.RSM"), Path.Combine(Folder, "EB_401.RSM"));
                List<RstRecord> Records = RstReader.ReadRst(File.ReadAllBytes(Path.Combine(EffectDirectory, "ES_020_01.RST")), out byte[] Footer);
                uint Hash = BinaryPrimitives.ReadUInt32BigEndian(Footer.AsSpan(12, 4));
                RstRecord Original = Records.First(Record => Record.HeaderWord == Hash && Record.ArgbPixels != null);
                RstRecord Duplicate = new RstRecord
                {
                    HeaderWord = Original.HeaderWord,
                    NameKey = Original.NameKey,
                    NameKey2 = Original.NameKey2,
                    Flags = Original.Flags,
                    Tail = Original.Tail,
                    Width = Original.Width,
                    Height = Original.Height,
                    ArgbPixels = (byte[])Original.ArgbPixels.Clone()
                };
                Duplicate.Tail ^= 1;
                if (DifferentPixels)
                {
                    Duplicate.ArgbPixels[0] ^= 1;
                }

                Records.Add(Duplicate);
                File.WriteAllBytes(Path.Combine(Folder, "EB_401.RST"), RstWriter.WriteRst(Records, IsBigEndian: true, Footer: Footer));
                string Source = Path.Combine(Folder, "scene.RSE");
                File.WriteAllText(Source, ParticleScene);
                if (DifferentPixels)
                {
                    Assert.That(() => RseExporter.Load(Source), Throws.TypeOf<InvalidDataException>()
                        .With.Message.Contains("ambiguous"));
                }
                else
                {
                    ModelRoot Root = RseExporter.Load(Source);
                    System.Text.Json.Nodes.JsonNode Playlist = Root.LogicalMaterials.First(Material =>
                    {
                        if (Material.Extras == null)
                        {
                            return false;
                        }

                        return Material.Extras["ifl"] != null;
                    }).Extras!["ifl"]!;
                    Assert.That(Playlist["frames"]!.AsArray().Count, Is.EqualTo(4));
                    string Output = Path.Combine(Folder, "scene.glb");
                    Root.SaveGLB(Output);
                    Assert.That(ModelRoot.Load(Output).LogicalTextures.Count, Is.GreaterThanOrEqualTo(4));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void ModelQuaternionKeysSlerpWhileBakedGeneratorQuaternionKeysDoNotTransformParticlesAgain(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-quat-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, SimpleScene.Replace("TIME=0", "TIME=0\nQUAT=0,0,0,0,1")
                    .Replace("TIME=2", "TIME=2\nQUAT=0,0,0,1,0"));
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Node Node = Root.LogicalNodes.Single(Node => Node.Name == "rse_object_0");
                System.Numerics.Quaternion Actual = Node.GetLocalTransform(Root.LogicalAnimations.Single(), 1f / FramesPerSecond).Rotation;
                System.Numerics.Quaternion Expected = System.Numerics.Quaternion.Slerp(System.Numerics.Quaternion.Identity, new System.Numerics.Quaternion(0, 0, -1, 0), .5f);
                Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Actual, Expected)), Is.EqualTo(1).Within(1e-6));
                File.WriteAllText(Source, ParticleScene + "[KeyData]\nTIME=0\nQUAT=1,0,0,1,0\n");
                Root = RseExporter.Load(Source, FramesPerSecond);
                Node = Root.LogicalNodes.Single(Node => Node.Name == "rse_object_10");
                Assert.That(Node.GetLocalTransform(Root.LogicalAnimations.Single(), 0).Translation, Is.EqualTo(new System.Numerics.Vector3(1, 2, -3)));
                Assert.That(Root.LogicalNodes.Any(Node => Node.Name == "rse_object_1"), Is.False);
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void OverlappingFadeWindowsKeepNativeBranchOrderAndSpinZeroIgnoresVelocity(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-fade-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Text = ParticleScene.Substring(0, ParticleScene.IndexOf("KEY3=", StringComparison.Ordinal))
                    .Replace("TotalFrame=3", "TotalFrame=10").Replace("FADEIN=10", "FADEIN=80").Replace("FADEOUT=90", "FADEOUT=20");
                for (int Frame = 0; Frame < 10; Frame++)
                {
                    Text += $"KEY3={Frame},{Frame},10,0,0,0,0,0,0,1,1,1\n";
                }

                List<SharpGLTF.Transforms.AffineTransform> Poses = new List<SharpGLTF.Transforms.AffineTransform>();
                foreach (bool WithVelocity in new[] { false, true })
                {
                    string Source = Path.Combine(Folder, "scene.RSE");
                    File.WriteAllText(Source, WithVelocity ? Text.Replace("SPIN=0", "SPIN=0\nROTX=12,5\nROTY=2,3\nROTZ=9,1") : Text);
                    ModelRoot Root = RseExporter.Load(Source, FramesPerSecond);
                    Animation Timeline = Root.LogicalAnimations.Single();
                    Material Material = Root.LogicalMaterials.Single(Material => Material.Name.StartsWith("rse_10_", StringComparison.Ordinal));
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Alpha = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                        .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    foreach ((int Age, float Alpha) Sample in new[] { (Age: 0, Alpha: 0f), (Age: 4, Alpha: .5f), (Age: 8, Alpha: 1f), (Age: 9, Alpha: .125f) })
                    {
                        Assert.That(Alpha.GetPoint(Sample.Age / (float)FramesPerSecond).W, Is.EqualTo(Sample.Alpha).Within(1e-6));
                    }

                    Node Node = Root.LogicalNodes.Single(Node => Node.Name == "rse_object_10");
                    Poses.Add(Node.GetLocalTransform(Timeline, 9f / FramesPerSecond));
                }
                Assert.That(Poses[1], Is.EqualTo(Poses[0]));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(0)]
        [TestCase(50)]
        [TestCase(100)]
        public void IdenticalOpacityKeysAreIdempotentAcrossTimelineBlocks(int Percent)
        {
            string Text = SimpleScene.Replace("MOPA=0,0,100",
                $"MOPA=0,0,{Percent}\nMOPA=0,0,{Percent}\n[KeyData]\nTIME=2\nMOPA=0,0,{Percent}");
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.MaterialOpacity[0].Count, Is.EqualTo(3));
            Assert.That(Scene.MaterialOpacity[0][2], Is.EqualTo(Percent / 100f));
        }

        [TestCase(30)]
        [TestCase(60)]
        public void DelayedUvKeysExtrapolateTheirFirstSegmentAndHoldTheirLastKey(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-delayed-uv-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "scene.RSE");
                foreach (bool Single in new[] { false, true })
                {
                    File.WriteAllText(Source, SimpleScene.Replace("TIME=2", "TIME=2\nUVANIM=0,0,.25,-.5")
                        .Replace("TIME=30", (Single ? "" : "TIME=10\nUVANIM=0,0,-.75,1.5\n[KeyData]\n") + "TIME=30"));
                    string Output = Path.Combine(Folder, "scene.glb");
                    RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                    ModelRoot Root = ModelRoot.Load(Output);
                    IAnimationSampler<System.Numerics.Vector2> Sampler = Root.LogicalAnimations.Single().Channels.Single(Channel => Channel.TargetPointerPath != null
                        && Channel.TargetPointerPath.EndsWith("/offset", StringComparison.Ordinal))
                        .GetSamplerOrNull<System.Numerics.Vector2>();
                    Assert.That(Sampler.InterpolationMode, Is.EqualTo(AnimationInterpolationMode.LINEAR));
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector2> Curve = Sampler.CreateCurveSampler(true);
                    foreach (float Frame in new[] { 0f, .5f, 1f, 2f, 6f, 10f, 30f })
                    {
                        System.Numerics.Vector2 Expected = new System.Numerics.Vector2(.25f, -.5f);
                        if (!Single)
                        {
                            Expected += new System.Numerics.Vector2(-1, 2) * ((Math.Min(Frame, 10) - 2) / 8);
                        }

                        Assert.That(System.Numerics.Vector2.Distance(Curve.GetPoint(Frame / FramesPerSecond), Expected),
                            Is.LessThan(1e-6), $"single={Single}, frame={Frame}");
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30, true)]
        [TestCase(60, true)]
        [TestCase(30, false)]
        [TestCase(60, false)]
        public void ParticleTexturePlaylistsPreserveDurationsAndNativeBirthResetConditions(int FramesPerSecond, bool BigEndian)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-particle-ifl-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "ES_020_01" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                if (!BigEndian)
                {
                    string TexturePath = Path.Combine(Folder, "EB_401.RST");
                    List<RstRecord> Records = RstReader.ReadRst(File.ReadAllBytes(TexturePath), out byte[] Footer);
                    for (int Position = 0; Position < Footer.Length; Position += 12)
                    {
                        Array.Reverse(Footer, Position, 4);
                        Array.Reverse(Footer, Position + 4, 2);
                        Array.Reverse(Footer, Position + 6, 2);
                        Array.Reverse(Footer, Position + 8, 4);
                    }
                    File.WriteAllBytes(TexturePath, RstWriter.WriteRst(Records, IsBigEndian: false, Footer: Footer));
                }
                (int, int)[] Ages = new[] { (-1, 0), (-1, 0), (0, 0), (5, 10), (7, 10), (0, 0),
                    (0, 10), (0, 0), (0, 0), (-1, 0), (0, 10), (20, 10) };
                string Text = ParticleScene.Substring(0, ParticleScene.IndexOf("KEY3=", StringComparison.Ordinal))
                    .Replace("TotalFrame=3", "TotalFrame=12");
                for (int Frame = 0; Frame < Ages.Length; Frame++)
                {
                    Text += $"KEY3={Frame},{Ages[Frame].Item1},{Ages[Frame].Item2},0,0,0,0,0,0,1,1,1\n";
                }

                string Source = Path.Combine(Folder, "scene.RSE");
                File.WriteAllText(Source, Text);
                string Output = Path.Combine(Folder, "scene.glb");
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                foreach ((string Prefix, int[] Indices) Expected in new[]
                {
                    (Prefix: "rse_0_", Indices: new[] { 0, 0, 1, 1, 2, 2, 3, 3, 0, 0, 1, 1, 1 }),
                    (Prefix: "rse_10_", Indices: new[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 2, 2, 2 })
                })
                {
                    System.Text.Json.Nodes.JsonNode Ifl = Root.LogicalMaterials.Single(Material => Material.Name.StartsWith(Expected.Prefix, StringComparison.Ordinal)
                        && Material.Extras != null && Material.Extras["ifl"] != null).Extras!["ifl"]!;
                    Assert.That(Ifl["frames"]!.AsArray().Count, Is.EqualTo(4));
                    Assert.That(Ifl["rseFramesPerSecond"]!.GetValue<float>(), Is.EqualTo(FramesPerSecond));
                    Assert.That(Ifl["rseFrameIndices"]!.AsArray().Select(Value => Value!.GetValue<int>()), Is.EqualTo(Expected.Indices));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void VariableDurationTexturePlaylistsKeepRepeatedImagesAndCumulativeBoundaries()
        {
            ModelRoot Root = RseExporter.Load(Path.Combine(EffectDirectory, "EB_478.RSE"));
            System.Text.Json.Nodes.JsonNode Ifl = Root.LogicalMaterials.Single(Material => Material.Name.StartsWith("rse_100_", StringComparison.Ordinal)
                && Material.Extras != null && Material.Extras["ifl"] != null).Extras!["ifl"]!;
            int[] Frames = Ifl["frames"]!.AsArray().Select(Value => Value!.GetValue<int>()).ToArray();
            int[] Indices = Ifl["rseFrameIndices"]!.AsArray().Select(Value => Value!.GetValue<int>()).ToArray();
            int[] Ends = { 1, 2, 3, 4, 6, 8, 10, 13, 16, 19, 22, 25, 30, 37, 55 };
            Assert.That(Frames.Length, Is.EqualTo(15), "The playlist revisits eight images; it is not an eight-frame fixed-rate loop.");
            for (int Frame = 0; Frame < 77; Frame++)
            {
                Assert.That(Indices[Frame], Is.EqualTo(Array.FindIndex(Ends, End => Frame % 55 < End)), $"frame {Frame}");
            }

            byte[] Image(int Entry)
            {
                return Root.LogicalTextures[Frames[Entry]].PrimaryImage.Content.Content.ToArray();
            }

            Assert.That(Image(1), Is.EqualTo(Image(13)));
            Assert.That(Image(0), Is.EqualTo(Image(14)));
        }

        [TestCase("empty")]
        [TestCase("truncated")]
        [TestCase("missing-image")]
        [TestCase("missing-list")]
        [TestCase("zero-boundary")]
        [TestCase("unordered-boundaries")]
        [TestCase("trailing-bytes")]
        public void InvalidTexturePlaylistsFailWithTheirSourcePath(string Invalid)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-invalid-playlist-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                File.Copy(Path.Combine(EffectDirectory, "ES_020_01.RSM"), Path.Combine(Folder, "EB_401.RSM"));
                byte[] Bytes = File.ReadAllBytes(Path.Combine(EffectDirectory, "ES_020_01.RST"));
                int Footer = Bytes.Length - 60;
                void Word(int Offset, uint Value)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(Offset, 4), Value);
                }

                switch (Invalid)
                {
                    case "empty": Word(Footer + 8, 0); break;
                    case "truncated": Word(Footer + 8, 5); break;
                    case "missing-image": Word(Footer + 12, 0); break;
                    case "missing-list": Word(Footer, 0); break;
                    case "zero-boundary": Word(Footer + 20, 0); break;
                    case "unordered-boundaries": Word(Footer + 32, 1); break;
                    case "trailing-bytes": Array.Resize(ref Bytes, Bytes.Length + 1); break;
                }
                string TexturePath = Path.Combine(Folder, "EB_401.RST");
                File.WriteAllBytes(TexturePath, Bytes);
                string Source = Path.Combine(Folder, "scene.RSE");
                File.WriteAllText(Source, ParticleScene);
                Assert.That(() => RseExporter.Load(Source), Throws.TypeOf<InvalidDataException>()
                    .With.Message.Contains(TexturePath).And.Message.Contains("playlist"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30, false)]
        [TestCase(60, false)]
        [TestCase(30, true)]
        [TestCase(60, true)]
        public void ParticleModelMotionUsesBirthRelativeSceneTimeInsteadOfAge(int FramesPerSecond, bool Loop)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-particle-motion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                CuldceptModel Package = CuldceptModel.Load(Path.Combine(Folder, "EB_401.RSM"), false, false);
                int Count = Package.Document.ListNode.Count;
                int Offset = 24 + Count * 8;
                byte[] Motion = new byte[Offset + 3 * 48];
                new byte[] { (byte)'r', (byte)'s', (byte)'a', 0, 0, 0, 0, 1 }.CopyTo(Motion, 0);
                void Short(int Position, ushort Value)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(Motion.AsSpan(Position, 2), Value);
                }

                void Float(int Position, float Value)
                {
                    BinaryPrimitives.WriteSingleBigEndian(Motion.AsSpan(Position, 4), Value);
                }

                Short(8, (ushort)Count);
                Float(16, 5);
                Float(20, 1);
                Short(24, (ushort)RsaChannelKind.TransformKeys);
                Short(26, 3);
                BinaryPrimitives.WriteUInt32BigEndian(Motion.AsSpan(28, 4), (uint)Offset);
                for (int Key = 0; Key < 3; Key++)
                {
                    int Frame = Key * 2, Position = Offset + Key * 48;
                    Float(Position + 4, Frame);
                    Float(Position + 8, 10 + Frame * 2);
                    Float(Position + 12, 20 + Frame * 3);
                    Float(Position + 16, 30 + Frame * 4);
                    System.Numerics.Quaternion Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, Frame * .1f);
                    Float(Position + 28, Rotation.Z);
                    Float(Position + 32, Rotation.W);
                    Float(Position + 36, 1 + Frame * .25f);
                    Float(Position + 40, 2 + Frame * .5f);
                    Float(Position + 44, 3 + Frame * .75f);
                }
                File.WriteAllBytes(Path.Combine(Folder, "motion.RSA"), Motion);
                foreach (bool InitiallyActive in new[] { false, true })
                {
                    (int, int)[] Ages = new[] { (InitiallyActive ? 0 : -1, InitiallyActive ? 9 : 0), (-1, 0), (0, 0),
                        (5, 9), (6, 9), (0, 0), (4, 9), (2, 9), (-1, 0), (0, 9), (15, 9), (0, 0),
                        (2, 9), (3, 9), (4, 9), (5, 9), (6, 9), (7, 9) };
                    string Text = ParticleScene.Substring(0, ParticleScene.IndexOf("KEY3=", StringComparison.Ordinal))
                        .Replace("TotalFrame=3", "TotalFrame=18")
                        .Replace("10=DUMMY_PARTICLE", "10=DUMMY_PARTICLE\n11=DUMMY_PARTICLE")
                        .Replace("PARTICLE=10,10", "PARTICLE=10,11")
                        .Replace("[Particle]", "[MotionList]\n0=motion.RSA\n[KeyData]\nTIME=0\nMOTION=0,0,0,NOLOOP\n[Particle]");
                    if (Loop)
                    {
                        Text = Text.Replace("NOLOOP", "LOOP");
                    }

                    for (int Frame = 0; Frame < Ages.Length; Frame++)
                    {
                        Text += $"KEY3={Frame},{Ages[Frame].Item1},{Ages[Frame].Item2},0,0,0,0,0,0,1,1,1\n";
                    }

                    Text += "OBJNO=11\n";
                    for (int Frame = 0; Frame < Ages.Length; Frame++)
                    {
                        Text += $"KEY3={Frame},{100 + Frame},200,0,0,0,0,0,0,1,1,1\n";
                    }

                    string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                    File.WriteAllText(Source, Text);
                    RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                    ModelRoot Root = ModelRoot.Load(Output);
                    Animation Timeline = Root.LogicalAnimations.Single();
                    foreach (int Id in new[] { 0, 10, 11 })
                    {
                        Node Node = Root.LogicalNodes.Single(Node => Node.Name == $"rse_{Id}_node_0");
                        if (Id != 0)
                        {
                            foreach (AnimationChannel? Channel in Timeline.Channels.Where(Channel => Channel.TargetNode == Node))
                            {
                                AnimationInterpolationMode Interpolation = Channel.GetSamplerOrNull<System.Numerics.Quaternion>().InterpolationMode;
                                IAnimationSampler<System.Numerics.Vector3> VectorSampler = Channel.GetSamplerOrNull<System.Numerics.Vector3>();
                                if (VectorSampler != null)
                                {
                                    Interpolation = VectorSampler.InterpolationMode;
                                }

                                Assert.That(Interpolation, Is.EqualTo(AnimationInterpolationMode.STEP));
                            }
                        }

                        for (int Frame = 0; Frame <= 18; Frame++)
                        {
                            int SampleFrame = Math.Min(Frame, 17);
                            int? Birth = Id != 10 ? 0 : SampleFrame >= 12 ? 12 : SampleFrame >= 6 ? 6
                                : SampleFrame >= 3 ? 3 : InitiallyActive ? 0 : null;
                            float Relative = Loop ? (SampleFrame - Birth.GetValueOrDefault()) % 5
                                : Math.Min(SampleFrame - Birth.GetValueOrDefault(), 4);
                            SharpGLTF.Transforms.AffineTransform Pose = Node.GetLocalTransform(Timeline, Frame / (float)FramesPerSecond);
                            RsmNode Bind = Package.Document.ListNode[0];
                            System.Numerics.Vector3 Position = Birth.HasValue ? new System.Numerics.Vector3(10 + Relative * 2, 20 + Relative * 3, -(30 + Relative * 4))
                                : new System.Numerics.Vector3(Bind.Translation.X, Bind.Translation.Y, -Bind.Translation.Z);
                            System.Numerics.Vector3 Scale = Birth.HasValue ? new System.Numerics.Vector3(1 + Relative * .25f, 2 + Relative * .5f, 3 + Relative * .75f)
                                : Bind.Scale;
                            Assert.That(System.Numerics.Vector3.Distance(Pose.Translation, Position), Is.LessThan(1e-4), $"id={Id}, frame={Frame}");
                            Assert.That(System.Numerics.Vector3.Distance(Pose.Scale, Scale), Is.LessThan(1e-5));
                            if (Birth.HasValue)
                            {
                                Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Pose.Rotation,
                                    System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, -Relative * .1f))),
                                    Is.EqualTo(1).Within(1e-5));
                            }

                            if (Id != 0 && Frame < 18)
                            {
                                Assert.That(Node.GetLocalTransform(Timeline, (Frame + .5f) / FramesPerSecond), Is.EqualTo(Pose));
                            }
                        }
                    }
                    File.WriteAllText(Source, Text.Replace("TIME=0\nMOTION", "TIME=1\nMOTION"));
                    Assert.That(() => RseExporter.Load(Source), Throws.TypeOf<NotSupportedException>()
                        .With.Message.Contains("particle source motions"));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30, false, false)]
        [TestCase(60, false, false)]
        [TestCase(30, true, false)]
        [TestCase(60, true, false)]
        [TestCase(30, false, true)]
        [TestCase(60, false, true)]
        [TestCase(30, true, true)]
        [TestCase(60, true, true)]
        public void VertexMotionsUseNativeForwardRecordSelectionAndPreserveBindPose(int FramesPerSecond, bool Loop, bool LittleEndian)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-vertex-motion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_491" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                CuldceptModel Package = CuldceptModel.Load(Path.Combine(Folder, "EB_401.RSM"), false, false);
                foreach (bool Normals in new[] { false, true })
                {
                    byte[] Motion = CreateVertexMotion(Package, Normals);
                    File.WriteAllBytes(Path.Combine(Folder, "vertex.RSA"), LittleEndian ? ToLittleEndianMotion(Motion) : Motion);
                    foreach (string Mode in new[] { "simple", "delayed", "particles", "setup" })
                    {
                        int Start = Mode == "delayed" ? 2 : Mode == "setup" ? -1 : 0;
                        int Total = Loop ? 12 : 7;
                        string Text = Mode == "particles" ? ParticleScene.Substring(0, ParticleScene.IndexOf("KEY3=", StringComparison.Ordinal))
                            .Replace("TotalFrame=3", $"TotalFrame={Total}")
                            : SimpleScene.Replace("TotalFrame=31", $"TotalFrame={Total}").Replace("TIME=30", "TIME=6");
                        if (Mode == "particles")
                        {
                            (int, int)[] Ages = new[] { (0, 0), (5, 9), (6, 9), (0, 0), (2, 9), (1, 9), (20, 9),
                                (3, 9), (4, 9), (5, 9), (6, 9), (7, 9) };
                            for (int Frame = 0; Frame < Total; Frame++)
                            {
                                Text += $"KEY3={Frame},{Ages[Frame].Item1},{Ages[Frame].Item2},0,0,0,0,0,0,1,1,1\n";
                            }

                            Text = Text.Replace("[Particle]", "[MotionList]\n0=vertex.RSA\n[KeyData]\nTIME=0\nMOTION=0,0,0,NOLOOP\n[Particle]");
                        }
                        else
                        {
                            Text = Text.Replace("[MotionList]", "[MotionList]\n0=vertex.RSA");
                            Text = Mode == "setup" ? Text.Replace("[Setup]", "[Setup]\nMOTION=0,0,0,NOLOOP")
                                : Text.Replace("[Setup]", $"[KeyData]\nTIME={Start}\nMOTION=0,0,0,NOLOOP\n[Setup]");
                        }
                        if (Loop)
                        {
                            Text = Text.Replace("NOLOOP", "LOOP");
                        }

                        string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                        File.WriteAllText(Source, Text);
                        RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                        ModelRoot Root = ModelRoot.Load(Output);
                        Animation Timeline = Root.LogicalAnimations.Single();
                        Assert.That(Timeline.Duration, Is.EqualTo(Total / (float)FramesPerSecond).Within(1e-6));
                        AnimationChannel[] Morphs = Timeline.Channels.Where(Channel =>
                        {
                            if (Channel.TargetNode == null)
                            {
                                return false;
                            }

                            return Channel.TargetNode.Mesh != null;
                        }).ToArray();
                        Assert.That(Morphs.Length, Is.EqualTo(Mode == "particles" ? 2 : 1));
                        foreach (AnimationChannel? Channel in Morphs)
                        {
                            bool Particle = Channel.TargetNode.Mesh.Name.StartsWith("rse_10_", StringComparison.Ordinal);
                            IAnimationSampler<float[]> Sampler = Channel.GetSamplerOrNull<float[]>();
                            Assert.That(Sampler.InterpolationMode, Is.EqualTo(AnimationInterpolationMode.STEP));
                            SharpGLTF.Animations.ICurveSampler<float[]> Curve = Sampler.CreateCurveSampler(true);
                            for (int Frame = 0; Frame <= Total; Frame++)
                            {
                                int Sample = Math.Min(Frame, Total - 1);
                                int? Relative = Particle ? Sample == 0 ? null : Sample - (Sample >= 4 ? 4 : 1)
                                    : Sample >= Start ? Sample - Start : null;
                                if (Loop)
                                {
                                    Relative %= 5;
                                }

                                int Target = Relative.HasValue ? Relative.Value == 0 ? 0 : Relative.Value <= 2 ? 1 : 2 : -1;
                                float[] Weights = Curve.GetPoint(Frame / (float)FramesPerSecond);
                                Assert.That(Weights, Is.EqualTo(Enumerable.Range(0, 3).Select(Index => Index == Target ? 1f : 0f)),
                                    $"{Mode}, frame={Frame}, normals={Normals}");
                                if (Frame < Total)
                                {
                                    Assert.That(Curve.GetPoint((Frame + .5f) / FramesPerSecond), Is.EqualTo(Weights));
                                }
                            }
                            foreach (MeshPrimitive? Primitive in Channel.TargetNode.Mesh.Primitives)
                            {
                                SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> Base = Primitive.GetVertexAccessor("POSITION").AsVector3Array();
                                for (int Target = 0; Target < 3; Target++)
                                {
                                    SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> Delta = Primitive.GetMorphTargetAccessors(Target)["POSITION"].AsVector3Array();
                                    System.Numerics.Vector3 ExpectedDelta = new System.Numerics.Vector3(.5f + Target * 2, Target * .5f, Target);
                                    for (int Vertex = 0; Vertex < Base.Count; Vertex++)
                                    {
                                        Assert.That(System.Numerics.Vector3.Distance(Delta[Vertex], ExpectedDelta), Is.LessThan(1e-4));
                                    }

                                    Assert.That(Primitive.GetMorphTargetAccessors(Target).ContainsKey("NORMAL"), Is.EqualTo(Normals));
                                }
                            }
                        }
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        private static byte[] ToLittleEndianMotion(byte[] Source)
        {
            byte[] Bytes = Source.ToArray();
            Bytes[0] = (byte)'R'; Bytes[1] = (byte)'S'; Bytes[2] = (byte)'A';
            void Reverse(int Offset, int Size)
            {
                Array.Reverse(Bytes, Offset, Size);
            }

            Reverse(4, 2); Reverse(6, 2); Reverse(8, 2);
            Reverse(12, 4); Reverse(16, 4); Reverse(20, 4);
            int Nodes = BinaryPrimitives.ReadUInt16BigEndian(Source.AsSpan(8, 2));
            for (int Node = 0; Node < Nodes; Node++)
            {
                int Entry = 24 + Node * 8;
                int Kind = BinaryPrimitives.ReadUInt16BigEndian(Source.AsSpan(Entry, 2));
                int Count = BinaryPrimitives.ReadUInt16BigEndian(Source.AsSpan(Entry + 2, 2));
                int Offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(Source.AsSpan(Entry + 4, 4)));
                Reverse(Entry, 2); Reverse(Entry + 2, 2); Reverse(Entry + 4, 4);
                for (int Frame = 0; Frame < Count; Frame++)
                {
                    if (Kind == (int)RsaChannelKind.TransformKeys)
                    {
                        // Native conversion leaves the unused flag word unchanged.
                        for (int Field = 4; Field < 48; Field += 4)
                        {
                            Reverse(Offset + Field, 4);
                        }

                        Offset += 48;
                    }
                    else if (Kind == (int)RsaChannelKind.VertexFrames)
                    {
                        int Vectors = BinaryPrimitives.ReadUInt16BigEndian(Source.AsSpan(Offset + 4, 2))
                            + BinaryPrimitives.ReadUInt16BigEndian(Source.AsSpan(Offset + 6, 2));
                        Reverse(Offset, 4); Reverse(Offset + 4, 2); Reverse(Offset + 6, 2);
                        for (int Field = 8; Field < 8 + Vectors * 12; Field += 4)
                        {
                            Reverse(Offset + Field, 4);
                        }

                        Offset += 8 + Vectors * 12;
                    }
                }
            }
            return Bytes;
        }

        [TestCase("truncated")]
        [TestCase("offset")]
        [TestCase("repeated-count")]
        [TestCase("nan-position")]
        [TestCase("bad-version")]
        public void LittleEndianVertexPayloadsStillFailExplicitlyWhenMalformed(string Invalid)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-little-rsa-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                File.Copy(Path.Combine(EffectDirectory, "EB_491.RSM"), Path.Combine(Folder, "EB_401.RSM"));
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(Folder, "EB_401.RSM"), false, false);
                byte[] Bytes = ToLittleEndianMotion(CreateVertexMotion(Package, true));
                int Offset = 24 + Package.Document.ListNode.Count * 8, FrameSize = (Bytes.Length - Offset) / 3;
                switch (Invalid)
                {
                    case "truncated": Array.Resize(ref Bytes, Bytes.Length - 1); break;
                    case "offset": BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(28, 4), uint.MaxValue); break;
                    case "repeated-count": BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(Offset + FrameSize + 4, 2), 1); break;
                    case "nan-position": BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(Offset + 8, 4), float.NaN); break;
                    case "bad-version": Bytes[6] = 2; break;
                }
                string Source = Path.Combine(Folder, "scene.RSE"), MotionPath = Path.Combine(Folder, "vertex.RSA");
                File.WriteAllBytes(MotionPath, Bytes);
                File.WriteAllText(Source, SimpleScene.Replace("AUTORST=1", "AUTORST=0")
                    .Replace("[MotionList]", "[MotionList]\n0=vertex.RSA")
                    .Replace("TIME=0", "TIME=0\nMOTION=0,0,0,NOLOOP"));
                Assert.That(() => RseExporter.Load(Source), Throws.TypeOf<InvalidDataException>()
                    .With.Message.Contains(MotionPath).And.Message.Contains("RSA"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        private static byte[] CreateVertexMotion(CuldceptModel Package, bool Normals)
        {
            RsmMesh Mesh = Package.Document.DicMeshByNodeIndex[0];
            int Offset = 24 + Package.Document.ListNode.Count * 8;
            int NormalCount = Normals ? Mesh.ListNormal.Count : 0;
            int FrameSize = 8 + 12 * (Mesh.ListPosition.Count + NormalCount);
            byte[] Bytes = new byte[Offset + 3 * FrameSize];
            new byte[] { (byte)'r', (byte)'s', (byte)'a', 0, 0, 0, 0, 1 }.CopyTo(Bytes, 0);
            void Short(int Position, ushort Value)
            {
                BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan(Position, 2), Value);
            }

            void Float(int Position, float Value)
            {
                BinaryPrimitives.WriteSingleBigEndian(Bytes.AsSpan(Position, 4), Value);
            }

            Short(8, (ushort)Package.Document.ListNode.Count);
            Float(16, 5);
            Float(20, 1);
            Short(24, (ushort)RsaChannelKind.VertexFrames);
            Short(26, 3);
            BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(28, 4), (uint)Offset);
            for (int Key = 0; Key < 3; Key++)
            {
                int Position = Offset + Key * FrameSize;
                Float(Position, Key * 2);
                Short(Position + 4, (ushort)Mesh.ListPosition.Count);
                Short(Position + 6, (ushort)NormalCount);
                Position += 8;
                foreach (System.Numerics.Vector3 Vertex in Mesh.ListPosition.Select(Vertex => Vertex + new System.Numerics.Vector3(.5f + Key * 2, Key * .5f, -Key))
                    .Concat(Normals ? Mesh.ListNormal.Select(Normal => System.Numerics.Vector3.Normalize(
                        Normal + new System.Numerics.Vector3(.1f, .2f, .3f))) : Array.Empty<System.Numerics.Vector3>()))
                {
                    Float(Position, Vertex.X);
                    Float(Position + 4, Vertex.Y);
                    Float(Position + 8, Vertex.Z);
                    Position += 12;
                }
            }
            return Bytes;
        }

        [TestCase("truncated")]
        [TestCase("offset")]
        [TestCase("overlap")]
        [TestCase("vertex-count")]
        [TestCase("normal-count")]
        [TestCase("repeated-count")]
        [TestCase("nan-position")]
        [TestCase("nan-normal")]
        [TestCase("duplicate-frame")]
        [TestCase("fractional-frame")]
        [TestCase("nan-frame")]
        [TestCase("past-end")]
        public void InvalidVertexMotionsFailWithTheirSourcePath(string Invalid)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-invalid-vertex-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_491" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                CuldceptModel Package = CuldceptModel.Load(Path.Combine(Folder, "EB_401.RSM"), false, false);
                byte[] Bytes = CreateVertexMotion(Package, true);
                int Offset = 24 + Package.Document.ListNode.Count * 8;
                int FrameSize = (Bytes.Length - Offset) / 3;
                void Short(int Position, ushort Value)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan(Position, 2), Value);
                }

                void Float(int Position, float Value)
                {
                    BinaryPrimitives.WriteSingleBigEndian(Bytes.AsSpan(Position, 4), Value);
                }

                switch (Invalid)
                {
                    case "truncated": Array.Resize(ref Bytes, Bytes.Length - 1); break;
                    case "offset": BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(28, 4), uint.MaxValue); break;
                    case "overlap": BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(28, 4), 24); break;
                    case "vertex-count": Short(Offset + 4, 1); break;
                    case "normal-count": Short(Offset + 6, 1); break;
                    case "repeated-count": Short(Offset + FrameSize + 4, 1); break;
                    case "nan-position": Float(Offset + 8, float.NaN); break;
                    case "nan-normal": Float(Offset + 8 + Package.Document.DicMeshByNodeIndex[0].ListPosition.Count * 12, float.NaN); break;
                    case "duplicate-frame": Float(Offset + FrameSize, 0); break;
                    case "fractional-frame": Float(Offset + FrameSize, 1.5f); break;
                    case "nan-frame": Float(Offset + FrameSize, float.NaN); break;
                    case "past-end": Float(Offset + FrameSize * 2, 5); break;
                }
                string MotionPath = Path.Combine(Folder, "vertex.RSA"), Source = Path.Combine(Folder, "scene.RSE");
                File.WriteAllBytes(MotionPath, Bytes);
                File.WriteAllText(Source, SimpleScene.Replace("[MotionList]", "[MotionList]\n0=vertex.RSA")
                    .Replace("TIME=0", "TIME=0\nMOTION=0,0,0,NOLOOP"));
                Assert.That(() => RseExporter.Load(Source), Throws.TypeOf<InvalidDataException>()
                    .With.Message.Contains(MotionPath).And.Message.Contains("RSA"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void DelayedVertexMotionCanBeClippedByTheSceneEnd(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "ES_030_03.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            ModelRoot Root = RseExporter.Load(Source, FramesPerSecond);
            Assert.That(Scene.DicObject[0].MotionStartFrame, Is.EqualTo(17));
            Animation Timeline = Root.LogicalAnimations.Single();
            Assert.That(Timeline.Duration, Is.EqualTo(63f / FramesPerSecond).Within(1e-6));
            AnimationChannel Channel = Timeline.Channels.Single(Channel =>
            {
                if (Channel.TargetNode == null)
                {
                    return false;
                }

                return Channel.TargetNode.Mesh != null;
            });
            SharpGLTF.Animations.ICurveSampler<float[]> Curve = Channel.GetSamplerOrNull<float[]>().CreateCurveSampler(true);
            foreach (int Frame in new[] { 0, 16, 17, 18, 40, 62, 63 })
            {
                float[] Weights = Curve.GetPoint(Frame / (float)FramesPerSecond);
                int Target = Frame < 17 ? -1 : Math.Min(Frame, 62) - 17;
                Assert.That(Weights, Is.EqualTo(Enumerable.Range(0, 62).Select(Index => Index == Target ? 1f : 0f)));
            }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb442ObjectOpacityFadesEveryMaterialWithoutReplacingItsAlpha(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_442.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.DicObject[0].DicObjectOpacity.Keys, Is.EqualTo(new[] { 0, 72, 82 }));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-object-opacity-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Material[] Materials = Root.LogicalMaterials.Where(Material =>
                {
                    if (Material.Extras == null)
                    {
                        return false;
                    }

                    return Material.Extras["rsm"] != null;
                }).ToArray();
                Assert.That(Materials, Is.Not.Empty);
                foreach (Material Material in Materials)
                {
                    float SourceAlpha = Material.Extras["rsm"]!["opacity"]!.GetValue<float>();
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Curve = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                        .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    foreach ((float Frame, float Alpha) Expected in new[] { (Frame: 0f, Alpha: 1f), (Frame: 72f, Alpha: 1f),
                        (Frame: 77f, Alpha: .5f), (Frame: 82f, Alpha: 0f) })
                    {
                        Assert.That(Curve.GetPoint(Expected.Frame / FramesPerSecond).W,
                            Is.EqualTo(SourceAlpha * Expected.Alpha).Within(1e-6));
                    }
                }
                Assert.That(Timeline.Channels.Any(Channel => Channel.TargetNode != null), Is.True);
            }
            finally { File.Delete(Output); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void ObjectAndMaterialOpacityMultiplyInsteadOfOverwritingEachOther(int FramesPerSecond)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-opacity-product-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "opacity.RSE");
                File.WriteAllText(Source, SimpleScene.Replace("TIME=0", "TIME=0\nOPACITY=0,100")
                    .Replace("TIME=2", "TIME=2\nOPACITY=0,50").Replace("TIME=30", "TIME=30\nOPACITY=0,0")
                    .Replace("[Setup]", "[Setup]\nOPACITY=0,0"));
                ModelRoot Root = RseExporter.Load(Source, FramesPerSecond);
                string Output = Path.Combine(Folder, "opacity.glb");
                Root.SaveGLB(Output);
                Root = ModelRoot.Load(Output);
                IAnimationSampler<System.Numerics.Vector4> Sampler = Root.LogicalAnimations.Single().Channels.Single(Channel => Channel.TargetPointerPath ==
                    "/materials/0/pbrMetallicRoughness/baseColorFactor").GetSamplerOrNull<System.Numerics.Vector4>();
                Assert.That(Sampler.InterpolationMode, Is.EqualTo(AnimationInterpolationMode.STEP));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Curve = Sampler.CreateCurveSampler(true);
                Assert.That(Curve.GetPoint(1f / FramesPerSecond).W, Is.EqualTo(.375f).Within(1e-6));
                Assert.That(Curve.GetPoint(1.75f / FramesPerSecond).W, Is.EqualTo(.375f).Within(1e-6));
                Assert.That(Curve.GetPoint(2f / FramesPerSecond).W, Is.EqualTo(.5f).Within(1e-6));
                Assert.That(Curve.GetPoint(16f / FramesPerSecond).W, Is.EqualTo(.125f).Within(1e-6));
                Assert.That(Curve.GetPoint(30f / FramesPerSecond).W, Is.Zero);
                File.WriteAllText(Source, SimpleScene.Replace("[Setup]", "[Setup]\nOPACITY=0,0\nMOPA=0,0,50")
                    .Replace("MOPA=0,0,0", "OPACITY=0,100").Replace("MOPA=0,0,100", "OPACITY=0,50"));
                Root = RseExporter.Load(Source, FramesPerSecond);
                Root.SaveGLB(Output);
                Root = ModelRoot.Load(Output);
                Curve = Root.LogicalAnimations.Single().Channels.Single(Channel => Channel.TargetPointerPath ==
                    "/materials/0/pbrMetallicRoughness/baseColorFactor").GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Curve.GetPoint(0).W, Is.EqualTo(.5f).Within(1e-6),
                    "Timed OPACITY replaces setup object opacity, while retaining independent slot setup.");
                Assert.That(Curve.GetPoint(2f / FramesPerSecond).W, Is.EqualTo(.25f).Within(1e-6));
                File.WriteAllText(Source, SimpleScene.Replace("TIME=0\nMOPA=0,0,0", "TIME=0\nOPACITY=0,100"));
                Root = RseExporter.Load(Source, FramesPerSecond);
                Curve = Root.LogicalAnimations.Single().Channels.Single(Channel => Channel.TargetPointerPath ==
                    "/materials/0/pbrMetallicRoughness/baseColorFactor").GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Curve.GetPoint(1f / FramesPerSecond).W, Is.EqualTo(.5f).Within(1e-6),
                    "Combining opacity retains existing MOPA interpolation from the initial slot value.");
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("TIME=0\nOPACITY=0,50\nOPACITY=0,50\n", null)]
        [TestCase("TIME=0\nOPACITY=0,50\nOPACITY=0,75\n", "conflicting")]
        [TestCase("OPACITY=0,50\nTIME=0\n", null)]
        [TestCase("TIME=31\nOPACITY=0,50\n", "outside TotalFrame")]
        [TestCase("TIME=0\nOPACITY=0,101\n", "opacity")]
        public void ObjectOpacityValidatesTimelineKeys(string Commands, string? Expected)
        {
            string Text = SimpleScene.Replace("[Setup]", "[Setup]\n[KeyData]\n" + Commands);
            if (Expected == null)
            {
                Assert.That(RseReader.Parse(Text).DicObject[0].DicObjectOpacity[0], Is.EqualTo(.5f));
            }
            else
            {
                Assert.That(Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text, "opacity.RSE"))!.Message,
                    Does.Contain("opacity.RSE").And.Contain(Expected));
            }
        }

        [Test]
        public void DelayedFirstObjectOpacityKeyIsExplicitlyUnsupported()
        {
            string Text = SimpleScene.Replace("TIME=2", "TIME=2\nOPACITY=0,50");
            Assert.That(Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text, "delayed.RSE"))!.Message,
                Does.Contain("delayed.RSE").And.Contain("frame-zero"));
        }

        [TestCase(1)]
        [TestCase(10)]
        public void ObjectOpacityOnParticleDummiesIsExplicitlyUnsupported(int ObjectId)
        {
            string Text = ParticleScene + $"[KeyData]\nTIME=0\nOPACITY={ObjectId},50\n";
            Assert.That(Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text, "particle-opacity.RSE"))!.Message,
                Does.Contain("particle-opacity.RSE").And.Contain("particle/generator"));
        }

        [Test]
        public void Es004OpacitySamplesRemainInRange()
        {
            ModelRoot Root = RseExporter.Load(Path.Combine(EffectDirectory, "ES_004.RSE"));
            foreach (AnimationChannel Channel in Root.LogicalAnimations.Single().Channels.Where(Channel =>
            {
                if (Channel.TargetPointerPath == null)
                {
                    return false;
                }

                return Channel.TargetPointerPath.EndsWith("/baseColorFactor", StringComparison.Ordinal);
            }))
            {
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Curve = Channel.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                for (int Quarter = 0; Quarter < 39 * 4; Quarter++)
                {
                    System.Numerics.Vector4 Color = Curve.GetPoint(Quarter / 240f);
                    Assert.That(new[] { Color.X, Color.Y, Color.Z, Color.W }, Is.All.InRange(0f, 1f),
                        $"{Channel.TargetPointerPath}, frame {Quarter / 4f}: {Color}");
                }
            }
        }

        [TestCase(30, false)]
        [TestCase(60, false)]
        [TestCase(60, true)]
        public void RawOpacityPreservesSignedCurvesAndUsesOnlyBoundedGltfFactors(int FramesPerSecond, bool ObjectFade)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-raw-opacity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Text = SimpleScene.Replace("MOPA=0,0,100", "MOPA=0,0,200");
                if (ObjectFade)
                {
                    Text += "[KeyData]\nTIME=0\nOPACITY=0,100\n[KeyData]\nTIME=30\nOPACITY=0,-41\n";
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.MaterialOpacity[0][2], Is.EqualTo(2));
                Assert.That(Scene.ListRawOpacityCommand.Count, Is.EqualTo(ObjectFade ? 2 : 1));
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonNode Raw = Root.LogicalMaterials[0].Extras!["rseRawOpacity"]!;
                Assert.That(Raw["interpolation"]!.GetValue<string>(), Is.EqualTo(ObjectFade ? "STEP" : "LINEAR"));
                Assert.That(Raw["fallback"]!.GetValue<string>(), Does.Contain("approximation"));
                float[] Times = Raw["times"]!.AsArray().Select(Value => Value!.GetValue<float>()).ToArray();
                float[] Values = Raw["values"]!.AsArray().Select(Value => Value!.GetValue<float>()).ToArray();
                Assert.That(Values.Max(), Is.GreaterThan(1));
                Assert.That(Root.Extras!["rse"]!["rawOpacityCommands"]!.AsArray().Count, Is.EqualTo(Scene.ListRawOpacityCommand.Count));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Fallback = Root.LogicalAnimations.Single().Channels.Single(Channel => Channel.TargetPointerPath ==
                    "/materials/0/pbrMetallicRoughness/baseColorFactor").GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                for (int Frame = 0; Frame <= 31; Frame++)
                {
                    float Material = Frame <= 2 ? Frame : Frame <= 30 ? 2 * (30 - Frame) / 28f : 0;
                    float Expected = Material * (ObjectFade ? 1 - 1.41f * Math.Min(Frame, 30) / 30f : 1);
                    int Index = Array.FindLastIndex(Times, Time => Time <= Frame / (float)FramesPerSecond);
                    float Actual = Values[Index];
                    if (!ObjectFade && Index + 1 < Times.Length)
                    {
                        Actual += (Values[Index + 1] - Actual) * ((Frame / (float)FramesPerSecond - Times[Index]) / (Times[Index + 1] - Times[Index]));
                    }

                    Assert.That(Actual, Is.EqualTo(Expected).Within(1e-5), $"Raw frame {Frame}");
                    Assert.That(Fallback.GetPoint(Frame / (float)FramesPerSecond).W,
                        Is.EqualTo(Math.Clamp(Expected, 0, 1)).Within(1e-5), $"Fallback frame {Frame}");
                }
                Text = SimpleScene.Replace("[Setup]", "[Setup]\nMOPA=0,0,-41")
                    .Replace("TIME=0\nMOPA=0,0,0", "TIME=0");
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                Raw = ModelRoot.Load(Output).LogicalMaterials[0].Extras!["rseRawOpacity"]!;
                Assert.That(Raw["values"]!.AsArray()[0]!.GetValue<float>(), Is.EqualTo(-.41f).Within(1e-6));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("EE_735", 30)]
        [TestCase("EE_735", 60)]
        [TestCase("ES_081", 30)]
        [TestCase("ES_081", 60)]
        public void AuthoredRawOpacityScenesPreserveKeysAndDisplayExplicitUiWarnings(string SceneName, int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, SceneName + ".RSE");
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-authored-raw-{Guid.NewGuid():N}.glb");
            try
            {
                RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
                Assert.That(Scene.ListRawOpacityCommand, Is.Not.Empty);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.Extras!["rse"]!["rawOpacityCommands"]!.AsArray().Count, Is.EqualTo(Scene.ListRawOpacityCommand.Count));
                Material[] RawMaterials = Root.LogicalMaterials.Where(Material =>
                {
                    if (Material.Extras == null)
                    {
                        return false;
                    }

                    return Material.Extras["rseRawOpacity"] != null;
                }).ToArray();
                Assert.That(RawMaterials.Length, Is.EqualTo(SceneName == "EE_735" ? 1 : 2));
                foreach (KeyValuePair<int, SortedDictionary<int, float>> Entry in Scene.MaterialOpacity)
                {
                    Material Material = Root.LogicalMaterials[Entry.Key];
                    System.Text.Json.Nodes.JsonNode? Raw = null;
                    if (Material.Extras != null)
                    {
                        Raw = Material.Extras["rseRawOpacity"];
                    }
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Bounded = Root.LogicalAnimations.Single().Channels.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                        .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    foreach (KeyValuePair<int, float> Key in Entry.Value)
                    {
                        if (Raw != null)
                        {
                            System.Text.Json.Nodes.JsonArray Times = Raw["times"]!.AsArray();
                            int Index = Enumerable.Range(0, Times.Count).Single(Index =>
                                Math.Abs(Times[Index]!.GetValue<float>() - Key.Key / (float)FramesPerSecond) < 1e-6);
                            Assert.That(Raw["values"]!.AsArray()[Index]!.GetValue<float>(), Is.EqualTo(Key.Value).Within(1e-6));
                        }
                        Assert.That(Bounded.GetPoint(Key.Key / (float)FramesPerSecond).W,
                            Is.EqualTo(Math.Clamp(Key.Value, 0, 1)).Within(1e-6));
                    }
                }
                if (FramesPerSecond != 60)
                {
                    return;
                }

                using CuldceptSagaExplorer Window = new global::CuldceptSagaExplorer.CuldceptSagaExplorer { ShowInTaskbar = false };
                global::GraphicsManager.Model3DViewer Viewer = Window.Controls.OfType<global::GraphicsManager.Model3DViewer>().Single();
                _ = Window.Handle;
                _ = Viewer.Handle;
                System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Window.GetType().GetMethod("LoadEffect", Flags)!.Invoke(Window, new object[] { Source });
                Label Status = (Label)Window.GetType().GetField("EffectStatus", Flags)!.GetValue(Window)!;
                Assert.That(Status.Text, Does.Contain("raw opacity is preserved").And.Contain("bounded approximation")
                    .And.Contain("native blend state is unverified"));
                Viewer.ExportEffect(Output);
                Assert.That(ModelRoot.Load(Output).LogicalMaterials.Count(Material =>
                {
                    if (Material.Extras == null)
                    {
                        return false;
                    }

                    System.Text.Json.Nodes.JsonNode? Rendering = Material.Extras["rendering"];
                    if (Rendering == null)
                    {
                        return false;
                    }

                    return Rendering["opacityTrack"] != null;
                }),
                    Is.EqualTo(RawMaterials.Length));
            }
            finally { File.Delete(Output); }
        }

        [TestCase("EE_735")]
        [TestCase("ES_081")]
        public void RawOpacityScenesRenderEveryFrameDirectlyAndAfterReload(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true,
                        RequireVisible: true, SampleEveryFrame: true);
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb437RepeatedOpacityKeysExportWithoutLosingMotionOrMaterialSlots(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_437.RSE");
            string Text = File.ReadAllText(Source);
            RseScene Scene = RseReader.Parse(Text, Source);
            Assert.That(Scene.MaterialOpacity.Count, Is.EqualTo(7));
            Assert.That(Scene.MaterialOpacity[5].Count, Is.EqualTo(51));
            Assert.That(Scene.MaterialOpacity[6].Count, Is.EqualTo(51));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-duplicates-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.Duration, Is.EqualTo(51f / FramesPerSecond).Within(1e-6));
                Assert.That(Root.LogicalMaterials.Any(Material =>
                    {
                        if (Material.Extras == null)
                        {
                            return false;
                        }

                        return Material.Extras["ifl"] != null;
                    }), Is.True,
                    "This scene also exercises texture-list playback on the RSE clock.");
                Assert.That(Timeline.Channels.Any(Channel => Channel.TargetNode != null), Is.True,
                    "Accepting duplicate opacity must not drop the declared RSA.");
                AnimationChannel[] Tracks = Timeline.Channels.Where(Channel => Channel.TargetPointerPath != null
                    && Channel.TargetPointerPath.EndsWith("/pbrMetallicRoughness/baseColorFactor", StringComparison.Ordinal)).ToArray();
                Assert.That(Tracks.Length, Is.EqualTo(7));
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_437.RSM"), false, false);
                bool DuplicateNames = Package.Document.ListMaterial.Select(Material => Material.Name).Distinct().Count()
                    != Package.Document.ListMaterial.Count;
                foreach (KeyValuePair<int, SortedDictionary<int, float>> Entry in Scene.MaterialOpacity)
                {
                    string OriginalName = Package.Document.ListMaterial[Entry.Key].Name;
                    Material Material = Root.LogicalMaterials.Single(Material =>
                        Material.Name == (DuplicateNames ? $"rse_0_material_{Entry.Key}_{OriginalName}" : $"rse_0_{OriginalName}"));
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Curve = Tracks.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                        .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    foreach (KeyValuePair<int, float> Key in Entry.Value)
                    {
                        Assert.That(Curve.GetPoint(Key.Key / (float)FramesPerSecond).W, Is.EqualTo(Key.Value).Within(1e-6));
                    }

                    Assert.That(Root.LogicalMeshes.SelectMany(Mesh => Mesh.Primitives).Any(Primitive => Primitive.Material == Material), Is.True);
                }
            }
            finally { File.Delete(Output); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb440ExportsProjectedBillboardsAndItsFrameOneMotion(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_440.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.DicObject[0].MotionStartFrame, Is.EqualTo(1));
            Assert.That(Scene.ListParticleGenerator.Single().Spin, Is.EqualTo(5));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-vertical-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.Duration, Is.EqualTo(81f / FramesPerSecond).Within(1e-6));
                Assert.That(Root.LogicalNodes.Count(Node =>
                {
                    if (Node.Extras == null)
                    {
                        return false;
                    }

                    System.Text.Json.Nodes.JsonNode? Billboard = Node.Extras["rseBillboard"];
                    if (Billboard == null)
                    {
                        return false;
                    }

                    return Billboard.GetValue<string>() == "verticalProjected";
                }),
                    Is.EqualTo(12));
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_440.RSM"), false, false);
                RsaClip Clip = RSAReader.Parse(File.ReadAllBytes(Path.Combine(EffectDirectory, "EB_440.RSA")), "source");
                for (int Index = 0; Index < Clip.ListChannel.Count; Index++)
                {
                    if (Clip.ListChannel[Index].ListKeyframe.Count == 0)
                    {
                        continue;
                    }

                    Node Node = Root.LogicalNodes.Single(Node => Node.Name == $"rse_0_node_{Index}");
                    RsmNode Bind = Package.Document.ListNode[Index];
                    foreach (float Frame in new[] { 0f, .5f })
                    {
                        SharpGLTF.Transforms.AffineTransform Pose = Node.GetLocalTransform(Timeline, Frame / FramesPerSecond);
                        System.Numerics.Vector3 Position = new System.Numerics.Vector3(Bind.Translation.X, Bind.Translation.Y, -Bind.Translation.Z);
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Translation, Position), Is.LessThan(1e-5));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Scale, Bind.Scale), Is.LessThan(1e-5));
                    }
                    foreach (RsaTransformKeyframe Key in Clip.ListChannel[Index].ListKeyframe)
                    {
                        SharpGLTF.Transforms.AffineTransform Pose = Node.GetLocalTransform(Timeline, (Key.Frame + 1) / FramesPerSecond);
                        System.Numerics.Vector3 Position = new System.Numerics.Vector3(Key.Translation.X, Key.Translation.Y, -Key.Translation.Z);
                        System.Numerics.Quaternion Rotation = System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(
                            Key.Rotation.X, Key.Rotation.Y, -Key.Rotation.Z, Key.Rotation.W));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Translation, Position), Is.LessThan(1e-5));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Scale, Key.Scale), Is.LessThan(1e-5));
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Pose.Rotation, Rotation)), Is.EqualTo(1).Within(1e-5));
                    }
                }
                foreach ((int Id, float Angle, float Velocity) Expected in new[] { (Id: 1000, Angle: 62.02f, Velocity: .17f), (Id: 1001, Angle: -67.14f, Velocity: .11f) })
                {
                    Node Particle = Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Expected.Id}");
                    Node Spin = Root.LogicalNodes.Single(Node => Node.Name == $"rse_spin_{Expected.Id}");
                    float Angle = Expected.Angle * (MathF.PI / 180);
                    foreach (RseParticleFrame Frame in Scene.DicObject[Expected.Id].ListParticleFrame)
                    {
                        if (Frame.Frame > 0 && Frame.Age >= 0 && Frame.Lifetime > 0)
                        {
                            Angle += Expected.Velocity * (MathF.PI / 180);
                        }

                        SharpGLTF.Transforms.AffineTransform Pose = Particle.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        SharpGLTF.Transforms.AffineTransform SpinPose = Spin.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        System.Numerics.Quaternion Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, Angle);
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(SpinPose.Rotation, Rotation)), Is.EqualTo(1).Within(1e-6));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Translation,
                            new System.Numerics.Vector3(Frame.Position.X, Frame.Position.Y, -Frame.Position.Z)), Is.LessThan(1e-5));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Scale, Frame.Scale), Is.LessThan(1e-5));
                    }
                }
            }
            finally { File.Delete(Output); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void EmptySceneExportsAndReopensWithoutInventedGeometry(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_425_01.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.IsEmpty, Is.True);
            Assert.That(Scene.TotalFrames, Is.EqualTo(2));
            Assert.That(Scene.SceneNumber, Is.EqualTo(-1));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-empty-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalMeshes, Is.Empty);
                Assert.That(Root.LogicalMaterials, Is.Empty);
                Assert.That(Root.Extras!["rse"]!["empty"]!.GetValue<bool>(), Is.True);
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(2f / FramesPerSecond).Within(1e-6));
                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = Window.Handle,
                        BackBufferWidth = 64,
                        BackBufferHeight = 64,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                using AlphaTestEffect Effect = new AlphaTestEffect(Device);
                using RenderTarget2D Target = new RenderTarget2D(Device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                using Animated3DModel Model = new Animated3DModel(Device, Root);
                Assert.That(Model.ListTexture, Is.Empty);
                Model.IsPaused = true;
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
                Assert.That(Model.AnimationTime, Is.Zero);
                Model.IsPaused = false;
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.5 / FramesPerSecond)));
                Assert.That(Model.AnimationTime, Is.GreaterThan(0));
                Model.RestartAnimation();
                Assert.That(Model.AnimationTime, Is.Zero);
                Device.SetRenderTarget(Target);
                try
                {
                    Device.Clear(Color.CornflowerBlue);
                    Model.Draw(Device, new GameTime(), Effect);
                }
                finally { Device.SetRenderTarget(null); }
                Color[] Pixels = new Color[64 * 64];
                Target.GetData(Pixels);
                Assert.That(Pixels.All(Pixel => Pixel == Color.CornflowerBlue), Is.True);
                Root.Extras!["rse"]!["empty"] = false;
                Assert.Throws<InvalidDataException>(() => new Animated3DModel(Device, Root),
                    "Unmarked geometry-free models must still fail explicitly.");
            }
            finally { File.Delete(Output); }
        }

        [Test]
        public void ParserPreservesRepeatedBlocksAndPercentageKeys()
        {
            RseScene Scene = RseReader.Parse(SimpleScene);
            Assert.That(Scene.TotalFrames, Is.EqualTo(31));
            Assert.That(Scene.ObjectId, Is.Zero);
            Assert.That(Scene.ModelFileName, Is.EqualTo("EB_401.RSM"));
            Assert.That(Scene.AutoTexture, Is.True);
            Assert.That(Scene.Loop, Is.Null, "An absent LOOP is unknown, not a game loop default.");
            Assert.That(Scene.MaterialOpacity[0].Keys, Is.EqualTo(new[] { 0, 2, 30 }));
            Assert.That(Scene.MaterialOpacity[0].Values, Is.EqualTo(new[] { 0f, 1f, 0f }));
            Scene = RseReader.Parse(SimpleScene.Replace("[Setup]", "[Setup]\nOPACITY=0,25\nMOPA=0,0,50"));
            Assert.That(Scene.SetupOpacity[-1], Is.EqualTo(0.25f));
            Assert.That(Scene.SetupOpacity[0], Is.EqualTo(0.5f));
            Scene = RseReader.Parse(SimpleScene.Replace("[Setup]", "[Setup]\nMOPA=0,0,50\nOPACITY=0,25"));
            Assert.That(Scene.SetupOpacity.Keys, Is.EqualTo(new[] { -1 }), "A later object-wide opacity replaces earlier slot setup.");
        }

        [Test]
        public void SoundCuesAndMissingSoundReferencesDoNotBlockVisualParsing()
        {
            string Text = SimpleScene.Replace("[MotionList]", "[SoundList]\n0=missing.xma\n[MotionList]")
                .Replace("[Setup]", "[Setup]\nSOUND=0")
                .Replace("TIME=2\n", "TIME=2\nSOUND=7\n");
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.HasSoundCues, Is.True);
            Assert.That(Scene.MaterialOpacity[0][2], Is.EqualTo(1));
            Assert.That(RseReader.Parse(SimpleScene).HasSoundCues, Is.False);
            Assert.That(RseReader.Parse(Text.Replace("SOUND=7", "#SOUND=7").Replace("SOUND=0", "#SOUND=0")).HasSoundCues, Is.False);
        }

        [TestCase("TIME=2", "TIME=2\nSOUND=invalid", "integer")]
        [TestCase("TIME=2", "TIME=2\nSOUND=-1", "non-negative")]
        [TestCase("TIME=2", "TIME=31\nSOUND=0", "TotalFrame")]
        [TestCase("[MotionList]", "[SoundList]\n0=\n[MotionList]", "non-empty")]
        [TestCase("[MotionList]", "[SoundList]\n0=a.xma\n0=b.xma\n[MotionList]", "unique")]
        [TestCase("MOPA=0,0,100", "MOPA=0,0,2147483648", "integer")]
        [TestCase("MOPA=0,0,100", "MOPA=0,-1,50", "slot")]
        [TestCase("MOPA=0,0,100", "MOPA=1,0,50", "undeclared")]
        [TestCase("TIME=2", "TIME=31", "TotalFrame")]
        [TestCase("TIME=2", "TIME=NaN", "integer")]
        [TestCase("TIME=2\n", "", "Duplicate")]
        [TestCase("MOPA=0,0,100", "MOPA=0,0,100\nMOPA=0,0,50", "Duplicate")]
        [TestCase("TotalFrame=31", "TotalFrame=0", "positive")]
        public void InvalidCommandsFailWithSourceContext(string Before, string After, string Expected)
        {
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(SimpleScene.Replace(Before, After), "example.RSE"));
            Assert.That(Error!.Message, Does.Contain("example.RSE").And.Contain("line").And.Contain(Expected));
        }

        [TestCase("MOTION=0,0")]
        [TestCase("MOTION=0,0,0,LOOP")]
        [TestCase("MOTION=0,0,0,123,LOOP")]
        public void ExplicitAndImplicitLoopingMotionFormsRetainNativeLoopFlag(string Command)
        {
            string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=clip.RSA")
                .Replace("TIME=0", "TIME=0\n" + Command);
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.DicObject[0].MotionLoops, Is.True);
            Assert.That(Scene.DicObject[0].MotionStartFrame, Is.Zero);
            Assert.That(Scene.DicObject[0].MotionId, Is.Zero);
        }

        [TestCase("0=EB_401.RSM", "0=DUMMY_PARTICLE_GENERATOR")]
        [TestCase("0=EB_401.RSM", "0=..\\EB_401.RSM")]
        [TestCase("[KeyData]\nTIME=2", "[KeyData]\nTIME=2\nPOS3=0,1,2,3")]
        [TestCase("[MotionList]", "[Particle]\nSPIN=6")]
        [TestCase("AUTORST=1\n", "")]
        public void UnsupportedScenesAreRejectedInsteadOfDroppingBehavior(string Before, string After)
        {
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(SimpleScene.Replace(Before, After)));
        }

        [TestCase("")]
        [TestCase("0,")]
        [TestCase("37,")]
        public void MotionAndUvKeysPreserveExplicitReferencesAndOffsets(string LegacyValue)
        {
            string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n7=EB_414.RSA")
                .Replace("TIME=0", $"TIME=0\nMOTION=0,7,0,{LegacyValue}NOLOOP\nUVANIM=0,0,.25,-.5")
                .Replace("TIME=30", "TIME=30\nUVANIM=0,0,-.25,.75");
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.DicMotion[7], Is.EqualTo("EB_414.RSA"));
            Assert.That(Scene.DicObject[0].MotionId, Is.EqualTo(7));
            Assert.That(Scene.DicObject[0].DicMaterialUv[0][0], Is.EqualTo(new System.Numerics.Vector2(.25f, -.5f)));
            Assert.That(Scene.DicObject[0].DicMaterialUv[0][30], Is.EqualTo(new System.Numerics.Vector2(-.25f, .75f)));
        }

        [TestCase("MOTION=0,9,0,NOLOOP", "undeclared motion")]
        [TestCase("MOTION=0,-1,0,NOLOOP", "non-negative")]
        [TestCase("MOTION=1,0,0,NOLOOP", "undeclared object")]
        [TestCase("MOTION=0,0,0,NOLOOP\nMOTION=0,0,0,NOLOOP", "Duplicate")]
        [TestCase("MOTION=0,0,0,invalid,NOLOOP", "integer")]
        [TestCase("MOTION=0,0,0,0,0,NOLOOP", "four or five")]
        [TestCase("UVANIM=0,-1,0,0", "slot")]
        [TestCase("UVANIM=0,0,NaN,0", "finite")]
        [TestCase("UVANIM=0,0,0,0\nUVANIM=0,0,1,1", "Duplicate")]
        public void InvalidMotionAndUvCommandsFailWithContext(string Command, string Expected)
        {
            string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=EB_414.RSA")
                .Replace("TIME=0", "TIME=0\n" + Command);
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text, "motion.RSE"));
            Assert.That(Error!.Message, Does.Contain("motion.RSE").And.Contain("line").And.Contain(Expected));
        }

        [TestCase(30, " O", false)]
        [TestCase(60, "O", true)]
        [TestCase(60, "0suffix", false)]
        [TestCase(30, "-0suffix", true)]
        [TestCase(60, "", false)]
        [TestCase(60, "+", false)]
        public void NativeMotionTransitionDecimalPrefixIsExplicitAndMatchesZeroTransitionAnimation(
            int FramesPerSecond, string Token, bool Setup)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-native-transition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST", ".RSA" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_414" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=EB_401.RSA");
                Text = Setup ? Text.Replace("[Setup]", "[Setup]\nMOTION=0,0,0,NOLOOP")
                    : Text.Replace("TIME=0", "TIME=0\nMOTION=0,0,0,NOLOOP");
                File.WriteAllText(Source, Text);
                ModelRoot Baseline = RseExporter.Load(Source, FramesPerSecond);
                Text = Text.Replace("MOTION=0,0,0,NOLOOP", $"MOTION=0,0,{Token},NOLOOP");
                RseScene Scene = RseReader.Parse(Text);
                RseNativeMotionTransition Native = Scene.ListNativeMotionTransition.Single();
                Assert.That(Native.ObjectId, Is.Zero);
                Assert.That(Native.Frame, Is.EqualTo(Setup ? -1 : 0));
                Assert.That(Native.Value, Is.EqualTo(Token));
                Assert.That(Native.Transition, Is.Zero);
                Assert.That(Scene.DicObject[0].MotionId, Is.Zero, "This motion is active, not rejected.");
                Assert.That(Scene.ListRejectedMotionCommand, Is.Empty);
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonNode Metadata = Root.Extras!["rse"]!["nativeMotionTransitions"]!.AsArray().Single()!;
                Assert.That(Metadata["objectId"]!.GetValue<int>(), Is.Zero);
                Assert.That(Metadata["frame"]!.GetValue<int>(), Is.EqualTo(Setup ? -1 : 0));
                Assert.That(Metadata["value"]!.GetValue<string>(), Is.EqualTo(Token));
                Assert.That(Metadata["transition"]!.GetValue<int>(), Is.Zero);
                Assert.That(Root.Extras!["rse"]!["dependencies"]!.AsArray()
                    .Count(Entry => Entry!["kind"]!.GetValue<string>() == "motion"), Is.EqualTo(1));
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(31f / FramesPerSecond).Within(1e-6));
                foreach (Node? Node in Root.LogicalNodes)
                {
                    Node Original = Baseline.LogicalNodes.Single(Other => Other.Name == Node.Name);
                    for (int HalfFrame = 0; HalfFrame <= 62; HalfFrame++)
                    {
                        float Time = HalfFrame / (2f * FramesPerSecond);
                        SharpGLTF.Transforms.AffineTransform Actual = Node.GetLocalTransform(Root.LogicalAnimations.Single(), Time);
                        SharpGLTF.Transforms.AffineTransform Expected = Original.GetLocalTransform(Baseline.LogicalAnimations.Single(), Time);
                        Assert.That(System.Numerics.Vector3.Distance(Actual.Translation, Expected.Translation), Is.LessThan(1e-5));
                        Assert.That(System.Numerics.Vector3.Distance(Actual.Scale, Expected.Scale), Is.LessThan(1e-5));
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Actual.Rotation, Expected.Rotation)),
                            Is.EqualTo(1).Within(1e-5));
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("1suffix")]
        [TestCase("-1suffix")]
        [TestCase("01suffix")]
        public void NonzeroNativeMotionTransitionsRemainUnsupported(string Token)
        {
            string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=clip.RSA")
                .Replace("TIME=0", $"TIME=0\nMOTION=0,0,{Token},NOLOOP");
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text));
        }

        [TestCase("2147483648")]
        [TestCase("-2147483649suffix")]
        public void OverflowingNativeMotionTransitionsFailExplicitly(string Token)
        {
            string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=clip.RSA")
                .Replace("TIME=0", $"TIME=0\nMOTION=0,0,{Token},NOLOOP");
            Assert.That(() => RseReader.Parse(Text, "overflow.RSE"),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("exceeds the supported range"));
        }

        [TestCase(30, "NOLLOP", false)]
        [TestCase(60, "NOLLOP", false)]
        [TestCase(60, "INVALID", true)]
        [TestCase(30, "noloop", false)]
        public void NativeRejectedMotionSuffixIsReportedAndPreservedWithoutGuessingOrResolvingItsClip(
            int FramesPerSecond, string Suffix, bool Setup)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-rejected-motion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(Source, SimpleScene);
                ModelRoot Baseline = RseExporter.Load(Source, FramesPerSecond);
                string Command = $"0,7,0,{Suffix}";
                string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n7=unused.RSA");
                Text = Setup ? Text.Replace("[Setup]", $"[Setup]\nMOTION={Command}")
                    : Text.Replace("TIME=0", $"TIME=0\nMOTION={Command}");
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.DicObject[0].MotionId, Is.Null, "A rejected command must not become a corrected animation.");
                Assert.That(Scene.ListRejectedMotionCommand.Single().Value, Is.EqualTo(Command));
                Assert.That(Scene.ListRejectedMotionCommand.Single().Frame, Is.EqualTo(Setup ? -1 : 0));
                File.WriteAllText(Source, Text);
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                System.Text.Json.Nodes.JsonNode Rejected = Root.Extras!["rse"]!["rejectedMotionCommands"]!.AsArray().Single()!;
                Assert.That(Rejected["value"]!.GetValue<string>(), Is.EqualTo(Command));
                Assert.That(Rejected["frame"]!.GetValue<int>(), Is.EqualTo(Setup ? -1 : 0));
                Assert.That(Rejected["reason"]!.GetValue<string>(), Does.Contain("Native MOTION parser rejects"));
                Assert.That(Root.Extras!["rse"]!["dependencies"]!.AsArray()
                    .Any(Entry => Entry!["kind"]!.GetValue<string>() == "motion"), Is.False);
                Assert.That(Root.LogicalAnimations.Single().Duration, Is.EqualTo(31f / FramesPerSecond).Within(1e-6));
                Assert.That(Root.LogicalAnimations.Single().Channels.Count,
                    Is.EqualTo(Baseline.LogicalAnimations.Single().Channels.Count));
                foreach ((AnimationChannel First, AnimationChannel Second) Pair in Root.LogicalAnimations.Single().Channels.Zip(Baseline.LogicalAnimations.Single().Channels))
                {
                    Assert.That(Pair.First.TargetPointerPath, Is.EqualTo(Pair.Second.TargetPointerPath));
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Actual = Pair.First.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Expected = Pair.Second.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    foreach (float Frame in new[] { 0f, .5f, 1f, 2f, 15f, 30f, 31f })
                    {
                        Assert.That(Actual.GetPoint(Frame / FramesPerSecond), Is.EqualTo(Expected.GetPoint(Frame / FramesPerSecond)));
                    }
                }
                foreach (Node? Node in Root.LogicalNodes)
                {
                    Node Original = Baseline.LogicalNodes.Single(Other => Other.Name == Node.Name);
                    Assert.That(Node.LocalTransform.Matrix, Is.EqualTo(Original.LocalTransform.Matrix));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void RejectedMotionWarningsAreVisibleInTheEffectsListBeforeLoading()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-motion-warning-{Guid.NewGuid():N}");
            string EffectsFolder = Path.Combine(Folder, "Culdcept Saga Extracted", "data", "EFFECT");
            Directory.CreateDirectory(EffectsFolder);
            try
            {
                string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=clip.RSA")
                    .Replace("TIME=0", "TIME=0\nMOTION=0,0,0,NOLLOP");
                File.WriteAllText(Path.Combine(EffectsFolder, "rejected.RSE"), Text);
                File.WriteAllText(Path.Combine(EffectsFolder, "valid.RSE"), SimpleScene);
                File.WriteAllText(Path.Combine(EffectsFolder, "raw.RSE"), SimpleScene.Replace("MOPA=0,0,100", "MOPA=0,0,102"));
                File.WriteAllText(Path.Combine(EffectsFolder, "native.RSE"), Text.Replace("0,NOLLOP", "O,NOLOOP"));
                File.WriteAllText(Path.Combine(EffectsFolder, "setup.RSE"), SimpleScene.Replace("[Setup]", "[Setup]\nPOS3=99,1,2,3"));
                File.WriteAllText(Path.Combine(EffectsFolder, "source.RSE"), ParticleScene.Replace("OBJ=0", "OBJ=99"));
                File.WriteAllText(Path.Combine(EffectsFolder, "ram.RSE"),
                    ParticleScene.Replace("0=EB_401.RSM", "0=ignored.RAM"));
                using CuldceptSagaExplorer Window = new global::CuldceptSagaExplorer.CuldceptSagaExplorer { ShowInTaskbar = false };
                ((TextBox)Window.Controls.Find("txtWorkingFolder", true).Single()).Text = Folder;
                Window.GetType().GetMethod("OpenEffectFolder",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Window, null);
                TabPage Effects = Window.Controls.OfType<TabControl>().Single().TabPages.Cast<TabPage>().Single(Page => Page.Text == "Effects");
                ListView List = Effects.Controls.OfType<ListView>().Single(Control => Control.Name == "lsEffectsModels");
                ListViewItem Rejected = List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "rejected");
                ListViewItem Valid = List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "valid");
                ListViewItem Native = List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "native");
                Assert.That(Rejected.SubItems[1].Text, Does.Contain("(warning)"));
                Assert.That(Rejected.ToolTipText, Does.Contain("skipped, not corrected").And.Contain("RSA animations are not applied"));
                Assert.That(Valid.SubItems[1].Text, Does.Not.Contain("(warning)"));
                ListViewItem Raw = List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "raw");
                Assert.That(Raw.SubItems[1].Text, Does.Contain("(warning)"));
                Assert.That(Raw.ToolTipText, Does.Contain("raw opacity outside 0-100% is preserved")
                    .And.Contain("bounded approximation").And.Contain("Native blend state is unverified"));
                Assert.That(Native.SubItems[1].Text, Does.Contain("(warning)"));
                Assert.That(Native.ToolTipText, Does.Contain("native signed decimal-prefix parser"));
                Assert.That(Native.ToolTipText, Does.Not.Contain("RSA animations are not applied"));
                ListViewItem Setup = List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "setup");
                ListViewItem Source = List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "source");
                Assert.That(Setup.SubItems[1].Text, Does.Contain("(warning)"));
                Assert.That(Setup.ToolTipText, Does.Contain("setup commands with no constructed target").And.Contain("No replacement object"));
                Assert.That(Source.SubItems[1].Text, Does.Contain("(warning)"));
                Assert.That(Source.ToolTipText, Does.Contain("particle source objects with no native binding").And.Contain("No replacement asset"));
                ListViewItem Ram = List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "ram");
                Assert.That(Ram.SubItems[1].Text, Does.Contain("(warning)"));
                Assert.That(Ram.ToolTipText, Does.Contain("NOT corrected to .RSM").And.Contain("Original declarations"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void RejectedMotionDoesNotConsumeTheValidCommandSlotOrWeakenOtherValidation()
        {
            string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=clip.RSA")
                .Replace("TIME=0", "TIME=0\nMOTION=0,0,0,NOLLOP\nMOTION=0,0,0,NOLOOP");
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.ListRejectedMotionCommand.Count, Is.EqualTo(1));
            Assert.That(Scene.DicObject[0].MotionId, Is.Zero);
            Assert.That(Scene.DicObject[0].MotionLoops, Is.False);
            Assert.That(Scene.DicObject[0].MotionStartFrame, Is.Zero);
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("TIME=0", "TIME=31")));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("MOTION=0,0,0,NOLLOP", "MOTION=0,0,2147483648,NOLLOP")));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("MOTION=0,0,0,NOLLOP", "MOTION=99,0,0,NOLLOP")));
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text.Replace("MOTION=0,0,0,NOLLOP", "MOTION=0,0,0,XLOOP")));
            Assert.Throws<InvalidDataException>(() => RseReader.Parse(
                Text.Replace("MOTION=0,0,0,NOLLOP", "MOTION=0,0,0,NOLOOP")));
        }

        [TestCase("MOTION=0,0,1,NOLOOP")]
        [TestCase("MOTION=0,0,1,0,NOLOOP")]
        public void UnverifiedMotionModesAreNotSilentlyApproximated(string Command)
        {
            string Text = SimpleScene.Replace("[MotionList]", "[MotionList]\n0=EB_414.RSA")
                .Replace("TIME=0", "TIME=0\n" + Command);
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(Text));
        }

        [TestCase("missing")]
        [TestCase("invalid")]
        [TestCase("mismatched")]
        public void MissingOrInvalidDeclaredMotionReportsTheExactResource(string ResourceMode)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-rse-motion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Source = Path.Combine(Folder, "test.RSE");
                string MotionPath = Path.Combine(Folder, "missing.RSA");
                if (ResourceMode == "invalid")
                {
                    File.WriteAllBytes(MotionPath, new byte[8]);
                }

                if (ResourceMode == "mismatched")
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_414.RSA"), MotionPath);
                }

                File.WriteAllText(Source, SimpleScene.Replace("[MotionList]", "[MotionList]\n0=missing.RSA")
                    .Replace("TIME=0", "TIME=0\nMOTION=0,0,0,NOLOOP"));
                if (ResourceMode == "missing")
                {
                    ModelRoot Root = RseExporter.Load(Source);
                    Assert.That(Root.LogicalMeshes, Is.Not.Empty);
                    Assert.That(Root.Extras!["rse"]!["dependencies"]!.AsArray().Select(Entry => Entry!["kind"]!.GetValue<string>()),
                        Is.EquivalentTo(new[] { "model", "texture" }));
                    Assert.That(Root.Extras!["rse"]!["absentDependencies"]!.AsArray().Single()!["path"]!.GetValue<string>(),
                        Is.EqualTo(MotionPath));
                }
                else
                {
                    InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseExporter.Load(Source));
                    Assert.That(Error!.Message, Does.Contain(MotionPath));
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb414MotionUvAndBillboardsShareOneExportedTimeline(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_414.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.TotalFrames, Is.EqualTo(101));
            Assert.That(Scene.DicObject[0].MotionId, Is.Zero);
            Assert.That(Scene.ListParticleGenerator.Single().Spin, Is.EqualTo(4));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-motion-{Guid.NewGuid():N}.glb");
            try
            {
                ModelRoot Root = RseExporter.Load(Source, FramesPerSecond);
                Root.SaveGLB(Output);
                Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.LogicalIndex, Is.EqualTo(Root.Extras!["rse"]!["animationIndex"]!.GetValue<int>()));
                Assert.That(Timeline.Duration, Is.EqualTo(101f / FramesPerSecond).Within(1e-6));
                RsaClip Clip = RSAReader.Parse(File.ReadAllBytes(Path.Combine(EffectDirectory, "EB_414.RSA")), "source");
                Node Node = Root.LogicalNodes.Single(Node => Node.Name == "rse_0_node_3");
                SharpGLTF.Transforms.AffineTransform First = Node.GetLocalTransform(Timeline, 0);
                foreach (int Frame in new[] { 0, 25, 50, 99 })
                {
                    SharpGLTF.Transforms.AffineTransform Pose = Node.GetLocalTransform(Timeline, Frame / (float)FramesPerSecond);
                    RsaTransformKeyframe SourceKey = Clip.ListChannel[3].ListKeyframe[Frame];
                    Assert.That(System.Numerics.Vector3.Distance(Pose.Scale, SourceKey.Scale), Is.LessThan(1e-5));
                    Assert.That(Pose.Translation.Z, Is.EqualTo(-SourceKey.Translation.Z).Within(1e-5));
                }
                Assert.That(Node.GetLocalTransform(Timeline, 99f / FramesPerSecond).Scale.X, Is.GreaterThan(First.Scale.X * 5));
                Assert.That(Node.GetLocalTransform(Timeline, 100.5f / FramesPerSecond).Scale,
                    Is.EqualTo(Node.GetLocalTransform(Timeline, 99f / FramesPerSecond).Scale), "NOLOOP must hold the final pose.");
                string MaterialName = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_414.RSM"), false, false)
                    .Document.ListMaterial[0].Name;
                Material Material = Root.LogicalMaterials.Single(Material => Material.Name == "rse_0_" + MaterialName);
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector2> Uv = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                    $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset")
                    .GetSamplerOrNull<System.Numerics.Vector2>().CreateCurveSampler(true);
                Assert.That(Uv.GetPoint(50f / FramesPerSecond).Y, Is.EqualTo(-.375f).Within(1e-6));
                Assert.That(Uv.GetPoint(100.5f / FramesPerSecond).Y, Is.EqualTo(-.75f).Within(1e-6));
                Node[] Billboards = Root.LogicalNodes.Where(Node => Node.Extras is System.Text.Json.Nodes.JsonObject Extras
                    && Extras["rseBillboard"] != null).ToArray();
                Assert.That(Billboards.Length, Is.EqualTo(80));
                Assert.That(Billboards.All(Node => Node.Extras!["rseBillboard"]!.GetValue<string>() == "spherical"), Is.True);
                Node Particle = Billboards.Single(Node => Node.Name == "rse_object_1000");
                SharpGLTF.Transforms.AffineTransform ParticlePose = Particle.GetLocalTransform(Timeline, 10f / FramesPerSecond);
                Assert.That(ParticlePose.Rotation, Is.EqualTo(System.Numerics.Quaternion.Identity),
                    "SPIN=4 replaces baked KEY3 Euler angles, just as SPIN=0 does.");
                Assert.That(ParticlePose.Translation.Z, Is.EqualTo(-Scene.DicObject[1000].ListParticleFrame[10].Position.Z).Within(1e-5));
            }
            finally { File.Delete(Output); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb419LegacyMotionExportsExactRsaPosesAndHoldsTheFinalFrame(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_419.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.TotalFrames, Is.EqualTo(56));
            Assert.That(Scene.DicObject[0].MotionId, Is.Zero);
            RsaClip Clip = RSAReader.Parse(File.ReadAllBytes(Path.Combine(EffectDirectory, "EB_419.RSA")), "source");
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-legacy-motion-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.Duration, Is.EqualTo(56f / FramesPerSecond).Within(1e-6));
                Assert.That(Root.LogicalImages.Count, Is.GreaterThan(0));
                int CheckedChannels = 0;
                bool HasMotion = false;
                for (int Index = 0; Index < Clip.ListChannel.Count; Index++)
                {
                    List<RsaTransformKeyframe> Keys = Clip.ListChannel[Index].ListKeyframe;
                    if (Keys.Count == 0)
                    {
                        continue;
                    }

                    Node Node = Root.LogicalNodes.Single(Node => Node.Name == $"rse_0_node_{Index}");
                    foreach (RsaTransformKeyframe? Key in Keys.Where(Key => Key.Frame == 0 || Key.Frame == 25 || Key == Keys.Last()))
                    {
                        SharpGLTF.Transforms.AffineTransform Pose = Node.GetLocalTransform(Timeline, Key.Frame / FramesPerSecond);
                        System.Numerics.Vector3 ExpectedPosition = new System.Numerics.Vector3(Key.Translation.X, Key.Translation.Y, -Key.Translation.Z);
                        System.Numerics.Quaternion ExpectedRotation = System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(
                            Key.Rotation.X, Key.Rotation.Y, -Key.Rotation.Z, Key.Rotation.W));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Translation, ExpectedPosition), Is.LessThan(1e-5));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Scale, Key.Scale), Is.LessThan(1e-5));
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Pose.Rotation, ExpectedRotation)),
                            Is.EqualTo(1).Within(1e-5));
                    }
                    SharpGLTF.Transforms.AffineTransform Final = Node.GetLocalTransform(Timeline, Keys.Last().Frame / FramesPerSecond);
                    SharpGLTF.Transforms.AffineTransform Held = Node.GetLocalTransform(Timeline, 55.5f / FramesPerSecond);
                    Assert.That(System.Numerics.Vector3.Distance(Held.Translation, Final.Translation), Is.LessThan(1e-5));
                    Assert.That(System.Numerics.Vector3.Distance(Held.Scale, Final.Scale), Is.LessThan(1e-5));
                    HasMotion |= Keys.Any(Key => Key.Translation != Keys[0].Translation || Key.Scale != Keys[0].Scale
                        || Key.Rotation != Keys[0].Rotation);
                    CheckedChannels++;
                }
                Assert.That(CheckedChannels, Is.EqualTo(55));
                Assert.That(HasMotion, Is.True, "Accepting the legacy syntax must not drop its RSA animation.");
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_419.RSM"), false, false);
                Assert.That(Package.Document.ListMaterial.Select(Material => Material.Name).Distinct().Count(),
                    Is.LessThan(Package.Document.ListMaterial.Count), "This scene exercises duplicate-name material slots.");
                Assert.That(Root.LogicalMaterials.Count, Is.EqualTo(Package.Document.ListMaterial.Count));
                for (int Slot = 0; Slot < Package.Document.ListMaterial.Count; Slot++)
                {
                    RsmMaterial SourceMaterial = Package.Document.ListMaterial[Slot];
                    Material Material = Root.LogicalMaterials.Single(Material =>
                        Material.Name == $"rse_0_material_{Slot}_{SourceMaterial.Name}");
                    Assert.That(Material.FindChannel("BaseColor")!.Value.Texture, Is.Not.Null);
                    Assert.That(Material.Extras!["rsm"]!["opacity"]!.GetValue<float>(), Is.EqualTo(SourceMaterial.Opacity));
                    Assert.That(Root.LogicalMeshes.SelectMany(Mesh => Mesh.Primitives).Any(Primitive => Primitive.Material == Material),
                        Is.True, "Each source slot must remain attached to its geometry.");
                    SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Opacity = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                        $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                        .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                    foreach (KeyValuePair<int, float> Key in Scene.DicObject[0].DicMaterialOpacity[Slot])
                    {
                        Assert.That(Opacity.GetPoint(Key.Key / (float)FramesPerSecond).W, Is.EqualTo(Key.Value).Within(1e-6));
                    }
                }
            }
            finally { File.Delete(Output); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb425ExportsSeededBillboardSpinAndBakedPositions(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_425.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.ListParticleGenerator.Count, Is.EqualTo(3));
            Assert.That(Scene.ListParticleGenerator[0].SpinVelocityRange.Z, Is.EqualTo(.01f));
            Assert.That(Scene.DicObject[0].DicGeneratorPosition[28].Y, Is.EqualTo(.888682f));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-spin-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Root.Extras!["rse"]!["particleCount"]!.GetValue<int>(), Is.EqualTo(73));
                Assert.That(Timeline.Duration, Is.EqualTo(72f / FramesPerSecond).Within(1e-6));
                foreach ((int Id, float Angle, float Velocity) Expected in new[] { (Id: 1000, Angle: -2.98f, Velocity: -.01f),
                    (Id: 1001, Angle: -2.14f, Velocity: -.01f), (Id: 1250, Angle: 62.02f, Velocity: 0f),
                    (Id: 1500, Angle: 62.02f, Velocity: .02f) })
                {
                    Node Particle = Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Expected.Id}");
                    Node Spin = Root.LogicalNodes.Single(Node => Node.Name == $"rse_spin_{Expected.Id}");
                    Assert.That(Particle.Extras!["rseBillboard"]!.GetValue<string>(), Is.EqualTo("spherical"));
                    float Angle = Expected.Angle;
                    foreach (RseParticleFrame Frame in Scene.DicObject[Expected.Id].ListParticleFrame)
                    {
                        if (Frame.Frame > 0 && Frame.Age >= 0 && Frame.Lifetime > 0)
                        {
                            Angle += Expected.Velocity;
                        }

                        SharpGLTF.Transforms.AffineTransform Pose = Particle.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        System.Numerics.Quaternion Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, Angle * MathF.PI / 180);
                        SharpGLTF.Transforms.AffineTransform SpinPose = Spin.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(SpinPose.Rotation, Rotation)), Is.EqualTo(1).Within(1e-6));
                        System.Numerics.Vector3 Position = new System.Numerics.Vector3(Frame.Position.X, Frame.Position.Y, -Frame.Position.Z);
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Translation, Position), Is.LessThan(1e-5),
                            "Generator position keys must not be added again to baked particle positions.");
                        SharpGLTF.Transforms.AffineTransform Held = Spin.GetLocalTransform(Timeline, (Frame.Frame + .5f) / FramesPerSecond);
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Held.Rotation, SpinPose.Rotation)), Is.EqualTo(1).Within(1e-6),
                            "Per-frame particle spin must use STEP sampling.");
                    }
                }
            }
            finally { File.Delete(Output); }
        }

        [TestCase(true, 4)]
        [TestCase(false, 4)]
        [TestCase(true, 1)]
        [TestCase(false, 1)]
        [TestCase(true, 5)]
        [TestCase(false, 5)]
        public void ParticleSpinAdvancesOnlyOnActiveSceneFramesAndDoesNotResetAtBirth(bool Seeded, int Spin)
        {
            bool Billboard = Spin == 4 || Spin == 5;
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-rse-spin-age-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Text = ParticleScene.Replace("TotalFrame=3", "TotalFrame=6")
                    .Replace("SPIN=0", $"SPIN={Spin}\nANGZ=10,0\nROTZ=90,0")
                    .Replace("KEY3=0,0,2", "KEY3=0,-1,0")
                    .Replace("KEY3=1,1,2", "KEY3=1,0,1")
                    + "KEY3=3,0,2,0,0,0,0,0,0,1,1,1\nKEY3=4,4,2,0,0,0,0,0,0,1,1,1\n"
                    + "KEY3=5,-1,0,0,0,0,0,0,0,1,1,1\n";
                if (!Seeded)
                {
                    Text = Text.Replace("SEED=12345", "#SEED=12345");
                }

                string SourcePath = Path.Combine(Folder, "spin.RSE");
                File.WriteAllText(SourcePath, Text.Replace(",1,1,1\n", ",1.3,.7,1\n"));
                ModelRoot Root = RseExporter.Load(SourcePath);
                Animation Timeline = Root.LogicalAnimations.Single();
                Node Particle = Root.LogicalNodes.Single(Node => Node.Name == (Billboard ? "rse_spin_10" : "rse_object_10"));
                if (Billboard)
                {
                    Assert.That(Particle.VisualParent.Name, Is.EqualTo("rse_object_10"));
                }

                float[] Angles = Seeded ? new[] { 10f, 100, 100, 190, 280, 280 } : new float[6];
                for (int Frame = 0; Frame < Angles.Length; Frame++)
                {
                    SharpGLTF.Transforms.AffineTransform Pose = Particle.GetLocalTransform(Timeline, Frame / 60f);
                    System.Numerics.Quaternion Expected = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, Angles[Frame] * MathF.PI / 180);
                    Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Pose.Rotation, Expected)), Is.EqualTo(1).Within(1e-6));
                    SharpGLTF.Transforms.AffineTransform ScalePose = Billboard ? Particle.VisualParent.GetLocalTransform(Timeline, Frame / 60f) : Pose;
                    Assert.That(ScalePose.Scale, Is.EqualTo(new System.Numerics.Vector3(1.3f, .7f, 1)));
                    if (Billboard)
                    {
                        Assert.That(ScalePose.Rotation, Is.EqualTo(System.Numerics.Quaternion.Identity));
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb429ExportsThreeAxisSpinWithoutMakingTheShardsBillboards(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_429.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.ListParticleGenerator.Select(Generator => Generator.Spin), Is.EqualTo(new[] { 1, 4 }));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-shards-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Root.Extras!["rse"]!["particleCount"]!.GetValue<int>(), Is.EqualTo(56));
                Assert.That(Timeline.Duration, Is.EqualTo(81f / FramesPerSecond).Within(1e-6));
                Assert.That(Root.LogicalNodes.Count(Node =>
                {
                    if (Node.Extras == null)
                    {
                        return false;
                    }

                    return Node.Extras["rseBillboard"] != null;
                }), Is.EqualTo(40));
                Material ShardMaterial = Root.LogicalMaterials.First(Material => Material.Name.StartsWith("rse_1000_", StringComparison.Ordinal));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Opacity = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                    $"/materials/{ShardMaterial.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Opacity.GetPoint(0).W, Is.Zero);
                Assert.That(Opacity.GetPoint(79f / FramesPerSecond).W, Is.EqualTo(1).Within(1e-6));
                Assert.That(Scene.DicObject[1000].ListParticleFrame[80].Lifetime, Is.Zero);
                Assert.That(Opacity.GetPoint(80f / FramesPerSecond).W, Is.Zero,
                    "The final inactive baked record hides the shard, rather than a guessed lifetime fade.");
                foreach (int Id in Enumerable.Range(1000, 16))
                {
                    Node Particle = Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Id}");
                    System.Text.Json.Nodes.JsonNode? Billboard = null;
                    if (Particle.Extras != null)
                    {
                        Billboard = Particle.Extras["rseBillboard"];
                    }

                    Assert.That(Billboard, Is.Null);
                    Assert.That(Timeline.Channels.Any(Channel => Channel.TargetNode == Particle && Channel.TargetNodePath == PropertyPath.rotation),
                        Is.True, "Each shard needs its own rotation channel.");
                    foreach (RseParticleFrame Frame in Scene.DicObject[Id].ListParticleFrame)
                    {
                        SharpGLTF.Transforms.AffineTransform Pose = Particle.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        System.Numerics.Vector3 Position = new System.Numerics.Vector3(Frame.Position.X, Frame.Position.Y, -Frame.Position.Z);
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Translation, Position), Is.LessThan(1e-5));
                    }
                }
                foreach ((int Id, Vector3 Angles, Vector3 Velocity) Expected in new[] {
                    (Id: 1000, Angles: new Vector3(62.02f, -56.08f, -67.14f), Velocity: new Vector3(5.36f, -14.93f, 9.33f)),
                    (Id: 1001, Angles: new Vector3(159.55f, 6.16f, -95.11f), Velocity: new Vector3(-7.11f, 4.9f, -4.03f)) })
                {
                    Node Particle = Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Expected.Id}");
                    Vector3 Angles = Expected.Angles * (MathF.PI / 180);
                    Vector3 Velocity = Expected.Velocity * (MathF.PI / 180);
                    foreach (RseParticleFrame Frame in Scene.DicObject[Expected.Id].ListParticleFrame)
                    {
                        if (Frame.Frame > 0 && Frame.Age >= 0 && Frame.Lifetime > 0)
                        {
                            Angles += Velocity;
                        }
                        // Reflect the game's Y*X*Z matrix into the export coordinate system.
                        Matrix ExpectedMatrix = Matrix.CreateRotationY(-Angles.Y)
                            * Matrix.CreateRotationX(-Angles.X) * Matrix.CreateRotationZ(Angles.Z);
                        SharpGLTF.Transforms.AffineTransform Pose = Particle.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        System.Numerics.Vector3 Point = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(.2f, .5f, .9f), Pose.Rotation);
                        Vector3 ExpectedPoint = Vector3.Transform(new Vector3(.2f, .5f, .9f), ExpectedMatrix);
                        Assert.That(System.Numerics.Vector3.Distance(Point,
                            new System.Numerics.Vector3(ExpectedPoint.X, ExpectedPoint.Y, ExpectedPoint.Z)), Is.LessThan(1e-5));
                        SharpGLTF.Transforms.AffineTransform Held = Particle.GetLocalTransform(Timeline, (Frame.Frame + .5f) / FramesPerSecond);
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Held.Rotation, Pose.Rotation)), Is.EqualTo(1).Within(1e-6));
                    }
                }
            }
            finally { File.Delete(Output); }
        }

        [TestCase(2, true, 30, false)]
        [TestCase(2, true, 60, false)]
        [TestCase(2, false, 60, false)]
        [TestCase(3, true, 30, false)]
        [TestCase(3, true, 60, false)]
        [TestCase(3, false, 60, false)]
        [TestCase(2, true, 60, true)]
        [TestCase(3, true, 60, true)]
        public void BakedSpinUsesSourceEulerOrderAndModeSpecificRandomConsumption(int Spin, bool Seeded, int FramesPerSecond, bool InitiallyInactive)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-baked-spin-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string Text = ParticleScene.Substring(0, ParticleScene.IndexOf("[ParticleData]", StringComparison.Ordinal))
                    .Replace("TotalFrame=3", "TotalFrame=6")
                    .Replace("10=DUMMY_PARTICLE", "10=DUMMY_PARTICLE\n11=DUMMY_PARTICLE")
                    .Replace("PARTICLE=10,10", "PARTICLE=10,11")
                    .Replace("SPIN=0", $"SPIN={Spin}\nANGX=10,180\nANGY=20,180\nANGZ=30,180\nROTX=1,15\nROTY=2,15\nROTZ=3,15");
                if (!Seeded)
                {
                    Text = Text.Replace("SEED=12345", "#SEED=12345");
                }

                Text += "[ParticleData]\n";
                foreach (int Id in new[] { 10, 11 })
                {
                    Text += $"OBJNO={Id}\n";
                    foreach (int Frame in Enumerable.Range(0, 6))
                    {
                        string AgeLife = Frame == 2 || Frame == 5 || Frame == 0 && InitiallyInactive
                            ? "-1,0" : Frame == 0 || Frame == 3 ? "0,2" : "4,2";
                        string Euler = Frame == 2 || Frame == 5 ? "1.1,1.2,1.3" : ".2,.3,.4";
                        Text += $"KEY3={Frame},{AgeLife},1,2,3,{Euler},1.3,.7,1\n";
                    }
                }
                string Source = Path.Combine(Folder, "spin.RSE");
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                string Output = Path.Combine(Folder, "spin.glb");
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Root.LogicalNodes.Any(Node =>
                {
                    if (Node.Extras == null)
                    {
                        return false;
                    }

                    return Node.Extras["rseBillboard"] != null;
                }), Is.False);
                foreach (int Index in Enumerable.Range(0, 2))
                {
                    int Id = 10 + Index;
                    Vector3 Angles = new Vector3(10, 20, 30);
                    Vector3 Velocity = Spin == 3 ? new Vector3(1, 2, 3) : Vector3.Zero;
                    if (Seeded)
                    {
                        Angles += Spin == 2
                            ? Index == 0 ? new Vector3(62.02f, -56.08f, -67.14f) : new Vector3(-69.64f, .07f, -95.67f)
                            : Index == 0 ? new Vector3(-69.64f, .07f, -95.67f) : new Vector3(127.89f, 109.9f, -79.03f);
                        if (Spin == 3)
                        {
                            Velocity += Index == 0
                            ? new Vector3(-12.98f, -11.08f, 7.86f) : new Vector3(-5.45f, -8.84f, 9.89f);
                        }
                    }
                    Angles *= MathF.PI / 180;
                    Velocity *= MathF.PI / 180;
                    Vector3 ExpectedAngles = Vector3.Zero;
                    Node Particle = Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Id}");
                    foreach (RseParticleFrame Frame in Scene.DicObject[Id].ListParticleFrame)
                    {
                        bool Active = Frame.Age >= 0 && Frame.Lifetime > 0;
                        if (Spin == 3 && Active)
                        {
                            Angles += Velocity;
                        }

                        if (Spin == 2 || Active)
                        {
                            ExpectedAngles = Angles + new Vector3(Frame.Rotation.X, Frame.Rotation.Y, Frame.Rotation.Z);
                        }

                        Matrix ExpectedMatrix = Matrix.CreateRotationZ(ExpectedAngles.Z)
                            * Matrix.CreateRotationX(-ExpectedAngles.X) * Matrix.CreateRotationY(-ExpectedAngles.Y);
                        SharpGLTF.Transforms.AffineTransform Pose = Particle.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        System.Numerics.Vector3 Point = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(.2f, .5f, .9f), Pose.Rotation);
                        Vector3 ExpectedPoint = Vector3.Transform(new Vector3(.2f, .5f, .9f), ExpectedMatrix);
                        Assert.That(System.Numerics.Vector3.Distance(Point,
                            new System.Numerics.Vector3(ExpectedPoint.X, ExpectedPoint.Y, ExpectedPoint.Z)), Is.LessThan(1e-5),
                            $"SPIN={Spin}, particle {Id}, frame {Frame.Frame}");
                        Assert.That(Pose.Scale, Is.EqualTo(new System.Numerics.Vector3(1.3f, .7f, 1)));
                        Assert.That(Pose.Translation, Is.EqualTo(new System.Numerics.Vector3(1, 2, -3)));
                        SharpGLTF.Transforms.AffineTransform Held = Particle.GetLocalTransform(Timeline, (Frame.Frame + .5f) / FramesPerSecond);
                        Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Held.Rotation, Pose.Rotation)), Is.EqualTo(1).Within(1e-6));
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InlineParticleModelLoadsWithoutInventingAVisibleTemplateObject(bool ParticleOnly)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-inline-model-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "123_model" + Extension));
                }

                string Text = ParticleScene.Replace("OBJ=0", "OBJ=123_model.RSM").Replace("0=EB_401.RSM\n",
                    ParticleOnly ? "" : "0=123_model.RSM\n");
                string Source = Path.Combine(Folder, "inline.RSE");
                File.WriteAllText(Source, Text);
                RseScene Scene = RseReader.Parse(Text);
                Assert.That(Scene.ListParticleGenerator.Single().ModelFileName, Is.EqualTo("123_model.RSM"));
                Assert.That(Scene.ListParticleGenerator.Single().ModelObjectId, Is.EqualTo(-1));
                Assert.That(Scene.DicObject.ContainsKey(123), Is.False, "A filename starting with digits is not an object ID in Explorer.");
                string Output = Path.Combine(Folder, "inline.glb");
                RseExporter.Load(Source).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Assert.That(Root.LogicalNodes.Count(Node => Node.Name.StartsWith("rse_object_", StringComparison.Ordinal)),
                    Is.EqualTo(ParticleOnly ? 1 : 2));
                Assert.That(Root.Extras!["rse"]!["inlineParticleModels"]![0]!["file"]!.GetValue<string>(), Is.EqualTo("123_model.RSM"));
                Assert.That(Root.LogicalNodes.Any(Node => Node.Name == "rse_object_10"), Is.True);
                File.Delete(Path.Combine(Folder, "123_model.RST"));
                Assert.That(Assert.Throws<FileNotFoundException>(() => RseExporter.Load(Source))!.FileName,
                    Does.EndWith("123_model.RST"));
                File.WriteAllText(Source, Text.Replace("AUTORST=1", "AUTORST=0"));
                Assert.DoesNotThrow(() => RseExporter.Load(Source));
                File.Delete(Path.Combine(Folder, "123_model.RSM"));
                ModelRoot MissingModel = RseExporter.Load(Source);
                Assert.That(MissingModel.LogicalMeshes, Is.Empty);
                Assert.That(MissingModel.Extras!["rse"]!["absentDependencies"]!.AsArray()
                    .Any(Entry => Entry!["file"]!.GetValue<string>() == "123_model.RSM"), Is.True);
            }
            finally { Directory.Delete(Folder, true); }
        }

        [TestCase("..\\model.RSM")]
        [TestCase("folder/model.RSM")]
        [TestCase("E:\\model.RSM")]
        [TestCase("model.RSA")]
        [TestCase("garbage")]
        public void InlineParticleModelsRejectUnsafeOrUnsupportedReferences(string FileName)
        {
            NotSupportedException? Error = Assert.Throws<NotSupportedException>(() => RseReader.Parse(
                ParticleScene.Replace("OBJ=0", $"OBJ={FileName}"), "inline.RSE"));
            Assert.That(Error!.Message, Does.Contain("inline.RSE").And.Contain("same-directory RSM"));
        }

        [TestCase("OBJ=0", "OBJ=-1", "non-negative")]
        [TestCase("PARTICLE=10,10", "PARTICLE=11,10", "range")]
        [TestCase("KEY3=1,1,2", "KEY3=2,1,2", "sequential")]
        [TestCase("KEY3=1,1,2", "KEY3=1,NaN,2", "finite")]
        [TestCase("MIDDLE=100", "MIDDLE=101", "percentages")]
        public void MalformedParticleTracksFailWithContext(string Before, string After, string Expected)
        {
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(ParticleScene.Replace(Before, After), "particles.RSE"));
            Assert.That(Error!.Message, Does.Contain("particles.RSE").And.Contain("line").And.Contain(Expected));
        }

        [TestCase("TIME=3\nPOS3=1,0,0,0\n", "outside TotalFrame")]
        [TestCase("TIME=0\nPOS3=1,NaN,0,0\n", "finite")]
        [TestCase("TIME=0\nPOS3=1,0,0,0\nPOS3=1,0,0,0\n", "Duplicate")]
        public void MalformedGeneratorPositionKeysFailWithContext(string Keys, string Expected)
        {
            string Text = ParticleScene.Replace("[ParticleData]", "[KeyData]\n" + Keys + "[ParticleData]");
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text, "generator.RSE"));
            Assert.That(Error!.Message, Does.Contain("generator.RSE").And.Contain("line").And.Contain(Expected));
        }

        [TestCase("SPIN=0", "SPIN=6")]
        [TestCase("KEY3=0", "KEY=0")]
        [TestCase("OBJ=0", "OBJ=..\\EB_401.RSM")]
        [TestCase("SEED=12345", "UNKNOWN=12345")]
        [TestCase("TotalFrame=3", "TotalFrame=4")]
        [TestCase("SPIN=0", "SPIN=4\nROTZ=0,.001")]
        [TestCase("[ParticleData]", "[KeyData]\nTIME=0\nPOS3=0,0,0,0\n[ParticleData]")]
        [TestCase("[ParticleData]", "[KeyData]\nTIME=0\nSCL3=0,1,1,1\n[ParticleData]")]
        [TestCase("[ParticleData]", "[KeyData]\nTIME=0\nSCL3=10,1,1,1\n[ParticleData]")]
        public void UnsupportedParticleModesAreNotSilentlyApproximated(string Before, string After)
        {
            Assert.Throws<NotSupportedException>(() => RseReader.Parse(ParticleScene.Replace(Before, After)));
        }

        [TestCase("TIME=3\nSCL3=1,1,1,1\n", "outside TotalFrame")]
        [TestCase("TIME=0\nSCL3=1,NaN,1,1\n", "finite")]
        [TestCase("TIME=0\nSCL3=1,1,1,1\nSCL3=1,2,1,1\n", "Duplicate")]
        [TestCase("TIME=0\nSCL3=1,1,1\n", "4 values")]
        public void MalformedGeneratorScaleKeysFailWithContext(string Keys, string Expected)
        {
            string Text = ParticleScene.Replace("[ParticleData]", "[KeyData]\n" + Keys + "[ParticleData]");
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text, "generator.RSE"));
            Assert.That(Error!.Message, Does.Contain("generator.RSE").And.Contain("line"));
            Assert.That(Error.Message, Does.Contain(Expected));
        }

        [TestCase(30)]
        [TestCase(60)]
        public void Eb431GeneratorScaleIsNotAppliedAgainToBakedParticles(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "EB_431.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(Source), Source);
            Assert.That(Scene.DicObject[1].DicGeneratorScale.Keys, Is.EqualTo(new[] { 0, 34, 65 }));
            Assert.That(Scene.DicObject[1].DicGeneratorScale[34].X, Is.EqualTo(1.482230f));
            Assert.That(Scene.DicObject[1].DicGeneratorScale[65].X, Is.EqualTo(1.328524f));
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-generator-scale-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Root.Extras!["rse"]!["particleCount"]!.GetValue<int>(), Is.EqualTo(55));
                Assert.That(Timeline.Duration, Is.EqualTo(77f / FramesPerSecond).Within(1e-6));
                foreach (RseObject Particle in Scene.DicObject.Values.Where(Object => Object.Kind == RseObjectKind.Particle))
                {
                    Node Node = Root.LogicalNodes.Single(Node => Node.Name == $"rse_object_{Particle.Id}");
                    foreach (RseParticleFrame Frame in Particle.ListParticleFrame)
                    {
                        SharpGLTF.Transforms.AffineTransform Pose = Node.GetLocalTransform(Timeline, Frame.Frame / (float)FramesPerSecond);
                        System.Numerics.Vector3 Position = new System.Numerics.Vector3(Frame.Position.X, Frame.Position.Y, -Frame.Position.Z);
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Translation, Position), Is.LessThan(1e-5));
                        Assert.That(System.Numerics.Vector3.Distance(Pose.Scale, Frame.Scale), Is.LessThan(1e-5),
                            "Generator scale must not multiply already-baked KEY3 scales.");
                    }
                }
            }
            finally { File.Delete(Output); }
        }

        [Test]
        public void MultipleGeneratorsCanShareOneParticleSectionButNotParticleIds()
        {
            string SecondGenerator = "GENERATOR=2\nPARTICLE=11,11\nOBJ=0\nSEED=12345\n";
            string SecondTrack = "OBJNO=11\n" + ParticleScene.Substring(ParticleScene.IndexOf("KEY3=", StringComparison.Ordinal));
            string Text = ParticleScene.Replace("10=DUMMY_PARTICLE", "10=DUMMY_PARTICLE\n2=DUMMY_PARTICLE_GENERATOR\n11=DUMMY_PARTICLE")
                .Replace("[ParticleData]", SecondGenerator + "[ParticleData]") + SecondTrack;
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.ListParticleGenerator.Select(Generator => Generator.ObjectId), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Scene.DicObject[11].ListParticleFrame.Count, Is.EqualTo(3));
            InvalidDataException? Error = Assert.Throws<InvalidDataException>(() => RseReader.Parse(Text.Replace("PARTICLE=11,11", "PARTICLE=10,10")));
            Assert.That(Error!.Message, Does.Contain("overlap"));
        }

        [Test]
        public void ParticleUvOffsetsFollowAgeAndResetAtBirth()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-rse-uv-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_401.RSM"), WithAnimations: false, WithTextures: false);
                Package.Document.ListMaterial[0].UvScroll = new System.Numerics.Vector2(.25f, .125f);
                using (FileStream Stream = File.Create(Path.Combine(Folder, "EB_401.RSM")))
                using (BigEndianBinaryWriter Writer = new BigEndianBinaryWriter(Stream))
                {
                    RSMWriter.WriteRsm(Writer, Package.Document);
                }

                File.Copy(Path.Combine(EffectDirectory, "EB_401.RST"), Path.Combine(Folder, "EB_401.RST"));
                string SourcePath = Path.Combine(Folder, "scene.RSE");
                File.WriteAllText(SourcePath, ParticleScene);
                ModelRoot Root = RseExporter.Load(SourcePath);
                Animation Timeline = Root.LogicalAnimations[Root.Extras!["rse"]!["animationIndex"]!.GetValue<int>()];
                Material Material = Root.LogicalMaterials.Single(Material => Material.Name.StartsWith("rse_10_", StringComparison.Ordinal));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector2> Uv = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                    $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset")
                    .GetSamplerOrNull<System.Numerics.Vector2>().CreateCurveSampler(true);
                Assert.That(Uv.GetPoint(0), Is.EqualTo(System.Numerics.Vector2.Zero));
                Assert.That(Uv.GetPoint(1f / 60), Is.EqualTo(new System.Numerics.Vector2(.25f, .125f)));
                Assert.That(Uv.GetPoint(1.5f / 60), Is.EqualTo(Uv.GetPoint(1f / 60)));
                Assert.That(Uv.GetPoint(2f / 60), Is.EqualTo(System.Numerics.Vector2.Zero), "Inactive/birth records reset scrolling instead of using global scene time.");
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [Test]
        public void NegativeSceneNumberParsesAndExportsTheReportedBattleEffect()
        {
            string ScenePath = Path.Combine(EffectDirectory, "EB_406_01.RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(ScenePath), ScenePath);
            Assert.That(Scene.SceneNumber, Is.EqualTo(-1));
            Assert.That(Scene.ModelFileName, Is.EqualTo("EB_406_01.RSM"));
            string OutputPath = Path.Combine(Path.GetTempPath(), $"culdcept-rse-negative-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(ScenePath).SaveGLB(OutputPath);
                ModelRoot Reloaded = ModelRoot.Load(OutputPath);
                Assert.That(Reloaded.Extras!["rse"]!["sceneNumber"]!.GetValue<int>(), Is.EqualTo(-1));
                Assert.That(Reloaded.LogicalImages.Count, Is.GreaterThan(0));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Sampler = Reloaded.LogicalAnimations.Single().Channels.Single()
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Sampler.GetPoint(0).W, Is.Zero);
                Assert.That(Sampler.GetPoint(2f / 60).W, Is.EqualTo(1));
                Assert.That(Sampler.GetPoint(30f / 60).W, Is.Zero);
            }
            finally
            {
                File.Delete(OutputPath);
            }
        }

        [Test]
        public void MultipleObjectsKeepIndependentOpacityAndSetupTransforms()
        {
            string Text = SimpleScene.Replace("0=EB_401.RSM", "0=EB_401.RSM\n1=EB_401.RSM")
                .Replace("[Setup]", "[Setup]\nPOS3=1,1,2,3\nQUAT=1,0,0.7071068,0,0.7071068\nSCL3=1,2,3,4\nOPACITY=1,25")
                .Replace("TIME=2\n", "TIME=2\nMOPA=1,0,75\n");
            RseScene Scene = RseReader.Parse(Text);
            Assert.That(Scene.DicObject.Count, Is.EqualTo(2));
            Assert.That(Scene.DicObject[1].Position, Is.EqualTo(new System.Numerics.Vector3(1, 2, 3)));
            Assert.That(Scene.DicObject[1].Scale, Is.EqualTo(new System.Numerics.Vector3(2, 3, 4)));
            Assert.That(Scene.DicObject[1].DicMaterialOpacity[0][2], Is.EqualTo(.75f));
            Assert.That(Scene.DicObject[0].DicMaterialOpacity[0][2], Is.EqualTo(1));
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-rse-objects-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Folder, "EB_401" + Extension));
                }

                string SourcePath = Path.Combine(Folder, "scene.RSE");
                string OutputPath = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(SourcePath, Text);
                RseExporter.Load(SourcePath).SaveGLB(OutputPath);
                ModelRoot Root = ModelRoot.Load(OutputPath);
                Animation Timeline = Root.LogicalAnimations.Single();
                Node ObjectRoot = Root.LogicalNodes.Single(Node => Node.Name == "rse_object_1");
                SharpGLTF.Transforms.AffineTransform Pose = ObjectRoot.GetLocalTransform(Timeline, 0);
                Assert.That(Pose.Translation, Is.EqualTo(new System.Numerics.Vector3(1, 2, -3)));
                Assert.That(Pose.Scale, Is.EqualTo(new System.Numerics.Vector3(2, 3, 4)));
                Assert.That(Pose.Rotation.Y, Is.EqualTo(MathF.Sqrt(.5f)).Within(1e-6));
                Assert.That(Root.LogicalMaterials.Count, Is.EqualTo(2), "Instances of the same model require independent material curves.");
                Material SecondMaterial = Root.LogicalMaterials.Single(Material => Material.Name.StartsWith("rse_1_", StringComparison.Ordinal));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Curve = Timeline.Channels.Single(Channel =>
                    Channel.TargetPointerPath == $"/materials/{SecondMaterial.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Curve.GetPoint(0).W, Is.EqualTo(.25f));
                Assert.That(Curve.GetPoint(2f / 60).W, Is.EqualTo(.75f));
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [TestCase("EB_410", false)]
        [TestCase("EB_412", true)]
        public void ReportedParticleSceneExportsEveryInstanceAndExactBakedTracks(string SceneName, bool HasSound)
        {
            string ScenePath = Path.Combine(EffectDirectory, SceneName + ".RSE");
            RseScene Scene = RseReader.Parse(File.ReadAllText(ScenePath), ScenePath);
            Assert.That(Scene.HasSoundCues, Is.EqualTo(HasSound));
            Assert.That(Scene.DicObject.Count, Is.EqualTo(103));
            Assert.That(Scene.ListParticleGenerator.Count, Is.EqualTo(1));
            Assert.That(Scene.DicObject[1000].ListParticleFrame.Count, Is.EqualTo(58));
            string OutputPath = Path.Combine(Path.GetTempPath(), $"culdcept-rse-particles-{Guid.NewGuid():N}.glb");
            try
            {
                ModelRoot Root = RseExporter.Load(ScenePath);
                Root.SaveGLB(OutputPath);
                ModelRoot Reloaded = ModelRoot.Load(OutputPath);
                Assert.That(Reloaded.Extras!["rse"]!["particleCount"]!.GetValue<int>(), Is.EqualTo(100));
                Assert.That(Reloaded.LogicalImages.Count, Is.GreaterThanOrEqualTo(2));
                Animation Timeline = Reloaded.LogicalAnimations[Reloaded.Extras!["rse"]!["animationIndex"]!.GetValue<int>()];
                Assert.That(Timeline.Duration, Is.EqualTo(58f / 60).Within(1e-6));
                Node ParticleRoot = Reloaded.LogicalNodes.Single(Node => Node.Name == "rse_object_1000");
                SharpGLTF.Transforms.AffineTransform Pose = ParticleRoot.GetLocalTransform(Timeline, 10f / 60);
                Assert.That(Pose.Translation.X, Is.EqualTo(-18.922390f).Within(1e-5));
                Assert.That(Pose.Translation.Z, Is.EqualTo(29.752600f).Within(1e-5));
                Assert.That(Pose.Scale.Z, Is.EqualTo(1.934561f).Within(1e-5));
                System.Numerics.Quaternion ExpectedRotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 62.02f * MathF.PI / 180);
                Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(Pose.Rotation, ExpectedRotation)), Is.EqualTo(1).Within(1e-6),
                    "The first seeded particle has a 62.02-degree Z orientation.");
                SharpGLTF.Transforms.AffineTransform SecondPose = Reloaded.LogicalNodes.Single(Node => Node.Name == "rse_object_1001").GetLocalTransform(Timeline, 0);
                System.Numerics.Quaternion SecondRotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, -56.08f * MathF.PI / 180);
                Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(SecondPose.Rotation, SecondRotation)), Is.EqualTo(1).Within(1e-6),
                    "Shared generator random state must advance in particle declaration order.");
                SharpGLTF.Transforms.AffineTransform HeldPose = ParticleRoot.GetLocalTransform(Timeline, 10.5f / 60);
                Assert.That(HeldPose.Translation, Is.EqualTo(Pose.Translation), "Baked particle poses are indexed by integer scene frame.");
                Material Material = Reloaded.LogicalMaterials.Single(Material => Material.Name.StartsWith("rse_1000_", StringComparison.Ordinal));
                Assert.That(Material.Extras!["rsm"]!["renderMode"]!.GetValue<int>(), Is.EqualTo(3));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Opacity = Timeline.Channels.Single(Channel =>
                    Channel.TargetPointerPath == $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Opacity.GetPoint(0).W, Is.Zero);
                Assert.That(Opacity.GetPoint(3f / 60).W, Is.EqualTo(.75f).Within(1e-6));
                Assert.That(Opacity.GetPoint(20f / 60).W, Is.EqualTo(.5f).Within(1e-6));
                Assert.That(Opacity.GetPoint(31f / 60).W, Is.Zero);
                Material Template = Reloaded.LogicalMaterials.Single(Material => Material.Name.StartsWith("rse_100_", StringComparison.Ordinal));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> TemplateOpacity = Timeline.Channels.Single(Channel =>
                    Channel.TargetPointerPath == $"/materials/{Template.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(TemplateOpacity.GetPoint(3f / 60).W, Is.Zero, "The particle source object must remain hidden.");
            }
            finally
            {
                File.Delete(OutputPath);
            }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void RealSceneExportsExactOpacityAndRoundTrips(int FramesPerSecond)
        {
            string OutputPath = Path.Combine(Path.GetTempPath(), $"culdcept-rse-{Guid.NewGuid():N}.glb");
            try
            {
                ModelRoot Model = RseExporter.Load(Path.Combine(EffectDirectory, "EB_401.RSE"), FramesPerSecond);
                Model.SaveGLB(OutputPath);
                ModelRoot Reloaded = ModelRoot.Load(OutputPath);
                Assert.That(Reloaded.LogicalImages.Count, Is.GreaterThan(0));
                Assert.That(Reloaded.LogicalMaterials[0].Alpha, Is.EqualTo(AlphaMode.BLEND));
                Assert.That(Reloaded.LogicalAnimations.Count, Is.EqualTo(1), "The undeclared neighboring RSA must not be loaded.");
                Animation Animation = Reloaded.LogicalAnimations.Single();
                Assert.That(Animation.Duration, Is.EqualTo(31f / FramesPerSecond).Within(1e-6));
                AnimationChannel Channel = Animation.Channels.Single();
                Assert.That(Channel.TargetPointerPath, Is.EqualTo("/materials/0/pbrMetallicRoughness/baseColorFactor"));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Sampler = Channel.GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                Assert.That(Sampler.GetPoint(0).W, Is.Zero);
                Assert.That(Sampler.GetPoint(1f / FramesPerSecond).W, Is.EqualTo(0.5).Within(1e-6));
                Assert.That(Sampler.GetPoint(1.5f / FramesPerSecond).W, Is.EqualTo(0.75).Within(1e-6));
                Assert.That(Sampler.GetPoint(2f / FramesPerSecond).W, Is.EqualTo(1));
                Assert.That(Sampler.GetPoint(15f / FramesPerSecond).W, Is.EqualTo(0.55).Within(1e-6));
                Assert.That(Sampler.GetPoint(30.5f / FramesPerSecond).W, Is.Zero);
                Assert.That(Reloaded.Extras!["rse"]!["timingAssumed"]!.GetValue<bool>(), Is.True);
                Assert.That(Reloaded.LogicalMaterials[0].Extras!["rsm"]!["opacity"]!.GetValue<float>(), Is.Zero,
                    "Export must preserve original RSM metadata, not overwrite initial opacity.");
            }
            finally
            {
                File.Delete(OutputPath);
            }
        }

        [Test]
        public void MissingDeclaredResourcesAndInvalidSlotsFailExplicitly()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-rse-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                string ScenePath = Path.Combine(Folder, "EB_401.RSE");
                File.WriteAllText(ScenePath, SimpleScene);
                ModelRoot MissingModel = RseExporter.Load(ScenePath);
                Assert.That(MissingModel.LogicalMeshes, Is.Empty);
                Assert.That(MissingModel.Extras!["rse"]!["absentDependencies"]!.AsArray().Single()!["file"]!.GetValue<string>(),
                    Is.EqualTo("EB_401.RSM"));
                File.Copy(Path.Combine(EffectDirectory, "EB_401.RSM"), Path.Combine(Folder, "EB_401.RSM"));
                Assert.That(Assert.Throws<FileNotFoundException>(() => RseExporter.Load(ScenePath))!.FileName,
                    Does.EndWith("EB_401.RST"));
                File.Copy(Path.Combine(EffectDirectory, "EB_401.RST"), Path.Combine(Folder, "EB_401.RST"));
                // A corrupt same-name motion is deliberately undeclared by this scene.
                File.WriteAllText(Path.Combine(Folder, "EB_401.RSA"), "not an RSA file");
                Assert.DoesNotThrow(() => RseExporter.Load(ScenePath));
                File.WriteAllText(ScenePath, SimpleScene.Replace("MOPA=0,0,", "MOPA=0,9,"));
                Assert.That(Assert.Throws<InvalidDataException>(() => RseExporter.Load(ScenePath))!.Message, Does.Contain("slot 9"));
                Assert.Throws<ArgumentOutOfRangeException>(() => RseExporter.Load(ScenePath, float.NaN));
                Assert.Throws<ArgumentOutOfRangeException>(() => RseExporter.Load(ScenePath, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => RseExporter.Load(ScenePath, float.Epsilon));
            }
            finally
            {
                foreach (string FilePath in Directory.EnumerateFiles(Folder))
                {
                    File.Delete(FilePath);
                }

                Directory.Delete(Folder);
            }
        }

        [Test]
        public void CollapsedBaseMorphTopologyPreservesEverySourceTriangleAndFrame()
        {
            CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_459_02.RSM"), false, false);
            Assert.That(Package.Document.DicMeshByNodeIndex[0].ListPosition.Distinct().Count(), Is.EqualTo(2));
            ModelRoot Model = RseExporter.Load(Path.Combine(EffectDirectory, "EB_459_02.RSE"));
            Mesh Mesh = Model.LogicalMeshes.Single();
            Assert.That(Mesh.Primitives.Sum(Primitive => Primitive.GetTriangleIndices().Count()), Is.EqualTo(20));
            Assert.That(Mesh.Primitives.Sum(Primitive => Primitive.GetVertexAccessor("POSITION").Count), Is.EqualTo(60));
            RsaChannel Channel = RSAReader.Parse(File.ReadAllBytes(Path.Combine(EffectDirectory, "EB_459_02.RSA")), "source").ListChannel[0];
            for (int GroupIndex = 0; GroupIndex < Mesh.Primitives.Count; GroupIndex++)
            {
                MeshPrimitive Primitive = Mesh.Primitives[GroupIndex];
                RsmCorner[] Corners = Package.Document.DicMeshByNodeIndex[0].ListMeshGroup[GroupIndex].ArrayCorner;
                SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> Base = Primitive.GetVertexAccessor("POSITION").AsVector3Array();
                Assert.That(Primitive.MorphTargetsCount, Is.EqualTo(Channel.ListVertexFrame.Count));
                for (int Frame = 0; Frame < Channel.ListVertexFrame.Count; Frame++)
                {
                    SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> Deltas = Primitive.GetMorphTargetAccessors(Frame)["POSITION"].AsVector3Array();
                    for (int Corner = 0; Corner < Corners.Length; Corner++)
                    {
                        System.Numerics.Vector3 Expected = Channel.ListVertexFrame[Frame].ArrayPosition[Corners[Corner].PositionId];
                        Expected.Z = -Expected.Z;
                        Assert.That(System.Numerics.Vector3.Distance(Base[Corner] + Deltas[Corner], Expected), Is.LessThan(1e-4f));
                    }
                }
                int Expanded = Channel.ListVertexFrame.FindIndex(Frame => Frame.Frame == 43);
                SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> Delta = Primitive.GetMorphTargetAccessors(Expanded)["POSITION"].AsVector3Array();
                SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> Normal = Primitive.GetVertexAccessor("NORMAL").AsVector3Array();
                SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3>? NormalTargets = Primitive.GetMorphTargetAccessors(Expanded).TryGetValue("NORMAL", out Accessor? NormalTarget)
                    ? NormalTarget.AsVector3Array() : null;
                foreach ((int A, int B, int C) Triangle in Primitive.GetTriangleIndices())
                {
                    System.Numerics.Vector3 A = Base[Triangle.Item1] + Delta[Triangle.Item1];
                    System.Numerics.Vector3 B = Base[Triangle.Item2] + Delta[Triangle.Item2];
                    System.Numerics.Vector3 C = Base[Triangle.Item3] + Delta[Triangle.Item3];
                    System.Numerics.Vector3 TargetNormal = System.Numerics.Vector3.Zero;
                    if (NormalTargets != null)
                    {
                        TargetNormal = NormalTargets[Triangle.Item1];
                    }

                    System.Numerics.Vector3 Stored = Normal[Triangle.Item1] + TargetNormal;
                    Assert.That(System.Numerics.Vector3.Dot(System.Numerics.Vector3.Cross(B - A, C - A), Stored),
                        Is.GreaterThan(0), "Expanded faces must retain the authored normal-facing winding after reflection.");
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void VertexFramesPreserveCollapsedAndSharedBaseIdentitiesWithoutEpsilon(bool FullyCollapsed, bool DuplicateMaterials)
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-vertex-topology-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            try
            {
                CuldceptModel Package = CuldceptModel.Load(Path.Combine(EffectDirectory, "EB_491.RSM"), false, false);
                RsmMesh Source = Package.Document.DicMeshByNodeIndex[0];
                System.Numerics.Vector3[] Original = Source.ListPosition.ToArray();
                byte[] Motion = CreateVertexMotion(Package, false);
                for (int Index = 0; Index < Source.ListPosition.Count; Index++)
                {
                    if (FullyCollapsed || Index < 2)
                    {
                        Source.ListPosition[Index] = System.Numerics.Vector3.Zero;
                    }
                }

                int FirstFramePositions = 24 + Package.Document.ListNode.Count * 8 + 8;
                for (int Index = 0; Index < Source.ListPosition.Count; Index++)
                {
                    System.Numerics.Vector3 Position = Source.ListPosition[Index];
                    BinaryPrimitives.WriteSingleBigEndian(Motion.AsSpan(FirstFramePositions + Index * 12), Position.X);
                    BinaryPrimitives.WriteSingleBigEndian(Motion.AsSpan(FirstFramePositions + Index * 12 + 4), Position.Y);
                    BinaryPrimitives.WriteSingleBigEndian(Motion.AsSpan(FirstFramePositions + Index * 12 + 8), Position.Z);
                }
                if (DuplicateMaterials)
                {
                    Package.Document.ListMaterial.Add(Package.Document.ListMaterial[0]);
                    RsmMeshGroup Group = Source.ListMeshGroup[0];
                    Source.ListMeshGroup.Add(new RsmMeshGroup
                    {
                        ArrayCorner = Group.ArrayCorner.ToArray(),
                        MaterialIndex = 1,
                        PrimitiveType = Group.PrimitiveType,
                        VertexFormat = Group.VertexFormat,
                        Unknown10 = Group.Unknown10
                    });
                }
                using (FileStream Stream = File.Create(Path.Combine(Folder, "EB_401.RSM")))
                using (BigEndianBinaryWriter Writer = new BigEndianBinaryWriter(Stream))
                {
                    RSMWriter.WriteRsm(Writer, Package.Document);
                }

                File.WriteAllBytes(Path.Combine(Folder, "vertex.RSA"), Motion);
                string Text = SimpleScene.Replace("AUTORST=1", "AUTORST=0")
                    .Replace("[MotionList]", "[MotionList]\n0=vertex.RSA")
                    .Replace("[Setup]", "[KeyData]\nTIME=0\nMOTION=0,0,0,NOLOOP\n[Setup]");
                string ScenePath = Path.Combine(Folder, "scene.RSE"), Output = Path.Combine(Folder, "scene.glb");
                File.WriteAllText(ScenePath, Text);
                RseExporter.Load(ScenePath).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                Mesh Mesh = Root.LogicalMeshes.Single();
                Assert.That(Mesh.Primitives.Count, Is.EqualTo(Source.ListMeshGroup.Count));
                Assert.That(Mesh.MorphWeights, Is.EqualTo(new float[3]));
                for (int GroupIndex = 0; GroupIndex < Mesh.Primitives.Count; GroupIndex++)
                {
                    MeshPrimitive Primitive = Mesh.Primitives[GroupIndex];
                    RsmMeshGroup Group = Source.ListMeshGroup[GroupIndex];
                    SharpGLTF.Memory.IAccessorArray<System.Numerics.Vector3> Base = Primitive.GetVertexAccessor("POSITION").AsVector3Array();
                    Assert.That(Base.Count, Is.EqualTo(Group.ArrayCorner.Length));
                    Assert.That(Primitive.GetTriangleIndices().Count(), Is.EqualTo(Group.ArrayCorner.Length / 3));
                    Assert.That(Primitive.MorphTargetsCount, Is.EqualTo(3));
                    Assert.That(Primitive.GetMorphTargetAccessors(0)["POSITION"].AsVector3Array(),
                        Is.All.EqualTo(System.Numerics.Vector3.Zero), "The bind frame must not acquire an epsilon displacement.");
                    for (int Index = 0; Index < Group.ArrayCorner.Length; Index++)
                    {
                        int Id = Group.ArrayCorner[Index].PositionId;
                        System.Numerics.Vector3 Bind = Source.ListPosition[Id];
                        Bind.Z = -Bind.Z;
                        Assert.That(Base[Index], Is.EqualTo(Bind));
                        for (int Key = 1; Key < 3; Key++)
                        {
                            System.Numerics.Vector3 Expected = Original[Id] + new System.Numerics.Vector3(.5f + Key * 2, Key * .5f, -Key);
                            Expected.Z = -Expected.Z;
                            System.Numerics.Vector3 Delta = Primitive.GetMorphTargetAccessors(Key)["POSITION"].AsVector3Array()[Index];
                            Assert.That(System.Numerics.Vector3.Distance(Base[Index] + Delta, Expected), Is.LessThan(1e-5f));
                        }
                    }
                    if (DuplicateMaterials)
                    {
                        Assert.That(Primitive.Material.Name, Does.Contain($"material_{Group.MaterialIndex}_"));
                    }
                }
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void CorpusScenesEitherExportOrExplainUnsupportedBehavior()
        {
            CheckScenePreviews(Directory.GetFiles(EffectDirectory, "*.RSE"));
        }

        [Test]
        public void CorpusScenesRoundTripOrExplainUnsupportedBehavior()
        {
            CheckScenePreviews(Directory.GetFiles(EffectDirectory, "*.RSE"), ExportOnly: true,
                        DependencyDirectory: Environment.GetEnvironmentVariable("CULDCEPT_EFFECT_DEPENDENCY_DIRECTORY"));
        }

        [TestCase("EB_443")]
        [TestCase("EB_446")]
        [TestCase("EB_454")]
        [TestCase("EB_456")]
        [TestCase("EB_457")]
        [TestCase("EB_465")]
        [TestCase("EB_476")]
        [TestCase("EE_729")]
        public void EffectPreviewLoopIsStable(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") });
        }

        [TestCase("EB_479")]
        [TestCase("EB_480")]
        [TestCase("EB_502")]
        [TestCase("EE_705")]
        [TestCase("EE_710")]
        [TestCase("EE_714")]
        [TestCase("ET_901")]
        public void NewParticleModesAndFilenameModelsOpenAndRender(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true);
        }

        [TestCase("EB_418")]
        [TestCase("EE_715")]
        [TestCase("EE_716")]
        [TestCase("EE_717")]
        [TestCase("EM_318")]
        [TestCase("ES_013")]
        [TestCase("ES_014")]
        [TestCase("ES_045")]
        [TestCase("ES_109")]
        [TestCase("EB_478")]
        [TestCase("EB_489")]
        [TestCase("EB_501")]
        [TestCase("EB_519")]
        [TestCase("EE_719")]
        [TestCase("ES_020")]
        [TestCase("ES_044")]
        [TestCase("ES_055")]
        [TestCase("ES_105")]
        public void DelayedUvAndParticleTextureEffectsOpenAndRender(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true);
        }

        [TestCase("EM_308")]
        [TestCase("ES_047")]
        [TestCase("ES_048")]
        [TestCase("ES_056")]
        [TestCase("ES_059")]
        public void ParticleModelMotionEffectsOpenAndRender(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true);
        }

        [TestCase("EB_491")]
        [TestCase("EB_556")]
        [TestCase("ES_030_03")]
        [TestCase("ES_032")]
        [TestCase("ES_080")]
        [TestCase("ES_107")]
        public void VertexMotionEffectsOpenAndRender(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true);
        }

        [TestCase("EB_439")]
        [TestCase("ES_110")]
        [TestCase("ES_115")]
        [TestCase("EB_466")]
        [TestCase("EB_468")]
        [TestCase("EB_472")]
        [TestCase("EB_473")]
        [TestCase("EB_549")]
        [TestCase("ES_027")]
        [TestCase("ES_103")]
        [TestCase("ES_051")]
        [TestCase("ES_089")]
        [TestCase("EM_301")]
        [TestCase("ES_106")]
        public void ExpandedMaterialAndParticleSyntaxEffectsOpenAndRender(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true);
        }

        [TestCase("EE_702")]
        [TestCase("EE_726")]
        [TestCase("EE_732")]
        [TestCase("EE_734")]
        [TestCase("ES_005")]
        [TestCase("ES_102")]
        [TestCase("ES_017")]
        [TestCase("ES_062")]
        public void TextBankAndSparseBillboardEffectsOpenAndRender(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true);
        }

        [TestCase("EB_459_02", true)]
        [TestCase("EM_306", false)]
        public void PreservedTopologyAndLittleEndianEffectsOpenAndRender(string SceneName, bool SampleEveryFrame)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true,
                        RequireVisible: true, SampleEveryFrame: SampleEveryFrame);
        }

        [Test]
        public void GeneratorMotionEffectOpensExportsAndVisiblyRendersAcrossItsFullTimeline()
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, "EB_459.RSE") }, RequireSupported: true,
                        RequireVisible: true, SampleEveryFrame: true);
        }

        [TestCase("EB_462", true)]
        [TestCase("EE_711", false)]
        [TestCase("EE_722", true)]
        [TestCase("EE_724", true)]
        [TestCase("EE_736", true)]
        public void UvDummyAndUnboundParticleScenesOpenExportAndRender(string SceneName, bool RequireVisible)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true,
                        RequireVisible: RequireVisible, SampleEveryFrame: true);
        }

        [TestCase("EE_721")]
        [TestCase("ES_019")]
        public void NativeRejectedMotionScenesOpenExportAndVisiblyRenderWithoutCorrectedAnimation(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true,
                        RequireVisible: true, SampleEveryFrame: true);
        }

        [Test]
        public void NativeTransitionMotionSceneOpensExportsAndVisiblyRendersAcrossItsFullTimeline()
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, "EM_305.RSE") }, RequireSupported: true,
                        RequireVisible: true, SampleEveryFrame: true);
        }

        [Test]
        public void NestedEffectSceneOpensExportsAndVisiblyRendersAcrossItsFullTimeline()
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, "ES_030.RSE") }, RequireSupported: true,
                        RequireVisible: true, SampleEveryFrame: true);
        }

        [TestCase(30)]
        [TestCase(60)]
        public void NestedEffectPreservesRealChildVertexFramesOpacityAndLoadedUi(int FramesPerSecond)
        {
            string Source = Path.Combine(EffectDirectory, "ES_030.RSE");
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-nested-real-{Guid.NewGuid():N}.glb");
            try
            {
                RseExporter.Load(Source, FramesPerSecond).SaveGLB(Output);
                ModelRoot Root = ModelRoot.Load(Output);
                ModelRoot Child = RseExporter.Load(Path.Combine(EffectDirectory, "ES_030_03.RSE"), FramesPerSecond);
                System.Text.Json.Nodes.JsonNode Metadata = Root.Extras!["rse"]!["nestedEffects"]!.AsArray().Single()!;
                int Id = Metadata["objects"]!.AsArray().Single()!["exportObjectId"]!.GetValue<int>();
                Assert.That(Metadata["totalFrames"]!.GetValue<int>(), Is.EqualTo(63));
                Animation Timeline = Root.LogicalAnimations.Single();
                Assert.That(Timeline.Duration, Is.EqualTo(92f / FramesPerSecond).Within(1e-6));
                AnimationChannel[] VertexChannels = Child.LogicalAnimations.Single().Channels
                    .Where(Channel => Channel.TargetNodePath == PropertyPath.weights).ToArray();
                Assert.That(VertexChannels, Is.Not.Empty);
                foreach (AnimationChannel? Channel in VertexChannels)
                {
                    string Target = Channel.TargetNode!.Name.Replace("rse_0_", $"rse_{Id}_");
                    SharpGLTF.Animations.ICurveSampler<float[]> Expected = Channel.GetSamplerOrNull<float[]>().CreateCurveSampler(true);
                    SharpGLTF.Animations.ICurveSampler<float[]> Actual = Timeline.Channels.Single(Candidate =>
                    {
                        if (Candidate.TargetNode == null)
                        {
                            return false;
                        }

                        return Candidate.TargetNode.Name == Target && Candidate.TargetNodePath == PropertyPath.weights;
                    }).GetSamplerOrNull<float[]>().CreateCurveSampler(true);
                    for (int Frame = 0; Frame <= 92; Frame++)
                    {
                        Assert.That(Actual.GetPoint(Frame / (float)FramesPerSecond),
                            Is.EqualTo(Expected.GetPoint(Math.Min(Frame, 62) / (float)FramesPerSecond)), $"Child morph frame {Frame}");
                    }
                }
                Material Material = Root.LogicalMaterials.Single(Material => Material.Name.StartsWith($"rse_{Id}_", StringComparison.Ordinal));
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> Opacity = Timeline.Channels.Single(Channel => Channel.TargetPointerPath ==
                    $"/materials/{Material.LogicalIndex}/pbrMetallicRoughness/baseColorFactor")
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                SharpGLTF.Animations.ICurveSampler<System.Numerics.Vector4> ChildOpacity = Child.LogicalAnimations.Single().Channels.Single(Channel => Channel.TargetPointerPath ==
                    $"/materials/0/pbrMetallicRoughness/baseColorFactor")
                    .GetSamplerOrNull<System.Numerics.Vector4>().CreateCurveSampler(true);
                for (int Frame = 0; Frame <= 92; Frame++)
                {
                    Assert.That(Opacity.GetPoint(Frame / (float)FramesPerSecond).W,
                        Is.EqualTo(ChildOpacity.GetPoint(Math.Min(Frame, 62) / (float)FramesPerSecond).W).Within(1e-6));
                }

                using CuldceptSagaExplorer Window = new global::CuldceptSagaExplorer.CuldceptSagaExplorer { ShowInTaskbar = false };
                global::GraphicsManager.Model3DViewer Viewer = Window.Controls.OfType<global::GraphicsManager.Model3DViewer>().Single();
                _ = Window.Handle;
                _ = Viewer.Handle;
                System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Window.GetType().GetMethod("LoadEffect", Flags)!.Invoke(Window, new object[] { Source });
                Label Status = (Label)Window.GetType().GetField("EffectStatus", Flags)!.GetValue(Window)!;
                Assert.That(Status.Text, Does.Contain("Generator 3, frame 0: ES_030_03.RSE")
                    .And.Contain("independent child motion start times"));
                Viewer.ExportEffect(Output);
                Assert.That(ModelRoot.Load(Output).Extras!["source"]!["culdceptSaga"]!["effectScene"]!["nestedEffects"]!.AsArray().Count, Is.EqualTo(1));
            }
            finally { File.Delete(Output); }
        }

        [TestCase("ES_008")]
        [TestCase("EB_511")]
        [TestCase("EB_513")]
        public void NativeUnmatchedBindingScenesOpenExportAndVisiblyRenderAcrossTheirFullTimelines(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true,
                        RequireVisible: true, SampleEveryFrame: true);
        }

        [TestCase("ES_008", "Object 0: POS3=", "NOT redirected")]
        [TestCase("EB_511", "Generator 2, OBJ=102, particles 1500..1509", "NOT replaced")]
        [TestCase("EB_513", "Object 100: EB_513_01.RAM", "NOT corrected to .RSM")]
        public void LoadedUnmatchedBindingScenesDisplayExactWarningsAndExportTheSameProvenance(
            string SceneName, string Detail, string Policy)
        {
            string Source = Path.Combine(EffectDirectory, SceneName + ".RSE");
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-binding-warning-{Guid.NewGuid():N}.glb");
            try
            {
                Assert.DoesNotThrow(() => RseExporter.Load(Source));
                using CuldceptSagaExplorer Window = new global::CuldceptSagaExplorer.CuldceptSagaExplorer { ShowInTaskbar = false };
                global::GraphicsManager.Model3DViewer Viewer = Window.Controls.OfType<global::GraphicsManager.Model3DViewer>().Single();
                _ = Window.Handle;
                _ = Viewer.Handle;
                System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Window.GetType().GetMethod("LoadEffect", Flags)!.Invoke(Window, new object[] { Source });
                Label Status = (Label)Window.GetType().GetField("EffectStatus", Flags)!.GetValue(Window)!;
                Assert.That(Status.Text, Does.Contain("WARNING").And.Contain(Detail).And.Contain(Policy));
                Assert.That(Viewer.GetAnimationNames().Single(), Does.Contain(SceneName));
                Viewer.ExportEffect(Output);
                System.Text.Json.Nodes.JsonNode Metadata = ModelRoot.Load(Output).Extras!["source"]!["culdceptSaga"]!["effectScene"]!;
                Assert.That(Metadata["unmatchedSetupCommands"]!.AsArray().Count,
                    Is.EqualTo(SceneName == "ES_008" ? 3 : 1));
                Assert.That(Metadata["unboundParticleGenerators"]!.AsArray().Count,
                    Is.EqualTo(SceneName == "ES_008" ? 0 : 1));
                Assert.That(Metadata["ignoredModelDeclarations"]!.AsArray().Count,
                    Is.EqualTo(SceneName == "EB_513" ? 1 : 0));
            }
            finally { File.Delete(Output); }
        }

        [TestCase("EE_738", true, false)]
        [TestCase("EE_742", true, false)]
        [TestCase("EE_745", true, false)]
        [TestCase("EE_746", false, true)]
        [TestCase("ES_016", true, false)]
        public void RecoveredDependenciesOpenExportAndRender(string SceneName, bool RequireVisible, bool SampleEveryFrame)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDirectory, SceneName + ".RSE") }, RequireSupported: true,
                        RequireVisible: RequireVisible, SampleEveryFrame: SampleEveryFrame, DependencyDirectory: EffectDependencyDirectory);
        }

        [TestCase("EE_706")]
        [TestCase("EE_707")]
        [TestCase("EE_737")]
        public void AlternateAuthoredPreviewScenesOpenWithoutReplacingTheBrokenOriginal(string SceneName)
        {
            CheckScenePreviews(new[] { Path.Combine(EffectDependencyDirectory, SceneName + ".RSE") },
                        RequireSupported: true, RequireVisible: true);
        }

        private static void CheckScenePreviews(string[] ScenePaths, bool RequireSupported = false, bool ExportOnly = false,
            bool RequireVisible = false, bool SampleEveryFrame = false, string? DependencyDirectory = null)
        {
            int Supported = 0;
            int Unsupported = 0;
            int MissingResources = 0;
            int RejectedData = 0;
            int UnsupportedModelHeaders = 0;
            string SuccessStatus = ExportOnly ? "Exported and reloaded" : "Opened and rendered";
            List<(string Name, string Status, string Stage, string Detail)> Outcomes = new List<(string Name, string Status, string Stage, string Detail)>();
            using Form Window = new Form { ShowInTaskbar = false };
            using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                new PresentationParameters
                {
                    DeviceWindowHandle = Window.Handle,
                    BackBufferWidth = 128,
                    BackBufferHeight = 128,
                    DepthStencilFormat = DepthFormat.Depth24Stencil8,
                    PresentationInterval = PresentInterval.Immediate
                });
            using AlphaTestEffect Effect = new AlphaTestEffect(Device) { VertexColorEnabled = true };
            using RenderTarget2D Target = new RenderTarget2D(Device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
            foreach (string ScenePath in ScenePaths.OrderBy(Path => Path, StringComparer.OrdinalIgnoreCase))
            {
                string Name = Path.GetFileName(ScenePath);
                RseScene Scene;
                try
                {
                    Scene = RseReader.Parse(File.ReadAllText(ScenePath), ScenePath);
                }
                catch (NotSupportedException Error)
                {
                    Record("Unsupported", "Parse", Error.Message);
                    Unsupported++;
                    continue;
                }
                catch (InvalidDataException Error)
                {
                    if (Error.Message.Contains(ScenePath))
                    {
                        RejectedData++;
                        Record("Rejected data", "Parse", Error.Message);
                    }
                    else
                    {
                        Record("Unexpected failure", "Parse", Error.ToString());
                    }

                    continue;
                }
                catch (Exception Error)
                {
                    Record("Unexpected failure", "Parse", Error.ToString());
                    continue;
                }
                ModelRoot Root;
                try
                {
                    Root = RseExporter.Load(ScenePath, DependencyDirectory: DependencyDirectory);
                }
                catch (FileNotFoundException Error)
                {
                    TestContext.WriteLine($"Missing dependency: {Path.GetFileName(ScenePath)}: {Error.FileName}");
                    MissingResources++;
                    Record("Missing dependency", "Export", $"{Error.Message} Dependency: {Error.FileName}");
                    continue;
                }
                catch (NotSupportedException Error)
                {
                    TestContext.WriteLine($"Unsupported resources: {Path.GetFileName(ScenePath)}: {Error.Message}");
                    Unsupported++;
                    Record("Unsupported", "Export", Error.Message);
                    continue;
                }
                catch (InvalidDataException Error) when (Error.Message == "Invalid RSM file header.")
                {
                    TestContext.WriteLine($"Unsupported model header: {Path.GetFileName(ScenePath)}: {Error.Message}");
                    UnsupportedModelHeaders++;
                    Record("Unsupported model header", "Export", Error.Message);
                    continue;
                }
                catch (Exception Error)
                {
                    Record("Unexpected failure", "Export", Error.ToString());
                    continue;
                }
                string Output = Path.Combine(Path.GetTempPath(), $"culdcept-rse-census-{Guid.NewGuid():N}.glb");
                string Stage = "GLB round trip";
                try
                {
                    Root.SaveGLB(Output);
                    ModelRoot Reloaded = ModelRoot.Load(Output);
                    if (ExportOnly)
                    {
                        Record(SuccessStatus, "Complete", $"empty={Scene.IsEmpty}; GPU unchecked");
                        Supported++;
                        continue;
                    }
                    Stage = "GPU open";
                    using Animated3DModel Direct = new Animated3DModel(Device, Root) { Lighting = SceneLighting.Unlit };
                    using Animated3DModel RoundTrip = new Animated3DModel(Device, Reloaded) { Lighting = SceneLighting.Unlit };
                    Stage = "GPU draw";
                    int VisibleSamples = 0;
                    double Previous = 0;
                    IEnumerable<int> SampleFrames = SampleEveryFrame ? Enumerable.Range(0, Scene.TotalFrames)
                        : new[] { 0, Scene.TotalFrames / 4, Scene.TotalFrames / 2, Scene.TotalFrames * 3 / 4, Scene.TotalFrames - 1 }.Distinct();
                    foreach (int Frame in SampleFrames)
                    {
                        double Time = Frame / 60.0;
                        GameTime Tick = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Time - Previous));
                        Direct.Update(Tick);
                        RoundTrip.Update(Tick);
                        Color[] Pixels = Render(Direct);
                        if (!Render(RoundTrip).SequenceEqual(Pixels))
                        {
                            throw new InvalidDataException($"{Name}: direct and reloaded previews differ at frame {Frame}.");
                        }

                        if (Pixels.Any(Pixel => Pixel != Color.Black))
                        {
                            VisibleSamples++;
                        }

                        Previous = Time;
                    }
                    Direct.IsPaused = true;
                    Color[] Paused = Render(Direct);
                    Direct.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
                    if (!Render(Direct).SequenceEqual(Paused))
                    {
                        throw new InvalidDataException($"{Name}: paused draw changed.");
                    }

                    Direct.RestartAnimation();
                    RoundTrip.RestartAnimation();
                    if (!Render(Direct).SequenceEqual(Render(RoundTrip)))
                    {
                        throw new InvalidDataException($"{Name}: restart round trip differs.");
                    }

                    Direct.IsPaused = false;
                    float Duration = Root.LogicalAnimations.Single().Duration;
                    GameTime LoopTick = new GameTime(TimeSpan.Zero,
                        TimeSpan.FromTicks((long)Math.Ceiling((double)Duration * TimeSpan.TicksPerSecond)));
                    Direct.Update(LoopTick);
                    RoundTrip.Update(LoopTick);
                    // A float clip duration can leave a sub-tick remainder, not exactly frame zero.
                    if (Direct.AnimationTime >= 1.0 / TimeSpan.TicksPerSecond + 1e-12
                        || RoundTrip.AnimationTime != Direct.AnimationTime)
                    {
                        throw new InvalidDataException($"{Name}: loop clocks did not return within one tick of zero.");
                    }

                    if (!Render(Direct).SequenceEqual(Render(RoundTrip)))
                    {
                        throw new InvalidDataException($"{Name}: looped direct/reloaded draws differ at the same clock phase.");
                    }

                    if (RequireVisible && VisibleSamples == 0)
                    {
                        throw new InvalidDataException($"{Name}: no visible geometry at the required preview checkpoints.");
                    }

                    Record(SuccessStatus, "Complete", $"empty={Scene.IsEmpty}; visibleSamples={VisibleSamples}");
                    Supported++;
                }
                catch (Exception Error)
                {
                    Record("Unexpected failure", Stage, Error.ToString());
                }
                finally
                {
                    Effect.Texture = null;
                    Device.SetVertexBuffer(null);
                    File.Delete(Output);
                }

                void Record(string Status, string Stage, string Detail)
                {
                    Outcomes.Add((Name, Status, Stage, Detail));
                    TestContext.WriteLine($"RSE_RESULT\t{Name}\t{Status}\t{Stage}\t{Detail.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")}");
                    TestContext.Progress.WriteLine($"RSE_PROGRESS {Outcomes.Count}: {Name}: {Status} ({Stage})");
                }

                Color[] Render(Animated3DModel Model)
                {
                    Device.SetRenderTarget(Target);
                    try
                    {
                        Device.Clear(Color.Black);
                        Model.Draw(Device, new GameTime(), Effect);
                    }
                    finally { Device.SetRenderTarget(null); }
                    Color[] Pixels = new Color[128 * 128];
                    Target.GetData(Pixels);
                    return Pixels;
                }
            }
            foreach (IGrouping<(string Status, string Stage, string Reason), (string Name, string Status, string Stage, string Detail)>? Group in Outcomes.Where(Outcome => Outcome.Status != SuccessStatus)
                .GroupBy(Outcome => (Outcome.Status, Outcome.Stage, Reason: System.Text.RegularExpressions.Regex.Replace(
                    Outcome.Detail, @"^(?:.*?, line \d+: |[A-Za-z]:\\.*?: )| Dependency: .*", ""))).OrderByDescending(Group => Group.Count()))
            {
                TestContext.WriteLine($"RSE_GROUP\t{Group.Count()}\t{Group.Key.Status}\t{Group.Key.Stage}\t{Group.Key.Reason}\t{string.Join(", ", Group.Select(Outcome => Outcome.Name))}");
            }

            TestContext.WriteLine($"RSE scenes: {Supported} supported, {Unsupported} explicitly unsupported, {MissingResources} missing dependencies, {RejectedData} rejected scene-data validation, {UnsupportedModelHeaders} unsupported model headers.");
            Assert.That(Outcomes.Count, Is.EqualTo(ScenePaths.Length));
            Assert.That(Outcomes.Where(Outcome => Outcome.Status == "Unexpected failure").Select(Outcome =>
                $"{Outcome.Name} ({Outcome.Stage}): {Outcome.Detail}"), Is.Empty);
            Assert.That(Supported, Is.GreaterThanOrEqualTo(1));
            if (RequireSupported)
            {
                Assert.That(Supported, Is.EqualTo(ScenePaths.Length));
            }
        }

        [Test]
        public void SelectingWorkingFolderDiscoversItsOwnPreviewFallbackAndClearsStaleSelections()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-working-folder-{Guid.NewGuid():N}");
            string First = Path.Combine(Folder, "first"), Second = Path.Combine(Folder, "second");
            string Preview = Path.Combine(First, "Culdcept Saga Extracted", "eeprev");
            Directory.CreateDirectory(Preview);
            Directory.CreateDirectory(Second);
            try
            {
                using CuldceptSagaExplorer Window = new global::CuldceptSagaExplorer.CuldceptSagaExplorer { ShowInTaskbar = false };
                TextBox WorkingFolder = (TextBox)Window.Controls.Find("txtWorkingFolder", true).Single();
                TabControl Tabs = Window.Controls.OfType<TabControl>().Single();
                TabPage Effects = Tabs.TabPages.Cast<TabPage>().Single(Page => Page.Text == "Effects");
                TextBox Fallback = ((TableLayoutPanel)((ToolStripControlHost)Effects.ContextMenuStrip!.Items[0]).Control).Controls
                    .OfType<FlowLayoutPanel>().Single().Controls.OfType<TextBox>().Single();
                System.Reflection.MethodInfo Refresh = Window.GetType().GetMethod("OpenEffectFolder",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                WorkingFolder.Text = First;
                Refresh.Invoke(Window, null);
                Assert.That(Fallback.Text, Is.EqualTo(Preview));
                Fallback.Clear();
                Assert.That(Fallback.Text, Is.Empty, "Manual clearing stays in effect until another folder refresh.");
                Fallback.Text = Path.Combine(Folder, "custom");
                WorkingFolder.Text = Second;
                Refresh.Invoke(Window, null);
                Assert.That(Fallback.Text, Is.Empty, "A new extraction must not inherit another folder's fallback.");
                WorkingFolder.Text = First;
                Refresh.Invoke(Window, null);
                Assert.That(Fallback.Text, Is.EqualTo(Preview));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void ExplorerExposesEffectsAndViewportLoadsExportsAndPreservesModelOnFailure()
        {
            string OutputPath = Path.Combine(Path.GetTempPath(), $"culdcept-rse-viewport-{Guid.NewGuid():N}.glb");
            string UnsupportedPath = OutputPath + ".RSE";
            try
            {
                using CuldceptSagaExplorer Window = new global::CuldceptSagaExplorer.CuldceptSagaExplorer { ShowInTaskbar = false };
                TabControl Tabs = Window.Controls.OfType<TabControl>().Single();
                Assert.That(Tabs.TabPages.Cast<TabPage>().Any(Page => Page.Text == "Effects"), Is.True);
                TabPage Effects = Tabs.TabPages.Cast<TabPage>().Single(Page => Page.Text == "Effects");
                Control[] EffectControls = ((TableLayoutPanel)((ToolStripControlHost)Effects.ContextMenuStrip!.Items[0]).Control).Controls
                    .OfType<FlowLayoutPanel>().Single().Controls.Cast<Control>().ToArray();
                TextBox FallbackFolder = EffectControls.OfType<TextBox>().Single();
                Assert.That(FallbackFolder.ReadOnly, Is.True);
                Assert.That(FallbackFolder.Text, Is.Empty, "Fallback lookup must be opt-in.");
                Assert.That(EffectControls.OfType<Button>().Any(Button => Button.Text == "Fallback folder..."), Is.True);
                Assert.That(EffectControls.OfType<Button>().Any(Button => Button.Text == "Clear fallback"), Is.True);
                global::GraphicsManager.Model3DViewer Viewer = Window.Controls.OfType<global::GraphicsManager.Model3DViewer>().Single();
                _ = Window.Handle;
                _ = Viewer.Handle;
                Viewer.LoadModel(Path.Combine(EffectDirectory, "EB_401.RSE"), 30);
                Assert.That(Viewer.GetAnimationNames().Single(), Does.Contain("EB_401"));
                Assert.That(Viewer.Textures.Count, Is.GreaterThan(0));
                Viewer.SetAnimationPaused(true);
                Viewer.RestartAnimation();
                Viewer.SetAnimationPaused(false);
                Viewer.ExportEffect(OutputPath);
                Assert.That(ModelRoot.Load(OutputPath).LogicalAnimations.Single().Duration, Is.EqualTo(31f / 30).Within(1e-6));
                ModelTexture[] ArrayOriginalTextures = Viewer.Textures.ToArray();
                File.WriteAllText(UnsupportedPath, File.ReadAllText(Path.Combine(EffectDirectory, "EB_401.RSE")).Replace("MOPA", "UNSUPPORTED"));
                Assert.Throws<NotSupportedException>(() => Viewer.LoadModel(UnsupportedPath));
                Assert.That(Viewer.Textures, Is.EqualTo(ArrayOriginalTextures), "Failed loads must not dispose the active effect.");
                Viewer.LoadModel(OutputPath);
                Assert.That(Viewer.GetAnimationNames().Single(), Does.Contain("EB_401"));
                Viewer.ExportEffect(OutputPath);
                Assert.That(ModelRoot.Load(OutputPath).Extras!["rendering"]!["effect"]!["framesPerSecond"]!.GetValue<float>(), Is.EqualTo(30));
                Viewer.LoadModel(Path.Combine(EffectDirectory, "EB_425_01.RSE"), 30);
                Assert.That(Viewer.Textures, Is.Empty, "An empty effect must replace the previous visible model.");
                Assert.That(Viewer.GetAnimationNames().Single(), Does.Contain("EB_425_01"));
                Viewer.SetAnimationPaused(true);
                Viewer.RestartAnimation();
                Viewer.SetAnimationPaused(false);
                Viewer.ExportEffect(OutputPath);
                Assert.That(ModelRoot.Load(OutputPath).LogicalMeshes, Is.Empty);
                Viewer.LoadModel(OutputPath);
                Assert.That(Viewer.Textures, Is.Empty);
                Viewer.LoadModel(Path.Combine(EffectDirectory, "EB_401.RSE"));
                Assert.That(Viewer.Textures.Count, Is.GreaterThan(0), "Nonempty effects must still load after an empty effect.");
                Viewer.LoadModel(Path.Combine(EffectDirectory, "EE_738.RSE"), EffectDependencyDirectory: EffectDependencyDirectory);
                Assert.That(Viewer.EffectFallbackDependencies, Is.EquivalentTo(new[]
                {
                    Path.Combine(EffectDependencyDirectory, "EE_738.RSM"), Path.Combine(EffectDependencyDirectory, "EE_738.RST")
                }));
                Viewer.ExportEffect(OutputPath);
                Assert.That(ModelRoot.Load(OutputPath).Extras!["source"]!["culdceptSaga"]!["effectScene"]!["dependencies"], Is.Not.Null);
                ModelTexture[] RecoveredTextures = Viewer.Textures.ToArray();
                Assert.Throws<DirectoryNotFoundException>(() => Viewer.LoadModel(Path.Combine(EffectDirectory, "EE_738.RSE"),
                    EffectDependencyDirectory: Path.Combine(EffectDependencyDirectory, "absent")));
                Assert.That(Viewer.Textures, Is.EqualTo(RecoveredTextures));
                Assert.That(Viewer.EffectFallbackDependencies.Count, Is.EqualTo(2), "Failed loads must preserve the active resolution provenance.");
            }
            finally
            {
                File.Delete(OutputPath);
                File.Delete(UnsupportedPath);
            }
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void BillboardsMatchTheNativeBasisAndThreeAxisSpinUnderPitchAndRoll(bool ExportAndReload, bool Spherical, bool ThreeAxisSpin)
        {
            Vector3[] Points = { new Vector3(-.8f, -.4f, 0), new Vector3(.8f, -.4f, 0), new Vector3(0, .8f, 0) };
            Vector3 Origin = new Vector3(.5f, .25f, -.2f);
            Vector3 Scale = new Vector3(1.3f, .7f, 1.1f);
            const float Spin = .7f;
            Matrix SpinMatrix = ThreeAxisSpin
                ? Matrix.CreateRotationY(-.3f) * Matrix.CreateRotationX(-.5f) * Matrix.CreateRotationZ(Spin)
                : Matrix.CreateRotationZ(Spin);
            Quaternion SpinRotation = Quaternion.CreateFromRotationMatrix(SpinMatrix);
            MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty> Mesh = new MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty>("triangle");
            PrimitiveBuilder<MaterialBuilder, VertexPosition, VertexEmpty, VertexEmpty> Primitive = Mesh.UsePrimitive(new MaterialBuilder("white").WithDoubleSide(true));
            Primitive.AddTriangle(new VertexPosition(new System.Numerics.Vector3(Points[0].X, Points[0].Y, Points[0].Z)),
                new VertexPosition(new System.Numerics.Vector3(Points[1].X, Points[1].Y, Points[1].Z)),
                new VertexPosition(new System.Numerics.Vector3(Points[2].X, Points[2].Y, Points[2].Z)));
            SceneBuilder Builder = new SceneBuilder();
            NodeBuilder Billboard = new NodeBuilder("vertical")
                .WithLocalTranslation(new System.Numerics.Vector3(Origin.X, Origin.Y, Origin.Z))
                .WithLocalScale(new System.Numerics.Vector3(Scale.X, Scale.Y, Scale.Z));
            Billboard.Extras = new System.Text.Json.Nodes.JsonObject { ["rseBillboard"] = Spherical ? "spherical" : "verticalProjected" };
            NodeBuilder SpinNode = new NodeBuilder("spin").WithLocalRotation(
                new System.Numerics.Quaternion(SpinRotation.X, SpinRotation.Y, SpinRotation.Z, SpinRotation.W));
            Billboard.AddNode(SpinNode);
            Builder.AddRigidMesh(Mesh, SpinNode);
            ModelRoot Root = Builder.ToGltf2();
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-projected-{Guid.NewGuid():N}.glb");
            try
            {
                if (ExportAndReload)
                {
                    Root.SaveGLB(Output);
                    Root = ModelRoot.Load(Output);
                }
                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = Window.Handle,
                        BackBufferWidth = 128,
                        BackBufferHeight = 128,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                using AlphaTestEffect Effect = new AlphaTestEffect(Device) { VertexColorEnabled = true };
                using RenderTarget2D Target = new RenderTarget2D(Device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                using Animated3DModel Model = new Animated3DModel(Device, Root) { Lighting = SceneLighting.Unlit, IsPaused = true };
                Color[] Render(Animated3DModel ActiveModel, Matrix View, Matrix World)
                {
                    Device.SetRenderTarget(Target);
                    try
                    {
                        Device.Clear(Color.Black);
                        ActiveModel.Draw(Device, new GameTime(), Effect, View, Matrix.CreateOrthographic(4, 4, .1f, 100), World);
                    }
                    finally { Device.SetRenderTarget(null); }
                    Color[] Pixels = new Color[128 * 128];
                    Target.GetData(Pixels);
                    return Pixels;
                }
                try
                {
                    foreach (Vector3 Direction in new[] { Vector3.UnitZ, Vector3.UnitX, new Vector3(1, 1, 1), new Vector3(1, 4, 1), -Vector3.UnitZ })
                    {
                        foreach (Matrix World in new[] { Matrix.Identity, Matrix.CreateRotationY(.6f) })
                        {
                            Vector3 Center = Vector3.Transform(Origin, World);
                            Matrix View = Matrix.CreateLookAt(Center + Vector3.Normalize(Direction) * 5, Center,
                                Vector3.Transform(Vector3.Up, Matrix.CreateRotationZ(.3f)));
                            Matrix Camera = World * View;
                            Matrix Basis = Spherical
                                ? new Matrix(Camera.M11, Camera.M21, Camera.M31, 0,
                                    Camera.M12, Camera.M22, Camera.M32, 0, Camera.M13, Camera.M23, Camera.M33, 0, 0, 0, 0, 1)
                                : new Matrix(Camera.M11, 0, Camera.M31, 0, 0, 1, 0, 0,
                                    Camera.M13, 0, Camera.M33, 0, 0, 0, 0, 1);
                            Matrix Expected = SpinMatrix * Matrix.CreateScale(Scale) * Basis * Matrix.CreateTranslation(Origin);
                            MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty> ReferenceMesh = new MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty>("reference");
                            PrimitiveBuilder<MaterialBuilder, VertexPosition, VertexEmpty, VertexEmpty> ReferencePrimitive = ReferenceMesh.UsePrimitive(new MaterialBuilder("white").WithDoubleSide(true));
                            VertexPosition[] Vertices = Points.Select(Point => Vector3.Transform(Point, Expected))
                                .Select(Point => new VertexPosition(new System.Numerics.Vector3(Point.X, Point.Y, Point.Z))).ToArray();
                            ReferencePrimitive.AddTriangle(Vertices[0], Vertices[1], Vertices[2]);
                            SceneBuilder ReferenceBuilder = new SceneBuilder();
                            ReferenceBuilder.AddRigidMesh(ReferenceMesh, new NodeBuilder("reference"));
                            using Animated3DModel Reference = new Animated3DModel(Device, ReferenceBuilder.ToGltf2()) { Lighting = SceneLighting.Unlit };
                            Color[] Actual = Render(Model, View, World);
                            Color[] ExpectedPixels = Render(Reference, View, World);
                            Assert.That(Actual.Count(Pixel => Pixel != Color.Black), Is.GreaterThan(30));
                            Assert.That(Actual.Where((Pixel, Index) => Pixel != ExpectedPixels[Index]).Count(), Is.LessThan(10),
                                "Native billboard rotation must precede scale and the full/projected camera basis.");
                            Assert.That(Render(Model, View, World), Is.EqualTo(Actual), "Paused draws must not accumulate spin.");
                        }
                    }
                }
                finally { Effect.Texture = null; Device.SetVertexBuffer(null); }
            }
            finally { File.Delete(Output); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void SphericalBillboardsFollowTheViewWhilePausedWithoutMovingTheirCenters(bool ExportAndReload, bool AnimateSpin)
        {
            MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty> Mesh = new MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty>("billboard_triangle");
            PrimitiveBuilder<MaterialBuilder, VertexPosition, VertexEmpty, VertexEmpty> Primitive = Mesh.UsePrimitive(new MaterialBuilder("white").WithDoubleSide(true));
            Primitive.AddTriangle(
                new VertexPosition(new System.Numerics.Vector3(-.8f, -.4f, 0)),
                new VertexPosition(new System.Numerics.Vector3(.8f, -.4f, 0)),
                new VertexPosition(new System.Numerics.Vector3(0, .8f, 0)));
            Vector3 Origin = new Vector3(.5f, .25f, -.2f);
            SceneBuilder Builder = new SceneBuilder();
            NodeBuilder Node = new NodeBuilder("billboard")
                .WithLocalTranslation(new System.Numerics.Vector3(Origin.X, Origin.Y, Origin.Z))
                .WithLocalScale(new System.Numerics.Vector3(1.3f, .7f, 1));
            Node.Extras = new System.Text.Json.Nodes.JsonObject { ["rseBillboard"] = "spherical" };
            NodeBuilder SpinNode = new NodeBuilder("spin");
            Node.AddNode(SpinNode);
            Builder.AddRigidMesh(Mesh, SpinNode);
            if (AnimateSpin)
            {
                SpinNode.UseRotation("pose").WithPoint(0, System.Numerics.Quaternion.Identity)
                    .WithPoint(.5f, System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, MathF.PI / 2))
                    .WithPoint(1, System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, MathF.PI / 2));
            }
            Node.UseTranslation("pose").WithPoint(0, new System.Numerics.Vector3(Origin.X, Origin.Y, Origin.Z))
                .WithPoint(.5f, new System.Numerics.Vector3(Origin.X, Origin.Y, Origin.Z))
                .WithPoint(1, new System.Numerics.Vector3(Origin.X + 1, Origin.Y, Origin.Z));
            ModelRoot Root = Builder.ToGltf2();
            string Output = Path.Combine(Path.GetTempPath(), $"culdcept-billboard-{Guid.NewGuid():N}.glb");
            try
            {
                if (ExportAndReload)
                {
                    Root.SaveGLB(Output);
                    Root = ModelRoot.Load(Output);
                }
                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = Window.Handle,
                        BackBufferWidth = 128,
                        BackBufferHeight = 128,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                using AlphaTestEffect Effect = new AlphaTestEffect(Device) { VertexColorEnabled = true };
                using RenderTarget2D Target = new RenderTarget2D(Device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                using Animated3DModel Model = new Animated3DModel(Device, Root) { Lighting = SceneLighting.Unlit, IsPaused = true };
                Color[] Render(Vector3 Direction, Matrix World)
                {
                    Vector3 Center = Vector3.Transform(Origin, World);
                    Matrix View = Matrix.CreateLookAt(Center + Vector3.Normalize(Direction) * 5, Center, Vector3.Up);
                    Device.SetRenderTarget(Target);
                    try
                    {
                        Device.Clear(Color.Black);
                        Model.Draw(Device, new GameTime(), Effect, View, Matrix.CreateOrthographic(4, 4, .1f, 100), World);
                    }
                    finally { Device.SetRenderTarget(null); }
                    Color[] Pixels = new Color[128 * 128];
                    Target.GetData(Pixels);
                    return Pixels;
                }
                try
                {
                    Color[] Front = Render(Vector3.UnitZ, Matrix.Identity);
                    Assert.That(Front.Count(Pixel => Pixel != Color.Black), Is.GreaterThan(500));
                    if (AnimateSpin)
                    {
                        Model.IsPaused = false;
                        Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.5)));
                        Model.IsPaused = true;
                        Color[] Spun = Render(Vector3.UnitZ, Matrix.Identity);
                        Assert.That(Spun.Where((Pixel, Index) => Pixel != Front[Index]).Count(), Is.GreaterThan(100));
                        Front = Spun;
                        Node SchemaSpin = Root.LogicalNodes.Single(Node => Node.Name == "spin");
                        SharpGLTF.Transforms.AffineTransform SpinPose = SchemaSpin.GetLocalTransform(Root.LogicalAnimations.Single(), .5f);
                        System.Numerics.Vector3 Point = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(-.8f, -.4f, 0), SpinPose.Rotation)
                            * new System.Numerics.Vector3(1.3f, .7f, 1);
                        Assert.That(Point.X, Is.EqualTo(.52f).Within(1e-5));
                        Assert.That(Point.Y, Is.EqualTo(-.56f).Within(1e-5), "Sprite spin precedes nonuniform billboard scale.");
                    }
                    Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(10)));
                    foreach (Vector3 Direction in new[] { Vector3.UnitX, new Vector3(1, 1, 1), -Vector3.UnitZ })
                    {
                        foreach (Matrix World in new[] { Matrix.Identity, Matrix.CreateRotationY(.6f) })
                        {
                            Color[] Rotated = Render(Direction, World);
                            Assert.That(Rotated.Where((Pixel, Index) => Pixel != Front[Index]).Count(), Is.LessThan(10),
                                "The billboard must retain its screen-space shape and center across yaw, pitch and model rotation.");
                        }
                    }
                    Assert.That(Model.AnimationTime, Is.EqualTo(AnimateSpin ? .5 : 0));
                    Assert.That(Render(Vector3.UnitZ, Matrix.Identity), Is.EqualTo(Front), "Repeated draws must not accumulate billboard rotations.");
                }
                finally
                {
                    Effect.Texture = null;
                    Device.SetVertexBuffer(null);
                }
            }
            finally { File.Delete(Output); }
        }

        [TestCase("EB_401", false)]
        [TestCase("EB_401", true)]
        [TestCase("EB_410", false)]
        [TestCase("EB_410", true)]
        [TestCase("EB_412", false)]
        [TestCase("EB_412", true)]
        [TestCase("EB_414", false)]
        [TestCase("EB_414", true)]
        [TestCase("EB_419", false)]
        [TestCase("EB_419", true)]
        [TestCase("EB_425", false)]
        [TestCase("EB_425", true)]
        [TestCase("EB_429", false)]
        [TestCase("EB_429", true)]
        [TestCase("EB_431", false)]
        [TestCase("EB_431", true)]
        [TestCase("EB_437", false)]
        [TestCase("EB_437", true)]
        [TestCase("EB_440", false)]
        [TestCase("EB_440", true)]
        [TestCase("EB_442", false)]
        [TestCase("EB_442", true)]
        public void BattleFadeActuallyRendersPausesLoopsAndRestarts(string SceneName, bool ExportAndReload)
        {
            int RenderSize = SceneName == "EB_429" || SceneName == "EB_431" ? 512 : 128;
            string OutputPath = Path.Combine(Path.GetTempPath(), $"culdcept-rse-render-{Guid.NewGuid():N}.glb");
            try
            {
                ModelRoot Root = RseExporter.Load(Path.Combine(EffectDirectory, SceneName + ".RSE"));
                int TotalFrames = Root.Extras!["rse"]!["totalFrames"]!.GetValue<int>();
                if (SceneName == "EB_442")
                {
                    // Its RSA geometry has already disappeared before the fade; isolate opacity on the initial pose.
                    Animation Timeline = Root.LogicalAnimations.Single();
                    foreach (Node Node in Root.LogicalNodes)
                    {
                        SharpGLTF.Transforms.AffineTransform Pose = Node.GetLocalTransform(Timeline, 0);
                        Timeline.CreateTranslationChannel(Node, new Dictionary<float, System.Numerics.Vector3>
                        { [0] = Pose.Translation, [Timeline.Duration] = Pose.Translation }, linear: false);
                        Timeline.CreateRotationChannel(Node, new Dictionary<float, System.Numerics.Quaternion>
                        { [0] = Pose.Rotation, [Timeline.Duration] = Pose.Rotation }, linear: false);
                        Timeline.CreateScaleChannel(Node, new Dictionary<float, System.Numerics.Vector3>
                        { [0] = Pose.Scale, [Timeline.Duration] = Pose.Scale }, linear: false);
                    }
                }
                if (ExportAndReload)
                {
                    Root.SaveGLB(OutputPath);
                    Root = ModelRoot.Load(OutputPath);
                }

                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = Window.Handle,
                        BackBufferWidth = RenderSize,
                        BackBufferHeight = RenderSize,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                using AlphaTestEffect Effect = new AlphaTestEffect(Device) { VertexColorEnabled = true };
                using RenderTarget2D Target = new RenderTarget2D(Device, RenderSize, RenderSize, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                using Animated3DModel Model = new Animated3DModel(Device, Root) { Lighting = SceneLighting.Unlit };
                Color[] Render()
                {
                    Device.SetRenderTarget(Target);
                    try
                    {
                        Device.Clear(Color.Black);
                        Model.Draw(Device, new GameTime(), Effect);
                    }
                    finally
                    {
                        Device.SetRenderTarget(null);
                    }
                    Color[] ArrayPixels = new Color[RenderSize * RenderSize];
                    Target.GetData(ArrayPixels);
                    return ArrayPixels;
                }
                void Advance(double Seconds)
                {
                    Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Seconds)));
                }

                try
                {
                    Color[] Initial = Render();
                    bool MotionScene = SceneName == "EB_414";
                    bool ObjectFade = SceneName == "EB_442";
                    Assert.That(Initial.All(Pixel => Pixel == Color.Black), Is.EqualTo(!MotionScene && !ObjectFade));
                    bool LongScene = MotionScene || SceneName == "EB_425" || SceneName == "EB_429" || SceneName == "EB_431" || SceneName == "EB_437" || SceneName == "EB_440";
                    double FirstFrame = ObjectFade ? 72.25 : LongScene ? 20.25 : 1.25;
                    double PeakFrame = ObjectFade ? 77.25 : LongScene ? 30.25 : 2.25;
                    Advance(FirstFrame / 60);
                    Color[] Half = Render();
                    Assert.That(Half.Count(Pixel => Pixel != Color.Black), Is.GreaterThan(100), "RSE opacity must reveal initially invisible geometry.");
                    Advance((PeakFrame - FirstFrame) / 60);
                    Color[] Peak = Render();
                    Assert.That(Peak.Where((Pixel, Index) => Pixel != Half[Index]).Count(), Is.GreaterThan(100));
                    Model.IsPaused = true;
                    Advance(10);
                    Assert.That(Render(), Is.EqualTo(Peak));
                    Model.IsPaused = false;
                    Advance((TotalFrames - .75 - PeakFrame) / 60);
                    Assert.That(Render().All(Pixel => Pixel == Color.Black), Is.True, "The final frame must leave no visible particles.");
                    Advance((PeakFrame + .75) / 60);
                    Assert.That(Render(), Is.EqualTo(Peak), "Preview loop must return to the same peak.");
                    Model.RestartAnimation();
                    Assert.That(Render(), Is.EqualTo(Initial));
                }
                finally
                {
                    Effect.Texture = null;
                    Device.SetVertexBuffer(null);
                }
            }
            finally
            {
                File.Delete(OutputPath);
            }
        }
    }
}
