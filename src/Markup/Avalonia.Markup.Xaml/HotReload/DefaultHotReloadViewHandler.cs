using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Controls.Primitives;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Default handler that re-applies templates and invalidates layout after a hot reload cycle.
/// </summary>
internal sealed class DefaultHotReloadViewHandler : IHotReloadViewHandler
{
    public static readonly DefaultHotReloadViewHandler Instance = new();

    private DefaultHotReloadViewHandler()
    {
    }

    public void OnBeforeReload(IHotReloadableView view)
    {
        if (view is not Control control)
            return;

        // Ensure any pending layout work is committed before we mutate the tree.
        control.UpdateLayout();
    }

    public void OnAfterReload(IHotReloadableView view)
    {
        if (view is StyledElement styled)
        {
            styled.InvalidateStyles(recurse: true);
        }

        if (view is Control control)
        {
            if (control is TemplatedControl templated)
            {
                // Force the template to be rebuilt with the new resources/types.
                templated.ApplyTemplate();
            }

            control.InvalidateMeasure();
            control.InvalidateArrange();
            control.InvalidateVisual();
        }
    }
}
