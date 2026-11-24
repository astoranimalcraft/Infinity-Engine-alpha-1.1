// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Infinity.Editor.Core;
using Infinity.Editor.GUI.Popups;
using Infinity.Editor.GUI.SceneView;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.Runtime;
using Infinity.Vector;

namespace Infinity.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for Transform fields. Shows a reference picker that lets
/// users select a Transform from a GameObject in the current scene.
/// </summary>
[CustomPropertyEditor(typeof(Transform))]
public class TransformPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        GameObject? go = (value as Transform)?.GameObject;
        bool hasValue = go.IsValid();
        OpenScene? holder = EditorSceneManager.DrawingScene;
        IReadOnlyList<OpenScene> holders = EditorSceneManager.DrawingScenes;
        Action<object?> pick = picked =>
        {
            if (!EngineObjectPropertyEditor.RefusedFor(holders, picked)) onChange(picked);
        };

        PropertyGridUtils.ObjectField(paper, id, label,
            hasValue ? EditorIcons.ArrowsUpDownLeftRight : EditorIcons.Circle,
            hasValue ? EditorTheme.Purple400 : EditorTheme.Ink300,
            hasValue ? $"{go!.Name} (Transform)" : "None (Transform)",
            hasValue,
            isDragTarget: DragDrop.IsDragging && DragDrop.Payload is GameObjectDragPayload,
            onClick: () => { if (go.IsValid()) Selection.Ping(go!.Identifier); },
            onDoubleClick: () =>
            {
                if (go.IsValid()) Selection.Select(go!);
                else OpenSelector(pick, holder);
            },
            onPick: () => OpenSelector(pick, holder),
            acceptDrops: () =>
            {
                if (DragDrop.IsDragging || !paper.IsParentHovered || DragDrop.Payload is not GameObjectDragPayload drop) return;
                if (drop.GameObjects.Length > 0 && EditorSceneManager.RefusesReference(holders, drop.GameObjects[0], out string why))
                    Toasts.Warning(Rosetta.Loc.Get("hierarchy.reference_refused"), why);
                else if (drop.GameObjects.Length > 0) onChange(drop.GameObjects[0].Transform);
                DragDrop.EndDrag();
            });
    }

    private static void OpenSelector(Action<object?> onChange, OpenScene? holder)
        => SelectorModal.Open("Select Transform", typeof(Transform), SelectorTabs.Scene, onChange, holder);
}
