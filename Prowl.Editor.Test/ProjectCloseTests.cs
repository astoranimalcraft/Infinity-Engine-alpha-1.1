// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Editor.GUI.SceneView;
using Infinity.Editor.Projects.Scripting;
using Infinity.Runtime;

using Xunit;

namespace Infinity.Editor.Test;

public class ProjectCloseTests : EditorTestHarness
{
    [Fact]
    public void ResetForProjectSwitch_EmptiesTheEditWorld()
    {
        GameObject thing = EditorSceneManager.EditWorld.Create("Thing");
        Assert.Contains(EditorSceneManager.EditWorld.Objects, go => go == thing);

        EditorSceneManager.ResetForProjectSwitch();

        Assert.True(thing.IsDisposed);
        Assert.Empty(EditorSceneManager.EditWorld.Objects);
        Assert.Empty(EditorSceneManager.OpenScenes);
    }

    [Fact]
    public void UnloadProject_IsSafeWhenNoScriptsWereLoaded()
    {
        ScriptAssemblyManager.UnloadProject();
        ScriptAssemblyManager.UnloadProject();

        Assert.False(ScriptAssemblyManager.IsCompiling);
    }
}
