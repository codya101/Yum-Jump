using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural Controls popup, opened from the Settings sub-menu. Lists the game's
/// key bindings (key on the left, action on the right) and a Back button that closes
/// the popup and returns to the Settings menu via the onBack callback. Styled like the
/// other overlays. Add future bindings to <see cref="Bindings"/>.
/// </summary>
public class ControlsMenu : MonoBehaviour
{
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new Color(0.13f, 0.16f, 0.26f, 0.97f);
    private static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color KeyColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color ActionColor = new Color(0.92f, 0.92f, 0.92f);
    private static readonly Color BackBtnColor = new Color(0.45f, 0.45f, 0.5f);

    // The control list. Extend this as new bindings are added.
    private static readonly (string key, string action)[] Bindings =
    {
        ("A / D", "Move Left / Right"),
        ("Hold A / D toward wall", "Wall Slide"),
        ("Spacebar", "Jump / Double Jump"),
        ("Spacebar while wall sliding", "Wall Jump"),
        ("Esc", "Open / Close Pause Menu"),
    };

    // Run after the popup closes so the Settings menu can re-show itself.
    private Action onBack;

    // The overlay UI is parented to the Canvas, not this controller's host
    // GameObject, so closing must destroy this reference (and the host) too.
    private GameObject root;

    public static void Show(Action onBack)
    {
        GameObject host = new GameObject("ControlsMenu");
        ControlsMenu menu = host.AddComponent<ControlsMenu>();
        menu.onBack = onBack;
        menu.Build();
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
        layout.padding = new RectOffset(48, 48, 32, 32);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = UIBuilder.CreateText(panel.transform, "Title", "Controls", font, 56f, TitleColor);
        UIBuilder.SetPreferredHeight(title.gameObject, 76f);

        // Build every row first, collecting the key/action cells so each column can
        // be sized to its widest entry below. This keeps the two columns aligned and
        // guarantees the (non-wrapping) text never spills past the panel, regardless
        // of how long the binding strings are.
        List<LayoutElement> keyCells = new List<LayoutElement>();
        List<LayoutElement> actionCells = new List<LayoutElement>();
        foreach ((string key, string action) in Bindings)
            CreateBindingRow(panel.transform, font, key, action, keyCells, actionCells);

        SizeColumn(keyCells);
        SizeColumn(actionCells);

        Button backBtn = UIBuilder.CreateButton(panel.transform, "BackBtn", "Back", font, BackBtnColor);
        backBtn.onClick.AddListener(Close);
    }

    private void CreateBindingRow(Transform parent, TMP_FontAsset font, string key, string action,
        List<LayoutElement> keyCells, List<LayoutElement> actionCells)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        UIBuilder.SetPreferredHeight(row, 44f);

        TextMeshProUGUI keyText = UIBuilder.CreateText(row.transform, "Key", key, font, 28f, KeyColor);
        keyText.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement keyLE = keyText.gameObject.AddComponent<LayoutElement>();
        keyLE.preferredHeight = 40f;
        keyCells.Add(keyLE);

        TextMeshProUGUI actionText = UIBuilder.CreateText(row.transform, "Action", action, font, 28f, ActionColor);
        actionText.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement actionLE = actionText.gameObject.AddComponent<LayoutElement>();
        actionLE.preferredHeight = 40f;
        actionCells.Add(actionLE);
    }

    /// <summary>
    /// Sets every cell in a column to the width of its widest text, so the column is
    /// exactly wide enough for its longest entry (plus a small margin) and the rows
    /// stay aligned.
    /// </summary>
    private static void SizeColumn(List<LayoutElement> cells)
    {
        const float margin = 12f;
        float width = 0f;
        foreach (LayoutElement le in cells)
        {
            TextMeshProUGUI tmp = le.GetComponent<TextMeshProUGUI>();
            width = Mathf.Max(width, tmp.GetPreferredValues(tmp.text).x);
        }
        width = Mathf.Ceil(width) + margin;
        foreach (LayoutElement le in cells)
        {
            le.preferredWidth = width;
            le.minWidth = width;
        }
    }

    private void Close()
    {
        Action cb = onBack;
        if (root != null) Destroy(root);
        Destroy(gameObject);
        cb?.Invoke();
    }
}
