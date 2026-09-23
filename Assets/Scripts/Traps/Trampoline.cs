using UnityEngine;
using YumJump.Agent;

public class Trampoline : SimBehaviour
{
    [SerializeField] private float bounceForce = 22f;
    [SerializeField] private float bounceCooldown = 0.15f;

    // Tolerance on "not moving upward", so a player resting on the pad with a hair of positive
    // velocity from the contact solver still counts as landed.
    private const float RisingSpeedThreshold = 0.01f;

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
        if (playerRb == null) return;

        // Only refuse a player who is on the way up - passing through the pad on the rise of a
        // bounce, or clipping it from below. Anything else that overlaps the pad is coming down
        // onto it and should launch.
        //
        // This used to demand a fall of at least 5 units/s, which made short drops dead: the
        // player touches down too slowly to qualify, the resting contact then zeroes vertical
        // velocity, and every later OnTriggerStay2D sees ~0 and refuses too - so the pad sat
        // inert while the player slid across it.
        if (playerRb.linearVelocity.y > RisingSpeedThreshold) return;

        Player player = collision.GetComponent<Player>();
        if (player == null) return;

        player.Bounce(bounceForce);
        anim.SetTrigger("bounce");
        AudioManager.Instance.PlayTrampoline();
        lastBounceTime = SimClock.Time;
    }
}
