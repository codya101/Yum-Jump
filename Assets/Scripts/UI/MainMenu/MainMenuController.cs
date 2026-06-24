using UnityEngine;

/// <summary>
/// Drives the Main Menu. Hook the public methods to button OnClick events
/// in the Inspector.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Scene loaded by New Game. Level1 is the start of the game.")]
    [SerializeField] private string newGameScene = "Level1";

    /// <summary>
    /// Starts a fresh game: confirms first, then wipes all saved progress and
    /// loads the first level. The confirmation guards against losing unlocks,
    /// best times, and fruit records on a misclick.
    /// </summary>
    public void NewGame()
    {
        ConfirmDialog.Show("Erase all progress and start over?", StartFreshGame);
    }

    private void StartFreshGame()
    {
        SaveSystem.Wipe();
        // The menu never pauses the game, but a previous session might have
        // left the timescale at 0 (e.g. a pause/level-complete screen), so
        // reset it before loading so the level runs normally.
        Time.timeScale = 1f;
        SceneTransition.LoadScene(newGameScene);
    }

    /// <summary>Opens the Level Select screen so the player can resume any
    /// unlocked level. Wired to the Continue button.</summary>
    public void Continue()
    {
        LevelSelectScreen.Show();
    }

    /// <summary>Opens the Settings sub-menu (Controls / Sound) as a popup overlay.
    /// Wired to the Settings button.</summary>
    public void OpenSettings()
    {
        SettingsMenu.Show();
    }

    /// <summary>Quits the game. Matches the Pause menu's Exit Game button: stops
    /// play mode in the editor, quits the application in a build. Wired to the
    /// Exit Game button.</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
