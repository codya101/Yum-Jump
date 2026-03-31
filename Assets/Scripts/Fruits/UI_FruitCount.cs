using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_FruitCount : MonoBehaviour
{
    [SerializeField] private Transform container;
    [SerializeField] private Sprite[] fruitSprites; // Index must match FruitType enum order
    [SerializeField] private float iconSize = 80;
    [SerializeField] private float fontSize = 28;
    [SerializeField] private float entrySpacing = 8;
    [SerializeField] private float rowSpacing = 2;

    private GameManager gameManager;
    private Dictionary<FruitType, GameObject> fruitEntries = new Dictionary<FruitType, GameObject>();

    private void Start()
    {
        gameManager = GameManager.instance;

        VerticalLayoutGroup verticalLayout = container.GetComponent<VerticalLayoutGroup>();
        if (verticalLayout != null)
        {
            verticalLayout.spacing = rowSpacing;
            verticalLayout.childControlHeight = false;
            verticalLayout.childControlWidth = false;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childForceExpandWidth = false;
        }
    }

    private void Update()
    {
        UpdateFruitDisplay();
    }

    private void UpdateFruitDisplay()
    {
        foreach (FruitType fruitType in Enum.GetValues(typeof(FruitType)))
        {
            int count = 0;
            if (gameManager.fruitsCollectedByType.ContainsKey(fruitType))
                count = gameManager.fruitsCollectedByType[fruitType];

            if (count > 0)
            {
                if (!fruitEntries.ContainsKey(fruitType))
                    CreateFruitEntry(fruitType);

                TextMeshProUGUI text = fruitEntries[fruitType].GetComponentInChildren<TextMeshProUGUI>();
                text.text = "x " + count;
            }
        }
    }

    private void CreateFruitEntry(FruitType fruitType)
    {
        GameObject entry = new GameObject(fruitType.ToString(), typeof(RectTransform));
        entry.transform.SetParent(container, false);

        LayoutElement entryLayout = entry.AddComponent<LayoutElement>();
        entryLayout.preferredHeight = iconSize;

        HorizontalLayoutGroup layout = entry.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = entrySpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = false;
        layout.childControlWidth = false;

        // Fruit icon
        GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
        iconObj.transform.SetParent(entry.transform, false);
        Image icon = iconObj.AddComponent<Image>();
        icon.sprite = fruitSprites[(int)fruitType];
        icon.preserveAspect = true;
        LayoutElement iconLayout = iconObj.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = iconSize;
        iconLayout.preferredHeight = iconSize;

        // Count text
        GameObject textObj = new GameObject("Count", typeof(RectTransform));
        textObj.transform.SetParent(entry.transform, false);
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        LayoutElement textLayout = textObj.AddComponent<LayoutElement>();
        textLayout.preferredWidth = iconSize * 3;
        textLayout.preferredHeight = iconSize;

        fruitEntries[fruitType] = entry;
    }
}
