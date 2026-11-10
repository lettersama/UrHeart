using System;
using System.Collections;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;

[Serializable]
public sealed class OSOpenWindowSaveData
{
    public string windowId;
    public bool minimized;
    public int siblingIndex;
}

[Serializable]
public sealed class OSResumeSaveData
{
    public bool captured;
    public List<OSOpenWindowSaveData> windows = new List<OSOpenWindowSaveData>();
    public int characterState;
    public bool secretInProgress;
    public bool giftStarted, giftLanded, giftClosed, chapterEntryConfirmed;
    public bool stateCheckMessageShown, recordHoverMessageShown;
    public bool hasFaultBoard, faultBoardCompleted;
    public bool faultGuideSeen, faultBoardInteracted;
    public int faultBoardMask;
    public OSHeartTreatmentResumeData treatment = new OSHeartTreatmentResumeData();

    public void EnsureData()
    {
        if (windows == null) windows = new List<OSOpenWindowSaveData>();
        windows.RemoveAll(x => x == null || string.IsNullOrWhiteSpace(x.windowId));
        if (treatment == null) treatment = new OSHeartTreatmentResumeData();
        treatment.EnsureData();
    }
}

[Serializable]
public sealed class OSHeartTreatmentResumeData
{
    public bool active;
    public int processIndex, nextBeatIndex, resolvedBeatCount, initialBattery;
    public float elapsed, state;
    public List<OSHeartBeatResumeData> beats = new List<OSHeartBeatResumeData>();
    public void EnsureData()
    {
        if (beats == null) beats = new List<OSHeartBeatResumeData>();
        beats.RemoveAll(x => x == null);
        elapsed = float.IsNaN(elapsed) || float.IsInfinity(elapsed) ? 0f : Mathf.Max(0f, elapsed);
        state = float.IsNaN(state) || float.IsInfinity(state) ? 0f : Mathf.Clamp(state, 0f, 100f);
        processIndex = Mathf.Clamp(processIndex, 0, 4);
        nextBeatIndex = Mathf.Max(0, nextBeatIndex);
        resolvedBeatCount = Mathf.Max(0, resolvedBeatCount);
        initialBattery = Mathf.Clamp(initialBattery, 0, 100);
        if (processIndex == 0) active = false;
    }
}

[Serializable]
public sealed class OSHeartBeatResumeData
{
    public int chartIndex;
    public bool resolved, reportedMiss, holding, startedHold, holdFailed, reinforcedBroken;
    public float holdFailureTime, holdFailureWidth, nextHoldFeedback, reinforcedBreakTime, reinforcedResolvedTime, missShatterTime;
}

// Called by the save service on the main thread, after gameplay updates. Only logical
// state crosses sessions; windows/effects are reconstructed against the current scene.
public static class OSSessionPersistence
{
    public static bool IsRestoring { get; private set; }

    public static void Capture(GameSaveData data)
    {
        if (data == null || IsRestoring || GameManager.Instance?.Phase == GamePhase.Boot) return;
        var os = OS_Handle.Instance;
        if (os == null) return;
        if (data.osResume == null) data.osResume = new OSResumeSaveData();
        os.CaptureSessionState(data.osResume);
    }

    public static IEnumerator Restore(GameSaveData data)
    {
        if (data == null || OS_Handle.Instance == null) yield break;
        if (data.osResume == null) data.osResume = new OSResumeSaveData();
        data.osResume.EnsureData();
        // Fresh sessions keep the scene-authored initial presentation.
        if (!data.osResume.captured) yield break;
        var os = OS_Handle.Instance;
        IsRestoring = true;
        try { yield return os.RestoreSessionState(data.osResume); }
        finally { IsRestoring = false; }
    }
}

public sealed partial class OS_Handle
{
    internal void CaptureSessionState(OSResumeSaveData saved)
    {
        CaptureWindowLayouts();
        saved.EnsureData();
        saved.captured = true;
        saved.windows.Clear();
        foreach (var window in GetComponentsInChildren<OSWindowUnit>(true))
        {
            if (window == null || !window.IsOpen || string.IsNullOrEmpty(window.WindowId)) continue;
            // Settings is a temporary pause surface, often open when quitting.
            // Restoring it behind gameplay would silently pause story timers.
            if (window.GetComponent<OSOptionWindowController>() != null) continue;
            saved.windows.Add(new OSOpenWindowSaveData {
                windowId = window.WindowId, minimized = window.IsMinimized,
                siblingIndex = window.transform.GetSiblingIndex()
            });
        }
        saved.characterState = characterWindowController != null ? (int)characterWindowController.CurrentState : 0;
        saved.secretInProgress = _isFirstHeartHealEndingActive;
        saved.giftStarted = _hasMainStoryStarted;
        saved.giftLanded = IsMainStoryGiftLanded;
        saved.giftClosed = _hasMainStoryGiftWindowClosed;
        saved.chapterEntryConfirmed = _hasChapter01EntryConfirmed;
        saved.stateCheckMessageShown = _hasChapter01StateCheckMessageShown;
        saved.recordHoverMessageShown = _hasChapter01RecordHoverMessageShown;
        heartHealToolWindowController?.CapturePersistentTreatment(saved.treatment);
        stateCheckWindowController?.CapturePersistentBoard(saved);
    }

    internal IEnumerator RestoreSessionState(OSResumeSaveData saved)
    {
        ResolveReferences();
        var data = GameDataManager.Instance;
        if (data == null) yield break;
        // Older chapter-four saves could contain the retired process-4 tool.
        // Suppress only that legacy route before its deferred restore coroutine
        // can start; the prologue and all other tool saves remain untouched.
        bool finalHealOwnsScene = data.CurrentChapterIndex == 4
            && data.OSProcessData.memory.chapter04Story?.completed == true
            && data.OSProcessData.memory.chapter04Story.finalTreatmentStage != OSChapter04FinalTreatmentStage.None;
        if (finalHealOwnsScene && saved.treatment.processIndex == OSChapter04StoryDirector.FinalHeartHealProcessIndex)
            saved.treatment = new OSHeartTreatmentResumeData();
        Chapter04Entry?.ResetPresentation();
        RefreshChapter04TV();
        linkMessageController?.NotifyOSHidden();
        SetChapter01LiveIntroProtected(false);
        CancelFirstHeartHealEnding();
        CancelChapter01RecordGuidance();
        StopStateCheckRestartDelay();
        StopMainStoryGiftSequence(false);
        _desktopProcessLayoutInitialized = false;
        _chapter01KeyRuntimeInitialized = false;
        InitializeDesktopProcessNewStates();
        InitializeDesktopProcessLayouts();
        InitializeChapter01KeyRuntime();

        // A reconstructed window must not overwrite saved layout while opening.
        _isRestoringWindowLayouts = true;
        try
        {
            if (saved.captured)
                foreach (var window in GetComponentsInChildren<OSWindowUnit>(true))
                    if (window != null && window.IsOpen) window.CloseSilently();

            _hasMainStoryGiftWindowClosed = saved.giftClosed || data.IsRecordDataUnlocked(MainStoryGiftRecordIndex);
            _hasChapter01EntryConfirmed = saved.chapterEntryConfirmed || data.CurrentChapterIndex >= 1;
            _hasChapter01StateCheckMessageShown = saved.stateCheckMessageShown;
            _hasChapter01RecordHoverMessageShown = saved.recordHoverMessageShown;
            _hasMainStoryStarted = saved.giftStarted || _hasChapter01EntryConfirmed || _hasMainStoryGiftWindowClosed;
            _isMainStoryInitialized = _hasMainStoryStarted;
            if (_hasMainStoryGiftWindowClosed)
            {
                SetMainStoryGiftInteraction(false);
                if (mainStoryGiftDesktopProcess != null) mainStoryGiftDesktopProcess.gameObject.SetActive(false);
            }
            else if (saved.giftLanded) ApplyMainStoryGiftFinalState();
            else if (saved.giftStarted) PlayMainStoryGiftIntro();

            if (Enum.IsDefined(typeof(OSCharacterWindowState), saved.characterState))
                characterWindowController?.ApplyStateImmediate((OSCharacterWindowState)saved.characterState);
            RestoreWindowLayouts();
            var windows = GetComponentsInChildren<OSWindowUnit>(true);
            saved.windows.Sort((a, b) => {
                var wa = Array.Find(windows, x => x != null && x.WindowId == a.windowId);
                var wb = Array.Find(windows, x => x != null && x.WindowId == b.windowId);
                int depth = SessionWindowDepth(wa).CompareTo(SessionWindowDepth(wb));
                return depth != 0 ? depth : a.siblingIndex.CompareTo(b.siblingIndex);
            });
            foreach (var entry in saved.windows)
            {
                OSWindowUnit window = Array.Find(windows, x => x != null && x.WindowId == entry.windowId);
                if (window == null || window == linkMessageController?.LinkWindow
                    || window == firstHeartHealSpecialWindow || window == firstHeartHealSpecialWindow01) continue;
                // Also ignore settings windows recorded by older save versions.
                if (window.GetComponent<OSOptionWindowController>() != null) continue;
                if (finalHealOwnsScene && window == heartHealToolWindowController?.GetComponent<OSWindowUnit>()) continue;
                // Mainline progress owns this standalone window, even if the
                // last snapshot caught its intake/close transition or no window.
                if (window == cursorExperimentWindow) continue;
                // The gift checkpoint reconstructs its key and mandatory heart reveal.
                if (window == motherKeyController?.Window) continue;
                if (window == motherHeartController?.Window && (!IsMotherHeartUnlocked
                    || data.OSProcessData.memory.chapter04Story.motherGiftStage == OSChapter04MotherGiftStage.CloseRequested)) continue;
                if ((window == clawMachineGameWindow || window == clawMachineWindow)
                    && data.OSProcessData.memory.chapter02CursorExperimentRound > 0) continue;
                if (window == paperPushWindow && !data.IsPaperPushPlayable) continue;
                if (window == chapter01KeyWindow)
                {
                    // The key container is a temporary standalone prompt, not
                    // an app. Old saves must not resurrect it as a blank taskbar item.
                    if (!data.IsChapter01KeyXAcquired) OpenChapter01KeyWindow();
                    continue;
                }
                if (window == mainStoryGiftWindow)
                {
                    if (!_hasMainStoryGiftWindowClosed) { ApplyMainStoryGiftFinalState();OpenMainStoryGiftWindow(); }
                }
                else if (window.transform.parent != null && window.transform.parent.GetComponentInParent<OSWindowUnit>() != null)
                    window.OpenStandalone();
                else
                {
                    DesktopProcessUnit launcher = FindSessionDesktopProcess(window);
                    if (launcher != null) OpenWindow(window, launcher, window.WindowId, 0f);
                    else window.OpenStandalone();
                }
            }
            // Let opening callbacks restore their own content before reinstating a live chart.
            float openDeadline = Time.unscaledTime + 3f;
            do { yield return null; }
            while (Time.unscaledTime < openDeadline && Array.Exists(windows, x => x != null && x.IsOpen && x.IsOpening));
            foreach (var entry in saved.windows)
            {
                var window = Array.Find(windows, x => x != null && x.WindowId == entry.windowId);
                if (window != null && window != chapter01KeyWindow && window.IsOpen && entry.minimized)
                    window.Minimize();
            }
            RestoreWindowLayouts();
            stateCheckWindowController?.RestorePersistentBoard(saved);
            heartHealToolWindowController?.RestorePersistentTreatment(saved.treatment);
        }
        finally { _isRestoringWindowLayouts = false; }

        bool secret = saved.secretInProgress && data.CurrentChapterIndex == 0;
        if (secret && IsOSVisible)
        {
            // The short meta sequence restarts from its entrance; treatment settlement is
            // already in SaveData and is deliberately not called again here.
            data.OSCharacterInteractionState.ActivateSequence(null);
            SetDesktopProcessLocked(OSDesktopProcessKeys.LockHeart, true);
            BeginFirstHeartHealEnding();
            linkMessageController?.NotifyOSBecameVisible();
        }
        else if (IsOSVisible)
        {
            linkMessageController?.NotifyOSBecameVisible();
            // The reboot/Live reconnect is a presentation checkpoint, not a new chapter.
            if (data.CurrentChapterIndex == 0 && data.CurrentStateCheckProgress >= 2f
                && !_hasChapter01EntryConfirmed
                && data.OSCharacterInteractionState.activeSequenceId == OSCharacterInteractionIds.Chapter01LiveReconnect)
                ProtectChapter01LiveIntro();
            if (data.CurrentChapterIndex == 0 && data.CurrentStateCheckProgress >= 2f && !saved.giftStarted)
            {
                if (data.OSCharacterInteractionState.activeSequenceId == OSCharacterInteractionIds.Chapter01LiveReconnect)
                {
                    PrepareChapter01ReconnectState();
                    characterWindowController?.BeginChapter01Reconnect();
                }
                else osSfxAction?.PlayCloseAndReOpenPart();
            }
        }
        RefreshStateValues();
    }

    DesktopProcessUnit FindSessionDesktopProcess(OSWindowUnit window)
    {
        if (window == tvWindow) return tvDesktopProcess;
        if (window == memoryWindow) return memoryDesktopProcess;
        if (window == recordWindow) return recordDesktopProcess;
        if (window == toolWindow) return toolDesktopProcess;
        if (window == gramophoneWindow) return gramophoneDesktopProcess;
        if (window == energyLoopWindow) return energyLoopDesktopProcess;
        if (window == paperPushWindow) return paperPushDesktopProcess;
        if (window == folderWindow) return fileDesktopProcess;
        if (window == characterWindow) return characterDesktopProcess;
        if (window == heartHealToolWindow) return heartHealToolDesktopProcess;
        if (window == stateCheckWindow) return stateCheckDesktopProcess;
        if (motherHeartController != null && window == motherHeartController.Window) return lockHeartDesktopProcess;
        return null;
    }

    static int SessionWindowDepth(OSWindowUnit window)
    {
        int depth = 0;
        for (Transform parent = window != null ? window.transform.parent : null; parent != null; parent = parent.parent) depth++;
        return depth;
    }
}
