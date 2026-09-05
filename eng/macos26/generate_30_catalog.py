#!/usr/bin/env python3
"""Materialize catalog integration and a deterministic, real-Avalonia capture app."""
from pathlib import Path
import json
import re
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / 'samples/ControlCatalog'
THEME = ROOT / 'src/Avalonia.Themes.MacOS'


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')


def main():
    marker = CATALOG / 'Pages/MacOSThemePage.xaml'
    if marker.exists():
        return
    # The optional ColorPicker assembly owns its companion styles, just as it does
    # for Fluent and Simple; the core MacOS theme does not depend on ColorPicker.
    source = ROOT / 'src/Avalonia.Controls.ColorPicker/Themes/Fluent'
    target = source.with_name('MacOS')
    inventory = json.loads((THEME / 'Tokens/token-manifest.json').read_text())
    keys = {t['legacyKey'] for t in inventory['tokens']}
    for path in sorted(source.rglob('*.xaml')):
        text = path.read_text(encoding='utf-8-sig').replace('/Themes/Fluent/', '/Themes/MacOS/')
        text = re.sub(r'\{(StaticResource|DynamicResource) ([^{}]+)\}',
            lambda m: '{DynamicResource MacOS.' + m[2] + '}' if m[2] in keys else m[0], text)
        destination = target / path.relative_to(source)
        if destination.name == 'Fluent.xaml':
            destination = destination.with_name('MacOS.xaml')
        write(destination, text)
    commit('feat(macos): add ColorPicker companion styles without a core dependency', 'src/Avalonia.Controls.ColorPicker/Themes/MacOS')

    path = CATALOG / 'ControlCatalog.csproj'
    text = path.read_text(encoding='utf-8-sig').replace('<ProjectReference Include="..\\..\\src\\Avalonia.Themes.Fluent\\Avalonia.Themes.Fluent.csproj" />',
        '<ProjectReference Include="..\\..\\src\\Avalonia.Themes.Fluent\\Avalonia.Themes.Fluent.csproj" />\n    <ProjectReference Include="..\\..\\src\\Avalonia.Themes.MacOS\\Avalonia.Themes.MacOS.csproj" />')
    path.write_text(text)
    path = CATALOG / 'Models/CatalogTheme.cs'
    path.write_text(path.read_text(encoding='utf-8-sig').replace('Simple\n', 'Simple,\n        MacOS\n'))
    path = CATALOG / 'MainView.xaml'
    path.write_text(path.read_text().replace('<models:CatalogTheme>Simple</models:CatalogTheme>',
        '<models:CatalogTheme>Simple</models:CatalogTheme>\n              <models:CatalogTheme>MacOS</models:CatalogTheme>'))
    path = CATALOG / 'App.xaml'
    text = path.read_text().replace('<SimpleTheme x:Key="SimpleTheme" />',
        '<SimpleTheme x:Key="SimpleTheme" />\n      <MacOSTheme x:Key="MacOSTheme" />\n      <StyleInclude x:Key="ColorPickerMacOS" Source="avares://Avalonia.Controls.ColorPicker/Themes/MacOS/MacOS.xaml" />')
    path.write_text(text)
    path = CATALOG / 'App.xaml.cs'
    text = path.read_text().replace('private SimpleTheme? _simpleTheme;',
        'private SimpleTheme? _simpleTheme;\n        private Avalonia.Themes.MacOS.MacOSTheme? _macOSTheme;\n        private IStyle? _colorPickerMacOS;\n\n        public Avalonia.Themes.MacOS.MacOSTheme MacOSTheme => _macOSTheme!;')
    text = text.replace('_simpleTheme = (SimpleTheme)Resources["SimpleTheme"]!;',
        '_simpleTheme = (SimpleTheme)Resources["SimpleTheme"]!;\n            _macOSTheme = (Avalonia.Themes.MacOS.MacOSTheme)Resources["MacOSTheme"]!;\n            _colorPickerMacOS = (IStyle)Resources["ColorPickerMacOS"]!;')
    text = text.replace('SetCatalogThemes(CatalogTheme.Fluent);',
        '_prevTheme = string.Equals(Environment.GetEnvironmentVariable("AVALONIA_CATALOG_THEME"), "MacOS", StringComparison.OrdinalIgnoreCase)\n                ? CatalogTheme.MacOS : CatalogTheme.Fluent;\n            SetCatalogThemes(_prevTheme);')
    text = text.replace('else if (theme == CatalogTheme.Simple)',
        'else if (theme == CatalogTheme.MacOS)\n            {\n                app._themeStylesContainer[0] = app._macOSTheme!;\n                app._themeStylesContainer[1] = app._colorPickerMacOS!;\n            }\n            else if (theme == CatalogTheme.Simple)')
    path.write_text(text)
    path = CATALOG / 'ViewModels/MainWindowViewModel_PageList.cs'
    text = path.read_text().replace('s.Add<HomePage>("Home", Icons.Home, "Overview of everything in the catalog");',
        's.Add<HomePage>("Home", Icons.Home, "Overview of everything in the catalog");\n            s.Add<MacOSThemePage>("macOS 26", Icons.Palette, "Design tokens, materials and control-state overview");')
    path.write_text(text)
    write(marker, '''<ContentPage xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="ControlCatalog.Pages.MacOSThemePage"
             Header="macOS 26">
  <ContentPage.Styles>
    <Style Selector="Border.card">
      <Setter Property="Background" Value="{DynamicResource MacOS.Brush.Surface}" />
      <Setter Property="BorderBrush" Value="{DynamicResource MacOS.Brush.Separator}" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="CornerRadius" Value="14" />
      <Setter Property="Padding" Value="20" />
      <Setter Property="Margin" Value="6" />
    </Style>
    <Style Selector="TextBlock.section">
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="FontSize" Value="14" />
      <Setter Property="Margin" Value="0,0,0,6" />
    </Style>
    <Style Selector="TextBlock.secondary">
      <Setter Property="Foreground" Value="{DynamicResource MacOS.Brush.SecondaryLabel}" />
      <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
  </ContentPage.Styles>
  <ScrollViewer HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="24" Spacing="16">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Spacing="5">
          <TextBlock Text="Designed to feel at home." FontSize="28" FontWeight="SemiBold" />
          <TextBlock Classes="secondary" Text="macOS 26 · Avalonia controls · Live design tokens" />
        </StackPanel>
        <Button Grid.Column="1" Content="Customize appearance" VerticalAlignment="Center" />
      </Grid>
      <Grid ColumnDefinitions="*,*,*" RowDefinitions="Auto,Auto,Auto">
        <Border Classes="card" Grid.Column="0" Grid.Row="0">
          <StackPanel Spacing="12">
            <TextBlock Classes="section" Text="Actions" />
            <StackPanel Orientation="Horizontal" Spacing="10">
              <Button x:Name="PrimaryAction" Classes="accent" Content="Continue" IsDefault="True" />
              <Button Content="Cancel" />
              <Button Content="Disabled" IsEnabled="False" />
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="10">
              <ToggleButton Content="Selected" IsChecked="True" />
              <ToggleButton Content="Option" />
              <DropDownButton Content="More">
                <DropDownButton.Flyout>
                  <MenuFlyout><MenuItem Header="Duplicate"/><MenuItem Header="Rename"/><Separator/><MenuItem Header="Move to Trash"/></MenuFlyout>
                </DropDownButton.Flyout>
              </DropDownButton>
            </StackPanel>
            <Button x:Name="PopupAction" Content="Open popover">
              <Button.Flyout><Flyout><StackPanel Spacing="12"><TextBlock Text="A focused surface" FontWeight="SemiBold"/><TextBlock Text="Real Avalonia popup content."/><Button Content="Done" Classes="accent"/></StackPanel></Flyout></Button.Flyout>
            </Button>
          </StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="1" Grid.Row="0">
          <StackPanel Spacing="10">
            <TextBlock Classes="section" Text="Selection &amp; switches" />
            <StackPanel Orientation="Horizontal" Spacing="20">
              <CheckBox Content="Enabled" IsChecked="True" />
              <CheckBox Content="Mixed" IsThreeState="True" IsChecked="{x:Null}" />
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="20">
              <RadioButton Content="Automatic" GroupName="appearance" IsChecked="True" />
              <RadioButton Content="Manual" GroupName="appearance" />
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="16">
              <ToggleSwitch IsChecked="True" OnContent="On" OffContent="Off" />
              <ToggleSwitch IsChecked="False" OnContent="On" OffContent="Off" />
              <ToggleSwitch IsChecked="True" IsEnabled="False" OnContent="" OffContent="" />
            </StackPanel>
          </StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="2" Grid.Row="0">
          <StackPanel Spacing="10">
            <TextBlock Classes="section" Text="Text &amp; forms" />
            <TextBox x:Name="SearchField" Watermark="Search your library" />
            <TextBox Text="A thoughtfully crafted interface" />
            <TextBox Text="Read-only value" IsReadOnly="True" />
          </StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="0" Grid.Row="1">
          <StackPanel Spacing="12">
            <TextBlock Classes="section" Text="Values &amp; progress" />
            <Slider Value="62" Maximum="100" />
            <ProgressBar Value="62" Maximum="100" />
            <Grid ColumnDefinitions="*,*">
              <NumericUpDown Value="24" Minimum="0" Maximum="100" Margin="0,0,6,0" />
              <ComboBox Grid.Column="1" SelectedIndex="0" HorizontalAlignment="Stretch" Margin="6,0,0,0"><ComboBoxItem>Balanced</ComboBoxItem><ComboBoxItem>Performance</ComboBoxItem><ComboBoxItem>Quiet</ComboBoxItem></ComboBox>
            </Grid>
            <CalendarDatePicker SelectedDate="2026-09-05" HorizontalAlignment="Stretch" />
          </StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="1" Grid.Row="1">
          <StackPanel Spacing="10">
            <TextBlock Classes="section" Text="Lists &amp; navigation" />
            <ListBox SelectedIndex="1" Height="158">
              <ListBoxItem>All documents</ListBoxItem><ListBoxItem>Recently opened</ListBoxItem><ListBoxItem>Shared with you</ListBoxItem><ListBoxItem IsEnabled="False">Archived documents</ListBoxItem>
            </ListBox>
          </StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="2" Grid.Row="1">
          <StackPanel Spacing="10">
            <TextBlock Classes="section" Text="Tabs &amp; disclosure" />
            <TabControl SelectedIndex="0">
              <TabItem Header="Overview"><TextBlock Classes="secondary" Margin="0,12" Text="A calm hierarchy. Familiar controls. One coherent token system." /></TabItem>
              <TabItem Header="Details"><TextBlock Margin="0,12" Text="Content follows the selected tab."/></TabItem>
              <TabItem Header="History"><TextBlock Margin="0,12" Text="Nothing to show yet."/></TabItem>
            </TabControl>
            <Expander Header="Advanced options" HorizontalAlignment="Stretch"><TextBlock Text="Keyboard and focus behavior are retained." TextWrapping="Wrap" /></Expander>
          </StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="0" Grid.Row="2">
          <StackPanel Spacing="10"><TextBlock Classes="section" Text="Menus"/><Menu><MenuItem Header="File"><MenuItem Header="New document"/><MenuItem Header="Open…"/><Separator/><MenuItem Header="Close"/></MenuItem><MenuItem Header="Edit"><MenuItem Header="Undo"/><MenuItem Header="Redo"/></MenuItem><MenuItem Header="View"><MenuItem Header="Show sidebar" ToggleType="CheckBox" IsChecked="True"/></MenuItem></Menu><TextBlock Classes="secondary" Text="Keyboard navigation, submenus and popup placement remain native Avalonia behavior."/></StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="1" Grid.Row="2">
          <StackPanel Spacing="10"><TextBlock Classes="section" Text="Hierarchy"/><TreeView Height="128"><TreeViewItem Header="Workspace" IsExpanded="True"><TreeViewItem Header="Design library"/><TreeViewItem Header="Project files" IsSelected="True"/><TreeViewItem Header="Shared assets"/></TreeViewItem></TreeView></StackPanel>
        </Border>
        <Border Classes="card" Grid.Column="2" Grid.Row="2">
          <StackPanel Spacing="10"><TextBlock Classes="section" Text="Built for adaptation"/><TextBlock Classes="secondary" Text="Light and dark appearance. Compact density. Increased contrast. Reduced motion. Opaque material fallback."/><Separator/><TextBlock Text="No bundled Apple fonts or symbol assets." FontSize="11" Classes="secondary"/></StackPanel>
        </Border>
      </Grid>
    </StackPanel>
  </ScrollViewer>
</ContentPage>
''')
    write(CATALOG / 'Pages/MacOSThemePage.xaml.cs', '''using Avalonia.Controls;

namespace ControlCatalog.Pages;

public partial class MacOSThemePage : ContentPage
{
    public MacOSThemePage() => InitializeComponent();
}
''')
    commit('feat(catalog): add macOS theme selection and a real control-state showcase', 'samples/ControlCatalog')

    harness = ROOT / 'samples/ControlCatalog.MacOS'
    write(harness / 'ControlCatalog.MacOS.csproj', '''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$(AvsCurrentTargetFramework)</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ControlCatalog/ControlCatalog.csproj" />
    <ProjectReference Include="../../src/Headless/Avalonia.Headless/Avalonia.Headless.csproj" />
    <ProjectReference Include="../../src/HarfBuzz/Avalonia.HarfBuzz/Avalonia.HarfBuzz.csproj" />
  </ItemGroup>
  <Import Project="../../build/BuildTargets.targets" />
  <Import Project="../../build/SampleApp.props" />
</Project>
''')
    write(harness / 'Program.cs', '''using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.MacOS;
using Avalonia.Threading;
using ControlCatalog.Pages;
using ControlCatalog.ViewModels;

namespace ControlCatalog.MacOS;

internal static class Program
{
    private static readonly List<object> s_evidence = new();
    private static string s_output = "artifacts/macos-theme";
    private static bool s_headless;

    [STAThread]
    private static int Main(string[] args)
    {
        s_headless = args.Contains("--headless", StringComparer.Ordinal);
        var index = Array.IndexOf(args, "--output");
        if (index >= 0)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException("--output requires a directory.");
            s_output = Path.GetFullPath(args[index + 1]);
        }
        Directory.CreateDirectory(s_output);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Environment.SetEnvironmentVariable("AVALONIA_CATALOG_THEME", "MacOS");
        var builder = AppBuilder.Configure<ControlCatalog.App>().UseSkia().UseHarfBuzz();
        builder = s_headless
            ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true })
            : builder.UsePlatformDetect();
        return builder.AfterSetup(_ => DispatcherTimer.RunOnce(Capture, TimeSpan.FromMilliseconds(350)))
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static async void Capture()
    {
        var app = (ControlCatalog.App)Application.Current!;
        var lifetime = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;
        try
        {
            var window = lifetime.MainWindow ?? throw new InvalidOperationException("ControlCatalog did not create a main window.");
            window.Width = 1280;
            window.Height = 1040;
            var theme = app.MacOSTheme;
            theme.ReduceMotion = true;
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = variant;
                window.Content = new MacOSThemePage();
                await Save(window, "overview-" + variant.Key);
            }
            window.RequestedThemeVariant = ThemeVariant.Light;
            theme.DensityStyle = DensityStyle.Compact;
            window.Content = new MacOSThemePage();
            await Save(window, "overview-Light-compact");
            theme.DensityStyle = DensityStyle.Normal;
            theme.IncreaseContrast = true;
            theme.ReduceTransparency = true;
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = variant;
                window.Content = new MacOSThemePage();
                await Save(window, "overview-" + variant.Key + "-contrast");
            }
            theme.IncreaseContrast = null;
            theme.ReduceTransparency = false;
            window.RequestedThemeVariant = ThemeVariant.Light;
            window.FlowDirection = FlowDirection.RightToLeft;
            window.Content = new MacOSThemePage();
            await Save(window, "overview-Light-rtl");
            window.FlowDirection = FlowDirection.LeftToRight;
            theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse("#F5CE42"));
            window.Content = new MacOSThemePage();
            await Save(window, "overview-Light-custom-accent");
            theme.ResetToken(MacOSTokens.SystemAccentColor);

            var requested = new HashSet<string>(StringComparer.Ordinal)
            {
                "Buttons", "CheckBox", "RadioButton", "ToggleSwitch", "TextBox", "ComboBox",
                "NumericUpDown", "Slider", "TreeView", "TableView", "Menu", "ColorPicker",
                "CommandBar", "Expander", "TabControl", "SplitView", "DatePicker", "TimePicker",
                "DateTimePicker", "CalendarDatePicker", "AutoCompleteBox", "ScrollViewer"
            };
            foreach (var item in new MainWindowViewModel().Pages.Where(p => requested.Contains(p.Header)))
            {
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = variant;
                    window.Content = item.CreatePage();
                    await Save(window, "catalog-" + item.Header + "-" + variant.Key);
                }
            }
            var page = new MacOSThemePage();
            window.Content = page;
            window.RequestedThemeVariant = ThemeVariant.Light;
            await Task.Delay(150);
            page.FindControl<Button>("PrimaryAction")!.Focus(NavigationMethod.Tab);
            await Save(window, "overview-Light-keyboard-focus");
            var popupButton = page.FindControl<Button>("PopupAction")!;
            popupButton.Flyout!.ShowAt(popupButton);
            await Save(window, "overview-Light-popover");
            popupButton.Flyout.Hide();

            File.WriteAllText(Path.Combine(s_output, "manifest.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                commit = Environment.GetEnvironmentVariable("MACOS_THEME_COMMIT") ?? "local-working-tree",
                operatingSystem = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                windowing = s_headless ? "Avalonia.Headless" : "native platform backend",
                capture = "RenderTargetBitmap of the running ControlCatalog window client area; not an OS desktop screenshot",
                screenshots = s_evidence
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Captured {s_evidence.Count} real ControlCatalog frames.");
            lifetime.Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(s_output, "capture-error.txt"), error.ToString());
            lifetime.Shutdown(1);
        }
    }

    private static async Task Save(Window window, string name)
    {
        await Task.Delay(180);
        window.UpdateLayout();
        var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height));
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("Cannot capture an unarranged window.");
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(window);
        var path = Path.Combine(s_output, name + ".png");
        bitmap.Save(path);
        var theme = ((ControlCatalog.App)Application.Current!).MacOSTheme;
        s_evidence.Add(new
        {
            file = Path.GetFileName(path), width = size.Width, height = size.Height,
            variant = window.ActualThemeVariant.Key.ToString(), density = theme.DensityStyle.ToString(),
            increaseContrast = theme.IncreaseContrast, reduceMotion = theme.ReduceMotion,
            reduceTransparency = theme.ReduceTransparency, flowDirection = window.FlowDirection.ToString(),
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
        });
        Console.WriteLine("Captured " + name);
    }
}
''')
    commit('test(macos): add native/headless ControlCatalog screenshot harness and provenance manifests', 'samples/ControlCatalog.MacOS')

if __name__ == '__main__':
    main()
