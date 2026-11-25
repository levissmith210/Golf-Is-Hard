using UnityEngine;

/// <summary>
/// Carries a complete cup, its trigger, and its perforated landing deck along a readable path.
/// All solid deck and cup colliders should be children of this kinematic body.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class AeroMovingCupPlatform : MonoBehaviour
{
    [Header("Travel")]
    [Tooltip("Offset between the two path endpoints, in the parent's local space.")]
    [SerializeField] private Vector3 localTravel = new Vector3(3f, 0f, 0f);
    [Tooltip("Seconds for a complete out-and-back cycle. Motion slows at each endpoint.")]
    [SerializeField, Min(2f)] private float periodSeconds = 10f;
    [Tooltip("Position in the motion cycle at startup. The authored position is retained at every phase.")]
    [SerializeField, Range(0f, 1f)] private float phase01;
    [SerializeField] private bool motionEnabled = true;

    [Header("Cup")]
    [Tooltip("The entire platform stops when this cup is completed so the ball can settle inside it.")]
    [SerializeField] private HoleTrigger cup;

    private Rigidbody body;
    private Vector3 homeLocalPosition;
    private Vector3 pathStartLocal;
    private float elapsed;
    private bool initialized;
    private Vector3 platformVelocity;

    public HoleTrigger Cup => cup;
    public bool MotionEnabled => motionEnabled;
    public Vector3 PlatformVelocity => platformVelocity;
    public float MaximumWorldSpeed => WorldVector(localTravel).magnitude * Mathf.PI / Mathf.Max(2f, periodSeconds);

    private void Awake()
    {
        Initialize();
    }

    private void OnValidate()
    {
        periodSeconds = Mathf.Max(2f, periodSeconds);
        phase01 = Mathf.Clamp01(phase01);
    }

    /// <summary>Configures travel in the parent's local coordinates without changing the authored pose.</summary>
    public void Configure(Vector3 travelInParentSpace, float cycleSeconds, float startingPhase, HoleTrigger targetCup)
    {
        localTravel = travelInParentSpace;
        periodSeconds = Mathf.Max(2f, cycleSeconds);
        phase01 = Mathf.Clamp01(startingPhase);
        cup = targetCup;
        ConfigureBody();
        if (Application.isPlaying)
        {
            initialized = false;
            Initialize();
        }
    }

    /// <summary>Configures an exact world-space displacement, including under a scaled parent.</summary>
    public void ConfigureWorldTravel(Vector3 travelInWorldSpace, float cycleSeconds, float startingPhase, HoleTrigger targetCup)
    {
        Vector3 parentSpace = transform.parent != null
            ? transform.parent.InverseTransformVector(travelInWorldSpace)
            : travelInWorldSpace;
        Configure(parentSpace, cycleSeconds, startingPhase, targetCup);
    }

    public void SetMotionEnabled(bool value)
    {
        motionEnabled = value;
        if (!value)
            platformVelocity = Vector3.zero;
    }

    /// <summary>Returns to the authored pose and restarts the same deterministic motion cycle.</summary>
    public void ResetMotion()
    {
        Initialize();
        elapsed = 0f;
        platformVelocity = Vector3.zero;
        Vector3 worldHome = WorldPoint(homeLocalPosition);
        body.position = worldHome;
        transform.position = worldHome;
    }

    private void Initialize()
    {
        if (initialized)
            return;

        ConfigureBody();
        homeLocalPosition = transform.localPosition;
        pathStartLocal = homeLocalPosition - localTravel * Wave(phase01);
        elapsed = 0f;
        initialized = true;
    }

    private void ConfigureBody()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void FixedUpdate()
    {
        if (!initialized)
            Initialize();

        if (!motionEnabled || (cup != null && cup.IsCompleted))
        {
            platformVelocity = Vector3.zero;
            body.MovePosition(body.position);
            return;
        }

        // A local clock makes restart deterministic and remains still while the game is paused.
        elapsed += Time.fixedDeltaTime;
        float cycle = phase01 + elapsed / Mathf.Max(2f, periodSeconds);
        Vector3 target = WorldPoint(pathStartLocal + localTravel * Wave(cycle));
        platformVelocity = (target - body.position) / Mathf.Max(0.0001f, Time.fixedDeltaTime);
        body.MovePosition(target);
    }

    private static float Wave(float cycle)
    {
        return 0.5f - 0.5f * Mathf.Cos(cycle * Mathf.PI * 2f);
    }

    private Vector3 WorldPoint(Vector3 point)
    {
        return transform.parent != null ? transform.parent.TransformPoint(point) : point;
    }

    private Vector3 WorldVector(Vector3 vector)
    {
        return transform.parent != null ? transform.parent.TransformVector(vector) : vector;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 start = initialized
            ? pathStartLocal
            : transform.localPosition - localTravel * Wave(phase01);
        Vector3 worldStart = WorldPoint(start);
        Vector3 worldEnd = WorldPoint(start + localTravel);
        Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.85f);
        Gizmos.DrawLine(worldStart, worldEnd);
        Gizmos.DrawWireSphere(worldStart, 0.18f);
        Gizmos.color = new Color(0.45f, 1f, 0.65f, 0.85f);
        Gizmos.DrawWireSphere(worldEnd, 0.18f);
    }
}
