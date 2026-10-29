using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 미니맵 (Screen Space 캔버스용)
/// - 평소: 작은 지도. 플레이어 중심, (기본) 진행 방향이 위로 회전
/// - M키: 큰 지도 토글. 북쪽이 위, 전체 지도 위에 플레이어 위치/방향 표시
/// - MinimapMarker가 붙은 대상은 아이콘으로 표시 (작은 지도에선 범위 밖이면 테두리에 붙음)
/// - MinimapLabel이 붙은 지명은 글자로 표시 (지도가 회전해도 글자는 똑바로)
/// - 오토바이 속도가 빠를수록 작은 지도가 넓게 보임 (줌 아웃)
///
/// 계층 예시
///  MinimapSmall (Mask + Image: 원형 마스크)
///    ├ SmallMap (RawImage)          ← smallMap
///    ├ SmallLabels (빈 RectTransform, 전체 채움)  ← smallLabelLayer
///    ├ SmallMarkers (빈 RectTransform, 전체 채움) ← smallMarkerLayer
///    └ SmallPlayer (Image: 화살표)  ← smallPlayer
///  MinimapSmallRing (Image: 테두리)  ← 마스크 밖, 장식
///  MinimapBig (패널, 처음엔 꺼둠)    ← bigPanel
///    ├ BigMap (RawImage, 정사각형)  ← bigMap
///    │  ├ BigLabels (전체 채움)     ← bigLabelLayer
///    │  ├ BigMarkers (전체 채움)    ← bigMarkerLayer
///    │  └ BigPlayer (Image: 화살표) ← bigPlayer
/// </summary>
public class MinimapUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] MinimapData data;
    [Tooltip("지도 중심이 될 대상. 비우면 RobotRider (탑승 중이면 오토바이를 따라감)")]
    [SerializeField] Transform target;

    [Header("Small Map")]
    [SerializeField] RectTransform smallRoot;            // 마스크가 있는 부모
    [SerializeField] RawImage smallMap;
    [SerializeField] RectTransform smallPlayer;
    [SerializeField] RectTransform smallMarkerLayer;
    [Tooltip("작은 지도에 보이는 범위 (지름, m). Zoom With Speed가 꺼져 있을 때 사용")]
    [SerializeField] float smallViewMeters = 180f;
    [Tooltip("켜면 진행 방향이 항상 위 (내비게이션 방식). 끄면 북쪽이 위")]
    [SerializeField] bool rotateWithHeading = true;
    [SerializeField] float rotateSmooth = 10f;

    [Header("Speed Zoom")]
    [Tooltip("오토바이 속도에 따라 작은 지도 범위를 넓힘")]
    [SerializeField] bool zoomWithSpeed = true;
    [SerializeField] MotorcycleTestDrive bike;             // 비우면 씬에서 찾음
    [Tooltip("멈춰 있거나 걸을 때 보이는 범위 (m)")]
    [SerializeField] float minViewMeters = 150f;
    [Tooltip("최고 속도일 때 보이는 범위 (m)")]
    [SerializeField] float maxViewMeters = 320f;
    [Tooltip("이 속도(km/h)에서 최대로 넓어짐")]
    [SerializeField] float speedForMaxView = 60f;
    [Tooltip("줌이 따라가는 속도 (작을수록 천천히)")]
    [SerializeField] float zoomSmooth = 1.5f;

    [Header("Big Map (M)")]
    [SerializeField] GameObject bigPanel;
    [SerializeField] RawImage bigMap;
    [SerializeField] RectTransform bigPlayer;
    [SerializeField] RectTransform bigMarkerLayer;
    [Tooltip("큰 지도를 열면 작은 지도는 숨김")]
    [SerializeField] bool hideSmallWhenBig = true;

    [Header("Labels")]
    [SerializeField] RectTransform smallLabelLayer;
    [SerializeField] RectTransform bigLabelLayer;
    [Tooltip("한글이 들어있는 TMP 폰트 (예: NotoSansKR SDF, Dynamic)")]
    [SerializeField] TMP_FontAsset labelFont;
    [SerializeField] float smallLabelSize = 15f;
    [SerializeField] float bigLabelSize = 18f;
    [SerializeField] Color labelColor = new Color(0.22f, 0.22f, 0.25f);
    [SerializeField] Color labelOutlineColor = Color.white;
    [Range(0f, 0.5f)][SerializeField] float labelOutline = 0.3f;

    [Header("Markers")]
    [SerializeField] Sprite defaultMarkerIcon;
    [SerializeField] float smallMarkerSize = 22f;
    [SerializeField] float bigMarkerSize = 26f;
    [Tooltip("작은 지도 테두리에 붙일 때 안쪽 여백 (px)")]
    [SerializeField] float edgePadding = 12f;

    float shownYaw;
    float currentView;
    RobotRider rider;
    Material labelMaterial;
    readonly Dictionary<MinimapLabel, TMP_Text> smallLabels = new Dictionary<MinimapLabel, TMP_Text>();
    readonly Dictionary<MinimapLabel, TMP_Text> bigLabels = new Dictionary<MinimapLabel, TMP_Text>();
    readonly Dictionary<MinimapMarker, Image> smallIcons = new Dictionary<MinimapMarker, Image>();
    readonly Dictionary<MinimapMarker, Image> bigIcons = new Dictionary<MinimapMarker, Image>();

    public bool IsBigOpen => bigPanel != null && bigPanel.activeSelf;

    void Awake()
    {
        rider = FindFirstObjectByType<RobotRider>();
        if (target == null && rider) target = rider.transform;
        if (bike == null) bike = FindFirstObjectByType<MotorcycleTestDrive>(FindObjectsInactive.Include);
        currentView = zoomWithSpeed ? minViewMeters : smallViewMeters;
        if (data != null && data.mapTexture != null)
        {
            if (smallMap) smallMap.texture = data.mapTexture;
            if (bigMap) { bigMap.texture = data.mapTexture; bigMap.uvRect = new Rect(0, 0, 1, 1); }
        }
        // 지도가 마스크 안에서 회전해도 모서리가 비지 않도록 √2배 크게
        if (smallMap)
        {
            smallMap.rectTransform.anchorMin = smallMap.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            smallMap.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            smallMap.rectTransform.anchoredPosition = Vector2.zero;
        }
        if (bigPanel) bigPanel.SetActive(false);
    }

    void Update()
    {
        if (ToggleKeyPressed() && bigPanel != null)
        {
            bigPanel.SetActive(!bigPanel.activeSelf);
            if (smallRoot && hideSmallWhenBig) smallRoot.gameObject.SetActive(!bigPanel.activeSelf);
        }
    }

    void LateUpdate()
    {
        if (data == null || target == null) return;

        Vector3 pos = target.position;
        Vector3 fwd = target.forward; fwd.y = 0f;
        float yaw = fwd.sqrMagnitude > 0.0001f ? Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg : 0f; // 북=0, 동=90
        shownYaw = Mathf.LerpAngle(shownYaw, yaw, 1f - Mathf.Exp(-rotateSmooth * Time.deltaTime));

        Vector2 uv = data.WorldToUV(pos);

        // 속도 줌: 탑승 중일 때만 속도 반영
        float targetView = smallViewMeters;
        if (zoomWithSpeed)
        {
            float kmh = (bike != null && rider != null && rider.IsRiding) ? Mathf.Abs(bike.SpeedKmh) : 0f;
            float t = Mathf.Clamp01(kmh / Mathf.Max(1f, speedForMaxView));
            targetView = Mathf.Lerp(minViewMeters, maxViewMeters, t * t * (3f - 2f * t)); // 부드러운 곡선
        }
        currentView = Mathf.Lerp(currentView, targetView, 1f - Mathf.Exp(-zoomSmooth * Time.deltaTime));

        if (smallRoot && smallRoot.gameObject.activeInHierarchy) UpdateSmall(uv);
        if (IsBigOpen) UpdateBig(uv);
    }

    // ---------------- 작은 지도 ----------------
    void UpdateSmall(Vector2 uv)
    {
        float view = Mathf.Max(10f, currentView);
        float side = Mathf.Min(smallRoot.rect.width, smallRoot.rect.height);
        const float over = 1.42f;                                 // 회전 여유 (√2)
        float uvSpan = view * over / data.worldSize;

        if (smallMap)
        {
            smallMap.rectTransform.sizeDelta = new Vector2(side * over, side * over);
            smallMap.uvRect = new Rect(uv.x - uvSpan * 0.5f, uv.y - uvSpan * 0.5f, uvSpan, uvSpan);
            smallMap.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotateWithHeading ? shownYaw : 0f);
        }
        if (smallPlayer)
        {
            smallPlayer.anchoredPosition = Vector2.zero;
            smallPlayer.localEulerAngles = new Vector3(0f, 0f, rotateWithHeading ? 0f : -shownYaw);
        }

        // 마커/지명: 플레이어 기준 상대 위치(m) → px
        float pxPerMeter = side / view;
        float radius = side * 0.5f - edgePadding;

        // 지명: 위치만 지도와 같이 돌고, 글자는 항상 똑바로. 원 밖이면 숨김
        SyncLabels(smallLabels, smallLabelLayer, smallLabelSize);
        foreach (var kv in smallLabels)
        {
            var l = kv.Key; var txt = kv.Value;
            if (l == null) continue;
            Vector3 d = l.transform.position - target.position;
            Vector2 p = new Vector2(d.x, d.z) * pxPerMeter;
            if (rotateWithHeading) p = Rotate(p, shownYaw);
            bool tooFar = l.hideWhenViewOver > 0f && view > l.hideWhenViewOver;
            txt.enabled = l.showOnSmall && !tooFar && p.magnitude <= radius - 10f;
            txt.rectTransform.anchoredPosition = p;
            txt.rectTransform.localEulerAngles = Vector3.zero;
        }
        SyncIcons(smallIcons, smallMarkerLayer, smallMarkerSize);
        foreach (var kv in smallIcons)
        {
            var m = kv.Key; var img = kv.Value;
            if (m == null) continue;
            Vector3 d = m.transform.position - target.position;
            Vector2 p = new Vector2(d.x, d.z) * pxPerMeter;
            if (rotateWithHeading) p = Rotate(p, shownYaw);   // 지도와 같이 회전
            bool inside = p.magnitude <= radius;
            if (!inside && m.clampToEdge) p = p.normalized * radius;
            img.enabled = m.visible && (inside || m.clampToEdge);
            img.rectTransform.anchoredPosition = p;
        }
    }

    // ---------------- 큰 지도 ----------------
    void UpdateBig(Vector2 uv)
    {
        if (bigMap == null) return;
        Rect r = bigMap.rectTransform.rect;
        if (bigPlayer)
        {
            bigPlayer.anchorMin = bigPlayer.anchorMax = Vector2.zero;
            bigPlayer.anchoredPosition = new Vector2(uv.x * r.width, uv.y * r.height);
            bigPlayer.localEulerAngles = new Vector3(0f, 0f, -shownYaw);
        }
        SyncLabels(bigLabels, bigLabelLayer, bigLabelSize);
        foreach (var kv in bigLabels)
        {
            var l = kv.Key; var txt = kv.Value;
            if (l == null) continue;
            Vector2 luv = data.WorldToUV(l.transform.position);
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = Vector2.zero;
            txt.rectTransform.anchoredPosition = new Vector2(luv.x * r.width, luv.y * r.height);
            txt.enabled = l.showOnBig;
        }

        SyncIcons(bigIcons, bigMarkerLayer, bigMarkerSize);
        foreach (var kv in bigIcons)
        {
            var m = kv.Key; var img = kv.Value;
            if (m == null) continue;
            Vector2 muv = data.WorldToUV(m.transform.position);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
            img.rectTransform.anchoredPosition = new Vector2(muv.x * r.width, muv.y * r.height);
            img.enabled = m.visible;
        }
    }

    // MinimapMarker.All 과 아이콘 목록을 맞춤
    void SyncIcons(Dictionary<MinimapMarker, Image> icons, RectTransform layer, float size)
    {
        if (layer == null) return;
        foreach (var m in MinimapMarker.All)
        {
            if (icons.ContainsKey(m)) continue;
            var go = new GameObject("Marker_" + m.name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(layer, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = m.icon != null ? m.icon : defaultMarkerIcon;
            img.color = m.color;
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            icons[m] = img;
        }
        // 사라진 마커 정리
        List<MinimapMarker> dead = null;
        foreach (var kv in icons)
            if (kv.Key == null || !MinimapMarker.All.Contains(kv.Key)) { if (dead == null) dead = new List<MinimapMarker>(); dead.Add(kv.Key); }
        if (dead != null)
            foreach (var m in dead) { if (icons[m]) Destroy(icons[m].gameObject); icons.Remove(m); }
    }

    // MinimapLabel.All 과 글자 목록을 맞춤
    void SyncLabels(Dictionary<MinimapLabel, TMP_Text> labels, RectTransform layer, float size)
    {
        if (layer == null) return;
        foreach (var l in MinimapLabel.All)
        {
            if (labels.ContainsKey(l)) continue;
            var go = new GameObject("Label_" + l.name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(layer, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (labelFont != null) t.font = labelFont;
            t.text = l.text;
            t.fontSize = size * Mathf.Max(0.1f, l.sizeScale);
            t.fontStyle = FontStyles.Bold;
            t.color = labelColor;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.rectTransform.sizeDelta = new Vector2(200f, 60f);
            // 외곽선은 공용 머티리얼 하나로 (글자마다 머티리얼이 늘어나지 않게)
            if (labelMaterial == null)
            {
                labelMaterial = new Material(t.fontSharedMaterial);
                labelMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
                labelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, labelOutlineColor);
                labelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, labelOutline);
            }
            t.fontSharedMaterial = labelMaterial;
            labels[l] = t;
        }
        List<MinimapLabel> dead = null;
        foreach (var kv in labels)
            if (kv.Key == null || !MinimapLabel.All.Contains(kv.Key)) { if (dead == null) dead = new List<MinimapLabel>(); dead.Add(kv.Key); }
        if (dead != null)
            foreach (var l in dead) { if (labels[l]) Destroy(labels[l].gameObject); labels.Remove(l); }
    }

    void OnDestroy()
    {
        if (labelMaterial != null) Destroy(labelMaterial);
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    static bool ToggleKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame;
#else
        return false;
#endif
    }
}
