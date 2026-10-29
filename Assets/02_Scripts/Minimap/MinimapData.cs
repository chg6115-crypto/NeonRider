using UnityEngine;

/// <summary>
/// 구워 둔 지도 이미지 + 그 이미지가 덮는 월드 범위.
/// MinimapBaker(에디터 창)가 자동으로 만들어 줌.
/// 이미지 위쪽 = 월드 +Z(북), 오른쪽 = 월드 +X(동)
/// </summary>
[CreateAssetMenu(menuName = "NeonRider/Minimap Data", fileName = "MinimapData")]
public class MinimapData : ScriptableObject
{
    public Texture2D mapTexture;
    [Tooltip("지도 이미지 왼쪽 아래 모서리의 월드 좌표 (x, z)")]
    public Vector2 worldMin;
    [Tooltip("지도 이미지 한 변이 덮는 월드 길이 (m). 정사각형")]
    public float worldSize = 1000f;

    /// <summary>월드 위치 → 지도 UV (0~1)</summary>
    public Vector2 WorldToUV(Vector3 world)
    {
        return new Vector2((world.x - worldMin.x) / worldSize, (world.z - worldMin.y) / worldSize);
    }
}
