using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 로봇 루트에 붙임. F키로 오토바이 탑승/하차.
/// 탑승 중: 시트에 고정 + 걷기 스크립트 비활성 + 라이딩 Animator Controller로 교체
///          + 오토바이 기울기에 맞춰 Turn 파라미터 갱신
/// 오토바이(부모)가 스케일되어 있어도 로봇의 실제 크기는 그대로 유지
/// - 탑승 : 로봇과 시트 사이에 벽이 있으면 탈 수 없음
/// - 하차 : 왼쪽 → 오른쪽 → 뒤 순서로 공간·바닥을 검사해 내릴 수 있는 곳에 내림. 다 막혀 있으면 하차 안 됨
/// </summary>
[DefaultExecutionOrder(100)]
public class RobotRider : MonoBehaviour
{
    [Header("Motorcycle")]
    [SerializeField] MotorcycleTestDrive bike;           // 오토바이 루트의 MotorcycleTestDrive
    [SerializeField] Transform seatPoint;                // 오토바이 안의 시트 위치(빈 오브젝트)
    [SerializeField] float mountDistance = 2.5f;         // 이 거리 안에서 F로 탑승
    [SerializeField] float maxDismountSpeed = 1f;        // 이 속도(m/s) 이하일 때만 하차

    [Header("Animation")]
    [SerializeField] Animator animator;                  // 비워두면 자동으로 찾음
    [SerializeField] RuntimeAnimatorController rideController; // Robot_Ride_AC
    [SerializeField] float leanForFullTurn = 35f;        // 오토바이가 이만큼 기울면 Turn = ±1
    [SerializeField] Animator bikeAnimator;
    [SerializeField] bool useSteerInput = true;
    [SerializeField] float turnSmooth = 8f;
    [SerializeField] bool invertTurn = false;

    [Header("Disable While Riding")]
    [Tooltip("탑승 중 끌 컴포넌트 (예: vThirdPersonInput, vThirdPersonController)")]
    [SerializeField] Behaviour[] disableWhileRiding;
    [Tooltip("탑승 중 끌 오브젝트 (예: Invector 걷기 카메라)")]
    [SerializeField] GameObject[] deactivateWhileRiding;
    [Tooltip("탑승 중 켤 오브젝트 (예: MotorcycleFollowCamera가 붙은 카메라)")]
    [SerializeField] GameObject[] activateWhileRiding;

    [Header("Dismount")]
    [SerializeField] Vector3 dismountOffset = new Vector3(-1.0f, 0f, 0f); // 오토바이 기준 왼쪽 (오토바이 로컬 단위)
    [Tooltip("왼쪽이 막혔을 때 뒤로 내리는 위치 (오토바이 로컬 단위). 오른쪽은 왼쪽 위치를 좌우 반전해서 사용")]
    [SerializeField] Vector3 dismountBackOffset = new Vector3(0f, 0f, -1.2f);
    [Tooltip("하차 위치·탑승 경로 검사에 쓸 레이어 (벽, 건물, 지형 등)")]
    [SerializeField] LayerMask obstacleMask = ~0;
    [Tooltip("바닥 찾기 : 하차 위치 위 이 높이에서 아래로 검사 (m)")]
    [SerializeField] float groundProbeUp = 1.5f;
    [Tooltip("바닥 찾기 : 아래로 검사하는 거리 (m)")]
    [SerializeField] float groundProbeDown = 3f;
    [Tooltip("내릴 자리 바닥 높이가 오토바이 바닥과 이만큼 넘게 차이나면 그 자리는 안 씀 (m)")]
    [SerializeField] float maxDismountHeightDiff = 0.8f;
    [SerializeField] bool debugDismount = false;

    /// <summary>하차하려 했지만 내릴 곳이 없을 때 / 너무 빨라서 못 내릴 때 (나중에 UI 안내용)</summary>
    public event System.Action<string> DismountBlocked;

    public bool IsRiding { get; private set; }

    static readonly int TurnId = Animator.StringToHash("Turn");

    RuntimeAnimatorController walkController;
    Rigidbody rb;
    Collider[] colliders;
    float turn;

    Transform originalParent;
    Vector3 originalLocalScale;

    // 로봇 캡슐 크기 (월드 기준, 하차 공간 검사용)
    float capsuleRadius = 0.35f, capsuleHeight = 1.8f;
    readonly RaycastHit[] hitBuf = new RaycastHit[16];
    readonly Collider[] overlapBuf = new Collider[16];

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();

        var cap = GetComponent<CapsuleCollider>();
        if (cap != null)
        {
            Vector3 ls = transform.lossyScale;
            capsuleRadius = cap.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
            capsuleHeight = Mathf.Max(cap.height * Mathf.Abs(ls.y), capsuleRadius * 2f);
        }
    }

    void Start()
    {
        if (bike != null) bike.enabled = false;   // 걷는 동안 WASD로 오토바이가 움직이지 않게
        SetActiveAll(activateWhileRiding, false);
    }

    void Update()
    {
        if (MountKeyPressed())
        {
            if (!IsRiding && CanMount()) Mount();
            else if (IsRiding)
            {
                if (Mathf.Abs(bike.Speed) > maxDismountSpeed) Blocked("속도를 줄여야 내릴 수 있어요");
                else if (FindDismountSpot(out var spot)) Dismount(spot);
                else Blocked("내릴 공간이 없어요");
            }
        }

        if (IsRiding && animator != null)
        {
            float target = useSteerInput ? bike.SteerInput : Mathf.Clamp(bike.LeanAngle / Mathf.Max(0.001f, leanForFullTurn), -1f, 1f);
            if (invertTurn) target = -target;
            turn = Mathf.Lerp(turn, target, 1f - Mathf.Exp(-turnSmooth * Time.deltaTime));
            animator.SetFloat(TurnId, turn);
            if (bikeAnimator) bikeAnimator.SetFloat(TurnId, turn);
        }
    }

    bool CanMount()
    {
        if (animator == null || bike == null || seatPoint == null || rideController == null) return false;
        if (Vector3.Distance(transform.position, seatPoint.position) > mountDistance) return false;

        // 로봇 가슴 → 시트 사이에 벽이 있으면 탑승 불가
        Vector3 chest = transform.position + Vector3.up * capsuleHeight * 0.6f;
        if (Blocking(chest, seatPoint.position)) { if (debugDismount) Debug.Log("[RobotRider] 벽 너머라 탑승 불가"); return false; }
        return true;
    }

    // ---------------- 하차 위치 찾기 ----------------
    bool FindDismountSpot(out Vector3 spot)
    {
        Transform b = bike.transform;
        Vector3 right = new Vector3(-dismountOffset.x, dismountOffset.y, dismountOffset.z);
        Vector3[] offsets = { dismountOffset, right, dismountBackOffset };
        float bikeGround = b.position.y;
        Vector3 bikeChest = b.position + Vector3.up * capsuleHeight * 0.6f;

        foreach (var off in offsets)
        {
            Vector3 p = b.TransformPoint(off);
            p.y = bikeGround;

            // 1. 바닥 찾기
            if (!FindGround(p, out float groundY)) { Log(off, "바닥 없음"); continue; }
            if (Mathf.Abs(groundY - bikeGround) > maxDismountHeightDiff) { Log(off, "높이 차이 큼"); continue; }
            p.y = groundY;

            // 2. 오토바이에서 그 자리까지 벽이 없는지
            if (Blocking(bikeChest, p + Vector3.up * capsuleHeight * 0.6f)) { Log(off, "사이에 벽"); continue; }

            // 3. 로봇이 들어갈 공간이 있는지 (바닥에서 살짝 띄운 캡슐)
            Vector3 bottom = p + Vector3.up * (capsuleRadius + 0.05f);
            Vector3 top = p + Vector3.up * (capsuleHeight - capsuleRadius);
            if (OverlapsObstacle(bottom, top, capsuleRadius * 0.95f)) { Log(off, "공간 부족"); continue; }

            spot = p;
            return true;
        }
        spot = default;
        return false;
    }

    bool FindGround(Vector3 p, out float groundY)
    {
        groundY = 0f;
        int n = Physics.RaycastNonAlloc(p + Vector3.up * groundProbeUp, Vector3.down, hitBuf, groundProbeUp + groundProbeDown, obstacleMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (IsIgnored(hitBuf[i].collider)) continue;
            if (hitBuf[i].distance < best) { best = hitBuf[i].distance; groundY = hitBuf[i].point.y; }
        }
        return best < float.MaxValue;
    }

    bool Blocking(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.001f) return false;
        int n = Physics.RaycastNonAlloc(from, d / len, hitBuf, len, obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!IsIgnored(hitBuf[i].collider)) return true;
        return false;
    }

    bool OverlapsObstacle(Vector3 a, Vector3 b, float r)
    {
        int n = Physics.OverlapCapsuleNonAlloc(a, b, r, overlapBuf, obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!IsIgnored(overlapBuf[i])) return true;
        return false;
    }

    // 오토바이·로봇 자신은 검사에서 제외
    bool IsIgnored(Collider c)
    {
        if (c == null) return true;
        Transform t = c.transform;
        return t.IsChildOf(bike.transform) || t.IsChildOf(transform);
    }

    void Log(Vector3 off, string why) { if (debugDismount) Debug.Log($"[RobotRider] 하차 위치 {off} : {why}"); }

    void Blocked(string reason)
    {
        if (debugDismount) Debug.Log("[RobotRider] 하차 불가 : " + reason);
        DismountBlocked?.Invoke(reason);
    }

    void Mount()
    {
        IsRiding = true;

        if (disableWhileRiding != null) foreach (var b in disableWhileRiding) if (b) b.enabled = false;
        if (rb) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
        foreach (var c in colliders) if (c) c.enabled = false;

        // 부모 스케일이 곱해지지 않도록 월드 크기를 기억해 두었다가 로컬 스케일을 다시 계산
        originalParent = transform.parent;
        originalLocalScale = transform.localScale;
        Vector3 worldScale = transform.lossyScale;

        transform.SetParent(seatPoint, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        Vector3 p = seatPoint.lossyScale;
        transform.localScale = new Vector3(
            worldScale.x / p.x,
            worldScale.y / p.y,
            worldScale.z / p.z);

        walkController = animator.runtimeAnimatorController;
        animator.runtimeAnimatorController = rideController;
        animator.applyRootMotion = false;
        turn = 0f;
        animator.SetFloat(TurnId, 0f);
        if (bikeAnimator) bikeAnimator.SetFloat(TurnId, 0f);

        SetActiveAll(deactivateWhileRiding, false);
        SetActiveAll(activateWhileRiding, true);
        bike.enabled = true;
    }

    void Dismount(Vector3 dropPos)
    {
        IsRiding = false;
        bike.enabled = false;
        if (bikeAnimator) bikeAnimator.SetFloat(TurnId, 0f);

        Transform bikeRoot = bike.transform;
        float yaw = bikeRoot.eulerAngles.y;

        // 원래 부모와 원래 스케일로 복원
        transform.SetParent(originalParent, false);
        transform.localScale = originalLocalScale;
        transform.position = dropPos;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        animator.runtimeAnimatorController = walkController;

        foreach (var c in colliders) if (c) c.enabled = true;
        if (rb) rb.isKinematic = false;
        if (disableWhileRiding != null) foreach (var b in disableWhileRiding) if (b) b.enabled = true;

        SetActiveAll(activateWhileRiding, false);
        SetActiveAll(deactivateWhileRiding, true);
    }

    static void SetActiveAll(GameObject[] list, bool active)
    {
        if (list == null) return;
        foreach (var go in list) if (go) go.SetActive(active);
    }

    bool MountKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && !GameInputBlock.Blocked && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return false;
#endif
    }
}
