using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 오토바이 주행 (Rigidbody 물리 방식)
/// W/S 전진·후진, A/D 회전 + 기울기, Shift 가속, Space 브레이크
/// - 입력은 Update, 이동은 FixedUpdate에서 Rigidbody 속도로 처리
/// - 앞뒤 바퀴 레이캐스트 서스펜션 (stepHeight 이하 턱은 넘어감, 진행 방향 앞쪽도 미리 검사)
/// - 경사에 맞춰 차체 pitch, 좌우 기울기는 Visual 자식에만 적용
/// - 벽에 비스듬히 닿으면 벽을 따라 미끄러짐, 정면이면 멈춤
/// - 스크립트가 꺼지면(하차) 땅에 붙인 뒤 Kinematic으로 고정
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MotorcycleTestDrive : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] float maxSpeed = 10f;
    [SerializeField] float acceleration = 6f;
    [SerializeField] float brake = 12f;          // 반대 방향 입력 시 감속
    [SerializeField] float coastDecel = 3f;      // 아무 입력 없을 때 자연 감속
    [SerializeField] float reverseMaxSpeed = 3f;
    [SerializeField] float turnSpeed = 90f;

    [Header("Boost (Shift)")]
    [SerializeField] float boostMaxSpeed = 18f;
    [SerializeField] float boostAcceleration = 12f;
    [SerializeField] float boostReleaseDecel = 4f;

    [Header("Brake (Space)")]
    [SerializeField] float handBrakeDecel = 25f; // 스페이스 브레이크 감속도 (m/s²)

    [Header("Lean")]
    [SerializeField] float maxLeanAngle = 35f;
    [SerializeField] float leanSmooth = 6f;
    [SerializeField] float fullLeanSpeed = 6f;
    [SerializeField] bool invertLean = false;

    [Header("Visual")]
    [Tooltip("기울기·연출만 적용할 자식. 비우면 'Visual' 이름으로 자동 검색")]
    [SerializeField] Transform visual;

    [Header("Ground / Suspension")]
    [Tooltip("비우면 'Wheel_Front' 이름으로 자동 검색")]
    [SerializeField] Transform frontWheel;
    [Tooltip("비우면 'Wheel_Rear' 이름으로 자동 검색")]
    [SerializeField] Transform rearWheel;
    [Tooltip("바퀴 반지름(m). 0이면 바퀴 메시 크기로 자동 계산")]
    [SerializeField] float wheelRadius = 0f;
    [Tooltip("바퀴 중심보다 이만큼 위에서 레이를 쏨 → 이 높이 이하 턱은 넘어감")]
    [SerializeField] float stepHeight = 0.5f;
    [Tooltip("진행 방향으로 바퀴보다 이만큼 앞도 검사 → 느린 속도에서도 턱을 미리 넘음")]
    [SerializeField] float probeAhead = 0.4f;
    [Tooltip("바퀴가 땅에서 이 거리 안이면 접지로 판단")]
    [SerializeField] float suspensionTravel = 0.3f;
    [SerializeField] float springStrength = 300f;
    [SerializeField] float springDamping = 30f;
    [SerializeField] LayerMask groundMask = ~0;

    [Header("Slope / Air")]
    [SerializeField] float pitchSmooth = 10f;
    [SerializeField] float maxGroundPitch = 40f;
    [SerializeField] float airPitchMax = 25f;
    [SerializeField] float airPitchSmooth = 2f;
    [Range(0f, 1f)][SerializeField] float airSteer = 0.2f;
    [Tooltip("경사에서 중력이 속도에 주는 영향 (0 = 경사에서도 속도 동일)")]
    [Range(0f, 1f)][SerializeField] float slopeInfluence = 0f;

    [Header("Wall")]
    [Tooltip("벽과 이 각도 이하로 닿으면 벽을 따라 미끄러짐, 넘으면 멈춤")]
    [SerializeField] float wallGlanceAngle = 50f;
    [Tooltip("벽을 따라 방향을 트는 속도 (도/초)")]
    [SerializeField] float wallDeflectSpeed = 240f;

    // ── 기존 public 속성 (RobotRider, HoloHUD, Lights, 카메라에서 사용) ──
    public float Speed => speed;
    public float SpeedKmh => speed * 3.6f;
    public float MaxSpeed => maxSpeed;
    public float BoostMaxSpeed => boostMaxSpeed;
    public bool IsBoosting { get; private set; }
    /// <summary>브레이크등용: 스페이스를 누르거나, 전진 중 S를 누르면 true</summary>
    public bool IsBraking { get; private set; }
    /// <summary>스페이스(핸드 브레이크)를 누르고 있는 동안만 true</summary>
    public bool IsHandBraking { get; private set; }
    public float LeanAngle => lean;
    public float SteerInput { get; private set; }

    // ── 새 속성 (착지 충격 → 음식 상태 등에 사용) ──
    public bool IsGrounded { get; private set; }
    public float AirTime { get; private set; }
    /// <summary>마지막 착지 순간 지면 방향으로 떨어지던 속도 (m/s)</summary>
    public float LastLandingImpact { get; private set; }
    /// <summary>착지 순간 호출. 인자는 충격 속도(m/s)</summary>
    public event Action<float> Landed;

    Rigidbody rb;
    float speed, yaw, pitch, lean;
    float inV, inH;
    bool inBoost, inBrake;
    Vector3 frontOffset, rearOffset;
    bool wasGrounded;
    float airTimer;
    bool wallGlancing;
    float wallTargetYaw;
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    static PhysicsMaterial slideMaterial;

    struct WheelHit
    {
        public bool hit;
        public Vector3 point;
        public Vector3 normal;
        public float distFromCenter; // 바퀴 중심 → 지면 거리
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (visual == null) visual = transform.Find("Visual");
        if (frontWheel == null) frontWheel = FindDeep(transform, "Wheel_Front");
        if (rearWheel == null) rearWheel = FindDeep(transform, "Wheel_Rear");

        Quaternion inv = Quaternion.Inverse(transform.rotation);
        frontOffset = frontWheel ? inv * (frontWheel.position - transform.position) : new Vector3(0f, 0.3f, 0.7f);
        rearOffset = rearWheel ? inv * (rearWheel.position - transform.position) : new Vector3(0f, 0.3f, -0.7f);

        if (wheelRadius <= 0f)
        {
            var r = frontWheel ? frontWheel.GetComponentInChildren<Renderer>() : null;
            wheelRadius = r ? r.bounds.extents.y : 0.3f;
        }

        // 벽에 닿았을 때 마찰로 멈추지 않도록 (직접 넣은 머티리얼이 있으면 그대로 둠)
        if (slideMaterial == null)
        {
            slideMaterial = new PhysicsMaterial("MotorcycleSlide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
        foreach (var col in GetComponents<Collider>())
            if (!col.isTrigger && col.sharedMaterial == null) col.sharedMaterial = slideMaterial;

        // 처음부터 꺼진 상태로 시작하면(걷는 중) 바로 주차 상태로
        if (!enabled) Park();
    }

    void OnEnable()
    {
        if (rb == null) return;
        rb.isKinematic = false;
        yaw = transform.eulerAngles.y;
        pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
        speed = 0f;
        wasGrounded = false;
        wallGlancing = false;
    }

    void OnDisable()
    {
        SteerInput = 0f;
        IsBoosting = IsBraking = IsHandBraking = false;
        speed = 0f;
        lean = 0f;
        wallGlancing = false;
        if (visual) visual.localRotation = Quaternion.identity;
        Park();
    }

    void Update()
    {
        ReadInput(out inV, out inH, out inBoost, out inBrake);
        SteerInput = Mathf.Clamp(inH, -1f, 1f);

        IsBoosting = inBoost && inV > 0f && !inBrake;
        IsBraking = inBrake || (inV < 0f && speed > 0.1f) || (inV > 0f && speed < -0.1f);
        IsHandBraking = inBrake;

        // 좌우 기울기 (Visual만)
        float dt = Time.deltaTime;
        float leanFactor = IsGrounded ? Mathf.Clamp01(Mathf.Max(speed, 0f) / fullLeanSpeed) : 0f;
        lean = Mathf.Lerp(lean, inH * maxLeanAngle * leanFactor, 1f - Mathf.Exp(-leanSmooth * dt));
        float roll = invertLean ? lean : -lean;
        if (visual) visual.localRotation = Quaternion.Euler(0f, 0f, roll);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 v = rb.linearVelocity;

        WheelHit f = CastWheel(frontOffset);
        WheelHit r = CastWheel(rearOffset);

        // 진행 방향 앞쪽 미리 검사: 턱이 있으면 바퀴가 닿기 전에 들어 올림
        if (speed >= -0.1f)
        {
            WheelHit p = CastWheel(frontOffset + Vector3.forward * probeAhead);
            if (p.hit && (!f.hit || p.distFromCenter < f.distFromCenter)) f = p;
        }
        else
        {
            WheelHit p = CastWheel(rearOffset + Vector3.back * probeAhead);
            if (p.hit && (!r.hit || p.distFromCenter < r.distFromCenter)) r = p;
        }

        bool grounded = f.hit || r.hit;

        Vector3 normal = Vector3.up;
        if (f.hit && r.hit) normal = (f.normal + r.normal).normalized;
        else if (f.hit) normal = f.normal;
        else if (r.hit) normal = r.normal;

        // 벽을 비스듬히 스쳤으면 벽을 따라가는 방향으로 틈
        if (wallGlancing)
            yaw = Mathf.MoveTowardsAngle(yaw, wallTargetYaw, wallDeflectSpeed * dt);
        wallGlancing = false;

        Vector3 fwdFlat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 fwd = grounded ? Vector3.ProjectOnPlane(fwdFlat, normal).normalized : fwdFlat;

        // 실제 속도 반영: 벽에 막혔으면 줄어든 속도로 맞춤
        if (grounded && wasGrounded)
        {
            float actual = Vector3.Dot(v, fwd);
            if (Mathf.Abs(actual) < Mathf.Abs(speed)) speed = actual;
        }
        else if (!grounded)
        {
            speed = Vector3.Dot(v, fwdFlat);
        }

        if (grounded)
        {
            UpdateThrottle(dt);
            if (slopeInfluence > 0f)
                speed -= Physics.gravity.magnitude * Vector3.Dot(fwd, Vector3.up) * slopeInfluence * dt;
        }

        // 조향
        float speed01 = Mathf.Clamp(speed / maxSpeed, -1f, 1f);
        yaw += inH * turnSpeed * speed01 * (grounded ? 1f : airSteer) * dt;
        fwdFlat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        fwd = grounded ? Vector3.ProjectOnPlane(fwdFlat, normal).normalized : fwdFlat;

        if (grounded)
        {
            // 지면 기울기를 따라가는 속도 + 지면 방향 속도는 유지 (서스펜션 움직임)
            float vn = Vector3.Dot(v, normal);
            rb.linearVelocity = fwd * speed + normal * vn;

            // 서스펜션
            float accel = 0f;
            if (f.hit) accel += 0.5f * springStrength * (wheelRadius - f.distFromCenter);
            if (r.hit) accel += 0.5f * springStrength * (wheelRadius - r.distFromCenter);
            int count = (f.hit ? 1 : 0) + (r.hit ? 1 : 0);
            accel -= springDamping * vn * (count * 0.5f);
            rb.AddForce(normal * accel, ForceMode.Acceleration);
        }

        // 차체 앞뒤 기울기
        float targetPitch;
        float pitchRate;
        if (f.hit && r.hit)
        {
            Vector3 d = f.point - r.point;
            float horiz = new Vector2(d.x, d.z).magnitude;
            float slope = Mathf.Atan2(d.y, Mathf.Max(horiz, 0.01f)) * Mathf.Rad2Deg;
            targetPitch = -Mathf.Clamp(slope, -maxGroundPitch, maxGroundPitch);
            pitchRate = pitchSmooth;
        }
        else if (grounded)
        {
            float slope = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            targetPitch = -Mathf.Clamp(slope, -maxGroundPitch, maxGroundPitch);
            pitchRate = pitchSmooth;
        }
        else
        {
            float horiz = new Vector2(v.x, v.z).magnitude;
            float ang = Mathf.Atan2(v.y, Mathf.Max(horiz, 0.1f)) * Mathf.Rad2Deg;
            targetPitch = -Mathf.Clamp(ang, -airPitchMax, airPitchMax);
            pitchRate = airPitchSmooth;
        }
        pitch = Mathf.LerpAngle(pitch, targetPitch, 1f - Mathf.Exp(-pitchRate * dt));
        rb.MoveRotation(Quaternion.Euler(pitch, yaw, 0f));

        // 착지 / 체공
        if (grounded && !wasGrounded && airTimer > 0.1f)
        {
            LastLandingImpact = Mathf.Max(0f, -Vector3.Dot(v, normal));
            Landed?.Invoke(LastLandingImpact);
        }
        airTimer = grounded ? 0f : airTimer + dt;
        AirTime = airTimer;
        IsGrounded = grounded;
        wasGrounded = grounded;
    }

    void OnCollisionStay(Collision c)
    {
        if (!enabled || Mathf.Abs(speed) < 0.5f) return;

        // 실제로 움직이는 방향 (후진이면 반대)
        Vector3 moveDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        if (speed < 0f) moveDir = -moveDir;

        for (int i = 0; i < c.contactCount; i++)
        {
            Vector3 n = c.GetContact(i).normal;
            if (Mathf.Abs(n.y) > 0.6f) continue; // 바닥·천장은 무시, 벽만

            Vector3 nFlat = new Vector3(n.x, 0f, n.z);
            if (nFlat.sqrMagnitude < 0.0001f) continue;
            nFlat.Normalize();

            float into = -Vector3.Dot(moveDir, nFlat); // 1 = 정면, 0 = 평행
            if (into <= 0.05f) continue;

            float angle = Mathf.Asin(Mathf.Clamp01(into)) * Mathf.Rad2Deg;
            if (angle > wallGlanceAngle) return; // 정면 충돌은 그대로 멈춤

            Vector3 slide = (moveDir + nFlat * into).normalized; // 벽을 따라가는 방향
            Vector3 heading = speed < 0f ? -slide : slide;
            wallTargetYaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            wallGlancing = true;
            return;
        }
    }

    void UpdateThrottle(float dt)
    {
        if (inBrake)
        {
            // 스페이스: 입력과 상관없이 강하게 0으로
            speed = Mathf.MoveTowards(speed, 0f, handBrakeDecel * dt);
            return;
        }

        float target;
        if (inV > 0f) target = IsBoosting ? boostMaxSpeed : maxSpeed;
        else if (inV < 0f) target = -reverseMaxSpeed;
        else target = 0f;

        float rate;
        if (target != 0f && Mathf.Sign(target) != Mathf.Sign(speed) && Mathf.Abs(speed) > 0.01f)
            rate = brake;
        else if (Mathf.Abs(target) > Mathf.Abs(speed))
            rate = IsBoosting ? boostAcceleration : acceleration;
        else if (inV > 0f && speed > maxSpeed)
            rate = boostReleaseDecel;
        else
            rate = coastDecel;
        speed = Mathf.MoveTowards(speed, target, rate * dt);
    }

    WheelHit CastWheel(Vector3 offset)
    {
        WheelHit result = default;
        Vector3 center = rb.position + rb.rotation * offset;
        Vector3 origin = center + Vector3.up * stepHeight;
        float length = stepHeight + wheelRadius + suspensionTravel;

        int n = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, length, groundMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = hitBuffer[i];
            if (h.collider.attachedRigidbody == rb || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                result.hit = true;
                result.point = h.point;
                result.normal = h.normal;
                result.distFromCenter = h.distance - stepHeight;
            }
        }
        return result;
    }

    /// <summary>하차·대기 상태: 바퀴를 땅에 붙이고 Kinematic으로 고정</summary>
    void Park()
    {
        if (rb == null) return;
        if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;

        WheelHit f = CastWheel(frontOffset);
        WheelHit r = CastWheel(rearOffset);
        int count = 0;
        float gap = 0f;
        if (f.hit) { gap += f.distFromCenter - wheelRadius; count++; }
        if (r.hit) { gap += r.distFromCenter - wheelRadius; count++; }
        if (count > 0)
        {
            Vector3 p = transform.position - Vector3.up * (gap / count);
            transform.position = p;
            rb.position = p;
        }
        rb.isKinematic = true;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    void ReadInput(out float v, out float h, out bool boost, out bool brakeKey)
    {
        v = 0f; h = 0f; boost = false; brakeKey = false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
        boost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        brakeKey = kb.spaceKey.isPressed;
#endif
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 바퀴 레이 위치 확인용 (하늘색: 바퀴, 노란색: 앞쪽 미리 검사)
        Transform fw = frontWheel ? frontWheel : FindDeep(transform, "Wheel_Front");
        Transform rw = rearWheel ? rearWheel : FindDeep(transform, "Wheel_Rear");
        float rad = wheelRadius > 0f ? wheelRadius : 0.3f;
        float len = stepHeight + rad + suspensionTravel;
        Gizmos.color = Color.cyan;
        foreach (var w in new[] { fw, rw })
        {
            if (!w) continue;
            Vector3 o = w.position + Vector3.up * stepHeight;
            Gizmos.DrawLine(o, o + Vector3.down * len);
            Gizmos.DrawWireSphere(w.position, rad);
        }
        if (fw)
        {
            Gizmos.color = Color.yellow;
            Vector3 o = fw.position + transform.forward * probeAhead + Vector3.up * stepHeight;
            Gizmos.DrawLine(o, o + Vector3.down * len);
        }
    }
#endif
}