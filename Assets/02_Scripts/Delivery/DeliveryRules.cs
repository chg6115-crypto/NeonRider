using System;
using UnityEngine;

public enum DeliveryGrade { Perfect, Great, Good, Fine, Poor, Failed }

/// <summary>
/// 지급액/등급 규칙. DeliveryManager Inspector에서 값 조절
/// </summary>
[Serializable]
public class DeliveryRules
{
    [Header("지급액")]
    [Tooltip("기본 배달비 (원)")]
    public int basePay = 2000;
    [Tooltip("직선거리 100m당 추가 (원)")]
    public int payPer100m = 100;

    [Header("점수 보너스 배율")]
    public float bonusAt9 = 1.2f;   // 9점 이상
    public float bonusAt8 = 1.1f;   // 8점 이상

    [Header("충격")]
    [Tooltip("이 속도(m/s) 미만의 착지 충격은 무시")]
    public float impactThreshold = 3f;
    [Tooltip("임계값을 1m/s 넘을 때마다 깎이는 형태 점수")]
    public float shapeLossPerMps = 0.35f;

    public static DeliveryGrade GradeOf(FoodCondition c)
    {
        if (c.IsFailed) return DeliveryGrade.Failed;
        int total = FoodCondition.ToPoints(c.Freshness) + FoodCondition.ToPoints(c.Shape);
        if (total >= 10) return DeliveryGrade.Perfect;
        if (total >= 9) return DeliveryGrade.Great;
        if (total >= 8) return DeliveryGrade.Good;
        if (total >= 7) return DeliveryGrade.Fine;
        return DeliveryGrade.Poor;
    }

    public int BaseFor(float distanceMeters)
    {
        int steps = Mathf.FloorToInt(Mathf.Max(0f, distanceMeters) / 100f);
        return basePay + steps * payPer100m;
    }

    public int PayFor(float distanceMeters, DeliveryGrade grade)
    {
        if (grade == DeliveryGrade.Failed) return 0;
        float mul = 1f;
        if (grade == DeliveryGrade.Perfect || grade == DeliveryGrade.Great) mul = bonusAt9;
        else if (grade == DeliveryGrade.Good) mul = bonusAt8;
        return Mathf.RoundToInt(BaseFor(distanceMeters) * mul);
    }

    public static string ReactionOf(DeliveryGrade g)
    {
        switch (g)
        {
            case DeliveryGrade.Perfect: return "와, 방금 만든 것 같아요! 완벽해요!";
            case DeliveryGrade.Great: return "따끈하고 멀쩡하네요. 감사합니다!";
            case DeliveryGrade.Good: return "잘 받았어요~";
            case DeliveryGrade.Fine: return "음... 괜찮네요.";
            case DeliveryGrade.Poor: return "좀 식었고... 모양이 좀 그렇네요.";
            default: return "이건 못 먹겠어요. 환불해 주세요.";
        }
    }
}
