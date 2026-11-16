using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Collections.Specialized;
using System.ComponentModel;
using OpenBar.Application.Dock;
using OpenBar.Dock;
using OpenBar.Domain.Layouts;
using OpenBar.Domain.Widgets;
using OpenBar.Presentation.Widgets;
using OpenBar.SystemBridge;
using OpenBar.WidgetRuntime;
using Windows.Foundation;

namespace OpenBar.Presentation.Dock;

/// <summary>
/// Dock surface: pinned tiles, temporary running tiles, drag reorder and a
/// context menu. Application discovery and settings open in a separate window
/// because a ContentDialog hosted by the very small dock window would be
/// clipped.
/// </summary>
public sealed partial class DockView : UserControl
{
    private readonly ILogger<DockView> _logger;
    private readonly IWidgetRuntime? _widgets;

    private DockItemViewModel? _dragSource;
    private Point _dragStart;
    private bool _dragging;
    private bool _suppressClick;
    private bool _interactionActive;
    private Storyboard? _hideStoryboard;
    private Action? _hideCompleted;

    public DockView(
        DockViewModel viewModel,
        WidgetSurfaceHost surfaceHost,
        ILogger<DockView> logger,
        IWidgetRuntime? widgets = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(surfaceHost);
        ArgumentNullException.ThrowIfNull(logger);

        ViewModel = viewModel;
        SurfaceHost = surfaceHost;
        _widgets = widgets;
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        ViewModel.SystemStatusChanged += OnSystemStatusChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SurfaceHost.SurfacesChanged += OnSurfacesChanged;
        SystemStatus.SetSnapshot(viewModel.SystemStatus);

        EmbeddedPanel.AddHandler(RightTappedEvent, new RightTappedEventHandler(OnEmbeddedRightTapped), true);

        // ListView handles pointer events internally; listen to handled events
        // too so the dock can implement its own reorder gesture.
        PinnedList.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPinnedPointerPressed), true);
        PinnedList.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPinnedPointerMoved), true);
        PinnedList.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPinnedPointerReleased), true);
        PinnedList.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnPinnedPointerCaptureLost), true);

        DockShell.AddHandler(PointerMovedEvent, new PointerEventHandler(OnDockPointerMoved), true);
        DockShell.AddHandler(PointerExitedEvent, new PointerEventHandler(OnDockPointerExited), true);
    }

    public DockViewModel ViewModel { get; }

    public WidgetSurfaceHost SurfaceHost { get; }

    public bool IsInteractionActive => _interactionActive;

    public event EventHandler? InteractionStateChanged;

    /// <summary>Opens the separate application/settings window.</summary>
    public Func<Task>? OpenToolsAsync { get; set; }

    public void PlayShowAnimation()
    {
        CancelHideAnimation();
        var duration = ViewModel.EffectiveAnimationDurationMs;

        if (duration <= 0)
        {
            DockShell.Opacity = 1;
            ShellTransform.ScaleX = 1;
            ShellTransform.ScaleY = 1;
            return;
        }

        DockShell.Opacity = 0;
        ShellTransform.ScaleX = 0.985;
        ShellTransform.ScaleY = 0.985;

        var storyboard = new Storyboard();
        AddAnimation(storyboard, DockShell, "Opacity", 1, duration);
        AddAnimation(storyboard, ShellTransform, "ScaleX", 1, duration);
        AddAnimation(storyboard, ShellTransform, "ScaleY", 1, duration);
        storyboard.Begin();
    }

    public void PlayHideAnimation(Action completed)
    {
        CancelHideAnimation();
        var duration = Math.Min(ViewModel.EffectiveAnimationDurationMs, 120);

        if (duration <= 0)
        {
            completed();
            return;
        }

        _hideCompleted = completed;
        _hideStoryboard = new Storyboard();
        AddAnimation(_hideStoryboard, DockShell, "Opacity", 0, duration);
        AddAnimation(_hideStoryboard, ShellTransform, "ScaleX", 0.985, duration);
        AddAnimation(_hideStoryboard, ShellTransform, "ScaleY", 0.985, duration);
        _hideStoryboard.Completed += OnHideAnimationCompleted;
        _hideStoryboard.Begin();
    }

    public void CancelHideAnimation()
    {
        if (_hideStoryboard is not null)
        {
            _hideStoryboard.Completed -= OnHideAnimationCompleted;
            _hideStoryboard.Stop();
            _hideStoryboard = null;
        }

        _hideCompleted = null;
    }

    private void OnHideAnimationCompleted(object? sender, object e)
    {
        var completed = _hideCompleted;
        _hideCompleted = null;

        if (_hideStoryboard is not null)
        {
            _hideStoryboard.Completed -= OnHideAnimationCompleted;
            _hideStoryboard = null;
        }

        completed?.Invoke();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InitializeAsync();
            SurfaceHost.AttachPanel(EmbeddedPanel);
            ApplyOrientation();
        }
        catch (Exception ex)
        {
            LogError(_logger, "initialize the dock", ex);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DockViewModel.Position)
            or nameof(DockViewModel.IconSize)
            or nameof(DockViewModel.Spacing)
            or nameof(DockViewModel.EffectiveAnimationDurationMs))
        {
            ApplyOrientation();
        }
    }

    private void OnSurfacesChanged(object? sender, EventArgs e)
    {
        ApplyOrientation();
        ViewModel.RefreshItemLayout();
    }

    private void ApplyOrientation()
    {
        var vertical = ViewModel.IsVertical;
        var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;

        DockItemsPanel.Orientation = orientation;
        SetPanelOrientation(PinnedList, orientation);
        SetPanelOrientation(RunningList, orientation);


        RunningDivider.Width = vertical ? 28 : 1;
        RunningDivider.Height = vertical ? 1 : 28;
        RunningDivider.Margin = vertical ? new Thickness(8, 8, 8, 8) : new Thickness(8, 0, 8, 0);

        var actionMargin = vertical ? new Thickness(0, 10, 0, 0) : new Thickness(10, 0, 0, 0);
        AddButton.Margin = actionMargin;
        SettingsButton.Margin = actionMargin;
        SystemStatus.Margin = actionMargin;

        ViewModel.RefreshItemLayout();
        SurfaceHost.ApplyOrientation(vertical, ViewModel.IconSize, Math.Max(0, ViewModel.Spacing));
    }

    private static void SetPanelOrientation(ItemsControl itemsControl, Orientation orientation)
    {
        if (itemsControl.ItemsPanelRoot is StackPanel panel)
        {
            panel.Orientation = orientation;
        }
    }

    private async void OnEmbeddedRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (FindSurface(e.OriginalSource as DependencyObject) is not { } surface)
        {
            return;
        }

        e.Handled = true;
        var flyout = new MenuFlyout();
        AddMenuItem(flyout, "Configurar widget…", OpenToolsAsync ?? (() => Task.CompletedTask));

        if (_widgets is not null)
        {
            AddMenuItem(
                flyout,
                "Mover a ventana independiente",
                () => _widgets.SetPresentationAsync(
                    surface.InstanceId,
                    WidgetPresentationMode.FloatingWindow,
                    surface.Order));

            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Desactivar widget",
                () => _widgets.SetEnabledAsync(surface.InstanceId, false));
            AddMenuItem(
                flyout,
                "Quitar del dock",
                () => _widgets.DeleteAsync(surface.InstanceId));
        }

        SetInteractionActive(true);
        flyout.Closed += (_, _) => SetInteractionActive(false);
        flyout.ShowAt((FrameworkElement)e.OriginalSource, e.GetPosition((UIElement)e.OriginalSource));
    }

    private static EmbeddedWidgetSurfaceViewModel? FindSurface(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: EmbeddedWidgetSurfaceViewModel surface })
            {
                return surface;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private async void OnItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_suppressClick)
        {
            _suppressClick = false;
            e.Handled = true;
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not DockItemViewModel item)
        {
            return;
        }

        e.Handled = true;
        await RunAsync(() => ViewModel.ActivateAsync(item), $"activate {item.DisplayName}");
    }

    private void OnItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DockItemViewModel item)
        {
            return;
        }

        e.Handled = true;
        var flyout = new MenuFlyout();
        AddMenuItem(flyout, "Abrir", () => ViewModel.ActivateAsync(item));

        if (item.PinnedId is Guid pinnedId)
        {
            AddMenuItem(
                flyout,
                "Quitar del dock",
                () => ViewModel.RemovePinnedAsync(pinnedId));
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Añadir aplicación…", OpenToolsAsync ?? (() => Task.CompletedTask));
        AddMenuItem(flyout, "Configuración del dock…", OpenToolsAsync ?? (() => Task.CompletedTask));

        SetInteractionActive(true);
        flyout.Closed += (_, _) => SetInteractionActive(false);
        flyout.ShowAt((FrameworkElement)sender, e.GetPosition((UIElement)sender));
    }

    private async void OnAddApplicationClick(object sender, RoutedEventArgs e)
        => await OpenToolsSafeAsync();

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
        => await OpenToolsSafeAsync();

    private async void OnSystemStatusTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        await OpenToolsSafeAsync();
    }

    private void OnOverflowClick(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var hidden = SurfaceHost.Surfaces.Where(surface => !surface.IsVisible).ToArray();

        foreach (var surface in hidden)
        {
            var item = new MenuFlyoutItem { Text = $"Mostrar {surface.Title}" };
            item.Click += async (_, _) => await ShowOverflowSurfaceAsync(surface);
            flyout.Items.Add(item);
        }

        if (hidden.Length > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddMenuItem(flyout, "Configurar widgets…", OpenToolsAsync ?? (() => Task.CompletedTask));
        SetInteractionActive(true);
        flyout.Closed += (_, _) => SetInteractionActive(false);
        flyout.ShowAt((FrameworkElement)sender, new Point(0, 0));
    }

    private async Task ShowOverflowSurfaceAsync(EmbeddedWidgetSurfaceViewModel surface)
    {
        if (_widgets is null)
        {
            return;
        }

        var ordered = SurfaceHost.Surfaces.ToList();
        if (!ordered.Remove(surface))
        {
            return;
        }

        ordered.Insert(0, surface);

        await RunAsync(
            async () =>
            {
                for (var index = 0; index < ordered.Count; index++)
                {
                    await _widgets.SetPresentationAsync(
                        ordered[index].InstanceId,
                        WidgetPresentationMode.DockEmbedded,
                        index);
                }
            },
            "show the embedded widget");
    }

    private void OnSystemStatusChanged(object? sender, SystemMetricsSnapshot snapshot)
    {
        if (SystemStatus.DispatcherQueue.HasThreadAccess)
        {
            SystemStatus.SetSnapshot(snapshot);
            return;
        }

        _ = SystemStatus.DispatcherQueue.TryEnqueue(() => SystemStatus.SetSnapshot(snapshot));
    }

    private void OnDockPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            ResetMagnification();
            return;
        }

        var controls = GetItemControls();
        if (controls.Count == 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(DockShell).Position;
        var horizontal = ViewModel.Position is DockEdge.Top or DockEdge.Bottom;
        var centers = new double[controls.Count];

        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var center = control.TransformToVisual(DockShell)
                .TransformPoint(new Point(control.ActualWidth / 2, control.ActualHeight / 2));
            centers[index] = horizontal ? center.X : center.Y;
        }

        var scales = DockMagnificationCalculator.Calculate(
            centers,
            horizontal ? point.X : point.Y,
            ViewModel.IconSize,
            ViewModel.Spacing,
            ViewModel.Magnification);

        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            if (scales[index] > 1.001)
            {
                control.SetMagnification(scales[index], ViewModel.EffectiveAnimationDurationMs);
            }
            else
            {
                control.ResetMagnification(ViewModel.EffectiveAnimationDurationMs);
            }
        }
    }

    private void OnDockPointerExited(object sender, PointerRoutedEventArgs e)
        => ResetMagnification();

    private void ResetMagnification()
    {
        foreach (var control in GetItemControls())
        {
            control.ResetMagnification(ViewModel.EffectiveAnimationDurationMs);
        }
    }

    private List<DockItemControl> GetItemControls()
    {
        var controls = new List<DockItemControl>();
        AddControls(PinnedList, ViewModel.PinnedItems);
        AddControls(RunningList, ViewModel.RunningItems);
        return controls;

        void AddControls(ItemsControl itemsControl, IEnumerable<DockItemViewModel> items)
        {
            foreach (var item in items)
            {
                if (itemsControl.ContainerFromItem(item) is not DependencyObject container)
                {
                    continue;
                }

                FindDescendants(container, controls);
            }
        }
    }

    private static void FindDescendants(DependencyObject parent, List<DockItemControl> results)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is DockItemControl control)
            {
                results.Add(control);
            }

            FindDescendants(child, results);
        }
    }

    private void SetInteractionActive(bool active)
    {
        if (_interactionActive == active)
        {
            return;
        }

        _interactionActive = active;
        InteractionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property, double value, int durationMs)
    {
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = new Duration(TimeSpan.FromMilliseconds(Math.Max(1, durationMs))),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void OnPinnedPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressClick = false;
        _dragSource = FindItem(e.OriginalSource as DependencyObject);

        if (_dragSource?.IsPinned != true)
        {
            _dragSource = null;
            return;
        }

        _dragStart = e.GetCurrentPoint(PinnedList).Position;
        _dragging = false;
        _ = PinnedList.CapturePointer(e.Pointer);
    }

    private void OnPinnedPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragSource is null)
        {
            return;
        }

        var position = e.GetCurrentPoint(PinnedList).Position;
        if (!_dragging
            && (Math.Abs(position.X - _dragStart.X) > 6
                || Math.Abs(position.Y - _dragStart.Y) > 6))
        {
            _dragging = true;
        }

        if (_dragging)
        {
            e.Handled = true;
        }
    }

    private async void OnPinnedPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var source = _dragSource;
        var wasDragging = _dragging;
        _dragSource = null;
        _dragging = false;
        PinnedList.ReleasePointerCapture(e.Pointer);

        if (source is null || !wasDragging)
        {
            return;
        }

        _suppressClick = true;
        var position = e.GetCurrentPoint(PinnedList).Position;
        var target = FindItemAt(position);

        if (source.PinnedId is Guid sourceId
            && target?.PinnedId is Guid targetId
            && sourceId != targetId)
        {
            await RunAsync(
                () => ViewModel.MovePinnedAsync(sourceId, targetId),
                "reorder the dock");
        }
    }

    private void OnPinnedPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragSource = null;
        _dragging = false;
    }

    private static DockItemViewModel? FindItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is DockItemControl { DataContext: DockItemViewModel item })
            {
                return item;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private DockItemViewModel? FindItemAt(Point position)
    {
        foreach (var item in ViewModel.PinnedItems)
        {
            if (PinnedList.ContainerFromItem(item) is not ListViewItem container)
            {
                continue;
            }

            var origin = container.TransformToVisual(PinnedList).TransformPoint(new Point(0, 0));
            var bounds = new Rect(origin.X, origin.Y, container.ActualWidth, container.ActualHeight);

            if (bounds.Contains(position))
            {
                return item;
            }
        }

        return null;
    }

    private async Task OpenToolsSafeAsync()
    {
        if (OpenToolsAsync is not null)
        {
            await OpenToolsAsync();
        }
    }

    private void AddMenuItem(MenuFlyout flyout, string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) => await RunAsync(action, text);
        flyout.Items.Add(item);
    }

    private async Task RunAsync(Func<Task> action, string operation)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogError(_logger, operation, ex);
        }
    }

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "Dock view could not {Operation}.")]
    private static partial void LogError(ILogger<DockView> logger, string operation, Exception exception);
}
