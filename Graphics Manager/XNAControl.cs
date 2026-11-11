using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;
using Color = Microsoft.Xna.Framework.Color;

namespace CuldceptSagaExplorer.GraphicsManager
{
    public abstract class XNAControl : Control
    {
        private GraphicsDevice OwnedGraphicsDevice;
        private Timer RenderTimer;
        private Stopwatch GameTimer;
        private TimeSpan LastFrameTime;
        private bool IsInitialized;
        private SwapChainRenderTarget SwapChainTarget;
        private RenderTarget2D SceneTarget;
        private DisplayGammaRamp GammaRamp;

        private bool IsDesignTime
        {
            get
            {
                return DesignMode || LicenseManager.UsageMode == LicenseUsageMode.Designtime;
            }
        }

        /// <summary>When not null, the frame is drawn through this 256-entry, 10-bit display gamma ramp.</summary>
        protected virtual ushort[] DisplayGammaRampTable
        {
            get
            {
                return null;
            }
        }

        [Browsable(false)]
        public GraphicsDevice GraphicsDevice
        {
            get
            {
                return OwnedGraphicsDevice;
            }
        }

        protected override void OnHandleCreated(EventArgs EventArgs)
        {
            base.OnHandleCreated(EventArgs);

            if (!IsDesignTime)
            {
                InitializeFna();

                LastFrameTime = TimeSpan.Zero;
                GameTimer = Stopwatch.StartNew();
                RenderTimer = new Timer { Interval = 16 };
                RenderTimer.Tick += OnRenderTimerTick;
                RenderTimer.Start();
            }
        }

        private void InitializeFna()
        {
            if (IsInitialized)
            {
                return;
            }

            OwnedGraphicsDevice = FnaDeviceManager.Acquire();

            try
            {
                EnsureRenderTarget();
                Initialize();
                IsInitialized = true;
            }
            finally
            {
                if (!IsInitialized)
                {
                    ReleaseFna();
                }
            }
        }

        protected override void OnHandleDestroyed(EventArgs EventArgs)
        {
            ReleaseFna();
            base.OnHandleDestroyed(EventArgs);
        }

        protected override void OnResize(EventArgs EventArgs)
        {
            base.OnResize(EventArgs);

            if (IsInitialized)
            {
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs PaintEventArgs)
        {
            // Stop GDI from painting the background to prevent flashing
        }

        protected override void OnPaint(PaintEventArgs PaintEventArgs)
        {
            if (IsDesignTime || !IsInitialized || OwnedGraphicsDevice == null)
            {
                PaintEventArgs.Graphics.Clear(System.Drawing.Color.DarkGray);
                return;
            }

            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            EnsureRenderTarget();

            TimeSpan CurrentTime = GameTimer.Elapsed;
            TimeSpan ElapsedTime = CurrentTime - LastFrameTime;
            LastFrameTime = CurrentTime;

            GameTime GameTime = new GameTime(CurrentTime, ElapsedTime);

            ushort[] RampTable = DisplayGammaRampTable;
            if (RampTable != null)
            {
                EnsureSceneTarget();
                if (GammaRamp == null)
                {
                    GammaRamp = new DisplayGammaRamp(OwnedGraphicsDevice, RampTable);
                }
            }

            RenderTarget2D FrameTarget = SwapChainTarget;
            if (RampTable != null)
            {
                FrameTarget = SceneTarget;
            }

            OwnedGraphicsDevice.SetRenderTarget(FrameTarget);
            try
            {
                // Each viewport starts with the state of a fresh graphics device.
                OwnedGraphicsDevice.BlendState = BlendState.Opaque;
                OwnedGraphicsDevice.DepthStencilState = DepthStencilState.Default;
                OwnedGraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

                // MonoGame 3.8.1 WindowsDX allocates 16 slots for each shader stage.
                for (int Index = 0; Index < 16; Index++)
                {
                    OwnedGraphicsDevice.Textures[Index] = null;
                    OwnedGraphicsDevice.SamplerStates[Index] = SamplerState.LinearWrap;
                }

                for (int Index = 0; Index < 16; Index++)
                {
                    OwnedGraphicsDevice.VertexTextures[Index] = null;
                    OwnedGraphicsDevice.VertexSamplerStates[Index] = SamplerState.LinearWrap;
                }

                OwnedGraphicsDevice.SetVertexBuffer(null);
                OwnedGraphicsDevice.Indices = null;
                Update(GameTime);
                OwnedGraphicsDevice.Clear(Color.CornflowerBlue);
                Draw(GameTime);

                if (RampTable != null)
                {
                    OwnedGraphicsDevice.SetRenderTarget(SwapChainTarget);
                    GammaRamp.Draw(SceneTarget);
                }
            }
            finally
            {
                OwnedGraphicsDevice.SetRenderTarget(null);
            }

            SwapChainTarget.Present();
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                ReleaseFna();
            }
            base.Dispose(Disposing);
        }

        private void ReleaseFna()
        {
            IsInitialized = false;
            if (RenderTimer != null)
            {
                RenderTimer.Stop();
                RenderTimer.Dispose();
                RenderTimer = null;
            }

            if (GameTimer != null)
            {
                GameTimer.Stop();
                GameTimer = null;
            }

            if (OwnedGraphicsDevice != null)
            {
                try
                {
                    UnloadContent();
                }
                finally
                {
                    try
                    {
                        if (GammaRamp != null)
                        {
                            GammaRamp.Dispose();
                            GammaRamp = null;
                        }

                        if (SceneTarget != null)
                        {
                            SceneTarget.Dispose();
                            SceneTarget = null;
                        }

                        if (SwapChainTarget != null)
                        {
                            SwapChainTarget.Dispose();
                            SwapChainTarget = null;
                        }
                    }
                    finally
                    {
                        OwnedGraphicsDevice = null;
                        FnaDeviceManager.Release();
                    }
                }
            }
        }

        private void EnsureRenderTarget()
        {
            int Width = Math.Max(ClientSize.Width, 1);
            int Height = Math.Max(ClientSize.Height, 1);
            if (SwapChainTarget != null && SwapChainTarget.Width == Width && SwapChainTarget.Height == Height)
            {
                return;
            }

            if (SwapChainTarget != null)
            {
                SwapChainTarget.Dispose();
                SwapChainTarget = null;
            }

            SwapChainTarget = new SwapChainRenderTarget(OwnedGraphicsDevice, Handle, Width, Height, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0,
                RenderTargetUsage.DiscardContents, PresentInterval.Immediate);
        }

        protected virtual void UnloadContent()
        {
        }

        private void EnsureSceneTarget()
        {
            if (SceneTarget != null && SceneTarget.Width == SwapChainTarget.Width && SceneTarget.Height == SwapChainTarget.Height)
            {
                return;
            }

            if (SceneTarget != null)
            {
                SceneTarget.Dispose();
                SceneTarget = null;
            }

            SceneTarget = new RenderTarget2D(OwnedGraphicsDevice, SwapChainTarget.Width, SwapChainTarget.Height, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.DiscardContents);
        }

        protected abstract void Initialize();

        protected abstract void Update(GameTime GameTime);

        protected abstract void Draw(GameTime GameTime);

        private void OnRenderTimerTick(object Sender, EventArgs TickEventArgs)
        {
            Invalidate();
        }
    }
}
