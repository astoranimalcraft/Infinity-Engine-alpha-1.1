using System;
using System.Collections.Generic;

using Prowl.Runtime;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The render feature a view draws gizmos with. It holds the batches the scene hands it and runs in the overlay slot,
/// over the finished image.
/// </summary>
/// <remarks>
/// Graphite ships no gizmo rasteriser, so this collects the batches and leaves the draw to a backend that has one.
/// Everything above it works unchanged: <see cref="Debug"/>'s draw calls fill a <see cref="GizmoBatch"/>, the batch
/// reports its lines for hitbox picking, and views hold this feature.
/// </remarks>
public sealed class Gizmos : RenderFeature, IDisposable
{
    private readonly List<GizmoBatch> _batches = [];

    public Gizmos(Renderer renderer) : base(RenderSlot.Overlay) { }

    /// <summary>The batches this feature draws.</summary>
    public IReadOnlyList<GizmoBatch> Batches => _batches;

    public void Add(GizmoBatch batch)
    {
        if (!_batches.Contains(batch)) _batches.Add(batch);
    }

    public void Remove(GizmoBatch batch) => _batches.Remove(batch);

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();

    public override void Render(FeatureContext context) { }

    public void Dispose() => _batches.Clear();
}
