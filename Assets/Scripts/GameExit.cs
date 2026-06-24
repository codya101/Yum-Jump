using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// One place for "leave the game" behavior across build targets, used by the
/// Main Menu and Pause menu Exit Game buttons.
///
/// Editor:     stop play mode.
/// WebGL:      Application.Quit() is a no-op that just halts the player loop
///             and leaves a frozen canvas, so instead navigate the page back
///             to the site home (the game runs inside the site's /play iframe).
/// Standalone: a real Application.Quit().
/// </summary>
public static class GameExit
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void YumJumpExitToSite();
#endif

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        YumJumpExitToSite();
#else
        Application.Quit();
#endif
    }
}
