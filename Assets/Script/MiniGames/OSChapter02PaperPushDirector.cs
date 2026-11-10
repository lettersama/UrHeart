using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Owns the chapter-ending transaction. The match controller owns rules and input,
// the companion owns her rendered pointer, and this component owns story order.
[DisallowMultipleComponent]
[DefaultExecutionOrder(620)]
public sealed partial class OSChapter02PaperPushDirector : MonoBehaviour
{
    public enum RematchStep
    {
        Sealed = 0, Unsealing = 1, Match1Title = 2, Match1 = 3,
        Match2Title = 4, Match2 = 5, Wrap = 6, HeartTalkHold = 7
    }
    public const string EntryPrefix = "paper_push_ch02_";
    public const string SignalPrefix = "paper_push_ch02.";
    OS_Handle os;
    OSCursorReleaseDirector actor;
    OSPaperPushStoryDirector chapter01Story;
    OSPaperPushWindowController game;
    OSLinkMessageController messages;
    OSWindowUnit window, liveWindow, settingsWindow;
    OSEnergyLoopSaveData observed;
    RematchStep renderedStep = (RematchStep)(-1);
    bool hasFocus = true, inputArmed, messageTyped, skipAdvance = true;
    bool choreographed, boundGame;
    int completedFrame = -1, lastInputFrame = -1;
    string messageId;
    float resultHold, tipTime, concessionWait = -1f;
    Image continueTip;

    OSEnergyLoopSaveData State => GameDataManager.Instance?.OSEnergyLoopState;
    public RematchStep Step => (RematchStep)(State?.chapter02PaperPushStep ?? 0);
    public bool IsStoryProtected => isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 2
        && Step >= RematchStep.Unsealing && Step <= RematchStep.HeartTalkHold;
    public bool IsBlockingInput => IsStoryProtected && (Step == RematchStep.Unsealing || Step == RematchStep.Wrap);
    public bool IsMessageProtected => IsStoryProtected && OwnMessageDisplayed;
    public bool IsWaitingForHeartTalk => IsStoryProtected && Step == RematchStep.HeartTalkHold;
    public string ReservedEntryId => !IsStoryProtected ? null
        : IsFirstResultWaiting && !FirstResultScreenReady ? string.Empty
        : State.chapter02PaperPushCueQueue.Count > 0 ? EntryPrefix + State.chapter02PaperPushCueQueue[0] : string.Empty;
    public event Action HeartTalkReady;

    bool Available => hasFocus && os != null && os.IsOSVisible
        && !GameTestConsole.BlocksGameplayInput && !OSSessionPersistence.IsRestoring
        && GameSessionFlow.Instance?.IsBusy != true && !os.IsSystemInputLockedWithoutChapter02Nightfall
        && !(settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized
            && settingsWindow.gameObject.activeInHierarchy);
    bool OwnMessageDisplayed => messages != null && messages.LinkWindow != null && messages.LinkWindow.IsOpen
        && IsOwnEntry(messages.DisplayedEntryId);
    bool IsFirstResultWaiting => Step == RematchStep.Match1 && State != null
        && !State.paperPushMatch.active && State.chapter02PaperPushMatch1Won != 0;
    bool FirstResultScreenReady => game?.Phase == OSPaperPushWindowController.MatchPhase.Title
        && window != null && window.IsOpen && !window.IsMinimized && !window.IsOpening && !window.IsClosing
        && window.gameObject.activeInHierarchy;
    string SecondMatchResultCue => State?.chapter02PaperPushTiltWin == 2 ? "m2_defeat" : "m2_concede";
    public static bool IsOwnEntry(string entry) => !string.IsNullOrEmpty(entry)
        && entry.StartsWith(EntryPrefix, StringComparison.Ordinal);

    void OnEnable() { hasFocus = Application.isFocused; Resolve(); }
    void OnApplicationFocus(bool focused) { hasFocus = focused; inputArmed = false; skipAdvance = true; }
    void OnDisable() { ReleaseHeartTalkAudio(); ResetPresentation(); Unbind(); }
    void OnDestroy()
    {
        ReleaseHeartTalkAudio(); ResetPresentation(); Unbind();
        if (continueTip != null) Destroy(continueTip.gameObject);
    }

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (actor == null) actor = os?.CursorRelease;
        if (chapter01Story == null) chapter01Story = GetComponent<OSPaperPushStoryDirector>();
        if (game == null) game = GetComponent<OSPaperPushWindowController>();
        if (window == null) window = chapter01Story?.Window;
        if (liveWindow == null) liveWindow = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settingsWindow == null) settingsWindow = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (game != null && !boundGame)
        {
            game.Chapter02MatchStarted += OnMatchStarted;
            game.Chapter02MatchFinished += OnMatchFinished;
            game.Chapter02Cue += QueueCue;
            boundGame = true;
        }
        var next = os?.LinkMessageController;
        if (next == messages) return;
        if (messages != null) messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages = next;
        if (messages != null) messages.EntryTextPlaybackCompleted += OnTextCompleted;
    }

    void Unbind()
    {
        if (messages != null) messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages = null;
        if (game != null && boundGame)
        {
            game.Chapter02MatchStarted -= OnMatchStarted;
            game.Chapter02MatchFinished -= OnMatchFinished;
            game.Chapter02Cue -= QueueCue;
        }
        boundGame = false;
    }

    void Update()
    {
        Resolve();
        RefreshHeartTalkAudio();
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State;
            State?.chapter02PaperPushCueQueue.RemoveAll(IsRetiredSecondMatchCue);
        }
        if (State == null) return;
        if (GameDataManager.Instance.CurrentChapterIndex != 2 || os?.IsOSVisible != true)
        {
            if (renderedStep != (RematchStep)(-1)) ResetPresentation();
            return;
        }
        // Polling the persisted boundary also upgrades an older save made after Live07.
        if (Step == RematchStep.Sealed && Available && os.Chapter02Finale?.Step == OSChapter02FinaleDirector.FinaleStep.Complete)
        {
            State.paperPushMatch.active = false;
            State.paperPushSealed = true;
            SetStep(RematchStep.Unsealing);
        }
        if (!IsStoryProtected) return;
        bool available = Available;
        actor?.ScriptActorSetPaused(this, !available);
        if (!available)
        {
            game?.SetChapter02InteractionPaused(true);
            inputArmed = false; skipAdvance = true;
            CloseOwnMessage();
            return;
        }
        float dt = skipAdvance ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        skipAdvance = false; tipTime += dt;
        if (actor == null || !actor.TryAcquireScriptActor(this))
        { game?.SetChapter02InteractionPaused(true); return; }
        actor.ScriptActorSetPaused(this, false);
        messages?.MaintainCursorExperimentMessageReservation();
        if (renderedStep != Step) EnterStep();
        if (Step == RematchStep.Unsealing || Step == RematchStep.Wrap)
        {
            game?.SetChapter02InteractionPaused(true);
            if (!choreographed) choreographed = BeginChoreography();
            if (choreographed) TickChoreography(dt);
            return;
        }
        if (Step == RematchStep.HeartTalkHold)
        {
            game?.SetChapter02InteractionPaused(true);
            TickBenchHold(dt);
            return;
        }
        TickMatchActor(dt);
        RecoverResultCue();
        if (TickConcessionWait(dt)) return;
        resultHold = Mathf.Max(0f, resultHold - dt);
        bool hasCue = State.chapter02PaperPushCueQueue.Count > 0;
        bool matchEnded = !State.paperPushMatch.active
            && (Step == RematchStep.Match1 && State.chapter02PaperPushMatch1Won != 0
                || Step == RematchStep.Match2 && State.chapter02PaperPushTiltWin != 0);
        if (IsFirstResultWaiting && !FirstResultScreenReady) CloseOwnMessage();
        bool ready = hasCue && resultHold <= 0f && game?.IsChapter02CueSafe == true
            && (IsFirstResultWaiting ? FirstResultScreenReady
                : matchEnded || window == null || window.IsOpen && !window.IsMinimized && !window.IsOpening && !window.IsClosing);
        // Keep resolving/blocked animations alive until their safe message boundary.
        game?.SetChapter02InteractionPaused(ready || OwnMessageDisplayed);
        if (ready) EnsureMessage();
        // An already visible message remains confirmable when the match window
        // is minimized or closed. Only dispatching a new cue needs that window.
        if (OwnMessageDisplayed)
        {
            // The message is a child of Live. A title-bar drag may have brought
            // the match above that whole group; keep this protected cue readable.
            if (liveWindow != null && window != null
                && liveWindow.transform.parent == window.transform.parent
                && liveWindow.transform.GetSiblingIndex() < window.transform.GetSiblingIndex())
                liveWindow.transform.SetAsLastSibling();
            RenderContinueTip();
            HandleContinueInput();
        }
        else HideContinueTip();
    }

    void EnterStep()
    {
        renderedStep = Step; inputArmed = false; choreographed = false;
        // Clear the previous reading/utility windows at story boundaries,
        // including a loaded title or match. Do not keep closing them per frame.
        if (Step >= RematchStep.Unsealing && Step <= RematchStep.Match2)
            CloseBackgroundWindowsForRematch(Step != RematchStep.Unsealing);
        if (Step == RematchStep.Match1Title || Step == RematchStep.Match2Title)
        {
            game?.PrepareChapter02Title(Step == RematchStep.Match1Title ? 1 : 2);
            if (window != null && (!window.IsOpen || window.IsMinimized)) os.OpenPaperPushWindow();
        }
        else if (Step == RematchStep.Match1 || Step == RematchStep.Match2)
        {
            // An in-progress loaded match intentionally shows its existing Continue title.
            bool finishedMatch = !State.paperPushMatch.active && (Step == RematchStep.Match1 && State.chapter02PaperPushMatch1Won != 0
                || Step == RematchStep.Match2 && State.chapter02PaperPushTiltWin != 0);
            if (!finishedMatch && window != null && (!window.IsOpen || window.IsMinimized)) os.OpenPaperPushWindow();
            // Reopening the OS can resume a suspended FallOff coroutine.
            if (game?.Phase == OSPaperPushWindowController.MatchPhase.Finished) resultHold = .65f;
        }
        else if (Step == RematchStep.HeartTalkHold)
        {
            HeartTalkReady?.Invoke();
        }
    }

    void SetStep(RematchStep next)
    {
        if (State == null) return;
        State.chapter02PaperPushStep = (int)next;
        renderedStep = (RematchStep)(-1); inputArmed = false; skipAdvance = true;
        messageId = null; messageTyped = false; completedFrame = -1;
        HideContinueTip();
        // This boundary follows closing the game, centering Live and parking the
        // companion. Winning or starting Wrap alone does not award the keepsake.
        if (next == RematchStep.HeartTalkHold) OSStoryRecordUnlocks.Refresh(GameDataManager.Instance);
    }

    void OnMatchStarted(int stage)
    {
        if (GameDataManager.Instance?.CurrentChapterIndex != 2) return;
        SetStep(stage == 1 ? RematchStep.Match1 : RematchStep.Match2);
    }
    void OnMatchFinished(int stage, bool playerWon)
    {
        if (GameDataManager.Instance?.CurrentChapterIndex != 2) return;
        // First-match invitations wait for the controller's restored title.
        // The second match retains its fall before the final reaction.
        resultHold = stage == 1 ? 0f : .65f;
        if (stage != 1)
        {
            RemoveRetiredSecondMatchCues();
            QueueCue(SecondMatchResultCue);
        }
    }
    void RecoverResultCue()
    {
        if (State.paperPushMatch.active) return;
        if (IsFirstResultWaiting && FirstResultScreenReady)
            QueueCue(State.chapter02PaperPushMatch1Won == 1 ? "m1_lose" : "m1_win");
        if (Step == RematchStep.Match2 && State.chapter02PaperPushTiltWin != 0)
        {
            RemoveRetiredSecondMatchCues();
            QueueCue(SecondMatchResultCue);
        }
    }
    // Old tilt/fallback cues yield to the persisted win/loss reaction.
    void RemoveRetiredSecondMatchCues()
    {
        string expected = SecondMatchResultCue;
        State.chapter02PaperPushCueQueue.RemoveAll(c => c != expected);
        if (OwnMessageDisplayed && messages.DisplayedEntryId != EntryPrefix + expected) CloseOwnMessage();
    }

    static bool IsRetiredSecondMatchCue(string cue) => cue == "m2_hint2" || cue == "m2_wrong_way"
        || cue != null && (cue.StartsWith("m2_tilt_win_", StringComparison.Ordinal)
            || cue.StartsWith("m2_fallback_", StringComparison.Ordinal));

    bool TickConcessionWait(float dt)
    {
        if (Step != RematchStep.Match2 || State.chapter02PaperPushTiltWin == 0) return false;
        string expected = SecondMatchResultCue;
        bool confirmed = State.chapter02PaperPushCueHistory.Contains(expected);
        bool spoken = OwnMessageDisplayed && messageId == EntryPrefix + expected && messageTyped;
        if (!confirmed && !spoken) return false;
        if (concessionWait < 0f) concessionWait = 0f;
        // This is a reading pause, not animation motion: a slow frame must not
        // stretch the requested three seconds. Availability still pauses it.
        else if (dt > 0f) concessionWait += Time.unscaledDeltaTime;
        // After the result's reading pause, ask before any closing/dragging.
        // Closing the match early may skip this pause, never the invitation.
        bool closed = window == null || !window.IsOpen || window.IsClosing;
        if (!closed && concessionWait < 3f) return false;
        if (!confirmed) CompleteCurrentMessage();
        var memory = GameDataManager.Instance.OSProcessData.memory;
        if (memory.chapter02FinalTalkStep == 0)
            memory.chapter02FinalTalkInvitationPending = true;
        game?.SetChapter02InteractionPaused(true);
        return true;
    }

    // The mandatory option commits this persisted boundary. The shared message
    // controller closes the choice after its callback, then Wrap starts next tick.
    public bool TryBeginHeartTalkTransition()
    {
        var memory = GameDataManager.Instance?.OSProcessData.memory;
        if (!IsStoryProtected || !Available || Step != RematchStep.Match2 || State.paperPushMatch.active
            || State.chapter02PaperPushTiltWin == 0 || memory == null
            || memory.chapter02FinalTalkStep != 0 || !memory.chapter02FinalTalkInvitationPending)
            return false;
        memory.chapter02FinalTalkInvitationPending = false;
        SetStep(RematchStep.Wrap);
        game?.SetChapter02InteractionPaused(true);
        return true;
    }
    void QueueCue(string suffix)
    {
        var save = State;
        if (save == null || string.IsNullOrEmpty(suffix) || save.chapter02PaperPushCueHistory.Contains(suffix)
            || save.chapter02PaperPushCueQueue.Contains(suffix)) return;
        save.chapter02PaperPushCueQueue.Add(suffix);
    }

    bool EnsureMessage()
    {
        string expected = ReservedEntryId;
        if (string.IsNullOrEmpty(expected) || messages == null) return false;
        if (messageId != expected)
        { messageId = expected; messageTyped = false; inputArmed = false; completedFrame = -1; }
        if (liveWindow == null || liveWindow.IsClosing) return false;
        if (!liveWindow.IsOpen || liveWindow.IsMinimized)
        { os.OpenCharacterWindowForReconnect(.15f); return false; }
        if (liveWindow.IsOpening) return false;
        if (!OwnMessageDisplayed || messages.DisplayedEntryId != expected)
        {
            if (messages.IsPresenting || messages.GlobalMessagesSuppressed || messages.IsWaitingForOption
                || (messages.LinkWindow != null && (messages.LinkWindow.IsOpen || messages.LinkWindow.IsClosing))) return false;
            GameDataManager.Instance.OSCharacterInteractionState.SetGlobalEntryConsumed(expected, false);
            messages.NotifyGlobalExternalSignal(SignalPrefix + expected.Substring(EntryPrefix.Length));
            return false;
        }
        if (messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized) return false;
        if (!messageTyped && (!messages.IsPresenting || messages.IsDisplayedTextComplete)) MarkTyped();
        return true;
    }

    void HandleContinueInput()
    {
        var mouse = Mouse.current; var keyboard = Keyboard.current;
        bool held = mouse?.leftButton.isPressed == true || keyboard?.eKey.isPressed == true || keyboard?.spaceKey.isPressed == true;
        bool pressed = mouse?.leftButton.wasPressedThisFrame == true || keyboard?.eKey.wasPressedThisFrame == true || keyboard?.spaceKey.wasPressedThisFrame == true;
        if (!held) inputArmed = true;
        if (!pressed || !inputArmed || lastInputFrame == Time.frameCount) return;
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected?.GetComponentInParent<TMPro.TMP_InputField>()?.isFocused == true) return;
        inputArmed = false; lastInputFrame = Time.frameCount;
        if (!OwnMessageDisplayed || messages.DisplayedEntryId != messageId
            || messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing) return;
        if (!messageTyped) { messages.TryCompleteDisplayedTextImmediately(messageId); return; }
        if (completedFrame == Time.frameCount) return;
        CompleteCurrentMessage();
    }

    void CompleteCurrentMessage()
    {
        if (State == null || State.chapter02PaperPushCueQueue.Count == 0) return;
        string suffix = State.chapter02PaperPushCueQueue[0];
        CloseOwnMessage();
        State.chapter02PaperPushCueQueue.RemoveAt(0);
        if (!State.chapter02PaperPushCueHistory.Contains(suffix)) State.chapter02PaperPushCueHistory.Add(suffix);
        if (suffix == "m1_win" || suffix == "m1_lose") SetStep(RematchStep.Match2Title);
        else if (suffix == "m2_hint1") game?.RevealChapter02Gear();
        else if ((suffix == "m2_concede" || suffix == "m2_defeat") && concessionWait < 0f) concessionWait = 0f;
    }
    void OnTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry?.EntryId == messageId && ReferenceEquals(observed, State)) MarkTyped();
    }
    void MarkTyped() { if (messageTyped) return; messageTyped = true; completedFrame = Time.frameCount; }

    void RenderContinueTip()
    {
        if (!OwnMessageDisplayed) { HideContinueTip(); return; }
        if (continueTip == null)
        {
            var parent = messages.WindowLayout?.transform.Find("MessageArea") as RectTransform;
            if (parent == null) return;
            var node = new GameObject("Chapter02PaperPushContinueTip", typeof(RectTransform), typeof(Image));
            node.layer = parent.gameObject.layer;
            var rect = (RectTransform)node.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(10f, 7f);
            continueTip = node.GetComponent<Image>(); continueTip.raycastTarget = false; continueTip.preserveAspect = true;
            var source = os.MemoryWindowController?.transform.Find("TextArea/ContinueTip")?.GetComponent<Image>();
            if (source != null) { continueTip.sprite = source.sprite; continueTip.color = source.color; }
        }
        messages.WindowLayout.SetTextFooterHeight(15f);
        continueTip.gameObject.SetActive(messageTyped && continueTip.sprite != null && !messages.LinkWindow.IsOpening && !messages.LinkWindow.IsClosing);
        continueTip.rectTransform.anchoredPosition = new Vector2(0f, 6f + Mathf.Round(Mathf.Sin(tipTime * 4.4f)));
    }
    void HideContinueTip()
    {
        if (continueTip != null) continueTip.gameObject.SetActive(false);
        if (messages != null && IsOwnEntry(messages.DisplayedEntryId)) messages.WindowLayout?.SetTextFooterHeight(0f);
    }
    void CloseOwnMessage()
    {
        HideContinueTip();
        if (OwnMessageDisplayed) messages.LinkWindow.CloseFromScript();
        messageId = null; messageTyped = false; inputArmed = false;
    }
    public void ResetPresentation()
    {
        CloseOwnMessage(); ResetChoreography(); actor?.ReleaseScriptActor(this);
        game?.SetChapter02InteractionPaused(false);
        renderedStep = (RematchStep)(-1); choreographed = false; skipAdvance = true;
        resultHold = tipTime = 0f; concessionWait = -1f; completedFrame = -1;
    }

    public static void PrepareDebugCheckpoint(OSEnergyLoopSaveData save, int stage = 1)
    {
        if (save == null) return;
        save.chapter02HeartTalkAudioStarted = false;
        save.minigameDiscovered = save.minigameUnlocked = true;
        save.paperPushSealed = stage != 2; save.paperPushSealPending = false;
        save.chapter02PaperPushStep = (int)(stage == 2 ? RematchStep.Match2Title : RematchStep.Unsealing);
        save.chapter02PaperPushMatch1Won = stage == 2 ? 1 : 0;
        save.chapter02PaperPushHintStage = save.chapter02PaperPushTiltWin = 0;
        save.chapter02PaperPushCueHistory.Clear(); save.chapter02PaperPushCueQueue.Clear();
        save.paperPushMatch = new OSPaperPushMatchSaveData { chapter = 2, matchStage = stage == 2 ? 2 : 1 };
    }
}
