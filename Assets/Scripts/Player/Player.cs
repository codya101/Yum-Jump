using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using YumJump.Agent;

public class Player : SimBehaviour
{
    private Rigidbody2D rb;
    private Animator anim;
    private CapsuleCollider2D cd;

    private bool canBeControlled = false;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float jumpForce;
    [SerializeField] private float doubleJumpForce;
    private float defaultGravityScale;
    private bool canDoubleJump;

    [Header("Buffer & Coyote Jump Settings")]
    [SerializeField] private float bufferJumpWindow = 0.25f;
    private float bufferJumpActivated = -1f;
    [SerializeField] private float coyoteJumpWindow = 0.5f;
    private float coyoteJumpActivated = -1f;

    [Header("Wall Settings")]
    [SerializeField] private float wallSlideSpeed = 3f;
    [SerializeField] private float wallJumpDuration = 0.6f;
    [SerializeField] private Vector2 wallJumpForce;
    private bool isWallJumping;

    [Header("Knockback Settings")]
    [SerializeField] private float knockbackDuration = 1f;
    [SerializeField] private Vector2 knockbackPower;
    private bool isKnocked;

    [Header("Collision Settings")]
    [SerializeField] private float groundCheckDistance;
    [SerializeField] private float wallCheckDistance;
    [SerializeField] private LayerMask whatIsGround;
    private bool isGrounded;
    private bool isAirborne;
    private bool isWallDetected;
    private float airborneStartTime;
    // A real jump/fall keeps the player airborne well past this; the rapid grounded/airborne
    // flicker from riding a falling platform doesn't, so it's used to gate the landing sound.
    private const float MinAirborneTimeForLandSound = 0.1f;

    private float xInput;
    private float yInput;

    private bool facingRight = true;
    private int facingDir = 1;

    [Header("Footstep Settings")]
    [Tooltip("Seconds between footstep sounds while walking on the ground.")]
    [SerializeField] private float footstepInterval = 0.3f;
    private float footstepTimer;
    // The surface underfoot, updated while grounded so footstep and landing SFX match
    // the terrain. Sand tiles sit on their own tagged tilemap; anything else is wood.
    private AudioManager.Surface currentSurface = AudioManager.Surface.Wood;
    private const string SandTag = "Sand";

    [Header("VFX")]
    [SerializeField] private GameObject deathVFX;

    // --- Observability for the agent server. Physics truth, never render-interpolated state. ---
    public override SimKind Kind => SimKind.Player;
    public bool IsGrounded => isGrounded;
    public bool IsWallDetected => isWallDetected;
    public bool CanDoubleJumpNow => canDoubleJump;
    public int FacingDirection => facingDir;
    public float MoveSpeed => moveSpeed;
    public float JumpForce => jumpForce;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        cd = GetComponent<CapsuleCollider2D>();
        anim = GetComponentInChildren<Animator>();

        // Read from the prefab in Awake, not Start: the agent server spawns the player and
        // hands over control in the same frame, which is before Start runs. Reading it later
        // would capture whatever gravity that handover had already set.
        defaultGravityScale = rb.gravityScale;
    }

    private void Start()
    {
        // Normal play waits for the respawn animation event to hand over control. In agent
        // mode there is no animation in the loop, so control starts immediately and this stays
        // idempotent no matter which order Start and the server's spawn call happen in.
        RespawnFinished(SimClock.ManualMode);
    }

    protected override void SimTick()
    {
        UpdateAirborneStatus();

        if (canBeControlled == false)
        {
            AudioManager.Instance.SetWallSliding(false);
            return;
        }

        if (isKnocked)
        {
            AudioManager.Instance.SetWallSliding(false);
            return;
        }

        HandleInput();
        HandleWallSlide();
        HandleMovement();
        HandleFlip();
        HandleCollision();
        HandleFootsteps();
        HandleAnimations();
    }

    public void RespawnFinished(bool finished)
    {
        if (finished)
        {
            rb.gravityScale = defaultGravityScale;
            canBeControlled = true;
            cd.enabled = true;
        }
        else
        {
            rb.gravityScale = 0;
            canBeControlled = false;
            cd.enabled = false;
        }
    }

    public void Knockback(float sourceDamageXPosition)
    {
        float knockbackDir = 1f;

        if (transform.position.x < sourceDamageXPosition)
            knockbackDir = -1f;

        if (isKnocked)
            return;

        StartCoroutine(KnockbackRoutine());
        rb.linearVelocity = new Vector2(knockbackPower.x * knockbackDir, knockbackPower.y);
    }

    private IEnumerator KnockbackRoutine()
    {
        isKnocked = true;
        anim.SetBool("isKnocked", true);

        yield return SimClock.Wait(knockbackDuration);

        isKnocked = false;
        anim.SetBool("isKnocked", false);
    }

    /// <summary>
    /// Kills the player. <paramref name="cause"/> is reported verbatim to the agent (e.g.
    /// "hazard:saw_2"), which is what turns a death into a usable learning signal.
    /// </summary>
    public void Die(string cause = null)
    {
        SimEvents.ReportDeath(cause);
        AudioManager.Instance.PlayDeath();
        GameObject newDeathVFX = Instantiate(deathVFX, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        // Kill the looping wall-slide sound if we're disabled/destroyed mid-slide
        // (e.g. death), so it doesn't get stuck on after we're gone. Guard on Exists so
        // this doesn't spawn a throwaway AudioManager while the scene is tearing down.
        if (AudioManager.Exists)
            AudioManager.Instance.SetWallSliding(false);
    }

    private void UpdateAirborneStatus()
    {
        if (isGrounded && isAirborne)
            HandleLanding();

        if (!isGrounded && !isAirborne)
            BecomeAirborne();
    }

    private void BecomeAirborne()
    {
        isAirborne = true;
        airborneStartTime = SimClock.Time;

        if (rb.linearVelocity.y < 0)
            ActivateCoyoteJump();
    }

    private void HandleLanding()
    {
        isAirborne = false;
        canDoubleJump = true;

        // Only after a genuine fall, not the frame-to-frame grounded/airborne flicker
        // caused by riding a falling platform down (the ground ray keeps re-hitting it).
        if (SimClock.Time - airborneStartTime >= MinAirborneTimeForLandSound)
            AudioManager.Instance.PlayLand(currentSurface);

        AttemptBufferJump();
    }

    private void HandleInput()
    {
        xInput = GameInput.Horizontal;
        yInput = GameInput.Vertical;

        if (GameInput.JumpPressed)
        {
            JumpButton();
            RequestBufferJump();
        }
    }

    #region Buffer & Coyote Jump
    private void RequestBufferJump()
    {
        if (isAirborne)
            bufferJumpActivated = SimClock.Time;
    }
    private void AttemptBufferJump()
    {
        if (SimClock.Time < bufferJumpActivated + bufferJumpWindow)
        {
            bufferJumpActivated = SimClock.Time - 1;
            Jump();
        }
    }
    private void ActivateCoyoteJump() => coyoteJumpActivated = SimClock.Time;
    private void CancelCoyoteJump() => coyoteJumpActivated = SimClock.Time - 1;
    #endregion

    private void JumpButton()
    {
        bool coyoteJumpAvailable = SimClock.Time < coyoteJumpActivated + coyoteJumpWindow;

        if (isGrounded || coyoteJumpAvailable)
        {
            Jump();
        }
        else if (isWallDetected && !isGrounded)
        {
            WallJump();
        }
        else if (canDoubleJump)
        {
            DoubleJump();
        }

        CancelCoyoteJump();
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        AudioManager.Instance.PlayJump();
    }

    public void Bounce(float force)
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);
        canDoubleJump = true;
        CancelCoyoteJump();
    }

    private void DoubleJump()
    {
        isWallJumping = false;
        canDoubleJump = false;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, doubleJumpForce);
        AudioManager.Instance.PlayJump();
    }

    private void WallJump()
    {
        canDoubleJump = true;
        rb.linearVelocity = new Vector2(wallJumpForce.x * -facingDir, wallJumpForce.y);
        AudioManager.Instance.PlayWallJump();

        Flip();

        StopAllCoroutines();
        StartCoroutine(WallJumpRoutine());
    }

    private IEnumerator WallJumpRoutine()
    {
        isWallJumping = true;
        yield return SimClock.Wait(wallJumpDuration);
        isWallJumping = false;
    }

    private void HandleWallSlide()
    {
        bool canWallSlide = isWallDetected && rb.linearVelocity.y < 0;
        AudioManager.Instance.SetWallSliding(canWallSlide);

        float yModifier = yInput < 0 ? 1f : 0.5f;

        if (canWallSlide == false)
            return;

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Clamp(rb.linearVelocity.y, -wallSlideSpeed * yModifier, float.MaxValue));
    }

    private void HandleMovement()
    {
        if (isWallDetected)
            return;

        if (isWallJumping)
            return;

        rb.linearVelocity = new Vector2(xInput * moveSpeed, rb.linearVelocity.y);
    }

    private void HandleFlip()
    {
        if (xInput < 0 && facingRight || xInput > 0 && !facingRight)
            Flip();
    }

    private void Flip()
    {
        facingDir *= -1;
        transform.Rotate(0f, 180f, 0f);
        facingRight = !facingRight;
    }

    /// <summary>Re-evaluates the ground/wall rays, so a freshly spawned player reports truth.</summary>
    public void RefreshCollisionState() => HandleCollision();

    private void HandleCollision()
    {
        RaycastHit2D groundHit = Physics2D.Raycast(transform.position, Vector2.down, groundCheckDistance, whatIsGround);
        isGrounded = groundHit.collider != null;
        isWallDetected = Physics2D.Raycast(transform.position, Vector2.right * facingDir, wallCheckDistance, whatIsGround);

        // Remember what we're standing on so the audio matches. Left unchanged while
        // airborne, so the landing sound uses the surface we actually took off from.
        if (isGrounded)
            currentSurface = groundHit.collider.CompareTag(SandTag)
                ? AudioManager.Surface.Sand
                : AudioManager.Surface.Wood;
    }

    // Plays a footstep on a fixed cadence while the player is actually walking on the
    // ground. Pushing into a wall (no real movement) or being airborne stays silent.
    // The timer resets on stop so the first step after starting to move plays instantly.
    private void HandleFootsteps()
    {
        bool isWalking = isGrounded && !isWallDetected && Mathf.Abs(rb.linearVelocity.x) > 0.1f;

        if (!isWalking)
        {
            footstepTimer = 0f;
            return;
        }

        footstepTimer -= SimClock.DeltaTime;
        if (footstepTimer <= 0f)
        {
            AudioManager.Instance.PlayFootstep(currentSurface);
            footstepTimer = footstepInterval;
        }
    }

    private void HandleAnimations()
    {
        anim.SetFloat("xVelocity", rb.linearVelocity.x);
        anim.SetFloat("yVelocity", rb.linearVelocity.y);
        anim.SetBool("isGrounded", isGrounded);
        anim.SetBool("isWallDetected", isWallDetected);
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawLine(transform.position, new Vector2(transform.position.x, transform.position.y - groundCheckDistance));
        Gizmos.DrawLine(transform.position, new Vector2(transform.position.x + (facingDir * wallCheckDistance), transform.position.y));
    }
}
