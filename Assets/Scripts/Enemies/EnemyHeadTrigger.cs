using UnityEngine;

public class EnemyHeadTrigger : MonoBehaviour
{
    private AngryPig enemy;

    private void Awake()
    {
        enemy = GetComponentInParent<AngryPig>();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
            enemy.OnHeadStomp(collision);
    }
}
