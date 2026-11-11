using UnityEngine;

/// <summary>
/// Marks a trigger volume as a hazard (sea, pond, sky drop). When the ball enters it,
/// <see cref="AeroBallSafety"/> returns the ball to its last resting spot with a penalty.
/// Water hazards let the ball float at the surface for a moment first.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AeroHazard : MonoBehaviour
{
    public string label = "Splash!";
    [Min(0)] public int penaltyStrokes = 1;
    [Tooltip("Seconds before the ball is returned (water hazards float the ball during this time).")]
    [Min(0f)] public float resetDelay = 1.8f;
    [Tooltip("Ball bobs at the water surface instead of sinking.")]
    public bool floats = true;
    [Tooltip("Water surface height above the top of this trigger.")]
    public float surfaceAboveTrigger = 0.15f;

    public float WaterSurfaceY => GetComponent<Collider>().bounds.max.y + surfaceAboveTrigger;

    private void Reset() { GetComponent<Collider>().isTrigger = true; }
    private void Awake() { GetComponent<Collider>().isTrigger = true; }
}
