using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Themes.MacOS.UnitTests;

public sealed class ThemeTests
{
    private static MacOSTheme Theme => ((TestApplication)Application.Current!).Theme;

    private static T Resolve<T>(string key, ThemeVariant? variant = null)
    {
        Assert.True(((IResourceNode)Theme).TryGetResource(key, variant ?? ThemeVariant.Light, out var value), key);
        return Assert.IsAssignableFrom<T>(value);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_Typed_Component_Token_Resolves_With_The_Declared_Type(bool dark)
    {
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var fields = typeof(MacOSTokens).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.True(fields.Length >= 998);
        foreach (var field in fields)
        {
            var token = field.GetValue(null)!;
            var key = (string)field.FieldType.GetProperty("Key")!.GetValue(token)!;
            Assert.True(((IResourceNode)Theme).TryGetResource(key, variant, out var value), key);
            Assert.IsAssignableFrom(field.FieldType.GetGenericArguments()[0], value);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_Inherited_Control_Theme_Is_Present(bool dark)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "token-manifest.json")));
        var assemblies = new[] { typeof(Control).Assembly, Assembly.Load("Avalonia.Dialogs") };
        var types = assemblies.SelectMany(a => a.GetTypes()).ToArray();
        foreach (var item in document.RootElement.GetProperty("controls").EnumerateArray())
        {
            var name = item.GetProperty("type").GetString()!.Split(':')[^1];
            var type = Assert.Single(types, t => t.Name == name);
            Assert.True(((IResourceNode)Theme).TryGetResource(type, dark ? ThemeVariant.Dark : ThemeVariant.Light, out var resource), name);
            Assert.Equal(type, Assert.IsType<ControlTheme>(resource).TargetType);
        }
    }

    [AvaloniaFact]
    public void Core_Theme_Does_Not_Reference_Fluent_Assembly()
    {
        Assert.DoesNotContain(typeof(MacOSTheme).Assembly.GetReferencedAssemblies(), a => a.Name == "Avalonia.Themes.Fluent");
    }

    [AvaloniaFact]
    public void Legacy_Resource_Lookup_Preserves_Compatibility()
    {
        Assert.Equal(Resolve<Thickness>(MacOSTokens.ButtonPadding.Key), Resolve<Thickness>("ButtonPadding"));
        Assert.Equal(Resolve<Color>(MacOSTokens.SystemAccentColor.Key), Resolve<Color>("SystemAccentColor"));
    }

    [AvaloniaFact]
    public void Component_Override_And_Reset_Update_An_Existing_Control()
    {
        var button = new Button { Content = "Continue" };
        using var host = new Host(button);
        var before = button.Padding;
        Theme.SetToken(MacOSTokens.ButtonPadding, new Thickness(27, 9));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Thickness(27, 9), button.Padding);
        Assert.True(Theme.ResetToken(MacOSTokens.ButtonPadding));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, button.Padding);
    }

    [AvaloniaFact]
    public void Semantic_Color_Override_Updates_Existing_Brushes()
    {
        var button = new Button { Content = "Continue" };
        using var host = new Host(button);
        Theme.SetToken(MacOSSemanticTokens.Control, Color.Parse("#F1E2D3"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#F1E2D3"), Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
    }

    [AvaloniaFact]
    public void Per_Variant_Overrides_Do_Not_Leak_To_Other_Variants()
    {
        var originalDark = Resolve<Thickness>(MacOSTokens.ButtonPadding.Key, ThemeVariant.Dark);
        Theme.SetToken(MacOSTokens.ButtonPadding, new Thickness(23), ThemeVariant.Light);
        Assert.Equal(new Thickness(23), Resolve<Thickness>(MacOSTokens.ButtonPadding.Key, ThemeVariant.Light));
        Assert.Equal(originalDark, Resolve<Thickness>(MacOSTokens.ButtonPadding.Key, ThemeVariant.Dark));
        Assert.True(Theme.ResetToken(MacOSTokens.ButtonPadding, ThemeVariant.Light));
    }

    [AvaloniaFact]
    public void Light_And_Dark_Scopes_Can_Coexist_In_One_Visual_Tree()
    {
        var light = new Button { Content = "Light" };
        var dark = new Button { Content = "Dark" };
        using var host = new Host(new StackPanel
        {
            Children =
            {
                new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = light },
                new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = dark }
            }
        });
        var lightColor = Assert.IsAssignableFrom<ISolidColorBrush>(light.Background).Color;
        var darkColor = Assert.IsAssignableFrom<ISolidColorBrush>(dark.Background).Color;
        Assert.NotEqual(lightColor, darkColor);
    }

    [AvaloniaFact]
    public void Compact_Density_Is_Live_And_Does_Not_Override_Explicit_Tokens()
    {
        var input = new TextBox();
        using var host = new Host(input);
        var regular = input.MinHeight;
        Theme.DensityStyle = DensityStyle.Compact;
        Dispatcher.UIThread.RunJobs();
        Assert.True(input.MinHeight < regular);
        Theme.SetToken(MacOSTokens.TextControlThemeMinHeight, 37d);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(37d, input.MinHeight);
    }

    [AvaloniaFact]
    public void Reduced_Motion_Disables_Interaction_Transitions()
    {
        Theme.ReduceMotion = true;
        Assert.Equal(TimeSpan.Zero, Resolve<TimeSpan>("MacOS.Motion.Interaction"));
        Assert.Equal(TimeSpan.Zero, Resolve<TimeSpan>("MacOS.Motion.Switch"));
        Assert.Equal(TimeSpan.Zero, Resolve<TimeSpan>(MacOSTokens.SplitViewPaneAnimationOpenDuration.Key));
        Theme.ReduceMotion = false;
        Assert.True(Resolve<TimeSpan>("MacOS.Motion.Switch") > TimeSpan.Zero);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reduced_Transparency_Produces_Opaque_Materials(bool dark)
    {
        Theme.ReduceTransparency = true;
        Assert.Equal(byte.MaxValue, Resolve<Color>(MacOSSemanticTokens.Material.Key, dark ? ThemeVariant.Dark : ThemeVariant.Light).A);
    }

    [AvaloniaFact]
    public void Increased_Contrast_Strengthens_Focus_And_Separators()
    {
        Theme.IncreaseContrast = true;
        Assert.Equal(new Thickness(4), Resolve<Thickness>(MacOSTokens.SystemControlFocusVisualPrimaryThickness.Key));
        Assert.Equal(byte.MaxValue, Resolve<Color>(MacOSSemanticTokens.Separator.Key).A);
    }

    [AvaloniaFact]
    public void Bright_Custom_Accent_Gets_Contrasting_Labels()
    {
        Theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse("#FFCC00"));
        Assert.Equal(Colors.Black, Resolve<Color>(MacOSSemanticTokens.OnAccent.Key));
        Theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse("#003399"));
        Assert.Equal(Colors.White, Resolve<Color>(MacOSSemanticTokens.OnAccent.Key));
    }

    [AvaloniaFact]
    public void Button_Pointer_And_Keyboard_Focus_Contracts_Are_Preserved()
    {
        var clicks = 0;
        var button = new Button { Content = "Continue", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        button.Click += (_, _) => clicks++;
        using var host = new Host(button);
        host.Window.MouseDown(new Point(50, 50), MouseButton.Left);
        host.Window.MouseUp(new Point(50, 50), MouseButton.Left);
        Assert.Equal(1, clicks);
        Assert.True(button.Focus(NavigationMethod.Tab));
        Assert.True(button.IsFocused);
        button.IsEnabled = false;
        host.Window.MouseDown(new Point(50, 50), MouseButton.Left);
        host.Window.MouseUp(new Point(50, 50), MouseButton.Left);
        Assert.Equal(1, clicks);
    }

    [AvaloniaFact]
    public void Checked_State_Is_Driven_By_The_Actual_CheckBox_Control()
    {
        var check = new CheckBox { Content = "Enabled", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        using var host = new Host(check);
        host.Window.MouseDown(new Point(40, 40), MouseButton.Left);
        host.Window.MouseUp(new Point(40, 40), MouseButton.Left);
        Assert.True(check.IsChecked);
    }

    [AvaloniaFact]
    public void Flyout_Can_Open_And_Close_With_MacOS_Resources()
    {
        var button = new Button { Content = "Open", Flyout = new Flyout { Content = new TextBox { Text = "Popover" } } };
        using var host = new Host(button);
        button.Flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.Flyout.IsOpen);
        button.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.Flyout.IsOpen);
    }

    [AvaloniaFact]
    public void Skia_Renders_A_Nonempty_Control_Frame()
    {
        using var host = new Host(new Button { Content = "macOS" });
        using var frame = host.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var output = new MemoryStream();
        frame.Save(output, PngBitmapEncoderOptions.Default);
        Assert.True(output.Length > 100);
    }

    [AvaloniaTheory]
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

    [AvaloniaFact]
    public void Optional_ColorPicker_Companion_Uses_The_Public_Token_Overrides()
    {
        Application.Current!.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.ColorPicker/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/MacOS/MacOS.xaml")
        });
        var control = new Avalonia.Controls.Primitives.ColorSlider();
        using var host = new Host(control);
        Theme.SetToken(MacOSTokens.ColorPicker_ColorSliderSize, 34d);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(34d, control.Height);
    }

    [AvaloniaTheory]
    [InlineData("#007AFF")]
    [InlineData("#0A84FF")]
    [InlineData("#FFCC00")]
    [InlineData("#8E44AD")]
    public void Accent_Fill_And_Label_Maintain_At_Least_4_Point_5_Contrast(string value)
    {
        Theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse(value));
        var background = Resolve<Color>(MacOSSemanticTokens.Accent.Key);
        var foreground = Resolve<Color>(MacOSSemanticTokens.OnAccent.Key);
        static double Linear(byte component)
        {
            var c = component / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        var a = Luminance(background);
        var b = Luminance(foreground);
        var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        Assert.True(ratio >= 4.5, $"{value}: contrast was {ratio:F3}:1");
    }

    [AvaloniaFact]
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

    private sealed class Host : IDisposable
    {
        public Window Window { get; }

        public Host(Control control)
        {
            Window = new Window { Width = 320, Height = 160, Content = control, RequestedThemeVariant = ThemeVariant.Light };
            Window.Show();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => Window.Close();
    }
}
