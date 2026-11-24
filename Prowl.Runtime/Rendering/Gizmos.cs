using System;
using System.Collections.Generic;

using Infinity.Runtime;

namespace Infinity.Graphite.Rendering;

/// <summary>
/// The render feature a view draws gizmos with. It holds the batches the scene hands it and runs in the overlay slot,
/// over the finished image.
/// </summary>
/// <remarks>
/// Graphite ships no gizmo rasteriser, so the editor's scene view draws these batches on its overlay and this feature
/// only holds them. Everything above it works unchanged: <see cref="Debug"/>'s draw calls fill a
/// <see cref="GizmoBatch"/>, the batch reports its lines for hitbox picking, and views hold this feature.
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

    // Declares no pass. WriteColor starts a new colour target for the features after this one to fill, and nothing here
    // fills it, so declaring the write left the view presenting an unwritten target: a blank viewport as soon as gizmos
    // were switched on. The batches stay collected, for the scene view to draw and for hitbox picking.
    public override void Setup(FeatureBuilder builder, in FeatureView view) { }

    public override void Render(FeatureContext context) { }

    public void Dispose() => _batches.Clear();
}
