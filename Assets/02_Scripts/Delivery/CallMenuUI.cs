using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 콜 메뉴 (휴대폰 UI). 실행할 때 UI를 코드로 만든다 (씬에 따로 배치할 필요 없음).
///
/// - 화면 위에 뜨는 평면 휴대폰 : 배경 영상(PhoneBG)이 휴대폰 화면 역할, 그 위에 콜 목록
/// - Q를 누르면 화면 오른쪽 아래에서 올라옴 (걷기·오토바이 상관없음)
/// - 휴대폰 크기는 영상 비율(720x1280)을 그대로 따름 → Phone Height만 바꾸면 됨
///
/// 조작
/// - Q : 열기 / 닫기 (ESC로도 닫힘)
/// - W/S, 방향키, 마우스 휠, 마우스 올리기 : 선택
/// - E, Enter, 클릭 : 수락
/// - 열려 있는 동안 오토바이·걷기·탑승·배달 상호작용 입력은 막힘, 커서 보임
/// - 닫혀 있을 때 새 콜이 오면 화면 오른쪽 위에 알림
/// </summary>
public class CallMenuUI : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("한글 TMP 폰트. 비우면 프로젝트에 로드된 폰트 중 이름에 Noto/KR이 들어간 것을 찾음")]
    [SerializeField] TMP_FontAsset font;
    [Tooltip("휴대폰 화면 영상 (Assets/07_UI/Phone/PhoneBG). 비우면 단색 휴대폰")]
    [SerializeField] VideoClip backgroundVideo;
    [Range(0f, 1f)] [SerializeField] float videoAlpha = 1f;
    [Tooltip("영상 밝기 (배경처럼 어둡게). 1 = 원본")]
    [Range(0f, 1f)] [SerializeField] float videoBrightness = 0.4f;
    [Tooltip("반복 시작 위치 (초). 영상 끝(Loop End)에 닿으면 여기로 이동. Q로 열 때는 항상 0초부터")]
    [SerializeField] float videoLoopStart = 1.07f;
    [Tooltip("반복 끝 위치 (초). 여기 닿으면 Loop Start로 이동. 0이면 영상 끝 직전")]
    [SerializeField] float videoLoopEnd = 9.95f;
    [Tooltip("(확인용) 지금 재생 위치. 플레이 중 Loop Start/End 맞출 때 참고")]
    [SerializeField] float videoTimeNow;
    [Tooltip("목록 뒤 어두운 판 / 알림 배경 / 영상이 없을 때 휴대폰 색")]
    [SerializeField] Color panelColor = new Color(0.02f, 0.06f, 0.09f, 0.92f);
    [Tooltip("목록 뒤 어두운 판 투명도 (영상이 비쳐 보이는 정도)")]
    [Range(0f, 1f)] [SerializeField] float contentDim = 0.55f;
    [SerializeField] Color accentColor = new Color(0.13f, 0.94f, 1f, 1f);   // HUD Cyan #22EFFF
    [SerializeField] Color textColor = new Color(0.86f, 0.95f, 1f, 1f);

    [Header("Phone (기준 해상도 1920x1080)")]
    [Tooltip("휴대폰 높이. 너비는 영상 비율로 자동")]
    [SerializeField] float phoneHeight = 760f;
    [Tooltip("화면 오른쪽 아래 모서리에서 떨어진 거리 (x 오른쪽, y 아래)")]
    [SerializeField] Vector2 phoneMargin = new Vector2(80f, 40f);
    [SerializeField] float slideTime = 0.25f;
    [Tooltip("영상 안에서 목록이 들어갈 영역 (영상 픽셀 기준 여백 : x 왼쪽, y 위, z 오른쪽, w 아래). 테두리 장식 피하기")]
    [SerializeField] Vector4 contentInsets = new Vector4(70f, 110f, 80f, 80f);

    [Header("Phone Body")]
    [Tooltip("휴대폰 바깥 모서리 둥글기")]
    [SerializeField] float cornerRadius = 72f;
    [Tooltip("베젤(테두리) 두께")]
    [SerializeField] float bezelWidth = 14f;
    [SerializeField] Color bezelColor = new Color(0.04f, 0.05f, 0.06f, 1f);
    [Tooltip("베젤 바깥 얇은 선 색 (투명도 0이면 없음)")]
    [SerializeField] Color bezelEdgeColor = new Color(0.13f, 0.94f, 1f, 0.35f);
    [Tooltip("화면 위쪽 가운데 카메라 구멍")]
    [SerializeField] bool cameraHole = true;

    [Header("Notification")]
    [SerializeField] float toastTime = 3.5f;
    [Tooltip("알림 위치 (오른쪽 위 기준, 미니맵과 겹치면 조정)")]
    [SerializeField] Vector2 toastOffset = new Vector2(-40f, -40f);

    public bool IsOpen { get; private set; }

    CallManager calls;
    Canvas canvas;
    RectTransform phone, content, listRoot, toastRoot;
    TMP_Text titleText, messageText, emptyText, toastText;
    VideoPlayer videoPlayer;
    RenderTexture videoRT;
    readonly List<Row> rows = new List<Row>();
    int selected;
    float messageTimer, toastTimer;
    float slide;           // 0 숨김 ~ 1 보임
    Vector2 videoSize = new Vector2(720f, 1280f);

    class Row
    {
        public DeliveryCall call;
        public RectTransform rt;
        public Image bg, timeBar;
        public TMP_Text title, detail, time;
    }

    // ---------------- 수명 ----------------
    void Start()
    {
        calls = CallManager.Instance != null ? CallManager.Instance : FindFirstObjectByType<CallManager>();
        if (calls == null) { Debug.LogWarning("[CallMenuUI] CallManager가 없어요"); enabled = false; return; }
        if (font == null) font = FindKoreanFont();
        Build();
        calls.CallAdded += OnCallAdded;
        calls.CallRemoved += OnCallRemoved;
        SetOpen(false);
        slide = 0f;
        ApplySlide();
    }

    void OnDestroy()
    {
        if (calls != null) { calls.CallAdded -= OnCallAdded; calls.CallRemoved -= OnCallRemoved; }
        if (IsOpen) { GameCursor.Release(this); GameInputBlock.Pop(this); }
        if (videoRT != null) { videoRT.Release(); Destroy(videoRT); }
    }

    void Update()
    {
        if (calls == null) return;

        if (TogglePressed()) SetOpen(!IsOpen);
        else if (IsOpen && ClosePressed()) SetOpen(false);

        if (IsOpen)
        {
            HandleNavigation();
            RefreshRows();
        }

        // 올라오기 / 내려가기
        float target = IsOpen ? 1f : 0f;
        if (!Mathf.Approximately(slide, target))
        {
            slide = Mathf.MoveTowards(slide, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, slideTime));
            ApplySlide();
        }

        if (IsOpen) CheckVideoLoop();

        if (messageTimer > 0f) { messageTimer -= Time.unscaledDeltaTime; if (messageTimer <= 0f) messageText.text = HintText; }
        if (toastTimer > 0f) { toastTimer -= Time.unscaledDeltaTime; if (toastTimer <= 0f) toastRoot.gameObject.SetActive(false); }
    }

    void ApplySlide()
    {
        bool show = slide > 0f;
        if (phone.gameObject.activeSelf != show) phone.gameObject.SetActive(show);
        if (!show)
        {
            if (videoPlayer != null && videoPlayer.isPlaying) videoPlayer.Pause();
            return;
        }
        float e = 1f - Mathf.Pow(1f - slide, 3f);   // 끝에서 부드럽게 멈춤
        float hiddenY = -phoneHeight - 20f;
        phone.anchoredPosition = new Vector2(-phoneMargin.x, Mathf.Lerp(hiddenY, phoneMargin.y, e));
    }

    // ---------------- 열기 / 닫기 ----------------
    void SetOpen(bool open)
    {
        IsOpen = open;
        if (open)
        {
            toastRoot.gameObject.SetActive(false);
            GameCursor.Request(this);
            GameInputBlock.Push(this);
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, calls.Calls.Count - 1));
            RebuildRows();
            messageText.text = HintText;
            if (videoPlayer != null)
            {
                videoPlayer.time = 0;                  // 열 때는 항상 처음부터
                nextSeekAllowed = Time.unscaledTime + 0.3f;
                videoPlayer.Play();
            }
        }
        else
        {
            GameCursor.Release(this);
            GameInputBlock.Pop(this);
            // 영상은 다 내려간 뒤 멈춤 (ApplySlide)
        }
    }

    // ---------------- 입력 ----------------
    const string HintText = "W/S 선택   E 수락   Q 닫기";

    void HandleNavigation()
    {
        int count = calls.Calls.Count;
        if (count == 0) return;
        int move = 0;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) move = -1;
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) move = 1;
            if (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) AcceptSelected();
        }
        var mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.1f) move = -1; else if (scroll < -0.1f) move = 1;
        }
#endif
        if (move != 0) selected = Mathf.Clamp(selected + move, 0, count - 1);
    }

    void AcceptSelected()
    {
        if (selected < 0 || selected >= calls.Calls.Count) return;
        var call = calls.Calls[selected];
        if (calls.TryAccept(call, out var reason))
        {
            ShowMessage($"수락 : {call.StoreName} → {call.DestinationName}", accentColor);
            SetOpen(false);
        }
        else ShowMessage(reason, new Color(1f, 0.45f, 0.4f));
    }

    static bool TogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
        return false;
#endif
    }

    static bool ClosePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return false;
#endif
    }

    void ShowMessage(string text, Color color)
    {
        messageText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
        messageTimer = 2.5f;
    }

    // ---------------- 콜 이벤트 ----------------
    void OnCallAdded(DeliveryCall c)
    {
        if (IsOpen) { RebuildRows(); return; }
        toastText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(accentColor)}>새 콜</color>  {c.StoreName} → {c.DestinationName}\n<size=80%>{c.Food?.displayName}  ·  {c.DistanceMeters:0}m  ·  ₩{c.EstimatedPay:N0}   [Q]</size>";
        toastRoot.gameObject.SetActive(true);
        toastTimer = toastTime;
    }

    void OnCallRemoved(DeliveryCall c)
    {
        if (!IsOpen) return;
        int idx = rows.FindIndex(r => r.call == c);
        if (idx >= 0 && idx < selected) selected--;
        RebuildRows();
    }

    // ---------------- 목록 ----------------
    void RebuildRows()
    {
        foreach (var r in rows) if (r.rt) Destroy(r.rt.gameObject);
        rows.Clear();
        foreach (var c in calls.Calls) rows.Add(MakeRow(c));
        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, rows.Count - 1));
        RefreshRows();
    }

    void RefreshRows()
    {
        titleText.text = $"CALLS  <size=70%><color=#{ColorUtility.ToHtmlStringRGB(accentColor)}>{calls.Calls.Count}</color></size>";
        emptyText.gameObject.SetActive(rows.Count == 0);
        var dm = DeliveryManager.Instance;
        bool busy = dm != null && dm.CurrentOrder != null;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            bool sel = i == selected;
            r.bg.color = sel ? new Color(accentColor.r, accentColor.g, accentColor.b, 0.25f) : new Color(0f, 0f, 0f, 0.45f);
            float rem = r.call.Remaining;
            r.time.text = $"{(int)rem / 60}:{(int)rem % 60:00}";
            r.timeBar.fillAmount = r.call.Remaining01;
            r.timeBar.color = r.call.Remaining01 < 0.25f ? new Color(1f, 0.45f, 0.4f) : accentColor;
            r.title.alpha = busy ? 0.5f : 1f;
        }
        if (busy && messageTimer <= 0f) messageText.text = "<color=#FFB347>진행 중인 배달이 있어요</color>\nQ 닫기";
        else if (messageTimer <= 0f) messageText.text = HintText;
    }

    Row MakeRow(DeliveryCall c)
    {
        var r = new Row { call = c };
        r.rt = NewUI("Call_" + c.Id, listRoot);
        var le = r.rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 80f;
        r.bg = r.rt.gameObject.AddComponent<Image>();   // 마우스 인식용으로도 사용

        r.title = NewText("Title", r.rt, 19f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        Place(r.title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -38), new Vector2(-62, -6));
        r.title.text = $"{c.StoreName}  →  {c.DestinationName}";

        r.detail = NewText("Detail", r.rt, 15f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        Place(r.detail.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -66), new Vector2(-12, -38));
        r.detail.color = new Color(textColor.r, textColor.g, textColor.b, 0.75f);
        r.detail.text = $"{c.Food?.displayName}  ·  {c.DistanceMeters:0}m  ·  ₩{c.EstimatedPay:N0}";

        r.time = NewText("Time", r.rt, 16f, FontStyles.Normal, TextAlignmentOptions.TopRight);
        Place(r.time.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -38), new Vector2(-10, -6));

        var barBg = NewUI("TimeBarBg", r.rt);
        Place(barBg, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 6), new Vector2(-12, 10));
        var barBgImg = barBg.gameObject.AddComponent<Image>();
        barBgImg.color = new Color(1f, 1f, 1f, 0.08f);
        barBgImg.raycastTarget = false;
        var bar = NewUI("TimeBar", barBg);
        Stretch(bar);
        r.timeBar = bar.gameObject.AddComponent<Image>();
        r.timeBar.type = Image.Type.Filled;
        r.timeBar.fillMethod = Image.FillMethod.Horizontal;
        r.timeBar.sprite = WhiteSprite();
        r.timeBar.raycastTarget = false;

        var hover = r.rt.gameObject.AddComponent<CallRowPointer>();
        hover.onEnter = () => selected = rows.IndexOf(r);
        hover.onClick = () => { selected = rows.IndexOf(r); AcceptSelected(); };
        return r;
    }

    // ---------------- UI 만들기 ----------------
    void Build()
    {
        canvas = NewCanvas("CallMenuCanvas (auto)", transform);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        if (backgroundVideo != null && backgroundVideo.width > 0 && backgroundVideo.height > 0)
            videoSize = new Vector2(backgroundVideo.width, backgroundVideo.height);

        // 휴대폰 (오른쪽 아래 기준, 영상 비율 그대로)
        phone = NewUI("CallPhone", canvas.transform);
        phone.anchorMin = phone.anchorMax = phone.pivot = new Vector2(1f, 0f);
        float bz = Mathf.Max(0f, bezelWidth);
        float screenH = phoneHeight - bz * 2f;
        float screenW = screenH * videoSize.x / videoSize.y;
        phone.sizeDelta = new Vector2(screenW + bz * 2f, phoneHeight);

        float outerR = Mathf.Max(1f, cornerRadius);
        float innerR = Mathf.Max(1f, outerR - bz);

        // 화면 (둥근 마스크 안에 영상 + 목록)
        var screen = NewUI("Screen", phone);
        Stretch(screen);
        screen.offsetMin = new Vector2(bz, bz);
        screen.offsetMax = new Vector2(-bz, -bz);
        var screenImg = screen.gameObject.AddComponent<Image>();
        screenImg.sprite = RoundedSprite(innerR, 0f);
        screenImg.type = Image.Type.Sliced;
        screenImg.color = backgroundVideo != null ? Color.black : panelColor;
        screenImg.raycastTarget = false;
        if (backgroundVideo != null) screen.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        if (backgroundVideo != null) BuildVideo(screen);

        // 목록 영역 (영상 테두리 안쪽, 화면 크기에 비례)
        content = NewUI("Content", screen);
        content.anchorMin = new Vector2(contentInsets.x / videoSize.x, contentInsets.w / videoSize.y);
        content.anchorMax = new Vector2(1f - contentInsets.z / videoSize.x, 1f - contentInsets.y / videoSize.y);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var dim = content.gameObject.AddComponent<Image>();
        dim.color = new Color(panelColor.r, panelColor.g, panelColor.b, contentDim);
        dim.raycastTarget = false;
        content.gameObject.AddComponent<RectMask2D>();

        BuildContent();

        if (cameraHole)
        {
            var hole = NewUI("CameraHole", screen);
            hole.anchorMin = hole.anchorMax = hole.pivot = new Vector2(0.5f, 1f);
            hole.sizeDelta = new Vector2(14f, 14f);
            hole.anchoredPosition = new Vector2(0f, -12f);
            var holeImg = hole.gameObject.AddComponent<Image>();
            holeImg.sprite = RoundedSprite(7f, 0f);
            holeImg.type = Image.Type.Sliced;
            holeImg.color = Color.black;
            holeImg.raycastTarget = false;
        }

        // 베젤 : 화면 위에 덮는 둥근 테두리 (안쪽 경계도 부드럽게 → 화면 모서리 각짐 가림)
        var bezel = NewUI("Bezel", phone);
        Stretch(bezel);
        var bezelImg = bezel.gameObject.AddComponent<Image>();
        bezelImg.sprite = FrameSprite(outerR, bz);
        bezelImg.type = Image.Type.Sliced;
        bezelImg.color = bezelColor;
        bezelImg.raycastTarget = false;
        if (bezelEdgeColor.a > 0f)
        {
            var edge = NewUI("BezelEdge", phone);
            Stretch(edge);
            var edgeImg = edge.gameObject.AddComponent<Image>();
            edgeImg.sprite = FrameSprite(outerR, 2f);
            edgeImg.type = Image.Type.Sliced;
            edgeImg.color = bezelEdgeColor;
            edgeImg.raycastTarget = false;
        }

        // 새 콜 알림 (오른쪽 위)
        toastRoot = NewUI("CallToast", canvas.transform);
        toastRoot.anchorMin = toastRoot.anchorMax = toastRoot.pivot = new Vector2(1f, 1f);
        toastRoot.sizeDelta = new Vector2(440f, 78f);
        toastRoot.anchoredPosition = toastOffset;
        toastRoot.gameObject.AddComponent<Image>().color = panelColor;
        var tOutline = toastRoot.gameObject.AddComponent<Outline>();
        tOutline.effectColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.6f);
        toastText = NewText("Text", toastRoot, 20f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Place(toastText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-16, -6));
        toastRoot.gameObject.SetActive(false);

        if (FindFirstObjectByType<EventSystem>() == null)
            Debug.LogWarning("[CallMenuUI] EventSystem이 없어서 마우스 선택이 안 돼요 (키보드는 동작)");
    }

    // 목록·제목·안내
    void BuildContent()
    {
        titleText = NewText("Title", content, 28f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        Place(titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -50), new Vector2(-14, -10));

        var line = NewUI("Line", content);
        Place(line, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -54), new Vector2(-14, -52));
        var lineImg = line.gameObject.AddComponent<Image>();
        lineImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.7f);
        lineImg.raycastTarget = false;

        listRoot = NewUI("List", content);
        Place(listRoot, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 70), new Vector2(-10, -64));
        var vlg = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        emptyText = NewText("Empty", content, 18f, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(emptyText.rectTransform, new Vector2(0, 0.4f), new Vector2(1, 0.6f), Vector2.zero, Vector2.zero);
        emptyText.text = "들어온 콜이 없어요";
        emptyText.alpha = 0.6f;

        messageText = NewText("Message", content, 16f, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(messageText.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(10, 10), new Vector2(-10, 64));
        messageText.textWrappingMode = TextWrappingModes.Normal;
        messageText.text = HintText;
    }

    // 휴대폰 전체 = 영상 (휴대폰 비율이 영상 비율과 같아서 그대로 채움)
    void BuildVideo(Transform screen)
    {
        var vr = NewUI("PhoneBG", screen);
        Stretch(vr);
        var img = vr.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        float b = videoBrightness;
        img.color = new Color(b, b, b, videoAlpha);

        videoRT = new RenderTexture((int)videoSize.x, (int)videoSize.y, 0, RenderTextureFormat.ARGB32) { name = "CallPhoneVideoRT" };
        videoRT.Create();
        img.texture = videoRT;

        // VideoPlayer는 꺼지지 않는 오브젝트에 둠 (휴대폰이 숨으면 비활성화되어 재생이 안 되기 때문)
        var vpGO = new GameObject("PhoneBG VideoPlayer (auto)");
        vpGO.transform.SetParent(transform, false);
        videoPlayer = vpGO.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;                // 멈추지 않게 기본 반복은 켜두고, 끝 근처에서 videoLoopStart로 이동
        videoPlayer.loopPointReached += _ => SeekToLoopStart();
        videoPlayer.clip = backgroundVideo;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoRT;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
        videoPlayer.Prepare();   // 처음 열 때 검은 화면 줄이기
    }

    // 영상 끝 근처에 오면 videoLoopStart로 이동 (재생 중에 옮겨서 멈추지 않음)
    float nextSeekAllowed;
    void CheckVideoLoop()
    {
        var vp = videoPlayer;
        if (vp == null) return;
        videoTimeNow = (float)vp.time;
        if (!vp.isPlaying || Time.unscaledTime < nextSeekAllowed) return;
        double len = vp.length;
        if (len <= 0.5) return;
        double end = videoLoopEnd > 0f ? System.Math.Min(videoLoopEnd, len - 0.04) : len - 0.1;
        if (vp.time >= end) SeekToLoopStart();
    }

    void SeekToLoopStart()
    {
        var vp = videoPlayer;
        if (vp == null) return;
        double len = vp.length;
        double t = Mathf.Max(0f, videoLoopStart);
        if (len > 0.5) t = System.Math.Min(t, len - 0.5);
        vp.time = t;
        nextSeekAllowed = Time.unscaledTime + 0.5f;   // 이동이 끝나기 전 중복 방지
    }

    Canvas NewCanvas(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.AddComponent<Canvas>();
    }

    TMP_Text NewText(string name, Transform parent, float size, FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = textColor;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    static Sprite whiteSprite;
    static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }

    // 둥근 사각형 스프라이트 (9-slice). ring > 0 이면 테두리 선만
    static readonly Dictionary<string, Sprite> roundCache = new Dictionary<string, Sprite>();
    static Sprite RoundedSprite(float radius, float ring)
    {
        int r = Mathf.Max(1, Mathf.CeilToInt(radius));
        string key = r + "_" + ring;
        if (roundCache.TryGetValue(key, out var cached) && cached != null) return cached;

        int size = r * 2 + 2;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Rounded_" + key };
        var px = new Color32[size * size];
        float c = size * 0.5f;
        float inner = c - r;   // 가운데 직선 구간 절반
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - c) - inner, 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - c) - inner, 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);          // 둥근 모서리 중심으로부터 거리
                float a = Mathf.Clamp01(r - d + 0.5f);              // 바깥 경계 부드럽게
                if (ring > 0f) a *= Mathf.Clamp01(d - (r - ring) + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        roundCache[key] = sp;
        return sp;
    }

    // 둥근 테두리 스프라이트 (9-slice) : 바깥 반지름 radius, 두께 width, 안쪽 반지름 radius - width. 안·밖 경계 모두 부드럽게
    static Sprite FrameSprite(float radius, float width)
    {
        int r = Mathf.Max(1, Mathf.CeilToInt(radius));
        float w = Mathf.Clamp(width, 0.5f, r);
        float ri = Mathf.Max(0.5f, r - w);
        string key = "F" + r + "_" + w;
        if (roundCache.TryGetValue(key, out var cached) && cached != null) return cached;

        int size = r * 2 + 2;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Frame_" + key };
        var px = new Color32[size * size];
        float c = size * 0.5f;
        float inner = c - r;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float ax = Mathf.Abs(x + 0.5f - c), ay = Mathf.Abs(y + 0.5f - c);
                // 바깥 둥근 사각형
                float dx = Mathf.Max(ax - inner, 0f), dy = Mathf.Max(ay - inner, 0f);
                float outerA = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                // 안쪽 둥근 사각형 (w만큼 들어간 곳, 반지름 ri)
                float half = c - w;                 // 안쪽 사각형 절반 크기
                float idx = Mathf.Max(ax - (half - ri), 0f), idy = Mathf.Max(ay - (half - ri), 0f);
                float innerA = Mathf.Clamp01(ri - Mathf.Sqrt(idx * idx + idy * idy) + 0.5f);
                float a = outerA * (1f - innerA);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        roundCache[key] = sp;
        return sp;
    }

    static TMP_FontAsset FindKoreanFont()
    {
        var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return all.FirstOrDefault(f => f.name.Contains("Noto") || f.name.Contains("KR") || f.name.Contains("Korean"))
            ?? TMP_Settings.defaultFontAsset;
    }
}

/// <summary>콜 목록 한 줄의 마우스 처리 (올리면 선택, 클릭하면 수락)</summary>
public class CallRowPointer : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public System.Action onEnter, onClick;
    public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
    public void OnPointerClick(PointerEventData e) => onClick?.Invoke();
}
