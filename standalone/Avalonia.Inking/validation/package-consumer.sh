#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
# Exercise the real shared sample UI as well as isolated reusable controls.
dotnet run --project "$root/tests/InkingStudio.UiSpecs" -c Release -- "$root/artifacts/validation/studio-ui.json"
consumer="$(mktemp -d)"
trap 'rm -rf "$consumer"' EXIT
export NUGET_PACKAGES="$consumer/packages"
cat > "$consumer/Consumer.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="Avalonia.Inking.Themes" Version="0.1.0" /></ItemGroup>
</Project>
PROJECT
cat > "$consumer/NuGet.Config" <<CONFIG
<configuration>
  <packageSources><clear/><add key="local" value="$root/artifacts/packages"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/><packageSource key="local"><package pattern="Avalonia.Inking*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping>
</configuration>
CONFIG
cat > "$consumer/Program.cs" <<'CS'
using Avalonia.Inking;
using Avalonia.Inking.Core;
using Avalonia.Inking.Themes;
var session = new InkSession();
session.AddStroke(new InkStroke(new[] { new InkPoint(1, 2, .25), new InkPoint(10, 20, .9) }));
var roundtrip = InkSerializer.Deserialize(InkSerializer.Serialize(session.Document));
if (roundtrip.StrokeCount != 1 || roundtrip.PointCount != 2) throw new Exception("Package roundtrip failed.");
if (!typeof(InkCanvas).IsSubclassOf(typeof(Avalonia.Controls.Primitives.TemplatedControl))) throw new Exception("Missing templated control.");
if (!typeof(InkTheme).IsSubclassOf(typeof(Avalonia.Styling.Styles))) throw new Exception("Missing packaged theme.");
Console.WriteLine("PASS: isolated NuGet consumer resolves all three locally built inking packages and executes the core.");
CS
cd "$consumer"
dotnet run --project Consumer.csproj -c Release
