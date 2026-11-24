using DockerinoLamborgino.Cli;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class CliHelpTests
{
    [Fact]
    public void HelpPrintsCommandsWithoutAccessingStorageOrStartingApp()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var storage = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Assert.Equal(0, Program.Run(["--help"], output, error, storage));
        Assert.Contains("Usage:", output.ToString());
        Assert.Contains("session output|follow", output.ToString());
        Assert.Contains("git fetch|pull", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.False(Directory.Exists(storage));
    }

    [Fact]
    public void HelpWithExtraArgumentsIsInvalid()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, Program.Run(["--help", "extra"], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void VersionPrintsAssemblyVersionWithoutAccessingStorage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var storage = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Assert.Equal(0, Program.Run(["--version"], output, error, storage));
        Assert.Equal(typeof(Program).Assembly.GetName().Version + Environment.NewLine, output.ToString());
        Assert.Equal("", error.ToString());
        Assert.False(Directory.Exists(storage));
    }
}
