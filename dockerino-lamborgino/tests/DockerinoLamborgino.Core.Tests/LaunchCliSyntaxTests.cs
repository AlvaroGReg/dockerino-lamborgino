using DockerinoLamborgino.Cli;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class LaunchCliSyntaxTests
{
    [Theory]
    [InlineData("launch", "start", "not-a-guid", "not-a-guid")]
    [InlineData("session", "stop", "00000000-0000-0000-0000-000000000000")]
    [InlineData("dashboard", "exit")]
    public void RejectsInvalidOperationsWithoutStartingApplication(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, Program.Run(args, output, error));
        Assert.NotEmpty(error.ToString());
    }
}
