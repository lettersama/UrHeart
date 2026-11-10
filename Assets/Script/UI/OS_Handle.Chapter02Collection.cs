using UnityEngine;
using UnityEngine.InputSystem;
using MrTGame.Narrative;

public sealed partial class OS_Handle
{
    const string Chapter02CollectionSequence = "chapter02.recovery";
    const string Chapter02CollectionObserveEntry = "chapter02_collecting_observe";
    const string Chapter02CollectionWaitEntry = "chapter02_collecting_wait";
    const string Chapter02CollectionNoticeEntry = "chapter02_collecting_notice";
    const string Chapter02CollectionBusyEntry = "chapter02_collecting_busy";
    const string Chapter02CollectionAppEntry = "chapter02_collecting_new_app";

    // Append-only save protocol. Timed gestures restart at their current beat
    // after a load; they never replay earlier dialogue or unlock the red cursor.
    enum CollectionIntroStep { Waiting, Observe, Roam, Notice, Busy, Assemble, Return, Offer, Complete }

    OSMemoryProcessSaveData chapter02CollectionMemory;
    OSChapter02CollectionIntroductionPresentation chapter02CollectionPresentation;
    OSWindowUnit chapter02CollectionSettings;
    float chapter02CollectionElapsed;
    double chapter02CollectionLastVisibleTime = -1d;
    Vector2 chapter02CollectionMouseBaseline;
    bool chapter02CollectionMouseArmed, chapter02ClawWindowRestored;

    CollectionIntroStep CollectionStep => (CollectionIntroStep)(
        GameDataManager.Instance?.OSProcessData.memory.chapter02CollectionIntroStep ?? 0);

    public bool IsChapter02CollectionChoiceProtected
    {
        get
        {
            var data = GameDataManager.Instance;
            if (data == null || data.CurrentChapterIndex != 2
                || data.CurrentOSMemoryDataIndex != OSMemoryData.Chapter02CollectionWaitingDataIndex
                || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true)
                return false;
            var step = CollectionStep;
            return step > CollectionIntroStep.Waiting && step < CollectionIntroStep.Complete;
        }
    }

    // Protect the quiet beats too: a desktop hint must not replace the invitation.
    // The final application line keeps its ordinary reading hold after discovery.
    public bool IsChapter02CollectionMessageProtected =>
        IsChapter02CollectionChoiceProtected
        || (GameDataManager.Instance?.CurrentChapterIndex == 2
            && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
            && linkMessageController != null
            && linkMessageController.DisplayedEntryId == Chapter02CollectionAppEntry
            && linkMessageController.LinkWindow != null
            && linkMessageController.LinkWindow.IsOpen);

    public static bool IsChapter02CollectionEntry(string entry) =>
        entry == Chapter02CollectionObserveEntry || entry == Chapter02CollectionWaitEntry
        || entry == Chapter02CollectionNoticeEntry || entry == Chapter02CollectionBusyEntry
        || entry == Chapter02CollectionAppEntry;

    public bool AllowsChapter02CollectionEntry(string sequence, string entry)
    {
        if (!IsChapter02CollectionMessageProtected) return true;
        return sequence == Chapter02CollectionSequence && entry == CollectionEntry(CollectionStep);
    }

    static string CollectionEntry(CollectionIntroStep step)
    {
        switch (step)
        {
            case CollectionIntroStep.Observe: return Chapter02CollectionObserveEntry;
            case CollectionIntroStep.Roam: return Chapter02CollectionWaitEntry;
            case CollectionIntroStep.Notice: return Chapter02CollectionNoticeEntry;
            case CollectionIntroStep.Busy: return Chapter02CollectionBusyEntry;
            case CollectionIntroStep.Offer:
            case CollectionIntroStep.Complete: return Chapter02CollectionAppEntry;
            default: return null;
        }
    }

    // Legacy saves already offered or accepted the old "wait" choice. Continue
    // from that receipt instead of making them watch the newly added lead-in.
    static CollectionIntroStep ResolveCollectionIntroStep(
        OSMemoryProcessSaveData memory, OSCharacterInteractionSaveData state)
    {
        if (memory.chapter02ClawMachineDiscovered || memory.chapter02ClawCursorCaptured
            || memory.chapter02CursorExperimentRound > 0 || memory.chapter02HeartTalkStep > 0
            || state.HasCompletedEntry(Chapter02CollectionAppEntry))
            return CollectionIntroStep.Complete;
        if (memory.chapter02CollectionIntroStep > 0)
            return (CollectionIntroStep)Mathf.Clamp(memory.chapter02CollectionIntroStep, 0, (int)CollectionIntroStep.Complete);
        if (!state.HasConsumedEntry(Chapter02CollectionObserveEntry))
        {
            if (state.HasCompletedEntry(Chapter02CollectionWaitEntry)
                || state.presentingEntryId == Chapter02CollectionAppEntry
                || state.pendingEntryId == Chapter02CollectionAppEntry)
                return CollectionIntroStep.Offer;
            if (state.HasConsumedEntry(Chapter02CollectionWaitEntry)
                || state.presentingEntryId == Chapter02CollectionWaitEntry
                || state.pendingEntryId == Chapter02CollectionWaitEntry)
                return CollectionIntroStep.Roam;
        }
        return CollectionIntroStep.Waiting;
    }

    void RefreshChapter02CollectionMessageLayering()
    {
        if (!IsOSVisible || !IsChapter02CollectionMessageProtected) return;
        OSWindowUnit liveRoot = ResolveTopLevelWindow(characterWindow);
        if (IsWindowMouseFocusable(liveRoot)) liveRoot.transform.SetAsLastSibling();
        OSWindowUnit message = linkMessageController?.LinkWindow;
        if (message == null || !message.IsOpen || message.IsMinimized) return;
        OSWindowUnit messageRoot = ResolveTopLevelWindow(message);
        if (messageRoot != liveRoot && IsWindowMouseFocusable(messageRoot))
            messageRoot.transform.SetAsLastSibling();
        else if (message != messageRoot)
            message.transform.SetAsLastSibling();
    }

    void RefreshChapter02CollectionIntroduction()
    {
        if (!IsSingletonInstance) return;
        var data = GameDataManager.Instance;
        var memory = data != null && data.CurrentChapterIndex == 2 ? data.OSProcessData.memory : null;
        if (!ReferenceEquals(chapter02CollectionMemory, memory))
        {
            ResetChapter02CollectionIntroduction();
            chapter02CollectionMemory = memory;
            chapter02ClawWindowRestored = false;
            ResetChapter02ClawMachinePresentation();
            if (memory != null)
                memory.chapter02CollectionIntroStep = (int)ResolveCollectionIntroStep(memory, data.OSCharacterInteractionState);
            RefreshEvent01LinkCloseSuppression();
        }
        if (memory == null) return;

        if (memory.chapter02ClawMachineDiscovered && !chapter02ClawWindowRestored
            && IsOSVisible && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true)
        {
            chapter02ClawWindowRestored = true;
            OpenChapter02ClawMachineWindow(false);
        }

        var state = data.OSCharacterInteractionState;
        if (CollectionStep == CollectionIntroStep.Complete
            || data.CurrentOSMemoryDataIndex != OSMemoryData.Chapter02CollectionWaitingDataIndex
            || state.activeSequenceId != Chapter02CollectionSequence
            || !state.HasCompletedEntry("chapter02_return_reaction"))
        {
            SuspendChapter02CollectionIntroduction();
            return;
        }

        if (chapter02CollectionSettings == null)
            chapter02CollectionSettings = transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        bool visible = Application.isFocused && IsOSVisible && !OSSessionPersistence.IsRestoring
            && GameSessionFlow.Instance?.IsBusy != true && !GameTestConsole.BlocksGameplayInput
            && !IsSystemInputLocked && linkMessageController != null && !linkMessageController.GlobalMessagesSuppressed
            && memoryWindow != null && memoryWindow.IsOpen && !memoryWindow.IsMinimized
            && !memoryWindow.IsOpening && !memoryWindow.IsClosing && memoryWindow.gameObject.activeInHierarchy
            && !(chapter02CollectionSettings != null && chapter02CollectionSettings.IsOpen
                && !chapter02CollectionSettings.IsMinimized && chapter02CollectionSettings.gameObject.activeInHierarchy)
            && Chapter01Ending?.IsChapter02LiveDisconnected != true
            && Chapter01Ending?.IsChapter02ReturnInProgress != true;
        if (!visible)
        {
            SuspendChapter02CollectionIntroduction();
            return;
        }
        if (characterWindow == null || characterWindow.IsClosing)
        {
            SuspendChapter02CollectionIntroduction();
            return;
        }
        if (!characterWindow.IsOpen || characterWindow.IsMinimized)
        {
            SuspendChapter02CollectionIntroduction();
            OpenCharacterWindowForReconnect(.15f);
            return;
        }
        if (characterWindow.IsOpening)
        {
            SuspendChapter02CollectionIntroduction();
            return;
        }

        linkMessageController.SetChapter02CollectionPaused(false);
        double now = Time.realtimeSinceStartupAsDouble;
        float dt = chapter02CollectionLastVisibleTime < 0d ? 0f
            : Mathf.Clamp((float)(now - chapter02CollectionLastVisibleTime), 0f, .05f);
        chapter02CollectionLastVisibleTime = now;
        var step = CollectionStep;
        if (step == CollectionIntroStep.Waiting)
        {
            // Finish any previous mandatory line before reserving the message slot.
            if (linkMessageController.IsPresenting || linkMessageController.IsWaitingForOption
                || linkMessageController.IsWaitingForExternalCompletion
                || !string.IsNullOrEmpty(state.pendingGlobalEntryId)) return;
            var previousMessage = linkMessageController.LinkWindow;
            if (previousMessage != null && (previousMessage.IsClosing
                || (previousMessage.IsOpen && !string.IsNullOrEmpty(linkMessageController.DisplayedEntryId)))) return;
            chapter02CollectionElapsed += dt;
            if (chapter02CollectionElapsed >= 2f) SetCollectionStep(CollectionIntroStep.Observe);
            return;
        }

        if (step == CollectionIntroStep.Observe)
        {
            if (CollectionLineFinished(Chapter02CollectionObserveEntry)) SetCollectionStep(CollectionIntroStep.Roam);
            return;
        }
        if (step == CollectionIntroStep.Roam)
        {
            if (!PresentCollectionBeat(OSChapter02CollectionIntroductionPresentation.Phase.Roam, dt)) return;
            if (chapter02CollectionElapsed < 1.4f || !CollectionLineFinished(Chapter02CollectionWaitEntry)
                || chapter02CollectionElapsed < 3.4f) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 position = mouse.position.ReadValue();
            if (!chapter02CollectionMouseArmed)
            {
                chapter02CollectionMouseBaseline = position;
                chapter02CollectionMouseArmed = true;
                return;
            }
            if ((position - chapter02CollectionMouseBaseline).sqrMagnitude < 16f) return;
            SetCollectionStep(CollectionIntroStep.Notice);
            chapter02CollectionPresentation.Present(OSChapter02CollectionIntroductionPresentation.Phase.WatchPlayer, 0f);
            return;
        }
        if (step == CollectionIntroStep.Notice || step == CollectionIntroStep.Busy)
        {
            if (!PresentCollectionBeat(OSChapter02CollectionIntroductionPresentation.Phase.WatchPlayer, dt)) return;
            if (CollectionLineFinished(CollectionEntry(step)))
                SetCollectionStep(step == CollectionIntroStep.Notice ? CollectionIntroStep.Busy : CollectionIntroStep.Assemble);
            return;
        }
        if (step == CollectionIntroStep.Assemble || step == CollectionIntroStep.Return)
        {
            var phase = step == CollectionIntroStep.Assemble
                ? OSChapter02CollectionIntroductionPresentation.Phase.Assemble
                : OSChapter02CollectionIntroductionPresentation.Phase.Return;
            if (!PresentCollectionBeat(phase, dt) || !chapter02CollectionPresentation.IsComplete) return;
            SetCollectionStep(step == CollectionIntroStep.Assemble ? CollectionIntroStep.Return : CollectionIntroStep.Offer);
            return;
        }
        if (step == CollectionIntroStep.Offer)
        {
            chapter02CollectionPresentation?.ResetPresentation();
            CollectionLineFinished(Chapter02CollectionAppEntry);
        }
    }

    bool CollectionLineFinished(string entry)
    {
        var state = GameDataManager.Instance.OSCharacterInteractionState;
        var message = linkMessageController.LinkWindow;
        bool onScreen = linkMessageController.DisplayedEntryId == entry
            && message != null && (message.IsOpen || message.IsClosing);
        // Completion is recorded at the end of typing; wait for the reading hold.
        if (state.HasCompletedEntry(entry)) return !onScreen;
        if (state.presentingEntryId == entry || state.pendingEntryId == entry)
        {
            if (!linkMessageController.IsPresenting && !onScreen)
                linkMessageController.NotifyOSBecameVisible();
            return false;
        }
        if (linkMessageController.IsPresenting || linkMessageController.IsWaitingForOption
            || linkMessageController.IsWaitingForExternalCompletion) return false;
        if (state.HasConsumedEntry(entry)) state.SetEntryConsumed(entry, false);
        linkMessageController.TryPresentEntry(entry);
        return false;
    }

    bool PresentCollectionBeat(OSChapter02CollectionIntroductionPresentation.Phase phase, float dt)
    {
        if (chapter02CollectionPresentation == null)
            chapter02CollectionPresentation = GetComponent<OSChapter02CollectionIntroductionPresentation>()
                ?? gameObject.AddComponent<OSChapter02CollectionIntroductionPresentation>();
        chapter02CollectionPresentation.SetVisible(true);
        float elapsed = chapter02CollectionElapsed + dt;
        if (!chapter02CollectionPresentation.Present(phase, elapsed)) return false;
        chapter02CollectionElapsed = elapsed;
        return true;
    }

    void SetCollectionStep(CollectionIntroStep step)
    {
        GameDataManager.Instance.OSProcessData.memory.chapter02CollectionIntroStep = (int)step;
        chapter02CollectionElapsed = 0f;
        chapter02CollectionMouseArmed = false;
        RefreshEvent01LinkCloseSuppression();
    }

    void SuspendChapter02CollectionIntroduction()
    {
        chapter02CollectionLastVisibleTime = -1d;
        chapter02CollectionMouseArmed = false;
        chapter02CollectionPresentation?.SetVisible(false);
        // The application line uses the normal message lifetime after discovery.
        if (CollectionStep != CollectionIntroStep.Complete)
            linkMessageController?.SetChapter02CollectionPaused(true);
    }

    void OnApplicationFocus(bool focused)
    {
        // Focus can change while background updates are disabled, so the next
        // pointer coordinate must establish a new baseline in either direction.
        chapter02CollectionMouseArmed = false;
        chapter02CollectionLastVisibleTime = -1d;
        if (!focused) SuspendChapter02CollectionIntroduction();
    }

    void OnApplicationPause(bool paused)
    {
        chapter02CollectionMouseArmed = false;
        chapter02CollectionLastVisibleTime = -1d;
        if (paused) SuspendChapter02CollectionIntroduction();
    }

    void ResetChapter02CollectionIntroduction()
    {
        chapter02CollectionPresentation?.ResetPresentation();
        linkMessageController?.SetChapter02CollectionPaused(false);
        chapter02CollectionMemory = null;
        chapter02CollectionElapsed = 0f;
        chapter02CollectionLastVisibleTime = -1d;
        chapter02CollectionMouseArmed = false;
    }

    void HandleChapter02CollectionPresentationStarted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (sequenceId != Chapter02CollectionSequence || entry == null
            || entry.EntryId != Chapter02CollectionAppEntry) return;
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 2) return;
        bool firstDiscovery = !data.OSProcessData.memory.chapter02ClawMachineDiscovered;
        data.OSProcessData.memory.chapter02ClawMachineDiscovered = true;
        SetCollectionStep(CollectionIntroStep.Complete);
        chapter02CollectionPresentation?.ResetPresentation();
        chapter02ClawWindowRestored = true;
        OpenChapter02ClawMachineWindow(firstDiscovery);
    }
}
