namespace CuldceptSagaExplorer.AssetManager.RST
{
    /// <summary>
    /// Represents an RST image record.
    /// </summary>
    public readonly struct RSTTextureRecord
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] RgbaBytes;

        public RSTTextureRecord(int Width, int Height, byte[] RgbaBytes)
        {
            this.Width = Width;
            this.Height = Height;
            this.RgbaBytes = RgbaBytes;
        }

        /// <summary>
        /// Indicates whether this record is a placeholder or has missing pixel data.
        /// </summary>
        public bool IsPlaceholder
        {
            get
            {
                return RgbaBytes == null;
            }
        }
    }

    public struct RstRecord
    {
        public ushort Width;
        public ushort Height;
        public ushort NameKey;
        public byte[] ArgbPixels;

        /// <summary>
        /// Texture name. When set, <see cref="RstWriter.WriteRst"/> rebuilds the name checksums
        /// from this string plus the width and height. Left empty by the reader.
        /// </summary>
        public string Name;

        /// <summary>Bytes 0..3: paired checksum of the name and the little-endian width and height.</summary>
        public uint HeaderWord;

        /// <summary>Bytes 6..7: second name checksum, sum(index * char) + 10416.</summary>
        public ushort NameKey2;

        /// <summary>
        /// Bytes 8..11. 0 = opaque, 1 = uses alpha, 4 = placeholder with no pixels.
        /// A rewrite keeps this value: it usually matches the pixels, and a few shipping images do not.
        /// </summary>
        public uint Flags;

        /// <summary>Bytes 18..19. Copied from the source record; identical images occur with different values.</summary>
        public ushort Tail;

        /// <summary>
        /// When set, the checksum uses this size instead of <see cref="Width"/> and <see cref="Height"/>.
        /// Placeholder records store 0×0 and still checksum the material's texture size.
        /// </summary>
        public bool HasChecksumSize;
        public ushort ChecksumWidth;
        public ushort ChecksumHeight;

        /// <summary>
        /// Keep <see cref="NameKey2"/> as stored. One shipping texture, book_001_04.png, is one higher than the formula.
        /// </summary>
        public bool UseStoredNameKey2;
    }

    /// <summary>
    /// Represents metadata for a single header entry extracted from an RST file.
    /// </summary>
    public readonly struct RstHeaderItem
    {
        public readonly int PixelOffset;
        public readonly int TextureWidth;
        public readonly int TextureHeight;
        public readonly ushort NameKey;
        public readonly bool IsPlaceholder;

        public RstHeaderItem(int PixelOffset, int TextureWidth, int TextureHeight, ushort NameKey, bool IsPlaceholder)
        {
            this.PixelOffset = PixelOffset;
            this.TextureWidth = TextureWidth;
            this.TextureHeight = TextureHeight;
            this.NameKey = NameKey;
            this.IsPlaceholder = IsPlaceholder;
        }

        internal void Deconstruct(out int PixelOffset, out int TextureWidth, out int TextureHeight, out ushort NameKey, out bool IsPlaceholder)
        {
            PixelOffset = this.PixelOffset;
            TextureWidth = this.TextureWidth;
            TextureHeight = this.TextureHeight;
            NameKey = this.NameKey;
            IsPlaceholder = this.IsPlaceholder;
        }
    }
}
