using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 검증 명령 1단계. Tools > NeonRider > Validate
///
/// 검사 내용
///  1. Missing Script : 스크립트가 지워졌거나 이름이 바뀌어 "(Missing)"으로 남은 컴포넌트   → 에러
///  2. 끊긴 참조      : 연결돼 있던 오브젝트·에셋이 지워진 칸 (Missing)                       → 에러
///  3. 빈 참조        : 내 스크립트(Assets/02_Scripts)의 Inspector 참조 칸이 None              → 경고
///
/// 검사 범위 : 열린 씬 전체 + 프리팹 폴더(03_Prefabs, Prefabs). 외부 애셋 폴더 프리팹은 "All Prefabs" 메뉴에서만
///
/// 비워 둬도 되는 칸 (빈 참조로 보고하지 않음)
///  - 필드 선언 줄 주석이나 바로 위 Tooltip에 "비우면" / "비워도" / "(선택)" / "optional"이 적힌 칸
///  - [OptionalRef] 가 붙은 칸
///
/// Console의 줄을 클릭하면 해당 오브젝트가 선택된다.
/// </summary>
public static class ProjectValidator
{
    const string ScriptsRoot = "Assets/02_Scripts/";
    static readonly string[] PrefabRoots = { "Assets/03_Prefabs", "Assets/Prefabs" };
    static readonly string[] OptionalWords = { "비우면", "비워도", "비워두면", "비워 두면", "(선택)", "optional" };

    class Report
    {
        public int errors, warnings, objects, prefabs;
        public void Error(string msg, UnityEngine.Object ctx) { errors++; Debug.LogError("[Validate] " + msg, ctx); }
        public void Warn(string msg, UnityEngine.Object ctx) { warnings++; Debug.LogWarning("[Validate] " + msg, ctx); }
    }

    [MenuItem("Tools/NeonRider/Validate (Scene + Prefabs)", priority = 200)]
    static void MenuValidate() => Run(includePrefabs: true, allPrefabs: false);

    [MenuItem("Tools/NeonRider/Validate Scene Only", priority = 201)]
    static void MenuValidateScene() => Run(includePrefabs: false, allPrefabs: false);

    [MenuItem("Tools/NeonRider/Validate All Prefabs (외부 애셋 포함, 느림)", priority = 202)]
    static void MenuValidateAll() => Run(includePrefabs: true, allPrefabs: true);

    /// <summary>검사 실행. 에러 수를 돌려줌 (나중에 자동 검증 루프에서 사용)</summary>
    public static int Run(bool includePrefabs, bool allPrefabs)
    {
        var r = new Report();
        optionalCache.Clear();

        // 1) 열린 씬
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                CheckHierarchy(root, scene.name, r);
        }

        // 2) 프리팹
        if (includePrefabs)
        {
            string[] guids = allPrefabs
                ? AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                : AssetDatabase.FindAssets("t:Prefab", PrefabRoots.Where(AssetDatabase.IsValidFolder).ToArray());
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (guids.Length > 50 && EditorUtility.DisplayCancelableProgressBar("Validate", path, (float)i / guids.Length)) break;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                r.prefabs++;
                CheckHierarchy(prefab, path, r);
            }
            EditorUtility.ClearProgressBar();
        }

        string summary = $"[Validate] 오브젝트 {r.objects}개, 프리팹 {r.prefabs}개 검사 → 에러 {r.errors}, 경고 {r.warnings}";
        if (r.errors == 0 && r.warnings == 0) Debug.Log(summary + "  ✔ 통과");
        else if (r.errors == 0) Debug.LogWarning(summary);
        else Debug.LogError(summary);
        return r.errors;
    }

    // ---------------- 오브젝트 검사 ----------------
    static void CheckHierarchy(GameObject root, string where, Report r)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;
            r.objects++;

            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0)
                r.Error($"Missing Script {missing}개 : {Path(go)}  ({where})", go);

            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                CheckReferences(c, where, r);
            }
        }
    }

    static void CheckReferences(Component c, string where, Report r)
    {
        var mb = c as MonoBehaviour;
        bool mine = mb != null && IsMyScript(mb);

        var so = new SerializedObject(c);
        var it = so.GetIterator();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = true;
            if (it.propertyType == SerializedPropertyType.String) { enter = false; continue; }
            if (it.propertyType != SerializedPropertyType.ObjectReference) continue;

            string pathStr = it.propertyPath;
            if (pathStr == "m_Script" || pathStr.Contains("m_PersistentCalls")) continue;
            if (it.objectReferenceValue != null) continue;

            // 끊긴 참조 : 무언가 연결돼 있었는데 지워짐 (모든 컴포넌트 대상)
            if (!it.objectReferenceEntityIdValue.Equals(default(EntityId)))
            {
                r.Error($"끊긴 참조 (Missing) : {Path(c.gameObject)} > {c.GetType().Name}.{Pretty(pathStr)}  ({where})", c);
                continue;
            }

            // 빈 참조 : 내 스크립트만, 비워도 되는 칸은 제외
            if (!mine) continue;
            if (pathStr.StartsWith("m_")) continue;   // Unity 내부 필드
            string field = RootField(pathStr);
            if (IsOptional(mb, field)) continue;
            bool arrayElement = pathStr.Contains(".Array.data[");
            r.Warn($"{(arrayElement ? "목록에 빈 칸" : "빈 참조")} : {Path(c.gameObject)} > {c.GetType().Name}.{Pretty(pathStr)}  ({where})", c);
        }
    }

    static bool IsMyScript(MonoBehaviour mb)
    {
        var ms = MonoScript.FromMonoBehaviour(mb);
        if (ms == null) return false;
        string p = AssetDatabase.GetAssetPath(ms);
        return p.StartsWith(ScriptsRoot);
    }

    // ---------------- 비워도 되는 칸 판단 ----------------
    static readonly Dictionary<(Type, string), bool> optionalCache = new Dictionary<(Type, string), bool>();

    static bool IsOptional(MonoBehaviour mb, string field)
    {
        var key = (mb.GetType(), field);
        if (optionalCache.TryGetValue(key, out bool cached)) return cached;

        bool result = false;
        var fi = FindField(mb.GetType(), field);
        if (fi != null && fi.GetCustomAttribute<OptionalRefAttribute>() != null) result = true;
        if (!result && fi != null)
        {
            var tip = fi.GetCustomAttribute<TooltipAttribute>();
            if (tip != null && HasOptionalWord(tip.tooltip)) result = true;
        }
        if (!result) result = SourceSaysOptional(mb, field);

        optionalCache[key] = result;
        return result;
    }

    static FieldInfo FindField(Type t, string name)
    {
        for (; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }

    // 스크립트 원본에서 필드 선언 줄의 주석 확인 (예: "[SerializeField] RobotRider rider;   // 비우면 씬에서 찾음")
    static readonly Dictionary<MonoScript, string[]> sourceCache = new Dictionary<MonoScript, string[]>();
    static bool SourceSaysOptional(MonoBehaviour mb, string field)
    {
        var ms = MonoScript.FromMonoBehaviour(mb);
        if (ms == null) return false;
        if (!sourceCache.TryGetValue(ms, out var lines)) { lines = ms.text.Split('\n'); sourceCache[ms] = lines; }

        var decl = new Regex(@"\b" + Regex.Escape(field) + @"\s*(=|;|,)");
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            string code = comment >= 0 ? line.Substring(0, comment) : line;
            if (!decl.IsMatch(code) || code.Contains("(") && !code.TrimStart().StartsWith("[")) continue;   // 메서드 안 대입 제외
            if (comment >= 0 && HasOptionalWord(line.Substring(comment))) return true;
            // 바로 위 속성 줄들 ([Tooltip(...)] 등)
            for (int j = i - 1; j >= 0 && lines[j].TrimStart().StartsWith("["); j--)
                if (HasOptionalWord(lines[j])) return true;
        }
        return false;
    }

    static bool HasOptionalWord(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        string low = s.ToLowerInvariant();
        return OptionalWords.Any(w => low.Contains(w));
    }

    // ---------------- 표시 ----------------
    static string RootField(string propertyPath)
    {
        int dot = propertyPath.IndexOf('.');
        return dot < 0 ? propertyPath : propertyPath.Substring(0, dot);
    }

    static string Pretty(string propertyPath) => propertyPath.Replace(".Array.data[", "[");

    static string Path(GameObject go)
    {
        string p = go.name;
        for (var t = go.transform.parent; t != null; t = t.parent) p = t.name + "/" + p;
        return p;
    }
}
