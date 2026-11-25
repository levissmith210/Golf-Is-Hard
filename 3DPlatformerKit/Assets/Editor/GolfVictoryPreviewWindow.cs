using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A safe editor-only visual preview; no Play mode, records, or saved scene changes.</summary>
public sealed class GolfVictoryPreviewWindow : EditorWindow
{
    private Scene previewScene;
    private GameObject previewRoot;
    private GolfVictoryScreen screen;
    private int selectedTier = 2;
    private bool showDetails;
    private double startedAt, lastTick;
    private static readonly int[] Par = { 3, 4, 4, 3, 4, 4, 4, 5, 5 };
    private static readonly string[] Names = { "Lagoon Lane", "Glass Bridge", "Waterfall Terraces", "Seashell Spire", "Pinball Garden", "Cloud Hop", "Halfpipe Canyon", "Aqua Dome", "Rainbow Finale" };

    [MenuItem("Tools/Golf is Hard/Preview Trophy Celebration")]
    public static void Open()
    {
        var window = GetWindow<GolfVictoryPreviewWindow>(true, "Golf is Hard - Trophy Preview", true);
        window.minSize = new Vector2(640f, 410f);
        window.position = new Rect(90f, 80f, 1280f, 760f);
        window.Show(); window.Focus();
    }
    private void OnEnable()
    {
        previewScene = EditorSceneManager.NewPreviewScene();
        previewRoot = new GameObject("Trophy Presentation Preview") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(previewRoot, previewScene);
        screen = previewRoot.AddComponent<GolfVictoryScreen>();
        ApplyAward();
        EditorApplication.update += Animate;
    }
    private void ApplyAward()
    {
        if (screen == null) return;
        int[] strokes = (int[])Par.Clone();
        if (selectedTier == 0) { for (int i = 0; i < strokes.Length; i++) strokes[i]++; strokes[8] += 4; }
        else if (selectedTier == 1) { for (int i = 0; i < 6; i++) strokes[i]++; }
        else if (selectedTier == 3) { for (int i = 0; i < 6; i++) strokes[i]--; }
        screen.SetPreviewScores(strokes, Par, Names, 23, 27, true);
        startedAt = lastTick = EditorApplication.timeSinceStartup;
    }
    private void Animate()
    {
        if (screen == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        double now = EditorApplication.timeSinceStartup;
        if (now - lastTick < 1.0 / 30.0) return;
        screen.TickPreview((float)(now - startedAt), (float)(now - lastTick));
        lastTick = now; Repaint();
    }
    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int next = GUILayout.Toolbar(selectedTier, new[] { "Bronze", "Silver", "Gold", "Diamond" }, EditorStyles.toolbarButton, GUILayout.Width(340f));
            if (next != selectedTier) { selectedTier = next; ApplyAward(); }
            GUILayout.Space(12f);
            if (GUILayout.Button("Replay reveal", EditorStyles.toolbarButton, GUILayout.Width(110f))) startedAt = EditorApplication.timeSinceStartup;
            showDetails = GUILayout.Toggle(showDetails, "Scorecard", EditorStyles.toolbarButton, GUILayout.Width(90f));
            GUILayout.FlexibleSpace();
            GUILayout.Label("EDITOR PREVIEW - NO RECORDS ARE SAVED", EditorStyles.miniLabel);
        }
        if (screen == null) return;
        screen.SetPreviewDetails(showDetails);
        screen.DrawPresentation(new Rect(0f, 24f, position.width, position.height - 24f), (float)(EditorApplication.timeSinceStartup - startedAt), false);
    }
    private void OnDisable()
    {
        EditorApplication.update -= Animate;
        if (screen != null) screen.Hide();
        if (previewRoot != null) DestroyImmediate(previewRoot);
        if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
        previewRoot = null; screen = null;
    }
}

[InitializeOnLoad]
public static class GolfVictoryInstallOnce
{
    private const string Key = "GolfIsHard.VictoryScreen.Install.20261124.v1";
    static GolfVictoryInstallOnce() { EditorApplication.update += Install; }
    private static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= Install;
        if (EditorPrefs.GetBool(Key)) return;
        try
        {
            const string prefabPath = "Assets/FrutigerGolfCourse/AeroGameplayRig.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var manager = root.GetComponentInChildren<GolfCourseManager>(true);
                if (manager == null) throw new InvalidOperationException("Gameplay rig has no course manager.");
                if (manager.GetComponent<GolfVictoryScreen>() == null) manager.gameObject.AddComponent<GolfVictoryScreen>();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (GolfVictoryScreen.EvaluateTrophy(49, 36, 9) != 1 || GolfVictoryScreen.EvaluateTrophy(45, 36, 9) != 2 ||
                GolfVictoryScreen.EvaluateTrophy(36, 36, 9) != 3 || GolfVictoryScreen.EvaluateTrophy(30, 36, 9) != 4 ||
                GolfVictoryScreen.EvaluateTrophy(31, 36, 9) != 3)
                throw new InvalidOperationException("Round trophy boundary checks failed.");
            EditorPrefs.SetBool(Key, true);
            Debug.Log("VICTORY_SETUP_PASS: reusable rig includes trophy screen/audio; bronze/silver/gold/diamond total-score boundary checks passed. Open scene was not saved.");
            GolfVictoryPreviewWindow.Open();
        }
        catch (Exception error) { Debug.LogException(error); }
    }
}


// TEMPORARY_AUDIO_AUDIT
[InitializeOnLoad]
public static class ExportGolfVictoryAudioOnce
{
    private const string Key = "GolfIsHard.ExportVictoryAudio.20261124.v1";
    static ExportGolfVictoryAudioOnce() { EditorApplication.update += ExportWhenReady; }

    private static void ExportWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        EditorApplication.update -= ExportWhenReady;
        if (EditorPrefs.GetBool(Key, false)) return;

        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject owner = null;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            owner = new GameObject("Temporary Victory Audio Export") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(owner, preview);
            GolfVictoryAudio audio = owner.AddComponent<GolfVictoryAudio>();
            Export(Generate(audio, "CreateVictoryMusic"), "GolfVictoryMusic.wav", true);
            Export(Generate(audio, "CreateFanfare", 3), "GolfVictoryFanfare.wav", false);
            Export(Generate(audio, "CreateReveal", 3), "GolfVictoryTrophy.wav", false);
            timer.Stop();
            EditorPrefs.SetBool(Key, true);
            Debug.Log("VICTORY_AUDIO_EXPORT_COMPLETE: pure default generation and WAV export took " + timer.ElapsedMilliseconds + " ms. No playback or scene save.");
        }
        catch (Exception error) { Debug.LogException(error); }
        finally
        {
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static AudioClip Generate(GolfVictoryAudio audio, string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(GolfVictoryAudio).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new MissingMethodException(typeof(GolfVictoryAudio).Name, methodName);
        AudioClip clip = method.Invoke(audio, arguments) as AudioClip;
        if (clip == null) throw new InvalidOperationException(methodName + " did not return audio.");
        return clip;
    }

    private static void Export(AudioClip clip, string filename, bool loop)
    {
        float[] samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0)) throw new InvalidOperationException("Could not read " + clip.name);
        float peak = 0f;
        double energy = 0;
        foreach (float sample in samples)
        {
            if (float.IsNaN(sample) || float.IsInfinity(sample)) throw new InvalidOperationException("Non-finite sample in " + clip.name);
            peak = Mathf.Max(peak, Mathf.Abs(sample));
            energy += sample * sample;
        }
        if (peak >= 0.9f || peak < 0.001f) throw new InvalidOperationException("Unexpected peak " + peak + " in " + clip.name);
        float boundary = 0f;
        for (int channel = 0; channel < clip.channels; channel++)
            boundary = Mathf.Max(boundary, Mathf.Abs(samples[channel] - samples[(clip.samples - 1) * clip.channels + channel]));
        if (loop && boundary > 0.08f) throw new InvalidOperationException("Music loop has an abrupt boundary: " + boundary);

        string path = Path.Combine(Path.GetTempPath(), filename);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            int bytes = samples.Length * sizeof(short);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((ushort)1); writer.Write((ushort)clip.channels);
            writer.Write(clip.frequency); writer.Write(clip.frequency * clip.channels * sizeof(short));
            writer.Write((ushort)(clip.channels * sizeof(short))); writer.Write((ushort)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            foreach (float sample in samples) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
        }
        Debug.Log($"VICTORY_AUDIO_EXPORT_PASS: {path} | {clip.length:0.00}s, {clip.channels}ch, {clip.frequency}Hz, peak {peak:0.000}, RMS {Math.Sqrt(energy / samples.Length):0.000}, boundary delta {boundary:0.00000}");
    }
}


[InitializeOnLoad]
public static class GolfVictoryAudioAssetsOnce
{
    private const string Key = "GolfIsHard.VictoryAudioAssets.20261124.v1";
    static GolfVictoryAudioAssetsOnce() { EditorApplication.update += Install; }
    private static void Install()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= Install;
        if (EditorPrefs.GetBool(Key)) return;
        string[] paths = { "Assets/Sounds/Victory/AeroVictoryMusic.wav", "Assets/Sounds/Victory/CourseFanfare.wav", "Assets/Sounds/Victory/GoldTrophyReveal.wav" };
        foreach (string path in paths) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/FrutigerGolfCourse/AeroGameplayRig.prefab");
        try
        {
            var manager = root.GetComponentInChildren<GolfCourseManager>(true);
            var audio = manager.GetComponent<GolfVictoryAudio>();
            var settings = new SerializedObject(audio);
            string[] fields = { "victoryMusic", "courseFanfare", "goldReveal" };
            for (int i = 0; i < fields.Length; i++)
            {
                var field = settings.FindProperty(fields[i]);
                if (field.objectReferenceValue == null)
                    field.objectReferenceValue = i == 1 && manager.CompletionSound != null ? manager.CompletionSound : AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/FrutigerGolfCourse/AeroGameplayRig.prefab");
            EditorPrefs.SetBool(Key, true);
            Debug.Log("VICTORY_AUDIO_ASSETS_PASS: original victory music, fanfare, and gold reveal imported and assigned to reusable gameplay rig. Existing overrides preserved.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
