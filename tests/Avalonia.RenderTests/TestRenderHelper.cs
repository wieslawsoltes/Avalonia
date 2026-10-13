using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Xunit;
using Avalonia.Input;
using Avalonia.Platform;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using Avalonia.Platform.Surfaces;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.UnitTests;
using Avalonia.Utilities;
using Avalonia.Harfbuzz;
using Avalonia.Skia;
using SkiaSharp;

namespace Avalonia.Skia.RenderTests;

static class TestRenderHelper
{
    static TestRenderHelper()
    {
        SkiaPlatform.Initialize();
        AvaloniaLocator.CurrentMutable.Bind<IAssetLoader>().ToConstant(new StandardAssetLoader());
        AvaloniaLocator.CurrentMutable.Bind<ITextShaperImpl>().ToConstant(new HarfBuzzTextShaper());
        AvaloniaLocator.CurrentMutable.Bind<ICursorFactory>().ToConstant(new NullCursorFactory());
    }

    private sealed class NullCursorFactory : ICursorFactory
    {
        public ICursorImpl GetCursor(StandardCursorType cursorType) => new NullCursor();
        public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot) => new NullCursor();

        private sealed class NullCursor : ICursorImpl
        {
            public void Dispose() { }
        }
    }
    
    
    public static Task RenderToFile(Control target, string path, bool immediate, double dpi = 96)
    {
        var dir = Path.GetDirectoryName(path);
        Assert.NotNull(dir);

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        
        var factory = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();
        var pixelSize = new PixelSize((int)target.Width, (int)target.Height);
        var size = new Size(target.Width, target.Height);
        var dpiVector = new Vector(dpi, dpi);

        if (immediate)
        {
            using (RenderTargetBitmap bitmap = new RenderTargetBitmap(pixelSize, dpiVector))
            {
                target.Measure(size);
                target.Arrange(new Rect(size));
                bitmap.Render(target);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
        }
        else
        {
            var timer = new ManualRenderTimer();

            var compositor = new Compositor(RenderLoop.FromTimer(timer), null, true,
                new DispatcherCompositorScheduler(), true, Dispatcher.UIThread);
            using (var writableBitmap = factory.CreateWriteableBitmap(pixelSize, dpiVector, factory.DefaultPixelFormat,
                       factory.DefaultAlphaFormat))
            {
                var root = new TestRenderRoot(dpiVector.X / 96, null!);
                using (var renderer = new CompositingRenderer(root, compositor,
                           () => new[] { new BitmapFramebufferSurface(writableBitmap) }))
                {
                    root.Initialize(renderer, target);
                    renderer.Start();
                    Dispatcher.UIThread.RunJobs();
                    renderer.Paint(new Rect(root.Bounds.Size), false);
                }

                using var fileStream = File.Create(path);
                writableBitmap.Save(fileStream, PngBitmapEncoderOptions.Default);
            }
        }

        return Task.CompletedTask;
    }

    class BitmapFramebufferSurface : IFramebufferPlatformSurface
    {
        private readonly IWriteableBitmapImpl _bitmap;

        public BitmapFramebufferSurface(IWriteableBitmapImpl bitmap)
        {
            _bitmap = bitmap;
        }

        public IFramebufferRenderTarget CreateFramebufferRenderTarget()
        {
            return new FuncFramebufferRenderTarget(() => _bitmap.Lock());
        }
    }


    public static void BeginTest()
    {
        Dispatcher.ResetBeforeUnitTests();
    }

    public static void EndTest()
    {
        if (Dispatcher.UIThread.CheckAccess()) 
            Dispatcher.UIThread.RunJobs();
        Dispatcher.ResetForUnitTests();
    }
    
    public static string GetTestsDirectory()
    {
        var path = Directory.GetCurrentDirectory();

        while (!string.IsNullOrEmpty(path) && Path.GetFileName(path) != "tests")
        {
            path = Path.GetDirectoryName(path);
        }

        Assert.NotNull(path);
        return path;
    }

    public static SKBitmap LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException("Unable to decode image: " + path);

        // Compare the original channels, without premultiplication rounding or color conversion.
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        return SKBitmap.Decode(codec, info)
            ?? throw new InvalidDataException("Unable to decode image: " + path);
    }

    public static void AssertCompareImages(string actualPath, string expectedPath)
    {
        using (var expected = TestRenderHelper.LoadImage(expectedPath))
        using (var actual = TestRenderHelper.LoadImage(actualPath))
        {
            double immediateError = TestRenderHelper.CompareImages(actual, expected);

            if (immediateError > 0.022)
            {
                Assert.Fail(actualPath + ": Error = " + immediateError);
            }
        }
    }
    
    /// <summary>
    /// Calculates root mean square error for given two images.
    /// Based roughly on ImageMagick implementation to ensure consistency.
    /// </summary>
    public static double CompareImages(SKBitmap actual, SKBitmap expected)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            throw new ArgumentException("Images have different resolutions");
        }

        var quantity = actual.Width * actual.Height;
        double squaresError = 0;

        const double scale = 1 / 255d;
            
        for (var x = 0; x < actual.Width; x++)
        {
            double localError = 0;
                
            for (var y = 0; y < actual.Height; y++)
            {
                var expectedPixel = expected.GetPixel(x, y);
                var actualPixel = actual.GetPixel(x, y);
                var expectedAlpha = expectedPixel.Alpha * scale;
                var actualAlpha = actualPixel.Alpha * scale;
                    
                var r = scale * (expectedAlpha * expectedPixel.Red - actualAlpha * actualPixel.Red);
                var g = scale * (expectedAlpha * expectedPixel.Green - actualAlpha * actualPixel.Green);
                var b = scale * (expectedAlpha * expectedPixel.Blue - actualAlpha * actualPixel.Blue);
                var a = expectedAlpha - actualAlpha;

                var error = r * r + g * g + b * b + a * a;

                localError += error;
            }

            squaresError += localError;
        }

        var meanSquaresError = squaresError / quantity;

        const int channelCount = 4;
            
        meanSquaresError = meanSquaresError / channelCount;
            
        return Math.Sqrt(meanSquaresError);
    }

}
