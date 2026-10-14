using Avalonia.Markup.Xaml;
using Avalonia.Styling;
namespace Avalonia.Inking.Themes;
/// <summary>Add after FluentTheme. All control templates, palettes, and presenters can be replaced by the host.</summary>
public sealed class InkTheme : Styles
{
    public InkTheme() => AvaloniaXamlLoader.Load(this);
}
