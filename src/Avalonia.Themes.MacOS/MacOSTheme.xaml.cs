using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Avalonia.Themes.MacOS
{
    public enum DensityStyle
    {
        Normal,
        Compact
    }

    /// <summary>
    /// Includes the macOS theme in an application.
    /// </summary>
    public class MacOSTheme : Styles, IResourceNode
    {
        private readonly ResourceDictionary _compactStyles;
        private DensityStyle _densityStyle;

        /// <summary>
        /// Initializes a new instance of the <see cref="MacOSTheme"/> class.
        /// </summary>
        /// <param name="sp">The parent's service provider.</param>
        public MacOSTheme(IServiceProvider? sp = null)
        {
            AvaloniaXamlLoader.Load(sp, this);
            
            _compactStyles = (ResourceDictionary)GetAndRemove("CompactStyles");

            Palettes = Resources.MergedDictionaries.OfType<ColorPaletteResourcesCollection>().FirstOrDefault()
                ?? throw new InvalidOperationException("MacOSTheme was initialized with missing ColorPaletteResourcesCollection.");
            
            object GetAndRemove(string key)
            {
                var val = Resources[key]
                          ?? throw new KeyNotFoundException($"Key {key} was not found in the resources");
                Resources.Remove(key);
                return val;
            }
        }

        public static readonly DirectProperty<MacOSTheme, DensityStyle> DensityStyleProperty = AvaloniaProperty.RegisterDirect<MacOSTheme, DensityStyle>(
            nameof(DensityStyle), o => o.DensityStyle, (o, v) => o.DensityStyle = v);

        /// <summary>
        /// Gets or sets the density style of the macOS theme (normal, compact).
        /// </summary>
        public DensityStyle DensityStyle
        {
            get => _densityStyle;
            set => SetAndRaise(DensityStyleProperty, ref _densityStyle, value);
        }

        public IDictionary<ThemeVariant, ColorPaletteResources> Palettes { get; }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == DensityStyleProperty)
            {
                Owner?.NotifyHostedResourcesChanged(ResourcesChangedEventArgs.Create());
            }
        }

        bool IResourceNode.TryGetResource(object key, ThemeVariant? theme, out object? value)
        {
            // DensityStyle dictionary should be checked first
            if (_densityStyle == DensityStyle.Compact
                && _compactStyles.TryGetResource(key, theme, out value))
            {
                return true;
            }

            return base.TryGetResource(key, theme, out value);
        }
    }
}
