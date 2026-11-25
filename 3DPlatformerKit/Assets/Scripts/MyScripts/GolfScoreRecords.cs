using System;
using System.Globalization;
using UnityEngine;

[Serializable]
public sealed class GolfHoleRecord
{
    // Zero means no completed attempt has been saved yet.
    public int bestStrokes;
    public int bestBubbles;
    public int bestMedal;
}

public struct GolfHoleRecordResult
{
    public bool newBestScore;
    public bool newBestBubbles;
    public bool newMedal;
    public int previousBest;
}

/// <summary>
/// Local records for one course. Bubble collections and medals are independent
/// achievements; they never subtract strokes from an actual golf score.
/// Only completed holes and completed rounds should be submitted by the course.
/// </summary>
public sealed class GolfScoreRecords
{
    private const int SchemaVersion = 1;
    private const int MaximumHoles = 256;
    private const int MaximumStrokesPerHole = 999;
    private const int MaximumBubblesPerHole = 999;

    [Serializable]
    private sealed class SavedRecords
    {
        public int schemaVersion;
        public string courseId;
        public int holeCount;
        public GolfHoleRecord[] holes;
        public int bestRound;
        public int bestRoundBubbles;
    }

    private readonly string courseId;
    private readonly string storageKey;

    public GolfHoleRecord[] Holes { get; }
    public int BestRound { get; private set; }
    public int BestRoundBubbles { get; private set; }

    public GolfScoreRecords(string courseId, int holeCount)
    {
        this.courseId = NormalizeCourseId(courseId);
        storageKey = StorageKeyFor(this.courseId);
        Holes = new GolfHoleRecord[Mathf.Clamp(holeCount, 0, MaximumHoles)];
        for (int i = 0; i < Holes.Length; i++)
            Holes[i] = new GolfHoleRecord();
        Load();
    }

    public GolfHoleRecordResult RecordHole(int index, int strokes, int par, int collected, int available)
    {
        var result = new GolfHoleRecordResult();
        if (index < 0 || index >= Holes.Length)
            return result;

        GolfHoleRecord record = Holes[index];
        result.previousBest = record.bestStrokes;
        if (strokes < 1 || strokes > MaximumStrokesPerHole || par < 1 || par > 99
            || available < 0 || available > MaximumBubblesPerHole || collected < 0 || collected > available)
            return result;

        result.newBestScore = record.bestStrokes == 0 || strokes < record.bestStrokes;
        result.newBestBubbles = collected > record.bestBubbles;
        int medal = Medal(strokes, par);
        result.newMedal = medal > record.bestMedal;

        if (result.newBestScore) record.bestStrokes = strokes;
        if (result.newBestBubbles) record.bestBubbles = collected;
        if (result.newMedal) record.bestMedal = medal;
        if (result.newBestScore || result.newBestBubbles || result.newMedal)
            Save();
        return result;
    }

    /// <returns>True only when the round's stroke record improves (including its first completion).</returns>
    public bool RecordRound(int strokes, int bubbles)
    {
        if (Holes.Length == 0 || strokes < Holes.Length || strokes > MaximumStrokesPerHole * Holes.Length
            || bubbles < 0 || bubbles > MaximumBubblesPerHole * Holes.Length)
            return false;

        bool newScore = BestRound == 0 || strokes < BestRound;
        bool newBubbles = bubbles > BestRoundBubbles;
        if (newScore) BestRound = strokes;
        if (newBubbles) BestRoundBubbles = bubbles;
        if (newScore || newBubbles)
            Save();
        return newScore;
    }

    public static string Rating(int strokes, int par)
    {
        if (strokes < 1 || par < 1)
            return "HOLE COMPLETE";
        if (strokes == 1)
            return "HOLE IN ONE!";

        long relative = (long)strokes - par;
        if (relative <= -3) return "ALBATROSS";
        if (relative == -2) return "EAGLE";
        if (relative == -1) return "BIRDIE";
        if (relative == 0) return "PAR";
        if (relative == 1) return "BOGEY";
        if (relative == 2) return "DOUBLE BOGEY";
        return "+" + relative.ToString(CultureInfo.InvariantCulture) + " OVER PAR";
    }

    /// <summary>Gold: under par or an ace. Silver: par. Bronze: one over par.</summary>
    public static int Medal(int strokes, int par)
    {
        if (strokes < 1 || par < 1) return 0;
        if (strokes == 1 || strokes < par) return 3;
        if (strokes == par) return 2;
        return (long)strokes == (long)par + 1 ? 1 : 0;
    }

    public static string Relative(int delta)
    {
        if (delta == 0) return "E";
        return delta > 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture) : delta.ToString(CultureInfo.InvariantCulture);
    }

    public static string StorageKeyFor(string courseId)
    {
        // A fixed UTF-16 FNV-1a hash is stable across processes, platforms and
        // runtime versions; string.GetHashCode is deliberately not used.
        string value = NormalizeCourseId(courseId);
        ulong hash = 14695981039346656037UL;
        unchecked
        {
            for (int i = 0; i < value.Length; i++)
            {
                hash = (hash ^ (byte)value[i]) * 1099511628211UL;
                hash = (hash ^ (byte)(value[i] >> 8)) * 1099511628211UL;
            }
        }
        return "GolfIsHard.ScoreRecords.v1." + hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static string NormalizeCourseId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "default-course" : value.Trim();
    }

    private void Load()
    {
        try
        {
            string json = PlayerPrefs.GetString(storageKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            SavedRecords saved = JsonUtility.FromJson<SavedRecords>(json);
            if (saved == null || saved.schemaVersion != SchemaVersion || saved.courseId != courseId || saved.holes == null
                || saved.holeCount < 0 || saved.holeCount > MaximumHoles || saved.holes.Length != saved.holeCount)
                return;

            int count = Math.Min(Holes.Length, saved.holes.Length);
            for (int i = 0; i < count; i++)
            {
                GolfHoleRecord record = saved.holes[i];
                if (record == null) continue;
                Holes[i].bestStrokes = record.bestStrokes >= 1 && record.bestStrokes <= MaximumStrokesPerHole ? record.bestStrokes : 0;
                Holes[i].bestBubbles = Mathf.Clamp(record.bestBubbles, 0, MaximumBubblesPerHole);
                Holes[i].bestMedal = Mathf.Clamp(record.bestMedal, 0, 3);
                // A collectible or medal can only have been banked by finishing a hole.
                if (Holes[i].bestStrokes == 0)
                    Holes[i].bestBubbles = Holes[i].bestMedal = 0;
            }
            // Existing individual records remain useful if holes are appended;
            // a round record for a different length would be misleading.
            if (saved.holeCount == Holes.Length && Holes.Length > 0)
            {
                BestRound = saved.bestRound >= Holes.Length && saved.bestRound <= MaximumStrokesPerHole * Holes.Length ? saved.bestRound : 0;
                BestRoundBubbles = Mathf.Clamp(saved.bestRoundBubbles, 0, MaximumBubblesPerHole * Holes.Length);
                if (BestRound == 0) BestRoundBubbles = 0;
            }
        }
        catch (Exception error)
        {
            // A damaged local preference should never block playing a course.
            Debug.LogWarning("Golf score records could not be loaded: " + error.Message);
        }
    }

    private void Save()
    {
        var saved = new SavedRecords
        {
            schemaVersion = SchemaVersion,
            courseId = courseId,
            holeCount = Holes.Length,
            holes = Holes,
            bestRound = BestRound,
            bestRoundBubbles = BestRoundBubbles
        };
        try
        {
            PlayerPrefs.SetString(storageKey, JsonUtility.ToJson(saved));
            PlayerPrefs.Save();
        }
        catch (Exception error)
        {
            Debug.LogWarning("Golf score records could not be saved: " + error.Message);
        }
    }
}
