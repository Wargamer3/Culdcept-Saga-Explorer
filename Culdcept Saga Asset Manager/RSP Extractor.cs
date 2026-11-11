using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Buffers.Binary;

namespace CuldceptSagaExplorer.AssetManager
{
    public static class RspUnpacker
    {
        private const int DataSlotCount = 6400;

        public static event ProgressCallback OnProgress;

        public static int DepackAllRspFiles(string RSPFolder, string ExtractionFolder)
        {
            if (!Directory.Exists(ExtractionFolder))
            {
                Directory.CreateDirectory(ExtractionFolder);
            }

            int FileExtractedCount = 0;

            IOrderedEnumerable<string> ArrayRSPFilePath = Directory.GetFiles(RSPFolder, "DATA*.RSP", SearchOption.TopDirectoryOnly)
                                 .OrderBy(FilePath => FilePath, StringComparer.OrdinalIgnoreCase);

            foreach (string ActiveRSPFilePath in ArrayRSPFilePath)
            {
                string ActiveRSPFileName = Path.GetFileNameWithoutExtension(ActiveRSPFilePath);
                long ActiveRSPFileSizeInByte = new FileInfo(ActiveRSPFilePath).Length;

                string RSPFolderDestination = Path.Combine(ExtractionFolder, ActiveRSPFileName);

                if (Directory.Exists(RSPFolderDestination))
                {
                    Directory.Delete(RSPFolderDestination, true);
                }
                Directory.CreateDirectory(RSPFolderDestination);

                using FileStream FS = File.OpenRead(ActiveRSPFilePath);

                int TableOfContentSize = DataSlotCount * 8;
                byte[] ArrayTableOfContent = new byte[TableOfContentSize];
                int BytesRead = FS.Read(ArrayTableOfContent, 0, TableOfContentSize);

                if (BytesRead < TableOfContentSize)
                {
                    throw new Exception($"'{ActiveRSPFilePath}' is too small.");
                }

                ReadOnlySpan<byte> HeaderSpan = ArrayTableOfContent;

                for (int SlotIndex = 0; SlotIndex < DataSlotCount; SlotIndex++)
                {
                    ReadOnlySpan<byte> HeaderData = HeaderSpan.Slice(SlotIndex * 8, 8);
                    uint SlotOffset = BinaryPrimitives.ReadUInt32LittleEndian(HeaderData);
                    uint SlotSize = BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(4));

                    //Empty slot
                    if (SlotOffset == 0xFFFFFFFF || SlotSize == 0)
                    {
                        continue;
                    }

                    if (SlotOffset + SlotSize > ActiveRSPFileSizeInByte)
                    {
                        throw new Exception($"'{ActiveRSPFileName}_{SlotIndex:D4}' is too big.");
                    }

                    FS.Seek(SlotOffset, SeekOrigin.Begin);
                    byte[] ArraySlotData = new byte[SlotSize];
                    FS.Read(ArraySlotData);

                    string FileExtension = GetExtension(ArraySlotData.AsSpan(0, Math.Min(3, ArraySlotData.Length)));

                    string FinalPath = Path.Combine(RSPFolderDestination, $"{ActiveRSPFileName}_{SlotIndex:D4}.{FileExtension}");

                    OnProgress(SlotIndex, DataSlotCount, $"Extracting {FinalPath}");

                    File.WriteAllBytes(FinalPath, ArraySlotData);
                    FileExtractedCount++;
                }
            }

            return FileExtractedCount;
        }

        public static int RepackAllRspFiles(string UnpackingDirectory, string RepackingDirectory)
        {
            if (!Directory.Exists(RepackingDirectory))
            {
                Directory.CreateDirectory(RepackingDirectory);
            }

            int FileExtractedCount = 0;

            IOrderedEnumerable<string> ArrayUnpackedRSPFolderPath = Directory.GetDirectories(UnpackingDirectory, "DATA*", SearchOption.TopDirectoryOnly)
                                 .OrderBy(FilePath => FilePath, StringComparer.OrdinalIgnoreCase);

            foreach (string ActiveUnpackedRSPFolderPath in ArrayUnpackedRSPFolderPath)
            {
                string FolderName = new DirectoryInfo(ActiveUnpackedRSPFolderPath).Name;
                string RSPDestinationFile = Path.Combine(RepackingDirectory, FolderName) + ".RSP";

                FileExtractedCount += RepackRSPFile(ActiveUnpackedRSPFolderPath, FolderName, RSPDestinationFile);

            }

            return FileExtractedCount;
        }

        private static int RepackRSPFile(string UnpackedRSPFolderPath, string FolderName, string RSPDestinationFile)
        {
            int FileExtractedCount = 0;

            int TableOfContentSize = DataSlotCount * 8; // 6,400 * 8 = 51,200
            byte[] ArrayTableOfContent = new byte[TableOfContentSize];

            uint AlignmentBoundary = 2048; // Standard Xbox 360 sector boundary

            // Align initial data offset so payload starts at a multiple of 2048
            uint DataOffset = Align((uint)TableOfContentSize, AlignmentBoundary);

            using FileStream DestinationStream = File.Create(RSPDestinationFile);

            // Allocate the empty header space on disk first
            DestinationStream.Write(ArrayTableOfContent, 0, TableOfContentSize);

            for (int SlotIndex = 0; SlotIndex < DataSlotCount; SlotIndex++)
            {
                string SearchPattern = $"{FolderName}_{SlotIndex:D4}.*";
                string[] ArrayRSPFilePathFound = Directory.GetFiles(UnpackedRSPFolderPath, SearchPattern);

                if (ArrayRSPFilePathFound.Length == 1)
                {
                    byte[] FileToWrite = File.ReadAllBytes(ArrayRSPFilePathFound[0]);
                    uint FileLength = (uint)FileToWrite.Length;

                    // Align offset BEFORE assigning it to the slot
                    DataOffset = Align(DataOffset, AlignmentBoundary);

                    BinaryPrimitives.WriteUInt32LittleEndian(ArrayTableOfContent.AsSpan(SlotIndex * 8, 4), DataOffset);
                    BinaryPrimitives.WriteUInt32LittleEndian(ArrayTableOfContent.AsSpan(SlotIndex * 8 + 4, 4), FileLength);

                    DestinationStream.Seek(DataOffset, SeekOrigin.Begin);
                    DestinationStream.Write(FileToWrite, 0, FileToWrite.Length);

                    DataOffset += FileLength;
                    FileExtractedCount++;
                }
                else if (ArrayRSPFilePathFound.Length > 1)
                {
                    throw new Exception($"Multiple files found for slot {SlotIndex:D4} in '{UnpackedRSPFolderPath}'.");
                }
                else
                {
                    // Empty / Invalid slot
                    BinaryPrimitives.WriteUInt32LittleEndian(ArrayTableOfContent.AsSpan(SlotIndex * 8, 4), 0xFFFFFFFF);
                    BinaryPrimitives.WriteUInt32LittleEndian(ArrayTableOfContent.AsSpan(SlotIndex * 8 + 4, 4), 0);
                }
            }

            // Set final padded file length
            DestinationStream.SetLength(Align(DataOffset, AlignmentBoundary));

            DestinationStream.Seek(0, SeekOrigin.Begin);
            DestinationStream.Write(ArrayTableOfContent, 0, TableOfContentSize);

            return FileExtractedCount;
        }

        private static uint Align(uint ValueToAlign, uint Alignment)
        {
            if (Alignment == 0)
            {
                return ValueToAlign;
            }

            uint Remainder = ValueToAlign % Alignment;
            return Remainder == 0 ? ValueToAlign : ValueToAlign + (Alignment - Remainder);
        }

        private static string GetExtension(ReadOnlySpan<byte> SpanFirst3Byte)
        {
            if (SpanFirst3Byte.Length < 3)
            {
                return "bin";
            }

            bool IsLetters = true;
            foreach (byte ActiveByte in SpanFirst3Byte)
            {
                if (!((ActiveByte >= 'A' && ActiveByte <= 'Z') || (ActiveByte >= 'a' && ActiveByte <= 'z')))
                {
                    IsLetters = false;
                    break;
                }
            }

            if (IsLetters)
            {
                return Encoding.ASCII.GetString(SpanFirst3Byte).ToLowerInvariant();
            }

            return "bin";
        }

        public delegate void ProgressCallback(int Current, int Total, string Message);
    }
}
