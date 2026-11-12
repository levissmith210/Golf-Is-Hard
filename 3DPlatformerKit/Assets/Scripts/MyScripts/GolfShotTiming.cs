using UnityEngine;

/// <summary>Deterministic timing rules shared by the meter and the actual shot.</summary>
public static class GolfShotTiming
{
    public const float PerfectStart = 0.92f;
    public const float PerfectEnd = 0.98f;

    public static float Power(float elapsed, float secondsToFull)
    {
        return Mathf.PingPong(Mathf.Max(0f, elapsed) / Mathf.Max(0.1f, secondsToFull), 1f);
    }

    public static bool IsPerfect(float power)
    {
        return power >= PerfectStart && power <= PerfectEnd;
    }

    public static float Force(float power, float minimum, float maximum, float perfectBonus)
    {
        // Keep the very bottom of the meter useful for feathering short putts.
        float force = Mathf.Lerp(Mathf.Min(minimum, 0.6f), maximum, Mathf.Clamp01(power));
        return force * (IsPerfect(power) ? 1f + Mathf.Max(0f, perfectBonus) : 1f);
    }
}
