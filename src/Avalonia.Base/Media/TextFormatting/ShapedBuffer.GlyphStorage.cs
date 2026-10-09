using System;
using System.Buffers;
using System.Threading;

namespace Avalonia.Media.TextFormatting;

public sealed partial class ShapedBuffer
{
    /// <summary>
    /// One disposable owner for the parallel glyph-info and glyph-index arrays.
    /// Every view aliases both arrays together, so they need only one reference count.
    /// Cluster-prefix storage still has its separate, lazily established lifetime.
    /// </summary>
    internal sealed class PooledGlyphArray : PooledArray<GlyphInfo>
    {
        private ushort[]? _indices;

        public PooledGlyphArray(int minLength) : base(minLength)
        {
            try
            {
                _indices = ArrayPool<ushort>.Shared.Rent(minLength);
            }
            catch
            {
                // The first rental must not be stranded if the second one fails.
                base.Dispose();
                throw;
            }
        }

        public ushort[] Indices => _indices ?? throw new ObjectDisposedException(nameof(PooledGlyphArray));

        public override void Dispose()
        {
            var indices = Interlocked.Exchange(ref _indices, null);
            try
            {
                base.Dispose();
            }
            finally
            {
                if (indices is not null)
                    ArrayPool<ushort>.Shared.Return(indices);
            }
        }
    }
}
