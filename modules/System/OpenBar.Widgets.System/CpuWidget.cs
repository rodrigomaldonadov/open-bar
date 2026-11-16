using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBar.SystemBridge;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public sealed partial class CpuWidget : WidgetBase, IWidgetSettingsConsumer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private CpuWidgetSettings _settings = new();
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
        var view = new MetricWidgetView("CPU");

        lock (_gate)
        {
            _view = view;

            if (_latest is { } latest)
            {
                Apply(view, latest);
            }
            else
            {
                view.SetData("—", "Uso de CPU", 0, false, "Iniciando…");
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
                "cpu.sample",
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

    private static void Apply(MetricWidgetView view, SystemMetricsSnapshot snapshot)
    {
        if (!snapshot.CpuAvailable || snapshot.CpuUsagePercent is not { } percent)
        {
            view.SetData("—", "Uso de CPU", 0, false, "Midiendo…");
            return;
        }

        view.SetData($"{percent:0}%", "Uso de CPU", percent, true, string.Empty);
    }

    private CpuWidgetSettings ReadSettings(WidgetSettings settings)
    {
        try
        {
            var value = settings.Deserialize<CpuWidgetSettings>(SerializerOptions) ?? new CpuWidgetSettings();
            return new CpuWidgetSettings { IntervalSeconds = Math.Clamp(value.IntervalSeconds, 1, 10) };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            LogInvalidSettings(Logger, ex);
            return new CpuWidgetSettings();
        }
    }

    [LoggerMessage(EventId = 6201, Level = LogLevel.Warning, Message = "CPU widget settings are invalid; using defaults.")]
    private static partial void LogInvalidSettings(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6202, Level = LogLevel.Warning, Message = "CPU widget could not read system metrics.")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
