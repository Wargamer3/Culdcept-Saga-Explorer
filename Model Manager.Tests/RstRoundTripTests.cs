using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.AssetManager.RST;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    public class RstRoundTripTests
    {
        [Test]
        public void DimensionHashMatchesKnownTextures()
        {
            Assert.That(RstReader.RstDimensionHash("pc_001_H01_01.png", 256, 256), Is.EqualTo(0xEE4062BDu));
            Assert.That(RstReader.RstNameKey("pc_001_H01_01.png"), Is.EqualTo(0xFF8A));
            Assert.That(RstReader.RstSecondNameKey("pc_001_H01_01.png"), Is.EqualTo(0x50EB));

            Assert.That(RstReader.RstDimensionHash("npc_032_01.png", 512, 512), Is.EqualTo(0xE25856D5u));
            Assert.That(RstReader.RstNameKey("npc_032_01.png"), Is.EqualTo(0xF35E));
            Assert.That(RstReader.RstSecondNameKey("npc_032_01.png"), Is.EqualTo(0x44BF));
            Assert.That(RstReader.RstDimensionHash(@"a\npc_032_03.png", 256, 256), Is.EqualTo(0xEB565FD3u));
        }

        [Test]
        public void WriterRebuildsChecksumsFlagsAndWideCount()
        {
            List<RstRecord> Records = new List<RstRecord>();
            for (int RecordIndex = 0; RecordIndex < 300; RecordIndex++)
            {
                bool Translucent = RecordIndex == 1;
                Records.Add(new RstRecord
                {
                    Name = $"tex_{RecordIndex:000}.png",
                    Width = 1,
                    Height = 1,
                    ArgbPixels = Translucent
                        ? new byte[] { 128, 1, 2, 3 }
                        : new byte[] { 255, 4, 5, 6 }
                });
            }
            Records.Add(new RstRecord { Name = "missing.png", Width = 0, Height = 0, ArgbPixels = null });

            byte[] Written = RstWriter.WriteRst(Records, IsBigEndian: true);
            List<RstRecord> Read = RstReader.ReadRst(Written);

            Assert.That(Read, Has.Count.EqualTo(301));
            Assert.That(Written[8], Is.EqualTo(0x01));
            Assert.That(Written[9], Is.EqualTo(0x2D));
            Assert.That(Read[0].HeaderWord, Is.EqualTo(RstReader.RstDimensionHash("tex_000.png", 1, 1)));
            Assert.That(Read[0].NameKey, Is.EqualTo(RstReader.RstNameKey("tex_000.png")));
            Assert.That(Read[0].NameKey2, Is.EqualTo(RstReader.RstSecondNameKey("tex_000.png")));
            Assert.That(Read[0].Flags, Is.EqualTo(0u));
            Assert.That(Read[1].Flags, Is.EqualTo(1u));
            Assert.That(Read[300].Flags, Is.EqualTo(4u));
            Assert.That(Read[300].HeaderWord, Is.EqualTo(RstReader.RstDimensionHash("missing.png", 0, 0)));
            Assert.That(Read[0].ArgbPixels, Is.EqualTo(new byte[] { 255, 4, 5, 6 }));
        }

        [Test]
        public void HeaderCountsPlaceholderRecords()
        {
            byte[] Written = RstWriter.WriteRst(new List<RstRecord>
            {
                new RstRecord { Name = "a.ifl", Width = 0, Height = 0, ArgbPixels = null },
                new RstRecord
                {
                    Name = "b.png",
                    Width = 1,
                    Height = 1,
                    ArgbPixels = new byte[] { 255, 0, 0, 0 }
                },
                new RstRecord { Name = "c.ifl", Width = 0, Height = 0, ArgbPixels = null }
            });

            Assert.That(Written[10], Is.EqualTo(0x00));
            Assert.That(Written[11], Is.EqualTo(0x02));
        }

        [Test]
        public void StoredAlphaFlagSurvivesAnOpaqueImage()
        {
            List<RstRecord> Records = new List<RstRecord>
            {
                new RstRecord
                {
                    Width = 1,
                    Height = 1,
                    ArgbPixels = new byte[] { 255, 1, 2, 3 },
                    Flags = 1,
                    HeaderWord = 0x11111111,
                    NameKey = 2,
                    NameKey2 = 3,
                    Tail = 4
                }
            };

            RstRecord Read = RstReader.ReadRst(RstWriter.WriteRst(Records, IsBigEndian: true))[0];
            Assert.That(Read.Flags, Is.EqualTo(1u));
            Assert.That(Read.Tail, Is.EqualTo(4));
            Assert.That(Read.HeaderWord, Is.EqualTo(0x11111111u));
        }

        [Test]
        public void PlaceholderChecksumUsesTheMaterialSize()
        {
            byte[] Written = RstWriter.WriteRst(new List<RstRecord>
            {
                new RstRecord
                {
                    Name = "map_battle_04_025.ifl",
                    Width = 0,
                    Height = 0,
                    ArgbPixels = null,
                    HasChecksumSize = true,
                    ChecksumWidth = 256,
                    ChecksumHeight = 128
                }
            });

            RstRecord Record = RstReader.ReadRst(Written)[0];
            Assert.That(Record.HeaderWord, Is.EqualTo(0x19D78E54u));
            Assert.That(Record.Width, Is.EqualTo(0));
            Assert.That(Record.Height, Is.EqualTo(0));
            Assert.That(Record.Flags, Is.EqualTo(4u));
        }

        [Test]
        public void AnimatedFileKeepsItsPlaylistFooter()
        {
            string RstPath = Path.Combine(CorpusRoot(), "DATA02", "DATA02_0011.rst");
            if (!File.Exists(RstPath))
            {
                Assert.Ignore("DATA02_0011.rst is not in the corpus.");
            }

            byte[] Original = File.ReadAllBytes(RstPath);
            List<RstRecord> Records = RstReader.ReadRst(Original, out byte[] Footer);
            Assert.That(Footer.Length, Is.EqualTo(408));
            Assert.That(Footer.Length % 12, Is.EqualTo(0));
            Assert.That(RstWriter.WriteRst(Records, IsBigEndian: true, Footer: Footer), Is.EqualTo(Original));
        }

        [Test]
        public void ReadWriteIsLosslessForNajaran()
        {
            string RstPath = Path.Combine(CorpusRoot(), "DATA00", "DATA00_0413.rst");
            string RsmPath = Path.Combine(CorpusRoot(), "DATA00", "DATA00_0412.rsm");
            if (!File.Exists(RstPath) || !File.Exists(RsmPath))
            {
                Assert.Ignore("DATA00_0412 / DATA00_0413 are not in the corpus.");
            }

            byte[] Original = File.ReadAllBytes(RstPath);
            List<RstRecord> Records = RstReader.ReadRst(Original);
            Assert.That(RstWriter.WriteRst(Records, IsBigEndian: true), Is.EqualTo(Original));

            RsmDocument Model;
            using (FileStream RsmStream = File.OpenRead(RsmPath))
            using (BigEndianBinaryReader Reader = new BigEndianBinaryReader(RsmStream))
            {
                Model = RSMReader.ParseRsm(Reader);
            }

            Assert.That(Records, Has.Count.EqualTo(Model.ListMaterial.Count));
            for (int RecordIndex = 0; RecordIndex < Records.Count; RecordIndex++)
            {
                RstRecord Record = Records[RecordIndex];
                Record.Name = Model.ListMaterial[RecordIndex].Name;
                Record.HeaderWord = 0;
                Record.NameKey = 0;
                Record.NameKey2 = 0;
                Record.Flags = 0;
                Records[RecordIndex] = Record;
            }

            byte[] Rebuilt = RstWriter.WriteRst(Records, IsBigEndian: true);
            Assert.That(Rebuilt, Is.EqualTo(Original));
        }

        [Test]
        public void ReadWriteIsLosslessAcrossTheCorpus()
        {
            if (Environment.GetEnvironmentVariable("CULDCEPT_RST_CORPUS") != "1")
            {
                Assert.Ignore("Set CULDCEPT_RST_CORPUS=1 to rewrite every RST. It reads about 1.3 GB.");
            }

            string Root = CorpusRoot();
            if (!Directory.Exists(Root))
            {
                Assert.Ignore("RST corpus is not available.");
            }

            int RoundTripped = 0;
            List<string> Skipped = new List<string>();
            foreach (string RstPath in Directory.EnumerateFiles(Root, "*.rst", SearchOption.AllDirectories))
            {
                byte[] Original = File.ReadAllBytes(RstPath);
                List<RstRecord> Records = RstReader.ReadRst(Original, out byte[] Footer);
                if (Records.Exists(Record => Record.ArgbPixels == null && Record.Flags != 4))
                {
                    Skipped.Add(Path.GetFileName(RstPath));
                    continue;
                }

                bool IsBigEndian = Original.Length >= 3 && Original[0] == (byte)'r';
                byte[] Written = RstWriter.WriteRst(Records, IsBigEndian, Footer);
                Assert.That(FirstMismatch(Written, Original), Is.EqualTo(-1), Path.GetFileName(RstPath));
                RoundTripped++;
            }

            Assert.That(RoundTripped, Is.GreaterThan(1000));
            Assert.That(Skipped, Is.EquivalentTo(new[] { "DATA00_0007.rst", "DATA00_0009.rst", "DATA00_0225.rst" }));
        }

        private static int FirstMismatch(byte[] Written, byte[] Original)
        {
            if (Written.Length != Original.Length)
            {
                return Math.Min(Written.Length, Original.Length);
            }

            for (int ByteIndex = 0; ByteIndex < Written.Length; ByteIndex++)
            {
                if (Written[ByteIndex] != Original[ByteIndex])
                {
                    return ByteIndex;
                }
            }

            return -1;
        }

        private static string CorpusRoot()
        {
            return Environment.GetEnvironmentVariable("CULDCEPT_RSM_DIRECTORY")
                ?? @"D:\CuldceptSaga\RSP_unpacked";
        }
    }
}
