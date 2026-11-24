// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Infinity.DIK;

/// <summary>How much a security finding matters.</summary>
public enum SecuritySeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>One file, registry entry or loaded module the guard does not trust.</summary>
public sealed record SecurityIssue(
    string Code,
    SecuritySeverity Severity,
    string Path,
    string Message,
    string Recommendation,
    bool BlocksLaunch = false);

/// <summary>What the guard looks at. Every scan is read-only.</summary>
public sealed class SecurityScanOptions
{
    /// <summary>The folder the game executable and its DLLs live in.</summary>
    public string GameDirectory { get; init; } = AppContext.BaseDirectory;

    public bool ScanAssemblies { get; init; } = true;
    public bool ScanScripts { get; init; } = true;
    public bool ScanGameManifest { get; init; } = true;
    public bool ScanRegistryStartup { get; init; } = true;
    public bool ScanLoadedModules { get; init; } = true;

    /// <summary>Files larger than this are checked for their header only, not searched for strings.</summary>
    public long MaxSearchedFileBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>Extra folders, relative to <see cref="GameDirectory"/>, to leave alone.</summary>
    public IReadOnlyCollection<string> IgnoredFolders { get; init; } =
        ["runtimes", "Content", "Settings", "Logs", "Library", "Temp", "Cache", "bin", "obj", ".git"];

    /// <summary>Names the game considers trusted even when they contain a risky string.</summary>
    public IReadOnlyCollection<string> TrustedFileNames { get; init; } = [];
}

/// <summary>The result of a startup scan.</summary>
public sealed record SecurityReport(
    IReadOnlyList<SecurityIssue> Issues,
    int FilesScanned,
    int AssembliesScanned,
    int ScriptsScanned,
    DateTime CompletedUtc)
{
    public bool HasIssues => Issues.Count > 0;

    public bool HasBlockingIssues => Issues.Any(issue => issue.BlocksLaunch || issue.Severity == SecuritySeverity.Critical);

    public int CriticalCount => Issues.Count(issue => issue.Severity == SecuritySeverity.Critical);
    public int WarningCount => Issues.Count(issue => issue.Severity == SecuritySeverity.Warning);

    /// <summary>A message box body: the problem, the files, and what the player can do next.</summary>
    public string FormatForDialog(int maxLines = 12)
    {
        if (!HasIssues)
            return "Nenhum arquivo suspeito ou corrompido foi encontrado.";

        var text = new StringBuilder();
        text.AppendLine("O jogo não foi iniciado porque pode prejudicar este PC.");
        text.AppendLine();
        text.AppendLine("Arquivos suspeitos ou corrompidos:");
        foreach (SecurityIssue issue in Issues
                     .OrderByDescending(issue => issue.Severity)
                     .ThenBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
                     .Take(Math.Max(1, maxLines)))
        {
            string name = string.IsNullOrEmpty(issue.Path) ? issue.Code : System.IO.Path.GetFileName(issue.Path);
            text.AppendLine($"- {name}: {issue.Message}");
        }

        int hidden = Issues.Count - Math.Max(1, maxLines);
        if (hidden > 0)
            text.AppendLine($"- ... e mais {hidden} arquivo(s).");

        text.AppendLine();
        text.AppendLine("Clique em SIM para executar mesmo assim.");
        text.AppendLine("Clique em NÃO para fechar o jogo.");
        return text.ToString();
    }
}

/// <summary>
/// A small, read-only safety check for a game folder. It looks for damaged PE files, process-injection
/// imports, dangerous script commands and suspicious Windows startup entries. It cannot clean, quarantine
/// or repair anything, and it never injects code or changes the registry.
/// </summary>
public static class SecurityScanner
{
    private static readonly string[] s_scriptExtensions =
        [".cs", ".js", ".ts", ".ps1", ".bat", ".cmd", ".vbs", ".sh", ".py"];

    private static readonly string[] s_injectionApis =
    [
        "VirtualAllocEx", "WriteProcessMemory", "CreateRemoteThread", "NtUnmapViewOfSection",
        "SetWindowsHookEx", "QueueUserAPC", "RtlCreateUserThread", "NtCreateThreadEx",
        "NtWriteVirtualMemory", "NtAllocateVirtualMemory",
    ];

    private static readonly (Regex Pattern, string Code, string Message, SecuritySeverity Severity)[] s_scriptPatterns =
    [
        (new(@"powershell(?:\.exe)?[^\r\n]{0,40}(?:-enc|-encodedcommand)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_POWERSHELL_ENCODED", "PowerShell codificado, usado para esconder comandos.", SecuritySeverity.Critical),
        (new(@"\b(?:IEX|Invoke-Expression)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_DYNAMIC_EXECUTION", "Execução dinâmica de código recebido de fora.", SecuritySeverity.Critical),
        (new(@"\b(?:DownloadString|DownloadFile|Invoke-WebRequest|Invoke-RestMethod)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_REMOTE_DOWNLOAD", "Download de código ou arquivos remotos.", SecuritySeverity.Warning),
        (new(@"\bbcdedit\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_BOOT_EDIT", "Tentativa de alterar a inicialização do Windows.", SecuritySeverity.Critical),
        (new(@"vssadmin[^\r\n]{0,40}delete[^\r\n]{0,20}shadows", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_SHADOW_DELETE", "Exclusão de cópias de segurança (shadow copies).", SecuritySeverity.Critical),
        (new(@"\bschtasks(?:\.exe)?[^\r\n]{0,40}/create\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_SCHEDULED_TASK", "Criação de tarefa agendada persistente.", SecuritySeverity.Warning),
        (new(@"\b(?:mimikatz|sekurlsa|lsass)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_CREDENTIAL_DUMP", "Possível captura de credenciais do sistema.", SecuritySeverity.Critical),
        (new(@"\b(?:keylog|GetAsyncKeyState|SetWindowsHookEx)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_KEYLOGGER", "Possível captura de teclado.", SecuritySeverity.Critical),
        (new(@"\b(?:ransom|bitcoin|encrypt[^\r\n]{0,30}(?:file|folder|disk))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_RANSOMWARE", "Possível comportamento de ransomware.", SecuritySeverity.Critical),
        (new(@"reg(?:\.exe)?\s+add\s+(?:HKLM|HKEY_LOCAL_MACHINE)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "SCRIPT_REGISTRY_PERSISTENCE", "Gravação de persistência no registro do Windows.", SecuritySeverity.Critical),
    ];

    private static readonly string[] s_dangerousStartupCommands =
        ["powershell", "pwsh", "mshta", "wscript", "cscript", "rundll32", "regsvr32", "cmd.exe /c", "cmd /c", "-enc", "base64"];

    public static SecurityReport Scan(SecurityScanOptions? options = null)
    {
        options ??= new SecurityScanOptions();
        var issues = new List<SecurityIssue>();
        int files = 0, assemblies = 0, scripts = 0;
        string root;

        try { root = Path.GetFullPath(options.GameDirectory); }
        catch (Exception e)
        {
            return new SecurityReport(
                [new SecurityIssue("SCAN_BAD_PATH", SecuritySeverity.Critical, options.GameDirectory,
                    $"Não foi possível abrir a pasta do jogo: {e.Message}", "Reinstale o jogo.", true)],
                0, 0, 0, DateTime.UtcNow);
        }

        if (!Directory.Exists(root))
        {
            return new SecurityReport(
                [new SecurityIssue("SCAN_MISSING_FOLDER", SecuritySeverity.Critical, root,
                    "A pasta do jogo não existe.", "Reinstale o jogo.", true)],
                0, 0, 0, DateTime.UtcNow);
        }

        if (options.ScanGameManifest && !File.Exists(Path.Combine(root, "player.manifest")))
        {
            return new SecurityReport(
                [new SecurityIssue("MANIFEST_MISSING", SecuritySeverity.Critical, Path.Combine(root, "player.manifest"),
                    "O manifesto do jogo está faltando; a instalação está incompleta.", "Reinstale o jogo.", true)],
                0, 0, 0, DateTime.UtcNow);
        }

        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault,
            });
        }
        catch (Exception e)
        {
            return new SecurityReport(
                [new SecurityIssue("SCAN_UNREADABLE", SecuritySeverity.Critical, root,
                    $"Não foi possível ler a pasta do jogo: {e.Message}", "Verifique as permissões e reinstale.", true)],
                0, 0, 0, DateTime.UtcNow);
        }

        foreach (string path in paths)
        {
            if (ShouldSkip(path, root, options)) continue;
            files++;

            string extension = Path.GetExtension(path);
            if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                assemblies++;
                ScanAssembly(path, root, options, issues);
            }
            else if (s_scriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                scripts++;
                ScanScript(path, options, issues);
            }
        }

        if (options.ScanRegistryStartup)
            ScanRegistryStartup(issues, root);

        if (options.ScanLoadedModules)
            ScanLoadedModules(issues);

        return new SecurityReport(issues, files, assemblies, scripts, DateTime.UtcNow);
    }

    private static bool ShouldSkip(string path, string root, SecurityScanOptions options)
    {
        string relative = Path.GetRelativePath(root, path);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string part in parts)
            foreach (string ignored in options.IgnoredFolders)
                if (part.Equals(ignored, StringComparison.OrdinalIgnoreCase))
                    return true;

        string name = Path.GetFileName(path);
        return options.TrustedFileNames.Any(trusted => trusted.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static void ScanAssembly(string path, string root, SecurityScanOptions options, List<SecurityIssue> issues)
    {
        string name = Path.GetFileName(path);
        long size;
        try { size = new FileInfo(path).Length; }
        catch { return; }

        if (size == 0)
        {
            issues.Add(new SecurityIssue("FILE_EMPTY", SecuritySeverity.Critical, path,
                "Arquivo vazio ou truncado.", "Reinstale o jogo.", true));
            return;
        }

        if (!DllTools.HasValidPeHeader(path))
        {
            issues.Add(new SecurityIssue("PE_INVALID", SecuritySeverity.Critical, path,
                "Cabeçalho PE inválido; o arquivo está corrompido.", "Reinstale o jogo.", true));
            return;
        }

        // Engine and Microsoft files are expected to mention process and window APIs. Only game-owned
        // DLLs are searched for injection imports, which keeps the check useful without flagging the engine.
        if (IsTrustedAssemblyName(name) || size > options.MaxSearchedFileBytes) return;

        string text;
        try { text = Encoding.Latin1.GetString(File.ReadAllBytes(path)); }
        catch { return; }

        foreach (string api in s_injectionApis)
        {
            if (!text.Contains(api, StringComparison.Ordinal)) continue;
            issues.Add(new SecurityIssue("PE_INJECTION_IMPORT", SecuritySeverity.Critical, path,
                $"Importa a API de injeção {api}.", "Remova o plugin ou compile o jogo de uma fonte confiável.", true));
            return;
        }
    }

    private static void ScanScript(string path, SecurityScanOptions options, List<SecurityIssue> issues)
    {
        long size;
        try { size = new FileInfo(path).Length; }
        catch { return; }
        if (size > options.MaxSearchedFileBytes) return;

        string text;
        try { text = File.ReadAllText(path); }
        catch { return; }

        foreach ((Regex pattern, string code, string message, SecuritySeverity severity) in s_scriptPatterns)
        {
            if (!pattern.IsMatch(text)) continue;
            issues.Add(new SecurityIssue(code, severity, path, message,
                severity == SecuritySeverity.Critical
                    ? "Remova o comando ou marque este arquivo como confiável."
                    : "Revise o comando antes de executar.",
                severity == SecuritySeverity.Critical));
        }
    }

    private static void ScanRegistryStartup(List<SecurityIssue> issues, string gameDirectory)
    {
        foreach (RegistryStartupEntry entry in RegistryTools.GetStartupEntries())
        {
            string value = entry.Value;

            // A normal Windows has many startup entries that mention cmd.exe or PowerShell. Only an entry
            // that points at this game (or at a temporary/downloaded copy of it) is the game's problem.
            bool referencesGame = value.Contains(gameDirectory, StringComparison.OrdinalIgnoreCase)
                || value.Contains(Path.GetFileName(gameDirectory), StringComparison.OrdinalIgnoreCase);
            if (!referencesGame) continue;

            string? dangerous = s_dangerousStartupCommands.FirstOrDefault(command =>
                value.Contains(command, StringComparison.OrdinalIgnoreCase));
            if (dangerous == null) continue;

            issues.Add(new SecurityIssue("REGISTRY_STARTUP_SUSPICIOUS", SecuritySeverity.Critical,
                $"{entry.Hive}\\{entry.Key}\\{entry.Name}",
                $"Inicialização automática do jogo contém '{dangerous}'.", "Remova a entrada suspeita do registro.", true));
        }
    }

    private static void ScanLoadedModules(List<SecurityIssue> issues)
    {
        string temp = Path.GetTempPath();
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        foreach (DllInfo module in DllTools.GetLoadedModules())
        {
            if (module.IsMicrosoft) continue;
            bool fromTemp = module.Path.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            bool fromDownloads = module.Path.StartsWith(downloads, StringComparison.OrdinalIgnoreCase);
            if (!fromTemp && !fromDownloads) continue;

            issues.Add(new SecurityIssue("MODULE_FROM_TEMP", SecuritySeverity.Warning, module.Path,
                "Módulo carregado de uma pasta temporária ou de downloads.",
                "Confirme a origem do plugin.", false));
        }
    }

    private static bool IsTrustedAssemblyName(string name)
        => name.StartsWith("Prowl.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Infinity.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Silk.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Aperture", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Clay", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Echo", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Paper", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Quill", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Rosetta", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Scaffold", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Scribe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
            || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The player's startup gate. A blocking finding shows a warning with the file list and two choices:
/// run anyway (the affirmative button must be clicked more than once) or close the game.
/// </summary>
public static class GameSecurityGuard
{
    public static bool ConfirmLaunch(SecurityScanOptions? options = null, nint owner = 0, int requiredClicks = 2)
    {
        SecurityReport report = SecurityScanner.Scan(options);
        if (!report.HasBlockingIssues) return true;

        // Without a native dialog there is no safe way to ask. Log the findings and let the developer run
        // the game under a debugger rather than silently refusing to start on Linux or macOS.
        if (!DialogTools.IsSupported)
        {
            Console.Error.WriteLine($"[Security] {report.CriticalCount} problema(s) crítico(s):");
            foreach (SecurityIssue issue in report.Issues.Where(issue => issue.Severity == SecuritySeverity.Critical))
                Console.Error.WriteLine($"[Security] {issue.Path}: {issue.Message}");
            return true;
        }

        return DialogTools.ConfirmWarning(report.FormatForDialog(), "Infinity Engine - Alerta de Segurança", owner,
            Math.Max(1, requiredClicks));
    }
}
