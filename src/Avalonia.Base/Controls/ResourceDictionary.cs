using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Collections;
using Avalonia.Controls.Templates;
using Avalonia.Styling;

namespace Avalonia.Controls
{
    /// <summary>An indexed dictionary of resources.</summary>
    public class ResourceDictionary : ResourceProvider, IResourceDictionary, IThemeVariantProvider
    {
        private object? _lastDeferredItemKey;
        private Dictionary<object, object?>? _inner;
        private AvaloniaList<IResourceProvider>? _mergedDictionaries;
        private AvaloniaDictionary<ThemeVariant, IThemeVariantProvider>? _themeDictionary;
        private ResourceLookupCache? _lookupCache;
        private bool _isLookupDependency;
        private bool _hasUnstableKeys;

        public ResourceDictionary() { }
        public ResourceDictionary(IResourceHost owner) : base(owner) { }
        public int Count => _inner?.Count ?? 0;

        public object? this[object key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { TryGetValue(key, out var value); return value; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (!_isLookupDependency && !_hasUnstableKeys && key is string)
                {
                    // Only strings/runtime Types are stored here, and this string query
                    // cannot call user hashing/equality. No dependency can appear during
                    // the write. Owner callbacks still run, after the value is committed.
                    Inner[key] = value;
                    base.RaiseResourcesChanged();
                }
                else SetValueWithCallbacks(key, value);
            }
        }

        private void SetValueWithCallbacks(object key, object? value)
        {
            if (key is not string) TrackKey(key);
            if (_isLookupDependency) SetValueWithCachedLocations(key, value);
            else
            {
                Inner[key] = value;
                // Arbitrary hashing/equality can establish a dependency during insertion.
                RaiseResourcesChanged();
            }
        }

        private void SetValueWithCachedLocations(object key, object? value)
        {
            var epoch = ResourceLookupCache.Epoch;
            bool preservesLocation;
            var inserted = false;
#if NET6_0_OR_GREATER
            {
                ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(Inner, key, out var exists);
                preservesLocation = exists && entry is not IDeferredContent && value is not IDeferredContent;
                inserted = !exists && value is not IDeferredContent;
                entry = value;
            }
#else
            Inner[key] = value;
            preservesLocation = false;
#endif
            if (preservesLocation && epoch == ResourceLookupCache.Epoch) base.RaiseResourcesChanged();
            else if (inserted) RaiseResourcesChanged(key, ResourceLookupChange.Inserted);
            else RaiseResourcesChanged();
        }

        public ICollection<object> Keys => (ICollection<object>?)_inner?.Keys ?? Array.Empty<object>();
        public ICollection<object?> Values => (ICollection<object?>?)_inner?.Values ?? Array.Empty<object?>();

        public IList<IResourceProvider> MergedDictionaries
        {
            get
            {
                if (_mergedDictionaries == null)
                {
                    _mergedDictionaries = new AvaloniaList<IResourceProvider>();
                    _mergedDictionaries.ResetBehavior = ResetBehavior.Remove;
                    _mergedDictionaries.CollectionChanged += (_, _) => ResourceLookupCache.Invalidate();
                    _mergedDictionaries.ForEachItem(
                        x => { if (Owner is not null) x.AddOwner(Owner); },
                        x => { if (Owner is not null) x.RemoveOwner(Owner); },
                        () => throw new NotSupportedException("Dictionary reset not supported"));
                }
                return _mergedDictionaries;
            }
        }

        public IDictionary<ThemeVariant, IThemeVariantProvider> ThemeDictionaries
        {
            get
            {
                if (_themeDictionary == null)
                {
                    _themeDictionary = new AvaloniaDictionary<ThemeVariant, IThemeVariantProvider>(2);
                    _themeDictionary.CollectionChanged += (_, _) => ResourceLookupCache.Invalidate();
                    _themeDictionary.ForEachItem(
                        (variant, x) =>
                        {
                            // ThemeVariant forwards equality/hash to its arbitrary Key. Mark
                            // opaque keys before owner callbacks can initiate another lookup.
                            TrackKey(variant.Key);
                            if (Owner is not null) x.AddOwner(Owner);
                        },
                        (_, x) => { if (Owner is not null) x.RemoveOwner(Owner); },
                        () => throw new NotSupportedException("Dictionary reset not supported"));
                }
                return _themeDictionary;
            }
        }

        ThemeVariant? IThemeVariantProvider.Key { get; set; }
        public sealed override bool HasResources
        {
            get
            {
                if (_inner?.Count > 0) return true;
                if (_mergedDictionaries?.Count > 0)
                    foreach (var i in _mergedDictionaries) if (i.HasResources) return true;
                return false;
            }
        }
        bool ICollection<KeyValuePair<object, object?>>.IsReadOnly => false;
        private Dictionary<object, object?> Inner => _inner ??= new();

        public void Add(object key, object? value)
        {
            TrackKey(key);
            Inner.Add(key, value);
            RaiseResourcesChanged(key, value is IDeferredContent ? ResourceLookupChange.None : ResourceLookupChange.Inserted);
        }
        public void AddDeferred(object key, Func<IServiceProvider?, object?> factory) => Add(key, new DeferredItem(factory));
        public void AddDeferred(object key, IDeferredContent deferredContent) => Add(key, deferredContent);
        public void AddNotSharedDeferred(object key, IDeferredContent deferredContent) => Add(key, new NotSharedDeferredItem(deferredContent));
        public void SetItems(IEnumerable<KeyValuePair<object, object?>> values)
        {
            try
            {
                foreach (var value in values)
                {
                    TrackKey(value.Key);
                    Inner[value.Key] = value.Value;
                    InvalidateLookupCache();
                }
            }
            finally { RaiseResourcesChanged(); }
        }
        public void Clear()
        {
            if (_inner?.Count > 0) { _inner.Clear(); RaiseResourcesChanged(); }
        }
        public bool ContainsKey(object key) => _inner?.ContainsKey(key) ?? false;
        public bool Remove(object key)
        {
            if (_inner?.Remove(key) == true)
            {
                RaiseResourcesChanged(key, ResourceLookupChange.Removed);
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sealed override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
        {
            if (TryGetValue(key, out value)) return true;
            // Inspect children after the local lookup: comparer/factory callbacks may have
            // added them. A standalone leaf has no resolution location of its own to cache.
            // Keep local hits outside the cache resolver's larger frame and TLS accesses.
            if (_themeDictionary is null && _mergedDictionaries is null) return false;
            return TryGetNonLocalResource(key, theme, out value);
        }

        private bool TryGetNonLocalResource(object key, ThemeVariant? theme, out object? value)
        {
            var eligible = !_hasUnstableKeys && ResourceLookupCache.DeferredDepth == 0 &&
                ResourceLookupCache.IsEligible(key) && ResourceLookupCache.IsStableTheme(theme);
            var epoch = ResourceLookupCache.Epoch;
            if (eligible && _lookupCache?.TryGet(key, theme, out var location) == true)
            {
                if (location is null) { value = null; return false; }
                if (location.TryGetValue(key, out value)) return true;
            }
            var cacheable = true;
            var result = TryGetResourceCore(key, theme, out value, out var foundIn, ref cacheable, localChecked: true);
            if (eligible && cacheable && (_themeDictionary?.Count > 0 || _mergedDictionaries?.Count > 0))
                (_lookupCache ??= new()).Add(key, theme, foundIn, epoch);
            return result;
        }

        private bool TryGetResourceCore(object key, ThemeVariant? theme, out object? value,
            out ResourceDictionary? foundIn, ref bool cacheable, bool localChecked = false)
        {
            var dictionary = this;
            while (true)
            {
                dictionary.MarkResourceLookupDependency();
                cacheable &= !dictionary._hasUnstableKeys;
                foundIn = null;
                if (!localChecked && dictionary.TryGetValue(key, out value)) { foundIn = dictionary; return true; }
                if (dictionary._themeDictionary is not null)
                {
                    if (theme is not null && theme != ThemeVariant.Default)
                    {
                        if (dictionary._themeDictionary.TryGetValue(theme, out var provider) &&
                            Probe(provider, key, theme, out value, out foundIn, ref cacheable)) return true;
                        var inherited = theme.InheritVariant;
                        while (inherited is not null)
                        {
                            if (dictionary._themeDictionary.TryGetValue(inherited, out provider) &&
                                Probe(provider, key, theme, out value, out foundIn, ref cacheable)) return true;
                            inherited = inherited.InheritVariant;
                        }
                    }
                    if (dictionary._themeDictionary.TryGetValue(ThemeVariant.Default, out var fallback) &&
                        Probe(fallback, key, theme, out value, out foundIn, ref cacheable)) return true;
                }
                if (dictionary._mergedDictionaries is not null)
                {
                    for (var i = dictionary._mergedDictionaries.Count - 1; i >= 0; --i)
                    {
                        var provider = dictionary._mergedDictionaries[i];
                        if (i == 0 && provider.GetType() == typeof(ResourceDictionary))
                        {
                            dictionary = (ResourceDictionary)provider;
                            localChecked = false;
                            goto NextDictionary;
                        }
                        if (Probe(provider, key, theme, out value, out foundIn, ref cacheable)) return true;
                    }
                }
                value = null;
                foundIn = null;
                return false;
            NextDictionary:;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Probe(IResourceProvider provider, object key, ThemeVariant? theme,
            out object? value, out ResourceDictionary? foundIn, ref bool cacheable)
        {
            if (provider.GetType() == typeof(ResourceDictionary))
            {
                var dictionary = (ResourceDictionary)provider;
                dictionary.MarkResourceLookupDependency();
                cacheable &= !dictionary._hasUnstableKeys;
                if (dictionary.TryGetValue(key, out value)) { foundIn = dictionary; return true; }
                // Comparers can add children while reporting a local miss.
                if (dictionary._themeDictionary is not null || dictionary._mergedDictionaries is not null)
                    return dictionary.TryGetResourceCore(key, theme, out value, out foundIn, ref cacheable, localChecked: true);
                foundIn = null;
                return false;
            }
            foundIn = null;
            cacheable = false;
            return provider.TryGetResource(key, theme, out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(object key, out object? value)
        {
            if (_inner is not null && _inner.TryGetValue(key, out value))
                return value is not IDeferredContent deferred || BuildDeferredValue(key, deferred, out value);
            value = null;
            return false;
        }
        private bool BuildDeferredValue(object key, IDeferredContent deferred, out object? value)
        {
            if (_lastDeferredItemKey == key) { value = null; return false; }
            try
            {
                _lastDeferredItemKey = key;
                ++ResourceLookupCache.DeferredDepth;
                value = deferred.Build(null) switch { ITemplateResult t => t.Result, { } v => v, _ => null };
                if (deferred is not NotSharedDeferredItem)
                {
                    TrackKey(key);
                    _inner![key] = value;
                    InvalidateLookupCache();
                }
            }
            finally { --ResourceLookupCache.DeferredDepth; _lastDeferredItemKey = null; }
            return true;
        }

        /// <summary>Ensures capacity for entries without further backing-storage expansion.</summary>
        /// <remarks>This method may have no effect when targeting .NET Standard 2.0.</remarks>
        public void EnsureCapacity(int capacity)
        {
            if (_inner is null) { _inner = new(capacity); return; }
#if !NETSTANDARD2_0
            Inner.EnsureCapacity(capacity);
#endif
        }
        public IEnumerator<KeyValuePair<object, object?>> GetEnumerator() =>
            _inner?.GetEnumerator() ?? Enumerable.Empty<KeyValuePair<object, object?>>().GetEnumerator();
        void ICollection<KeyValuePair<object, object?>>.Add(KeyValuePair<object, object?> item) => Add(item.Key, item.Value);
        bool ICollection<KeyValuePair<object, object?>>.Contains(KeyValuePair<object, object?> item) =>
            (_inner as ICollection<KeyValuePair<object, object?>>)?.Contains(item) ?? false;
        void ICollection<KeyValuePair<object, object?>>.CopyTo(KeyValuePair<object, object?>[] array, int arrayIndex) =>
            (_inner as ICollection<KeyValuePair<object, object?>>)?.CopyTo(array, arrayIndex);
        bool ICollection<KeyValuePair<object, object?>>.Remove(KeyValuePair<object, object?> item)
        {
            if ((_inner as ICollection<KeyValuePair<object, object?>>)?.Remove(item) == true)
            { RaiseResourcesChanged(item.Key, ResourceLookupChange.Removed); return true; }
            return false;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        internal bool ContainsDeferredKey(object key) =>
            _inner is not null && _inner.TryGetValue(key, out var result) && result is IDeferredContent;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkResourceLookupDependency()
        {
            if (!_isLookupDependency) _isLookupDependency = true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrackKey(object key)
        {
            if (!_hasUnstableKeys && !ResourceLookupCache.IsStableStoredKey(key))
            {
                // Custom resource or theme-key equality can change answers or run callbacks
                // without a resource mutation. Such a dictionary must be probed live.
                _hasUnstableKeys = true;
                InvalidateLookupCache();
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvalidateLookupCache()
        {
            if (_isLookupDependency) ResourceLookupCache.Invalidate();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private new void RaiseResourcesChanged()
        {
            InvalidateLookupCache();
            base.RaiseResourcesChanged();
        }
        private void RaiseResourcesChanged(object key, ResourceLookupChange change)
        {
            if (_isLookupDependency)
            {
                if (change != ResourceLookupChange.None && key is string { Length: <= 256 } text && !_hasUnstableKeys)
                    ResourceLookupCache.Invalidate(this, text, change);
                else ResourceLookupCache.Invalidate();
            }
            base.RaiseResourcesChanged();
        }
        protected sealed override void OnAddOwner(IResourceHost owner)
        {
            var hasResources = _inner?.Count > 0;
            if (_mergedDictionaries is not null)
                foreach (var i in _mergedDictionaries) { i.AddOwner(owner); hasResources |= i.HasResources; }
            if (_themeDictionary is not null)
                foreach (var i in _themeDictionary.Values) { i.AddOwner(owner); hasResources |= i.HasResources; }
            if (hasResources) owner.NotifyHostedResourcesChanged(ResourcesChangedEventArgs.Create());
        }
        protected sealed override void OnRemoveOwner(IResourceHost owner)
        {
            var hasResources = _inner?.Count > 0;
            if (_mergedDictionaries is not null)
                foreach (var i in _mergedDictionaries) { i.RemoveOwner(owner); hasResources |= i.HasResources; }
            if (_themeDictionary is not null)
                foreach (var i in _themeDictionary.Values) { i.RemoveOwner(owner); hasResources |= i.HasResources; }
            if (hasResources) owner.NotifyHostedResourcesChanged(ResourcesChangedEventArgs.Create());
        }
        private sealed class DeferredItem : IDeferredContent
        {
            private readonly Func<IServiceProvider?, object?> _factory;
            public DeferredItem(Func<IServiceProvider?, object?> factory) => _factory = factory;
            public object? Build(IServiceProvider? serviceProvider) => _factory(serviceProvider);
        }
        private sealed class NotSharedDeferredItem(IDeferredContent deferredContent) : IDeferredContent
        {
            private readonly IDeferredContent _deferredContent = deferredContent;
            public object? Build(IServiceProvider? serviceProvider) => _deferredContent.Build(serviceProvider);
        }
    }
}
