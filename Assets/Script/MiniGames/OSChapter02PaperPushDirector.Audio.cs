using UnityEngine;

public sealed partial class OSChapter02PaperPushDirector
{
    // Live travels for 1.1 seconds, then the pointer settles beside the player.
    // Let the whole sound bed recede through that movement into the conversation.
    public const float HeartTalkAudioFadeSeconds = 1.8f;
    OSEnergyLoopSaveData heartTalkAudioState;
    AudioManager heartTalkAudioManager;
    AudioSource heartTalkWindowMusic;
    AudioClip heartTalkWindowClip;
    float heartTalkWindowGain, heartTalkFadeStarted, heartTalkFadeDuration;
    bool heartTalkAudioApplied;

    public bool IsHeartTalkAudioTransition => isActiveAndEnabled
        && GameDataManager.Instance?.CurrentChapterIndex == 2
        && (Step == RematchStep.Wrap || Step == RematchStep.HeartTalkHold || IsConcessionAudioReserved);

    // Gramophone.Update runs before this director. Reserve the bed as soon as
    // the last line is read, including a player closing the match in its 3s hold.
    bool IsConcessionAudioReserved => Step == RematchStep.Match2 && State != null
        && State.chapter02PaperPushTiltWin != 0
        && (State.chapter02PaperPushCueHistory.Contains(SecondMatchResultCue)
            || OwnMessageDisplayed && messageId == EntryPrefix + SecondMatchResultCue && messageTyped);

    void BeginHeartTalkAudio()
    {
        if (!IsHeartTalkAudioTransition || State == null) return;
        State.chapter02HeartTalkAudioStarted = true;
        ApplyHeartTalkAudio(HeartTalkAudioFadeSeconds);
    }

    // The gramophone calls this before automatic restoration as well. Silence
    // survives hidden windows and language changes; a loaded talk starts quiet.
    public void RefreshHeartTalkAudio()
    {
        if (!ReferenceEquals(heartTalkAudioState, State))
        {
            ReleaseHeartTalkAudio();
            heartTalkAudioState = State;
        }
        if (!IsHeartTalkAudioTransition || State == null)
        {
            ReleaseHeartTalkAudio();
            return;
        }
        if (Step == RematchStep.HeartTalkHold) State.chapter02HeartTalkAudioStarted = true;
        if (!State.chapter02HeartTalkAudioStarted) return;
        if (!heartTalkAudioApplied || heartTalkAudioManager != AudioManager.Instance)
            ApplyHeartTalkAudio(0f);
        TickHeartTalkWindowMusic();
    }

    void ApplyHeartTalkAudio(float fade)
    {
        if (heartTalkAudioApplied && heartTalkAudioManager == AudioManager.Instance) return;
        if (AudioManager.Instance == null) return;
        heartTalkAudioState = State;
        heartTalkAudioManager = AudioManager.Instance;
        heartTalkAudioManager.SetBackgroundAudioSuppressed(this, true, fade);
        // Paper-push plays through a dedicated source outside AudioManager's
        // dual music channel, so capture and fade that voice separately.
        heartTalkWindowMusic = GetComponent<OSPaperPushMusic>()?.Source;
        heartTalkWindowClip = heartTalkWindowMusic != null ? heartTalkWindowMusic.clip : null;
        heartTalkWindowGain = heartTalkWindowMusic != null ? heartTalkWindowMusic.volume : 0f;
        heartTalkFadeStarted = Time.unscaledTime;
        heartTalkFadeDuration = Mathf.Max(0f, fade);
        heartTalkAudioApplied = true;
        TickHeartTalkWindowMusic();
    }

    void TickHeartTalkWindowMusic()
    {
        if (!heartTalkAudioApplied || heartTalkWindowMusic == null
            || heartTalkWindowMusic.clip != heartTalkWindowClip) return;
        float progress = heartTalkFadeDuration <= 0f ? 1f
            : Mathf.Clamp01((Time.unscaledTime - heartTalkFadeStarted) / heartTalkFadeDuration);
        heartTalkWindowMusic.volume = Mathf.Lerp(heartTalkWindowGain, 0f, progress);
        if (progress >= 1f) heartTalkWindowMusic.Stop();
    }

    void ReleaseHeartTalkAudio()
    {
        if (heartTalkAudioManager != null)
            heartTalkAudioManager.SetBackgroundAudioSuppressed(this, false);
        if (heartTalkAudioApplied && heartTalkWindowMusic != null
            && heartTalkWindowMusic.clip == heartTalkWindowClip)
        {
            heartTalkWindowMusic.Stop();
            heartTalkWindowMusic.volume = heartTalkWindowGain;
        }
        heartTalkAudioManager = null;
        heartTalkWindowMusic = null;
        heartTalkWindowClip = null;
        heartTalkAudioApplied = false;
    }
}
