using System.Collections;
using UnityEngine;

/// <summary>Pinball bumper: fires the ball away from its centre on contact, with a squash-pop.</summary>
public class AeroBumper : MonoBehaviour
{
    public float launchSpeed = 15f;
    public float upSpeed = 2.5f;
    public Transform pulseTarget;
    public static bool debugLog;
    private float lastHit;

    private void OnCollisionEnter(Collision c)
    {
        if (debugLog) Debug.Log($"[AeroBumper] contact with {c.collider.name}");
        var ball = c.collider.GetComponentInParent<BallPlayerController>();
        if (ball == null || Time.time - lastHit < 0.08f) return;
        lastHit = Time.time;
        var rb = ball.GetComponent<Rigidbody>();
        Vector3 dir = ball.transform.position - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = -c.relativeVelocity;
        dir.y = 0f; dir.Normalize();
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = dir * launchSpeed + Vector3.up * upSpeed;
#else
        rb.velocity = dir * launchSpeed + Vector3.up * upSpeed;
#endif
        if (debugLog) Debug.Log($"[AeroBumper] launched ball {dir} at {launchSpeed} m/s");
        if (pulseTarget != null) { StopAllCoroutines(); StartCoroutine(Pulse()); }
    }

    private IEnumerator Pulse()
    {
        Vector3 s = Vector3.one;
        for (float t = 0f; t < 0.22f; t += Time.deltaTime)
        {
            float k = 1f + 0.18f * Mathf.Sin(t / 0.22f * Mathf.PI);
            pulseTarget.localScale = new Vector3(s.x * k, s.y / k, s.z * k);
            yield return null;
        }
        pulseTarget.localScale = s;
    }
}
