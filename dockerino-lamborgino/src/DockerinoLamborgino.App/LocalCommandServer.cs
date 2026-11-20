using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DockerinoLamborgino.App;

/// <summary>A single JSON request and response per connection. Dispatch is not on the UI thread.</summary>
public sealed class LocalCommandServer : IAsyncDisposable
{
    public static string PipeName => Environment.GetEnvironmentVariable("DOCKERINO_PIPE_NAME") ?? "DockerinoLamborgino.Phase5";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<LocalCommandRequest, CancellationToken, Task<LocalCommandResponse>> dispatch;
    private readonly CancellationTokenSource stopping = new();
    private Task? loop;

    public LocalCommandServer(Func<LocalCommandRequest, CancellationToken, Task<LocalCommandResponse>> dispatch)
        => this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));

    public void Start()
    {
        if (loop is not null) throw new InvalidOperationException("Server has already started.");
        // Construct synchronously so a duplicate owner fails before Start returns.
        var first = CreatePipe(first: true);
        loop = RunAsync(first);
    }

    private static NamedPipeServerStream CreatePipe(bool first = false) =>
        new(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly |
            (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    private async Task RunAsync(NamedPipeServerStream first)
    {
        var pipe = first;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                var connected = pipe;
                pipe = CreatePipe();
                _ = HandleAsync(connected, stopping.Token);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally { await pipe.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                LocalCommandResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<LocalCommandRequest>(line ?? "", Json)
                        ?? throw new JsonException("Missing request.");
                    response = await dispatch(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    response = new(false, null, ex.Message);
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json).AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        if (loop is not null) await loop.ConfigureAwait(false);
        stopping.Dispose();
    }
}

public sealed record LocalCommandRequest(string Operation, Guid? ProjectId = null, Guid? ActionId = null,
    Guid? Id = null, long Offset = 0);

/// <summary>For session.output, Data must contain text, nextOffset and completed.</summary>
public sealed record LocalCommandResponse(bool Success, object? Data = null, string? Error = null);

public sealed record LocalSessionOutput(string Text, long NextOffset, bool Completed);
