# ProGPU rendering for Avalonia

These packages run Avalonia 12 on the ProGPU/WebGPU renderer with Silk.NET windowing.

| Package | Purpose |
| --- | --- |
| `ProGPU.Avalonia.Rendering` | Avalonia renderer backed by ProGPU and WebGPU |
| `ProGPU.Avalonia.SilkNet` | Cross-platform Silk.NET desktop windowing backend |

Version `12.0.5-preview.13` is built against exactly Avalonia `12.0.5` and ProGPU `0.1.0-preview.14` on .NET 10.

## Install

Reference the renderer, windowing backend, text shaper, and font package:

```xml
<ItemGroup>
  <PackageReference Include="Avalonia" Version="12.0.5" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.5" />
  <PackageReference Include="Avalonia.HarfBuzz" Version="12.0.5" />
  <PackageReference Include="ProGPU.Avalonia.Rendering" Version="12.0.5-preview.13" />
  <PackageReference Include="ProGPU.Avalonia.SilkNet" Version="12.0.5-preview.13" />
</ItemGroup>
```

The integration packages carry exact dependencies on their supported Avalonia and ProGPU versions. Upgrade the five references together when a matching integration preview is released.

## Configure the app

Configure Silk.NET windowing, ProGPU rendering, HarfBuzz shaping, and the Inter font before starting the desktop lifetime:

```csharp
using Avalonia;
using Avalonia.Rendering.Composition;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UseSilkNet()
        .UseProGpu()
        .With(new CompositionOptions
        {
            UseRegionDirtyRectClipping = false
        })
        .UseHarfBuzz()
        .WithInterFont();
```

`UseRegionDirtyRectClipping = false` is the current recommended setting for the ProGPU renderer. `UseSkia()` remains a compatibility alias for `UseProGpu()`, but the explicit ProGPU name avoids confusion with Avalonia's Skia renderer.

Start the app normally:

```csharp
[STAThread]
public static void Main(string[] args) =>
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
```

## Use the ProGPU API lease

Custom controls can submit ProGPU scene commands from an Avalonia custom draw operation. Acquire `IProGpuApiLeaseFeature` only inside `ICustomDrawOperation.Render`, dispose the lease before returning, and pass `CurrentTransform` to transform-aware ProGPU methods.

```csharp
using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.ProGpu;
using Avalonia.Rendering.SceneGraph;
using ProGpuBrush = ProGPU.Vector.SolidColorBrush;
using ProGpuRect = ProGPU.Scene.Rect;

public sealed class ProGpuControl : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new DrawOperation(
            new Rect(0, 0, Bounds.Width, Bounds.Height)));
    }

    private sealed class DrawOperation : ICustomDrawOperation
    {
        public DrawOperation(Rect bounds) => Bounds = bounds;

        public Rect Bounds { get; }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<IProGpuApiLeaseFeature>();
            if (feature is null)
                return; // A different Avalonia renderer is active.

            using var lease = feature.Lease();
            var fill = new ProGpuBrush(
                new Vector4(0.09f, 0.72f, 0.96f, 1f));

            lease.DrawingContext.DrawRoundedRectangle(
                fill,
                null,
                new ProGpuRect(
                    0,
                    0,
                    (float)Bounds.Width,
                    (float)Bounds.Height),
                12,
                12,
                lease.CurrentTransform);

            // The active ProGPU.Backend.WgpuContext is also available when
            // custom resources or direct WebGPU work are required.
            var gpuContext = lease.WgpuContext;
        }

        public bool HitTest(Point point) => Bounds.Contains(point);

        public bool Equals(ICustomDrawOperation? other) =>
            other is DrawOperation operation && operation.Bounds == Bounds;

        public void Dispose()
        {
        }
    }
}
```

The lease also exposes `CurrentOpacity`, `PixelSize`, and `Dpi`. Avalonia's current opacity is already represented by the active ProGPU command stack, so do not multiply it into brush alpha a second time.

Lease rules:

- Acquire and dispose the lease on the render thread within the same `Render` call.
- Do not retain the command recorder, `WgpuContext`, native handles, or other leased objects.
- Do not issue Avalonia drawing-context calls while the ProGPU API is leased.
- Balance every ProGPU push command with its matching pop command.
- Treat `IProGpuApiLeaseFeature` and `IProGpuApiLease` as unstable preview APIs.

## Run a WGSL shader through the lease

ProGPU's built-in ShaderToy extension compiles WGSL and encodes it into the compositor's active WebGPU render pass. Record the extension command through the leased drawing context so Avalonia transform, clipping, opacity, and frame ordering remain intact.

Keep each fixed shader in its own `.wgsl` file and embed it with a stable logical name:

```xml
<ItemGroup>
  <EmbeddedResource Include="Shaders/*.wgsl"
                    LogicalName="$(AssemblyName).Shaders.%(Filename)%(Extension)" />
</ItemGroup>
```

Load the source once through ProGPU's cached resource loader. `ApiLeaseWave.wgsl` should define `mainImage` and document its algorithm, time complexity, and space or bandwidth complexity at the top of the file.

```csharp
using System.Numerics;
using ProGPU.Backend;
using ProGPU.Scene.Extensions;

private static readonly string s_wgsl =
    ShaderResource.Load<ProGpuControl>("ApiLeaseWave.wgsl");

var width = (float)Bounds.Width;
var height = (float)Bounds.Height;
var shaderRect = new ProGPU.Scene.Rect(0, 0, width, height);
var shader = new ShaderToyParams
{
    Rect = shaderRect,
    Resolution = new Vector3(width, height, 1),
    Time = 0,
    TimeDelta = 1f / 60f,
    Frame = 0,
    FrameRate = 60,
    ShaderKey = "MyAvaloniaWgslShaderV1",
    ShaderSource = s_wgsl
};

lease.DrawingContext.DrawExtension(
    ProGPU.Scene.CompositorBuiltInExtensions.ShaderToy,
    dataParam: shader,
    transform: lease.CurrentTransform);
```

Keep `ShaderKey` stable for a stable shader source so ProGPU can reuse the compiled WebGPU pipeline. Resource loading and UTF-8 decoding occur once, outside the frame hot path. The package-only sample runs a complete animated version from `integration/ProGpuPackageApp/Shaders/ApiLeaseWave.wgsl`.

## Validate local and published packages

The repository includes a package-only integration app that exercises startup and the ProGPU API lease without project references.

Use packages freshly built from the checkout:

```bash
./integration/ProGpuPackageApp/run.sh local
```

Use packages from NuGet.org only:

```bash
./integration/ProGpuPackageApp/run.sh nuget
```

For a non-interactive restore and build check:

```bash
PROGPU_INTEGRATION_BUILD_ONLY=1 ./integration/ProGpuPackageApp/run.sh local
PROGPU_INTEGRATION_BUILD_ONLY=1 ./integration/ProGpuPackageApp/run.sh nuget
```

Set `PROGPU_INTEGRATION_PACKAGE_VERSION` to test another integration preview.

## Troubleshooting

- `No text shaping system configured`: reference `Avalonia.HarfBuzz` and call `UseHarfBuzz()`.
- Missing default typeface: reference `Avalonia.Fonts.Inter` and call `WithInterFont()`.
- Incomplete dirty-region updates: set `UseRegionDirtyRectClipping = false`.
- `IProGpuApiLeaseFeature` is unavailable: verify that `UseProGpu()` selected the active renderer.

Source, issues, and integration history are available in the [Avalonia ProGPU branch](https://github.com/wieslawsoltes/Avalonia/tree/feature/progpu).
