using UnityEngine;

/// <summary>
/// Orbits a third-person camera around a target, follows it, and avoids obstacles.
/// Attach this component to the Camera and assign the golf ball as Target.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 focusOffset = new Vector3(0f, 0.7f, 0f);

    [Header("Orbit and Zoom")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 3f;
    [SerializeField, Min(0.1f)] private float startingDistance = 6f;
    [SerializeField, Min(0.1f)] private float minimumDistance = 2f;
    [SerializeField, Min(0.1f)] private float maximumDistance = 12f;
    [SerializeField, Min(0f)] private float zoomSpeed = 2f;
    [SerializeField] private float minimumPitch = -10f;
    [SerializeField] private float maximumPitch = 70f;

    [Header("Follow")]
    [Tooltip("0 attaches the camera to the ball. Higher values create a softer, bouncier rubber-band follow. Mouse orbit remains immediate.")]
    [SerializeField, Range(0f, 1.5f)] private float followSpringiness = 0.85f;
    [Tooltip("Interpolate a Rigidbody target between physics steps to reduce visible camera jitter.")]
    [SerializeField] private bool interpolateTargetRigidbody = true;

    [Header("Obstacle Avoidance")]
    [SerializeField] private bool avoidObstacles = true;
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;
    [SerializeField, Min(0f)] private float collisionPadding = 0.1f;

    [Header("Shot Camera")]
    [SerializeField] private bool chaseShots = true;
    [SerializeField, Range(0f, 1f)] private float impactShake = 0.65f;
    [SerializeField, Range(0f, 12f)] private float flightFieldOfView = 6f;
    private Camera viewCamera;
    private BallPlayerController ball;
    private float baseFieldOfView;
    private float flightBlend;
    private float impactAt = -100f;
    private float impactStrength;
    private float lastLookAt = -100f;
    private Vector3 launchDirection;

    private float yaw;
    private float pitch;
    private float distance;
    private Vector3 followedTargetPosition;
    private Vector3 followVelocity;
    private bool hasFollowPosition;
    private bool isCursorLocked;
    private bool tutorialLookAllowed = true;
    private bool tutorialZoomAllowed = true;
    private bool tutorialZoomResetAllowed = true;

    /// <summary>
    /// True during the frame the cursor was locked, allowing the ball controller
    /// to ignore the click used to capture the mouse.
    /// </summary>
    public bool CursorCapturedThisFrame { get; private set; }
    public bool IsCursorLocked => isCursorLocked;
    public bool TutorialLookInputDetected { get; private set; }
    public bool TutorialZoomInUsed { get; private set; }
    public bool TutorialZoomOutUsed { get; private set; }
    public bool TutorialZoomResetUsed { get; private set; }

    public void NotifyShot(float power, Vector3 velocity)
    {
        impactAt = Time.unscaledTime;
        impactStrength = power >= 0.65f ? Mathf.InverseLerp(0.5f, 1f, power) : 0f;
        launchDirection = Vector3.ProjectOnPlane(velocity, Vector3.up).normalized;
    }

    public void ResetShotPresentation()
    {
        flightBlend = 0f;
        impactAt = -100f;
        impactStrength = 0f;
        if (viewCamera != null)
            viewCamera.fieldOfView = baseFieldOfView;
    }

    public void SetTutorialInputAccess(bool allowLook, bool allowZoom, bool allowZoomReset, bool resetProgress)
    {
        tutorialLookAllowed = allowLook;
        tutorialZoomAllowed = allowZoom;
        tutorialZoomResetAllowed = allowZoomReset;

        if (resetProgress)
        {
            TutorialLookInputDetected = false;
            TutorialZoomInUsed = false;
            TutorialZoomOutUsed = false;
            TutorialZoomResetUsed = false;
        }
    }

    public void LockMouse()
    {
        SetCursorLocked(true);
    }

    public void UnlockMouse()
    {
        SetCursorLocked(false);
    }

    /// <summary>Snaps the follow point after the ball is moved to another tee.</summary>
    public void SnapFollowToTarget()
    {
        if (target == null)
            return;

        followedTargetPosition = target.position;
        followVelocity = Vector3.zero;
        hasFollowPosition = true;
        ResetShotPresentation();
    }

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
        if (viewCamera != null)
            baseFieldOfView = viewCamera.fieldOfView;
        if (target != null)
            ball = target.GetComponentInParent<BallPlayerController>();
        distance = Mathf.Clamp(startingDistance, minimumDistance, maximumDistance);
        yaw = transform.eulerAngles.y;
        pitch = NormalizeAngle(transform.eulerAngles.x);

        if (target == null)
            Debug.LogWarning("ThirdPersonCameraController needs the golf ball assigned as its Target.", this);
        else
        {
            if (interpolateTargetRigidbody)
            {
                Rigidbody targetRigidbody = target.GetComponent<Rigidbody>();
                if (targetRigidbody == null)
                    targetRigidbody = target.GetComponentInParent<Rigidbody>();

                if (targetRigidbody != null)
                    targetRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            }

            followedTargetPosition = target.position;
            hasFollowPosition = true;
        }
    }

    private void Start()
    {
        SetCursorLocked(false);
    }

    private void Update()
    {
        CursorCapturedThisFrame = false;

        if (target == null || Time.timeScale <= 0f)
            return;

        // The first left click captures the mouse. The capture click is ignored
        // as a shot input by BallPlayerController; PauseMenu releases the cursor.
        if (!isCursorLocked && Input.GetMouseButtonDown(0))
        {
            SetCursorLocked(true);
            CursorCapturedThisFrame = true;
            return;
        }

        if (!isCursorLocked)
            return;

        float sensitivity = mouseSensitivity * GameSettings.CameraSensitivity;
        float horizontalDirection = GameSettings.InvertCameraX ? -1f : 1f;
        float verticalDirection = GameSettings.InvertCameraY ? -1f : 1f;
        if (tutorialLookAllowed)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mouseX) > 0.01f || Mathf.Abs(mouseY) > 0.01f)
            {
                TutorialLookInputDetected = true;
                lastLookAt = Time.unscaledTime;
            }

            yaw += mouseX * sensitivity * horizontalDirection;
            pitch -= mouseY * sensitivity * verticalDirection;
            pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
        }

        bool middleMouseReset = Input.GetMouseButtonDown(2);
        bool keyboardReset = Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (tutorialZoomResetAllowed && (middleMouseReset || keyboardReset))
        {
            distance = Mathf.Clamp(startingDistance, minimumDistance, maximumDistance);
            if (TutorialZoomInUsed && TutorialZoomOutUsed)
                TutorialZoomResetUsed = true;
        }
        else if (tutorialZoomAllowed)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0.001f)
                TutorialZoomInUsed = true;
            else if (scroll < -0.001f)
                TutorialZoomOutUsed = true;

            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minimumDistance, maximumDistance);
        }
    }

    private void LateUpdate()
    {
        if (target == null || Time.timeScale <= 0f)
            return;

        GolfShotFeedback feedback = ball != null ? ball.ShotFeedback : null;
        bool inFlight = chaseShots && ball != null && ball.enabled && feedback != null
            && feedback.HasActiveShot && !ball.IsGrounded && ball.Velocity.magnitude > 3f;
        float response = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f);
        flightBlend = Mathf.Lerp(flightBlend, inFlight ? 1f : 0f, response);
        float cupBlend = feedback != null ? feedback.CupFocusWeight : 0f;

        if (!hasFollowPosition)
        {
            followedTargetPosition = target.position;
            hasFollowPosition = true;
        }
        else if (followSpringiness <= 0f)
        {
            followedTargetPosition = target.position;
            followVelocity = Vector3.zero;
        }
        else
        {
            // A lightly under-damped spring gives shots a delayed, elastic chase.
            // Substeps keep the spring stable when the frame rate dips.
            float springAmount = Mathf.Clamp01(followSpringiness / 1.5f);
            float springFrequency = Mathf.Lerp(18f, 7.5f, springAmount);
            float dampingRatio = Mathf.Lerp(0.92f, 0.38f, springAmount);
            springFrequency = Mathf.Lerp(springFrequency, 18f, flightBlend);
            dampingRatio = Mathf.Lerp(dampingRatio, 0.9f, flightBlend);
            float deltaTime = Time.deltaTime;
            int substepCount = Mathf.Max(1, Mathf.CeilToInt(deltaTime / (1f / 60f)));
            float substep = deltaTime / substepCount;

            for (int i = 0; i < substepCount; i++)
            {
                Vector3 displacement = target.position - followedTargetPosition;
                Vector3 acceleration = displacement * (springFrequency * springFrequency)
                    - followVelocity * (2f * dampingRatio * springFrequency);

                followVelocity += acceleration * substep;
                followedTargetPosition += followVelocity * substep;
            }
        }

        Vector3 focusPoint = followedTargetPosition + focusOffset;
        Vector3 velocity = ball != null ? ball.Velocity : Vector3.zero;
        focusPoint += Vector3.ClampMagnitude(velocity * 0.07f, 1.8f) * flightBlend;
        if (cupBlend > 0f)
            focusPoint = Vector3.Lerp(focusPoint, (target.position + feedback.CupFocus) * 0.5f + focusOffset, cupBlend * 0.65f);
        Vector3 travel = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (travel.sqrMagnitude < 4f)
            travel = launchDirection;
        float chaseYaw = travel.sqrMagnitude > 0.01f ? Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg : yaw;
        float chaseWeight = Time.unscaledTime - lastLookAt > 0.8f ? flightBlend * 0.72f : 0f;
        float renderedYaw = Mathf.LerpAngle(yaw, chaseYaw, chaseWeight);
        float renderedPitch = Mathf.Lerp(pitch, Mathf.Clamp(pitch, 12f, 27f), flightBlend * 0.65f);
        renderedPitch = Mathf.Lerp(renderedPitch, 29f, cupBlend * 0.55f);
        Quaternion orbitRotation = Quaternion.Euler(renderedPitch, renderedYaw, 0f);
        Vector3 cameraDirection = orbitRotation * Vector3.back;
        float shotDistance = Mathf.Lerp(distance, distance * 1.18f, flightBlend);
        shotDistance = Mathf.Lerp(shotDistance, Mathf.Clamp(distance * 0.72f, 3f, 8f), cupBlend * 0.7f);
        Vector3 desiredPosition = focusPoint + cameraDirection * shotDistance;

        if (avoidObstacles)
            desiredPosition = GetPositionBeforeObstacle(focusPoint, cameraDirection, desiredPosition, shotDistance);

        // Apply the orbit directly so mouse movement has no smoothing or lag.
        transform.position = desiredPosition;
        transform.rotation = orbitRotation;

        float impactAge = Time.unscaledTime - impactAt;
        float kick = impactAge >= 0f && impactAge < 0.3f ? Mathf.Exp(-impactAge * 17f) * impactStrength : 0f;
        if (kick > 0.001f && impactShake > 0f)
        {
            float noiseTime = impactAge * 55f;
            Vector2 noise = new Vector2(Mathf.PerlinNoise(noiseTime, 0.13f), Mathf.PerlinNoise(0.67f, noiseTime)) * 2f - Vector2.one;
            transform.position += orbitRotation * new Vector3(noise.x, noise.y, 0f) * (0.1f * kick * impactShake);
            transform.rotation *= Quaternion.Euler(-0.7f * kick * impactShake, 0f, noise.x * 0.6f * kick * impactShake);
        }
        if (viewCamera != null)
        {
            float desiredFov = baseFieldOfView + flightFieldOfView * flightBlend + 3f * kick - 3f * cupBlend;
            viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, desiredFov, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            SetCursorLocked(false);
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private void SetCursorLocked(bool locked)
    {
        isCursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private Vector3 GetPositionBeforeObstacle(
        Vector3 focusPoint,
        Vector3 cameraDirection,
        Vector3 desiredPosition,
        float castDistance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            focusPoint,
            collisionRadius,
            cameraDirection,
            castDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = castDistance;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == target || hitTransform.IsChildOf(target))
                continue;

            if (hits[i].distance < nearestDistance)
                nearestDistance = hits[i].distance;
        }

        if (nearestDistance < castDistance)
        {
            float safeDistance = Mathf.Max(0.05f, nearestDistance - collisionPadding);
            return focusPoint + cameraDirection * safeDistance;
        }

        return desiredPosition;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}

