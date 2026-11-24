// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infinity.DIK;

/// <summary>One stage of a fake intrusion sequence.</summary>
public sealed record HackStage(string Name, string Description, float Progress);

/// <summary>Text and timing for a safe fake intrusion. Nothing here touches the real system.</summary>
public sealed class HackSimulationOptions
{
    public string TargetName { get; init; } = "THIS-PC";
    public string AttackerName { get; init; } = "UNKNOWN";
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(8);
    public int Seed { get; init; } = 1337;
    public bool ShowTerminal { get; init; } = true;
    public bool ShowDlls { get; init; } = true;
    public bool ShowRegistry { get; init; } = true;
    public bool ShowFiles { get; init; } = true;
    public string FinalMessage { get; init; } = "ACESSO CONCEDIDO";
}

/// <summary>
/// Plays a cinematic fake hack: fake terminal lines, loaded DLL names, startup registry entries and
/// fake file paths. It is a show, not an exploit; it never writes files, edits the registry, injects code
/// or contacts another machine.
/// </summary>
public sealed class HackSimulation : IDisposable
{
    private readonly HackSimulationOptions _options;
    private readonly SynchronizationContext? _context;
    private CancellationTokenSource _cancel = new();
    private Task? _loop;
    private int _nextStage;

    public HackSimulation(HackSimulationOptions? options = null)
    {
        _options = options ?? new HackSimulationOptions();
        _context = SynchronizationContext.Current;
        Seed = _options.Seed;
        TerminalLines = BuildTerminalLines();
        Dlls = BuildDlls();
        RegistryKeys = BuildRegistryKeys();
        Files = BuildFiles();
        Stages = BuildStages();
    }

    public int Seed { get; }
    public IReadOnlyList<HackStage> Stages { get; }
    public IReadOnlyList<string> TerminalLines { get; }
    public IReadOnlyList<string> Dlls { get; }
    public IReadOnlyList<string> RegistryKeys { get; }
    public IReadOnlyList<string> Files { get; }

    public float Progress { get; private set; }
    public int CurrentStage { get; private set; } = -1;
    public bool IsRunning { get; private set; }

    public event Action<int, HackStage>? StageChanged;
    public event Action<float>? ProgressChanged;
    public event Action? Completed;
    public event Action? Cancelled;

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        Progress = 0;
        CurrentStage = -1;
        _nextStage = 0;
        _cancel = new CancellationTokenSource();
        _loop = Task.Run(() => Run(_cancel.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try { _cancel.Cancel(); } catch { }
    }

    private async Task Run(CancellationToken token)
    {
        double duration = Math.Max(0.1, _options.Duration.TotalSeconds);
        var clock = Stopwatch.StartNew();
        bool reachedEnd = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                float progress = (float)Math.Clamp(clock.Elapsed.TotalSeconds / duration * 100.0, 0.0, 100.0);
                Progress = progress;
                Raise(() => ProgressChanged?.Invoke(progress));

                while (_nextStage < Stages.Count && progress >= Stages[_nextStage].Progress)
                {
                    int index = _nextStage++;
                    CurrentStage = index;
                    HackStage stage = Stages[index];
                    Raise(() => StageChanged?.Invoke(index, stage));
                }

                if (progress >= 100)
                {
                    reachedEnd = true;
                    break;
                }

                await Task.Delay(40, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Raise(() => Cancelled?.Invoke());
        }
        finally
        {
            Progress = 0;
            IsRunning = false;
            if (reachedEnd) Raise(() => Completed?.Invoke());
        }
    }

    private void Raise(Action action)
    {
        if (_context != null)
        {
            try { _context.Post(_ => action(), null); return; } catch { }
        }
        action();
    }

    public void Dispose()
    {
        Stop();
        try { if (_loop == null || _loop.Wait(750)) _cancel.Dispose(); } catch { }
    }

    private IReadOnlyList<string> BuildTerminalLines()
    {
        if (!_options.ShowTerminal) return [];
        string target = _options.TargetName;
        return
        [
            $"iniciando sonda em {target}...",
            "porta 22/tcp aberta",
            "porta 445/tcp aberta",
            "tentando credenciais padrão...",
            "acesso de administrator concedido",
            "lendo lista de módulos do kernel...",
            "coletando nomes de DLLs...",
            "lendo chaves de inicialização...",
            "mapeando arquivos de usuário...",
            "criptografando pacotes de sessão...",
            "escrevendo registro de acesso...",
            "conexão remota estabelecida",
            _options.FinalMessage,
        ];
    }

    private IReadOnlyList<string> BuildDlls()
    {
        if (!_options.ShowDlls) return [];
        var names = DllTools.GetLoadedModules()
            .Take(8)
            .Select(dll => dll.Name)
            .ToList();
        if (names.Count == 0)
            names.AddRange(["kernel32.dll", "ntdll.dll", "user32.dll", "advapi32.dll", "winhttp.dll"]);
        return names;
    }

    private IReadOnlyList<string> BuildRegistryKeys()
    {
        if (!_options.ShowRegistry) return [];
        var keys = RegistryTools.GetStartupEntries()
            .Take(6)
            .Select(entry => $"{entry.Hive}\\{entry.Key}\\{entry.Name}")
            .ToList();
        if (keys.Count == 0)
            keys.AddRange(
            [
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WindowsSecurity",
                @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\SystemUpdate",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce\DriverHelper",
            ]);
        return keys;
    }

    private IReadOnlyList<string> BuildFiles()
    {
        if (!_options.ShowFiles) return [];
        string root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Path.Combine(root, "Documents", "passwords.txt"),
            Path.Combine(root, "Desktop", "wallet.dat"),
            Path.Combine(root, "Pictures", "private.zip"),
            Path.Combine(root, "AppData", "Roaming", "browser", "cookies.sqlite"),
            Path.Combine(root, "Downloads", "backup.rar"),
        ];
    }

    private IReadOnlyList<HackStage> BuildStages()
    {
        float step = 100f / 7f;
        return
        [
            new("Sondando portas", "Procurando serviços expostos.", step * 1),
            new("Testando credenciais", "Testando senhas comuns.", step * 2),
            new("Lendo DLLs", "Listando módulos carregados no sistema.", step * 3),
            new("Abrindo registro", "Lendo chaves de inicialização.", step * 4),
            new("Mapeando arquivos", "Listando documentos e backups.", step * 5),
            new("Elevando privilégios", "Simulando acesso de administrador.", step * 6),
            new("Acesso concedido", _options.FinalMessage, 100),
        ];
    }
}
