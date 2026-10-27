using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Invector vThirdPersonCamera 보조 (같은 오브젝트에 붙임)
/// - 휠: 확대/축소 (걷기/탑승 모두)
/// - 탑승 중: LateUpdate 모드로 전환(끊김 방지), 마우스로 자유 회전 (걷기와 동일)
///            마우스 입력이 없고 달리는 중이면 일정 시간 후 오토바이 뒤로 자동 복귀
/// </summary>
[RequireComponent(typeof(vThirdPersonCamera))]
public class InvectorCameraHelper : MonoBehaviour
{
    [SerializeField] vThirdPersonCamera tpCamera;   // 비워두면 자동
    [SerializeField] RobotRider rider;              // 로봇의 RobotRider
    [SerializeField] MotorcycleTestDrive bike;      // 자동 복귀 판단용 (비워도 됨)

    [Header("Zoom (Wheel)")]
    [SerializeField] float minDistance = 1.5f;
    [SerializeField] float maxDistance = 12f;
    [SerializeField] float zoomStep = 0.6f;
    [SerializeField] bool invertZoom = false;

    [Header("Riding")]
    [SerializeField] float rideHeight = 2.2f;        // 탑승 중 카메라 높이
    [SerializeField] float rideExtraDistance = 1.5f; // 탑승 중 거리 추가
    [Tooltip("걷기 때 vThirdPersonInput의 Mouse Sensitivity와 같게 맞추면 감도가 동일")]
    [SerializeField] float rideMouseSensitivity = 0.1f;

    [Header("Auto Recenter (Riding)")]
    [SerializeField] bool autoRecenter = true;
    [SerializeField] float recenterDelay = 2f;       // 마우스 입력 없이 이 시간 지나면
    [SerializeField] float recenterMinSpeed = 2f;    // 이 속도(m/s) 이상 달릴 때만

    float walkHeight;
    float zoomDistance;
    bool wasRiding;
    float idleTimer;

    void Awake()
    {
        if (tpCamera == null) tpCamera = GetComponent<vThirdPersonCamera>();
    }

    void Start()
    {
        walkHeight = tpCamera.height;
        zoomDistance = tpCamera.defaultDistance;
    }

    void Update()
    {
        ReadMouse(out Vector2 delta, out float scroll);

        // 휠 줌
        if (scroll != 0f)
        {
            if (invertZoom) scroll = -scroll;
            zoomDistance = Mathf.Clamp(zoomDistance - scroll * zoomStep, minDistance, maxDistance);
        }

        bool riding = rider != null && rider.IsRiding;

        if (riding)
        {
            tpCamera.updateInLateUpdate = true;
            tpCamera.height = rideHeight;
            tpCamera.defaultDistance = zoomDistance + rideExtraDistance;

            bool mouseMoved = delta.sqrMagnitude > 0.01f;
            idleTimer = mouseMoved ? 0f : idleTimer + Time.deltaTime;

            float speed = bike != null ? Mathf.Abs(bike.Speed) : 0f;
            bool recenter = autoRecenter && !mouseMoved && idleTimer > recenterDelay
                            && (bike == null || speed > recenterMinSpeed);

            if (recenter)
            {
                // 오토바이 뒤로 (카메라 자체 Slerp로 부드럽게 돌아감)
                tpCamera.lockCamera = true;
                tpCamera.RotateCamera(0f, 0f);
            }
            else
            {
                // 걷기와 동일한 자유 회전 (탑승 중엔 vThirdPersonInput이 꺼져 있어 직접 전달)
                tpCamera.lockCamera = false;
                tpCamera.RotateCamera(delta.x * rideMouseSensitivity, delta.y * rideMouseSensitivity);
            }
        }
        else
        {
            if (wasRiding)
            {
                tpCamera.lockCamera = false;
                tpCamera.updateInLateUpdate = false;
                tpCamera.height = walkHeight;
            }
            tpCamera.defaultDistance = zoomDistance;
            idleTimer = 0f;
        }

        wasRiding = riding;
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
}