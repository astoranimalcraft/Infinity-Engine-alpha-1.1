// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.Projects;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Runtime;

namespace Infinity.Editor.Inspector;

/// <summary>
/// Mesh renderer inspector with a one-click texture setup. The Project selection is the texture set; when nothing is
/// selected the mesh's own folder is scanned, so an imported model and its maps become a lit material in one click.
/// </summary>
[CustomEditor(typeof(MeshRenderer))]
public class MeshRendererEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var renderer = (MeshRenderer)target;
        DrawDefaultInspector(paper, id, renderer);

        EditorGUI.SectionHeader(paper, $"{id}_autoTex", "Textures", compact: true);
        EditorGUI.Note(paper, $"{id}_autoTexNote",
            "Select the texture maps in the Project panel, or keep them beside the mesh, then assign them to a Lit material.");
        EditorGUI.Row(paper, $"{id}_autoTexRow", "", () =>
            Origami.Button(paper, $"{id}_autoTexBtn", $"{EditorIcons.Image}  Auto Assign Textures",
                () => MeshTextureAutoAssign.Assign(renderer)).Width(UnitValue.Stretch()).Show());
    }
}
