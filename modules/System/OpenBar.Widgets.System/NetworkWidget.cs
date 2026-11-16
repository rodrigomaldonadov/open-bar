using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBar.SystemBridge;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public sealed partial class NetworkWidget : WidgetBase, IWidgetSettingsConsumer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private NetworkWidgetSettings _settings = new();
    private MetricWidgetView? _view;
    private IDisposable? _schedule;
    private NetworkMetricsSnapshot? _latest;

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
        var view = new MetricWidgetView("RED");

        lock (_gate)
        {
            _view = view;

            if (_latest is { } latest)
            {
                Apply(view, latest);
            }
            else
            {
                view.SetData("—", "Red", 0, false, "Iniciando…", showProgress: false);
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
                "network.sample",
                TimeSpan.FromSeconds(_settings.IntervalSeconds),
                SampleAsync);
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await Context.Network
                .RefreshAsync(_settings.AdapterId, cancellationToken)
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
            Publish(NetworkMetricsSnapshot.Empty);
        }
    }

    private void Publish(NetworkMetricsSnapshot snapshot)
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

    private static void Apply(MetricWidgetView view, NetworkMetricsSnapshot snapshot)
    {
        if (!snapshot.IsAvailable || snapshot.Adapter is null)
        {
            view.SetData("—", "Sin conexión", 0, false, "No disponible", showProgress: false);
            return;
        }

        if (snapshot.DownloadBytesPerSecond is not { } download
            || snapshot.UploadBytesPerSecond is not { } upload)
        {
            view.SetData("—", snapshot.Adapter.Name, 0, true, "Midiendo…", showProgress: false);
            return;
        }

        view.SetData(
            FormatRate(download),
            $"↑ {FormatRate(upload)} · {snapshot.Adapter.Name}",
            0,
            true,
            string.Empty,
            showProgress: false);
    }

    private static string FormatRate(double bytesPerSecond)
    {
        const double kilobyte = 1024;
        const double megabyte = 1024 * 1024;
        const double gigabyte = 1024 * 1024 * 1024;

        if (bytesPerSecond >= gigabyte)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{bytesPerSecond / gigabyte:0.0} GB/s");
        }

        if (bytesPerSecond >= megabyte)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{bytesPerSecond / megabyte:0.0} MB/s");
        }

        if (bytesPerSecond >= kilobyte)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{bytesPerSecond / kilobyte:0.0} KB/s");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{bytesPerSecond:0} B/s");
    }

    private NetworkWidgetSettings ReadSettings(WidgetSettings settings)
    {
        try
        {
            var value = settings.Deserialize<NetworkWidgetSettings>(SerializerOptions) ?? new NetworkWidgetSettings();
            return new NetworkWidgetSettings
            {
                IntervalSeconds = Math.Clamp(value.IntervalSeconds, 1, 10),
                AdapterId = value.AdapterId ?? string.Empty
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            LogInvalidSettings(Logger, ex);
            return new NetworkWidgetSettings();
        }
    }

    [LoggerMessage(EventId = 6501, Level = LogLevel.Warning, Message = "Network widget settings are invalid; using defaults.")]
    private static partial void LogInvalidSettings(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6502, Level = LogLevel.Warning, Message = "Network widget could not read system metrics.")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
