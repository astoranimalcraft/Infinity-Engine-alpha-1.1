// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.


using Infinity.Editor.Build;
using Infinity.Editor.Importers;
using Infinity.Editor.Projects.Scripting;
using Infinity.Editor.Projects.Settings;
using Infinity.Runtime;
using Infinity.Runtime.Resources;

using Xunit;

namespace Infinity.Editor.Test;

/// <summary>
/// Confirms a sub-asset's own dependency graph entry (e.g. a Sprite's reference to its Texture2D)
/// survives a simulated editor restart, not just the current session - SubAssetEntry persists its
/// own Dependencies and EditorAssetDatabase re-seeds them from the metadata cache on startup.
/// </summary>
[Trait("Category", "Build")]
public class SubAssetDependencyPersistenceTests : EditorTestHarness
{
    private const int TexSize = 4;
    private const byte R = 10, G = 200, B = 90, A = 255;

    [Fact]
    public void SpriteDependency_SurvivesSimulatedEditorRestart()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        string pngPath = AssetAbsolutePath("PersistTexture.png");
        TestImages.WriteSolidPng(pngPath, TexSize, R, G, B, A);
        Guid texGuid = Assets.ImportFile("PersistTexture.png");
        Assert.NotEqual(Guid.Empty, texGuid);

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.Single });
        SubAssetEntry[] subAssets = Assets.GetSubAssets(texGuid);
        Assert.True(subAssets.Length > 0, "Expected a Sprite sub-asset after enabling Sprite mode.");
        Guid spriteGuid = subAssets[0].Guid;

        Guid sceneGuid = AuthorSceneReferencingSprite(spriteGuid);

        // Simulate closing and reopening the editor without touching the texture again, so this
        // only exercises what gets restored from the persisted metadata cache.
        ReopenDatabase();

        // Confirm the sub-asset itself still resolves post-reopen.
        var spriteAfterReopen = AssetDatabase.Get(spriteGuid) as Sprite;
        Assert.True(spriteAfterReopen.IsValid(), "Sprite sub-asset itself should still resolve after a reopen.");

        string outputDir = RunBuild(sceneGuid, AssetPackagingMode.LooseFiles);
        try
        {
            string stdout = RunPlayerHeadless(outputDir);

            Assert.Contains("PROWL_SPRITE_CHECK", stdout);
            Assert.Contains("spriteValid=True", stdout);
            Assert.Contains("texValid=True", stdout); // fails if sub-asset dependencies aren't persisted
        }
        finally
        {
            TryDeleteDir(outputDir);
        }
    }

    private Guid AuthorSceneReferencingSprite(Guid spriteGuid)
    {
        File.WriteAllText(AssetAbsolutePath("PersistCheckComponent.cs"), """
            using Infinity.Runtime;
            using Infinity.Runtime.Resources;

            public class PersistCheckComponent : Component
            {
                public Sprite? MySprite;

                public override void Start()
                {
                    var sprite = MySprite;
                    var tex = sprite.IsValid() ? sprite.Texture : null;
                    System.Console.WriteLine($"PROWL_SPRITE_CHECK|spriteValid={sprite.IsValid()}|texValid={tex.IsValid()}|width={(tex.IsValid() ? tex.Width : 0)}|height={(tex.IsValid() ? tex.Height : 0)}");
                }
            }
            """);

        ScriptCompiler.CompileResult compile = Projects.Scripting.ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Script compile failed:\n{compile.Errors}\n{compile.Output}");

        var gameAsm = System.Reflection.Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        Type? compType = gameAsm.GetType("PersistCheckComponent");
        Assert.NotNull(compType);

        var scene = new SceneInstance(SceneMode.Edit);
        var go = new GameObject("PersistChecker");
        Component comp = go.AddComponent(compType!);
        compType!.GetField("MySprite")!.SetValue(comp, AssetDatabase.Get<Sprite>(spriteGuid));
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");
        Assert.NotEqual(Guid.Empty, sceneGuid);
        return sceneGuid;
    }
}
