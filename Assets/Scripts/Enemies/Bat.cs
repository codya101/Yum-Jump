using System.Collections;
using UnityEngine;
using YumJump.Agent;

/// <summary>
/// Ceiling bat. Hangs upside-down at its perch (harmless) until the player enters
/// the detection box below/beside it. Then: a short wings-out screech telegraph —
/// the player's dodge cue, never skipped — followed by an arcing dive toward where
/// the player WAS at the moment the dive began (a snapshot, not homing). After the
/// dive it loops back to the perch; that whole return is stomp-vulnerable via the
/// HeadTrigger child (same EnemyHeadTrigger relay as AngryPig). Back at the perch
/// it cools down before it can dive again.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class Bat : SimBehaviour, IStompable
{
    [Header("Detection")]
    [Tooltip("Size of the box (world units) the player must enter to trigger a dive. Drawn as a yellow gizmo.")]
    [SerializeField] private Vector2 detectionBoxSize = new Vector2(7f, 5f);
    [Tooltip("Offset of the detection box from the perch — default hangs it below the bat.")]
    [SerializeField] private Vector2 detectionBoxOffset = new Vector2(0f, -3f);
    [SerializeField] private LayerMask sightBlockingLayers;

    [Header("Telegraph")]
    [Tooltip("Wings-out screech time before the dive commits. Keep at 0.2-0.3s — this is the player's dodge window.")]
    [SerializeField] private float alertDuration = 0.25f;

    [Header("Dive")]
    [SerializeField] private float diveSpeed = 10f;
    [Tooltip("How far the dive path bows below the straight line to the target, giving the swoop its arc.")]
    [SerializeField] private float diveArcDepth = 1.5f;

    [Header("Recover")]
    [Tooltip("Time to loop back up to the perch after a dive — the stomp-vulnerable window.")]
    [SerializeField] private float recoverDuration = 1.75f;
    [Tooltip("How far the return path swings below the straight line back to the perch.")]
    [SerializeField] private float recoverArcDepth = 1f;
    [Tooltip("Rest time at the perch before the bat can dive again.")]
    [SerializeField] private float diveCooldown = 1.5f;

    [Header("Stomp")]
    [SerializeField] private float stompBounceForce = 14f;
    [Tooltip("Recover is always stompable. Turn this on to allow stomps mid-dive too.")]
    [SerializeField] private bool stompableWhileDiving = false;

    [Header("VFX")]
    [SerializeField] private GameObject deathVFX;

    private Rigidbody2D rb;
    private CapsuleCollider2D col;
    private Animator anim;
    private SpriteRenderer sr;
    private Transform player;

    private Vector2 perchPosition;
    private float nextDiveTime;
    private bool isDead;
    private const float DespawnDelay = 0.5f;

    private enum State { Perched, Alert, Diving, Recovering }
    private State state = State.Perched;

    // Animator "state" values (must match the transition conditions in Bat_AC,
    // built by BatSetup). These deliberately diverge from the logic states: the
    // flying pose covers the dive AND most of the return flight, and the
    // ceiling-in pose only plays once for the final landing — looping it over the
    // whole return made the sprite flip-flop between fly and hang poses.
    private const int AnimPerched = 0;
    private const int AnimAlert   = 1;
    private const int AnimFlying  = 2;
    private const int AnimLanding = 3;

    private float landingAnimLength = 0.4f; // batCeilingIn clip length, read in Awake

    private void Awake()
    {
        rb  = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();
        anim = GetComponentInChildren<Animator>();
        sr   = GetComponentInChildren<SpriteRenderer>();

        // The landing pose starts this long before the bat reaches its perch, so
        // the wing-fold completes right as it reattaches to the ceiling.
        foreach (AnimationClip clip in anim.runtimeAnimatorController.animationClips)
        {
            if (clip.name == "batCeilingIn")
            {
                landingAnimLength = clip.length;
                break;
            }
        }
    }

    private void Start()
    {
        perchPosition = rb.position;
        FindPlayer();
    }

    public override SimKind Kind => SimKind.Enemy;

    /// <summary>Dive cooldown, flight state and perch - none of it lives in the transform.</summary>
    private sealed class BatState
    {
        public float nextDiveTime;
        public State state;
        public bool isDead;
        public Vector2 perch;
    }

    protected override object CaptureExtra() =>
        new BatState { nextDiveTime = nextDiveTime, state = state, isDead = isDead, perch = perchPosition };

    protected override void RestoreExtra(object extra)
    {
        if (extra is BatState s)
        {
            nextDiveTime = s.nextDiveTime;
            state = s.state;
            isDead = s.isDead;
            perchPosition = s.perch;
        }
        FindPlayer();
    }

    protected override void SimTick()
    {
        if (isDead) return;

        if (player == null)
        {
            FindPlayer();
            return;
        }

        if (state == State.Perched && SimClock.Time >= nextDiveTime && PlayerInDetectionBox())
            StartCoroutine(AttackSequence());
    }

    // ─── Attack sequence ──────────────────────────────────────────────────────

    private IEnumerator AttackSequence()
    {
        // Telegraph — the non-negotiable dodge cue: screech + wings out, THEN commit.
        state = State.Alert;
        SetAnimState(AnimAlert);
        AudioManager.Instance.PlayBatScreech();
        if (player != null)
            FaceTravelDirection(player.position.x - rb.position.x);
        yield return SimClock.Wait(alertDuration);

        // Dive — arc to where the player is right now. Snapshot, not homing: the
        // player dodges by moving during the dive.
        state = State.Diving;
        SetAnimState(AnimFlying);
        Vector2 start = rb.position;
        Vector2 target = player != null ? (Vector2)player.position : start + Vector2.down * 2f;
        Vector2 diveControl = (start + target) * 0.5f + Vector2.down * diveArcDepth;
        float diveDuration = Mathf.Max(0.15f, Vector2.Distance(start, target) / diveSpeed);
        yield return FlyAlongArc(start, diveControl, target, diveDuration);

        // Recover — loop back up to the perch, stompable the whole way. The flying
        // anim keeps playing; only the final approach folds into the landing pose.
        state = State.Recovering;
        Vector2 returnControl = (rb.position + perchPosition) * 0.5f + Vector2.down * recoverArcDepth;
        yield return FlyAlongArc(rb.position, returnControl, perchPosition, recoverDuration,
                                 landingAnimLead: landingAnimLength);

        rb.MovePosition(perchPosition);
        state = State.Perched;
        SetAnimState(AnimPerched);
        nextDiveTime = SimClock.Time + diveCooldown;
    }

    /// <summary>Flies a quadratic bezier from <paramref name="from"/> to
    /// <paramref name="to"/> (bowed toward <paramref name="control"/>) over
    /// <paramref name="duration"/>, easing in and out. If
    /// <paramref name="landingAnimLead"/> is set, the landing pose is triggered
    /// that long before arrival.</summary>
    private IEnumerator FlyAlongArc(Vector2 from, Vector2 control, Vector2 to, float duration,
                                    float landingAnimLead = -1f)
    {
        float elapsed = 0f;
        Vector2 prev = from;

        while (elapsed < duration)
        {
            elapsed += SimClock.FixedDelta;

            if (landingAnimLead >= 0f && duration - elapsed <= landingAnimLead)
            {
                SetAnimState(AnimLanding);
                landingAnimLead = -1f; // trigger once
            }
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            Vector2 next = QuadBezier(from, control, to, t);
            rb.MovePosition(next);
            FaceTravelDirection(next.x - prev.x);
            prev = next;
            yield return SimClock.WaitStep();
        }
    }

    private static Vector2 QuadBezier(Vector2 a, Vector2 control, Vector2 b, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * control + t * t * b;
    }

    // ─── Detection ────────────────────────────────────────────────────────────

    private bool PlayerInDetectionBox()
    {
        Vector2 center = perchPosition + detectionBoxOffset;
        Vector2 delta = (Vector2)player.position - center;

        if (Mathf.Abs(delta.x) > detectionBoxSize.x * 0.5f ||
            Mathf.Abs(delta.y) > detectionBoxSize.y * 0.5f)
            return false;

        return HasLineOfSight();
    }

    private bool HasLineOfSight()
    {
        Vector2 direction = (Vector2)player.position - rb.position;
        RaycastHit2D hit = Physics2D.Raycast(rb.position, direction, direction.magnitude, sightBlockingLayers);
        return hit.collider == null;
    }

    // ─── Damage ───────────────────────────────────────────────────────────────

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        // Perched/alert bat is harmless — only the swoop and the flight home hurt.
        if (state != State.Diving && state != State.Recovering) return;

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
        if (state != State.Recovering && !(stompableWhileDiving && state == State.Diving)) return;

        Player player = collision.GetComponent<Player>();
        if (player == null) return;

        Rigidbody2D playerRb = collision.GetComponent<Rigidbody2D>();
        if (playerRb == null || playerRb.linearVelocity.y >= 0) return;

        // Play on the very frame the stomp is registered, before any bounce/anim work.
        AudioManager.Instance.PlayEnemyKicked();

        player.Bounce(stompBounceForce);
        isDead = true;
        StopAllCoroutines();
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

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetAnimState(int value)
    {
        anim.SetInteger("state", value);
    }

    private void FaceTravelDirection(float dx)
    {
        if (Mathf.Abs(dx) < 0.001f) return;
        sr.flipX = dx > 0f; // sprites face left natively, same convention as AngryPig
    }

    private void FindPlayer()
    {
        GameObject obj = GameObject.FindGameObjectWithTag("Player");
        if (obj != null) player = obj.transform;
    }

    private void OnDrawGizmos()
    {
        Vector2 origin = Application.isPlaying ? perchPosition : (Vector2)transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(origin + detectionBoxOffset, detectionBoxSize);
    }
}
