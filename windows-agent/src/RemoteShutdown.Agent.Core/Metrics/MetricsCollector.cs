using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RemoteShutdown.Agent.Core.Metrics;

public sealed record DiskMetrics(string Drive, long UsedBytes, long TotalBytes);

public sealed record MetricsSnapshot(double CpuPercent, long RamUsedBytes, long RamTotalBytes, IReadOnlyList<DiskMetrics> Disks);

/// <summary>
/// Collects the CPU/RAM/disk snapshot described in docs/protocol.md §8.
/// CPU via a PerformanceCounter (WMI-backed under the hood), RAM via GlobalMemoryStatusEx
/// (cheaper and more direct than a WMI query), disks via DriveInfo.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MetricsCollector : IDisposable
{
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
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private readonly System.Diagnostics.PerformanceCounter _cpuCounter =
        new("Processor", "% Processor Time", "_Total");

    public MetricsCollector()
    {
        // First call always returns 0 for a rate counter; prime it so the first real
        // reading a caller gets back isn't misleadingly zero.
        _cpuCounter.NextValue();
    }

    public MetricsSnapshot Collect()
    {
        var cpuPercent = _cpuCounter.NextValue();

        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        long ramUsed = 0, ramTotal = 0;
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            ramTotal = (long)memStatus.ullTotalPhys;
            ramUsed = ramTotal - (long)memStatus.ullAvailPhys;
        }

        var disks = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => new DiskMetrics(d.Name, d.TotalSize - d.AvailableFreeSpace, d.TotalSize))
            .ToList();

        return new MetricsSnapshot(cpuPercent, ramUsed, ramTotal, disks);
    }

    public void Dispose() => _cpuCounter.Dispose();
}
