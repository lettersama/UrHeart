using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Opt-in Play Mode integration coverage. The main route uses the real Home,
// message completion, mirror resume, keypad and paper callbacks; it never calls
// OnDrainCue or edits a story queue to obtain the -21/+12 total. Reflection is
// limited to observations and the independent presentation/guardrail fixtures.
// All save writes are redirected to a fresh Temp directory before fixtures run.
public static class OSChapter03ResourceDrainQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] HeartCues = { "live.breakdown.01", "live.breakdown.02", "live.breakdown.03",
        "live.breakdown.05", "live.breakdown.06", "live.breakdown.07", "live.offline.east", "live.offline.diary13", "live.offline.bed" };
    static readonly string[] FaultCues = { "live.breakdown.02", "live.breakdown.06", "live.breakdown.07", "failed_typing" };
    static readonly string[] LineCues = HeartCues.Concat(new[] { "live.breakdown.04_yes", "live.breakdown.04_no" }).Distinct().ToArray();
    static readonly List<string> checks = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<ResourceEvent> resourceEvents = new List<ResourceEvent>();
    static readonly Dictionary<string, int> typedCounts = new Dictionary<string, int>();
    static GameDataManager data;
    static GameAutosaveService saves;
    static GameSetup setup;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSLinkMessageController boundMessages;
    static OSWindowUnit memoryWindow, liveWindow, settingsWindow;
    static Mouse testMouse;
    static string context, eventScope;
    static bool cancelled, video, videoArmed, keepVideo, knowsAnswer, isolated;
    static bool sawFirstTypingWithdrawal, sawSecondTypingWithdrawal, observedTypingTooEarly;
    static int lastSampleFrame, firstTypingStageFrame, secondTypingStageFrame;
    static Exception callbackError;

    sealed class ResourceEvent
    {
        public string scope, entry;
        public int frame, heart, fault, appearedIcons, statusLights;
        public float value;
        public ResourceType type;
    }

    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static string VideoStage { get; private set; } = "idle";
    public static bool VideoReady { get; private set; }
    public static bool VideoComplete { get; private set; }
    public static string Start() => StartRun(false, true);
    public static string StartNoAnswer() => StartRun(false, false);
    public static string StartVideo() => StartRun(true, true);
    public static string StartVideoNoAnswer() => StartRun(true, false);
    public static void BeginVideo() { if (video && VideoReady) videoArmed = true; }
    public static void FinishVideo() => keepVideo = false;
    public static void Cancel() { cancelled = true; keepVideo = false; }

    static OSChapter03StoryState S => story.State;
    static OSChapter03StoryPresentation P => story.Presentation;
    static OSHomeView V => home.View;
    static OSLinkMessageController Messages => os.LinkMessageController;
    static OSChapter02NightfallPresentation Night => os.GetComponent<OSChapter02NightfallPresentation>();
    static int Heart => data.GetOSResourceValue(ResourceType.Heart);
    static int Fault => data.GetOSResourceValue(ResourceType.Fault);
    static string Branch => knowsAnswer ? "live.breakdown.04_yes" : "live.breakdown.04_no";
    static string OtherBranch => knowsAnswer ? "live.breakdown.04_no" : "live.breakdown.04_yes";

    static string StartRun(bool forVideo, bool answer)
    {
        if (Status == "running" || Status == "video-ready" || Status == "video-complete") return Status;
        data = GameDataManager.Instance; saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || data == null || saves == null || GameSessionFlow.Instance == null
            || GameManager.Instance == null || SceneLoadManager.Instance == null || GameTest.Instance == null
            || GameResourceSystem.Instance == null) return "Initialized Play Mode required";
        if (saves.IsBusy || saves.IsWriting || GameSessionFlow.Instance.IsBusy || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for the active save or scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to fingerprint the original save path";
        setup = typeof(GameSetup).GetField("activeSetup", StaticFields)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup required";
        checks.Clear(); observations.Clear(); resourceEvents.Clear(); typedCounts.Clear();
        cancelled = false; isolated = false; callbackError = null; lastSampleFrame = -1;
        video = forVideo; knowsAnswer = answer; videoArmed = false; keepVideo = forVideo;
        VideoReady = VideoComplete = false; VideoStage = "preparing bedroom";
        sawFirstTypingWithdrawal = sawSecondTypingWithdrawal = observedTypingTooEarly = false;
        firstTypingStageFrame = secondTypingStageFrame = -1;
        context = "initialization"; eventScope = "fixture";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03ResourceDrainQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        Status = "running";
        data.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources.enabled, background = Application.runInBackground;
        bool demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        var stack = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession(); isolated = true;
            data.ReplaceSaveData(Clone(original)); Set(setup, "isDemo", false);
            Application.runInBackground = true;
            if (!video) AudioListener.volume = 0f;
            // Only the unrelated global heartbeat listener is disabled. Modify,
            // persisted resources and all drain animation Updates remain real.
            resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03ResourceDrainQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameEvents.ResourceChanged += OnResourceChanged;
            stack.Push(video ? RunVideo() : Run());
            while (stack.Count > 0)
            {
                object next = null;
                Exception failure = null;
                try
                {
                    Focus(); Sample();
                    if (cancelled) throw new OperationCanceledException("Resource drain QA cancelled");
                    if (callbackError != null) throw new InvalidOperationException("Resource/event observation failed", callbackError);
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + failure;
                    Debug.LogError("[Chapter03 Resource Drain QA] " + Status);
                    if (!video) ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "failure.png"));
                    yield break;
                }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            Check(files.Select(Hash).SequenceEqual(hashes), "original save and scene files remain byte-for-byte unchanged");
            Status = "PASS: " + checks.Count + " checks";
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameEvents.ResourceChanged -= OnResourceChanged;
            BindMessages(null);
            try
            {
                saves.EnterTestSession(); saves.EndSession();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                story?.ResetPresentation(); home?.Suspend();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                Set(setup, "isDemo", demo);
                resources.enabled = resourceEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                UnityEngine.Random.state = random;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language
                    || GameSetup.IsDemo != demo || resources.enabled != resourceEnabled
                    || AudioListener.volume != volume || Application.runInBackground != background)
                    throw new InvalidOperationException("Original in-memory environment was not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save/scene file hash changed");
                checks.Add("cleanup restored the original save object, language, Demo mode, resource listener, audio, background, random state and mouse");
            }
            catch (Exception cleanup) { Status = "FAILED during cleanup: " + cleanup + "\n" + Status; }
            if (Status == "running" || Status == "video-ready" || Status == "video-complete") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, video, knowsAnswer, checks, typedCounts, resourceEvents, observations,
                firstTypingStageFrame, secondTypingStageFrame, originalFiles = files, originalHashes = hashes,
                finalHashes = files.Select(Hash).ToArray(), note = "Exit Play Mode after this isolated run. Production drain amounts, floors, durations, sorting and story queues were not edited. StartVideo waits for BeginVideo, then uses natural text and production clicks through the return to Home; FinishVideo performs cleanup."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Resource Drain QA] " + Status + " | " + OutputDirectory);
            isolated = false; ClearReferences(); testMouse = null; setup = null;
        }
    }

    static IEnumerator Run()
    {
        yield return Fixture();
        yield return BeginMirror();
        context = "first real line, settings pause and icon appearance";
        yield return Drive(() => Typed("live.breakdown.01") == 1, "01 completes through EntryTextPlaybackCompleted", 90f);
        Check(Heart == 55 && Fault == 42 && P.IsResourceDrainPlaying && !Paid("live.breakdown.01", true),
            "typing completion starts the heart art without changing its value");
        yield return CheckSettingsFreeze();
        yield return ObserveFirstAppearance();

        context = "02 real pre-appearance disk save and Continue";
        yield return Drive(() => Typed("live.breakdown.02") == 1, "02 completes through the production text event", 90f);
        Check(Heart == 53 && Fault == 42 && Started("live.breakdown.02", true) && Started("live.breakdown.02", false)
            && !Paid("live.breakdown.02", true) && !Paid("live.breakdown.02", false),
            "02 starts both resources and has not charged before either icon appears");
        Check(ActiveDrains().All(d => !(bool)Get(d, "paid") && (float)Get(d, "clock") < .12f),
            "02 is frozen immediately, inside the real pre-appearance window");
        settingsWindow.OpenStandalone(0f);
        yield return Wait(() => P.Paused && AllDrainGroupsHidden() && AllDrainLightsCleared(),
            "settings safely freezes both pending icons and clears their lights");
        Check(Heart == 53 && Fault == 42 && ActiveDrains().All(d => !(bool)Get(d, "paid") && (float)Get(d, "clock") < .12f),
            "disk snapshot is still before both appearances");
        yield return SaveAndContinueMidFlight();

        context = "remaining real breakdown and the second failed typing withdrawal";
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Offline && Idle,
            "01-09 finish and the actual mirror Resume button returns Live to Home", 220f);
        yield return Wait(() => !P.IsResourceDrainPlaying && !P.IsScreenZoomTransitioning, "mirror resources and screen return finish");
        AssertMirrorTotal();
        yield return FlushAndCheck("returned-small-window", 39, 54);

        context = "three production offline crying cues";
        yield return RunOfflineRoute();
        yield return Wait(() => !P.IsResourceDrainPlaying, "last bedside icon and status light finish");
        Check(Heart == 34 && Fault == 54, "55/42 reaches exactly 34/54 through the real mirror and offline route");
        AssertReceipts();
        yield return FlushAndCheck("complete-mirror-and-offline", 34, 54);

        eventScope = "isolated edge cases";
        yield return CheckMinimizedFallback();
        yield return PrepareEdgeMessages();
        yield return CheckIconPresentation();
        yield return CheckBusyLanding();
        yield return CheckPresentationInterruption(false);
        yield return CheckPresentationInterruption(true);
        yield return CheckClampsAndFallbacks();
    }

    static IEnumerator RunVideo()
    {
        yield return Fixture();
        VideoReady = true; VideoStage = "ready: bedroom 55/42; call BeginVideo"; Status = "video-ready";
        while (!videoArmed) { Focus(); yield return null; }
        Status = "running"; VideoReady = false; VideoStage = "walking to bathroom";
        yield return BeginMirror();
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Offline && Idle
            && S.Has("live_back_home") && !P.IsResourceDrainPlaying && !P.IsScreenZoomTransitioning,
            "natural video route finishes 01-09 and returns to Home", 300f);
        AssertMirrorTotal();
        VideoStage = "complete: returned Home, heart 39, fault 54"; VideoComplete = true; Status = "video-complete";
        while (keepVideo) { Focus(); yield return null; }
    }

    static IEnumerator Fixture(bool resetEvidence = true)
    {
        context = "isolated bedroom entrance";
        saves.EnterTestSession(); saves.EndSession(); ClearReferences();
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03Bedroom), "real bedroom checkpoint starts");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "bedroom scene loads", 80f);
        Resolve();
        Check(GameTest.Instance.JumpSucceeded && saves.IsTestSession, "checkpoint runs only in isolated storage");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        data.SetCurrentLanguage(LanguageState.zh_CN); S.knowAnswer = knowsAnswer ? 1 : 2;
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && Ready(liveWindow)
            && V.CurrentRoom?.id == "bed" && V.InputEnabled, "bedroom and Live are ready");
        if (!video) foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
        yield return Wait(() => memory.IsCurrentTextComplete && S.memoryQueue.Count == 0 && S.liveQueue.Count == 0,
            "bedroom fixture has no pending narration");
        Check(!S.flags.Any(flag => flag.StartsWith("drain.", StringComparison.Ordinal)), "no drain receipts are pre-seeded");
        GameResourceSystem.Instance.ApplySnapshot(55, data.OSResourceData.energy, 42);
        if (resetEvidence) { typedCounts.Clear(); resourceEvents.Clear(); eventScope = "production"; }
        Check(Heart == 55 && Fault == 42 && !P.IsResourceDrainPlaying, "production arrival baseline is heart 55, fault 42");
    }

    static IEnumerator BeginMirror()
    {
        var door = V.CurrentRoom.FindDoor("bed_bath");
        Check(door != null && !door.locked, "ordinary bedroom door leads to bathroom");
        Call(V, "Click", door.tile);
        yield return Wait(() => V.CurrentRoom?.id == "bath" && !V.IsWalking && !V.IsTransitioning,
            "real Home tile click walks through the bathroom door");
        yield return Drive(() => Idle, "bathroom arrival narration completes");
        var mirror = V.CurrentRoom.FindThing("bath_mirror");
        Check(mirror != null && mirror.interactive && !mirror.hidden, "bathroom mirror is available");
        Check((bool)Call(V, "AcceptsInput"), "Home accepts the production mirror tile click");
        Call(V, "Click", mirror.tile);
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Mirror,
            "real mirror tile and note close start the authored sequence", 60f);
    }

    static IEnumerator CheckSettingsFreeze()
    {
        Check(ActiveDrains().Length > 0 && ActiveDrains().All(d => !(bool)Get(d, "paid") && (float)Get(d, "clock") < .12f),
            "settings opens before the first icon's 0.12-second appearance");
        foreach (var drain in ActiveDrains())
        {
            var root = (RectTransform)Get(drain, "root"); var canvas = root.GetComponent<Canvas>();
            var group = (CanvasGroup)Get(drain, "group");
            Check(canvas != null && canvas.overrideSorting && canvas.sortingOrder == 1201,
                "resource art retains authored sorting order 1201");
            Check(!group.blocksRaycasts && !group.interactable, "resource art never intercepts UI input");
            CheckIconSource(drain);
        }
        bool previousPause = P.Paused;
        try
        {
            P.Paused = true;
            Check(AllDrainGroupsHidden() && AllDrainLightsCleared(),
                "the Paused setter hides icons and clears their lights immediately");
        }
        finally { P.Paused = previousPause; }
        settingsWindow.OpenStandalone(0f);
        yield return Wait(() => P.Paused && AllDrainGroupsHidden() && AllDrainLightsCleared(),
            "real settings window hides and pauses the icon and its status light");
        var before = DrainGeometry(); int heart = Heart, fault = Fault;
        yield return Hold(.8f);
        Check(before == DrainGeometry() && Heart == heart && Fault == fault && !Paid("live.breakdown.01", true),
            "settings freezes icon clocks and positions without paying hidden effects");
        observations.Add(new { kind = "settings-freeze", before, after = DrainGeometry(), heart, fault });
        settingsWindow.CloseSilently();
        yield return Wait(() => story.IsAvailable && !P.Paused && ActiveDrains().Any(d => ((CanvasGroup)Get(d, "group")).alpha > 0f
            && Night.TryGetTransientLight(d, out _, out _, out _, out int level) && level == 4),
            "closing settings resumes the same icon, payment and status light");
    }

    static IEnumerator ObserveFirstAppearance()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!Paid("live.breakdown.01", true) && Time.realtimeSinceStartup < deadline)
        {
            Check(Heart == 55, "first cue leaves global heart unchanged until its icon appears");
            Focus(); yield return null;
        }
        Check(Paid("live.breakdown.01", true) && Heart == 53 && Fault == 42, "first icon appearance charges exactly two heart");
        var events = resourceEvents.Where(e => e.scope == "production" && e.type == ResourceType.Heart).ToArray();
        Check(events.Length == 1 && events[0].value == 53 && events[0].appearedIcons == 1 && events[0].statusLights == 1,
            "icon appearance, full status-slot light and ResourceChanged occur together exactly once");
        yield return Wait(() => !P.IsResourceDrainPlaying, "first-cue icon fade and status light finish");
        Check(resourceEvents.Count(e => e.scope == "production" && e.type == ResourceType.Heart) == 1 && Heart == 53,
            "the same cue's dissolve and light tail cannot charge a second time");
    }

    static IEnumerator SaveAndContinueMidFlight()
    {
        int beforeTyped = Typed("live.breakdown.02");
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        Check(saves.FlushNow(), "real autosave flush writes the in-flight snapshot");
        saves.EndSession();
        var saved = ReadSnapshot();
        Check(saved.osResources.heart == 53 && saved.osResources.error == 42
            && saved.osProcess.memory.chapter03Story.Has("drain.live.breakdown.02.heart")
            && saved.osProcess.memory.chapter03Story.Has("drain.live.breakdown.02.fault")
            && !saved.osProcess.memory.chapter03Story.Has("drain.live.breakdown.02.heart.paid")
            && !saved.osProcess.memory.chapter03Story.Has("drain.live.breakdown.02.fault.paid"),
            "disk contains started-but-unpaid heart and fault with unchanged resources");
        File.WriteAllText(Path.Combine(OutputDirectory, "mid-flight-logical-save.json"), JsonUtility.ToJson(saved, true));
        var oldStory = story;
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "real session flow returns to the menu");
        ClearReferences();
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !GameSessionFlow.Instance.IsBusy, "main menu finishes", 80f);
        Check(GameSessionFlow.Instance.ContinueGame(), "Continue loads the saved file and reconstructs the scene");
        yield return Wait(() => !GameSessionFlow.Instance.IsBusy && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null && saves.SessionActive,
            "Continue completes real scene reconstruction", 90f);
        Resolve();
        yield return Wait(() => Ready(memoryWindow) && home.View != null && S != null && P != null, "restored Memory resolves");
        Check(oldStory == null, "Continue destroys the old icon owner and rebuilds the actual OS scene");
        Check(Typed("live.breakdown.02") == beforeTyped, "Continue does not replay the saved text-completion event");
        Check(!P.IsResourceDrainPlaying, "Continue does not recreate icons for started cues");
        // A saved settings window may either restore open or be deliberately omitted
        // by session UI restoration. Both paths must quietly settle the same receipts.
        if (settingsWindow.IsOpen) settingsWindow.CloseSilently();
        yield return Wait(() => story.IsAvailable && Paid("live.breakdown.02", true) && Paid("live.breakdown.02", false),
            "restored pending receipts quietly settle once");
        Check(Heart == 51 && Fault == 44 && !P.IsResourceDrainPlaying && Typed("live.breakdown.02") == beforeTyped,
            "restoring mid-flight pays -2/+2 exactly once without animation or text replay");
        saves.EnterTestSession();
        yield return Hold(.7f);
        Check(Heart == 51 && Fault == 44, "the next settle tick cannot pay the restored cue twice");
    }

    static void AssertMirrorTotal()
    {
        Check(Heart == 39 && Fault == 54, "mirror 01-09 and the second failed typing withdrawal total -16 heart/+12 fault");
        Check(S.Has("live_back_home") && S.liveOffline && S.Has("mirror_done") && !P.IsScreenZoomed,
            "ordinary Live has returned to Home after the zoom and mirror finish");
        foreach (string key in HeartCues.Where(key => key.StartsWith("live.breakdown.", StringComparison.Ordinal)))
            Check(Typed(key) == 1 && Paid(key, true), "one production completion and one paid heart receipt: " + key);
        Check(Typed(Branch) == 1 && Typed(OtherBranch) == 0 && Paid(Branch, false) && !Started(OtherBranch, false),
            "04 yes/no are mutually exclusive in real EntryTextPlaybackCompleted events and fault receipts");
        Check(sawFirstTypingWithdrawal && sawSecondTypingWithdrawal && !observedTypingTooEarly
            && Paid("failed_typing", false), "failed_typing adds fault only on its second withdrawal (stage 3)");
    }

    static IEnumerator RunOfflineRoute()
    {
        bool walked = false;
        Check(V.WalkTo(new Vector2Int(7, 5), () => walked = true), "first offline step starts");
        yield return Drive(() => walked && Idle, "first offline step narration completes");
        Check(V.EnterRoom("east", new Vector2Int(7, 4)), "real room callback enters east corridor");
        yield return Drive(() => Idle, "east corridor password clue completes");
        walked = false;
        Check(V.WalkTo(new Vector2Int(7, 5), () => walked = true), "real east step triggers the first crying line");
        yield return Drive(() => walked && Idle && S.Has("offline_read_receipt"), "east cry and real reply option finish", 80f);
        Check(Typed("live.offline.east") == 1 && Paid("live.offline.east", true) && Heart == 38 && Fault == 54,
            "east cry takes one heart through its real text completion");
        var labDoor = V.CurrentRoom.FindDoor("lab_door");
        Check(labDoor != null && story.IsAvailable && !story.BlocksHomeInput, "laboratory door can be activated");
        Call(story, "OnDoor", labDoor);
        yield return Drive(() => Head("keypad") && P.IsModalOpen && ModalReady, "laboratory keypad opens");
        foreach (char digit in "0710") ClickButton(ModalButton("Digit" + digit));
        ClickButton(ModalButton("Submit"));
        yield return Drive(() => V.CurrentRoom?.id == "lab" && Idle, "production keypad callback opens the laboratory");
        ActivateThing("lab_bed");
        yield return Drive(() => S.Has("bed_first") && Idle, "first bedside illustration and narration finish");
        ActivateThing("lab_pillow_note");
        yield return Drive(() => S.papers.Contains("diary_13") && S.Has("dong_absorbed") && Idle,
            "real diary thirteen paper close triggers its crying line", 90f);
        Check(Typed("live.offline.diary13") == 1 && Paid("live.offline.diary13", true) && Heart == 36 && Fault == 54,
            "diary thirteen cry takes two heart exactly once");
        ActivateThing("lab_bed");
        yield return Drive(() => S.Has("bed_second_done") && Idle, "real second bedside sequence and crying line finish", 90f);
        Check(Typed("live.offline.bed") == 1 && Paid("live.offline.bed", true), "second bedside cry is completed and paid once");
    }

    static void AssertReceipts()
    {
        foreach (string cue in HeartCues) Check(Paid(cue, true), "persisted heart payment: " + cue);
        foreach (string cue in FaultCues.Concat(new[] { Branch })) Check(Paid(cue, false), "persisted fault payment: " + cue);
        Check(LineCues.Where(key => key != OtherBranch).All(key => Typed(key) == 1) && Typed(OtherBranch) == 0,
            "all 01-07 and offline cues came from one real completion apiece; the unused branch never fired");
    }

    static IEnumerator FlushAndCheck(string label, int heart, int fault)
    {
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        Check(saves.FlushNow(), label + ": actual save flush succeeds");
        saves.EndSession();
        var snapshot = ReadSnapshot();
        Check(snapshot.osResources.heart == heart && snapshot.osResources.error == fault && Heart == heart && Fault == fault,
            label + ": saved resource values match the visible runtime state");
        string[] paid = S.flags.Where(flag => flag.StartsWith("drain.", StringComparison.Ordinal) && flag.EndsWith(".paid", StringComparison.Ordinal)).OrderBy(flag => flag).ToArray();
        Check(snapshot.osProcess.memory.chapter03Story.flags.Where(flag => flag.StartsWith("drain.", StringComparison.Ordinal)
            && flag.EndsWith(".paid", StringComparison.Ordinal)).OrderBy(flag => flag).SequenceEqual(paid),
            label + ": every payment receipt is persisted with its corresponding values");
        File.WriteAllText(Path.Combine(OutputDirectory, label + "-logical-save.json"), JsonUtility.ToJson(snapshot, true));
        saves.EnterTestSession(); yield return null;
    }

    static IEnumerator PrepareEdgeMessages()
    {
        // The completed Lab route deliberately reserves an empty message slot.
        // Enter a real reading context instead of forging a queue or bypassing
        // that reservation. Keep the main route's observations and save artifacts.
        yield return Fixture(false);
        context = "independent real mirror reading context";
        yield return BeginMirror();
        yield return Drive(() => Messages.DisplayedEntryId == OSChapter03StoryDirector.LiveId("live.breakdown.02")
            && Messages.IsReadingHoldActive, "independent route naturally reaches the second line's reading hold", 100f);
        FreezeFixtureReading();
        yield return Wait(() => !P.IsResourceDrainPlaying, "independent route's own initial icons finish before boundary probes");
    }

    static void FreezeFixtureReading()
    {
        var timer = Get(Messages, "autoCloseTween") as Tween;
        Check(timer != null && timer.IsActive(), "independent fixture owns a real reading tween");
        // Keep IsReadingHoldActive true so the real director retains its slot.
        // Killing this tween would advance the queue and invalidate the source.
        timer.timeScale = 0f;
    }

    // Restore only the currently reserved, already-read message. No authored data,
    // queue or consumed history is edited, and no completion event is replayed.
    static IEnumerator VisibleMessageFixture()
    {
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => Ready(liveWindow) && story.IsAvailable, "Live opens for the display-only fixture");
        yield return Drive(() => !string.IsNullOrEmpty(story.ReservedEntryId)
            && Messages.DisplayedEntryId == story.ReservedEntryId && Messages.IsReadingHoldActive,
            "fixture uses the real director's currently reserved reading slot", 100f);
        FreezeFixtureReading();
        yield return Wait(() => !P.IsResourceDrainPlaying, "current message's production icons finish before a boundary probe");
        string id = story.ReservedEntryId;
        Check(data.OSCharacterInteractionData.TryGetGlobalEntry(id, out var entry) && entry.AutoCloseDelay > 0f,
            "fixture resolves an existing authored text entry: " + id);
        string history = JsonUtility.ToJson(data.OSProcessData.characterInteraction);
        // Close and restore in the same coroutine tick: this clears any previous
        // display-only hole without allowing the director to consume its slot.
        Messages.LinkWindow.CloseSilently();
        Check((bool)Call(Messages, "RestoreChapter03ReadingHold", id, entry.AutoCloseDelay),
            "fixture restores a real read message without a completion callback");
        FreezeFixtureReading();
        yield return Wait(() => Ready(Messages.LinkWindow) && Messages.DisplayedTextLabel.isActiveAndEnabled,
            "fixture message is actually visible");
        Messages.DisplayedTextLabel.ForceMeshUpdate();
        Check(Messages.DisplayedTextLabel.textInfo.characterCount > 0
            && JsonUtility.ToJson(data.OSProcessData.characterInteraction) == history,
            "display fixture preserves message history and supplies real TMP geometry");
    }

    static IEnumerator CheckIconPresentation()
    {
        context = "local Live icons, immediate status light and unchanged message";
        yield return VisibleMessageFixture();
        ResetCue("live.breakdown.02", 55, 42);
        var label = Messages.DisplayedTextLabel;
        string text = label.text, vertices = TextVertexAlpha(label);
        string history = JsonUtility.ToJson(data.OSProcessData.characterInteraction);
        Check(data.OSCharacterInteractionData.TryGetGlobalEntry(Messages.DisplayedEntryId, out var entry),
            "icon fixture resolves the original message data");
        string authored = JsonUtility.ToJson(entry);
        int firstEvent = resourceEvents.Count;
        Call(story, "OnDrainCue", "live.breakdown.02");
        var pair = ActiveDrains();
        Check(pair.Length == 2 && pair.Count(d => (bool)Get(d, "heart")) == 1, "visible 02 owns independent heart and fault icons");
        foreach (var drain in pair) CheckIconSource(drain);
        var heart = pair.Single(d => (bool)Get(d, "heart"));
        var fault = pair.Single(d => !(bool)Get(d, "heart"));
        Check(Mathf.Abs((float)Get(fault, "delay") - .35f) < .001f, "fault retains its authored 0.35-second delay");
        var sources = pair.ToDictionary(d => d, d => (Vector2)Get(d, "sourceLocal"));
        var levels = pair.ToDictionary(d => d, d => new HashSet<int>());
        bool sawHeartCrack = false, sawHeartFade = false, sawFaultRise = false, sawFaultFade = false;
        bool checkedLitPause = false;
        float deadline = Time.realtimeSinceStartup + 12f;
        while (P.IsResourceDrainPlaying && Time.realtimeSinceStartup < deadline)
        {
            foreach (var drain in pair.Where(d => ActiveDrains().Contains(d)))
            {
                CheckIconFrame(drain, sources[drain]);
                bool isHeart = (bool)Get(drain, "heart");
                float clock = (float)Get(drain, "clock");
                float opacity = (float)Get(drain, "opacity");
                if (isHeart)
                {
                    sawHeartCrack |= clock > .28f && DrainBits(drain).Any(bit => !(bool)Get(bit, "seam")
                        && Vector2.Distance(((RectTransform)Get(bit, "rect")).anchoredPosition, (Vector2)Get(bit, "home")) >= .5f);
                    sawHeartFade |= clock > .55f && opacity > 0f && opacity < .95f;
                }
                else
                {
                    sawFaultRise |= ((Vector2)Get(drain, "position")).y - ((Vector2)Get(drain, "origin")).y >= 2f;
                    sawFaultFade |= clock > .5f && opacity > 0f && opacity < .95f;
                }
                if (!(bool)Get(drain, "paid")) continue;
                float elapsed = clock - (float)Get(drain, "paidAt");
                bool lit = Night.TryGetTransientLight(drain, out var screen, out float core, out float band, out int level);
                int expected = elapsed < 1f ? 4 : Mathf.Max(0, 3 - Mathf.FloorToInt((elapsed - 1f) / .07f));
                Require(lit == (expected > 0) && (!lit || level == expected), "status light holds full for 1.0 seconds, then steps 4/3/2/1/0");
                levels[drain].Add(lit ? level : 0);
                if (lit)
                    Require(Mathf.Abs(core - (isHeart ? 14f : 10f)) < .001f
                        && Mathf.Abs(band - (isHeart ? 3f : 2f)) < .001f
                        && Vector2.Distance(screen, ScreenForRect((RectTransform)Get(drain, "targetIcon"))) < .1f,
                        "status light stays at the actual resource icon rectangle with authored dimensions");
            }
            Require(label.text == text && TextVertexAlpha(label) == vertices && JsonUtility.ToJson(entry) == authored
                && JsonUtility.ToJson(data.OSProcessData.characterInteraction) == history,
                "local icon effects never alter message text, its vertex alpha, authored data or history");
            if (!checkedLitPause && pair.All(d => (bool)Get(d, "paid")))
            {
                checkedLitPause = true;
                Check(Heart == 53 && Fault == 44, "both icon appearances have already paid once");
                settingsWindow.OpenStandalone(0f);
                yield return Wait(() => P.Paused && AllDrainGroupsHidden() && AllDrainLightsCleared(),
                    "settings hides both local icons and clears both active status lights");
                string frozen = DrainGeometry();
                yield return Hold(.35f);
                Check(frozen == DrainGeometry() && Heart == 53 && Fault == 44, "settings freezes paid effects and light clocks without repeating payment");
                settingsWindow.CloseSilently();
                yield return Wait(() => story.IsAvailable && !P.Paused && pair.All(d =>
                    Night.TryGetTransientLight(d, out _, out _, out _, out int level) && level == 4),
                    "closing settings restores both status lights at their preserved full level");
            }
            Focus(); yield return null;
        }
        Check(!P.IsResourceDrainPlaying && Heart == 53 && Fault == 44, "both local icons finish and pay exactly once");
        var appearances = resourceEvents.Skip(firstEvent).ToArray();
        Check(appearances.Length == 2 && appearances.All(e => e.appearedIcons == 1 && e.statusLights == 1),
            "both real resource changes occur with one visible local icon and its full status light in the same callback");
        Check(sawHeartCrack && sawHeartFade && sawFaultRise && sawFaultFade,
            "heart cracks and fades locally while fault rises and fades locally");
        foreach (var drain in pair)
        {
            Check(levels[drain].IsSupersetOf(new[] { 4, 3, 2, 1, 0 }), "status light exposes every authored fade level");
            Check(!Night.TryGetTransientLight(drain, out _, out _, out _, out _), "finished icon owns no residual transient light");
        }
        Check(label.text == text && TextVertexAlpha(label) == vertices && JsonUtility.ToJson(entry) == authored
            && JsonUtility.ToJson(data.OSProcessData.characterInteraction) == history,
            "message content and all character alpha remain unchanged after both icons disappear");
        observations.Add(new { kind = "local-icon-status-light", heartSource = Pair(sources[heart]), faultSource = Pair(sources[fault]),
            heartLevels = levels[heart].OrderBy(x => x).ToArray(), faultLevels = levels[fault].OrderBy(x => x).ToArray() });
    }

    static void CheckIconSource(object drain)
    {
        var anchor = Get(drain, "sourceAnchor") as RectTransform;
        Vector2 local = (Vector2)Get(drain, "sourceLocal");
        var target = Get(drain, "targetIcon") as RectTransform;
        var root = (RectTransform)Get(drain, "root");
        var bits = DrainBits(drain);
        Check(anchor == liveWindow.WindowRect && anchor.rect.Contains(local),
            "random icon source belongs to Live and lies inside its visible window");
        Check(target != null && root.GetComponentsInChildren<TMP_Text>(true).Length == 0 && bits.Length > 0,
            "resource effect contains a pixel icon and a real status target, with no text nodes");
        Check(bits.All(bit => ((RectTransform)Get(bit, "rect")).sizeDelta == Vector2.one * 2f
            && !((UnityEngine.UI.Image)Get(bit, "image")).raycastTarget),
            "icon pixels stay two OS pixels and never intercept input");
    }

    static void CheckIconFrame(object drain, Vector2 sourceLocal)
    {
        var anchor = (RectTransform)Get(drain, "sourceAnchor");
        var rect = (RectTransform)Get(drain, "iconRoot");
        Require((Vector2)Get(drain, "sourceLocal") == sourceLocal, "one icon retains its selected local source throughout its effect");
        if ((bool)Get(drain, "iconVisible"))
        {
            Vector2 actual = LocalForDrain(rect.TransformPoint(rect.rect.center));
            Vector2 source = LocalForDrain(anchor.TransformPoint(sourceLocal));
            Require(Vector2.Distance(actual, source) <= 10f, "icon motion remains near Live instead of crossing the screen");
        }
        Require(DrainBits(drain).All(bit => Vector2.Distance(((RectTransform)Get(bit, "rect")).anchoredPosition,
            (Vector2)Get(bit, "home")) <= 6f), "crack pixels stay within the local icon");
        float clock = (float)Get(drain, "clock");
        bool paid = (bool)Get(drain, "paid");
        if ((float)Get(drain, "delay") > 0f || clock < .12f)
            Require(!paid && !(bool)Get(drain, "iconVisible") && !Night.TryGetTransientLight(drain, out _, out _, out _, out _),
                "pending effect has no icon, light or payment before its 0.12-second appearance");
        if (!paid) return;
        Require((float)Get(drain, "paidAt") >= .12f && (float)Get(drain, "paidAt") <= .2201f,
            "payment is made on the first presentation tick at the authored appearance time");
        if (Night.TryGetTransientLight(drain, out var screen, out _, out _, out _))
            Require(Vector2.Distance(screen, ScreenForRect((RectTransform)Get(drain, "targetIcon"))) < .1f,
                "status light tracks the resource icon's actual world-space centre");
    }

    static object[] DrainBits(object drain) => ((IEnumerable)Get(drain, "bits")).Cast<object>().ToArray();

    static Vector2 LocalForDrain(Vector3 world)
    {
        var parent = (RectTransform)Get(P, "windowParent");
        return (Vector2)parent.InverseTransformPoint(world) - parent.rect.center;
    }

    static Vector2 ScreenForRect(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
    }

    static string TextVertexAlpha(TMP_Text label) => string.Join(",", Enumerable.Range(0, label.textInfo.characterCount)
        .Where(i => label.textInfo.characterInfo[i].isVisible).SelectMany(i => {
            var c = label.textInfo.characterInfo[i];
            return Enumerable.Range(0, 4).Select(v => label.textInfo.meshInfo[c.materialReferenceIndex].colors32[c.vertexIndex + v].a);
        }));

    static void Require(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); }

    static IEnumerator CheckBusyLanding()
    {
        context = "Busy guard at a icon landing callback";
        Check(!GameSessionFlow.Instance.IsBusy && !P.IsResourceDrainPlaying, "Busy edge starts in an idle isolated session");
        yield return VisibleMessageFixture();
        ResetCue("live.breakdown.01", 55, 42);
        Call(story, "OnDrainCue", "live.breakdown.01");
        var drain = ActiveDrains().FirstOrDefault(d => (bool)Get(d, "heart"));
        Check(drain != null && !Paid("live.breakdown.01", true), "Busy edge owns a real pending icon callback");
        var flow = GameSessionFlow.Instance;
        var busyField = typeof(GameSessionFlow).GetField("<IsBusy>k__BackingField", Fields);
        Check(busyField != null, "isolated Busy boundary can be restored in finally");
        bool beforeBusy = flow.IsBusy;
        try
        {
            busyField.SetValue(flow, true);
            // Reproduce the late callback race: this is the actual callback held by
            // the real icon, not a direct resource write or a timing change.
            var callback = Get(drain, "landed") as Action;
            Check(callback != null, "Busy fixture retains the production landing callback");
            Set(drain, "landed", null); Set(drain, "interrupted", null); callback();
            Check(Heart == 55 && Fault == 42 && Started("live.breakdown.01", true) && !Paid("live.breakdown.01", true),
                "landing while GameSessionFlow is Busy leaves both value and paid receipt untouched");
        }
        finally { busyField.SetValue(flow, beforeBusy); }
        yield return Wait(() => Paid("live.breakdown.01", true), "pending Busy landing settles after the session becomes available");
        Check(Heart == 53 && Fault == 42, "Busy recovery settles the pending two heart once");
        yield return Wait(() => !P.IsResourceDrainPlaying, "Busy edge icon and light finish without a second payment");
        Check(Heart == 53 && Fault == 42, "the light tail cannot duplicate a recovered Busy payment");
    }

    static IEnumerator CheckMinimizedFallback()
    {
        // The completed Lab route permits normal window dismissal. The mirror
        // cinematic deliberately blocks it, so exercise this guard before
        // entering the separate visible-message fixture.
        context = "minimized Live fallback after the real Lab route";
        ResetCue("live.breakdown.01", 55, 42);
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => Ready(liveWindow), "Live reopens for minimize fallback");
        liveWindow.Minimize();
        Check(liveWindow.IsMinimized, "real minimize API hides Live");
        Call(story, "OnDrainCue", "live.breakdown.01");
        Check(Heart == 53 && Fault == 42 && Paid("live.breakdown.01", true) && !P.IsResourceDrainPlaying,
            "minimized Live pays immediately without icons");
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => Ready(liveWindow), "Live restores after minimize fallback");
    }

    static IEnumerator CheckClampsAndFallbacks()
    {
        context = "32/68 guardrails and invisible source/target fallbacks";
        ResetCue("live.breakdown.07", 32, 68);
        liveWindow.CloseSilently();
        Call(story, "OnDrainCue", "live.breakdown.07");
        Check(Heart == 30 && Fault == 70 && Paid("live.breakdown.07", true) && Paid("live.breakdown.07", false)
            && !P.IsResourceDrainPlaying, "closed Live settles immediately and clamps 32/68 to 30/70");
        Call(story, "OnDrainCue", "live.breakdown.07");
        Check(Heart == 30 && Fault == 70, "repeating a paid cue cannot cross either guardrail");

        ResetCue("live.breakdown.02", 30, 70);
        Call(story, "OnDrainCue", "live.breakdown.02");
        Check(Heart == 30 && Fault == 70 && Paid("live.breakdown.02", true) && Paid("live.breakdown.02", false)
            && !P.IsResourceDrainPlaying, "a later cue at both limits records payment without visual or numeric drift");

        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => Ready(liveWindow), "Live restores before the visible-message fallbacks");
        yield return VisibleMessageFixture();

        ResetCue("live.breakdown.02", 55, 42);
        Messages.LinkWindow.CloseSilently();
        Check(Ready(liveWindow) && !Messages.LinkWindow.IsOpen, "hidden-message edge keeps Live open while closing only its message");
        Call(story, "OnDrainCue", "live.breakdown.02");
        Check(Heart == 55 && Fault == 42 && !Paid("live.breakdown.02", true) && !Paid("live.breakdown.02", false)
            && ActiveDrains().Length == 2, "closed message still starts both local icons on the visible Live window");
        foreach (var drain in ActiveDrains()) CheckIconSource(drain);
        yield return Wait(() => Paid("live.breakdown.02", true) && Paid("live.breakdown.02", false) && !P.IsResourceDrainPlaying,
            "message visibility is not required for local icons, status lights and their payments");
        Check(Heart == 53 && Fault == 44, "closed-message icons still pay exactly minus two heart and plus two fault");
        yield return VisibleMessageFixture();

        ResetCue("live.breakdown.02", 55, 42);
        var views = os.GetComponentsInChildren<OSResourceStateView>(true)
            .Where(view => view.ResourceType == ResourceType.Heart || view.ResourceType == ResourceType.Fault).ToArray();
        bool[] enabled = views.Select(view => view.enabled).ToArray();
        try
        {
            foreach (var view in views) view.enabled = false;
            Call(story, "OnDrainCue", "live.breakdown.02");
            Check(Heart == 53 && Fault == 44 && Paid("live.breakdown.02", true) && Paid("live.breakdown.02", false)
                && !P.IsResourceDrainPlaying, "no eligible resource target settles both resources immediately");
        }
        finally { for (int i = 0; i < views.Length; i++) if (views[i] != null) views[i].enabled = enabled[i]; }
        Check(views.Select(view => view.enabled).SequenceEqual(enabled), "missing-target test restores every resource-view component");
        yield return FlushAndCheck("fallbacks", 53, 44);
    }


    static IEnumerator CheckPresentationInterruption(bool destroyRoot)
    {
        context = destroyRoot ? "lost generated icon root" : "presentation-only reset while a cue is unpaid";
        yield return VisibleMessageFixture();
        ResetCue("live.breakdown.01", 55, 42);
        Call(story, "OnDrainCue", "live.breakdown.01");
        Check(P.IsResourceDrainPlaying && Heart == 55 && !Paid("live.breakdown.01", true), "real pending icon exists");
        var drain = ActiveDrains().Single();
        Check((float)Get(drain, "clock") < .12f && !(bool)Get(drain, "paid")
            && !Night.TryGetTransientLight(drain, out _, out _, out _, out _),
            "interruption occurs before the icon appears or its status light is registered");
        var owner = story;
        if (destroyRoot)
        {
            var root = (RectTransform)Get(drain, "root");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }
        else P.ResetPresentation(); // Intentionally do NOT reset the director.
        Check(ReferenceEquals(story, owner) && Heart == 55 && !Paid("live.breakdown.01", true),
            "presentation loss does not pay synchronously or replace/reset its director");
        yield return Wait(() => Paid("live.breakdown.01", true) && !P.IsResourceDrainPlaying,
            "the existing settle tick recovers the interrupted payment without replaying icons");
        Check(!Night.TryGetTransientLight(drain, out _, out _, out _, out _), "interruption clears its independent light owner");
        Check(Heart == 53 && Fault == 42, "interrupted cue is paid once at its original amount");
        yield return Hold(.65f);
        Check(Heart == 53 && Fault == 42 && !P.IsResourceDrainPlaying, "subsequent settle ticks do not replay or repeat the interrupted cue");
    }

    static void ResetCue(string key, int heart, int fault)
    {
        Check(!P.IsResourceDrainPlaying, "independent edge case has no earlier icon or light tail in flight");
        foreach (string suffix in new[] { ".heart", ".heart.paid", ".fault", ".fault.paid" }) S.flags.Remove("drain." + key + suffix);
        GameResourceSystem.Instance.ApplySnapshot(heart, data.OSResourceData.energy, fault);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Bind before the rebuilt scene's first Update so an erroneous completion
        // replay during Continue cannot hide before the main wait resolves.
        var next = OS_Handle.Instance;
        if (isolated && next != null && next.gameObject.scene == scene) BindMessages(next.LinkMessageController);
    }

    static void Resolve()
    {
        os = OS_Handle.Instance; memory = os?.MemoryWindowController;
        home = memory != null ? memory.GetComponent<OSChapter03HomeController>() : null;
        story = os?.Chapter03Story;
        memoryWindow = memory?.GetComponent<OSWindowUnit>();
        liveWindow = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        settingsWindow = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        Check(os != null && memory != null && home != null && story != null && memoryWindow != null
            && liveWindow != null && settingsWindow != null, "production Chapter 3 components resolve");
        BindMessages(Messages);
    }

    static void ClearReferences()
    {
        BindMessages(null);
        os = null; memory = null; home = null; story = null;
        memoryWindow = liveWindow = settingsWindow = null;
    }

    static void BindMessages(OSLinkMessageController next)
    {
        if (boundMessages == next) return;
        if (boundMessages != null) boundMessages.EntryTextPlaybackCompleted -= OnTyped;
        boundMessages = next;
        if (boundMessages != null) boundMessages.EntryTextPlaybackCompleted += OnTyped;
    }

    static void OnTyped(string sequence, OSCharacterInteractionEntryData entry)
    {
        try
        {
            string key = LineCues.FirstOrDefault(cue => entry != null && entry.EntryId == OSChapter03StoryDirector.LiveId(cue));
            if (key == null || eventScope != "production") return;
            typedCounts[key] = Typed(key) + 1;
            observations.Add(new { kind = "EntryTextPlaybackCompleted", key, count = typedCounts[key], frame = Time.frameCount,
                heart = Heart, fault = Fault, scene = OS_Handle.Instance != null ? OS_Handle.Instance.gameObject.scene.name : "none" });
        }
        catch (Exception error) { callbackError = error; }
    }

    static void OnResourceChanged(ResourceType type, float value, float max)
    {
        if (!isolated || type == ResourceType.Battery) return;
        try
        {
            var appeared = ActiveDrains().Where(d => (bool)Get(d, "heart") == (type == ResourceType.Heart)
                && (bool)Get(d, "paid") && (bool)Get(d, "iconVisible")
                && Mathf.Abs((float)Get(d, "clock") - (float)Get(d, "paidAt")) < .001f).ToArray();
            int lights = appeared.Count(d => Night != null
                && Night.TryGetTransientLight(d, out var screen, out _, out _, out int level) && level == 4
                && Vector2.Distance(screen, ScreenForRect((RectTransform)Get(d, "targetIcon"))) < .1f);
            resourceEvents.Add(new ResourceEvent { scope = eventScope, type = type, value = value,
                frame = Time.frameCount, heart = Heart, fault = Fault, appearedIcons = appeared.Length, statusLights = lights,
                entry = boundMessages != null ? boundMessages.DisplayedEntryId : "" });
        }
        catch (Exception error) { callbackError = error; }
    }

    static void Sample()
    {
        if (lastSampleFrame == Time.frameCount || story == null || S == null) return;
        lastSampleFrame = Time.frameCount;
        if (video && videoArmed && !VideoComplete) VideoStage = !string.IsNullOrEmpty(Messages.DisplayedEntryId)
            ? Messages.DisplayedEntryId : story.CurrentPhase + (S.memoryQueue.Count > 0 ? ": " + S.memoryQueue[0].key : "");
        if (eventScope != "production" || !Head("effect", "failed_typing")
            || !(bool)Get(story, "runtimeMemoryStarted")
            || !ReferenceEquals(Get(story, "runtimeMemoryBeat"), S.memoryQueue[0])) return;
        int stage = (int)Get(story, "effectStage"); float clock = (float)Get(story, "effectClock");
        if (stage >= 1 && stage < 3)
        {
            if (!sawFirstTypingWithdrawal) firstTypingStageFrame = Time.frameCount;
            sawFirstTypingWithdrawal = true;
            observedTypingTooEarly |= Started("failed_typing", false) || Fault != 51;
        }
        if (stage >= 3)
        {
            if (!sawSecondTypingWithdrawal)
            {
                secondTypingStageFrame = Time.frameCount;
                observations.Add(new { kind = "failed-typing-second-withdrawal", stage, clock, frame = Time.frameCount,
                    started = Started("failed_typing", false), paid = Paid("failed_typing", false), heart = Heart, fault = Fault });
            }
            sawSecondTypingWithdrawal = Started("failed_typing", false);
        }
    }

    static bool Idle => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !Messages.IsWaitingForOption
        && !Messages.IsReadingHoldActive && !P.IsModalOpen && !P.IsAnimating && !V.IsWalking && !V.IsTransitioning;
    static bool Head(string type, string key = null) => S.memoryQueue.Count > 0 && S.memoryQueue[0].type == type
        && (key == null || S.memoryQueue[0].key == key);
    static bool ModalReady => P != null && (bool)typeof(OSChapter03StoryPresentation).GetProperty("CanInteract", Fields).GetValue(P);
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing
        && !window.IsMinimized && window.gameObject.activeInHierarchy;
    static bool Started(string key, bool heart) => S.Has("drain." + key + (heart ? ".heart" : ".fault"));
    static bool Paid(string key, bool heart) => S.Has("drain." + key + (heart ? ".heart.paid" : ".fault.paid"));
    static int Typed(string key) => typedCounts.TryGetValue(key, out int value) ? value : 0;
    static object[] ActiveDrains() => story != null && P != null && Get(P, "drains") is IEnumerable list
        ? list.Cast<object>().ToArray() : Array.Empty<object>();
    static bool AllDrainGroupsHidden() => ActiveDrains().Length > 0
        && ActiveDrains().All(drain => ((CanvasGroup)Get(drain, "group")).alpha == 0f);
    static bool AllDrainLightsCleared() => Night != null
        && ActiveDrains().All(drain => !Night.TryGetTransientLight(drain, out _, out _, out _, out _));

    static string DrainGeometry() => JsonConvert.SerializeObject(ActiveDrains().Select(drain => new {
        clock = (float)Get(drain, "clock"), delay = (float)Get(drain, "delay"),
        alpha = ((CanvasGroup)Get(drain, "group")).alpha,
        position = Pair((Vector2)Get(drain, "position")), sourceLocal = Pair((Vector2)Get(drain, "sourceLocal")),
        paid = (bool)Get(drain, "paid"), lightLevel = (int)Get(drain, "lightLevel"),
        lightVisible = (bool)Get(drain, "lightVisible"), iconVisible = (bool)Get(drain, "iconVisible"),
        opacity = (float)Get(drain, "opacity")
    }).ToArray());

    static void Pump()
    {
        Focus();
        if (story == null || !story.IsAvailable) return;
        // Test runs may use the player's ordinary complete-text command outside
        // the breakdown. Video runs never skip typing or reading holds.
        if (!video)
        {
            foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
            string id = Messages.DisplayedEntryId;
            if (OSChapter03StoryDirector.IsOwnEntry(id)
                && !id.StartsWith(OSChapter03StoryDirector.LiveId("live.breakdown."), StringComparison.Ordinal))
            {
                Messages.TryCompleteDisplayedTextImmediately(id);
                Messages.TryCompleteDisplayedOptionTextImmediately(id);
            }
        }
        if (Messages.IsWaitingForOption) ClickFirstOption();
        if (story.IsWaitingForMirrorResume && P.MirrorResumeButton != null && P.MirrorResumeButton.IsInteractable())
            ClickButton(P.MirrorResumeButton);
        if (ModalReady && (Head("paper") || Head("art") && !P.IsArtAutoClosing)) ClickButton(ModalButton("Close"));
    }

    static void ClickFirstOption()
    {
        var units = Get(Messages, "optionUnits") as OSLinkMessageSelectUnit[];
        var unit = units?.FirstOrDefault(candidate => candidate != null && candidate.gameObject.activeInHierarchy && candidate.IsInteractable);
        if (unit == null || EventSystem.current == null) return;
        var rect = unit.ScriptTarget;
        var canvas = unit.GetComponentInParent<Canvas>()?.rootCanvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        unit.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)),
            pointerId = -1, clickCount = 1, eligibleForClick = true });
    }

    static void ClickButton(Button button)
    {
        Check(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null,
            "production button is clickable: " + (button != null ? button.name : "missing"));
        var rect = button.transform as RectTransform;
        var canvas = button.GetComponentInParent<Canvas>()?.rootCanvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        button.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)),
            pointerId = -1, clickCount = 1, eligibleForClick = true });
    }

    static Button ModalButton(string name)
    {
        var frame = Get(P, "modalFrame") as RectTransform;
        var button = frame?.GetComponentsInChildren<Button>(true).FirstOrDefault(candidate => candidate.name == name);
        if (button == null) throw new InvalidOperationException("Missing modal button: " + name);
        return button;
    }

    static void ActivateThing(string id)
    {
        Check(story.IsAvailable && !story.BlocksHomeInput, "ordinary Home interaction is available: " + id);
        var thing = V.CurrentRoom.FindThing(id);
        Check(thing != null && !thing.hidden, "visible Home target exists: " + id);
        Call(story, "OnThing", thing);
    }

    static void Focus()
    {
        testMouse?.MakeCurrent();
        if (os == null || story == null) return;
        story.DebugSetFocus(true); os.Chapter03Entry?.DebugSetFocus(true);
        SetIfPresent(os.Chapter02FinalTalk?.Nightfall, "hasFocus", true);
        SetIfPresent(os.CursorRelease, "hasFocus", true);
        // The only bypass is the Editor being in the background. Keep the actual
        // reading tween, settings/minimize/load pauses and its original rate.
        if (Application.isFocused || Application.isBatchMode || !isolated || !story.IsAvailable || P == null || P.Paused
            || !os.IsOSVisible || Messages == null || !Messages.isActiveAndEnabled
            || !OSChapter03StoryDirector.IsOwnEntry(Messages.DisplayedEntryId)
            || !(bool)Get(Messages, "isOSVisible") || (bool)Get(Messages, "chapter03TextPaused")
            || (bool)Get(Messages, "collectionReadingPaused") || (bool)Call(Messages, "IsSuspendedByMinimize")
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance.IsBusy || GameTestConsole.BlocksGameplayInput
            || (bool)Call(Messages, "IsOptionCountdownSettingsOpen")) return;
        var owner = Get(Messages, "ownerCharacterWindow") as OSWindowUnit;
        if (!Ready(Messages.LinkWindow) || owner != null && !Ready(owner)) return;
        var timer = Get(Messages, "autoCloseTween") as Tween;
        if (timer != null && timer.IsActive()) timer.Play();
    }

    static IEnumerator Drive(Func<bool> done, string label, float seconds = 70f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < deadline) { Pump(); yield return null; }
        Check(done(), label + " | " + Describe());
    }

    static IEnumerator Wait(Func<bool> done, string label, float seconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < deadline) { Focus(); yield return null; }
        Check(done(), label + " | " + Describe());
    }

    static IEnumerator Hold(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline) { Focus(); yield return null; }
    }

    static GameSaveData ReadSnapshot()
    {
        var result = new GameAutosaveStorage(OutputDirectory).Read();
        if (!result.Success) throw new IOException("Read isolated save: " + result.Error);
        return JsonUtility.FromJson<GameSaveData>(result.Payload);
    }
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static string Describe() => story == null || S == null ? "scene changing" : "phase=" + story.CurrentPhase
        + ", room=" + V?.CurrentRoom?.id + ", heart=" + Heart + ", fault=" + Fault
        + ", memory=" + (S.memoryQueue.Count > 0 ? S.memoryQueue[0].type + ":" + S.memoryQueue[0].key : "empty")
        + ", live=" + Messages.DisplayedEntryId + ", icons=" + ActiveDrains().Length;
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static float[] Pair(Vector2 value) => new[] { value.x, value.y };
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    static void SetIfPresent(object target, string name, object value) => target?.GetType().GetField(name, Fields)?.SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
