using System.Collections;
using UnityEngine;

/// <summary>Menu/round playlists, live volume controls, and a temporary victory music crossfade.</summary>
[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    [Header("Start Menu")]
    [Tooltip("The song that loops while the start menu is visible.")]
    [SerializeField] private AudioClip menuSong;

    [Header("Music Levels")]
    [Tooltip("Overall music level before the Music slider; set slightly lower than sound effects by default.")]
    [SerializeField, Range(0.25f, 1f)] private float musicLevelMultiplier = 0.6f;
    [Tooltip("Music volume multiplier while the pause menu is open.")]
    [SerializeField, Range(0f, 1f)] private float pausedVolumeMultiplier = 0.15f;
    [Tooltip("Time in seconds for the music to return to normal volume after unpausing.")]
    [SerializeField, Min(0f)] private float resumeVolumeFadeDuration = 0.5f;

    [Header("Gameplay Playlist")]
    [Tooltip("Songs play in this order, then the playlist starts over.")]
    [SerializeField] private AudioClip[] gameplaySongs = new AudioClip[0];

    [Header("Victory Music")]
    [Tooltip("Time for the final results music to replace the course playlist.")]
    [SerializeField, Range(0f, 2f)] private float victoryCrossfadeDuration = 0.6f;

    private AudioSource musicSource;
    private AudioSource victorySource;
    private bool playingGameplayMusic;
    private int nextGameplaySongIndex;
    private float normalMusicVolume = 1f;
    private Coroutine volumeFadeCoroutine;
    private Coroutine victoryFadeCoroutine;
    private bool isPaused;
    private bool victoryActive;
    private bool resumeMainWasPlaying;
    private float pauseGain = 1f;
    private float mainGain = 1f;
    private float victoryGain;

    public bool IsPlayingVictoryMusic => victoryActive;

    private void OnEnable() => GameSettings.MusicVolumeChanged += HandleMusicVolumeChanged;

    private void OnDisable()
    {
        GameSettings.MusicVolumeChanged -= HandleMusicVolumeChanged;
        StopVolumeFade();
        StopVictoryFade();
        if (victorySource != null) victorySource.Stop();
        victoryActive = false;
        mainGain = 1f;
        victoryGain = 0f;
    }

    private void Awake()
    {
        musicSource = GetComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.ignoreListenerPause = true;
        normalMusicVolume = musicSource.volume;
        musicSource.Stop();
        ApplyVolumes();
    }

    private void Update()
    {
        ApplyVolumes();
        if (musicSource != null && playingGameplayMusic && !victoryActive && !musicSource.isPlaying)
            PlayNextGameplaySong();
    }

    public void SetPaused(bool paused)
    {
        EnsureMusicSource();
        isPaused = paused;
        StopVolumeFade();
        if (paused)
        {
            pauseGain = pausedVolumeMultiplier;
            ApplyVolumes();
            return;
        }
        if (resumeVolumeFadeDuration <= 0f || Mathf.Approximately(pauseGain, 1f))
        {
            pauseGain = 1f;
            ApplyVolumes();
        }
        else volumeFadeCoroutine = StartCoroutine(FadeVolumeToNormal());
    }

    private IEnumerator FadeVolumeToNormal()
    {
        float start = pauseGain;
        float elapsed = 0f;
        while (elapsed < resumeVolumeFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            pauseGain = Mathf.Lerp(start, 1f, Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(0.001f, resumeVolumeFadeDuration)));
            ApplyVolumes();
            yield return null;
        }
        pauseGain = 1f;
        ApplyVolumes();
        volumeFadeCoroutine = null;
    }

    private float GetNormalMusicVolume() => normalMusicVolume * musicLevelMultiplier * GameSettings.MusicVolume;
    private float GetPausedMusicVolume() => GetNormalMusicVolume() * pausedVolumeMultiplier;
    private void HandleMusicVolumeChanged() => ApplyVolumes();

    private void ApplyVolumes()
    {
        float gain = isPaused ? GetPausedMusicVolume() : GetNormalMusicVolume() * pauseGain;
        if (musicSource != null) musicSource.volume = gain * mainGain;
        if (victorySource != null) victorySource.volume = gain * victoryGain;
    }

    private void EnsureMusicSource()
    {
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
    }

    private void StopVolumeFade()
    {
        if (volumeFadeCoroutine == null) return;
        StopCoroutine(volumeFadeCoroutine);
        volumeFadeCoroutine = null;
    }

    private void StopVictoryFade()
    {
        if (victoryFadeCoroutine == null) return;
        StopCoroutine(victoryFadeCoroutine);
        victoryFadeCoroutine = null;
    }

    /// <summary>Temporarily replaces the playlist. Assigned menu and gameplay songs remain untouched.</summary>
    public void BeginVictoryMusic(AudioClip clip)
    {
        if (clip == null) return;
        EnsureMusicSource();
        if (victoryActive && victorySource != null && victorySource.clip == clip) return;
        if (!victoryActive) resumeMainWasPlaying = musicSource != null && musicSource.isPlaying;
        StopVictoryFade();
        if (victorySource == null)
        {
            victorySource = gameObject.AddComponent<AudioSource>();
            victorySource.playOnAwake = false;
            victorySource.spatialBlend = 0f;
            victorySource.ignoreListenerPause = true;
        }
        victorySource.clip = clip;
        victorySource.loop = true;
        victoryActive = true;
        ApplyVolumes();
        victorySource.Play();
        victoryFadeCoroutine = StartCoroutine(FadeVictoryMix(true));
    }

    public void EndVictoryMusic()
    {
        if (!victoryActive) return;
        StopVictoryFade();
        victoryActive = false;
        if (resumeMainWasPlaying && musicSource != null && musicSource.clip != null) musicSource.UnPause();
        victoryFadeCoroutine = StartCoroutine(FadeVictoryMix(false));
    }

    private IEnumerator FadeVictoryMix(bool entering)
    {
        float startMain = mainGain, startVictory = victoryGain;
        float duration = Mathf.Max(0.001f, victoryCrossfadeDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            mainGain = Mathf.Lerp(startMain, entering ? 0f : 1f, t);
            victoryGain = Mathf.Lerp(startVictory, entering ? 1f : 0f, t);
            ApplyVolumes();
            yield return null;
        }
        mainGain = entering ? 0f : 1f;
        victoryGain = entering ? 1f : 0f;
        ApplyVolumes();
        if (entering)
        {
            if (musicSource != null) musicSource.Pause();
        }
        else if (victorySource != null)
        {
            victorySource.Stop();
            victorySource.clip = null;
        }
        victoryFadeCoroutine = null;
    }

    private void CancelVictoryForPlaylist()
    {
        StopVictoryFade();
        victoryActive = false;
        mainGain = 1f;
        victoryGain = 0f;
        resumeMainWasPlaying = false;
        if (victorySource != null) { victorySource.Stop(); victorySource.clip = null; }
    }

    public void PlayMenuMusic()
    {
        CancelVictoryForPlaylist();
        playingGameplayMusic = false;
        EnsureMusicSource();
        if (musicSource == null) return;
        SetPaused(false);
        musicSource.Stop();
        musicSource.loop = true;
        musicSource.clip = menuSong;
        ApplyVolumes();
        if (menuSong != null) musicSource.Play();
    }

    public void PlayGameplayMusic()
    {
        CancelVictoryForPlaylist();
        EnsureMusicSource();
        if (musicSource == null) return;
        SetPaused(false);
        musicSource.Stop();
        musicSource.loop = false;
        nextGameplaySongIndex = 0;
        playingGameplayMusic = true;
        PlayNextGameplaySong();
    }

    private void PlayNextGameplaySong()
    {
        if (gameplaySongs == null || gameplaySongs.Length == 0)
        {
            playingGameplayMusic = false;
            musicSource.Stop();
            return;
        }
        for (int checkedSongs = 0; checkedSongs < gameplaySongs.Length; checkedSongs++)
        {
            AudioClip clip = gameplaySongs[nextGameplaySongIndex];
            nextGameplaySongIndex = (nextGameplaySongIndex + 1) % gameplaySongs.Length;
            if (clip == null) continue;
            musicSource.clip = clip;
            musicSource.loop = false;
            musicSource.Play();
            return;
        }
        playingGameplayMusic = false;
        musicSource.Stop();
    }
}

