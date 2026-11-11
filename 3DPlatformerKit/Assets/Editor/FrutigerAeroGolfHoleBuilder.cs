using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class FrutigerAeroGolfHoleBuilder
{
    private const string RootFolder = "Assets/Models/FrutigerAeroGolfHole";
    private const string MeshFolder = RootFolder + "/Meshes";
    private const string MaterialFolder = RootFolder + "/Materials";
    private const string PrefabPath = RootFolder + "/FrutigerAeroGolfHole.prefab";

    static FrutigerAeroGolfHoleBuilder()
    {
        EditorApplication.delayCall += CreatePrefabIfMissing;
    }

    [MenuItem("Tools/Golf is Hard/Rebuild Frutiger Aero Golf Hole")]
    public static void RebuildPrefab()
    {
        BuildPrefab(true);
    }

    private static void CreatePrefabIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            BuildPrefab(false);
    }

    private static void BuildPrefab(bool overwrite)
    {
        EnsureFolder("Assets/Models");
        EnsureFolder(RootFolder);
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);

        if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            return;

        Material turf = MakeMaterial("Turf_Mint", new Color(0.19f, 0.72f, 0.48f), 0.02f, 0.56f);
        Material porcelain = MakeMaterial("Rim_Pearl", new Color(0.86f, 0.97f, 0.98f), 0.10f, 0.92f);
        Material cyan = MakeMaterial("Cup_Aqua", new Color(0.13f, 0.66f, 0.77f), 0.12f, 0.91f);
        Material deepBlue = MakeMaterial("Cup_DeepBlue", new Color(0.025f, 0.10f, 0.20f), 0.18f, 0.80f);
        Material poleWhite = MakeMaterial("Pole_Pearl", new Color(0.91f, 0.98f, 1f), 0.22f, 0.86f);
        Material accentBlue = MakeMaterial("Accent_Aqua", new Color(0.10f, 0.74f, 0.94f), 0.16f, 0.93f);
        Material flagBlue = MakeMaterial("Flag_Aqua", new Color(0.08f, 0.66f, 0.89f), 0.04f, 0.72f);
        Material flagWhite = MakeMaterial("Flag_Pearl", new Color(0.90f, 0.98f, 1f), 0.06f, 0.86f);
        Material lime = MakeMaterial("Accent_Lime", new Color(0.69f, 0.94f, 0.36f), 0.03f, 0.70f);

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("Frutiger Aero Golf Hole");
            SceneManager.MoveGameObjectToScene(root, previewScene);
            root.AddComponent<GolfHoleAutoCutter>();

            Vector2[] collarProfile =
            {
                new Vector2(1.00f, -0.090f), new Vector2(1.055f, -0.075f),
                new Vector2(1.085f, -0.045f), new Vector2(1.095f, -0.020f),
                new Vector2(1.082f, -0.020f), new Vector2(1.055f, -0.020f),
                new Vector2(0.835f, -0.080f), new Vector2(0.805f, -0.110f),
                new Vector2(0.785f, -0.140f), new Vector2(0.785f, -0.190f),
                new Vector2(0.830f, -0.120f)
            };
            AddLathe(root.transform, "Soft Mint Turf Collar", "Collar_Rounded", collarProfile, 128, turf, true);

            Vector2[] rimProfile =
            {
                new Vector2(0.825f, -0.200f), new Vector2(0.826f, -0.110f),
                new Vector2(0.812f, -0.110f), new Vector2(0.786f, -0.140f),
                new Vector2(0.755f, -0.170f), new Vector2(0.575f, -0.240f),
                new Vector2(0.545f, -0.270f), new Vector2(0.527f, -0.300f),
                new Vector2(0.523f, -0.320f), new Vector2(0.535f, -0.350f),
                new Vector2(0.561f, -0.350f), new Vector2(0.785f, -0.140f)
            };
            AddLathe(root.transform, "Rounded Pearl Cup Rim", "Rim_Beveled", rimProfile, 128, porcelain, true);
            AddTorus(root.transform, "Aqua Rim Inlay", "Rim_Aqua_Inlay", 0.654f, 0.0065f, -0.315f, 128, accentBlue, false);
            AddTorus(root.transform, "Outer Mint Pinstripe", "Collar_Pinstripe", 1.045f, 0.008f, -0.020f, 128, accentBlue, false);

            Vector2[] linerProfile =
            {
                new Vector2(0.526f, -0.320f), new Vector2(0.513f, -0.350f),
                new Vector2(0.492f, -0.410f), new Vector2(0.465f, -0.500f),
                new Vector2(0.437f, -0.590f), new Vector2(0.412f, -0.650f),
                new Vector2(0.391f, -0.680f), new Vector2(0.373f, -0.660f),
                new Vector2(0.397f, -0.620f), new Vector2(0.421f, -0.530f),
                new Vector2(0.449f, -0.440f), new Vector2(0.478f, -0.370f),
                new Vector2(0.501f, -0.330f)
            };
            AddLathe(root.transform, "Smooth Aqua Cup Liner", "Cup_Liner_Smooth", linerProfile, 112, cyan, true);
            AddDisk(root.transform, "Deep Lagoon Cup Bottom", "Cup_Bottom", 0.391f, -0.675f, 96, deepBlue, true);
            AddTorus(root.transform, "Cup Bottom Glint", "Cup_Bottom_Glint", 0.262f, 0.008f, -0.668f, 96, accentBlue, false);
            AddTorus(root.transform, "Cup Socket", "Pole_Socket", 0.070f, 0.014f, -0.550f, 64, porcelain, false);

            Mesh sphere = SaveMesh("Smooth_Sphere", CreateSphere("Smooth_Sphere", 32, 20));
            AddCylinder(root.transform, "Pearl Flagpole", "Pole_Main", 0.028f, -0.58f, 2.76f, 48, poleWhite, false);

            AddTorus(root.transform, "Pole Band Aqua 1", "Pole_Band_1", 0.030f, 0.007f, 0.48f, 48, accentBlue, false);
            AddTorus(root.transform, "Pole Band Aqua 2", "Pole_Band_2", 0.030f, 0.007f, 1.02f, 48, accentBlue, false);
            AddTorus(root.transform, "Pole Band Aqua 3", "Pole_Band_3", 0.030f, 0.007f, 1.56f, 48, accentBlue, false);
            AddTorus(root.transform, "Pole Band Aqua 4", "Pole_Band_4", 0.030f, 0.007f, 2.10f, 48, accentBlue, false);
            AddTorus(root.transform, "Flag Mount Collar", "Flag_Mount", 0.040f, 0.012f, 2.12f, 48, porcelain, false);
            AddSphere(root.transform, "Polished Pole Cap", sphere, poleWhite,
                new Vector3(0f, 2.625f, 0f), new Vector3(0.085f, 0.085f, 0.085f));
            AddSphere(root.transform, "Pole Base Jewel", sphere, accentBlue,
                new Vector3(0f, 0.085f, 0f), new Vector3(0.105f, 0.060f, 0.105f));

            AddFlag(root.transform, "Soft Wave Aqua Flag", "Flag_Waved", flagBlue, flagWhite);
            AddSphere(root.transform, "Flag Sun Emblem Pearl", sphere, flagWhite,
                new Vector3(0.405f, 2.345f, 0.20f), new Vector3(0.105f, 0.105f, 0.030f));
            AddSphere(root.transform, "Flag Sun Emblem Lime", sphere, lime,
                new Vector3(0.405f, 2.345f, 0.225f), new Vector3(0.057f, 0.057f, 0.018f));

            for (int i = 0; i < 8; i++)
            {
                float angle = Mathf.PI * 2f * i / 8f;
                Vector3 position = new Vector3(Mathf.Cos(angle) * 0.91f, -0.042f, Mathf.Sin(angle) * 0.91f);
                AddSphere(root.transform, "Pearl Collar Stud " + (i + 1).ToString("00"), sphere, porcelain,
                    position, new Vector3(0.035f, 0.018f, 0.035f));
            }

            GameObject triggerObject = new GameObject("Hole Trigger");
            triggerObject.transform.SetParent(root.transform, false);
            SphereCollider trigger = triggerObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.20f;
            trigger.center = new Vector3(0f, -0.50f, 0f);
            triggerObject.AddComponent<HoleTrigger>();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        catch (System.SystemException exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (root != null)
                Object.DestroyImmediate(root);
            if (previewScene.IsValid() && previewScene.isLoaded)
                EditorSceneManager.ClosePreviewScene(previewScene);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created the smooth Frutiger Aero golf-hole prefab at " + PrefabPath + ".");
    }

    private static void AddLathe(Transform parent, string objectName, string meshName,
        Vector2[] profile, int segments, Material material, bool addCollider)
    {
        Mesh mesh = SaveMesh(meshName, CreateLatheMesh(meshName, profile, segments));
        AddMeshObject(parent, objectName, mesh, material, addCollider);
    }

    private static void AddTorus(Transform parent, string objectName, string meshName,
        float majorRadius, float minorRadius, float height, int segments, Material material, bool addCollider)
    {
        const int profileSteps = 20;
        Vector2[] profile = new Vector2[profileSteps];
        for (int i = 0; i < profileSteps; i++)
        {
            float angle = Mathf.PI * 2f * i / profileSteps;
            profile[i] = new Vector2(majorRadius + Mathf.Cos(angle) * minorRadius,
                height + Mathf.Sin(angle) * minorRadius);
        }
        AddLathe(parent, objectName, meshName, profile, segments, material, addCollider);
    }

    private static void AddDisk(Transform parent, string objectName, string meshName,
        float radius, float height, int segments, Material material, bool addCollider)
    {
        Vector2[] profile =
        {
            new Vector2(radius, height - 0.045f), new Vector2(radius, height - 0.008f),
            new Vector2(radius * 0.96f, height + 0.002f), new Vector2(radius * 0.90f, height),
            new Vector2(radius * 0.78f, height), new Vector2(0f, height),
            new Vector2(0f, height - 0.045f), new Vector2(radius * 0.72f, height - 0.045f)
        };
        AddLathe(parent, objectName, meshName, profile, segments, material, addCollider);
    }

    private static GameObject AddCylinder(Transform parent, string objectName, string meshName,
        float radius, float bottom, float top, int segments, Material material, bool addCollider)
    {
        Vector2[] profile =
        {
            new Vector2(0f, bottom), new Vector2(radius * 0.82f, bottom), new Vector2(radius, bottom + 0.012f),
            new Vector2(radius, top - 0.012f), new Vector2(radius * 0.82f, top), new Vector2(0f, top)
        };
        Mesh mesh = SaveMesh(meshName, CreateLatheMesh(meshName, profile, segments));
        return AddMeshObject(parent, objectName, mesh, material, addCollider);
    }

    private static GameObject AddMeshObject(Transform parent, string objectName, Mesh mesh,
        Material material, bool addCollider)
    {
        GameObject child = new GameObject(objectName);
        child.transform.SetParent(parent, false);
        MeshFilter filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        if (addCollider)
        {
            MeshCollider collider = child.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }
        return child;
    }

    private static void AddSphere(Transform parent, string objectName, Mesh sphere, Material material,
        Vector3 localPosition, Vector3 localScale)
    {
        GameObject child = AddMeshObject(parent, objectName, sphere, material, false);
        child.transform.localPosition = localPosition;
        child.transform.localScale = localScale;
    }

    private static void AddFlag(Transform parent, string objectName, string meshName,
        Material aquaMaterial, Material whiteMaterial)
    {
        const int columns = 28;
        const int rows = 14;
        int frontCount = (columns + 1) * (rows + 1);
        Vector3[] vertices = new Vector3[frontCount * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        for (int row = 0; row <= rows; row++)
        {
            float v = row / (float)rows;
            for (int column = 0; column <= columns; column++)
            {
                float u = column / (float)columns;
                float wave = Mathf.Sin(u * Mathf.PI * 1.15f) * u;
                float flutter = Mathf.Sin(u * Mathf.PI * 2.2f + v * Mathf.PI * 1.1f) * u;
                float x = 0.035f + u * 0.82f;
                float y = 2.095f + v * 0.47f + wave * 0.030f * v;
                float z = 0.040f + wave * 0.095f + flutter * 0.025f;
                int index = row * (columns + 1) + column;
                vertices[index] = new Vector3(x, y, z);
                vertices[index + frontCount] = new Vector3(x, y, z - 0.024f);
                uvs[index] = new Vector2(u, v);
                uvs[index + frontCount] = new Vector2(u, v);
            }
        }

        List<int> aquaTriangles = new List<int>();
        List<int> whiteTriangles = new List<int>();
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int a = row * (columns + 1) + column;
            int b = a + 1;
            int c = a + columns + 1;
            int d = c + 1;
            float midU = (column + 0.5f) / columns;
            List<int> target = midU < 0.14f || (midU > 0.82f && midU < 0.89f)
                ? whiteTriangles : aquaTriangles;
            target.Add(a); target.Add(b); target.Add(d);
            target.Add(a); target.Add(d); target.Add(c);

            int backA = a + frontCount;
            int backB = b + frontCount;
            int backC = c + frontCount;
            int backD = d + frontCount;
            target.Add(backA); target.Add(backD); target.Add(backB);
            target.Add(backA); target.Add(backC); target.Add(backD);
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(aquaTriangles, 0);
        mesh.SetTriangles(whiteTriangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh = SaveMesh(meshName, mesh);

        GameObject flag = new GameObject(objectName);
        flag.transform.SetParent(parent, false);
        MeshFilter filter = flag.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = flag.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { aquaMaterial, whiteMaterial };
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        flag.AddComponent<GolfHoleFlagWave>();
    }

    private static Mesh CreateLatheMesh(string meshName, Vector2[] profile, int segments)
    {
        Vector3[] vertices = new Vector3[profile.Length * segments];
        Vector3[] normals = new Vector3[vertices.Length];
        int[] triangles = new int[profile.Length * segments * 6];
        for (int p = 0; p < profile.Length; p++)
        {
            Vector2 previous = profile[(p + profile.Length - 1) % profile.Length];
            Vector2 next = profile[(p + 1) % profile.Length];
            Vector2 tangent = (next - previous).normalized;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float cosine = Mathf.Cos(angle);
                float sine = Mathf.Sin(angle);
                int index = p * segments + i;
                vertices[index] = new Vector3(profile[p].x * cosine, profile[p].y, profile[p].x * sine);
                normals[index] = new Vector3(tangent.y * cosine, -tangent.x, tangent.y * sine).normalized;
            }
        }

        int triangleIndex = 0;
        for (int p = 0; p < profile.Length; p++)
        for (int i = 0; i < segments; i++)
        {
            int nextSegment = (i + 1) % segments;
            int nextProfile = (p + 1) % profile.Length;
            int a = p * segments + i;
            int b = p * segments + nextSegment;
            int c = nextProfile * segments + i;
            int d = nextProfile * segments + nextSegment;
            triangles[triangleIndex++] = a; triangles[triangleIndex++] = c; triangles[triangleIndex++] = d;
            triangles[triangleIndex++] = a; triangles[triangleIndex++] = d; triangles[triangleIndex++] = b;
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateSphere(string meshName, int longitudeSegments, int latitudeSegments)
    {
        int columns = longitudeSegments + 1;
        Vector3[] vertices = new Vector3[(latitudeSegments + 1) * columns];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[latitudeSegments * longitudeSegments * 6];
        for (int row = 0; row <= latitudeSegments; row++)
        {
            float v = row / (float)latitudeSegments;
            float latitude = Mathf.PI * v;
            for (int column = 0; column <= longitudeSegments; column++)
            {
                float u = column / (float)longitudeSegments;
                float longitude = Mathf.PI * 2f * u;
                Vector3 normal = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude),
                    Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                int index = row * columns + column;
                vertices[index] = normal;
                normals[index] = normal.normalized;
                uvs[index] = new Vector2(u, v);
            }
        }

        int triangleIndex = 0;
        for (int row = 0; row < latitudeSegments; row++)
        for (int column = 0; column < longitudeSegments; column++)
        {
            int a = row * columns + column;
            int b = a + 1;
            int c = a + columns;
            int d = c + 1;
            triangles[triangleIndex++] = a; triangles[triangleIndex++] = d; triangles[triangleIndex++] = c;
            triangles[triangleIndex++] = a; triangles[triangleIndex++] = b; triangles[triangleIndex++] = d;
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh SaveMesh(string meshName, Mesh mesh)
    {
        string path = MeshFolder + "/" + meshName + ".asset";
        Mesh oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (oldMesh != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }

    private static Material MakeMaterial(string materialName, Color color, float metallic, float smoothness)
    {
        string path = MaterialFolder + "/" + materialName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader) { name = materialName };
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}


