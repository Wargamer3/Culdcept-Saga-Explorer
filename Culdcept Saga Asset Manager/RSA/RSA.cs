using System;
using System.Numerics;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSA
{
    /// <summary>
    /// Represent a RSA file.
    /// Probably stands for Ressource Animation
    /// </summary>
    public enum RsaChannelKind : ushort
    {
        None = 0,
        TransformKeys = 1,
        VertexFrames = 2
    }

    public class RsaTransformKeyframe
    {
        public float Frame;
        public Vector3 Translation;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    public class RsaVertexFrame
    {
        public float Frame;
        public Vector3[] ArrayPosition;
        public Vector3[] ArrayNormal;
    }

    public class RsaChannel
    {
        public RsaChannelKind Kind;
        public ushort Count;
        public uint Offset;
        public ushort VertexCount;
        public ushort NormalCount;

        public List<RsaTransformKeyframe> ListKeyframe { get; } = new List<RsaTransformKeyframe>();
        public List<RsaVertexFrame> ListVertexFrame { get; } = new List<RsaVertexFrame>();
    }

    public class RsaClip
    {
        public string Name;
        public ushort NodeCount;
        public List<RsaChannel> ListChannel { get; } = new List<RsaChannel>();

        public float GetStartFrame()
        {
            float MinFrame = float.MaxValue;
            foreach (RsaChannel Channel in ListChannel)
            {
                if (Channel.Kind == RsaChannelKind.TransformKeys && Channel.ListKeyframe.Count > 0)
                {
                    MinFrame = Math.Min(MinFrame, Channel.ListKeyframe[0].Frame);
                }
                else if (Channel.Kind == RsaChannelKind.VertexFrames && Channel.ListVertexFrame.Count > 0)
                {
                    MinFrame = Math.Min(MinFrame, Channel.ListVertexFrame[0].Frame);
                }
            }
            return MinFrame == float.MaxValue ? 0.0f : MinFrame;
        }
    }
}
