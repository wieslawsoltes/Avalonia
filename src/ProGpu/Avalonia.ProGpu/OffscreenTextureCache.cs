using System;
using ProGPU.Backend;

namespace Avalonia.ProGpu
{
    internal class OffscreenTextureCache : IDisposable
    {
        public GpuTexture? CachedTexture;
        public GpuTextureReadbackBuffer? CachedReadbackBuffer;
        public uint CachedWidth;
        public uint CachedHeight;
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

        public void Invalidate(WgpuContext? context)
        {
            if (CachedTexture != null)
            {
                CachedTexture.Dispose();
                CachedTexture = null;
            }
            CachedReadbackBuffer?.Dispose();
            CachedReadbackBuffer = null;
            CachedWidth = 0;
            CachedHeight = 0;
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
