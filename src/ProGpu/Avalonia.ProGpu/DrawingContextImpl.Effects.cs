using System;
using System.Collections.Generic;
using Avalonia.Media;
using ProGPU.Scene;
using ProGPU.Backend;

namespace Avalonia.ProGpu;

public class BlendEffect : IEffect
{
    public GpuBlendMode BlendMode { get; }

    public BlendEffect(GpuBlendMode blendMode)
    {
        BlendMode = blendMode;
    }
}

partial class DrawingContextImpl
{
    private readonly Stack<bool> _pushedBlendModeStack = new();

    public void PushEffect(Avalonia.Rect? effectClipRect, IEffect effect)
    {
        bool isBlend = false;
        if (effect is BlendEffect blendEffect)
        {
            DrawingContext.PushBlendMode(blendEffect.BlendMode);
            isBlend = true;
        }
        _pushedBlendModeStack.Push(isBlend);

        if (effectClipRect.HasValue)
        {
            DrawingContext.PushClip(ToProGpuRect(effectClipRect.Value));
        }
    }

    public void PopEffect()
    {
        if (_pushedBlendModeStack.Count > 0 && _pushedBlendModeStack.Pop())
        {
            DrawingContext.PopBlendMode();
        }

        DrawingContext.PopClip();
    }
}
