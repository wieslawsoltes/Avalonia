#!/usr/bin/env python3
"""Verify copied template contracts and the public design-token inventory."""
from pathlib import Path
from xml.etree import ElementTree as ET
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'src/Avalonia.Themes.Fluent'
THEME = ROOT / 'src/Avalonia.Themes.MacOS'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'


def main():
    subprocess.run([sys.executable, str(ROOT / 'eng/macos26/update-tokens.py'), '--check'], check=True)
    errors = []
    manifest = json.loads((THEME / 'Tokens/token-manifest.json').read_text())
    tokens = {entry['key'] for entry in manifest['tokens']}
    if len(tokens) != len(manifest['tokens']):
        errors.append('Duplicate token keys in manifest')
    declared, references = set(), set()
    for path in THEME.rglob('*.xaml'):
        text = path.read_text()
        ET.fromstring(text)
        if 'avares://Avalonia.Themes.Fluent/' in text or 'using:Avalonia.Themes.Fluent' in text:
            errors.append(f'Fluent dependency in {path.relative_to(ROOT)}')
        for node in ET.fromstring(text).iter():
            key = node.get(X + 'Key', '')
            if key.startswith('MacOS.'):
                declared.add(key)
        references.update(re.findall(r'\{(?:DynamicResource|StaticResource) (MacOS\.[^{}]+)\}', text))
    for path in THEME.rglob('*.xaml'):
        for dictionary in ET.parse(path).iter():
            if dictionary.tag.rsplit('}', 1)[-1] != 'ResourceDictionary':
                continue
            keys = [node.get(X + 'Key') for node in dictionary if node.get(X + 'Key')]
            if len(keys) != len(set(keys)):
                errors.append('Duplicate keys in resource dictionary: ' + str(path.relative_to(ROOT)))
    for path in THEME.rglob('*.xaml'):
        for node in ET.parse(path).iter():
            if node.tag.rsplit('}', 1)[-1] not in ('Style', 'ControlTheme'):
                continue
            properties = [child.get('Property') for child in node
                          if child.tag.rsplit('}', 1)[-1] == 'Setter']
            if len(properties) != len(set(properties)):
                errors.append('Duplicate direct setters: ' + str(path.relative_to(ROOT)) + ' ' + str(node.attrib))
    for key in references - tokens:
        errors.append('Referenced visual token missing from public inventory: ' + key)
    runtime = {'MacOS.SystemAccentColor' + suffix for suffix in ('', 'Dark1', 'Dark2', 'Dark3', 'Light1', 'Light2', 'Light3')}
    for key in tokens - declared - runtime:
        errors.append('Token has no XAML or runtime definition: ' + key)
    for original in SOURCE.rglob('*.xaml'):
        relative = original.relative_to(SOURCE)
        destination = THEME / str(relative).replace('Fluent', 'MacOS')
        if not destination.exists():
            errors.append('Missing copied dictionary: ' + str(relative))
            continue
        before, after = original.read_text(encoding='utf-8-sig'), destination.read_text()
        expected_parts = set(re.findall(r'(?:x:)?Name="(PART_[^"]+)"', before))
        actual_parts = set(re.findall(r'(?:x:)?Name="(PART_[^"]+)"', after))
        if expected_parts - actual_parts:
            errors.append(f'{relative}: removed template parts {expected_parts - actual_parts}')
        expected_selectors = set(re.findall(r'Selector="([^"]+)"', before))
        # Intentional upstream selector correction: the header presenter is
        # inside PART_Header, not a direct child of PART_LayoutRoot.
        if str(relative) == 'Controls/TreeViewItem.xaml':
            expected_selectors = {s.replace(' > ContentPresenter#PART_HeaderPresenter',
                ' > Grid#PART_Header > ContentPresenter#PART_HeaderPresenter') for s in expected_selectors}
        actual_selectors = set(re.findall(r'Selector="([^"]+)"', after))
        if expected_selectors - actual_selectors:
            errors.append(f'{relative}: removed pseudo-class/style selectors {expected_selectors - actual_selectors}')
        expected_types = set(re.findall(r'x:Key="(\{x:Type [^}]+\})"', before))
        actual_types = set(re.findall(r'x:Key="(\{x:Type [^}]+\})"', after))
        if expected_types - actual_types:
            errors.append(f'{relative}: removed implicit themes {expected_types - actual_types}')
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'PASS: {len(tokens)} public tokens; {len(manifest["controls"])} implicit themes; template parts and selectors retained.')

if __name__ == '__main__':
    main()
