using UnityEngine;

// Activity music borrows the player's Music channel without changing the saved
// record selection. Story progress reconstructs the lease; transient gaps do not.
[DisallowMultipleComponent]
[DefaultExecutionOrder(620)]
public sealed partial class OSChapter02ActivityMusic : MonoBehaviour
{
    public const string ClawMusicKey = "Chapter02_ClawMachineMusic";
    public const string CursorMusicKey = "Chapter02_CursorExperimentMusic";
    public const string ClawTrackId = "chapter02_claw_activity";
    public const string CursorTrackId = "chapter02_cursor_activity";
    const float CaptureGapSeconds = .20f;
    const float WarningFadeSeconds = .12f;

    enum Activity { None, Memory, Claw, Cursor }
    OS_Handle os;
    OSGramophonePlayer player;
    OSWindowUnit settings, live;
    OSMemoryProcessSaveData observed;
    Activity activity;
    bool warningShown, returnApplied, captureNotified;
    int observedChapter = -1, previousHeartStep, captureFrame = -1;
    float captureGap;

    public bool IsHoldingMusic => activity != Activity.None;
    public bool IsWarningSilent => warningShown && !returnApplied;
    public bool HasReturnedToMainOS => returnApplied;

    void Awake() => Resolve();
    void OnEnable() => Resolve();

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (player == null) player = GetComponent<OSGramophonePlayer>();
        if (settings == null && os != null)
            settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (live == null && os?.CharacterWindowController != null)
            live = os.CharacterWindowController.GetComponent<OSWindowUnit>();
    }

    void Update()
    {
        RefreshMusicState();
        if (captureGap <= 0f || Time.frameCount == captureFrame || !Available) return;
        captureGap = Mathf.Max(0f, captureGap - Mathf.Min(Time.unscaledDeltaTime, .05f));
        if (captureGap <= 0f) RefreshMusicState();
    }

    // Also called by the player before its automatic background-restoration path.
    public void RefreshMusicState()
    {
        Resolve();
        // The closing choreography keeps the current bed until Live starts moving,
        // then owns its fade. A routine memory refresh must not pause or replace it.
        if (os?.Chapter02PaperPush?.IsHeartTalkAudioTransition == true) return;
        var data = GameDataManager.Instance;
        var memory = data?.OSProcessData.memory;
        int chapter = data != null ? data.CurrentChapterIndex : -1;
        if (!ReferenceEquals(observed, memory) || observedChapter != chapter)
        {
            player?.ReleaseActivityMusic(this, false, false);
            observed = memory;
            observedChapter = chapter;
            activity = Activity.None;
            captureGap = 0f;
            captureNotified = returnApplied = false;
            int restoredStep = memory?.chapter02HeartTalkStep ?? 0;
            // A restored Quake already restores its warning. A live transition
            // into Quake stays musical until RenderAlert actually shows the band.
            warningShown = restoredStep >= (int)OSCursorHeartTalkDirector.HeartTalkStep.Quake;
            previousHeartStep = restoredStep;
        }
        if (player == null || os == null || memory == null || chapter != 2)
        {
            player?.ReleaseActivityMusic(this, false, false);
            activity = Activity.None;
            return;
        }

        int heartStep = memory.chapter02HeartTalkStep;
        if (heartStep < previousHeartStep)
        {
            // Console/checkpoint rewinds can reuse the same save object.
            warningShown = heartStep >= (int)OSCursorHeartTalkDirector.HeartTalkStep.Quake;
            returnApplied = captureNotified = false;
            captureGap = 0f;
        }
        previousHeartStep = heartStep;
        if (heartStep >= (int)OSCursorHeartTalkDirector.HeartTalkStep.Done)
        {
            ApplyReturn();
        }
        if (heartStep >= (int)OSCursorHeartTalkDirector.HeartTalkStep.QuakeLine) warningShown = true;

        if (memory.chapter02ClawCursorCaptured) captureNotified = false;
        bool captured = !returnApplied && (memory.chapter02ClawCursorCaptured || captureNotified
            || memory.chapter02CursorExperimentRound > 0 || memory.chapter02CursorReleaseStep > 0 || heartStep > 0);
        var clawWindow = os.ClawMachineGame?.Window;
        Activity wanted = captured ? Activity.Cursor
            : !returnApplied && clawWindow != null && clawWindow.IsOpen && !clawWindow.IsClosing ? Activity.Claw : Activity.None;
        if (wanted == Activity.None)
        {
            RefreshMemoryMusic(memory);
            return;
        }
        activity = wanted;
        bool silent = warningShown || captureGap > 0f;
        string key = wanted == Activity.Claw ? ClawMusicKey : CursorMusicKey;
        string id = wanted == Activity.Claw ? ClawTrackId : CursorTrackId;
        bool visible = wanted == Activity.Claw ? WindowVisible(clawWindow) : CursorPresentationVisible(memory);
        float fade = captureGap > 0f ? 0f : warningShown ? WarningFadeSeconds
            : wanted == Activity.Cursor ? .10f : .35f;
        player.SetActivityMusic(this, id, FindMusic(key), Available && visible, silent, fade);
    }

    bool Available => Application.isPlaying && Application.isFocused && os != null && os.IsOSVisible
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && !os.IsSystemInputLockedWithoutCursorExperiment
        && !(settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy);

    static bool WindowVisible(OSWindowUnit window) => window != null && window.IsOpen && !window.IsMinimized
        && !window.IsOpening && !window.IsClosing && window.gameObject.activeInHierarchy;

    bool CursorPresentationVisible(OSMemoryProcessSaveData memory)
    {
        var interior = os.CursorExperimentInterior;
        if (interior?.IsTransitioning == true) return true;
        if (memory.chapter02CursorReleaseStep > 0 || memory.chapter02HeartTalkStep > 0
            || memory.chapter02CursorExperimentRound >= 4) return WindowVisible(live);
        return WindowVisible(os.ClawMachineGame?.Window)
            || WindowVisible(os.CursorExperiment != null ? os.CursorExperiment.GetComponent<OSWindowUnit>() : null);
    }

    static AudioClip FindMusic(string key) => string.IsNullOrEmpty(key) ? null : GameDataManager.Instance?.AudioData?.BGM?.Find(
        entry => entry != null && entry.keyid == key)?.audioclip;

    static OSChapter02ActivityMusic Current => OS_Handle.Instance?.GetComponent<OSChapter02ActivityMusic>();

    // Called at the actual contact, before the capture SFX. Restoring the saved
    // captured flag never calls this and therefore cannot replay the silent gap.
    public static void NotifyCursorCaptured()
    {
        var current = Current;
        if (current == null) return;
        current.RefreshMusicState();
        current.captureNotified = true;
        current.captureGap = CaptureGapSeconds;
        current.captureFrame = Time.frameCount;
        current.RefreshMusicState();
    }

    public static void NotifyWarningShown()
    {
        var current = Current;
        if (current == null) return;
        current.RefreshMusicState();
        current.warningShown = true;
        current.captureGap = 0f;
        current.RefreshMusicState();
    }

    public static void NotifyLoadingStarted() => NotifyWarningShown();

    public static void NotifyReturnedToMainOS()
    {
        var current = Current;
        if (current == null) return;
        current.RefreshMusicState();
        current.ApplyReturn();
    }

    void ApplyReturn()
    {
        if (returnApplied || player == null) return;
        player.ReleaseActivityMusic(this, false, true);
        player.SuppressAutomaticBackgroundMusic(0f);
        activity = Activity.None;
        captureGap = 0f;
        returnApplied = true;
        // The next refresh resumes the authored memory segment at 5410.
        // The old gramophone song must not restart between those two owners.
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) player?.PauseActivityMusic();
    }

    void OnDisable()
    {
        player?.ReleaseActivityMusic(this, false, false);
        activity = Activity.None;
        observed = null;
        captureGap = 0f;
    }
}
