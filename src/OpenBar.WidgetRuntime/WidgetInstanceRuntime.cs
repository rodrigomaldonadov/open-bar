using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBar.Domain.Widgets;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Owns the runtime objects of one persisted instance. Every lifecycle
/// operation is serialized with a per-instance gate; a fresh widget object and
/// scheduler scope are created on each start and released on stop.
/// </summary>
internal sealed partial class WidgetInstanceRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly WidgetDefinition? _definition;
    private readonly WidgetScheduler _scheduler;
    private readonly OpenBar.SystemBridge.ISystemMetrics _metrics;
    private readonly ILogger _logger;

    private WidgetInstance _entity;
    private IWidget? _widget;
    private WidgetContext? _context;
    private WidgetSchedulerScope? _schedulerScope;
    private volatile WidgetInstanceState _state;
    private string? _lastError;

    public WidgetInstanceRuntime(
        WidgetDefinition? definition,
        WidgetInstance entity,
        WidgetScheduler scheduler,
        OpenBar.SystemBridge.ISystemMetrics metrics,
        ILogger logger)
    {
        _definition = definition;
        _entity = entity;
        _scheduler = scheduler;
        _metrics = metrics;
        _logger = logger;
        _state = definition is null ? WidgetInstanceState.Unavailable : WidgetInstanceState.Created;
    }

    public Guid Id => _entity.Id;

    public string WidgetId => _entity.WidgetId;

    public string InstanceName => _entity.InstanceName;

    public bool IsAvailable => _definition is not null;

    public WidgetInstanceState State => _state;

    public WidgetDefinition Definition
        => _definition ?? throw new InvalidOperationException("This instance has no available widget definition.");

    public IWidget? Widget => _widget;

    public WidgetInstanceInfo ToInfo()
        => new(
            _entity.Id,
            _entity.WidgetId,
            _entity.InstanceName,
            _state,
            _entity.IsEnabled,
            IsAvailable,
            _lastError,
            _entity.PositionX,
            _entity.PositionY,
            _entity.Width,
            _entity.Height);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == WidgetInstanceState.Running)
            {
                return;
            }

            if (_state is WidgetInstanceState.Disposed or WidgetInstanceState.Unavailable)
            {
                throw new WidgetStateException(Id, _state, "start");
            }

            if (_state == WidgetInstanceState.Faulted)
            {
                throw new WidgetStateException(Id, _state, "start (use restart)");
            }

            if (_state is WidgetInstanceState.Initializing
                or WidgetInstanceState.Initialized
                or WidgetInstanceState.Starting
                or WidgetInstanceState.Stopping)
            {
                throw new WidgetStateException(Id, _state, "start");
            }

            _lastError = null;
            Transition(WidgetInstanceState.Initializing);

            try
            {
                _schedulerScope = _scheduler.CreateScope(Id);
                _widget = _definition!.CreateWidget()
                    ?? throw new InvalidOperationException("The widget factory returned null.");

                _context = new WidgetContext
                {
                    InstanceId = Id,
                    WidgetId = WidgetId,
                    InstanceName = InstanceName,
                    Logger = _logger,
                    Scheduler = _schedulerScope,
                    Metrics = _metrics,
                    Settings = ParseSettings()
                };

                await _widget.InitializeAsync(_context, cancellationToken).ConfigureAwait(false);
                Transition(WidgetInstanceState.Initialized);

                Transition(WidgetInstanceState.Starting);
                await _widget.StartAsync(cancellationToken).ConfigureAwait(false);
                Transition(WidgetInstanceState.Running);
            }
            catch (OperationCanceledException)
            {
                await CleanupFailedStartAsync().ConfigureAwait(false);
                Transition(WidgetInstanceState.Stopped);
                throw;
            }
            catch (Exception ex)
            {
                await CleanupFailedStartAsync().ConfigureAwait(false);
                _lastError = ex.Message;
                Transition(WidgetInstanceState.Faulted);
                LogStartFailed(_logger, Id, InstanceName, ex);
                throw new WidgetLifecycleException(
                    Id,
                    "start",
                    $"Widget '{InstanceName}' failed to start: {ex.Message}",
                    ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops and releases resources. Returns false when a cleanup step failed.</summary>
    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is WidgetInstanceState.Stopped
                or WidgetInstanceState.Disposed
                or WidgetInstanceState.Unavailable)
            {
                return true;
            }

            if (_state == WidgetInstanceState.Created)
            {
                Transition(WidgetInstanceState.Stopped);
                return true;
            }

            if (_state == WidgetInstanceState.Faulted)
            {
                await ReleaseResourcesAsync(cancellationToken).ConfigureAwait(false);
                Transition(WidgetInstanceState.Stopped);
                return true;
            }

            if (_state is WidgetInstanceState.Initializing
                or WidgetInstanceState.Starting
                or WidgetInstanceState.Stopping)
            {
                throw new WidgetStateException(Id, _state, "stop");
            }

            Transition(WidgetInstanceState.Stopping);
            var success = await ReleaseResourcesAsync(cancellationToken).ConfigureAwait(false);
            Transition(success ? WidgetInstanceState.Stopped : WidgetInstanceState.Faulted);
            return success;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyEntityAsync(WidgetInstance entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _entity = entity;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplySettingsAsync(
        WidgetInstance entity,
        WidgetSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _entity = entity;
            _context?.UpdateSettings(settings);

            if (_widget is not IWidgetSettingsConsumer consumer)
            {
                return;
            }

            try
            {
                await consumer.OnSettingsChangedAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await ReleaseResourcesAsync(CancellationToken.None).ConfigureAwait(false);
                _lastError = ex.Message;
                Transition(WidgetInstanceState.Faulted);
                LogSettingsFailed(_logger, Id, InstanceName, ex);
                throw new WidgetLifecycleException(
                    Id,
                    "update settings",
                    $"Widget '{InstanceName}' rejected the new settings: {ex.Message}",
                    ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkFaultedAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _lastError = exception.Message;
            if (_state != WidgetInstanceState.Disposed)
            {
                Transition(WidgetInstanceState.Faulted);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_state == WidgetInstanceState.Disposed)
            {
                return;
            }

            if (_state is not WidgetInstanceState.Created
                and not WidgetInstanceState.Stopped
                and not WidgetInstanceState.Unavailable)
            {
                await ReleaseResourcesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Transition(WidgetInstanceState.Disposed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ReleaseResourcesAsync(CancellationToken cancellationToken)
    {
        var failed = false;

        if (_schedulerScope is not null)
        {
            try
            {
                await _schedulerScope.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failed = true;
                LogSchedulerReleaseFailed(_logger, Id, InstanceName, ex);
            }
            finally
            {
                _schedulerScope = null;
            }
        }

        if (_widget is not null)
        {
            try
            {
                await _widget.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failed = true;
                LogStopFailed(_logger, Id, InstanceName, ex);
            }

            try
            {
                await _widget.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failed = true;
                LogDisposeFailed(_logger, Id, InstanceName, ex);
            }

            _widget = null;
            _context = null;
        }

        return !failed;
    }

    private async Task CleanupFailedStartAsync()
    {
        _ = await ReleaseResourcesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private WidgetSettings ParseSettings()
    {
        try
        {
            return WidgetSettings.Parse(_entity.SettingsJson, _entity.SettingsSchemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException)
        {
            throw new WidgetValidationException(
                $"Widget instance '{InstanceName}' has invalid persisted settings: {ex.Message}");
        }
    }

    private void Transition(WidgetInstanceState next)
    {
        if (!IsTransitionAllowed(_state, next))
        {
            throw new WidgetStateException(Id, _state, $"transition to {next}");
        }

        _state = next;
    }

    private static bool IsTransitionAllowed(WidgetInstanceState current, WidgetInstanceState next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            WidgetInstanceState.Created => next is WidgetInstanceState.Initializing
                or WidgetInstanceState.Stopped
                or WidgetInstanceState.Faulted
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Initializing => next is WidgetInstanceState.Initialized
                or WidgetInstanceState.Stopped
                or WidgetInstanceState.Faulted
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Initialized => next is WidgetInstanceState.Starting
                or WidgetInstanceState.Stopping
                or WidgetInstanceState.Stopped
                or WidgetInstanceState.Faulted
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Starting => next is WidgetInstanceState.Running
                or WidgetInstanceState.Stopped
                or WidgetInstanceState.Faulted
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Running => next is WidgetInstanceState.Stopping
                or WidgetInstanceState.Faulted
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Stopping => next is WidgetInstanceState.Stopped
                or WidgetInstanceState.Faulted
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Stopped => next is WidgetInstanceState.Initializing
                or WidgetInstanceState.Faulted
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Faulted => next is WidgetInstanceState.Stopped
                or WidgetInstanceState.Disposed,
            WidgetInstanceState.Unavailable => next is WidgetInstanceState.Disposed,
            _ => false
        };
    }

    [LoggerMessage(EventId = 5201, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) failed to start.")]
    private static partial void LogStartFailed(ILogger logger, Guid instanceId, string instanceName, Exception exception);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) failed while stopping.")]
    private static partial void LogStopFailed(ILogger logger, Guid instanceId, string instanceName, Exception exception);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) failed while disposing.")]
    private static partial void LogDisposeFailed(ILogger logger, Guid instanceId, string instanceName, Exception exception);

    [LoggerMessage(EventId = 5204, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) failed while releasing its scheduler.")]
    private static partial void LogSchedulerReleaseFailed(ILogger logger, Guid instanceId, string instanceName, Exception exception);

    [LoggerMessage(EventId = 5205, Level = LogLevel.Error, Message = "Widget instance {InstanceId} ({InstanceName}) failed to apply new settings.")]
    private static partial void LogSettingsFailed(ILogger logger, Guid instanceId, string instanceName, Exception exception);
}
