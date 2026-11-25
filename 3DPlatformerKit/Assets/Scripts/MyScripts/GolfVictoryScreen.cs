using UnityEngine;

/// <summary>The final-course trophy presentation. Per-hole scorecards continue to use GolfScorecard.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(GolfVictoryTrophy), typeof(GolfVictoryAudio))]
public sealed class GolfVictoryScreen : MonoBehaviour
{
    private GolfCourseManager manager;
    private GolfScorecard scorecard;
    private GolfVictoryTrophy trophy;
    private GolfVictoryAudio sound;
    private int[] strokes, pars;
    private string[] names;
    private int total, parTotal, bubbles, availableBubbles, bestRound, tier;
    private bool newRecord, visible, details, revealed, recordPlayed;
    private float age;
    private GUIStyle eyebrow, headline, award, number, body, small, button, table;
    private static readonly Color Ink = new Color(0.055f, 0.25f, 0.36f);
    private static readonly Color Muted = new Color(0.20f, 0.43f, 0.53f);

    public bool Visible => visible;
    public bool ReadyForAction => visible && age >= 2.4f;
    public int AwardTier => tier;

    public static int EvaluateTrophy(int score, int par, int holes)
    {
        int delta = score - par;
        if (delta <= -Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(1, holes) * 0.66f))) return 4;
        if (delta <= 0) return 3;
        if (delta <= Mathf.Max(1, holes)) return 2;
        return 1;
    }

    public void Configure(GolfCourseManager course, GolfScorecard scoring)
    {
        manager = course; scorecard = scoring;
        trophy = GetComponent<GolfVictoryTrophy>();
        sound = GetComponent<GolfVictoryAudio>();
        if (course != null) sound.SetCourseFanfareFallback(course.CompletionSound);
    }

    public void Show(bool personalBest)
    {
        if (scorecard == null) return;
        SetData(scorecard.RoundStrokes, scorecard.Pars, scorecard.HoleNames,
            scorecard.BubbleTotal, scorecard.AvailableBubbleTotal, scorecard.Records.BestRound, personalBest);
        age = 0f; revealed = false; recordPlayed = false; details = false; visible = true;
        trophy = GetComponent<GolfVictoryTrophy>(); sound = GetComponent<GolfVictoryAudio>();
        trophy.SetAward(tier);
        sound.Begin(tier);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        FrutigerAeroUI.ResetGlassButtonHover();
    }

    private void SetData(int[] round, int[] coursePar, string[] holeNames, int collected, int available, int best, bool personalBest)
    {
        strokes = round != null ? (int[])round.Clone() : new int[0];
        pars = coursePar != null ? (int[])coursePar.Clone() : new int[0];
        names = holeNames != null ? (string[])holeNames.Clone() : new string[0];
        total = 0; parTotal = 0;
        foreach (int value in strokes) total += value;
        foreach (int value in pars) parTotal += value;
        bubbles = collected; availableBubbles = available; bestRound = best; newRecord = personalBest;
        tier = EvaluateTrophy(total, parTotal, pars.Length);
    }

    public void Hide()
    {
        bool wasVisible = visible;
        visible = false; details = false;
        if (sound == null) sound = GetComponent<GolfVictoryAudio>();
        if (trophy == null) trophy = GetComponent<GolfVictoryTrophy>();
        if (wasVisible && sound != null) sound.End();
        if (trophy != null) trophy.DisposePreview();
    }

    private void Update()
    {
        if (!visible || Time.timeScale <= 0f) return;
        age += Time.unscaledDeltaTime;
        trophy.Tick(age, Time.unscaledDeltaTime);
        if (!revealed && age >= 0.65f) { revealed = true; sound.PlayTrophyReveal(); }
        if (!recordPlayed && age >= 2.15f)
        {
            recordPlayed = true;
            if (newRecord) sound.PlayRecordAccent();
        }
    }

    private void OnGUI()
    {
        if (!visible || Time.timeScale <= 0f || manager == null || !manager.IsShowingResults) return;
        int depth = GUI.depth; GUI.depth = -45;
        DrawPresentation(new Rect(0f, 0f, Screen.width, Screen.height), age, true);
        GUI.depth = depth;
    }

    private void Styles()
    {
        if (eyebrow != null) return;
        eyebrow = FrutigerAeroUI.CreateStyle(12, Muted);
        headline = FrutigerAeroUI.CreateStyle(46, Ink);
        award = FrutigerAeroUI.CreateStyle(30, Ink);
        number = FrutigerAeroUI.CreateStyle(92, Ink);
        body = FrutigerAeroUI.CreateStyle(18, Ink);
        small = FrutigerAeroUI.CreateStyle(14, Muted, TextAnchor.MiddleCenter, false);
        button = FrutigerAeroUI.CreateStyle(18, Ink);
        table = FrutigerAeroUI.CreateStyle(13, Ink, TextAnchor.MiddleLeft, false);
    }

    public void DrawPresentation(Rect viewport, float elapsed, bool interactive)
    {
        Styles();
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        bool previousEnabled = GUI.enabled;
        Solid(viewport, new Color(0.015f, 0.17f, 0.27f, Mathf.Clamp01(elapsed / 0.4f) * 0.58f));
        float scale = Mathf.Min(viewport.width / 1280f, viewport.height / 720f);
        Vector2 offset = new Vector2(viewport.x + (viewport.width - 1280f * scale) * 0.5f,
            viewport.y + (viewport.height - 720f * scale) * 0.5f);
        GUI.matrix = previousMatrix * Matrix4x4.TRS(offset, Quaternion.identity, new Vector3(scale, scale, 1f));
        Color accent = AwardColor(tier);
        float enter = Smooth(Mathf.Clamp01(elapsed / 0.55f));
        GUI.matrix *= Matrix4x4.Translate(Vector3.up * ((1f - enter) * 24f));
        Rect panel = new Rect(118f, 44f, 1044f, 632f);
        FrutigerAeroUI.DrawGlassPanel(panel, new Color(0.22f, 0.78f, 0.97f), new Color(0.86f, 0.97f, 1f, 0.97f), 0.32f);
        FrutigerAeroUI.DrawRounded(new Rect(130f, 56f, 1020f, 90f), new Color(1f, 1f, 1f, 0.46f));
        GUI.Label(new Rect(240f, 62f, 800f, 22f), "GOLF IS HARD   /   THE AERO TOUR", eyebrow);
        float headlinePop = 1f + Mathf.Sin(elapsed * 15f) * Mathf.Exp(-elapsed * 4f) * 0.035f;
        Matrix4x4 baseMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(Vector2.one * headlinePop, new Vector2(640f, 118f));
        GUI.Label(new Rect(180f, 87f, 920f, 60f), "COURSE COMPLETE!", headline);
        GUI.matrix = baseMatrix;
        FrutigerAeroUI.DrawRounded(new Rect(145f, 163f, 426f, 380f), new Color(0.56f, 0.88f, 0.98f, 0.23f));
        DrawTrophyStage(elapsed, accent);
        Solid(new Rect(591f, 180f, 2f, 358f), new Color(0.12f, 0.55f, 0.73f, 0.16f));
        if (details) DrawRoundTable(); else DrawSummary(elapsed, accent);
        award.normal.textColor = Color.Lerp(accent, Ink, 0.38f);
        GUI.Label(new Rect(150f, 501f, 420f, 40f), AwardName(tier), award);
        GUI.Label(new Rect(150f, 540f, 420f, 27f), AwardMessage(tier), small);
        bool ready = elapsed >= 2.4f;
        GUI.enabled = previousEnabled && (!interactive || ready);
        if (DrawButton(new Rect(300f, 590f, 270f, 50f), "PLAY AGAIN", FrutigerAeroUI.Green, true, interactive))
            manager.ReplayRound();
        if (DrawButton(new Rect(585f, 590f, 210f, 50f), "MAIN MENU", FrutigerAeroUI.Blue, false, interactive))
            manager.ReturnToStartMenu();
        if (DrawButton(new Rect(810f, 590f, 170f, 50f), details ? "SUMMARY" : "SCORECARD", FrutigerAeroUI.Cyan, false, interactive))
            details = !details;
        GUI.enabled = previousEnabled;
        GUI.Label(new Rect(205f, 650f, 870f, 18f), ready ? "SPACE TO PLAY AGAIN   ·   YOUR RECORDS ARE SAVED" : "A LITTLE APPLAUSE. YOU EARNED THIS.", eyebrow);
        DrawConfetti(elapsed, accent);
        GUI.matrix = previousMatrix; GUI.color = previousColor; GUI.enabled = previousEnabled;
    }

    private bool DrawButton(Rect rect, string label, Color color, bool primary, bool interactive)
    {
        bool clicked = FrutigerAeroUI.DrawGlassButton(rect, label, button, color, primary,
            interactive && sound != null ? sound.HoverClip : null, interactive && sound != null ? sound.UiSource : null, 0.33f);
        if (!clicked || !interactive) return false;
        sound.PlayUiClick(); return true;
    }

    private void DrawTrophyStage(float elapsed, Color accent)
    {
        Vector2 center = new Vector2(358f, 330f);
        FrutigerAeroUI.DrawCircle(new Rect(190f, 170f, 335f, 335f), new Color(1f, 1f, 1f, 0.46f));
        FrutigerAeroUI.DrawCircle(new Rect(222f, 204f, 270f, 270f), new Color(accent.r, accent.g, accent.b, 0.18f));
        Matrix4x4 matrix = GUI.matrix;
        for (int i = 0; i < 14; i++)
        {
            GUIUtility.RotateAroundPivot(i * (360f / 14f) + elapsed * 4f, center);
            FrutigerAeroUI.DrawPill(new Rect(center.x - 5f, center.y - 164f, 10f, 120f), new Color(1f, 1f, 1f, 0.14f));
            GUI.matrix = matrix;
        }
        FrutigerAeroUI.DrawCircle(new Rect(250f, 464f, 215f, 25f), new Color(0.03f, 0.35f, 0.50f, 0.12f));
        float reveal = BackOut(Mathf.Clamp01((elapsed - 0.25f) / 0.85f));
        if (trophy != null && trophy.PreviewTexture != null && reveal > 0f)
        {
            Rect picture = new Rect(153f, 151f, 410f, 360f);
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Max(0.001f, reveal), new Vector2(358f, 435f));
            GUI.DrawTexture(picture, trophy.PreviewTexture, ScaleMode.ScaleToFit, true);
            GUI.matrix = matrix;
        }
        for (int i = 0; i < 9; i++)
        {
            float angle = i * 2.39996f;
            float twinkle = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(elapsed * 2.2f + i * 1.6f)), 5f) * Mathf.Clamp01((elapsed - 0.7f) * 2f);
            Vector2 point = center + new Vector2(Mathf.Cos(angle) * 157f, Mathf.Sin(angle) * 145f);
            Star(point, 3f + twinkle * 5f, new Color(1f, 1f, 1f, 0.22f + twinkle * 0.7f));
        }
    }

    private void DrawSummary(float elapsed, Color accent)
    {
        const float x = 621f, width = 478f;
        GUI.Label(new Rect(x, 180f, width, 24f), "TOTAL STROKES", eyebrow);
        float count = Smooth(Mathf.Clamp01((elapsed - 0.85f) / 1.25f));
        int shown = Mathf.RoundToInt(total * count);
        GUI.Label(new Rect(x, 201f, width, 106f), shown.ToString(), number);
        Color relativeColor = total <= parTotal ? new Color(0.11f, 0.58f, 0.40f) : new Color(0.12f, 0.45f, 0.69f);
        FrutigerAeroUI.DrawPill(new Rect(x + 82f, 309f, 314f, 37f), new Color(relativeColor.r, relativeColor.g, relativeColor.b, 0.12f));
        GUI.Label(new Rect(x + 82f, 309f, 314f, 37f), RelativeLabel(total - parTotal) + "   /   PAR " + parTotal, body);
        if (newRecord && elapsed >= 2.1f)
        {
            float burst = BackOut(Mathf.Clamp01((elapsed - 2.1f) / 0.35f));
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Max(0.01f, burst), new Vector2(x + width * 0.5f, 375f));
            FrutigerAeroUI.DrawPill(new Rect(x + 56f, 356f, width - 112f, 36f), new Color(1f, 0.82f, 0.22f, 0.86f));
            GUI.Label(new Rect(x + 56f, 356f, width - 112f, 36f), "NEW PERSONAL BEST!", button);
            GUI.matrix = matrix;
        }
        else
            GUI.Label(new Rect(x, 356f, width, 36f), "ALL " + pars.Length + " HOLES. ONE VERY HAPPY GOLF BALL.", eyebrow);
        Chip(new Rect(x + 10f, 409f, 218f, 71f), "BEST ROUND", bestRound > 0 ? bestRound + " STROKES" : "FIRST FINISH!");
        Chip(new Rect(x + 244f, 409f, 218f, 71f), "BONUS BUBBLES", availableBubbles > 0 ? bubbles + " / " + availableBubbles : bubbles + " COLLECTED");
        string goal = tier == 4 ? "THE DIAMOND CUP IS YOURS. TAKE A BOW." :
            tier == 3 ? "DIAMOND CUP: " + Mathf.Max(pars.Length, parTotal - Mathf.Max(2, Mathf.CeilToInt(pars.Length * 0.66f))) + " STROKES" :
            tier == 2 ? "GOLD CUP: " + parTotal + " STROKES OR BETTER" :
            "SILVER CUP: " + (parTotal + Mathf.Max(1, pars.Length)) + " STROKES OR BETTER";
        GUI.Label(new Rect(x, 496f, width, 40f), goal, small);
    }

    private void DrawRoundTable()
    {
        const float x = 621f, width = 478f;
        GUI.Label(new Rect(x, 174f, width, 28f), "YOUR VICTORY LAP", body);
        GUI.Label(new Rect(x + 12f, 208f, 242f, 24f), "HOLE", table);
        GUI.Label(new Rect(x + 267f, 208f, 62f, 24f), "PAR", small);
        GUI.Label(new Rect(x + 333f, 208f, 62f, 24f), "SHOTS", small);
        GUI.Label(new Rect(x + 400f, 208f, 65f, 24f), "+ / -", small);
        float rowHeight = Mathf.Min(28f, 260f / Mathf.Max(1, pars.Length));
        for (int i = 0; i < pars.Length; i++)
        {
            float y = 237f + i * rowHeight;
            int value = i < strokes.Length ? strokes[i] : 0;
            FrutigerAeroUI.DrawPill(new Rect(x, y, width, rowHeight - 1f),
                value <= pars[i] ? new Color(0.18f, 0.74f, 0.64f, 0.10f) : new Color(0.14f, 0.57f, 0.80f, i % 2 == 0 ? 0.08f : 0.025f));
            string label = (i + 1) + ". " + (i < names.Length ? names[i] : "Island " + (i + 1));
            GUI.Label(new Rect(x + 12f, y, 248f, rowHeight), label, table);
            GUI.Label(new Rect(x + 267f, y, 62f, rowHeight), pars[i].ToString(), small);
            GUI.Label(new Rect(x + 333f, y, 62f, rowHeight), value.ToString(), small);
            GUI.Label(new Rect(x + 400f, y, 65f, rowHeight), GolfScoreRecords.Relative(value - pars[i]), small);
        }
        GUI.Label(new Rect(x, 513f, width, 30f), total + " STROKES   /   " + RelativeLabel(total - parTotal) + "   /   " + bubbles + " BUBBLES", body);
    }

    private void Chip(Rect rect, string label, string value)
    {
        FrutigerAeroUI.DrawGlassPanel(rect, new Color(0.39f, 0.81f, 0.95f), new Color(0.97f, 1f, 1f, 0.64f), 0.04f);
        GUI.Label(new Rect(rect.x, rect.y + 9f, rect.width, 21f), label, eyebrow);
        GUI.Label(new Rect(rect.x, rect.y + 32f, rect.width, 29f), value, body);
    }

    private static void DrawConfetti(float elapsed, Color accent)
    {
        if (elapsed < 0.65f) return;
        Matrix4x4 matrix = GUI.matrix;
        Color[] colors = { accent, new Color(0.18f, 0.79f, 1f), new Color(0.55f, 0.96f, 0.42f), new Color(1f, 0.63f, 0.75f), Color.white };
        for (int i = 0; i < 104; i++)
        {
            float seed = Mathf.Repeat(i * 0.618034f, 1f);
            float t = elapsed - 0.65f - seed * 0.55f;
            if (t < 0f || t > 4.5f) continue;
            bool left = i % 2 == 0;
            float px = (left ? 40f : 1240f) + (left ? 1f : -1f) * (160f + seed * 350f) * t;
            float py = 590f - (310f + (i % 7) * 27f) * t + 115f * t * t;
            Color color = colors[i % colors.Length]; color.a = Mathf.Clamp01((4.5f - t) / 0.8f);
            GUIUtility.RotateAroundPivot(i * 37f + t * (60f + i % 90), new Vector2(px, py));
            FrutigerAeroUI.DrawPill(new Rect(px - 3f, py - 6f, 6f + i % 3, 10f + i % 5), color);
            GUI.matrix = matrix;
        }
    }

    private static void Star(Vector2 point, float size, Color color)
    {
        FrutigerAeroUI.DrawPill(new Rect(point.x - size, point.y - size * 0.15f, size * 2f, size * 0.3f), color);
        FrutigerAeroUI.DrawPill(new Rect(point.x - size * 0.15f, point.y - size, size * 0.3f, size * 2f), color);
    }
    private static void Solid(Rect rect, Color color)
    {
        Color before = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = before;
    }
    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float BackOut(float t) { t -= 1f; return 1f + 2.70158f * t * t * t + 1.70158f * t * t; }
    private static string RelativeLabel(int delta) => delta == 0 ? "EVEN PAR" : delta < 0 ? (-delta) + " UNDER PAR" : delta + " OVER PAR";
    public static string AwardName(int value) => value == 4 ? "DIAMOND CUP" : value == 3 ? "GOLD CUP" : value == 2 ? "SILVER CUP" : "BRONZE CUP";
    private static string AwardMessage(int value) => value == 4 ? "Ridiculous golf. Glorious trophy." : value == 3 ? "Blue skies. Golden finish." : value == 2 ? "A smooth finish. A shiny souvenir." : "You conquered the course. Take your trophy.";
    private static Color AwardColor(int value) => value == 4 ? new Color(0.32f, 0.83f, 1f) : value == 3 ? new Color(0.99f, 0.73f, 0.14f) : value == 2 ? new Color(0.59f, 0.77f, 0.87f) : new Color(0.82f, 0.47f, 0.24f);

#if UNITY_EDITOR
    public void SetPreviewScores(int[] previewStrokes, int[] previewPars, string[] previewNames, int collected, int available, bool personalBest)
    {
        SetData(previewStrokes, previewPars, previewNames, collected, available, Sum(previewStrokes), personalBest);
        trophy = GetComponent<GolfVictoryTrophy>(); trophy.SetAward(tier);
    }
    public void SetPreviewDetails(bool show) { details = show; }
    public void TickPreview(float elapsed, float dt) { if (trophy != null) trophy.Tick(elapsed, dt); }
    private static int Sum(int[] values) { int result = 0; foreach (int value in values) result += value; return result; }
#endif
    private void OnDisable() => Hide();
    private void OnDestroy() => Hide();
}


