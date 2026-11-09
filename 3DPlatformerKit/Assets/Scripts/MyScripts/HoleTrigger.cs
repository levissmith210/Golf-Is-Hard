using UnityEngine;

/// <summary>
/// Completes the hole when the golf ball enters this trigger, plays the assigned
/// effects, and displays the number of strokes used.
/// Attach this to the hole's trigger collider.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HoleTrigger : MonoBehaviour
{
    [Header("Hole Effects")]
    [Tooltip("A particle system prefab to spawn when the ball reaches the hole.")]
    [SerializeField] private ParticleSystem holeParticlePrefab;
    [Tooltip("Sound played when the ball reaches the hole.")]
    [SerializeField] private AudioClip holeSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 1f;
    [Tooltip("Sound played once when the ball enters a hole out of course order.")]
    [SerializeField] private AudioClip wrongHoleSound;
    [SerializeField, Range(0f, 1f)] private float wrongHoleSoundVolume = 0.8f;
    [SerializeField] private AudioSource soundEffectsSource;

    [Header("Result Display")]
    [SerializeField] private bool showResult = true;

    [Header("Wrong Hole Warning")]
    [SerializeField, Min(0.1f)] private float wrongWayArrowHeight = 2.1f;
    [SerializeField, Min(0.1f)] private float wrongWayArrowSize = 1.15f;

    private bool holeCompleted;
    private bool warnedMissingCourseManager;
    private int completedStrokeCount;
    private float resultShownAt;
    private Collider holeCollider;
    private PauseMenu pauseMenu;
    private GolfCourseManager courseManager;
    private BallPlayerController wrongWayBall;
    private Transform correctHoleTarget;
    private Texture2D roundedTexture;
    private Texture2D circleTexture;
    private GameObject wrongArrowRoot;
    private Mesh wrongArrowMesh;
    private MeshRenderer wrongArrowRenderer;
    private MeshRenderer wrongArrowFaceRenderer;
    private Material wrongArrowOutlineMaterial;
    private Material wrongArrowFaceMaterial;
    private GUIStyle headingStyle;
    private GUIStyle scoreStyle;
    private GUIStyle scoreShadowStyle;
    private GUIStyle strokeLabelStyle;
    private GUIStyle wrongWayStyle;

    private void Awake()
    {
        holeCollider = GetComponent<Collider>();
        if (soundEffectsSource == null)
            soundEffectsSource = GetComponent<AudioSource>();
        if (soundEffectsSource == null)
            soundEffectsSource = gameObject.AddComponent<AudioSource>();
        soundEffectsSource.playOnAwake = false;
        soundEffectsSource.spatialBlend = 1f;
        soundEffectsSource.dopplerLevel = 0f;

        if (holeCollider != null)
            holeCollider.isTrigger = true;

        pauseMenu = FindObjectOfType<PauseMenu>();
        courseManager = FindObjectOfType<GolfCourseManager>();
        roundedTexture = CreateRoundedTexture(256, 128);
        circleTexture = CreateCircleTexture(64);
    }

    private void Update()
    {
        UpdateWrongWayArrow();
    }

    private void OnTriggerEnter(Collider other)
    {
        BallPlayerController ball = other.GetComponentInParent<BallPlayerController>();
        if (ball == null)
            return;

        if (courseManager == null)
        {
            if (!warnedMissingCourseManager)
            {
                Debug.LogWarning("Add a GolfCourseManager to the gameplay scene and list this hole in course order.", this);
                warnedMissingCourseManager = true;
            }
            return;
        }

        courseManager.HandleHoleEntered(this, ball);
    }

    private void OnTriggerExit(Collider other)
    {
        BallPlayerController ball = other.GetComponentInParent<BallPlayerController>();
        if (ball == null)
            return;

        if (courseManager != null)
            courseManager.HandleHoleExited(this, ball);
        else
            ClearWrongWayWarning(ball);
    }

    public void SetCourseManager(GolfCourseManager manager)
    {
        courseManager = manager;
    }

    public void ShowHoleComplete(int strokeCount)
    {
        if (holeCompleted)
            return;

        ClearWrongWayWarning(wrongWayBall);
        holeCompleted = true;
        completedStrokeCount = strokeCount;
        resultShownAt = Time.unscaledTime;
        PlayHoleEffects();
    }

    public void ShowWrongWay(BallPlayerController ball, Transform rightHole)
    {
        if (holeCompleted || ball == null || rightHole == null)
            return;

        bool isNewWarning = wrongWayBall != ball || correctHoleTarget != rightHole;
        wrongWayBall = ball;
        correctHoleTarget = rightHole;
        if (isNewWarning)
            PlayHoleSound(wrongHoleSound, wrongHoleSoundVolume);
        CreateWrongWayArrowIfNeeded();
        UpdateWrongWayArrow();
    }

    public void ClearWrongWayWarning(BallPlayerController ball)
    {
        if (ball == null || wrongWayBall != ball)
            return;

        wrongWayBall = null;
        correctHoleTarget = null;
        if (wrongArrowRoot != null)
            wrongArrowRoot.SetActive(false);
    }

    public void ResetForNextHole()
    {
        holeCompleted = false;
        completedStrokeCount = 0;
        resultShownAt = 0f;
        ClearWrongWayWarning(wrongWayBall);
    }

    private void PlayHoleEffects()
    {
        Vector3 holePosition = holeCollider != null ? holeCollider.bounds.center : transform.position;

        if (holeParticlePrefab != null)
        {
            ParticleSystem particles = Instantiate(
                holeParticlePrefab,
                holePosition,
                transform.rotation);
            particles.Play(true);

            ParticleSystem.MainModule main = particles.main;
            float lifetime = main.duration + main.startLifetime.constantMax;
            Destroy(particles.gameObject, Mathf.Max(2f, lifetime));
        }

        PlayHoleSound(holeSound, soundVolume);
    }

    private void PlayHoleSound(AudioClip clip, float volume)
    {
        if (clip != null && soundEffectsSource != null)
            soundEffectsSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private void OnGUI()
    {
        if (pauseMenu != null && pauseMenu.IsPaused)
            return;

        EnsureStyles();

        if (wrongWayBall != null)
            DrawWrongWayBanner();

        if (!holeCompleted || !showResult)
            return;

        float scale = Mathf.Clamp(Screen.width / 1200f, 0.72f, 1.25f);
        float width = 356f * scale;
        float height = 174f * scale;
        float popProgress = Mathf.Clamp01((Time.unscaledTime - resultShownAt) / 0.32f);
        float popEase = 1f - Mathf.Pow(1f - popProgress, 3f);
        float popScale = Mathf.Lerp(0.82f, 1f, popEase);
        float bob = popProgress >= 1f ? Mathf.Sin(Time.unscaledTime * 2.5f) * 1.5f * scale : 0f;
        Rect panel = new Rect((Screen.width - width) * 0.5f, 40f * scale + bob, width, height);

        Matrix4x4 previousMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(popScale, popScale), panel.center);

        FrutigerAeroUI.DrawGlassPanel(panel, FrutigerAeroUI.Cyan,
            new Color(0.83f, 0.97f, 1f, 0.95f), 0.24f);
        FrutigerAeroUI.DrawPill(new Rect(panel.x + 58f * scale, panel.y + 13f * scale,
            panel.width - 116f * scale, 4f * scale), new Color(0.24f, 0.78f, 0.93f, 0.46f));
        FrutigerAeroUI.DrawGolfBall(new Vector2(panel.x + 39f * scale, panel.y + 112f * scale), 30f * scale);
        FrutigerAeroUI.DrawGolfBall(new Vector2(panel.xMax - 39f * scale, panel.y + 112f * scale), 30f * scale);

        headingStyle.fontSize = Mathf.RoundToInt(19f * scale);
        scoreStyle.fontSize = Mathf.RoundToInt(66f * scale);
        scoreShadowStyle.fontSize = scoreStyle.fontSize;
        strokeLabelStyle.fontSize = Mathf.RoundToInt(15f * scale);

        GUI.Label(new Rect(panel.x + 12f * scale, panel.y + 24f * scale,
            panel.width - 24f * scale, 29f * scale), "HOLE IN", headingStyle);
        Rect scoreRect = new Rect(panel.x + 12f * scale, panel.y + 51f * scale,
            panel.width - 24f * scale, 74f * scale);
        GUI.Label(new Rect(scoreRect.x, scoreRect.y + 3f * scale, scoreRect.width, scoreRect.height),
            completedStrokeCount.ToString(), scoreShadowStyle);
        GUI.Label(scoreRect, completedStrokeCount.ToString(), scoreStyle);

        string strokeWord = completedStrokeCount == 1 ? "STROKE" : "STROKES";
        GUI.Label(new Rect(panel.x + 12f * scale, panel.y + 129f * scale,
            panel.width - 24f * scale, 24f * scale), strokeWord, strokeLabelStyle);

        GUI.matrix = previousMatrix;
    }

    private void DrawWrongWayBanner()
    {
        float scale = Mathf.Clamp(Screen.width / 1200f, 0.72f, 1.25f);
        float width = 286f * scale;
        float height = 54f * scale;
        Rect banner = new Rect((Screen.width - width) * 0.5f, 38f * scale, width, height);
        float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f));

        FrutigerAeroUI.DrawGlassPanel(banner,
            Color.Lerp(FrutigerAeroUI.Coral, Color.white, pulse * 0.15f),
            new Color(1f, 0.88f, 0.88f, 0.95f), 0.18f);
        FrutigerAeroUI.DrawCircle(new Rect(banner.x + 15f * scale, banner.center.y - 8f * scale,
            16f * scale, 16f * scale), new Color(1f, 0.34f, 0.36f, pulse));
        wrongWayStyle.fontSize = Mathf.RoundToInt(21f * scale);
        GUI.Label(new Rect(banner.x + 38f * scale, banner.y, banner.width - 52f * scale, banner.height),
            "WRONG WAY", wrongWayStyle);
    }

    private void EnsureStyles()
    {
        if (headingStyle != null)
            return;

        headingStyle = FrutigerAeroUI.CreateStyle(19, FrutigerAeroUI.Blue);
        scoreStyle = FrutigerAeroUI.CreateStyle(66, new Color(0.12f, 0.54f, 0.33f, 1f));
        scoreShadowStyle = FrutigerAeroUI.CreateStyle(66, new Color(0.08f, 0.31f, 0.36f, 0.18f));
        strokeLabelStyle = FrutigerAeroUI.CreateStyle(15, new Color(0.13f, 0.42f, 0.52f, 1f));
        wrongWayStyle = FrutigerAeroUI.CreateStyle(21, new Color(0.58f, 0.08f, 0.13f, 1f));
    }

    private void UpdateWrongWayArrow()
    {
        if (wrongWayBall == null || correctHoleTarget == null)
            return;

        CreateWrongWayArrowIfNeeded();
        if (wrongArrowRoot == null)
            return;

        wrongArrowRoot.SetActive(true);
        wrongArrowRoot.transform.position = wrongWayBall.transform.position + Vector3.up * wrongWayArrowHeight;
        wrongArrowRoot.transform.localScale = Vector3.one * wrongWayArrowSize;

        Vector3 worldDirection = correctHoleTarget.position - wrongWayBall.transform.position;
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.001f)
            worldDirection = Vector3.forward;
        wrongArrowRoot.transform.rotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);

        bool visible = pauseMenu == null || !pauseMenu.IsPaused;
        visible &= Mathf.Sin(Time.unscaledTime * 11f) > -0.2f;
        if (wrongArrowRenderer != null)
            wrongArrowRenderer.enabled = visible;
        if (wrongArrowFaceRenderer != null)
            wrongArrowFaceRenderer.enabled = visible;
    }

    private void CreateWrongWayArrowIfNeeded()
    {
        if (wrongArrowRoot != null)
            return;

        wrongArrowMesh = CreateArrowMesh();
        wrongArrowOutlineMaterial = CreateUnlitMaterial(new Color(0.28f, 0.015f, 0.045f, 1f), "Wrong Way Arrow Outline");
        wrongArrowFaceMaterial = CreateUnlitMaterial(new Color(1f, 0.12f, 0.19f, 1f), "Wrong Way Arrow Red");

        wrongArrowRoot = new GameObject("Wrong Hole 3D Direction Arrow");
        MeshFilter outlineFilter = wrongArrowRoot.AddComponent<MeshFilter>();
        outlineFilter.sharedMesh = wrongArrowMesh;
        wrongArrowRenderer = wrongArrowRoot.AddComponent<MeshRenderer>();
        wrongArrowRenderer.sharedMaterial = wrongArrowOutlineMaterial;

        GameObject face = new GameObject("Arrow Face");
        face.transform.SetParent(wrongArrowRoot.transform, false);
        face.transform.localPosition = new Vector3(0f, 0.077f, 0f);
        face.transform.localScale = new Vector3(0.76f, 0.1f, 0.84f);
        MeshFilter faceFilter = face.AddComponent<MeshFilter>();
        faceFilter.sharedMesh = wrongArrowMesh;
        wrongArrowFaceRenderer = face.AddComponent<MeshRenderer>();
        wrongArrowFaceRenderer.sharedMaterial = wrongArrowFaceMaterial;
        wrongArrowRoot.SetActive(false);
    }

    private static Mesh CreateArrowMesh()
    {
        // The arrow lies in the XZ plane and is extruded vertically, with its
        // point along local +Z so it can aim through the course in world space.
        Vector2[] outline =
        {
            new Vector2(-0.12f, -0.43f),
            new Vector2(-0.12f, 0.08f),
            new Vector2(-0.32f, 0.08f),
            new Vector2(0f, 0.48f),
            new Vector2(0.32f, 0.08f),
            new Vector2(0.12f, 0.08f),
            new Vector2(0.12f, -0.43f)
        };

        const float halfThickness = 0.07f;
        int pointCount = outline.Length;
        int topCenter = pointCount * 2;
        int bottomCenter = topCenter + 1;
        Vector3[] vertices = new Vector3[pointCount * 2 + 2];
        for (int i = 0; i < pointCount; i++)
        {
            vertices[i] = new Vector3(outline[i].x, halfThickness, outline[i].y);
            vertices[i + pointCount] = new Vector3(outline[i].x, -halfThickness, outline[i].y);
        }
        vertices[topCenter] = new Vector3(0f, halfThickness, 0.015f);
        vertices[bottomCenter] = new Vector3(0f, -halfThickness, 0.015f);

        int[] triangles = new int[pointCount * 12];
        int triangleIndex = 0;
        for (int i = 0; i < pointCount; i++)
        {
            int next = (i + 1) % pointCount;

            // Top and bottom caps.
            triangles[triangleIndex++] = topCenter;
            triangles[triangleIndex++] = i;
            triangles[triangleIndex++] = next;
            triangles[triangleIndex++] = bottomCenter;
            triangles[triangleIndex++] = next + pointCount;
            triangles[triangleIndex++] = i + pointCount;

            // Extruded outer wall.
            triangles[triangleIndex++] = i;
            triangles[triangleIndex++] = i + pointCount;
            triangles[triangleIndex++] = next + pointCount;
            triangles[triangleIndex++] = i;
            triangles[triangleIndex++] = next + pointCount;
            triangles[triangleIndex++] = next;
        }

        Mesh mesh = new Mesh { name = "Wrong Way Arrow Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateUnlitMaterial(Color color, string materialName)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogWarning("Could not find an unlit shader for the wrong-hole arrow.");
            return null;
        }

        Material material = new Material(shader) { name = materialName };
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_CullMode"))
            material.SetFloat("_CullMode", 0f);
        return material;
    }

    private void DrawRoundedRect(Rect rect, Color color)
    {
        if (roundedTexture == null)
            return;

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, roundedTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;
    }

    private void DrawCircle(Rect rect, Color color)
    {
        if (circleTexture == null)
            return;

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, circleTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;
    }

    private void DrawGolfBall(Vector2 center, float size)
    {
        Rect ball = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
        DrawCircle(new Rect(ball.x + 1f, ball.y + 3f, size, size), new Color(0f, 0f, 0f, 0.28f));
        DrawCircle(ball, new Color(1f, 0.98f, 0.87f, 1f));
        float dimple = size * 0.1f;
        DrawCircle(new Rect(center.x - size * 0.2f, center.y - size * 0.15f, dimple, dimple),
            new Color(0.72f, 0.69f, 0.6f, 0.85f));
        DrawCircle(new Rect(center.x + size * 0.08f, center.y - size * 0.22f, dimple, dimple),
            new Color(0.72f, 0.69f, 0.6f, 0.85f));
        DrawCircle(new Rect(center.x + size * 0.12f, center.y + size * 0.09f, dimple, dimple),
            new Color(0.72f, 0.69f, 0.6f, 0.85f));
    }

    private static Texture2D CreateRoundedTexture(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Hole Result Rounded Panel",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        const int samples = 4;
        float radius = height * 0.22f;
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        float px = x + (sx + 0.5f) / samples;
                        float py = y + (sy + 0.5f) / samples;
                        float dx = Mathf.Abs(px - width * 0.5f) - (width * 0.5f - radius);
                        float dy = Mathf.Abs(py - height * 0.5f) - (height * 0.5f - radius);
                        float outsideX = Mathf.Max(0f, dx);
                        float outsideY = Mathf.Max(0f, dy);
                        float distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                            + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                        if (distance <= 0f)
                            covered++;
                    }
                }

                pixels[y * width + x] = new Color(1f, 1f, 1f, covered / (float)(samples * samples));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Hole Result Golf Ball Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        const int samples = 4;
        float radius = size * 0.5f;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        float px = x + (sx + 0.5f) / samples - radius;
                        float py = y + (sy + 0.5f) / samples - radius;
                        if (px * px + py * py <= radius * radius)
                            covered++;
                    }
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, covered / (float)(samples * samples));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void OnDestroy()
    {
        if (roundedTexture != null)
            Destroy(roundedTexture);
        if (circleTexture != null)
            Destroy(circleTexture);
        if (wrongArrowRoot != null)
            Destroy(wrongArrowRoot);
        if (wrongArrowMesh != null)
            Destroy(wrongArrowMesh);
        if (wrongArrowOutlineMaterial != null)
            Destroy(wrongArrowOutlineMaterial);
        if (wrongArrowFaceMaterial != null)
            Destroy(wrongArrowFaceMaterial);
    }
}
