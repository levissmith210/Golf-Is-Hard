using UnityEngine;

/// <summary>
/// A baked grid of surface types (fairway / rough / green / sand / tee) over a sculpted island,
/// in the island's local XZ space. Written by the course builder; read by the ball.
/// </summary>
[DisallowMultipleComponent]
public class AeroSurfaceMap : MonoBehaviour
{
    [SerializeField] private float originX, originZ;
    [SerializeField] private float cellSize = 0.6f;
    [SerializeField] private int width, height;
    [SerializeField, HideInInspector] private byte[] cells;

    public void Configure(float x0, float z0, float cell, int w, int h, byte[] data)
    {
        originX = x0; originZ = z0; cellSize = cell; width = w; height = h; cells = data;
    }

    public AeroSurfaceType Sample(Vector3 worldPoint)
    {
        if (cells == null || cells.Length == 0) return AeroSurfaceType.Default;
        Vector3 p = transform.InverseTransformPoint(worldPoint);
        int ix = Mathf.FloorToInt((p.x - originX) / cellSize);
        int iz = Mathf.FloorToInt((p.z - originZ) / cellSize);
        if (ix < 0 || iz < 0 || ix >= width || iz >= height) return AeroSurfaceType.Rough;
        return (AeroSurfaceType)cells[iz * width + ix];
    }
}
