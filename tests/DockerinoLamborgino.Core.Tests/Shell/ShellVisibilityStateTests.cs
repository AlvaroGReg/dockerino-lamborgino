using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public class ShellVisibilityStateTests
{
    [Fact]
    public void VisibilityTogglesAreIndependentAndPreserveTheirState()
    {
        var state = new ShellState();
        Assert.False(state.IsCollapsed);
        Assert.False(state.IsMenuVisible);

        state.ToggleMenu();
        state.ToggleCollapsed();
        Assert.True(state.IsCollapsed);
        Assert.True(state.IsMenuVisible);

        state.ToggleMenu();
        Assert.True(state.IsCollapsed);
        Assert.False(state.IsMenuVisible);
        state.ToggleCollapsed();
        Assert.False(state.IsCollapsed);
        Assert.False(state.IsMenuVisible);
    }

    [Fact]
    public void VisibilityChangesDoNotChangeSessionsOrSelection()
    {
        var registry = new SessionRegistry();
        var session = new FakeSession();
        var opened = registry.Open(null, session);
        var state = new ShellState();

        state.ToggleMenu();
        state.ToggleCollapsed();
        state.ToggleMenu();
        state.ToggleCollapsed();

        Assert.Equal(opened, Assert.Single(registry.List()));
        Assert.Equal(opened.Id, registry.SelectedId);
        Assert.Equal(0, session.Stops);
    }

    private sealed class FakeSession : ISession
    {
        public int Stops { get; private set; }
        public void Start() { }
        public void Stop() => Stops++;
    }
}
