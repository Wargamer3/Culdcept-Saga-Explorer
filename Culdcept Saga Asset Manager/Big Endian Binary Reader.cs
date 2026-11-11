using System.IO;
using System.Buffers.Binary;

namespace CuldceptSagaExplorer.AssetManager
{
    public class BigEndianBinaryReader : BinaryReader
    {
        public BigEndianBinaryReader(Stream SourceStream) : base(SourceStream) { }

        public short ReadInt16BigEndian()
        {
            return BinaryPrimitives.ReverseEndianness(base.ReadInt16());
        }

        public ushort ReadUInt16BigEndian()
        {
            return BinaryPrimitives.ReverseEndianness(base.ReadUInt16());
        }

        public int ReadInt32BigEndian()
        {
            return BinaryPrimitives.ReverseEndianness(base.ReadInt32());
        }

        public uint ReadUInt32BigEndian()
        {
            return BinaryPrimitives.ReverseEndianness(base.ReadUInt32());
        }

        public float ReadSingleBigEndian()
        {
            int RawInt = BinaryPrimitives.ReverseEndianness(base.ReadInt32());
            return System.BitConverter.Int32BitsToSingle(RawInt);
        }
    }
}
