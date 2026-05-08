using UnityEngine;

public class Trampoline : MonoBehaviour
{
    [SerializeField] private float bounceForce = 22f;
    [SerializeField] private float bounceCooldown = 0.15f;

    private Animator anim;
    private float lastBounceTime = -1f;

    private void Awake()
    {
        anim = GetComponent<Animator>();
    }

    private void OnTriggerEnter2D(Collider2D collision) => TryBounce(collision);
    private void OnTriggerStay2D(Collider2D collision) => TryBounce(collision);

    private void TryBounce(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (Time.time - lastBounceTime < bounceCooldown) return;

        Rigidbody2D playerRb = collision.GetComponent<Rigidbody2D>();
        if (playerRb == null || playerRb.linearVelocity.y > -5f) return;

        Player player = collision.GetComponent<Player>();
        if (player == null) return;

        player.Bounce(bounceForce);
        anim.SetTrigger("bounce");
        lastBounceTime = Time.time;
    }
}
