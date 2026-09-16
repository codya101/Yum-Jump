using UnityEngine;
using YumJump.Agent;

public class PlantBullet : SimBehaviour
{
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private GameObject impactVFX;
    [Tooltip("Layers that stop the bullet. Defaults to Ground + SmoothWall (tower walls) if left empty.")]
    [SerializeField] private LayerMask obstacleMask;

    private Vector2 direction;
    private float speed;
    private float radius;
    private float ageSeconds;

    private void Awake()
    {
        // Fall back to terrain layers if the mask wasn't assigned in the inspector.
        if (obstacleMask == 0)
            obstacleMask = LayerMask.GetMask("Ground", "SmoothWall");

        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle != null)
            radius = circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
    }

    public void Launch(Vector2 dir, float spd)
    {
        direction = dir.normalized;
        speed = spd;

        SpriteRenderer renderer = GetComponentInChildren<SpriteRenderer>();
        if (renderer != null && direction.x > 0) renderer.flipX = true;

        ageSeconds = 0f;
    }

    public override SimKind Kind => SimKind.Hazard;

    protected override void SimTick()
    {
        // Timed Destroy runs off the engine clock, so the bullet ages in sim time instead and
        // its whole life stays tick-quantized.
        ageSeconds += SimClock.DeltaTime;
        if (ageSeconds >= lifetime)
        {
            DestroySelf();
            return;
        }

        float step = speed * SimClock.DeltaTime;

        // Destroy on solid terrain (incl. tower walls) before passing through it.
        // A raycast is used rather than physics triggers because the bullet moves
        // by transform and the tilemaps are static colliders with no Rigidbody2D,
        // so OnTriggerEnter2D never fires against them. The raycast also ignores the
        // layer collision matrix, so it works regardless of those settings.
        RaycastHit2D hit = Physics2D.Raycast(transform.position, direction, step + radius, obstacleMask);
        if (hit.collider != null)
        {
            DestroySelf();
            return;
        }

        transform.Translate(direction * step, Space.World);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            Player player = collision.GetComponent<Player>();
            if (player != null)
            {
                player.Die("hazard:" + SimId);
                GameManager.instance.RespawnPlayer();
            }
            DestroySelf();
            return;
        }

        // Shooters are immune to bullets — without this, a trunk's own shot spawns
        // overlapping its body (which has a Rigidbody2D, so trigger events DO fire,
        // unlike the static plant) and pops instantly at the muzzle.
        if (collision.GetComponent<Plant>() != null) return;
        if (collision.GetComponent<Trunk>() != null) return;
        if (collision.isTrigger) return;

        DestroySelf();
    }

    private void DestroySelf()
    {
        if (impactVFX != null) Instantiate(impactVFX, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }
}
