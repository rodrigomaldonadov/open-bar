using Microsoft.Extensions.Logging;
using OpenBar.SystemBridge;

namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Services and instance data a widget is allowed to use. Deliberately narrow:
/// widgets never receive the application's DI container, database or window
/// manager. Shared services are added only when a concrete widget needs them.
/// </summary>
public sealed class WidgetContext
{
    private WidgetSettings _settings = WidgetSettings.Empty;

    public required Guid InstanceId { get; init; }

    public required string WidgetId { get; init; }

    public required string InstanceName { get; init; }

    public required ILogger Logger { get; init; }

    /// <summary>Per-instance scheduler owned by the runtime.</summary>
    public required IWidgetScheduler Scheduler { get; init; }

    /// <summary>
    /// Shared System Bridge measurements. The default no-op implementation is
    /// used in isolated tests; the runtime supplies the real service.
    /// </summary>
    public ISystemMetrics Metrics { get; init; } = NullSystemMetrics.Instance;

    public WidgetSettings Settings
    {
        get => _settings;
        init => _settings = value;
    }

    public void UpdateSettings(WidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }
}
