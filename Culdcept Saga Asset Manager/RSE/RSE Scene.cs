using System.Linq;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSE
{
    public sealed class RseScene
    {
        public readonly Dictionary<int, RseObject> DicObject = new Dictionary<int, RseObject>();
        public readonly Dictionary<int, string> DicMotion = new Dictionary<int, string>();
        public readonly List<RseParticleGenerator> ListParticleGenerator = new List<RseParticleGenerator>();
        public readonly List<RseIgnoredDummyCommand> ListIgnoredDummyCommand = new List<RseIgnoredDummyCommand>();
        public readonly List<RseRejectedMotionCommand> ListRejectedMotionCommand = new List<RseRejectedMotionCommand>();
        public readonly List<RseNativeMotionTransition> ListNativeMotionTransition = new List<RseNativeMotionTransition>();
        public readonly List<RseUnmatchedSetupCommand> ListUnmatchedSetupCommand = new List<RseUnmatchedSetupCommand>();
        public readonly Dictionary<int, string> DicIgnoredModelDeclaration = new Dictionary<int, string>();
        public readonly Dictionary<int, string> DicEffect = new Dictionary<int, string>();
        public readonly List<RseEffectCommand> ListEffectCommand = new List<RseEffectCommand>();
        public readonly List<RseRawOpacityCommand> ListRawOpacityCommand = new List<RseRawOpacityCommand>();

        public int TotalFrames;
        public int? SceneNumber;
        public bool AutoTexture;
        public bool? Loop;
        public bool HasSoundCues;

        public int ObjectId => DicObject.Values.First(Object => Object.Kind == RseObjectKind.Model).Id;
        public string ModelFileName => DicObject[ObjectId].FileName;
        public bool IsEmpty => DicObject.Count == 0 && DicIgnoredModelDeclaration.Count == 0;
        public bool HasRenderableObjects => DicObject.Values.Any(Object => Object.Kind == RseObjectKind.Model)
            || ListParticleGenerator.Any(Generator => Generator.HasBoundModel) || ListEffectCommand.Count > 0;
        public IReadOnlyDictionary<int, SortedDictionary<int, float>> MaterialOpacity => DicObject[ObjectId].DicMaterialOpacity;
        public IReadOnlyDictionary<int, float> SetupOpacity => DicObject[ObjectId].DicSetupOpacity;
    }
}
