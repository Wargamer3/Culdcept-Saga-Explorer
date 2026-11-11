using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CuldceptSagaExplorer.AssetManager.RST
{
    public class RstReader
    {
        #region Constants

        // RST / RTD / RTJ texture files
        private const int RstHeaderSize = 12;
        private const int RstCountOffset = 8;
        private const int RstRecordHeaderSize = 20;
        // Every record starts with four uint16 checksums, all sum(index * byte) truncated to 16 bits:
        //   bytes 0..1  name + little-endian width/height at indices 32..35, plus 0xC5C1
        //   bytes 2..3  the same sum plus 0xC5C1 - 0x8B83
        //   bytes 4..5  name only, plus 55119
        //   bytes 6..7  name only, plus 10416
        //     (book_001_04.png in DATA00_0031.rst is the only named texture whose stored value is one higher)
        // Width and height are the little-endian bytes of the RSM material's texture size. A placeholder
        // stores 0×0 in the record and still checksums that material size. The uint16 at file offset 10
        // counts those placeholders (0 on every non-animated file). The same nine files append a playlist
        // of 12-byte entries (checksum, name keys, frame value) after the images.
        private const int RstNameKeyOffset = 4;
        private const int RstNameKey2Offset = 6;
        private const int RstNameKeyBias = 55119;
        private const int RstNameKey2Bias = 10416;
        private const int RstDimensionHashBias = 0xC5C1;
        private const int RstDimensionHashGap = 0x8B83;
        // Shared textures (DATA01 player parts) sit up to ~40 slots away from the models that use them.
        private const int RstNameSearchSlots = 64;
        private const int RstFlagsOffset = 8;
        private const int RstFormatOffset = 12;
        internal const int RstPlaceholderFlag = 4;
        private const int BytesPerPixel = 4;
        private const int ExpectedBitsPerPixel = 32;
        private const int RtdHeaderSize = 0x30;  // RTD / RTJ hold a DDS / JPEG image after this header
        private const int DdsFourCcOffset = 84;
        private const int DdsFourCcSize = 4;

        #endregion

        public static List<RstRecord> ReadRst(byte[] SourceBytes)
        {
            return ReadRst(SourceBytes, out _);
        }

        public static List<RstRecord> ReadRst(byte[] SourceBytes, out byte[] Footer)
        {
            List<RstRecord> ListReadRST = new List<RstRecord>();
            Footer = Array.Empty<byte>();

            if (SourceBytes == null || SourceBytes.Length < RstHeaderSize)
            {
                return ListReadRST;
            }

            bool IsBigEndian = SourceBytes[0] == 'r' && SourceBytes[1] == 's' && SourceBytes[2] == 't';
            int NumberOfRstInFile = ReadNumberOfRstInFile(SourceBytes, IsBigEndian);
            int ReaderOffset = RstHeaderSize;
            bool Complete = true;

            for (int RecordIndex = 0; RecordIndex < NumberOfRstInFile; RecordIndex++)
            {
                if (ReaderOffset + RstRecordHeaderSize > SourceBytes.Length)
                {
                    Complete = false;
                    break;
                }

                uint HeaderWord = IsBigEndian
                    ? BinaryPrimitives.ReadUInt32BigEndian(SourceBytes.AsSpan(ReaderOffset))
                    : BinaryPrimitives.ReadUInt32LittleEndian(SourceBytes.AsSpan(ReaderOffset));

                ushort NameKey = IsBigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(SourceBytes.AsSpan(ReaderOffset + RstNameKeyOffset))
                    : BinaryPrimitives.ReadUInt16LittleEndian(SourceBytes.AsSpan(ReaderOffset + RstNameKeyOffset));

                ushort NameKey2 = IsBigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(SourceBytes.AsSpan(ReaderOffset + RstNameKey2Offset))
                    : BinaryPrimitives.ReadUInt16LittleEndian(SourceBytes.AsSpan(ReaderOffset + RstNameKey2Offset));

                uint Flags = IsBigEndian
                    ? BinaryPrimitives.ReadUInt32BigEndian(SourceBytes.AsSpan(ReaderOffset + RstFlagsOffset))
                    : BinaryPrimitives.ReadUInt32LittleEndian(SourceBytes.AsSpan(ReaderOffset + RstFlagsOffset));

                ushort BitsPerPixel = IsBigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset))
                    : BinaryPrimitives.ReadUInt16LittleEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset));

                ushort Width = IsBigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset + 2))
                    : BinaryPrimitives.ReadUInt16LittleEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset + 2));

                ushort Height = IsBigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset + 4))
                    : BinaryPrimitives.ReadUInt16LittleEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset + 4));

                ushort Tail = IsBigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset + 6))
                    : BinaryPrimitives.ReadUInt16LittleEndian(SourceBytes.AsSpan(ReaderOffset + RstFormatOffset + 6));

                ReaderOffset += RstRecordHeaderSize;

                if ((Flags & RstPlaceholderFlag) != 0)
                {
                    ListReadRST.Add(new RstRecord
                    {
                        Width = 0,
                        Height = 0,
                        NameKey = NameKey,
                        NameKey2 = NameKey2,
                        HeaderWord = HeaderWord,
                        Flags = Flags,
                        Tail = Tail,
                        ArgbPixels = null
                    });
                    continue;
                }

                int PixelBytes = Width * Height * BytesPerPixel;
                if (BitsPerPixel != ExpectedBitsPerPixel || ReaderOffset + PixelBytes > SourceBytes.Length)
                {
                    ListReadRST.Add(new RstRecord
                    {
                        Width = Width,
                        Height = Height,
                        NameKey = NameKey,
                        NameKey2 = NameKey2,
                        HeaderWord = HeaderWord,
                        Flags = Flags,
                        Tail = Tail,
                        ArgbPixels = null
                    });
                    Complete = false;
                    break;
                }

                byte[] PixelBuffer = new byte[PixelBytes];
                Array.Copy(SourceBytes, ReaderOffset, PixelBuffer, 0, PixelBytes);
                ListReadRST.Add(new RstRecord
                {
                    Width = Width,
                    Height = Height,
                    NameKey = NameKey,
                    NameKey2 = NameKey2,
                    HeaderWord = HeaderWord,
                    Flags = Flags,
                    Tail = Tail,
                    ArgbPixels = PixelBuffer
                });

                ReaderOffset += PixelBytes;
            }

            if (Complete && ReaderOffset < SourceBytes.Length)
            {
                Footer = new byte[SourceBytes.Length - ReaderOffset];
                Array.Copy(SourceBytes, ReaderOffset, Footer, 0, Footer.Length);
            }

            return ListReadRST;
        }

        private static IEnumerable<RstHeaderItem> IterateRst(ReadOnlyMemory<byte> SourceBytes)
        {
            bool IsBigEndian = SourceBytes.Length >= 3
                && SourceBytes.Span[0] == (byte)'r'
                && SourceBytes.Span[1] == (byte)'s'
                && SourceBytes.Span[2] == (byte)'t';

            if (SourceBytes.Length < RstCountOffset + 2)
            {
                yield break;
            }

            int RecordCount = ReadNumberOfRstInFile(SourceBytes.Span, IsBigEndian);
            int ReaderOffset = RstHeaderSize;

            for (int RecordIndex = 0; RecordIndex < RecordCount; RecordIndex++)
            {
                if (ReaderOffset + RstRecordHeaderSize > SourceBytes.Length)
                {
                    yield break;
                }

                ReadOnlySpan<byte> RecordHeader = SourceBytes.Slice(ReaderOffset, RstRecordHeaderSize).Span;

                ushort NameKey = ReadUInt16(RecordHeader.Slice(RstNameKeyOffset), IsBigEndian);
                uint Flags = ReadUInt32(RecordHeader.Slice(RstFlagsOffset), IsBigEndian);

                ReadOnlySpan<byte> FormatSpan = RecordHeader.Slice(RstFormatOffset);
                ushort BitsPerPixel = ReadUInt16(FormatSpan, IsBigEndian);
                ushort Width = ReadUInt16(FormatSpan.Slice(2), IsBigEndian);
                ushort Height = ReadUInt16(FormatSpan.Slice(4), IsBigEndian);

                ReaderOffset += RstRecordHeaderSize;

                if ((Flags & RstPlaceholderFlag) != 0)
                {
                    yield return new RstHeaderItem(0, 0, 0, NameKey, true);
                    continue;
                }

                int PixelBytes = Width * Height * BytesPerPixel;

                if (BitsPerPixel != ExpectedBitsPerPixel || ReaderOffset + PixelBytes > SourceBytes.Length)
                {
                    yield return new RstHeaderItem(0, Width, Height, NameKey, true);
                    yield break;
                }

                yield return new RstHeaderItem(ReaderOffset, Width, Height, NameKey, false);
                ReaderOffset += PixelBytes;
            }
        }

        private static IEnumerable<ushort> RstNameKeys(ReadOnlyMemory<byte> SourceBytes)
        {
            foreach ((int _, int _, int _, ushort NameKey, bool _) in IterateRst(SourceBytes))
            {
                yield return NameKey;
            }
        }

        public static byte[] ConvertArgbToPng(ushort Width, ushort Height, byte[] ArgbPixels)
        {
            if (ArgbPixels == null || Width == 0 || Height == 0)
            {
                return null;
            }

            // In RST, pixels are stored as ARGB (A, R, G, B).
            // GDI+ Format32bppArgb expects memory in BGRA order (B, G, R, A) on Little-Endian systems.
            byte[] BgraPixels = new byte[ArgbPixels.Length];
            for (int PixelOffset = 0; PixelOffset < ArgbPixels.Length; PixelOffset += 4)
            {
                byte Alpha = ArgbPixels[PixelOffset];
                byte Red = ArgbPixels[PixelOffset + 1];
                byte Green = ArgbPixels[PixelOffset + 2];
                byte Blue = ArgbPixels[PixelOffset + 3];

                BgraPixels[PixelOffset] = Blue;
                BgraPixels[PixelOffset + 1] = Green;
                BgraPixels[PixelOffset + 2] = Red;
                BgraPixels[PixelOffset + 3] = Alpha;
            }

            using Bitmap LoadedBitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            BitmapData LockedBits = LoadedBitmap.LockBits(new System.Drawing.Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(BgraPixels, 0, LockedBits.Scan0, BgraPixels.Length);
            LoadedBitmap.UnlockBits(LockedBits);

            using MemoryStream DestinationStream = new MemoryStream();
            LoadedBitmap.Save(DestinationStream, ImageFormat.Png);
            return DestinationStream.ToArray();
        }

        public static ushort RstNameKey(string Name)
        {
            return (ushort)(WeightedNameSum(Name) + RstNameKeyBias);
        }

        public static ushort RstSecondNameKey(string Name)
        {
            return (ushort)(WeightedNameSum(Name) + RstNameKey2Bias);
        }

        /// <summary>
        /// Bytes 0..3 of a record. The high half is the name checksum extended with the
        /// little-endian width and height at byte indices 32..35; the low half is that value minus 0x8B83.
        /// </summary>
        public static uint RstDimensionHash(string Name, int Width, int Height)
        {
            int Sum = WeightedNameSum(Name);
            Sum += 32 * (Width & 0xFF);
            Sum += 33 * ((Width >> 8) & 0xFF);
            Sum += 34 * (Height & 0xFF);
            Sum += 35 * ((Height >> 8) & 0xFF);
            ushort HighWord = (ushort)(Sum + RstDimensionHashBias);
            ushort LowWord = (ushort)(HighWord - RstDimensionHashGap);
            return ((uint)HighWord << 16) | LowWord;
        }

        private static int WeightedNameSum(string Name)
        {
            int Sum = 0;
            byte[] NameBytes = Encoding.GetEncoding("latin1").GetBytes(Name ?? string.Empty);

            for (int NameByteIndex = 0; NameByteIndex < NameBytes.Length; NameByteIndex++)
            {
                Sum += NameByteIndex * NameBytes[NameByteIndex];
            }

            return Sum;
        }

        private static int ReadNumberOfRstInFile(ReadOnlySpan<byte> SourceSpan, bool IsBigEndian)
        {
            if (SourceSpan.Length < RstCountOffset + 2)
            {
                return 0;
            }

            return IsBigEndian
                ? BinaryPrimitives.ReadUInt16BigEndian(SourceSpan.Slice(RstCountOffset))
                : BinaryPrimitives.ReadUInt16LittleEndian(SourceSpan.Slice(RstCountOffset));
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> SourceSpan, bool IsBigEndian)
        {
            return IsBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(SourceSpan) : BinaryPrimitives.ReadUInt16LittleEndian(SourceSpan);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> SourceSpan, bool IsBigEndian)
        {
            return IsBigEndian ? BinaryPrimitives.ReadUInt32BigEndian(SourceSpan) : BinaryPrimitives.ReadUInt32LittleEndian(SourceSpan);
        }
    }
}
