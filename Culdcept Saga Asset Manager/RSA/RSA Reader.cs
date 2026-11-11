using System;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CuldceptSagaExplorer.AssetManager.RSA
{
    public class RSAReader
    {
        private const ushort RsaTableOffset = 0x20;
        private const ushort RsaFirstEntryOffset = 24;

        internal static bool IsLittleEndian(byte[] Bytes)
        {
            return Bytes.AsSpan(0, Math.Min(Bytes.Length, 8)).SequenceEqual(new byte[] { (byte)'R', (byte)'S', (byte)'A', 0, 0, 0, 1, 0 });
        }

        public static RsaClip Parse(byte[] SourceBytes, string ClipName)
        {
            if (SourceBytes == null || SourceBytes.Length < RsaTableOffset)
            {
                return null;
            }

            bool LittleEndian = IsLittleEndian(SourceBytes);
            if (!LittleEndian && !SourceBytes.Take(8).SequenceEqual(new byte[8] { (byte)'r', (byte)'s', (byte)'a', 0, 0, 0, 0, 1 }))
            {
                return null;
            }

            using (MemoryStream SourceStream = new MemoryStream(SourceBytes))
            using (EndianBinaryReader SourceReader = new EndianBinaryReader(SourceStream, LittleEndian))
            {
                SourceStream.Seek(8, SeekOrigin.Begin);
                ushort NodeCount = SourceReader.ReadUInt16();

                if (NodeCount < 1 || RsaTableOffset + 8 * (NodeCount - 1) > SourceBytes.Length)
                {
                    return null;
                }

                RsaClip LoadedClip = new RsaClip
                {
                    Name = ClipName,
                    NodeCount = NodeCount
                };

                // Read Channel Entries
                RsaChannel[] ArrayLoadedChannel = new RsaChannel[NodeCount];

                // First entry
                SourceStream.Seek(RsaFirstEntryOffset, SeekOrigin.Begin);
                ArrayLoadedChannel[0] = new RsaChannel
                {
                    Kind = (RsaChannelKind)SourceReader.ReadUInt16(),
                    Count = SourceReader.ReadUInt16(),
                    Offset = SourceReader.ReadUInt32()
                };
                // Sibling entries
                for (int ChannelIndex = 1; ChannelIndex < NodeCount; ChannelIndex++)
                {
                    SourceStream.Seek(RsaTableOffset + 8 * (ChannelIndex - 1), SeekOrigin.Begin);
                    ArrayLoadedChannel[ChannelIndex] = new RsaChannel { Kind = (RsaChannelKind)SourceReader.ReadUInt16(), Count = SourceReader.ReadUInt16(), Offset = SourceReader.ReadUInt32() };
                }

                // Read Payload Data per Channel
                for (int ChannelIndex = 0; ChannelIndex < NodeCount; ChannelIndex++)
                {
                    RsaChannel ActiveChannel = ArrayLoadedChannel[ChannelIndex];
                    RsaChannel ActiveChannelCopy = new RsaChannel
                    {
                        Kind = ActiveChannel.Kind,
                        Count = ActiveChannel.Count,
                        Offset = ActiveChannel.Offset
                    };

                    if (ActiveChannelCopy.Kind == RsaChannelKind.TransformKeys && ActiveChannelCopy.Count > 0 && ActiveChannelCopy.Offset + 48 * ActiveChannelCopy.Count <= SourceBytes.Length)
                    {
                        SourceStream.Seek(ActiveChannelCopy.Offset, SeekOrigin.Begin);
                        for (int KeyframeIndex = 0; KeyframeIndex < ActiveChannelCopy.Count; KeyframeIndex++)
                        {
                            float Flag = SourceReader.ReadSingle();
                            float Frame = SourceReader.ReadSingle();
                            Vector3 Translation = new Vector3(SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle());
                            Quaternion Rotation = new Quaternion(SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle());
                            Vector3 Scale = new Vector3(SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle());

                            ActiveChannelCopy.ListKeyframe.Add(new RsaTransformKeyframe
                            {
                                Frame = Frame,
                                Translation = Translation,
                                Rotation = Rotation,
                                Scale = Scale
                            });
                        }
                    }
                    else if (ActiveChannelCopy.Kind == RsaChannelKind.VertexFrames && ActiveChannelCopy.Count > 0 && ActiveChannelCopy.Offset + 8 <= SourceBytes.Length)
                    {
                        SourceStream.Seek(ActiveChannelCopy.Offset + 4, SeekOrigin.Begin);
                        ActiveChannelCopy.VertexCount = SourceReader.ReadUInt16();
                        ActiveChannelCopy.NormalCount = SourceReader.ReadUInt16();

                        int FrameSize = 8 + 12 * (ActiveChannelCopy.VertexCount + ActiveChannelCopy.NormalCount);

                        if (ActiveChannelCopy.Offset + ActiveChannelCopy.Count * FrameSize <= SourceBytes.Length)
                        {
                            for (int FrameIndex = 0; FrameIndex < ActiveChannelCopy.Count; FrameIndex++)
                            {
                                SourceStream.Seek(ActiveChannelCopy.Offset + FrameIndex * FrameSize, SeekOrigin.Begin);
                                float Frame = SourceReader.ReadSingle();
                                SourceReader.ReadUInt16(); // Skip counts repeat
                                SourceReader.ReadUInt16();

                                RsaVertexFrame LoadedVertexFrame = new RsaVertexFrame
                                {
                                    Frame = Frame,
                                    ArrayPosition = new Vector3[ActiveChannelCopy.VertexCount],
                                    ArrayNormal = new Vector3[ActiveChannelCopy.NormalCount]
                                };

                                for (int VertexIndex = 0; VertexIndex < ActiveChannelCopy.VertexCount; VertexIndex++)
                                {
                                    LoadedVertexFrame.ArrayPosition[VertexIndex] = new Vector3(SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle());
                                }

                                for (int NormalIndex = 0; NormalIndex < ActiveChannelCopy.NormalCount; NormalIndex++)
                                {
                                    LoadedVertexFrame.ArrayNormal[NormalIndex] = new Vector3(SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle());
                                }

                                ActiveChannelCopy.ListVertexFrame.Add(LoadedVertexFrame);
                            }
                        }
                        else
                        {
                            ActiveChannelCopy.Kind = RsaChannelKind.None;
                        }
                    }
                    else
                    {
                        ActiveChannelCopy.Kind = RsaChannelKind.None;
                    }

                    LoadedClip.ListChannel.Add(ActiveChannelCopy);
                }

                return LoadedClip;
            }
        }
    }
}
