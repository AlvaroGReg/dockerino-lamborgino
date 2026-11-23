namespace DockerinoLamborgino.Core;

/// <summary>UI-independent visibility state; changing it never affects sessions.</summary>
public sealed class ShellState
{
    public bool IsCollapsed { get; private set; }
    public bool IsMenuVisible { get; private set; }

    public void ToggleCollapsed() => IsCollapsed = !IsCollapsed;
    public void ToggleMenu() => IsMenuVisible = !IsMenuVisible;
}
