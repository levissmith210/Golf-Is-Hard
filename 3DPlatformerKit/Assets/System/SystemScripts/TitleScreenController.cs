using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleScreenController : MonoBehaviour
{
    public Animator animator;
    public float timeToWait = 2f;
    [SerializeField] private string courseSceneName = "NineHoleCourse";

    private bool startPressed;
    private bool previewReady;
    private Camera titleCamera;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle eyebrowStyle;
    private GUIStyle buttonStyle;
    private GUIStyle footerStyle;

    private void Awake()
    {
        GameObject oldPrompt = GameObject.Find("ContinueText");
        if (oldPrompt != null)
            oldPrompt.SetActive(false);

        ApplyTitleSceneSetup();
    }

    private void ApplyTitleSceneSetup()
    {
        // Awake can run before Unity marks the scene as fully loaded, so repeat
        // this setup from Start as well to ensure the old canvas is handled.
        titleCamera = GetCameraInScene(gameObject.scene);
        Canvas[] canvases = GetCanvasesInScene(gameObject.scene);
        for (int i = 0; i < canvases.Length; i++)
        {
            UnityEngine.UI.Image[] legacyImages = canvases[i].GetComponentsInChildren<UnityEngine.UI.Image>(true);
            for (int imageIndex = 0; imageIndex < legacyImages.Length; imageIndex++)
            {
                UnityEngine.UI.Image legacyImage = legacyImages[imageIndex];
                if (legacyImage.sprite != null && legacyImage.sprite.name.StartsWith("TitleScreen"))
                {
                    legacyImage.enabled = false;
                    legacyImage.raycastTarget = false;
                }
            }

            Transform oldBackdrop = canvases[i].transform.Find("Image");
            if (oldBackdrop != null)
                oldBackdrop.gameObject.SetActive(false);

            if (canvases[i].renderMode != RenderMode.ScreenSpaceOverlay)
            {
                canvases[i].renderMode = RenderMode.ScreenSpaceOverlay;
                canvases[i].worldCamera = null;
            }
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private IEnumerator Start()
    {
        ApplyTitleSceneSetup();
        Scene preview = SceneManager.GetSceneByName(courseSceneName);
        if (preview.IsValid() && preview.isLoaded)
        {
            PrepareCoursePreview(preview);
            yield break;
        }

        AsyncOperation load = SceneManager.LoadSceneAsync(courseSceneName, LoadSceneMode.Additive);
        if (load == null)
        {
            Debug.LogWarning("Could not load the course preview scene: " + courseSceneName, this);
            yield break;
        }

        yield return load;

        if (!previewReady)
            PrepareCoursePreview(SceneManager.GetSceneByName(courseSceneName));
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == courseSceneName)
            PrepareCoursePreview(scene);
    }

    private void PrepareCoursePreview(Scene scene)
    {
        if (previewReady || !scene.IsValid() || !scene.isLoaded)
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        Camera previewCamera = GetCameraInScene(scene);
        Terrain bestTerrain = null;
        float largestArea = 0f;

        for (int i = 0; i < roots.Length; i++)
        {
            BallPlayerController[] balls = roots[i].GetComponentsInChildren<BallPlayerController>(true);
            for (int b = 0; b < balls.Length; b++)
            {
                balls[b].enabled = false;
                Rigidbody body = balls[b].GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
            }

            GolfCourseManager[] managers = roots[i].GetComponentsInChildren<GolfCourseManager>(true);
            for (int m = 0; m < managers.Length; m++)
                managers[m].enabled = false;

            PauseMenu[] pauseMenus = roots[i].GetComponentsInChildren<PauseMenu>(true);
            for (int p = 0; p < pauseMenus.Length; p++)
                pauseMenus[p].enabled = false;

            HoleTrigger[] holeTriggers = roots[i].GetComponentsInChildren<HoleTrigger>(true);
            for (int h = 0; h < holeTriggers.Length; h++)
            {
                holeTriggers[h].enabled = false;
                Collider triggerCollider = holeTriggers[h].GetComponent<Collider>();
                if (triggerCollider != null && triggerCollider.isTrigger)
                    triggerCollider.enabled = false;
            }

            ThirdPersonCameraController[] followCameras =
                roots[i].GetComponentsInChildren<ThirdPersonCameraController>(true);
            for (int c = 0; c < followCameras.Length; c++)
                followCameras[c].enabled = false;

            Terrain[] terrains = roots[i].GetComponentsInChildren<Terrain>(true);
            for (int t = 0; t < terrains.Length; t++)
            {
                Terrain terrain = terrains[t];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                float area = terrain.terrainData.size.x * terrain.terrainData.size.z;
                if (terrain.name == "Course Terrain")
                    bestTerrain = terrain;
                else if (area > largestArea)
                {
                    largestArea = area;
                    if (bestTerrain == null)
                        bestTerrain = terrain;
                }
            }

            if (roots[i].name == "Nine Hole Course" && !roots[i].activeSelf)
                roots[i].SetActive(true);
        }

        if (previewCamera == null)
        {
            Debug.LogWarning("The course preview scene needs a Camera.", this);
            return;
        }

        if (titleCamera != null)
        {
            titleCamera.tag = "Untagged";
            titleCamera.enabled = false;
            AudioListener titleListener = titleCamera.GetComponent<AudioListener>();
            if (titleListener != null)
                titleListener.enabled = false;
        }

        previewCamera.tag = "MainCamera";
        previewCamera.enabled = true;
        previewCamera.depth = 0f;

        Vector3 target = Vector3.zero;
        float radius = 95f;
        float height = 66f;

        if (bestTerrain != null)
        {
            Vector3 size = bestTerrain.terrainData.size;
            target = bestTerrain.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            target.y = bestTerrain.SampleHeight(target) + bestTerrain.transform.position.y + 2.5f;
            radius = Mathf.Max(110f, Mathf.Max(size.x, size.z) * 1.1f);
            height = Mathf.Max(72f, Mathf.Max(size.x, size.z) * 0.78f);
        }

        CourseMenuCamera menuCamera = previewCamera.GetComponent<CourseMenuCamera>();
        if (menuCamera == null)
            menuCamera = previewCamera.gameObject.AddComponent<CourseMenuCamera>();
        menuCamera.Configure(target, radius, height, 0.018f);

        previewReady = true;
    }

    private void Update()
    {
        if (!startPressed && Input.anyKey)
            BeginGame();
    }

    private void OnGUI()
    {
        if (startPressed)
            return;

        EnsureStyles();
        int previousDepth = GUI.depth;
        GUI.depth = -100;

        if (previewReady)
            FrutigerAeroUI.DrawSolid(new Rect(0f, 0f, Screen.width, Screen.height),
                new Color(0.02f, 0.29f, 0.46f, 0.10f));
        else
            FrutigerAeroUI.DrawSkyBackdrop();

        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.65f, 1.12f);
        float panelWidth = Mathf.Min(610f * scale, Screen.width - 32f);
        float panelHeight = Mathf.Min(410f * scale, Screen.height - 32f);
        Rect panel = new Rect(Screen.width * 0.07f,
            (Screen.height - panelHeight) * 0.5f, panelWidth, panelHeight);

        FrutigerAeroUI.DrawGlassPanel(panel, FrutigerAeroUI.Cyan,
            new Color(0.84f, 0.97f, 1f, 0.88f), 0.24f);

        FrutigerAeroUI.DrawGolfBall(new Vector2(panel.center.x, panel.y + 50f * scale), 46f * scale);
        eyebrowStyle.fontSize = Mathf.RoundToInt(14f * scale);
        titleStyle.fontSize = Mathf.RoundToInt(47f * scale);
        subtitleStyle.fontSize = Mathf.RoundToInt(18f * scale);
        buttonStyle.fontSize = Mathf.RoundToInt(21f * scale);
        footerStyle.fontSize = Mathf.RoundToInt(13f * scale);

        GUI.Label(new Rect(panel.x + 18f, panel.y + 73f * scale, panel.width - 36f, 26f * scale),
            "A SUNNY LITTLE GOLF ADVENTURE", eyebrowStyle);
        GUI.Label(new Rect(panel.x + 18f, panel.y + 100f * scale, panel.width - 36f, 70f * scale),
            "Golf is Hard", titleStyle);
        GUI.Label(new Rect(panel.x + 28f, panel.y + 165f * scale, panel.width - 56f, 43f * scale),
            "Nine holes • big swings • blue skies", subtitleStyle);

        FrutigerAeroUI.DrawPill(new Rect(panel.x + panel.width * 0.2f, panel.y + 214f * scale,
            panel.width * 0.6f, 3f * scale), new Color(0.1f, 0.65f, 0.84f, 0.3f));

        float buttonWidth = Mathf.Min(278f * scale, panel.width - 70f);
        Rect playButton = new Rect(panel.center.x - buttonWidth * 0.5f,
            panel.y + 237f * scale, buttonWidth, 62f * scale);
        if (FrutigerAeroUI.DrawGlassButton(playButton, "PLAY GOLF", buttonStyle, FrutigerAeroUI.Green, true))
            BeginGame();

        GUI.Label(new Rect(panel.x + 20f, panel.yMax - 48f * scale, panel.width - 40f, 24f * scale),
            "CLICK OR PRESS ANY BUTTON", footerStyle);

        GUI.depth = previousDepth;
    }

    private void BeginGame()
    {
        if (startPressed)
            return;

        startPressed = true;
        StartCoroutine(StartGameEnumerator());
    }

    private IEnumerator StartGameEnumerator()
    {
        if (animator != null)
            animator.SetTrigger("begin");

        yield return new WaitForSeconds(timeToWait);

        Scene titleScene = gameObject.scene;
        Scene courseScene = SceneManager.GetSceneByName(courseSceneName);
        if (previewReady && courseScene.IsValid() && courseScene.isLoaded &&
            SceneManager.SetActiveScene(courseScene))
        {
            ActivateCourseGameplay(courseScene);
            SceneManager.UnloadSceneAsync(titleScene);
            yield break;
        }

        SceneManager.LoadScene(courseSceneName, LoadSceneMode.Single);
    }

    private static void ActivateCourseGameplay(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == "Nine Hole Course" && !roots[i].activeSelf)
                roots[i].SetActive(true);
        }

        for (int i = 0; i < roots.Length; i++)
        {
            BallPlayerController[] balls = roots[i].GetComponentsInChildren<BallPlayerController>(true);
            for (int b = 0; b < balls.Length; b++)
            {
                balls[b].enabled = true;
                Rigidbody body = balls[b].GetComponent<Rigidbody>();
                if (body != null)
                    body.isKinematic = false;
            }

            GolfCourseManager[] managers = roots[i].GetComponentsInChildren<GolfCourseManager>(true);
            for (int m = 0; m < managers.Length; m++)
                managers[m].enabled = true;

            PauseMenu[] pauseMenus = roots[i].GetComponentsInChildren<PauseMenu>(true);
            for (int p = 0; p < pauseMenus.Length; p++)
                pauseMenus[p].enabled = true;

            HoleTrigger[] holeTriggers = roots[i].GetComponentsInChildren<HoleTrigger>(true);
            for (int h = 0; h < holeTriggers.Length; h++)
            {
                Collider triggerCollider = holeTriggers[h].GetComponent<Collider>();
                if (triggerCollider != null && triggerCollider.isTrigger)
                    triggerCollider.enabled = true;
                holeTriggers[h].enabled = true;
            }

            ThirdPersonCameraController[] followCameras =
                roots[i].GetComponentsInChildren<ThirdPersonCameraController>(true);
            for (int c = 0; c < followCameras.Length; c++)
                followCameras[c].enabled = true;

            CourseMenuCamera[] menuCameras = roots[i].GetComponentsInChildren<CourseMenuCamera>(true);
            for (int c = 0; c < menuCameras.Length; c++)
                menuCameras[c].StopOrbit();
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        titleStyle = FrutigerAeroUI.CreateStyle(47, FrutigerAeroUI.Ink);
        subtitleStyle = FrutigerAeroUI.CreateStyle(18, new Color(0.11f, 0.42f, 0.55f, 1f),
            TextAnchor.MiddleCenter, false);
        eyebrowStyle = FrutigerAeroUI.CreateStyle(14, FrutigerAeroUI.Blue);
        buttonStyle = FrutigerAeroUI.CreateStyle(21, FrutigerAeroUI.Ink);
        footerStyle = FrutigerAeroUI.CreateStyle(13, new Color(0.17f, 0.45f, 0.57f, 1f));
    }

    private static Camera GetCameraInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        Camera fallback = null;
        for (int i = 0; i < roots.Length; i++)
        {
            Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
            for (int c = 0; c < cameras.Length; c++)
            {
                if (cameras[c] == null)
                    continue;
                if (cameras[c].CompareTag("MainCamera"))
                    return cameras[c];
                if (fallback == null)
                    fallback = cameras[c];
            }
        }
        return fallback;
    }

    private static Canvas[] GetCanvasesInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return new Canvas[0];

        System.Collections.Generic.List<Canvas> canvases = new System.Collections.Generic.List<Canvas>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            canvases.AddRange(roots[i].GetComponentsInChildren<Canvas>(true));
        return canvases.ToArray();
    }
}

