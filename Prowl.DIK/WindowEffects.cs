// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Infinity.DIK;

/// <summary>One running window shake. Dispose or call Stop to restore the original position immediately.</summary>
public sealed class WindowShake : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _task;

    internal WindowShake(nint hwnd, int intensity, TimeSpan duration, double frequency)
    {
        _task = Task.Run(() =>
        {
            WindowBounds? original = WindowTools.GetBounds(hwnd);
            if (original is not { } start) return;

            var clock = Stopwatch.StartNew();
            try
            {
                while (clock.Elapsed < duration && !_cancellation.IsCancellationRequested)
                {
                    double seconds = clock.Elapsed.TotalSeconds;
                    int dx = (int)Math.Round(Math.Sin(seconds * Math.Tau * frequency) * intensity);
                    int dy = (int)Math.Round(Math.Cos(seconds * Math.Tau * frequency * 1.31) * intensity);
                    WindowTools.SetPosition(hwnd, start.X + dx, start.Y + dy);
                    Thread.Sleep(16);
                }
            }
            finally
            {
                WindowTools.SetBounds(hwnd, start.X, start.Y, start.Width, start.Height);
            }
        });
    }

    /// <summary>True until the shake finishes or is stopped.</summary>
    public bool IsRunning => !_task.IsCompleted;

    /// <summary>Stops the shake and restores the window position.</summary>
    public void Stop() => _cancellation.Cancel();

    /// <summary>Stops the shake and waits briefly for the original position to be restored.</summary>
    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            if (_task.Wait(500)) _cancellation.Dispose();
        }
        catch { }
    }
}

public static partial class WindowTools
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExTransparent = 0x00000020L;
    private const int LwaAlpha = 0x00000002;
    private const nint HwndTopMost = -1;
    private const nint HwndNoTopMost = -2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>
    /// Removes the title bar, resize border and system menu. Useful for a game's final fullscreen or
    /// desktop-pet window; call with false to restore the standard frame.
    /// </summary>
    public static bool SetBorderless(nint hwnd, bool borderless = true)
    {
        if (!IsSupported || hwnd == 0) return false;
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        long exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();

        if (borderless)
            style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
        else
            style |= WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu;

        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle));
        return SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>Keeps the window above normal windows.</summary>
    public static bool SetAlwaysOnTop(nint hwnd, bool onTop = true)
    {
        if (!IsSupported || hwnd == 0) return false;
        return SetWindowPos(hwnd, onTop ? HwndTopMost : HwndNoTopMost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Sets window opacity from 0 (invisible) to 255 (opaque).</summary>
    public static bool SetOpacity(nint hwnd, byte opacity)
    {
        if (!IsSupported || hwnd == 0) return false;
        long exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle | WsExLayered));
        return SetLayeredWindowAttributes(hwnd, 0, opacity, LwaAlpha);
    }

    /// <summary>Makes the window ignore mouse input, so clicks pass through to the window below.</summary>
    public static bool SetClickThrough(nint hwnd, bool clickThrough = true)
    {
        if (!IsSupported || hwnd == 0) return false;
        long exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        long updated = clickThrough
            ? exStyle | WsExLayered | WsExTransparent
            : exStyle & ~WsExTransparent;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(updated));
        return true;
    }

    /// <summary>Reads the foreground window handle, or zero.</summary>
    public static nint GetActiveWindow()
        => IsSupported ? GetForegroundWindow() : 0;

    /// <summary>Starts a short camera-shake style movement of a window. Null when the window is gone.</summary>
    public static WindowShake? Shake(nint hwnd, int intensity = 12, int durationMilliseconds = 500, double frequency = 18)
    {
        if (!IsSupported || hwnd == 0 || intensity <= 0 || durationMilliseconds <= 0 || frequency <= 0)
            return null;
        if (GetBounds(hwnd) == null) return null;
        return new WindowShake(hwnd, intensity, TimeSpan.FromMilliseconds(durationMilliseconds), frequency);
    }

    private static IntPtr GetWindowLongPtr(nint hwnd, int index)
        => IntPtr.Size == 8
            ? GetWindowLongPtrW(hwnd, index)
            : new IntPtr(GetWindowLongW(hwnd, index));

    private static IntPtr SetWindowLongPtr(nint hwnd, int index, IntPtr value)
        => IntPtr.Size == 8
            ? SetWindowLongPtrW(hwnd, index, value)
            : new IntPtr(SetWindowLongW(hwnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(nint hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLongW(nint hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, int flags);
}
