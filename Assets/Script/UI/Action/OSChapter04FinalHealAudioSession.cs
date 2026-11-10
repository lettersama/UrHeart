using System;
using UnityEngine;

/// <summary>
/// Four independent voices retain authored tails during scheduled handoffs.
/// Lives under the persistent AudioManager so menu fades survive the OS director.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSChapter04FinalHealAudioSession : MonoBehaviour
{
    public enum MusicPhase { Stopped, Battle, Waiting, Bloom, CalmLoop, MenuFade }
    const float BattleGain = .45f;
    const float LoopGain = .4f;
    const double InitialLead = .1d;
    const double ImmediateLead = .025d;
    const string MasterPitchParameter = "MasterPlaybackPitch";

    sealed class Voice
    {
        public AudioSource Source;
        public double StartDsp = double.NaN;
        public int StartSample;
        public bool Armed, PendingPause, ActivePause;
        public bool Fading, StopAfterFade;
        public double FadeAt, FadeSeconds;
        public float FadeFrom, FadeTo;
    }

    static OSChapter04FinalHealAudioSession current;
    public static bool HasBackgroundAudioOwnership => current != null && !current.disposed
        && current.leaseHeld && !current.IsFadingForMenu;
    readonly Voice[] voices = new Voice[4];
    Voice battle, wait, bloom, loop;
    UnityEngine.Object consumer;
    AudioManager audio;
    AudioLowPassFilter lowPass;
    AudioDistortionFilter distortion;
    double beatSeconds, loopAt, bloomLoopAt, pausedAt, lastBattleTime, menuFadeEnd;
    bool leaseHeld, disposed, battleCollected, calmCollected;

    public AudioSource BattleSource => battle?.Source;
    public AudioSource WaitSource => wait?.Source;
    public AudioSource BloomSource => bloom?.Source;
    public AudioSource LoopSource => loop?.Source;
    public MusicPhase Phase { get; private set; }
    // The chart's zero point differs from the source start for a debug shortcut.
    public double BattleStartDsp => battle != null
        ? battle.StartDsp - battle.StartSample / (double)battle.Source.clip.frequency : double.NaN;
    public double WaitStartDsp => wait != null ? wait.StartDsp : double.NaN;
    public double BloomStartDsp => bloom != null ? bloom.StartDsp : double.NaN;
    public double LoopStartDsp => loop != null ? loop.StartDsp : double.NaN;
    public bool IsPaused { get; private set; }
    public bool IsFadingForMenu { get; private set; }
    public bool IsMenuFadeComplete { get; private set; }
    public float AuthoredSourcePitch { get; private set; } = 1f;

    public static OSChapter04FinalHealAudioSession Create(UnityEngine.Object owner,
        AudioClip battleClip, AudioClip waitClip, AudioClip bloomClip, AudioClip loopClip,
        double beat, double normalLoopTime, double bloomLoopTime)
    {
        if (current != null) current.DisposeNow();
        var manager = AudioManager.Instance;
        var root = new GameObject("FinalHealAudioSession");
        if (manager != null) root.transform.SetParent(manager.transform, false);
        else DontDestroyOnLoad(root);
        var session = root.AddComponent<OSChapter04FinalHealAudioSession>();
        session.consumer = owner;
        session.audio = manager;
        // MasterMix currently speeds the rest of the game to 1.05x.
        // Cancel that inherited pitch on these authored, sample-timed voices
        // without changing the mixer value or any other music/SFX source.
        var mixer = manager?.MusicMixerGroup?.audioMixer;
        if (mixer != null && mixer.GetFloat(MasterPitchParameter, out float inheritedPitch)
            && inheritedPitch > 0f)
            session.AuthoredSourcePitch = 1f / inheritedPitch;
        session.beatSeconds = beat;
        session.loopAt = normalLoopTime;
        session.bloomLoopAt = bloomLoopTime;
        session.battle = session.voices[0] = session.MakeVoice("Battle", battleClip, false, BattleGain);
        session.wait = session.voices[1] = session.MakeVoice("MamaWait", waitClip, true, BattleGain);
        session.bloom = session.voices[2] = session.MakeVoice("Bloom", bloomClip, false, BattleGain);
        session.loop = session.voices[3] = session.MakeVoice("CalmLoop", loopClip, true, LoopGain);
        session.lowPass = session.BattleSource.gameObject.AddComponent<AudioLowPassFilter>();
        session.distortion = session.BattleSource.gameObject.AddComponent<AudioDistortionFilter>();
        session.ClearTreatmentEffect();
        if (manager != null)
        {
            manager.SetBackgroundAudioSuppressed(session, true, 0f);
            session.leaseHeld = true;
        }
        current = session;
        return session;
    }

    Voice MakeVoice(string label, AudioClip clip, bool looping, float gain)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = audio?.MusicMixerGroup;
        source.clip = clip;
        source.loop = looping;
        source.volume = gain;
        source.pitch = AuthoredSourcePitch;
        return new Voice { Source = source };
    }

    public void StartBattle()
    {
        if (disposed) return;
        Schedule(battle, AudioSettings.dspTime + InitialLead, BattleGain);
        lastBattleTime = 0d;
        Phase = MusicPhase.Battle;
    }

    public void StartCalmLoop()
    {
        if (disposed) return;
        ClearTreatmentEffect();
        Schedule(loop, AudioSettings.dspTime + ImmediateLead, LoopGain);
        Phase = MusicPhase.CalmLoop;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool DebugStartBattleAt(double seconds)
    {
        if (disposed || Phase != MusicPhase.Battle || BattleSource?.clip == null) return false;
        bool wasPaused = IsPaused;
        SetPaused(false);
        int sample = Mathf.Clamp((int)(seconds * BattleSource.clip.frequency), 0, BattleSource.clip.samples - 1);
        Schedule(battle, AudioSettings.dspTime + InitialLead, BattleGain, sample);
        lastBattleTime = sample / (double)BattleSource.clip.frequency;
        SetPaused(wasPaused);
        return true;
    }
#endif

    public double ReadBattleTime()
    {
        if (BattleSource == null || BattleSource.clip == null) return lastBattleTime;
        // DSP only arranges source handoffs; decoded samples drive the chart.
        lastBattleTime = Math.Max(lastBattleTime,
            BattleSource.timeSamples / (double)BattleSource.clip.frequency);
        return lastBattleTime;
    }

    public void BeginMotherWait(double actualBattleTime)
    {
        if (disposed || Phase != MusicPhase.Battle) return;
        ClearTreatmentEffect();
        double now = AudioSettings.dspTime;
        double beat = Math.Floor(actualBattleTime / beatSeconds + 1e-8d) + 1d;
        double at = BattleStartDsp + beat * beatSeconds;
        // A stalled frame must not submit a scheduled request in the past.
        while (at < now + ImmediateLead) at += beatSeconds;
        StopVoice(loop);
        Schedule(wait, at, BattleGain);
        Fade(battle, at, .3d, 0f, true);
        Phase = MusicPhase.Waiting;
    }

    public void CompleteMotherMusic(bool wasWaiting)
    {
        if (disposed || Phase == MusicPhase.Bloom || Phase == MusicPhase.CalmLoop) return;
        ClearTreatmentEffect();
        if (!wasWaiting)
        {
            Schedule(loop, BattleStartDsp + loopAt, LoopGain);
            // Battle keeps playing through its authored 115s natural tail.
            return;
        }
        double now = AudioSettings.dspTime;
        if (wait.Armed && now < wait.StartDsp) StopVoice(wait);
        else Fade(wait, now, .2d, 0f, true);
        // The player can finish both waiting presses before the beat handoff.
        Fade(battle, now, .2d, 0f, true);
        Schedule(bloom, now + ImmediateLead, BattleGain);
        Schedule(loop, bloom.StartDsp + bloomLoopAt, LoopGain);
        Phase = MusicPhase.Bloom;
    }

    public void SetPaused(bool value)
    {
        if (disposed || IsFadingForMenu || value == IsPaused) return;
        double now = AudioSettings.dspTime;
        IsPaused = value;
        if (value)
        {
            pausedAt = now;
            foreach (var voice in voices)
            {
                if (voice == null || !voice.Armed) continue;
                if (now < voice.StartDsp)
                {
                    // Cancel future starts and rebuild them after the pause.
                    voice.Source.Stop();
                    voice.PendingPause = true;
                }
                else if (voice.Source.isPlaying)
                {
                    voice.Source.Pause();
                    voice.ActivePause = true;
                }
            }
        }
        else
        {
            double shift = now - pausedAt;
            foreach (var voice in voices)
            {
                if (voice == null) continue;
                if (voice.Armed) voice.StartDsp += shift;
                if (voice.Fading) voice.FadeAt += shift;
                if (voice.PendingPause)
                {
                    voice.Source.timeSamples = voice.StartSample;
                    voice.Source.PlayScheduled(voice.StartDsp);
                }
                else if (voice.ActivePause) voice.Source.UnPause();
                voice.PendingPause = voice.ActivePause = false;
            }
        }
    }

    public void SetTreatmentEffect(float cutoff, float amount)
    {
        if (disposed) return;
        lowPass.enabled = true;
        lowPass.cutoffFrequency = Mathf.Clamp(cutoff, 10f, 22000f);
        distortion.enabled = true;
        distortion.distortionLevel = Mathf.Clamp01(amount);
    }

    public void ClearTreatmentEffect()
    {
        if (lowPass != null) lowPass.enabled = false;
        if (distortion != null) distortion.enabled = false;
    }

    public void BeginMenuFade()
    {
        if (disposed || IsFadingForMenu) return;
        SetPaused(false);
        ClearTreatmentEffect();
        IsFadingForMenu = true;
        consumer = null;
        Phase = MusicPhase.MenuFade;
        double now = AudioSettings.dspTime;
        menuFadeEnd = now + 2d;
        foreach (var voice in voices)
        {
            if (voice == null || !voice.Armed) continue;
            if (voice.StartDsp > now) StopVoice(voice);
            else Fade(voice, now, 2d, 0f, true);
        }
        // New-game music must not be blocked by a detached, fading old tail.
        ReleaseLease();
    }

    void Update()
    {
        if (disposed) return;
        if (consumer == null && !IsFadingForMenu) { DisposeNow(); return; }
        if (IsPaused) return;
        double now = AudioSettings.dspTime;
        // Scheduling alone is not a visit. Ending-only restores collect just
        // the calm record once its source is actually running after the pause.
        // Detached menu tails cannot add progress after the return-menu save.
        if (!IsFadingForMenu && !OSSessionPersistence.IsRestoring && GameDataManager.Instance != null
            && MrTGame.Core.GameManager.Instance?.Phase != MrTGame.Core.GamePhase.Boot)
        {
            if (!battleCollected && HasStarted(battle, now))
            {
                OSGramophonePlayer.NotifyStoryTrackHeard(OSGramophonePlayer.FinalHealBattleTrack);
                battleCollected = true;
            }
            if (!calmCollected && HasStarted(loop, now))
            {
                OSGramophonePlayer.NotifyStoryTrackHeard(OSGramophonePlayer.FinalHealCalmTrack);
                calmCollected = true;
            }
        }
        foreach (var voice in voices)
        {
            if (voice == null || !voice.Fading || now < voice.FadeAt) continue;
            float t = Mathf.Clamp01((float)((now - voice.FadeAt) / voice.FadeSeconds));
            voice.Source.volume = Mathf.Lerp(voice.FadeFrom, voice.FadeTo, t);
            if (t < 1f) continue;
            voice.Fading = false;
            if (voice.StopAfterFade) StopVoice(voice);
        }
        if (!IsFadingForMenu && loop != null && loop.Armed && now >= loop.StartDsp)
            Phase = MusicPhase.CalmLoop;
        if (IsFadingForMenu && now >= menuFadeEnd)
        {
            IsMenuFadeComplete = true;
            DisposeNow();
        }
    }

    static void Schedule(Voice voice, double at, float gain, int startSample = 0)
    {
        if (voice?.Source == null || voice.Source.clip == null) return;
        StopVoice(voice);
        voice.StartSample = startSample;
        voice.Source.timeSamples = startSample;
        voice.Source.volume = gain;
        voice.StartDsp = at;
        voice.Armed = true;
        voice.Source.PlayScheduled(at);
    }

    static bool HasStarted(Voice voice, double now) => voice?.Source != null && voice.Source.clip != null
        && voice.Armed && !voice.PendingPause && !voice.ActivePause
        && now >= voice.StartDsp && voice.Source.isPlaying && voice.Source.volume > 0f;

    static void Fade(Voice voice, double at, double seconds, float target, bool stop)
    {
        if (voice == null || !voice.Armed) return;
        voice.FadeAt = at;
        voice.FadeSeconds = seconds;
        voice.FadeFrom = voice.Source.volume;
        voice.FadeTo = target;
        voice.StopAfterFade = stop;
        voice.Fading = true;
    }

    static void StopVoice(Voice voice)
    {
        if (voice == null || voice.Source == null) return;
        voice.Source.Stop();
        voice.Armed = voice.Fading = voice.PendingPause = voice.ActivePause = false;
    }

    void ReleaseLease()
    {
        if (leaseHeld && audio != null) audio.SetBackgroundAudioSuppressed(this, false);
        leaseHeld = false;
    }

    public void DisposeNow()
    {
        if (disposed) return;
        disposed = true;
        foreach (var voice in voices) StopVoice(voice);
        ReleaseLease();
        if (current == this) current = null;
        Phase = MusicPhase.Stopped;
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        ReleaseLease();
        if (current == this) current = null;
    }
}
