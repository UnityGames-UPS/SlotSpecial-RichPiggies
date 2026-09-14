using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Every sound in the game. All sources are created at runtime, so the only Inspector wiring
/// is the clips. Every sound fades in; music crossfades between the base and free-spin tracks.
/// </summary>
public class AudioManager : MonoBehaviour
{
    internal static AudioManager Instance { get; private set; }

    private const string PrefKeyMusic    = "audio_music_enabled";
    private const string PrefKeysfx      = "audio_sfx_enabled";
    private const string PrefKeyMusicVol = "audio_music_volume";
    private const string PrefKeySfxVol   = "audio_sfx_volume";

    [Header("Music")]
    [SerializeField] private AudioClip clipBg;
    [SerializeField] private AudioClip clipFreeSpinBg;

    [Header("SFX")]
    [SerializeField] private AudioClip clipButton;
    [SerializeField] private AudioClip clipMaxBet;
    [SerializeField] private AudioClip clipNormalWin;
    [SerializeField] private AudioClip clipBigWins;
    [SerializeField] private AudioClip clipCongratulations;
    [SerializeField] private AudioClip clipFreeSpinRewarded;
    [SerializeField] private AudioClip clipMeterAdd;
    [SerializeField] private AudioClip clipJackpotMultiplierIncrease;
    [SerializeField] private AudioClip clipMysteryReveal;
    [SerializeField] private AudioClip clipReelSpinning;
    [SerializeField] private AudioClip clipReelStop;

    [Header("Fades")]
    [SerializeField] private float musicCrossfadeDuration = 1f;
    [SerializeField] private float sfxFadeInDuration = 0.08f;
    [SerializeField] private float sfxFadeOutDuration = 0.25f;

    [Tooltip("Fade-in for the reel-stop thud. A percussive hit loses its attack under any real " +
             "fade and reads as landing late, so this defaults to none.")]
    [SerializeField] private float reelStopFadeInDuration = 0f;

    [Tooltip("Fade-out of the reel-spinning loop once the last reel starts landing.")]
    [SerializeField] private float reelSpinFadeOutDuration = 0.12f;

    [Tooltip("How many SFX can overlap. The oldest one is cut when all are busy.")]
    [SerializeField] private int sfxPoolSize = 8;

    private AudioSource musicA;
    private AudioSource musicB;
    private AudioSource activeMusic;
    private AudioSource reelSpinSource;
    private AudioSource winSource;
    private readonly List<AudioSource> sfxPool = new List<AudioSource>();
    private readonly Dictionary<AudioSource, float> sfxStartTimes = new Dictionary<AudioSource, float>();

    private bool _musicEnabled = true;
    private bool _sfxEnabled   = true;
    private float _musicVolume = 0.5f;
    private float _sfxVolume   = 1.0f;

    internal bool MusicEnabled => _musicEnabled;
    internal bool SfxEnabled   => _sfxEnabled;
    internal float MusicVolume => _musicVolume;
    internal float SfxVolume   => _sfxVolume;

    private float MusicTargetVolume => _musicEnabled ? _musicVolume : 0f;
    private float SfxTargetVolume   => _sfxEnabled ? _sfxVolume : 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _musicEnabled = PlayerPrefs.GetInt(PrefKeyMusic, 1) == 1;
        _sfxEnabled   = PlayerPrefs.GetInt(PrefKeysfx,   1) == 1;
        _musicVolume  = PlayerPrefs.GetFloat(PrefKeyMusicVol, 0.5f);
        _sfxVolume    = PlayerPrefs.GetFloat(PrefKeySfxVol,   1.0f);

        musicA         = CreateSource(loop: true);
        musicB         = CreateSource(loop: true);
        reelSpinSource = CreateSource(loop: true);
        winSource      = CreateSource(loop: false);
        for (int i = 0; i < Mathf.Max(1, sfxPoolSize); i++)
            sfxPool.Add(CreateSource(loop: false));

        activeMusic = musicA;
    }

    private void Start()
    {
        PlayBgMusic();
    }

    private AudioSource CreateSource(bool loop)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake  = false;
        source.loop         = loop;
        source.spatialBlend = 0f;
        source.volume       = 0f;
        return source;
    }

    #region Settings

    internal void SetMusicEnabled(bool on)
    {
        _musicEnabled = on;
        PlayerPrefs.SetInt(PrefKeyMusic, on ? 1 : 0);
        PlayerPrefs.Save();
        ApplyMusicVolume();
    }

    internal void SetSfxEnabled(bool on)
    {
        _sfxEnabled = on;
        PlayerPrefs.SetInt(PrefKeysfx, on ? 1 : 0);
        PlayerPrefs.Save();
        ApplySfxVolume();
    }

    internal void SetMusicVolume(float volume)
    {
        _musicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeyMusicVol, _musicVolume);
        PlayerPrefs.Save();
        ApplyMusicVolume();
    }

    internal void SetSfxVolume(float volume)
    {
        _sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeySfxVol, _sfxVolume);
        PlayerPrefs.Save();
        ApplySfxVolume();
    }

    /// <summary>
    /// Snap the playing music to the new volume. A source that is fading OUT is left alone — it
    /// is on its way to silence either way.
    /// </summary>
    private void ApplyMusicVolume()
    {
        SnapIfPlaying(activeMusic, MusicTargetVolume);
    }

    private void ApplySfxVolume()
    {
        float v = SfxTargetVolume;
        foreach (var source in sfxPool) SnapIfPlaying(source, v);
        SnapIfPlaying(winSource, v);
        SnapIfPlaying(reelSpinSource, v);
    }

    private void SnapIfPlaying(AudioSource source, float volume)
    {
        if (source == null || !source.isPlaying || fadingOut.Contains(source)) return;
        DOTween.Kill(source);
        source.volume = volume;
    }

    #endregion

    #region Fades

    // Sources currently fading to silence. A settings change must not snap these back up,
    // since killing their tween would also skip the Stop at its end.
    private readonly HashSet<AudioSource> fadingOut = new HashSet<AudioSource>();

    private Tween FadeVolume(AudioSource source, float target, float duration)
    {
        DOTween.Kill(source);
        fadingOut.Remove(source);
        return DOTween.To(() => source.volume, v => source.volume = v, target, Mathf.Max(0f, duration))
                      .SetTarget(source)
                      .SetUpdate(true);
    }

    private void FadeIn(AudioSource source, AudioClip clip, float targetVolume, float duration)
    {
        DOTween.Kill(source);
        fadingOut.Remove(source);
        source.clip   = clip;
        source.volume = 0f;
        source.Play();
        FadeVolume(source, targetVolume, duration);
    }

    private void FadeOutAndStop(AudioSource source, float duration)
    {
        if (source == null || !source.isPlaying) return;
        FadeVolume(source, 0f, duration).OnComplete(() =>
        {
            fadingOut.Remove(source);
            source.Stop();
        });
        fadingOut.Add(source);
    }

    #endregion

    #region Music

    private void CrossfadeTo(AudioClip clip)
    {
        if (clip == null || musicA == null) return;
        if (activeMusic.isPlaying && activeMusic.clip == clip) return;

        AudioSource outgoing = activeMusic;
        AudioSource incoming = activeMusic == musicA ? musicB : musicA;

        FadeOutAndStop(outgoing, musicCrossfadeDuration);
        FadeIn(incoming, clip, MusicTargetVolume, musicCrossfadeDuration);
        activeMusic = incoming;
    }

    internal void PlayBgMusic()    => CrossfadeTo(clipBg);
    internal void PlayMainBg()     => PlayBgMusic();
    internal void PlayFreeSpinBg() => CrossfadeTo(clipFreeSpinBg);

    internal void StopBgMusic()
    {
        FadeOutAndStop(musicA, musicCrossfadeDuration);
        FadeOutAndStop(musicB, musicCrossfadeDuration);
    }

    #endregion

    #region SFX

    /// <summary>
    /// Play a clip on a free pooled source with a fade-in. When every source is busy the one
    /// that started longest ago is cut.
    /// </summary>
    private AudioSource PlaySfx(AudioClip clip) => PlaySfx(clip, sfxFadeInDuration);

    private AudioSource PlaySfx(AudioClip clip, float fadeInDuration)
    {
        if (!_sfxEnabled || clip == null || sfxPool.Count == 0) return null;

        AudioSource chosen = null;
        float oldest = float.MaxValue;
        foreach (var source in sfxPool)
        {
            if (!source.isPlaying) { chosen = source; break; }

            float started = sfxStartTimes.TryGetValue(source, out var t) ? t : 0f;
            if (started < oldest) { oldest = started; chosen = source; }
        }

        sfxStartTimes[chosen] = Time.unscaledTime;
        FadeIn(chosen, clip, SfxTargetVolume, fadeInDuration);
        return chosen;
    }

    // Buttons — every button shares one click.
    internal void PlayButton() => PlaySfx(clipButton);

    internal void PlayGeneralButtonClick() => PlayButton();
    internal void PlayPrimaryActionButton() => PlayButton();
    internal void PlaySpinStart()          => PlayButton();
    internal void PlaySpinStop()           => PlayButton();
    internal void PlayTakeButton()         => PlayButton();
    internal void PlayAutoplayStop()       => PlayButton();
    internal void PlayPopupOpenClose()     => PlayButton();
    internal void PlayPopupOpen()          => PlayButton();
    internal void PlayPopupClose()         => PlayButton();
    internal void PlayAutoplayPanelOpen()  => PlayButton();
    internal void PlayBetPlusMinus()       => PlayButton();
    internal void PlayBetPlus()            => PlayButton();
    internal void PlayBetMinus()           => PlayButton();

    internal void PlayMaxBet()         => PlaySfx(clipMaxBet);
    internal void PlayMaxBetReached()  => PlayMaxBet();

    internal void PlayCongratulations()           => PlaySfx(clipCongratulations);
    internal void PlayFreeSpinRewarded()          => PlaySfx(clipFreeSpinRewarded);
    internal void PlayMeterAdd()                  => PlaySfx(clipMeterAdd);
    internal void PlayJackpotMultiplierIncrease() => PlaySfx(clipJackpotMultiplierIncrease);
    internal void PlayMysteryReveal()             => PlaySfx(clipMysteryReveal);
    internal void PlayReelStop()                  => PlaySfx(clipReelStop, reelStopFadeInDuration);

    /// <summary>NormalWin for the lowest tier, BigWins for every tier above it.</summary>
    internal void PlayWin(bool bigWin)
    {
        if (winSource == null) return;
        if (!_sfxEnabled) return;

        AudioClip clip = bigWin ? clipBigWins : clipNormalWin;
        if (clip == null) return;

        FadeIn(winSource, clip, SfxTargetVolume, sfxFadeInDuration);
    }

    internal void StopWin() => FadeOutAndStop(winSource, sfxFadeOutDuration);

    /// <summary>One shared loop for all reels. A second call while it is already running is a no-op.</summary>
    internal void StartReelSpin()
    {
        if (reelSpinSource == null || clipReelSpinning == null || !_sfxEnabled) return;

        // Still playing but mid fade-out from the last stop: bring it back up instead of restarting.
        if (reelSpinSource.isPlaying && reelSpinSource.clip == clipReelSpinning)
        {
            FadeVolume(reelSpinSource, SfxTargetVolume, sfxFadeInDuration);
            return;
        }

        FadeIn(reelSpinSource, clipReelSpinning, SfxTargetVolume, sfxFadeInDuration);
    }

    internal void StopReelSpin() => FadeOutAndStop(reelSpinSource, reelSpinFadeOutDuration);

    #endregion

    #region Focus

    private bool isForceMuted = false;

    internal void SetMuteAll(bool forceMute)
    {
        if (forceMute == isForceMuted) return;
        isForceMuted = forceMute;

        AudioListener.volume = forceMute ? 0f : 1f;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        SetMuteAll(!hasFocus);
    }

    private void OnApplicationPause(bool isPaused)
    {
        SetMuteAll(isPaused);
    }

    #endregion
}
