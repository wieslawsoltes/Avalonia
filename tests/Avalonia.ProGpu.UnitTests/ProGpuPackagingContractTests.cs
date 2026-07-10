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

            Assert.Contains(">Source</ProGpuDependencyMode>", properties, StringComparison.Ordinal);
            Assert.Contains(">12.0.5</ProGpuAvaloniaVersion>", properties, StringComparison.Ordinal);
            Assert.Contains(">0.1.0-preview.2</ProGpuRuntimeVersion>", properties, StringComparison.Ordinal);
            Assert.Contains(">12.0.5-preview.1</ProGpuIntegrationVersion>", properties, StringComparison.Ordinal);
            Assert.Contains("<PackageIcon>ProGpuAvaloniaIcon.png</PackageIcon>", properties, StringComparison.Ordinal);
            Assert.Contains("<None Remove=\"$(MSBuildThisFileDirectory)Assets/Icon.png\"", properties, StringComparison.Ordinal);
            Assert.Contains("Assets/ProGpuAvaloniaIcon.svg", properties, StringComparison.Ordinal);

            Assert.Contains("<PackageId>ProGPU.Avalonia.Rendering</PackageId>", renderer, StringComparison.Ordinal);
            Assert.Contains("<PackageId>ProGPU.Avalonia.SilkNet</PackageId>", windowing, StringComparison.Ordinal);
            Assert.DoesNotContain("<PackageId>Avalonia.", renderer, StringComparison.Ordinal);
            Assert.DoesNotContain("<PackageId>Avalonia.", windowing, StringComparison.Ordinal);
            Assert.Contains("VersionOverride=\"[$(ProGpuAvaloniaVersion)]\"", renderer, StringComparison.Ordinal);
            Assert.Contains("VersionOverride=\"[$(ProGpuAvaloniaVersion)]\"", windowing, StringComparison.Ordinal);
        }

        [Fact]
        public void PackagingScriptsAndDocumentationCoverBothArtifacts()
        {
            var packageList = ReadRepoFile("scripts", "progpu-package-list.sh");
            var pack = ReadRepoFile("scripts", "progpu-pack.sh");
            var publish = ReadRepoFile("scripts", "progpu-publish.sh");
            var documentation = ReadRepoFile("docs", "progpu-packaging.md");

            foreach (var packageId in new[] { "ProGPU.Avalonia.Rendering", "ProGPU.Avalonia.SilkNet" })
            {
                Assert.Contains(packageId, packageList, StringComparison.Ordinal);
                Assert.Contains(packageId, documentation, StringComparison.Ordinal);
            }

            Assert.Contains("ProGpuDependencyMode=Package", pack, StringComparison.Ordinal);
            Assert.Contains("PROGPU_PACKAGE_SOURCE", pack, StringComparison.Ordinal);
            Assert.Contains("NUGET_API_KEY", publish, StringComparison.Ordinal);
            Assert.Contains("--skip-duplicate", publish, StringComparison.Ordinal);
            Assert.DoesNotContain(".snupkg", publish, StringComparison.Ordinal);
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
