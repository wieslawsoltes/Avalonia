using MiniMvvm;

namespace RenderDemo.ViewModels;

public class GlassShaderViewModel : ViewModelBase
{
    private double _angle;
    private double _intensity = 0.2;
    private double _refraction = 2.0;
    private double _depth = 1.0;
    private double _dispersion = 0.1;
    private double _frost = 0.5;

    public double Angle
    {
        get => _angle;
        set => RaiseAndSetIfChanged(ref _angle, value);
    }

    public double Intensity
    {
        get => _intensity;
        set => RaiseAndSetIfChanged(ref _intensity, value);
    }

    public double Refraction
    {
        get => _refraction;
        set => RaiseAndSetIfChanged(ref _refraction, value);
    }

    public double Depth
    {
        get => _depth;
        set => RaiseAndSetIfChanged(ref _depth, value);
    }

    public double Dispersion
    {
        get => _dispersion;
        set => RaiseAndSetIfChanged(ref _dispersion, value);
    }

    public double Frost
    {
        get => _frost;
        set => RaiseAndSetIfChanged(ref _frost, value);
    }
}
