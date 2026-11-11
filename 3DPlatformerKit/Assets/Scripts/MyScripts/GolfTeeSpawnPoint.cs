using UnityEngine;

/// <summary>
/// Marks a tee prefab and exposes the child transform where the ball should be placed.
/// The tee root stays on the ground; the spawn point sits above its contact surface.
/// </summary>
[DisallowMultipleComponent]
public sealed class GolfTeeSpawnPoint : MonoBehaviour
{
    [SerializeField] private Transform ballSpawnPoint;
    [SerializeField, Min(0.05f)] private float ballRadius = 0.5f;
    [SerializeField, Min(0f)] private float contactSurfaceHeight = 0.31f;

    public Transform BallSpawnPoint
    {
        get
        {
            if (ballSpawnPoint == null)
                ballSpawnPoint = transform.Find("Ball Spawn Point");
            return ballSpawnPoint;
        }
    }

    public void Configure(Transform spawnPoint, float radius, float surfaceHeight)
    {
        ballSpawnPoint = spawnPoint;
        ballRadius = Mathf.Max(0.05f, radius);
        contactSurfaceHeight = Mathf.Max(0f, surfaceHeight);
        UpdateSpawnPointHeight();
    }

    private void OnValidate()
    {
        ballRadius = Mathf.Max(0.05f, ballRadius);
        contactSurfaceHeight = Mathf.Max(0f, contactSurfaceHeight);

        if (ballSpawnPoint == null)
            ballSpawnPoint = transform.Find("Ball Spawn Point");

        UpdateSpawnPointHeight();
    }

    private void UpdateSpawnPointHeight()
    {
        if (ballSpawnPoint != null && ballSpawnPoint.parent == transform)
        {
            Vector3 localPosition = ballSpawnPoint.localPosition;
            localPosition.y = contactSurfaceHeight + ballRadius;
            ballSpawnPoint.localPosition = localPosition;
        }
    }
}
