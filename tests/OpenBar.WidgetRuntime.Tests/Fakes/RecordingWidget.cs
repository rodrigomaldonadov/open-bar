using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

/// <summary>Deterministic widget used to observe lifecycle calls and inject failures.</summary>
internal sealed class RecordingWidget : IWidget, IWidgetSettingsConsumer
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<WidgetSettings> _appliedSettings = [];

    public int InitializeCalls { get; private set; }

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public bool ThrowOnInitialize { get; set; }

    public bool ThrowOnStart { get; set; }

    public bool ThrowOnStop { get; set; }

    public WidgetContext? Context { get; private set; }

    public IReadOnlyList<WidgetSettings> AppliedSettings
    {
        get
        {
            lock (_appliedSettings)
            {
                return _appliedSettings.ToArray();
            }
        }
    }

    public Task OnSettingsChangedAsync(WidgetSettings settings, CancellationToken cancellationToken = default)
    {
        lock (_appliedSettings)
        {
            _appliedSettings.Add(settings);
        }

        return Task.CompletedTask;
    }

    public Task Started => _started.Task;

    public Task InitializeAsync(WidgetContext context, CancellationToken cancellationToken = default)
    {
        InitializeCalls++;
        Context = context;

        return ThrowOnInitialize
            ? Task.FromException(new InvalidOperationException("Initialize failed."))
            : Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCalls++;
        _started.TrySetResult();

        return ThrowOnStart
            ? Task.FromException(new InvalidOperationException("Start failed."))
            : Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;

        return ThrowOnStop
            ? Task.FromException(new InvalidOperationException("Stop failed."))
            : Task.CompletedTask;
    }

    public object CreateView() => new object();

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}
