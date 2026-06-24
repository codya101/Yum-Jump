using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural Sound popup, opened from the Settings sub-menu. Shows the same Music/SFX
/// volume sliders and mute toggles as the in-game <see cref="PauseMenu"/> (both built
/// via <see cref="AudioSettingsUI"/>, bound to <see cref="AudioManager"/>). Back closes
/// the popup and returns to the Settings menu via the onBack callback.
/// </summary>
public class SoundMenu : MonoBehaviour
{
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new Color(0.13f, 0.16f, 0.26f, 0.97f);
    private static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color BackBtnColor = new Color(0.45f, 0.45f, 0.5f);

    // Run after the popup closes so the Settings menu can re-show itself.
    private Action onBack;

    // The overlay UI is parented to the Canvas, not this controller's host
    // GameObject, so closing must destroy this reference (and the host) too.
    private GameObject root;

    public static void Show(Action onBack)
    {
        GameObject host = new GameObject("SoundMenu");
        SoundMenu menu = host.AddComponent<SoundMenu>();
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
        panelRT.sizeDelta = new Vector2(660f, 200f);
        panelRT.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 28, 28);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = UIBuilder.CreateText(panel.transform, "Title", "Sound", font, 56f, TitleColor);
        UIBuilder.SetPreferredHeight(title.gameObject, 72f);

        AudioSettingsUI.CreateAudioRow(panel.transform, "Music", font,
            () => AudioManager.Instance.MusicVolume,
            v => AudioManager.Instance.MusicVolume = v,
            () => AudioManager.Instance.MusicMuted,
            m => AudioManager.Instance.MusicMuted = m,
            out _);

        AudioSettingsUI.CreateAudioRow(panel.transform, "SFX", font,
            () => AudioManager.Instance.SfxVolume,
            v => AudioManager.Instance.SfxVolume = v,
            () => AudioManager.Instance.SfxMuted,
            m => AudioManager.Instance.SfxMuted = m,
            out _);

        Button backBtn = UIBuilder.CreateButton(panel.transform, "BackBtn", "Back", font, BackBtnColor);
        backBtn.onClick.AddListener(Close);
    }

    private void Close()
    {
        Action cb = onBack;
        if (root != null) Destroy(root);
        Destroy(gameObject);
        cb?.Invoke();
    }
}
