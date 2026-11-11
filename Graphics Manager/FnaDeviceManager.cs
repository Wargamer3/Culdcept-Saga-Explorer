using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading;
using System.Windows.Forms;

namespace CuldceptSagaExplorer.GraphicsManager
{
    internal static class FnaDeviceManager
    {
        private static GraphicsDevice GraphicsDevice;
        private static Form DeviceWindow;
        private static int ReferenceCount;
        private static int OwnerThreadId;

        internal static GraphicsDevice Acquire()
        {
            if (ReferenceCount > 0)
            {
                if (OwnerThreadId != Thread.CurrentThread.ManagedThreadId)
                {
                    throw new InvalidOperationException("MonoGame controls must share the same UI thread.");
                }
                ReferenceCount++;
                return GraphicsDevice;
            }

            try
            {
                // Keep the device's HWND alive independently of any visible viewport.
                DeviceWindow = new Form
                {
                    ShowInTaskbar = false,
                    ClientSize = new System.Drawing.Size(1, 1)
                };

                GraphicsDevice = new GraphicsDevice(
                    GraphicsAdapter.DefaultAdapter,
                    GraphicsProfile.HiDef,
                    new PresentationParameters
                    {
                        DeviceWindowHandle = DeviceWindow.Handle,
                        BackBufferWidth = 1,
                        BackBufferHeight = 1,
                        IsFullScreen = false,
                        DepthStencilFormat = DepthFormat.Depth24Stencil8,
                        PresentationInterval = PresentInterval.Immediate
                    });
                OwnerThreadId = Thread.CurrentThread.ManagedThreadId;
                ReferenceCount = 1;
                return GraphicsDevice;
            }
            finally
            {
                if (ReferenceCount == 0)
                {
                    DestroyDevice();
                }
            }
        }

        internal static void Release()
        {
            if (ReferenceCount == 0 || OwnerThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("The shared MonoGame device must be released by its owning UI thread.");
            }

            ReferenceCount--;
            if (ReferenceCount == 0)
            {
                DestroyDevice();
            }
        }

        private static void DestroyDevice()
        {
            try
            {
                if (GraphicsDevice != null)
                {
                    GraphicsDevice.Dispose();
                }
            }
            finally
            {
                GraphicsDevice = null;
                if (DeviceWindow != null)
                {
                    DeviceWindow.Dispose();
                }

                DeviceWindow = null;
                OwnerThreadId = 0;
            }
        }
    }
}
