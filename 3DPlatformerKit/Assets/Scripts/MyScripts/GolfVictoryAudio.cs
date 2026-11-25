using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Soft synthesized celebration defaults, plus optional Inspector clip overrides.</summary>
[DisallowMultipleComponent]
public sealed class GolfVictoryAudio : MonoBehaviour
{
    [Header("Victory Music")]
    [Tooltip("Optional looping song for the trophy screen. An original upbeat bell-and-pad loop is generated when empty.")]
    [SerializeField] private AudioClip victoryMusic;
    [Header("Celebration Sound Overrides")]
    [SerializeField] private AudioClip courseFanfare;
    [SerializeField] private AudioClip bronzeReveal;
    [SerializeField] private AudioClip silverReveal;
    [SerializeField] private AudioClip goldReveal;
    [SerializeField] private AudioClip diamondReveal;
    [SerializeField] private AudioClip newRecordSound;
    [Header("Button Sound Overrides")]
    [SerializeField] private AudioClip buttonHoverSound;
    [SerializeField] private AudioClip buttonClickSound;
    [SerializeField, Range(0f, 1f)] private float celebrationVolume = 0.65f;
    [SerializeField, Range(0f, 1f)] private float buttonVolume = 0.42f;

    private MusicManager music;
    private AudioSource effectsSource, uiSource, fallbackMusicSource;
    private AudioClip generatedMusic, generatedHover, generatedClick, generatedRecord;
    private readonly AudioClip[] generatedFanfares = new AudioClip[4];
    private readonly AudioClip[] generatedReveals = new AudioClip[4];
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private AudioClip legacyFanfare;
    private int trophyTier = 1;
    private bool active;
    private float fallbackGain;
    private Coroutine warmMusicRoutine;

    public AudioSource UiSource { get { EnsureSources(); return uiSource; } }
    public AudioClip HoverClip
    {
        get
        {
            if (buttonHoverSound != null) return buttonHoverSound;
            if (generatedHover == null) generatedHover = CreateUiSound(false);
            return generatedHover;
        }
    }

    public void SetCourseFanfareFallback(AudioClip clip) { legacyFanfare = clip; }
    private void Awake() => EnsureSources();

    private void Start()
    {
        // Prepare one bar per frame while the title/course is running, keeping the final reveal smooth.
        if (victoryMusic == null && generatedMusic == null) warmMusicRoutine = StartCoroutine(WarmMusic());
    }

    private IEnumerator WarmMusic()
    {
        var sound = new SoundBuffer(16f, true);
        yield return null;
        for (int bar = 0; bar < 8; bar++)
        {
            AddMusicBar(sound, bar);
            yield return null;
        }
        generatedMusic = Finish(sound, "Aero victory lounge loop", 0.55f);
        warmMusicRoutine = null;
    }

    private void EnsureSources()
    {
        if (effectsSource == null)
        {
            effectsSource = gameObject.AddComponent<AudioSource>();
            effectsSource.playOnAwake = false;
            effectsSource.spatialBlend = 0f;
            effectsSource.dopplerLevel = 0f;
        }
        if (uiSource == null)
        {
            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.spatialBlend = 0f;
            uiSource.ignoreListenerPause = true;
            uiSource.dopplerLevel = 0f;
        }
        if (music == null) music = FindFirstObjectByType<MusicManager>();
    }

    /// <param name="tier">1 = bronze, 2 = silver, 3 = gold, 4 = diamond.</param>
    public void Begin(int tier)
    {
        EnsureSources();
        trophyTier = Mathf.Clamp(tier, 1, 4);
        active = true;
        effectsSource.Stop();
        AudioClip song = victoryMusic;
        if (song == null)
        {
            if (generatedMusic == null)
            {
                if (warmMusicRoutine != null) { StopCoroutine(warmMusicRoutine); warmMusicRoutine = null; }
                generatedMusic = CreateVictoryMusic();
            }
            song = generatedMusic;
        }
        if (music != null) music.BeginVictoryMusic(song);
        else
        {
            if (fallbackMusicSource == null)
            {
                fallbackMusicSource = gameObject.AddComponent<AudioSource>();
                fallbackMusicSource.playOnAwake = false;
                fallbackMusicSource.spatialBlend = 0f;
                fallbackMusicSource.ignoreListenerPause = true;
            }
            fallbackMusicSource.loop = true;
            fallbackMusicSource.clip = song;
            fallbackGain = 0f;
            fallbackMusicSource.volume = 0f;
            fallbackMusicSource.Play();
        }
        AudioClip fanfare = courseFanfare != null ? courseFanfare : legacyFanfare;
        int slot = trophyTier - 1;
        if (fanfare == null)
        {
            if (generatedFanfares[slot] == null) generatedFanfares[slot] = CreateFanfare(trophyTier);
            fanfare = generatedFanfares[slot];
        }
        GameSettings.PlaySfx(effectsSource, fanfare, celebrationVolume);
    }

    public void PlayTrophyReveal()
    {
        if (!active) return;
        AudioClip clip = trophyTier == 4 ? diamondReveal : trophyTier == 3 ? goldReveal : trophyTier == 2 ? silverReveal : bronzeReveal;
        int slot = trophyTier - 1;
        if (clip == null)
        {
            if (generatedReveals[slot] == null) generatedReveals[slot] = CreateReveal(trophyTier);
            clip = generatedReveals[slot];
        }
        GameSettings.PlaySfx(effectsSource, clip, celebrationVolume * 0.8f);
    }

    public void PlayRecordAccent()
    {
        if (!active) return;
        if (newRecordSound == null && generatedRecord == null) generatedRecord = CreateRecordSound();
        GameSettings.PlaySfx(effectsSource, newRecordSound != null ? newRecordSound : generatedRecord, celebrationVolume * 0.8f);
    }

    public void PlayUiHover() => GameSettings.PlaySfx(UiSource, HoverClip, buttonVolume * 0.55f);

    public void PlayUiClick()
    {
        if (buttonClickSound == null && generatedClick == null) generatedClick = CreateUiSound(true);
        GameSettings.PlaySfx(UiSource, buttonClickSound != null ? buttonClickSound : generatedClick, buttonVolume);
    }

    public void End()
    {
        if (!active) return;
        active = false;
        if (effectsSource != null) effectsSource.Stop();
        if (music != null) music.EndVictoryMusic();
        if (fallbackMusicSource != null) fallbackMusicSource.Stop();
        // A UI click uses its own source, so the click survives a menu transition.
    }

    private void Update()
    {
        if (fallbackMusicSource == null || !active) return;
        fallbackGain = Mathf.MoveTowards(fallbackGain, 1f, Time.unscaledDeltaTime / 0.6f);
        float pause = Time.timeScale <= 0f || AudioListener.pause ? 0.15f : 1f;
        fallbackMusicSource.volume = 0.34f * GameSettings.MusicVolume * fallbackGain * pause;
    }

    private void OnDisable()
    {
        End();
        if (warmMusicRoutine != null) { StopCoroutine(warmMusicRoutine); warmMusicRoutine = null; }
    }
    private void OnDestroy()
    {
        End();
        foreach (AudioClip clip in ownedClips)
            if (clip != null)
            {
                if (Application.isPlaying) Destroy(clip);
                else DestroyImmediate(clip);
            }
        ownedClips.Clear();
    }

    private AudioClip CreateFanfare(int tier)
    {
        var sound = new SoundBuffer(1.8f, false);
        int[] notes = { 60, 64, 67, 72, 76, 79 };
        float[] starts = { 0f, 0.1f, 0.23f, 0.42f, 0.62f, 0.86f };
        for (int i = 0; i < notes.Length; i++)
            sound.Note(notes[i], starts[i], 0.65f, 0.11f + i * 0.01f, 0, (i % 2 == 0 ? -0.25f : 0.25f));
        sound.Note(48, 0.0f, 1.5f, 0.1f, 1, 0f);
        sound.Note(55, 0.04f, 1.4f, 0.06f, 1, -0.15f);
        if (tier >= 2) sound.Note(84, 1.02f, 0.7f, 0.09f, 0, -0.35f);
        if (tier >= 3) sound.Note(88, 1.13f, 0.6f, 0.08f, 0, 0.35f);
        if (tier >= 4) sound.Note(91, 1.25f, 0.5f, 0.07f, 0, 0f);
        return Finish(sound, "Aero course fanfare " + tier, 0.76f);
    }

    private AudioClip CreateReveal(int tier)
    {
        var sound = new SoundBuffer(1.2f, false);
        int root = tier >= 3 ? 72 : 60;
        sound.Note(root, 0f, 0.85f, 0.18f, 0, -0.2f);
        sound.Note(root + 4, 0.045f, 0.85f, 0.14f, 0, 0.2f);
        sound.Note(root + 7, 0.09f, 0.85f, 0.12f, 0, 0f);
        sound.Note(root + 12, 0.17f, 0.8f, 0.1f, 0, -0.25f);
        sound.Note(48, 0f, 0.4f, 0.07f, 2, 0f);
        if (tier == 4)
        {
            sound.Note(91, 0.25f, 0.6f, 0.065f, 0, -0.5f);
            sound.Note(96, 0.33f, 0.65f, 0.045f, 0, 0.5f);
        }
        return Finish(sound, "Aero trophy chime " + tier, 0.62f);
    }

    private AudioClip CreateRecordSound()
    {
        var sound = new SoundBuffer(1.5f, false);
        int[] notes = { 72, 74, 76, 79, 81, 84 };
        for (int i = 0; i < notes.Length; i++) sound.Note(notes[i], i * 0.095f, 0.8f, 0.1f, 0, Mathf.Sin(i * 1.2f) * 0.4f);
        return Finish(sound, "Aero personal best flourish", 0.62f);
    }

    private AudioClip CreateUiSound(bool click)
    {
        var sound = new SoundBuffer(click ? 0.16f : 0.12f, false);
        sound.Note(click ? 79 : 84, 0f, click ? 0.14f : 0.1f, 0.12f, 0, 0f);
        if (click) sound.Note(72, 0.025f, 0.12f, 0.055f, 0, 0f);
        return Finish(sound, click ? "Aero soft button pop" : "Aero button bubble", click ? 0.3f : 0.22f);
    }

    private AudioClip CreateVictoryMusic()
    {
        // Eight bars at 120 BPM. Tail samples wrap into the start for a seamless loop.
        var sound = new SoundBuffer(16f, true);
        for (int bar = 0; bar < 8; bar++) AddMusicBar(sound, bar);
        return Finish(sound, "Aero victory lounge loop", 0.55f);
    }

    private static void AddMusicBar(SoundBuffer sound, int bar)
    {
        int[] roots = { 60, 57, 53, 55, 60, 57, 53, 55 };
        float start = bar * 2f;
        int root = roots[bar];
        int third = root == 57 ? 3 : 4;
        sound.Note(root, start, 2.35f, 0.038f, 1, -0.3f);
        sound.Note(root + third, start + 0.015f, 2.3f, 0.027f, 1, 0.3f);
        sound.Note(root + 7, start + 0.03f, 2.25f, 0.025f, 1, 0f);
        sound.Note(root - 12, start, 0.62f, 0.09f, 2, 0f);
        sound.Note(root - 5, start + 1f, 0.55f, 0.07f, 2, 0f);
        int[] melody = { 12, 12 + third, 19, bar % 2 == 0 ? 24 : 19, 12 + third };
        float[] timing = { 0f, 0.5f, 1f, 1.5f, 1.75f };
        for (int i = 0; i < melody.Length; i++)
            sound.Note(root + melody[i], start + timing[i], 0.42f, i == 3 ? 0.05f : 0.042f, 0, (i % 2 == 0 ? -0.25f : 0.25f));
        for (int beat = 0; beat < 4; beat++)
        {
            sound.Kick(start + beat * 0.5f, beat % 2 == 0 ? 0.042f : 0.026f);
            sound.Shaker(start + beat * 0.5f + 0.25f, 0.007f, beat % 2 == 0 ? -0.45f : 0.45f);
        }
    }
    private AudioClip Finish(SoundBuffer buffer, string name, float peak)
    {
        AudioClip clip = buffer.ToClip(name, peak);
        ownedClips.Add(clip);
        return clip;
    }

    /// <summary>Band-limited harmonic tones with smooth envelopes. No downloaded audio is required.</summary>
    private sealed class SoundBuffer
    {
        private const int Rate = 24000;
        private readonly int frames;
        private readonly bool wrap;
        private readonly float[] samples;
        public SoundBuffer(float seconds, bool looping)
        {
            frames = Mathf.CeilToInt(seconds * Rate);
            samples = new float[frames * 2];
            wrap = looping;
        }
        public void Note(int midi, float start, float duration, float amplitude, int timbre, float pan)
        {
            float frequency = 440f * Mathf.Pow(2f, (midi - 69f) / 12f);
            int offset = Mathf.RoundToInt(start * Rate), length = Mathf.CeilToInt(duration * Rate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float attack = Mathf.Clamp01(t / (timbre == 1 ? 0.16f : 0.006f));
                attack = attack * attack * (3f - 2f * attack);
                float release = Mathf.Clamp01((duration - t) / (timbre == 1 ? 0.48f : 0.045f));
                float phase = Mathf.PI * 2f * frequency * t;
                float tone;
                if (timbre == 0)
                    tone = (Mathf.Sin(phase) + 0.2f * Mathf.Sin(phase * 2.001f) * Mathf.Exp(-t * 12f) +
                        0.07f * Mathf.Sin(phase * 3.002f) * Mathf.Exp(-t * 18f)) * Mathf.Exp(-t * 5f);
                else if (timbre == 1)
                    tone = (Mathf.Sin(phase) * 0.8f + Mathf.Sin(phase * 2f) * 0.12f + Mathf.Sin(phase * 3f) * 0.035f) * 0.75f;
                else tone = (Mathf.Sin(phase) + 0.1f * Mathf.Sin(phase * 2f)) * Mathf.Exp(-t * 5.5f);
                Add(offset + i, tone * attack * release * amplitude, pan);
            }
        }
        public void Kick(float start, float amplitude)
        {
            int offset = Mathf.RoundToInt(start * Rate);
            int length = Mathf.CeilToInt(0.22f * Rate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float phase = Mathf.PI * 2f * (44f * t + 2.4f * (1f - Mathf.Exp(-t * 28f)));
                Add(offset + i, Mathf.Sin(phase) * Mathf.Clamp01(t / 0.004f) * Mathf.Exp(-t * 25f) * amplitude, 0f);
            }
        }
        public void Shaker(float start, float amplitude, float pan)
        {
            uint seed = 1273u + (uint)Mathf.RoundToInt(start * Rate);
            int offset = Mathf.RoundToInt(start * Rate), length = Mathf.CeilToInt(0.06f * Rate);
            float previous = 0f;
            for (int i = 0; i < length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 9) / 8388608f) * 2f - 1f;
                float high = noise - previous; previous = noise;
                float t = i / (float)Rate;
                Add(offset + i, high * Mathf.Clamp01(t / 0.003f) * Mathf.Exp(-t * 65f) * amplitude, pan);
            }
        }
        private void Add(int frame, float value, float pan)
        {
            if (wrap) frame %= frames;
            if (frame < 0 || frame >= frames) return;
            float right = Mathf.Sqrt((Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.5f);
            float left = Mathf.Sqrt(1f - right * right);
            samples[frame * 2] += value * left;
            samples[frame * 2 + 1] += value * right;
        }
        public AudioClip ToClip(string name, float desiredPeak)
        {
            float maximum = 0.001f;
            for (int i = 0; i < samples.Length; i++) maximum = Mathf.Max(maximum, Mathf.Abs(samples[i]));
            float gain = Mathf.Min(4f, desiredPeak / maximum);
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i] * gain, -0.95f, 0.95f);
            var clip = AudioClip.Create(name, frames, 2, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}



