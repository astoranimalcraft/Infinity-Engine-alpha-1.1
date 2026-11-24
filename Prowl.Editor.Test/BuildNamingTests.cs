// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Editor.Projects.Settings;

using Xunit;

namespace Infinity.Editor.Test;

public class BuildNamingTests
{
    [Theory]
    [InlineData("My Game", "My Game")]
    [InlineData("My Game.exe", "My Game")]
    [InlineData("A:B*C", "A_B_C")]
    [InlineData("CON", "CON_")]
    [InlineData("  Final Build.  ", "Final Build")]
    public void ExecutableName_IsSafeForTheFileSystem(string requested, string expected)
    {
        var settings = new BuildSettings { ExecutableName = requested };
        Assert.Equal(expected, settings.ResolveExecutableName());
    }
}
