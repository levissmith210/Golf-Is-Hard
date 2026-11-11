using System.Collections;
using UnityEngine;

/// <summary>
/// Remembers where the ball last came to rest on solid ground, and puts it back there
/// (with a penalty stroke) when it lands in an <see cref="AeroHazard"/> or falls out of the world.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BallPlayerController))]
public class AeroBallSafety : MonoBehaviour
{
    [SerializeField, Min(0f)] private float restSpeed = 0.25f;
    [SerializeField, Min(0f)] private float restTime = 0.35f;
    [SerializeField] private float failsafeY = -25f;
    [SerializeField, Min(0f)] private float messageSeconds = 1.8f;

    private BallPlayerController ball;
    private Rigidbody body;
    private SphereCollider sphere;
    private Vector3 safeSpot, teeSpot;
    private bool hasSafeSpot, hasTeeSpot;
    private bool landedSinceReset = true;
    private float restTimer;
    private int hazardContacts;
    private bool resetting;
    private string message;
    private float messageUntil;
    private GUIStyle style, shadow;

    public Vector3 SafeSpot => safeSpot;
    public bool IsResetting => resetting;

    private void Awake()
    {
        ball = GetComponent<BallPlayerController>();
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
    }

    private float Radius => sphere != null ? sphere.radius * Mathf.Max(transform.lossyScale.x, 0.01f) : 0.25f;

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
        if (resetting || body == null || body.isKinematic)
            return;

        if (hazardContacts == 0 && Velocity.magnitude < restSpeed && Grounded())
        {
            restTimer += Time.fixedDeltaTime;
            if (restTimer >= restTime)
            {
                safeSpot = body.position;
                hasSafeSpot = true;
                // Resting before the first shot means we're on the tee.
                if (ball.StrokeCount == 0) { teeSpot = body.position; hasTeeSpot = true; }
                landedSinceReset = true;
            }
        }
        else
        {
            restTimer = 0f;
        }

        if (body.position.y < failsafeY)
            StartCoroutine(ReturnRoutine(0f, 1, "Out of bounds!"));
    }

    private bool Grounded()
    {
        return Physics.Raycast(body.position, Vector3.down, Radius + 0.12f, ~0, QueryTriggerInteraction.Ignore);
    }

    private void OnTriggerEnter(Collider other)
    {
        var hazard = other.GetComponentInParent<AeroHazard>();
        if (hazard == null) return;
        hazardContacts++;
        if (!resetting && ball.enabled)
            StartCoroutine(ReturnRoutine(hazard.resetDelay, hazard.penaltyStrokes, hazard.label));
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<AeroHazard>() != null)
            hazardContacts = Mathf.Max(0, hazardContacts - 1);
    }

    private IEnumerator ReturnRoutine(float delay, int penalty, string label)
    {
        resetting = true;
        message = penalty > 0 ? $"{label}  +{penalty} stroke" : label;
        messageUntil = Time.time + delay + messageSeconds;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        // The hole may have been finished or restarted while we waited.
        if (ball.enabled && (hasSafeSpot || hasTeeSpot))
        {
            // If the last return never came to rest before hitting a hazard again, that spot isn't safe: use the tee.
            bool useTee = (!landedSinceReset || !hasSafeSpot) && hasTeeSpot;
            ball.ReturnToSafeSpot((useTee ? teeSpot : safeSpot) + Vector3.up * 0.05f, penalty);
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
        var r = new Rect(0f, Screen.height * 0.22f, Screen.width, 60f);
        GUI.Label(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), message, shadow);
        GUI.Label(r, message, style);
    }
}
