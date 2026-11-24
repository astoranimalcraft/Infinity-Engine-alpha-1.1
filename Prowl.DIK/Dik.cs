// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Infinity.DIK;

/// <summary>What the current platform lets DIK do.</summary>
public sealed record DikCapabilities(bool FileSystem, bool Registry, bool Windows, bool Processes, bool Icons);

/// <summary>
/// Entry point for the Infinity DIK library (Desktop Integration Kit). Use it to inspect the machine, read and
/// write files, access the Windows registry, and find or control other application windows.
/// </summary>
public static class Dik
{
    /// <summary>True on Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Which feature groups are available on this platform.</summary>
    public static DikCapabilities Capabilities => new(
        FileSystem: true,
        Registry: RegistryTools.IsSupported,
        Windows: WindowTools.IsSupported,
        Processes: true,
        Icons: WindowTools.IsSupported);

    /// <summary>Collects a full machine snapshot.</summary>
    public static ComputerSnapshot Snapshot() => SystemInfo.GetSnapshot();

    /// <summary>Finds a visible window whose title contains the text.</summary>
    public static WindowInfo? FindWindow(string title) => WindowTools.FindByTitle(title);

    /// <summary>Reads a registry string. Returns null on non-Windows platforms.</summary>
    public static string? ReadRegistryString(RegistryHive hive, string subKey, string valueName)
        => RegistryTools.ReadString(hive, subKey, valueName);

    /// <summary>Reads the position of the first window whose title contains the text.</summary>
    public static WindowPoint? GetWindowPosition(string title)
        => FindWindow(title) is { } window ? WindowTools.GetPosition(window.Handle) : null;

    /// <summary>Moves the first window whose title contains the text.</summary>
    public static bool SetWindowPosition(string title, int x, int y)
        => FindWindow(title) is { } window && WindowTools.SetPosition(window.Handle, x, y);

    /// <summary>Shakes the first window whose title contains the text. Dispose the result to stop early.</summary>
    public static WindowShake? ShakeWindow(string title, int intensity = 12, int durationMilliseconds = 500)
        => FindWindow(title) is { } window
            ? WindowTools.Shake(window.Handle, intensity, durationMilliseconds)
            : null;

    /// <summary>Starts a safe simulated blue screen and returns the controller.</summary>
    public static BsodSimulator SimulateBsod(BsodOptions? options = null)
    {
        var simulator = new BsodSimulator(options);
        simulator.Start();
        return simulator;
    }

    /// <summary>Starts a safe cinematic fake hack and returns its controller.</summary>
    public static HackSimulation SimulateHack(HackSimulationOptions? options = null)
    {
        var simulation = new HackSimulation(options);
        simulation.Start();
        return simulation;
    }

    /// <summary>Runs the read-only security scan used by the player startup guard.</summary>
    public static SecurityReport ScanSecurity(SecurityScanOptions? options = null)
        => SecurityScanner.Scan(options);

    /// <summary>Shows the startup warning and returns true only when the player chose to run anyway.</summary>
    public static bool ConfirmRiskyLaunch(SecurityReport report, nint owner = 0, int requiredClicks = 2)
        => !DialogTools.IsSupported || DialogTools.ConfirmWarning(
            report.FormatForDialog(), "Infinity Engine - Alerta de Segurança", owner, requiredClicks);

    /// <summary>Shows a native Windows message box. Other platforms write the text to the console.</summary>
    public static MessageBoxResult MessageBox(
        string text,
        string caption = "Infinity Engine",
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.Information,
        nint owner = 0)
        => DialogTools.Show(text, caption, buttons, icon, owner);

    /// <summary>Shows a message box whose affirmative button must be clicked <paramref name="requiredClicks"/> times.</summary>
    public static MessageBoxResult MessageBox(
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        int requiredClicks,
        nint owner = 0)
        => DialogTools.Show(text, caption, buttons, icon, requiredClicks, owner);
    // ── Windows and screen ───────────────────────────────────────────────

    /// <summary>Every visible window, optionally with its icon decoded.</summary>
    public static IReadOnlyList<WindowInfo> GetWindows(bool visibleOnly = true, bool includeIcons = false)
        => WindowTools.GetWindows(visibleOnly, includeIcons);

    /// <summary>Finds a window by its exact title.</summary>
    public static WindowInfo? FindWindowExact(string title) => WindowTools.FindByTitle(title, exact: true);

    /// <summary>The window owned by a process.</summary>
    public static WindowInfo? FindWindowByProcess(int processId) => WindowTools.FindByProcessId(processId);

    /// <summary>The window currently in the foreground.</summary>
    public static WindowInfo? ActiveWindow() => WindowTools.FromHandle(WindowTools.GetActiveWindow());

    /// <summary>Changes a window title.</summary>
    public static bool SetWindowTitle(string title, string newTitle)
        => FindWindow(title) is { } window && WindowTools.SetTitle(window.Handle, newTitle);

    /// <summary>Minimizes the first window whose title contains the text.</summary>
    public static bool MinimizeWindow(string title)
        => FindWindow(title) is { } window && WindowTools.Minimize(window.Handle);

    /// <summary>Maximizes the first window whose title contains the text.</summary>
    public static bool MaximizeWindow(string title)
        => FindWindow(title) is { } window && WindowTools.Maximize(window.Handle);

    /// <summary>Restores the first window whose title contains the text.</summary>
    public static bool RestoreWindow(string title)
        => FindWindow(title) is { } window && WindowTools.Restore(window.Handle);

    /// <summary>Closes the first window whose title contains the text.</summary>
    public static bool CloseWindow(string title)
        => FindWindow(title) is { } window && WindowTools.Close(window.Handle);

    /// <summary>Hides the first window whose title contains the text.</summary>
    public static bool HideWindow(string title)
        => FindWindow(title) is { } window && WindowTools.Hide(window.Handle);

    /// <summary>Shows the first window whose title contains the text.</summary>
    public static bool ShowWindow(string title)
        => FindWindow(title) is { } window && WindowTools.Show(window.Handle);

    /// <summary>Brings a window to the front.</summary>
    public static bool ActivateWindow(string title)
        => FindWindow(title) is { } window && WindowTools.Activate(window.Handle);

    /// <summary>Makes a window borderless, or restores its frame.</summary>
    public static bool SetWindowBorderless(string title, bool borderless = true)
        => FindWindow(title) is { } window && WindowTools.SetBorderless(window.Handle, borderless);

    /// <summary>Keeps a window above the others, or stops doing so.</summary>
    public static bool SetWindowAlwaysOnTop(string title, bool onTop = true)
        => FindWindow(title) is { } window && WindowTools.SetAlwaysOnTop(window.Handle, onTop);

    /// <summary>Sets a window's opacity from 0 to 255.</summary>
    public static bool SetWindowOpacity(string title, byte opacity)
        => FindWindow(title) is { } window && WindowTools.SetOpacity(window.Handle, opacity);

    /// <summary>Makes clicks pass through a window, or stops it.</summary>
    public static bool SetWindowClickThrough(string title, bool clickThrough = true)
        => FindWindow(title) is { } window && WindowTools.SetClickThrough(window.Handle, clickThrough);

    /// <summary>The bounds of the primary monitor.</summary>
    public static ScreenRect PrimaryScreen => ScreenTools.PrimaryScreen;

    /// <summary>The bounds of the whole virtual desktop.</summary>
    public static ScreenRect VirtualScreen => ScreenTools.VirtualScreen;

    /// <summary>Where the mouse pointer is, in screen pixels.</summary>
    public static WindowPoint? CursorPosition => ScreenTools.CursorPosition;

    /// <summary>The monitor a window is on.</summary>
    public static ScreenRect? GetWindowMonitor(string title)
        => FindWindow(title) is { } window ? ScreenTools.GetMonitorBounds(window.Handle) : null;

    /// <summary>Reads a window icon as RGBA pixels.</summary>
    public static WindowIcon? GetWindowIcon(string title)
        => FindWindow(title) is { } window ? WindowTools.GetIcon(window.Handle) : null;

    /// <summary>Saves a window icon as a PNG file.</summary>
    public static bool SaveWindowIcon(string title, string path)
        => FindWindow(title) is { } window && WindowTools.TrySaveIconPng(window.Handle, path);

    // ── Files ────────────────────────────────────────────────────────────

    public static bool FileExists(string path) => FileTools.Exists(path);
    public static string? ReadFileText(string path) => FileTools.ReadText(path);
    public static byte[]? ReadFileBytes(string path) => FileTools.ReadBytes(path);
    public static bool WriteFileText(string path, string contents) => FileTools.WriteText(path, contents);
    public static bool WriteFileBytes(string path, byte[] contents) => FileTools.WriteBytes(path, contents);
    public static bool AppendFileText(string path, string contents) => FileTools.AppendText(path, contents);
    public static bool CopyFile(string source, string destination, bool overwrite = true) => FileTools.CopyFile(source, destination, overwrite);
    public static bool MoveFile(string source, string destination, bool overwrite = true) => FileTools.MoveFile(source, destination, overwrite);
    public static bool DeleteFile(string path) => FileTools.DeleteFile(path);
    public static bool CreateDirectory(string path) => FileTools.CreateDirectory(path);
    public static bool DeleteDirectory(string path, bool recursive = true) => FileTools.DeleteDirectory(path, recursive);
    public static long GetFileSize(string path) => FileTools.GetSize(path);
    public static IReadOnlyList<FileEntry> ListFiles(string path, string pattern = "*", bool recursive = false)
        => FileTools.List(path, pattern, recursive);
    public static FileEntry? GetFileInfo(string path) => FileTools.GetInfo(path);
    public static string TempPath => FileTools.TempPath;
    public static string SpecialFolder(Environment.SpecialFolder folder) => FileTools.SpecialFolder(folder);

    // ── Processes and OS ─────────────────────────────────────────────────

    public static IReadOnlyList<ProcessInfo> ListProcesses() => ProcessTools.List();
    public static ProcessInfo? GetProcess(int id) => ProcessTools.Get(id);
    public static Process? StartProcess(string fileName, string? arguments = null, string? workingDirectory = null)
        => ProcessTools.Start(fileName, arguments, workingDirectory);
    public static string? RunProcess(string fileName, string? arguments = null, int timeoutMilliseconds = 15000)
        => ProcessTools.Run(fileName, arguments, timeoutMilliseconds);
    public static bool KillProcess(int id) => ProcessTools.Kill(id);
    public static bool IsProcessRunning(int id) => ProcessTools.IsRunning(id);
    public static OsInfo GetOsInfo() => OsTools.GetInfo();
    public static IReadOnlyList<NetworkAdapterInfo> NetworkAdapters => OsTools.NetworkAdapters;
    public static string? GetEnvironment(string name) => OsTools.GetEnvironmentVariable(name);
    public static bool SetEnvironment(string name, string? value) => OsTools.SetEnvironmentVariable(name, value);
    public static bool OpenUrl(string url) => OsTools.OpenUrl(url);
    public static bool OpenFolder(string path) => OsTools.OpenFolder(path);
    public static bool Beep(int frequency = 800, int durationMilliseconds = 120) => OsTools.Beep(frequency, durationMilliseconds);

    // ── DLLs and registry ───────────────────────────────────────────────

    public static IReadOnlyList<DllInfo> GetLoadedDlls() => DllTools.GetLoadedModules();
    public static DllInfo? InspectDll(string path) => DllTools.Inspect(path);
    public static IReadOnlyList<DllInfo> ListDlls(string directory, bool recursive = false) => DllTools.List(directory, recursive);
    public static bool HasValidPeHeader(string path) => DllTools.HasValidPeHeader(path);
    public static bool IsMicrosoftDll(string path) => DllTools.IsMicrosoftPath(path);
    public static IReadOnlyList<RegistryStartupEntry> GetStartupEntries() => RegistryTools.GetStartupEntries();
    public static IReadOnlyList<RegistryValue> GetRegistryValues(RegistryHive hive, string subKey) => RegistryTools.GetValues(hive, subKey);
    public static IReadOnlyList<string> GetRegistrySubKeys(RegistryHive hive, string subKey) => RegistryTools.GetSubKeyNames(hive, subKey);
    public static bool WriteRegistryString(RegistryHive hive, string subKey, string valueName, string value, bool expand = false)
        => RegistryTools.WriteString(hive, subKey, valueName, value, expand);
    public static bool DeleteRegistryValue(RegistryHive hive, string subKey, string valueName)
        => RegistryTools.DeleteValue(hive, subKey, valueName);

    /// <summary>A friendly one line description of what DIK can do on this machine.</summary>
    public static string DescribeCapabilities()
    {
        DikCapabilities c = Capabilities;
        return $"DIK on {SystemInfo.OsDescription}: files={c.FileSystem}, registry={c.Registry}, windows={c.Windows}, processes={c.Processes}, icons={c.Icons}";
    }

}
