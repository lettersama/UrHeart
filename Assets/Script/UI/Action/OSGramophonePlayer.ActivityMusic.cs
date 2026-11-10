using UnityEngine;

// A temporary story lease is deliberately separate from the saved selection.
// Only this owner can release its voice; a later unrelated music owner wins.
public sealed partial class OSGramophonePlayer
{
    Object activityOwner;
    string activityTrackId;
    AudioClip activityClip;
    AudioSource activitySource;
    bool activityManualOverride, activitySilence, activitySuspended, activityManualSuspended, activityPaused;
    bool activityAllowsTemporary;
    float activityGain = 1f, activityLowPass = 22000f, activityDistortion;
    bool suppressAutomaticBackground;
    float activityFade = .35f;

    bool HasActivitySelection => activityOwner != null && !activityManualOverride && !temporaryMusic;
    bool ActivityHoldsSilence => activitySilence && (!activityManualOverride || !activityAllowsTemporary);
    bool ActivityPlaybackSuspended => activityManualOverride ? activityManualSuspended : activitySuspended;
    AudioSource ActivitySource => activitySource != null && activitySource.clip == activityClip
        && AudioManager.Instance?.CurrentMusicSource == activitySource ? activitySource : null;

    internal void SetActivityMusic(Object owner, string trackId, AudioClip clip,
        bool availableNow, bool silence, float fadeDuration, bool allowTemporaryMusic = false,
        float gain = 1f, float lowPass = 22000f, float distortion = 0f, bool? manualAvailableNow = null)
    {
        if (owner == null || GameDataManager.Instance == null) return;
        BindSave(GameDataManager.Instance);
        bool acquiring = activityOwner != owner;
        bool changing = acquiring || activityTrackId != trackId;
        if (!allowTemporaryMusic && temporaryMusic)
        {
            ReleaseTemporary(false);
            paperWasVisible = false;
        }
        if (acquiring)
        {
            CapturePosition();
            ResetActivityMusic();
            if (!allowTemporaryMusic) ReleaseTemporary(false);
            activityOwner = owner;
            if (!allowTemporaryMusic) paperWasVisible = false;
        }
        if (changing)
        {
            // Capture changes use zero fade before their explicit silent gap.
            // A song replacing a song crossfades: the old voice keeps playing
            // until the new track's PlayMusic takes it over.
            if (silence) { StopBorrowedMusic(fadeDuration); ClearActivityCrossfade(true); }
            else BeginActivityCrossfade();
            activityTrackId = trackId;
            activityClip = clip;
            activitySource = null;
            activityPaused = false;
            activityManualOverride = false;
        }
        else if (activityClip == null && clip != null) activityClip = clip;

        bool enteringSilence = silence && (!activitySilence || changing
            || (fadeDuration <= 0f && activityFade > 0f));
        activitySilence = silence;
        activitySuspended = !availableNow;
        // A hidden memory pauses its authored soundtrack, while a manually
        // selected record only follows the shared focus/settings/story gates.
        activityManualSuspended = !(manualAvailableNow ?? availableNow);
        activityAllowsTemporary = allowTemporaryMusic;
        activityGain = Mathf.Clamp01(gain);
        activityLowPass = Mathf.Clamp(lowPass, 10f, 22000f);
        activityDistortion = Mathf.Clamp01(distortion);
        activityFade = Mathf.Max(0f, fadeDuration);
        if (enteringSilence)
        {
            CapturePosition();
            StopBorrowedMusic(activityFade);
            ClearActivityCrossfade(true);
            // A new authored silent beat still cuts off a previously selected
            // record; only a subsequent explicit player action may resume it.
            activityManualOverride = false;
        }
        if (!ActivityHoldsSilence) RefreshActivityPlayback();
        RefreshActivityEffects();
        if (changing || enteringSilence) Changed?.Invoke();
    }

    internal void ReleaseActivityMusic(Object owner, bool resumeBackground, bool keepBackgroundSilent)
    {
        if (owner == null || activityOwner != owner) return;
        CapturePosition();
        StopBorrowedMusic(0f);
        ResetActivityMusic();
        suppressAutomaticBackground |= keepBackgroundSilent;
        if (suppressAutomaticBackground) restored = true;
        if (resumeBackground && !suppressAutomaticBackground && state?.wantsPlayback == true
            && (AudioManager.Instance?.CurrentMusicSource == null || OwnedSource != null))
            RestoreBackground();
        Changed?.Invoke();
    }

    public void SuppressAutomaticBackgroundMusic(float fadeDuration = .35f)
    {
        // Loading a Done save has no live lease to release. Preserve the user's
        // selection while preventing a one-frame restoration of the old song.
        if (GameDataManager.Instance != null) BindSave(GameDataManager.Instance);
        CapturePosition();
        ReleaseTemporary(false);
        if (OwnedSource != null) AudioManager.Instance?.StopMusic(Mathf.Max(0f, fadeDuration));
        suppressAutomaticBackground = true;
        restored = true;
    }

    internal void PauseActivityMusic()
    {
        if (activityOwner == null) return;
        activitySuspended = activityManualSuspended = true;
        RefreshActivityPlayback();
        RefreshActivityEffects();
    }

    void RefreshActivityPlayback()
    {
        if (activityOwner == null || ActivityHoldsSilence || state == null) return;
        var audio = AudioManager.Instance;
        if (audio == null) return;
        var source = activityManualOverride ? OwnedSource : ActivitySource;
        if (ActivityPlaybackSuspended || !state.wantsPlayback || temporaryMusic)
        {
            // A track change that has not started yet still owns the old voice.
            if (source == null && !activityManualOverride) source = ActivityCrossfadeSource;
            if (source != null && source.isPlaying)
            {
                if (activityManualOverride) { CapturePosition(); backgroundPaused = true; }
                PauseBorrowedVoice(audio);
                activityPaused = true;
            }
            return;
        }
        if (activityManualOverride)
        {
            if (audio.CurrentMusicSource == null || OwnedSource != null) RestoreBackground();
            return;
        }
        if (activityClip == null) return;
        if (source != null)
        {
            audio.SetMusicPlaybackGain(Volume * activityGain);
            if (activityPaused)
            {
                audio.ResumeMusicPlayback(BorrowedResumeFade);
                activityPaused = false;
            }
            return;
        }
        // Do not overwrite another story's shared-channel takeover.
        if (audio.CurrentMusicSource != null && OwnedSource == null && ActivityCrossfadeSource == null) return;
        audio.PlayMusic(activityClip, activityFade, true, Volume * activityGain);
        activitySource = audio.CurrentMusicSource;
        ApplyActivityResumePosition(activitySource);
        ClearActivityCrossfade(false);
        activityPaused = false;
    }

    void YieldActivityToManualSelection()
    {
        if (activityOwner == null) return;
        StopActivityVoice(0f);
        activityManualOverride = true;
        activityPaused = false;
    }

    void StopActivityVoice(float fade)
    {
        if (ActivitySource != null || ActivityCrossfadeSource != null) AudioManager.Instance?.StopMusic(fade);
        // Retain the reference during a fade, so another owner's voice is never
        // mistaken for ours and repeated refreshes do not restart the fade.
        activityPaused = false;
    }

    void StopBorrowedMusic(float fade)
    {
        if (ActivitySource != null || OwnedSource != null || ActivityCrossfadeSource != null)
            AudioManager.Instance?.StopMusic(fade);
        activityPaused = false;
    }

    void ResetActivityMusic()
    {
        RestoreActivityEffects();
        StopActivityVoice(0f);
        ClearActivityCrossfade(true);
        activityOwner = null;
        activityTrackId = null;
        activityClip = null;
        activitySource = null;
        activityManualOverride = activitySilence = activitySuspended = activityManualSuspended = activityPaused = false;
        activityAllowsTemporary = false;
        activityGain = 1f;
        activityLowPass = 22000f;
        activityDistortion = 0f;
    }
}
