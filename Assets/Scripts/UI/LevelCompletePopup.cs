using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelCompletePopup : MonoBehaviour
{
    private static LevelCompletePopup instance;

    private GameObject root;
    private TextMeshProUGUI yourScoreText;
    private TextMeshProUGUI requiredScoreText;
    private TextMeshProUGUI yourTimeText;
    private TextMeshProUGUI bestTimeText;
    private Button nextButton;
    private TextMeshProUGUI nextButtonLabel;
    private string pendingNextScene;

    // Any level whose FinishPoint points at this scene is the final level, so
    // its "next" button reads as a finish instead of "Next Level". Point any
    // level's FinishPoint at this scene and the label updates automatically.
    private const string CreditsSceneName = "TheEnd";
    private const string NextLevelLabel = "Next Level";
    private const string FinalLevelLabel = "The End";

    public static void Show(int score, int requiredScore, string nextSceneName, float levelTime, int levelNumber)
    {
        if (instance == null)
        {
            GameObject host = new GameObject("LevelCompletePopup");
            instance = host.AddComponent<LevelCompletePopup>();
            instance.Build();
        }

        instance.Display(score, requiredScore, nextSceneName, levelTime, levelNumber);
    }

    private void Build()
    {
        Canvas canvas = ResolveScreenSpaceCanvas();
        if (canvas == null) return;

        TMP_FontAsset font = FindSceneFont();

        root = new GameObject("Root", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;
        Image rootImg = root.GetComponent<Image>();
        rootImg.color = new Color(0f, 0f, 0f, 0.7f);

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(560f, 380f);
        panelRT.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.13f, 0.16f, 0.26f, 0.97f);

        VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(28, 28, 28, 28);
        panelLayout.spacing = 14f;
        panelLayout.childAlignment = TextAnchor.MiddleCenter;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        // Let the panel size itself to its content so the 28px padding stays
        // even on every side. With a fixed height the content block was
        // centered within leftover slack, leaving more space top/bottom than
        // left/right and making the popup look off-center.
        ContentSizeFitter panelFitter = panel.AddComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CreateText(panel.transform, "Title", "Level Completed!", font, 56f, new Color(1f, 0.85f, 0.3f));
        SetPreferredHeight(title.gameObject, 80f);

        yourScoreText = CreateText(panel.transform, "YourScore", "Your score: 0", font, 36f, Color.white);
        SetPreferredHeight(yourScoreText.gameObject, 48f);

        requiredScoreText = CreateText(panel.transform, "RequiredScore", "Required score: 0", font, 28f, new Color(0.85f, 0.85f, 0.85f));
        SetPreferredHeight(requiredScoreText.gameObject, 40f);

        yourTimeText = CreateText(panel.transform, "YourTime", "Your time: 0:00", font, 32f, Color.white);
        SetPreferredHeight(yourTimeText.gameObject, 44f);

        bestTimeText = CreateText(panel.transform, "BestTime", "Best time: 0:00", font, 28f, new Color(0.85f, 0.85f, 0.85f));
        SetPreferredHeight(bestTimeText.gameObject, 40f);

        GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup rowLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childForceExpandWidth = false;
        SetPreferredHeight(buttonRow, 80f);

        Button restartBtn = CreateButton(buttonRow.transform, "RestartBtn", "Restart", font, new Color(0.62f, 0.28f, 0.28f));
        restartBtn.onClick.AddListener(OnRestart);

        nextButton = CreateButton(buttonRow.transform, "NextBtn", NextLevelLabel, font, new Color(0.28f, 0.55f, 0.32f));
        nextButton.onClick.AddListener(OnNext);
        nextButtonLabel = nextButton.GetComponentInChildren<TextMeshProUGUI>();

        // The title ends in "!", whose glyph advance carries extra trailing
        // space, so plain center alignment lets the visible text drift left.
        // Force a layout pass so the title has its real width, then re-center
        // it on the actual rendered glyphs.
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);
        CenterTextHorizontally(title);

        root.SetActive(false);
    }

    private void Display(int score, int requiredScore, string nextSceneName, float levelTime, int levelNumber)
    {
        pendingNextScene = nextSceneName;
        yourScoreText.text = "Your score: " + score;
        requiredScoreText.text = "Required score: " + requiredScore;

        bool beaten = score >= requiredScore;
        nextButton.interactable = beaten;

        // Reaching the finish with enough score counts as beating the level:
        // record it (fastest time, best fruit, level total) which also unlocks
        // the next level for the Level Select screen.
        if (beaten)
        {
            string scene = SceneManager.GetActiveScene().name;
            GameManager gm = GameManager.instance;
            int fruits = gm != null ? gm.fruitsCollected : 0;
            int totalFruits = gm != null ? gm.totalFruits : 0;
            SaveSystem.RecordCompletion(scene, levelNumber, levelTime, fruits, totalFruits);
        }

        yourTimeText.text = "Your time: " + SaveSystem.FormatTime(levelTime);
        // Best is read back after recording, so a new record shows immediately.
        float best = SaveSystem.GetLevel(SceneManager.GetActiveScene().name).bestTime;
        bestTimeText.text = "Best time: " + SaveSystem.FormatTime(best);

        // Final level (the one leading to the credits) gets a finish label.
        if (nextButtonLabel != null)
            nextButtonLabel.text = nextSceneName == CreditsSceneName ? FinalLevelLabel : NextLevelLabel;

        root.SetActive(true);
        Time.timeScale = 0f;
    }

    private void OnRestart()
    {
        Time.timeScale = 1f;
        SceneTransition.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnNext()
    {
        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(pendingNextScene))
            SceneTransition.LoadScene(pendingNextScene);
    }

    /// <summary>
    /// Returns a screen-space canvas to host the popup. Tutorial signs create
    /// World Space canvases at runtime, so FindFirstObjectByType&lt;Canvas&gt;()
    /// is unreliable — it can return a sign's tiny world-space canvas and the
    /// popup would render microscopically off-screen. Falls back to creating a
    /// screen-space canvas if the scene has none.
    /// </summary>
    private static Canvas ResolveScreenSpaceCanvas()
    {
        Canvas fallback = null;
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.WorldSpace)
                continue;
            // Skip the scene-transition fade overlay; it toggles inactive between
            // transitions and isn't a valid host for persistent UI.
            if (c.GetComponentInParent<SceneTransition>() != null)
                continue;
            if (c.isRootCanvas)
                return c;
            fallback = c;
        }
        if (fallback != null)
            return fallback;

        GameObject canvasGO = new GameObject("LevelCompleteCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas created = canvasGO.GetComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        created.sortingOrder = 32760;
        return created;
    }

    private static TMP_FontAsset FindSceneFont()
    {
        foreach (TextMeshProUGUI tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tmp.font != null) return tmp.font;
        }
        return null;
    }

    /// <summary>
    /// Compensates for asymmetric glyph side-bearings (e.g. a trailing "!")
    /// so a center-aligned line is optically centered within its rect, not
    /// merely centered by character advance. Measures the actual rendered
    /// glyph quads and applies a width-neutral margin shift.
    /// </summary>
    private static void CenterTextHorizontally(TextMeshProUGUI tmp)
    {
        tmp.ForceMeshUpdate();
        TMP_TextInfo info = tmp.textInfo;

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible) continue;
            if (ci.bottomLeft.x < min) min = ci.bottomLeft.x;
            if (ci.topRight.x > max) max = ci.topRight.x;
        }
        if (min > max) return;

        // The visible glyphs span [min, max]; the rect center is local x = 0
        // (pivot 0.5). A trailing "!" leaves that span off-center, so translate
        // the text — equal-and-opposite margins keep the width unchanged — until
        // the rendered ink straddles the center evenly.
        float inkCenter = (min + max) * 0.5f;
        if (Mathf.Abs(inkCenter) > 0.25f)
        {
            Vector4 m = tmp.margin;
            tmp.margin = new Vector4(m.x - inkCenter, m.y, m.z + inkCenter, m.w);
            tmp.ForceMeshUpdate();
        }
    }

    private static void SetPreferredHeight(GameObject go, float h)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = h;
        le.minHeight = h;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        if (font != null) tmp.font = font;
        tmp.outlineWidth = 0.15f;
        tmp.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = bgColor;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 220f;
        le.preferredHeight = 70f;
        le.minWidth = 200f;
        le.minHeight = 60f;

        GameObject labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        RectTransform labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        TextMeshProUGUI labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label;
        labelTMP.fontSize = 30f;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = Color.white;
        if (font != null) labelTMP.font = font;
        labelTMP.outlineWidth = 0.15f;
        labelTMP.outlineColor = new Color(0f, 0f, 0f, 0.9f);

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        btn.colors = cb;

        return btn;
    }
}
