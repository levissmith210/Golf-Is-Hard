using System;
using UnityEngine;

/// <summary>
/// Tracks the required hole order and moves the ball to each next tee after a
/// successful hole. Configure the stages in course order in the Inspector.
/// </summary>
public class GolfCourseManager : MonoBehaviour
{
    [Serializable]
    private class HoleStage
    {
        [Tooltip("The hole trigger that completes this stage.")]
        public HoleTrigger hole;

        [Tooltip("Ball spawn position and facing for this hole. The first entry is used when the scene starts.")]
        public Transform teePoint;
    }

    [Header("Course Order")]
    [Tooltip("Add one entry for each hole, ordered from the first hole to the last.")]
    [SerializeField] private HoleStage[] holesInOrder = new HoleStage[0];

    [Header("References")]
    [SerializeField] private BallPlayerController ball;
    [SerializeField] private ThirdPersonCameraController cameraController;

    [Header("Start Menu")]
    [SerializeField] private bool showStartMenuOnLaunch = true;

    [Header("Sound Effects")]
    [Tooltip("Played when the player starts the round from the title menu or keyboard.")]
    [SerializeField] private AudioClip startGameSound;
    [Tooltip("Played when moving from a completed hole to the next tee.")]
    [SerializeField] private AudioClip nextHoleSound;
    [Tooltip("Played when restarting the active hole from the pause menu.")]
    [SerializeField] private AudioClip restartHoleSound;
    [SerializeField, Range(0f, 1f)] private float restartHoleSoundVolume = 0.65f;
    [Tooltip("Played when returning from the pause menu to the title menu.")]
    [SerializeField] private AudioClip returnToMenuSound;
    [Tooltip("Played when the final hole is completed.")]
    [SerializeField] private AudioClip courseCompleteSound;
    [Tooltip("Played once when the pointer moves onto a title menu button.")]
    [SerializeField] private AudioClip startMenuButtonHoverSound;
    [SerializeField, Range(0f, 1f)] private float transitionSoundVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float startMenuHoverVolume = 0.45f;
    [SerializeField] private AudioSource uiSoundSource;

    private PauseMenu pauseMenu;
    private MusicManager musicManager;
    private CourseMenuCamera menuCamera;
    private bool showingStartMenu;
    private GUIStyle startKickerStyle;
    private GUIStyle startTitleStyle;
    private GUIStyle startSubtitleStyle;
    private GUIStyle startButtonStyle;
    private GUIStyle startQuitButtonStyle;
    private GUIStyle startDetailStyle;
    private GUIStyle startFooterStyle;
    private int activeHoleIndex;
    private bool isTransitioning;
    private bool awaitingNextHole;
    private bool courseComplete;
    private GUIStyle nextHolePromptStyle;
    private GUIStyle nextHolePromptShadowStyle;

    public int ActiveHoleNumber => activeHoleIndex + 1;
    public int TotalHoleCount => holesInOrder != null ? holesInOrder.Length : 0;
    public bool IsCourseComplete => courseComplete;

    private void Awake()
    {
        if (ball == null)
            ball = FindObjectOfType<BallPlayerController>();

        if (cameraController == null)
            cameraController = FindObjectOfType<ThirdPersonCameraController>();

        if (pauseMenu == null)
            pauseMenu = FindObjectOfType<PauseMenu>();

        if (musicManager == null)
            musicManager = FindObjectOfType<MusicManager>();

        SetupUiSoundSource();

        if (holesInOrder == null)
            return;

        for (int i = 0; i < holesInOrder.Length; i++)
        {
            if (holesInOrder[i] != null && holesInOrder[i].hole != null)
                holesInOrder[i].hole.SetCourseManager(this);
        }
    }

    private void Start()
    {
        HoleStage firstStage = GetActiveStage();
        if (ball == null || firstStage == null || firstStage.teePoint == null)
            return;

        ball.ResetForNextHole(firstStage.teePoint.position, firstStage.teePoint.rotation);

        if (showStartMenuOnLaunch)
        {
            PrepareStartMenu();
            return;
        }

        SetGameplayEnabled(true);
        if (musicManager != null)
            musicManager.PlayGameplayMusic();
        if (cameraController != null)
            cameraController.SnapFollowToTarget();
    }

    private void Update()
    {
        if (showingStartMenu)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                BeginGameplay();
            return;
        }

        if (!awaitingNextHole || Time.timeScale <= 0f)
            return;

        if (Input.GetKeyDown(KeyCode.Space))
            AdvanceToNextHole();
    }

    private void OnGUI()
    {
        if (showingStartMenu)
        {
            DrawStartMenu();
            return;
        }

        if (!awaitingNextHole || Time.timeScale <= 0f)
            return;

        EnsurePromptStyles();
        float scale = Mathf.Clamp(Screen.width / 1200f, 0.75f, 1.25f);
        float width = Mathf.Min(390f * scale, Screen.width - 28f);
        float height = 48f * scale;
        Rect prompt = new Rect((Screen.width - width) * 0.5f,
            Screen.height - 40f * scale - height, width, height);

        FrutigerAeroUI.DrawGlassPanel(prompt, FrutigerAeroUI.Cyan,
            new Color(0.83f, 0.97f, 1f, 0.94f), 0.14f);

        float keyWidth = 82f * scale;
        Rect key = new Rect(prompt.x + 11f * scale, prompt.y + 9f * scale,
            keyWidth, prompt.height - 18f * scale);
        FrutigerAeroUI.DrawPill(key, new Color(0.1f, 0.57f, 0.86f, 1f));
        nextHolePromptShadowStyle.fontSize = Mathf.RoundToInt(12f * scale);
        GUI.Label(key, "PRESS SPACE", nextHolePromptShadowStyle);

        nextHolePromptStyle.fontSize = Mathf.RoundToInt(16f * scale);
        Rect message = new Rect(key.xMax + 6f * scale, prompt.y, prompt.xMax - key.xMax - 18f * scale, prompt.height);
        GUI.Label(message, "FOR NEXT HOLE", nextHolePromptStyle);
    }

    private void PrepareStartMenu()
    {
        SetGameplayEnabled(false);
        if (musicManager != null)
            musicManager.PlayMenuMusic();
        ConfigureMenuCamera();
        FrutigerAeroUI.ResetGlassButtonHover();
        showingStartMenu = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void BeginGameplay()
    {
        if (!showingStartMenu)
            return;

        FrutigerAeroUI.ResetGlassButtonHover();
        showingStartMenu = false;
        PlayUiSound(startGameSound);
        if (menuCamera != null)
            menuCamera.StopOrbit();

        HoleStage firstStage = GetActiveStage();
        if (ball != null && firstStage != null && firstStage.teePoint != null)
            ball.ResetForNextHole(firstStage.teePoint.position, firstStage.teePoint.rotation);

        SetGameplayEnabled(true);
        if (musicManager != null)
            musicManager.PlayGameplayMusic();
        if (cameraController != null)
            cameraController.SnapFollowToTarget();

        if (cameraController != null)
            cameraController.LockMouse();
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void SetGameplayEnabled(bool enabled)
    {
        if (ball != null)
        {
            Rigidbody body = ball.GetComponent<Rigidbody>();
            if (body != null)
            {
#if UNITY_6000_0_OR_NEWER
                body.linearVelocity = Vector3.zero;
#else
                body.velocity = Vector3.zero;
#endif
                body.angularVelocity = Vector3.zero;
                body.isKinematic = !enabled;
                if (enabled)
                    body.WakeUp();
            }
            ball.enabled = enabled;
        }

        if (pauseMenu != null)
            pauseMenu.enabled = enabled;
        if (cameraController != null)
            cameraController.enabled = enabled;
    }

    private void ConfigureMenuCamera()
    {
        if (cameraController == null)
            return;

        cameraController.enabled = false;
        menuCamera = cameraController.GetComponent<CourseMenuCamera>();
        if (menuCamera == null)
            menuCamera = cameraController.gameObject.AddComponent<CourseMenuCamera>();

        Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Terrain courseTerrain = null;
        Terrain largestTerrain = null;
        float largestArea = 0f;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null || terrain.gameObject.scene != gameObject.scene)
                continue;

            if (terrain.name == "Course Terrain")
                courseTerrain = terrain;

            float area = terrain.terrainData.size.x * terrain.terrainData.size.z;
            if (area > largestArea)
            {
                largestArea = area;
                largestTerrain = terrain;
            }
        }

        Terrain focusTerrain = courseTerrain != null ? courseTerrain : largestTerrain;
        Vector3 target = ball != null ? ball.transform.position : Vector3.zero;
        float radius = 26f;
        float height = 18f;
        if (focusTerrain != null)
        {
            Vector3 size = focusTerrain.terrainData.size;
            target = focusTerrain.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            target.y = focusTerrain.SampleHeight(target) + focusTerrain.transform.position.y + 2.5f;
            float largestSide = Mathf.Max(size.x, size.z);
            radius = Mathf.Max(110f, largestSide * 1.08f);
            height = Mathf.Max(72f, largestSide * 0.72f);
        }

        menuCamera.Configure(target, radius, height, 0.018f);
    }

    private void DrawStartMenu()
    {
        EnsureStartMenuStyles();
        int previousDepth = GUI.depth;
        GUI.depth = -100;

        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.65f, 1.12f);
        float panelWidth = Mathf.Min(610f * scale, Screen.width - 36f);
        float panelHeight = Mathf.Min(448f * scale, Screen.height - 34f);
        Rect panel = new Rect(Screen.width * 0.055f,
            (Screen.height - panelHeight) * 0.5f, panelWidth, panelHeight);
        FrutigerAeroUI.DrawGlassPanel(panel, FrutigerAeroUI.Cyan,
            new Color(0.83f, 0.97f, 1f, 0.92f), 0.27f);

        FrutigerAeroUI.DrawGolfBall(new Vector2(panel.center.x, panel.y + 47f * scale), 54f * scale);
        startKickerStyle.fontSize = Mathf.RoundToInt(14f * scale);
        startTitleStyle.fontSize = Mathf.RoundToInt(46f * scale);
        startSubtitleStyle.fontSize = Mathf.RoundToInt(17f * scale);
        startButtonStyle.fontSize = Mathf.RoundToInt(21f * scale);
        startQuitButtonStyle.fontSize = Mathf.RoundToInt(16f * scale);
        startDetailStyle.fontSize = Mathf.RoundToInt(12f * scale);
        startFooterStyle.fontSize = Mathf.RoundToInt(13f * scale);

        FrutigerAeroUI.DrawPill(new Rect(panel.center.x - 116f * scale, panel.y + 80f * scale,
            232f * scale, 25f * scale), new Color(0.21f, 0.72f, 0.44f, 0.90f));
        GUI.Label(new Rect(panel.center.x - 112f * scale, panel.y + 80f * scale,
            224f * scale, 25f * scale), "THE NINE-HOLE TOUR", startKickerStyle);
        GUI.Label(new Rect(panel.x + 20f, panel.y + 108f * scale, panel.width - 40f, 64f * scale),
            "Golf is Hard", startTitleStyle);
        GUI.Label(new Rect(panel.x + 30f, panel.y + 167f * scale, panel.width - 60f, 42f * scale),
            "A sunny little golf adventure", startSubtitleStyle);

        float detailWidth = Mathf.Min(390f * scale, panel.width - 68f);
        Rect detail = new Rect(panel.center.x - detailWidth * 0.5f, panel.y + 218f * scale,
            detailWidth, 31f * scale);
        FrutigerAeroUI.DrawPill(detail, new Color(0.76f, 0.95f, 0.99f, 0.72f));
        GUI.Label(detail, "9 HOLES   •   1 BALL   •   BLUE SKIES", startDetailStyle);

        FrutigerAeroUI.DrawPill(new Rect(panel.x + panel.width * 0.18f, panel.y + 265f * scale,
            panel.width * 0.64f, 3f * scale), new Color(0.1f, 0.65f, 0.84f, 0.34f));

        float buttonWidth = Mathf.Min(285f * scale, panel.width - 70f);
        Rect playButton = new Rect(panel.center.x - buttonWidth * 0.5f,
            panel.y + 289f * scale, buttonWidth, 62f * scale);
        if (FrutigerAeroUI.DrawGlassButton(playButton, "LET'S TEE OFF", startButtonStyle,
                FrutigerAeroUI.Green, true, startMenuButtonHoverSound, uiSoundSource, startMenuHoverVolume))
            BeginGameplay();

        float quitButtonWidth = Mathf.Min(178f * scale, panel.width - 80f);
        Rect quitButton = new Rect(panel.center.x - quitButtonWidth * 0.5f,
            panel.y + 357f * scale, quitButtonWidth, 40f * scale);
        if (FrutigerAeroUI.DrawGlassButton(quitButton, "QUIT GAME", startQuitButtonStyle,
                FrutigerAeroUI.Coral, false, startMenuButtonHoverSound, uiSoundSource, startMenuHoverVolume))
            QuitApplication();

        GUI.Label(new Rect(panel.x + 20f, panel.yMax - 44f * scale, panel.width - 40f, 22f * scale),
            "CLICK TO START  •  PRESS SPACE", startFooterStyle);

        GUI.depth = previousDepth;
    }

    private void EnsureStartMenuStyles()
    {
        if (startTitleStyle != null)
            return;

        startKickerStyle = FrutigerAeroUI.CreateStyle(14, Color.white);
        startTitleStyle = FrutigerAeroUI.CreateStyle(46, FrutigerAeroUI.Ink);
        startSubtitleStyle = FrutigerAeroUI.CreateStyle(17,
            new Color(0.12f, 0.42f, 0.55f, 1f), TextAnchor.MiddleCenter, false);
        startButtonStyle = FrutigerAeroUI.CreateStyle(21, FrutigerAeroUI.Ink);
        startQuitButtonStyle = FrutigerAeroUI.CreateStyle(16, FrutigerAeroUI.Ink);
        startDetailStyle = FrutigerAeroUI.CreateStyle(12, new Color(0.10f, 0.45f, 0.60f, 1f));
        startFooterStyle = FrutigerAeroUI.CreateStyle(13, new Color(0.16f, 0.45f, 0.57f, 1f));
    }

    public void ReturnToStartMenu()
    {
        PlayUiSound(returnToMenuSound);
        if (menuCamera != null)
            menuCamera.StopOrbit();

        activeHoleIndex = 0;
        isTransitioning = false;
        awaitingNextHole = false;
        courseComplete = false;

        if (holesInOrder != null)
        {
            for (int i = 0; i < holesInOrder.Length; i++)
            {
                if (holesInOrder[i] != null && holesInOrder[i].hole != null)
                    holesInOrder[i].hole.ResetForNextHole();
            }
        }

        HoleStage firstStage = GetActiveStage();
        if (ball != null && firstStage != null && firstStage.teePoint != null)
            ball.ResetForNextHole(firstStage.teePoint.position, firstStage.teePoint.rotation);

        PrepareStartMenu();
    }

    private void SetupUiSoundSource()
    {
        if (uiSoundSource == null)
            uiSoundSource = GetComponent<AudioSource>();
        if (uiSoundSource == null)
            uiSoundSource = gameObject.AddComponent<AudioSource>();

        uiSoundSource.playOnAwake = false;
        uiSoundSource.spatialBlend = 0f;
        uiSoundSource.ignoreListenerPause = true;
    }

    private void PlayUiSound(AudioClip clip)
    {
        PlayUiSound(clip, transitionSoundVolume);
    }

    private void PlayUiSound(AudioClip clip, float volume)
    {
        if (clip != null && uiSoundSource != null)
            uiSoundSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    public void RestartCurrentHole()
    {
        HoleStage activeStage = GetActiveStage();
        if (ball == null || activeStage == null || activeStage.hole == null || activeStage.teePoint == null)
            return;

        PlayUiSound(restartHoleSound, restartHoleSoundVolume);
        activeStage.hole.ResetForNextHole();
        ball.ResetForNextHole(activeStage.teePoint.position, activeStage.teePoint.rotation);
        isTransitioning = false;
        awaitingNextHole = false;
        courseComplete = false;
        if (cameraController != null)
            cameraController.SnapFollowToTarget();
    }

    public void HandleHoleEntered(HoleTrigger enteredHole, BallPlayerController enteringBall)
    {
        if (enteredHole == null || enteringBall == null || courseComplete || isTransitioning)
            return;

        if (ball == null)
            ball = enteringBall;

        if (enteringBall != ball)
            return;

        HoleStage activeStage = GetActiveStage();
        if (activeStage == null || activeStage.hole == null)
        {
            Debug.LogError("GolfCourseManager needs at least one valid Hole entry in Holes In Order.", this);
            return;
        }

        if (enteredHole == activeStage.hole)
        {
            enteredHole.ShowHoleComplete(ball.StrokeCount);
            ball.StopForHole();

            if (activeHoleIndex + 1 < TotalHoleCount)
            {
                isTransitioning = true;
                awaitingNextHole = true;
            }
            else
            {
                courseComplete = true;
                PlayUiSound(courseCompleteSound);
            }
        }
        else
        {
            enteredHole.ShowWrongWay(ball, activeStage.hole.transform);
        }
    }

    public void HandleHoleExited(HoleTrigger exitedHole, BallPlayerController exitingBall)
    {
        if (exitedHole != null && exitingBall != null)
            exitedHole.ClearWrongWayWarning(exitingBall);
    }

    private void AdvanceToNextHole()
    {
        int nextIndex = activeHoleIndex + 1;
        if (holesInOrder == null || nextIndex >= holesInOrder.Length)
        {
            courseComplete = true;
            isTransitioning = false;
            awaitingNextHole = false;
            return;
        }

        HoleStage nextStage = holesInOrder[nextIndex];
        if (nextStage == null || nextStage.hole == null || nextStage.teePoint == null)
        {
            Debug.LogError(
                "The next course stage needs both a Hole and a Tee Point assigned. " +
                "Add the stages in order in GolfCourseManager.", this);
            isTransitioning = false;
            awaitingNextHole = false;
            courseComplete = true;
            return;
        }

        PlayUiSound(nextHoleSound);
        HoleTrigger completedHole = GetActiveStage().hole;
        activeHoleIndex = nextIndex;
        completedHole.ResetForNextHole();
        ball.ResetForNextHole(nextStage.teePoint.position, nextStage.teePoint.rotation);

        if (cameraController != null)
            cameraController.SnapFollowToTarget();

        isTransitioning = false;
        awaitingNextHole = false;
    }

    private void EnsurePromptStyles()
    {
        if (nextHolePromptStyle != null)
            return;

        nextHolePromptStyle = FrutigerAeroUI.CreateStyle(16, FrutigerAeroUI.Ink);
        nextHolePromptShadowStyle = FrutigerAeroUI.CreateStyle(12, Color.white);
    }

    private HoleStage GetActiveStage()
    {
        if (holesInOrder == null || activeHoleIndex < 0 || activeHoleIndex >= holesInOrder.Length)
            return null;

        return holesInOrder[activeHoleIndex];
    }
}
