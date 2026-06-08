using UnityEngine;

/// <summary>
/// Persistent settings singleton for music/SFX volume and mute state.
///
/// The game has no audio yet, but the pause menu's sliders and mute toggles need
/// real, persisted state to bind to. This stores that state in PlayerPrefs so it
/// survives scene loads and play sessions. When audio is added later, assign an
/// AudioMixer in the inspector (or to <see cref="mixer"/> from code) and the
/// ApplyToMixer() hook below becomes the single place that pushes these values to
/// the actual mixer — no other code needs to change.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private const string MusicVolumeKey = "MusicVolume";
    private const string SfxVolumeKey = "SFXVolume";
    private const string MusicMutedKey = "MusicMuted";
    private const string SfxMutedKey = "SFXMuted";

    private static AudioManager instance;

    /// <summary>
    /// Lazily creates a DontDestroyOnLoad instance the first time it's accessed,
    /// matching the auto-create style used elsewhere in the project.
    /// </summary>
    public static AudioManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<AudioManager>();
                if (instance == null)
                {
                    GameObject host = new GameObject("AudioManager");
                    instance = host.AddComponent<AudioManager>();
                }
            }
            return instance;
        }
    }

    [Header("Audio Mixer (optional, for when audio is added)")]
    [Tooltip("Leave empty for now. When audio exists, assign a mixer with exposed " +
             "'MusicVolume' and 'SFXVolume' parameters and ApplyToMixer() will drive them.")]
    public UnityEngine.Audio.AudioMixer mixer;

    private float musicVolume = 1f;
    private float sfxVolume = 1f;
    private bool musicMuted;
    private bool sfxMuted;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
        musicMuted = PlayerPrefs.GetInt(MusicMutedKey, 0) == 1;
        sfxMuted = PlayerPrefs.GetInt(SfxMutedKey, 0) == 1;

        ApplyToMixer();
    }

    public float MusicVolume
    {
        get => musicVolume;
        set
        {
            musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
            ApplyToMixer();
        }
    }

    public float SfxVolume
    {
        get => sfxVolume;
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
            ApplyToMixer();
        }
    }

    public bool MusicMuted
    {
        get => musicMuted;
        set
        {
            musicMuted = value;
            PlayerPrefs.SetInt(MusicMutedKey, musicMuted ? 1 : 0);
            ApplyToMixer();
        }
    }

    public bool SfxMuted
    {
        get => sfxMuted;
        set
        {
            sfxMuted = value;
            PlayerPrefs.SetInt(SfxMutedKey, sfxMuted ? 1 : 0);
            ApplyToMixer();
        }
    }

    /// <summary>Effective 0..1 levels after accounting for mute. Future audio reads these.</summary>
    public float EffectiveMusicVolume => musicMuted ? 0f : musicVolume;
    public float EffectiveSfxVolume => sfxMuted ? 0f : sfxVolume;

    /// <summary>
    /// Pushes the current volumes to the mixer. No-op until a mixer is assigned, so
    /// it's safe to call now; wiring audio later means only filling this in.
    /// </summary>
    private void ApplyToMixer()
    {
        if (mixer == null) return;

        // AudioMixer faders are in decibels; convert 0..1 to dB (with a floor at -80).
        mixer.SetFloat("MusicVolume", LinearToDecibels(EffectiveMusicVolume));
        mixer.SetFloat("SFXVolume", LinearToDecibels(EffectiveSfxVolume));
    }

    private static float LinearToDecibels(float linear)
    {
        return linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
    }
}
