using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public sealed class GolfHoleFlagWave : MonoBehaviour
{
    [SerializeField, Min(0f)] private float waveAmplitude = 0.075f;
    [SerializeField, Min(0f)] private float flutterAmplitude = 0.024f;
    [SerializeField, Min(0f)] private float waveSpeed = 1.8f;
    [SerializeField, Min(0f)] private float waveFrequency = 4.2f;

    private MeshFilter meshFilter;
    private Mesh originalMesh;
    private Mesh animatedMesh;
    private Vector3[] restVertices;
    private Vector3[] animatedVertices;
    private Transform[] details;
    private Vector3[] detailRestPositions;
    private float minX;
    private float maxX;
    private float minY;
    private float maxY;
    private float poleEdgeX;
    private float freeEdgeDirection;

    private void OnEnable()
    {
        meshFilter = GetComponent<MeshFilter>();
        originalMesh = meshFilter.sharedMesh;
        if (originalMesh == null)
            return;

        Bounds bounds = originalMesh.bounds;
        minX = bounds.min.x;
        maxX = bounds.max.x;
        minY = bounds.min.y;
        maxY = bounds.max.y;

        // Flag meshes are authored with the pole attachment at the edge nearest x = 0.
        // This handles both the positive-X generated flag and negative-X imported flag.
        if (Mathf.Abs(minX) <= Mathf.Abs(maxX))
        {
            poleEdgeX = minX;
            freeEdgeDirection = 1f;
        }
        else
        {
            poleEdgeX = maxX;
            freeEdgeDirection = -1f;
        }

        animatedMesh = Instantiate(originalMesh);
        animatedMesh.name = originalMesh.name + " (Animated)";
        animatedMesh.MarkDynamic();
        meshFilter.sharedMesh = animatedMesh;
        restVertices = originalMesh.vertices;
        animatedVertices = new Vector3[restVertices.Length];

        int childCount = transform.childCount;
        details = new Transform[childCount];
        detailRestPositions = new Vector3[childCount];
        for (int i = 0; i < childCount; i++)
        {
            details[i] = transform.GetChild(i);
            detailRestPositions[i] = details[i].localPosition;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || animatedMesh == null || restVertices == null)
            return;

        float time = Time.time;
        float width = Mathf.Max(0.0001f, maxX - minX);
        float height = Mathf.Max(0.0001f, maxY - minY);
        for (int i = 0; i < restVertices.Length; i++)
        {
            Vector3 rest = restVertices[i];
            float u = Mathf.Clamp01((rest.x - poleEdgeX) * freeEdgeDirection / width);
            float v = Mathf.Clamp01((rest.y - minY) / height);
            float edgeWeight = u * u * (3f - 2f * u);
            rest.z += WaveOffset(u, v, time) * edgeWeight;
            rest.y += waveAmplitude * 0.12f * edgeWeight *
                      Mathf.Sin(time * waveSpeed - u * waveFrequency + v * 1.4f);
            animatedVertices[i] = rest;
        }

        animatedMesh.vertices = animatedVertices;
        animatedMesh.RecalculateNormals();
        animatedMesh.RecalculateBounds();

        for (int i = 0; i < details.Length; i++)
        {
            if (details[i] == null)
                continue;

            Vector3 rest = detailRestPositions[i];
            float u = Mathf.Clamp01((rest.x - poleEdgeX) * freeEdgeDirection / width);
            float v = Mathf.Clamp01((rest.y - minY) / height);
            float edgeWeight = u * u * (3f - 2f * u);
            Vector3 animated = rest;
            animated.z += WaveOffset(u, v, time) * edgeWeight;
            animated.y += waveAmplitude * 0.12f * edgeWeight *
                          Mathf.Sin(time * waveSpeed - u * waveFrequency + v * 1.4f);
            details[i].localPosition = animated;
        }
    }

    private float WaveOffset(float u, float v, float time)
    {
        float broadWave = Mathf.Sin(time * waveSpeed - u * waveFrequency + v * 1.2f);
        float lightFlutter = Mathf.Sin(time * waveSpeed * 1.7f - u * 8f + v * 2.3f);
        return waveAmplitude * broadWave + flutterAmplitude * lightFlutter;
    }

    private void OnDisable()
    {
        if (meshFilter != null && animatedMesh != null)
            meshFilter.sharedMesh = originalMesh;

        if (animatedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(animatedMesh);
            else
                DestroyImmediate(animatedMesh);
        }

        animatedMesh = null;
        restVertices = null;
        animatedVertices = null;
        details = null;
        detailRestPositions = null;
    }
}
