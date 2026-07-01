using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent audio singleton: holds music/SFX volume + mute state (persisted to
/// PlayerPrefs so it survives scene loads) AND plays one-shot sound effects.
///
/// Clips are loaded by name from a Resources folder (Assets/Resources/Audio) the
/// first time they're used and then cached, so nothing needs to be wired in the
/// inspector — call sites just do e.g. <c>AudioManager.Instance.PlayJump()</c>.
/// All SFX go through one shared 2D AudioSource created at runtime, so sounds keep
/// playing even when the object that triggered them is destroyed (e.g. the player
/// on death) and are unaffected by Time.timeScale (so menu clicks work while paused).
/// </summary>
public class AudioManager : MonoBehaviour
{
    // Resources subfolder (under any Assets/.../Resources/ folder) holding the SFX
    // .wav clips. Names below are the file names without extension.
    private const string AudioResourceFolder = "Audio/SFX";
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

    // Shared 2D source for all one-shot SFX, plus a name->clip cache so each clip
    // is only loaded from Resources once.
    private AudioSource sfxSource;
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();

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

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;        // 2D — full volume regardless of position.
        sfxSource.ignoreListenerPause = true; // still audible if audio is globally paused.

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

    // ─── Sound effects ──────────────────────────────────────────────────────────
    // Named helpers so call sites never deal with raw clip names. Add a new sound
    // by dropping SFX_Something.wav into Assets/Resources/Audio and adding a method.

    public void PlayJump()        => PlaySfx("SFX_Jump");
    public void PlayWallJump()    => PlaySfx("SFX_WallJump");
    public void PlayDeath()       => PlaySfx("SFX_Death");
    public void PlayEnemyKicked() => PlaySfx("SFX_EnemyKicked");
    public void PlayFinish()      => PlaySfx("SFX_Finish");
    public void PlayPickup()      => PlaySfxRandom("SFX_PickUp_1", "SFX_PickUp_2");
    public void PlayRespawn()     => PlaySfxRandom("SFX_Respawn_1", "SFX_Respawn_2");
    public void PlayMenuSelect()  => PlaySfxRandom("SFX_MenuSelect_1", "SFX_MenuSelect_2");

    /// <summary>
    /// Plays a one-shot SFX by clip name (file name in Assets/Resources/Audio,
    /// without extension), scaled by the current effective SFX volume.
    /// </summary>
    public void PlaySfx(string clipName, float volumeScale = 1f)
    {
        if (sfxSource == null || string.IsNullOrEmpty(clipName)) return;

        AudioClip clip = GetClip(clipName);
        if (clip == null) return;

        sfxSource.PlayOneShot(clip, EffectiveSfxVolume * volumeScale);
    }

    /// <summary>Plays one of several clips at random — for events with variants
    /// (pickups, respawns, menu clicks) so they don't sound repetitive.</summary>
    private void PlaySfxRandom(params string[] clipNames)
    {
        if (clipNames == null || clipNames.Length == 0) return;
        PlaySfx(clipNames[Random.Range(0, clipNames.Length)]);
    }

    private AudioClip GetClip(string clipName)
    {
        if (clipCache.TryGetValue(clipName, out AudioClip cached))
            return cached;

        AudioClip clip = Resources.Load<AudioClip>($"{AudioResourceFolder}/{clipName}");
        if (clip == null)
            Debug.LogWarning($"[AudioManager] No clip at Resources/{AudioResourceFolder}/{clipName}. " +
                             "Check the file exists in Assets/Resources/Audio and the name matches.");

        // Cache even a null result so a missing clip isn't re-searched every call.
        clipCache[clipName] = clip;
        return clip;
    }
}
