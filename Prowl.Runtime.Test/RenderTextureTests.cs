// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Runtime.Resources;

using Xunit;

namespace Infinity.Runtime.Test;

public class RenderTextureTests
{
    // Effects rent width / 2, which is 0 on a 1 pixel viewport; release has to find it again.
    [Fact]
    public void TemporaryRT_ZeroSizedRentIsReused()
    {
        TextureFormat[] format = [TextureFormat.RG32Float];
        var first = RenderTexture.GetTemporaryRT(0, 7, false, format);
        RenderTexture.ReleaseTemporaryRT(first);

        var second = RenderTexture.GetTemporaryRT(0, 7, false, format);
        RenderTexture.ReleaseTemporaryRT(second);

        Assert.Same(first, second);
    }
}
