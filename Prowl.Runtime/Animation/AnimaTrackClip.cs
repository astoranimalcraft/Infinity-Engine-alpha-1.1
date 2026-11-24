// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Motion;
using Infinity.Vector;
using Infinity.Vector.Spatial;

namespace Infinity.Runtime.Animation;

/// <summary>
/// Plays an <see cref="AnimaTrack"/> on a Motion skeleton, which is what makes a timeline animation usable in an
/// animation graph. Transform tracks are matched to bones by name: a track whose target ends in the bone's name drives
/// that bone. Tracks that match no bone are ignored, so a graph can sample a take authored for a slightly different
/// hierarchy.
/// </summary>
public sealed class AnimaTrackClip : AnimationClipBase
{
    private readonly AnimaTrack _track;
    private readonly Dictionary<int, AnimationTrack> _boneTracks = [];
    private readonly Dictionary<int, AnimationTrack> _rotationTracks = [];
    private readonly Dictionary<int, AnimationTrack> _scaleTracks = [];

    public AnimaTrackClip(AnimaTrack track, Skeleton skeleton)
        : base(skeleton, Math.Max(2, track.FrameCount), MathF.Max(track.Duration, 1f / 1000f), false, null, null, null, null)
    {
        _track = track;
        MapBones(skeleton);
    }

    public AnimaTrack Track => _track;

    private void MapBones(Skeleton skeleton)
    {
        foreach (AnimationTrack track in _track.Take.Tracks)
        {
            int bone = BoneOf(skeleton, track.Target);
            if (bone < 0) continue;

            switch (track.Kind)
            {
                case AnimationTrackKind.TransformPosition:
                    _boneTracks[bone] = track;
                    break;
                case AnimationTrackKind.TransformRotation:
                    _rotationTracks[bone] = track;
                    break;
                case AnimationTrackKind.TransformScale:
                    _scaleTracks[bone] = track;
                    break;
            }
        }
    }

    private static int BoneOf(Skeleton skeleton, string target)
    {
        if (string.IsNullOrEmpty(target)) return skeleton.BoneCount > 0 ? 0 : -1;

        string name = target.Replace('\\', '/');
        int slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];

        int bone = skeleton.GetBoneIndex(new StringID(name));
        if (bone >= 0) return bone;

        // A path may name a bone through its parent, so try every segment from the leaf upward.
        string[] parts = target.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            bone = skeleton.GetBoneIndex(new StringID(parts[i]));
            if (bone >= 0) return bone;
        }
        return -1;
    }

    public override void GetPose(FrameTime frameTime, Pose result, SkeletonMapping? mapping)
    {
        float frames = MathF.Max(1f, FrameCount - 1f);
        float time = Math.Clamp(frameTime.ToFloat() / frames, 0f, 1f) * Duration;

        Skeleton skeleton = result.Skeleton;
        IReadOnlyList<Transform3D> reference = skeleton.ParentSpaceReferencePose;

        for (int bone = 0; bone < skeleton.BoneCount; bone++)
        {
            Transform3D pose = reference[bone];

            if (_boneTracks.TryGetValue(bone, out AnimationTrack? position))
                pose.position = position.EvaluateVector(time, pose.position);
            if (_rotationTracks.TryGetValue(bone, out AnimationTrack? rotation))
            {
                Float3 euler = rotation.EvaluateVector(time, pose.rotation.EulerAngles);
                pose.rotation = Quaternion.FromEuler(euler);
            }
            if (_scaleTracks.TryGetValue(bone, out AnimationTrack? scale))
                pose.scale = scale.EvaluateVector(time, pose.scale);

            result.SetTransform(bone, pose);
        }
    }
}
