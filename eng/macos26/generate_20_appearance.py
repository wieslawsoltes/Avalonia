#!/usr/bin/env python3
"""One-time conversion from the preserved templates to macOS visual tokens.

The output is ordinary checked-in XAML/C#, not a runtime dependency on Python.
All decisions below are explicit and reviewable; original Fluent is untouched.
"""
from pathlib import Path
import json
import re
import sys
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'
NS = 'xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"'

# These are authored theme values, not representations of Apple's private tokens.
COLORS = {
    'Window': ('#F5F5F7', '#1E1E20'),
    'Surface': ('#FFFFFF', '#2C2C2F'),
    'Control': ('#FFFFFF', '#47474B'),
    'ControlHover': ('#F4F4F6', '#535358'),
    'ControlPressed': ('#E5E5EA', '#3B3B40'),
    'ControlDisabled': ('#EBEBEF', '#343438'),
    'Label': ('#1D1D1F', '#F5F5F7'),
    'SecondaryLabel': ('#636368', '#BDBDC5'),
    'DisabledLabel': ('#919197', '#808088'),
    'Separator': ('#26000000', '#33FFFFFF'),
    'ControlStroke': ('#38000000', '#38FFFFFF'),
    'SelectionInactive': ('#DCDCE2', '#4B4B52'),
    'Hover': ('#10000000', '#18FFFFFF'),
    'Track': ('#D5D5DA', '#5B5B63'),
    'Knob': ('#FFFFFF', '#F8F8FA'),
    'Destructive': ('#C92B25', '#FF6961'),
    'Success': ('#248A3D', '#50D26C'),
    'Warning': ('#8A5A00', '#FFD166'),
    'Material': ('#EEFFFFFF', '#ED303034'),
    'MaterialOpaque': ('#FAFAFC', '#303034'),
    'OnAccent': ('#FFFFFF', '#FFFFFF'),
    'Transparent': ('#00000000', '#00000000'),
}

DIMENSIONS = {
    'ContentControlThemeFontFamily': '$Default', 'ControlContentThemeFontSize': '13',
    'ControlCornerRadius': '8', 'OverlayCornerRadius': '14',
    'ButtonPadding': '14,5', 'ComboBoxPadding': '10,4,0,4',
    'ComboBoxEditableTextPadding': '10,4,30,4', 'ComboBoxItemThemePadding': '10,5',
    'ListBoxItemPadding': '10,6', 'TextControlThemePadding': '9,5',
    'TextControlBorderThemeThicknessFocused': '1', 'TextControlPlaceholderOpacity': '1',
    'MenuFlyoutItemThemePadding': '10,5', 'MenuFlyoutItemThemePaddingNarrow': '10,4',
    'MenuFlyoutPresenterThemePadding': '5', 'MenuFlyoutSeparatorThemePadding': '8,5',
    'MenuFlyoutScrollerMargin': '0', 'MenuBarHeight': '28',
    'ToolTipBorderThemePadding': '9,5', 'ToolTipContentThemeFontSize': '11',
    'SliderTrackThemeHeight': '4', 'SliderHorizontalHeight': '28',
    'ScrollBarSize': '12', 'GroupBoxHeaderFontSize': '13', 'GroupBoxPadding': '12',
    'GroupBoxHeaderMargin': '0,0,0,10', 'TreeViewItemIndent': '18',
    'TreeViewItemExpandCollapseChevronMargin': '8,0',
    'TreeViewItemExpandCollapseChevronSize': '9', 'ExpanderMinHeight': '36',
    'ExpanderChevronButtonSize': '24', 'ExpanderChevronMargin': '10,0,8,0',
    'ExpanderHeaderPadding': '12,0,0,0', 'ExpanderContentPadding': '12',
    'TabItemHeaderFontSize': '13', 'TabItemHeaderMargin': '14,4', 'TabItemMargin': '2',
    'TabItemMinHeight': '28', 'TabStripItemMinHeight': '28',
    'TabItemPipeThickness': '0', 'TabStripItemPipeThickness': '0',
    'TableViewRowPadding': '8,5', 'TableViewCellPadding': '8,0',
    'TabbedPageTabItemHeaderMinHeight': '36', 'TabbedPageTabItemHeaderPipeThickness': '0',
    'TabbedPageTabItemHeaderCornerRadius': '8', 'TabbedPageTabStripPadding': '4',
    'TabbedPageTabStripCornerRadius': '12', 'CommandBarMinHeight': '44',
    'CommandBarButtonMinHeight': '30', 'CommandBarButtonWidth': '56',
    'CommandBarButtonCompactWidth': '32', 'CommandBarButtonIsInOverflowMinHeight': '28',
    'NavigationControlNavBarHeight': '44',
    'SystemControlFocusVisualMargin': '-3',
    'SystemControlFocusVisualPrimaryThickness': '3',
    'SystemControlFocusVisualSecondaryThickness': '0',
    'ToggleSwitchOuterBorderStrokeThickness': '0',
}
for name in ('ComboBoxMinHeight', 'TextControlThemeMinHeight', 'CalendarDatePickerMinHeight',
             'DropDownButtonMinHeight', 'SplitButtonMinHeight', 'TreeViewItemMinHeight',
             'RadioButtonMinHeight', 'CheckBoxMinHeight', 'MenuFlyoutThemeMinHeight'):
    DIMENSIONS[name] = '28'


def semantic_color(key):
    """Map component brush roles without changing template selectors or parts."""
    disabled = 'Disabled' in key
    hover = 'PointerOver' in key
    pressed = 'Pressed' in key
    active = any(word in key for word in ('Checked', 'Indeterminate', 'Selected')) and not any(word in key for word in ('Unchecked', 'Unselected'))
    state = 'ControlDisabled' if disabled else 'ControlPressed' if pressed else 'ControlHover' if hover else 'Control'
    if key.startswith(('AccentButton', 'ToggleButton')):
        active = key.startswith('AccentButton') or active
        if 'Foreground' in key:
            return 'DisabledLabel' if disabled else 'OnAccent' if active else 'Label'
        if 'Background' in key:
            return state if disabled or not active else '@SystemAccentColorDark1' if pressed else '@SystemAccentColor'
        if 'BorderBrush' in key:
            return 'Transparent' if active else 'ControlStroke'
    if key.startswith(('Button', 'RepeatButton', 'DropDownButton', 'SplitButton')):
        if 'Foreground' in key: return 'DisabledLabel' if disabled else 'Label'
        if 'Background' in key: return state
        if 'BorderBrush' in key: return 'ControlStroke'
    if key.startswith('TextControl'):
        if 'ButtonBackground' in key: return 'Hover' if hover or pressed else 'Transparent'
        if 'ButtonBorder' in key: return 'Transparent'
        if 'SelectionHighlight' in key: return '@SystemAccentColor'
        if 'Placeholder' in key: return 'DisabledLabel' if disabled else 'SecondaryLabel'
        if 'Foreground' in key: return 'DisabledLabel' if disabled else 'Label'
        if 'Background' in key: return 'ControlDisabled' if disabled else 'Surface'
        if 'BorderBrush' in key: return '@SystemAccentColor' if 'Focused' in key else 'ControlStroke'
    if key.startswith('ToggleSwitch'):
        if 'KnobFill' in key: return 'ControlDisabled' if disabled else 'Knob'
        if 'FillOn' in key: return 'Track' if disabled else '@SystemAccentColor'
        if 'FillOff' in key: return 'ControlDisabled' if disabled else 'Track'
        if 'Stroke' in key or 'ContainerBackground' in key: return 'Transparent'
        if 'Foreground' in key: return 'DisabledLabel' if disabled else 'Label'
    if key.startswith('CheckBox'):
        if 'CheckGlyphForeground' in key: return 'DisabledLabel' if disabled else 'OnAccent'
        if 'CheckBackgroundFill' in key: return 'ControlDisabled' if disabled else '@SystemAccentColor' if active else state
        if 'CheckBackgroundStroke' in key: return 'Transparent' if active else 'ControlStroke'
        if 'Foreground' in key: return 'DisabledLabel' if disabled else 'Label'
        if 'Background' in key or 'BorderBrush' in key: return 'Transparent'
    if key.startswith('RadioButton'):
        if 'CheckGlyph' in key: return 'DisabledLabel' if disabled else 'OnAccent'
        if 'OuterEllipseChecked' in key: return 'Track' if disabled else '@SystemAccentColor'
        if 'OuterEllipseStroke' in key: return 'ControlStroke'
        if 'OuterEllipseFill' in key: return state
        if 'Foreground' in key: return 'DisabledLabel' if disabled else 'Label'
        if 'Background' in key or 'BorderBrush' in key: return 'Transparent'
    if key.startswith('SliderThumbBackground'): return 'ControlDisabled' if disabled else 'Knob'
    if key.startswith(('TabItemHeader', 'TabStripItem', 'TabbedPageTabItemHeader')):
        if 'Foreground' in key: return 'DisabledLabel' if disabled else 'Label'
        if 'Pipe' in key: return 'Transparent'
        if 'Background' in key: return 'ControlDisabled' if disabled else 'Control' if active else 'Hover' if hover else 'Transparent'
    if key.startswith(('MenuFlyoutItem', 'MenuFlyoutSubItem')):
        if 'Background' in key: return '@SystemAccentColor' if (hover or pressed) and not disabled else 'Transparent'
        if 'Foreground' in key or 'Chevron' in key: return 'DisabledLabel' if disabled else 'OnAccent' if hover or pressed or 'SubMenuOpened' in key else 'Label'
    if key.startswith(('TreeViewItem', 'ListBoxItem', 'ListViewItem', 'ComboBoxItem', 'TableViewRow')):
        if 'Foreground' in key: return 'DisabledLabel' if disabled else 'OnAccent' if active else 'Label'
        if 'Background' in key: return 'SelectionInactive' if active and disabled else '@SystemAccentColor' if active else 'Hover' if hover or pressed else 'Transparent'
        if 'BorderBrush' in key: return 'Transparent'
    if key.startswith('ScrollBar'):
        if any(word in key for word in ('Thumb', 'Foreground')): return 'DisabledLabel' if disabled else 'SecondaryLabel'
        if 'Background' in key or 'Fill' in key or 'Stroke' in key or 'BorderBrush' in key: return 'Transparent'
    if key in ('MenuFlyoutPresenterBackground', 'FlyoutPresenterBackground', 'ToolTipBackground', 'ComboBoxDropDownBackground', 'AutoCompleteListBackground'):
        return 'Material'
    if key.endswith(('PresenterBorderBrush', 'DropDownBorderBrush')): return 'Separator'
    if key in ('SystemControlFocusVisualPrimaryBrush', 'FocusBorderBrush'): return '@SystemAccentColor'
    if key == 'SystemControlFocusVisualSecondaryBrush': return 'Transparent'
    return None


def main():
    marker = THEME / 'Tokens/Semantic.xaml'
    if marker.exists():
        return
    manifest = json.loads((THEME / 'Tokens/token-manifest.json').read_text())
    kinds = {t['legacyKey']: t['type'] for t in manifest['tokens']}
    for path in sorted(THEME.rglob('*.xaml')):
        text = path.read_text()
        for key, value in DIMENSIONS.items():
            # Compact dictionary must never accidentally become larger than regular.
            if 'DensityStyles' in path.parts and key not in ('ContentControlThemeFontFamily', 'ControlContentThemeFontSize'):
                continue
            text = re.sub(r'(<[\w:]+\s+x:Key="MacOS\.' + re.escape(key) + r'"[^>]*>)[^<]*(</[\w:]+>)',
                          lambda m, value=value: m[1] + value + m[2], text)
        # Static brush aliases become explicit brushes: semantic color overrides are live.
        def brush(match):
            node = match[0]
            key_match = re.search(r'x:Key="MacOS\.([^"]+)"', node)
            if not key_match or kinds.get(key_match[1]) != 'SolidColorBrush':
                return node
            role = semantic_color(key_match[1])
            if role is None:
                return node
            color = 'MacOS.' + role[1:] if role.startswith('@') else 'MacOS.Color.' + role
            return '<SolidColorBrush x:Key="MacOS.' + key_match[1] + '" Color="{DynamicResource ' + color + '}" />'
        text = re.sub(r'<(?:SolidColorBrush|StaticResource)\b[^>]*?/>|<SolidColorBrush\b[^>]*>[^<]*</SolidColorBrush>', brush, text)
        path.write_text(text)

    lines = [f'<ResourceDictionary {NS}>', '  <ResourceDictionary.ThemeDictionaries>']
    for i, variant in enumerate(('Default', 'Dark')):
        lines.append(f'    <ResourceDictionary x:Key="{variant}">')
        for name, values in COLORS.items():
            lines.append(f'      <Color x:Key="MacOS.Color.{name}">{values[i]}</Color>')
            lines.append(f'      <SolidColorBrush x:Key="MacOS.Brush.{name}" Color="{{DynamicResource MacOS.Color.{name}}}" />')
        lines.append('    </ResourceDictionary>')
    lines += ['  </ResourceDictionary.ThemeDictionaries>',
              '  <x:Double x:Key="MacOS.Switch.TrackWidth">38</x:Double>',
              '  <x:Double x:Key="MacOS.Switch.TrackHeight">22</x:Double>',
              '  <x:Double x:Key="MacOS.Switch.KnobSize">18</x:Double>',
              '  <CornerRadius x:Key="MacOS.Switch.Radius">11</CornerRadius>',
              '  <CornerRadius x:Key="MacOS.Focus.Radius">10</CornerRadius>',
              '  <CornerRadius x:Key="MacOS.Selection.Radius">6</CornerRadius>',
              '  <TimeSpan x:Key="MacOS.Motion.Interaction">0:0:0.12</TimeSpan>',
              '  <TimeSpan x:Key="MacOS.Motion.Switch">0:0:0.18</TimeSpan>',
              '</ResourceDictionary>']
    marker.write_text('\n'.join(lines) + '\n')
    path = THEME / 'MacOSTheme.xaml'
    text = path.read_text().replace('<MergeResourceInclude Source="/Accents/BaseResources.xaml" />',
        '<MergeResourceInclude Source="/Tokens/Semantic.xaml" />\n        <MergeResourceInclude Source="/Accents/BaseResources.xaml" />')
    path.write_text(text)

    # Harmonize inherited semantic system colors for the remaining control families.
    path = THEME / 'Accents/BaseColorsPalette.xaml'
    text = path.read_text()
    palette = {'SystemRegionColor': COLORS['Window'], 'SystemBaseHighColor': COLORS['Label'],
               'SystemBaseMediumColor': COLORS['SecondaryLabel'], 'SystemBaseMediumLowColor': COLORS['DisabledLabel'],
               'SystemChromeLowColor': COLORS['Window'], 'SystemChromeMediumColor': COLORS['Surface'],
               'SystemChromeMediumLowColor': COLORS['Surface'], 'SystemChromeHighColor': COLORS['Track'],
               'SystemChromeDisabledHighColor': COLORS['ControlDisabled'], 'SystemErrorTextColor': COLORS['Destructive']}
    for key, values in palette.items():
        count = [0]
        def color(m):
            value = values[min(count[0], 1)]
            count[0] += 1
            return m[1] + value + m[2]
        text = re.sub(r'(<Color x:Key="MacOS\.' + key + r'">)[^<]+(</Color>)', color, text)
    path.write_text(text)

    path = THEME / 'Controls/ToggleSwitch.xaml'
    text = path.read_text().replace('Width="40"', 'Width="{DynamicResource MacOS.Switch.TrackWidth}"')
    text = text.replace('Width="20"', 'Width="{DynamicResource MacOS.Switch.TrackHeight}"')
    text = text.replace('Height="20"', 'Height="{DynamicResource MacOS.Switch.TrackHeight}"')
    text = text.replace('Width="10"', 'Width="{DynamicResource MacOS.Switch.KnobSize}"')
    text = text.replace('Height="10"', 'Height="{DynamicResource MacOS.Switch.KnobSize}"')
    text = text.replace('CornerRadius="10"', 'CornerRadius="{DynamicResource MacOS.Switch.Radius}"')
    text = text.replace('Duration="0:0:0.2"', 'Duration="{DynamicResource MacOS.Motion.Switch}"')
    path.write_text(text)
    path = THEME / 'Controls/AdornerLayer.xaml'
    text = path.read_text().replace('<Border BorderThickness=', '<Border CornerRadius="{DynamicResource MacOS.Focus.Radius}" BorderThickness=')
    path.write_text(text)
    for name in ('TabItem', 'TabStripItem'):
        path = THEME / f'Controls/{name}.xaml'
        text = path.read_text().replace('<Setter Property="Margin" Value="0" />',
             '<Setter Property="Margin" Value="2" />\n    <Setter Property="CornerRadius" Value="{DynamicResource MacOS.Selection.Radius}" />')
        path.write_text(text)
    for name in ('Button', 'RepeatButton', 'ToggleButton'):
        path = THEME / f'Controls/{name}.xaml'
        text = path.read_text().replace('Duration="0:0:0.075"', 'Duration="{DynamicResource MacOS.Motion.Interaction}"')
        path.write_text(text)

    # Public typed semantic keys complement the complete component-token inventory.
    code = ['using Avalonia.Media;', '', 'namespace Avalonia.Themes.MacOS;', '',
            '/// <summary>Semantic palette tokens shared by all macOS control families.</summary>',
            'public static class MacOSSemanticTokens', '{']
    for name in COLORS:
        code += [f'    /// <summary>Gets the {name} color token.</summary>',
                 f'    public static readonly MacOSToken<Color> {name} = new("MacOS.Color.{name}");',
                 f'    /// <summary>Gets the {name} brush token.</summary>',
                 f'    public static readonly MacOSToken<IBrush> {name}Brush = new("MacOS.Brush.{name}");']
    code += ['}', '']
    (THEME / 'MacOSSemanticTokens.cs').write_text('\n'.join(code))
    commit('feat(macos): add light/dark semantic palettes and native-scale control treatments', 'src/Avalonia.Themes.MacOS')

if __name__ == '__main__':
    main()
