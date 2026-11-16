using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenBar.Application.Widgets;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.Presentation.Widgets;

/// <summary>One compact widget hosted by the dock.</summary>
public sealed class EmbeddedWidgetSurfaceViewModel : ObservableObject
{
    private int _order;
    private bool _isVisible = true;

    public EmbeddedWidgetSurfaceViewModel(WidgetSurfaceRequest request, UIElement content)
    {
        InstanceId = request.InstanceId;
        WidgetId = request.WidgetId;
        Title = request.Title;
        Content = content;
        CompactWidth = Math.Max(72, request.CompactWidth);
        CompactHeight = Math.Max(72, request.CompactHeight);
        _order = request.Order;
    }

    public Guid InstanceId { get; }

    public string WidgetId { get; }

    public string Title { get; }

    public UIElement Content { get; }

    public double CompactWidth { get; }

    public double CompactHeight { get; }

    public int Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>
/// UI-thread host for embedded widget views. It owns only visual surfaces;
/// the widget runtime still owns the widget lifecycle and schedules.
/// </summary>
public sealed partial class WidgetSurfaceHost : IWidgetSurfaceService
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<WidgetSurfaceHost> _logger;
    private readonly Dictionary<Guid, EmbeddedWidgetSurfaceViewModel> _surfaces = new();
    private readonly Dictionary<Guid, EmbeddedWidgetSurfaceView> _views = new();
    private StackPanel? _panel;

    public WidgetSurfaceHost(DispatcherQueue dispatcherQueue, ILogger<WidgetSurfaceHost> logger)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(logger);

        _dispatcherQueue = dispatcherQueue;
        _logger = logger;
    }

    public ObservableCollection<EmbeddedWidgetSurfaceViewModel> Surfaces { get; } = [];

    public event EventHandler? SurfacesChanged;

    public void AttachPanel(StackPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (ReferenceEquals(_panel, panel))
        {
            return;
        }

        _panel = panel;
        panel.Children.Clear();

        foreach (var surface in Surfaces)
        {
            panel.Children.Add(GetOrCreateView(surface));
        }
    }

    public void ApplyOrientation(bool vertical, double iconSize, double spacing)
    {
        if (_panel is not null)
        {
            _panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        }

        for (var index = 0; index < Surfaces.Count; index++)
        {
            var surface = Surfaces[index];
            if (_views.TryGetValue(surface.InstanceId, out var view))
            {
                view.ApplyOrientation(vertical, iconSize, spacing, index == Surfaces.Count - 1);
            }
        }
    }

    public void SetVisibleCount(int visibleCount)
    {
        var count = Math.Clamp(visibleCount, 0, Surfaces.Count);

        for (var index = 0; index < Surfaces.Count; index++)
        {
            var surface = Surfaces[index];
            surface.IsVisible = index < count;

            if (_views.TryGetValue(surface.InstanceId, out var view))
            {
                view.SetVisible(surface.IsVisible);
            }
        }
    }

    public Task OpenAsync(WidgetSurfaceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RunOnUiAsync(
            () =>
            {
                if (_surfaces.ContainsKey(request.InstanceId))
                {
                    LogDuplicateSurface(_logger, request.InstanceId);
                    return;
                }

                if (request.Widget.CreateView() is not UIElement view)
                {
                    throw new InvalidOperationException(
                        $"Widget '{request.WidgetId}' did not return a WinUI UIElement view.");
                }

                if (view is IWidgetSurfaceView surfaceView)
                {
                    surfaceView.SetSurfaceMode(embedded: true);
                }

                var surface = new EmbeddedWidgetSurfaceViewModel(request, view);
                _surfaces[request.InstanceId] = surface;
                Surfaces.Add(surface);
                GetOrCreateView(surface);
                ReorderSurfaces();
                _panel?.Children.Add(_views[request.InstanceId]);
                ReorderPanel();
                SurfacesChanged?.Invoke(this, EventArgs.Empty);
            },
            cancellationToken);
    }

    public Task CloseAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => RunOnUiAsync(
            () =>
            {
                if (_surfaces.Remove(instanceId, out var surface))
                {
                    Surfaces.Remove(surface);

                    if (_views.Remove(instanceId, out var view))
                    {
                        _panel?.Children.Remove(view);
                    }

                    SurfacesChanged?.Invoke(this, EventArgs.Empty);
                }
            },
            cancellationToken);

    public Task UpdateOrderAsync(Guid instanceId, int order, CancellationToken cancellationToken = default)
        => RunOnUiAsync(
            () =>
            {
                if (!_surfaces.TryGetValue(instanceId, out var surface) || surface.Order == order)
                {
                    return;
                }

                surface.Order = Math.Max(0, order);
                ReorderSurfaces();
                ReorderPanel();
                SurfacesChanged?.Invoke(this, EventArgs.Empty);
            },
            cancellationToken);

    public Task CloseAllAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(
            () =>
            {
                if (Surfaces.Count == 0)
                {
                    return;
                }

                _surfaces.Clear();
                _views.Clear();
                _panel?.Children.Clear();
                Surfaces.Clear();
                SurfacesChanged?.Invoke(this, EventArgs.Empty);
            },
            cancellationToken);

    private void ReorderSurfaces()
    {
        var ordered = Surfaces
            .Select((surface, index) => (surface, index))
            .OrderBy(entry => entry.surface.Order)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.surface)
            .ToArray();

        for (var target = 0; target < ordered.Length; target++)
        {
            var current = ordered[target];
            var currentIndex = Surfaces.IndexOf(current);

            if (currentIndex != target)
            {
                Surfaces.Move(currentIndex, target);
            }
        }
    }

    private void ReorderPanel()
    {
        if (_panel is null)
        {
            return;
        }

        for (var target = 0; target < Surfaces.Count; target++)
        {
            if (!_views.TryGetValue(Surfaces[target].InstanceId, out var view))
            {
                continue;
            }

            var currentIndex = _panel.Children.IndexOf(view);
            if (currentIndex >= 0 && currentIndex != target)
            {
                _panel.Children.RemoveAt(currentIndex);
                _panel.Children.Insert(target, view);
            }
        }
    }

    private EmbeddedWidgetSurfaceView GetOrCreateView(EmbeddedWidgetSurfaceViewModel surface)
    {
        if (_views.TryGetValue(surface.InstanceId, out var view))
        {
            return view;
        }

        view = new EmbeddedWidgetSurfaceView(surface);
        _views[surface.InstanceId] = view;
        return view;
    }

    private Task RunOnUiAsync(Action action, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

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
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException("The dock dispatcher is not available."));
        }

        return completion.Task;
    }

    [LoggerMessage(EventId = 7301, Level = LogLevel.Warning, Message = "Embedded surface {InstanceId} was already open; the duplicate request was ignored.")]
    private static partial void LogDuplicateSurface(ILogger<WidgetSurfaceHost> logger, Guid instanceId);
}

