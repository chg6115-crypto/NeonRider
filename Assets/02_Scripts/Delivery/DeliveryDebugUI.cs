using UnityEngine;

/// <summary>
/// 임시 디버그 표시 (게이지 UI 만들기 전까지). 아무 오브젝트에나 붙이면 화면 왼쪽 위에 표시.
/// </summary>
public class DeliveryDebugUI : MonoBehaviour
{
    [SerializeField] bool show = true;
    GUIStyle style;

    void OnGUI()
    {
        var m = DeliveryManager.Instance;
        if (!show || m == null) return;
        if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, richText = true };

        string s = $"<b>보유 {m.Money:N0}원</b>\n";
        var o = m.CurrentOrder;
        if (o != null)
        {
            string step = o.State == OrderState.Accepted ? "픽업하러 가기" : "배달하러 가기";
            s += $"주문 #{o.Id}  {o.StoreName} → {o.Destination.DisplayName}\n";
            s += $"{o.Food?.displayName}  /  {o.DistanceMeters:0}m  /  예상 {m.Rules.BaseFor(o.DistanceMeters):N0}원\n";
            s += $"<b>{step}</b> : {o.Target.DisplayName}  ({Vector3.Distance(Camera.main ? Camera.main.transform.position : Vector3.zero, o.Target.EntrancePosition):0}m)\n";
            s += $"<color=#4FC3FF>신선도 {o.Condition.Freshness:0.0}</color>   <color=#FF5A5A>형태 {o.Condition.Shape:0.0}</color>";
            if (o.Condition.IsFailed) s += "   <color=red><b>위험!</b></color>";
            s += "\n";
            var rider = m.Rider;
            bool inside = rider != null && o.Target.Contains(rider.transform.position);
            bool riding = rider != null && rider.IsRiding;
            if (rider == null) s += "<color=red>RobotRider를 못 찾음</color>\n";
            else if (inside && !riding) s += $"[E] 길게 누르기  {Mathf.RoundToInt(m.HoldProgress01 * 100)}%\n";
            else if (inside && riding) s += "오토바이에서 내려야 해요 (F)\n";
            else s += $"<size=12>영역 밖  (목적지까지 {Vector3.Distance(rider.transform.position, o.Target.transform.position):0.0}m)</size>\n";
        }
        else s += "다음 주문 대기 중...\n";

        if (m.LastResult is DeliveryResult r)
            s += $"\n지난 배달: {r.grade}  신선도 {r.freshnessPoints} / 형태 {r.shapePoints}  +{r.pay:N0}원\n\"{r.reaction}\"";

        GUI.Box(new Rect(12, 12, 460, 210), s, style);
    }
}
