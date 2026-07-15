using System;
using System.IO;
using Xunit;

namespace Avalonia.ProGpu.UnitTests
{
    public class ProGpuPackagingContractTests
    {
        [Fact]
        public void IntegrationProjectsUseProGpuPackageIdsAndExactVersionPins()
        {
            var properties = ReadRepoFile("build", "ProGpuIntegration.props");
            var renderer = ReadRepoFile("src", "ProGpu", "Avalonia.ProGpu", "Avalonia.ProGpu.csproj");
            var windowing = ReadRepoFile("src", "Windows", "Avalonia.SilkNet", "Avalonia.SilkNet.csproj");
            var packageVersions = ReadRepoFile("Directory.Packages.props");

            Assert.Contains(">Source</ProGpuDependencyMode>", properties, StringComparison.Ordinal);
            Assert.Contains(">12.0.5</ProGpuAvaloniaVersion>", properties, StringComparison.Ordinal);
            Assert.Contains(">0.1.0-preview.16</ProGpuRuntimeVersion>", properties, StringComparison.Ordinal);
            Assert.Contains(">12.0.5-preview.14</ProGpuIntegrationVersion>", properties, StringComparison.Ordinal);
            Assert.Contains("<PackageIcon>ProGpuAvaloniaIcon.png</PackageIcon>", properties, StringComparison.Ordinal);
            Assert.Contains("docs/progpu-package-readme.md", properties, StringComparison.Ordinal);
            Assert.Contains("<None Remove=\"$(MSBuildThisFileDirectory)Assets/Icon.png\"", properties, StringComparison.Ordinal);
            Assert.Contains("Assets/ProGpuAvaloniaIcon.svg", properties, StringComparison.Ordinal);

            Assert.Contains("<PackageId>ProGPU.Avalonia.Rendering</PackageId>", renderer, StringComparison.Ordinal);
            Assert.Contains("<PackageId>ProGPU.Avalonia.SilkNet</PackageId>", windowing, StringComparison.Ordinal);
            Assert.DoesNotContain("<PackageId>Avalonia.", renderer, StringComparison.Ordinal);
            Assert.DoesNotContain("<PackageId>Avalonia.", windowing, StringComparison.Ordinal);
            Assert.Contains("VersionOverride=\"[$(ProGpuAvaloniaVersion)]\"", renderer, StringComparison.Ordinal);
            Assert.Contains("VersionOverride=\"[$(ProGpuAvaloniaVersion)]\"", windowing, StringComparison.Ordinal);
            Assert.Contains("<PackageReference Include=\"OpenFontSharp\" />", renderer, StringComparison.Ordinal);
            Assert.Contains("<PackageReference Include=\"StbImageSharp\" />", renderer, StringComparison.Ordinal);
            Assert.Contains("<PackageVersion Include=\"OpenFontSharp\" Version=\"1.0.0\" />", packageVersions, StringComparison.Ordinal);
            Assert.Contains("<PackageVersion Include=\"StbImageSharp\" Version=\"2.30.15\" />", packageVersions, StringComparison.Ordinal);
        }

        [Fact]
        public void ControlCatalogDefaultsToProGpuOnSilkNet()
        {
            var program = ReadRepoFile("samples", "ControlCatalog.Desktop", "Program.cs");

            Assert.Contains("args.Contains(\"--skia\")", program, StringComparison.Ordinal);
            Assert.Contains(": BuildAvaloniaApp();", program, StringComparison.Ordinal);
            Assert.Contains(".UseSilkNet()", program, StringComparison.Ordinal);
            Assert.Contains(".UseProGpu()", program, StringComparison.Ordinal);
            Assert.Contains("UseRegionDirtyRectClipping = false", program, StringComparison.Ordinal);
        }

        [Fact]
        public void WebGpuPresentationAvoidsReadbackAndReleasesAcquiredTextures()
        {
            var directDrawingContext = ReadRepoFile(
                "src", "ProGpu", "Avalonia.ProGpu", "DrawingContextImpl.cs");
            var shimTarget = ReadRepoFile(
                "src", "Skia", "Avalonia.SkiaShim", "WebGpuFramebufferTarget.cs");
            var program = ReadRepoFile("samples", "ControlCatalog.Desktop", "Program.cs");

            Assert.Contains("GpuTextureBlitter.Blit", shimTarget, StringComparison.Ordinal);
            Assert.Contains("GpuTextureBlitter.Blit", directDrawingContext, StringComparison.Ordinal);
            Assert.DoesNotContain("ReadPixels", shimTarget, StringComparison.Ordinal);
            Assert.Contains("TextureRelease(surfaceTexture.Texture)", shimTarget, StringComparison.Ordinal);
            Assert.Contains("TextureRelease(surfaceTexture.Texture)", directDrawingContext, StringComparison.Ordinal);
            Assert.Contains("private static AppBuilder BuildSkiaShimApp()", program, StringComparison.Ordinal);
            Assert.Contains(".UseSilkNet()", program, StringComparison.Ordinal);
        }

        [Fact]
        public void PackagingScriptsAndDocumentationCoverBothArtifacts()
        {
            var packageList = ReadRepoFile("scripts", "progpu-package-list.sh");
            var pack = ReadRepoFile("scripts", "progpu-pack.sh");
            var publish = ReadRepoFile("scripts", "progpu-publish.sh");
            var documentation = ReadRepoFile("docs", "progpu-packaging.md");
            var packageReadme = ReadRepoFile("docs", "progpu-package-readme.md");

            foreach (var packageId in new[] { "ProGPU.Avalonia.Rendering", "ProGPU.Avalonia.SilkNet" })
            {
                Assert.Contains(packageId, packageList, StringComparison.Ordinal);
                Assert.Contains(packageId, documentation, StringComparison.Ordinal);
            }

            Assert.Contains("ProGpuDependencyMode=Package", pack, StringComparison.Ordinal);
            Assert.Contains("PROGPU_PACKAGE_SOURCE", pack, StringComparison.Ordinal);
            Assert.Contains("NUGET_HTTP_CACHE_PATH", pack, StringComparison.Ordinal);
            Assert.Contains("--artifacts-path", pack, StringComparison.Ordinal);
            Assert.Contains("NUGET_API_KEY", publish, StringComparison.Ordinal);
            Assert.Contains("--skip-duplicate", publish, StringComparison.Ordinal);
            Assert.DoesNotContain(".snupkg", publish, StringComparison.Ordinal);
            Assert.Contains(".UseHarfBuzz()", documentation, StringComparison.Ordinal);
            Assert.Contains(".WithInterFont()", documentation, StringComparison.Ordinal);
            Assert.Contains("IProGpuApiLeaseFeature", packageReadme, StringComparison.Ordinal);
            Assert.Contains("lease.CurrentTransform", packageReadme, StringComparison.Ordinal);
            Assert.Contains("ShaderToyParams", packageReadme, StringComparison.Ordinal);
            Assert.Contains("ShaderResource.Load", packageReadme, StringComparison.Ordinal);
            Assert.Contains("ApiLeaseWave.wgsl", packageReadme, StringComparison.Ordinal);
        }

        [Fact]
        public void IntegrationAppConsumesOnlyLocalOrNuGetPackages()
        {
            var project = ReadRepoFile("integration", "ProGpuPackageApp", "ProGpuPackageApp.csproj");
            var program = ReadRepoFile("integration", "ProGpuPackageApp", "Program.cs");
            var leaseView = ReadRepoFile("integration", "ProGpuPackageApp", "ProGpuLeaseView.cs");
            var shader = ReadRepoFile(
                "integration", "ProGpuPackageApp", "Shaders", "ApiLeaseWave.wgsl");
            var runScript = ReadRepoFile("integration", "ProGpuPackageApp", "run.sh");
            var drawingContext = ReadRepoFile(
                "src", "ProGpu", "Avalonia.ProGpu", "DrawingContextImpl.cs");
            var lockedFramebuffer = ReadRepoFile(
                "src", "Windows", "Avalonia.SilkNet", "SilkNetLockedFramebuffer.cs");

            Assert.Contains("ProGPU.Avalonia.Rendering", project, StringComparison.Ordinal);
            Assert.Contains("ProGPU.Avalonia.SilkNet", project, StringComparison.Ordinal);
            Assert.Contains("Avalonia.HarfBuzz", project, StringComparison.Ordinal);
            Assert.Contains("Avalonia.Fonts.Inter", project, StringComparison.Ordinal);
            Assert.Contains("EmbeddedResource Include=\"Shaders/*.wgsl\"", project, StringComparison.Ordinal);
            Assert.Contains("$(AssemblyName).Shaders.%(Filename)%(Extension)", project, StringComparison.Ordinal);
            Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
            Assert.Contains(".UseSilkNet()", program, StringComparison.Ordinal);
            Assert.Contains(".UseProGpu()", program, StringComparison.Ordinal);
            Assert.Contains("UseRegionDirtyRectClipping = false", program, StringComparison.Ordinal);
            Assert.Contains(".UseHarfBuzz()", program, StringComparison.Ordinal);
            Assert.Contains(".WithInterFont()", program, StringComparison.Ordinal);
            Assert.Contains("IProGpuApiLeaseFeature", leaseView, StringComparison.Ordinal);
            Assert.Contains("lease.CurrentTransform", leaseView, StringComparison.Ordinal);
            Assert.Contains("ShaderToyParams", leaseView, StringComparison.Ordinal);
            Assert.Contains("ShaderResource.Load<ProGpuDrawOperation>(\"ApiLeaseWave.wgsl\")", leaseView, StringComparison.Ordinal);
            Assert.DoesNotContain("fn mainImage", leaseView, StringComparison.Ordinal);
            Assert.Contains("// Algorithm:", shader, StringComparison.Ordinal);
            Assert.Contains("// Time complexity:", shader, StringComparison.Ordinal);
            Assert.Contains("// Space complexity:", shader, StringComparison.Ordinal);
            Assert.Contains("fn mainImage", shader, StringComparison.Ordinal);
            Assert.Contains("IPlatformHandle", lockedFramebuffer, StringComparison.Ordinal);
            Assert.Contains("WGPU_SURFACE", lockedFramebuffer, StringComparison.Ordinal);
            Assert.Contains("WGPU_SURFACE", drawingContext, StringComparison.Ordinal);
            Assert.DoesNotContain("IProGpuSurfaceFramebuffer", drawingContext, StringComparison.Ordinal);
            Assert.Contains("local)", runScript, StringComparison.Ordinal);
            Assert.Contains("nuget)", runScript, StringComparison.Ordinal);
            Assert.Contains("--configfile", runScript, StringComparison.Ordinal);
            Assert.Contains("--artifacts-path", runScript, StringComparison.Ordinal);
        }

        private static string ReadRepoFile(params string[] path)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, Path.Combine(path));
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);

                directory = directory.Parent;
            }

            throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(path)}'.");
        }
    }
}
