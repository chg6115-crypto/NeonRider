using UnityEngine;

/// <summary>
/// 음식 상태 계산 (Unity 오브젝트에 의존하지 않는 순수 클래스 → 나중에 EditMode 테스트 대상)
/// 신선도 5 + 형태 5 = 10점. 둘 중 하나라도 1점 이하면 실패.
/// </summary>
public class FoodCondition
{
    public const float MaxPoints = 5f;
    public const float FailPoints = 1f;

    public float Freshness { get; private set; } = MaxPoints;
    public float Shape { get; private set; } = MaxPoints;

    public float Total => Freshness + Shape;
    public bool IsFailed => Freshness <= FailPoints || Shape <= FailPoints;

    /// <summary>시간 경과. minutes = 지난 시간(분)</summary>
    public void Tick(float minutes, float lossPerMinute)
    {
        Freshness = Mathf.Max(0f, Freshness - Mathf.Max(0f, minutes) * lossPerMinute);
    }

    /// <summary>
    /// 충격. impact = 지면을 향한 속도(m/s).
    /// threshold 미만은 무시, 넘는 만큼 pointsPerMps 비율로 감점 × 음식 민감도
    /// </summary>
    /// <returns>깎인 점수</returns>
    public float ApplyImpact(float impact, float threshold, float pointsPerMps, float sensitivity)
    {
        float over = impact - threshold;
        if (over <= 0f) return 0f;
        float loss = over * pointsPerMps * Mathf.Max(0f, sensitivity);
        float before = Shape;
        Shape = Mathf.Max(0f, Shape - loss);
        return before - Shape;
    }

    /// <summary>표시/판정용 정수 점수 (반올림, 0~5)</summary>
    public static int ToPoints(float v) => Mathf.Clamp(Mathf.RoundToInt(v), 0, (int)MaxPoints);
}
