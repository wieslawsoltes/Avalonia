# Code-Only Avalonia Guide

Avalonia's API lets you build complete user interfaces directly in C# without using XAML. This document summarizes common patterns used throughout the repository and explains how they work.

## 1. Instantiating Controls

Controls are standard CLR objects. You can construct them with object initializers and compose the visual tree by assigning children. This mirrors what a XAML file would produce.

```csharp
var window = new Window
{
    Width = 400,
    Height = 300,
    Content = new StackPanel
    {
        Children =
        {
            new Button { Content = "Ok" },
            new Button { Content = "Cancel" }
        }
    }
};
window.Show();
```

## 2. Control Templates with `FuncControlTemplate`

`FuncControlTemplate` is a delegate-based template. Avalonia calls the delegate whenever the template is applied. The delegate receives the control being templated and an `INameScope` used for registering named parts.

```csharp
private static IControlTemplate TabControlTemplate()
{
    return new FuncControlTemplate<TabControl>((parent, scope) =>
        new StackPanel
        {
            Children =
            {
                new ItemsPresenter
                {
                    Name = "PART_ItemsPresenter",
                }.RegisterInNameScope(scope),
                new ContentPresenter
                {
                    Name = "PART_SelectedContentHost",
                    [~ContentPresenter.ContentProperty] =
                        new TemplateBinding(TabControl.SelectedContentProperty),
                    [~ContentPresenter.ContentTemplateProperty] =
                        new TemplateBinding(TabControl.SelectedContentTemplateProperty),
                }.RegisterInNameScope(scope)
            }
        });
}
```

`RegisterInNameScope` throws if `Name` is null, ensuring template parts can be located.

## 3. Data and Tree Templates

`FuncDataTemplate` and `FuncTreeDataTemplate` let you create templates for items and hierarchical data.

```csharp
var itemTemplate = new FuncDataTemplate<string>((item, _) =>
    new TextBlock { Text = item });

var treeTemplate = new FuncTreeDataTemplate<Node>(
    (node, _) => new TextBlock { Text = node.Name },
    node => node.Children);
```

`FuncTreeDataTemplate` extends `FuncDataTemplate` and also provides an `ItemsSelector` callback that returns the child collection.

## 4. Binding Syntax

Bindings are objects that connect properties to data. In templates, the indexer syntax `[~Property]` creates a binding to the templated parent via `TemplateBinding`.

```csharp
[~TextBlock.TextProperty] = new TemplateBinding(ContentControl.ContentProperty)
```

`CompiledBindingExtension` precompiles the expression for performance:

```csharp
[!TextBox.TextProperty] = new CompiledBindingExtension
{
    Path = nameof(MyViewModel.Text),
    Mode = BindingMode.TwoWay
};
```

## 5. Name Scopes and Template Parts

`INameScope` is used to look up elements created in a template. The helper `RegisterInNameScope` adds the control to a scope and ensures it has a name.

```csharp
public static T RegisterInNameScope<T>(this T control, INameScope scope)
    where T : StyledElement
{
    if (control.Name is null)
        throw new ArgumentException("RegisterInNameScope must be called on a control with non-null name.");

    scope.Register(control.Name, control);
    return control;
}
```

## 6. Styling

Styles are created in code with selector expressions and a collection of setters. Adding the style to `Application.Current.Styles` applies it globally.

```csharp
Style style = new Style(x => x.OfType<Border>().Class("highlight"))
{
    Setters =
    {
        new Setter(Border.BorderThicknessProperty, new Thickness(2)),
    }
};
Application.Current.Styles.Add(style);
```

## 7. Resources and Theming

Resource dictionaries can be merged and looked up dynamically. Theme dictionaries provide different values per `ThemeVariant`.

```csharp
var scope = new ThemeVariantScope
{
    RequestedThemeVariant = ThemeVariant.Light,
    Resources = new ResourceDictionary
    {
        ThemeDictionaries =
        {
            [ThemeVariant.Dark]  = new ResourceDictionary { ["DemoBackground"] = Brushes.Black },
            [ThemeVariant.Light] = new ResourceDictionary { ["DemoBackground"] = Brushes.White }
        }
    },
    Child = new Border()
};
var border = (Border)scope.Child!;
border[!Border.BackgroundProperty] = new DynamicResourceExtension("DemoBackground");
DelayedBinding.ApplyBindings(border);
```

Switching `RequestedThemeVariant` on the scope updates the bound background brush.

## 8. Animations

### Page Transitions

Implement `IPageTransition` to define custom transitions. The transition receives the old and new visuals and a cancellation token.

```csharp
class TestTransition : IPageTransition
{
    TaskCompletionSource? _tcs;
    public int StartCount { get; private set; }
    public int FinishCount { get; private set; }
    public int CancelCount { get; private set; }

    public event Action<Visual?, Visual?, bool>? Started;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken ct)
    {
        ++StartCount;
        Started?.Invoke(from, to, forward);
        _tcs = new TaskCompletionSource();
        ct.Register(() => _tcs.TrySetResult());
        await _tcs.Task;
        _tcs = null;

        if (!ct.IsCancellationRequested)
            ++FinishCount;
        else
            ++CancelCount;
    }

    public void Complete() => _tcs!.TrySetResult();
}
```

Attach it to a control:

```csharp
var transition = new TestTransition();
var control = new TransitioningContentControl
{
    Content = someContent,
    PageTransition = transition,
    Template = CreateTemplate(),
};
```

### Key Frame Animations

Animations consist of key frames with setters. They run against a clock, which can be a `TestClock` for deterministic progression.

```csharp
var animation = new Avalonia.Animation.Animation
{
    Duration = TimeSpan.FromSeconds(5),
    Children =
    {
        new KeyFrame
        {
            KeyTime = TimeSpan.Zero,
            Setters = { new Setter(RotateTransform.AngleProperty, -2.5) }
        },
        new KeyFrame
        {
            KeyTime = TimeSpan.FromSeconds(5),
            Setters = { new Setter(RotateTransform.AngleProperty, 2.5) }
        }
    },
    IterationCount = new IterationCount(5),
    PlaybackDirection = PlaybackDirection.Alternate,
    Easing = new SpringEasing(1, 10, 1)
};

var transform = new RotateTransform(-2.5);
var rect = new Rectangle { RenderTransform = transform };
var clock = new TestClock();
animation.RunAsync(rect, clock);
```

Advance the animation in tests using `clock.Step(...)`.

## 9. State Management

### `INotifyPropertyChanged`

A simple base class implements `INotifyPropertyChanged` so view models can notify bindings.

```csharp
public class NotifyingBase : INotifyPropertyChanged
{
    private PropertyChangedEventHandler? _propertyChanged;
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { _propertyChanged += value; }
        remove { _propertyChanged -= value; }
    }

    public void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

Properties simply call `RaisePropertyChanged` in their setters.

### Reactive Bindings

Reactive streams such as `BehaviorSubject` can feed styled properties directly.

```csharp
var target = new ComboBox
{
    ItemsSource = new[] { "Foo" },
};

var source = new BehaviorSubject<BindingNotification>(
    new BindingNotification(new InvalidCastException("failed"), BindingErrorType.DataValidationError));

target.Bind(ComboBox.SelectedItemProperty, source);
```

## 10. Application Startup

Even without XAML you still use `AppBuilder` to configure Avalonia and start the main window.

```csharp
[STAThread]
public static void Main(string[] args)
{
    BuildAvaloniaApp().Start(AppMain, args);
}

private static void AppMain(Application app, string[] args)
{
    app.Run(new MainWindow());
}

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
```

## 11. Complete Example

The following minimal application combines templates, bindings, styles, animations and resources defined entirely in code.

```csharp
class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Resources = new ResourceDictionary
        {
            ["Accent"] = Brushes.CornflowerBlue
        };

        Styles.Add(new Style(x => x.OfType<Button>().Class("accent"))
        {
            Setters = { new Setter(Button.BackgroundProperty, new DynamicResourceExtension("Accent")) }
        });

        var vm = new MainViewModel { Text = "Hello" };

        var window = new Window
        {
            Width = 300,
            Height = 200,
            DataContext = vm,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBox
                    {
                        [!TextBox.TextProperty] = new CompiledBindingExtension
                        {
                            Path = nameof(MainViewModel.Text),
                            Mode = BindingMode.TwoWay
                        }
                    },
                    new Button
                    {
                        Classes = { "accent" },
                        Content = "Animate"
                    }
                }
            }
        };

        var animation = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            Children =
            {
                new KeyFrame
                {
                    KeyTime = TimeSpan.Zero,
                    Setters = { new Setter(Window.OpacityProperty, 0.0) }
                },
                new KeyFrame
                {
                    KeyTime = TimeSpan.FromSeconds(1),
                    Setters = { new Setter(Window.OpacityProperty, 1.0) }
                }
            }
        };

        window.Opened += async (_, __) => await animation.RunAsync(window, null);
        window.Show();
        base.OnFrameworkInitializationCompleted();
    }
}

class MainViewModel : NotifyingBase
{
    string _text = string.Empty;
    public string Text
    {
        get => _text;
        set { _text = value; RaisePropertyChanged(); }
    }
}
```

This example creates resources, a style, a view model, bindings, and an animation programmatically and runs a window without any XAML.
