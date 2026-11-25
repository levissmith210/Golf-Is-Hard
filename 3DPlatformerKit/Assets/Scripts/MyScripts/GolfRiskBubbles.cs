using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Three optional, triggerless rewards on each hole's precision/shortcut route.
/// Counts belong to the current attempt; the scorecard banks them only on a finish.
/// </summary>
[DisallowMultipleComponent]
public sealed class GolfRiskBubbles : MonoBehaviour
{
    private sealed class Bubble
    {
        public Transform visual;
        public Transform rim;
        public Transform gem;
        public Vector3 anchor;
        public bool taken;
    }

    private Bubble[] bubbles = Array.Empty<Bubble>();
    private GolfCourseManager manager;
    private BallPlayerController ball;
    private GameObject runtimeRoot;
    private Mesh sphereMesh, gemMesh;
    private Material shellMaterial, goldMaterial, highlightMaterial;
    private ParticleSystem sparkles;
    private AudioSource pickupSource;
    private AudioClip pickupSound;
    private Vector3 previousPosition;
    private bool hasPreviousPosition;
    private float animationTime;
    private float collectionRadius = 1.3f;
    private AeroLaunchPad routeLauncher;
    private bool alignedWithLaunch;

    public int Collected { get; private set; }
    public int Available { get; private set; }
    public event Action CollectedBubble;

    public void Configure(GolfCourseManager courseManager)
    {
        manager = courseManager;
        EnsureResources();
    }

    public void BeginHole(int index, HoleTrigger hole, Transform tee, BallPlayerController activeBall)
    {
        Clear();
        ball = activeBall;
        if (hole == null || tee == null || ball == null)
            return;
        EnsureResources();
        if (sphereMesh == null || shellMaterial == null)
            return;

        Transform root = FindHoleRoot(index, hole.transform);
        Vector3[] positions = AuthoredRoute(index, root, tee, hole.transform);
        if (positions == null || positions.Length == 0)
            positions = SafeFallbackRoute(root, tee, hole.transform);

        runtimeRoot = new GameObject("Collectible Route Bubbles");
        runtimeRoot.transform.SetParent(transform, false);
        bubbles = new Bubble[positions.Length];
        Available = positions.Length;
        routeLauncher = FindRouteLauncher(index, root);
        if (routeLauncher != null)
            routeLauncher.Launched += OnRouteLaunched;
        alignedWithLaunch = false;
        var collider = ball.GetComponent<Collider>();
        collectionRadius = 1.05f + (collider != null ? Mathf.Max(0.15f, collider.bounds.extents.magnitude * 0.577f) : 0.25f);
        for (int i = 0; i < positions.Length; i++)
            bubbles[i] = CreateBubble(i, positions[i]);
        previousPosition = ball.transform.position;
        hasPreviousPosition = true;
    }

    public void Clear()
    {
        if (routeLauncher != null)
            routeLauncher.Launched -= OnRouteLaunched;
        if (runtimeRoot != null)
        {
            runtimeRoot.SetActive(false);
            Destroy(runtimeRoot);
        }
        runtimeRoot = null;
        bubbles = Array.Empty<Bubble>();
        Collected = Available = 0;
        ball = null;
        routeLauncher = null;
        alignedWithLaunch = false;
        hasPreviousPosition = false;
        animationTime = 0f;
        if (sparkles != null)
            sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Update()
    {
        if (ball == null || bubbles.Length == 0)
            return;
        bool playable = manager != null && manager.IsPlayingCourse && ball.enabled && !ball.IsHoleComplete;
        if (runtimeRoot != null && runtimeRoot.activeSelf != playable)
            runtimeRoot.SetActive(playable);
        if (!playable || Time.timeScale <= 0f)
        {
            // Do not sweep across a menu, a pause or a hole transition.
            previousPosition = ball.transform.position;
            hasPreviousPosition = true;
            return;
        }

        animationTime += Time.deltaTime;
        Camera view = Camera.main;
        Vector3 current = ball.transform.position;
        Vector3 from = hasPreviousPosition ? previousPosition : current;
        float segmentLength = Vector3.Distance(from, current);
        float plausibleTravel = Mathf.Max(8f, ball.Velocity.magnitude * Time.deltaTime * 2.5f + 2f);
        bool teleported = segmentLength > plausibleTravel;
        bool canCollect = ball.StrokeCount > 0 && !teleported;
        for (int i = 0; i < bubbles.Length; i++)
        {
            Bubble bubble = bubbles[i];
            if (bubble.taken || bubble.visual == null)
                continue;
            bubble.visual.position = bubble.anchor + Vector3.up * (Mathf.Sin(animationTime * 2.5f + i * 1.7f) * 0.12f);
            bubble.visual.localScale = Vector3.one * (1f + 0.035f * Mathf.Sin(animationTime * 3f + i));
            if (view != null)
                bubble.rim.rotation = Quaternion.LookRotation(view.transform.position - bubble.visual.position);
            bubble.gem.localRotation = Quaternion.Euler(12f, animationTime * 65f + i * 40f, 0f);
            if (canCollect && DistanceSquaredToSegment(bubble.visual.position, from, current) <= collectionRadius * collectionRadius)
                Collect(bubble);
        }
        previousPosition = current;
        hasPreviousPosition = true;
    }

    private void OnRouteLaunched(BallPlayerController launchedBall, Vector3 position, Vector3 velocity, float flightTime)
    {
        if (alignedWithLaunch || routeLauncher == null || launchedBall != ball || ball == null || ball.StrokeCount <= 0
            || manager == null || !manager.IsPlayingCourse || flightTime <= 0f)
            return;

        // The pad calculates its velocity from the exact point where the ball
        // enters its trigger. A long ramp can be entered from several positions;
        // fit the reward arc once to that real launch rather than an imaginary
        // center-of-trigger launch. The bubbles remain fixed for the rest of it.
        float g = -Physics.gravity.y * routeLauncher.gravityMultiplier;
        for (int i = 0; i < bubbles.Length; i++)
        {
            float t = flightTime * (0.2f + i * 0.3f);
            bubbles[i].anchor = position + velocity * t + Vector3.down * (0.5f * g * t * t);
        }
        alignedWithLaunch = true;
    }

    private static AeroLaunchPad FindRouteLauncher(int index, Transform root)
    {
        if (root == null) return null;
        string path = index == 2 ? "Cliff Kicker Launcher" : index == 3 ? "Summit Cannon/Launch Trigger"
            : index == 5 ? "Sky Cannon/Launch Trigger" : index == 6 ? "Leap Ramp Launcher" : null;
        Transform trigger = path != null ? root.Find(path) : null;
        return trigger != null ? trigger.GetComponent<AeroLaunchPad>() : null;
    }

    private static float DistanceSquaredToSegment(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.sqrMagnitude;
        float t = length > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - from, delta) / length) : 0f;
        return (point - (from + delta * t)).sqrMagnitude;
    }

    private void Collect(Bubble bubble)
    {
        bubble.taken = true;
        bubble.visual.gameObject.SetActive(false);
        Collected++;
        if (pickupSource != null)
        {
            pickupSource.pitch = 1f + (Collected - 1) * 0.12f;
            GameSettings.PlaySfx(pickupSource, pickupSound, 0.55f);
        }
        if (sparkles != null)
        {
            sparkles.Play();
            for (int i = 0; i < 18; i++)
            {
                float angle = i * 2.399963f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = bubble.visual.position,
                    velocity = new Vector3(Mathf.Cos(angle), 0.7f + i % 4 * 0.18f, Mathf.Sin(angle)) * (1f + i % 3 * 0.35f),
                    startColor = i % 3 == 0 ? new Color(0.5f, 0.95f, 1f) : new Color(1f, 0.87f, 0.25f),
                    startLifetime = 0.4f + i % 4 * 0.08f,
                    startSize = 0.09f + i % 3 * 0.035f
                };
                sparkles.Emit(emit, 1);
            }
        }
        CollectedBubble?.Invoke();
    }

    private static Transform FindHoleRoot(int index, Transform hole)
    {
        string prefix = "Hole " + (index + 1) + " - ";
        for (Transform p = hole; p != null; p = p.parent)
            if (p.name.StartsWith(prefix, StringComparison.Ordinal))
                return p;
        return hole.parent != null && hole.parent.parent != null ? hole.parent.parent : hole.parent;
    }

    private static Vector3[] AuthoredRoute(int index, Transform root, Transform tee, Transform hole)
    {
        if (root != null && root.name.StartsWith("Hole " + (index + 1) + " - ", StringComparison.Ordinal))
        {
            switch (index)
            {
                case 0: // Lagoon Lane: precision through each of the three hoop gates.
                    return new[] { AtFeature(root, "Hoop Gate 1"), AtFeature(root, "Hoop Gate 2"), AtFeature(root, "Hoop Gate 3") };
                case 1: // Glass Bridge: the lily-pad shortcut carries three rewards over open water.
                    return new[] { AtFeature(root, "Lily Pad Hop 1"), AtFeature(root, "Lily Pad Hop 2"), AtFeature(root, "Lily Pad Hop 3") };
                case 2: // Waterfall Terraces: commit to the kicker rather than the safe side ramp.
                    return LaunchRoute(root, "Cliff Kicker Launcher");
                case 3: // Seashell Spire: reward the high cannon lob onto the summit.
                    return LaunchRoute(root, "Summit Cannon/Launch Trigger");
                case 4: // Pinball Garden: the narrow glass channel bypasses the bumper field.
                    return new[] { AboveSurface(root, new Vector3(-20f, 0f, 40f)), AboveSurface(root, new Vector3(-20f, 0f, 75f)), AboveSurface(root, new Vector3(-15f, 0f, 110f)) };
                case 5: // Cloud Hop: the sky cannon crosses the chasm straight to the green.
                    return LaunchRoute(root, "Sky Cannon/Launch Trigger");
                case 6: // Halfpipe Canyon: the Leap Ramp skips the protected halfpipe.
                    return LaunchRoute(root, "Leap Ramp Launcher");
                case 7: // Aqua Dome: three bubbles trace the narrow isthmus through the lagoon.
                    return new[] { AboveSurface(root, new Vector3(6f, 0f, 95f)), AboveSurface(root, new Vector3(6f, 0f, 128f)), AboveSurface(root, new Vector3(4.5f, 0f, 148f)) };
                case 8: // Rainbow Finale: take the elevated bay shortcut, not the wide coastal fairway.
                    var result = new Vector3[3];
                    for (int i = 0; i < 3; i++)
                    {
                        float t = 0.22f + i * 0.28f;
                        result[i] = AboveSurface(root, new Vector3(Mathf.Lerp(-3f, 16f, t), 0f, Mathf.Lerp(22f, 156f, t)));
                    }
                    return result;
            }
        }

        // Other courses get surface-checked side-line challenges, never airborne
        // guesses over empty water. Tutorials are excluded by the manager.
        return SafeFallbackRoute(root, tee, hole);
    }

    private static Vector3[] SafeFallbackRoute(Transform root, Transform tee, Transform hole)
    {
        var fallback = new Vector3[3];
        Vector3 forward = Vector3.ProjectOnPlane(hole.position - tee.position, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = tee.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        for (int i = 0; i < 3; i++)
        {
            Vector3 seed = Vector3.Lerp(tee.position, hole.position, 0.3f + i * 0.2f) + side * (i % 2 == 0 ? 3f : -3f);
            if (!TrySurfacePoint(root, seed, out Vector3 surface))
            {
                // A missing shortcut must not leave collectibles floating over
                // empty sea. All course tees have a broad playable apron.
                seed = tee.position + forward * (4f + i * 2.5f) + side * ((i - 1) * 2f);
                if (!TrySurfacePoint(root, seed, out surface) && !TrySurfacePoint(null, seed, out surface))
                {
                    // For a hand-authored course without colliders at these
                    // points, use the actual tee surface as the final anchor.
                    surface = tee.position + side * ((i - 1) * 2.4f) + forward * 2.5f;
                    if (TrySurfacePoint(null, surface, out Vector3 teeSurface)) surface = teeSurface;
                }
            }
            fallback[i] = surface + Vector3.up * 0.85f;
        }
        return fallback;
    }

    private static Vector3 AtFeature(Transform root, string path)
    {
        Transform feature = root.Find(path);
        // A vertical ray through a standing hoop hits its TOP arc. Its transform
        // is already the center of the opening: reward a lofted shot through it.
        if (feature != null && path.StartsWith("Hoop Gate", StringComparison.Ordinal))
            return feature.position;
        return SurfacePoint(root, feature != null ? feature.position : root.position) + Vector3.up * 0.85f;
    }

    private static Vector3 AboveSurface(Transform root, Vector3 local)
    {
        return SurfacePoint(root, root.TransformPoint(local)) + Vector3.up * 0.85f;
    }

    private static Vector3[] LaunchRoute(Transform root, string path)
    {
        Transform trigger = root.Find(path);
        AeroLaunchPad launcher = trigger != null ? trigger.GetComponent<AeroLaunchPad>() : null;
        if (launcher == null || launcher.target == null)
            return Array.Empty<Vector3>();
        Vector3 from = trigger.position;
        // Kickers trigger at their front edge, not at the center of the long trigger.
        if (trigger.name.EndsWith(" Launcher", StringComparison.Ordinal))
        {
            BoxCollider box = trigger.GetComponent<BoxCollider>();
            if (box != null)
                from -= trigger.forward * (box.size.z * trigger.lossyScale.z * 0.5f);
            from = SurfacePoint(root, from) + Vector3.up * 0.25f;
        }
        else
        {
            // Flat cannon triggers sit 0.6 m above their pads. Use their real
            // bottom, rather than raycasting into nearby tree canopies (Isle A).
            BoxCollider box = trigger.GetComponent<BoxCollider>();
            if (box != null)
                from = trigger.TransformPoint(box.center + Vector3.down * box.size.y * 0.5f) + Vector3.up * 0.25f;
        }
        Vector3 to = launcher.target.position;
        Vector3 flat = Vector3.ProjectOnPlane(to - from, Vector3.up);
        float d = flat.magnitude;
        if (d < 0.5f) return Array.Empty<Vector3>();
        float g = -Physics.gravity.y * launcher.gravityMultiplier;
        float angle = launcher.launchAngle;
        float speedSquared = -1f;
        for (int i = 0; i < 8 && speedSquared <= 0f; i++, angle += 5f)
        {
            float radians = angle * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float denominator = 2f * cosine * cosine * (d * Mathf.Tan(radians) - (to.y - from.y));
            if (denominator > 0.01f) speedSquared = g * d * d / denominator;
        }
        if (speedSquared <= 0f) return Array.Empty<Vector3>();
        float finalAngle = (angle - 5f) * Mathf.Deg2Rad;
        float speed = Mathf.Sqrt(speedSquared);
        Vector3 velocity = flat.normalized * (speed * Mathf.Cos(finalAngle)) + Vector3.up * (speed * Mathf.Sin(finalAngle));
        float flightTime = d / Mathf.Max(0.01f, speed * Mathf.Cos(finalAngle));
        var points = new Vector3[3];
        for (int i = 0; i < points.Length; i++)
        {
            float t = flightTime * (0.2f + i * 0.3f);
            points[i] = from + velocity * t + Vector3.down * (0.5f * g * t * t);
        }
        return points;
    }

    private static Vector3 SurfacePoint(Transform root, Vector3 seed)
    {
        return TrySurfacePoint(root, seed, out Vector3 point) ? point : seed;
    }

    private static bool TrySurfacePoint(Transform root, Vector3 seed, out Vector3 point)
    {
        RaycastHit[] hits = Physics.RaycastAll(seed + Vector3.up * 70f, Vector3.down, 150f, ~0, QueryTriggerInteraction.Ignore);
        bool found = false;
        RaycastHit nearest = default;
        foreach (RaycastHit hit in hits)
        {
            if (Vector3.Dot(hit.normal, Vector3.up) < 0.35f || (root != null && !hit.transform.IsChildOf(root))) continue;
            if (!found || hit.distance < nearest.distance) { nearest = hit; found = true; }
        }
        point = found ? nearest.point : seed;
        return found;
    }

    private Bubble CreateBubble(int index, Vector3 position)
    {
        var go = new GameObject("Risk Bubble " + (index + 1));
        go.transform.SetParent(runtimeRoot.transform, false);
        go.transform.position = position;
        MeshObject("Bubble shell", go.transform, sphereMesh, shellMaterial, Vector3.zero, Vector3.one * 0.72f);
        var gem = MeshObject("Gold pearl", go.transform, gemMesh, goldMaterial, Vector3.zero, new Vector3(0.24f, 0.37f, 0.24f));
        MeshObject("Reflection", go.transform, sphereMesh, highlightMaterial, new Vector3(-0.24f, 0.3f, -0.5f), new Vector3(0.12f, 0.19f, 0.07f));
        var rim = new GameObject("Golden collectible rim").transform;
        rim.SetParent(go.transform, false);
        var line = rim.gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = goldMaterial;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 48;
        line.widthMultiplier = 0.055f;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        for (int i = 0; i < line.positionCount; i++)
        {
            float a = i * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.76f, Mathf.Sin(a) * 0.76f, 0f));
        }
        return new Bubble { anchor = position, visual = go.transform, rim = rim, gem = gem.transform };
    }

    private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    private void EnsureResources()
    {
        if (shellMaterial != null) return;
        Shader shellShader = Resources.Load<Shader>("GolfRewardBubble");
        Shader glowShader = Resources.Load<Shader>("GolfShotGlow");
        if (shellShader == null || glowShader == null) return;
        shellMaterial = new Material(shellShader) { name = "Collectible bubble glass", hideFlags = HideFlags.HideAndDontSave };
        goldMaterial = new Material(glowShader) { name = "Collectible gold", hideFlags = HideFlags.HideAndDontSave };
        goldMaterial.SetColor("_Color", new Color(1f, 0.8f, 0.2f, 0.96f));
        highlightMaterial = new Material(shellShader) { name = "Bubble highlight", hideFlags = HideFlags.HideAndDontSave };
        highlightMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0.8f));
        sphereMesh = MakeSphere();
        gemMesh = MakeGem();
        pickupSource = gameObject.AddComponent<AudioSource>();
        pickupSource.playOnAwake = false;
        pickupSource.spatialBlend = 0f;
        pickupSound = MakePickupSound();
        var particleObject = new GameObject("Bubble pickup sparkles");
        particleObject.transform.SetParent(transform, false);
        sparkles = particleObject.AddComponent<ParticleSystem>();
        sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparkles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 0.55f;
        main.maxParticles = 80;
        main.gravityModifier = 0.25f;
        var emission = sparkles.emission; emission.enabled = false;
        var shape = sparkles.shape; shape.enabled = false;
        var size = sparkles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        sparkles.GetComponent<ParticleSystemRenderer>().sharedMaterial = goldMaterial;
    }

    private static Mesh MakeSphere()
    {
        const int longitude = 24, latitude = 16;
        var vertices = new Vector3[(longitude + 1) * (latitude + 1)];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[longitude * latitude * 6];
        for (int y = 0; y <= latitude; y++)
            for (int x = 0; x <= longitude; x++)
            {
                int index = y * (longitude + 1) + x;
                float phi = Mathf.PI * y / latitude, theta = Mathf.PI * 2f * x / longitude;
                vertices[index] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                normals[index] = vertices[index];
                uv[index] = new Vector2(x / (float)longitude, y / (float)latitude);
                colors[index] = Color.white;
            }
        int cursor = 0;
        for (int y = 0; y < latitude; y++)
            for (int x = 0; x < longitude; x++)
            {
                int a = y * (longitude + 1) + x, b = a + longitude + 1;
                triangles[cursor++] = a; triangles[cursor++] = a + 1; triangles[cursor++] = b;
                triangles[cursor++] = a + 1; triangles[cursor++] = b + 1; triangles[cursor++] = b;
            }
        var mesh = new Mesh { name = "Runtime collectible bubble", hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices, normals = normals, uv = uv, colors = colors, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh MakeGem()
    {
        var mesh = new Mesh { name = "Collectible gold pearl", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.forward, Vector3.right, Vector3.back };
        mesh.triangles = new[] { 0, 3, 2, 0, 4, 3, 0, 5, 4, 0, 2, 5, 1, 2, 3, 1, 3, 4, 1, 4, 5, 1, 5, 2 };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
        // Shot Glow feathers the UV edges; keep the jewel in its fully visible center.
        mesh.uv = new[] { Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static AudioClip MakePickupSound()
    {
        const int rate = 22050;
        var samples = new float[rate / 3];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float envelope = Mathf.Clamp01(t * 300f) * Mathf.Exp(-t * 17f) * Mathf.Clamp01((samples.Length - i) / 250f);
            samples[i] = (Mathf.Sin(t * 1046.5f * Mathf.PI * 2f) + Mathf.Sin(t * 1569.75f * Mathf.PI * 2f) * 0.3f) * envelope * 0.22f;
        }
        var clip = AudioClip.Create("Collectible bubble chime", samples.Length, 1, rate, false);
        clip.hideFlags = HideFlags.HideAndDontSave;
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        Clear();
        if (sphereMesh != null) Destroy(sphereMesh);
        if (gemMesh != null) Destroy(gemMesh);
        if (shellMaterial != null) Destroy(shellMaterial);
        if (goldMaterial != null) Destroy(goldMaterial);
        if (highlightMaterial != null) Destroy(highlightMaterial);
        if (pickupSound != null) Destroy(pickupSound);
    }
}
