using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public class SessionRegistryTests
{
    private static readonly Project Project = new("example", "C:\\example");

    private sealed class FakeSession : ISession
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool FailStart { get; set; }
        public bool FailStop { get; set; }
        public void Start()
        {
            Starts++;
            if (FailStart) throw new InvalidOperationException("start failed");
        }
        public void Stop()
        {
            Stops++;
            if (FailStop) throw new InvalidOperationException("stop failed");
        }
    }

    [Fact]
    public void OpenSelectAndStopAreIsolated()
    {
        var registry = new SessionRegistry();
        var first = new FakeSession();
        var second = new FakeSession();
        var a = registry.Open(Project, first);
        var b = registry.Open(new Project("other", "C:\\other"), second);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(SessionState.Starting, a.State);
        registry.ReportState(a.Id, SessionState.Running);
        registry.ReportState(b.Id, SessionState.Running);
        Assert.Equal(b.Id, registry.SelectedId);
        Assert.Equal(a.Id, registry.Select(a.Id).Id);
        Assert.Equal(SessionState.Stopped, registry.Stop(b.Id).State);
        Assert.Equal(a.Id, registry.SelectedId);
        Assert.Equal(0, first.Stops);
        Assert.Equal(1, second.Stops);
        Assert.Equal(SessionState.Running, registry.List().Single(x => x.Id == a.Id).State);
        Assert.Equal(SessionState.Stopped, registry.Stop(a.Id).State);
        Assert.Equal(a.Id, registry.SelectedId);
        registry.Stop(a.Id);
        Assert.Equal(1, first.Stops);
        Assert.Equal(a.Id, registry.Select(a.Id).Id);
    }

    [Fact]
    public void UnknownIdsCannotAffectOwnedSessions()
    {
        var registry = new SessionRegistry();
        var fake = new FakeSession();
        var owned = registry.Open(Project, fake);
        var unknown = Guid.NewGuid();
        Assert.Throws<KeyNotFoundException>(() => registry.Select(unknown));
        Assert.Throws<KeyNotFoundException>(() => registry.Stop(unknown));
        Assert.Equal(owned.Id, registry.SelectedId);
        Assert.Equal(0, fake.Stops);
    }

    [Fact]
    public void FailedStartIsListedAndCanBeCleanedUp()
    {
        var registry = new SessionRegistry();
        var existing = registry.Open(Project, new FakeSession());
        var fake = new FakeSession { FailStart = true };
        var failed = registry.Open(Project, fake);
        Assert.Equal(SessionState.Failed, failed.State);
        Assert.Equal("start failed", failed.Error);
        Assert.Equal(existing.Id, registry.SelectedId);
        Assert.Equal(failed.Id, registry.Select(failed.Id).Id);
        Assert.Contains(registry.List(), x => x == failed);
        Assert.Equal(SessionState.Stopped, registry.Stop(failed.Id).State);
        Assert.Equal(1, fake.Stops);
    }

    [Fact]
    public void FailedStopCanBeRetried()
    {
        var registry = new SessionRegistry();
        var fake = new FakeSession { FailStop = true };
        var session = registry.Open(Project, fake);
        var failed = registry.Stop(session.Id);
        Assert.Equal(SessionState.Failed, failed.State);
        Assert.Equal("stop failed", failed.Error);
        Assert.Equal(session.Id, registry.SelectedId);
        fake.FailStop = false;
        var stopped = registry.Stop(session.Id);
        Assert.Equal(SessionState.Stopped, stopped.State);
        Assert.Null(stopped.Error);
        Assert.Equal(2, fake.Stops);
    }

    [Fact]
    public void SameInstanceCannotBeOwnedTwice()
    {
        var registry = new SessionRegistry();
        var fake = new FakeSession();
        registry.Open(Project, fake);
        Assert.Throws<InvalidOperationException>(() => registry.Open(Project, fake));
        Assert.Equal(1, fake.Starts);
        Assert.Single(registry.List());
    }

    [Fact]
    public void ProjectlessSessionReportsReadinessAndExitAndRemainsSelectable()
    {
        var registry = new SessionRegistry();
        var session = registry.Open(null, new FakeSession());
        Assert.Null(session.Project);
        Assert.Equal(SessionState.Starting, session.State);
        Assert.Equal(session.Id, registry.Select(session.Id).Id);
        Assert.Equal(SessionState.Running, registry.ReportState(session.Id, SessionState.Running).State);
        Assert.Equal(SessionState.Exited, registry.ReportState(session.Id, SessionState.Exited).State);
        Assert.Equal(session.Id, registry.Select(session.Id).Id);
        Assert.Throws<InvalidOperationException>(() => registry.ReportState(session.Id, SessionState.Running));
        Assert.Equal(SessionState.Stopped, registry.Stop(session.Id).State);
        Assert.Throws<InvalidOperationException>(() => registry.ReportState(session.Id, SessionState.Exited));
    }

    [Fact]
    public void ReportsEnforceTransitionsAndOwnership()
    {
        var registry = new SessionRegistry();
        var first = registry.Open(null, new FakeSession());
        var second = registry.Open(null, new FakeSession());
        Assert.Throws<KeyNotFoundException>(() => registry.ReportState(Guid.NewGuid(), SessionState.Running));
        Assert.Throws<InvalidOperationException>(() => registry.ReportState(first.Id, SessionState.Stopped));
        Assert.Throws<ArgumentException>(() => registry.ReportState(first.Id, SessionState.Running, "error"));
        var failed = registry.ReportState(first.Id, SessionState.Failed, "async failure");
        Assert.Equal("async failure", failed.Error);
        Assert.Equal(SessionState.Starting, registry.List().Single(x => x.Id == second.Id).State);
        Assert.Equal(first.Id, registry.Select(first.Id).Id);
        Assert.Throws<InvalidOperationException>(() => registry.ReportState(first.Id, SessionState.Running));
    }
}
