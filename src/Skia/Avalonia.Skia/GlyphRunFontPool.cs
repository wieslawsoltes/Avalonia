using System;
using Avalonia.Media;
using SkiaSharp;

namespace Avalonia.Skia;

/// <summary>
/// Bounded configured fonts for glyph measurement and blob construction. A mutable SKFont
/// belongs to exactly one lease while in use. General CreateSKFont callers never enter here.
/// </summary>
internal sealed class GlyphRunFontPool(SkiaTypeface owner) : IDisposable
{
    internal const int Capacity = 16;
    private readonly object _gate = new();
    private readonly Slot[] _slots = new Slot[Capacity];
    private int _nextReplacement;
    private bool _disposed;

    private struct Slot
    {
        internal SKFont? Font;
        internal float Size;
        internal int Options;
        internal long Generation;
        internal bool InUse;
    }

    internal int RetainedCount
    {
        get
        {
            lock (_gate)
            {
                var count = 0;
                foreach (var slot in _slots) if (slot.Font is not null) ++count;
                return count;
            }
        }
    }

    internal Lease Rent(float size, TextOptions options)
    {
        var optionKey = GetOptionKey(options);
        lock (_gate)
        {
            if (_disposed || owner.IsDisposed) throw new ObjectDisposedException(nameof(SkiaTypeface));
            // Keep the original native behavior for non-finite sizes, without retaining them.
            if (float.IsNaN(size) || float.IsInfinity(size))
                return new Lease(this, CreateFont(size, optionKey), -1, 0);
            for (var i = 0; i < _slots.Length; ++i)
            {
                ref var slot = ref _slots[i];
                if (slot.Font is not null && !slot.InUse && slot.Size == size && slot.Options == optionKey)
                {
                    slot.InUse = true;
                    return new Lease(this, slot.Font, i, ++slot.Generation);
                }
            }
            var replacement = -1;
            for (var i = 0; i < _slots.Length; ++i)
            {
                var index = (_nextReplacement + i) & (Capacity - 1);
                if (_slots[index].Font is null) { replacement = index; break; }
                if (!_slots[index].InUse && replacement < 0) replacement = index;
            }
            if (replacement < 0)
                return new Lease(this, CreateFont(size, optionKey), -1, 0);
            ref var candidate = ref _slots[replacement];
            candidate.Font?.Dispose();
            candidate.Font = null;
            var font = CreateFont(size, optionKey);
            candidate.Font = font;
            candidate.Size = size;
            candidate.Options = optionKey;
            candidate.InUse = true;
            _nextReplacement = (replacement + 1) & (Capacity - 1);
            return new Lease(this, font, replacement, ++candidate.Generation);
        }
    }

    private void Return(SKFont font, int index, long generation)
    {
        if (index < 0) { font.Dispose(); return; }
        lock (_gate)
        {
            ref var slot = ref _slots[index];
            // A copied/disposed lease cannot return a slot borrowed by someone else later.
            if (!slot.InUse || slot.Generation != generation || !ReferenceEquals(slot.Font, font)) return;
            slot.InUse = false;
            if (_disposed)
            {
                slot.Font = null;
                font.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            for (var i = 0; i < _slots.Length; ++i)
            {
                ref var slot = ref _slots[i];
                // Native SKFont owns its own typeface reference. Active users release their
                // font on return, rather than losing it during a concurrent owner disposal.
                if (!slot.InUse)
                {
                    slot.Font?.Dispose();
                    slot.Font = null;
                }
            }
        }
    }

    private static int GetOptionKey(TextOptions options)
    {
        var edging = options.TextRenderingMode switch
        {
            TextRenderingMode.Alias => 0,
            TextRenderingMode.Antialias => 1,
            _ => 2
        };
        var hinting = options.TextHintingMode switch
        {
            TextHintingMode.None => 0,
            TextHintingMode.Light => 1,
            _ => 2
        };
        var snap = options.BaselinePixelAlignment == BaselinePixelAlignment.Unaligned ? 0 : 1;
        return edging * 6 + hinting * 2 + snap;
    }

    private SKFont CreateFont(float size, int options)
    {
        var edging = options / 6;
        var hinting = (options % 6) / 2;
        var font = owner.CreateSKFont(size);
        try
        {
            font.ForceAutoHinting = hinting == 1;
            font.Hinting = hinting switch { 0 => SKFontHinting.None, 1 => SKFontHinting.Slight, _ => SKFontHinting.Full };
            font.Subpixel = edging != 0;
            font.Edging = edging switch { 0 => SKFontEdging.Alias, 1 => SKFontEdging.Antialias, _ => SKFontEdging.SubpixelAntialias };
            font.BaselineSnap = (options & 1) != 0;
            return font;
        }
        catch { font.Dispose(); throw; }
    }

    /// <summary>Use only for read-only native operations; do not mutate or expose the font.</summary>
    internal readonly struct Lease(GlyphRunFontPool pool, SKFont font, int index, long generation) : IDisposable
    {
        internal SKFont Font => font;
        public void Dispose() => pool.Return(font, index, generation);
    }
}
