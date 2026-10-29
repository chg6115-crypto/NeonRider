using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 맵을 위에서 내려다본 지도 이미지로 구워서 MinimapData를 만드는 에디터 창.
/// Tools → NeonRider → Minimap Baker
/// - 직교(Orthographic) 카메라를 맵 위에 잠깐 만들어 찍고 PNG로 저장 후 삭제
/// - 플레이어/오토바이 등은 Hide While Capture에 넣으면 찍히지 않음
/// </summary>
public class MinimapBaker : EditorWindow
{
    Transform mapRoot;
    float margin = 20f;
    int resolution = 4096;
    LayerMask cullingMask = ~0;
    Color background = new Color(0.93f, 0.91f, 0.87f);
    bool disableFog = true;
    bool includeShadows = true;
    [SerializeField] GameObject[] hideWhileCapture = new GameObject[0];
    string folder = "Assets/07_UI/Minimap";
    string fileName = "Minimap_Gongju";

    SerializedObject so;

    [MenuItem("Tools/NeonRider/Minimap Baker")]
    static void Open() => GetWindow<MinimapBaker>("Minimap Baker");

    void OnGUI()
    {
        EditorGUILayout.LabelField("범위", EditorStyles.boldLabel);
        mapRoot = (Transform)EditorGUILayout.ObjectField("Map Root (예: Map_Gongju)", mapRoot, typeof(Transform), true);
        margin = EditorGUILayout.FloatField("여백 (m)", margin);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("촬영", EditorStyles.boldLabel);
        resolution = EditorGUILayout.IntPopup("해상도", resolution, new[] { "2048", "4096", "8192" }, new[] { 2048, 4096, 8192 });
        cullingMask = LayerMaskField("찍을 레이어", cullingMask);
        background = EditorGUILayout.ColorField("배경색", background);
        disableFog = EditorGUILayout.Toggle("촬영 중 안개 끄기", disableFog);
        includeShadows = EditorGUILayout.Toggle("그림자 포함", includeShadows);

        if (so == null) so = new SerializedObject(this);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty(nameof(hideWhileCapture)), new GUIContent("촬영 중 숨길 오브젝트"), true);
        so.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("저장", EditorStyles.boldLabel);
        folder = EditorGUILayout.TextField("폴더", folder);
        fileName = EditorGUILayout.TextField("파일 이름", fileName);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(mapRoot == null))
        {
            if (GUILayout.Button("지도 굽기", GUILayout.Height(32))) Bake();
        }
        if (mapRoot == null) EditorGUILayout.HelpBox("Map Root에 맵 최상위 오브젝트를 넣어 주세요.", MessageType.Info);
    }

    void Bake()
    {
        // 1) 범위 계산 (XZ 정사각형)
        var renderers = mapRoot.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) { Debug.LogError("[MinimapBaker] Renderer가 없습니다."); return; }
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        float size = Mathf.Max(b.size.x, b.size.z) + margin * 2f;
        Vector2 min = new Vector2(b.center.x - size * 0.5f, b.center.z - size * 0.5f);

        // 2) 임시 카메라 (위에서 아래로, 이미지 위쪽 = +Z)
        var camGO = new GameObject("~MinimapBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = size * 0.5f;
        cam.aspect = 1f;
        cam.transform.position = new Vector3(b.center.x, b.max.y + 50f, b.center.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = b.size.y + 200f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        cam.cullingMask = cullingMask;
        cam.enabled = false;

        var rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;

        // 3) 촬영 환경 임시 변경
        bool fog = RenderSettings.fog;
        float shadowDist = QualitySettings.shadowDistance;
        var hidden = new System.Collections.Generic.List<GameObject>();
        foreach (var go in hideWhileCapture) if (go && go.activeSelf) { go.SetActive(false); hidden.Add(go); }
        if (disableFog) RenderSettings.fog = false;

        try
        {
            if (includeShadows) QualitySettings.shadowDistance = Mathf.Max(shadowDist, size * 1.5f);

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();

            // 4) PNG 저장
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            if (!AssetDatabase.IsValidFolder(folder)) CreateFolders(folder);
            string pngPath = $"{folder}/{fileName}.png";
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            DestroyImmediate(tex);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            imp.textureType = TextureImporterType.Default;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = resolution;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();

            // 5) MinimapData 저장/갱신
            string dataPath = $"{folder}/{fileName}_Data.asset";
            var data = AssetDatabase.LoadAssetAtPath<MinimapData>(dataPath);
            if (data == null) { data = CreateInstance<MinimapData>(); AssetDatabase.CreateAsset(data, dataPath); }
            data.mapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            data.worldMin = min;
            data.worldSize = size;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MinimapBaker] 완료: {pngPath} ({size:0}m × {size:0}m, {resolution}px → {size / resolution:0.00}m/px)");
            EditorGUIUtility.PingObject(data);
        }
        finally
        {
            RenderSettings.fog = fog;
            QualitySettings.shadowDistance = shadowDist;
            foreach (var go in hidden) if (go) go.SetActive(true);
            cam.targetTexture = null;
            rt.Release();
            DestroyImmediate(rt);
            DestroyImmediate(camGO);
        }
    }

    static void CreateFolders(string path)
    {
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static LayerMask LayerMaskField(string label, LayerMask mask)
    {
        var names = new System.Collections.Generic.List<string>();
        var layers = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 32; i++)
        {
            string n = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(n)) { names.Add(n); layers.Add(i); }
        }
        int compact = 0;
        for (int i = 0; i < layers.Count; i++) if ((mask.value & (1 << layers[i])) != 0) compact |= 1 << i;
        compact = EditorGUILayout.MaskField(label, compact, names.ToArray());
        int full = 0;
        for (int i = 0; i < layers.Count; i++) if ((compact & (1 << i)) != 0) full |= 1 << layers[i];
        if (compact == -1) full = ~0;
        mask.value = full;
        return mask;
    }
}
