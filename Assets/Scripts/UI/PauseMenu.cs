using System;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ESC-toggled pause menu. Lives on the (repurposed) FruitContainer object so it
/// inherits the fruit sprites + font that were already wired for the old top-left
/// HUD. Builds its UI procedurally, matching the style of <see cref="LevelCompletePopup"/>:
/// a full-screen dim overlay + centered panel, paused via Time.timeScale = 0.
///
/// Contents: level title, per-fruit collected counts (replacing the old HUD),
/// Music/SFX volume sliders with mute toggles (bound to <see cref="AudioManager"/>),
/// and Main Menu / Exit Game buttons.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    // --- Kept under the SAME names as the old UI_FruitCount so the serialized
    // --- scene assignments survive the script swap. ---
    [Header("Fruit Display")]
    [SerializeField] private Sprite[] fruitSprites;
    [SerializeField] private TMP_FontAsset font;

    [Header("Menu")]
    [Tooltip("Leave empty to auto-derive from the scene name, e.g. 'Level1' -> 'Level 1'.")]
    [SerializeField] private string levelNameOverride = "";
    [Tooltip("Scene loaded by the Main Menu button. You can build this scene later; " +
             "until it's added to Build Settings the button logs a warning instead of erroring.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // Style constants mirrored from LevelCompletePopup for a consistent look.
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new Color(0.13f, 0.16f, 0.26f, 0.97f);
    private static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color MainMenuBtnColor = new Color(0.28f, 0.45f, 0.62f);
    private static readonly Color ExitBtnColor = new Color(0.62f, 0.28f, 0.28f);

    private GameObject root;
    private Transform fruitListContainer;
    private Slider musicSlider, sfxSlider;
    private Image musicMuteIcon, sfxMuteIcon;
    private bool isPaused;
    private bool built;

    private static Sprite speakerOnSprite;
    private static Sprite speakerMutedSprite;

    private void Start()
    {
        Build();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) Resume();
            // Don't open the menu on top of another pause source (e.g. the level
            // complete popup, which also sets timeScale to 0).
            else if (Time.timeScale != 0f) Pause();
        }
    }

    private void OnDestroy()
    {
        // Never leave the game frozen if this object is torn down while paused.
        if (isPaused) Time.timeScale = 1f;
    }

    #region Open / Close

    private void Pause()
    {
        if (!built) Build();

        // Activate the menu BEFORE building the fruit rows. TMP can only set
        // outlineWidth once its material exists, which requires the text object to
        // be created under an active hierarchy — building rows while root is still
        // inactive throws a NullReferenceException in SetOutlineThickness.
        root.SetActive(true);
        root.transform.SetAsLastSibling();

        RefreshFruitList();
        SyncAudioControls();

        Time.timeScale = 0f;
        isPaused = true;
    }

    private void Resume()
    {
        root.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;
    }

    #endregion

    #region Build

    private void Build()
    {
        if (built) return;

        Canvas canvas = ResolveScreenSpaceCanvas();
        if (canvas == null) return;
        EnsureEventSystem();

        if (font == null) font = FindSceneFont();

        root = new GameObject("PauseRoot", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = DimColor;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(660f, 200f);
        panelRT.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(32, 32, 28, 28);
        panelLayout.spacing = 16f;
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        // Size the panel to its content (height only), keeping even padding.
        ContentSizeFitter panelFitter = panel.AddComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Title
        TextMeshProUGUI title = CreateText(panel.transform, "Title", GetLevelName(), 56f, TitleColor);
        SetPreferredHeight(title.gameObject, 72f);

        // Fruit list (rebuilt on each open)
        GameObject fruitList = new GameObject("FruitList", typeof(RectTransform), typeof(VerticalLayoutGroup));
        fruitList.transform.SetParent(panel.transform, false);
        VerticalLayoutGroup fruitLayout = fruitList.GetComponent<VerticalLayoutGroup>();
        fruitLayout.spacing = 6f;
        fruitLayout.childAlignment = TextAnchor.MiddleCenter;
        fruitLayout.childControlHeight = true;
        fruitLayout.childControlWidth = true;
        fruitLayout.childForceExpandHeight = false;
        fruitLayout.childForceExpandWidth = false;
        fruitList.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fruitListContainer = fruitList.transform;

        AddSeparator(panel.transform);

        // Audio section
        musicSlider = CreateAudioRow(panel.transform, "Music",
            () => AudioManager.Instance.MusicVolume,
            v => AudioManager.Instance.MusicVolume = v,
            () => AudioManager.Instance.MusicMuted,
            m => AudioManager.Instance.MusicMuted = m,
            out musicMuteIcon);

        sfxSlider = CreateAudioRow(panel.transform, "SFX",
            () => AudioManager.Instance.SfxVolume,
            v => AudioManager.Instance.SfxVolume = v,
            () => AudioManager.Instance.SfxMuted,
            m => AudioManager.Instance.SfxMuted = m,
            out sfxMuteIcon);

        AddSeparator(panel.transform);

        // Buttons
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

        Button mainMenuBtn = CreateButton(buttonRow.transform, "MainMenuBtn", "Main Menu", MainMenuBtnColor);
        mainMenuBtn.onClick.AddListener(OnMainMenu);

        Button exitBtn = CreateButton(buttonRow.transform, "ExitBtn", "Exit Game", ExitBtnColor);
        exitBtn.onClick.AddListener(OnExitGame);

        root.SetActive(false);
        built = true;
    }

    private string GetLevelName()
    {
        if (!string.IsNullOrWhiteSpace(levelNameOverride))
            return levelNameOverride;

        string sceneName = SceneManager.GetActiveScene().name;
        // "Level1" -> "Level 1": insert a space between a letter and a digit.
        return Regex.Replace(sceneName, "(?<=[A-Za-z])(?=[0-9])", " ");
    }

    #endregion

    #region Fruit list

    private void RefreshFruitList()
    {
        for (int i = fruitListContainer.childCount - 1; i >= 0; i--)
            Destroy(fruitListContainer.GetChild(i).gameObject);

        GameManager gm = GameManager.instance;
        if (gm == null) return;

        bool any = false;
        foreach (FruitType type in Enum.GetValues(typeof(FruitType)))
        {
            int total = gm.totalFruitsByType.TryGetValue(type, out int t) ? t : 0;
            if (total <= 0) continue;
            int collected = gm.fruitsCollectedByType.TryGetValue(type, out int c) ? c : 0;
            CreateFruitRow(type, collected, total);
            any = true;
        }

        if (!any)
            CreateText(fruitListContainer, "NoFruit", "No fruit in this level", 26f, new Color(0.8f, 0.8f, 0.8f));
    }

    private void CreateFruitRow(FruitType type, int collected, int total)
    {
        GameObject row = new GameObject(type.ToString(), typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(fruitListContainer, false);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        SetPreferredHeight(row, 56f);

        GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
        iconObj.transform.SetParent(row.transform, false);
        Image icon = iconObj.AddComponent<Image>();
        if (fruitSprites != null && (int)type < fruitSprites.Length)
            icon.sprite = fruitSprites[(int)type];
        icon.preserveAspect = true;
        LayoutElement iconLE = iconObj.AddComponent<LayoutElement>();
        iconLE.preferredWidth = 56f;
        iconLE.preferredHeight = 56f;

        TextMeshProUGUI text = CreateText(row.transform, "Count", "x " + collected + " / " + total, 30f, Color.white);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement textLE = text.gameObject.AddComponent<LayoutElement>();
        textLE.preferredWidth = 160f;
        textLE.preferredHeight = 56f;
    }

    #endregion

    #region Audio controls

    private void SyncAudioControls()
    {
        AudioManager am = AudioManager.Instance;
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(am.MusicVolume);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(am.SfxVolume);
        if (musicMuteIcon != null) musicMuteIcon.sprite = am.MusicMuted ? GetSpeakerMutedSprite() : GetSpeakerOnSprite();
        if (sfxMuteIcon != null) sfxMuteIcon.sprite = am.SfxMuted ? GetSpeakerMutedSprite() : GetSpeakerOnSprite();
    }

    private Slider CreateAudioRow(Transform parent, string label,
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
        SetPreferredHeight(row, 50f);

        TextMeshProUGUI labelText = CreateText(row.transform, "Label", label, 28f, Color.white);
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

    private Slider CreateSlider(Transform parent)
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

    #endregion

    #region Button handlers

    /// <summary>
    /// Saves fruit collected on this (possibly unfinished) run so it still counts
    /// toward "most fruit on any attempt". No time is recorded — the run wasn't
    /// completed — and checkpoint progress is intentionally discarded, so the
    /// level restarts from the beginning next time.
    /// </summary>
    private void FlushAttemptProgress()
    {
        GameManager gm = GameManager.instance;
        if (gm == null) return;

        string scene = SceneManager.GetActiveScene().name;
        SaveSystem.RecordAttempt(scene, SaveSystem.ParseLevelNumber(scene), gm.fruitsCollected, gm.totalFruits);
    }

    private void OnMainMenu()
    {
        FlushAttemptProgress();
        Time.timeScale = 1f;
        isPaused = false;
        if (Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
        {
            SceneTransition.LoadScene(mainMenuSceneName);
        }
        else
        {
            Debug.LogWarning($"[PauseMenu] Main Menu scene '{mainMenuSceneName}' is not in " +
                "Build Settings yet. Create it and add it to Build Settings, or update " +
                "'Main Menu Scene Name' on the PauseMenu component.");
            // Re-open so the player isn't left in a frozen-but-hidden state.
            Pause();
        }
    }

    private void OnExitGame()
    {
        FlushAttemptProgress();
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    #region UI helpers (mirrors LevelCompletePopup styling)

    private void AddSeparator(Transform parent)
    {
        GameObject sep = new GameObject("Separator", typeof(RectTransform), typeof(Image));
        sep.transform.SetParent(parent, false);
        sep.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredHeight = 2f;
        le.minHeight = 2f;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, string text, float size, Color color)
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

    private Button CreateButton(Transform parent, string name, string label, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bgColor;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 240f;
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
        btn.targetGraphic = go.GetComponent<Image>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        btn.colors = cb;

        return btn;
    }

    private static void SetPreferredHeight(GameObject go, float h)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = h;
        le.minHeight = h;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    private static Canvas ResolveScreenSpaceCanvas()
    {
        Canvas fallback = null;
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.WorldSpace) continue;
            // Never host UI under the scene-transition fade overlay: it toggles
            // inactive between transitions, which would leave our menu's parent
            // inactive and break TMP material init.
            if (c.GetComponentInParent<SceneTransition>() != null) continue;
            if (c.isRootCanvas) return c;
            fallback = c;
        }
        if (fallback != null) return fallback;

        GameObject canvasGO = new GameObject("PauseMenuCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas created = canvasGO.GetComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        created.sortingOrder = 32750;
        return created;
    }

    private static TMP_FontAsset FindSceneFont()
    {
        foreach (TextMeshProUGUI tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tmp.font != null) return tmp.font;
        }
        return null;
    }

    #endregion

    #region Procedural speaker icons

    private static Sprite GetSpeakerOnSprite()
    {
        if (speakerOnSprite == null) speakerOnSprite = BuildSpeakerSprite(true);
        return speakerOnSprite;
    }

    private static Sprite GetSpeakerMutedSprite()
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
