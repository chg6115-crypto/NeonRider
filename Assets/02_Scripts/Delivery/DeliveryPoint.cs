using System.Collections.Generic;
using UnityEngine;

public enum DeliveryPointType { Pickup, Dropoff }

/// <summary>
/// 픽업(가게) / 배달(손님) 지점. 영역(BoxCollider, Is Trigger) 안에 로봇이 서 있는지 위치로 판정.
/// - 같은 오브젝트에 BoxCollider 필요 (Is Trigger는 자동으로 켜짐 → 길을 막지 않음)
/// - entrance: 입구 위치(자식 빈 오브젝트). 거리 계산·나중에 길찾기 목적지로 사용
/// - 미니맵 마커: 없으면 자동으로 추가 (픽업 주황 / 배달 청록). 현재 목적지일 때만 보임
/// - 바닥 표시: BoxCollider 바닥 크기에 맞춰 자동 생성. 현재 목적지일 때만 보이고 은은하게 깜빡임
///   (머티리얼은 DeliveryManager의 Zone Material 사용)
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DeliveryPoint : MonoBehaviour
{
    public static readonly List<DeliveryPoint> All = new List<DeliveryPoint>();

    [SerializeField] DeliveryPointType type = DeliveryPointType.Dropoff;
    [Tooltip("픽업 지점일 때 이 가게")]
    [SerializeField] StoreData store;
    [Tooltip("배달 지점 이름 (예: 대전지방법원 공주지원)")]
    [SerializeField] string displayName = "";
    [SerializeField] Transform entrance;

    [Header("Minimap")]
    [Tooltip("MinimapMarker가 없으면 자동으로 추가")]
    [SerializeField] bool autoMarker = true;
    [SerializeField] Color pickupColor = new Color(1f, 0.6f, 0.15f);
    [SerializeField] Color dropoffColor = new Color(0.25f, 0.9f, 1f);

    [Header("Floor Zone")]
    [SerializeField] bool showFloorZone = true;
    [Tooltip("비우면 DeliveryManager의 Zone Material 사용")]
    [SerializeField] Material zoneMaterial;
    [Tooltip("바닥에서 띄우는 높이 (겹침 방지)")]
    [SerializeField] float zoneLift = 0.03f;
    [Range(0f, 1f)] [SerializeField] float zoneAlpha = 0.85f;
    [SerializeField] float pulseSpeed = 3f;

    public DeliveryPointType Type => type;
    public StoreData Store => store;
    public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName
                               : store != null ? store.storeName : name;
    public Vector3 EntrancePosition => entrance ? entrance.position : transform.position;
    public Color ThemeColor => type == DeliveryPointType.Pickup ? pickupColor : dropoffColor;

    BoxCollider area;
    MinimapMarker marker;
    GameObject zone;
    Renderer zoneRenderer;
    MaterialPropertyBlock mpb;
    bool active;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Detection")]
    [Tooltip("박스 위아래로 이만큼(m) 여유를 두고 판정 (발 위치가 바닥보다 살짝 낮거나 높아도 인정)")]
    [SerializeField] float verticalTolerance = 1f;

    /// <summary>
    /// 위치가 영역 안인지. 트리거 이벤트 대신 위치로 판정 (탑승/하차로 콜라이더가 꺼져도 안전).
    /// 가로/세로는 박스 크기 그대로, 높이는 위아래로 verticalTolerance 만큼 여유
    /// </summary>
    public bool Contains(Vector3 worldPos)
    {
        if (area == null || !area.enabled) return false;
        Vector3 p = transform.InverseTransformPoint(worldPos) - area.center;
        Vector3 h = area.size * 0.5f;
        return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.z) <= h.z && Mathf.Abs(p.y) <= h.y + verticalTolerance;
    }

    /// <summary>걸어서(하차 상태로) 영역 안에 서 있는 로봇인지</summary>
    public bool IsOnFootInside(RobotRider r) => r != null && !r.IsRiding && Contains(r.transform.position);

    void Reset()
    {
        var c = GetComponent<BoxCollider>();
        c.isTrigger = true;
        c.size = new Vector3(3f, 2f, 3f);
        c.center = new Vector3(0f, 1f, 0f);
    }

    void Awake()
    {
        area = GetComponent<BoxCollider>();
        area.isTrigger = true;

        marker = GetComponentInChildren<MinimapMarker>(true);
        if (marker == null && autoMarker) marker = gameObject.AddComponent<MinimapMarker>();
        if (marker != null && autoMarker) marker.color = ThemeColor;

        SetBeacon(false);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>현재 주문의 목적지일 때 켬: 미니맵 마커 + 바닥 표시</summary>
    public void SetBeacon(bool on)
    {
        active = on;
        if (marker) marker.visible = on;

        if (on && showFloorZone && zone == null) BuildZone();
        if (zone) zone.SetActive(on && showFloorZone);
    }

    void Update()
    {
        if (!active || zoneRenderer == null) return;
        float a = zoneAlpha * (0.7f + 0.3f * Mathf.Sin(Time.time * pulseSpeed));
        Color c = ThemeColor; c.a = a;
        zoneRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, c);
        mpb.SetColor(ColorId, c);
        zoneRenderer.SetPropertyBlock(mpb);
    }

    // BoxCollider 바닥 크기에 맞는 평평한 판을 만들고, 아래 바닥 높이에 붙임
    void BuildZone()
    {
        Material mat = zoneMaterial;
        if (mat == null && DeliveryManager.Instance != null) mat = DeliveryManager.Instance.ZoneMaterial;
        if (mat == null) { showFloorZone = false; return; }

        zone = new GameObject("FloorZone (auto)");
        zone.transform.SetParent(transform, false);
        zone.layer = gameObject.layer;

        var mf = zone.AddComponent<MeshFilter>();
        mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        zoneRenderer = zone.AddComponent<MeshRenderer>();
        zoneRenderer.sharedMaterial = mat;
        zoneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        zoneRenderer.receiveShadows = false;
        mpb = new MaterialPropertyBlock();

        // 박스 바닥 중심 (로컬) → 아래로 레이를 쏴서 실제 바닥 높이 찾기
        Vector3 bottomLocal = area.center - new Vector3(0f, area.size.y * 0.5f, 0f);
        Vector3 topWorld = transform.TransformPoint(area.center + new Vector3(0f, area.size.y * 0.5f, 0f));
        float groundY = transform.TransformPoint(bottomLocal).y;
        if (Physics.Raycast(topWorld, Vector3.down, out var hit, area.size.y + 3f, ~0, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;

        Vector3 worldPos = transform.TransformPoint(bottomLocal);
        worldPos.y = groundY + zoneLift;
        zone.transform.position = worldPos;
        zone.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 바닥에 눕힘 (앞면이 위)
        zone.transform.localScale = new Vector3(area.size.x, area.size.z, 1f);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = type == DeliveryPointType.Pickup ? new Color(1f, 0.6f, 0.1f, 0.35f) : new Color(0.2f, 0.9f, 1f, 0.35f);
        var b = GetComponent<BoxCollider>();
        if (b)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(EntrancePosition, 0.2f);
    }
#endif
}
