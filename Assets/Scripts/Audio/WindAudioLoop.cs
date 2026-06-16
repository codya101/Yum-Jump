using UnityEngine;

/// <summary>
/// Plays a looping, procedurally generated wind/gust sound for the fan updraft.
///
/// There are no audio assets in the project yet, so the clip is synthesized in code
/// (brown noise + a breathy low-passed layer + slow gusting) and cached statically so
/// every fan shares one clip. The AudioSource is 3D, so the sound fades in as the
/// player nears the fan and out as they leave.
///
/// Volume is driven every frame from <see cref="AudioManager.EffectiveSfxVolume"/>, so
/// the pause menu's SFX slider and SFX mute toggle raise/lower this live — including
/// while paused, since Update is not affected by Time.timeScale.
/// </summary>
public class WindAudioLoop : MonoBehaviour
{
    [Tooltip("Loudness of the wind at full SFX volume, before the SFX slider scales it.")]
    [SerializeField, Range(0f, 1f)] private float baseVolume = 0.3f;

    [Tooltip("How far up the updraft column (world units above the fan) the sound emits from.")]
    [SerializeField] private float emitterHeight = 12f;

    [Tooltip("Distance (world units) at which the wind is at full volume.")]
    [SerializeField] private float minDistance = 4f;

    [Tooltip("Distance (world units) beyond which the wind is silent.")]
    [SerializeField] private float maxDistance = 16f;

    private AudioSource source;
    private static AudioClip windClip;

    private void Awake()
    {
        if (windClip == null) windClip = GenerateWindClip();

        // Emit from a child partway up the column instead of the fan base, so the
        // audible sphere is centered over the draft rather than bleeding far below.
        GameObject emitter = new GameObject("WindEmitter");
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = new Vector3(0f, emitterHeight, 0f);
        source = emitter.AddComponent<AudioSource>();

        source.clip = windClip;
        source.loop = true;
        source.playOnAwake = true;
        source.spatialBlend = 1f;                 // 3D: fades with distance from the fan
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.volume = 0f;                        // set properly in the first Update

        if (!source.isPlaying) source.Play();
    }

    private void Update()
    {
        // EffectiveSfxVolume already folds in the SFX mute toggle (returns 0 when muted).
        source.volume = baseVolume * AudioManager.Instance.EffectiveSfxVolume;
    }

    /// <summary>
    /// Synthesizes a seamless ~3s wind loop: brown noise for low rumble, a low-passed
    /// white layer for airy hiss, slow periodic gusting, and an overlap crossfade so
    /// the loop point is click-free.
    /// </summary>
    private static AudioClip GenerateWindClip()
    {
        const int sampleRate = 44100;
        const int n = sampleRate * 3;     // 3 seconds
        const int fade = sampleRate / 4;  // 0.25s crossfade region

        // Generate a little extra (n + fade) so the loop can overlap-fold cleanly.
        float[] raw = new float[n + fade];
        System.Random rng = new System.Random(1234);
        float brown = 0f;  // integrated (brown) noise accumulator -> low rumble
        float lp = 0f;     // low-pass state -> soft, muffled airiness (low cutoff)
        float smooth = 0f; // final one-pole low-pass to remove crackle/static
        for (int i = 0; i < raw.Length; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.996f;
            lp += (white - lp) * 0.012f;             // lower cutoff -> less hiss
            float mixed = brown * 0.9f + lp * 0.25f; // rumble-dominant, quieter airy layer
            smooth += (mixed - smooth) * 0.08f;      // tame high-frequency harshness
            raw[i] = smooth;
        }

        // Overlap-fold the tail back into the head: final[0] continues from raw[n],
        // which flows smoothly out of raw[n-1] -> seamless loop boundary.
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (i < fade)
            {
                float w = i / (float)fade;               // 0 at loop point -> 1
                data[i] = raw[i] * w + raw[n + i] * (1f - w);
            }
            else
            {
                data[i] = raw[i];
            }
        }

        // Slow gusting, periodic over the loop (integer cycles) so it stays seamless.
        // Also remove DC offset and normalize to a safe peak.
        float mean = 0f;
        for (int i = 0; i < n; i++) mean += data[i];
        mean /= n;

        float max = 0f;
        for (int i = 0; i < n; i++)
        {
            float gust = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 2f * i / n);
            data[i] = (data[i] - mean) * gust;
            max = Mathf.Max(max, Mathf.Abs(data[i]));
        }
        if (max > 0f)
        {
            float g = 0.9f / max;
            for (int i = 0; i < n; i++) data[i] *= g;
        }

        AudioClip clip = AudioClip.Create("FanWindLoop", n, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
