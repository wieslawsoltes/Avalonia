using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Tracks <see cref="IHotReloadableView"/> instances and coordinates transfer hooks during runtime reloads.
/// </summary>
internal static class HotReloadViewRegistry
{
    private static readonly object s_gate = new();
    private static readonly List<WeakReference<IHotReloadableView>> s_views = new();
    private static readonly Dictionary<Type, int> s_replacementCounts = new();

    public static void Register(IHotReloadableView view)
    {
        if (view is null)
            throw new ArgumentNullException(nameof(view));

        view.ReloadHandler ??= DefaultHotReloadViewHandler.Instance;

        lock (s_gate)
        {
            PruneDead_NoLock();

            if (!Contains_NoLock(view))
                s_views.Add(new WeakReference<IHotReloadableView>(view));
        }
    }

    public static void Unregister(IHotReloadableView view)
    {
        if (view is null)
            throw new ArgumentNullException(nameof(view));

        lock (s_gate)
        {
            for (var i = s_views.Count - 1; i >= 0; --i)
            {
                if (s_views[i].TryGetTarget(out var target) && ReferenceEquals(target, view))
                {
                    s_views.RemoveAt(i);
                }
                else if (target is null)
                {
                    s_views.RemoveAt(i);
                }
            }
        }
    }

    internal static IReadOnlyList<IHotReloadableView> GetLiveViewsSnapshot()
    {
        lock (s_gate)
        {
            if (s_views.Count == 0)
                return Array.Empty<IHotReloadableView>();

            var alive = new List<IHotReloadableView>(s_views.Count);
            for (var i = s_views.Count - 1; i >= 0; --i)
            {
                if (s_views[i].TryGetTarget(out var target) && target is not null)
                    alive.Add(target);
                else
                    s_views.RemoveAt(i);
            }

            return alive.Count == 0
                ? Array.Empty<IHotReloadableView>()
                : alive.ToArray();
        }
    }

    internal static void NotifyReloading(IHotReloadableView view)
    {
        if (view is null)
            return;

        try
        {
            view.ReloadHandler?.OnBeforeReload(view);
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Hot reload handler threw before reload for '{0}'.",
                ex,
                view.GetType().FullName ?? view.ToString());
        }
    }

    internal static void NotifyReloaded(IHotReloadableView view)
    {
        if (view is null)
            return;

        try
        {
            view.Reload();
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Hot reload view '{0}' failed during Reload().",
                ex,
                view.GetType().FullName ?? view.ToString());
        }

        try
        {
            view.ReloadHandler?.OnAfterReload(view);
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Hot reload handler threw after reload for '{0}'.",
                ex,
                view.GetType().FullName ?? view.ToString());
        }
    }

    public static Control ReplaceView(IHotReloadableView view, Func<Control> replacementFactory)
    {
        if (view is null)
            throw new ArgumentNullException(nameof(view));
        if (replacementFactory is null)
            throw new ArgumentNullException(nameof(replacementFactory));

        NotifyReloading(view);

        Control replacement;
        try
        {
            replacement = replacementFactory();
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Hot reload failed to construct a replacement view for '{0}'.",
                ex,
                view.GetType().FullName ?? view.ToString());
            throw;
        }

        try
        {
            view.TransferState(replacement);
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Hot reload failed to transfer state from '{0}' to '{1}'.",
                ex,
                view.GetType().FullName ?? view.ToString(),
                replacement.GetType().FullName ?? replacement.ToString());
        }

        if (replacement is IHotReloadableView newView)
        {
            if (newView.ReloadHandler is null)
                newView.ReloadHandler = view.ReloadHandler;

            Register(newView);
            RegisterReplacement(view.GetType(), newView.GetType());
            NotifyReloaded(newView);
        }
        else
        {
            NotifyReloaded(view);
        }

        Unregister(view);
        return replacement;
    }

    internal static IReadOnlyList<IHotReloadableView> GetActiveViews() => GetLiveViewsSnapshot();

    internal static IReadOnlyDictionary<string, int> GetReplacementStats()
    {
        lock (s_gate)
        {
            if (s_replacementCounts.Count == 0)
                return new Dictionary<string, int>();

            var result = new Dictionary<string, int>(s_replacementCounts.Count, StringComparer.Ordinal);
            foreach (var pair in s_replacementCounts)
                result[pair.Key.FullName ?? pair.Key.Name] = pair.Value;
            return result;
        }
    }

    private static void PruneDead_NoLock()
    {
        for (var i = s_views.Count - 1; i >= 0; --i)
        {
            if (!s_views[i].TryGetTarget(out var target) || target is null)
                s_views.RemoveAt(i);
        }
    }

    private static bool Contains_NoLock(IHotReloadableView view)
    {
        for (var i = s_views.Count - 1; i >= 0; --i)
        {
            if (s_views[i].TryGetTarget(out var target) && ReferenceEquals(target, view))
                return true;
        }

        return false;
    }

    private static void RegisterReplacement(Type originalType, Type replacementType)
    {
        lock (s_gate)
        {
            if (!s_replacementCounts.TryGetValue(originalType, out var count))
                count = 0;
            s_replacementCounts[originalType] = count + 1;
        }
    }
}
