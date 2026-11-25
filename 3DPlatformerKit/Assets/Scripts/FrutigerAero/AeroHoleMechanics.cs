using UnityEngine;

/// <summary>Owns the state of one hole's moving cups and shot-reactive course features.</summary>
[DefaultExecutionOrder(-80)]
[DisallowMultipleComponent]
public sealed class AeroHoleMechanics : MonoBehaviour
{
    [SerializeField] private GolfCourseManager manager;
    [SerializeField] private HoleTrigger owner;
    [SerializeField] private AeroMovingCupPlatform[] platforms = new AeroMovingCupPlatform[0];
    [SerializeField] private AeroShotSwitch[] switches = new AeroShotSwitch[0];
    [SerializeField] private AeroReactiveMechanism[] mechanisms = new AeroReactiveMechanism[0];
    [Header("Optional Hole Hint")]
    [SerializeField] private string hintMessage;
    [SerializeField, Min(1f)] private float hintSeconds = 6f;

    private HoleTrigger subscribedOwner;
    private bool active;
    private bool initialized;
    private bool hintShownForRun;
    private float hintAge = 1000f;
    private GUIStyle hintStyle;

    public HoleTrigger Owner => owner;
    public bool IsActive => active;

    /// <summary>Serialized scene wiring; safe to call from an editor installer before Awake.</summary>
    public void Configure(GolfCourseManager courseManager, HoleTrigger hole,
        AeroMovingCupPlatform[] movingPlatforms, AeroShotSwitch[] shotSwitches,
        AeroReactiveMechanism[] reactiveMechanisms)
    {
        manager = courseManager;
        owner = hole;
        platforms = movingPlatforms ?? new AeroMovingCupPlatform[0];
        switches = shotSwitches ?? new AeroShotSwitch[0];
        mechanisms = reactiveMechanisms ?? new AeroReactiveMechanism[0];
        if (Application.isPlaying && initialized)
        {
            SubscribeToOwner();
            ApplyActiveState(CanRun());
        }
    }

    public void ConfigureHint(string message)
    {
        hintMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (Application.isPlaying && active)
            ShowHintIfNeeded();
    }

    private void Awake()
    {
        if (manager == null) manager = FindFirstObjectByType<GolfCourseManager>();
        if (owner == null) owner = GetComponentInChildren<HoleTrigger>(true);
        initialized = true;
        SubscribeToOwner();
        // Disable motion before the first physics step; the menu and inactive holes stay still.
        ApplyActiveState(false);
    }

    private void OnEnable()
    {
        if (initialized)
            ApplyActiveState(false);
    }

    private void FixedUpdate()
    {
        RefreshActiveState();
    }

    private void Update()
    {
        RefreshActiveState();
        if (active && Time.timeScale > 0f && hintAge < hintSeconds)
            hintAge += Time.unscaledDeltaTime;
    }

    private void RefreshActiveState()
    {
        bool shouldRun = CanRun();
        if (shouldRun != active)
            ApplyActiveState(shouldRun);
    }

    private bool CanRun()
    {
        return isActiveAndEnabled && manager != null && owner != null &&
               manager.IsGameplayActive && manager.ActiveHole == owner && !owner.IsCompleted;
    }

    private void ApplyActiveState(bool value)
    {
        active = value;
        for (int i = 0; i < platforms.Length; i++)
            if (platforms[i] != null) platforms[i].SetMotionEnabled(value);
        for (int i = 0; i < switches.Length; i++)
            if (switches[i] != null) switches[i].enabled = value;
        for (int i = 0; i < mechanisms.Length; i++)
            if (mechanisms[i] != null) mechanisms[i].enabled = value;
        if (value)
            ShowHintIfNeeded();
    }

    private void ShowHintIfNeeded()
    {
        if (hintShownForRun || string.IsNullOrEmpty(hintMessage)) return;
        hintShownForRun = true;
        hintAge = 0f;
    }

    /// <summary>Restarts the exact same timing and mechanism layout for a fair retry.</summary>
    public void ResetHoleMechanics()
    {
        for (int i = 0; i < switches.Length; i++)
            if (switches[i] != null) switches[i].ResetSwitch(false);
        for (int i = 0; i < mechanisms.Length; i++)
            if (mechanisms[i] != null) mechanisms[i].ResetMechanism();
        for (int i = 0; i < platforms.Length; i++)
            if (platforms[i] != null) platforms[i].ResetMotion();
        hintShownForRun = false;
        hintAge = 1000f;
        ApplyActiveState(CanRun());
    }

    private void SubscribeToOwner()
    {
        if (subscribedOwner == owner) return;
        if (subscribedOwner != null) subscribedOwner.ResetRequested -= ResetHoleMechanics;
        subscribedOwner = owner;
        if (subscribedOwner != null) subscribedOwner.ResetRequested += ResetHoleMechanics;
    }

    private void OnGUI()
    {
        if (!active || !CanRun() || Time.timeScale <= 0f || hintAge >= hintSeconds || string.IsNullOrEmpty(hintMessage))
            return;

        float scale = Mathf.Max(0.4f, Mathf.Min(Screen.width / 1600f, Screen.height / 900f));
        float width = Mathf.Min(500f * scale, Screen.width - 32f * scale);
        float height = 46f * scale;
        Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.16f, width, height);
        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(hintAge / 0.25f, (hintSeconds - hintAge) / 0.6f)));
        int previousDepth = GUI.depth;
        GUI.depth = -4;

        // Use the shared glass shapes with alpha in each layer, so the whole panel fades.
        FrutigerAeroUI.DrawRounded(new Rect(panel.x + scale, panel.y + 4f * scale, panel.width, panel.height),
            new Color(0.025f, 0.16f, 0.28f, 0.13f * alpha));
        Color cyan = FrutigerAeroUI.Cyan;
        FrutigerAeroUI.DrawRounded(panel, new Color(cyan.r, cyan.g, cyan.b, 0.94f * alpha));
        Rect inside = new Rect(panel.x + 2f * scale, panel.y + 2f * scale,
            panel.width - 4f * scale, panel.height - 4f * scale);
        FrutigerAeroUI.DrawRounded(inside, new Color(0.84f, 0.97f, 1f, 0.95f * alpha));
        FrutigerAeroUI.DrawPill(new Rect(inside.x + 6f * scale, inside.y + 3f * scale,
            inside.width - 12f * scale, 9f * scale), new Color(1f, 1f, 1f, 0.48f * alpha));

        if (hintStyle == null)
        {
            hintStyle = FrutigerAeroUI.CreateStyle(17, FrutigerAeroUI.Ink, TextAnchor.MiddleCenter, false);
            hintStyle.wordWrap = false;
            hintStyle.clipping = TextClipping.Clip;
        }
        hintStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(17f * scale));
        Color ink = FrutigerAeroUI.Ink;
        hintStyle.normal.textColor = new Color(ink.r, ink.g, ink.b, alpha);
        GUI.Label(new Rect(panel.x + 14f * scale, panel.y, panel.width - 28f * scale, panel.height), hintMessage, hintStyle);
        GUI.depth = previousDepth;
    }

    private void OnDisable()
    {
        if (initialized) ApplyActiveState(false);
    }

    private void OnDestroy()
    {
        if (subscribedOwner != null) subscribedOwner.ResetRequested -= ResetHoleMechanics;
    }
}
