using System.Collections.Generic;
using ControlCatalog.Models;
using ControlCatalog.ViewModels;

namespace ControlCatalog;

/// <summary>Exposes the catalog's existing page factories to the screenshot harness.</summary>
public static class MacOSCatalogPages
{
    /// <summary>Creates an isolated catalog page inventory without exposing its view model.</summary>
    public static IReadOnlyList<PageItem> Create() => new MainWindowViewModel().Pages;
}
