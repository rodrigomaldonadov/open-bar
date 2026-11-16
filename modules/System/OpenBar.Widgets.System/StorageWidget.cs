using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBar.SystemBridge;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public sealed partial class StorageWidget : WidgetBase, IWidgetSettingsConsumer
{
    private const double Gigabyte = 1024d * 1024d * 1024d;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private StorageWidgetSettings _settings = new();
    private MetricWidgetView? _view;
    private IDisposable? _schedule;
    private StorageMetricsSnapshot? _latest;

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
        var view = new MetricWidgetView("DISCO");

        lock (_gate)
        {
            _view = view;

            if (_latest is { } latest)
            {
                Apply(view, latest);
            }
            else
            {
                view.SetData("—", "Almacenamiento", 0, false, "Iniciando…");
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
                "storage.sample",
                TimeSpan.FromSeconds(_settings.IntervalSeconds),
                SampleAsync);
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await Context.Storage
                .RefreshAsync(_settings.VolumeId, cancellationToken)
                .ConfigureAwait(false);
            Publish(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSampleFailed(Logger, ex);
            Publish(StorageMetricsSnapshot.Empty);
        }
    }

    private void Publish(StorageMetricsSnapshot snapshot)
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

    private static void Apply(MetricWidgetView view, StorageMetricsSnapshot snapshot)
    {
        if (!snapshot.IsAvailable
            || snapshot.Volume is not { } volume
            || snapshot.UsedBytes is not { } used
            || snapshot.UsagePercent is not { } percent)
        {
            view.SetData("—", "Almacenamiento", 0, false, "No disponible");
            return;
        }

        view.SetData(
            string.Create(CultureInfo.CurrentCulture, $"{percent:0}%"),
            $"{used / Gigabyte:0.0} / {volume.TotalBytes / Gigabyte:0.0} GB · {volume.Name}",
            percent,
            true,
            string.Empty);
    }

    private StorageWidgetSettings ReadSettings(WidgetSettings settings)
    {
        try
        {
            var value = settings.Deserialize<StorageWidgetSettings>(SerializerOptions) ?? new StorageWidgetSettings();
            return new StorageWidgetSettings
            {
                IntervalSeconds = Math.Clamp(value.IntervalSeconds, 1, 10),
                VolumeId = value.VolumeId ?? string.Empty
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            LogInvalidSettings(Logger, ex);
            return new StorageWidgetSettings();
        }
    }

    [LoggerMessage(EventId = 6601, Level = LogLevel.Warning, Message = "Storage widget settings are invalid; using defaults.")]
    private static partial void LogInvalidSettings(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6602, Level = LogLevel.Warning, Message = "Storage widget could not read system metrics.")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
