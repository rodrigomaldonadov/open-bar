using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenBar.App.Services;
using OpenBar.Application.Lifecycle;
using OpenBar.Application.Persistence;
using OpenBar.Application.Dock;
using OpenBar.Application.Startup;
using OpenBar.Application.Widgets;
using OpenBar.Application.Windows;
using OpenBar.Dock;
using OpenBar.Infrastructure.Persistence;
using OpenBar.Infrastructure.Windows.Dock;
using OpenBar.Infrastructure.Windows.SingleInstance;
using OpenBar.Infrastructure.Windows.SystemTray;
using OpenBar.Presentation.Dock;
using OpenBar.WidgetRuntime;
using OpenBar.Widgets.Clock;

namespace OpenBar.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly DispatcherQueue _dispatcherQueue;

    private IHost? _host;
    private DockWindowService? _windowService;
    private DockToolsWindow? _dockTools;
    private IApplicationLifecycle? _lifecycle;
    private ISingleInstanceService? _singleInstance;
    private ILogger<App>? _logger;
    private int _shutdownStarted;

    public App()
    {
        InitializeComponent();

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _host = BuildHost();
            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _singleInstance = _host.Services.GetRequiredService<ISingleInstanceService>();
            _host.Services.GetRequiredService<DockView>().OpenToolsAsync = OpenDockToolsAsync;

            if (!_singleInstance.TryAcquire())
            {
                bool notified;
                try
                {
                    notified = await _singleInstance.NotifyExistingInstanceAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    LogNotifyExistingInstanceFailed(_logger, ex);
                    notified = false;
                }

                if (notified)
                {
                    LogSecondInstanceActivatedExisting(_logger);
                    await DisposeHostAsync(wasStarted: false);
                    ExitApplication();
                    return;
                }

                // The previous process may have exited between the mutex check
                // and the pipe connection (for example after a crash). Retry
                // acquisition once before giving up.
                await Task.Delay(250);
                if (!_singleInstance.TryAcquire())
                {
                    LogExistingInstanceDidNotRespond(_logger);
                    await DisposeHostAsync(wasStarted: false);
                    ExitApplication();
                    return;
                }

                LogPreviousInstanceExited(_logger);
            }

            _windowService = _host.Services.GetRequiredService<DockWindowService>();
            _lifecycle = _host.Services.GetRequiredService<IApplicationLifecycle>();
            _lifecycle.ShutdownRequested += OnShutdownRequested;
            _singleInstance.ActivationRequested += OnActivationRequested;
            _lifecycle.MarkRunning();

            // Hosted services start in registration order: persistence first,
            // then the single-instance listener, then the tray icon. Persistence
            // initialization runs off the UI thread and completes before the
            // window is shown.
            await _host.StartAsync();

            LogHostStarted(_logger);

            var configuration = _host.Services.GetRequiredService<PersistedConfigurationCache>();
            LogPreferencesLoaded(_logger, configuration.Values.Count);

            await RecordStartupJournalAsync(_host.Services);

            _windowService.ShowAndActivate();
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogStartupFailed(_logger, ex);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"OpenBar failed to start: {ex}");
            }

            WriteStartupFailure(ex);
            await DisposeHostAsync(wasStarted: true);
            ExitApplication();
        }
    }

    private static void WriteStartupFailure(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenBar");
            _ = Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-error.log"),
                $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private IHost BuildHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices((_, services) =>
            {
                services.Configure<HostOptions>(options =>
                {
                    options.ShutdownTimeout = TimeSpan.FromSeconds(5);
                });

                services.AddSingleton(new SingleInstanceOptions
                {
                    ApplicationId = "OpenBar"
                });

                services.AddSingleton(new TrayIconOptions
                {
                    ToolTip = "OpenBar",
                    IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenBar.ico")
                });

                services.AddSingleton<IApplicationLifecycle, ApplicationLifecycleService>();
                services.AddSingleton<TrayCommandController>();

                // Persistence initializes before the single-instance listener
                // and the tray so no service observes an uninitialized database.
                services.AddOpenBarPersistence();
                services.AddSingleton<ApplicationStartupJournal>();

                // Dock services: platform-neutral engine (OpenBar.Dock) plus
                // Windows adapters (Start Menu, shortcut resolver, windows).
                services.AddOpenBarDockInfrastructure();
                services.AddOpenBarDock();
                services.AddSingleton<DockIconService>();
                services.AddSingleton<DockViewModel>(provider =>
                {
                    var viewModel = new DockViewModel(
                        provider.GetRequiredService<IDockController>(),
                        provider.GetRequiredService<DockIconService>(),
                        provider.GetRequiredService<ILogger<DockViewModel>>());

                    viewModel.AddTestWidget = () =>
                        provider.GetRequiredService<IWidgetRuntime>()
                            .CreateAsync(ClockWidgetDefinition.WidgetId);

                    return viewModel;
                });
                services.AddSingleton<DockView>();

                // The runtime starts after persistence (hosted-service order) and
                // before the tray/single-instance listeners, so recovered widgets
                // are ready when the dock becomes visible. It stops before
                // persistence during shutdown.
                services.AddSingleton(ClockWidgetDefinition.Create());
                services.AddSingleton<WidgetWindowService>(provider => new WidgetWindowService(
                    _dispatcherQueue,
                    provider.GetRequiredService<ILogger<WidgetWindowService>>()));
                services.AddSingleton<IWidgetWindowService>(
                    provider => provider.GetRequiredService<WidgetWindowService>());
                services.AddOpenBarWidgetRuntime();

                services.AddSingleton<DockWindow>();
                services.AddSingleton<DockWindowService>();
                services.AddSingleton<IWindowManagementService>(sp => sp.GetRequiredService<DockWindowService>());

                services.AddSingleton<WindowsSingleInstanceService>();
                services.AddSingleton<ISingleInstanceService>(sp => sp.GetRequiredService<WindowsSingleInstanceService>());
                services.AddHostedService(sp => sp.GetRequiredService<WindowsSingleInstanceService>());

                services.AddSingleton<WindowsSystemTrayService>();
                services.AddSingleton<ISystemTrayService>(sp => sp.GetRequiredService<WindowsSystemTrayService>());
                services.AddHostedService(sp => sp.GetRequiredService<WindowsSystemTrayService>());
            })
            .Build();
    }

    private Task OpenDockToolsAsync()
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            OpenDockTools();
            return Task.CompletedTask;
        }

        _ = _dispatcherQueue.TryEnqueue(OpenDockTools);
        return Task.CompletedTask;
    }

    private void OpenDockTools()
    {
        var host = _host;
        if (host is null)
        {
            return;
        }

        if (_dockTools is null)
        {
            _dockTools = new DockToolsWindow(
                host.Services.GetRequiredService<DockViewModel>(),
                host.Services.GetRequiredService<IWidgetRuntime>());

            _dockTools.Closed += (_, _) => _dockTools = null;
        }

        _dockTools.Activate();
    }

    private void OnActivationRequested(object? sender, EventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_logger is not null)
                {
                    LogActivationRequested(_logger);
                }

                _windowService?.ShowAndActivate();
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogActivationFailed(_logger, ex);
                }
            }
        });
    }

    private void OnShutdownRequested(object? sender, ApplicationShutdownRequestedEventArgs e)
    {
        if (_logger is not null)
        {
            LogShutdownRequested(_logger, e.Reason);
        }

        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _ = ShutdownAsync(e.Reason);
    }

    private async Task ShutdownAsync(string reason)
    {
        var host = _host;
        _host = null;

        if (host is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await host.StopAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (_logger is not null)
                {
                    LogShutdownHostStopTimedOut(_logger, reason);
                }
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogShutdownHostStopFailed(_logger, ex, reason);
                }
            }
        }

        void CompleteShutdown()
        {
            try
            {
                _dockTools?.Close();
                _dockTools = null;
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogShutdownWindowCloseFailed(_logger, ex);
                }
            }

            try
            {
                _windowService?.CloseForShutdown();
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogShutdownWindowCloseFailed(_logger, ex);
                }
            }

            try
            {
                _lifecycle?.MarkStopped();
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogShutdownLifecycleStopFailed(_logger, ex);
                }
            }

            try
            {
                host?.Dispose();
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogShutdownHostDisposeFailed(_logger, ex);
                }
            }

            Exit();
        }

        if (!_dispatcherQueue.TryEnqueue(CompleteShutdown))
        {
            // The dispatcher is already shutting down; finish synchronously.
            CompleteShutdown();
        }
    }

    private async Task DisposeHostAsync(bool wasStarted)
    {
        var host = _host;
        _host = null;

        if (host is null)
        {
            return;
        }

        if (wasStarted)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await host.StopAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (_logger is not null)
                {
                    LogStartupHostStopTimedOut(_logger);
                }
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogStartupHostStopFailed(_logger, ex);
                }
            }
        }

        try
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogStartupHostDisposeFailed(_logger, ex);
            }
        }
    }

    private void ExitApplication()
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            Exit();
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(Exit))
        {
            Exit();
        }
    }

    private static async Task RecordStartupJournalAsync(IServiceProvider services)
    {
        var journal = services.GetRequiredService<ApplicationStartupJournal>();
        var logger = services.GetRequiredService<ILogger<App>>();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // SQLite is synchronous under the provider's async surface; run the
            // journal write on a worker so the UI thread is never blocked.
            var entry = await Task.Run(
                () => journal.RecordStartAsync(DateTimeOffset.UtcNow, timeout.Token),
                timeout.Token);

            LogStartupJournalRecorded(logger, entry.StartCount, entry.PreviousStartedAtUtc);
        }
        catch (OperationCanceledException)
        {
            LogStartupJournalTimedOut(logger);
        }
        catch (Exception ex)
        {
            // The startup journal is diagnostic state; a failure here must not
            // prevent the dock from opening.
            LogStartupJournalFailed(logger, ex);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            LogUnhandledWinUiException(_logger, e.Exception);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled WinUI exception: {e.Exception}");
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Failed to notify the existing OpenBar instance.")]
    private static partial void LogNotifyExistingInstanceFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "OpenBar is already running. The existing instance was activated; exiting the second instance.")]
    private static partial void LogSecondInstanceActivatedExisting(ILogger<App> logger);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "An existing OpenBar instance did not respond to activation; exiting the second instance.")]
    private static partial void LogExistingInstanceDidNotRespond(ILogger<App> logger);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "The previous OpenBar instance exited before responding; this process became the primary instance.")]
    private static partial void LogPreviousInstanceExited(ILogger<App> logger);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "OpenBar host started; showing the DockWindow.")]
    private static partial void LogHostStarted(ILogger<App> logger);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Critical, Message = "OpenBar failed to start.")]
    private static partial void LogStartupFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information, Message = "Second instance requested activation; bringing the DockWindow forward.")]
    private static partial void LogActivationRequested(ILogger<App> logger);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Error, Message = "Failed to activate the DockWindow for a second instance.")]
    private static partial void LogActivationFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Information, Message = "Controlled shutdown requested. Reason: {Reason}.")]
    private static partial void LogShutdownRequested(ILogger<App> logger, string reason);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Warning, Message = "Host stop timed out during shutdown. Reason: {Reason}.")]
    private static partial void LogShutdownHostStopTimedOut(ILogger<App> logger, string reason);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Error, Message = "Host stop failed during shutdown. Reason: {Reason}.")]
    private static partial void LogShutdownHostStopFailed(ILogger<App> logger, Exception exception, string reason);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Error, Message = "Failed to close the DockWindow during shutdown.")]
    private static partial void LogShutdownWindowCloseFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Error, Message = "Failed to mark the application lifecycle as stopped.")]
    private static partial void LogShutdownLifecycleStopFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Error, Message = "Host disposal failed during shutdown.")]
    private static partial void LogShutdownHostDisposeFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Warning, Message = "Host stop timed out after a startup failure.")]
    private static partial void LogStartupHostStopTimedOut(ILogger<App> logger);

    [LoggerMessage(EventId = 3016, Level = LogLevel.Error, Message = "Host stop failed after a startup failure.")]
    private static partial void LogStartupHostStopFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3017, Level = LogLevel.Error, Message = "Host disposal failed after a startup failure.")]
    private static partial void LogStartupHostDisposeFailed(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3018, Level = LogLevel.Critical, Message = "Unhandled WinUI exception.")]
    private static partial void LogUnhandledWinUiException(ILogger<App> logger, Exception exception);

    [LoggerMessage(EventId = 3019, Level = LogLevel.Information, Message = "Loaded {SettingCount} persisted setting(s) from SQLite.")]
    private static partial void LogPreferencesLoaded(ILogger<App> logger, int settingCount);

    [LoggerMessage(EventId = 3020, Level = LogLevel.Information, Message = "Startup journal recorded: start #{StartCount}; previous start: {PreviousStartedAtUtc}.")]
    private static partial void LogStartupJournalRecorded(
        ILogger<App> logger,
        int startCount,
        DateTimeOffset? previousStartedAtUtc);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Warning, Message = "The startup journal could not be recorded within the timeout.")]
    private static partial void LogStartupJournalTimedOut(ILogger<App> logger);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Warning, Message = "The startup journal could not be recorded; the dock will still open.")]
    private static partial void LogStartupJournalFailed(ILogger<App> logger, Exception exception);
}
