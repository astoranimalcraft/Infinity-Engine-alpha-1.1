// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Infinity.DIK;

/// <summary>Metadata for a file or directory returned by <see cref="FileTools.List"/>.</summary>
public sealed record FileEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime CreatedUtc,
    DateTime ModifiedUtc);

/// <summary>
/// File and folder operations for game tooling, mod installers and desktop integrations. Every method is safe to
/// call with a missing path; failures return false or an empty list instead of throwing.
/// </summary>
public static class FileTools
{
    /// <summary>Combines path parts using the platform separator.</summary>
    public static string Combine(params string[] parts) => Path.Combine(parts);

    /// <summary>True when a file or directory exists.</summary>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>True when the path is a file.</summary>
    public static bool IsFile(string path) => File.Exists(path);

    /// <summary>True when the path is a directory.</summary>
    public static bool IsDirectory(string path) => Directory.Exists(path);

    /// <summary>Creates a directory, including parents. Returns false when it cannot be created.</summary>
    public static bool CreateDirectory(string path)
    {
        try { Directory.CreateDirectory(path); return true; }
        catch { return false; }
    }

    /// <summary>Writes a UTF-8 text file, creating its folder first.</summary>
    public static bool WriteText(string path, string contents)
    {
        try
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, contents);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Writes bytes, creating the parent folder first.</summary>
    public static bool WriteBytes(string path, byte[] contents)
    {
        try
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, contents);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Reads a UTF-8 text file, or null when it cannot be read.</summary>
    public static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    /// <summary>Reads bytes, or null when the file cannot be read.</summary>
    public static byte[]? ReadBytes(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch { return null; }
    }

    /// <summary>Appends UTF-8 text, creating the file and folder when needed.</summary>
    public static bool AppendText(string path, string contents)
    {
        try
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.AppendAllText(path, contents);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Deletes a file. Returns false when it is missing or locked.</summary>
    public static bool DeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Deletes a directory. Returns false when it is missing or locked.</summary>
    public static bool DeleteDirectory(string path, bool recursive = true)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            Directory.Delete(path, recursive);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Copies a file, creating the destination folder first.</summary>
    public static bool CopyFile(string source, string destination, bool overwrite = true)
    {
        try
        {
            if (!File.Exists(source)) return false;
            string? folder = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.Copy(source, destination, overwrite);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Moves a file, creating the destination folder first.</summary>
    public static bool MoveFile(string source, string destination, bool overwrite = true)
    {
        try
        {
            if (!File.Exists(source)) return false;
            string? folder = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.Move(source, destination, overwrite);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Returns the size of a file in bytes, or zero.</summary>
    public static long GetSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    /// <summary>Lists files and folders matching a pattern.</summary>
    public static IReadOnlyList<FileEntry> List(string path, string pattern = "*", bool recursive = false)
    {
        if (!Directory.Exists(path)) return [];
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault,
            };
            return Directory.EnumerateFileSystemEntries(path, pattern, options)
                .Select(ToEntry)
                .Where(entry => entry != null)
                .Select(entry => entry!)
                .ToArray();
        }
        catch { return []; }
    }

    /// <summary>Returns metadata for one path, or null when it does not exist.</summary>
    public static FileEntry? GetInfo(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                return new FileEntry(info.FullName, info.Name, false, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc);
            }
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                return new FileEntry(info.FullName, info.Name, true, 0, info.CreationTimeUtc, info.LastWriteTimeUtc);
            }
        }
        catch { }
        return null;
    }

    /// <summary>The system temporary folder.</summary>
    public static string TempPath => Path.GetTempPath();

    /// <summary>A well-known folder such as Desktop, Documents or ApplicationData.</summary>
    public static string SpecialFolder(Environment.SpecialFolder folder)
        => Environment.GetFolderPath(folder);

    private static FileEntry? ToEntry(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                return new FileEntry(info.FullName, info.Name, true, 0, info.CreationTimeUtc, info.LastWriteTimeUtc);
            }
            var file = new FileInfo(path);
            return new FileEntry(file.FullName, file.Name, false, file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc);
        }
        catch { return null; }
    }
}
