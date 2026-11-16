using Microsoft.Extensions.Logging;

namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Convenience base class for widgets. Lifecycle methods are optional no-ops
/// and <see cref="CreateView"/> throws unless overridden, which keeps simple
/// widgets small without forcing redundant implementations.
/// </summary>
public abstract class WidgetBase : IWidget
{
    protected WidgetContext Context { get; private set; } = null!;

    protected ILogger Logger => Context.Logger;

    public virtual Task InitializeAsync(WidgetContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        return Task.CompletedTask;
    }

    public virtual Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual object CreateView()
        => throw new NotSupportedException(
            $"{GetType().FullName} does not provide a visual view. Override CreateView.");

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
