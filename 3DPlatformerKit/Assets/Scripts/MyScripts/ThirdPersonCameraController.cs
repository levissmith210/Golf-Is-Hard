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

    private float yaw;
    private float pitch;
    private float distance;
    private Vector3 followedTargetPosition;
    private Vector3 followVelocity;
    private bool hasFollowPosition;
    private bool isCursorLocked;

    /// <summary>
    /// True during the frame the cursor was locked, allowing the ball controller
    /// to ignore the click used to capture the mouse.
    /// </summary>
    public bool CursorCapturedThisFrame { get; private set; }
    public bool IsCursorLocked => isCursorLocked;

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
    }

    private void Awake()
    {
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

        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);

        bool resetZoom = Input.GetMouseButtonDown(2)
            || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (resetZoom)
        {
            distance = Mathf.Clamp(startingDistance, minimumDistance, maximumDistance);
        }
        else
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minimumDistance, maximumDistance);
        }
    }

    private void LateUpdate()
    {
        if (target == null || Time.timeScale <= 0f)
            return;

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
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 cameraDirection = orbitRotation * Vector3.back;
        Vector3 desiredPosition = focusPoint + cameraDirection * distance;

        if (avoidObstacles)
            desiredPosition = GetPositionBeforeObstacle(focusPoint, cameraDirection, desiredPosition);

        // Apply the orbit directly so mouse movement has no smoothing or lag.
        transform.position = desiredPosition;
        transform.rotation = orbitRotation;
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
        Vector3 desiredPosition)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            focusPoint,
            collisionRadius,
            cameraDirection,
            distance,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = distance;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == target || hitTransform.IsChildOf(target))
                continue;

            if (hits[i].distance < nearestDistance)
                nearestDistance = hits[i].distance;
        }

        if (nearestDistance < distance)
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
