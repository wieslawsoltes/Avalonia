using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.Templates;
using Avalonia.Styling;

namespace Avalonia.Controls
{
    /// <summary>
    /// An indexed dictionary of resources.
    /// </summary>
    public class ResourceDictionary : ResourceProvider, IResourceDictionary, IThemeVariantProvider
    {
        private object? _lastDeferredItemKey;
        private Dictionary<object, object?>? _inner;
        private AvaloniaList<IResourceProvider>? _mergedDictionaries;
        private AvaloniaDictionary<ThemeVariant, IThemeVariantProvider>? _themeDictionary;
        private ResourceLookupCache? _lookupCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceDictionary"/> class.
        /// </summary>
        public ResourceDictionary() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceDictionary"/> class.
        /// </summary>
        public ResourceDictionary(IResourceHost owner) : base(owner) { }

        public int Count => _inner?.Count ?? 0;

        public object? this[object key]
        {
            get { TryGetValue(key, out var value); return value; }
            set { Inner[key] = value; RaiseResourcesChanged(); }
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
                    // Register before owner propagation: owner callbacks may perform lookups.
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
                        (_, x) => { if (Owner is not null) x.AddOwner(Owner); },
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
                    foreach (var i in _mergedDictionaries)
                        if (i.HasResources) return true;
                return false;
            }
        }

        bool ICollection<KeyValuePair<object, object?>>.IsReadOnly => false;
        private Dictionary<object, object?> Inner => _inner ??= new();

        public void Add(object key, object? value) { Inner.Add(key, value); RaiseResourcesChanged(); }
        public void AddDeferred(object key, Func<IServiceProvider?, object?> factory) => Add(key, new DeferredItem(factory));
        public void AddDeferred(object key, IDeferredContent deferredContent) => Add(key, deferredContent);
        public void AddNotSharedDeferred(object key, IDeferredContent deferredContent) => Add(key, new NotSharedDeferredItem(deferredContent));

        public void SetItems(IEnumerable<KeyValuePair<object, object?>> values)
        {
            try
            {
                foreach (var value in values)
                {
                    Inner[value.Key] = value.Value;
                    // The iterator itself can reenter between successive assignments.
                    ResourceLookupCache.Invalidate();
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
            if (_inner?.Remove(key) == true) { RaiseResourcesChanged(); return true; }
            return false;
        }

        public sealed override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
        {
            // Local entries remain the cheapest path and always take precedence.
            if (TryGetValue(key, out value)) return true;
            var eligible = ResourceLookupCache.DeferredDepth == 0 && ResourceLookupCache.IsEligible(key);
            var epoch = ResourceLookupCache.Epoch;
            if (eligible && _lookupCache?.TryGet(key, theme, out var location) == true)
            {
                if (location is null) { value = null; return false; }
                if (location.TryGetValue(key, out value)) return true;
            }

            var result = TryGetResourceCore(key, theme, out value, out var foundIn, out var cacheable, localChecked: true);
            if (eligible && cacheable && (_themeDictionary?.Count > 0 || _mergedDictionaries?.Count > 0))
                (_lookupCache ??= new()).Add(key, theme, foundIn, epoch);
            return result;
        }

        private bool TryGetResourceCore(object key, ThemeVariant? theme, out object? value,
            out ResourceDictionary? foundIn, out bool cacheable, bool localChecked = false)
        {
            foundIn = null;
            cacheable = true;
            if (!localChecked && TryGetValue(key, out value)) { foundIn = this; return true; }
            if (_themeDictionary is not null)
            {
                if (theme is not null && theme != ThemeVariant.Default)
                {
                    if (_themeDictionary.TryGetValue(theme, out var provider) && Probe(provider, key, theme, out value, out foundIn, ref cacheable))
                        return true;
                    var inherited = theme.InheritVariant;
                    while (inherited is not null)
                    {
                        if (_themeDictionary.TryGetValue(inherited, out provider) && Probe(provider, key, theme, out value, out foundIn, ref cacheable))
                            return true;
                        inherited = inherited.InheritVariant;
                    }
                }
                if (_themeDictionary.TryGetValue(ThemeVariant.Default, out var fallback) && Probe(fallback, key, theme, out value, out foundIn, ref cacheable))
                    return true;
            }
            if (_mergedDictionaries is not null)
                for (var i = _mergedDictionaries.Count - 1; i >= 0; --i)
                    if (Probe(_mergedDictionaries[i], key, theme, out value, out foundIn, ref cacheable))
                        return true;
            value = null;
            return false;
        }

        private static bool Probe(IResourceProvider provider, object key, ThemeVariant? theme,
            out object? value, out ResourceDictionary? foundIn, ref bool cacheable)
        {
            // Custom providers (including interface reimplementations on subclasses) may have
            // dynamic lookup behavior. Never cache an answer which bypasses one of their probes.
            if (provider.GetType() == typeof(ResourceDictionary))
            {
                var found = ((ResourceDictionary)provider).TryGetResourceCore(key, theme, out value, out foundIn, out var childCacheable);
                cacheable &= childCacheable;
                return found;
            }
            foundIn = null;
            cacheable = false;
            return provider.TryGetResource(key, theme, out value);
        }

        public bool TryGetValue(object key, out object? value)
        {
            if (_inner is not null && _inner.TryGetValue(key, out value))
            {
                if (value is IDeferredContent deferred)
                {
                    if (_lastDeferredItemKey == key) { value = null; return false; }
                    try
                    {
                        _lastDeferredItemKey = key;
                        ++ResourceLookupCache.DeferredDepth;
                        value = deferred.Build(null) switch
                        {
                            ITemplateResult t => t.Result,
                            { } v => v,
                            _ => null,
                        };
                        if (deferred is not NotSharedDeferredItem)
                        {
                            _inner[key] = value;
                            ResourceLookupCache.Invalidate();
                        }
                    }
                    finally
                    {
                        --ResourceLookupCache.DeferredDepth;
                        _lastDeferredItemKey = null;
                    }
                }
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Ensures that the resource dictionary can hold up to <paramref name="capacity"/> entries without
        /// any further expansion of its backing storage.
        /// </summary>
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
            { RaiseResourcesChanged(); return true; }
            return false;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal bool ContainsDeferredKey(object key) =>
            _inner is not null && _inner.TryGetValue(key, out var result) && result is IDeferredContent;

        private new void RaiseResourcesChanged()
        {
            ResourceLookupCache.Invalidate();
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
