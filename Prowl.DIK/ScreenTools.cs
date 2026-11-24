// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

namespace Infinity.DIK;

/// <summary>A rectangle on the desktop, in screen pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>
/// Desktop geometry and cursor control. These calls are implemented on Windows; other platforms report
/// <see cref="IsSupported"/> false and return empty values.
/// </summary>
public static class ScreenTools
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>The primary monitor's rectangle.</summary>
    public static ScreenRect PrimaryScreen
    {
        get
        {
            if (!IsSupported) return default;
            return new ScreenRect(0, 0, GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));
        }
    }

    /// <summary>The rectangle covering every monitor.</summary>
    public static ScreenRect VirtualScreen
    {
        get
        {
            if (!IsSupported) return default;
            return new ScreenRect(
                GetSystemMetrics(SmXVirtualScreen),
                GetSystemMetrics(SmYVirtualScreen),
                GetSystemMetrics(SmCxVirtualScreen),
                GetSystemMetrics(SmCyVirtualScreen));
        }
    }

    /// <summary>Current mouse position, or null when the platform cannot read it.</summary>
    public static WindowPoint? CursorPosition
    {
        get
        {
            if (!IsSupported || !GetCursorPos(out Point point)) return null;
            return new WindowPoint(point.X, point.Y);
        }
        set
        {
            if (IsSupported && value is { } point) SetCursorPos(point.X, point.Y);
        }
    }

    /// <summary>The monitor that contains most of the window, or null.</summary>
    public static ScreenRect? GetMonitorBounds(nint hwnd)
    {
        if (!IsSupported || hwnd == 0) return null;
        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == 0) return null;
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return null;
        return new ScreenRect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top);
    }

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
}
