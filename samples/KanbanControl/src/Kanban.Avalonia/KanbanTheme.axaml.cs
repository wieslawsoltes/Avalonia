using Avalonia.Markup.Xaml;
using Avalonia.Styling;
namespace Kanban.Avalonia;
/// <summary>Include after the application's base theme to install replaceable Kanban control themes.</summary>
public sealed partial class KanbanTheme : Styles
{
    public KanbanTheme() => AvaloniaXamlLoader.Load(this);
}
