using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBar.SystemBridge;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public sealed partial class MemoryWidget : WidgetBase, IWidgetSettingsConsumer
{
    private const double Megabyte = 1024d * 1024d;
    private const double Gigabyte = 1024d * 1024d * 1024d;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private MemoryWidgetSettings _settings = new();
    private MetricWidgetView? _view;
    private IDisposable? _schedule;
    private SystemMetricsSnapshot? _latest;

    public override Task InitializeAsync(WidgetContext context, CancellationToken cancellationToken = default)
    {
        _ = base.InitializeAsync(context, cancellationToken);
        _settings = ReadSettings(context.Settings);
        return Task.CompletedTask;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await SampleAsync(cancellationToken).ConfigureAwait(false);
        Schedule(force: true);
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
        var view = new MetricWidgetView("RAM");

        lock (_gate)
        {
            _view = view;

            if (_latest is { } latest)
            {
                Apply(view, latest);
            }
            else
            {
                view.SetData("—", "Memoria física", 0, false, Iniciando());
            }
        }

        return view;
    }

    public Task OnSettingsChangedAsync(WidgetSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = ReadSettings(settings);
        Schedule();
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

    private void Schedule(bool force = false)
    {
        lock (_gate)
        {
            if (!force && _schedule is null)
            {
                return;
            }

            _schedule?.Dispose();
            _schedule = Context.Scheduler.Schedule(
                "memory.sample",
                TimeSpan.FromSeconds(_settings.IntervalSeconds),
                SampleAsync);
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await Context.Metrics.RefreshAsync(cancellationToken).ConfigureAwait(false);
            Publish(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSampleFailed(Logger, ex);
            Publish(SystemMetricsSnapshot.Empty);
        }
    }

    private void Publish(SystemMetricsSnapshot snapshot)
    {
        MetricWidgetView? view;

        lock (_gate)
        {
            _latest = snapshot;
            view = _view;
        }

        if (view is not null)
        {
            view.DispatcherQueue.TryEnqueue(() => Apply(view, snapshot));
        }
    }

    private void Apply(MetricWidgetView view, SystemMetricsSnapshot snapshot)
    {
        if (!snapshot.MemoryAvailable
            || snapshot.MemoryUsagePercent is not { } percent
            || snapshot.UsedMemoryBytes is not { } used
            || snapshot.TotalMemoryBytes is not { } total)
        {
            view.SetData("—", "Memoria física", 0, false, "No disponible");
            return;
        }

        var useGigabytes = _settings.Unit.ToLowerInvariant() switch
        {
            "gb" => true,
            "mb" => false,
            _ => total >= Gigabyte
        };

        var divisor = useGigabytes ? Gigabyte : Megabyte;
        var unit = useGigabytes ? "GB" : "MB";
        var usedText = (used / divisor).ToString("0.0", CultureInfo.CurrentCulture);
        var totalText = (total / divisor).ToString("0.0", CultureInfo.CurrentCulture);

        view.SetData(
            $"{percent:0}%",
            $"{usedText} / {totalText} {unit}",
            percent,
            true,
            string.Empty);
    }

    private MemoryWidgetSettings ReadSettings(WidgetSettings settings)
    {
        try
        {
            var value = settings.Deserialize<MemoryWidgetSettings>(SerializerOptions) ?? new MemoryWidgetSettings();
            var unit = value.Unit?.ToLowerInvariant() is "mb" or "gb" ? value.Unit.ToLowerInvariant() : "auto";
            return new MemoryWidgetSettings
            {
                IntervalSeconds = Math.Clamp(value.IntervalSeconds, 1, 10),
                Unit = unit
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            LogInvalidSettings(Logger, ex);
            return new MemoryWidgetSettings();
        }
    }

    private static string Iniciando() => "Iniciando…";

    [LoggerMessage(EventId = 6301, Level = LogLevel.Warning, Message = "Memory widget settings are invalid; using defaults.")]
    private static partial void LogInvalidSettings(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6302, Level = LogLevel.Warning, Message = "Memory widget could not read system metrics.")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
