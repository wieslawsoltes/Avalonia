#!/usr/bin/env python3
"""Fix a helper-theme setter collision reported by the real catalog renderer."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    path = ROOT / 'src/Avalonia.Themes.MacOS/Controls/TreeViewItem.xaml'
    text = path.read_text()
    pair = '<Setter Property="CornerRadius" Value="{DynamicResource MacOS.Selection.Radius}" />\n    <Setter Property="Margin" Value="{DynamicResource MacOS.Selection.Margin}" />\n    '
    text = text.replace(pair, '')
    anchor = '<ControlTheme x:Key="{x:Type TreeViewItem}" TargetType="TreeViewItem">'
    if anchor not in text:
        raise RuntimeError('Expected the implicit TreeViewItem theme')
    text = text.replace(anchor, anchor + '\n    ' + pair.rstrip())
    path.write_text(text)
    path = ROOT / 'eng/macos26/audit.py'
    text = path.read_text()
    anchor = '    for key in references - tokens:'
    if 'Duplicate direct setters' not in text:
        text = text.replace(anchor, '''    for path in THEME.rglob('*.xaml'):
        for node in ET.parse(path).iter():
            if node.tag.rsplit('}', 1)[-1] not in ('Style', 'ControlTheme'):
                continue
            properties = [child.get('Property') for child in node
                          if child.tag.rsplit('}', 1)[-1] == 'Setter']
            if len(properties) != len(set(properties)):
                errors.append('Duplicate direct setters: ' + str(path.relative_to(ROOT)) + ' ' + str(node.attrib))
''' + anchor)
    path.write_text(text)
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    text = path.read_text()
    if 'Representative_Control_Families_Apply_Their_Templates' not in text:
        text = text.replace('    private sealed class Host : IDisposable', '''    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Representative_Control_Families_Apply_Their_Templates(bool dark)
    {
        var tree = new TreeView();
        var root = new TreeViewItem { Header = "Root", IsExpanded = true };
        root.Items.Add(new TreeViewItem { Header = "Selected", IsSelected = true });
        tree.Items.Add(root);
        var list = new ListBox { SelectedIndex = 0 };
        list.Items.Add("Document");
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Overview", Content = "Content" });
        var controls = new StackPanel
        {
            Children =
            {
                tree, list, tabs, new ToggleSwitch { IsChecked = true },
                new RadioButton { Content = "Automatic", IsChecked = true },
                new Slider { Value = 50 }, new Expander { Header = "Options", IsExpanded = true, Content = "Settings" }
            }
        };
        using var host = new Host(controls);
        host.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        host.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(root.Bounds.Height > 0);
        Assert.True(tabs.Bounds.Height > 0);
    }

    private sealed class Host : IDisposable''')
    path.write_text(text)
    commit('fix(macos): isolate tree item setters and cover helper-theme layout regressions',
           'src/Avalonia.Themes.MacOS', 'eng/macos26/audit.py', 'tests/Avalonia.Themes.MacOS.UnitTests')

if __name__ == '__main__':
    main()
