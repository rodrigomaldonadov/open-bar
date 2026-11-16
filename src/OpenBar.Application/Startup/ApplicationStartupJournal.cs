using System.Globalization;
using OpenBar.Application.Persistence;

namespace OpenBar.Application.Startup;

/// <summary>
/// Records real application state (start count and previous start time) in the
/// settings store. This is the first concrete consumer of persistence and is
/// used to verify that configuration survives restarts. It stores no secrets
/// and writes both keys in one transaction.
/// </summary>
public sealed class ApplicationStartupJournal
{
    public const string StartCountKey = "application.startup.count";
    public const string LastStartedAtKey = "application.startup.last_started_utc";

    private readonly IAppSettingsService _settings;

    public ApplicationStartupJournal(IAppSettingsService settings)
    {
        _settings = settings;
    }

    public async Task<StartupJournalEntry> RecordStartAsync(
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = await _settings.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var previousCount = 0;
        DateTimeOffset? previousStartedAt = null;

        foreach (var setting in existing)
        {
            if (string.Equals(setting.Key, StartCountKey, StringComparison.Ordinal))
            {
                _ = int.TryParse(
                    setting.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out previousCount);
            }
            else if (string.Equals(setting.Key, LastStartedAtKey, StringComparison.Ordinal))
            {
                if (DateTimeOffset.TryParse(
                        setting.Value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var parsed))
                {
                    previousStartedAt = parsed;
                }
            }
        }

        if (previousCount < 0)
        {
            previousCount = 0;
        }

        var startCount = previousCount + 1;
        var startedAt = startedAtUtc.ToUniversalTime();

        await _settings.SetManyAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [StartCountKey] = startCount.ToString(CultureInfo.InvariantCulture),
                [LastStartedAtKey] = startedAt.ToString("O", CultureInfo.InvariantCulture)
            },
            cancellationToken).ConfigureAwait(false);

        return new StartupJournalEntry(startCount, previousStartedAt, startedAt);
    }
}
