using System.Collections;
using UnityEngine;
using YumJump.Agent;

/// <summary>
/// A walking shooter: patrols between waypoints like the pig (no chase state) and fires
/// a PlantBullet on a fixed metronome in whichever direction it is facing — so its body
/// language IS the threat: walking away means its shots fly away.
///
/// Movement is the AngryPig patrol pattern; firing is the Plant metronome pattern,
/// including the animation-event Fire() with the agent-mode tick fallback, so replays
/// put the bullet in the same place regardless of animation timing.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class Trunk : SimBehaviour, IStompable
{
    [Header("Patrol")]
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private float walkSpeed = 1.5f;
    [SerializeField] private float idleTimeAtWaypoint = 1.5f;
    [SerializeField] private float waypointReachedDistance = 0.2f;

    [Header("Attack")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float attackInterval = 2.5f;
    [SerializeField] private float bulletSpeed = 6f;
    [Tooltip("How long the trunk stands still while shooting, so the attack anim isn't " +
             "played over a moonwalk.")]
    [SerializeField] private float attackPause = 0.5f;

    [Header("Stomp")]
    [SerializeField] private float stompBounceForce = 14f;

    [Header("VFX")]
    [SerializeField] private GameObject deathVFX;

    [Header("Detection Raycasts")]
    [SerializeField] private float wallCheckDistance = 0.5f;
    [SerializeField] private float edgeCheckDistance = 1f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Audio")]
    [Tooltip("Loudness of the shot at full SFX volume, before the SFX slider scales it.")]
    [SerializeField, Range(0f, 1f)] private float popVolume = 1f;
    [Tooltip("Distance (world units) within which the shot is at full volume.")]
    [SerializeField] private float minDistance = 6f;
    [Tooltip("Distance (world units) beyond which the shot is silent.")]
    [SerializeField] private float maxDistance = 18f;
    private const string PopClip = "Audio/SFX/SFX_Pop";
    private static AudioClip popClip;
    private AudioSource popSource;

    private Rigidbody2D rb;
    private Animator anim;
    private SpriteRenderer sr;
    private CapsuleCollider2D col;

    private Vector3[] waypointPositions;
    private int currentWaypointIndex;
    private int facingDir = -1; // -1 = left (the sprite's un-flipped facing), 1 = right
    private float nextAttackTime;
    private float attackPauseUntil;
    private bool isDead;
    private int firedOnTick = -1;
    private Coroutine idleCoroutine;
    private const float DespawnDelay = 0.5f;

    private enum State { Patrolling, Idle }
    private State state = State.Patrolling;

    public override SimKind Kind => SimKind.Enemy;

    private void Awake()
    {
        rb  = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();
        anim = GetComponentInChildren<Animator>();
        sr   = GetComponentInChildren<SpriteRenderer>();

        SetupAudio();
    }

    // 3D one-shot source, same setup as the plant: shots fall off with distance so far-away
    // trunks are inaudible. The clip is cached statically across all trunks.
    private void SetupAudio()
    {
        if (popClip == null) popClip = Resources.Load<AudioClip>(PopClip);

        popSource = gameObject.AddComponent<AudioSource>();
        popSource.playOnAwake = false;
        popSource.spatialBlend = 1f;
        popSource.rolloffMode = AudioRolloffMode.Linear;
        popSource.minDistance = minDistance;
        popSource.maxDistance = maxDistance;
        popSource.dopplerLevel = 0f;
    }

    private void Start()
    {
        SnapshotWaypointPositions();
        nextAttackTime = SimClock.Time + attackInterval;
    }

    private void SnapshotWaypointPositions()
    {
        waypointPositions = new Vector3[waypoints.Length];
        for (int i = 0; i < waypoints.Length; i++)
            waypointPositions[i] = waypoints[i].position;
    }

    // ─── Sim state ────────────────────────────────────────────────────────────

    /// <summary>Patrol/attack state that a transform-only restore cannot recover.</summary>
    private sealed class TrunkState
    {
        public int waypointIndex;
        public State state;
        public int facingDir;
        public float nextAttackTime;
        public float attackPauseUntil;
        public bool isDead;
    }

    protected override object CaptureExtra() => new TrunkState
    {
        waypointIndex = currentWaypointIndex,
        state = state,
        facingDir = facingDir,
        nextAttackTime = nextAttackTime,
        attackPauseUntil = attackPauseUntil,
        isDead = isDead
    };

    protected override void RestoreExtra(object extra)
    {
        if (extra is TrunkState s)
        {
            currentWaypointIndex = s.waypointIndex;
            state = s.state;
            nextAttackTime = s.nextAttackTime;
            attackPauseUntil = s.attackPauseUntil;
            isDead = s.isDead;
            SetFacing(s.facingDir);
        }
        idleCoroutine = null;
    }

    public override void DescribeTo(System.Collections.Generic.Dictionary<string, object> fields)
    {
        fields["facing"] = facingDir < 0 ? "left" : "right";
    }

    // ─── Tick ─────────────────────────────────────────────────────────────────

    protected override void SimTick()
    {
        if (isDead) return;

        HandleAttack();

        bool pausedToShoot = SimClock.Time < attackPauseUntil;

        if (pausedToShoot)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else if (state == State.Patrolling)
        {
            HandlePatrol();
        }

        anim.SetBool("isWalking", state == State.Patrolling && !pausedToShoot);
    }

    // ─── Patrol (AngryPig pattern, minus chase) ──────────────────────────────

    private void HandlePatrol()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        Vector3 target = waypointPositions[currentWaypointIndex];

        // Compare x only: the trunk moves horizontally, and its feet-level pivot rarely
        // matches the waypoint transform's height, so a 2D distance check could hover
        // just above the threshold forever (rapid flip-flop around the target x).
        float dx = target.x - transform.position.x;

        if (Mathf.Abs(dx) < waypointReachedDistance)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            idleCoroutine = StartCoroutine(IdleAtWaypoint());
            return;
        }

        float dir = Mathf.Sign(dx);

        if (!CanMoveInDirection(dir))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            idleCoroutine = StartCoroutine(IdleAtWaypoint());
            return;
        }

        // Face before moving so a shot fired this tick leaves the way the body points.
        SetFacing((int)dir);
        rb.linearVelocity = new Vector2(dir * walkSpeed, rb.linearVelocity.y);
    }

    private IEnumerator IdleAtWaypoint()
    {
        state = State.Idle;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        yield return SimClock.Wait(idleTimeAtWaypoint);

        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        state = State.Patrolling;
    }

    private bool CanMoveInDirection(float dir)
    {
        // Wall check — horizontal ray slightly above ground level
        Vector2 wallOrigin = (Vector2)transform.position + Vector2.up * 0.3f;
        if (Physics2D.Raycast(wallOrigin, Vector2.right * dir, wallCheckDistance, groundLayer))
            return false;

        // Edge check — downward ray slightly ahead of feet
        Vector2 edgeOrigin = (Vector2)transform.position + Vector2.right * (dir * 0.3f);
        if (!Physics2D.Raycast(edgeOrigin, Vector2.down, edgeCheckDistance, groundLayer))
            return false;

        return true;
    }

    private void SetFacing(int dir)
    {
        if (dir == 0 || dir == facingDir) return;

        facingDir = dir;
        sr.flipX = dir > 0;

        if (firePoint != null)
        {
            Vector3 lp = firePoint.localPosition;
            lp.x = Mathf.Abs(lp.x) * dir;
            firePoint.localPosition = lp;
        }
    }

    // ─── Attack (Plant pattern) ──────────────────────────────────────────────

    private void HandleAttack()
    {
        if (SimClock.Time < nextAttackTime) return;

        anim.SetTrigger("attack");
        nextAttackTime = SimClock.Time + attackInterval;
        attackPauseUntil = SimClock.Time + attackPause;

        // Firing is normally driven by an animation event, and animation timing runs off the
        // frame clock rather than the tick clock. In agent mode the shot leaves the tick itself,
        // so a replay puts the bullet in the same place; the animation still plays for the look.
        if (SimClock.ManualMode) Fire();
    }

    /// <summary>Animation event in normal play; called straight from the tick in agent mode.</summary>
    public void Fire()
    {
        if (isDead || bulletPrefab == null) return;
        // Stops the animation event from doubling the tick-driven shot.
        if (SimClock.ManualMode && firedOnTick == SimClock.Tick) return;
        firedOnTick = SimClock.Tick;

        Vector3 origin = firePoint != null ? firePoint.position : transform.position;
        GameObject bullet = Instantiate(bulletPrefab, origin, Quaternion.identity);
        PlantBullet pb = bullet.GetComponent<PlantBullet>();
        if (pb != null) pb.Launch(facingDir < 0 ? Vector2.left : Vector2.right, bulletSpeed);

        if (popSource != null && popClip != null)
            popSource.PlayOneShot(popClip, popVolume * AudioManager.Instance.EffectiveSfxVolume);
    }

    // ─── Damage ──────────────────────────────────────────────────────────────

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        Player playerComp = collision.collider.GetComponent<Player>();
        if (playerComp != null)
        {
            playerComp.Die("enemy:" + SimId);
            GameManager.instance.RespawnPlayer();
        }
    }

    // Called by EnemyHeadTrigger child script
    public void OnHeadStomp(Collider2D collision)
    {
        if (isDead) return;

        Player player = collision.GetComponent<Player>();
        if (player == null) return;

        Rigidbody2D playerRb = collision.GetComponent<Rigidbody2D>();
        if (playerRb == null || playerRb.linearVelocity.y >= 0) return;

        // Play on the very frame the stomp is registered, before any bounce/anim work.
        AudioManager.Instance.PlayEnemyKicked();

        player.Bounce(stompBounceForce);
        isDead = true;
        StopAllCoroutines();
        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;
        col.enabled = false;
        anim.SetTrigger("isHit");
        StartCoroutine(DespawnAfterHit());
    }

    private IEnumerator DespawnAfterHit()
    {
        yield return SimClock.Wait(DespawnDelay);
        Instantiate(deathVFX, transform.position, Quaternion.identity);
        SimEvents.ReportEnemyKilled(SimId);
        SimObjects.Despawn(gameObject);
    }
}
