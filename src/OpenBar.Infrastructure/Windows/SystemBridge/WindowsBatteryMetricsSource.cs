using System.Runtime.InteropServices;
using OpenBar.SystemBridge;

namespace OpenBar.Infrastructure.Windows.SystemBridge;

/// <summary>Battery state from <c>GetSystemPowerStatus</c>.</summary>
public sealed class WindowsBatteryMetricsSource : IBatteryMetricsSource
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownFlag = 255;
    private const byte UnknownPercent = 255;
    private const byte ChargingFlag = 8;

    public bool TryRead(out BatterySample sample)
    {
        sample = default;

        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                return false;
            }

            var hasBattery = status.BatteryFlag != NoSystemBattery
                && status.BatteryFlag != UnknownFlag
                && status.BatteryLifePercent != UnknownPercent;
            var percent = status.BatteryLifePercent == UnknownPercent
                ? 0
                : status.BatteryLifePercent;

            sample = new BatterySample(
                hasBattery,
                Math.Clamp((int)percent, 0, 100),
                (status.BatteryFlag & ChargingFlag) != 0,
                status.ACLineStatus == 1);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
