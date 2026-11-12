using UnityEngine;

/// <summary>Speed strip: pushes the ball along this object's forward direction up to a target speed.</summary>
[RequireComponent(typeof(Collider))]
public class AeroBoostPad : MonoBehaviour
{
    public float boostSpeed = 16f;
    private void Awake() { GetComponent<Collider>().isTrigger = true; }

    private void OnTriggerEnter(Collider other)
    {
        var ball = other.GetComponentInParent<BallPlayerController>();
        if (ball == null) return;
        var rb = ball.GetComponent<Rigidbody>();
        Vector3 f = transform.forward;
#if UNITY_6000_0_OR_NEWER
        Vector3 v = rb.linearVelocity;
#else
        Vector3 v = rb.velocity;
#endif
        float along = Vector3.Dot(v, f);
        if (along < -0.5f) return; // only boosts in its arrow direction
        v += f * Mathf.Max(0f, boostSpeed - along);
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }
}
