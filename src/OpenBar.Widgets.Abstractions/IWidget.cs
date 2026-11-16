namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Lifecycle contract for a widget instance. The runtime calls the methods in
/// this order: <see cref="InitializeAsync"/>, <see cref="StartAsync"/>,
/// <see cref="StopAsync"/>, <see cref="IAsyncDisposable.DisposeAsync"/>.
/// A widget object is created per start, so it must not reuse state from a
/// previous run.
/// </summary>
public interface IWidget : IAsyncDisposable
{
    /// <summary>Receives the instance context. Must not create UI here.</summary>
    Task InitializeAsync(WidgetContext context, CancellationToken cancellationToken = default);

    /// <summary>Starts timers, subscriptions and work. Must not block the caller.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops work started by <see cref="StartAsync"/>. Must be idempotent.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the widget's visual content. The runtime calls this on the UI
    /// thread through the host window service. WinUI widgets return a
    /// <c>Microsoft.UI.Xaml.UIElement</c>; the abstraction stays UI-framework
    /// agnostic so future out-of-process plugins can return a serialized view
    /// descriptor instead.
    /// </summary>
    object CreateView();
}
