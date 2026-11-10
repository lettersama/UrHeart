using System;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Persistent chapter-four dialogue. The TV is a presentation consumer, never
/// the owner of a line, option or narrative clock. No production lines are
/// invented when the authored manifest is empty.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(700)]
public sealed partial class OSChapter04StoryDirector : MonoBehaviour
{
    public const string EntryPrefix = OSChapter04StoryContent.EntryPrefix;
    OS_Handle os;
    OSLinkMessageController messages;
    OSWindowUnit liveWindow, settingsWindow, chatWindow;
    OSChapter04ChatView chatView;
    OSChapter04PresentationBridge presentation, subscribedPresentation;
    OSChapter04StoryState observed;
    OSChapter04StoryContent checkedContent;
    OSCharacterInteractionData_SO checkedInteractions;
    bool contentValid, hasFocus = true, optionReleasePending, ownsDialogueLayout, externalDialogueLayout;
    int optionBlockedFrame = -1;
    string contentError;
#if UNITY_EDITOR
    OSChapter04StoryContent debugContent;
    bool debugIgnoreFocus;
#endif

    public OSChapter04StoryState State => GameDataManager.Instance?.OSProcessData.memory.chapter04Story;
    public OSChapter04PresentationBridge Presentation => presentation;
    public OSChapter04StoryContent Content
    {
        get
        {
#if UNITY_EDITOR
            if (debugContent != null) return debugContent;
#endif
            return OSChapter04StoryContent.Instance;
        }
    }
    public bool IsActive => isActiveAndEnabled && !GameSetup.IsDemo
        && GameDataManager.Instance?.CurrentChapterIndex == 4
        && GameDataManager.Instance.OSProcessData.memory.chapter04EntryStep == (int)OSChapter04EntryDirector.EntryStep.Complete;
    public bool HasContent => contentValid && Content.Available;
    public string ContentError => contentError;
    public bool IsWaitingForContent => IsActive && !HasContent;
    public string ReservedEntryId => !IsActive ? null
        : State?.started == true && !State.completed ? State.currentEntryId ?? string.Empty : string.Empty;
    bool HasAttachedDialogue => chatView != null && chatView.OwnsLayout;
    OSWindowUnit DialogueWindow => HasAttachedDialogue ? liveWindow : chatWindow;
    public bool IsOptionGestureBlocked => !IsAvailable || DialogueWindow?.IsOpening == true || DialogueWindow?.IsClosing == true
        || optionReleasePending || optionBlockedFrame == Time.frameCount;
    public bool IsAvailable => IsActive && CommonAvailable && Ready(os?.TVWindow) && Ready(liveWindow)
        && State?.dialogueSuspended != true && (HasAttachedDialogue || chatWindow?.IsMinimized != true);
    public bool CanContinue => IsAvailable && HasContent && State?.completed == false
        && MotherGiftAllowsContinue
        && State.currentTextCompleted && State.currentCueCommitted && State.currentResourcesCommitted && !IsOptionGestureBlocked
        && Content.Find(State.currentEntryId)?.endAction != OSChapter04StoryContent.FailureWarningAction
        && CurrentMessageReady && CurrentEntry?.PartType == OSCharacterInteractionPartType.Text;
    public OSCharacterInteractionEntryData CurrentEntry
    {
        get
        {
            var data = GameDataManager.Instance?.OSCharacterInteractionData;
            return data != null && data.TryGetGlobalEntry(State?.currentEntryId, out var entry) ? entry : null;
        }
    }
    public string DateText => GetDateText(State?.currentEntryId);
    public event Action ProgressChanged;

    bool CommonAvailable => HasFocus && os != null && os.IsOSVisible
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && !os.IsSystemInputLockedWithoutChapter02Nightfall
        && !(settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized);
    bool CurrentMessageReady => messages != null && messages.DisplayedEntryId == State?.currentEntryId
        && messages.IsPresenting && Ready(DialogueWindow);
    bool HasFocus
    {
        get
        {
#if UNITY_EDITOR
            if (debugIgnoreFocus) return true;
#endif
            return hasFocus;
        }
    }
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsMinimized
        && !window.IsOpening && !window.IsClosing && window.gameObject.activeInHierarchy;
    public static bool IsOwnEntry(string id) => !string.IsNullOrEmpty(id)
        && id.StartsWith(EntryPrefix, StringComparison.Ordinal);

    void OnEnable()
    {
        hasFocus = Application.isFocused;
        GameEvents.LanguageChanged += OnLanguageChanged;
        Resolve();
    }
    void OnApplicationFocus(bool value) { hasFocus = value; if (!value) optionReleasePending = false; }
    void OnLanguageChanged(LanguageState language) => ProgressChanged?.Invoke();
    void OnDisable() { GameEvents.LanguageChanged -= OnLanguageChanged; ResetPresentation(); Unbind(); }
    void OnDestroy() { GameEvents.LanguageChanged -= OnLanguageChanged; ResetPresentation(); Unbind(); }
    void Update() => Tick();

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (os == null) return;
        if (chatView == null) TryGetComponent(out chatView);
        ResolveFinalTreatment();
        if (liveWindow == null)
        {
            liveWindow = os.CharacterWindowController?.GetComponent<OSWindowUnit>();
            if (liveWindow != null)
            {
                liveWindow.OpenStarted += OnLiveOpened;
                liveWindow.Closed += OnLiveClosed;
                liveWindow.LayoutChanged += OnLiveLayoutChanged;
            }
        }
        if (settingsWindow == null) settingsWindow = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (presentation == null)
        {
            presentation = GetComponent<OSChapter04PresentationBridge>();
            if (presentation == null) presentation = gameObject.AddComponent<OSChapter04PresentationBridge>();
        }
        if (subscribedPresentation != presentation)
        {
            if (subscribedPresentation != null) subscribedPresentation.StateChanged -= OnPresentationStateChanged;
            subscribedPresentation = presentation;
            if (subscribedPresentation != null) subscribedPresentation.StateChanged += OnPresentationStateChanged;
        }
        var next = os.LinkMessageController;
        if (messages == next) return;
        if (messages != null)
        {
            messages.EntryTextPlaybackCompleted -= OnTextCompleted;
            messages.OptionSelected -= OnOptionSelected;
        }
        messages = next;
        if (messages != null)
        {
            messages.EntryTextPlaybackCompleted += OnTextCompleted;
            messages.OptionSelected += OnOptionSelected;
        }
        if (chatWindow != null) chatWindow.LayoutChanged -= OnChatLayoutChanged;
        chatWindow = messages?.LinkWindow;
        if (chatWindow != null) chatWindow.LayoutChanged += OnChatLayoutChanged;
    }

    void Unbind()
    {
        UnbindGriefPresentation();
        UnbindFinalTreatment();
        if (subscribedPresentation != null) subscribedPresentation.StateChanged -= OnPresentationStateChanged;
        subscribedPresentation = null;
        if (liveWindow != null)
        {
            liveWindow.OpenStarted -= OnLiveOpened;
            liveWindow.Closed -= OnLiveClosed;
            liveWindow.LayoutChanged -= OnLiveLayoutChanged;
        }
        if (chatWindow != null) chatWindow.LayoutChanged -= OnChatLayoutChanged;
        liveWindow = chatWindow = null;
        if (messages == null) return;
        messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages.OptionSelected -= OnOptionSelected;
        messages = null;
    }

    void Tick()
    {
        Resolve();
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State;
            observed?.EnsureData();
        }
        if (optionReleasePending && Mouse.current?.leftButton.isPressed != true)
        { optionReleasePending = false; optionBlockedFrame = Time.frameCount; }
        if (!IsActive || State == null)
        {
            ResetMotherGiftPresentation();
            ApplyEntryLiveEmotion();
            finalTreatment?.Stop();
            ClearGriefPresentation();
            ReleaseFinalTreatmentBackground();
            if (IsOwnEntry(messages?.DisplayedEntryId)) messages.ClearChapter04StoryPresentation();
            chatView?.ReleaseView();
            ReleaseConversationTVLayout();
            externalDialogueLayout = false;
            if (presentation?.IsBound == true) presentation.Unbind();
            if (ownsDialogueLayout)
            {
                messages?.WindowLayout?.SetTemporaryLeftDialogue(this, false);
                ownsDialogueLayout = false;
            }
            return;
        }
        ValidateContent();
        if (OSSessionPersistence.IsRestoring)
        { presentation?.SetPaused(true); griefPresentation?.SetPaused(true); SetMotherGiftPaused(true); return; }
        MigrateLegacyGraveEntry();
        if (!State.started)
        {
            if (!CommonAvailable || !Ready(os.TVWindow)) return;
            State.version = 3; State.started = true;
            liveEmotionRestorePending = false;
            griefRestorePending = false;
            CaptureResourceBaseline();
            State.theatreState = OSChapter04PresentationBridge.CityWalk;
            InitializeFirstLine();
            presentation.Bind(State.theatreState, false);
            ProgressChanged?.Invoke();
        }
        else if (!presentation.IsBound)
        {
            // Old entry-only saves begin with the same first loop. Restoring a
            // saved transition skips to its stable end, without narrative events.
            string target = string.IsNullOrEmpty(State.theatreState)
                ? OSChapter04PresentationBridge.CityWalk : State.theatreState;
            if (!presentation.Bind(target, true))
            { SetContentError("Unknown saved theatre state: " + target); return; }
        }
        InitializeFirstLine();
        CaptureResourceBaseline();
        ApplyLiveEmotionForCurrentLine();
        ApplyGriefForCurrentLine();
        TickMotherGift();
        TickFinalTreatment();
        if (HasAttachedDialogue) EnsureConversationTVLayout();
        if (!ownsDialogueLayout && !externalDialogueLayout && Ready(liveWindow))
        {
            os.Chapter04Entry?.ReleaseDialogueShellToStory();
            messages?.WindowLayout?.SetTemporaryLeftDialogue(this, true);
            ownsDialogueLayout = true;
        }
        presentation.SetPaused(!IsAvailable);
        griefPresentation?.SetPaused(!IsAvailable);
        messages?.MaintainCursorExperimentMessageReservation();
        if (!IsAvailable || !HasContent || State.completed || string.IsNullOrEmpty(State.currentEntryId)) return;
        var beat = Content.Find(State.currentEntryId);
        if (beat == null)
        { SetContentError("Saved chapter-four entry is absent from the current script: " + State.currentEntryId); return; }
        EnsureMessage(beat);
        if (!State.currentTextCompleted || !CurrentMessageReady) return;
        if (!State.currentCueCommitted) CommitCue(beat);
        if (!State.currentResourcesCommitted) CommitResources(beat, false);
        if (beat.autoAdvanceDelay >= 0f && CurrentEntry?.PartType == OSCharacterInteractionPartType.Text)
        {
            State.currentReadElapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (State.currentReadElapsed >= beat.autoAdvanceDelay && CanContinue) TryContinue();
        }
    }

    void ValidateContent()
    {
        var interactions = GameDataManager.Instance?.OSCharacterInteractionData;
        if (ReferenceEquals(checkedContent, Content) && checkedInteractions == interactions) return;
        checkedContent = Content; checkedInteractions = interactions;
        var errors = Content.Validate(interactions);
        contentValid = errors.Count == 0;
        contentError = contentValid ? null : string.Join("\n", errors);
        if (!contentValid) Debug.LogError("[Chapter04 Story] " + contentError, this);
    }

    void SetContentError(string error)
    {
        contentValid = false;
        if (contentError != error) Debug.LogError("[Chapter04 Story] " + error, this);
        contentError = error;
        presentation?.SetPaused(true);
        griefPresentation?.SetPaused(true);
    }

    void InitializeFirstLine()
    {
        if (!HasContent || !State.started || State.completed || !string.IsNullOrEmpty(State.currentEntryId)) return;
        State.currentEntryId = Content.FirstEntryId;
        State.currentVisit = Mathf.Max(1, State.currentVisit + 1);
        State.currentTextCompleted = State.currentCueCommitted = false;
        State.currentResourcesPrepared = State.currentResourcesCommitted = false;
        State.currentReadElapsed = 0f;
        ProgressChanged?.Invoke();
    }

    void MigrateLegacyGraveEntry()
    {
        if (!HasContent || State?.started != true || State.completed
            || State.finalTreatmentStage != OSChapter04FinalTreatmentStage.None) return;
        var route = Content.FindResumeRoute(State.currentEntryId);
        if (route == null) return;
        // Keep existing history and live resource values. The new weighted
        // curve only charges toward absolute targets, so previously paid costs
        // are never restored or charged a second time during this migration.
        messages?.ClearChapter04StoryPresentation();
        State.currentEntryId = route.nextEntryId;
        State.currentVisit++;
        State.currentTextCompleted = State.currentCueCommitted = false;
        State.currentResourcesPrepared = State.currentResourcesCommitted = false;
        State.currentReadElapsed = 0f;
        State.heartWeightApplied = route.heartWeightBefore;
        State.faultWeightApplied = route.faultWeightBefore;
        State.theatreState = route.theatreState;
        State.version = Math.Max(3, State.version);
        presentation?.Unbind();
        griefRestorePending = true;
        ProgressChanged?.Invoke();
    }

    void EnsureMessage(OSChapter04StoryContent.Beat beat)
    {
        if (messages == null) return;
        if (messages.DisplayedEntryId == beat.entryId && messages.IsPresenting
            && messages.LinkWindow?.IsOpen == true && !messages.LinkWindow.IsClosing) return;
        if (messages.LinkWindow?.IsClosing == true || messages.IsPresenting) return;
        var interaction = GameDataManager.Instance.OSCharacterInteractionState;
        if (State.currentTextCompleted || interaction.presentingGlobalEntryId == beat.entryId)
        {
            messages.RestoreChapter04StoryEntry(beat.entryId);
            return;
        }
        if (interaction.pendingGlobalEntryId == beat.entryId) return;
        // Consumption is a UI delivery receipt, not a story checkpoint. Re-arm
        // only the unfinished current line after its previous window was closed.
        interaction.SetGlobalEntryConsumed(beat.entryId, false);
        messages.NotifyGlobalExternalSignal(beat.signalId);
    }

    public bool CanPresentEntry(string entryId) => IsAvailable && HasContent && State?.started == true
        && !State.completed && ReferenceEquals(observed, State) && State.currentEntryId == entryId;
    public bool HasCompletedCurrentText(string entryId) => IsActive && ReferenceEquals(observed, State)
        && State?.currentEntryId == entryId && State.currentTextCompleted;

    void OnTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (sequence != OSCharacterInteractionIds.GlobalSequence || entry == null
            || !CanPresentEntry(entry.EntryId) || State.currentTextCompleted) return;
        State.currentTextCompleted = true;
        State.currentReadElapsed = 0f;
        var completedState = State;
        RecordCurrentLine();
        CommitCue(Content.Find(entry.EntryId));
        if (!ReferenceEquals(completedState, State)) return;
        CommitResources(Content.Find(entry.EntryId));
        if (!ReferenceEquals(completedState, State)) return;
        BeginFailureWarningDelay(Content.Find(entry.EntryId));
        GuardCurrentGesture();
        ProgressChanged?.Invoke();
    }

    OSChapter04StoryRecord RecordCurrentLine()
    {
        foreach (var record in State.history)
            if (record != null && record.visit == State.currentVisit) return record;
        var added = new OSChapter04StoryRecord { visit = State.currentVisit, entryId = State.currentEntryId };
        State.history.Add(added);
        return added;
    }

    void CommitCue(OSChapter04StoryContent.Beat beat)
    {
        if (State.currentCueCommitted || beat == null) return;
        // A frontend may save synchronously from a presentation event. Commit
        // this receipt before dispatch so that save cannot reissue the cue.
        State.currentCueCommitted = true;
        if (!string.IsNullOrEmpty(beat.theatreStateAfterText) && !presentation.Goto(beat.theatreStateAfterText))
        {
            State.currentCueCommitted = false;
            SetContentError("Theatre cue " + beat.theatreStateAfterText + " is invalid from " + presentation.CurrentState);
        }
    }

    public bool TryCompleteCurrentText()
    {
        if (!IsAvailable || State?.completed != false || State.currentTextCompleted || messages == null) return false;
        return CurrentEntry?.PartType == OSCharacterInteractionPartType.TextWithOptions
            ? messages.TryCompleteDisplayedOptionTextImmediately(State.currentEntryId)
            : messages.TryCompleteDisplayedTextImmediately(State.currentEntryId);
    }

    public bool TryContinue()
    {
        if (!CanContinue) return false;
        var beat = Content.Find(State.currentEntryId);
        if (beat == null) return false;
        if (TryBeginMotherHeartClose()) return true;
        string finished = State.currentEntryId;
        AdvanceTo(beat.nextEntryId);
        messages?.FinishChapter04StoryEntry(finished);
        return true;
    }

    public bool TrySelectOption(string optionId)
    {
        if (!IsAvailable || State?.completed != false || !State.currentTextCompleted
            || !State.currentCueCommitted || !State.currentResourcesCommitted || IsOptionGestureBlocked
            || CurrentEntry?.PartType != OSCharacterInteractionPartType.TextWithOptions || messages == null) return false;
        return messages.TrySelectDisplayedOptionImmediately(OSCharacterInteractionIds.GlobalSequence,
            State.currentEntryId, optionId);
    }

    void OnOptionSelected(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (sequence != OSCharacterInteractionIds.GlobalSequence || entry == null || option == null
            || !CanPresentEntry(entry.EntryId) || !State.currentTextCompleted
            || !State.currentCueCommitted || !State.currentResourcesCommitted || IsOptionGestureBlocked) return;
        var beat = Content.Find(entry.EntryId);
        if (beat == null) return;
        foreach (var route in beat.options)
        {
            if (route == null || route.optionId != option.OptionId) continue;
            RecordCurrentLine().optionId = option.OptionId;
            // The old message callback still has cleanup to do. Store the new
            // position here; Update presents it only on the following frame.
            AdvanceTo(route.nextEntryId);
            return;
        }
    }

    void AdvanceTo(string nextEntryId)
    {
        State.currentReadElapsed = 0f;
        optionReleasePending = false; optionBlockedFrame = Time.frameCount;
        if (string.IsNullOrWhiteSpace(nextEntryId))
        {
            State.completed = true;
            if (Content.Find(State.currentEntryId)?.endAction == OSChapter04StoryContent.FinalHeartHealAction)
                QueueFinalTreatment();
        }
        else
        {
            State.currentEntryId = nextEntryId; State.currentVisit++;
            State.currentTextCompleted = State.currentCueCommitted = false;
            State.currentResourcesPrepared = State.currentResourcesCommitted = false;
        }
        ProgressChanged?.Invoke();
    }

    void GuardCurrentGesture()
    {
        var mouse = Mouse.current?.leftButton;
        if (mouse != null && (mouse.isPressed || mouse.wasPressedThisFrame))
        { optionReleasePending = mouse.isPressed; optionBlockedFrame = Time.frameCount; }
    }

    void OnPresentationStateChanged(string stateId)
    {
        if (!IsActive || State == null || !ReferenceEquals(observed, State)) return;
        State.theatreState = stateId;
        ProgressChanged?.Invoke();
    }

    public void NotifyDialogueDismissed()
    {
        if (!IsActive || State?.started != true || !ReferenceEquals(observed, State)
            || OSSessionPersistence.IsRestoring) return;
        State.dialogueSuspended = true;
        presentation?.SetPaused(true);
        ProgressChanged?.Invoke();
    }

    void OnChatLayoutChanged(OSWindowUnit window)
    {
        if (!IsActive || State?.started != true || !ReferenceEquals(observed, State)
            || OSSessionPersistence.IsRestoring || window != chatWindow || HasAttachedDialogue) return;
        if (window.IsMinimized) NotifyDialogueDismissed();
        else if (State.dialogueSuspended && window.IsOpen && !window.IsClosing && window.gameObject.activeInHierarchy)
        { State.dialogueSuspended = false; ProgressChanged?.Invoke(); }
    }

    void OnLiveOpened(OSWindowUnit window)
    {
        if (!OSSessionPersistence.IsRestoring) ResumeDialogue();
    }

    void OnLiveClosed(OSWindowUnit window) => NotifyDialogueDismissed();

    void OnLiveLayoutChanged(OSWindowUnit window)
    {
        if (!HasAttachedDialogue || !IsActive || State?.started != true
            || !ReferenceEquals(observed, State) || OSSessionPersistence.IsRestoring) return;
        if (window.IsMinimized) NotifyDialogueDismissed();
        else if (State.dialogueSuspended && window.IsOpen && !window.IsClosing
            && window.gameObject.activeInHierarchy) ResumeDialogue();
    }

    /// <summary>Call only for an explicit user opening of the private chat.</summary>
    public void ResumeDialogue()
    {
        if (!IsActive || State?.started != true || !ReferenceEquals(observed, State)
            || OSSessionPersistence.IsRestoring) return;
        State.dialogueSuspended = false;
        if (chatWindow?.IsMinimized == true) chatWindow.RestoreAndBringToFront();
        if (!HasContent && !externalDialogueLayout && Ready(liveWindow)) messages?.OpenChapter04DialogueShell(this);
        ProgressChanged?.Invoke();
    }

    /// <summary>The custom chat frontend may take layout ownership explicitly.</summary>
    public void ReleaseDialogueLayout()
    {
        externalDialogueLayout = true;
        messages?.WindowLayout?.SetTemporaryLeftDialogue(this, false);
        ownsDialogueLayout = false;
    }

    public string GetLineText(string entryId)
    {
        var data = GameDataManager.Instance?.OSCharacterInteractionData;
        return data != null && data.TryGetGlobalEntry(entryId, out var entry)
            ? Localize(entry.LocalizationKey, entry.FallbackText) : string.Empty;
    }
    public string GetDateText(string entryId)
    {
        Content.GetDate(entryId, out string key, out string fallback);
        return Localize(key, fallback);
    }
    public string GetDateKey(string entryId)
    {
        Content.GetDate(entryId, out string key, out string fallback);
        return !string.IsNullOrEmpty(key) ? key : fallback ?? string.Empty;
    }
    public string GetOptionText(string entryId, string optionId)
    {
        var data = GameDataManager.Instance?.OSCharacterInteractionData;
        if (data != null && data.TryGetGlobalEntry(entryId, out var entry))
            foreach (var option in entry.Options)
                if (option != null && option.OptionId == optionId) return Localize(option.LocalizationKey, option.FallbackText);
        // Old saves can still show these retired replies in their chat history.
        // Their punctuation is language-independent; neither is a selectable route now.
        if (optionId == "silence" && (entryId == "chapter04_story_29" || entryId == "chapter04_story_39")) return "....";
        return string.Empty;
    }
    static string Localize(string key, string fallback) => string.IsNullOrEmpty(key)
        ? fallback ?? string.Empty : GameDataManager.Instance?.GetLocalizedText(key, fallback) ?? fallback ?? string.Empty;

    /// <summary>Clear runtime ownership only; never rewind the saved story.</summary>
    public void ResetPresentation()
    {
        ResetMotherGiftPresentation();
        ReleaseLiveEmotion();
        finalTreatment?.Stop();
        finalTreatmentRequestQueued = false;
        ClearGriefPresentation();
        griefRestorePending = true;
        griefEntryId = null;
        ReleaseFinalTreatmentBackground();
        chatView?.ReleaseView();
        ReleaseConversationTVLayout();
        messages?.ClearChapter04StoryPresentation();
        messages?.WindowLayout?.SetTemporaryLeftDialogue(this, false);
        ownsDialogueLayout = false;
        externalDialogueLayout = false;
        presentation?.Unbind();
        observed = null;
        optionReleasePending = false; optionBlockedFrame = -1;
    }

#if UNITY_EDITOR
    public void DebugSetContent(OSChapter04StoryContent content)
    { debugContent = content; checkedContent = null; checkedInteractions = null; }
    public void DebugSetFocus(bool focused) => debugIgnoreFocus = focused;
    public void DebugStep() => Tick();
#endif
}
