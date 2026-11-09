using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GolfNineHoleCourseBuilder
{
    const string SourceScene = "Assets/Scenes/GolfScene.unity";
    const string OutputScene = "Assets/Scenes/NineHoleCourse.unity";
    const string Folder = "Assets/Courses/GolfIsHardNineHole";
    const string TerrainAsset = Folder + "/NineHoleTerrainData.asset";
    const string LayerFolder = "Assets/FlipGameDev/Terrain&GrassPack/Terrain_Settings/Terrain_Layers/";
    const float Size = 120f, Height = 12f;

    sealed class Stage
    {
        public Vector2 tee, cup;
        public Stage(float tx, float tz, float cx, float cz)
        { tee = new Vector2(tx, tz); cup = new Vector2(cx, cz); }
    }

    static readonly Stage[] Stages =
    {
        new Stage(8,14,34,14), new Stage(72,14,46,14), new Stage(86,14,112,14),
        new Stage(112,52,86,52), new Stage(72,52,46,52), new Stage(34,52,8,52),
        new Stage(8,90,34,90), new Stage(48,90,74,90), new Stage(86,90,112,90)
    };

    [MenuItem("Tools/Golf is Hard/Build Nine Hole Course")]
    public static void Build()
    {
        EnsureFolder("Assets/Courses");
        EnsureFolder(Folder);
        EnsureFolder(Folder + "/Materials");

        Scene source = SceneManager.GetSceneByPath(SourceScene);
        bool openedSource = !source.IsValid() || !source.isLoaded;
        if (openedSource) source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
        if (!source.IsValid() || !source.isLoaded)
        {
            Debug.LogError("Could not load GolfScene to create the nine-hole course.");
            return;
        }

        Scene course = SceneManager.GetSceneByPath(OutputScene);
        if (!course.IsValid() || !course.isLoaded)
            course = SceneManager.GetSceneByName("NineHoleCourse");
        if (!course.IsValid() || !course.isLoaded)
        {
            // Save a copy first, leaving GolfScene itself untouched.
            if (!EditorSceneManager.SaveScene(source, OutputScene, true))
            {
                Debug.LogError("Could not create Assets/Scenes/NineHoleCourse.unity.");
                return;
            }
            AssetDatabase.ImportAsset(OutputScene, ImportAssetOptions.ForceUpdate);
            course = EditorSceneManager.OpenScene(OutputScene, OpenSceneMode.Additive);
        }
        if (!course.IsValid() || !course.isLoaded)
        {
            Debug.LogError("Could not open the new course scene.");
            return;
        }
        EditorSceneManager.SetActiveScene(course);
        RemoveOldTestObjects(course);

        GolfCourseManager manager = Find<GolfCourseManager>(course);
        BallPlayerController ball = Find<BallPlayerController>(course);
        ThirdPersonCameraController cameraController = Find<ThirdPersonCameraController>(course);
        Camera mainCamera = FindMainCamera(course);
        if (manager == null || ball == null || cameraController == null || mainCamera == null)
        {
            Debug.LogError("The source scene is missing its golf manager, ball, or camera.");
            return;
        }

        GameObject root = new GameObject("Nine Hole Course");
        SceneManager.MoveGameObjectToScene(root, course);

        TerrainData data = BuildTerrainData();
        if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAsset) != null)
            AssetDatabase.DeleteAsset(TerrainAsset);
        AssetDatabase.CreateAsset(data, TerrainAsset);
        Terrain terrain = Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>();
        terrain.gameObject.name = "Course Terrain";
        terrain.transform.SetParent(root.transform, false);
        terrain.transform.position = Vector3.zero;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 5f;
        terrain.basemapDistance = 500f;
        terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        CarveCups(data);
        PaintTerrain(data);

        Material mint = MakeMaterial(Folder + "/Materials/TeeMarker_Mint.mat", new Color(.24f,.95f,.69f));
        Material coral = MakeMaterial(Folder + "/Materials/TeeMarker_Coral.mat", new Color(1f,.31f,.27f));
        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/GolfHole.obj");
        if (modelPrefab == null)
        {
            Debug.LogError("Could not find Assets/Models/GolfHole.obj.");
            return;
        }
        GameObject effectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Particles/CollectableGetParticle.prefab");
        ParticleSystem particles = effectPrefab != null ? effectPrefab.GetComponentInChildren<ParticleSystem>(true) : null;
        AudioClip sound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/Sfx/Collect1.wav");

        HoleTrigger[] holes = new HoleTrigger[Stages.Length];
        Transform[] tees = new Transform[Stages.Length];
        for (int i = 0; i < Stages.Length; i++)
        {
            Stage stage = Stages[i];
            Vector3 direction = new Vector3(stage.cup.x - stage.tee.x, 0, stage.cup.y - stage.tee.y).normalized;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            Quaternion facing = Quaternion.LookRotation(direction);
            Vector3 teePos = new Vector3(stage.tee.x, SurfaceHeight(stage.tee.x, stage.tee.y) + .3275f, stage.tee.y);
            Vector3 cupPos = new Vector3(stage.cup.x, SurfaceHeight(stage.cup.x, stage.cup.y), stage.cup.y);

            GameObject tee = new GameObject((i + 1).ToString("00") + " Tee");
            tee.transform.SetParent(root.transform, false);
            tee.transform.SetPositionAndRotation(teePos, facing);
            tees[i] = tee.transform;
            MakeTeeMarker(tee.transform, -.58f, mint);
            MakeTeeMarker(tee.transform, .58f, coral);

            GameObject hole = new GameObject((i + 1).ToString("00") + " Hole");
            hole.transform.SetParent(root.transform, false);
            hole.transform.position = cupPos;
            SphereCollider triggerCollider = hole.AddComponent<SphereCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.center = new Vector3(0, -.12f, 0);
            triggerCollider.radius = .76f;
            HoleTrigger trigger = hole.AddComponent<HoleTrigger>();

            GameObject cup = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, course);
            cup.name = "Cup, Liner and Flag";
            cup.transform.SetParent(hole.transform, false);
            EnsureModelColliders(cup);
            holes[i] = trigger;

            SerializedObject triggerData = new SerializedObject(trigger);
            SerializedProperty effect = triggerData.FindProperty("holeParticlePrefab");
            SerializedProperty sfx = triggerData.FindProperty("holeSound");
            if (effect != null) effect.objectReferenceValue = particles;
            if (sfx != null) sfx.objectReferenceValue = sound;
            triggerData.ApplyModifiedPropertiesWithoutUndo();
        }

        Configure(manager, ball, cameraController, mainCamera, holes, tees);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(course);
        EditorSceneManager.SaveScene(course);
        EditorSceneManager.SetActiveScene(course);
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.pivot = new Vector3(60, 1.5f, 60);
            SceneView.lastActiveSceneView.size = 155;
            SceneView.lastActiveSceneView.rotation = Quaternion.Euler(58, 0, 0);
            SceneView.lastActiveSceneView.Repaint();
        }
        if (openedSource) EditorSceneManager.CloseScene(source, true);
        Debug.Log("Built NineHoleCourse.unity with terrain, 9 cups, ordered hole triggers, tees, and Space progression.");
    }

    static TerrainData BuildTerrainData()
    {
        const string textureFolder = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Grass/";
        Texture2D roughTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureFolder + "A_RoughGrass/Grass_darked_right.png");
        Texture2D fairwayTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureFolder + "A_FarewayGrass/Grass_normal_right.png");
        Texture2D greenTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureFolder + "A_PuttingGrass/Grass_lighted_right.png");
        TerrainLayer sourceSand = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerFolder + "Sand.terrainlayer");
        if (sourceSand == null || sourceSand.diffuseTexture == null)
            throw new System.InvalidOperationException("Could not load the existing bunker sand texture.");

        TerrainLayer rough = CreateCourseTerrainLayer(Folder + "/GolfRough.terrainlayer", "Golf Rough", roughTexture, 8f);
        TerrainLayer fairway = CreateCourseTerrainLayer(Folder + "/GolfFairway.terrainlayer", "Golf Fairway", fairwayTexture, 8f);
        TerrainLayer green = CreateCourseTerrainLayer(Folder + "/GolfPuttingGreen.terrainlayer", "Golf Putting Green", greenTexture, 5f);
        TerrainLayer sand = CreateCourseTerrainLayer(Folder + "/GolfBunkerSand.terrainlayer", "Golf Bunker Sand", sourceSand.diffuseTexture, 5f);
        if (rough == null || fairway == null || green == null || sand == null)
            throw new System.InvalidOperationException("Could not load the course Terrain Layers.");

        TerrainData data = new TerrainData();
        data.heightmapResolution = 513;
        data.alphamapResolution = 512;
        data.baseMapResolution = 1024;
        data.size = new Vector3(Size, Height, Size);
        data.terrainLayers = new[] { rough, fairway, green, sand };

        int resolution = data.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        for (int z = 0; z < resolution; z++)
        {
            float worldZ = (float)z / (resolution - 1) * Size;
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (float)x / (resolution - 1) * Size;
                heights[z, x] = EvaluateHeight(worldX, worldZ) / Height;
            }
        }
        data.SetHeights(0, 0, heights);
        return data;
    }

    static TerrainLayer CreateCourseTerrainLayer(string path, string layerName, Texture2D texture, float tileSize)
    {
        if (texture == null)
            throw new System.InvalidOperationException("Could not load texture for " + layerName + ".");

        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            layer.name = layerName;
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = texture;
        layer.normalMapTexture = null;
        layer.tileSize = new Vector2(tileSize, tileSize);
        layer.normalScale = 1f;
        EditorUtility.SetDirty(layer);
        return layer;
    }
    static void CarveCups(TerrainData data)
    {
        int resolution = data.holesResolution;
        bool[,] holes = new bool[resolution, resolution];
        for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
                holes[z, x] = true;

        float cell = Size / resolution;
        int radius = Mathf.CeilToInt(1.12f / cell);
        for (int i = 0; i < Stages.Length; i++)
        {
            int cx = Mathf.RoundToInt(Stages[i].cup.x / Size * resolution);
            int cz = Mathf.RoundToInt(Stages[i].cup.y / Size * resolution);
            for (int z = Mathf.Max(0, cz - radius); z <= Mathf.Min(resolution - 1, cz + radius); z++)
            for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(resolution - 1, cx + radius); x++)
            {
                float dx = (x - cx) * cell, dz = (z - cz) * cell;
                if (dx * dx + dz * dz <= 1.12f * 1.12f)
                    holes[z, x] = false;
            }
        }
        data.SetHoles(0, 0, holes);
    }

    static void PaintTerrain(TerrainData data)
    {
        int resolution = data.alphamapResolution;
        float[,,] alpha = new float[resolution, resolution, 4];
        Vector2[] bunkers = new Vector2[Stages.Length];
        for (int i = 0; i < Stages.Length; i++)
        {
            Vector2 dir = (Stages[i].cup - Stages[i].tee).normalized;
            Vector2 side = new Vector2(-dir.y, dir.x) * (i % 2 == 0 ? 1f : -1f);
            bunkers[i] = Stages[i].cup - dir * 2f + side * 9f;
        }

        for (int z = 0; z < resolution; z++)
        {
            float worldZ = (float)z / (resolution - 1) * Size;
            for (int x = 0; x < resolution; x++)
            {
                Vector2 point = new Vector2((float)x / (resolution - 1) * Size, worldZ);
                float fw = 0, green = 0, sand = 0;
                for (int i = 0; i < Stages.Length; i++)
                {
                    float laneDist = DistanceToSegment(point, Stages[i].tee, Stages[i].cup);
                    fw = Mathf.Max(fw, 1f - Smooth01(5.3f, 7.4f, laneDist));
                    green = Mathf.Max(green, 1f - Smooth01(5.8f, 7.8f, Vector2.Distance(point, Stages[i].cup)));
                    Vector2 offset = point - bunkers[i];
                    float ellipse = Mathf.Sqrt(offset.x * offset.x / (4.5f * 4.5f) + offset.y * offset.y / (3.3f * 3.3f));
                    sand = Mathf.Max(sand, 1f - Smooth01(.72f, 1.05f, ellipse));
                }

                sand = Mathf.Clamp01(sand);
                green = Mathf.Clamp01(green) * (1f - sand);
                fw = Mathf.Clamp01(fw) * (1f - sand) * (1f - green);
                float rough = Mathf.Max(0f, 1f - sand - green - fw);
                float total = rough + fw + green + sand;
                alpha[z, x, 0] = rough / total;
                alpha[z, x, 1] = fw / total;
                alpha[z, x, 2] = green / total;
                alpha[z, x, 3] = sand / total;
            }
        }
        data.SetAlphamaps(0, 0, alpha);
    }

    static float EvaluateHeight(float x, float z)
    {
        float roll = Mathf.Sin(x * .055f + z * .026f) * .16f
            + Mathf.Cos(z * .073f - x * .018f) * .11f
            + Mathf.Sin(x * .12f) * Mathf.Cos(z * .09f) * .045f;
        return 1.18f + roll;
    }

    static float SurfaceHeight(float x, float z) { return EvaluateHeight(x, z); }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float sqr = delta.sqrMagnitude;
        if (sqr < .0001f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, delta) / sqr);
        return Vector2.Distance(p, a + delta * t);
    }

    static float Smooth01(float a, float b, float value)
    {
        float t = Mathf.Clamp01((value - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    static void Configure(GolfCourseManager manager, BallPlayerController ball,
        ThirdPersonCameraController cameraController, Camera mainCamera,
        HoleTrigger[] holes, Transform[] tees)
    {
        SerializedObject so = new SerializedObject(manager);
        SerializedProperty stages = so.FindProperty("holesInOrder");
        stages.arraySize = holes.Length;
        for (int i = 0; i < holes.Length; i++)
        {
            SerializedProperty stage = stages.GetArrayElementAtIndex(i);
            stage.FindPropertyRelative("hole").objectReferenceValue = holes[i];
            stage.FindPropertyRelative("teePoint").objectReferenceValue = tees[i];
        }
        so.FindProperty("ball").objectReferenceValue = ball;
        so.FindProperty("cameraController").objectReferenceValue = cameraController;
        so.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject cameraData = new SerializedObject(cameraController);
        cameraData.FindProperty("target").objectReferenceValue = ball.transform;
        cameraData.ApplyModifiedPropertiesWithoutUndo();
        cameraController.transform.rotation = Quaternion.Euler(18f, 90f, 0f);

        SerializedObject ballData = new SerializedObject(ball);
        SerializedProperty aimCamera = ballData.FindProperty("aimCamera");
        if (aimCamera != null) aimCamera.objectReferenceValue = mainCamera;
        ballData.ApplyModifiedPropertiesWithoutUndo();

        PauseMenu pause = Find<PauseMenu>(manager.gameObject.scene);
        if (pause != null)
        {
            SerializedObject pauseData = new SerializedObject(pause);
            SerializedProperty cameraField = pauseData.FindProperty("cameraController");
            if (cameraField != null) cameraField.objectReferenceValue = cameraController;
            pauseData.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void MakeTeeMarker(Transform parent, float localX, Material material)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Tee Marker";
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = new Vector3(localX, -.2925f, 0f);
        marker.transform.localScale = new Vector3(.16f, .035f, .16f);
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    static void EnsureModelColliders(GameObject root)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter f = filters[i];
            if (f.sharedMesh == null || f.GetComponent<Collider>() != null) continue;
            MeshCollider collider = f.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = f.sharedMesh;
        }
    }

    static void RemoveOldTestObjects(Scene scene)
    {
        string[] names = { "Hole", "Hole (1)", "GolfHole", "Plane", "Terrain", "Cube (1)", "Nine Hole Course" };
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            for (int n = 0; n < names.Length; n++)
                if (roots[i].name == names[n])
                {
                    Object.DestroyImmediate(roots[i]);
                    break;
                }
    }

    static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
            if (scenes[i].path == OutputScene) return;

        int index = 0;
        for (int i = 0; i < scenes.Count; i++)
            if (scenes[i].path.EndsWith("TitleScreen.unity"))
            {
                index = i + 1;
                break;
            }
        scenes.Insert(index, new EditorBuildSettingsScene(OutputScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static Material MakeMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .18f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static T Find<T>(Scene scene) where T : Component
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            T found = roots[i].GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    static Camera FindMainCamera(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
            for (int c = 0; c < cameras.Length; c++)
                if (cameras[c].CompareTag("MainCamera")) return cameras[c];
        }
        return null;
    }
}

