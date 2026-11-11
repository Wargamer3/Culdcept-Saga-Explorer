using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CuldceptSagaExplorer.AssetManager.RSA;
using CuldceptSagaExplorer.AssetManager.RST;

namespace CuldceptSagaExplorer.AssetManager.RSM
{
    public class CuldceptModel
    {
        private static readonly Regex SlotRegex = new Regex(@"^(.*_)(\d+)$", RegexOptions.Compiled);
        private static readonly string[] Extensions = { ".RST", ".rst", ".RTD", ".rtd", ".RTJ", ".rtj" };

        #region Constants

        private const int RstNameSearchSlots = 64;

        #endregion

        public RsmDocument Document;

        public Dictionary<string, List<byte[]>> TextureMap = new Dictionary<string, List<byte[]>>(StringComparer.OrdinalIgnoreCase);

        public List<RsaClip> Animations = new List<RsaClip>();

        public static CuldceptModel Load(string RsmFilePath, bool WithAnimations = true, bool WithTextures = true)
        {
            CuldceptModel LoadedPackage = new CuldceptModel();

            using (FileStream SourceStream = new FileStream(RsmFilePath, FileMode.Open, FileAccess.Read))
            using (BigEndianBinaryReader SourceReader = new BigEndianBinaryReader(SourceStream))
            {
                LoadedPackage.Document = RSMReader.ParseRsm(SourceReader);
            }

            if (WithTextures)
            {
                string TexturePath = GetRSTTexturePath(RsmFilePath);
                LoadedPackage.TextureMap = LoadTextures(RsmFilePath, TexturePath, LoadedPackage.Document.ListMaterial);
            }
            if (WithAnimations)
            {
                LoadedPackage.Animations = LoadClips(RsmFilePath, LoadedPackage.Document);
            }

            return LoadedPackage;
        }

        private static (string Prefix, int Number, int Width)? SplitSlotName(string Stem)
        {
            Match SlotMatch = Regex.Match(Stem, @"^(.*_)(\d+)$");
            if (!SlotMatch.Success)
            {
                return null;
            }
            return (SlotMatch.Groups[1].Value, int.Parse(SlotMatch.Groups[2].Value), SlotMatch.Groups[2].Value.Length);
        }

        private static string SlotPath(string Folder, string Prefix, int Number, int Width, string Extension)
        {
            return Path.Combine(Folder, $"{Prefix}{Number.ToString($"D{Width}")}.{Extension.TrimStart('.')}");
        }

        public static string GetRSTTexturePath(string RSMPath)
        {
            if (string.IsNullOrEmpty(RSMPath))
            {
                return null;
            }

            string RSMFolder = Path.GetDirectoryName(RSMPath);
            string RSMFileName = Path.GetFileNameWithoutExtension(RSMPath);

            foreach (string Extension in Extensions)
            {
                string CandidatePath = Path.Combine(RSMFolder, RSMFileName + Extension);
                if (File.Exists(CandidatePath))
                {
                    return CandidatePath;
                }
            }

            Match SlotMatch = SlotRegex.Match(RSMFileName);
            if (!SlotMatch.Success)
            {
                return null;
            }

            string FolderName = SlotMatch.Groups[1].Value;
            string NumberText = SlotMatch.Groups[2].Value;

            int Number = int.Parse(NumberText);
            int NumberWidth = NumberText.Length;

            for (int NextSlotNumber = Number + 1; NextSlotNumber < Number + 12; NextSlotNumber++)
            {
                string RsmLower = SlotPath(RSMFolder, FolderName, NextSlotNumber, NumberWidth, "rsm");
                if (File.Exists(RsmLower))
                {
                    return null;
                }

                foreach (string Extension in Extensions)
                {
                    string CandidatePath = SlotPath(RSMFolder, FolderName, NextSlotNumber, NumberWidth, Extension);
                    if (File.Exists(CandidatePath))
                    {
                        return CandidatePath;
                    }
                }
            }

            return null;
        }

        internal static Dictionary<string, List<byte[]>> LoadTextures(string RsmPath, string TexturePath, List<RsmMaterial> ListMaterial,
            bool RequireValidRst = false)
        {
            Dictionary<string, List<byte[]>> TextureMap = new Dictionary<string, List<byte[]>>();
            if (ListMaterial == null || ListMaterial.Count == 0)
            {
                return TextureMap;
            }

            List<string> UniqueNames = ListMaterial
                .Select(Material => Material.Name)
                .Where(MaterialName => !string.IsNullOrEmpty(MaterialName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            byte[] FileData = !string.IsNullOrEmpty(TexturePath) && File.Exists(TexturePath)
                ? File.ReadAllBytes(TexturePath) : null;

            if (RequireValidRst && (FileData == null || FileData.Length < 12
                || !(FileData[0] == 'r' && FileData[1] == 's' && FileData[2] == 't' && FileData[3] == 0
                    || FileData[0] == 'R' && FileData[1] == 'S' && FileData[2] == 'T' && FileData[3] == 0)))
            {
                throw new InvalidDataException($"{TexturePath}: invalid or truncated RST dependency header.");
            }

            // RTD is a DDS and RTJ is a JPEG, both after a 0x30 header. Both magics start with R,
            // so they have to be decoded before the RST record walk.
            byte[] PackedPng = RtdReader.ConvertToPng(FileData);
            if (PackedPng != null)
            {
                foreach (string MaterialName in UniqueNames)
                {
                    TextureMap[MaterialName.ToLower()] = new List<byte[]> { PackedPng };
                }

                return TextureMap;
            }

            Dictionary<string, byte[]> FileCache = new Dictionary<string, byte[]>();
            if (!string.IsNullOrEmpty(TexturePath) && FileData != null)
            {
                FileCache[TexturePath] = FileData;
            }

            // Next-slot record whose name key is not this material. DATA01 keeps each
            // equipment texture in its own RST, so that slot is often a different piece.
            // It is used only when no neighbouring file contains the material name.
            Dictionary<string, byte[]> FallbackMap = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            if (FileData != null)
            {
                List<RstRecord> ListRecord = RstReader.ReadRst(FileData);   // your existing ParseRst → List<RstRecord>
                if (RequireValidRst)
                {
                    int ExpectedRecords = FileData[0] == 'r'
                        ? BinaryPrimitives.ReadUInt16BigEndian(FileData.AsSpan(8, 2))
                        : BinaryPrimitives.ReadUInt16LittleEndian(FileData.AsSpan(8, 2));
                    if (ListRecord.Count != ExpectedRecords || ListRecord.Any(Record =>
                        Record.ArgbPixels == null && (Record.Flags & RstReader.RstPlaceholderFlag) == 0))
                    {
                        throw new InvalidDataException($"{TexturePath}: truncated or unsupported RST dependency records.");
                    }
                }
                List<ushort> ListNameKey = ListRecord.Select(Record => Record.NameKey).ToList();
                HashSet<ushort> ModelKeys = UniqueNames.Select(RstReader.RstNameKey).ToHashSet();

                // ----- AssignTextureRecords (Python logic) -----
                Dictionary<string, List<int>> Assignment = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                int RecordPosition = 0;
                int FrameBudget = Math.Max(ListRecord.Count - UniqueNames.Count, 0);
                List<int> SharedFrames = null;

                foreach (string MaterialName in UniqueNames)
                {
                    if (MaterialName.ToLower().EndsWith(".ifl"))
                    {
                        int FrameEnd = RecordPosition + 1;
                        while (FrameEnd < ListRecord.Count &&
                               ListRecord[FrameEnd].ArgbPixels != null &&
                               FrameBudget > 0)
                        {
                            FrameEnd++;
                            FrameBudget--;
                        }
                        if (FrameEnd > RecordPosition + 1)
                        {
                            SharedFrames = Enumerable.Range(RecordPosition + 1, FrameEnd - RecordPosition - 1).ToList();
                        }

                        // list without its own frames reuses the previous list
                        Assignment[MaterialName] = SharedFrames != null
                            ? new List<int>(SharedFrames)
                            : new List<int> { RecordPosition + 1 }; // will be filtered later if invalid
                        RecordPosition = FrameEnd;
                    }
                    else
                    {
                        Assignment[MaterialName] = new List<int> { RecordPosition };
                        RecordPosition++;
                    }
                }

                // Convert assigned record indices → PNG lists
                foreach (KeyValuePair<string, List<int>> AssignmentPair in Assignment)
                {
                    string MaterialName = AssignmentPair.Key;
                    List<int> RecordIndices = AssignmentPair.Value;
                    bool IsList = MaterialName.ToLower().EndsWith(".ifl");

                    if (!IsList && RecordIndices.Count > 0 && RecordIndices[0] < ListNameKey.Count
                        && ListNameKey[RecordIndices[0]] != RstReader.RstNameKey(MaterialName))
                    {
                        // Name-order mismatch. Hold the image until the name search has had a turn.
                        if (!ModelKeys.Contains(ListNameKey[RecordIndices[0]])
                            && ListRecord[RecordIndices[0]].ArgbPixels != null)
                        {
                            byte[] PngBytes = RstReader.ConvertArgbToPng(
                                ListRecord[RecordIndices[0]].Width,
                                ListRecord[RecordIndices[0]].Height,
                                ListRecord[RecordIndices[0]].ArgbPixels);
                            if (PngBytes != null)
                            {
                                FallbackMap[MaterialName.ToLower()] = PngBytes;
                            }
                        }
                        continue;
                    }

                    List<byte[]> Frames = new List<byte[]>();
                    foreach (int RecordIndex in RecordIndices)
                    {
                        if (RecordIndex < 0 || RecordIndex >= ListRecord.Count || ListRecord[RecordIndex].ArgbPixels == null)
                        {
                            continue;
                        }
                        byte[] PngBytes = RstReader.ConvertArgbToPng(
                            ListRecord[RecordIndex].Width, ListRecord[RecordIndex].Height, ListRecord[RecordIndex].ArgbPixels);
                        if (PngBytes != null)
                        {
                            Frames.Add(PngBytes);
                        }
                    }
                    if (Frames.Count > 0)
                    {
                        TextureMap[MaterialName.ToLower()] = Frames;
                    }
                }
            }

            // A matching name in a neighbouring RST wins over the next-slot image.
            foreach (string MaterialName in UniqueNames)
            {
                string MaterialKey = MaterialName.ToLower();
                if (TextureMap.ContainsKey(MaterialKey) || MaterialKey.EndsWith(".ifl"))
                {
                    continue;
                }

                byte[] PngBytes = FindNamedTexture(RsmPath, TexturePath, MaterialName, FileCache);
                if (PngBytes != null)
                {
                    TextureMap[MaterialKey] = new List<byte[]> { PngBytes };
                }
            }

            foreach (string MaterialName in UniqueNames)
            {
                string MaterialKey = MaterialName.ToLower();
                if (TextureMap.ContainsKey(MaterialKey) || MaterialKey.EndsWith(".ifl"))
                {
                    continue;
                }
                if (FallbackMap.TryGetValue(MaterialKey, out byte[] FallbackPng))
                {
                    TextureMap[MaterialKey] = new List<byte[]> { FallbackPng };
                }
            }

            return TextureMap;
        }

        private static byte[] FindNamedTexture(string RsmPath, string TexturePath, string MaterialName, Dictionary<string, byte[]> FileCache)
        {
            ushort NameKey = RstReader.RstNameKey(MaterialName);
            string Folder = Path.GetDirectoryName(RsmPath);
            string Stem = Path.GetFileNameWithoutExtension(RsmPath);

            List<string> CandidatePaths = new List<string>();
            if (!string.IsNullOrEmpty(TexturePath))
            {
                CandidatePaths.Add(TexturePath);
            }

            Match SlotMatch = SlotRegex.Match(Stem);
            if (SlotMatch.Success)
            {
                string Prefix = SlotMatch.Groups[1].Value;
                int Number = int.Parse(SlotMatch.Groups[2].Value);
                int Width = SlotMatch.Groups[2].Value.Length;

                for (int SlotDistance = 1; SlotDistance <= RstNameSearchSlots; SlotDistance++)
                {
                    foreach (int CandidateNumber in new[] { Number - SlotDistance, Number + SlotDistance })
                    {
                        if (CandidateNumber < 0)
                        {
                            continue;
                        }
                        foreach (string Extension in new[] { "rst", "RST" })
                        {
                            string CandidatePath = SlotPath(Folder, Prefix, CandidateNumber, Width, Extension);
                            if (CandidatePath != TexturePath && File.Exists(CandidatePath))
                            {
                                CandidatePaths.Add(CandidatePath);
                            }
                        }
                    }
                }
            }

            foreach (string CandidatePath in CandidatePaths)
            {
                if (!FileCache.TryGetValue(CandidatePath, out byte[] FileData))
                {
                    FileData = File.ReadAllBytes(CandidatePath);
                    FileCache[CandidatePath] = FileData;
                }

                if (FileData == null || FileData.Length < 3 || (FileData[0] != 'r' && FileData[0] != 'R'))
                {
                    continue;
                }

                List<RstRecord> ListRecord = RstReader.ReadRst(FileData);
                foreach (RstRecord Record in ListRecord)
                {
                    if (Record.NameKey == NameKey && Record.ArgbPixels != null)
                    {
                        return RstReader.ConvertArgbToPng(Record.Width, Record.Height, Record.ArgbPixels);
                    }
                }
            }

            return null;
        }

        private static List<RsaClip> LoadClips(string RsmPath, RsmDocument Model)
        {
            RSAFileContainer RSAFiles = FindAnimationFiles(RsmPath);
            RsmDocument NextModel = null;

            if (RSAFiles.NextModel != null)
            {
                using FileStream SourceStream = File.OpenRead(RSAFiles.NextModel);
                using BigEndianBinaryReader SourceReader = new BigEndianBinaryReader(SourceStream);
                NextModel = RSMReader.ParseRsm(SourceReader);
            }

            List<RsaClip> ListRSAClip = new List<RsaClip>();

            foreach (string ActiveRSAPath in RSAFiles.ListRSAFileBefore.Concat(RSAFiles.ListRSAFileAfter))
            {
                byte[] ArrayRSAData = File.ReadAllBytes(ActiveRSAPath);

                RsaClip FoundRSA = RSAReader.Parse(ArrayRSAData, Path.GetFileNameWithoutExtension(ActiveRSAPath));

                if (FoundRSA == null || !ClipFits(FoundRSA, Model))
                {
                    continue;
                }

                // Prefer the next model when the clip also fits it
                if (RSAFiles.ListRSAFileAfter.Contains(ActiveRSAPath) && NextModel != null && ClipFits(FoundRSA, NextModel))
                {
                    continue;
                }

                ListRSAClip.Add(FoundRSA);
            }

            return ListRSAClip;
        }

        private static RSAFileContainer FindAnimationFiles(string RsmPath)
        {
            string RSMFolder = Path.GetDirectoryName(RsmPath);
            string RSMFileName = Path.GetFileNameWithoutExtension(RsmPath);

            // Stand-alone name
            foreach (string Extension in new[] { ".RSA", ".rsa" })
            {
                string CandidatePath = Path.Combine(RSMFolder, RSMFileName + Extension);
                if (File.Exists(CandidatePath))
                {
                    return new RSAFileContainer(new List<string> { CandidatePath }, new List<string>(), null);
                }
            }

            List<string> RsaPathsBefore = new List<string>();
            List<string> RsaPathsAfter = new List<string>();
            string NextModel = null;

            (string Prefix, int Number, int Width)? Slot = SplitSlotName(RSMFileName);
            if (Slot == null)
            {
                return new RSAFileContainer(RsaPathsBefore, RsaPathsAfter, null);
            }

            (string Prefix, int Number, int Width) = Slot.Value;

            // Walk backwards for the contiguous RSA run that belongs to this model
            int PreviousNumber = Number - 1;
            while (PreviousNumber >= 0 && File.Exists(SlotPath(RSMFolder, Prefix, PreviousNumber, Width, "rsa")))
            {
                RsaPathsBefore.Insert(0, SlotPath(RSMFolder, Prefix, PreviousNumber, Width, "rsa"));
                PreviousNumber--;
            }

            // After the texture, collect possible later RSAs (they usually belong to the next model)
            int NextSlotNumber = Number + 1;

            if (File.Exists(SlotPath(RSMFolder, Prefix, NextSlotNumber, Width, "rst"))
                || File.Exists(SlotPath(RSMFolder, Prefix, NextSlotNumber, Width, "rtd")))
            {
                NextSlotNumber++;
                while (File.Exists(SlotPath(RSMFolder, Prefix, NextSlotNumber, Width, "rsa")))
                {
                    RsaPathsAfter.Add(SlotPath(RSMFolder, Prefix, NextSlotNumber, Width, "rsa"));
                    NextSlotNumber++;
                }
                foreach (string Extension in new[] { "rsm", "RSM" })
                {
                    string CandidatePath = SlotPath(RSMFolder, Prefix, NextSlotNumber, Width, Extension);
                    if (File.Exists(CandidatePath))
                    {
                        NextModel = CandidatePath;
                        break;
                    }
                }
            }

            return new RSAFileContainer(RsaPathsBefore, RsaPathsAfter, NextModel);
        }

        private static bool RigMatches(RsaClip Clip, List<RsmNode> ListNode)
        {
            List<float> ListError = new List<float>();
            List<float> ListNodeTranslationLength = new List<float>();

            for (int ChannelIndex = 0; ChannelIndex < Clip.ListChannel.Count; ChannelIndex++)
            {
                RsaChannel ActiveChannel = Clip.ListChannel[ChannelIndex];

                if (ActiveChannel.Kind != RsaChannelKind.TransformKeys || ChannelIndex == 0 || ActiveChannel.ListKeyframe.Count == 0)
                {
                    continue;
                }

                Vector3 ClipTranslation = ActiveChannel.ListKeyframe[0].Translation;
                Vector3 NodeTranslation = ListNode[ChannelIndex].Translation;

                ListError.Add(Vector3.Distance(ClipTranslation, NodeTranslation));
                ListNodeTranslationLength.Add(NodeTranslation.Length());
            }

            if (ListError.Count == 0)
            {
                return true;
            }

            float MeanError = ListError.Average();
            float MeanLength = Math.Max(ListNodeTranslationLength.Average(), 1e-6f);
            return MeanError < 0.5f * MeanLength;
        }

        private static bool ClipFits(RsaClip Clip, RsmDocument Model)
        {
            if (Clip.NodeCount != Model.ListNode.Count)
            {
                return false;
            }

            bool Usable = false;
            for (int ChannelIndex = 0; ChannelIndex < Clip.ListChannel.Count; ChannelIndex++)
            {
                RsaChannel Channel = Clip.ListChannel[ChannelIndex];
                if (Channel.Kind == RsaChannelKind.TransformKeys)
                {
                    Usable = true;
                }
                else if (Channel.Kind == RsaChannelKind.VertexFrames
                         && Model.DicMeshByNodeIndex.TryGetValue(ChannelIndex, out RsmMesh LoadedMesh)
                         && !LoadedMesh.IsSkinned
                         && Channel.VertexCount == LoadedMesh.ListPosition.Count)
                {
                    Usable = true;
                }
            }

            return Usable && RigMatches(Clip, Model.ListNode);
        }

        internal struct RSAFileContainer
        {
            public List<string> ListRSAFileBefore;
            public List<string> ListRSAFileAfter;
            public string NextModel;

            public RSAFileContainer(List<string> ListRSAFileBefore, List<string> ListRSAFileAfter, string NextModel)
            {
                this.ListRSAFileBefore = ListRSAFileBefore;
                this.ListRSAFileAfter = ListRSAFileAfter;
                this.NextModel = NextModel;
            }
        }
    }
}
