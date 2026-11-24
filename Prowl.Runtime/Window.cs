// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Infinity.Runtime.Diagnostics;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Infinity.Runtime;

/// <summary>How the game window occupies the screen, see <see cref="Window.Mode"/>.</summary>
public enum WindowMode { Windowed, Fullscreen, Borderless }

public static class Window
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public static IWindow InternalWindow { get; internal set; }
    public static IInputContext InternalInput { get; internal set; }

    // --- Per-monitor DPI awareness on Windows ----------------------------------------------
    // [DllImport("user32.dll")]
    // private static extern bool SetProcessDpiAwarenessContext(nint dpiContext);

    // [DllImport("shcore.dll")]
    // private static extern int SetProcessDpiAwareness(int level);

    // [DllImport("user32.dll")]
    // private static extern bool SetProcessDPIAware();

    // private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // private static void EnsureDpiAwareOnWindows()
    // {
    //     if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
    //     try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; } catch { }
    //     try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
    //     try { SetProcessDPIAware(); } catch { }
    // }

    public static event Action? Load;
    public static event Action<float>? Update;
    public static event Action<float>? Render;
    public static event Action<float>? PostRender;
    public static event Action<bool>? FocusChanged;
    public static event Action<Vector2D<int>>? Resize;
    public static event Action<Vector2D<int>>? FramebufferResize;
    public static event Action? Closing;

    /// <summary>Asked when the window is about to close. Returning false keeps it open.</summary>
    public static Func<bool>? CloseRequested;

    private static bool s_closeConfirmed;

    public static event Action<Vector2D<int>>? Move;
    public static event Action<WindowState>? StateChanged;
    public static event Action<string[]>? FileDrop;

    private static nint s_contentScaleProc;
    private static bool s_contentScaleResolved;

    /// <summary>
    /// Physical-to-logical pixel ratio for the window's current display.
    /// <para>
    /// On Windows, resolves <c>glfwGetWindowContentScale</c> via the native GLFW context
    /// to capture the system DPI factor (e.g. 1.25 at 125%). This is needed because
    /// <c>FramebufferSize / Size</c> only reflects the ratio when the process is
    /// per-monitor DPI-aware; if DPI awareness failed at startup the OS virtualises
    /// the framebuffer (fb == win) and the ratio collapses to 1.
    /// </para>
    /// <para>
    /// On macOS and Linux the GLFW proc address is not reachable via this path, so we
    /// fall back to <c>FramebufferSize / Size</c> which is always correct on those
    /// platforms (Retina: 3024 fb / 1512 points = 2; 1× monitor: 1).
    /// </para>
    /// </summary>
    public static unsafe float ContentScale
    {
        get
        {
            if (InternalWindow == null) return 1f;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                nint? nativeGlfw = InternalWindow.Native?.Glfw;
                if (nativeGlfw.HasValue && nativeGlfw.Value != 0 && TryGetContentScaleViaProc(nativeGlfw.Value, out float scale))
                    return scale;
            }

            Vector2D<int> fb = InternalWindow.FramebufferSize;
            Vector2D<int> win = InternalWindow.Size;
            return win.X > 0 ? (float)fb.X / win.X : 1f;
        }
    }

    private static unsafe bool TryGetContentScaleViaProc(nint glfwWindow, out float scale)
    {
        scale = 1f;

        if (!s_contentScaleResolved)
        {
            s_contentScaleResolved = true;
            try
            {
                s_contentScaleProc = Silk.NET.GLFW.GlfwProvider.GLFW.Value.Context.GetProcAddress("glfwGetWindowContentScale");
            }
            catch { }
        }

        if (s_contentScaleProc == 0) return false;

        float x, y;
        ((delegate* unmanaged[Cdecl]<nint, float*, float*, void>)s_contentScaleProc)(glfwWindow, &x, &y);
        if (x <= 0) return false;
        scale = x;
        return true;
    }

    public static Vector2D<int> Position
    {
        get { return InternalWindow.Position; }
        set { InternalWindow.Position = value; }
    }

    public static Vector2D<int> Size
    {
        get { return InternalWindow.Size; }
        set { InternalWindow.Size = value; }
    }

    private static WindowMode s_mode = WindowMode.Windowed;
    private static Vector2D<int> s_windowedPosition;
    private static Vector2D<int> s_windowedSize;

    /// <summary>
    /// Windowed, exclusive fullscreen, or a borderless window covering its monitor. Leaving fullscreen or
    /// borderless restores the last windowed position and size. Ignored without a window (headless), and
    /// ignored with a warning during editor play mode, where the window belongs to the editor.
    /// </summary>
    public static WindowMode Mode
    {
        get => s_mode;
        set
        {
            if (InternalWindow == null || s_mode == value) return;
            if (Application.IsEditor && Application.IsPlaying)
            {
                Debug.LogWarningOnce("Window.Mode.Editor", "Window.Mode is ignored in editor play mode, the window belongs to the editor.");
                return;
            }

            if (s_mode == WindowMode.Windowed)
            {
                s_windowedPosition = InternalWindow.Position;
                s_windowedSize = InternalWindow.Size;
            }
            s_mode = value;

            switch (value)
            {
                case WindowMode.Fullscreen:
                    InternalWindow.WindowBorder = WindowBorder.Resizable;
                    InternalWindow.WindowState = WindowState.Fullscreen;
                    break;

                case WindowMode.Borderless:
                    InternalWindow.WindowState = WindowState.Normal;
                    InternalWindow.WindowBorder = WindowBorder.Hidden;
                    if (InternalWindow.Monitor is { } monitor)
                    {
                        InternalWindow.Position = monitor.Bounds.Origin;
                        InternalWindow.Size = monitor.Bounds.Size;
                    }
                    break;

                default:
                    InternalWindow.WindowState = WindowState.Normal;
                    InternalWindow.WindowBorder = WindowBorder.Resizable;
                    if (s_windowedSize.X > 0 && s_windowedSize.Y > 0)
                    {
                        InternalWindow.Size = s_windowedSize;
                        InternalWindow.Position = s_windowedPosition;
                    }
                    break;
            }
        }
    }

    public static bool IsVisible
    {
        get { return InternalWindow.IsVisible; }
        set { InternalWindow.IsVisible = value; }
    }

    public static nint Handle
    {
        get { return InternalWindow.Handle; }
    }

    private static bool isFocused = true;
    private static DefaultInputHandler WindowInputHandler;
    private static string s_iconResource = "prowl-icon.rgba";
    private static int s_iconRefreshFrames;

    public static bool IsFocused
    {
        get { return isFocused; }
    }

    private static string s_taskbarAppId = "Astora.InfinityEngine";

    public static void InitWindow(string title, int width, int height, WindowState startState = WindowState.Normal,
        bool vsync = false, string? taskbarAppId = null)
    {
        if (!string.IsNullOrWhiteSpace(taskbarAppId)) s_taskbarAppId = taskbarAppId;
        WindowOptions options = WindowOptions.Default;
        options.Title = title;
        options.Size = new Vector2D<int>(width, height);
        options.WindowState = startState;
        s_mode = startState == WindowState.Fullscreen ? WindowMode.Fullscreen : WindowMode.Windowed;
        options.VSync = vsync;
        Application.VSync = vsync;
        // Update and Render are driven by MainLoop, and frames present through Graphite's swapchain.
        options.ShouldSwapAutomatically = false;
        options.API = new GraphicsAPI(ContextAPI.Vulkan, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(1, 3));
        InitializeVulkanLoader();

        // Set before the window exists: Windows groups the taskbar button by this id, and applying it afterwards
        // leaves a host process (dotnet, a launcher) owning the button and its generic icon.
        SetTaskbarAppId(s_taskbarAppId);

        // Silk finds platforms by reflection, which NativeAOT cannot do, so name them up front.
        Silk.NET.Windowing.Glfw.GlfwWindowing.RegisterPlatform();
        Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();
        InternalWindow = Silk.NET.Windowing.Window.Create(options);
        HookEvents();
    }

    private delegate void GlfwInitVulkanLoader(nint vkGetInstanceProcAddr);

    // GLFW on macOS only looks for the Vulkan SDK's loader, so it is handed MoltenVK's entry point before it starts.
    private static void InitializeVulkanLoader()
    {
        if (!OperatingSystem.IsMacOS()) return;
        string path = Path.Join(AppContext.BaseDirectory, "runtimes/osx/native/libMoltenVK.dylib");
        if (!File.Exists(path)) return;
        nint moltenVK = NativeLibrary.Load(path);
        nint getInstanceProcAddr = NativeLibrary.GetExport(moltenVK, "vkGetInstanceProcAddr");
        Silk.NET.GLFW.Glfw glfw = Silk.NET.GLFW.Glfw.GetApi();
        nint initVulkanLoader = glfw.Context.GetProcAddress("glfwInitVulkanLoader");
        if (initVulkanLoader != 0)
            Marshal.GetDelegateForFunctionPointer<GlfwInitVulkanLoader>(initVulkanLoader)(getInstanceProcAddr);
    }

    private static void HookEvents()
    {
        InternalWindow.Load += OnLoad;
        InternalWindow.Resize += OnResize;
        InternalWindow.FramebufferResize += OnFramebufferResize;
        InternalWindow.Move += OnMove;
        InternalWindow.StateChanged += (state) => StateChanged?.Invoke(state);
        InternalWindow.FileDrop += (files) => FileDrop?.Invoke(files);
        InternalWindow.FocusChanged += (focused) =>
        {
            isFocused = focused;
            FocusChanged?.Invoke(focused);
        };
    }

    private static void OnMove(Vector2D<int> d) => Move?.Invoke(d);

    public static void Start()
    {
        InternalWindow.Initialize();
        Load?.Invoke();

        try { MainLoop(); }
        finally
        {
            OnClose();
            InternalWindow.Reset();
        }
    }

    private static void MainLoop()
    {
        long lastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long freq = System.Diagnostics.Stopwatch.Frequency;

        FrameStats.Start();

        while (!ShouldExit())
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            float delta = (float)((now - lastTicks) / (double)freq);
            lastTicks = now;

            WindowInputHandler?.BeginFrame();
            InternalWindow.DoEvents();

            // The taskbar button is created from the window class before the first frame. Re-applying the
            // icon for the first few frames makes the button pick it up on startup instead of only after
            // the window is recreated (which is what happened when a project closed).
            if (s_iconRefreshFrames > 0)
            {
                s_iconRefreshFrames--;
                SetTaskbarAppId(s_taskbarAppId);
                TrySetWindowIcon();
            }

            Update?.Invoke(delta);
            WindowInputHandler?.LateUpdate();
            Render?.Invoke(delta);
            PostRender?.Invoke(delta);

            Graphics.SubmitFrame(delta);

            Application.WaitForNextFrame();

            FrameStats.EndFrame();
        }
    }

    /// <summary>Closes the window. A forced stop skips <see cref="CloseRequested"/>.</summary>
    public static void Stop(bool force = false)
    {
        s_closeConfirmed |= force;
        InternalWindow.Close();
    }

    private static bool ShouldExit()
    {
        if (!InternalWindow.IsClosing) return false;
        if (s_closeConfirmed || CloseRequested == null) return true;

        bool close;
        try { close = CloseRequested(); }
        catch (Exception ex)
        {
            Debug.LogError($"A close handler threw, so the window closes anyway: {ex}");
            close = true;
        }

        if (!close) InternalWindow.IsClosing = false;
        return close;
    }

    public static void OnLoad()
    {
        InternalInput = InternalWindow.CreateInput();
        WindowInputHandler = new DefaultInputHandler(InternalInput);
        Graphics.Initialize(InternalWindow, Application.VSync);
        Input.PushHandler(WindowInputHandler);
        SetTaskbarAppId(s_taskbarAppId);
        TrySetWindowIcon();
        s_iconRefreshFrames = 6;
    }

    /// <summary>Re-applies the window, title bar and taskbar icon, for when a project changes its icon while running.</summary>
    public static void RefreshWindowIcon() => TrySetWindowIcon();

    /// <summary>Uses the engine icon (the ribbon on its black tile) for the title bar and taskbar.</summary>
    public static void SetEngineIcon()
    {
        s_iconResource = "prowl-icon.rgba";
        s_iconRefreshFrames = 6;
        TrySetWindowIcon();
    }

    /// <summary>
    /// Keeps the engine tile for the title bar and taskbar when a project opens. The transparent project mark is
    /// used by the launcher and loading screen, where a background is not wanted.
    /// </summary>
    public static void SetProjectIcon()
    {
        s_iconResource = "prowl-icon.rgba";
        s_iconRefreshFrames = 6;
        TrySetWindowIcon();
    }

    // Windows groups taskbar buttons by an explicit application id. Without one a host process (dotnet, a test
    // runner, a launcher) decides the group, and the taskbar shows that host's icon instead of ours.
    public static void SetTaskbarAppId(string appId)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { SetCurrentProcessExplicitAppUserModelID(appId); }
        catch { /* older Windows builds do not export it */ }
    }


    /// <summary>
    /// Sets the window icon, which is what the title bar, the taskbar button and the Alt+Tab list show. The editor calls
    /// this when the project's icon changes, and a player gets it from <see cref="TrySetWindowIcon"/>.
    /// </summary>
    public static void SetWindowIcon(IReadOnlyList<Silk.NET.Core.RawImage> icons)
    {
        if (icons.Count == 0) return;

        try
        {
            var images = new Silk.NET.Core.RawImage[icons.Count];
            for (int i = 0; i < icons.Count; i++) images[i] = icons[i];
            InternalWindow.SetWindowIcon(images);
        }
        catch { /* the platform may not support icons at all */ }
    }

    // Sets the window icon from a raw-RGBA resource embedded in the entry assembly whose name is "prowl-icon.rgba".
    // Format: int32 count, then per image: int32 width, int32 height, width*height*4 straight-alpha RGBA.
    private static void TrySetWindowIcon()
    {
        if (TrySetEmbeddedWindowIcon(s_iconResource)) return;

        // A build that shipped an icon file next to the executable is as good as one that embedded it, and it is what
        // the project settings produce, since the icon has to be readable before any asset is loaded.
        SetWindowIcon(Infinity.Runtime.Resources.AppIcon.FindNextToExecutable());
    }

    private static bool TrySetEmbeddedWindowIcon(string resourceSuffix)
    {
        // Note: GLFW for Windows initially creates windows with an icon that is sourced from a specific named
        // resource ("GLFW_ICON"). In a .rc file the needed resource would look like this:
        //
        // GLFW_ICON ICON DISCARDABLE "<path to .ico file>"
        try
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            string name = System.Array.Find(asm?.GetManifestResourceNames() ?? System.Array.Empty<string>(),
                n => n.EndsWith("." + resourceSuffix, StringComparison.Ordinal));
            if (name == null) return false;

            using Stream? stream = asm!.GetManifestResourceStream(name);
            if (stream == null) return false;
            using var br = new System.IO.BinaryReader(stream);

            int count = br.ReadInt32();
            var icons = new Silk.NET.Core.RawImage[count];
            for (int i = 0; i < count; i++)
            {
                int w = br.ReadInt32();
                int h = br.ReadInt32();
                byte[] pixels = br.ReadBytes(w * h * 4);
                icons[i] = new Silk.NET.Core.RawImage(w, h, pixels);
            }
            InternalWindow.SetWindowIcon(icons);
            return true;
        }
        catch { /* no icon shipped, or the platform doesn't support it - keep the default */ }

        return false;
    }

    public static void OnResize(Vector2D<int> size) => Resize?.Invoke(size);
    public static void OnFramebufferResize(Vector2D<int> size)
    {
        Graphics.Resize(size.X, size.Y);
        FramebufferResize?.Invoke(size);
    }

    public static void OnClose()
    {
        // Stop background asset loading first so no load runs during teardown (it would
        // otherwise race scene unload and try to submit GPU work after the render thread exits).
        AssetLoader.Stop();
        Application.TargetFrameRate = 0; // and with it the finer system timer a limit holds

        Closing?.Invoke();
        WindowInputHandler.Dispose();
        Graphics.Dispose();
    }
}
