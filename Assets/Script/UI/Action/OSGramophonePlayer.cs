using System;
using System.Collections.Generic;
using UnityEngine;

// Lives on the OS root; closing/minimizing its controls does not stop music.
[DisallowMultipleComponent]
public sealed partial class OSGramophonePlayer : MonoBehaviour
{
    public const string Secret = "secret", Chapter01 = "old_dream", PaperPush = "paper_push", Doorway = "doorway";
    public static OSGramophonePlayer Instance { get; private set; }
    [SerializeField] OSWindowUnit gramophoneWindow;
    [SerializeField] OSWindowUnit paperPushWindow;
    OS_Handle os;
    OSPaperPushMusic paperMusic;
    OSGramophoneSaveData state;
    readonly List<string> available = new List<string> { Secret, Chapter01 };
    AudioClip backgroundClip;
    bool temporaryMusic, temporaryPaused, suppressTemporary, paperWasVisible, backgroundPaused;
    bool restored, chapterWasActive;
    bool IsHeartTalkTransition => os?.Chapter02PaperPush?.IsHeartTalkAudioTransition == true;

    public event Action Changed;
    public IReadOnlyList<string> AvailableTracks => available;
    public string CurrentTrackId => HasActivitySelection ? (activityClip != null ? activityTrackId : null)
        : temporaryMusic ? PaperPush : state?.selectedTrackId;
    public float Volume => state?.volume ?? .7f;
    public bool IsMuted => Volume <= 0f || (temporaryMusic && paperMusic != null && paperMusic.IsMuted);
    AudioSource DisplaySource => HasActivitySelection ? ActivitySource : temporaryMusic ? paperMusic?.Source : OwnedSource;
    AudioSource OwnedSource
    {
        get
        {
            var source = AudioManager.Instance?.CurrentMusicSource;
            return source != null && backgroundClip != null && source.clip == backgroundClip ? source : null;
        }
    }
    public bool IsPlaying => DisplaySource != null && DisplaySource.isPlaying;
    public float PositionSeconds => DisplaySource != null ? DisplaySource.time : state?.playbackSeconds ?? 0f;
    public float DurationSeconds => DisplaySource?.clip != null ? DisplaySource.clip.length : (HasActivitySelection ? activityClip : Clip(CurrentTrackId))?.length ?? 0f;

    void Awake()
    {
        Instance = this;
        os = GetComponent<OS_Handle>();
        paperMusic = GetComponent<OSPaperPushMusic>();
    }

    void OnEnable()
    {
        if (paperPushWindow != null) paperPushWindow.OpenStarted += OnPaperOpened;
    }

    void OnDisable()
    {
        if (paperPushWindow != null) paperPushWindow.OpenStarted -= OnPaperOpened;
        CapturePosition();
        ResetActivityMusic();
        ReleaseTemporary(false, true);
        StopTemporaryTail();
        if (OwnedSource != null) AudioManager.Instance.StopMusic(0f);
        backgroundClip = null;
        restored = false;
    }

    void OnDestroy() { DisposeActivityEffects(); if (Instance == this) Instance = null; }

    void Update()
    {
        // Before any early return: a released paper-push voice always finishes its fade.
        TickTemporaryTail();
        var data = GameDataManager.Instance;
        var audio = AudioManager.Instance;
        if (data == null || audio == null) return;
        BindSave(data);
        // Resolve the story lease before automatic restore, including on a load
        // directly into a warning/loading/returned checkpoint.
        GetComponent<OSChapter02ActivityMusic>()?.RefreshMusicState();
        if (RefreshCollectedActivityTracks(data) | RefreshCollectedStoryTracks(data)) Changed?.Invoke();
        if (IsHeartTalkTransition)
        {
            // Keep the paper-push source alive during the closing gesture. The
            // director fades it when Live starts moving, then holds every bed quiet.
            os.Chapter02PaperPush.RefreshHeartTalkAudio();
            CapturePosition();
            return;
        }
        if (data.CurrentChapterIndex < 2) suppressAutomaticBackground = false;
        if (OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true) return;
        if (os?.Chapter01Ending?.IsPresenting == true) return;
        if (MrTGame.Core.GameManager.Instance?.Phase == MrTGame.Core.GamePhase.Boot)
        {
            ReleaseTemporary(false);
            paperWasVisible = false;
            if (OwnedSource != null) { CapturePosition();audio.StopMusic(0f); }
            restored = false;
            return;
        }
        if (StoryBlocksPlayback()) { SuspendBackgroundForStory(); return; }
        if (activityOwner != null && !activityAllowsTemporary) return;
        bool chapterActive = data.CurrentChapterIndex >= 1;
        if (!chapterActive)
        {
            if (chapterWasActive)
            {
                CapturePosition();ReleaseTemporary(false);
                if (OwnedSource != null) audio.StopMusic(0f);
                audio.SetMusicPlaybackGain(1f);
                backgroundClip = null;restored = false;
            }
            chapterWasActive = false;
            return;
        }
        chapterWasActive = true;
        if (!state.chapter01MusicInitialized && data.CurrentChapterIndex == 1)
        {
            state.chapter01MusicInitialized = true;
            state.selectedTrackId = Chapter01;
            state.wantsPlayback = true;
            state.playbackSeconds = 0f;
            restored = false;
            audio.StopAmbient(.65f);
        }
        if (!restored && state.chapter01MusicInitialized && !suppressAutomaticBackground)
        {
            restored = true;
            RestoreBackground();
            Changed?.Invoke();
        }

        bool storyOwnsMusic = audio.IsBackgroundAudioSuppressed
            || (audio.CurrentMusicSource != null && OwnedSource == null
                && !(activityAllowsTemporary && ActivitySource != null));
        bool paperVisible = paperPushWindow != null && paperPushWindow.IsOpen && !paperPushWindow.IsMinimized
            && os != null && os.IsOSVisible && !os.IsSystemInputLocked && !storyOwnsMusic;
        if (paperPushWindow != null && paperPushWindow.IsOpen) CollectPaperPush();
        if (!paperVisible && paperWasVisible)
        {
            ReleaseTemporary(true);
            suppressTemporary = false;
        }
        if (paperVisible && !temporaryMusic && !suppressTemporary && paperMusic?.Source != null)
            StartTemporary();
        paperWasVisible = paperVisible;
        if (!temporaryMusic && restored && state.wantsPlayback && !suppressAutomaticBackground && audio.CurrentMusicSource == null)
            RestoreBackground();
        if (temporaryMusic && paperMusic.Source != null)
        {
            TickTemporaryFadeIn(paperMusic.Source);
            paperMusic.Source.mute = paperMusic.IsMuted;
        }
        if (activityOwner != null) { RefreshActivityPlayback(); RefreshActivityEffects(); }
        CapturePosition();
    }

    void BindSave(GameDataManager data)
    {
        var next = data.OSGramophoneState;
        if (ReferenceEquals(state, next)) return;
        // Loading a save must not write the old session's playhead into the new save.
        ResetActivityMusic();
        ReleaseTemporary(false, true);
        StopTemporaryTail();
        if (OwnedSource != null) AudioManager.Instance?.StopMusic(0f);
        state = next;state.EnsureData();backgroundClip = null;
        restored = backgroundPaused = suppressTemporary = paperWasVisible = false;
        suppressAutomaticBackground = false;
        available.Clear();available.Add(Secret);available.Add(Chapter01);
        var energy = data.OSEnergyLoopState;
        // Migrate existing saves with evidence of an actual match, not just an unlocked icon.
        if (energy.paperPushWins + energy.paperPushLosses > 0 || energy.paperPushMatch.active || energy.paperPushSealed)
            state.paperPushCollected = true;
        if (state.paperPushCollected) available.Add(PaperPush);
        var picture = data.OSProcessData.memory.pictureScenario;
        // Older saves already inside the home have earned this track too.
        if ((picture != null && picture.initialized && picture.scenarioId == "chapter01_home")
            || data.CurrentChapterIndex > 1) state.doorwayCollected = true;
        if (state.doorwayCollected) available.Add(Doorway);
        RestoreCollectedActivityTracks(data);
        RestoreCollectedStoryTracks(data);
        if (!string.IsNullOrEmpty(state.selectedTrackId) && !available.Contains(state.selectedTrackId))
            state.selectedTrackId = Chapter01;
        Changed?.Invoke();
    }

    public void SuspendForChapterEnding()
    {
        CapturePosition();
        ReleaseTemporary(false);
        paperWasVisible = false;
        restored = false;
    }

    public void NotifyChapter01HomeEntered()
    {
        var data = GameDataManager.Instance;
        var audio = AudioManager.Instance;
        if (data == null || audio == null || data.CurrentChapterIndex != 1
            || os?.Chapter01Ending?.IsPresenting == true) return;
        BindSave(data);
        if (!state.doorwayCollected)
        {
            state.doorwayCollected = true;
            if (!available.Contains(Doorway)) available.Add(Doorway);
            Changed?.Invoke();
        }
        // This is a story cue, not an ongoing override of the player's controls.
        // Reopening the illustration or loading after a manual stop must stay silent.
        if (state.chapter01DoorwayMusicInitialized || Clip(Doorway) == null) return;
        CapturePosition();
        ReleaseTemporary(false);
        suppressTemporary = paperWasVisible;
        state.chapter01MusicInitialized = true;
        state.chapter01DoorwayMusicInitialized = true;
        state.selectedTrackId = Doorway;
        state.wantsPlayback = true;
        state.playbackSeconds = 0f;
        backgroundPaused = false;
        audio.StopAmbient(1.1f);
        RestoreBackground(1.1f);
        restored = true;
        // Automatic playback intentionally leaves the library's NEW dot intact.
        Changed?.Invoke();
    }

    void OnPaperOpened(OSWindowUnit _) { if (GameDataManager.Instance != null) { BindSave(GameDataManager.Instance);CollectPaperPush(); } }
    void CollectPaperPush()
    {
        if (state == null || state.paperPushCollected) return;
        state.paperPushCollected = true;available.Add(PaperPush);Changed?.Invoke();
    }


    public bool IsNew(string id) => state == null || !state.manuallyPlayedTrackIds.Contains(id);
    public void PlayTrack(string id, bool manuallySelected = true)
    {
        if (StoryBlocksPlayback()) return;
        if (state == null || !available.Contains(id) || Clip(id) == null) return;
        CapturePosition();
        YieldActivityToManualSelection();
        suppressAutomaticBackground = false;
        ReleaseTemporary(false);
        suppressTemporary = paperWasVisible;
        if (manuallySelected && !state.manuallyPlayedTrackIds.Contains(id)) state.manuallyPlayedTrackIds.Add(id);
        bool changed = state.selectedTrackId != id;
        state.selectedTrackId = id;state.wantsPlayback = true;
        if (changed) state.playbackSeconds = 0f;
        RestoreBackground();Changed?.Invoke();
    }

    public void NextTrack(int direction)
    {
        if (available.Count == 0) return;
        int index = available.IndexOf(CurrentTrackId);
        PlayTrack(available[(Mathf.Max(0, index) + direction % available.Count + available.Count) % available.Count], false);
    }

    public void TogglePlayback()
    {
        if (StoryBlocksPlayback()) return;
        if (state == null) return;
        // A silent/hidden memory has no activity to resume. Explicit Play uses
        // the saved record, just as choosing it from the library does.
        if (HasActivitySelection && activityAllowsTemporary
            && (activitySilence || (activitySuspended && !activityManualSuspended)))
            YieldActivityToManualSelection();
        if (HasActivitySelection)
        {
            state.wantsPlayback = !state.wantsPlayback;
            RefreshActivityPlayback();
            Changed?.Invoke();
            return;
        }
        suppressAutomaticBackground = false;
        if (temporaryMusic)
        {
            temporaryPaused = !temporaryPaused;
            if (temporaryPaused) paperMusic.Source.Pause();else paperMusic.Source.UnPause();
        }
        else if (IsPlaying)
        {
            CapturePosition();state.wantsPlayback = false;
            AudioManager.Instance?.PauseMusicPlayback();backgroundPaused = true;
        }
        else
        {
            if (string.IsNullOrEmpty(state.selectedTrackId)) state.selectedTrackId = Chapter01;
            state.wantsPlayback = true;RestoreBackground();
        }
        Changed?.Invoke();
    }

    public void StopPlayback()
    {
        if (IsHeartTalkTransition) return;
        if (state == null) return;
        CapturePosition();ReleaseTemporary(false);suppressTemporary = paperWasVisible;
        state.wantsPlayback = false;state.playbackSeconds = 0f;
        StopActivityVoice(0f);
        if (OwnedSource != null) AudioManager.Instance?.StopMusic(.15f);
        backgroundPaused = false;Changed?.Invoke();
    }

    public void Seek(float seconds)
    {
        if (IsHeartTalkTransition) return;
        var source = DisplaySource;
        if (source?.clip == null || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        source.time = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, source.clip.length - .05f));
        if (!temporaryMusic && !HasActivitySelection && state != null) state.playbackSeconds = source.time;
    }

    public void SetVolume(float value)
    {
        if (IsHeartTalkTransition) return;
        if (state == null || float.IsNaN(value) || float.IsInfinity(value)) return;
        state.volume = Mathf.Clamp01(value);
        if (state.volume > 0f) state.lastAudibleVolume = state.volume;
        if (OwnedSource != null || ActivitySource != null)
            AudioManager.Instance.SetMusicPlaybackGain(state.volume * (HasActivitySelection ? activityGain : RecordGain(state.selectedTrackId)));
        // Turning down is immediate; turning up (or a fade-in still running) goes through Update.
        if (temporaryMusic && paperMusic?.Source != null)
            paperMusic.Source.volume = Mathf.Min(paperMusic.Source.volume, state.volume);
        Changed?.Invoke();
    }

    public void ToggleMute()
    {
        if (IsHeartTalkTransition) return;
        if (state == null) return;
        if (temporaryMusic && paperMusic != null && paperMusic.IsMuted)
        {
            paperMusic.ToggleMute();
            if (Volume <= 0f) SetVolume(state.lastAudibleVolume);
            Changed?.Invoke();
        }
        else SetVolume(Volume <= 0f ? state.lastAudibleVolume : 0f);
    }

    void RestoreBackground(float fadeDuration = .65f)
    {
        if (StoryBlocksPlayback()) return;
        if (activityOwner != null && (ActivityHoldsSilence || ActivityPlaybackSuspended || !activityManualOverride)) return;
        var audio = AudioManager.Instance;
        var clip = Clip(state?.selectedTrackId);
        if (audio == null || clip == null) return;
        backgroundClip = clip;
        float gain = Volume * RecordGain(state.selectedTrackId);
        audio.SetMusicPlaybackGain(gain);
        // Load/seek once, not on every UI refresh or window reopen.
        if (audio.CurrentMusicSource == null || audio.CurrentMusicSource.clip != clip)
        {
            audio.PlayMusic(clip, fadeDuration, true, gain);
            var source = audio.CurrentMusicSource;
            if (source != null) source.time = Mathf.Repeat(state.playbackSeconds, Mathf.Max(.01f, clip.length));
        }
        else if (backgroundPaused) audio.ResumeMusicPlayback(BorrowedResumeFade);
        else if (state.wantsPlayback && !audio.CurrentMusicSource.isPlaying)
            audio.PlayMusic(clip, .25f, true, gain);
        ApplyRecordPitch(audio.CurrentMusicSource);
        backgroundPaused = !state.wantsPlayback;
        if (backgroundPaused) audio.PauseMusicPlayback();
    }

    void StartTemporary()
    {
        if (StoryBlocksPlayback()) return;
        CollectPaperPush();
        var clip = Clip(PaperPush);
        if (clip == null) return;
        CapturePosition();
        // The record steps back while the paper-push track comes up: a short crossfade, not a cut.
        if (OwnedSource != null) { AudioManager.Instance.PauseMusicPlayback(BorrowedPauseFade);backgroundPaused = true; }
        else if (ActivitySource != null) { AudioManager.Instance.PauseMusicPlayback(BorrowedPauseFade);activityPaused = true; }
        var source = paperMusic.Source;
        source.outputAudioMixerGroup = AudioManager.Instance.MusicMixerGroup;
        // Reopened while its tail is still fading: turn the same voice back up.
        bool continuing = ResumeTemporaryTail(source, clip);
        if (!continuing) { source.clip = clip;source.loop = true;source.volume = 0f; }
        source.mute = paperMusic.IsMuted;
        if (!continuing) source.Play();
        temporaryMusic = true;temporaryPaused = false;Changed?.Invoke();
    }

    void ReleaseTemporary(bool resume, bool immediate = false)
    {
        if (!temporaryMusic) return;
        if (paperMusic?.Source != null) BeginTemporaryTail(paperMusic.Source, immediate || temporaryPaused);
        temporaryMusic = temporaryPaused = false;
        if (resume && activityOwner != null) RefreshActivityPlayback();
        if (resume && activityOwner == null && !suppressAutomaticBackground && state != null && state.wantsPlayback
            && (AudioManager.Instance?.CurrentMusicSource == null || OwnedSource != null)) RestoreBackground();
        Changed?.Invoke();
    }

    void CapturePosition()
    {
        if (state != null && OwnedSource != null && !backgroundPaused && state.wantsPlayback)
            state.playbackSeconds = OwnedSource.time;
    }

    static AudioClip Clip(string id)
    {
        string key = id == Secret ? "SecretPart" : id == Chapter01 ? "Chapter01Dream"
            : id == PaperPush ? "PaperPush" : id == Doorway ? "Chapter01Doorway" : ActivityMusicKey(id) ?? StoryMusicKey(id);
        return key == null ? null : GameDataManager.Instance?.AudioData?.BGM?.Find(x => x != null && x.keyid == key)?.audioclip;
    }
    public static string TrackNameKey(string id) => "ui.os.gramophone.track." + id;
    public static string TrackFallback(string id) => id == Secret ? "屏幕后" : id == Chapter01 ? "旧梦"
        : id == PaperPush ? "纸上角力" : id == Doorway ? "家？"
        : id == OSChapter02ActivityMusic.ClawTrackId ? "Cute Tune"
        : id == OSChapter02ActivityMusic.CursorTrackId ? "Curious Circuits"
        : id == OSChapter02ActivityMusic.DailyTrackId ? "照常"
        : id == OSChapter02ActivityMusic.WarehouseTrackId ? "无人应答"
        : id == OSChapter02ActivityMusic.ReturnHomeTrackId ? "归家"
        : id == OSChapter02ActivityMusic.MakeupTrackId ? "One Moment" : StoryTrackFallback(id);
}
