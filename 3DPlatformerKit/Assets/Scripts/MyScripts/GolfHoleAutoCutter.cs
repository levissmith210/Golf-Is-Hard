using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class GolfHoleAutoCutter : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float cutRadius = 1.11f;
    [SerializeField] private bool cutBoxSurfaceOnPlacement = true;
    [SerializeField, HideInInspector] private bool hasCutSurface;
    [System.NonSerialized] private string lastCutMessage;

    public float CutRadius => cutRadius;
    public bool CutBoxSurfaceOnPlacement => cutBoxSurfaceOnPlacement;
    public bool HasCutSurface => hasCutSurface;
    public string LastCutMessage => lastCutMessage;

    public void SetCutMessage(string message)
    {
        lastCutMessage = message;
    }

    public void MarkSurfaceCut()
    {
        hasCutSurface = true;
        lastCutMessage = null;
    }
}

