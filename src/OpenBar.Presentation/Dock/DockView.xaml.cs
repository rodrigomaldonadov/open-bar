using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenBar.Application.Dock;
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
    }

    public DockViewModel ViewModel { get; }

    /// <summary>Opens the separate application/settings window.</summary>
    public Func<Task>? OpenToolsAsync { get; set; }

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

        flyout.ShowAt((FrameworkElement)sender, e.GetPosition((UIElement)sender));
    }

    private async void OnAddApplicationClick(object sender, RoutedEventArgs e)
        => await OpenToolsSafeAsync();

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
        => await OpenToolsSafeAsync();

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
