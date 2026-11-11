using CuldceptSagaExplorer.GraphicsManager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using System.Threading;
using System.Windows.Forms;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [Category("GammaRamp")]
    [Apartment(ApartmentState.STA)]
    public class GammaRampTests
    {
        [Test]
        public void CuldceptSagaRampMapsEachChannelThroughTheCapturedTable()
        {
            using Form Window = new Form { ShowInTaskbar = false };
            using GraphicsDevice Device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                new PresentationParameters
                {
                    DeviceWindowHandle = Window.Handle,
                    BackBufferWidth = 4,
                    BackBufferHeight = 1,
                    PresentationInterval = PresentInterval.Immediate
                });
            using Texture2D Source = new Texture2D(Device, 4, 1);
            Source.SetData(new[] { new Color(0, 0, 0), new Color(64, 128, 192), new Color(255, 255, 255), new Color(16, 32, 96) });
            using RenderTarget2D Target = new RenderTarget2D(Device, 4, 1);
            using DisplayGammaRamp Ramp = new DisplayGammaRamp(Device, DisplayGammaRamp.CuldceptSaga);

            Device.SetRenderTarget(Target);
            Ramp.Draw(Source);
            Device.SetRenderTarget(null);

            Color[] Pixels = new Color[4];
            Target.GetData(Pixels);
            static byte Expected(int Value)
            {
                return (byte)System.Math.Round(DisplayGammaRamp.CuldceptSaga[Value] * 255.0 / 1023.0);
            }

            Assert.That(Pixels[0], Is.EqualTo(new Color(0, 0, 0)));
            Assert.That(Pixels[1], Is.EqualTo(new Color(Expected(64), Expected(128), Expected(192))));
            Assert.That(Pixels[2], Is.EqualTo(new Color(255, 255, 255)));
            Assert.That(Pixels[3], Is.EqualTo(new Color(Expected(16), Expected(32), Expected(96))));
            Assert.That(Expected(128), Is.EqualTo(115), "Mid-grey darkens like the capture (128 -> 115).");
        }
    }
}
