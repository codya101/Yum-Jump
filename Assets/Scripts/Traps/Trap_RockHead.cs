using UnityEngine;
using YumJump.Agent;

/// <summary>
/// Ceiling crusher. Hangs armed at its placed position until the player enters the
/// detection column beneath it, then: a short blink/shake telegraph (the dodge cue,
/// never skipped) -> a fast slam to the floor -> a grounded rest (during which it is
/// ordinary terrain: bullets break on it and the player can stand on it) -> a slow
/// rise back to the armed position.
///
/// It only kills through the KillStrip child (a DamageTrigger under the bottom face)
/// which is enabled during the slam alone — the top and sides are always-safe solid
/// collider, so riding a grounded head up is legal by design.
///
/// Runs as a SimTick state machine on SimClock time with no coroutines, so agent-mode
/// capture/restore can drop back into any phase mid-motion.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class Trap_RockHead : SimBehaviour
{
    private enum Phase { Armed, Telegraph, Slamming, Grounded, Rising }

    [Header("Detection")]
    [Tooltip("Width of the trigger column under the head. Slightly narrower than the head " +
             "so grazing the edge doesn't trigger it; widen per-instance for a demonstration " +
             "crusher that fires when the player walks PAST it.")]
    [SerializeField] private float detectionWidth = 2.2f;
    [Tooltip("How far below the bottom face the trigger column reaches. 0 = automatic: " +
             "raycast to the floor at Start.")]
    [SerializeField] private float detectionDepth = 0f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Cycle")]
    [Tooltip("Blink/shake time between detecting the player and committing to the slam. " +
             "Keep at ~0.2s — this is the player's escape window.")]
    [SerializeField] private float telegraphDuration = 0.2f;
    [SerializeField] private float slamSpeed = 20f;
    [Tooltip("Rest time on the floor after impact — the window where the head is cover " +
             "and the player crosses or climbs it.")]
    [SerializeField] private float groundedDuration = 0.8f;
    [SerializeField] private float riseSpeed = 2.5f;

    [Header("Kill")]
    [Tooltip("Trigger collider just under the bottom face, with a DamageTrigger alongside " +
             "it. Enabled only while slamming.")]
    [SerializeField] private Collider2D slamKillCollider;

    [Header("Audio")]
    [Tooltip("Loudness of the slam impact at full SFX volume, before the SFX slider scales it.")]
    [SerializeField, Range(0f, 1f)] private float slamVolume = 0.35f;
    [Tooltip("Distance (world units) within which the impact is at full volume.")]
    [SerializeField] private float minDistance = 10f;
    [Tooltip("Distance (world units) beyond which the impact is silent.")]
    [SerializeField] private float maxDistance = 22f;
    private const string SlamClip = "Audio/SFX/SFX_RockHead_Slam";
    private static AudioClip slamClip;
    private AudioSource slamSource;

    private const float Skin = 0.02f;          // raycast clearance below the solid collider
    private const float FallbackDepth = 5f;    // detection depth if the floor raycast finds nothing

    private Rigidbody2D rb;
    private BoxCollider2D col;
    private Animator anim;
    private Transform player;

    private Vector2 armedPosition;
    private float autoDepth = FallbackDepth;
    private Phase phase = Phase.Armed;
    private float phaseEndTime;

    public override SimKind Kind => SimKind.Hazard;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<BoxCollider2D>();
        anim = GetComponentInChildren<Animator>();

        SetupAudio();
    }

    // 3D one-shot impact, same setup as the saw/fire audio. Cached statically across heads.
    private void SetupAudio()
    {
        if (slamClip == null) slamClip = Resources.Load<AudioClip>(SlamClip);
        if (slamClip == null) return; // clip not added to Resources yet; stay silent

        slamSource = gameObject.AddComponent<AudioSource>();
        slamSource.clip = slamClip;
        slamSource.loop = false;
        slamSource.playOnAwake = false;
        slamSource.spatialBlend = 1f;
        slamSource.rolloffMode = AudioRolloffMode.Linear;
        slamSource.minDistance = minDistance;
        slamSource.maxDistance = maxDistance;
        slamSource.dopplerLevel = 0f;
    }

    private void Start()
    {
        armedPosition = rb.position;
        autoDepth = MeasureDepthToFloor();
        FindPlayer();

        if (slamKillCollider != null)
            slamKillCollider.enabled = false;
        SetAnim("Idle");
    }

    private float MeasureDepthToFloor()
    {
        Vector2 origin = new Vector2(rb.position.x, col.bounds.min.y - Skin);
        RaycastHit2D hit = FirstGroundHitBelow(origin, 50f);
        return hit.collider != null ? hit.distance : FallbackDepth;
    }

    // Downward ground raycast that skips this trap's own colliders. The enabled
    // KillStrip straddles the bottom face, so a plain raycast starting just below the
    // face begins inside it — and if that child sits on the Ground layer, the slam
    // "lands" instantly at distance zero without ever moving.
    private RaycastHit2D FirstGroundHitBelow(Vector2 origin, float distance)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.down, distance, groundLayer);
        foreach (RaycastHit2D h in hits)
        {
            if (h.collider.attachedRigidbody == rb) continue;
            return h;
        }
        return default;
    }

    // ─── Sim state ────────────────────────────────────────────────────────────

    /// <summary>Cycle phase and geometry that a transform-only restore cannot recover.</summary>
    private sealed class RockHeadState
    {
        public Phase phase;
        public float phaseEndTime;
        public Vector2 armedPosition;
        public float autoDepth;
    }

    protected override object CaptureExtra() => new RockHeadState
    {
        phase = phase,
        phaseEndTime = phaseEndTime,
        armedPosition = armedPosition,
        autoDepth = autoDepth
    };

    protected override void RestoreExtra(object extra)
    {
        if (extra is RockHeadState s)
        {
            phase = s.phase;
            phaseEndTime = s.phaseEndTime;
            armedPosition = s.armedPosition;
            autoDepth = s.autoDepth;
        }

        if (slamKillCollider != null)
            slamKillCollider.enabled = phase == Phase.Slamming;
        FindPlayer();
    }

    public override void DescribeTo(System.Collections.Generic.Dictionary<string, object> fields)
    {
        fields["phase"] = phase.ToString().ToLowerInvariant();
    }

    // ─── Tick ─────────────────────────────────────────────────────────────────

    protected override void SimTick()
    {
        switch (phase)
        {
            case Phase.Armed:
                if (player == null) { FindPlayer(); break; }
                if (PlayerInDetectionColumn())
                {
                    phase = Phase.Telegraph;
                    phaseEndTime = SimClock.Time + telegraphDuration;
                    SetAnim("Blink");
                }
                break;

            case Phase.Telegraph:
                if (SimClock.Time >= phaseEndTime)
                {
                    phase = Phase.Slamming;
                    if (slamKillCollider != null)
                        slamKillCollider.enabled = true;
                }
                break;

            case Phase.Slamming:
                StepSlam();
                break;

            case Phase.Grounded:
                if (SimClock.Time >= phaseEndTime)
                {
                    phase = Phase.Rising;
                    SetAnim("Idle");
                }
                break;

            case Phase.Rising:
                StepRise();
                break;
        }
    }

    private void StepSlam()
    {
        // Velocity-driven like the pig and trunk: physics integrates the velocity at its
        // own fixed cadence, so the fall speed is identical at every frame rate.
        // (MovePosition per render frame gets overwritten between physics steps — it fell
        // slower the higher the refresh rate, which read as inconsistent speed.)
        // Look ahead one full physics step so the stop check can never tunnel past the floor.
        float lookAhead = slamSpeed * SimClock.FixedDelta + Skin;

        Vector2 origin = new Vector2(rb.position.x, col.bounds.min.y - Skin);
        RaycastHit2D hit = FirstGroundHitBelow(origin, lookAhead);

        if (hit.collider != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(rb.position + Vector2.down * Mathf.Max(0f, hit.distance - Skin));
            Impact();
            return;
        }

        rb.linearVelocity = Vector2.down * slamSpeed;
    }

    private void Impact()
    {
        phase = Phase.Grounded;
        phaseEndTime = SimClock.Time + groundedDuration;

        if (slamKillCollider != null)
            slamKillCollider.enabled = false;
        SetAnim("Hit");

        if (slamSource != null && AudioManager.Instance != null)
        {
            slamSource.volume = slamVolume * AudioManager.Instance.EffectiveSfxVolume;
            slamSource.Play();
        }
    }

    private void StepRise()
    {
        float remaining = armedPosition.y - rb.position.y;

        // Arrive once the next physics step would overshoot the armed position.
        if (remaining <= riseSpeed * SimClock.FixedDelta)
        {
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(armedPosition);
            phase = Phase.Armed;
            return;
        }

        rb.linearVelocity = Vector2.up * riseSpeed;
    }

    // ─── Detection ────────────────────────────────────────────────────────────

    // Pure-math box check against the player transform (the bat's approach): cheap,
    // deterministic, and it never fires physics queries mid-tick.
    private bool PlayerInDetectionColumn()
    {
        float depth = detectionDepth > 0f ? detectionDepth : autoDepth;
        Vector2 bottomFace = new Vector2(rb.position.x, col.bounds.min.y);
        Vector2 center = bottomFace + Vector2.down * (depth * 0.5f);
        Vector2 delta = (Vector2)player.position - center;

        return Mathf.Abs(delta.x) <= detectionWidth * 0.5f &&
               Mathf.Abs(delta.y) <= depth * 0.5f;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetAnim(string stateName)
    {
        if (anim != null)
            anim.Play(stateName, 0, 0f);
    }

    private void FindPlayer()
    {
        GameObject obj = GameObject.FindGameObjectWithTag("Player");
        if (obj != null) player = obj.transform;
    }

    private void OnValidate()
    {
        detectionWidth = Mathf.Max(0.1f, detectionWidth);
        detectionDepth = Mathf.Max(0f, detectionDepth);
        telegraphDuration = Mathf.Max(0.05f, telegraphDuration);
        slamSpeed = Mathf.Max(0.5f, slamSpeed);
        groundedDuration = Mathf.Max(0.05f, groundedDuration);
        riseSpeed = Mathf.Max(0.1f, riseSpeed);
    }

    // Yellow column = detection zone, drawn from the bottom face toward the floor so
    // trigger coverage can be checked while placing, before entering Play mode.
    private void OnDrawGizmos()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        float depth = detectionDepth > 0f ? detectionDepth : FallbackDepth;
        if (Application.isPlaying) depth = detectionDepth > 0f ? detectionDepth : autoDepth;

        Vector2 origin = Application.isPlaying ? (Vector2)rb.position : (Vector2)transform.position;
        Vector2 bottomFace = new Vector2(origin.x, box.bounds.min.y);
        Vector2 center = bottomFace + Vector2.down * (depth * 0.5f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, new Vector3(detectionWidth, depth, 0f));
    }
}
