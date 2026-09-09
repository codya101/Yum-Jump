using System.Collections.Generic;
using UnityEngine;

namespace YumJump.Agent
{
    public enum SimEventType
    {
        Death,
        Checkpoint,
        LevelEnd,
        FruitCollected,
        EnemyKilled
    }

    public struct SimEvent
    {
        public int tick;
        public SimEventType type;
        /// <summary>Free-form detail, e.g. "hazard:saw_2" or the checkpoint id.</summary>
        public string detail;
    }

    /// <summary>
    /// Gameplay-to-server event channel. Game scripts report what happened; the server decides
    /// what it means (invariant I5 - the game never interprets). Calls are no-ops in normal play
    /// beyond a list append, so the hooks are safe to leave in shipping code.
    /// </summary>
    public static class SimEvents
    {
        private static readonly List<SimEvent> pending = new List<SimEvent>();

        public static void ReportDeath(string cause)
        {
            Add(SimEventType.Death, string.IsNullOrEmpty(cause) ? "unknown" : cause);
        }

        public static void ReportCheckpoint(string id) => Add(SimEventType.Checkpoint, id);

        public static void ReportLevelEnd() => Add(SimEventType.LevelEnd, "finish");

        public static void ReportFruitCollected(string id) => Add(SimEventType.FruitCollected, id);

        public static void ReportEnemyKilled(string id) => Add(SimEventType.EnemyKilled, id);

        /// <summary>
        /// The name an event goes out under. Mapped explicitly rather than derived from the enum
        /// member: the wire format is snake_case, matching the terminals the client already
        /// switches on ("level_end"), and renaming a member must not silently change the protocol.
        /// </summary>
        public static string WireName(SimEventType type)
        {
            switch (type)
            {
                case SimEventType.Death: return "death";
                case SimEventType.Checkpoint: return "checkpoint";
                case SimEventType.LevelEnd: return "level_end";
                case SimEventType.FruitCollected: return "fruit_collected";
                case SimEventType.EnemyKilled: return "enemy_killed";
                default: return "unknown";
            }
        }

        private static void Add(SimEventType type, string detail)
        {
            if (!SimClock.ManualMode) return;
            pending.Add(new SimEvent { tick = SimClock.Tick, type = type, detail = detail });
            if (pending.Count > 4096) pending.RemoveRange(0, 2048);
        }

        /// <summary>Events recorded since the last <see cref="Clear"/>.</summary>
        public static IReadOnlyList<SimEvent> Pending => pending;

        public static void Clear() => pending.Clear();
    }

    /// <summary>Helpers for despawning in a way a reset can undo.</summary>
    public static class SimObjects
    {
        /// <summary>
        /// Removes an object from play. In agent mode, objects that were part of the scene are
        /// only deactivated, so a reset can bring them back exactly as they were; anything
        /// spawned at runtime is destroyed as usual.
        /// </summary>
        public static void Despawn(GameObject go)
        {
            if (go == null) return;

            if (SimClock.ManualMode)
            {
                SimBehaviour sim = go.GetComponent<SimBehaviour>();
                bool sceneObject = sim != null && !string.IsNullOrEmpty(sim.SortKey) && sim.SortKey[0] == 'A';
                if (sceneObject)
                {
                    go.SetActive(false);
                    return;
                }
            }

            Object.Destroy(go);
        }
    }
}
