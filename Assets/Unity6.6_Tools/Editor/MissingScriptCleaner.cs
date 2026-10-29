using UnityEditor;
using UnityEngine;

public static class MissingScriptCleaner
{
    [MenuItem("Tools/URP FPS/Clean Missing Scripts")]
    public static void CleanAll()
    {
        int removedCount = 0;

        // 1) Scene objects
        GameObject[] sceneObjects = Object.FindObjectsOfType<GameObject>(true);
        foreach (GameObject go in sceneObjects)
        {
            removedCount += RemoveMissingFromGameObject(go);
        }

        // 2) Prefab assets
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            int removedInPrefab = RemoveMissingFromGameObject(root);
            if (removedInPrefab > 0)
            {
                removedCount += removedInPrefab;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }

            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[MSC] Removed {removedCount} missing scripts.");
    }

    private static int RemoveMissingFromGameObject(GameObject go)
    {
        int count = 0;

        Component[] components = go.GetComponents<Component>();
        for (int i = components.Length - 1; i >= 0; i--)
        {
            if (components[i] == null)
            {
                Undo.RegisterCompleteObjectUndo(go, "Clean Missing Scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                count++;
                break; // RemoveMonoBehavioursWithMissingScript removes all missing on this GO
            }
        }

        foreach (Transform child in go.transform)
        {
            count += RemoveMissingFromGameObject(child.gameObject);
        }

        return count;
    }
}
