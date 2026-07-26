using System;
using System.Collections.Generic;
using System.Threading;
using ProGPU.Backend;
using ProGPU.Scene;

namespace Avalonia.ProGpu
{
    internal class OffscreenTextureCache : IDisposable
    {
        private const int MaximumRetainedCompositionPictures = 2048;
        private readonly object _compositionPictureLock = new();
        private readonly Dictionary<long, RetainedCompositionPicture> _compositionPictures = new();
        private long _compositionPictureHits;
        private long _compositionPictureMisses;
        private long _compositionPictureCompilations;

        private sealed record RetainedCompositionPicture(ulong Revision, GpuPicture Picture);

        public GpuTexture? CachedTexture;
        public GpuTextureReadbackBuffer? CachedReadbackBuffer;
        public ProGPU.Scene.DrawingContext FrameDrawingContext { get; } = new();
        public uint CachedWidth;
        public uint CachedHeight;
        public bool IsTextureFresh = true;
        internal int CompositionPictureCount
        {
            get
            {
                lock (_compositionPictureLock)
                    return _compositionPictures.Count;
            }
        }
        internal long CompositionPictureHits => Interlocked.Read(ref _compositionPictureHits);
        internal long CompositionPictureMisses => Interlocked.Read(ref _compositionPictureMisses);
        internal long CompositionPictureCompilations => Interlocked.Read(ref _compositionPictureCompilations);

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

        public bool TryGetCompositionPicture(
            long id,
            ulong revision,
            out GpuPicture? picture)
        {
            lock (_compositionPictureLock)
            {
                if (_compositionPictures.TryGetValue(id, out var cached) &&
                    cached.Revision == revision)
                {
                    Interlocked.Increment(ref _compositionPictureHits);
                    picture = cached.Picture;
                    return true;
                }
            }

            Interlocked.Increment(ref _compositionPictureMisses);
            picture = null;
            return false;
        }

        public void StoreCompositionPicture(long id, ulong revision, GpuPicture picture)
        {
            ArgumentNullException.ThrowIfNull(picture);
            lock (_compositionPictureLock)
            {
                if (_compositionPictures.Remove(id, out var replaced))
                    replaced.Picture.Dispose();
                if (_compositionPictures.Count >= MaximumRetainedCompositionPictures)
                    ClearCompositionPictures();
                _compositionPictures.Add(id, new RetainedCompositionPicture(revision, picture));
                Interlocked.Increment(ref _compositionPictureCompilations);
            }
        }

        private void ClearCompositionPictures()
        {
            foreach (var cached in _compositionPictures.Values)
                cached.Picture.Dispose();
            _compositionPictures.Clear();
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
            lock (_compositionPictureLock)
                ClearCompositionPictures();
        }

        public void Dispose()
        {
            WgpuContext.Disposing -= OnContextDisposing;
            var context = CachedTexture?.Context ?? WgpuContext.Current;
            Invalidate(context);
            FrameDrawingContext.Clear();
        }
    }
}
