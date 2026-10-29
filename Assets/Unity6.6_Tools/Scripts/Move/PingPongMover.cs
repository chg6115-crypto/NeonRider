using UnityEngine;

public class PingPongMover : MonoBehaviour
{
    public enum SpaceMode { World, Local }

    [Header("Motion")]
    [SerializeField] private SpaceMode spaceMode = SpaceMode.World;
    [SerializeField] private Vector3 axis = Vector3.right;
    [SerializeField] private float amplitude = 2f;
    [SerializeField] private float speed = 2f;

    [Header("Behavior")]
    [SerializeField] private bool useSmoothPingPong = true;
    [SerializeField] private float edgePauseSeconds = 0f;

    private Vector3 _startPos;
    private float _t;
    private int _dir = 1;
    private float _pauseTimer;

    private void Awake()
    {
        axis = axis.sqrMagnitude < 0.0001f ? Vector3.right : axis.normalized;

        if (spaceMode == SpaceMode.World)
            _startPos = transform.position;
        else
            _startPos = transform.localPosition;
    }

    private void Update()
    {
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= Time.deltaTime;
            return;
        }

        if (useSmoothPingPong)
        {
            _t += Time.deltaTime * speed;
            float s = Mathf.PingPong(_t, 1f);
            Vector3 offset = axis * Mathf.Lerp(-amplitude, amplitude, s);

            ApplyPosition(_startPos + offset);

            if (edgePauseSeconds > 0f && (s <= 0.0001f || s >= 0.9999f))
                _pauseTimer = edgePauseSeconds;
        }
        else
        {
            float step = speed * Time.deltaTime * _dir;
            Vector3 pos = GetPosition() + axis * step;

            Vector3 min = _startPos - axis * amplitude;
            Vector3 max = _startPos + axis * amplitude;

            float along = Vector3.Dot(pos - _startPos, axis);

            if (along > amplitude)
            {
                pos = max;
                _dir = -1;
                _pauseTimer = edgePauseSeconds;
            }
            else if (along < -amplitude)
            {
                pos = min;
                _dir = 1;
                _pauseTimer = edgePauseSeconds;
            }

            ApplyPosition(pos);
        }
    }

    private Vector3 GetPosition()
    {
        return spaceMode == SpaceMode.World ? transform.position : transform.localPosition;
    }

    private void ApplyPosition(Vector3 p)
    {
        if (spaceMode == SpaceMode.World) transform.position = p;
        else transform.localPosition = p;
    }

    private void OnValidate()
    {
        if (axis.sqrMagnitude > 0.0001f) axis = axis.normalized;
        amplitude = Mathf.Max(0f, amplitude);
        speed = Mathf.Max(0f, speed);
        edgePauseSeconds = Mathf.Max(0f, edgePauseSeconds);
    }
}
