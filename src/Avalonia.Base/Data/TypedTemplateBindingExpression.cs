using System;
using System.Collections.Generic;
using Avalonia.Data.Core;
using Avalonia.PropertyStore;
using Avalonia.Utilities;

namespace Avalonia.Data;

internal interface ITypedTemplateBindingFactory
{
    BindingExpressionBase? CreateTypedTemplateBinding(AvaloniaProperty target, BindingMode mode);
}

/// <summary>
/// Same-type, converter-free styled template binding. The value store consumes IValueEntry<T>
/// directly, so neither source reads nor normal target publication box value types.
/// </summary>
internal sealed class TypedTemplateBindingExpression<T> : BindingExpressionBase, IValueEntry<T>, IDescription
{
    private readonly StyledProperty<T> _sourceProperty;
    private readonly StyledProperty<T> _targetProperty;
    private readonly BindingMode _mode;
    private WeakReference<StyledElement>? _target;
    private WeakReference<AvaloniaObject>? _parent;
    private IBindingExpressionSink? _sink;
    private ImmediateValueFrame? _frame;
    private Optional<T> _value;
    private T _defaultValue = default!;
    private bool _defaultInitialized;
    private bool _running;
    private bool _produceValue;
    private bool _hasPublishedValue;

    public TypedTemplateBindingExpression(StyledProperty<T> source, StyledProperty<T> target, BindingMode mode)
        : base(BindingPriority.Template)
    {
        _sourceProperty = source;
        _targetProperty = target;
        _mode = mode;
    }

    public string Description => $"{{TemplateBinding {_sourceProperty}}}";

    internal override void Attach(IBindingExpressionSink sink, ImmediateValueFrame? frame,
        AvaloniaObject target, AvaloniaProperty targetProperty, BindingPriority priority)
    {
        if (_sink is not null || targetProperty != _targetProperty || target is not StyledElement element)
            throw new InvalidOperationException("Invalid template binding attachment.");
        _sink = sink;
        _frame = frame;
        _target = new(element);
        TargetProperty = targetProperty;
        Priority = priority;
    }

    internal override void Start(bool produceValue)
    {
        if (_running)
            return;
        _running = true;
        _hasPublishedValue = false;
        try
        {
            _produceValue = produceValue;
            OnParentChanged();
            if (_target?.TryGetTarget(out var target) == true)
                target.PropertyChanged += OnTargetChanged;
        }
        finally { _produceValue = true; }
    }

    public override void Dispose()
    {
        if (_sink is null)
            return;
        Stop();
        var sink = _sink;
        var frame = _frame;
        _sink = null;
        _frame = null;
        sink.OnCompleted(this);
        frame?.OnEntryDisposed(this);
    }

    private protected override bool HasValue()
    {
        Start(false);
        // Like TemplateBindingExpression, an absent parent produces the target default,
        // not an absent priority entry which would expose a lower-priority style value.
        return true;
    }

    T IValueEntry<T>.GetValue() => GetTypedValue();
    private protected override object? GetUntypedValue() => GetTypedValue();

    private T GetTypedValue()
    {
        Start(false);
        if (_value.HasValue)
            return _value.Value;
        if (!_defaultInitialized)
        {
            if (_target?.TryGetTarget(out var target) != true)
                return default!;
            _defaultValue = _targetProperty.GetDefaultValue(target);
            _defaultInitialized = true;
        }
        return _defaultValue;
    }

    private protected override bool GetDataValidationState(out BindingValueType state, out Exception? error)
    {
        state = BindingValueType.Value;
        error = null;
        return false;
    }

    private protected override void Unsubscribe() => Stop();

    private void Stop()
    {
        if (!_running)
            return;
        if (_parent?.TryGetTarget(out var parent) == true)
            parent.PropertyChanged -= OnParentPropertyChanged;
        if (_target?.TryGetTarget(out var target) == true)
            target.PropertyChanged -= OnTargetChanged;
        _parent = null;
        _running = false;
        _value = default;
    }

    private void OnParentChanged()
    {
        if (_parent?.TryGetTarget(out var oldParent) == true)
            oldParent.PropertyChanged -= OnParentPropertyChanged;
        _parent = null;
        if (_target?.TryGetTarget(out var target) == true && target.TemplatedParent is { } parent)
        {
            _parent = new(parent);
            parent.PropertyChanged += OnParentPropertyChanged;
        }
        ReadSource();
    }

    private void ReadSource()
    {
        if (_parent?.TryGetTarget(out var parent) == true)
        {
            Publish(new Optional<T>(parent.GetValue(_sourceProperty)));
            _hasPublishedValue = true;
        }
        else if (_hasPublishedValue)
            Publish(default);
    }

    private void Publish(Optional<T> value)
    {
        if (!_running)
            return;
        var changed = value.HasValue != _value.HasValue || value.HasValue &&
            !(typeof(T).IsValueType ? EqualityComparer<T>.Default.Equals(value.Value, _value.Value) :
                ReferenceEquals(value.Value, _value.Value));
        if (changed)
            _value = value;
        // Styled-property access and notifications are UI-thread confined. Keep even unchanged
        // publications: the sink decides whether a current-value override needs reevaluation.
        if (_produceValue)
            _sink?.OnChanged(this, changed, false);
    }

    private void OnParentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == _sourceProperty)
            ReadSource();
    }

    private void OnTargetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == StyledElement.TemplatedParentProperty)
            OnParentChanged();
        else if (_mode == BindingMode.TwoWay && e.Property == _targetProperty &&
                 e is AvaloniaPropertyChangedEventArgs<T> typed &&
                 _parent?.TryGetTarget(out var parent) == true)
            parent.SetCurrentValue(_sourceProperty, typed.NewValue.Value);
    }
}
