using Microsoft.Win32;
using System.IO;
using System.Reflection;

namespace DockerinoLamborgino.App;

internal sealed class WindowsStartupSetting
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DockerinoLamborgino";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the application executable.");
            var command = $"\"{processPath}\"";
            if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var assemblyPath = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(assemblyPath))
                    throw new InvalidOperationException("Could not determine the application assembly path.");
                command += $" \"{assemblyPath}\"";
            }
            key.SetValue(ValueName, command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
