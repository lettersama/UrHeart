using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>Chapter-four TV music. Consumes the story contract; never advances it.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(720)]
public sealed class OSChapter04Music : MonoBehaviour
{
    public enum Ambience { None, CityLab, Wasteland, LabNight, Rain }
    // The TV recollection belongs to the user's Ambient setting, including its
    // musical layers. Keep their authored balance under one environmental trim.
    // Matches the gramophone's existing quiet records at its default 0.7 gain
    // (about -33 LUFS before mixer faders); the yard rain is not a reference.
    public const float EnvironmentGain = .27f;
    const string Folder = "Chapter04/Audio/";
    const double StartLead = .075d;
    const float ThemeGain = .5f * EnvironmentGain, AmbientGain = .35f * EnvironmentGain,
        DroneGain = .25f * EnvironmentGain;
    const float AmbientFade = 1.75f;

    sealed class Voice
    {
        public AudioSource Source;
        public bool Armed, PendingPause, ActivePause, Fading, StopAfterFade;
        public double StartDsp;
        public int StartSample;
        public float FadeAt, FadeSeconds, FadeFrom, FadeTo;
    }

    OSChapter04StoryDirector story;
    OSChapter04PresentationBridge bridge;
    OSChapter04StoryState observed;
    AudioManager audio;
    Voice melody, accompaniment, drone, heartbeat;
    readonly Voice[] ambience = new Voice[2];
    Voice[] voices;
    AudioClip cityClip, wastelandClip, nightClip, rainClip;
    AudioClip[] heartbeatClips;
    AudioLowPassFilter accompanimentLowPass;
    int requestRevision = -1, activeAmbient = -1, tier = -1;
    float clock, warpAt, rainGain = -1f;
    double pausedAt;
    bool leaseHeld, heartbeatLeaseHeld, warning, heartbeatCounted, restoredWarp;
    bool themeCollected;

    public bool IsRunning { get; private set; }
    // Readable before this component's late Update. A started TV story keeps
    // its authored silence through warning/treatment handoffs and paused views.
    public bool OwnsGramophoneAudio
    {
        get
        {
            var currentStory = story != null ? story : GetComponent<OSChapter04StoryDirector>();
            return isActiveAndEnabled
                && MrTGame.Core.GameManager.Instance?.Phase != MrTGame.Core.GamePhase.Boot
                && currentStory?.IsActive == true && currentStory.State?.started == true;
        }
    }
    public bool IsPaused { get; private set; }
    public bool IsWarningSilent => warning && MusicVoicesSilent();
    public int GriefTier => Math.Max(0, tier);
    public string TheatreState { get; private set; }
    public Ambience CurrentAmbience { get; private set; }
    public AudioSource MelodySource => melody?.Source;
    public AudioSource AccompanimentSource => accompaniment?.Source;
    public AudioSource DroneSource => drone?.Source;
    public AudioSource WarningHeartbeatSource => heartbeat?.Source;
    public AudioSource AmbienceSource => activeAmbient < 0 ? null : ambience[activeAmbient]?.Source;
    public AudioSource FadingAmbienceSource => activeAmbient < 0 ? null : ambience[1 - activeAmbient]?.Source;
    public AudioLowPassFilter AccompanimentLowPass => accompanimentLowPass;
    public double ThemeStartDsp { get; private set; } = double.NaN;
    public int ThemeStarts { get; private set; }
    public int AmbienceStarts { get; private set; }
    public int WarningHeartbeatPlays { get; private set; }
    public double WarningStartedDsp { get; private set; } = double.NaN;
    public double WarningSilencedDsp { get; private set; } = double.NaN;
    public float AuthoredSourcePitch { get; private set; } = 1f;
    public float MusicClock => clock;
    public float GriefWarpSeconds => tier >= 2 ? Mathf.Max(0f, clock - warpAt) : 0f;

    void Update()
    {
        Resolve();
        // Returning to the menu keeps the OS scene alive until Continue loads
        // the next session. Its paused story still reports IsActive, but must
        // no longer own the shared background channels or heartbeat gate.
        if (MrTGame.Core.GameManager.Instance?.Phase == MrTGame.Core.GamePhase.Boot)
        { StopPlayback(); return; }
        if (!OwnsGramophoneAudio)
        { StopPlayback(); return; }
        // Keep the silent lease if the treatment is still waiting to construct
        // its own audio session. A saved Pending/Ending state has the same gap.
        if (InFinalTreatment(story.FinalTreatmentStage))
        {
            StopPlayback(!OSChapter04FinalHealAudioSession.HasBackgroundAudioOwnership);
            return;
        }
        if (bridge?.IsBound != true) { StopPlayback(); return; }
        if (OSSessionPersistence.IsRestoring) { SetPaused(true); return; }

        var request = bridge.CurrentRequest;
        bool restore = request.Revision != requestRevision && request.IsRestore;
        if (!ReferenceEquals(observed, story.State) || restore)
        { StopPlayback(); observed = story.State; }
        requestRevision = request.Revision;
        TheatreState = bridge.CurrentState;
        if (!IsRunning) StartPlayback(restore);
        SetPaused(bridge.IsPaused);
        if (!IsPaused) clock += Time.unscaledDeltaTime;

        if (story.IsFailureWarningPending)
        {
            if (!warning) BeginWarning(restore);
        }
        else if (!warning)
        {
            SetTier(story.State.griefTier, false);
            SetAmbience(ResolveAmbience(TheatreState), false);
        }
        if (IsPaused) return;
        ApplyWarp();
        foreach (var voice in voices) TickFade(voice);
        if (!themeCollected && !warning && !OSSessionPersistence.IsRestoring
            && HasStarted(melody) && HasStarted(accompaniment))
        {
            OSGramophonePlayer.NotifyStoryTrackHeard(OSGramophonePlayer.Chapter04ThemeTrack);
            themeCollected = true;
        }
        if (warning && heartbeat.Armed && !heartbeatCounted && AudioSettings.dspTime >= heartbeat.StartDsp)
        { heartbeatCounted = true; WarningHeartbeatPlays++; }
    }

    void Resolve()
    {
        if (story == null) story = GetComponent<OSChapter04StoryDirector>();
        var next = story?.Presentation;
        if (next == bridge) return;
        Unbind(); StopPlayback(); bridge = next;
        if (bridge == null) return;
        bridge.PresentationCleared += OnPresentationCleared;
        bridge.PauseChanged += SetPaused;
        requestRevision = -1;
    }

    void Unbind()
    {
        if (bridge != null)
        {
            bridge.PresentationCleared -= OnPresentationCleared;
            bridge.PauseChanged -= SetPaused;
        }
        bridge = null;
    }

    void OnPresentationCleared() { StopPlayback(); observed = null; requestRevision = -1; }
    void OnDisable() { StopPlayback(); Unbind(); }
    void OnDestroy()
    {
        StopPlayback(); Unbind();
        if (voices != null)
            foreach (var voice in voices)
                if (voice?.Source != null) Destroy(voice.Source.gameObject);
    }

    public static Ambience ResolveAmbience(string state)
    {
        switch (state)
        {
            case OSChapter04PresentationBridge.CityWalk:
            case OSChapter04PresentationBridge.CityArrive:
            case OSChapter04PresentationBridge.DoorLocked:
            case OSChapter04PresentationBridge.FoldToLab:
            case OSChapter04PresentationBridge.AtDoor:
            case OSChapter04PresentationBridge.EnterDoor:
            case OSChapter04PresentationBridge.LabRummage:
            case OSChapter04PresentationBridge.LabTidy:
            case OSChapter04PresentationBridge.MedicineReady: return Ambience.CityLab;
            case OSChapter04PresentationBridge.WastelandWalk:
            case OSChapter04PresentationBridge.SettlementWait:
            case OSChapter04PresentationBridge.TentCare:
            case OSChapter04PresentationBridge.NorthSettlement:
            case OSChapter04PresentationBridge.GirlHome:
            case OSChapter04PresentationBridge.FlowerWalk: return Ambience.Wasteland;
            case OSChapter04PresentationBridge.LabNight: return Ambience.LabNight;
            case OSChapter04PresentationBridge.FlowerWalkRain:
            case OSChapter04PresentationBridge.Grave:
            case OSChapter04PresentationBridge.GravePlaceFlowers:
            case OSChapter04PresentationBridge.GraveBreakdown: return Ambience.Rain;
            default: return Ambience.None;
        }
    }

    static bool InFinalTreatment(OSChapter04FinalTreatmentStage stage)
        => stage == OSChapter04FinalTreatmentStage.Pending || stage == OSChapter04FinalTreatmentStage.Opened
            || stage == OSChapter04FinalTreatmentStage.FinalHeal || stage == OSChapter04FinalTreatmentStage.Ending
            || stage == OSChapter04FinalTreatmentStage.Completed;

    void EnsureSources()
    {
        if (voices != null) return;
        audio = AudioManager.Instance;
        var mixer = audio?.AmbientMixerGroup?.audioMixer;
        if (mixer != null && mixer.GetFloat("MasterPlaybackPitch", out float inheritedPitch) && inheritedPitch > 0f)
            AuthoredSourcePitch = 1f / inheritedPitch;
        melody = MakeVoice("ThemeMelody", Load("Ch04_Theme_Melody"), audio?.AmbientMixerGroup, true);
        accompaniment = MakeVoice("ThemeAccompaniment", Load("Ch04_Theme_Accomp"), audio?.AmbientMixerGroup, true);
        drone = MakeVoice("GriefLowDrone", Load("Ch04_Grief_LowDrone"), audio?.AmbientMixerGroup, true);
        accompanimentLowPass = accompaniment.Source.gameObject.AddComponent<AudioLowPassFilter>();
        accompanimentLowPass.enabled = false;
        cityClip = Load("Ch04_Amb_CityLab"); wastelandClip = Load("Ch04_Amb_Wasteland");
        nightClip = Load("Ch04_Amb_LabNight"); rainClip = Resources.Load<AudioClip>("Chapter03Story/Audio/RainHome");
        for (int i = 0; i < ambience.Length; i++)
        {
            ambience[i] = MakeVoice("Ambience" + i, null, audio?.AmbientMixerGroup, true);
            ambience[i].Source.gameObject.AddComponent<AudioHighPassFilter>().cutoffFrequency = 300f;
            ambience[i].Source.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 4000f;
        }
        heartbeatClips = new AudioClip[6];
        var sounds = GameDataManager.Instance?.AudioData?.SFX;
        for (int i = 0; i < heartbeatClips.Length; i++)
        {
            string key = "HeartBeat" + i.ToString("00");
            heartbeatClips[i] = sounds?.Find(item => item != null && item.keyid == key)?.audioclip;
        }
        heartbeat = MakeVoice("WarningHeartbeat", heartbeatClips[0], audio?.SFXMixerGroup, false);
        voices = new[] { melody, accompaniment, drone, ambience[0], ambience[1], heartbeat };
    }

    AudioClip Load(string name)
    {
        var clip = Resources.Load<AudioClip>(Folder + name);
        if (clip == null) Debug.LogWarning("[Chapter04 Music] Missing clip: " + Folder + name, this);
        else clip.LoadAudioData();
        return clip;
    }

    Voice MakeVoice(string name, AudioClip clip, AudioMixerGroup group, bool loop)
    {
        var child = new GameObject("Chapter04Music_" + name);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false; source.spatialBlend = 0f; source.loop = loop;
        source.clip = clip; source.volume = 0f; source.pitch = AuthoredSourcePitch;
        source.outputAudioMixerGroup = group;
        return new Voice { Source = source };
    }

    void StartPlayback(bool restoring)
    {
        EnsureSources(); IsRunning = true; IsPaused = false; clock = 0f; tier = -1;
        themeCollected = false;
        warning = heartbeatCounted = false; activeAmbient = -1; rainGain = -1f;
        WarningStartedDsp = WarningSilencedDsp = double.NaN;
        if (audio != null) { audio.SetBackgroundAudioSuppressed(this, true, .15f); leaseHeld = true; }
        // Restored warning screens stay silent; they never replay their entrance beat.
        if (story.IsFailureWarningPending) { BeginWarning(true); return; }
        int startingTier = Mathf.Clamp(story.State.griefTier, 0, 3);
        if (startingTier < 3)
        {
            ThemeStartDsp = AudioSettings.dspTime + StartLead;
            if (startingTier == 0) Schedule(melody, ThemeStartDsp, 0f);
            Schedule(accompaniment, ThemeStartDsp, 0f);
            ThemeStarts++;
        }
        SetTier(startingTier, restoring);
        SetAmbience(ResolveAmbience(TheatreState), restoring);
    }

    void SetTier(int value, bool instant)
    {
        value = Mathf.Clamp(value, 0, 3);
        if (value == tier) return;
        int previous = tier; tier = value;
        if (value < 2) { restoredWarp = false; warpAt = clock; }
        else if (previous < 2) { warpAt = clock; restoredWarp = instant; }
        Fade(melody, value == 0 ? ThemeGain : 0f, instant ? 0f : 2f, value >= 1);
        if (value < 3)
        {
            if (previous < 0) Fade(accompaniment, ThemeGain, instant ? 0f : 2f, false);
            if (drone.Armed) Fade(drone, 0f, instant ? 0f : 3f, true);
        }
        else
        {
            Fade(accompaniment, 0f, instant ? 0f : 3f, true);
            Schedule(drone, AudioSettings.dspTime + StartLead, instant ? DroneGain : 0f);
            Fade(drone, DroneGain, instant ? 0f : 3f, false);
        }
        ApplyWarp();
    }

    void ApplyWarp()
    {
        if (accompanimentLowPass == null) return;
        accompanimentLowPass.enabled = tier >= 2 && !warning;
        if (tier < 2) { accompaniment.Source.pitch = AuthoredSourcePitch; return; }
        float t = GriefWarpSeconds, r = restoredWarp ? 1f : Mathf.Clamp01(t / 4f);
        float pitch = 1f - .006f * r - .008f * r * Mathf.Sin(2f * Mathf.PI * .4f * t)
            - .003f * r * Mathf.Sin(2f * Mathf.PI * 1.7f * t);
        accompaniment.Source.pitch = AuthoredSourcePitch * pitch;
        accompanimentLowPass.cutoffFrequency = 18000f * Mathf.Pow(1500f / 18000f, r);
    }

    float RainGain() => EnvironmentGain * (TheatreState == OSChapter04PresentationBridge.FlowerWalkRain ? .08f
        : TheatreState == OSChapter04PresentationBridge.Grave ? .12f
        : TheatreState == OSChapter04PresentationBridge.GravePlaceFlowers ? .18f : .24f);

    void SetAmbience(Ambience next, bool instant)
    {
        float gain = next == Ambience.Rain ? RainGain() : AmbientGain;
        if (next == CurrentAmbience && activeAmbient >= 0)
        {
            if (next == Ambience.Rain && !Mathf.Approximately(gain, rainGain))
            { rainGain = gain; Fade(ambience[activeAmbient], gain, instant ? 0f : AmbientFade, false); }
            return;
        }
        if (activeAmbient >= 0) Fade(ambience[activeAmbient], 0f, instant ? 0f : AmbientFade, true);
        CurrentAmbience = next; rainGain = gain;
        if (next == Ambience.None) return;
        int target = activeAmbient < 0 ? 0 : 1 - activeAmbient;
        var voice = ambience[target]; StopVoice(voice);
        voice.Source.clip = next == Ambience.CityLab ? cityClip : next == Ambience.Wasteland ? wastelandClip
            : next == Ambience.LabNight ? nightClip : rainClip;
        activeAmbient = target;
        // Starting at zero preserves the authored seamless loops and avoids altering story Random state.
        Schedule(voice, AudioSettings.dspTime + StartLead, instant ? gain : 0f);
        Fade(voice, gain, instant ? 0f : AmbientFade, false); AmbienceStarts++;
    }

    void BeginWarning(bool restoring)
    {
        warning = true; tier = 3;
        WarningStartedDsp = AudioSettings.dspTime;
        // The warning is an authored hard silence within 50 ms. A frame-driven
        // 50 ms fade can overrun during UI work; clear and Stop in this same
        // frame, before the warning is rendered, instead of waiting for Update.
        foreach (var voice in voices)
            if (voice != heartbeat) StopVoice(voice);
        WarningSilencedDsp = AudioSettings.dspTime;
        if (audio != null)
        { audio.SetSfxClipsSuppressed(this, true, heartbeatClips); heartbeatLeaseHeld = true; }
        OS_Handle.Instance?.HeartbeatWaveform?.StopCurrentBeatSounds();
        if (!restoring) Schedule(heartbeat, WarningStartedDsp + .6d, .45f);
    }

    void Schedule(Voice voice, double at, float gain)
    {
        if (voice?.Source == null || voice.Source.clip == null) return;
        StopVoice(voice);
        if (IsPaused) at = pausedAt + Math.Max(0d, at - AudioSettings.dspTime);
        voice.Source.timeSamples = 0; voice.Source.volume = gain;
        voice.StartDsp = at; voice.StartSample = 0; voice.Armed = true;
        if (IsPaused) voice.PendingPause = true;
        else voice.Source.PlayScheduled(at);
    }

    void Fade(Voice voice, float to, float seconds, bool stop)
    {
        if (voice?.Source == null) return;
        voice.FadeAt = clock; voice.FadeSeconds = seconds; voice.FadeFrom = voice.Source.volume;
        voice.FadeTo = to; voice.Fading = seconds > 0f; voice.StopAfterFade = stop;
        if (seconds > 0f) return;
        voice.Source.volume = to;
        if (stop) StopVoice(voice);
    }

    void TickFade(Voice voice)
    {
        if (!voice.Fading) return;
        float progress = Mathf.Clamp01((clock - voice.FadeAt) / voice.FadeSeconds);
        voice.Source.volume = Mathf.Lerp(voice.FadeFrom, voice.FadeTo, progress);
        if (progress < 1f) return;
        voice.Fading = false;
        if (voice.StopAfterFade) StopVoice(voice);
    }

    void SetPaused(bool value)
    {
        if (!IsRunning || value == IsPaused) return;
        double now = AudioSettings.dspTime;
        IsPaused = value;
        if (value)
        {
            pausedAt = now;
            foreach (var voice in voices)
            {
                if (!voice.Armed) continue;
                if (now < voice.StartDsp) { voice.Source.Stop(); voice.PendingPause = true; }
                else if (voice.Source.isPlaying) { voice.Source.Pause(); voice.ActivePause = true; }
            }
        }
        else
        {
            double shift = now - pausedAt;
            if (!double.IsNaN(ThemeStartDsp)) ThemeStartDsp += shift;
            foreach (var voice in voices)
            {
                if (voice.Armed) voice.StartDsp += shift;
                if (voice.PendingPause)
                { voice.Source.timeSamples = voice.StartSample; voice.Source.PlayScheduled(voice.StartDsp); }
                else if (voice.ActivePause) voice.Source.UnPause();
                voice.PendingPause = voice.ActivePause = false;
            }
        }
    }

    bool MusicVoicesSilent()
    {
        if (voices == null) return true;
        foreach (var voice in voices)
            if (voice != heartbeat && voice.Source.volume > .0001f) return false;
        return true;
    }

    static bool HasStarted(Voice voice) => voice?.Source != null && voice.Source.clip != null
        && voice.Armed && !voice.PendingPause && !voice.ActivePause
        && AudioSettings.dspTime >= voice.StartDsp && voice.Source.isPlaying && voice.Source.volume > 0f;

    static void StopVoice(Voice voice)
    {
        if (voice?.Source == null) return;
        voice.Source.Stop(); voice.Source.volume = 0f;
        voice.Armed = voice.PendingPause = voice.ActivePause = voice.Fading = false;
    }

    void StopPlayback(bool keepBackgroundSilent = false)
    {
        if (keepBackgroundSilent && !leaseHeld)
        {
            audio = AudioManager.Instance;
            if (audio != null)
            { audio.SetBackgroundAudioSuppressed(this, true, 0f); leaseHeld = true; }
        }
        if (!IsRunning && !leaseHeld && !heartbeatLeaseHeld) return;
        if (voices != null) foreach (var voice in voices) StopVoice(voice);
        if (!keepBackgroundSilent)
        {
            if (leaseHeld && audio != null) audio.SetBackgroundAudioSuppressed(this, false);
            leaseHeld = false;
        }
        if (heartbeatLeaseHeld && audio != null) audio.SetSfxClipsSuppressed(this, false);
        heartbeatLeaseHeld = IsRunning = IsPaused = warning = heartbeatCounted = false;
        CurrentAmbience = Ambience.None; activeAmbient = -1; tier = -1;
        ThemeStartDsp = double.NaN; clock = 0f;
    }
}
