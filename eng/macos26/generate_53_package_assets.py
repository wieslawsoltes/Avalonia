#!/usr/bin/env python3
"""Ensure package documentation is included regardless of default None globs."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    path = ROOT / 'src/Avalonia.Themes.MacOS/Avalonia.Themes.MacOS.csproj'
    text = path.read_text()
    text = text.replace('<None Update="README.md" Pack="true" PackagePath="/" />',
        '<None Remove="README.md" />\n    <None Include="README.md" Pack="true" PackagePath="" />')
    text = text.replace('<None Update="Tokens/token-manifest.json" Pack="true" PackagePath="design-tokens/" />',
        '<None Remove="Tokens/token-manifest.json" />\n    <None Include="Tokens/token-manifest.json" Pack="true" PackagePath="design-tokens/" />')
    path.write_text(text)
    commit('fix(macos): explicitly pack documentation and the complete token schema', 'src/Avalonia.Themes.MacOS')

if __name__ == '__main__':
    main()
