using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DockerinoLamborgino.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new System.Windows.Application();
        var terminal = new TerminalSessionView();
        var interactions = 0;
        terminal.UserInteracted += () => interactions++;
        var button = new System.Windows.Controls.Button { Content = "Other focus target", Height = 32 };
        var nextButton = new System.Windows.Controls.Button { Content = "Next focus target", Height = 32 };
        var panel = new DockPanel();
        DockPanel.SetDock(button, Dock.Top);
        DockPanel.SetDock(nextButton, Dock.Top);
        panel.Children.Add(button);
        panel.Children.Add(nextButton);
        panel.Children.Add(terminal);
        var window = new Window { Content = panel, Width = 800, Height = 500, Title = "Terminal Tab integration" };
        var result = 1;
        var outputFile = Path.Combine(Path.GetTempPath(), $"terminal-tab-{Guid.NewGuid():N}.txt");

        window.Loaded += async (_, _) =>
        {
            try
            {
                terminal.Start();
                await WaitUntil(() => terminal.State == TerminalSessionState.Running && terminal.Output.Contains("PS "), "PowerShell prompt");
                var host = FindHost(terminal) ?? throw new Exception("Terminal HWND host not found.");
                window.Activate();
                var point = host.PointToScreen(new System.Windows.Point(host.ActualWidth / 2, host.ActualHeight / 2));
                SetCursorPos((int)point.X, (int)point.Y);
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                await WaitUntil(() => GetFocus() == host.Handle, "terminal keyboard focus");
                await WaitUntil(() => interactions > 0, "terminal mouse interaction");
                if (GetForegroundWindow() != new WindowInteropHelper(window).Handle)
                    throw new Exception("Test window is not foreground; keyboard input cannot be tested safely.");

                // Tab completes Set-Cont to Set-Content. The file proves that
                // the shell received Tab and accepted the remaining keystrokes.
                var beforeTyping = interactions;
                TypeText("Set-Cont");
                await Task.Delay(300);
                if (interactions <= beforeTyping) throw new Exception("Terminal keyboard interaction was not reported.");
                PressKey(0x09);
                await Task.Delay(300);
                if (GetFocus() != host.Handle) throw new Exception("Tab moved focus away from the terminal.");
                TypeText($" (Join-Path $env:TEMP '{Path.GetFileName(outputFile)}') 'tab-ok'");
                PressKey(0x0d);
                await WaitUntil(() => File.Exists(outputFile), "completed PowerShell command");
                if (File.ReadAllText(outputFile).Trim() != "tab-ok") throw new Exception("Unexpected command output.");

                keybd_event(0x10, 0, 0, UIntPtr.Zero);
                PressKey(0x09);
                keybd_event(0x10, 0, 2, UIntPtr.Zero);
                await Task.Delay(200);
                if (GetFocus() != host.Handle) throw new Exception("Shift+Tab moved focus away from the terminal.");

                button.Focus();
                await Task.Delay(100);
                if (!button.IsKeyboardFocused) throw new Exception("Could not focus the non-terminal button.");
                var beforeOtherControl = interactions;
                PressKey(0x09);
                await Task.Delay(200);
                if (!nextButton.IsKeyboardFocused) throw new Exception("Tab did not navigate to the next non-terminal button.");
                if (interactions != beforeOtherControl) throw new Exception("Input outside the terminal was reported as terminal interaction.");
                Console.WriteLine("PASS: PowerShell received Tab; Tab and Shift+Tab retained terminal focus; Tab navigated outside the terminal.");
                result = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex}");
                Console.Error.WriteLine($"Terminal output: {terminal.Output}");
            }
            finally
            {
                terminal.Stop();
                window.Close();
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
        };
        app.Run(window);
        return result;
    }

    private static async Task WaitUntil(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Timed out waiting for {description}.");
            await Task.Delay(100);
        }
    }

    private static HwndHost? FindHost(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is HwndHost host) return host;
            if (FindHost(child) is { } nested) return nested;
        }
        return null;
    }

    private static void PressKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
    }

    private static void TypeText(string text)
    {
        foreach (var character in text)
        {
            var key = VkKeyScan(character);
            if (key == -1) throw new Exception($"Cannot type character: {character}");
            var modifiers = key >> 8;
            if ((modifiers & 1) != 0) keybd_event(0x10, 0, 0, UIntPtr.Zero);
            if ((modifiers & 2) != 0) keybd_event(0x11, 0, 0, UIntPtr.Zero);
            if ((modifiers & 4) != 0) keybd_event(0x12, 0, 0, UIntPtr.Zero);
            PressKey((byte)key);
            if ((modifiers & 4) != 0) keybd_event(0x12, 0, 2, UIntPtr.Zero);
            if ((modifiers & 2) != 0) keybd_event(0x11, 0, 2, UIntPtr.Zero);
            if ((modifiers & 1) != 0) keybd_event(0x10, 0, 2, UIntPtr.Zero);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
