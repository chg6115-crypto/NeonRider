#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 선택한 오브젝트(자식 포함)에서 Missing Script 컴포넌트를 모두 제거
/// 사용법 : Hierarchy에서 오브젝트 선택 → 메뉴 Tools > Remove Missing Scripts
/// </summary>
public static class RemoveMissingScripts
{
    [MenuItem("Tools/Remove Missing Scripts")]
    static void Run()
    {
        int removed = 0, objects = 0;
        foreach (var root in Selection.gameObjects)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (n == 0) continue;
                Undo.RegisterCompleteObjectUndo(t.gameObject, "Remove Missing Scripts");
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                objects++;
            }
        }
        Debug.Log($"[Remove Missing Scripts] 오브젝트 {objects}개에서 Missing Script {removed}개 제거");
    }

    [MenuItem("Tools/Remove Missing Scripts", true)]
    static bool Validate() => Selection.gameObjects.Length > 0;
}
#endif
