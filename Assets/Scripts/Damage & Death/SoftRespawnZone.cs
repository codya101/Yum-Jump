using UnityEngine;

public class SoftRespawnZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        FallingPlatform platform = collision.GetComponent<FallingPlatform>();
        if (platform != null)
        {
            platform.ResetPlatform();
            return;
        }

        Player player = collision.GetComponent<Player>();
        if (player != null)
        {
            // Same death feedback as a DeadZone: play the death SFX + VFX and destroy
            // the player. Other objects (falling platforms) still just reset above.
            player.Die();
            GameManager.instance.RespawnPlayer();
        }
    }
}
