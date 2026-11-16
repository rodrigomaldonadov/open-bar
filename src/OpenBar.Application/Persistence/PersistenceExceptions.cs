namespace OpenBar.Application.Persistence;

/// <summary>
/// A controlled persistence failure. Messages are safe to log: they contain no
/// setting values, credentials or raw SQL.
/// </summary>
public class PersistenceException : Exception
{
    public PersistenceException()
    {
    }

    public PersistenceException(string message)
        : base(message)
    {
    }

    public PersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The supplied data is invalid and was rejected before writing.</summary>
public sealed class PersistenceValidationException : PersistenceException
{
    public PersistenceValidationException()
    {
    }

    public PersistenceValidationException(string message)
        : base(message)
    {
    }

    public PersistenceValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A unique or relational constraint prevented the write.</summary>
public sealed class PersistenceConflictException : PersistenceException
{
    public PersistenceConflictException()
    {
    }

    public PersistenceConflictException(string message)
        : base(message)
    {
    }

    public PersistenceConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The requested record does not exist.</summary>
public sealed class PersistenceNotFoundException : PersistenceException
{
    public PersistenceNotFoundException()
    {
    }

    public PersistenceNotFoundException(string message)
        : base(message)
    {
    }

    public PersistenceNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Database creation, integrity checking or migration failed.</summary>
public sealed class DatabaseInitializationException : PersistenceException
{
    public DatabaseInitializationException()
    {
    }

    public DatabaseInitializationException(string message)
        : base(message)
    {
    }

    public DatabaseInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
