using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
[RequireComponent(typeof(DecalProjector))]
public class DecalScaler : MonoBehaviour
{
    private DecalProjector projector;

    private void Awake()
    {
        projector = GetComponent<DecalProjector>();
    }

    private void Update()
    {
        if (projector != null)
        {
            projector.size = transform.localScale;
        }
    }

    private void Reset()
    {
        transform.localScale = new Vector3(1f, 1f, 1f);
    }
}
