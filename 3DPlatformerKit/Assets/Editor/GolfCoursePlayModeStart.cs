#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the main golf course the entry point when testing with Unity's Play button.
/// Player builds already start from the first enabled scene in Build Settings.
/// </summary>
[InitializeOnLoad]
public static class GolfCoursePlayModeStart
{
    private const string CourseScenePath = "Assets/Scenes/NineHoleCourse.unity";

    static GolfCoursePlayModeStart()
    {
        EditorApplication.delayCall += SetCourseSceneAsPlayModeStart;
    }

    private static void SetCourseSceneAsPlayModeStart()
    {
        SceneAsset courseScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(CourseScenePath);
        if (courseScene != null && EditorSceneManager.playModeStartScene != courseScene)
            EditorSceneManager.playModeStartScene = courseScene;
    }
}
#endif
