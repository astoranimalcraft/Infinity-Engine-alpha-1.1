// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Infinity.DIK;

/// <summary>Processor information.</summary>
public sealed record CpuInfo(string Name, int LogicalProcessors, string Architecture);

/// <summary>Physical and process memory in bytes.</summary>
public sealed record MemoryInfo(long TotalBytes, long AvailableBytes, long ProcessBytes);

/// <summary>One mounted volume.</summary>
public sealed record DiskInfo(string Name, string DriveType, string Format, long TotalBytes, long FreeBytes);

/// <summary>A snapshot of the machine, suitable for a diagnostics screen or a support bundle.</summary>
public sealed record ComputerSnapshot(
    string MachineName,
    string UserName,
    string OsDescription,
    string OsVersion,
    string Architecture,
    DateTime BootTimeUtc,
    CpuInfo Cpu,
    MemoryInfo Memory,
    IReadOnlyList<DiskInfo> Disks,
    int ProcessCount);

/// <summary>Reads operating system, CPU, memory, disk and process information.</summary>
public static class SystemInfo
{
    /// <summary>The computer name.</summary>
    public static string MachineName => Environment.MachineName;

    /// <summary>The signed-in user name.</summary>
    public static string UserName => Environment.UserName;

    /// <summary>A human-readable OS name, such as "Microsoft Windows 11".</summary>
    public static string OsDescription => RuntimeInformation.OSDescription;

    /// <summary>The OS version string.</summary>
    public static string OsVersion => Environment.OSVersion.VersionString;

    /// <summary>Process architecture, such as X64 or Arm64.</summary>
    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>Time since the machine booted.</summary>
    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>When the machine booted, in UTC.</summary>
    public static DateTime BootTimeUtc => DateTime.UtcNow - Uptime;

    /// <summary>Number of logical processors.</summary>
    public static int ProcessorCount => Environment.ProcessorCount;

    /// <summary>CPU model name, when the platform exposes it.</summary>
    public static string CpuName
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    foreach (string line in File.ReadLines("/proc/cpuinfo"))
                        if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                            return line[(line.IndexOf(':') + 1)..].Trim();
                }
                catch { }
            }
            return RuntimeInformation.ProcessArchitecture.ToString();
        }
    }

    /// <summary>Total physical memory in bytes.</summary>
    public static long TotalMemoryBytes
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : 0;
            }
            if (OperatingSystem.IsLinux())
            {
                long total = ReadMemInfo("MemTotal");
                if (total > 0) return total;
            }
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
    }

    /// <summary>Available physical memory in bytes.</summary>
    public static long AvailableMemoryBytes
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : 0;
            }
            if (OperatingSystem.IsLinux())
            {
                long available = ReadMemInfo("MemAvailable");
                if (available > 0) return available;
            }
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
    }

    /// <summary>Memory currently held by this process.</summary>
    public static long ProcessMemoryBytes
    {
        get
        {
            try { return Process.GetCurrentProcess().WorkingSet64; }
            catch { return Environment.WorkingSet; }
        }
    }

    /// <summary>All mounted volumes. Empty when the platform hides them.</summary>
    public static IReadOnlyList<DiskInfo> Disks
    {
        get
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady)
                    .Select(drive => new DiskInfo(
                        drive.Name,
                        drive.DriveType.ToString(),
                        drive.DriveFormat,
                        drive.TotalSize,
                        drive.AvailableFreeSpace))
                    .ToArray();
            }
            catch { return []; }
        }
    }

    /// <summary>Number of running processes.</summary>
    public static int ProcessCount
    {
        get
        {
            try { return Process.GetProcesses().Length; }
            catch { return 0; }
        }
    }

    /// <summary>Collects the full snapshot in one call.</summary>
    public static ComputerSnapshot GetSnapshot() => new(
        MachineName,
        UserName,
        OsDescription,
        OsVersion,
        Architecture,
        BootTimeUtc,
        new CpuInfo(CpuName, ProcessorCount, Architecture),
        new MemoryInfo(TotalMemoryBytes, AvailableMemoryBytes, ProcessMemoryBytes),
        Disks,
        ProcessCount);

    private static long ReadMemInfo(string key)
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                string[] parts = line[(key.Length + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && long.TryParse(parts[0], out long kilobytes)) return kilobytes * 1024;
            }
        }
        catch { }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
