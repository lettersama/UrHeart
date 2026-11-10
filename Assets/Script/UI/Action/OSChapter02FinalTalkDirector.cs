using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>The chapter-ending conversation after the paper-push rematch.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(630)]
public sealed partial class OSChapter02FinalTalkDirector : MonoBehaviour
{
    // Persisted values. Append new states; never reorder an existing state.
    public enum FinalTalkStep
    {
        Inactive = 0, Opening = 1, Company = 2, Introduction = 3, Skills = 4,
        Role = 5, Grave = 6, Forget = 7, Comfort = 8, Notes = 9,
        Identity = 10, Research = 11, Question = 12, QuestionReply = 13,
        Night = 14, Goodbye = 15, Chapter03Ready = 16, Complete = 17
    }

    public const string EntryPrefix = "chapter02_final_talk_";
    public const string SignalPrefix = "chapter02_final_talk.";
    OS_Handle os;
    OSLinkMessageController messages;
    OSWindowUnit liveWindow, settingsWindow;
    OSMemoryProcessSaveData observed;
    FinalTalkStep renderedStep = (FinalTalkStep)(-1);
    Image continueTip;
    bool hasFocus = true, inputArmed, messageTyped;
    int completedFrame = -1, lastInputFrame = -1;
    bool optionPointerReleasePending;
    int optionPointerBlockedFrame = -1;
    float tipTime;
    string messageId;

    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    public FinalTalkStep Step => (FinalTalkStep)(State?.chapter02FinalTalkStep ?? 0);
    public bool IsAwaitingInvitation => Step == FinalTalkStep.Inactive && State?.chapter02FinalTalkInvitationPending == true;
    public bool IsEngaged => isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 2
        && (IsAwaitingInvitation || Step >= FinalTalkStep.Opening && Step <= FinalTalkStep.Chapter03Ready);
    public bool IsStoryProtected => IsEngaged;
    public bool IsMessageProtected => IsEngaged && OwnMessageDisplayed;
    public bool IsChapter03EntryReady => Step == FinalTalkStep.Chapter03Ready;
    public bool IsPaused => IsEngaged && !Available;
    public bool IsOptionGestureBlocked => optionPointerReleasePending || optionPointerBlockedFrame == Time.frameCount;
    public string ReservedEntryId => !IsEngaged ? null
        : CurrentSuffix == null ? string.Empty : EntryPrefix + CurrentSuffix;

    // Frontend subscribers reconstruct from Step on restore. These callbacks
    // can repeat after reopening the OS and must not award gameplay resources.
    public event Action<FinalTalkStep> StepChanged;
    public event Action<string> LineTextCompleted;
    public event Action Chapter03EntryReady;

    bool HasOptions => IsAwaitingInvitation || Step == FinalTalkStep.Forget || Step == FinalTalkStep.Question;
    bool IsDialogueBoundaryClear => messages != null && !messages.IsPresenting && !messages.IsWaitingForOption
        && (messages.LinkWindow == null || !messages.LinkWindow.IsOpen && !messages.LinkWindow.IsClosing);
    bool OwnMessageDisplayed => messages != null && messages.LinkWindow != null
        && messages.LinkWindow.IsOpen && IsOwnEntry(messages.DisplayedEntryId);
    bool Available => hasFocus && os != null && os.IsOSVisible
        && GameDataManager.Instance?.CurrentChapterIndex == 2
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && !os.IsSystemInputLockedWithoutChapter02PaperPush
        && !(settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized
            && settingsWindow.gameObject.activeInHierarchy);
    string CurrentSuffix => IsAwaitingInvitation ? "invitation" : GetEntrySuffix(Step, State?.chapter02ReadAssistant ?? 0,
        State?.chapter02FinalTalkComfortAnswer ?? 0, State?.chapter02FinalTalkQuestionAnswer ?? 0);

    public static bool IsOwnEntry(string entryId) => !string.IsNullOrEmpty(entryId)
        && entryId.StartsWith(EntryPrefix, StringComparison.Ordinal);

    public static string GetEntrySuffix(FinalTalkStep step, int assistant, int comfort, int question)
    {
        switch (step)
        {
            case FinalTalkStep.Opening: return "opening";
            case FinalTalkStep.Company: return "company";
            case FinalTalkStep.Introduction: return "introduction";
            case FinalTalkStep.Skills: return "skills";
            case FinalTalkStep.Role: return assistant == 1 ? "role_assistant" : assistant == 2 ? "role_partner" : null;
            case FinalTalkStep.Grave: return "grave";
            case FinalTalkStep.Forget: return "forget";
            case FinalTalkStep.Comfort: return comfort == 1 ? "comfort_together" : "comfort_silent";
            case FinalTalkStep.Notes: return "notes";
            case FinalTalkStep.Identity: return "identity";
            case FinalTalkStep.Research: return "research";
            case FinalTalkStep.Question: return "question";
            case FinalTalkStep.QuestionReply: return question == 1 ? "question_ask" : "question_wait";
            case FinalTalkStep.Night: return "night";
            case FinalTalkStep.Goodbye: return assistant == 1 ? "goodbye_assistant" : "goodbye_partner";
            default: return null;
        }
    }

    void OnEnable() { hasFocus = Application.isFocused; Resolve(); }
    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused; inputArmed = false;
        ClearOptionGesture();
    }
    void OnDisable() { ResetPresentation(); Unbind(); }
    void OnDestroy()
    {
        ResetPresentation(); Unbind();
        if (continueTip != null) Destroy(continueTip.gameObject);
    }

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (os != null && Application.isPlaying && os.Chapter02DemoEnding == null)
            os.gameObject.AddComponent<OSChapter02DemoEnding>();
        if (liveWindow == null) liveWindow = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settingsWindow == null) settingsWindow = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        var next = os?.LinkMessageController;
        if (next == messages) return;
        Unbind(); messages = next;
        if (messages == null) return;
        messages.EntryPresentationStarted += OnPresentationStarted;
        messages.EntryTextPlaybackCompleted += OnTextCompleted;
        messages.OptionSelected += OnOptionSelected;
    }

    void Unbind()
    {
        if (messages == null) return;
        messages.EntryPresentationStarted -= OnPresentationStarted;
        messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages.OptionSelected -= OnOptionSelected;
        messages = null;
    }

    void Update()
    {
        Tick();
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        bool held = mouse?.leftButton.isPressed == true || keyboard?.eKey.isPressed == true || keyboard?.spaceKey.isPressed == true;
        bool pressed = mouse?.leftButton.wasPressedThisFrame == true || keyboard?.eKey.wasPressedThisFrame == true || keyboard?.spaceKey.wasPressedThisFrame == true;
        HandleContinueInput(pressed, held, Time.frameCount);
    }

    void Tick()
    {
        Resolve();
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State;
        }
        RefreshOptionGesture();
        if (State == null) return;
        if (GameDataManager.Instance?.CurrentChapterIndex != 2 || os?.IsOSVisible != true)
        {
            if (renderedStep != (FinalTalkStep)(-1)) ResetPresentation();
            return;
        }
        // Paper-push owns the invitation before Wrap. Its choice starts that
        // choreography; the talk itself starts only after Live has settled.
        // An older save already waiting on the invitation at HeartTalkHold
        // keeps its unanswered choice without replaying the completed movement.
        if (Step == FinalTalkStep.Inactive && !IsAwaitingInvitation && Available
            && os.Chapter02PaperPush?.IsWaitingForHeartTalk == true)
            SetStep(FinalTalkStep.Opening);
        if (!IsEngaged) return;
        if (!Available)
        {
            inputArmed = false;
            CloseOwnMessage();
            return;
        }
        if (Step == FinalTalkStep.Role && CurrentSuffix == null) SetStep(FinalTalkStep.Grave);
        if (IsAwaitingInvitation) messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
        else messages?.WindowLayout?.SetTemporaryBottomDialogue(this, true);
        messages?.MaintainCursorExperimentMessageReservation();
        if (Step == FinalTalkStep.Chapter03Ready && !IsDialogueBoundaryClear) return;
        if (renderedStep != Step)
        {
            renderedStep = Step;
            inputArmed = false;
            StepChanged?.Invoke(Step);
            if (Step == FinalTalkStep.Chapter03Ready)
            {
                StoryAchievementProgress.Record(StoryAchievementMilestone.Chapter02Complete);
                Chapter03EntryReady?.Invoke();
            }
        }
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        TickHeartbeat(dt);
        if (!IsEngaged) return;
        if (Step == FinalTalkStep.Chapter03Ready) { ArmNightfallClick(); return; }
        tipTime += dt;
        if (TickSilentBeat(dt)) return;
        EnsureMessage();
        RenderContinueTip();
    }

    bool EnsureMessage()
    {
        string expected = ReservedEntryId;
        if (messages == null || string.IsNullOrEmpty(expected)) return false;
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
        if (messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized
            || !messages.LinkWindow.gameObject.activeInHierarchy) return false;
        if (!messageTyped && (!messages.IsPresenting || messages.IsDisplayedTextComplete || messages.IsWaitingForOption)) MarkTyped();
        return true;
    }

    void HandleContinueInput(bool pressed, bool held, int frame)
    {
        if (!IsEngaged || !Available || !OwnMessageDisplayed || messages.DisplayedEntryId != messageId
            || messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized
            || !messages.LinkWindow.gameObject.activeInHierarchy)
        { inputArmed = false; return; }
        if (!held) inputArmed = true;
        if (!pressed || !inputArmed || lastInputFrame == frame) return;
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected?.GetComponentInParent<TMPro.TMP_InputField>()?.isFocused == true) return;
        inputArmed = false; lastInputFrame = frame;
        if (!messageTyped)
        {
            if (HasOptions) messages.TryCompleteDisplayedOptionTextImmediately(messageId);
            else messages.TryCompleteDisplayedTextImmediately(messageId);
            return;
        }
        if (HasOptions || completedFrame == frame) return;
        var next = (FinalTalkStep)((int)Step + 1);
        if (next == FinalTalkStep.Role && State.chapter02ReadAssistant != 1 && State.chapter02ReadAssistant != 2)
            next = FinalTalkStep.Grave;
        CloseOwnMessage();
        SetStep(next);
    }

    void SetStep(FinalTalkStep next)
    {
        if (State == null) return;
        State.chapter02FinalTalkStep = (int)next;
        if (next != FinalTalkStep.Inactive) State.chapter02FinalTalkInvitationPending = false;
        HideContinueTip();
        renderedStep = (FinalTalkStep)(-1);
        inputArmed = messageTyped = false;
        nightfallClickArmed = false;
        nightfallReadyFrame = Time.frameCount;
        messageId = null; completedFrame = -1;
        ClearOptionGesture();
    }

    void OnPresentationStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry?.EntryId != messageId || !ReferenceEquals(observed, State)) return;
        messageTyped = inputArmed = false; completedFrame = -1;
        ClearOptionGesture();
    }
    void OnTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry?.EntryId == messageId && ReferenceEquals(observed, State)) MarkTyped();
    }
    void MarkTyped()
    {
        if (messageTyped) return;
        messageTyped = true; completedFrame = Time.frameCount;
        // Options become interactable while the completing mouse press is
        // still down. Its later PointerClick must only finish reading, never
        // also commit an answer. Keyboard/script completion remains immediate.
        var button = Mouse.current?.leftButton;
        if (HasOptions && button != null && (button.isPressed || button.wasPressedThisFrame))
        {
            optionPointerReleasePending = button.isPressed;
            optionPointerBlockedFrame = Time.frameCount;
        }
        LineTextCompleted?.Invoke(messageId);
    }

    void RefreshOptionGesture()
    {
        if (!optionPointerReleasePending || Mouse.current?.leftButton.isPressed == true) return;
        optionPointerReleasePending = false;
        // EventSystem may deliver PointerClick before or after this director.
        optionPointerBlockedFrame = Time.frameCount;
    }
    void ClearOptionGesture()
    {
        optionPointerReleasePending = false;
        optionPointerBlockedFrame = -1;
    }
    void OnOptionSelected(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (!IsEngaged || !Available || !ReferenceEquals(observed, State)
            || entry?.EntryId != ReservedEntryId || option == null) return;
        if (IsAwaitingInvitation)
        {
            if (option.OptionId != "what") return;
            if (os.Chapter02PaperPush?.IsWaitingForHeartTalk == true)
                SetStep(FinalTalkStep.Opening); // Compatibility: an older save already finished Wrap.
            else if (os.Chapter02PaperPush?.TryBeginHeartTalkTransition() == true)
                SetStep(FinalTalkStep.Inactive); // The original opening waits for the end of Wrap.
        }
        else if (Step == FinalTalkStep.Forget)
        {
            if (option.OptionId != "together" && option.OptionId != "silent") return;
            State.chapter02FinalTalkComfortAnswer = option.OptionId == "together" ? 1 : 2;
            SetStep(FinalTalkStep.Comfort);
            if (option.OptionId == "silent") BeginSilentBeat();
        }
        else if (Step == FinalTalkStep.Question)
        {
            if (option.OptionId != "ask" && option.OptionId != "wait") return;
            State.chapter02FinalTalkQuestionAnswer = option.OptionId == "ask" ? 1 : 2;
            SetStep(FinalTalkStep.QuestionReply);
            if (option.OptionId == "wait") BeginSilentBeat();
        }
        // The shared controller commits/consumes/closes the choice after this
        // callback. Do not close it here or alter any resource receipt.
    }

    /// <summary>
    /// Called by the future chapter-entry presentation after its handoff. Keep
    /// the completed talk at its saved boundary until chapter 3 has real data.
    /// </summary>
    public bool TryEnterChapter03()
    {
        // The production handoff owns unlock, installation and the player's launch.
        if (os?.Chapter03Entry?.IsEngaged == true) return false;
        var data = GameDataManager.Instance;
        if (data == null) return false;
        if (Step == FinalTalkStep.Complete) return data.CurrentChapterIndex >= 3;
        if (!IsChapter03EntryReady || !Available || !IsDialogueBoundaryClear
            || !data.TryGetOSMemoryChapterProgressRange(3, out var range) || range == null
            || !range.HasMemoryDataRange || !data.TryGetOSMemoryData(range.FirstDataIndex, out _)
            || range.Contains(data.CurrentOSMemoryDataIndex)) return false;
        data.SetCurrentChapterIndex(3);
        if (!os.StartChapter(3))
        {
            data.SetCurrentChapterIndex(2);
            return false;
        }
        SetStep(FinalTalkStep.Complete);
        os.Chapter02PaperPush?.ResetPresentation();
        os.Chapter02PaperPush?.RefreshHeartTalkAudio();
        ResetPresentation();
        return true;
    }

    void RenderContinueTip()
    {
        if (!OwnMessageDisplayed || HasOptions) { HideContinueTip(); return; }
        if (continueTip == null)
        {
            var parent = messages.WindowLayout?.transform.Find("MessageArea") as RectTransform;
            if (parent == null) return;
            var node = new GameObject("Chapter02FinalTalkContinueTip", typeof(RectTransform), typeof(Image));
            node.layer = parent.gameObject.layer;
            var rect = (RectTransform)node.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(10f, 7f);
            continueTip = node.GetComponent<Image>(); continueTip.raycastTarget = false; continueTip.preserveAspect = true;
        }
        if (continueTip.sprite == null)
        {
            var source = os.MemoryWindowController?.transform.Find("TextArea/ContinueTip")?.GetComponent<Image>();
            if (source != null) { continueTip.sprite = source.sprite; continueTip.color = source.color; }
        }
        messages.WindowLayout.SetTextFooterHeight(15f);
        continueTip.gameObject.SetActive(messageTyped && continueTip.sprite != null
            && !messages.LinkWindow.IsOpening && !messages.LinkWindow.IsClosing);
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
        messageId = null; messageTyped = inputArmed = false;
        ClearOptionGesture();
    }
    public void ResetPresentation()
    {
        CloseOwnMessage();
        messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
        renderedStep = (FinalTalkStep)(-1);
        tipTime = 0f; completedFrame = lastInputFrame = -1;
        ResetTalkPresentation();
    }

    public static void PrepareDebugCheckpoint(OSMemoryProcessSaveData memory, OSEnergyLoopSaveData energy, int assistantRelation = 0)
    {
        if (memory == null || energy == null) return;
        memory.chapter02HeartTalkStep = (int)OSCursorHeartTalkDirector.HeartTalkStep.Done;
        memory.chapter02FinaleStep = (int)OSChapter02FinaleDirector.FinaleStep.Complete;
        memory.chapter02FinaleHiddenLine = 24;
        memory.chapter02FinalTalkStep = (int)FinalTalkStep.Inactive;
        memory.chapter02FinalTalkInvitationPending = false;
        memory.chapter02FinalTalkComfortAnswer = memory.chapter02FinalTalkQuestionAnswer = 0;
        memory.chapter02DemoEndReached = false;
        memory.chapter03EntryStep = memory.chapter03EntryInstallAnswer = 0;
        memory.chapter02ReadAssistant = Mathf.Clamp(assistantRelation, 0, 2);
        memory.chapter02ReadAlongQueue.Clear();
        OSChapter02PaperPushDirector.PrepareDebugCheckpoint(energy, 2);
        energy.chapter02PaperPushStep = (int)OSChapter02PaperPushDirector.RematchStep.HeartTalkHold;
        energy.chapter02PaperPushMatch1Won = energy.chapter02PaperPushTiltWin = 1;
        energy.chapter02HeartTalkAudioStarted = true;
    }

#if UNITY_EDITOR
    public void DebugStep() => Tick();
    public void DebugConfirm(bool pressed, bool held, int frame) => HandleContinueInput(pressed, held, frame);
    public void DebugSetFocus(bool focused) => OnApplicationFocus(focused);
    public string DebugMessageId => messageId;
    public bool DebugMessageTyped => messageTyped;
#endif
}
