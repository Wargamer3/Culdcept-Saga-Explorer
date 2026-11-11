using System;
using System.Numerics;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSM
{
    /// <summary>
    /// Represent a RSM file, which is a 3D model format used in the game Culdcept Saga. This class contains lists of materials, nodes, and meshes, as well as transformation matrices for the model's hierarchy.
    /// Probably stands for Ressource Model
    /// </summary>
    public class RsmDocument
    {
        public List<RsmMaterial> ListMaterial = new List<RsmMaterial>();
        public List<RsmNode> ListNode = new List<RsmNode>();
        public Dictionary<int, RsmMesh> DicMeshByNodeIndex = new Dictionary<int, RsmMesh>();

        public int[] ArrayParentIndex = Array.Empty<int>();
        public Matrix4x4[] ArrayLocalMatrix = Array.Empty<Matrix4x4>();
        public Matrix4x4[] ArrayWorldMatrix = Array.Empty<Matrix4x4>();
    }

    public class RsmMaterial
    {
        public string Name = string.Empty;
        public ushort TextureWidth;
        public ushort TextureHeight;
        public uint RenderMode;
        public float Opacity;

        /// <summary>shader.fx SelfIllum, uploaded just after opacity. 0 is fully lit and 1 keeps the vertex colour.</summary>
        public float SelfIllumination;
        public Vector2 UvScroll;
    }

    public class RsmNode
    {
        public uint GeometryOffset;
        public short ChildIndex;
        public short SiblingIndex;
        public Vector3 Translation;
        public Quaternion Rotation;
        public Vector3 Scale;
        public uint GeometryPadding;
    }

    public class RsmMesh
    {
        public List<Vector3> ListPosition = new List<Vector3>();
        public List<Vector3> ListNormal = new List<Vector3>();
        public List<Vector2> ListUV = new List<Vector2>();
        public List<byte[]> ListColor = new List<byte[]>();

        public bool IsSkinned;
        public List<RsmInfluenceGroup> ListSkinPositionInfluenceGroup = new List<RsmInfluenceGroup>();
        public List<RsmInfluenceGroup> ListSkinNormalInfluenceGroup = new List<RsmInfluenceGroup>();

        public List<RsmMeshGroup> ListMeshGroup = new List<RsmMeshGroup>();
    }

    public class RsmMeshGroup
    {
        public const ushort Points = 0;
        public const ushort LineList = 1;
        public const ushort LineStrip = 2;
        public const ushort TriangleList = 3;
        public const ushort TriangleFan = 4;
        public const ushort TriangleStrip = 5;

        public ushort MaterialIndex;
        public RsmCorner[] ArrayCorner = Array.Empty<RsmCorner>();
        public uint VertexFormat; // channel bitmask: `0x01` position, `0x02` normal, `0x04` colour, `0x08` UV. The game only emits `0x03`/`0x07`/`0x0B`/`0x0F`

        /// <summary>Draw primitive. Shipping groups are <see cref="TriangleList"/>.</summary>
        public ushort PrimitiveType;

        /// <summary>Group-header ushort at +10. Draw never reads it. Files store many values, so the stored value is kept. New groups use 0.</summary>
        public ushort Unknown10;
    }

    public struct RsmCorner
    {
        public ushort PositionId;
        public ushort NormalId;
        public ushort ColorId;
        public ushort UvId;
    }

    public struct RsmInfluence
    {
        public ushort VertexId;
        public ushort BoneIndex;
        public float Weight;
    }

    public class RsmInfluenceGroup
    {
        public List<RsmInfluence> ListInfluence = new List<RsmInfluence>();
    }
}
