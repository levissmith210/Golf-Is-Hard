using UnityEngine;

/// <summary>What the ball is sitting on. Drives shot power, roll friction and landing bounce.</summary>
public enum AeroSurfaceType : byte
{
    Default = 0, // unknown ground: uses the ball's own rolling drag
    Fairway = 1,
    Rough = 2,
    Green = 3,
    Sand = 4,
    Tee = 5,
    Hard = 6,    // glass decks, pearl ramps, pipes, bumpers
}

/// <summary>Gives a collider a fixed surface type (bridges, floating greens, ramps...).</summary>
[DisallowMultipleComponent]
public class AeroSurface : MonoBehaviour
{
    public AeroSurfaceType type = AeroSurfaceType.Fairway;

    /// <summary>Surface type at a world point on the given collider.</summary>
    public static AeroSurfaceType Lookup(Collider collider, Vector3 worldPoint)
    {
        if (collider == null) return AeroSurfaceType.Default;
        var fixedSurface = collider.GetComponentInParent<AeroSurface>();
        if (fixedSurface != null) return fixedSurface.type;
        var map = collider.GetComponentInParent<AeroSurfaceMap>();
        if (map != null) return map.Sample(worldPoint);
        return AeroSurfaceType.Default;
    }

    public static string Label(AeroSurfaceType t)
    {
        switch (t)
        {
            case AeroSurfaceType.Fairway: return "FAIRWAY";
            case AeroSurfaceType.Rough: return "ROUGH";
            case AeroSurfaceType.Green: return "GREEN";
            case AeroSurfaceType.Sand: return "SAND";
            case AeroSurfaceType.Tee: return "TEE";
            case AeroSurfaceType.Hard: return "HARD";
            default: return "";
        }
    }
}
