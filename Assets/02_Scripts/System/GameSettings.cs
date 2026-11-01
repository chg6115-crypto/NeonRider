using System;
using UnityEngine;

/// <summary>
/// 게임 전체 설정 (현재 : 프레임 제한)
/// - 기본 60fps 고정. VSync는 끄고 Application.targetFrameRate로 제한
/// - 씬에 오브젝트가 없어도 게임 시작 시 자동으로 60fps 적용 (RuntimeInitializeOnLoadMethod)
/// - GameManager에 붙이면 Inspector에서 기본값을 바꿀 수 있음
/// - 나중에 설정 메뉴에서 GameSettings.SetFrameRate(120) 호출 → 적용 + 저장 (PlayerPrefs)
/// 물리(Fixed Timestep 0.02)는 프레임과 따로 돌아서 60/120 어느 쪽이든 주행 물리는 같음
/// </summary>
[DefaultExecutionOrder(-1000)]
public class GameSettings : MonoBehaviour
{
    public const int DefaultFrameRate = 60;
    const string FrameRateKey = "Settings.TargetFrameRate";

    /// <summary>설정 메뉴에서 고를 수 있는 프레임 (나중에 추가할 옵션)</summary>
    public static readonly int[] FrameRateOptions = { 60, 120 };

    [Tooltip("기본 프레임 제한")]
    [SerializeField] int frameRate = DefaultFrameRate;
    [Tooltip("설정 메뉴에서 저장한 값이 있으면 그 값을 사용. 끄면 항상 위 기본값 사용 (테스트용)")]
    [SerializeField] bool useSavedValue = true;

    public static int TargetFrameRate { get; private set; } = DefaultFrameRate;
    public static event Action<int> FrameRateChanged;

    static GameSettings instance;

    // 씬에 GameSettings가 없어도 적용되도록, 첫 씬 로드 전에 저장값(없으면 60) 적용
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InitOnLoad()
    {
        Apply(PlayerPrefs.GetInt(FrameRateKey, DefaultFrameRate), false);
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;

        int fps = useSavedValue && PlayerPrefs.HasKey(FrameRateKey)
            ? PlayerPrefs.GetInt(FrameRateKey)
            : frameRate;
        Apply(fps, false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>프레임 제한 변경 + 저장 (설정 메뉴용)</summary>
    public static void SetFrameRate(int fps) => Apply(fps, true);

    /// <summary>저장값 지우고 기본값(60)으로</summary>
    public static void ResetFrameRate()
    {
        PlayerPrefs.DeleteKey(FrameRateKey);
        Apply(instance != null ? instance.frameRate : DefaultFrameRate, false);
    }

    static void Apply(int fps, bool save)
    {
        fps = Mathf.Clamp(fps, 30, 240);

        QualitySettings.vSyncCount = 0;          // VSync가 켜져 있으면 targetFrameRate가 무시됨
        Application.targetFrameRate = fps;
        TargetFrameRate = fps;

        if (save)
        {
            PlayerPrefs.SetInt(FrameRateKey, fps);
            PlayerPrefs.Save();
        }
        FrameRateChanged?.Invoke(fps);
    }
}
