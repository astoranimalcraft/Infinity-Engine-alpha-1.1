// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Infinity.DIK;

/// <summary>Text and timing for a simulated blue screen. It never touches the real operating system display.</summary>
public sealed record BsodOptions
{
    public string Title { get; init; } = ":( Seu jogo encontrou um problema";
    public string Message { get; init; } = "Estamos coletando algumas informações de erro e reiniciaremos em seguida.";
    public string StopCode { get; init; } = "INFINITY_ENGINE_SIMULATED_BSOD";
    public string ProgressLabel { get; init; } = "Progresso";
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(12);
    public int BarWidth { get; init; } = 56;
    public bool UseNativeWindow { get; init; } = true;
    public bool AllowEscapeToClose { get; init; } = true;

    /// <summary>The stop code shown the way Windows shows a bug check, for a convincing simulation.</summary>
    public string BugCheckCode { get; init; } = "0x000000EF";
    public string BugCheckName { get; init; } = "CRITICAL_PROCESS_DIED";
    public string FailureText { get; init; } = "ntoskrnl.exe";
    public string KernelAddress { get; init; } = "ntoskrnl.exe+0x1a2b3c";
    public string DumpPath { get; init; } = @"C:\Windows\Minidump\112224-12345-01.dmp";
    public bool ShowTechnicalDetails { get; init; } = true;
    public bool ShowQrCode { get; init; } = true;
}

/// <summary>
/// Plays a safe, simulated blue screen with a percentage bar. It is a normal topmost window (or a console
/// fallback), so it cannot crash the machine. Thresholds let a game trigger events at 25%, 50%, 75% and 100%.
/// The overlay closes and progress returns to zero when it reaches 100%.
/// </summary>
public sealed class BsodSimulator : IDisposable
{
    private readonly BsodOptions _options;
    private readonly SynchronizationContext? _context;
    private CancellationTokenSource _cancellation = new();
    private Task? _loop;
    private BsodOverlay? _overlay;
    private readonly Stopwatch _clock = new();
    private int _nextThreshold;

    public BsodSimulator(BsodOptions? options = null)
    {
        _options = options ?? new BsodOptions();
        _context = SynchronizationContext.Current;
    }

    public BsodOptions Options => _options;

    /// <summary>Current progress from 0 to 100.</summary>
    public float Progress { get; private set; }

    /// <summary>True while the simulated blue screen is visible.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Percentages that raise <see cref="ThresholdReached"/>. Sorted ascending before use.</summary>
    public IReadOnlyList<int> Thresholds { get; set; } = [25, 50, 75, 100];

    public event Action<float>? ProgressChanged;
    public event Action<int>? ThresholdReached;
    public event Action? Completed;
    public event Action? Cancelled;

    /// <summary>Shows the overlay and starts counting in the background. Returns immediately.</summary>
    public void Start()
    {
        if (IsRunning) return;

        IsRunning = true;
        Progress = 0f;
        _nextThreshold = 0;
        _cancellation = new CancellationTokenSource();
        try
        {
            _overlay = BsodOverlay.Create(_options, this);
            _loop = Task.Run(() => Run(_cancellation.Token));
        }
        catch
        {
            IsRunning = false;
            _overlay?.Close();
            _overlay = null;
            throw;
        }
    }

    /// <summary>Closes the overlay immediately and restores progress to zero.</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        try { _cancellation.Cancel(); } catch { }
    }

    private async Task Run(CancellationToken token)
    {
        double duration = Math.Max(0.1, _options.Duration.TotalSeconds);
        bool reachedEnd = false;
        _clock.Restart();
        try
        {
            while (!token.IsCancellationRequested)
            {
                float progress = (float)Math.Clamp(_clock.Elapsed.TotalSeconds / duration * 100.0, 0.0, 100.0);
                Progress = progress;
                _overlay?.Update(progress);
                Raise(() => ProgressChanged?.Invoke(progress));

                int[] thresholds = [.. Thresholds];
                Array.Sort(thresholds);
                while (_nextThreshold < thresholds.Length && progress >= thresholds[_nextThreshold])
                {
                    int reached = thresholds[_nextThreshold++];
                    Raise(() => ThresholdReached?.Invoke(reached));
                }

                if (progress >= 100f)
                {
                    reachedEnd = true;
                    break;
                }

                await Task.Delay(33, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Raise(() => Cancelled?.Invoke());
        }
        finally
        {
            _overlay?.Close();
            _overlay = null;
            Progress = 0f;
            IsRunning = false;
            _clock.Stop();
            if (reachedEnd) Raise(() => Completed?.Invoke());
        }
    }

    private void Raise(Action action)
    {
        if (_context != null)
        {
            try { _context.Post(_ => action(), null); return; }
            catch { }
        }
        action();
    }

    public void Dispose()
    {
        Stop();
        try
        {
            if (_loop == null || _loop.Wait(750)) _cancellation.Dispose();
        }
        catch { }
        _overlay?.Close();
        _overlay = null;
    }
}

internal abstract class BsodOverlay : IDisposable
{
    public static BsodOverlay Create(BsodOptions options, BsodSimulator owner)
        => OperatingSystem.IsWindows() && options.UseNativeWindow
            ? new Win32BsodOverlay(options, owner)
            : new ConsoleBsodOverlay(options);

    public abstract void Update(float progress);
    public abstract void Close();
    public virtual void Dispose() => Close();
}

internal sealed class ConsoleBsodOverlay(BsodOptions options) : BsodOverlay
{
    private bool _started;

    public override void Update(float progress)
    {
        try
        {
            if (!_started)
            {
                _started = true;
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Clear();
            }

            Console.SetCursorPosition(0, 0);
            int width = Math.Max(40, Math.Min(Console.WindowWidth - 2, 100));
            string bar = new string(' ', Math.Max(1, (int)((width - 2) * progress / 100f)));
            string empty = new string(' ', Math.Max(0, width - 2 - bar.Length));
            Console.WriteLine(options.Title);
            Console.WriteLine();
            Console.WriteLine(options.Message);
            Console.WriteLine();
            Console.WriteLine($"{options.ProgressLabel}: {progress,6:0.0}%");
            Console.WriteLine($"[{bar}{empty}]");
            Console.WriteLine();
            Console.WriteLine(options.StopCode);
        }
        catch { /* A game without a console simply has no text fallback. */ }
    }

    public override void Close()
    {
        try
        {
            Console.ResetColor();
            Console.Clear();
        }
        catch { }
    }
}

internal sealed class Win32BsodOverlay : BsodOverlay
{
    private const int WmCreate = 0x0001;
    private const int WmDestroy = 0x0002;
    private const int WmPaint = 0x000F;
    private const int WmClose = 0x0010;
    private const int WmKeyDown = 0x0100;
    private const int WmTimer = 0x0113;
    private const int VkEscape = 0x1B;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int DtLeft = 0x0000;
    private const int DtWordBreak = 0x0010;
    private const int DtNoPrefix = 0x0800;
    private const int FwNormal = 400;
    private const int FwBold = 700;
    private const int DefaultCharset = 1;
    private const int OutDefaultPrecis = 0;
    private const int ClipDefaultPrecis = 0;
    private const int CleartypeQuality = 5;
    private const int DefaultPitch = 0;
    private const int FfDontCare = 0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint ColorWindow = 5;

    private readonly BsodOptions _options;
    private readonly BsodSimulator _owner;
    private readonly Thread _thread;
    private readonly WindowProcDelegate _wndProc;
    private nint _hwnd;
    private volatile float _progress;
    private volatile bool _closed;
    private GCHandle _self;

    public Win32BsodOverlay(BsodOptions options, BsodSimulator owner)
    {
        _options = options;
        _owner = owner;
        _self = GCHandle.Alloc(this);
        _wndProc = WndProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "Infinity BSOD overlay" };
        _thread.Start();
    }

    public override void Update(float progress)
    {
        _progress = progress;
        if (_hwnd != 0) InvalidateRect(_hwnd, 0, false);
    }

    public override void Close()
    {
        _closed = true;
        if (_hwnd != 0) PostMessageW(_hwnd, WmClose, 0, 0);
        try { _thread.Join(750); } catch { }
        if (_self.IsAllocated) _self.Free();
    }

    private void Run()
    {
        string className = "InfinityBsodOverlay_" + Environment.ProcessId;
        nint instance = GetModuleHandleW(null);
        var windowClass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            Style = 0,
            WindowProc = _wndProc,
            ClsExtra = 0,
            WndExtra = 0,
            Instance = instance,
            Icon = 0,
            Cursor = LoadCursorW(0, 32512),
            Background = 0,
            MenuName = null,
            ClassName = className,
            IconSmall = 0,
        };

        try
        {
            RegisterClassExW(ref windowClass);
            int width = GetSystemMetrics(SmCxScreen);
            int height = GetSystemMetrics(SmCyScreen);
            _hwnd = CreateWindowExW(0x00000008 | 0x00000080, className, _options.Title,
                0x80000000 | 0x10000000, 0, 0, width, height, 0, 0, instance, GCHandle.ToIntPtr(_self));
            if (_hwnd == 0) return;
            if (_closed)
            {
                DestroyWindow(_hwnd);
                _hwnd = 0;
                return;
            }

            ShowWindow(_hwnd, 5);
            UpdateWindow(_hwnd);
            SetTimer(_hwnd, 1, 50, 0);
            SetWindowPos(_hwnd, -1, 0, 0, width, height, SwpShowWindow | SwpNoActivate);

            while (GetMessageW(out Msg message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch { }
        finally
        {
            if (_hwnd != 0) DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmCreate:
                return 0;
            case WmPaint:
            {
                Paint(hwnd);
                return 0;
            }
            case WmTimer:
                InvalidateRect(hwnd, 0, false);
                return 0;
            case WmKeyDown:
                if (wParam == VkEscape && _options.AllowEscapeToClose)
                {
                    _owner.Stop();
                    return 0;
                }
                break;
            case WmClose:
                DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void Paint(nint hwnd)
    {
        if (!GetClientRect(hwnd, out Rect rect)) return;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        nint dc = BeginPaint(hwnd, out PaintStruct paint);
        try
        {
            nint background = CreateSolidBrush(Rgb(0, 120, 215));
            FillRect(dc, ref rect, background);
            DeleteObject(background);

            SetBkMode(dc, 1);
            SetTextColor(dc, Rgb(255, 255, 255));

            nint titleFont = CreateFontW(Scale(64), 0, 0, 0, FwBold, 0, 0, 0, DefaultCharset,
                OutDefaultPrecis, ClipDefaultPrecis, CleartypeQuality, DefaultPitch | FfDontCare, "Segoe UI");
            nint bodyFont = CreateFontW(Scale(26), 0, 0, 0, FwNormal, 0, 0, 0, DefaultCharset,
                OutDefaultPrecis, ClipDefaultPrecis, CleartypeQuality, DefaultPitch | FfDontCare, "Segoe UI");
            nint smallFont = CreateFontW(Scale(18), 0, 0, 0, FwNormal, 0, 0, 0, DefaultCharset,
                OutDefaultPrecis, ClipDefaultPrecis, CleartypeQuality, DefaultPitch | FfDontCare, "Segoe UI");

            int left = Scale(80);
            int top = Scale(80);
            int textWidth = Math.Max(100, width - left * 2);

            nint previous = SelectObject(dc, titleFont);
            var titleRect = new Rect(left, top, left + textWidth, top + Scale(90));
            DrawTextW(dc, _options.Title, -1, ref titleRect, DtLeft | DtWordBreak | DtNoPrefix);

            SelectObject(dc, bodyFont);
            top += Scale(110);
            var messageRect = new Rect(left, top, left + textWidth, top + Scale(90));
            DrawTextW(dc, _options.Message, -1, ref messageRect, DtLeft | DtWordBreak | DtNoPrefix);

            if (_options.ShowTechnicalDetails)
            {
                SelectObject(dc, smallFont);
                int detailsTop = top + Scale(100);
                int detailsWidth = Math.Max(100, textWidth - Scale(240));
                var detailsRect = new Rect(left, detailsTop, left + detailsWidth, detailsTop + Scale(170));
                string details =
                    $"O que falhou: {_options.FailureText}\r\n" +
                    $"Código de parada: {_options.BugCheckCode} ({_options.BugCheckName})\r\n" +
                    $"Kernel: {_options.KernelAddress}\r\n" +
                    $"Despejo: {_options.DumpPath}";
                DrawTextW(dc, details, -1, ref detailsRect, DtLeft | DtWordBreak | DtNoPrefix);

                if (_options.ShowQrCode)
                    DrawQrCode(dc, left + textWidth - Scale(200), detailsTop, Scale(180));
            }

            int barTop = height - Scale(150);
            int barHeight = Scale(28);
            string percent = $"{_progress:0.0}%";
            SelectObject(dc, bodyFont);
            var percentRect = new Rect(left, barTop - Scale(36), left + textWidth, barTop - Scale(4));
            DrawTextW(dc, $"{_options.ProgressLabel}: {percent}", -1, ref percentRect, DtLeft | DtNoPrefix);

            nint track = CreateSolidBrush(Rgb(20, 80, 150));
            var trackRect = new Rect(left, barTop, left + textWidth, barTop + barHeight);
            FillRect(dc, ref trackRect, track);
            DeleteObject(track);

            int filled = (int)(textWidth * Math.Clamp(_progress, 0f, 100f) / 100f);
            if (filled > 0)
            {
                nint fill = CreateSolidBrush(Rgb(255, 255, 255));
                var fillRect = new Rect(left, barTop, left + filled, barTop + barHeight);
                FillRect(dc, ref fillRect, fill);
                DeleteObject(fill);
            }

            SelectObject(dc, smallFont);
            var stopRect = new Rect(left, barTop + Scale(44), left + textWidth, barTop + Scale(80));
            DrawTextW(dc, _options.StopCode, -1, ref stopRect, DtLeft | DtNoPrefix);
            var hintRect = new Rect(left, barTop + Scale(78), left + textWidth, barTop + Scale(112));
            DrawTextW(dc, "Pressione ESC para fechar a simulação.", -1, ref hintRect, DtLeft | DtNoPrefix);

            SelectObject(dc, previous);
            DeleteObject(titleFont);
            DeleteObject(bodyFont);
            DeleteObject(smallFont);
        }
        finally
        {
            EndPaint(hwnd, ref paint);
        }
    }

    private void DrawQrCode(nint dc, int x, int y, int size)
    {
        const int cells = 21;
        int cell = Math.Max(1, size / cells);
        nint white = CreateSolidBrush(Rgb(255, 255, 255));
        nint black = CreateSolidBrush(Rgb(0, 0, 0));
        try
        {
            var background = new Rect(x, y, x + cell * cells, y + cell * cells);
            FillRect(dc, ref background, white);

            uint hash = 2166136261;
            foreach (char c in _options.StopCode)
                hash = (hash ^ c) * 16777619;
            hash ^= (uint)_options.BugCheckCode.GetHashCode();

            for (int row = 0; row < cells; row++)
            {
                for (int column = 0; column < cells; column++)
                {
                    bool finder =
                        (row < 7 && column < 7) ||
                        (row < 7 && column >= cells - 7) ||
                        (row >= cells - 7 && column < 7);
                    bool on = finder
                        ? !(row is 1 or 5 || column is 1 or 5) && !(row is >= 2 and <= 4 && column is >= 2 and <= 4)
                        : ((hash >> ((row * 3 + column) & 31)) & 1) != 0;
                    if (!on) continue;
                    var block = new Rect(x + column * cell, y + row * cell,
                        x + (column + 1) * cell, y + (row + 1) * cell);
                    FillRect(dc, ref block, black);
                }
            }
        }
        finally
        {
            DeleteObject(white);
            DeleteObject(black);
        }
    }

    private int Scale(int value)
    {
        uint dpi = _hwnd != 0 ? GetDpiForWindow(_hwnd) : 96;
        if (dpi == 0) dpi = 96;
        return (int)Math.Round(value * dpi / 96.0);
    }

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public Rect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint DeviceContext;
        public bool Erase;
        public Rect Paint;
        public bool Restore;
        public bool IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WindowProcDelegate WindowProc;
        public int ClsExtra;
        public int WndExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string? ClassName;
        public nint IconSmall;
    }

    private delegate nint WindowProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hwnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(nint hwnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint dc, ref Rect rect, nint brush);

    [DllImport("user32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("user32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("user32.dll")]
    private static extern int SetBkMode(nint dc, int mode);

    [DllImport("user32.dll")]
    private static extern uint SetTextColor(nint dc, uint color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [DllImport("user32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(nint dc, string text, int length, ref Rect rect, int format);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint SetTimer(nint hwnd, nint id, uint interval, nint callback);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg message, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref Msg message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint instance, int cursor);

}
