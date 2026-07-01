using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Main Menu. Hook the public methods to button OnClick events
/// in the Inspector.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Scene loaded by New Game. Level1 is the start of the game.")]
    [SerializeField] private string newGameScene = "Level1";

    [Tooltip("Continue button. Grayed out (non-interactable) when there is no save.")]
    [SerializeField] private Button continueButton;

    private void Start()
    {
        // A brand-new player has nothing to continue, so disable Continue until
        // there is saved progress. Only New Game and Settings are usable then.
        if (continueButton != null)
            continueButton.interactable = SaveSystem.HasSave();
    }

    /// <summary>
    /// Starts a fresh game. When there is existing progress this confirms first,
    /// then wipes all saved progress and loads the first level. The confirmation
    /// guards against losing unlocks, best times, and fruit records on a misclick.
    /// With no save there is nothing to lose, so it skips straight to the level.
    /// </summary>
    public void NewGame()
    {
        AudioManager.Instance.PlayMenuSelect();
        if (SaveSystem.HasSave())
            ConfirmDialog.Show("Erase all progress and start over?", StartFreshGame);
        else
            StartFreshGame();
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
        AudioManager.Instance.PlayMenuSelect();
        LevelSelectScreen.Show();
    }

    /// <summary>Opens the Settings sub-menu (Controls / Sound) as a popup overlay.
    /// Wired to the Settings button.</summary>
    public void OpenSettings()
    {
        AudioManager.Instance.PlayMenuSelect();
        SettingsMenu.Show();
    }

    /// <summary>Quits the game. Matches the Pause menu's Exit Game button: stops
    /// play mode in the editor, quits the application in a build. Wired to the
    /// Exit Game button.</summary>
    public void QuitGame()
    {
        AudioManager.Instance.PlayMenuSelect();
        GameExit.Quit();
    }
}
