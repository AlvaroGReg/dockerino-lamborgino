using System.Text;

namespace DockerinoLamborgino.Core;

/// <summary>Thread-safe, untruncated output retained for the lifetime of a session.</summary>
public sealed class SessionOutputHistory
{
    private readonly object gate = new();
    private readonly StringBuilder output = new();
    private bool completed;
    private Exception? error;

    public void Append(ReadOnlySpan<char> text)
    {
        lock (gate) output.Append(text);
    }

    public void Complete()
    {
        lock (gate) completed = true;
    }

    /// <summary>Reports a failed output reader instead of presenting incomplete output as complete.</summary>
    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate) error = exception;
    }

    /// <summary>Offsets count UTF-16 code units, as in the CLI protocol.</summary>
    public SessionOutputSnapshot Read(long offset)
    {
        lock (gate)
        {
            if (error is not null)
                throw new InvalidOperationException("Terminal output could not be fully read.", error);
            if (offset < 0 || offset > output.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var start = (int)offset;
            return new(output.ToString(start, output.Length - start), output.Length, completed);
        }
    }
}

public sealed record SessionOutputSnapshot(string Text, long NextOffset, bool Completed);
