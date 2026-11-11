using CuldceptSagaExplorer.ModelManager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [Category("ModelCorpus")]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class ModelCorpusTests
    {
        public static IEnumerable<TestCaseData> ModelFiles()
        {
            string ModelDirectory = Environment.GetEnvironmentVariable("CULDCEPT_MODEL_DIRECTORY")
                ?? @"D:\CuldceptSaga\glTF\DATA06";
            if (!Directory.Exists(ModelDirectory))
            {
                throw new DirectoryNotFoundException($"Model corpus directory does not exist: {ModelDirectory}");
            }

            string[] Files = Directory.EnumerateFiles(ModelDirectory, "*", SearchOption.AllDirectories)
                .Where(ModelPath => string.Equals(Path.GetExtension(ModelPath), ".glb", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetExtension(ModelPath), ".gltf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(ModelPath => ModelPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (Files.Length == 0)
            {
                throw new InvalidDataException($"No GLB or glTF files were found in {ModelDirectory}.");
            }

            foreach (string ModelFile in Files)
            {
                yield return new TestCaseData(ModelFile)
                    .SetName($"LoadAndDraw({Path.GetRelativePath(ModelDirectory, ModelFile)})");
            }
        }

        [TestCaseSource(nameof(ModelFiles))]
        public void LoadAndDraw(string FilePath)
        {
            TestContext.Progress.WriteLine($"Testing {FilePath}");
            TestContext.WriteLine($"Model: {FilePath}");
            TestContext.WriteLine("Stage: creating the WindowsDX graphics device");
            using Form Window = new Form
            {
                ShowInTaskbar = false,
                ClientSize = new System.Drawing.Size(256, 256)
            };
            using GraphicsDevice Device = new GraphicsDevice(
                GraphicsAdapter.DefaultAdapter,
                GraphicsProfile.HiDef,
                new PresentationParameters
                {
                    DeviceWindowHandle = Window.Handle,
                    BackBufferWidth = 256,
                    BackBufferHeight = 256,
                    DepthStencilFormat = DepthFormat.Depth24Stencil8,
                    PresentationInterval = PresentInterval.Immediate,
                    IsFullScreen = false
                });
            using AlphaTestEffect Effect = new AlphaTestEffect(Device) { VertexColorEnabled = true };
            using RenderTarget2D Target = new RenderTarget2D(Device, 256, 256, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);

            TestContext.WriteLine("Stage: strict glTF validation, geometry conversion, and texture/buffer upload");
            Animated3DModel Model = new Animated3DModel(Device, FilePath);
            Texture2D[] TextureResources = Model.ListTexture.Select(Texture => Texture.Texture).ToArray();
            try
            {
                ModelRoot Source = SharpGLTF.Schema2.ModelRoot.Load(FilePath);
                Assert.That(Model.ListTexture.Count, Is.EqualTo(Source.LogicalTextures.Count));
                Assert.That(Model.GetTextureNames(), Is.EqualTo(Model.ListTexture.Select(Texture => Texture.Name)));
                Assert.That(Model.GetAnimationNames(), Is.EqualTo(Source.LogicalAnimations.Select(Animation =>
                    string.IsNullOrWhiteSpace(Animation.Name) ? $"Animation {Animation.LogicalIndex}" : Animation.Name)));
                for (int TextureIndex = 0; TextureIndex < Model.ListTexture.Count; TextureIndex++)
                {
                    ModelTexture Texture = Model.ListTexture[TextureIndex];
                    SharpGLTF.Schema2.Texture Original = Source.LogicalTextures[TextureIndex];
                    string? MaterialName = null;
                    foreach (SharpGLTF.Schema2.Material Material in Source.LogicalMaterials)
                    {
                        if (string.IsNullOrWhiteSpace(Material.Name))
                        {
                            continue;
                        }

                        SharpGLTF.Schema2.MaterialChannel? BaseColor = Material.FindChannel("BaseColor");
                        if (BaseColor.HasValue && BaseColor.Value.Texture == Original)
                        {
                            MaterialName = Material.Name;
                            break;
                        }
                    }

                    string? ImageName = null;
                    if (Original.PrimaryImage != null)
                    {
                        ImageName = Original.PrimaryImage.Name;
                    }

                    string ExpectedName = $"Texture {TextureIndex}";
                    if (!string.IsNullOrWhiteSpace(MaterialName))
                    {
                        ExpectedName = MaterialName;
                    }

                    if (!string.IsNullOrWhiteSpace(ImageName))
                    {
                        ExpectedName = ImageName;
                    }

                    if (!string.IsNullOrWhiteSpace(Original.Name))
                    {
                        ExpectedName = Original.Name;
                    }
                    Assert.That(Texture.Index, Is.EqualTo(TextureIndex));
                    Assert.That(Texture.Name, Is.EqualTo(ExpectedName));
                    Assert.That(Texture.Texture.IsDisposed, Is.False);
                    Assert.That(Texture.Texture.GraphicsDevice, Is.SameAs(Device));
                    Assert.That(Texture.Texture.Width, Is.GreaterThan(0));
                    Color[] TexturePixels = new Color[Texture.Texture.Width * Texture.Texture.Height];
                    Texture.Texture.GetData(TexturePixels);
                }
                List<string> AnimationNames = Model.GetAnimationNames();
                AnimationNames.Clear();
                Assert.That(Model.GetAnimationNames().Count, Is.EqualTo(Source.LogicalAnimations.Count),
                    "Returned name lists must not mutate model metadata.");
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(Source.LogicalAnimations.Count > 0 ? 0 : -1));

                TestContext.WriteLine("Stage: updating and rendering a frame");
                Device.SetRenderTarget(Target);
                try
                {
                    Device.Clear(Color.CornflowerBlue);
                    GameTime Time = new GameTime(TimeSpan.Zero, TimeSpan.Zero);
                    Model.Update(Time);
                    Model.Draw(Device, Time, Effect);
                    for (int AnimationIndex = 0; AnimationIndex < Source.LogicalAnimations.Count; AnimationIndex++)
                    {
                        Model.PlayAnimation(AnimationIndex);
                        GameTime Sample = new GameTime(TimeSpan.FromSeconds(0.37), TimeSpan.FromSeconds(0.37));
                        Model.Update(Sample);
                        Device.Clear(Color.CornflowerBlue);
                        Model.Draw(Device, Sample, Effect);
                    }
                }
                finally
                {
                    Device.SetRenderTarget(null);
                }

                TestContext.WriteLine("Stage: GPU readback");
                Color[] Pixels = new Color[256 * 256];
                Target.GetData(Pixels);
            }
            finally
            {
                TestContext.WriteLine("Stage: releasing model resources");
                Effect.Texture = null;
                Device.SetVertexBuffer(null);
                Device.Indices = null;
                Device.Textures[0] = null;
                Model.UnloadContent();
            }
            Assert.That(TextureResources.All(Texture => Texture.IsDisposed), Is.True);
            Assert.That(Model.ListTexture, Is.Empty);
            Assert.That(Model.GetAnimationNames(), Is.Empty);
        }
    }
}
