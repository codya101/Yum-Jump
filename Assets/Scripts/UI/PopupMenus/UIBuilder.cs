using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared helpers for the procedurally-built menus (PauseMenu, LevelCompletePopup,
/// LevelSelectScreen, ConfirmDialog). Centralizes canvas resolution, the EventSystem
/// guard, font lookup, and styled text/button creation so the look stays consistent
/// and the construction code isn't copy-pasted across screens.
/// </summary>
public static class UIBuilder
{
    /// <summary>
    /// Returns a non-world-space root canvas to host runtime UI, falling back to
    /// creating one. Skips world-space canvases (e.g. tutorial signs) and the
    /// scene-transition fade overlay, which toggles inactive between transitions.
    /// </summary>
    public static Canvas ResolveScreenSpaceCanvas(int createdSortingOrder = 32750)
    {
        Canvas fallback = null;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.WorldSpace) continue;
            if (c.GetComponentInParent<SceneTransition>() != null) continue;
            if (c.isRootCanvas) return c;
            fallback = c;
        }
        if (fallback != null) return fallback;

        GameObject canvasGO = new GameObject("RuntimeUICanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas created = canvasGO.GetComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        created.sortingOrder = createdSortingOrder;
        return created;
    }

    public static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        Object.DontDestroyOnLoad(es);
    }

    public static TMP_FontAsset FindSceneFont()
    {
        foreach (TextMeshProUGUI tmp in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tmp.font != null) return tmp.font;
        }
        return null;
    }

    // The game's primary UI font (lives under a Resources folder so it loads at
    // runtime). Resolving it explicitly keeps the procedural menus on one font
    // instead of whatever TMP object FindSceneFont happens to hit first.
    private const string UIFontResourcePath = "Fonts & Materials/LcdSolid-VPzB SDF";
    private static TMP_FontAsset uiFont;

    /// <summary>
    /// Returns the LcdSolid UI font, falling back to any scene font if it can't
    /// be loaded. Use this for procedural menus so their text stays consistent.
    /// </summary>
    public static TMP_FontAsset GetUIFont()
    {
        if (uiFont == null)
            uiFont = Resources.Load<TMP_FontAsset>(UIFontResourcePath);
        return uiFont != null ? uiFont : FindSceneFont();
    }

    public static void SetPreferredHeight(GameObject go, float h)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = h;
        le.minHeight = h;
    }

    public static TextMeshProUGUI CreateText(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color)
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

    public static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font, Color bgColor)
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
