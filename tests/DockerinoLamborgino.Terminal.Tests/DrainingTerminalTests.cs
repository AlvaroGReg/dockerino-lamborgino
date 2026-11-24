using System.IO;
using System.Text;
using DockerinoLamborgino.Terminal;
using Xunit;

namespace DockerinoLamborgino.Terminal.Tests;

public class DrainingTerminalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessExitDrainsOutputAndFinishesWithOrWithoutConnectionStart(bool connectionStart)
    {
        using var factory = new JobObjectProcessFactory();
        var terminal = new DrainingTerminal();
        var output = new StringBuilder();
        terminal.InterceptOutputToUITerminal = (ref Span<char> text) =>
        {
            lock (output) output.Append(text);
            text = Span<char>.Empty;
        };
        terminal.TermReady += (_, _) =>
        {
            if (connectionStart) terminal.Start();
        };
        var command = $"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /d /q /c \"for /L %i in (1,1,5000) do @echo DRAIN-LINE-%i & echo DRAIN-END\"";
        var run = Task.Run(() => terminal.Start(command, factory: terminal.WrapFactory(factory)));
        await run.WaitAsync(TimeSpan.FromSeconds(30));
        lock (output)
        {
            var text = output.ToString();
            var plain = System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            var lines = System.Text.RegularExpressions.Regex.Matches(plain, @"DRAIN-LINE-(\d+)");
            Assert.Equal(5000, lines.Count);
            for (var i = 0; i < lines.Count; i++)
                Assert.Equal(i + 1, int.Parse(lines[i].Groups[1].Value));
            Assert.Contains("DRAIN-END", text);
        }
    }

    [Fact]
    public async Task StoppingInteractiveProcessDrainsAndReturns()
    {
        using var factory = new JobObjectProcessFactory();
        var terminal = new DrainingTerminal();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();
        terminal.InterceptOutputToUITerminal = (ref Span<char> text) =>
        {
            lock (output)
            {
                output.Append(text);
                if (output.ToString().Contains("BEFORE-STOP")) marker.TrySetResult();
            }
            text = Span<char>.Empty;
        };
        terminal.TermReady += (_, _) => ready.TrySetResult();
        var command = $"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /d /q /k echo BEFORE-STOP";
        var run = Task.Run(() => terminal.Start(command, factory: terminal.WrapFactory(factory)));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await marker.Task.WaitAsync(TimeSpan.FromSeconds(15));
        factory.Dispose();
        await run.WaitAsync(TimeSpan.FromSeconds(30));
        lock (output) Assert.Contains("BEFORE-STOP", output.ToString());
    }

    [Fact]
    public async Task ReaderErrorIsVisibleWhenConnectionOwnsReader()
    {
        using var factory = new JobObjectProcessFactory();
        var terminal = new DrainingTerminal();
        terminal.InterceptOutputToUITerminal = (ref Span<char> _) => throw new IOException("reader failed");
        terminal.TermReady += (_, _) => terminal.Start();
        var command = $"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /d /q /c echo failure-test";
        var run = Task.Run(() => terminal.Start(command, factory: terminal.WrapFactory(factory)));
        var error = await Assert.ThrowsAsync<IOException>(async () => await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("reader failed", error.Message);
        Assert.Same(error, terminal.ReaderError);
    }
}
