// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Infinity.DIK;

/// <summary>Read-only information about one DLL or EXE.</summary>
public sealed record DllInfo(
    string Path,
    string Name,
    long Size,
    bool IsManaged,
    bool IsMicrosoft,
    string? FileVersion,
    string? ProductName,
    string? CompanyName,
    string? Description);

/// <summary>
/// Inspects DLLs and EXEs without loading them into the process. Nothing here injects code, writes to a
/// system folder or changes another process; the game can use it to show realistic system information.
/// </summary>
public static class DllTools
{
    private static readonly string[] s_scanExtensions = [".dll", ".exe"];

    /// <summary>Every module loaded in the current process, or an empty list when the OS denies access.</summary>
    public static IReadOnlyList<DllInfo> GetLoadedModules()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return process.Modules
                .Cast<ProcessModule>()
                .Where(module => module.FileName is { Length: > 0 })
                .Select(module => Inspect(module.FileName))
                .Where(info => info != null)
                .Select(info => info!)
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return []; }
    }

    /// <summary>Inspects one file, or null when it is missing or unreadable.</summary>
    public static DllInfo? Inspect(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;

            string? version = null, product = null, company = null, description = null;
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(file.FullName);
                version = info.FileVersion;
                product = info.ProductName;
                company = info.CompanyName;
                description = info.FileDescription;
            }
            catch { }

            bool managed = false;
            try
            {
                AssemblyName.GetAssemblyName(file.FullName);
                managed = true;
            }
            catch (BadImageFormatException) { }
            catch { }

            return new DllInfo(
                file.FullName,
                file.Name,
                file.Length,
                managed,
                IsMicrosoftPath(file.FullName),
                version,
                product,
                company,
                description);
        }
        catch { return null; }
    }

    /// <summary>Lists DLLs and EXEs in a folder. Read-only and safe with a missing folder.</summary>
    public static IReadOnlyList<DllInfo> List(string directory, bool recursive = false)
    {
        if (!Directory.Exists(directory)) return [];
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault,
            };
            return Directory.EnumerateFiles(directory, "*", options)
                .Where(path => s_scanExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(Inspect)
                .Where(info => info != null)
                .Select(info => info!)
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return []; }
    }

    /// <summary>True when the PE file has the DOS and PE signatures a loadable module needs.</summary>
    public static bool HasValidPeHeader(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length < 64) return false;

            Span<byte> header = stackalloc byte[64];
            stream.ReadExactly(header);
            if (header[0] != (byte)'M' || header[1] != (byte)'Z') return false;

            int peOffset = BitConverter.ToInt32(header[0x3C..0x40]);
            if (peOffset < 0 || peOffset + 24L > stream.Length) return false;

            stream.Position = peOffset;
            Span<byte> pe = stackalloc byte[24];
            stream.ReadExactly(pe);
            return pe[0] == (byte)'P' && pe[1] == (byte)'E' && pe[2] == 0 && pe[3] == 0;
        }
        catch { return false; }
    }

    /// <summary>The Windows, Program Files and .NET runtime folders are Microsoft-owned.</summary>
    public static bool IsMicrosoftPath(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return Under(full, windows) || Under(full, programFiles) || Under(full, programFilesX86)
            || full.Contains("\\dotnet\\shared\\", StringComparison.OrdinalIgnoreCase);

        static bool Under(string candidate, string root)
            => !string.IsNullOrEmpty(root) && candidate.StartsWith(
                root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
