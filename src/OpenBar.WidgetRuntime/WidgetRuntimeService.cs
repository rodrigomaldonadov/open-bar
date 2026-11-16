using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Persistence;
using OpenBar.Application.Widgets;
using OpenBar.Domain.Widgets;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Central lifecycle manager: recovers persisted instances, serializes
/// operations, isolates widget failures and keeps the persisted configuration
/// intact when a widget cannot start.
/// </summary>
public sealed partial class WidgetRuntimeService : IWidgetRuntime, IAsyncDisposable
{
    private readonly IWidgetInstanceService _instances;
    private readonly IWidgetRegistry _registry;
    private readonly IWidgetWindowService _windows;
    private readonly WidgetScheduler _scheduler;
    private readonly ILogger<WidgetRuntimeService> _logger;
    private readonly ConcurrentDictionary<Guid, WidgetInstanceRuntime> _entries = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);

    private int _started;
    private int _disposed;

    public WidgetRuntimeService(
        IWidgetInstanceService instances,
        IWidgetRegistry registry,
        IWidgetWindowService windows,
        WidgetScheduler scheduler,
        ILogger<WidgetRuntimeService> logger)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(scheduler);

        _instances = instances;
        _registry = registry;
        _windows = windows;
        _scheduler = scheduler;
        _logger = logger;

        _windows.CloseRequested += OnWindowCloseRequested;
    }

    public async Task<WidgetStartupReport> StartAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) != 0)
            {
                return new WidgetStartupReport(0, 0, 0, [], AlreadyStarted: true);
            }

            IReadOnlyList<WidgetInstance> persisted;
            try
            {
                persisted = await _instances.GetAllAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new WidgetRuntimeException("Could not load persisted widget instances.", ex);
            }

            var started = 0;
            var skippedDisabled = 0;
            var unavailable = 0;
            var failures = new List<WidgetStartupFailure>();

            foreach (var entity in persisted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_entries.ContainsKey(entity.Id))
                {
                    continue;
                }

                var definition = _registry.Find(entity.WidgetId);
                var entry = new WidgetInstanceRuntime(definition, entity, _scheduler, _logger);
                _entries[entity.Id] = entry;

                if (definition is null)
                {
                    unavailable++;
                    LogUnavailableInstance(_logger, entity.Id, entity.InstanceName, entity.WidgetId);
                    continue;
                }

                if (!entity.IsEnabled)
                {
                    skippedDisabled++;
                    continue;
                }

                try
                {
                    await StartAndOpenAsync(entry, cancellationToken).ConfigureAwait(false);
                    started++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add(new WidgetStartupFailure(entity.Id, entity.WidgetId, entity.InstanceName, ex.Message));
                    LogInstanceStartFailed(_logger, entity.Id, entity.InstanceName, ex);
                }
            }

            Volatile.Write(ref _started, 1);

            LogRecovered(
                _logger,
                started,
                skippedDisabled,
                unavailable,
                failures.Count);

            return new WidgetStartupReport(started, skippedDisabled, unavailable, failures);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await StopAllCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WidgetInstanceInfo> CreateAsync(
        string widgetId,
        string? instanceName = null,
        string? settingsJson = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(widgetId))
        {
            throw new WidgetValidationException("A widget id is required.", nameof(widgetId));
        }

        var definition = _registry.Find(widgetId)
            ?? throw new WidgetValidationException($"Widget '{widgetId}' is not available.", nameof(widgetId));

        IReadOnlyList<WidgetInstance> existing;
        try
        {
            existing = await _instances.GetByWidgetIdAsync(widgetId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WidgetRuntimeException($"Could not load existing instances of widget '{widgetId}'.", ex);
        }

        if (!definition.Metadata.AllowsMultipleInstances && existing.Count > 0)
        {
            throw new WidgetValidationException(
                $"Widget '{definition.Metadata.DisplayName}' does not allow multiple instances.");
        }

        var name = ResolveInstanceName(definition, instanceName, existing);
        var json = settingsJson ?? definition.DefaultSettingsJson;
        _ = ParseSettings(json, definition.SettingsSchemaVersion, name);

        var entity = new WidgetInstance
        {
            WidgetId = definition.WidgetId,
            InstanceName = name,
            IsEnabled = true,
            Width = definition.Metadata.DefaultWidth,
            Height = definition.Metadata.DefaultHeight,
            SettingsJson = json,
            SettingsSchemaVersion = definition.SettingsSchemaVersion
        };

        try
        {
            entity = await _instances.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WidgetLifecycleException(
                null,
                "create",
                $"Could not persist widget instance '{name}': {ex.Message}",
                ex);
        }

        var entry = new WidgetInstanceRuntime(definition, entity, _scheduler, _logger);
        _entries[entity.Id] = entry;

        try
        {
            await StartAndOpenAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The persisted row and its configuration are intentionally kept so
            // the user can retry, inspect or delete the instance.
            LogCreateFailed(_logger, entity.Id, name, ex);
            throw new WidgetLifecycleException(
                entity.Id,
                "create",
                $"Widget instance '{name}' was persisted but could not start: {ex.Message}",
                ex);
        }

        LogInstanceCreated(_logger, entity.Id, name, definition.WidgetId);
        return entry.ToInfo();
    }

    public async Task StartAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = GetEntryOrThrow(instanceId);
        await StartAndOpenAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> StopAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = GetEntryOrThrow(instanceId);
        var success = await entry.StopAsync(cancellationToken).ConfigureAwait(false);
        await CloseWindowAsync(instanceId, cancellationToken).ConfigureAwait(false);
        return success;
    }

    public async Task RestartAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = GetEntryOrThrow(instanceId);

        var stopped = await entry.StopAsync(cancellationToken).ConfigureAwait(false);
        await CloseWindowAsync(instanceId, cancellationToken).ConfigureAwait(false);

        if (!stopped)
        {
            throw new WidgetLifecycleException(
                instanceId,
                "restart",
                "The widget could not be stopped cleanly; it was not restarted.");
        }

        await StartAndOpenAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _entries.TryGetValue(instanceId, out var entry);

        if (entry is not null)
        {
            var stopped = await entry.StopAsync(cancellationToken).ConfigureAwait(false);
            await CloseWindowAsync(instanceId, cancellationToken).ConfigureAwait(false);

            if (!stopped)
            {
                throw new WidgetLifecycleException(
                    instanceId,
                    "delete",
                    "The widget could not be stopped cleanly; the persisted instance was not deleted.");
            }
        }

        var deleted = await _instances.DeleteAsync(instanceId, cancellationToken).ConfigureAwait(false);

        if (entry is not null && _entries.TryRemove(instanceId, out _))
        {
            await entry.DisposeAsync().ConfigureAwait(false);
        }

        if (deleted)
        {
            LogInstanceDeleted(_logger, instanceId);
        }

        return deleted;
    }

    public async Task SetEnabledAsync(Guid instanceId, bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var entity = await _instances.GetAsync(instanceId, cancellationToken).ConfigureAwait(false)
            ?? throw new WidgetNotFoundException(instanceId);

        _entries.TryGetValue(instanceId, out var entry);

        if (!enabled)
        {
            if (entry is not null)
            {
                var stopped = await entry.StopAsync(cancellationToken).ConfigureAwait(false);
                await CloseWindowAsync(instanceId, cancellationToken).ConfigureAwait(false);

                if (!stopped)
                {
                    throw new WidgetLifecycleException(
                        instanceId,
                        "disable",
                        "The widget could not be stopped cleanly; it remains enabled.");
                }
            }

            await _instances.SetEnabledAsync(instanceId, false, cancellationToken).ConfigureAwait(false);
            entity.IsEnabled = false;

            if (entry is not null)
            {
                await entry.ApplyEntityAsync(entity, cancellationToken).ConfigureAwait(false);
            }

            LogInstanceDisabled(_logger, instanceId);
            return;
        }

        await _instances.SetEnabledAsync(instanceId, true, cancellationToken).ConfigureAwait(false);
        entity.IsEnabled = true;

        if (entry is null)
        {
            return;
        }

        await entry.ApplyEntityAsync(entity, cancellationToken).ConfigureAwait(false);

        if (!entry.IsAvailable)
        {
            return;
        }

        await StartAndOpenAsync(entry, cancellationToken).ConfigureAwait(false);
        LogInstanceEnabled(_logger, instanceId);
    }

    public async Task UpdateSettingsAsync(
        Guid instanceId,
        string settingsJson,
        int? settingsSchemaVersion = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var entity = await _instances.GetAsync(instanceId, cancellationToken).ConfigureAwait(false)
            ?? throw new WidgetNotFoundException(instanceId);

        var definition = _registry.Find(entity.WidgetId)
            ?? throw new WidgetValidationException(
                $"Widget '{entity.WidgetId}' is not available; its settings cannot be updated.");

        if (!definition.Metadata.Capabilities.HasFlag(WidgetCapabilities.Settings))
        {
            throw new WidgetValidationException($"Widget '{definition.WidgetId}' does not support settings.");
        }

        var version = settingsSchemaVersion ?? entity.SettingsSchemaVersion;
        var settings = ParseSettings(settingsJson, version, entity.InstanceName);

        var updated = await _instances
            .UpdateSettingsAsync(instanceId, settingsJson, version, cancellationToken)
            .ConfigureAwait(false);

        if (_entries.TryGetValue(instanceId, out var entry))
        {
            await entry.ApplySettingsAsync(updated, settings, cancellationToken).ConfigureAwait(false);
        }

        LogSettingsUpdated(_logger, instanceId);
    }

    public IReadOnlyList<WidgetInstanceInfo> GetInstances()
        => _entries.Values
            .Select(entry => entry.ToInfo())
            .OrderBy(info => info.InstanceName, StringComparer.Ordinal)
            .ThenBy(info => info.Id)
            .ToArray();

    public WidgetInstanceInfo? GetInstance(Guid instanceId)
        => _entries.TryGetValue(instanceId, out var entry) ? entry.ToInfo() : null;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _windows.CloseRequested -= OnWindowCloseRequested;

        await StopAllCoreAsync(CancellationToken.None).ConfigureAwait(false);

        foreach (var entry in _entries.Values)
        {
            await entry.DisposeAsync().ConfigureAwait(false);
        }

        _entries.Clear();
        _startGate.Dispose();
    }

    private async Task StopAllCoreAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in _entries.Values.ToArray())
        {
            try
            {
                _ = await entry.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown cancellation: keep stopping the remaining instances.
            }
            catch (Exception ex)
            {
                LogStopAllFailed(_logger, entry.Id, entry.InstanceName, ex);
            }
        }

        try
        {
            await _windows.CloseAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogWindowsCloseFailed(_logger, ex);
        }

        Volatile.Write(ref _started, 0);
    }

    private async Task StartAndOpenAsync(WidgetInstanceRuntime entry, CancellationToken cancellationToken)
    {
        await entry.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _windows.OpenAsync(CreateWindowRequest(entry), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = await entry.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _ = await entry.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await entry.MarkFaultedAsync(ex).ConfigureAwait(false);
            throw new WidgetLifecycleException(
                entry.Id,
                "open window",
                $"Widget '{entry.InstanceName}' could not create its window: {ex.Message}",
                ex);
        }
    }

    private static WidgetWindowRequest CreateWindowRequest(WidgetInstanceRuntime entry)
    {
        var definition = entry.Definition;
        var info = entry.ToInfo();

        return new WidgetWindowRequest
        {
            InstanceId = entry.Id,
            WidgetId = entry.WidgetId,
            Title = $"{entry.InstanceName} · {definition.Metadata.DisplayName}",
            Widget = entry.Widget
                ?? throw new InvalidOperationException($"Widget instance {entry.Id} has no running widget."),
            Width = Clamp(info.Width ?? definition.Metadata.DefaultWidth, definition.Metadata.MinWidth, definition.Metadata.MaxWidth),
            Height = Clamp(info.Height ?? definition.Metadata.DefaultHeight, definition.Metadata.MinHeight, definition.Metadata.MaxHeight),
            PositionX = info.PositionX,
            PositionY = info.PositionY
        };
    }

    private async Task CloseWindowAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        try
        {
            await _windows.CloseAsync(instanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogWindowCloseFailed(_logger, instanceId, ex);
        }
    }

    private WidgetInstanceRuntime GetEntryOrThrow(Guid instanceId)
    {
        if (instanceId == Guid.Empty)
        {
            throw new WidgetNotFoundException(instanceId);
        }

        return _entries.TryGetValue(instanceId, out var entry)
            ? entry
            : throw new WidgetNotFoundException(instanceId);
    }

    private static string ResolveInstanceName(
        WidgetDefinition definition,
        string? requestedName,
        IReadOnlyList<WidgetInstance> existing)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var trimmed = requestedName.Trim();

            if (trimmed.Length > WidgetInstance.MaxInstanceNameLength)
            {
                throw new WidgetValidationException(
                    $"The instance name must be at most {WidgetInstance.MaxInstanceNameLength} characters.",
                    nameof(requestedName));
            }

            if (existing.Any(instance => string.Equals(instance.InstanceName, trimmed, StringComparison.Ordinal)))
            {
                throw new WidgetValidationException(
                    $"An instance of '{definition.Metadata.DisplayName}' named '{trimmed}' already exists.",
                    nameof(requestedName));
            }

            return trimmed;
        }

        var baseName = definition.Metadata.DisplayName.Trim();
        if (baseName.Length > WidgetInstance.MaxInstanceNameLength)
        {
            baseName = baseName[..WidgetInstance.MaxInstanceNameLength];
        }

        if (!existing.Any(instance => string.Equals(instance.InstanceName, baseName, StringComparison.Ordinal)))
        {
            return baseName;
        }

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{baseName} {index}";
            if (candidate.Length > WidgetInstance.MaxInstanceNameLength)
            {
                break;
            }

            if (!existing.Any(instance => string.Equals(instance.InstanceName, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }

        throw new WidgetValidationException(
            $"Could not generate a unique instance name for '{definition.Metadata.DisplayName}'.");
    }

    private static WidgetSettings ParseSettings(string json, int schemaVersion, string instanceName)
    {
        try
        {
            return WidgetSettings.Parse(json, schemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException)
        {
            throw new WidgetValidationException(
                $"Widget instance '{instanceName}' has invalid settings JSON: {ex.Message}");
        }
    }

    private static double Clamp(double value, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;

    private void OnWindowCloseRequested(object? sender, WidgetWindowCloseRequestedEventArgs e)
        => _ = StopAfterWindowCloseAsync(e.InstanceId);

    private async Task StopAfterWindowCloseAsync(Guid instanceId)
    {
        try
        {
            if (_entries.TryGetValue(instanceId, out var entry))
            {
                _ = await entry.StopAsync(CancellationToken.None).ConfigureAwait(false);
                LogWindowClosedByUser(_logger, instanceId, entry.InstanceName);
            }
        }
        catch (Exception ex)
        {
            LogWindowCloseStopFailed(_logger, instanceId, ex);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    [LoggerMessage(EventId = 5301, Level = LogLevel.Warning, Message = "Widget instance {InstanceId} ({InstanceName}) references unavailable widget type {WidgetId}; its configuration is preserved.")]
    private static partial void LogUnavailableInstance(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        string instanceName,
        string widgetId);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Information, Message = "Widget runtime recovered {Started} started, {Disabled} disabled and {Unavailable} unavailable instance(s); {FailureCount} failure(s).")]
    private static partial void LogRecovered(
        ILogger<WidgetRuntimeService> logger,
        int started,
        int disabled,
        int unavailable,
        int failureCount);

    [LoggerMessage(EventId = 5303, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) failed during startup recovery.")]
    private static partial void LogInstanceStartFailed(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        string instanceName,
        Exception exception);

    [LoggerMessage(EventId = 5304, Level = LogLevel.Information, Message = "Widget instance {InstanceId} ({InstanceName}) created for widget {WidgetId}.")]
    private static partial void LogInstanceCreated(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        string instanceName,
        string widgetId);

    [LoggerMessage(EventId = 5305, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) could not be created.")]
    private static partial void LogCreateFailed(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        string instanceName,
        Exception exception);

    [LoggerMessage(EventId = 5306, Level = LogLevel.Information, Message = "Widget instance {InstanceId} deleted.")]
    private static partial void LogInstanceDeleted(ILogger<WidgetRuntimeService> logger, Guid instanceId);

    [LoggerMessage(EventId = 5307, Level = LogLevel.Information, Message = "Widget instance {InstanceId} disabled; its configuration is preserved.")]
    private static partial void LogInstanceDisabled(ILogger<WidgetRuntimeService> logger, Guid instanceId);

    [LoggerMessage(EventId = 5308, Level = LogLevel.Information, Message = "Widget instance {InstanceId} enabled and started.")]
    private static partial void LogInstanceEnabled(ILogger<WidgetRuntimeService> logger, Guid instanceId);

    [LoggerMessage(EventId = 5309, Level = LogLevel.Information, Message = "Settings updated for widget instance {InstanceId}.")]
    private static partial void LogSettingsUpdated(ILogger<WidgetRuntimeService> logger, Guid instanceId);

    [LoggerMessage(EventId = 5310, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) failed while stopping during shutdown.")]
    private static partial void LogStopAllFailed(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        string instanceName,
        Exception exception);

    [LoggerMessage(EventId = 5311, Level = LogLevel.Error, Message = "Widget windows could not be closed during shutdown.")]
    private static partial void LogWindowsCloseFailed(ILogger<WidgetRuntimeService> logger, Exception exception);

    [LoggerMessage(EventId = 5312, Level = LogLevel.Error, Message = "Widget window {InstanceId} could not be closed.")]
    private static partial void LogWindowCloseFailed(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        Exception exception);

    [LoggerMessage(EventId = 5313, Level = LogLevel.Information, Message = "Widget instance {InstanceId} ({InstanceName}) stopped because the user closed its window.")]
    private static partial void LogWindowClosedByUser(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        string instanceName);

    [LoggerMessage(EventId = 5314, Level = LogLevel.Error, Message = "Widget instance {InstanceId} failed to stop after its window was closed.")]
    private static partial void LogWindowCloseStopFailed(
        ILogger<WidgetRuntimeService> logger,
        Guid instanceId,
        Exception exception);
}
