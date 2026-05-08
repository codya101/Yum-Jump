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
    private Button nextButton;
    private string pendingNextScene;

    public static void Show(int score, int requiredScore, string nextSceneName)
    {
        if (instance == null)
        {
            GameObject host = new GameObject("LevelCompletePopup");
            instance = host.AddComponent<LevelCompletePopup>();
            instance.Build();
        }

        instance.Display(score, requiredScore, nextSceneName);
    }

    private void Build()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
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
        panelLayout.childControlHeight = false;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        TextMeshProUGUI title = CreateText(panel.transform, "Title", "Level Completed!", font, 56f, new Color(1f, 0.85f, 0.3f));
        SetPreferredHeight(title.gameObject, 80f);

        yourScoreText = CreateText(panel.transform, "YourScore", "Your score: 0", font, 36f, Color.white);
        SetPreferredHeight(yourScoreText.gameObject, 48f);

        requiredScoreText = CreateText(panel.transform, "RequiredScore", "Required score: 0", font, 28f, new Color(0.85f, 0.85f, 0.85f));
        SetPreferredHeight(requiredScoreText.gameObject, 40f);

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

        nextButton = CreateButton(buttonRow.transform, "NextBtn", "Next Level", font, new Color(0.28f, 0.55f, 0.32f));
        nextButton.onClick.AddListener(OnNext);

        root.SetActive(false);
    }

    private void Display(int score, int requiredScore, string nextSceneName)
    {
        pendingNextScene = nextSceneName;
        yourScoreText.text = "Your score: " + score;
        requiredScoreText.text = "Required score: " + requiredScore;
        nextButton.interactable = score >= requiredScore;

        root.SetActive(true);
        Time.timeScale = 0f;
    }

    private void OnRestart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnNext()
    {
        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(pendingNextScene))
            SceneManager.LoadScene(pendingNextScene);
    }

    private static TMP_FontAsset FindSceneFont()
    {
        foreach (TextMeshProUGUI tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tmp.font != null) return tmp.font;
        }
        return null;
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
