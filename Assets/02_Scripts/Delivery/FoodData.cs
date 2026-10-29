using UnityEngine;

/// <summary>
/// 음식 한 종류의 고정 데이터. Project 창 우클릭 → Create → NeonRider → Food
/// </summary>
[CreateAssetMenu(menuName = "NeonRider/Food", fileName = "Food_")]
public class FoodData : ScriptableObject
{
    public string displayName = "피자탕수육";

    [Header("신선도 (시간)")]
    [Tooltip("픽업 후 1분마다 깎이는 신선도 점수 (5점 만점). 0.5면 8분 뒤 1점")]
    public float freshnessLossPerMinute = 0.5f;

    [Header("형태 (충격)")]
    [Tooltip("충격에 약한 정도. 1 = 보통, 2 = 두 배로 잘 망가짐 (국물/케이크), 0.5 = 튼튼함 (빵)")]
    public float impactSensitivity = 1f;
}
