using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a self-contained Frutiger Aero / Wii Sports Resort style golf hole in a new scene.
/// Uses its own generated island mesh (no Unity Terrain) plus the existing Frutiger Aero cup and tee prefabs.
/// </summary>
public static class FrutigerAeroCourseSceneBuilder
{
    private const string Root = "Assets/FrutigerGolfCourse";
    private const string ScenePath = Root + "/FrutigerAeroHole.unity";
    private const string HolePrefab = "Assets/Models/FrutigerAeroGolfHole/FrutigerAeroGolfHole.prefab";
    private const string TeePrefab = "Assets/Models/FrutigerAeroGolfTee/FrutigerAeroGolfTee.prefab";

    // World layout (+Z is down the hole).
    private const float X0 = -45f, X1 = 45f, Z0 = -30f, Z1 = 125f;
    private const float LandH = 1.0f;       // base height of the island above the seabed
    private const float WaterY = -0.45f;
    private const float SeabedY = -3.0f;
    private static readonly Vector2 Green = new Vector2(0f, 0f); // set in static ctor
    private static Vector2 GreenC;
    private static Vector2[] pathPts;
    private static float[] fairR;

    private static readonly Vector3[] Bunkers =
    {
        new Vector3(10.5f, 64f, 5.0f), // x, z, radius-ish (x radius; z radius = 0.7x)
        new Vector3(9.5f, 84f, 4.2f),
        new Vector3(-12.5f, 97f, 3.8f),
    };
    private static readonly Vector2 PondC = new Vector2(-22f, 42f);
    private static readonly Vector2 PondR = new Vector2(10f, 7.5f);

    static FrutigerAeroCourseSceneBuilder()
    {
        BuildPath();
    }

    private static float PathX(float z) { return 9f * Mathf.Sin(z * 0.04f); }

    private static void BuildPath()
    {
        int n = 40;
        pathPts = new Vector2[n];
        fairR = new float[n];
        for (int i = 0; i < n; i++)
        {
            float s = -6f + i * 2.5f;
            pathPts[i] = new Vector2(PathX(s), s);
            fairR[i] = 8.5f + 2.2f * Mathf.Sin(s * 0.08f + 0.5f);
        }
        GreenC = new Vector2(PathX(90f), 90f);
    }

    // ---------- field functions ----------
    private static float Ell(float x, float z, float cx, float cz, float rx, float rz)
    {
        float dx = (x - cx) / rx, dz = (z - cz) / rz;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float Smooth(float a, float b, float v)
    {
        float t = Mathf.Clamp01((v - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // >0 inside fairway
    private static float FairT(float x, float z)
    {
        float best = -9f;
        for (int i = 0; i < pathPts.Length; i++)
        {
            float dx = x - pathPts[i].x, dz = z - pathPts[i].y;
            float v = 1f - Mathf.Sqrt(dx * dx + dz * dz) / fairR[i];
            if (v > best) best = v;
        }
        return best;
    }

    // >0 inside island (rough footprint)
    private static float IslandT(float x, float z)
    {
        float best = 1f - Ell(x, z, 0f, 3f, 17f, 16f);
        for (int i = 0; i < pathPts.Length; i++)
        {
            float dx = x - pathPts[i].x, dz = z - pathPts[i].y;
            float v = 1f - Mathf.Sqrt(dx * dx + dz * dz) / (fairR[i] + 11f);
            if (v > best) best = v;
        }
        float g = 1f - Ell(x, z, GreenC.x, GreenC.y, 27f, 23f);
        return Mathf.Max(best, g);
    }

    private static float GreenT(float x, float z) { return 1f - Ell(x, z, GreenC.x, GreenC.y, 13.5f, 10.5f); }
    private static float TeeT(float x, float z) { return 1f - Ell(x, z, 0f, 0f, 6.5f, 5.5f); }
    private static float BunkerT(float x, float z)
    {
        float best = -9f;
        for (int i = 0; i < Bunkers.Length; i++)
        {
            float v = 1f - Ell(x, z, Bunkers[i].x + (i == 2 ? GreenC.x : 0f), Bunkers[i].y, Bunkers[i].z, Bunkers[i].z * 0.72f);
            if (v > best) best = v;
        }
        return best;
    }
    private static float PondT(float x, float z) { return 1f - Ell(x, z, PondC.x, PondC.y, PondR.x, PondR.y); }

    private static float Height(float x, float z)
    {
        float t = IslandT(x, z);
        float gT = GreenT(x, z), teT = TeeT(x, z);
        float flat = 1f - Mathf.Max(Smooth(-0.15f, 0.1f, gT), Smooth(-0.1f, 0.1f, teT));
        float roll = (0.45f * Mathf.Sin(x * 0.11f + z * 0.05f) + 0.30f * Mathf.Sin(z * 0.075f) * Mathf.Cos(x * 0.065f)) * flat;
        float land = LandH + roll;
        land += 0.30f * Smooth(-0.25f, 0.06f, gT);
        land += 0.12f * Smooth(-0.1f, 0.1f, teT);
        land -= 0.55f * Smooth(0.0f, 0.45f, BunkerT(x, z));
        float shore = Smooth(-0.10f, 0.16f, t);
        float h = Mathf.Lerp(SeabedY, land, shore);
        h = Mathf.Lerp(h, -1.8f, Smooth(0.0f, 0.3f, PondT(x, z)));
        return h;
    }

    // ---------- colors ----------
    private static readonly Color Rough = new Color(0.17f, 0.64f, 0.30f);
    private static readonly Color Fairway = new Color(0.34f, 0.82f, 0.27f);
    private static readonly Color Collar = new Color(0.40f, 0.86f, 0.32f);
    private static readonly Color GreenCol = new Color(0.52f, 0.94f, 0.38f);
    private static readonly Color Sand = new Color(1.00f, 0.94f, 0.72f);
    private static readonly Color Beach = new Color(0.98f, 0.92f, 0.74f);
    private static readonly Color Shallow = new Color(0.70f, 0.95f, 0.88f);
    private static readonly Color Deep = new Color(0.30f, 0.72f, 0.80f);

    private static Color Paint(float x, float z)
    {
        float h = Height(x, z);
        float fT = FairT(x, z), gT = GreenT(x, z), teT = TeeT(x, z), bT = BunkerT(x, z);

        Color c = Rough;
        float n = 0.5f + 0.5f * Mathf.Sin(x * 0.45f) * Mathf.Sin(z * 0.38f + 1.3f);
        c *= 0.94f + 0.12f * n;

        float fw = Smooth(-0.04f, 0.07f, fT);
        float stripe = 0.5f + 0.5f * Mathf.Sin(z * 1.45f + x * 0.35f);
        Color fc = Fairway * (0.93f + 0.14f * Smooth(0.30f, 0.70f, stripe));
        c = Color.Lerp(c, fc, fw);

        float collarW = Smooth(-0.22f, -0.10f, gT);
        c = Color.Lerp(c, Collar, collarW);

        float gw = Smooth(-0.04f, 0.03f, gT);
        float gStripe = 0.5f + 0.5f * Mathf.Sin(z * 3.1f);
        Color gc = GreenCol * (0.95f + 0.09f * Smooth(0.30f, 0.70f, gStripe));
        c = Color.Lerp(c, gc, gw);

        float tw = Smooth(-0.04f, 0.05f, teT);
        c = Color.Lerp(c, GreenCol * 1.02f, tw);

        float bw = Smooth(-0.10f, 0.08f, bT);
        c = Color.Lerp(c, Sand, bw);

        float beach = Smooth(0.7f, 0.25f, h);
        c = Color.Lerp(c, Beach, beach);

        float uw = Smooth(-0.35f, -1.1f, h);
        Color under = Color.Lerp(Shallow, Deep, Smooth(-0.9f, -2.8f, h));
        c = Color.Lerp(c, under, uw);
        c.a = 1f;
        return c;
    }

    // ---------- materials ----------
    private static Material Lit(string name, Color col, float smooth, float metal)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        AssetDatabase.CreateAsset(m, Root + "/Materials/" + name + ".mat");
        return m;
    }

    private static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(m);
    }

    private static void Emit(Material m, Color c)
    {
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
    }

    // ---------- procedural textures ----------
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static int Wrap(int v, int p) { return ((v % p) + p) % p; }

    // Tileable value noise with px x py lattice cells across the 0..1 square.
    private static float VNoise(float u, float v, int px, int py, int seed)
    {
        float x = u * px, y = v * py;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash(Wrap(xi, px), Wrap(yi, py), seed), b = Hash(Wrap(xi + 1, px), Wrap(yi, py), seed);
        float c = Hash(Wrap(xi, px), Wrap(yi + 1, py), seed), d = Hash(Wrap(xi + 1, px), Wrap(yi + 1, py), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Fbm(float u, float v, int baseP, int oct, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < oct; o++)
        {
            int p = baseP << o;
            sum += amp * VNoise(u, v, p, p, seed + o * 13);
            norm += amp; amp *= 0.5f;
        }
        return sum / norm;
    }

    // Tileable Worley: returns F2-F1 (bright thin cell edges) in 0..~1.
    private static float WorleyEdge(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float f1 = 9f, f2 = 9f;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int cx = xi + i, cy = yi + j;
                int wx = Wrap(cx, cells), wy = Wrap(cy, cells);
                float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 71);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
            }
        return f2 - f1;
    }

    private static Texture2D SaveTexture(Texture2D t, string name, bool normalMap, bool srgb, bool mips = true)
    {
        string assetPath = Root + "/Textures/" + name + ".png";
        string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        File.WriteAllBytes(full, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = srgb;
        imp.mipmapEnabled = mips;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.filterMode = FilterMode.Trilinear;
        imp.anisoLevel = 8;
        imp.alphaIsTransparency = !normalMap && !srgb ? false : imp.alphaIsTransparency;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // Grass/turf micro-detail: neutral grey (0.5) with soft clumps and fine blade streaks.
    private static Texture2D MakeGrassDetail(out float[] height, int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        height = new float[n * n];
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float clump = Fbm(u, v, 6, 3, 11);
                float blades = VNoise(u, v, 96, 24, 5) * 0.6f + VNoise(u, v, 160, 40, 9) * 0.4f;
                float h = clump * 0.55f + blades * 0.45f;
                height[y * n + x] = h;
                float g = 0.56f + (h - 0.5f) * 0.42f;
                px[y * n + x] = new Color(g, g, g, 1f);
            }
        t.SetPixels(px); t.Apply(false);
        return t;
    }

    private static Texture2D HeightToNormal(float[] h, int n, float strength)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float l = h[y * n + Wrap(x - 1, n)], r = h[y * n + Wrap(x + 1, n)];
                float d = h[Wrap(y - 1, n) * n + x], u = h[Wrap(y + 1, n) * n + x];
                var nrm = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                px[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
            }
        t.SetPixels(px); t.Apply(false);
        return t;
    }

    // Soft overlapping ripples for the water surface (tileable, analytic normals).
    private static Texture2D MakeWaterNormal(int n)
    {
        int[,] waves = { { 3, 1 }, { -2, 4 }, { 5, 3 }, { 1, -6 }, { -4, -3 }, { 7, 2 } };
        float[] phase = { 0.3f, 1.7f, 2.9f, 4.1f, 5.3f, 0.9f };
        float[] amp = { 1.0f, 0.8f, 0.5f, 0.45f, 0.35f, 0.25f };
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float dx = 0f, dy = 0f;
                for (int w = 0; w < 6; w++)
                {
                    float arg = 2f * Mathf.PI * (waves[w, 0] * u + waves[w, 1] * v) + phase[w];
                    float c = Mathf.Cos(arg) * amp[w];
                    dx += c * waves[w, 0]; dy += c * waves[w, 1];
                }
                var nrm = new Vector3(-dx * 0.045f, -dy * 0.045f, 1f).normalized;
                px[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
            }
        t.SetPixels(px); t.Apply(false);
        return t;
    }

    // Bright wobbly caustic network, tinted aqua, for the seabed.
    private static Texture2D MakeCaustics(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        Color baseC = new Color(0.34f, 0.74f, 0.80f), hi = new Color(0.90f, 1f, 0.95f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float wu = u + 0.025f * (VNoise(u, v, 4, 4, 3) - 0.5f), wv = v + 0.025f * (VNoise(u, v, 4, 4, 8) - 0.5f);
                float e = Mathf.Exp(-WorleyEdge(wu, wv, 7, 21) * 9f);
                float e2 = Mathf.Exp(-WorleyEdge(wu + 0.31f, wv + 0.17f, 11, 37) * 9f) * 0.6f;
                float k = Mathf.Clamp01(e + e2);
                px[y * n + x] = Color.Lerp(baseC, hi, k * 0.75f);
            }
        t.SetPixels(px); t.Apply(false);
        return t;
    }

    // Soft cumulus sprite: flat-ish bottom, billowy top, blue-grey underside shading, alpha edge.
    private static Texture2D MakeCloudSprite(int w, int h, int seed)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var rng = new System.Random(seed);
        int blobs = 9;
        var bx = new float[blobs]; var by = new float[blobs]; var br = new float[blobs];
        for (int i = 0; i < blobs; i++)
        {
            float f = (i + 0.5f) / blobs;
            bx[i] = Mathf.Lerp(0.18f, 0.82f, f) + ((float)rng.NextDouble() - 0.5f) * 0.05f;
            float mid = 1f - Mathf.Abs(f - 0.5f) * 2f;
            br[i] = Mathf.Lerp(0.10f, 0.24f, mid) * (0.8f + 0.4f * (float)rng.NextDouble());
            by[i] = 0.30f + br[i] * 0.55f;
        }
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w, v = (float)y / h;
                float wob = (Fbm(u, v, 4, 3, seed) - 0.5f) * 0.10f;
                float dens = 0f;
                for (int i = 0; i < blobs; i++)
                {
                    float dx = (u + wob - bx[i]) * (w / (float)h) * 0.5f, dy = (v + wob - by[i]);
                    float d = Mathf.Sqrt(dx * dx * 4f + dy * dy) / br[i];
                    dens = Mathf.Max(dens, 1f - d);
                }
                dens -= Smooth(0.34f, 0.18f, v) * 0.9f; // flatten the bottom
                float a = Smooth(0.0f, 0.45f, dens);
                float shade = Smooth(0.30f, 0.62f, v);
                Color c = Color.Lerp(new Color(0.74f, 0.84f, 0.96f), Color.white, shade);
                px[y * w + x] = new Color(c.r, c.g, c.b, a);
            }
        t.SetPixels(px); t.Apply(false);
        return t;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = false, bool shadows = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ---------- main ----------
    [MenuItem("Tools/Golf is Hard/Build Frutiger Aero Course Scene")]
    public static void Build()
    {
        BuildPath();
        EnsureFolder(Root);
        EnsureFolder(Root + "/Materials");
        EnsureFolder(Root + "/Meshes");
        EnsureFolder(Root + "/Textures");

        // ----- generated textures -----
        float[] grassH;
        var grassAlbedo = SaveTexture(MakeGrassDetail(out grassH, 256), "Grass_Detail", false, false);
        var grassNormal = SaveTexture(HeightToNormal(grassH, 256, 2.2f), "Grass_DetailNormal", true, false);
        var waterNormal = SaveTexture(MakeWaterNormal(256), "Water_Ripples_N", true, false);
        var caustics = SaveTexture(MakeCaustics(512), "Seabed_Caustics", false, true);
        var cloudTexA = SaveTexture(MakeCloudSprite(512, 256, 3), "Cloud_A", false, true);
        var cloudTexB = SaveTexture(MakeCloudSprite(512, 256, 19), "Cloud_B", false, true);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ----- ground mesh + painted texture -----
        const int nx = 150, nz = 260;
        const int tw = 1024, th = 1792;
        var verts = new Vector3[(nx + 1) * (nz + 1)];
        var uvs = new Vector2[verts.Length];
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float u = (float)i / nx, v = (float)j / nz;
                float x = Mathf.Lerp(X0, X1, u), z = Mathf.Lerp(Z0, Z1, v);
                verts[j * (nx + 1) + i] = new Vector3(x, Height(x, z), z);
                uvs[j * (nx + 1) + i] = new Vector2(u, v);
            }
        var tris = new int[nx * nz * 6];
        int k = 0;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }
        var mesh = new Mesh { name = "IslandGround", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Root + "/Meshes/IslandGround.asset");

        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true) { name = "IslandPaint", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 8 };
        var px = new Color[tw * th];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
                px[y * tw + x] = Paint(Mathf.Lerp(X0, X1, (x + 0.5f) / tw), Mathf.Lerp(Z0, Z1, (y + 0.5f) / th));
        tex.SetPixels(px); tex.Apply(true);
        AssetDatabase.CreateAsset(tex, Root + "/Meshes/IslandPaint.asset");

        var groundMat = Lit("Ground_Painted", Color.white, 0.38f, 0f);
        groundMat.SetTexture("_BaseMap", tex);
        groundMat.SetTexture("_DetailAlbedoMap", grassAlbedo);
        groundMat.SetTexture("_DetailNormalMap", grassNormal);
        groundMat.SetTextureScale("_DetailAlbedoMap", new Vector2(45f, 78f)); // repeat every ~2 units
        groundMat.SetFloat("_DetailAlbedoMapScale", 0.7f);
        groundMat.SetFloat("_DetailNormalMapScale", 0.45f);
        groundMat.EnableKeyword("_DETAIL_MULX2");
        EditorUtility.SetDirty(groundMat);

        var rootGO = new GameObject("Frutiger Aero Hole");
        var ground = new GameObject("Island Ground");
        ground.transform.SetParent(rootGO.transform, false);
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        ground.AddComponent<MeshRenderer>().sharedMaterial = groundMat;
        ground.AddComponent<MeshCollider>().sharedMesh = mesh;

        // ----- water -----
        var waterMat = Lit("Water_Aqua", new Color(0.18f, 0.80f, 0.92f, 0.78f), 0.97f, 0f);
        MakeTransparent(waterMat);
        waterMat.SetTexture("_BumpMap", waterNormal);
        waterMat.SetTextureScale("_BumpMap", new Vector2(150f, 150f));
        waterMat.SetFloat("_BumpScale", 0.7f);
        waterMat.EnableKeyword("_NORMALMAP");
        var water = Prim(PrimitiveType.Plane, "Ocean", rootGO.transform, new Vector3(0, WaterY, 40), new Vector3(120, 1, 120), waterMat, false, false);

        var seabedMat = Lit("Seabed", Color.white, 0.2f, 0f);
        seabedMat.SetTexture("_BaseMap", caustics);
        seabedMat.SetTextureScale("_BaseMap", new Vector2(100f, 100f));
        EditorUtility.SetDirty(seabedMat);
        Prim(PrimitiveType.Plane, "Seabed", rootGO.transform, new Vector3(0, SeabedY - 0.05f, 40), new Vector3(120, 1, 120), seabedMat, false, false);

        // ----- tee + cup (existing prefabs) -----
        var teeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TeePrefab);
        var holeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HolePrefab);
        if (teeAsset != null)
        {
            var tee = (GameObject)PrefabUtility.InstantiatePrefab(teeAsset, scene);
            tee.transform.SetParent(rootGO.transform, false);
            tee.transform.position = new Vector3(0f, Height(0f, 0f), 0f);
        }
        if (holeAsset != null)
        {
            var cup = (GameObject)PrefabUtility.InstantiatePrefab(holeAsset, scene);
            cup.transform.SetParent(rootGO.transform, false);
            cup.transform.position = new Vector3(GreenC.x, Height(GreenC.x, GreenC.y), GreenC.y);
        }

        // ----- trees (lollipop style) -----
        var trunkMat = Lit("Tree_Trunk", new Color(0.96f, 0.88f, 0.72f), 0.6f, 0f);
        var leafA = Lit("Tree_LeafLime", new Color(0.55f, 0.90f, 0.26f), 0.65f, 0f);
        var leafB = Lit("Tree_LeafEmerald", new Color(0.12f, 0.72f, 0.42f), 0.65f, 0f);
        var trees = new GameObject("Trees"); trees.transform.SetParent(rootGO.transform, false);
        var rng = new System.Random(7);
        int placed = 0, tries = 0;
        while (placed < 70 && tries++ < 4000)
        {
            float x = Mathf.Lerp(X0, X1, (float)rng.NextDouble()), z = Mathf.Lerp(-12f, 112f, (float)rng.NextDouble());
            if (IslandT(x, z) < 0.30f) continue;
            if (FairT(x, z) > -0.30f) continue;
            if (GreenT(x, z) > -0.9f || TeeT(x, z) > -0.8f) continue;
            if (BunkerT(x, z) > -0.8f || PondT(x, z) > -0.5f) continue;
            float h = Height(x, z);
            if (h < 0.5f) continue;
            float s = 0.85f + (float)rng.NextDouble() * 0.9f;
            var t = new GameObject("Tree"); t.transform.SetParent(trees.transform, false);
            t.transform.position = new Vector3(x, h, z);
            Prim(PrimitiveType.Cylinder, "Trunk", t.transform, new Vector3(x, h + 0.9f * s, z), new Vector3(0.35f * s, 0.9f * s, 0.35f * s), trunkMat);
            var leaf = (rng.Next(2) == 0) ? leafA : leafB;
            Prim(PrimitiveType.Sphere, "Canopy", t.transform, new Vector3(x, h + 2.9f * s, z), Vector3.one * 2.6f * s, leaf);
            if (rng.Next(3) == 0)
                Prim(PrimitiveType.Sphere, "CanopyTop", t.transform, new Vector3(x + 0.3f * s, h + 4.2f * s, z), Vector3.one * 1.6f * s, leaf);
            placed++;
        }

        // ----- flowers -----
        var flowers = new GameObject("Flowers"); flowers.transform.SetParent(rootGO.transform, false);
        Color[] fcols = { new Color(1f, 0.55f, 0.75f), new Color(1f, 0.92f, 0.35f), Color.white, new Color(0.65f, 0.75f, 1f) };
        var fmats = new Material[fcols.Length];
        for (int i = 0; i < fcols.Length; i++) fmats[i] = Lit("Flower_" + i, fcols[i], 0.7f, 0f);
        placed = 0; tries = 0;
        while (placed < 140 && tries++ < 6000)
        {
            float x = Mathf.Lerp(X0, X1, (float)rng.NextDouble()), z = Mathf.Lerp(-12f, 112f, (float)rng.NextDouble());
            if (IslandT(x, z) < 0.22f || FairT(x, z) > -0.12f || GreenT(x, z) > -0.4f) continue;
            if (BunkerT(x, z) > -0.3f || PondT(x, z) > -0.2f) continue;
            float h = Height(x, z); if (h < 0.5f) continue;
            Prim(PrimitiveType.Sphere, "Flower", flowers.transform, new Vector3(x, h + 0.12f, z), Vector3.one * 0.28f, fmats[rng.Next(fmats.Length)], false, false);
            placed++;
        }

        // ----- clouds -----
        var cloudMat = Lit("Cloud_White", Color.white, 0.1f, 0f);
        Emit(cloudMat, new Color(0.55f, 0.6f, 0.65f));
        var clouds = new GameObject("Clouds"); clouds.transform.SetParent(rootGO.transform, false);
        for (int i = 0; i < 8; i++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float rad = Mathf.Lerp(120f, 300f, (float)rng.NextDouble());
            Vector3 c = new Vector3(Mathf.Cos(ang) * rad, Mathf.Lerp(45f, 100f, (float)rng.NextDouble()), 40f + Mathf.Sin(ang) * rad);
            var cl = new GameObject("Cloud"); cl.transform.SetParent(clouds.transform, false);
            float cs = Mathf.Lerp(0.8f, 1.6f, (float)rng.NextDouble());
            for (int p = 0; p < 6; p++)
            {
                Vector3 o = new Vector3((p - 2.5f) * 9f * cs, (float)rng.NextDouble() * 4f, (float)rng.NextDouble() * 8f - 4f);
                float r = (9f - Mathf.Abs(p - 2.5f) * 1.6f) * cs;
                Prim(PrimitiveType.Sphere, "Puff", cl.transform, c + o, new Vector3(r * 1.5f, r * 0.8f, r * 1.1f), cloudMat, false, false);
            }
        }

        // ----- cloud sprites (billboards facing the course) -----
        var sprites = new GameObject("Cloud Sprites"); sprites.transform.SetParent(rootGO.transform, false);
        var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        Vector3 look = new Vector3(0f, 10f, 40f);
        for (int i = 0; i < 22; i++)
        {
            var sm = new Material(unlitShader) { name = "CloudSprite_" + i };
            sm.SetTexture("_BaseMap", (i % 2 == 0) ? cloudTexA : cloudTexB);
            sm.SetColor("_BaseColor", Color.white);
            MakeTransparent(sm);
            sm.SetInt("_Cull", 0);
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float rad = Mathf.Lerp(190f, 330f, (float)rng.NextDouble());
            var pos = new Vector3(Mathf.Cos(ang) * rad, Mathf.Lerp(22f, 85f, (float)rng.NextDouble()), 40f + Mathf.Sin(ang) * rad);
            float wdt = Mathf.Lerp(110f, 230f, (float)rng.NextDouble());
            var q = Prim(PrimitiveType.Quad, "CloudSprite", sprites.transform, pos, new Vector3(wdt, wdt * 0.5f, 1f), sm, false, false);
            q.transform.rotation = Quaternion.LookRotation(pos - look);
            AssetDatabase.CreateAsset(sm, Root + "/Materials/CloudSprite_" + i + ".mat");
        }

        // ----- distant hills -----
        var hillMat = Lit("Hill_Green", new Color(0.30f, 0.80f, 0.40f), 0.5f, 0f);
        var hills = new GameObject("Distant Hills"); hills.transform.SetParent(rootGO.transform, false);
        for (int i = 0; i < 7; i++)
        {
            float ang = i * (Mathf.PI * 2f / 7f) + 0.4f;
            float rad = Mathf.Lerp(230f, 330f, (float)rng.NextDouble());
            float sz = Mathf.Lerp(45f, 90f, (float)rng.NextDouble());
            Prim(PrimitiveType.Sphere, "Hill", hills.transform, new Vector3(Mathf.Cos(ang) * rad, -10f, 40f + Mathf.Sin(ang) * rad), new Vector3(sz, sz * 0.38f, sz), hillMat, false, false);
        }

        // ----- bubbles -----
        var bubMat = Lit("Bubble", new Color(0.7f, 0.95f, 1f, 0.22f), 1f, 0f);
        MakeTransparent(bubMat);
        Emit(bubMat, new Color(0.10f, 0.25f, 0.3f));
        var bubbles = new GameObject("Bubbles"); bubbles.transform.SetParent(rootGO.transform, false);
        for (int i = 0; i < 28; i++)
        {
            float x = Mathf.Lerp(-35f, 35f, (float)rng.NextDouble()), z = Mathf.Lerp(-10f, 100f, (float)rng.NextDouble());
            Prim(PrimitiveType.Sphere, "Bubble", bubbles.transform, new Vector3(x, Mathf.Lerp(3f, 16f, (float)rng.NextDouble()), z), Vector3.one * Mathf.Lerp(0.5f, 2.2f, (float)rng.NextDouble()), bubMat, false, false);
        }

        // ----- lighting & sky -----
        var skyMat = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky_Aero" };
        skyMat.SetColor("_SkyTint", new Color(0.28f, 0.62f, 1f));
        skyMat.SetColor("_GroundColor", new Color(0.72f, 0.92f, 1f));
        skyMat.SetFloat("_AtmosphereThickness", 0.75f);
        skyMat.SetFloat("_Exposure", 1.35f);
        skyMat.SetFloat("_SunSize", 0.05f);
        AssetDatabase.CreateAsset(skyMat, Root + "/Materials/Sky_Aero.mat");

        var sunGO = new GameObject("Sun");
        sunGO.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        var sun = sunGO.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.97f, 0.9f);
        sun.intensity = 1.35f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.75f;

        RenderSettings.skybox = skyMat;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.15f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.74f, 0.92f, 1f);
        RenderSettings.fogStartDistance = 130f;
        RenderSettings.fogEndDistance = 520f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        DynamicGI.UpdateEnvironment();

        // ----- camera -----
        var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 55f; cam.nearClipPlane = 0.2f; cam.farClipPlane = 900f;
        cam.clearFlags = CameraClearFlags.Skybox;
        camGO.AddComponent<AudioListener>();
        var ucd = camGO.AddComponent<UniversalAdditionalCameraData>();
        ucd.renderPostProcessing = true;
        camGO.transform.position = new Vector3(0f, 5.5f, -13f);
        camGO.transform.rotation = Quaternion.Euler(14f, 0f, 0f);

        // ----- post processing -----
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.85f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.75f);
        var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
        var grade = profile.Add<ColorAdjustments>(true);
        grade.saturation.Override(18f); grade.contrast.Override(8f); grade.postExposure.Override(0.15f);
        var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.16f); vig.smoothness.Override(0.6f);
        AssetDatabase.CreateAsset(profile, Root + "/FrutigerAeroVolume.asset");
        var volGO = new GameObject("Post Processing Volume");
        var vol = volGO.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Frutiger Aero course built: " + ScenePath);
    }
}
