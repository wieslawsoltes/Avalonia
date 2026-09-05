#!/usr/bin/env python3
"""Apply corrections found by reviewing checkpoint 01 actual Avalonia frames."""
from pathlib import Path
import re
from generate_10_tokens import commit
from generate_45_contracts import main as refresh_tokens

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'


def edit(relative, action):
    path = THEME / relative
    path.write_text(action(path.read_text()))


def main():
    semantic = THEME / 'Tokens/Semantic.xaml'
    if 'MacOS.Switch.Travel' in semantic.read_text():
        return
    extra = '''  <!-- The control interprets PART_SwitchKnob.Width as travel, not track size. -->
  <x:Double x:Key="MacOS.Switch.Travel">16</x:Double>
  <x:Double x:Key="MacOS.CheckBox.Size">16</x:Double>
  <x:Double x:Key="MacOS.CheckBox.GlyphSize">12</x:Double>
  <CornerRadius x:Key="MacOS.CheckBox.Radius">4</CornerRadius>
  <x:Double x:Key="MacOS.RadioButton.Size">16</x:Double>
  <x:Double x:Key="MacOS.RadioButton.GlyphSize">6</x:Double>
  <Thickness x:Key="MacOS.Selection.Margin">0,1</Thickness>
  <Thickness x:Key="MacOS.Slider.ThumbStroke">1</Thickness>
'''
    semantic.write_text(semantic.read_text().replace('</ResourceDictionary>\n', '</ResourceDictionary>\n', 1).rsplit('</ResourceDictionary>', 1)[0] + extra + '</ResourceDictionary>\n')
    edit('Controls/ToggleSwitch.xaml', lambda text: re.sub(
        r'(<Canvas\s+x:Name="PART_SwitchKnob"\s+Grid.Row="1"\s+Width=")[^"]+',
        r'\1{DynamicResource MacOS.Switch.Travel}', text))
    edit('Controls/Slider.xaml', lambda text: re.sub(
        r'(<x:Double x:Key="MacOS.Slider(?:Horizontal|Vertical)Thumb(?:Width|Height)">)20', r'\g<1>18', text)
        .replace('MacOS.SliderPreContentMargin">15', 'MacOS.SliderPreContentMargin">4')
        .replace('MacOS.SliderPostContentMargin">15', 'MacOS.SliderPostContentMargin">4')
        .replace('BorderThickness="0"', 'BorderThickness="{DynamicResource MacOS.Slider.ThumbStroke}" BorderBrush="{DynamicResource MacOS.Brush.ControlStroke}"'))
    edit('Controls/CheckBox.xaml', lambda text: text
        .replace('Value="{DynamicResource MacOS.ControlCornerRadius}"', 'Value="{DynamicResource MacOS.CheckBox.Radius}"')
        .replace('Height="20"', 'Height="{DynamicResource MacOS.CheckBox.Size}"')
        .replace('Width="20"', 'Width="{DynamicResource MacOS.CheckBox.Size}"')
        .replace('<Viewbox UseLayoutRounding="False">', '<Viewbox Width="{DynamicResource MacOS.CheckBox.GlyphSize}" Height="{DynamicResource MacOS.CheckBox.GlyphSize}" UseLayoutRounding="False">'))
    edit('Controls/RadioButton.xaml', lambda text: text
        .replace('Height="20"', 'Height="{DynamicResource MacOS.RadioButton.Size}"')
        .replace('Width="20"', 'Width="{DynamicResource MacOS.RadioButton.Size}"')
        .replace('Height="8"', 'Height="{DynamicResource MacOS.RadioButton.GlyphSize}"')
        .replace('Width="8"', 'Width="{DynamicResource MacOS.RadioButton.GlyphSize}"'))
    edit('Controls/ContentPage.xaml', lambda text: text.replace('MacOS.SystemControlPageBackgroundAltHighBrush', 'MacOS.Brush.Window'))
    edit('Controls/Window.xaml', lambda text: text.replace('MacOS.SystemRegionBrush', 'MacOS.Brush.Window').replace('MacOS.SystemControlBackgroundAltHighBrush', 'MacOS.Brush.Window'))
    for name in ('ListBoxItem', 'TreeViewItem'):
        edit(f'Controls/{name}.xaml', lambda text: text.replace('<Setter Property="Template">',
            '<Setter Property="CornerRadius" Value="{DynamicResource MacOS.Selection.Radius}" />\n    <Setter Property="Margin" Value="{DynamicResource MacOS.Selection.Margin}" />\n    <Setter Property="Template">', 1))
    def list_selection(text):
        text = text.replace('MacOS.SystemControlDisabledBaseMediumLowBrush', 'MacOS.Brush.DisabledLabel')
        text = text.replace('MacOS.SystemControlHighlightAltBaseHighBrush', 'MacOS.Brush.Label')
        for role in ('Low', 'Medium'):
            text = text.replace('MacOS.SystemControlHighlightList' + role + 'Brush', 'MacOS.Brush.Hover')
        for role in ('Low', 'Medium', 'High'):
            text = text.replace('MacOS.SystemControlHighlightListAccent' + role + 'Brush', 'MacOS.ButtonBackground') if False else text
            text = text.replace('MacOS.SystemControlHighlightListAccent' + role + 'Brush', 'MacOS.AccentButtonBackground')
        before, after = text.split('<!--  Selected State  -->', 1)
        return before + '<!--  Selected State  -->' + after.replace('MacOS.Brush.Label', 'MacOS.Brush.OnAccent')
    edit('Controls/ListBoxItem.xaml', list_selection)
    edit('Controls/TabControl.xaml', lambda text: text.replace('''<ItemsPresenter Name="PART_ItemsPresenter"
                            ItemsPanel="{TemplateBinding ItemsPanel}"
                            DockPanel.Dock="{TemplateBinding TabStripPlacement}" />''', '''<Border DockPanel.Dock="{TemplateBinding TabStripPlacement}"
                    Background="{DynamicResource MacOS.Brush.ControlDisabled}"
                    CornerRadius="{DynamicResource MacOS.ControlCornerRadius}" Padding="2">
              <ItemsPresenter Name="PART_ItemsPresenter" ItemsPanel="{TemplateBinding ItemsPanel}" />
            </Border>'''))

    def brush_role(key):
        disabled = 'Disabled' in key
        hover, pressed = 'PointerOver' in key, 'Pressed' in key
        state = 'ControlDisabled' if disabled else 'ControlPressed' if pressed else 'ControlHover' if hover else 'Control'
        if key.startswith(('ComboBox', 'CalendarDatePicker', 'DatePicker', 'TimePicker', 'NumericUpDown')) and not key.startswith('ComboBoxItem'):
            if 'Selection' in key and 'Background' in key: return '@SystemAccentColor'
            if 'Foreground' in key: return 'DisabledLabel' if disabled else 'SecondaryLabel' if 'Placeholder' in key else 'Label'
            if 'Background' in key: return 'Material' if 'DropDown' in key else state
            if 'BorderBrush' in key: return '@SystemAccentColor' if 'Focused' in key else 'ControlStroke'
        if key.startswith('Expander'):
            if 'Foreground' in key or 'Chevron' in key and 'Background' not in key and 'Border' not in key: return 'DisabledLabel' if disabled else 'Label'
            if 'Background' in key: return 'Hover' if hover or pressed else 'Transparent'
            if 'BorderBrush' in key: return 'Separator'
        if key.startswith('Slider'):
            if 'TrackFill' in key or 'TrackValueFill' in key: return 'Track' if disabled or 'Value' not in key else '@SystemAccentColor'
            if 'Thumb' in key and 'Background' in key: return 'ControlDisabled' if disabled else 'Knob'
        return None
    def brushes(text):
        def node(match):
            value = match[0]
            found = re.search(r'x:Key="MacOS.([^"]+)"', value)
            if not found: return value
            role = brush_role(found[1])
            if role is None: return value
            color = 'MacOS.' + role[1:] if role.startswith('@') else 'MacOS.Color.' + role
            return f'<SolidColorBrush x:Key="MacOS.{found[1]}" Color="{{DynamicResource {color}}}" />'
        return re.sub(r'<SolidColorBrush\b[^>]*?/>|<SolidColorBrush\b[^>]*>[^<]*</SolidColorBrush>', node, text)
    for path in (THEME / 'Accents').glob('*.xaml'):
        path.write_text(brushes(path.read_text()))
    path = ROOT / 'samples/ControlCatalog.MacOS/Program.cs'
    text = path.read_text().replace('return builder.AfterSetup', 'if (s_headless)\n            builder = builder.WithInterFont();\n        return builder.AfterSetup')
    path.write_text(text)
    # Tests use the same cross-platform font fallback as ControlCatalog screenshots.
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/Avalonia.Themes.MacOS.UnitTests.csproj'
    path.write_text(path.read_text().replace('<ItemGroup>', '<ItemGroup>\n    <ProjectReference Include="../../src/Avalonia.Fonts.Inter/Avalonia.Fonts.Inter.csproj" />', 1))
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/TestApplication.cs'
    path.write_text(path.read_text().replace('.UseHarfBuzz().UseSkia()', '.UseHarfBuzz().UseSkia().WithInterFont()'))
    commit('fix(macos): correct switch travel, visible slider thumbs, geometry and selected surfaces',
           'src/Avalonia.Themes.MacOS', 'samples/ControlCatalog.MacOS', 'tests/Avalonia.Themes.MacOS.UnitTests')
    refresh_tokens()

if __name__ == '__main__':
    main()
