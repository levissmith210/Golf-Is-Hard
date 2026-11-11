using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class FrutigerAeroGolfTeeBuilder
{
    private const string RootFolder = "Assets/Models/FrutigerAeroGolfTee";
    private const string MeshFolder = RootFolder + "/Meshes";
    private const string MaterialFolder = RootFolder + "/Materials";
    private const string PrefabPath = RootFolder + "/FrutigerAeroGolfTee.prefab";

    static FrutigerAeroGolfTeeBuilder()
    {
        EditorApplication.delayCall += CreatePrefabIfMissing;
    }

    [MenuItem("Tools/Golf is Hard/Rebuild Frutiger Aero Golf Tee")]
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

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            AssetDatabase.DeleteAsset(PrefabPath);

        Material pearl = MakeMaterial("Tee_Pearl", new Color(0.83f, 0.97f, 1f), 0.12f, 0.90f);
        Material aqua = MakeMaterial("Tee_Aqua", new Color(0.08f, 0.55f, 0.78f), 0.20f, 0.94f);
        Material mint = MakeMaterial("Tee_Mint", new Color(0.20f, 0.78f, 0.66f), 0.12f, 0.90f);
        Material lime = MakeMaterial("Tee_Lime_Accent", new Color(0.68f, 0.93f, 0.40f), 0.06f, 0.86f);

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("Frutiger Aero Golf Tee");
            SceneManager.MoveGameObjectToScene(root, previewScene);

            Vector2[] baseProfile =
            {
                new Vector2(0f, 0f), new Vector2(0.16f, 0f),
                new Vector2(0.205f, 0.008f), new Vector2(0.232f, 0.026f),
                new Vector2(0.238f, 0.047f), new Vector2(0.226f, 0.064f),
                new Vector2(0.196f, 0.075f), new Vector2(0f, 0.075f)
            };
            AddLathe(root.transform, "Rounded Pearl Base", "Tee_Base_Smooth", baseProfile, 80, pearl);
            AddTorus(root.transform, "Mint Base Inlay", "Tee_Base_Inlay", 0.207f, 0.006f, 0.071f, 80, mint);

            Vector2[] stemProfile =
            {
                new Vector2(0f, 0.061f), new Vector2(0.049f, 0.061f),
                new Vector2(0.060f, 0.074f), new Vector2(0.049f, 0.094f),
                new Vector2(0.036f, 0.195f), new Vector2(0.031f, 0.235f),
                new Vector2(0.042f, 0.253f), new Vector2(0f, 0.253f)
            };
            AddLathe(root.transform, "Tapered Aqua Stem", "Tee_Stem_Smooth", stemProfile, 72, aqua);

            Vector2[] crownProfile =
            {
                new Vector2(0f, 0.237f), new Vector2(0.035f, 0.237f),
                new Vector2(0.065f, 0.248f), new Vector2(0.093f, 0.269f),
                new Vector2(0.122f, 0.286f), new Vector2(0.143f, 0.299f),
                new Vector2(0.146f, 0.307f), new Vector2(0.137f, 0.315f),
                new Vector2(0.118f, 0.317f), new Vector2(0.101f, 0.311f),
                new Vector2(0.083f, 0.305f), new Vector2(0f, 0.305f)
            };
            AddLathe(root.transform, "Sculpted Mint Ball Crown", "Tee_Crown_Smooth", crownProfile, 80, mint);
            AddTorus(root.transform, "Pearl Crown Highlight", "Tee_Crown_Highlight", 0.112f, 0.0045f, 0.313f, 80, pearl);

            BoxCollider contact = root.AddComponent<BoxCollider>();
            contact.center = new Vector3(0f, 0.295f, 0f);
            contact.size = new Vector3(0.18f, 0.020f, 0.18f);

            GameObject accent = new GameObject("Lime Alignment Dot");
            accent.transform.SetParent(root.transform, false);
            accent.transform.localPosition = new Vector3(0f, 0.078f, 0f);
            Mesh accentMesh = SaveMesh("Tee_Accent_Dot", CreateSphere("Tee_Accent_Dot", 24, 16));
            MeshFilter accentFilter = accent.AddComponent<MeshFilter>();
            accentFilter.sharedMesh = accentMesh;
            MeshRenderer accentRenderer = accent.AddComponent<MeshRenderer>();
            accentRenderer.sharedMaterial = lime;
            accentRenderer.shadowCastingMode = ShadowCastingMode.Off;
            accentRenderer.receiveShadows = true;
            accent.transform.localScale = new Vector3(0.045f, 0.012f, 0.045f);

            GameObject spawnObject = new GameObject("Ball Spawn Point");
            spawnObject.transform.SetParent(root.transform, false);
            GolfTeeSpawnPoint spawn = root.AddComponent<GolfTeeSpawnPoint>();
            spawn.Configure(spawnObject.transform, 0.5f, 0.305f);

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
        Debug.Log("Created the smooth Frutiger Aero golf tee prefab at " + PrefabPath + ".");
    }

    private static void AddLathe(Transform parent, string objectName, string meshName,
        Vector2[] profile, int segments, Material material)
    {
        Mesh mesh = SaveMesh(meshName, CreateLatheMesh(meshName, profile, segments));
        GameObject child = new GameObject(objectName);
        child.transform.SetParent(parent, false);
        MeshFilter filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static void AddTorus(Transform parent, string objectName, string meshName,
        float majorRadius, float minorRadius, float height, int segments, Material material)
    {
        const int profileSteps = 20;
        Vector2[] profile = new Vector2[profileSteps];
        for (int i = 0; i < profileSteps; i++)
        {
            float angle = Mathf.PI * 2f * i / profileSteps;
            profile[i] = new Vector2(majorRadius + Mathf.Cos(angle) * minorRadius,
                height + Mathf.Sin(angle) * minorRadius);
        }

        AddLathe(parent, objectName, meshName, profile, segments, material);
    }

    private static Mesh CreateLatheMesh(string meshName, Vector2[] profile, int segments)
    {
        Vector3[] vertices = new Vector3[profile.Length * segments];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
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
                uvs[index] = new Vector2(i / (float)(segments - 1), p / (float)(profile.Length - 1));
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
            triangles[triangleIndex++] = a;
            triangles[triangleIndex++] = c;
            triangles[triangleIndex++] = d;
            triangles[triangleIndex++] = a;
            triangles[triangleIndex++] = d;
            triangles[triangleIndex++] = b;
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
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
            triangles[triangleIndex++] = a;
            triangles[triangleIndex++] = d;
            triangles[triangleIndex++] = c;
            triangles[triangleIndex++] = a;
            triangles[triangleIndex++] = b;
            triangles[triangleIndex++] = d;
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
            if (shader == null)
                shader = Shader.Find("Standard");
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
