﻿using System;
using Avalonia.Platform;
using JetBrains.Annotations;
#if SKIASHARPSHIM
using ShimSkiaSharp;
#else
using SkiaSharp;
#endif

namespace Avalonia.Skia
{
    /// <inheritdoc />
    public class GlyphRunImpl : IGlyphRunImpl
    {
        public GlyphRunImpl([NotNull] SKTextBlob textBlob)
        {
            TextBlob = textBlob ?? throw new ArgumentNullException (nameof (textBlob));
        }

        /// <summary>
        ///     Gets the text blob to draw.
        /// </summary>
        public SKTextBlob TextBlob { get; }

        void IDisposable.Dispose()
        {
            TextBlob.Dispose();
        }
    }
}
