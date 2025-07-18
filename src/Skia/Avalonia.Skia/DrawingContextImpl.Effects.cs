using System;
using Avalonia.Media;
using SkiaSharp;

namespace Avalonia.Skia;

partial class DrawingContextImpl
{
    
    public void PushEffect(IEffect effect)
    {
        CheckLease();
        using var filter = CreateEffect(effect);
        var paint = SKPaintCache.Shared.Get();
        paint.ImageFilter = filter;
        Canvas.SaveLayer(paint);
        SKPaintCache.Shared.ReturnReset(paint);
    }

    public void PopEffect()
    {
        CheckLease();
        Canvas.Restore();
    }

    SKImageFilter? CreateEffect(IEffect effect)
    {
        if (effect is IBlurEffect blur)
        {
            if (blur.Radius <= 0)
                return null;
            var sigma = SkBlurRadiusToSigma(blur.Radius);
            return SKImageFilter.CreateBlur(sigma, sigma);
        }

        if (effect is IDropShadowEffect drop)
        {
            var sigma = drop.BlurRadius > 0 ? SkBlurRadiusToSigma(drop.BlurRadius) : 0;
            var alpha = drop.Color.A * drop.Opacity;
            if (!_useOpacitySaveLayer)
                alpha *= _currentOpacity;
            var color = new SKColor(drop.Color.R, drop.Color.G, drop.Color.B, (byte)Math.Max(0, Math.Min(255, alpha)));

            return SKImageFilter.CreateDropShadow((float)drop.OffsetX, (float)drop.OffsetY, sigma, sigma, color);
        }

        if (effect is IPixelShaderEffect pixelShader)
        {
            var runtimeEffect = SKRuntimeEffect.Create(pixelShader.ShaderSource, out var errors);
            if (runtimeEffect is not null)
            {
                using var colorFilter = runtimeEffect.ToColorFilter();
                return SKImageFilter.CreateColorFilter(colorFilter);
            }
            else
            {
                throw new InvalidOperationException($"Failed to compile shader: {errors}");
            }
        }

        return null;
    }
    
}
