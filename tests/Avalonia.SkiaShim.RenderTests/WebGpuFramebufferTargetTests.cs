using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Skia;
using ProGPU.Backend;
using Silk.NET.WebGPU;
using Xunit;

namespace Avalonia.Skia.RenderTests;

public sealed class WebGpuFramebufferTargetTests
{
    [Fact]
    public unsafe void ResolvesCurrentContextFromPublicSurfaceHandle()
    {
        var previous = WgpuContext.Current;
        var context = (WgpuContext)RuntimeHelpers.GetUninitializedObject(typeof(WgpuContext));
        var surfaceHandle = new IntPtr(0x1234);
        var surfaceField = typeof(WgpuContext).GetField(
            "<Surface>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(surfaceField);
        surfaceField.SetValue(
            context,
            Pointer.Box((void*)surfaceHandle, typeof(Surface*)));

        try
        {
            WgpuContext.Current = context;
            using var framebuffer = new TestLockedFramebuffer(
                new PixelSize(1, 1),
                surfaceHandle,
                "WGPU_SURFACE");

            Assert.True(WebGpuFramebufferTarget.TryResolveContext(framebuffer, out var resolved));
            Assert.Same(context, resolved);
        }
        finally
        {
            WgpuContext.Current = previous;
        }
    }

    [Fact]
    public void IgnoresUnrelatedPlatformHandles()
    {
        using var framebuffer = new TestLockedFramebuffer(
            new PixelSize(1, 1),
            new IntPtr(0x1234),
            "TEST_SURFACE");

        Assert.False(WebGpuFramebufferTarget.TryResolveContext(framebuffer, out _));
    }

    [Fact]
    public void CpuFramebufferIsDisposedWhenSurfaceCreationFails()
    {
        var framebuffer = new TestLockedFramebuffer(
            new PixelSize(0, 0),
            IntPtr.Zero,
            "TEST_SURFACE");
        var platformSurface = new TestFramebufferSurface(framebuffer);
        using var target = new FramebufferRenderTarget(platformSurface);

        Assert.Throws<ArgumentException>(() => target.CreateDrawingContext(default, out _));

        Assert.True(framebuffer.IsDisposed);
    }

    private sealed class TestFramebufferSurface : IFramebufferPlatformSurface
    {
        private readonly ILockedFramebuffer _framebuffer;

        public TestFramebufferSurface(ILockedFramebuffer framebuffer)
        {
            _framebuffer = framebuffer;
        }

        public IFramebufferRenderTarget CreateFramebufferRenderTarget()
        {
            return new FuncFramebufferRenderTarget(() => _framebuffer);
        }
    }

    private sealed class TestLockedFramebuffer : ILockedFramebuffer, IPlatformHandle
    {
        public TestLockedFramebuffer(PixelSize size, IntPtr handle, string handleDescriptor)
        {
            Size = size;
            Handle = handle;
            HandleDescriptor = handleDescriptor;
        }

        public IntPtr Address => IntPtr.Zero;
        public PixelSize Size { get; }
        public int RowBytes => Math.Max(0, Size.Width * 4);
        public Vector Dpi => new(96, 96);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Premul;
        public IntPtr Handle { get; }
        public string HandleDescriptor { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
