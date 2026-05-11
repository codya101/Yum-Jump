using UnityEngine;

public class Fan : MonoBehaviour
{
    [SerializeField] private float updraftSpeed = 12f;

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;

        Rigidbody2D playerRb = collision.GetComponent<Rigidbody2D>();
        if (playerRb == null) return;

        Vector2 vel = playerRb.linearVelocity;
        if (vel.y < updraftSpeed)
            vel.y = updraftSpeed;
        playerRb.linearVelocity = vel;
    }
}
