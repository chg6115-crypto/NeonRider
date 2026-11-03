using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 최소 배달 루프
/// 주문 생성 → (하차 후) 가게 영역에서 E 0.5초 홀드로 픽업 → 목적지 영역에서 E 0.5초 홀드로 전달 → 판정·지급 → 다음 주문
/// - 신선도: 픽업 후부터 시간에 따라 감소
/// - 형태: 픽업 후 오토바이 착지 충격(MotorcycleTestDrive.Landed)으로 감소
/// - 실패: 신선도/형태 둘 중 하나라도 1점 이하 → 전달 시점에 판정 (지급 0)
/// </summary>
public class DeliveryManager : MonoBehaviour
{
    public static DeliveryManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] RobotRider rider;                 // 비우면 씬에서 찾음
    [SerializeField] MotorcycleTestDrive bike;         // 비우면 씬에서 찾음

    [Header("Rules")]
    [SerializeField] DeliveryRules rules = new DeliveryRules();

    [Header("Visual")]
    [Tooltip("배달 지점 바닥 표시용 머티리얼 (URP Unlit, Transparent, Base Map = Delivery_Zone.png)")]
    [SerializeField] Material zoneMaterial;

    [Header("Interaction")]
    [SerializeField] float holdTime = 0.3f;

    [Header("Orders")]
    [SerializeField] bool autoCreateOrders = true;
    [SerializeField] float nextOrderDelay = 3f;
    [Tooltip("이보다 가까운 가게-목적지 조합은 고르지 않음 (m)")]
    [SerializeField] float minDistance = 50f;

    [Header("Debug")]
    [Tooltip("테스트용: 1분을 몇 초로 볼지 (60 = 실제 시간)")]
    [SerializeField] float secondsPerMinute = 60f;
    [Tooltip("디버그 키 (에디터/개발 빌드에서만)\nF9: 현재 단계 즉시 완료 (픽업 또는 전달)\nF10: 현재 주문 바로 배달 완료\nF11: 현재 주문 버리고 새 주문")]
    [SerializeField] bool debugKeys = true;

    // ---- 상태 ----
    public Order CurrentOrder { get; private set; }
    public long Money { get; private set; }
    public float HoldProgress01 { get; private set; }   // 0~1, UI용
    public DeliveryResult? LastResult { get; private set; }
    public DeliveryRules Rules => rules;
    public RobotRider Rider => rider;
    public Material ZoneMaterial => zoneMaterial;

    // ---- 이벤트 (UI는 이것만 듣기) ----
    public event Action<Order> OrderCreated;
    public event Action<Order> PickedUp;
    public event Action<DeliveryResult> Delivered;
    public event Action<Order, float> ShapeDamaged;   // 깎인 점수
    public event Action<long> MoneyChanged;

    float holdTimer;
    float nextOrderTimer = -1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (rider == null) rider = FindFirstObjectByType<RobotRider>();
        if (bike == null) bike = FindFirstObjectByType<MotorcycleTestDrive>(FindObjectsInactive.Include);
    }

    void OnEnable() { if (bike != null) bike.Landed += OnBikeLanded; }
    void OnDisable() { if (bike != null) bike.Landed -= OnBikeLanded; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (autoCreateOrders) CreateRandomOrder();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        HandleDebugKeys();

        // 다음 주문 대기
        if (CurrentOrder == null && autoCreateOrders && nextOrderTimer >= 0f)
        {
            nextOrderTimer -= dt;
            if (nextOrderTimer < 0f) CreateRandomOrder();
        }

        var o = CurrentOrder;
        if (o == null) { ResetHold(); return; }

        // 신선도: 픽업 후부터
        if (o.State == OrderState.PickedUp && o.Food != null)
            o.Condition.Tick(dt / Mathf.Max(0.01f, secondsPerMinute), o.Food.freshnessLossPerMinute);

        // E 홀드 상호작용
        var target = o.Target;
        bool canInteract = target != null && target.IsOnFootInside(rider);
        if (canInteract && InteractHeld())
        {
            holdTimer += dt;
            HoldProgress01 = Mathf.Clamp01(holdTimer / Mathf.Max(0.01f, holdTime));
            if (holdTimer >= holdTime)
            {
                ResetHold();
                if (o.State == OrderState.Accepted) DoPickup(o);
                else if (o.State == OrderState.PickedUp) DoDeliver(o);
            }
        }
        else ResetHold();
    }

    void ResetHold() { holdTimer = 0f; HoldProgress01 = 0f; }

    // ---------------- 주문 ----------------
    public bool CreateRandomOrder()
    {
        var pickups = DeliveryPoint.All.Where(p => p.Type == DeliveryPointType.Pickup && p.Store != null && p.Store.available && p.Store.menu != null && p.Store.menu.Length > 0).ToList();
        var drops = DeliveryPoint.All.Where(p => p.Type == DeliveryPointType.Dropoff).ToList();
        if (pickups.Count == 0 || drops.Count == 0)
        {
            Debug.LogWarning("[Delivery] 픽업(가게+메뉴 설정) 또는 배달 지점이 없습니다.");
            return false;
        }

        var store = pickups[UnityEngine.Random.Range(0, pickups.Count)];
        var candidates = drops.Where(d => Vector3.Distance(d.EntrancePosition, store.EntrancePosition) >= minDistance).ToList();
        if (candidates.Count == 0) candidates = drops;
        var dest = candidates[UnityEngine.Random.Range(0, candidates.Count)];

        StartOrder(new Order(store, dest, store.Store.RandomFood()));
        return true;
    }

    public void StartOrder(Order order)
    {
        CurrentOrder = order;
        nextOrderTimer = -1f;
        RefreshBeacons();
        Debug.Log($"[Delivery] 주문 #{order.Id}: {order.StoreName} → {order.Destination.DisplayName} ({order.Food?.displayName}), 직선 {order.DistanceMeters:0}m, 예상 {rules.BaseFor(order.DistanceMeters, order.Food):N0}원");
        OrderCreated?.Invoke(order);
    }

    void DoPickup(Order o)
    {
        o.State = OrderState.PickedUp;
        o.PickedUpTime = Time.time;
        RefreshBeacons();
        Debug.Log($"[Delivery] 픽업 완료: {o.Food?.displayName}");
        PickedUp?.Invoke(o);
    }

    void DoDeliver(Order o)
    {
        var grade = DeliveryRules.GradeOf(o.Condition);
        int pay = rules.PayFor(o.DistanceMeters, grade, o.Food);
        o.State = grade == DeliveryGrade.Failed ? OrderState.Failed : OrderState.Delivered;

        var result = new DeliveryResult
        {
            order = o,
            grade = grade,
            freshnessPoints = FoodCondition.ToPoints(o.Condition.Freshness),
            shapePoints = FoodCondition.ToPoints(o.Condition.Shape),
            pay = pay,
            reaction = DeliveryRules.ReactionOf(grade)
        };

        Money += pay;
        LastResult = result;
        CurrentOrder = null;
        RefreshBeacons();

        Debug.Log($"[Delivery] 전달 #{o.Id}: {grade} (신선도 {result.freshnessPoints}/형태 {result.shapePoints}) +{pay:N0}원 → 보유 {Money:N0}원 / \"{result.reaction}\"");
        Delivered?.Invoke(result);
        MoneyChanged?.Invoke(Money);

        if (autoCreateOrders) nextOrderTimer = nextOrderDelay;
    }

    void RefreshBeacons()
    {
        foreach (var p in DeliveryPoint.All) p.SetBeacon(false);
        if (CurrentOrder?.Target != null) CurrentOrder.Target.SetBeacon(true);
    }

    // ---------------- 충격 ----------------
    void OnBikeLanded(float impact)
    {
        var o = CurrentOrder;
        if (o == null || o.State != OrderState.PickedUp || o.Food == null) return;
        if (rider != null && !rider.IsRiding) return;   // 음식은 오토바이에 실려 있을 때만 영향

        float lost = o.Condition.ApplyImpact(impact, rules.impactThreshold, rules.shapeLossPerMps, o.Food.impactSensitivity);
        if (lost > 0f)
        {
            Debug.Log($"[Delivery] 착지 충격 {impact:0.0}m/s → 형태 -{lost:0.00} (현재 {o.Condition.Shape:0.0})");
            ShapeDamaged?.Invoke(o, lost);
        }
    }

    // ---------------- 디버그 ----------------
    public bool DebugKeysEnabled => debugKeys;

    /// <summary>현재 단계 즉시 완료. 주문이 없으면 새 주문 생성</summary>
    public void DebugCompleteStep()
    {
        var o = CurrentOrder;
        if (o == null) { CreateRandomOrder(); return; }
        ResetHold();
        if (o.State == OrderState.Accepted) DoPickup(o);
        else if (o.State == OrderState.PickedUp) DoDeliver(o);
    }

    /// <summary>현재 주문을 픽업부터 전달까지 한 번에 완료 (지금 음식 상태로 판정)</summary>
    public void DebugCompleteOrder()
    {
        var o = CurrentOrder;
        if (o == null) { Debug.Log("[Delivery][Debug] 진행 중인 주문 없음"); return; }
        ResetHold();
        if (o.State == OrderState.Accepted) DoPickup(o);
        if (o.State == OrderState.PickedUp) DoDeliver(o);
    }

    /// <summary>현재 주문을 버리고 바로 새 주문</summary>
    public void DebugNewOrder()
    {
        if (CurrentOrder != null)
        {
            Debug.Log($"[Delivery][Debug] 주문 #{CurrentOrder.Id} 취소");
            CurrentOrder.State = OrderState.Failed;
            CurrentOrder = null;
            RefreshBeacons();
        }
        ResetHold();
        CreateRandomOrder();
    }

    void HandleDebugKeys()
    {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (!debugKeys || kb == null) return;
        if (kb.f9Key.wasPressedThisFrame) DebugCompleteStep();
        if (kb.f10Key.wasPressedThisFrame) DebugCompleteOrder();
        if (kb.f11Key.wasPressedThisFrame) DebugNewOrder();
#endif
    }

    // ---------------- 입력 ----------------
    static bool InteractHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.isPressed;
#else
        return false;
#endif
    }
}
