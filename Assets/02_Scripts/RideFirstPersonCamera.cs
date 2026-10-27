using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 탑승 중 1인칭(콕핏) 카메라. Invector 카메라(vThirdPersonCamera_LITE)와 같은 오브젝트에 붙임.
/// - 탑승 + 1인칭 모드: 3인칭 스크립트 끄고 FPS_Eye 위치에서 마우스로 둘러보기, 로봇 몸은 그림자만 남기고 숨김
/// - V키: 1인칭/3인칭 전환 (탑승 중)
/// - 휠: FOV 조절
/// </summary>
[RequireComponent(typeof(Camera))]
public class RideFirstPersonCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] RobotRider rider;
    [SerializeField] Transform eyePoint;                 // Motorcycle/FPS_Eye
    [SerializeField] MotorcycleTestDrive bike;           // 속도 FOV 효과용 (비워도 됨)
    [Tooltip("1인칭일 때 끌 3인칭 컴포넌트 (vThirdPersonCamera, InvectorCameraHelper)")]
    [SerializeField] Behaviour[] thirdPersonBehaviours;
    [SerializeField] Transform robotRoot;                // 숨길 로봇 (비우면 rider 오브젝트)

    [Header("Mode")]
    [SerializeField] bool startInFirstPerson = true;

    [Header("Look")]
    [SerializeField] float mouseSensitivity = 0.1f;
    [SerializeField] float maxYaw = 100f;
    [SerializeField] float minPitch = -35f;
    [SerializeField] float maxPitch = 40f;
    [SerializeField] float lookSmooth = 15f;
    [SerializeField] float recenterDelay = 1.5f;         // 마우스 입력 없이 이 시간 지나면
    [SerializeField] float recenterSpeed = 3f;           // 정면으로 복귀
    [Range(0, 1)][SerializeField] float leanFollow = 0.35f; // 바이크 기울기를 화면에 반영하는 비율

    [Header("FOV (Wheel)")]
    [SerializeField] float fov = 70f;
    [SerializeField] float minFov = 55f;
    [SerializeField] float maxFov = 95f;
    [SerializeField] float fovStep = 3f;
    [SerializeField] float speedFovBonus = 8f;           // 최고속도일 때 추가 FOV
    [SerializeField] float nearClip = 0.03f;

    public bool IsFirstPerson { get; private set; }

    Camera cam;
    bool preferFirstPerson;
    float yaw, pitch, smYaw, smPitch, idleTimer;
    float savedNear, savedFov;
    Renderer[] robotRenderers;
    ShadowCastingMode[] savedShadowModes;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (robotRoot == null && rider != null) robotRoot = rider.transform;
        preferFirstPerson = startInFirstPerson;
    }

    void LateUpdate()
    {
        if (rider == null || eyePoint == null) return;

        if (TogglePressed()) preferFirstPerson = !preferFirstPerson;

        bool shouldBeFP = rider.IsRiding && preferFirstPerson;
        if (shouldBeFP && !IsFirstPerson) Enter();
        else if (!shouldBeFP && IsFirstPerson) Exit();

        if (!IsFirstPerson) return;

        float dt = Time.deltaTime;
        ReadMouse(out Vector2 delta, out float scroll);

        // 둘러보기
        if (delta.sqrMagnitude > 0.0001f)
        {
            yaw = Mathf.Clamp(yaw + delta.x * mouseSensitivity, -maxYaw, maxYaw);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, minPitch, maxPitch);
            idleTimer = 0f;
        }
        else idleTimer += dt;

        if (idleTimer > recenterDelay)
        {
            float k = 1f - Mathf.Exp(-recenterSpeed * dt);
            yaw = Mathf.Lerp(yaw, 0f, k);
            pitch = Mathf.Lerp(pitch, 0f, k);
        }

        float s = 1f - Mathf.Exp(-lookSmooth * dt);
        smYaw = Mathf.Lerp(smYaw, yaw, s);
        smPitch = Mathf.Lerp(smPitch, pitch, s);

        // 휠 → FOV
        if (scroll != 0f) fov = Mathf.Clamp(fov - scroll * fovStep, minFov, maxFov);

        // 위치/회전: 눈 위치 + 바이크 진행 방향 + 기울기 일부 반영
        float baseYaw = FlatYaw(eyePoint);
        float roll = Mathf.DeltaAngle(0f, eyePoint.eulerAngles.z) * leanFollow;
        transform.SetPositionAndRotation(
            eyePoint.position,
            Quaternion.Euler(smPitch, baseYaw + smYaw, roll));

        float speed01 = 0f;
        if (bike != null && bike.BoostMaxSpeed > 0f) speed01 = Mathf.Clamp01(Mathf.Abs(bike.Speed) / bike.BoostMaxSpeed);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov + speedFovBonus * speed01, 1f - Mathf.Exp(-6f * dt));
    }

    void Enter()
    {
        IsFirstPerson = true;
        foreach (var b in thirdPersonBehaviours) if (b) b.enabled = false;

        savedNear = cam.nearClipPlane;
        savedFov = cam.fieldOfView;
        cam.nearClipPlane = nearClip;

        yaw = pitch = smYaw = smPitch = 0f;
        idleTimer = 0f;

        if (robotRoot != null)
        {
            robotRenderers = robotRoot.GetComponentsInChildren<Renderer>(true);
            savedShadowModes = new ShadowCastingMode[robotRenderers.Length];
            for (int i = 0; i < robotRenderers.Length; i++)
            {
                savedShadowModes[i] = robotRenderers[i].shadowCastingMode;
                robotRenderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly; // 몸은 안 보이고 그림자만
            }
        }
    }

    void Exit()
    {
        IsFirstPerson = false;

        if (robotRenderers != null)
        {
            for (int i = 0; i < robotRenderers.Length; i++)
                if (robotRenderers[i]) robotRenderers[i].shadowCastingMode = savedShadowModes[i];
            robotRenderers = null;
        }

        cam.nearClipPlane = savedNear;
        cam.fieldOfView = savedFov;
        foreach (var b in thirdPersonBehaviours) if (b) b.enabled = true;
    }

    static float FlatYaw(Transform t)
    {
        Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up);
        return f.sqrMagnitude < 0.0001f ? t.eulerAngles.y : Quaternion.LookRotation(f, Vector3.up).eulerAngles.y;
    }

    bool TogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.V);
#endif
    }

    void ReadMouse(out Vector2 delta, out float scroll)
    {
        float raw;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) { delta = Vector2.zero; scroll = 0f; return; }
        delta = m.delta.ReadValue();
        raw = m.scroll.ReadValue().y;
#else
        delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
        raw = Input.mouseScrollDelta.y;
#endif
        scroll = Mathf.Abs(raw) >= 20f ? raw / 120f : raw;
    }

    void OnDisable()
    {
        if (IsFirstPerson) Exit();
    }
}