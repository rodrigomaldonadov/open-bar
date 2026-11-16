using System.Net.NetworkInformation;
using OpenBar.SystemBridge;

namespace OpenBar.Infrastructure.Windows.SystemBridge;

/// <summary>Network adapters and cumulative byte counters from .NET/Windows.</summary>
public sealed class WindowsNetworkMetricsSource : INetworkMetricsSource
{
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsUsable)
                .Select(adapter => new NetworkAdapterInfo(
                    adapter.Id,
                    string.IsNullOrWhiteSpace(adapter.Name) ? adapter.Description : adapter.Name,
                    adapter.Description))
                .OrderBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    public bool TryRead(string? adapterId, out NetworkSample sample)
    {
        sample = default;

        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Where(IsUsable).ToArray();
            var adapter = string.IsNullOrWhiteSpace(adapterId)
                ? adapters.FirstOrDefault()
                : adapters.FirstOrDefault(candidate =>
                      string.Equals(candidate.Id, adapterId, StringComparison.OrdinalIgnoreCase))
                  ?? adapters.FirstOrDefault();

            if (adapter is null)
            {
                return false;
            }

            var statistics = adapter.GetIPStatistics();

            sample = new NetworkSample(
                new NetworkAdapterInfo(
                    adapter.Id,
                    string.IsNullOrWhiteSpace(adapter.Name) ? adapter.Description : adapter.Name,
                    adapter.Description),
                statistics.BytesReceived,
                statistics.BytesSent);
            return true;
        }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool IsUsable(NetworkInterface adapter)
        => adapter.OperationalStatus == OperationalStatus.Up
            && adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel
            && adapter.GetPhysicalAddress().GetAddressBytes().Length >= 6
            && !ContainsNoise(adapter.Name)
            && !ContainsNoise(adapter.Description);

    private static bool ContainsNoise(string value)
        => value.Contains("QoS", StringComparison.OrdinalIgnoreCase)
            || value.Contains("WFP", StringComparison.OrdinalIgnoreCase)
            || value.Contains("LightWeight", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Filter", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Pseudo", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Loopback", StringComparison.OrdinalIgnoreCase);
}
