using UnityEngine;

/// <summary>Spins an obstacle around a local axis. Uses a kinematic Rigidbody so the ball is pushed properly.</summary>
[DisallowMultipleComponent]
public class AeroSpinner : MonoBehaviour
{
    public Vector3 localAxis = Vector3.up;
    public float degreesPerSecond = 60f;
    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        body.MoveRotation(body.rotation * Quaternion.AngleAxis(degreesPerSecond * Time.fixedDeltaTime, localAxis));
    }
}
