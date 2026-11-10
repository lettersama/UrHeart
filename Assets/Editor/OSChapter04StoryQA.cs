using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in backend regression. Dialogue below exists only in an in-memory clone
// of the interaction asset. It is test text, never authored chapter content.
public static class OSChapter04StoryQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Prefix = "chapter04_story_qa_";
    static readonly List<string> checks = new List<string>();
    static readonly List<string> runtimeErrors = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly Dictionary<MonoBehaviour, bool> focusStates = new Dictionary<MonoBehaviour, bool>();
    static readonly List<OSChapter04PresentationBridge.PresentationRequest> requests = new List<OSChapter04PresentationBridge.PresentationRequest>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04StoryDirector director;
    static OSChapter04EntryDirector entry;
    static OSWindowUnit live, settings;
    static OSLinkMessageController observedLink;
    static OSChapter04PresentationBridge observedBridge;
    static OSChapter04StoryDirector observedDirector;
    static OSCharacterInteractionData_SO interactionClone;
    static OSChapter04StoryContent fixtureContent;
    static Mouse mouse;
    static Keyboard keyboard;
    static float originalTypingSpeed;
    static bool hasOriginalTypingSpeed, cancelled;
    static int textCompletions, resourceCues, finalTreatmentRequests;
    static string context;
    static OSLinkMessageController Link => os.LinkMessageController;
    static OSChapter04StoryState State => director.State;
    static OSChapter04PresentationBridge Bridge => director.Presentation;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    public static string Start()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameManager.Instance == null
            || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || SceneLoadManager.Instance.IsBusy
            || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); runtimeErrors.Clear(); observations.Clear(); focusStates.Clear(); requests.Clear();
        data = GameDataManager.Instance; cancelled = hasOriginalTypingSpeed = false;
        textCompletions = resourceCues = finalTreatmentRequests = 0;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter04StoryQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var originalSave = data.SaveData;
        var originalInteraction = data.OSCharacterInteractionData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current;
        var previousKeyboard = Keyboard.current;
        string interactionPath = UnityEditor.AssetDatabase.GetAssetPath(originalInteraction);
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp", interactionPath }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        Exception failure = null;
        var work = new Stack<IEnumerator>();
        Application.logMessageReceived += CaptureRuntimeError;
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(originalSave));
            data.SetCurrentLanguage(LanguageState.zh_CN); Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            Application.runInBackground = true; AudioListener.volume = 0f;
            mouse = InputSystem.AddDevice<Mouse>("Chapter04StoryQAMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("Chapter04StoryQAKeyboard"); ReleaseInput();
            interactionClone = UnityEngine.Object.Instantiate(originalInteraction);
            interactionClone.name = originalInteraction.name + " (Chapter04 QA only)";
            interactionClone.hideFlags = HideFlags.DontSave;
            Set(data, "osCharacterInteractionData", interactionClone);
            AddFixtureEntries();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancelled) throw new OperationCanceledException("Chapter 4 story QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + failure;
                    Debug.LogError("[Chapter04 Story QA] " + Status);
                    break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null)
            {
                Check(files.Select(Hash).SequenceEqual(hashes), "formal save, source dialogue asset and scenes are unchanged");
                if (runtimeErrors.Count == 0)
                {
                    checks.Add("runtime: no Error, Exception or Assert logged during QA execution");
                    Status = "PASS";
                }
                else Status = "FAILED: " + runtimeErrors.Count + " runtime Error/Exception/Assert log(s) captured during QA execution";
            }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                UnbindObservers();
                if (hasOriginalTypingSpeed && os?.LinkMessageController != null)
                    Set(os.LinkMessageController, "charactersPerSecond", originalTypingSpeed);
                director?.DebugSetFocus(false); director?.DebugSetContent(null); director?.ResetPresentation(); entry?.ResetPresentation();
                os?.Chapter03Story?.ResetPresentation();
                os?.MemoryWindowController?.GetComponent<OSChapter03HomeController>()?.Suspend();
                os?.LinkMessageController?.NotifyOSHidden();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                foreach (var pair in focusStates) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                bool focusRestored = focusStates.All(pair => pair.Key == null || Equals(Get(pair.Key, "hasFocus"), pair.Value));
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                Set(data, "osCharacterInteractionData", originalInteraction);
                data.ReplaceSaveData(originalSave); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourceEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                if (interactionClone != null) UnityEngine.Object.Destroy(interactionClone);
                if (!ReferenceEquals(data.SaveData, originalSave) || data.OSCharacterInteractionData != originalInteraction
                    || data.CurrentLanguageState != language || GameSetup.IsDemo != demo || !focusRestored
                    || Application.runInBackground != background || AudioListener.volume != volume
                    || resources != null && resources.enabled != resourceEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse
                    || previousKeyboard != null && previousKeyboard.added && Keyboard.current != previousKeyboard
                    || !UnityEngine.Random.state.Equals(random))
                    throw new InvalidOperationException("Original session settings were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes))
                    throw new IOException("Formal save, dialogue asset or scene changed");
                checks.Add("cleanup: restores original save, dialogue asset, language, Demo, resources, audio, background, input, focus and random state");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            Application.logMessageReceived -= CaptureRuntimeError;
            if (Status == "PASS" && runtimeErrors.Count > 0)
                Status = "FAILED: " + runtimeErrors.Count + " runtime Error/Exception/Assert log(s) captured during QA cleanup";
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, runtimeErrors = runtimeErrors.ToArray(), originalFiles = files,
                originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                note = "Isolated, muted Play Mode. An in-memory dialogue clone supplies QA-only text. Backend state, real Live typing/option callbacks, window controls and session persistence are tested. No TV renderer, artwork, animation or authored chapter dialogue is installed."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter04 Story QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; director = null; entry = null; live = settings = null;
            interactionClone = null; fixtureContent = null; mouse = null; keyboard = null;
        }
    }

    static IEnumerator Run()
    {
        BridgeContract();
        context = "dialogue data contract";
        Check(!OSChapter04StoryContent.FromJson("{\"version\":1,\"beats\":[]}").Available,
            "an empty production manifest is valid but unavailable");
        fixtureContent = OSChapter04StoryContent.FromJson(FixtureJson());
        Check(fixtureContent.Available && fixtureContent.Validate(interactionClone).Count == 0,
            "the independent QA manifest resolves every global entry and route");
        var invalid = OSChapter04StoryContent.FromJson(FixtureJson().Replace("CityArrive", "NotATheatreState"));
        Check(invalid.Validate(interactionClone).Count > 0, "an unknown stage command fails content validation");
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart), "existing OS checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Bind();
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint runs in isolated storage");
        yield return PrepareAwaitTV();
        yield return EmptyContentAndFirstOpen();
        yield return TypingAndWindowLifecycle();
        yield return CueAndOptionPersistence();
        yield return AutomaticReadingHold();
        yield return SameSaveChapterExit();
        yield return GriefAndFailureWarning();
    }

    static void BridgeContract()
    {
        context = "presentation bridge contract";
        var host = new GameObject("Chapter04BridgeQA");
        try
        {
            var bridge = host.AddComponent<OSChapter04PresentationBridge>();
            int requested = 0, changed = 0, cleared = 0;
            bridge.PresentationRequested += _ => requested++;
            bridge.StateChanged += _ => changed++;
            bridge.PresentationCleared += () => cleared++;
            Check(!bridge.IsBound && bridge.IsPaused, "unbound presentation starts paused");
            Check(!bridge.Goto("CityArrive") && !bridge.Bind("unknown", false), "unbound and unknown commands are rejected");
            Check(bridge.Bind("CityWalk", false), "a fresh opening binds CityWalk");
            var initial = bridge.CurrentRequest;
            Check(initial.StateId == "CityWalk" && !initial.IsRestore && initial.Revision == bridge.Revision,
                "a late frontend can read the complete opening request");
            Check(bridge.Goto("CityWalk") && requested == 1, "duplicate opening commands do not restart the frontend");
            Check(bridge.Goto("CityArrive") && requested == 2, "a story tag requests arrival exactly once");
            int revision = bridge.Revision;
            Check(!bridge.ReportState("DoorLocked", revision), "paused animation cannot commit a transition");
            bridge.SetPaused(false);
            Check(!bridge.ReportState("FoldToLab", revision), "an automatic transition cannot skip a state");
            Check(!bridge.ReportState("DoorLocked", initial.Revision), "a stale animation callback is rejected");
            Check(bridge.ReportState("DoorLocked", revision), "the arrival reports the locked door");
            Check(bridge.ReportState("DoorLocked", revision) && requested == 2, "duplicate reports are idempotent without a new animation request");
            Check(bridge.ReportState("FoldToLab", revision) && bridge.ReportState("LabRummage", revision),
                "the frontend can report the complete automatic chain");
            Check(bridge.Revision == revision && bridge.CurrentState == "LabRummage" && requested == 2,
                "automatic boundaries preserve the command revision");
            Check(bridge.Bind("CityArrive", true) && bridge.CurrentState == "AtDoor",
                "restoring arrival waits at the door instead of skipping line 9");
            foreach (string state in new[] { "EnterDoor", "DoorLocked", "FoldToLab" })
            {
                Check(bridge.Bind(state, true) && bridge.CurrentState == "LabRummage" && bridge.CurrentRequest.IsRestore,
                    "restoring " + state + " resolves directly to the final loop");
                Check(!bridge.ReportState("DoorLocked", revision), "restore rejects old callbacks from " + state);
            }
            Check(bridge.Bind("CityWalk", true) && bridge.CurrentState == "CityWalk", "restoring CityWalk preserves its loop");
            Check(bridge.Bind(OSChapter04PresentationBridge.FlowerWalk, true), "the grave-section bridge starts from FlowerWalk");
            bridge.SetPaused(false);
            foreach (string state in new[] { OSChapter04TheaterStage.FlowerWalkRain, OSChapter04PresentationBridge.Grave,
                OSChapter04TheaterStage.GravePlaceFlowers, OSChapter04PresentationBridge.GraveBreakdown })
            {
                Check(OSChapter04PresentationBridge.IsKnownState(state) && OSChapter04PresentationBridge.IsLaterScene(state)
                    && OSChapter04PresentationBridge.IsLoopState(state), "the grave-section state is registered as a stable loop: " + state);
                int before = requested;
                Check(bridge.Goto(state) && bridge.CurrentState == state && !bridge.CurrentRequest.IsRestore
                    && requested == before + 1, "the live bridge accepts the grave-section cue once: " + state);
                Check(bridge.Goto(state) && requested == before + 1, "the duplicate grave-section cue is ignored: " + state);
                Check(bridge.Bind(state, true) && bridge.CurrentState == state && bridge.CurrentRequest.IsRestore,
                    "the saved grave-section state restores to the same final picture: " + state);
                bridge.SetPaused(false);
            }
            int savedChanges = changed;
            bridge.Unbind();
            Check(!bridge.IsBound && bridge.IsPaused && cleared == 1 && changed == savedChanges,
                "unbinding clears runtime requests without writing a new saved state");
        }
        finally { UnityEngine.Object.Destroy(host); }
    }

    static IEnumerator PrepareAwaitTV()
    {
        context = "await TV fixture";
        Link.NotifyOSHidden(); entry.ResetPresentation(); director.ResetPresentation(); os.Chapter03Story?.ResetPresentation();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true)) if (window.IsOpen) window.CloseSilently();
        var memory = os.MemoryWindowController;
        memory?.GetComponent<OSChapter03HomeController>()?.ResetRuntime(); memory?.PrepareRuntime();
        var save = Clone(data.SaveData);
        save.osProcess.currentChapterIndex = 3;
        if (!save.osProcess.installedToolProgramIds.Contains("tv")) save.osProcess.installedToolProgramIds.Add("tv");
        save.osProcess.memory.chapter04EntryStep = (int)OSChapter04EntryDirector.EntryStep.AwaitTV;
        save.osProcess.memory.chapter04EntryElapsed = 0f;
        save.osProcess.memory.chapter04Story = new OSChapter04StoryState();
        save.osProcess.memory.currentDataIndex = 11090;
        save.osProcess.memory.chapter03Story = new OSChapter03StoryState { version = 1,
            phase = (int)OSChapter03StoryDirector.Phase.Complete, homeOpened = true };
        save.osProcess.memory.chapter03Story.Add("letter_collected");
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        save.osResume = new OSResumeSaveData();
        director.DebugSetContent(OSChapter04StoryContent.FromJson("{\"version\":1,\"beats\":[]}"));
        data.ReplaceSaveData(save); Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => Ready(live) && os.TVDesktopIcon != null && os.TVDesktopIcon.gameObject.activeInHierarchy,
            "existing entry accepts an installed TV while Live is open");
        Check(!State.started && string.IsNullOrEmpty(State.currentEntryId), "installation alone has not started the story");
    }

    static IEnumerator EmptyContentAndFirstOpen()
    {
        context = "empty content / first TV opening";
        int chapters = 0;
        Action<int> onChapter = chapter => { if (chapter == 4) chapters++; };
        os.ChapterStarted += onChapter;
        try
        {
            Check(os.OpenTVWindow(), "the production TV launcher accepts first open");
            yield return Wait(() => Ready(os.TVWindow) && State.started && Bridge.IsBound, "first TV opening initializes backend presentation");
            Check(data.CurrentChapterIndex == 4 && entry.Step == OSChapter04EntryDirector.EntryStep.Complete && chapters == 1,
                "first TV opening owns the single chapter-start boundary");
            Check(State.theatreState == "CityWalk" && string.IsNullOrEmpty(State.currentEntryId) && State.history.Count == 0,
                "absent production text initializes only CityWalk and does not invent dialogue");
            int revision = Bridge.Revision;
            os.OpenTVWindow(); yield return Hold(.15f);
            Check(Bridge.Revision == revision && chapters == 1 && State.history.Count == 0,
                "opening the existing TV does not restart the chapter or its presentation");
            director.DebugSetContent(fixtureContent);
            yield return Wait(() => Current(1) && director.IsAvailable, "supplying the QA manifest starts its first line");
            Check(State.currentVisit == 1 && !State.currentTextCompleted, "the first line has one unfinished visit");
        }
        finally { os.ChapterStarted -= onChapter; }
    }

    static IEnumerator TypingAndWindowLifecycle()
    {
        context = "typing pause and window lifecycle";
        Check(!State.currentTextCompleted && !director.CanContinue, "typing cannot skip ahead before completion");
        settings.OpenStandalone(); yield return Wait(() => Ready(settings), "settings opens over the active line");
        yield return AssertPaused("settings");
        settings.CloseFromScript(); yield return Wait(() => director.IsAvailable, "closing settings resumes the current line");

        os.TVWindow.Minimize(); yield return Wait(() => os.TVWindow.IsMinimized, "TV minimizes through its normal control");
        yield return AssertPaused("minimized TV");
        os.OpenTVWindow(); yield return Wait(() => director.IsAvailable && Current(1), "TV restore resumes the same line");

        live.Minimize(); yield return Wait(() => live.IsMinimized, "Live minimizes through its normal control");
        yield return AssertPaused("minimized Live");
        string minimizedLive = Capture("live-minimized");
        yield return Restore(minimizedLive, "minimized Live");
        yield return Hold(.2f);
        Check(live.IsOpen && live.IsMinimized, "restoring a minimized Live window keeps it minimized");
        yield return AssertPaused("saved minimized Live");
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => director.IsAvailable && Current(1), "Live restore resumes the same line");

        os.TVWindow.Close(); yield return Wait(() => !os.TVWindow.IsOpen && !os.TVWindow.IsClosing, "TV closes normally");
        yield return AssertPaused("closed TV");
        int beforeReopen = Bridge.Revision;
        os.OpenTVWindow(); yield return Wait(() => director.IsAvailable && Current(1), "reopening TV preserves current story progress");
        Check(Bridge.Revision == beforeReopen, "ordinary window reopening does not reissue the opening performance");

        live.Close(); yield return Wait(() => !live.IsOpen && !live.IsClosing, "Live closes normally");
        yield return AssertPaused("closed Live");
        yield return Hold(.3f);
        Check(!live.IsOpen, "the story respects the player's closed Live window");
        string closedLive = Capture("live-closed");
        yield return Restore(closedLive, "closed Live");
        yield return Hold(.3f);
        Check(!live.IsOpen && !live.IsOpening, "loading a completed-entry save does not reopen the player's closed Live window");
        yield return AssertPaused("saved closed Live");
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => director.IsAvailable && Current(1), "reopening Live reconstructs the unfinished current line");

        var attached = director.GetComponent<OSChapter04ChatView>();
        Check(attached != null && attached.OwnsLayout && attached.HostWindow == live
            && attached.Root.IsChildOf(live.transform), "the conversation belongs to the Live window");
        Check(attached.Root.GetComponentsInChildren<OSWindowButton>(true).Length == 0,
            "the attached conversation has no independent close or minimize controls");
        // A pre-attachment save can still contain the old independent chat's
        // suspension flag. Explicitly opening Live must recover that save.
        director.NotifyDialogueDismissed();
        Link.LinkWindow.Close();
        yield return AssertPaused("legacy suspended chat");
        Check(State.dialogueSuspended && Ready(live) && Ready(os.TVWindow),
            "the legacy saved suspension remains meaningful with an attached view");
        string closedChat = Capture("chat-closed");
        yield return Restore(closedChat, "legacy suspended chat");
        yield return Hold(.3f);
        Check(State.dialogueSuspended,
            "restoring a legacy suspended chat does not silently advance it");
        yield return AssertPaused("saved legacy suspended chat");
        os.NotifyTaskbarProcessPressed(live);
        yield return Wait(() => director.IsAvailable && Current(1), "pressing the Live taskbar resumes the legacy suspended chat");
        Check(!State.dialogueSuspended && State.currentVisit == 1 && State.history.Count == 0,
            "chat resume neither skips a visit nor manufactures a transcript entry");

        string untyped = Capture("typing");
        yield return Restore(untyped, "unfinished text");
        yield return Wait(() => director.IsAvailable && Current(1), "unfinished text restores at its current line");
        Check(!State.currentTextCompleted && State.history.Count == 0 && State.currentVisit == 1,
            "restoring unfinished text does not consume or duplicate it");
        Finish(1, false); yield return null;
        Check(State.currentTextCompleted && director.CanContinue && State.history.Count == 1,
            "normal text completion makes one durable chat record and enables continuation");
        string completed = Capture("completed-line"); int completions = textCompletions;
        yield return Restore(completed, "completed text");
        yield return Wait(() => director.IsAvailable && Current(1) && director.CanContinue, "completed text is restored ready to continue");
        yield return Hold(.2f);
        Check(textCompletions == completions && State.history.Count == 1 && State.currentTextCompleted,
            "restoring completed text shows it without replaying typing completion or chat history");
        Check(director.TryContinue(), "the frontend continue command advances one line");
        Check(!director.TryContinue(), "a duplicate continue command cannot skip an unfinished successor");
        yield return Wait(() => Current(2), "the second line is presented on the following update");
        Link.NotifyGlobalExternalSignal("chapter04.qa.intruder"); yield return Hold(.2f);
        Check(Current(2) && State.currentEntryId == Id(2), "an unrelated global signal cannot replace the reserved story line");
    }

    static IEnumerator CueAndOptionPersistence()
    {
        context = "tag dispatch and option persistence";
        for (int i = 2; i <= 5; i++)
        {
            yield return Wait(() => Current(i) && director.IsAvailable, "fixture line " + i + " is ready");
            Finish(i, false);
            Check(director.TryContinue(), "completed line " + i + " advances through its manifest route");
        }
        yield return Wait(() => Current(6) && director.IsAvailable, "the tagged option line is typing");
        Check(State.theatreState == "CityWalk" && requests.Count(request => request.StateId == "CityArrive" && !request.IsRestore) == 0,
            "the stage tag does not run at presentation start");
        Finish(6, true); yield return null;
        Check(State.currentTextCompleted && State.currentCueCommitted && State.theatreState == "CityArrive"
            && Link.IsWaitingForOption, "finishing the tagged line commits the stage request while leaving its choice pending");
        Check(requests.Count(request => request.StateId == "CityArrive" && !request.IsRestore) == 1,
            "the tag produces exactly one new presentation request");
        Link.TryCompleteDisplayedOptionTextImmediately(Id(6)); yield return null;
        Check(requests.Count(request => request.StateId == "CityArrive" && !request.IsRestore) == 1
            && State.history.Count(record => record.entryId == Id(6)) == 1,
            "duplicate text-complete callbacks cannot repeat the cue or chat record");
        Check(!director.TryContinue(), "continue cannot bypass an unanswered option");
        data.SetCurrentLanguage(LanguageState.en);
        yield return Wait(() => Current(6) && Link.IsWaitingForOption, "language change rebuilds the completed choice", 3f);
        Check(requests.Count(request => request.StateId == "CityArrive" && !request.IsRestore) == 1
            && State.history.Count(record => record.entryId == Id(6)) == 1,
            "changing language does not commit another stage cue or transcript record");
        data.SetCurrentLanguage(LanguageState.zh_CN);
        yield return Wait(() => Current(6) && Link.IsWaitingForOption, "original language restores the completed choice", 3f);
        string choice = Capture("waiting-choice");
        int staleRevision = Bridge.Revision;
        yield return Restore(choice, "waiting option with an in-flight transition");
        yield return Wait(() => Current(6) && director.IsAvailable && Link.IsWaitingForOption, "completed option text restores with its real selectable rows");
        Check(State.theatreState == "AtDoor" && Bridge.CurrentRequest.IsRestore,
            "save restore resolves arrival to the stable door wait");
        Check(!Bridge.ReportState("DoorLocked", staleRevision), "an animation callback from the previous save cannot mutate restored progress");
        Check(requests.Count(request => request.StateId == "CityArrive" && !request.IsRestore) == 1,
            "restoring the choice does not replay its narrative tag");
        int visit = State.currentVisit;
        Check(Link.TryGetActiveOptionTarget("proceed", out var target), "restored choice exposes its production option target");
        var row = target.GetComponentInParent<OSLinkMessageSelectUnit>();
        Check(row != null && row.IsInteractable, "the restored choice is interactable");
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        row.OnPointerClick(pointer); row.OnPointerClick(pointer);
        yield return Wait(() => Current(7), "one option selection advances to the terminal response");
        Check(State.currentVisit == visit + 1 && State.history.Count(record => record.entryId == Id(6) && record.optionId == "proceed") == 1,
            "duplicate option clicks persist a single answer and visit advance");
        string answered = Capture("answered-choice");
        yield return Restore(answered, "answered choice");
        yield return Wait(() => Current(7) && director.IsAvailable, "the answered-choice save resumes at its response");
        Check(State.history.Count(record => record.entryId == Id(6) && record.optionId == "proceed") == 1,
            "the chat transcript preserves the selected answer across session restore");
        Finish(7, false);
        Check(director.TryContinue() && State.completed, "continuing the final line marks this authored fragment complete");
        int historyCount = State.history.Count, finalVisit = State.currentVisit;
        Check(historyCount == 7, "the complete fixture records every line once");
        os.TVWindow.Close(); yield return Wait(() => !os.TVWindow.IsOpen && !os.TVWindow.IsClosing, "completed TV can close");
        os.OpenTVWindow(); yield return Wait(() => Ready(os.TVWindow), "completed TV can reopen");
        Check(State.completed && State.history.Count == historyCount && State.currentVisit == finalVisit
            && State.theatreState == "AtDoor", "reopening after completion does not restart story, transcript or stage");
        Capture("fragment-complete");
    }

    static IEnumerator AssertPaused(string label)
    {
        yield return null; yield return null;
        string entryId = State.currentEntryId;
        int visit = State.currentVisit, historyCount = State.history.Count;
        string text = DisplayedText(); bool completed = State.currentTextCompleted;
        yield return Hold(.25f);
        Check(!director.IsAvailable && Bridge.IsPaused, label + " pauses the backend and presentation contract");
        Check(State.currentEntryId == entryId && State.currentVisit == visit && State.history.Count == historyCount
            && State.currentTextCompleted == completed && DisplayedText() == text, label + " does not advance or type hidden text");
    }

    static IEnumerator AutomaticReadingHold()
    {
        context = "automatic reading hold and saved remaining time";
        const float holdDuration = 1.2f;
        fixtureContent = OSChapter04StoryContent.FromJson(FixtureJson(holdDuration));
        Check(fixtureContent.Validate(interactionClone).Count == 0, "the automatic-hold fixture uses the same validated dialogue data");
        Link.NotifyOSHidden(); director.ResetPresentation();
        var save = Clone(data.SaveData);
        save.osProcess.memory.chapter04Story = new OSChapter04StoryState();
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        data.ReplaceSaveData(save); director.DebugSetContent(fixtureContent);
        Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f); os.OpenTVWindow();
        yield return Wait(() => director.IsAvailable && Current(1), "the independent automatic-hold fixture opens its first line");
        Finish(1, false);
        yield return Wait(() => State.currentReadElapsed >= .18f, "completed text accumulates visible reading time", 2f);
        Check(State.currentEntryId == Id(1) && State.currentReadElapsed < .6f,
            "the configured reading hold does not immediately advance the line");

        settings.OpenStandalone();
        float settingsClock = State.currentReadElapsed;
        yield return Wait(() => Ready(settings), "settings opens during the automatic reading hold");
        yield return Hold(.35f);
        Check(!director.IsAvailable && Mathf.Abs(State.currentReadElapsed - settingsClock) < .025f
            && State.currentEntryId == Id(1), "settings pauses the automatic reading clock as well as text playback");

        os.TVWindow.Close(); settings.CloseFromScript();
        yield return Wait(() => !os.TVWindow.IsOpen && !os.TVWindow.IsClosing, "TV closes while the reading clock is paused");
        float closedClock = State.currentReadElapsed;
        yield return Hold(.35f);
        Check(!director.IsAvailable && Mathf.Abs(State.currentReadElapsed - closedClock) < .025f
            && State.currentEntryId == Id(1), "a closed TV preserves the elapsed reading hold");

        string snapshot = Capture("automatic-hold-tv-closed");
        float savedElapsed = State.currentReadElapsed;
        int beforeRestoreCompletions = textCompletions;
        yield return Restore(snapshot, "partly elapsed automatic hold with a closed TV");
        yield return Hold(.25f);
        Check(!os.TVWindow.IsOpen && State.currentTextCompleted && State.currentEntryId == Id(1)
            && Mathf.Abs(State.currentReadElapsed - savedElapsed) < .025f,
            "real session restore preserves both the closed TV and the already-read hold clock");
        Check(textCompletions == beforeRestoreCompletions && State.history.Count == 1,
            "restoring the reading hold does not replay text completion or transcript insertion");

        os.OpenTVWindow();
        yield return Wait(() => director.IsAvailable && Current(1), "explicit TV reopen resumes the saved reading hold");
        float resumedClock = State.currentReadElapsed;
        float remaining = holdDuration - resumedClock;
        float resumedAt = Time.realtimeSinceStartup;
        Check(remaining > .35f && remaining < holdDuration,
            "the resumed automatic hold has a shorter, nonzero remaining duration");
        yield return Hold(Mathf.Max(0f, remaining - .2f));
        Check(State.currentEntryId == Id(1) && State.currentVisit == 1,
            "the automatic route waits for its saved remaining reading time");
        yield return Wait(() => State.currentEntryId == Id(2), "the reading hold automatically advances to its manifest successor", remaining + 3f);
        float elapsed = Time.realtimeSinceStartup - resumedAt;
        Check(elapsed >= remaining - .15f && State.currentVisit == 2,
            "the automatic route advances exactly once after the remaining hold expires");
        observations.Add(new { context, holdDuration, savedElapsed, resumedClock, remaining, elapsedAfterReopen = elapsed });
        yield return Wait(() => Current(2), "the automatic successor uses the normal Live presentation path");
        yield return Hold(.25f);
        Check(State.currentEntryId == Id(2) && State.currentVisit == 2 && State.history.Count == 1
            && !State.currentTextCompleted && !State.completed,
            "the successor retains its manual progression policy and is not skipped by the expired timer");
        Capture("automatic-hold-advanced-once");
    }

    static IEnumerator SameSaveChapterExit()
    {
        context = "chapter exit and return without replacing the save";
        var sameState = State;
        int visit = State.currentVisit, historyCount = State.history.Count;
        string entryId = State.currentEntryId;
        var layout = Link.WindowLayout;
        var ownerField = typeof(OSLinkMessageWindowLayout).GetField("externalLayoutOwner", Members);
        Check(layout != null && ownerField != null, "the existing chat layout exposes its actual temporary owner field");
        Check(ReferenceEquals(ownerField.GetValue(layout), director.GetComponent<OSChapter04ChatView>()), "the chat view owns the layout after the director hands it over");
        data.SetCurrentChapterIndex(5);
        yield return null; yield return null;
        Check(ReferenceEquals(State, sameState) && !director.IsActive,
            "leaving chapter four preserves the same story save object");
        Check(!Bridge.IsBound && ownerField.GetValue(layout) == null,
            "leaving the chapter releases both presentation binding and the external chat layout");
        data.SetCurrentChapterIndex(4);
        yield return Wait(() => director.IsAvailable && State.currentEntryId == entryId && Current(2),
            "returning to chapter four resumes the same unfinished line");
        Check(ReferenceEquals(State, sameState) && State.currentVisit == visit && State.history.Count == historyCount
            && State.currentEntryId == entryId, "returning with the same save object does not rewind or duplicate story progress");
    }

    // A second in-memory fragment supplies only diagnostic text. It exercises
    // the final grief flow without requiring or modifying the authored script.
    static IEnumerator GriefAndFailureWarning()
    {
        context = "grief fixture preparation";
        fixtureContent = OSChapter04StoryContent.FromJson(GriefFixtureJson());
        Check(fixtureContent.Validate(interactionClone).Count == 0,
            "the QA-only grief fragment validates its tiers, manual lines and warning action");
        Link.NotifyOSHidden(); director.ResetPresentation();
        var save = Clone(data.SaveData);
        save.osProcess.memory.chapter04Story = new OSChapter04StoryState
        { started = true, theatreState = OSChapter04PresentationBridge.FlowerWalk };
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        data.ReplaceSaveData(save); director.DebugSetContent(fixtureContent);
        Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f); os.OpenTVWindow();
        yield return Wait(() => CurrentGrief(1) && director.IsAvailable,
            "the independent grief fragment starts from the restored flower walk");
        int requestBaseline = finalTreatmentRequests;
        float finalTextFinishedAt = 0f;

        for (int line = 1; line <= 9; line++)
        {
            context = "grief fixture line " + line;
            yield return Wait(() => CurrentGrief(line) && director.IsAvailable && !director.IsOptionGestureBlocked,
                "the real Live typewriter presents the grief fixture line");
            var grief = OSChapter04GriefPresentation.Current;
            int tier = FixtureGriefTier(line), step = line >= 6 ? line - 6 : 0;
            Check(State.griefTier == tier && State.griefLineStep == step && (grief?.Tier ?? 0) == tier
                && !State.currentTextCompleted,
                "the saved tier and line step reach the frontend before typing completes");
            Check(!Link.IsWaitingForOption && !director.CanContinue,
                "every grief fixture line is manual plain text with no premature Continue");

            if (line == 7)
            {
                Check(grief.DarkLevel == 1, "the second tier-three line starts at the first line's completed darkness");
                yield return RestoreGriefLine(line, false, 1);
                yield return RestoreRetiredGriefLine();
                grief = OSChapter04GriefPresentation.Current;
            }

            int beforeCues = resourceCues;
            Check(director.TryCompleteCurrentText(), "the original text-completion input finishes the grief line");
            if (line == 9) finalTextFinishedAt = Time.realtimeSinceStartup;
            yield return Wait(() => State.currentTextCompleted && State.currentCueCommitted && State.currentResourcesCommitted,
                "grief text, theatre and resource receipts commit together");
            yield return null;
            var beat = fixtureContent.Find(GriefId(line));
            Check(resourceCues == beforeCues + (beat.heartWeight > 0 || beat.faultWeight > 0 ? 1 : 0),
                "the finished grief line emits exactly its one resource cue when weighted");
            if (!string.IsNullOrEmpty(beat.theatreStateAfterText))
                Check(State.theatreState == beat.theatreStateAfterText,
                    "the completed grief line records its exact theatre state");
            if (tier == 3)
            {
                yield return Wait(() => OSChapter04GriefPresentation.Current?.DarkLevel == step + 1,
                    "each completed tier-three line adds exactly one darkness step", 3f);
                Check(State.griefTier == 3 && State.griefLineStep == step,
                    "the tier-three step is durable independently of the frontend counter");
            }
            if (line == 6 || line == 7)
                yield return RestoreGriefLine(line, true, step + 1);
            if (line == 9) break;

            yield return Wait(() => director.CanContinue, "the completed grief line waits for a normal Continue input");
            // Let tier two's temporary pulse expire before evaluating tier
            // three's cumulative darkness, which has a different lifetime.
            if (line == 5) yield return Hold(1.1f);
            int visit = State.currentVisit;
            Check(director.TryContinue() && State.currentVisit == visit + 1,
                "one explicit Continue advances the grief fragment once");
        }

        context = "failure warning delay";
        Check(State.completed && director.FinalTreatmentStage == OSChapter04FinalTreatmentStage.WaitingForWarning
            && !director.CanContinue && !director.CanOpenFinalHeartHeal,
            "finishing the terminal text enters the saved waiting state without Continue or treatment access");
        Check(!director.TryContinue() && !director.TryOpenFinalHeartHeal() && finalTreatmentRequests == requestBaseline,
            "neither direct Continue nor treatment reopen can skip the warning delay");
        Check(!OSChapter04GriefPresentation.Current.IsFailureWarningVisible,
            "the failure warning is not shown on the final text-completion frame");
        string waiting = Capture("grief-warning-wait");
        yield return Hold(.3f);
        Check(director.FinalTreatmentStage == OSChapter04FinalTreatmentStage.WaitingForWarning
            && !OSChapter04GriefPresentation.Current.IsFailureWarningVisible,
            "the terminal waiting state does not immediately show the warning");
        yield return Wait(() => director.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FailureWarning
            && OSChapter04GriefPresentation.Current?.IsFailureWarningVisible == true,
            "the real two-second timer shows the modal failure warning", 5f);
        float warningDelay = Time.realtimeSinceStartup - finalTextFinishedAt;
        Check(warningDelay >= 1.9f, "the warning waits two real seconds after the final typed character");
        observations.Add(new { context, warningDelay, State.griefTier, State.griefLineStep,
            darkLevel = OSChapter04GriefPresentation.Current.DarkLevel });

        context = "restored warning delay";
        yield return Restore(waiting, "unfinished failure-warning delay");
        Check(director.FinalTreatmentStage == OSChapter04FinalTreatmentStage.WaitingForWarning
            && !OSChapter04GriefPresentation.Current.IsFailureWarningVisible && finalTreatmentRequests == requestBaseline,
            "restoring a partly elapsed delay hides the old warning and preserves the pending player gate");
        yield return Wait(() => director.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FailureWarning
            && OSChapter04GriefPresentation.Current?.IsFailureWarningVisible == true,
            "the restored waiting state eventually shows the warning without requesting treatment", 5f);

        context = "failure warning saved modal";
        var currentGrief = OSChapter04GriefPresentation.Current;
        Check(currentGrief.Tier == 3 && currentGrief.DarkLevel == 4 && State.griefLineStep == 3,
            "the final warning retains the last tier-three darkness step");
        yield return Hold(.3f);
        Check(finalTreatmentRequests == requestBaseline && !director.CanOpenFinalHeartHeal && !director.CanContinue
            && !director.TryOpenFinalHeartHeal() && !director.TryContinue(),
            "waiting at the visible warning never opens treatment or exposes Continue");
        int savedCues = resourceCues;
        int heart = data.GetOSResourceValue(ResourceType.Heart), fault = data.GetOSResourceValue(ResourceType.Fault);
        string warning = Capture("grief-failure-warning");
        yield return Restore(warning, "visible failure warning");
        yield return Wait(() => director.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FailureWarning
            && OSChapter04GriefPresentation.Current?.IsFailureWarningVisible == true,
            "the saved warning reopens as the same modal player gate");
        currentGrief = OSChapter04GriefPresentation.Current;
        Check(currentGrief.Tier == 3 && currentGrief.DarkLevel == 4 && resourceCues == savedCues
            && finalTreatmentRequests == requestBaseline && data.GetOSResourceValue(ResourceType.Heart) == heart
            && data.GetOSResourceValue(ResourceType.Fault) == fault,
            "restoring the visible warning preserves darkness and resources without replaying cues or treatment requests");

        context = "warning button starts treatment";
        var button = currentGrief.WarningButton;
        Check(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null,
            "the modal exposes its real enabled treatment button");
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        Check(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler),
            "the real warning button receives the player's pointer click");
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(finalTreatmentRequests == requestBaseline + 1 && !currentGrief.IsFailureWarningVisible,
            "the first warning click requests treatment once and a duplicate click cannot repeat it");
        yield return Wait(() => director.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FinalHeal,
            "confirmation opens the final treatment through the production handoff");
        var treatment = director.FinalHeal;
        Check(treatment != null && treatment.IsRunning && !treatment.IsEnding
            && OSChapter04FinalHealPresentation.Current?.IsVisible == true,
            "the warning handoff starts the final-treatment backend and full-screen presentation");
        Check(os.HeartHealToolWindowController?.GetComponent<OSWindowUnit>()?.IsOpen != true,
            "the final-treatment handoff does not open the legacy treatment tool");
        Check(currentGrief.Tier == 0 && currentGrief.DarkLevel == 0 && !currentGrief.IsFailureWarningVisible
            && !currentGrief.OwnsPointer && !currentGrief.IsGlitchVisible,
            "entering treatment clears the grief overlay, modal, pointer ownership and scan line");
        Capture("grief-treatment-opened");
    }

    static IEnumerator RestoreGriefLine(int line, bool completed, int expectedDark)
    {
        string label = "grief-line-" + line + (completed ? "-completed" : "-typing");
        int visit = State.currentVisit, history = State.history.Count, cues = resourceCues, completions = textCompletions;
        int heart = data.GetOSResourceValue(ResourceType.Heart), fault = data.GetOSResourceValue(ResourceType.Fault);
        int heartWeight = State.heartWeightApplied, faultWeight = State.faultWeightApplied;
        string snapshot = Capture(label);
        yield return Restore(snapshot, label);
        yield return Wait(() => CurrentGrief(line) && director.IsAvailable
            && OSChapter04GriefPresentation.Current?.Tier == 3,
            "the saved tier-three line resumes its real text presentation: " + label);
        yield return Hold(.15f);
        Check(State.currentTextCompleted == completed && State.currentVisit == visit && State.history.Count == history
            && State.griefTier == 3 && State.griefLineStep == line - 6
            && OSChapter04GriefPresentation.Current.DarkLevel == expectedDark,
            "tier-three restore keeps the exact line, completion flag and cumulative darkness: " + label);
        Check(resourceCues == cues && textCompletions == completions && State.heartWeightApplied == heartWeight
            && State.faultWeightApplied == faultWeight && data.GetOSResourceValue(ResourceType.Heart) == heart
            && data.GetOSResourceValue(ResourceType.Fault) == fault,
            "tier-three restore does not duplicate text completion or resource charges: " + label);
    }

    static IEnumerator RestoreRetiredGriefLine()
    {
        int visit = State.currentVisit, cues = resourceCues, completions = textCompletions;
        int heart = data.GetOSResourceValue(ResourceType.Heart), fault = data.GetOSResourceValue(ResourceType.Fault);
        string history = JsonConvert.SerializeObject(State.history);
        var legacy = JsonUtility.FromJson<GameSaveData>(Capture("grief-before-legacy-migration"));
        var retired = legacy.osProcess.memory.chapter04Story;
        retired.currentEntryId = Prefix + "grief_retired";
        retired.currentVisit = visit - 1;
        retired.currentTextCompleted = retired.currentCueCommitted = true;
        retired.currentResourcesPrepared = retired.currentResourcesCommitted = true;
        retired.currentHeartTarget = 0; retired.currentFaultTarget = 100;
        retired.heartWeightApplied = retired.faultWeightApplied = 0;
        retired.currentReadElapsed = 10f;
        string json = JsonUtility.ToJson(legacy, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-grief-retired-input.json"), json);
        yield return Restore(json, "QA-only retired grief entry");
        yield return Wait(() => CurrentGrief(7) && director.IsAvailable, "the retired ID resumes at its mapped active grief line");
        Check(State.currentVisit == visit && !State.currentTextCompleted && !State.currentCueCommitted
            && !State.currentResourcesPrepared && !State.currentResourcesCommitted && State.currentReadElapsed == 0f
            && State.heartWeightApplied == 2 && State.faultWeightApplied == 1,
            "legacy migration creates one new visit and rebuilds only the weights preceding the target line");
        Check(State.theatreState == OSChapter04PresentationBridge.GraveBreakdown
            && State.griefTier == 3 && State.griefLineStep == 1 && OSChapter04GriefPresentation.Current.DarkLevel == 1
            && JsonConvert.SerializeObject(State.history) == history && resourceCues == cues && textCompletions == completions
            && data.GetOSResourceValue(ResourceType.Heart) == heart && data.GetOSResourceValue(ResourceType.Fault) == fault,
            "legacy migration restores the final picture while retaining transcript and actual resources without replay");
    }

    static void AddFixtureEntries()
    {
        var globals = (List<OSCharacterInteractionEntryData>)Get(interactionClone, "globalEntries");
        for (int i = 1; i <= 7; i++)
        {
            var line = new OSCharacterInteractionEntryData();
            Set(line, "entryId", Id(i)); Set(line, "triggerType", OSCharacterInteractionTriggerType.ExternalSignal);
            Set(line, "triggerTargetId", Signal(i)); Set(line, "requiredChapterIndex", 4);
            Set(line, "partType", i == 6 ? OSCharacterInteractionPartType.TextWithOptions : OSCharacterInteractionPartType.Text);
            Set(line, "fallbackText", "[QA only / no authored dialogue] Fixture line " + i + ". "
                + "This deliberately long diagnostic string keeps the original typewriter active while window pause and save restoration are tested. ");
            Set(line, "waitForExternalCompletion", true); Set(line, "preserveOnOwnerClose", true);
            if (i == 6)
            {
                var option = new OSCharacterInteractionOptionData();
                Set(option, "optionId", "proceed"); Set(option, "fallbackText", "[QA] Proceed");
                Set(line, "options", new List<OSCharacterInteractionOptionData> { option });
            }
            globals.Add(line);
        }
        for (int i = 1; i <= 9; i++)
        {
            var line = new OSCharacterInteractionEntryData();
            Set(line, "entryId", GriefId(i)); Set(line, "triggerType", OSCharacterInteractionTriggerType.ExternalSignal);
            Set(line, "triggerTargetId", GriefSignal(i)); Set(line, "requiredChapterIndex", 4);
            Set(line, "partType", OSCharacterInteractionPartType.Text);
            Set(line, "fallbackText", "[QA only / no authored dialogue] Grief fixture line " + i + ". "
                + "This diagnostic text remains unfinished long enough to test tier restoration and exact-once completion. ");
            Set(line, "waitForExternalCompletion", true); Set(line, "preserveOnOwnerClose", true);
            globals.Add(line);
        }
        var intruder = new OSCharacterInteractionEntryData();
        Set(intruder, "entryId", "chapter04_qa_unrelated"); Set(intruder, "triggerType", OSCharacterInteractionTriggerType.ExternalSignal);
        Set(intruder, "triggerTargetId", "chapter04.qa.intruder"); Set(intruder, "requiredChapterIndex", 4);
        Set(intruder, "fallbackText", "[QA only] Unrelated global message must not replace a reserved chapter line.");
        globals.Add(intruder);
    }

    static string FixtureJson(float openingAutoAdvanceDelay = -1f) => JsonConvert.SerializeObject(new {
        version = 1, firstEntryId = Id(1), dateLocalizationKey = string.Empty, dateFallback = "[QA] 3001-07",
        beats = Enumerable.Range(1, 7).Select(i => new {
            entryId = Id(i), signalId = Signal(i), nextEntryId = i < 6 ? Id(i + 1) : string.Empty,
            theatreStateAfterText = i == 6 ? "CityArrive" : string.Empty, autoAdvanceDelay = i == 1 ? openingAutoAdvanceDelay : -1f,
            options = i == 6 ? new[] { new { optionId = "proceed", nextEntryId = Id(7) } }
                : new[] { new { optionId = string.Empty, nextEntryId = string.Empty } }.Take(0).ToArray()
        }).ToArray()
    });

    static string GriefFixtureJson() => JsonConvert.SerializeObject(new {
        version = 1, firstEntryId = GriefId(1), heartWeightBudget = 3, faultWeightBudget = 3,
        resumeRoutes = new[] { new { oldEntryId = Prefix + "grief_retired", nextEntryId = GriefId(7),
            theatreState = OSChapter04PresentationBridge.GraveBreakdown, heartWeightBefore = 2, faultWeightBefore = 1 } },
        beats = Enumerable.Range(1, 9).Select(i => new {
            entryId = GriefId(i), signalId = GriefSignal(i), nextEntryId = i < 9 ? GriefId(i + 1) : string.Empty,
            theatreStateAfterText = i == 1 ? OSChapter04TheaterStage.FlowerWalkRain
                : i == 2 ? OSChapter04PresentationBridge.Grave : i == 3 ? OSChapter04TheaterStage.GravePlaceFlowers
                : i == 4 ? OSChapter04PresentationBridge.GraveBreakdown : string.Empty,
            griefTier = FixtureGriefTier(i), griefLineStep = i >= 6 ? i - 6 : 0,
            heartWeight = i == 4 || i == 6 || i == 7 ? 1 : 0,
            faultWeight = i == 5 || i == 8 || i == 9 ? 1 : 0,
            autoAdvanceDelay = -1f, endAction = i == 9 ? OSChapter04StoryContent.FailureWarningAction : string.Empty,
            options = new object[0]
        }).ToArray()
    });

    static void Bind()
    {
        os = OS_Handle.Instance; director = os?.Chapter04Story; entry = os?.Chapter04Entry;
        live = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        settings = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        Check(os != null && director != null && entry != null && live != null && settings != null,
            "chapter backend and existing windows resolve");
        if (!hasOriginalTypingSpeed)
        { originalTypingSpeed = (float)Get(Link, "charactersPerSecond"); hasOriginalTypingSpeed = true; }
        Set(Link, "charactersPerSecond", 4f);
        if (observedLink != Link)
        {
            if (observedLink != null) observedLink.EntryTextPlaybackCompleted -= OnTextComplete;
            observedLink = Link; observedLink.EntryTextPlaybackCompleted += OnTextComplete;
        }
        if (observedBridge != Bridge)
        {
            if (observedBridge != null) observedBridge.PresentationRequested -= OnRequest;
            observedBridge = Bridge;
            if (observedBridge != null) observedBridge.PresentationRequested += OnRequest;
        }
        if (observedDirector != director)
        {
            if (observedDirector != null)
            {
                observedDirector.ResourceCueCommitted -= OnResourceCue;
                observedDirector.FinalTreatmentRequested -= OnFinalTreatmentRequested;
            }
            observedDirector = director;
            observedDirector.ResourceCueCommitted += OnResourceCue;
            observedDirector.FinalTreatmentRequested += OnFinalTreatmentRequested;
        }
        GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false); Focus();
    }

    static void UnbindObservers()
    {
        if (observedLink != null) observedLink.EntryTextPlaybackCompleted -= OnTextComplete;
        if (observedBridge != null) observedBridge.PresentationRequested -= OnRequest;
        if (observedDirector != null)
        {
            observedDirector.ResourceCueCommitted -= OnResourceCue;
            observedDirector.FinalTreatmentRequested -= OnFinalTreatmentRequested;
        }
        observedLink = null; observedBridge = null; observedDirector = null;
    }
    static void OnTextComplete(string sequence, OSCharacterInteractionEntryData line)
    { if (line?.EntryId.StartsWith(Prefix, StringComparison.Ordinal) == true) textCompletions++; }
    static void OnResourceCue(OSChapter04StoryDirector.ResourceCue cue)
    { if (cue.EntryId?.StartsWith(Prefix, StringComparison.Ordinal) == true) resourceCues++; }
    static void OnFinalTreatmentRequested() => finalTreatmentRequests++;
    static void CaptureRuntimeError(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeErrors.Add(type + ": " + condition + "\n" + stackTrace);
    }
    static void OnRequest(OSChapter04PresentationBridge.PresentationRequest request)
    {
        requests.Add(request);
        observations.Add(new { context, request.StateId, request.IsRestore, request.Revision, frame = Time.frameCount });
    }
    static string Capture(string label)
    {
        OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-" + label + ".json"), json);
        observations.Add(new { context, label, State.currentEntryId, State.currentVisit, State.currentTextCompleted,
            State.currentCueCommitted, State.theatreState, State.griefTier, State.griefLineStep,
            finalTreatmentStage = State.finalTreatmentStage.ToString(), history = State.history.Count });
        return json;
    }
    static IEnumerator Restore(string json, string label)
    {
        ReleaseInput(); director.ResetPresentation(); entry.ResetPresentation(); Link.NotifyOSHidden();
        var save = JsonUtility.FromJson<GameSaveData>(json);
        data.ReplaceSaveData(save); yield return OSSessionPersistence.Restore(save); Bind();
        director.DebugSetContent(fixtureContent);
        Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        yield return null; yield return null;
        Check(ReferenceEquals(State, save.osProcess.memory.chapter04Story) && !OSSessionPersistence.IsRestoring,
            "real session restore completed: " + label);
    }
    static void Finish(int line, bool options)
    {
        Check(Current(line), "finishing the actual visible fixture line " + line);
        bool result = options ? Link.TryCompleteDisplayedOptionTextImmediately(Id(line)) : Link.TryCompleteDisplayedTextImmediately(Id(line));
        Check(result || State.currentTextCompleted, "production text completion succeeds for line " + line);
    }
    static void Focus()
    {
        mouse?.MakeCurrent(); keyboard?.MakeCurrent();
        foreach (var target in new MonoBehaviour[] { director, director?.FinalHeal, entry, os?.Chapter03Story, os?.Chapter03Entry,
            os?.Chapter02FinalTalk, os?.CursorRelease, os?.GetComponent<OSChapter02NightfallPresentation>() })
        {
            if (target == null || !(Get(target, "hasFocus") is bool value)) continue;
            if (!focusStates.ContainsKey(target)) focusStates.Add(target, value);
            Set(target, "hasFocus", true);
        }
        director?.DebugSetFocus(true);
    }
    static void ReleaseInput()
    {
        if (mouse != null) InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
        if (keyboard != null) InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType);
    }
    static string DisplayedText()
    {
        var label = Get(Link, Link.IsWaitingForOption ? "textWithOptionsLabel" : "textLabel") as TMPro.TMP_Text;
        return label == null ? string.Empty : label.text + "/" + label.maxVisibleCharacters;
    }
    static bool Current(int line) => State.currentEntryId == Id(line) && Link.DisplayedEntryId == Id(line) && Ready(Link.LinkWindow);
    static bool CurrentGrief(int line) => State.currentEntryId == GriefId(line) && Link.DisplayedEntryId == GriefId(line) && Ready(Link.LinkWindow);
    static int FixtureGriefTier(int line) => line < 4 ? 0 : line == 4 ? 1 : line == 5 ? 2 : 3;
    static string GriefId(int line) => Prefix + "grief_" + line.ToString("00");
    static string GriefSignal(int line) => "chapter04.story.qa.grief." + line.ToString("00");
    static string Id(int line) => Prefix + line.ToString("00");
    static string Signal(int line) => "chapter04.story.qa." + line.ToString("00");
    static IEnumerator Hold(float seconds)
    { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing && !window.IsMinimized;
    static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static string Describe() => "chapter=" + data?.CurrentChapterIndex + ", entryStep=" + entry?.Step
        + ", active=" + director?.IsActive + ", available=" + director?.IsAvailable + ", line=" + director?.State?.currentEntryId
        + ", displayed=" + os?.LinkMessageController?.DisplayedEntryId + ", TV=" + os?.TVWindow?.IsOpen
        + ", live=" + live?.IsOpen + ", settings=" + settings?.IsOpen + ", state=" + director?.State?.theatreState;
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Members)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Members)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    static string Hash(string path)
    { if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
}
