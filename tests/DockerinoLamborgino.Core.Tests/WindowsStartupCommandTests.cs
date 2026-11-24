using DockerinoLamborgino.App;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class WindowsStartupCommandTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Dockerino\App.exe")]
    [InlineData(@"C:\Program Files\Dockerino\single-file.exe")]
    public void ApphostDoesNotRequestAssemblyPath(string processPath)
    {
        Assert.Equal($"\"{processPath}\"", WindowsStartupCommand.Create(processPath,
            () => throw new InvalidOperationException("Assembly path must not be requested.")));
    }

    [Fact]
    public void DotnetHostIncludesQuotedAssemblyPath()
    {
        Assert.Equal("\"C:\\Program Files\\dotnet\\DOTNET.EXE\" \"C:\\My App\\App.dll\"",
            WindowsStartupCommand.Create(@"C:\Program Files\dotnet\DOTNET.EXE", () => @"C:\My App\App.dll"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DotnetHostRejectsMissingAssemblyPath(string? assemblyPath)
    {
        Assert.Throws<InvalidOperationException>(() => WindowsStartupCommand.Create(@"C:\dotnet\dotnet.exe", () => assemblyPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RejectsMissingProcessPath(string? processPath)
    {
        Assert.Throws<InvalidOperationException>(() => WindowsStartupCommand.Create(processPath, () => "unused"));
    }
}
