using CuldceptSagaExplorer.ModelManager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [Category("Animation")]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class AnimationPlaybackTests
    {
        [TestCase("Rigid")]
        [TestCase("Skinned")]
        [TestCase("Morph")]
        public void PlaybackDeformsGeometryLoopsAndRestarts(string Kind)
        {
            WithModel(Kind, true, (Model, Render, CheckGeometry) =>
            {
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(0), "First clip must auto-play.");
                Color[] Initial = Render();
                Assert.That(Initial.Count(Pixel => Pixel != Color.CornflowerBlue), Is.GreaterThan(100));

                Advance(Model, 0.5);
                Color[] Middle = Render();
                AssertChanged(Initial, Middle, $"{Kind} animation must visibly change geometry.");
                CheckGeometry(new[]
                {
                    new Vector3(-0.6f, -1, 0), new Vector3(1.4f, -1, 0), new Vector3(0.4f, 1, 0)
                });

                Advance(Model, 1);
                Assert.That(Render(), Is.EqualTo(Middle), "Looping must not accumulate transforms.");
                Model.PlayAnimation(0);
                Assert.That(Render(), Is.EqualTo(Initial), "Selecting a clip must restart it immediately.");
                Advance(Model, 0.25);
                Advance(Model, 0.25);
                Assert.That(Render(), Is.EqualTo(Middle), "Playback must depend on elapsed time, not frame count.");

                Model.PlayAnimation(1);
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(1));
                Color[] OtherInitial = Render();
                float DefaultOffset = Kind == "Morph" ? 0.2f : 0;
                CheckGeometry(new[]
                {
                    new Vector3(-1 + DefaultOffset, -1, 0), new Vector3(1 + DefaultOffset, -1, 0),
                    new Vector3(DefaultOffset, 1, 0)
                });
                if (Kind != "Morph")
                {
                    Assert.That(OtherInitial, Is.EqualTo(Initial), "Switching clips must reset unkeyed channels.");
                }
                Advance(Model, 0.5);
                Color[] Other = Render();
                AssertChanged(OtherInitial, Other, "The selected second clip must play.");
                AssertChanged(Middle, Other, "Clip selection must change the animation.");
                Model.PlayAnimation(1);
                Assert.That(Render(), Is.EqualTo(OtherInitial));
                Assert.Throws<ArgumentOutOfRangeException>(() => Model.PlayAnimation(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => Model.PlayAnimation(2));
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(1));
            });
        }

        [TestCase("Rigid")]
        [TestCase("Skinned")]
        [TestCase("Morph")]
        public void ModelWithoutAnimationsKeepsItsDefaultPose(string Kind)
        {
            WithModel(Kind, false, (Model, Render, CheckGeometry) =>
            {
                Assert.That(Model.SelectedAnimationIndex, Is.EqualTo(-1));
                Color[] Initial = Render();
                float Offset = Kind == "Morph" ? 0.2f : 0;
                CheckGeometry(new[]
                {
                    new Vector3(-1 + Offset, -1, 0), new Vector3(1 + Offset, -1, 0),
                    new Vector3(Offset, 1, 0)
                });
                Advance(Model, 12);
                Assert.That(Render(), Is.EqualTo(Initial));
                Assert.Throws<ArgumentOutOfRangeException>(() => Model.PlayAnimation(0));
            });
        }

        [Test]
        public void ZeroDurationAnimationKeepsItsFirstFrame()
        {
            WithModel("Rigid", true, (Model, Render, CheckGeometry) =>
            {
                Model.PlayAnimation(2);
                Color[] Initial = Render();
                Advance(Model, 12);
                Assert.That(Render(), Is.EqualTo(Initial));
            }, ZeroDuration: true);
        }

        private static void Advance(Animated3DModel Model, double Seconds)
        {
            Model.Update(new GameTime(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(Seconds)));
        }

        [Test]
        [Category("Camera")]
        public void CameraChangesRenderedViewWithoutResettingOnAnimationSelection()
        {
            WithModel("Rigid", true, (Model, Render, CheckGeometry) =>
            {
                Color[] Initial = Render();
                Model.Camera.Rotate(70, 20);
                AssertChanged(Initial, Render(), "Orbiting must change the rendered view.");
                Model.Camera.Reset();
                Assert.That(Render(), Is.EqualTo(Initial));
                Model.Camera.Pan(40, 30, new Viewport(0, 0, 256, 256));
                Color[] Panned = Render();
                AssertChanged(Initial, Panned, "Panning must change the rendered view.");
                Model.PlayAnimation(1);
                Assert.That(Render(), Is.EqualTo(Panned), "Clip switching must preserve the camera.");
                Model.Camera.Reset();
                Model.Camera.Zoom(2);
                AssertChanged(Initial, Render(), "The wheel must move the camera closer.");
                Model.Camera.Reset();
                Assert.That(Render(), Is.EqualTo(Initial), "Reset must restore the fitted view.");
            });
        }

        private static void AssertChanged(Color[] Before, Color[] After, string Message)
        {
            int Changed = Before.Zip(After, (BeforeColor, AfterColor) => BeforeColor != AfterColor ? 1 : 0).Sum();
            Assert.That(Changed, Is.GreaterThan(100), Message);
        }

        private static void WithModel(string Kind, bool Animated,
            Action<Animated3DModel, Func<Color[]>, Action<Vector3[]>> Test, bool ZeroDuration = false)
        {
            string ModelPath = Path.Combine(Path.GetTempPath(), $"culdcept-animation-{Guid.NewGuid():N}.gltf");
            try
            {
                WriteModel(ModelPath, Kind, Animated, ZeroDuration);
                using Form Window = new Form { ShowInTaskbar = false };
                using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = Window.Handle,
                        BackBufferWidth = 256,
                        BackBufferHeight = 256,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                using AlphaTestEffect Effect = new AlphaTestEffect(Device) { VertexColorEnabled = true };
                using RenderTarget2D Target = new RenderTarget2D(Device, 256, 256, false,
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                Animated3DModel Model = new Animated3DModel(Device, ModelPath);
                try
                {
                    Color[] Render()
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
                        Color[] Pixels = new Color[256 * 256];
                        Target.GetData(Pixels);
                        return Pixels;
                    }
                    void CheckGeometry(Vector3[] Positions)
                    {
                        Color[] Pixels = Render();
                        Viewport CameraViewport = new Viewport(0, 0, 256, 256);
                        Vector3[] Projected = Positions.Select(Position =>
                            CameraViewport.Project(Position, Effect.Projection, Effect.View, Effect.World)).ToArray();
                        Vector2 Expected = new Vector2(Projected.Average(Position => Position.X),
                            Projected.Average(Position => Position.Y));
                        int[] Occupied = Enumerable.Range(0, Pixels.Length)
                            .Where(PixelIndex => Pixels[PixelIndex] != Color.CornflowerBlue).ToArray();
                        Assert.That(Occupied.Length, Is.GreaterThan(100));
                        Vector2 Actual = new Vector2(Occupied.Average(PixelIndex => PixelIndex % 256 + 0.5f),
                            Occupied.Average(PixelIndex => PixelIndex / 256 + 0.5f));
                        Assert.That(Vector2.Distance(Expected, Actual), Is.LessThan(1f),
                            "Rendered triangle centroid must match the expected deformed positions.");
                    }
                    Test(Model, Render, CheckGeometry);
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
            }
        }

        private static void WriteModel(string ModelPath, string Kind, bool Animated, bool ZeroDuration)
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
            int Positions = Accessor(new float[] { -1, -1, 0, 1, -1, 0, 0, 1, 0 },
                "VEC3", 3, new float[] { -1, -1, 0 }, new float[] { 1, 1, 0 });
            Dictionary<string, int> Attributes = new Dictionary<string, int> { ["POSITION"] = Positions };
            Dictionary<string, object> Primitive = new Dictionary<string, object> { ["attributes"] = Attributes };
            Dictionary<string, object> Mesh = new Dictionary<string, object> { ["primitives"] = new[] { Primitive } };
            Dictionary<string, object> Node = new Dictionary<string, object> { ["mesh"] = 0 };
            Dictionary<string, object> Root = new Dictionary<string, object>
            {
                ["asset"] = new { version = "2.0" },
                ["scene"] = 0,
                ["scenes"] = new[] { new { nodes = Kind == "Skinned" ? new[] { 0, 1 } : new[] { 0 } } },
                ["meshes"] = new[] { Mesh }
            };
            if (Kind == "Skinned")
            {
                int Offset = checked((int)Bytes.Position);
                for (int ComponentIndex = 0; ComponentIndex < 12; ComponentIndex++)
                {
                    Writer.Write((ushort)0);
                }

                Views.Add(new { buffer = 0, byteOffset = Offset, byteLength = 24 });
                Accessors.Add(new { bufferView = Views.Count - 1, componentType = 5123, count = 3, type = "VEC4" });
                Attributes["JOINTS_0"] = Accessors.Count - 1;
                Attributes["WEIGHTS_0"] = Accessor(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }, "VEC4", 3);
                int InverseBind = Accessor(new float[]
                {
                    1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -0.3f, 0, 0, 1
                }, "MAT4", 1);
                Node["skin"] = 0;
                Root["skins"] = new[] { new { joints = new[] { 1 }, inverseBindMatrices = InverseBind } };
                Root["nodes"] = new object[] { Node, new { name = "Joint", translation = new float[] { 0.3f, 0, 0 } } };
            }
            else
            {
                Root["nodes"] = new[] { Node };
            }
            if (Kind == "Morph")
            {
                int Delta = Accessor(new float[] { 0.8f, 0, 0, 0.8f, 0, 0, 0.8f, 0, 0 },
                    "VEC3", 3, new float[] { 0.8f, 0, 0 }, new float[] { 0.8f, 0, 0 });
                Primitive["targets"] = new[] { new { POSITION = Delta } };
                Mesh["weights"] = new[] { 0.25f };
            }
            if (Animated)
            {
                int Times = Accessor(new float[] { 0, 1 }, "SCALAR", 2, new[] { 0f }, new[] { 1f });
                int Output = Kind == "Morph"
                    ? Accessor(new float[] { 0, 1 }, "SCALAR", 2)
                    : Accessor(Kind == "Skinned"
                        ? new float[] { 0.3f, 0, 0, 1.1f, 0, 0 }
                        : new float[] { 0, 0, 0, 0.8f, 0, 0 }, "VEC3", 2);
                int Scale = Accessor(new float[] { 1, 1, 1, 1.6f, 0.6f, 1 }, "VEC3", 2);
                object Clip(string Name, int Input, int Values, int TargetNode, string TargetPath)
                {
                    return new
                    {
                        name = Name,
                        samplers = new[] { new { input = Input, output = Values, interpolation = "LINEAR" } },
                        channels = new[] { new { sampler = 0, target = new { node = TargetNode, path = TargetPath } } }
                    };
                }

                List<object> Clips = new List<object>
                {
                    Clip("Duplicate", Times, Output, Kind == "Skinned" ? 1 : 0,
                        Kind == "Morph" ? "weights" : "translation"),
                    Clip("Duplicate", Times, Scale, Kind == "Skinned" ? 1 : 0, "scale")
                };
                if (ZeroDuration)
                {
                    int Instant = Accessor(new[] { 0f }, "SCALAR", 1, new[] { 0f }, new[] { 0f });
                    int Translation = Accessor(new float[] { 0.4f, 0, 0 }, "VEC3", 1);
                    Clips.Add(Clip("Instant", Instant, Translation, 0, "translation"));
                }
                Root["animations"] = Clips;
            }
            Root["buffers"] = new[] { new { byteLength = Bytes.Length, uri = "data:application/octet-stream;base64," + Convert.ToBase64String(Bytes.ToArray()) } };
            Root["bufferViews"] = Views;
            Root["accessors"] = Accessors;
            File.WriteAllText(ModelPath, JsonSerializer.Serialize(Root));
        }
    }
}
