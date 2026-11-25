using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>A shot switch can smoothly deploy or retract this physical gate, bridge, or platform.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class AeroReactiveMechanism : MonoBehaviour
{
    [Header("Activated Pose")]
    [SerializeField] private Vector3 activatedLocalOffset = Vector3.up * 4f;
    [SerializeField] private Vector3 activatedLocalEuler;
    [SerializeField, Min(0.1f)] private float travelSeconds = 1.2f;
    [SerializeField] private bool startsActivated;

    [Header("Status Lights")]
    [SerializeField] private Renderer[] statusLights = new Renderer[0];
    [SerializeField] private Color idleColor = new Color(0.08f, 0.66f, 0.96f);
    [SerializeField] private Color activeColor = new Color(0.48f, 1f, 0.3f);
    [SerializeField, Min(0f)] private float emissionStrength = 1.3f;

    [Header("Optional Sound Effects")]
    [SerializeField] private AudioClip activateSound;
    [SerializeField] private AudioClip deactivateSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.65f;
    [SerializeField] private AudioSource soundSource;
    [SerializeField] private UnityEvent<bool> onStateChanged = new UnityEvent<bool>();

    private Rigidbody body;
    private Transform motionParent;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private bool initialized;
    private bool activated;
    private bool moving;
    private float progress;
    private float fromProgress;
    private float elapsed;
    private MaterialPropertyBlock lightProperties;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public bool IsActivated => activated;
    public bool IsMoving => moving;
    public event Action<bool> StateChanged;

    /// <summary>Call in the editor after positioning the mechanism in its closed/resting pose.</summary>
    public void Configure(Vector3 localTravel, Vector3 localEulerTravel, float seconds = 1.2f, bool initiallyActive = false)
    {
        activatedLocalOffset = localTravel;
        activatedLocalEuler = localEulerTravel;
        travelSeconds = Mathf.Max(0.1f, seconds);
        startsActivated = initiallyActive;
        ConfigureBody();
    }

    public void ConfigureIndicators(Renderer[] lights, Color idle, Color active)
    {
        statusLights = lights ?? new Renderer[0];
        idleColor = idle;
        activeColor = active;
        if (Application.isPlaying && initialized)
            UpdateLights();
    }

    public void ConfigureAudio(AudioClip onSound, AudioClip offSound, float volume = 0.65f)
    {
        activateSound = onSound;
        deactivateSound = offSound;
        soundVolume = Mathf.Clamp01(volume);
    }

    private void Awake()
    {
        Initialize();
    }

    private void ConfigureBody()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null) return;
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void Initialize()
    {
        if (initialized) return;
        ConfigureBody();
        motionParent = transform.parent;
        restLocalPosition = transform.localPosition;
        restLocalRotation = transform.localRotation;
        lightProperties = new MaterialPropertyBlock();
        if (soundSource == null) soundSource = GetComponent<AudioSource>();
        if (soundSource == null) soundSource = gameObject.AddComponent<AudioSource>();
        soundSource.playOnAwake = false;
        soundSource.spatialBlend = 1f;
        soundSource.dopplerLevel = 0f;
        soundSource.rolloffMode = AudioRolloffMode.Linear;
        soundSource.minDistance = 4f;
        soundSource.maxDistance = 75f;
        initialized = true;
        activated = startsActivated;
        progress = activated ? 1f : 0f;
        ApplyPose(progress, true);
        UpdateLights();
    }

    public void SetActivated(bool active, bool immediate = false)
    {
        Initialize();
        bool changed = activated != active;
        activated = active;
        fromProgress = progress;
        elapsed = 0f;
        moving = !immediate && !Mathf.Approximately(progress, activated ? 1f : 0f);
        if (immediate)
        {
            progress = activated ? 1f : 0f;
            ApplyPose(progress, true);
        }
        UpdateLights();
        if (changed)
        {
            GameSettings.PlaySfx(soundSource, activated ? activateSound : deactivateSound, soundVolume);
            StateChanged?.Invoke(activated);
            onStateChanged?.Invoke(activated);
        }
    }

    private void FixedUpdate()
    {
        if (!moving || Time.timeScale <= 0f) return;
        elapsed += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, travelSeconds));
        float smooth = t * t * (3f - 2f * t);
        progress = Mathf.Lerp(fromProgress, activated ? 1f : 0f, smooth);
        ApplyPose(progress, false);
        if (t >= 1f) moving = false;
    }

    private void ApplyPose(float amount, bool immediate)
    {
        Vector3 localPosition = restLocalPosition + activatedLocalOffset * amount;
        Quaternion localRotation = restLocalRotation * Quaternion.Slerp(Quaternion.identity,
            Quaternion.Euler(activatedLocalEuler), amount);
        Vector3 worldPosition = motionParent != null ? motionParent.TransformPoint(localPosition) : localPosition;
        Quaternion worldRotation = motionParent != null ? motionParent.rotation * localRotation : localRotation;
        if (immediate)
        {
            body.position = worldPosition;
            body.rotation = worldRotation;
            transform.SetPositionAndRotation(worldPosition, worldRotation);
        }
        else
        {
            body.MovePosition(worldPosition);
            body.MoveRotation(worldRotation);
        }
    }

    /// <summary>Restores the authored initial state without replaying activation sounds.</summary>
    public void ResetMechanism()
    {
        Initialize();
        moving = false;
        elapsed = 0f;
        activated = startsActivated;
        progress = activated ? 1f : 0f;
        fromProgress = progress;
        ApplyPose(progress, true);
        UpdateLights();
    }

    private void UpdateLights()
    {
        if (lightProperties == null) lightProperties = new MaterialPropertyBlock();
        Color color = activated ? activeColor : idleColor;
        for (int i = 0; i < statusLights.Length; i++)
        {
            Renderer light = statusLights[i];
            if (light == null) continue;
            light.GetPropertyBlock(lightProperties);
            lightProperties.SetColor(BaseColorId, color);
            lightProperties.SetColor(ColorId, color);
            lightProperties.SetColor(EmissionId, color * emissionStrength);
            light.SetPropertyBlock(lightProperties);
        }
    }
}
