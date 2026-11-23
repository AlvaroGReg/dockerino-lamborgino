using System.Runtime.InteropServices;

namespace DockerinoLamborgino.App;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect { public int Left, Top, Right, Bottom; }

internal sealed record LauncherMonitor(string Device, NativeRect Work, bool Primary);

internal static class LauncherNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    internal static List<LauncherMonitor> GetMonitors()
    {
        var result = new List<LauncherMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
                result.Add(new LauncherMonitor(info.Device, info.Work, (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
