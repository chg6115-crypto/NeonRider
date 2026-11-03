using UnityEngine;

/// <summary>
/// 가게 고정 데이터. Project 창 우클릭 → Create → NeonRider → Store
/// 씬의 위치는 DeliveryPoint(Pickup)가 이 에셋을 참조해서 연결
/// </summary>
[CreateAssetMenu(menuName = "NeonRider/Store", fileName = "Store_")]
public class StoreData : ScriptableObject
{
    [Tooltip("고정 ID. 엑셀(DeliveryData.xlsx) Stores 탭의 id. 한번 정하면 바꾸지 않기")]
    public string id = "";
    public string storeName = "피탕김탕";
    [Tooltip("끄면 주문이 생기지 않음 (엑셀 available)")]
    public bool available = true;
    public FoodData[] menu;

    public FoodData RandomFood()
    {
        if (menu == null || menu.Length == 0) return null;
        return menu[Random.Range(0, menu.Length)];
    }
}
