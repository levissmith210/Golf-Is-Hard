using UnityEngine;

/// <summary>
/// Marks a trigger volume as a hazard (sea, pond, sky drop). When the ball enters it,
/// <see cref="AeroBallSafety"/> returns the ball to its last resting spot with a penalty.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AeroHazard : MonoBehaviour
{
    public string label = "Splash!";
    [Min(0)] public int penaltyStrokes = 1;
    [Min(0f)] public float resetDelay = 0.9f;

    private void Reset() { GetComponent<Collider>().isTrigger = true; }
    private void Awake() { GetComponent<Collider>().isTrigger = true; }
}
