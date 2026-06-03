using System;
using Avalonia.Platform;
using Silk.NET.WebGPU;
using ProGPU.Backend;

namespace Avalonia.ProGpu
{
    internal class OffscreenTextureCache : IDisposable
    {
        public GpuTexture? CachedTexture;
        public IntPtr CachedStagingBuffer = IntPtr.Zero;
        public uint CachedWidth;
        public uint CachedHeight;
        public uint CachedStagingBufferSize;
        public uint CachedBytesPerRow;
        public bool IsTextureFresh = true;

        public OffscreenTextureCache()
        {
            WgpuContext.Disposing += OnContextDisposing;
        }

        private void OnContextDisposing(WgpuContext context)
        {
            if (CachedTexture?.Context == context)
            {
                Invalidate(context);
            }
        }

        public unsafe void Invalidate(WgpuContext? context)
        {
            if (CachedTexture != null)
            {
                CachedTexture.Dispose();
                CachedTexture = null;
            }
            if (CachedStagingBuffer != IntPtr.Zero && context != null)
            {
                lock (context.RenderLock)
                {
                    if (!context.IsDisposed)
                    {
                        context.Wgpu.BufferDestroy((Silk.NET.WebGPU.Buffer*)CachedStagingBuffer);
                        context.Wgpu.BufferRelease((Silk.NET.WebGPU.Buffer*)CachedStagingBuffer);
                    }
                }
                CachedStagingBuffer = IntPtr.Zero;
            }
            CachedWidth = 0;
            CachedHeight = 0;
            CachedStagingBufferSize = 0;
            CachedBytesPerRow = 0;
            IsTextureFresh = true;
        }

        public void Dispose()
        {
            WgpuContext.Disposing -= OnContextDisposing;
            var context = CachedTexture?.Context ?? WgpuContext.Current;
            Invalidate(context);
        }
    }
}
