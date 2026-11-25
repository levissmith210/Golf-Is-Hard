using System.Collections;
using UnityEngine;

/// <summary>
/// Records a nearby resting point, relative to moving ground when appropriate,
/// then returns the ball there with a penalty after water or out-of-bounds hits.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BallPlayerController))]
public class AeroBallSafety : MonoBehaviour
{
    [SerializeField, Min(0f)] private float restSpeed = 0.25f;
    [SerializeField, Min(0f)] private float restTime = 0.35f;
    [SerializeField] private float failsafeY = -25f;
    [SerializeField, Min(0f)] private float messageSeconds = 1.8f;

    private struct SupportCheckpoint
    {
        public Vector3 worldPosition;
        public Transform support;
        public Vector3 localPosition;
        public bool usesSupport;
    }

    private BallPlayerController ball;
    private Rigidbody body;
    private SphereCollider sphere;
    private SupportCheckpoint safePoint, teePoint;
    private bool hasSafeSpot, hasTeeSpot;
    private bool landedSinceReset = true;
    private float restTimer;
    private int hazardContacts;
    private bool resetting;
    private string message;
    private float messageUntil;
    private GUIStyle style, shadow;
    private bool floating;
    private bool gravityBeforeFloat;
    private float floatSurfaceY;

    public Vector3 SafeSpot
    {
        get
        {
            if (hasSafeSpot && TryResolve(safePoint, out Vector3 safe, out _))
                return safe;
            if (hasTeeSpot && TryResolve(teePoint, out Vector3 tee, out _))
                return tee;
            return hasTeeSpot ? teePoint.worldPosition : safePoint.worldPosition;
        }
    }
    public bool IsResetting => resetting;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (ball == null) ball = GetComponent<BallPlayerController>();
        if (body == null) body = GetComponent<Rigidbody>();
        if (sphere == null) sphere = GetComponent<SphereCollider>();
    }

    private float Radius => sphere != null
        ? sphere.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z))
        : 0.25f;

    private Vector3 Velocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return body.linearVelocity;
#else
            return body.velocity;
#endif
        }
    }

    private void FixedUpdate()
    {
        if (floating && body != null && !body.isKinematic)
            Float();

        if (resetting || body == null || body.isKinematic || ball == null || !ball.enabled)
            return;

        if (hazardContacts == 0 && TryGetGround(out RaycastHit ground))
        {
            Rigidbody support = ground.rigidbody;
            Vector3 supportVelocity = support != null ? support.GetPointVelocity(body.position) : Vector3.zero;
            if ((Velocity - supportVelocity).magnitude < restSpeed)
            {
                restTimer += Time.fixedDeltaTime;
                if (restTimer >= restTime)
                {
                    safePoint = Capture(body.position, support);
                    hasSafeSpot = true;
                    if (ball.StrokeCount == 0)
                    {
                        teePoint = safePoint;
                        hasTeeSpot = true;
                    }
                    landedSinceReset = true;
                }
            }
            else
            {
                restTimer = 0f;
            }
        }
        else
        {
            restTimer = 0f;
        }

        if (body.position.y < failsafeY)
            StartCoroutine(ReturnRoutine(0f, 1, "Out of bounds!"));
    }

    /// <summary>
    /// Called for a real restart or new tee. Hazard returns deliberately retain these checkpoints.
    /// Canceling delayed returns prevents an old water recovery from teleporting a new round.
    /// </summary>
    public void ResetCheckpoint(Vector3 spawn)
    {
        ResolveReferences();
        StopAllCoroutines();
        RestoreFloatGravity();
        resetting = false;
        hazardContacts = 0;
        restTimer = 0f;
        landedSinceReset = true;
        message = null;
        messageUntil = 0f;
        safePoint = teePoint = Capture(spawn, null);
        hasSafeSpot = hasTeeSpot = true;

        // The ball has just been teleported. Synchronize once so an actual moving tee
        // can be recorded in its own coordinates, even before the first resting frame.
        Physics.SyncTransforms();
        if (body != null && TryGetGround(out RaycastHit ground))
            safePoint = teePoint = Capture(spawn, ground.rigidbody);
    }

    private SupportCheckpoint Capture(Vector3 position, Rigidbody support)
    {
        bool tracksSupport = support != null && support != body;
        return new SupportCheckpoint
        {
            worldPosition = position,
            support = tracksSupport ? support.transform : null,
            localPosition = tracksSupport ? support.transform.InverseTransformPoint(position) : position,
            usesSupport = tracksSupport
        };
    }

    private static bool TryResolve(SupportCheckpoint checkpoint, out Vector3 position, out Rigidbody support)
    {
        support = null;
        position = checkpoint.worldPosition;
        if (!checkpoint.usesSupport)
            return true;
        if (checkpoint.support == null || !checkpoint.support.gameObject.activeInHierarchy)
            return false;

        position = checkpoint.support.TransformPoint(checkpoint.localPosition);
        support = checkpoint.support.GetComponent<Rigidbody>();
        return true;
    }

    private bool TryGetGround(out RaycastHit hit)
    {
        if (body != null && Physics.Raycast(body.position, Vector3.down, out hit,
            Radius + 0.12f, ~0, QueryTriggerInteraction.Ignore))
            return hit.rigidbody != body && Vector3.Dot(hit.normal, Vector3.up) >= 0.55f;
        hit = default;
        return false;
    }

    private void Float()
    {
        float target = floatSurfaceY - Radius * 0.35f + Mathf.Sin(Time.time * 3.2f) * 0.035f;
        Vector3 velocity = Velocity;
        float vy = Mathf.Lerp(velocity.y, (target - body.position.y) * 6f, 0.25f);
        Vector3 drift = new Vector3(velocity.x, 0f, velocity.z) * 0.94f;
        SetVelocity(new Vector3(drift.x, vy, drift.z));
        body.angularVelocity *= 0.9f;
    }

    private void RestoreFloatGravity()
    {
        if (floating && body != null)
            body.useGravity = gravityBeforeFloat;
        floating = false;
    }

    private void SetVelocity(Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = velocity;
#else
        body.velocity = velocity;
#endif
    }

    private void OnTriggerEnter(Collider other)
    {
        AeroHazard hazard = other.GetComponentInParent<AeroHazard>();
        if (hazard == null) return;
        hazardContacts++;
        if (!resetting && ball != null && ball.enabled)
            StartCoroutine(ReturnRoutine(hazard.resetDelay, hazard.penaltyStrokes, hazard.label,
                hazard.floats ? hazard.WaterSurfaceY : float.NaN));
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<AeroHazard>() != null)
            hazardContacts = Mathf.Max(0, hazardContacts - 1);
    }

    private IEnumerator ReturnRoutine(float delay, int penalty, string label, float waterSurfaceY = float.NaN)
    {
        resetting = true;
        message = penalty > 0 ? $"{label}  +{penalty} stroke" : label;
        messageUntil = Time.time + delay + messageSeconds;
        if (!float.IsNaN(waterSurfaceY) && body != null)
        {
            gravityBeforeFloat = body.useGravity;
            floating = true;
            floatSurfaceY = waterSurfaceY;
            body.useGravity = false;
        }
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        RestoreFloatGravity();

        if (ball != null && ball.enabled && (hasSafeSpot || hasTeeSpot))
        {
            bool useTee = (!landedSinceReset || !hasSafeSpot) && hasTeeSpot;
            SupportCheckpoint checkpoint = useTee ? teePoint : safePoint;
            if (!TryResolve(checkpoint, out Vector3 destination, out Rigidbody support))
            {
                // A removed or disabled moving deck is no longer a safe landing point.
                if (!hasTeeSpot || !TryResolve(teePoint, out destination, out support))
                {
                    destination = teePoint.worldPosition;
                    support = null;
                }
            }

            Vector3 supportVelocity = support != null ? support.GetPointVelocity(destination) : Vector3.zero;
            ball.ReturnToSafeSpot(destination + Vector3.up * 0.05f, penalty);
            if (body != null && !body.isKinematic)
                SetVelocity(supportVelocity);
        }
        landedSinceReset = false;
        hazardContacts = 0;
        restTimer = 0f;
        resetting = false;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(message) || Time.time > messageUntil)
            return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 38, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(0.85f, 0.97f, 1f);
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0.02f, 0.25f, 0.45f, 0.85f);
        }
        Rect rect = new Rect(0f, Screen.height * 0.22f, Screen.width, 60f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), message, shadow);
        GUI.Label(rect, message, style);
    }
}
