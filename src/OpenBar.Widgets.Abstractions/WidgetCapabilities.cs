namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Optional features a widget supports. Capabilities are declarative metadata:
/// the runtime uses them to decide which operations are allowed, but a widget
/// can still ignore a capability it declares.
/// </summary>
[Flags]
public enum WidgetCapabilities
{
    None = 0,

    /// <summary>The widget exposes a JSON configuration that can be updated.</summary>
    Settings = 1 << 0,

    /// <summary>The widget uses the shared scheduler for periodic updates.</summary>
    PeriodicUpdates = 1 << 1,

    /// <summary>The widget accepts commands from the host or the user.</summary>
    Commands = 1 << 2,

    /// <summary>The widget may publish user-visible notifications.</summary>
    Notifications = 1 << 3,

    All = Settings | PeriodicUpdates | Commands | Notifications
}
