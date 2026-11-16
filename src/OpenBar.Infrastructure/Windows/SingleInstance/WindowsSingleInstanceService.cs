using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Windows;

namespace OpenBar.Infrastructure.Windows.SingleInstance;

/// <summary>
/// Windows single-instance implementation.
///
/// Mutual exclusion uses a named mutex created WITHOUT ownership. The first
/// process creates the kernel object and keeps its handle open for its lifetime;
/// subsequent processes observe <c>createdNew == false</c>. Because the mutex is
/// never acquired, there is no thread-affinity problem when the process shuts
/// down on a different thread, and a crashed process releases the object when
/// Windows closes its handles.
///
/// Activation (bringing the existing window forward) uses a per-user named pipe
/// server. The second process connects and sends a one-byte activation request,
/// so activation never depends on discovering a foreign HWND.
/// </summary>
public sealed partial class WindowsSingleInstanceService : ISingleInstanceService, IHostedService, IDisposable, IAsyncDisposable
{
    private const byte ActivationMessage = 0x01;

    private readonly ILogger<WindowsSingleInstanceService> _logger;
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly object _gate = new();

    private Mutex? _mutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _disposed;

    public WindowsSingleInstanceService(SingleInstanceOptions options, ILogger<WindowsSingleInstanceService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;

        var applicationId = Normalize(options.ApplicationId);
        var userScope = GetCurrentUserScope();
        _mutexName = $"Local\\{applicationId}.SingleInstance.{userScope}";
        _pipeName = $"{applicationId}.SingleInstance.{userScope}";
    }

    public event EventHandler? ActivationRequested;

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_mutex is not null)
            {
                return true;
            }

            try
            {
                // initiallyOwned: false is intentional. The mutex is used as a
                // named kernel object marker, not as a lock held by a thread.
                var mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);
                if (!createdNew)
                {
                    mutex.Dispose();
                    LogExistingInstanceOwnsMutex(_logger, _mutexName);
                    return false;
                }

                _mutex = mutex;
                LogMutexAcquired(_logger, _mutexName);
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Another instance created the object with an ACL that denies
                // us access. Treat it as an existing instance rather than
                // risking two primaries.
                LogMutexAccessDenied(_logger, ex, _mutexName);
                return false;
            }
        }
    }

    public async Task<bool> NotifyExistingInstanceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(3);
        var deadline = DateTime.UtcNow + effectiveTimeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                await client.ConnectAsync(250, cancellationToken).ConfigureAwait(false);
                await client.WriteAsync(new[] { ActivationMessage }, cancellationToken).ConfigureAwait(false);
                await client.FlushAsync(cancellationToken).ConfigureAwait(false);

                LogActivationRequestSent(_logger);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                try
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }

        LogActivationNotifyFailed(_logger, lastError, effectiveTimeout);
        return false;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_mutex is null)
            {
                LogListenerNotStartedWithoutMutex(_logger);
                return Task.CompletedTask;
            }

            if (_listenerTask is not null)
            {
                return Task.CompletedTask;
            }

            _listenerCts = new CancellationTokenSource();
            var token = _listenerCts.Token;
            _listenerTask = Task.Run(() => ListenAsync(token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;
        Task? listenerTask;

        lock (_gate)
        {
            cts = _listenerCts;
            listenerTask = _listenerTask;
            _listenerCts = null;
            _listenerTask = null;
        }

        if (cts is null || listenerTask is null)
        {
            return;
        }

        try
        {
            cts.Cancel();

            await listenerTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host may cancel the stop operation; the listener still exits
            // because its own token was cancelled.
        }
        catch (TimeoutException)
        {
            LogListenerStopTimedOut(_logger);
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        LogListenerStarted(_logger, _pipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var buffer = new byte[1];
                var read = await server.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read > 0 && buffer[0] == ActivationMessage)
                {
                    LogActivationReceived(_logger);
                    RaiseActivationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                LogListenerFailedRetrying(_logger, ex);
                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        LogListenerStopped(_logger);
    }

    private void RaiseActivationRequested()
    {
        var handler = ActivationRequested;
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler target in handler.GetInvocationList())
        {
            try
            {
                target(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogActivationHandlerFailed(_logger, ex);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogListenerStopFailedOnDispose(_logger, ex);
        }

        ReleaseMutex();
        ActivationRequested = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogListenerStopFailedOnAsyncDispose(_logger, ex);
        }

        ReleaseMutex();
        ActivationRequested = null;
    }

    private void ReleaseMutex()
    {
        lock (_gate)
        {
            _mutex?.Dispose();
            _mutex = null;
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "OpenBar";
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Length == 0 ? "OpenBar" : normalized;
    }

    private static string GetCurrentUserScope()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (!string.IsNullOrWhiteSpace(sid))
            {
                return new string(sid.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            }
        }
        catch
        {
            // Fall back to the user name below.
        }

        return Normalize(Environment.UserName);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "An existing OpenBar instance already owns the single-instance mutex {MutexName}.")]
    private static partial void LogExistingInstanceOwnsMutex(ILogger<WindowsSingleInstanceService> logger, string mutexName);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Single-instance mutex acquired: {MutexName}.")]
    private static partial void LogMutexAcquired(ILogger<WindowsSingleInstanceService> logger, string mutexName);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Access denied while opening single-instance mutex {MutexName}.")]
    private static partial void LogMutexAccessDenied(ILogger<WindowsSingleInstanceService> logger, Exception exception, string mutexName);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Activation request sent to the existing OpenBar instance.")]
    private static partial void LogActivationRequestSent(ILogger<WindowsSingleInstanceService> logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Could not notify the existing OpenBar instance within {Timeout}.")]
    private static partial void LogActivationNotifyFailed(ILogger<WindowsSingleInstanceService> logger, Exception? exception, TimeSpan timeout);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "Single-instance listener was not started because this process has not acquired the mutex.")]
    private static partial void LogListenerNotStartedWithoutMutex(ILogger<WindowsSingleInstanceService> logger);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "The single-instance activation listener did not stop within the timeout.")]
    private static partial void LogListenerStopTimedOut(ILogger<WindowsSingleInstanceService> logger);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Single-instance activation listener started on pipe {PipeName}.")]
    private static partial void LogListenerStarted(ILogger<WindowsSingleInstanceService> logger, string pipeName);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Activation request received from a second OpenBar instance.")]
    private static partial void LogActivationReceived(ILogger<WindowsSingleInstanceService> logger);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Single-instance activation listener failed; retrying.")]
    private static partial void LogListenerFailedRetrying(ILogger<WindowsSingleInstanceService> logger, Exception exception);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Single-instance activation listener stopped.")]
    private static partial void LogListenerStopped(ILogger<WindowsSingleInstanceService> logger);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "An activation request handler failed.")]
    private static partial void LogActivationHandlerFailed(ILogger<WindowsSingleInstanceService> logger, Exception exception);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Warning, Message = "The single-instance listener did not stop cleanly during disposal.")]
    private static partial void LogListenerStopFailedOnDispose(ILogger<WindowsSingleInstanceService> logger, Exception exception);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Warning, Message = "The single-instance listener did not stop cleanly during async disposal.")]
    private static partial void LogListenerStopFailedOnAsyncDispose(ILogger<WindowsSingleInstanceService> logger, Exception exception);
}
