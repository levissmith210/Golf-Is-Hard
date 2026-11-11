using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(GolfHoleAutoCutter))]
public sealed class GolfHoleAutoCutterInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GolfHoleAutoCutter cutter = (GolfHoleAutoCutter)target;
        EditorGUILayout.Space(6f);

        if (cutter.HasCutSurface)
        {
            EditorGUILayout.HelpBox("A compatible platform surface has been cut for this hole.", MessageType.Info);
        }
        else
        {
            if (!string.IsNullOrEmpty(cutter.LastCutMessage))
                EditorGUILayout.HelpBox(cutter.LastCutMessage, MessageType.Warning);
            else if (EditorUtility.IsPersistent(cutter))
                EditorGUILayout.HelpBox(
                    "Place this prefab instance over a square platform with a BoxCollider, MeshFilter, and MeshRenderer. It will cut after you drop or move it into place.",
                    MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Move the hole flush onto a compatible square platform. It will cut automatically when it settles. You can also retry the cut below.",
                    MessageType.Info);

            using (new EditorGUI.DisabledScope(!cutter.CutBoxSurfaceOnPlacement || EditorUtility.IsPersistent(cutter)))
            {
                if (GUILayout.Button("Cut Box Surface Under Hole"))
                {
                    GolfHoleSurfaceAutoCutEditor.TryCutUnderHole(cutter);
                    Repaint();
                }
            }
        }

        if (cutter.HasCutSurface)
        {
            using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(cutter)))
            {
                if (GUILayout.Button("Snap Hole to Cut Opening"))
                {
                    GolfHoleSurfaceAutoCutEditor.SnapToClosestExistingOpening(cutter);
                    Repaint();
                }
            }
        }
    }
}

[InitializeOnLoad]
public static class GolfHoleSurfaceAutoCutEditor
{
    private const string CutObjectName = "GolfHoleSurfaceCut";
    private const string MeshFolder = "Assets/Models/FrutigerAeroGolfHole/GeneratedCuts";
    private const int RingSegments = 128;
    private const float PlacementTolerance = 0.15f;
    private const float PlatformEdgeMargin = 0.015f;
    private const float TransformEpsilon = 0.00001f;

    private sealed class PlacementWatch
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public double nextAttemptTime;

        public PlacementWatch(Transform target, double attemptTime)
        {
            Capture(target);
            nextAttemptTime = attemptTime;
        }

        public bool HasMoved(Transform target)
        {
            return (target.position - position).sqrMagnitude > TransformEpsilon * TransformEpsilon ||
                   Quaternion.Angle(target.rotation, rotation) > 0.05f ||
                   (target.lossyScale - scale).sqrMagnitude > TransformEpsilon * TransformEpsilon;
        }

        public void Capture(Transform target)
        {
            position = target.position;
            rotation = target.rotation;
            scale = target.lossyScale;
        }
    }

    private static readonly Dictionary<int, PlacementWatch> placementWatches =
        new Dictionary<int, PlacementWatch>();
    private static double nextSceneScanTime;

    static GolfHoleSurfaceAutoCutEditor()
    {
        SeedExistingHoleInstances();
        EditorApplication.hierarchyChanged += QueueSceneScan;
        EditorApplication.update += ScanSceneForPlacementChanges;
    }

    private static void SeedExistingHoleInstances()
    {
        GolfHoleAutoCutter[] existing = Resources.FindObjectsOfTypeAll<GolfHoleAutoCutter>();
        foreach (GolfHoleAutoCutter cutter in existing)
        {
            if (cutter == null || !cutter.gameObject.scene.IsValid() || EditorUtility.IsPersistent(cutter))
                continue;

            placementWatches[cutter.GetInstanceID()] =
                new PlacementWatch(cutter.transform, double.PositiveInfinity);
        }
    }

    private static void QueueSceneScan()
    {
        EditorApplication.delayCall -= ScanSceneForPlacementChanges;
        EditorApplication.delayCall += ScanSceneForPlacementChanges;
    }

    private static void ScanSceneForPlacementChanges()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        double now = EditorApplication.timeSinceStartup;
        if (now < nextSceneScanTime)
            return;
        nextSceneScanTime = now + 0.2d;

        GolfHoleAutoCutter[] cutters = Resources.FindObjectsOfTypeAll<GolfHoleAutoCutter>();
        HashSet<int> liveInstances = new HashSet<int>();
        foreach (GolfHoleAutoCutter cutter in cutters)
        {
            if (cutter == null || !cutter.gameObject.scene.IsValid() || EditorUtility.IsPersistent(cutter))
                continue;

            int instanceId = cutter.GetInstanceID();
            liveInstances.Add(instanceId);
            if (cutter.HasCutSurface || !cutter.CutBoxSurfaceOnPlacement)
                continue;

            if (!placementWatches.TryGetValue(instanceId, out PlacementWatch watch))
            {
                placementWatches[instanceId] = new PlacementWatch(cutter.transform, now + 0.25d);
                continue;
            }

            if (watch.HasMoved(cutter.transform))
            {
                watch.Capture(cutter.transform);
                watch.nextAttemptTime = now + 0.25d;
                continue;
            }

            if (now < watch.nextAttemptTime)
                continue;

            if (TryCutUnderHole(cutter))
                watch.nextAttemptTime = double.PositiveInfinity;
            else
                watch.nextAttemptTime = now + 0.5d;
        }

        if (placementWatches.Count == liveInstances.Count)
            return;

        List<int> removed = new List<int>();
        foreach (KeyValuePair<int, PlacementWatch> entry in placementWatches)
        {
            if (!liveInstances.Contains(entry.Key))
                removed.Add(entry.Key);
        }
        for (int i = 0; i < removed.Count; i++)
            placementWatches.Remove(removed[i]);
    }

    public static bool SnapToClosestExistingOpening(GolfHoleAutoCutter hole)
    {
        if (hole == null || !hole.gameObject.scene.IsValid() || EditorUtility.IsPersistent(hole))
            return false;

        MeshCollider[] cutSurfaces = Resources.FindObjectsOfTypeAll<MeshCollider>();
        Transform closestPlatform = null;
        Vector3 closestPosition = Vector3.zero;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < cutSurfaces.Length; i++)
        {
            MeshCollider cutSurface = cutSurfaces[i];
            if (cutSurface == null || cutSurface.name != CutObjectName ||
                cutSurface.gameObject.scene != hole.gameObject.scene ||
                !cutSurface.gameObject.activeInHierarchy || cutSurface.transform.parent == null)
                continue;

            Transform platform = cutSurface.transform.parent;
            BoxCollider box = platform.GetComponent<BoxCollider>();
            if (box == null)
                continue;

            Vector3 localTop = new Vector3(box.center.x, box.center.y + box.size.y * 0.5f, box.center.z);
            Vector3 candidatePosition = platform.TransformPoint(localTop);
            candidatePosition += Vector3.up * (0.02f * Mathf.Abs(hole.transform.lossyScale.y));
            float distance = (candidatePosition - hole.transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlatform = platform;
                closestPosition = candidatePosition;
            }
        }

        if (closestPlatform == null)
        {
            Debug.LogWarning("No existing golf-hole cut opening was found in this scene.", hole);
            return false;
        }

        Undo.RecordObject(hole.transform, "Align Golf Hole to Existing Opening");
        hole.transform.position = closestPosition;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hole.transform);
        EditorSceneManager.MarkSceneDirty(hole.gameObject.scene);
        hole.SetCutMessage(null);
        Debug.Log("Aligned golf hole '" + hole.name + "' to the cut opening on '" + closestPlatform.name + "'.", hole);
        return true;
    }

    [MenuItem("Tools/Golf is Hard/Cut Terrain Under Course Holes")]
    public static void CutCourseTerrainForGolfHoles()
    {
        const string courseScenePath = "Assets/Scenes/NineHoleCourse.unity";
        const float openingRadius = 0.90f;
        const float previousOpeningRadius = 1.12f;

        Scene courseScene = SceneManager.GetSceneByPath(courseScenePath);
        bool openedScene = !courseScene.IsValid() || !courseScene.isLoaded;
        if (openedScene)
            courseScene = EditorSceneManager.OpenScene(courseScenePath, OpenSceneMode.Additive);

        try
        {
            Terrain courseTerrain = null;
            Terrain[] terrains = Resources.FindObjectsOfTypeAll<Terrain>();
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain candidate = terrains[i];
                if (candidate != null && candidate.gameObject.scene == courseScene &&
                    candidate.name == "Course Terrain")
                {
                    courseTerrain = candidate;
                    break;
                }
            }

            if (courseTerrain == null || courseTerrain.terrainData == null)
            {
                Debug.LogError("Could not find Course Terrain with TerrainData in NineHoleCourse.");
                return;
            }

            TerrainData data = courseTerrain.terrainData;
            int resolution = data.holesResolution;
            if (resolution < 2)
            {
                Debug.LogError("Course Terrain does not have a usable holes resolution.", courseTerrain);
                return;
            }

            List<Transform> courseHoles = new List<Transform>();
            GameObject[] roots = courseScene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] descendants = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int childIndex = 0; childIndex < descendants.Length; childIndex++)
                {
                    Transform candidate = descendants[childIndex];
                    if (candidate.name.Length == 7 && candidate.name.EndsWith(" Hole") &&
                        int.TryParse(candidate.name.Substring(0, 2), out int holeNumber) &&
                        holeNumber >= 1 && holeNumber <= 9)
                        courseHoles.Add(candidate);
                }
            }
            courseHoles.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            if (courseHoles.Count != 9)
            {
                Debug.LogError("Expected nine numbered hole markers in NineHoleCourse, found " + courseHoles.Count + ".");
                return;
            }

            bool[,] holes = data.GetHoles(0, 0, resolution, resolution);
            Vector3 terrainSize = data.size;
            Vector3 terrainScale = courseTerrain.transform.lossyScale;
            float scaleX = Mathf.Max(0.0001f, Mathf.Abs(terrainScale.x));
            float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(terrainScale.z));
            float cellX = terrainSize.x / resolution;
            float cellZ = terrainSize.z / resolution;
            int cutCells = 0;
            int restoredCells = 0;
            int processedHoles = 0;
            List<string> skipped = new List<string>();

            for (int holeIndex = 0; holeIndex < courseHoles.Count; holeIndex++)
            {
                Transform holeTransform = courseHoles[holeIndex];
                Transform[] descendants = holeTransform.GetComponentsInChildren<Transform>(true);
                for (int childIndex = 0; childIndex < descendants.Length; childIndex++)
                {
                    if (descendants[childIndex] != holeTransform &&
                        descendants[childIndex].name == "Cup, Liner and Flag")
                    {
                        holeTransform = descendants[childIndex];
                        break;
                    }
                }

                Vector3 local = courseTerrain.transform.InverseTransformPoint(holeTransform.position);
                float previousLocalRadiusX = previousOpeningRadius / scaleX;
                float previousLocalRadiusZ = previousOpeningRadius / scaleZ;
                if (local.x < previousLocalRadiusX || local.x > terrainSize.x - previousLocalRadiusX ||
                    local.z < previousLocalRadiusZ || local.z > terrainSize.z - previousLocalRadiusZ)
                {
                    skipped.Add(holeTransform.name + " is outside the terrain edge");
                    continue;
                }

                int centerX = Mathf.Clamp(Mathf.RoundToInt(local.x / terrainSize.x * resolution), 0, resolution - 1);
                int centerZ = Mathf.Clamp(Mathf.RoundToInt(local.z / terrainSize.z * resolution), 0, resolution - 1);
                int radiusX = Mathf.CeilToInt(previousLocalRadiusX / cellX);
                int radiusZ = Mathf.CeilToInt(previousLocalRadiusZ / cellZ);
                int minX = Mathf.Max(0, centerX - radiusX);
                int maxX = Mathf.Min(resolution - 1, centerX + radiusX);
                int minZ = Mathf.Max(0, centerZ - radiusZ);
                int maxZ = Mathf.Min(resolution - 1, centerZ + radiusZ);
                float openingRadiusSq = openingRadius * openingRadius;
                float previousRadiusSq = previousOpeningRadius * previousOpeningRadius;

                for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - centerX) * cellX * scaleX;
                    float dz = (z - centerZ) * cellZ * scaleZ;
                    float distanceSq = dx * dx + dz * dz;
                    if (distanceSq <= openingRadiusSq)
                    {
                        if (holes[z, x])
                        {
                            holes[z, x] = false;
                            cutCells++;
                        }
                    }
                    else if (distanceSq <= previousRadiusSq && !holes[z, x])
                    {
                        holes[z, x] = true;
                        restoredCells++;
                    }
                }
                processedHoles++;
            }

            if (cutCells > 0 || restoredCells > 0)
            {
                Undo.RegisterCompleteObjectUndo(data, "Fit Nine Golf Holes to Course Terrain");
                data.SetHoles(0, 0, holes);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                courseTerrain.Flush();
            }

            string report = "Fitted terrain openings for " + processedHoles + "/" + courseHoles.Count +
                            " course holes (radius " + openingRadius.ToString("0.00") +
                            ", terrain holes resolution " + resolution + "). Cut cells: " + cutCells +
                            "; restored oversized edge cells: " + restoredCells + ".";
            if (skipped.Count > 0)
                report += " Skipped: " + string.Join("; ", skipped.ToArray()) + ".";
            Debug.Log(report, courseTerrain);
        }
        finally
        {
            if (openedScene && courseScene.IsValid() && courseScene.isLoaded)
                EditorSceneManager.CloseScene(courseScene, true);
        }
    }
    public static bool TryCutUnderHole(GolfHoleAutoCutter hole)
    {
        if (hole == null || EditorApplication.isPlayingOrWillChangePlaymode ||
            !hole.gameObject.scene.IsValid() || EditorUtility.IsPersistent(hole))
            return false;

        if (hole.HasCutSurface)
            return true;

        if (!hole.CutBoxSurfaceOnPlacement)
        {
            hole.SetCutMessage("Automatic cutting is turned off for this hole. Enable Cut Box Surface On Placement in the Inspector.");
            return false;
        }

        BoxCollider box = FindBoxSurface(hole, out Bounds visibleBounds,
            out Vector3 holeOffset, out float radiusX, out float radiusZ, out string failureReason);
        if (box == null)
        {
            hole.SetCutMessage(failureReason);
            return false;
        }

        GameObject target = box.gameObject;
        MeshFilter sourceFilter = target.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = target.GetComponent<MeshRenderer>();
        if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null)
        {
            hole.SetCutMessage("The platform needs a MeshFilter and MeshRenderer on the same object as its BoxCollider.");
            return false;
        }

        Collider[] colliders = target.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != box && collider.enabled && !collider.isTrigger)
            {
                hole.SetCutMessage("Remove or disable the platform's extra solid collider before cutting its opening.");
                return false;
            }
        }

        Mesh cutMesh = CreateBoxWithCircularOpening(visibleBounds, holeOffset, radiusX, radiusZ);
        if (cutMesh == null)
        {
            hole.SetCutMessage("The platform is too thin to cut through. Increase its thickness, then move the hole again.");
            return false;
        }

        EnsureFolder(MeshFolder);
        string safeName = SanitizeFileName(target.name);
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(
            MeshFolder + "/" + safeName + "_GolfHoleCut.asset");
        AssetDatabase.CreateAsset(cutMesh, meshPath);
        Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (savedMesh == null)
        {
            hole.SetCutMessage("Unity could not save the cut platform mesh. Try the cut button again.");
            return false;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Cut Golf Hole Opening");

        Undo.RecordObject(sourceRenderer, "Hide Original Platform Surface");
        Undo.RecordObject(box, "Replace Platform Collider");
        sourceRenderer.enabled = false;
        box.enabled = false;

        GameObject cutSurface = new GameObject(CutObjectName);
        Undo.RegisterCreatedObjectUndo(cutSurface, "Create Golf Hole Opening");
        cutSurface.transform.SetParent(target.transform, false);
        cutSurface.transform.localPosition = Vector3.zero;
        cutSurface.transform.localRotation = Quaternion.identity;
        cutSurface.transform.localScale = Vector3.one;
        cutSurface.layer = target.layer;
        cutSurface.isStatic = target.isStatic;

        MeshFilter cutFilter = Undo.AddComponent<MeshFilter>(cutSurface);
        cutFilter.sharedMesh = savedMesh;
        MeshRenderer cutRenderer = Undo.AddComponent<MeshRenderer>(cutSurface);
        cutRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        cutRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        cutRenderer.receiveShadows = sourceRenderer.receiveShadows;
        cutRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        cutRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;

        MeshCollider cutCollider = Undo.AddComponent<MeshCollider>(cutSurface);
        cutCollider.sharedMesh = savedMesh;
        cutCollider.convex = false;

        Undo.RecordObject(hole, "Record Golf Hole Cut");
        hole.MarkSurfaceCut();

        EditorUtility.SetDirty(sourceRenderer);
        EditorUtility.SetDirty(box);
        EditorUtility.SetDirty(cutFilter);
        EditorUtility.SetDirty(cutRenderer);
        EditorUtility.SetDirty(cutCollider);
        EditorSceneManager.MarkSceneDirty(target.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log("Cut a circular opening under golf hole '" + hole.name + "' in '" + target.name + "'.", hole);
        return true;
    }

    private static BoxCollider FindBoxSurface(GolfHoleAutoCutter hole, out Bounds visibleBounds,
        out Vector3 holeOffset, out float radiusX, out float radiusZ, out string failureReason)
    {
        visibleBounds = default;
        holeOffset = Vector3.zero;
        radiusX = 0f;
        radiusZ = 0f;
        failureReason = "Place the hole directly over a platform with a BoxCollider, MeshFilter, and MeshRenderer on the same object.";

        BoxCollider[] candidates = Resources.FindObjectsOfTypeAll<BoxCollider>();
        BoxCollider best = null;
        float bestDistance = float.PositiveInfinity;
        float tooSmallDistance = float.PositiveInfinity;
        string tooSmallMessage = null;

        for (int i = 0; i < candidates.Length; i++)
        {
            BoxCollider candidate = candidates[i];
            if (candidate == null || candidate.isTrigger || !candidate.enabled ||
                !candidate.gameObject.activeInHierarchy ||
                candidate.gameObject.scene != hole.gameObject.scene ||
                candidate.transform == hole.transform || candidate.transform.IsChildOf(hole.transform) ||
                !TryGetVisibleBoxBounds(candidate, out Bounds candidateBounds))
                continue;

            Transform surface = candidate.transform;
            Vector3 localUp = surface.InverseTransformDirection(hole.transform.up).normalized;
            if (Vector3.Dot(localUp, Vector3.up) < 0.985f)
                continue;

            Vector3 localHole = surface.InverseTransformPoint(hole.transform.position);
            float worldYDistance = Mathf.Abs(localHole.y - candidateBounds.max.y) * Mathf.Abs(surface.lossyScale.y);
            if (worldYDistance > PlacementTolerance)
                continue;

            Vector3 offset = localHole - candidateBounds.center;
            if (Mathf.Abs(offset.x) > candidateBounds.extents.x || Mathf.Abs(offset.z) > candidateBounds.extents.z)
                continue;

            GetTargetLocalOpeningRadii(hole, surface, out float candidateRadiusX, out float candidateRadiusZ,
                out float scaleX, out float scaleZ);
            if (candidateRadiusX <= 0f || candidateRadiusZ <= 0f || scaleX < 0.0001f || scaleZ < 0.0001f)
                continue;

            float marginX = PlatformEdgeMargin / scaleX;
            float marginZ = PlatformEdgeMargin / scaleZ;
            bool fits = Mathf.Abs(offset.x) + candidateRadiusX + marginX <= candidateBounds.extents.x &&
                        Mathf.Abs(offset.z) + candidateRadiusZ + marginZ <= candidateBounds.extents.z;
            if (!fits)
            {
                if (worldYDistance < tooSmallDistance)
                {
                    tooSmallDistance = worldYDistance;
                    float worldRadius = hole.CutRadius * Mathf.Max(
                        Mathf.Abs(hole.transform.lossyScale.x), Mathf.Abs(hole.transform.lossyScale.z));
                    Vector3 worldSize = Vector3.Scale(candidateBounds.size, new Vector3(scaleX, 1f, scaleZ));
                    tooSmallMessage = "This platform is too small for the hole opening. The hole needs about " +
                        (worldRadius * 2f + PlatformEdgeMargin * 2f).ToString("0.00") +
                        " units of clear width; this platform is " + worldSize.x.ToString("0.00") + " x " +
                        worldSize.z.ToString("0.00") + ". Enlarge the platform or reduce the Golf Hole object's scale.";
                }
                continue;
            }

            if (worldYDistance < bestDistance)
            {
                bestDistance = worldYDistance;
                best = candidate;
                visibleBounds = candidateBounds;
                holeOffset = offset;
                radiusX = candidateRadiusX;
                radiusZ = candidateRadiusZ;
            }
        }

        if (best != null)
            return best;

        if (!string.IsNullOrEmpty(tooSmallMessage))
            failureReason = tooSmallMessage;
        return null;
    }

    private static bool TryGetVisibleBoxBounds(BoxCollider box, out Bounds visibleBounds)
    {
        visibleBounds = default;
        MeshFilter filter = box.GetComponent<MeshFilter>();
        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        if (filter == null || renderer == null || filter.sharedMesh == null)
            return false;

        visibleBounds = filter.sharedMesh.bounds;
        const float tolerance = 0.01f;
        return (visibleBounds.center - box.center).sqrMagnitude <= tolerance * tolerance &&
               (visibleBounds.size - box.size).sqrMagnitude <= tolerance * tolerance;
    }

    private static void GetTargetLocalOpeningRadii(GolfHoleAutoCutter hole, Transform target,
        out float radiusX, out float radiusZ, out float scaleX, out float scaleZ)
    {
        scaleX = Mathf.Abs(target.lossyScale.x);
        scaleZ = Mathf.Abs(target.lossyScale.z);
        float holeScale = Mathf.Max(Mathf.Abs(hole.transform.lossyScale.x),
            Mathf.Abs(hole.transform.lossyScale.z));
        float worldRadius = hole.CutRadius * holeScale;

        radiusX = scaleX > 0.0001f ? worldRadius / scaleX : 0f;
        radiusZ = scaleZ > 0.0001f ? worldRadius / scaleZ : 0f;
    }

    private static Mesh CreateBoxWithCircularOpening(Bounds bounds, Vector3 holeOffset,
        float radiusX, float radiusZ)
    {
        Vector3 size = bounds.size;
        if (size.x <= radiusX * 2f || size.z <= radiusZ * 2f || size.y <= 0f)
            return null;

        float left = bounds.min.x;
        float right = bounds.max.x;
        float front = bounds.min.z;
        float back = bounds.max.z;
        float bottom = bounds.min.y;
        float top = bounds.max.y;
        Vector2 center = new Vector2(holeOffset.x, holeOffset.z);

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        AddAnnularSurface(vertices, uvs, triangles, center, radiusX, radiusZ, left, right, front, back, top, true);
        AddAnnularSurface(vertices, uvs, triangles, center, radiusX, radiusZ, left, right, front, back, bottom, false);
        AddInnerWall(vertices, uvs, triangles, center, radiusX, radiusZ, top, bottom, bounds.center, size);
        AddOuterWalls(vertices, uvs, triangles, left, right, front, back, bottom, top);

        Mesh mesh = new Mesh
        {
            name = "GolfHoleCut",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddAnnularSurface(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector2 center, float radiusX, float radiusZ, float left, float right, float front, float back,
        float y, bool facesUp)
    {
        float halfX = (right - left) * 0.5f;
        float halfZ = (back - front) * 0.5f;
        float centerX = (left + right) * 0.5f + center.x;
        float centerZ = (front + back) * 0.5f + center.y;
        const float fullTurn = Mathf.PI * 2f;

        // Add exact corner directions so the annulus ends at the actual square corners.
        List<float> angles = new List<float>(RingSegments + 4);
        for (int i = 0; i < RingSegments; i++)
            angles.Add(fullTurn * i / RingSegments);

        Vector2[] corners =
        {
            new Vector2(-halfX, -halfZ), new Vector2(-halfX, halfZ),
            new Vector2(halfX, -halfZ), new Vector2(halfX, halfZ)
        };
        for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
        {
            Vector2 fromCenter = corners[cornerIndex] - center;
            float cornerAngle = Mathf.Atan2(fromCenter.y, fromCenter.x);
            if (cornerAngle < 0f)
                cornerAngle += fullTurn;

            bool alreadyPresent = false;
            for (int angleIndex = 0; angleIndex < angles.Count; angleIndex++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(angles[angleIndex] * Mathf.Rad2Deg,
                        cornerAngle * Mathf.Rad2Deg)) < 0.001f)
                {
                    alreadyPresent = true;
                    break;
                }
            }
            if (!alreadyPresent)
                angles.Add(cornerAngle);
        }
        angles.Sort();

        for (int i = 0; i < angles.Count; i++)
        {
            float angleA = angles[i];
            float angleB = i + 1 < angles.Count ? angles[i + 1] : angles[0] + fullTurn;
            Vector2 dirA = new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA));
            Vector2 dirB = new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB));
            float outerA = RayDistanceToBox(center, dirA, halfX, halfZ);
            float outerB = RayDistanceToBox(center, dirB, halfX, halfZ);

            Vector3 outerPointA = new Vector3(centerX + dirA.x * outerA, y, centerZ + dirA.y * outerA);
            Vector3 outerPointB = new Vector3(centerX + dirB.x * outerB, y, centerZ + dirB.y * outerB);
            Vector3 innerPointA = new Vector3(centerX + dirA.x * radiusX, y, centerZ + dirA.y * radiusZ);
            Vector3 innerPointB = new Vector3(centerX + dirB.x * radiusX, y, centerZ + dirB.y * radiusZ);

            Vector2 uvOuterA = new Vector2((outerPointA.x - left) / (right - left), (outerPointA.z - front) / (back - front));
            Vector2 uvOuterB = new Vector2((outerPointB.x - left) / (right - left), (outerPointB.z - front) / (back - front));
            Vector2 uvInnerA = new Vector2((innerPointA.x - left) / (right - left), (innerPointA.z - front) / (back - front));
            Vector2 uvInnerB = new Vector2((innerPointB.x - left) / (right - left), (innerPointB.z - front) / (back - front));

            if (facesUp)
            {
                AddTriangleFacing(vertices, uvs, triangles, outerPointA, innerPointA, innerPointB,
                    uvOuterA, uvInnerA, uvInnerB, Vector3.up);
                AddTriangleFacing(vertices, uvs, triangles, outerPointA, innerPointB, outerPointB,
                    uvOuterA, uvInnerB, uvOuterB, Vector3.up);
            }
            else
            {
                AddTriangleFacing(vertices, uvs, triangles, outerPointA, innerPointB, innerPointA,
                    uvOuterA, uvInnerB, uvInnerA, Vector3.down);
                AddTriangleFacing(vertices, uvs, triangles, outerPointA, outerPointB, innerPointB,
                    uvOuterA, uvOuterB, uvInnerB, Vector3.down);
            }
        }
    }

    private static float RayDistanceToBox(Vector2 center, Vector2 direction, float halfX, float halfZ)
    {
        float tx = direction.x > 0.00001f
            ? (halfX - center.x) / direction.x
            : direction.x < -0.00001f ? (-halfX - center.x) / direction.x : float.PositiveInfinity;
        float tz = direction.y > 0.00001f
            ? (halfZ - center.y) / direction.y
            : direction.y < -0.00001f ? (-halfZ - center.y) / direction.y : float.PositiveInfinity;
        return Mathf.Min(tx, tz);
    }

    private static void AddInnerWall(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector2 center, float radiusX, float radiusZ, float top, float bottom, Vector3 boxCenter, Vector3 boxSize)
    {
        float centerX = boxCenter.x + center.x;
        float centerZ = boxCenter.z + center.y;

        for (int i = 0; i < RingSegments; i++)
        {
            float angleA = Mathf.PI * 2f * i / RingSegments;
            float angleB = Mathf.PI * 2f * (i + 1) / RingSegments;
            float cosineA = Mathf.Cos(angleA);
            float sineA = Mathf.Sin(angleA);
            float cosineB = Mathf.Cos(angleB);
            float sineB = Mathf.Sin(angleB);
            float xA = centerX + cosineA * radiusX;
            float zA = centerZ + sineA * radiusZ;
            float xB = centerX + cosineB * radiusX;
            float zB = centerZ + sineB * radiusZ;

            Vector3 topA = new Vector3(xA, top, zA);
            Vector3 bottomA = new Vector3(xA, bottom, zA);
            Vector3 topB = new Vector3(xB, top, zB);
            Vector3 bottomB = new Vector3(xB, bottom, zB);
            Vector2 uvTopA = new Vector2(i / (float)RingSegments, 1f);
            Vector2 uvBottomA = new Vector2(i / (float)RingSegments, 0f);
            Vector2 uvTopB = new Vector2((i + 1f) / RingSegments, 1f);
            Vector2 uvBottomB = new Vector2((i + 1f) / RingSegments, 0f);
            Vector3 wallNormal = new Vector3(-cosineA / radiusX, 0f, -sineA / radiusZ).normalized;

            AddTriangleFacing(vertices, uvs, triangles, topA, bottomA, bottomB,
                uvTopA, uvBottomA, uvBottomB, wallNormal);
            AddTriangleFacing(vertices, uvs, triangles, topA, bottomB, topB,
                uvTopA, uvBottomB, uvTopB, wallNormal);
        }
    }

    private static void AddOuterWalls(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        float left, float right, float front, float back, float bottom, float top)
    {
        AddQuadFacing(vertices, uvs, triangles,
            new Vector3(right, bottom, front), new Vector3(right, bottom, back),
            new Vector3(right, top, back), new Vector3(right, top, front), Vector3.right);
        AddQuadFacing(vertices, uvs, triangles,
            new Vector3(left, bottom, back), new Vector3(left, bottom, front),
            new Vector3(left, top, front), new Vector3(left, top, back), Vector3.left);
        AddQuadFacing(vertices, uvs, triangles,
            new Vector3(left, bottom, back),
            new Vector3(right, bottom, back), new Vector3(right, top, back),
            new Vector3(left, top, back), Vector3.forward);
        AddQuadFacing(vertices, uvs, triangles,
            new Vector3(right, bottom, front), new Vector3(left, bottom, front),
            new Vector3(left, top, front), new Vector3(right, top, front), Vector3.back);
    }

    private static void AddQuadFacing(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 desiredNormal)
    {
        bool isXFace = Mathf.Abs(desiredNormal.x) > 0.5f;
        Vector2 uvA = isXFace ? new Vector2(a.z, a.y) : new Vector2(a.x, a.y);
        Vector2 uvB = isXFace ? new Vector2(b.z, b.y) : new Vector2(b.x, b.y);
        Vector2 uvC = isXFace ? new Vector2(c.z, c.y) : new Vector2(c.x, c.y);
        Vector2 uvD = isXFace ? new Vector2(d.z, d.y) : new Vector2(d.x, d.y);

        AddTriangleFacing(vertices, uvs, triangles, a, b, c, uvA, uvB, uvC, desiredNormal);
        AddTriangleFacing(vertices, uvs, triangles, a, c, d, uvA, uvC, uvD, desiredNormal);
    }

    private static void AddTriangleFacing(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector3 desiredNormal)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), desiredNormal) < 0f)
        {
            Vector3 swapPosition = b;
            b = c;
            c = swapPosition;
            Vector2 swapUv = uvB;
            uvB = uvC;
            uvC = swapUv;
        }

        int first = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        triangles.Add(first);
        triangles.Add(first + 1);
        triangles.Add(first + 2);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Replace('/', '_').Replace('\\', '_');
    }
}








