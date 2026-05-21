using UnityEngine;

/// <summary>
/// Keeps the stacked tutorial overlay camera's projection aligned with the main
/// camera every frame. The Pixel Perfect Camera can change the main camera's
/// orthographic size at runtime (e.g. on a screen / aspect-ratio change); without
/// this sync the overlay would keep the orthographic size copied once at editor
/// setup time and render the tutorial signs at a stale scale.
///
/// Lives on the overlay camera, which is parented to the main camera.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class TutorialOverlayCameraSync : MonoBehaviour
{
    [Tooltip("Main camera to mirror. If empty, falls back to the parent's Camera, then Camera.main.")]
    public Camera mainCamera;

    private Camera overlayCamera;

    private void OnEnable()
    {
        overlayCamera = GetComponent<Camera>();
        ResolveMainCamera();
    }

    private void ResolveMainCamera()
    {
        if (mainCamera != null)
            return;

        if (transform.parent != null)
            mainCamera = transform.parent.GetComponent<Camera>();

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (overlayCamera == null)
            overlayCamera = GetComponent<Camera>();

        if (mainCamera == null)
            ResolveMainCamera();

        if (mainCamera == null || overlayCamera == null)
            return;

        overlayCamera.orthographic = mainCamera.orthographic;
        overlayCamera.orthographicSize = mainCamera.orthographicSize;
        overlayCamera.fieldOfView = mainCamera.fieldOfView;
        overlayCamera.nearClipPlane = mainCamera.nearClipPlane;
        overlayCamera.farClipPlane = mainCamera.farClipPlane;
    }
}
