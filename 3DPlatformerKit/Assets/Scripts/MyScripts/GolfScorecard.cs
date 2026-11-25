using UnityEngine;

/// <summary>Round scoring, local achievement records, and the shared score presentation.</summary>
[DisallowMultipleComponent]
public sealed class GolfScorecard : MonoBehaviour
{
    private GolfCourseManager manager;
    private GolfVictoryScreen victory;
    private GolfScoreRecords records;
    private int[] availableBubbles;
    private int resultIndex;
    private float resultAge;
    private GolfHoleRecordResult recordResult;
    private bool newRoundRecord;
    private AudioSource audioSource;
    private AudioClip resultSound, greatResultSound;
    private GUIStyle small, body, title, button;
    private float uiScale;

    public GolfScoreRecords Records => records;
    public int[] RoundStrokes { get; private set; }
    public int[] RoundBubbles { get; private set; }
    public string[] HoleNames { get; private set; }
    public int[] Pars { get; private set; }
    public string CourseId { get; private set; }
    public bool ShowingResult { get; private set; }
    public bool ReadyForAction => ShowingResult && (victory != null && victory.Visible ? victory.ReadyForAction : resultAge >= 0.65f);
    public int RoundTotal => Sum(RoundStrokes);
    public int BubbleTotal => Sum(RoundBubbles);
    public int AvailableBubbleTotal => Sum(availableBubbles);
    public int CompletedCount
    {
        get { int n = 0; if (RoundStrokes != null) foreach (int value in RoundStrokes) if (value > 0) n++; return n; }
    }
    public string MenuSummary
    {
        get
        {
            int gold = 0, bubbles = 0;
            if (records == null) return "BUILD YOUR FIRST COURSE RECORD";
            foreach (GolfHoleRecord record in records.Holes)
            {
                if (record.bestMedal == 3) gold++;
                bubbles += record.bestBubbles;
            }
            return $"BEST {(records.BestRound > 0 ? records.BestRound.ToString() : "—")}   ·   GOLD {gold}/{Pars.Length}   ·   BUBBLES {bubbles}/{Pars.Length * 3}";
        }
    }

    public void Configure(GolfCourseManager course)
    {
        manager = course;
        int count = manager.TotalHoleCount;
        Pars = new int[count];
        HoleNames = new string[count];
        for (int i = 0; i < count; i++)
        {
            Pars[i] = manager.GetHolePar(i);
            HoleNames[i] = manager.GetHoleTitle(i);
        }
        CourseId = gameObject.scene.name + ".AeroTour.v1";
        records = new GolfScoreRecords(CourseId, count);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        resultSound = MakeStinger(false);
        greatResultSound = MakeStinger(true);
        victory = GetComponent<GolfVictoryScreen>();
        if (victory == null) victory = gameObject.AddComponent<GolfVictoryScreen>();
        victory.Configure(manager, this);
        ResetRound();
    }

    public void ResetRound()
    {
        int count = Pars != null ? Pars.Length : 0;
        RoundStrokes = new int[count];
        RoundBubbles = new int[count];
        availableBubbles = new int[count];
        HideResults();
    }

    public void BeginHole(int index)
    {
        HideResults();
        if (index < 0 || index >= RoundStrokes.Length) return;
        RoundStrokes[index] = 0;
        RoundBubbles[index] = 0;
        availableBubbles[index] = 0;
    }

    public void HideResults()
    {
        if (victory != null) victory.Hide();
        ShowingResult = false;
        resultAge = 0f;
        FrutigerAeroUI.ResetGlassButtonHover();
    }

    public void CompleteHole(int index, int strokes, int bubbles, int available)
    {
        if (ShowingResult || index < 0 || index >= RoundStrokes.Length || strokes < 1) return;
        resultIndex = index;
        RoundStrokes[index] = strokes;
        RoundBubbles[index] = Mathf.Clamp(bubbles, 0, available);
        availableBubbles[index] = available;
        recordResult = records.RecordHole(index, strokes, Pars[index], RoundBubbles[index], available);
        newRoundRecord = CompletedCount == RoundStrokes.Length && records.RecordRound(RoundTotal, BubbleTotal);
        ShowingResult = true;
        resultAge = 0f;
        FrutigerAeroUI.ResetGlassButtonHover();
        if (manager.IsCourseComplete && RoundStrokes.Length > 0 && CompletedCount == RoundStrokes.Length && victory != null)
        {
            victory.Show(newRoundRecord);
            return;
        }
        GameSettings.PlaySfx(audioSource, strokes <= Pars[index] ? greatResultSound : resultSound, 0.65f);
    }

    private void Update()
    {
        if (ShowingResult && Time.timeScale > 0f)
            resultAge += Time.unscaledDeltaTime;
    }

    private void OnGUI()
    {
        if (manager == null || records == null || Time.timeScale <= 0f) return;
        if (ShowingResult && victory != null && victory.Visible) return;
        uiScale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        uiScale = Mathf.Max(0.35f, uiScale);
        EnsureStyles();
        if (ShowingResult)
        {
            int depth = GUI.depth;
            GUI.depth = -40;
            DrawResults();
            GUI.depth = depth;
        }
        else if (manager.IsPlayingCourse)
            DrawHud();
    }

    private void EnsureStyles()
    {
        if (body == null)
        {
            small = FrutigerAeroUI.CreateStyle(13, FrutigerAeroUI.Ink);
            body = FrutigerAeroUI.CreateStyle(18, FrutigerAeroUI.Ink);
            title = FrutigerAeroUI.CreateStyle(48, FrutigerAeroUI.Ink);
            button = FrutigerAeroUI.CreateStyle(18, FrutigerAeroUI.Ink);
        }
        small.fontSize = Mathf.Max(8, Mathf.RoundToInt(13f * uiScale));
        body.fontSize = Mathf.Max(9, Mathf.RoundToInt(18f * uiScale));
        title.fontSize = Mathf.Max(18, Mathf.RoundToInt(48f * uiScale));
        button.fontSize = Mathf.Max(10, Mathf.RoundToInt(18f * uiScale));
    }

    private void DrawHud()
    {
        int index = manager.ActiveHoleNumber - 1;
        if (index < 0 || index >= Pars.Length || manager.Ball == null) return;
        float s = uiScale;
        Rect r = new Rect(22f * s, 20f * s, 360f * s, 83f * s);
        FrutigerAeroUI.DrawGlassPanel(r, FrutigerAeroUI.Cyan, new Color(0.85f, 0.98f, 1f, 0.91f));
        GUI.Label(new Rect(r.x + 12f * s, r.y + 7f * s, r.width - 24f * s, 26f * s),
            $"{index + 1}/{Pars.Length}  ·  {HoleNames[index]}", body);
        string best = records.Holes[index].bestStrokes > 0 ? records.Holes[index].bestStrokes.ToString() : "—";
        GUI.Label(new Rect(r.x + 10f * s, r.y + 40f * s, r.width - 20f * s, 25f * s),
            $"PAR {Pars[index]}    STROKES {manager.Ball.StrokeCount}    BEST {best}", small);

        Rect bubbles = new Rect(Screen.width - 250f * s, 20f * s, 228f * s, 83f * s);
        FrutigerAeroUI.DrawGlassPanel(bubbles, Gold, new Color(0.9f, 0.98f, 1f, 0.9f));
        GUI.Label(new Rect(bubbles.x, bubbles.y + 7f * s, bubbles.width, 28f * s),
            $"BUBBLES {manager.RiskBubbles.Collected}/{manager.RiskBubbles.Available}", body);
        GUI.Label(new Rect(bubbles.x, bubbles.y + 41f * s, bubbles.width, 23f * s),
            $"GOLD: {Mathf.Max(1, Pars[index] - 1)} STROKES OR LESS", small);
    }

    private void DrawResults()
    {
        float s = uiScale;
        bool finished = manager.IsCourseComplete;
        int strokes = RoundStrokes[resultIndex], par = Pars[resultIndex];
        int medal = GolfScoreRecords.Medal(strokes, par);
        Color accent = medal == 3 ? Gold : medal == 2 ? FrutigerAeroUI.Cyan : medal == 1 ? Bronze : FrutigerAeroUI.Blue;
        Rect panel = new Rect((Screen.width - 830f * s) * 0.5f, (Screen.height - 650f * s) * 0.5f, 830f * s, 650f * s);
        DrawSolid(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.16f, 0.25f, 0.33f));
        FrutigerAeroUI.DrawGlassPanel(panel, accent, new Color(0.87f, 0.98f, 1f, 0.97f), 0.25f);
        float x = panel.x, y = panel.y, w = panel.width;
        GUI.Label(new Rect(x, y + 15f * s, w, 25f * s),
            finished ? "THE NINE-HOLE TOUR  ·  ROUND COMPLETE" : $"HOLE {resultIndex + 1}  ·  {HoleNames[resultIndex].ToUpperInvariant()}", small);
        float pop = 1f + Mathf.Sin(resultAge * 18f) * Mathf.Exp(-resultAge * 6f) * 0.09f;
        Matrix4x4 matrix = GUI.matrix;
        Rect headline = new Rect(x + 25f * s, y + 43f * s, w - 50f * s, 63f * s);
        GUIUtility.ScaleAroundPivot(Vector2.one * pop, headline.center);
        GUI.Label(headline, GolfScoreRecords.Rating(strokes, par), title);
        GUI.matrix = matrix;
        GUI.Label(new Rect(x, y + 108f * s, w, 28f * s),
            $"{strokes} {(strokes == 1 ? "STROKE" : "STROKES")}  ·  PAR {par}  ·  {GolfScoreRecords.Relative(strokes - par)}", body);
        if (medal > 0)
        {
            DrawMedallion(new Vector2(x + 72f * s, y + 96f * s), 25f * s, medal);
            DrawMedallion(new Vector2(x + w - 72f * s, y + 96f * s), 25f * s, medal);
        }
        string recordText = newRoundRecord ? "NEW ROUND RECORD!" : recordResult.newBestScore
            ? (recordResult.previousBest > 0 ? $"NEW PERSONAL BEST!  {recordResult.previousBest} → {strokes}" : "FIRST RECORD SET!")
            : $"PERSONAL BEST: {records.Holes[resultIndex].bestStrokes}";
        GUI.Label(new Rect(x, y + 144f * s, w, 28f * s),
            $"{recordText}     ·     {MedalName(medal)}     ·     BUBBLES {RoundBubbles[resultIndex]}/{availableBubbles[resultIndex]}", small);

        float tableX = x + 28f * s, tableY = y + 190f * s;
        float tableW = w - 56f * s;
        float[] cols = { 0f, 0.36f, 0.45f, 0.55f, 0.65f, 0.76f, 0.88f, 1f };
        string[] headers = { "HOLE", "PAR", "ROUND", "+/−", "BEST", "MEDAL", "BUBBLES" };
        for (int c = 0; c < headers.Length; c++)
            Cell(tableX, tableY, tableW, cols[c], cols[c + 1], headers[c], small, 24f * s);
        float rowH = Mathf.Min(27f, 243f / Mathf.Max(1, Pars.Length)) * s;
        for (int i = 0; i < Pars.Length; i++)
        {
            float rowY = tableY + 28f * s + rowH * i;
            if (i == resultIndex || i % 2 == 0)
                FrutigerAeroUI.DrawPill(new Rect(tableX, rowY, tableW, rowH - 1f * s),
                    i == resultIndex ? new Color(accent.r, accent.g, accent.b, 0.22f) : new Color(0.2f, 0.7f, 0.85f, 0.07f));
            var record = records.Holes[i];
            bool played = RoundStrokes[i] > 0;
            string[] values = { $"{i + 1}. {HoleNames[i]}", Pars[i].ToString(), played ? RoundStrokes[i].ToString() : "—",
                played ? GolfScoreRecords.Relative(RoundStrokes[i] - Pars[i]) : "—",
                record.bestStrokes > 0 ? record.bestStrokes.ToString() : "—", MedalName(record.bestMedal),
                $"{record.bestBubbles}/3" };
            for (int c = 0; c < values.Length; c++)
                Cell(tableX, rowY, tableW, cols[c], cols[c + 1], values[c], small, rowH);
        }
        int completedPar = 0;
        for (int i = 0; i < Pars.Length; i++) if (RoundStrokes[i] > 0) completedPar += Pars[i];
        string totalDetail = finished ? $"BEST ROUND {records.BestRound}" : $"{CompletedCount}/{Pars.Length} HOLES";
        GUI.Label(new Rect(x + 25f * s, y + 477f * s, w - 50f * s, 29f * s),
            $"ROUND {RoundTotal}  ({GolfScoreRecords.Relative(RoundTotal - completedPar)})     ·     {totalDetail}     ·     {BubbleTotal} BUBBLES", body);
        GUI.Label(new Rect(x + 20f * s, y + 512f * s, w - 40f * s, 23f * s),
            "GOLD: UNDER PAR   ·   SILVER: PAR   ·   BRONZE: +1   ·   BUBBLE COLUMN SHOWS YOUR BEST", small);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && ReadyForAction;
        Rect primary = new Rect(x + 208f * s, y + 555f * s, 234f * s, 48f * s);
        if (FrutigerAeroUI.DrawGlassButton(primary, finished ? "PLAY AGAIN" : "NEXT HOLE", button, FrutigerAeroUI.Green, true))
            manager.ContinueAfterScorecard();
        Rect retry = new Rect(x + 457f * s, y + 555f * s, 165f * s, 48f * s);
        if (FrutigerAeroUI.DrawGlassButton(retry, "RETRY HOLE", button, FrutigerAeroUI.Blue, false))
            manager.RetryScoredHole();
        GUI.enabled = wasEnabled;
        GUI.Label(new Rect(x + 20f * s, y + 615f * s, w - 40f * s, 22f * s),
            $"SPACE: {(finished ? "PLAY AGAIN" : "NEXT HOLE")}     ·     RECORDS SAVED ON THIS DEVICE", small);
        if (finished || medal >= 2 || strokes == 1) DrawConfetti(accent);
    }

    private static void Cell(float x, float y, float width, float start, float end, string text, GUIStyle style, float height)
    {
        TextAnchor alignment = style.alignment;
        if (start == 0f) style.alignment = TextAnchor.MiddleLeft;
        GUI.Label(new Rect(x + width * start + 7f, y, width * (end - start) - 14f, height), text, style);
        style.alignment = alignment;
    }

    private void DrawMedallion(Vector2 center, float radius, int medal)
    {
        Color color = medal == 3 ? Gold : medal == 2 ? new Color(0.65f, 0.83f, 0.93f) : Bronze;
        FrutigerAeroUI.DrawPill(new Rect(center.x - radius * 0.65f, center.y + radius * 0.5f, radius * 0.5f, radius), FrutigerAeroUI.Blue);
        FrutigerAeroUI.DrawPill(new Rect(center.x + radius * 0.15f, center.y + radius * 0.5f, radius * 0.5f, radius), FrutigerAeroUI.Cyan);
        FrutigerAeroUI.DrawCircle(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), color);
        FrutigerAeroUI.DrawCircle(new Rect(center.x - radius * 0.8f, center.y - radius * 0.8f, radius * 1.6f, radius * 1.6f), Color.Lerp(color, Color.white, 0.5f));
        GUI.Label(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), medal == 3 ? "G" : medal == 2 ? "S" : "B", body);
    }

    private void DrawConfetti(Color accent)
    {
        if (resultAge > 2.6f) return;
        Matrix4x4 matrix = GUI.matrix;
        for (int i = 0; i < 55; i++)
        {
            float seed = Mathf.Repeat(i * 0.618034f, 1f);
            float t = Mathf.Max(0f, resultAge - seed * 0.3f);
            float px = Screen.width * seed + Mathf.Sin(i * 2.1f + t * 3f) * 45f * uiScale;
            float py = -30f * uiScale + (180f + i % 7 * 24f) * t * uiScale;
            Rect piece = new Rect(px, py, 8f * uiScale, (i % 2 == 0 ? 5f : 11f) * uiScale);
            GUIUtility.RotateAroundPivot(i * 39f + t * 180f, piece.center);
            Color color = i % 3 == 0 ? accent : i % 3 == 1 ? FrutigerAeroUI.Aqua : new Color(1f, 0.76f, 0.3f);
            color.a = Mathf.Clamp01((2.6f - resultAge) / 0.5f);
            DrawSolid(piece, color);
            GUI.matrix = matrix;
        }
    }

    private static readonly Color Gold = new Color(1f, 0.79f, 0.2f);
    private static readonly Color Bronze = new Color(0.83f, 0.48f, 0.24f);
    private static string MedalName(int medal) => medal == 3 ? "GOLD" : medal == 2 ? "SILVER" : medal == 1 ? "BRONZE" : "—";
    private static int Sum(int[] values) { int sum = 0; if (values != null) foreach (int value in values) sum += value; return sum; }
    private static void DrawSolid(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static AudioClip MakeStinger(bool great)
    {
        const int rate = 44100;
        var samples = new float[(int)(rate * (great ? 1.1f : 0.65f))];
        float[] notes = great ? new[] { 523.25f, 659.25f, 783.99f, 1046.5f } : new[] { 523.25f, 659.25f };
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float value = 0f;
            for (int n = 0; n < notes.Length; n++)
            {
                float age = t - n * 0.12f;
                if (age < 0f) continue;
                float envelope = (1f - Mathf.Exp(-age * 150f)) * Mathf.Exp(-age * 7f);
                value += (Mathf.Sin(age * notes[n] * 2f * Mathf.PI) + 0.15f * Mathf.Sin(age * notes[n] * 4f * Mathf.PI)) * envelope * 0.22f;
            }
            samples[i] = value * Mathf.Clamp01((samples.Length / (float)rate - t) / 0.04f);
        }
        var clip = AudioClip.Create(great ? "Under par fanfare" : "Hole result chime", samples.Length, 1, rate, false);
        clip.hideFlags = HideFlags.HideAndDontSave;
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (resultSound != null) Destroy(resultSound);
        if (greatResultSound != null) Destroy(greatResultSound);
    }
}


