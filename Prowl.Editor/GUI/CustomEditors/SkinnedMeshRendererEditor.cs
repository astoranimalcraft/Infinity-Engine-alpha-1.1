using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.Projects;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Runtime;
using Infinity.Runtime.Resources;
using Infinity.Scribe;

namespace Infinity.Editor.Inspector;

// ================================================================
//  Skinned Mesh Renderer Component Editor
//  Draws the default fields, then a "Blend Shapes" section with a
//  0-100 weight slider per blend shape on the shared mesh.
// ================================================================

[CustomEditor(typeof(SkinnedMeshRenderer))]
public class SkinnedMeshRendererEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var renderer = (SkinnedMeshRenderer)target;
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Default inspector (mesh, materials, bone paths, etc.).
        DrawDefaultInspector(paper, id, renderer);

        EditorGUI.SectionHeader(paper, $"{id}_autoTex", "Textures", compact: true);
        EditorGUI.Note(paper, $"{id}_autoTexNote",
            "Select the texture maps in the Project panel, or keep them beside the mesh, then assign them to a Lit material.");
        EditorGUI.Row(paper, $"{id}_autoTexRow", "", () =>
            Origami.Button(paper, $"{id}_autoTexBtn", $"{EditorIcons.Image}  Auto Assign Textures",
                () => MeshTextureAutoAssign.Assign(renderer)).Width(UnitValue.Stretch()).Show());

        Mesh? mesh = renderer.SharedMesh;
        int count = mesh.IsValid() ? mesh.BlendShapeCount : 0;
        if (mesh.IsNotValid() || count == 0)
            return;

        Origami.Separator(paper, $"{id}_bs_sep").Show();
        Origami.Header(paper, $"{id}_bs_header", $"Blend Shapes ({count})").Underline().Show();

        for (int i = 0; i < count; i++)
        {
            int index = i; // capture for the closure
            string name = mesh.GetBlendShapeName(index);
            if (string.IsNullOrEmpty(name)) name = $"BlendShape {index}";
            float weight = renderer.GetBlendShapeWeight(index);

            EditorGUI.Row(paper, $"{id}_bs_{index}", name, () =>
                Origami.Slider(paper, $"{id}_bs_{index}_v", weight,
                    v =>
                    {
                        Undo.RecordObject(renderer, "Change Blend Shape Weight");
                        renderer.SetBlendShapeWeight(index, v);
                    },
                    0f, 100f).Format("F1").Show());
        }

        Origami.Button(paper, $"{id}_bs_reset", "Reset Weights", () =>
        {
            Undo.RecordObject(renderer, "Reset Blend Shape Weights");
            for (int i = 0; i < count; i++)
                renderer.SetBlendShapeWeight(i, 0f);
        }).Show();
    }
}
