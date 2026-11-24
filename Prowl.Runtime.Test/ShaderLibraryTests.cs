// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Graphite.Rendering;
using Infinity.Graphite.ShaderDef;

using Xunit;

namespace Infinity.Runtime.Test;

public class ShaderLibraryTests
{
    static ShaderLibraryTests() => ShaderLibrary.Register(typeof(Renderer).Assembly);

    [Theory]
    [InlineData("Graphite/Fallback.shader")]
    [InlineData("Graphite/DepthCaster.shader")]
    [InlineData("Graphite/Standard/Lit.shader")]
    [InlineData("Graphite/Skies/Gradient.shader")]
    public void BuiltInShader_IsEmbedded(string name)
        => Assert.False(string.IsNullOrWhiteSpace(ShaderLibrary.Get(name)), $"{name} is missing from the embedded shader library.");

    [Fact]
    public void FallbackAndDepthCaster_Parse()
    {
        _ = ShaderParser.Parse(ShaderLibrary.Get("Graphite/Fallback.shader")!);
        _ = ShaderParser.Parse(ShaderLibrary.Get("Graphite/DepthCaster.shader")!);
    }
}
