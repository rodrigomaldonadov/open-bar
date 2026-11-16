namespace OpenBar.WidgetRuntime;

/// <summary>One instance that failed during startup recovery.</summary>
public sealed record WidgetStartupFailure(Guid InstanceId, string WidgetId, string InstanceName, string Message);

/// <summary>Summary of a runtime start pass. Individual failures are isolated.</summary>
public sealed record WidgetStartupReport(
    int Started,
    int SkippedDisabled,
    int Unavailable,
    IReadOnlyList<WidgetStartupFailure> Failures,
    bool AlreadyStarted = false);
