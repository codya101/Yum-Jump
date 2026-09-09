using UnityEngine;
using YumJump.Agent;

[RequireComponent(typeof(CapsuleCollider2D))]
public class Plant : SimBehaviour
{
    [Header("Attack")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float attackInterval = 2.5f;
    [SerializeField] private float bulletSpeed = 6f;

    [Header("Facing")]
    [SerializeField] private bool facingLeft = true;

    [Header("Audio")]
    [Tooltip("Loudness of the shot at full SFX volume, before the SFX slider scales it.")]
    [SerializeField, Range(0f, 1f)] private float popVolume = 1f;
    [Tooltip("Distance (world units) within which the shot is at full volume.")]
    [SerializeField] private float minDistance = 6f;
    [Tooltip("Distance (world units) beyond which the shot is silent — plants firing off here go unheard.")]
    [SerializeField] private float maxDistance = 18f;
    private const string PopClip = "Audio/SFX/SFX_Pop";
    private static AudioClip popClip;
    private AudioSource popSource;

    private Animator anim;
    private SpriteRenderer sr;
    private float nextAttackTime;
    private int firedOnTick = -1;

    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        sr = GetComponentInChildren<SpriteRenderer>();

        SetupAudio();
    }

    // 3D one-shot source on the (stationary) plant, so a shot's volume falls off with the
    // player's distance and plants firing far away/off-screen are inaudible. The clip is
    // cached statically across all plants.
    private void SetupAudio()
    {
        if (popClip == null) popClip = Resources.Load<AudioClip>(PopClip);

        popSource = gameObject.AddComponent<AudioSource>();
        popSource.playOnAwake = false;
        popSource.spatialBlend = 1f;                 // 3D: only heard when the player is near.
        popSource.rolloffMode = AudioRolloffMode.Linear;
        popSource.minDistance = minDistance;
        popSource.maxDistance = maxDistance;
        popSource.dopplerLevel = 0f;
    }

    private void Start()
    {
        if (!facingLeft)
        {
            sr.flipX = true;
            if (firePoint != null)
            {
                Vector3 lp = firePoint.localPosition;
                lp.x = -lp.x;
                firePoint.localPosition = lp;
            }
        }
        nextAttackTime = SimClock.Time + attackInterval;
    }

    public override SimKind Kind => SimKind.Enemy;

    protected override object CaptureExtra() => nextAttackTime;

    protected override void RestoreExtra(object extra)
    {
        nextAttackTime = extra is float f ? f : attackInterval;
    }

    protected override void SimTick()
    {
        if (SimClock.Time < nextAttackTime) return;

        anim.SetTrigger("attack");
        nextAttackTime = SimClock.Time + attackInterval;

        // Firing is normally driven by an animation event, and animation timing runs off the
        // frame clock rather than the tick clock. In agent mode the shot leaves the tick itself,
        // so a replay puts the bullet in the same place; the animation still plays for the look.
        if (SimClock.ManualMode) Fire();
    }

    /// <summary>Animation event in normal play; called straight from the tick in agent mode.</summary>
    public void Fire()
    {
        if (bulletPrefab == null) return;
        // Stops the animation event from doubling the tick-driven shot.
        if (SimClock.ManualMode && firedOnTick == SimClock.Tick) return;
        firedOnTick = SimClock.Tick;

        Vector3 origin = firePoint != null ? firePoint.position : transform.position;
        GameObject bullet = Instantiate(bulletPrefab, origin, Quaternion.identity);
        PlantBullet pb = bullet.GetComponent<PlantBullet>();
        if (pb != null) pb.Launch(facingLeft ? Vector2.left : Vector2.right, bulletSpeed);

        // 3D rolloff scales this by distance, so out-of-range plants are silent.
        if (popSource != null && popClip != null)
            popSource.PlayOneShot(popClip, popVolume * AudioManager.Instance.EffectiveSfxVolume);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Player playerComp = collision.collider.GetComponent<Player>();
        if (playerComp != null)
        {
            playerComp.Die("enemy:" + SimId);
            GameManager.instance.RespawnPlayer();
        }
    }
}
