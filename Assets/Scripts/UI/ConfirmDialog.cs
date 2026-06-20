using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small procedural yes/cancel modal, styled like the other menus
/// (<see cref="PauseMenu"/> / <see cref="LevelCompletePopup"/>). Used by New Game
/// to confirm wiping save data. Self-contained: <see cref="Show"/> builds a fresh
/// instance, runs <paramref name="onConfirm"/> on Yes, and tears itself down.
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new Color(0.13f, 0.16f, 0.26f, 0.97f);
    private static readonly Color ConfirmBtnColor = new Color(0.62f, 0.28f, 0.28f);
    private static readonly Color CancelBtnColor = new Color(0.28f, 0.45f, 0.62f);

    private Action onConfirm;

    // The overlay is parented to the Canvas, not this controller's host, so
    // closing must destroy this reference too — not just the host.
    private GameObject root;

    public static void Show(string message, Action onConfirm, string confirmLabel = "Yes", string cancelLabel = "Cancel")
    {
        GameObject host = new GameObject("ConfirmDialog");
        ConfirmDialog dialog = host.AddComponent<ConfirmDialog>();
        dialog.onConfirm = onConfirm;
        dialog.Build(message, confirmLabel, cancelLabel);
    }

    private void Build(string message, string confirmLabel, string cancelLabel)
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
        panelRT.sizeDelta = new Vector2(660f, 220f);
        panelRT.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 28, 28);
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI msg = UIBuilder.CreateText(panel.transform, "Message", message, font, 32f, Color.white);
        // Allow the message to wrap so long text stays inside the panel instead
        // of overflowing past its edges.
        msg.textWrappingMode = TextWrappingModes.Normal;
        msg.overflowMode = TextOverflowModes.Overflow;
        UIBuilder.SetPreferredHeight(msg.gameObject, 100f);

        GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup rowLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childForceExpandWidth = false;
        UIBuilder.SetPreferredHeight(buttonRow, 80f);

        Button confirmBtn = UIBuilder.CreateButton(buttonRow.transform, "ConfirmBtn", confirmLabel, font, ConfirmBtnColor);
        confirmBtn.onClick.AddListener(OnConfirm);

        Button cancelBtn = UIBuilder.CreateButton(buttonRow.transform, "CancelBtn", cancelLabel, font, CancelBtnColor);
        cancelBtn.onClick.AddListener(OnCancel);
    }

    private void OnConfirm()
    {
        Action cb = onConfirm;
        Close();
        cb?.Invoke();
    }

    private void OnCancel() => Close();

    private void Close()
    {
        if (root != null) Destroy(root);
        Destroy(gameObject);
    }
}
