using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public class TutorialSign : MonoBehaviour
{
    public enum KeyKind { W, A, S, D, Space }

    [Header("Content")]
    [TextArea(1, 5)] public string message = "Welcome!";
    public KeyKind[] keys = new KeyKind[0];

    [Header("Font (empty = LCD Solid pixel font)")]
    public TMP_FontAsset fontOverride;

    [Header("World Layout")]
    [Tooltip("World units per canvas pixel. Default 0.0625 = 1/16, matching the project's 16 PPU pixel-perfect grid.")]
    public float canvasWorldScale = 0.0625f;
    [Tooltip("Canvas size in canvas pixels. With default scale, 256x128 = 16x8 world units.")]
    public Vector2 canvasPixelSize = new Vector2(256, 128);
    public float textFontSize = 28f;
    public Color textColor = Color.white;

    [Header("Keycap Style")]
    public float keycapPixelSize = 32f;
    public float keycapSpacingPx = 6f;
    public float keysGapBelowTextPx = 8f;
    public Color keycapFillColor = new Color(0.98f, 0.95f, 0.85f);
    public Color keycapBorderColor = Color.black;
    public Color keycapLabelColor = new Color(0.05f, 0.05f, 0.05f);
    public float keycapBorderPx = 3f;
    public float spaceKeyWidthMultiplier = 4f;

    [Header("Rendering")]
    public int sortingOrder = 10;
    public string sortingLayerName = "Default";
    [Tooltip("Unity Layer the visual is placed on. The overlay camera renders only this layer; the main camera is set to exclude it. Run 'Tools > Yum Jump > Setup Tutorial Overlay Camera' once to create the layer + camera.")]
    public string targetLayerName = "TutorialUI";

    private static Sprite cachedWhiteSprite;
    private static TMP_FontAsset cachedPixelFont;

    private void OnEnable() => Rebuild();

    private void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += DeferredRebuild;
#endif
    }

#if UNITY_EDITOR
    private void DeferredRebuild()
    {
        if (this == null) return;
        if (!isActiveAndEnabled) return;
        Rebuild();
    }
#endif

    public void Rebuild()
    {
        ClearVisual();

        GameObject visualRoot = new GameObject("__SignVisual");
        visualRoot.transform.SetParent(transform, false);
        visualRoot.hideFlags = HideFlags.DontSave;

        GameObject canvasGO = new GameObject("Canvas", typeof(Canvas));
        canvasGO.transform.SetParent(visualRoot.transform, false);
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingLayerName = sortingLayerName;
        canvas.sortingOrder = sortingOrder;
        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = canvasPixelSize;
        canvasRT.pivot = new Vector2(0.5f, 0.5f);
        canvasRT.localScale = Vector3.one * canvasWorldScale;
        canvasRT.localPosition = Vector3.zero;

        TMP_FontAsset font = fontOverride != null ? fontOverride : GetPixelFont();

        bool hasKeys = keys != null && keys.Length > 0;
        float keysZoneHeight = hasKeys ? (keycapPixelSize + keycapBorderPx * 2f + keysGapBelowTextPx) : 0f;

        GameObject textGO = new GameObject("Message", typeof(RectTransform));
        textGO.transform.SetParent(canvasRT, false);
        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = textFontSize;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        if (font != null) tmp.font = font;
        RectTransform tmpRT = tmp.rectTransform;
        tmpRT.anchorMin = Vector2.zero;
        tmpRT.anchorMax = Vector2.one;
        tmpRT.pivot = new Vector2(0.5f, 0.5f);
        tmpRT.offsetMin = new Vector2(0f, keysZoneHeight);
        tmpRT.offsetMax = Vector2.zero;

        if (hasKeys)
        {
            GameObject keyRow = new GameObject("Keys", typeof(RectTransform));
            keyRow.transform.SetParent(canvasRT, false);
            RectTransform rowRT = keyRow.GetComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0.5f, 0f);
            rowRT.anchorMax = new Vector2(0.5f, 0f);
            rowRT.pivot = new Vector2(0.5f, 0f);
            rowRT.sizeDelta = new Vector2(canvasPixelSize.x, keycapPixelSize + keycapBorderPx * 2f);
            rowRT.anchoredPosition = Vector2.zero;

            float totalWidth = 0f;
            for (int i = 0; i < keys.Length; i++) totalWidth += KeyWidthPx(keys[i]);
            totalWidth += keycapSpacingPx * Mathf.Max(0, keys.Length - 1);

            float xCursor = -totalWidth * 0.5f;
            for (int i = 0; i < keys.Length; i++)
            {
                float w = KeyWidthPx(keys[i]);
                CreateKeycap(rowRT, keys[i], new Vector2(xCursor + w * 0.5f, keycapPixelSize * 0.5f + keycapBorderPx), w, font);
                xCursor += w + keycapSpacingPx;
            }
        }

        int layer = LayerMask.NameToLayer(targetLayerName);
        if (layer >= 0) SetLayerRecursive(visualRoot, layer);
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }

    private float KeyWidthPx(KeyKind k) => k == KeyKind.Space ? keycapPixelSize * spaceKeyWidthMultiplier : keycapPixelSize;

    private void CreateKeycap(Transform parent, KeyKind k, Vector2 anchoredPos, float widthPx, TMP_FontAsset font)
    {
        GameObject cap = new GameObject(k + "Cap", typeof(RectTransform));
        cap.transform.SetParent(parent, false);
        RectTransform capRT = cap.GetComponent<RectTransform>();
        capRT.anchorMin = new Vector2(0.5f, 0f);
        capRT.anchorMax = new Vector2(0.5f, 0f);
        capRT.pivot = new Vector2(0.5f, 0.5f);
        capRT.sizeDelta = new Vector2(widthPx, keycapPixelSize);
        capRT.anchoredPosition = anchoredPos;

        GameObject borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(capRT, false);
        RectTransform borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-keycapBorderPx, -keycapBorderPx);
        borderRT.offsetMax = new Vector2(keycapBorderPx, keycapBorderPx);
        Image borderImg = borderGO.GetComponent<Image>();
        borderImg.color = keycapBorderColor;
        borderImg.sprite = GetWhiteSprite();

        GameObject fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(capRT, false);
        RectTransform fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        Image fillImg = fillGO.GetComponent<Image>();
        fillImg.color = keycapFillColor;
        fillImg.sprite = GetWhiteSprite();

        GameObject labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(capRT, false);
        TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = k == KeyKind.Space ? "SPACE" : k.ToString();
        label.fontSize = k == KeyKind.Space ? keycapPixelSize * 0.55f : keycapPixelSize * 0.75f;
        label.color = keycapLabelColor;
        label.alignment = TextAlignmentOptions.Center;
        if (font != null) label.font = font;
        RectTransform labelRT = label.rectTransform;
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
    }

    private void ClearVisual()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c != null && c.name == "__SignVisual")
            {
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
        }
    }

    private static Sprite GetWhiteSprite()
    {
        if (cachedWhiteSprite != null) return cachedWhiteSprite;
        Texture2D tex = Texture2D.whiteTexture;
        cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        cachedWhiteSprite.hideFlags = HideFlags.DontSave;
        return cachedWhiteSprite;
    }

    private static TMP_FontAsset GetPixelFont()
    {
        if (cachedPixelFont != null) return cachedPixelFont;
        cachedPixelFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LcdSolid-VPzB SDF");
        return cachedPixelFont;
    }
}
