using UnityEngine;

/// <summary>
/// 플레이어가 가까이 오면 열리는 여닫이 자동문.
/// 경첩 위치의 부모 오브젝트에 붙입니다.
/// 자식의 충돌체 또는 Renderer로 문 중심을 계산합니다.
/// </summary>
public class AutoDoor : MonoBehaviour
{
    [Header("Open")]
    [SerializeField] float openAngle = 90f;
    [Tooltip("초당 회전 속도")]
    [SerializeField] float openSpeed = 200f;
    [Tooltip("감지 중심에서 이 거리 안으로 접근하면 열림")]
    [SerializeField] float openDistance = 2.2f;
    [SerializeField] float closeDelay = 0.8f;
    [Tooltip("오토바이에서 내린 상태에서만 열림")]
    [SerializeField] bool onlyOnFoot = true;
    [Tooltip("플레이어 반대쪽으로 열림")]
    [SerializeField] bool swingAwayFromPlayer = true;

    [Header("References")]
    [SerializeField] RobotRider rider;
    [SerializeField] Behaviour[] disableOnStart;

    [Header("Optional")]
    [Tooltip("양문에 같은 대상을 지정하면 같은 위치에서 접근을 감지합니다. " +
             "회전하지 않는 입구 중앙 Empty를 지정하세요.")]
    [SerializeField] Transform detectionPoint;

    [Tooltip("감지 경계에서 문이 반복해서 열리고 닫히는 것을 방지")]
    [Min(0f)]
    [SerializeField] float closeDistancePadding = 0.3f;

    Quaternion closedRot;
    Vector3 closedCenter;
    Vector3 leafDir;
    Vector3 hingeAxisLocal;
    Vector3 hingeAxisWorld;

    float currentAngle;
    float targetAngle;
    float farTimer;
    bool playerNearby;

    void Awake()
    {
        if (rider == null)
            rider = FindFirstObjectByType<RobotRider>();

        if (disableOnStart != null && disableOnStart.Length > 0)
        {
            foreach (var behaviour in disableOnStart)
            {
                if (behaviour != null && behaviour != this)
                    behaviour.enabled = false;
            }
        }
        else
        {
            var animator = GetComponent<Animator>();
            if (animator != null)
                animator.enabled = false;

            foreach (var behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour != null &&
                    behaviour != this &&
                    behaviour.GetType().Name == "opencloseDoor")
                {
                    behaviour.enabled = false;
                }
            }
        }

        closedRot = transform.localRotation;

        // 親の上方向を 경첩 축으로 사용.
        hingeAxisWorld = transform.parent != null
            ? transform.parent.up
            : Vector3.up;

        // closedRot 뒤에 곱할 회전이므로 문 자신의 좌표로 변환.
        hingeAxisLocal =
            transform.InverseTransformDirection(hingeAxisWorld).normalized;

        closedCenter = FindDoorCenter();

        leafDir = Vector3.ProjectOnPlane(
            closedCenter - transform.position,
            hingeAxisWorld
        );

        if (leafDir.sqrMagnitude < 0.0001f)
        {
            leafDir = Vector3.ProjectOnPlane(
                transform.right,
                hingeAxisWorld
            );
        }

        leafDir.Normalize();
    }

    Vector3 FindDoorCenter()
    {
        Bounds bounds = default;
        bool found = false;

        // 부모뿐 아니라 유리·프레임 등 자식 충돌체까지 포함.
        foreach (var collider in GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger)
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (found)
            return bounds.center;

        // 충돌체가 없으면 보이는 문 메시를 기준으로 계산.
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found ? bounds.center : transform.position;
    }

    void Update()
    {
        Vector3 detectionCenter = detectionPoint != null
            ? detectionPoint.position
            : closedCenter;

        bool canOpen =
            rider != null && (!onlyOnFoot || !rider.IsRiding);

        Vector3 toPlayer = Vector3.zero;

        if (canOpen)
        {
            toPlayer = Vector3.ProjectOnPlane(
                rider.transform.position - detectionCenter,
                hingeAxisWorld
            );

            float distance = Mathf.Max(0f, openDistance);

            if (playerNearby)
                distance += Mathf.Max(0f, closeDistancePadding);

            playerNearby = toPlayer.sqrMagnitude <= distance * distance;
        }
        else
        {
            playerNearby = false;
        }

        if (playerNearby)
        {
            farTimer = 0f;

            if (Mathf.Approximately(targetAngle, 0f))
            {
                // 閉じる途中に戻ってきた場合は開く向きを反転させない.
                float sign = Mathf.Abs(currentAngle) > 0.1f
                    ? Mathf.Sign(currentAngle)
                    : OpenSign(toPlayer);

                targetAngle = sign * Mathf.Abs(openAngle);
            }
        }
        else
        {
            farTimer += Time.deltaTime;

            if (farTimer >= Mathf.Max(0f, closeDelay))
                targetAngle = 0f;
        }

        currentAngle = Mathf.MoveTowards(
            currentAngle,
            targetAngle,
            Mathf.Max(0f, openSpeed) * Time.deltaTime
        );

        transform.localRotation =
            closedRot *
            Quaternion.AngleAxis(currentAngle, hingeAxisLocal);
    }

    float OpenSign(Vector3 toPlayer)
    {
        if (!swingAwayFromPlayer)
            return openAngle < 0f ? -1f : 1f;

        if (toPlayer.sqrMagnitude < 0.0001f)
            return 1f;

        // 양의 회전에서 문이 움직이기 시작하는 방향.
        // 좌우 문짝은 leafDir이 반대이므로 회전 방향도 반대로 계산됨.
        Vector3 openingDirection =
            Vector3.Cross(hingeAxisWorld, leafDir);

        return Vector3.Dot(openingDirection, toPlayer) > 0f
            ? -1f
            : 1f;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 center = detectionPoint != null
            ? detectionPoint.position
            : Application.isPlaying
                ? closedCenter
                : FindDoorCenter();

        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(center, Mathf.Max(0f, openDistance));
    }
#endif
}