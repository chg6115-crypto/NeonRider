using UnityEngine;

/// <summary>
/// 가게 고정 데이터. Project 창 우클릭 → Create → NeonRider → Store
/// 씬의 위치는 DeliveryPoint(Pickup)가 이 에셋을 참조해서 연결
/// </summary>
[CreateAssetMenu(menuName = "NeonRider/Store", fileName = "Store_")]
public class StoreData : ScriptableObject
{
    public string storeName = "피탕김탕";
    public FoodData[] menu;

    public FoodData RandomFood()
    {
        if (menu == null || menu.Length == 0) return null;
        return menu[Random.Range(0, menu.Length)];
    }
}
