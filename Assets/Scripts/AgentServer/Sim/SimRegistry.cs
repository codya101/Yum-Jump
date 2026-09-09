using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>
    /// Registry of every live <see cref="SimBehaviour"/>, kept in a deterministic order.
    ///
    /// Unity's own Update order between objects of the same type is unspecified and changes as
    /// objects are created and destroyed, which would make replays drift. In agent mode nothing
    /// runs off Unity's Update: this registry ticks every object in a stable, sorted order, so
    /// two runs of the same plan visit the same objects in the same sequence.
    /// </summary>
    public static class SimRegistry
    {
        private static readonly List<SimBehaviour> members = new List<SimBehaviour>();
        private static readonly List<SimBehaviour> tickBuffer = new List<SimBehaviour>();
        private static readonly Dictionary<string, int> idCounters = new Dictionary<string, int>();
        private static bool dirty;
        private static int spawnCounter;

        public static IReadOnlyList<SimBehaviour> All
        {
            get
            {
                SortIfNeeded();
                return members;
            }
        }

        internal static void Register(SimBehaviour b)
        {
            if (b == null || members.Contains(b)) return;

            if (b is Player)
            {
                // The player is destroyed and re-instantiated on every reset, so it gets a fixed
                // identity that sorts ahead of everything else: same id, same tick order, always.
                b.SortKey = "0player";
                b.SimId = "player";
            }
            else if (string.IsNullOrEmpty(b.SortKey))
            {
                // Registered after the initial scan => spawned at runtime (a bullet, a VFX).
                // The counter is zeroed on reset, so a replay hands out the same ids again.
                spawnCounter++;
                b.SortKey = "B" + spawnCounter.ToString("D6");
                b.SimId = ShortName(b.GetType()) + "@" + spawnCounter;
            }

            members.Add(b);
            dirty = true;
        }

        internal static void Unregister(SimBehaviour b)
        {
            if (members.Remove(b)) dirty = true;
        }

        /// <summary>
        /// Assigns stable ids/sort keys to everything already in the scene. Sorting by type and
        /// hierarchy path (not by instance id, which varies run to run) is what makes the ids
        /// reproducible between sessions.
        /// </summary>
        internal static void AssignSceneIdentities()
        {
            idCounters.Clear();
            spawnCounter = 0;

            SimBehaviour[] all = UnityEngine.Object.FindObjectsByType<SimBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Array.Sort(all, (x, y) =>
            {
                int c = string.CompareOrdinal(x.GetType().Name, y.GetType().Name);
                if (c != 0) return c;
                c = string.CompareOrdinal(HierarchyPath(x.transform), HierarchyPath(y.transform));
                if (c != 0) return c;
                c = x.transform.position.x.CompareTo(y.transform.position.x);
                if (c != 0) return c;
                return x.transform.position.y.CompareTo(y.transform.position.y);
            });

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is Player)
                {
                    all[i].SortKey = "0player";
                    all[i].SimId = "player";
                    continue;
                }
                all[i].SortKey = "A" + i.ToString("D5");
                all[i].SimId = NextId(all[i].GetType());
            }

            dirty = true;
        }

        /// <summary>Zeroes the runtime-spawn counter so ids repeat across replays.</summary>
        internal static void ResetSpawnCounter()
        {
            spawnCounter = 0;
        }

        /// <summary>Runs one tick of every registered object, in deterministic order.</summary>
        internal static void TickAll()
        {
            SortIfNeeded();

            // Copy first: a tick may enable/disable/spawn objects, which mutates the list.
            tickBuffer.Clear();
            tickBuffer.AddRange(members);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                SimBehaviour b = tickBuffer[i];
                if (b == null || !b.isActiveAndEnabled) continue;
                b.DriveTick();
            }
        }

        /// <summary>Refreshes derived velocities after the physics step.</summary>
        internal static void SampleVelocities()
        {
            for (int i = 0; i < members.Count; i++)
            {
                SimBehaviour b = members[i];
                if (b != null) b.SampleDerivedVelocity();
            }
        }

        private static void SortIfNeeded()
        {
            if (!dirty) return;
            members.RemoveAll(m => m == null);
            members.Sort((x, y) => string.CompareOrdinal(x.SortKey, y.SortKey));
            dirty = false;
        }

        private static string NextId(Type type)
        {
            string name = ShortName(type);
            idCounters.TryGetValue(name, out int n);
            n++;
            idCounters[name] = n;
            return name + "_" + n;
        }

        /// <summary>Human-readable prefix for object ids, so plans and logs stay legible.</summary>
        public static string ShortName(Type type)
        {
            switch (type.Name)
            {
                case "Trap_Saw": return "saw";
                case "AngryPig": return "pig";
                case "PlantBullet": return "bullet";
                case "FallingPlatform": return "platform";
                default: return type.Name.ToLowerInvariant();
            }
        }

        private static string HierarchyPath(Transform t)
        {
            StringBuilder sb = new StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null)
            {
                sb.Insert(0, "/").Insert(0, p.name);
                p = p.parent;
            }
            sb.Append('#').Append(t.GetSiblingIndex().ToString("D4"));
            return sb.ToString();
        }
    }
}
