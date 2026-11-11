using System.Numerics;

namespace CuldceptSagaExplorer.AssetManager.RSE
{
    public enum RseObjectKind { Model, ParticleGenerator, Particle, UvAnimationDummy, EffectGenerator }

    public readonly struct RseIgnoredDummyCommand
    {
        public readonly int ObjectId;
        public readonly int Frame;
        public readonly string Command;
        public readonly string Value;

        public RseIgnoredDummyCommand(int ObjectId, int Frame, string Command, string Value)
        {
            this.ObjectId = ObjectId;
            this.Frame = Frame;
            this.Command = Command;
            this.Value = Value;
        }
    }

    public readonly struct RseRejectedMotionCommand
    {
        public readonly int Frame;
        public readonly string Value;
        public readonly string Reason;

        public RseRejectedMotionCommand(int Frame, string Value, string Reason)
        {
            this.Frame = Frame;
            this.Value = Value;
            this.Reason = Reason;
        }
    }

    public readonly struct RseNativeMotionTransition
    {
        public readonly int ObjectId;
        public readonly int Frame;
        public readonly string Value;
        public readonly int Transition;

        public RseNativeMotionTransition(int ObjectId, int Frame, string Value, int Transition)
        {
            this.ObjectId = ObjectId;
            this.Frame = Frame;
            this.Value = Value;
            this.Transition = Transition;
        }
    }

    public readonly struct RseUnmatchedSetupCommand
    {
        public readonly int ObjectId;
        public readonly int Frame;
        public readonly string Command;
        public readonly string Value;

        public RseUnmatchedSetupCommand(int ObjectId, int Frame, string Command, string Value)
        {
            this.ObjectId = ObjectId;
            this.Frame = Frame;
            this.Command = Command;
            this.Value = Value;
        }
    }

    public readonly struct RseEffectCommand
    {
        public readonly int ObjectId;
        public readonly int EffectId;
        public readonly int Frame;

        public RseEffectCommand(int ObjectId, int EffectId, int Frame)
        {
            this.ObjectId = ObjectId;
            this.EffectId = EffectId;
            this.Frame = Frame;
        }
    }

    public readonly struct RseRawOpacityCommand
    {
        public readonly int ObjectId;
        public readonly int Frame;
        public readonly string Command;
        public readonly string Value;

        public RseRawOpacityCommand(int ObjectId, int Frame, string Command, string Value)
        {
            this.ObjectId = ObjectId;
            this.Frame = Frame;
            this.Command = Command;
            this.Value = Value;
        }
    }

    public readonly struct RseParticleFrame
    {
        public readonly int Frame;
        public readonly float Age;
        public readonly float Lifetime;
        public readonly Vector3 Position;
        public readonly Vector3 Rotation;
        public readonly Vector3 Scale;
        public readonly Quaternion? QuaternionRotation;

        public RseParticleFrame(int Frame, float Age, float Lifetime, Vector3 Position, Vector3 Rotation,
            Vector3 Scale, Quaternion? QuaternionRotation)
        {
            this.Frame = Frame;
            this.Age = Age;
            this.Lifetime = Lifetime;
            this.Position = Position;
            this.Rotation = Rotation;
            this.Scale = Scale;
            this.QuaternionRotation = QuaternionRotation;
        }
    }
}
