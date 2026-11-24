// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Runtime;
using Infinity.Vector;

namespace Infinity.Runtime.Graphs;

/// <summary>
/// The base class generated script graph classes derive from. It hands the graph the same GameObject, transform
/// and time values a hand-written component has.
/// </summary>
public abstract class ScriptGraphBehaviour
{
    /// <summary>The object the graph runs on.</summary>
    public GameObject GameObject { get; internal set; } = null!;

    /// <summary>The object's transform, the usual target of move and rotate nodes.</summary>
    public Transform Transform => GameObject.Transform;

    /// <summary>Called once before the first update.</summary>
    public virtual void OnStart() { }

    /// <summary>Called every frame.</summary>
    public virtual void OnUpdate() { }

    /// <summary>Called every frame after Update.</summary>
    public virtual void OnLateUpdate() { }

    /// <summary>Called when the runner is enabled.</summary>
    public virtual void OnEnable() { }

    /// <summary>Called when the runner is disabled.</summary>
    public virtual void OnDisable() { }
}
