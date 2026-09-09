using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>
    /// The authoritative tick counter for agent (manual-stepping) mode, and the single source
    /// of time for every gameplay script.
    ///
    /// In normal play <see cref="ManualMode"/> is false and this is a thin pass-through to
    /// <see cref="UnityEngine.Time"/>, so the game behaves exactly as it always has. In agent
    /// mode the whole simulation advances in fixed <see cref="FixedDelta"/> steps driven by
    /// <see cref="SimDriver"/> - one tick per rendered frame - and <see cref="Time"/> is derived
    /// from the tick counter, which is zeroed on every reset. That last part is what makes
    /// replays identical: gameplay timers always restart from the same float values.
    /// </summary>
    public static class SimClock
    {
        /// <summary>True while the game is being stepped by the agent server.</summary>
        public static bool ManualMode { get; private set; }

        /// <summary>True only during the part of a frame in which a tick is being executed.</summary>
        public static bool Running { get; private set; }

        /// <summary>Seconds per tick. Mirrors Time.fixedDeltaTime (0.0166667 =&gt; 60 Hz).</summary>
        public static float FixedDelta { get; private set; } = 1f / 60f;

        public static int TickRateHz => Mathf.RoundToInt(1f / FixedDelta);

        /// <summary>Ticks since the last reset. This is the tick reported over the protocol.</summary>
        public static int Tick { get; private set; }

        /// <summary>Ticks since the process started. Never reset; pacing diagnostics only.</summary>
        public static long TotalTicks { get; private set; }

        /// <summary>Simulation seconds since the last reset (agent mode) or Time.time (normal play).</summary>
        public static float Time => ManualMode ? Tick * FixedDelta : UnityEngine.Time.time;

        /// <summary>The delta gameplay code should integrate with.</summary>
        public static float DeltaTime => ManualMode ? FixedDelta : UnityEngine.Time.deltaTime;

        internal static void EnterManualMode(float fixedDelta)
        {
            FixedDelta = fixedDelta;
            ManualMode = true;
            Running = false;
            Tick = 0;
        }

        internal static void ExitManualMode()
        {
            ManualMode = false;
            Running = false;
        }

        /// <summary>Zeroes the tick counter (and therefore sim time). Called by ResetController.</summary>
        internal static void ResetTicks()
        {
            Tick = 0;
        }

        internal static void BeginTick()
        {
            Running = true;
        }

        internal static void EndTick()
        {
            Tick++;
            TotalTicks++;
            Running = false;
        }

        /// <summary>
        /// Yield instruction for gameplay coroutines: waits <paramref name="seconds"/> of
        /// simulation time. In normal play this is an ordinary WaitForSeconds; in agent mode it
        /// resolves on a tick boundary, so coroutine timing is tick-quantized and replayable.
        /// </summary>
        public static object Wait(float seconds)
        {
            if (!ManualMode)
                return new WaitForSeconds(seconds);

            return new SimWait(seconds);
        }

        /// <summary>
        /// Waits one step: a frame in normal play, exactly one tick in agent mode. Use this
        /// instead of WaitForFixedUpdate, which keeps running off the engine clock even when the
        /// simulation is paused.
        /// </summary>
        public static object WaitStep()
        {
            return ManualMode ? (object)new SimWait(FixedDelta) : null;
        }
    }

    /// <summary>Sim-time equivalent of WaitForSeconds. Evaluated once per frame == once per tick.</summary>
    public sealed class SimWait : CustomYieldInstruction
    {
        private readonly int endTick;

        public SimWait(float seconds)
        {
            endTick = SimClock.Tick + Mathf.Max(1, Mathf.RoundToInt(seconds / SimClock.FixedDelta));
        }

        public override bool keepWaiting => SimClock.Tick < endTick;
    }
}
