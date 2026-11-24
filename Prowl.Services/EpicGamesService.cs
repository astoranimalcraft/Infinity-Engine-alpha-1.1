// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Infinity.Services;

/// <summary>Epic Online Services (EOS) product settings.</summary>
public sealed class EpicGamesConfig
{
    /// <summary>EOS product id.</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>EOS sandbox id.</summary>
    public string SandboxId { get; set; } = string.Empty;

    /// <summary>EOS deployment id.</summary>
    public string DeploymentId { get; set; } = string.Empty;

    /// <summary>OAuth client id for server-side calls.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. Server-only.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Environment name, normally "Production" or "Dev".</summary>
    public string Environment { get; set; } = "Production";

    /// <summary>True when the three required ids are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProductId)
        && !string.IsNullOrWhiteSpace(SandboxId)
        && !string.IsNullOrWhiteSpace(DeploymentId);
}

/// <summary>
/// The native EOS SDK calls the engine cannot ship by default. Implement it over the Epic Online Services SDK
/// and assign it to <see cref="EpicGamesService.Backend"/>.
/// </summary>
public interface IEpicOnlineBackend
{
    /// <summary>Starts the EOS platform with the product settings.</summary>
    ServiceResult Initialize(EpicGamesConfig config);

    /// <summary>Shuts the EOS platform down.</summary>
    void Shutdown();

    /// <summary>True when a local player is signed in.</summary>
    bool IsLoggedIn { get; }

    /// <summary>The signed-in player display name, or null.</summary>
    string? DisplayName { get; }

    /// <summary>Sets the player's rich presence.</summary>
    void SetPresence(string presence);

    /// <summary>Unlocks an achievement.</summary>
    void UnlockAchievement(string id);

    /// <summary>Sets a stat.</summary>
    void SetStat(string name, int value);
}

/// <summary>An EOS backend that accepts calls but talks to nothing. Useful for editors, tests and offline builds.</summary>
public sealed class NullEpicOnlineBackend : IEpicOnlineBackend
{
    /// <inheritdoc />
    public ServiceResult Initialize(EpicGamesConfig config) => ServiceResult.Ok();

    /// <inheritdoc />
    public void Shutdown() { }

    /// <inheritdoc />
    public bool IsLoggedIn => false;

    /// <inheritdoc />
    public string? DisplayName => null;

    /// <inheritdoc />
    public void SetPresence(string presence) { }

    /// <inheritdoc />
    public void UnlockAchievement(string id) { }

    /// <inheritdoc />
    public void SetStat(string name, int value) { }
}

/// <summary>
/// Achievements, stats, presence and player identity for Epic Online Services games. The service validates the
/// product settings and delegates the native calls to <see cref="Backend"/>.
/// </summary>
public sealed class EpicGamesService : GameServiceBase
{
    private readonly HashSet<string> _achievements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _stats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an Epic Games service.</summary>
    public EpicGamesService() : base("Epic Games", ServiceKind.EpicGames)
    {
    }

    /// <summary>Product, sandbox and deployment settings.</summary>
    public EpicGamesConfig Config { get; set; } = new();

    /// <summary>Native EOS binding. Defaults to a no-op backend.</summary>
    public IEpicOnlineBackend Backend { get; set; } = new NullEpicOnlineBackend();

    /// <summary>True when the product settings are complete.</summary>
    public bool IsAvailable => Config.IsConfigured;

    /// <summary>True when a player is signed in.</summary>
    public bool IsLoggedIn => Backend.IsLoggedIn;

    /// <summary>The signed-in player name, or null.</summary>
    public string? DisplayName => Backend.DisplayName;

    /// <summary>Achievement ids unlocked in this session.</summary>
    public IReadOnlyCollection<string> UnlockedAchievements => _achievements;

    /// <inheritdoc />
    public override ServiceResult Initialize()
    {
        if (!Config.IsConfigured)
        {
            State = ServiceState.NotConfigured;
            return ServiceResult.Fail("Epic ProductId, SandboxId and DeploymentId are required.");
        }

        try
        {
            ServiceResult result = Backend.Initialize(Config);
            State = result.Success ? ServiceState.Ready : ServiceState.Failed;
            return result;
        }
        catch (Exception e)
        {
            State = ServiceState.Failed;
            return ServiceResult.Fail(e.Message);
        }
    }

    /// <summary>Sets the player's rich presence.</summary>
    public ServiceResult SetPresence(string presence)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Epic Games is not initialized.");
        try { Backend.SetPresence(presence); return ServiceResult.Ok(); }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Unlocks an achievement.</summary>
    public ServiceResult UnlockAchievement(string id)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Epic Games is not initialized.");
        if (string.IsNullOrWhiteSpace(id)) return ServiceResult.Fail("Achievement id is empty.");
        if (!_achievements.Add(id)) return ServiceResult.Ok("already unlocked");
        try { Backend.UnlockAchievement(id); return ServiceResult.Ok(id); }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Sets an integer stat.</summary>
    public ServiceResult SetStat(string name, int value)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Epic Games is not initialized.");
        try
        {
            Backend.SetStat(name, value);
            _stats[name] = value;
            return ServiceResult.Ok();
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Reads a stat set in this session, or null.</summary>
    public int? GetStat(string name) => _stats.TryGetValue(name, out int value) ? value : null;

    /// <summary>True when the achievement was unlocked in this session.</summary>
    public bool HasAchievement(string id) => _achievements.Contains(id);

    /// <inheritdoc />
    public override void Shutdown()
    {
        try { Backend.Shutdown(); } catch { }
        base.Shutdown();
    }
}
