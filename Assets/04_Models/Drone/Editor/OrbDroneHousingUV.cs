using UnityEngine;
using UnityEditor;

// Add UVs only to the unwrapped procedural housing; geometry is unchanged.
public sealed class OrbDroneHousingUV : AssetPostprocessor
{
    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != "Assets/04_Models/Drone/OrbDrone.fbx") return;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name != "Lens_Housing") continue;
            var mesh = filter.sharedMesh;
            if (!mesh || mesh.uv.Length == mesh.vertexCount) continue;
            var vertices = mesh.vertices;
            var uv = new Vector2[vertices.Length];
            var bounds = mesh.bounds;
            for (int i = 0; i < vertices.Length; i++)
                uv[i] = new Vector2(
                    (vertices[i].x - bounds.min.x) / bounds.size.x,
                    (vertices[i].z - bounds.min.z) / bounds.size.z);
            mesh.uv = uv;
            mesh.RecalculateTangents();
        }
    }
}