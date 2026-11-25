using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Self-contained trophy photograph for the win screen. The hidden preview rig is isolated
/// from the course, and owns every mesh, material, light, and texture that it creates.
/// </summary>
[DisallowMultipleComponent]
public sealed class GolfVictoryTrophy : MonoBehaviour
{
    private const int PreviewLayer = 31;
    private const int TextureSize = 768;
    private const uint PreviewRenderingLayer = 1u << 7;
    private static readonly Vector3 PreviewOrigin = new Vector3(12000f, -12000f, 12000f);
    private static int previewSerial;
    private Vector3 previewOrigin;

    private readonly List<Object> ownedAssets = new List<Object>();
    private readonly List<Transform> floatingPearls = new List<Transform>();
    private GameObject previewRoot;
    private Transform trophyRoot;
    private Transform halo;
    private Camera previewCamera;
    private RenderTexture previewTexture;
    private Material accentMaterial;
    private Material metalMaterial;
    private UniversalRenderPipeline.SingleCameraRequest renderRequest;
    private bool rendering;
    private int awardTier;

    public Texture PreviewTexture => previewTexture;

    /// <param name="tier">1 bronze, 2 silver, 3 gold, 4 diamond.</param>
    public void SetAward(int tier)
    {
        tier = Mathf.Clamp(tier, 1, 4);
        if (previewRoot != null && awardTier == tier && previewTexture != null)
            return;

        DisposePreview();
        awardTier = tier;
        BuildPreview();
        Tick(1f, 0f);
    }

    /// <summary>Call only while the win presentation is visible, outside OnGUI.</summary>
    public void Tick(float age, float deltaTime)
    {
        if (previewRoot == null || previewCamera == null || previewTexture == null)
            return;

        age = Mathf.Max(0f, age);
        float reveal = Mathf.Clamp01(age / 0.85f);
        float eased = 1f + 2.45f * Mathf.Pow(reveal - 1f, 3f) + 1.45f * Mathf.Pow(reveal - 1f, 2f);
        float scale = Mathf.LerpUnclamped(0.84f, 1f, eased);
        trophyRoot.localScale = Vector3.one * scale;
        trophyRoot.localPosition = new Vector3(0f, Mathf.Sin(age * 1.65f) * 0.035f, 0f);
        trophyRoot.localRotation = Quaternion.Euler(
            Mathf.Sin(age * 1.1f) * 2f,
            -16f + Mathf.Sin(age * 0.65f) * 17f + (1f - reveal) * -24f,
            Mathf.Sin(age * 1.45f) * 2f);

        if (halo != null)
        {
            // Keep the ring in a fixed camera-facing plane behind the sculpture.
            // Rotating Euler angles can otherwise turn this thin torus edge-on.
            halo.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(0f, 0.6f, -6.55f).normalized);
            float breath = 1f + Mathf.Sin(age * 1.3f) * 0.025f;
            halo.localScale = Vector3.one * breath;
        }
        for (int i = 0; i < floatingPearls.Count; i++)
        {
            float angle = age * 0.44f + i * Mathf.PI * 2f / floatingPearls.Count;
            floatingPearls[i].localPosition = new Vector3(
                Mathf.Cos(angle) * (1.12f + i * 0.035f),
                1.3f + Mathf.Sin(angle * 1.25f + i) * 0.85f,
                0.35f + Mathf.Sin(angle) * 0.22f);
        }
        if (awardTier == 4 && metalMaterial != null)
        {
            Color ice = Color.Lerp(new Color(0.52f, 0.91f, 1f), new Color(0.87f, 0.76f, 1f),
                0.5f + Mathf.Sin(age * 0.7f) * 0.5f);
            metalMaterial.SetColor("_BaseColor", ice);
        }
        RenderPreview();
    }

    private void BuildPreview()
    {
        previewRoot = NewObject("Golf Trophy Preview (Temporary)", null);
        previewOrigin = ChoosePreviewOrigin();
        previewRoot.transform.position = previewOrigin;
        trophyRoot = NewObject("Award Sculpture", previewRoot.transform).transform;

        Color metal = awardTier == 1 ? new Color(0.79f, 0.39f, 0.18f)
            : awardTier == 2 ? new Color(0.75f, 0.88f, 0.96f)
            : awardTier == 3 ? new Color(1f, 0.67f, 0.13f)
            : new Color(0.63f, 0.92f, 1f);
        Color darkMetal = Color.Lerp(metal, new Color(0.12f, 0.22f, 0.31f), 0.5f);
        Color accent = awardTier == 1 ? new Color(0.31f, 0.87f, 0.93f)
            : awardTier == 2 ? new Color(0.23f, 0.78f, 1f)
            : awardTier == 3 ? new Color(0.28f, 0.92f, 0.74f)
            : new Color(0.62f, 0.58f, 1f);
        metalMaterial = MakeMaterial("Award Polished Metal", metal, 0.88f, 0.89f);
        Material innerMaterial = MakeMaterial("Award Inner Metal", Color.Lerp(metal, Color.white, 0.18f), 0.7f, 0.8f);
        Material darkMaterial = MakeMaterial("Award Metal Recess", darkMetal, 0.72f, 0.78f);
        Material pearlMaterial = MakeMaterial("Award Pearl Porcelain", new Color(0.93f, 0.99f, 1f), 0.2f, 0.88f);
        Material golfMaterial = MakeMaterial("Award Golf Ball", new Color(0.97f, 0.99f, 1f), 0.05f, 0.6f);
        accentMaterial = MakeMaterial("Award Aqua Light", accent, 0.22f, 0.91f, 0.35f);
        Material starMaterial = MakeMaterial("Award Star", Color.Lerp(metal, Color.white, 0.68f), 0.7f, 0.95f, 0.06f);
        Material haloMaterial = MakeMaterial("Award Halo", Color.Lerp(accent, Color.white, 0.22f), 0.1f, 0.65f, 0.55f);

        // A gently bevelled pearl podium keeps the award visually rooted without a background plane.
        MeshObject("Pearl Pedestal", trophyRoot, Lathe("Pearl Podium Mesh", new[]
        {
            new Vector2(0f, 0.03f), new Vector2(0.53f, 0.03f), new Vector2(0.63f, 0.08f),
            new Vector2(0.66f, 0.14f), new Vector2(0.66f, 0.23f), new Vector2(0.61f, 0.29f),
            new Vector2(0.49f, 0.34f), new Vector2(0.46f, 0.40f), new Vector2(0f, 0.40f)
        }, 96), pearlMaterial);
        MeshObject("Lower Metal Band", trophyRoot, Torus("Podium Metal Band Mesh", 0.628f, 0.031f, 96, 12), metalMaterial)
            .transform.localPosition = Vector3.up * 0.113f;
        MeshObject("Pedestal Aqua Inlay", trophyRoot, Torus("Podium Light Band Mesh", 0.649f, 0.012f, 96, 10), accentMaterial)
            .transform.localPosition = Vector3.up * 0.217f;
        MeshObject("Upper Award Foot", trophyRoot, Lathe("Award Foot Mesh", new[]
        {
            new Vector2(0f, 0.395f), new Vector2(0.39f, 0.395f), new Vector2(0.405f, 0.43f),
            new Vector2(0.37f, 0.47f), new Vector2(0.26f, 0.51f), new Vector2(0.19f, 0.59f),
            new Vector2(0.145f, 0.73f), new Vector2(0.13f, 0.91f), new Vector2(0.18f, 1.03f),
            new Vector2(0.25f, 1.10f), new Vector2(0f, 1.10f)
        }, 96), metalMaterial);
        MeshObject("Stem Pearl Collar", trophyRoot, Torus("Stem Collar Mesh", 0.145f, 0.035f, 72, 12), pearlMaterial)
            .transform.localPosition = Vector3.up * 0.75f;

        // The bowl profile returns down its inside: the cup is a real double-wall form.
        var bowl = new[]
        {
            new Vector2(0.17f, 1.02f), new Vector2(0.25f, 1.08f), new Vector2(0.34f, 1.20f),
            new Vector2(0.44f, 1.35f), new Vector2(0.54f, 1.55f), new Vector2(0.62f, 1.78f),
            new Vector2(0.66f, 1.98f), new Vector2(0.66f, 2.035f), new Vector2(0.61f, 2.035f),
            new Vector2(0.575f, 1.83f), new Vector2(0.50f, 1.61f), new Vector2(0.39f, 1.41f),
            new Vector2(0.28f, 1.27f), new Vector2(0.17f, 1.205f), new Vector2(0f, 1.205f)
        };
        MeshObject("Champion Cup", trophyRoot, Lathe("Champion Cup Mesh", bowl, 112), metalMaterial);
        MeshObject("Inner Cup Dish", trophyRoot, Lathe("Inner Cup Dish Mesh", new[]
        {
            new Vector2(0.21f, 1.25f), new Vector2(0.14f, 1.212f), new Vector2(0f, 1.212f)
        }, 96), innerMaterial);
        MeshObject("Rolled Cup Rim", trophyRoot, Torus("Cup Rolled Rim Mesh", 0.635f, 0.042f, 112, 16), starMaterial)
            .transform.localPosition = Vector3.up * 2.023f;
        MeshObject("Lower Bowl Accent", trophyRoot, Torus("Bowl Accent Mesh", 0.317f, 0.016f, 96, 10), darkMaterial)
            .transform.localPosition = Vector3.up * 1.19f;
        MeshObject("Right Swept Handle", trophyRoot, Handle("Right Handle Mesh", 1f), metalMaterial);
        MeshObject("Left Swept Handle", trophyRoot, Handle("Left Handle Mesh", -1f), metalMaterial);

        GameObject star = MeshObject("Cup Star Badge", trophyRoot, Star("Cup Star Badge Mesh", 0.19f, 0.084f, 0.022f), starMaterial);
        star.transform.localPosition = new Vector3(0f, 1.55f, -0.55f);
        star.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
        GameObject plinthStar = MeshObject("Podium Star Badge", trophyRoot, Star("Podium Star Mesh", 0.073f, 0.031f, 0.009f), metalMaterial);
        plinthStar.transform.localPosition = new Vector3(0f, 0.19f, -0.665f);

        Mesh ballMesh = Sphere("Dimpled Golf Ball Mesh", 0.265f, 96, 48, true);
        MeshObject("Crowning Golf Ball", trophyRoot, ballMesh, golfMaterial).transform.localPosition = Vector3.up * 2.115f;
        Mesh pearlMesh = Sphere("Orbit Pearl Mesh", 0.045f, 24, 12, false);
        for (int i = 0; i < (awardTier == 4 ? 5 : 3); i++)
            floatingPearls.Add(MeshObject("Floating Award Pearl", previewRoot.transform, pearlMesh, i % 2 == 0 ? pearlMaterial : accentMaterial).transform);

        halo = MeshObject("Halo Behind Award", previewRoot.transform, Torus("Award Halo Mesh", 1.27f, 0.011f, 112, 8), haloMaterial).transform;
        halo.localPosition = new Vector3(0f, 1.27f, 0.65f);
        halo.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(0f, 0.6f, -6.55f).normalized);
        BuildReflectionProbe();
        BuildLights();
        BuildCamera();
    }

    private Vector3 ChoosePreviewOrigin()
    {
        // Editor windows can coexist, and script reloads reset static counters. Choose a
        // free studio location instead of placing a new trophy inside an existing one.
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int attempt = 0; attempt < 256; attempt++)
        {
            int slot = previewSerial++ & 255;
            Vector3 candidate = PreviewOrigin + new Vector3((slot & 15) * 32f, 0f, (slot >> 4) * 32f);
            bool occupied = false;
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject obj = objects[i];
                if (obj == null || obj == previewRoot || obj.name != "Golf Trophy Preview (Temporary)" ||
                    (obj.hideFlags & HideFlags.HideAndDontSave) != HideFlags.HideAndDontSave)
                    continue;
                if ((obj.transform.position - candidate).sqrMagnitude < 25f * 25f) { occupied = true; break; }
            }
            if (!occupied) return candidate;
        }
        return PreviewOrigin + Vector3.up * (32f * (previewSerial + 1));
    }
    private void BuildCamera()
    {
        GameObject cameraObject = NewObject("Award Preview Camera", previewRoot.transform);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 16f;
        previewCamera.fieldOfView = 31f;
        previewCamera.aspect = 1f;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = true;
        previewCamera.useOcclusionCulling = false;
        previewCamera.transform.localPosition = new Vector3(0f, 1.87f, -5.9f);
        previewCamera.transform.LookAt(previewOrigin + Vector3.up * 1.25f);
        var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.requiresColorTexture = false;
        data.requiresDepthTexture = false;
        data.volumeLayerMask = 0;
        data.allowXRRendering = false;
        data.antialiasing = AntialiasingMode.None;
        previewTexture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32)
        {
            name = "Golf Victory Trophy Preview",
            antiAliasing = 4,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        previewTexture.Create();
        previewCamera.targetTexture = previewTexture;
        renderRequest = new UniversalRenderPipeline.SingleCameraRequest { destination = previewTexture };
    }

    private void BuildLights()
    {
        AddLight("Warm Key", new Vector3(-2.4f, 4.0f, -3.2f), new Color(1f, 0.92f, 0.75f), 38f);
        AddLight("Aqua Fill", new Vector3(2.5f, 2.3f, -1.5f), new Color(0.60f, 0.87f, 1f), 19f);
        AddLight("Pearl Rim", new Vector3(0.7f, 3.2f, 2.3f), Color.white, 43f);
        AddLight("Base Fill", new Vector3(-0.7f, 0.3f, -2f), new Color(0.64f, 0.91f, 1f), 5f);
    }

    private void AddLight(string name, Vector3 local, Color color, float intensity)
    {
        GameObject obj = NewObject(name, previewRoot.transform);
        obj.transform.localPosition = local;
        Light light = obj.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 11f;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
        light.cullingMask = 1 << PreviewLayer;
        light.renderMode = LightRenderMode.ForcePixel;
        light.renderingLayerMask = (int)PreviewRenderingLayer;
        var data = obj.AddComponent<UniversalAdditionalLightData>();
        data.renderingLayers = PreviewRenderingLayer;
    }

    private void BuildReflectionProbe()
    {
        const int size = 32;
        var environment = Own(new Cubemap(size, TextureFormat.RGBA32, true)
        {
            name = "Award Studio Reflection",
            hideFlags = HideFlags.HideAndDontSave
        });
        for (int face = 0; face < 6; face++)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                Vector3 direction = CubeDirection(face, u, v).normalized;
                Color color = Color.Lerp(new Color(0.16f, 0.43f, 0.58f), new Color(0.89f, 0.98f, 1f), Mathf.Clamp01(direction.y * 0.5f + 0.6f));
                float window = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, new Vector3(-0.45f, 0.68f, -0.55f).normalized)), 14f);
                float strip = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, new Vector3(0.65f, 0.3f, -0.65f).normalized)), 40f);
                color = Color.Lerp(color, Color.white, Mathf.Clamp01(window * 0.95f + strip));
                pixels[y * size + x] = color;
            }
            environment.SetPixels(pixels, (CubemapFace)face);
        }
        environment.Apply(true, true);
        GameObject obj = NewObject("Award Studio Reflection Probe", previewRoot.transform);
        obj.transform.localPosition = Vector3.up * 1.25f;
        var probe = obj.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = environment;
        probe.size = Vector3.one * 9f;
        probe.blendDistance = 0.5f;
        probe.boxProjection = false;
        probe.importance = 100;
        probe.intensity = 1f;
    }

    private static Vector3 CubeDirection(int face, float u, float v)
    {
        switch ((CubemapFace)face)
        {
            case CubemapFace.PositiveX: return new Vector3(1f, v, -u);
            case CubemapFace.NegativeX: return new Vector3(-1f, v, u);
            case CubemapFace.PositiveY: return new Vector3(u, 1f, -v);
            case CubemapFace.NegativeY: return new Vector3(u, -1f, v);
            case CubemapFace.PositiveZ: return new Vector3(u, v, 1f);
            default: return new Vector3(-u, v, -1f);
        }
    }

    private void RenderPreview()
    {
        if (rendering || previewCamera == null || previewTexture == null)
            return;
        rendering = true;
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            if (!previewTexture.IsCreated()) previewTexture.Create();
            if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(previewCamera, renderRequest))
                RenderPipeline.SubmitRenderRequest(previewCamera, renderRequest);
            else
                previewCamera.Render();
        }
        finally { RenderTexture.active = previousTarget; rendering = false; }
    }

    private GameObject NewObject(string name, Transform parent)
    {
        var obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave, layer = PreviewLayer };
        if (parent != null) obj.transform.SetParent(parent, false);
        return obj;
    }

    private GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
    {
        GameObject obj = NewObject(name, parent);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.renderingLayerMask = PreviewRenderingLayer;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;
        return obj;
    }

    private Material MakeMaterial(string name, Color color, float metallic, float smoothness, float emission = 0f)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = Own(new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave });
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Glossiness", smoothness);
        if (emission > 0f)
        {
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }
        return material;
    }

    private Mesh Lathe(string name, Vector2[] profile, int segments)
    {
        int columns = segments + 1;
        var vertices = new Vector3[profile.Length * columns];
        var uv = new Vector2[vertices.Length];
        for (int row = 0; row < profile.Length; row++)
        for (int segment = 0; segment <= segments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / segments;
            vertices[row * columns + segment] = new Vector3(Mathf.Cos(angle) * profile[row].x, profile[row].y, Mathf.Sin(angle) * profile[row].x);
            uv[row * columns + segment] = new Vector2((float)segment / segments, (float)row / Mathf.Max(1, profile.Length - 1));
        }
        var triangles = new int[(profile.Length - 1) * segments * 6];
        int ti = 0;
        for (int row = 0; row < profile.Length - 1; row++)
        for (int segment = 0; segment < segments; segment++)
        {
            int a = row * columns + segment, b = a + 1, c = a + columns, d = c + 1;
            triangles[ti++] = a; triangles[ti++] = c; triangles[ti++] = b;
            triangles[ti++] = b; triangles[ti++] = c; triangles[ti++] = d;
        }
        return FinishMesh(name, vertices, triangles, uv, true, columns);
    }

    private Mesh Torus(string name, float majorRadius, float tubeRadius, int around, int tube)
    {
        var vertices = new Vector3[(around + 1) * (tube + 1)];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[around * tube * 6];
        int index = 0;
        for (int i = 0; i <= around; i++)
        for (int j = 0; j <= tube; j++)
        {
            float a = i * Mathf.PI * 2f / around, b = j * Mathf.PI * 2f / tube;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 normal = radial * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
            vertices[index] = radial * majorRadius + normal * tubeRadius;
            normals[index] = normal;
            uv[index++] = new Vector2((float)i / around, (float)j / tube);
        }
        int ti = 0;
        for (int i = 0; i < around; i++)
        for (int j = 0; j < tube; j++)
        {
            int a = i * (tube + 1) + j, b = a + 1, c = a + tube + 1, d = c + 1;
            triangles[ti++] = a; triangles[ti++] = b; triangles[ti++] = c;
            triangles[ti++] = b; triangles[ti++] = d; triangles[ti++] = c;
        }
        Mesh mesh = FinishMesh(name, vertices, triangles, uv, false);
        mesh.normals = normals;
        return mesh;
    }

    private Mesh Handle(string name, float side)
    {
        const int steps = 56, ring = 14;
        var vertices = new Vector3[(steps + 1) * (ring + 1)];
        var uv = new Vector2[vertices.Length];
        var normals = new Vector3[vertices.Length];
        var triangles = new int[steps * ring * 6];
        Vector3 Point(float t)
        {
            float q = 1f - t;
            return new Vector3(side * (q * q * q * 0.605f + 3f * q * q * t * 1.20f + 3f * q * t * t * 1.23f + t * t * t * 0.37f),
                q * q * q * 1.965f + 3f * q * q * t * 2.08f + 3f * q * t * t * 1.12f + t * t * t * 1.25f, 0f);
        }
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 point = Point(t);
            Vector3 tangent = (Point(Mathf.Min(1f, t + 0.005f)) - Point(Mathf.Max(0f, t - 0.005f))).normalized;
            Vector3 cross = Vector3.Cross(tangent, Vector3.forward).normalized;
            float thickness = Mathf.Lerp(0.056f, 0.049f, Mathf.Sin(t * Mathf.PI));
            for (int j = 0; j <= ring; j++)
            {
                float angle = j * Mathf.PI * 2f / ring;
                Vector3 normal = cross * Mathf.Cos(angle) + Vector3.forward * Mathf.Sin(angle);
                int n = i * (ring + 1) + j;
                vertices[n] = point + normal * thickness; normals[n] = normal;
                uv[n] = new Vector2((float)j / ring, t);
            }
        }
        int ti = 0;
        for (int i = 0; i < steps; i++)
        for (int j = 0; j < ring; j++)
        {
            int a = i * (ring + 1) + j, b = a + 1, c = a + ring + 1, d = c + 1;
            triangles[ti++] = a; triangles[ti++] = c; triangles[ti++] = b;
            triangles[ti++] = b; triangles[ti++] = c; triangles[ti++] = d;
        }
        Mesh mesh = FinishMesh(name, vertices, triangles, uv, false); mesh.normals = normals; return mesh;
    }

    private Mesh Sphere(string name, float radius, int segments, int rings, bool dimples)
    {
        var vertices = new Vector3[(rings + 1) * (segments + 1)];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[rings * segments * 6];
        var centers = new Vector3[dimples ? 180 : 0];
        for (int i = 0; i < centers.Length; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / centers.Length;
            float r = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
            centers[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }
        for (int row = 0; row <= rings; row++)
        for (int segment = 0; segment <= segments; segment++)
        {
            float polar = row * Mathf.PI / rings, azimuth = segment * Mathf.PI * 2f / segments;
            Vector3 direction = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(azimuth));
            float dent = 0f;
            for (int i = 0; i < centers.Length; i++)
            {
                float dot = Vector3.Dot(direction, centers[i]);
                float t = Mathf.Clamp01((dot - 0.9958f) / 0.0042f);
                dent = Mathf.Max(dent, Mathf.Sin(t * Mathf.PI * 0.5f) * 0.0065f);
            }
            int n = row * (segments + 1) + segment;
            vertices[n] = direction * (radius - dent);
            uv[n] = new Vector2((float)segment / segments, (float)row / rings);
        }
        int ti = 0;
        for (int row = 0; row < rings; row++)
        for (int segment = 0; segment < segments; segment++)
        {
            int a = row * (segments + 1) + segment, b = a + 1, c = a + segments + 1, d = c + 1;
            triangles[ti++] = a; triangles[ti++] = b; triangles[ti++] = c;
            triangles[ti++] = b; triangles[ti++] = d; triangles[ti++] = c;
        }
        return FinishMesh(name, vertices, triangles, uv, true, segments + 1);
    }

    private Mesh Star(string name, float outer, float inner, float depth)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        var points = new Vector3[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            points[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (i % 2 == 0 ? outer : inner);
        }
        for (int i = 0; i < 10; i++)
        {
            int n = vertices.Count; Vector3 a = points[i], b = points[(i + 1) % 10];
            vertices.Add(Vector3.back * depth); vertices.Add(b + Vector3.back * depth); vertices.Add(a + Vector3.back * depth);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            n = vertices.Count;
            vertices.Add(a + Vector3.back * depth); vertices.Add(b + Vector3.back * depth); vertices.Add(b + Vector3.forward * depth); vertices.Add(a + Vector3.forward * depth);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2); triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
        }
        return FinishMesh(name, vertices.ToArray(), triangles.ToArray(), null, true);
    }

    private Mesh FinishMesh(string name, Vector3[] vertices, int[] triangles, Vector2[] uv, bool recalculateNormals, int seamColumns = 0)
    {
        var mesh = Own(new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave });
        mesh.vertices = vertices; mesh.triangles = triangles;
        if (uv != null) mesh.uv = uv;
        if (recalculateNormals)
        {
            mesh.RecalculateNormals();
            if (seamColumns > 1)
            {
                Vector3[] normals = mesh.normals;
                for (int i = 0; i < normals.Length; i += seamColumns)
                {
                    Vector3 average = (normals[i] + normals[i + seamColumns - 1]).normalized;
                    normals[i] = average; normals[i + seamColumns - 1] = average;
                }
                mesh.normals = normals;
            }
        }
        mesh.RecalculateBounds();
        if (uv != null && recalculateNormals) mesh.RecalculateTangents();
        return mesh;
    }

    private T Own<T>(T asset) where T : Object
    {
        ownedAssets.Add(asset);
        return asset;
    }

    public void DisposePreview()
    {
        if (previewCamera != null) { previewCamera.enabled = false; previewCamera.targetTexture = null; }
        if (previewRoot != null) { previewRoot.SetActive(false); Release(previewRoot); }
        previewRoot = null; trophyRoot = null; halo = null; previewCamera = null;
        floatingPearls.Clear();
        if (previewTexture != null) { previewTexture.Release(); Release(previewTexture); previewTexture = null; }
        for (int i = ownedAssets.Count - 1; i >= 0; i--) if (ownedAssets[i] != null) Release(ownedAssets[i]);
        ownedAssets.Clear();
        metalMaterial = null; accentMaterial = null; renderRequest = null; awardTier = 0;
    }

    private static void Release(Object asset)
    {
        if (Application.isPlaying) Destroy(asset);
        else DestroyImmediate(asset);
    }

    private void OnDisable() { DisposePreview(); }
    private void OnDestroy() { DisposePreview(); }
}








