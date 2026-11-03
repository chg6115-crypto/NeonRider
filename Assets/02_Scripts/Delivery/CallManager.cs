using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 콜 한 건 (수락 전 주문 제안). 수락하면 DeliveryManager의 Order가 된다.
/// </summary>
public class DeliveryCall
{
    static int nextId = 1;

    public readonly int Id;
    public readonly DeliveryPoint Store;
    public readonly DeliveryPoint Destination;
    public readonly FoodData Food;
    public readonly float DistanceMeters;
    public readonly int EstimatedPay;
    public readonly float CreatedTime;
    public readonly float ExpireTime;

    public float Remaining => Mathf.Max(0f, ExpireTime - Time.time);
    public float Remaining01 => Mathf.Clamp01(Remaining / Mathf.Max(0.01f, ExpireTime - CreatedTime));
    public bool IsExpired => Time.time >= ExpireTime;
    public string StoreName => Store != null && Store.Store != null ? Store.Store.storeName : Store != null ? Store.DisplayName : "?";
    public string DestinationName => Destination != null ? Destination.DisplayName : "?";

    public DeliveryCall(DeliveryPoint store, DeliveryPoint destination, FoodData food, int estimatedPay, float lifetime)
    {
        Id = nextId++;
        Store = store;
        Destination = destination;
        Food = food;
        DistanceMeters = Vector3.Distance(store.EntrancePosition, destination.EntrancePosition);
        EstimatedPay = estimatedPay;
        CreatedTime = Time.time;
        ExpireTime = Time.time + lifetime;
    }
}

/// <summary>
/// 콜 시스템. 여러 가게에서 콜이 계속 들어와 목록에 쌓이고, 수락하지 않으면 일정 시간 뒤 사라진다.
/// - 같은 오브젝트(또는 아무 곳)에 하나만 두면 DeliveryManager의 자동 주문 생성 대신 이 방식으로 동작
/// - 수락: TryAccept(call) → 진행 중인 배달이 없을 때만 Order로 시작
/// - UI는 CallMenuUI (Q)
/// </summary>
public class CallManager : MonoBehaviour
{
    public static CallManager Instance { get; private set; }

    [Header("Generate")]
    [SerializeField] bool generateCalls = true;
    [Tooltip("목록에 동시에 있을 수 있는 최대 콜 수")]
    [SerializeField] int maxCalls = 6;
    [Tooltip("시작할 때 바로 들어와 있는 콜 수")]
    [SerializeField] int initialCalls = 3;
    [Tooltip("새 콜이 들어오는 간격 (초, 최소~최대 랜덤)")]
    [SerializeField] Vector2 spawnInterval = new Vector2(8f, 18f);
    [Tooltip("콜이 사라지기까지 시간 (초, 최소~최대 랜덤)")]
    [SerializeField] Vector2 lifetime = new Vector2(60f, 120f);
    [Tooltip("같은 가게→목적지 콜이 이미 있으면 다시 만들지 않음")]
    [SerializeField] bool avoidDuplicates = true;

    readonly List<DeliveryCall> calls = new List<DeliveryCall>();
    public IReadOnlyList<DeliveryCall> Calls => calls;

    public event Action<DeliveryCall> CallAdded;
    public event Action<DeliveryCall> CallRemoved;     // 만료 또는 수락
    public event Action<DeliveryCall> CallAccepted;

    float spawnTimer;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        for (int i = 0; i < initialCalls; i++) SpawnCall();
        ResetTimer();
    }

    void Update()
    {
        // 만료 정리
        for (int i = calls.Count - 1; i >= 0; i--)
        {
            if (!calls[i].IsExpired) continue;
            var c = calls[i];
            calls.RemoveAt(i);
            CallRemoved?.Invoke(c);
        }

        if (!generateCalls) return;
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            if (calls.Count < maxCalls) SpawnCall();
            ResetTimer();
        }
    }

    void ResetTimer() => spawnTimer = UnityEngine.Random.Range(spawnInterval.x, Mathf.Max(spawnInterval.x, spawnInterval.y));

    /// <summary>새 콜 하나 만들기 (디버그에서도 사용)</summary>
    public DeliveryCall SpawnCall()
    {
        var dm = DeliveryManager.Instance;
        if (dm == null) return null;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (!dm.TryPickRoute(out var store, out var dest, out var food)) return null;
            if (avoidDuplicates && calls.Exists(c => c.Store == store && c.Destination == dest)) continue;

            float dist = Vector3.Distance(store.EntrancePosition, dest.EntrancePosition);
            int pay = dm.Rules.BaseFor(dist, food);
            float life = UnityEngine.Random.Range(lifetime.x, Mathf.Max(lifetime.x, lifetime.y));
            var call = new DeliveryCall(store, dest, food, pay, life);
            calls.Add(call);
            CallAdded?.Invoke(call);
            return call;
        }
        return null;
    }

    /// <summary>콜 수락. 진행 중인 배달이 있거나 만료됐으면 실패</summary>
    public bool TryAccept(DeliveryCall call, out string reason)
    {
        reason = null;
        var dm = DeliveryManager.Instance;
        if (call == null || !calls.Contains(call)) { reason = "없는 콜이에요"; return false; }
        if (call.IsExpired) { reason = "만료된 콜이에요"; return false; }
        if (dm == null) { reason = "DeliveryManager가 없어요"; return false; }
        if (dm.CurrentOrder != null) { reason = "진행 중인 배달을 먼저 끝내야 해요"; return false; }

        calls.Remove(call);
        dm.StartOrder(new Order(call.Store, call.Destination, call.Food));
        CallRemoved?.Invoke(call);
        CallAccepted?.Invoke(call);
        return true;
    }
}
