using System.Numerics;

namespace CuldceptSagaExplorer.AssetManager.RSE
{
    public sealed class RseParticleGenerator
    {
        public int ObjectId = -1;
        public int FirstParticle = -1;
        public int LastParticle = -1;
        public int ModelObjectId = -1;
        public string ModelFileName;
        public bool HasModelReference => ModelFileName != null || ModelObjectId >= 0;
        public bool IsModelObjectMissing;
        public bool HasBoundModel => HasModelReference && !IsModelObjectMissing;
        public int? IgnoredRsmStart;
        public int StartOpacity;
        public int MiddleOpacity;
        public int EndOpacity;
        public int FadeIn;
        public int FadeOut;
        public int? Seed;
        public int Spin;
        public Vector3 Angle;
        public Vector3 AngleRange;
        public Vector3 SpinVelocity;
        public Vector3 SpinVelocityRange;
    }
}
