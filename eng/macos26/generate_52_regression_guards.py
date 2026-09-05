#!/usr/bin/env python3
"""Turn the migration failure into a permanent structural regression guard."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    path.write_text(path.read_text().replace('Assert.Single(types.Where(t => t.Name == name))', 'Assert.Single(types, t => t.Name == name)'))
    path = ROOT / 'eng/macos26/generate_20_appearance.py'
    lines = path.read_text().splitlines()
    for index, line in enumerate(lines):
        if "text = re.sub(r'<(?:SolidColorBrush|StaticResource)" in line:
            lines[index] = "        text = re.sub(r'<(?:SolidColorBrush|StaticResource)\\b[^>]*?/>|<SolidColorBrush\\b[^>]*>[^<]*</SolidColorBrush>', brush, text)"
    path.write_text('\n'.join(lines) + '\n')
    path = ROOT / 'eng/macos26/audit.py'
    text = path.read_text()
    marker = '    for key in references - tokens:'
    if 'Duplicate keys in resource dictionary' not in text:
        text = text.replace(marker, '''    for path in THEME.rglob('*.xaml'):
        for dictionary in ET.parse(path).iter():
            if dictionary.tag.rsplit('}', 1)[-1] != 'ResourceDictionary':
                continue
            keys = [node.get(X + 'Key') for node in dictionary if node.get(X + 'Key')]
            if len(keys) != len(set(keys)):
                errors.append('Duplicate keys in resource dictionary: ' + str(path.relative_to(ROOT)))
''' + marker)
    path.write_text(text)
    commit('test(macos): guard resource declaration integrity and satisfy xUnit analyzers',
           'tests/Avalonia.Themes.MacOS.UnitTests', 'eng/macos26')

if __name__ == '__main__':
    main()
