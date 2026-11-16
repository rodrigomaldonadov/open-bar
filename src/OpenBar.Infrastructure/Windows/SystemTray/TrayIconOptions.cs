namespace OpenBar.Infrastructure.Windows.SystemTray;

/// <summary>
/// Tray presentation options supplied by the UI layer. <see cref="IconPath"/>
/// points at a project-owned .ico copied next to the executable.
/// </summary>
public sealed class TrayIconOptions
{
    public string ToolTip { get; init; } = "OpenBar";

    public string IconPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenBar.ico");
}
