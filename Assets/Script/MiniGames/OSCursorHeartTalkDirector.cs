using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Saved mainline conversation after the cursor tests. Presentation is deliberately
/// separate: this component owns choices, message reservations and progression only.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(595)]
public sealed partial class OSCursorHeartTalkDirector : MonoBehaviour
{
    // Save protocol: append future states; never reorder these values.
    public enum HeartTalkStep
    {
        Inactive = 0, Seat = 1, Ask = 2, AskWait = 3, AskAgain = 4,
        AskWait2 = 5, AskLast = 6, Scroll = 7, ScrollReply = 8,
        Angry = 9, Fix = 10, FixReply = 11, FixClose = 12,
        // Memory / MemoryReply are retired save values; old saves advance to Quake.
        Identity = 13, IdentityReply = 14, Memory = 15, MemoryReply = 16,
        Quake = 17, QuakeLine = 18, Loading = 19, Return = 20, Done = 21
    }

    public const string EntryPrefix = "heart_talk_ch02_";
    public const string SignalPrefix = "heart_talk.story.";
    const float SeatSeconds = 1.2f, WaitSeconds = 3f, QuakeSilenceSeconds = 1f;
    const float LoadingSeconds = 5f, LoadingLineSeconds = 3f;
    // The one persisted timer also records whether the wait line was confirmed.
    // Zero = still reading; epsilon + seconds = silent wait (including its first frame).
    const float WaitConfirmed = .0001f;

    OSLinkMessageController messages;
    OSCursorHeartTalkPresentation presentation;
    OSWindowUnit live, settings;
    OSMemoryProcessSaveData observed;
    HeartTalkStep renderedStep = (HeartTalkStep)(-1);
    bool hasFocus = true, skipAdvance = true, messageTyped, clickArmed;
    bool restorePending = true, quakeTriggered, loadingLineFinished, returnNotified;
    float elapsed, tipTime, loadingLineElapsed;
    int completedFrame = -1, lastClickFrame = -1, releaseStableFrame = -1;
    string messageId;

    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    public HeartTalkStep Step => (HeartTalkStep)(State?.chapter02HeartTalkStep ?? 0);
    public bool IsEngaged => isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 2
        && Step >= HeartTalkStep.Seat && Step < HeartTalkStep.Done;
    public bool IsStoryProtected => IsEngaged;
    public bool IsMessageProtected => IsEngaged && OwnMessageDisplayed;
    public string ReservedEntryId
    {
        get
        {
            if (!IsEngaged) return null;
            // A saved LinkMessage may try to resume before the presentation has
            // restarted. Keep its slot empty until the loading reveal is ready.
            if (Step == HeartTalkStep.Loading && (restorePending || !LoadingReadyForLine)) return string.Empty;
            string suffix = Suffix();
            return string.IsNullOrEmpty(suffix) ? string.Empty : EntryPrefix + suffix;
        }
    }
    bool LoadingReadyForLine => presentation != null ? presentation.IsLoadingReadyForLine : elapsed >= 1.2f;
    public bool IsWaitingSilently => IsWaitStep && State?.chapter02HeartTalkWaitElapsed > 0f;
    public float WaitElapsed => Mathf.Max(0f, (State?.chapter02HeartTalkWaitElapsed ?? 0f) - WaitConfirmed);
    bool IsWaitStep => Step == HeartTalkStep.AskWait || Step == HeartTalkStep.AskWait2;
    bool HasOptions => Step == HeartTalkStep.Ask || Step == HeartTalkStep.AskAgain || Step == HeartTalkStep.AskLast
        || Step == HeartTalkStep.Scroll || Step == HeartTalkStep.Fix || Step == HeartTalkStep.Identity
        || Step == HeartTalkStep.QuakeLine;
    bool HasManualLine => !HasOptions && Step != HeartTalkStep.Loading && !string.IsNullOrEmpty(Suffix());
    bool OwnMessageDisplayed => messages != null && messages.LinkWindow != null && messages.LinkWindow.IsOpen
        && IsOwnEntry(messages.DisplayedEntryId);
    bool AmbientAvailable => hasFocus && OS_Handle.Instance?.IsOSVisible == true && !OSSessionPersistence.IsRestoring
        && GameSessionFlow.Instance?.IsBusy != true && !GameTestConsole.BlocksGameplayInput
        && !(settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy)
        && OS_Handle.Instance?.IsSystemInputLockedWithoutCursorExperiment != true;
    bool LiveAvailable => live != null && live.IsOpen && !live.IsMinimized && !live.IsOpening
        && !live.IsClosing && live.gameObject.activeInHierarchy;
    bool Available => AmbientAvailable && (Step >= HeartTalkStep.Return
        || (LiveAvailable && (Step >= HeartTalkStep.Loading
            || OS_Handle.Instance?.CursorExperimentInterior?.IsReadyForStory == true)));

    static bool IsOwnEntry(string id) => !string.IsNullOrEmpty(id)
        && id.StartsWith(EntryPrefix, System.StringComparison.Ordinal);

    void OnEnable() { hasFocus = Application.isFocused; Resolve(); }
    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        skipAdvance = true;
        clickArmed = false;
        if (!focused) PauseMessage();
    }
    void OnDisable() { ResetPresentation(); Unbind(); }
    void OnDestroy() { ResetPresentation(); Unbind(); DestroyContinueTip(); }

    void Resolve()
    {
        var next = OS_Handle.Instance?.LinkMessageController;
        if (messages != next)
        {
            Unbind(); messages = next;
            if (messages != null)
            {
                messages.EntryPresentationStarted += OnPresentationStarted;
                messages.EntryTextPlaybackCompleted += OnTextCompleted;
                messages.OptionSelected += OnOptionSelected;
            }
        }
        if (presentation == null) presentation = GetComponent<OSCursorHeartTalkPresentation>();
        if (live == null) live = OS_Handle.Instance?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settings == null) settings = OS_Handle.Instance?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
    }

    void Unbind()
    {
        if (messages == null) return;
        messages.EntryPresentationStarted -= OnPresentationStarted;
        messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages.OptionSelected -= OnOptionSelected;
        messages = null;
    }

    void ObserveSave()
    {
        if (ReferenceEquals(observed, State)) return;
        ResetPresentation();
        observed = State;
        observed?.EnsureData();
        restorePending = true;
    }

    /// <summary>Refresh after a checkpoint/load without ticking any story timer.</summary>
    public void Refresh()
    {
        Resolve(); ObserveSave();
        if (IsEngaged) messages?.MaintainCursorExperimentMessageReservation();
    }

    public void BeginAfterRelease()
    {
        Refresh();
        if (State == null || GameDataManager.Instance?.CurrentChapterIndex != 2 || Step != HeartTalkStep.Inactive
            || State.chapter02CursorReleaseStep != (int)OSCursorReleaseDirector.ReleaseStep.ConversationHold) return;
        State.chapter02HeartTalkWaitElapsed = 0f;
        SetStep(HeartTalkStep.Seat);
    }

    void TryStart()
    {
        bool ready = State != null && Step == HeartTalkStep.Inactive && GameDataManager.Instance?.CurrentChapterIndex == 2
            && State.chapter02CursorReleaseStep == (int)OSCursorReleaseDirector.ReleaseStep.ConversationHold
            && Available && OS_Handle.Instance?.CursorExperimentInterior?.IsReadyForStory == true;
        if (!ready) { releaseStableFrame = -1; return; }
        // The release director reaches ConversationHold earlier in this same
        // frame. Allow it to finish the stow and park the actor before taking over.
        if (releaseStableFrame < 0) { releaseStableFrame = Time.frameCount; return; }
        if (Time.frameCount != releaseStableFrame) BeginAfterRelease();
    }

    void Update()
    {
        Refresh(); TryStart();
        if (Step == HeartTalkStep.Done)
        {
            if (GameDataManager.Instance?.CurrentChapterIndex == 2 && AmbientAvailable && !returnNotified) RestoreFinishedDesktop();
            return;
        }
        if (!IsEngaged) { presentation?.PauseAudio(); return; }
        if (!Available) { PauseMessage(); return; }
        float dt = skipAdvance ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        skipAdvance = false;
        Tick(dt);
        var mouse = Mouse.current;
        HandleClick(mouse != null && mouse.leftButton.wasPressedThisFrame,
            mouse != null && mouse.leftButton.isPressed, Time.frameCount);
    }

    void Tick(float dt)
    {
        if (renderedStep != Step) EnterStep();
        messages?.WindowLayout?.SetTemporaryBottomDialogue(this, true);
        elapsed += dt; tipTime += dt;
        presentation?.AdvancePresentation(dt);

        if (Step == HeartTalkStep.Seat)
        {
            if (elapsed >= SeatSeconds) SetStep(HeartTalkStep.Ask);
        }
        else if (IsWaitingSilently)
        {
            State.chapter02HeartTalkWaitElapsed = Mathf.Min(WaitSeconds + WaitConfirmed,
                State.chapter02HeartTalkWaitElapsed + dt);
            if (WaitElapsed >= WaitSeconds - .00001f)
                SetStep(Step == HeartTalkStep.AskWait ? HeartTalkStep.AskAgain : HeartTalkStep.AskLast);
        }
        else if (Step == HeartTalkStep.Quake)
        {
            if (!quakeTriggered && elapsed >= QuakeSilenceSeconds)
            {
                quakeTriggered = true;
                presentation?.TriggerQuake();
            }
            if (quakeTriggered && (presentation == null || presentation.IsQuakeSettled)) SetStep(HeartTalkStep.QuakeLine);
        }
        else if (Step == HeartTalkStep.Loading) TickLoading(dt);
        else if (Step == HeartTalkStep.Return)
        {
            if (presentation == null || presentation.IsReturned) CompleteReturn();
        }
        else EnsureMessage();
        RenderContinueTip();
    }

    void EnterStep()
    {
        renderedStep = Step;
        elapsed = tipTime = loadingLineElapsed = 0f;
        clickArmed = false; completedFrame = -1;
        loadingLineFinished = false; quakeTriggered = false;
        if (restorePending)
        {
            presentation?.RestorePresentation((int)Step);
            // Loading intentionally replays; a restored quake begins with its
            // warning already present rather than repeating the initial impact.
            quakeTriggered = Step == HeartTalkStep.Quake || Step == HeartTalkStep.QuakeLine;
            restorePending = false;
            return;
        }
        if (Step == HeartTalkStep.Seat) presentation?.BeginBench();
        if (Step == HeartTalkStep.FixClose) presentation?.SetCalm(.5f);
        if (Step == HeartTalkStep.IdentityReply) presentation?.SetCalm(.8f);
        if (Step == HeartTalkStep.Quake) presentation?.SetCalm(1f);
        if (Step == HeartTalkStep.Loading) presentation?.BeginLoading();
        if (Step == HeartTalkStep.Return) presentation?.EndLoading();
    }

    void TickLoading(float dt)
    {
        bool ready = LoadingReadyForLine;
        if (!loadingLineFinished && ready && EnsureMessage())
        {
            loadingLineElapsed += dt;
            // This is the only automatic line. A long localization is allowed
            // to finish typing before it closes; focus/settings pause both clocks.
            if (loadingLineElapsed >= LoadingLineSeconds && messageTyped)
            {
                CloseOwnMessage();
                loadingLineFinished = true;
            }
        }
        if (elapsed >= LoadingSeconds && loadingLineFinished) SetStep(HeartTalkStep.Return);
    }

    void CompleteReturn()
    {
        CloseOwnMessage();
        State.chapter02HeartTalkStep = (int)HeartTalkStep.Done;
        State.chapter02HeartTalkWaitElapsed = 0f;
        renderedStep = HeartTalkStep.Done;
        returnNotified = true;
        messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
        // Persist Done before releasing the interior, so the release director
        // cannot reconstruct the hidden room during the desktop hand-off.
        OS_Handle.Instance?.CompleteChapter02HeartTalkReturn();
        OSChapter02ActivityMusic.NotifyReturnedToMainOS();
    }

    void RestoreFinishedDesktop()
    {
        presentation?.RestorePresentation((int)HeartTalkStep.Done);
        renderedStep = HeartTalkStep.Done;
        returnNotified = true;
        messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
        OS_Handle.Instance?.CompleteChapter02HeartTalkReturn();
        OSChapter02ActivityMusic.NotifyReturnedToMainOS();
    }

    void SetStep(HeartTalkStep next)
    {
        if (State == null) return;
        State.chapter02HeartTalkStep = (int)next;
        State.chapter02HeartTalkWaitElapsed = 0f;
        renderedStep = (HeartTalkStep)(-1);
        elapsed = loadingLineElapsed = 0f;
        restorePending = false; skipAdvance = true;
        clickArmed = messageTyped = false;
        messageId = null; completedFrame = -1;
    }

    string Suffix()
    {
        if (IsWaitingSilently) return null;
        switch (Step)
        {
            case HeartTalkStep.Ask: return "ask";
            case HeartTalkStep.AskWait: return "ask_wait";
            case HeartTalkStep.AskAgain: return "ask_again";
            case HeartTalkStep.AskWait2: return "ask_wait2";
            case HeartTalkStep.AskLast: return "ask_last";
            case HeartTalkStep.Scroll: return "scroll";
            case HeartTalkStep.ScrollReply: return State.chapter02HeartTalkScrollAnswer == 1 ? "scroll_reply_me" : "scroll_reply_not_me";
            case HeartTalkStep.Angry: return State.chapter02CursorReleaseChoice == 2 ? "angry_ignore" : "angry_accept";
            case HeartTalkStep.Fix: return "fix";
            case HeartTalkStep.FixReply: return State.chapter02HeartTalkFixAnswer == 1 ? "fix_reply_accompany" : "fix_reply_restart";
            case HeartTalkStep.FixClose: return "fix_close";
            case HeartTalkStep.Identity: return "identity";
            case HeartTalkStep.IdentityReply: return State.chapter02HeartTalkIdentityAnswer == 1 ? "identity_reply_human" : "identity_reply_silent";
            case HeartTalkStep.QuakeLine: return "quake_line";
            case HeartTalkStep.Loading: return loadingLineFinished ? null : State.chapter02HeartTalkFixAnswer == 1 ? "loading_accompany" : "loading_restart";
            default: return null;
        }
    }

    bool EnsureMessage()
    {
        string suffix = Suffix();
        if (messages == null || string.IsNullOrEmpty(suffix)) return false;
        string expected = EntryPrefix + suffix;
        if (messageId != expected)
        {
            messageId = expected; messageTyped = false; clickArmed = false;
        }
        if (!OwnMessageDisplayed || messages.DisplayedEntryId != expected)
        {
            if (messages.IsPresenting || messages.GlobalMessagesSuppressed || messages.IsWaitingForOption
                || (messages.LinkWindow != null && (messages.LinkWindow.IsOpen || messages.LinkWindow.IsClosing))) return false;
            var state = GameDataManager.Instance?.OSCharacterInteractionState;
            if (state == null) return false;
            state.SetGlobalEntryConsumed(expected, false);
            messages.NotifyGlobalExternalSignal(SignalPrefix + suffix);
            return false;
        }
        if (messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized
            || !messages.LinkWindow.gameObject.activeInHierarchy) return false;
        if (!messageTyped && (!messages.IsPresenting || messages.IsDisplayedTextComplete || messages.IsWaitingForOption)) MarkTyped();
        return true;
    }

    void OnPresentationStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry == null || entry.EntryId != messageId || !ReferenceEquals(observed, State)) return;
        messageTyped = clickArmed = false;
        completedFrame = -1;
    }
    void OnTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry != null && entry.EntryId == messageId && ReferenceEquals(observed, State)) MarkTyped();
    }
    void MarkTyped()
    {
        if (messageTyped) return;
        messageTyped = true;
        completedFrame = Time.frameCount;
    }

    void HandleClick(bool pressed, bool held, int frame)
    {
        if (!IsEngaged || !Available || !OwnMessageDisplayed || messages.DisplayedEntryId != messageId
            || messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized
            || !messages.LinkWindow.gameObject.activeInHierarchy)
        { clickArmed = false; return; }
        if (!held) clickArmed = true;
        if (!pressed || !clickArmed || frame == lastClickFrame) return;
        clickArmed = false; lastClickFrame = frame;
        if (!messageTyped)
        {
            if (HasOptions) messages.TryCompleteDisplayedOptionTextImmediately(messageId);
            else messages.TryCompleteDisplayedTextImmediately(messageId);
            return;
        }
        if (completedFrame == frame || !HasManualLine) return;
        CloseOwnMessage();
        if (IsWaitStep)
        {
            State.chapter02HeartTalkWaitElapsed = WaitConfirmed;
            skipAdvance = true;
        }
        else SetStep(Step == HeartTalkStep.IdentityReply
            ? HeartTalkStep.Quake : (HeartTalkStep)((int)Step + 1));
    }

    void OnOptionSelected(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (!IsEngaged || !Available || !ReferenceEquals(observed, State) || entry == null || option == null
            || entry.EntryId != ReservedEntryId) return;
        string id = option.OptionId;
        HeartTalkStep next;
        switch (Step)
        {
            case HeartTalkStep.Ask:
                if (id != "ask" && id != "wait") return;
                next = id == "ask" ? HeartTalkStep.Scroll : HeartTalkStep.AskWait; break;
            case HeartTalkStep.AskAgain:
                if (id != "ask" && id != "wait_again") return;
                next = id == "ask" ? HeartTalkStep.Scroll : HeartTalkStep.AskWait2; break;
            case HeartTalkStep.AskLast:
                if (id != "ask") return;
                next = HeartTalkStep.Scroll; break;
            case HeartTalkStep.Scroll:
                if (id != "me" && id != "not_me") return;
                State.chapter02HeartTalkScrollAnswer = id == "me" ? 1 : 2;
                next = HeartTalkStep.ScrollReply; break;
            case HeartTalkStep.Fix:
                if (id != "accompany" && id != "restart") return;
                // OSLinkMessageController applied the idempotent +3 receipt
                // before this event. The director only persists the answer.
                State.chapter02HeartTalkFixAnswer = id == "accompany" ? 1 : 2;
                next = HeartTalkStep.FixReply; break;
            case HeartTalkStep.Identity:
                if (id != "human" && id != "silent") return;
                State.chapter02HeartTalkIdentityAnswer = id == "human" ? 1 : 2;
                next = HeartTalkStep.IdentityReply; break;
            case HeartTalkStep.QuakeLine:
                if (id != "go") return;
                next = HeartTalkStep.Loading; break;
            default: return;
        }
        // The controller closes its option message after this callback. Closing
        // it here would invalidate the controller's resource/consumption commit.
        HideContinueTip();
        SetStep(next);
    }

    void CloseOwnMessage()
    {
        HideContinueTip();
        if (OwnMessageDisplayed) messages.LinkWindow.CloseFromScript();
        messageTyped = false; messageId = null;
    }
    void PauseMessage()
    {
        presentation?.PauseAudio();
        CloseOwnMessage();
        skipAdvance = true; clickArmed = false;
    }
    public void NotifyOSHidden() => PauseMessage();
    public void ResetPresentation()
    {
        CloseOwnMessage();
        presentation?.ResetPresentation();
        messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
        observed = null; renderedStep = (HeartTalkStep)(-1);
        elapsed = tipTime = loadingLineElapsed = 0f;
        restorePending = skipAdvance = true;
        messageTyped = clickArmed = quakeTriggered = loadingLineFinished = returnNotified = false;
        completedFrame = lastClickFrame = releaseStableFrame = -1;
    }

#if UNITY_EDITOR
    public void DebugStep(float seconds)
    {
        Refresh();
        if (Step == HeartTalkStep.Done)
        {
            if (GameDataManager.Instance?.CurrentChapterIndex == 2 && AmbientAvailable && !returnNotified) RestoreFinishedDesktop();
            return;
        }
        if (!IsEngaged || !Available)
        {
            if (IsEngaged) PauseMessage();
            else presentation?.PauseAudio();
            return;
        }
        Tick(Mathf.Clamp(seconds, 0f, .1f));
    }
    public void DebugPointerButton(bool pressed, bool held, int frame) => HandleClick(pressed, held, frame);
    public string DebugMessageId => messageId;
    public bool DebugMessageTyped => messageTyped;
    public float DebugElapsed => elapsed;
    public float DebugLoadingLineElapsed => loadingLineElapsed;
#endif
}
