using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 차고 셔터 문. 문 오브젝트(Garage door)에 붙임.
/// - 플레이어(오토바이 또는 도보)가 문 앞뒤 구역에 들어오면 아래에서 위로 올라가며 열리고, 나가면 closeDelay 뒤 닫힘
/// - Roll : 윗변은 고정하고 아래에서 위로 말려 올라감 (셔터). 문이 지붕을 뚫고 나가지 않음
///   Slide : 문 전체가 위로 미끄러져 올라감 (문 위에 숨길 공간이 있을 때)
/// - 구역 크기는 문 메시 크기로 자동 계산. 오토바이는 빠르니까 구역을 더 길게 잡음
/// - 내려오는 중에 다시 들어오면 그 자리에서 다시 올라감
/// 문에 콜라이더가 있으면 같이 움직임 (Roll은 높이가 같이 줄어듦)
/// </summary>
public class GarageDoor : MonoBehaviour
{
    public enum OpenMode { Roll, Slide }

    [Header("Open")]
    [SerializeField] OpenMode mode = OpenMode.Roll;
    [Tooltip("다 열리는 데 걸리는 시간 (초)")]
    [SerializeField] float openTime = 1.6f;
    [Tooltip("다 닫히는 데 걸리는 시간 (초)")]
    [SerializeField] float closeTime = 2.2f;
    [Tooltip("구역에서 나간 뒤 닫히기까지 대기 (초)")]
    [SerializeField] float closeDelay = 1.5f;
    [Tooltip("Roll : 다 열렸을 때 남는 높이 비율 (0.05 = 5%만 남음)")]
    [SerializeField, Range(0.01f, 0.5f)] float rolledRatio = 0.05f;
    [Tooltip("Slide : 올라가는 높이 (m). 0이면 문 높이만큼")]
    [SerializeField] float slideHeight = 0f;

    [Header("Detect")]
    [Tooltip("오토바이 탄 채로 다가오면 열림")]
    [SerializeField] bool openWhenRiding = true;
    [Tooltip("걸어서 다가와도 열림")]
    [SerializeField] bool openOnFoot = true;
    [Tooltip("오토바이일 때 문 앞뒤로 감지하는 거리 (m). 10m/s 기준 열리는 시간만큼 여유")]
    [SerializeField] float ridingDepth = 9f;
    [Tooltip("도보일 때 문 앞뒤로 감지하는 거리 (m)")]
    [SerializeField] float footDepth = 2.5f;
    [Tooltip("문 폭 양옆으로 더 감지하는 여유 (m)")]
    [SerializeField] float sideMargin = 1f;

    [Header("References")]
    [SerializeField] RobotRider rider;                 // 비우면 씬에서 찾음

    [Header("Events (소리, 경고등 등)")]
    public UnityEvent onOpenStart;
    public UnityEvent onCloseStart;

    public bool IsOpen => progress >= 1f;
    public bool IsClosed => progress <= 0f;

    // 닫힌 상태 기록
    Vector3 closedPos, closedScale, topLocal, topWorld;
    int heightAxis;                 // 문 높이 방향 로컬 축 (0 x, 1 y, 2 z)
    Vector3 center, normal, widthDir;
    float halfWidth, doorHeight;

    float progress, target, outTimer;
    bool lastWantOpen;

    void Awake()
    {
        if (rider == null) rider = FindFirstObjectByType<RobotRider>();

        closedPos = transform.position;
        closedScale = transform.localScale;

        // 문 메시의 로컬 크기에서 높이·폭·두께 방향 찾기
        var mf = GetComponent<MeshFilter>();
        Bounds lb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);

        heightAxis = 0; float best = -1f;
        for (int i = 0; i < 3; i++)
        {
            float up = Mathf.Abs(Vector3.Dot(transform.TransformDirection(Axis(i)).normalized, Vector3.up));
            if (up > best) { best = up; heightAxis = i; }
        }
        float upSign = Mathf.Sign(Vector3.Dot(transform.TransformDirection(Axis(heightAxis)), Vector3.up));
        topLocal = lb.center + Axis(heightAxis) * lb.extents[heightAxis] * upSign;
        topWorld = transform.TransformPoint(topLocal);

        // 남은 두 축 중 짧은 쪽 = 두께(문 정면 방향), 긴 쪽 = 폭
        int a = (heightAxis + 1) % 3, b = (heightAxis + 2) % 3;
        float sa = transform.TransformVector(Axis(a) * lb.size[a]).magnitude;
        float sb = transform.TransformVector(Axis(b) * lb.size[b]).magnitude;
        int thinAxis = sa < sb ? a : b, wideAxis = sa < sb ? b : a;

        center = transform.TransformPoint(lb.center);
        normal = Flat(transform.TransformDirection(Axis(thinAxis)));
        widthDir = Flat(transform.TransformDirection(Axis(wideAxis)));
        halfWidth = transform.TransformVector(Axis(wideAxis) * lb.size[wideAxis]).magnitude * 0.5f;
        doorHeight = transform.TransformVector(Axis(heightAxis) * lb.size[heightAxis]).magnitude;
    }

    void Update()
    {
        bool want = PlayerInZone();

        if (want)
        {
            outTimer = 0f;
            target = 1f;
        }
        else
        {
            outTimer += Time.deltaTime;
            if (outTimer >= closeDelay) target = 0f;
        }

        if (target > 0.5f && !lastWantOpen && progress < 1f) onOpenStart?.Invoke();
        if (target < 0.5f && lastWantOpen && progress > 0f) onCloseStart?.Invoke();
        lastWantOpen = target > 0.5f;

        float time = target > progress ? openTime : closeTime;
        progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(0.01f, time));
        Apply(Mathf.SmoothStep(0f, 1f, progress));
    }

    bool PlayerInZone()
    {
        if (rider == null) return false;
        bool riding = rider.IsRiding;
        if (riding ? !openWhenRiding : !openOnFoot) return false;

        Vector3 d = rider.transform.position - center;
        if (Mathf.Abs(d.y) > doorHeight + 2f) return false;               // 위·아래층 무시
        float depth = riding ? ridingDepth : footDepth;
        return Mathf.Abs(Vector3.Dot(d, widthDir)) <= halfWidth + sideMargin
            && Mathf.Abs(Vector3.Dot(d, normal)) <= depth;
    }

    void Apply(float t)
    {
        if (mode == OpenMode.Slide)
        {
            float h = slideHeight > 0f ? slideHeight : doorHeight;
            transform.localScale = closedScale;
            transform.position = closedPos + Vector3.up * h * t;
            return;
        }

        // Roll : 높이 축만 줄이고, 윗변이 제자리에 있도록 위치 보정
        Vector3 s = closedScale;
        s[heightAxis] = closedScale[heightAxis] * Mathf.Lerp(1f, rolledRatio, t);
        transform.localScale = s;
        transform.position = closedPos;
        transform.position += topWorld - transform.TransformPoint(topLocal);
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) Awake();
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.LookRotation(normal, Vector3.up), Vector3.one);
        Vector3 w = new Vector3((halfWidth + sideMargin) * 2f, doorHeight, 0f);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(w.x, w.y, ridingDepth * 2f));
        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(w.x, w.y, footDepth * 2f));
    }
#endif
}
