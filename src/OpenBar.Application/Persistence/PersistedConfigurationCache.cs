using OpenBar.Domain.Settings;

namespace OpenBar.Application.Persistence;

/// <summary>
/// Thread-safe, in-memory snapshot of the settings loaded during startup.
/// Readers get an immutable dictionary; a refresh swaps the whole snapshot.
/// </summary>
public sealed class PersistedConfigurationCache
{
    private IReadOnlyDictionary<string, string> _values =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Values => Volatile.Read(ref _values);

    public DateTimeOffset? LoadedAtUtc { get; private set; }

    /// <summary>Replaces the snapshot with the supplied settings.</summary>
    public void Replace(IEnumerable<AppSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            snapshot[setting.Key] = setting.Value;
        }

        Volatile.Write(ref _values, snapshot);
        LoadedAtUtc = DateTimeOffset.UtcNow;
    }
}
