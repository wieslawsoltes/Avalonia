using System;
using System.Reactive.Subjects;
using Avalonia.Data;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class AvaloniaPropertyTests_RegistrationMetadata
{
    [Fact]
    public void Styled_Registration_Preserves_Metadata_Validation_And_Inheritance()
    {
        var metadata = new StyledPropertyMetadata<string?>("default", BindingMode.TwoWay,
            (_, value) => value?.ToUpperInvariant(), enableDataValidation: true)
        {
            BindingValueBehavior = BindingValueBehavior.ReplaceNonWritableBindingOnSetValue,
        };
        var property = AvaloniaProperty.Register<Owner, string?>(metadata, "Styled", inherits: true,
            validate: value => value != "invalid");
        var target = new Owner();

        Assert.Same(metadata, property.GetMetadata<Owner>());
        Assert.Same(metadata, property.GetMetadata<DerivedOwner>());
        Assert.True(property.Inherits);
        Assert.True(metadata.EnableDataValidation);
        Assert.Equal(BindingMode.TwoWay, metadata.DefaultBindingMode);
        Assert.Equal("default", target.GetValue(property));
        target.SetValue(property, "value");
        Assert.Equal("VALUE", target.GetValue(property));
        Assert.Throws<ArgumentException>(() => target.SetValue(property, "invalid"));
        Assert.Throws<InvalidOperationException>(() => metadata.Merge(new StyledPropertyMetadata<string?>(), property));
        Assert.Throws<InvalidOperationException>(() => property.OverrideMetadata<Owner>(new StyledPropertyMetadata<string?>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_Metadata_Applies_To_Original_Owner_Bindings(bool direct)
    {
        var target = new RegisteredOwner();
        AvaloniaProperty property = direct ? RegisteredOwner.DirectProperty : RegisteredOwner.StyledProperty;
        using var source = new BehaviorSubject<string?>("source");
        using var binding = target.Bind(property, source);
        Assert.Equal("source", target.GetValue(property));

        target[property] = "assigned";
        source.OnNext("later");
        Assert.Equal("assigned", target.GetValue(property));
        target.ClearValue(property);
        Assert.Equal("unset", target.GetValue(property));
    }

    [Fact]
    public void Direct_Registration_Preserves_Metadata_And_Duplicate_Override_Invariant()
    {
        var metadata = RegisteredOwner.DirectProperty.GetMetadata(typeof(RegisteredOwner));
        Assert.Equal("unset", metadata.UnsetValue);
        Assert.True(metadata.EnableDataValidation);
        Assert.Equal(BindingMode.TwoWay, metadata.DefaultBindingMode);
        Assert.Throws<InvalidOperationException>(() => RegisteredOwner.DirectProperty.OverrideMetadata<RegisteredOwner>(
            new DirectPropertyMetadata<string?>()));
        Assert.Throws<InvalidOperationException>(() => metadata.Merge(new DirectPropertyMetadata<string?>(), RegisteredOwner.DirectProperty));
    }

    [Fact]
    public void Metadata_Overloads_Validate_Null_And_Invalid_Arguments()
    {
        Assert.Throws<ArgumentNullException>(() => AvaloniaProperty.Register<Owner, string?>((StyledPropertyMetadata<string?>)null!, "NullMetadata"));
        Assert.Throws<ArgumentNullException>(() => AvaloniaProperty.Register<Owner, string?>(new StyledPropertyMetadata<string?>(), null!));
        Assert.Throws<ArgumentException>(() => AvaloniaProperty.Register<Owner, string?>(new StyledPropertyMetadata<string?>("invalid"), "Invalid", validate: _ => false));
        Assert.Throws<ArgumentNullException>(() => AvaloniaProperty.RegisterDirect<Owner, string?>((DirectPropertyMetadata<string?>)null!, "NullMetadata", _ => null));
        Assert.Throws<ArgumentNullException>(() => AvaloniaProperty.RegisterDirect<Owner, string?>(new DirectPropertyMetadata<string?>(), "NullGetter", null!));
    }

    private class Owner : AvaloniaObject { }
    private sealed class DerivedOwner : Owner { }

    private sealed class RegisteredOwner : AvaloniaObject
    {
        private const BindingValueBehavior Behavior = BindingValueBehavior.ReplaceNonWritableBindingOnSetValue |
            BindingValueBehavior.RemoveBindingOnClearValue;
        public static readonly StyledProperty<string?> StyledProperty = AvaloniaProperty.Register<RegisteredOwner, string?>(
            new StyledPropertyMetadata<string?>("unset") { BindingValueBehavior = Behavior }, "Styled");
        public static readonly DirectProperty<RegisteredOwner, string?> DirectProperty = AvaloniaProperty.RegisterDirect<RegisteredOwner, string?>(
            new DirectPropertyMetadata<string?>("unset", BindingMode.TwoWay, enableDataValidation: true)
            { BindingValueBehavior = Behavior }, "Direct", target => target._direct,
            (target, value) => target.SetAndRaise(DirectProperty!, ref target._direct, value));
        private string? _direct = "unset";
    }
}
