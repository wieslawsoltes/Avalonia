using System;
using System.Globalization;
using Avalonia.Logging;

namespace Avalonia.Markup.Xaml.HotReload;

internal static class HotReloadDiagnostics
{
    private const string EnvironmentVariable = "AVALONIA_HOTRELOAD_DIAGNOSTICS";
    private const string AppContextSwitch = "AvaloniaHotReloadDiagnostics";
    private static readonly Lazy<bool> s_isEnabled = new(EvaluateEnabled);

    public static bool IsEnabled => s_isEnabled.Value;

    private static bool EvaluateEnabled()
    {
        if (AppContext.TryGetSwitch(AppContextSwitch, out var switchValue) && switchValue)
            return true;

        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(env))
            return false;

        if (bool.TryParse(env, out var boolValue))
            return boolValue;

        if (int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return intValue != 0;

        return string.Equals(env, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string Format(string message, object?[] args) =>
        args is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, message, args)
            : message;

    public static void ReportInfo(string message, params object?[] args)
    {
        if (!IsEnabled)
            return;

        var logger = Logger.TryGet(LogEventLevel.Information, LogArea.HotReload);
        if (!logger.HasValue)
            return;

        logger.Value.Log(null, Format(message, args));
    }

    public static void ReportWarning(string message, params object?[] args)
    {
        if (!IsEnabled)
            return;

        var logger = Logger.TryGet(LogEventLevel.Warning, LogArea.HotReload);
        if (!logger.HasValue)
            return;

        logger.Value.Log(null, Format(message, args));
    }

    public static void ReportError(string message, Exception exception, params object?[] args)
    {
        if (!IsEnabled)
            return;

        var logger = Logger.TryGet(LogEventLevel.Error, LogArea.HotReload);
        if (!logger.HasValue)
            return;

        var formatted = Format(message, args);
        logger.Value.Log(null, "{0}: {1}", formatted, exception);
    }
}
