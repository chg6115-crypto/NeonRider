using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지도에 글자로 표시할 지명 (예: 신관초 교차로). 그 위치에 빈 오브젝트를 만들고 붙이면 됨.
/// 지도가 회전해도 글자는 항상 똑바로 서 있고, 위치만 따라감.
/// </summary>
public class MinimapLabel : MonoBehaviour
{
    public static readonly List<MinimapLabel> All = new List<MinimapLabel>();

    [TextArea] public string text = "신관초 교차로";
    [Tooltip("글자 크기 배율 (큰 도로/주요 지점은 1.2 등)")]
    public float sizeScale = 1f;
    public bool showOnSmall = true;
    public bool showOnBig = true;
    [Tooltip("작은 지도 보기 범위가 이 값(m)보다 넓어지면 숨김. 0 = 항상 표시 (속도가 빨라져 축소되면 작은 지명은 숨기기용)")]
    public float hideWhenViewOver = 0f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 2f);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, text);
    }
#endif
}
