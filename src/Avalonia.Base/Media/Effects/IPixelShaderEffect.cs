// ReSharper disable once CheckNamespace
using Avalonia.Animation.Animators;

namespace Avalonia.Media;

public interface IPixelShaderEffect : IEffect
{
    string ShaderSource { get; }
}

public class ImmutablePixelShaderEffect : IPixelShaderEffect, IImmutableEffect
{
    static ImmutablePixelShaderEffect()
    {
        EffectAnimator.EnsureRegistered();
    }

    public ImmutablePixelShaderEffect(string shaderSource)
    {
        ShaderSource = shaderSource;
    }

    public string ShaderSource { get; }

    public bool Equals(IEffect? other) =>
        other is IPixelShaderEffect ps && ps.ShaderSource == ShaderSource;
}

public sealed class PixelShaderEffect : Effect, IPixelShaderEffect, IMutableEffect
{
    public static readonly StyledProperty<string> ShaderSourceProperty =
        AvaloniaProperty.Register<PixelShaderEffect, string>(nameof(ShaderSource));

    static PixelShaderEffect()
    {
        AffectsRender<PixelShaderEffect>(ShaderSourceProperty);
    }

    public string ShaderSource
    {
        get => GetValue(ShaderSourceProperty);
        set => SetValue(ShaderSourceProperty, value);
    }

    public IImmutableEffect ToImmutable() => new ImmutablePixelShaderEffect(ShaderSource);
}
