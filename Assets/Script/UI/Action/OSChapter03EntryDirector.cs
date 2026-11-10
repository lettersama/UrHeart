using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Production Chapter 2 -> 3 handoff. Unlock, package installation and the
/// player's first memory launch are separate persisted boundaries.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(650)]
public sealed partial class OSChapter03EntryDirector : MonoBehaviour
{
    // Save protocol: append only.
    public enum EntryStep
    {
        Inactive = 0, Thunder = 1, Important = 2, AwaitUnlock = 3,
        UpgradeOffer = 4, AutoInstall = 5, Installing = 6,
        Ready = 7, AwaitMemory = 8, Complete = 9
    }

    public const string EntryPrefix = "chapter03_entry_";
    const string SignalPrefix = "chapter03_entry.";
    OS_Handle os;
    OSLinkMessageController messages;
    OSChapter02NightfallPresentation night;
    OSMemoryProcessSaveData observed;
    OSWindowUnit stateWindow, memoryWindow, toolWindow, liveWindow, settingsWindow;
    OSToolWindowController terminal;
    OSToolProgramLibrary library;
    DesktopProcessUnit memoryIcon, toolIcon, stateIcon;
    bool hasFocus = true, typed, importantShown, resumePending;
    bool optionReleasePending;
    int optionBlockedFrame = -1;
    string messageId;

    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    public EntryStep Step => (EntryStep)(State?.chapter03EntryStep ?? 0);
    bool InProduction => !GameSetup.IsDemo && GameDataManager.Instance != null
        && GameDataManager.Instance.CurrentChapterIndex >= 2 && GameDataManager.Instance.CurrentChapterIndex <= 3;
    public bool IsEngaged => isActiveAndEnabled && InProduction
        && Step >= EntryStep.Thunder && Step < EntryStep.Complete;
    public bool BlocksMemory => InProduction && Step >= EntryStep.UpgradeOffer && Step < EntryStep.AwaitMemory;
    public bool CanUnlock => IsEngaged && Step == EntryStep.AwaitUnlock && Available;
    public bool IsAwaitingMemoryOpen => InProduction && Step == EntryStep.AwaitMemory;
    public bool CanStartMemory => InProduction && Step >= EntryStep.AwaitMemory
        && GameDataManager.Instance.IsMemoryRepair04Installed;
    public bool IsMessageProtected => IsEngaged && OwnMessageVisible;
    public bool IsOptionGestureBlocked => optionReleasePending || optionBlockedFrame == Time.frameCount;
    public string ReservedEntryId => !IsEngaged ? null : Step == EntryStep.Important ? EntryPrefix + "important"
        : Step == EntryStep.UpgradeOffer ? EntryPrefix + "upgrade"
        : Step == EntryStep.Installing && State.chapter03EntryInstallAnswer == 0 ? EntryPrefix + "installing"
        : Step == EntryStep.Ready || Step == EntryStep.AwaitMemory ? EntryPrefix + "ready" : string.Empty;
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
        if (!focused) ReleasePlayer();
    }
    void OnDisable() { ResetPresentation(); Unbind(); }
    void OnDestroy() { ResetPresentation(); Unbind(); }

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (os == null) return;
        if (memoryWindow == null) memoryWindow = os.MemoryWindowController?.GetComponent<OSWindowUnit>();
        if (stateWindow == null) stateWindow = os.StateCheckWindowController?.GetComponent<OSWindowUnit>();
        if (terminal == null) terminal = os.ToolWindowController;
        if (toolWindow == null) toolWindow = terminal?.GetComponent<OSWindowUnit>();
        if (library == null) library = terminal?.ProgramLibrary;
        if (liveWindow == null) liveWindow = os.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settingsWindow == null) settingsWindow = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        foreach (var icon in os.DesktopProcessUnits)
        {
            if (icon == null) continue;
            if (icon.ProcessKey == OSDesktopProcessKeys.Memory) memoryIcon = icon;
            else if (icon.ProcessKey == OSDesktopProcessKeys.Tool) toolIcon = icon;
            else if (icon.ProcessKey == OSDesktopProcessKeys.StateCheck) stateIcon = icon;
        }
        var next = os.LinkMessageController;
        if (messages != next)
        {
            UnbindMessages(); messages = next;
            if (messages != null)
            {
                messages.EntryPresentationStarted += OnEntryPresentationStarted;
                messages.EntryTextPlaybackCompleted += OnTextCompleted;
                messages.OptionSelected += OnOptionSelected;
            }
        }
        if (night == null && (Step != EntryStep.Inactive || os.Chapter02FinalTalk?.IsChapter03EntryReady == true))
        {
            night = os.Chapter02FinalTalk?.Nightfall;
            if (night != null) night.EndReached += OnThunderEnded;
        }
    }

    void UnbindMessages()
    {
        if (messages == null) return;
        messages.EntryPresentationStarted -= OnEntryPresentationStarted;
        messages.EntryTextPlaybackCompleted -= OnTextCompleted;
        messages.OptionSelected -= OnOptionSelected;
        messages = null;
    }
    void Unbind()
    {
        UnbindMessages();
        if (night != null) night.EndReached -= OnThunderEnded;
        night = null;
    }

    public bool TryBegin()
    {
        Resolve();
        if (!InProduction || !Available || Step != EntryStep.Inactive
            || os.Chapter02FinalTalk?.IsChapter03EntryReady != true || !DialogueClear
            || night == null || !night.BeginThunder()) return false;
        observed = State;
        SetStep(EntryStep.Thunder);
        return true;
    }

    void OnThunderEnded()
    {
        if (!InProduction || Step != EntryStep.Thunder || !ReferenceEquals(observed, State)) return;
        FinishThunder();
    }

    void FinishThunder()
    {
        State.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Complete;
        os.Chapter02FinalTalk?.ResetPresentation();
        os.Chapter02PaperPush?.ResetPresentation();
        GameDataManager.Instance.SetCurrentChapterIndex(3);
        os.Chapter02PaperPush?.RefreshHeartTalkAudio();
        SetStep(EntryStep.Important);
        night?.RestoreEntryNight();
        RegisterGlows();
        os.OpenStateCheck(GameDataManager.Instance.CurrentStateCheckProgress);
    }

    void Update() => Tick();

    void Tick()
    {
        Resolve();
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State; resumePending = true;
        }
        RestoreEntryAnimationIntent();
        if (optionReleasePending && Mouse.current?.leftButton.isPressed != true)
        { optionReleasePending = false; optionBlockedFrame = Time.frameCount; }
        memoryIcon?.SetTemporarilyUnavailable(BlocksMemory);
        if (InProduction && Step >= EntryStep.Important) RegisterGlows();
        if (resumePending && InProduction && Step == EntryStep.Complete && Available)
        {
            resumePending = false;
            night?.RestoreEntryNight();
            RegisterGlows();
        }
        if (!IsEngaged)
        {
            ReleaseEntryAnimation(instant: true);
            ReleasePlayer(); ReleaseActor();
            return;
        }
        if (!Available)
        {
            ReleasePlayer();
            actor?.ScriptActorSetPaused(this, true);
            return;
        }
        actor?.ScriptActorSetPaused(this, false);
        if (resumePending)
        {
            resumePending = false;
            if (Step == EntryStep.Thunder && night?.IsThundering != true)
            { FinishThunder(); return; }
            if (Step >= EntryStep.Important)
            {
                State.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Complete;
                os.Chapter02FinalTalk?.ResetPresentation();
                os.Chapter02PaperPush?.ResetPresentation();
                if (GameDataManager.Instance.CurrentChapterIndex != 3) GameDataManager.Instance.SetCurrentChapterIndex(3);
                night?.RestoreEntryNight();
            }
        }
        if (Step == EntryStep.Thunder) return;
        messages?.MaintainCursorExperimentMessageReservation();
        if (BlocksMemory && memoryWindow != null && memoryWindow.IsOpen && !memoryWindow.IsClosing)
            memoryWindow.CloseAnimated();
        if (!EnsureActor()) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        stepClock += dt;
        switch (Step)
        {
            case EntryStep.Important:
                if (typed && importantShown && !OwnMessageVisible && DialogueClear)
                { SetStep(EntryStep.AwaitUnlock); break; }
                if (stateWindow == null || stateWindow.IsClosing) break;
                if (!stateWindow.IsOpen || stateWindow.IsMinimized)
                    os.OpenStateCheck(GameDataManager.Instance.CurrentStateCheckProgress);
                if (!stateWindow.IsOpening)
                {
                    ParkBesideChapter();
                    EnsureMessage();
                }
                break;
            case EntryStep.AwaitUnlock:
                ParkBesideChapter();
                break;
            case EntryStep.UpgradeOffer:
                if (memoryWindow?.IsClosing == true) break;
                // Let the unlock hop land, lean toward the new chapter, then speak.
                if (stepClock < UnlockHopSeconds) break;
                ParkBesideChapter(true);
                if (stepClock >= UnlockSpeakSeconds) EnsureMessage();
                break;
            case EntryStep.AutoInstall:
                if (DialogueClear) TickAutoInstall(dt);
                break;
            case EntryStep.Installing:
                ReleasePlayer();
                if (!GameDataManager.Instance.IsMemoryRepair04Installed && library?.IsInstalling != true)
                {
                    // Installer progress is transient. Restart safely after restore/cancellation.
                    CloseOwnMessage(); SetStep(EntryStep.AutoInstall); break;
                }
                if (TickInstallingCue(dt) && State.chapter03EntryInstallAnswer == 0) EnsureMessage();
                else if (State.chapter03EntryInstallAnswer != 0
                    && GameDataManager.Instance.IsMemoryRepair04Installed && DialogueClear)
                    SetStep(EntryStep.Ready);
                break;
            case EntryStep.Ready:
                EnsureMessage();
                break;
            case EntryStep.AwaitMemory:
                TickAwaitMemory(dt);
                break;
        }
        RefreshEntryAnimation();
    }

    public bool TryHandleChapterRecordClick(int chapter)
    {
        if (chapter != 3 || !IsEngaged) return false;
        TryUnlockChapter();
        return true;
    }

    public bool TryUnlockChapter()
    {
        if (!CanUnlock || stateWindow == null || !stateWindow.IsOpen || stateWindow.IsOpening || stateWindow.IsClosing) return false;
        State.chapter03EntryInstallAnswer = 0;
        SetStep(EntryStep.UpgradeOffer);
        os.StateCheckWindowController.PlayChapter03RecordUnlockPresentation();
        memoryIcon?.SetTemporarilyUnavailable(true);
        if (memoryWindow != null && memoryWindow.IsOpen) memoryWindow.CloseAnimated();
        // A small reaction before she offers to install the new viewer.
        if (EnsureActor()) actor.ScriptActorHop(this);
        library?.RefreshPrograms();
        return true;
    }

    public bool TryEnterMemory()
    {
        if (!CanStartMemory || !Available) return false;
        var data = GameDataManager.Instance;
        if (!data.TryGetOSMemoryChapterProgressRange(3, out var range) || !range.HasMemoryDataRange) return false;
        return range.Contains(data.CurrentOSMemoryDataIndex) || os.StartChapter(3);
    }

    public void NotifyMemoryOpened()
    {
        if (!IsAwaitingMemoryOpen || memoryWindow?.IsOpen != true) return;
        SetStep(EntryStep.Complete);
        memoryIcon?.SetTemporarilyUnavailable(false);
        ReleasePlayer(); ReleaseActor();
    }

    public bool BlocksDismissal(OSWindowUnit window)
    {
        if (!IsEngaged || window == null) return false;
        if (window == liveWindow || window == messages?.LinkWindow) return true;
        if (Step == EntryStep.AutoInstall && (window == stateWindow || window == toolWindow)) return true;
        return library?.IsInstalling == true && (window == library.Installer.GetComponent<OSWindowUnit>() || window == toolWindow);
    }

    void EnsureMessage()
    {
        string expected = ReservedEntryId;
        if (messages == null || string.IsNullOrEmpty(expected)) return;
        if (messageId != expected) { messageId = expected; typed = false; }
        if (OwnMessageVisible && messages.DisplayedEntryId == expected)
        {
            if (Step == EntryStep.Important) importantShown = true;
            return;
        }
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
        if (Step == EntryStep.Important) importantShown = true;
        if (Step == EntryStep.UpgradeOffer || Step == EntryStep.Installing)
        {
            var button = Mouse.current?.leftButton;
            if (button != null && (button.isPressed || button.wasPressedThisFrame))
            { optionReleasePending = button.isPressed; optionBlockedFrame = Time.frameCount; }
        }
        if (Step == EntryStep.Ready)
        {
            // Keep the same message reservation so its existing 5s timer can finish.
            State.chapter03EntryStep = (int)EntryStep.AwaitMemory;
            memoryIcon?.SetTemporarilyUnavailable(false);
            parkTarget = null; ResetCues();
        }
    }

    void OnOptionSelected(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (!IsEngaged || !Available || !ReferenceEquals(observed, State)
            || entry?.EntryId != ReservedEntryId || option == null) return;
        if (Step == EntryStep.UpgradeOffer && option.OptionId == "thanks") SetStep(EntryStep.AutoInstall);
        else if (Step == EntryStep.Installing && (option.OptionId == "assumed" || option.OptionId == "fine"))
        {
            State.chapter03EntryInstallAnswer = option.OptionId == "assumed" ? 1 : 2;
            RefreshEntryAnimation();
        }
        // The message controller consumes and closes the selected option afterward.
    }

    void SetStep(EntryStep step)
    {
        if (State == null) return;
        State.chapter03EntryStep = (int)step;
        messageId = null; typed = false;
        optionReleasePending = false; optionBlockedFrame = -1;
        parkTarget = null;
        if (step != EntryStep.AutoInstall) ReleasePlayer();
        autoPhase = 0; autoClock = 0f; motionStarted = false;
        ResetCues();
        installingExpressionPresented = false;
        RefreshEntryAnimation(instant: step == EntryStep.Complete || step == EntryStep.Inactive);
        if (step == EntryStep.Important) importantShown = false;
    }

    void RegisterGlows()
    {
        if (night == null) return;
        night.AddGlowWindow(stateWindow);
        night.AddGlowWindow(toolWindow);
        night.AddGlowWindow(os?.RecordWindowController?.GetComponent<OSWindowUnit>());
        night.AddGlowWindow(library?.Installer?.GetComponent<OSWindowUnit>());
    }

    void CloseOwnMessage()
    {
        if (OwnMessageVisible) messages.LinkWindow.CloseFromScript();
        messageId = null; typed = false;
    }

    public void ResetPresentation()
    {
        ReleaseEntryAnimation(instant: true);
        installingExpressionPresented = false;
        entryAnimationRestorePending = true;
        ReleasePlayer(); ReleaseActor();
        autoPhase = 0; autoClock = 0f; motionStarted = false; parkTarget = null;
        ResetCues();
        messageId = null; typed = importantShown = false;
        optionReleasePending = false; optionBlockedFrame = -1;
        memoryIcon?.SetTemporarilyUnavailable(false);
    }

#if UNITY_EDITOR
    public void DebugStep() => Tick();
    public void DebugSetFocus(bool focused) => OnApplicationFocus(focused);
    public int DebugAutoPhase => autoPhase;
#endif
}
