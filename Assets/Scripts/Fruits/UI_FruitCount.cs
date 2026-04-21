using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_FruitCount : MonoBehaviour
{
    [SerializeField] private Sprite[] fruitSprites;
    [SerializeField] private TMP_FontAsset font;

    [Header("Position")]
    [SerializeField] private Vector2 screenOffset = new Vector2(10f, -10f);

    [Header("Layout")]
    [SerializeField] private float iconSize = 80f;
    [SerializeField] private float fontSize = 28f;
    [SerializeField] private float entrySpacing = 8f;
    [SerializeField] private float rowSpacing = 4f;

    [Header("Slide-In")]
    [SerializeField] private float slideDuration = 0.25f;

    [Header("Text")]
    [SerializeField] private Color textColor = Color.white;

    [Header("Flash")]
    [SerializeField] private Color flashColor = new Color(1f, 1f, 0.4f, 1f);
    [SerializeField] private float flashDuration = 0.2f;

    private GameManager gameManager;
    private Dictionary<FruitType, GameObject> fruitEntries = new Dictionary<FruitType, GameObject>();
    private Dictionary<FruitType, int> lastCounts = new Dictionary<FruitType, int>();
    private Dictionary<FruitType, (TextMeshProUGUI text, Image icon)> entryComponents
        = new Dictionary<FruitType, (TextMeshProUGUI, Image)>();

    private void Start()
    {
        gameManager = GameManager.instance;

        RectTransform rt = GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = screenOffset;

        VerticalLayoutGroup layout = gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = rowSpacing;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.padding = new RectOffset(8, 8, 8, 8);

        ContentSizeFitter fitter = gameObject.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

    }

    private void Update()
    {
        UpdateFruitDisplay();
    }

    private void UpdateFruitDisplay()
    {
        foreach (FruitType fruitType in Enum.GetValues(typeof(FruitType)))
        {
            int count = gameManager.fruitsCollectedByType.TryGetValue(fruitType, out int c) ? c : 0;
            if (count <= 0) continue;

            if (!fruitEntries.ContainsKey(fruitType))
            {
                CreateFruitEntry(fruitType);
                lastCounts[fruitType] = 0;
            }

            int typeTotal = gameManager.totalFruitsByType.TryGetValue(fruitType, out int t) ? t : count;
            entryComponents[fruitType].text.text = "x " + count + " / " + typeTotal;

            int prev = lastCounts.TryGetValue(fruitType, out int lc) ? lc : 0;
            if (count > prev)
            {
                lastCounts[fruitType] = count;
                StartCoroutine(FlashEntry(fruitType));
            }
        }
    }

    private void CreateFruitEntry(FruitType fruitType)
    {
        GameObject entry = new GameObject(fruitType.ToString(), typeof(RectTransform));
        entry.transform.SetParent(transform, false);

        LayoutElement entryLayout = entry.AddComponent<LayoutElement>();
        entryLayout.preferredHeight = iconSize;
        entryLayout.preferredWidth = iconSize * 4f;

        HorizontalLayoutGroup layout = entry.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = entrySpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
        iconObj.transform.SetParent(entry.transform, false);
        Image icon = iconObj.AddComponent<Image>();
        icon.sprite = fruitSprites[(int)fruitType];
        icon.preserveAspect = true;
        LayoutElement iconLayout = iconObj.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = iconSize;
        iconLayout.preferredHeight = iconSize;

        GameObject textObj = new GameObject("Count", typeof(RectTransform));
        textObj.transform.SetParent(entry.transform, false);
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        if (font != null) text.font = font;
        text.color = textColor;
        text.outlineWidth = 0.15f;
        text.outlineColor = new Color(0f, 0f, 0f, 0.9f);

        LayoutElement textLayout = textObj.AddComponent<LayoutElement>();
        textLayout.preferredWidth = iconSize * 3f;
        textLayout.preferredHeight = iconSize;

        fruitEntries[fruitType] = entry;
        entryComponents[fruitType] = (text, icon);

        StartCoroutine(FadeIn(entry));
    }

    private IEnumerator FadeIn(GameObject entry)
    {
        CanvasGroup cg = entry.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.SmoothStep(0f, 1f, elapsed / slideDuration);
            yield return null;
        }
        cg.alpha = 1f;
        Destroy(cg);
    }

    private IEnumerator FlashEntry(FruitType fruitType)
    {
        var (text, icon) = entryComponents[fruitType];
        Color originalText = text.color;
        Color originalIcon = icon.color;
        text.color = flashColor;
        icon.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        text.color = originalText;
        icon.color = originalIcon;
    }

}
