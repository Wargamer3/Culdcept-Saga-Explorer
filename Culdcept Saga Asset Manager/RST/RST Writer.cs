using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CuldceptSagaExplorer.AssetManager.RST
{
    public class RstWriter
    {
        #region Constants

        private const int RstHeaderSize = 12;
        private const int RstCountOffset = 8;
        private const int RstRecordHeaderSize = 20;
        private const int RstNameKeyOffset = 4;
        private const int RstNameKey2Offset = 6;
        private const int RstFlagsOffset = 8;
        private const int RstFormatOffset = 12;
        private const int RstTailOffset = 18;
        private const int RstPlaceholderFlag = 4;
        private const int RstAlphaFlag = 1;
        private const int BytesPerPixel = 4;
        private const int ExpectedBitsPerPixel = 32;

        #endregion

        /// <summary>
        /// Serializes a list of RstRecords into raw RST binary file bytes.
        /// </summary>
        /// <param name="ListRecord">List of texture records to pack.</param>
        /// <param name="IsBigEndian">Set true for big-endian Xbox 360 files ('rst'), false for little-endian.</param>
        /// <param name="Footer">Playlist copied from an animated file. Empty on every other RST.</param>
        public static byte[] WriteRst(List<RstRecord> ListRecord, bool IsBigEndian = true, byte[] Footer = null)
        {
            if (ListRecord == null)
            {
                ListRecord = new List<RstRecord>();
            }

            using (MemoryStream DestinationStream = new MemoryStream())
            {
                // 1. File header: magic, uint16 0, uint16 version 1, uint16 count, uint16 0.
                byte[] HeaderBytes = new byte[RstHeaderSize];
                ushort RecordCount = (ushort)ListRecord.Count;

                if (IsBigEndian)
                {
                    HeaderBytes[0] = (byte)'r';
                    HeaderBytes[1] = (byte)'s';
                    HeaderBytes[2] = (byte)'t';
                    HeaderBytes[7] = 1;
                    WriteUInt16(HeaderBytes.AsSpan(RstCountOffset), RecordCount, true);
                }
                else
                {
                    HeaderBytes[0] = (byte)'R';
                    HeaderBytes[1] = (byte)'S';
                    HeaderBytes[2] = (byte)'T';
                    HeaderBytes[6] = 1;
                    WriteUInt16(HeaderBytes.AsSpan(RstCountOffset), RecordCount, false);
                }

                DestinationStream.Write(HeaderBytes, 0, RstHeaderSize);

                // 2. Write Records. Bytes 10..11 count placeholder records (the animated lists).
                ushort ListCount = 0;
                foreach (RstRecord Record in ListRecord)
                {
                    byte[] RecordHeader = new byte[RstRecordHeaderSize];
                    bool IsPlaceholder = Record.ArgbPixels == null || Record.Width == 0 || Record.Height == 0;

                    ushort NameKey;
                    ushort NameKey2;
                    uint HeaderWord;
                    uint Flags;
                    if (!string.IsNullOrEmpty(Record.Name))
                    {
                        NameKey = RstReader.RstNameKey(Record.Name);
                        NameKey2 = Record.UseStoredNameKey2
                            ? Record.NameKey2
                            : RstReader.RstSecondNameKey(Record.Name);
                        int HashWidth;
                        int HashHeight;
                        if (Record.HasChecksumSize)
                        {
                            HashWidth = Record.ChecksumWidth;
                            HashHeight = Record.ChecksumHeight;
                        }
                        else if (IsPlaceholder)
                        {
                            HashWidth = 0;
                            HashHeight = 0;
                        }
                        else
                        {
                            HashWidth = Record.Width;
                            HashHeight = Record.Height;
                        }
                        HeaderWord = RstReader.RstDimensionHash(Record.Name, HashWidth, HashHeight);
                        Flags = IsPlaceholder
                            ? (uint)RstPlaceholderFlag
                            : (HasTranslucentPixel(Record.ArgbPixels) ? (uint)RstAlphaFlag : 0u);
                    }
                    else
                    {
                        NameKey = Record.NameKey;
                        NameKey2 = Record.NameKey2;
                        HeaderWord = Record.HeaderWord;
                        // Keep the stored flag. A few shipping images do not follow the alpha rule:
                        // DATA01_0296 is fully transparent with flag 0, and three opaque images use flag 1.
                        Flags = IsPlaceholder
                            ? (Record.Flags != 0 ? Record.Flags : RstPlaceholderFlag)
                            : Record.Flags;
                    }

                    if ((Flags & RstPlaceholderFlag) != 0)
                    {
                        ListCount++;
                    }

                    WriteUInt32(RecordHeader.AsSpan(0), HeaderWord, IsBigEndian);
                    WriteUInt16(RecordHeader.AsSpan(RstNameKeyOffset), NameKey, IsBigEndian);
                    WriteUInt16(RecordHeader.AsSpan(RstNameKey2Offset), NameKey2, IsBigEndian);
                    WriteUInt32(RecordHeader.AsSpan(RstFlagsOffset), Flags, IsBigEndian);

                    ushort BitsPerPixel = IsPlaceholder ? (ushort)0 : (ushort)ExpectedBitsPerPixel;
                    WriteUInt16(RecordHeader.AsSpan(RstFormatOffset), BitsPerPixel, IsBigEndian);
                    WriteUInt16(RecordHeader.AsSpan(RstFormatOffset + 2), Record.Width, IsBigEndian);
                    WriteUInt16(RecordHeader.AsSpan(RstFormatOffset + 4), Record.Height, IsBigEndian);
                    WriteUInt16(RecordHeader.AsSpan(RstTailOffset), Record.Tail, IsBigEndian);

                    DestinationStream.Write(RecordHeader, 0, RstRecordHeaderSize);

                    // 3. Write Pixel Data if not a placeholder
                    if (!IsPlaceholder)
                    {
                        int ExpectedBytes = Record.Width * Record.Height * BytesPerPixel;
                        if (Record.ArgbPixels.Length != ExpectedBytes)
                        {
                            throw new ArgumentException(
                                $"Invalid pixel buffer size for {Record.Width}x{Record.Height}. Expected {ExpectedBytes} bytes, got {Record.ArgbPixels.Length}.");
                        }

                        DestinationStream.Write(Record.ArgbPixels, 0, ExpectedBytes);
                    }
                }

                if (ListCount != 0)
                {
                    byte[] ListField = new byte[2];
                    WriteUInt16(ListField.AsSpan(), ListCount, IsBigEndian);
                    DestinationStream.Position = 10;
                    DestinationStream.Write(ListField, 0, 2);
                }

                if (Footer != null && Footer.Length > 0)
                {
                    DestinationStream.Position = DestinationStream.Length;
                    DestinationStream.Write(Footer, 0, Footer.Length);
                }

                return DestinationStream.ToArray();
            }
        }

        /// <summary>
        /// Converts standard PNG/Bitmap byte streams into raw ARGB pixel byte arrays expected by RST.
        /// </summary>
        public static byte[] ConvertPngToArgb(byte[] PngData, out ushort Width, out ushort Height)
        {
            if (PngData == null || PngData.Length == 0)
            {
                Width = 0;
                Height = 0;
                return null;
            }

            using (MemoryStream SourceStream = new MemoryStream(PngData))
            using (Bitmap LoadedBitmap = new Bitmap(SourceStream))
            {
                Width = (ushort)LoadedBitmap.Width;
                Height = (ushort)LoadedBitmap.Height;

                int PixelBytes = Width * Height * BytesPerPixel;
                byte[] BgraPixels = new byte[PixelBytes];

                // Lock GDI+ memory (GDI+ Format32bppArgb stores memory as BGRA on Windows)
                BitmapData LockedBits = LoadedBitmap.LockBits(
                    new Rectangle(0, 0, Width, Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                Marshal.Copy(LockedBits.Scan0, BgraPixels, 0, PixelBytes);
                LoadedBitmap.UnlockBits(LockedBits);

                // Reorder BGRA (GDI+) back to ARGB (RST)
                byte[] ArgbPixels = new byte[PixelBytes];
                for (int PixelOffset = 0; PixelOffset < PixelBytes; PixelOffset += 4)
                {
                    byte Blue = BgraPixels[PixelOffset];
                    byte Green = BgraPixels[PixelOffset + 1];
                    byte Red = BgraPixels[PixelOffset + 2];
                    byte Alpha = BgraPixels[PixelOffset + 3];

                    ArgbPixels[PixelOffset] = Alpha;
                    ArgbPixels[PixelOffset + 1] = Red;
                    ArgbPixels[PixelOffset + 2] = Green;
                    ArgbPixels[PixelOffset + 3] = Blue;
                }

                return ArgbPixels;
            }
        }

        private static bool HasTranslucentPixel(byte[] ArgbPixels)
        {
            for (int PixelOffset = 0; PixelOffset < ArgbPixels.Length; PixelOffset += 4)
            {
                if (ArgbPixels[PixelOffset] != 255)
                {
                    return true;
                }
            }
            return false;
        }

        #region Helper Binary Writers

        private static void WriteUInt16(Span<byte> DestinationSpan, ushort Value, bool IsBigEndian)
        {
            if (IsBigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(DestinationSpan, Value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(DestinationSpan, Value);
            }
        }

        private static void WriteUInt32(Span<byte> DestinationSpan, uint Value, bool IsBigEndian)
        {
            if (IsBigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(DestinationSpan, Value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(DestinationSpan, Value);
            }
        }

        #endregion
    }
}
