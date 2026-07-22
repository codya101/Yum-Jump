using System.Collections;
using UnityEngine;

public class Trap_Saw : MonoBehaviour
{
    private Animator anim;
    private SpriteRenderer sr;

    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float cooldown = 1f;
    [SerializeField] private Transform[] wayPoints;
    private Vector3[] wayPointPositions;

    public int wayPointIndex = 1;
    public int moveDirection = 1;
    private bool canMove = true;

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
    // distance (like the fan wind). Gated on canMove in Update so it goes quiet while the
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

    private void Update()
    {
        anim.SetBool("active", canMove);

        // Whir only while the blade is moving; silent while parked. The 3D rolloff on
        // the source then scales this by the player's distance. EffectiveSfxVolume folds
        // in the SFX slider + mute, so this stays in sync with the rest of the audio.
        if (sawSource != null)
            sawSource.volume = (canMove ? sawVolume : 0f) * AudioManager.Instance.EffectiveSfxVolume;

        if (canMove == false)
            return;

        // Stationary saw: no path to follow, just spin and whir in place.
        if (wayPointPositions.Length < 2)
            return;

        transform.position = Vector2.MoveTowards(transform.position, wayPointPositions[wayPointIndex], moveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, wayPointPositions[wayPointIndex]) < 0.1f)
        {
            if (wayPointIndex == wayPointPositions.Length - 1 || wayPointIndex == 0)
            {
                moveDirection *= -1;
                StartCoroutine(StopMovement(cooldown));
            }

            wayPointIndex += moveDirection;
        }
    }

    private IEnumerator StopMovement(float delay)
    {
        canMove = false;

        yield return new WaitForSeconds(delay);

        canMove = true;
        //sr.flipX = !sr.flipX;
    }
}
