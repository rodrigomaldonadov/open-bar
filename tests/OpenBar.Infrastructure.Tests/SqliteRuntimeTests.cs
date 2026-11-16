using Microsoft.Data.Sqlite;
using Xunit;

namespace OpenBar.Infrastructure.Tests;

public sealed class SqliteRuntimeTests
{
    [Fact]
    public void BundledSqliteProviderCanOpenInMemoryDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "select sqlite_version();";

        var version = command.ExecuteScalar() as string;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.True(Version.TryParse(version, out var parsed));
        Assert.True(parsed >= new Version(3, 50, 2), $"Unexpected SQLite version: {version}");
    }
}
