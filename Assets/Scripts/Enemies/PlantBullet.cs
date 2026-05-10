using UnityEngine;

public class PlantBullet : MonoBehaviour
{
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private GameObject impactVFX;

    private Vector2 direction;
    private float speed;

    public void Launch(Vector2 dir, float spd)
    {
        direction = dir.normalized;
        speed = spd;

        SpriteRenderer renderer = GetComponentInChildren<SpriteRenderer>();
        if (renderer != null && direction.x > 0) renderer.flipX = true;

        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            Player player = collision.GetComponent<Player>();
            if (player != null)
            {
                player.Die();
                GameManager.instance.RespawnPlayer();
            }
            DestroySelf();
            return;
        }

        if (collision.GetComponent<Plant>() != null) return;
        if (collision.isTrigger) return;

        DestroySelf();
    }

    private void DestroySelf()
    {
        if (impactVFX != null) Instantiate(impactVFX, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }
}
