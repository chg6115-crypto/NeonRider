using UnityEngine;

/// <summary>
/// 가까이 가면 열리는 자동문 (여닫이). 문 오브젝트(피벗 = 경첩)에 붙임.
/// - 로봇이 걸어서 openDistance 안에 들어오면 열리고, 멀어지면 closeDelay 뒤 닫힘
/// - 플레이어 반대쪽으로 열림 (안에서 나갈 땐 바깥으로, 밖에서 들어갈 땐 안으로)
/// - 키트의 Animator / opencloseDoor(구 입력 방식)는 자동으로 꺼짐
/// 양문이면 문 두 짝에 각각 붙이면 됨.
/// </summary>
public class AutoDoor : MonoBehaviour
{
    [Header("Open")]
    [SerializeField] float openAngle = 90f;
    [Tooltip("초당 회전 속도(도). 클수록 빨리 열림")]
    [SerializeField] float openSpeed = 200f;
    [Tooltip("로봇이 문 중심에서 이 거리(m) 안이면 열림")]
    [SerializeField] float openDistance = 2.2f;
    [Tooltip("멀어진 뒤 닫히기까지 대기(초)")]
    [SerializeField] float closeDelay = 0.8f;
    [Tooltip("켜면 걸어서 다가올 때만 열림 (오토바이 탄 채로는 안 열림)")]
    [SerializeField] bool onlyOnFoot = true;
    [Tooltip("켜면 플레이어 반대쪽으로 열림. 끄면 항상 같은 방향(+openAngle)")]
    [SerializeField] bool swingAwayFromPlayer = true;

    [Header("References")]
    [SerializeField] RobotRider rider;                    // 비우면 씬에서 찾음
    [Tooltip("문을 움직이던 기존 컴포넌트 (비우면 Animator·opencloseDoor 자동 비활성화)")]
    [SerializeField] Behaviour[] disableOnStart;

    Quaternion closedRot;
    Vector3 closedCenter;       // 닫힌 상태의 문 중심 (월드)
    Vector3 leafDir;            // 경첩 → 문 끝 방향 (월드, 수평)
    float currentAngle, targetAngle, farTimer;

    void Awake()
    {
        if (rider == null) rider = FindFirstObjectByType<RobotRider>();

        if (disableOnStart != null && disableOnStart.Length > 0)
        {
            foreach (var b in disableOnStart) if (b) b.enabled = false;
        }
        else
        {
            var anim = GetComponent<Animator>();
            if (anim) anim.enabled = false;              // 애니메이션이 회전을 덮어쓰지 않게
            foreach (var mb in GetComponents<MonoBehaviour>())
                if (mb != this && mb.GetType().Name == "opencloseDoor") mb.enabled = false;
        }

        closedRot = transform.localRotation;

        var col = GetComponent<Collider>();
        closedCenter = col ? col.bounds.center : transform.position;
        leafDir = closedCenter - transform.position;
        leafDir.y = 0f;
        if (leafDir.sqrMagnitude < 0.0001f) leafDir = transform.right;
        leafDir.Normalize();
    }

    void Update()
    {
        bool near = false;
        Vector3 toPlayer = Vector3.zero;
        if (rider != null && (!onlyOnFoot || !rider.IsRiding))
        {
            toPlayer = rider.transform.position - closedCenter;
            toPlayer.y = 0f;
            near = toPlayer.magnitude <= openDistance;
        }

        if (near)
        {
            farTimer = 0f;
            if (Mathf.Approximately(targetAngle, 0f))     // 닫혀 있을 때만 열 방향 결정
                targetAngle = OpenSign(toPlayer) * openAngle;
        }
        else
        {
            farTimer += Time.deltaTime;
            if (farTimer >= closeDelay) targetAngle = 0f;
        }

        currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, openSpeed * Time.deltaTime);
        transform.localRotation = closedRot * Quaternion.AngleAxis(currentAngle, Vector3.up);
    }

    // 문 끝이 플레이어 반대쪽으로 움직이는 회전 방향(+1 / -1)
    float OpenSign(Vector3 toPlayer)
    {
        if (!swingAwayFromPlayer || toPlayer.sqrMagnitude < 0.0001f) return 1f;
        Vector3 up = transform.parent ? transform.parent.up : Vector3.up;
        Vector3 moved = Quaternion.AngleAxis(openAngle, up) * leafDir - leafDir;   // +방향으로 열 때 문 끝이 움직이는 방향
        return Vector3.Dot(moved, toPlayer) > 0f ? -1f : 1f;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        Vector3 c = Application.isPlaying ? closedCenter : (col ? col.bounds.center : transform.position);
        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(c, openDistance);
    }
#endif
}
