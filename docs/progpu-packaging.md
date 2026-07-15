# ProGPU rendering and Silk.NET windowing

The ProGPU integration has two development modes and ships as two preview packages:

| Package | Assembly | Purpose |
| --- | --- | --- |
| `ProGPU.Avalonia.Rendering` `12.0.5-preview.16` | `Avalonia.ProGpu` | ProGPU/WebGPU rendering backend |
| `ProGPU.Avalonia.SilkNet` `12.0.5-preview.16` | `Avalonia.SilkNet` | Cross-platform Silk.NET windowing backend |

Both packages are built against exactly Avalonia `12.0.5` and ProGPU `0.1.0-preview.18`. They intentionally use `ProGPU.*` package IDs; no `Avalonia.*` package ID is produced by this release lane.

The NuGet package page uses `docs/progpu-package-readme.md`. Keep its install, startup, API lease, and troubleshooting instructions current when package contracts change.

The original package artwork is maintained as `build/Assets/ProGpuAvaloniaIcon.svg` and rendered to `build/Assets/ProGpuAvaloniaIcon.png`. NuGet uses the PNG, and both files are included in each integration package.

## Development modes

Source mode is the default inside this repository. It references the local Avalonia projects and a sibling ProGPU checkout at `../ProGPU`:

```bash
dotnet build src/ProGpu/Avalonia.ProGpu/Avalonia.ProGpu.csproj
dotnet build src/Windows/Avalonia.SilkNet/Avalonia.SilkNet.csproj
```

Set `ProGpuSourceRoot` when the ProGPU checkout is elsewhere:

```bash
dotnet build src/ProGpu/Avalonia.ProGpu/Avalonia.ProGpu.csproj \
  -p:ProGpuSourceRoot=/absolute/path/to/ProGPU
```

Package mode removes those source references and consumes official packages:

```bash
dotnet build src/ProGpu/Avalonia.ProGpu/Avalonia.ProGpu.csproj \
  -p:ProGpuDependencyMode=Package
dotnet build src/Windows/Avalonia.SilkNet/Avalonia.SilkNet.csproj \
  -p:ProGpuDependencyMode=Package
```

Package mode enables Avalonia's unstable private-API build target. Warning `AVA3001` is expected. The integration package dependencies are exact pins, so upgrading Avalonia requires rebuilding and publishing a matching ProGPU integration version.

## Control Catalog defaults

The desktop Control Catalog starts with Silk.NET windowing and ProGPU rendering when no renderer argument is supplied:

```bash
dotnet run --project samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj
```

Pass `--skia` to opt into Avalonia's regular Skia renderer.

## Pack locally

Publish ProGPU `0.1.0-preview.18` first, then pack the integrations:

```bash
./scripts/progpu-pack.sh
```

To validate against packages from a local ProGPU checkout before they reach NuGet.org, provide its package output as an absolute path. The script creates an isolated NuGet configuration and package cache so an older extraction of the same preview version cannot be reused:

```bash
PROGPU_PACKAGE_SOURCE=/absolute/path/to/ProGPU/artifacts/packages/Release \
  ./scripts/progpu-pack.sh
```

The expected output is four files under `artifacts/packages/Release`: one `.nupkg` and one `.snupkg` for each package ID.

## Publish to NuGet.org

Keep the API key out of command history and repository files. From Bash:

```bash
read -rsp "NuGet API key: " NUGET_API_KEY && printf '\n'
export NUGET_API_KEY
./scripts/progpu-publish.sh
unset NUGET_API_KEY
```

`progpu-publish.sh` repacks, validates all expected artifacts, and pushes each package with `--skip-duplicate`; `dotnet nuget push` discovers and uploads the matching symbol package automatically. Override `NUGET_SOURCE` only when publishing to another NuGet-compatible server.

Release order:

1. Tag and publish ProGPU `0.1.0-preview.18`.
2. Confirm the required ProGPU packages are available from NuGet.org.
3. Pack and test the Avalonia integrations in package mode.
4. Publish `ProGPU.Avalonia.Rendering` and `ProGPU.Avalonia.SilkNet` `12.0.5-preview.16`.

## Consume the packages

```xml
<ItemGroup>
  <PackageReference Include="Avalonia" Version="12.0.5" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.5" />
  <PackageReference Include="Avalonia.HarfBuzz" Version="12.0.5" />
  <PackageReference Include="ProGPU.Avalonia.Rendering" Version="12.0.5-preview.16" />
  <PackageReference Include="ProGPU.Avalonia.SilkNet" Version="12.0.5-preview.16" />
</ItemGroup>
```

Configure both backends before starting the desktop lifetime:

```csharp
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

`UseSkia()` remains available as a compatibility alias for the ProGPU renderer, but `UseProGpu()` avoids ambiguity with Avalonia's Skia package.

Use `IProGpuApiLeaseFeature` from `ICustomDrawOperation.Render` for scoped access to the ProGPU scene command recorder and active `WgpuContext`. The complete vector drawing and WGSL ShaderToy examples, plus the lease lifetime rules, are in `docs/progpu-package-readme.md` and `integration/ProGpuPackageApp/ProGpuLeaseView.cs`.
