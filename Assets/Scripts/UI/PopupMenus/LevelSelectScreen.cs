using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural Level Select overlay, opened from the Main Menu's Continue button.
/// Lists every "LevelN" scene found in Build Settings (via <see cref="LevelCatalog"/>),
/// showing each level's best time and best-fruit/total count from <see cref="SaveSystem"/>.
/// Unlocked levels are clickable and load the scene; locked levels render darkened
/// and do nothing. Built fresh on each open and destroyed on Back.
/// </summary>
public class LevelSelectScreen : MonoBehaviour
{
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new Color(0.13f, 0.16f, 0.26f, 0.97f);
    private static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color UnlockedBtnColor = new Color(0.28f, 0.45f, 0.62f);
    private static readonly Color LockedBtnColor = new Color(0.16f, 0.18f, 0.22f);
    private static readonly Color BackBtnColor = new Color(0.45f, 0.45f, 0.5f);
    private static readonly Color SubTextColor = new Color(0.88f, 0.88f, 0.88f);
    private static readonly Color LockedTextColor = new Color(0.55f, 0.55f, 0.6f);

    // The overlay UI is parented to the Canvas, not to this controller's host
    // GameObject, so closing must destroy this reference (and the host) — not
    // just the host, or the overlay would linger on screen.
    private GameObject root;

    public static void Show()
    {
        GameObject host = new GameObject("LevelSelectScreen");
        host.AddComponent<LevelSelectScreen>().Build();
    }

    private void Build()
    {
        Canvas canvas = UIBuilder.ResolveScreenSpaceCanvas();
        if (canvas == null)
        {
            Destroy(gameObject);
            return;
        }
        UIBuilder.EnsureEventSystem();
        TMP_FontAsset font = UIBuilder.GetUIFont();

        root = new GameObject("Root", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = DimColor;
        root.transform.SetAsLastSibling();

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 32, 32);
        layout.spacing = 28f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = UIBuilder.CreateText(panel.transform, "Title", "Select Level", font, 56f, TitleColor);
        UIBuilder.SetPreferredHeight(title.gameObject, 76f);

        // Row of level buttons.
        GameObject row = new GameObject("LevelRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childForceExpandWidth = false;

        List<LevelCatalog.LevelEntry> levels = LevelCatalog.GetLevels();
        foreach (LevelCatalog.LevelEntry level in levels)
            CreateLevelButton(row.transform, font, level);

        // Back button.
        Button backBtn = UIBuilder.CreateButton(panel.transform, "BackBtn", "Back", font, BackBtnColor);
        backBtn.onClick.AddListener(Close);
    }

    private void Close()
    {
        if (root != null) Destroy(root);
        Destroy(gameObject);
    }

    private void CreateLevelButton(Transform parent, TMP_FontAsset font, LevelCatalog.LevelEntry level)
    {
        bool unlocked = SaveSystem.IsUnlocked(level.number);
        SaveSystem.LevelProgress progress = SaveSystem.GetLevel(level.scene);

        GameObject go = new GameObject("Level" + level.number + "Btn",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = unlocked ? UnlockedBtnColor : LockedBtnColor;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 240f;
        le.preferredHeight = 200f;
        le.minWidth = 220f;
        le.minHeight = 180f;

        VerticalLayoutGroup btnLayout = go.GetComponent<VerticalLayoutGroup>();
        btnLayout.padding = new RectOffset(14, 14, 16, 16);
        btnLayout.spacing = 10f;
        btnLayout.childAlignment = TextAnchor.MiddleCenter;
        btnLayout.childControlHeight = true;
        btnLayout.childControlWidth = true;
        btnLayout.childForceExpandHeight = false;
        btnLayout.childForceExpandWidth = true;

        Color titleColor = unlocked ? Color.white : LockedTextColor;
        Color subColor = unlocked ? SubTextColor : LockedTextColor;

        TextMeshProUGUI name = UIBuilder.CreateText(go.transform, "Name", "Level " + level.number, font, 34f, titleColor);
        UIBuilder.SetPreferredHeight(name.gameObject, 44f);

        if (unlocked)
        {
            TextMeshProUGUI timeText = UIBuilder.CreateText(go.transform, "Time",
                "Best: " + SaveSystem.FormatTime(progress.bestTime), font, 24f, subColor);
            UIBuilder.SetPreferredHeight(timeText.gameObject, 32f);

            string fruitStr = progress.totalFruits > 0
                ? "Fruits: " + progress.bestFruits + "/" + progress.totalFruits
                : "Fruits: ?";
            TextMeshProUGUI fruitText = UIBuilder.CreateText(go.transform, "Fruits", fruitStr, font, 24f, subColor);
            UIBuilder.SetPreferredHeight(fruitText.gameObject, 32f);
        }
        else
        {
            TextMeshProUGUI lockText = UIBuilder.CreateText(go.transform, "Locked", "Locked", font, 26f, subColor);
            UIBuilder.SetPreferredHeight(lockText.gameObject, 36f);
        }

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.selectedColor = Color.white;
        // Darken locked buttons; they remain visible but clearly inactive.
        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        btn.colors = cb;

        btn.interactable = unlocked;
        if (unlocked)
        {
            string scene = level.scene;
            btn.onClick.AddListener(() =>
            {
                Time.timeScale = 1f;
                SceneTransition.LoadScene(scene);
            });
        }
    }
}
