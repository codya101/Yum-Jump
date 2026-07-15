using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent audio singleton: holds music/SFX volume + mute state (persisted to
/// PlayerPrefs so it survives scene loads), plays one-shot sound effects, AND drives
/// looping background music that carries across scene loads.
///
/// Clips are loaded by name from a Resources folder (Assets/Resources/Audio) the
/// first time they're used and then cached, so nothing needs to be wired in the
/// inspector — call sites just do e.g. <c>AudioManager.Instance.PlayJump()</c>.
/// All SFX go through one shared 2D AudioSource created at runtime, so sounds keep
/// playing even when the object that triggered them is destroyed (e.g. the player
/// on death) and are unaffected by Time.timeScale (so menu clicks work while paused).
///
/// Music is chosen per scene by <see cref="TrackForScene"/>. Because the manager
/// persists (DontDestroyOnLoad) and re-requesting the already-playing track is a
/// no-op, the song continues seamlessly when scenes that share a track hand off to
/// each other (e.g. Level1 -> Level2, or The End -> Main Menu).
/// </summary>
public class AudioManager : MonoBehaviour
{
    // Resources subfolder (under any Assets/.../Resources/ folder) holding the SFX
    // .wav clips. Names below are the file names without extension.
    private const string AudioResourceFolder = "Audio/SFX";

    // Background music. Track names are file names (without extension) inside
    // MusicResourceFolder. Which track plays where is decided by TrackForScene().
    private const string MusicResourceFolder = "Audio/BGM/8Bit Music Album - 051321";
    private const string LevelMusic = "1. Track 1"; // Level1 / Level2 / Level3
    private const string MenuMusic  = "3. Track 3"; // MainMenu / TheEnd

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

    /// <summary>
    /// True only if an instance already exists — accessing this never creates one.
    /// Callers running during scene teardown (OnDisable/OnDestroy) should guard on this
    /// so they don't lazily spawn a throwaway AudioManager while the scene is closing.
    /// </summary>
    public static bool Exists => instance != null;

    [Header("Audio Mixer (optional, for when audio is added)")]
    [Tooltip("Leave empty for now. When audio exists, assign a mixer with exposed " +
             "'MusicVolume' and 'SFXVolume' parameters and ApplyToMixer() will drive them.")]
    public UnityEngine.Audio.AudioMixer mixer;

    private float musicVolume = 1f;
    private float sfxVolume = 1f;
    private bool musicMuted;
    private bool sfxMuted;

    // Shared 2D source for all one-shot SFX, a separate looping source for music,
    // plus a name->clip cache so each clip is only loaded from Resources once.
    private AudioSource sfxSource;
    private AudioSource musicSource;
    private string currentMusicTrack;
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();

    // Looping wall-slide friction sound. Unlike the one-shots it's a held state, so it
    // gets its own source that Player drives on/off via SetWallSliding (instant, no fade).
    // The clip is loud, so it sits at a low base level.
    private AudioSource wallSlideSource;
    private const string WallSlideClip = "SFX_Sliding";
    private const float WallSlideVolume = 0.3f;    // base level before the SFX slider scales it.

    /// <summary>
    /// Forces the singleton to exist at launch so it can start music on the very
    /// first scene. That scene has already loaded by this point, so sceneLoaded
    /// won't fire for it — we drive its music directly here. Mirrors how
    /// SceneTransition bootstraps itself.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Instance.HandleSceneMusic(SceneManager.GetActiveScene().name);
    }

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

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;            // only ever restarts at its natural end.
        musicSource.spatialBlend = 0f;      // 2D.
        musicSource.ignoreListenerPause = true; // keeps playing while the game is paused.

        wallSlideSource = gameObject.AddComponent<AudioSource>();
        wallSlideSource.playOnAwake = false;
        wallSlideSource.loop = true;
        wallSlideSource.spatialBlend = 0f;  // 2D.
        wallSlideSource.volume = 0f;        // faded up by Update while sliding.
        wallSlideSource.clip = GetClip(WallSlideClip);

        // Switch tracks whenever a new scene loads (see HandleSceneMusic).
        SceneManager.sceneLoaded += OnSceneLoaded;

        ApplyToMixer();
    }

    private void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
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
    /// Pushes the current volumes to the live music source (so slider/mute changes
    /// take effect immediately) and, if one is assigned, to the mixer. SFX volume is
    /// applied per-shot in PlaySfx, so it needs nothing continuous here.
    /// </summary>
    private void ApplyToMixer()
    {
        if (musicSource != null)
            musicSource.volume = EffectiveMusicVolume;

        if (mixer == null) return;

        // AudioMixer faders are in decibels; convert 0..1 to dB (with a floor at -80).
        mixer.SetFloat("MusicVolume", LinearToDecibels(EffectiveMusicVolume));
        mixer.SetFloat("SFXVolume", LinearToDecibels(EffectiveSfxVolume));
    }

    private static float LinearToDecibels(float linear)
    {
        return linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
    }

    // ─── Background music ───────────────────────────────────────────────────────

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HandleSceneMusic(scene.name);

    /// <summary>Starts the track this scene should use. Scenes that share a track
    /// (the levels, or Main Menu + The End) hand the song off without restarting it;
    /// scenes with no mapping leave whatever is playing untouched.</summary>
    private void HandleSceneMusic(string sceneName)
    {
        string track = TrackForScene(sceneName);
        if (track != null)
            PlayMusic(track);
    }

    private static string TrackForScene(string sceneName)
    {
        switch (sceneName)
        {
            case "MainMenu":
            case "TheEnd":
                return MenuMusic;
            case "Level1":
            case "Level2":
            case "Level3":
                return LevelMusic;
            default:
                return null;
        }
    }

    /// <summary>
    /// Plays a looping background track by name (file in MusicResourceFolder, without
    /// extension). If that track is already playing it's left alone, so music carries
    /// seamlessly across scene loads; looping means it only ever restarts from the top
    /// when it reaches its natural end.
    /// </summary>
    public void PlayMusic(string trackName)
    {
        if (musicSource == null || string.IsNullOrEmpty(trackName)) return;

        if (currentMusicTrack == trackName && musicSource.isPlaying) return;

        AudioClip clip = GetMusicClip(trackName);
        if (clip == null) return;

        currentMusicTrack = trackName;
        musicSource.clip = clip;
        musicSource.volume = EffectiveMusicVolume;
        musicSource.Play();
    }

    public void StopMusic()
    {
        currentMusicTrack = null;
        if (musicSource != null) musicSource.Stop();
    }

    private AudioClip GetMusicClip(string trackName)
    {
        if (clipCache.TryGetValue(trackName, out AudioClip cached))
            return cached;

        AudioClip clip = Resources.Load<AudioClip>($"{MusicResourceFolder}/{trackName}");
        if (clip == null)
            Debug.LogWarning($"[AudioManager] No music at Resources/{MusicResourceFolder}/{trackName}. " +
                             "Check the file exists and the name matches.");

        clipCache[trackName] = clip;
        return clip;
    }

    // ─── Sound effects ──────────────────────────────────────────────────────────
    // Named helpers so call sites never deal with raw clip names. Add a new sound
    // by dropping SFX_Something.wav into Assets/Resources/Audio and adding a method.

    public void PlayJump()        => PlaySfx("SFX_Jump");
    public void PlayWallJump()    => PlaySfx("SFX_WallJump");
    public void PlayDeath()       => PlaySfx("SFX_Death");
    public void PlayEnemyKicked() => PlaySfx("SFX_EnemyKicked");
    public void PlayFinish()      => PlaySfx("SFX_Finish");
    public void PlayCheckpoint()  => PlaySfx("SFX_Finish"); // reuses the finish sound for now
    public void PlayPickup()      => PlaySfxRandom("SFX_PickUp_1", "SFX_PickUp_2");
    public void PlayRespawn()     => PlaySfxRandom("SFX_Respawn_1", "SFX_Respawn_2");
    public void PlayMenuSelect()  => PlaySfxRandom("SFX_MenuSelect_1", "SFX_MenuSelect_2");
    public void PlayStartFlag()   => PlaySfx("SFX_Spring_Boing"); // arrow sign bend-and-flick
    public void PlayPigCharge()   => PlaySfx("SFX_Pig", 3f);      // AngryPig turns red and charges (clip is quiet, so 3x)

    // ─── Footsteps ──────────────────────────────────────────────────────────────
    // The imported "Footsteps - Essentials" pack keeps one folder per surface, each
    // holding numbered "Walk" variants. Levels 1-3 are all wood, so PlayFootstep
    // defaults to the wood set; when a level introduces new terrain, add a Surface
    // value and a case below. Footsteps are scaled down so they sit under the other
    // SFX instead of drowning them out.
    public enum Surface { Wood }

    private const string FootstepsRoot = "Footsteps - Essentials";
    private const float FootstepVolume = 1.0f;

    // Alternates between the two clips below (left/right foot) rather than picking at
    // random, which sounded uneven. Flips 0 <-> 1 on each step.
    private int footstepIndex;

    /// <summary>Plays the next walk step for the given surface (wood by default),
    /// cycling through a small set of clips so steps alternate like footfalls.</summary>
    public void PlayFootstep(Surface surface = Surface.Wood)
    {
        switch (surface)
        {
            case Surface.Wood:
                // Cycle Footsteps_Wood_Walk_01 <-> _02 for a steady left/right cadence.
                int n = footstepIndex + 1;
                footstepIndex = (footstepIndex + 1) % 2;
                PlaySfx($"{FootstepsRoot}/Footsteps_Wood/Footsteps_Wood_Walk/Footsteps_Wood_Walk_{n:00}", FootstepVolume);
                break;
        }
    }

    /// <summary>Plays a landing thud for the given surface (wood by default) — call
    /// this the moment the player touches down after being airborne.</summary>
    public void PlayLand(Surface surface = Surface.Wood)
    {
        switch (surface)
        {
            case Surface.Wood:
                // One-shot on landing, so a random pick of the two variants reads fine.
                int n = Random.Range(1, 3); // 01 or 02
                PlaySfx($"{FootstepsRoot}/Footsteps_Wood/Footsteps_Wood_Jump/Footsteps_Wood_Jump_Land_{n:00}", FootstepVolume);
                break;
        }
    }

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

    // ─── Wall slide (looping) ─────────────────────────────────────────────────────

    /// <summary>Turns the looping wall-slide friction sound on/off instantly. Player
    /// calls this every frame with its current slide state, so setting the volume here
    /// also keeps it tracking the SFX slider live while a slide is held.</summary>
    public void SetWallSliding(bool sliding)
    {
        if (wallSlideSource == null) return;

        if (sliding)
        {
            wallSlideSource.volume = WallSlideVolume * EffectiveSfxVolume;
            if (wallSlideSource.clip != null && !wallSlideSource.isPlaying)
                wallSlideSource.Play();
        }
        else if (wallSlideSource.isPlaying)
        {
            wallSlideSource.Stop();
        }
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
