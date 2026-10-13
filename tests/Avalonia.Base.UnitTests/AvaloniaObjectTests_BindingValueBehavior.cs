using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.PropertyStore;
using Avalonia.Styling;
using Avalonia.Styling.Activators;
using Avalonia.Threading;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class AvaloniaObjectTests_BindingValueBehavior
{
    private const BindingValueBehavior Compatibility =
        BindingValueBehavior.ReplaceNonWritableBindingOnSetValue |
        BindingValueBehavior.RemoveBindingOnClearValue |
        BindingValueBehavior.ResetCurrentValueOnSourceChange;

    public static IEnumerable<object[]> AssignmentCases()
    {
        foreach (var direct in new[] { false, true })
        foreach (var mode in new[] { BindingMode.Default, BindingMode.OneWay, BindingMode.OneTime,
                     BindingMode.TwoWay, BindingMode.OneWayToSource })
        foreach (var path in new[] { 0, 1, 2 })
        foreach (var equal in new[] { false, true })
            yield return new object[] { direct, mode, path, equal };
    }

    [Theory]
    [MemberData(nameof(AssignmentCases))]
    public void Explicit_Assignment_Replaces_Only_NonWritable_Local_Binding(
        bool direct, BindingMode mode, int path, bool equal)
    {
        var target = new OptInTarget();
        var source = new Source { Value = "initial" };
        var property = Property(direct);
        var expression = Bind(target, source, property, mode);
        var previous = target.GetValue(property);
        var sourceBeforeAssignment = source.Value;
        var value = equal ? (string?)previous : "assigned";
        var writable = mode is BindingMode.TwoWay or BindingMode.OneWayToSource;
        var changes = new List<(object? Old, object? New, bool Bound)>();
        target.PropertyChanged += (_, e) =>
        {
            if (e.Property == property)
                changes.Add((e.OldValue, e.NewValue, Expression(target, property) is not null));
        };

        Assign(target, direct, path, value);

        Assert.Equal(value, target.GetValue(property));
        Assert.Equal(writable, Expression(target, property) is not null);
        if (equal)
            Assert.Empty(changes);
        else
            Assert.Equal(new[] { (previous, (object?)value, writable) }, changes);
        Assert.Equal(writable && !equal ? value : sourceBeforeAssignment, source.Value);
        source.Value = "later";
        Assert.Equal(mode == BindingMode.TwoWay ? "later" : value, target.GetValue(property));
        Assert.Equal(mode == BindingMode.Default ? BindingMode.OneWay : mode, expression.Mode);
    }

    public static IEnumerable<object[]> ClearCases()
    {
        foreach (var direct in new[] { false, true })
        foreach (var mode in new[] { BindingMode.OneWay, BindingMode.TwoWay, BindingMode.OneWayToSource })
        foreach (var path in new[] { 0, 1, 2, 3, 4 })
            yield return new object[] { direct, mode, path };
    }

    [Theory]
    [MemberData(nameof(ClearCases))]
    public void Clear_Detaches_Before_The_Single_Reset_Notification(bool direct, BindingMode mode, int path)
    {
        var target = new OptInTarget();
        var source = new Source { Value = "initial" };
        var property = Property(direct);
        Bind(target, source, property, mode);
        target.SetCurrentValue(property, "current");
        var sourceBeforeClear = source.Value;
        var changes = new List<(object? Old, object? New)>();
        target.PropertyChanged += (_, e) =>
        {
            if (e.Property == property)
            {
                Assert.Null(Expression(target, property));
                Assert.Equal(sourceBeforeClear, source.Value);
                changes.Add((e.OldValue, e.NewValue));
            }
        };

        Clear(target, direct, path);

        Assert.Equal(new[] { ((object?)"current", (object?)"unset") }, changes);
        Assert.Equal("unset", target.GetValue(property));
        source.Value = "later";
        Assert.Equal("unset", target.GetValue(property));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Current_Value_And_Binding_Publications_Preserve_OneWay_Binding(bool direct)
    {
        var target = new OptInTarget();
        var source = new Source { Value = "initial" };
        var property = Property(direct);
        var expression = Bind(target, source, property);

        source.Value = "second";
        Assert.Equal("second", target.GetValue(property));
        target.SetCurrentValue(property, "current");
        Assert.Same(expression, Expression(target, property));
        Assert.Equal("second", source.Value);
        source.Value = "third";
        Assert.Equal("third", target.GetValue(property));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoNothing_Does_Not_Retire_Binding(bool direct)
    {
        var target = new OptInTarget();
        var source = new Source { Value = "initial" };
        var property = Property(direct);
        var expression = Bind(target, source, property);
        target.SetValue(property, BindingOperations.DoNothing);
        target.SetCurrentValue(property, BindingOperations.DoNothing);
        Assert.Same(expression, Expression(target, property));
        source.Value = "later";
        Assert.Equal("later", target.GetValue(property));
    }

    [Fact]
    public void Invalid_Explicit_Value_Does_Not_Retire_Binding()
    {
        var target = new OptInTarget();
        var source = new Source { Value = "initial" };
        var expression = Bind(target, source, Target.ValueProperty);
        Assert.Throws<ArgumentException>(() => target.Value = "invalid");
        Assert.Same(expression, Expression(target, Target.ValueProperty));
        source.Value = "later";
        Assert.Equal("later", target.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Observable_Binding_Detaches_And_Clears_Validation(bool direct, bool clear)
    {
        var target = new OptInTarget();
        var source = new BehaviorSubject<BindingValue<string?>>("initial");
        var property = Property(direct);
        if (direct)
            target.Bind(Target.DirectProperty, source);
        else
            target.Bind(Target.ValueProperty, source);
        source.OnNext(BindingValue<string?>.DataValidationError(new Exception("error")));
        Assert.NotNull(target.Error);

        if (clear)
            Clear(target, direct, 1);
        else
            Assign(target, direct, 1, "assigned");

        Assert.False(source.HasObservers);
        Assert.Null(target.Error);
        source.OnNext("later");
        Assert.Equal(clear ? "unset" : "assigned", target.GetValue(property));
    }

    [Fact]
    public void Custom_And_Observable_Expressions_Default_To_OneWay()
    {
        var custom = new CustomExpression();
        Assert.Equal(BindingMode.OneWay, custom.Mode);
        var target = new OptInTarget();
        target.GetValueStore().AddBinding(Target.ValueProperty, custom);
        target.Value = "assigned";
        Assert.True(custom.Stopped);
        Assert.Null(Expression(target, Target.ValueProperty));

        var subject = new BehaviorSubject<object?>("observable");
        var observable = target.Bind(Target.ValueProperty, subject.ToBinding());
        Assert.Equal(BindingMode.OneWay, observable.Mode);
        target.Value = "assigned";
        Assert.False(subject.HasObservers);
        Assert.Null(Expression(target, Target.ValueProperty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void None_Preserves_Previous_Assignment_And_Clear_Behavior(bool direct)
    {
        var target = new Target();
        var source = new Source { Value = "initial" };
        var property = Property(direct);
        var expression = Bind(target, source, property);
        Assign(target, direct, 1, "assigned");
        Assert.Same(expression, Expression(target, property));
        source.Value = "second";
        Assert.Equal("second", target.GetValue(property));
        Clear(target, direct, 0);
        Assert.Same(expression, Expression(target, property));
        source.Value = "third";
        Assert.Equal("third", target.GetValue(property));
    }

    [Fact]
    public void Metadata_Inherits_And_Can_Explicitly_Reset_Options()
    {
        var enabled = new OptInTarget();
        var inherited = new InheritedTarget();
        var disabled = new DisabledTarget();
        foreach (var property in new[] { Property(false), Property(true) })
        {
            Assert.Equal(Compatibility, property.GetMetadata(enabled).BindingValueBehavior);
            Assert.Equal(Compatibility, property.GetMetadata(inherited).BindingValueBehavior);
            Assert.Equal(BindingValueBehavior.None, property.GetMetadata(disabled).BindingValueBehavior);
            var copy = property.GetMetadata(inherited).GenerateTypeSafeMetadata();
            Assert.Equal(Compatibility, copy.BindingValueBehavior);
            Assert.True(copy.IsReadOnly);
        }

        Assert.Equal(Compatibility, new StyledPropertyMetadata<string?> { BindingValueBehavior = Compatibility }
            .GenerateTypeSafeMetadata().BindingValueBehavior);
        Assert.Equal(Compatibility, new DirectPropertyMetadata<string?> { BindingValueBehavior = Compatibility }
            .GenerateTypeSafeMetadata().BindingValueBehavior);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Default_Mode_Resolved_From_Metadata_Preserves_Writable_Binding(bool direct)
    {
        var target = new DefaultTwoWayTarget();
        var source = new Source { Value = "initial" };
        var property = Property(direct);
        var expression = Bind(target, source, property, BindingMode.Default);
        Assert.Equal(BindingMode.TwoWay, expression.Mode);
        Assign(target, direct, 0, "assigned");
        Assert.Same(expression, Expression(target, property));
        Assert.Equal("assigned", source.Value);
    }

    [Theory]
    [InlineData(BindingPriority.Style)]
    [InlineData(BindingPriority.Template)]
    [InlineData(BindingPriority.StyleTrigger)]
    public void Disposing_Immediate_Source_Releases_Current_Override(BindingPriority priority)
    {
        var target = new OptInTarget();
        var source = target.SetValue(Target.ValueProperty, "source", priority);
        target.SetCurrentValue(Target.ValueProperty, "current");
        source!.Dispose();
        Assert.Equal("unset", target.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Retired_Observable_Cannot_Overwrite_Explicit_Value_With_Queued_Publication(bool direct, bool expression)
    {
        using var app = UnitTestApplication.Start();
        var target = new OptInTarget();
        var subject = new Subject<string?>();
        var property = Property(direct);
        if (expression)
            target.Bind(property, subject.ToBinding());
        else if (direct)
            target.Bind(Target.DirectProperty, subject);
        else
            target.Bind(Target.ValueProperty, subject);
        ThreadRunHelper.RunOnDedicatedThreadAndWait(() => subject.OnNext("queued"));
        Assign(target, direct, 0, "assigned");
        Dispatcher.UIThread.RunJobs(null, TestContext.Current.CancellationToken);
        Assert.Equal("assigned", target.GetValue(property));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_Style_Source_Releases_Current_Value(bool binding)
    {
        var target = new OptInTarget();
        var source = new BehaviorSubject<string?>("styled");
        var frame = Style(target, binding ? source.ToBinding() : "styled");
        target.SetCurrentValue(Target.ValueProperty, "current");
        target.GetValueStore().RemoveFrame(frame);
        Assert.Equal("unset", target.Value);
        Assert.False(source.HasObservers);
        source.OnNext("later");
        Assert.Equal("unset", target.Value);
    }

    [Fact]
    public void Deactivating_And_Reactivating_Style_Resets_All_Current_Overrides()
    {
        var target = new OptInTarget();
        var activator = new Activator();
        var subject = new BehaviorSubject<string?>("styled");
        Style(target, subject.ToBinding(), activator, new Setter(Target.OtherProperty, "other-style"));
        target.SetCurrentValue(Target.ValueProperty, "current");
        target.SetCurrentValue(Target.OtherProperty, "other-current");

        activator.Set(false);

        Assert.Equal("unset", target.Value);
        Assert.Equal("other-unset", target.GetValue(Target.OtherProperty));
        Assert.False(subject.HasObservers);
        subject.OnNext("later");
        activator.Set(true);
        Assert.Equal("later", target.Value);
        Assert.Equal("other-style", target.GetValue(Target.OtherProperty));
    }

    [Fact]
    public void Unrelated_Style_Removal_Preserves_Current_Override()
    {
        var target = new OptInTarget();
        var origin = Style(target, "styled");
        target.SetCurrentValue(Target.ValueProperty, "current");
        var otherStyle = new Style { Setters = { new Setter(Target.OtherProperty, "other") } };
        var other = AddStyle(target, otherStyle, null);
        target.GetValueStore().RemoveFrame(other);
        Assert.Equal("current", target.Value);
        target.GetValueStore().RemoveFrame(origin);
        Assert.Equal("unset", target.Value);
    }

    [Fact]
    public void Local_Current_Value_Does_Not_Inherit_An_Old_Style_Source_Lifetime()
    {
        var target = new OptInTarget();
        var frame = Style(target, "styled");
        target.Value = "local";
        target.SetCurrentValue(Target.ValueProperty, "current");
        target.GetValueStore().RemoveFrame(frame);
        Assert.Equal("current", target.Value);
    }

    [Fact]
    public void None_Retains_Current_Value_After_Style_Removal()
    {
        var target = new Target();
        var frame = Style(target, "styled");
        target.SetCurrentValue(Target.ValueProperty, "current");
        target.GetValueStore().RemoveFrame(frame);
        Assert.Equal("current", target.Value);
    }

    private static BindingExpressionBase Bind(Target target, Source source, AvaloniaProperty property,
        BindingMode mode = BindingMode.OneWay) => target.Bind(property,
        CompiledBinding.Create<Source, string?>(x => x.Value, source: source, mode: mode));

    private static BindingExpressionBase? Expression(Target target, AvaloniaProperty property) =>
        BindingOperations.GetBindingExpressionBase(target, property);

    private static AvaloniaProperty Property(bool direct) => direct ? Target.DirectProperty : Target.ValueProperty;

    private static void Assign(Target target, bool direct, int path, string? value)
    {
        switch (path)
        {
            case 0:
                if (direct) target.Direct = value;
                else target.Value = value;
                break;
            case 1: ((AvaloniaObject)target).SetValue(Property(direct), value); break;
            case 2: target[Property(direct)] = value; break;
        }
    }

    private static void Clear(Target target, bool direct, int path)
    {
        switch (path)
        {
            case 0:
                if (direct) target.ClearValue(Target.DirectProperty);
                else target.ClearValue(Target.ValueProperty);
                break;
            case 1: ((AvaloniaObject)target).ClearValue(Property(direct)); break;
            case 2: target.SetValue(Property(direct), AvaloniaProperty.UnsetValue); break;
            case 3: target[Property(direct)] = AvaloniaProperty.UnsetValue; break;
            case 4: target.SetCurrentValue(Property(direct), AvaloniaProperty.UnsetValue); break;
        }
    }

    private static StyleInstance Style(Target target, object? value, Activator? activator = null,
        Setter? other = null)
    {
        var style = new Style { Setters = { new Setter(Target.ValueProperty, value) } };
        if (other is not null)
            style.Setters.Add(other);
        return AddStyle(target, style, activator);
    }

    private static StyleInstance AddStyle(Target target, Style style, Activator? activator)
    {
        var result = new StyleInstance(style, activator, FrameType.Style);
        foreach (var setter in style.Setters)
            result.Add(setter.Instance(result, target));
        target.GetValueStore().AddFrame(result);
        return result;
    }

    private class Source : AvaloniaObject
    {
        public static readonly StyledProperty<string?> ValueProperty =
            AvaloniaProperty.Register<Source, string?>(nameof(Value));
        public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }

    private class Target : StyledElement
    {
        public static readonly StyledProperty<string?> ValueProperty =
            AvaloniaProperty.Register<Target, string?>(nameof(Value), "unset", validate: x => x != "invalid",
                enableDataValidation: true);
        public static readonly StyledProperty<string?> OtherProperty =
            AvaloniaProperty.Register<Target, string?>("Other", "other-unset");
        public static readonly DirectProperty<Target, string?> DirectProperty =
            AvaloniaProperty.RegisterDirect<Target, string?>(nameof(Direct), x => x._direct,
                (x, value) => x.SetDirect(value), "unset", enableDataValidation: true);
        private string? _direct = "unset";
        public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string? Direct { get => _direct; set => SetValue(DirectProperty, value); }
        private void SetDirect(string? value) => SetAndRaise(DirectProperty, ref _direct, value);
        public Exception? Error { get; private set; }
        protected override void UpdateDataValidation(AvaloniaProperty property, BindingValueType state, Exception? error)
            => Error = error;
    }

    private class OptInTarget : Target
    {
        static OptInTarget()
        {
            ValueProperty.OverrideMetadata<OptInTarget>(new StyledPropertyMetadata<string?>(enableDataValidation: true) { BindingValueBehavior = Compatibility });
            OtherProperty.OverrideMetadata<OptInTarget>(new StyledPropertyMetadata<string?> { BindingValueBehavior = Compatibility });
            DirectProperty.OverrideMetadata<OptInTarget>(new DirectPropertyMetadata<string?> { BindingValueBehavior = Compatibility });
        }
    }

    private class InheritedTarget : OptInTarget
    {
        static InheritedTarget()
        {
            ValueProperty.OverrideMetadata<InheritedTarget>(new StyledPropertyMetadata<string?>());
            DirectProperty.OverrideMetadata<InheritedTarget>(new DirectPropertyMetadata<string?>());
        }
    }

    private class DisabledTarget : InheritedTarget
    {
        static DisabledTarget()
        {
            ValueProperty.OverrideMetadata<DisabledTarget>(new StyledPropertyMetadata<string?> { BindingValueBehavior = BindingValueBehavior.None });
            DirectProperty.OverrideMetadata<DisabledTarget>(new DirectPropertyMetadata<string?> { BindingValueBehavior = BindingValueBehavior.None });
        }
    }

    private class DefaultTwoWayTarget : OptInTarget
    {
        static DefaultTwoWayTarget()
        {
            ValueProperty.OverrideMetadata<DefaultTwoWayTarget>(new StyledPropertyMetadata<string?>(defaultBindingMode: BindingMode.TwoWay));
            DirectProperty.OverrideMetadata<DefaultTwoWayTarget>(new DirectPropertyMetadata<string?>(defaultBindingMode: BindingMode.TwoWay));
        }
    }

    private sealed class CustomExpression : UntypedBindingExpressionBase
    {
        public CustomExpression() : base(BindingPriority.LocalValue) { }
        public override string Description => "Custom";
        public bool Stopped { get; private set; }
        protected override void StartCore() { }
        protected override void StopCore() => Stopped = true;
    }

    private sealed class Activator : StyleActivatorBase
    {
        private bool _active = true;
        public void Set(bool active) { _active = active; ReevaluateIsActive(); }
        protected override bool EvaluateIsActive() => _active;
        protected override void Initialize() { }
        protected override void Deinitialize() { }
    }
}
