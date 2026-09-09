using UnityEngine;
using YumJump.Agent;

public class Trampoline : SimBehaviour
{
    [SerializeField] private float bounceForce = 22f;
    [SerializeField] private float bounceCooldown = 0.15f;

    private Animator anim;
    private float lastBounceTime = -1f;

    protected override void SimTick() { }

    // The cooldown is stored as an absolute sim time, and sim time restarts at zero on every
    // reset - so it has to be restored, or the pad would refuse to bounce after a reset.
    protected override object CaptureExtra() => lastBounceTime;

    protected override void RestoreExtra(object extra)
    {
        lastBounceTime = extra is float f ? f : -1f;
    }

    private void Awake()
    {
        anim = GetComponent<Animator>();
    }

    private void OnTriggerEnter2D(Collider2D collision) => TryBounce(collision);
    private void OnTriggerStay2D(Collider2D collision) => TryBounce(collision);

    private void TryBounce(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (SimClock.Time - lastBounceTime < bounceCooldown) return;

        Rigidbody2D playerRb = collision.GetComponent<Rigidbody2D>();
        if (playerRb == null || playerRb.linearVelocity.y > -5f) return;

        Player player = collision.GetComponent<Player>();
        if (player == null) return;

        player.Bounce(bounceForce);
        anim.SetTrigger("bounce");
        AudioManager.Instance.PlayTrampoline();
        lastBounceTime = SimClock.Time;
    }
}
