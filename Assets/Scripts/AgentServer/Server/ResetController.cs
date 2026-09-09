using System.Collections.Generic;
using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>
    /// Implements invariant I3: a reset puts the player back at spawn (or a checkpoint) AND puts
    /// every dynamic object back into the state it had at that moment, then zeroes the tick
    /// counter. After a reset, replaying a plan produces an identical trace.
    ///
    /// Two things make that possible: every dynamic object is a <see cref="SimBehaviour"/> that
    /// can snapshot itself, and objects that "die" during play are only deactivated (see
    /// <see cref="SimObjects.Despawn"/>) so they can be brought back rather than rebuilt.
    /// </summary>
    public static class ResetController
    {
        private sealed class WorldSnapshot
        {
            public string id;
            public Vector3 spawnPosition;
            public Transform spawnTransform;
            public int score;
            public int fruitsCollected;
            public Dictionary<FruitType, int> fruitsCollectedByType;
            public List<KeyValuePair<SimBehaviour, SimObjectState>> objects =
                new List<KeyValuePair<SimBehaviour, SimObjectState>>();
        }

        private static WorldSnapshot spawnSnapshot;
        private static readonly Dictionary<string, WorldSnapshot> checkpointSnapshots =
            new Dictionary<string, WorldSnapshot>();

        private static readonly HashSet<int> initialObjectIds = new HashSet<int>();
        private static Transform initialSpawnTransform;
        private static string pendingCheckpointId;
        private static Transform pendingCheckpointTransform;
        private static Vector3 initialSpawnPosition;
        private static bool initialised;

        public static bool Initialised => initialised;

        /// <summary>Checkpoint the current run started from, or null when it started at spawn.</summary>
        public static string CurrentCheckpoint { get; private set; }

        /// <summary>Ids of the checkpoints whose world state has been captured this session.</summary>
        public static IEnumerable<string> KnownCheckpoints => checkpointSnapshots.Keys;

        /// <summary>
        /// Called once after the level has loaded and settled. Records identities, the pristine
        /// world state and the set of objects the scene shipped with.
        /// </summary>
        public static void Initialise()
        {
            SimRegistry.AssignSceneIdentities();

            initialObjectIds.Clear();
            Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Transform t in all)
                initialObjectIds.Add(t.gameObject.GetInstanceID());

            GameManager gm = GameManager.instance;
            initialSpawnTransform = gm != null ? gm.RespawnPointRef : null;
            initialSpawnPosition = initialSpawnTransform != null
                ? initialSpawnTransform.position
                : (gm != null && gm.player != null ? gm.player.transform.position : Vector3.zero);

            checkpointSnapshots.Clear();
            CurrentCheckpoint = null;
            pendingCheckpointId = null;
            pendingCheckpointTransform = null;
            spawnSnapshot = CaptureWorld("spawn", initialSpawnTransform, initialSpawnPosition);
            initialised = true;
        }

        /// <summary>
        /// Records the world exactly as it was when a checkpoint was touched. Without this,
        /// resuming from a checkpoint would restart with hazards at the wrong phase and the
        /// agent's verified prefix would stop reproducing.
        /// </summary>
        public static void CaptureCheckpoint(string id, Transform spawnPoint)
        {
            if (!initialised || string.IsNullOrEmpty(id)) return;
            checkpointSnapshots[id] = CaptureWorld(id, spawnPoint,
                spawnPoint != null ? spawnPoint.position : initialSpawnPosition);
            CurrentCheckpoint = id;
        }

        public static bool HasCheckpoint(string id) => checkpointSnapshots.ContainsKey(id);

        /// <summary>
        /// Queues a checkpoint capture for the end of the current tick. Checkpoints are triggered
        /// from inside the physics step, and a snapshot taken mid-step would restore the world to
        /// a state no tick boundary ever had.
        /// </summary>
        public static void RequestCheckpointCapture(string id, Transform spawnPoint)
        {
            if (!initialised || string.IsNullOrEmpty(id)) return;
            pendingCheckpointId = id;
            pendingCheckpointTransform = spawnPoint;
        }

        /// <summary>Called by the server after the physics step has completed.</summary>
        internal static void ProcessPendingCapture()
        {
            if (pendingCheckpointId == null) return;
            string id = pendingCheckpointId;
            Transform point = pendingCheckpointTransform;
            pendingCheckpointId = null;
            pendingCheckpointTransform = null;
            CaptureCheckpoint(id, point);
        }

        /// <summary>
        /// Restores the world. <paramref name="target"/> is "spawn" or "checkpoint:&lt;id&gt;".
        /// Returns false (with a reason) if the requested checkpoint has never been reached.
        /// </summary>
        public static bool Reset(string target, out string error)
        {
            error = null;
            if (!initialised)
            {
                error = "reset controller not initialised";
                return false;
            }

            WorldSnapshot snapshot = spawnSnapshot;
            string checkpoint = null;
            if (!string.IsNullOrEmpty(target) && target.StartsWith("checkpoint:"))
            {
                string id = target.Substring("checkpoint:".Length);
                if (!checkpointSnapshots.TryGetValue(id, out snapshot))
                {
                    error = "unknown checkpoint '" + id + "' (it has not been reached in this session)";
                    return false;
                }
                checkpoint = id;
            }
            CurrentCheckpoint = checkpoint;

            pendingCheckpointId = null;
            pendingCheckpointTransform = null;

            DestroyRuntimeObjects();
            RestoreWorld(snapshot);

            GameManager gm = GameManager.instance;
            if (gm != null)
            {
                gm.AgentRestoreProgress(snapshot.score, snapshot.fruitsCollected, snapshot.fruitsCollectedByType);
                if (snapshot.spawnTransform != null) gm.UpdateRespawnPosition(snapshot.spawnTransform);
            }

            SimRegistry.ResetSpawnCounter();
            SimEvents.Clear();
            SimClock.ResetTicks();

            SpawnPlayer(snapshot.spawnPosition);
            Physics2D.SyncTransforms();

            return true;
        }

        /// <summary>Instantiates the player prefab and hands it straight to the agent's control.</summary>
        public static Player SpawnPlayer(Vector3 position)
        {
            GameManager gm = GameManager.instance;
            if (gm == null) return null;

            if (gm.player != null)
                Object.DestroyImmediate(gm.player.gameObject);

            GameObject prefab = gm.PlayerPrefabRef;
            if (prefab == null)
            {
                Debug.LogError("[AgentServer] GameManager has no player prefab assigned.");
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
            Player player = instance.GetComponent<Player>();
            gm.player = player;

            Rigidbody2D body = instance.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                // Interpolation makes transform.position a render-time estimate between physics
                // steps; the controller raycasts from that transform, so leaving it on would put
                // frame timing into gameplay. Physics truth only, in agent mode.
                body.interpolation = RigidbodyInterpolation2D.None;
                body.linearVelocity = Vector2.zero;
                body.position = position;
            }

            // The respawn animation normally enables control through an animation event, which is
            // frame-timed. In agent mode control is handed over immediately instead.
            if (player != null)
            {
                player.RespawnFinished(true);
                // Push the new transform into the physics world and re-run the ground/wall rays,
                // so the state reported at tick 0 is real rather than "just constructed".
                Physics2D.SyncTransforms();
                player.RefreshCollisionState();
            }

            CameraFollow cam = Object.FindFirstObjectByType<CameraFollow>();
            if (cam != null && player != null) cam.target = player.transform;

            return player;
        }

        // ------------------------------------------------------------------ internals

        private static WorldSnapshot CaptureWorld(string id, Transform spawnTransform, Vector3 spawnPosition)
        {
            var snapshot = new WorldSnapshot
            {
                id = id,
                spawnTransform = spawnTransform,
                spawnPosition = spawnPosition
            };

            GameManager gm = GameManager.instance;
            if (gm != null)
            {
                snapshot.score = gm.score;
                snapshot.fruitsCollected = gm.fruitsCollected;
                // Copied, not aliased: the live dictionary keeps counting after the snapshot.
                snapshot.fruitsCollectedByType = new Dictionary<FruitType, int>(gm.fruitsCollectedByType);
            }

            SimBehaviour[] all = Object.FindObjectsByType<SimBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (SimBehaviour b in all)
            {
                if (b == null || b is Player) continue;              // the player is respawned, not restored
                if (!initialObjectIds.Contains(b.gameObject.GetInstanceID())) continue;  // runtime spawn
                snapshot.objects.Add(new KeyValuePair<SimBehaviour, SimObjectState>(b, b.Capture()));
            }

            return snapshot;
        }

        private static void RestoreWorld(WorldSnapshot snapshot)
        {
            foreach (var entry in snapshot.objects)
            {
                if (entry.Key == null) continue;
                entry.Key.Restore(entry.Value);
            }
        }

        /// <summary>
        /// Removes gameplay objects created since the level loaded - bullets, VFX, the current
        /// player. Anything the scene shipped with is restored instead, never destroyed, and
        /// non-gameplay runtime objects (procedural UI, the audio manager, the server itself) are
        /// deliberately left alone.
        /// </summary>
        private static void DestroyRuntimeObjects()
        {
            Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Transform t in all)
            {
                if (t == null) continue;
                GameObject go = t.gameObject;
                if (initialObjectIds.Contains(go.GetInstanceID())) continue;
                if (!go.scene.isLoaded || go.scene.name == "DontDestroyOnLoad") continue;
                if (go.GetComponent<AgentServer>() != null) continue;

                bool spawnedGameplayObject =
                    go.GetComponent<SimBehaviour>() != null ||
                    go.GetComponent<Player>() != null ||
                    go.name.EndsWith("(Clone)");

                if (!spawnedGameplayObject) continue;

                // Only destroy the root of a spawned hierarchy; children go with it.
                if (t.parent != null && !initialObjectIds.Contains(t.parent.gameObject.GetInstanceID())) continue;

                Object.DestroyImmediate(go);
            }
        }
    }
}
