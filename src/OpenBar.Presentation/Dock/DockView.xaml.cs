using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OpenBar.Application.Dock;
using OpenBar.Dock;
using OpenBar.Domain.Layouts;
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

    private DockItemViewModel? _dragSource;
    private Point _dragStart;
    private bool _dragging;
    private bool _suppressClick;
    private bool _interactionActive;
    private Storyboard? _hideStoryboard;
    private Action? _hideCompleted;

    public DockView(DockViewModel viewModel, ILogger<DockView> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(logger);

        ViewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;

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
        }
        catch (Exception ex)
        {
            LogError(_logger, "initialize the dock", ex);
        }
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
