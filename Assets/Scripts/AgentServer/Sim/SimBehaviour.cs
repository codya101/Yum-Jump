using System.Collections.Generic;
using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>Taxonomy reported to the agent for every dynamic object.</summary>
    public enum SimKind
    {
        None,
        Player,
        MovingPlatform,
        Hazard,
        Enemy,
        Collectible,
        Trigger
    }

    /// <summary>
    /// Base class for every gameplay object whose behaviour has to be replayable.
    ///
    /// It gives three things to the agent server:
    ///   1. tick-driven logic - <see cref="SimTick"/> replaces Update(). In normal play Unity's
    ///      Update calls it once per frame; in agent mode <see cref="SimRegistry"/> calls it once
    ///      per tick, in a deterministic order.
    ///   2. snapshot/restore - so reset (invariant I3) can put the world back exactly as it was,
    ///      including at a checkpoint.
    ///   3. observability - kind, position, velocity and AABB for the dynamics list.
    ///
    /// Objects that hide Unity messages (Awake/Start/OnEnable/OnDisable) must override the
    /// protected virtuals here and call base, otherwise registration silently stops working.
    /// </summary>
    public abstract class SimBehaviour : MonoBehaviour
    {
        private Rigidbody2D cachedBody;
        private Collider2D[] cachedColliders;
        private Vector2 previousPosition;
        private Vector2 derivedVelocity;
        private bool hasPreviousPosition;

        /// <summary>Stable identity across resets; assigned by <see cref="SimRegistry"/>.</summary>
        public string SimId { get; internal set; }

        /// <summary>Deterministic tick/report ordering key; assigned by <see cref="SimRegistry"/>.</summary>
        internal string SortKey { get; set; }

        /// <summary>How this object is described to the agent. None keeps it out of the list.</summary>
        public virtual SimKind Kind => SimKind.None;

        public Rigidbody2D Body
        {
            get
            {
                if (cachedBody == null) cachedBody = GetComponent<Rigidbody2D>();
                return cachedBody;
            }
        }

        /// <summary>Physics-truth position (never the interpolated render transform).</summary>
        public Vector2 SimPosition => Body != null ? Body.position : (Vector2)transform.position;

        /// <summary>
        /// Velocity of the object. Rigidbody movers report the body velocity; transform movers
        /// (saws, for instance) report the per-tick position delta, which is what an observer
        /// can actually measure.
        /// </summary>
        public Vector2 SimVelocity
        {
            get
            {
                Rigidbody2D body = Body;
                if (body != null && body.bodyType != RigidbodyType2D.Static)
                    return body.linearVelocity;
                return derivedVelocity;
            }
        }

        /// <summary>Half-extents of the object's collision footprint, in world units.</summary>
        public Vector2 SimHalfExtents
        {
            get
            {
                Collider2D[] cols = Colliders;
                bool any = false;
                Bounds b = new Bounds(transform.position, Vector3.zero);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] == null || !cols[i].enabled) continue;
                    if (!any) { b = cols[i].bounds; any = true; }
                    else b.Encapsulate(cols[i].bounds);
                }
                return any ? (Vector2)b.extents : Vector2.zero;
            }
        }

        protected Collider2D[] Colliders
        {
            get
            {
                if (cachedColliders == null) cachedColliders = GetComponentsInChildren<Collider2D>(true);
                return cachedColliders;
            }
        }

        protected virtual void OnEnable()
        {
            SimRegistry.Register(this);
            previousPosition = SimPosition;
            hasPreviousPosition = true;
            derivedVelocity = Vector2.zero;
        }

        protected virtual void OnDisable()
        {
            SimRegistry.Unregister(this);
        }

        protected virtual void Update()
        {
            // In agent mode SimRegistry drives the tick so that ordering, cadence and physics
            // stepping are all under our control. Unity's own Update must stay out of it.
            if (SimClock.ManualMode) return;
            SimTick();
        }

        /// <summary>One step of this object's behaviour. Formerly the body of Update().</summary>
        protected abstract void SimTick();

        internal void DriveTick()
        {
            SimTick();
        }

        /// <summary>Called by the driver after physics, so transform movers get a real velocity.</summary>
        internal void SampleDerivedVelocity()
        {
            Vector2 p = SimPosition;
            derivedVelocity = hasPreviousPosition ? (p - previousPosition) / SimClock.FixedDelta : Vector2.zero;
            previousPosition = p;
            hasPreviousPosition = true;
        }

        // ---------------------------------------------------------------- snapshot / restore

        /// <summary>Captures everything needed to put this object back exactly where it was.</summary>
        public SimObjectState Capture()
        {
            SimObjectState s = new SimObjectState
            {
                active = gameObject.activeSelf,
                position = transform.position,
                rotation = transform.rotation,
                scale = transform.localScale,
                extra = CaptureExtra()
            };

            Rigidbody2D body = Body;
            if (body != null)
            {
                s.hasBody = true;
                s.bodyType = body.bodyType;
                s.gravityScale = body.gravityScale;
                s.simulated = body.simulated;
                s.velocity = body.bodyType == RigidbodyType2D.Static ? Vector2.zero : body.linearVelocity;
                s.angularVelocity = body.bodyType == RigidbodyType2D.Static ? 0f : body.angularVelocity;
            }

            Collider2D[] cols = Colliders;
            s.collidersEnabled = new bool[cols.Length];
            for (int i = 0; i < cols.Length; i++)
                s.collidersEnabled[i] = cols[i] != null && cols[i].enabled;

            return s;
        }

        /// <summary>Restores a snapshot produced by <see cref="Capture"/>.</summary>
        public void Restore(SimObjectState s)
        {
            StopAllCoroutines();

            gameObject.SetActive(s.active);
            transform.position = s.position;
            transform.rotation = s.rotation;
            transform.localScale = s.scale;

            Rigidbody2D body = Body;
            if (body != null && s.hasBody)
            {
                body.bodyType = s.bodyType;
                body.gravityScale = s.gravityScale;
                body.simulated = s.simulated;
                if (body.bodyType != RigidbodyType2D.Static)
                {
                    body.linearVelocity = s.velocity;
                    body.angularVelocity = s.angularVelocity;
                }
                body.position = s.position;
                body.rotation = s.rotation.eulerAngles.z;
            }

            Collider2D[] cols = Colliders;
            if (s.collidersEnabled != null)
            {
                for (int i = 0; i < cols.Length && i < s.collidersEnabled.Length; i++)
                    if (cols[i] != null) cols[i].enabled = s.collidersEnabled[i];
            }

            previousPosition = SimPosition;
            derivedVelocity = Vector2.zero;
            hasPreviousPosition = true;

            RestoreExtra(s.extra);
        }

        /// <summary>Script-private state (timers, indices, flags) that Capture cannot see.</summary>
        protected virtual object CaptureExtra() => null;

        protected virtual void RestoreExtra(object extra) { }

        /// <summary>Extra key/value pairs surfaced to the agent in the dynamics list.</summary>
        public virtual void DescribeTo(Dictionary<string, object> fields) { }
    }

    /// <summary>Opaque per-object snapshot. Held by ResetController for spawn and checkpoints.</summary>
    public sealed class SimObjectState
    {
        public bool active;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public bool hasBody;
        public RigidbodyType2D bodyType;
        public float gravityScale;
        public bool simulated;
        public Vector2 velocity;
        public float angularVelocity;
        public bool[] collidersEnabled;
        public object extra;
    }
}
