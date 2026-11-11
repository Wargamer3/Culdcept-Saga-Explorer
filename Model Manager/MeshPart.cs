using Microsoft.Xna.Framework.Graphics;
using System;

namespace CuldceptSagaExplorer.GraphicsManager
{
    public class MeshPart : IDisposable
    {
        public VertexBuffer Vertices;
        public IndexBuffer Indices;
        public Texture2D Texture;
        public int TriangleCount;
        public bool OwnsTexture = true;

        public void Dispose()
        {
            if (Vertices != null)
            {
                Vertices.Dispose();
            }

            if (Indices != null)
            {
                Indices.Dispose();
            }

            if (OwnsTexture && Texture != null)
            {
                Texture.Dispose();
            }
        }
    }
}
