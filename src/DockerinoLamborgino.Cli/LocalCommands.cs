using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DockerinoLamborgino.Cli;

internal static class LocalCommands
{
    private static string PipeName => Environment.GetEnvironmentVariable("DOCKERINO_PIPE_NAME") ?? "DockerinoLamborgino.Phase5";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Request(string Operation, Guid? ProjectId = null, Guid? ActionId = null,
        Guid? Id = null, long Offset = 0);
    private sealed record Response(bool Success, JsonElement Data, string? Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var resetId = Guid.Empty;
        if (args is ["reset"] && !Id(Environment.GetEnvironmentVariable("DOCKERINO_SESSION_ID"), out resetId))
        {
            error.WriteLine("Cannot reset: DOCKERINO_SESSION_ID is missing or invalid. Run reset from a managed session.");
            return 1;
        }

        Request? request = args switch
        {
            ["reset"] => new("session.reset", Id: resetId),
            ["launch", "start", var p, var a] when Id(p, out var project) && Id(a, out var action)
                => new("launch.start", ProjectId: project, ActionId: action),
            ["launch", "list"] => new("launch.list"),
            ["launch", "inspect", var value] when Id(value, out var id) => new("launch.inspect", Id: id),
            ["launch", "stop", var value] when Id(value, out var id) => new("launch.stop", Id: id),
            ["session", "list"] => new("session.list"),
            ["session", "inspect", var value] when Id(value, out var id) => new("session.inspect", Id: id),
            ["session", "stop", var value] when Id(value, out var id) => new("session.stop", Id: id),
            ["session", "reset", var value] when Id(value, out var id) => new("session.reset", Id: id),
            ["session", "output" or "follow", var value] when Id(value, out var id)
                => new("session.output", Id: id),
            ["session", "output" or "follow", var value, "--offset", var offset]
                when Id(value, out var id) && long.TryParse(offset, out var n) && n >= 0
                => new("session.output", Id: id, Offset: n),
            ["projects", "refresh"] => new("projects.refresh"),
            ["projects", "refresh", var value] when Id(value, out var projectId)
                => new("projects.refresh", ProjectId: projectId),
            ["dashboard", "open"] => new("dashboard.open"),
            _ => null
        };
        if (request is null)
        {
            error.WriteLine("Usage: projects refresh [project-id] | launch start <project-id> <action-id> | launch list | launch inspect|stop <launch-id> | session list | session inspect|stop|reset <session-id> | session output|follow <session-id> [--offset <offset>] | reset | dashboard open");
            return 2;
        }

        try
        {
            var follow = args[0] == "session" && args[1] == "follow";
            while (true)
            {
                var response = Send(request);
                if (!response.Success)
                {
                    error.WriteLine(response.Error ?? "Application request failed.");
                    return 1;
                }
                if (request.Operation != "session.output")
                {
                    output.WriteLine(response.Data.GetRawText());
                    return 0;
                }
                // Output offsets are opaque positions supplied by the app, not byte counts computed here.
                var data = response.Data;
                if (!data.TryGetProperty("text", out var text) ||
                    !data.TryGetProperty("nextOffset", out var next) ||
                    !next.TryGetInt64(out var nextOffset) || nextOffset < request.Offset ||
                    !data.TryGetProperty("completed", out var completed) ||
                    completed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Invalid session output response.");
                output.Write(text.GetString());
                output.Flush();
                if (!follow || completed.GetBoolean()) return 0;
                request = request with { Offset = nextOffset };
                Thread.Sleep(250);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool Id(string? value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;

    private static Response Send(Request request)
    {
        using var pipe = Connect(request.Operation != "session.reset");
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(request, Json));
        var line = reader.ReadLine() ?? throw new IOException("Application closed the connection without a response.");
        return JsonSerializer.Deserialize<Response>(line, Json) ?? throw new IOException("Invalid application response.");
    }

    private static NamedPipeClientStream Connect(bool allowStart)
    {
        var pipe = NewPipe();
        try { pipe.Connect(250); return pipe; }
        catch (TimeoutException) { pipe.Dispose(); }
        if (!allowStart) throw new IOException("The owning application is not running or its pipe is unavailable.");

        // Serialize startup among CLI processes. Recheck after acquiring the lock.
        using var mutex = new Mutex(false, @"Local\DockerinoLamborgino.Phase5.Startup");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("Timed out waiting for application startup.");
            pipe = NewPipe();
            try { pipe.Connect(250); return pipe; }
            catch (TimeoutException) { pipe.Dispose(); }

            var executable = Environment.GetEnvironmentVariable("DOCKERINO_APP_PATH");
            if (string.IsNullOrWhiteSpace(executable))
                executable = Path.Combine(AppContext.BaseDirectory, "DockerinoLamborgino.App.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Application executable not found. Place it beside the CLI or set DOCKERINO_APP_PATH.", executable);
            _ = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, ArgumentList = { "--launcher-only" } })
                ?? throw new IOException("Could not start the application.");
            pipe = NewPipe();
            try { pipe.Connect(15000); return pipe; }
            catch { pipe.Dispose(); throw; }
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private static NamedPipeClientStream NewPipe() =>
        new(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
}
