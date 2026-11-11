using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static AeroKit;

/// <summary>
/// Builds "Aero Isles": a nine-hole Frutiger Aero / Wii Sports style course on a ring of islands
/// around a lighthouse hub. Every cup gets a real hole cut through the ground beneath it.
/// Menu: Tools > Golf is Hard > Build Frutiger Aero Nine Hole Course
/// </summary>
public static class FrutigerAeroNineHoleBuilder
{
    private const string RootFolder = "Assets/FrutigerGolfCourse/NineHole";
    private const string ScenePath = "Assets/FrutigerGolfCourse/FrutigerAeroNineHole.unity";
    private const string CupPrefab = "Assets/Models/FrutigerAeroGolfHole/FrutigerAeroGolfHole.prefab";
    private const string TeePrefab = "Assets/Models/ClassicWhiteGolfTee/ClassicWhiteGolfTee.prefab";
    private const float RingR = 430f;
    public const float BallScale = 0.3f; // the ball is always this size in every scene

    private class HoleInfo { public int number, par; public string title; public Transform root, tee, cup, view; }
    private static List<HoleInfo> holes;

    // structures whose underside the terrain must stay below (recorded for the overlap check)
    private class Tracked { public Transform root; public string name; public List<Vector3> path; public float halfWidth, below; }
    private static readonly List<Tracked> tracked = new List<Tracked>();
    private static readonly List<List<Vector3>> waterfallPaths = new List<List<Vector3>>();
    private static Transform currentRoot;

    /// <summary>Carve the island under a structure path and remember it for validation.</summary>
    private static void Carve(IslandDef d, Transform root, string name, List<Vector3> path, float halfWidth, float below, float blend)
    {
        d.clamps.Add(new Corridor(path, halfWidth, below, blend));
        // validation: no ground may rise into the walking surface (deck top / pipe floor / rainbow bands).
        // The carve itself keeps an extra margin below that, so shells can sit embedded at the very ends.
        tracked.Add(new Tracked { root = root, name = name, path = path, halfWidth = halfWidth, below = 0.02f });
    }

    /// <summary>
    /// Checks the open scene for things cutting through each other: ground poking up into bridges, pipes,
    /// lanes, the rainbow or waterfalls, and trees / bubbles overlapping any structure.
    /// </summary>
    public static string ValidateOverlaps()
    {
        Physics.SyncTransforms();
        var report = new System.Text.StringBuilder();
        int groundHits = 0, decorHits = 0;

        float GroundY(Vector3 world)
        {
            float best = float.MinValue;
            foreach (var h in Physics.RaycastAll(world + Vector3.up * 60f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.name.EndsWith(" Ground") || h.collider.name == "Cup Turf Collar") best = Mathf.Max(best, h.point.y);
            return best;
        }

        foreach (var t in tracked)
        {
            if (t.root == null) continue;
            int bad = 0; float worst = 0f;
            for (int i = 1; i < t.path.Count - 1; i++)
            {
                var tan = t.path[i + 1] - t.path[i - 1]; tan.y = 0f; tan.Normalize();
                var side = new Vector3(tan.z, 0f, -tan.x);
                float edge = t.halfWidth - 0.3f; // carve zones carry a 0.3 m margin beyond the structure itself
                for (float o = -edge; o <= edge + 0.01f; o += 0.5f)
                {
                    var local = t.path[i] + side * o;
                    var w = t.root.TransformPoint(local);
                    float gy = GroundY(w);
                    float over = gy - (w.y - t.below) - 0.03f;
                    if (over > 0f) { bad++; worst = Mathf.Max(worst, over); }
                }
            }
            if (bad > 0) { groundHits += bad; report.AppendLine($"  ground into {t.name}: {bad} samples, worst {worst:F2} m"); }
        }

        // trees and bubbles vs. any structure collider (and bubbles vs. ground)
        var structures = new HashSet<string> { "Deck", "Panel L", "Panel R", "Pipe", "Lip L", "Lip R", "Rail Lane", "Ramp", "Ramp Back",
            "Tower", "Glass Shell", "Glass Inner Collider", "Funnel Wall L", "Funnel Wall R", "Body", "Spinner Hub", "Paddle", "Pillar",
            "Pipe Pillar", "Basin", "Column", "Cap", "Rim", "Green", "Fairway", "Summit Green", "Outer Wall", "Bounce Pad", "Post", "Board" };
        for (int i = 0; i < 7; i++) structures.Add("Band " + i);
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            bool tree = r.name == "Canopy" || r.name == "CanopyTop";
            bool bubble = r.name.EndsWith("Bubble");
            if (!tree && !bubble) continue;
            var b = r.bounds;
            float rad = Mathf.Min(b.extents.x, b.extents.z) * 0.92f;
            foreach (var c in Physics.OverlapSphere(b.center, rad, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(r.transform.parent)) continue;
                bool hit = structures.Contains(c.name) || (bubble && (c.name.EndsWith(" Ground") || c.name == "Trunk" || c.name == "Canopy"));
                if (!hit) continue;
                decorHits++;
                if (decorHits <= 12) report.AppendLine($"  {r.name} at {b.center:F0} overlaps {c.name} ({c.transform.root.name})");
                break;
            }
        }
        string head = $"Overlap check: {groundHits} ground samples poking into structures, {decorHits} decor overlaps.";
        Debug.Log(head + "\n" + report);
        return head + "\n" + report;
    }
    private static Scene scene;
    private static System.Random rng;

    // materials
    private static Material mWater, mSeabed, mPearl, mAqua, mGlass, mPanel, mLime, mEmerald, mPink, mYellow, mSky, mViolet,
        mOrange, mCoral, mTrunk, mCloud, mBubble, mFloatGrass, mFloatGreen, mFloatRim, mFloatRock, mCrystal, mWaterfall,
        mChrome, mFoam, mLantern, mSprayMat;
    private static Material[] mFlowers, mRainbow, mRainbowSky, mCloudSprites;
    private static PhysicsMaterial pBouncy, pSuperBouncy, pWall;

    [MenuItem("Tools/Golf is Hard/Build Frutiger Aero Nine Hole Course")]
    public static void Build()
    {
        // building replaces the open scene, so never throw away someone's unsaved work
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var open = SceneManager.GetSceneAt(i);
            if (open.isDirty && open.path != ScenePath)
            {
                Debug.LogError($"Not building: '{open.name}' has unsaved changes. Save it first.");
                return;
            }
        }
        Begin(RootFolder);
        rng = new System.Random(2026);
        holes = new List<HoleInfo>();
        torusCache.Clear(); tracked.Clear(); waterfallPaths.Clear(); keepouts.Clear(); existingSun = null;
        MakeSharedTextures();
        MakeMaterials();

        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var env = new GameObject("Environment").transform;
        var course = new GameObject("Aero Isles Course").transform;
        BuildEnvironment(env);
        BuildHub(course);

        Hole1(course); Hole2(course); Hole3(course); Hole4(course); Hole5(course);
        Hole6(course); Hole7(course); Hole8(course); Hole9(course);

        // gameplay: ball, follow camera, course manager, pause menu, music (from the saved rig prefab)
        var v = holes[0].view;
        if (!AttachGameplayRig())
        {
            var cam = MakeCamera();
            cam.transform.SetPositionAndRotation(v.position, v.rotation);
        }
        AddHazards(env);
        TagSurfaces();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        var sb = new System.Text.StringBuilder("Aero Isles built: ");
        foreach (var h in holes) sb.Append($"[{h.number} {h.title} par {h.par}] ");
        Debug.Log(sb.ToString());
    }

    // ===================================================================== tutorial scene restyle
    /// <summary>
    /// Restyles the open tutorial scene (the one with "Tutorial Holes") to match Aero Isles. Every floor, wall,
    /// cup and tee stays exactly where it is, so the tutorial plays the same. Safe to run again: it replaces
    /// its own previous output. The scene is marked dirty but not saved.
    /// </summary>
    [MenuItem("Tools/Golf is Hard/Restyle Open Tutorial Scene (Aero Isles)")]
    public static string RestyleTutorialScene()
    {
        var tut = GameObject.Find("Tutorial Holes");
        if (tut == null) return "No 'Tutorial Holes' object in the open scene.";
        scene = SceneManager.GetActiveScene();
        Begin(RootFolder);
        rng = new System.Random(77);
        holes = new List<HoleInfo>();
        torusCache.Clear(); tracked.Clear(); waterfallPaths.Clear(); keepouts.Clear();
        if (!LoadSharedTextures()) MakeSharedTextures();
        MakeMaterials();

        // clear anything a previous run made
        foreach (var n in new[] { "Aero Environment", "Aero Tutorial Isle" }) { var old = GameObject.Find(n); if (old != null) Object.DestroyImmediate(old); }
        var oldRails = new List<GameObject>();
        foreach (var t in tut.GetComponentsInChildren<Transform>(true)) if (t.name == "Aero Rail") oldRails.Add(t.gameObject);
        foreach (var g in oldRails) Object.DestroyImmediate(g);

        // sky, sea, clouds, haze, post-processing; the scene's own directional light becomes the sun
        existingSun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.gameObject.scene == scene) { existingSun = l; break; }
        var env = new GameObject("Aero Environment").transform;
        BuildEnvironment(env);
        existingSun = null;
        AddHazards(env);

        var cam = Camera.main;
        if (cam != null)
        {
            cam.farClipPlane = 3000f;
            var ucd = cam.GetComponent<UniversalAdditionalCameraData>();
            if (ucd == null) ucd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            ucd.renderPostProcessing = true;
        }
        var ballCtl = Object.FindFirstObjectByType<BallPlayerController>();
        if (ballCtl != null && ballCtl.GetComponent<AeroBallSafety>() == null) ballCtl.gameObject.AddComponent<AeroBallSafety>();
        if (ballCtl != null) ballCtl.transform.localScale = Vector3.one * BallScale;

        // restyle the lanes: striped fairway floors, glossy pearl walls with rounded aqua bumper rails
        var laneTex = LaneTexture();
        var laneMat = Lit("Tutorial_Lane", Color.white, 0.4f);
        laneMat.SetTexture("_BaseMap", laneTex);
        AddGrassDetail(laneMat, new Vector2(20f, 5f));
        var platMat = Lit("Tutorial_Platform", Color.white, 0.4f);
        platMat.SetTexture("_BaseMap", laneTex);
        platMat.SetTextureScale("_BaseMap", new Vector2(0.3f, 1f));
        AddGrassDetail(platMat, new Vector2(6f, 5f));

        var isle = new GameObject("Aero Tutorial Isle").transform;
        var railGroup = Group("Lane Rails", isle).transform; // unscaled parent, so rails keep exact world sizes
        // low, flat island: grass down to a thin beach at the waterline
        var d = new IslandDef { name = "Tutorial_Isle", landH = 0.35f, roll = 0.6f, beachLevel = 0.1f, texPerUnit = 8f };
        var lanes = new List<(Transform hole, Bounds b, List<Vector3> path)>();
        Bounds all = new Bounds();
        bool first = true;
        foreach (Transform hole in tut.transform)
        {
            var body = hole.Find("CourseBody");
            if (body == null) continue;
            Bounds lb = new Bounds(); bool lf = true;
            foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true))
            {
                string mn = r.sharedMaterial != null ? r.sharedMaterial.name : "";
                bool floor = mn.StartsWith("Grass") || mn.StartsWith("Tutorial_");
                bool wall = mn == "WhiteMaterial" || mn == "Pearl";
                if (floor)
                    r.sharedMaterial = r.bounds.size.x < 20f ? platMat : laneMat;
                else if (wall)
                {
                    r.sharedMaterial = mPearl;
                    var b = r.bounds;
                    bool alongX = b.size.x >= b.size.z;
                    float len = alongX ? b.size.x : b.size.z;
                    // rounded bumper rail along the wall top: an even tube with ball ends (a stretched capsule would taper)
                    var top = new Vector3(b.center.x, b.max.y, b.center.z);
                    var axis = alongX ? Vector3.right : Vector3.forward;
                    float tubeLen = Mathf.Max(0.1f, len - 0.6f);
                    Prim(PrimitiveType.Cylinder, "Aero Rail", railGroup, top, new Vector3(0.6f, tubeLen * 0.5f, 0.6f), mAqua, false, true,
                        alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f));
                    Prim(PrimitiveType.Sphere, "Aero Rail End", railGroup, top + axis * (tubeLen * 0.5f), Vector3.one * 0.6f, mAqua, false);
                    Prim(PrimitiveType.Sphere, "Aero Rail End", railGroup, top - axis * (tubeLen * 0.5f), Vector3.one * 0.6f, mAqua, false);
                }
                if (!r.enabled) continue;
                if (lf) { lb = r.bounds; lf = false; } else lb.Encapsulate(r.bounds);
            }
            if (lf) continue;
            if (first) { all = lb; first = false; } else all.Encapsulate(lb);
            var path = new List<Vector3> { new Vector3(lb.min.x - 0.3f, 0.18f, lb.center.z), new Vector3(lb.center.x, 0.18f, lb.center.z), new Vector3(lb.max.x + 0.3f, 0.18f, lb.center.z) };
            lanes.Add((hole, lb, path));
        }
        if (first) return "No lane geometry found under Tutorial Holes/*/CourseBody.";

        // one island under both lanes; the ground is carved to sit just under each lane floor and dug out under low cups
        d.land.Add(new Blob(all.center.x, all.center.z, all.extents.x + 18f, all.extents.z + 18f));
        foreach (var ln in lanes)
        {
            Carve(d, isle, ln.hole.name + " lane", ln.path, ln.b.extents.z + 0.6f, 0.06f, 3f);
            KeepOutPath(ln.path, ln.b.extents.z + 1.0f, 1f, 9f);
            var cutter = ln.hole.GetComponentInChildren<GolfHoleAutoCutter>(true);
            if (cutter != null && cutter.transform.position.y < 1.5f)
            {
                var c = cutter.transform.position;
                Carve(d, isle, ln.hole.name + " cup", new List<Vector3> { c + Vector3.left * 0.6f, c + Vector3.right * 0.6f }, 1.5f, 1.3f, 0.6f);
            }
        }
        AeroIsland.Build(d, isle, mWater);

        // signs beside each tutorial tee
        string[] titles = { "Rolling Shot", "Angle Shot", "Practice", "Practice" };
        int idx = 0;
        foreach (var ln in lanes)
        {
            var tee = ln.hole.GetComponentInChildren<GolfTeeSpawnPoint>(true);
            if (tee == null) { idx++; continue; }
            var tp = tee.transform.position;
            float sx = tp.x + 1.5f, sz = ln.b.max.z + 3.2f;
            var info = new HoleInfo { number = idx + 1, title = titles[Mathf.Min(idx, titles.Length - 1)], par = 2, root = isle };
            var viewer = new Vector3(tp.x - 14f, 0f, tp.z);
            HoleSign(isle, info, new Vector3(sx, d.Height(sx, sz), sz), Yaw(new Vector3(sx, 0f, sz) - viewer), "TUTORIAL");
            KeepOutPath(new List<Vector3> { new Vector3(sx, 0f, sz) }, 2.4f, 2f, 5f);
            idx++;
        }

        Dress(d, isle, 46, 110);
        Bubbles(isle, new Vector3(all.center.x, 3f, all.center.z), new Vector3(all.extents.x + 14f, 12f, all.extents.z + 14f), 20);
        CloudPuff(isle, new Vector3(all.center.x - 40f, 55f, all.center.z + 120f), 1.4f);
        CloudPuff(isle, new Vector3(all.center.x + 60f, 70f, all.center.z - 110f), 1.2f);
        TagSurfaces();

        EditorSceneManager.MarkSceneDirty(scene);
        return $"Restyled {lanes.Count} tutorial lanes in {scene.name} (not saved).";
    }

    /// <summary>
    /// Gives every playable collider in the open scene a surface type for the ball (islands carry a baked map instead):
    /// tees, cups and greens, fairway-like pads and lanes, grassy rims as rough, everything else (glass, pearl, pipes) as hard.
    /// </summary>
    public static string TagSurfaces()
    {
        int tagged = 0;
        var counts = new Dictionary<AeroSurfaceType, int>();
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c.isTrigger || c.gameObject.scene != SceneManager.GetActiveScene()) continue;
            if (c.GetComponentInParent<AeroSurfaceMap>() != null || c.GetComponentInParent<BallPlayerController>() != null) continue;
            if (c.name == "Seabed") continue;
            AeroSurfaceType t;
            if (c.GetComponentInParent<GolfTeeSpawnPoint>() != null) t = AeroSurfaceType.Tee;
            else if (c.GetComponentInParent<GolfHoleAutoCutter>() != null) t = AeroSurfaceType.Green;
            else
            {
                var r = c.GetComponent<Renderer>();
                string m = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
                if (m == "Float_Green") t = AeroSurfaceType.Green;
                else if (m == "Float_Fairway" || m == "Gloss_Lime" || m.StartsWith("Tutorial_")) t = AeroSurfaceType.Fairway;
                else if (m == "Float_Rim") t = AeroSurfaceType.Rough;
                else if (m.StartsWith("Grass")) t = AeroSurfaceType.Fairway;
                else t = AeroSurfaceType.Hard;
            }
            var s = c.GetComponent<AeroSurface>();
            if (s == null) s = c.gameObject.AddComponent<AeroSurface>();
            s.type = t;
            tagged++;
            counts[t] = counts.TryGetValue(t, out int n) ? n + 1 : 1;
        }
        var sb = new System.Text.StringBuilder($"Tagged {tagged} colliders:");
        foreach (var kv in counts) sb.Append($" {kv.Key}={kv.Value}");
        return sb.ToString();
    }

    private static bool LoadSharedTextures()
    {
        Texture2D L(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + n + ".png");
        GrassDetail = L("Grass_Detail"); GrassDetailNormal = L("Grass_DetailNormal"); WaterNormal = L("Water_Ripples_N");
        Caustics = L("Seabed_Caustics"); CloudA = L("Cloud_A"); CloudB = L("Cloud_B"); SoftDot = L("Soft_Dot"); Streaks = L("Waterfall_Streaks");
        return GrassDetail && GrassDetailNormal && WaterNormal && Caustics && CloudA && CloudB && SoftDot && Streaks;
    }

    /// <summary>Mowing stripes down the length of a lane, with a slightly lighter fringe along the walls.</summary>
    private static Texture2D LaneTexture()
    {
        int w = 1024, h = 256;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        Color fair = new Color(0.34f, 0.82f, 0.27f), fringe = new Color(0.42f, 0.88f, 0.33f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float s = Smooth(0.3f, 0.7f, 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * 10f));
                Color c = fair * (0.93f + 0.14f * s);
                c = Color.Lerp(c, fringe, Smooth(0.09f, 0.0f, Mathf.Min(v, 1f - v)) * 0.7f);
                c.a = 1f;
                px[y * w + x] = c;
            }
        t.SetPixels(px); t.Apply(false);
        return SaveTexture(t, "Tutorial_Lane", false, true, false, TextureWrapMode.Repeat);
    }

    // ===================================================================== gameplay wiring
    private const string RigPrefab = "Assets/FrutigerGolfCourse/AeroGameplayRig.prefab";
    private const string SourceGameplayScene = "Assets/Scenes/NineHoleCourse.unity";
    private static readonly string[] RigObjects = { "Main Camera", "PauseController", "GolfCourseManagerObject", "Ball", "MusicManager" };

    /// <summary>
    /// One-time: copies the working gameplay objects (ball, follow camera, course manager, pause menu, music)
    /// out of the existing course scene into a reusable prefab. The source scene is closed without saving.
    /// </summary>
    [MenuItem("Tools/Golf is Hard/Create Aero Gameplay Rig Prefab")]
    public static string CreateGameplayRigPrefab()
    {
        var src = EditorSceneManager.OpenScene(SourceGameplayScene, OpenSceneMode.Additive);
        try
        {
            var rig = new GameObject("Aero Gameplay Rig");
            SceneManager.MoveGameObjectToScene(rig, src);
            var found = new List<string>();
            foreach (var g in src.GetRootGameObjects())
            {
                if (System.Array.IndexOf(RigObjects, g.name) < 0) continue;
                g.transform.SetParent(rig.transform, true);
                found.Add(g.name);
            }
            var mgr = rig.GetComponentInChildren<GolfCourseManager>(true);
            if (mgr != null)
            {
                var so = new SerializedObject(mgr);
                so.FindProperty("holesInOrder").arraySize = 0;
                so.FindProperty("tutorialHoles").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var ballCtl = rig.GetComponentInChildren<BallPlayerController>(true);
            if (ballCtl != null && ballCtl.GetComponent<AeroBallSafety>() == null) ballCtl.gameObject.AddComponent<AeroBallSafety>();
            if (ballCtl != null) ballCtl.transform.localScale = Vector3.one * BallScale;
            var cam = rig.GetComponentInChildren<Camera>(true);
            if (cam != null)
            {
                cam.farClipPlane = 3000f;
                var ucd = cam.GetComponent<UniversalAdditionalCameraData>();
                if (ucd == null) ucd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                ucd.renderPostProcessing = true;
            }
            PrefabUtility.SaveAsPrefabAsset(rig, RigPrefab, out bool ok);
            return ok ? "rig saved with: " + string.Join(", ", found) : "save failed";
        }
        finally
        {
            EditorSceneManager.CloseScene(src, true); // discard: the source scene file is untouched
        }
    }

    private static bool AttachGameplayRig()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
        if (asset == null) { Debug.LogWarning("No gameplay rig prefab yet. Run Tools > Golf is Hard > Create Aero Gameplay Rig Prefab."); return false; }
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        var mgr = rig.GetComponentInChildren<GolfCourseManager>(true);
        var ballCtl = rig.GetComponentInChildren<BallPlayerController>(true);
        var cam = rig.GetComponentInChildren<Camera>(true);

        if (mgr != null)
        {
            var so = new SerializedObject(mgr);
            var arr = so.FindProperty("holesInOrder");
            arr.arraySize = holes.Count;
            for (int i = 0; i < holes.Count; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("hole").objectReferenceValue = holes[i].cup != null ? holes[i].cup.GetComponentInChildren<HoleTrigger>(true) : null;
                el.FindPropertyRelative("teePoint").objectReferenceValue = holes[i].tee;
            }
            so.FindProperty("tutorialHoles").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        if (ballCtl != null)
        {
            if (ballCtl.GetComponent<AeroBallSafety>() == null) ballCtl.gameObject.AddComponent<AeroBallSafety>();
            ballCtl.transform.localScale = Vector3.one * BallScale;
            var spawn = holes[0].tee.Find("Ball Spawn Point");
            var sp = spawn != null ? spawn : holes[0].tee;
            ballCtl.transform.SetPositionAndRotation(sp.position, sp.rotation);
        }
        if (cam != null)
        {
            cam.farClipPlane = 3000f;
            cam.transform.SetPositionAndRotation(holes[0].view.position, holes[0].view.rotation);
        }
        return true;
    }

    private static void AddHazards(Transform env)
    {
        // the sea: a thin trigger slab just under the surface, covering the whole world
        var sea = new GameObject("Sea Hazard");
        sea.transform.SetParent(env, false);
        sea.transform.position = new Vector3(0f, WaterY - 0.65f, 0f);
        var box = sea.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(7000f, 1f, 7000f);
        sea.AddComponent<AeroHazard>().label = "Splash!";

        // ponds and lagoons (their water levels were recorded while building each island)
        foreach (var w in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (w.name != "Pond Water") continue;
            var t = w.transform;
            var hz = new GameObject("Pond Hazard");
            hz.transform.SetParent(t.parent, false);
            hz.transform.localPosition = t.localPosition + Vector3.down * 0.65f;
            hz.transform.localRotation = t.localRotation;
            hz.transform.localScale = new Vector3(t.localScale.x * 0.97f, 0.5f, t.localScale.z * 0.97f);
            var mc = hz.AddComponent<MeshCollider>();
            mc.sharedMesh = w.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true; mc.isTrigger = true;
            hz.AddComponent<AeroHazard>().label = "Splash!";
        }

        // the drop below the Cloud Hop sky islands
        var sky = holes.Find(h => h.number == 6);
        if (sky != null)
        {
            var drop = new GameObject("Sky Drop Hazard");
            drop.transform.SetParent(sky.root, false);
            drop.transform.localPosition = new Vector3(0f, -24f, 70f);
            var b = drop.AddComponent<BoxCollider>(); b.isTrigger = true; b.size = new Vector3(140f, 2f, 240f);
            var hz = drop.AddComponent<AeroHazard>(); hz.label = "Into the clouds!"; hz.resetDelay = 0.2f; hz.floats = false;
        }
    }

    /// <summary>Puts the main camera high above a hole, looking down the length of it.</summary>
    public static string AerialHole(int number)
    {
        var h = GameObject.Find($"Hole {number} Cup");
        var t = GameObject.Find($"Hole {number} Tee");
        var cam = Camera.main;
        if (h == null || t == null || cam == null) return "missing";
        var root = h.transform.parent;
        Vector3 a = t.transform.position, b = h.transform.position;
        Vector3 mid = (a + b) * 0.5f;
        float len = Vector3.Distance(a, b);
        cam.transform.position = mid - root.forward * len * 0.75f + root.right * len * 0.35f + Vector3.up * (len * 0.55f + 10f);
        cam.transform.LookAt(mid + root.forward * len * 0.05f);
        return "ok";
    }

    /// <summary>Moves the main camera to a hole's preview viewpoint (handy for screenshots).</summary>
    public static string ViewHole(int number)
    {
        var view = GameObject.Find($"Hole {number} View");
        var cam = Camera.main;
        if (view == null || cam == null) return "missing";
        cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
        return "ok";
    }

    // ===================================================================== materials
    private static void MakeMaterials()
    {
        mWater = Lit("Water_Aqua", new Color(0.18f, 0.80f, 0.92f, 0.78f), 0.97f);
        mWater.SetTexture("_BumpMap", WaterNormal);
        mWater.SetTextureScale("_BumpMap", new Vector2(700f, 700f));
        mWater.SetFloat("_BumpScale", 0.7f);
        mWater.EnableKeyword("_NORMALMAP");

        mSeabed = Lit("Seabed", Color.white, 0.2f);
        mSeabed.SetTexture("_BaseMap", Caustics);
        mSeabed.SetTextureScale("_BaseMap", new Vector2(480f, 480f));

        mPearl = Lit("Pearl", new Color(0.93f, 0.97f, 1f), 0.9f);
        mAqua = Lit("Aqua_Gloss", new Color(0.18f, 0.70f, 0.92f), 0.92f);
        mGlass = Lit("Glass_Aqua", new Color(0.55f, 0.92f, 1f, 0.42f), 0.97f, 0f, new Color(0.02f, 0.08f, 0.1f), true);
        mPanel = Lit("Glass_Panel", new Color(0.75f, 0.96f, 1f, 0.28f), 0.97f, 0f, null, true);
        mLime = Lit("Gloss_Lime", new Color(0.55f, 0.90f, 0.26f), 0.75f);
        mEmerald = Lit("Gloss_Emerald", new Color(0.12f, 0.72f, 0.42f), 0.75f);
        mPink = Lit("Gloss_Pink", new Color(1f, 0.52f, 0.74f), 0.85f);
        mYellow = Lit("Gloss_Yellow", new Color(1f, 0.86f, 0.28f), 0.85f);
        mSky = Lit("Gloss_Sky", new Color(0.32f, 0.62f, 1f), 0.88f);
        mViolet = Lit("Gloss_Violet", new Color(0.64f, 0.50f, 1f), 0.88f);
        mOrange = Lit("Gloss_Orange", new Color(1f, 0.63f, 0.28f), 0.85f);
        mCoral = Lit("Gloss_Coral", new Color(1f, 0.38f, 0.48f), 0.85f);
        mTrunk = Lit("Tree_Trunk", new Color(0.96f, 0.88f, 0.72f), 0.6f);
        mCloud = Lit("Cloud_White", Color.white, 0.1f, 0f, new Color(0.55f, 0.6f, 0.65f));
        mBubble = Lit("Bubble", new Color(0.7f, 0.95f, 1f, 0.22f), 1f, 0f, new Color(0.10f, 0.25f, 0.3f));
        mChrome = Lit("Chrome", new Color(0.88f, 0.94f, 1f), 0.96f, 1f);
        mFoam = Lit("Foam", Color.white, 0.3f, 0f, new Color(0.6f, 0.7f, 0.75f));
        mLantern = Lit("Lantern_Glow", new Color(0.6f, 0.95f, 1f), 0.95f, 0f, new Color(0.9f, 1.6f, 1.8f));
        mCrystal = Lit("Crystal", new Color(0.55f, 0.9f, 1f, 0.6f), 0.98f, 0f, new Color(0.2f, 0.55f, 0.7f));

        mFloatGrass = Lit("Float_Fairway", new Color(0.34f, 0.82f, 0.27f), 0.4f);
        AddGrassDetail(mFloatGrass, new Vector2(1f, 1f));
        mFloatGreen = Lit("Float_Green", new Color(0.52f, 0.94f, 0.38f), 0.4f);
        AddGrassDetail(mFloatGreen, new Vector2(1f, 1f));
        mFloatRim = Lit("Float_Rim", new Color(0.17f, 0.64f, 0.30f), 0.45f);
        mFloatRock = Lit("Float_Rock", new Color(0.80f, 0.86f, 1f), 0.75f);

        mWaterfall = Lit("Waterfall", new Color(1f, 1f, 1f, 0.85f), 0.9f, 0f, new Color(0.15f, 0.25f, 0.3f), true);
        mWaterfall.SetTexture("_BaseMap", Streaks);
        mWaterfall.SetTextureScale("_BaseMap", new Vector2(0.35f, 0.18f));

        mSprayMat = Particle("Spray", new Color(0.9f, 0.98f, 1f, 0.75f), SoftDot);

        Color[] fcols = { new Color(1f, 0.55f, 0.75f), new Color(1f, 0.92f, 0.35f), Color.white, new Color(0.65f, 0.75f, 1f) };
        mFlowers = new Material[fcols.Length];
        for (int i = 0; i < fcols.Length; i++) mFlowers[i] = Lit("Flower_" + i, fcols[i], 0.7f);

        Color[] rb =
        {
            new Color(1f, 0.33f, 0.45f), new Color(1f, 0.62f, 0.25f), new Color(1f, 0.90f, 0.30f),
            new Color(0.45f, 0.90f, 0.35f), new Color(0.25f, 0.82f, 0.95f), new Color(0.30f, 0.52f, 1f), new Color(0.66f, 0.45f, 1f)
        };
        mRainbow = new Material[7]; mRainbowSky = new Material[7];
        for (int i = 0; i < 7; i++)
        {
            mRainbow[i] = Lit("Rainbow_" + i, rb[i], 0.9f, 0f, rb[i] * 0.35f);
            mRainbowSky[i] = Unlit("RainbowSky_" + i, new Color(rb[i].r, rb[i].g, rb[i].b, 0.30f));
        }

        mCloudSprites = new Material[2];
        mCloudSprites[0] = Unlit("CloudSprite_A", Color.white, CloudA);
        mCloudSprites[1] = Unlit("CloudSprite_B", Color.white, CloudB);

        pBouncy = Bouncy("Aero_Bumper", 0.95f, 0.2f);
        pSuperBouncy = Bouncy("Aero_BouncePad", 1.0f, 0.1f);
        pWall = Bouncy("Aero_Wall", 0.6f, 0.3f);
    }

    // ===================================================================== environment
    private static Light existingSun;

    private static void BuildEnvironment(Transform env)
    {
        var ocean = Prim(PrimitiveType.Plane, "Ocean", env, new Vector3(0f, WaterY, 0f), new Vector3(700f, 1f, 700f), mWater, false, false);
        var scroll = ocean.AddComponent<AeroUVScroll>(); scroll.textureProperty = "_BumpMap"; scroll.speed = new Vector2(0.6f, 0.35f);
        Prim(PrimitiveType.Plane, "Seabed", env, new Vector3(0f, SeabedY - 0.05f, 0f), new Vector3(700f, 1f, 700f), mSeabed, true, false);

        var sky = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Sky_Aero.mat");
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, Root + "/Materials/Sky_Aero.mat"); }
        sky.SetColor("_SkyTint", new Color(0.28f, 0.62f, 1f));
        sky.SetColor("_GroundColor", new Color(0.72f, 0.92f, 1f));
        sky.SetFloat("_AtmosphereThickness", 0.75f);
        sky.SetFloat("_Exposure", 1.35f);
        sky.SetFloat("_SunSize", 0.05f);
        EditorUtility.SetDirty(sky);

        // reuse an existing directional light if the scene has one (so a restyled scene doesn't get two suns)
        Light sun = existingSun;
        if (sun == null)
        {
            var sunGO = new GameObject("Sun");
            sunGO.transform.SetParent(env, false);
            sun = sunGO.AddComponent<Light>();
        }
        sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.97f, 0.9f);
        sun.intensity = 1.35f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.72f;

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.15f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.74f, 0.92f, 1f);
        RenderSettings.fogStartDistance = 260f;
        RenderSettings.fogEndDistance = 1400f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        DynamicGI.UpdateEnvironment();

        // post processing
        string volPath = Root + "/AeroIslesVolume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(volPath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, volPath); }
        if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.85f); bloom.intensity.Override(0.75f); bloom.scatter.Override(0.75f);
        if (!profile.TryGet(out Tonemapping tone)) tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        if (!profile.TryGet(out ColorAdjustments grade)) grade = profile.Add<ColorAdjustments>(true);
        grade.saturation.Override(18f); grade.contrast.Override(8f); grade.postExposure.Override(0.15f);
        if (!profile.TryGet(out Vignette vig)) vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.16f); vig.smoothness.Override(0.6f);
        EditorUtility.SetDirty(profile);
        var volGO = new GameObject("Post Processing Volume");
        volGO.transform.SetParent(env, false);
        var vol = volGO.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;

        // sky dressing
        var clouds = Group("Cloud Sprites", env);
        for (int i = 0; i < 46; i++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float rad = Mathf.Lerp(650f, 1250f, (float)rng.NextDouble());
            var pos = new Vector3(Mathf.Cos(ang) * rad, Mathf.Lerp(50f, 190f, (float)rng.NextDouble()), Mathf.Sin(ang) * rad);
            float w = Mathf.Lerp(220f, 460f, (float)rng.NextDouble());
            var q = Prim(PrimitiveType.Quad, "Cloud", clouds.transform, pos, new Vector3(w, w * 0.5f, 1f), mCloudSprites[i % 2], false, false);
            q.transform.rotation = Quaternion.LookRotation(new Vector3(pos.x, 0f, pos.z));
        }
        var puffs = Group("Cloud Puffs", env);
        for (int i = 0; i < 12; i++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float rad = Mathf.Lerp(250f, 700f, (float)rng.NextDouble());
            CloudPuff(puffs.transform, new Vector3(Mathf.Cos(ang) * rad, Mathf.Lerp(70f, 140f, (float)rng.NextDouble()), Mathf.Sin(ang) * rad), Mathf.Lerp(1.4f, 2.6f, (float)rng.NextDouble()));
        }
        var hillMat = Lit("Hill_Green", new Color(0.30f, 0.80f, 0.40f), 0.5f);
        var hills = Group("Distant Isles", env);
        for (int i = 0; i < 11; i++)
        {
            float ang = i * (Mathf.PI * 2f / 11f) + 0.3f;
            float rad = Mathf.Lerp(950f, 1350f, (float)rng.NextDouble());
            float sz = Mathf.Lerp(120f, 260f, (float)rng.NextDouble());
            Prim(PrimitiveType.Sphere, "Isle", hills.transform, new Vector3(Mathf.Cos(ang) * rad, -18f, Mathf.Sin(ang) * rad), new Vector3(sz, sz * 0.35f, sz), hillMat, false, false);
        }
        MarkStatic(clouds); MarkStatic(hills);
    }

    private static void CloudPuff(Transform parent, Vector3 c, float cs)
    {
        var cl = Group("Cloud", parent);
        for (int p = 0; p < 6; p++)
        {
            var o = new Vector3((p - 2.5f) * 9f * cs, (float)rng.NextDouble() * 4f * cs, ((float)rng.NextDouble() * 8f - 4f) * cs);
            float r = (9f - Mathf.Abs(p - 2.5f) * 1.6f) * cs;
            Prim(PrimitiveType.Sphere, "Puff", cl.transform, c + o, new Vector3(r * 1.5f, r * 0.8f, r * 1.1f), mCloud, false, false);
        }
    }

    private static Camera MakeCamera()
    {
        var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 55f; cam.nearClipPlane = 0.2f; cam.farClipPlane = 3000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        return cam;
    }

    // ===================================================================== hole scaffolding
    private static Transform HoleRoot(Transform course, int number, string title, int par, float length, float yOffset = 0f)
    {
        int index = number - 1;
        float th = (90f - index * 40f) * Mathf.Deg2Rad;
        var P = new Vector3(Mathf.Cos(th) * RingR, 0f, Mathf.Sin(th) * RingR);
        var dir = new Vector3(Mathf.Sin(th), 0f, -Mathf.Cos(th));
        var go = new GameObject($"Hole {number} - {title} (Par {par})");
        go.transform.SetParent(course, false);
        go.transform.position = P - dir * length * 0.5f + Vector3.up * yOffset;
        go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        holes.Add(new HoleInfo { number = number, title = title, par = par, root = go.transform });
        keepouts.Clear();
        return go.transform;
    }

    private static HoleInfo Info(Transform root) { return holes.Find(h => h.root == root); }

    private static void PlaceTee(Transform root, Vector3 local, float yaw = 0f)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(TeePrefab);
        var info = Info(root);
        if (asset == null) { Debug.LogWarning("Tee prefab missing: " + TeePrefab); return; }
        var tee = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        tee.name = $"Hole {info.number} Tee";
        tee.transform.SetParent(root, false);
        tee.transform.localPosition = local;
        tee.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        info.tee = tee.transform;

        // preview viewpoint behind the tee
        var view = new GameObject($"Hole {info.number} View").transform;
        view.SetParent(root, false);
        view.localPosition = local + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 6.5f, -14f);
        view.localRotation = Quaternion.Euler(13f, yaw, 0f);
        info.view = view;

        HoleSign(root, info, local + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-6.5f, 0f, 2f), yaw + 12f);
    }

    /// <summary>Glossy Wii-style sign board: hole number, name and par.</summary>
    private static void HoleSign(Transform root, HoleInfo info, Vector3 groundPos, float yaw, string bottomLine = null)
    {
        var s = Group($"Hole {info.number} Sign", root);
        s.transform.localPosition = groundPos;
        s.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Prim(PrimitiveType.Cylinder, "Post", s.transform, new Vector3(0f, 0.9f, 0.05f), new Vector3(0.22f, 0.9f, 0.22f), mPearl, true);
        Prim(PrimitiveType.Cube, "Board", s.transform, new Vector3(0f, 2.4f, 0f), new Vector3(3.4f, 1.6f, 0.18f), mPearl, true);
        Prim(PrimitiveType.Cube, "Frame", s.transform, new Vector3(0f, 2.4f, 0.04f), new Vector3(3.62f, 1.82f, 0.14f), mAqua, false);
        foreach (float y in new[] { 1.5f, 3.3f })
            Prim(PrimitiveType.Capsule, "Frame Edge", s.transform, new Vector3(0f, y, 0.02f), new Vector3(0.22f, 1.85f, 0.22f), mAqua, false, true, Quaternion.Euler(0f, 0f, 90f));
        Prim(PrimitiveType.Sphere, "Badge", s.transform, new Vector3(-1.25f, 2.4f, -0.12f), new Vector3(1.05f, 1.05f, 0.25f), mLime, false);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        TextMesh Text(string name, string text, Vector3 pos, float size, Color col, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(s.transform, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.text = text; tm.fontSize = 64; tm.characterSize = size;
            tm.anchor = anchor; tm.alignment = TextAlignment.Left; tm.color = col; tm.fontStyle = FontStyle.Bold;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }
        Text("Number", info.number.ToString(), new Vector3(-1.25f, 2.42f, -0.26f), 0.11f, Color.white, TextAnchor.MiddleCenter);
        Text("Title", info.title, new Vector3(-0.6f, 2.65f, -0.12f), 0.045f, new Color(0.05f, 0.3f, 0.55f), TextAnchor.MiddleLeft);
        Text("Par", bottomLine ?? ("PAR " + info.par), new Vector3(-0.6f, 2.12f, -0.12f), 0.05f, new Color(0.1f, 0.6f, 0.35f), TextAnchor.MiddleLeft);
    }

    private static List<Vector2> RoundedRect(float w, float h, float r)
    {
        var l = new List<Vector2>();
        float hw = w * 0.5f - r, hh = h * 0.5f - r;
        Vector2[] centers = { new Vector2(hw, hh), new Vector2(hw, -hh), new Vector2(-hw, -hh), new Vector2(-hw, hh) };
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.PI * 0.5f - (c * Mathf.PI * 0.5f) - i / 6f * Mathf.PI * 0.5f;
                l.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        return l;
    }

    private static void PlaceCup(Transform root, Vector3 local)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CupPrefab);
        var info = Info(root);
        if (asset == null) { Debug.LogWarning("Cup prefab missing: " + CupPrefab); return; }
        var cup = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        cup.name = $"Hole {info.number} Cup";
        cup.transform.SetParent(root, false);
        cup.transform.localPosition = local;
        cup.transform.localRotation = Quaternion.identity;
        // the ground under the cup is already cut by AeroKit, so stop the box-surface auto cutter from acting
        var cutter = cup.GetComponent<GolfHoleAutoCutter>();
        if (cutter != null)
        {
            var so = new SerializedObject(cutter);
            var p = so.FindProperty("cutBoxSurfaceOnPlacement"); if (p != null) p.boolValue = false;
            var h = so.FindProperty("hasCutSurface"); if (h != null) h.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        info.cup = cup.transform;
    }

    private static Vector3 V(IslandDef d, float x, float z, float up = 0f) { return new Vector3(x, d.Height(x, z) + up, z); }

    // ===================================================================== decoration helpers
    private static void Tree(Transform parent, Vector3 pos, float s)
    {
        var t = Group("Tree", parent);
        t.transform.localPosition = pos;
        Prim(PrimitiveType.Cylinder, "Trunk", t.transform, new Vector3(0f, 0.9f * s, 0f), new Vector3(0.35f * s, 0.9f * s, 0.35f * s), mTrunk, true);
        var leaf = rng.Next(2) == 0 ? mLime : mEmerald;
        Prim(PrimitiveType.Sphere, "Canopy", t.transform, new Vector3(0f, 2.9f * s, 0f), Vector3.one * 2.6f * s, leaf, true);
        if (rng.Next(3) == 0) Prim(PrimitiveType.Sphere, "CanopyTop", t.transform, new Vector3(0.3f * s, 4.2f * s, 0f), Vector3.one * 1.6f * s, leaf);
    }

    private static bool Clear(float x, float z, List<Vector3> exclude)
    {
        if (exclude == null) return true;
        foreach (var e in exclude) if ((x - e.x) * (x - e.x) + (z - e.z) * (z - e.z) < e.y * e.y) return false;
        return true;
    }

    // ---- keep-outs: structure volumes (in the current hole's local space) that decoration must not overlap
    private class KeepOut { public List<Vector3> pts; public float radius, below, above; }
    private static readonly List<KeepOut> keepouts = new List<KeepOut>();

    /// <summary>Reserve a tube around a path (or a single point): radius in XZ, and a vertical band from y-below to y+above.</summary>
    private static void KeepOutPath(List<Vector3> pts, float radius, float below, float above)
    {
        keepouts.Add(new KeepOut { pts = pts, radius = radius, below = below, above = above });
    }

    /// <summary>True if a thing at p (base), with XZ radius r and the given height, would overlap any keep-out.</summary>
    private static bool Blocked(Vector3 p, float r, float height)
    {
        var q = new Vector2(p.x, p.z);
        foreach (var k in keepouts)
        {
            float bestD = float.MaxValue, bestY = k.pts[0].y;
            if (k.pts.Count == 1) { bestD = Vector2.Distance(q, new Vector2(k.pts[0].x, k.pts[0].z)); }
            for (int i = 0; i < k.pts.Count - 1; i++)
            {
                Vector2 a = new Vector2(k.pts[i].x, k.pts[i].z), b = new Vector2(k.pts[i + 1].x, k.pts[i + 1].z), ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                float dd = Vector2.Distance(q, a + ab * t);
                if (dd < bestD) { bestD = dd; bestY = Mathf.Lerp(k.pts[i].y, k.pts[i + 1].y, t); }
            }
            if (bestD > k.radius + r) continue;
            if (p.y + height < bestY - k.below || p.y > bestY + k.above) continue;
            return true;
        }
        return false;
    }

    /// <summary>exclude entries: (x, radius, z).</summary>
    private static void Dress(IslandDef d, Transform parent, int trees, int flowers, List<Vector3> exclude = null, float treeIsland = 0.30f)
    {
        var tg = Group("Trees", parent); var fg = Group("Flowers", parent);
        Bounds(d, out float x0, out float x1, out float z0, out float z1);
        int placed = 0, tries = 0;
        while (placed < trees && tries++ < trees * 80)
        {
            float x = Mathf.Lerp(x0, x1, (float)rng.NextDouble()), z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
            if (d.IslandT(x, z) < treeIsland || d.FairT(x, z) > -0.30f || d.GreenT(x, z) > -0.9f || d.TeeT(x, z) > -0.8f) continue;
            if (d.BunkerT(x, z) > -0.8f || d.PondT(x, z) > -0.5f || d.ProtectT(x, z) > -0.4f || !Clear(x, z, exclude)) continue;
            float h = d.Height(x, z); if (h < d.beachLevel - 0.2f) continue;
            if (d.hasCup && Vector2.Distance(new Vector2(x, z), d.cup) < 14f) continue;
            float s = 0.85f + (float)rng.NextDouble() * 0.9f;
            if (Blocked(new Vector3(x, h, z), 1.4f * s, 5.6f * s)) continue;
            Tree(tg.transform, new Vector3(x, h, z), s);
            placed++;
        }
        placed = 0; tries = 0;
        while (placed < flowers && tries++ < flowers * 60)
        {
            float x = Mathf.Lerp(x0, x1, (float)rng.NextDouble()), z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
            if (d.IslandT(x, z) < 0.22f || d.FairT(x, z) > -0.12f || d.GreenT(x, z) > -0.4f || d.TeeT(x, z) > -0.3f) continue;
            if (d.BunkerT(x, z) > -0.3f || d.PondT(x, z) > -0.25f || d.ProtectT(x, z) > -0.1f || !Clear(x, z, exclude)) continue;
            float h = d.Height(x, z); if (h < d.beachLevel - 0.2f) continue;
            if (Blocked(new Vector3(x, h, z), 0.3f, 0.4f)) continue;
            Prim(PrimitiveType.Sphere, "Flower", fg.transform, new Vector3(x, h + 0.12f, z), Vector3.one * 0.28f, mFlowers[rng.Next(mFlowers.Length)], false, false);
            placed++;
        }
        MarkStatic(tg); MarkStatic(fg);
    }

    private static void Bounds(IslandDef d, out float x0, out float x1, out float z0, out float z1)
    {
        x0 = z0 = float.MaxValue; x1 = z1 = float.MinValue;
        foreach (var p in d.paths) foreach (var q in p.pts) { x0 = Mathf.Min(x0, q.x - 30f); x1 = Mathf.Max(x1, q.x + 30f); z0 = Mathf.Min(z0, q.y - 30f); z1 = Mathf.Max(z1, q.y + 30f); }
        foreach (var b in d.land) { x0 = Mathf.Min(x0, b.x - b.rx); x1 = Mathf.Max(x1, b.x + b.rx); z0 = Mathf.Min(z0, b.z - b.rz); z1 = Mathf.Max(z1, b.z + b.rz); }
    }

    private static void Bubbles(Transform parent, Vector3 center, Vector3 extent, int count, float minS = 0.5f, float maxS = 2.2f, bool checkKeepouts = true)
    {
        var g = Group("Bubbles", parent);
        Physics.SyncTransforms();
        for (int i = 0; i < count; i++)
        {
            Vector3 p = center; float size = minS; bool ok = false;
            for (int attempt = 0; attempt < 40 && !ok; attempt++)
            {
                p = center + new Vector3(((float)rng.NextDouble() * 2f - 1f) * extent.x, (float)rng.NextDouble() * extent.y, ((float)rng.NextDouble() * 2f - 1f) * extent.z);
                size = Mathf.Lerp(minS, maxS, (float)rng.NextDouble());
                // leave room for the bob motion too
                ok = !checkKeepouts || !Blocked(p - Vector3.up * (size * 0.5f + 1f), size * 0.5f + 0.3f, size + 2f);
                // and test against everything already built (ground, trees, structures) across the bob range
                if (ok)
                {
                    var w = parent.TransformPoint(p);
                    float r = size * 0.5f + 0.25f;
                    ok = !Physics.CheckSphere(w, r, ~0, QueryTriggerInteraction.Ignore)
                      && !Physics.CheckSphere(w + Vector3.up * 1.0f, r, ~0, QueryTriggerInteraction.Ignore)
                      && !Physics.CheckSphere(w - Vector3.up * 1.0f, r, ~0, QueryTriggerInteraction.Ignore);
                }
            }
            if (!ok) continue;
            var b = Prim(PrimitiveType.Sphere, "Bubble", g.transform, p, Vector3.one * size, mBubble, false, false);
            var bob = b.AddComponent<AeroBob>(); bob.amplitude = Mathf.Lerp(0.3f, 1.0f, (float)rng.NextDouble()); bob.speed = Mathf.Lerp(0.4f, 0.9f, (float)rng.NextDouble());
        }
    }

    private static Dictionary<string, Mesh> torusCache = new Dictionary<string, Mesh>();

    private static Mesh TorusMesh(string name, float radius, float tube, int segs = 56, float arcDeg = 360f)
    {
        if (torusCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var path = new List<Vector3>();
        bool closed = arcDeg >= 359.9f;
        int n = closed ? segs : segs + 1;
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Deg2Rad * (arcDeg * i / segs);
            path.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
        var m = SaveMesh(Sweep(path, Circle(tube, Vector2.zero, 12, true), true, false, _ => Vector3.forward, closed), name);
        torusCache[name] = m;
        return m;
    }

    /// <summary>A standing hoop facing local +Z (rotated by yaw), bottom sunk slightly into the ground.</summary>
    private static GameObject Hoop(Transform parent, string name, Vector3 groundPos, float yaw, float radius, float tube, Material mat, bool collider)
    {
        var mesh = TorusMesh(name + "_Mesh", radius, tube);
        var go = MeshObject(name, parent, mesh, mat, collider);
        go.transform.localPosition = groundPos + Vector3.up * (radius - tube * 1.5f);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    private static float Yaw(Vector3 dir) { return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; }

    /// <summary>
    /// Half-ring arch rising out of the sea and spanning a path point. Only placed when both feet land in
    /// open water (so it never cuts through an island) and it clears the structure it spans.
    /// </summary>
    private static bool WaterArch(Transform parent, string name, IslandDef d, List<Vector3> path, int k, float radius, float tube, Material mat, float clearTop, float topAbove)
    {
        var p = path[k];
        var t = path[Mathf.Min(k + 1, path.Count - 1)] - path[Mathf.Max(k - 1, 0)]; t.y = 0f; t.Normalize();
        var side = new Vector3(t.z, 0f, -t.x);
        for (float s = -1f; s <= 1f; s += 2f)
        {
            var foot = p + side * (radius * s);
            if (d.Height(foot.x, foot.z) > WaterY - 0.6f) return false;
        }
        // the arch at the structure's half-width must sit above its top
        float rise = Mathf.Sqrt(Mathf.Max(0f, radius * radius - clearTop * clearTop));
        if (WaterY + rise - tube < p.y + topAbove + 0.25f) return false;
        var a = MeshObject(name, parent, TorusMesh(name + "_Mesh_" + radius, radius, tube, 48, 180f), mat, false);
        a.transform.localPosition = new Vector3(p.x, WaterY, p.z);
        a.transform.localRotation = Quaternion.Euler(0f, Yaw(t), 0f);
        return true;
    }

    // ===================================================================== structure helpers
    /// <summary>Glass deck bridge with glass side panels (so the ball can't roll off) and pearl handrails.</summary>
    private static void Bridge(Transform parent, string name, List<Vector3> path, float width, IslandDef ground = null, float wallH = 0.7f,
        Material deck = null, bool pillars = true, float baseY = SeabedY)
    {
        var g = Group(name, parent);
        float w = width * 0.5f;
        KeepOutPath(path, w + 0.4f, 0.6f, wallH + 0.6f);
        var deckMesh = SaveMesh(Sweep(path, Slab(width, 0.45f), true, true), name + "_Deck");
        MeshObject("Deck", g.transform, deckMesh, deck ?? mGlass, true);
        var wallL = SaveMesh(Sweep(path, new List<Vector2> { new Vector2(-w, wallH), new Vector2(-w + 0.14f, wallH), new Vector2(-w + 0.14f, -0.05f), new Vector2(-w, -0.05f) }, true, true), name + "_WallL");
        var wallR = SaveMesh(Sweep(path, new List<Vector2> { new Vector2(w - 0.14f, wallH), new Vector2(w, wallH), new Vector2(w, -0.05f), new Vector2(w - 0.14f, -0.05f) }, true, true), name + "_WallR");
        MeshObject("Panel L", g.transform, wallL, mPanel, true, null, false);
        MeshObject("Panel R", g.transform, wallR, mPanel, true, null, false);
        var railL = SaveMesh(Sweep(path, Circle(0.1f, new Vector2(-w + 0.07f, wallH + 0.06f), 8, true), true, false), name + "_RailL");
        var railR = SaveMesh(Sweep(path, Circle(0.1f, new Vector2(w - 0.07f, wallH + 0.06f), 8, true), true, false), name + "_RailR");
        MeshObject("Rail L", g.transform, railL, mPearl, false);
        MeshObject("Rail R", g.transform, railR, mPearl, false);
        if (!pillars) return;
        float acc = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            acc += Vector3.Distance(path[i - 1], path[i]);
            if (acc < 13f) continue;
            acc = 0f;
            var p = path[i];
            float gy = ground != null ? ground.Height(p.x, p.z) : baseY;
            if (gy > p.y - 1.2f) continue;
            float bottom = Mathf.Max(gy, baseY) - 0.5f;
            float hgt = p.y - 0.45f - bottom;
            Prim(PrimitiveType.Cylinder, "Pillar", g.transform, new Vector3(p.x, bottom + hgt * 0.5f, p.z), new Vector3(0.9f, hgt * 0.5f, 0.9f), mPearl, true);
        }
    }

    /// <summary>A curved kicker ramp (quarter-pipe lip) that launches the ball.</summary>
    private static void Kicker(Transform parent, string name, Vector3 start, Vector3 dir, float length, float rise, float width, Material mat)
    {
        dir.y = 0f; dir.Normalize();
        var path = new List<Vector3>();
        for (int i = 0; i <= 14; i++)
        {
            float t = i / 14f;
            path.Add(start + dir * length * t + Vector3.up * (rise * t * t));
        }
        var g = Group(name, parent);
        KeepOutPath(path, width * 0.5f + 0.6f, rise + 1f, 1f);
        var mesh = SaveMesh(Sweep(path, Slab(width, rise + 0.8f, 0.15f), true, true), name + "_Ramp");
        MeshObject("Ramp", g.transform, mesh, mat, true);
        var end = start + dir * length + Vector3.up * (rise * 0.5f - 0.35f);
        Prim(PrimitiveType.Cube, "Ramp Back", g.transform, end, new Vector3(width, rise + 0.75f, 0.2f), mat, true, true, Quaternion.LookRotation(dir));
        var sl = SaveMesh(Sweep(path, Circle(0.14f, new Vector2(-width * 0.5f, 0.05f), 8, true), true, false), name + "_StripeL");
        var sr = SaveMesh(Sweep(path, Circle(0.14f, new Vector2(width * 0.5f, 0.05f), 8, true), true, false), name + "_StripeR");
        MeshObject("Stripe L", g.transform, sl, mYellow, false);
        MeshObject("Stripe R", g.transform, sr, mYellow, false);
        // chevrons painted on the ramp
        for (int i = 0; i < 3; i++)
        {
            var p = path[3 + i * 3];
            Prim(PrimitiveType.Cube, "Chevron", g.transform, p + Vector3.up * 0.02f, new Vector3(width * 0.6f, 0.03f, 0.35f), mYellow, false, false, Quaternion.LookRotation(path[4 + i * 3] - path[3 + i * 3]));
        }
    }

    /// <summary>Floating sky island: grassy cap (optionally a putting green with a cup hole), rim, and a pearly crystal underside.</summary>
    private static GameObject FloatIsland(Transform parent, string name, Vector3 pos, float R, bool cup, float greenR = 0f, float depth = 14f, bool keepOut = true)
    {
        if (keepOut) KeepOutPath(new List<Vector3> { pos }, R + 1f, depth + 1f, 3f);
        var g = Group(name, parent);
        g.transform.localPosition = pos;
        float inner = cup ? CupCutRadius : 0.001f;
        if (cup && greenR > 0f)
        {
            MeshObject("Green", g.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(greenR, 0f), new Vector2(inner, 0f) }, 96, false, true, 0.5f), name + "_Green"), mFloatGreen, true);
            MeshObject("Fairway", g.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(R, 0f), new Vector2(greenR, 0f) }, 96, false, true, 0.5f), name + "_Cap"), mFloatGrass, true);
        }
        else
            MeshObject("Cap", g.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(R, 0f), new Vector2(inner, 0f) }, 96, false, true, 0.5f), name + "_Cap"), mFloatGrass, true);
        MeshObject("Rim", g.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(R + 0.05f, -1.3f), new Vector2(R + 0.38f, -0.65f), new Vector2(R + 0.22f, -0.12f), new Vector2(R, 0f) }, 96), name + "_Rim"), mFloatRim, true);
        var under = new List<Vector2> { new Vector2(0.001f, -depth), new Vector2(R * 0.18f, -depth * 0.86f), new Vector2(R * 0.5f, -depth * 0.55f), new Vector2(R * 0.84f, -depth * 0.24f), new Vector2(R + 0.05f, -1.3f) };
        MeshObject("Underside", g.transform, SaveMesh(Lathe(under, 64), name + "_Under"), mFloatRock, false);
        // hanging crystals
        for (int i = 0; i < 5; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = R * Mathf.Lerp(0.2f, 0.7f, (float)rng.NextDouble());
            float y = -depth * (1f - r / R) * 0.9f;
            Prim(PrimitiveType.Sphere, "Crystal", g.transform, new Vector3(Mathf.Cos(a) * r, y - 1.2f, Mathf.Sin(a) * r), new Vector3(0.7f, 2.4f, 0.7f), mCrystal, false, false);
        }
        return g;
    }

    private static ParticleSystem Spray(Transform parent, Vector3 lp, float speed, float angle, float rate, float size, float life, float gravity = 1f)
    {
        var go = new GameObject("Spray");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed);
        main.startLifetime = life;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.gravityModifier = gravity;
        main.maxParticles = 1500;
        main.startColor = new Color(0.88f, 0.98f, 1f, 0.85f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = angle; sh.radius = 0.25f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.95f, 1f), 1f) },
                     new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mSprayMat;
        return ps;
    }

    // ===================================================================== hub
    private static void BuildHub(Transform course)
    {
        var hub = Group("Lighthouse Isle (Hub)", course).transform;
        var d = new IslandDef { name = "Hub", roll = 1.4f };
        d.land.Add(new Blob(0f, 0f, 70f, 56f));
        d.land.Add(new Blob(-30f, 36f, 32f, 26f));
        d.land.Add(new Blob(38f, -28f, 26f, 20f));
        d.bumps.Add(new Blob(8f, -6f, 26f, 22f, 4.5f));
        d.bunkers.Add(new Blob(-34f, -20f, 9f, 6f));
        d.ponds.Add(new Blob(-26f, 34f, 13f, 10f, 2.2f));
        d.texPerUnit = 6f; d.cell = 1.0f;
        AeroIsland.Build(d, hub, mWater);

        // lighthouse
        float gy = d.Height(8f, -6f);
        var lh = Group("Lighthouse", hub);
        lh.transform.localPosition = new Vector3(8f, gy, -6f);
        var body = new List<Vector2> { new Vector2(6.2f, -1f), new Vector2(5.8f, 2f), new Vector2(4.2f, 30f), new Vector2(4.4f, 30.2f) };
        MeshObject("Tower", lh.transform, SaveMesh(Lathe(body, 64), "Hub_Tower"), mPearl, true);
        for (int i = 0; i < 4; i++)
        {
            float y = 5f + i * 6.5f;
            float r = Mathf.Lerp(5.85f, 4.25f, y / 30f) + 0.05f;
            MeshObject("Band", lh.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(r, y), new Vector2(r + 0.02f, y + 2.2f) }, 64), "Hub_Band" + i), mAqua, false)
                .transform.localPosition = Vector3.zero;
        }
        MeshObject("Balcony", lh.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(4.4f, 30f), new Vector2(6.4f, 30.1f), new Vector2(6.4f, 30.8f), new Vector2(4.5f, 30.9f) }, 64, true), "Hub_Balcony"), mChrome, false);
        Prim(PrimitiveType.Sphere, "Lantern", lh.transform, new Vector3(0f, 33.5f, 0f), new Vector3(6f, 6.5f, 6f), mLantern, false, false);
        Prim(PrimitiveType.Sphere, "Lantern Glass", lh.transform, new Vector3(0f, 33.5f, 0f), new Vector3(7.4f, 7.9f, 7.4f), mGlass, false, false);
        MeshObject("Cap", lh.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(4.6f, 36.6f), new Vector2(3.2f, 38.2f), new Vector2(0.001f, 40.2f) }, 48), "Hub_Cap"), mSky, false);

        // giant sky rainbow arching over the hub
        var rbow = Group("Sky Rainbow", hub);
        rbow.transform.localPosition = new Vector3(0f, WaterY, 20f); // ends touch the sea surface, not the seabed
        rbow.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
        for (int i = 0; i < 7; i++)
        {
            float r = 170f - i * 4.2f;
            var path = new List<Vector3>();
            for (int k = 0; k <= 64; k++) { float a = Mathf.PI * k / 64f; path.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f)); }
            var m = SaveMesh(Sweep(path, new List<Vector2> { new Vector2(-2.1f, 0f), new Vector2(2.1f, 0f) }, false, false, _ => Vector3.forward), "Hub_SkyRainbow" + i);
            MeshObject("Band " + i, rbow.transform, m, mRainbowSky[i], false, null, false);
        }

        Dress(d, hub, 70, 120, new List<Vector3> { new Vector3(8f, 9f, -6f) }, 0.2f);
        keepouts.Clear();
        KeepOutPath(new List<Vector3> { new Vector3(8f, gy, -6f) }, 8f, 1f, 42f); // lighthouse
        Bubbles(hub, new Vector3(8f, gy + 8f, -6f), new Vector3(40f, 30f, 40f), 30, 0.8f, 3.5f);
        CloudPuff(hub, new Vector3(-60f, 60f, 90f), 1.5f);
    }

    // ===================================================================== HOLE 1  Lagoon Lane
    private static void Hole1(Transform course)
    {
        var root = HoleRoot(course, 1, "Lagoon Lane", 3, 92f);
        var d = new IslandDef { name = "H1_LagoonLane" };
        var p = new LandPath { extra = 11f };
        for (float s = -6f; s <= 92f; s += 4f) p.Add(9f * Mathf.Sin(s * 0.04f), s, 8.5f + 2.2f * Mathf.Sin(s * 0.08f + 0.5f));
        d.paths.Add(p);
        d.land.Add(new Blob(0f, 3f, 17f, 16f)); d.land.Add(new Blob(-17f, 42f, 15f, 12f));
        d.hasTee = true; d.tee = new Vector2(0f, 0f);
        d.hasGreen = true; d.green = new Blob(-4f, 90f, 13.5f, 10.5f); d.hasCup = true; d.cup = new Vector2(-3f, 91f);
        d.bunkers.Add(new Blob(10.5f, 64f, 5f, 3.6f)); d.bunkers.Add(new Blob(9.5f, 84f, 4.2f, 3f)); d.bunkers.Add(new Blob(-16.5f, 97f, 3.8f, 2.7f));
        d.ponds.Add(new Blob(-17f, 42f, 7.5f, 5.5f, 2.0f));
        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f)); PlaceCup(root, V(d, d.cup.x, d.cup.y));

        // hoop gates down the fairway: thread them for style
        Material[] hm = { mAqua, mPink, mLime };
        float[] ss = { 26f, 48f, 68f };
        for (int i = 0; i < 3; i++)
        {
            float s = ss[i], x = 9f * Mathf.Sin(s * 0.04f);
            float yaw = Mathf.Atan(9f * 0.04f * Mathf.Cos(s * 0.04f)) * Mathf.Rad2Deg;
            Hoop(root, "Hoop Gate " + (i + 1), V(d, x, s), yaw, 3.0f, 0.3f, hm[i], true);
            KeepOutPath(new List<Vector3> { V(d, x, s) }, 3.6f, 0.5f, 6.8f);
        }
        // lily pads + fountain in the pond
        Spray(root, new Vector3(-17f, d.pondWater[0].y, 42f), 7f, 6f, 70f, 0.5f, 1.6f);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.3f;
            Prim(PrimitiveType.Cylinder, "Lily Pad", root, new Vector3(-17f + Mathf.Cos(a) * 4.5f, d.pondWater[0].y + 0.03f, 42f + Mathf.Sin(a) * 3.2f), new Vector3(1.4f, 0.02f, 1.4f), mLime, false, false);
        }
        Dress(d, root, 60, 90, new List<Vector3> { new Vector3(-17f, 9f, 42f) });
        Bubbles(root, new Vector3(0f, 3f, 45f), new Vector3(30f, 13f, 50f), 18);
    }

    // ===================================================================== HOLE 2  Glass Bridge
    private static void Hole2(Transform course)
    {
        var root = HoleRoot(course, 2, "Glass Bridge", 4, 135f);
        var d = new IslandDef { name = "H2_GlassBridge" };
        d.paths.Add(new LandPath { extra = 8f }.Add(0f, -6f, 9f).Add(0f, 18f, 9f));
        d.land.Add(new Blob(0f, 2f, 16f, 15f)); d.land.Add(new Blob(0f, 132f, 26f, 22f));
        d.fair.Add(new Blob(0f, 117f, 9f, 6f));
        d.hasTee = true; d.tee = Vector2.zero;
        d.hasGreen = true; d.green = new Blob(0f, 134f, 12f, 9.5f); d.hasCup = true; d.cup = new Vector2(1f, 135f);
        d.bunkers.Add(new Blob(-13f, 130f, 4f, 3f)); d.bunkers.Add(new Blob(12f, 141f, 4.5f, 3.2f)); d.bunkers.Add(new Blob(10f, 121f, 3.5f, 2.5f));
        d.flats.Add(new Blob(0f, 16f, 8f, 8f)); d.flats.Add(new Blob(2f, 115f, 8f, 7f));

        // the winding glass bridge (main route): planned first so the terrain can be carved to fit under it
        float deckY = Mathf.Max(d.Height(0f, 14f), d.Height(2f, 116f)) + 0.03f;
        var ctrl = new List<Vector3>
        {
            new Vector3(0f, d.Height(0f, 12f) - 0.05f, 12f), new Vector3(2f, deckY, 20f), new Vector3(12f, deckY, 34f), new Vector3(23f, deckY, 52f),
            new Vector3(25f, deckY, 74f), new Vector3(18f, deckY, 96f), new Vector3(6f, deckY, 110f), new Vector3(2f, d.Height(2f, 119f) - 0.05f, 119f)
        };
        var path = CatmullRom(ctrl, 1.0f);
        Carve(d, root, "Hole 2 Glass Bridge", path, 2.8f, 0.08f, 2.0f);

        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f)); PlaceCup(root, V(d, d.cup.x, d.cup.y));
        Bridge(root, "Glass Bridge", path, 5f, d);
        // pearl half-arches rising out of the sea over the bridge (only where both feet are in open water)
        int placedArches = 0;
        for (int i = 1; i <= 9 && placedArches < 4; i += 2)
        {
            int k = path.Count * i / 10;
            if (WaterArch(root, "Bridge Arch " + (placedArches + 1), d, path, k, 4.8f, 0.24f, placedArches % 2 == 0 ? mAqua : mPearl, 2.6f, 0.85f))
                placedArches++;
        }

        // SHORTCUT: lily pad hops straight at the green
        Vector3[] pads = { new Vector3(-2f, 0f, 46f), new Vector3(2f, 0f, 70f), new Vector3(-1f, 0f, 94f) };
        float[] pr = { 4.5f, 4f, 4.5f };
        for (int i = 0; i < 3; i++)
        {
            float r = pr[i];
            var prof = new List<Vector2> { new Vector2(0.001f, -1.6f), new Vector2(r * 0.95f, -1.6f), new Vector2(r, 0.25f), new Vector2(r * 0.96f, 0.5f), new Vector2(0.001f, 0.5f) };
            var pad = MeshObject("Lily Pad Hop " + (i + 1), root, SaveMesh(Lathe(prof, 64, false, true, 0.5f), "H2_Pad" + i), mLime, true);
            pad.transform.localPosition = pads[i];
            Prim(PrimitiveType.Sphere, "Pad Flower", pad.transform, new Vector3(r * 0.6f, 0.65f, r * 0.3f), new Vector3(0.9f, 0.5f, 0.9f), mPink, false);
            KeepOutPath(new List<Vector3> { pads[i] }, r + 0.5f, 2f, 1.5f);
        }
        Dress(d, root, 34, 70);
        Bubbles(root, new Vector3(10f, 2f, 70f), new Vector3(25f, 12f, 40f), 22);
    }

    // ===================================================================== HOLE 3  Waterfall Terraces
    private static void Hole3(Transform course)
    {
        var root = HoleRoot(course, 3, "Waterfall Terraces", 4, 152f);
        var d = new IslandDef { name = "H3_WaterfallTerraces" };
        d.paths.Add(new LandPath { extra = 12f }.Add(0f, -6f, 10.5f).Add(3f, 30f, 10.5f).Add(6f, 60f, 10.5f).Add(2f, 100f, 10.5f).Add(0f, 130f, 10.5f).Add(2f, 150f, 10f));
        d.land.Add(new Blob(0f, 2f, 17f, 15f)); d.land.Add(new Blob(2f, 152f, 26f, 22f));
        d.hasTee = true; d.tee = Vector2.zero;
        d.hasGreen = true; d.green = new Blob(2f, 152f, 12f, 10f); d.hasCup = true; d.cup = new Vector2(3f, 153f);
        d.custom = (x, z) =>
        {
            float m1 = Smooth(9f, 4f, Mathf.Abs(x - 14f)), w1 = Mathf.Lerp(2.4f, 30f, m1);
            float m2 = Smooth(9f, 4f, Mathf.Abs(x + 14f)), w2 = Mathf.Lerp(2.4f, 30f, m2);
            return 3.5f * Smooth(60f - w1 * 0.5f, 60f + w1 * 0.5f, z) + 3.5f * Smooth(112f - w2 * 0.5f, 112f + w2 * 0.5f, z);
        };
        d.bunkers.Add(new Blob(-9f, 82f, 4f, 3f)); d.bunkers.Add(new Blob(13f, 132f, 4f, 3f)); d.bunkers.Add(new Blob(-9f, 147f, 3.5f, 2.6f));
        d.ponds.Add(new Blob(-5f, 54.5f, 6f, 4f, 1.6f)); d.ponds.Add(new Blob(-5f, 68.5f, 4.5f, 3f, 1.0f));
        d.ponds.Add(new Blob(6f, 106.5f, 6f, 4f, 1.6f)); d.ponds.Add(new Blob(6f, 120.5f, 4.5f, 3f, 1.0f));
        d.flats.Add(new Blob(-5f, 61f, 7f, 13f)); d.flats.Add(new Blob(6f, 113f, 7f, 13f)); d.flats.Add(new Blob(-4f, 95f, 5f, 7f));
        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f)); PlaceCup(root, V(d, d.cup.x, d.cup.y));

        Waterfall(root, d, -5f, 61.2f, 58.8f, d.pondWater[1].y, d.pondWater[0].y);
        Waterfall(root, d, 6f, 113.2f, 110.8f, d.pondWater[3].y, d.pondWater[2].y);

        // SHORTCUT: kicker on the middle terrace launches over the upper cliff toward the green
        Kicker(root, "Cliff Kicker", V(d, -4f, 91.5f, -0.02f), Vector3.forward, 7f, 1.8f, 4f, mPearl);
        // ramp markers so players can spot the side ramps
        Prim(PrimitiveType.Sphere, "Ramp Marker", root, V(d, 14f, 47f, 0.5f), Vector3.one * 1f, mYellow, false);
        Prim(PrimitiveType.Sphere, "Ramp Marker", root, V(d, -14f, 99f, 0.5f), Vector3.one * 1f, mYellow, false);
        Dress(d, root, 55, 90, new List<Vector3> { new Vector3(-5f, 9f, 61f), new Vector3(6f, 9f, 113f), new Vector3(-4f, 6f, 95f) });
        Bubbles(root, new Vector3(0f, 6f, 85f), new Vector3(25f, 14f, 60f), 20);
    }

    private static void Waterfall(Transform root, IslandDef d, float x, float wallTopZ, float wallBotZ, float topWater, float lowWater)
    {
        var g = Group("Waterfall", root);
        const float halfW = 2.3f;
        // highest ground across the sheet's width at a given z
        float GroundAcross(float z)
        {
            float m = float.MinValue;
            for (float o = -halfW - 0.3f; o <= halfW + 0.31f; o += 0.4f) m = Mathf.Max(m, d.Height(x + o, z));
            return m;
        }

        // 1) a shallow stream from the source pool across the bank to the cliff lip, riding just above the grass
        var path = new List<Vector3>();
        float poolEdge = wallTopZ + 4.5f;
        for (float z = poolEdge; z > wallTopZ; z -= 0.25f)
            path.Add(new Vector3(x, Mathf.Max(topWater + 0.02f, GroundAcross(z) + 0.06f), z));
        float lipZ = wallTopZ, lipY = Mathf.Max(path[path.Count - 1].y, GroundAcross(lipZ) + 0.06f);
        path.Add(new Vector3(x, lipY, lipZ));

        // 2) free fall off the lip: increase the launch speed until the arc clears the cliff everywhere
        List<Vector3> fallPts = null;
        for (float v = 2.0f; v <= 9f; v += 0.25f)
        {
            var pts = new List<Vector3>();
            bool clear = true;
            for (float t = 0.04f; ; t += 0.04f)
            {
                float y = lipY - 4.9f * t * t, z = lipZ - v * t;
                if (y <= lowWater - 0.05f) { pts.Add(new Vector3(x, lowWater - 0.05f, z)); break; }
                if (GroundAcross(z) > y - 0.18f) { clear = false; break; }
                pts.Add(new Vector3(x, y, z));
            }
            if (clear) { fallPts = pts; break; }
        }
        if (fallPts == null) { Debug.LogWarning($"Waterfall at x={x}: no clear arc found"); fallPts = new List<Vector3>(); }
        path.AddRange(fallPts);
        waterfallPaths.Add(path);
        tracked.Add(new Tracked { root = root, name = "Hole 3 Waterfall x=" + x, path = path, halfWidth = halfW, below = 0.05f });
        KeepOutPath(path, halfW + 0.5f, 0.6f, 1f);

        var mesh = SaveMesh(Sweep(path, new List<Vector2> { new Vector2(-halfW, 0f), new Vector2(halfW, 0f) }, false, false, _ => new Vector3(0f, 0.45f, -0.9f).normalized), $"H3_Fall_{x}");
        var fall = MeshObject("Falling Water", g.transform, mesh, mWaterfall, false, null, false);
        var sc = fall.AddComponent<AeroUVScroll>(); sc.speed = new Vector2(0f, -1.4f);

        Vector3 land = path[path.Count - 1];
        for (int i = 0; i < 5; i++)
            Prim(PrimitiveType.Sphere, "Foam", g.transform, new Vector3(x - 2f + i, lowWater + 0.05f, land.z - (i % 2) * 0.6f), new Vector3(1.3f, 0.45f, 1.0f), mFoam, false, false);
        var mist = Spray(g.transform, new Vector3(x, lowWater + 0.1f, land.z + 0.2f), 2.2f, 35f, 40f, 0.9f, 1.4f, 0.1f);
        var shape = mist.shape; shape.radius = 1.8f;
    }

    // ===================================================================== HOLE 4  Seashell Spire
    private static void Hole4(Transform course)
    {
        var root = HoleRoot(course, 4, "Seashell Spire", 3, 70f);
        var d = new IslandDef { name = "H4_SeashellSpire" };
        d.paths.Add(new LandPath { extra = 8f }.Add(0f, -6f, 8f).Add(0f, 36f, 8f));
        d.land.Add(new Blob(0f, 2f, 15f, 14f)); d.land.Add(new Blob(0f, 68f, 26f, 26f));
        d.fair.Add(new Blob(0f, 50f, 9f, 6f));
        d.hasTee = true; d.tee = Vector2.zero;
        d.flats.Add(new Blob(0f, 68f, 17f, 17f));
        foreach (float a in new[] { 30f, 150f, 205f, 335f })
        {
            float r = a * Mathf.Deg2Rad;
            d.bunkers.Add(new Blob(Mathf.Cos(r) * 18.5f, 68f + Mathf.Sin(r) * 18.5f, 4.2f, 2.8f, 0f, -a + 90f));
        }
        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f));

        float gH = d.Height(0f, 68f), top = gH + 14f;
        var spire = Group("Spire", root);
        spire.transform.localPosition = new Vector3(0f, 0f, 68f);
        MeshObject("Tower", spire.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(8.6f, gH - 1.5f), new Vector2(8.4f, gH + 0.5f), new Vector2(8.0f, top - 0.5f), new Vector2(8.0f, top) }, 72), "H4_Tower"), mPearl, true);
        KeepOutPath(new List<Vector3> { new Vector3(0f, gH, 68f) }, 8.8f, 2f, 16f);
        // putting surface on top with a real cup hole
        MeshObject("Summit Green", spire.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(8.0f, top), new Vector2(CupCutRadius, top) }, 96, false, true, 0.5f), "H4_SummitGreen"), mFloatGreen, true);
        PlaceCup(root, new Vector3(0f, top, 68f));

        // spiral ramp: two full turns from the ground to the summit
        var path = new List<Vector3>();
        int steps = 180;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float a = (-90f + 720f * t) * Mathf.Deg2Rad;
            path.Add(new Vector3(Mathf.Cos(a) * 10f, Mathf.Lerp(gH + 0.02f, top, t), 68f + Mathf.Sin(a) * 10f));
        }
        var ramp = Group("Spiral Ramp", root);
        MeshObject("Deck", ramp.transform, SaveMesh(Sweep(path, Slab(4.3f, 0.45f), true, true), "H4_SpiralDeck"), mAqua, true);
        MeshObject("Outer Wall", ramp.transform, SaveMesh(Sweep(path, new List<Vector2> { new Vector2(2.0f, 0.7f), new Vector2(2.15f, 0.7f), new Vector2(2.15f, -0.05f), new Vector2(2.0f, -0.05f) }, true, true), "H4_SpiralWall"), mPanel, true, null, false);
        MeshObject("Outer Rail", ramp.transform, SaveMesh(Sweep(path, Circle(0.11f, new Vector2(2.08f, 0.78f), 8, true), true, false), "H4_SpiralRail"), mPearl, false);
        KeepOutPath(path, 2.7f, 0.6f, 1.2f);

        // pastel shell stripes that spiral up the tower in the gap between the ramp's turns (never crossing it)
        Material[] stripeMats = { mYellow, mPink, mSky };
        float[] stripeLift = { 1.9f, 3.5f, 5.1f }; // ramp turns are 7 m apart; deck slab is 0.45 thick
        for (int s = 0; s < 3; s++)
        {
            var sp = new List<Vector3>();
            foreach (var p in path)
            {
                float y = p.y + stripeLift[s];
                if (y + 0.5f > top - 0.4f) break;
                var radial = new Vector3(p.x, 0f, p.z - 68f).normalized;
                float wallR = Mathf.Lerp(8.4f, 8.0f, Mathf.InverseLerp(gH + 0.5f, top - 0.5f, y)); // tower taper
                sp.Add(new Vector3(radial.x * wallR, y, 68f + radial.z * wallR));
            }
            if (sp.Count < 3) continue;
            // a thin raised strip on the wall (lateral axis points outward from the tower)
            var prof = new List<Vector2> { new Vector2(-0.05f, 0.45f), new Vector2(0.06f, 0.45f), new Vector2(0.06f, -0.45f), new Vector2(-0.05f, -0.45f) };
            MeshObject("Shell Stripe", spire.transform.parent, SaveMesh(Sweep(sp, prof, true, true), "H4_Stripe" + s), stripeMats[s], false);
        }

        // shell petals (kept clear of the bunkers and the ramp); SHORTCUT is a high lob straight onto the summit
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Deg2Rad * (i * 30f);
            float px = Mathf.Cos(a) * 15.2f, pz = 68f + Mathf.Sin(a) * 15.2f;
            bool nearBunker = false;
            foreach (var bk in d.bunkers) if (bk.T(px, pz, 1.6f) > -0.15f) nearBunker = true;
            if (nearBunker || (Mathf.Abs(px) < 8f && pz < 58f)) continue; // also leave the ramp entrance open
            Prim(PrimitiveType.Sphere, "Shell Petal", root, V(d, px, pz, 0.3f), new Vector3(2.4f, 1.1f, 3.4f), i % 2 == 0 ? mPink : mPearl, false, true, Quaternion.Euler(0f, -i * 30f + 90f, 0f));
            KeepOutPath(new List<Vector3> { V(d, px, pz) }, 1.9f, 0.5f, 1.2f);
        }
        var halo = Group("Bubble Halo", root);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6f;
            var b = Prim(PrimitiveType.Sphere, "Halo Bubble", halo.transform, new Vector3(Mathf.Cos(a) * 11.5f, top + 4f, 68f + Mathf.Sin(a) * 11.5f), Vector3.one * 1.4f, mBubble, false, false);
            b.AddComponent<AeroBob>().amplitude = 0.7f;
        }
        Dress(d, root, 30, 60, new List<Vector3> { new Vector3(0f, 22f, 68f) });
        Bubbles(root, new Vector3(0f, 3f, 34f), new Vector3(18f, 10f, 25f), 10);
    }

    // ===================================================================== HOLE 5  Pinball Garden
    private static void Hole5(Transform course)
    {
        var root = HoleRoot(course, 5, "Pinball Garden", 4, 134f);
        var d = new IslandDef { name = "H5_PinballGarden", roll = 0.35f };
        d.paths.Add(new LandPath { extra = 11f }.Add(0f, -6f, 14f).Add(0f, 140f, 14f));
        d.land.Add(new Blob(0f, 2f, 18f, 15f)); d.land.Add(new Blob(0f, 132f, 28f, 22f));
        d.hasTee = true; d.tee = Vector2.zero;
        d.hasGreen = true; d.green = new Blob(0f, 132f, 12f, 10f); d.hasCup = true; d.cup = new Vector2(0f, 133f);
        d.bunkers.Add(new Blob(-15f, 140f, 3.5f, 2.6f)); d.bunkers.Add(new Blob(15f, 124f, 3.5f, 2.6f));

        // plan the rail lane first and carve a bed for it, so the gentle terrain roll can't poke through the glass
        var laneCtrl = new List<Vector3> { V(d, -19f, 14f), V(d, -20f, 40f), V(d, -20f, 80f), V(d, -18f, 100f), V(d, -12f, 115f), V(d, -6.5f, 121.5f) };
        var lane = CatmullRom(laneCtrl, 1f);
        for (int i = 0; i < lane.Count; i++) lane[i] = V(d, lane[i].x, lane[i].z, 0.02f);
        for (int pass = 0; pass < 4; pass++) // smooth out little bumps
            for (int i = 1; i < lane.Count - 1; i++) { var p = lane[i]; p.y = (lane[i - 1].y + p.y * 2f + lane[i + 1].y) * 0.25f; lane[i] = p; }
        Carve(d, root, "Hole 5 Rail Lane", lane, 1.65f, 0.26f, 1.5f);

        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f)); PlaceCup(root, V(d, d.cup.x, d.cup.y));

        // bumper field
        var field = Group("Bumper Field", root);
        Material[] bc = { mAqua, mLime, mPink, mYellow, mViolet };
        int k = 0;
        for (int row = 0; row < 7; row++)
        {
            float z = 30f + row * 6f;
            float[] xs = row % 2 == 0 ? new[] { -10f, -5f, 0f, 5f, 10f } : new[] { -7.5f, -2.5f, 2.5f, 7.5f };
            foreach (float x in xs)
            {
                var b = Group("Bumper", field.transform);
                b.transform.localPosition = V(d, x, z);
                var body = Prim(PrimitiveType.Cylinder, "Body", b.transform, new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 0.55f, 1.8f), bc[k % bc.Length], false);
                var mc = body.AddComponent<MeshCollider>(); mc.sharedMesh = body.GetComponent<MeshFilter>().sharedMesh; mc.convex = true; mc.sharedMaterial = pBouncy;
                Prim(PrimitiveType.Sphere, "Dome", b.transform, new Vector3(0f, 1.1f, 0f), new Vector3(1.8f, 0.9f, 1.8f), bc[k % bc.Length], false);
                var ring = MeshObject("Ring", b.transform, TorusMesh("H5_BumperRing", 0.95f, 0.12f), mPearl, false);
                ring.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Prim(PrimitiveType.Sphere, "Glow", b.transform, new Vector3(0f, 1.55f, 0f), Vector3.one * 0.35f, mLantern, false, false);
                KeepOutPath(new List<Vector3> { V(d, x, z) }, 1.1f, 0.2f, 2.0f);
                k++;
            }
        }

        // twin spinning paddles
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -6f : 6f;
            var hubPos = V(d, x, 86f);
            KeepOutPath(new List<Vector3> { hubPos }, 5.8f, 0.5f, 1.8f);
            Prim(PrimitiveType.Cylinder, "Spinner Hub", root, hubPos + Vector3.up * 0.7f, new Vector3(0.9f, 0.7f, 0.9f), mPearl, true);
            var spin = Group("Spinner " + (i + 1), root);
            spin.transform.localPosition = hubPos + Vector3.up * 0.45f;
            var rb = spin.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var sp = spin.AddComponent<AeroSpinner>(); sp.degreesPerSecond = i == 0 ? 55f : -55f;
            Prim(PrimitiveType.Cube, "Paddle", spin.transform, Vector3.zero, new Vector3(10f, 0.6f, 0.5f), i == 0 ? mPink : mSky, true, true, null, pWall);
            Prim(PrimitiveType.Sphere, "Paddle Tip", spin.transform, new Vector3(5f, 0f, 0f), Vector3.one * 0.8f, mYellow, false);
            Prim(PrimitiveType.Sphere, "Paddle Tip", spin.transform, new Vector3(-5f, 0f, 0f), Vector3.one * 0.8f, mYellow, false);
        }

        // funnel walls guiding to the green
        for (int side = -1; side <= 1; side += 2)
        {
            var ctrl = new List<Vector3> { V(d, 16f * side, 95f, -0.1f), V(d, 12f * side, 104f, -0.1f), V(d, 6.5f * side, 116f, -0.1f) };
            var path = CatmullRom(ctrl, 0.8f);
            for (int i = 0; i < path.Count; i++) path[i] = V(d, path[i].x, path[i].z, -0.1f);
            MeshObject(side < 0 ? "Funnel Wall L" : "Funnel Wall R", root, SaveMesh(Sweep(path, Wall(0.7f, 0.9f), true, false), "H5_Funnel" + side), mPearl, true, pWall);
            KeepOutPath(path, 0.8f, 0.5f, 1.4f);
            for (int i = 2; i < path.Count; i += 4)
                Prim(PrimitiveType.Sphere, "Funnel Light", root, path[i] + Vector3.up * 1.05f, Vector3.one * 0.3f, mLantern, false, false);
        }

        // SHORTCUT: glass rail lane down the left edge that skips the bumpers and paddles (path planned above)
        MeshObject("Rail Lane", root, SaveMesh(Sweep(lane, UChannel(1.3f, 0.9f, 0.18f, 10), true, false), "H5_RailLane"), mGlass, true);
        KeepOutPath(lane, 2.0f, 0.4f, 1.4f);
        for (int i = 0; i < lane.Count; i += 6)
            Prim(PrimitiveType.Sphere, "Lane Light", root, lane[i] + Vector3.up * 1.0f + Vector3.left * 0.0f, Vector3.one * 0.28f, mLantern, false, false);

        Dress(d, root, 36, 80, new List<Vector3> { new Vector3(0f, 16f, 50f), new Vector3(0f, 14f, 86f), new Vector3(-19f, 3f, 70f) });
        Bubbles(root, new Vector3(0f, 4f, 70f), new Vector3(26f, 12f, 55f), 22);
    }

    // ===================================================================== HOLE 6  Cloud Hop (sky islands)
    private static void Hole6(Transform course)
    {
        var root = HoleRoot(course, 6, "Cloud Hop", 4, 136f, 38f);
        var isl = Group("Sky Islands", root).transform;
        var T = new Vector3(0f, 0f, 0f); float rT = 11f;
        var A = new Vector3(10f, 2f, 46f); float rA = 9f;
        var B = new Vector3(-8f, 4f, 88f); float rB = 9f;
        var G = new Vector3(1f, 3f, 136f); float rG = 14f;
        FloatIsland(isl, "Tee Isle", T, rT, false);
        FloatIsland(isl, "Isle A", A, rA, false);
        FloatIsland(isl, "Isle B", B, rB, false);
        FloatIsland(isl, "Green Isle", G, rG, true, 8f, 18f);
        PlaceTee(root, new Vector3(0f, 0f, -3f));
        PlaceCup(root, G);

        SkyBridge(root, "Bridge 1", T, rT, A, rA, -6f);
        SkyBridge(root, "Bridge 2", A, rA, B, rB, 7f);
        SkyBridge(root, "Bridge 3", B, rB, G, rG, -6f);

        // SHORTCUT: a bounce pad hanging below the islands that springs the ball toward the green
        var prof = new List<Vector2> { new Vector2(0.001f, -0.8f), new Vector2(3.4f, -0.8f), new Vector2(3.6f, 0.1f), new Vector2(3.4f, 0.3f), new Vector2(0.001f, 0.3f) };
        var pad = MeshObject("Bounce Pad", root, SaveMesh(Lathe(prof, 64, false, true, 0.5f), "H6_BouncePad"), mPink, true, pSuperBouncy);
        pad.transform.localPosition = new Vector3(17f, -3f, 93f);
        pad.transform.localRotation = Quaternion.Euler(14f, -18f, 0f);
        var ring = MeshObject("Bounce Ring", pad.transform, TorusMesh("H6_PadRing", 3.6f, 0.2f), mYellow, false);
        ring.transform.localPosition = new Vector3(0f, 0.25f, 0f); ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        FloatIsland(pad.transform, "Pad Rock", new Vector3(0f, -0.9f, 0f), 3.4f, false, 0f, 5f, false);
        KeepOutPath(new List<Vector3> { new Vector3(17f, -3f, 93f) }, 4.6f, 7.5f, 2.5f);

        // trees on islands
        Tree(isl, A + new Vector3(4f, 0f, 3f), 1.1f); Tree(isl, A + new Vector3(-4f, 0f, -2f), 0.9f);
        Tree(isl, B + new Vector3(-4f, 0f, 2f), 1.2f); Tree(isl, B + new Vector3(3f, 0f, 5f), 0.8f);
        Tree(isl, T + new Vector3(-6f, 0f, 4f), 1.0f); Tree(isl, T + new Vector3(6.5f, 0f, -3f), 1.2f);
        Tree(isl, G + new Vector3(-11f, 0f, 5f), 1.1f); Tree(isl, G + new Vector3(10f, 0f, 8f), 1.0f);

        // dressing: clouds under the islands, floating crystals, tiny decor isles
        var dress = Group("Sky Dressing", root).transform;
        // clouds sit well below each island's crystal underside (cone tips are 14-18 m down) so they never overlap it
        CloudPuff(dress, T + new Vector3(0f, -27f, 0f), 0.9f); CloudPuff(dress, A + new Vector3(0f, -27f, 0f), 0.9f);
        CloudPuff(dress, B + new Vector3(0f, -27f, 0f), 0.9f); CloudPuff(dress, G + new Vector3(0f, -32f, 0f), 0.9f);
        Vector3[] mini = { new Vector3(-26f, 6f, 20f), new Vector3(28f, 9f, 60f), new Vector3(-30f, 12f, 110f), new Vector3(26f, -2f, 150f) };
        foreach (var m in mini)
        {
            FloatIsland(dress, "Mini Isle", m, 3.5f, false, 0f, 6f);
            Tree(dress, m, 0.8f);
        }
        CloudPuff(dress, new Vector3(-34f, -10f, 64f), 1.0f); CloudPuff(dress, new Vector3(38f, -14f, 122f), 0.9f);
        var lathe = SaveMesh(Lathe(new List<Vector2> { new Vector2(0.001f, -1.6f), new Vector2(0.7f, 0f), new Vector2(0.001f, 1.6f) }, 6, true), "H6_Crystal");
        int crystals = 0;
        for (int tries = 0; crystals < 16 && tries < 400; tries++)
        {
            var p = new Vector3(Mathf.Lerp(-35f, 35f, (float)rng.NextDouble()), Mathf.Lerp(-6f, 14f, (float)rng.NextDouble()), Mathf.Lerp(-10f, 150f, (float)rng.NextDouble()));
            float sc = Mathf.Lerp(0.8f, 2.0f, (float)rng.NextDouble());
            if (Blocked(p - Vector3.up * (1.6f * sc + 1f), 0.8f * sc + 0.5f, 3.2f * sc + 2f)) continue; // incl. bob range
            var cr = MeshObject("Floating Crystal", dress, lathe, mCrystal, false, null, false);
            cr.transform.localPosition = p;
            cr.transform.localScale = Vector3.one * sc;
            var bob = cr.AddComponent<AeroBob>(); bob.amplitude = 0.8f; bob.spinDegreesPerSecond = 25f;
            crystals++;
        }
        Bubbles(root, new Vector3(0f, -4f, 70f), new Vector3(30f, 20f, 70f), 24);
    }

    private static void SkyBridge(Transform root, string name, Vector3 a, float ra, Vector3 b, float rb, float bend)
    {
        var flat = b - a; flat.y = 0f;
        var dir = flat.normalized;
        var perp = new Vector3(dir.z, 0f, -dir.x);
        var s = a + dir * (ra - 1.2f) + Vector3.up * 0.0f;
        var e = b - dir * (rb - 1.2f);
        var mid = (s + e) * 0.5f + perp * bend + Vector3.up * 1.2f;
        var path = CatmullRom(new List<Vector3> { s, Vector3.Lerp(s, mid, 0.5f), mid, Vector3.Lerp(mid, e, 0.5f), e }, 0.8f);
        Bridge(root, name, path, 2.8f, null, 0.6f, mGlass, false);
    }

    // ===================================================================== HOLE 7  Halfpipe Canyon
    private static void Hole7(Transform course)
    {
        var root = HoleRoot(course, 7, "Halfpipe Canyon", 4, 162f);
        var d = new IslandDef { name = "H7_HalfpipeCanyon" };
        d.paths.Add(new LandPath { extra = 8f }.Add(0f, -6f, 9f).Add(0f, 18f, 9f));
        d.land.Add(new Blob(0f, 2f, 16f, 14f)); d.land.Add(new Blob(-10f, 86f, 12f, 10f)); d.land.Add(new Blob(-10f, 160f, 26f, 22f));
        d.fair.Add(new Blob(-10f, 86f, 6f, 5f)); d.fair.Add(new Blob(-7f, 141f, 9f, 6f));
        d.hasTee = true; d.tee = Vector2.zero;
        d.hasGreen = true; d.green = new Blob(-10f, 162f, 12f, 10f); d.hasCup = true; d.cup = new Vector2(-9f, 163f);
        d.bunkers.Add(new Blob(-16f, 90f, 3f, 2.2f)); d.bunkers.Add(new Blob(3f, 158f, 4f, 3f)); d.bunkers.Add(new Blob(-23f, 167f, 4f, 3f));
        d.flats.Add(new Blob(1f, 22f, 9f, 14f)); d.flats.Add(new Blob(-11f, 141f, 8f, 7f)); d.flats.Add(new Blob(-9f, 23f, 4f, 7f));

        // plan the halfpipe first, then carve a trench for it so the islands sit under its shell
        var ctrl = new List<Vector3> { new Vector3(0f, 0f, 13f), new Vector3(3f, 0f, 32f), new Vector3(18f, 0f, 54f), new Vector3(28f, 0f, 76f), new Vector3(20f, 0f, 98f), new Vector3(2f, 0f, 112f), new Vector3(-10f, 0f, 126f), new Vector3(-11f, 0f, 141f) };
        var path = CatmullRom(ctrl, 0.8f);
        float y0 = d.Height(0f, 13f), y1 = d.Height(-11f, 141f);
        for (int i = 0; i < path.Count; i++)
        {
            float t = i / (float)(path.Count - 1);
            var p = path[i]; p.y = Mathf.Lerp(y0, y1, t) - 0.6f * Mathf.Sin(Mathf.PI * t); path[i] = p;
        }
        Carve(d, root, "Hole 7 Halfpipe", path, 5.3f, 0.66f, 2.5f);

        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f)); PlaceCup(root, V(d, d.cup.x, d.cup.y));
        KeepOutPath(path, 5.6f, 0.8f, 3.4f);

        var pipe = Group("Halfpipe", root);
        MeshObject("Pipe", pipe.transform, SaveMesh(Sweep(path, UChannel(4.4f, 2.6f, 0.5f, 18), true, false), "H7_Halfpipe"), mAqua, true);
        MeshObject("Lip L", pipe.transform, SaveMesh(Sweep(path, Circle(0.32f, new Vector2(-4.7f, 2.68f), 10, true), true, false), "H7_LipL"), mPearl, true);
        MeshObject("Lip R", pipe.transform, SaveMesh(Sweep(path, Circle(0.32f, new Vector2(4.7f, 2.68f), 10, true), true, false), "H7_LipR"), mPearl, true);
        float acc = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            acc += Vector3.Distance(path[i - 1], path[i]);
            if (acc < 12f) continue; acc = 0f;
            var p = path[i];
            if (d.Height(p.x, p.z) > p.y - 1.5f) continue;
            float bottom = SeabedY - 0.5f, h = p.y - 0.4f - bottom;
            Prim(PrimitiveType.Cylinder, "Pipe Pillar", pipe.transform, new Vector3(p.x, bottom + h * 0.5f, p.z), new Vector3(1.2f, h * 0.5f, 1.2f), mPearl, true);
        }
        // half-hoops rising out of the sea over the pipe (feet in open water, clear of the pipe's lips)
        Material[] hm = { mYellow, mPink, mLime };
        int hoopCount = 0;
        for (int i = 2; i <= 10 && hoopCount < 3; i++)
        {
            int k = path.Count * i / 12;
            if (WaterArch(pipe.transform, "Pipe Hoop " + (hoopCount + 1), d, path, k, 7.4f, 0.35f, hm[hoopCount], 5.05f, 3.05f)) { hoopCount++; i += 2; }
        }

        // SHORTCUT: launch ramp off the tee island to the mid-sea islet, then chip to the green
        var dir = new Vector3(-1f, 0f, 66f).normalized;
        Kicker(root, "Leap Ramp", V(d, -9f, 19.5f, -0.02f), dir, 7f, 1.7f, 4f, mPearl);
        Tree(root, V(d, -14f, 84f), 1.2f); Tree(root, V(d, -5f, 91f), 0.9f);

        Dress(d, root, 34, 70, new List<Vector3> { new Vector3(-9f, 6f, 24f), new Vector3(0f, 8f, 13f), new Vector3(-11f, 8f, 141f) });
        Bubbles(root, new Vector3(10f, 4f, 80f), new Vector3(30f, 14f, 60f), 24);
    }

    // ===================================================================== HOLE 8  Aqua Dome
    private static void Hole8(Transform course)
    {
        var root = HoleRoot(course, 8, "Aqua Dome", 5, 190f);
        var d = new IslandDef { name = "H8_AquaDome", texPerUnit = 8f };
        d.paths.Add(new LandPath { extra = 11f }.Add(0f, -6f, 9f).Add(0f, 30f, 9.5f).Add(-12f, 60f, 9f).Add(-26f, 95f, 9f).Add(-24f, 130f, 9f).Add(-10f, 158f, 9f).Add(4f, 176f, 9f));
        d.paths.Add(new LandPath { extra = 1.6f, protect = true }.Add(2f, 52f, 2.2f).Add(6f, 95f, 2.2f).Add(6f, 140f, 2.2f).Add(2f, 162f, 2.4f));
        d.land.Add(new Blob(0f, 2f, 17f, 15f)); d.land.Add(new Blob(6f, 105f, 34f, 62f)); d.land.Add(new Blob(8f, 184f, 30f, 24f));
        d.hasTee = true; d.tee = Vector2.zero;
        d.hasGreen = true; d.green = new Blob(8f, 186f, 13f, 11f); d.hasCup = true; d.cup = new Vector2(9f, 187f);
        d.ponds.Add(new Blob(8f, 102f, 18f, 33f, 2.4f));
        d.bunkers.Add(new Blob(-34f, 80f, 4.5f, 3f)); d.bunkers.Add(new Blob(-16f, 150f, 4f, 3f)); d.bunkers.Add(new Blob(10f, 40f, 4f, 3f));
        d.flats.Add(new Blob(8f, 186f, 22f, 22f));
        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f)); PlaceCup(root, V(d, d.cup.x, d.cup.y));

        // the glass greenhouse dome over the green, with three arched doorways
        float gH = d.Height(8f, 186f);
        KeepOutPath(new List<Vector3> { new Vector3(8f, gH, 186f) }, 20f, 1f, 22f);
        var dome = Group("Aqua Dome", root);
        dome.transform.localPosition = new Vector3(8f, gH - 0.05f, 186f);
        float R = 19f;
        float[] openC = { -95f, -148f, -32f }, openW = { 17f, 14f, 14f };
        System.Func<float, float> archH = a =>
        {
            float best = 0f;
            for (int i = 0; i < openC.Length; i++)
            {
                float da = Mathf.Abs(Mathf.DeltaAngle(a, openC[i]));
                if (da < openW[i]) best = Mathf.Max(best, 7.5f * Mathf.Sqrt(1f - (da / openW[i]) * (da / openW[i])));
            }
            return best;
        };
        var prof = new List<Vector2>();
        for (int i = 0; i <= 24; i++) { float phi = Mathf.PI * 0.5f * i / 24f; prof.Add(new Vector2(R * Mathf.Cos(phi) + 0.001f, R * Mathf.Sin(phi))); }
        var shell = SaveMesh(Lathe(prof, 120, false, false, 0.25f, 0f, 360f, (a, y) => y < archH(a)), "H8_DomeShell");
        MeshObject("Glass Shell", dome.transform, shell, mGlass, true, null, false);
        // inward-facing collider layer: the ball's continuous collision ignores back faces, so the
        // outward shell alone wouldn't stop a ball rolling into the glass from inside
        var inner = new List<Vector2>();
        for (int i = prof.Count - 1; i >= 0; i--) inner.Add(prof[i] * ((R - 0.06f) / R));
        var innerMesh = SaveMesh(Lathe(inner, 120, false, false, 0.25f, 0f, 360f, (a, y) => y < archH(a)), "H8_DomeInner");
        var innerGo = new GameObject("Glass Inner Collider");
        innerGo.transform.SetParent(dome.transform, false);
        innerGo.AddComponent<MeshCollider>().sharedMesh = innerMesh;
        for (int i = 0; i < 16; i++)
        {
            float a = i * 22.5f;
            float h0 = archH(a);
            float phi0 = Mathf.Asin(Mathf.Clamp01(h0 / R));
            var rib = new List<Vector3>();
            for (int k = 0; k <= 20; k++)
            {
                float phi = Mathf.Lerp(phi0, Mathf.PI * 0.5f, k / 20f);
                float r = (R + 0.05f) * Mathf.Cos(phi);
                rib.Add(new Vector3(r * Mathf.Cos(a * Mathf.Deg2Rad), (R + 0.05f) * Mathf.Sin(phi), r * Mathf.Sin(a * Mathf.Deg2Rad)));
            }
            MeshObject("Rib", dome.transform, SaveMesh(Sweep(rib, Circle(0.16f, Vector2.zero, 8, true), true, false, _ => Vector3.up), "H8_Rib" + i), mPearl, false);
        }
        // arch frames around the doorways
        for (int i = 0; i < openC.Length; i++)
        {
            var arch = new List<Vector3>();
            for (int k = 0; k <= 24; k++)
            {
                float a = openC[i] - openW[i] + 2f * openW[i] * k / 24f;
                float h = archH(a);
                float phi = Mathf.Asin(Mathf.Clamp01(h / R));
                float r = (R + 0.1f) * Mathf.Cos(phi);
                arch.Add(new Vector3(r * Mathf.Cos(a * Mathf.Deg2Rad), h, r * Mathf.Sin(a * Mathf.Deg2Rad)));
            }
            MeshObject("Door Arch", dome.transform, SaveMesh(Sweep(arch, Circle(0.3f, Vector2.zero, 10, true), true, false, _ => Vector3.up), "H8_DoorArch" + i), mAqua, false);
        }
        Prim(PrimitiveType.Sphere, "Dome Finial", dome.transform, new Vector3(0f, R + 0.6f, 0f), Vector3.one * 1.8f, mLantern, false, false);
        Bubbles(dome.transform, new Vector3(0f, 4f, 0f), new Vector3(11f, 8f, 11f), 12, 0.4f, 1.2f, false);
        for (int i = 0; i < 5; i++)
        {
            float a = Mathf.Deg2Rad * (20f + i * 50f + (i > 1 ? 40f : 0f));
            if (archH(a * Mathf.Rad2Deg) > 0f) continue;
            Tree(dome.transform, new Vector3(Mathf.Cos(a) * 15.5f, 0f, Mathf.Sin(a) * 15.5f), 0.75f);
        }

        // lagoon dressing: lily pads and twin fountains
        float wy = d.pondWater[0].y;
        Spray(root, new Vector3(-2f, wy, 90f), 9f, 5f, 80f, 0.55f, 1.8f);
        Spray(root, new Vector3(17f, wy, 112f), 9f, 5f, 80f, 0.55f, 1.8f);
        for (int i = 0; i < 12; i++)
        {
            float x = Mathf.Lerp(-6f, 22f, (float)rng.NextDouble()), z = Mathf.Lerp(75f, 130f, (float)rng.NextDouble());
            if (Mathf.Abs(x - 5f) < 4f) continue;
            Prim(PrimitiveType.Cylinder, "Lily Pad", root, new Vector3(x, wy + 0.03f, z), new Vector3(1.6f, 0.02f, 1.6f), mLime, false, false);
        }
        // SHORTCUT: the narrow isthmus straight across the lagoon (marked with lanterns)
        for (float z = 60f; z <= 150f; z += 15f)
        {
            float x = z < 95f ? Mathf.Lerp(2f, 6f, (z - 52f) / 43f) : 6f;
            Prim(PrimitiveType.Sphere, "Isthmus Lantern", root, V(d, x - 3.4f, z, 0.6f), Vector3.one * 0.5f, mLantern, false, false);
            Prim(PrimitiveType.Sphere, "Isthmus Lantern", root, V(d, x + 3.4f, z, 0.6f), Vector3.one * 0.5f, mLantern, false, false);
        }
        Dress(d, root, 70, 110, new List<Vector3> { new Vector3(8f, 22f, 186f) });
        Bubbles(root, new Vector3(5f, 3f, 100f), new Vector3(30f, 14f, 60f), 24);
    }

    // ===================================================================== HOLE 9  Rainbow Finale
    private static void Hole9(Transform course)
    {
        var root = HoleRoot(course, 9, "Rainbow Finale", 5, 175f);
        var d = new IslandDef { name = "H9_RainbowFinale", texPerUnit = 8f };
        d.paths.Add(new LandPath { extra = 12f }.Add(0f, -6f, 10f).Add(-6f, 20f, 10f).Add(-24f, 48f, 10f).Add(-42f, 85f, 10f).Add(-42f, 122f, 10f).Add(-26f, 152f, 10f).Add(-2f, 170f, 10f).Add(14f, 172f, 9f));
        d.land.Add(new Blob(0f, 2f, 18f, 16f)); d.land.Add(new Blob(8f, 98f, 11f, 11f)); d.land.Add(new Blob(24f, 170f, 28f, 24f));
        d.hasTee = true; d.tee = Vector2.zero;
        d.hasGreen = true; d.green = new Blob(24f, 170f, 13f, 11f); d.hasCup = true; d.cup = new Vector2(25f, 171f);
        d.bumps.Add(new Blob(24f, 170f, 17f, 15f, 1.0f));
        d.bunkers.Add(new Blob(-36f, 60f, 5f, 3.5f)); d.bunkers.Add(new Blob(-52f, 110f, 4f, 3f)); d.bunkers.Add(new Blob(-14f, 166f, 4f, 3f)); d.bunkers.Add(new Blob(37f, 178f, 4f, 3f));
        d.flats.Add(new Blob(-3f, 24f, 6f, 6f)); d.flats.Add(new Blob(16f, 156f, 6f, 6f)); d.flats.Add(new Blob(8f, 98f, 9f, 9f));

        // SHORTCUT: the rainbow bridge arching over the bay (and the fountain). Planned before the island is
        // built so the ground near its low ends is carved to stay under the bands.
        Vector3 A = V(d, -3f, 22f, -0.06f), B = V(d, 16f, 156f, -0.06f);
        var path = new List<Vector3>();
        for (int i = 0; i <= 140; i++)
        {
            float t = i / 140f;
            var p = Vector3.Lerp(A, B, t);
            p.y += 15f * Mathf.Sin(Mathf.PI * t);
            path.Add(p);
        }
        Carve(d, root, "Hole 9 Rainbow", path, 3.8f, 0.08f, 2.0f);

        AeroIsland.Build(d, root, mWater);
        PlaceTee(root, V(d, 0f, 0f)); PlaceCup(root, V(d, d.cup.x, d.cup.y));
        KeepOutPath(path, 4.2f, 0.6f, 1.4f);
        KeepOutPath(new List<Vector3> { V(d, 8f, 98f) }, 7.2f, 0.5f, 7f); // fountain

        var rb = Group("Rainbow Bridge", root);
        for (int i = 0; i < 7; i++)
        {
            float x0 = -3.5f + i, x1 = x0 + 1f;
            MeshObject("Band " + i, rb.transform, SaveMesh(Sweep(path, Band(x0, x1, 0.4f), true, true), "H9_RainbowBand" + i), mRainbow[i], true);
        }
        float w = 3.5f;
        MeshObject("Panel L", rb.transform, SaveMesh(Sweep(path, new List<Vector2> { new Vector2(-w - 0.14f, 0.75f), new Vector2(-w, 0.75f), new Vector2(-w, -0.05f), new Vector2(-w - 0.14f, -0.05f) }, true, true), "H9_PanelL"), mPanel, true, null, false);
        MeshObject("Panel R", rb.transform, SaveMesh(Sweep(path, new List<Vector2> { new Vector2(w, 0.75f), new Vector2(w + 0.14f, 0.75f), new Vector2(w + 0.14f, -0.05f), new Vector2(w, -0.05f) }, true, true), "H9_PanelR"), mPanel, true, null, false);
        MeshObject("Rail L", rb.transform, SaveMesh(Sweep(path, Circle(0.1f, new Vector2(-w - 0.07f, 0.82f), 8, true), true, false), "H9_RailL"), mPearl, false);
        MeshObject("Rail R", rb.transform, SaveMesh(Sweep(path, Circle(0.1f, new Vector2(w + 0.07f, 0.82f), 8, true), true, false), "H9_RailR"), mPearl, false);

        // fountain islet
        float fy = d.Height(8f, 98f);
        var f = Group("Grand Fountain", root);
        f.transform.localPosition = new Vector3(8f, fy, 98f);
        MeshObject("Basin", f.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(6.4f, -0.4f), new Vector2(6.6f, 0.8f), new Vector2(6.2f, 1.0f), new Vector2(5.8f, 0.9f), new Vector2(5.7f, 0.3f), new Vector2(0.001f, 0.3f) }, 72, true), "H9_Basin"), mPearl, true);
        Prim(PrimitiveType.Cylinder, "Basin Water", f.transform, new Vector3(0f, 0.72f, 0f), new Vector3(11.5f, 0.005f, 11.5f), mWater, false, false);
        MeshObject("Column", f.transform, SaveMesh(Lathe(new List<Vector2> { new Vector2(1.1f, 0.2f), new Vector2(0.8f, 3.4f), new Vector2(3.2f, 3.6f), new Vector2(3.3f, 4.0f), new Vector2(0.6f, 4.1f), new Vector2(0.5f, 5.6f), new Vector2(1.6f, 5.8f), new Vector2(1.6f, 6.1f), new Vector2(0.001f, 6.15f) }, 48), "H9_Column"), mPearl, true);
        Prim(PrimitiveType.Cylinder, "Mid Water", f.transform, new Vector3(0f, 3.95f, 0f), new Vector3(6.1f, 0.005f, 6.1f), mWater, false, false);
        Spray(f.transform, new Vector3(0f, 6.2f, 0f), 13f, 4f, 120f, 0.6f, 2.2f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            var s = Spray(f.transform, new Vector3(Mathf.Cos(a) * 4.8f, 0.8f, Mathf.Sin(a) * 4.8f), 6f, 3f, 40f, 0.4f, 1.3f);
            s.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Cos(a), 2.2f, -Mathf.Sin(a)));
        }

        // victory stage: giant ring behind the green and a circle of floating bubbles
        float gy = d.Height(24f, 170f);
        var vr = MeshObject("Victory Ring", root, TorusMesh("H9_VictoryRing", 9f, 0.55f), mPearl, false);
        vr.transform.localPosition = new Vector3(25f, gy + 4.5f, 186f);
        for (int i = 0; i < 7; i++)
        {
            var band = MeshObject("Victory Band " + i, vr.transform, TorusMesh("H9_VictoryBand" + i, 9.9f + i * 0.45f, 0.2f, 56, 180f), mRainbow[6 - i], false);
            band.transform.localPosition = Vector3.zero;
        }
        var bub = Group("Stage Bubbles", root);
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.PI / 5f;
            var b = Prim(PrimitiveType.Sphere, "Stage Bubble", bub.transform, new Vector3(24f + Mathf.Cos(a) * 17f, gy + 5f + (i % 3), 170f + Mathf.Sin(a) * 15f), Vector3.one * 2.6f, mBubble, false, false);
            b.AddComponent<AeroBob>().amplitude = 0.9f;
        }
        Dress(d, root, 80, 130, new List<Vector3> { new Vector3(8f, 10f, 98f), new Vector3(-3f, 6f, 24f), new Vector3(16f, 6f, 156f), new Vector3(24f, 20f, 172f) });
        Bubbles(root, new Vector3(0f, 3f, 100f), new Vector3(40f, 16f, 70f), 26);
    }
}
