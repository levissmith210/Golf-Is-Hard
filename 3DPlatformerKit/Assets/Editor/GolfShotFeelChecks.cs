#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in checks against the real course, physics, camera and pause menu.</summary>
[InitializeOnLoad]
public static class GolfShotFeelChecks
{
    private const string PendingKey = "Golf.ShotFeelChecks.Pending";
    private static readonly List<string> results = new List<string>();
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static GolfShotFeelChecks()
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

    [MenuItem("Tools/Golf is Hard/Verify Shot Feel %F8")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before running the shot checks.");
            return;
        }
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static async void RunInPlayMode()
    {
        results.Clear();
        float initialFixedStep = Time.fixedDeltaTime;
        try
        {
            Check(Mathf.Approximately(GolfShotTiming.Power(0f, 1f), 0f), "Meter begins at zero");
            Check(Mathf.Approximately(GolfShotTiming.Power(1f, 1f), 1f), "Meter reaches full power");
            Check(Mathf.Approximately(GolfShotTiming.Power(1.5f, 1f), 0.5f), "Holding past full power returns down the meter");
            Check(Mathf.Approximately(GolfShotTiming.Power(2f, 1f), 0f), "Meter completes a cycle");
            Check(!GolfShotTiming.IsPerfect(0.9f) && GolfShotTiming.IsPerfect(0.95f)
                && !GolfShotTiming.IsPerfect(1f), "Perfect timing has both an early and a late boundary");
            Check(GolfShotTiming.Force(0f, 4.5f, 21f, 0.08f) < 1f, "Feather putts have a low launch floor");
            Check(GolfShotTiming.Force(0.95f, 4.5f, 21f, 0.08f) > GolfShotTiming.Force(1f, 4.5f, 21f, 0.08f),
                "A timed perfect shot beats an untimed maximum shot");
            await Wait(0.8f);
            var course = UnityEngine.Object.FindFirstObjectByType<GolfCourseManager>();
            var ball = UnityEngine.Object.FindFirstObjectByType<BallPlayerController>();
            var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
            Check(course != null && ball != null && pause != null, "Live course, ball and pause menu found");
            Set(course, "playTutorialOnStart", false);
            Call(course, "BeginGameplay");
            await Wait(0.7f);
            Vector3 tee = ball.transform.position;
            Quaternion rotation = ball.transform.rotation;
            var body = ball.GetComponent<Rigidbody>();
            var feedback = ball.ShotFeedback;
            var camera = Camera.main;
            float baseFov = camera.fieldOfView;
            Check(feedback != null && feedback.enabled, "Shot presentation is attached automatically");
            Check(ball.IsGrounded, "Ball has real ground contact at the first tee");

            Set(ball, "isCharging", true);
            Set(ball, "showAngleIndicator", true);
            Set(ball, "chargeStartedAt", Time.time);
            await Wait(0.45f);
            Directory.CreateDirectory("Screenshots");
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/shot-feel-meter.png"));
            await Wait(0.08f);
            Call(ball, "CancelShot");

            Fire(ball, 0.01f, 0f);
            Check(body.linearVelocity.magnitude > 0.1f && body.linearVelocity.magnitude < 2f && ball.StrokeCount == 1,
                "A low-power putt launches softly and counts exactly one stroke");
            await Reset(ball, tee, rotation);
            body.linearVelocity = Vector3.right * 3f;
            Fire(ball, 0.5f, 25f);
            Check(Mathf.Abs(Vector3.Dot(body.linearVelocity.normalized, Vector3.right)) < 0.99f,
                "Launch follows the aimed direction rather than stale travel");
            float expectedSpeed = ExpectedSpeed(ball, 0.5f);
            Check(Mathf.Abs(body.linearVelocity.magnitude - expectedSpeed) < 0.02f,
                "Launch replaces previous velocity without adding inherited speed");
            await Wait(0.22f);
            var trail = ball.GetComponentInChildren<TrailRenderer>();
            Check(feedback.HasActiveShot && trail != null && trail.positionCount > 1,
                "A moving shot produces a visible trail");
            Check(camera.fieldOfView > baseFov + 0.1f, "Flight camera widens during a lofted shot");

            await Reset(ball, tee, rotation);
            Fire(ball, 0.95f, 28f);
            Check(Mathf.Abs(body.linearVelocity.magnitude - ExpectedSpeed(ball, 0.95f)) < 0.03f,
                "Perfect timing applies the bonus to the actual launch");
            Check((string)Get(feedback, "message") == "PERFECT!", "Perfect shot displays its timing feedback");
            await Wait(0.09f);
            Directory.CreateDirectory("Screenshots");
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/shot-feel-perfect.png"));
            await Wait(0.12f);

            // A real airborne shot must land, not merely fire a synthetic collision event.
            float landingDeadline = Time.realtimeSinceStartup + 8f;
            while (!(bool)Get(feedback, "hasLanded") && Time.realtimeSinceStartup < landingDeadline)
                await Wait(0.05f);
            Check((bool)Get(feedback, "hasLanded"), "Lofted shot produces first-landing feedback through physics");
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/shot-feel-landing.png"));
            await Wait(0.08f);

            await Reset(ball, tee, rotation);
            Set(ball, "isCharging", true);
            Set(ball, "chargeStartedAt", Time.time - 0.4f);
            int strokes = ball.StrokeCount;
            Call(ball, "CancelShot");
            Check(!ball.IsCharging && ball.StrokeCount == strokes, "Canceling a charge never fires or adds a stroke");
            Set(ball, "isCharging", true);
            Call(ball, "OnApplicationFocus", false);
            Check(!ball.IsCharging, "Losing focus safely cancels a held charge");

            HoleTrigger hole = course.ActiveHole;
            Check(hole != null, "Cup moment targets the active hole");
            PlaceAtCup(ball, hole, Vector3.back * 3f);
            Check(!feedback.IsCupMomentActive, "Moving away from the cup does not trigger slow motion");
            PlaceAtCup(ball, hole, Vector3.forward * 3f, 6f);
            Check(!feedback.IsCupMomentActive, "Flying high over the cup does not trigger slow motion");
            PlaceAtCup(ball, hole, Vector3.right * 3f);
            Check(!feedback.IsCupMomentActive, "A nearby sideways miss does not trigger slow motion");
            PlaceAtCup(ball, hole, Vector3.forward * 3f);
            Check(feedback.IsCupMomentActive && Time.timeScale < 1f,
                "A low approaching shot earns the cup slow-motion moment");
            Check(Mathf.Abs(Time.fixedDeltaTime / Time.timeScale - initialFixedStep) < 0.0001f,
                "Cup slow motion preserves physics step frequency");
            Call(pause, "PauseGame");
            Check(Time.timeScale == 0f && !feedback.IsCupMomentActive && !ball.IsCharging,
                "Pausing cancels the cinematic and leaves gameplay paused");
            await Wait(0.15f);
            Check(Time.timeScale == 0f, "Cinematic expiration cannot unpause the menu");
            Call(pause, "ResumeGame", false);
            Check(Mathf.Approximately(Time.timeScale, 1f) && Mathf.Approximately(Time.fixedDeltaTime, initialFixedStep),
                "Resume restores normal time and the original physics step");

            PlaceAtCup(ball, hole, Vector3.forward * 3f);
            Check(feedback.IsCupMomentActive, "A new shot can earn a new cup moment");
            body.isKinematic = true;
            await Wait(1f);
            Check(!feedback.IsCupMomentActive && Mathf.Approximately(Time.timeScale, 1f),
                "Cup slow motion expires after its bounded real-time duration");
            body.isKinematic = false;

            PlaceAtCup(ball, hole, Vector3.forward * 3f);
            Call(pause, "PauseGame");
            Call(pause, "RestartLevel");
            Check(!pause.IsPaused && Time.timeScale == 1f && !feedback.HasActiveShot && !trail.gameObject.activeSelf && !trail.emitting,
                "Restarting from pause clears shot effects and teleport trails");

            await Reset(ball, tee, rotation);
            Fire(ball, 0.6f, 15f);
            await Wait(0.05f);
            int beforePenalty = ball.StrokeCount;
            ball.ReturnToSafeSpot(tee, 1);
            Check(ball.StrokeCount == beforePenalty + 1 && !feedback.HasActiveShot && !trail.gameObject.activeSelf && !trail.emitting,
                "Hazard return keeps penalties and clears the trail across teleportation");

            PlaceAtCup(ball, hole, Vector3.forward * 3f);
            course.HandleHoleEntered(hole, ball);
            Check(ball.IsHoleComplete && !ball.enabled && !feedback.IsCupMomentActive && Time.timeScale == 1f,
                "Cup capture stops shots and restores normal time");
            Check(((ParticleSystem)Get(feedback, "sparks")).particleCount > 0,
                "Cup celebration survives disabling the ball controller");
            await Wait(0.1f);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/shot-feel-cup.png"));
            await Wait(0.1f);
            results.Add("PASS: All shot feel checks completed.");
            Debug.Log(string.Join("\n", results));
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
            Debug.LogError("Shot feel checks failed: " + error);
        }
        finally
        {
            GolfShotFeedback.CancelActiveCinematics();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = initialFixedStep;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Directory.CreateDirectory("Temp/ShotFeelChecks");
            File.WriteAllLines("Temp/ShotFeelChecks/results.txt", results);
            EditorApplication.isPlaying = false;
        }
    }

    private static void PlaceAtCup(BallPlayerController ball, HoleTrigger hole, Vector3 velocity, float height = 0.25f)
    {
        Vector3 position = hole.transform.position + Vector3.back + Vector3.up * height;
        ball.ResetForNextHole(position, Quaternion.identity);
        ball.GetComponent<Rigidbody>().linearVelocity = velocity;
        ball.ShotFeedback.Launch(0.3f, false, velocity);
        Set(ball.ShotFeedback, "launchedAt", Time.time - 0.2f);
        Physics.SyncTransforms();
        Call(ball.ShotFeedback, "TryCupMoment", velocity);
    }

    private static async Task Reset(BallPlayerController ball, Vector3 position, Quaternion rotation)
    {
        ball.ResetForNextHole(position, rotation);
        Camera.main.GetComponent<ThirdPersonCameraController>().SnapFollowToTarget();
        await Wait(0.45f);
        Check(ball.IsGrounded, "Reset ball settles on the tee");
    }

    private static void Fire(BallPlayerController ball, float power, float elevation)
    {
        Set(ball, "isCharging", true);
        Set(ball, "shotElevationDegrees", elevation);
        float duration = (float)Get(ball, "maxChargeTime") / (float)Get(ball, "chargeSpeedMultiplier");
        Set(ball, "chargeStartedAt", Time.time - duration * power);
        Call(ball, "ReleaseShot");
    }

    private static float ExpectedSpeed(BallPlayerController ball, float power)
    {
        return GolfShotTiming.Force(power, (float)Get(ball, "minimumShotForce"), (float)Get(ball, "maximumShotForce"),
            (float)Get(ball, "perfectShotBonus")) * (float)Get(ball, "shotForceMultiplier")
            * (float)Get(ball, "shotDistanceBoost") * (float)Call(ball, "LiePowerMultiplier") / ball.GetComponent<Rigidbody>().mass;
    }

    private static object Get(object instance, string field) => instance.GetType().GetField(field, Private).GetValue(instance);
    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static object Call(object instance, string method, params object[] arguments) => instance.GetType().GetMethod(method, Private).Invoke(instance, arguments);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        results.Add("PASS: " + message);
    }
    private static async Task Wait(float seconds)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < until)
        {
            if (!EditorApplication.isPlaying) throw new OperationCanceledException("Play Mode ended during checks.");
            await Task.Delay(16);
        }
    }
}
#endif
