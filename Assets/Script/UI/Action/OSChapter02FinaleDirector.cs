using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Chapter 2's hidden memory and Live's recognition. The rematch is a later beat.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(610)]
public sealed class OSChapter02FinaleDirector : MonoBehaviour
{
    // Save protocol: append future states; never reorder.
    public enum FinaleStep
    {
        Inactive = 0, AwaitingBoundary = 1, HiddenText = 2, ReturnPause = 3,
        Live01 = 4, Live02 = 5, Live03 = 6, Live04 = 7,
        Live05 = 8, Live06 = 9, Live07 = 10, Complete = 11
    }

    public const int MemoryEndDataIndex = 7460;
    public const string EntryPrefix = "finale_ch02_";
    public const string SignalPrefix = "finale.story.";
    const float MemoryReadHold = .9f, ReturnHold = .7f;

    OS_Handle os;
    OSLinkMessageController messages;
    OSChapter02HiddenMemoryPresentation presentation;
    OSMemoryProcessSaveData observed;
    OSWindowUnit memoryWindow, liveWindow, settingsWindow;
    Image continueTip;
    FinaleStep renderedStep = (FinaleStep)(-1);
    bool hasFocus = true, inputArmed, skipAdvance = true, messageTyped;
    bool memoryBoundaryAdvanceRequested;
    int completedFrame = -1, lastInputFrame = -1;
    float elapsed, tipTime;
    string messageId;

    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    public FinaleStep Step => (FinaleStep)(State?.chapter02FinaleStep ?? 0);
    public bool IsEngaged => isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 2
        && Step >= FinaleStep.HiddenText && Step < FinaleStep.Complete;
    public bool IsHiddenTextActive => IsEngaged && Step == FinaleStep.HiddenText && os?.IsOSVisible == true;
    public bool IsStoryProtected => IsEngaged;
    public bool IsMessageProtected => IsEngaged && OwnMessageDisplayed;
    public string ReservedEntryId => IsEngaged ? (LiveSuffix == null ? string.Empty : EntryPrefix + LiveSuffix) : null;
    public int HiddenLine => State?.chapter02FinaleHiddenLine ?? 0;
    public int RecognitionChoice => State?.chapter02FinaleRecognitionChoice ?? 0;
    public bool IsWaitingForMemoryAdvance => isActiveAndEnabled
        && GameDataManager.Instance?.CurrentChapterIndex == 2
        && State?.currentDataIndex == MemoryEndDataIndex
        && Step <= FinaleStep.AwaitingBoundary && !memoryBoundaryAdvanceRequested;

    public bool TryContinueFromMemory()
    {
        if (!IsWaitingForMemoryAdvance || !Available
            || os.MemoryWindowController?.CurrentData?.DataIndex != MemoryEndDataIndex
            || !os.MemoryWindowController.IsCurrentTextComplete)
            return false;
        // Keep a deliberate click while an outstanding read-along line finishes.
        memoryBoundaryAdvanceRequested = true;
        return true;
    }

    string LiveSuffix => Step >= FinaleStep.Live01 && Step <= FinaleStep.Live07
        ? "live_" + ((int)Step - (int)FinaleStep.Live01 + 1).ToString("00") : null;
    bool HasOptions => Step == FinaleStep.Live04;
    bool OwnMessageDisplayed => messages != null && messages.LinkWindow != null && messages.LinkWindow.IsOpen
        && IsOwnEntry(messages.DisplayedEntryId);
    bool Available => hasFocus && os != null && os.IsOSVisible && GameDataManager.Instance?.CurrentChapterIndex == 2
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && !os.IsSystemInputLockedWithoutChapter02Finale
        && !(settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized
            && settingsWindow.gameObject.activeInHierarchy);

    public static bool IsOwnEntry(string id) => !string.IsNullOrEmpty(id)
        && id.StartsWith(EntryPrefix, StringComparison.Ordinal);

    void OnEnable() { hasFocus = Application.isFocused; Resolve(); }
    void OnApplicationFocus(bool focused) { hasFocus = focused; inputArmed = false; skipAdvance = true; }
    void OnDisable() { ResetPresentation(); Unbind(); }
    void OnDestroy()
    {
        ResetPresentation(); Unbind();
        if (continueTip != null) Destroy(continueTip.gameObject);
    }

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (presentation == null) presentation = GetComponent<OSChapter02HiddenMemoryPresentation>();
        var next = os?.LinkMessageController;
        if (next != messages)
        {
            Unbind(); messages = next;
            if (messages != null)
            {
                messages.EntryPresentationStarted += OnPresentationStarted;
                messages.EntryTextPlaybackCompleted += OnTextCompleted;
                messages.OptionSelected += OnOptionSelected;
            }
        }
        if (memoryWindow == null) memoryWindow = os?.MemoryWindowController?.GetComponent<OSWindowUnit>();
        if (liveWindow == null) liveWindow = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settingsWindow == null) settingsWindow = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
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
        Resolve();
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State;
            memoryBoundaryAdvanceRequested = false;
        }
        if (State == null) return;
        if (GameDataManager.Instance?.CurrentChapterIndex != 2 || os?.IsOSVisible != true)
        {
            if (renderedStep != (FinaleStep)(-1)) ResetPresentation();
            return;
        }
        TryQueueFromCompletedMemory();
        if (Step == FinaleStep.Inactive || Step >= FinaleStep.Complete) return;
        if (Step == FinaleStep.HiddenText) presentation?.SetPaused(!Available);
        if (!Available)
        {
            inputArmed = false; skipAdvance = true;
            // Shared Live typing uses tweens; close its transient visuals while
            // paused and reconstruct the same saved line when play resumes.
            CloseOwnMessage();
            return;
        }
        float dt = skipAdvance ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        skipAdvance = false;
        if (Step == FinaleStep.AwaitingBoundary)
        {
            TickMemoryBoundary(dt);
            return;
        }
        messages?.MaintainCursorExperimentMessageReservation();
        if (renderedStep != Step) EnterStep();
        elapsed += dt; tipTime += dt;
        if (Step == FinaleStep.HiddenText)
        {
            presentation?.AdvancePresentation(dt);
            HandleContinueInput();
        }
        else if (Step == FinaleStep.ReturnPause)
        {
            if (elapsed >= ReturnHold) SetStep(FinaleStep.Live01);
        }
        else
        {
            EnsureMessage();
            RenderContinueTip();
            HandleContinueInput();
        }
    }

    void TryQueueFromCompletedMemory()
    {
        if (Step != FinaleStep.Inactive || !Available) return;
        var memory = os.MemoryWindowController;
        // Checking the actual text unit, not merely triggeredDataIndices, also
        // lets older saves at 7460 enter once their restored final line finishes.
        if (GameDataManager.Instance.CurrentOSMemoryDataIndex == MemoryEndDataIndex
            && memory?.CurrentData?.DataIndex == MemoryEndDataIndex && memory.IsCurrentTextComplete)
            SetStep(FinaleStep.AwaitingBoundary);
    }

    void TickMemoryBoundary(float dt)
    {
        if (GameDataManager.Instance.CurrentOSMemoryDataIndex != MemoryEndDataIndex)
        {
            SetStep(FinaleStep.Inactive); return;
        }
        var memory = os.MemoryWindowController;
        var interaction = GameDataManager.Instance.OSCharacterInteractionState;
        bool ready = memory != null && memory.CurrentData?.DataIndex == MemoryEndDataIndex
            && memory.IsCurrentTextComplete && !memory.IsSubwindowSequencePlaying
            && memoryWindow != null && memoryWindow.IsOpen && !memoryWindow.IsOpening
            && !memoryWindow.IsClosing && !memoryWindow.IsMinimized
            && messages != null && !messages.IsPresenting && !messages.IsWaitingForOption
            && !messages.IsWaitingForExternalCompletion && !messages.IsReadAlongQuestionPending
            && !messages.GlobalMessagesSuppressed && State.chapter02ReadAlongQueue.Count == 0
            && string.IsNullOrEmpty(interaction.pendingGlobalEntryId)
            && string.IsNullOrEmpty(interaction.presentingGlobalEntryId)
            && (messages.LinkWindow == null || (!messages.LinkWindow.IsOpen && !messages.LinkWindow.IsClosing));
        if (!ready || !memoryBoundaryAdvanceRequested) { elapsed = 0f; return; }
        elapsed += dt;
        if (elapsed >= MemoryReadHold) SetStep(FinaleStep.HiddenText);
    }

    void EnterStep()
    {
        renderedStep = Step; elapsed = tipTime = 0f;
        inputArmed = false; messageTyped = false; completedFrame = -1;
        if (Step != FinaleStep.HiddenText) return;
        EventSystem.current?.SetSelectedGameObject(null);
        var saved = State;
        presentation?.Begin(saved.chapter02FinaleHiddenLine,
            nextLine =>
            {
                if (ReferenceEquals(saved, State) && Step == FinaleStep.HiddenText)
                    saved.chapter02FinaleHiddenLine = Mathf.Clamp(nextLine, 0, 24);
            },
            () =>
            {
                if (!ReferenceEquals(saved, State) || Step != FinaleStep.HiddenText) return;
                saved.chapter02FinaleHiddenLine = 24;
                SetStep(FinaleStep.ReturnPause);
            });
    }

    void SetStep(FinaleStep next)
    {
        if (State == null) return;
        State.chapter02FinaleStep = (int)next;
        renderedStep = (FinaleStep)(-1); elapsed = 0f;
        messageId = null; messageTyped = false; inputArmed = false;
        skipAdvance = true; completedFrame = -1;
        HideContinueTip();
    }

    bool EnsureMessage()
    {
        string suffix = LiveSuffix;
        if (messages == null || suffix == null) return false;
        string expected = EntryPrefix + suffix;
        if (messageId != expected)
        {
            messageId = expected; messageTyped = false; inputArmed = false;
        }
        if (liveWindow == null || liveWindow.IsClosing) return false;
        if (!liveWindow.IsOpen || liveWindow.IsMinimized)
        {
            os.OpenCharacterWindowForReconnect(.15f); return false;
        }
        if (liveWindow.IsOpening) return false;
        if (!OwnMessageDisplayed || messages.DisplayedEntryId != expected)
        {
            if (messages.IsPresenting || messages.GlobalMessagesSuppressed || messages.IsWaitingForOption
                || (messages.LinkWindow != null && (messages.LinkWindow.IsOpen || messages.LinkWindow.IsClosing))) return false;
            GameDataManager.Instance.OSCharacterInteractionState.SetGlobalEntryConsumed(expected, false);
            messages.NotifyGlobalExternalSignal(SignalPrefix + suffix);
            return false;
        }
        if (messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing || messages.LinkWindow.IsMinimized) return false;
        if (!messageTyped && (!messages.IsPresenting || messages.IsDisplayedTextComplete || messages.IsWaitingForOption)) MarkTyped();
        return true;
    }

    void HandleContinueInput()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        bool held = mouse?.leftButton.isPressed == true || keyboard?.eKey.isPressed == true
            || keyboard?.spaceKey.isPressed == true;
        bool pressed = mouse?.leftButton.wasPressedThisFrame == true || keyboard?.eKey.wasPressedThisFrame == true
            || keyboard?.spaceKey.wasPressedThisFrame == true;
        if (!held) inputArmed = true;
        if (!pressed || !inputArmed || lastInputFrame == Time.frameCount || IsEditingText()) return;
        inputArmed = false; lastInputFrame = Time.frameCount;
        if (Step == FinaleStep.HiddenText)
        {
            presentation?.HandleContinue(); return;
        }
        if (!OwnMessageDisplayed || messages.DisplayedEntryId != messageId
            || messages.LinkWindow.IsOpening || messages.LinkWindow.IsClosing) return;
        if (!messageTyped)
        {
            if (HasOptions) messages.TryCompleteDisplayedOptionTextImmediately(messageId);
            else messages.TryCompleteDisplayedTextImmediately(messageId);
            return;
        }
        if (HasOptions || completedFrame == Time.frameCount) return;
        FinaleStep next = (FinaleStep)((int)Step + 1);
        CloseOwnMessage(); SetStep(next);
        // Completion intentionally leaves the ordinary desktop available. No
        // rematch, seal removal, or heart-talk direction is implied by this draft.
    }

    static bool IsEditingText()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        var field = selected != null ? selected.GetComponentInParent<TMPro.TMP_InputField>() : null;
        return field != null && field.isFocused;
    }

    void OnPresentationStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry?.EntryId != messageId || !ReferenceEquals(observed, State)) return;
        messageTyped = false; inputArmed = false; completedFrame = -1;
    }
    void OnTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (entry?.EntryId == messageId && ReferenceEquals(observed, State)) MarkTyped();
    }
    void MarkTyped()
    {
        if (messageTyped) return;
        messageTyped = true; completedFrame = Time.frameCount;
    }
    void OnOptionSelected(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (!Available || Step != FinaleStep.Live04 || !ReferenceEquals(observed, State)
            || entry?.EntryId != ReservedEntryId || option == null
            || (option.OptionId != "me" && option.OptionId != "who_else")) return;
        State.chapter02FinaleRecognitionChoice = option.OptionId == "me" ? 1 : 2;
        // The shared controller commits and closes its option after this callback.
        SetStep(FinaleStep.Live05);
    }

    void RenderContinueTip()
    {
        if (!OwnMessageDisplayed || HasOptions) { HideContinueTip(); return; }
        if (continueTip == null)
        {
            var parent = messages.WindowLayout?.transform.Find("MessageArea") as RectTransform;
            if (parent == null) return;
            var node = new GameObject("Chapter02FinaleContinueTip", typeof(RectTransform), typeof(Image));
            node.layer = parent.gameObject.layer;
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(10f, 7f);
            continueTip = node.GetComponent<Image>();
            continueTip.raycastTarget = false; continueTip.preserveAspect = true;
        }
        if (continueTip.sprite == null)
        {
            var source = os.MemoryWindowController?.transform.Find("TextArea/ContinueTip")?.GetComponent<Image>();
            if (source != null)
            {
                continueTip.sprite = source.sprite;
                Color color = source.color; color.a = 1f; continueTip.color = color;
            }
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
        messageId = null; messageTyped = false;
    }
    public void ResetPresentation()
    {
        CloseOwnMessage(); presentation?.ResetPresentation();
        renderedStep = (FinaleStep)(-1); elapsed = tipTime = 0f;
        inputArmed = false; skipAdvance = true; completedFrame = -1;
    }

    public static void PrepareDebugCheckpoint(OSMemoryProcessSaveData memory)
    {
        if (memory == null) return;
        memory.chapter02FinaleStep = (int)FinaleStep.HiddenText;
        memory.chapter02FinaleHiddenLine = 0;
        memory.chapter02FinaleRecognitionChoice = 0;
    }
}
