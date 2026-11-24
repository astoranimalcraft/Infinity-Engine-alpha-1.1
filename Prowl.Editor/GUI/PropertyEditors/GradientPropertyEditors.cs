using System;

using Infinity.Editor.GUI;
using Infinity.Editor.GUI.Widgets;
using Infinity.PaperUI;

using Gradient = Infinity.Vector.Gradient;
namespace Infinity.Editor.Inspector;

// ================================================================
//  Gradient Property Editor
// ================================================================

[CustomPropertyEditor(typeof(Gradient))]
public class GradientPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        Gradient gradient = value as Gradient ?? new Gradient();

        EditorGUI.Row(paper, id, label, () =>
            GradientField.Create(paper, $"{id}_gf", gradient,
                v => onChange(v)).Show());
    }
}
