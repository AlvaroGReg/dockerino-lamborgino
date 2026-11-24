using System.IO;
using DockerinoLamborgino.Terminal;
using Xunit;

namespace DockerinoLamborgino.Terminal.Tests;

public class ProcessStartDiagnosticsTests
{
    [Theory]
    [InlineData("pwsh.exe -NoLogo -NoProfile -NoExit")]
    [InlineData("pwsh.exe -NoLogo -NoProfile -NoExit -EncodedCommand ZgA=")]
    [InlineData("\"PWSH.EXE\" -NoLogo")]
    public void Missing_default_shell_has_actionable_diagnostic(string command)
    {
        var error = JobObjectProcessFactory.CreateProcessStartException(command, null, 2);

        Assert.Equal(2, error.NativeErrorCode);
        Assert.Contains("PowerShell 7", error.Message);
        Assert.Contains("pwsh.exe", error.Message);
        Assert.Contains("PATH", error.Message);
        Assert.Contains("install", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("pwsh.exe -NoLogo", 5)]
    [InlineData("pwsh.exe -NoLogo", 3)]
    [InlineData("other.exe", 2)]
    [InlineData("pwsh.exe.other -NoLogo", 2)]
    [InlineData("C:\\missing\\pwsh.exe -NoLogo", 2)]
    public void Other_failures_keep_generic_diagnostic(string command, int nativeError)
    {
        var error = JobObjectProcessFactory.CreateProcessStartException(command, null, nativeError);

        Assert.Equal(nativeError, error.NativeErrorCode);
        Assert.Equal("Could not create the terminal process.", error.Message);
    }

    [Fact]
    public void Missing_working_directory_is_not_reported_as_missing_shell()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var error = JobObjectProcessFactory.CreateProcessStartException("pwsh.exe -NoLogo", directory, 2);

        Assert.Equal("Could not create the terminal process.", error.Message);
    }
}
