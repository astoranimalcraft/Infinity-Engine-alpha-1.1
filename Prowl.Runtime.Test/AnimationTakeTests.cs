// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Runtime.Animation;
using Infinity.Vector;

using TrackCurve = Infinity.Vector.AnimationCurve;

using Xunit;

namespace Infinity.Runtime.Test;

/// <summary>The authored animation model: curves, takes sampled onto a hierarchy, and the player that advances them.</summary>
public class AnimationTakeTests : RuntimeTestBase
{
    [Fact]
    public void Curve_InterpolatesLinearlyBetweenKeys()
    {
        var curve = new TrackCurve();
        curve.AddKey(0f, 0f);
        curve.AddKey(1f, 10f);

        Assert.Equal(5f, curve.Evaluate(0.5f), 3);
    }

    [Fact]
    public void Curve_HoldsTheLastValuePastTheEnd()
    {
        var curve = new TrackCurve();
        curve.AddKey(0f, 2f);
        curve.AddKey(1f, 4f);

        Assert.Equal(4f, curve.Evaluate(3f), 3);
        Assert.Equal(2f, curve.Evaluate(-1f), 3);
    }

    [Fact]
    public void Track_GivesTheFallbackOnAnAxisWithNoKeys()
    {
        var curve = new TrackCurve();

        Assert.Equal(7f, AnimationTrack.EvaluateAxis(curve, 0.5f, 7f), 3);
    }

    [Fact]
    public void StepKey_HoldsItsValueUntilTheNextKey()
    {
        var curve = new TrackCurve();
        curve.AddKey(new Keyframe(0f, 1f) { Interpolation = CurveInterpolation.Step });
        curve.AddKey(1f, 9f);

        Assert.Equal(1f, curve.Evaluate(0.9f), 3);
    }

    [Fact]
    public void Track_SetKeyOverwritesTheKeyAlreadyAtThatTime()
    {
        var track = new AnimationTrack(string.Empty, AnimationTrackKind.TransformPosition);
        track.SetKey(0, 0.5f, 1f);
        track.SetKey(0, 0.5f, 3f);

        Assert.Equal(1, track.X.Count);
        Assert.Equal(3f, track.X.Evaluate(0.5f), 3);
    }

    [Fact]
    public void Track_KeyTimesMergesTheAxes()
    {
        var track = new AnimationTrack(string.Empty, AnimationTrackKind.TransformPosition);
        track.SetKey(0, 0f, 1f);
        track.SetKey(1, 0.5f, 1f);
        track.SetKey(2, 0.5f, 1f);

        Assert.Equal(new[] { 0f, 0.5f }, track.KeyTimes());
    }

    [Fact]
    public void Take_AppliesPositionToTheNamedChild()
    {
        GameObject root = CreateGameObject("Rig");
        var arm = new GameObject("Arm");
        arm.SetParent(root, false);

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack("Arm", AnimationTrackKind.TransformPosition);
        track.Y.AddKey(0f, 0f);
        track.Y.AddKey(1f, 2f);

        take.Apply(root.Transform, 0.5f);

        Assert.Equal(1f, arm.Transform.LocalPosition.Y, 3);
    }

    [Fact]
    public void Take_LeavesAxesWithoutKeysWhereTheyWere()
    {
        GameObject root = CreateGameObject("Rig");
        root.Transform.LocalPosition = new Float3(5f, 0f, 7f);

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.TransformPosition);
        track.Y.AddKey(0f, 0f);
        track.Y.AddKey(1f, 2f);

        take.Apply(root.Transform, 1f);

        Assert.Equal(5f, root.Transform.LocalPosition.X, 3);
        Assert.Equal(2f, root.Transform.LocalPosition.Y, 3);
        Assert.Equal(7f, root.Transform.LocalPosition.Z, 3);
    }

    [Fact]
    public void Take_SkipsATargetThatIsGone()
    {
        GameObject root = CreateGameObject("Rig");

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack("Missing/Bone", AnimationTrackKind.TransformPosition);
        track.X.AddKey(0f, 1f);

        take.Apply(root.Transform, 0f);

        Assert.Equal(0f, root.Transform.LocalPosition.X, 3);
    }

    [Fact]
    public void Player_WrapsAtTheEndWhenTheTakeLoops()
    {
        GameObject root = CreateGameObject("Rig");
        AnimationPlayer player = root.AddComponent<AnimationPlayer>();
        player.Take.Duration = 1f;
        player.Take.Loop = true;

        player.Play();
        player.Advance(0.6f);
        player.Advance(0.6f);

        Assert.Equal(0.2f, player.CurrentTime, 3);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public void Player_StopsAtTheEndWhenTheTakeDoesNotLoop()
    {
        GameObject root = CreateGameObject("Rig");
        AnimationPlayer player = root.AddComponent<AnimationPlayer>();
        player.Take.Duration = 1f;
        player.Take.Loop = false;

        player.Play();
        player.Advance(2f);

        Assert.Equal(1f, player.CurrentTime, 3);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Player_SeekPosesWithoutPlaying()
    {
        GameObject root = CreateGameObject("Rig");
        AnimationPlayer player = root.AddComponent<AnimationPlayer>();
        player.Take.Duration = 1f;
        AnimationTrack track = player.Take.GetOrAddTrack(string.Empty, AnimationTrackKind.TransformScale);
        track.X.AddKey(0f, 1f);
        track.X.AddKey(1f, 3f);

        player.Seek(1f);

        Assert.Equal(3f, root.Transform.LocalScale.X, 3);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Player_KeepsItsTakeThroughSerialization()
    {
        GameObject root = CreateGameObject("Rig");
        AnimationPlayer player = root.AddComponent<AnimationPlayer>();
        player.Take.Duration = 2f;
        AnimationTrack track = player.Take.GetOrAddTrack("Arm", AnimationTrackKind.TransformPosition);
        track.X.AddKey(0f, 0f);
        track.X.AddKey(2f, 5f);

        Echo.EchoObject echo = Echo.Serializer.Serialize(typeof(object), root);
        GameObject copy = Echo.Serializer.Deserialize<GameObject>(echo)!;

        AnimationPlayer? copyPlayer = copy.GetComponent<AnimationPlayer>();
        Assert.NotNull(copyPlayer);
        Assert.Equal(2f, copyPlayer!.Take.Duration, 3);
        Assert.Single(copyPlayer.Take.Tracks);
        Assert.Equal(5f, copyPlayer.Take.Tracks[0].X.Evaluate(2f), 3);
    }
}
