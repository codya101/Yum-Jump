using UnityEngine;

/// <summary>
/// Scrolls a Tiled-draw-mode SpriteRenderer's texture to create an infinitely
/// animated background, and lets you pick from the Pixel Adventure color tiles.
///
/// Designed to live on a standalone prefab: it follows the main camera on its
/// own, so you can drop it anywhere in a scene. Requires a SpriteRenderer with
/// Draw Mode = Tiled and the sprite texture's Wrap Mode = Repeat.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[ExecuteAlways] // keep the chosen color visible in the editor without pressing Play
public class Background : MonoBehaviour
{
    // Order here must match the order of the sprites assigned in colorSprites.
    public enum BackgroundColor { Blue, Brown, Gray, Green, Pink, Purple, Yellow }

    [Header("Scrolling")]
    [Tooltip("Texture units per second. X scrolls sideways, Y scrolls vertically.")]
    [SerializeField] private Vector2 scrollSpeed = new Vector2(0.2f, 0.2f);

    [Header("Color")]
    [SerializeField] private BackgroundColor color = BackgroundColor.Blue;

    [Tooltip("Assign in the SAME order as the BackgroundColor enum: " +
             "Blue, Brown, Gray, Green, Pink, Purple, Yellow.")]
    [SerializeField] private Sprite[] colorSprites;

    [Header("Camera Follow")]
    [Tooltip("Keep the background centered on the main camera so it always fills " +
             "the view. Leave on so the prefab works without parenting it to the camera.")]
    [SerializeField] private bool followCamera = true;

    private SpriteRenderer sr;
    private Transform cam;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        ApplyColor();
    }

    private void OnEnable()
    {
        if (Camera.main != null)
            cam = Camera.main.transform;
    }

    private void LateUpdate()
    {
        // Follow the camera after it has moved this frame so the background
        // stays centered. Keep our own Z so sorting/render order is preserved.
        if (followCamera)
        {
            if (cam == null && Camera.main != null)
                cam = Camera.main.transform;
            if (cam != null)
                transform.position = new Vector3(cam.position.x, cam.position.y, transform.position.z);
        }

        // Only scroll while actually playing. mainTextureOffset edits the
        // instanced material, which we don't want to touch in edit mode.
        if (!Application.isPlaying || sr == null)
            return;

        sr.material.mainTextureOffset += scrollSpeed * Time.deltaTime;
    }

    /// <summary>Swap the background color at runtime (e.g. per level or on an event).</summary>
    public void SetColor(BackgroundColor newColor)
    {
        color = newColor;
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (sr == null)
            sr = GetComponent<SpriteRenderer>();

        int index = (int)color;
        if (colorSprites != null && index >= 0 && index < colorSprites.Length && colorSprites[index] != null)
            sr.sprite = colorSprites[index];
    }

    // Lets you preview color changes live in the Inspector without entering Play mode.
    private void OnValidate()
    {
        ApplyColor();
    }
}
