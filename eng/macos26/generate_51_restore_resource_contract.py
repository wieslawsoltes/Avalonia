#!/usr/bin/env python3
"""Restore resources removed by an over-broad initial brush rewrite.

Rebuild this dictionary from the preserved original, transform each bounded XML
node, then assert every source resource occurrence survived before committing.
"""
from pathlib import Path
from collections import Counter
from xml.etree import ElementTree as ET
import json
import re
from generate_10_tokens import commit
from generate_20_appearance import DIMENSIONS, semantic_color

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'


def main():
    destination = THEME / 'Accents/MacOSControlResources.xaml'
    if 'bounded-resource-migration-v1' in destination.read_text():
        return
    original = (ROOT / 'src/Avalonia.Themes.Fluent/Accents/FluentControlResources.xaml').read_text(encoding='utf-8-sig')
    manifest = json.loads((THEME / 'Tokens/token-manifest.json').read_text())
    inventory = {item['legacyKey']: item for item in manifest['tokens'] if item['legacyKey'] is not None}
    key_map = {key: item['key'] for key, item in inventory.items()}
    text = re.sub(r'\{(StaticResource|DynamicResource)\s+([^{}]+)\}',
        lambda m: '{DynamicResource ' + key_map[m[2]] + '}' if m[2] in key_map else m[0], original)
    text = re.sub(r'(x:Key|ResourceKey)="([^"]+)"',
        lambda m: m[1] + '="' + key_map.get(m[2], m[2]) + '"', text)
    for key, value in DIMENSIONS.items():
        text = re.sub(r'(<[\w:]+\s+x:Key="MacOS\.' + re.escape(key) + r'"[^>]*>)[^<]*(</[\w:]+>)',
            lambda m, value=value: m[1] + value + m[2], text)

    # First alternative is deliberately bounded to a single self-closing node.
    # The second permits only text content, never siblings or nested dictionaries.
    pattern = re.compile(r'<(?:SolidColorBrush|StaticResource)\b[^>]*?/>|<SolidColorBrush\b[^>]*>[^<]*</SolidColorBrush>')

    def brush(match):
        node = match[0]
        key_match = re.search(r'x:Key="MacOS\.([^"]+)"', node)
        if not key_match:
            return node
        key = key_match[1]
        if inventory.get(key, {}).get('type') != 'SolidColorBrush':
            return node
        role = semantic_color(key)
        if role is None:
            return node
        color = 'MacOS.' + role[1:] if role.startswith('@') else 'MacOS.Color.' + role
        return '<SolidColorBrush x:Key="MacOS.' + key + '" Color="{DynamicResource ' + color + '}" />'

    text = pattern.sub(brush, text)
    before = Counter(key_map.get(node.get(X + 'Key'), node.get(X + 'Key')) for node in ET.fromstring(original).iter() if node.get(X + 'Key'))
    after = Counter(node.get(X + 'Key') for node in ET.fromstring(text).iter() if node.get(X + 'Key'))
    if before != after:
        raise RuntimeError(f'Resource migration changed declarations: missing={before-after}, extra={after-before}')
    destination.write_text('<!-- bounded-resource-migration-v1: all source resource occurrences preserved -->\n' + text)
    for name in ('Button', 'RepeatButton', 'ToggleButton'):
        path = THEME / f'Controls/{name}.xaml'
        path.write_text(path.read_text().replace('Duration="0:0:.075"', 'Duration="{DynamicResource MacOS.Motion.Interaction}"'))
    commit('fix(macos): preserve all resource declarations and correctly tokenize button motion', 'src/Avalonia.Themes.MacOS')
    print(f'Preserved {sum(after.values())} resource declarations with bounded XML-node migration.')

if __name__ == '__main__':
    main()
