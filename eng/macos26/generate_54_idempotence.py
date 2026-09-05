#!/usr/bin/env python3
"""Normalize imports and prevent repeat migration from introducing duplicates."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    path = ROOT / 'eng/macos26/generate_46_capture_fixes.py'
    text = path.read_text()
    old = "text = path.read_text().replace('using Avalonia.Media;\\n', 'using Avalonia.Media;\\nusing Avalonia.Media.Imaging;\\n')"
    new = "text = path.read_text().replace('using Avalonia.Media.Imaging;\\n', '')\n    text = text.replace('using Avalonia.Media;\\n', 'using Avalonia.Media;\\nusing Avalonia.Media.Imaging;\\n')"
    path.write_text(text.replace(old, new))
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    lines, imports = [], set()
    for line in path.read_text().splitlines():
        if line.startswith('using '):
            if line in imports:
                continue
            imports.add(line)
        lines.append(line)
    path.write_text('\n'.join(lines).replace('Assert.Single(types.Where(t => t.Name == name))', 'Assert.Single(types, t => t.Name == name)') + '\n')
    path = ROOT / 'eng/macos26/audit.py'
    path.write_text(path.read_text().replace("r'x:Name=", "r'(?:x:)?Name="))
    commit('fix(macos): stabilize repeated migrations and strengthen template-part audit',
           'eng/macos26', 'tests/Avalonia.Themes.MacOS.UnitTests')

if __name__ == '__main__':
    main()
