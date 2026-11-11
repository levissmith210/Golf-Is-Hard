using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ClassicWhiteGolfTeeBuilder
{
    private const string RootFolder = "Assets/Models/ClassicWhiteGolfTee";
    private const string MeshFolder = RootFolder + "/Meshes";
    private const string MaterialFolder = RootFolder + "/Materials";
    private const string PrefabPath = RootFolder + "/ClassicWhiteGolfTee.prefab";

    static ClassicWhiteGolfTeeBuilder()
    {
        EditorApplication.delayCall += CreatePrefabIfMissing;
    }

    [MenuItem("Tools/Golf is Hard/Create Classic White Golf Tee")]
    public static void CreatePrefab()
    {
        BuildPrefab(true);
    }

    private static void CreatePrefabIfMissing()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            BuildPrefab(false);
        }
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

        Material whitePlastic = MakeMaterial("Tee_Classic_White", new Color(0.95f, 0.95f, 0.92f), 0f, 0.38f);
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("Classic White Golf Tee");
            SceneManager.MoveGameObjectToScene(root, previewScene);

            Vector2[] teeProfile =
            {
                new Vector2(0f, -0.040f), new Vector2(0.003f, -0.030f),
                new Vector2(0.008f, -0.010f), new Vector2(0.016f, 0.055f),
                new Vector2(0.029f, 0.145f), new Vector2(0.047f, 0.235f),
                new Vector2(0.069f, 0.282f), new Vector2(0.091f, 0.302f),
                new Vector2(0.098f, 0.313f), new Vector2(0.093f, 0.322f),
                new Vector2(0.080f, 0.323f), new Vector2(0.067f, 0.314f),
                new Vector2(0.055f, 0.301f), new Vector2(0.039f, 0.296f),
                new Vector2(0f, 0.296f)
            };
            Mesh teeMesh = SaveMesh("Classic_White_Tee_Smooth", CreateLatheMesh(teeProfile, 96));
            GameObject body = new GameObject("Smooth Molded Tee");
            body.transform.SetParent(root.transform, false);
            MeshFilter filter = body.AddComponent<MeshFilter>();
            filter.sharedMesh = teeMesh;
            MeshRenderer renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = whitePlastic;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            BoxCollider contact = root.AddComponent<BoxCollider>();
            contact.center = new Vector3(0f, 0.31f, 0f);
            contact.size = new Vector3(0.16f, 0.020f, 0.16f);

            GameObject spawnObject = new GameObject("Ball Spawn Point");
            spawnObject.transform.SetParent(root.transform, false);
            GolfTeeSpawnPoint spawn = root.AddComponent<GolfTeeSpawnPoint>();
            spawn.Configure(spawnObject.transform, 0.5f, 0.32f);

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
        Debug.Log("Created the classic smooth white golf tee prefab at " + PrefabPath + ".");
    }

    private static Mesh CreateLatheMesh(Vector2[] profile, int segments)
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

        Mesh mesh = new Mesh { name = "Classic White Golf Tee" };
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
