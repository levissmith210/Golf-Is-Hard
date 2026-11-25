#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Temporary asset-only render check. Never enters Play mode or saves a scene.</summary>
[InitializeOnLoad]
public static class ExportGolfVictoryTrophiesOnce
{
    private const string Key = "GolfIsHard.ExportVictoryTrophies.20261124.v5";
    static ExportGolfVictoryTrophiesOnce() { EditorApplication.update += ExportWhenReady; }

    private static void ExportWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        EditorApplication.update -= ExportWhenReady;
        if (EditorPrefs.GetBool(Key, false)) return;

        CleanupUnownedPreviewRigs();
        int previewRootsBefore = CountPreviewRoots();
        var owner = new GameObject("Temporary Trophy Asset Export") { hideFlags = HideFlags.HideAndDontSave };
        var trophy = owner.AddComponent<GolfVictoryTrophy>();
        RenderTexture previous = RenderTexture.active;
        try
        {
            string[] tiers = { "Bronze", "Silver", "Gold", "Diamond" };
            for (int i = 0; i < tiers.Length; i++)
            {
                trophy.SetAward(i + 1);
                trophy.Tick(1.55f, 0f);
                var texture = trophy.PreviewTexture as RenderTexture;
                if (texture == null || !texture.IsCreated())
                    throw new InvalidOperationException("The trophy preview did not create its render texture.");
                RenderTexture.active = texture;
                var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    pixels.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
                    pixels.Apply();
                    string path = Path.Combine(Path.GetTempPath(), "GolfVictory_" + tiers[i] + ".png");
                    File.WriteAllBytes(path, pixels.EncodeToPNG());
                    Debug.Log("VICTORY_TROPHY_PREVIEW: " + path);
                }
                finally { UnityEngine.Object.DestroyImmediate(pixels); }
                RenderTexture.active = previous;
                trophy.DisposePreview();
            }
            EditorPrefs.SetBool(Key, true);
        }
        catch (Exception exception) { Debug.LogException(exception); }
        finally
        {
            RenderTexture.active = previous;
            trophy.DisposePreview();
            UnityEngine.Object.DestroyImmediate(owner);
            int remaining = CountPreviewRoots();
            Debug.Log("VICTORY_TROPHY_CLEANUP: export-owned root delta " + (remaining - previewRootsBefore) + "; " + remaining + " pre-existing preview roots left untouched. No scene was saved or Play mode entered.");
        }
    }
    private static void CleanupUnownedPreviewRigs()
    {
        var referenced = new System.Collections.Generic.HashSet<GameObject>();
        var rootField = typeof(GolfVictoryTrophy).GetField("previewRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (GolfVictoryTrophy owner in Resources.FindObjectsOfTypeAll<GolfVictoryTrophy>())
        {
            GameObject root = rootField != null ? rootField.GetValue(owner) as GameObject : null;
            if (root != null) referenced.Add(root);
        }
        var abandoned = new System.Collections.Generic.List<GameObject>();
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (obj.name == "Golf Trophy Preview (Temporary)" &&
                (obj.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave && !referenced.Contains(obj))
                abandoned.Add(obj);
        }
        int assetsReleased = 0;
        foreach (GameObject root in abandoned)
        {
            var assets = new System.Collections.Generic.HashSet<UnityEngine.Object>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh != null) assets.Add(filter.sharedMesh);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials) if (material != null) assets.Add(material);
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true)) if (camera.targetTexture != null) assets.Add(camera.targetTexture);
            foreach (ReflectionProbe probe in root.GetComponentsInChildren<ReflectionProbe>(true)) if (probe.customBakedTexture != null) assets.Add(probe.customBakedTexture);
            root.SetActive(false);
            UnityEngine.Object.DestroyImmediate(root);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset == null || AssetDatabase.Contains(asset) ||
                    (asset.hideFlags & HideFlags.HideAndDontSave) != HideFlags.HideAndDontSave) continue;
                if (asset is RenderTexture target)
                {
                    if (RenderTexture.active == target) RenderTexture.active = null;
                    target.Release();
                }
                UnityEngine.Object.DestroyImmediate(asset);
                assetsReleased++;
            }
        }
        Debug.Log("VICTORY_TROPHY_ORPHAN_CLEANUP: " + abandoned.Count + " unreferenced, exact-name hidden trophy rigs and " + assetsReleased + " owned temporary assets released. Live preview owners were preserved.");
    }
    private static int CountPreviewRoots()
    {
        int count = 0;
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
            if (obj.name == "Golf Trophy Preview (Temporary)" || obj.name == "Temporary Trophy Asset Export") count++;
        return count;
    }
}
#endif





