// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Infinity.DIK;

/// <summary>One running process.</summary>
public sealed record ProcessInfo(int Id, string Name, string? Path, long WorkingSetBytes, DateTime? StartTimeUtc);

/// <summary>Starts, lists and stops processes.</summary>
public static class ProcessTools
{
    /// <summary>Lists running processes. Returns an empty list when the platform denies access.</summary>
    public static IReadOnlyList<ProcessInfo> List()
    {
        try
        {
            return Process.GetProcesses()
                .Select(ToInfo)
                .Where(info => info != null)
                .Select(info => info!)
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return []; }
    }

    /// <summary>Finds a process by id, or null.</summary>
    public static ProcessInfo? Get(int id)
    {
        try { return ToInfo(Process.GetProcessById(id)); }
        catch { return null; }
    }

    /// <summary>Starts a process and returns it, or null when it could not start.</summary>
    public static Process? Start(string fileName, string? arguments = null, string? workingDirectory = null)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = true,
            };
            return Process.Start(info);
        }
        catch { return null; }
    }

    /// <summary>Starts a process and captures its standard output.</summary>
    public static string? Run(string fileName, string? arguments = null, int timeoutMilliseconds = 15000)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                Arguments = arguments ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? process = Process.Start(info);
            if (process == null) return null;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(true); } catch { }
                return output + error;
            }
            return output + error;
        }
        catch { return null; }
    }

    /// <summary>Stops a process by id.</summary>
    public static bool Kill(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            process.Kill(true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>True when a process with this id is running.</summary>
    public static bool IsRunning(int id)
    {
        try { using Process process = Process.GetProcessById(id); return !process.HasExited; }
        catch { return false; }
    }

    private static ProcessInfo? ToInfo(Process process)
    {
        try
        {
            string? path = null;
            DateTime? started = null;
            try { path = process.MainModule?.FileName; } catch { }
            try { started = process.StartTime.ToUniversalTime(); } catch { }
            return new ProcessInfo(process.Id, process.ProcessName, path, process.WorkingSet64, started);
        }
        catch { return null; }
        finally { process.Dispose(); }
    }
}
