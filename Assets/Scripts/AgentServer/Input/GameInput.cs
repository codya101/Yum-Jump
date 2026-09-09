using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>
    /// Everything the player controller needs to know about the controls.
    /// Two implementations exist: the real keyboard, and the agent's virtual keys.
    /// </summary>
    public interface IInputSource
    {
        /// <summary>-1, 0 or 1.</summary>
        float Horizontal { get; }

        /// <summary>-1, 0 or 1.</summary>
        float Vertical { get; }

        /// <summary>True only on the first tick/frame the jump key went down.</summary>
        bool JumpPressed { get; }

        /// <summary>True while the jump key is held.</summary>
        bool JumpHeld { get; }
    }

    /// <summary>The normal-play input source: Unity's keyboard axes.</summary>
    public sealed class KeyboardInputSource : IInputSource
    {
        public float Horizontal => Input.GetAxisRaw("Horizontal");
        public float Vertical => Input.GetAxisRaw("Vertical");
        public bool JumpPressed => Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow);
        public bool JumpHeld => Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow);
    }

    /// <summary>
    /// The one seam between the game and the agent: the controller reads its input here instead
    /// of from UnityEngine.Input, and the server swaps the source when it takes over.
    /// </summary>
    public static class GameInput
    {
        private static IInputSource source = new KeyboardInputSource();

        public static IInputSource Source
        {
            get => source;
            set => source = value ?? new KeyboardInputSource();
        }

        public static void UseKeyboard() => source = new KeyboardInputSource();

        public static float Horizontal => source.Horizontal;
        public static float Vertical => source.Vertical;
        public static bool JumpPressed => source.JumpPressed;
        public static bool JumpHeld => source.JumpHeld;
    }
}
