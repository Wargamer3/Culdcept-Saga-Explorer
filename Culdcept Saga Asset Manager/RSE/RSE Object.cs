using System.Numerics;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSE
{
    public sealed class RseObject
    {
        public readonly Dictionary<int, SortedDictionary<int, float>> DicMaterialOpacity = new Dictionary<int, SortedDictionary<int, float>>();
        public readonly Dictionary<int, SortedDictionary<int, Vector2>> DicMaterialUv = new Dictionary<int, SortedDictionary<int, Vector2>>();
        public readonly SortedDictionary<int, Vector3> DicGeneratorPosition = new SortedDictionary<int, Vector3>();
        public readonly SortedDictionary<int, Vector3> DicGeneratorScale = new SortedDictionary<int, Vector3>();
        public readonly SortedDictionary<int, Quaternion> DicRotation = new SortedDictionary<int, Quaternion>();
        public readonly Dictionary<int, float> DicSetupOpacity = new Dictionary<int, float>();
        public readonly SortedDictionary<int, float> DicObjectOpacity = new SortedDictionary<int, float>();
        public readonly List<RseParticleFrame> ListParticleFrame = new List<RseParticleFrame>();

        public int Id;
        public RseObjectKind Kind;
        public string FileName;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Scale = Vector3.One;
        public int? MotionId;
        public int MotionStartFrame;
        public bool MotionLoops;
    }
}
