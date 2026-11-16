using OpenBar.Application.Persistence;
using OpenBar.Domain.Settings;
using Xunit;

namespace OpenBar.Application.Tests;

public sealed class PersistedConfigurationCacheTests
{
    [Fact]
    public void NewCacheIsEmpty()
    {
        var cache = new PersistedConfigurationCache();

        Assert.Empty(cache.Values);
        Assert.Null(cache.LoadedAtUtc);
    }

    [Fact]
    public void ReplacePopulatesValuesAndTimestamp()
    {
        var cache = new PersistedConfigurationCache();

        cache.Replace(
        [
            Setting("ui.theme", "dark"),
            Setting("ui.language", "es-ES")
        ]);

        Assert.Equal(2, cache.Values.Count);
        Assert.Equal("dark", cache.Values["ui.theme"]);
        Assert.Equal("es-ES", cache.Values["ui.language"]);
        Assert.NotNull(cache.LoadedAtUtc);
    }

    [Fact]
    public void ReplaceSwapsTheSnapshotInsteadOfMutatingIt()
    {
        var cache = new PersistedConfigurationCache();
        cache.Replace([Setting("ui.theme", "dark")]);

        var previousSnapshot = cache.Values;

        cache.Replace([Setting("ui.theme", "light")]);

        Assert.Equal("dark", previousSnapshot["ui.theme"]);
        Assert.Equal("light", cache.Values["ui.theme"]);
        Assert.NotSame(previousSnapshot, cache.Values);
    }

    [Fact]
    public void ReplaceUsesOrdinalComparisonAndLastValueWinsForDuplicates()
    {
        var cache = new PersistedConfigurationCache();

        cache.Replace(
        [
            Setting("UI.Theme", "dark"),
            Setting("ui.theme", "light")
        ]);

        Assert.Equal(2, cache.Values.Count);
        Assert.Equal("dark", cache.Values["UI.Theme"]);
        Assert.Equal("light", cache.Values["ui.theme"]);
    }

    [Fact]
    public void ReplaceNullThrows()
    {
        var cache = new PersistedConfigurationCache();

        Assert.Throws<ArgumentNullException>(() => cache.Replace(null!));
    }

    private static AppSetting Setting(string key, string value)
        => new()
        {
            Key = key,
            Value = value,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
