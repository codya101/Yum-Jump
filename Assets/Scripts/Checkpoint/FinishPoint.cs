using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FinishPoint : MonoBehaviour
{
    [SerializeField] private string nextSceneName;
    [SerializeField] private int requiredScore;

    private Animator anim => GetComponent<Animator>();
    private bool isTriggered;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isTriggered) return;

        Player player = collision.GetComponent<Player>();

        if (player != null)
        {
            isTriggered = true;
            // Freeze the run time the moment the finish is reached, before the
            // activation animation plays out.
            if (GameManager.instance != null)
                GameManager.instance.StopLevelTimer();
            anim.SetTrigger("activate");
            StartCoroutine(ShowPopupRoutine());
        }
    }

    private IEnumerator ShowPopupRoutine()
    {
        yield return null;
        float length = anim.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(length);

        int score = GameManager.instance != null ? GameManager.instance.score : 0;
        float levelTime = GameManager.instance != null ? GameManager.instance.levelTime : 0f;
        int levelNumber = SaveSystem.ParseLevelNumber(SceneManager.GetActiveScene().name);
        LevelCompletePopup.Show(score, requiredScore, nextSceneName, levelTime, levelNumber);
    }
}
