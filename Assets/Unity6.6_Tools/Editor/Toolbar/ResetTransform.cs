using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[EditorToolbarElement("PrimTools/ResetTransform")]
public class ResetTransformButton : EditorToolbarButton
{
    public ResetTransformButton()
    {
        icon = ToolsAssetLocator.Load<Texture2D>("20fb051e3031e4049a013f4d51263c59", "reset.png");
        tooltip = "Reset Transform of Selected Object(s)";

        clicked += () =>
        {
            if (Selection.transforms == null || Selection.transforms.Length == 0)
            {
                Debug.LogWarning("No object selected.");
                return;
            }

            foreach (Transform t in Selection.transforms)
            {
                Undo.RecordObject(t, "Reset Transform");
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
            }

            Debug.Log($"Reset transform for {Selection.transforms.Length} object(s).");
        };
    }
}

