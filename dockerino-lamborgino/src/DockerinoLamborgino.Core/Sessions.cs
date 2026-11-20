namespace DockerinoLamborgino.Core;

public enum SessionState
{
    Starting,
    Running,
    Stopping,
    Exited,
    Stopped,
    Failed
}

/// <summary>The implementation owns its resources; callers never provide process IDs or commands.</summary>
public interface ISession
{
    void Start();
    void Stop();
}

public sealed record SessionInfo(Guid Id, Project? Project, SessionState State, string? Error);

/// <summary>
/// In-memory owner of sessions. Operations are serialized; implementations must not call
/// back into the registry or block indefinitely. Stopped and failed entries remain listed.
/// </summary>
public sealed class SessionRegistry
{
    private sealed class Entry(Guid id, Project? project, ISession session)
    {
        public Guid Id { get; } = id;
        public Project? Project { get; } = project;
        public ISession Session { get; } = session;
        public SessionState State { get; set; } = SessionState.Starting;
        public string? Error { get; set; }
        public SessionInfo Snapshot() => new(Id, Project, State, Error);
    }

    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private Guid? selectedId;

    public Guid? SelectedId
    {
        get { lock (gate) return selectedId; }
    }

    public IReadOnlyList<SessionInfo> List()
    {
        lock (gate) return entries.Values.Select(entry => entry.Snapshot()).ToArray();
    }

    /// <summary>
    /// Registers and starts an owned session. A successful Start only means startup was
    /// requested; the owner must report readiness or exit separately using ReportState.
    /// Synchronous startup failures remain inspectable and can be stopped for cleanup.
    /// </summary>
    public SessionInfo Open(Project? project, ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            if (entries.Values.Any(entry => ReferenceEquals(entry.Session, session)))
                throw new InvalidOperationException("A session instance can only be registered once.");

            var entry = new Entry(Guid.NewGuid(), project, session);
            entries.Add(entry.Id, entry);
            try
            {
                session.Start();
                selectedId = entry.Id;
            }
            catch (Exception exception)
            {
                entry.State = SessionState.Failed;
                entry.Error = exception.Message;
            }
            return entry.Snapshot();
        }
    }

    public SessionInfo Select(Guid id)
    {
        lock (gate)
        {
            var entry = Get(id);
            selectedId = id;
            return entry.Snapshot();
        }
    }

    /// <summary>
    /// Reports an asynchronous lifecycle event for an owned session. Only Starting to
    /// Running, Exited or Failed, and Running to Exited or Failed are permitted.
    /// Late reports after stopping, exit or failure are rejected.
    /// </summary>
    public SessionInfo ReportState(Guid id, SessionState state, string? error = null)
    {
        lock (gate)
        {
            var entry = Get(id);
            var valid = entry.State switch
            {
                SessionState.Starting => state is SessionState.Running or SessionState.Exited or SessionState.Failed,
                SessionState.Running => state is SessionState.Exited or SessionState.Failed,
                _ => false
            };
            if (!valid) throw new InvalidOperationException($"Cannot report {state} from {entry.State}.");
            if (state != SessionState.Failed && error is not null)
                throw new ArgumentException("Only failed sessions can have an error.", nameof(error));
            entry.State = state;
            entry.Error = error;
            return entry.Snapshot();
        }
    }

    /// <summary>Stops one owned session. Repeated stops of a stopped session are harmless.
    /// A failed stop can be retried, including cleanup after a failed start.</summary>
    public SessionInfo Stop(Guid id)
    {
        lock (gate)
        {
            var entry = Get(id);
            if (entry.State == SessionState.Stopped) return entry.Snapshot();
            entry.State = SessionState.Stopping;
            try
            {
                entry.Session.Stop();
                entry.State = SessionState.Stopped;
                entry.Error = null;
            }
            catch (Exception exception)
            {
                entry.State = SessionState.Failed;
                entry.Error = exception.Message;
            }
            return entry.Snapshot();
        }
    }

    private Entry Get(Guid id) => entries.TryGetValue(id, out var entry)
        ? entry
        : throw new KeyNotFoundException($"Unknown session ID: {id}");
}
