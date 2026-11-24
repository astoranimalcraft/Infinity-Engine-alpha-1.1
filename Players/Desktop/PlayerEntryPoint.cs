// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;

using Infinity.DIK;
using Infinity.Runtime;

namespace Infinity.Player;

/// <summary>
/// The whole of a built player's startup. The generated program is one line that calls this, so
/// everything here is ordinary compiled code with a debugger, tests and a compiler behind it.
/// </summary>
public static class PlayerEntryPoint
{
    /// <summary>
    /// Installs dependency resolution, then starts the game.
    /// </summary>
    /// <remarks>
    /// This method must not mention a type that pulls in a dependency living under <c>runtimes/</c>.
    /// Preparing a method loads every type in its body, and that happens before its first statement
    /// runs, so naming <see cref="DesktopPlayer"/> here would demand Paper before the resolver that
    /// finds Paper has been installed. Hence the split, and hence the NoInlining on the other half.
    /// </remarks>
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => ReportCrash(e.Exception);

        try
        {
            ManagedAssemblyResolver.Install();
            NativeLibraryResolver.Install();
            return Start(args);
        }
        catch (Exception e)
        {
            ReportCrash(e);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Start(string[] args)
    {
        var manifest = PlayerManifest.Load(AppContext.BaseDirectory);
        using var game = new DesktopPlayer(manifest);

        if (IsHeadless(args))
        {
            game.RunHeadless(ParseHeadless(args));
        }
        else
        {
            if (manifest.SecurityCheck && !HasFlag(args, "--skip-security"))
            {
                var scan = new SecurityScanOptions { GameDirectory = AppContext.BaseDirectory };
                if (!GameSecurityGuard.ConfirmLaunch(scan, 0, Math.Max(1, manifest.SecurityConfirmClicks)))
                {
                    Debug.LogWarning("[Security] O jogo foi fechado pelo alerta de segurança.");
                    return 2;
                }
            }

            PlayerSettingsLoader.ApplyBeforeWindow(Path.Combine(AppContext.BaseDirectory, "Content", "Settings"));
            game.Run(manifest.ProductName, manifest.WindowWidth, manifest.WindowHeight);
        }

        return 0;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds engine assemblies the build organized into Engine or Runtime. The host reads the deps file before
    /// this runs, so this is the fallback for assemblies copied into the output after publish (plugins and the
    /// project's own references), which have no deps entry pointing at the new folder.
    /// </summary>
    private static class ManagedAssemblyResolver
    {
        public static void Install()
        {
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                string file = (name.Name ?? "") + ".dll";
                foreach (string folder in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "Engine"), Path.Combine(AppContext.BaseDirectory, "Runtime") })
                {
                    string path = Path.Combine(folder, file);
                    if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
                }
                return null;
            };
        }
    }

    private static void ReportCrash(Exception? exception)
    {
        if (exception == null) return;

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "infinity-crash.log");
            string text =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Infinity Engine player crash{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                $"Runtime: {Environment.Version}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(path, text);
        }
        catch { }

        try
        {
            string details = exception.ToString();
            if (details.Length > 1400) details = details[..1400] + "\n...";
            DialogTools.Show(
                "O jogo encontrou um erro e foi fechado.\n\n" + details +
                "\n\nO log completo foi salvo em infinity-crash.log ao lado do executável.",
                "Infinity Engine - Erro",
                MessageBoxButtons.Ok,
                MessageBoxIcon.Error);
        }
        catch { }
    }

    private static bool IsHeadless(string[] args)
        => args.Contains("--headless") || args.Contains("-headless");

    private static HeadlessRunOptions ParseHeadless(string[] args)
    {
        var options = new HeadlessRunOptions();

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--frames" when long.TryParse(args[i + 1], out long frames):
                    options.MaxFrames = frames;
                    break;

                case "--seconds" when double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out double seconds):
                    options.MaxSeconds = seconds;
                    break;

                case "--fps" when int.TryParse(args[i + 1], out int fps):
                    options.TargetFrameRate = fps;
                    break;
            }
        }

        return options;
    }
}
