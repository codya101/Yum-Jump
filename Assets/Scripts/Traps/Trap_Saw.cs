using UnityEngine;
using YumJump.Agent;

public class Trap_Saw : SimBehaviour
{
    private Animator anim;
    private SpriteRenderer sr;

    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float cooldown = 1f;
    [SerializeField] private Transform[] wayPoints;
    private Vector3[] wayPointPositions;

    public int wayPointIndex = 1;
    public int moveDirection = 1;
    // Sim time left parked at a waypoint. This used to be a coroutine holding `canMove` false
    // across a SimClock.Wait, which does not survive a snapshot: SimBehaviour.Restore calls
    // StopAllCoroutines and nothing re-armed it, so a blade captured mid-park came back with
    // canMove false and never moved again - freezing it into a permanent wall for every run
    // resumed from that checkpoint. Counting the park down here instead keeps all of the
    // blade's timing inside CaptureExtra/RestoreExtra, where a reset can restore it.
    //
    // Seconds rather than ticks because SimTick runs once per *rendered frame* in normal
    // play: a frame counter would dwell 30 frames, which is the authored 0.5s at 60Hz but
    // half that at 120Hz. In agent mode DeltaTime is FixedDelta, so this stays exactly
    // tick-quantized and the park is the same integer number of ticks every replay.
    private float parkSecondsLeft;

    /// <summary>Parked blades neither move nor whir; the park is counted in sim time.</summary>
    private bool CanMove => parkSecondsLeft <= 0f;

    [Header("Audio")]
    [Tooltip("Loudness of the saw whir at full SFX volume, before the SFX slider scales it. " +
             "Kept low so nearby saws don't drown out other SFX.")]
    [SerializeField, Range(0f, 1f)] private float sawVolume = 0.5f;
    [Tooltip("Distance (world units) within which the saw is at full volume.")]
    [SerializeField] private float minDistance = 10f;
    [Tooltip("Distance (world units) beyond which the saw is silent.")]
    [SerializeField] private float maxDistance = 24f;
    private const string SawClip = "Audio/SFX/SFX_Saw_Trap";
    private static AudioClip sawClip;
    private AudioSource sawSource;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();

        SetupAudio();
    }

    // 3D looping whir that follows the saw, so its volume falls off with the player's
    // distance (like the fan wind). Gated on CanMove in SimTick so it goes quiet while the
    // blade is parked at a waypoint. The clip is cached statically across all saws.
    private void SetupAudio()
    {
        if (sawClip == null) sawClip = Resources.Load<AudioClip>(SawClip);

        sawSource = gameObject.AddComponent<AudioSource>();
        sawSource.clip = sawClip;
        sawSource.loop = true;
        sawSource.playOnAwake = true;
        sawSource.spatialBlend = 1f;                 // 3D: fades with distance from the saw.
        sawSource.rolloffMode = AudioRolloffMode.Linear;
        sawSource.minDistance = minDistance;
        sawSource.maxDistance = maxDistance;
        sawSource.dopplerLevel = 0f;
        sawSource.volume = 0f;                        // set properly each frame in Update.

        if (sawClip != null) sawSource.Play();
    }

    private void Start()
    {
        UpdateWaypointsInfo();

        if (wayPointPositions.Length > 0)
            transform.position = wayPointPositions[0];
    }

    private void UpdateWaypointsInfo()
    {
        wayPointPositions = new Vector3[wayPoints.Length];

        for (int i = 0; i < wayPoints.Length; i++)
        {
            wayPointPositions[i] = wayPoints[i].position;
        }
    }

    public override SimKind Kind => SimKind.Hazard;

    protected override void SimTick()
    {
        anim.SetBool("active", CanMove);

        // Whir only while the blade is moving; silent while parked. The 3D rolloff on
        // the source then scales this by the player's distance. EffectiveSfxVolume folds
        // in the SFX slider + mute, so this stays in sync with the rest of the audio.
        if (sawSource != null)
        {
            // Re-apply the distances every frame so they can be tuned live in Play
            // mode from the Inspector (they were previously only read once in Awake).
            sawSource.minDistance = minDistance;
            sawSource.maxDistance = maxDistance;
            sawSource.volume = (CanMove ? sawVolume : 0f) * AudioManager.Instance.EffectiveSfxVolume;
        }

        if (parkSecondsLeft > 0f)
        {
            // A step of the park spent. Counting it here rather than at the top of SimTick
            // keeps the park equal to the coroutine's: the blade stays still for `cooldown`
            // seconds of sim time and moves again on the step after.
            parkSecondsLeft -= SimClock.DeltaTime;
            return;
        }

        // Stationary saw: no path to follow, just spin and whir in place.
        if (wayPointPositions.Length < 2)
            return;

        transform.position = Vector2.MoveTowards(transform.position, wayPointPositions[wayPointIndex], moveSpeed * SimClock.DeltaTime);

        if (Vector2.Distance(transform.position, wayPointPositions[wayPointIndex]) < 0.1f)
        {
            if (wayPointIndex == wayPointPositions.Length - 1 || wayPointIndex == 0)
            {
                moveDirection *= -1;
                parkSecondsLeft = cooldown;
            }

            wayPointIndex += moveDirection;
        }
    }

    /// <summary>Path state a transform-only restore would miss.</summary>
    private sealed class State
    {
        public int wayPointIndex;
        public int moveDirection;
        public float parkSecondsLeft;
    }

    protected override object CaptureExtra() =>
        new State
        {
            wayPointIndex = wayPointIndex,
            moveDirection = moveDirection,
            parkSecondsLeft = parkSecondsLeft,
        };

    protected override void RestoreExtra(object extra)
    {
        if (extra is State s)
        {
            wayPointIndex = s.wayPointIndex;
            moveDirection = s.moveDirection;
            parkSecondsLeft = s.parkSecondsLeft;
        }
    }

    // Scene-view visualization of the audible range. The AudioListener rides the
    // camera at z = -10, so the on-screen radius where the whir starts is
    // sqrt(maxDistance² - 100), not maxDistance itself.
    private void OnDrawGizmosSelected()
    {
        const float listenerZ = 10f;

        float maxSq = maxDistance * maxDistance - listenerZ * listenerZ;
        if (maxSq > 0f)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, Mathf.Sqrt(maxSq));
        }

        float minSq = minDistance * minDistance - listenerZ * listenerZ;
        if (minSq > 0f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, Mathf.Sqrt(minSq));
        }
    }
}
