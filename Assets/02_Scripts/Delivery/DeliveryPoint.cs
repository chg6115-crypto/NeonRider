using System.Collections.Generic;
using UnityEngine;

public enum DeliveryPointType { Pickup, Dropoff }

/// <summary>
/// 픽업(가게) / 배달(손님) 지점. 영역(BoxCollider, Is Trigger) 안에 로봇이 서 있는지 위치로 판정.
/// - 같은 오브젝트에 BoxCollider 필요 (Is Trigger는 자동으로 켜짐 → 길을 막지 않음)
/// - entrance: 입구 위치(자식 빈 오브젝트). 거리 계산·나중에 길찾기 목적지로 사용
/// - beacon: 목적지일 때 켜지는 표시(빛 기둥 등). 비워도 됨
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DeliveryPoint : MonoBehaviour
{
    public static readonly List<DeliveryPoint> All = new List<DeliveryPoint>();

    [SerializeField] DeliveryPointType type = DeliveryPointType.Dropoff;
    [Tooltip("픽업 지점일 때 이 가게")]
    [SerializeField] StoreData store;
    [Tooltip("배달 지점 이름 (예: 신관동 OO빌라 201호)")]
    [SerializeField] string displayName = "";
    [SerializeField] Transform entrance;
    [SerializeField] GameObject beacon;

    public DeliveryPointType Type => type;
    public StoreData Store => store;
    public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName
                               : store != null ? store.storeName : name;
    public Vector3 EntrancePosition => entrance ? entrance.position : transform.position;

    Collider area;

    /// <summary>위치가 영역 안인지 (트리거 이벤트 대신 위치로 판정 → 탑승/하차로 콜라이더가 꺼져도 안전)</summary>
    public bool Contains(Vector3 worldPos)
    {
        if (area == null || !area.enabled) return false;
        return (area.ClosestPoint(worldPos) - worldPos).sqrMagnitude < 0.0001f;
    }

    /// <summary>걸어서(하차 상태로) 영역 안에 서 있는 로봇인지</summary>
    public bool IsOnFootInside(RobotRider r) => r != null && !r.IsRiding && Contains(r.transform.position);

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c) c.isTrigger = true;
    }

    void Awake()
    {
        area = GetComponent<Collider>();
        area.isTrigger = true;
        SetBeacon(false);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void SetBeacon(bool on)
    {
        if (beacon) beacon.SetActive(on);
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
