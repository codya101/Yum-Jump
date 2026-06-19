using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a looping sprite-swap animation on a UI Image (e.g. a fruit next to
/// the menu title). Mirrors the in-game fruit animation, which the SpriteRenderer
/// clips can't drive on a UI Image.
/// </summary>
[RequireComponent(typeof(Image))]
public class UISpriteAnimator : MonoBehaviour
{
    [Tooltip("Animation frames in order, e.g. Apple_0 ... Apple_16.")]
    [SerializeField] private Sprite[] frames;

    [Tooltip("Playback speed. The in-game fruits run at 20.")]
    [SerializeField] private float framesPerSecond = 20f;

    private Image image;
    private float timer;
    private int index;

    private void Awake() => image = GetComponent<Image>();

    private void OnEnable()
    {
        timer = 0f;
        index = 0;
        if (frames != null && frames.Length > 0)
            image.sprite = frames[0];
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0 || framesPerSecond <= 0f)
            return;

        // Unscaled so it keeps animating even if the game is paused (timeScale 0).
        timer += Time.unscaledDeltaTime;
        float frameTime = 1f / framesPerSecond;

        while (timer >= frameTime)
        {
            timer -= frameTime;
            index = (index + 1) % frames.Length;
            image.sprite = frames[index];
        }
    }
}
