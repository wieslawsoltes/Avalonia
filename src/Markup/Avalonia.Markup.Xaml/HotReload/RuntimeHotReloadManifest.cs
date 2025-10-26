using System;
using System.Collections.Generic;
using System.IO;

#if !NETSTANDARD2_0
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Provides helper methods for loading and saving hot reload metadata manifests.
/// </summary>
public static class RuntimeHotReloadManifest
{
#if !NETSTANDARD2_0
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
#endif

    public static IReadOnlyDictionary<string, RuntimeHotReloadMetadata> Load(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static IReadOnlyDictionary<string, RuntimeHotReloadMetadata> Load(Stream stream)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

#if NETSTANDARD2_0
        throw new NotSupportedException("Manifest loading is not supported on netstandard2.0.");
#else
        using var reader = new StreamReader(stream, leaveOpen: true);
        var json = reader.ReadToEnd();
        var result = JsonSerializer.Deserialize<Dictionary<string, RuntimeHotReloadMetadata>>(json, s_jsonOptions);
        return result ?? new Dictionary<string, RuntimeHotReloadMetadata>();
#endif
    }

    public static void Save(string path, IReadOnlyDictionary<string, RuntimeHotReloadMetadata> manifest)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        using var stream = File.Create(path);
        Save(stream, manifest);
    }

    public static void Save(Stream stream, IReadOnlyDictionary<string, RuntimeHotReloadMetadata> manifest)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));

#if NETSTANDARD2_0
        throw new NotSupportedException("Manifest saving is not supported on netstandard2.0.");
#else
        using var writer = new StreamWriter(stream, leaveOpen: true);
        var json = JsonSerializer.Serialize(manifest, s_jsonOptions);
        writer.Write(json);
        writer.Flush();
#endif
    }
}
