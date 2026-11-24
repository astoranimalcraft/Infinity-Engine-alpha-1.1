// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Infinity.Editor.Core;
using Infinity.Editor.GUI.Panels;
using Infinity.Editor.GUI.SceneView;
using Infinity.Editor.Projects;
using Infinity.Editor.Theming;
using Infinity.Runtime;
using Infinity.Vector;

using Color = System.Drawing.Color;

namespace Infinity.Editor.MultiProgram;

/// <summary>
/// A small LAN/Radmin collaboration session. The host owns the user list, roles, bans and history;
/// clients keep the same project open and exchange presence, the scene/file they edit, and their
/// selection color over a newline-delimited JSON socket.
/// </summary>
public static class MultiProgramSession
{
    public const int DefaultPort = 45832;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, MultiProgramUser> s_users = new(StringComparer.Ordinal);
    private static readonly List<MultiProgramHistoryEntry> s_history = [];
    private static readonly List<MultiProgramBan> s_bans = [];
    private static readonly Dictionary<string, ServerPeer> s_peers = new(StringComparer.Ordinal);

    private static TcpListener? s_listener;
    private static CancellationTokenSource? s_cancel;
    private static TcpClient? s_client;
    private static StreamWriter? s_clientWriter;
    private static long s_lastPresence;
    private static string s_editingFile = string.Empty;
    private static string? s_lastHistoryKey;

    public static bool IsHosting { get; private set; }
    public static bool IsConnected { get; private set; }
    public static bool IsActive => IsHosting || IsConnected;
    public static string LocalId { get; private set; } = string.Empty;
    public static string HostName { get; private set; } = string.Empty;
    public static string StatusMessage { get; private set; } = "Desconectado.";
    public static MultiProgramRole LocalRole { get; private set; } = MultiProgramRole.Viewer;
    public static string ProjectName { get; private set; } = string.Empty;

    public static string LocalName
    {
        get => EditorSettings.Instance.MultiProgramName;
        set
        {
            EditorSettings.Instance.MultiProgramName = string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();
            EditorSettings.Instance.Save();
            TouchLocal();
        }
    }

    public static string LocalColor
    {
        get => NormalizeColor(EditorSettings.Instance.MultiProgramColor);
        set
        {
            EditorSettings.Instance.MultiProgramColor = NormalizeColor(value);
            EditorSettings.Instance.Save();
            TouchLocal();
        }
    }

    public static int LastPort
    {
        get => EditorSettings.Instance.MultiProgramPort <= 0 ? DefaultPort : EditorSettings.Instance.MultiProgramPort;
        set
        {
            EditorSettings.Instance.MultiProgramPort = Math.Clamp(value, 1024, 65535);
            EditorSettings.Instance.Save();
        }
    }

    public static string LastHost
    {
        get => EditorSettings.Instance.MultiProgramHost;
        set
        {
            EditorSettings.Instance.MultiProgramHost = value.Trim();
            EditorSettings.Instance.Save();
        }
    }

    /// <summary>The relative path of the file this developer currently has open, shown to the team.</summary>
    public static string EditingFile
    {
        get => s_editingFile;
        set => s_editingFile = value ?? string.Empty;
    }

    public static IReadOnlyList<MultiProgramUser> Users
    {
        get
        {
            lock (Sync)
                return s_users.Values.OrderByDescending(u => u.Role == MultiProgramRole.Owner)
                    .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase).Select(Copy).ToArray();
        }
    }

    public static IReadOnlyList<MultiProgramHistoryEntry> History
    {
        get { lock (Sync) return s_history.OrderByDescending(h => h.AtUtc).Select(Copy).ToArray(); }
    }

    public static IReadOnlyList<MultiProgramBan> Bans
    {
        get { lock (Sync) return s_bans.OrderByDescending(b => b.AtUtc).Select(Copy).ToArray(); }
    }

    public static MultiProgramUser? LocalUser
    {
        get { lock (Sync) return s_users.TryGetValue(LocalId, out MultiProgramUser? u) ? Copy(u) : null; }
    }

    public static string LocalAddress
    {
        get
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString()
                    ?? "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }
    }

    /// <summary>Starts listening on every interface, so a Radmin VPN or LAN address works without extra setup.</summary>
    public static bool Host(int port)
    {
        Disconnect();
        try
        {
            LastPort = port;
            EnsureLocalUser(MultiProgramRole.Owner);
            LoadBans();
            s_cancel = new CancellationTokenSource();
            s_listener = new TcpListener(IPAddress.Any, port);
            s_listener.Start();
            IsHosting = true;
            IsConnected = true;
            HostName = LocalName;
            ProjectName = Project.Current?.Name ?? "Infinity Engine Project";
            StatusMessage = $"Servidor ativo em {LocalAddress}:{port}";
            AddHistory("Sessão", "Servidor iniciado", $"{LocalAddress}:{port}");
            _ = AcceptLoop(s_cancel.Token);
            return true;
        }
        catch (Exception e)
        {
            StatusMessage = $"Não foi possível hospedar: {e.Message}";
            Disconnect();
            return false;
        }
    }

    public static bool Join(string host, int port)
    {
        Disconnect();
        try
        {
            LastHost = host;
            LastPort = port;
            EnsureLocalUser(MultiProgramRole.Editor);
            var client = new TcpClient();
            Task connect = client.ConnectAsync(host.Trim(), port);
            if (!connect.Wait(5000))
            {
                client.Dispose();
                StatusMessage = $"Tempo esgotado ao conectar em {host}:{port}.";
                return false;
            }

            s_client = client;
            NetworkStream stream = client.GetStream();
            s_clientWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            IsHosting = false;
            IsConnected = true;
            HostName = host.Trim();
            ProjectName = Project.Current?.Name ?? "Infinity Engine Project";
            StatusMessage = $"Conectando em {host}:{port}...";

            SendToServer(new MultiProgramMessage
            {
                Type = "hello",
                Id = LocalId,
                Name = LocalName,
                Pc = Environment.MachineName,
                Color = LocalColor,
                Scene = CurrentSceneName(),
                File = EditingFile,
                Object = CurrentObjectName(),
            });
            _ = ClientReadLoop(client);
            return true;
        }
        catch (Exception e)
        {
            StatusMessage = $"Não foi possível conectar: {e.Message}";
            Disconnect();
            return false;
        }
    }

    public static void Disconnect()
    {
        try { SendToServer(new MultiProgramMessage { Type = "leave", Id = LocalId }); } catch { }
        try { s_cancel?.Cancel(); } catch { }
        try { s_listener?.Stop(); } catch { }
        try { s_client?.Close(); } catch { }
        lock (Sync)
        {
            foreach (ServerPeer peer in s_peers.Values) peer.Close();
            s_peers.Clear();
            s_users.Clear();
            s_history.Clear();
            s_bans.Clear();
        }
        s_listener = null;
        s_cancel?.Dispose();
        s_cancel = null;
        s_client = null;
        s_clientWriter = null;
        IsHosting = false;
        IsConnected = false;
        LocalRole = MultiProgramRole.Viewer;
        LocalId = string.Empty;
        HostName = string.Empty;
        ProjectName = string.Empty;
        StatusMessage = "Desconectado.";
        s_lastHistoryKey = null;
    }

    /// <summary>Called once per editor frame. Sends a heartbeat and refreshes what this developer is editing.</summary>
    public static void Tick()
    {
        if (!IsActive) return;
        long now = Environment.TickCount64;
        if (now - s_lastPresence < 1500) return;
        s_lastPresence = now;

        MultiProgramUser? local;
        lock (Sync) local = s_users.TryGetValue(LocalId, out MultiProgramUser? u) ? u : null;
        if (local == null) return;
        local.Name = LocalName;
        local.PcName = Environment.MachineName;
        local.Color = LocalColor;
        local.Scene = CurrentSceneName();
        local.File = EditingFile;
        local.ObjectName = CurrentObjectName();
        local.LastSeenUtc = DateTime.UtcNow;

        if (IsHosting)
        {
            BroadcastUsers();
            AddHistoryIfChanged("Presença", local.ObjectName.Length > 0 ? local.ObjectName : local.File, local.Scene);
        }
        else
        {
            SendToServer(new MultiProgramMessage
            {
                Type = "presence",
                Id = LocalId,
                Name = local.Name,
                Pc = local.PcName,
                Color = local.Color,
                Scene = local.Scene,
                File = local.File,
                Object = local.ObjectName,
            });
        }
    }

    public static void SetRole(string userId, MultiProgramRole role)
    {
        if (!IsHosting || role == MultiProgramRole.Owner) return;
        lock (Sync)
        {
            if (!s_users.TryGetValue(userId, out MultiProgramUser? user)) return;
            user.Role = role;
            if (s_peers.TryGetValue(userId, out ServerPeer? peer))
                _ = SendAsync(peer, new MultiProgramMessage { Type = "role", Id = userId, Role = role });
        }
        AddHistory("Cargo", userId, role.ToString());
        BroadcastUsers();
    }

    public static void Kick(string userId, bool ban, string reason = "")
    {
        if (!IsHosting) return;
        string name = string.Empty;
        lock (Sync)
        {
            if (!s_users.TryGetValue(userId, out MultiProgramUser? user) || user.Role == MultiProgramRole.Owner) return;
            name = user.Name;
            if (ban)
            {
                s_bans.Add(new MultiProgramBan { Name = name, PcName = user.PcName, Reason = reason });
                SaveBans();
            }
            if (s_peers.TryGetValue(userId, out ServerPeer? peer))
            {
                _ = SendAsync(peer, new MultiProgramMessage
                {
                    Type = "kicked",
                    Message = ban ? "Você foi banido pelo dono da sessão." : "Você foi removido pelo dono da sessão.",
                });
                peer.Close();
                s_peers.Remove(userId);
            }
            s_users.Remove(userId);
        }
        AddHistory(ban ? "Banimento" : "Expulsão", name, reason);
        BroadcastUsers();
    }

    public static void Unban(string banId)
    {
        if (!IsHosting) return;
        lock (Sync) s_bans.RemoveAll(b => b.Id == banId);
        SaveBans();
    }
    public static void ClearHistory()
    {
        if (!IsHosting) return;
        lock (Sync) s_history.Clear();
    }

    public static void AddHistory(string action, string target, string detail)
    {
        var entry = new MultiProgramHistoryEntry
        {
            User = LocalName,
            Action = action,
            Target = target,
            Detail = detail,
            AtUtc = DateTime.UtcNow,
        };
        lock (Sync) s_history.Insert(0, entry);
        if (!IsHosting && IsConnected)
            SendToServer(new MultiProgramMessage { Type = "history", Action = action, Detail = detail, TargetId = target });
    }

    private static void AddHistoryIfChanged(string action, string target, string detail)
    {
        string key = action + "|" + target + "|" + detail;
        if (key == s_lastHistoryKey) return;
        s_lastHistoryKey = key;
        AddHistory(action, target, detail);
    }

    /// <summary>Returns the color of the first developer editing a project-relative file, or null.</summary>
    public static Color? ColorForFile(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        lock (Sync)
        {
            foreach (MultiProgramUser user in s_users.Values)
            {
                if (user.Id == LocalId) continue;
                if (string.Equals(NormalizePath(user.File), NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase))
                    return ParseColor(user.Color);
            }
        }
        return null;
    }

    /// <summary>Returns the color of the first developer selecting a GameObject with this name, or null.</summary>
    public static Color? ColorForObject(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return null;
        lock (Sync)
        {
            foreach (MultiProgramUser user in s_users.Values)
            {
                if (user.Id == LocalId) continue;
                if (string.Equals(user.ObjectName, objectName, StringComparison.OrdinalIgnoreCase))
                    return ParseColor(user.Color);
            }
        }
        return null;
    }

    public static string? NameForFile(string relativePath)
    {
        lock (Sync)
        {
            foreach (MultiProgramUser user in s_users.Values)
                if (user.Id != LocalId && string.Equals(NormalizePath(user.File), NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase))
                    return user.Name;
        }
        return null;
    }

    public static string? NameForObject(string objectName)
    {
        lock (Sync)
        {
            foreach (MultiProgramUser user in s_users.Values)
                if (user.Id != LocalId && string.Equals(user.ObjectName, objectName, StringComparison.OrdinalIgnoreCase))
                    return user.Name;
        }
        return null;
    }

    public static Color ParseColor(string? hex)
    {
        hex = NormalizeColor(hex).TrimStart('#');
        try
        {
            if (hex.Length == 6)
                return Color.FromArgb(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
        }
        catch { }
        return Color.FromArgb(124, 92, 255);
    }

    public static string NormalizeColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "#7C5CFF";
        hex = hex.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        return hex.Length == 7 ? hex.ToUpperInvariant() : "#7C5CFF";
    }

    /// <summary>Points the scene view at the main game camera, matching the game's view.</summary>
    public static void FocusGameCamera()
    {
        Camera? camera = Camera.Main;
        if (camera.IsNotValid()) return;
        Float3 euler = camera!.Transform.LocalEulerAngles;
        SceneViewPanel.ActiveCamera?.SetPose(camera.Transform.Position, euler.Y, euler.X);
    }

    private sealed class ServerPeer
    {
        public required TcpClient Tcp;
        public required StreamReader Reader;
        public required StreamWriter Writer;
        public readonly SemaphoreSlim WriteLock = new(1, 1);
        public MultiProgramUser? User;

        public void Close()
        {
            try { Tcp.Close(); } catch { }
        }
    }

    private static string CurrentSceneName()
    {
        try { return EditorSceneManager.ActiveScene?.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string CurrentObjectName()
        => Selection.ActiveObject is GameObject go && go.IsValid() ? go.Name : string.Empty;

    private static string NormalizePath(string path)
        => string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimStart('/');

    private static void EnsureLocalUser(MultiProgramRole role)
    {
        LocalId = "local-" + Guid.NewGuid().ToString("N")[..8];
        LocalRole = role;
        s_users[LocalId] = new MultiProgramUser
        {
            Id = LocalId,
            Name = LocalName,
            PcName = Environment.MachineName,
            Color = LocalColor,
            Role = role,
            IsHost = role == MultiProgramRole.Owner,
            Scene = CurrentSceneName(),
            File = EditingFile,
            ObjectName = CurrentObjectName(),
        };
    }

    private static void TouchLocal()
    {
        lock (Sync)
        {
            if (!s_users.TryGetValue(LocalId, out MultiProgramUser? u)) return;
            u.Name = LocalName;
            u.Color = LocalColor;
            u.LastSeenUtc = DateTime.UtcNow;
        }
    }

    private static MultiProgramUser Copy(MultiProgramUser user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        PcName = user.PcName,
        Color = user.Color,
        Role = user.Role,
        Scene = user.Scene,
        File = user.File,
        ObjectName = user.ObjectName,
        IsHost = user.IsHost,
        LastSeenUtc = user.LastSeenUtc,
    };

    private static MultiProgramHistoryEntry Copy(MultiProgramHistoryEntry h) => new()
    {
        Id = h.Id, User = h.User, Action = h.Action, Target = h.Target, Detail = h.Detail, AtUtc = h.AtUtc,
    };

    private static MultiProgramBan Copy(MultiProgramBan b) => new()
    {
        Id = b.Id, Name = b.Name, PcName = b.PcName, Reason = b.Reason, AtUtc = b.AtUtc,
    };

    private static List<MultiProgramUser> SnapshotUsers()
    {
        lock (Sync)
            return s_users.Values.Select(Copy).OrderByDescending(u => u.Role == MultiProgramRole.Owner)
                .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<MultiProgramBan> SnapshotBans()
    {
        lock (Sync) return s_bans.Select(Copy).ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && s_listener != null)
        {
            TcpClient tcp;
            try { tcp = await s_listener.AcceptTcpClientAsync(ct); }
            catch { break; }

            NetworkStream stream = tcp.GetStream();
            var peer = new ServerPeer
            {
                Tcp = tcp,
                Reader = new StreamReader(stream, new UTF8Encoding(false)),
                Writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true },
            };
            _ = ReadPeer(peer, ct);
        }
    }

    private static async Task ReadPeer(ServerPeer peer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await peer.Reader.ReadLineAsync();
                if (line == null) break;
                MultiProgramMessage? message = JsonSerializer.Deserialize<MultiProgramMessage>(line, JsonOptions);
                if (message != null) await HandleServerMessage(peer, message);
            }
        }
        catch { }
        finally
        {
            string? removeId;
            string? removeName;
            lock (Sync)
            {
                removeId = peer.User?.Id;
                removeName = peer.User?.Name;
                if (removeId != null)
                {
                    s_users.Remove(removeId);
                    s_peers.Remove(removeId);
                }
            }
            peer.Close();
            if (removeId != null)
            {
                AddHistory("Saiu", removeName ?? removeId, string.Empty);
                BroadcastUsers();
            }
        }
    }

    private static async Task HandleServerMessage(ServerPeer peer, MultiProgramMessage msg)
    {
        switch (msg.Type)
        {
            case "hello":
            {
                if (IsBanned(msg.Name, msg.Pc))
                {
                    await SendAsync(peer, new MultiProgramMessage { Type = "rejected", Message = "Você está banido desta sessão." });
                    peer.Close();
                    return;
                }

                string id = string.IsNullOrWhiteSpace(msg.Id) ? "dev-" + Guid.NewGuid().ToString("N")[..8] : msg.Id;
                if (id == LocalId) id = "dev-" + Guid.NewGuid().ToString("N")[..8];
                var user = new MultiProgramUser
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(msg.Name) ? "Developer" : msg.Name,
                    PcName = msg.Pc,
                    Color = NormalizeColor(msg.Color),
                    Role = MultiProgramRole.Editor,
                    Scene = msg.Scene,
                    File = msg.File,
                    ObjectName = msg.Object,
                    IsHost = false,
                };
                peer.User = user;
                lock (Sync)
                {
                    s_users[id] = user;
                    s_peers[id] = peer;
                }
                await SendAsync(peer, new MultiProgramMessage
                {
                    Type = "welcome",
                    Id = id,
                    Role = MultiProgramRole.Editor,
                    HostName = LocalName,
                    Project = ProjectName,
                    Users = SnapshotUsers(),
                });
                AddHistory("Entrou", user.Name, user.PcName + " • " + user.Color);
                BroadcastUsers();
                break;
            }
            case "presence":
            {
                if (peer.User == null) return;
                lock (Sync)
                {
                    MultiProgramUser user = peer.User;
                    user.Name = msg.Name;
                    user.PcName = msg.Pc;
                    user.Color = NormalizeColor(msg.Color);
                    user.Scene = msg.Scene;
                    user.File = msg.File;
                    user.ObjectName = msg.Object;
                    user.LastSeenUtc = DateTime.UtcNow;
                }
                BroadcastUsers();
                break;
            }
            case "history":
            {
                if (peer.User == null || peer.User.Role == MultiProgramRole.Viewer) return;
                lock (Sync)
                    s_history.Insert(0, new MultiProgramHistoryEntry
                    {
                        User = peer.User.Name,
                        Action = msg.Action,
                        Target = msg.TargetId,
                        Detail = msg.Detail,
                        AtUtc = DateTime.UtcNow,
                    });
                break;
            }
            case "role":
            {
                if (peer.User == null || peer.User.Role != MultiProgramRole.Owner) return;
                SetRole(msg.Id, msg.Role);
                break;
            }
            case "kick":
            {
                if (peer.User == null || peer.User.Role != MultiProgramRole.Owner) return;
                Kick(msg.TargetId, ban: false);
                break;
            }
            case "ban":
            {
                if (peer.User == null || peer.User.Role != MultiProgramRole.Owner) return;
                Kick(msg.TargetId, ban: true, msg.Message);
                break;
            }
            case "leave":
                peer.Close();
                break;
        }
    }

    private static void BroadcastUsers()
    {
        if (!IsHosting) return;
        MultiProgramMessage snapshot = new() { Type = "users", Users = SnapshotUsers() };
        ServerPeer[] peers;
        lock (Sync) peers = s_peers.Values.ToArray();
        foreach (ServerPeer peer in peers) _ = SendAsync(peer, snapshot);
    }

    private static async Task SendAsync(ServerPeer peer, MultiProgramMessage message)
    {
        try
        {
            string json = JsonSerializer.Serialize(message, JsonOptions);
            await peer.WriteLock.WaitAsync();
            try { await peer.Writer.WriteLineAsync(json); }
            finally { peer.WriteLock.Release(); }
        }
        catch { peer.Close(); }
    }

    private static void SendToServer(MultiProgramMessage message)
    {
        try { s_clientWriter?.WriteLine(JsonSerializer.Serialize(message, JsonOptions)); }
        catch { }
    }

    private static async Task ClientReadLoop(TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false));
            while (client.Connected)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) break;
                MultiProgramMessage? message = JsonSerializer.Deserialize<MultiProgramMessage>(line, JsonOptions);
                if (message != null) HandleClientMessage(message);
            }
        }
        catch { }
        finally
        {
            if (IsConnected && !IsHosting)
            {
                IsConnected = false;
                StatusMessage = "Conexão encerrada pelo servidor.";
            }
        }
    }

    private static void HandleClientMessage(MultiProgramMessage msg)
    {
        switch (msg.Type)
        {
            case "welcome":
                LocalId = msg.Id;
                LocalRole = msg.Role;
                HostName = msg.HostName;
                ProjectName = msg.Project;
                StatusMessage = $"Conectado a {HostName} ({msg.Project}).";
                ReplaceUsers(msg.Users);
                break;
            case "users":
                ReplaceUsers(msg.Users);
                break;
            case "role":
                LocalRole = msg.Role;
                lock (Sync) if (s_users.TryGetValue(LocalId, out MultiProgramUser? me)) me.Role = msg.Role;
                StatusMessage = $"Seu cargo agora é {msg.Role}.";
                break;
            case "kicked":
            case "rejected":
                StatusMessage = msg.Message;
                IsConnected = false;
                try { s_client?.Close(); } catch { }
                break;
        }
    }

    private static void ReplaceUsers(List<MultiProgramUser>? users)
    {
        if (users == null) return;
        lock (Sync)
        {
            s_users.Clear();
            foreach (MultiProgramUser user in users) s_users[user.Id] = user;
        }
    }

    private static bool IsBanned(string name, string pc)
    {
        lock (Sync)
            return s_bans.Any(b =>
                (!string.IsNullOrWhiteSpace(b.Name) && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(b.PcName) && string.Equals(b.PcName, pc, StringComparison.OrdinalIgnoreCase)));
    }

    private static string BanPath => Project.Current is { } project
        ? Path.Combine(project.ProjectSettingsPath, "MultiProgramBans.json")
        : string.Empty;

    private static void LoadBans()
    {
        lock (Sync) s_bans.Clear();
        try
        {
            string path = BanPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            List<MultiProgramBan>? loaded = JsonSerializer.Deserialize<List<MultiProgramBan>>(File.ReadAllText(path), JsonOptions);
            if (loaded != null) lock (Sync) s_bans.AddRange(loaded);
        }
        catch (Exception e) { Debug.LogWarning($"MultiProgram bans could not be read: {e.Message}"); }
    }

    private static void SaveBans()
    {
        try
        {
            string path = BanPath;
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(SnapshotBans(), JsonOptions));
        }
        catch (Exception e) { Debug.LogWarning($"MultiProgram bans could not be saved: {e.Message}"); }
    }
}
