using TMPro;
using UnityEngine;

public class UI_Score : MonoBehaviour
{
    private TextMeshProUGUI scoreText;
    private GameManager gameManager;

    private void Start()
    {
        scoreText = GetComponent<TextMeshProUGUI>();
        gameManager = GameManager.instance;
    }

    private void Update()
    {
        scoreText.text = "Score: " + gameManager.score;
    }
}
