using Microsoft.Win32;

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
        if (enabled)
        {
            var command = WindowsStartupCommand.Create(Environment.ProcessPath, WindowsStartupCommand.GetEntryAssemblyPath);
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, command);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
