using UnityEngine;
using UnityEngine.InputSystem;

// The player's mouse is free throughout this scene. Character's red pointer is
// only a non-interactive actor; scripted clicks never enter the input module.
[DisallowMultipleComponent]
[DefaultExecutionOrder(590)]
public sealed partial class OSCursorReleaseDirector : MonoBehaviour
{
    // Values 0..10 are shipped save states. Append new beats; never re-number.
    public enum ReleaseStep { Inactive, Apology, CloseMessage, MoveLive, Explain,
        FetchPaper, Chase, Celebrate, Thanks, AcceptedHold, ForgivenHold,
        AcceptanceReaction = 11, Promise = 12, FetchYeah = 13,
        StowPaper = 14, ConversationHold = 15 }
    const string Prefix = "cursor_release_ch02_";
    // While the free player sulks, Live keeps chasing; a nod (vertical shake)
    // forgives her. The button is only a late fallback for players who never gesture.
    const float ForgiveFallbackSeconds = 9f;
    // The grudging accept route opens its thank-you differently.
    bool AcceptedRoute => State?.chapter02CursorReleaseChoice == 1;
    bool AssistedEscape => State?.chapter02CursorExperimentAssisted == true;
    string ApologyEntryId => AssistedEscape ? OSCursorExperimentStoryDirector.AssistanceEntryId : Prefix + "apology";
    string ThanksSuffix => State?.chapter02CursorReleaseChoice == 1 ? "thanks_accept" : "thanks";
    OSLinkMessageController messages;
    OSWindowUnit live, settings;
    OSMemoryProcessSaveData observed;
    ReleaseStep renderedStep = (ReleaseStep)(-1);
    ReleaseStep audioStep = (ReleaseStep)(-1);
    float elapsed, idleTime;
    bool hasFocus = true, skipAdvance = true, messageTyped, clickArmed;
    int completedFrame = -1, lastClickFrame = -1;
    string messageId;

    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    public ReleaseStep Step => (ReleaseStep)(State?.chapter02CursorReleaseStep ?? 0);
    public bool IsEngaged => isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 2
        && Step > ReleaseStep.Inactive && State?.chapter02HeartTalkStep == 0;
    public bool IsStoryProtected => IsEngaged;
    public bool IsMessageProtected => IsEngaged && OwnMessageDisplayed;
    public string ReservedEntryId
    {
        get
        {
            if (!IsEngaged) return null;
            if (Step == ReleaseStep.Apology || Step == ReleaseStep.CloseMessage) return ApologyEntryId;
            if (Step == ReleaseStep.Explain) return Prefix + "recognition";
            if (Step == ReleaseStep.Chase && elapsed >= ForgiveFallbackSeconds) return Prefix + "forgive";
            if (Step == ReleaseStep.Celebrate || Step == ReleaseStep.Thanks) return Prefix + ThanksSuffix;
            if (Step == ReleaseStep.Promise) return Prefix + "promise";
            return string.Empty;
        }
    }
    bool OwnMessageDisplayed => messages != null && messages.LinkWindow != null && messages.LinkWindow.IsOpen
        && !string.IsNullOrEmpty(messages.DisplayedEntryId)
        && (messages.DisplayedEntryId.StartsWith(Prefix, System.StringComparison.Ordinal)
            || (IsEngaged && AssistedEscape && messages.DisplayedEntryId == OSCursorExperimentStoryDirector.AssistanceEntryId));
    bool AmbientAvailable => hasFocus && OS_Handle.Instance?.IsOSVisible == true && !OSSessionPersistence.IsRestoring
        && GameSessionFlow.Instance?.IsBusy != true && !GameTestConsole.BlocksGameplayInput
        && !(settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy)
        && OS_Handle.Instance?.IsSystemInputLockedWithoutCursorExperiment != true;
    bool Available => AmbientAvailable && OS_Handle.Instance?.CursorExperimentInterior?.IsReadyForStory == true;

    void OnEnable() { hasFocus = Application.isFocused; Resolve(); }
    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused; skipAdvance = true; clickArmed = false;
        if (!focused) PausePresentation();
    }
    void OnDisable() { ResetPresentation(); Unbind(); }
    void OnDestroy() { ResetPresentation(); Unbind(); DestroyVisuals(); }
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
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State;
            // A saved celebration has already granted its resource receipt.
            // Resume its thank-you, not the particles or choice callback.
            if (Step == ReleaseStep.Celebrate) State.chapter02CursorReleaseStep = (int)ReleaseStep.Thanks;
            // The accept route no longer fetches a Yeah puppet; old saves join its thank-you.
            if (Step == ReleaseStep.FetchYeah) State.chapter02CursorReleaseStep = (int)ReleaseStep.Thanks;
            // Stowing has no remaining choice or reward. Complete it on load
            // instead of pulling an already-covered paper back out of Live.
            if (Step == ReleaseStep.StowPaper) State.chapter02CursorReleaseStep = (int)ReleaseStep.ConversationHold;
            // Previously this hold meant the thanks was already confirmed.
            if (Step == ReleaseStep.ForgivenHold) State.chapter02CursorReleaseStep = (int)ReleaseStep.StowPaper;
        }
        if (State == null || Step != ReleaseStep.AcceptedHold
            || State.chapter02CursorReleaseChoice != 1
            || GameDataManager.Instance.CurrentChapterIndex != 2 || OSSessionPersistence.IsRestoring) return;
        // The old accept option had zero resources, so it wrote no receipt.
        // Reuse the real option/receipt for the new reward; never edit balance.
        // If content is not ready yet, keep this legacy hold and retry later.
        if (!State.chapter02CursorReleaseHeartGranted && !TryRestoreAcceptedReward()) return;
        State.chapter02CursorReleaseChoice = 1;
        State.chapter02CursorReleaseStep = (int)ReleaseStep.Promise;
        renderedStep = (ReleaseStep)(-1);
    }
    bool TryRestoreAcceptedReward()
    {
        var data = GameDataManager.Instance;
        var entries = data?.OSCharacterInteractionData?.GlobalEntries;
        if (entries == null || State == null) return false;
        foreach (var entry in entries)
        {
            if (entry == null || entry.EntryId != Prefix + "recognition") continue;
            foreach (var option in entry.Options)
            {
                var change = option?.ResourceChange;
                if (option?.OptionId != "accept" || change == null || change.Heart != 5f
                    || change.Battery != 0f || change.Fault != 0f) continue;
                var memory = State;
                data.TryApplyCharacterOptionResources(OSCharacterInteractionIds.GlobalSequence, entry.EntryId, option);
                if (!ReferenceEquals(memory, State)) return false;
                // False here only means the identical receipt already exists.
                memory.chapter02CursorReleaseHeartGranted = true;
                return true;
            }
        }
        return false;
    }
    public void BeginAfterEscape()
    {
        Resolve(); ObserveSave();
        if (State == null || Step != ReleaseStep.Inactive) return;
        GetComponent<OSCursorExperimentLiveReaction>()?.PausePresentation();
        State.chapter02CursorReleaseStep = (int)ReleaseStep.Apology;
        renderedStep = (ReleaseStep)(-1); skipAdvance = true;
    }
    void Update()
    {
        Resolve(); ObserveSave();
        if (TickScriptActor()) return;
        if (!IsEngaged)
        {
            if (OS_Handle.Instance?.CursorHeartTalk?.IsEngaged == true)
            {
                HoldForHeartTalk();
                return;
            }
            // The apology unlocks a companion pointer permanently. Later OS
            // chapters keep this actor, without reserving any story slot or
            // reopening/moving Live on the player's behalf.
            if (State?.chapter02RedCursorUnlocked != true || !AmbientAvailable)
            { SetVisualsVisible(false); skipAdvance = true; return; }
            EnsureVisuals(); SetVisualsVisible(true); SetPaperVisible(false);
            float ambientDt = skipAdvance ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
            skipAdvance = false; elapsed += ambientDt; idleTime += ambientDt;
            if (!TickHiddenMemoryRead(ambientDt) && !TickReadAlong(ambientDt)) TickIdle(ambientDt);
            SavePointerPosition(); return;
        }
        if (!Available) { PausePresentation(); return; }
        if (!EnsureLive()) return;
        EnsureVisuals();
        float dt = skipAdvance ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        skipAdvance = false;
        Tick(dt);
        TickHeartPop(dt);
        var mouse = Mouse.current;
        HandleClick(mouse != null && mouse.leftButton.wasPressedThisFrame,
            mouse != null && mouse.leftButton.isPressed, Time.frameCount);
    }
    Vector2 HeartPopAtLive() => live != null
        ? LocalPoint(live.WindowRect, new Vector2(live.WindowRect.rect.xMax - 16f, live.WindowRect.rect.yMax - 34f)) : CursorPoint;
    // Above the puppet's head on its stick.
    Vector2 HeartPopAtPaper() => CursorPoint + new Vector2(0f, 66f);
    bool ExperimentWindowLeaving => GetComponent<OSCursorExperimentDiscardPresentation>()?.IsFalling == true;
    bool EnsureLive()
    {
        if (live == null) return false;
        if (live.IsOpen && !live.IsMinimized && !live.IsOpening && !live.IsClosing && live.gameObject.activeInHierarchy) return true;
        if (!live.IsOpening && !live.IsClosing) OS_Handle.Instance?.OpenCharacterWindowForReconnect(.15f);
        return false;
    }
    void Tick(float dt)
    {
        if (renderedStep != Step) EnterStep();
        float previousTime = elapsed;
        elapsed += dt; idleTime += dt;
        TickStepAudio(previousTime, elapsed);
        bool centered = Step >= ReleaseStep.Explain;
        if (centered) KeepLiveCentered();
        messages?.WindowLayout?.SetTemporaryBottomDialogue(this, centered);
        SetVisualsVisible(State.chapter02RedCursorUnlocked);
        if (Step == ReleaseStep.Apology)
        {
            // Let the opened exit and falling window finish before she speaks.
            if (!ExperimentWindowLeaving && EnsureMessage("apology", AssistedEscape) && AssistedEscape)
                SetStep(ReleaseStep.CloseMessage);
        }
        else if (Step == ReleaseStep.CloseMessage)
        {
            if (EnsureMessage("apology", true)) TickCloseMessage(dt);
            else elapsed = Mathf.Max(0f, elapsed - dt);
        }
        else if (Step == ReleaseStep.MoveLive) TickMoveLive();
        else if (Step == ReleaseStep.Explain) { EnsureMessage("recognition", false); TickIdle(dt); }
        else if (Step == ReleaseStep.FetchPaper) TickFetchPaper();
        else if (Step == ReleaseStep.Chase)
        {
            TickChase(dt);
            if (Step == ReleaseStep.Chase && elapsed >= ForgiveFallbackSeconds) EnsureMessage("forgive", true);
        }
        else if (Step == ReleaseStep.Celebrate) TickCelebrate();
        else if (Step == ReleaseStep.Thanks)
        {
            EnsureMessage(ThanksSuffix, false);
            if (AcceptedRoute) TickIdle(dt);
        }
        else if (Step == ReleaseStep.AcceptanceReaction) TickAcceptanceReaction(dt);
        else if (Step == ReleaseStep.Promise) { EnsureMessage("promise", false); TickIdle(dt); }
        else if (Step == ReleaseStep.FetchYeah) TickFetchYeah();
        else if (Step == ReleaseStep.StowPaper) TickStowPaper();
        else if (Step == ReleaseStep.AcceptedHold || Step == ReleaseStep.ConversationHold) TickIdle(dt);
        RenderContinueTip();
        SavePointerPosition();
    }
    void EnterStep()
    {
        renderedStep = Step; elapsed = 0f; clickArmed = false; completedFrame = -1;
        // The nod hint belongs to the chase only; a step change always hides it.
        if (nodHint != null) nodHint.gameObject.SetActive(false);
        fromPoint = CursorPoint; phaseTarget = fromPoint; dragStarted = false; closeWasClicked = false;
        SetPressed(false);
        if (Step >= ReleaseStep.CloseMessage) State.chapter02RedCursorUnlocked = true;
        if (Step >= ReleaseStep.Explain) KeepLiveCentered();
        if (Step == ReleaseStep.FetchPaper || Step == ReleaseStep.FetchYeah) SetPaperVisible(false);
        if (Step == ReleaseStep.Chase) { SetPaper(false); SetPaperVisible(true); ResetChaseGesture(); }
        if (Step == ReleaseStep.Celebrate) { SetPaper(true); SetPaperVisible(true); SpawnConfetti(); }
        if (Step == ReleaseStep.Thanks && AcceptedRoute) SetPaperVisible(false);
        else if (Step == ReleaseStep.Thanks || Step == ReleaseStep.StowPaper)
        {
            SetPaper(true); SetPaperVisible(true); SetPressed(true); PlacePaperAtPointer();
        }
        if (Step == ReleaseStep.AcceptedHold || Step == ReleaseStep.AcceptanceReaction
            || Step == ReleaseStep.Promise || Step == ReleaseStep.ConversationHold) SetPaperVisible(false);
        if (Step == ReleaseStep.ConversationHold) { PlaceCursor(LiveSidePoint()); followVelocity = Vector2.zero; }
        if (Step == ReleaseStep.MoveLive)
        {
            dragFromOffset = live.PresentationOffset;
        }
    }
    void SetStep(ReleaseStep next)
    {
        if (State == null) return;
        State.chapter02CursorReleaseStep = (int)next;
        // Only real transitions arm prop audio. ObserveSave restores the pose
        // directly, so reopening a saved action cannot replay its sound.
        audioStep = next;
        renderedStep = (ReleaseStep)(-1); elapsed = 0f; skipAdvance = true;
        clickArmed = false; messageTyped = false; messageId = null;
    }
    bool EnsureMessage(string suffix, bool completeImmediately)
    {
        if (messages == null) return false;
        string expected = suffix == "apology" ? ApologyEntryId : Prefix + suffix;
        if (messageId != expected)
        {
            messageId = expected; messageTyped = false; clickArmed = false;
        }
        if (!OwnMessageDisplayed || messages.DisplayedEntryId != expected)
        {
            var state = GameDataManager.Instance.OSCharacterInteractionState;
            if (messages.IsPresenting || messages.GlobalMessagesSuppressed || messages.IsWaitingForOption
                || (messages.LinkWindow != null && (messages.LinkWindow.IsOpen || messages.LinkWindow.IsClosing))) return false;
            state.SetGlobalEntryConsumed(expected, false);
            messages.NotifyGlobalExternalSignal(expected == OSCursorExperimentStoryDirector.AssistanceEntryId
                ? "cursor_test.story.assisted_release" : "cursor_release.story." + suffix);
            return false;
        }
        if (messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing) return false;
        if (completeImmediately && !messageTyped && !messages.IsDisplayedTextComplete && !messages.IsWaitingForOption)
        {
            if (suffix == "forgive") messages.TryCompleteDisplayedOptionTextImmediately(expected);
            else messages.TryCompleteDisplayedTextImmediately(expected);
        }
        if (!messageTyped && (!messages.IsPresenting || messages.IsDisplayedTextComplete || messages.IsWaitingForOption)) MarkTyped();
        return true;
    }
    void OnPresentationStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry == null || entry.EntryId != messageId || !ReferenceEquals(observed, State)) return;
        // Re-localizing the displayed entry starts a fresh typewriter. Keep
        // the first click as "finish typing", never "advance the story".
        messageTyped = false; clickArmed = false; completedFrame = -1;
    }
    void OnTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry != null && entry.EntryId == messageId && ReferenceEquals(observed, State)) MarkTyped();
    }
    void MarkTyped() { if (messageTyped) return; messageTyped = true; completedFrame = Time.frameCount; }
    void HandleClick(bool pressed, bool held, int frame)
    {
        if (!Available || (AssistedEscape && Step == ReleaseStep.Apology)
            || !OwnMessageDisplayed || messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing
            || messages.LinkWindow.IsMinimized || !messages.LinkWindow.gameObject.activeInHierarchy)
        { clickArmed = false; return; }
        if (!held) clickArmed = true;
        if (!pressed || !clickArmed || frame == lastClickFrame) return;
        clickArmed = false; lastClickFrame = frame;
        if (!messageTyped)
        {
            if (Step == ReleaseStep.Explain || Step == ReleaseStep.Chase) messages.TryCompleteDisplayedOptionTextImmediately(messageId);
            else messages.TryCompleteDisplayedTextImmediately(messageId);
            return;
        }
        if (completedFrame == frame) return;
        if (Step == ReleaseStep.Apology) SetStep(ReleaseStep.CloseMessage);
        else if (Step == ReleaseStep.Promise) { CloseOwnMessage(); SetStep(ReleaseStep.Thanks); }
        else if (Step == ReleaseStep.Thanks)
        {
            CloseOwnMessage();
            // Only the forgiven route holds a puppet that must be put away.
            SetStep(AcceptedRoute ? ReleaseStep.ConversationHold : ReleaseStep.StowPaper);
        }
    }
    void OnOptionSelected(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (!Available || !ReferenceEquals(observed, State) || entry == null || option == null) return;
        if (entry.EntryId == Prefix + "recognition" && Step == ReleaseStep.Explain)
        {
            bool accepted = option.OptionId == "accept";
            if (!accepted && option.OptionId != "ignore") return;
            State.chapter02CursorReleaseChoice = accepted ? 1 : 2;
            if (accepted)
            {
                State.chapter02CursorReleaseHeartGranted = true;
                SpawnHeartPop(HeartPopAtLive());
            }
            SetStep(accepted ? ReleaseStep.AcceptanceReaction : ReleaseStep.FetchPaper);
        }
        else if (entry.EntryId == Prefix + "forgive" && Step == ReleaseStep.Chase && option.OptionId == "forgive")
        {
            // OSLinkMessageController applied its idempotent +5-heart receipt
            // immediately before this callback; never apply the reward twice.
            State.chapter02CursorReleaseHeartGranted = true;
            SpawnHeartPop(HeartPopAtPaper());
            SetStep(ReleaseStep.Celebrate);
        }
    }
    /// <summary>A nod during the chase: same reward receipt as the fallback button.</summary>
    void ForgiveByGesture()
    {
        if (State == null || Step != ReleaseStep.Chase) return;
        var data = GameDataManager.Instance;
        var entries = data?.OSCharacterInteractionData?.GlobalEntries;
        if (entries != null)
            foreach (var entry in entries)
            {
                if (entry == null || entry.EntryId != Prefix + "forgive") continue;
                foreach (var option in entry.Options)
                    if (option != null && option.OptionId == "forgive")
                        data.TryApplyCharacterOptionResources(OSCharacterInteractionIds.GlobalSequence, entry.EntryId, option);
            }
        // Mirrors the option callback; a fallback already on screen is withdrawn.
        State.chapter02CursorReleaseHeartGranted = true;
        if (OwnMessageDisplayed) CloseOwnMessage();
        SpawnHeartPop(HeartPopAtPaper());
        SetStep(ReleaseStep.Celebrate);
    }
    void CloseOwnMessage()
    {
        HideContinueTip();
        if (OwnMessageDisplayed) messages.LinkWindow.CloseFromScript();
        messageTyped = false; messageId = null;
    }
    void PausePresentation()
    {
        CloseOwnMessage(); ClearCloseHover(); SetVisualsVisible(false);
        skipAdvance = true; clickArmed = false;
    }
    public void ResetPresentation()
    {
        ResetScriptActor();
        heartTalkOwnsActor = false;
        CloseOwnMessage(); ClearCloseHover(); SetVisualsVisible(false);
        live?.ClearPresentationOffset(this);
        messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
        if (liveMoveInteractionOwned && live != null) live.ConfigureInteraction(liveCouldMove, liveCouldResize);
        liveMoveInteractionOwned = false;
        observed = null; renderedStep = (ReleaseStep)(-1); elapsed = idleTime = 0f;
        audioStep = (ReleaseStep)(-1);
        messageId = null; clickArmed = false; messageTyped = false; skipAdvance = true;
        completedFrame = lastClickFrame = -1;
        ClearConfetti();
        DestroyVisuals();
    }
#if UNITY_EDITOR
    public void DebugStep(float seconds)
    {
        Resolve(); ObserveSave(); if (!IsEngaged || !EnsureLive()) return;
        EnsureVisuals(); Tick(Mathf.Clamp(seconds, 0f, .1f)); TickHeartPop(Mathf.Clamp(seconds, 0f, .1f));
    }
    public void DebugPointerButton(bool pressed, bool held, int frame) => HandleClick(pressed, held, frame);
#endif
}
