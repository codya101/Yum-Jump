using System.Collections;
using TMPro;
using UnityEngine;

public class UI_Score : MonoBehaviour
{
    [Header("Count-Up")]
    [SerializeField] private float countUpSpeed = 2000f;

    [Header("Punch")]
    [SerializeField] private float punchScale = 1.35f;
    [SerializeField] private float punchDuration = 0.12f;

    private int displayDigits = 4;
    private TextMeshProUGUI scoreText;
    private GameManager gameManager;
    private float displayedScore;
    private int lastScore;
    private bool isPunching;

    private void Start()
    {
        scoreText = GetComponent<TextMeshProUGUI>();
        gameManager = GameManager.instance;

        scoreText.outlineWidth = 0.15f;
        scoreText.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        scoreText.textWrappingMode = TextWrappingModes.NoWrap;
        scoreText.overflowMode = TextOverflowModes.Overflow;
    }

    private void Update()
    {
        int target = gameManager.score;

        if (displayedScore < target)
            displayedScore = Mathf.MoveTowards(displayedScore, target, countUpSpeed * Time.deltaTime);

        scoreText.text = "Score: " + ((int)displayedScore).ToString("D" + displayDigits);

        if (target > lastScore && !isPunching)
        {
            lastScore = target;
            StartCoroutine(PunchScale());
        }
    }

    private IEnumerator PunchScale()
    {
        isPunching = true;
        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / punchDuration;
            transform.localScale = Vector3.one * Mathf.Lerp(punchScale, 1f, t);
            yield return null;
        }
        transform.localScale = Vector3.one;
        isPunching = false;
    }
}
