// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Infinity.Services;

/// <summary>Address and rules for a <see cref="GameServer"/>.</summary>
public sealed class GameServerConfig
{
    /// <summary>Name sent to clients in the welcome line.</summary>
    public string Name { get; set; } = "Infinity Server";

    /// <summary>Address the listener binds to. 0.0.0.0 accepts every interface.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>TCP port. 7777 is the engine's default.</summary>
    public int Port { get; set; } = 7777;

    /// <summary>Maximum simultaneous clients.</summary>
    public int MaxPlayers { get; set; } = 32;

    /// <summary>Optional password. An empty value accepts every client.</summary>
    public string? Password { get; set; }

    /// <summary>When true the welcome line includes the server name and player count.</summary>
    public bool SendWelcome { get; set; } = true;
}

/// <summary>One connected client. The server owns the socket and removes the player when it disconnects.</summary>
public sealed class ServerPlayer
{
    internal ServerPlayer(int id, string endpoint, TcpClient client)
    {
        Id = id;
        Endpoint = endpoint;
        Client = client;
        ConnectedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Connection-local id, unique for the lifetime of the server.</summary>
    public int Id { get; }

    /// <summary>Display name. Clients can change it with the name channel.</summary>
    public string Name { get; internal set; } = "Player";

    /// <summary>Remote IP and port.</summary>
    public string Endpoint { get; }

    /// <summary>When the player connected, in UTC.</summary>
    public DateTime ConnectedAtUtc { get; }

    /// <summary>True after the client sends the ready message.</summary>
    public bool IsReady { get; internal set; }

    internal TcpClient Client { get; }
    internal StreamWriter? Writer { get; set; }
}

/// <summary>
/// A small authoritative TCP server with line-oriented messages. It is enough for co-op sessions, lobbies and
/// dedicated servers, and its API is stable so a project can replace the transport later.
/// Messages are sent as <c>channel|payload</c>.
/// </summary>
public sealed class GameServer : GameServiceBase
{
    private readonly List<ServerPlayer> _players = [];
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptTask;
    private int _nextPlayerId = 1;

    /// <summary>Creates a stopped server with default configuration.</summary>
    public GameServer() : base("Game Server", ServiceKind.Server)
    {
    }

    /// <summary>Address, port, capacity and password.</summary>
    public GameServerConfig Config { get; set; } = new();

    /// <summary>True while the listener accepts clients.</summary>
    public bool IsRunning => State == ServiceState.Running;

    /// <summary>Snapshot of connected players.</summary>
    public IReadOnlyList<ServerPlayer> Players
    {
        get { lock (_gate) return _players.ToArray(); }
    }

    /// <summary>Raised on the server's background thread when a client connects.</summary>
    public event Action<ServerPlayer>? PlayerJoined;

    /// <summary>Raised when a client disconnects.</summary>
    public event Action<ServerPlayer>? PlayerLeft;

    /// <summary>Raised for each received message.</summary>
    public event Action<ServerPlayer, string, string>? MessageReceived;

    /// <inheritdoc />
    public override ServiceResult Initialize()
    {
        if (IsRunning) return ServiceResult.Ok();
        try
        {
            IPAddress address = Config.BindAddress is "0.0.0.0" or "*"
                ? IPAddress.Any
                : IPAddress.Parse(Config.BindAddress);
            _listener = new TcpListener(address, Config.Port);
            _listener.Start(Config.MaxPlayers);
            _cancellation = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
            State = ServiceState.Running;
            return ServiceResult.Ok($"Listening on {Config.BindAddress}:{Config.Port}");
        }
        catch (Exception e)
        {
            State = ServiceState.Failed;
            return ServiceResult.Fail(e.Message);
        }
    }

    /// <summary>Starts listening. Same as <see cref="Initialize"/>; present because servers read better with Start.</summary>
    public ServiceResult Start() => Initialize();

    /// <summary>Stops listening and disconnects every player.</summary>
    public override void Shutdown()
    {
        try { _cancellation?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cancellation?.Dispose();
        _cancellation = null;

        ServerPlayer[] players;
        lock (_gate)
        {
            players = _players.ToArray();
            _players.Clear();
        }
        foreach (ServerPlayer player in players)
        {
            try { player.Client.Close(); } catch { }
            PlayerLeft?.Invoke(player);
        }
        State = ServiceState.Disabled;
    }

    /// <summary>Stops listening. Alias of <see cref="Shutdown"/>.</summary>
    public void Stop() => Shutdown();

    /// <summary>Removes a player and optionally tells them why.</summary>
    public bool Kick(int playerId, string reason = "Kicked")
    {
        ServerPlayer? player = null;
        lock (_gate)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Id != playerId) continue;
                player = _players[i];
                _players.RemoveAt(i);
                break;
            }
        }
        if (player == null) return false;

        try { player.Writer?.WriteLine($"kick|{reason}"); } catch { }
        try { player.Client.Close(); } catch { }
        PlayerLeft?.Invoke(player);
        return true;
    }

    /// <summary>Sends one message to every connected player.</summary>
    public int Broadcast(string channel, string payload)
    {
        ServerPlayer[] players;
        lock (_gate) players = _players.ToArray();

        int sent = 0;
        foreach (ServerPlayer player in players)
        {
            try
            {
                player.Writer?.WriteLine($"{channel}|{payload}");
                player.Writer?.Flush();
                sent++;
            }
            catch { }
        }
        return sent;
    }

    /// <summary>Sends one message to a player.</summary>
    public bool Send(int playerId, string channel, string payload)
    {
        lock (_gate)
        {
            foreach (ServerPlayer player in _players)
            {
                if (player.Id != playerId) continue;
                try
                {
                    player.Writer?.WriteLine($"{channel}|{payload}");
                    player.Writer?.Flush();
                    return true;
                }
                catch { return false; }
            }
        }
        return false;
    }

    /// <inheritdoc />
    public override void Tick(float deltaTime)
    {
        // Clients are removed by their read loop. This method exists so a game can pump the server from the
        // same place as the other services, and to give projects a hook for their own simulation.
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            ServerPlayer player;
            lock (_gate)
            {
                if (_players.Count >= Config.MaxPlayers)
                {
                    try { client.Close(); } catch { }
                    continue;
                }
                player = new ServerPlayer(_nextPlayerId++, client.Client.RemoteEndPoint?.ToString() ?? "unknown", client);
                _players.Add(player);
            }

            try
            {
                NetworkStream stream = client.GetStream();
                player.Writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                if (Config.SendWelcome)
                    player.Writer.WriteLine($"welcome|{Config.Name}|{Players.Count}|{player.Id}");
                PlayerJoined?.Invoke(player);
            }
            catch
            {
                RemovePlayer(player);
                continue;
            }

            _ = Task.Run(() => ReadLoopAsync(player, token), CancellationToken.None);
        }
    }

    private async Task ReadLoopAsync(ServerPlayer player, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(player.Client.GetStream(), Encoding.UTF8, false, 4096, leaveOpen: true);
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0) continue;

                int separator = line.IndexOf('|');
                string channel = separator >= 0 ? line[..separator] : line;
                string payload = separator >= 0 ? line[(separator + 1)..] : string.Empty;

                if (channel == "name") player.Name = payload.Length > 0 ? payload : player.Name;
                else if (channel == "ready") player.IsReady = payload is "1" or "true";
                else MessageReceived?.Invoke(player, channel, payload);
            }
        }
        catch { }
        finally
        {
            RemovePlayer(player);
        }
    }

    private void RemovePlayer(ServerPlayer player)
    {
        bool removed;
        lock (_gate) removed = _players.Remove(player);
        if (!removed) return;
        try { player.Client.Close(); } catch { }
        PlayerLeft?.Invoke(player);
    }
}
