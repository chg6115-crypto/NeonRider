using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 높이 기준으로 건물 벽 머티리얼을 자동 배정하는 에디터 창
public class BuildingMaterialAssigner : EditorWindow
{
    Transform buildingsRoot;
    Material baseWall;      // FBX Remap으로 들어간 기본 벽 (Bricks051)
    Material tallWall;      // 고층 건물용 벽 (Facade017)
    float heightThreshold = 15f;

    // 미리보기 결과
    int previewTotal = -1;
    int previewTall;
    float minH, maxH;

    [MenuItem("Tools/NeonRider/Building Material Assigner")]
    static void Open() => GetWindow<BuildingMaterialAssigner>("Building Materials");

    void OnGUI()
    {
        EditorGUILayout.LabelField("대상", EditorStyles.boldLabel);
        buildingsRoot = (Transform)EditorGUILayout.ObjectField("Buildings 루트", buildingsRoot, typeof(Transform), true);
        baseWall = (Material)EditorGUILayout.ObjectField("기본 벽 (교체 대상)", baseWall, typeof(Material), false);
        tallWall = (Material)EditorGUILayout.ObjectField("고층 벽", tallWall, typeof(Material), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("기준", EditorStyles.boldLabel);
        heightThreshold = EditorGUILayout.FloatField("기준 높이 (m)", heightThreshold);

        bool ready = buildingsRoot && baseWall && tallWall;
        using (new EditorGUI.DisabledScope(!ready))
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("미리보기 (개수 확인)")) Preview();

            if (previewTotal >= 0)
            {
                EditorGUILayout.HelpBox(
                    $"전체 {previewTotal}개 / 높이 {minH:0.0}m ~ {maxH:0.0}m\n" +
                    $"기준 {heightThreshold}m 이상 → {previewTall}개가 고층 벽으로 바뀜",
                    MessageType.Info);
            }

            if (GUILayout.Button("적용", GUILayout.Height(30))) Apply();
            if (GUILayout.Button("되돌리기 (고층 벽 → 기본 벽)")) Revert();
        }

        if (!ready)
            EditorGUILayout.HelpBox("Buildings 루트와 머티리얼 2개를 넣어주세요.", MessageType.Warning);
    }

    IEnumerable<MeshRenderer> Renderers() =>
        buildingsRoot.GetComponentsInChildren<MeshRenderer>(true);

    static float HeightOf(MeshRenderer r) => r.bounds.size.y;

    void Preview()
    {
        var list = Renderers().ToList();
        previewTotal = list.Count;
        if (previewTotal == 0) { minH = maxH = 0; previewTall = 0; return; }

        var heights = list.Select(HeightOf).ToList();
        minH = heights.Min();
        maxH = heights.Max();
        previewTall = heights.Count(h => h >= heightThreshold);
    }

    void Apply()
    {
        int changed = 0;
        Undo.SetCurrentGroupName("Assign Tall Building Walls");
        int group = Undo.GetCurrentGroup();

        foreach (var r in Renderers())
        {
            bool tall = HeightOf(r) >= heightThreshold;
            var mats = r.sharedMaterials;
            bool dirty = false;

            for (int i = 0; i < mats.Length; i++)
            {
                // 기준 이상 → 기본 벽을 고층 벽으로
                if (tall && mats[i] == baseWall) { mats[i] = tallWall; dirty = true; }
                // 기준 미만인데 고층 벽이 들어가 있으면 → 기본 벽으로 (기준 높이 바꿔서 다시 적용할 때)
                else if (!tall && mats[i] == tallWall) { mats[i] = baseWall; dirty = true; }
            }

            if (dirty)
            {
                Undo.RecordObject(r, "Assign Wall Material");
                r.sharedMaterials = mats;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                changed++;
            }
        }

        Undo.CollapseUndoOperations(group);
        MarkSceneDirty();
        Debug.Log($"[BuildingMaterialAssigner] {changed}개 건물 머티리얼 변경 (기준 {heightThreshold}m)");
        Preview();
    }

    void Revert()
    {
        int changed = 0;
        Undo.SetCurrentGroupName("Revert Tall Building Walls");
        int group = Undo.GetCurrentGroup();

        foreach (var r in Renderers())
        {
            var mats = r.sharedMaterials;
            bool dirty = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == tallWall) { mats[i] = baseWall; dirty = true; }

            if (dirty)
            {
                Undo.RecordObject(r, "Revert Wall Material");
                r.sharedMaterials = mats;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                changed++;
            }
        }

        Undo.CollapseUndoOperations(group);
        MarkSceneDirty();
        Debug.Log($"[BuildingMaterialAssigner] {changed}개 건물 되돌림");
    }

    void MarkSceneDirty()
    {
        if (buildingsRoot)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(buildingsRoot.gameObject.scene);
    }
}