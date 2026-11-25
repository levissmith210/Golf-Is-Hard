using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Saved player options shared by the audio, camera, and menus.</summary>
public static class GameSettings
{
    private const float SfxLevelBoost = 1.3f;
    private const string MasterVolumeKey = "GolfIsHard.MasterVolume";
    private const string MusicVolumeKey = "GolfIsHard.MusicVolume";
    private const string SfxVolumeKey = "GolfIsHard.SfxVolume";
    private const string InvertXKey = "GolfIsHard.InvertCameraX";
    private const string InvertYKey = "GolfIsHard.InvertCameraY";
    private const string CameraSensitivityKey = "GolfIsHard.CameraSensitivity";

    private static bool loaded;
    private sealed class RegisteredSfxSource
    {
        public AudioSource source;
        public float originalVolume;
    }

    private static readonly List<RegisteredSfxSource> registeredSfxSources = new List<RegisteredSfxSource>();
    private static float masterVolume = 1f;
    private static float musicVolume = 1f;
    private static float sfxVolume = 1f;
    private static bool invertCameraX;
    private static bool invertCameraY;
    private static float cameraSensitivity = 1f;

    private static GUIStyle titleStyle;
    private static GUIStyle subtitleStyle;
    private static GUIStyle labelStyle;
    private static GUIStyle valueStyle;
    private static GUIStyle toggleStyle;
    private static GUIStyle backButtonStyle;

    public static event Action MusicVolumeChanged;

    public static float MasterVolume { get { EnsureLoaded(); return masterVolume; } }
    public static float MusicVolume { get { EnsureLoaded(); return musicVolume; } }
    // A stronger baseline lift keeps effects clear beside the quieter background music.
    public static float SfxVolume { get { EnsureLoaded(); return sfxVolume * SfxLevelBoost; } }
    public static bool InvertCameraX { get { EnsureLoaded(); return invertCameraX; } }
    public static bool InvertCameraY { get { EnsureLoaded(); return invertCameraY; } }
    public static float CameraSensitivity { get { EnsureLoaded(); return cameraSensitivity; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeSceneLoad()
    {
        EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
        musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
        invertCameraX = PlayerPrefs.GetInt(InvertXKey, 0) != 0;
        invertCameraY = PlayerPrefs.GetInt(InvertYKey, 0) != 0;
        cameraSensitivity = PlayerPrefs.GetFloat(CameraSensitivityKey, 1f);

        masterVolume = Mathf.Clamp01(masterVolume);
        musicVolume = Mathf.Clamp01(musicVolume);
        sfxVolume = Mathf.Clamp01(sfxVolume);
        cameraSensitivity = Mathf.Clamp(cameraSensitivity, 0.25f, 2.5f);
        loaded = true;
        AudioListener.volume = masterVolume;
    }

    public static void SetMasterVolume(float value)
    {
        EnsureLoaded();
        float clampedValue = Mathf.Clamp01(value);
        if (Mathf.Approximately(masterVolume, clampedValue))
            return;

        masterVolume = clampedValue;
        PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume);
        AudioListener.volume = masterVolume;
    }

    public static void SetMusicVolume(float value)
    {
        EnsureLoaded();
        float clampedValue = Mathf.Clamp01(value);
        if (Mathf.Approximately(musicVolume, clampedValue))
            return;

        musicVolume = clampedValue;
        PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        MusicVolumeChanged?.Invoke();
    }

    public static void SetSfxVolume(float value)
    {
        EnsureLoaded();
        float clampedValue = Mathf.Clamp01(value);
        if (Mathf.Approximately(sfxVolume, clampedValue))
            return;

        sfxVolume = clampedValue;
        PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
        ApplySfxVolumeToActiveSources();
    }

    /// <summary>Plays an effect through the live SFX bus so slider changes affect active and future sounds.</summary>
    public static void PlaySfx(AudioSource source, AudioClip clip, float volumeScale = 1f)
    {
        EnsureLoaded();
        if (source == null || clip == null)
            return;

        RegisteredSfxSource entry = RegisterSfxSource(source);
        source.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * entry.originalVolume * SfxLevelBoost);
    }

    private static RegisteredSfxSource RegisterSfxSource(AudioSource source)
    {
        for (int i = registeredSfxSources.Count - 1; i >= 0; i--)
        {
            RegisteredSfxSource entry = registeredSfxSources[i];
            if (entry.source == null)
            {
                registeredSfxSources.RemoveAt(i);
                continue;
            }

            if (entry.source == source)
                return entry;
        }

        RegisteredSfxSource newEntry = new RegisteredSfxSource
        {
            source = source,
            originalVolume = source.volume
        };
        registeredSfxSources.Add(newEntry);
        source.volume = sfxVolume;
        return newEntry;
    }

    private static void ApplySfxVolumeToActiveSources()
    {
        for (int i = registeredSfxSources.Count - 1; i >= 0; i--)
        {
            RegisteredSfxSource entry = registeredSfxSources[i];
            if (entry.source == null)
            {
                registeredSfxSources.RemoveAt(i);
                continue;
            }

            entry.source.volume = sfxVolume;
        }
    }

    public static void SetInvertCameraX(bool value)
    {
        EnsureLoaded();
        if (invertCameraX == value)
            return;

        invertCameraX = value;
        PlayerPrefs.SetInt(InvertXKey, value ? 1 : 0);
    }

    public static void SetInvertCameraY(bool value)
    {
        EnsureLoaded();
        if (invertCameraY == value)
            return;

        invertCameraY = value;
        PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0);
    }

    public static void SetCameraSensitivity(float value)
    {
        EnsureLoaded();
        float clampedValue = Mathf.Clamp(value, 0.25f, 2.5f);
        if (Mathf.Approximately(cameraSensitivity, clampedValue))
            return;

        cameraSensitivity = clampedValue;
        PlayerPrefs.SetFloat(CameraSensitivityKey, cameraSensitivity);
    }

    public static void Save()
    {
        EnsureLoaded();
        PlayerPrefs.Save();
    }

    /// <summary>Draws the shared settings panel and returns true when Back is pressed.</summary>
    public static bool DrawSettingsMenu(Action replayTutorial = null)
    {
        EnsureLoaded();
        EnsureStyles();

        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.62f, 1.1f);
        float panelWidth = Mathf.Min(610f * scale, Screen.width - 24f);
        float panelHeight = Mathf.Min(540f * scale, Screen.height - 24f);
        Rect panel = new Rect((Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f, panelWidth, panelHeight);

        int previousDepth = GUI.depth;
        GUI.depth = -110;
        FrutigerAeroUI.DrawGlassPanel(panel, FrutigerAeroUI.Cyan,
            new Color(0.82f, 0.96f, 1f, 0.97f), 0.3f);

        titleStyle.fontSize = Mathf.RoundToInt(32f * scale);
        subtitleStyle.fontSize = Mathf.RoundToInt(13f * scale);
        labelStyle.fontSize = Mathf.RoundToInt(15f * scale);
        valueStyle.fontSize = Mathf.RoundToInt(15f * scale);
        toggleStyle.fontSize = Mathf.RoundToInt(14f * scale);
        backButtonStyle.fontSize = Mathf.RoundToInt(17f * scale);

        GUI.Label(new Rect(panel.x + 24f * scale, panel.y + 18f * scale,
            panel.width - 48f * scale, 44f * scale), "SETTINGS", titleStyle);
        GUI.Label(new Rect(panel.x + 24f * scale, panel.y + 55f * scale,
            panel.width - 48f * scale, 22f * scale),
            replayTutorial != null ? "AUDIO  •  CAMERA  •  TUTORIAL" : "AUDIO  •  CAMERA", subtitleStyle);
        FrutigerAeroUI.DrawPill(new Rect(panel.x + 25f * scale, panel.y + 84f * scale,
            panel.width - 50f * scale, 3f * scale), new Color(0.12f, 0.65f, 0.82f, 0.32f));

        float rowStart = 94f * scale;
        float rowGap = 63f * scale;
        masterVolume = DrawSlider(panel, rowStart, scale, "MASTER VOLUME", masterVolume, 0f, 1f,
            Mathf.RoundToInt(masterVolume * 100f) + "%");
        SetMasterVolume(masterVolume);
        musicVolume = DrawSlider(panel, rowStart + rowGap, scale, "MUSIC VOLUME", musicVolume, 0f, 1f,
            Mathf.RoundToInt(musicVolume * 100f) + "%");
        SetMusicVolume(musicVolume);
        sfxVolume = DrawSlider(panel, rowStart + rowGap * 2f, scale, "SFX VOLUME", sfxVolume, 0f, 1f,
            Mathf.RoundToInt(sfxVolume * 100f) + "%");
        SetSfxVolume(sfxVolume);
        cameraSensitivity = DrawSlider(panel, rowStart + rowGap * 3f, scale, "CAMERA SENSITIVITY",
            cameraSensitivity, 0.25f, 2.5f, cameraSensitivity.ToString("0.00") + "×");
        SetCameraSensitivity(cameraSensitivity);

        float toggleX = panel.x + 27f * scale;
        float toggleWidth = panel.width - 54f * scale;
        Rect invertXRect = new Rect(toggleX, panel.y + 351f * scale, toggleWidth, 32f * scale);
        Rect invertYRect = new Rect(toggleX, panel.y + 392f * scale, toggleWidth, 32f * scale);
        SetInvertCameraX(GUI.Toggle(invertXRect, InvertCameraX, "INVERT HORIZONTAL CAMERA", toggleStyle));
        SetInvertCameraY(GUI.Toggle(invertYRect, InvertCameraY, "INVERT VERTICAL CAMERA", toggleStyle));

        bool replayPressed = false;
        float backWidth = replayTutorial != null
            ? (panel.width - 70f * scale) * 0.5f
            : Mathf.Min(230f * scale, panel.width - 56f * scale);
        Rect backButton = new Rect(replayTutorial != null ? panel.xMax - 28f * scale - backWidth : panel.center.x - backWidth * 0.5f,
            panel.y + 457f * scale, backWidth, 50f * scale);
        if (replayTutorial != null)
        {
            Rect replayButton = new Rect(panel.x + 28f * scale, backButton.y, backWidth, backButton.height);
            replayPressed = FrutigerAeroUI.DrawGlassButton(
                replayButton, "REPLAY TUTORIAL", backButtonStyle, FrutigerAeroUI.Blue, false);
        }
        bool backPressed = FrutigerAeroUI.DrawGlassButton(
            backButton, "BACK", backButtonStyle, FrutigerAeroUI.Green, true);

        GUI.depth = previousDepth;
        if (replayPressed)
            replayTutorial();
        return backPressed;
    }

    private static float DrawSlider(
        Rect panel,
        float y,
        float scale,
        string label,
        float value,
        float minimum,
        float maximum,
        string valueText)
    {
        Rect labelRect = new Rect(panel.x + 29f * scale, panel.y + y * scale,
            panel.width - 130f * scale, 22f * scale);
        Rect valueRect = new Rect(panel.xMax - 95f * scale, panel.y + y * scale,
            66f * scale, 22f * scale);
        GUI.Label(labelRect, label, labelStyle);
        GUI.Label(valueRect, valueText, valueStyle);

        Rect sliderRect = new Rect(panel.x + 30f * scale, panel.y + (y + 27f) * scale,
            panel.width - 60f * scale, 18f * scale);
        FrutigerAeroUI.DrawPill(sliderRect, new Color(0.15f, 0.49f, 0.62f, 0.35f));
        float normalized = Mathf.InverseLerp(minimum, maximum, value);
        if (normalized > 0f)
            FrutigerAeroUI.DrawPill(new Rect(sliderRect.x, sliderRect.y + 3f * scale,
                sliderRect.width * normalized, sliderRect.height - 6f * scale),
                new Color(0.12f, 0.68f, 0.88f, 0.82f));

        Color previousColor = GUI.color;
        GUI.color = Color.white;
        value = GUI.HorizontalSlider(sliderRect, value, minimum, maximum);
        GUI.color = previousColor;
        return value;
    }

    private static void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        titleStyle = FrutigerAeroUI.CreateStyle(32, FrutigerAeroUI.Ink);
        subtitleStyle = FrutigerAeroUI.CreateStyle(13, FrutigerAeroUI.Blue);
        labelStyle = FrutigerAeroUI.CreateStyle(15, FrutigerAeroUI.Ink, TextAnchor.MiddleLeft);
        valueStyle = FrutigerAeroUI.CreateStyle(15, FrutigerAeroUI.Blue, TextAnchor.MiddleRight);
        toggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(20, 6, 2, 2)
        };
        toggleStyle.normal.textColor = FrutigerAeroUI.Ink;
        toggleStyle.onNormal.textColor = FrutigerAeroUI.Blue;
        toggleStyle.hover.textColor = FrutigerAeroUI.Blue;
        toggleStyle.onHover.textColor = FrutigerAeroUI.Blue;
        backButtonStyle = FrutigerAeroUI.CreateStyle(17, FrutigerAeroUI.Ink);
    }
}
