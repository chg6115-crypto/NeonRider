#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "URP Converter", true)]
public class URPConverterOverlay : Overlay
{
    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Row;
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;

        var convertButton = new Button(() =>
        {
            bool success = EditorApplication.ExecuteMenuItem("Edit/Rendering/Materials/Convert Selected Built-in Materials to URP");
            if (!success)
            {
                Debug.LogWarning("메뉴 실행 실패: 'Convert Selected Built-in Materials to URP'");
            }
        })
        {
            text = "Convert to URP",
            tooltip = "Convert selected materials to URP"
        };

        convertButton.style.paddingLeft = 6;
        convertButton.style.paddingRight = 6;
        convertButton.style.marginRight = 6;
        convertButton.style.backgroundColor = new Color(0.2f, 0.6f, 1f);
        convertButton.style.color = Color.white;

        root.Add(convertButton);

        return root;
    }
}
#endif

