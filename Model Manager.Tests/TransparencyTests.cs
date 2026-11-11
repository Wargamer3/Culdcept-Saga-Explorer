using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Forms;
using ModelRoot = SharpGLTF.Schema2.ModelRoot;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [Category("Transparency")]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class TransparencyTests
    {

        [Test]
        public void TranslucentPartBlendsOverOpaquePartBehindItEvenWhenListedFirst()
        {
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(128, 255, 0, 0)),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 255, 0))
            }, (Model, Render) =>
            {
                Assert.That(Model.ListTexture.Select(Texture => Texture.HasTransparency), Is.EqualTo(new[] { true, false }));
                AssertCenter(Render(), new Color(128, 127, 0));
            });
        }

        [Test]
        public void FullyTransparentTexelsDoNotHideOrTintPartsBehind()
        {
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(0, 255, 0, 0)),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 255, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(0, 255, 0)));
        }

        [Test]
        public void OpaqueModelsAreUnaffected()
        {
            Render(new[] { new Quad(0, System.Drawing.Color.FromArgb(255, 40, 80, 160)) },
                (Model, Render) => AssertCenter(Render(), new Color(40, 80, 160)));
        }

        [Test]
        public void ExternalDrawKeepsTheTargetAndOccludesALaterDepthRead()
        {
            string ModelPath = Path.Combine(Path.GetTempPath(), $"culdcept-alpha-{Guid.NewGuid():N}.gltf");
            try
            {
                WriteModel(ModelPath, new[] { new Quad(0, System.Drawing.Color.FromArgb(255, 40, 80, 160)) });
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
                using RenderTarget2D Target = new RenderTarget2D(Device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
                Animated3DModel Model = new Animated3DModel(Device, ModelPath) { Lighting = SceneLighting.Unlit };
                Color Kept = new Color(12, 34, 56);
                try
                {
                    Device.SetRenderTarget(Target);
                    Device.Clear(Kept);
                    Device.BlendState = BlendState.Additive;
                    Device.DepthStencilState = DepthStencilState.None;
                    Device.RasterizerState = RasterizerState.CullNone;
                    Device.SamplerStates[0] = SamplerState.PointClamp;
                    Matrix View = Model.Camera.CreateView(1f);
                    Matrix Projection = Model.Camera.CreateProjection(1f);
                    Model.Draw(Device, new GameTime(), Effect, View, Projection, Matrix.Identity);
                    Assert.That(Device.BlendState, Is.SameAs(BlendState.Additive));
                    Assert.That(Device.DepthStencilState, Is.SameAs(DepthStencilState.None));
                    Assert.That(Device.RasterizerState, Is.SameAs(RasterizerState.CullNone));
                    Assert.That(Device.SamplerStates[0], Is.SameAs(SamplerState.PointClamp));
                    Device.SetRenderTarget(null);
                    Color[] Pixels = new Color[128 * 128];
                    Target.GetData(Pixels);
                    AssertNear(Pixels[0], Kept);
                    AssertNear(Pixels[64 * 128 + 64], new Color(40, 80, 160));
                    Device.SetRenderTarget(Target);

                    Matrix Inverse = Matrix.Invert(View);
                    Vector3 Eye = Inverse.Translation;
                    Vector3 Forward = Vector3.Normalize(-Eye);
                    Vector3 Right = Vector3.Normalize(Vector3.Cross(Vector3.Up, Forward));
                    Vector3 Up = Vector3.Cross(Forward, Right);
                    Vector3 Behind = Forward * 2f;
                    const float Span = 12f;
                    Color Rain = new Color(0, 80, 255);
                    VertexPositionColor[] Sheet = new[]
                    {
                        new VertexPositionColor(Behind - Right * Span - Up * Span, Rain),
                        new VertexPositionColor(Behind + Right * Span - Up * Span, Rain),
                        new VertexPositionColor(Behind + Right * Span + Up * Span, Rain),
                        new VertexPositionColor(Behind - Right * Span - Up * Span, Rain),
                        new VertexPositionColor(Behind + Right * Span + Up * Span, Rain),
                        new VertexPositionColor(Behind - Right * Span + Up * Span, Rain),
                    };
                    using BasicEffect Flat = new BasicEffect(Device)
                    {
                        LightingEnabled = false,
                        VertexColorEnabled = true,
                        TextureEnabled = false,
                        View = View,
                        Projection = Projection,
                        World = Matrix.Identity,
                    };
                    Device.BlendState = BlendState.Opaque;
                    Device.DepthStencilState = DepthStencilState.DepthRead;
                    Device.RasterizerState = RasterizerState.CullNone;
                    foreach (EffectPass Pass in Flat.CurrentTechnique.Passes)
                    {
                        Pass.Apply();
                        Device.DrawUserPrimitives(PrimitiveType.TriangleList, Sheet, 0, 2);
                    }

                    Device.SetRenderTarget(null);
                    Target.GetData(Pixels);
                    AssertNear(Pixels[64 * 128 + 64], new Color(40, 80, 160));
                    AssertNear(Pixels[0], Rain);

                    Device.SetRenderTarget(Target);
                    Device.Clear(Kept);
                    Model.Draw(Device, new GameTime(), Effect, View, Projection, Matrix.CreateTranslation(50f, 0f, 0f));
                    Device.SetRenderTarget(null);
                    Target.GetData(Pixels);
                    AssertNear(Pixels[0], Kept);
                    AssertNear(Pixels[64 * 128 + 64], Kept);
                }
                finally
                {
                    Device.SetRenderTarget(null);
                    Effect.Texture = null;
                    Device.SetVertexBuffer(null);
                    Model.UnloadContent();
                }
            }
            finally
            {
                File.Delete(ModelPath);
            }
        }

        [Test]
        public void RenderMode0HidesTheBackFace()
        {
            Render(new[] { new Quad(0, System.Drawing.Color.FromArgb(255, 40, 80, 160)) },
                (Model, Render) =>
                {
                    AssertCenter(Render(), new Color(40, 80, 160));
                    Model.Camera.Rotate(-MathHelper.Pi / 0.01f, 0);
                    AssertCenter(Render(), new Color(0, 0, 0));
                });
        }

        [Test]
        public void TranslucentPartsAreSortedBackToFrontFromTheCurrentView()
        {
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(128, 255, 0, 0), 1),
                new Quad(0, System.Drawing.Color.FromArgb(128, 0, 0, 255), 1)
            }, (Model, Render) =>
            {
                // Black background, then blue, then the nearer red layer on top.
                AssertCenter(Render(), new Color(128, 0, 64));
                // From behind, the blue layer is nearer and must be blended last.
                Model.Camera.Rotate(-MathHelper.Pi / 0.01f, 0);
                AssertCenter(Render(), new Color(64, 0, 128));
            });
        }

        [Test]
        public void OverlappingTranslucentPartsKeepFileOrderFromEveryAngle()
        {
            Render(new[]
            {
                new Quad(0, System.Drawing.Color.FromArgb(128, 255, 0, 0), 1),
                new Quad(0, System.Drawing.Color.FromArgb(128, 0, 0, 255), 1)
            }, (Model, Render) =>
            {
                AssertCenter(Render(), new Color(64, 0, 128));
                Model.Camera.Rotate(-MathHelper.Pi / 0.01f, 0);
                AssertCenter(Render(), new Color(64, 0, 128));
            });
        }

        [Test]
        public void TrianglesInsideATranslucentPartAreSortedBackToFront()
        {
            // One part shaped like a closed shell: a red layer at z = 0 and a blue layer at z = 0.5, in that file order.
            Quad Shell = new Quad(0, System.Drawing.Color.FromArgb(128, 255, 255, 255), 1, Layers: 2, LayerZStep: 0.5f,
                LayerColors: new[] { new Vector4(1, 0, 0, 1), new Vector4(0, 0, 1, 1) });
            Render(new[] { Shell }, (Model, Render) =>
            {
                // From the front the blue layer is nearer and is blended last.
                AssertCenter(Render(), new Color(64, 0, 128));
                // From behind the red layer is nearer, even though it comes first in the file.
                Model.Camera.Rotate(-MathHelper.Pi / 0.01f, 0);
                AssertCenter(Render(), new Color(128, 0, 64));
            });
        }

        [Test]
        public void AdditivePartsAddTheirColorAndBlackTexelsVanish()
        {
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 200, 50, 0), 2),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 100, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(200, 150, 0)));
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 0, 0, 0), 2),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 100, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(0, 100, 0)));
            // Render mode 3 is the same source-alpha + one pass as mode 2.
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 200, 50, 0), 3),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 100, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(200, 150, 0)));
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 0, 0, 0), 3),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 100, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(0, 100, 0)));
        }

        [Test]
        public void AdditivePartsAreHiddenBehindOpaqueParts()
        {
            Render(new[]
            {
                new Quad(0, System.Drawing.Color.FromArgb(255, 200, 50, 0), 2),
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 0, 100, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(0, 100, 0)));
        }

        [Test]
        public void OverlappingShadowDecalTrianglesDarkenOnlyOnce()
        {
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(128, 0, 0, 0), Layers: 2),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 200, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(0, 100, 0)));
        }

        [Test]
        public void OverlappingColoredTranslucentTrianglesStillBlendEachLayer()
        {
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(128, 255, 0, 0), Layers: 2),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 200, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(191, 50, 0)));
        }

        [Test]
        public void GameLightingIsAmbientPlusDirectionalTimesFacingAndSelfIllumBlendsBackToUnlit()
        {
            SceneLighting Lighting = new SceneLighting
            {
                Ambient = new Vector3(0.25f),
                LightDirections = new[] { new Vector4(0, 0, 1, 0) },
                LightColors = new[] { new Vector3(0.5f) },
            };
            System.Drawing.Color White = System.Drawing.Color.FromArgb(255, 200, 200, 200);
            // Facing the light (+Z): 200 * (0.25 + 0.5).
            Render(new[] { new Quad(0, White, Normal: new Vector3(0, 0, 1)) },
                (Model, Render) => AssertCenter(Render(), new Color(150, 150, 150)), Lighting);
            // Facing away: ambient only.
            Render(new[] { new Quad(0, White, Normal: new Vector3(0, 0, -1)) }, (Model, Render) =>
            {
                AssertCenter(Render(), new Color(50, 50, 50));
                Model.Lighting = SceneLighting.Unlit;
                AssertCenter(Render(), new Color(200, 200, 200));
            }, Lighting);
            // SelfIllum 0.5 = lerp(lit, unlit, 0.5).
            Render(new[] { new Quad(0, White, Normal: new Vector3(0, 0, -1), SelfIllum: 0.5f) },
                (Model, Render) => AssertCenter(Render(), new Color(125, 125, 125)), Lighting);
        }

        [Test]
        public void LightDirectionWIsNormalizedWithTheDirectionLikeTheGameShader()
        {
            SceneLighting Lighting = new SceneLighting
            {
                LightDirections = new[] { new Vector4(0, 0, 1, 1) },
                LightColors = new[] { new Vector3(1) },
            };
            // N·normalize((0,0,1,1)) = 1/√2 → 200 * 0.7071.
            Render(new[] { new Quad(0, System.Drawing.Color.FromArgb(255, 200, 200, 200), Normal: new Vector3(0, 0, 1)) },
                (Model, Render) => AssertCenter(Render(), new Color(141, 141, 141)), Lighting);
        }

        [Test]
        public void TranslucentAndAdditiveCardsAreLitFromBothSides()
        {
            SceneLighting Lighting = new SceneLighting
            {
                Ambient = new Vector3(0.25f),
                LightDirections = new[] { new Vector4(0, 0, 1, 0) },
                LightColors = new[] { new Vector3(0.5f) },
            };
            System.Drawing.Color White = System.Drawing.Color.FromArgb(255, 200, 200, 200);
            // Same factor as a front face: 200 * (0.25 + 0.5). A mode 0 back face stays ambient-only.
            Render(new[] { new Quad(0, White, RenderMode: 1, Normal: new Vector3(0, 0, -1)) },
                (Model, Render) => AssertCenter(Render(), new Color(150, 150, 150)), Lighting);
            Render(new[] { new Quad(0, White, RenderMode: 3, Normal: new Vector3(0, 0, -1)) },
                (Model, Render) => AssertCenter(Render(), new Color(150, 150, 150)), Lighting);
        }

        [Test]
        public void CharacterSelectionLightBrightensFrontFacesPastTheMapLight()
        {
            // Front face: 0.3 + 1.35 * 0.9988/√2 ≈ 1.25 → saturates; a side face only gets the 0.3 ambient.
            Render(new[] { new Quad(0, System.Drawing.Color.FromArgb(255, 200, 200, 200), Normal: new Vector3(0, 0, 1)) },
                (Model, Render) => AssertCenter(Render(), new Color(200, 200, 200)), SceneLighting.CharacterSelection);
            Render(new[] { new Quad(0, System.Drawing.Color.FromArgb(255, 200, 200, 200), Normal: new Vector3(-1, 0, 0)) },
                (Model, Render) => AssertCenter(Render(), new Color(60, 60, 60)), SceneLighting.CharacterSelection);
        }

        [Test]
        public void RsmOpacityScalesAlpha()
        {
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 255, 0, 0), Opacity: 0.5f),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 255, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(128, 127, 0)));
        }

        [Test]
        public void VertexAlphaBlendsInRenderMode0And1()
        {
            // The opaque pass blends with source alpha, so a half-alpha vertex fades even in render mode 0.
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 255, 0, 0), VertexAlpha: 0.5f),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 255, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(128, 127, 0)));
            Render(new[]
            {
                new Quad(0.5f, System.Drawing.Color.FromArgb(255, 255, 0, 0), RenderMode: 1, VertexAlpha: 0.5f),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 255, 0))
            }, (Model, Render) => AssertCenter(Render(), new Color(128, 127, 0)));
        }

        private static void AssertCenter(Color[] Pixels, Color Expected)
        {
            AssertNear(Pixels[64 * 128 + 64], Expected);
        }

        private static void AssertNear(Color Pixel, Color Expected)
        {
            Assert.That(Math.Abs(Pixel.R - Expected.R), Is.LessThanOrEqualTo(3), $"R of {Pixel}");
            Assert.That(Math.Abs(Pixel.G - Expected.G), Is.LessThanOrEqualTo(3), $"G of {Pixel}");
            Assert.That(Math.Abs(Pixel.B - Expected.B), Is.LessThanOrEqualTo(3), $"B of {Pixel}");
        }

        [TestCase(2f, 128, .5f, 128)]
        [TestCase(4f, 128, .5f, 255)]
        [TestCase(1.02f, 128, 1f, 131)]
        [TestCase(-.41f, 128, .5f, 0)]
        [TestCase(2f, 0, 1f, 0)]
        public void RawOpacityMultipliesTextureAndVertexAlphaBeforeSaturating(float Opacity, int TextureAlpha,
            float VertexAlpha, int ExpectedRed)
        {
            Render(new[]
            {
                new Quad(.5f, System.Drawing.Color.FromArgb(TextureAlpha, 255, 0, 0), RenderMode: 1, VertexAlpha: VertexAlpha),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 255, 0))
            }, (Model, Draw) =>
            {
                AssertCenter(Draw(), new Color(ExpectedRed, 255 - ExpectedRed, 0));
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.5)));
                AssertCenter(Draw(), new Color(ExpectedRed, 255 - ExpectedRed, 0));
                Model.IsPaused = true;
                Color[] Paused = Draw();
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.25)));
                Assert.That(Draw(), Is.EqualTo(Paused));
                Model.RestartAnimation();
                Assert.That(Draw(), Is.EqualTo(Paused));
            }, Configure: Root => AddRawOpacity(Root, Opacity));
        }

        [Test]
        public void RawOpacityAdditivePassUsesTheFinalAlphaProduct()
        {
            Render(new[]
            {
                new Quad(.5f, System.Drawing.Color.FromArgb(128, 255, 0, 0), RenderMode: 2, VertexAlpha: .5f),
                new Quad(0, System.Drawing.Color.FromArgb(255, 0, 80, 0))
            }, (Model, Draw) => AssertCenter(Draw(), new Color(128, 80, 0)),
                Configure: Root => AddRawOpacity(Root, 2));
        }

        [Test]
        public void RawOpacityFadeKeepsItsOpaquePlateauAndRestartsWithoutUsingTheFallback()
        {
            Render(new[]
            {
                new Quad(.5f, System.Drawing.Color.Red, RenderMode: 1),
                new Quad(0, System.Drawing.Color.Lime)
            }, (Model, Draw) =>
            {
                AssertCenter(Draw(), new Color(255, 0, 0));
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.25)));
                AssertCenter(Draw(), new Color(255, 0, 0));
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.5)));
                AssertCenter(Draw(), new Color(128, 127, 0));
                Model.IsPaused = true;
                Color[] Paused = Draw();
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.25)));
                Assert.That(Draw(), Is.EqualTo(Paused));
                Model.IsPaused = false;
                Model.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.5)));
                AssertCenter(Draw(), new Color(255, 0, 0));
                Model.RestartAnimation();
                AssertCenter(Draw(), new Color(255, 0, 0));
            }, Configure: Root =>
            {
                AddRawOpacity(Root, 2);
                Root.LogicalMaterials[0].Extras!["rseRawOpacity"]!["values"] = new JsonArray(2f, 0f);
            });
        }

        [TestCase("version")]
        [TestCase("animationIndex")]
        [TestCase("interpolation")]
        [TestCase("times")]
        [TestCase("values")]
        [TestCase("unordered")]
        [TestCase("endpoint")]
        public void MalformedRawOpacityMetadataIsNotSilentlyIgnored(string Field)
        {
            Assert.Throws<InvalidDataException>(() => Render(
                new[] { new Quad(0, System.Drawing.Color.Red) }, (Model, Draw) => Assert.Fail("Must reject before drawing"),
                Configure: Root =>
                {
                    AddRawOpacity(Root, 2);
                    JsonNode Raw = Root.LogicalMaterials[0].Extras!["rseRawOpacity"]!;
                    if (Field == "unordered")
                    {
                        Raw["times"] = new JsonArray(0f, 0f);
                    }
                    else if (Field == "endpoint")
                    {
                        Raw["times"] = new JsonArray(0f, 2f);
                    }
                    else
                    {
                        Raw.AsObject().Remove(Field);
                    }
                }));
        }

        private static void AddRawOpacity(ModelRoot Root, float Opacity)
        {
            SharpGLTF.Schema2.Animation Animation = Root.CreateAnimation("raw opacity");
            Animation.DangerousCreatePointerChannel("/materials/0/pbrMetallicRoughness/baseColorFactor",
                new Dictionary<float, System.Numerics.Vector4>
                {
                    [0] = new System.Numerics.Vector4(1, 1, 1, Math.Clamp(Opacity, 0, 1)),
                    [1] = new System.Numerics.Vector4(1, 1, 1, Math.Clamp(Opacity, 0, 1))
                }, linear: true);
            Root.LogicalMaterials[0].Extras!["rseRawOpacity"] = new JsonObject
            {
                ["version"] = 1,
                ["animationIndex"] = 0,
                ["interpolation"] = "LINEAR",
                ["times"] = new JsonArray(0f, 1f),
                ["values"] = new JsonArray(Opacity, Opacity)
            };
        }

        private static void Render(Quad[] Quads, Action<Animated3DModel, Func<Color[]>> Test, SceneLighting? Lighting = null,
            Action<ModelRoot>? Configure = null)
        {
            string ModelPath = Path.Combine(Path.GetTempPath(), $"culdcept-alpha-{Guid.NewGuid():N}.gltf");
            string GlbPath = Path.ChangeExtension(ModelPath, ".glb");
            try
            {
                WriteModel(ModelPath, Quads);
                if (Configure != null)
                {
                    ModelRoot Root = ModelRoot.Load(ModelPath);
                    Configure(Root);
                    Root.SaveGLB(GlbPath);
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
                using RenderTarget2D Target = new RenderTarget2D(Device, 128, 128, false,
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                Animated3DModel Model = new Animated3DModel(Device, Configure == null ? ModelPath : GlbPath) { Lighting = Lighting ?? SceneLighting.Unlit };
                try
                {
                    Test(Model, () =>
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
                        Color[] Pixels = new Color[128 * 128];
                        Target.GetData(Pixels);
                        return Pixels;
                    });
                }
                finally
                {
                    Effect.Texture = null;
                    Device.SetVertexBuffer(null);
                    Model.UnloadContent();
                }
            }
            finally
            {
                File.Delete(ModelPath);
                File.Delete(GlbPath);
            }
        }

        private static string CreateTexture(System.Drawing.Color Color)
        {
            using System.Drawing.Bitmap Bitmap = new System.Drawing.Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int Y = 0; Y < 8; Y++)
            {
                for (int X = 0; X < 8; X++)
                {
                    Bitmap.SetPixel(X, Y, Color);
                }
            }

            using MemoryStream ImageStream = new MemoryStream();
            Bitmap.Save(ImageStream, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ImageStream.ToArray());
        }

        private static void WriteModel(string ModelPath, Quad[] Quads)
        {
            using MemoryStream Bytes = new MemoryStream();
            using BinaryWriter Writer = new BinaryWriter(Bytes);
            List<object> Views = new List<object>();
            List<object> Accessors = new List<object>();
            int Accessor(float[] Values, string AccessorType, int Count, float[]? Min = null, float[]? Max = null)
            {
                int Offset = checked((int)Bytes.Position);
                foreach (float Value in Values)
                {
                    Writer.Write(Value);
                }

                Views.Add(new { buffer = 0, byteOffset = Offset, byteLength = Values.Length * 4 });
                Dictionary<string, object> AccessorObject = new Dictionary<string, object>
                {
                    ["bufferView"] = Views.Count - 1,
                    ["componentType"] = 5126,
                    ["count"] = Count,
                    ["type"] = AccessorType
                };
                if (Min != null)
                {
                    AccessorObject["min"] = Min;
                }
                if (Max != null)
                {
                    AccessorObject["max"] = Max;
                }
                Accessors.Add(AccessorObject);
                return Accessors.Count - 1;
            }

            int Uvs = Accessor(new float[] { 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0 }, "VEC2", 6);
            var Meshes = Quads.Select((SourceQuad, MaterialIndex) =>
            {
                float Z = SourceQuad.Z;
                float[] Layer(float LayerZ)
                {
                    return new[] { -1, -1, LayerZ, 1, -1, LayerZ, 1, 1, LayerZ, -1, -1, LayerZ, 1, 1, LayerZ, -1, 1, LayerZ };
                }

                float LastZ = Z + SourceQuad.LayerZStep * (SourceQuad.Layers - 1);
                int Positions = Accessor(Enumerable.Range(0, SourceQuad.Layers).SelectMany(LayerIndex => Layer(Z + SourceQuad.LayerZStep * LayerIndex)).ToArray(),
                    "VEC3", 6 * SourceQuad.Layers, new float[] { -1, -1, Math.Min(Z, LastZ) }, new float[] { 1, 1, Math.Max(Z, LastZ) });
                int LayerUVs = SourceQuad.Layers == 1 ? Uvs : Accessor(Enumerable.Repeat(new float[] { 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0 }, SourceQuad.Layers)
                    .SelectMany(Values => Values).ToArray(), "VEC2", 6 * SourceQuad.Layers);
                Dictionary<string, int> Attributes = new Dictionary<string, int> { ["POSITION"] = Positions, ["TEXCOORD_0"] = LayerUVs };
                if (SourceQuad.Normal is Vector3 Normal)
                {
                    Attributes["NORMAL"] = Accessor(Enumerable.Repeat(new[] { Normal.X, Normal.Y, Normal.Z }, 6 * SourceQuad.Layers)
                        .SelectMany(Values => Values).ToArray(), "VEC3", 6 * SourceQuad.Layers);
                }
                if (SourceQuad.VertexAlpha is float VertexAlpha)
                {
                    Attributes["COLOR_0"] = Accessor(Enumerable.Repeat(new[] { 1f, 1f, 1f, VertexAlpha }, 6 * SourceQuad.Layers)
                        .SelectMany(Values => Values).ToArray(), "VEC4", 6 * SourceQuad.Layers);
                }
                if (SourceQuad.LayerColors is Vector4[] LayerColors)
                {
                    Attributes["COLOR_0"] = Accessor(LayerColors.SelectMany(LayerColor => Enumerable.Repeat(new[] { LayerColor.X, LayerColor.Y, LayerColor.Z, LayerColor.W }, 6))
                        .SelectMany(Values => Values).ToArray(), "VEC4", 6 * SourceQuad.Layers);
                }
                return new
                {
                    primitives = new[]
                    {
                        new
                        {
                            attributes = Attributes,
                            material = MaterialIndex
                        }
                    }
                };
            }).ToArray();

            Dictionary<string, object> Root = new Dictionary<string, object>
            {
                ["asset"] = new { version = "2.0" },
                ["scene"] = 0,
                ["scenes"] = new[] { new { nodes = Enumerable.Range(0, Quads.Length).ToArray() } },
                ["nodes"] = Enumerable.Range(0, Quads.Length).Select(NodeIndex => new { mesh = NodeIndex }).ToArray(),
                ["meshes"] = Meshes,
                ["materials"] = Quads.Select((SourceQuad, MaterialIndex) => new Dictionary<string, object>
                {
                    ["doubleSided"] = true,
                    ["pbrMetallicRoughness"] = new { baseColorTexture = new { index = MaterialIndex }, metallicFactor = 0f },
                    ["extras"] = new { rsm = new { renderMode = SourceQuad.RenderMode, opacity = SourceQuad.Opacity, selfIllumination = SourceQuad.SelfIllum } }
                }).ToArray(),
                ["textures"] = Quads.Select((SourceQuad, TextureIndex) => new { source = TextureIndex }).ToArray(),
                ["images"] = Quads.Select(SourceQuad => new { uri = CreateTexture(SourceQuad.Color) }).ToArray(),
                ["buffers"] = new[] { new { byteLength = Bytes.Length, uri = "data:application/octet-stream;base64," + Convert.ToBase64String(Bytes.ToArray()) } },
                ["bufferViews"] = Views,
                ["accessors"] = Accessors
            };
            File.WriteAllText(ModelPath, JsonSerializer.Serialize(Root));
        }
        private sealed record Quad(float Z, System.Drawing.Color Color, int RenderMode = 0, int Layers = 1,
            Vector3? Normal = null, float SelfIllum = 0, float Opacity = 1, float? VertexAlpha = null,
            float LayerZStep = 0, Vector4[]? LayerColors = null);
    }
}
