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

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [Category("Animation")]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class UVAnimationTests
    {
        [Test]
        public void UVOnlyClipScrollsAlongsideTheSelectedBodyClip()
        {
            Render(true, null, (Model, Render) =>
            {
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(0), "The body clip must stay auto-selected.");
                Color[] Initial = Render();
                Advance(Model, 0.5);
                Color[] Middle = Render();
                AssertChanged(Initial, Middle, "The UV clip must scroll while the body clip plays.");

                Model.PlayAnimation(0);
                Assert.That(Render(), Is.EqualTo(Middle), "Restarting the body clip must not reset the UV scroll.");

                Advance(Model, 0.5);
                Color[] FirstLoopEnd = Render();
                AssertChanged(Initial, FirstLoopEnd, "The UV scroll must continue past the clip end instead of snapping back.");
                AssertChanged(Middle, FirstLoopEnd, "The UV scroll must keep advancing.");

                Advance(Model, 1);
                Assert.That(Render(), Is.EqualTo(Initial), "Two loops of a 0.5 scroll must land on a whole texture width.");

                Advance(Model, 0.25);
                Advance(Model, 0.25);
                Assert.That(Render(), Is.EqualTo(Middle), "UV scroll must depend on elapsed time.");

                Model.PlayAnimation(1);
                Assert.That(Render(), Is.EqualTo(Initial), "Selecting the UV clip must restart it.");
                Advance(Model, 0.5);
                Assert.That(Render(), Is.EqualTo(Middle));
                Advance(Model, 0.5);
                Assert.That(Render(), Is.EqualTo(FirstLoopEnd), "A selected UV clip must also keep scrolling.");
            });
        }

        [Test]
        public void StaticTextureTransformMatchesTheEquivalentAnimatedOffset()
        {
            Color[] AnimatedMiddle = Array.Empty<Color>();
            Color[] Untransformed = Array.Empty<Color>();
            Render(true, null, (Model, Render) =>
            {
                Advance(Model, 0.5);
                AnimatedMiddle = Render();
            });
            Render(false, null, (Model, Render) =>
            {
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(-1));
                Untransformed = Render();
                Advance(Model, 0.5);
                Assert.That(Render(), Is.EqualTo(Untransformed), "Models without UV clips must not scroll.");
            });
            // Animated U scroll runs opposite to the glTF offset, as in the game (e.g. the Alcyon waterfall).
            Render(false, new[] { -0.25f, 0 }, (Model, Render) =>
            {
                Color[] Transformed = Render();
                AssertChanged(Untransformed, Transformed, "The static texture offset must be applied.");
                Assert.That(Transformed, Is.EqualTo(AnimatedMiddle));
            });
        }

        [Test]
        public void TextureListFramesPlayWithoutASelectedClip()
        {
            Render(false, null, (Model, Render) =>
            {
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(-1));
                Color[] First = Render();
                Advance(Model, 0.25);
                Assert.That(Render(), Is.EqualTo(First), "The first frame must last 1 / framesPerSecond.");
                Advance(Model, 0.25);
                Color[] Second = Render();
                AssertChanged(First, Second, "The texture list must advance to its second frame.");
                Advance(Model, 0.5);
                Assert.That(Render(), Is.EqualTo(First), "The texture list must loop.");
            }, TextureList: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RseTextureTimelineUsesBakedIndicesAndSupportsPauseRestartAndLoop(bool RoundTrip)
        {
            Render(true, null, (Model, Render) =>
            {
                Color[] First = Render();
                Advance(Model, .25);
                Color[] Second = Render();
                AssertChanged(First, Second, "The baked timeline selects frame one at .25 seconds, not the generic 2 FPS clock.");
                Advance(Model, .25);
                Assert.That(Render(), Is.EqualTo(Second), "Baked frame holds are independent of the generic texture timer.");
                Model.IsPaused = true;
                Advance(Model, .25);
                Assert.That(Render(), Is.EqualTo(Second));
                Model.RestartAnimation();
                Assert.That(Render(), Is.EqualTo(First));
                Model.IsPaused = false;
                Advance(Model, .75);
                Assert.That(Render(), Is.EqualTo(First));
                Advance(Model, .5);
                Assert.That(Render(), Is.EqualTo(Second), "Looping restarts the baked scene timeline.");
            }, TextureList: true, RseTextureTimeline: true, RoundTrip: RoundTrip);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PausedSeekImmediatelyUpdatesBakedTexturesAndRejectsInvalidTimes(bool RoundTrip)
        {
            Render(true, null, (Model, Render) =>
            {
                Color[] First = Render();
                Advance(Model, .25);
                Color[] Second = Render();
                AssertChanged(First, Second, "The fixture must show distinct texture frames.");
                Model.IsPaused = true;
                Model.SetAnimationSeconds(0);
                Assert.That(Render(), Is.EqualTo(First));
                Model.SetAnimationSeconds(.25);
                Assert.That(Render(), Is.EqualTo(Second));
                foreach (double Time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => Model.SetAnimationSeconds(Time));
                }

                Assert.That(Model.AnimationTime, Is.EqualTo(.25));
                Assert.That(Render(), Is.EqualTo(Second));
                Model.RestartAnimation();
                Assert.That(Model.AnimationTime, Is.Zero);
                Assert.That(Model.IsPaused, Is.True);
                Assert.That(Render(), Is.EqualTo(First));
            }, TextureList: true, RseTextureTimeline: true, RoundTrip: RoundTrip);
        }

        [Test]
        public void PausedSeekAndRestartRefreshUvScrollWithoutChangingTheSelectedClip()
        {
            Render(true, null, (Model, Render) =>
            {
                Color[] First = Render();
                Advance(Model, .5);
                Color[] Middle = Render();
                AssertChanged(First, Middle, "The fixture must show scrolling UVs.");
                Model.IsPaused = true;
                Model.SetAnimationSeconds(0);
                Assert.That(Render(), Is.EqualTo(First));
                Model.SetAnimationSeconds(.5);
                Assert.That(Render(), Is.EqualTo(Middle));
                Model.RestartAnimation();
                Assert.That(Render(), Is.EqualTo(First));
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(0));
                Assert.That(Model.IsPaused, Is.True);
            });
        }

        [Test]
        public void TextureSequenceWithoutAClipCanSeekAndRestartWhilePaused()
        {
            Render(false, null, (Model, Render) =>
            {
                Color[] First = Render();
                Model.IsPaused = true;
                Model.SetAnimationSeconds(.5);
                AssertChanged(First, Render(), "Seeking must advance the independent texture clock.");
                Model.RestartAnimation();
                Assert.That(Render(), Is.EqualTo(First));
                Assert.That(Model.IsPaused, Is.True);
            }, TextureList: true);
        }

        [TestCase("missing-rate")]
        [TestCase("zero-rate")]
        [TestCase("nan-rate")]
        [TestCase("missing-indices")]
        [TestCase("empty-indices")]
        [TestCase("bad-index")]
        [TestCase("negative-index")]
        [TestCase("fractional-index")]
        [TestCase("missing-scene")]
        public void InvalidRseTextureTimelinesFailExplicitly(string Invalid)
        {
            string PathName = Path.Combine(Path.GetTempPath(), $"culdcept-invalid-ifl-{Guid.NewGuid():N}.gltf");
            try
            {
                WriteModel(PathName, true, null, true);
                JsonObject Root = JsonNode.Parse(File.ReadAllText(PathName))!.AsObject();
                Root["extras"] = new JsonObject { ["rse"] = new JsonObject { ["animationIndex"] = 0 } };
                JsonNode Ifl = Root["materials"]![0]!["extras"]!["ifl"]!;
                Ifl["rseFrameIndices"] = new JsonArray(0, 1);
                Ifl["rseFramesPerSecond"] = 4;
                switch (Invalid)
                {
                    case "missing-rate": Ifl.AsObject().Remove("rseFramesPerSecond"); break;
                    case "zero-rate": Ifl["rseFramesPerSecond"] = 0; break;
                    case "nan-rate": Ifl["rseFramesPerSecond"] = "NaN"; break;
                    case "missing-indices": Ifl.AsObject().Remove("rseFrameIndices"); break;
                    case "empty-indices": Ifl["rseFrameIndices"] = new JsonArray(); break;
                    case "bad-index": Ifl["rseFrameIndices"]![1] = 2; break;
                    case "negative-index": Ifl["rseFrameIndices"]![1] = -1; break;
                    case "fractional-index": Ifl["rseFrameIndices"]![1] = .5; break;
                    case "missing-scene": Root.Remove("extras"); break;
                }
                File.WriteAllText(PathName, Root.ToJsonString());
                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters { DeviceWindowHandle = Window.Handle, BackBufferWidth = 128, BackBufferHeight = 128 });
                Assert.That(() => new Animated3DModel(Device, PathName), Throws.TypeOf<InvalidDataException>()
                    .With.Message.Contains("RSE texture timeline"));
            }
            finally { File.Delete(PathName); }
        }

        private static void Advance(Animated3DModel Model, double Seconds)
        {
            Model.Update(new GameTime(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(Seconds)));
        }

        private static void AssertChanged(Color[] Before, Color[] After, string Message)
        {
            int Changed = Before.Zip(After, (BeforeColor, AfterColor) => BeforeColor != AfterColor ? 1 : 0).Sum();
            Assert.That(Changed, Is.GreaterThan(1000), Message);
        }

        private static void Render(bool Animated, float[]? StaticOffset, Action<Animated3DModel, Func<Color[]>> Test,
            bool TextureList = false, bool RseTextureTimeline = false, bool RoundTrip = false)
        {
            string ModelPath = Path.Combine(Path.GetTempPath(), $"culdcept-uv-{Guid.NewGuid():N}.gltf");
            try
            {
                WriteModel(ModelPath, Animated, StaticOffset, TextureList);
                if (RseTextureTimeline)
                {
                    JsonObject Root = JsonNode.Parse(File.ReadAllText(ModelPath))!.AsObject();
                    Root["extras"] = new JsonObject { ["rse"] = new JsonObject { ["animationIndex"] = 0 } };
                    JsonNode Ifl = Root["materials"]![0]!["extras"]!["ifl"]!;
                    Ifl["rseFrameIndices"] = new JsonArray(0, 1, 1, 0, 0);
                    Ifl["rseFramesPerSecond"] = 4;
                    Root["animations"]!.AsArray().RemoveAt(1);
                    File.WriteAllText(ModelPath, Root.ToJsonString());
                }
                string GlbPath = ModelPath + ".glb";
                if (RoundTrip)
                {
                    SharpGLTF.Schema2.ModelRoot.Load(ModelPath).SaveGLB(GlbPath);
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
                Animated3DModel Model = new Animated3DModel(Device, RoundTrip ? GlbPath : ModelPath);
                try
                {
                    Test(Model, () =>
                    {
                        Device.SetRenderTarget(Target);
                        try
                        {
                            Device.Clear(Color.CornflowerBlue);
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
                File.Delete(ModelPath + ".glb");
            }
        }

        private static byte[] CreateSplitTexture(bool Swapped = false)
        {
            using System.Drawing.Bitmap Bitmap = new System.Drawing.Bitmap(64, 64);
            for (int Y = 0; Y < 64; Y++)
            {
                for (int X = 0; X < 64; X++)
                {
                    Bitmap.SetPixel(X, Y, (X < 32) != Swapped ? System.Drawing.Color.Red : System.Drawing.Color.Lime);
                }
            }

            using MemoryStream ImageStream = new MemoryStream();
            Bitmap.Save(ImageStream, System.Drawing.Imaging.ImageFormat.Png);
            return ImageStream.ToArray();
        }

        private static void WriteModel(string ModelPath, bool Animated, float[]? StaticOffset, bool TextureList)
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

            int Positions = Accessor(new float[] { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 },
                "VEC3", 4, new float[] { -1, -1, 0 }, new float[] { 1, 1, 0 });
            int Uvs = Accessor(new float[] { 0, 1, 1, 1, 1, 0, 0, 0 }, "VEC2", 4);
            int IndexOffset = checked((int)Bytes.Position);
            foreach (ushort VertexIndex in new ushort[] { 0, 1, 2, 0, 2, 3 })
            {
                Writer.Write(VertexIndex);
            }

            Views.Add(new { buffer = 0, byteOffset = IndexOffset, byteLength = 12 });
            Accessors.Add(new { bufferView = Views.Count - 1, componentType = 5123, count = 6, type = "SCALAR" });
            int Indices = Accessors.Count - 1;

            Dictionary<string, object> TextureInfo = new Dictionary<string, object> { ["index"] = 0 };
            if (StaticOffset != null)
            {
                TextureInfo["extensions"] = new Dictionary<string, object>
                {
                    ["KHR_texture_transform"] = new { offset = StaticOffset }
                };
            }

            Dictionary<string, object> Root = new Dictionary<string, object>
            {
                ["asset"] = new { version = "2.0" },
                ["extensionsUsed"] = new[] { "KHR_texture_transform", "KHR_animation_pointer" },
                ["scene"] = 0,
                ["scenes"] = new[] { new { nodes = new[] { 0, 1 } } },
                ["nodes"] = new object[] { new { mesh = 0 }, new { name = "Body" } },
                ["meshes"] = new[]
                {
                    new
                    {
                        primitives = new[]
                        {
                            new
                            {
                                attributes = new Dictionary<string, int> { ["POSITION"] = Positions, ["TEXCOORD_0"] = Uvs },
                                indices = Indices,
                                material = 0
                            }
                        }
                    }
                },
                ["materials"] = new[]
                {
                    TextureList
                        ? new
                        {
                            pbrMetallicRoughness = new { baseColorTexture = TextureInfo, metallicFactor = 0f },
                            extras = (object)new { ifl = new { frames = new[] { 0, 1 }, framesPerSecond = 2f } }
                        }
                        : (object)new { pbrMetallicRoughness = new { baseColorTexture = TextureInfo, metallicFactor = 0f } }
                },
                ["textures"] = new[] { new { source = 0 }, new { source = 1 } },
                ["images"] = new[]
                {
                    new { uri = "data:image/png;base64," + Convert.ToBase64String(CreateSplitTexture()) },
                    new { uri = "data:image/png;base64," + Convert.ToBase64String(CreateSplitTexture(true)) }
                }
            };

            if (Animated)
            {
                int Times = Accessor(new float[] { 0, 1 }, "SCALAR", 2, new[] { 0f }, new[] { 1f });
                int BodyTranslation = Accessor(new float[] { 0, 0, 0, 1, 0, 0 }, "VEC3", 2);
                int UvOffset = Accessor(new float[] { 0, 0, 0.5f, 0 }, "VEC2", 2);
                Root["animations"] = new object[]
                {
                    new
                    {
                        name = "Body",
                        samplers = new[] { new { input = Times, output = BodyTranslation, interpolation = "LINEAR" } },
                        channels = new[] { new { sampler = 0, target = new { node = 1, path = "translation" } } }
                    },
                    new
                    {
                        name = "UVScroll",
                        samplers = new[] { new { input = Times, output = UvOffset, interpolation = "LINEAR" } },
                        channels = new[]
                        {
                            new
                            {
                                sampler = 0,
                                target = new
                                {
                                    path = "pointer",
                                    extensions = new
                                    {
                                        KHR_animation_pointer = new
                                        {
                                            pointer = "/materials/0/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset"
                                        }
                                    }
                                }
                            }
                        }
                    }
                };
            }

            Root["buffers"] = new[] { new { byteLength = Bytes.Length, uri = "data:application/octet-stream;base64," + Convert.ToBase64String(Bytes.ToArray()) } };
            Root["bufferViews"] = Views;
            Root["accessors"] = Accessors;
            File.WriteAllText(ModelPath, JsonSerializer.Serialize(Root));
        }
    }
}
