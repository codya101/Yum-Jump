using UnityEngine;

/// <summary>
/// WebGL-only runtime setup, run automatically before the first scene loads.
///
/// * Locks the frame rate to 60. On WebGL, QualitySettings.vSyncCount is
///   ignored and Application.targetFrameRate is the only frame-rate control;
///   left unset it follows the browser's refresh rate, so the game runs too
///   fast on 120/144 Hz displays and inconsistently elsewhere. Pinning 60
///   gives the steady 60 fps the gameplay is tuned for.
///
/// * Spawns a persistent <see cref="WebPauseBridge"/> the page can SendMessage
///   to when the player exits fullscreen with ESC (see that class for why).
/// </summary>
public static class WebGLBootstrap
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Application.targetFrameRate = 60;

        var go = new GameObject("WebPauseBridge");
        go.AddComponent<WebPauseBridge>();
        Object.DontDestroyOnLoad(go);
    }
#endif
}
