using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Top-right "m:ss" timer HUD shown during gameplay. Self-bootstrapping like
/// <see cref="SceneTransition"/>: it spawns itself after every scene load but
/// only acts inside a "LevelN" scene that has a <see cref="GameManager"/>, so it
/// stays out of the Main Menu and credits. No prefab or scene setup required.
///
/// It just displays <see cref="GameManager.levelTime"/>; the timer itself lives
/// on the GameManager so pausing and respawns behave correctly.
/// </summary>
public class UI_LevelTimer : MonoBehaviour
{
    private static readonly Regex LevelSceneRegex = new Regex(@"^Level\d+$");

    private TextMeshProUGUI label;
    private GameManager gameManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn(scene);

    private static void TrySpawn(Scene scene)
    {
        if (!LevelSceneRegex.IsMatch(scene.name)) return;
        if (GameManager.instance == null) return;

        GameObject host = new GameObject("LevelTimerHUD");
        host.AddComponent<UI_LevelTimer>();
    }

    private void Start()
    {
        gameManager = GameManager.instance;
        if (gameManager == null)
        {
            Destroy(gameObject);
            return;
        }

        Canvas canvas = ResolveScreenSpaceCanvas();
        if (canvas == null)
        {
            Destroy(gameObject);
            return;
        }

        GameObject labelGO = new GameObject("TimerLabel", typeof(RectTransform));
        labelGO.transform.SetParent(canvas.transform, false);

        RectTransform rt = labelGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(20f, -20f);
        rt.sizeDelta = new Vector2(220f, 60f);

        label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = "0:00";
        label.fontSize = 40f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        TMP_FontAsset font = UIBuilder.GetUIFont();
        if (font != null) label.font = font;

        // Give the label its own material instance so a crisp black outline shows
        // reliably (setting it on the shared material can fail to refresh padding
        // for this text). Accessing fontMaterial creates the per-instance copy.
        Material mat = label.fontMaterial;
        mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.25f);
        label.UpdateMeshPadding();
    }

    private void Update()
    {
        if (gameManager == null || label == null) return;
        label.text = SaveSystem.FormatTime(gameManager.levelTime);
    }

    /// <summary>
    /// Finds a non-world-space root canvas to host the HUD, skipping the
    /// scene-transition fade overlay (it toggles inactive between transitions).
    /// Mirrors the resolver used by PauseMenu / LevelCompletePopup.
    /// </summary>
    private static Canvas ResolveScreenSpaceCanvas()
    {
        Canvas fallback = null;
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.WorldSpace) continue;
            if (c.GetComponentInParent<SceneTransition>() != null) continue;
            if (c.isRootCanvas) return c;
            fallback = c;
        }
        if (fallback != null) return fallback;

        GameObject canvasGO = new GameObject("LevelTimerCanvas",
            typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Canvas created = canvasGO.GetComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        created.sortingOrder = 30000;
        return created;
    }
}
