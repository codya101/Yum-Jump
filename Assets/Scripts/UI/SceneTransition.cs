using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fades the screen to black when leaving a scene and back in when a new scene
/// loads, giving every scene change a smooth transition.
///
/// It is fully self-contained: a persistent singleton bootstraps itself on game
/// launch (no prefab or scene setup needed) and builds its own full-screen black
/// overlay canvas that sits above all gameplay UI and popups. Load scenes through
/// <see cref="LoadScene(string)"/> instead of SceneManager.LoadScene to get the
/// fade-out; the fade-in happens automatically on every scene that loads.
/// </summary>
public class SceneTransition : MonoBehaviour
{
    private static SceneTransition instance;

    // Kept short so transitions feel snappy rather than sluggish.
    private const float FadeDuration = 0.4f;

    private GameObject fadeCanvas;
    private CanvasGroup canvasGroup;
    private bool isTransitioning;

    /// <summary>
    /// Creates the singleton right after the first scene finishes loading, so the
    /// game fades in on launch without anything needing to reference this class.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureExists();
    }

    private static void EnsureExists()
    {
        if (instance != null) return;

        GameObject host = new GameObject("SceneTransition");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<SceneTransition>();
        instance.Build();
    }

    /// <summary>
    /// Fades out of the current scene, loads <paramref name="sceneName"/>, then
    /// fades into it. Use this in place of SceneManager.LoadScene.
    /// </summary>
    public static void LoadScene(string sceneName)
    {
        EnsureExists();
        instance.BeginLoad(sceneName);
    }

    private void Build()
    {
        fadeCanvas = new GameObject("FadeCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        fadeCanvas.transform.SetParent(transform, false);

        Canvas canvas = fadeCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above everything, including the level-complete popup (32760).
        canvas.sortingOrder = short.MaxValue;

        canvasGroup = fadeCanvas.GetComponent<CanvasGroup>();

        GameObject overlayGO = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
        overlayGO.transform.SetParent(fadeCanvas.transform, false);
        RectTransform rt = overlayGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        overlayGO.GetComponent<Image>().color = Color.black;

        SceneManager.sceneLoaded += OnSceneLoaded;

        // The first scene is already loaded when we bootstrap (sceneLoaded won't
        // fire for it), so start fully black and fade it in here.
        FadeIn();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Reveal each newly loaded scene with a fade-in. Stay opaque until the
        // new scene is up so the swap itself is never visible.
        FadeIn();
    }

    private void FadeIn()
    {
        fadeCanvas.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        StartCoroutine(Fade(1f, 0f, () =>
        {
            canvasGroup.blocksRaycasts = false;
            isTransitioning = false;
            // Deactivate while idle. Other UI (PauseMenu, LevelCompletePopup)
            // searches for an active root canvas to host itself; if this overlay
            // stayed active it would be adopted as that host and render the menu
            // invisibly under its alpha-0 CanvasGroup.
            fadeCanvas.SetActive(false);
        }));
    }

    private void BeginLoad(string sceneName)
    {
        if (isTransitioning) return; // ignore double-clicks mid-transition
        isTransitioning = true;
        StartCoroutine(FadeOutAndLoad(sceneName));
    }

    private IEnumerator FadeOutAndLoad(string sceneName)
    {
        // Block input while fading so the player can't act on a scene that's
        // about to be torn down.
        fadeCanvas.SetActive(true);
        canvasGroup.blocksRaycasts = true;
        yield return Fade(0f, 1f);
        // OnSceneLoaded takes over and fades the new scene in.
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator Fade(float from, float to, Action onComplete = null)
    {
        canvasGroup.alpha = from;

        // Unscaled time: pause and level-complete screens set Time.timeScale to
        // 0, but the fade must still animate from those states.
        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / FadeDuration);
            yield return null;
        }

        canvasGroup.alpha = to;
        onComplete?.Invoke();
    }

    private void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
