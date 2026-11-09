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

    [Header("Pause Mix")]
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

    private void Awake()
    {
        musicSource = GetComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.ignoreListenerPause = true;
        normalMusicVolume = musicSource.volume;
        musicSource.Stop();
    }

    private void Update()
    {
        if (!playingGameplayMusic || musicSource == null)
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

        if (volumeFadeCoroutine != null)
        {
            StopCoroutine(volumeFadeCoroutine);
            volumeFadeCoroutine = null;
        }

        if (paused)
        {
            musicSource.volume = normalMusicVolume * pausedVolumeMultiplier;
            return;
        }

        if (Mathf.Approximately(musicSource.volume, normalMusicVolume))
            return;

        if (resumeVolumeFadeDuration <= 0f)
        {
            musicSource.volume = normalMusicVolume;
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
            musicSource.volume = Mathf.Lerp(startVolume, normalMusicVolume, Mathf.SmoothStep(0f, 1f, progress));
            yield return null;
        }

        musicSource.volume = normalMusicVolume;
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