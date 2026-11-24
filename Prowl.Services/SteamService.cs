// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infinity.Services;

/// <summary>Steamworks application settings.</summary>
public sealed class SteamConfig
{
    /// <summary>Steam application id from Steamworks.</summary>
    public uint AppId { get; set; }

    /// <summary>Publisher Web API key. Keep it on the server, never in a client build.</summary>
    public string? WebApiKey { get; set; }

    /// <summary>Language used by the Steam API, for example "english" or "brazilian".</summary>
    public string Language { get; set; } = "english";

    /// <summary>Stores stats after every achievement or stat change.</summary>
    public bool AutoStoreStats { get; set; } = true;
}

/// <summary>
/// The native Steamworks calls the engine cannot ship by default. Implement it over Steamworks.NET, Facepunch,
/// or your own binding and assign it to <see cref="SteamService.Backend"/>.
/// </summary>
public interface ISteamBackend
{
    /// <summary>Starts Steamworks with the app id.</summary>
    ServiceResult Initialize(SteamConfig config);

    /// <summary>Shuts Steamworks down.</summary>
    void Shutdown();

    /// <summary>Unlocks an achievement id.</summary>
    void UnlockAchievement(string id);

    /// <summary>Marks an achievement as unlocked without showing the popup.</summary>
    void SetAchievement(string id);

    /// <summary>Sets an integer stat.</summary>
    void SetStat(string name, int value);

    /// <summary>Sets a float stat.</summary>
    void SetStat(string name, float value);

    /// <summary>Uploads changed stats and achievements.</summary>
    void StoreStats();

    /// <summary>Sets a rich presence key/value pair.</summary>
    void SetRichPresence(string key, string value);

    /// <summary>The signed-in player name, or null.</summary>
    string? GetPersonaName();
}

/// <summary>A backend that accepts calls but talks to nothing. Useful for editors, tests and offline builds.</summary>
public sealed class NullSteamBackend : ISteamBackend
{
    /// <inheritdoc />
    public ServiceResult Initialize(SteamConfig config) => ServiceResult.Ok();

    /// <inheritdoc />
    public void Shutdown() { }

    /// <inheritdoc />
    public void UnlockAchievement(string id) { }

    /// <inheritdoc />
    public void SetAchievement(string id) { }

    /// <inheritdoc />
    public void SetStat(string name, int value) { }

    /// <inheritdoc />
    public void SetStat(string name, float value) { }

    /// <inheritdoc />
    public void StoreStats() { }

    /// <inheritdoc />
    public void SetRichPresence(string key, string value) { }

    /// <inheritdoc />
    public string? GetPersonaName() => null;
}

/// <summary>
/// Achievements, stats, rich presence and the public Web API for Steam games. The engine owns the state and
/// delegates the native calls to <see cref="Backend"/>, so projects can swap Steamworks bindings without changing
/// gameplay code.
/// </summary>
public sealed class SteamService : GameServiceBase
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HashSet<string> _unlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _stats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a Steam service.</summary>
    public SteamService() : base("Steam", ServiceKind.Steam)
    {
    }

    /// <summary>Application settings.</summary>
    public SteamConfig Config { get; set; } = new();

    /// <summary>Native Steamworks binding. Defaults to a no-op backend.</summary>
    public ISteamBackend Backend { get; set; } = new NullSteamBackend();

    /// <summary>True when an app id is configured.</summary>
    public bool IsAvailable => Config.AppId != 0;

    /// <summary>Achievement ids that were unlocked in this session.</summary>
    public IReadOnlyCollection<string> UnlockedAchievements => _unlocked;

    /// <inheritdoc />
    public override ServiceResult Initialize()
    {
        if (Config.AppId == 0)
        {
            State = ServiceState.NotConfigured;
            return ServiceResult.Fail("Steam AppId is not configured.");
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

    /// <summary>Unlocks an achievement and stores stats when <see cref="SteamConfig.AutoStoreStats"/> is on.</summary>
    public ServiceResult UnlockAchievement(string id)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Steam is not initialized.");
        if (string.IsNullOrWhiteSpace(id)) return ServiceResult.Fail("Achievement id is empty.");
        if (!_unlocked.Add(id)) return ServiceResult.Ok("already unlocked");

        try
        {
            Backend.UnlockAchievement(id);
            if (Config.AutoStoreStats) Backend.StoreStats();
            return ServiceResult.Ok(id);
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Marks an achievement as unlocked without the popup.</summary>
    public ServiceResult SetAchievement(string id)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Steam is not initialized.");
        try
        {
            Backend.SetAchievement(id);
            _unlocked.Add(id);
            return ServiceResult.Ok(id);
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Sets an integer stat.</summary>
    public ServiceResult SetStat(string name, int value)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Steam is not initialized.");
        try
        {
            Backend.SetStat(name, value);
            _stats[name] = value;
            if (Config.AutoStoreStats) Backend.StoreStats();
            return ServiceResult.Ok();
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Sets a float stat.</summary>
    public ServiceResult SetStat(string name, float value)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Steam is not initialized.");
        try
        {
            Backend.SetStat(name, value);
            _stats[name] = value;
            if (Config.AutoStoreStats) Backend.StoreStats();
            return ServiceResult.Ok();
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Reads a stat set in this session, or null.</summary>
    public double? GetStat(string name) => _stats.TryGetValue(name, out double value) ? value : null;

    /// <summary>True when the achievement was unlocked in this session.</summary>
    public bool HasAchievement(string id) => _unlocked.Contains(id);

    /// <summary>Uploads changed stats and achievements.</summary>
    public ServiceResult StoreStats()
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Steam is not initialized.");
        try { Backend.StoreStats(); return ServiceResult.Ok(); }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Sets a rich presence entry shown on the friends list.</summary>
    public ServiceResult SetRichPresence(string key, string value)
    {
        if (State != ServiceState.Ready) return ServiceResult.Fail("Steam is not initialized.");
        try { Backend.SetRichPresence(key, value); return ServiceResult.Ok(); }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>The signed-in player name, or null.</summary>
    public string? GetPersonaName()
    {
        try { return Backend.GetPersonaName(); }
        catch { return null; }
    }

    /// <summary>
    /// Calls a Steam Web API endpoint such as <c>ISteamUserStats/GetPlayerAchievements/v1</c>. Requires
    /// <see cref="SteamConfig.WebApiKey"/> and a Steam id. Never call this with a key shipped to players.
    /// </summary>
    public async Task<ServiceResult> WebApiGetAsync(string interfaceMethodVersion, ulong steamId)
    {
        if (string.IsNullOrWhiteSpace(Config.WebApiKey))
            return ServiceResult.Fail("Steam WebApiKey is not configured.");

        string url = $"https://api.steampowered.com/{interfaceMethodVersion.Trim('/')}?key={Uri.EscapeDataString(Config.WebApiKey)}&steamid={steamId}&appid={Config.AppId}&l={Uri.EscapeDataString(Config.Language)}";
        try
        {
            using HttpResponseMessage response = await s_http.GetAsync(url).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ServiceResult.Ok(body) : ServiceResult.Fail(body, (int)response.StatusCode);
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Parses a Web API body into a JSON document, or null when the body is not JSON.</summary>
    public static JsonDocument? ParseWebApi(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { return null; }
    }

    /// <inheritdoc />
    public override void Shutdown()
    {
        try { Backend.Shutdown(); } catch { }
        base.Shutdown();
    }
}
