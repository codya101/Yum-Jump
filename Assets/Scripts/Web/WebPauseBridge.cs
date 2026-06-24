using UnityEngine;

/// <summary>
/// Persistent receiver for browser-side events on WebGL. The WebGL page
/// (Assets/WebGLTemplates/YumJump/index.html) calls
/// <c>unityInstance.SendMessage("WebPauseBridge", "OnExitFullscreen")</c>
/// whenever the player leaves fullscreen -- typically by pressing ESC, which
/// the browser consumes to exit fullscreen before Unity can see the keypress.
/// We respond by opening the pause menu, so ESC-in-fullscreen still pauses.
///
/// Spawned (named "WebPauseBridge", kept across scene loads) by
/// <see cref="WebGLBootstrap"/>. The object name and method name here MUST
/// match the SendMessage call in the template.
/// </summary>
public class WebPauseBridge : MonoBehaviour
{
    public void OnExitFullscreen()
    {
        PauseMenu pause = FindFirstObjectByType<PauseMenu>();
        if (pause != null) pause.RequestPause();
    }
}
