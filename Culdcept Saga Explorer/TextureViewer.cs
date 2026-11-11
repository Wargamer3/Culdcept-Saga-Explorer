using CuldceptSagaExplorer.GraphicsManager;
using CuldceptSagaExplorer.ModelManager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace GraphicsManager
{
    public partial class TextureViewer : XNAControl
    {
        private SpriteBatch SpriteBatch;
        private ModelTexture DisplayedTexture;

        protected override void Initialize()
        {
            if (GraphicsDevice == null)
            {
                return;
            }

            SpriteBatch = new SpriteBatch(GraphicsDevice);
        }

        public void LoadTexture(ModelTexture TextureToDisplay)
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException("Load textures on the viewport's UI thread.");
            }
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(TextureViewer));
            }
            if (GraphicsDevice != null)
            {
                if (TextureToDisplay == null)
                {
                    throw new ArgumentNullException(nameof(TextureToDisplay));
                }
                if (TextureToDisplay.Texture.IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(TextureToDisplay));
                }
                if (TextureToDisplay.Texture.GraphicsDevice != GraphicsDevice)
                {
                    throw new ArgumentException("The texture must belong to this viewer's graphics device.", nameof(TextureToDisplay));
                }
                DisplayedTexture = TextureToDisplay;
            }
            else
            {
                throw new InvalidOperationException("The viewport must be initialized before loading a texture.");
            }

            Invalidate();
        }

        public void ClearTexture()
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException("Clear textures on the viewport's UI thread.");
            }
            DisplayedTexture = null;
            Invalidate();
        }

        protected override void Update(GameTime GameTime)
        {
        }

        protected override void Draw(GameTime GameTime)
        {
            if (DisplayedTexture == null || SpriteBatch == null)
            {
                return;
            }
            Texture2D ActiveTexture = DisplayedTexture.Texture;
            // The owning model may have been unloaded or its handle recreated.
            if (ActiveTexture.IsDisposed)
            {
                ClearTexture();
                return;
            }

            Viewport DeviceViewport = GraphicsDevice.Viewport;
            float Scale = Math.Min((float)DeviceViewport.Width / ActiveTexture.Width,
                (float)DeviceViewport.Height / ActiveTexture.Height);
            int Width = Math.Max(1, (int)(ActiveTexture.Width * Scale));
            int Height = Math.Max(1, (int)(ActiveTexture.Height * Scale));
            Rectangle Destination = new Rectangle((DeviceViewport.Width - Width) / 2,
                (DeviceViewport.Height - Height) / 2, Width, Height);

            SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            try
            {
                SpriteBatch.Draw(ActiveTexture, Destination, Color.White);
            }
            finally
            {
                SpriteBatch.End();
            }
        }

        protected override void UnloadContent()
        {
            if (SpriteBatch != null)
            {
                SpriteBatch.Dispose();
                SpriteBatch = null;
            }
            base.UnloadContent();
        }
    }
}
