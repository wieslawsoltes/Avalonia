using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Markup.Xaml.XamlIl.CompilerExtensions;
using XamlX.TypeSystem;

namespace Avalonia.Build.Tasks;

internal static class HotReloadManifestWriter
{
    internal sealed class HotReloadManifestEntry
    {
        public string TargetTypeName = string.Empty;
        public string BuilderTypeName = string.Empty;
        public string PopulateMethodName = string.Empty;
        public string? BuildMethodName;
        public string? BuildReturnTypeName;
        public string? SourcePath;
        public string? RelativeSourcePath;
    }

    public static HotReloadManifestEntry? CreateHotReloadEntry(
        XamlDocumentTypeBuilderProvider provider,
        string? sourcePath,
        string? projectDirectory)
    {
        var populateMethod = provider.PopulateMethod;
        if (populateMethod.Parameters.Count < 2)
            return null;

        var targetType = populateMethod.Parameters[1];
        if (targetType is null)
            return null;

        var entry = new HotReloadManifestEntry
        {
            TargetTypeName = GetTypeFullName(targetType),
            BuilderTypeName = GetTypeFullName(provider.PopulateDeclaringType),
            PopulateMethodName = populateMethod.Name,
            BuildMethodName = provider.BuildMethod?.Name,
            BuildReturnTypeName = provider.BuildMethod?.ReturnType is { } rt ? GetTypeFullName(rt) : null,
            SourcePath = sourcePath,
            RelativeSourcePath = CreateRelativeSourcePath(sourcePath, projectDirectory)
        };

        return entry;
    }

    public static void WriteHotReloadManifest(string outputAssembly, string? assemblyName, IReadOnlyList<HotReloadManifestEntry> entries)
    {
        if (string.IsNullOrEmpty(outputAssembly) || entries.Count == 0)
            return;

        assemblyName ??= Path.GetFileNameWithoutExtension(outputAssembly);
        var manifestPath = Path.ChangeExtension(outputAssembly, ".axaml.hotreload.json");
        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var map = new Dictionary<string, HotReloadManifestEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
            map[entry.TargetTypeName] = entry;

        var sb = new StringBuilder();
        sb.AppendLine("{");

        var index = 0;
        foreach (var kvp in map)
        {
            var entry = kvp.Value;
            sb.Append("  \"").Append(EscapeJson(kvp.Key)).Append("\": {");
            sb.Append("\"AssemblyName\": \"").Append(EscapeJson(assemblyName!)).Append("\",");
            sb.Append(" \"BuilderTypeName\": \"").Append(EscapeJson(entry.BuilderTypeName)).Append("\",");
            sb.Append(" \"PopulateMethodName\": \"").Append(EscapeJson(entry.PopulateMethodName)).Append("\",");
            sb.Append(" \"PopulateTargetTypeName\": \"").Append(EscapeJson(entry.TargetTypeName)).Append("\"");

            sb.Append(", \"BuildMethodName\": ");
            sb.Append(entry.BuildMethodName is null ? "null" : "\"" + EscapeJson(entry.BuildMethodName) + "\"");

            sb.Append(", \"BuildReturnTypeName\": ");
            sb.Append(entry.BuildReturnTypeName is null ? "null" : "\"" + EscapeJson(entry.BuildReturnTypeName) + "\"");

            var sourcePath = entry.SourcePath;
            if (!string.IsNullOrEmpty(sourcePath))
            {
                try
                {
                    sourcePath = Path.GetFullPath(sourcePath);
                }
                catch
                {
                }
            }

            sb.Append(", \"SourcePath\": ");
            sb.Append(string.IsNullOrEmpty(sourcePath) ? "null" : "\"" + EscapeJson(sourcePath) + "\"");

            var relativeSourcePath = entry.RelativeSourcePath;
            sb.Append(", \"RelativeSourcePath\": ");
            sb.Append(string.IsNullOrEmpty(relativeSourcePath) ? "null" : "\"" + EscapeJson(relativeSourcePath) + "\"");

            sb.Append(" }");
            if (++index < map.Count)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.Append('}');

        File.WriteAllText(manifestPath, sb.ToString());
    }

    private static string GetTypeFullName(IXamlType type)
    {
        if (!string.IsNullOrEmpty(type.FullName))
            return type.FullName;
        var ns = (type.Namespace ?? string.Empty).Trim();
        return string.IsNullOrEmpty(ns) ? type.Name : ns + "." + type.Name;
    }

    private static string EscapeJson(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private static string? CreateRelativeSourcePath(string? sourcePath, string? projectDirectory)
    {
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(projectDirectory))
            return null;

        try
        {
            var fullSource = Path.GetFullPath(sourcePath);
            var fullProject = Path.GetFullPath(projectDirectory);

            if (!fullProject.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
                !fullProject.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                fullProject += Path.DirectorySeparatorChar;
            }

            var projectUri = new Uri(fullProject, UriKind.Absolute);
            var sourceUri = new Uri(fullSource, UriKind.Absolute);

            if (!projectUri.IsBaseOf(sourceUri))
                return null;

            var relative = projectUri.MakeRelativeUri(sourceUri).ToString();
            if (string.IsNullOrEmpty(relative))
                return string.Empty;

            relative = Uri.UnescapeDataString(relative);
            relative = relative.Replace('/', Path.DirectorySeparatorChar);
            return relative;
        }
        catch
        {
            return null;
        }
    }
}
