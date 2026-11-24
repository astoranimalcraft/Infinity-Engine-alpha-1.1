// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Infinity.DIK;

/// <summary>Window rectangle in screen pixels.</summary>
public readonly record struct WindowBounds(int X, int Y, int Width, int Height);

/// <summary>A point in screen pixels.</summary>
public readonly record struct WindowPoint(int X, int Y);

/// <summary>Everything the engine can read about another application's window.</summary>
public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ClassName,
    int ProcessId,
    string ProcessName,
    WindowBounds Bounds,
    bool IsVisible,
    bool IsMinimized,
    bool IsMaximized,
    bool IsForeground,
    nint IconHandle);

/// <summary>
/// Finds, reads, moves and controls other application windows. Windows uses user32; other platforms report
/// <see cref="IsSupported"/> false and return empty results. Icon extraction also works on Windows.
/// </summary>
public static partial class WindowTools
{
    /// <summary>True on Windows, where the window APIs are implemented.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Lists top-level windows. Pass <paramref name="includeIcons"/> only when icons are needed.</summary>
    public static IReadOnlyList<WindowInfo> GetWindows(bool visibleOnly = true, bool includeIcons = false)
    {
        if (!IsSupported) return [];
        var windows = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (visibleOnly && !IsWindowVisible(hwnd)) return true;
            if (GetWindowTextLengthW(hwnd) <= 0) return true;
            WindowInfo? info = FromHandle(hwnd, includeIcons);
            if (info != null) windows.Add(info);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>Finds a window by title. Contains matching is case-insensitive unless <paramref name="exact"/> is true.</summary>
    public static WindowInfo? FindByTitle(string title, bool exact = false)
    {
        if (!IsSupported || string.IsNullOrEmpty(title)) return null;
        foreach (WindowInfo window in GetWindows(visibleOnly: false))
        {
            bool match = exact
                ? string.Equals(window.Title, title, StringComparison.OrdinalIgnoreCase)
                : window.Title.Contains(title, StringComparison.OrdinalIgnoreCase);
            if (match) return window;
        }
        return null;
    }

    /// <summary>Finds the first top-level window owned by a process.</summary>
    public static WindowInfo? FindByProcessId(int processId)
    {
        if (!IsSupported) return null;
        foreach (WindowInfo window in GetWindows(visibleOnly: false))
            if (window.ProcessId == processId) return window;
        return null;
    }

    /// <summary>Reads one window, or null when the handle is gone.</summary>
    public static WindowInfo? FromHandle(nint hwnd, bool includeIcon = true)
    {
        if (!IsSupported || hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            GetWindowRect(hwnd, out Rect rect);
            return new WindowInfo(
                hwnd,
                GetWindowText(hwnd),
                GetClassName(hwnd),
                (int)pid,
                ProcessName((int)pid),
                new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                IsWindowVisible(hwnd),
                IsIconic(hwnd),
                IsZoomed(hwnd),
                GetForegroundWindow() == hwnd,
                includeIcon ? GetIconHandle(hwnd) : IntPtr.Zero);
        }
        catch { return null; }
    }

    /// <summary>True while the window still exists. Use it to detect that another app closed.</summary>
    public static bool IsOpen(nint hwnd) => IsSupported && hwnd != IntPtr.Zero && IsWindow(hwnd);

    /// <summary>True when the window is minimized.</summary>
    public static bool IsMinimized(nint hwnd) => IsSupported && IsIconic(hwnd);

    /// <summary>True when the window is maximized.</summary>
    public static bool IsMaximized(nint hwnd) => IsSupported && IsZoomed(hwnd);

    /// <summary>True when the window is visible.</summary>
    public static bool IsVisible(nint hwnd) => IsSupported && IsWindowVisible(hwnd);

    /// <summary>Changes the title bar text.</summary>
    public static bool SetTitle(nint hwnd, string title) => IsSupported && SetWindowTextW(hwnd, title);

    /// <summary>Reads the window rectangle, or null when the handle is gone.</summary>
    public static WindowBounds? GetBounds(nint hwnd)
    {
        if (!IsSupported || !GetWindowRect(hwnd, out Rect rect)) return null;
        return new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>Reads the window's top-left position, or null when the handle is gone.</summary>
    public static WindowPoint? GetPosition(nint hwnd)
        => GetBounds(hwnd) is { } bounds ? new WindowPoint(bounds.X, bounds.Y) : null;

    /// <summary>Moves the window by a relative offset.</summary>
    public static bool MoveBy(nint hwnd, int dx, int dy)
        => GetBounds(hwnd) is { } bounds && SetPosition(hwnd, bounds.X + dx, bounds.Y + dy);

    /// <summary>Moves the window without changing its size.</summary>
    public static bool SetPosition(nint hwnd, int x, int y)
    {
        if (!IsSupported || !GetWindowRect(hwnd, out Rect rect)) return false;
        return MoveWindow(hwnd, x, y, rect.Right - rect.Left, rect.Bottom - rect.Top, true);
    }

    /// <summary>Resizes the window without moving it.</summary>
    public static bool SetSize(nint hwnd, int width, int height)
    {
        if (!IsSupported || !GetWindowRect(hwnd, out Rect rect)) return false;
        return MoveWindow(hwnd, rect.Left, rect.Top, width, height, true);
    }

    /// <summary>Moves and resizes the window.</summary>
    public static bool SetBounds(nint hwnd, int x, int y, int width, int height)
        => IsSupported && MoveWindow(hwnd, x, y, width, height, true);

    /// <summary>Shows the window.</summary>
    public static bool Show(nint hwnd) => IsSupported && ShowWindow(hwnd, SwShow);

    /// <summary>Hides the window.</summary>
    public static bool Hide(nint hwnd) => IsSupported && ShowWindow(hwnd, SwHide);

    /// <summary>Minimizes the window.</summary>
    public static bool Minimize(nint hwnd) => IsSupported && ShowWindow(hwnd, SwMinimize);

    /// <summary>Maximizes the window.</summary>
    public static bool Maximize(nint hwnd) => IsSupported && ShowWindow(hwnd, SwMaximize);

    /// <summary>Restores a minimized or maximized window.</summary>
    public static bool Restore(nint hwnd) => IsSupported && ShowWindow(hwnd, SwRestore);

    /// <summary>Brings the window to the foreground.</summary>
    public static bool Activate(nint hwnd)
    {
        if (!IsSupported) return false;
        ShowWindow(hwnd, SwRestore);
        return SetForegroundWindow(hwnd);
    }

    /// <summary>Posts WM_CLOSE, the same request as the title bar's X button.</summary>
    public static bool Close(nint hwnd) => IsSupported && PostMessageW(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Returns the window's large icon handle, or zero.</summary>
    public static nint GetIconHandle(nint hwnd)
    {
        if (!IsSupported) return IntPtr.Zero;
        if (SendMessageTimeoutW(hwnd, WmGetIcon, IconvBig, IntPtr.Zero, SmtoAbortIfHung, 120, out nint icon) && icon != IntPtr.Zero)
            return icon;
        if (SendMessageTimeoutW(hwnd, WmGetIcon, IconvSmall, IntPtr.Zero, SmtoAbortIfHung, 120, out icon) && icon != IntPtr.Zero)
            return icon;

        return IntPtr.Size == 8 ? GetClassLongPtrW(hwnd, GclpHicon) : new IntPtr(GetClassLongW(hwnd, GclpHicon));
    }

    private static string GetWindowText(nint hwnd)
    {
        int length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;
        var text = new StringBuilder(length + 1);
        GetWindowTextW(hwnd, text, text.Capacity);
        return text.ToString();
    }

    private static string GetClassName(nint hwnd)
    {
        var name = new StringBuilder(256);
        GetClassNameW(hwnd, name, name.Capacity);
        return name.ToString();
    }

    private static string ProcessName(int pid)
    {
        try { using Process process = Process.GetProcessById(pid); return process.ProcessName; }
        catch { return string.Empty; }
    }

    private const int WmClose = 0x0010;
    private const int WmGetIcon = 0x007F;
    private const int IconvSmall = 0;
    private const int IconvBig = 1;
    private const int GclpHicon = -14;
    private const int SmtoAbortIfHung = 0x0002;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwMinimize = 6;
    private const int SwMaximize = 3;
    private const int SwRestore = 9;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(nint hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(nint hwnd, string text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(nint hwnd, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SendMessageTimeoutW(nint hwnd, int message, nint wParam, nint lParam,
        int flags, int timeout, out nint result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtrW(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLongW(nint hwnd, int index);
}
