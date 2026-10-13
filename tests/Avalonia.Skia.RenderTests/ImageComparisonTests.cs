using System;
using System.IO;
using SkiaSharp;
using Xunit;

namespace Avalonia.Skia.RenderTests;

public class ImageComparisonTests
{
    // A 2x2, eight-bit RGBA PNG with exact straight-alpha channel values.
    private const string RgbaPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGklEQVR4nGMQDD7JyBl+up7hP4PDf+3UiwwANmAGeL8jgvUAAAAASUVORK5CYII=";

    [Fact]
    public void Decode_Preserves_Dimensions_And_Unpremultiplied_Channels()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String(RgbaPng));

            using var image = TestRenderHelper.LoadImage(path);

            Assert.Equal(2, image.Width);
            Assert.Equal(2, image.Height);
            Assert.Equal(SKAlphaType.Unpremul, image.AlphaType);
            Assert.Equal(new SKColor(17, 83, 201, 1), image.GetPixel(0, 0));
            Assert.Equal(new SKColor(9, 87, 203, 127), image.GetPixel(1, 0));
            Assert.Equal(new SKColor(255, 0, 64, 255), image.GetPixel(0, 1));
            Assert.Equal(new SKColor(43, 101, 209, 0), image.GetPixel(1, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Compare_Uses_Alpha_Weighted_Four_Channel_Root_Mean_Square_Error()
    {
        using var expected = CreatePixel(new SKColor(255, 0, 0, 128));
        using var actual = CreatePixel(new SKColor(0, 0, 255, 64));

        var expectedError = Math.Sqrt((128d * 128 + 64d * 64 + 64d * 64) / (4 * 255d * 255));

        Assert.Equal(expectedError, TestRenderHelper.CompareImages(actual, expected), 12);
    }

    [Fact]
    public void Compare_Ignores_Color_Channels_Of_Fully_Transparent_Pixels()
    {
        using var expected = CreatePixel(new SKColor(255, 0, 0, 0));
        using var actual = CreatePixel(new SKColor(0, 0, 255, 0));

        Assert.Equal(0, TestRenderHelper.CompareImages(actual, expected));
    }

    [Fact]
    public void Compare_Rejects_Different_Dimensions()
    {
        using var expected = new SKBitmap(2, 1);
        using var actual = new SKBitmap(1, 2);

        var error = Assert.Throws<ArgumentException>(() => TestRenderHelper.CompareImages(actual, expected));

        Assert.Equal("Images have different resolutions", error.Message);
    }

    [Fact]
    public void Compare_Preserves_The_Path_And_Error_In_Failure_Output()
    {
        var expectedPath = Path.GetTempFileName();
        var actualPath = Path.GetTempFileName();
        try
        {
            using var expected = CreatePixel(SKColors.Red);
            using var actual = CreatePixel(SKColors.Blue);
            using var expectedData = expected.Encode(SKEncodedImageFormat.Png, 100);
            using var actualData = actual.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(expectedPath, expectedData.ToArray());
            File.WriteAllBytes(actualPath, actualData.ToArray());

            var error = Assert.ThrowsAny<Exception>(() =>
                TestRenderHelper.AssertCompareImages(actualPath, expectedPath));

            Assert.Contains(actualPath + ": Error = ", error.Message);
        }
        finally
        {
            File.Delete(expectedPath);
            File.Delete(actualPath);
        }
    }

    private static SKBitmap CreatePixel(SKColor color)
    {
        var image = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        image.SetPixel(0, 0, color);
        return image;
    }
}
