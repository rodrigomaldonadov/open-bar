namespace OpenBar.SystemBridge;

/// <summary>Raw battery state from Windows.</summary>
public readonly record struct BatterySample(
    bool IsAvailable,
    int Percent,
    bool IsCharging,
    bool IsOnAcPower);

public sealed record BatteryMetricsSnapshot(
    bool IsAvailable,
    int? Percent,
    bool IsCharging,
    bool IsOnAcPower,
    DateTimeOffset CapturedAtUtc)
{
    public static BatteryMetricsSnapshot Empty { get; } = new(
        false,
        null,
        false,
        false,
        DateTimeOffset.MinValue);
}

/// <summary>Native battery source, replaceable in tests.</summary>
public interface IBatteryMetricsSource
{
    bool TryRead(out BatterySample sample);
}

/// <summary>Shared, cached battery measurements.</summary>
public interface IBatteryMetrics
{
    BatteryMetricsSnapshot Current { get; }

    Task<BatteryMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class NullBatteryMetrics : IBatteryMetrics
{
    public static NullBatteryMetrics Instance { get; } = new();

    public BatteryMetricsSnapshot Current => BatteryMetricsSnapshot.Empty;

    public Task<BatteryMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current);
}
