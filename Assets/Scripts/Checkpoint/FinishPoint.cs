using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FinishPoint : MonoBehaviour
{
    [SerializeField] private string nextSceneName;

    private Animator anim => GetComponent<Animator>();
    private bool isTriggered;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isTriggered) return;

        Player player = collision.GetComponent<Player>();

        if (player != null)
        {
            isTriggered = true;
            anim.SetTrigger("activate");
            StartCoroutine(LoadNextSceneRoutine());
        }
    }

    private IEnumerator LoadNextSceneRoutine()
    {
        yield return null;
        float length = anim.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(length);

        if (!string.IsNullOrEmpty(nextSceneName))
            SceneManager.LoadScene(nextSceneName);
    }
}
