#!/usr/bin/env python3
"""Consume the intermediate theme package in an isolated, package-only application.

The repository's production pipeline subsequently merges its framework packages
using Numerge. This smoke test deliberately validates the raw development feed
without relying on project references or the runner's global NuGet cache.
"""
from pathlib import Path
from xml.etree import ElementTree as ET
import hashlib
import json
import os
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/macos-theme'
FEED = OUTPUT / 'packages'
NS = {'n': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}


def metadata(path):
    with zipfile.ZipFile(path) as package:
        name = next(name for name in package.namelist() if name.endswith('.nuspec'))
        return ET.fromstring(package.read(name))


def run(*args, cwd=ROOT, env=None):
    subprocess.run(args, cwd=cwd, env=env, check=True, timeout=600)


def main():
    packages = list(FEED.glob('Avalonia.Themes.MacOS.*.nupkg'))
    if len(packages) != 1:
        raise RuntimeError('Expected exactly one freshly built theme package')
    theme = packages[0]
    version = metadata(theme).find('n:metadata/n:version', NS).text
    pending, built = ['Avalonia.Headless', 'Avalonia.Themes.MacOS'], set()
    projects = {p.stem: p for directory in ('src', 'packages')
                for p in (ROOT / directory).rglob('*.csproj')}
    while pending:
        name = pending.pop()
        if name in built:
            continue
        package = FEED / f'{name}.{version}.nupkg'
        if not package.exists():
            if name not in projects:
                raise RuntimeError('No source project for dependency ' + name)
            run('dotnet', 'pack', str(projects[name]), '-c', 'Release', '-o', str(FEED))
        built.add(name)
        for dependency in metadata(package).findall('.//n:dependency', NS):
            dependency_name = dependency.get('id')
            if dependency_name in projects and dependency_name not in built:
                pending.append(dependency_name)
    with tempfile.TemporaryDirectory(prefix='macos-theme-consumer-') as temporary:
        directory = Path(temporary)
        config = ET.Element('configuration')
        sources = ET.SubElement(config, 'packageSources')
        ET.SubElement(sources, 'clear')
        ET.SubElement(sources, 'add', key='matching-source-build', value=str(FEED))
        ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
        ET.ElementTree(config).write(directory / 'NuGet.Config', encoding='utf-8', xml_declaration=True)
        (directory / 'Consumer.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup><PackageReference Include="Avalonia.Themes.MacOS" Version="{version}"/><PackageReference Include="Avalonia.Headless" Version="{version}"/></ItemGroup>
</Project>''')
        (directory / 'Program.cs').write_text('''using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.MacOS;
using Avalonia.Threading;

AppBuilder.Configure<ConsumerApplication>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
    .SetupWithoutStarting();
var theme = new MacOSTheme();
Application.Current!.Styles.Add(theme);
if (!((IResourceNode)theme).TryGetResource(typeof(Button), ThemeVariant.Light, out var resource)
    || resource is not ControlTheme)
    throw new InvalidOperationException("Packaged control resources did not load.");
var button = new Button { Content = "Package consumer" };
var window = new Window { Width = 320, Height = 160, Content = button };
window.Show();
theme.SetToken(MacOSTokens.ButtonPadding, new Thickness(25, 8));
Dispatcher.UIThread.RunJobs();
if (button.Padding != new Thickness(25, 8))
    throw new InvalidOperationException("Packaged live token override did not propagate.");
window.Close();
Console.WriteLine("PASS: package-only consumer loaded compiled XAML and live design tokens.");
public sealed class ConsumerApplication : Application { }
''')
        environment = dict(os.environ, NUGET_PACKAGES=str(directory / 'isolated-nuget-cache'))
        run('dotnet', 'restore', 'Consumer.csproj', '--configfile', 'NuGet.Config', cwd=directory, env=environment)
        run('dotnet', 'run', '--project', 'Consumer.csproj', '-c', 'Release', '--no-restore', cwd=directory, env=environment)
    (OUTPUT / 'package-consumption.json').write_text(json.dumps({
        'status': 'passed', 'version': version, 'framework': 'net10.0',
        'packageOnly': True, 'isolatedNugetCache': True,
        'feedKind': 'matching-source intermediate packages, before production Numerge',
        'packages': sorted(built), 'themeSha256': hashlib.sha256(theme.read_bytes()).hexdigest()
    }, indent=2) + '\n')

if __name__ == '__main__':
    main()
