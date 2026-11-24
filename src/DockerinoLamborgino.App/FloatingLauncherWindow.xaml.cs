using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DockerinoLamborgino.App;

/// <summary>A floating shell. The host owns dashboard activation and application shutdown.</summary>
public partial class LauncherWindow : Window
{
    private const double LongSide = 64;
    private const double ShortSide = 34;
    private readonly string positionFile;
    private LauncherPosition position = new("", LauncherEdge.Top, 0.5);
    private NativePoint pressPoint;
    private bool pressed;
    private bool dragging;
    private bool positioning;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public void SetTerminalStatus(bool hasError, bool isLoading)
    {
        var (fill, stroke, description) = hasError
            ? ("#E8B83F3F", "#FFFF7777", "Terminal status: error")
            : isLoading
                ? ("#E8B88724", "#FFFFD166", "Terminal status: loading")
                : ("#E82E6968", "#FF76B4AD", "Terminal status: normal");
        Body.Fill = (Brush)new BrushConverter().ConvertFromString(fill)!;
        Body.Stroke = (Brush)new BrushConverter().ConvertFromString(stroke)!;
        Glow.Fill = Body.Stroke;
        Title = description;
        Body.ToolTip = description;
        Surface.ToolTip = description;
        System.Windows.Automation.AutomationProperties.SetName(this, description);
        System.Windows.Automation.AutomationProperties.SetName(Surface, description);
    }

    public LauncherWindow() : this(null) { }

    /// <param name="positionFile">Optional absolute path for launcher-only state.</param>
    public LauncherWindow(string? positionFile)
    {
        if (positionFile is not null && !Path.IsPathFullyQualified(positionFile))
            throw new ArgumentException("The position file path must be absolute.", nameof(positionFile));
        this.positionFile = positionFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dockerino-lamborgino", "launcher-position.json");
        InitializeComponent();
        SourceInitialized += (_, _) => RestorePosition();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => Place(position, save: false));

    private void RestorePosition()
    {
        try
        {
            if (File.Exists(positionFile))
                position = JsonSerializer.Deserialize<LauncherPosition>(File.ReadAllText(positionFile)) ?? position;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // An unreadable or invalid preference must not prevent the application from starting.
        }
        Place(position, save: false);
    }

    private void Place(LauncherPosition requested, bool save)
    {
        if (positioning) return;
        var monitors = LauncherNative.GetMonitors();
        if (monitors.Count == 0) return;
        var monitor = monitors.FirstOrDefault(m => m.Device == requested.Monitor)
            ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
        var edge = Enum.IsDefined(requested.Edge) ? requested.Edge : LauncherEdge.Top;
        var fraction = double.IsFinite(requested.Fraction) ? Math.Clamp(requested.Fraction, 0, 1) : 0.5;
        if (monitor.Device != requested.Monitor) { edge = LauncherEdge.Top; fraction = 0.5; }
        position = new(monitor.Device, edge, fraction);

        var horizontal = edge is LauncherEdge.Top or LauncherEdge.Bottom;
        Width = horizontal ? LongSide : ShortSide;
        Height = horizontal ? ShortSide : LongSide;
        DrawBody(edge);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        positioning = true;
        try
        {
            // First move establishes the destination monitor, then use its actual window DPI.
            var area = monitor.Work;
            LauncherNative.SetWindowPos(hwnd, IntPtr.Zero, area.Left, area.Top, 0, 0, 0x0015);
            var dpi = LauncherNative.GetDpiForWindow(hwnd);
            var scale = (dpi == 0 ? 96 : dpi) / 96.0;
            var width = (int)Math.Round(Width * scale);
            var height = (int)Math.Round(Height * scale);
            var availableX = Math.Max(0, area.Right - area.Left - width);
            var availableY = Math.Max(0, area.Bottom - area.Top - height);
            var x = edge switch
            {
                LauncherEdge.Left => area.Left,
                LauncherEdge.Right => area.Right - width,
                _ => area.Left + (int)Math.Round(availableX * fraction)
            };
            var y = edge switch
            {
                LauncherEdge.Top => area.Top,
                LauncherEdge.Bottom => area.Bottom - height,
                _ => area.Top + (int)Math.Round(availableY * fraction)
            };
            LauncherNative.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, 0x0014);
        }
        finally { positioning = false; }
        if (save) SavePosition();
    }

    private void DrawBody(LauncherEdge edge)
    {
        // A half ellipse whose straight side lies on the selected work-area edge.
        var geometry = edge switch
        {
            LauncherEdge.Top => "M 2,0 L 62,0 C 62,18 49,31 32,31 C 15,31 2,18 2,0 Z",
            LauncherEdge.Bottom => "M 2,34 L 62,34 C 62,16 49,3 32,3 C 15,3 2,16 2,34 Z",
            LauncherEdge.Left => "M 0,2 L 0,62 C 18,62 31,49 31,32 C 31,15 18,2 0,2 Z",
            _ => "M 34,2 L 34,62 C 16,62 3,49 3,32 C 3,15 16,2 34,2 Z"
        };
        Body.Data = Geometry.Parse(geometry);
        Glow.Data = Body.Data;
        ExitButton.HorizontalAlignment = edge switch
        {
            LauncherEdge.Left => HorizontalAlignment.Left,
            LauncherEdge.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        ExitButton.VerticalAlignment = edge switch
        {
            LauncherEdge.Top => VerticalAlignment.Top,
            LauncherEdge.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center
        };
        SetExpanded(Surface.IsMouseOver);
    }

    private void SetExpanded(bool expanded)
    {
        var horizontal = position.Edge is LauncherEdge.Top or LauncherEdge.Bottom;
        var scale = expanded ? 1.0 : 0.78;
        var origin = position.Edge switch
        {
            LauncherEdge.Bottom => new Point(0.5, 1),
            LauncherEdge.Right => new Point(1, 0.5),
            _ => new Point(0, 0)
        };
        Body.RenderTransformOrigin = origin;
        Glow.RenderTransformOrigin = origin;
        Body.RenderTransform = new ScaleTransform(horizontal ? 1 : scale, horizontal ? scale : 1);
        Glow.RenderTransform = new ScaleTransform(horizontal ? 1 : scale, horizontal ? scale : 1);
    }

    private void SavePosition()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(positionFile)!);
            File.WriteAllText(positionFile, JsonSerializer.Serialize(position));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Placement remains usable for this session if preferences cannot be written.
        }
    }

    private void Surface_MouseEnter(object sender, MouseEventArgs e)
    {
        ExitButton.Visibility = Visibility.Visible;
        Glow.Opacity = 0.8;
        SetExpanded(true);
    }

    private void Surface_MouseLeave(object sender, MouseEventArgs e)
    {
        if (pressed) return;
        ExitButton.Visibility = Visibility.Collapsed;
        Glow.Opacity = 0.45;
        SetExpanded(false);
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (source == ExitButton || IsChildOf(source, ExitButton))) return;
        if (!LauncherNative.GetCursorPos(out pressPoint)) return;
        pressed = true;
        dragging = false;
        Surface.CaptureMouse();
        e.Handled = true;
    }

    private static bool IsChildOf(DependencyObject source, DependencyObject parent)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current == parent) return true;
        return false;
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!pressed || !LauncherNative.GetCursorPos(out var point)) return;
        if (!dragging && (Math.Abs(point.X - pressPoint.X) >= LauncherNative.GetSystemMetrics(68) ||
                          Math.Abs(point.Y - pressPoint.Y) >= LauncherNative.GetSystemMetrics(69)))
            dragging = true;
        if (dragging) MoveToNearestEdge(point);
    }

    private void MoveToNearestEdge(NativePoint point)
    {
        var monitors = LauncherNative.GetMonitors();
        if (monitors.Count == 0) return;
        // Distance to the finite edge segment allows either side of a shared monitor boundary.
        var candidates = monitors.SelectMany(m => Enum.GetValues<LauncherEdge>()
            .Select(edge => (Monitor: m, Edge: edge, Distance: DistanceSquared(point, m.Work, edge))))
            .OrderBy(c => c.Distance)
            .ThenByDescending(c => c.Monitor.Device == position.Monitor && c.Edge == position.Edge)
            .First();
        var area = candidates.Monitor.Work;
        var horizontal = candidates.Edge is LauncherEdge.Top or LauncherEdge.Bottom;
        var hwnd = new WindowInteropHelper(this).Handle;
        var dpi = LauncherNative.GetDpiForWindow(hwnd);
        var length = LongSide * (dpi == 0 ? 1 : dpi / 96.0);
        var start = horizontal ? area.Left : area.Top;
        var available = Math.Max(0, (horizontal ? area.Right - area.Left : area.Bottom - area.Top) - length);
        var coordinate = horizontal ? point.X : point.Y;
        var fraction = available == 0 ? 0.5 : Math.Clamp((coordinate - start - length / 2) / available, 0, 1);
        Place(new(candidates.Monitor.Device, candidates.Edge, fraction), save: false);
    }

    private static double DistanceSquared(NativePoint p, NativeRect r, LauncherEdge edge)
    {
        var x = edge switch { LauncherEdge.Left => r.Left, LauncherEdge.Right => r.Right, _ => Math.Clamp(p.X, r.Left, r.Right) };
        var y = edge switch { LauncherEdge.Top => r.Top, LauncherEdge.Bottom => r.Bottom, _ => Math.Clamp(p.Y, r.Top, r.Bottom) };
        return Math.Pow((double)p.X - x, 2) + Math.Pow((double)p.Y - y, 2);
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!pressed) return;
        var wasDragging = dragging;
        pressed = false;
        Surface.ReleaseMouseCapture();
        if (wasDragging) SavePosition();
        else OpenRequested?.Invoke();
        if (!Surface.IsMouseOver) Surface_MouseLeave(sender, e);
        e.Handled = true;
    }

    private void Surface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (pressed && dragging) SavePosition();
        pressed = false;
        dragging = false;
        if (!Surface.IsMouseOver) Surface_MouseLeave(sender, e);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}

public enum LauncherEdge { Top, Bottom, Left, Right }

public sealed record LauncherPosition(string Monitor, LauncherEdge Edge, double Fraction);
