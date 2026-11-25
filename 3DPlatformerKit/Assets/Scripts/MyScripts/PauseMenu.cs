using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pauses the game with Escape and presents the shared bright glass menu.
/// Attach this component to an always-active GameObject in the gameplay scene.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [SerializeField] private ThirdPersonCameraController cameraController;
    [SerializeField] private MusicManager musicManager;

    [Header("Sound Effects")]
    [Tooltip("Played when opening the pause menu with Escape.")]
    [SerializeField] private AudioClip pauseOpenSound;
    [Tooltip("Played when resuming with Escape or the Resume button.")]
    [SerializeField] private AudioClip resumeSound;
    [Tooltip("Short click used by the pause menu buttons.")]
    [SerializeField] private AudioClip menuButtonClickSound;
    [Tooltip("Played once when the pointer moves onto a pause menu button.")]
    [SerializeField] private AudioClip menuButtonHoverSound;
    [SerializeField, Range(0f, 1f)] private float pauseSoundVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float menuButtonHoverVolume = 0.45f;
    [SerializeField] private AudioSource uiSoundSource;

    private bool isPaused;
    private bool settingsOpen;
    public bool IsPaused => isPaused;

    private bool wasCameraEnabled;
    private bool previousAudioPaused;
    private float previousTimeScale = 1f;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle footerStyle;

    private void Awake()
    {
        if (uiSoundSource == null)
            uiSoundSource = GetComponent<AudioSource>();
        if (uiSoundSource == null)
            uiSoundSource = gameObject.AddComponent<AudioSource>();
        uiSoundSource.playOnAwake = false;
        uiSoundSource.spatialBlend = 0f;
        uiSoundSource.ignoreListenerPause = true;

        if (cameraController == null && Camera.main != null)
            cameraController = Camera.main.GetComponent<ThirdPersonCameraController>();

        if (cameraController == null)
            cameraController = FindObjectOfType<ThirdPersonCameraController>();

        if (musicManager == null)
            musicManager = FindObjectOfType<MusicManager>();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        if (settingsOpen)
        {
            settingsOpen = false;
            GameSettings.Save();
            FrutigerAeroUI.ResetGlassButtonHover();
            return;
        }

        if (isPaused)
            ResumeGame();
        else
            PauseGame();
    }

    private void OnGUI()
    {
        if (!isPaused)
            return;

        EnsureStyles();
        int previousDepth = GUI.depth;
        GUI.depth = -100;

        FrutigerAeroUI.DrawPauseBackdrop();
        if (settingsOpen)
        {
            if (GameSettings.DrawSettingsMenu(ReplayTutorialFromSettings))
            {
                settingsOpen = false;
                GameSettings.Save();
                FrutigerAeroUI.ResetGlassButtonHover();
            }

            GUI.depth = previousDepth;
            return;
        }

        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1100f, Screen.height / 720f), 0.68f, 1.12f);
        float panelWidth = Mathf.Min(500f * scale, Screen.width - 28f);
        float panelHeight = Mathf.Min(540f * scale, Screen.height - 28f);
        Rect panel = new Rect((Screen.width - panelWidth) * 0.5f, (Screen.height - panelHeight) * 0.5f,
            panelWidth, panelHeight);

        FrutigerAeroUI.DrawGlassPanel(panel, FrutigerAeroUI.Cyan, new Color(0.79f, 0.95f, 1f, 0.94f), 0.28f);
        FrutigerAeroUI.DrawGolfBall(new Vector2(panel.center.x, panel.y + 52f * scale), 42f * scale);

        titleStyle.fontSize = Mathf.RoundToInt(39f * scale);
        subtitleStyle.fontSize = Mathf.RoundToInt(16f * scale);
        buttonStyle.fontSize = Mathf.RoundToInt(20f * scale);
        footerStyle.fontSize = Mathf.RoundToInt(13f * scale);

        GUI.Label(new Rect(panel.x + 20f, panel.y + 75f * scale, panel.width - 40f, 54f * scale),
            "Golf is Hard", titleStyle);
        GUI.Label(new Rect(panel.x + 20f, panel.y + 126f * scale, panel.width - 40f, 30f * scale),
            "PAUSED • TAKE A BREATHER", subtitleStyle);

        FrutigerAeroUI.DrawPill(new Rect(panel.x + panel.width * 0.2f, panel.y + 163f * scale,
            panel.width * 0.6f, 3f * scale), new Color(0.12f, 0.65f, 0.82f, 0.32f));

        float buttonWidth = panelWidth * 0.72f;
        float buttonHeight = 48f * scale;
        float gap = 10f * scale;
        float buttonX = panel.x + (panelWidth - buttonWidth) * 0.5f;
        float buttonY = panel.y + 181f * scale;

        if (FrutigerAeroUI.DrawGlassButton(new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                "RESUME", buttonStyle, FrutigerAeroUI.Green, true,
                menuButtonHoverSound, uiSoundSource, menuButtonHoverVolume))
        {
            PlayUiSound(menuButtonClickSound);
            ResumeGame();
        }

        buttonY += buttonHeight + gap;
        if (FrutigerAeroUI.DrawGlassButton(new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                "SETTINGS", buttonStyle, FrutigerAeroUI.Cyan, false,
                menuButtonHoverSound, uiSoundSource, menuButtonHoverVolume))
        {
            PlayUiSound(menuButtonClickSound);
            settingsOpen = true;
            FrutigerAeroUI.ResetGlassButtonHover();
        }

        buttonY += buttonHeight + gap;
        if (FrutigerAeroUI.DrawGlassButton(new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                "RESTART HOLE", buttonStyle, FrutigerAeroUI.Blue, false,
                menuButtonHoverSound, uiSoundSource, menuButtonHoverVolume))
        {
            RestartLevel();
        }

        buttonY += buttonHeight + gap;
        if (FrutigerAeroUI.DrawGlassButton(new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                "MAIN MENU", buttonStyle, FrutigerAeroUI.Coral, false,
                menuButtonHoverSound, uiSoundSource, menuButtonHoverVolume))
        {
            PlayUiSound(menuButtonClickSound);
            ReturnToMainMenu();
        }

        GUI.Label(new Rect(panel.x + 20f, panel.yMax - 42f * scale, panel.width - 40f, 24f * scale),
            "PRESS ESC TO JUMP BACK IN", footerStyle);

        GUI.depth = previousDepth;
    }

    private void PauseGame()
    {
        if (isPaused)
            return;

        GolfShotFeedback.CancelActiveCinematics();
        FrutigerAeroUI.ResetGlassButtonHover();
        isPaused = true;
        previousTimeScale = Time.timeScale;
        wasCameraEnabled = cameraController != null && cameraController.enabled;
        previousAudioPaused = AudioListener.pause;

        Time.timeScale = 0f;
        if (musicManager != null)
            musicManager.SetPaused(true);
        AudioListener.pause = true;
        PlayUiSound(pauseOpenSound);
        if (wasCameraEnabled)
            cameraController.enabled = false;
        UnlockCursor();
    }
    private void ResumeGame(bool playResumeSound = true)
    {
        if (!isPaused)
            return;

        FrutigerAeroUI.ResetGlassButtonHover();
        isPaused = false;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        AudioListener.pause = previousAudioPaused;
        if (playResumeSound)
            PlayUiSound(resumeSound);
        if (musicManager != null)
            musicManager.SetPaused(false);

        if (cameraController != null)
        {
            cameraController.enabled = wasCameraEnabled;
            cameraController.LockMouse();
        }
        else
        {
            LockCursor();
        }
    }
    private void ReplayTutorialFromSettings()
    {
        GolfCourseManager courseManager = FindObjectOfType<GolfCourseManager>();
        if (courseManager == null)
            return;

        settingsOpen = false;
        GameSettings.Save();
        ResumeGame(false);
        courseManager.ReplayTutorial();
    }

    private void RestartLevel()
    {
        GolfCourseManager courseManager = FindObjectOfType<GolfCourseManager>();
        if (courseManager != null)
        {
            courseManager.RestartCurrentHole();
            ResumeGame(false);
            return;
        }

        isPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = previousAudioPaused;
        if (musicManager != null)
            musicManager.SetPaused(false);
        if (cameraController != null)
            cameraController.enabled = wasCameraEnabled;
        UnlockCursor();

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
            SceneManager.LoadScene(activeScene.buildIndex);
        else
            SceneManager.LoadScene(activeScene.name);
    }
    private void ReturnToMainMenu()
    {
        GolfCourseManager courseManager = FindObjectOfType<GolfCourseManager>();
        if (courseManager == null)
        {
            ResumeGame();
            return;
        }

        FrutigerAeroUI.ResetGlassButtonHover();
        isPaused = false;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        AudioListener.pause = previousAudioPaused;
        if (musicManager != null)
            musicManager.SetPaused(false);

        courseManager.ReturnToStartMenu();
    }

    private void OnDisable()
    {
        if (!isPaused)
            return;

        FrutigerAeroUI.ResetGlassButtonHover();
        isPaused = false;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        AudioListener.pause = previousAudioPaused;
        if (musicManager != null)
            musicManager.SetPaused(false);
        if (cameraController != null)
            cameraController.enabled = wasCameraEnabled;
        UnlockCursor();
    }
    private void PlayUiSound(AudioClip clip)
    {
        GameSettings.PlaySfx(uiSoundSource, clip, pauseSoundVolume);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        titleStyle = FrutigerAeroUI.CreateStyle(39, FrutigerAeroUI.Ink);
        subtitleStyle = FrutigerAeroUI.CreateStyle(16, FrutigerAeroUI.Blue);
        buttonStyle = FrutigerAeroUI.CreateStyle(20, FrutigerAeroUI.Ink);
        footerStyle = FrutigerAeroUI.CreateStyle(13, new Color(0.15f, 0.44f, 0.56f, 1f));
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

