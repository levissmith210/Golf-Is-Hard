using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>A solid, illuminated target: one deliberate shot impact toggles its linked course mechanisms.</summary>
[DisallowMultipleComponent]
public sealed class AeroShotSwitch : MonoBehaviour
{
    [Header("Linked Course Mechanisms")]
    [SerializeField] private AeroReactiveMechanism[] targets = new AeroReactiveMechanism[0];
    [SerializeField] private bool startsActivated;
    [SerializeField] private bool toggleOnEveryHit = true;
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 1.5f;
    [SerializeField, Min(0.05f)] private float hitCooldown = 0.6f;

    [Header("Physical Button and Lights")]
    [SerializeField] private Transform buttonFace;
    [SerializeField] private Vector3 localPressOffset = new Vector3(0f, 0f, -0.075f);
    [SerializeField, Min(0.05f)] private float pressSeconds = 0.28f;
    [SerializeField] private Renderer[] statusLights = new Renderer[0];
    [SerializeField] private Color idleColor = new Color(0.08f, 0.66f, 0.96f);
    [SerializeField] private Color activeColor = new Color(0.48f, 1f, 0.3f);
    [SerializeField, Min(0f)] private float emissionStrength = 1.6f;

    [Header("Optional Sound Effects")]
    [SerializeField] private AudioClip activateSound;
    [SerializeField] private AudioClip deactivateSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.75f;
    [SerializeField] private AudioSource soundSource;
    [SerializeField] private UnityEvent<bool> onStateChanged = new UnityEvent<bool>();

    private readonly Dictionary<BallPlayerController, int> lastAcceptedStroke = new Dictionary<BallPlayerController, int>();
    private bool initialized;
    private bool activated;
    private float lastHitTime = -1000f;
    private float pressAge = 1000f;
    private Vector3 faceRestPosition;
    private MaterialPropertyBlock lightProperties;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public bool IsActivated => activated;
    public event Action<AeroShotSwitch, bool> StateChanged;

    /// <summary>Assign a child face whose local +Z points out from the button, toward the approaching ball.</summary>
    public void Configure(AeroReactiveMechanism[] linkedTargets, Transform face, Renderer[] lights, Color idle, Color active)
    {
        // Runtime AddComponent invokes Awake before the caller can assign the visual face.
        // Restore an old face before taking the new one as the button's authored rest pose.
        if (initialized && buttonFace != null)
            buttonFace.localPosition = faceRestPosition;

        targets = linkedTargets ?? new AeroReactiveMechanism[0];
        buttonFace = face;
        statusLights = lights ?? new Renderer[0];
        idleColor = idle;
        activeColor = active;

        if (initialized)
        {
            faceRestPosition = buttonFace != null ? buttonFace.localPosition : Vector3.zero;
            pressAge = 1000f;
            UpdateLights(0f);
        }
    }

    public void ConfigureAudio(AudioClip onSound, AudioClip offSound, float volume = 0.75f)
    {
        activateSound = onSound;
        deactivateSound = offSound;
        soundVolume = Mathf.Clamp01(volume);
    }

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;
        activated = startsActivated;
        if (buttonFace != null) faceRestPosition = buttonFace.localPosition;
        lightProperties = new MaterialPropertyBlock();
        if (soundSource == null) soundSource = GetComponent<AudioSource>();
        if (soundSource == null) soundSource = gameObject.AddComponent<AudioSource>();
        soundSource.playOnAwake = false;
        soundSource.spatialBlend = 1f;
        soundSource.dopplerLevel = 0f;
        soundSource.rolloffMode = AudioRolloffMode.Linear;
        soundSource.minDistance = 4f;
        soundSource.maxDistance = 65f;
        initialized = true;
        UpdateLights(0f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!isActiveAndEnabled || Time.timeScale <= 0f) return;
        BallPlayerController ball = collision.collider.GetComponentInParent<BallPlayerController>();
        TryActivate(ball, collision.relativeVelocity.magnitude);
    }

    public bool Activate(BallPlayerController ball)
    {
        return TryActivate(ball, ball != null ? ball.Velocity.magnitude : 0f);
    }

    private bool TryActivate(BallPlayerController ball, float speed)
    {
        if (!isActiveAndEnabled || Time.timeScale <= 0f || ball == null || !ball.enabled ||
            ball.IsHoleComplete || ball.IsCharging || ball.StrokeCount <= 0 || speed < minimumImpactSpeed)
            return false;
        if (ball.ShotFeedback != null && !ball.ShotFeedback.HasActiveShot)
            return false;
        Initialize();
        if (Time.time - lastHitTime < hitCooldown)
            return false;
        if (lastAcceptedStroke.TryGetValue(ball, out int previousStroke) && previousStroke == ball.StrokeCount)
            return false;
        if (!toggleOnEveryHit && activated)
            return false;

        lastAcceptedStroke[ball] = ball.StrokeCount;
        lastHitTime = Time.time;
        pressAge = 0f;
        activated = toggleOnEveryHit ? !activated : true;
        for (int i = 0; i < targets.Length; i++)
            if (targets[i] != null) targets[i].SetActivated(activated);
        UpdateLights(1f);
        GameSettings.PlaySfx(soundSource, activated ? activateSound : deactivateSound, soundVolume);
        StateChanged?.Invoke(this, activated);
        onStateChanged?.Invoke(activated);
        return true;
    }

    private void Update()
    {
        if (Time.timeScale <= 0f || pressAge > Mathf.Max(pressSeconds, 0.5f)) return;
        pressAge += Time.deltaTime;
        float t = Mathf.Clamp01(pressAge / Mathf.Max(0.05f, pressSeconds));
        if (buttonFace != null)
            buttonFace.localPosition = faceRestPosition + localPressOffset * Mathf.Sin(t * Mathf.PI);
        UpdateLights(Mathf.Clamp01(1f - pressAge / 0.5f));
    }

    /// <summary>Clear stroke debouncing when the hole restarts. Pass false if a coordinator resets targets separately.</summary>
    public void ResetSwitch(bool resetTargets = true)
    {
        Initialize();
        activated = startsActivated;
        lastAcceptedStroke.Clear();
        lastHitTime = -1000f;
        pressAge = 1000f;
        if (buttonFace != null) buttonFace.localPosition = faceRestPosition;
        if (resetTargets)
        {
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null) targets[i].ResetMechanism();
        }
        UpdateLights(0f);
    }

    private void UpdateLights(float flash)
    {
        if (lightProperties == null) lightProperties = new MaterialPropertyBlock();
        Color color = Color.Lerp(activated ? activeColor : idleColor, Color.white, flash * 0.5f);
        for (int i = 0; i < statusLights.Length; i++)
        {
            Renderer light = statusLights[i];
            if (light == null) continue;
            light.GetPropertyBlock(lightProperties);
            lightProperties.SetColor(BaseColorId, color);
            lightProperties.SetColor(ColorId, color);
            lightProperties.SetColor(EmissionId, color * emissionStrength * (1f + flash));
            light.SetPropertyBlock(lightProperties);
        }
    }
}

