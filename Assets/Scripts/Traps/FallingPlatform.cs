using System.Collections;
using UnityEngine;

public class FallingPlatform : MonoBehaviour
{
    [SerializeField] private float shakeDuration = 0.6f;
    [SerializeField] private float respawnDelay = 3f;
    [SerializeField] private float fallGravityScale = 2f;

    private Rigidbody2D rb;
    private Animator anim;
    private Vector3 startPosition;
    private bool triggered;
    private Coroutine fallRoutine;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
    }

    private void Start()
    {
        startPosition = transform.position;
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (triggered) return;
        if (!collision.collider.CompareTag("Player")) return;

        Bounds platformBounds = GetComponent<Collider2D>().bounds;
        Bounds playerBounds = collision.collider.bounds;

        if (playerBounds.min.y < platformBounds.max.y - 0.05f) return;

        triggered = true;
        anim.SetTrigger("activate");
        AudioManager.Instance.PlayFallingPlatform();
        fallRoutine = StartCoroutine(FallAndRespawn());
    }

    private IEnumerator FallAndRespawn()
    {
        yield return new WaitForSeconds(shakeDuration);

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = fallGravityScale;

        yield return new WaitForSeconds(respawnDelay);

        ResetPlatform();
    }

    public void ResetPlatform()
    {
        // Cancel any pending fall/respawn timer so an early reset (e.g. from a
        // SoftRespawnZone) can't leave a stale coroutine that resets the platform
        // again later — potentially yanking it out from under the player.
        if (fallRoutine != null)
        {
            StopCoroutine(fallRoutine);
            fallRoutine = null;
        }

        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        transform.position = startPosition;
        anim.Play("Idle");
        triggered = false;
    }
}
