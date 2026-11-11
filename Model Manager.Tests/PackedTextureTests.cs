using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.AssetManager.RST;
using NUnit.Framework;
using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    public class PackedTextureTests
    {
        private static readonly string Unpacked = Directory.Exists(@"E:\Culdcept\RSP_unpacked")
            ? @"E:\Culdcept\RSP_unpacked"
            : Environment.GetEnvironmentVariable("CULDCEPT_RSM_DIRECTORY") ?? @"D:\CuldceptSaga\RSP_unpacked";

        [Test]
        public void SkeletonRtdBecomesTheBoneAtlas()
        {
            string ModelPath = Path.Combine(Unpacked, "DATA04", "DATA04_0018.rsm");
            if (!File.Exists(ModelPath))
            {
                Assert.Ignore("Unpacked creature models are not on this machine.");
            }

            CuldceptModel Model = CuldceptModel.Load(ModelPath, WithAnimations: false);
            byte[] Png = Model.TextureMap["cr005_skeleton.png"][0];
            AssertPng(Png, 512, 512);

            string Reference = @"E:\Culdcept\DAE\DATA06\cr005_skeleton.png";
            if (!File.Exists(Reference))
            {
                return;
            }

            using Bitmap Decoded = new Bitmap(new MemoryStream(Png));
            using Bitmap Expected = new Bitmap(Reference);
            Assert.That(Decoded.Width, Is.EqualTo(Expected.Width));
            Assert.That(Decoded.Height, Is.EqualTo(Expected.Height));
            long Difference = 0;
            long Flipped = 0;
            int Samples = 0;
            for (int Y = 0; Y < Decoded.Height; Y += 8)
            {
                for (int X = 0; X < Decoded.Width; X += 8)
                {
                    Color Left = Decoded.GetPixel(X, Y);
                    Color Right = Expected.GetPixel(X, Y);
                    Color Other = Expected.GetPixel(X, Decoded.Height - 1 - Y);
                    Difference += Math.Abs(Left.R - Right.R) + Math.Abs(Left.G - Right.G) + Math.Abs(Left.B - Right.B);
                    Flipped += Math.Abs(Left.R - Other.R) + Math.Abs(Left.G - Other.G) + Math.Abs(Left.B - Other.B);
                    Samples++;
                }
            }

            Assert.That(Difference / Samples, Is.LessThan(24), "decoded DDS drifted from the exported atlas");
            Assert.That(Flipped / Samples, Is.GreaterThan(Difference / Samples));
        }

        [Test]
        public void GiantRatRtdIsEmbedded()
        {
            string ModelPath = Path.Combine(Unpacked, "DATA04", "DATA04_0002.rsm");
            if (!File.Exists(ModelPath))
            {
                Assert.Ignore("Unpacked creature models are not on this machine.");
            }

            CuldceptModel Model = CuldceptModel.Load(ModelPath, WithAnimations: false);
            AssertPng(Model.TextureMap["cr001_giantrat.png"][0], 512, 512);
        }

        [Test]
        public void RtjJpegBecomesPng()
        {
            string TexturePath = Path.Combine(Unpacked, "DATA00", "DATA00_0026.rtj");
            if (!File.Exists(TexturePath))
            {
                Assert.Ignore("Unpacked RTJ textures are not on this machine.");
            }

            byte[] Png = RtdReader.ConvertToPng(File.ReadAllBytes(TexturePath));
            Assert.That(Png, Is.Not.Null);
            Assert.That(Png.Take(8), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        }

        private static void AssertPng(byte[] Png, int Width, int Height)
        {
            Assert.That(Png, Is.Not.Null);
            Assert.That(Png.Take(8), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            Assert.That(ReadInt32(Png, 16), Is.EqualTo(Width));
            Assert.That(ReadInt32(Png, 20), Is.EqualTo(Height));
        }

        private static int ReadInt32(byte[] Source, int Offset)
        {
            return (Source[Offset] << 24) | (Source[Offset + 1] << 16) | (Source[Offset + 2] << 8) | Source[Offset + 3];
        }
    }
}
