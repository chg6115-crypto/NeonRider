using UnityEditor;
using UnityEngine;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "Transform", true)]
public class TransformOverlay : Overlay
{
    private Vector3 copiedPosition;
    private Vector3 copiedRotation;
    private Vector3 copiedScale;

    public override VisualElement CreatePanelContent()
    {
        var container = new VisualElement();
        container.style.paddingTop = 4;
        container.style.paddingBottom = 4;
        container.style.paddingLeft = 4;
        container.style.paddingRight = 4;
        container.style.width = 200;

        var positionField = new Vector3Field("P");
        var rotationField = new Vector3Field("R");
        var scaleField = new Vector3Field("S");

        positionField.labelElement.style.minWidth = 20;
        rotationField.labelElement.style.minWidth = 20;
        scaleField.labelElement.style.minWidth = 20;

        positionField.RegisterValueChangedCallback(evt => {
            ApplyToSelected(t => t.position = evt.newValue);
        });
        rotationField.RegisterValueChangedCallback(evt => {
            ApplyToSelected(t => t.eulerAngles = evt.newValue);
        });
        scaleField.RegisterValueChangedCallback(evt => {
            ApplyToSelected(t => t.localScale = evt.newValue);
        });

        var buttonContainer = new VisualElement();
        buttonContainer.style.flexDirection = FlexDirection.Row;
        buttonContainer.style.marginTop = 4;

        var resetButton = new Button(() => {
            ApplyToSelected(t => {
                t.position = Vector3.zero;
                t.eulerAngles = Vector3.zero;
                t.localScale = Vector3.one;
            });
            UpdateFields();
        })
        { text = "Reset" };

        var copyButton = new Button(() => {
            if (Selection.activeTransform != null)
            {
                copiedPosition = Selection.activeTransform.position;
                copiedRotation = Selection.activeTransform.eulerAngles;
                copiedScale = Selection.activeTransform.localScale;
                Debug.Log("Transform 복사 완료");
            }
        })
        { text = "Copy" };

        var pasteButton = new Button(() => {
            ApplyToSelected(t => {
                t.position = copiedPosition;
                t.eulerAngles = copiedRotation;
                t.localScale = copiedScale;
            });
            UpdateFields();
        })
        { text = "Paste" };

        buttonContainer.Add(copyButton);
        buttonContainer.Add(pasteButton);
        buttonContainer.Add(resetButton);

        container.Add(positionField);
        container.Add(rotationField);
        container.Add(scaleField);
        container.Add(CreateMinimalRotationControls(rotationField));
        container.Add(buttonContainer);

        void UpdateFields()
        {
            if (Selection.activeTransform != null)
            {
                positionField.SetValueWithoutNotify(Selection.activeTransform.position);
                rotationField.SetValueWithoutNotify(Selection.activeTransform.eulerAngles);
                scaleField.SetValueWithoutNotify(Selection.activeTransform.localScale);
            }
        }

        Selection.selectionChanged += UpdateFields;
        EditorApplication.update += UpdateFields;
        UpdateFields();

        return container;
    }

    private void ApplyToSelected(System.Action<Transform> action)
    {
        if (Selection.activeTransform != null)
        {
            Undo.RecordObject(Selection.activeTransform, "Transform Change");
            action(Selection.activeTransform);
        }
    }

    private VisualElement CreateMinimalRotationControls(Vector3Field rotationField)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        container.style.marginTop = 4;

        string[] axes = { "X", "Y", "Z" };

        for (int i = 0; i < 3; i++)
        {
            int axisIndex = i;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            var label = new Label($"R {axes[i]}");
            label.style.minWidth = 30;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);

            var minusBtn = new Button();
            minusBtn.text = "↺";
            minusBtn.tooltip = $"{axes[i]} 축 -90도";
            minusBtn.style.marginRight = 4;
            minusBtn.style.backgroundColor = new Color(1f, 0.4f, 0.4f);

            minusBtn.clicked += () =>
            {
                AnimateButton(minusBtn);
                ApplyToSelected(t =>
                {
                    Vector3 rot = t.eulerAngles;
                    rot[axisIndex] -= 90;
                    t.eulerAngles = rot;
                    rotationField.SetValueWithoutNotify(t.eulerAngles);
                });
            };

            var plusBtn = new Button();
            plusBtn.text = "↻";
            plusBtn.tooltip = $"{axes[i]} 축 +90도";
            plusBtn.style.backgroundColor = new Color(0.3f, 0.6f, 1f);

            plusBtn.clicked += () =>
            {
                AnimateButton(plusBtn);
                ApplyToSelected(t =>
                {
                    Vector3 rot = t.eulerAngles;
                    rot[axisIndex] += 90;
                    t.eulerAngles = rot;
                    rotationField.SetValueWithoutNotify(t.eulerAngles);
                });
            };

            row.Add(minusBtn);
            row.Add(plusBtn);
            container.Add(row);
        }

        return container;
    }

    private void AnimateButton(Button button)
    {
        var scaleUp = new StyleScale(new Scale(new Vector3(1.2f, 1.2f, 1)));
        var scaleNormal = new StyleScale(new Scale(Vector3.one));

        button.style.scale = scaleUp;
        EditorApplication.delayCall += () => button.style.scale = scaleNormal;
    }
}

[EditorToolbarElement(ID)]
public class OpenTransformOverlayButton : EditorToolbarButton
{
    public const string ID = "CustomTools/TransformOverlayButton";

    public OpenTransformOverlayButton()
    {
        text = "Transform";
        tooltip = "씬 뷰에서 Transform Overlay를 활성화하세요";
        icon = (Texture2D)EditorGUIUtility.IconContent("d_Transform Icon").image;
        clicked += () => Debug.Log("👆 상단 [+] 버튼에서 'Transform' 오버레이를 켜세요.");
    }
}
