using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity6.6_Tools 에디터 리소스(아이콘, USS)를 폴더 위치와 상관없이 찾아오는 헬퍼.
/// 1순위: .meta의 GUID로 찾기 / 2순위: 파일 이름으로 프로젝트 전체 검색.
/// </summary>
internal static class ToolsAssetLocator
{
    public static T Load<T>(string guid, string fileName) where T : Object
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
        }

        string searchName = Path.GetFileNameWithoutExtension(fileName);
        foreach (string foundGuid in AssetDatabase.FindAssets(searchName))
        {
            string foundPath = AssetDatabase.GUIDToAssetPath(foundGuid);
            if (Path.GetFileName(foundPath) != fileName)
                continue;

            T asset = AssetDatabase.LoadAssetAtPath<T>(foundPath);
            if (asset != null)
                return asset;
        }

        return null;
    }
}
