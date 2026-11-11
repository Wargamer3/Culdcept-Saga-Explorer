using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSM
{
    public class RSMReader
    {
        private const int RsmIdentityOffset = 8;
        private const int RsmHeaderSize = 12;
        private const int MaterialNameSize = 32;

        public static RsmDocument ParseRsm(BigEndianBinaryReader SourceReader)
        {
            SourceReader.BaseStream.Seek(0, SeekOrigin.Begin);
            byte[] ArrayHeaderData = SourceReader.ReadBytes(RsmIdentityOffset);
            bool LittleEndian = ArrayHeaderData.SequenceEqual(new byte[] { (byte)'R', (byte)'S', (byte)'M', 0, 0, 0, 1, 0 });
            if (!LittleEndian && !ArrayHeaderData.SequenceEqual(new byte[] { (byte)'r', (byte)'s', (byte)'m', 0, 0, 0, 0, 1 }))
            {
                throw new InvalidDataException("Invalid RSM file header.");
            }
            using EndianBinaryReader Reader = new EndianBinaryReader(SourceReader.BaseStream, LittleEndian);
            return ParseDocument(Reader);
        }

        private static RsmDocument ParseDocument(EndianBinaryReader SourceReader)
        {
            RsmDocument LoadedModel = new RsmDocument();

            // Read material count and node count at offset 8.
            SourceReader.BaseStream.Seek(RsmIdentityOffset, SeekOrigin.Begin);
            ushort ListMaterialCount = SourceReader.ReadUInt16();
            ushort ListNodeCount = SourceReader.ReadUInt16();

            // 1. Read Materials (starts at RsmHeaderSize = offset 12)
            SourceReader.BaseStream.Seek(RsmHeaderSize, SeekOrigin.Begin);
            for (int MaterialIndex = 0; MaterialIndex < ListMaterialCount; MaterialIndex++)
            {
                LoadedModel.ListMaterial.Add(ParseMaterial(SourceReader));
            }

            // 2. Read Nodes (48 bytes per record)
            for (int NodeIndex = 0; NodeIndex < ListNodeCount; NodeIndex++)
            {
                LoadedModel.ListNode.Add(ParseNode(SourceReader));
            }

            // 3. Reconstruct Hierarchy and Transformation Matrices
            BuildHierarchyAndWorldMatrices(LoadedModel.ListNode, out int[] ArrayParentIndex, out Matrix4x4[] ArrayLocalMatrix, out Matrix4x4[] ArrayWorldMatrix);
            LoadedModel.ArrayParentIndex = ArrayParentIndex;
            LoadedModel.ArrayLocalMatrix = ArrayLocalMatrix;
            LoadedModel.ArrayWorldMatrix = ArrayWorldMatrix;

            // 4. Parse Geometry for each node
            for (int NodeIndex = 0; NodeIndex < LoadedModel.ListNode.Count; NodeIndex++)
            {
                uint GeometryOffset = LoadedModel.ListNode[NodeIndex].GeometryOffset;

                if (GeometryOffset > 0)
                {
                    ushort GeometryPadding;

                    RsmMesh LoadedMesh = ParseMesh(SourceReader, GeometryOffset, out GeometryPadding);

                    if (LoadedMesh != null)
                    {
                        LoadedModel.ListNode[NodeIndex].GeometryPadding = GeometryPadding;
                        LoadedModel.DicMeshByNodeIndex[NodeIndex] = LoadedMesh;
                    }
                }
            }

            return LoadedModel;
        }

        private static RsmNode ParseNode(EndianBinaryReader SourceReader)
        {
            uint GeometryOffset = SourceReader.ReadUInt32();
            short ChildIndex = SourceReader.ReadInt16();
            short Sibling = SourceReader.ReadInt16();

            Vector3 Translation = new Vector3(
                SourceReader.ReadSingle(),
                SourceReader.ReadSingle(),
                SourceReader.ReadSingle()
            );

            Quaternion Rotation = new Quaternion(
                SourceReader.ReadSingle(),
                SourceReader.ReadSingle(),
                SourceReader.ReadSingle(),
                SourceReader.ReadSingle()
            );

            Vector3 Scale = new Vector3(
                SourceReader.ReadSingle(),
                SourceReader.ReadSingle(),
                SourceReader.ReadSingle()
            );

            return new RsmNode
            {
                GeometryOffset = GeometryOffset,
                ChildIndex = ChildIndex,
                SiblingIndex = Sibling,
                Translation = Translation,
                Rotation = Rotation,
                Scale = Scale
            };
        }

        private static RsmMaterial ParseMaterial(EndianBinaryReader SourceReader)
        {
            byte[] MaterialNameBuffer = SourceReader.ReadBytes(MaterialNameSize);
            string MaterialName = Encoding.ASCII.GetString(MaterialNameBuffer).Split('\0')[0];

            ushort TextureWidth = SourceReader.ReadUInt16LittleEndian();
            ushort TextureHeight = SourceReader.ReadUInt16LittleEndian();

            uint RenderMode = SourceReader.ReadUInt32();
            float Opacity = SourceReader.ReadSingle();
            float SelfIllumination = SourceReader.ReadSingle();

            float UvScrollX = SourceReader.ReadSingle();
            float UvScrollY = SourceReader.ReadSingle();

            return new RsmMaterial
            {
                Name = MaterialName,
                TextureWidth = TextureWidth,
                TextureHeight = TextureHeight,
                RenderMode = RenderMode,
                Opacity = Opacity,
                SelfIllumination = SelfIllumination,
                UvScroll = new Vector2(UvScrollX, UvScrollY)
            };
        }

        private static RsmMesh ParseMesh(EndianBinaryReader SourceReader, uint GeometryOffset, out ushort GeometryPadding)
        {
            SourceReader.BaseStream.Position = GeometryOffset;

            ushort ListPositionCount = SourceReader.ReadUInt16();
            ushort ListNormalCount = SourceReader.ReadUInt16();
            ushort ListColorCount = SourceReader.ReadUInt16();
            ushort ListUVCount = SourceReader.ReadUInt16();
            ushort ListSkinPositionInfluenceCount = SourceReader.ReadUInt16();
            ushort ListSkinNormalInfluenceCount = SourceReader.ReadUInt16();
            ushort ListMeshGroupCount = SourceReader.ReadUInt16();
            GeometryPadding = SourceReader.ReadUInt16();

            // A non-zero GeometryOffset may still point at an all-zero geometry header.
            // The game keeps that 16-byte block (and its padding), so preserve it as an empty
            // mesh instead of returning null; returning null makes WriteRsm drop the block and
            // shrink the file, breaking the round trip.
            if (ListPositionCount == 0 && ListNormalCount == 0 && ListColorCount == 0 && ListUVCount == 0)
            {
                return new RsmMesh();
            }

            RsmMesh LoadedMesh = new RsmMesh();
            long CurrentReadingPositon = GeometryOffset + 16; // Skip 16-byte header

            // 1. Read Positions
            SourceReader.BaseStream.Position = CurrentReadingPositon;
            LoadedMesh.ListPosition = new List<Vector3>(ListPositionCount);
            for (int PositionIndex = 0; PositionIndex < ListPositionCount; PositionIndex++)
            {
                LoadedMesh.ListPosition.Add(new Vector3(SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle()));
            }

            CurrentReadingPositon += ListPositionCount * 12;
            SourceReader.BaseStream.Position = CurrentReadingPositon;

            // 2. Read Normals
            LoadedMesh.ListNormal = new List<Vector3>(ListNormalCount);
            for (int NormalIndex = 0; NormalIndex < ListNormalCount; NormalIndex++)
            {
                LoadedMesh.ListNormal.Add(new Vector3(SourceReader.ReadSingle(), SourceReader.ReadSingle(), SourceReader.ReadSingle()));
            }

            CurrentReadingPositon += ListNormalCount * 12;
            SourceReader.BaseStream.Position = CurrentReadingPositon;

            // 3. Read Colors (RGBA uint8)
            LoadedMesh.ListColor = new List<byte[]>(ListColorCount);
            for (int ColorIndex = 0; ColorIndex < ListColorCount; ColorIndex++)
            {
                LoadedMesh.ListColor.Add(SourceReader.ReadBytes(4));
            }

            CurrentReadingPositon += ListColorCount * 4;
            SourceReader.BaseStream.Position = CurrentReadingPositon;

            // 4. Read UVs
            LoadedMesh.ListUV = new List<Vector2>(ListUVCount);
            for (int UvIndex = 0; UvIndex < ListUVCount; UvIndex++)
            {
                LoadedMesh.ListUV.Add(new Vector2(SourceReader.ReadSingle(), SourceReader.ReadSingle()));
            }

            CurrentReadingPositon += ListUVCount * 8;

            // 5. Read Skin Influences
            long SkinOffset = CurrentReadingPositon;
            CurrentReadingPositon += 12 * (ListSkinPositionInfluenceCount + ListSkinNormalInfluenceCount);

            if (ListSkinPositionInfluenceCount == ListPositionCount && ListPositionCount > 0 && ListSkinNormalInfluenceCount == ListNormalCount)
            {
                List<RsmInfluenceGroup> ListSkinPositionInfluence = ReadInfluences(SourceReader, SkinOffset, ListSkinPositionInfluenceCount);
                List<RsmInfluenceGroup> ListSkinNormalInfluence = ReadInfluences(SourceReader, SkinOffset + (12 * ListSkinPositionInfluenceCount), ListSkinNormalInfluenceCount);

                if (ListSkinPositionInfluence != null && ListSkinNormalInfluence != null)
                {
                    LoadedMesh.IsSkinned = true;
                    LoadedMesh.ListSkinPositionInfluenceGroup = ListSkinPositionInfluence;
                    LoadedMesh.ListSkinNormalInfluenceGroup = ListSkinNormalInfluence;
                }
            }

            // 6. Read Groups & Corners
            for (int GroupIndex = 0; GroupIndex < ListMeshGroupCount; GroupIndex++)
            {
                SourceReader.BaseStream.Position = CurrentReadingPositon;
                uint VertexFormat = SourceReader.ReadUInt32();
                ushort PrimitiveType = SourceReader.ReadUInt16();
                ushort MaterialIndex = SourceReader.ReadUInt16();
                ushort ArrayCornerCount = SourceReader.ReadUInt16();
                ushort Unknown10 = SourceReader.ReadUInt16();
                CurrentReadingPositon += 12; // Group header size

                SourceReader.BaseStream.Position = CurrentReadingPositon;
                RsmCorner[] ArrayCorner = new RsmCorner[ArrayCornerCount];
                for (int CornerIndex = 0; CornerIndex < ArrayCornerCount; CornerIndex++)
                {
                    ArrayCorner[CornerIndex] = new RsmCorner
                    {
                        PositionId = SourceReader.ReadUInt16(),
                        NormalId = SourceReader.ReadUInt16(),
                        ColorId = SourceReader.ReadUInt16(),
                        UvId = SourceReader.ReadUInt16()
                    };
                }
                CurrentReadingPositon += ArrayCornerCount * 8; // 4 * uint16

                if (ArrayCornerCount / 3 > 0)
                {
                    LoadedMesh.ListMeshGroup.Add(new RsmMeshGroup
                    {
                        MaterialIndex = MaterialIndex,
                        ArrayCorner = ArrayCorner,
                        VertexFormat = VertexFormat,
                        PrimitiveType = PrimitiveType,
                        Unknown10 = Unknown10
                    });
                }
            }

            return LoadedMesh;
        }

        private static List<RsmInfluenceGroup> ReadInfluences(EndianBinaryReader SourceReader, long ReaderOffset, ushort ListInfluenceGroupCount)
        {
            List<RsmInfluenceGroup> ListInfluenceGroup = new List<RsmInfluenceGroup>();
            int InfluenceGroupIndex = 0;

            while (InfluenceGroupIndex < ListInfluenceGroupCount)
            {
                SourceReader.BaseStream.Position = ReaderOffset + (InfluenceGroupIndex * 12) + 4;
                ushort ListInfluenceCount = SourceReader.ReadUInt16();

                if (ListInfluenceCount < 1 || InfluenceGroupIndex + ListInfluenceCount > ListInfluenceGroupCount)
                {
                    return null;
                }

                RsmInfluenceGroup LoadedInfluenceGroup = new RsmInfluenceGroup();

                for (int InfluenceIndex = 0; InfluenceIndex < ListInfluenceCount; InfluenceIndex++)
                {
                    SourceReader.BaseStream.Position = ReaderOffset + ((InfluenceGroupIndex + InfluenceIndex) * 12);
                    ushort VertexId = SourceReader.ReadUInt16();
                    ushort BoneIndex = SourceReader.ReadUInt16();
                    SourceReader.BaseStream.Position += 4;
                    float Weight = SourceReader.ReadSingle();

                    LoadedInfluenceGroup.ListInfluence.Add(new RsmInfluence { VertexId = VertexId, BoneIndex = BoneIndex, Weight = Weight });
                }

                ListInfluenceGroup.Add(LoadedInfluenceGroup);

                InfluenceGroupIndex += ListInfluenceCount;
            }

            return ListInfluenceGroup;
        }

        public static Matrix4x4 CreateTransformMatrix(Quaternion Rotation, Vector3 ScaleVector, Vector3 TranslationVector)
        {
            Quaternion InverseRotation = Quaternion.Normalize(new Quaternion(-Rotation.X, -Rotation.Y, -Rotation.Z, Rotation.W));
            Matrix4x4 RotationMatrix = Matrix4x4.CreateFromQuaternion(InverseRotation);
            Matrix4x4 ScaleMatrix = Matrix4x4.CreateScale(ScaleVector);

            Matrix4x4 FinalMatrix = ScaleMatrix * RotationMatrix;

            FinalMatrix.M41 = TranslationVector.X;
            FinalMatrix.M42 = TranslationVector.Y;
            FinalMatrix.M43 = TranslationVector.Z;

            return FinalMatrix;
        }

        public static void BuildHierarchyAndWorldMatrices(List<RsmNode> ListNode, out int[] ArrayParentIndex, out Matrix4x4[] ArrayLocalMatrix, out Matrix4x4[] ArrayWorldMatrix)
        {
            ArrayParentIndex = new int[ListNode.Count];
            Array.Fill(ArrayParentIndex, -1);

            for (int NodeIndex = 0; NodeIndex < ListNode.Count; NodeIndex++)
            {
                int ActiveChildIndex = ListNode[NodeIndex].ChildIndex;
                int Guard = 0;// Prevent infinite loops in case of malformed data

                while (ActiveChildIndex >= 0 && ActiveChildIndex < ListNode.Count && Guard < 4096)
                {
                    ArrayParentIndex[ActiveChildIndex] = NodeIndex;
                    ActiveChildIndex = ListNode[ActiveChildIndex].SiblingIndex;
                    Guard++;
                }
            }

            ArrayLocalMatrix = new Matrix4x4[ListNode.Count];
            for (int NodeIndex = 0; NodeIndex < ListNode.Count; NodeIndex++)
            {
                ArrayLocalMatrix[NodeIndex] = CreateTransformMatrix(ListNode[NodeIndex].Rotation, ListNode[NodeIndex].Scale, ListNode[NodeIndex].Translation);
            }

            ArrayWorldMatrix = CalculateWorldMatrices(ListNode.Count, ArrayParentIndex, ArrayLocalMatrix);
        }

        public static Matrix4x4[] CalculateWorldMatrices(int ListNodeCount, int[] ArrayParentIndex, Matrix4x4[] ArrayLocalMatrix)
        {
            Matrix4x4[] ArrayWorldMatrix = new Matrix4x4[ListNodeCount];
            bool[] ArrayIsCalculated = new bool[ListNodeCount];

            for (int NodeIndex = 0; NodeIndex < ListNodeCount; NodeIndex++)
            {
                AssignWorldMatrix(NodeIndex, ArrayParentIndex, ArrayLocalMatrix, ArrayWorldMatrix, ArrayIsCalculated);
            }

            return ArrayWorldMatrix;
        }

        private static Matrix4x4 AssignWorldMatrix(int NodeIndex, int[] ArrayParentIndex, Matrix4x4[] ArrayLocalMatrix, Matrix4x4[] ArrayWorldMatrix, bool[] ArrayIsCalculated)
        {
            if (ArrayIsCalculated[NodeIndex])
            {
                return ArrayWorldMatrix[NodeIndex];
            }

            int ParentIndex = ArrayParentIndex[NodeIndex];

            Matrix4x4 ParentWorldMatrix;
            if ((ParentIndex >= 0))
            {
                ParentWorldMatrix = AssignWorldMatrix(ParentIndex, ArrayParentIndex, ArrayLocalMatrix, ArrayWorldMatrix, ArrayIsCalculated);
            }
            else
            {
                ParentWorldMatrix = Matrix4x4.Identity;
            }

            // Row vectors transform as v' = v * M, so the child matrix sits to the left of its parent.
            ArrayWorldMatrix[NodeIndex] = ArrayLocalMatrix[NodeIndex] * ParentWorldMatrix;
            ArrayIsCalculated[NodeIndex] = true;

            return ArrayWorldMatrix[NodeIndex];
        }
    }
}
