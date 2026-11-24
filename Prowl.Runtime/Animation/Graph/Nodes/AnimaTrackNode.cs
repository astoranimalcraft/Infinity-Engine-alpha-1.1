// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Motion;

namespace Infinity.Runtime.AnimationNodes;

/// <summary>
/// Plays an <see cref="AnimaTrack"/> authored on the timeline as a pose in an animation graph. Transform tracks are
/// matched to bones by name, so a take made by keying a hierarchy can be blended with imported clips.
/// </summary>
public sealed class AnimaTrackNode : AnimationGraphNode
{
    private readonly AssetSetting _track = Asset("Anima Track", NodeValueKind.AnimaTrack);
    private readonly FlagSetting _loop = Toggle("Loop", true);
    private readonly NumberSetting _speed = Number("Speed", 1f);
    private readonly NumberSetting _start = Number("Start");

    public AnimaTrackNode() => Settings(_track, _loop, _speed, _start);

    public override string Id => AnimationNodeIds.AnimaTrack;
    public override string DisplayName => "Anima Track";
    public override string Category => "Sources";
    public override string Description => "Plays a timeline Anima Track as a pose, matched to bones by name.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord record)
    {
        AnimationClipBase? clip = ctx.AnimaTrackClip(record, _track);
        if (clip == null) return ctx.Graph.AddReferencePose();

        return ctx.Graph.AddNode(new ClipNodeDefinition(clip)
        {
            Loop = record.Get(_loop),
            SpeedMultiplier = record.Get(_speed),
            StartTime = record.Get(_start),
        });
    }
}
