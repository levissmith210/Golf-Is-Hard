using UnityEngine;

/// <summary>Gentle decorative floating motion (bubbles, crystals, clouds). Not for objects the ball collides with.</summary>
[DisallowMultipleComponent]
public class AeroBob : MonoBehaviour
{
    public float amplitude = 0.5f;
    public float speed = 0.6f;
    public float spinDegreesPerSecond = 0f;
    private Vector3 start;
    private float phase;

    private void Start()
    {
        start = transform.localPosition;
        phase = (transform.position.x * 0.37f + transform.position.z * 0.21f) % 6.283f;
    }

    private void Update()
    {
        transform.localPosition = start + Vector3.up * (Mathf.Sin(Time.time * speed + phase) * amplitude);
        if (spinDegreesPerSecond != 0f) transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
    }
}
