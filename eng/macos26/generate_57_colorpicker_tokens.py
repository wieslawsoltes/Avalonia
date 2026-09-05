#!/usr/bin/env python3
"""Keep ColorPicker optional while making its authored visual resources overridable."""
from pathlib import Path
from xml.etree import ElementTree as ET
import re
from generate_10_tokens import commit, TYPES
from generate_45_contracts import main as refresh_tokens

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'
COMPANION = ROOT / 'src/Avalonia.Controls.ColorPicker/Themes/MacOS'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'


def main():
    marker = THEME / 'Tokens/ColorPicker.xaml'
    if marker.exists():
        return
    TYPES['VisualBrush'] = 'global::Avalonia.Media.IBrush'
    TYPES['PlacementMode'] = 'global::Avalonia.Controls.PlacementMode'
    source = ROOT / 'eng/macos26/generate_10_tokens.py'
    text = source.read_text().replace("    'GridLength':", "    'VisualBrush': 'global::Avalonia.Media.IBrush',\n    'PlacementMode': 'global::Avalonia.Controls.PlacementMode',\n    'GridLength':")
    source.write_text(text)
    definitions = {}
    for path in sorted(COMPANION.glob('*.xaml')):
        text = path.read_text()
        inventory = {node.get(X + 'Key'): node.tag.rsplit('}', 1)[-1] for node in ET.fromstring(text).iter()
                     if node.get(X + 'Key') and node.tag.rsplit('}', 1)[-1] in TYPES}
        # The flyout has different corner geometry from the standalone ColorView.
        keys = {key: 'MacOS.ColorPicker.' + ('FlyoutTabBackgroundCornerRadius'
                if key == 'ColorViewTabBackgroundCornerRadius' and path.name == 'ColorPicker.xaml' else key)
                for key in inventory}
        for old, new in keys.items():
            kind = inventory[old]
            tag = '(?:x:)?' + kind
            pattern = r'<' + tag + r'\s+x:Key="' + re.escape(old) + r'"[^>]*?(?:/>|>.*?</' + tag + r'>)'
            matches = list(re.finditer(pattern, text, flags=re.S))
            if not matches:
                raise RuntimeError('Could not locate the full resource element: ' + old)
            for match in matches:
                node = match[0].replace('x:Key="' + old + '"', 'x:Key="' + new + '"')
                if new in definitions and definitions[new] != node:
                    raise RuntimeError('Conflicting token defaults: ' + new)
                definitions[new] = node
            text = re.sub(pattern, '', text, flags=re.S)
        # The root companion also defines shared resources used by other files.
        path.write_text(text)
    all_keys = {key.removeprefix('MacOS.ColorPicker.'): key for key in definitions}
    for path in sorted(COMPANION.glob('*.xaml')):
        text = path.read_text()
        mapping = dict(all_keys)
        if path.name == 'ColorPicker.xaml':
            mapping['ColorViewTabBackgroundCornerRadius'] = 'MacOS.ColorPicker.FlyoutTabBackgroundCornerRadius'
        text = re.sub(r'\{(?:StaticResource|DynamicResource) ([^{}]+)\}',
                      lambda m: '{DynamicResource ' + mapping[m[1]] + '}' if m[1] in mapping else m[0], text)
        path.write_text(text)
    marker.write_text('<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
                      + '\n'.join(definitions[key] for key in sorted(definitions)) + '\n</ResourceDictionary>\n')
    ET.parse(marker)
    path = THEME / 'MacOSTheme.xaml'
    text = path.read_text().replace('<MergeResourceInclude Source="/Tokens/Semantic.xaml" />',
        '<MergeResourceInclude Source="/Tokens/Semantic.xaml" />\n        <MergeResourceInclude Source="/Tokens/ColorPicker.xaml" />')
    path.write_text(text)
    # Two undefined inherited lookups are corrected rather than silently falling back.
    path = THEME / 'Controls/ToggleSwitch.xaml'
    path.write_text(path.read_text().replace('{DynamicResource ToggleSwitchFillOffDisabled}', '{DynamicResource MacOS.Brush.ControlDisabled}'))
    path = THEME / 'Controls/ScrollBar.xaml'
    path.write_text(path.read_text().replace('{DynamicResource ScrollBarButtonBackgroundDisabled}', '{DynamicResource MacOS.Brush.Transparent}'))
    commit('feat(macos): unify optional ColorPicker tokens and fix unresolved disabled-state brushes',
           'src/Avalonia.Themes.MacOS', 'src/Avalonia.Controls.ColorPicker/Themes/MacOS', 'eng/macos26/generate_10_tokens.py')
    refresh_tokens()

if __name__ == '__main__':
    main()
