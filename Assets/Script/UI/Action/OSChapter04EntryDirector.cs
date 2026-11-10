using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The completed letter, the player's chapter-card click, installation and TV
/// launch are separate saved boundaries. No Memory paragraph is used for chapter 4.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(690)]
public sealed partial class OSChapter04EntryDirector : MonoBehaviour
{
    // Save protocol: append only.
    public enum EntryStep
    {
        Inactive = 0, Darken = 1, CloseHome = 2, CentreMemory = 3, Silence = 4,
        EndLine = 5, Later = 6, OpenState = 7, AwaitUnlock = 8,
        Prepare = 9, Installing = 10, AwaitTV = 11, Complete = 12
    }

    public const string EntryPrefix = "chapter04_entry_";
    public const float InstallationSeconds = 50f;
    const string SignalPrefix = "chapter04_entry.";
    OS_Handle os;
    OSLinkMessageController messages;
    OSMemoryProcessSaveData observed;
    OSWindowUnit memoryWindow, stateWindow, liveWindow, settingsWindow;
    OSToolInstallWindowController installer;
    DesktopProcessUnit stateIcon, memoryIcon;
    bool hasFocus = true, typed, optionReleasePending, resumePending, dialogueShellPending;
    int optionBlockedFrame = -1;
    string messageId;

    public OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    public EntryStep Step => (EntryStep)(State?.chapter04EntryStep ?? 0);
    bool InProduction => !GameSetup.IsDemo && GameDataManager.Instance != null
        && GameDataManager.Instance.CurrentChapterIndex >= 3 && GameDataManager.Instance.CurrentChapterIndex <= 4;
    public bool IsEngaged => isActiveAndEnabled && InProduction && Step > EntryStep.Inactive && Step < EntryStep.Complete;
    public bool BlocksMemoryAdvance => isActiveAndEnabled && InProduction && Step >= EntryStep.Darken;
    public bool BlocksMemoryOpen => InProduction && Step == EntryStep.Complete;
    public bool SuppressHome => InProduction && Step >= EntryStep.CloseHome;
    public bool IsMessageProtected => IsEngaged && OwnMessageVisible;
    public float DaylightProgress
    {
        get
        {
            if (!isActiveAndEnabled || !InProduction || Step < EntryStep.CentreMemory) return 0f;
            if (Step > EntryStep.Silence) return 1f;
            float elapsed = State?.chapter04EntryElapsed ?? 0f;
            if (Step == EntryStep.Silence) elapsed += .8f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 2.5f));
        }
    }
    public bool IsOptionGestureBlocked => optionReleasePending || optionBlockedFrame == Time.frameCount;
    public bool CanUnlock => IsEngaged && Step == EntryStep.AwaitUnlock && Available
        && stateWindow != null && stateWindow.IsOpen && !stateWindow.IsMinimized
        && !stateWindow.IsOpening && !stateWindow.IsClosing;
    public string ReservedEntryId => !IsEngaged ? null : Step == EntryStep.EndLine ? EntryPrefix + "end"
        : Step == EntryStep.Later ? EntryPrefix + "later" : Step == EntryStep.Prepare ? EntryPrefix + "prepare" : string.Empty;
    bool OwnMessageVisible => messages?.LinkWindow != null && messages.LinkWindow.IsOpen
        && IsOwnEntry(messages.DisplayedEntryId);
    bool DialogueClear => messages != null && !messages.IsPresenting && !messages.IsWaitingForOption
        && (messages.LinkWindow == null || !messages.LinkWindow.IsOpen && !messages.LinkWindow.IsClosing);
    bool Available => hasFocus && os != null && os.IsOSVisible
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && !os.IsSystemInputLockedWithoutChapter02Nightfall
        && !(settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized);

    public static bool IsOwnEntry(string id) => !string.IsNullOrEmpty(id)
        && id.StartsWith(EntryPrefix, StringComparison.Ordinal);

    void OnEnable() { hasFocus = Application.isFocused; Resolve(); }
    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        optionReleasePending = false; optionBlockedFrame = -1;
        actor?.ScriptActorSetPaused(this, !focused);
    }
    void OnDisable() { ResetPresentation(); Unbind(); }
    void OnDestroy() { ResetPresentation(); Unbind(); }

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (os == null) return;
        os.ResolveChapter04TVReferences();
        if (memoryWindow == null) memoryWindow = os.MemoryWindowController?.GetComponent<OSWindowUnit>();
        if (stateWindow == null) stateWindow = os.StateCheckWindowController?.GetComponent<OSWindowUnit>();
        if (liveWindow == null) liveWindow = os.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settingsWindow == null) settingsWindow = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (installer == null) installer = os.ToolWindowController?.ProgramLibrary?.Installer;
        foreach (var icon in os.DesktopProcessUnits)
        {
            if (icon == null) continue;
            if (icon.ProcessKey == OSDesktopProcessKeys.StateCheck) stateIcon = icon;
            else if (icon.ProcessKey == OSDesktopProcessKeys.Memory) memoryIcon = icon;
        }
        var next = os.LinkMessageController;
        if (messages == next) return;
        Unbind(); messages = next;
        if (messages != null)
        {
            messages.EntryTextPlaybackCompleted += OnTextCompleted;
            messages.OptionSelected += OnOptionSelected;
        }
    }

    void Unbind()
    {
        if (messages == null) return;
        messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages.OptionSelected -= OnOptionSelected;
        messages = null;
    }

    public bool TryBegin()
    {
        Resolve();
        var story = State?.chapter03Story;
        if (!InProduction || !Available || Step != EntryStep.Inactive || GameDataManager.Instance.CurrentChapterIndex != 3
            || story == null || story.phase != (int)OSChapter03StoryDirector.Phase.Complete
            || !story.Has("letter_collected") || story.memoryQueue.Count != 0 || story.liveQueue.Count != 0
            || !DialogueClear || memoryWindow == null || !memoryWindow.IsOpen || memoryWindow.IsOpening) return false;
        // The letter's companion cursor belongs to the previous director until here.
        os.Chapter03Story?.ResetPresentation();
        observed = State;
        SetStep(EntryStep.Darken);
        return true;
    }

    void Update() => Tick();

    void Tick()
    {
        Resolve();
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State; resumePending = true;
        }
        if (optionReleasePending && Mouse.current?.leftButton.isPressed != true)
        { optionReleasePending = false; optionBlockedFrame = Time.frameCount; }
        memoryIcon?.SetTemporarilyUnavailable(BlocksMemoryOpen);
        if (!InProduction || State == null) return;
        os.RefreshChapter04TV();
        if (Step == EntryStep.Inactive)
        {
            TryBegin();
            if (Step == EntryStep.Inactive) return;
        }
        if (OSSessionPersistence.IsRestoring) return;
        if (resumePending)
        {
            resumePending = false;
            os.Chapter03Story?.ResetPresentation();
            os.StateCheckWindowController?.RefreshFromSave();
            dialogueShellPending = Step == EntryStep.Complete && os.TVWindow?.IsOpen == true
                && State.chapter04Story?.started != true;
        }
        if (Step == EntryStep.Complete)
        {
            ApplyOutroPresentation(4, 0f);
            if (memoryWindow != null && memoryWindow.IsOpen && !memoryWindow.IsClosing) memoryWindow.CloseFromScript();
            if (dialogueShellPending && Available)
            {
                if (liveWindow == null || !liveWindow.IsOpen || liveWindow.IsMinimized) os.OpenCharacterWindowForReconnect(.15f);
                else if (messages?.OpenChapter04DialogueShell(this) == true) dialogueShellPending = false;
            }
            ReleaseActor();
            return;
        }

        int outroStage = Step <= EntryStep.CentreMemory ? (int)Step - 1 : 3;
        ApplyOutroPresentation(outroStage, State.chapter04EntryElapsed);
        messages?.MaintainCursorExperimentMessageReservation();
        actor?.ScriptActorSetPaused(this, !Available);
        if (!Available) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        switch (Step)
        {
            case EntryStep.Darken:
                if (AdvanceTime(dt, 2.5f)) { ApplyOutroPresentation(0, 2.5f); SetStep(EntryStep.CloseHome); }
                break;
            case EntryStep.CloseHome:
                if (AdvanceTime(dt, .6f)) { ApplyOutroPresentation(1, .6f); SetStep(EntryStep.CentreMemory); }
                break;
            case EntryStep.CentreMemory:
                if (AdvanceTime(dt, .8f)) { ApplyOutroPresentation(2, .8f); SetStep(EntryStep.Silence); }
                break;
            case EntryStep.Silence:
                if (AdvanceTime(dt, 3f)) SetStep(EntryStep.EndLine);
                break;
            case EntryStep.EndLine:
                EnsureMessage();
                if (typed && OwnMessageVisible && AdvanceTime(dt, 1.5f))
                { CloseOwnMessage(); SetStep(EntryStep.Later); }
                break;
            case EntryStep.Later:
                EnsureMessage();
                break;
            case EntryStep.OpenState:
                if (DialogueClear) TickOpenState(dt);
                break;
            case EntryStep.AwaitUnlock:
                ParkBesideChapter();
                break;
            case EntryStep.Prepare:
                ParkBesideChapter(true);
                if (AdvanceTime(dt, .75f)) EnsureMessage();
                break;
            case EntryStep.Installing:
                if (!DialogueClear || installer == null) break;
                ParkAboveLive();
                if (!installer.IsChapter04Installation
                    && !installer.BeginChapter04Installation(State.chapter04EntryElapsed, os.TVDesktopIcon?.ProcessIcon?.sprite)) break;
                if (AdvanceTime(dt, InstallationSeconds))
                {
                    installer.SetChapter04Progress(InstallationSeconds);
                    var result = GameDataManager.Instance.TryInstallToolProgram(OSToolProgramIds.TV);
                    if (result == OSToolInstallResult.Success || result == OSToolInstallResult.AlreadyInstalled)
                    {
                        installer.FinishChapter04Installation();
                        SetStep(EntryStep.AwaitTV);
                        os.RefreshChapter04TV();
                    }
                }
                else installer.SetChapter04Progress(State.chapter04EntryElapsed);
                break;
            case EntryStep.AwaitTV:
                TickAwaitTV(dt);
                break;
        }
    }

    bool AdvanceTime(float dt, float duration)
    {
        State.chapter04EntryElapsed = Mathf.Min(duration, State.chapter04EntryElapsed + dt);
        return State.chapter04EntryElapsed >= duration;
    }

    public bool TryHandleChapterRecordClick(int chapter)
    {
        if (chapter != 4 || !IsEngaged) return false;
        TryUnlockChapter();
        return true;
    }

    public bool TryUnlockChapter()
    {
        if (!CanUnlock) return false;
        StoryAchievementProgress.Record(StoryAchievementMilestone.Chapter03Complete);
        SetStep(EntryStep.Prepare);
        os.StateCheckWindowController.PlayChapter04RecordUnlockPresentation();
        if (EnsureActor()) actor.ScriptActorHop(this);
        return true;
    }

    // Permission only. The OS opens the window, then calls OnTVOpened on success.
    public bool TryOpenTV() => InProduction && Available && Step >= EntryStep.AwaitTV
        && GameDataManager.Instance.IsTVInstalled;

    public void OnTVOpened()
    {
        if (!TryOpenTV() || os.TVWindow?.IsOpen != true) return;
        if (Step != EntryStep.Complete)
        {
            SetStep(EntryStep.Complete);
            GameDataManager.Instance.SetCurrentChapterIndex(4);
            os.NotifyChapter04Started();
        }
        ApplyOutroPresentation(4, 0f);
        if (memoryWindow != null && memoryWindow.IsOpen) memoryWindow.CloseAnimated(.32f);
        memoryIcon?.SetTemporarilyUnavailable(true);
        dialogueShellPending = State.chapter04Story?.started != true;
        ReleaseActor();
        os.StateCheckWindowController?.RefreshFromSave();
    }

    public bool BlocksDismissal(OSWindowUnit window)
    {
        if (!IsEngaged || window == null) return false;
        if (window == liveWindow) return true;
        if (window == memoryWindow) return Step <= EntryStep.CentreMemory;
        if (Step <= EntryStep.CloseHome && window == os.Chapter03Story?.HomeView?.GetComponentInParent<OSWindowUnit>()) return true;
        if ((Step == EntryStep.EndLine || Step == EntryStep.Later || Step == EntryStep.Prepare)
            && window == messages?.LinkWindow) return true;
        if (Step == EntryStep.OpenState && window == stateWindow) return true;
        return Step == EntryStep.Installing && installer?.IsChapter04Installation == true
            && window == installer.GetComponent<OSWindowUnit>();
    }

    void EnsureMessage()
    {
        string expected = ReservedEntryId;
        if (messages == null || string.IsNullOrEmpty(expected)) return;
        if (messageId != expected) { messageId = expected; typed = false; }
        if (OwnMessageVisible && messages.DisplayedEntryId == expected) return;
        if (!DialogueClear || liveWindow == null || liveWindow.IsClosing) return;
        if (!liveWindow.IsOpen || liveWindow.IsMinimized)
        { os.OpenCharacterWindowForReconnect(.15f); return; }
        if (liveWindow.IsOpening) return;
        GameDataManager.Instance.OSCharacterInteractionState.SetGlobalEntryConsumed(expected, false);
        messages.NotifyGlobalExternalSignal(SignalPrefix + expected.Substring(EntryPrefix.Length));
    }

    void OnTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (!IsEngaged || !ReferenceEquals(observed, State) || entry?.EntryId != ReservedEntryId) return;
        typed = true;
        if (Step != EntryStep.Later && Step != EntryStep.Prepare) return;
        var button = Mouse.current?.leftButton;
        if (button != null && (button.isPressed || button.wasPressedThisFrame))
        { optionReleasePending = button.isPressed; optionBlockedFrame = Time.frameCount; }
    }

    void OnOptionSelected(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (!IsEngaged || !Available || !ReferenceEquals(observed, State)
            || entry?.EntryId != ReservedEntryId || option == null) return;
        if (Step == EntryStep.Later && option.OptionId == "later") SetStep(EntryStep.OpenState);
        else if (Step == EntryStep.Prepare && option.OptionId == "ready")
        {
            // The card has served its purpose. Clear its large panel before the
            // background installer appears beside the held Memory window.
            stateWindow?.CloseFromScript();
            SetStep(EntryStep.Installing);
        }
    }

    void SetStep(EntryStep step)
    {
        if (State == null) return;
        State.chapter04EntryStep = (int)step;
        State.chapter04EntryElapsed = 0f;
        messageId = null; typed = false;
        optionReleasePending = false; optionBlockedFrame = -1;
        ResetCursorCues();
        os.StateCheckWindowController?.RefreshFromSave();
    }

    void CloseOwnMessage()
    {
        if (OwnMessageVisible) messages.LinkWindow.CloseFromScript();
        messageId = null; typed = false;
    }

    public void ResetPresentation()
    {
        ReleaseActor(); ResetCursorCues(); ResetOutroPresentation();
        messages?.WindowLayout?.SetTemporaryLeftDialogue(this, false);
        if (installer?.IsChapter04Installation == true) installer.CancelChapter04Installation();
        messageId = null; typed = false;
        dialogueShellPending = false;
        optionReleasePending = false; optionBlockedFrame = -1;
        memoryIcon?.SetTemporarilyUnavailable(false);
    }

    internal void ReleaseDialogueShellToStory()
    {
        dialogueShellPending = false;
        messages?.WindowLayout?.SetTemporaryLeftDialogue(this, false);
    }

#if UNITY_EDITOR
    public void DebugStep() => Tick();
    public void DebugSetFocus(bool focused) => OnApplicationFocus(focused);
#endif
}
