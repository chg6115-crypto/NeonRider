using UnityEngine;

public enum OrderState { Accepted, PickedUp, Delivered, Failed }

/// <summary>
/// 진행 중인 주문 한 건 (게임 중에 생겼다 사라지는 데이터)
/// </summary>
public class Order
{
    static int nextId = 1;

    public readonly int Id;
    public readonly DeliveryPoint Store;
    public readonly DeliveryPoint Destination;
    public readonly FoodData Food;
    public readonly float DistanceMeters;      // 가게 → 목적지 직선거리
    public readonly FoodCondition Condition = new FoodCondition();

    public OrderState State { get; set; } = OrderState.Accepted;
    public float AcceptedTime { get; }
    public float PickedUpTime { get; set; }

    public string StoreName => Store != null && Store.Store != null ? Store.Store.storeName : Store != null ? Store.DisplayName : "?";

    public Order(DeliveryPoint store, DeliveryPoint destination, FoodData food)
    {
        Id = nextId++;
        Store = store;
        Destination = destination;
        Food = food;
        AcceptedTime = Time.time;
        DistanceMeters = Vector3.Distance(store.EntrancePosition, destination.EntrancePosition);
    }

    /// <summary>지금 가야 할 곳</summary>
    public DeliveryPoint Target => State == OrderState.Accepted ? Store : Destination;
}

/// <summary>배달 완료 결과 (UI/평판 시스템에 전달)</summary>
public struct DeliveryResult
{
    public Order order;
    public DeliveryGrade grade;
    public int freshnessPoints;
    public int shapePoints;
    public int pay;
    public string reaction;
}
