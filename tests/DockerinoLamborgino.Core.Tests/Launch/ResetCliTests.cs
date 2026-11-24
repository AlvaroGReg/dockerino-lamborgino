using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DockerinoLamborgino.Cli;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class ResetCliTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MissingOrInvalidContextIsRejected(string? context)
    {
        var previous = Environment.GetEnvironmentVariable("DOCKERINO_SESSION_ID");
        try
        {
            Environment.SetEnvironmentVariable("DOCKERINO_SESSION_ID", context);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, Program.Run(["reset"], output, error));
            Assert.Contains("DOCKERINO_SESSION_ID", error.ToString());
            Assert.Contains("missing or invalid", error.ToString());
            Assert.Equal("", output.ToString());
        }
        finally { Environment.SetEnvironmentVariable("DOCKERINO_SESSION_ID", previous); }
    }

    [Fact]
    public void ExtraArgumentsAreRejected()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, Program.Run(["reset", "extra"], output, error));
        Assert.Contains("reset", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ExplicitResetRejectsInvalidSyntax(string id)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, Program.Run(["session", "reset", id], output, error));
    }

    [Fact]
    public void ResetDoesNotStartUnavailableApplication()
    {
        var previousPipe = Environment.GetEnvironmentVariable("DOCKERINO_PIPE_NAME");
        var previousApp = Environment.GetEnvironmentVariable("DOCKERINO_APP_PATH");
        try
        {
            Environment.SetEnvironmentVariable("DOCKERINO_PIPE_NAME", $"dockerino-test-{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable("DOCKERINO_APP_PATH", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, Program.Run(["session", "reset", Guid.NewGuid().ToString()], output, error));
            Assert.Contains("not running", error.ToString());
            Assert.Equal("", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCKERINO_PIPE_NAME", previousPipe);
            Environment.SetEnvironmentVariable("DOCKERINO_APP_PATH", previousApp);
        }
    }

    [Fact]
    public async Task ExplicitResetUsesIdWithoutContext()
    {
        var previousId = Environment.GetEnvironmentVariable("DOCKERINO_SESSION_ID");
        var previousPipe = Environment.GetEnvironmentVariable("DOCKERINO_PIPE_NAME");
        var id = Guid.NewGuid();
        var launchId = Guid.NewGuid();
        var pipeName = $"dockerino-test-{Guid.NewGuid():N}";
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            Environment.SetEnvironmentVariable("DOCKERINO_SESSION_ID", null);
            Environment.SetEnvironmentVariable("DOCKERINO_PIPE_NAME", pipeName);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var command = Task.Run(() => Program.Run(["session", "reset", id.ToString()], output, error));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            using var request = JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
            Assert.Equal("session.reset", request.RootElement.GetProperty("operation").GetString());
            Assert.Equal(id, request.RootElement.GetProperty("id").GetGuid());
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { success = true, data = new { launchId, originalSessionId = id } }));
            Assert.Equal(0, await command.WaitAsync(timeout.Token));
            Assert.Contains(launchId.ToString(), output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCKERINO_SESSION_ID", previousId);
            Environment.SetEnvironmentVariable("DOCKERINO_PIPE_NAME", previousPipe);
        }
    }

    [Fact]
    public async Task ResetSendsSessionIdToExistingPipe()
    {
        var previousId = Environment.GetEnvironmentVariable("DOCKERINO_SESSION_ID");
        var previousPipe = Environment.GetEnvironmentVariable("DOCKERINO_PIPE_NAME");
        var id = Guid.NewGuid();
        var pipeName = $"dockerino-test-{Guid.NewGuid():N}";
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            Environment.SetEnvironmentVariable("DOCKERINO_SESSION_ID", id.ToString());
            Environment.SetEnvironmentVariable("DOCKERINO_PIPE_NAME", pipeName);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var command = Task.Run(() => Program.Run(["reset"], output, error));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            var line = await reader.ReadLineAsync(timeout.Token);
            using var request = JsonDocument.Parse(Assert.IsType<string>(line));
            Assert.Equal("session.reset", request.RootElement.GetProperty("operation").GetString());
            Assert.Equal(id, request.RootElement.GetProperty("id").GetGuid());
            await writer.WriteLineAsync("""{"success":true,"data":{"reset":true}}""");
            Assert.Equal(0, await command.WaitAsync(timeout.Token));
            Assert.Contains("\"reset\":true", output.ToString());
            Assert.Equal("", error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCKERINO_SESSION_ID", previousId);
            Environment.SetEnvironmentVariable("DOCKERINO_PIPE_NAME", previousPipe);
        }
    }
}
