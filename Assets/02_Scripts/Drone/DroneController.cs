using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// OrbDrone 이동
/// - Follow : 캐릭터 주변(가로 1m 이내)을 천천히 떠돌고, 높이는 허리 ~ 머리 위 20cm 사이에서 오르락내리락
/// - 급격한 변동(낙하, 달리기, 주행) : 최고 속도 제한으로 늦게 따라오되, 정상 범위에서 leashExtra(1m) 이상은 벌어지지 않음
/// - Hologram : DroneHoloPoint(머리 좌측)로 빠르게 이동해 카메라 방향을 바라봄. 도착하면 HologramReady 이벤트
/// - 충돌 : SphereCast로 벽·건물 앞에서 멈추고 벽면을 따라 미끄러짐. 캐릭터와 목표 사이가 막히면 머리 뒤로 와서 따라옴.
///          캐릭터 몸과는 최소 거리를 유지 (밀지 않음). 계속 끼어 있으면 머리 뒤로 순간이동
/// Rigidbody 없이 Transform만 움직인다. PlayerRobot의 자식이 아니라 PlayerRig 아래 형제로 둘 것.
/// PlayerRig(부모) 아래의 콜라이더(로봇, 오토바이)는 충돌 검사에서 무시한다.
/// </summary>
[DefaultExecutionOrder(100)]   // Invector 이동·애니메이션 이후에 계산
public class DroneController : MonoBehaviour
{
    public enum DroneState { Follow, Hologram }

    [Header("References (비우면 자동)")]
    [Tooltip("따라갈 캐릭터. 비우면 RobotRider를 찾음")]
    [SerializeField] Transform player;
    [Tooltip("허리·머리 높이를 읽을 Humanoid Animator. 비우면 player에서 찾음")]
    [SerializeField] Animator playerAnimator;
    [Tooltip("Q를 눌렀을 때 갈 자리 (PlayerRobot 아래 DroneHoloPoint). 비우면 머리 좌측을 계산")]
    [SerializeField] Transform holoPoint;
    [Tooltip("홀로그램 때 바라볼 카메라. 비우면 Camera.main")]
    [SerializeField] Transform viewCamera;
    [Tooltip("충돌 검사에서 무시할 루트 (로봇, 오토바이). 비우면 부모(PlayerRig)")]
    [SerializeField] Transform ignoreRoot;

    [Header("Follow - 가로")]
    [Tooltip("캐릭터 기준 기본 위치 (x 오른쪽, z 앞). 캐릭터가 보는 방향 기준")]
    [SerializeField] Vector2 followOffset = new Vector2(0.6f, -0.35f);
    [Tooltip("기본 위치 주변을 떠도는 반경 (m)")]
    [SerializeField] float wanderRadius = 0.3f;
    [SerializeField] float wanderSpeed = 0.12f;
    [Tooltip("평상시 캐릭터와의 최대 가로 거리 (m)")]
    [SerializeField] float followRadius = 1f;

    [Header("Follow - 높이")]
    [Tooltip("머리 뼈(Head) 위치에서 정수리까지 높이 (m, 월드). 로봇 스케일 반영된 값")]
    [SerializeField] float headTopOffset = 0.25f;
    [Tooltip("정수리 위로 더 올라갈 수 있는 높이 (m)")]
    [SerializeField] float aboveHead = 0.2f;
    [Tooltip("높이 범위 안에서 오르락내리락하는 속도")]
    [SerializeField] float bobSpeed = 0.18f;
    [Tooltip("Animator가 Humanoid가 아닐 때 쓰는 허리·정수리 높이 (발 기준 m)")]
    [SerializeField] Vector2 fallbackWaistTop = new Vector2(1.0f, 1.75f);

    [Header("Follow - 움직임")]
    [SerializeField] float smoothTime = 0.55f;
    [Tooltip("평상시 최고 속도 (m/s). 이보다 빠른 변동은 늦게 따라옴")]
    [SerializeField] float maxSpeed = 6f;
    [Tooltip("급격한 변동 때 정상 범위에서 최대 벗어날 수 있는 거리 (m)")]
    [SerializeField] float leashExtra = 1f;
    [Tooltip("이보다 멀어지면 순간이동 (리스폰 등)")]
    [SerializeField] float teleportDistance = 25f;
    [SerializeField] float turnSpeed = 4f;

    [Header("Collision")]
    [Tooltip("드론이 피할 대상 (지형, 건물, 도로, 가게). Drone 레이어는 빼기")]
    [SerializeField] LayerMask obstacleLayers = ~0;
    [Tooltip("드론 충돌 반지름 (m). 지름 약 0.3m 모델 기준")]
    [SerializeField] float droneRadius = 0.15f;
    [Tooltip("벽과 띄워 두는 간격 (m)")]
    [SerializeField] float skin = 0.02f;
    [Tooltip("캐릭터 몸 중심축과의 최소 가로 거리 (m). 머리보다 낮을 때만 적용")]
    [SerializeField] float minPlayerDistance = 0.4f;
    [Tooltip("캐릭터와 목표 사이가 막혔을 때 갈 자리 : 머리 기준 (x 오른쪽, y 위, z 앞)")]
    [SerializeField] Vector3 behindHeadOffset = new Vector3(0f, 0.2f, -0.45f);
    [Tooltip("막힘이 풀린 뒤 이 시간 동안은 머리 뒤에 머묾 (깜빡임 방지)")]
    [SerializeField] float unblockDelay = 0.6f;
    [Tooltip("캐릭터와 드론 사이가 이 시간 이상 막혀 있으면 머리 뒤로 순간이동")]
    [SerializeField] float stuckTeleportTime = 1.5f;

    [Header("Hologram")]
    [SerializeField] float holoSmoothTime = 0.12f;
    [SerializeField] float holoMaxSpeed = 15f;
    [Tooltip("이 거리 안에 들어오면 도착으로 봄 (m)")]
    [SerializeField] float holoArriveDistance = 0.06f;
    [Tooltip("holoPoint가 없을 때 : 머리 기준 (x 오른쪽, y 위, z 앞)")]
    [SerializeField] Vector3 fallbackHoloOffset = new Vector3(-0.45f, 0.1f, 0.15f);
    [Tooltip("홀로그램 중 미세한 흔들림 (m)")]
    [SerializeField] float holoBobAmount = 0.015f;

    [Header("Model")]
    [Tooltip("모델의 렌즈가 +Z를 보지 않으면 보정 (예: 0,180,0)")]
    [SerializeField] Vector3 modelRotationOffset = Vector3.zero;

    [Header("Test")]
    [Tooltip("콜 메뉴가 생기기 전 테스트용 : Q로 홀로그램 위치 토글, ESC로 닫기. 콜 메뉴 연결 후 끄기")]
    [SerializeField] bool useTestKey = true;
    [SerializeField] bool logBlocked = false;

    // ---- 상태 ----
    public DroneState State { get; private set; } = DroneState.Follow;
    public bool IsHologramReady { get; private set; }
    public bool IsBlocked { get; private set; }     // 머리 뒤 우회 중
    public event Action HologramReady;    // 홀로그램 자리에 도착 (콜 메뉴가 여기서 패널을 열면 됨)
    public event Action HologramClosed;

    Vector3 velocity;
    float seed;
    Transform hips, head;
    SphereCollider sphere;
    float unblockTimer, stuckTimer;

    readonly RaycastHit[] hitBuf = new RaycastHit[16];
    readonly Collider[] overlapBuf = new Collider[16];

    void Awake()
    {
        seed = UnityEngine.Random.Range(0f, 100f);
        if (player == null)
        {
            var rider = FindFirstObjectByType<RobotRider>();
            if (rider != null) player = rider.transform;
        }
        if (playerAnimator == null && player != null) playerAnimator = player.GetComponentInChildren<Animator>();
        if (playerAnimator != null && playerAnimator.isHuman)
        {
            hips = playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
            head = playerAnimator.GetBoneTransform(HumanBodyBones.Head);
        }
        if (viewCamera == null && Camera.main != null) viewCamera = Camera.main.transform;
        if (ignoreRoot == null) ignoreRoot = transform.parent;

        // 밀어내기 계산용 구 콜라이더 (트리거 : 물리적으로 캐릭터·오토바이를 밀지 않음)
        sphere = GetComponent<SphereCollider>();
        if (sphere == null) sphere = gameObject.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.center = Vector3.zero;
        float s = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
        sphere.radius = droneRadius / Mathf.Max(0.0001f, s);
    }

    void Start()
    {
        if (player != null) transform.position = FollowTarget();
    }

    // ---------------- 외부 API (콜 메뉴가 사용) ----------------
    public void SetHologram(bool on)
    {
        var next = on ? DroneState.Hologram : DroneState.Follow;
        if (next == State) return;
        State = next;
        if (!on && IsHologramReady)
        {
            IsHologramReady = false;
            HologramClosed?.Invoke();
        }
    }

    public void ToggleHologram() => SetHologram(State != DroneState.Hologram);

    // ---------------- 업데이트 ----------------
    void Update()
    {
        if (!useTestKey) return;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.qKey.wasPressedThisFrame) ToggleHologram();
        else if (kb.escapeKey.wasPressedThisFrame && State == DroneState.Hologram) SetHologram(false);
#endif
    }

    void LateUpdate()
    {
        if (player == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (State == DroneState.Follow) UpdateFollow(dt);
        else UpdateHologram(dt);
    }

    void UpdateFollow(float dt)
    {
        Vector3 eye = EyePoint();
        Vector3 target = FollowTarget();

        // 캐릭터 → 목표 사이가 막혔으면 머리 뒤로
        bool blockedNow = Blocked(eye, target);
        if (blockedNow) unblockTimer = unblockDelay;
        else unblockTimer -= dt;
        bool blocked = unblockTimer > 0f;
        if (blocked != IsBlocked && logBlocked) Debug.Log($"[Drone] {(blocked ? "막힘 → 머리 뒤" : "막힘 해제")}");
        IsBlocked = blocked;
        if (blocked) target = BehindHeadTarget();

        Vector3 pos = transform.position;

        // 멀리 떨어졌거나(리스폰) 캐릭터와 드론 사이가 오래 막혀 있으면 순간이동
        stuckTimer = Blocked(eye, pos) ? stuckTimer + dt : 0f;
        if ((pos - target).sqrMagnitude > teleportDistance * teleportDistance || stuckTimer > stuckTeleportTime)
        {
            transform.position = Depenetrate(BehindHeadTarget());
            velocity = Vector3.zero;
            stuckTimer = 0f;
            return;
        }

        Vector3 next = Vector3.SmoothDamp(pos, target, ref velocity, smoothTime, maxSpeed, dt);
        next = ApplyLeash(next);
        next = KeepOutOfPlayer(next);
        MoveTo(next, dt);

        // 캐릭터가 보는 방향으로 천천히 회전 (수평)
        Vector3 fwd = Flat(player.forward);
        if (fwd.sqrMagnitude > 0.0001f) RotateTowards(fwd, dt);
    }

    void UpdateHologram(float dt)
    {
        IsBlocked = false;
        stuckTimer = 0f;

        Vector3 target = HoloTarget();
        float t = Time.time;
        target += new Vector3(0f, (Mathf.PerlinNoise(seed, t * 0.8f) - 0.5f) * 2f * holoBobAmount, 0f);

        Vector3 next = Vector3.SmoothDamp(transform.position, target, ref velocity, holoSmoothTime, holoMaxSpeed, dt);
        MoveTo(next, dt);

        Vector3 look = viewCamera != null ? viewCamera.forward : player.forward;
        RotateTowards(look, dt * 2f);

        if (!IsHologramReady && (transform.position - HoloTarget()).sqrMagnitude < holoArriveDistance * holoArriveDistance)
        {
            IsHologramReady = true;
            HologramReady?.Invoke();
        }
    }

    // ---------------- 목표 위치 ----------------
    Vector3 FollowTarget()
    {
        float t = Time.time;
        Quaternion yaw = Yaw();

        // 가로 : 기본 위치 + 떠돌기, 캐릭터로부터 followRadius 이내
        Vector2 wander = new Vector2(
            Mathf.PerlinNoise(seed, t * wanderSpeed) - 0.5f,
            Mathf.PerlinNoise(seed + 31.7f, t * wanderSpeed) - 0.5f) * 2f * wanderRadius;
        Vector3 local = new Vector3(followOffset.x + wander.x, 0f, followOffset.y + wander.y);
        Vector3 horiz = Vector3.ClampMagnitude(yaw * local, followRadius);

        // 높이 : 허리 ~ 정수리 + aboveHead 사이에서 천천히 오르락내리락
        GetHeightBand(out float low, out float high);
        float n = Mathf.InverseLerp(0.2f, 0.8f, Mathf.PerlinNoise(seed + 77.3f, t * bobSpeed));
        float y = Mathf.Lerp(low, high, Mathf.SmoothStep(0f, 1f, n));

        Vector3 p = player.position + horiz;
        p.y = y;
        return p;
    }

    Vector3 BehindHeadTarget()
    {
        Vector3 h = HeadPoint();
        Vector3 p = h + Yaw() * behindHeadOffset;
        // 머리 뒤도 막혀 있으면 (벽에 등을 댄 경우) 막힌 지점 앞까지만
        if (Cast(h, p - h, out RaycastHit hit))
            p = h + (p - h).normalized * Mathf.Max(0f, hit.distance - skin);
        return p;
    }

    Vector3 HoloTarget()
    {
        if (holoPoint != null) return holoPoint.position;
        return HeadPoint() + Yaw() * fallbackHoloOffset;
    }

    Vector3 HeadPoint() => head != null ? head.position : player.position + Vector3.up * fallbackWaistTop.y;

    // 시야 검사 기준점 : 머리 (드론이 머리 위쪽에 있을 수 있어서)
    Vector3 EyePoint() => HeadPoint();

    Quaternion Yaw() => Quaternion.Euler(0f, player.eulerAngles.y, 0f);

    void GetHeightBand(out float low, out float high)
    {
        if (hips != null && head != null)
        {
            low = hips.position.y;
            high = head.position.y + headTopOffset + aboveHead;
        }
        else
        {
            low = player.position.y + fallbackWaistTop.x;
            high = player.position.y + fallbackWaistTop.y + aboveHead;
        }
        if (high < low) high = low;
    }

    // 정상 범위(가로 followRadius, 높이 band)에서 leashExtra 이상 벗어나지 않게
    Vector3 ApplyLeash(Vector3 pos)
    {
        Vector3 center = player.position;
        Vector3 d = pos - center; d.y = 0f;
        float maxH = followRadius + leashExtra;
        if (d.sqrMagnitude > maxH * maxH)
        {
            d = d.normalized * maxH;
            pos.x = center.x + d.x;
            pos.z = center.z + d.z;
        }

        GetHeightBand(out float low, out float high);
        pos.y = Mathf.Clamp(pos.y, low - leashExtra, high + leashExtra);
        return pos;
    }

    // 캐릭터 몸(세로 축)과 최소 가로 거리 유지. 머리보다 높으면 적용 안 함
    Vector3 KeepOutOfPlayer(Vector3 pos)
    {
        GetHeightBand(out _, out float high);
        float headTop = high - aboveHead;
        if (pos.y > headTop) return pos;

        Vector3 d = pos - player.position; d.y = 0f;
        float min = minPlayerDistance + droneRadius;
        if (d.sqrMagnitude >= min * min) return pos;

        Vector3 dir = d.sqrMagnitude > 0.0001f ? d.normalized : Yaw() * Vector3.right;
        Vector3 p = player.position + dir * min;
        pos.x = p.x; pos.z = p.z;
        return pos;
    }

    // ---------------- 충돌 ----------------
    // 현재 위치에서 목표까지 SphereCast로 이동. 벽에 닿으면 벽면을 따라 미끄러짐
    void MoveTo(Vector3 desired, float dt)
    {
        Vector3 start = transform.position;
        Vector3 pos = start;
        Vector3 move = desired - start;
        bool hitAny = false;

        for (int i = 0; i < 2 && move.sqrMagnitude > 0.000001f; i++)
        {
            if (Cast(pos, move, out RaycastHit hit))
            {
                hitAny = true;
                float travel = Mathf.Max(0f, hit.distance - skin);
                Vector3 dir = move.normalized;
                pos += dir * travel;
                Vector3 remain = move - dir * travel;
                move = Vector3.ProjectOnPlane(remain, hit.normal);   // 벽면 따라 미끄러짐
            }
            else
            {
                pos += move;
                move = Vector3.zero;
            }
        }

        pos = Depenetrate(pos);
        transform.position = pos;

        // 벽에 막혔으면 쌓인 속도를 실제 이동량으로 맞춤 (벽에서 튀어나가지 않게)
        if (hitAny) velocity = (pos - start) / dt;
    }

    bool Blocked(Vector3 from, Vector3 to) => Cast(from, to - from, out _);

    // 가장 가까운 장애물 (PlayerRig 아래 콜라이더, 트리거, 시작 시 겹친 것 제외)
    bool Cast(Vector3 origin, Vector3 delta, out RaycastHit best)
    {
        best = default;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return false;

        int n = Physics.SphereCastNonAlloc(origin, droneRadius, delta / dist, hitBuf, dist,
                                           obstacleLayers, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = hitBuf[i];
            if (h.distance <= 0f) continue;           // 시작부터 겹침 → Depenetrate가 처리
            if (Ignored(h.collider)) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; }
        }
        return bestDist < float.MaxValue;
    }

    // 벽 안에 들어가 있으면 밖으로 밀어냄
    Vector3 Depenetrate(Vector3 pos)
    {
        if (sphere == null) return pos;
        float r = droneRadius + skin;
        for (int iter = 0; iter < 3; iter++)
        {
            int n = Physics.OverlapSphereNonAlloc(pos, r, overlapBuf, obstacleLayers, QueryTriggerInteraction.Ignore);
            bool moved = false;
            for (int i = 0; i < n; i++)
            {
                var c = overlapBuf[i];
                if (c == sphere || Ignored(c)) continue;
                if (Physics.ComputePenetration(sphere, pos, transform.rotation,
                                               c, c.transform.position, c.transform.rotation,
                                               out Vector3 dir, out float d))
                {
                    pos += dir * (d + skin);
                    moved = true;
                }
            }
            if (!moved) break;
        }
        return pos;
    }

    bool Ignored(Collider c)
    {
        if (c == null) return true;
        if (c.transform.IsChildOf(transform)) return true;
        return ignoreRoot != null && c.transform.IsChildOf(ignoreRoot);
    }

    void RotateTowards(Vector3 dir, float dt)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(modelRotationOffset);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSpeed * dt));
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ---------------- 기즈모 ----------------
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, droneRadius);
        if (player == null || !Application.isPlaying) return;

        GetHeightBand(out float low, out float high);
        Vector3 c = player.position;
        Gizmos.color = new Color(0.13f, 0.94f, 1f, 0.8f);
        DrawCircle(new Vector3(c.x, low, c.z), followRadius);
        DrawCircle(new Vector3(c.x, high, c.z), followRadius);
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.6f);
        DrawCircle(new Vector3(c.x, high + leashExtra, c.z), followRadius + leashExtra);
        Gizmos.color = Color.red;
        DrawCircle(new Vector3(c.x, low, c.z), minPlayerDistance + droneRadius);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(HoloTarget(), 0.05f);
        Gizmos.color = IsBlocked ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(BehindHeadTarget(), 0.05f);
        Gizmos.DrawLine(EyePoint(), transform.position);
    }

    static void DrawCircle(Vector3 c, float r)
    {
        const int seg = 32;
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}
