using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Tracks resource dictionaries so their owners can be notified after hot reload updates.
/// </summary>
internal static class HotReloadResourceDictionaryRegistry
{
    private static readonly object s_gate = new();
    private static readonly List<WeakReference<ResourceDictionary>> s_dictionaries = new();

    public static void Register(ResourceDictionary dictionary)
    {
        if (dictionary is null)
            throw new ArgumentNullException(nameof(dictionary));

        lock (s_gate)
        {
            Prune_NoLock();
            s_dictionaries.Add(new WeakReference<ResourceDictionary>(dictionary));
        }
    }

    public static void NotifyReloaded(ResourceDictionary dictionary)
    {
        if (dictionary is null)
            return;

        dictionary.NotifyHotReload();
    }

    public static void NotifyTypeReloaded(Type type)
    {
        if (type is null)
            return;

        List<ResourceDictionary>? targets = null;

        lock (s_gate)
        {
            for (var i = s_dictionaries.Count - 1; i >= 0; --i)
            {
                if (!s_dictionaries[i].TryGetTarget(out var dictionary) || dictionary is null)
                {
                    s_dictionaries.RemoveAt(i);
                    continue;
                }

                if (type.IsInstanceOfType(dictionary))
                {
                    targets ??= new List<ResourceDictionary>();
                    targets.Add(dictionary);
                }
            }
        }

        if (targets is null)
            return;

        foreach (var dictionary in targets)
            dictionary.NotifyHotReload();
    }

    private static void Prune_NoLock()
    {
        for (var i = s_dictionaries.Count - 1; i >= 0; --i)
        {
            if (!s_dictionaries[i].TryGetTarget(out _))
                s_dictionaries.RemoveAt(i);
        }
    }
}
