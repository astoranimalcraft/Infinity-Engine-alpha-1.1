// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Editor.GUI.ApiDocs;

using Xunit;

namespace Infinity.Editor.Test;

public class ApiDocsTests
{
    [Fact]
    public void Catalog_IndexesCoreTypesAndMembers()
    {
        IReadOnlyList<ApiDocEntry> entries = ApiDocsCatalog.GetEntries(includeNonPublic: false);

        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Runtime.GameObject" && entry.IsType);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Runtime.AnimationPlayer"
                                       && entry.Category == ApiDocCategory.Component);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Runtime.GameObject.AddComponent"
                                       && entry.Category == ApiDocCategory.Method);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Runtime.Time.DeltaTime"
                                       && entry.Category == ApiDocCategory.Property);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Vector.Maths" && entry.IsType);
        Assert.Contains(entries, entry => entry.QualifiedName == "Infinity.Vector.Maths.Clamp"
                                       && entry.Category == ApiDocCategory.Function);
    }

    [Fact]
    public void Catalog_EntriesHaveSearchableSummariesAndExamples()
    {
        IReadOnlyList<ApiDocEntry> entries = ApiDocsCatalog.GetEntries(includeNonPublic: false);

        ApiDocEntry gameObject = Assert.Single(entries,
            entry => entry.QualifiedName == "Infinity.Runtime.GameObject" && entry.IsType);
        Assert.False(string.IsNullOrWhiteSpace(gameObject.Summary));
        Assert.False(string.IsNullOrWhiteSpace(gameObject.Example));
        Assert.Contains("gameobject", gameObject.SearchText);

        ApiDocEntry play = Assert.Single(entries,
            entry => entry.QualifiedName == "Infinity.Runtime.AnimationPlayer.Play"
                  && entry.Member is System.Reflection.MethodInfo method && method.GetParameters().Length == 2);
        Assert.False(string.IsNullOrWhiteSpace(play.Example));
        Assert.Contains("AnimaTrack", play.Signature);
    }

    [Fact]
    public void Catalog_ReusesTheSameIndexBetweenCalls()
    {
        IReadOnlyList<ApiDocEntry> first = ApiDocsCatalog.GetEntries(includeNonPublic: false);
        IReadOnlyList<ApiDocEntry> second = ApiDocsCatalog.GetEntries(includeNonPublic: false);

        Assert.Same(first, second);
    }

    [Fact]
    public void Catalog_NonPublicModeAddsEntries()
    {
        int publicCount = ApiDocsCatalog.GetEntries(includeNonPublic: false).Count;
        int allCount = ApiDocsCatalog.GetEntries(includeNonPublic: true).Count;

        Assert.True(allCount > publicCount);
    }
}
