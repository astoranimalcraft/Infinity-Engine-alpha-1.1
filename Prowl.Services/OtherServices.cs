// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infinity.Services;

/// <summary>Discord webhook and bot settings.</summary>
public sealed class DiscordConfig
{
    /// <summary>Webhook URL used by <see cref="DiscordService.SendMessageAsync"/>.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Bot token for the REST API. Never ship a token in a client build.</summary>
    public string? BotToken { get; set; }

    /// <summary>Optional guild id for bot calls.</summary>
    public ulong GuildId { get; set; }
}

/// <summary>Sends messages to a Discord webhook and exposes a minimal REST surface for bot tooling.</summary>
public sealed class DiscordService : GameServiceBase
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Creates a Discord service.</summary>
    public DiscordService() : base("Discord", ServiceKind.Discord)
    {
    }

    /// <summary>Webhook and bot configuration.</summary>
    public DiscordConfig Config { get; set; } = new();

    /// <summary>True when a webhook or bot token is configured.</summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(Config.WebhookUrl) || !string.IsNullOrWhiteSpace(Config.BotToken);

    /// <inheritdoc />
    public override ServiceResult Initialize()
    {
        State = IsAvailable ? ServiceState.Ready : ServiceState.NotConfigured;
        return IsAvailable ? ServiceResult.Ok() : ServiceResult.Fail("Discord webhook or bot token is not configured.");
    }

    /// <summary>Posts a message through the configured webhook.</summary>
    public async Task<ServiceResult> SendMessageAsync(string content, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(Config.WebhookUrl))
            return ServiceResult.Fail("Discord WebhookUrl is not configured.");

        string json = JsonSerializer.Serialize(new { content, username });
        using var request = new HttpRequestMessage(HttpMethod.Post, Config.WebhookUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using HttpResponseMessage response = await s_http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ServiceResult.Ok(body) : ServiceResult.Fail(body, (int)response.StatusCode);
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }

    /// <summary>Sends a message to a channel with the bot token.</summary>
    public async Task<ServiceResult> SendChannelMessageAsync(ulong channelId, string content)
    {
        if (string.IsNullOrWhiteSpace(Config.BotToken))
            return ServiceResult.Fail("Discord BotToken is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v10/channels/{channelId}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", Config.BotToken);
        try
        {
            using HttpResponseMessage response = await s_http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ServiceResult.Ok(body) : ServiceResult.Fail(body, (int)response.StatusCode);
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }
}

/// <summary>PlayFab title settings.</summary>
public sealed class PlayFabConfig
{
    /// <summary>PlayFab title id.</summary>
    public string TitleId { get; set; } = string.Empty;

    /// <summary>Optional developer secret key. Server-only.</summary>
    public string? SecretKey { get; set; }
}

/// <summary>Minimal PlayFab login and profile API. Uses the public REST endpoints.</summary>
public sealed class PlayFabService : GameServiceBase
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Creates a PlayFab service.</summary>
    public PlayFabService() : base("PlayFab", ServiceKind.PlayFab)
    {
    }

    /// <summary>Title configuration.</summary>
    public PlayFabConfig Config { get; set; } = new();

    /// <inheritdoc />
    public override ServiceResult Initialize()
    {
        State = string.IsNullOrWhiteSpace(Config.TitleId) ? ServiceState.NotConfigured : ServiceState.Ready;
        return State == ServiceState.Ready ? ServiceResult.Ok() : ServiceResult.Fail("PlayFab TitleId is not configured.");
    }

    /// <summary>Logs in with a custom id. The response contains a session ticket for later calls.</summary>
    public async Task<ServiceResult> LoginWithCustomIdAsync(string customId, bool createAccount = true)
    {
        if (string.IsNullOrWhiteSpace(Config.TitleId))
            return ServiceResult.Fail("PlayFab TitleId is not configured.");

        string url = $"https://{Config.TitleId}.playfabapi.com/Client/LoginWithCustomID";
        string json = JsonSerializer.Serialize(new { TitleId = Config.TitleId, CustomId = customId, CreateAccount = createAccount });
        try
        {
            using var response = await s_http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ServiceResult.Ok(body) : ServiceResult.Fail(body, (int)response.StatusCode);
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }
}

/// <summary>Generic authenticated HTTP backend for a project's own server.</summary>
public sealed class WebServiceConfig
{
    /// <summary>Base URL, for example https://api.example.com/.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Optional bearer token.</summary>
    public string? BearerToken { get; set; }
}

/// <summary>A thin REST client for a custom backend. Use it when a project has its own account service.</summary>
public sealed class WebService : GameServiceBase
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Creates a custom web service.</summary>
    public WebService() : base("Web Service", ServiceKind.Custom)
    {
    }

    /// <summary>Base URL and token.</summary>
    public WebServiceConfig Config { get; set; } = new();

    /// <inheritdoc />
    public override ServiceResult Initialize()
    {
        State = string.IsNullOrWhiteSpace(Config.BaseUrl) ? ServiceState.NotConfigured : ServiceState.Ready;
        return State == ServiceState.Ready ? ServiceResult.Ok() : ServiceResult.Fail("WebService BaseUrl is not configured.");
    }

    /// <summary>Performs a GET and returns the response body.</summary>
    public Task<ServiceResult> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);

    /// <summary>Performs a POST with a JSON body and returns the response body.</summary>
    public Task<ServiceResult> PostJsonAsync(string path, string json) => SendAsync(HttpMethod.Post, path, json);

    /// <summary>Performs a DELETE and returns the response body.</summary>
    public Task<ServiceResult> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    private async Task<ServiceResult> SendAsync(HttpMethod method, string path, string? json)
    {
        if (string.IsNullOrWhiteSpace(Config.BaseUrl))
            return ServiceResult.Fail("WebService BaseUrl is not configured.");

        string url = Config.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(method, url);
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(Config.BearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.BearerToken);

        try
        {
            using HttpResponseMessage response = await s_http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ServiceResult.Ok(body) : ServiceResult.Fail(body, (int)response.StatusCode);
        }
        catch (Exception e) { return ServiceResult.Fail(e.Message); }
    }
}

/// <summary>One score on a <see cref="Leaderboard"/>.</summary>
public sealed class LeaderboardEntry
{
    /// <summary>Stable player id.</summary>
    public string PlayerId { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Score. Higher is better.</summary>
    public double Score { get; init; }

    /// <summary>When the score was submitted, in UTC.</summary>
    public DateTime SubmittedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>An in-memory leaderboard. Replace it with a server call when the project needs persistence.</summary>
public sealed class Leaderboard
{
    private readonly List<LeaderboardEntry> _entries = [];
    private readonly object _gate = new();

    /// <summary>Creates a leaderboard with a name and capacity.</summary>
    public Leaderboard(string name, int capacity = 100)
    {
        Name = name;
        Capacity = Math.Max(1, capacity);
    }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Maximum entries kept.</summary>
    public int Capacity { get; }

    /// <summary>Submits or improves a player's score.</summary>
    public LeaderboardEntry Submit(string playerId, string name, double score)
    {
        var entry = new LeaderboardEntry { PlayerId = playerId, Name = name, Score = score };
        lock (_gate)
        {
            int existing = _entries.FindIndex(e => e.PlayerId == playerId);
            if (existing >= 0)
            {
                if (_entries[existing].Score >= score) return _entries[existing];
                _entries[existing] = entry;
            }
            else _entries.Add(entry);

            _entries.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
        return entry;
    }

    /// <summary>Top scores, best first.</summary>
    public IReadOnlyList<LeaderboardEntry> Top(int count = 10)
    {
        lock (_gate) return _entries.Take(Math.Max(0, count)).ToArray();
    }
}

/// <summary>A simple in-memory matchmaking queue for prototypes and local testing.</summary>
public sealed class Matchmaker
{
    private readonly List<string> _queue = [];
    private readonly object _gate = new();

    /// <summary>Adds a player to the queue.</summary>
    public bool Join(string playerId)
    {
        lock (_gate)
        {
            if (_queue.Contains(playerId)) return false;
            _queue.Add(playerId);
            return true;
        }
    }

    /// <summary>Removes a player from the queue.</summary>
    public bool Leave(string playerId)
    {
        lock (_gate) return _queue.Remove(playerId);
    }

    /// <summary>Takes up to <paramref name="size"/> players when enough are waiting.</summary>
    public IReadOnlyList<string>? TryMatch(int size = 2)
    {
        lock (_gate)
        {
            if (_queue.Count < size) return null;
            string[] match = _queue.Take(size).ToArray();
            _queue.RemoveRange(0, size);
            return match;
        }
    }

    /// <summary>Number of waiting players.</summary>
    public int Count { get { lock (_gate) return _queue.Count; } }
}
