using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared builder for the Music/SFX volume rows — a labelled slider plus a mute
/// toggle with a procedural speaker icon. Used by both the in-game
/// <see cref="PauseMenu"/> and the Main Menu's <see cref="SoundMenu"/> so the two
/// stay visually and behaviourally identical. Bind each row to <see cref="AudioManager"/>
/// through the getter/setter delegates.
/// </summary>
public static class AudioSettingsUI
{
    public static Slider CreateAudioRow(Transform parent, string label, TMP_FontAsset font,
        Func<float> getVolume, Action<float> setVolume,
        Func<bool> getMuted, Action<bool> setMuted,
        out Image muteIcon)
    {
        GameObject row = new GameObject(label + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        UIBuilder.SetPreferredHeight(row, 50f);

        TextMeshProUGUI labelText = UIBuilder.CreateText(row.transform, "Label", label, font, 28f, Color.white);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement labelLE = labelText.gameObject.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 110f;
        labelLE.preferredHeight = 44f;

        Slider slider = CreateSlider(row.transform);
        LayoutElement sliderLE = slider.gameObject.AddComponent<LayoutElement>();
        sliderLE.preferredWidth = 320f;
        sliderLE.preferredHeight = 30f;
        sliderLE.flexibleWidth = 1f;
        slider.SetValueWithoutNotify(getVolume());
        slider.onValueChanged.AddListener(v => setVolume(v));

        // Mute toggle button with a procedural speaker icon.
        GameObject muteGO = new GameObject("Mute", typeof(RectTransform), typeof(Image), typeof(Button));
        muteGO.transform.SetParent(row.transform, false);
        Image muteBg = muteGO.GetComponent<Image>();
        muteBg.color = new Color(0f, 0f, 0f, 0.25f);
        LayoutElement muteLE = muteGO.AddComponent<LayoutElement>();
        muteLE.preferredWidth = 50f;
        muteLE.preferredHeight = 50f;
        muteLE.minWidth = 50f;

        GameObject iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(muteGO.transform, false);
        RectTransform iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(34f, 34f);
        iconRT.anchoredPosition = Vector2.zero;
        Image iconImg = iconGO.AddComponent<Image>();
        iconImg.sprite = getMuted() ? GetSpeakerMutedSprite() : GetSpeakerOnSprite();
        muteIcon = iconImg;

        Image capturedIcon = iconImg;
        Button muteBtn = muteGO.GetComponent<Button>();
        muteBtn.targetGraphic = muteBg;
        muteBtn.onClick.AddListener(() =>
        {
            bool newMuted = !getMuted();
            setMuted(newMuted);
            capturedIcon.sprite = newMuted ? GetSpeakerMutedSprite() : GetSpeakerOnSprite();
        });

        return slider;
    }

    private static Slider CreateSlider(Transform parent)
    {
        GameObject sliderGO = new GameObject("Slider", typeof(RectTransform));
        sliderGO.transform.SetParent(parent, false);
        Slider slider = sliderGO.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        // Background track
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(sliderGO.transform, false);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.25f);
        bgRT.anchorMax = new Vector2(1f, 0.75f);
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.1f, 1f);

        // Fill area + fill
        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGO.transform, false);
        RectTransform fillAreaRT = fillArea.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRT.offsetMin = new Vector2(8f, 0f);
        fillAreaRT.offsetMax = new Vector2(-8f, 0f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.sizeDelta = new Vector2(10f, 0f);
        fill.GetComponent<Image>().color = new Color(0.95f, 0.78f, 0.25f, 1f);

        // Handle area + handle
        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGO.transform, false);
        RectTransform handleAreaRT = handleArea.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = new Vector2(0f, 0f);
        handleAreaRT.anchorMax = new Vector2(1f, 1f);
        handleAreaRT.offsetMin = new Vector2(8f, 0f);
        handleAreaRT.offsetMax = new Vector2(-8f, 0f);

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRT = handle.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0f);
        handleRT.anchorMax = new Vector2(0f, 1f);
        handleRT.sizeDelta = new Vector2(18f, 0f);
        handle.GetComponent<Image>().color = Color.white;

        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;

        return slider;
    }

    #region Procedural speaker icons

    private static Sprite speakerOnSprite;
    private static Sprite speakerMutedSprite;

    public static Sprite GetSpeakerOnSprite()
    {
        if (speakerOnSprite == null) speakerOnSprite = BuildSpeakerSprite(true);
        return speakerOnSprite;
    }

    public static Sprite GetSpeakerMutedSprite()
    {
        if (speakerMutedSprite == null) speakerMutedSprite = BuildSpeakerSprite(false);
        return speakerMutedSprite;
    }

    /// <summary>
    /// Draws a 16x16 pixel-art speaker. <paramref name="soundOn"/> adds sound-wave
    /// pixels; otherwise a red diagonal slash is drawn over a dimmed speaker.
    /// </summary>
    private static Sprite BuildSpeakerSprite(bool soundOn)
    {
        const int size = 16;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        Color clear = new Color(0, 0, 0, 0);
        Color body = soundOn ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
        Color wave = new Color(0.95f, 0.78f, 0.25f, 1f);
        Color slash = new Color(0.9f, 0.2f, 0.2f, 1f);

        Color[] px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        const float centerY = 7.5f;
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                bool on = false;

                // Speaker body (a small square block on the left).
                if (x >= 3 && x <= 5 && y >= 6 && y <= 9) on = true;

                // Cone: a triangle widening to the right.
                if (x > 5 && x <= 9)
                {
                    float t = (x - 5) / 4f;
                    float half = Mathf.Lerp(2f, 5f, t);
                    if (Mathf.Abs(y - centerY) <= half + 0.5f) on = true;
                }

                if (on) px[y * size + x] = body;
            }
        }

        if (soundOn)
        {
            // Two sound-wave bars to the right of the cone.
            for (int y = 6; y <= 9; y++) px[y * size + 11] = wave;
            for (int y = 4; y <= 11; y++) px[y * size + 13] = wave;
        }
        else
        {
            // Red diagonal slash across the whole icon.
            for (int d = 1; d < size - 1; d++)
            {
                px[d * size + d] = slash;
                if (d + 1 < size) px[d * size + (d + 1)] = slash;
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    #endregion
}
