using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Dock;
using OpenBar.Application.Persistence;
using OpenBar.Domain.Dock;
using OpenBar.Domain.Layouts;

namespace OpenBar.Dock;

/// <inheritdoc cref="IDockController" />
public sealed partial class DockController : IDockController, IDisposable
{
    private readonly IPinnedApplicationService _pinnedApplications;
    private readonly IDockLayoutService _layouts;
    private readonly IApplicationCatalogService _catalog;
    private readonly IShortcutResolver _shortcutResolver;
    private readonly IApplicationLauncher _launcher;
    private readonly IAppWindowService _appWindows;
    private readonly ILogger<DockController> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastLaunch = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<PinnedApplication> _pinned = [];
    private IReadOnlyList<DockItemInfo> _items = [];
    private DockLayout? _layout;
    private bool _initialized;
    private int _disposed;

    public DockController(
        IPinnedApplicationService pinnedApplications,
        IDockLayoutService layouts,
        IApplicationCatalogService catalog,
        IShortcutResolver shortcutResolver,
        IApplicationLauncher launcher,
        IAppWindowService appWindows,
        TimeProvider timeProvider,
        ILogger<DockController> logger)
    {
        _pinnedApplications = pinnedApplications;
        _layouts = layouts;
        _catalog = catalog;
        _shortcutResolver = shortcutResolver;
        _launcher = launcher;
        _appWindows = appWindows;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public DockLayout Layout
        => _layout ?? throw new InvalidOperationException("The dock controller has not been initialized.");

    public IReadOnlyList<DockItemInfo> Items => Volatile.Read(ref _items);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            _layout = await GetOrCreateLayoutAsync(cancellationToken).ConfigureAwait(false);
            _pinned = await _pinnedApplications.GetAllAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;

            LogInitialized(_logger, _layout.Position, _pinned.Count, _items.Count - _pinned.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshRunningAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRefreshFailed(_logger, ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<DiscoveredApplication>> DiscoverApplicationsAsync(
        CancellationToken cancellationToken = default)
        => _catalog.DiscoverAsync(cancellationToken);

    public async Task AddPinnedAsync(
        DiscoveredApplication application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(application.Target)
            || string.IsNullOrWhiteSpace(application.Name))
        {
            throw new DockValidationException("The application name and target are required.");
        }

        if (application.TargetKind is PinnedTargetKind.Executable or PinnedTargetKind.Shortcut
            && !File.Exists(application.Target))
        {
            throw new DockValidationException($"The application target '{application.Target}' is not available.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pinned.Any(pinned => pinned.Target.Equals(application.Target, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DockValidationException("The application is already pinned to the dock.");
            }

            var resolved = await _shortcutResolver
                .ResolveAsync(application.Target, application.TargetKind, cancellationToken)
                .ConfigureAwait(false);

            var iconKey = application.IconPath ?? application.Target;
            if (application.TargetKind == PinnedTargetKind.Shortcut
                && !string.IsNullOrWhiteSpace(resolved.ExecutablePath)
                && File.Exists(resolved.ExecutablePath)
                && !Path.GetFileName(resolved.ExecutablePath)
                    .Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer the resolved executable thumbnail; the .lnk shell
                // thumbnail can be a generic document for some installers.
                iconKey = resolved.ExecutablePath;
            }

            var entity = new PinnedApplication
            {
                Name = application.Name.Trim(),
                Target = application.Target,
                Arguments = application.Arguments ?? resolved.Arguments,
                TargetKind = application.TargetKind,
                ResolvedTarget = resolved.ExecutablePath,
                AppUserModelId = resolved.AppUserModelId,
                IconKey = iconKey,
                SortOrder = _pinned.Count == 0 ? 0 : _pinned.Max(pinned => pinned.SortOrder) + 1
            };

            _ = await _pinnedApplications.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            _pinned = await _pinnedApplications.GetAllAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);

            LogPinnedAdded(_logger, entity.Name, entity.Target);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemovePinnedAsync(Guid pinnedId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = await _pinnedApplications.RemoveAsync(pinnedId, cancellationToken).ConfigureAwait(false);
            if (!removed)
            {
                throw new DockValidationException("The pinned application no longer exists.");
            }

            _pinned = await _pinnedApplications.GetAllAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            LogPinnedRemoved(_logger, pinnedId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReorderPinnedAsync(
        IReadOnlyList<Guid> orderedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _pinnedApplications.ReorderAsync(orderedIds, cancellationToken).ConfigureAwait(false);
            _pinned = await _pinnedApplications.GetAllAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ActivateAsync(DockItemInfo item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfDisposed();

        if (item.WindowHandle is IntPtr handle)
        {
            try
            {
                await _launcher.ActivateWindowAsync(handle, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                LogActivationFailed(_logger, item.DisplayName, ex);
            }
        }

        if (!item.IsPinned || string.IsNullOrWhiteSpace(item.LaunchTarget))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (_lastLaunch.TryGetValue(item.Key, out var last) && now - last < TimeSpan.FromMilliseconds(1_500))
        {
            LogLaunchSuppressed(_logger, item.DisplayName);
            return;
        }

        _lastLaunch[item.Key] = now;

        try
        {
            await _launcher.LaunchAsync(item, cancellationToken).ConfigureAwait(false);
            LogLaunched(_logger, item.DisplayName);
            _ = RefreshAfterLaunchAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LogLaunchFailed(_logger, item.DisplayName, ex);
            throw new DockValidationException($"Could not launch '{item.DisplayName}': {ex.Message}");
        }
    }

    public async Task UpdateLayoutAsync(DockLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_layout is null || layout.Id == Guid.Empty)
            {
                throw new DockValidationException("The dock layout is not initialized.");
            }

            Clamp(layout);
            _layout = await _layouts.UpdateAsync(layout, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            LogLayoutUpdated(_logger, _layout.Position, _layout.IconSize, _layout.AutoHide);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DockLayout> GetOrCreateLayoutAsync(CancellationToken cancellationToken)
    {
        var layout = await _layouts.GetDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (layout is not null)
        {
            return layout;
        }

        var existing = await _layouts.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            await _layouts.SetDefaultAsync(existing[0].Id, cancellationToken).ConfigureAwait(false);
            return await _layouts.GetDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? existing[0];
        }

        return await _layouts.CreateAsync(DockLayoutDefaults.Create(), cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var windows = await _appWindows.GetVisibleWindowsAsync(cancellationToken).ConfigureAwait(false);
        var items = DockItemComposer.Compose(_pinned, windows);
        Volatile.Write(ref _items, items);
        RaiseChanged();
    }

    private async Task RefreshAfterLaunchAsync()
    {
        try
        {
            await Task.Delay(1_200, CancellationToken.None).ConfigureAwait(false);
            await RefreshRunningAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogPostLaunchRefreshFailed(_logger, ex);
        }
    }

    private static void Clamp(DockLayout layout)
    {
        layout.IconSize = Math.Clamp(layout.IconSize, 24, 96);
        layout.Spacing = Math.Clamp(layout.Spacing, 0, 40);
        layout.Magnification = Math.Clamp(layout.Magnification, 1, 1.5);
        layout.MaterialOpacity = Math.Clamp(layout.MaterialOpacity, 0.05, 1);
        layout.AnimationDurationMs = Math.Clamp(layout.AnimationDurationMs, 0, 1_000);
        layout.Offset = Math.Max(0, layout.Offset);
        layout.Size = layout.IconSize + 2 * DockLayoutCalculator.Padding;
        layout.MonitorTarget = string.IsNullOrWhiteSpace(layout.MonitorTarget)
            ? DockLayout.PrimaryMonitorTarget
            : layout.MonitorTarget.Trim();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogChangedHandlerFailed(_logger, ex);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "Dock initialized: edge {Edge}, {PinnedCount} pinned, {RunningCount} running.")]
    private static partial void LogInitialized(ILogger<DockController> logger, DockEdge edge, int pinnedCount, int runningCount);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Dock running-window refresh failed.")]
    private static partial void LogRefreshFailed(ILogger<DockController> logger, Exception exception);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information, Message = "Pinned '{Name}' ({Target}).")]
    private static partial void LogPinnedAdded(ILogger<DockController> logger, string name, string target);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Information, Message = "Pinned application {PinnedId} removed.")]
    private static partial void LogPinnedRemoved(ILogger<DockController> logger, Guid pinnedId);

    [LoggerMessage(EventId = 7005, Level = LogLevel.Warning, Message = "Could not activate '{Name}'; trying to launch it instead.")]
    private static partial void LogActivationFailed(ILogger<DockController> logger, string name, Exception exception);

    [LoggerMessage(EventId = 7006, Level = LogLevel.Debug, Message = "Launch of '{Name}' suppressed by the duplicate-launch cooldown.")]
    private static partial void LogLaunchSuppressed(ILogger<DockController> logger, string name);

    [LoggerMessage(EventId = 7007, Level = LogLevel.Information, Message = "Launched '{Name}'.")]
    private static partial void LogLaunched(ILogger<DockController> logger, string name);

    [LoggerMessage(EventId = 7008, Level = LogLevel.Error, Message = "Could not launch '{Name}'.")]
    private static partial void LogLaunchFailed(ILogger<DockController> logger, string name, Exception exception);

    [LoggerMessage(EventId = 7009, Level = LogLevel.Information, Message = "Dock layout updated: edge {Edge}, icon {IconSize}, auto-hide {AutoHide}.")]
    private static partial void LogLayoutUpdated(ILogger<DockController> logger, DockEdge edge, double iconSize, bool autoHide);

    [LoggerMessage(EventId = 7010, Level = LogLevel.Debug, Message = "Post-launch dock refresh failed.")]
    private static partial void LogPostLaunchRefreshFailed(ILogger<DockController> logger, Exception exception);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Warning, Message = "A dock Changed handler failed.")]
    private static partial void LogChangedHandlerFailed(ILogger<DockController> logger, Exception exception);
}
