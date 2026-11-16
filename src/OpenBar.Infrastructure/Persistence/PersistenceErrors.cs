using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenBar.Application.Persistence;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// Translates provider-level constraint failures into controlled application
/// exceptions without exposing SQL or stored values.
/// </summary>
internal static class PersistenceErrors
{
    private const int SqliteConstraint = 19;

    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintForeignKey = 787;
    private const int SqliteConstraintCheck = 275;
    private const int SqliteConstraintNotNull = 1299;

    public static PersistenceException Translate(DbUpdateException exception, string operation)
    {
        var sqlite = FindSqliteException(exception);

        if (sqlite is not null)
        {
            if (sqlite.SqliteErrorCode == SqliteConstraint)
            {
                return sqlite.SqliteExtendedErrorCode switch
                {
                    SqliteConstraintUnique or SqliteConstraintPrimaryKey => new PersistenceConflictException(
                        $"{operation} conflicts with an existing record.", exception),
                    SqliteConstraintForeignKey => new PersistenceConflictException(
                        $"{operation} violates a database relationship.", exception),
                    SqliteConstraintCheck or SqliteConstraintNotNull => new PersistenceValidationException(
                        $"{operation} violated a database constraint.", exception),
                    _ => new PersistenceException($"{operation} was rejected by the database.", exception)
                };
            }
        }

        return new PersistenceException($"{operation} failed.", exception);
    }

    private static SqliteException? FindSqliteException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite)
            {
                return sqlite;
            }
        }

        return null;
    }
}
