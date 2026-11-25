#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in checks for replay, skipping and transitions across the real tutorial scenes.</summary>
[InitializeOnLoad]
public static class GolfTutorialChecks
{
    private const string PendingKey = "Golf.TutorialChecks.Pending";
    private const string CompletedKey = "GolfIsHard.TutorialCompleted.v1";
    private const string MainScene = "FrutigerAeroNineHole";
    private const string TutorialScene = "NineHoleCourse";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> results = new List<string>();

    static GolfTutorialChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                RunInPlayMode();
            }
        };
    }

    [MenuItem("Tools/Golf is Hard/Verify Tutorial")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before running the tutorial checks.");
            return;
        }
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static async void RunInPlayMode()
    {
        results.Clear();
        bool hadCompletedPreference = PlayerPrefs.HasKey(CompletedKey);
        int originalCompletedPreference = PlayerPrefs.GetInt(CompletedKey, 0);
        float originalFixedStep = Time.fixedDeltaTime;
        string realRecordKey = null;
        string originalRecordValue = null;
        bool hadRealRecords = false;
        try
        {
            await Wait(0.8f);
            var course = UnityEngine.Object.FindFirstObjectByType<GolfCourseManager>();
            Check(course != null && SceneManager.GetActiveScene().name == MainScene,
                "The checks begin in the real main course");
            realRecordKey = GolfScoreRecords.StorageKeyFor(course.Scorecard.CourseId);
            hadRealRecords = PlayerPrefs.HasKey(realRecordKey);
            originalRecordValue = PlayerPrefs.GetString(realRecordKey, string.Empty);

            ParameterInfo[] settingsParameters = typeof(GameSettings).GetMethod("DrawSettingsMenu").GetParameters();
            Check(settingsParameters.Length == 1 && settingsParameters[0].ParameterType == typeof(Action),
                "The shared settings panel accepts a replay-tutorial action");
            Set(course, "settingsOpen", true);
            Directory.CreateDirectory("Screenshots");
            await Wait(0.1f);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/tutorial-settings.png"));
            await Wait(0.1f);

            PlayerPrefs.SetInt(CompletedKey, 1);
            course.ReplayTutorial();
            course = await WaitForCourse(TutorialScene, true);
            Check(PlayerPrefs.GetInt(CompletedKey) == 1,
                "Explicit replay works even after the tutorial was previously completed");
            VerifyFreshTutorial(course);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/tutorial-skip.png"));
            await Wait(0.1f);

            Check(!Hold(course, true, 0.5f) && course.TutorialSkipProgress > 0f && course.TutorialSkipProgress < 1f,
                "A partial Space hold advances the skip progress without changing scenes");
            Check(!Hold(course, false, 0f) && course.TutorialSkipProgress == 0f && course.IsInTutorial,
                "Releasing Space cancels a partial skip");

            Hold(course, true, 0.4f);
            var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
            Call(pause, "PauseGame");
            Check(Time.timeScale == 0f && pause.IsPaused, "The tutorial can be paused normally");
            Check(!Hold(course, true, 2f) && course.TutorialSkipProgress == 0f,
                "Holding Space while paused cannot skip or accumulate progress");
            Call(pause, "ResumeGame", false);
            Check(Time.timeScale == 1f && !pause.IsPaused, "Resuming the tutorial restores normal gameplay time");

            Hold(course, true, 0.4f);
            Call(course, "OnApplicationFocus", false);
            Check(course.TutorialSkipProgress == 0f && course.IsInTutorial,
                "Losing application focus cancels an unfinished skip hold");
            Check(!Hold(course, true, 1.19f) && course.IsInTutorial,
                "A hold shorter than the skip threshold stays in the tutorial");
            Check(Hold(course, true, 0.02f), "A full hold triggers the whole-tutorial skip");
            course = await WaitForCourse(MainScene, false);
            VerifyPlayableMainCourse(course);
            Check(PlayerPrefs.GetInt(CompletedKey, 0) == 1,
                "Skipping remembers the tutorial choice for future tee-offs");

            course.ReplayTutorial();
            course = await WaitForCourse(TutorialScene, true);
            SetTutorialStep(course, "WaitingForNextHole");
            Set(course, "isTransitioning", true);
            Set(course, "awaitingNextHole", true);
            Call(course, "AdvanceToNextHole");
            Check((int)Get(course, "activeTutorialHoleIndex") == 1
                && Get(course, "tutorialStep").ToString() == "AngleSecondHole",
                "The second tutorial lesson is a valid skip starting point");
            Check(Hold(course, true, 1.3f), "Holding Space also skips the entire second lesson");
            course = await WaitForCourse(MainScene, false);
            VerifyPlayableMainCourse(course);

            pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
            Call(pause, "PauseGame");
            Set(pause, "settingsOpen", true);
            Check(pause.IsPaused && Time.timeScale == 0f && AudioListener.pause,
                "Pause settings begin with gameplay and audio paused");
            Call(pause, "ReplayTutorialFromSettings");
            course = await WaitForCourse(TutorialScene, true);
            VerifyFreshTutorial(course);
            pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
            Check(pause != null && !pause.IsPaused && Time.timeScale == 1f && !AudioListener.pause,
                "Replay from pause settings clears the old pause and audio state");

            Hold(course, true, 0.4f);
            course.ReplayTutorial();
            course = await WaitForCourse(TutorialScene, true);
            VerifyFreshTutorial(course);
            Check(course.TutorialSkipProgress == 0f,
                "Replaying an active tutorial restarts its first lesson with no stale hold progress");
            SetTutorialStep(course, "WaitingForNextHole");
            Set(course, "isTransitioning", true);
            Set(course, "awaitingNextHole", true);
            Check(Hold(course, true, 1.3f), "The skip prompt also works between tutorial holes");
            course = await WaitForCourse(MainScene, false);
            VerifyPlayableMainCourse(course);
            Check(PlayerPrefs.HasKey(realRecordKey) == hadRealRecords
                && PlayerPrefs.GetString(realRecordKey, string.Empty) == originalRecordValue,
                "Tutorial replay and skipping leave competitive score records untouched");
            results.Add("PASS: All tutorial checks completed.");
            Debug.Log(string.Join("\n", results));
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
            Debug.LogError("Tutorial checks failed: " + error);
        }
        finally
        {
            if (hadCompletedPreference) PlayerPrefs.SetInt(CompletedKey, originalCompletedPreference);
            else PlayerPrefs.DeleteKey(CompletedKey);
            PlayerPrefs.Save();
            GolfShotFeedback.CancelActiveCinematics();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = originalFixedStep;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Directory.CreateDirectory("Temp/GolfTutorialChecks");
            File.WriteAllLines("Temp/GolfTutorialChecks/results.txt", results);
            EditorApplication.isPlaying = false;
        }
    }

    private static void VerifyFreshTutorial(GolfCourseManager course)
    {
        Check(course.IsInTutorial && (int)Get(course, "activeTutorialHoleIndex") == 0
            && Get(course, "tutorialStep").ToString() == "LookAround",
            "Replay starts at the first look-around lesson");
        var ball = UnityEngine.Object.FindFirstObjectByType<BallPlayerController>();
        Check(ball != null && ball.enabled && !(bool)Get(ball, "tutorialCanShoot")
            && !(bool)Get(ball, "tutorialCanAdjustAngle"),
            "The replayed first lesson restores its guided shot-control gates");
        Check(course.RiskBubbles.Available == 0 && !course.IsShowingResults,
            "Tutorial replay hides competitive collectibles and scorecards");
    }

    private static void VerifyPlayableMainCourse(GolfCourseManager course)
    {
        Check(!course.IsInTutorial && course.IsPlayingCourse && course.ActiveHoleNumber == 1,
            "Skipping goes directly to the first main-course tee");
        var ball = UnityEngine.Object.FindFirstObjectByType<BallPlayerController>();
        Check(ball != null && ball.enabled && (bool)Get(ball, "tutorialCanShoot")
            && (bool)Get(ball, "tutorialCanAdjustAngle"),
            "Skipping restores both shooting and shot-angle controls");
        Check(Time.timeScale == 1f && !AudioListener.pause,
            "The skipped-to course runs at normal speed with audio active");
    }

    private static async Task<GolfCourseManager> WaitForCourse(string scene, bool tutorial)
    {
        double deadline = EditorApplication.timeSinceStartup + 20f;
        while (EditorApplication.timeSinceStartup < deadline)
        {
            if (!EditorApplication.isPlaying) throw new OperationCanceledException("Play Mode ended during tutorial checks.");
            var course = UnityEngine.Object.FindFirstObjectByType<GolfCourseManager>();
            if (SceneManager.GetActiveScene().name == scene && course != null && course.IsInTutorial == tutorial)
            {
                await Wait(0.12f);
                return course;
            }
            await Task.Delay(16);
        }
        throw new TimeoutException("Tutorial transition did not reach " + scene + ".");
    }

    private static bool Hold(GolfCourseManager course, bool held, float delta)
        => (bool)Call(course, "UpdateTutorialSkipHold", held, delta);
    private static void SetTutorialStep(GolfCourseManager course, string step)
    {
        FieldInfo field = course.GetType().GetField("tutorialStep", Private);
        field.SetValue(course, Enum.Parse(field.FieldType, step));
    }
    private static object Get(object instance, string field) => instance.GetType().GetField(field, Private).GetValue(instance);
    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static object Call(object instance, string method, params object[] arguments)
        => instance.GetType().GetMethod(method, Private).Invoke(instance, arguments);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        results.Add("PASS: " + message);
    }
    private static async Task Wait(float seconds)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < deadline)
        {
            if (!EditorApplication.isPlaying) throw new OperationCanceledException("Play Mode ended during tutorial checks.");
            await Task.Delay(16);
        }
    }
}
#endif
