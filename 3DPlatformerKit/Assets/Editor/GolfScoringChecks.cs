#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Opt-in rules, persistence and course-flow checks. Every save written by these
/// checks uses an isolated key; existing player records are never cleared.
/// </summary>
[InitializeOnLoad]
public static class GolfScoringChecks
{
    private const string PendingKey = "Golf.ScoringChecks.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> results = new List<string>();
    private static readonly List<string> temporaryKeys = new List<string>();

    static GolfScoringChecks()
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

    [MenuItem("Tools/Golf is Hard/Verify Scoring")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before running the scoring checks.");
            return;
        }
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static async void RunInPlayMode()
    {
        results.Clear();
        temporaryKeys.Clear();
        float initialFixedStep = Time.fixedDeltaTime;
        try
        {
            VerifyRulesAndRecords();
            await Wait(0.8f);
            var course = UnityEngine.Object.FindFirstObjectByType<GolfCourseManager>();
            var ball = UnityEngine.Object.FindFirstObjectByType<BallPlayerController>();
            Check(course != null && ball != null && course.Scorecard != null, "Scoring attaches to the live golf course");
            var scorecard = course.Scorecard;
            int count = course.TotalHoleCount;
            Check(count == 9 && scorecard.Pars.Length == count && scorecard.HoleNames.Length == count,
                "The scorecard contains all nine real course holes");
            Check(scorecard.Pars[0] == 3 && scorecard.HoleNames[0].Contains("Lagoon Lane"),
                "Course pars and titles match the authored holes");
            string courseId = NewCourseId();
            var isolatedRecords = new GolfScoreRecords(courseId, count);
            Set(scorecard, "records", isolatedRecords);
            Set(course, "playTutorialOnStart", false);
            Call(course, "BeginGameplay");
            await Wait(0.35f);
            Check(ReferenceEquals(scorecard.Records, isolatedRecords), "Gameplay retains the isolated record store");
            Check(course.IsPlayingCourse && !course.IsShowingResults && ball.enabled, "A new round starts in playable state");
            Check(AllZero(scorecard.RoundStrokes) && AllZero(scorecard.RoundBubbles), "New-round score and collectible totals begin empty");
            Check(course.RiskBubbles != null && course.RiskBubbles.Available == 3 && course.RiskBubbles.Collected == 0,
                "The first hole offers three fresh optional risk bubbles");
            VerifyBubblePlacements(course.RiskBubbles, 1);
            Directory.CreateDirectory("Screenshots");
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/scoring-bubbles.png"));
            await Wait(0.1f);

            course.HandleHoleEntered(course.ActiveHole, ball);
            Check(!course.IsShowingResults && scorecard.Records.Holes[0].bestStrokes == 0,
                "A zero-stroke arrival cannot manufacture an ace or a saved record");

            int firstPar = scorecard.Pars[0];
            Complete(course, ball, firstPar - 1);
            Check(course.IsShowingResults && scorecard.ShowingResult && !ball.enabled,
                "Sinking a hole opens the scorecard and stops further shots");
            var cameraController = Camera.main.GetComponent<ThirdPersonCameraController>();
            Check(!cameraController.IsCursorLocked && Cursor.lockState == CursorLockMode.None,
                "The scorecard releases the mouse for its action buttons");
            cameraController.LockMouse();
            Check(!cameraController.IsCursorLocked,
                "Result-screen clicks cannot recapture the cursor behind the scorecard");
            Check(scorecard.RoundStrokes[0] == firstPar - 1 && isolatedRecords.Holes[0].bestStrokes == firstPar - 1,
                "Hole completion banks the score and saves the personal best");
            course.HandleHoleEntered(course.ActiveHole, ball);
            Check(scorecard.RoundStrokes[0] == firstPar - 1, "Duplicate cup notifications cannot bank the hole twice");
            await Wait(0.4f);
            Directory.CreateDirectory("Screenshots");
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/scoring-result.png"));
            await Wait(0.1f);

            course.RetryScoredHole();
            Check(course.ActiveHoleNumber == 1 && !course.IsShowingResults && ball.enabled && scorecard.RoundStrokes[0] == 0,
                "Retry clears only the active round entry and restores the same tee");
            Check(isolatedRecords.Holes[0].bestStrokes == firstPar - 1,
                "Retrying does not erase an earned personal best");
            Check(course.RiskBubbles.Collected == 0 && course.RiskBubbles.Available == 3,
                "Retry respawns collectible opportunities without carrying pickups forward");
            Complete(course, ball, firstPar + 1);
            Check(scorecard.RoundStrokes[0] == firstPar + 1 && isolatedRecords.Holes[0].bestStrokes == firstPar - 1,
                "A worse retry changes the round score without replacing the saved best");
            course.ContinueAfterScorecard();
            Check(course.ActiveHoleNumber == 2 && !course.IsShowingResults && ball.enabled
                && scorecard.RoundStrokes[0] == firstPar + 1,
                "Continuing advances one hole and preserves the completed score");
            Check(course.RiskBubbles.Available == 3 && course.RiskBubbles.Collected == 0,
                "The next hole begins with its own three bubbles");
            VerifyBubblePlacements(course.RiskBubbles, 2);

            VerifySweptBubblePickup(course, ball);
            int collected = course.RiskBubbles.Collected;
            Complete(course, ball, scorecard.Pars[1]);
            Check(scorecard.RoundBubbles[1] == collected && isolatedRecords.Holes[1].bestBubbles == collected,
                "Collected bubbles bank on cup completion separately from strokes");
            Check(scorecard.RoundStrokes[1] == scorecard.Pars[1], "Collecting a bubble never discounts the golf score");
            course.ContinueAfterScorecard();

            while (!course.IsCourseComplete)
            {
                int index = course.ActiveHoleNumber - 1;
                Check(course.RiskBubbles.Available == 3, $"Hole {index + 1} has three authored risk bubbles");
                VerifyBubblePlacements(course.RiskBubbles, index + 1);
                VerifyLauncherRewards(course, ball, index + 1);
                Complete(course, ball, scorecard.Pars[index]);
                if (!course.IsCourseComplete)
                    course.ContinueAfterScorecard();
            }
            int total = Sum(scorecard.RoundStrokes);
            Check(!AllZero(scorecard.RoundStrokes) && Array.TrueForAll(scorecard.RoundStrokes, n => n > 0),
                "Completing the course records every hole exactly once");
            Check(course.IsShowingResults && scorecard.ShowingResult && isolatedRecords.BestRound == total,
                "The final scorecard records the complete round total");
            var reloaded = new GolfScoreRecords(courseId, count);
            Check(reloaded.BestRound == total && reloaded.Holes[0].bestStrokes == firstPar - 1,
                "Saved hole and round records survive creating a fresh record store");
            await Wait(0.4f);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Screenshots/scoring-round.png"));
            await Wait(0.1f);

            course.ReplayRound();
            Check(ReferenceEquals(scorecard.Records, isolatedRecords), "Replay keeps the same record store");
            Check(course.ActiveHoleNumber == 1 && course.IsPlayingCourse && !course.IsShowingResults
                && AllZero(scorecard.RoundStrokes) && AllZero(scorecard.RoundBubbles),
                "Replay starts a fresh round at the first tee");
            Check(isolatedRecords.BestRound == total && isolatedRecords.Holes[0].bestStrokes == firstPar - 1,
                "Replay retains earned records and medals");
            course.ReturnToStartMenu();
            Check(!course.IsPlayingCourse && course.RiskBubbles.Collected == 0 && course.RiskBubbles.Available == 0,
                "Returning to the menu clears live collectibles and gameplay state");
            Check(!(bool)Get(cameraController, "presentationInputBlocked"), "Returning to the menu clears result camera input blocking");
            results.Add("PASS: All scoring checks completed.");
            Debug.Log(string.Join("\n", results));
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
            Debug.LogError("Scoring checks failed: " + error);
        }
        finally
        {
            foreach (string key in temporaryKeys)
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            GolfShotFeedback.CancelActiveCinematics();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = initialFixedStep;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Directory.CreateDirectory("Temp/GolfScoringChecks");
            File.WriteAllLines("Temp/GolfScoringChecks/results.txt", results);
            EditorApplication.isPlaying = false;
        }
    }

    private static void VerifyRulesAndRecords()
    {
        Check(GolfScoreRecords.Rating(1, 5).Contains("HOLE IN ONE"), "Hole-in-one takes precedence over relative score");
        Check(GolfScoreRecords.Rating(2, 5).Contains("ALBATROSS"), "Three under par earns an albatross");
        Check(GolfScoreRecords.Rating(2, 4).Contains("EAGLE"), "Two under par earns an eagle");
        Check(GolfScoreRecords.Rating(2, 3).Contains("BIRDIE"), "One under par earns a birdie");
        Check(GolfScoreRecords.Rating(3, 3) == "PAR", "Even par earns the par announcement");
        Check(GolfScoreRecords.Rating(4, 3).Contains("BOGEY") && GolfScoreRecords.Rating(5, 3).Contains("DOUBLE"),
            "Over-par ratings reflect the actual stroke difference");
        Check(GolfScoreRecords.Medal(2, 3) == 3 && GolfScoreRecords.Medal(3, 3) == 2
            && GolfScoreRecords.Medal(4, 3) == 1 && GolfScoreRecords.Medal(5, 3) == 0,
            "Gold, silver and bronze reward under-par, par and one-over scores");
        Check(GolfScoreRecords.Relative(0) == "E" && GolfScoreRecords.Relative(-2) == "-2"
            && GolfScoreRecords.Relative(3) == "+3", "Relative totals use recognizable golf notation");

        string id = NewCourseId();
        var records = new GolfScoreRecords(id, 9);
        Check(records.BestRound == 0 && records.Holes.Length == 9 && records.Holes[0].bestStrokes == 0,
            "Missing records load as unplayed holes rather than zero-stroke wins");
        var first = records.RecordHole(0, 4, 3, 1, 3);
        Check(first.newBestScore && first.previousBest == 0 && records.Holes[0].bestMedal == 1,
            "The first finish establishes a best score and its bronze medal");
        var worse = records.RecordHole(0, 5, 3, 3, 3);
        Check(!worse.newBestScore && records.Holes[0].bestStrokes == 4
            && worse.newBestBubbles && records.Holes[0].bestBubbles == 3,
            "Bubble progress can improve independently of the stroke record");
        records.RecordHole(0, 3, 3, 0, 3);
        Check(records.Holes[0].bestMedal == 2 && records.Holes[0].bestBubbles == 3,
            "A par finish upgrades to silver without losing the bubble record");
        var gold = records.RecordHole(0, 2, 3, 0, 3);
        Check(gold.newBestScore && gold.newMedal && gold.previousBest == 3 && records.Holes[0].bestMedal == 3,
            "A birdie upgrades the saved score and gold medal");
        var same = records.RecordHole(0, 2, 3, 0, 3);
        Check(!same.newBestScore && !same.newBestBubbles && !same.newMedal,
            "Matching an existing result does not repeatedly award a new record");
        Check(records.RecordRound(30, 9), "The first completed round establishes its record");
        Check(!records.RecordRound(33, 15) && records.BestRound == 30 && records.BestRoundBubbles == 15,
            "A slower round preserves the stroke record while improving bubble progress");
        var reloaded = new GolfScoreRecords(id, 9);
        Check(reloaded.Holes[0].bestStrokes == 2 && reloaded.Holes[0].bestMedal == 3
            && reloaded.Holes[0].bestBubbles == 3 && reloaded.BestRound == 30 && reloaded.BestRoundBubbles == 15,
            "Hole records, medals and both round records persist through reload");
        records.RecordHole(-1, 1, 3, 3, 3);
        records.RecordHole(999, 1, 3, 3, 3);
        records.RecordHole(0, 0, 3, 3, 3);
        Check(records.Holes[0].bestStrokes == 2, "Invalid hole indices and zero-stroke results cannot corrupt a record");
        var changedCount = new GolfScoreRecords(id, 3);
        Check(changedCount.Holes.Length == 3, "A changed course hole count loads safely");
        string corruptId = NewCourseId();
        PlayerPrefs.SetString(GolfScoreRecords.StorageKeyFor(corruptId), "{invalid saved data");
        var corrupt = new GolfScoreRecords(corruptId, 9);
        Check(corrupt.Holes.Length == 9 && corrupt.BestRound == 0 && corrupt.Holes[0].bestStrokes == 0,
            "Corrupt save data recovers to a clean playable state");
    }

    private static void VerifySweptBubblePickup(GolfCourseManager course, BallPlayerController ball)
    {
        var pickups = course.RiskBubbles;
        var bubbles = (Array)Get(pickups, "bubbles");
        Check(bubbles != null && bubbles.Length == 3, "Risk-route collectibles exist as three visible world objects");
        object bubble = bubbles.GetValue(0);
        var visual = (Transform)bubble.GetType().GetField("visual").GetValue(bubble);
        Check(visual != null && visual.gameObject.activeInHierarchy, "A risk bubble has a visible active target");
        var body = ball.GetComponent<Rigidbody>();
        bool gravity = body.useGravity;
        body.useGravity = false;
        body.isKinematic = false;
        SetStrokes(ball, 1);
        Vector3 center = visual.position;
        MoveBall(body, center - Vector3.forward * 3f, Vector3.forward * 80f);
        Call(pickups, "Update"); // Establish a fresh sweep origin after the deliberate test teleport.
        int before = pickups.Collected;
        MoveBall(body, center + Vector3.forward * 3f, Vector3.forward * 80f);
        Call(pickups, "Update");
        Check(pickups.Collected == before + 1, "A fast shot crossing a bubble collects it between rendered positions");
        Call(pickups, "Update");
        Check(pickups.Collected == before + 1, "A collected bubble can be awarded only once per attempt");
        object next = bubbles.GetValue(1);
        var nextVisual = (Transform)next.GetType().GetField("visual").GetValue(next);
        int beforePause = pickups.Collected;
        Time.timeScale = 0f;
        MoveBall(body, nextVisual.position, Vector3.zero);
        Call(pickups, "Update");
        Check(pickups.Collected == beforePause, "Paused gameplay cannot collect a bubble");
        Time.timeScale = 1f;
        body.useGravity = gravity;
    }

    private static void VerifyBubblePlacements(GolfRiskBubbles pickups, int holeNumber)
    {
        var bubbles = (Array)Get(pickups, "bubbles");
        Check(bubbles != null && bubbles.Length == 3, $"Hole {holeNumber} creates all three collectible visuals");
        var positions = new Vector3[bubbles.Length];
        for (int i = 0; i < bubbles.Length; i++)
        {
            object bubble = bubbles.GetValue(i);
            var visual = (Transform)bubble.GetType().GetField("visual").GetValue(bubble);
            Check(visual != null, $"Hole {holeNumber} bubble {i + 1} has a visible target");
            positions[i] = visual.position;
            bool finite = !(float.IsNaN(positions[i].x) || float.IsInfinity(positions[i].x)
                || float.IsNaN(positions[i].y) || float.IsInfinity(positions[i].y)
                || float.IsNaN(positions[i].z) || float.IsInfinity(positions[i].z));
            Check(finite, $"Hole {holeNumber} bubble {i + 1} has a finite world placement");
            for (int previous = 0; previous < i; previous++)
                Check(Vector3.Distance(positions[i], positions[previous]) > 2f,
                    $"Hole {holeNumber} bubbles {previous + 1} and {i + 1} reward separate parts of the route");
        }
    }

    private static void VerifyLauncherRewards(GolfCourseManager course, BallPlayerController ball, int holeNumber)
    {
        var pickups = course.RiskBubbles;
        var launcher = (AeroLaunchPad)Get(pickups, "routeLauncher");
        if (launcher == null) return;
        var body = ball.GetComponent<Rigidbody>();
        Vector3 from = launcher.GetComponent<Collider>().bounds.center;
        SetStrokes(ball, 1);
        MoveBall(body, from, launcher.transform.forward * 4f);
        Call(launcher, "OnTriggerEnter", ball.GetComponent<Collider>());
        Check((bool)Get(pickups, "alignedWithLaunch"), $"Hole {holeNumber} reward route responds to an actual launcher firing");
        var bubbles = (Array)Get(pickups, "bubbles");
        for (int i = 0; i < bubbles.Length; i++)
        {
            float t = launcher.LastFlightTime * (0.2f + i * 0.3f);
            Vector3 expected = from + body.linearVelocity * t + Physics.gravity * (0.5f * launcher.gravityMultiplier * t * t);
            Vector3 anchor = (Vector3)bubbles.GetValue(i).GetType().GetField("anchor").GetValue(bubbles.GetValue(i));
            Check(Vector3.Distance(anchor, expected) < 0.01f, $"Hole {holeNumber} bubble {i + 1} lies on the actual launch trajectory");
        }
    }

    private static void MoveBall(Rigidbody body, Vector3 position, Vector3 velocity)
    {
        body.position = position;
        body.transform.position = position;
        body.linearVelocity = velocity;
        Physics.SyncTransforms();
    }

    private static void Complete(GolfCourseManager course, BallPlayerController ball, int strokes)
    {
        MoveBall(ball.GetComponent<Rigidbody>(), course.ActiveHole.transform.position + Vector3.up * 0.25f, Vector3.zero);
        Camera.main.GetComponent<ThirdPersonCameraController>().SnapFollowToTarget();
        SetStrokes(ball, strokes);
        ball.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        course.HandleHoleEntered(course.ActiveHole, ball);
    }

    private static void SetStrokes(BallPlayerController ball, int value)
    {
        typeof(BallPlayerController).GetProperty("StrokeCount").SetValue(ball, value);
    }

    private static string NewCourseId()
    {
        string id = "scoring-check-" + Guid.NewGuid().ToString("N");
        temporaryKeys.Add(GolfScoreRecords.StorageKeyFor(id));
        return id;
    }
    private static bool AllZero(int[] values) => values != null && Array.TrueForAll(values, n => n == 0);
    private static int Sum(int[] values) { int sum = 0; foreach (int value in values) sum += value; return sum; }
    private static object Get(object instance, string field) => instance.GetType().GetField(field, Private).GetValue(instance);
    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static object Call(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, Private).Invoke(instance, args);
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
            if (!EditorApplication.isPlaying) throw new OperationCanceledException("Play Mode ended during scoring checks.");
            await Task.Delay(16);
        }
    }
}
#endif
