// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;
using System.Threading;

using Infinity.DIK;
using Infinity.Runtime;
using Infinity.Editor.GUI.ApiDocs;
using Infinity.Services;

using Xunit;

namespace Infinity.Editor.Test;

public class InfinityLibrariesTests
{
    [Fact]
    public void ApiDocs_IncludesServicesAndDikLibraries()
    {
        var entries = ApiDocsCatalog.GetEntries(includeNonPublic: false);

        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Services.GameServer" && entry.IsType);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Services.SteamService" && entry.IsType);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Services.EpicGamesService" && entry.IsType);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.DIK.FileTools" && entry.IsType);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.DIK.WindowTools" && entry.IsType);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.DIK.RegistryTools" && entry.IsType);
    }

    [Fact]
    public void ApiDocs_ExamplesAreSpecificToTheMember()
    {
        var entries = ApiDocsCatalog.GetEntries(includeNonPublic: false);

        ApiDocEntry addComponent = entries.First(
            entry => entry.QualifiedName == "Infinity.Runtime.GameObject.AddComponent" && entry.Member is not null);
        ApiDocEntry log = entries.First(
            entry => entry.QualifiedName == "Infinity.Runtime.Debug.Log" && entry.Member is not null);

        Assert.Contains("AddComponent", addComponent.Example);
        Assert.Contains("Hello from Infinity Engine", log.Example);
        Assert.NotEqual(addComponent.Example, log.Example);
    }

    [Fact]
    public void FileTools_RoundTripsAndDeletes()
    {
        string folder = Path.Combine(Path.GetTempPath(), "infinity-dik-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(folder, "save.txt");
        try
        {
            Assert.True(FileTools.WriteText(file, "level=3"));
            Assert.Equal("level=3", FileTools.ReadText(file));
            Assert.True(FileTools.AppendText(file, "\ncoins=12"));
            Assert.Contains("coins=12", FileTools.ReadText(file));
            Assert.True(FileTools.GetInfo(file) is { IsDirectory: false, Size: > 0 });
            Assert.True(FileTools.DeleteFile(file));
            Assert.False(FileTools.Exists(file));
        }
        finally
        {
            FileTools.DeleteDirectory(folder);
        }
    }

    [Fact]
    public void ServiceManager_RegistersAndShutsDown()
    {
        using var manager = new ServiceManager();
        var server = manager.Register(new GameServer());
        var steam = manager.Register(new SteamService());

        Assert.Same(server, manager.Get<GameServer>());
        Assert.Same(steam, manager.Get<SteamService>());
        Assert.True(manager.TryGet<GameServer>(out GameServer? found));
        Assert.Same(server, found);
        Assert.Equal(2, manager.Services.Count);

        manager.Tick(0.016f);
        manager.ShutdownAll();
        Assert.Empty(manager.Services);
    }

    [Fact]
    public void DialogTools_ReportsTheCurrentPlatform()
    {
        Assert.Equal(OperatingSystem.IsWindows(), DialogTools.IsSupported);
    }

    [Fact]
    public void SteamService_TracksAchievementsAndStats()
    {
        var steam = new SteamService { Config = new SteamConfig { AppId = 480 } };

        Assert.True(steam.Initialize().Success);
        Assert.Equal(ServiceState.Ready, steam.State);
        Assert.True(steam.UnlockAchievement("ACH_TEST").Success);
        Assert.True(steam.HasAchievement("ACH_TEST"));
        Assert.True(steam.SetStat("kills", 7).Success);
        Assert.Equal(7, steam.GetStat("kills"));
        Assert.False(steam.UnlockAchievement("").Success);
    }

    [Fact]
    public void EpicGamesService_RequiresProductSettings()
    {
        var epic = new EpicGamesService();
        Assert.False(epic.Initialize().Success);
        Assert.Equal(ServiceState.NotConfigured, epic.State);

        epic.Config = new EpicGamesConfig
        {
            ProductId = "product",
            SandboxId = "sandbox",
            DeploymentId = "deployment",
        };

        Assert.True(epic.Initialize().Success);
        Assert.True(epic.UnlockAchievement("ACH_EPIC").Success);
        Assert.True(epic.HasAchievement("ACH_EPIC"));
        Assert.True(epic.SetPresence("Menu").Success);
    }

    [Fact]
    public void Leaderboard_KeepsBestScores()
    {
        var board = new Leaderboard("test", capacity: 2);
        board.Submit("a", "A", 10);
        board.Submit("b", "B", 30);
        board.Submit("c", "C", 20);

        var top = board.Top(2);
        Assert.Equal(2, top.Count);
        Assert.Equal("B", top[0].Name);
        Assert.Equal("C", top[1].Name);

        board.Submit("a", "A", 40);
        Assert.Equal("A", board.Top(1).Single().Name);
    }

    [Fact]
    public void Jobs_RunsIndexedAndItemWork()
    {
        int[] values = new int[1024];
        Jobs.For(0, values.Length, i => values[i] = i * 2);
        Assert.Equal(2046, values[^1]);
        Assert.Equal(0, values[0]);

        int sum = 0;
        Jobs.ForEach(values, value => Interlocked.Add(ref sum, value));
        Assert.Equal(1023 * 1024, sum);
    }

    [Fact]
    public void Dik_OsAndScreenApisAreSafeOnEveryPlatform()
    {
        OsInfo info = OsTools.GetInfo();
        Assert.True(info.ProcessId > 0);
        Assert.True(info.ProcessorCount > 0);
        Assert.True(info.PageSizeBytes > 0);
        Assert.Equal(OperatingSystem.IsWindows(), ScreenTools.IsSupported);
        Assert.Null(WindowTools.GetPosition(0));
        Assert.Null(WindowTools.Shake(0));
    }

    [Fact]
    public async Task Dik_BsodSimulatorRunsThresholdsAndCompletes()
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thresholds = new List<int>();
        using var bsod = new BsodSimulator(new BsodOptions
        {
            Duration = TimeSpan.FromMilliseconds(150),
            UseNativeWindow = false,
        });
        bsod.Thresholds = [50, 100];
        bsod.ThresholdReached += thresholds.Add;
        bsod.Completed += () => completed.TrySetResult(true);

        bsod.Start();
        Task finished = await Task.WhenAny(completed.Task, Task.Delay(3000));
        Assert.Same(completed.Task, finished);
        Assert.Equal([50, 100], thresholds);
        Assert.False(bsod.IsRunning);
        Assert.Equal(0f, bsod.Progress);
    }

    [Fact]
    public void Dik_CapabilitiesMatchPlatform()
    {
        Assert.True(Dik.Capabilities.FileSystem);
        Assert.True(Dik.Capabilities.Processes);
        Assert.Equal(OperatingSystem.IsWindows(), Dik.Capabilities.Registry);
        Assert.Equal(OperatingSystem.IsWindows(), Dik.Capabilities.Windows);
    }
}
