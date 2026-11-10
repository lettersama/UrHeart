using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed partial class OSLinkMessageController : MonoBehaviour, IPointerClickHandler
{
    const string GlobalPresentationSequenceId =
        OSCharacterInteractionIds.GlobalSequence;

    [Header("References")]
    [SerializeField] OSWindowUnit linkWindow;
    [SerializeField] OSLinkMessageWindowLayout windowLayout;
    [SerializeField] GameObject textPart;
    [SerializeField] GameObject textWithOptionsPart;
    [SerializeField] GameObject imagePart;
    [SerializeField] TMP_Text textLabel;
    [SerializeField] TMP_Text textWithOptionsLabel;
    [SerializeField] RectTransform buttonArea;
    [SerializeField]
    OSLinkMessageSelectUnit[] optionUnits =
        new OSLinkMessageSelectUnit[0];
    [SerializeField] OSWindowUnit ownerCharacterWindow;
    [SerializeField]
    OSCharacterWindowController characterWindowController;

    [Header("Text Playback")]
    [SerializeField, Min(1f)] float charactersPerSecond = 40f;
    [SerializeField, Min(0f)] float minimumTextDuration = 0.1f;
    [SerializeField, Min(0f)] float partReopenDelay = 0.08f;

    [Header("Live Reconnect")]
    [SerializeField, Min(0f)] float reconnectOpenDuration = 0.4f;
    [SerializeField, Min(0f)] float reconnectAnimationDelay = 0.1f;

    readonly Dictionary<
        OSLinkMessageSelectUnit,
        OSCharacterInteractionOptionData> activeOptions =
            new Dictionary<
                OSLinkMessageSelectUnit,
                OSCharacterInteractionOptionData>();

    Tween textTween;
    Tween reopenTween;
    Tween pendingSequenceTween;
    Tween pendingGlobalTween;
    Tween reconnectAnimationTween;
    Tween trailingDotsTween;
    Tween autoCloseTween;
    TMP_Text trailingDotsLabel;
    string trailingDotsOriginalText;
    OSCharacterInteractionSequenceData activeSequence;
    OSCharacterInteractionEntryData activeEntry;
    OSCharacterInteractionEntryData reconnectEntry;
    string activePresentationEntryId;
    string displayedSequenceId;
    string displayedEntryId;
    int presentationVersion;
    bool activePresentationIsGlobal;
    bool displayedEntryIsGlobal;
    bool hasDisplayedMessage;
    bool activeTextCompleted;
    bool isWaitingForOption;
    bool isOSVisible;
    bool suppressLinkWindowClosedCallback;
    bool suppressOwnerWindowClosedNotification;
    bool globalMessagesSuppressed;
    bool resumePresentationAfterOwnerRestore;
    bool restartAutoCloseAfterRestore;
    float preservedAutoCloseElapsed;
    bool collectionReadingPaused;

    // The visible reading hold owns this message even after its text-complete
    // event has been delivered. Automatic arrivals must wait for this clock.
    public bool IsReadingHoldActive => hasDisplayedMessage &&
        ((autoCloseTween != null && autoCloseTween.IsActive()) || restartAutoCloseAfterRestore);
    public float ReadingHoldRemaining => autoCloseTween != null && autoCloseTween.IsActive()
        ? Mathf.Max(0f, autoCloseTween.Duration(false) - autoCloseTween.Elapsed(false))
        : restartAutoCloseAfterRestore && TryGetDisplayedEntry(out _, out var heldEntry)
            ? Mathf.Max(0f, heldEntry.AutoCloseDelay - preservedAutoCloseElapsed) : 0f;

    public bool IsPresenting =>
        !string.IsNullOrEmpty(activePresentationEntryId);
    public bool IsWaitingForOption => isWaitingForOption;
    public bool IsDisplayedTextComplete => IsPresenting && activeTextCompleted;
    public string DisplayedSequenceId => displayedSequenceId;
    public string DisplayedEntryId => displayedEntryId;
    public OSWindowUnit LinkWindow => linkWindow;
    public OSLinkMessageWindowLayout WindowLayout => windowLayout;
    public TMP_Text DisplayedTextLabel => textLabel;
    public bool IsWaitingForExternalCompletion =>
        TryGetActiveSequence(out _, out var saveState, out var sequence)
        && sequence.TryGetEntry(saveState.presentingEntryId, out var entry)
        && entry.WaitForExternalCompletion;
    public bool GlobalMessagesSuppressed => globalMessagesSuppressed;
    bool AreGlobalMessagesSuppressed => globalMessagesSuppressed
        || OS_Handle.Instance?.IsCursorExperimentInteriorBlockingMessages == true
        || OS_Handle.Instance?.IsChapter02CollectionMessageProtected == true
        || OS_Handle.Instance?.IsClawMachineMessageProtected == true
        || OS_Handle.Instance?.IsCursorExperimentMessageProtected == true
        || OS_Handle.Instance?.IsChapter02FinaleMessageProtected == true
        || OS_Handle.Instance?.IsChapter02PaperPushMessageProtected == true
        || OS_Handle.Instance?.IsChapter04EntryMessageProtected == true
        || OS_Handle.Instance?.IsChapter03EntryMessageProtected == true
            || OS_Handle.Instance?.IsChapter03StoryMessageProtected == true
        || OS_Handle.Instance?.IsChapter02FinalTalkMessageProtected == true
        || OS_Handle.Instance?.Chapter01Ending?.IsChapter02LiveDisconnected == true
        || OS_Handle.Instance?.Chapter01Ending?.IsChapter02ReturnInProgress == true
        || HasPendingFirstTreatmentTutorial(GetSaveState())
        || TryGetProtectedPresentation(out _, out _);

    public event Action<
        string,
        OSCharacterInteractionEntryData> EntryPresentationStarted;
    public event Action<
        string,
        OSCharacterInteractionEntryData> EntryPresentationCompleted;
    public event Action<
        string,
        OSCharacterInteractionEntryData> EntryTextPlaybackCompleted;
    public event Action<
        string,
        OSCharacterInteractionEntryData,
        OSCharacterInteractionOptionData> OptionSelected;

    void Awake()
    {
        ResolveReferences();
        SanitizeSettings();
        BindWindowEvents();
    }

    void OnEnable()
    {
        ResolveReferences();
        ApplyLocalizedFonts();
        BindWindowEvents();

        if (restartAutoCloseAfterRestore)
        {
            restartAutoCloseAfterRestore = false;
            if (isOSVisible && linkWindow != null && linkWindow.IsOpen
                && hasDisplayedMessage && !IsPresenting
                && TryGetDisplayedEntry(out _, out var entry))
            {
                ScheduleAutoClose(entry, presentationVersion, preservedAutoCloseElapsed);
            }
        }

        if (!resumePresentationAfterOwnerRestore)
            return;

        resumePresentationAfterOwnerRestore = false;
        ResumePresentationAfterOwnerRestore();
    }

    void OnDisable()
    {
        PreserveNextEntryCountdown();
        // Keep the elapsed reading time across hierarchy deactivation.
        preservedAutoCloseElapsed = autoCloseTween != null && autoCloseTween.IsActive()
            ? autoCloseTween.Elapsed(false) : 0f;
        restartAutoCloseAfterRestore = isOSVisible && hasDisplayedMessage && !IsPresenting
            && linkWindow != null && linkWindow.IsOpen
            && TryGetDisplayedEntry(out _, out var entry) && entry.AutoCloseDelay > 0f;
        ReleaseFrontLifts();
        if (IsSuspendedByMinimize())
        {
            // A completed option must survive the parent's temporary
            // hierarchy deactivation. In-progress text restarts on restore.
            if (isWaitingForOption)
            {
                KillPresentationTweens();
                return;
            }

            resumePresentationAfterOwnerRestore = IsPresenting;
        }
        else
        {
            resumePresentationAfterOwnerRestore = false;
        }

        presentationVersion++;
        ClearActivePresentationRuntime();
        KillPresentationTweens();
    }

    void OnDestroy()
    {
        UnbindWindowEvents();
        KillPresentationTweens();
        KillPendingSequenceTween();
        KillPendingGlobalTween();
        KillReconnectAnimationTween();
    }

    void Update()
    {
        TickChapter03Text();
        TickChapter04Text();
        RefreshReadingHoldPause();
        MaintainCursorExperimentMessageReservation();
        var chapterData = GameDataManager.Instance;
        if (displayedEntryIsGlobal && hasDisplayedMessage && chapterData?.OSCharacterInteractionData != null
            && chapterData.OSCharacterInteractionData.TryGetGlobalEntry(displayedEntryId, out var displayedGlobal)
            && !MatchesChapter(displayedGlobal, chapterData))
        {
            InterruptActiveSavePresentation();StopVisualPresentation(true);CloseLinkWindowWithoutCallback();
            return;
        }
        TickOptionCountdown();
        TickWaitLine();
    }

    public bool ActivateSequence(string sequenceId)
    {
        if (TryGetReadAlongProtectedQuestion(out _) || TryGetCountdownQuestion(out _, out _)) return false;
        if (TryGetCursorExperimentReservedEntry(out _)) return false;
        if (OS_Handle.Instance?.IsClawMachineMessageProtected == true
            || OS_Handle.Instance?.IsCursorExperimentMessageProtected == true) return false;
        if (OS_Handle.Instance?.IsChapter02CollectionMessageProtected == true
            && GetSaveState()?.activeSequenceId != sequenceId) return false;
        ResolveReferences();
        if (!TryResolveSequence(
                sequenceId,
                out _,
                out OSCharacterInteractionSequenceData sequence))
        {
            Debug.LogWarning(
                $"[OSLinkMessage] Sequence '{sequenceId}' was not found.",
                this);
            return false;
        }

        OSCharacterInteractionSaveData saveState = GetSaveState();
        if (saveState == null)
            return false;

        if (saveState.activeSequenceId != sequence.SequenceId && activeEntry != null && activeEntry.SelectTimeoutOptionOnInterrupt)
            InterruptActiveSavePresentation();
        bool changed = saveState.ActivateSequence(sequence.SequenceId);
        if (changed)
        {
            KillPendingSequenceTween();
            StopVisualPresentation(true);
            CloseLinkWindowWithoutCallback();
        }

        SyncEvent01TutorialSuppression();
        if (isOSVisible)
            NotifyOSBecameVisible();
        return true;
    }

    public void NotifyOSBecameVisible()
    {
        isOSVisible = true;
        if (GameSessionFlow.Instance?.IsBusy == true) return;
        ResolveReferences();
        BindWindowEvents();
        SyncEvent01TutorialSuppression();

        OSCharacterInteractionSaveData saveState = GetSaveState();
        if (saveState == null)
            return;

        saveState.EnsureData();
        MaintainCursorExperimentMessageReservation();
        if (TryGetCursorExperimentReservedEntry(out _))
        {
            // Only the current interrogation line may resume after its Live
            // entrance. An older desktop sequence must not take this slot.
            TryResumeOrScheduleGlobal(saveState);
            return;
        }
        if (OS_Handle.Instance?.IsEvent01HeartHealTutorialSuppressed == true
            && RestoreInterruptedFirstTreatmentTutorial())
        {
            return;
        }
        if (!AreGlobalMessagesSuppressed
            && TryResumeOrScheduleGlobal(saveState))
            return;

        if (!TryGetActiveSequence(
                out GameDataManager dataManager,
                out saveState,
                out OSCharacterInteractionSequenceData sequence))
        {
            return;
        }

        if (saveState.phase == OSCharacterInteractionSavePhase.Presenting
            && !string.IsNullOrEmpty(saveState.presentingEntryId))
        {
            if (IsPresenting
                && !activePresentationIsGlobal
                && string.Equals(
                    activePresentationEntryId,
                    saveState.presentingEntryId,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (sequence.TryGetEntry(
                    saveState.presentingEntryId,
                    out OSCharacterInteractionEntryData resumedEntry)
                && MatchesConditions(resumedEntry, dataManager, saveState))
            {
                PresentSequenceEntry(sequence, resumedEntry, true);
                return;
            }

            saveState.CancelPresentation(false);
        }

        if (!string.IsNullOrEmpty(saveState.pendingEntryId)
            && sequence.TryGetEntry(
                saveState.pendingEntryId,
                out OSCharacterInteractionEntryData pendingEntry)
            && CanPresentSequenceEntry(
                pendingEntry,
                dataManager,
                saveState))
        {
            ScheduleOrPresentSequenceEntry(sequence, pendingEntry);
            return;
        }

        TryTrigger(
            OSCharacterInteractionTriggerType.OSBecameVisible,
            null);
    }

    public void NotifyOSHidden()
    {
        isOSVisible = false;
        resumePresentationAfterOwnerRestore = false;
        KillPendingSequenceTween();
        KillPendingGlobalTween();
        KillReconnectAnimationTween();
        StopVisualPresentation(true);
        CloseLinkWindowWithoutCallback();
    }

    public void SetGlobalMessagesSuppressed(bool suppressed)
    {
        if (globalMessagesSuppressed == suppressed)
            return;

        globalMessagesSuppressed = suppressed;
        if (suppressed)
        {
            KillPendingGlobalTween();
            KillReconnectAnimationTween();
            if (activePresentationIsGlobal)
            {
                StopVisualPresentation(true);
                CloseLinkWindowWithoutCallback();
            }

            return;
        }

        if (isOSVisible)
        {
            OSCharacterInteractionSaveData saveState = GetSaveState();
            if (saveState != null)
                TryResumeOrScheduleGlobal(saveState);
        }
    }

    public void CloseOwnerWindowSilently()
    {
        if (OS_Handle.Instance?.IsChapter02CollectionChoiceProtected == true
            || OS_Handle.Instance?.IsCursorExperimentStoryProtected == true
            || OS_Handle.Instance?.IsChapter02FinaleStoryProtected == true
            || OS_Handle.Instance?.IsChapter02PaperPushStoryProtected == true
            || OS_Handle.Instance?.IsChapter04EntryStoryProtected == true
            || OS_Handle.Instance?.IsChapter03EntryStoryProtected == true
                || OS_Handle.Instance?.IsChapter03StoryProtected == true
            || OS_Handle.Instance?.IsChapter02FinalTalkStoryProtected == true)
            return;
        ResolveReferences();
        suppressOwnerWindowClosedNotification = true;
        try
        {
            InterruptActiveSavePresentation();
            StopVisualPresentation(true);
            CloseLinkWindowWithoutCallback();
            if (ownerCharacterWindow != null
                && ownerCharacterWindow.IsOpen)
            {
                ownerCharacterWindow.Close();
            }
        }
        finally
        {
            suppressOwnerWindowClosedNotification = false;
        }
    }

    public bool NotifyDesktopProcessPointerEnter(string processKey)
    {
        return TryTrigger(
            OSCharacterInteractionTriggerType.DesktopProcessPointerEnter,
            processKey);
    }

    public void NotifyDesktopProcessClicked(string processKey)
    {
        TryTrigger(
            OSCharacterInteractionTriggerType.DesktopProcessClicked,
            processKey);
    }

    public void NotifyWindowOpened(string windowId)
    {
        if (!IsPresenting && (TryGetReadAlongProtectedQuestion(out _) || TryGetCountdownQuestion(out _, out _))
            && ((ownerCharacterWindow != null && windowId == ownerCharacterWindow.WindowId)
                || (linkWindow != null && windowId == linkWindow.WindowId)))
        {
            NotifyOSBecameVisible();
            return;
        }
        if (ownerCharacterWindow != null
            && windowId == ownerCharacterWindow.WindowId
            && !IsPresenting
            && TryGetProtectedPresentation(out _, out _))
        {
            NotifyOSBecameVisible();
            return;
        }

        TryTrigger(
            OSCharacterInteractionTriggerType.WindowOpened,
            windowId);
    }

    public void NotifyWindowClosed(string windowId)
    {
        ResolveReferences();
        bool closedVoidCharacterWindow = ownerCharacterWindow != null
            && characterWindowController != null
            && characterWindowController.CurrentState ==
                OSCharacterWindowState.VoidMessage
            && string.Equals(
                windowId,
                ownerCharacterWindow.WindowId,
                StringComparison.Ordinal);
        if (closedVoidCharacterWindow)
            return;

        TryQueueGlobalTrigger(
            OSCharacterInteractionTriggerType.WindowClosed,
            windowId);
    }

    public void NotifyExternalSignal(string signalId)
    {
        TryNotifyExternalSignal(signalId);
    }

    public bool TryNotifyExternalSignal(string signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return false;

        return TryTrigger(
            OSCharacterInteractionTriggerType.ExternalSignal,
            signalId);
    }

    public bool TryPresentEntry(string entryId)
    {
        if (TryGetReadAlongProtectedQuestion(out _) && !TryGetCountdownQuestion(out _, out _)) return false;
        if (OS_Handle.Instance?.AllowsChapter02CollectionEntry(GetSaveState()?.activeSequenceId, entryId) == false) return false;
        if (OS_Handle.Instance?.IsClawMachineMessageProtected == true
            || OS_Handle.Instance?.IsCursorExperimentMessageProtected == true)
            return false;
        if (!isOSVisible
            || !TryGetActiveSequence(out GameDataManager dataManager,
                out OSCharacterInteractionSaveData saveState,
                out OSCharacterInteractionSequenceData sequence)
            || !sequence.TryGetEntry(entryId, out OSCharacterInteractionEntryData entry)
            || !CanPresentSequenceEntry(entry, dataManager, saveState))
        {
            return false;
        }

        ScheduleOrPresentSequenceEntry(sequence, entry);
        return true;
    }

    public bool RestoreInterruptedFirstTreatmentTutorial()
    {
        if (!isOSVisible
            || !TryGetActiveSequence(out var dataManager, out var saveState, out var sequence)
            || sequence.SequenceId != OSCharacterInteractionIds.PrologueEvent01HeartHeal
            || dataManager.CurrentHeartHealProcessIndex != 1
            || saveState.HasCompletedEntry(OSCharacterInteractionIds.Event01Entries.HeartHealStartAfterShock)
            || saveState.HasCompletedEntry(OSCharacterInteractionIds.Event01Entries.HeartHealShockAfterStart)
            || IsFirstTreatmentTutorialEntry(saveState.pendingEntryId))
        {
            return false;
        }

        // Older saves can contain a consumed choice that a desktop hint replaced
        // before it was answered. Resume that choice without resetting the tutorial.
        foreach (var entry in sequence.Entries)
        {
            if (entry == null
                || !IsFirstTreatmentTutorialEntry(entry.EntryId)
                || !saveState.HasConsumedEntry(entry.EntryId)
                || saveState.HasCompletedEntry(entry.EntryId)
                || !MatchesConditions(entry, dataManager, saveState))
            {
                continue;
            }

            if (IsPresenting && !activePresentationIsGlobal
                && activePresentationEntryId == entry.EntryId
                && linkWindow != null && linkWindow.IsOpen)
            {
                return false;
            }

            KillPendingSequenceTween();
            PresentSequenceEntry(sequence, entry, false);
            return true;
        }

        return false;
    }

    static bool HasPendingFirstTreatmentTutorial(OSCharacterInteractionSaveData saveState)
    {
        return saveState != null
            && saveState.activeSequenceId == OSCharacterInteractionIds.PrologueEvent01HeartHeal
            && (IsFirstTreatmentTutorialEntry(saveState.presentingEntryId)
                || IsFirstTreatmentTutorialEntry(saveState.pendingEntryId));
    }

    static bool IsFirstTreatmentTutorialEntry(string entryId)
    {
        return entryId == OSCharacterInteractionIds.Event01Entries.HeartHealRecallChoice
            || entryId == OSCharacterInteractionIds.Event01Entries.HeartHealShockFirst
            || entryId == OSCharacterInteractionIds.Event01Entries.HeartHealStartAfterShock
            || entryId == OSCharacterInteractionIds.Event01Entries.HeartHealStartFirst
            || entryId == OSCharacterInteractionIds.Event01Entries.HeartHealShockAfterStart;
    }

    public bool IsAwaitingExternalCompletion(string sequenceId, string entryId)
    {
        OSCharacterInteractionSaveData saveState = GetSaveState();
        return saveState != null
            && saveState.activeSequenceId == sequenceId
            && saveState.presentingEntryId == entryId
            && TryResolveSequence(sequenceId, out _, out var sequence)
            && sequence.TryGetEntry(entryId, out var entry)
            && entry.WaitForExternalCompletion;
    }

    public bool TryCompleteAwaitedEntry(string sequenceId, string entryId)
    {
        if (!IsAwaitingExternalCompletion(sequenceId, entryId)
            || !TryResolveSequence(sequenceId, out _, out var sequence)
            || !sequence.TryGetEntry(entryId, out var entry))
        {
            return false;
        }

        // The owner may be closed, so complete the saved presentation even
        // when there is no active text animation or visible message.
        StopVisualPresentation(true);
        CloseLinkWindowWithoutCallback();
        OSCharacterInteractionSaveData saveState = GetSaveState();
        saveState.CompletePresentation(entryId, !entry.Repeatable);
        EntryPresentationCompleted?.Invoke(sequenceId, entry);
        UpdateSequenceCompletion(sequence, saveState);
        return true;
    }

    public bool NotifyGlobalExternalSignal(string signalId)
    {
        return TryQueueGlobalTrigger(
            OSCharacterInteractionTriggerType.ExternalSignal,
            signalId);
    }

    public void SetSequenceMessageConsumed(
        string sequenceId,
        string entryId,
        bool consumed)
    {
        OSCharacterInteractionSaveData saveState = GetSaveState();
        if (saveState == null
            || !string.Equals(
                saveState.activeSequenceId,
                sequenceId,
                StringComparison.Ordinal))
        {
            return;
        }

        saveState.SetEntryConsumed(entryId, consumed);
        SyncEvent01TutorialSuppression();
    }

    public void SetGlobalMessageConsumed(string entryId, bool consumed)
    {
        GetSaveState()?.SetGlobalEntryConsumed(entryId, consumed);
    }

    public bool TryGetActiveOptionTarget(
        string optionId,
        out RectTransform target)
    {
        target = null;
        if (!TryGetActiveOptionUnit(optionId, out OSLinkMessageSelectUnit unit))
            return false;

        target = unit.ScriptTarget;
        return target != null;
    }

    public bool TrySetScriptOptionHover(
        UnityEngine.Object owner,
        string optionId,
        bool hovered)
    {
        return TryGetActiveOptionUnit(optionId, out OSLinkMessageSelectUnit unit)
            && unit.SetScriptHover(owner, hovered);
    }

    public bool TrySelectOptionFromScript(
        UnityEngine.Object owner,
        string optionId)
    {
        return !IsChapter02FinalTalkOptionGestureBlocked()
            && TryGetActiveOptionUnit(optionId, out OSLinkMessageSelectUnit unit)
            && unit.TryInvokeScriptClick(owner);
    }

    public bool TrySelectDisplayedOptionImmediately(
        string sequenceId,
        string entryId,
        string optionId)
    {
        if (!IsPresenting
            || !string.Equals(
                displayedSequenceId,
                sequenceId,
                StringComparison.Ordinal)
            || !string.Equals(
                displayedEntryId,
                entryId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!isWaitingForOption
            && !CompleteDisplayedOptionTextImmediately())
        {
            return false;
        }

        if (IsChapter02FinalTalkOptionGestureBlocked()) return false;

        if (!TryGetActiveOptionUnit(
                optionId,
                out OSLinkMessageSelectUnit unit))
        {
            return false;
        }

        HandleOptionClicked(unit);
        return true;
    }

    public bool StartDisplayedTrailingDotsLoop(
        string sequenceId,
        string entryId,
        float interval)
    {
        if (string.IsNullOrEmpty(entryId)
            || !string.Equals(
                displayedSequenceId,
                sequenceId,
                StringComparison.Ordinal)
            || !string.Equals(
                displayedEntryId,
                entryId,
                StringComparison.Ordinal)
            || !TryGetDisplayedEntry(
                out _,
                out OSCharacterInteractionEntryData entry))
        {
            return false;
        }

        TMP_Text targetLabel = entry.PartType ==
                               OSCharacterInteractionPartType.TextWithOptions
            ? textWithOptionsLabel
            : textLabel;
        if (targetLabel == null)
            return false;

        string originalText = targetLabel.text ?? string.Empty;
        string baseText = RemoveTrailingDots(originalText);
        if (baseText.Length == originalText.Length)
            return false;

        StopDisplayedTrailingDotsLoop(false);
        trailingDotsLabel = targetLabel;
        trailingDotsOriginalText = originalText;
        windowLayout?.SetMeasurementText(targetLabel, baseText + "...");
        windowLayout?.RefreshLayout();

        float stepDuration = Mathf.Max(0.05f, interval);
        Sequence sequence = DOTween.Sequence();
        trailingDotsTween = sequence;
        for (int dotCount = 1; dotCount <= 3; dotCount++)
        {
            int displayedDotCount = dotCount;
            sequence.AppendCallback(() =>
            {
                if (targetLabel != null)
                {
                    targetLabel.text = baseText
                                       + new string('.', displayedDotCount);
                }
            });
            sequence.AppendInterval(stepDuration);
        }

        sequence
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (trailingDotsTween != sequence)
                    return;

                trailingDotsTween = null;
                trailingDotsLabel = null;
                trailingDotsOriginalText = null;
            });
        return true;
    }

    public void StopDisplayedTrailingDotsLoop(bool restoreText)
    {
        TMP_Text targetLabel = trailingDotsLabel;
        string originalText = trailingDotsOriginalText;
        Tween tween = trailingDotsTween;
        trailingDotsTween = null;
        trailingDotsLabel = null;
        trailingDotsOriginalText = null;
        tween?.Kill(false);

        if (restoreText && targetLabel != null)
            targetLabel.text = originalText ?? string.Empty;
    }

    public void RefreshCurrentMessageLocalization()
    {
        ApplyLocalizedFonts();
        if (!isOSVisible
            || string.IsNullOrEmpty(displayedEntryId))
        {
            return;
        }

        if (!TryGetDisplayedEntry(
                out OSCharacterInteractionSequenceData sequence,
                out OSCharacterInteractionEntryData entry))
        {
            return;
        }

        if (IsPresenting
            && string.Equals(
                activePresentationEntryId,
                entry.EntryId,
                StringComparison.Ordinal))
        {
            if (displayedEntryIsGlobal)
                PresentGlobalEntry(entry, true);
            else
                PresentSequenceEntry(sequence, entry, true);
            return;
        }

        string localizedText = ResolveLocalizedText(entry);
        TMP_Text targetLabel = entry.PartType ==
                               OSCharacterInteractionPartType.TextWithOptions
            ? textWithOptionsLabel
            : textLabel;
        if (targetLabel != null)
            targetLabel.text = localizedText;
        if (entry.PartType == OSCharacterInteractionPartType.TextWithOptions)
            ConfigureOptions(entry, isWaitingForOption);
        windowLayout?.SetMeasurementText(targetLabel, localizedText);
        windowLayout?.RefreshLayout();
    }

    bool TryTrigger(
        OSCharacterInteractionTriggerType triggerType,
        string triggerTargetId)
    {
        if (TryGetReadAlongProtectedQuestion(out _) && !TryGetCountdownQuestion(out _, out _)) return false;
        if (TryGetCursorExperimentReservedEntry(out _)) return false;
        if (OS_Handle.Instance?.IsChapter02CollectionMessageProtected == true || OS_Handle.Instance?.IsClawMachineMessageProtected == true
            || OS_Handle.Instance?.IsCursorExperimentMessageProtected == true)
            return false;
        if (!isOSVisible
            || !TryGetActiveSequence(
                out GameDataManager dataManager,
                out OSCharacterInteractionSaveData saveState,
                out OSCharacterInteractionSequenceData sequence)
            || saveState.isCompleted)
        {
            return false;
        }

        // These choices unlock treatment. Ambient desktop/window hints must not
        // replace them, including while the first choice waits for earlier text.
        // Option clicks advance directly, so both tutorial branches remain usable.
        if (HasPendingFirstTreatmentTutorial(saveState))
            return false;

        IReadOnlyList<OSCharacterInteractionEntryData> entries =
            sequence.Entries;
        if (entries == null)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            OSCharacterInteractionEntryData entry = entries[i];
            if (!CanTriggerSequenceEntry(
                    entry,
                    triggerType,
                    triggerTargetId,
                    dataManager,
                    saveState))
            {
                continue;
            }

            if (TryDeferForOptionCountdown(sequence.SequenceId, entry)) return true;

            // Resource confirmations remain available while the player reads on.
            bool hasPendingResourceChoice =
                saveState.phase == OSCharacterInteractionSavePhase.Presenting
                && sequence.TryGetEntry(saveState.presentingEntryId, out var presentedEntry)
                && HasResourceOptions(presentedEntry);
            bool shouldWait = hasPendingResourceChoice || (entry.ReplacementPolicy ==
                                  OSCharacterInteractionReplacementPolicy
                                      .WaitForCurrentTextComplete
                              && IsTextAnimating());
            if (shouldWait)
            {
                saveState.SetPendingEntry(entry.EntryId);
                return true;
            }

            ScheduleOrPresentSequenceEntry(sequence, entry);
            return true;
        }

        return false;
    }

    bool TryQueueGlobalTrigger(
        OSCharacterInteractionTriggerType triggerType,
        string triggerTargetId)
    {
        if (TryGetReadAlongProtectedQuestion(out _) && !TryGetCountdownQuestion(out _, out _)) return false;
        MaintainCursorExperimentMessageReservation();
        if (AreGlobalMessagesSuppressed)
            return false;

        GameDataManager dataManager = GameDataManager.Instance;
        OSCharacterInteractionData_SO data = dataManager != null
            ? dataManager.OSCharacterInteractionData
            : null;
        OSCharacterInteractionSaveData saveState = GetSaveState();
        IReadOnlyList<OSCharacterInteractionEntryData> entries =
            data != null ? data.GlobalEntries : null;
        if (saveState == null || entries == null)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            OSCharacterInteractionEntryData entry = entries[i];
            if (entry == null
                || !AllowsCursorExperimentEntry(entry)
                || string.IsNullOrWhiteSpace(entry.EntryId)
                || (!entry.Repeatable
                    && saveState.HasConsumedGlobalEntry(entry.EntryId))
                || !MatchesTrigger(entry, triggerType, triggerTargetId)
                || !MatchesConditions(entry, dataManager, saveState))
            {
                continue;
            }

            // Preserve the pending identity of a delayed FIFO head. A newer
            // arrival must enter the queue before it can touch that old slot.
            if (TryDeferForOptionCountdown(GlobalPresentationSequenceId, entry)) return true;
            if (!saveState.QueueGlobalEntry(entry.EntryId, entry.Repeatable))
                return false;

            ScheduleGlobalEntry(entry);
            return true;
        }

        return false;
    }

    void ScheduleOrPresentSequenceEntry(
        OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionEntryData entry)
    {
        if (sequence != null && TryDeferForOptionCountdown(sequence.SequenceId, entry)) return;
        if (TryGetCursorExperimentReservedEntry(out _)) return;
        if (!HasMessageContent(entry))
        {
            // Empty text placeholders have nothing to present and must never
            // occupy the pending slot used by real messages.
            return;
        }

        if (entry.TriggerDelay <= 0f)
        {
            PresentSequenceEntry(sequence, entry, false);
            return;
        }

        OSCharacterInteractionSaveData saveState = GetSaveState();
        saveState?.SetPendingEntry(entry.EntryId);
        KillPendingSequenceTween();
        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                entry.TriggerDelay,
                () =>
                {
                    if (pendingSequenceTween != delay)
                        return;

                    pendingSequenceTween = null;
                    if (isOSVisible)
                        PresentSequenceEntry(sequence, entry, false);
                },
                true)
            .SetUpdate(true);
        pendingSequenceTween = delay;
        delay.OnKill(() =>
        {
            if (pendingSequenceTween == delay)
                pendingSequenceTween = null;
        });
    }

    void ScheduleGlobalEntry(OSCharacterInteractionEntryData entry)
    {
        if (TryDeferForOptionCountdown(GlobalPresentationSequenceId, entry)) return;
        if (!ReadAlongQuestionAllows(entry)) return;
        if (!AllowsCursorExperimentEntry(entry)) return;
        if (!CanUseGlobalChapter(entry)) return;
        KillPendingGlobalTween();
        float delayDuration = Mathf.Max(0f, entry.TriggerDelay);
        if (delayDuration <= 0f)
        {
            StartQueuedGlobalEntry(entry, false);
            return;
        }

        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                delayDuration,
                () =>
                {
                    if (pendingGlobalTween != delay)
                        return;

                    pendingGlobalTween = null;
                    if (isOSVisible)
                        StartQueuedGlobalEntry(entry, false);
                },
                true)
            .SetUpdate(true);
        pendingGlobalTween = delay;
        delay.OnKill(() =>
        {
            if (pendingGlobalTween == delay)
                pendingGlobalTween = null;
        });
    }

    bool TryResumeOrScheduleGlobal(
        OSCharacterInteractionSaveData saveState)
    {
        MaintainCursorExperimentMessageReservation();
        if (AreGlobalMessagesSuppressed)
            return false;

        GameDataManager dataManager = GameDataManager.Instance;
        OSCharacterInteractionData_SO data = dataManager != null
            ? dataManager.OSCharacterInteractionData
            : null;
        if (data == null)
            return false;

        if (!string.IsNullOrEmpty(saveState.presentingGlobalEntryId)
            && data.TryGetGlobalEntry(
                saveState.presentingGlobalEntryId,
                out OSCharacterInteractionEntryData presentingEntry))
        {
            if (!CanUseGlobalChapter(presentingEntry)) return false;
            if (IsPresenting && activePresentationIsGlobal
                && (OS_Handle.IsChapter02ReadAlongEntry(presentingEntry.EntryId)
                    || CanExpireUnanswered(presentingEntry, null))
                && activePresentationEntryId == presentingEntry.EntryId) return true;
            StartQueuedGlobalEntry(presentingEntry, true);
            return true;
        }

        if (!string.IsNullOrEmpty(saveState.pendingGlobalEntryId)
            && data.TryGetGlobalEntry(
                saveState.pendingGlobalEntryId,
                out OSCharacterInteractionEntryData pendingEntry))
        {
            if (!CanUseGlobalChapter(pendingEntry)) return false;
            if (TryDeferForOptionCountdown(GlobalPresentationSequenceId, pendingEntry)) return false;
            ScheduleGlobalEntry(pendingEntry);
            return true;
        }

        return false;
    }

    void StartQueuedGlobalEntry(
        OSCharacterInteractionEntryData entry,
        bool isResume)
    {
        if (!CanPresentChapter04Entry(entry)) return;
        if (!isResume && TryDeferForOptionCountdown(GlobalPresentationSequenceId, entry)) return;
        if (!ReadAlongQuestionAllows(entry)) return;
        // A manually closed Live stays closed; opening it resumes the saved question.
        if (isResume && TryGetReadAlongProtectedQuestion(out _)
            && ownerCharacterWindow != null
            && (!ownerCharacterWindow.IsOpen || ownerCharacterWindow.IsMinimized)) return;
        if (AreGlobalMessagesSuppressed || !isOSVisible || entry == null || !AllowsCursorExperimentEntry(entry))
            return;
        if (!CanUseGlobalChapter(entry)) return;

        if (entry.PresentationCueId ==
            OSCharacterInteractionIds.PresentationCues.ReconnectLive)
        {
            BeginLiveReconnect(entry, isResume);
            return;
        }

        PresentGlobalEntry(entry, isResume);
    }

    void BeginLiveReconnect(
        OSCharacterInteractionEntryData entry,
        bool isResume)
    {
        ResolveReferences();
        reconnectEntry = entry;
        if (ownerCharacterWindow == null || ownerCharacterWindow.IsOpen)
        {
            CompleteLiveReconnectOpen(isResume);
            return;
        }

        ownerCharacterWindow.OpenAnimationCompleted -=
            HandleReconnectOpenAnimationCompleted;
        ownerCharacterWindow.OpenAnimationCompleted +=
            HandleReconnectOpenAnimationCompleted;
        bool opened = OS_Handle.Instance?.OpenCharacterWindowForReconnect(
            reconnectOpenDuration) ?? false;
        if (!opened && !ownerCharacterWindow.IsOpen)
        {
            ownerCharacterWindow.OpenAnimationCompleted -=
                HandleReconnectOpenAnimationCompleted;
            CompleteLiveReconnectOpen(isResume);
        }
    }

    void HandleReconnectOpenAnimationCompleted(OSWindowUnit source)
    {
        if (source != ownerCharacterWindow)
            return;

        ownerCharacterWindow.OpenAnimationCompleted -=
            HandleReconnectOpenAnimationCompleted;
        OSCharacterInteractionSaveData saveState = GetSaveState();
        bool isResume = saveState != null
                        && reconnectEntry != null
                        && saveState.presentingGlobalEntryId ==
                        reconnectEntry.EntryId;
        CompleteLiveReconnectOpen(isResume);
    }

    void CompleteLiveReconnectOpen(bool isResume)
    {
        OSCharacterInteractionEntryData entry = reconnectEntry;
        reconnectEntry = null;
        // The window may finish opening after a protected story beat takes over.
        if (entry == null || !isOSVisible || AreGlobalMessagesSuppressed || !AllowsCursorExperimentEntry(entry))
            return;

        PresentGlobalEntry(entry, isResume);
        KillReconnectAnimationTween();
        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                reconnectAnimationDelay,
                () => characterWindowController?.PlayReconnectAnimation(),
                true)
            .SetUpdate(true);
        reconnectAnimationTween = delay;
        delay.OnKill(() =>
        {
            if (reconnectAnimationTween == delay)
                reconnectAnimationTween = null;
        });
        delay.OnComplete(() =>
        {
            if (reconnectAnimationTween == delay)
                reconnectAnimationTween = null;
        });
    }

    void PresentSequenceEntry(
        OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionEntryData entry,
        bool isResume)
    {
        if (sequence != null && !isResume && TryDeferForOptionCountdown(sequence.SequenceId, entry)) return;
        if (TryGetReadAlongProtectedQuestion(out _)) return;
        if (sequence == null || entry == null || TryGetCursorExperimentReservedEntry(out _))
            return;
        if (OS_Handle.Instance?.AllowsChapter02CollectionEntry(sequence.SequenceId, entry.EntryId) == false)
            return;

        // A delayed ambient sequence callback must not replace the experiment's
        // protected message. Resuming that same entry remains valid.
        if (OS_Handle.Instance?.IsCursorExperimentMessageProtected == true
            && (displayedSequenceId != sequence.SequenceId || displayedEntryId != entry.EntryId))
            return;

        OSCharacterInteractionSaveData saveState = GetSaveState();
        if (saveState == null)
            return;

        if (!isResume)
        {
            InterruptActiveSavePresentation();
            saveState.BeginPresentation(entry.EntryId, !entry.Repeatable);
        }

        BeginEntryVisualPresentation(
            sequence,
            entry,
            false,
            sequence.SequenceId);
    }

    void PresentGlobalEntry(
        OSCharacterInteractionEntryData entry,
        bool isResume)
    {
        if (!CanPresentChapter04Entry(entry)) return;
        if (!isResume && TryDeferForOptionCountdown(GlobalPresentationSequenceId, entry)) return;
        if (!ReadAlongQuestionAllows(entry)) return;
        // Protection blocks replacement by unrelated messages, but the active
        // entry still needs to rebuild its text and buttons on language change.
        bool resumingDisplayedEntry = isResume && entry != null && displayedEntryIsGlobal
            && string.Equals(displayedEntryId, entry.EntryId, StringComparison.Ordinal)
            && string.Equals(activePresentationEntryId, entry.EntryId, StringComparison.Ordinal);
        if ((OS_Handle.Instance?.IsChapter02CollectionMessageProtected == true
            || OS_Handle.Instance?.IsCursorExperimentMessageProtected == true
            || OS_Handle.Instance?.IsChapter02FinaleMessageProtected == true
            || OS_Handle.Instance?.IsChapter02PaperPushMessageProtected == true
            || OS_Handle.Instance?.IsChapter04EntryMessageProtected == true
        || OS_Handle.Instance?.IsChapter03EntryMessageProtected == true
            || OS_Handle.Instance?.IsChapter03StoryMessageProtected == true
            || OS_Handle.Instance?.IsChapter02FinalTalkMessageProtected == true) && !resumingDisplayedEntry) return;
        if (entry == null || !AllowsCursorExperimentEntry(entry))
            return;
        if (!CanUseGlobalChapter(entry)) return;

        OSCharacterInteractionSaveData saveState = GetSaveState();
        if (saveState == null)
            return;

        if (!isResume)
        {
            InterruptActiveSavePresentation();
            saveState.BeginGlobalPresentation(
                entry.EntryId,
                !entry.Repeatable);
        }

        BeginEntryVisualPresentation(
            null,
            entry,
            true,
            GlobalPresentationSequenceId);
    }

    void BeginEntryVisualPresentation(
        OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionEntryData entry,
        bool isGlobal,
        string presentationSequenceId)
    {
        int version = BeginNewPresentation();
        activeSequence = sequence;
        activeEntry = entry;
        activePresentationIsGlobal = isGlobal;
        activePresentationEntryId = entry.EntryId;
        displayedEntryIsGlobal = isGlobal;
        displayedSequenceId = presentationSequenceId;
        displayedEntryId = entry.EntryId;
        PrepareCountdownMessageReservation(presentationSequenceId, entry.EntryId);
        PrepareOptionCountdown();

        if (!isGlobal && entry.PreserveOnOwnerClose)
        {
            KillPendingGlobalTween();
            KillReconnectAnimationTween();
            if (ownerCharacterWindow != null
                && (!ownerCharacterWindow.IsOpen || ownerCharacterWindow.IsMinimized))
            {
                OS_Handle.Instance?.OpenCharacterWindowForReconnect(reconnectOpenDuration);
            }
        }

        string localizedText = ResolveLocalizedText(entry);
        EntryPresentationStarted?.Invoke(presentationSequenceId, entry);
        ShowEntry(entry, localizedText, version);
    }

    void ShowEntry(
        OSCharacterInteractionEntryData entry,
        string localizedText,
        int version)
    {
        ResolveReferences();
        TMP_Text targetLabel;
        GameObject targetPart;
        bool waitsForOption;
        switch (entry.PartType)
        {
            case OSCharacterInteractionPartType.Text:
                targetLabel = textLabel;
                targetPart = textPart;
                waitsForOption = false;
                break;
            case OSCharacterInteractionPartType.TextWithOptions:
                targetLabel = textWithOptionsLabel;
                targetPart = textWithOptionsPart;
                waitsForOption = true;
                break;
            default:
                Debug.LogWarning(
                    $"[OSLinkMessage] Part '{entry.PartType}' is not ready "
                    + $"for entry '{entry.EntryId}'.",
                    this);
                CompleteActivePresentation(entry.EntryId, version);
                return;
        }

        if (linkWindow == null || targetPart == null || targetLabel == null)
        {
            Debug.LogWarning(
                $"[OSLinkMessage] References for part '{entry.PartType}' "
                + $"are incomplete on entry '{entry.EntryId}'.",
                this);
            CompleteActivePresentation(entry.EntryId, version);
            return;
        }

        linkWindow.OpenStandalone();
        SetOnlyPartActive(targetPart);
        if (waitsForOption)
            ConfigureOptions(entry, false);
        windowLayout?.SetMeasurementText(targetLabel, localizedText);
        windowLayout?.RefreshLayout();

        bool shouldReopenPart = hasDisplayedMessage;
        if (shouldReopenPart)
            targetPart.SetActive(false);

        if (!shouldReopenPart || partReopenDelay <= 0f)
        {
            BeginTextPlayback(
                entry,
                targetPart,
                targetLabel,
                localizedText,
                waitsForOption,
                version);
            return;
        }

        Tween delay = DOVirtual.DelayedCall(
                partReopenDelay,
                () => BeginTextPlayback(
                    entry,
                    targetPart,
                    targetLabel,
                    localizedText,
                    waitsForOption,
                    version),
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        reopenTween = delay;
        delay.OnKill(() =>
        {
            if (reopenTween == delay)
                reopenTween = null;
        });
        TickChapter03Text();
    }

    void BeginTextPlayback(
        OSCharacterInteractionEntryData entry,
        GameObject targetPart,
        TMP_Text targetLabel,
        string localizedText,
        bool waitsForOption,
        int version)
    {
        if (version != presentationVersion
            || entry == null
            || targetPart == null
            || targetLabel == null)
        {
            return;
        }

        reopenTween = null;
        if (!targetPart.activeSelf)
            targetPart.SetActive(true);

        string targetText = localizedText ?? string.Empty;
        targetLabel.maxVisibleCharacters = int.MaxValue;
        targetLabel.text = targetText;
        targetLabel.ForceMeshUpdate(true);
        int visibleCharacterCount = Mathf.Max(
            0,
            targetLabel.textInfo.characterCount);
        windowLayout?.SetMeasurementText(targetLabel, targetText);
        windowLayout?.RefreshLayout();
        hasDisplayedMessage = true;
        isWaitingForOption = false;

        if (RestoreCompletedChapter04Text(entry, targetLabel, waitsForOption)) return;

        if (entry.DisplayPolicy == OSCharacterInteractionDisplayPolicy.Immediate
            || visibleCharacterCount == 0)
        {
            targetLabel.text = targetText;
            HandleTextPlaybackCompleted(
                entry,
                waitsForOption,
                version);
            return;
        }

        characterWindowController?.BeginLinkMessageTyping();
        targetLabel.text = string.Empty;
        float duration = Mathf.Max(
            minimumTextDuration,
            visibleCharacterCount / Mathf.Max(1f, charactersPerSecond));
        bool chapter03Text = OSChapter03StoryDirector.IsOwnEntry(entry.EntryId);
        // Chapter 4 at the grave (round 8): the grief tier sets the rhythm.
        int griefTier = chapter03Text ? 0 : Chapter04GriefTier(entry);
        bool steppedText = chapter03Text || griefTier > 0;
        Tween tween = (chapter03Text
            ? CreateChapter03TextTween(targetLabel, targetText, entry)
            : griefTier > 0 ? CreateChapter04GriefTween(targetLabel, targetText, griefTier)
            : targetLabel.DOText(targetText, duration, true))
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(targetPart, LinkBehaviour.KillOnDisable);
        textTween = tween;
        int voicedCharacterCount = 0;
        string voicedText = null;
        tween.OnUpdate(() =>
        {
            // Chapters 1/2 still use DOText; refresh its changed partial string
            // before reading TMP's parsed characters (rich tags are not sounds).
            if (!chapter03Text && targetLabel.text != voicedText)
            {
                targetLabel.ForceMeshUpdate(true);
                voicedText = targetLabel.text;
            }
            int count = Mathf.Min(targetLabel.textInfo.characterCount,
                steppedText ? targetLabel.maxVisibleCharacters : int.MaxValue);
            if (count > voicedCharacterCount)
            {
                // A slow frame may reveal several characters. The last one
                // decides the gate, so a newly reached ellipsis has no tail.
                characterWindowController?.NotifyLinkMessageCharacter(
                    targetLabel.textInfo.characterInfo[count - 1].character);
            }
            voicedCharacterCount = count;
            if (chapter03Text)
                OS_Handle.Instance?.Chapter03Story?.OnChapter03TextProgress(entry.EntryId, targetLabel);
        });
        tween.OnPause(() => characterWindowController?.SuspendLinkMessageTypingVoice());
        tween.OnComplete(() =>
        {
            if (textTween == tween)
                textTween = null;
            HandleTextPlaybackCompleted(
                entry,
                waitsForOption,
                version);
        });
        tween.OnKill(() =>
        {
            if (textTween == tween)
            {
                textTween = null;
                characterWindowController?.EndLinkMessageTyping();
            }
        });
        TickChapter03Text();
        TickChapter04Text();
    }

    void HandleTextPlaybackCompleted(
        OSCharacterInteractionEntryData entry,
        bool waitsForOption,
        int version)
    {
        if (version != presentationVersion)
            return;

        if (DeferChapter04Completion(entry, waitsForOption, version)) return;

        activeTextCompleted = true;
        characterWindowController?.EndLinkMessageTyping();
        if (waitsForOption)
        {
            isWaitingForOption = true;
            SetOptionsVisible(true);
            SetOptionsInteractable(true);
            windowLayout?.RefreshLayout();
            EntryTextPlaybackCompleted?.Invoke(
                displayedSequenceId,
                entry);
            return;
        }

        EntryTextPlaybackCompleted?.Invoke(displayedSequenceId, entry);
        if (version != presentationVersion)
            return;

        if (!activePresentationIsGlobal && !string.IsNullOrWhiteSpace(entry.NextEntryId))
        {
            ScheduleNextEntry(entry, version);
            return;
        }

        if (entry.WaitForExternalCompletion)
            return;

        ScheduleAutoClose(entry, version);
        // Arm before completion callbacks/pending messages can try to replace it.
        CompleteActivePresentation(entry.EntryId, version);
    }

    void ScheduleNextEntry(OSCharacterInteractionEntryData entry, int version)
    {
        ArmNextEntryCountdown(activeSequence, entry);
        if (entry.LoopTrailingDots)
            StartDisplayedTrailingDotsLoop(displayedSequenceId, entry.EntryId, 0.35f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left
            || eventData.dragging || !isOSVisible || !IsPresenting || activeTextCompleted
            || IsSuspendedByMinimize() || IsChapter04ChatVisible
            || chapter03TextPaused || chapter04TextPaused || collectionReadingPaused
            || OSSessionPersistence.IsRestoring || GameTestConsole.BlocksGameplayInput
            || IsOptionCountdownSettingsOpen() || CursorHandle.Instance?.IsScriptControlled == true)
            return;

        TMP_Text label = activeEntry?.PartType == OSCharacterInteractionPartType.TextWithOptions
            ? textWithOptionsLabel : textLabel;
        if (label == null || !label.gameObject.activeInHierarchy
            || !RectTransformUtility.RectangleContainsScreenPoint(
                label.rectTransform, eventData.position, eventData.pressEventCamera))
            return;

        // Handle the release on the message itself: newly revealed options need
        // a separate press/release, so this click cannot also pick an answer.
        if (activeEntry.PartType == OSCharacterInteractionPartType.TextWithOptions)
            TryCompleteDisplayedOptionTextImmediately(displayedEntryId);
        else
            TryCompleteDisplayedTextImmediately(displayedEntryId);
    }

    /// <summary>Complete this exact plain-text message without advancing another entry.</summary>
    public bool TryCompleteDisplayedTextImmediately(string expectedEntryId)
    {
        if (activeEntry == null || activeEntry.PartType != OSCharacterInteractionPartType.Text
            || activeTextCompleted || activeEntry.EntryId != expectedEntryId
            || displayedEntryId != expectedEntryId || linkWindow == null || !linkWindow.IsOpen
            || linkWindow.IsOpening || linkWindow.IsClosing || textPart == null || textLabel == null)
            return false;

        // Cancel the pending reveal as well as the typewriter, then use the same
        // completion callback as natural playback. A click never does both jobs.
        reopenTween?.Kill(false);
        reopenTween = null;
        textTween?.Kill(false);
        textTween = null;
        characterWindowController?.EndLinkMessageTyping();
        textPart.SetActive(true);
        string fullText = ResolveLocalizedText(activeEntry);
        textLabel.text = fullText;
        textLabel.maxVisibleCharacters = int.MaxValue;
        windowLayout?.SetMeasurementText(textLabel, fullText);
        windowLayout?.RefreshLayout();
        hasDisplayedMessage = true;
        HandleTextPlaybackCompleted(activeEntry, false, presentationVersion);
        return true;
    }

    /// <summary>Finish a specific option message without selecting an answer.</summary>
    public bool TryCompleteDisplayedOptionTextImmediately(string expectedEntryId)
    {
        if (activeEntry == null || activeTextCompleted || activeEntry.EntryId != expectedEntryId
            || displayedEntryId != expectedEntryId || linkWindow == null || !linkWindow.IsOpen
            || linkWindow.IsOpening || linkWindow.IsClosing) return false;
        return CompleteDisplayedOptionTextImmediately();
    }

    bool CompleteDisplayedOptionTextImmediately()
    {
        if (activeEntry == null
            || activeEntry.PartType !=
               OSCharacterInteractionPartType.TextWithOptions)
        {
            return false;
        }

        if (reopenTween != null)
        {
            Tween tween = reopenTween;
            reopenTween = null;
            tween.Kill(false);
        }

        if (textTween != null)
        {
            Tween tween = textTween;
            textTween = null;
            tween.Kill(false);
        }

        characterWindowController?.EndLinkMessageTyping();
        if (textWithOptionsPart != null
            && !textWithOptionsPart.activeSelf)
        {
            textWithOptionsPart.SetActive(true);
        }

        string localizedText = ResolveLocalizedText(activeEntry);
        if (textWithOptionsLabel != null)
        {
            textWithOptionsLabel.text = localizedText;
            textWithOptionsLabel.maxVisibleCharacters = int.MaxValue;
        }
        windowLayout?.SetMeasurementText(
            textWithOptionsLabel,
            localizedText);
        windowLayout?.RefreshLayout();
        hasDisplayedMessage = true;
        HandleTextPlaybackCompleted(activeEntry, true, presentationVersion);
        return isWaitingForOption;
    }

    void HandleOptionClicked(OSLinkMessageSelectUnit unit)
    {
        if (!isWaitingForOption
            || IsChapter02FinalTalkOptionGestureBlocked()
            || unit == null
            || !activeOptions.TryGetValue(
                unit,
                out OSCharacterInteractionOptionData option)
            || activeEntry == null)
        {
            return;
        }

        bool isGlobal = activePresentationIsGlobal;
        OSCharacterInteractionSequenceData sequence = activeSequence;
        if (!isGlobal && sequence == null)
            return;

        OSCharacterInteractionEntryData entry = activeEntry;
        string sequenceId = isGlobal
            ? GlobalPresentationSequenceId
            : sequence.SequenceId;
        string nextEntryId = option.NextEntryId;
        bool closeAfterSelection = isGlobal
            || ShouldCloseAfterOptionSelection(
                sequenceId,
                entry.EntryId,
                nextEntryId);
        int version = presentationVersion;
        string deferredEntryId = HasResourceOptions(entry) ? GetSaveState()?.pendingEntryId : null;
        bool fromCountdown = GetOptionCountdownDuration(entry, sequence) > 0f;
        isWaitingForOption = false;
        SetOptionsInteractable(false);
        GameDataManager resourcesOwner = GameDataManager.Instance;
        if (resourcesOwner != null)
        {
            int energyBefore = resourcesOwner.GetOSResourceValue(MrTGame.Core.ResourceType.Battery);
            resourcesOwner.TryApplyCharacterOptionResources(sequenceId, entry.EntryId, option);
            if (resourcesOwner.GetOSResourceValue(MrTGame.Core.ResourceType.Battery) < energyBefore)
                OSUIEnergySpendPresenter.Show(this, unit.ScriptTarget);
        }
        OptionSelected?.Invoke(sequenceId, entry, option);
        CompleteActivePresentation(entry.EntryId, version, false);

        OSCharacterInteractionSaveData saveState = GetSaveState();
        GameDataManager dataManager = GameDataManager.Instance;
        if (isGlobal)
        {
            OSCharacterInteractionData_SO data = dataManager != null
                ? dataManager.OSCharacterInteractionData
                : null;
            if (!string.IsNullOrWhiteSpace(nextEntryId)
                && data != null
                && data.TryGetGlobalEntry(
                    nextEntryId,
                    out OSCharacterInteractionEntryData nextGlobalEntry))
            {
                PresentCountdownReply(null, nextGlobalEntry, true, fromCountdown);
                return;
            }

            StopVisualPresentation(true);
            if (!OSChapter04StoryDirector.IsOwnEntry(entry.EntryId) || !IsChapter04ChatVisible)
                CloseLinkWindowWithoutCallback();
            return;
        }

        if (!string.IsNullOrWhiteSpace(nextEntryId)
            && sequence.TryGetEntry(
                nextEntryId,
                out OSCharacterInteractionEntryData nextEntry)
            && CanPresentSequenceEntry(nextEntry, dataManager, saveState))
        {
            PresentCountdownReply(sequence, nextEntry, false, fromCountdown);
            return;
        }

        if (closeAfterSelection)
        {
            StopVisualPresentation(true);
            // A confirmed choice must close even while user dismissal is
            // guarded, and still notify story listeners waiting on Closed.
            CloseLinkWindowWithoutCallback(true);
        }

        if (!string.IsNullOrEmpty(deferredEntryId)
            && sequence.TryGetEntry(deferredEntryId, out var deferredEntry)
            && CanPresentSequenceEntry(deferredEntry, dataManager, saveState))
        {
            ScheduleOrPresentSequenceEntry(sequence, deferredEntry);
        }
    }

    static bool HasResourceOptions(OSCharacterInteractionEntryData entry)
    {
        if (entry == null || entry.PartType != OSCharacterInteractionPartType.TextWithOptions)
            return false;
        foreach (OSCharacterInteractionOptionData option in entry.Options)
        {
            var change = option?.ResourceChange;
            if (change != null && (Mathf.RoundToInt(change.Heart) != 0
                || Mathf.RoundToInt(change.Battery) != 0 || Mathf.RoundToInt(change.Fault) != 0))
                return true;
        }
        return false;
    }

    static bool ShouldCloseAfterOptionSelection(
        string sequenceId,
        string entryId,
        string nextEntryId)
    {
        if (!string.IsNullOrWhiteSpace(nextEntryId))
        {
            return false;
        }

        if (sequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair
            && (entryId == OSCharacterInteractionIds.Chapter01Tool.DecorationPrompt
                || entryId == "chapter01_memory_mother_daughter_recall"))
            return true;

        if (sequenceId == "chapter02.recovery"
            && (entryId == "chapter02_recovery_done" || entryId == "chapter02_return_ready"))
            return true;

        if (string.Equals(
                sequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal))
        {
            return string.Equals(
                entryId,
                OSCharacterInteractionIds.Chapter01Entries
                    .LiveEntryConfirm,
                StringComparison.Ordinal);
        }

        if (string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHealEnding,
                StringComparison.Ordinal))
        {
            return string.Equals(
                       entryId,
                       OSCharacterInteractionIds.Event01EndingEntries
                           .KeyRecovered,
                       StringComparison.Ordinal)
                   || string.Equals(
                       entryId,
                       OSCharacterInteractionIds.Event01EndingEntries
                           .CheckResult,
                       StringComparison.Ordinal)
                   || string.Equals(
                       entryId,
                       OSCharacterInteractionIds.Event01EndingEntries.Return,
                       StringComparison.Ordinal);
        }

        if (!string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHeal,
                StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
                   entryId,
                   OSCharacterInteractionIds.Event01Entries
                       .HeartHealStartAfterShock,
                   StringComparison.Ordinal)
               || string.Equals(
                   entryId,
                   OSCharacterInteractionIds.Event01Entries
                       .HeartHealShockAfterStart,
                   StringComparison.Ordinal);
    }

    void CompleteActivePresentation(
        string entryId,
        int expectedVersion = -1,
        bool presentPending = true)
    {
        if (expectedVersion >= 0 && expectedVersion != presentationVersion)
            return;

        OSCharacterInteractionEntryData completedEntry = activeEntry;
        OSCharacterInteractionSequenceData completedSequence = activeSequence;
        bool completedGlobal = activePresentationIsGlobal;
        string sequenceId = completedGlobal
            ? GlobalPresentationSequenceId
            : completedSequence != null
                ? completedSequence.SequenceId
                : displayedSequenceId;
        PreservePendingCountdownMessage(completedSequence, completedEntry);
        ClearOptionCountdown(sequenceId, entryId);
        ClearActivePresentationRuntime();

        OSCharacterInteractionSaveData saveState = GetSaveState();
        if (saveState == null || completedEntry == null)
            return;

        if (completedGlobal)
        {
            saveState.CompleteGlobalPresentation(entryId);
            EntryPresentationCompleted?.Invoke(sequenceId, completedEntry);
            return;
        }

        string pendingEntryId = saveState.pendingEntryId;
        saveState.CompletePresentation(
            entryId,
            !completedEntry.Repeatable);
        EntryPresentationCompleted?.Invoke(sequenceId, completedEntry);
        SyncEvent01TutorialSuppression();

        if (!presentPending || completedSequence == null)
        {
            saveState.SetPendingEntry(null);
            UpdateSequenceCompletion(completedSequence, saveState);
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (!string.IsNullOrEmpty(pendingEntryId)
            && completedSequence.TryGetEntry(
                pendingEntryId,
                out OSCharacterInteractionEntryData pendingEntry)
            && CanPresentSequenceEntry(
                pendingEntry,
                dataManager,
                saveState))
        {
            ScheduleOrPresentSequenceEntry(completedSequence, pendingEntry);
            return;
        }

        saveState.SetPendingEntry(null);
        UpdateSequenceCompletion(completedSequence, saveState);
    }

    int BeginNewPresentation()
    {
        restartAutoCloseAfterRestore = false;
        presentationVersion++;
        KillPresentationTweens();
        ClearActivePresentationRuntime();
        return presentationVersion;
    }

    void InterruptActiveSavePresentation()
    {
        if (!IsPresenting)
            return;
        // Closing or replacing UI must not silently answer an optional conversation.
        // Keep its saved presentation so Live can resume it when explicitly reopened.
        if (activePresentationIsGlobal && TryGetReadAlongProtectedQuestion(out _)) return;
        if (TryGetCountdownQuestion(out _, out _)) return;

        OSCharacterInteractionSaveData saveState = GetSaveState();
        if (activeEntry != null && activeEntry.SelectTimeoutOptionOnInterrupt)
        {
            foreach (OSCharacterInteractionOptionData option in activeEntry.Options)
            {
                if (option == null || option.OptionId != activeEntry.TimeoutOptionId)
                    continue;

                // Resolve the default decision without launching a branch over the incoming message.
                OSCharacterInteractionEntryData entry = activeEntry;
                bool isGlobal = activePresentationIsGlobal;
                string sequenceId = displayedSequenceId;
                ClearActivePresentationRuntime();
                OptionSelected?.Invoke(sequenceId, entry, option);
                if (isGlobal)
                    saveState?.CompleteGlobalPresentation(entry.EntryId);
                else
                    saveState?.CompletePresentation(entry.EntryId, !entry.Repeatable);
                EntryPresentationCompleted?.Invoke(sequenceId, entry);
                return;
            }
        }
        if (activePresentationIsGlobal)
            saveState?.CancelGlobalPresentation();
        else
            saveState?.CancelPresentation();
    }

    void StopVisualPresentation(bool clearText)
    {
        resumePresentationAfterOwnerRestore = false;
        restartAutoCloseAfterRestore = false;
        presentationVersion++;
        ClearActivePresentationRuntime();
        KillPresentationTweens();
        windowLayout?.ClearMeasurementText();

        if (clearText)
        {
            if (textLabel != null)
                textLabel.text = string.Empty;
            if (textWithOptionsLabel != null)
                textWithOptionsLabel.text = string.Empty;
        }

        hasDisplayedMessage = false;
        displayedSequenceId = null;
        displayedEntryId = null;
        displayedEntryIsGlobal = false;
    }

    bool IsSuspendedByMinimize()
    {
        return isOSVisible
            && ((ownerCharacterWindow != null && ownerCharacterWindow.IsOpen
                    && ownerCharacterWindow.IsMinimized)
                || (linkWindow != null && linkWindow.IsOpen && linkWindow.IsMinimized));
    }

    void ResumePresentationAfterOwnerRestore()
    {
        if (isOSVisible)
            NotifyOSBecameVisible();
    }

    void ClearActivePresentationRuntime()
    {
        activeTextCompleted = false;
        SetOptionCountdownProgress(0f, false);
        activePresentationEntryId = null;
        activeSequence = null;
        activeEntry = null;
        activePresentationIsGlobal = false;
        isWaitingForOption = false;
        SetOptionsInteractable(false);
    }

    void KillPresentationTweens()
    {
        StopDisplayedTrailingDotsLoop(false);

        if (autoCloseTween != null)
        {
            Tween tween = autoCloseTween;
            autoCloseTween = null;
            tween.Kill(false);
        }

        if (reopenTween != null)
        {
            Tween tween = reopenTween;
            reopenTween = null;
            tween.Kill(false);
        }

        if (textTween != null)
        {
            Tween tween = textTween;
            textTween = null;
            tween.Kill(false);
        }

        textLabel?.DOKill(false);
        textWithOptionsLabel?.DOKill(false);
        characterWindowController?.EndLinkMessageTyping();
    }

    // Only the invitation's own text/reading hold follows its visible-time clock.
    // Unrelated messages and other story tweens keep their existing lifetime.
    internal void SetChapter02CollectionPaused(bool paused)
    {
        if (!OS_Handle.IsChapter02CollectionEntry(displayedEntryId)) return;
        collectionReadingPaused = paused;
        if (paused)
        {
            textTween?.Pause();
            autoCloseTween?.Pause();
        }
        else
        {
            textTween?.Play();
            autoCloseTween?.Play();
        }
    }

    void ScheduleAutoClose(
        OSCharacterInteractionEntryData entry,
        int expectedVersion,
        float elapsed = 0f)
    {
        float delayDuration = entry != null
            ? Mathf.Max(0f, entry.AutoCloseDelay)
            : 0f;
        if (delayDuration <= 0f
            || expectedVersion != presentationVersion
            || !string.Equals(
                displayedEntryId,
                entry.EntryId,
                StringComparison.Ordinal))
        {
            return;
        }

        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                delayDuration,
                () =>
                {
                    if (autoCloseTween != delay)
                        return;

                    if (expectedVersion != presentationVersion
                        || !string.Equals(
                            displayedEntryId,
                            entry.EntryId,
                            StringComparison.Ordinal))
                    {
                        return;
                    }

                    TickWaitLine();
                    autoCloseTween = null;
                    StopVisualPresentation(true);
                    CloseLinkWindowWithoutCallback();
                },
                true)
            .SetUpdate(UpdateType.Late, true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        autoCloseTween = delay;
        if (elapsed > 0f) delay.Goto(Mathf.Min(elapsed, delayDuration), false);
        RefreshReadingHoldPause();
        delay.OnKill(() =>
        {
            if (autoCloseTween == delay)
                autoCloseTween = null;
        });
    }

    void RefreshReadingHoldPause()
    {
        if (autoCloseTween == null || !autoCloseTween.IsActive()) return;
        bool paused = !isOSVisible || IsSuspendedByMinimize() || chapter03TextPaused
            || (collectionReadingPaused && OS_Handle.IsChapter02CollectionEntry(displayedEntryId))
            || (!Application.isBatchMode && !Application.isFocused)
            || linkWindow == null || !linkWindow.IsOpen || linkWindow.IsOpening || linkWindow.IsClosing
            || (ownerCharacterWindow != null && (!ownerCharacterWindow.IsOpen || ownerCharacterWindow.IsClosing))
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || IsOptionCountdownSettingsOpen();
        if (paused) autoCloseTween.Pause();
        else autoCloseTween.Play();
    }

    static string RemoveTrailingDots(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        int endIndex = text.Length;
        while (endIndex > 0)
        {
            char character = text[endIndex - 1];
            if (character != '.' && character != '\u2026')
                break;

            endIndex--;
        }

        return endIndex == text.Length
            ? text
            : text.Substring(0, endIndex);
    }

    void KillPendingSequenceTween()
    {
        if (pendingSequenceTween == null)
            return;

        Tween tween = pendingSequenceTween;
        pendingSequenceTween = null;
        tween.Kill(false);
    }

    void KillPendingGlobalTween()
    {
        if (pendingGlobalTween == null)
            return;

        Tween tween = pendingGlobalTween;
        pendingGlobalTween = null;
        tween.Kill(false);
    }

    void KillReconnectAnimationTween()
    {
        if (reconnectAnimationTween == null)
            return;

        Tween tween = reconnectAnimationTween;
        reconnectAnimationTween = null;
        tween.Kill(false);
    }

    void ConfigureOptions(
        OSCharacterInteractionEntryData entry,
        bool interactable)
    {
        ClearOptionBindings();
        IReadOnlyList<OSCharacterInteractionOptionData> options =
            entry != null ? entry.Options : null;
        int optionCount = options != null ? options.Count : 0;
        int availableCount = optionUnits != null ? optionUnits.Length : 0;
        for (int i = 0; i < availableCount; i++)
        {
            OSLinkMessageSelectUnit unit = optionUnits[i];
            if (unit == null)
                continue;

            unit.ClearScriptHover();
            bool hasOption = i < optionCount && options[i] != null;
            if (unit.gameObject.activeSelf != hasOption)
                unit.gameObject.SetActive(hasOption);
            if (!hasOption)
                continue;

            OSCharacterInteractionOptionData option = options[i];
            string localizedText = ResolveLocalizedText(
                option.LocalizationKey,
                option.FallbackText);
            unit.SetText(MrTGame.Narrative.OSOptionLabel.Format(localizedText, i), option.ResourceChange);
            unit.SetInteractable(interactable, true);
            unit.Clicked += HandleOptionClicked;
            activeOptions[unit] = option;
        }

        SetOptionsVisible(interactable);
        if (optionCount > availableCount)
        {
            Debug.LogWarning(
                $"[OSLinkMessage] Entry '{entry.EntryId}' has "
                + $"{optionCount} options but only {availableCount} SelectUnits.",
                this);
        }
    }

    void SetOptionsVisible(bool visible)
    {
        if (buttonArea != null)
            buttonArea.gameObject.SetActive(visible);
        foreach (var pair in activeOptions)
            if (pair.Key != null) pair.Key.gameObject.SetActive(visible);
    }

    void SetOptionsInteractable(bool interactable)
    {
        foreach (KeyValuePair<
                     OSLinkMessageSelectUnit,
                     OSCharacterInteractionOptionData> pair in activeOptions)
        {
            pair.Key?.SetInteractable(interactable);
        }
    }

    void ClearOptionBindings()
    {
        if (optionUnits != null)
        {
            for (int i = 0; i < optionUnits.Length; i++)
            {
                OSLinkMessageSelectUnit unit = optionUnits[i];
                if (unit == null)
                    continue;

                unit.ClearScriptHover();
                unit.Clicked -= HandleOptionClicked;
                unit.SetInteractable(false, true);
            }
        }

        activeOptions.Clear();
    }

    bool TryGetActiveOptionUnit(
        string optionId,
        out OSLinkMessageSelectUnit unit)
    {
        unit = null;
        if (!isWaitingForOption || string.IsNullOrWhiteSpace(optionId))
            return false;

        string normalizedId = optionId.Trim();
        foreach (KeyValuePair<
                     OSLinkMessageSelectUnit,
                     OSCharacterInteractionOptionData> pair in activeOptions)
        {
            if (pair.Key != null
                && pair.Value != null
                && string.Equals(
                    pair.Value.OptionId,
                    normalizedId,
                    StringComparison.Ordinal))
            {
                unit = pair.Key;
                return true;
            }
        }

        return false;
    }

    void SetOnlyPartActive(GameObject activePart)
    {
        SetPartActive(textPart, textPart == activePart);
        SetPartActive(
            textWithOptionsPart,
            textWithOptionsPart == activePart);
        SetPartActive(imagePart, imagePart == activePart);
    }

    static void SetPartActive(GameObject part, bool isActive)
    {
        if (part != null && part.activeSelf != isActive)
            part.SetActive(isActive);
    }

    void CloseLinkWindowWithoutCallback(bool ignoreDismissalGuard = false)
    {
        if (linkWindow == null || !linkWindow.IsOpen)
            return;

        bool wasSuppressed = suppressLinkWindowClosedCallback;
        suppressLinkWindowClosedCallback = true;
        try
        {
            if (ignoreDismissalGuard || OS_Handle.Instance?.IsChapter02CollectionChoiceProtected == true
                || OS_Handle.Instance?.IsCursorExperimentStoryProtected == true
                || OS_Handle.Instance?.IsChapter02FinaleStoryProtected == true
                || OS_Handle.Instance?.IsChapter02PaperPushStoryProtected == true
                || OS_Handle.Instance?.IsChapter04EntryStoryProtected == true
            || OS_Handle.Instance?.IsChapter03EntryStoryProtected == true
                || OS_Handle.Instance?.IsChapter03StoryProtected == true
                || OS_Handle.Instance?.IsChapter02FinalTalkStoryProtected == true)
                linkWindow.CloseFromScript();
            else
                linkWindow.Close();
        }
        finally
        {
            suppressLinkWindowClosedCallback = wasSuppressed;
        }
    }

    void BindWindowEvents()
    {
        if (linkWindow != null)
        {
            linkWindow.Closed -= HandleLinkWindowClosed;
            linkWindow.Closed += HandleLinkWindowClosed;
        }

        if (ownerCharacterWindow != null)
        {
            ownerCharacterWindow.Closed -= HandleOwnerCharacterWindowClosed;
            ownerCharacterWindow.Closed += HandleOwnerCharacterWindowClosed;
        }
    }

    void UnbindWindowEvents()
    {
        if (linkWindow != null)
            linkWindow.Closed -= HandleLinkWindowClosed;
        if (ownerCharacterWindow != null)
        {
            ownerCharacterWindow.Closed -= HandleOwnerCharacterWindowClosed;
            ownerCharacterWindow.OpenAnimationCompleted -=
                HandleReconnectOpenAnimationCompleted;
        }
    }

    void HandleLinkWindowClosed(OSWindowUnit source)
    {
        if (source != linkWindow || suppressLinkWindowClosedCallback)
            return;

        // Attached chapter-four chat is dismissed by its Live host. Closing
        // the hidden playback carrier alone is an internal presentation reset.
        if (!IsChapter04ChatVisible)
            OS_Handle.Instance?.Chapter04Story?.NotifyDialogueDismissed();

        bool dismissedOptional = TryDismissOptionalMemoryQuestion();
        bool hasContinuation = !dismissedOptional && PreserveNextEntryCountdown();
        if (!dismissedOptional && !hasContinuation && !TryGetProtectedPresentation(out _, out _) && !TryGetReadAlongProtectedQuestion(out _)
            && !TryGetCountdownQuestion(out _, out _))
            InterruptActiveSavePresentation();
        StopVisualPresentation(true);
    }

    void HandleOwnerCharacterWindowClosed(OSWindowUnit source)
    {
        if (source != ownerCharacterWindow)
            return;

        bool dismissedOptional = TryDismissOptionalMemoryQuestion();
        bool preserve = !dismissedOptional && (PreserveNextEntryCountdown()
            || TryGetProtectedPresentation(out _, out _) || TryGetReadAlongProtectedQuestion(out _)
            || TryGetCountdownQuestion(out _, out _));
        if (!preserve && !dismissedOptional)
            InterruptActiveSavePresentation();
        StopVisualPresentation(true);
        CloseLinkWindowWithoutCallback();
        if (!preserve && !suppressOwnerWindowClosedNotification)
            NotifyWindowClosed(ownerCharacterWindow.WindowId);
    }

    bool TryGetProtectedPresentation(
        out OSCharacterInteractionSequenceData sequence,
        out OSCharacterInteractionEntryData entry)
    {
        entry = null;
        return TryGetActiveSequence(out _, out var saveState, out sequence)
            && sequence.TryGetEntry(saveState.presentingEntryId, out entry)
            && entry.PreserveOnOwnerClose;
    }

    static bool HasMessageContent(OSCharacterInteractionEntryData entry)
    {
        if (entry == null)
            return false;
        if (!string.IsNullOrWhiteSpace(entry.PresentationCueId))
            return true;

        string text = ResolveLocalizedText(entry);
        return !string.IsNullOrWhiteSpace(text)
            && (text != entry.LocalizationKey || !string.IsNullOrWhiteSpace(entry.FallbackText));
    }

    static bool IsEmptySequencePlaceholder(OSCharacterInteractionEntryData entry)
        => entry != null && entry.PartType == OSCharacterInteractionPartType.Text
            && !entry.WaitForExternalCompletion && string.IsNullOrWhiteSpace(entry.NextEntryId)
            && !HasMessageContent(entry);

    bool TryGetActiveSequence(
        out GameDataManager dataManager,
        out OSCharacterInteractionSaveData saveState,
        out OSCharacterInteractionSequenceData sequence)
    {
        dataManager = GameDataManager.Instance;
        saveState = dataManager != null
            ? dataManager.OSCharacterInteractionState
            : null;
        sequence = null;
        if (saveState == null)
            return false;

        saveState.EnsureData();
        return TryResolveSequence(
            saveState.activeSequenceId,
            out dataManager,
            out sequence);
    }

    bool TryResolveSequence(
        string sequenceId,
        out GameDataManager dataManager,
        out OSCharacterInteractionSequenceData sequence)
    {
        dataManager = GameDataManager.Instance;
        sequence = null;
        return dataManager != null
            && dataManager.OSCharacterInteractionData != null
            && dataManager.OSCharacterInteractionData.TryGetSequence(
                sequenceId,
                out sequence);
    }

    bool TryGetDisplayedEntry(
        out OSCharacterInteractionSequenceData sequence,
        out OSCharacterInteractionEntryData entry)
    {
        sequence = null;
        entry = null;
        GameDataManager dataManager = GameDataManager.Instance;
        OSCharacterInteractionData_SO data = dataManager != null
            ? dataManager.OSCharacterInteractionData
            : null;
        if (data == null)
            return false;

        if (displayedEntryIsGlobal)
            return data.TryGetGlobalEntry(displayedEntryId, out entry);

        return data.TryGetSequence(displayedSequenceId, out sequence)
            && sequence.TryGetEntry(displayedEntryId, out entry);
    }

    OSCharacterInteractionSaveData GetSaveState()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        return dataManager != null
            ? dataManager.OSCharacterInteractionState
            : null;
    }

    static bool CanTriggerSequenceEntry(
        OSCharacterInteractionEntryData entry,
        OSCharacterInteractionTriggerType triggerType,
        string triggerTargetId,
        GameDataManager dataManager,
        OSCharacterInteractionSaveData saveState)
    {
        return entry != null
            && !string.IsNullOrWhiteSpace(entry.EntryId)
            && !IsEmptySequencePlaceholder(entry)
            && (entry.Repeatable
                || !saveState.HasConsumedEntry(entry.EntryId))
            && MatchesTrigger(entry, triggerType, triggerTargetId)
            && MatchesConditions(entry, dataManager, saveState);
    }

    static bool CanPresentSequenceEntry(
        OSCharacterInteractionEntryData entry,
        GameDataManager dataManager,
        OSCharacterInteractionSaveData saveState)
    {
        return entry != null
            && saveState != null
            && !IsEmptySequencePlaceholder(entry)
            && (entry.Repeatable
                || !saveState.HasConsumedEntry(entry.EntryId))
            && MatchesConditions(entry, dataManager, saveState);
    }

    static bool MatchesTrigger(
        OSCharacterInteractionEntryData entry,
        OSCharacterInteractionTriggerType triggerType,
        string triggerTargetId)
    {
        if (entry.TriggerType != triggerType)
            return false;

        string expectedTarget = string.IsNullOrWhiteSpace(
            entry.TriggerTargetId)
            ? null
            : entry.TriggerTargetId.Trim();
        string actualTarget = string.IsNullOrWhiteSpace(triggerTargetId)
            ? null
            : triggerTargetId.Trim();
        return string.Equals(
            expectedTarget,
            actualTarget,
            StringComparison.Ordinal);
    }

    bool CanUseGlobalChapter(OSCharacterInteractionEntryData entry)
    {
        if (MatchesChapter(entry, GameDataManager.Instance)) return true;
        var state = GetSaveState();
        if (state != null)
        {
            if (state.pendingGlobalEntryId == entry.EntryId) state.pendingGlobalEntryId = null;
            if (state.presentingGlobalEntryId == entry.EntryId) state.presentingGlobalEntryId = null;
        }
        return false;
    }

    static bool MatchesChapter(OSCharacterInteractionEntryData entry, GameDataManager data)
        => entry != null && (entry.RequiredChapterIndex < 0 || (data != null && data.CurrentChapterIndex == entry.RequiredChapterIndex));

    static bool MatchesConditions(
        OSCharacterInteractionEntryData entry,
        GameDataManager dataManager,
        OSCharacterInteractionSaveData saveState)
    {
        if (!MatchesChapter(entry, dataManager)) return false;
        if (entry.RequiredHeartHealProcessIndex >= 0
            && (dataManager == null
                || dataManager.CurrentHeartHealProcessIndex !=
                entry.RequiredHeartHealProcessIndex))
        {
            return false;
        }

        IReadOnlyList<string> requiredEntries =
            entry.RequiredCompletedEntryIds;
        if (requiredEntries == null || requiredEntries.Count == 0)
            return true;
        if (saveState == null)
            return false;

        for (int i = 0; i < requiredEntries.Count; i++)
        {
            string requiredEntryId = requiredEntries[i];
            if (!string.IsNullOrWhiteSpace(requiredEntryId)
                && !saveState.HasCompletedEntry(requiredEntryId))
            {
                return false;
            }
        }

        return true;
    }

    static void UpdateSequenceCompletion(
        OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionSaveData saveState)
    {
        if (sequence == null || saveState == null)
            return;

        IReadOnlyList<OSCharacterInteractionEntryData> entries =
            sequence.Entries;
        if (entries == null)
            return;

        bool hasConsumableEntry = false;
        for (int i = 0; i < entries.Count; i++)
        {
            OSCharacterInteractionEntryData entry = entries[i];
            if (entry == null
                || entry.Repeatable
                || string.IsNullOrWhiteSpace(entry.EntryId))
            {
                continue;
            }

            hasConsumableEntry = true;
            if (!saveState.HasConsumedEntry(entry.EntryId))
            {
                return;
            }
        }

        if (hasConsumableEntry)
            saveState.MarkCompleted();
    }

    static string ResolveLocalizedText(
        OSCharacterInteractionEntryData entry)
    {
        return entry == null
            ? string.Empty
            : ResolveLocalizedText(
                entry.LocalizationKey,
                entry.FallbackText);
    }

    static string ResolveLocalizedText(string key, string fallback)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        return OSPalette.TintRichText(dataManager != null
            ? dataManager.GetLocalizedText(key, fallback)
            : fallback ?? string.Empty);
    }

    bool IsTextAnimating()
    {
        return textTween != null || reopenTween != null;
    }

    void SyncEvent01TutorialSuppression()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        OSCharacterInteractionSaveData saveState = dataManager != null
            ? dataManager.OSCharacterInteractionState
            : null;
        bool shouldSuppress = saveState != null
                              && dataManager.CurrentHeartHealProcessIndex == 1
                              && string.Equals(
                                  saveState.activeSequenceId,
                                  OSCharacterInteractionIds
                                      .PrologueEvent01HeartHeal,
                                  StringComparison.Ordinal)
                              && !saveState.HasCompletedEntry(
                                  OSCharacterInteractionIds.Event01Entries
                                      .OpeningRememberedFolder);
        OS_Handle.Instance?.SetEvent01DesktopTutorialSuppressed(
            shouldSuppress);
    }

    void ResolveReferences()
    {
        if (linkWindow == null)
            linkWindow = GetComponent<OSWindowUnit>();
        if (windowLayout == null)
            windowLayout = GetComponent<OSLinkMessageWindowLayout>();

        Transform messageArea = transform.Find("MessageArea");
        if (textPart == null)
            textPart = messageArea?.Find("TextPart")?.gameObject;
        if (textWithOptionsPart == null)
        {
            textWithOptionsPart = messageArea
                ?.Find("Text+ButtonPart")
                ?.gameObject;
        }
        if (imagePart == null)
            imagePart = messageArea?.Find("ImagePart")?.gameObject;
        if (textLabel == null && textPart != null)
            textLabel = textPart.GetComponentInChildren<TMP_Text>(true);

        if (textWithOptionsPart != null)
        {
            Transform textArea = textWithOptionsPart.transform.Find("TextArea");
            if (textWithOptionsLabel == null && textArea != null)
                textWithOptionsLabel = textArea.GetComponent<TMP_Text>();
            if (buttonArea == null && textArea != null)
                buttonArea = textArea.Find("ButtonArea") as RectTransform;
        }

        if ((optionUnits == null || optionUnits.Length == 0)
            && buttonArea != null)
        {
            optionUnits = buttonArea
                .GetComponentsInChildren<OSLinkMessageSelectUnit>(true);
        }

        if (ownerCharacterWindow == null && transform.parent != null)
        {
            ownerCharacterWindow =
                transform.parent.GetComponent<OSWindowUnit>();
        }
        if (characterWindowController == null
            && ownerCharacterWindow != null)
        {
            characterWindowController = ownerCharacterWindow
                .GetComponent<OSCharacterWindowController>();
        }
    }

    void ApplyLocalizedFonts()
    {
        OSLocalizedFontUtility.ApplyCurrent(textLabel);
        OSLocalizedFontUtility.ApplyCurrent(textWithOptionsLabel);
    }

    void SanitizeSettings()
    {
        charactersPerSecond = Mathf.Max(1f, charactersPerSecond);
        minimumTextDuration = Mathf.Max(0f, minimumTextDuration);
        partReopenDelay = Mathf.Max(0f, partReopenDelay);
        reconnectOpenDuration = Mathf.Max(0f, reconnectOpenDuration);
        reconnectAnimationDelay = Mathf.Max(0f, reconnectAnimationDelay);
    }

    void Reset()
    {
        ResolveReferences();
        SanitizeSettings();
    }

    void OnValidate()
    {
        ResolveReferences();
        SanitizeSettings();
    }
}
