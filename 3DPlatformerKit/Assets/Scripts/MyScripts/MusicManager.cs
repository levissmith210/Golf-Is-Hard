using System.Collections;
using UnityEngine;

/// <summary>
/// Plays one looping song on the title menu, then cycles through the gameplay playlist in order.
/// </summary>
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

    private AudioSource musicSource;
    private bool playingGameplayMusic;
    private int nextGameplaySongIndex;
    private float normalMusicVolume = 1f;
    private Coroutine volumeFadeCoroutine;
    private bool isPaused;

    private void OnEnable()
    {
        GameSettings.MusicVolumeChanged += HandleMusicVolumeChanged;
    }

    private void OnDisable()
    {
        GameSettings.MusicVolumeChanged -= HandleMusicVolumeChanged;
        StopVolumeFade();
    }

    private void Awake()
    {
        musicSource = GetComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.ignoreListenerPause = true;
        normalMusicVolume = musicSource.volume;
        musicSource.volume = GetNormalMusicVolume();
        musicSource.Stop();
    }

    private void Update()
    {
        if (musicSource == null)
            return;

        // Keep the live title-menu song synced with its slider as well as gameplay music.
        if (volumeFadeCoroutine == null)
        {
            float targetVolume = isPaused ? GetPausedMusicVolume() : GetNormalMusicVolume();
            if (!Mathf.Approximately(musicSource.volume, targetVolume))
                musicSource.volume = targetVolume;
        }

        if (!playingGameplayMusic)
            return;

        if (!musicSource.isPlaying)
            PlayNextGameplaySong();
    }

    public void SetPaused(bool paused)
    {
        if (musicSource == null)
            musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
            return;

        isPaused = paused;
        StopVolumeFade();

        if (isPaused)
        {
            musicSource.volume = GetPausedMusicVolume();
            return;
        }

        if (Mathf.Approximately(musicSource.volume, GetNormalMusicVolume()))
            return;

        if (resumeVolumeFadeDuration <= 0f)
        {
            musicSource.volume = GetNormalMusicVolume();
            return;
        }

        volumeFadeCoroutine = StartCoroutine(FadeVolumeToNormal());
    }

    private IEnumerator FadeVolumeToNormal()
    {
        float startVolume = musicSource.volume;
        float elapsed = 0f;
        float duration = resumeVolumeFadeDuration;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            musicSource.volume = Mathf.Lerp(startVolume, GetNormalMusicVolume(), Mathf.SmoothStep(0f, 1f, progress));
            yield return null;
        }

        musicSource.volume = GetNormalMusicVolume();
        volumeFadeCoroutine = null;
    }

    private float GetNormalMusicVolume()
    {
        return normalMusicVolume * musicLevelMultiplier * GameSettings.MusicVolume;
    }

    private float GetPausedMusicVolume()
    {
        return GetNormalMusicVolume() * pausedVolumeMultiplier;
    }

    private void HandleMusicVolumeChanged()
    {
        if (musicSource == null)
            return;

        if (isPaused)
        {
            StopVolumeFade();
            musicSource.volume = GetPausedMusicVolume();
            return;
        }

        if (volumeFadeCoroutine != null)
        {
            StopVolumeFade();
            if (resumeVolumeFadeDuration > 0f
                && !Mathf.Approximately(musicSource.volume, GetNormalMusicVolume()))
            {
                volumeFadeCoroutine = StartCoroutine(FadeVolumeToNormal());
            }
            else
            {
                musicSource.volume = GetNormalMusicVolume();
            }
            return;
        }

        musicSource.volume = GetNormalMusicVolume();
    }

    private void StopVolumeFade()
    {
        if (volumeFadeCoroutine == null)
            return;

        StopCoroutine(volumeFadeCoroutine);
        volumeFadeCoroutine = null;
    }

    public void PlayMenuMusic()
    {
        playingGameplayMusic = false;
        if (musicSource == null)
            musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
            return;

        SetPaused(false);
        musicSource.Stop();
        musicSource.loop = true;
        musicSource.clip = menuSong;
        if (menuSong != null)
            musicSource.Play();
    }

    public void PlayGameplayMusic()
    {
        if (musicSource == null)
            musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
            return;

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
            if (clip == null)
                continue;

            musicSource.clip = clip;
            musicSource.loop = false;
            musicSource.Play();
            return;
        }

        playingGameplayMusic = false;
        musicSource.Stop();
    }
}
