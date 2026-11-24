using System.IO.Pipes;
using System.Reflection;
using System.Text;
using DockerinoLamborgino.App;
using Xunit;

namespace DockerinoLamborgino.CommandServerTests;

public sealed class ResponseDeliveryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AfterResponseRunsWhenClientDisconnectsBeforeResponse()
    {
        var pipeName = $"dockerino-delivery-test-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connected = pipe.WaitForConnectionAsync();
        await client.ConnectAsync().WaitAsync(Timeout);
        await connected.WaitAsync(Timeout);

        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new DisconnectedPipeProbe(pipe);
        var callbacks = 0;
        await using var server = new LocalCommandServer(async (_, _) =>
        {
            dispatched.TrySetResult();
            await release.Task;
            return new LocalCommandResponse(true, probe)
            {
                AfterResponse = () => { Interlocked.Increment(ref callbacks); callback.TrySetResult(); }
            };
        });

        // Invoke the real per-connection handler with a pipe we own, so the probe
        // can establish that writes fail before the handler attempts its response.
        var method = typeof(LocalCommandServer).GetMethod("HandleAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handling = (Task)method.Invoke(server, [pipe, CancellationToken.None])!;
        try
        {
            await client.WriteAsync(Encoding.UTF8.GetBytes("{\"operation\":\"session.reset\"}\n"));
            await client.FlushAsync();
            await dispatched.Task.WaitAsync(Timeout);
            await client.DisposeAsync();
            release.TrySetResult();

            await callback.Task.WaitAsync(Timeout);
            await handling.WaitAsync(Timeout);
            Assert.IsAssignableFrom<IOException>(probe.WriteError);
            Assert.Equal(1, callbacks);
        }
        finally { release.TrySetResult(); }
    }

    public sealed class DisconnectedPipeProbe(NamedPipeServerStream pipe)
    {
        public Exception? WriteError { get; private set; }

        // JSON serialization evaluates this before the handler writes the response.
        public string Value
        {
            get
            {
                try { pipe.WriteByte(42); }
                catch (IOException ex) { WriteError = ex; }
                return "accepted";
            }
        }
    }
}
