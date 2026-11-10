using UnityEngine;
using UnityEngine.InputSystem;

// Owns the opening interrogation and both between-test cage conversations.
// Appended story IDs preserve existing save checkpoints.
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public sealed class OSCursorExperimentStoryDirector : MonoBehaviour
{
    const string EntryPrefix = "cursor_test_ch02_";
    const string SignalPrefix = "cursor_test.story.";
    // Appended IDs keep old saves' 6 (rules) and 7 (complete) unchanged.
    public const int SafetyQuestionStep = 8, SafetyReplyStep = 9;
    public const int SecondTestQuestionStep = 10, SecondTestReplyStep = 11,
        SecondTestRulesStep = 12, SecondTestReleasedStep = 13;
    public const int ThirdTestReturnStep = 14, ThirdTestQuestionStep = 15, ThirdTestReplyStep = 16,
        ThirdTestPromiseStep = 17, ThirdTestRulesStep = 18, ThirdTestReleasedStep = 19;
    public const int AssistanceStep = 20;
    public const float AssistanceWaitSeconds = 5f;
    public const string AssistanceEntryId = EntryPrefix + "assisted_release";
    public const int PersistenceReactionStep = 21;
    public const float PersistenceReactionWaitSeconds = 4f;
    public const string PersistenceReactionEntryId = EntryPrefix + "persistence";
    bool IsAssistanceStep => Step == AssistanceStep;
    bool IsPersistenceReactionStep => Step == PersistenceReactionStep;
    bool IsTimedMessage => IsAssistanceStep || IsPersistenceReactionStep;
    readonly OSCursorShakeRecognizer gestures = new OSCursorShakeRecognizer();
    OSLinkMessageController messages;
    OSWindowUnit live;
    OSWindowUnit settings;
    OSCursorExperimentAnswerView answerView;
    OSCursorExperimentLiveReaction liveReaction;
    OSMemoryProcessSaveData observedSave;
    string messageId;
    bool messageTyped, hasFocus = true, inputWarmup = true;
    bool clickArmed;
    int lastHandledClickFrame = -1, textCompletedFrame = -1;

    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    OSCursorExperimentController Experiment => OS_Handle.Instance?.CursorExperiment;
    public int Step => State?.chapter02CursorStoryStep ?? 0;
    public int Answer => State?.chapter02CursorStoryAnswer ?? 0;
    public int SafetyAnswer => State?.chapter02CursorStorySafetyAnswer ?? 0;
    public int SecondTestAnswer => State?.chapter02CursorStorySecondTestAnswer ?? 0;
    public int ThirdTestAnswer => State?.chapter02CursorStoryThirdTestAnswer ?? 0;
    bool IsSecondTestStory => Step >= SecondTestQuestionStep && Step <= SecondTestRulesStep;
    bool IsThirdTestStory => Step >= ThirdTestReturnStep && Step <= ThirdTestRulesStep;
    bool IsGestureQuestion => Step == 3 || Step == SafetyQuestionStep || Step == SecondTestQuestionStep || Step == ThirdTestQuestionStep;
    bool IsRulesStep => Step == 6 || Step == SafetyReplyStep;
    int DisplayedAnswer => Step == ThirdTestQuestionStep ? ThirdTestAnswer
        : Step == SecondTestQuestionStep ? SecondTestAnswer : Step == SafetyQuestionStep ? SafetyAnswer : Answer;
    bool CanContinue => !IsTimedMessage && messageTyped && (!IsGestureQuestion || Step == 3 || DisplayedAnswer != 0);
    public float Elapsed => State?.chapter02CursorStoryElapsed ?? 0f;
    public bool IsStoryProtected => isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 2
        && State?.chapter02ClawCursorCaptured == true
        && ((Step >= 1 && Step <= 6) || Step == SafetyQuestionStep || Step == SafetyReplyStep || IsSecondTestStory || IsThirdTestStory
            || (IsAssistanceStep && State.chapter02CursorExperimentRound == 3 && State.chapter02CursorExperimentAssisted)
            || (IsPersistenceReactionStep && State.chapter02CursorExperimentRound == 3));
    public bool IsMessageProtected => IsStoryProtected && OwnMessageDisplayed;
    bool OwnMessageDisplayed => messages != null && IsOwnEntry(messages.DisplayedEntryId)
        && messages.LinkWindow != null && messages.LinkWindow.IsOpen
        && !(messages.DisplayedEntryId == AssistanceEntryId && State?.chapter02CursorExperimentRound >= 4);
    static bool IsOwnEntry(string id) => !string.IsNullOrEmpty(id) && id.StartsWith(EntryPrefix, System.StringComparison.Ordinal);

    void OnEnable()
    {
        hasFocus = Application.isFocused;
        ResolveReferences();
    }

    void ResolveReferences()
    {
        var next = OS_Handle.Instance?.LinkMessageController;
        if (messages != next)
        {
            if (messages != null) messages.EntryTextPlaybackCompleted -= OnTextCompleted;
            messages = next;
            if (messages != null) messages.EntryTextPlaybackCompleted += OnTextCompleted;
        }
        if (live == null) live = OS_Handle.Instance?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settings == null) settings = OS_Handle.Instance?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (answerView == null) answerView = GetComponent<OSCursorExperimentAnswerView>();
        if (answerView == null) answerView = gameObject.AddComponent<OSCursorExperimentAnswerView>();
        if (liveReaction == null) liveReaction = GetComponent<OSCursorExperimentLiveReaction>();
        if (liveReaction == null) liveReaction = gameObject.AddComponent<OSCursorExperimentLiveReaction>();
    }

    void OnDisable()
    {
        ResetPresentation();
        if (messages != null) messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages = null;
    }

    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        gestures.Reset();
        inputWarmup = true;
        if (!focused) PauseMessage();
    }

    void ObserveSave()
    {
        if (ReferenceEquals(observedSave, State)) return;
        ResetPresentation();
        observedSave = State;
    }

    void InitializeProgress()
    {
        var p = State;
        if (p == null || GameDataManager.Instance?.CurrentChapterIndex != 2 || !p.chapter02ClawCursorCaptured) return;
        p.chapter02CursorStoryStep = Mathf.Clamp(p.chapter02CursorStoryStep, 0, PersistenceReactionStep);
        p.chapter02CursorStoryAnswer = Mathf.Clamp(p.chapter02CursorStoryAnswer, 0, 2);
        p.chapter02CursorStorySafetyAnswer = Mathf.Clamp(p.chapter02CursorStorySafetyAnswer, 0, 2);
        p.chapter02CursorStorySecondTestAnswer = Mathf.Clamp(p.chapter02CursorStorySecondTestAnswer, 0, 2);
        p.chapter02CursorStoryThirdTestAnswer = Mathf.Clamp(p.chapter02CursorStoryThirdTestAnswer, 0, 2);
        // Earlier saves used an after-typing timer. Preserve an already attached
        // ball, but never let that obsolete timer advance a manual conversation.
        if (p.chapter02CursorStoryStep >= 6 || (p.chapter02CursorStoryStep == 5 && p.chapter02CursorStoryElapsed > 0f))
            p.chapter02CursorStoryTetherAttached = true;
        if (p.chapter02CursorExperimentAssisted && p.chapter02CursorExperimentRound == 3)
            p.chapter02CursorStoryStep = AssistanceStep;
        p.chapter02CursorStoryElapsed = IsAssistanceStep
            ? Mathf.Clamp(p.chapter02CursorStoryElapsed, 0f, AssistanceWaitSeconds)
            : IsPersistenceReactionStep ? Mathf.Clamp(p.chapter02CursorStoryElapsed, 0f, PersistenceReactionWaitSeconds) : 0f;
        if (p.chapter02CursorStoryStep != 0) return;
        bool alreadyTesting = p.chapter02CursorExperimentRound > 1
            || (p.chapter02CursorExperimentRound == 1
                && p.chapter02CursorExperimentPhase != (int)OSCursorExperimentController.ExperimentPhase.Intake
                && p.chapter02CursorExperimentPhase != (int)OSCursorExperimentController.ExperimentPhase.Inactive);
        p.chapter02CursorStoryStep = alreadyTesting ? 7 : 1;
        p.chapter02CursorStoryElapsed = 0;
    }

    public void BeginAssistance()
    {
        ResolveReferences();
        ObserveSave();
        if (State == null || Experiment?.IsAssistancePending != true) return;
        State.chapter02CursorStoryStep = AssistanceStep;
        State.chapter02CursorStoryElapsed = 0f;
        ApplyStoryHold();
    }

    // Called only on the sixth real collision, so closing or loading cannot replay it.
    public void BeginPersistenceReaction()
    {
        ResolveReferences();
        ObserveSave();
        if (State == null || Experiment?.Round != 3 || Experiment.IsAssistancePending
            || State.chapter02CursorExperimentThirdFailures != OSCursorExperimentController.PersistenceReactionThreshold) return;
        State.chapter02CursorStoryStep = PersistenceReactionStep;
        State.chapter02CursorStoryElapsed = 0f;
        ApplyStoryHold();
    }

    // Called immediately after Begin, before the experiment can tick a frame.
    public void ApplyStoryHold()
    {
        ObserveSave();
        InitializeProgress();
        messages?.MaintainCursorExperimentMessageReservation();
        var c = Experiment;
        if (c == null || !c.IsActive) return;
        bool waitingForRules = IsRulesStep
            && (c.Phase == OSCursorExperimentController.ExperimentPhase.Intake
                || c.Phase == OSCursorExperimentController.ExperimentPhase.RoundIntro);
        c.IsFirstTestStoryHolding = IsStoryProtected && !IsTimedMessage && !IsSecondTestStory && !IsThirdTestStory && (!IsRulesStep || waitingForRules);
        c.ShowCageStoryWeight = IsStoryProtected && (Step == 5 || Step == SafetyQuestionStep
                || Step == SecondTestQuestionStep || Step == SecondTestReplyStep)
            && State.chapter02CursorStoryTetherAttached;
        c.ShowCageGestureHint = IsStoryProtected && IsGestureQuestion && messageTyped && Available;
        if (IsStoryProtected && IsRulesStep) c.OpenFirstTestForStory();
        if (IsStoryProtected && Step == SecondTestRulesStep) c.OpenSecondTestForStory();
        if (IsStoryProtected && Step == ThirdTestRulesStep) c.OpenThirdTestForStory();
    }

    bool Available => hasFocus && OS_Handle.Instance?.IsOSVisible == true
        && OS_Handle.Instance?.IsCursorExperimentInteriorBlockingMessages != true
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && Experiment?.IsSettingsOpen != true
        && !(settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy)
        && OS_Handle.Instance?.IsSystemInputLockedWithoutCursorExperiment != true;

    void Update()
    {
        ResolveReferences();
        ApplyStoryHold();
        if (!IsStoryProtected) { PauseMessage(); return; }
        if (!Available) { PauseMessage(); return; }
        TickStory(Mathf.Min(Time.unscaledDeltaTime, .05f), CursorHandle.Instance != null ? CursorHandle.Instance.RawMouseDelta : Vector2.zero);
        var mouse = Mouse.current;
        HandleClick(mouse != null && mouse.leftButton.wasPressedThisFrame,
            mouse != null && mouse.leftButton.isPressed, Time.frameCount);
    }

    void LateUpdate()
    {
        // Run after OSWindowUnit's forced-topmost LateUpdate. Same window stack;
        // never move Live's saved position or override the settings modal canvas.
        var c = Experiment;
        var interior = OS_Handle.Instance?.CursorExperimentInterior;
        if (interior != null && interior.IsEngaged && live != null && live.IsOpen
            && !live.IsMinimized && live.gameObject.activeInHierarchy && c != null
            && live.transform.parent == c.transform.parent)
        {
            // The entire Live subtree, including its LinkMessage, belongs in
            // front of the forced-topmost observation window in this screen.
            // This also applies during Live's entrance and between two lines.
            // Settings retain their separately sorted modal Canvas above both.
            if (live.transform.GetSiblingIndex() < c.transform.GetSiblingIndex())
                live.transform.SetSiblingIndex(c.transform.GetSiblingIndex());
            return;
        }
        if (!OwnMessageDisplayed || !Available || live == null || c == null || !c.IsActive) return;
        if (live.transform.parent == c.transform.parent && live.transform.GetSiblingIndex() < c.transform.GetSiblingIndex())
            live.transform.SetSiblingIndex(c.transform.GetSiblingIndex());
    }

    string Suffix()
    {
        switch (Step)
        {
            case AssistanceStep: return "assisted_release";
            case PersistenceReactionStep: return "persistence";
            case 1: return "caught";
            case 2: return "study";
            case 3: return "question";
            case 4: return Answer == 1 ? "not_bug" : Answer == 2 ? "human" : "silent";
            case 5: return "tether";
            case 6: return "first_test";
            case SafetyQuestionStep: return "safety_question";
            case SafetyReplyStep: return SafetyAnswer == 1 ? "safety_deny" : "safety_confirm";
            case SecondTestQuestionStep: return "second_question";
            case SecondTestReplyStep: return SecondTestAnswer == 1 ? "second_deny" : "second_confirm";
            case SecondTestRulesStep: return "second_test";
            case ThirdTestReturnStep: return "third_return";
            case ThirdTestQuestionStep: return "third_question";
            case ThirdTestReplyStep: return ThirdTestAnswer == 1 ? "third_deny" : "third_confirm";
            case ThirdTestPromiseStep: return "third_promise";
            case ThirdTestRulesStep: return "third_test";
            default: return null;
        }
    }

    void TickStory(float dt, Vector2 rawDelta)
    {
        // The interior owns the pan, cage deposit and Live entrance. Keep the
        // mainline protected, but never reopen Live or emit a line ahead of it.
        if (OS_Handle.Instance?.IsCursorExperimentInteriorBlockingMessages == true) return;
        if (messages == null || !EnsureLiveVisible()) return;
        var c = Experiment;
        if (Step >= 3 && (c == null || !c.IsActive)) return;
        // Let the claw leave and the gate close before speaking again.
        if ((IsSecondTestStory || IsThirdTestStory) && c.Phase == OSCursorExperimentController.ExperimentPhase.Recapture
            && c.PhaseTime < 2.45f) return;
        if (Step == ThirdTestRulesStep && c.IsThirdTestPointerFaultActive) return;
        if (((Step >= 3 && Step <= 5) || Step == SafetyQuestionStep) && !c.IsPointerFreeInCage) return;
        if (IsRulesStep && c.Phase == OSCursorExperimentController.ExperimentPhase.Intake) return;
        string wantedId = EntryPrefix + Suffix();
        if (messageId != wantedId)
        {
            CloseOwnMessage();
            messageId = wantedId;
            messageTyped = false;
            clickArmed = false;
            inputWarmup = true;
            gestures.Reset();
        }
        if (!OwnMessageDisplayed || messages.DisplayedEntryId != messageId)
        {
            var state = GameDataManager.Instance.OSCharacterInteractionState;
            if (messages.IsPresenting || messages.IsWaitingForOption || messages.IsWaitingForExternalCompletion
                || messages.GlobalMessagesSuppressed || !string.IsNullOrEmpty(state.pendingGlobalEntryId)
                || !string.IsNullOrEmpty(state.presentingGlobalEntryId)
                || (messages.LinkWindow != null && (messages.LinkWindow.IsOpen || messages.LinkWindow.IsClosing))) return;
            state.SetGlobalEntryConsumed(messageId, false);
            messageTyped = false;
            messages.NotifyGlobalExternalSignal(SignalPrefix + Suffix());
            return;
        }
        if (IsTimedMessage)
        {
            TickTimedMessage(dt);
            return;
        }
        // Reserve the footer while the line is typing; revealing the arrow must
        // not resize the message underneath the reader.
        answerView.Show(messages, DisplayedAnswer, IsGestureQuestion, CanContinue);
        if (messages.LinkWindow.IsOpening || messages.LinkWindow.IsMinimized || !messages.LinkWindow.gameObject.activeInHierarchy) return;
        liveReaction?.PlayReaction(Suffix());
        // A restored global text can finish before the director is rebound.
        if (!messageTyped && !messages.IsPresenting) MarkTextCompleted();
        if (!messageTyped) return;
        if (IsGestureQuestion)
        {
            if (c.IsPaused) { gestures.Reset(); inputWarmup = true; return; }
            int result = inputWarmup ? 0 : gestures.Push(rawDelta, dt);
            inputWarmup = false;
            if (result != 0)
            {
                if (result != DisplayedAnswer)
                    Chapter02Audio.Play("Chapter02_ChoiceConfirm", .54f, result == 1 ? .95f : 1.05f, .25f);
                if (Step == SafetyQuestionStep) State.chapter02CursorStorySafetyAnswer = result;
                else if (Step == SecondTestQuestionStep) State.chapter02CursorStorySecondTestAnswer = result;
                else if (Step == ThirdTestQuestionStep) State.chapter02CursorStoryThirdTestAnswer = result;
                else State.chapter02CursorStoryAnswer = result;
            }
        }
        answerView.Show(messages, DisplayedAnswer, IsGestureQuestion, CanContinue);
    }

    void TickTimedMessage(float dt)
    {
        float waitSeconds = IsAssistanceStep ? AssistanceWaitSeconds : PersistenceReactionWaitSeconds;
        answerView.Hide();
        if (messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized
            || !messages.LinkWindow.gameObject.activeInHierarchy) return;
        // A paused or restored countdown retains its elapsed time and its fully read line.
        if (Elapsed > 0f && messages.IsPresenting && !messages.IsDisplayedTextComplete)
            messages.TryCompleteDisplayedTextImmediately(messageId);
        if (messages.IsPresenting && !messages.IsDisplayedTextComplete)
        {
            messageTyped = false;
            messages.SetStoryWaitLineProgress(messageId, -1f);
            return;
        }
        if (!messageTyped) MarkTextCompleted();
        State.chapter02CursorStoryElapsed = Mathf.Min(waitSeconds, Elapsed + dt);
        messages.SetStoryWaitLineProgress(messageId, 1f - Elapsed / waitSeconds);
        if (Elapsed < waitSeconds) return;
        if (IsPersistenceReactionStep)
        {
            CloseOwnMessage();
            State.chapter02CursorStoryStep = ThirdTestReleasedStep;
            State.chapter02CursorStoryElapsed = 0f;
            messageId = null;
            messageTyped = false;
            ApplyStoryHold();
            return;
        }
        // Hand the fully read message to the release director. Its red cursor
        // closes this same window after the maze falls, without replaying a victory line.
        messages.SetStoryWaitLineProgress(AssistanceEntryId, -1f);
        Experiment.CompleteAssistance();
        messageId = null;
        messageTyped = false;
    }

    void HandleClick(bool pressed, bool held, int frame)
    {
        if (!Available || !IsStoryProtected || !OwnMessageDisplayed || messages.DisplayedEntryId != messageId
            || messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized
            || CursorHandle.Instance?.IsPointerInputSuppressed != true)
        { clickArmed = false; return; }
        // Resume/opening clicks are not buffered. Require a released button
        // before a new press, without re-enabling the physical-position UI module.
        if (!held) clickArmed = true;
        if (!pressed || !clickArmed || lastHandledClickFrame == frame) return;
        clickArmed = false;
        lastHandledClickFrame = frame;
        if (!messageTyped)
        {
            messages.TryCompleteDisplayedTextImmediately(messageId);
            return;
        }
        if (textCompletedFrame == frame) return;
        if (!CanContinue) return;
        if (IsRulesStep) Experiment?.ReleaseFirstTestForStory();
        if (Step == SecondTestRulesStep) Experiment?.ReleaseSecondTestForStory();
        if (Step == ThirdTestRulesStep) Experiment?.ReleaseThirdTestForStory();
        Advance();
    }

    void OnTextCompleted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (entry != null && entry.EntryId == messageId && IsStoryProtected && ReferenceEquals(observedSave, State))
            MarkTextCompleted();
    }

    void MarkTextCompleted()
    {
        if (messageTyped) return;
        messageTyped = true;
        textCompletedFrame = Time.frameCount;
        if (IsTimedMessage)
        {
            answerView.Hide();
            float waitSeconds = IsAssistanceStep ? AssistanceWaitSeconds : PersistenceReactionWaitSeconds;
            messages.SetStoryWaitLineProgress(messageId, 1f - Elapsed / waitSeconds);
            return;
        }
        if (IsGestureQuestion)
        {
            gestures.Reset();
            inputWarmup = true;
            answerView.Show(messages, DisplayedAnswer, true, CanContinue);
            Experiment.ShowCageGestureHint = true;
        }
        if (Step == 5)
        {
            if (!State.chapter02CursorStoryTetherAttached)
                Chapter02Audio.Play("Chapter02_TetherAttach", .7f);
            State.chapter02CursorStoryTetherAttached = true;
            Experiment.ShowCageStoryWeight = true;
            messages.StartDisplayedTrailingDotsLoop(messages.DisplayedSequenceId, messageId, .35f);
        }
        answerView.Show(messages, DisplayedAnswer, IsGestureQuestion, CanContinue);
    }

    void Advance()
    {
        CloseOwnMessage();
        State.chapter02CursorStoryStep = IsSecondTestStory || IsThirdTestStory ? Step + 1 : Step == 5 ? SafetyQuestionStep
            : Step == SafetyQuestionStep ? SafetyReplyStep
            : Step == SafetyReplyStep ? 7 : Mathf.Min(7, Step + 1);
        State.chapter02CursorStoryElapsed = 0f;
        messageId = null;
        messageTyped = false;
        clickArmed = false;
        gestures.Reset();
        inputWarmup = true;
        ApplyStoryHold();
    }

    bool EnsureLiveVisible()
    {
        if (live != null && live.IsOpen && !live.IsMinimized && !live.IsOpening && !live.IsClosing && live.gameObject.activeInHierarchy) return true;
        if (live != null && !live.IsOpening && !live.IsClosing) OS_Handle.Instance?.OpenCharacterWindowForReconnect(.15f);
        return false;
    }

    void CloseOwnMessage()
    {
        messages?.SetStoryWaitLineProgress(AssistanceEntryId, -1f);
        messages?.SetStoryWaitLineProgress(PersistenceReactionEntryId, -1f);
        answerView?.Hide();
        if (Experiment != null) Experiment.ShowCageGestureHint = false;
        if (!OwnMessageDisplayed) return;
        messages.StopDisplayedTrailingDotsLoop(false);
        messages.LinkWindow.CloseFromScript();
    }

    void PauseMessage()
    {
        liveReaction?.PausePresentation();
        CloseOwnMessage();
        messageTyped = false;
        clickArmed = false;
        gestures.Reset();
        inputWarmup = true;
    }

    public void ResetPresentation()
    {
        liveReaction?.ResetPresentation();
        CloseOwnMessage();
        messageId = null;
        messageTyped = false;
        clickArmed = false;
        lastHandledClickFrame = textCompletedFrame = -1;
        gestures.Reset();
        inputWarmup = true;
        if (Experiment != null)
        {
            Experiment.IsFirstTestStoryHolding = false;
            Experiment.ShowCageStoryWeight = false;
            Experiment.ShowCageGestureHint = false;
        }
        observedSave = null;
    }

#if UNITY_EDITOR
    // Drives the actual timing/gesture logic in paused-editor validation.
    public void DebugStep(Vector2 rawDelta, float seconds)
    {
        ResolveReferences();
        ApplyStoryHold();
        if (IsStoryProtected) TickStory(Mathf.Clamp(seconds, 0f, .1f), rawDelta);
    }

    public void DebugPointerButton(bool pressed, bool held, int frame) => HandleClick(pressed, held, frame);
#endif
}
