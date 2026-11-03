using UnityEngine;

/// <summary>
/// 구워 둔 지도 이미지 + 그 이미지가 덮는 월드 범위.
/// MinimapBaker(에디터 창)가 자동으로 만들어 줌.
/// 이미지 위쪽 = 월드 +Z, 오른쪽 = 월드 +X (MinimapBaker 촬영 기준)
/// 북쪽이 +Z가 아닌 맵은 northUpRotation으로 보정 (공주 맵: 북쪽 = -Z → 180)
/// </summary>
[CreateAssetMenu(menuName = "NeonRider/Minimap Data", fileName = "MinimapData")]
public class MinimapData : ScriptableObject
{
    public Texture2D mapTexture;
    [Tooltip("지도 이미지 왼쪽 아래 모서리의 월드 좌표 (x, z)")]
    public Vector2 worldMin;
    [Tooltip("지도 이미지 한 변이 덮는 월드 길이 (m). 정사각형")]
    public float worldSize = 1000f;
    [Tooltip("지도를 이 각도(도)만큼 돌려야 북쪽이 위로 옴. 북쪽이 +Z면 0, -Z면 180, +X면 90, -X면 -90")]
    public float northUpRotation = 0f;

    /// <summary>월드 위치 → 지도 UV (0~1)</summary>
    public Vector2 WorldToUV(Vector3 world)
    {
        return new Vector2((world.x - worldMin.x) / worldSize, (world.z - worldMin.y) / worldSize);
    }
}
