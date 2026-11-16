namespace OpenBar.Application.Startup;

/// <summary>Observable result of recording an application start.</summary>
/// <param name="StartCount">Total starts recorded in the database, including this one.</param>
/// <param name="PreviousStartedAtUtc">Start time stored by the previous run, if any.</param>
/// <param name="StartedAtUtc">Start time recorded by this run.</param>
public sealed record StartupJournalEntry(
    int StartCount,
    DateTimeOffset? PreviousStartedAtUtc,
    DateTimeOffset StartedAtUtc);
