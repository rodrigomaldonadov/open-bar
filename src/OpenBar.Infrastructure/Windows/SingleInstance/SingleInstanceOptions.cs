namespace OpenBar.Infrastructure.Windows.SingleInstance;

/// <summary>
/// Identifies the logical application instance. The value is combined with the
/// current user SID so different Windows users (or test runs) never share the
/// same mutex or pipe.
/// </summary>
public sealed class SingleInstanceOptions
{
    public string ApplicationId { get; init; } = "OpenBar";
}
