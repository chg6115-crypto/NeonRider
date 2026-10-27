using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 로봇 루트에 붙임. F키로 오토바이 탑승/하차.
/// 탑승 중: 시트에 고정 + 걷기 스크립트 비활성 + 라이딩 Animator Controller로 교체
///          + 오토바이 기울기에 맞춰 Turn 파라미터 갱신
/// 오토바이(부모)가 스케일되어 있어도 로봇의 실제 크기는 그대로 유지
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

    public bool IsRiding { get; private set; }

    static readonly int TurnId = Animator.StringToHash("Turn");

    RuntimeAnimatorController walkController;
    Rigidbody rb;
    Collider[] colliders;
    float turn;

    Transform originalParent;
    Vector3 originalLocalScale;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();
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
            else if (IsRiding && Mathf.Abs(bike.Speed) <= maxDismountSpeed) Dismount();
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
        return animator != null && bike != null && seatPoint != null && rideController != null
            && Vector3.Distance(transform.position, seatPoint.position) <= mountDistance;
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

    void Dismount()
    {
        IsRiding = false;
        bike.enabled = false;
        if (bikeAnimator) bikeAnimator.SetFloat(TurnId, 0f);

        Transform bikeRoot = bike.transform;
        Vector3 dropPos = bikeRoot.TransformPoint(dismountOffset);
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
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return false;
#endif
    }
}
