using System;
using System.Collections.Generic;
using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>One step of a plan: a set of keys held for a whole number of ticks.</summary>
    public sealed class PlanStep
    {
        public string[] keys = Array.Empty<string>();
        public int ticks;
    }

    /// <summary>
    /// Runs a keystroke plan tick by tick, records the per-tick trace, and stops the moment
    /// something terminal happens. The trace up to a death is kept and returned: that is the
    /// agent's main learning signal, so it must never be thrown away.
    /// </summary>
    public sealed class PlanExecutor
    {
        public const int MaxPlanTicks = 6000;
        public const int MaxObserveTicks = 3000;

        private enum JobKind { None, Plan, Observe }

        private JobKind kind = JobKind.None;
        private List<PlanStep> steps;
        private int stepIndex;
        private int ticksIntoStep;
        private int startTick;
        private int ticksRun;
        private int observeTicks;

        private List<object> trace;
        private List<object> events;
        private string terminal;
        private string deathCause;
        private string checkpointId;
        private Vector2 lastKnownPosition;
        private Vector2 lastKnownVelocity;

        private Action<JsonObj> complete;
        private readonly VirtualInput input;

        public PlanExecutor(VirtualInput input)
        {
            this.input = input;
        }

        public bool Busy => kind != JobKind.None;

        /// <summary>Begins executing a plan. The response is delivered through <paramref name="onDone"/>.</summary>
        public void StartPlan(List<PlanStep> plan, Action<JsonObj> onDone)
        {
            steps = plan;
            kind = JobKind.Plan;
            stepIndex = 0;
            ticksIntoStep = 0;
            ticksRun = 0;
            startTick = SimClock.Tick;
            terminal = null;
            deathCause = null;
            checkpointId = null;
            complete = onDone;

            trace = new List<object>(256);
            events = new List<object>();
            SimEvents.Clear();

            RecordPlayerSample();   // state at startTick, before any input
        }

        /// <summary>Advances the paused world with no input, recording where dynamic objects go.</summary>
        public void StartObserve(int ticks, Action<JsonObj> onDone)
        {
            kind = JobKind.Observe;
            observeTicks = Mathf.Clamp(ticks, 1, MaxObserveTicks);
            ticksRun = 0;
            startTick = SimClock.Tick;
            terminal = null;
            deathCause = null;
            checkpointId = null;
            complete = onDone;

            trace = new List<object>(observeTicks + 1);
            events = new List<object>();
            SimEvents.Clear();

            RecordDynamicsSample();
        }

        /// <summary>
        /// Cuts the running job short and hands its partial result back to the caller, or returns
        /// null if nothing was running. The job's own callback is dropped rather than invoked: the
        /// client matches responses by id, so one request must produce exactly one response, and
        /// the command that ordered the abort is the one that answers.
        /// </summary>
        public JsonObj Abort(string reason)
        {
            if (kind == JobKind.None) return null;
            terminal = "aborted";
            return Finish(reason, false);
        }

        /// <summary>True while a job still has ticks to run - the driver steps the world only then.</summary>
        public bool WantsTick()
        {
            if (kind == JobKind.None) return false;
            if (kind == JobKind.Observe) return ticksRun < observeTicks;
            return stepIndex < steps.Count;
        }

        /// <summary>Applies this tick's keys before any gameplay logic runs (invariant I2).</summary>
        public void BeginTick()
        {
            if (kind == JobKind.Plan && stepIndex < steps.Count)
                input.ApplyForTick(steps[stepIndex].keys);
            else
                input.ApplyForTick(null);
        }

        /// <summary>Called after the physics step: records the tick and checks for terminals.</summary>
        public void EndTick()
        {
            if (kind == JobKind.None) return;

            ticksRun++;

            if (kind == JobKind.Plan)
            {
                ticksIntoStep++;
                if (stepIndex < steps.Count && ticksIntoStep >= steps[stepIndex].ticks)
                {
                    stepIndex++;
                    ticksIntoStep = 0;
                }
                RecordPlayerSample();
            }
            else
            {
                RecordDynamicsSample();
            }

            DrainEvents();

            bool stop = terminal == "died" || terminal == "level_end" ||
                        !WantsTick() || ticksRun >= MaxPlanTicks;

            if (stop)
            {
                if (terminal == null)
                    terminal = checkpointId != null ? "checkpoint" : "completed";
                Finish(null, true);
            }
        }

        // ------------------------------------------------------------------ internals

        private void DrainEvents()
        {
            IReadOnlyList<SimEvent> pending = SimEvents.Pending;
            for (int i = 0; i < pending.Count; i++)
            {
                SimEvent e = pending[i];
                events.Add(new JsonObj()
                    .Add("tick", e.tick)
                    .Add("type", SimEvents.WireName(e.type))
                    .Add("detail", e.detail));

                switch (e.type)
                {
                    case SimEventType.Death:
                        if (terminal == null) { terminal = "died"; deathCause = e.detail; }
                        break;
                    case SimEventType.LevelEnd:
                        if (terminal != "died") terminal = "level_end";
                        break;
                    case SimEventType.Checkpoint:
                        checkpointId = e.detail;
                        break;
                }
            }
            SimEvents.Clear();
        }

        private void RecordPlayerSample()
        {
            Player player = GameManager.instance != null ? GameManager.instance.player : null;
            if (player != null)
            {
                lastKnownPosition = player.SimPosition;
                lastKnownVelocity = player.SimVelocity;
            }

            trace.Add(new JsonObj()
                .Add("tick", SimClock.Tick)
                .Add("pos", lastKnownPosition)
                .Add("vel", lastKnownVelocity)
                .Add("grounded", player != null && player.IsGrounded));
        }

        private void RecordDynamicsSample()
        {
            trace.Add(new JsonObj()
                .Add("tick", SimClock.Tick)
                .Add("dynamics", WorldSampler.DynamicsPositions()));
        }

        /// <summary>
        /// Ends the job and builds its result. <paramref name="deliver"/> is false for an abort,
        /// where the result is returned to the aborting command instead of answering on the job's
        /// own request id.
        /// </summary>
        private JsonObj Finish(string note, bool deliver)
        {
            var result = new JsonObj()
                .Add("startTick", startTick)
                .Add("endTick", SimClock.Tick)
                .Add("ticksRun", ticksRun);

            if (kind == JobKind.Plan)
            {
                result.Add("terminal", terminal ?? "completed")
                      .Add("deathCause", deathCause)
                      .Add("checkpoint", checkpointId);
            }

            result.Add("trace", trace)
                  .Add("events", events);

            if (note != null) result.Add("note", note);

            Action<JsonObj> callback = complete;

            kind = JobKind.None;
            steps = null;
            complete = null;
            input.ApplyForTick(null);

            if (deliver) callback?.Invoke(result);
            return result;
        }
    }
}
