using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lets the golf ball charge and fire a shot in the direction the camera faces.
/// Attach this component to the ball GameObject, which must also have a Rigidbody.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallPlayerController : MonoBehaviour
{
    private sealed class StretchableMesh
    {
        public MeshFilter filter;
        public Mesh originalMesh;
        public Mesh stretchedMesh;
        public Vector3 center;
        public Vector3[] originalVertices;
        public Vector3[] stretchedVertices;
    }

    [Header("Shot")]
    [Tooltip("Camera used to aim. If empty, the camera tagged MainCamera is used.")]
    [SerializeField] private Camera aimCamera;

    [Tooltip("Time in seconds to reach full power.")]
    [SerializeField, Min(0.01f)] private float maxChargeTime = 2f;

    [Tooltip("Multiplies the charge rate. 2 means the bar fills twice as fast.")]
    [SerializeField, Min(0.1f)] private float chargeSpeedMultiplier = 2f;

    [Tooltip("Impulse applied when releasing at zero charge.")]
    [SerializeField, Min(0f)] private float minimumShotForce = 4.5f;

    [Tooltip("Impulse applied when releasing at full charge. Rigidbody mass affects the resulting speed.")]
    [SerializeField, Min(0f)] private float maximumShotForce = 21f;

    [Tooltip("Scales the selected shot force. 1.35 is 35 percent more powerful.")]
    [SerializeField, Min(0f)] private float shotForceMultiplier = 1.35f;

    [Header("Shot Response")]
    [Tooltip("Briefly eases out high existing speed when a new shot is weaker, preventing a sudden visual jerk.")]
    [SerializeField, Range(0f, 0.25f)] private float fastMomentumReplacementTime = 0.16f;

    [Tooltip("Light rolling drag applied only while grounded. Higher values help the ball settle sooner without abruptly stopping it.")]
    [SerializeField, Range(0f, 1.5f)] private float groundedRollingDrag = 0.65f;

    [Tooltip("Extra gravity while airborne. Values above 1 shorten the floaty part of lofted shots.")]
    [SerializeField, Range(1f, 2f)] private float airborneGravityMultiplier = 1.2f;

    [Header("Distance & Surfaces")]
    [Tooltip("Multiplies every shot's launch speed. 1 = original distance. Carry grows roughly with the square of this.")]
    [SerializeField, Range(0.5f, 2f)] private float shotDistanceBoost = 1.25f;
    [Tooltip("Scales Grounded Rolling Drag on ground with no surface info (lower = rolls further).")]
    [SerializeField, Range(0.1f, 2f)] private float defaultRollDragScale = 0.6f;
    [Tooltip("Let the surface under the ball (fairway, rough, sand, green...) change shot power, roll and landing.")]
    [SerializeField] private bool useSurfaceFeel = true;
    [SerializeField] private SurfaceFeel fairwayFeel = new SurfaceFeel(1.0f, 0.45f, 0.75f);
    [SerializeField] private SurfaceFeel roughFeel = new SurfaceFeel(0.72f, 1.15f, 0.6f);
    [SerializeField] private SurfaceFeel sandFeel = new SurfaceFeel(0.5f, 2.8f, 0.22f);
    [SerializeField] private SurfaceFeel greenFeel = new SurfaceFeel(0.8f, 0.28f, 0.75f);
    [SerializeField] private SurfaceFeel teeFeel = new SurfaceFeel(1.0f, 0.45f, 0.75f);
    [SerializeField] private SurfaceFeel hardFeel = new SurfaceFeel(1.0f, 0.2f, 1.0f);
    [SerializeField] private bool showLieBadge = true;

    [System.Serializable]
    public class SurfaceFeel
    {
        [Tooltip("Shot power when hitting from this surface (1 = full).")]
        [Range(0.1f, 1.5f)] public float shotPower = 1f;
        [Tooltip("Rolling drag while on this surface (higher stops sooner).")]
        [Range(0f, 4f)] public float rollingDrag = 0.4f;
        [Tooltip("Fraction of speed kept when the ball lands on this surface (sand grabs the ball).")]
        [Range(0.05f, 1f)] public float landingKeep = 1f;

        public SurfaceFeel() { }
        public SurfaceFeel(float power, float drag, float keep) { shotPower = power; rollingDrag = drag; landingKeep = keep; }
    }

    /// <summary>The surface the ball is resting or rolling on (last known while airborne).</summary>
    public AeroSurfaceType CurrentSurface { get; private set; }

    [Header("Shot Direction")]
    [Tooltip("Hold W to raise the shot and S to lower it. The allowed range is 0 to 50 degrees.")]
    [SerializeField, Range(0f, 50f)] private float shotElevationDegrees;
    [SerializeField, Min(1f)] private float elevationChangeSpeed = 45f;
    [SerializeField] private Color directionArrowColor = new Color(0.18f, 0.78f, 0.96f, 0.52f);
    [SerializeField, Min(0.5f)] private float directionArrowLength = 2.6f;
    [Tooltip("Scales the arrow length while leaving its head shape intact.")]
    [SerializeField, Range(0.25f, 1f)] private float directionArrowLengthMultiplier = 0.65f;
    [SerializeField, Min(0f)] private float directionArrowStartOffset = 0.55f;
    [Tooltip("Extra space between the ball and the arrow shaft.")]
    [SerializeField, Min(0f)] private float directionArrowGap = 0.25f;
    [SerializeField, Min(0.05f)] private float directionArrowShaftWidth = 0.1f;
    [SerializeField, Min(0.1f)] private float directionArrowHeadLength = 0.45f;
    [SerializeField, Min(0.05f)] private float directionArrowHeadWidth = 0.28f;

    [Header("Ground Contact")]
    [Tooltip("Layers that count as ground. Ground contact must also have an upward-facing surface.")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField, Range(0f, 1f)] private float minimumGroundUpDot = 0.55f;

    [Header("Cartoon Ball Motion")]
    [Tooltip("Maximum visual stretch at high speed. The collider is not changed.")]
    [SerializeField, Range(0f, 0.25f)] private float maximumSpeedStretch = 0.1f;
    [SerializeField, Min(0.1f)] private float speedForMaximumStretch = 20f;
    [Tooltip("How much the ball visually compresses at the instant of a shot.")]
    [SerializeField, Range(0f, 0.3f)] private float shotImpactSquash = 0.14f;

    [Header("Power Bar")]
    [SerializeField] private Vector2 powerBarSize = new Vector2(320f, 24f);
    [Tooltip("Distance from the bottom edge of the screen to the power bar.")]
    [SerializeField, Min(0f)] private float powerBarBottomOffset = 90f;
    [SerializeField] private Color powerBarBackground = new Color(0.08f, 0.1f, 0.12f, 0.9f);
    [SerializeField] private Color powerBarFill = new Color(0.25f, 0.8f, 0.35f, 1f);

    [Header("Surface Friction")]
    [Tooltip("Higher friction helps the ball grip the course surface.")]
    [SerializeField, Range(0f, 1f)] private float ballStaticFriction = 1f;
    [SerializeField, Range(0f, 1f)] private float ballDynamicFriction = 0.9f;
    [Tooltip("A subtle surface bounce for a small, cartoony hop on landings.")]
    [SerializeField, Range(0f, 1f)] private float ballBounciness = 0.25f;
    [Tooltip("Allows enough spin for the ball to roll smoothly at full shot speed.")]
    [SerializeField, Min(7f)] private float maxBallAngularVelocity = 55f;

    [Header("Sound Effects")]
    [Tooltip("Short sound when a grounded shot starts charging.")]
    [SerializeField] private AudioClip chargeStartSound;
    [Tooltip("Sound played when a shot launches.")]
    [SerializeField] private AudioClip shotLaunchSound;
    [Tooltip("One-time cue when the power bar reaches full charge.")]
    [SerializeField] private AudioClip fullChargeSound;
    [Tooltip("Sound played when a charged shot is canceled with right click.")]
    [SerializeField] private AudioClip shotCancelSound;
    [Tooltip("Quiet tick played as W or S changes the shot angle.")]
    [SerializeField] private AudioClip angleAdjustSound;
    [Tooltip("Sound played when the ball lands or bounces on the ground.")]
    [SerializeField] private AudioClip groundBounceSound;
    [Tooltip("Sound played when the ball hits a wall or other obstacle.")]
    [SerializeField] private AudioClip obstacleImpactSound;
    [Tooltip("Optional looping sound while the ball rolls along the ground.")]
    [SerializeField] private AudioClip rollingLoopSound;
    [SerializeField] private AudioSource soundEffectsSource;
    [SerializeField, Range(0f, 1f)] private float shotSoundVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float smallCueVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float impactSoundVolume = 0.8f;
    [SerializeField, Min(0f)] private float minimumImpactSpeedForSound = 1.25f;
    [SerializeField, Min(0.1f)] private float impactSpeedForFullVolume = 12f;
    [SerializeField, Min(0f)] private float impactSoundCooldown = 0.12f;
    [SerializeField, Min(0f)] private float angleTickInterval = 0.14f;
    [SerializeField, Range(0f, 1f)] private float rollingSoundVolume = 0.38f;
    [SerializeField, Min(0f)] private float rollingSoundStartSpeed = 0.5f;
    [SerializeField, Min(0.1f)] private float rollingSpeedForFullVolume = 12f;

    private Rigidbody ballRigidbody;
    private Collider ballCollider;
    private PhysicsMaterial runtimeBallMaterial;
    private Texture2D pillTexture;
    private Texture2D degreeCircleTexture;
    private GameObject runtimeArrowObject;
    private Mesh runtimeArrowMesh;
    private MeshRenderer directionArrowRenderer;
    private Material runtimeArrowMaterial;
    private Material runtimeArrowGlowMaterial;
    private Material runtimeArrowInnerGlowMaterial;
    private readonly List<StretchableMesh> stretchableMeshes = new List<StretchableMesh>();
    private readonly HashSet<Collider> groundedColliders = new HashSet<Collider>();
    private bool isCharging;
    private bool showAngleIndicator;
    private bool isReplacingMomentum;
    private bool holeCompleted;
    private float chargeStartedAt;
    private float lastAngleIndicatorInputTime;
    private float momentumReplacementElapsed;
    private float lastShotTime = -100f;
    private float lastAngleSoundTime = -100f;
    private float lastImpactSoundTime = -100f;
    private bool fullChargeSoundPlayed;
    private bool tutorialCanShoot = true;
    private bool tutorialCanAdjustAngle = true;
    private AudioSource rollingSoundSource;
    private Vector3 lastShotDirection = Vector3.forward;
    private Vector3 momentumReplacementStartVelocity;
    private Vector3 momentumReplacementTargetVelocity;
    private Vector3 momentumReplacementStartAngularVelocity;
    private GUIStyle angleLabelStyle;

    /// <summary>Number of shots successfully fired during this round.</summary>
    public int StrokeCount { get; private set; }
    public bool TutorialWInputUsed { get; private set; }
    public bool TutorialSInputUsed { get; private set; }
    public bool TutorialAngleInputUsed => TutorialWInputUsed && TutorialSInputUsed;

    public void SetTutorialControlAccess(bool canShoot, bool canAdjustAngle)
    {
        tutorialCanShoot = canShoot;
        tutorialCanAdjustAngle = canAdjustAngle;
        if (!canShoot && isCharging)
            CancelShot();
        if (!canAdjustAngle && !isCharging && runtimeArrowObject != null)
            runtimeArrowObject.SetActive(false);
    }

    public void ResetTutorialAngleInputProgress()
    {
        TutorialWInputUsed = false;
        TutorialSInputUsed = false;
    }

    private float ChargeProgress
    {
        get
        {
            if (!isCharging)
                return 0f;

            float duration = Mathf.Max(0.01f, maxChargeTime);
            float speed = Mathf.Max(0.1f, chargeSpeedMultiplier);
            return Mathf.Clamp01((Time.time - chargeStartedAt) / (duration / speed));
        }
    }

    private void Awake()
    {
        ballRigidbody = GetComponent<Rigidbody>();
        SetupSoundEffects();
        ballRigidbody.maxAngularVelocity = maxBallAngularVelocity;
        ApplyBallFriction();
        pillTexture = CreatePillTexture(512, 64);
        degreeCircleTexture = CreateCircleTexture(32);
        SetupStretchableMeshes();
        SetupDirectionArrow();

        if (ballRigidbody.isKinematic)
            Debug.LogWarning("BallPlayerController uses Rigidbody physics. Turn off Is Kinematic on the ball Rigidbody.", this);

        if (aimCamera == null)
            aimCamera = Camera.main;
    }

    private void OnDisable()
    {
        StopRollingSound();
    }

    private void OnDestroy()
    {
        if (pillTexture != null)
            Destroy(pillTexture);

        if (degreeCircleTexture != null)
            Destroy(degreeCircleTexture);

        if (runtimeBallMaterial != null)
            Destroy(runtimeBallMaterial);

        if (runtimeArrowMaterial != null)
            Destroy(runtimeArrowMaterial);

        if (runtimeArrowGlowMaterial != null)
            Destroy(runtimeArrowGlowMaterial);

        if (runtimeArrowInnerGlowMaterial != null)
            Destroy(runtimeArrowInnerGlowMaterial);

        if (runtimeArrowMesh != null)
            Destroy(runtimeArrowMesh);

        if (runtimeArrowObject != null)
            Destroy(runtimeArrowObject);

        for (int i = 0; i < stretchableMeshes.Count; i++)
        {
            StretchableMesh visual = stretchableMeshes[i];
            if (visual.filter != null)
                visual.filter.sharedMesh = visual.originalMesh;
            if (visual.stretchedMesh != null)
                Destroy(visual.stretchedMesh);
        }
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            if (isCharging)
                CancelShot();
            return;
        }

        if (tutorialCanAdjustAngle)
            UpdateShotElevation();
        UpdateAngleIndicatorTimeout();
        UpdateRollingSound();

        if (!tutorialCanShoot)
        {
            if (isCharging)
                CancelShot();
            return;
        }

        if (aimCamera == null)
            aimCamera = Camera.main;

        if (aimCamera != null)
        {
            ThirdPersonCameraController cameraController = aimCamera.GetComponent<ThirdPersonCameraController>();
            if (cameraController != null && cameraController.CursorCapturedThisFrame)
                return;
        }

        // Check cancellation first so a right click wins if both buttons change this frame.
        if (isCharging && Input.GetMouseButtonDown(1))
        {
            CancelShot();
            return;
        }

        if (isCharging && Input.GetMouseButtonUp(0))
        {
            ReleaseShot();
            return;
        }

        if (!isCharging && Input.GetMouseButtonDown(0))
        {
            if (!IsTouchingGround())
                return;

            isCharging = true;
            showAngleIndicator = true;
            lastAngleIndicatorInputTime = Time.unscaledTime;
            chargeStartedAt = Time.time;
            fullChargeSoundPlayed = false;
            PlayBallSound(chargeStartSound, smallCueVolume);
        }

        if (isCharging && !fullChargeSoundPlayed && ChargeProgress >= 1f)
        {
            fullChargeSoundPlayed = true;
            PlayBallSound(fullChargeSound, smallCueVolume);
        }
    }

    private void UpdateShotElevation()
    {
        float elevationInput = 0f;
        bool angleKeyActive = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S);
        if (Input.GetKey(KeyCode.W))
        {
            elevationInput += 1f;
            TutorialWInputUsed = true;
        }
        if (Input.GetKey(KeyCode.S))
        {
            elevationInput -= 1f;
            TutorialSInputUsed = true;
        }

        if (angleKeyActive)
        {
            showAngleIndicator = true;
            lastAngleIndicatorInputTime = Time.unscaledTime;
        }

        float previousElevation = shotElevationDegrees;
        shotElevationDegrees = Mathf.Clamp(
            shotElevationDegrees + elevationInput * elevationChangeSpeed * Time.deltaTime,
            0f,
            50f);

        if (Mathf.Abs(shotElevationDegrees - previousElevation) > 0.01f
            && Time.unscaledTime - lastAngleSoundTime >= angleTickInterval)
        {
            lastAngleSoundTime = Time.unscaledTime;
            PlayBallSound(angleAdjustSound, smallCueVolume);
        }
    }

    private void UpdateAngleIndicatorTimeout()
    {
        if (!showAngleIndicator)
            return;

        bool mouseButtonActive = false;
        for (int button = 0; button < 3; button++)
        {
            if (Input.GetMouseButton(button)
                || Input.GetMouseButtonDown(button)
                || Input.GetMouseButtonUp(button))
            {
                mouseButtonActive = true;
                break;
            }
        }

        bool angleKeyActive = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S);
        if (mouseButtonActive || angleKeyActive)
        {
            lastAngleIndicatorInputTime = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - lastAngleIndicatorInputTime >= 3f)
        {
            showAngleIndicator = false;
            if (runtimeArrowObject != null)
                runtimeArrowObject.SetActive(false);
        }
    }

    private void FixedUpdate()
    {
        if (ballRigidbody == null || ballRigidbody.isKinematic)
            return;

        if (ballRigidbody.useGravity && airborneGravityMultiplier > 1f && !IsTouchingGround())
        {
            ballRigidbody.AddForce(
                Physics.gravity * (airborneGravityMultiplier - 1f),
                ForceMode.Acceleration);
        }

        if (isReplacingMomentum)
        {
            momentumReplacementElapsed += Time.fixedDeltaTime;
            float duration = Mathf.Max(0.001f, fastMomentumReplacementTime);
            float progress = Mathf.Clamp01(momentumReplacementElapsed / duration);
            // Quintic smoothstep starts and ends gently, avoiding a sharp change in acceleration.
            float easedProgress = progress * progress * progress
                * (progress * (progress * 6f - 15f) + 10f);

            Vector3 desiredVelocity = Vector3.Lerp(
                momentumReplacementStartVelocity,
                momentumReplacementTargetVelocity,
                easedProgress);
            Vector3 desiredAngularVelocity = Vector3.Lerp(
                momentumReplacementStartAngularVelocity,
                Vector3.zero,
                easedProgress);

            // Correct velocity through the physics solver so ground contact, gravity, and collisions
            // still participate during the short handoff instead of being overwritten each step.
            ballRigidbody.AddForce(desiredVelocity - GetBallVelocity(), ForceMode.VelocityChange);
            ballRigidbody.AddTorque(desiredAngularVelocity - ballRigidbody.angularVelocity, ForceMode.VelocityChange);

            if (progress >= 1f)
            {
                isReplacingMomentum = false;
                ballRigidbody.WakeUp();
            }

            return;
        }

        UpdateSurface();
        float rollingDrag = CurrentRollingDrag();
        if (rollingDrag <= 0f || !IsTouchingGround())
            return;

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(GetBallVelocity(), Vector3.up);
        if (horizontalVelocity.sqrMagnitude > 0.0025f)
            ballRigidbody.AddForce(-horizontalVelocity * rollingDrag, ForceMode.Acceleration);
    }

    private float BallRadius
    {
        get
        {
            if (ballCollider == null) ballCollider = GetComponent<Collider>();
            return ballCollider != null ? ballCollider.bounds.extents.y : 0.25f;
        }
    }

    /// <summary>Reads the surface under the ball from whatever ground it is touching.</summary>
    private void UpdateSurface()
    {
        if (!useSurfaceFeel) { CurrentSurface = AeroSurfaceType.Default; return; }
        foreach (Collider collider in groundedColliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            CurrentSurface = AeroSurface.Lookup(collider, transform.position + Vector3.down * BallRadius);
            return;
        }
    }

    private SurfaceFeel FeelFor(AeroSurfaceType surface)
    {
        switch (surface)
        {
            case AeroSurfaceType.Fairway: return fairwayFeel;
            case AeroSurfaceType.Rough: return roughFeel;
            case AeroSurfaceType.Sand: return sandFeel;
            case AeroSurfaceType.Green: return greenFeel;
            case AeroSurfaceType.Tee: return teeFeel;
            case AeroSurfaceType.Hard: return hardFeel;
            default: return null;
        }
    }

    private float CurrentRollingDrag()
    {
        SurfaceFeel feel = useSurfaceFeel ? FeelFor(CurrentSurface) : null;
        return feel != null ? feel.rollingDrag : groundedRollingDrag * defaultRollDragScale;
    }

    /// <summary>Shot power multiplier from the lie (rough and sand take power off).</summary>
    private float LiePowerMultiplier()
    {
        SurfaceFeel feel = useSurfaceFeel ? FeelFor(CurrentSurface) : null;
        return feel != null ? feel.shotPower : 1f;
    }

    /// <summary>Soft surfaces grab the ball when it lands (sand plugs it, rough deadens it).</summary>
    private void ApplyLandingFeel(Collision collision)
    {
        if (!useSurfaceFeel || isReplacingMomentum || collision.relativeVelocity.magnitude < 2.5f)
            return;
        ContactPoint[] contacts = collision.contacts;
        for (int i = 0; i < contacts.Length; i++)
        {
            if (Vector3.Dot(contacts[i].normal, Vector3.up) < minimumGroundUpDot) continue;
            SurfaceFeel feel = FeelFor(AeroSurface.Lookup(collision.collider, contacts[i].point));
            if (feel == null || feel.landingKeep >= 0.999f) return;
            SetBallVelocity(GetBallVelocity() * feel.landingKeep);
            ballRigidbody.angularVelocity *= feel.landingKeep;
            return;
        }
    }

    private void SetBallVelocity(Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        ballRigidbody.linearVelocity = velocity;
#else
        ballRigidbody.velocity = velocity;
#endif
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f)
            return;

        UpdateDirectionArrow();
        UpdateBallStretch();
    }

    private void ReleaseShot()
    {
        float power = ChargeProgress;
        isCharging = false;
        showAngleIndicator = false;
        fullChargeSoundPlayed = false;

        if (!IsTouchingGround())
            return;

        if (aimCamera == null)
            aimCamera = Camera.main;

        if (aimCamera == null)
        {
            Debug.LogWarning("BallPlayerController needs an aim camera or a camera tagged MainCamera.", this);
            return;
        }

        UpdateSurface();
        float shotForce = Mathf.Lerp(minimumShotForce, maximumShotForce, power) * shotForceMultiplier
            * shotDistanceBoost * LiePowerMultiplier();
        Vector3 shotDirection = GetShotDirection();
        StrokeCount++;
        lastShotDirection = shotDirection;
        lastShotTime = Time.unscaledTime;

        // A force-mode impulse from rest produces delta-v = impulse / mass.
        // Use that as the replacement velocity so the new shot never inherits old travel.
        float safeMass = Mathf.Max(0.0001f, ballRigidbody.mass);
        Vector3 targetVelocity = shotDirection * (shotForce / safeMass);
        Vector3 currentVelocity = GetBallVelocity();
        bool replaceFastMotionSmoothly = fastMomentumReplacementTime > 0f
            && currentVelocity.magnitude >= 4f
            && (currentVelocity - targetVelocity).magnitude >= 4f;

        isReplacingMomentum = replaceFastMotionSmoothly;
        if (isReplacingMomentum)
        {
            momentumReplacementElapsed = 0f;
            momentumReplacementStartVelocity = currentVelocity;
            momentumReplacementTargetVelocity = targetVelocity;
            momentumReplacementStartAngularVelocity = ballRigidbody.angularVelocity;
        }
        else
        {
            ballRigidbody.angularVelocity = Vector3.zero;
            ballRigidbody.AddForce(shotDirection * shotForce, ForceMode.Impulse);
        }

        ballRigidbody.WakeUp();
        PlayBallSound(shotLaunchSound, shotSoundVolume);
    }

    /// <summary>Stops further shots while leaving Rigidbody motion active so the ball can sink.</summary>
    public void StopForHole()
    {
        if (holeCompleted)
            return;

        holeCompleted = true;
        isCharging = false;
        showAngleIndicator = false;
        isReplacingMomentum = false;
        StopRollingSound();

        if (runtimeArrowObject != null)
            runtimeArrowObject.SetActive(false);

        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();

        if (ballRigidbody != null)
            ballRigidbody.WakeUp();

        // Restore the ball's normal shape in case the hole was reached during a fast roll.
        lastShotTime = -100f;
        UpdateBallStretch();
        enabled = false;
    }

    /// <summary>Places the ball at the next tee and restores shot control for a new hole.</summary>
    public void ResetForNextHole(Vector3 position, Quaternion rotation)
    {
        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();

        if (ballRigidbody != null)
            ballRigidbody.isKinematic = true;

        transform.SetPositionAndRotation(position, rotation);
        if (ballRigidbody != null)
        {
            ballRigidbody.position = position;
            ballRigidbody.rotation = rotation;
            ballRigidbody.isKinematic = false;
#if UNITY_6000_0_OR_NEWER
            ballRigidbody.linearVelocity = Vector3.zero;
#else
            ballRigidbody.velocity = Vector3.zero;
#endif
            ballRigidbody.angularVelocity = Vector3.zero;
        }

        StrokeCount = 0;
        isCharging = false;
        showAngleIndicator = false;
        isReplacingMomentum = false;
        shotElevationDegrees = 0f;
        holeCompleted = false;
        groundedColliders.Clear();
        lastShotTime = -100f;
        lastShotDirection = transform.forward;
        UpdateBallStretch();

        if (runtimeArrowObject != null)
            runtimeArrowObject.SetActive(false);

        if (ballRigidbody != null)
            ballRigidbody.WakeUp();

        enabled = true;
    }

    /// <summary>
    /// Puts the ball back on a safe spot during a hole (for example after a water hazard),
    /// keeping the strokes already taken and adding any penalty strokes.
    /// </summary>
    public void ReturnToSafeSpot(Vector3 position, int penaltyStrokes)
    {
        int strokesSoFar = StrokeCount;
        ResetForNextHole(position, transform.rotation);
        StrokeCount = strokesSoFar + Mathf.Max(0, penaltyStrokes);
    }

    private void OnCollisionEnter(Collision collision)
    {
        UpdateGroundContact(collision);
        ApplyLandingFeel(collision);
        PlayImpactSound(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        groundedColliders.Remove(collision.collider);
    }

    private void UpdateGroundContact(Collision collision)
    {
        Collider otherCollider = collision.collider;
        if (otherCollider == null || (groundLayers.value & (1 << otherCollider.gameObject.layer)) == 0)
        {
            groundedColliders.Remove(otherCollider);
            return;
        }

        bool hasGroundFacingContact = false;
        ContactPoint[] contacts = collision.contacts;
        for (int i = 0; i < contacts.Length; i++)
        {
            if (Vector3.Dot(contacts[i].normal, Vector3.up) >= minimumGroundUpDot)
            {
                hasGroundFacingContact = true;
                break;
            }
        }

        if (hasGroundFacingContact)
            groundedColliders.Add(otherCollider);
        else
            groundedColliders.Remove(otherCollider);
    }

    private bool IsTouchingGround()
    {
        foreach (Collider collider in groundedColliders)
        {
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private void CancelShot()
    {
        bool wasCharging = isCharging;
        isCharging = false;
        showAngleIndicator = false;
        fullChargeSoundPlayed = false;
        if (wasCharging)
            PlayBallSound(shotCancelSound, smallCueVolume);

        if (runtimeArrowObject != null)
            runtimeArrowObject.SetActive(false);
    }

    private void OnGUI()
    {
        if (Time.timeScale <= 0f)
            return;

        if (isCharging)
            DrawPowerBar();

        if (showAngleIndicator)
            DrawAngleIndicator();

    }

    private GUIStyle lieStyle;

    /// <summary>Small glass badge (bottom-left) showing the lie while the ball is at rest.</summary>
    private void DrawLieBadge()
    {
        if (!showLieBadge || !useSurfaceFeel || holeCompleted || ballRigidbody == null || ballRigidbody.isKinematic)
            return;
        SurfaceFeel feel = FeelFor(CurrentSurface);
        if (feel == null || !IsTouchingGround() || GetBallVelocity().magnitude > 0.35f)
            return;

        float s = HudScale;
        int fontSize = Mathf.Max(11, Mathf.RoundToInt(15f * s));
        if (lieStyle == null) lieStyle = FrutigerAeroUI.CreateStyle(fontSize, FrutigerAeroUI.Ink);
        lieStyle.fontSize = fontSize;
        string text = AeroSurface.Label(CurrentSurface);
        float w = lieStyle.CalcSize(new GUIContent(text)).x + 34f * s, h = 34f * s;
        Rect r = new Rect(18f * s, Screen.height - HudBottomOffset - h, w, h);
        Color accent;
        switch (CurrentSurface)
        {
            case AeroSurfaceType.Sand: accent = new Color(1f, 0.82f, 0.45f); break;
            case AeroSurfaceType.Rough: accent = new Color(0.25f, 0.72f, 0.35f); break;
            case AeroSurfaceType.Green: accent = new Color(0.55f, 0.95f, 0.4f); break;
            case AeroSurfaceType.Hard: accent = FrutigerAeroUI.Cyan; break;
            default: accent = new Color(0.4f, 0.86f, 0.32f); break;
        }
        FrutigerAeroUI.DrawGlassPanel(r, accent, new Color(0.86f, 0.98f, 1f, 0.92f), 0.16f);
        GUI.Label(new Rect(r.x + 17f * s, r.y, r.width - 20f * s, r.height), text, lieStyle);
    }

    private float HudScale
    {
        get
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return 1f;
            return 1.815f * Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        }
    }

    private float HudBottomOffset => Mathf.Max(0f, powerBarBottomOffset - 40f) * HudScale;

    private void SetupSoundEffects()
    {
        if (soundEffectsSource == null)
            soundEffectsSource = GetComponent<AudioSource>();
        if (soundEffectsSource == null)
            soundEffectsSource = gameObject.AddComponent<AudioSource>();

        soundEffectsSource.playOnAwake = false;
        // The camera stays several units from the ball, so keep one-shot cues clear and audible.
        // The continuous rolling loop below remains 3D and positional.
        soundEffectsSource.spatialBlend = 0f;
        soundEffectsSource.dopplerLevel = 0f;

        LoadSoundClip(chargeStartSound);
        LoadSoundClip(shotLaunchSound);
        LoadSoundClip(fullChargeSound);
        LoadSoundClip(shotCancelSound);
        LoadSoundClip(angleAdjustSound);
        LoadSoundClip(groundBounceSound);
        LoadSoundClip(obstacleImpactSound);
        LoadSoundClip(rollingLoopSound);

        if (rollingLoopSound == null)
            return;

        rollingSoundSource = gameObject.AddComponent<AudioSource>();
        rollingSoundSource.clip = rollingLoopSound;
        rollingSoundSource.loop = true;
        rollingSoundSource.playOnAwake = false;
        rollingSoundSource.spatialBlend = 1f;
        rollingSoundSource.dopplerLevel = 0f;
        rollingSoundSource.volume = 0f;
    }

    private static void LoadSoundClip(AudioClip clip)
    {
        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
    }

    private void PlayBallSound(AudioClip clip, float volume)
    {
        if (clip == null || soundEffectsSource == null)
            return;

        GameSettings.PlaySfx(soundEffectsSource, clip, volume);
    }

    private void PlayImpactSound(Collision collision)
    {
        if (collision == null)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < minimumImpactSpeedForSound
            || Time.time - lastImpactSoundTime < impactSoundCooldown)
            return;

        bool isGroundImpact = false;
        if (collision.collider != null
            && (groundLayers.value & (1 << collision.collider.gameObject.layer)) != 0)
        {
            ContactPoint[] contacts = collision.contacts;
            for (int i = 0; i < contacts.Length; i++)
            {
                if (Vector3.Dot(contacts[i].normal, Vector3.up) >= minimumGroundUpDot)
                {
                    isGroundImpact = true;
                    break;
                }
            }
        }

        AudioClip impactClip = isGroundImpact ? groundBounceSound : obstacleImpactSound;
        if (impactClip == null)
            return;

        lastImpactSoundTime = Time.time;
        float intensity = Mathf.InverseLerp(minimumImpactSpeedForSound, impactSpeedForFullVolume, impactSpeed);
        float volume = impactSoundVolume * Mathf.Lerp(0.35f, 1f, intensity);
        PlayBallSound(impactClip, volume);
    }

    private void UpdateRollingSound()
    {
        if (rollingSoundSource == null || ballRigidbody == null)
            return;

        float speed = GetBallVelocity().magnitude;
        bool shouldRoll = IsTouchingGround() && speed >= rollingSoundStartSpeed;
        float intensity = shouldRoll
            ? Mathf.InverseLerp(rollingSoundStartSpeed, rollingSpeedForFullVolume, speed)
            : 0f;
        float targetVolume = rollingSoundVolume * intensity * GameSettings.SfxVolume;
        rollingSoundSource.volume = Mathf.MoveTowards(
            rollingSoundSource.volume, targetVolume, Time.deltaTime * 2.5f);

        if (shouldRoll && !rollingSoundSource.isPlaying)
            rollingSoundSource.Play();
        else if (!shouldRoll && rollingSoundSource.volume <= 0.005f && rollingSoundSource.isPlaying)
            rollingSoundSource.Stop();

        if (rollingSoundSource.isPlaying)
            rollingSoundSource.pitch = Mathf.Lerp(0.82f, 1.12f, intensity);
    }

    private void StopRollingSound()
    {
        if (rollingSoundSource == null)
            return;

        rollingSoundSource.Stop();
        rollingSoundSource.volume = 0f;
    }

    private void DrawPowerBar()
    {
        float progress = ChargeProgress;
        float uiScale = HudScale;
        float edgeMargin = 16f * uiScale;
        float width = Mathf.Min(Mathf.Max(290f, powerBarSize.x) * uiScale,
            Mathf.Max(1f, Screen.width - edgeMargin * 2f));
        float height = Mathf.Max(50f, powerBarSize.y + 26f) * uiScale;
        float shakeX = 0f;
        float shakeY = 0f;
        bool fullyCharged = progress >= 1f;

        if (fullyCharged)
        {
            float shakeTime = Time.unscaledTime * 42f;
            shakeX = (Mathf.PerlinNoise(shakeTime, 0.37f) - 0.5f) * 4f * uiScale;
            shakeY = (Mathf.PerlinNoise(0.73f, shakeTime) - 0.5f) * 2.5f * uiScale;
        }

        float x = (Screen.width - width) * 0.5f + shakeX;
        float y = Screen.height - HudBottomOffset - height + shakeY;
        Rect shell = new Rect(x, y, width, height);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
        Color fillColor = GetPowerColor(progress, pulse);

        if (fullyCharged)
            FrutigerAeroUI.DrawPill(new Rect(x - 9f * uiScale, y - 8f * uiScale, width + 18f * uiScale, height + 16f * uiScale),
                new Color(1f, 0.21f, 0.28f, 0.12f + pulse * 0.17f));

        FrutigerAeroUI.DrawGlassPanel(shell,
            fullyCharged ? Color.Lerp(FrutigerAeroUI.Coral, Color.white, pulse * 0.12f) : FrutigerAeroUI.Cyan,
            new Color(0.80f, 0.96f, 1f, 0.94f), 0.18f);

        Rect track = new Rect(shell.x + 15f * uiScale, shell.y + 17f * uiScale, shell.width - 30f * uiScale, shell.height - 29f * uiScale);
        FrutigerAeroUI.DrawProgressTrack(track, progress, fillColor, fullyCharged);

        for (int i = 1; i < 16; i++)
        {
            float tickX = track.x + track.width * i / 16f;
            FrutigerAeroUI.DrawPill(new Rect(tickX - 0.7f * uiScale, track.y + 5f * uiScale, 1.4f * uiScale, track.height - 10f * uiScale),
                new Color(0.03f, 0.28f, 0.42f, 0.22f));
        }

        if (fullyCharged)
        {
            float shine = 0.4f + 0.35f * pulse;
            FrutigerAeroUI.DrawPill(new Rect(shell.x + shell.width * 0.36f, shell.y + 5f,
                shell.width * 0.28f, 3f * uiScale), new Color(1f, 1f, 1f, shine));
        }
    }

    private void DrawAngleIndicator()
    {
        float uiScale = HudScale;
        int angleFontSize = Mathf.Max(12, Mathf.RoundToInt(22f * uiScale));
        if (angleLabelStyle == null)
            angleLabelStyle = FrutigerAeroUI.CreateStyle(angleFontSize, FrutigerAeroUI.Ink);

        float barHeight = Mathf.Max(50f, powerBarSize.y + 26f) * uiScale;
        float badgeWidth = 120f * uiScale;
        float badgeHeight = 50f * uiScale;
        float badgeX = (Screen.width - badgeWidth) * 0.5f;
        float badgeY = Screen.height - HudBottomOffset - barHeight - badgeHeight - 10f * uiScale;
        Rect badge = new Rect(badgeX, badgeY, badgeWidth, badgeHeight);
        Color accent = GetAngleColor();

        FrutigerAeroUI.DrawGlassPanel(badge, FrutigerAeroUI.Cyan,
            new Color(0.86f, 0.98f, 1f, 0.96f), 0.16f);

        string angleText = shotElevationDegrees.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        angleLabelStyle.fontSize = angleFontSize;
        float numberWidth = angleLabelStyle.CalcSize(new GUIContent(angleText)).x;
        float groupWidth = numberWidth + 12f * uiScale;
        float numberX = badge.center.x - groupWidth * 0.5f;
        Rect labelRect = new Rect(numberX, badge.y + 6f * uiScale, numberWidth, badge.height - 19f * uiScale);
        GUI.Label(new Rect(labelRect.x + uiScale, labelRect.y + uiScale, labelRect.width, labelRect.height),
            angleText, FrutigerAeroUI.CreateStyle(angleFontSize, new Color(0.04f, 0.30f, 0.43f, 0.22f)));
        GUI.Label(labelRect, angleText, angleLabelStyle);

        Rect degreeMark = new Rect(labelRect.xMax + 3f * uiScale, labelRect.y + 7f * uiScale, 7f * uiScale, 7f * uiScale);
        FrutigerAeroUI.DrawCircle(degreeMark, accent);
        FrutigerAeroUI.DrawCircle(new Rect(degreeMark.x + 2f * uiScale, degreeMark.y + 2f * uiScale, 3f * uiScale, 3f * uiScale),
            new Color(0.86f, 0.98f, 1f, 1f));

        Rect track = new Rect(badge.x + 15f * uiScale, badge.yMax - 10f * uiScale, badge.width - 30f * uiScale, 4f * uiScale);
        Color gaugeFill = Color.Lerp(accent, Color.white, 0.22f);
        FrutigerAeroUI.DrawProgressTrack(track, shotElevationDegrees / 50f, gaugeFill);
    }

    private void ApplyBallFriction()
    {
        ballCollider = GetComponent<Collider>();
        if (ballCollider == null)
        {
            Debug.LogWarning("Add a Collider to the ball for surface friction to take effect.", this);
            return;
        }

        // Collider.material creates a per-collider instance so the ball can use its own surface feel.
        runtimeBallMaterial = ballCollider.material;
        if (runtimeBallMaterial == null)
        {
            runtimeBallMaterial = new PhysicsMaterial("Ball High Friction");
            ballCollider.material = runtimeBallMaterial;
        }

        runtimeBallMaterial.staticFriction = ballStaticFriction;
        runtimeBallMaterial.dynamicFriction = ballDynamicFriction;
        runtimeBallMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
        runtimeBallMaterial.bounciness = ballBounciness;
        runtimeBallMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
    }

    private void SetupStretchableMeshes()
    {
        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;

            Mesh sourceMesh = filter.sharedMesh;
            if (!sourceMesh.isReadable)
            {
                Debug.LogWarning("The ball's visual mesh is not readable, so cartoon stretching is disabled for it. Enable Read/Write on the mesh import settings.", filter);
                continue;
            }

            Mesh stretchedMesh = Instantiate(sourceMesh);
            stretchedMesh.name = sourceMesh.name + " (Cartoon Stretch)";
            filter.sharedMesh = stretchedMesh;

            Vector3[] originalVertices = stretchedMesh.vertices;
            stretchableMeshes.Add(new StretchableMesh
            {
                filter = filter,
                originalMesh = sourceMesh,
                stretchedMesh = stretchedMesh,
                center = stretchedMesh.bounds.center,
                originalVertices = originalVertices,
                stretchedVertices = new Vector3[originalVertices.Length]
            });
        }

        if (stretchableMeshes.Count == 0)
            Debug.LogWarning("BallPlayerController could not find a readable MeshFilter to animate.", this);
    }

    private void UpdateBallStretch()
    {
        if (stretchableMeshes.Count == 0)
            return;

        Vector3 velocity = GetBallVelocity();
        float speed = velocity.magnitude;
        Vector3 stretchDirection = speed > 0.15f ? velocity / speed : lastShotDirection;
        float speedFraction = Mathf.Clamp01(speed / Mathf.Max(0.1f, speedForMaximumStretch));
        float stretchBlend = Mathf.Clamp01((Time.unscaledTime - lastShotTime) / 0.18f);
        float speedStretch = maximumSpeedStretch * speedFraction * stretchBlend;

        float timeSinceShot = Time.unscaledTime - lastShotTime;
        float impactPulse = timeSinceShot >= 0f && timeSinceShot < 0.5f
            ? Mathf.Exp(-timeSinceShot * 15f) * Mathf.Cos(timeSinceShot * 25f)
            : 0f;

        float lengthScale = Mathf.Clamp(1f + speedStretch - shotImpactSquash * impactPulse, 0.75f, 1.25f);
        float widthScale = 1f / Mathf.Sqrt(lengthScale);

        for (int meshIndex = 0; meshIndex < stretchableMeshes.Count; meshIndex++)
        {
            StretchableMesh visual = stretchableMeshes[meshIndex];
            if (visual.filter == null || visual.stretchedMesh == null)
                continue;

            Vector3 localDirection = visual.filter.transform.InverseTransformDirection(stretchDirection).normalized;
            for (int vertexIndex = 0; vertexIndex < visual.originalVertices.Length; vertexIndex++)
            {
                Vector3 relative = visual.originalVertices[vertexIndex] - visual.center;
                float along = Vector3.Dot(relative, localDirection);
                Vector3 alongAxis = localDirection * along;
                Vector3 acrossAxes = relative - alongAxis;
                visual.stretchedVertices[vertexIndex] = visual.center + alongAxis * lengthScale + acrossAxes * widthScale;
            }

            visual.stretchedMesh.vertices = visual.stretchedVertices;
            visual.stretchedMesh.RecalculateNormals();
            visual.stretchedMesh.RecalculateBounds();
        }
    }

    private Vector3 GetBallVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return ballRigidbody.linearVelocity;
#else
        return ballRigidbody.velocity;
#endif
    }

    private void SetupDirectionArrow()
    {
        runtimeArrowObject = new GameObject("Shot Direction Arrow");
        runtimeArrowObject.layer = gameObject.layer;
        runtimeArrowObject.SetActive(false);

        MeshFilter meshFilter = runtimeArrowObject.AddComponent<MeshFilter>();
        directionArrowRenderer = runtimeArrowObject.AddComponent<MeshRenderer>();
        runtimeArrowMesh = CreateDirectionArrowMesh();
        meshFilter.sharedMesh = runtimeArrowMesh;
        directionArrowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        directionArrowRenderer.receiveShadows = false;

        Shader arrowShader = Shader.Find("Universal Render Pipeline/Lit");
        if (arrowShader == null)
            arrowShader = Shader.Find("Standard");
        if (arrowShader == null)
            arrowShader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (arrowShader == null)
            arrowShader = Shader.Find("Legacy Shaders/Diffuse");
        if (arrowShader == null)
            arrowShader = Shader.Find("Unlit/Color");

        if (arrowShader == null)
        {
            Debug.LogWarning("Could not find an unlit shader for the shot direction arrow.", this);
            return;
        }

        runtimeArrowMaterial = new Material(arrowShader)
        {
            name = "Translucent Aqua Shot Arrow",
            hideFlags = HideFlags.HideAndDontSave
        };
        ConfigureGlassArrowMaterial(runtimeArrowMaterial, directionArrowColor, false);
        if (runtimeArrowMaterial.HasProperty("_BaseColor"))
            runtimeArrowMaterial.SetColor("_BaseColor", directionArrowColor);
        if (runtimeArrowMaterial.HasProperty("_Color"))
            runtimeArrowMaterial.SetColor("_Color", directionArrowColor);
        if (runtimeArrowMaterial.HasProperty("_Metallic"))
            runtimeArrowMaterial.SetFloat("_Metallic", 0.04f);
        if (runtimeArrowMaterial.HasProperty("_Smoothness"))
            runtimeArrowMaterial.SetFloat("_Smoothness", 0.94f);
        if (runtimeArrowMaterial.HasProperty("_Glossiness"))
            runtimeArrowMaterial.SetFloat("_Glossiness", 0.94f);
        if (runtimeArrowMaterial.HasProperty("_ClearCoatMask"))
            runtimeArrowMaterial.SetFloat("_ClearCoatMask", 0.65f);
        if (runtimeArrowMaterial.HasProperty("_ClearCoatSmoothness"))
            runtimeArrowMaterial.SetFloat("_ClearCoatSmoothness", 0.95f);
        if (runtimeArrowMaterial.HasProperty("_EmissionColor"))
        {
            runtimeArrowMaterial.SetColor("_EmissionColor", new Color(directionArrowColor.r * 0.12f,
                directionArrowColor.g * 0.12f, directionArrowColor.b * 0.12f, 1f));
            runtimeArrowMaterial.EnableKeyword("_EMISSION");
        }
        directionArrowRenderer.sharedMaterial = runtimeArrowMaterial;
        CreateArrowGlow(arrowShader);
    }

    private void CreateArrowGlow(Shader arrowShader)
    {
        runtimeArrowGlowMaterial = CreateArrowGlowLayer(arrowShader,
            "Soft Aqua Outer Glow", new Vector3(1.48f, 1.48f, 1.14f), 0.58f, 0.28f,
            (int)RenderQueue.Transparent - 20);
        runtimeArrowInnerGlowMaterial = CreateArrowGlowLayer(arrowShader,
            "Aqua Arrow Inner Halo", new Vector3(1.20f, 1.20f, 1.06f), 0.82f, 0.30f,
            (int)RenderQueue.Transparent - 10);
    }

    private Material CreateArrowGlowLayer(Shader arrowShader, string layerName, Vector3 scale,
        float colorMultiplier, float alpha, int renderQueue)
    {
        GameObject glowObject = new GameObject(layerName);
        glowObject.layer = gameObject.layer;
        glowObject.transform.SetParent(runtimeArrowObject.transform, false);
        glowObject.transform.localScale = scale;
        glowObject.transform.localPosition = new Vector3(0f, -0.004f, 0f);

        MeshFilter glowFilter = glowObject.AddComponent<MeshFilter>();
        glowFilter.sharedMesh = runtimeArrowMesh;
        MeshRenderer glowRenderer = glowObject.AddComponent<MeshRenderer>();
        glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;

        Color glowColor = new Color(directionArrowColor.r * colorMultiplier,
            directionArrowColor.g * colorMultiplier, directionArrowColor.b * colorMultiplier, alpha);
        Material glowMaterial = new Material(arrowShader)
        {
            name = layerName + " Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        ConfigureGlassArrowMaterial(glowMaterial, glowColor, true);
        if (glowMaterial.HasProperty("_BaseColor"))
            glowMaterial.SetColor("_BaseColor", glowColor);
        if (glowMaterial.HasProperty("_Color"))
            glowMaterial.SetColor("_Color", glowColor);
        glowMaterial.renderQueue = renderQueue;
        glowRenderer.sharedMaterial = glowMaterial;
        return glowMaterial;
    }
    private static void ConfigureGlassArrowMaterial(Material material, Color color, bool additive)
    {
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", additive ? 2f : 0f);
        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 3f);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_SrcBlend"))
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 0);

        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = additive
            ? (int)RenderQueue.Transparent - 10
            : (int)RenderQueue.Transparent;
    }
    private void UpdateDirectionArrow()
    {
        if (runtimeArrowObject == null)
            return;

        if (runtimeArrowObject.activeSelf != showAngleIndicator)
            runtimeArrowObject.SetActive(showAngleIndicator);
        if (!showAngleIndicator)
            return;

        Vector3 horizontalForward = GetHorizontalForward();
        Vector3 shotDirection = GetShotDirection();
        Vector3 arrowStart = transform.position + horizontalForward * (directionArrowStartOffset + directionArrowGap);
        runtimeArrowObject.transform.SetPositionAndRotation(
            arrowStart,
            Quaternion.LookRotation(shotDirection, Vector3.up));
    }

    private Mesh CreateDirectionArrowMesh()
    {
        const int segments = 16;
        float arrowLength = directionArrowLength * directionArrowLengthMultiplier * (2f / 3f);
        float headLength = Mathf.Min(directionArrowHeadLength, arrowLength * 0.55f);
        float headBaseZ = arrowLength - headLength;
        float shaftRadius = directionArrowShaftWidth * 0.5f;
        float headRadius = directionArrowHeadWidth * 0.5f;
        int tipIndex = segments * 3;
        int backCenterIndex = tipIndex + 1;
        Vector3[] vertices = new Vector3[backCenterIndex + 1];

        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            vertices[i] = new Vector3(cos * shaftRadius, sin * shaftRadius, 0f);
            vertices[segments + i] = new Vector3(cos * shaftRadius, sin * shaftRadius, headBaseZ);
            vertices[segments * 2 + i] = new Vector3(cos * headRadius, sin * headRadius, headBaseZ);
        }

        vertices[tipIndex] = new Vector3(0f, 0f, arrowLength);
        vertices[backCenterIndex] = Vector3.zero;

        List<int> triangles = new List<int>(segments * 18);
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;

            // Shaft sides.
            triangles.Add(i);
            triangles.Add(next);
            triangles.Add(segments + next);
            triangles.Add(i);
            triangles.Add(segments + next);
            triangles.Add(segments + i);

            // Arrowhead cone sides.
            triangles.Add(segments * 2 + i);
            triangles.Add(segments * 2 + next);
            triangles.Add(tipIndex);

            // Shoulder between the shaft and the wider arrowhead.
            triangles.Add(segments * 2 + i);
            triangles.Add(segments + i);
            triangles.Add(segments + next);
            triangles.Add(segments * 2 + i);
            triangles.Add(segments + next);
            triangles.Add(segments * 2 + next);

            // Back cap at the arrow's base.
            triangles.Add(backCenterIndex);
            triangles.Add(next);
            triangles.Add(i);
        }

        Mesh mesh = new Mesh
        {
            name = "Shot Direction Arrow Mesh",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            triangles = triangles.ToArray()
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Vector3 GetShotDirection()
    {
        // Camera pitch is discarded here; only the W/S elevation setting adds vertical aim.
        Vector3 horizontalForward = GetHorizontalForward();
        float angleRadians = shotElevationDegrees * Mathf.Deg2Rad;
        return (horizontalForward * Mathf.Cos(angleRadians) + Vector3.up * Mathf.Sin(angleRadians)).normalized;
    }

    private Vector3 GetHorizontalForward()
    {
        Vector3 cameraForward = aimCamera != null ? aimCamera.transform.forward : transform.forward;
        Vector3 horizontalForward = new Vector3(cameraForward.x, 0f, cameraForward.z);

        if (horizontalForward.sqrMagnitude < 0.001f)
            horizontalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
            horizontalForward = Vector3.forward;

        return horizontalForward.normalized;
    }

    private Color GetPowerColor(float progress, float pulse)
    {
        Color skyBlue = new Color(0.13f, 0.69f, 0.98f, 1f);
        Color springGreen = Color.Lerp(powerBarFill, new Color(0.27f, 0.86f, 0.43f, 1f), 0.5f);
        Color sunnyYellow = new Color(1f, 0.88f, 0.24f, 1f);
        Color hotRed = new Color(1f, 0.17f, 0.2f, 1f);

        if (progress < 0.4f)
            return Color.Lerp(skyBlue, springGreen, progress / 0.4f);
        if (progress < 0.76f)
            return Color.Lerp(springGreen, sunnyYellow, (progress - 0.4f) / 0.36f);

        Color color = Color.Lerp(sunnyYellow, hotRed, (progress - 0.76f) / 0.24f);
        if (progress >= 1f)
            color = Color.Lerp(hotRed, new Color(1f, 0.39f, 0.23f, 1f), pulse * 0.42f);

        return color;
    }

    private Color GetAngleColor()
    {
        Color lowColor = new Color(0.12f, 0.70f, 0.98f, 1f);
        Color highColor = new Color(0.24f, 0.72f, 0.40f, 1f);
        return Color.Lerp(lowColor, highColor, shotElevationDegrees / 50f);
    }

    private void DrawPill(Rect rect, Color color)
    {
        if (pillTexture == null)
            return;

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pillTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;
    }

    private void DrawCircle(Rect rect, Color color)
    {
        if (degreeCircleTexture == null)
            return;

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, degreeCircleTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;
    }

    private static Rect Inset(Rect rect, float amount)
    {
        return new Rect(rect.x + amount, rect.y + amount,
            Mathf.Max(1f, rect.width - amount * 2f), Mathf.Max(1f, rect.height - amount * 2f));
    }

    private static Texture2D CreatePillTexture(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Power Bar Rounded Shape",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        const int samplesPerAxis = 4;
        float radius = height * 0.24f;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int coveredSamples = 0;
                for (int sampleY = 0; sampleY < samplesPerAxis; sampleY++)
                {
                    for (int sampleX = 0; sampleX < samplesPerAxis; sampleX++)
                    {
                        float px = x + (sampleX + 0.5f) / samplesPerAxis;
                        float py = y + (sampleY + 0.5f) / samplesPerAxis;
                        float dx = Mathf.Abs(px - width * 0.5f) - (width * 0.5f - radius);
                        float dy = Mathf.Abs(py - height * 0.5f) - (height * 0.5f - radius);
                        float outsideX = Mathf.Max(0f, dx);
                        float outsideY = Mathf.Max(0f, dy);
                        float signedDistance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                            + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                        if (signedDistance <= 0f)
                            coveredSamples++;
                    }
                }

                float alpha = coveredSamples / (float)(samplesPerAxis * samplesPerAxis);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Degree Mark Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        const int samplesPerAxis = 4;
        float radius = size * 0.5f;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int coveredSamples = 0;
                for (int sampleY = 0; sampleY < samplesPerAxis; sampleY++)
                {
                    for (int sampleX = 0; sampleX < samplesPerAxis; sampleX++)
                    {
                        float px = x + (sampleX + 0.5f) / samplesPerAxis - radius;
                        float py = y + (sampleY + 0.5f) / samplesPerAxis - radius;
                        if (px * px + py * py <= radius * radius)
                            coveredSamples++;
                    }
                }

                float alpha = coveredSamples / (float)(samplesPerAxis * samplesPerAxis);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static void DrawRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }
}



