// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Runtime.Animation;
using Infinity.Runtime.Resources;
using Infinity.Runtime.UI;
using Infinity.Vector;

using Xunit;

namespace Infinity.Runtime.Test;

/// <summary>
/// The track kinds added for look animation: tint and opacity, material uniforms, material maps and component
/// properties, plus the timeline editing operations that move keys around.
/// </summary>
public class AnimationTrackKindsTests : RuntimeTestBase
{
    [Fact]
    public void TintTrack_FadesColorAndAlphaTogether()
    {
        GameObject go = CreateGameObject("Sprite");
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.Color = Color.White;

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.TintColor);
        track.SetVectorKey(0f, new Float4(1f, 1f, 1f, 1f));
        track.SetVectorKey(1f, new Float4(1f, 0f, 0f, 0f));

        take.Apply(go.Transform, 0.5f);

        Assert.Equal(1f, renderer.Color.R, 3);
        Assert.Equal(0.5f, renderer.Color.G, 3);
        Assert.Equal(0.5f, renderer.Color.A, 3);
    }

    [Fact]
    public void TintTrack_KeysAllFourChannels()
    {
        var track = new AnimationTrack(string.Empty, AnimationTrackKind.TintColor);
        track.SetVectorKey(0.25f, new Float4(1f, 0.5f, 0.25f, 0.75f));

        Assert.Equal(4, track.Channels);
        Assert.Equal(new[] { 0.25f }, track.KeyTimes());
        Assert.Equal(0.75f, track.W.Evaluate(0.25f), 3);
    }

    [Fact]
    public void MaterialTrack_AnimatesAFloatUniform()
    {
        GameObject go = CreateGameObject("Mesh");
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        var material = new Material(Shader.LoadDefault(DefaultShader.Lit));
        renderer.Material = material;

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.MaterialProperty, string.Empty, "base_metalness", 1);
        track.SetKey(0, 0f, 0f);
        track.SetKey(0, 1f, 1f);

        take.Apply(go.Transform, 0.25f);

        Assert.Equal(0.25f, material.GetFloat("base_metalness"), 3);
    }

    [Fact]
    public void MaterialTrack_AnimatesAColorUniform()
    {
        GameObject go = CreateGameObject("Mesh");
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        var material = new Material(Shader.LoadDefault(DefaultShader.Lit));
        renderer.Material = material;

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.MaterialProperty, string.Empty, "base_color", 4);
        track.SetVectorKey(0f, new Float4(1f, 1f, 1f, 1f));
        track.SetVectorKey(1f, new Float4(0f, 0f, 0f, 0f));

        take.Apply(go.Transform, 0.5f);

        Assert.Equal(0.5f, material.GetColor("base_color").R, 3);
        Assert.Equal(0.5f, material.GetColor("base_color").A, 3);
    }

    [Fact]
    public void ComponentTrack_AnimatesAPropertyOfAComponent()
    {
        GameObject go = CreateGameObject("Panel");
        CanvasGroup group = go.AddComponent<CanvasGroup>();
        group.Alpha = 1f;

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.ComponentProperty, "CanvasGroup", "Alpha", 1);
        track.SetKey(0, 0f, 1f);
        track.SetKey(0, 1f, 0f);

        take.Apply(go.Transform, 0.75f);

        Assert.Equal(0.25f, group.Alpha, 3);
    }

    [Fact]
    public void ComponentTrack_AnimatesAVectorProperty()
    {
        GameObject go = CreateGameObject("Panel");
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.SizeDelta = new Float2(100f, 50f);

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.ComponentProperty, "RectTransform", "SizeDelta", 2);
        track.SetKey(0, 0f, 100f);
        track.SetKey(0, 1f, 200f);
        track.SetKey(1, 0f, 50f);
        track.SetKey(1, 1f, 150f);

        take.Apply(go.Transform, 0.5f);

        Assert.Equal(150f, rect.SizeDelta.X, 3);
        Assert.Equal(100f, rect.SizeDelta.Y, 3);
    }

    [Fact]
    public void TextureTrack_SwapsTheMapAtItsKeys()
    {
        GameObject go = CreateGameObject("Mesh");
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        var material = new Material(Shader.LoadDefault(DefaultShader.Lit));
        renderer.Material = material;

        var first = new Texture2D(4, 4);
        var second = new Texture2D(4, 4);

        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.MaterialTexture, string.Empty, "base_color_map", 1);
        track.AddTextureKey(0f, first);
        track.AddTextureKey(0.5f, second);

        take.Apply(go.Transform, 0.25f);
        Assert.Same(first, material.GetTexture("base_color_map"));

        take.Apply(go.Transform, 0.75f);
        Assert.Same(second, material.GetTexture("base_color_map"));
    }

    [Fact]
    public void InsertTime_ShiftsLaterKeysAndGrowsTheTake()
    {
        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.TransformPosition);
        track.X.AddKey(0f, 0f);
        track.X.AddKey(1f, 5f);

        take.InsertTime(0.5f, 0.5f);

        Assert.Equal(new[] { 0f, 1.5f }, track.KeyTimes());
        Assert.Equal(1.5f, take.Duration, 3);
        Assert.Equal(5f, track.X.Evaluate(1.5f), 3);
    }

    [Fact]
    public void InsertTime_PastTheEndLeavesTheLengthAlone()
    {
        var take = new AnimationTake { Duration = 1f };
        AnimationTrack track = take.GetOrAddTrack(string.Empty, AnimationTrackKind.TransformPosition);
        track.X.AddKey(0f, 0f);

        take.InsertTime(2f, 0.5f);

        Assert.Equal(1f, take.Duration, 3);
        Assert.Equal(new[] { 0f }, track.KeyTimes());
    }

    [Fact]
    public void MoveKeys_RetimesAKeyAndKeepsItsValue()
    {
        var track = new AnimationTrack(string.Empty, AnimationTrackKind.TransformScale);
        track.SetKey(0, 0f, 1f);
        track.SetKey(0, 1f, 3f);

        track.MoveKeys(1f, 2f);

        Assert.Equal(new[] { 0f, 2f }, track.KeyTimes());
        Assert.Equal(3f, track.X.Evaluate(2f), 3);
        Assert.Equal(2.5f, track.X.Evaluate(1.5f), 3);
    }

    [Fact]
    public void SetInterpolationAt_ReachesEveryChannelOfTheKey()
    {
        var track = new AnimationTrack(string.Empty, AnimationTrackKind.TransformPosition);
        track.SetKey(0, 0f, 0f);
        track.SetKey(1, 0f, 0f);
        track.SetKey(2, 0f, 0f);

        track.SetInterpolationAt(0f, CurveInterpolation.Step);

        Assert.Equal(CurveInterpolation.Step, track.X[0].Interpolation);
        Assert.Equal(CurveInterpolation.Step, track.Y[0].Interpolation);
        Assert.Equal(CurveInterpolation.Step, track.Z[0].Interpolation);
    }

    [Fact]
    public void AllKeyTimes_MergesEveryTrack()
    {
        var take = new AnimationTake { Duration = 2f };
        take.GetOrAddTrack(string.Empty, AnimationTrackKind.TransformPosition).X.AddKey(0.5f, 0f);
        take.GetOrAddTrack("Arm", AnimationTrackKind.TransformScale).X.AddKey(0.5f, 1f);
        take.GetOrAddTrack("Arm", AnimationTrackKind.TransformRotation).X.AddKey(1.25f, 2f);

        Assert.Equal(new[] { 0.5f, 1.25f }, take.AllKeyTimes());
    }
}
