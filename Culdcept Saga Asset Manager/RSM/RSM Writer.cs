using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSM
{
    public class RSMWriter
    {
        private const int RsmIdentityOffset = 8;
        private const int RsmHeaderSize = 12;
        private const int MaterialNameSize = 32;
        private const int MaterialRecordSize = 56;
        private const int NodeRecordSize = 48;

        public static void WriteRsm(BigEndianBinaryWriter DestinationWriter, RsmDocument ModelToWrite)
        {
            DestinationWriter.BaseStream.Seek(0, SeekOrigin.Begin);

            DestinationWriter.Write(new byte[8]
            {
                (byte)'r', (byte)'s', (byte) 'm', 0,
                0, 0, 0, 1
            });

            // 1. Write Identity (Offset 8)
            DestinationWriter.BaseStream.Seek(RsmIdentityOffset, SeekOrigin.Begin);
            DestinationWriter.WriteUInt16BigEndian((ushort)ModelToWrite.ListMaterial.Count);
            DestinationWriter.WriteUInt16BigEndian((ushort)ModelToWrite.ListNode.Count);

            // 2. Calculate and update Geometry Offsets for all nodes before writing tables
            uint CurrentGeometryOffset = (uint)(RsmHeaderSize +
                (ModelToWrite.ListMaterial.Count * MaterialRecordSize) +
                (ModelToWrite.ListNode.Count * NodeRecordSize));

            for (int NodeIndex = 0; NodeIndex < ModelToWrite.ListNode.Count; NodeIndex++)
            {
                if (ModelToWrite.DicMeshByNodeIndex.ContainsKey(NodeIndex) && ModelToWrite.DicMeshByNodeIndex[NodeIndex] != null)
                {
                    ModelToWrite.ListNode[NodeIndex].GeometryOffset = CurrentGeometryOffset;
                    CurrentGeometryOffset += CalculateGeometrySize(ModelToWrite.DicMeshByNodeIndex[NodeIndex]);
                }
                else
                {
                    ModelToWrite.ListNode[NodeIndex].GeometryOffset = 0;
                }
            }

            // 3. Write Materials (Starts at RsmHeaderSize = 12)
            DestinationWriter.BaseStream.Seek(RsmHeaderSize, SeekOrigin.Begin);
            for (int MaterialIndex = 0; MaterialIndex < ModelToWrite.ListMaterial.Count; MaterialIndex++)
            {
                WriteMaterialRecord(DestinationWriter, ModelToWrite.ListMaterial[MaterialIndex]);
            }

            // 4. Write Nodes
            for (int NodeIndex = 0; NodeIndex < ModelToWrite.ListNode.Count; NodeIndex++)
            {
                WriteNode(DestinationWriter, ModelToWrite.ListNode[NodeIndex]);
            }

            // 5. Write Geometry for each active node
            for (int NodeIndex = 0; NodeIndex < ModelToWrite.ListNode.Count; NodeIndex++)
            {
                if (ModelToWrite.DicMeshByNodeIndex.TryGetValue(NodeIndex, out RsmMesh ActiveMesh) && ActiveMesh != null)
                {
                    WriteGeometry(DestinationWriter, ActiveMesh, ModelToWrite.ListNode[NodeIndex].GeometryOffset, ModelToWrite.ListNode[NodeIndex].GeometryPadding);
                }
            }
        }

        private static void WriteNode(BigEndianBinaryWriter DestinationWriter, RsmNode NoteToWrite)
        {
            DestinationWriter.WriteUInt32BigEndian(NoteToWrite.GeometryOffset);
            DestinationWriter.WriteInt16BigEndian(NoteToWrite.ChildIndex);
            DestinationWriter.WriteInt16BigEndian(NoteToWrite.SiblingIndex);

            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Translation.X);
            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Translation.Y);
            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Translation.Z);

            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Rotation.X);
            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Rotation.Y);
            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Rotation.Z);
            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Rotation.W);

            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Scale.X);
            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Scale.Y);
            DestinationWriter.WriteSingleBigEndian(NoteToWrite.Scale.Z);
        }

        private static void WriteMaterialRecord(BigEndianBinaryWriter DestinationWriter, RsmMaterial MaterialToWrite)
        {
            byte[] MaterialNameBuffer = new byte[MaterialNameSize];
            if (!string.IsNullOrEmpty(MaterialToWrite.Name))
            {
                byte[] ASCIIMaterialName = Encoding.ASCII.GetBytes(MaterialToWrite.Name);
                Array.Copy(ASCIIMaterialName, MaterialNameBuffer, Math.Min(ASCIIMaterialName.Length, MaterialNameSize));
            }
            DestinationWriter.Write(MaterialNameBuffer);

            // Width and Height are Little-Endian
            DestinationWriter.Write(MaterialToWrite.TextureWidth);
            DestinationWriter.Write(MaterialToWrite.TextureHeight);

            DestinationWriter.WriteUInt32BigEndian(MaterialToWrite.RenderMode);
            DestinationWriter.WriteSingleBigEndian(MaterialToWrite.Opacity);
            DestinationWriter.WriteSingleBigEndian(MaterialToWrite.SelfIllumination);
            DestinationWriter.WriteSingleBigEndian(MaterialToWrite.UvScroll.X);
            DestinationWriter.WriteSingleBigEndian(MaterialToWrite.UvScroll.Y);
        }

        private static void WriteGeometry(BigEndianBinaryWriter DestinationWriter, RsmMesh MeshToWrite, uint GeometryOffset, uint GeometryPadding)
        {
            DestinationWriter.BaseStream.Position = GeometryOffset;

            int PositionCount = 0;
            if (MeshToWrite.ListPosition != null)
            {
                PositionCount = MeshToWrite.ListPosition.Count;
            }

            int NormalCount = 0;
            if (MeshToWrite.ListNormal != null)
            {
                NormalCount = MeshToWrite.ListNormal.Count;
            }

            int ColorCount = 0;
            if (MeshToWrite.ListColor != null)
            {
                ColorCount = MeshToWrite.ListColor.Count;
            }

            int UvCount = 0;
            if (MeshToWrite.ListUV != null)
            {
                UvCount = MeshToWrite.ListUV.Count;
            }

            ushort ListPositionCount = (ushort)PositionCount;
            ushort ListNormalCount = (ushort)NormalCount;
            ushort ListColorCount = (ushort)ColorCount;
            ushort ListUVCount = (ushort)UvCount;

            // Calculate total influence entries across all groups
            ushort ListSkinPositionInfluenceCount = 0;
            if (MeshToWrite.IsSkinned && MeshToWrite.ListSkinPositionInfluenceGroup != null)
            {
                foreach (RsmInfluenceGroup ActiveSkinPositionInfluence in MeshToWrite.ListSkinPositionInfluenceGroup)
                {
                    ListSkinPositionInfluenceCount += (ushort)ActiveSkinPositionInfluence.ListInfluence.Count;
                }
            }

            ushort ListSkinNormalInfluenceCount = 0;
            if (MeshToWrite.IsSkinned && MeshToWrite.ListSkinNormalInfluenceGroup != null)
            {
                foreach (RsmInfluenceGroup ActiveSkinNormalInfluence in MeshToWrite.ListSkinNormalInfluenceGroup)
                {
                    ListSkinNormalInfluenceCount += (ushort)ActiveSkinNormalInfluence.ListInfluence.Count;
                }
            }

            int MeshGroupCount = 0;
            if (MeshToWrite.ListMeshGroup != null)
            {
                MeshGroupCount = MeshToWrite.ListMeshGroup.Count;
            }

            ushort ListMeshGroupCount = (ushort)MeshGroupCount;

            // Write Geometry Header (16 bytes)
            DestinationWriter.WriteUInt16BigEndian(ListPositionCount);
            DestinationWriter.WriteUInt16BigEndian(ListNormalCount);
            DestinationWriter.WriteUInt16BigEndian(ListColorCount);
            DestinationWriter.WriteUInt16BigEndian(ListUVCount);
            DestinationWriter.WriteUInt16BigEndian(ListSkinPositionInfluenceCount);
            DestinationWriter.WriteUInt16BigEndian(ListSkinNormalInfluenceCount);
            DestinationWriter.WriteUInt16BigEndian(ListMeshGroupCount);
            DestinationWriter.WriteUInt16BigEndian((ushort)GeometryPadding);

            if (ListPositionCount == 0 && ListNormalCount == 0 && ListColorCount == 0 && ListUVCount == 0)
            {
                return;
            }

            long CurrentWritingPositon = GeometryOffset + 16;

            // 1. Write Positions
            DestinationWriter.BaseStream.Position = CurrentWritingPositon;
            for (int PositionIndex = 0; PositionIndex < ListPositionCount; PositionIndex++)
            {
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListPosition[PositionIndex].X);
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListPosition[PositionIndex].Y);
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListPosition[PositionIndex].Z);
            }
            CurrentWritingPositon += ListPositionCount * 12;

            // 2. Write Normals
            DestinationWriter.BaseStream.Position = CurrentWritingPositon;
            for (int NormalIndex = 0; NormalIndex < ListNormalCount; NormalIndex++)
            {
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListNormal[NormalIndex].X);
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListNormal[NormalIndex].Y);
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListNormal[NormalIndex].Z);
            }
            CurrentWritingPositon += ListNormalCount * 12;

            // 3. Write Colors (RGBA uint8)
            DestinationWriter.BaseStream.Position = CurrentWritingPositon;
            for (int ColorIndex = 0; ColorIndex < ListColorCount; ColorIndex++)
            {
                DestinationWriter.Write(MeshToWrite.ListColor[ColorIndex]);
            }
            CurrentWritingPositon += ListColorCount * 4;

            // 4. Write UVs
            DestinationWriter.BaseStream.Position = CurrentWritingPositon;
            for (int UvIndex = 0; UvIndex < ListUVCount; UvIndex++)
            {
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListUV[UvIndex].X);
                DestinationWriter.WriteSingleBigEndian(MeshToWrite.ListUV[UvIndex].Y);
            }
            CurrentWritingPositon += ListUVCount * 8;

            // 5. Write Skin Influences
            long WriterOffset = CurrentWritingPositon;
            CurrentWritingPositon += 12 * (ListSkinPositionInfluenceCount + ListSkinNormalInfluenceCount);

            if (MeshToWrite.IsSkinned && ListSkinPositionInfluenceCount > 0 && ListSkinNormalInfluenceCount > 0)
            {
                WriteInfluences(DestinationWriter, MeshToWrite.ListSkinPositionInfluenceGroup, WriterOffset);
                WriteInfluences(DestinationWriter, MeshToWrite.ListSkinNormalInfluenceGroup, WriterOffset + (12 * ListSkinPositionInfluenceCount));
            }

            // 6. Write Groups & Corners
            for (int GroupIndex = 0; GroupIndex < ListMeshGroupCount; GroupIndex++)
            {
                RsmMeshGroup ActiveMeshGroup = MeshToWrite.ListMeshGroup[GroupIndex];
                DestinationWriter.BaseStream.Position = CurrentWritingPositon;

                DestinationWriter.WriteUInt32BigEndian(ActiveMeshGroup.VertexFormat);
                DestinationWriter.WriteUInt16BigEndian(ActiveMeshGroup.PrimitiveType);
                DestinationWriter.WriteUInt16BigEndian(ActiveMeshGroup.MaterialIndex);

                int CornerCount = 0;
                if (ActiveMeshGroup.ArrayCorner != null)
                {
                    CornerCount = ActiveMeshGroup.ArrayCorner.Length;
                }

                ushort ArrayCornerCount = (ushort)CornerCount;
                DestinationWriter.WriteUInt16BigEndian(ArrayCornerCount);
                DestinationWriter.WriteUInt16BigEndian(ActiveMeshGroup.Unknown10);
                CurrentWritingPositon += 12; // Group header

                DestinationWriter.BaseStream.Position = CurrentWritingPositon;
                for (int CornerIndex = 0; CornerIndex < ArrayCornerCount; CornerIndex++)
                {
                    DestinationWriter.WriteUInt16BigEndian(ActiveMeshGroup.ArrayCorner[CornerIndex].PositionId);
                    DestinationWriter.WriteUInt16BigEndian(ActiveMeshGroup.ArrayCorner[CornerIndex].NormalId);
                    DestinationWriter.WriteUInt16BigEndian(ActiveMeshGroup.ArrayCorner[CornerIndex].ColorId);
                    DestinationWriter.WriteUInt16BigEndian(ActiveMeshGroup.ArrayCorner[CornerIndex].UvId);
                }

                CurrentWritingPositon += ArrayCornerCount * 8;
            }
        }

        private static void WriteInfluences(BigEndianBinaryWriter DestinationWriter, List<RsmInfluenceGroup> ListInfluenceGroup, long WriterOffset)
        {
            int CurrentOffset = 0;

            foreach (RsmInfluenceGroup ActiveGroup in ListInfluenceGroup)
            {
                ushort ListInfluenceCount = (ushort)ActiveGroup.ListInfluence.Count;

                for (int InfluenceIndex = 0; InfluenceIndex < ListInfluenceCount; InfluenceIndex++)
                {
                    DestinationWriter.BaseStream.Position = WriterOffset + ((CurrentOffset + InfluenceIndex) * 12);

                    RsmInfluence ActiveInfluence = ActiveGroup.ListInfluence[InfluenceIndex];
                    DestinationWriter.WriteUInt16BigEndian(ActiveInfluence.VertexId);
                    DestinationWriter.WriteUInt16BigEndian(ActiveInfluence.BoneIndex);

                    // Offset 4 within 12-byte slot is the influence count marker for the first item
                    if (InfluenceIndex == 0)
                    {
                        DestinationWriter.WriteUInt16BigEndian(ListInfluenceCount);
                        DestinationWriter.BaseStream.Position += 2; // Pad remaining 2 bytes
                    }
                    else
                    {
                        DestinationWriter.BaseStream.Position += 4;
                    }

                    DestinationWriter.WriteSingleBigEndian(ActiveInfluence.Weight);
                }

                CurrentOffset += ListInfluenceCount;
            }
        }

        private static uint CalculateGeometrySize(RsmMesh MeshToWrite)
        {
            if (MeshToWrite == null)
            {
                return 0;
            }

            uint GeometrySize = 16; // Header
            if (MeshToWrite.ListPosition != null)
            {
                GeometrySize += (uint)(MeshToWrite.ListPosition.Count * 12);
            }

            if (MeshToWrite.ListNormal != null)
            {
                GeometrySize += (uint)(MeshToWrite.ListNormal.Count * 12);
            }

            if (MeshToWrite.ListColor != null)
            {
                GeometrySize += (uint)(MeshToWrite.ListColor.Count * 4);
            }

            if (MeshToWrite.ListUV != null)
            {
                GeometrySize += (uint)(MeshToWrite.ListUV.Count * 8);
            }

            if (MeshToWrite.IsSkinned)
            {
                int ListSkinPositionInfluenceCount = 0;
                if (MeshToWrite.ListSkinPositionInfluenceGroup != null)
                {
                    foreach (RsmInfluenceGroup ActiveInfluenceGroup in MeshToWrite.ListSkinPositionInfluenceGroup)
                    {
                        ListSkinPositionInfluenceCount += ActiveInfluenceGroup.ListInfluence.Count;
                    }
                }

                int ListSkinNormalInfluenceCount = 0;
                if (MeshToWrite.ListSkinNormalInfluenceGroup != null)
                {
                    foreach (RsmInfluenceGroup ActiveInfluenceGroup in MeshToWrite.ListSkinNormalInfluenceGroup)
                    {
                        ListSkinNormalInfluenceCount += ActiveInfluenceGroup.ListInfluence.Count;
                    }
                }

                GeometrySize += (uint)(12 * (ListSkinPositionInfluenceCount + ListSkinNormalInfluenceCount));
            }

            if (MeshToWrite.ListMeshGroup != null)
            {
                foreach (RsmMeshGroup ActiveMeshGroup in MeshToWrite.ListMeshGroup)
                {
                    GeometrySize += 12; // Group Header
                    if (ActiveMeshGroup.ArrayCorner != null)
                    {
                        GeometrySize += (uint)(ActiveMeshGroup.ArrayCorner.Length * 8);
                    }
                }
            }

            return GeometrySize;
        }
    }
}
