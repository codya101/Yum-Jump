using System.Collections;
using UnityEngine;

/// <summary>
/// Turns the normal Player prefab into a living idle decoration for the main
/// menu: skips the "Appearing" spawn animation, never falls or takes input,
/// and idly hops, turns around, and runs back and forth for a bit of life.
///
/// Add this to a Player prefab instance placed in the MainMenu scene. It leaves
/// the real Player prefab untouched.
/// </summary>
[DisallowMultipleComponent]
public class MenuPlayer : MonoBehaviour
{
    [Header("Behavior")]
    [Tooltip("Idly hop, turn, and run around on a random timer.")]
    [SerializeField] private bool enableLife = true;
    [Tooltip("Seconds of idle between actions.")]
    [SerializeField] private float minActionDelay = 1.5f;
    [SerializeField] private float maxActionDelay = 4f;

    [Header("Hop")]
    [SerializeField] private float hopHeight = 1.2f;
    [SerializeField] private float hopDuration = 0.6f;

    [Header("Run")]
    [Tooltip("World units per second while running.")]
    [SerializeField] private float runSpeed = 4f;
    [SerializeField] private float minRunDistance = 1.5f;
    [SerializeField] private float maxRunDistance = 4f;
    [Tooltip("World X of the left edge the player may walk to (e.g. the left side of the ground).")]
    [SerializeField] private float patrolLeftX = -8f;
    [Tooltip("World X of the right edge the player may walk to (e.g. the right side of the ground).")]
    [SerializeField] private float patrolRightX = 8f;

    private Animator anim;
    private float groundY;
    private int facingDir = 1; // +1 right, -1 left; prefab faces right by default

    private void Start()
    {
        // Strip out gameplay behaviour so this is purely decorative.
        Player player = GetComponent<Player>();
        if (player != null)
            player.enabled = false;

        // Stop the spawn animation event (FinishRespawn) from doing anything.
        PlayerAnimationEvents events = GetComponentInChildren<PlayerAnimationEvents>();
        if (events != null)
            events.enabled = false;

        // Kinematic so it never falls (the disabled Player no longer zeroes gravity).
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        // Skip the "Appearing" spawn VFX: jump straight to the idle state and
        // apply it this frame so the spawn poof is never rendered.
        anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            SetIdleAnim();
            anim.Play("Idle/Move", 0, 0f);
            anim.Update(0f);
        }

        groundY = transform.position.y;

        if (enableLife && anim != null)
            StartCoroutine(LifeRoutine());
    }

    private IEnumerator LifeRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minActionDelay, maxActionDelay));

            if (Random.value < 0.5f)
                yield return Hop();
            else
                yield return RunAndStop();
        }
    }

    private IEnumerator Hop()
    {
        anim.SetBool("isGrounded", false);

        float t = 0f;
        while (t < hopDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / hopDuration);

            // Parabolic arc that peaks at the midpoint and returns to the ground.
            float height = hopHeight * 4f * p * (1f - p);
            transform.position = new Vector3(transform.position.x, groundY + height, transform.position.z);

            // Drive the Jump/Fall blend tree: positive rising, negative falling.
            anim.SetFloat("yVelocity", Mathf.Cos(p * Mathf.PI));
            yield return null;
        }

        transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
        anim.SetFloat("yVelocity", 0f);
        anim.SetBool("isGrounded", true);
    }

    private IEnumerator RunAndStop()
    {
        // Pick a direction, but if there's no room that way (we're near an edge)
        // turn and walk the other way instead of stalling in place.
        int dir = Random.value < 0.5f ? -1 : 1;
        if (RoomInDirection(dir) < minRunDistance)
            dir = -dir;

        float room = RoomInDirection(dir);
        if (room < 0.1f)
            yield break; // pinned on both sides (patrol range too small) — skip

        float distance = Mathf.Min(Random.Range(minRunDistance, maxRunDistance), room);
        float targetX = transform.position.x + dir * distance;

        FaceTowards(targetX);

        // The blend tree treats |xVelocity| >= 1 as running; sign is irrelevant.
        anim.SetFloat("xVelocity", runSpeed * facingDir);

        while (Mathf.Abs(targetX - transform.position.x) > 0.01f)
        {
            float step = runSpeed * Time.deltaTime;
            float newX = Mathf.MoveTowards(transform.position.x, targetX, step);
            transform.position = new Vector3(newX, groundY, transform.position.z);
            yield return null;
        }

        SetIdleAnim();
    }

    private void SetIdleAnim()
    {
        anim.SetBool("isGrounded", true);
        anim.SetFloat("xVelocity", 0f);
        anim.SetFloat("yVelocity", 0f);
    }

    // World-space distance the player can still travel in dir (+1 right, -1 left)
    // before reaching the edge of its patrol bounds.
    private float RoomInDirection(int dir)
    {
        return dir == 1
            ? patrolRightX - transform.position.x
            : transform.position.x - patrolLeftX;
    }

    private void FaceTowards(float targetX)
    {
        int desiredDir = targetX < transform.position.x ? -1 : 1;
        if (desiredDir != facingDir)
            Flip();
    }

    private void Flip()
    {
        facingDir *= -1;
        transform.Rotate(0f, 180f, 0f);
    }
}
