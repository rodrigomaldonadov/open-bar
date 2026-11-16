using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using OpenBar.Application.Widgets;

namespace OpenBar.App.Services;

/// <summary>
/// Host implementation of the widget window port. All window creation,
/// closing and dictionary access is marshalled to the UI thread; the runtime
/// can call it from its background scheduled tasks.
/// </summary>
public sealed partial class WidgetWindowService : IWidgetWindowService, IAsyncDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<WidgetWindowService> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, WidgetWindow> _windows = new();
    private bool _disposed;

    public WidgetWindowService(DispatcherQueue dispatcherQueue, ILogger<WidgetWindowService> logger)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _dispatcherQueue = dispatcherQueue;
        _logger = logger;
    }

    public event EventHandler<WidgetWindowCloseRequestedEventArgs>? CloseRequested;

    public Task OpenAsync(WidgetWindowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunOnUiAsync(() => OpenCore(request), cancellationToken);
    }

    public Task CloseAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => RunOnUiAsync(() => CloseCore(instanceId), cancellationToken);

    public Task CloseAllAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(CloseAllCore, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await CloseAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCloseAllFailed(_logger, ex);
        }

        _disposed = true;
    }

    private void OpenCore(WidgetWindowRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            if (_windows.ContainsKey(request.InstanceId))
            {
                LogWindowAlreadyOpen(_logger, request.InstanceId);
                return;
            }
        }

        WidgetWindow? window = null;

        try
        {
            window = new WidgetWindow(request, OnWindowClosedByUser);

            lock (_sync)
            {
                _windows[request.InstanceId] = window;
            }

            window.AppWindow.Show();
            LogWindowOpened(_logger, request.InstanceId, request.WidgetId, request.Title);
        }
        catch
        {
            if (window is not null)
            {
                lock (_sync)
                {
                    _windows.Remove(request.InstanceId);
                }

                try
                {
                    window.CloseProgrammatically();
                }
                catch (Exception closeEx)
                {
                    LogRollbackCloseFailed(_logger, request.InstanceId, closeEx);
                }
            }

            throw;
        }
    }

    private void CloseCore(Guid instanceId)
    {
        WidgetWindow? window;
        lock (_sync)
        {
            if (!_windows.Remove(instanceId, out window))
            {
                return;
            }
        }

        window.CloseProgrammatically();
        LogWindowClosed(_logger, instanceId);
    }

    private void CloseAllCore()
    {
        WidgetWindow[] windows;
        lock (_sync)
        {
            windows = _windows.Values.ToArray();
            _windows.Clear();
        }

        foreach (var window in windows)
        {
            try
            {
                window.CloseProgrammatically();
            }
            catch (Exception ex)
            {
                LogWindowCloseFailed(_logger, ex);
            }
        }
    }

    private void OnWindowClosedByUser(Guid instanceId)
    {
        lock (_sync)
        {
            _windows.Remove(instanceId);
        }

        LogWindowClosedByUser(_logger, instanceId);
        CloseRequested?.Invoke(this, new WidgetWindowCloseRequestedEventArgs(instanceId));
    }

    private Task RunOnUiAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException("The WinUI dispatcher is not available."));
        }

        return completion.Task;
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "Widget window {InstanceId} opened for {WidgetId} ({Title}).")]
    private static partial void LogWindowOpened(
        ILogger<WidgetWindowService> logger,
        Guid instanceId,
        string widgetId,
        string title);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Debug, Message = "Widget window {InstanceId} is already open.")]
    private static partial void LogWindowAlreadyOpen(ILogger<WidgetWindowService> logger, Guid instanceId);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Information, Message = "Widget window {InstanceId} closed.")]
    private static partial void LogWindowClosed(ILogger<WidgetWindowService> logger, Guid instanceId);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Information, Message = "The user closed widget window {InstanceId}.")]
    private static partial void LogWindowClosedByUser(ILogger<WidgetWindowService> logger, Guid instanceId);

    [LoggerMessage(EventId = 6005, Level = LogLevel.Error, Message = "A widget window failed to close.")]
    private static partial void LogWindowCloseFailed(ILogger<WidgetWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 6006, Level = LogLevel.Error, Message = "Failed to roll back widget window {InstanceId} after an open failure.")]
    private static partial void LogRollbackCloseFailed(
        ILogger<WidgetWindowService> logger,
        Guid instanceId,
        Exception exception);

    [LoggerMessage(EventId = 6007, Level = LogLevel.Error, Message = "Failed to close all widget windows.")]
    private static partial void LogCloseAllFailed(ILogger<WidgetWindowService> logger, Exception exception);
}
