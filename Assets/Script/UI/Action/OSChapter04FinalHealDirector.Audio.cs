using System;
using MrTGame.Core;
using UnityEngine;

public sealed partial class OSChapter04FinalHealDirector
{
    // The chart source, also retained for the existing backend QA inspection.
    AudioSource musicSource;
    OSChapter04FinalHealAudioSession audioSession;
    AudioClip slashClip;
    float feedbackAt = -10f;
    bool feedbackHit;

#if UNITY_EDITOR
    public OSChapter04FinalHealAudioSession DebugAudioSession => audioSession;
#endif

    bool EnsureAudioSession()
    {
        if (audioSession != null) return true;
        AudioClip Load(string path) => string.IsNullOrEmpty(path) ? null : Resources.Load<AudioClip>(path);
        var battle = Load(Content.MusicResource);
        var wait = Load(Content.MamaWaitResource);
        var bloom = Load(Content.BloomMusicResource);
        var loop = Load(Content.CalmLoopResource);
        slashClip = Load(Content.SlashResource);
        if (battle == null || wait == null || bloom == null || loop == null || slashClip == null)
        {
            Debug.LogError("[FinalHeal] One or more authored treatment audio resources are missing.", this);
            return false;
        }
        audioSession = OSChapter04FinalHealAudioSession.Create(this, battle, wait, bloom, loop,
            60d / Content.Bpm, Content.LoopStartTime, Content.BloomLoopStartTime);
        musicSource = audioSession.BattleSource;
        feedbackAt = -10f;
        return true;
    }

    void StartTreatmentMusic()
    {
        if (!EnsureAudioSession()) return;
        audioSession.StartBattle();
        audioSession.SetTreatmentEffect(1600f, .3f);
        RefreshMusicPause();
    }

    void StartEndingMusic()
    {
        // A live cure already scheduled its authored tail and loop. Only an
        // ending-only save restoration starts directly in the calm loop.
        if (audioSession != null) return;
        if (!EnsureAudioSession()) return;
        audioSession.StartCalmLoop();
        RefreshMusicPause();
    }

    double ReadMusicTime()
    {
#if UNITY_EDITOR
        if (debugClock) return musicTime;
#endif
        if (IsWaitingForMother || audioSession == null) return musicTime;
        return Math.Max(musicTime, audioSession.ReadBattleTime());
    }

    void BeginMotherWait(double actualBattleTime) => audioSession?.BeginMotherWait(actualBattleTime);
    void CompleteMotherMusic(bool wasWaiting) => audioSession?.CompleteMotherMusic(wasWaiting);

    void PlaySlash()
    {
        if (slashClip != null) AudioManager.Instance?.PlaySfx(slashClip, 1f, audioSession?.AuthoredSourcePitch ?? 1f);
    }

    // The mother section keeps the ordinary lane/heartbeat mix muted. Its
    // first tap gets just the small hit cue; the last tap owns the musical bloom.
    void PlayMotherTapHit() => AudioManager.Instance?.PlaySfx("BeatHit", 1f, 1f);

    void RefreshMusicPause()
    {
        bool shouldPause = paused;
#if UNITY_EDITOR
        shouldPause |= debugClock;
#endif
        audioSession?.SetPaused(shouldPause);
    }

    void BeginMenuMusicFade() => audioSession?.BeginMenuFade();

    void StopOwnedMusic()
    {
        if (audioSession != null)
        {
            // Boot may be committed synchronously inside ReturnToMainMenu,
            // before its caller gets the opportunity to request the fade.
            if (audioSession.IsFadingForMenu || GameManager.Instance?.Phase == GamePhase.Boot)
                audioSession.BeginMenuFade();
            else
                audioSession.DisposeNow();
        }
        audioSession = null;
        musicSource = null;
        slashClip = null;
    }

    void PulseMusicEffect(bool hit) { feedbackHit = hit; feedbackAt = activeSeconds; }

    void UpdateMusicEffect()
    {
        if (audioSession == null) return;
        if (released || ending || musicTime >= Content.ReleaseTime)
        {
            audioSession.ClearTreatmentEffect();
            return;
        }
        float health = Mathf.Clamp01(heart / 100f);
        var baseline = new Vector2(Mathf.Lerp(1600f, 7200f, health), Mathf.Lerp(.3f, .05f, health));
        if (frozen && !released) baseline = new Vector2(350f, .08f);
        float since = activeSeconds - feedbackAt;
        float release = feedbackHit ? .3f : .45f;
        var peak = feedbackHit ? new Vector2(12000f, 0f) : new Vector2(850f, Mathf.Clamp01(baseline.y + .22f));
        float weight = since < .06f ? Mathf.Clamp01(since / .06f) : 1f - Mathf.Clamp01((since - .06f) / release);
        var value = frozen && !released ? baseline : Vector2.Lerp(baseline, peak, weight);
        audioSession.SetTreatmentEffect(value.x, value.y);
    }
}
