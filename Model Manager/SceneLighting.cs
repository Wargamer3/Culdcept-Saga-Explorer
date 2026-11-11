using Microsoft.Xna.Framework;
using System;

namespace CuldceptSagaExplorer.ModelManager
{
    /// <summary>
    /// Vertex lighting from the game's shader.fx:
    /// Diff = color * (litAmb + Σ litCol[n] * max(0, N·litVec[n])), then lerp(Diff, color, SelfIllum).
    /// Directions point toward the light and are in glTF space (the converter negates the game's Z).
    /// The game normalizes litVec as a float4 (w included) against a float4 normal with w = 0, so a
    /// unit direction with w = 1 contributes N·L / √2.
    /// </summary>
    public sealed class SceneLighting
    {
        public bool Enabled = true;
        public Vector3 Ambient;
        public Vector4[] LightDirections = Array.Empty<Vector4>();
        public Vector3[] LightColors = Array.Empty<Vector3>();

        public static SceneLighting Unlit { get; } = new SceneLighting { Enabled = false };

        // Shader constants captured with RenderDoc from Alcyon (DATA02_0035) in Xenia:
        // litAmb = c25, litVec[0] = c12, litCol[0] = c14; the second light and point lights were black.
        public static SceneLighting Alcyon { get; } = new SceneLighting
        {
            Ambient = new Vector3(0.27313f, 0.32163f, 0.38028f),
            LightDirections = new[] { new Vector4(-0.45097f, 0.66687f, 0.59322f, 1f) },
            LightColors = new[] { new Vector3(1.08834f, 1.0853f, 0.94626f) },
        };

        // Captured from the Story Character Selection screen (npc_014, world matrix = identity): a white light
        // from the model's front, brighter than the map lights, so lit faces saturate.
        public static SceneLighting CharacterSelection { get; } = new SceneLighting
        {
            Ambient = new Vector3(0.3f),
            LightDirections = new[] { new Vector4(0.0349f, -0.0349f, 0.9988f, 1f) },
            LightColors = new[] { new Vector3(1.35f) },
        };

        public static SceneLighting Default => Alcyon;

        internal Vector3 Apply(Vector3 UnlitColor, Vector3 Normal, float SelfIllumination, bool TwoSided = false)
        {
            if (!Enabled)
            {
                return UnlitColor;
            }
            Vector3 Light = Ambient;
            if (Normal.LengthSquared() > 0)
            {
                Normal.Normalize();
                for (int Index = 0; Index < LightDirections.Length && Index < LightColors.Length; Index++)
                {
                    Vector4 Direction = LightDirections[Index];
                    if (Direction.LengthSquared() == 0)
                    {
                        continue;
                    }
                    Direction.Normalize();
                    float Facing = Normal.X * Direction.X + Normal.Y * Direction.Y + Normal.Z * Direction.Z;
                    // Flame cards are drawn from both sides, and their normals often point away from the preview lamp.
                    if (TwoSided)
                    {
                        Facing = MathF.Abs(Facing);
                    }
                    Light += LightColors[Index] * MathF.Max(0, Facing);
                }
            }
            return Vector3.Lerp(UnlitColor * Light, UnlitColor, SelfIllumination);
        }
    }
}
