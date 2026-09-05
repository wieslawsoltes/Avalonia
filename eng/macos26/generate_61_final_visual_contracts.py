#!/usr/bin/env python3
"""Resolve issues found in the second visual review without changing control logic."""
from pathlib import Path
import json
import re
from generate_10_tokens import commit
from generate_45_contracts import main as refresh_tokens

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'


def main():
    path = THEME / 'Tokens/Semantic.xaml'
    if 'MacOS.Stepper.Width' in path.read_text():
        return
    path.write_text(path.read_text().rsplit('</ResourceDictionary>', 1)[0] + '''  <x:Double x:Key="MacOS.Stepper.Width">22</x:Double>
  <x:Double x:Key="MacOS.Stepper.ButtonHeight">12</x:Double>
  <x:Double x:Key="MacOS.Stepper.IconWidth">8</x:Double>
  <x:Double x:Key="MacOS.Stepper.IconHeight">4</x:Double>
  <Thickness x:Key="MacOS.Stepper.Margin">2</Thickness>
</ResourceDictionary>
''')
    path = THEME / 'Controls/TreeViewItem.xaml'
    text = path.read_text().replace(' > ContentPresenter#PART_HeaderPresenter', ' > Grid#PART_Header > ContentPresenter#PART_HeaderPresenter')
    path.write_text(text)
    path = ROOT / 'eng/macos26/audit.py'
    text = path.read_text().replace('        actual_selectors = set(',
        '''        # Intentional upstream selector correction: the header presenter is
        # inside PART_Header, not a direct child of PART_LayoutRoot.
        if str(relative) == 'Controls/TreeViewItem.xaml':
            expected_selectors = {s.replace(' > ContentPresenter#PART_HeaderPresenter',
                ' > Grid#PART_Header > ContentPresenter#PART_HeaderPresenter') for s in expected_selectors}
        actual_selectors = set(''')
    path.write_text(text)
    path = THEME / 'Controls/ButtonSpinner.xaml'
    text = path.read_text().replace('Orientation="Horizontal"', 'Orientation="Vertical" VerticalAlignment="Center" Margin="{DynamicResource MacOS.Stepper.Margin}"')
    text = text.replace('MinWidth="34"', 'MinWidth="{DynamicResource MacOS.Stepper.Width}" Height="{DynamicResource MacOS.Stepper.ButtonHeight}" Padding="0"')
    text = text.replace('<PathIcon Width="16"', '<PathIcon Width="{DynamicResource MacOS.Stepper.IconWidth}"')
    text = text.replace('Height="8"', 'Height="{DynamicResource MacOS.Stepper.IconHeight}"')
    path.write_text(text)
    # Include StaticResource brush aliases, which the earlier refinement omitted.
    kinds = {t['key']: t['type'] for t in json.loads((THEME / 'Tokens/token-manifest.json').read_text())['tokens']}
    def role(key):
        disabled, hover, pressed = 'Disabled' in key, 'PointerOver' in key, 'Pressed' in key
        state = 'ControlDisabled' if disabled else 'ControlPressed' if pressed else 'ControlHover' if hover else 'Control'
        if key.startswith(('ComboBox', 'CalendarDatePicker', 'DatePicker', 'TimePicker')) and not key.startswith('ComboBoxItem'):
            if 'BorderBrush' in key: return 'Accent' if 'Focused' in key else 'ControlStroke'
            if 'Foreground' in key: return 'DisabledLabel' if disabled else 'SecondaryLabel' if 'Placeholder' in key else 'Label'
            if 'Background' in key: return 'Material' if 'DropDown' in key else state
        if key.startswith('Expander'):
            if 'Foreground' in key: return 'DisabledLabel' if disabled else 'Label'
            if 'BorderBrush' in key: return 'Separator'
            if 'Background' in key: return 'Hover' if hover or pressed else 'Transparent'
        return None
    path = THEME / 'Accents/MacOSControlResources.xaml'
    def replace(match):
        node = match[0]
        found = re.search(r'x:Key="(MacOS.[^"]+)"', node)
        if not found or kinds.get(found[1]) != 'SolidColorBrush': return node
        value = role(found[1].removeprefix('MacOS.'))
        return node if value is None else f'<SolidColorBrush x:Key="{found[1]}" Color="{{DynamicResource MacOS.Color.{value}}}" />'
    path.write_text(re.sub(r'<(?:SolidColorBrush|StaticResource)\b[^>]*?/>|<SolidColorBrush\b[^>]*>[^<]*</SolidColorBrush>', replace, path.read_text()))
    path = ROOT / 'samples/ControlCatalog.MacOS/Program.cs'
    text = path.read_text().replace('            theme.ReduceMotion = true;',
        '            theme.ReduceMotion = true;\n            if (s_headless)\n                theme.SetToken(MacOSTokens.ContentControlThemeFontFamily, new FontFamily("fonts:Inter#Inter"));')
    text = text.replace('var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height));',
        'const double renderScale = 2;\n        var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width * renderScale), (int)Math.Ceiling(window.Bounds.Height * renderScale));')
    text = text.replace('new Vector(96, 96)', 'new Vector(96 * renderScale, 96 * renderScale)')
    text = text.replace('file = Path.GetFileName(path), width = size.Width, height = size.Height,',
        'file = Path.GetFileName(path), width = size.Width, height = size.Height, renderScale, dpi = 96 * renderScale,\n            font = s_headless ? "Inter (explicit headless fallback)" : "platform default",')
    path.write_text(text)
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    text = path.read_text().replace('Assert.Equal(34d, control.MinHeight);', 'Assert.Equal(34d, control.Height);')
    if 'Selected_Tree_Header_Uses_Contrasting_Accent_Text' not in text:
        text = text.replace('    private sealed class Host : IDisposable', '''    [AvaloniaFact]
    public void Selected_Tree_Header_Uses_Contrasting_Accent_Text()
    {
        var item = new TreeViewItem { Header = "Selected document", IsSelected = true };
        var tree = new TreeView();
        tree.Items.Add(item);
        using var host = new Host(tree);
        var presenter = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(item)
            .OfType<Avalonia.Controls.Presenters.ContentPresenter>()
            .Single(p => p.Name == "PART_HeaderPresenter");
        Assert.Equal(Resolve<Color>(MacOSSemanticTokens.OnAccent.Key),
            Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
    }

    private sealed class Host : IDisposable''')
    path.write_text(text)
    commit('fix(macos): refine native-scale steppers, semantic forms and high-resolution capture fidelity',
           'src/Avalonia.Themes.MacOS', 'samples/ControlCatalog.MacOS', 'tests/Avalonia.Themes.MacOS.UnitTests', 'eng/macos26/audit.py')
    refresh_tokens()

if __name__ == '__main__':
    main()
