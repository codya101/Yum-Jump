using UnityEngine;

public class EnemyHeadTrigger : MonoBehaviour
{
    private IStompable enemy;

    private void Awake()
    {
        enemy = GetComponentInParent<IStompable>();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (enemy != null && collision.CompareTag("Player"))
            enemy.OnHeadStomp(collision);
    }
}
