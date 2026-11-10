using UnityEngine;

public sealed partial class OS_Handle
{
    [Header("Chapter 02 Cursor Experiment")]
    [SerializeField] OSWindowUnit cursorExperimentWindow;
    [SerializeField] OSCursorExperimentController cursorExperiment;
    float cursorExperimentHandoffHold;
    OSCursorExperimentStoryDirector cursorExperimentStory;
    bool cursorExperimentStoryProtectionActive;
    OSCursorExperimentInteriorPresentation cursorExperimentInterior;
    OSCursorReleaseDirector cursorRelease;
    OSCursorHeartTalkDirector cursorHeartTalk;
    OSCursorExperimentDiscardPresentation cursorExperimentDiscard;

    public OSCursorReleaseDirector CursorRelease => cursorRelease != null
        ? cursorRelease : (cursorRelease = GetComponent<OSCursorReleaseDirector>());
    public OSCursorHeartTalkDirector CursorHeartTalk => cursorHeartTalk != null
        ? cursorHeartTalk : (cursorHeartTalk = GetComponent<OSCursorHeartTalkDirector>());

    public OSCursorExperimentController CursorExperiment => cursorExperiment;
    public OSCursorExperimentInteriorPresentation CursorExperimentInterior => cursorExperimentInterior != null
        ? cursorExperimentInterior : (cursorExperimentInterior = GetComponent<OSCursorExperimentInteriorPresentation>());
    public bool IsCursorExperimentInteriorBlockingMessages
    {
        get
        {
            var data = GameDataManager.Instance;
            var p = data != null ? data.OSProcessData.memory : null;
            return data != null && data.CurrentChapterIndex == 2 && p != null && p.chapter02ClawCursorCaptured
                && p.chapter02CursorExperimentRound < 4
                && (CursorExperimentInterior == null || !CursorExperimentInterior.IsReadyForStory);
        }
    }
    public OSCursorExperimentStoryDirector CursorExperimentStory => cursorExperimentStory != null
        ? cursorExperimentStory : (cursorExperimentStory = GetComponent<OSCursorExperimentStoryDirector>());
    public bool IsCursorExperimentMessageProtected => (CursorExperimentStory != null && CursorExperimentStory.IsMessageProtected)
        || (CursorRelease != null && CursorRelease.IsMessageProtected)
        || (CursorHeartTalk != null && CursorHeartTalk.IsMessageProtected);
    public bool IsCursorExperimentStoryProtected => (CursorExperimentStory != null && CursorExperimentStory.IsStoryProtected)
        || (CursorRelease != null && CursorRelease.IsStoryProtected)
        || (CursorHeartTalk != null && CursorHeartTalk.IsStoryProtected);
    public bool IsCursorExperimentPending
    {
        get
        {
            var data = GameDataManager.Instance;
            int round = data != null ? data.OSProcessData.memory.chapter02CursorExperimentRound : 0;
            return data != null && data.CurrentChapterIndex == 2 && round >= 1 && round <= 3;
        }
    }

    void ResolveCursorExperimentReferences()
    {
        if (cursorExperimentWindow == null)
            cursorExperimentWindow = transform.Find("Windows/WindowUnit_CursorExperiment")?.GetComponent<OSWindowUnit>();
        if (cursorExperiment == null && cursorExperimentWindow != null)
            cursorExperiment = cursorExperimentWindow.GetComponent<OSCursorExperimentController>();
        if (Application.isPlaying && GetComponent<OSChapter02ActivityMusic>() == null)
            gameObject.AddComponent<OSChapter02ActivityMusic>();
        if (Application.isPlaying && CursorExperimentStory == null)
            cursorExperimentStory = gameObject.AddComponent<OSCursorExperimentStoryDirector>();
        if (Application.isPlaying && GetComponent<OSCursorExperimentAmbientGlitch>() == null)
            gameObject.AddComponent<OSCursorExperimentAmbientGlitch>();
        if (Application.isPlaying && GetComponent<OSCursorExperimentFocusPresentation>() == null)
            gameObject.AddComponent<OSCursorExperimentFocusPresentation>();
        if (Application.isPlaying && CursorExperimentInterior == null)
            cursorExperimentInterior = gameObject.AddComponent<OSCursorExperimentInteriorPresentation>();
        if (Application.isPlaying && CursorRelease == null)
            cursorRelease = gameObject.AddComponent<OSCursorReleaseDirector>();
        if (Application.isPlaying && GetComponent<OSCursorHeartTalkPresentation>() == null)
            gameObject.AddComponent<OSCursorHeartTalkPresentation>();
        if (Application.isPlaying && CursorHeartTalk == null)
            cursorHeartTalk = gameObject.AddComponent<OSCursorHeartTalkDirector>();
        if (Application.isPlaying && GetComponent<OSMemoryQuakePresentation>() == null)
            gameObject.AddComponent<OSMemoryQuakePresentation>();
        if (Application.isPlaying && GetComponent<OSMemoryReadAlongPresentation>() == null)
            gameObject.AddComponent<OSMemoryReadAlongPresentation>();
        if (Application.isPlaying && cursorExperimentDiscard == null)
            cursorExperimentDiscard = gameObject.AddComponent<OSCursorExperimentDiscardPresentation>();
        if (cursorExperiment != null)
        {
            cursorExperiment.Completed -= HandleCursorExperimentCompleted;
            cursorExperiment.Completed += HandleCursorExperimentCompleted;
        }
    }

    void RefreshCursorExperiment()
    {
        RefreshCursorExperimentStoryProtection();
        if (!IsSingletonInstance || cursorExperiment == null || cursorExperimentWindow == null) return;
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 2) return;
        var progress = data.OSProcessData.memory;
        if (progress.chapter02HeartTalkStep >= (int)OSCursorHeartTalkDirector.HeartTalkStep.Return) return;
        if (!progress.chapter02ClawCursorCaptured) return;
        if (!IsOSVisible || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || IsSystemInputLockedWithoutCursorExperiment) return;
        if (CursorExperimentInterior != null && CursorExperimentInterior.IsEngaged) return;

        if (progress.chapter02CursorExperimentRound >= 4)
        {
            // A load after escape restores the conversation room, never the
            // maze/cage or the discarded cabinet. Pending future OS return is
            // deliberately a separate story step.
            CursorExperimentInterior?.BeginEntry(Vector2.zero, true);
            CursorRelease?.BeginAfterEscape();
            RefreshCursorExperimentStoryProtection();
            return;
        }

        bool resume = progress.chapter02CursorExperimentRound > 0 || CursorExperimentInterior?.HasFreshCapture != true;
        if (!resume && clawMachineGame != null && clawMachineGame.IsPresentationVisible)
        {
            if (clawMachineGame.Phase != OSClawMachineGameWindowController.ClawPhase.CursorSuspended)
            { cursorExperimentHandoffHold = 0f; return; }
            // A slow frame during the shell fall must not consume the whole
            // readable pause on the first frame of the suspended pose.
            cursorExperimentHandoffHold += Mathf.Min(Time.unscaledDeltaTime, .05f);
            if (cursorExperimentHandoffHold < .6f) return;
        }

        Vector2 from = CursorHandle.Instance != null ? CursorHandle.Instance.DisplayedScreenPosition : new Vector2(Screen.width / 2f, Screen.height / 2f);
        CursorExperimentInterior?.BeginEntry(from, resume);
        CursorExperimentStory?.ApplyStoryHold();
        RefreshCursorExperimentStoryProtection();
        cursorExperimentHandoffHold = 0f;
    }

    // Called under the frozen signal frame, so ownership and scene replacement
    // happen without exposing an intermediate desktop or a travelling claw.
    internal void RetireCapturedClawForInterior()
    {
        clawMachineGame?.SuspendCursorTrap();
        clawMachineGameWindow?.CloseFromScript();
        clawMachineWindow?.CloseFromScript();
        if (clawMachineDesktopProcess != null) clawMachineDesktopProcess.gameObject.SetActive(false);
        if (memoryWindow != null && memoryWindow.IsOpen && !memoryWindow.IsMinimized) memoryWindow.Minimize();
    }

    void RefreshCursorExperimentStoryProtection()
    {
        bool protect = IsCursorExperimentStoryProtected;
        if (cursorExperimentStoryProtectionActive == protect) return;
        cursorExperimentStoryProtectionActive = protect;
        RefreshEvent01LinkCloseSuppression();
    }

    void HandleCursorExperimentCompleted()
    {
        GetComponent<OSCursorExperimentAmbientGlitch>()?.ResetEffect();
        GetComponent<OSCursorExperimentFocusPresentation>()?.ResetPresentation();
        cursorExperimentDiscard?.Begin(cursorExperimentWindow);
        CursorRelease?.BeginAfterEscape();
        cursorExperimentHandoffHold = 0f;
        RefreshCursorExperimentStoryProtection();
    }

    void ResetCursorExperimentPresentation()
    {
        CursorHeartTalk?.ResetPresentation();
        CursorRelease?.ResetPresentation();
        cursorExperimentDiscard?.ResetPresentation();
        GetComponent<OSCursorExperimentAmbientGlitch>()?.ResetEffect();
        GetComponent<OSCursorExperimentFocusPresentation>()?.ResetPresentation();
        // The controller snapshots the interior's settings overlay. Release its
        // modal layer before the interior restores the underlying desktop.
        cursorExperiment?.ResetPresentation();
        CursorExperimentInterior?.ResetPresentation();
        CursorExperimentStory?.ResetPresentation();
        cursorExperimentWindow?.PrepareRuntimeClosed();
        cursorExperimentHandoffHold = 0f;
        RefreshCursorExperimentStoryProtection();
    }
}
