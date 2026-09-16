using UnityEngine;
using YumJump.Agent;

/// <summary>
/// A floor block that erupts in flame on a fixed cycle:
/// Off (safe) -> Warmup (the "Hit" spark telegraph, still safe) -> Burning (lethal) -> Off...
///
/// The flame's trigger collider (with a DamageTrigger on the same object) is enabled only
/// while Burning, so standing on the block through the telegraph is survivable by design.
/// phaseOffset shifts an instance's cycle forward in time, so a row of traps can fire as a
/// wave (offsets 0, 0.5, 1.0, ...) while sharing identical durations.
///
/// The cycle runs on SimClock time and its only mutable state is the cycle timer, so
/// agent-mode capture/restore and replays stay deterministic.
/// </summary>
public class Trap_Fire : SimBehaviour
{
    private enum Phase { Off, Warmup, Burning }

    [Header("Cycle (seconds)")]
    [SerializeField] private float offDuration = 2f;
    [SerializeField] private float warmupDuration = 0.4f;
    [SerializeField] private float flameDuration = 0.8f;
    [Tooltip("Shifts this trap's cycle forward in time. Give a row of traps offsets of " +
             "0, 0.5, 1.0, ... to make them erupt in sequence as a wave.")]
    [SerializeField] private float phaseOffset = 0f;

    [Header("Flame")]
    [Tooltip("Trigger collider covering the flame area, with a DamageTrigger alongside it. " +
             "Enabled only while the flame is burning.")]
    [SerializeField] private Collider2D flameCollider;

    [Header("Audio")]
    [Tooltip("Loudness of the ignite whoosh at full SFX volume, before the SFX slider scales it.")]
    [SerializeField, Range(0f, 1f)] private float igniteVolume = 0.2f;
    [Tooltip("Distance (world units) within which the whoosh is at full volume.")]
    [SerializeField] private float minDistance = 10f;
    [Tooltip("Distance (world units) beyond which the whoosh is silent.")]
    [SerializeField] private float maxDistance = 18f;
    private const string IgniteClip = "Audio/SFX/SFX_Fire_Trap";
    private static AudioClip igniteClip;
    private AudioSource igniteSource;

    private Animator anim;
    private float cycleTime;
    private Phase currentPhase;
    private bool phaseApplied;

    private float Period => offDuration + warmupDuration + flameDuration;

    public override SimKind Kind => SimKind.Hazard;

    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        SetupAudio();
    }

    // 3D one-shot whoosh played on ignite, so its volume falls off with the player's
    // distance (same setup as the saw whir). The clip is cached statically across all traps.
    private void SetupAudio()
    {
        if (igniteClip == null) igniteClip = Resources.Load<AudioClip>(IgniteClip);
        if (igniteClip == null) return; // clip not added to Resources yet; stay silent

        igniteSource = gameObject.AddComponent<AudioSource>();
        igniteSource.clip = igniteClip;
        igniteSource.loop = false;
        igniteSource.playOnAwake = false;
        igniteSource.spatialBlend = 1f;
        igniteSource.rolloffMode = AudioRolloffMode.Linear;
        igniteSource.minDistance = minDistance;
        igniteSource.maxDistance = maxDistance;
        igniteSource.dopplerLevel = 0f;
    }

    private void Start()
    {
        cycleTime = Mathf.Max(0f, phaseOffset);
        phaseApplied = false;
        ApplyPhase(PhaseAt(cycleTime));
    }

    protected override void SimTick()
    {
        cycleTime += SimClock.DeltaTime;

        // Keep the timer bounded so float precision never degrades on long sessions.
        if (cycleTime >= Period * 2f)
            cycleTime -= Period;

        ApplyPhase(PhaseAt(cycleTime));
    }

    private Phase PhaseAt(float time)
    {
        float t = Mathf.Repeat(time, Period);

        if (t < offDuration) return Phase.Off;
        if (t < offDuration + warmupDuration) return Phase.Warmup;
        return Phase.Burning;
    }

    private void ApplyPhase(Phase phase)
    {
        if (phaseApplied && phase == currentPhase) return;

        currentPhase = phase;
        phaseApplied = true;

        if (flameCollider != null)
            flameCollider.enabled = phase == Phase.Burning;

        switch (phase)
        {
            case Phase.Off:
                anim.Play("Off");
                break;
            case Phase.Warmup:
                anim.Play("Hit", 0, 0f);
                PlayIgniteWhoosh();
                break;
            case Phase.Burning:
                anim.Play("On", 0, 0f);
                break;
        }
    }

    private void PlayIgniteWhoosh()
    {
        if (igniteSource == null || AudioManager.Instance == null) return;

        igniteSource.volume = igniteVolume * AudioManager.Instance.EffectiveSfxVolume;
        igniteSource.Play();
    }

    // ---------------------------------------------------------------- snapshot / restore

    /// <summary>Cycle progress that a transform-only restore cannot recover.</summary>
    private sealed class FireState
    {
        public float cycleTime;
    }

    protected override object CaptureExtra() => new FireState { cycleTime = cycleTime };

    protected override void RestoreExtra(object extra)
    {
        if (extra is FireState s)
            cycleTime = s.cycleTime;

        phaseApplied = false; // force the phase visuals/collider to re-apply
        ApplyPhase(PhaseAt(cycleTime));
    }

    public override void DescribeTo(System.Collections.Generic.Dictionary<string, object> fields)
    {
        fields["phase"] = currentPhase.ToString().ToLowerInvariant();
    }

    // ---------------------------------------------------------------- editor helpers

    private void OnValidate()
    {
        offDuration = Mathf.Max(0.05f, offDuration);
        warmupDuration = Mathf.Max(0.05f, warmupDuration);
        flameDuration = Mathf.Max(0.05f, flameDuration);
        phaseOffset = Mathf.Max(0f, phaseOffset);
    }

    // Draws the flame kill zone in the Scene view so eruption coverage can be checked
    // without entering Play mode.
    private void OnDrawGizmosSelected()
    {
        BoxCollider2D box = flameCollider as BoxCollider2D;
        if (box == null) return;

        Gizmos.color = Color.red;
        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.offset, box.size);
    }
}
