using System;
using System.IO;
using System.Text;
using System.Buffers.Binary;

namespace CuldceptSagaExplorer.AssetManager
{
    internal sealed class EndianBinaryReader : BinaryReader
    {
        private readonly bool LittleEndian;

        public EndianBinaryReader(Stream Stream, bool LittleEndian) : base(Stream, Encoding.ASCII, leaveOpen: true)
        {
            this.LittleEndian = LittleEndian;
        }

        public override ushort ReadUInt16()
        {
            return LittleEndian ? base.ReadUInt16() : BinaryPrimitives.ReverseEndianness(base.ReadUInt16());
        }

        public override short ReadInt16()
        {
            return unchecked((short)ReadUInt16());
        }

        public override uint ReadUInt32()
        {
            return LittleEndian ? base.ReadUInt32() : BinaryPrimitives.ReverseEndianness(base.ReadUInt32());
        }

        public override float ReadSingle()
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32()));
        }

        public ushort ReadUInt16LittleEndian()
        {
            return base.ReadUInt16();
        }
    }
}
