using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace CuldceptSagaExplorer.AssetManager.RST
{
    /// <summary>
    /// RTD and RTJ files are one image for every material on the model.
    /// A 0x30 header is followed by a DDS (RTD, DXT4 or DXT5) or a JPEG (RTJ).
    /// </summary>
    public static class RtdReader
    {
        private const int HeaderSize = 0x30;
        private const int DdsHeaderSize = 128;
        private const int DdsFourCcOffset = 84;

        public static byte[] ConvertToPng(byte[] SourceBytes)
        {
            if (SourceBytes == null || SourceBytes.Length < 16 || IsRst(SourceBytes))
            {
                return null;
            }

            int Payload = ImageOffset(SourceBytes);
            if (Payload < 0 || Payload >= SourceBytes.Length)
            {
                return null;
            }

            if (IsDds(SourceBytes, Payload))
            {
                return DecodeDds(SourceBytes, Payload);
            }

            if (IsJpeg(SourceBytes, Payload))
            {
                return JpegToPng(SourceBytes, Payload);
            }

            return null;
        }

        private static bool IsRst(byte[] SourceBytes)
        {
            return (SourceBytes[0] == 'r' || SourceBytes[0] == 'R')
                && (SourceBytes[1] == 's' || SourceBytes[1] == 'S')
                && (SourceBytes[2] == 't' || SourceBytes[2] == 'T');
        }

        private static int ImageOffset(byte[] SourceBytes)
        {
            bool Wrapped = SourceBytes.Length > HeaderSize
                && (SourceBytes[0] == 'R' || SourceBytes[0] == 'r')
                && (SourceBytes[1] == 'T' || SourceBytes[1] == 't')
                && (SourceBytes[2] == 'D' || SourceBytes[2] == 'd' || SourceBytes[2] == 'J' || SourceBytes[2] == 'j');
            if (Wrapped || (SourceBytes.Length > HeaderSize && (IsDds(SourceBytes, HeaderSize) || IsJpeg(SourceBytes, HeaderSize))))
            {
                return HeaderSize;
            }

            if (IsDds(SourceBytes, 0) || IsJpeg(SourceBytes, 0))
            {
                return 0;
            }

            return -1;
        }

        private static bool IsDds(byte[] SourceBytes, int Offset)
        {
            return Offset + 4 <= SourceBytes.Length
                && SourceBytes[Offset] == (byte)'D'
                && SourceBytes[Offset + 1] == (byte)'D'
                && SourceBytes[Offset + 2] == (byte)'S'
                && SourceBytes[Offset + 3] == (byte)' ';
        }

        private static bool IsJpeg(byte[] SourceBytes, int Offset)
        {
            return Offset + 2 <= SourceBytes.Length
                && SourceBytes[Offset] == 0xFF
                && SourceBytes[Offset + 1] == 0xD8;
        }

        private static byte[] DecodeDds(byte[] SourceBytes, int Offset)
        {
            if (Offset + DdsHeaderSize > SourceBytes.Length)
            {
                return null;
            }

            int Height = ReadInt32(SourceBytes, Offset + 12);
            int Width = ReadInt32(SourceBytes, Offset + 16);
            if (Width <= 0 || Height <= 0 || Width > ushort.MaxValue || Height > ushort.MaxValue)
            {
                return null;
            }

            string FourCc = System.Text.Encoding.ASCII.GetString(SourceBytes, Offset + DdsFourCcOffset, 4);
            int BlockBytes;
            bool ExplicitAlpha;
            bool InterpolatedAlpha;
            if (FourCc == "DXT1")
            {
                BlockBytes = 8;
                ExplicitAlpha = false;
                InterpolatedAlpha = false;
            }
            else if (FourCc == "DXT3")
            {
                BlockBytes = 16;
                ExplicitAlpha = true;
                InterpolatedAlpha = false;
            }
            else if (FourCc == "DXT4" || FourCc == "DXT5")
            {
                BlockBytes = 16;
                ExplicitAlpha = false;
                InterpolatedAlpha = true;
            }
            else
            {
                return null;
            }

            int BlocksWide = (Width + 3) / 4;
            int BlocksHigh = (Height + 3) / 4;
            int DataOffset = Offset + DdsHeaderSize;
            if (DataOffset + (long)BlocksWide * BlocksHigh * BlockBytes > SourceBytes.Length)
            {
                return null;
            }

            byte[] Pixels = new byte[Width * Height * 4];
            int BlockOffset = DataOffset;
            for (int BlockY = 0; BlockY < BlocksHigh; BlockY++)
            {
                for (int BlockX = 0; BlockX < BlocksWide; BlockX++)
                {
                    DecodeBlock(SourceBytes, BlockOffset, BlockBytes, ExplicitAlpha, InterpolatedAlpha, Pixels, Width, Height, BlockX * 4, BlockY * 4);
                    BlockOffset += BlockBytes;
                }
            }

            return RstReader.ConvertArgbToPng((ushort)Width, (ushort)Height, Pixels);
        }

        private static void DecodeBlock(byte[] Source, int Offset, int BlockBytes, bool ExplicitAlpha, bool InterpolatedAlpha,
            byte[] Pixels, int Width, int Height, int OriginX, int OriginY)
        {
            int AlphaOffset = Offset;
            int ColorOffset = ExplicitAlpha || InterpolatedAlpha ? Offset + 8 : Offset;
            ushort Color0 = ReadUInt16(Source, ColorOffset);
            ushort Color1 = ReadUInt16(Source, ColorOffset + 2);
            uint ColorBits = ReadUInt32(Source, ColorOffset + 4);
            Expand565(Color0, out byte Red0, out byte Green0, out byte Blue0);
            Expand565(Color1, out byte Red1, out byte Green1, out byte Blue1);

            byte[] Red = new byte[4];
            byte[] Green = new byte[4];
            byte[] Blue = new byte[4];
            byte[] Opaque = new byte[4];
            Red[0] = Red0;
            Green[0] = Green0;
            Blue[0] = Blue0;
            Opaque[0] = 255;
            Red[1] = Red1;
            Green[1] = Green1;
            Blue[1] = Blue1;
            Opaque[1] = 255;
            bool PunchThrough = BlockBytes == 8 && Color0 <= Color1;
            if (PunchThrough)
            {
                Red[2] = (byte)((Red0 + Red1) / 2);
                Green[2] = (byte)((Green0 + Green1) / 2);
                Blue[2] = (byte)((Blue0 + Blue1) / 2);
                Opaque[2] = 255;
                Red[3] = 0;
                Green[3] = 0;
                Blue[3] = 0;
                Opaque[3] = 0;
            }
            else
            {
                Red[2] = (byte)((2 * Red0 + Red1) / 3);
                Green[2] = (byte)((2 * Green0 + Green1) / 3);
                Blue[2] = (byte)((2 * Blue0 + Blue1) / 3);
                Opaque[2] = 255;
                Red[3] = (byte)((Red0 + 2 * Red1) / 3);
                Green[3] = (byte)((Green0 + 2 * Green1) / 3);
                Blue[3] = (byte)((Blue0 + 2 * Blue1) / 3);
                Opaque[3] = 255;
            }

            byte[] Alpha = null;
            ulong AlphaBits = 0;
            if (InterpolatedAlpha)
            {
                Alpha = BuildAlphaRamp(Source[AlphaOffset], Source[AlphaOffset + 1]);
                AlphaBits = 0;
                for (int ByteIndex = 0; ByteIndex < 6; ByteIndex++)
                {
                    AlphaBits |= (ulong)Source[AlphaOffset + 2 + ByteIndex] << (8 * ByteIndex);
                }
            }

            for (int PixelY = 0; PixelY < 4; PixelY++)
            {
                for (int PixelX = 0; PixelX < 4; PixelX++)
                {
                    int X = OriginX + PixelX;
                    int Y = OriginY + PixelY;
                    if (X >= Width || Y >= Height)
                    {
                        continue;
                    }

                    int Pixel = PixelY * 4 + PixelX;
                    int ColorIndex = (int)((ColorBits >> (2 * Pixel)) & 3);
                    byte PixelAlpha = Opaque[ColorIndex];
                    if (ExplicitAlpha)
                    {
                        int Nibble = Source[AlphaOffset + Pixel / 2];
                        int Value = (Pixel & 1) == 0 ? Nibble & 0x0F : Nibble >> 4;
                        PixelAlpha = (byte)(Value * 17);
                    }
                    else if (InterpolatedAlpha)
                    {
                        PixelAlpha = Alpha[(int)((AlphaBits >> (3 * Pixel)) & 7)];
                    }

                    int Destination = (Y * Width + X) * 4;
                    Pixels[Destination] = PixelAlpha;
                    Pixels[Destination + 1] = Red[ColorIndex];
                    Pixels[Destination + 2] = Green[ColorIndex];
                    Pixels[Destination + 3] = Blue[ColorIndex];
                }
            }
        }

        private static byte[] BuildAlphaRamp(byte Alpha0, byte Alpha1)
        {
            byte[] Ramp = new byte[8];
            Ramp[0] = Alpha0;
            Ramp[1] = Alpha1;
            if (Alpha0 > Alpha1)
            {
                Ramp[2] = (byte)((6 * Alpha0 + Alpha1) / 7);
                Ramp[3] = (byte)((5 * Alpha0 + 2 * Alpha1) / 7);
                Ramp[4] = (byte)((4 * Alpha0 + 3 * Alpha1) / 7);
                Ramp[5] = (byte)((3 * Alpha0 + 4 * Alpha1) / 7);
                Ramp[6] = (byte)((2 * Alpha0 + 5 * Alpha1) / 7);
                Ramp[7] = (byte)((Alpha0 + 6 * Alpha1) / 7);
            }
            else
            {
                Ramp[2] = (byte)((4 * Alpha0 + Alpha1) / 5);
                Ramp[3] = (byte)((3 * Alpha0 + 2 * Alpha1) / 5);
                Ramp[4] = (byte)((2 * Alpha0 + 3 * Alpha1) / 5);
                Ramp[5] = (byte)((Alpha0 + 4 * Alpha1) / 5);
                Ramp[6] = 0;
                Ramp[7] = 255;
            }

            return Ramp;
        }

        private static void Expand565(ushort Color, out byte Red, out byte Green, out byte Blue)
        {
            int Red5 = (Color >> 11) & 31;
            int Green6 = (Color >> 5) & 63;
            int Blue5 = Color & 31;
            Red = (byte)((Red5 << 3) | (Red5 >> 2));
            Green = (byte)((Green6 << 2) | (Green6 >> 4));
            Blue = (byte)((Blue5 << 3) | (Blue5 >> 2));
        }

        private static byte[] JpegToPng(byte[] SourceBytes, int Offset)
        {
            using MemoryStream Source = new MemoryStream(SourceBytes, Offset, SourceBytes.Length - Offset, false);
            using Image Loaded = Image.FromStream(Source);
            using Bitmap Bitmap = new Bitmap(Loaded);
            using MemoryStream Destination = new MemoryStream();
            Bitmap.Save(Destination, ImageFormat.Png);
            return Destination.ToArray();
        }

        private static int ReadInt32(byte[] Source, int Offset)
        {
            return Source[Offset] | (Source[Offset + 1] << 8) | (Source[Offset + 2] << 16) | (Source[Offset + 3] << 24);
        }

        private static ushort ReadUInt16(byte[] Source, int Offset)
        {
            return (ushort)(Source[Offset] | (Source[Offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] Source, int Offset)
        {
            return (uint)ReadInt32(Source, Offset);
        }
    }
}
