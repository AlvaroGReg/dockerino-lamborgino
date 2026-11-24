using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class GitAuthenticationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchAndPullRejectInteractiveAuthentication(bool pull)
    {
        string root = Path.Combine(Path.GetTempPath(), "dockerino git auth " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        using var cancellation = new CancellationTokenSource();
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        int requests = 0;
        Task server = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellation.Token))) { }
                    Interlocked.Increment(ref requests);
                    byte[] response = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        });

        try
        {
            Git(root, "init", "-b", "main");
            Git(root, "config", "user.name", "Test");
            Git(root, "config", "user.email", "test@example.com");
            Git(root, "config", "credential.helper", "");
            Git(root, "config", "core.askPass", "");
            Git(root, "config", "http.proxy", "");
            Git(root, "config", "http.extraHeader", "");
            File.WriteAllText(Path.Combine(root, "initial"), "initial");
            Git(root, "add", "initial");
            Git(root, "commit", "-m", "initial");
            Git(root, "remote", "add", "origin", $"http://127.0.0.1:{port}/repository.git");
            Git(root, "config", "branch.main.remote", "origin");
            Git(root, "config", "branch.main.merge", "refs/heads/main");

            var stopwatch = Stopwatch.StartNew();
            var service = new GitRepositoryService();
            var result = Assert.Single(pull ? service.Pull([root]) : service.Fetch([root]));

            Assert.False(result.Success);
            Assert.True(Volatile.Read(ref requests) > 0, "Git did not reach the local authentication challenge.");
            Assert.Contains("terminal prompts disabled", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("noninteractive", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Authentication took {stopwatch.Elapsed}.");
            Assert.Equal("main", result.State!.CurrentBranch);
            Assert.Equal("initial", File.ReadAllText(Path.Combine(root, "initial")));
        }
        finally
        {
            cancellation.Cancel();
            await server;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    private static void Git(string path, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{string.Join(" ", args)}: {error.Result} {output.Result}");
    }
}
