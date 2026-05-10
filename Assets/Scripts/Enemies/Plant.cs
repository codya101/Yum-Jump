using UnityEngine;

[RequireComponent(typeof(CapsuleCollider2D))]
public class Plant : MonoBehaviour
{
    [Header("Attack")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float attackInterval = 2.5f;
    [SerializeField] private float bulletSpeed = 6f;

    [Header("Facing")]
    [SerializeField] private bool facingLeft = true;

    private Animator anim;
    private SpriteRenderer sr;
    private float nextAttackTime;

    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        sr = GetComponentInChildren<SpriteRenderer>();
    }

    private void Start()
    {
        if (!facingLeft)
        {
            sr.flipX = true;
            if (firePoint != null)
            {
                Vector3 lp = firePoint.localPosition;
                lp.x = -lp.x;
                firePoint.localPosition = lp;
            }
        }
        nextAttackTime = Time.time + attackInterval;
    }

    private void Update()
    {
        if (Time.time >= nextAttackTime)
        {
            anim.SetTrigger("attack");
            nextAttackTime = Time.time + attackInterval;
        }
    }

    public void Fire()
    {
        if (bulletPrefab == null) return;

        Vector3 origin = firePoint != null ? firePoint.position : transform.position;
        GameObject bullet = Instantiate(bulletPrefab, origin, Quaternion.identity);
        PlantBullet pb = bullet.GetComponent<PlantBullet>();
        if (pb != null) pb.Launch(facingLeft ? Vector2.left : Vector2.right, bulletSpeed);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Player playerComp = collision.collider.GetComponent<Player>();
        if (playerComp != null)
        {
            playerComp.Die();
            GameManager.instance.RespawnPlayer();
        }
    }
}
