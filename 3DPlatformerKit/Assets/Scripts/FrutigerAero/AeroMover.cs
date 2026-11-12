using UnityEngine;

/// <summary>Kinematic obstacle that glides back and forth between its start and start + offset.</summary>
public class AeroMover : MonoBehaviour
{
    public Vector3 localOffset = new Vector3(3f, 0f, 0f);
    public float period = 3f;
    private Rigidbody body;
    private Vector3 start;
    private float phase;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate;
        start = transform.localPosition;
        phase = (transform.position.x + transform.position.z) * 0.13f;
    }

    private void FixedUpdate()
    {
        float t = 0.5f - 0.5f * Mathf.Cos((Time.time / Mathf.Max(0.2f, period)) * Mathf.PI * 2f + phase);
        Vector3 local = start + localOffset * t;
        body.MovePosition(transform.parent != null ? transform.parent.TransformPoint(local) : local);
    }
}
