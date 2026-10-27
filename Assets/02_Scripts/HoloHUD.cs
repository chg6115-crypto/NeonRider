using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 홀로그램 HUD (월드 스페이스 캔버스). 탑승 중(기본: 1인칭일 때만) 표시.
/// - LEAN: 원호 위 하얀 바늘이 기울기만큼 회전 + "LEAN 12° R" 글자
/// - 속도: 큰 숫자 + 아래 9-slice 바 (가로 길이로 표시)
/// - 상태 색: 평소 하늘색 / BOOST 분홍 / BRAKE 빨강 (tintTargets에만 적용, 바늘·숫자는 흰색 유지)
/// - 홀로그램 효과: 켜질 때 세로로 펼쳐지며 깜빡임, 미세 깜빡임/흔들림
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class HoloHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] MotorcycleTestDrive bike;            // 비우면 부모에서 찾음
    [SerializeField] RobotRider rider;                    // 비우면 씬에서 찾음
    [SerializeField] RideFirstPersonCamera fpCamera;      // 비우면 씬에서 찾음
    [SerializeField] bool onlyInFirstPerson = true;

    [Header("Speed")]
    [SerializeField] TMP_Text speedText;
    [SerializeField] TMP_Text unitText;                   // (구버전 호환) 비워도 됨
    [Tooltip("속도 숫자 뒤에 붙는 단위 (TMP 리치텍스트)")]
    [SerializeField] string unitSuffix = "<size=34%><color=#5CF2FF> km/h</color></size>";
    [SerializeField] RectTransform speedBarFill;          // 9-slice 채움 (부모 폭 기준 비율로 길이 조절)
    [SerializeField] RectTransform speedBarFillGlow;
    [Tooltip("9-slice 양 끝이 찌그러지지 않는 최소 폭 (캔버스 px)")]
    [SerializeField] float barMinWidth = 58f;
    [SerializeField] Image speedBar;                      // (구버전 호환) Filled Image, 비워도 됨

    [Header("Lean")]
    [SerializeField] TMP_Text leanText;
    [SerializeField] RectTransform leanNeedlePivot;       // 원호 중심에 있는 회전축
    [Tooltip("바늘이 움직이는 최대 각도 (원호 끝 ±19° 안쪽)")]
    [SerializeField] float needleRange = 16f;
    [Tooltip("오토바이가 이 각도만큼 기울면 바늘이 끝까지 감")]
    [SerializeField] float leanForFullNeedle = 35f;
    [SerializeField] float needleSmooth = 12f;
    [SerializeField] bool invertNeedle = false;

    [Header("Status (선택)")]
    [SerializeField] TMP_Text statusText;

    [Header("Tint")]
    [SerializeField] Graphic[] tintTargets;
    [SerializeField] Color normalColor = new Color(0.35f, 0.95f, 1f);
    [SerializeField] Color boostColor = new Color(1f, 0.35f, 0.9f);
    [SerializeField] Color brakeColor = new Color(1f, 0.3f, 0.25f);
    [SerializeField] float colorSmooth = 8f;

    [Header("Holo FX")]
    [SerializeField] float fadeSpeed = 5f;
    [SerializeField] float bootTime = 0.35f;
    [SerializeField] float flickerAmount = 0.12f;
    [SerializeField] float flickerSpeed = 18f;
    [SerializeField] RawImage scanlines;                  // (선택) 비워도 됨
    [SerializeField] float scanlineSpeed = 0.6f;
    [SerializeField] float scanlineTiling = 40f;
    [SerializeField] float jitterAmount = 0.002f;
    [SerializeField] float jitterChance = 0.03f;

    CanvasGroup group;
    Vector3 basePos, baseScale;
    float vis, boot, scanOffset, needleAngle;
    Color currentColor;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        basePos = transform.localPosition;
        baseScale = transform.localScale;
        currentColor = normalColor;

        if (bike == null) bike = GetComponentInParent<MotorcycleTestDrive>();
        if (rider == null) rider = FindFirstObjectByType<RobotRider>();
        if (fpCamera == null) fpCamera = FindFirstObjectByType<RideFirstPersonCamera>();

        if (scanlines != null && scanlines.texture == null) scanlines.texture = MakeScanlineTexture();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        bool show = rider != null && rider.IsRiding
                    && (!onlyInFirstPerson || fpCamera == null || fpCamera.IsFirstPerson);

        // ---- 표시/홀로그램 효과 ----
        vis = Mathf.MoveTowards(vis, show ? 1f : 0f, fadeSpeed * dt);
        boot = show ? Mathf.Min(1f, boot + dt / Mathf.Max(0.01f, bootTime)) : 0f;

        float bootFlicker = boot < 1f ? (Random.value > 0.35f ? 1f : 0.2f) : 1f;
        float noise = 1f - flickerAmount * Mathf.PerlinNoise(Time.time * flickerSpeed, 0.37f);
        group.alpha = vis * noise * bootFlicker;

        float open = Mathf.SmoothStep(0.05f, 1f, boot);
        transform.localScale = new Vector3(baseScale.x, baseScale.y * open, baseScale.z);

        transform.localPosition = (Random.value < jitterChance)
            ? basePos + (Vector3)(Random.insideUnitCircle * jitterAmount)
            : basePos;

        if (group.alpha <= 0.001f || bike == null) return;

        // ---- 속도 ----
        float kmh = Mathf.Abs(bike.SpeedKmh);
        if (speedText) speedText.text = Mathf.RoundToInt(kmh) + unitSuffix;

        float t = bike.BoostMaxSpeed > 0f ? Mathf.Clamp01(Mathf.Abs(bike.Speed) / bike.BoostMaxSpeed) : 0f;
        SetBar(speedBarFill, t);
        SetBar(speedBarFillGlow, t);
        if (speedBar) speedBar.fillAmount = t;

        // ---- 기울기 ----
        float lean = bike.LeanAngle;   // + = 오른쪽(D)
        float lean01 = Mathf.Clamp(lean / Mathf.Max(1f, leanForFullNeedle), -1f, 1f);
        float targetAngle = lean01 * needleRange * (invertNeedle ? 1f : -1f); // 오른쪽 = 시계방향(-Z)
        needleAngle = Mathf.Lerp(needleAngle, targetAngle, 1f - Mathf.Exp(-needleSmooth * dt));
        if (leanNeedlePivot) leanNeedlePivot.localRotation = Quaternion.Euler(0f, 0f, needleAngle);

        if (leanText)
        {
            float a = Mathf.Abs(lean);
            string side = a < 0.5f ? "" : (lean > 0f ? " R" : " L");
            leanText.text = $"LEAN {a:0}°{side}";
        }

        // ---- 상태 색 ----
        Color target = normalColor;
        string status = "";
        if (bike.IsBraking) { target = brakeColor; status = "BRAKE"; }
        else if (bike.IsBoosting) { target = boostColor; status = "BOOST"; }
        if (statusText) statusText.text = status;

        currentColor = Color.Lerp(currentColor, target, 1f - Mathf.Exp(-colorSmooth * dt));
        if (tintTargets != null)
        {
            foreach (var g in tintTargets)
            {
                if (g == null) continue;
                var c = currentColor; c.a = g.color.a;   // 알파는 각 요소 값 유지
                g.color = c;
            }
        }

        if (scanlines)
        {
            scanOffset = (scanOffset + scanlineSpeed * dt) % 1f;
            scanlines.uvRect = new Rect(0f, scanOffset, 1f, scanlineTiling);
        }
    }

    // 9-slice 바: 부모 폭 × 비율로 길이 조절. 최소 폭 이하에선 가로 스케일로 줄여 끝 모양 유지
    void SetBar(RectTransform r, float t)
    {
        if (r == null || r.parent == null) return;
        float full = ((RectTransform)r.parent).rect.width;
        float w = full * t;
        if (w >= barMinWidth)
        {
            r.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            r.localScale = Vector3.one;
        }
        else
        {
            r.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, barMinWidth);
            r.localScale = new Vector3(barMinWidth > 0f ? w / barMinWidth : 0f, 1f, 1f);
        }
    }

    static Texture2D MakeScanlineTexture()
    {
        var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "HoloScanlines"
        };
        tex.SetPixels(new[] { Color.white, new Color(1, 1, 1, 0.25f), Color.clear, Color.clear });
        tex.Apply();
        return tex;
    }
}