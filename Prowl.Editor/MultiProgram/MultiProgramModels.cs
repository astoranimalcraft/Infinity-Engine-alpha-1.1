// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Infinity.Editor.MultiProgram;

/// <summary>What a connected developer is allowed to do.</summary>
public enum MultiProgramRole
{
    Owner,
    Editor,
    Viewer,
}

/// <summary>One person connected to a MultiProgram session.</summary>
public sealed class MultiProgramUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Developer";
    public string PcName { get; set; } = Environment.MachineName;
    public string Color { get; set; } = "#7C5CFF";
    public MultiProgramRole Role { get; set; } = MultiProgramRole.Editor;
    public string Scene { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A change recorded by the host. Only the owner can read this list.</summary>
public sealed class MultiProgramHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A name/PC pair the host refuses to let back into the session.</summary>
public sealed class MultiProgramBan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string PcName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>The newline-delimited JSON envelope used between the host and its clients.</summary>
public sealed class MultiProgramMessage
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Pc { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Scene { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public MultiProgramRole Role { get; set; }
    public List<MultiProgramUser>? Users { get; set; }
    public List<MultiProgramBan>? Bans { get; set; }
    public MultiProgramHistoryEntry? History { get; set; }
}
