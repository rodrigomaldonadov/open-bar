using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenBar.Application.Dock;
using OpenBar.Domain.Layouts;
using OpenBar.SystemBridge;
using Windows.UI.ViewManagement;

namespace OpenBar.Presentation.Dock;

/// <summary>
/// Presentation state for the dock. It reloads pinned/running tiles from the
/// platform-neutral controller, loads shell icons asynchronously and persists
/// order changes produced by the ListView drag-reorder gesture.
/// </summary>
public sealed partial class DockViewModel : ObservableObject, IDisposable
{
    private readonly IDockController _controller;
    private readonly DockIconService _icons;
    private readonly ISystemMetrics _systemMetrics;
    private readonly ILogger<DockViewModel> _logger;
    private readonly DispatcherQueue? _dispatcher;
    private readonly DispatcherQueueTimer? _refreshTimer;
    private readonly Dictionary<string, DockItemViewModel> _itemCache = new(StringComparer.Ordinal);
    private readonly UISettings? _uiSettings;

    private DockEdge _position = DockEdge.Bottom;
    private DockAlignment _alignment = DockAlignment.Center;
    private double _iconSize = 48;
    private double _spacing = 10;
    private double _magnification = 1.12;
    private double _materialOpacity = 0.55;
    private int _animationDurationMs = 180;
    private int _effectiveAnimationDurationMs = 180;
    private bool _autoHide;
    private double _offset = 12;
    private string _monitorTarget = DockLayout.PrimaryMonitorTarget;
    private DockTheme _theme = DockTheme.System;
    private double _cornerRadius = 22;
    private int _autoHideShowDelayMs = 150;
    private int _autoHideHideDelayMs = 550;
    private bool _reduceAnimations;
    private bool _hideInFullscreen = true;
    private bool _initialized;
    private IDisposable? _systemMetricsSubscription;
    private SystemMetricsSnapshot _systemStatus = SystemMetricsSnapshot.Empty;
    private bool _active;
    private bool _refreshing;
    private bool _suppressReorder;
    private bool _disposed;
    private DateTimeOffset? _layoutVersion;

    public DockViewModel(
        IDockController controller,
        DockIconService icons,
        ILogger<DockViewModel> logger,
        ISystemMetrics? systemMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(icons);

        _controller = controller;
        _icons = icons;
        _systemMetrics = systemMetrics ?? NullSystemMetrics.Instance;
        _logger = logger;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        try
        {
            _uiSettings = new UISettings();
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        }
        catch (Exception ex)
        {
            LogAnimationPreferenceFailed(_logger, ex);
        }

        if (_dispatcher is not null)
        {
            _refreshTimer = _dispatcher.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(2);
            _refreshTimer.IsRepeating = true;
            _refreshTimer.Tick += OnRefreshTimerTick;
        }

        PinnedItems.CollectionChanged += OnPinnedItemsChanged;
    }

    public ObservableCollection<DockItemViewModel> PinnedItems { get; } = [];

    public ObservableCollection<DockItemViewModel> RunningItems { get; } = [];

    public event EventHandler? LayoutChanged;

    public Func<Task>? AddTestWidget { get; set; }

    public event EventHandler<SystemMetricsSnapshot>? SystemStatusChanged;

    public SystemMetricsSnapshot SystemStatus => _systemStatus;

    public DockEdge Position
    {
        get => _position;
        private set => SetProperty(ref _position, value);
    }

    public DockAlignment Alignment
    {
        get => _alignment;
        private set => SetProperty(ref _alignment, value);
    }

    public double IconSize
    {
        get => _iconSize;
        private set => SetProperty(ref _iconSize, value);
    }

    public double Spacing
    {
        get => _spacing;
        private set => SetProperty(ref _spacing, value);
    }

    public double Magnification
    {
        get => _magnification;
        private set => SetProperty(ref _magnification, value);
    }

    public double MaterialOpacity
    {
        get => _materialOpacity;
        private set => SetProperty(ref _materialOpacity, value);
    }

    public int AnimationDurationMs
    {
        get => _animationDurationMs;
        private set => SetProperty(ref _animationDurationMs, value);
    }

    public int EffectiveAnimationDurationMs
    {
        get => _effectiveAnimationDurationMs;
        private set => SetProperty(ref _effectiveAnimationDurationMs, value);
    }

    public DockTheme Theme
    {
        get => _theme;
        private set
        {
            if (SetProperty(ref _theme, value))
            {
                OnPropertyChanged(nameof(ThemeElement));
            }
        }
    }

    public ElementTheme ThemeElement
        => _theme switch
        {
            DockTheme.Light => ElementTheme.Light,
            DockTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

    public double CornerRadius
    {
        get => _cornerRadius;
        private set
        {
            if (SetProperty(ref _cornerRadius, value))
            {
                OnPropertyChanged(nameof(CornerRadiusValue));
            }
        }
    }

    public Microsoft.UI.Xaml.CornerRadius CornerRadiusValue => new(_cornerRadius);

    public int AutoHideShowDelayMs
    {
        get => _autoHideShowDelayMs;
        private set => SetProperty(ref _autoHideShowDelayMs, value);
    }

    public int AutoHideHideDelayMs
    {
        get => _autoHideHideDelayMs;
        private set => SetProperty(ref _autoHideHideDelayMs, value);
    }

    public bool ReduceAnimations
    {
        get => _reduceAnimations;
        private set => SetProperty(ref _reduceAnimations, value);
    }

    public bool HideInFullscreen
    {
        get => _hideInFullscreen;
        private set => SetProperty(ref _hideInFullscreen, value);
    }

    public bool AutoHide
    {
        get => _autoHide;
        private set => SetProperty(ref _autoHide, value);
    }

    public double Offset
    {
        get => _offset;
        private set => SetProperty(ref _offset, value);
    }

    public string MonitorTarget
    {
        get => _monitorTarget;
        private set => SetProperty(ref _monitorTarget, value);
    }

    public int ItemCount => PinnedItems.Count + RunningItems.Count + 3;

    public int PinnedCount => PinnedItems.Count;

    public int RunningCount => RunningItems.Count;

    public bool HasRunningItems => RunningItems.Count > 0;

    public bool IsEmpty => PinnedItems.Count + RunningItems.Count == 0;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _controller.Changed += OnControllerChanged;
        await _controller.InitializeAsync(cancellationToken).ConfigureAwait(true);
        ApplyLayout(_controller.Layout);
        ApplyItems(_controller.Items);
        _initialized = true;
    }

    public void SetActive(bool active)
    {
        _active = active;

        if (_refreshTimer is null)
        {
            return;
        }

        if (active)
        {
            _refreshTimer.Start();
            _systemMetricsSubscription ??= _systemMetrics.Subscribe(
                OnSystemMetrics,
                TimeSpan.FromSeconds(1));
            OnSystemMetrics(_systemMetrics.Current);
            _ = RefreshSafeAsync();
        }
        else
        {
            _refreshTimer.Stop();
            _systemMetricsSubscription?.Dispose();
            _systemMetricsSubscription = null;
        }
    }

    public Task<IReadOnlyList<DiscoveredApplication>> DiscoverApplicationsAsync(
        CancellationToken cancellationToken = default)
        => _controller.DiscoverApplicationsAsync(cancellationToken);

    public async Task AddPinnedAsync(DiscoveredApplication application, CancellationToken cancellationToken = default)
    {
        await _controller.AddPinnedAsync(application, cancellationToken).ConfigureAwait(true);
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task RemovePinnedAsync(Guid pinnedId, CancellationToken cancellationToken = default)
    {
        await _controller.RemovePinnedAsync(pinnedId, cancellationToken).ConfigureAwait(true);
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Moves a pinned tile to another pinned tile's position.</summary>
    public Task MovePinnedAsync(Guid sourceId, Guid targetId)
    {
        if (sourceId == targetId)
        {
            return Task.CompletedTask;
        }

        var source = PinnedItems.FirstOrDefault(item => item.PinnedId == sourceId);
        var target = PinnedItems.FirstOrDefault(item => item.PinnedId == targetId);

        if (source is null || target is null)
        {
            return Task.CompletedTask;
        }

        var sourceIndex = PinnedItems.IndexOf(source);
        var targetIndex = PinnedItems.IndexOf(target);

        if (sourceIndex >= 0 && targetIndex >= 0)
        {
            PinnedItems.Move(sourceIndex, targetIndex);
        }

        return Task.CompletedTask;
    }

    public async Task ActivateAsync(DockItemViewModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _controller.ActivateAsync(item.Info, cancellationToken).ConfigureAwait(true);
    }

    public async Task SaveSettingsAsync(
        DockEdge position,
        double iconSize,
        double spacing,
        double magnification,
        double materialOpacity,
        int animationDurationMs,
        bool autoHide,
        DockTheme theme,
        double cornerRadius,
        int autoHideShowDelayMs,
        int autoHideHideDelayMs,
        bool reduceAnimations,
        bool hideInFullscreen,
        string monitorTarget,
        CancellationToken cancellationToken = default)
    {
        var layout = _controller.Layout;
        layout.Position = position;
        layout.IconSize = iconSize;
        layout.Spacing = spacing;
        layout.Magnification = magnification;
        layout.MaterialOpacity = materialOpacity;
        layout.AnimationDurationMs = animationDurationMs;
        layout.AutoHide = autoHide;
        layout.Theme = theme;
        layout.CornerRadius = cornerRadius;
        layout.AutoHideShowDelayMs = autoHideShowDelayMs;
        layout.AutoHideHideDelayMs = autoHideHideDelayMs;
        layout.ReduceAnimations = reduceAnimations;
        layout.HideInFullscreen = hideInFullscreen;
        layout.MonitorTarget = monitorTarget;

        await _controller.UpdateLayoutAsync(layout, cancellationToken).ConfigureAwait(true);
        ApplyLayout(_controller.Layout);
    }

    public async Task AddTestWidgetAsync()
    {
        if (AddTestWidget is not null)
        {
            await AddTestWidget().ConfigureAwait(true);
        }
    }

    private void OnControllerChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _ = _dispatcher.TryEnqueue(() => _ = ReloadSafeAsync());
            return;
        }

        _ = ReloadSafeAsync();
    }

    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogReloadFailed(_logger, ex);
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var layout = _controller.Layout;
        if (_layoutVersion != layout.UpdatedAt)
        {
            ApplyLayout(layout);
        }

        ApplyItems(_controller.Items);
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task RefreshSafeAsync()
    {
        if (!_active || _refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            await _controller.RefreshRunningAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogRefreshFailed(_logger, ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnRefreshTimerTick(DispatcherQueueTimer sender, object args)
        => _ = RefreshSafeAsync();

    private void OnSystemMetrics(SystemMetricsSnapshot snapshot)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _ = _dispatcher.TryEnqueue(() => OnSystemMetrics(snapshot));
            return;
        }

        _systemStatus = snapshot;
        SystemStatusChanged?.Invoke(this, snapshot);
    }

    private void ApplyLayout(DockLayout layout)
    {
        _layoutVersion = layout.UpdatedAt;
        Position = layout.Position;
        Alignment = layout.Alignment;
        IconSize = layout.IconSize;
        Spacing = layout.Spacing;
        Magnification = layout.Magnification;
        MaterialOpacity = layout.MaterialOpacity;
        AnimationDurationMs = layout.AnimationDurationMs;
        AutoHide = layout.AutoHide;
        Offset = layout.Offset;
        MonitorTarget = layout.MonitorTarget;
        Theme = layout.Theme;
        CornerRadius = layout.CornerRadius;
        AutoHideShowDelayMs = layout.AutoHideShowDelayMs;
        AutoHideHideDelayMs = layout.AutoHideHideDelayMs;
        ReduceAnimations = layout.ReduceAnimations;
        HideInFullscreen = layout.HideInFullscreen;
        EffectiveAnimationDurationMs = ComputeEffectiveAnimationDuration();

        ApplyItemLayout(PinnedItems);
        ApplyItemLayout(RunningItems);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private int ComputeEffectiveAnimationDuration()
        => _reduceAnimations || _uiSettings is { AnimationsEnabled: false }
            ? 0
            : Math.Max(0, _animationDurationMs);

    private void OnAnimationsEnabledChanged(
        UISettings sender,
        UISettingsAnimationsEnabledChangedEventArgs args)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _ = _dispatcher.TryEnqueue(RefreshAnimationPreference);
            return;
        }

        RefreshAnimationPreference();
    }

    private void RefreshAnimationPreference()
    {
        EffectiveAnimationDurationMs = ComputeEffectiveAnimationDuration();
        ApplyItemLayout(PinnedItems);
        ApplyItemLayout(RunningItems);
    }

    private void ApplyItemLayout(ObservableCollection<DockItemViewModel> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            items[index].ApplyLayout(
                IconSize,
                Spacing,
                Magnification,
                EffectiveAnimationDurationMs,
                isLast: index == items.Count - 1);
        }
    }

    private void ApplyItems(IReadOnlyList<DockItemInfo> items)
    {
        var previousCount = PinnedItems.Count + RunningItems.Count;
        _suppressReorder = true;
        try
        {
            var pinned = items.Where(item => item.IsPinned).ToArray();
            var running = items.Where(item => !item.IsPinned).ToArray();

            Merge(PinnedItems, pinned);
            Merge(RunningItems, running);

            foreach (var stale in _itemCache.Keys
                         .Where(key => items.All(item => item.Key != key))
                         .ToArray())
            {
                _ = _itemCache.Remove(stale);
            }

            ApplyItemLayout(PinnedItems);
            ApplyItemLayout(RunningItems);
        }
        finally
        {
            _suppressReorder = false;
        }

        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(PinnedCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(HasRunningItems));
        OnPropertyChanged(nameof(IsEmpty));

        if (previousCount != PinnedItems.Count + RunningItems.Count)
        {
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Merge(ObservableCollection<DockItemViewModel> target, DockItemInfo[] infos)
    {
        var desired = new List<DockItemViewModel>(infos.Length);

        foreach (var info in infos)
        {
            if (!_itemCache.TryGetValue(info.Key, out var item))
            {
                item = new DockItemViewModel(info);
                _itemCache[info.Key] = item;
                _ = LoadIconSafeAsync(item);
            }
            else
            {
                item.Update(info);

                if (item.Icon is null)
                {
                    _ = LoadIconSafeAsync(item);
                }
            }

            desired.Add(item);
        }

        var sameOrder = target.Count == desired.Count
                        && target.Select(item => item.Key).SequenceEqual(desired.Select(item => item.Key), StringComparer.Ordinal);

        if (sameOrder)
        {
            return;
        }

        target.Clear();
        foreach (var item in desired)
        {
            target.Add(item);
        }
    }

    private async Task LoadIconSafeAsync(DockItemViewModel item)
    {
        try
        {
            await item.LoadIconAsync(_icons).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogIconFailed(_logger, item.DisplayName, ex);
        }
    }

    private void OnPinnedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressReorder || _disposed)
        {
            return;
        }

        if (e.Action is not (NotifyCollectionChangedAction.Move
            or NotifyCollectionChangedAction.Add
            or NotifyCollectionChangedAction.Remove))
        {
            return;
        }

        var ids = PinnedItems
            .Select(item => item.PinnedId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToArray();

        _ = PersistOrderAsync(ids);
    }

    private async Task PersistOrderAsync(Guid[] ids)
    {
        try
        {
            await _controller.ReorderPinnedAsync(ids).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogReorderFailed(_logger, ex);
            await ReloadSafeAsync().ConfigureAwait(true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Changed -= OnControllerChanged;
        PinnedItems.CollectionChanged -= OnPinnedItemsChanged;
        _systemMetricsSubscription?.Dispose();
        _systemMetricsSubscription = null;

        if (_uiSettings is not null)
        {
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        }

        if (_refreshTimer is not null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
        }
    }

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Could not reload the dock after a controller change.")]
    private static partial void LogReloadFailed(ILogger<DockViewModel> logger, Exception exception);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Debug, Message = "Dock running-window refresh failed.")]
    private static partial void LogRefreshFailed(ILogger<DockViewModel> logger, Exception exception);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Debug, Message = "Could not load the icon for '{Name}'.")]
    private static partial void LogIconFailed(ILogger<DockViewModel> logger, string name, Exception exception);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Warning, Message = "Could not persist the dock order; reloading.")]
    private static partial void LogReorderFailed(ILogger<DockViewModel> logger, Exception exception);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Debug, Message = "Could not query the Windows animation preference.")]
    private static partial void LogAnimationPreferenceFailed(ILogger<DockViewModel> logger, Exception exception);
}
