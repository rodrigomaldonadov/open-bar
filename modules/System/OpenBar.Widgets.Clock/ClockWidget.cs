using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.Clock;

/// <summary>
/// Official clock widget. The logic is deliberately UI-free; the WinUI view is
/// created only by <see cref="CreateView"/> on the UI thread. Updates run
/// through the runtime scheduler, never through a widget-owned timer.
/// </summary>
public sealed partial class ClockWidget : WidgetBase, IWidgetSettingsConsumer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();

    private ClockWidgetSettings _settings = new();
    private ClockWidgetView? _view;
    private IDisposable? _schedule;
    private string _timeText = "--:--";
    private string _dateText = string.Empty;

    public override Task InitializeAsync(WidgetContext context, CancellationToken cancellationToken = default)
    {
        _ = base.InitializeAsync(context, cancellationToken);
        _settings = ReadSettings(context.Settings);
        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        UpdateNow();

        _schedule = Context.Scheduler.Schedule(
            "clock.tick",
            TimeSpan.FromSeconds(1),
            _ =>
            {
                UpdateNow();
                return Task.CompletedTask;
            });

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _schedule?.Dispose();
            _schedule = null;
        }

        return Task.CompletedTask;
    }

    public override object CreateView()
    {
        var view = new ClockWidgetView();

        lock (_gate)
        {
            _view = view;
            ApplyToView(view, _timeText, _dateText);
        }

        return view;
    }

    public Task OnSettingsChangedAsync(WidgetSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = ReadSettings(settings);
        UpdateNow();
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _schedule?.Dispose();
            _schedule = null;
            _view = null;
        }

        GC.SuppressFinalize(this);
        return base.DisposeAsync();
    }

    private void UpdateNow()
    {
        var now = DateTimeOffset.Now;
        var timeFormat = _settings.Use24Hour
            ? (_settings.ShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (_settings.ShowSeconds ? "h:mm:ss tt" : "h:mm tt");

        var time = now.ToString(timeFormat, CultureInfo.CurrentCulture);
        var date = _settings.ShowDate
            ? now.ToString("ddd, d MMM yyyy", CultureInfo.CurrentCulture)
            : string.Empty;

        ClockWidgetView? view;
        lock (_gate)
        {
            _timeText = time;
            _dateText = date;
            view = _view;
        }

        view?.DispatcherQueue.TryEnqueue(() => ApplyToView(view, time, date));
    }

    private static void ApplyToView(ClockWidgetView view, string time, string date)
        => view.SetTime(time, date);

    private ClockWidgetSettings ReadSettings(WidgetSettings settings)
    {
        try
        {
            return settings.Deserialize<ClockWidgetSettings>(SerializerOptions) ?? new ClockWidgetSettings();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            LogInvalidSettings(Logger, ex);
            return new ClockWidgetSettings();
        }
    }

    [LoggerMessage(EventId = 5501, Level = LogLevel.Warning, Message = "Clock widget settings are not valid; using defaults.")]
    private static partial void LogInvalidSettings(ILogger logger, Exception exception);
}
