namespace OpenBar.WidgetRuntime;

/// <summary>
/// Public lifecycle surface of the widget runtime. All operations are
/// asynchronous, cancelable and isolated per instance.
/// </summary>
public interface IWidgetRuntime
{
    /// <summary>
    /// Discovers definitions and recovers persisted instances. Enabled
    /// instances are started; disabled ones are reported but not started;
    /// instances without a definition are preserved as unavailable.
    /// </summary>
    Task<WidgetStartupReport> StartAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops every active instance and closes its window.</summary>
    Task StopAllAsync(CancellationToken cancellationToken = default);

    Task<WidgetInstanceInfo> CreateAsync(
        string widgetId,
        string? instanceName = null,
        string? settingsJson = null,
        CancellationToken cancellationToken = default);

    Task StartAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Stops one instance. Returns false when cleanup failed.</summary>
    Task<bool> StopAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task RestartAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the widget, closes its window and deletes the persisted row. The
    /// row is not deleted when stopping fails.
    /// </summary>
    Task<bool> DeleteAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the enabled flag and starts or stops the instance. Disabling
    /// stops the widget but keeps its configuration.
    /// </summary>
    Task SetEnabledAsync(Guid instanceId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the presentation mode/order. A mode change stops the current
    /// lifecycle and starts a fresh one in the new host; schedules never overlap.
    /// </summary>
    Task SetPresentationAsync(
        Guid instanceId,
        OpenBar.Domain.Widgets.WidgetPresentationMode presentationMode,
        int presentationOrder,
        CancellationToken cancellationToken = default);

    Task UpdateSettingsAsync(
        Guid instanceId,
        string settingsJson,
        int? settingsSchemaVersion = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<WidgetInstanceInfo> GetInstances();

    WidgetInstanceInfo? GetInstance(Guid instanceId);
}
