// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Infinity.DIK;

/// <summary>Read-only facts about the running operating system and process.</summary>
public sealed record OsInfo(
    string MachineName,
    string UserName,
    string OsDescription,
    Version OsVersion,
    string Runtime,
    string Architecture,
    bool Is64BitOperatingSystem,
    bool Is64BitProcess,
    int ProcessorCount,
    int PageSizeBytes,
    string SystemDirectory,
    string CurrentDirectory,
    string? ProcessPath,
    int ProcessId,
    string CommandLine,
    long UptimeMilliseconds,
    long WorkingSetBytes);

/// <summary>One network adapter and its current addresses.</summary>
public sealed record NetworkAdapterInfo(string Name, string Description, string Type, string Status,
    IReadOnlyList<string> Addresses);

/// <summary>
/// Safe user-mode access to the operating system: environment, process identity, network adapters and shell
/// integration. It does not load kernel drivers; <see cref="NativeTools"/> can call exported native functions
/// when a game really needs a platform API.
/// </summary>
public static class OsTools
{
    /// <summary>Collects the current OS and process facts.</summary>
    public static OsInfo GetInfo() => new(
        Environment.MachineName,
        Environment.UserName,
        RuntimeInformation.OSDescription,
        Environment.OSVersion.Version,
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.Is64BitOperatingSystem,
        Environment.Is64BitProcess,
        Environment.ProcessorCount,
        Environment.SystemPageSize,
        Environment.SystemDirectory,
        Environment.CurrentDirectory,
        Environment.ProcessPath,
        Environment.ProcessId,
        Environment.CommandLine,
        Environment.TickCount64,
        Environment.WorkingSet);

    /// <summary>All environment variables, read-only.</summary>
    public static IReadOnlyDictionary<string, string> EnvironmentVariables
    {
        get
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                if (entry.Key is string key && entry.Value is string value)
                    values[key] = value;
            return values;
        }
    }

    public static string? GetEnvironmentVariable(string name)
        => Environment.GetEnvironmentVariable(name);

    public static bool SetEnvironmentVariable(string name, string? value)
    {
        try { Environment.SetEnvironmentVariable(name, value); return true; }
        catch { return false; }
    }

    /// <summary>Current process id.</summary>
    public static int ProcessId => Environment.ProcessId;

    /// <summary>Path of the running executable, when the platform exposes it.</summary>
    public static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>True on a 64-bit OS.</summary>
    public static bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;

    /// <summary>True when this process is 64-bit.</summary>
    public static bool Is64BitProcess => Environment.Is64BitProcess;

    /// <summary>System page size in bytes.</summary>
    public static int PageSizeBytes => Environment.SystemPageSize;

    /// <summary>Seconds since the machine booted.</summary>
    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>Current working directory. Set it to change where relative paths resolve.</summary>
    public static string CurrentDirectory
    {
        get => Environment.CurrentDirectory;
        set => Environment.CurrentDirectory = value;
    }

    /// <summary>Network adapters and their addresses. Empty when the platform denies access.</summary>
    public static IReadOnlyList<NetworkAdapterInfo> NetworkAdapters
    {
        get
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Select(adapter => new NetworkAdapterInfo(
                        adapter.Name,
                        adapter.Description,
                        adapter.NetworkInterfaceType.ToString(),
                        adapter.OperationalStatus.ToString(),
                        adapter.GetIPProperties().UnicastAddresses
                            .Select(address => address.Address.ToString())
                            .ToArray()))
                    .ToArray();
            }
            catch { return []; }
        }
    }

    /// <summary>Opens a URL with the default browser.</summary>
    public static bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Opens a folder in the system file manager.</summary>
    public static bool OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Plays a short system beep. Returns false when the platform has no console speaker.</summary>
    public static bool Beep(int frequency = 800, int durationMilliseconds = 120)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                BeepWin32((uint)frequency, (uint)durationMilliseconds);
                return true;
            }
            Console.Beep(Math.Clamp(frequency, 37, 32767), Math.Clamp(durationMilliseconds, 1, 10000));
            return true;
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll", EntryPoint = "Beep")]
    private static extern bool BeepWin32(uint frequency, uint duration);
}

/// <summary>
/// Loads native libraries and resolves exported functions. Use this for platform APIs such as user32,
/// kernel32, libc or a vendor SDK. Loading a kernel-mode driver still requires administrator rights, a
/// signed driver and a separate service; this API is deliberately user-mode only.
/// </summary>
public static class NativeTools
{
    /// <summary>Loads a native library and returns its handle, or zero.</summary>
    public static nint Load(string libraryPath)
    {
        try { return NativeLibrary.Load(libraryPath); }
        catch { return 0; }
    }

    /// <summary>Frees a library loaded by <see cref="Load"/>.</summary>
    public static bool Free(nint handle)
    {
        if (handle == 0) return false;
        try { NativeLibrary.Free(handle); return true; }
        catch { return false; }
    }

    /// <summary>Resolves an exported symbol, or zero.</summary>
    public static nint GetExport(nint library, string symbol)
    {
        if (library == 0) return 0;
        try { return NativeLibrary.GetExport(library, symbol); }
        catch { return 0; }
    }

    /// <summary>Resolves an exported symbol as a typed delegate.</summary>
    public static T? GetDelegate<T>(nint library, string symbol) where T : Delegate
    {
        nint address = GetExport(library, symbol);
        if (address == 0) return null;
        try { return Marshal.GetDelegateForFunctionPointer<T>(address); }
        catch { return null; }
    }
}
