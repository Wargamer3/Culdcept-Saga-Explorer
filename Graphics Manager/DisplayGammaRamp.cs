using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CuldceptSagaExplorer.GraphicsManager
{
    /// <summary>
    /// Full-screen display gamma ramp pass, applied after the scene like the Xbox 360 display ramp.
    /// </summary>
    public sealed class DisplayGammaRamp : IDisposable
    {
        /// <summary>
        /// The 10-bit ramp Culdcept Saga sets (identical for R, G and B), captured in Xenia with RenderDoc.
        /// Roughly a 1.16 power curve that darkens mid-tones and crushes shadows.
        /// </summary>
        public static readonly ushort[] CuldceptSaga =
        {
            0, 1, 3, 4, 6, 7, 8, 10, 11, 13, 14, 15, 17, 18, 20, 22,
            24, 26, 28, 30, 32, 34, 37, 39, 42, 45, 47, 50, 53, 56, 59, 63,
            66, 70, 73, 77, 81, 85, 89, 93, 97, 101, 105, 109, 113, 117, 121, 125,
            129, 133, 137, 141, 145, 149, 153, 157, 161, 165, 169, 173, 177, 181, 185, 189,
            193, 197, 201, 206, 210, 214, 218, 222, 226, 230, 234, 238, 242, 247, 251, 255,
            259, 263, 267, 271, 276, 281, 285, 289, 293, 297, 302, 306, 310, 314, 318, 322,
            327, 331, 335, 339, 343, 348, 352, 356, 360, 364, 369, 373, 377, 381, 386, 390,
            394, 398, 403, 407, 411, 415, 420, 424, 428, 432, 437, 441, 445, 449, 454, 458,
            462, 466, 471, 475, 479, 484, 488, 492, 496, 501, 505, 509, 514, 518, 522, 527,
            531, 535, 539, 544, 548, 552, 557, 561, 565, 570, 574, 578, 583, 587, 591, 596,
            600, 604, 609, 613, 618, 622, 626, 631, 635, 639, 645, 649, 654, 658, 662, 667,
            671, 675, 680, 684, 689, 693, 697, 702, 706, 710, 715, 719, 724, 728, 732, 737,
            741, 746, 750, 754, 759, 763, 768, 772, 777, 781, 785, 790, 794, 799, 803, 808,
            812, 816, 821, 825, 830, 834, 839, 843, 847, 852, 856, 861, 865, 870, 874, 879,
            883, 888, 892, 896, 901, 905, 910, 914, 919, 923, 928, 932, 937, 941, 946, 950,
            955, 959, 963, 968, 972, 977, 981, 986, 990, 995, 999, 1004, 1008, 1013, 1017, 1023,
        };

        private readonly Effect RampEffect;
        private readonly Texture2D RampTexture;
        private readonly SpriteBatch SpriteBatch;

        public DisplayGammaRamp(GraphicsDevice GraphicsDevice, ushort[] Table)
        {
            if (Table == null || Table.Length != 256)
            {
                throw new ArgumentException("A gamma ramp needs 256 entries.", nameof(Table));
            }
            RampEffect = new Effect(GraphicsDevice, LoadEffectCode());
            RampTexture = new Texture2D(GraphicsDevice, 256, 1, false, SurfaceFormat.Color);
            Color[] RampColors = new Color[256];
            for (int Index = 0; Index < RampColors.Length; Index++)
            {
                byte Value = (byte)Math.Round(Math.Min(Table[Index], (ushort)1023) * 255.0 / 1023.0);
                RampColors[Index] = new Color(Value, Value, Value, (byte)255);
            }
            RampTexture.SetData(RampColors);
            RampEffect.Parameters["RampTexture"].SetValue(RampTexture);
            SpriteBatch = new SpriteBatch(GraphicsDevice);
        }

        /// <summary>Draws <paramref name="Source"/> to the current render target through the ramp.</summary>
        public void Draw(Texture2D Source)
        {
            GraphicsDevice GraphicsDevice = SpriteBatch.GraphicsDevice;
            SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, RampEffect);
            GraphicsDevice.SamplerStates[1] = SamplerState.PointClamp;
            SpriteBatch.Draw(Source, Vector2.Zero, Color.White);
            SpriteBatch.End();
        }

        public void Dispose()
        {
            SpriteBatch.Dispose();
            RampTexture.Dispose();
            RampEffect.Dispose();
        }

        private static byte[] LoadEffectCode()
        {
            using Stream Stream = typeof(DisplayGammaRamp).Assembly.GetManifestResourceStream("CuldceptSagaExplorer.GraphicsManager.Shaders.GammaRamp.mgfxo")
                ?? throw new InvalidOperationException("The GammaRamp.mgfxo resource is missing.");
            using MemoryStream Memory = new MemoryStream();
            Stream.CopyTo(Memory);
            return Memory.ToArray();
        }
    }
}
