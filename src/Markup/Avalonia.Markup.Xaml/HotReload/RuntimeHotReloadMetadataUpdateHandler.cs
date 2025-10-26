[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Avalonia.Markup.Xaml.HotReload.RuntimeHotReloadMetadataUpdateHandler))]

namespace Avalonia.Markup.Xaml.HotReload;

internal static class RuntimeHotReloadMetadataUpdateHandler
{
    public static void ClearCache(System.Type[]? types)
    {
        if (types is null || types.Length == 0)
        {
            RuntimeHotReloadService.ClearDelegates();
        }
        else
        {
            var keys = new string[types.Length];
            for (var i = 0; i < types.Length; ++i)
                keys[i] = types[i].FullName ?? types[i].Name;
            RuntimeHotReloadService.InvalidateDelegates(keys);
        }
    }

    public static void UpdateApplication(System.Type[]? types)
    {
        RuntimeHotReloadService.ReloadRegisteredManifests();
    }
}
