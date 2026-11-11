using System;
using System.IO;
using System.Collections.Generic;
using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.AssetManager.RST;
using CuldceptSagaExplorer.AssetManager.ExternalModel;
using SharpGLTF.Schema2;

namespace CuldceptSagaExplorer.AssetManager
{
    public class RsmExtractor
    {
        public static void ReplaceModelGltf(string RSMToReplacePath, string RSTToReplacePath, string ReplacementGltfPath)
        {
            CuldceptModel ReplacementRSM = GltfImporter.ImportFromGltf(ReplacementGltfPath, UndoReflectZ: true);

            // 1. Write .RSM
            using (FileStream DestinationStream = new FileStream(RSMToReplacePath, FileMode.Create, FileAccess.Write))
            using (BigEndianBinaryWriter DestinationWriter = new BigEndianBinaryWriter(DestinationStream))
            {
                RSMWriter.WriteRsm(DestinationWriter, ReplacementRSM.Document);
            }

            // 2. Write .RST
            if (ReplacementRSM.TextureMap == null || ReplacementRSM.TextureMap.Count == 0)
            {
                return;
            }

            byte[] RstBytes = BuildReplacementRst(ReplacementRSM.Document.ListMaterial, ReplacementRSM.TextureMap, RSTToReplacePath, UseFullName: true);

            File.WriteAllBytes(RSTToReplacePath, RstBytes);
        }

        private static byte[] BuildReplacementRst(List<RsmMaterial> ListMaterial, Dictionary<string, List<byte[]>> DicTexturesByMaterialName, string ExistingRstPath, bool UseFullName)
        {
            List<RstRecord> ListPreviousTexture = null;
            byte[] Footer = null;

            if (!string.IsNullOrEmpty(ExistingRstPath) && File.Exists(ExistingRstPath))
            {
                ListPreviousTexture = RstReader.ReadRst(File.ReadAllBytes(ExistingRstPath), out Footer);
            }

            List<RstRecord> ListRST;

            if (ListPreviousTexture != null && ListPreviousTexture.Count > 0)
            {
                ListRST = RebuildFromPrevious(ListMaterial, DicTexturesByMaterialName, ListPreviousTexture, UseFullName);
            }
            else
            {
                ListRST = BuildFromNames(ListMaterial, DicTexturesByMaterialName, UseFullName);
            }

            return RstWriter.WriteRst(ListRST, IsBigEndian: true, Footer: Footer);
        }

        /// <summary>
        /// Keep each original record's checksum. Animated lists store a placeholder (hashed with the
        /// material size) and then frames that have their own names; those names are not in the RSM.
        /// A later list that reuses those frames stores only its own placeholder.
        /// </summary>
        private static List<RstRecord> RebuildFromPrevious(List<RsmMaterial> ListMaterial, Dictionary<string, List<byte[]>> DicTexturesByMaterialName, List<RstRecord> ListPreviousTexture, bool UseFullName)
        {
            List<RstRecord> ListTexture = new List<RstRecord>();
            int TextureIndex = 0;
            int FrameBudget = Math.Max(ListPreviousTexture.Count - ListMaterial.Count, 0);

            foreach (RsmMaterial ActiveMaterial in ListMaterial)
            {
                string MaterialName = GetCleanMaterialName(ActiveMaterial, UseFullName);
                bool IsList = MaterialName.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase);
                List<byte[]> PngFrames = TexturesFor(ActiveMaterial, DicTexturesByMaterialName, UseFullName);

                if (!IsList)
                {
                    if (TextureIndex < ListPreviousTexture.Count)
                    {
                        ListTexture.Add(ReplacePixels(ListPreviousTexture[TextureIndex], PngFrames, 0, MaterialName));
                    }
                    else
                    {
                        ListTexture.AddRange(Synthesize(ActiveMaterial, PngFrames, MaterialName));
                    }

                    TextureIndex++;
                    continue;
                }

                int PlaceholderIndex = TextureIndex;
                int FrameEnd = TextureIndex + 1;
                while (FrameEnd < ListPreviousTexture.Count && ListPreviousTexture[FrameEnd].ArgbPixels != null && FrameBudget > 0)
                {
                    FrameEnd++;
                    FrameBudget--;
                }

                if (PlaceholderIndex < ListPreviousTexture.Count)
                {
                    ListTexture.Add(ListPreviousTexture[PlaceholderIndex]);
                }

                if (FrameEnd > TextureIndex + 1)
                {
                    int FrameIndex = 0;
                    for (int RecordIndex = PlaceholderIndex + 1; RecordIndex < FrameEnd; RecordIndex++)
                    {
                        ListTexture.Add(ReplacePixels(ListPreviousTexture[RecordIndex], PngFrames, FrameIndex++, MaterialName));
                    }
                }

                TextureIndex = Math.Min(FrameEnd, ListPreviousTexture.Count);
            }

            while (TextureIndex < ListPreviousTexture.Count)
            {
                ListTexture.Add(ListPreviousTexture[TextureIndex++]);
            }

            return ListTexture;
        }

        private static RstRecord ReplacePixels(RstRecord SourceRecord, List<byte[]> ListTexture, int FrameIndex, string MaterialName)
        {
            if (SourceRecord.ArgbPixels == null || ListTexture == null || FrameIndex < 0 || FrameIndex >= ListTexture.Count)
            {
                return SourceRecord;
            }

            byte[] ArgbPixels = RstWriter.ConvertPngToArgb(ListTexture[FrameIndex], out ushort Width, out ushort Height);

            if (ArgbPixels == null)
            {
                return SourceRecord;
            }

            bool WasTranslucent = HasTranslucentPixel(SourceRecord.ArgbPixels);
            bool NowTranslucent = HasTranslucentPixel(ArgbPixels);
            bool SameSize = Width == SourceRecord.Width && Height == SourceRecord.Height;
            bool SameName = SourceRecord.NameKey == RstReader.RstNameKey(MaterialName);
            SourceRecord.Width = Width;
            SourceRecord.Height = Height;
            SourceRecord.ArgbPixels = ArgbPixels;
            if (WasTranslucent != NowTranslucent)
            {
                SourceRecord.Flags = NowTranslucent ? 1u : 0u;
            }

            if (!SameName)
            {
                // This slot still carries the previous texture's name and size checksum.
                // The game trusts that checksum, so a 32×32 image labeled as 512×512
                // runs off the end of the buffer when the character is opened.
                SourceRecord.Name = MaterialName;
                SourceRecord.UseStoredNameKey2 = false;
                SourceRecord.HasChecksumSize = false;
                SourceRecord.Tail = 0;
            }
            else if (!SameSize)
            {
                SourceRecord.Name = MaterialName;
                SourceRecord.UseStoredNameKey2 = true;
            }
            return SourceRecord;
        }

        private static List<RstRecord> BuildFromNames(List<RsmMaterial> ListMaterial, Dictionary<string, List<byte[]>> DicTextureByName, bool UseFullName)
        {
            List<RstRecord> ListRST = new List<RstRecord>();

            foreach (RsmMaterial ActiveMaterial in ListMaterial)
            {
                ListRST.AddRange(Synthesize(ActiveMaterial, TexturesFor(ActiveMaterial, DicTextureByName, UseFullName), GetCleanMaterialName(ActiveMaterial, UseFullName)));
            }

            return ListRST;
        }

        private static List<RstRecord> Synthesize(RsmMaterial Material, List<byte[]> ListTexture, string MaterialName)
        {
            List<RstRecord> ListRST = new List<RstRecord>();
            bool IsList = MaterialName.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase);
            if (IsList)
            {
                ListRST.Add(CreatePlaceholderTexture(Material, MaterialName));
            }

            if (ListTexture != null && ListTexture.Count > 0)
            {
                foreach (byte[] PngData in ListTexture)
                {
                    byte[] ArgbPixels = RstWriter.ConvertPngToArgb(PngData, out ushort Width, out ushort Height);
                    ListRST.Add(new RstRecord
                    {
                        Name = MaterialName,
                        Width = Width,
                        Height = Height,
                        ArgbPixels = ArgbPixels
                    });
                }
            }
            else if (!IsList)
            {
                ListRST.Add(CreatePlaceholderTexture(Material, MaterialName));
            }

            return ListRST;
        }

        private static RstRecord CreatePlaceholderTexture(RsmMaterial Material, string MaterialName)
        {
            return new RstRecord
            {
                Name = MaterialName,
                Width = 0,
                Height = 0,
                ArgbPixels = null,
                HasChecksumSize = true,
                ChecksumWidth = Material.TextureWidth,
                ChecksumHeight = Material.TextureHeight
            };
        }

        private static bool HasTranslucentPixel(byte[] ArgbPixels)
        {
            if (ArgbPixels == null)
            {
                return false;
            }

            for (int PixelOffset = 0; PixelOffset < ArgbPixels.Length; PixelOffset += 4)
            {
                if (ArgbPixels[PixelOffset] != 255)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetCleanMaterialName(RsmMaterial MaterialToClean, bool UseFullName)
        {
            string OriginalMaterialName = MaterialToClean.Name ?? "";

            return UseFullName ? OriginalMaterialName : Path.GetFileName(OriginalMaterialName);
        }

        private static List<byte[]> TexturesFor(RsmMaterial Material, Dictionary<string, List<byte[]>> DicTexturesByMaterialName, bool UseFullName)
        {
            if (DicTexturesByMaterialName == null || Material == null)
            {
                return null;
            }

            string FullName = Material.Name ?? "";
            string CleanName = Path.GetFileName(FullName);
            if (UseFullName && DicTexturesByMaterialName.TryGetValue(FullName.ToLowerInvariant(), out List<byte[]> ListTexture))
            {
                return ListTexture;
            }
            if (DicTexturesByMaterialName.TryGetValue(CleanName.ToLowerInvariant(), out ListTexture))
            {
                return ListTexture;
            }
            return null;
        }

        public static void ExtractAll(string ExtractionDirectory, string ExportDirectory, Action<string> Progress = null)
        {
            foreach (string ActiveSubFolder in Directory.GetDirectories(ExtractionDirectory))
            {
                string ActiveSubFolderName = Path.GetFileNameWithoutExtension(ActiveSubFolder);

                foreach (string ActiveFilePath in Directory.GetFiles(ActiveSubFolder, "*.rsm"))
                {
                    if (Progress != null)
                    {
                        Progress.Invoke($"Converting model {ActiveFilePath}");
                    }
                    ExtractRsmToGlb(ActiveFilePath, Path.Combine(ExportDirectory, ActiveSubFolderName, Path.GetFileNameWithoutExtension(ActiveFilePath) + ".glb"));
                }
            }
        }

        public static ModelRoot ConvertRsmToGlb(string RsmFilePath)
        {
            CuldceptModel LoadedPackage = CuldceptModel.Load(RsmFilePath);

            return GltfExporter.CreateGlbFromRSM(LoadedPackage.Document, LoadedPackage.TextureMap, LoadedPackage.Animations);
        }

        public static void ExtractRsmToGlb(string RsmFilePath, string ExportPath)
        {
            if (!File.Exists(RsmFilePath))
            {
                Console.WriteLine($"RSM file not found: {RsmFilePath}");
                return;
            }

            ModelRoot GLTFModel = ConvertRsmToGlb(RsmFilePath);

            string ExportFolder = Path.GetDirectoryName(ExportPath);
            if (!string.IsNullOrEmpty(ExportFolder))
            {
                Directory.CreateDirectory(ExportFolder);
            }

            CuldceptSagaExplorer.ModelManager.GltfExportMetadata.SaveGlb(GLTFModel, ExportPath);
        }
    }
}
