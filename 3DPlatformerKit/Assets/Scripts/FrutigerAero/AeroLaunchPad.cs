using UnityEngine;

/// <summary>Trigger that launches the ball on an arc so it lands on a target (ramps, cannons, shortcut pads).</summary>
[RequireComponent(typeof(Collider))]
public class AeroLaunchPad : MonoBehaviour
{
    public Transform target;
    [Range(15f, 75f)] public float launchAngle = 42f;
    public float minEntrySpeed = 0.6f;   // ball must actually be moving into it
    public float gravityMultiplier = 1.2f; // matches the ball's airborne gravity
    [Tooltip("Fraction of sideways speed kept on the first touchdown, so launched balls stick near the target.")]
    [Range(0.05f, 1f)] public float landingKeep = 0.22f;
    private Rigidbody flying;
    private float landAfter, giveUpAt;

    private void FixedUpdate()
    {
        if (flying == null || Time.time < landAfter) return;
        if (Time.time > giveUpAt) { flying = null; return; }
        if (!Physics.Raycast(flying.position, Vector3.down, 0.45f, ~0, QueryTriggerInteraction.Ignore)) return;
#if UNITY_6000_0_OR_NEWER
        Vector3 v = flying.linearVelocity; flying.linearVelocity = new Vector3(v.x * landingKeep, v.y, v.z * landingKeep);
#else
        Vector3 v = flying.velocity; flying.velocity = new Vector3(v.x * landingKeep, v.y, v.z * landingKeep);
#endif
        flying.angularVelocity *= landingKeep;
        flying = null;
    }

    public static bool debugLog;
    public float LastFlightTime { get; private set; }
    public event System.Action<BallPlayerController, Vector3, Vector3, float> Launched;
    private float lastLaunch;

    private void Awake() { GetComponent<Collider>().isTrigger = true; }

    private void OnTriggerEnter(Collider other)
    {
        var ball = other.GetComponentInParent<BallPlayerController>();
        if (ball == null || target == null || Time.time - lastLaunch < 0.6f) return;
        var rb = ball.GetComponent<Rigidbody>();
#if UNITY_6000_0_OR_NEWER
        Vector3 v0 = rb.linearVelocity;
#else
        Vector3 v0 = rb.velocity;
#endif
        if (v0.magnitude < minEntrySpeed) return;
        lastLaunch = Time.time;
        Vector3 from = ball.transform.position, to = target.position;
        Vector3 flat = to - from; float h = flat.y; flat.y = 0f;
        float d = flat.magnitude; if (d < 0.5f) return;
        float g = -Physics.gravity.y * gravityMultiplier;
        float angle = launchAngle, v2 = -1f;
        for (int i = 0; i < 8 && v2 <= 0f; i++, angle += 5f)
        {
            float th = angle * Mathf.Deg2Rad, c = Mathf.Cos(th);
            float den = 2f * c * c * (d * Mathf.Tan(th) - h);
            if (den > 0.01f) v2 = g * d * d / den;
        }
        if (v2 <= 0f) return;
        float a = (angle - 5f) * Mathf.Deg2Rad;
        Vector3 vel = flat.normalized * (Mathf.Sqrt(v2) * Mathf.Cos(a)) + Vector3.up * (Mathf.Sqrt(v2) * Mathf.Sin(a));
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = vel;
#else
        rb.velocity = vel;
#endif
        rb.angularVelocity = Vector3.zero;
        LastFlightTime = d / Mathf.Max(0.01f, Mathf.Sqrt(v2) * Mathf.Cos(a));
        flying = rb; landAfter = Time.time + LastFlightTime * 0.7f; giveUpAt = Time.time + LastFlightTime * 2.5f + 1f;
        Launched?.Invoke(ball, from, vel, LastFlightTime);
        if (debugLog) Debug.Log($"[AeroLaunchPad] {name} -> {target.name}: v={vel.magnitude:F1} angle={(angle - 5f):F0} d={d:F1} h={h:F1} T={LastFlightTime:F2}");
    }
}
