#!/usr/bin/env python3
"""One-time, reviewable migrations for the copied macOS theme.

Generated changes are committed to the feature branch, not generated at package
build time. Re-running a completed migration is a no-op. Never edits Fluent.
"""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'


def commit(message, *paths):
    subprocess.run(['git', 'add', '--', *paths], cwd=ROOT, check=True)
    result = subprocess.run(['git', 'diff', '--cached', '--quiet'], cwd=ROOT)
    if result.returncode == 1:
        subprocess.run(['git', 'commit', '-m', message], cwd=ROOT, check=True)
    elif result.returncode != 0:
        raise RuntimeError('Unable to inspect staged changes')


def isolate():
    if (THEME / 'MacOSTheme.xaml').exists():
        return
    if not (THEME / 'FluentTheme.xaml').exists():
        raise RuntimeError('Expected the complete Fluent baseline copy')
    for path in sorted(THEME.rglob('*')):
        if path.is_file() and path.suffix in ('.cs', '.xaml', '.csproj'):
            text = path.read_text(encoding='utf-8-sig')
            text = text.replace('Fluent', 'MacOS').replace('fluent', 'macOS')
            destination = path.with_name(path.name.replace('Fluent', 'MacOS'))
            destination.write_text(text, encoding='utf-8')
            if destination != path:
                path.unlink()
    path = THEME / 'Accents/SystemAccentColors.cs'
    text = path.read_text().replace('Color.FromRgb(0, 120, 215)', 'Color.FromRgb(0, 122, 255)')
    path.write_text(text)
    commit('refactor(macos): isolate theme namespaces, assets and native accent fallback',
           'src/Avalonia.Themes.MacOS')


if __name__ == '__main__':
    isolate()
