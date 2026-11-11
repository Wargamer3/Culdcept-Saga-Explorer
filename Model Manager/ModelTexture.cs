using Microsoft.Xna.Framework.Graphics;

namespace CuldceptSagaExplorer.ModelManager
{
    public sealed class ModelTexture
    {
        public int Index { get; }
        public string Name { get; }
        /// <summary>Premultiplied-alpha texture; draw it with <see cref="BlendState.AlphaBlend"/>.</summary>
        public Texture2D Texture { get; }
        /// <summary>True when any texel has an alpha below 255.</summary>
        public bool HasTransparency { get; }

        internal ModelTexture(int Index, string Name, Texture2D Texture, bool HasTransparency)
        {
            this.Index = Index;
            this.Name = Name;
            this.Texture = Texture;
            this.HasTransparency = HasTransparency;
        }
    }
}
