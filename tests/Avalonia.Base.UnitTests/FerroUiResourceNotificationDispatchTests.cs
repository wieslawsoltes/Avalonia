using System;
using Avalonia.Controls;
using Avalonia.Styling;
using Moq;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceNotificationDispatchTests
{
    [Fact]
    public void Ownerless_Notifications_Do_Not_Allocate()
    {
        var provider = new TestProvider();
        for (var i = 0; i < 256; ++i) provider.Publish();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; ++i) provider.Publish();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Reentrant_Owner_Replacement_Is_Observed_On_The_Next_Publication()
    {
        var provider = new TestProvider();
        var source = (IResourceProvider)provider;
        var first = new Mock<IResourceHost>();
        var second = new Mock<IResourceHost>();
        var firstCount = 0;
        var secondCount = 0;
        first.Setup(x => x.NotifyHostedResourcesChanged(It.IsAny<ResourcesChangedEventArgs>())).Callback(() =>
        {
            ++firstCount;
            source.RemoveOwner(first.Object);
            source.AddOwner(second.Object);
        });
        second.Setup(x => x.NotifyHostedResourcesChanged(It.IsAny<ResourcesChangedEventArgs>()))
            .Callback(() => ++secondCount);
        source.AddOwner(first.Object);
        provider.Publish();
        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
        Assert.Same(second.Object, provider.Owner);
        provider.Publish();
        Assert.Equal(1, secondCount);
        source.RemoveOwner(second.Object);
        provider.Publish();
        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public void Throwing_Owner_Callback_Does_Not_Suppress_Subsequent_Publication()
    {
        var provider = new TestProvider();
        var owner = new Mock<IResourceHost>();
        var count = 0;
        owner.Setup(x => x.NotifyHostedResourcesChanged(It.IsAny<ResourcesChangedEventArgs>())).Callback(() =>
        {
            if (++count == 1) throw new InvalidOperationException("notification");
        });
        ((IResourceProvider)provider).AddOwner(owner.Object);
        Assert.Throws<InvalidOperationException>(provider.Publish);
        Assert.Same(owner.Object, provider.Owner);
        provider.Publish();
        Assert.Equal(2, count);
    }

    [Fact]
    public void First_String_Write_Commits_Before_A_Throwing_Host_Callback()
    {
        var dictionary = new ResourceDictionary();
        var owner = new StyledElement { Resources = dictionary };
        var notifications = 0;
        ((IResourceHost)owner).ResourcesChanged += (_, _) =>
        {
            ++notifications;
            Assert.Equal("committed", dictionary["key"]);
            if (notifications == 1) throw new InvalidOperationException("notification");
        };
        Assert.Throws<InvalidOperationException>(() => dictionary["key"] = "committed");
        Assert.Equal("committed", dictionary["key"]);
        dictionary["key"] = "committed";
        Assert.Equal(2, notifications);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_Tracking_Flag_Combination_Preserves_Live_Insertion_And_Removal(bool dependency, bool opaque)
    {
        var dictionary = new ResourceDictionary();
        if (opaque) dictionary.Add(new object(), "opaque");
        var root = new ResourceDictionary { MergedDictionaries = { dictionary } };
        if (dependency) Assert.False(root.TryGetResource("key", null, out _));
        dictionary["key"] = 42;
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal(42, value);
        dictionary["key"] = null;
        Assert.True(root.TryGetResource("key", null, out value));
        Assert.Null(value);
        dictionary.Remove("key");
        Assert.False(root.TryGetResource("key", null, out _));
    }

    private sealed class TestProvider : ResourceProvider
    {
        public override bool HasResources => false;
        public override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
        {
            value = null;
            return false;
        }
        public void Publish() => RaiseResourcesChanged();
    }
}
