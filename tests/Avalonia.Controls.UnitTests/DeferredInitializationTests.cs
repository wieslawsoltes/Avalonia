using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Controls.UnitTests;

public class DeferredInitializationTests : ScopedTestBase
{
    [Fact]
    public void Opted_In_Window_Initializes_After_Derived_Constructor_State_Is_Available()
    {
        using var app = UnitTestApplication.Start(TestServices.StyledWindow);
        var state = new object();
        var window = new DeferredWindow(state);
        Assert.False(window.IsInitialized);
        Assert.Equal(0, window.Calls);

        window.Show();

        Assert.True(window.IsInitialized);
        Assert.Same(state, window.InitializedState);
        Assert.Equal(1, window.Calls);
        window.Hide();
        window.Show();
        Assert.Equal(1, window.Calls);
        window.Close();
    }

    [Fact]
    public void Default_Window_Initialization_Is_Unchanged()
    {
        using var app = UnitTestApplication.Start(TestServices.StyledWindow);
        var window = new Window();
        Assert.True(window.IsInitialized);
        window.Close();
    }

    private sealed class DeferredWindow : Window
    {
        private readonly object _state;
        public DeferredWindow(object state) => _state = state;
        public object? InitializedState { get; private set; }
        public int Calls { get; private set; }
        protected override bool IsInitializationDeferred => _state is null;
        protected override void OnInitialized()
        {
            InitializedState = _state;
            ++Calls;
            base.OnInitialized();
        }
    }
}
