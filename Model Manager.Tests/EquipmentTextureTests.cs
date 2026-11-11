using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.AssetManager.RST;
using NUnit.Framework;
using System.IO;
using System.Linq;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    public class EquipmentTextureTests
    {
        private static readonly string Data01 = Path.Combine(
            System.Environment.GetEnvironmentVariable("CULDCEPT_RSM_DIRECTORY") ?? @"D:\CuldceptSaga\RSP_unpacked",
            "DATA01");

        [Test]
        public void NamedNeighbourWinsOverTheNextSlotImage()
        {
            // The RST immediately after each of these heads belongs to a different piece.
            Expect("DATA01_0643.rsm", "pc_001_HA05_01.png", "DATA01_0631.rst");
            Expect("DATA01_0574.rsm", "pc_001_F01.png", "DATA01_0557.rst");
            Expect("DATA01_0574.rsm", "pc_001_H02_01.png", "DATA01_0567.rst");
            Expect("DATA01_0598.rsm", "pc_001_F01.png", "DATA01_0557.rst");
            Expect("DATA01_0598.rsm", "pc_001_H05_01.png", "DATA01_0591.rst");
            Expect("DATA01_0704.rsm", "pc_002_F01.png", "DATA01_0663.rst");
            Expect("DATA01_0704.rsm", "pc_002_H05_01.png", "DATA01_0697.rst");
        }

        private static void Expect(string ModelFile, string MaterialName, string TextureFile)
        {
            CuldceptModel Model = CuldceptModel.Load(Path.Combine(Data01, ModelFile));
            string MapKey = MaterialName.ToLowerInvariant();
            Assert.That(Model.TextureMap.ContainsKey(MapKey), Is.True, ModelFile + " " + MaterialName);
            byte[] Loaded = Model.TextureMap[MapKey][0];

            ushort NameKey = RstReader.RstNameKey(MaterialName);
            RstRecord Record = RstReader.ReadRst(File.ReadAllBytes(Path.Combine(Data01, TextureFile)))
                .First(Item => Item.NameKey == NameKey && Item.ArgbPixels != null);
            byte[] Expected = RstReader.ConvertArgbToPng(Record.Width, Record.Height, Record.ArgbPixels);

            Assert.That(Loaded, Is.EqualTo(Expected), ModelFile + " " + MaterialName);
        }
    }
}
