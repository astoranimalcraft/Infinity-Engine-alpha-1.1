// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

using Infinity.Editor.Projects;
using Infinity.Editor.Theming;
using Infinity.Runtime;

namespace Infinity.Editor.Core;

/// <summary>
/// Discord Rich Presence over the local Discord IPC socket. No external package is needed: the protocol is a
/// length prefixed JSON handshake followed by SET_ACTIVITY commands. Create a Discord application, upload the
/// engine logo as an art asset named by <see cref="DiscordPreferences.LargeImageKey"/>, and paste its
/// application id in Preferences > Discord Rich Presence.
/// </summary>
public static class DiscordRichPresence
{
    private static readonly object s_lock = new();
    private static Thread? _worker;
    private static Stream? _stream;
    private static volatile bool _running;
    private static string _applicationId = "";
    private static string _largeImageKey = "infinity_engine";
    private static string _details = "Infinity Engine";
    private static string _state = "Editor de jogos";
    private static long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static bool _dirty = true;

    /// <summary>Starts or restarts the presence with the current preferences.</summary>
    public static void Start()
    {
        Stop();
        DiscordPreferences prefs;
        try { prefs = Preferences.Get<DiscordPreferences>(); }
        catch (InvalidOperationException) { return; }
        if (!prefs.Enabled) return;

        string appId = prefs.ApplicationId.Trim();
        if (appId.Length == 0) appId = Environment.GetEnvironmentVariable("INFINITY_DISCORD_APP_ID")?.Trim() ?? "";
        if (appId.Length == 0)
        {
            Runtime.Debug.Log("[Discord] Rich Presence is on but no application id is set. Preferences > Discord Rich Presence.");
            return;
        }

        _applicationId = appId;
        _largeImageKey = string.IsNullOrWhiteSpace(prefs.LargeImageKey) ? "infinity_engine" : prefs.LargeImageKey.Trim();
        _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SetProject(Project.Current?.Name);
        _running = true;
        _dirty = true;
        _worker = new Thread(Loop) { IsBackground = true, Name = "Infinity Discord Presence" };
        _worker.Start();
    }

    /// <summary>Stops the worker and closes the IPC pipe.</summary>
    public static void Stop()
    {
        _running = false;
        lock (s_lock)
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }
        _worker = null;
    }

    /// <summary>Shows the open project in the presence, or the editor alone when null.</summary>
    public static void SetProject(string? projectName)
    {
        _details = string.IsNullOrWhiteSpace(projectName) ? "Infinity Engine" : $"Projeto: {projectName}";
        _state = string.IsNullOrWhiteSpace(projectName) ? "Editor de jogos" : "Editando no Infinity Engine";
        _dirty = true;
    }

    private static void Loop()
    {
        while (_running)
        {
            try
            {
                if (_stream == null && !Connect()) { Thread.Sleep(5000); continue; }
                if (_dirty) { _dirty = false; SendActivity(); }
                Thread.Sleep(1000);
            }
            catch
            {
                CloseStream();
                Thread.Sleep(3000);
            }
        }
        CloseStream();
    }

    private static bool Connect()
    {
        for (int i = 0; i < 10 && _running; i++)
        {
            try
            {
                Stream stream = OperatingSystem.IsWindows()
                    ? ConnectPipe(i)
                    : ConnectSocket(i);
                lock (s_lock) _stream = stream;

                SendFrame(0, JsonSerializer.Serialize(new { v = 1, client_id = _applicationId }));
                Thread.Sleep(250);
                return true;
            }
            catch
            {
                CloseStream();
            }
        }
        return false;
    }

    private static Stream ConnectSocket(int index)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        string? runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string[] candidates =
        [
            string.IsNullOrEmpty(runtimeDir) ? "" : Path.Combine(runtimeDir, $"discord-ipc-{index}"),
            Path.Combine(Path.GetTempPath(), $"discord-ipc-{index}"),
            $"/tmp/discord-ipc-{index}",
        ];

        Exception? last = null;
        foreach (string path in candidates)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) { last = e; }
        }
        socket.Dispose();
        throw last ?? new IOException("Discord IPC socket not found.");
    }

    private static Stream ConnectPipe(int index)
    {
        var pipe = new NamedPipeClientStream(".", $"discord-ipc-{index}", PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(400);
        return pipe;
    }

    private static void SendActivity()
    {
        var activity = new Dictionary<string, object?>
        {
            ["details"] = _details,
            ["state"] = _state,
            ["timestamps"] = new Dictionary<string, object?> { ["start"] = _startedAt },
            ["assets"] = new Dictionary<string, object?>
            {
                ["large_image"] = _largeImageKey,
                ["large_text"] = "Infinity Engine",
            },
            ["instance"] = true,
        };

        var payload = new Dictionary<string, object?>
        {
            ["cmd"] = "SET_ACTIVITY",
            ["nonce"] = Guid.NewGuid().ToString(),
            ["args"] = new Dictionary<string, object?>
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity,
            },
        };
        SendFrame(1, JsonSerializer.Serialize(payload));
    }

    private static void SendFrame(int opcode, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        lock (s_lock)
        {
            if (_stream == null) return;
            Span<byte> header = stackalloc byte[8];
            BitConverter.TryWriteBytes(header[..4], opcode);
            BitConverter.TryWriteBytes(header[4..], body.Length);
            _stream.Write(header);
            _stream.Write(body);
            _stream.Flush();
        }
    }

    private static void CloseStream()
    {
        lock (s_lock)
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }
    }
}

/// <summary>Preferences for Discord Rich Presence. The application id is required; the logo is an uploaded art asset key.</summary>
[EditorPreferences("Discord Rich Presence", EditorIcons.Gamepad, order: 16)]
public sealed class DiscordPreferences : EditorPreferences
{
    [Tooltip("Show the project being edited on the Discord profile.")]
    public bool Enabled = true;

    [Tooltip("Application id from https://discord.com/developers/applications. Rich Presence assets and the engine logo live there.")]
    public string ApplicationId = Environment.GetEnvironmentVariable("INFINITY_DISCORD_APP_ID") ?? "";

    [Tooltip("Art asset key uploaded to the Discord application. Use infinity_engine for the engine logo.")]
    public string LargeImageKey = "infinity_engine";

    public override void Apply()
    {
        try { DiscordRichPresence.Start(); }
        catch (Exception e) { Runtime.Debug.LogWarning($"[Discord] Could not start Rich Presence: {e.Message}"); }
    }
}
