using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 드론 프로펠러 회전 + (선택) 제자리 둥실거림
/// 드론 루트(MCAircraft_A 등)에 붙이면 자식 중 이름이 rotorPrefix로 시작하는 오브젝트(RotorA~D)를 찾아 회전시킨다.
/// - 이웃한 로터는 반대 방향으로 회전 (실제 쿼드콥터처럼)
/// - 켜질 때 서서히 빨라지고, spinning을 끄면 서서히 멈춤
/// 회전 방향이 이상하면 Spin Axis를 (0,1,0) / (0,0,1) / (1,0,0) 중에서 바꿔 볼 것
/// </summary>
public class DroneRotorSpin : MonoBehaviour
{
    [Header("Rotor")]
    [Tooltip("이 이름으로 시작하는 자식을 로터로 사용")]
    [SerializeField] string rotorPrefix = "Rotor";
    [Tooltip("직접 지정하려면 여기에 넣기 (비우면 이름으로 자동 검색)")]
    [SerializeField] List<Transform> rotors = new List<Transform>();
    [Tooltip("로터 로컬 기준 회전축")]
    [SerializeField] Vector3 spinAxis = Vector3.up;
    [Tooltip("최고 회전 속도 (도/초). 1800 = 초당 5바퀴")]
    [SerializeField] float maxSpeed = 1800f;
    [Tooltip("최고 속도까지 걸리는 시간 (초)")]
    [SerializeField] float spinUpTime = 2f;
    [Tooltip("이웃한 로터를 반대 방향으로 회전")]
    [SerializeField] bool alternateDirection = true;
    [SerializeField] bool spinning = true;

    [Header("Hover (선택)")]
    [SerializeField] bool hover = true;
    [Tooltip("위아래 흔들림 폭 (m)")]
    [SerializeField] float hoverAmplitude = 0.3f;
    [Tooltip("위아래 흔들림 속도 (초당 왕복 횟수)")]
    [SerializeField] float hoverFrequency = 0.25f;
    [Tooltip("좌우로 살짝 기우는 각도")]
    [SerializeField] float swayAngle = 1.5f;

    public bool Spinning { get => spinning; set => spinning = value; }

    float speed;
    Vector3 basePos;
    Quaternion baseRot;
    float seed;

    void Awake()
    {
        if (rotors.Count == 0)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name.StartsWith(rotorPrefix)) rotors.Add(t);
            rotors.Sort((a, b) => string.CompareOrdinal(a.name, b.name));   // RotorA, B, C, D 순서
        }
        if (rotors.Count == 0) Debug.LogWarning($"[DroneRotorSpin] '{rotorPrefix}'로 시작하는 자식을 찾지 못했습니다.", this);

        basePos = transform.localPosition;
        baseRot = transform.localRotation;
        seed = Random.Range(0f, 100f);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // 회전 속도 : 서서히 올리고 내리기
        float target = spinning ? maxSpeed : 0f;
        float accel = maxSpeed / Mathf.Max(0.01f, spinUpTime);
        speed = Mathf.MoveTowards(speed, target, accel * dt);

        if (speed > 0.01f)
        {
            Vector3 axis = spinAxis.sqrMagnitude > 0.0001f ? spinAxis.normalized : Vector3.up;
            for (int i = 0; i < rotors.Count; i++)
            {
                if (rotors[i] == null) continue;
                // A, C 정방향 / B, D 역방향 (대각선끼리 같은 방향)
                float dir = alternateDirection && (i % 2 == 1) ? -1f : 1f;
                rotors[i].Rotate(axis, speed * dir * dt, Space.Self);
            }
        }

        // 둥실거림 : 프로펠러가 돌 때만 (속도 비율만큼)
        if (hover)
        {
            float k = maxSpeed > 0f ? speed / maxSpeed : 0f;
            float t = Time.time;
            float y = Mathf.Sin(t * hoverFrequency * Mathf.PI * 2f) * hoverAmplitude * k;
            float roll = (Mathf.PerlinNoise(seed, t * 0.3f) - 0.5f) * 2f * swayAngle * k;
            float pitch = (Mathf.PerlinNoise(seed + 50f, t * 0.3f) - 0.5f) * 2f * swayAngle * k;
            transform.localPosition = basePos + Vector3.up * y;
            transform.localRotation = baseRot * Quaternion.Euler(pitch, 0f, roll);
        }
    }

    void OnDisable()
    {
        if (hover)
        {
            transform.localPosition = basePos;
            transform.localRotation = baseRot;
        }
    }
}
