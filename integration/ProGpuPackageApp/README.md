# ProGPU package integration app

This code-only desktop app verifies the published integration surface without any Avalonia or ProGPU `ProjectReference`. It starts with Silk.NET windowing and the ProGPU renderer, then uses `IProGpuApiLeaseFeature` from a custom draw operation to submit ProGPU vector commands and an animated WGSL shader through the WebGPU ShaderToy extension. The shader lives in `Shaders/ApiLeaseWave.wgsl`, is embedded at build time, and is decoded once through `ShaderResource`.

Use freshly packed packages from this checkout:

```bash
./integration/ProGpuPackageApp/run.sh local
```

Local mode also consumes ProGPU runtime packages from the sibling `../ProGPU/artifacts/packages/Release` directory. Set `PROGPU_PACKAGE_SOURCE` when the ProGPU checkout or package output is elsewhere.

Use only packages indexed on NuGet.org:

```bash
./integration/ProGpuPackageApp/run.sh nuget
```

Both modes use a temporary NuGet configuration, HTTP cache, and global-packages folder. Local mode packs the integration packages first and puts the local package source before NuGet.org.

Run a deterministic native smoke that opens maximized, renders the API-lease and WGSL example, and closes after two seconds:

```bash
PROGPU_INTEGRATION_SMOKE=1 ./integration/ProGpuPackageApp/run.sh local
PROGPU_INTEGRATION_SMOKE=1 ./integration/ProGpuPackageApp/run.sh nuget
```

For a non-interactive compile check, set `PROGPU_INTEGRATION_BUILD_ONLY=1`:

```bash
PROGPU_INTEGRATION_BUILD_ONLY=1 ./integration/ProGpuPackageApp/run.sh local
PROGPU_INTEGRATION_BUILD_ONLY=1 ./integration/ProGpuPackageApp/run.sh nuget
```

Override `PROGPU_INTEGRATION_PACKAGE_VERSION` to validate another integration preview.
