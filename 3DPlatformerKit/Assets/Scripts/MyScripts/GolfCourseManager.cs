using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks the required hole order and moves the ball to each next tee after a
/// successful hole. Configure the stages in course order in the Inspector.
/// </summary>
public class GolfCourseManager : MonoBehaviour
{
    private const string TutorialCompletedPreference = "GolfIsHard.TutorialCompleted.v1";
    private const string TutorialSceneName = "NineHoleCourse";
    private const string MainCourseSceneName = "FrutigerAeroNineHole";
    private static bool startTutorialOnSceneLoad;
    private static bool startCourseOnSceneLoad;
    private enum TutorialStep
    {
        None,
        LookAround,
        ZoomCamera,
        ShootFirstHole,
        WaitingForNextHole,
        AngleSecondHole,
        Complete
    }

    [Serializable]
    private class HoleStage
    {
        [Tooltip("The hole trigger that completes this stage.")]
        public HoleTrigger hole;

        [Tooltip("Assign a Golf Tee prefab root or a ball spawn Transform. Tee prefabs use their Ball Spawn Point child.")]
        public Transform teePoint;
    }

    [Header("Course Order")]
    [Tooltip("Add only the playable course holes here, ordered from the first hole to the last.")]
    [SerializeField] private HoleStage[] holesInOrder = new HoleStage[0];

    [Header("Tutorial Holes")]
    [Tooltip("Assign the tutorial practice holes here. They are played before the actual course and are not included in its hole count.")]
    [SerializeField] private HoleStage[] tutorialHoles = new HoleStage[0];

    [Header("References")]
    [SerializeField] private BallPlayerController ball;
    [SerializeField] private ThirdPersonCameraController cameraController;

    [Header("Start Menu")]
    [SerializeField] private bool showStartMenuOnLaunch = true;

    [Header("Tutorial")]
    [Tooltip("Start the guided controls tutorial when the player chooses Tee Off.")]
    [SerializeField] private bool playTutorialOnStart = true;

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
    private bool settingsOpen;
    private GUIStyle startKickerStyle;
    private GUIStyle startTitleStyle;
    private GUIStyle startSubtitleStyle;
    private GUIStyle startButtonStyle;
    private GUIStyle startQuitButtonStyle;
    private GUIStyle startDetailStyle;
    private GUIStyle startFooterStyle;
    private int activeHoleIndex;
    private int activeTutorialHoleIndex;
    private bool tutorialMode;
    private bool isTransitioning;
    private bool awaitingNextHole;
    private bool courseComplete;
    private GUIStyle nextHolePromptStyle;
    private GUIStyle nextHolePromptShadowStyle;
    private GUIStyle tutorialEyebrowStyle;
    private GUIStyle tutorialTitleStyle;
    private GUIStyle tutorialBodyStyle;
    private GUIStyle tutorialProgressStyle;
    private GUIStyle tutorialStepNumberStyle;
    private TutorialStep tutorialStep;
    private bool tutorialAngleShotUnlocked;
    private float tutorialLookStartTime = -1f;
    private float tutorialCourseTransitionTime = -1f;

    public int ActiveHoleNumber => activeHoleIndex + 1;
    public int TotalHoleCount => holesInOrder != null ? holesInOrder.Length : 0;
    public bool IsCourseComplete => courseComplete;
    public HoleTrigger ActiveHole => GetActiveStage()?.hole;

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

        AssignCourseManagerToStages(holesInOrder);
        AssignCourseManagerToStages(tutorialHoles);
    }

    private void Start()
    {
        bool autoStartTutorial = startTutorialOnSceneLoad;
        bool autoStartCourse = startCourseOnSceneLoad;
        startTutorialOnSceneLoad = false;
        startCourseOnSceneLoad = false;

        if (autoStartTutorial)
        {
            if (!CanRunTutorial())
            {
                Debug.LogError("The tutorial scene needs two tutorial stages, a ball, and a camera assigned.", this);
                showStartMenuOnLaunch = true;
            }
            else
            {
                StartGameplayFromScene();
                StartTutorial();
                return;
            }
        }

        HoleStage firstStage = GetActiveStage();
        if (ball == null || firstStage == null || firstStage.teePoint == null)
        {
            if (autoStartCourse)
                Debug.LogError("The Frutiger Aero course needs its first hole and tee assigned.", this);
            return;
        }

        Transform spawnPoint = ResolveBallSpawnPoint(firstStage.teePoint);
        ball.ResetForNextHole(spawnPoint.position, spawnPoint.rotation);

        if (showStartMenuOnLaunch && !autoStartCourse)
        {
            PrepareStartMenu();
            return;
        }

        StartGameplayFromScene();
    }

    private void StartGameplayFromScene()
    {
        SetGameplayEnabled(true);
        if (musicManager != null)
            musicManager.PlayGameplayMusic();
        if (cameraController != null)
        {
            cameraController.SnapFollowToTarget();
            cameraController.LockMouse();
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
    private void Update()
    {
        if (showingStartMenu)
        {
            if (settingsOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    settingsOpen = false;
                    GameSettings.Save();
                    FrutigerAeroUI.ResetGlassButtonHover();
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                BeginGameplay();
            return;
        }

        if (tutorialCourseTransitionTime >= 0f)
        {
            if (Time.timeScale > 0f && Time.unscaledTime >= tutorialCourseTransitionTime)
            {
                tutorialCourseTransitionTime = -1f;
                StartActualCourseAfterTutorial();
            }
            return;
        }

        if (Time.timeScale > 0f)
            UpdateTutorialProgress();

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

        if (Time.timeScale <= 0f)
            return;

        if (awaitingNextHole)
        {
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
            Rect message = new Rect(key.xMax + 6f * scale, prompt.y,
                prompt.xMax - key.xMax - 18f * scale, prompt.height);
            string promptMessage = tutorialMode && tutorialStep == TutorialStep.Complete
                ? "TO START THE COURSE"
                : "FOR NEXT HOLE";
            GUI.Label(message, promptMessage, nextHolePromptStyle);
            return;
        }

        DrawTutorialPrompt();
    }
    private void PrepareStartMenu()
    {
        SetGameplayEnabled(false);
        if (musicManager != null)
            musicManager.PlayMenuMusic();
        ConfigureMenuCamera();
        FrutigerAeroUI.ResetGlassButtonHover();
        settingsOpen = false;
        showingStartMenu = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void BeginGameplay()
    {
        if (!showingStartMenu)
            return;

        FrutigerAeroUI.ResetGlassButtonHover();
        PlayUiSound(startGameSound);
        if (menuCamera != null)
            menuCamera.StopOrbit();

        Scene activeScene = SceneManager.GetActiveScene();
        bool isFirstPlayFromMainCourse = activeScene.name == MainCourseSceneName &&
            playTutorialOnStart && PlayerPrefs.GetInt(TutorialCompletedPreference, 0) == 0;
        if (isFirstPlayFromMainCourse)
        {
            showingStartMenu = false;
            startTutorialOnSceneLoad = true;
            SceneManager.LoadScene(TutorialSceneName);
            return;
        }

        showingStartMenu = false;
        HoleStage firstStage = GetActiveStage();
        if (ball != null && firstStage != null && firstStage.teePoint != null)
        {
            Transform spawnPoint = ResolveBallSpawnPoint(firstStage.teePoint);
            ball.ResetForNextHole(spawnPoint.position, spawnPoint.rotation);
        }

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

        if (CanRunTutorial())
            StartTutorial();
        else
            CompleteTutorial();
    }
    private bool CanRunTutorial()
    {
        return playTutorialOnStart && tutorialHoles != null &&
               tutorialHoles.Length >= 2 &&
               IsValidStage(tutorialHoles[0]) && IsValidStage(tutorialHoles[1]) &&
               ball != null && cameraController != null;
    }

    private void StartTutorial()
    {
        tutorialMode = true;
        activeTutorialHoleIndex = 0;
        tutorialStep = TutorialStep.LookAround;
        tutorialAngleShotUnlocked = false;
        tutorialLookStartTime = -1f;
        isTransitioning = false;
        awaitingNextHole = false;
        courseComplete = false;

        HoleStage firstTutorialStage = GetActiveStage();
        firstTutorialStage.hole.ResetForNextHole();
        Transform spawnPoint = ResolveBallSpawnPoint(firstTutorialStage.teePoint);
        ball.ResetForNextHole(spawnPoint.position, spawnPoint.rotation);
        ball.SetTutorialControlAccess(false, false);
        cameraController.SetTutorialInputAccess(true, false, false, true);
        cameraController.SnapFollowToTarget();
    }

    private void UpdateTutorialProgress()
    {
        if (cameraController == null || ball == null)
            return;

        if (tutorialStep == TutorialStep.LookAround && cameraController.TutorialLookInputDetected)
        {
            if (tutorialLookStartTime < 0f)
                tutorialLookStartTime = Time.time;

            if (Time.time - tutorialLookStartTime >= 2f)
            {
                tutorialStep = TutorialStep.ZoomCamera;
                tutorialLookStartTime = -1f;
                cameraController.SetTutorialInputAccess(true, true, true, true);
            }
        }
        else if (tutorialStep == TutorialStep.ZoomCamera &&
                 cameraController.TutorialZoomInUsed &&
                 cameraController.TutorialZoomOutUsed &&
                 cameraController.TutorialZoomResetUsed)
        {
            tutorialStep = TutorialStep.ShootFirstHole;
            ball.SetTutorialControlAccess(true, false);
            cameraController.SetTutorialInputAccess(true, true, true, false);
        }
        else if (tutorialStep == TutorialStep.AngleSecondHole &&
                 !tutorialAngleShotUnlocked &&
                 ball.TutorialAngleInputUsed)
        {
            tutorialAngleShotUnlocked = true;
            ball.SetTutorialControlAccess(true, true);
        }
    }

    private void CompleteTutorial()
    {
        tutorialStep = TutorialStep.Complete;
        tutorialAngleShotUnlocked = true;
        if (ball != null)
            ball.SetTutorialControlAccess(true, true);
        if (cameraController != null)
            cameraController.SetTutorialInputAccess(true, true, true, false);
    }

    private void BeginSecondHoleTutorial()
    {
        tutorialStep = TutorialStep.AngleSecondHole;
        tutorialAngleShotUnlocked = false;
        if (ball != null)
        {
            ball.ResetTutorialAngleInputProgress();
            ball.SetTutorialControlAccess(false, true);
        }
        if (cameraController != null)
            cameraController.SetTutorialInputAccess(true, true, true, false);
    }

    private void RestoreTutorialControlsForCurrentHole()
    {
        if (tutorialStep == TutorialStep.WaitingForNextHole)
        {
            if (activeTutorialHoleIndex == 0)
            {
                tutorialStep = TutorialStep.ShootFirstHole;
                if (ball != null)
                    ball.SetTutorialControlAccess(true, false);
            }
            else if (activeTutorialHoleIndex == 1)
            {
                BeginSecondHoleTutorial();
            }
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
        if (settingsOpen)
        {
            if (GameSettings.DrawSettingsMenu())
            {
                settingsOpen = false;
                GameSettings.Save();
                FrutigerAeroUI.ResetGlassButtonHover();
            }
            return;
        }

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
            "A Frutiger Aero Golf Experience", startSubtitleStyle);

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
        if (FrutigerAeroUI.DrawGlassButton(playButton, "TEE OFF", startButtonStyle,
                FrutigerAeroUI.Green, true, startMenuButtonHoverSound, uiSoundSource, startMenuHoverVolume))
            BeginGameplay();

        float lowerButtonWidth = Mathf.Min(178f * scale, (panel.width - 78f * scale) * 0.5f);
        float lowerButtonY = panel.y + 357f * scale;
        Rect settingsButton = new Rect(panel.center.x - lowerButtonWidth - 7f * scale,
            lowerButtonY, lowerButtonWidth, 40f * scale);
        if (FrutigerAeroUI.DrawGlassButton(settingsButton, "SETTINGS", startQuitButtonStyle,
                FrutigerAeroUI.Blue, false, startMenuButtonHoverSound, uiSoundSource, startMenuHoverVolume))
        {
            settingsOpen = true;
            FrutigerAeroUI.ResetGlassButtonHover();
        }

        Rect quitButton = new Rect(panel.center.x + 7f * scale, lowerButtonY,
            lowerButtonWidth, 40f * scale);
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
        activeTutorialHoleIndex = 0;
        tutorialMode = false;
        isTransitioning = false;
        awaitingNextHole = false;
        tutorialCourseTransitionTime = -1f;
        courseComplete = false;
        tutorialStep = TutorialStep.None;
        tutorialAngleShotUnlocked = false;
        if (ball != null)
            ball.SetTutorialControlAccess(true, true);
        if (cameraController != null)
            cameraController.SetTutorialInputAccess(true, true, true, false);

        ResetStages(holesInOrder);
        ResetStages(tutorialHoles);

        HoleStage firstStage = GetActiveStage();
        if (ball != null && firstStage != null && firstStage.teePoint != null)
        {
            Transform spawnPoint = ResolveBallSpawnPoint(firstStage.teePoint);
            ball.ResetForNextHole(spawnPoint.position, spawnPoint.rotation);
        }

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
        GameSettings.PlaySfx(uiSoundSource, clip, volume);
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
        Transform spawnPoint = ResolveBallSpawnPoint(activeStage.teePoint);
        ball.ResetForNextHole(spawnPoint.position, spawnPoint.rotation);
        isTransitioning = false;
        awaitingNextHole = false;
        courseComplete = false;
        if (cameraController != null)
            cameraController.SnapFollowToTarget();

        RestoreTutorialControlsForCurrentHole();
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
            Debug.LogError(tutorialMode
                ? "GolfCourseManager needs a valid active entry in Tutorial Holes."
                : "GolfCourseManager needs at least one valid Hole entry in Holes In Order.", this);
            return;
        }

        if (enteredHole != activeStage.hole)
        {
            enteredHole.ShowWrongWay(ball, activeStage.hole.transform);
            return;
        }

        enteredHole.ShowHoleComplete(ball.StrokeCount);
        ball.StopForHole();
        if (ball.ShotFeedback != null)
            ball.ShotFeedback.CupCaptured(enteredHole.transform.position);

        if (tutorialMode)
        {
            bool finishedTutorial = false;
            if (tutorialStep == TutorialStep.ShootFirstHole && activeTutorialHoleIndex == 0)
            {
                tutorialStep = TutorialStep.WaitingForNextHole;
            }
            else if (tutorialStep == TutorialStep.AngleSecondHole &&
                     activeTutorialHoleIndex == 1 && tutorialAngleShotUnlocked)
            {
                CompleteTutorial();
                finishedTutorial = true;
            }

            isTransitioning = true;
            awaitingNextHole = !finishedTutorial;
            if (finishedTutorial)
                tutorialCourseTransitionTime = Time.unscaledTime + 1.5f;
        }
        else if (activeHoleIndex + 1 < TotalHoleCount)
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
    public void HandleHoleExited(HoleTrigger exitedHole, BallPlayerController exitingBall)
    {
        if (exitedHole != null && exitingBall != null)
            exitedHole.ClearWrongWayWarning(exitingBall);
    }

    private void AdvanceToNextHole()
    {
        if (tutorialMode)
        {
            if (tutorialStep == TutorialStep.Complete)
            {
                StartActualCourseAfterTutorial();
                return;
            }

            int nextTutorialIndex = activeTutorialHoleIndex + 1;
            if (tutorialHoles == null || nextTutorialIndex >= tutorialHoles.Length ||
                !IsValidStage(tutorialHoles[nextTutorialIndex]))
            {
                Debug.LogError("The next tutorial stage needs both a Hole and a Tee Point assigned.", this);
                isTransitioning = false;
                awaitingNextHole = false;
                return;
            }

            PlayUiSound(nextHoleSound);
            HoleStage completedTutorialStage = GetActiveStage();
            completedTutorialStage.hole.ResetForNextHole();
            activeTutorialHoleIndex = nextTutorialIndex;
            HoleStage nextTutorialStage = GetActiveStage();
            nextTutorialStage.hole.ResetForNextHole();
            Transform tutorialSpawnPoint = ResolveBallSpawnPoint(nextTutorialStage.teePoint);
            ball.ResetForNextHole(tutorialSpawnPoint.position, tutorialSpawnPoint.rotation);

            if (activeTutorialHoleIndex == 1)
                BeginSecondHoleTutorial();

            if (cameraController != null)
                cameraController.SnapFollowToTarget();

            isTransitioning = false;
            awaitingNextHole = false;
            return;
        }

        int nextIndex = activeHoleIndex + 1;
        if (holesInOrder == null || nextIndex >= holesInOrder.Length)
        {
            courseComplete = true;
            isTransitioning = false;
            awaitingNextHole = false;
            return;
        }

        HoleStage nextStage = holesInOrder[nextIndex];
        if (!IsValidStage(nextStage))
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
        Transform spawnPoint = ResolveBallSpawnPoint(nextStage.teePoint);
        ball.ResetForNextHole(spawnPoint.position, spawnPoint.rotation);

        if (cameraController != null)
            cameraController.SnapFollowToTarget();

        isTransitioning = false;
        awaitingNextHole = false;
    }

    private void StartActualCourseAfterTutorial()
    {
        tutorialCourseTransitionTime = -1f;
        tutorialMode = false;
        activeTutorialHoleIndex = 0;
        isTransitioning = true;
        awaitingNextHole = false;
        courseComplete = false;

        PlayerPrefs.SetInt(TutorialCompletedPreference, 1);
        PlayerPrefs.Save();
        startCourseOnSceneLoad = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(MainCourseSceneName);
    }
    private void EnsurePromptStyles()
    {
        if (nextHolePromptStyle != null)
            return;

        nextHolePromptStyle = FrutigerAeroUI.CreateStyle(16, FrutigerAeroUI.Ink);
        nextHolePromptShadowStyle = FrutigerAeroUI.CreateStyle(12, Color.white);
    }

    private void DrawTutorialPrompt()
    {
        if (tutorialStep != TutorialStep.LookAround &&
            tutorialStep != TutorialStep.ZoomCamera &&
            tutorialStep != TutorialStep.ShootFirstHole &&
            tutorialStep != TutorialStep.AngleSecondHole)
            return;

        EnsureTutorialStyles();
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.72f, 1.15f);
        float width = Mathf.Min(660f * scale, Screen.width - 28f);
        float height = 178f * scale;
        Rect panel = new Rect((Screen.width - width) * 0.5f, 24f * scale, width, height);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f);

        FrutigerAeroUI.DrawGlassPanel(panel, FrutigerAeroUI.Cyan,
            new Color(0.88f, 0.98f, 1f, 0.95f), 0.24f);
        FrutigerAeroUI.DrawCircle(new Rect(panel.x + 18f * scale, panel.y + 9f * scale,
            46f * scale, 46f * scale), new Color(0.35f, 0.89f, 1f, 0.12f + pulse * 0.04f));
        FrutigerAeroUI.DrawCircle(new Rect(panel.xMax - 52f * scale, panel.yMax - 55f * scale,
            68f * scale, 68f * scale), new Color(0.45f, 0.93f, 0.68f, 0.11f + pulse * 0.035f));

        int stageNumber = tutorialStep == TutorialStep.LookAround ? 1 :
            tutorialStep == TutorialStep.ZoomCamera ? 2 :
            tutorialStep == TutorialStep.ShootFirstHole ? 3 : 4;
        string heading;
        string instruction;

        switch (tutorialStep)
        {
            case TutorialStep.LookAround:
                heading = "LOOK AROUND";
                instruction = "Move your mouse to look around.";
                break;
            case TutorialStep.ZoomCamera:
            {
                heading = "CAMERA ZOOM";
                bool triedBothZoomDirections = cameraController != null &&
                    cameraController.TutorialZoomInUsed && cameraController.TutorialZoomOutUsed;
                instruction = triedBothZoomDirections
                    ? "Press the scroll wheel or Enter to reset the camera."
                    : "Scroll in and out to zoom the camera.";
                break;
            }
            case TutorialStep.ShootFirstHole:
                heading = "TAKE YOUR SHOT";
                instruction = "Hold left click: power goes up and down. Release in gold for a perfect shot. Right click cancels.";
                break;
            default:
                heading = "AIM YOUR SHOT";
                instruction = tutorialAngleShotUnlocked
                    ? "Aim set! Shoot Hole 2 with left click; right click cancels."
                    : "Use W to raise your shot and S to lower it. Try both to unlock shooting.";
                break;
        }

        Rect badge = new Rect(panel.x + 22f * scale, panel.y + 15f * scale,
            166f * scale, 27f * scale);
        FrutigerAeroUI.DrawPill(badge,
            Color.Lerp(new Color(0.08f, 0.63f, 0.87f, 0.95f),
                new Color(0.12f, 0.76f, 0.76f, 0.98f), pulse * 0.18f));
        tutorialEyebrowStyle.fontSize = Mathf.RoundToInt(12f * scale);
        GUI.Label(badge, "TUTORIAL   " + stageNumber + " / 4", tutorialEyebrowStyle);

        tutorialTitleStyle.fontSize = Mathf.RoundToInt(25f * scale);
        tutorialBodyStyle.fontSize = Mathf.RoundToInt(16f * scale);
        tutorialProgressStyle.fontSize = Mathf.RoundToInt(12f * scale);
        GUI.Label(new Rect(panel.x + 20f * scale, panel.y + 49f * scale,
            panel.width - 40f * scale, 31f * scale), heading, tutorialTitleStyle);
        GUI.Label(new Rect(panel.x + 25f * scale, panel.y + 82f * scale,
            panel.width - 50f * scale, 31f * scale), instruction, tutorialBodyStyle);

        float trackX = panel.x + 25f * scale;
        float trackWidth = panel.width - 50f * scale;
        float trackY = panel.yMax - 20f * scale;
        Rect track = new Rect(trackX, trackY, trackWidth, 5f * scale);
        FrutigerAeroUI.DrawPill(track, new Color(0.23f, 0.63f, 0.76f, 0.24f));

        float lookProgress = tutorialStep == TutorialStep.LookAround && tutorialLookStartTime >= 0f
            ? Mathf.Clamp01((Time.time - tutorialLookStartTime) / 2f)
            : 0f;
        float overallProgress = Mathf.Clamp01((stageNumber - 1f + lookProgress) / 4f);
        if (overallProgress > 0f)
        {
            Rect fill = new Rect(track.x, track.y, Mathf.Max(5f * scale, track.width * overallProgress), track.height);
            FrutigerAeroUI.DrawPill(fill, new Color(0.12f, 0.71f, 0.91f, 0.9f));
            float shineX = fill.x + fill.width * pulse - 5f * scale;
            Rect shine = new Rect(shineX, fill.y, 10f * scale, fill.height);
            FrutigerAeroUI.DrawPill(shine, new Color(1f, 1f, 1f, 0.7f));
        }

        float nodeSize = 15f * scale;
        for (int i = 0; i < 4; i++)
        {
            float x = trackX + trackWidth * i / 3f - nodeSize * 0.5f;
            Rect node = new Rect(x, trackY - 5f * scale, nodeSize, nodeSize);
            bool completed = i < stageNumber - 1;
            bool current = i == stageNumber - 1;
            if (current)
                FrutigerAeroUI.DrawCircle(new Rect(node.x - 5f * scale, node.y - 5f * scale,
                    node.width + 10f * scale, node.height + 10f * scale),
                    new Color(0.18f, 0.78f, 1f, 0.12f + pulse * 0.12f));
            FrutigerAeroUI.DrawCircle(node, completed
                ? new Color(0.23f, 0.78f, 0.55f, 1f)
                : current ? new Color(0.12f, 0.68f, 0.91f, 1f)
                : new Color(0.53f, 0.77f, 0.84f, 0.72f));
            tutorialProgressStyle.fontSize = Mathf.RoundToInt(9f * scale);
            tutorialProgressStyle.normal.textColor = Color.white;
            GUI.Label(node, (i + 1).ToString(), tutorialProgressStyle);
        }
    }

    private void EnsureTutorialStyles()
    {
        if (tutorialTitleStyle != null)
            return;

        tutorialEyebrowStyle = FrutigerAeroUI.CreateStyle(12, Color.white);
        tutorialTitleStyle = FrutigerAeroUI.CreateStyle(24, FrutigerAeroUI.Ink);
        tutorialBodyStyle = FrutigerAeroUI.CreateStyle(16,
            new Color(0.12f, 0.35f, 0.46f, 1f), TextAnchor.MiddleCenter, false);
        tutorialProgressStyle = FrutigerAeroUI.CreateStyle(12,
            new Color(0.16f, 0.47f, 0.59f, 1f), TextAnchor.MiddleCenter, false);
        tutorialStepNumberStyle = FrutigerAeroUI.CreateStyle(10, Color.white);
    }
    private HoleStage GetActiveStage()
    {
        HoleStage[] stages = tutorialMode ? tutorialHoles : holesInOrder;
        int stageIndex = tutorialMode ? activeTutorialHoleIndex : activeHoleIndex;
        if (stages == null || stageIndex < 0 || stageIndex >= stages.Length)
            return null;

        return stages[stageIndex];
    }

    private void AssignCourseManagerToStages(HoleStage[] stages)
    {
        if (stages == null)
            return;

        for (int i = 0; i < stages.Length; i++)
        {
            if (stages[i] != null && stages[i].hole != null)
                stages[i].hole.SetCourseManager(this);
        }
    }

    private static bool IsValidStage(HoleStage stage)
    {
        return stage != null && stage.hole != null && stage.teePoint != null;
    }

    private static void ResetStages(HoleStage[] stages)
    {
        if (stages == null)
            return;

        for (int i = 0; i < stages.Length; i++)
        {
            if (stages[i] != null && stages[i].hole != null)
                stages[i].hole.ResetForNextHole();
        }
    }

    private static Transform ResolveBallSpawnPoint(Transform teePoint)
    {
        if (teePoint == null)
            return null;

        GolfTeeSpawnPoint tee = teePoint.GetComponentInParent<GolfTeeSpawnPoint>();
        return tee != null && tee.BallSpawnPoint != null ? tee.BallSpawnPoint : teePoint;
    }
}






