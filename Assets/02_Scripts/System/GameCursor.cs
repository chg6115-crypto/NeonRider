using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 마우스 커서 관리. GameManager 같은 곳에 하나 붙여 둠.
/// - 플레이 중에는 커서를 숨기고 화면 가운데 고정 (카메라 회전용)
/// - 메뉴(콜 메뉴, ESC 메뉴 등)가 열릴 때 GameCursor.Request(this) → 커서 보임
///   닫을 때 GameCursor.Release(this) → 다시 숨김. 여러 메뉴가 동시에 열려도 안전
/// - 에디터에서 Left Alt를 누르고 있는 동안은 커서가 잠깐 보임 (Inspector 조작용)
/// </summary>
public class GameCursor : MonoBehaviour
{
    [Tooltip("플레이 시작 시 커서를 숨기고 고정")]
    [SerializeField] bool lockOnStart = true;
    [Tooltip("에디터에서 이 키를 누르는 동안 커서 표시")]
    [SerializeField] bool altShowsCursorInEditor = true;

    static readonly HashSet<object> requests = new HashSet<object>();
    static GameCursor instance;

    /// <summary>메뉴 등이 커서를 필요로 할 때</summary>
    public static void Request(object owner) { if (owner != null) requests.Add(owner); Apply(); }
    /// <summary>필요 없어졌을 때</summary>
    public static void Release(object owner) { if (owner != null) requests.Remove(owner); Apply(); }
    public static bool IsFree => requests.Count > 0;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        requests.Clear();
    }

    void Start() { if (lockOnStart) Apply(); }

    void OnApplicationFocus(bool focus) { if (focus) Apply(); }

    void Update()
    {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        if (altShowsCursorInEditor && Keyboard.current != null)
        {
            var alt = Keyboard.current.leftAltKey;
            if (alt.wasPressedThisFrame) SetCursor(true);
            else if (alt.wasReleasedThisFrame) Apply();
        }
#endif
    }

    void OnDestroy()
    {
        if (instance == this) { instance = null; requests.Clear(); SetCursor(true); }
    }

    static void Apply()
    {
        if (instance == null) return;
        SetCursor(requests.Count > 0 || !instance.lockOnStart);
    }

    static void SetCursor(bool visible)
    {
        Cursor.visible = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
    }
}
