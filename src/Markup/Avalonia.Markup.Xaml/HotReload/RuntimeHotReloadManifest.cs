using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Provides helper methods for loading and saving hot reload metadata manifests.
/// </summary>
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Hot reload manifest JSON parsing runs in development scenarios only.")]
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Hot reload manifest JSON parsing runs in development scenarios only.")]
public static class RuntimeHotReloadManifest
{
#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
#endif

    [RequiresUnreferencedCode("Runtime hot reload manifest loading uses System.Text.Json which requires dynamic access to metadata.")]
    public static IReadOnlyDictionary<string, RuntimeHotReloadMetadata> Load(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    [RequiresUnreferencedCode("Runtime hot reload manifest loading uses System.Text.Json which requires dynamic access to metadata.")]
    public static IReadOnlyDictionary<string, RuntimeHotReloadMetadata> Load(Stream stream)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

#if NETSTANDARD2_0 && !NET6_0_OR_GREATER
        throw new NotSupportedException("Manifest loading is not supported on netstandard2.0.");
#else
        using var reader = new StreamReader(stream, leaveOpen: true);
        var json = reader.ReadToEnd();
        var result = JsonSerializer.Deserialize<Dictionary<string, RuntimeHotReloadMetadata>>(json, s_jsonOptions);
        return result ?? new Dictionary<string, RuntimeHotReloadMetadata>();
#endif
    }

    [RequiresUnreferencedCode("Runtime hot reload manifest saving uses System.Text.Json which requires dynamic access to metadata.")]
    public static void Save(string path, IReadOnlyDictionary<string, RuntimeHotReloadMetadata> manifest)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        using var stream = File.Create(path);
        Save(stream, manifest);
    }

    [RequiresUnreferencedCode("Runtime hot reload manifest saving uses System.Text.Json which requires dynamic access to metadata.")]
    public static void Save(Stream stream, IReadOnlyDictionary<string, RuntimeHotReloadMetadata> manifest)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));

#if NETSTANDARD2_0 && !NET6_0_OR_GREATER
        throw new NotSupportedException("Manifest saving is not supported on netstandard2.0.");
#else
        using var writer = new StreamWriter(stream, leaveOpen: true);
        var json = JsonSerializer.Serialize(manifest, s_jsonOptions);
        writer.Write(json);
        writer.Flush();
#endif
    }
}
