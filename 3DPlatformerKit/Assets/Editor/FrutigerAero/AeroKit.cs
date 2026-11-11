using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Shared toolkit for the Frutiger Aero course builders: materials, procedural textures,
/// swept / lathed meshes, and sculpted heightfield islands (with real cup holes cut through them).
/// </summary>
public static class AeroKit
{
    public static string Root = "Assets/FrutigerGolfCourse/NineHole";
    public const float WaterY = -0.45f;
    public const float SeabedY = -3.0f;
    public const float CupCutRadius = 1.09f;

    private static Dictionary<string, Material> matCache = new Dictionary<string, Material>();
    public static Texture2D GrassDetail, GrassDetailNormal, WaterNormal, Caustics, CloudA, CloudB, SoftDot, Streaks;

    // ===================================================================== setup
    public static void Begin(string root)
    {
        Root = root;
        matCache.Clear();
        EnsureFolder(Root);
        EnsureFolder(Root + "/Materials");
        EnsureFolder(Root + "/Meshes");
        EnsureFolder(Root + "/Textures");
        EnsureFolder(Root + "/Physics");
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    public static float Smooth(float a, float b, float v)
    {
        float t = Mathf.Clamp01((v - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // ===================================================================== materials
    private static Material GetOrCreateMat(string name, Shader shader)
    {
        if (matCache.TryGetValue(name, out var cached)) return cached;
        string path = Root + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        else
        {
            m.shader = shader;
            m.shaderKeywords = new string[0];
        }
        matCache[name] = m;
        return m;
    }

    /// <summary>URP Lit. Alpha below 1 makes it transparent.</summary>
    public static Material Lit(string name, Color col, float smooth, float metal = 0f, Color? emission = null, bool doubleSided = false)
    {
        var m = GetOrCreateMat(name, Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Surface", 0f);
        m.SetOverrideTag("RenderType", "Opaque");
        m.SetInt("_SrcBlend", (int)BlendMode.One);
        m.SetInt("_DstBlend", (int)BlendMode.Zero);
        m.SetInt("_ZWrite", 1);
        m.renderQueue = -1;
        m.SetShaderPassEnabled("ShadowCaster", true);
        if (col.a < 0.999f) MakeTransparent(m);
        if (emission.HasValue) Emit(m, emission.Value);
        m.SetFloat("_Cull", doubleSided ? 0f : 2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    public static Material Unlit(string name, Color col, Texture tex = null)
    {
        var m = GetOrCreateMat(name, Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", col);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        if (col.a < 0.999f || tex != null) MakeTransparent(m);
        m.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    public static Material Particle(string name, Color col, Texture tex)
    {
        var m = GetOrCreateMat(name, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    public static void MakeTransparent(Material m)
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

    public static void Emit(Material m, Color c)
    {
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
    }

    public static void AddGrassDetail(Material m, Vector2 tiling, float albedo = 0.7f, float normal = 0.45f)
    {
        m.SetTexture("_DetailAlbedoMap", GrassDetail);
        m.SetTexture("_DetailNormalMap", GrassDetailNormal);
        m.SetTextureScale("_DetailAlbedoMap", tiling);
        m.SetFloat("_DetailAlbedoMapScale", albedo);
        m.SetFloat("_DetailNormalMapScale", normal);
        m.EnableKeyword("_DETAIL_MULX2");
        EditorUtility.SetDirty(m);
    }

    public static PhysicsMaterial Bouncy(string name, float bounce, float friction = 0.3f)
    {
        string path = Root + "/Physics/" + name + ".physicMaterial";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (pm == null) { pm = new PhysicsMaterial(name); AssetDatabase.CreateAsset(pm, path); }
        pm.bounciness = bounce;
        pm.dynamicFriction = friction;
        pm.staticFriction = friction;
        pm.bounceCombine = PhysicsMaterialCombine.Maximum;
        pm.frictionCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(pm);
        return pm;
    }

    // ===================================================================== objects
    public static Mesh SaveMesh(Mesh m, string name)
    {
        string path = Root + "/Meshes/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            // rewrite the data through the Mesh API so both rendering and physics see the new geometry
            existing.Clear();
            existing.indexFormat = m.indexFormat;
            existing.vertices = m.vertices;
            existing.normals = m.normals;
            existing.tangents = m.tangents;
            existing.uv = m.uv;
            existing.subMeshCount = 1;
            existing.SetTriangles(m.triangles, 0);
            existing.RecalculateBounds();
            existing.name = name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
            return existing;
        }
        m.name = name;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool collider,
        PhysicsMaterial pm = null, bool shadows = true, bool convex = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
        if (collider)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = convex;
            if (pm != null) mc.sharedMaterial = pm;
        }
        return go;
    }

    public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat,
        bool collider = false, bool shadows = true, Quaternion? localRot = null, PhysicsMaterial pm = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot ?? Quaternion.identity;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
        var col = go.GetComponent<Collider>();
        if (!collider) Object.DestroyImmediate(col);
        else if (pm != null) col.sharedMaterial = pm;
        return go;
    }

    public static GameObject Group(string name, Transform parent)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        return g;
    }

    public static void MarkStatic(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
    }

    // ===================================================================== sweep meshes
    /// <summary>
    /// Sweeps a 2D profile (x = right, y = up in the path frame) along a 3D path.
    /// Visible side: the left-hand normal of the profile direction, i.e. a profile running left-to-right faces up,
    /// a clockwise closed profile faces outward, a counter-clockwise one faces inward.
    /// </summary>
    public static Mesh Sweep(IList<Vector3> path, IList<Vector2> profile, bool closedProfile, bool hardEdges,
        Func<int, Vector3> upAt = null, bool closedPath = false, float uvScale = 1f)
    {
        int n = path.Count;
        // columns
        var colPos = new List<Vector2>();
        var colU = new List<float>();
        var pairs = new List<Vector2Int>();
        int m = profile.Count;
        int segCount = closedProfile ? m : m - 1;
        if (hardEdges)
        {
            float acc = 0f;
            for (int s = 0; s < segCount; s++)
            {
                Vector2 a = profile[s], b = profile[(s + 1) % m];
                colPos.Add(a); colU.Add(acc);
                acc += Vector2.Distance(a, b);
                colPos.Add(b); colU.Add(acc);
                pairs.Add(new Vector2Int(colPos.Count - 2, colPos.Count - 1));
            }
        }
        else
        {
            float acc = 0f;
            for (int s = 0; s < m; s++)
            {
                if (s > 0) acc += Vector2.Distance(profile[s - 1], profile[s]);
                colPos.Add(profile[s]); colU.Add(acc);
            }
            if (closedProfile) { acc += Vector2.Distance(profile[m - 1], profile[0]); colPos.Add(profile[0]); colU.Add(acc); }
            for (int c = 0; c < colPos.Count - 1; c++) pairs.Add(new Vector2Int(c, c + 1));
        }
        int C = colPos.Count;
        int rows = closedPath ? n + 1 : n;

        var verts = new Vector3[rows * C];
        var uvs = new Vector2[rows * C];
        Vector3 prevR = Vector3.right;
        float v = 0f;
        for (int r = 0; r < rows; r++)
        {
            int i = r % n;
            Vector3 p = path[i];
            Vector3 next = closedPath ? path[(i + 1) % n] : path[Mathf.Min(i + 1, n - 1)];
            Vector3 prev = closedPath ? path[(i - 1 + n) % n] : path[Mathf.Max(i - 1, 0)];
            Vector3 T = (next - prev).normalized;
            Vector3 up = upAt != null ? upAt(i) : Vector3.up;
            Vector3 R = Vector3.Cross(up, T);
            if (R.sqrMagnitude < 1e-6f) R = prevR; else R.Normalize();
            prevR = R;
            Vector3 U = Vector3.Cross(T, R).normalized;
            if (r > 0) v += Vector3.Distance(path[(r - 1) % n], p);
            for (int c = 0; c < C; c++)
            {
                verts[r * C + c] = p + R * colPos[c].x + U * colPos[c].y;
                uvs[r * C + c] = new Vector2(colU[c] * uvScale, v * uvScale);
            }
        }
        var tris = new List<int>((rows - 1) * pairs.Count * 6);
        for (int r = 0; r < rows - 1; r++)
            foreach (var pr in pairs)
            {
                int a = r * C + pr.x, b = r * C + pr.y, cc = (r + 1) * C + pr.x, d = (r + 1) * C + pr.y;
                tris.Add(a); tris.Add(cc); tris.Add(b);
                tris.Add(b); tris.Add(cc); tris.Add(d);
            }

        var vlist = new List<Vector3>(verts);
        var ulist = new List<Vector2>(uvs);
        // End caps: close both ends of a solid so nothing (the ball included) can slip inside the shell.
        if (closedProfile && !closedPath && profile.Count >= 3)
        {
            var capTris = EarClip(profile);
            for (int end = 0; end < 2; end++)
            {
                int i = end == 0 ? 0 : n - 1;
                Vector3 p = path[i];
                Vector3 next = path[Mathf.Min(i + 1, n - 1)], prev = path[Mathf.Max(i - 1, 0)];
                Vector3 T = (next - prev).normalized;
                Vector3 up = upAt != null ? upAt(i) : Vector3.up;
                Vector3 R = Vector3.Cross(up, T); if (R.sqrMagnitude < 1e-6f) R = Vector3.right; R.Normalize();
                Vector3 U = Vector3.Cross(T, R).normalized;
                int baseIdx = vlist.Count;
                foreach (var q in profile) { vlist.Add(p + R * q.x + U * q.y); ulist.Add(q * uvScale); }
                for (int k = 0; k < capTris.Count; k += 3)
                {
                    // start cap faces backwards (-T), end cap faces forwards (+T)
                    if (end == 0) { tris.Add(baseIdx + capTris[k]); tris.Add(baseIdx + capTris[k + 1]); tris.Add(baseIdx + capTris[k + 2]); }
                    else { tris.Add(baseIdx + capTris[k]); tris.Add(baseIdx + capTris[k + 2]); tris.Add(baseIdx + capTris[k + 1]); }
                }
            }
        }
        var mesh = new Mesh { indexFormat = vlist.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vlist); mesh.SetUVs(0, ulist); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Ear-clipping triangulation of a simple polygon; triangles keep the polygon's winding.</summary>
    public static List<int> EarClip(IList<Vector2> poly)
    {
        var idx = new List<int>();
        for (int i = 0; i < poly.Count; i++) idx.Add(i);
        float area = 0f;
        for (int i = 0; i < poly.Count; i++) { var a = poly[i]; var b = poly[(i + 1) % poly.Count]; area += a.x * b.y - b.x * a.y; }
        bool ccw = area > 0f;
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        var tris = new List<int>();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 20000)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i - 1 + idx.Count) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                float cr = Cross(poly[ia], poly[ib], poly[ic]);
                if (ccw ? cr < -1e-7f : cr > 1e-7f) continue; // reflex corner
                bool blocked = false;
                if (Mathf.Abs(cr) > 1e-7f)
                    for (int j = 0; j < idx.Count && !blocked; j++)
                    {
                        int ij = idx[j];
                        if (ij == ia || ij == ib || ij == ic) continue;
                        if (Inside(poly[ij], poly[ia], poly[ib], poly[ic])) blocked = true;
                    }
                if (blocked) continue;
                if (Mathf.Abs(cr) > 1e-7f) { tris.Add(ia); tris.Add(ib); tris.Add(ic); }
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }
        if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
        return tris;
    }

    // ===================================================================== lathe meshes
    /// <summary>
    /// Revolves a (radius, y) profile around Y. Visible side: outer walls listed bottom-to-top,
    /// top surfaces listed outside-to-inside, undersides inside-to-outside.
    /// skip(angleDeg, y) lets you punch openings (doorways) into the surface.
    /// </summary>
    public static Mesh Lathe(IList<Vector2> profile, int segs, bool hardEdges = false, bool planarUV = false, float planarScale = 0.25f,
        float a0 = 0f, float a1 = 360f, Func<float, float, bool> skip = null)
    {
        var colPos = new List<Vector2>();
        var colU = new List<float>();
        var pairs = new List<Vector2Int>();
        int m = profile.Count;
        if (hardEdges)
        {
            float acc = 0f;
            for (int s = 0; s < m - 1; s++)
            {
                colPos.Add(profile[s]); colU.Add(acc);
                acc += Vector2.Distance(profile[s], profile[s + 1]);
                colPos.Add(profile[s + 1]); colU.Add(acc);
                pairs.Add(new Vector2Int(colPos.Count - 2, colPos.Count - 1));
            }
        }
        else
        {
            float acc = 0f;
            for (int s = 0; s < m; s++)
            {
                if (s > 0) acc += Vector2.Distance(profile[s - 1], profile[s]);
                colPos.Add(profile[s]); colU.Add(acc);
            }
            for (int c = 0; c < m - 1; c++) pairs.Add(new Vector2Int(c, c + 1));
        }
        int C = colPos.Count;
        int rows = segs + 1;
        var verts = new Vector3[rows * C];
        var uvs = new Vector2[rows * C];
        for (int k = 0; k < rows; k++)
        {
            float a = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (float)k / segs);
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            for (int c = 0; c < C; c++)
            {
                var p = new Vector3(colPos[c].x * ca, colPos[c].y, colPos[c].x * sa);
                verts[k * C + c] = p;
                uvs[k * C + c] = planarUV ? new Vector2(p.x * planarScale, p.z * planarScale) : new Vector2((float)k / segs * 8f, colU[c] * 0.25f);
            }
        }
        var tris = new List<int>();
        for (int k = 0; k < segs; k++)
        {
            float aMid = Mathf.Lerp(a0, a1, (k + 0.5f) / segs);
            foreach (var pr in pairs)
            {
                if (skip != null && skip(aMid, (colPos[pr.x].y + colPos[pr.y].y) * 0.5f)) continue;
                int a = k * C + pr.x, b = k * C + pr.y, c2 = (k + 1) * C + pr.x, d = (k + 1) * C + pr.y;
                tris.Add(a); tris.Add(b); tris.Add(c2);
                tris.Add(c2); tris.Add(b); tris.Add(d);
            }
        }
        var mesh = new Mesh { indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = verts; mesh.uv = uvs; mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }

    // ===================================================================== profiles & paths
    public static List<Vector2> Circle(float radius, Vector2 center, int segs, bool clockwise)
    {
        var l = new List<Vector2>();
        for (int i = 0; i < segs; i++)
        {
            float a = (clockwise ? -1f : 1f) * i / (float)segs * Mathf.PI * 2f;
            l.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
        return l;
    }

    /// <summary>Closed rounded slab; top surface at y=0, outward visible.</summary>
    public static List<Vector2> Slab(float width, float thickness, float bevel = 0.12f)
    {
        float w = width * 0.5f;
        return new List<Vector2>
        {
            new Vector2(-w, -bevel), new Vector2(-w + bevel * 0.3f, -bevel * 0.3f), new Vector2(-w + bevel, 0f),
            new Vector2(w - bevel, 0f), new Vector2(w - bevel * 0.3f, -bevel * 0.3f), new Vector2(w, -bevel),
            new Vector2(w, -thickness), new Vector2(-w, -thickness)
        };
    }

    /// <summary>Closed slab spanning lateral range [x0, x1].</summary>
    public static List<Vector2> Band(float x0, float x1, float thickness)
    {
        return new List<Vector2> { new Vector2(x0, 0f), new Vector2(x1, 0f), new Vector2(x1, -thickness), new Vector2(x0, -thickness) };
    }

    /// <summary>Closed U channel with thickness. Floor at y=0, inner surface visible from inside, shell from outside.</summary>
    public static List<Vector2> UChannel(float halfWidth, float depth, float wall, int segs = 14)
    {
        var l = new List<Vector2>();
        l.Add(new Vector2(-halfWidth - wall, depth));
        for (int i = 0; i <= segs; i++)
        {
            float t = Mathf.PI + Mathf.PI * i / segs;
            l.Add(new Vector2(halfWidth * Mathf.Cos(t), depth + depth * Mathf.Sin(t)));
        }
        l.Add(new Vector2(halfWidth + wall, depth));
        for (int i = segs; i >= 0; i--)
        {
            float t = Mathf.PI + Mathf.PI * i / segs;
            if (i == segs || i == 0) continue;
            l.Add(new Vector2((halfWidth + wall) * Mathf.Cos(t), depth + (depth + wall) * Mathf.Sin(t)));
        }
        return l;
    }

    /// <summary>Rounded wall profile standing on y=0.</summary>
    public static List<Vector2> Wall(float width, float height)
    {
        float w = width * 0.5f;
        var l = new List<Vector2> { new Vector2(-w, -0.4f), new Vector2(-w, height - w) };
        for (int i = 1; i < 8; i++)
        {
            float a = Mathf.PI - Mathf.PI * i / 8f;
            l.Add(new Vector2(Mathf.Cos(a) * w, height - w + Mathf.Sin(a) * w));
        }
        l.Add(new Vector2(w, height - w)); l.Add(new Vector2(w, -0.4f));
        return l;
    }

    public static List<Vector3> CatmullRom(IList<Vector3> pts, float spacing)
    {
        var outPts = new List<Vector3>();
        int n = pts.Count;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, n - 1)];
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / spacing));
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                outPts.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        outPts.Add(pts[n - 1]);
        return outPts;
    }

    // ===================================================================== textures
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

    public static float VNoise(float u, float v, int px, int py, int seed)
    {
        float x = u * px, y = v * py;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash(Wrap(xi, px), Wrap(yi, py), seed), b = Hash(Wrap(xi + 1, px), Wrap(yi, py), seed);
        float c = Hash(Wrap(xi, px), Wrap(yi + 1, py), seed), d = Hash(Wrap(xi + 1, px), Wrap(yi + 1, py), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    public static float Fbm(float u, float v, int baseP, int oct, int seed)
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

    public static Texture2D SaveTexture(Texture2D t, string name, bool normalMap, bool srgb, bool alphaTransparency = false,
        TextureWrapMode wrap = TextureWrapMode.Repeat)
    {
        string assetPath = Root + "/Textures/" + name + ".png";
        string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        File.WriteAllBytes(full, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = srgb;
        imp.mipmapEnabled = true;
        imp.wrapMode = wrap;
        imp.filterMode = FilterMode.Trilinear;
        imp.anisoLevel = 8;
        imp.alphaIsTransparency = alphaTransparency;
        imp.maxTextureSize = 2048;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Texture2D Fill(int w, int h, Func<float, float, Color> f)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
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

    public static void MakeSharedTextures()
    {
        // Grass micro detail (neutral grey around 0.56) and its normal map.
        int n = 256;
        var hgt = new float[n * n];
        var grass = Fill(n, n, (u, v) =>
        {
            float clump = Fbm(u, v, 6, 3, 11);
            float blades = VNoise(u, v, 96, 24, 5) * 0.6f + VNoise(u, v, 160, 40, 9) * 0.4f;
            float h = clump * 0.55f + blades * 0.45f;
            int ix = Mathf.Clamp((int)(u * n), 0, n - 1), iy = Mathf.Clamp((int)(v * n), 0, n - 1);
            hgt[iy * n + ix] = h;
            float g = 0.56f + (h - 0.5f) * 0.42f;
            return new Color(g, g, g, 1f);
        });
        GrassDetail = SaveTexture(grass, "Grass_Detail", false, false);
        GrassDetailNormal = SaveTexture(HeightToNormal(hgt, n, 2.2f), "Grass_DetailNormal", true, false);

        // Water ripples (tileable, analytic).
        int[,] waves = { { 3, 1 }, { -2, 4 }, { 5, 3 }, { 1, -6 }, { -4, -3 }, { 7, 2 } };
        float[] phase = { 0.3f, 1.7f, 2.9f, 4.1f, 5.3f, 0.9f };
        float[] amp = { 1.0f, 0.8f, 0.5f, 0.45f, 0.35f, 0.25f };
        WaterNormal = SaveTexture(Fill(256, 256, (u, v) =>
        {
            float dx = 0f, dy = 0f;
            for (int w = 0; w < 6; w++)
            {
                float arg = 2f * Mathf.PI * (waves[w, 0] * u + waves[w, 1] * v) + phase[w];
                float c = Mathf.Cos(arg) * amp[w];
                dx += c * waves[w, 0]; dy += c * waves[w, 1];
            }
            var nrm = new Vector3(-dx * 0.045f, -dy * 0.045f, 1f).normalized;
            return new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
        }), "Water_Ripples_N", true, false);

        // Seabed caustics.
        Color baseC = new Color(0.34f, 0.74f, 0.80f), hi = new Color(0.90f, 1f, 0.95f);
        Caustics = SaveTexture(Fill(512, 512, (u, v) =>
        {
            float wu = u + 0.025f * (VNoise(u, v, 4, 4, 3) - 0.5f), wv = v + 0.025f * (VNoise(u, v, 4, 4, 8) - 0.5f);
            float e = Mathf.Exp(-WorleyEdge(wu, wv, 7, 21) * 9f);
            float e2 = Mathf.Exp(-WorleyEdge(wu + 0.31f, wv + 0.17f, 11, 37) * 9f) * 0.6f;
            return Color.Lerp(baseC, hi, Mathf.Clamp01(e + e2) * 0.75f);
        }), "Seabed_Caustics", false, true);

        CloudA = SaveTexture(CloudSprite(512, 256, 3), "Cloud_A", false, true, true, TextureWrapMode.Clamp);
        CloudB = SaveTexture(CloudSprite(512, 256, 19), "Cloud_B", false, true, true, TextureWrapMode.Clamp);

        SoftDot = SaveTexture(Fill(64, 64, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - d);
            return new Color(1f, 1f, 1f, a * a);
        }), "Soft_Dot", false, true, true, TextureWrapMode.Clamp);

        // Waterfall streaks: tileable vertical streaks with foamy highlights.
        Streaks = SaveTexture(Fill(128, 256, (u, v) =>
        {
            float s = VNoise(u, v, 24, 3, 41) * 0.6f + VNoise(u, v, 48, 6, 43) * 0.4f;
            float foam = Smooth(0.55f, 0.85f, s);
            Color c = Color.Lerp(new Color(0.55f, 0.88f, 1f), Color.white, foam);
            return new Color(c.r, c.g, c.b, Mathf.Lerp(0.55f, 0.95f, s));
        }), "Waterfall_Streaks", false, true, true);
    }

    private static Texture2D CloudSprite(int w, int h, int seed)
    {
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
        return Fill(w, h, (u, v) =>
        {
            float wob = (Fbm(u, v, 4, 3, seed) - 0.5f) * 0.10f;
            float dens = 0f;
            for (int i = 0; i < blobs; i++)
            {
                float dx = (u + wob - bx[i]) * (w / (float)h) * 0.5f, dy = (v + wob - by[i]);
                float d = Mathf.Sqrt(dx * dx * 4f + dy * dy) / br[i];
                dens = Mathf.Max(dens, 1f - d);
            }
            dens -= Smooth(0.34f, 0.18f, v) * 0.9f;
            float a = Smooth(0.0f, 0.45f, dens);
            Color c = Color.Lerp(new Color(0.80f, 0.89f, 0.98f), Color.white, Smooth(0.30f, 0.62f, v));
            return new Color(c.r, c.g, c.b, a);
        });
    }
}

// ========================================================================= heightfield islands
public class Blob
{
    public float x, z, rx, rz, rot, h;
    public Blob(float x, float z, float rx, float rz, float h = 0f, float rotDeg = 0f)
    { this.x = x; this.z = z; this.rx = rx; this.rz = rz; this.h = h; rot = rotDeg * Mathf.Deg2Rad; }
    public float Ell(float px, float pz, float scale = 1f)
    {
        float dx = px - x, dz = pz - z;
        if (rot != 0f) { float c = Mathf.Cos(rot), s = Mathf.Sin(rot); float ndx = dx * c - dz * s; dz = dx * s + dz * c; dx = ndx; }
        dx /= rx * scale; dz /= rz * scale;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
    public float T(float px, float pz, float scale = 1f) { return 1f - Ell(px, pz, scale); }
}

public class LandPath
{
    public List<Vector2> pts = new List<Vector2>();
    public List<float> r = new List<float>();
    public float extra = 11f;
    public bool fairway = true;
    public bool protect = false;
    public LandPath Add(float x, float z, float radius) { pts.Add(new Vector2(x, z)); r.Add(radius); return this; }

    /// <summary>Returns 1 - d / (radius + pad) for the closest point on the polyline.</summary>
    public float T(float x, float z, float pad)
    {
        float best = -99f;
        var p = new Vector2(x, z);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            float d = Vector2.Distance(p, a + ab * t);
            float rad = Mathf.Lerp(r[i], r[i + 1], t) + pad;
            float v = 1f - d / rad;
            if (v > best) best = v;
        }
        return best;
    }
}

/// <summary>
/// A structure footprint (bridge deck, pipe, lane...) that the terrain must stay under.
/// pts carry the structure's reference height (deck top / pipe floor); ground inside halfWidth is held at
/// least `below` under it, blending back to natural terrain over `blend` metres. Ends are not capped,
/// so the ground just past either end is untouched (the ball can roll on and off).
/// </summary>
public class Corridor
{
    public List<Vector3> pts;
    public float halfWidth, below, blend = 2.5f;
    public Vector4 bbox; // xmin, xmax, zmin, zmax (expanded)

    public Corridor(List<Vector3> path, float halfWidth, float below, float blend = 2.5f, float spacing = 1.5f)
    {
        // thin the path so the per-sample cost stays low
        pts = new List<Vector3> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
            if (Vector3.Distance(pts[pts.Count - 1], path[i]) >= spacing) pts.Add(path[i]);
        pts.Add(path[path.Count - 1]);
        this.halfWidth = halfWidth; this.below = below; this.blend = blend;
        float m = halfWidth + blend + 0.5f;
        bbox = new Vector4(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);
        foreach (var p in pts) bbox = new Vector4(Mathf.Min(bbox.x, p.x - m), Mathf.Max(bbox.y, p.x + m), Mathf.Min(bbox.z, p.z - m), Mathf.Max(bbox.w, p.z + m));
    }

    /// <summary>Returns the max ground height allowed here, or +inf when outside the corridor.</summary>
    public float Limit(float x, float z, float natural)
    {
        if (x < bbox.x || x > bbox.y || z < bbox.z || z > bbox.w) return float.PositiveInfinity;
        float bestD = float.MaxValue, bestY = 0f;
        var p = new Vector2(x, z);
        // flat cut-off planes at both ends: nothing in front of an entrance or past an exit is touched
        Vector2 s0 = new Vector2(pts[0].x, pts[0].z), s1 = new Vector2(pts[1].x, pts[1].z);
        Vector2 e0 = new Vector2(pts[pts.Count - 1].x, pts[pts.Count - 1].z), e1 = new Vector2(pts[pts.Count - 2].x, pts[pts.Count - 2].z);
        if (Vector2.Dot(p - s0, s1 - s0) < 0f || Vector2.Dot(p - e0, e1 - e0) < 0f) return float.PositiveInfinity;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = new Vector2(pts[i].x, pts[i].z), b = new Vector2(pts[i + 1].x, pts[i + 1].z), ab = b - a;
            float t = Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude);
            if ((i == 0 && t < 0f) || (i == pts.Count - 2 && t > 1f)) continue; // no end caps
            t = Mathf.Clamp01(t);
            float dd = Vector2.Distance(p, a + ab * t);
            if (dd < bestD) { bestD = dd; bestY = Mathf.Lerp(pts[i].y, pts[i + 1].y, t); }
        }
        if (bestD > halfWidth + blend) return float.PositiveInfinity;
        float lim = bestY - below;
        if (bestD <= halfWidth) return lim;
        float w = AeroKit.Smooth(halfWidth + blend, halfWidth, bestD);
        return Mathf.Lerp(natural, Mathf.Min(natural, lim), w);
    }
}

public class IslandDef
{
    public List<Corridor> clamps = new List<Corridor>();
    public string name;
    public float landH = 1.0f, roll = 1f, greenRaise = 0.30f;
    public List<LandPath> paths = new List<LandPath>();
    public List<Blob> land = new List<Blob>(), fair = new List<Blob>(), bunkers = new List<Blob>(),
        ponds = new List<Blob>(), bumps = new List<Blob>(), flats = new List<Blob>(), cuts = new List<Blob>();
    public bool hasTee, hasGreen, hasCup;
    public Vector2 tee, cup;
    public float teeRx = 6.5f, teeRz = 5.5f;
    public Blob green;
    public Func<float, float, float> custom;
    public float cell = 0.6f, texPerUnit = 9f;
    public List<Vector3> pondWater = new List<Vector3>(); // filled by builder: x, waterY, z

    // ---- fields ----
    public float IslandT(float x, float z)
    {
        float t = -99f;
        foreach (var p in paths) t = Mathf.Max(t, p.T(x, z, p.extra));
        foreach (var b in land) t = Mathf.Max(t, b.T(x, z));
        if (hasGreen) t = Mathf.Max(t, green.T(x, z, 1.9f));
        if (hasTee) t = Mathf.Max(t, 1f - Ell(x, z, tee.x, tee.y + 2f, teeRx * 2.6f, teeRz * 2.8f));
        foreach (var c in cuts) t = Mathf.Min(t, c.Ell(x, z) - 1f);
        return t;
    }
    public float FairT(float x, float z)
    {
        float t = -99f;
        foreach (var p in paths) if (p.fairway) t = Mathf.Max(t, p.T(x, z, 0f));
        foreach (var b in fair) t = Mathf.Max(t, b.T(x, z));
        return t;
    }
    public float ProtectT(float x, float z)
    {
        float t = -99f;
        foreach (var p in paths) if (p.protect) t = Mathf.Max(t, p.T(x, z, p.extra));
        return t;
    }
    public float GreenT(float x, float z) { return hasGreen ? green.T(x, z) : -99f; }
    public float TeeT(float x, float z) { return hasTee ? 1f - Ell(x, z, tee.x, tee.y, teeRx, teeRz) : -99f; }
    public float BunkerT(float x, float z) { float t = -99f; foreach (var b in bunkers) t = Mathf.Max(t, b.T(x, z)); return t; }
    public float PondT(float x, float z) { float t = -99f; foreach (var b in ponds) t = Mathf.Max(t, b.T(x, z)); return t; }

    private static float Ell(float x, float z, float cx, float cz, float rx, float rz)
    {
        float dx = (x - cx) / rx, dz = (z - cz) / rz;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public float LandHeight(float x, float z)
    {
        float gT = GreenT(x, z), teT = TeeT(x, z);
        float flat = Mathf.Max(AeroKit.Smooth(-0.15f, 0.1f, gT), AeroKit.Smooth(-0.1f, 0.1f, teT));
        foreach (var f in flats) flat = Mathf.Max(flat, AeroKit.Smooth(0f, 0.35f, f.T(x, z)));
        if (hasCup) flat = Mathf.Max(flat, AeroKit.Smooth(5f, 2f, Vector2.Distance(new Vector2(x, z), cup)));
        float rollV = roll * (0.45f * Mathf.Sin(x * 0.11f + z * 0.05f) + 0.30f * Mathf.Sin(z * 0.075f) * Mathf.Cos(x * 0.065f)) * (1f - flat);
        float h = landH + rollV;
        if (hasGreen) h += greenRaise * AeroKit.Smooth(-0.25f, 0.06f, gT);
        h += 0.12f * AeroKit.Smooth(-0.1f, 0.1f, teT);
        foreach (var b in bumps) h += b.h * AeroKit.Smooth(0f, 1f, b.T(x, z));
        if (custom != null) h += custom(x, z);
        h -= 0.55f * AeroKit.Smooth(0.0f, 0.45f, BunkerT(x, z));
        return h;
    }

    public float Height(float x, float z)
    {
        float t = IslandT(x, z);
        float land = LandHeight(x, z);
        float shore = AeroKit.Smooth(-0.10f, 0.16f, t);
        float h = Mathf.Lerp(AeroKit.SeabedY, land, shore);
        if (ponds.Count > 0)
        {
            float keep = 1f - AeroKit.Smooth(-0.05f, 0.12f, ProtectT(x, z));
            foreach (var p in ponds)
            {
                float pw = AeroKit.Smooth(0f, 0.3f, p.T(x, z)) * keep;
                if (pw > 0f) h = Mathf.Lerp(h, land - p.h, pw);
            }
        }
        // keep the ground under bridges, pipes and lanes so nothing pokes through them
        foreach (var c in clamps) h = Mathf.Min(h, c.Limit(x, z, h));
        return h;
    }

    /// <summary>Terrain height before structure carving (used for painting, so carved beds keep their grass colour).</summary>
    public float HeightNatural(float x, float z)
    {
        var saved = clamps;
        clamps = noClamps;
        float h = Height(x, z);
        clamps = saved;
        return h;
    }
    private static readonly List<Corridor> noClamps = new List<Corridor>();
}

public static class AeroIsland
{
    const float CupGridCut = 2.2f;      // grid triangles touching this radius are removed
    const float CupCollarOuter = 3.1f;  // collar overlaps the jagged grid edge
    static readonly Color Rough = new Color(0.17f, 0.64f, 0.30f);
    static readonly Color Fairway = new Color(0.34f, 0.82f, 0.27f);
    static readonly Color Collar = new Color(0.40f, 0.86f, 0.32f);
    static readonly Color GreenCol = new Color(0.52f, 0.94f, 0.38f);
    static readonly Color Sand = new Color(1.00f, 0.93f, 0.70f);
    static readonly Color Beach = new Color(0.98f, 0.92f, 0.74f);
    static readonly Color Shallow = new Color(0.70f, 0.95f, 0.88f);
    static readonly Color Deep = new Color(0.30f, 0.72f, 0.80f);

    static Color Paint(IslandDef d, float x, float z)
    {
        float h = d.HeightNatural(x, z);
        float fT = d.FairT(x, z), gT = d.GreenT(x, z), teT = d.TeeT(x, z), bT = d.BunkerT(x, z), pT = d.PondT(x, z);

        Color c = Rough;
        float n = 0.5f + 0.5f * Mathf.Sin(x * 0.45f) * Mathf.Sin(z * 0.38f + 1.3f);
        c *= 0.94f + 0.12f * n;

        float stripe = 0.5f + 0.5f * Mathf.Sin(z * 1.45f + x * 0.35f);
        c = Color.Lerp(c, Fairway * (0.93f + 0.14f * AeroKit.Smooth(0.3f, 0.7f, stripe)), AeroKit.Smooth(-0.04f, 0.07f, fT));
        c = Color.Lerp(c, Collar, AeroKit.Smooth(-0.22f, -0.10f, gT));
        float gStripe = 0.5f + 0.5f * Mathf.Sin(z * 3.1f);
        c = Color.Lerp(c, GreenCol * (0.95f + 0.09f * AeroKit.Smooth(0.3f, 0.7f, gStripe)), AeroKit.Smooth(-0.04f, 0.03f, gT));
        c = Color.Lerp(c, GreenCol * 1.02f, AeroKit.Smooth(-0.04f, 0.05f, teT));
        c = Color.Lerp(c, Sand, AeroKit.Smooth(-0.10f, 0.08f, bT));

        // beach near the sea
        c = Color.Lerp(c, Beach, AeroKit.Smooth(0.7f, 0.25f, h));
        // sandy pond rims, then underwater tint
        if (pT > -0.3f)
        {
            float keep = 1f - AeroKit.Smooth(-0.05f, 0.12f, d.ProtectT(x, z));
            c = Color.Lerp(c, Beach, AeroKit.Smooth(-0.18f, -0.02f, pT) * keep);
            c = Color.Lerp(c, Color.Lerp(Shallow, Deep, AeroKit.Smooth(0.05f, 0.5f, pT)), AeroKit.Smooth(0.0f, 0.12f, pT) * keep);
        }
        float uw = AeroKit.Smooth(-0.35f, -1.1f, h);
        c = Color.Lerp(c, Color.Lerp(Shallow, Deep, AeroKit.Smooth(-0.9f, -2.8f, h)), uw);
        c.a = 1f;
        return c;
    }

    static void Bounds(IslandDef d, out float x0, out float x1, out float z0, out float z1)
    {
        float ax0 = float.MaxValue, az0 = float.MaxValue, ax1 = float.MinValue, az1 = float.MinValue;
        void Inc(float x, float z, float rx, float rz)
        {
            ax0 = Mathf.Min(ax0, x - rx); ax1 = Mathf.Max(ax1, x + rx);
            az0 = Mathf.Min(az0, z - rz); az1 = Mathf.Max(az1, z + rz);
        }
        foreach (var p in d.paths) for (int i = 0; i < p.pts.Count; i++) Inc(p.pts[i].x, p.pts[i].y, p.r[i] + p.extra, p.r[i] + p.extra);
        foreach (var b in d.land) Inc(b.x, b.z, Mathf.Max(b.rx, b.rz), Mathf.Max(b.rx, b.rz));
        if (d.hasGreen) Inc(d.green.x, d.green.z, d.green.rx * 1.9f, d.green.rz * 1.9f);
        if (d.hasTee) Inc(d.tee.x, d.tee.y + 2f, d.teeRx * 2.6f, d.teeRz * 2.8f);
        float m = 9f;
        x0 = ax0 - m; x1 = ax1 + m; z0 = az0 - m; z1 = az1 + m;
    }

    /// <summary>Builds the island mesh (with a real hole cut at the cup), its painted texture, collider and pond water.</summary>
    public static GameObject Build(IslandDef d, Transform parent, Material waterMat)
    {
        Bounds(d, out float x0, out float x1, out float z0, out float z1);
        int nx = Mathf.CeilToInt((x1 - x0) / d.cell), nz = Mathf.CeilToInt((z1 - z0) / d.cell);
        int C = nx + 1;
        var verts = new Vector3[(nx + 1) * (nz + 1)];
        var uvs = new Vector2[verts.Length];
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float u = (float)i / nx, v = (float)j / nz;
                float x = Mathf.Lerp(x0, x1, u), z = Mathf.Lerp(z0, z1, v);
                verts[j * C + i] = new Vector3(x, d.Height(x, z), z);
                uvs[j * C + i] = new Vector2(u, v);
            }

        var tris = new List<int>(nx * nz * 6);
        void Tri(int a, int b, int c)
        {
            Vector3 va = verts[a], vb = verts[b], vc = verts[c];
            // drop deep seabed triangles (hidden under the ocean, saves a lot of geometry)
            if (va.y <= AeroKit.SeabedY + 0.01f && vb.y <= AeroKit.SeabedY + 0.01f && vc.y <= AeroKit.SeabedY + 0.01f) return;
            if (d.hasCup)
            {
                // open a rough hole around the cup; a round turf collar (below) fills it back in up to the cup edge
                float ra = Vector2.Distance(new Vector2(va.x, va.z), d.cup), rb = Vector2.Distance(new Vector2(vb.x, vb.z), d.cup), rc = Vector2.Distance(new Vector2(vc.x, vc.z), d.cup);
                if (Mathf.Min(ra, Mathf.Min(rb, rc)) < CupGridCut) return;
            }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * C + i, b = a + 1, c = a + C, e = c + 1;
                Tri(a, c, b);
                Tri(b, c, e);
            }

        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.uv = uvs; mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        mesh = AeroKit.SaveMesh(mesh, d.name + "_Ground");

        // painted texture
        float tpu = d.texPerUnit;
        int tw = Mathf.Min(2048, Mathf.CeilToInt((x1 - x0) * tpu)), th = Mathf.Min(2048, Mathf.CeilToInt((z1 - z0) * tpu));
        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
        var px = new Color[tw * th];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
                px[y * tw + x] = Paint(d, Mathf.Lerp(x0, x1, (x + 0.5f) / tw), Mathf.Lerp(z0, z1, (y + 0.5f) / th));
        tex.SetPixels(px); tex.Apply(false);
        var saved = AeroKit.SaveTexture(tex, d.name + "_Paint", false, true, false, TextureWrapMode.Clamp);

        var mat = AeroKit.Lit(d.name + "_Ground", Color.white, 0.38f);
        mat.SetTexture("_BaseMap", saved);
        AeroKit.AddGrassDetail(mat, new Vector2((x1 - x0) / 2f, (z1 - z0) / 2f));

        var go = AeroKit.MeshObject(d.name + " Ground", parent, mesh, mat, true);

        if (d.hasCup)
        {
            // round turf collar: shares the island's paint (same UV space), follows the terrain, and its
            // inner edge is exactly the cup radius so the cup prefab drops straight into a clean hole
            float[] radii = { CupCollarOuter, 2.45f, 1.9f, 1.45f, AeroKit.CupCutRadius };
            int segs = 96, R = radii.Length;
            var cv = new Vector3[(segs + 1) * R];
            var cuv = new Vector2[cv.Length];
            for (int k = 0; k <= segs; k++)
            {
                float a = k / (float)segs * Mathf.PI * 2f;
                for (int i = 0; i < R; i++)
                {
                    float x = d.cup.x + Mathf.Cos(a) * radii[i], z = d.cup.y + Mathf.Sin(a) * radii[i];
                    const float lift = 0.006f;
                    cv[k * R + i] = new Vector3(x, d.Height(x, z) + lift, z);
                    cuv[k * R + i] = new Vector2(Mathf.InverseLerp(x0, x1, x), Mathf.InverseLerp(z0, z1, z));
                }
            }
            var ct = new List<int>();
            for (int k = 0; k < segs; k++)
                for (int i = 0; i < R - 1; i++)
                {
                    int a = k * R + i, b = k * R + i + 1, c = (k + 1) * R + i, e = (k + 1) * R + i + 1;
                    ct.Add(a); ct.Add(b); ct.Add(c);
                    ct.Add(c); ct.Add(b); ct.Add(e);
                }
            var cm = new Mesh();
            cm.vertices = cv; cm.uv = cuv; cm.SetTriangles(ct, 0);
            cm.RecalculateNormals(); cm.RecalculateTangents(); cm.RecalculateBounds();
            cm = AeroKit.SaveMesh(cm, d.name + "_CupCollar");
            AeroKit.MeshObject("Cup Turf Collar", go.transform, cm, mat, true);
        }

        // pond water surfaces (each at its own level, so ponds can sit on raised terraces)
        d.pondWater.Clear();
        foreach (var p in d.ponds)
        {
            float wy = d.LandHeight(p.x, p.z) - 0.28f;
            d.pondWater.Add(new Vector3(p.x, wy, p.z));
            var w = AeroKit.Prim(PrimitiveType.Cylinder, "Pond Water", go.transform, new Vector3(p.x, wy, p.z),
                new Vector3(p.rx * 2.05f, 0.005f, p.rz * 2.05f), waterMat, false, false, Quaternion.Euler(0f, -p.rot * Mathf.Rad2Deg, 0f));
            w.AddComponent<AeroUVScroll>().textureProperty = "_BumpMap";
            w.GetComponent<AeroUVScroll>().speed = new Vector2(0.02f, 0.015f);
        }
        return go;
    }
}
