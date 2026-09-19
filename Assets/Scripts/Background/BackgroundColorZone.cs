using UnityEngine;

/// <summary>
/// Trigger zone that recolors the scrolling background when the player crosses it —
/// e.g. the Brown -> Purple shift at Level4's Act 2/3 seam. Re-triggering is harmless
/// (SetColor is idempotent), so a second zone further back with the original color
/// makes the shift reversible for backtracking players.
///
/// Purely cosmetic, so it stays a plain MonoBehaviour outside the sim registry.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BackgroundColorZone : MonoBehaviour
{
    [Tooltip("The scene's Background instance. Found automatically if left empty.")]
    [SerializeField] private Background background;
    [SerializeField] private Background.BackgroundColor color = Background.BackgroundColor.Purple;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;

        if (background == null)
            background = FindFirstObjectByType<Background>();

        if (background != null)
            background.SetColor(color);
    }
}
