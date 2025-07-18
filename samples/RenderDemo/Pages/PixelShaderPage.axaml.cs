using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace RenderDemo.Pages;

public partial class PixelShaderPage : UserControl
{
    public PixelShaderPage()
    {
        InitializeComponent();
        var rect = this.FindControl<Rectangle>("Target");
        rect.Effect = new PixelShaderEffect
        {
            ShaderSource = "uniform shader input; half4 main(float2 coord) { half4 c = sample(input, coord); return half4(c.r, c.r, c.r, c.a); }"
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
