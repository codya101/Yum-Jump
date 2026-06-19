using UnityEngine;

/// <summary>
/// Drives the Main Menu. Hook the public methods to button OnClick events
/// in the Inspector.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Scene loaded by New Game. Level1 is the start of the game.")]
    [SerializeField] private string newGameScene = "Level1";

    /// <summary>Starts a fresh game from the first level.</summary>
    public void NewGame()
    {
        // The menu never pauses the game, but a previous session might have
        // left the timescale at 0 (e.g. a pause/level-complete screen), so
        // reset it before loading so the level runs normally.
        Time.timeScale = 1f;
        SceneTransition.LoadScene(newGameScene);
    }

    /// <summary>Quits the application (ignored in the editor).</summary>
    public void QuitGame()
    {
        Application.Quit();
    }
}
