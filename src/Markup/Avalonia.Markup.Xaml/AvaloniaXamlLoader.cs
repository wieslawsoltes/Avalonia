using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Platform;
#if !NETSTANDARD2_0
using Avalonia.Markup.Xaml.HotReload;
#endif

namespace Avalonia.Markup.Xaml
{
    /// <summary>
    /// Loads XAML for a avalonia application.
    /// </summary>
    public static class AvaloniaXamlLoader
    {
        internal interface IRuntimeXamlLoader
        {
            object Load(RuntimeXamlLoaderDocument document, RuntimeXamlLoaderConfiguration configuration);
        }
        
        /// <summary>
        /// Loads the XAML into a Avalonia component.
        /// </summary>
        /// <param name="obj">The object to load the XAML into.</param>
        public static void Load(object obj)
        {
            throw new XamlLoadException(
                $"No precompiled XAML found for {obj.GetType()}, make sure to specify x:Class and include your XAML file as AvaloniaResource");
        }
        
        /// <summary>
        /// Loads the XAML into a Avalonia component.
        /// </summary>
        /// <param name="sp">The parent's service provider.</param>
        /// <param name="obj">The object to load the XAML into.</param>
        public static void Load(IServiceProvider? sp, object obj)
        {
            throw new XamlLoadException(
                $"No precompiled XAML found for {obj.GetType()}, make sure to specify x:Class and include your XAML file as AvaloniaResource");
        }

        /// <summary>
        /// Loads XAML from a URI.
        /// </summary>
        /// <param name="uri">The URI of the XAML file.</param>
        /// <param name="baseUri">
        /// A base URI to use if <paramref name="uri"/> is relative.
        /// </param>
        /// <returns>The loaded object.</returns>
        [RequiresUnreferencedCode(TrimmingMessages.AvaloniaXamlLoaderRequiresUnreferenceCodeMessage)]
        public static object Load(Uri uri, Uri? baseUri = null)
        {
            return Load(null, uri, baseUri);
        }

        /// <summary>
        /// Loads XAML from a URI.
        /// </summary>
        /// <param name="sp">The parent's service provider.</param>
        /// <param name="uri">The URI of the XAML file.</param>
        /// <param name="baseUri">
        /// A base URI to use if <paramref name="uri"/> is relative.
        /// </param>
        /// <returns>The loaded object.</returns>
        [RequiresUnreferencedCode(TrimmingMessages.AvaloniaXamlLoaderRequiresUnreferenceCodeMessage)]
        public static object Load(IServiceProvider? sp, Uri uri, Uri? baseUri = null)
        {
            if (uri is null)
                throw new ArgumentNullException(nameof(uri));

            var assetLocator = AvaloniaLocator.Current.GetService<IAssetLoader>();

            if (assetLocator == null)
            {
                throw new InvalidOperationException(
                    "Could not create IAssetLoader : maybe Application.RegisterServices() wasn't called?");
            }

            var absoluteUri = uri.IsAbsoluteUri
                ? uri
                : new Uri(baseUri ?? throw new InvalidOperationException("Cannot load relative Uri when BaseUri is null"), uri);

            var compiledLoader = assetLocator.GetAssembly(uri, baseUri)
                ?.GetType("CompiledAvaloniaXaml.!XamlLoader")
                ?.GetMethod("TryLoad", new[] { typeof(IServiceProvider), typeof(string) });
            if (compiledLoader != null)
            {
                var compiledResult = compiledLoader.Invoke(null, new object?[] { sp, absoluteUri.ToString()});
                if (compiledResult != null)
                    return compiledResult;
            }
            else
            {
                compiledLoader = assetLocator.GetAssembly(uri, baseUri)
                    ?.GetType("CompiledAvaloniaXaml.!XamlLoader")
                    ?.GetMethod("TryLoad", new[] {typeof(string)});
                if (compiledLoader != null)
                {
                    var compiledResult = compiledLoader.Invoke(null, new object?[] {absoluteUri.ToString()});
                    if (compiledResult != null)
                        return compiledResult;
                }   
            }

            // This is intended for unit-tests only
            var runtimeLoader = AvaloniaLocator.Current.GetService<IRuntimeXamlLoader>();
            if (runtimeLoader != null)
            {
                var asset = assetLocator.OpenAndGetAssembly(uri, baseUri);
#if !NETSTANDARD2_0
                TryRegisterHotReloadManifest(asset.assembly);
#endif
                using (var stream = asset.stream)
                {
                    var document = new RuntimeXamlLoaderDocument(absoluteUri, stream) { ServiceProvider = sp };
                    var configuration = new RuntimeXamlLoaderConfiguration { LocalAssembly = asset.assembly };
                    return runtimeLoader.Load(document, configuration);
                }
            }

            throw new XamlLoadException(
                $"No precompiled XAML found for {uri} (baseUri: {baseUri}), make sure to specify x:Class and include your XAML file as AvaloniaResource");
        }
        
    }

#if !NETSTANDARD2_0
    static void TryRegisterHotReloadManifest(Assembly? assembly)
    {
        try
        {
            var location = assembly?.Location;
            if (string.IsNullOrEmpty(location))
                return;

            var manifestPath = Path.ChangeExtension(location, ".axaml.hotreload.json");
            if (manifestPath != null)
            {
                if (File.Exists(manifestPath))
                {
                    RuntimeHotReloadService.RegisterManifestPath(manifestPath);
                }
                else if (assembly != null)
                {
                    var resourceName = assembly
                        .GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith(".axaml.hotreload.json", StringComparison.OrdinalIgnoreCase));
                    if (resourceName != null)
                    {
                        using var manifestStream = assembly.GetManifestResourceStream(resourceName);
                        if (manifestStream != null)
                            RuntimeHotReloadService.RegisterManifest(manifestStream);
                    }
                }
            }
        }
        catch
        {
            // Ignore manifest registration failures.
        }
    }
#endif
}
