using System;
using System.Collections.Generic;
using MrTGame.Narrative;
using TMPro;
using UnityEngine;

/// <summary>
/// Chapter three is an event graph. The two saved queues deliberately outlive
/// Home windows and keep a late click from replacing a paragraph being read.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(680)]
public sealed partial class OSChapter03StoryDirector : MonoBehaviour
{
    public enum Phase
    {
        Opening = 0, Explore = 10, FinalKnock = 20, Yard = 30,
        YardReturn = 40, KeyReady = 50, Mirror = 60, Offline = 70,
        Lab = 80, BedSecond = 85, Reunion = 90, Letter = 100, Complete = 110
    }

    public const string EntryPrefix = "chapter03_story_";
    OS_Handle os;
    OSMemoryWindowController memory;
    OSChapter03HomeController home;
    OSHomeView view;
    OSHomeItemBar items;
    OSLinkMessageController messages;
    OSChapter03StoryPresentation presentation;
    OSChapter03StoryState observed;
    OSWindowUnit memoryWindow, liveWindow, settingsWindow;
    OSChapter03StoryBeat runtimeMemoryBeat, runtimeLiveBeat;
    // A page picked up in the home opens while its line is written (beat number 1).
    OSChapter03StoryBeat paperAlongside;
    bool runtimeMemoryStarted, runtimeLiveStarted, rebinding;
    bool hasFocus = true, debugIgnoreFocus;
    float effectClock;
    int generation;
    int presentedLanguage = -1;
    string interactionSource;
    readonly List<string> itemSnapshot = new List<string>();

    public OSChapter03StoryState State => GameDataManager.Instance?.OSProcessData.memory.chapter03Story;
    OSChapter03StoryContent Content => OSChapter03StoryContent.Instance;
    public Phase CurrentPhase => (Phase)(State?.phase ?? 0);
    public bool IsActive => isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 3
        && GameDataManager.Instance.IsMemoryRepair04Installed
        && GameDataManager.Instance.CurrentOSMemoryDataIndex >= 8000 && Content.Available;
    public bool IsCinematic => IsActive && (CurrentPhase == Phase.FinalKnock
        || CurrentPhase == Phase.YardReturn || CurrentPhase == Phase.Mirror
        || CurrentPhase == Phase.BedSecond || CurrentPhase == Phase.Reunion || CurrentPhase == Phase.Letter);
    public bool IsMirrorSequenceActive => IsActive && CurrentPhase == Phase.Mirror && !Has("mirror_done");
    public bool IsMirrorPerformanceLocked => IsMirrorSequenceActive && (!Has("live_back_home") || IsMirrorPlaybackPaused);
    public bool BlocksHomeInput => !IsActive || !State.homeOpened || !State.Has("home_revealed")
        || CurrentPhase == Phase.Complete || IsCinematic || presentation?.IsModalOpen == true || presentation?.IsAnimating == true;
    public bool BlocksMemoryAdvance => IsActive && (IsCinematic || presentation?.IsModalOpen == true);
    public bool IsMessageProtected => IsActive && messages != null
        && IsOwnEntry(messages.DisplayedEntryId) && (IsCinematic || messages.IsWaitingForOption);
    public string ReservedEntryId => !IsActive ? null : State.liveQueue.Count > 0
        ? LiveId(State.liveQueue[0].key) : string.Empty;
    public OSChapter03StoryPresentation Presentation => presentation;
    public OSHomeView HomeView => view;
    public bool IsAvailable => IsActive && (hasFocus || debugIgnoreFocus) && os != null && os.IsOSVisible
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && !os.IsSystemInputLockedWithoutChapter02Nightfall
        && memoryWindow != null && memoryWindow.IsOpen && !memoryWindow.IsMinimized
        && !memoryWindow.IsOpening && !memoryWindow.IsClosing
        && !(settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized);

    public static string LiveId(string key) => EntryPrefix + (key ?? string.Empty)
        .Replace("os.chapter03.", string.Empty).Replace('.', '_');
    public static bool IsOwnEntry(string id) => !string.IsNullOrEmpty(id)
        && id.StartsWith(EntryPrefix, StringComparison.Ordinal);
    public static string Signal(string key) => "chapter03.story." + key.Replace("os.chapter03.", string.Empty);
    public string Text(string key) => Content.Text(key);

    void OnEnable() { hasFocus = Application.isFocused; Resolve(); OSMemoryTextUnit.HighlightProvider = IsGuideNode; }
    void OnApplicationFocus(bool value) => hasFocus = value;
    void OnDisable()
    {
        ResetPresentation(); Unbind();
        if (OSMemoryTextUnit.HighlightProvider == (Func<int, bool>)IsGuideNode) OSMemoryTextUnit.HighlightProvider = null;
    }
    void OnDestroy() { ResetPresentation(); Unbind(); }
    public void DebugSetFocus(bool value) => debugIgnoreFocus = value;

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (os == null) return;
        var nextMemory = os.MemoryWindowController;
        if (memory != nextMemory)
        {
            if (memory != null)
            { memory.DataShown -= OnDataShown; memory.CurrentTextCompleted -= OnMemoryTextCompleted; }
            memory = nextMemory;
            if (memory != null)
            { memory.DataShown += OnDataShown; memory.CurrentTextCompleted += OnMemoryTextCompleted; }
        }
        if (memory != null)
        {
            memoryWindow = memory.GetComponent<OSWindowUnit>();
            home = memory.GetComponent<OSChapter03HomeController>();
        }
        var nextView = home?.View;
        if (nextView != view)
        {
            UnbindView(); view = nextView;
            if (view != null)
            {
                view.ThingActivated += OnThing;
                view.DoorReached += OnDoor;
                view.RoomEntered += OnRoom;
                view.Stepped += OnStep;
                view.FloorClicked += OnFloorClicked;
            }
        }
        var nextMessages = os.LinkMessageController;
        if (messages != nextMessages)
        {
            if (messages != null)
            {
                messages.EntryPresentationCompleted -= OnLiveCompleted;
                messages.EntryPresentationStarted -= OnLiveStarted;
                messages.EntryTextPlaybackCompleted -= OnLiveTextCompleted;
                messages.OptionSelected -= OnLiveOption;
            }
            messages = nextMessages;
            if (messages != null)
            {
                messages.EntryPresentationCompleted += OnLiveCompleted;
                messages.EntryPresentationStarted += OnLiveStarted;
                messages.EntryTextPlaybackCompleted += OnLiveTextCompleted;
                messages.OptionSelected += OnLiveOption;
            }
        }
        if (liveWindow == null) liveWindow = os.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (settingsWindow == null) settingsWindow = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (presentation == null && IsActive)
        {
            presentation = gameObject.GetComponent<OSChapter03StoryPresentation>();
            if (presentation == null) presentation = gameObject.AddComponent<OSChapter03StoryPresentation>();
            presentation.Initialize(os);
        }
    }

    void UnbindView()
    {
        if (view == null) return;
        view.ThingActivated -= OnThing; view.DoorReached -= OnDoor; view.RoomEntered -= OnRoom;
        view.Stepped -= OnStep; view.FloorClicked -= OnFloorClicked;
        view = null;
    }
    void Unbind()
    {
        UnbindView();
        if (items != null) items.ItemClicked -= OnItemClicked;
        if (memory != null)
        { memory.DataShown -= OnDataShown; memory.CurrentTextCompleted -= OnMemoryTextCompleted; memory = null; }
        if (messages != null)
        {
            messages.EntryPresentationCompleted -= OnLiveCompleted;
            messages.EntryPresentationStarted -= OnLiveStarted;
            messages.EntryTextPlaybackCompleted -= OnLiveTextCompleted;
            messages.OptionSelected -= OnLiveOption;
            messages = null;
        }
    }

    void Update()
    {
        Resolve();
        if (!ReferenceEquals(observed, State))
        {
            ResetPresentation(); observed = State;
            if (IsActive) InitializeState();
        }
        if (MrTGame.Core.GameManager.Instance?.Phase == MrTGame.Core.GamePhase.Boot)
        {
            StopMusicNow();
            SetActorPaused(true);
            return;
        }
        RefreshGramophoneAudioOwnership();
        if (!IsActive)
        {
            if (presentation != null) presentation.ResetPresentation();
            RestoreExternalPresentation();
            return;
        }
        if (State.version == 0) InitializeState();
        // Keep the saved portrait intent while Home is closed, minimized or paused.
        RefreshLivePose();
        int language = (int)GameDataManager.Instance.CurrentLanguageState;
        if (presentedLanguage != language)
        { presentedLanguage = language; RefreshLocalizedPresentation(); }
        if (presentation != null) presentation.Paused = !IsAvailable;
        SetActorPaused(!IsAvailable);
        if (!IsAvailable) return;
        RefreshPersistentPresentation();
        if (memory.IsCurrentTextComplete && memory.CurrentData != null)
            ObserveOpeningCompletion(memory.CurrentData.DataIndex);
        TickMemoryQueue();
        TickLiveQueue();
        TryQueueDeduction();
        TickStoryEffects();
    }

    void InitializeState()
    {
        var s = State;
        if (s == null) return;
        s.EnsureData();
        RestoreLivePortraitCue();
        DeferPendingInitialOfflineOffer();
        if (s.version == 0)
        {
            s.version = 1;
            int node = GameDataManager.Instance.CurrentOSMemoryDataIndex;
            // The earlier walking preview ended at 8110; keep those positions
            // and begin the new exploration, without replaying its introduction.
            if (node >= 8090)
            {
                s.homeOpened = true; s.phase = (int)Phase.Explore;
                s.Add("home_revealed"); s.Add("opening_8080"); s.Add("paper_note_01");
                if (node >= 8100) s.Add("first_floor");
                if (node >= 8110) s.Add("opening_8110");
            }
            else if (node == 8085)
            { s.Add("opening_8080"); Enqueue("paper", "note_01"); }
        }
        rebinding = true;
        try { ApplyWorldState(); }
        finally { rebinding = false; }
    }

    public void ResetPresentation()
    {
        generation++;
        runtimeMemoryBeat = runtimeLiveBeat = paperAlongside = null;
        runtimeMemoryStarted = runtimeLiveStarted = false;
        drainsInFlight.Clear();
        effectClock = 0f;
        presentedLanguage = -1;
        presentation?.ResetPresentation();
        RestoreExternalPresentation();
        itemSnapshot.Clear();
    }

    public bool BlocksDismissal(OSWindowUnit window) => IsActive && window != null
        && (IsMirrorSequenceActive && window != settingsWindow
            || IsCinematic && (window == memoryWindow || window == liveWindow
            || window == home?.HomeWindow || window == messages?.LinkWindow)
            || IsMessageProtected && (window == liveWindow || window == messages?.LinkWindow));

    public bool BlocksWindowInteraction(OSWindowUnit window) => IsMirrorPerformanceLocked
        && window != null && window != settingsWindow;

    void OnDataShown(OSMemoryReadData data)
    {
        if (!IsActive || data == null) return;
        if (data.DataIndex == 8010) PulseBattery();
        if (data.DataIndex == 8040 || data.DataIndex == 8060) StoryBeat(1f);
    }
    void OnMemoryTextCompleted(OSMemoryReadData data)
    {
        if (!IsActive || data == null) return;
        ObserveOpeningCompletion(data.DataIndex);
        if (State.memoryQueue.Count > 0)
        {
            var beat = State.memoryQueue[0];
            if (beat.type == "text" && Content.Node(beat.key) == data.DataIndex) beat.complete = true;
        }
    }
    void ObserveOpeningCompletion(int node)
    {
        if (node == 8020 && State.Add("opening_8020")) Live("live.opening.voice");
        if (node == 8070 && State.Add("opening_8070")) Live("live.opening.memory");
        if (node == 8080 && State.Add("opening_8080")) Enqueue("paper", "note_01");
        if (node == 8110 && State.Add("opening_8110"))
        { StoryBeat(1f); Live("live.opening.heartbeat"); }
    }

    void Enqueue(string type, string key = null, string value = null, float delay = 0f, int number = 0)
        => State.memoryQueue.Add(new OSChapter03StoryBeat(type, key, value, delay, number)
            { interactionSource = interactionSource });
    void Say(string key, float delay = .65f) => Enqueue("text", key, delay: delay);
    void Live(string key, float delay = 0f) => State.liveQueue.Add(new OSChapter03StoryBeat("live", key, delay: delay)
        { interactionSource = interactionSource });

    void QueueInteraction(string source, Action interaction)
    {
        // Check the entire response before applying flags, pickups or other side
        // effects. Distinct objects keep their original place in both queues.
        if (State.memoryQueue.Exists(beat => beat.interactionSource == source)
            || State.liveQueue.Exists(beat => beat.interactionSource == source)) return;
        WithInteractionSource(source, interaction);
    }

    void WithInteractionSource(string source, Action action)
    {
        string previous = interactionSource;
        interactionSource = source;
        try { action(); }
        finally { interactionSource = previous; }
    }
    void Effect(string key, string value = null, float seconds = 0f) => Enqueue("effect", key, value, seconds);
    void Art(string id, string titleKey, float autoCloseSeconds = 0f)
        => Enqueue("art", titleKey, id, autoCloseSeconds, autoCloseSeconds > 0f ? 1 : 0);
    string ArtTitle(string key) => key != null && key.StartsWith("os.home.", StringComparison.Ordinal)
        ? GameDataManager.Instance.GetLocalizedText(key, string.Empty) : Text(key);
    static string ArtId(OSChapter03StoryBeat beat)
    {
        if (!string.IsNullOrEmpty(beat.value)) return beat.value;
        // Saves made while these old placeholders were open contain only their title key.
        switch ((beat.key ?? string.Empty).Replace("os.chapter03.", string.Empty))
        {
            case "ui.art.old_leg": return "old-leg";
            case "ui.art.cast": return "plaster-face";
            case "ui.art.hairclip": return "hairclip";
            default: throw new InvalidOperationException("Unknown Chapter 3 saved art key: " + beat.key);
        }
    }
    static bool IsRetiredArt(OSChapter03StoryBeat beat)
        => beat.value == "plaster-face" || beat.key == "ui.art.cast" || beat.key == "os.chapter03.ui.art.cast";
    void WaitLive() => Enqueue("wait_live");
    bool Once(string key) => State.Add(key);
    bool Has(string key) => State.Has(key);

    void OpenQueuedPaper(OSChapter03StoryBeat beat)
    {
        PlayPaperSfx(beat);
        beat.begun = true; paperAlongside = beat;
        int token = generation;
        presentation.ShowPaper(PaperTitle(beat.key), Content.PaperBody(beat.key), () =>
        {
            if (token != generation || !ReferenceEquals(observed, State)) return;
            WithInteractionSource(beat.interactionSource, () => CollectPaper(beat.key));
            beat.complete = true;
        }, PaperStyle(beat.key));
    }

    void TickMemoryQueue()
    {
        var q = State.memoryQueue;
        if (q.Count == 0) { runtimeMemoryBeat = null; return; }
        var beat = q[0];
        if (!ReferenceEquals(runtimeMemoryBeat, beat))
        { runtimeMemoryBeat = beat; runtimeMemoryStarted = false; effectClock = 0f; }
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        // A save can land between a presentation callback and this queue commit.
        // Its completed visual must not be recreated after restoring that save.
        if (beat.complete && (beat.type == "paper" || beat.type == "art" || beat.type == "effect"))
        { q.RemoveAt(0); runtimeMemoryBeat = null; return; }
        if (beat.type == "text" && q.Count > 1 && q[1].type == "paper" && q[1].number == 1
            && !q[1].complete && !ReferenceEquals(paperAlongside, q[1]))
            OpenQueuedPaper(q[1]);
        switch (beat.type)
        {
            case "text":
                if (!runtimeMemoryStarted)
                {
                    if (memory.CurrentData != null && !memory.IsCurrentTextComplete && !beat.begun) return;
                    int node = Content.Node(beat.key);
                    if (node < 0) { Debug.LogError("[Chapter03] Missing memory key: " + beat.key); return; }
                    runtimeMemoryStarted = true;
                    PrepareTextPresentation(beat.key, firstBegin: !beat.begun);
                    bool alreadyDisplayed = beat.begun && memory.CurrentData?.DataIndex == node;
                    beat.begun = true;
                    if (!alreadyDisplayed) memory.ShowData(node);
                    else if (memory.IsCurrentTextComplete) beat.complete = true;
                }
                if (memory.CurrentData?.DataIndex == Content.Node(beat.key) && memory.IsCurrentTextComplete)
                    beat.complete = true;
                if (!beat.complete) return;
                beat.remaining = Mathf.Max(0f, beat.remaining - dt);
                if (beat.remaining > 0f) return;
                break;
            case "paper":
                if (!runtimeMemoryStarted)
                {
                    runtimeMemoryStarted = true;
                    if (!ReferenceEquals(paperAlongside, beat) || !presentation.IsModalOpen) OpenQueuedPaper(beat);
                }
                if (!beat.complete) return;
                break;
            case "art":
                // The cast illustration was withdrawn. Resume older saved queues
                // without requiring that removed resource or another dismissal.
                if (IsRetiredArt(beat)) break;
                if (!runtimeMemoryStarted)
                {
                    int token = generation;
                    presentation.ShowArt(ArtId(beat), ArtTitle(beat.key), () => {
                        if (token == generation && ReferenceEquals(observed, State)) beat.complete = true;
                    }, dismissible: beat.number == 0);
                    runtimeMemoryStarted = true; beat.begun = true;
                }
                if (beat.number > 0 && !beat.complete)
                {
                    beat.remaining = Mathf.Max(0f, beat.remaining - dt);
                    if (beat.remaining <= 0f) presentation.HideModal(true);
                }
                if (!beat.complete) return;
                break;
            case "wait_live":
                if (State.liveQueue.Count > 0 || messages.IsWaitingForOption) return;
                break;
            case "live": WithInteractionSource(beat.interactionSource, () => Live(beat.key)); break;
            case "flag": State.Add(beat.key); break;
            case "phase": State.phase = beat.number; break;
            case "delay":
                beat.remaining -= dt; if (beat.remaining > 0f) return; break;
            case "effect":
                if (!RunEffect(beat, dt)) return;
                break;
            case "walk":
                if (view == null || view.CurrentRoom == null) return;
                if (!runtimeMemoryStarted)
                {
                    runtimeMemoryStarted = true; int token = generation;
                    Vector2Int tile = ParseTile(beat.value);
                    if (!view.WalkTo(tile, () => { if (token == generation) beat.complete = true; }))
                    { view.Teleport(tile); beat.complete = true; }
                }
                if (!beat.complete) return;
                break;
            case "room":
                if (view == null || view.CurrentRoom == null) return;
                if (!runtimeMemoryStarted)
                {
                    PlayRoomBeatSfx(beat);
                    runtimeMemoryStarted = true;
                    rebinding = true;
                    try { view.EnterRoom(beat.key, ParseTile(beat.value), true); }
                    finally { rebinding = false; }
                }
                if (view.IsTransitioning) return;
                home.CaptureViewState(); break;
            case "keypad":
                if (!RunKeypad(beat)) return;
                break;
            case "letter":
                if (!RunLetter(beat, dt)) return;
                break;
            default: Debug.LogError("[Chapter03] Unknown saved beat: " + beat.type); return;
        }
        if (q.Count > 0 && ReferenceEquals(q[0], beat)) q.RemoveAt(0);
        runtimeMemoryBeat = null;
    }

    void TickLiveQueue()
    {
        messages?.MaintainCursorExperimentMessageReservation();
        var q = State.liveQueue;
        if (messages == null || q.Count == 0) { runtimeLiveBeat = null; return; }
        var beat = q[0];
        // The storage choice may still be pending when the mirror sequence is queued.
        // Resolve this reference to the answer at playback, preserving already-started saved beats.
        if (!beat.begun && !beat.complete && State.knowAnswer > 0
            && (beat.key == "live.breakdown.04_yes" || beat.key == "live.breakdown.04_no"))
            beat.key = State.knowAnswer == 1 ? "live.breakdown.04_yes" : "live.breakdown.04_no";
        if (!ReferenceEquals(runtimeLiveBeat, beat)) { runtimeLiveBeat = beat; runtimeLiveStarted = false; }
        if (!beat.begun && !beat.complete && beat.remaining > 0f)
        {
            beat.remaining = Mathf.Max(0f, beat.remaining - Mathf.Min(Time.unscaledDeltaTime, .1f));
            if (beat.remaining > 0f) return;
        }
        if (liveWindow != null && !beat.complete)
        {
            if (!liveWindow.IsOpen || liveWindow.IsMinimized)
            { os.OpenCharacterWindowForReconnect(.15f); return; }
            if (liveWindow.IsOpening || liveWindow.IsClosing) return;
        }
        if (beat.complete)
        {
            // The controller's visible countdown is the reading time. A second
            // length-based timer used to advance while that line was still full.
            if (messages.DisplayedEntryId == LiveId(beat.key) && messages.IsReadingHoldActive)
            {
                beat.remaining = messages.ReadingHoldRemaining;
                runtimeLiveStarted = true;
                return;
            }
            // Loading clears transient Link UI. Rebuild the saved reading hold
            // instead of treating text completion as countdown completion.
            if (!runtimeLiveStarted && beat.remaining > 0f
                && liveWindow != null && liveWindow.IsOpen)
            {
                if (liveWindow.IsMinimized) return;
                if (messages.RestoreChapter03ReadingHold(LiveId(beat.key), beat.remaining))
                { runtimeLiveStarted = true; return; }
            }
            if (messages.LinkWindow != null && messages.LinkWindow.IsClosing) return;
            beat.remaining = 0f;
            AfterLiveCompleted(beat.key); q.RemoveAt(0); runtimeLiveBeat = null; return;
        }
        string id = LiveId(beat.key);
        if (messages.DisplayedEntryId == id && (messages.IsPresenting || messages.IsWaitingForOption))
        { runtimeLiveStarted = true; return; }
        // Closing a non-cinematic Live window cancels its transient controller
        // presentation. The durable queue still owns this line and must retry it.
        if (runtimeLiveStarted && !messages.HasChapter03EntryPending(id)) runtimeLiveStarted = false;
        if (!runtimeLiveStarted)
        {
            messages.SetGlobalMessageConsumed(id, false);
            runtimeLiveStarted = messages.NotifyGlobalExternalSignal(Signal(beat.key));
            beat.begun |= runtimeLiveStarted;
        }
    }

    void OnLiveStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (IsActive && entry != null && IsOwnEntry(entry.EntryId)) BeginLiveTextEffect(entry.EntryId);
    }
    void OnLiveCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (!IsActive || entry == null || State.liveQueue.Count == 0) return;
        var beat = State.liveQueue[0];
        if (entry.EntryId != LiveId(beat.key)) return;
        beat.complete = true;
        runtimeLiveStarted = true;
        beat.remaining = messages.IsReadingHoldActive ? messages.ReadingHoldRemaining : 0f;
    }
    void OnLiveTextCompleted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (!IsActive || entry == null || !IsOwnEntry(entry.EntryId)) return;
        OnLiveTextEffect(entry.EntryId);
    }
    void OnLiveOption(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (!IsActive || entry == null || option == null || State.liveQueue.Count == 0) return;
        var beat = State.liveQueue[0];
        if (entry.EntryId != LiveId(beat.key)) return;
        WithInteractionSource(beat.interactionSource, () => HandleChoice(beat.key, option.OptionId));
    }

    static Vector2Int ParseTile(string value)
    {
        var parts = (value ?? "7,4").Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y)
            ? new Vector2Int(x, y) : new Vector2Int(7, 4);
    }
}
