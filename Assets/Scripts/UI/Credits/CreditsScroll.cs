using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Scrolls a credits panel up from the bottom of the screen and, once it has
/// fully passed the top, loads the menu scene. Put this on the credits content
/// RectTransform (anchored to bottom-center, pivot 0,0.5 -> y pivot 0).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CreditsScroll : MonoBehaviour
{
    [Tooltip("Scroll speed in canvas units per second.")]
    [SerializeField] private float scrollSpeed = 120f;

    [Tooltip("Scene to load once the credits finish.")]
    [SerializeField] private string returnScene = "MainMenu";

    [Tooltip("Extra distance scrolled after the last line leaves the screen.")]
    [SerializeField] private float endPadding = 150f;

    [Tooltip("Let the player press any key / click to skip to the end.")]
    [SerializeField] private bool allowSkip = true;

    [Tooltip("Seconds before skipping is allowed (avoids an instant skip).")]
    [SerializeField] private float skipDelay = 0.75f;

    private RectTransform content;
    private float endY;
    private float elapsed;
    private bool finished;

    private void Start()
    {
        content = GetComponent<RectTransform>();

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("CreditsScroll must live under a Canvas.");
            enabled = false;
            return;
        }

        // Make sure the content has its real height before we measure it.
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        float canvasHeight = (canvas.transform as RectTransform).rect.height;
        float contentHeight = content.rect.height;

        // Start fully below the screen (top edge at the bottom of the view) and
        // finish once the bottom edge has scrolled past the top of the view.
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, -contentHeight);
        endY = canvasHeight + endPadding;
    }

    private void Update()
    {
        if (finished)
            return;

        elapsed += Time.unscaledDeltaTime;

        if (allowSkip && elapsed >= skipDelay && Input.anyKeyDown)
        {
            Finish();
            return;
        }

        Vector2 pos = content.anchoredPosition;
        pos.y += scrollSpeed * Time.unscaledDeltaTime;
        content.anchoredPosition = pos;

        if (pos.y >= endY)
            Finish();
    }

    private void Finish()
    {
        finished = true;
        Time.timeScale = 1f; // in case a paused state carried over
        SceneTransition.LoadScene(returnScene);
    }
}
