using System;
using UnityEngine;

public sealed partial class OSChapter04StoryDirector
{
    public const int FinalHeartHealProcessIndex = 4;
    OSChapter04FinalHealDirector finalTreatment;
    bool finalTreatmentRequestQueued;
    OSWindowUnit treatmentBackgroundLive, treatmentBackgroundTV;
    OSChapter04GriefPresentation griefPresentation;
    bool griefRestorePending = true;
    string griefEntryId;
    int griefVisit = -1;
    public const float FailureWarningDelay = 2f;

    public OSChapter04FinalTreatmentStage FinalTreatmentStage => State?.finalTreatmentStage
        ?? OSChapter04FinalTreatmentStage.None;
    public bool IsFinalTreatmentPending => FinalTreatmentStage == OSChapter04FinalTreatmentStage.Pending;
    public bool IsFinalTreatmentComplete => FinalTreatmentStage == OSChapter04FinalTreatmentStage.Completed
        || FinalTreatmentStage == OSChapter04FinalTreatmentStage.Ending;
    public bool IsWaitingForFailureWarning => FinalTreatmentStage == OSChapter04FinalTreatmentStage.WaitingForWarning;
    public bool IsFailureWarningPending => FinalTreatmentStage == OSChapter04FinalTreatmentStage.FailureWarning;
    public bool HasFinalTreatmentAccess => IsFinalTreatmentPending || FinalTreatmentStage == OSChapter04FinalTreatmentStage.Opened
        || FinalTreatmentStage == OSChapter04FinalTreatmentStage.FinalHeal;
    public OSChapter04FinalHealDirector FinalHeal => finalTreatment;
    public bool CanOpenFinalHeartHeal => IsActive && CommonAvailable && State?.completed == true
        && HasFinalTreatmentAccess;
    // The new grave section requests treatment only after the warning is confirmed.
    public event Action FinalTreatmentRequested;
    public event Action FinalTreatmentCompleted;

    void ResolveFinalTreatment()
    {
        var next = os != null ? os.GetComponent<OSChapter04FinalHealDirector>() : null;
        if (next == null && os != null) next = os.gameObject.AddComponent<OSChapter04FinalHealDirector>();
        if (next == finalTreatment) return;
        UnbindFinalTreatment();
        finalTreatment = next;
        if (finalTreatment != null)
        {
            finalTreatment.Healed += OnFinalTreatmentConfirmed;
            finalTreatment.EndingCompleted += OnFinalEndingCompleted;
        }
    }

    void UnbindFinalTreatment()
    {
        ReleaseFinalTreatmentBackground();
        if (finalTreatment != null)
        {
            finalTreatment.Healed -= OnFinalTreatmentConfirmed;
            finalTreatment.EndingCompleted -= OnFinalEndingCompleted;
            finalTreatment.Stop();
        }
        finalTreatment = null;
    }

    void QueueFinalTreatment()
    {
        if (State == null || State.finalTreatmentStage != OSChapter04FinalTreatmentStage.None
            && State.finalTreatmentStage != OSChapter04FinalTreatmentStage.FailureWarning) return;
        State.finalTreatmentStage = OSChapter04FinalTreatmentStage.Pending;
        State.griefTier = State.griefLineStep = 0;
        ClearGriefPresentation();
        finalTreatmentRequestQueued = true;
        // Start on the next Tick, after the warning's pointer callback ends.
        FinalTreatmentRequested?.Invoke();
    }

    void TickFinalTreatment()
    {
        // Covers a save captured from the completion/resource event before the
        // final text callback could enter its durable warning-delay phase.
        BeginFailureWarningDelay(Content.Find(State?.currentEntryId));
        TickFailureWarning();
        if (HasFinalTreatmentAccess && finalTreatment?.IsRunning != true) TryOpenFinalHeartHeal();
        else if (IsFinalTreatmentComplete && finalTreatment?.IsRunning != true && IsActive && CommonAvailable)
            StartFinalHeartHeal(true);
        bool visible = finalTreatment?.IsRunning == true;
        if (!visible) { ReleaseFinalTreatmentBackground(); return; }
        // Live and TV are normally pinned. Temporarily place them behind the
        // treatment without changing the player's saved pin preferences.
        SetTreatmentBackground(ref treatmentBackgroundLive, liveWindow);
        SetTreatmentBackground(ref treatmentBackgroundTV, os?.TVWindow);
    }

    void BeginFailureWarningDelay(OSChapter04StoryContent.Beat beat)
    {
        if (State == null || !State.currentTextCompleted || !State.currentCueCommitted
            || !State.currentResourcesCommitted || FinalTreatmentStage != OSChapter04FinalTreatmentStage.None
            || beat?.endAction != OSChapter04StoryContent.FailureWarningAction) return;
        State.completed = true;
        State.currentReadElapsed = 0f;
        State.finalTreatmentStage = OSChapter04FinalTreatmentStage.WaitingForWarning;
        ProgressChanged?.Invoke();
    }

    void TickFailureWarning()
    {
        if ((!IsWaitingForFailureWarning && !IsFailureWarningPending) || !IsAvailable) return;
        if (IsWaitingForFailureWarning)
        {
            State.currentReadElapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (State.currentReadElapsed < FailureWarningDelay) return;
            State.currentReadElapsed = FailureWarningDelay;
            State.finalTreatmentStage = OSChapter04FinalTreatmentStage.FailureWarning;
            var saved = State;
            ProgressChanged?.Invoke();
            if (ReferenceEquals(saved, State)) griefPresentation?.ShowFailureWarning(false);
        }
        else if (griefPresentation != null && !griefPresentation.IsFailureWarningVisible)
            griefPresentation.ShowFailureWarning(true);
    }

    void ApplyGriefForCurrentLine()
    {
        if (State == null) return;
        if (HasFinalTreatmentAccess || IsFinalTreatmentComplete)
        { ClearGriefPresentation(); return; }
        var beat = Content.Find(State.currentEntryId);
        int tier = IsFailureWarningPending || IsWaitingForFailureWarning ? 3 : beat?.griefTier ?? 0;
        int step = IsFailureWarningPending || IsWaitingForFailureWarning ? 3 : beat?.griefLineStep ?? 0;
        State.griefTier = tier;
        State.griefLineStep = step;
        if (griefPresentation == null && tier == 0)
        { griefRestorePending = false; return; }
        var next = OSChapter04GriefPresentation.Ensure();
        if (next != griefPresentation)
        {
            UnbindGriefPresentation();
            griefPresentation = next;
            if (griefPresentation != null) griefPresentation.FailureWarningConfirmed += OnFailureWarningConfirmed;
        }
        if (griefPresentation == null) return;
        if (griefRestorePending || griefEntryId != State.currentEntryId || griefVisit != State.currentVisit)
        {
            griefPresentation.SetGriefTier(tier, griefRestorePending && State.started, step);
            griefRestorePending = false;
            griefEntryId = State.currentEntryId;
            griefVisit = State.currentVisit;
        }
    }

    void OnFailureWarningConfirmed()
    {
        if (!IsActive || !CommonAvailable || !IsFailureWarningPending
            || !ReferenceEquals(observed, State) || OSSessionPersistence.IsRestoring) return;
        QueueFinalTreatment();
        ProgressChanged?.Invoke();
    }

    void ClearGriefPresentation()
    {
        var current = griefPresentation != null ? griefPresentation : OSChapter04GriefPresentation.Current;
        if (current != null && (current.Tier != 0 || current.IsFailureWarningVisible)) current.ClearAll();
        // Returning to this same save object after a chapter exit must restore
        // the tier even though neither the visit nor entry ID has changed.
        griefEntryId = null;
        griefVisit = -1;
        griefRestorePending = true;
    }

    void UnbindGriefPresentation()
    {
        if (griefPresentation != null) griefPresentation.FailureWarningConfirmed -= OnFailureWarningConfirmed;
        griefPresentation = null;
    }

    void SetTreatmentBackground(ref OSWindowUnit current, OSWindowUnit next)
    {
        if (current == next) return;
        if (current != null) current.SetPresentationBackground(this, false);
        current = next != null && next.SetPresentationBackground(this, true) ? next : null;
    }

    void ReleaseFinalTreatmentBackground()
    {
        SetTreatmentBackground(ref treatmentBackgroundLive, null);
        SetTreatmentBackground(ref treatmentBackgroundTV, null);
    }

    /// <summary>
    /// Pending treatment opens automatically; the chat can also expose this as
    /// an explicit reopen action. An already restored treatment is never reset.
    /// </summary>
    public bool TryOpenFinalHeartHeal()
    {
        if (!CanOpenFinalHeartHeal) return false;
        return StartFinalHeartHeal(false);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool DebugBeginMotherCheckpoint()
    {
        // A checkpoint owns the console input guard until preparation finishes.
        // Use the same startup internally, then let closing the console resume it.
        if (!IsActive || State?.completed != true || !StartFinalHeartHeal(false)) return false;
        return finalTreatment.DebugSkipToMother();
    }
#endif

    bool StartFinalHeartHeal(bool endingOnly)
    {
        if (MrTGame.Core.GameManager.Instance?.IsTransitioning == true
            || MrTGame.Core.GameManager.Instance?.Phase != MrTGame.Core.GamePhase.OSMain) return false;
        ResolveFinalTreatment();
        if (finalTreatment == null) return false;
        // Completed story text has no active message to reconstruct its own
        // shell. The treatment explicitly restores its grave/Live background.
        os.SetCharacterWindowState(OSCharacterWindowState.Anima, false);
        if (liveWindow?.IsOpen != true) os.OpenCharacterWindowForReconnect(0f);
        liveWindow?.CompleteOpeningFromScript();
        if (liveWindow?.IsMinimized == true) liveWindow.RestoreAndBringToFront();
        os.ResolveChapter04TVReferences();
        if (os.TVWindow == null) return false;
        if (!os.TVWindow.IsOpen) os.TVWindow.OpenStandalone();
        else if (os.TVWindow.IsMinimized) os.TVWindow.RestoreAndBringToFront();
        State.dialogueSuspended = false;
        if (!os.LinkMessageController.OpenChapter04DialogueShell(this)) return false;
        chatView = OSChapter04ChatView.Attach(this, os.LinkMessageController);
        if (finalTreatment.IsRunning) return true;
        ClearGriefPresentation();
        bool restoring = !finalTreatmentRequestQueued;
        if (!finalTreatment.Begin(this, restoring, endingOnly)) return false;
        finalTreatmentRequestQueued = false;
        if (!endingOnly)
        {
            State.finalTreatmentStage = OSChapter04FinalTreatmentStage.FinalHeal;
            State.finalHealSection = 1;
        }
        ProgressChanged?.Invoke();
        return true;
    }

    void OnFinalTreatmentConfirmed()
    {
        if (!IsActive || State?.completed != true || !ReferenceEquals(observed, State)
            || OSSessionPersistence.IsRestoring || FinalTreatmentStage != OSChapter04FinalTreatmentStage.Ending) return;
        ApplyLiveEmotionForCurrentLine();
        ProgressChanged?.Invoke();
        FinalTreatmentCompleted?.Invoke();
    }

    void OnFinalEndingCompleted()
    {
        ApplyLiveEmotionForCurrentLine();
        ProgressChanged?.Invoke();
    }
}
