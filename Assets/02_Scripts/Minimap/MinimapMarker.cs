using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지도에 아이콘으로 표시할 대상 (가게, 배달 목적지 등). 오브젝트에 붙이면 자동 등록.
/// 배달 루프에서는 DeliveryManager가 목적지의 마커만 켜고 끄면 됨.
/// </summary>
public class MinimapMarker : MonoBehaviour
{
    public static readonly List<MinimapMarker> All = new List<MinimapMarker>();

    public Sprite icon;                                   // 비우면 MinimapUI의 기본 마커
    public Color color = new Color(1f, 0.6f, 0.15f);
    [Tooltip("작은 지도 범위 밖이면 테두리에 붙여서 방향 표시")]
    public bool clampToEdge = true;
    public bool visible = true;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }
}
