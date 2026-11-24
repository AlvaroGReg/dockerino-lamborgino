using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace DockerinoLamborgino.App;

internal static class WindowsStartupCommand
{
    public static string Create(string? processPath, Func<string?> getAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("Could not determine the application executable.");

        var command = $"\"{processPath}\"";
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = getAssemblyPath();
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Could not determine the application assembly path.");
            command += $" \"{assemblyPath}\"";
        }
        return command;
    }

    // A dotnet-hosted application has a physical entry assembly; bundled apps
    // use their process executable and never invoke this method.
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Only called when the process is the dotnet host, not a single-file apphost.")]
    public static string? GetEntryAssemblyPath() => Assembly.GetEntryAssembly()?.Location;
}
