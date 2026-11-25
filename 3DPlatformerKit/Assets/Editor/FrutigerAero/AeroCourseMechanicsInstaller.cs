using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Adds the interactive course features to the saved course without rebuilding its scenery.</summary>
public static class AeroCourseMechanicsInstaller
{
    public const string ScenePath = "Assets/FrutigerGolfCourse/FrutigerAeroNineHole.unity";
    private const string Folder = "Assets/FrutigerGolfCourse/InteractiveCourse";
    private const string Marker = "Interactive Course Mechanics";
    private static Material pearl, green, aqua, lamp, rail;
    private static PhysicsMaterial puttingPhysics;
    private static readonly Color Idle = new Color(0.08f, 0.72f, 1f);
    private static readonly Color Active = new Color(0.46f, 1f, 0.24f);

    [MenuItem("Tools/Golf is Hard/Add Moving Cups and Shot Switches")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play mode before installing course mechanisms.");
            return;
        }
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try { UpgradeScene(scene); }
        finally { if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
    }

    public static void UpgradeScene(Scene scene, bool saveScene = true)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        if (!string.IsNullOrEmpty(scene.path) && scene.path != ScenePath)
            throw new System.InvalidOperationException("Interactive course upgrades apply only to FrutigerAeroNineHole.");
        GolfCourseManager manager = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            manager = root.GetComponentInChildren<GolfCourseManager>(true);
            if (manager != null) break;
        }
        if (manager == null) throw new System.InvalidOperationException("The course has no GolfCourseManager.");
        var settings = new SerializedObject(manager);
        SerializedProperty stages = settings.FindProperty("holesInOrder");
        if (stages == null || stages.arraySize != 9)
            throw new System.InvalidOperationException("Expected the nine configured course stages; no course objects were changed.");
        PrepareAssets();
        Physics.SyncTransforms();
        int upgraded = 0;
        for (int i = 0; i < stages.arraySize; i++)
        {
            int number = i + 1;
            if (number != 2 && number != 3 && number != 5 && number != 6 && number != 7 && number != 9) continue;
            var stage = stages.GetArrayElementAtIndex(i);
            var owner = stage.FindPropertyRelative("hole").objectReferenceValue as HoleTrigger;
            var tee = stage.FindPropertyRelative("teePoint").objectReferenceValue as Transform;
            if (owner == null || tee == null) throw new System.InvalidOperationException("Missing hole or tee for stage " + number);
            GameObject cupPrefab = PrefabUtility.GetNearestPrefabInstanceRoot(owner.gameObject);
            Transform cup = cupPrefab != null ? cupPrefab.transform : owner.transform.parent;
            Transform holeRoot = cup;
            while (holeRoot != null && !holeRoot.name.StartsWith("Hole " + number + " -")) holeRoot = holeRoot.parent;
            if (holeRoot == null) throw new System.InvalidOperationException("Could not resolve course root for hole " + number);
            if (holeRoot.Find(Marker) != null) continue;

            var setup = new GameObject(Marker);
            setup.transform.SetParent(holeRoot, false);
            var platforms = new List<AeroMovingCupPlatform>();
            var switches = new List<AeroShotSwitch>();
            var mechanisms = new List<AeroReactiveMechanism>();
            if (number == 3 || number == 9)
                platforms.Add(CreateMovingDock(setup.transform, cup, owner, number == 3 ? 3.6f : 5f, number == 3 ? 19f : 23f));
            if (number == 6)
                platforms.Add(MoveCloudGreen(setup.transform, cup, owner, holeRoot));
            if (number == 2)
                InstallBridgeSwitches(setup.transform, holeRoot, tee, switches, mechanisms);
            if (number == 5)
                InstallPinballSwitch(setup.transform, holeRoot, tee, switches, mechanisms);
            if (number == 7)
                InstallShortcutSwitch(setup.transform, holeRoot, tee, switches, mechanisms);
            var coordinator = setup.AddComponent<AeroHoleMechanics>();
            coordinator.Configure(manager, owner, platforms.ToArray(), switches.ToArray(), mechanisms.ToArray());
            coordinator.ConfigureHint(number == 2 ? "Hit cyan targets to lift the bridge gates." : number == 5 ? "Hit the target to clear the center bumper lane." : number == 7 ? "Hit the target to open the leap-ramp shortcut." : number == 6 ? "The green drifts - aim for where it will be." : "Time your chip onto the moving green.");
            upgraded++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Interactive course installed: " + upgraded + " upgraded holes. Moving greens: holes 3, 6, 9. Shot switches: bridge gates on 2, retracting bumper lane on 5, shortcut gate on 7.");
    }

    private static AeroMovingCupPlatform CreateMovingDock(Transform setup, Transform cup, HoleTrigger owner, float travel, float period)
    {
        Vector3 original = cup.position;
        float cupScale = Mathf.Max(Mathf.Abs(cup.lossyScale.x), Mathf.Abs(cup.lossyScale.z));
        float opening = 1.065f * cupScale;
        // Fill the original stationary opening before relocating its cup onto the moving deck.
        var plug = MeshObject("Sealed Original Cup Opening", setup, Annulus(0f, 1.26f * cupScale, 0.35f, "CupOpeningPlug"), green, pearl, true);
        plug.transform.position = original - Vector3.up * 0.02f;
        plug.AddComponent<AeroSurface>().type = AeroSurfaceType.Green;
        var dock = new GameObject("Moving Cup Dock");
        dock.transform.SetParent(setup, false);
        dock.transform.position = original + Vector3.up * 1.35f - setup.right * (travel * 0.5f);
        var deck = MeshObject("Perforated Putting Deck", dock.transform, Annulus(opening, 3.8f, 0.60f, "MovingPuttingDeck"), green, pearl, true);
        deck.AddComponent<AeroSurface>().type = AeroSurfaceType.Green;
        var trim = MeshObject("Aqua Deck Rim", dock.transform, Annulus(3.68f, 3.83f, 0.12f, "MovingDeckTrim"), aqua, pearl, false);
        trim.transform.localPosition = Vector3.down * 0.05f;
        cup.SetParent(dock.transform, true);
        cup.position = dock.transform.position + Vector3.up * (0.02f * cup.lossyScale.y);
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f;
            Primitive(PrimitiveType.Sphere, "Deck Running Light", dock.transform,
                new Vector3(Mathf.Cos(angle) * 3.72f, -0.18f, Mathf.Sin(angle) * 3.72f), Vector3.one * 0.12f, lamp);
        }
        var motion = dock.AddComponent<AeroMovingCupPlatform>();
        motion.ConfigureWorldTravel(setup.right * travel, period, 0f, owner);
        Track(setup, dock.transform.position - Vector3.up * 0.72f, dock.transform.position + setup.right * travel - Vector3.up * 0.72f);
        return motion;
    }

    private static AeroMovingCupPlatform MoveCloudGreen(Transform setup, Transform cup, HoleTrigger owner, Transform holeRoot)
    {
        Transform island = Find(holeRoot, "Green Isle");
        if (island == null) throw new System.InvalidOperationException("Cloud Hop's Green Isle was not found.");
        var platform = new GameObject("Drifting Green Isle");
        platform.transform.SetParent(setup, false);
        platform.transform.position = cup.position;
        var ridingTrees = new List<Transform>();
        foreach (Transform child in island.parent)
        {
            Vector3 delta = child.position - cup.position;
            if (child.name == "Tree" && Mathf.Abs(delta.y) < 0.5f && new Vector2(delta.x, delta.z).magnitude < 14f)
                ridingTrees.Add(child);
        }
        foreach (Transform tree in ridingTrees) tree.SetParent(platform.transform, true);
        island.SetParent(platform.transform, true);
        cup.SetParent(platform.transform, true);
        platform.transform.position -= setup.right * 1.6f;
        var motion = platform.AddComponent<AeroMovingCupPlatform>();
        motion.ConfigureWorldTravel(setup.right * 3.2f, 22f, 0f, owner);
        Track(setup, platform.transform.position - Vector3.up * 1.7f,
            platform.transform.position + setup.right * 3.2f - Vector3.up * 1.7f);
        return motion;
    }

    private static void InstallBridgeSwitches(Transform setup, Transform holeRoot, Transform tee,
        List<AeroShotSwitch> switches, List<AeroReactiveMechanism> mechanisms)
    {
        var sliders = holeRoot.GetComponentsInChildren<AeroMover>(true);
        int count = 0;
        foreach (var slider in sliders)
        {
            if (slider.name != "Bridge Slider") continue;
            GameObject gate = slider.gameObject;
            Vector3 slideWorld = gate.transform.parent != null
                ? gate.transform.parent.TransformVector(slider.localOffset)
                : slider.localOffset;
            // Sliders were authored at one edge of their sideways sweep. The reactive gate
            // must close in the middle of that sweep, over the actual bridge lane.
            gate.transform.position += slideWorld * 0.5f;
            Object.DestroyImmediate(slider);
            var mechanism = gate.AddComponent<AeroReactiveMechanism>();
            mechanism.Configure(Vector3.up * 3.6f, Vector3.zero, 1.25f);
            var light = Primitive(PrimitiveType.Sphere, "Gate Status Light", gate.transform,
                new Vector3(0f, 0.72f, 0f), Vector3.one * 0.3f, lamp).GetComponent<Renderer>();
            mechanism.ConfigureIndicators(new[] { light }, Idle, Active);
            Vector3 approach = Vector3.Cross(Vector3.up, slideWorld).normalized;
            if (Vector3.Dot(approach, tee.position - gate.transform.position) < 0f)
                approach = -approach;
            Vector3 position = gate.transform.position + approach * 4f;
            position.y = gate.transform.position.y + 0.22f;
            switches.Add(CreateSwitch("Bridge Gate Target " + (++count), setup, position,
                position + approach * 10f, new[] { mechanism }, gate.transform.position));
            mechanisms.Add(mechanism);
        }
        if (count != 2) throw new System.InvalidOperationException("Expected two bridge sliders, found " + count);
    }
    private static void InstallPinballSwitch(Transform setup, Transform holeRoot, Transform tee,
        List<AeroShotSwitch> switches, List<AeroReactiveMechanism> mechanisms)
    {
        Transform field = Find(holeRoot, "Bumper Field");
        if (field == null) throw new System.InvalidOperationException("Pinball Garden's bumpers were not found.");
        var lane = new GameObject("Retracting Center Bumper Lane");
        lane.transform.SetParent(setup, false);
        var selected = new List<Transform>();
        foreach (Transform bumper in field)
            if (Mathf.Abs(holeRoot.InverseTransformPoint(bumper.position).x) <= 2.6f) selected.Add(bumper);
        if (selected.Count == 0) throw new System.InvalidOperationException("No center bumpers found.");
        var lights = new List<Renderer>();
        foreach (Transform bumper in selected)
        {
            bumper.SetParent(lane.transform, true);
            foreach (Renderer r in bumper.GetComponentsInChildren<Renderer>(true)) if (r.name == "Glow") lights.Add(r);
        }
        var mechanism = lane.AddComponent<AeroReactiveMechanism>();
        mechanism.Configure(Vector3.down * 3.6f, Vector3.zero, 1.6f);
        mechanism.ConfigureIndicators(lights.ToArray(), Idle, Active);
        Vector3 basePoint = Ground(holeRoot, new Vector3(0f, 0f, 21f));
        switches.Add(CreateSwitch("Pinball Lane Target", setup, basePoint + Vector3.up * 0.72f, tee.position,
            new[] { mechanism }, Ground(holeRoot, new Vector3(0f, 0f, 29f))));
        mechanisms.Add(mechanism);
    }

    private static void InstallShortcutSwitch(Transform setup, Transform holeRoot, Transform tee,
        List<AeroShotSwitch> switches, List<AeroReactiveMechanism> mechanisms)
    {
        Vector3 basePoint = Ground(holeRoot, new Vector3(-9f, 0f, 15.3f));
        var gate = new GameObject("Leap Ramp Shortcut Gate");
        gate.transform.SetParent(setup, false);
        gate.transform.position = basePoint + Vector3.up * 0.8f;
        var collider = gate.AddComponent<BoxCollider>(); collider.size = new Vector3(4.5f, 1.45f, 0.35f);
        Primitive(PrimitiveType.Cube, "Aqua Gate Panel", gate.transform, Vector3.zero, collider.size, aqua);
        Primitive(PrimitiveType.Cube, "Pearl Gate Top", gate.transform, Vector3.up * 0.76f, new Vector3(4.7f, 0.14f, 0.45f), pearl);
        var status = Primitive(PrimitiveType.Sphere, "Shortcut Status Light", gate.transform, new Vector3(0f, 0.2f, -0.25f), new Vector3(0.4f, 0.4f, 0.15f), lamp).GetComponent<Renderer>();
        var mechanism = gate.AddComponent<AeroReactiveMechanism>();
        mechanism.Configure(Vector3.up * 3.8f, Vector3.zero, 1.4f);
        mechanism.ConfigureIndicators(new[] { status }, Idle, Active);
        for (int side = -1; side <= 1; side += 2)
        {
            var post = Primitive(PrimitiveType.Cylinder, "Shortcut Guide Post", setup, Vector3.zero, new Vector3(0.22f, 2.5f, 0.22f), pearl);
            post.transform.position = basePoint + holeRoot.right * (2.45f * side) + Vector3.up * 2.4f;
        }
        switches.Add(CreateSwitch("Shortcut Ramp Target", setup, Ground(holeRoot, new Vector3(-5.6f, 0f, 10f)) + Vector3.up * 0.72f,
            tee.position, new[] { mechanism }, basePoint));
        mechanisms.Add(mechanism);
    }

    private static AeroShotSwitch CreateSwitch(string name, Transform setup, Vector3 position, Vector3 lookAt,
        AeroReactiveMechanism[] targets, Vector3 destination)
    {
        var root = new GameObject(name);
        root.transform.SetParent(setup, false);
        root.transform.position = position;
        Vector3 direction = lookAt - position; direction.y = 0f;
        root.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.01f ? direction : -setup.forward);
        var solid = root.AddComponent<BoxCollider>(); solid.size = new Vector3(1.5f, 1.4f, 0.78f); solid.center = Vector3.forward * 0.04f;
        Primitive(PrimitiveType.Sphere, "Pearl Target Shell", root.transform, Vector3.zero, new Vector3(1.65f, 1.65f, 0.5f), pearl);
        var face = Primitive(PrimitiveType.Sphere, "Glowing Target Face", root.transform, new Vector3(0f, 0f, 0.32f), new Vector3(1.3f, 1.3f, 0.22f), lamp);
        Primitive(PrimitiveType.Sphere, "Bullseye", face.transform, new Vector3(0f, 0f, 0.58f), new Vector3(0.27f, 0.27f, 0.12f), pearl);
        var line = new GameObject("Glowing Circuit Link").AddComponent<LineRenderer>();
        line.transform.SetParent(setup, false);
        line.useWorldSpace = true; line.widthMultiplier = 0.06f; line.numCornerVertices = 6; line.numCapVertices = 6;
        line.sharedMaterial = rail; line.positionCount = 3;
        Vector3 start = position - Vector3.up * 0.68f;
        line.SetPositions(new[] { start, Vector3.Lerp(start, destination, 0.5f) + Vector3.up * 0.04f, destination + Vector3.up * 0.07f });
        var target = root.AddComponent<AeroShotSwitch>();
        target.Configure(targets, face.transform, new Renderer[] { face.GetComponent<Renderer>(), line }, Idle, Active);
        target.ConfigureAudio(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/Sfx/Powerup1.wav"),
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/Sfx/Bump1.wav"), 0.48f);
        return target;
    }

    private static Vector3 Ground(Transform holeRoot, Vector3 local)
    {
        Vector3 point = holeRoot.TransformPoint(local);
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 80f, Vector3.down, 160f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(holeRoot) || hit.normal.y < 0.65f) continue;
            string name = hit.collider.name;
            if (!name.EndsWith(" Ground") && name != "Green" && name != "Fairway" && name != "Cap") continue;
            best = Mathf.Max(best, hit.point.y);
        }
        if (float.IsNegativeInfinity(best)) throw new System.InvalidOperationException("No course ground at " + local + " on " + holeRoot.name);
        point.y = best;
        return point;
    }

    private static void Track(Transform parent, Vector3 a, Vector3 b)
    {
        var line = new GameObject("Moving Green Travel Rail").AddComponent<LineRenderer>();
        line.transform.SetParent(parent, false); line.useWorldSpace = true;
        line.sharedMaterial = rail; line.widthMultiplier = 0.11f; line.numCapVertices = 8;
        line.positionCount = 2; line.SetPositions(new[] { a, b });
        foreach (Vector3 point in new[] { a, b })
        {
            var marker = Primitive(PrimitiveType.Sphere, "Travel Endpoint", parent, Vector3.zero, Vector3.one * 0.35f, lamp);
            marker.transform.position = point;
        }
    }

    private static Transform Find(Transform parent, string name)
    {
        foreach (Transform item in parent.GetComponentsInChildren<Transform>(true)) if (item.name == name) return item;
        return null;
    }

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(type); obj.name = name;
        obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = scale;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.GetComponent<Renderer>().sharedMaterial = material;
        return obj;
    }

    private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material top, Material side, bool collision)
    {
        var obj = new GameObject(name); obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterials = new[] { top, side };
        if (collision) { var col = obj.AddComponent<MeshCollider>(); col.sharedMesh = mesh; col.sharedMaterial = puttingPhysics; }
        return obj;
    }

    private static Mesh Annulus(float inner, float outer, float depth, string name)
    {
        string path = Folder + "/" + name + ".asset";
        // Same geometry is intentionally shared between matching docks.
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EnsurePlanarUV(existing); return existing; }
        var vertices = new List<Vector3>(); var topTriangles = new List<int>(); var sideTriangles = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> triangles)
        {
            int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2); triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
        }
        const int segments = 96;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments, b = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 ia = new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner);
            Vector3 ib = new Vector3(Mathf.Cos(b) * inner, 0f, Mathf.Sin(b) * inner);
            Vector3 oa = new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer);
            Vector3 ob = new Vector3(Mathf.Cos(b) * outer, 0f, Mathf.Sin(b) * outer);
            Vector3 down = Vector3.down * depth;
            Quad(ia, ib, ob, oa, topTriangles);
            Quad(oa, ob, ob + down, oa + down, sideTriangles);
            Quad(ib, ia, ia + down, ib + down, sideTriangles);
            Quad(ia + down, oa + down, ob + down, ib + down, sideTriangles);
        }
        var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.subMeshCount = 2;
        mesh.SetTriangles(topTriangles, 0); mesh.SetTriangles(sideTriangles, 1); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        EnsurePlanarUV(mesh); AssetDatabase.CreateAsset(mesh, path); return mesh;
    }

    private static void EnsurePlanarUV(Mesh mesh)
    {
        if (mesh.uv.Length == mesh.vertexCount) return;
        Vector3[] vertices = mesh.vertices;
        var uv = new Vector2[vertices.Length];
        for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(vertices[i].x * 0.5f, vertices[i].z * 0.5f);
        mesh.uv = uv;
        mesh.RecalculateTangents();
        EditorUtility.SetDirty(mesh);
    }
    private static void PrepareAssets()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/FrutigerGolfCourse", "InteractiveCourse");
        pearl = Material("Pearl", new Color(0.91f, 0.98f, 1f), 0.7f, false);
        green = Material("PuttingMint", new Color(0.26f, 0.78f, 0.43f), 0.3f, false);
        aqua = Material("Aqua", new Color(0.08f, 0.66f, 0.88f), 0.85f, false);
        lamp = Material("StatusGlow", Idle, 0.9f, true);
        rail = Material("CircuitGlow", Idle, 0.6f, true);
        var courseTurf = AssetDatabase.LoadAssetAtPath<Material>("Assets/FrutigerGolfCourse/NineHole/Materials/Float_Green.mat");
        if (courseTurf != null)
        {
            green.CopyPropertiesFromMaterial(courseTurf);
            green.SetFloat("_Smoothness", 0.28f);
            EditorUtility.SetDirty(green);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder }))
        {
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
            if (saved != null) EnsurePlanarUV(saved);
        }
        foreach (Material glowing in new[] { lamp, rail })
        {
            glowing.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            glowing.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(glowing);
        }
        puttingPhysics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder + "/MovingGreenPhysics.asset");
        if (puttingPhysics == null)
        {
            puttingPhysics = new PhysicsMaterial("Moving Green Grip") { dynamicFriction = 0.65f, staticFriction = 0.8f, bounciness = 0.08f,
                frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum };
            AssetDatabase.CreateAsset(puttingPhysics, Folder + "/MovingGreenPhysics.asset");
        }
    }

    private static Material Material(string name, Color color, float smoothness, bool glow)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.name = name;
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", smoothness);
        if (glow) { material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.5f); }
        AssetDatabase.CreateAsset(material, path); return material;
    }
}







