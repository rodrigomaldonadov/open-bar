using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// Applies the per-connection SQLite pragmas. <c>foreign_keys</c> and
/// <c>busy_timeout</c> are connection scoped, so they must be set every time a
/// pooled or new connection is opened. WAL is a persistent database property
/// and is configured once by the initializer.
/// </summary>
public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    private readonly int _busyTimeoutMilliseconds;

    public SqliteConnectionInterceptor(OpenBarDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _busyTimeoutMilliseconds = Math.Max(1, (int)options.BusyTimeout.TotalMilliseconds);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaScript();
        command.ExecuteNonQuery();
    }

    private async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaScript();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildPragmaScript()
        => $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {_busyTimeoutMilliseconds};";
}
