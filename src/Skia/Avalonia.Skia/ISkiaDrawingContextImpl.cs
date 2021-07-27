using Avalonia.Platform;
#if SKIASHARPSHIM
using ShimSkiaSharp;
#else
using SkiaSharp;
#endif

namespace Avalonia.Skia
{
    public interface ISkiaDrawingContextImpl : IDrawingContextImpl
    {
        SKCanvas SkCanvas { get; }
        GRContext GrContext { get; }
        SKSurface SkSurface { get; }
    }
}
