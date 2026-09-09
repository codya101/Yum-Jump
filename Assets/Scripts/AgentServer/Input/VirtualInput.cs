using System.Collections.Generic;

namespace YumJump.Agent
{
    /// <summary>
    /// The agent's keyboard. Key state is set once at the top of a tick (invariant I2) and is
    /// then constant for the whole tick, so "right held for 20 ticks" means exactly that: 20
    /// gameplay steps and 20 physics steps with the key down.
    /// </summary>
    public sealed class VirtualInput : IInputSource
    {
        public const string KeyLeft = "left";
        public const string KeyRight = "right";
        public const string KeyUp = "up";
        public const string KeyDown = "down";
        public const string KeyJump = "jump";

        public static readonly string[] SupportedKeys = { KeyLeft, KeyRight, KeyUp, KeyDown, KeyJump };

        private bool left, right, up, down, jump;
        private bool jumpWasHeld;
        private bool jumpEdge;

        public float Horizontal => (right ? 1f : 0f) - (left ? 1f : 0f);
        public float Vertical => (up ? 1f : 0f) - (down ? 1f : 0f);
        public bool JumpPressed => jumpEdge;
        public bool JumpHeld => jump;

        /// <summary>
        /// Applies the key set for the tick that is about to run. The jump edge is computed here
        /// (not sampled from the engine), so a jump fires on exactly one tick no matter how the
        /// frame rate behaves.
        /// </summary>
        public void ApplyForTick(IEnumerable<string> keys)
        {
            left = right = up = down = jump = false;

            if (keys != null)
            {
                foreach (string k in keys)
                {
                    if (k == null) continue;
                    switch (k.ToLowerInvariant())
                    {
                        case KeyLeft: left = true; break;
                        case KeyRight: right = true; break;
                        case KeyUp: up = true; break;
                        case KeyDown: down = true; break;
                        case KeyJump: jump = true; break;
                    }
                }
            }

            jumpEdge = jump && !jumpWasHeld;
            jumpWasHeld = jump;
        }

        /// <summary>Clears all keys and the jump edge history (used on reset).</summary>
        public void Clear()
        {
            left = right = up = down = jump = false;
            jumpWasHeld = false;
            jumpEdge = false;
        }

        public static bool IsSupported(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string k = key.ToLowerInvariant();
            for (int i = 0; i < SupportedKeys.Length; i++)
                if (SupportedKeys[i] == k) return true;
            return false;
        }
    }
}
