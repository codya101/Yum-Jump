using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural Settings sub-menu popup, opened from the Main Menu's Settings button.
/// Styled like the other overlays (<see cref="LevelSelectScreen"/>,
/// <see cref="ConfirmDialog"/>): a dim backdrop over a centered panel. Holds the
/// "Controls" and "Sound" entries (wired up in a later step) plus a Back button.
/// Built fresh on each open and destroyed on Back.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new Color(0.13f, 0.16f, 0.26f, 0.97f);
    private static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color ButtonColor = new Color(0.28f, 0.45f, 0.62f);
    private static readonly Color BackBtnColor = new Color(0.45f, 0.45f, 0.5f);

    // The overlay UI is parented to the Canvas, not this controller's host
    // GameObject, so closing must destroy this reference (and the host) — not
    // just the host, or the overlay would linger on screen.
    private GameObject root;

    // Optional: invoked after this menu closes (Back). Lets a caller (e.g. the
    // PauseMenu) know the sub-menu is no longer open so it can re-assert itself.
    private Action onClose;

    public static void Show(Action onClose = null)
    {
        GameObject host = new GameObject("SettingsMenu");
        SettingsMenu menu = host.AddComponent<SettingsMenu>();
        menu.onClose = onClose;
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
        layout.padding = new RectOffset(40, 40, 32, 32);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = UIBuilder.CreateText(panel.transform, "Title", "Settings", font, 56f, TitleColor);
        UIBuilder.SetPreferredHeight(title.gameObject, 76f);

        Button controlsBtn = UIBuilder.CreateButton(panel.transform, "ControlsBtn", "Controls", font, ButtonColor);
        controlsBtn.onClick.AddListener(OpenControls);

        Button soundBtn = UIBuilder.CreateButton(panel.transform, "SoundBtn", "Sound", font, ButtonColor);
        soundBtn.onClick.AddListener(OpenSound);

        Button backBtn = UIBuilder.CreateButton(panel.transform, "BackBtn", "Back", font, BackBtnColor);
        backBtn.onClick.AddListener(Close);
    }

    // Hide this menu while a sub-popup is open, then restore it when the sub-popup's
    // Back button fires the callback — so Back always returns to the Settings menu.
    private void OpenControls()
    {
        root.SetActive(false);
        ControlsMenu.Show(RestoreSelf);
    }

    private void OpenSound()
    {
        root.SetActive(false);
        SoundMenu.Show(RestoreSelf);
    }

    private void RestoreSelf()
    {
        if (root == null) return;
        root.SetActive(true);
        root.transform.SetAsLastSibling();
    }

    private void Close()
    {
        Action cb = onClose;
        if (root != null) Destroy(root);
        Destroy(gameObject);
        cb?.Invoke();
    }
}
