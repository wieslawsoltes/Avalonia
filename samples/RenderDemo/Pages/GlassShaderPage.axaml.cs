using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using RenderDemo.ViewModels;

namespace RenderDemo.Pages;

public partial class GlassShaderPage : UserControl
{
    private readonly PixelShaderEffect _effect = new();
    private readonly GlassShaderViewModel _vm;

    private const string ShaderTemplate = @"uniform shader input;

half4 main(float2 coord)
{
    const float angle = {0};
    const float intensity = {1};
    const float refraction = {2};
    const float depth = {3};
    const float dispersion = {4};
    const float frost = {5};

    float2 dir = float2(cos(angle), sin(angle));
    float noise = fract(sin(dot(coord.xy, float2(12.9898,78.233))) * 43758.5453);
    float2 offset = dir * ((noise * depth) + refraction) * frost;
    half4 color = sample(input, coord + offset);
    color.rgb += noise * intensity;
    color.r += offset.x * dispersion;
    color.b += offset.y * dispersion;
    color = mix(color, half4(1.0, 1.0, 1.0, color.a), frost * 0.25);
    return color;
}";

    public GlassShaderPage()
    {
        InitializeComponent();
        _vm = DataContext as GlassShaderViewModel ?? new GlassShaderViewModel();
        DataContext = _vm;
        var rect = this.FindControl<Rectangle>("Target");
        rect.Effect = _effect;
        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateShader();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateShader();
    }

    private void UpdateShader()
    {
        _effect.ShaderSource = string.Format(CultureInfo.InvariantCulture, ShaderTemplate,
            _vm.Angle,
            _vm.Intensity,
            _vm.Refraction,
            _vm.Depth,
            _vm.Dispersion,
            _vm.Frost);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
