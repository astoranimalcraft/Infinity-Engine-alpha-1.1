using System;
using System.Collections.Generic;
using System.Threading;

namespace Infinity.Editor.Build;

/// <summary>One line of build output, which the progress modal shows above the bar.</summary>
public sealed record BuildState(string Message);

/// <summary>What a finished build reports back to the editor and to the CLI.</summary>
public sealed class BuildResult
{
    public bool Success { get; init; }

    public bool Cancelled { get; init; }

    public string OutputPath { get; init; } = "";

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// The state of one build, polled by the editor while its modal is up and by the CLI while it waits. Written on the
/// build's thread and read on the UI's, so every field it hands out is set once and never mutated afterwards.
/// </summary>
public sealed class BuildProgress
{
    private readonly CancellationTokenSource _cancellation = new();
    private BuildState? _state;
    private BuildResult? _result;
    private float _progress;

    /// <summary>Whether the build reported a result, successfully or not.</summary>
    public bool IsComplete => _result != null;

    public bool IsCancelled => _cancellation.IsCancellationRequested;

    /// <summary>How far along the build is, from 0 to 1.</summary>
    public float ProgressValue => _progress;

    public BuildResult? Result => _result;

    /// <summary>Cancelled when the build should stop, which the build's steps check between them.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>The last line the build logged.</summary>
    public BuildState? GetState() => _state;

    public void Report(string message, float progress)
    {
        _state = new BuildState(message);
        _progress = Math.Clamp(progress, 0f, 1f);
    }

    public void Complete(BuildResult result)
    {
        if (result.Success) _progress = 1f;
        _result = result;
    }

    public void Cancel() => _cancellation.Cancel();
}
