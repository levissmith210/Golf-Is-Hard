using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Presentation for the entire shot. Added by the ball at runtime, so all existing
/// courses get the same feedback without rewriting scenes or prefabs.
/// </summary>
[DisallowMultipleComponent]
public sealed class GolfShotFeedback : MonoBehaviour
{
    [Header("Feel")]
    [SerializeField] private bool showTrail = true;
    [SerializeField] private bool cupSlowMotion = true;
    [SerializeField, Range(0f, 1f)] private float feedbackVolume = 0.65f;
    [SerializeField, Range(0.25f, 0.8f)] private float cupTimeScale = 0.42f;
    [SerializeField, Range(0.3f, 1.2f)] private float cupMomentSeconds = 0.85f;

    private static GolfShotFeedback timeOwner;
    private BallPlayerController ball;
    private GolfCourseManager course;
    private ThirdPersonCameraController shotCamera;
    private TrailRenderer trail;
    private ParticleSystem sparks;
    private LineRenderer ring;
    private Material glowMaterial;
    private AudioSource audioSource;
    private AudioClip impactClip, perfectClip, landingClip, cupClip;
    private Vector3 launchPosition;
    private float launchedAt;
    private float airborneSeconds;
    private float settledSeconds;
    private bool hasLanded;
    private bool perfectShot;
    private bool cupMomentUsed;
    private float savedTimeScale, savedFixedStep, appliedTimeScale, slowMotionLeft;
    private float cupFocusWeight;
    private Vector3 cupFocus;
    private float ringAge = 100f;
    private float ringDuration;
    private float ringRadius;
    private Color ringColor;
    private string message, detail;
    private float messageAge = 100f;
    private float messageDuration;
    private Color messageColor;
    private GUIStyle messageStyle, detailStyle;

    public bool HasActiveShot { get; private set; }
    public bool IsCupMomentActive => timeOwner == this;
    public float CupFocusWeight => cupFocusWeight;
    public Vector3 CupFocus => cupFocus;

    private void Awake()
    {
        ball = GetComponent<BallPlayerController>();
        course = FindFirstObjectByType<GolfCourseManager>();
        ResolveCamera();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        impactClip = CreateSound("Shot impact body", 0);
        perfectClip = CreateSound("Perfect shot shimmer", 1);
        landingClip = CreateSound("Soft landing accent", 2);
        cupClip = CreateSound("Cup celebration", 3);
        SetupVisuals();
    }

    private void ResolveCamera()
    {
        if (Camera.main != null)
            shotCamera = Camera.main.GetComponent<ThirdPersonCameraController>();
    }

    public void PrepareToCharge()
    {
        EndCupMoment();
        HasActiveShot = false;
        cupFocusWeight = 0f;
        messageAge = 100f;
        if (trail != null)
            trail.emitting = false;
    }

    public void Launch(float power, bool perfect, Vector3 velocity, float perfectBonus = 0.08f)
    {
        EndCupMoment();
        ResolveCamera();
        HasActiveShot = true;
        perfectShot = perfect;
        hasLanded = false;
        cupMomentUsed = false;
        airborneSeconds = settledSeconds = 0f;
        launchedAt = Time.time;
        launchPosition = transform.position;
        messageAge = 100f;
        if (trail != null)
        {
            trail.gameObject.SetActive(true);
            trail.Clear();
            trail.colorGradient = TrailGradient(perfect ? new Color(1f, 0.8f, 0.18f) : new Color(0.3f, 0.92f, 1f));
            trail.emitting = showTrail;
        }
        if (shotCamera != null)
            shotCamera.NotifyShot(power, velocity);
        Play(impactClip, Mathf.Lerp(0.14f, 0.9f, power));
        if (power > 0.35f)
            Burst(transform.position, perfect ? new Color(1f, 0.86f, 0.3f) : new Color(0.6f, 0.97f, 1f),
                Mathf.RoundToInt(Mathf.Lerp(7f, 22f, power)), Mathf.Lerp(1f, 3.4f, power));
        if (perfect)
        {
            Play(perfectClip, 0.65f);
            Announce("PERFECT!", $"+{perfectBonus * 100f:0}% POWER", new Color(1f, 0.82f, 0.24f), 1.1f);
            Pulse(transform.position, new Color(1f, 0.82f, 0.24f), 1.8f, 0.4f);
        }
    }

    public void Land(Collision collision)
    {
        if (!HasActiveShot || hasLanded || airborneSeconds < 0.12f || Time.time - launchedAt < 0.15f)
            return;
        hasLanded = true;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        float impact = collision.relativeVelocity.magnitude;
        Color color = new Color(0.65f, 0.97f, 1f);
        HoleTrigger hole = course != null ? course.ActiveHole : null;
        bool onGreen = AeroSurface.Lookup(collision.collider, point) == AeroSurfaceType.Green;
        bool nearCup = hole != null && Vector3.Distance(transform.position, hole.transform.position) < 9f;
        if (onGreen || nearCup)
        {
            color = new Color(0.62f, 1f, 0.4f);
            float carry = Vector3.ProjectOnPlane(point - launchPosition, Vector3.up).magnitude;
            Announce(onGreen ? "ON THE GREEN!" : "NICE APPROACH!", $"{carry:0} m CARRY", color, 1.2f);
            Play(landingClip, 0.45f);
        }
        if (impact > 2f)
        {
            Pulse(point + Vector3.up * 0.04f, color, Mathf.Clamp(impact * 0.12f, 0.5f, 2f), 0.45f);
            Burst(point, color, 10, 1.8f);
        }
    }

    public void CupCaptured(Vector3 position)
    {
        EndCupMoment();
        HasActiveShot = false;
        cupFocus = position;
        cupFocusWeight = 1f;
        if (trail != null)
            trail.emitting = false;
        messageAge = 100f; // The existing stroke result panel owns the headline.
        Burst(position + Vector3.up * 0.3f, new Color(1f, 0.85f, 0.25f), 48, 3.3f);
        Pulse(position + Vector3.up * 0.08f, new Color(1f, 0.85f, 0.25f), 3.5f, 0.8f);
        Play(cupClip, 0.75f);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;
        float dt = Time.unscaledDeltaTime;
        messageAge += dt;
        UpdateRing(dt);
        if (IsCupMomentActive)
        {
            slowMotionLeft -= dt;
            if (slowMotionLeft <= 0f || ball == null || !ball.enabled || !HasActiveShot)
                EndCupMoment();
        }
        cupFocusWeight = Mathf.MoveTowards(cupFocusWeight, IsCupMomentActive ? 1f : 0f, dt * 4f);
        if (ball == null || !ball.enabled || !HasActiveShot)
            return;
        Vector3 velocity = ball.Velocity;
        float speed = velocity.magnitude;
        if (!ball.IsGrounded)
            airborneSeconds += Time.deltaTime;
        if (trail != null)
        {
            trail.emitting = showTrail && speed > 3.5f;
            trail.time = Mathf.Lerp(0.12f, perfectShot ? 0.42f : 0.3f, Mathf.InverseLerp(3f, 25f, speed));
        }
        settledSeconds = ball.IsGrounded && speed < 0.4f ? settledSeconds + Time.deltaTime : 0f;
        if (settledSeconds > 0.25f)
        {
            HasActiveShot = false;
            EndCupMoment();
            if (trail != null)
                trail.emitting = false;
            return;
        }
        TryCupMoment(velocity);
    }

    private void TryCupMoment(Vector3 velocity)
    {
        if (!cupSlowMotion || cupMomentUsed || course == null || ball.IsCharging || Time.time - launchedAt < 0.12f)
            return;
        HoleTrigger hole = course.ActiveHole;
        if (hole == null)
            return;
        Vector3 toCup = hole.transform.position - transform.position;
        Vector3 flatToCup = Vector3.ProjectOnPlane(toCup, Vector3.up);
        Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float speed = flatVelocity.magnitude;
        float distance = flatToCup.magnitude;
        // Only a plausible, approaching shot earns this moment. Nearby misses,
        // a stationary ball, and balls flying high above the cup stay at full speed.
        if (distance > 3.5f || distance < 0.12f || speed < 0.7f || speed > 13f || Mathf.Abs(toCup.y) > 1.4f)
            return;
        float approach = Vector3.Dot(flatToCup, flatVelocity.normalized);
        float missDistance = (flatToCup - flatVelocity.normalized * approach).magnitude;
        if (approach <= 0f || missDistance > 0.65f || approach / speed > 0.9f)
            return;
        Vector3 sightEnd = hole.transform.position + Vector3.up * 0.35f;
        if (Physics.Linecast(transform.position, sightEnd, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
            && hit.distance < Vector3.Distance(transform.position, sightEnd) - 0.8f
            && !hit.transform.IsChildOf(hole.transform.parent))
            return;
        if (timeOwner != null || !Mathf.Approximately(Time.timeScale, 1f))
            return;
        cupMomentUsed = true;
        cupFocus = hole.transform.position;
        savedTimeScale = Time.timeScale;
        savedFixedStep = Time.fixedDeltaTime;
        appliedTimeScale = cupTimeScale;
        slowMotionLeft = cupMomentSeconds;
        timeOwner = this;
        Time.timeScale = appliedTimeScale;
        Time.fixedDeltaTime = savedFixedStep * appliedTimeScale / savedTimeScale;
    }

    private void EndCupMoment()
    {
        if (timeOwner != this)
            return;
        // Never unpause a menu or overwrite another system's time scale.
        if (Mathf.Approximately(Time.timeScale, appliedTimeScale))
            Time.timeScale = savedTimeScale;
        Time.fixedDeltaTime = savedFixedStep;
        timeOwner = null;
    }

    public static void CancelActiveCinematics()
    {
        if (timeOwner != null)
            timeOwner.EndCupMoment();
    }

    public void CancelPresentation()
    {
        EndCupMoment();
        HasActiveShot = false;
        cupFocusWeight = 0f;
        messageAge = ringAge = 100f;
        if (trail != null)
        {
            trail.emitting = false;
            // Hide before the ball teleports. Unity may retain renderer points
            // until its next update even after Clear(), so emission alone is insufficient.
            trail.gameObject.SetActive(false);
            trail.Clear();
        }
        if (ring != null)
            ring.enabled = false;
        if (sparks != null)
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (shotCamera != null)
            shotCamera.ResetShotPresentation();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
            EndCupMoment();
    }

    private void OnDisable() => CancelPresentation();

    private void OnDestroy()
    {
        EndCupMoment();
        if (ring != null) Destroy(ring.gameObject);
        if (sparks != null) Destroy(sparks.gameObject);
        if (glowMaterial != null) Destroy(glowMaterial);
        if (impactClip != null) Destroy(impactClip);
        if (perfectClip != null) Destroy(perfectClip);
        if (landingClip != null) Destroy(landingClip);
        if (cupClip != null) Destroy(cupClip);
    }

    private void SetupVisuals()
    {
        Shader shader = Resources.Load<Shader>("GolfShotGlow");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return;
        glowMaterial = new Material(shader) { name = "Shot aqua and gold", hideFlags = HideFlags.HideAndDontSave };
        var trailObject = new GameObject("Shot flight trail");
        trailObject.transform.SetParent(transform, false);
        trail = trailObject.AddComponent<TrailRenderer>();
        trail.sharedMaterial = glowMaterial;
        trail.time = 0.3f;
        trail.minVertexDistance = 0.05f;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 0.16f, 1f, 0f);
        trail.numCapVertices = 3;
        trail.numCornerVertices = 3;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
        trail.colorGradient = TrailGradient(new Color(0.3f, 0.92f, 1f));
        var sparkObject = new GameObject("Shot sparkles");
        sparks = sparkObject.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = 0.55f;
        main.startSize = 0.085f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.65f;
        main.maxParticles = 96;
        var emission = sparks.emission;
        emission.enabled = false;
        var shape = sparks.shape;
        shape.enabled = false;
        var colors = sparks.colorOverLifetime;
        colors.enabled = true;
        colors.color = TrailGradient(Color.white);
        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = glowMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var ringObject = new GameObject("Shot landing ripple");
        ring = ringObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = glowMaterial;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 64;
        ring.widthMultiplier = 0.055f;
        ring.shadowCastingMode = ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.enabled = false;
    }

    private static Gradient TrailGradient(Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.25f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.35f), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    private void Burst(Vector3 position, Color color, int count, float speed)
    {
        if (sparks == null) return;
        sparks.Play();
        for (int i = 0; i < count; i++)
        {
            // A deterministic golden-angle fan avoids changing gameplay's Random state.
            float angle = i * 2.399963f;
            float spread = 0.45f + (i % 5) * 0.12f;
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = new Vector3(Mathf.Cos(angle) * spread, 0.5f + (i % 4) * 0.22f, Mathf.Sin(angle) * spread) * speed,
                startColor = color,
                startSize = 0.07f + (i % 3) * 0.035f,
                startLifetime = 0.35f + (i % 5) * 0.09f
            };
            sparks.Emit(emit, 1);
        }
    }

    private void Pulse(Vector3 position, Color color, float radius, float duration)
    {
        if (ring == null) return;
        ring.transform.position = position;
        ringAge = 0f;
        ringDuration = duration;
        ringRadius = radius;
        ringColor = color;
        ring.enabled = true;
        UpdateRing(0f);
    }

    private void UpdateRing(float dt)
    {
        if (ring == null || !ring.enabled) return;
        ringAge += dt;
        float t = Mathf.Clamp01(ringAge / ringDuration);
        if (t >= 1f) { ring.enabled = false; return; }
        float radius = Mathf.Lerp(0.15f, ringRadius, 1f - (1f - t) * (1f - t));
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
        Color fade = ringColor;
        fade.a = (1f - t) * 0.8f;
        ring.startColor = ring.endColor = fade;
    }

    private void Announce(string title, string subtitle, Color color, float duration)
    {
        message = title;
        detail = subtitle;
        messageColor = color;
        messageAge = 0f;
        messageDuration = duration;
    }

    private void OnGUI()
    {
        if (Time.timeScale <= 0f || messageAge >= messageDuration || string.IsNullOrEmpty(message)) return;
        float s = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.55f, 1.35f);
        if (messageStyle == null)
        {
            messageStyle = FrutigerAeroUI.CreateStyle(30, FrutigerAeroUI.Ink);
            detailStyle = FrutigerAeroUI.CreateStyle(12, FrutigerAeroUI.Ink);
        }
        messageStyle.fontSize = Mathf.RoundToInt(30f * s);
        detailStyle.fontSize = Mathf.RoundToInt(12f * s);
        float fade = Mathf.Clamp01((messageDuration - messageAge) / 0.25f);
        float pop = 1f + 0.12f * Mathf.Exp(-messageAge * 12f) * Mathf.Sin(messageAge * 24f);
        Rect panel = new Rect((Screen.width - 310f * s) * 0.5f, Screen.height * 0.2f, 310f * s, 79f * s);
        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.color = new Color(1f, 1f, 1f, fade);
        GUIUtility.ScaleAroundPivot(Vector2.one * pop, panel.center);
        FrutigerAeroUI.DrawGlassPanel(panel, messageColor, new Color(0.88f, 0.98f, 1f, 0.94f));
        GUI.Label(new Rect(panel.x, panel.y + 7f * s, panel.width, 40f * s), message, messageStyle);
        GUI.Label(new Rect(panel.x, panel.y + 46f * s, panel.width, 23f * s), detail, detailStyle);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }

    private void Play(AudioClip clip, float volume)
    {
        GameSettings.PlaySfx(audioSource, clip, volume * feedbackVolume);
    }

    private static AudioClip CreateSound(string name, int kind)
    {
        const int sampleRate = 44100;
        float duration = kind == 0 ? 0.22f : kind == 3 ? 0.85f : 0.42f;
        var samples = new float[Mathf.CeilToInt(duration * sampleRate)];
        uint noiseState = 7319;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)sampleRate;
            float sample = 0f;
            if (kind == 0)
            {
                noiseState = noiseState * 1664525u + 1013904223u;
                float noise = (noiseState & 65535u) / 32767.5f - 1f;
                float thump = Mathf.Sin(2f * Mathf.PI * (130f * t - 180f * t * t)) * Mathf.Exp(-t * 29f);
                sample = thump * 0.62f + noise * Mathf.Exp(-t * 100f) * 0.38f;
            }
            else
            {
                int notes = kind == 3 ? 4 : kind == 1 ? 2 : 1;
                for (int note = 0; note < notes; note++)
                {
                    float age = t - note * 0.085f;
                    if (age < 0f) continue;
                    float frequency = (kind == 2 ? 660f : 880f) * Mathf.Pow(2f, (note == 3 ? 12f : note * 4f) / 12f);
                    float envelope = (1f - Mathf.Exp(-age * 250f)) * Mathf.Exp(-age * 10f);
                    sample += (Mathf.Sin(age * frequency * Mathf.PI * 2f) + 0.22f * Mathf.Sin(age * frequency * Mathf.PI * 4f)) * envelope * 0.2f;
                }
            }
            float edge = Mathf.Clamp01(t / 0.0015f) * Mathf.Clamp01((duration - t) / 0.025f);
            samples[i] = Mathf.Clamp(sample * edge, -0.9f, 0.9f);
        }
        AudioClip clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
        clip.hideFlags = HideFlags.HideAndDontSave;
        clip.SetData(samples, 0);
        return clip;
    }
}
