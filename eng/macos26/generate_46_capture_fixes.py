#!/usr/bin/env python3
"""Fix failures identified by the first full ControlCatalog build."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    catalog = ROOT / 'samples/ControlCatalog'
    path = catalog / 'MacOSCatalogPages.cs'
    if not path.exists():
        path.write_text('''using System.Collections.Generic;
using ControlCatalog.Models;
using ControlCatalog.ViewModels;

namespace ControlCatalog;

/// <summary>Exposes the catalog's existing page factories to the screenshot harness.</summary>
public static class MacOSCatalogPages
{
    /// <summary>Creates an isolated catalog page inventory without exposing its view model.</summary>
    public static IReadOnlyList<PageItem> Create() => new MainWindowViewModel().Pages;
}
''')
    path = ROOT / 'samples/ControlCatalog.MacOS/Program.cs'
    text = path.read_text().replace('using ControlCatalog.ViewModels;\n', '')
    text = text.replace('new MainWindowViewModel().Pages', 'MacOSCatalogPages.Create()')
    text = text.replace('bitmap.Save(path);', 'bitmap.Save(path, PngBitmapEncoderOptions.Default);')
    path.write_text(text)
    path = catalog / 'Pages/MacOSThemePage.xaml'
    path.write_text(path.read_text().replace('Watermark="', 'PlaceholderText="'))
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    text = path.read_text().replace('using Avalonia.Media;\n', 'using Avalonia.Media;\nusing Avalonia.Media.Imaging;\n')
    text = text.replace('frame.Save(output);', 'frame.Save(output, PngBitmapEncoderOptions.Default);')
    path.write_text(text)
    commit('fix(macos): use public catalog factories and explicit PNG encoder options',
           'samples/ControlCatalog', 'samples/ControlCatalog.MacOS', 'tests/Avalonia.Themes.MacOS.UnitTests')

if __name__ == '__main__':
    main()
