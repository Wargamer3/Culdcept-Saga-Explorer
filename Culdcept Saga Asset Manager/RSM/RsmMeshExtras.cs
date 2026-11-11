using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSM
{
    /// <summary>Serializes an RSM mesh (geometry + skin influences + group headers) into a glTF
    /// "rsmMesh" extras block and reads it back, preserving skinned meshes verbatim.</summary>
    public static class RsmMeshExtras
    {
        public const string Key = "rsmMesh";

        public static JsonNode ToJson(RsmMesh SourceMesh)
        {
            SkinDto MeshData = new SkinDto
            {
                Positions = Flatten(SourceMesh.ListPosition),
                Normals = Flatten(SourceMesh.ListNormal),
                Uvs = Flatten(SourceMesh.ListUV),
                Colors = FlattenColors(SourceMesh.ListColor),
                PositionInfluences = ConvertInfluences(SourceMesh.ListSkinPositionInfluenceGroup),
                NormalInfluences = ConvertInfluences(SourceMesh.ListSkinNormalInfluenceGroup),
                Groups = new List<GroupDto>()
            };

            foreach (RsmMeshGroup MeshGroup in SourceMesh.ListMeshGroup)
            {
                ushort[] FlatCorners = new ushort[MeshGroup.ArrayCorner.Length * 4];
                for (int CornerIndex = 0; CornerIndex < MeshGroup.ArrayCorner.Length; CornerIndex++)
                {
                    RsmCorner Corner = MeshGroup.ArrayCorner[CornerIndex];
                    FlatCorners[CornerIndex * 4 + 0] = Corner.PositionId;
                    FlatCorners[CornerIndex * 4 + 1] = Corner.NormalId;
                    FlatCorners[CornerIndex * 4 + 2] = Corner.ColorId;
                    FlatCorners[CornerIndex * 4 + 3] = Corner.UvId;
                }
                MeshData.Groups.Add(new GroupDto
                {
                    MaterialIndex = MeshGroup.MaterialIndex,
                    VertexFormat = MeshGroup.VertexFormat,
                    Unknown4 = MeshGroup.PrimitiveType,
                    Unknown10 = MeshGroup.Unknown10,
                    Corners = FlatCorners
                });
            }

            return JsonSerializer.SerializeToNode(MeshData, new JsonSerializerOptions { IncludeFields = true });
        }

        public static RsmMesh FromJson(JsonNode SourceNode)
        {
            if (!(SourceNode is JsonObject MeshObject))
            {
                return null;
            }
            SkinDto MeshData = MeshObject.Deserialize<SkinDto>(new JsonSerializerOptions { IncludeFields = true });
            if (MeshData == null)
            {
                return null;
            }

            RsmMesh LoadedMesh = new RsmMesh
            {
                ListPosition = UnflattenVector3(MeshData.Positions),
                ListNormal = UnflattenVector3(MeshData.Normals),
                ListUV = UnflattenVector2(MeshData.Uvs),
                ListColor = UnflattenColors(MeshData.Colors),
                ListSkinPositionInfluenceGroup = ConvertInfluences(MeshData.PositionInfluences),
                ListSkinNormalInfluenceGroup = ConvertInfluences(MeshData.NormalInfluences),
                ListMeshGroup = new List<RsmMeshGroup>(),
                IsSkinned = true
            };

            foreach (GroupDto GroupData in MeshData.Groups)
            {
                RsmCorner[] ArrayCorner = new RsmCorner[GroupData.Corners.Length / 4];
                for (int CornerIndex = 0; CornerIndex < ArrayCorner.Length; CornerIndex++)
                {
                    ArrayCorner[CornerIndex] = new RsmCorner
                    {
                        PositionId = GroupData.Corners[CornerIndex * 4 + 0],
                        NormalId = GroupData.Corners[CornerIndex * 4 + 1],
                        ColorId = GroupData.Corners[CornerIndex * 4 + 2],
                        UvId = GroupData.Corners[CornerIndex * 4 + 3]
                    };
                }
                LoadedMesh.ListMeshGroup.Add(new RsmMeshGroup
                {
                    MaterialIndex = (ushort)GroupData.MaterialIndex,
                    VertexFormat = GroupData.VertexFormat,
                    PrimitiveType = GroupData.Unknown4,
                    Unknown10 = GroupData.Unknown10,
                    ArrayCorner = ArrayCorner
                });
            }

            return LoadedMesh;
        }

        private static List<List<InfluenceDto>> ConvertInfluences(List<RsmInfluenceGroup> ListInfluenceGroup)
        {
            List<List<InfluenceDto>> ListConvertedInfluence = new List<List<InfluenceDto>>(ListInfluenceGroup.Count);
            foreach (RsmInfluenceGroup InfluenceGroup in ListInfluenceGroup)
            {
                List<InfluenceDto> ListInfluence = new List<InfluenceDto>(InfluenceGroup.ListInfluence.Count);
                foreach (RsmInfluence Influence in InfluenceGroup.ListInfluence)
                {
                    ListInfluence.Add(new InfluenceDto { Vertex = Influence.VertexId, Bone = Influence.BoneIndex, Weight = Influence.Weight });
                }

                ListConvertedInfluence.Add(ListInfluence);
            }
            return ListConvertedInfluence;
        }

        private static List<RsmInfluenceGroup> ConvertInfluences(List<List<InfluenceDto>> ListDtoGroup)
        {
            List<RsmInfluenceGroup> ListInfluenceGroup = new List<RsmInfluenceGroup>(ListDtoGroup.Count);
            foreach (List<InfluenceDto> InfluenceList in ListDtoGroup)
            {
                RsmInfluenceGroup InfluenceGroup = new RsmInfluenceGroup();
                foreach (InfluenceDto Influence in InfluenceList)
                {
                    InfluenceGroup.ListInfluence.Add(new RsmInfluence { VertexId = Influence.Vertex, BoneIndex = Influence.Bone, Weight = Influence.Weight });
                }

                ListInfluenceGroup.Add(InfluenceGroup);
            }
            return ListInfluenceGroup;
        }

        private static float[] Flatten(List<Vector3> ListVector)
        {
            float[] FlatValues = new float[ListVector.Count * 3];
            for (int VectorIndex = 0; VectorIndex < ListVector.Count; VectorIndex++)
            {
                FlatValues[VectorIndex * 3 + 0] = ListVector[VectorIndex].X;
                FlatValues[VectorIndex * 3 + 1] = ListVector[VectorIndex].Y;
                FlatValues[VectorIndex * 3 + 2] = ListVector[VectorIndex].Z;
            }
            return FlatValues;
        }

        private static List<Vector3> UnflattenVector3(float[] FlatValues)
        {
            List<Vector3> ListVector = new List<Vector3>(FlatValues.Length / 3);
            for (int ValueIndex = 0; ValueIndex + 2 < FlatValues.Length; ValueIndex += 3)
            {
                ListVector.Add(new Vector3(FlatValues[ValueIndex], FlatValues[ValueIndex + 1], FlatValues[ValueIndex + 2]));
            }

            return ListVector;
        }

        private static float[] Flatten(List<Vector2> ListVector)
        {
            float[] FlatValues = new float[ListVector.Count * 2];
            for (int VectorIndex = 0; VectorIndex < ListVector.Count; VectorIndex++)
            {
                FlatValues[VectorIndex * 2 + 0] = ListVector[VectorIndex].X;
                FlatValues[VectorIndex * 2 + 1] = ListVector[VectorIndex].Y;
            }
            return FlatValues;
        }

        private static List<Vector2> UnflattenVector2(float[] FlatValues)
        {
            List<Vector2> ListVector = new List<Vector2>(FlatValues.Length / 2);
            for (int ValueIndex = 0; ValueIndex + 1 < FlatValues.Length; ValueIndex += 2)
            {
                ListVector.Add(new Vector2(FlatValues[ValueIndex], FlatValues[ValueIndex + 1]));
            }

            return ListVector;
        }

        private static byte[] FlattenColors(List<byte[]> ListColor)
        {
            if (ListColor == null || ListColor.Count == 0)
            {
                return null;
            }
            byte[] FlatColors = new byte[ListColor.Count * 4];
            for (int ColorIndex = 0; ColorIndex < ListColor.Count; ColorIndex++)
            {
                FlatColors[ColorIndex * 4 + 0] = ListColor[ColorIndex][0];
                FlatColors[ColorIndex * 4 + 1] = ListColor[ColorIndex][1];
                FlatColors[ColorIndex * 4 + 2] = ListColor[ColorIndex][2];
                FlatColors[ColorIndex * 4 + 3] = ListColor[ColorIndex][3];
            }
            return FlatColors;
        }

        private static List<byte[]> UnflattenColors(byte[] FlatColors)
        {
            List<byte[]> ListColor = new List<byte[]>();
            if (FlatColors == null || FlatColors.Length == 0)
            {
                return ListColor;
            }
            for (int ColorIndex = 0; ColorIndex + 3 < FlatColors.Length; ColorIndex += 4)
            {
                ListColor.Add(new byte[] { FlatColors[ColorIndex], FlatColors[ColorIndex + 1], FlatColors[ColorIndex + 2], FlatColors[ColorIndex + 3] });
            }

            return ListColor;
        }

        private sealed class InfluenceDto
        {
            public ushort Vertex;
            public ushort Bone;
            public float Weight;
        }

        private sealed class GroupDto
        {
            public uint MaterialIndex;
            public uint VertexFormat;

            // Snapshot key stays Unknown4 so a glTF exported before the rename still restores the primitive.
            public ushort Unknown4;
            public ushort Unknown10;
            public ushort[] Corners;
        }

        private sealed class SkinDto
        {
            public float[] Positions;
            public float[] Normals;
            public float[] Uvs;
            public byte[] Colors;
            public List<List<InfluenceDto>> PositionInfluences;
            public List<List<InfluenceDto>> NormalInfluences;
            public List<GroupDto> Groups;
        }
    }
}
