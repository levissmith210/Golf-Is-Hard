using UnityEngine;

/// <summary>
/// Slow hovering orbit used by the course start menu.
/// </summary>
public class CourseMenuCamera : MonoBehaviour
{
    private Vector3 targetPoint;
    private float orbitRadius;
    private float height;
    private float orbitSpeed;
    private float orbitAngle;
    private bool configured;
    private Camera targetCamera;
    private float originalFarClipPlane;
    private bool originalOcclusionCulling;
    private bool cameraSettingsCaptured;

    public void Configure(Vector3 target, float radius, float cameraHeight, float speed)
    {
        enabled = true;
        targetPoint = target;
        orbitRadius = Mathf.Max(8f, radius);
        height = Mathf.Max(5f, cameraHeight);
        orbitSpeed = speed;
        ApplyMenuCameraSettings();
        orbitAngle = 0f;
        configured = true;
        UpdateCameraPose(0f);
    }

    public void StopOrbit()
    {
        configured = false;
        RestoreCameraSettings();
        enabled = false;
    }

    private void ApplyMenuCameraSettings()
    {
        targetCamera = GetComponent<Camera>();
        if (targetCamera == null)
            return;

        if (!cameraSettingsCaptured)
        {
            originalFarClipPlane = targetCamera.farClipPlane;
            originalOcclusionCulling = targetCamera.useOcclusionCulling;
            cameraSettingsCaptured = true;
        }

        // The title orbit frames the whole course, so allow distant terrain to stay visible.
        float neededFarClip = Mathf.Max(10000f, (orbitRadius + height) * 2f);
        targetCamera.farClipPlane = Mathf.Max(targetCamera.farClipPlane, neededFarClip);
        targetCamera.useOcclusionCulling = false;
    }

    private void RestoreCameraSettings()
    {
        if (!cameraSettingsCaptured)
            return;

        if (targetCamera != null)
        {
            targetCamera.farClipPlane = originalFarClipPlane;
            targetCamera.useOcclusionCulling = originalOcclusionCulling;
        }

        cameraSettingsCaptured = false;
    }

    private void OnDisable()
    {
        configured = false;
        RestoreCameraSettings();
    }
    private void LateUpdate()
    {
        if (!configured)
            return;

        orbitAngle += Time.unscaledDeltaTime * orbitSpeed;
        UpdateCameraPose(Time.unscaledTime);
    }

    private void UpdateCameraPose(float time)
    {
        float gentleDrift = Mathf.Sin(time * 0.19f) * 2.5f;
        float softBob = Mathf.Sin(time * 0.34f) * 1.8f;
        Vector3 offset = new Vector3(
            Mathf.Sin(orbitAngle) * (orbitRadius + gentleDrift),
            height + softBob,
            Mathf.Cos(orbitAngle) * (orbitRadius + gentleDrift));

        transform.position = targetPoint + offset;
        transform.rotation = Quaternion.LookRotation(targetPoint - transform.position, Vector3.up);
    }
}

