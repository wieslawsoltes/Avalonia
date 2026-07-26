using System;
using ProGPU.Scene;
using Xunit;

namespace Avalonia.ProGpu.UnitTests
{
    public sealed class OffscreenTextureCacheTests
    {
        [Fact]
        public void CompositionPictureCacheUsesStableIdAndRevision()
        {
            using var cache = new OffscreenTextureCache();
            var first = EmptyPicture();

            cache.StoreCompositionPicture(42, 1, first);

            Assert.True(cache.TryGetCompositionPicture(42, 1, out GpuPicture? cached));
            Assert.Same(first, cached);
            Assert.False(cache.TryGetCompositionPicture(42, 2, out _));
            Assert.Equal(1, cache.CompositionPictureCount);
            Assert.Equal(1, cache.CompositionPictureHits);
            Assert.Equal(1, cache.CompositionPictureMisses);
            Assert.Equal(1, cache.CompositionPictureCompilations);

            var second = EmptyPicture();
            cache.StoreCompositionPicture(42, 2, second);

            Assert.Throws<ObjectDisposedException>(() => first.Clone());
            Assert.True(cache.TryGetCompositionPicture(42, 2, out cached));
            Assert.Same(second, cached);
            Assert.Equal(1, cache.CompositionPictureCount);
            Assert.Equal(2, cache.CompositionPictureCompilations);
        }

        [Fact]
        public void CompositionPictureCacheIsBoundedAndDisposesEvictedPictures()
        {
            using var cache = new OffscreenTextureCache();
            var first = EmptyPicture();
            cache.StoreCompositionPicture(0, 1, first);

            for (var id = 1; id < 2048; id++)
                cache.StoreCompositionPicture(id, 1, EmptyPicture());

            var newest = EmptyPicture();
            cache.StoreCompositionPicture(2048, 1, newest);

            Assert.Throws<ObjectDisposedException>(() => first.Clone());
            Assert.False(cache.TryGetCompositionPicture(0, 1, out _));
            Assert.True(cache.TryGetCompositionPicture(2048, 1, out GpuPicture? cached));
            Assert.Same(newest, cached);
            Assert.Equal(1, cache.CompositionPictureCount);
        }

        private static GpuPicture EmptyPicture() =>
            new(
                Array.Empty<RenderCommand>(),
                Array.Empty<System.Numerics.Vector2>(),
                Array.Empty<double>(),
                Array.Empty<Line3D>(),
                Array.Empty<float>());
    }
}
