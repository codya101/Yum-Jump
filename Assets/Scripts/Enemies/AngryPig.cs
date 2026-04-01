using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class AngryPig : MonoBehaviour
{
    [Header("Patrol")]
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float idleTimeAtWaypoint = 2f;
    [SerializeField] private float waypointReachedDistance = 0.2f;

    [Header("Chase")]
    [SerializeField] private float runSpeed = 4.5f;
    [SerializeField] private float detectionRange = 5f;
    [SerializeField] private float losePlayerRange = 7f;
    [SerializeField] private float eyeLevelTolerance = 0.5f;

    [Header("VFX")]
    [SerializeField] private GameObject deathVFX;

    [Header("Detection Raycasts")]
    [SerializeField] private float wallCheckDistance = 0.5f;
    [SerializeField] private float edgeCheckDistance = 1f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private LayerMask sightBlockingLayers;

    private Rigidbody2D rb;
    private Animator anim;
    private SpriteRenderer sr;
    private CapsuleCollider2D col;
    private Transform player;

    private Vector3[] waypointPositions;
    private int currentWaypointIndex;
    private bool isDead;
    private Coroutine idleCoroutine;
    private readonly WaitForSeconds despawnWait = new(0.5f);

    private enum State { Patrolling, Idle, Chasing, Returning }
    private State state = State.Patrolling;

    private void Awake()
    {
        rb  = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();
        anim = GetComponentInChildren<Animator>();
        sr   = GetComponentInChildren<SpriteRenderer>();
    }

    private void Start()
    {
        FindPlayer();
        SnapshotWaypointPositions();
    }

    private void SnapshotWaypointPositions()
    {
        waypointPositions = new Vector3[waypoints.Length];
        for (int i = 0; i < waypoints.Length; i++)
            waypointPositions[i] = waypoints[i].position;
    }

    private void Update()
    {
        if (isDead) return;

        if (player == null) FindPlayer();

        switch (state)
        {
            case State.Patrolling:
                HandlePatrol();
                CheckForPlayer();
                break;
            case State.Idle:
                CheckForPlayer();
                break;
            case State.Chasing:
                HandleChase();
                break;
            case State.Returning:
                HandleReturn();
                CheckForPlayer();
                break;
        }
    }

    // ─── Patrol ───────────────────────────────────────────────────────────────

    private void HandlePatrol()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        Vector3 target = waypointPositions[currentWaypointIndex];

        if (Vector2.Distance(transform.position, target) < waypointReachedDistance)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            idleCoroutine = StartCoroutine(IdleAtWaypoint());
            return;
        }

        MoveToward(target, walkSpeed);
        SetAnimator(isWalking: true, seesPlayer: false, reachedWayPoint: false);
    }

    private IEnumerator IdleAtWaypoint()
    {
        state = State.Idle;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        SetAnimator(isWalking: false, seesPlayer: false, reachedWayPoint: true);

        yield return new WaitForSeconds(idleTimeAtWaypoint);

        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        state = State.Patrolling;
    }

    // ─── Chase ────────────────────────────────────────────────────────────────

    private void HandleChase()
    {
        if (player == null)
        {
            EnterReturning();
            return;
        }

        if (Vector2.Distance(transform.position, player.position) > losePlayerRange || !HasLineOfSight())
        {
            EnterReturning();
            return;
        }

        float dir = Mathf.Sign(player.position.x - transform.position.x);

        if (!CanMoveInDirection(dir))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            SetAnimator(isWalking: false, seesPlayer: true, reachedWayPoint: false);
            return;
        }

        rb.linearVelocity = new Vector2(dir * runSpeed, rb.linearVelocity.y);
        FlipSprite(dir);
        SetAnimator(isWalking: false, seesPlayer: true, reachedWayPoint: false);
    }

    private void CheckForPlayer()
    {
        if (player == null) return;

        bool withinRange = Vector2.Distance(transform.position, player.position) <= detectionRange;
        bool atEyeLevel = Mathf.Abs(player.position.y - transform.position.y) <= eyeLevelTolerance;

        if (withinRange && atEyeLevel && HasLineOfSight())
        {
            if (idleCoroutine != null)
            {
                StopCoroutine(idleCoroutine);
                idleCoroutine = null;
            }
            state = State.Chasing;
        }
    }

    private bool HasLineOfSight()
    {
        Vector2 direction = player.position - transform.position;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, direction, direction.magnitude, sightBlockingLayers);
        return hit.collider == null;
    }

    // ─── Return ───────────────────────────────────────────────────────────────

    private void EnterReturning()
    {
        // Snap currentWaypointIndex to nearest waypoint before entering return state
        currentWaypointIndex = GetNearestWaypointIndex();
        state = State.Returning;
    }

    private void HandleReturn()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        Vector3 target = waypointPositions[currentWaypointIndex];

        if (Vector2.Distance(transform.position, target) < waypointReachedDistance)
        {
            state = State.Patrolling;
            return;
        }

        MoveToward(target, walkSpeed);
        SetAnimator(isWalking: true, seesPlayer: false, reachedWayPoint: false);
    }

    private int GetNearestWaypointIndex()
    {
        int nearest = 0;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < waypoints.Length; i++)
        {
            float d = Vector2.Distance(transform.position, waypointPositions[i]);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = i;
            }
        }

        return nearest;
    }

    // ─── Movement & Detection ─────────────────────────────────────────────────

    private void MoveToward(Vector3 target, float speed)
    {
        float dir = Mathf.Sign(target.x - transform.position.x);

        if (!CanMoveInDirection(dir))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            SetAnimator(isWalking: false, seesPlayer: false, reachedWayPoint: true);
            if (state == State.Patrolling)
                idleCoroutine = StartCoroutine(IdleAtWaypoint());
            return;
        }

        rb.linearVelocity = new Vector2(dir * speed, rb.linearVelocity.y);
        FlipSprite(dir);
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

    // ─── Damage ───────────────────────────────────────────────────────────────

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        Player playerComp = collision.collider.GetComponent<Player>();
        if (playerComp != null)
        {
            playerComp.Die();
            GameManager.instance.RespawnPlayer();
        }
    }

    // Called by EnemyHeadTrigger child script
    public void OnHeadStomp(Collider2D collision)
    {
        if (isDead) return;

        Rigidbody2D playerRb = collision.GetComponent<Rigidbody2D>();
        if (playerRb == null || playerRb.linearVelocity.y >= 0) return;

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
        yield return despawnWait;
        Instantiate(deathVFX, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetAnimator(bool isWalking, bool seesPlayer, bool reachedWayPoint)
    {
        anim.SetBool("isWalking", isWalking);
        anim.SetBool("seesPlayer", seesPlayer);
        anim.SetBool("reachedWayPoint", reachedWayPoint);
    }

    private void FlipSprite(float dir)
    {
        sr.flipX = dir > 0;
    }

    private void FindPlayer()
    {
        GameObject obj = GameObject.FindGameObjectWithTag("Player");
        if (obj != null) player = obj.transform;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, losePlayerRange);
    }
}
