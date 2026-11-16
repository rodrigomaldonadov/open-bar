using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.WidgetRuntime;
using OpenBar.Widgets.Abstractions;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class WidgetDiscoveryTests
{
    [Fact]
    public async Task RegistersDefinitionsFromEveryProvider()
    {
        var registry = new WidgetRegistry();
        var discovery = CreateDiscovery(
            registry,
            Provider(TestDefinitions.Create("a.widget")),
            Provider(TestDefinitions.Create("b.widget")));

        var result = await discovery.DiscoverAsync();

        Assert.Equal(2, result.Registered.Count);
        Assert.Empty(result.Failures);
        Assert.NotNull(registry.Find("a.widget"));
        Assert.NotNull(registry.Find("b.widget"));
    }

    [Fact]
    public async Task AThrowingProviderDoesNotPreventOtherProviders()
    {
        var registry = new WidgetRegistry();
        var discovery = CreateDiscovery(
            registry,
            new ThrowingProvider(),
            Provider(TestDefinitions.Create()));

        var result = await discovery.DiscoverAsync();

        Assert.Single(result.Registered);
        Assert.Single(result.Failures);
        Assert.NotNull(registry.Find(TestDefinitions.WidgetId));
    }

    [Fact]
    public async Task AnInvalidDefinitionIsRecordedAndOthersStillRegister()
    {
        var registry = new WidgetRegistry();
        var invalid = TestDefinitions.Create("Invalid.Id");
        var discovery = CreateDiscovery(
            registry,
            Provider(invalid, TestDefinitions.Create("valid.widget")));

        var result = await discovery.DiscoverAsync();

        Assert.Single(result.Registered);
        Assert.Single(result.Failures);
        Assert.Equal("Invalid.Id", result.Failures[0].WidgetId);
        Assert.NotNull(registry.Find("valid.widget"));
    }

    [Fact]
    public async Task DuplicateDefinitionsAcrossProvidersAreRecorded()
    {
        var registry = new WidgetRegistry();
        var discovery = CreateDiscovery(
            registry,
            Provider(TestDefinitions.Create()),
            Provider(TestDefinitions.Create()));

        var result = await discovery.DiscoverAsync();

        Assert.Single(result.Registered);
        Assert.Single(result.Failures);
        Assert.Contains(result.Failures, failure => failure.WidgetId == TestDefinitions.WidgetId);
    }

    [Fact]
    public async Task NullDefinitionListIsRecordedAsAFailure()
    {
        var registry = new WidgetRegistry();
        var discovery = CreateDiscovery(registry, new NullProvider());

        var result = await discovery.DiscoverAsync();

        Assert.Empty(result.Registered);
        Assert.Single(result.Failures);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var registry = new WidgetRegistry();
        var discovery = CreateDiscovery(registry, new CanceledProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.DiscoverAsync(cts.Token));
    }

    private static WidgetDiscoveryService CreateDiscovery(
        WidgetRegistry registry,
        params IWidgetDefinitionProvider[] providers)
        => new(providers, registry, NullLogger<WidgetDiscoveryService>.Instance);

    private static DelegateProvider Provider(params WidgetDefinition[] definitions)
        => new(_ => new ValueTask<IReadOnlyList<WidgetDefinition>>(definitions));

    private sealed class DelegateProvider : IWidgetDefinitionProvider
    {
        private readonly Func<CancellationToken, ValueTask<IReadOnlyList<WidgetDefinition>>> _get;

        public DelegateProvider(Func<CancellationToken, ValueTask<IReadOnlyList<WidgetDefinition>>> get)
        {
            _get = get;
        }

        public ValueTask<IReadOnlyList<WidgetDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
            => _get(cancellationToken);
    }

    private sealed class ThrowingProvider : IWidgetDefinitionProvider
    {
        public ValueTask<IReadOnlyList<WidgetDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Provider failure.");
    }

    private sealed class NullProvider : IWidgetDefinitionProvider
    {
        public ValueTask<IReadOnlyList<WidgetDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<WidgetDefinition>)null!);
    }

    private sealed class CanceledProvider : IWidgetDefinitionProvider
    {
        public ValueTask<IReadOnlyList<WidgetDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IReadOnlyList<WidgetDefinition>>(Array.Empty<WidgetDefinition>());
        }
    }
}
