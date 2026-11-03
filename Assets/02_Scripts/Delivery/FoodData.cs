using UnityEngine;

/// <summary>
/// 음식 영향 단계. 수치는 FoodSensitivityTable에 고정 (음식마다 따로 숫자를 넣지 않음)
/// </summary>
public enum FoodSensitivity { Low = 0, Normal = 1, High = 2, VeryHigh = 3 }

/// <summary>
/// 영향 단계별 고정 수치. 밸런스 기준이므로 바꿀 때는 모든 음식에 같이 적용됨
///
///  단계      | 신선도 감소 (분당, 5점 만점) | 1점까지     | 형태 충격 배율
///  낮음      | 0.25                        | 16분        | 0.5
///  보통      | 0.5                         | 8분         | 1.0
///  높음      | 0.75                        | 약 5분 20초 | 1.5
///  매우높음  | 1.0                         | 4분         | 2.0
/// </summary>
public static class FoodSensitivityTable
{
    public static float FreshnessLossPerMinute(FoodSensitivity s)
    {
        switch (s)
        {
            case FoodSensitivity.Low: return 0.25f;
            case FoodSensitivity.High: return 0.75f;
            case FoodSensitivity.VeryHigh: return 1f;
            default: return 0.5f;
        }
    }

    public static float ImpactMultiplier(FoodSensitivity s)
    {
        switch (s)
        {
            case FoodSensitivity.Low: return 0.5f;
            case FoodSensitivity.High: return 1.5f;
            case FoodSensitivity.VeryHigh: return 2f;
            default: return 1f;
        }
    }

    public static string Korean(FoodSensitivity s)
    {
        switch (s)
        {
            case FoodSensitivity.Low: return "낮음";
            case FoodSensitivity.High: return "높음";
            case FoodSensitivity.VeryHigh: return "매우높음";
            default: return "보통";
        }
    }

    /// <summary>엑셀 값 읽기: 낮음/보통/높음/매우높음 (또는 Low/Normal/High/VeryHigh)</summary>
    public static bool TryParse(string text, out FoodSensitivity s)
    {
        string t = (text ?? "").Replace(" ", "").Trim().ToLowerInvariant();
        switch (t)
        {
            case "낮음": case "low": s = FoodSensitivity.Low; return true;
            case "보통": case "normal": s = FoodSensitivity.Normal; return true;
            case "높음": case "high": s = FoodSensitivity.High; return true;
            case "매우높음": case "veryhigh": s = FoodSensitivity.VeryHigh; return true;
        }
        s = FoodSensitivity.Normal;
        return false;
    }
}

/// <summary>
/// 음식 한 종류의 고정 데이터. Project 창 우클릭 → Create → NeonRider → Food
/// 엑셀(DeliveryData.xlsx) Foods 탭에서 관리
/// </summary>
[CreateAssetMenu(menuName = "NeonRider/Food", fileName = "Food_")]
public class FoodData : ScriptableObject
{
    [Tooltip("고정 ID. 엑셀(DeliveryData.xlsx) Foods 탭의 id. 한번 정하면 바꾸지 않기")]
    public string id = "";
    public string displayName = "피자탕수육";

    [Header("신선도 (시간)")]
    [Tooltip("픽업 후 시간에 따라 신선도가 떨어지는 정도. 낮음 16분 / 보통 8분 / 높음 약 5분 / 매우높음 4분 뒤 1점")]
    public FoodSensitivity freshness = FoodSensitivity.Normal;

    [Header("형태 (충격)")]
    [Tooltip("착지·충돌 충격에 망가지는 정도. 낮음 0.5배 / 보통 1배 / 높음 1.5배 / 매우높음 2배")]
    public FoodSensitivity impact = FoodSensitivity.Normal;

    [Header("배달비")]
    [Tooltip("기본 배달비 (원). 0이면 DeliveryRules의 기본값 사용. 거리 추가분은 그 위에 더해짐")]
    public int basePay = 0;
    [Tooltip("배달비 배율. 1 = 기본, 1.5 = 1.5배")]
    public float payMultiplier = 1f;

    /// <summary>픽업 후 1분마다 깎이는 신선도 점수 (단계 고정값)</summary>
    public float freshnessLossPerMinute => FoodSensitivityTable.FreshnessLossPerMinute(freshness);

    /// <summary>충격 배율 (단계 고정값)</summary>
    public float impactSensitivity => FoodSensitivityTable.ImpactMultiplier(impact);
}
