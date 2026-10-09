using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using SkiaSharp;

namespace Avalonia.Skia
{
    internal class SkiaTypeface : IPlatformTypeface
    {
        internal const int GlyphProbeInterval = 67;
        internal const int GlyphRecoverySampleInterval = 32;
        internal const int GlyphRecoveryProbeCount = SharedGlyphRunCache.AdmissionWindow * 2 + 1;
        private int _disposed;
        private int _glyphMisses;
        private int _glyphProbeCountdown;
        private int _glyphAdmissionClock;
        private int _glyphSampledProbes;
        private int _glyphRecoveryProbesRemaining;

        public SkiaTypeface(SKTypeface typeface, FontSimulations fontSimulations)
        {
            SKTypeface = typeface ?? throw new ArgumentNullException(nameof(typeface));
            FontSimulations = fontSimulations;
            Weight = (FontWeight)typeface.FontWeight;
            Style = typeface.FontStyle.Slant.ToAvalonia();
            Stretch = (FontStretch)typeface.FontWidth;
        }

        public SKTypeface SKTypeface { get; }
        public FontSimulations FontSimulations { get; }
        public string FamilyName => SKTypeface.FamilyName;
        public FontWeight Weight { get; }
        public FontStyle Style { get; }
        public FontStretch Stretch { get; }
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal bool ShouldProbeGlyphCache()
        {
            if (Volatile.Read(ref _glyphRecoveryProbesRemaining) > 0)
            {
                Interlocked.Decrement(ref _glyphRecoveryProbesRemaining);
                return true;
            }
            if (Volatile.Read(ref _glyphProbeCountdown) <= 0) return true;
            if (Interlocked.Decrement(ref _glyphProbeCountdown) <= 0)
            {
                // A cyclic set can otherwise miss forever because its sampled second
                // touch expires. Bound occasional full-rate recovery work, without
                // slowing the ordinary isolated probe's recovery to a single hot run.
                if (Interlocked.Increment(ref _glyphSampledProbes) >= GlyphRecoverySampleInterval)
                {
                    Volatile.Write(ref _glyphSampledProbes, 0);
                    Volatile.Write(ref _glyphRecoveryProbesRemaining, GlyphRecoveryProbeCount - 1);
                }
                return true;
            }
            // Unsuccessful work ages hints even when the expensive lookup is bypassed.
            // Keep the clock per font: another font's scan cannot expire this font's hints.
            NextGlyphCacheAdmissionStamp();
            return false;
        }

        internal uint NextGlyphCacheAdmissionStamp() =>
            unchecked((uint)Interlocked.Increment(ref _glyphAdmissionClock));

        // Called under the native cache's gate. Per-typeface state prevents a unique-text font
        // from delaying reuse of an unrelated font. A non-power-of-two interval avoids common scan strides.
        internal void RecordGlyphCacheMiss()
        {
            if (_glyphMisses < GlyphProbeInterval) ++_glyphMisses;
            if (_glyphMisses >= GlyphProbeInterval) Volatile.Write(ref _glyphProbeCountdown, GlyphProbeInterval);
        }

        internal void RecordGlyphCacheAdmission()
        {
            // Give a newly recurring run one immediate opportunity to hit. Creating a cache
            // entry is not itself a benefit and must not reset an unproductive miss streak.
            Volatile.Write(ref _glyphProbeCountdown, 0);
        }

        internal void RecordGlyphCacheBenefit()
        {
            _glyphMisses = 0;
            Volatile.Write(ref _glyphProbeCountdown, 0);
            Volatile.Write(ref _glyphSampledProbes, 0);
            Volatile.Write(ref _glyphRecoveryProbesRemaining, 0);
        }

        public SKFont CreateSKFont(float size) =>
            new(SKTypeface, size, skewX: (FontSimulations & FontSimulations.Oblique) != 0 ? -0.3f : 0.0f)
            {
                LinearMetrics = true,
                Embolden = (FontSimulations & FontSimulations.Bold) != 0
            };

        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
        {
            table = default;
            if (SKTypeface.TryGetTableData(tag, out var data)) { table = data; return true; }
            return false;
        }
        public bool TryGetStream([NotNullWhen(true)] out Stream? stream)
        {
            try
            {
                var asset = SKTypeface.OpenStream();
                var size = asset.Length;
                var buffer = new byte[size];
                asset.Read(buffer, size);
                stream = new MemoryStream(buffer);
                return true;
            }
            catch { stream = null; return false; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            SharedGlyphRunCache.RemoveTypeface(this);
            SKTypeface.Dispose();
        }
    }
}
