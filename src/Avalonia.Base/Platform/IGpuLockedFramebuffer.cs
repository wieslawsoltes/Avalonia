using System;

namespace Avalonia.Platform
{
    public interface IGpuLockedFramebuffer : ILockedFramebuffer
    {
        IntPtr SurfacePointer { get; }
        IntPtr WindowPointer { get; }
    }
}
