using System.Globalization;
using OpenBar.Application.Startup;
using OpenBar.Application.Tests.Fakes;
using Xunit;

namespace OpenBar.Application.Tests;

public sealed class ApplicationStartupJournalTests
{
    [Fact]
    public async Task FirstRunRecordsStartCountOneAndNoPreviousStart()
    {
        var settings = new InMemoryAppSettingsService();
        var journal = new ApplicationStartupJournal(settings);
        var startedAt = new DateTimeOffset(2026, 11, 16, 10, 30, 0, TimeSpan.Zero);

        var entry = await journal.RecordStartAsync(startedAt);

        Assert.Equal(1, entry.StartCount);
        Assert.Null(entry.PreviousStartedAtUtc);
        Assert.Equal(startedAt, entry.StartedAtUtc);
        Assert.Equal("1", await settings.GetValueAsync(ApplicationStartupJournal.StartCountKey));
        Assert.NotNull(await settings.GetValueAsync(ApplicationStartupJournal.LastStartedAtKey));
        Assert.Equal(1, settings.SetManyCallCount);
    }

    [Fact]
    public async Task SecondRunIncrementsCountAndReportsThePreviousStart()
    {
        var settings = new InMemoryAppSettingsService();
        var journal = new ApplicationStartupJournal(settings);

        var firstStart = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
        var secondStart = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

        _ = await journal.RecordStartAsync(firstStart);
        var second = await journal.RecordStartAsync(secondStart);

        Assert.Equal(2, second.StartCount);
        Assert.Equal(firstStart, second.PreviousStartedAtUtc);
        Assert.Equal(secondStart, second.StartedAtUtc);
        Assert.Equal("2", await settings.GetValueAsync(ApplicationStartupJournal.StartCountKey));

        // Both keys must be persisted in a single transactional call.
        Assert.Equal(2, settings.SetManyCallCount);
        Assert.Equal(2, (await settings.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ContinuesFromAnExistingCount()
    {
        var settings = new InMemoryAppSettingsService();
        _ = await settings.SetAsync(ApplicationStartupJournal.StartCountKey, "41");

        var journal = new ApplicationStartupJournal(settings);
        var entry = await journal.RecordStartAsync(DateTimeOffset.UtcNow);

        Assert.Equal(42, entry.StartCount);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("-7")]
    public async Task TreatsAnUnusableStoredCountAsZero(string storedValue)
    {
        var settings = new InMemoryAppSettingsService();
        _ = await settings.SetAsync(ApplicationStartupJournal.StartCountKey, storedValue);

        var journal = new ApplicationStartupJournal(settings);
        var entry = await journal.RecordStartAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, entry.StartCount);
    }

    [Fact]
    public async Task IgnoresAnUnparseablePreviousStartTimestamp()
    {
        var settings = new InMemoryAppSettingsService();
        _ = await settings.SetAsync(ApplicationStartupJournal.StartCountKey, "3");
        _ = await settings.SetAsync(ApplicationStartupJournal.LastStartedAtKey, "yesterday-ish");

        var journal = new ApplicationStartupJournal(settings);
        var entry = await journal.RecordStartAsync(DateTimeOffset.UtcNow);

        Assert.Equal(4, entry.StartCount);
        Assert.Null(entry.PreviousStartedAtUtc);
    }

    [Fact]
    public async Task StoresTheStartTimestampInUtc()
    {
        var settings = new InMemoryAppSettingsService();
        var journal = new ApplicationStartupJournal(settings);
        var localOffsetStart = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(2));

        var entry = await journal.RecordStartAsync(localOffsetStart);

        Assert.Equal(TimeSpan.Zero, entry.StartedAtUtc.Offset);

        var stored = await settings.GetValueAsync(ApplicationStartupJournal.LastStartedAtKey);
        Assert.True(DateTimeOffset.TryParse(
            stored,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed));
        Assert.Equal(entry.StartedAtUtc, parsed);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }
}
