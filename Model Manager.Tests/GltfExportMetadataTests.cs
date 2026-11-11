using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.ExternalModel;
using CuldceptSagaExplorer.AssetManager.RSE;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using SharpGLTF.Schema2;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Forms;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class GltfExportMetadataTests
    {
        private static string Effects => @"D:\CuldceptSaga\Culdcept Saga Extracted\data\EFFECT";

        [TestCase("EB_401")]
        [TestCase("EE_735")]
        [TestCase("ES_004")]
        public void ExportRoundTripPreservesNativePlaybackDataAndDoesNotMutatePreview(string Name)
        {
            ModelRoot Original = RseExporter.Load(Path.Combine(Effects, Name + ".RSE"));
            byte[] OriginalBytes = Original.WriteGLB().ToArray();
            string PathName = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".glb");
            try
            {
                GltfExportMetadata.SaveGlb(Original, PathName);
                Assert.That(Original.WriteGLB().ToArray(), Is.EqualTo(OriginalBytes), "Saving must not change the running preview.");
                ModelRoot Export = ModelRoot.Load(PathName);
                Assert.That(Export.Extras!["rse"], Is.Null);
                Assert.That(Export.Extras["rendering"]!["version"]!.GetValue<int>(), Is.EqualTo(1));
                Assert.That(Export.Extras["source"]!["culdceptSaga"]!["effectScene"], Is.Not.Null);
                foreach (Material? Material in Export.LogicalMaterials)
                {
                    JsonNode? Rsm = null;
                    JsonNode? Ifl = null;
                    JsonNode? RawOpacity = null;
                    if (Material.Extras != null)
                    {
                        Rsm = Material.Extras["rsm"];
                        Ifl = Material.Extras["ifl"];
                        RawOpacity = Material.Extras["rseRawOpacity"];
                    }

                    Assert.That(Rsm, Is.Null);
                    Assert.That(Ifl, Is.Null);
                    Assert.That(RawOpacity, Is.Null);
                }
                GltfExportMetadata.PrepareForNativeReaders(Export);
                Assert.That(Export.Extras!["rse"]!.ToJsonString(), Is.EqualTo(Original.Extras!["rse"]!.ToJsonString()));
                for (int I = 0; I < Original.LogicalMaterials.Count; I++)
                {
                    foreach (string Key in new[] { "rsm", "ifl", "rseRawOpacity" })
                    {
                        // Nullable optional playlist fields can be explicit null in the new contract.
                        JsonNode? Expected = null;
                        if (Original.LogicalMaterials[I].Extras != null)
                        {
                            Expected = Original.LogicalMaterials[I].Extras[Key];
                        }

                        JsonNode? Actual = null;
                        if (Export.LogicalMaterials[I].Extras != null)
                        {
                            Actual = Export.LogicalMaterials[I].Extras[Key];
                        }

                        if (Expected is JsonObject Values)
                        {
                            foreach (System.Collections.Generic.KeyValuePair<string, JsonNode?> Value in Values)
                            {
                                string? ActualJson = null;
                                if (Actual != null)
                                {
                                    JsonNode? Child = Actual[Value.Key];
                                    if (Child != null)
                                    {
                                        ActualJson = Child.ToJsonString();
                                    }
                                }

                                string? ExpectedJson = null;
                                if (Value.Value != null)
                                {
                                    ExpectedJson = Value.Value.ToJsonString();
                                }

                                Assert.That(ActualJson, Is.EqualTo(ExpectedJson), $"{I}: {Key}.{Value.Key}");
                            }
                        }
                        else
                        {
                            Assert.That(Actual, Is.Null);
                        }
                    }
                }

                for (int I = 0; I < Original.LogicalNodes.Count; I++)
                {
                    string? ExportBillboard = null;
                    if (Export.LogicalNodes[I].Extras != null)
                    {
                        JsonNode? ExportNode = Export.LogicalNodes[I].Extras["rseBillboard"];
                        if (ExportNode != null)
                        {
                            ExportBillboard = ExportNode.ToJsonString();
                        }
                    }

                    string? OriginalBillboard = null;
                    if (Original.LogicalNodes[I].Extras != null)
                    {
                        JsonNode? OriginalNode = Original.LogicalNodes[I].Extras["rseBillboard"];
                        if (OriginalNode != null)
                        {
                            OriginalBillboard = OriginalNode.ToJsonString();
                        }
                    }

                    Assert.That(ExportBillboard, Is.EqualTo(OriginalBillboard));
                }

                Assert.That(Export.LogicalAnimations.Select(A => A.Duration), Is.EqualTo(Original.LogicalAnimations.Select(A => A.Duration)));
            }
            finally { File.Delete(PathName); }
        }

        [Test]
        public void NativeModelImportPreservesReconstructionSnapshotsAcrossBothExportGenerations()
        {
            ModelRoot Model = RsmExtractor.ConvertRsmToGlb(Path.Combine(Effects, "EB_401.RSM"));
            string Folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Folder);
            try
            {
                string Legacy = Path.Combine(Folder, "legacy.glb"), Neutral = Path.Combine(Folder, "neutral.glb");
                Model.SaveGLB(Legacy);
                GltfExportMetadata.SaveGlb(Model, Neutral);
                AssetManager.RSM.RsmDocument Before = GltfImporter.ImportFromGltf(Legacy).Document;
                AssetManager.RSM.RsmDocument After = GltfImporter.ImportFromGltf(Neutral).Document;
                byte[] Serialize(global::CuldceptSagaExplorer.AssetManager.RSM.RsmDocument Document)
                {
                    using MemoryStream Stream = new MemoryStream();
                    using (BigEndianBinaryWriter Writer = new BigEndianBinaryWriter(Stream))
                    {
                        global::CuldceptSagaExplorer.AssetManager.RSM.RSMWriter.WriteRsm(Writer, Document);
                    }

                    return Stream.ToArray();
                }
                Assert.That(Serialize(After), Is.EqualTo(Serialize(Before)));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void NeutralRenderingWorksWithoutSagaSourceAndOverridesConflictingLegacyFields()
        {
            ModelRoot Model = ModelRoot.CreateModel();
            Material Material = Model.CreateMaterial();
            Material.Extras = JsonNode.Parse(@"
                {""custom"":""keep"", ""rsm"":{""renderMode"":0,""opacity"":1},
                 ""rendering"":{""version"":1,""material"":{""blendMode"":""additive"",""forceBlend"":true,""opacity"":0.25,""selfIllumination"":0.75},
                 ""textureSequence"":{""textures"":[0,1],""framesPerSecond"":12,""frameIndices"":[1,0,1],""frameIndicesPerSecond"":60},
                 ""opacityTrack"":{""version"":1,""animationIndex"":0,""times"":[0,1],""values"":[-2,3],""interpolation"":""LINEAR""}}}
                ");
            GltfExportMetadata.PrepareForNativeReaders(Model);
            Assert.That(Material.Extras!["rsm"]!["renderMode"]!.GetValue<uint>(), Is.EqualTo(3));
            Assert.That(Material.Extras["rsm"]!["opacity"]!.GetValue<float>(), Is.EqualTo(.25f));
            Assert.That(Material.Extras["ifl"]!["rseFrameIndices"]!.ToJsonString(), Is.EqualTo("[1,0,1]"));
            Assert.That(Material.Extras["rseRawOpacity"]!["values"]!.ToJsonString(), Is.EqualTo("[-2,3]"));
            Assert.That(Material.Extras["custom"]!.GetValue<string>(), Is.EqualTo("keep"));
        }

        [Test]
        public void UnknownSchemaVersionIsRejected()
        {
            ModelRoot Model = ModelRoot.CreateModel();
            Model.Extras = JsonNode.Parse("{\"rendering\":{\"version\":2}}");
            Assert.Throws<InvalidDataException>(() => GltfExportMetadata.PrepareForNativeReaders(Model));
        }

        [TestCase("EB_401")]
        [TestCase("EE_735")]
        [TestCase("ES_004")]
        public void SavedNeutralExportRendersTheSamePixelsAsNativePreview(string Name)
        {
            string Output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".glb");
            using Form Window = new Form { ShowInTaskbar = false };
            using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                new PresentationParameters
                {
                    DeviceWindowHandle = Window.Handle,
                    BackBufferWidth = 128,
                    BackBufferHeight = 128,
                    DepthStencilFormat = DepthFormat.Depth24Stencil8,
                    PresentationInterval = PresentInterval.Immediate,
                    IsFullScreen = false
                });
            using RenderTarget2D Target = new RenderTarget2D(Device, 128, 128, false, SurfaceFormat.Color,
                DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
            using AlphaTestEffect Effect = new AlphaTestEffect(Device) { VertexColorEnabled = true };
            try
            {
                ModelRoot Source = RseExporter.Load(Path.Combine(Effects, Name + ".RSE"));
                GltfExportMetadata.SaveGlb(Source, Output);
                using Animated3DModel Before = new Animated3DModel(Device, Source);
                using Animated3DModel After = new Animated3DModel(Device, Output);
                Color[] Render(Animated3DModel Model)
                {
                    Device.SetRenderTarget(Target);
                    try
                    {
                        Device.Clear(Color.CornflowerBlue);
                        Model.Draw(Device, new GameTime(), Effect);
                    }
                    finally { Device.SetRenderTarget(null); }
                    Color[] Pixels = new Color[128 * 128];
                    Target.GetData(Pixels);
                    return Pixels;
                }
                bool Visible = false;
                foreach (double Seconds in Enumerable.Repeat(Source.LogicalAnimations.Max(A => (double)A.Duration) / 20, 20))
                {
                    GameTime Time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Seconds));
                    Before.Update(Time);
                    After.Update(Time);
                    Color[] Expected = Render(Before);
                    Visible |= Expected.Any(Pixel => Pixel != Color.CornflowerBlue);
                    Assert.That(Render(After), Is.EqualTo(Expected), $"{Name} at step {Seconds}");
                }
                Assert.That(Visible, Is.True, "Comparison must include visible geometry, not just empty frames.");
                Effect.Texture = null;
                Device.SetVertexBuffer(null);
                Device.Indices = null;
                Device.Textures[0] = null;
            }
            finally { File.Delete(Output); }
        }
    }
}
