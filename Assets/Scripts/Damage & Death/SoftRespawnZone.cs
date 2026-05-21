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
            Destroy(player.gameObject);
            GameManager.instance.RespawnPlayer();
        }
    }
}
