using System.IO;
using System.Buffers.Binary;

namespace CuldceptSagaExplorer.AssetManager
{
    public class BigEndianBinaryWriter : BinaryWriter
    {
        public BigEndianBinaryWriter(Stream DestinationStream) : base(DestinationStream) { }

        public void WriteInt16BigEndian(short Value)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(Value));
        }

        public void WriteUInt16BigEndian(ushort Value)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(Value));
        }

        public void WriteInt32BigEndian(int Value)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(Value));
        }

        public void WriteUInt32BigEndian(uint Value)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(Value));
        }

        public void WriteSingleBigEndian(float Value)
        {
            int RawInt = System.BitConverter.SingleToInt32Bits(Value);
            base.Write(BinaryPrimitives.ReverseEndianness(RawInt));
        }
    }
}
