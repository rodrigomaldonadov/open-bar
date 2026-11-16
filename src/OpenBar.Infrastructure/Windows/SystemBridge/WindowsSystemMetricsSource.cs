using System.Runtime.InteropServices;
using OpenBar.SystemBridge;

namespace OpenBar.Infrastructure.Windows.SystemBridge;

/// <summary>Windows implementation of the System Bridge native source.</summary>
public sealed class WindowsSystemMetricsSource : ISystemMetricsSource
{
    public bool TryReadCpuTimes(out CpuTimesSample sample)
    {
        sample = default;

        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return false;
            }

            sample = new CpuTimesSample(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryReadMemory(out MemorySample sample)
    {
        sample = default;

        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                return false;
            }

            sample = new MemorySample(status.ullTotalPhys, status.ullAvailPhys);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FILETIME idleTime,
        out FILETIME kernelTime,
        out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
