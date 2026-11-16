using OpenBar.Widgets.Abstractions;
using Xunit;

namespace OpenBar.Integration.Tests;

public sealed class AssemblyCompositionTests
{
    [Fact]
    public void WidgetContractIsResolvableFromReferencedAssemblies()
    {
        var contract = typeof(IWidget);
        var assembly = contract.Assembly;

        Assert.Equal("OpenBar.Widgets.Abstractions", assembly.GetName().Name);
        Assert.True(contract.IsInterface);
        Assert.Contains(nameof(IWidget.InitializeAsync), contract.GetMembers().Select(member => member.Name));
        Assert.Contains(nameof(IWidget.StartAsync), contract.GetMembers().Select(member => member.Name));
        Assert.Contains(nameof(IWidget.StopAsync), contract.GetMembers().Select(member => member.Name));
        Assert.Contains(nameof(IWidget.CreateView), contract.GetMembers().Select(member => member.Name));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(contract));
    }
}
