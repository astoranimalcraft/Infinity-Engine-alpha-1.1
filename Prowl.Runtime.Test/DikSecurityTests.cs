// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;

using Infinity.DIK;

using Xunit;

namespace Infinity.Runtime.Test;

public class DikSecurityTests
{
    [Fact]
    public void ACorruptedDll_BlocksLaunch()
    {
        string folder = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "broken.dll"), "this is not a PE file");
            SecurityReport report = SecurityScanner.Scan(new SecurityScanOptions
            {
                GameDirectory = folder,
                ScanGameManifest = false,
                ScanRegistryStartup = false,
                ScanLoadedModules = false,
            });

            Assert.True(report.HasBlockingIssues);
            Assert.Contains(report.Issues, issue => issue.Code == "PE_INVALID");
            Assert.Contains("broken.dll", report.FormatForDialog());
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ASafeScript_DoesNotBlock()
    {
        string folder = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "Player.cs"),
                "public class Player : Component { public override void Update() { } }");
            SecurityReport report = SecurityScanner.Scan(new SecurityScanOptions
            {
                GameDirectory = folder,
                ScanGameManifest = false,
                ScanRegistryStartup = false,
                ScanLoadedModules = false,
            });

            Assert.False(report.HasBlockingIssues);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ADangerousScript_BlocksLaunch()
    {
        string folder = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "Cheat.ps1"), "bcdedit /set {default} safeboot minimal");
            SecurityReport report = SecurityScanner.Scan(new SecurityScanOptions
            {
                GameDirectory = folder,
                ScanGameManifest = false,
                ScanRegistryStartup = false,
                ScanLoadedModules = false,
            });

            Assert.True(report.HasBlockingIssues);
            Assert.Contains(report.Issues, issue => issue.Code == "SCRIPT_BOOT_EDIT");
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void TheEngineAssembly_HasAValidPeHeader()
    {
        Assert.True(DllTools.HasValidPeHeader(typeof(Dik).Assembly.Location));
    }

    [Fact]
    public void TheHackSimulation_IsOnlyTextAndDoesNotTouchTheSystem()
    {
        using var hack = new HackSimulation(new HackSimulationOptions
        {
            TargetName = "TEST-PC",
            ShowDlls = false,
            ShowRegistry = false,
            ShowFiles = false,
        });

        Assert.Equal(7, hack.Stages.Count);
        Assert.NotEmpty(hack.TerminalLines);
        Assert.Empty(hack.Dlls);
        Assert.Empty(hack.RegistryKeys);
        Assert.Empty(hack.Files);
        Assert.Equal(100f, hack.Stages[^1].Progress);
    }

    [Fact]
    public void ReadingStartupEntries_DoesNotThrowOnAnyPlatform()
    {
        _ = RegistryTools.GetStartupEntries();
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "infinity-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
