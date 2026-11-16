using Microsoft.Extensions.Logging;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Discovers widget definitions provider by provider and registers each one
/// independently. A failing provider or definition is logged and recorded; the
/// remaining catalog is still discovered.
/// </summary>
public sealed partial class WidgetDiscoveryService
{
    private readonly IReadOnlyList<IWidgetDefinitionProvider> _providers;
    private readonly IWidgetRegistry _registry;
    private readonly ILogger<WidgetDiscoveryService> _logger;

    public WidgetDiscoveryService(
        IEnumerable<IWidgetDefinitionProvider> providers,
        IWidgetRegistry registry,
        ILogger<WidgetDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(registry);

        _providers = providers.ToArray();
        _registry = registry;
        _logger = logger;
    }

    public async Task<WidgetDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var registered = new List<WidgetDefinition>();
        var failures = new List<WidgetDiscoveryFailure>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<WidgetDefinition> definitions;
            try
            {
                definitions = await provider.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var source = provider.GetType().Name;
                failures.Add(new WidgetDiscoveryFailure(source, null, ex.Message));
                LogProviderFailed(_logger, source, ex);
                continue;
            }

            if (definitions is null)
            {
                var source = provider.GetType().Name;
                failures.Add(new WidgetDiscoveryFailure(source, null, "The provider returned no definition list."));
                LogProviderReturnedNull(_logger, source);
                continue;
            }

            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    _registry.Register(definition);
                    registered.Add(definition);
                }
                catch (WidgetDefinitionValidationException ex)
                {
                    failures.Add(new WidgetDiscoveryFailure(provider.GetType().Name, definition?.WidgetId, ex.Message));
                    LogDefinitionRejected(_logger, definition?.WidgetId ?? "<null>", ex.Message);
                }
                catch (WidgetRegistrationException ex)
                {
                    failures.Add(new WidgetDiscoveryFailure(provider.GetType().Name, definition?.WidgetId, ex.Message));
                    LogDefinitionDuplicate(_logger, ex.WidgetId);
                }
                catch (Exception ex)
                {
                    failures.Add(new WidgetDiscoveryFailure(provider.GetType().Name, definition?.WidgetId, ex.Message));
                    LogDefinitionFailed(_logger, definition?.WidgetId ?? "<null>", ex);
                }
            }
        }

        LogDiscoveryCompleted(_logger, registered.Count, failures.Count);
        return new WidgetDiscoveryResult(registered, failures);
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Error, Message = "Widget definition provider {Provider} failed; continuing with the remaining providers.")]
    private static partial void LogProviderFailed(ILogger<WidgetDiscoveryService> logger, string provider, Exception exception);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Warning, Message = "Widget definition provider {Provider} returned a null definition list.")]
    private static partial void LogProviderReturnedNull(ILogger<WidgetDiscoveryService> logger, string provider);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Warning, Message = "Widget definition {WidgetId} was rejected: {Reason}.")]
    private static partial void LogDefinitionRejected(ILogger<WidgetDiscoveryService> logger, string widgetId, string reason);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Warning, Message = "Widget definition {WidgetId} is duplicated and was ignored.")]
    private static partial void LogDefinitionDuplicate(ILogger<WidgetDiscoveryService> logger, string widgetId);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Error, Message = "Widget definition {WidgetId} could not be registered.")]
    private static partial void LogDefinitionFailed(ILogger<WidgetDiscoveryService> logger, string widgetId, Exception exception);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Information, Message = "Widget discovery completed: {RegisteredCount} registered, {FailureCount} failure(s).")]
    private static partial void LogDiscoveryCompleted(ILogger<WidgetDiscoveryService> logger, int registeredCount, int failureCount);
}
