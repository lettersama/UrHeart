using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in integration runner. It never imports assets and all save writes stay in Temp.
// The fixture changes only the entrance save. Choices, papers, doors, walks, and
// letter gestures use their production callbacks; story flags are never forced.
public static class OSChapter03StoryQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const float ExpectedMirrorZoom = 1.075f;
    static readonly List<string> checks = new List<string>();
    static readonly List<int> shownNodes = new List<int>();
    static readonly List<string> shownLive = new List<string>();
    static readonly List<string> chosenOptions = new List<string>();
    static readonly List<string> issues = new List<string>();
    static readonly List<object> zoomObservations = new List<object>();
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSLinkMessageController evidenceMessages;
    static OSChapter03HomeController home;
    static OSWindowUnit memoryWindow;
    static OSChapter03StoryDirector story;
    static GameSetup setup;
    static EditorWindow gameView;
    static Mouse testMouse;
    static Camera mirrorCamera;
    static Matrix4x4 defaultProjection;
    static float normalLogicalPixelScreenWidth, normalCursorPixelWidth;
    static string context, route;
    static bool coffeeRoute;
    static bool localizationOnly, artOnly, storyDisabledForVisuals, previewOnly, keepPreview;
    static int walkedSteps, routeSelection;
    static OSChapter03StoryState S => story.State;
    static OSChapter03StoryPresentation P => story.Presentation;
    static OSHomeView V => home.View;
    static OSLinkMessageController Messages => os.LinkMessageController;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start() => StartRun(false);
    public static string StartCoffeeRoute() => StartRun(false, singleRoute: 1);
    public static string StartNoCoffeeRoute() => StartRun(false, singleRoute: 2);
    public static string StartLocalizationVisuals() => StartRun(true);
    public static string StartArtVisuals() => StartRun(false, artVisuals: true);
    public static string StartPreview() => StartRun(false, true);
    public static void FinishPreview() => keepPreview = false;

    static string StartRun(bool visuals, bool preview = false, bool artVisuals = false, int singleRoute = 0)
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null
            || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to hash the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        gameView = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().FullName == "UnityEditor.GameView");
        if (gameView == null) return "An existing Game view is required for runtime screenshots";
        checks.Clear(); issues.Clear(); zoomObservations.Clear(); context = "initialization"; route = "setup";
        routeSelection = singleRoute;
        localizationOnly = visuals; storyDisabledForVisuals = false;
        artOnly = artVisuals;
        previewOnly = keepPreview = preview;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03StoryQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground;
        float originalVolume = AudioListener.volume;
        bool demo = (bool)Get(setup, "isDemo");
        var previousMouse = Mouse.current;
        string[] paths = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();
            hashes = paths.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Application.runInBackground = true; AudioListener.volume = 0f; Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03StoryQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            testMouse.MakeCurrent();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + error;
                    Debug.LogError("[Chapter03 Story QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "original save files unchanged");
            Status = issues.Count == 0 ? "PASS: " + checks.Count + " checks"
                : "FAILED: " + issues.Count + " recorded issues; " + checks.Count + " passing checks";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                UnbindEvidence();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                story?.ResetPresentation(); home?.Suspend();
                InvokeOptional(os?.Chapter03Entry, "ResetPresentation");
                os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                if (storyDisabledForVisuals && story != null) story.enabled = true;
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = originalVolume;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || AudioListener.volume != originalVolume || Application.runInBackground != background)
                    throw new InvalidOperationException("Original save object/language/demo mode was not restored");
                if (hashes != null && !paths.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save files changed");
                checks.Add("cleanup restored original save object, language, mode, listener, background, resources, mouse, and save/scene hashes");
            }
            catch (Exception cleanup) { Status = "FAILED during cleanup: " + cleanup + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                status = Status, localizationOnly, artOnly, context, checks, issues, zoomObservations,
                originalFiles = paths, originalHashes = hashes,
                finalHashes = paths.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Muted background Play Mode, no Editor focus. Exit Play Mode after this isolated run. Memory fixture delays outside Mirror are shortened; all Mirror effects, delays, breakdown typing and Live reading holds retain authored timing. Route A also retains complete reasoning. Zoom diagnostics report compatibility failures without changing production layout. Face diagnostics use the actual portrait bounds and authored nose point; there is no source-defined face rectangle."
            }, Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks) + "\n" + string.Join("\n", issues));
            Debug.Log("[Chapter03 Story QA] " + Status + " | " + OutputDirectory);
            os = null; memory = null; home = null; memoryWindow = null; story = null; setup = null; testMouse = null;
        }
    }

    static IEnumerator Run()
    {
        if (previewOnly)
        {
            route = "preview"; yield return Fixture();
            while (keepPreview) { Focus(); yield return null; }
            yield break;
        }
        if (localizationOnly) { yield return RunLocalizationVisuals(); yield break; }
        if (artOnly) { yield return RunArtVisuals(); yield break; }
        if (routeSelection != 2) yield return RunRouteSafely(true);
        if (routeSelection != 1) yield return RunRouteSafely(false);
    }

    // A route failure must preserve its evidence and still allow the independent second route.
    static IEnumerator RunRouteSafely(bool withCoffee)
    {
        var work = new Stack<IEnumerator>(); work.Push(RunRoute(withCoffee));
        Exception failure = null;
        try
        {
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
        }
        finally { while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose(); }
        if (failure == null) yield break;
        issues.Add(route + ": FAILED at " + context + " | " + Describe() + " | " + failure);
        Debug.LogWarning("[Chapter03 Story QA] " + issues[issues.Count - 1]);
        if (story != null && S != null) WriteEvidence();
        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, route + "-failure.png"));
        yield return null; yield return new WaitForSecondsRealtime(.2f);
    }

    static IEnumerator RunRoute(bool withCoffee)
    {
        coffeeRoute = withCoffee; route = withCoffee ? "yes-coffee" : "no-coffee";
        shownNodes.Clear(); shownLive.Clear(); chosenOptions.Clear(); walkedSteps = 0;
        yield return Fixture();

        context = route + ": opening note";
        yield return Drive(() => Head("paper", "note_01") && P.IsModalOpen, "opening reaches note 1", opening: true, autoPaper: false);
        Check(!S.homeOpened && !V.InputEnabled, "Home remains locked while note 1 is open");
        Check(!S.papers.Contains("note_01"), "opening note is not collected before close");
        P.HideModal(true);
        yield return Drain("closing note 1 reveals Home");
        yield return Wait(() => S.Has("home_revealed") && V.InputEnabled, "Home finishes its light reveal");
        Check(S.homeOpened && S.papers.Contains("note_01") && shownNodes.Contains(8090), "note close enables Home and queues 8090");
        Check(!S.items.Contains("letters"), "instruction note does not enter the letter inventory");

        context = route + ": first floor and real walking";
        bool arrived = false;
        int restored8110Shows = 0;
        Check(V.WalkTo(new Vector2Int(9, 7), () => arrived = true), "real multi-tile Home walk starts");
        if (withCoffee)
        {
            string heartbeat = OSChapter03StoryDirector.LiveId("live.opening.heartbeat");
            yield return Drive(() => arrived && !V.IsWalking && Messages.DisplayedEntryId == heartbeat
                && Messages.IsReadingHoldActive, "first-floor Live reaches its real reading hold");
            Check(shownNodes.Count(n => n == 8100) == 1 && shownNodes.Count(n => n == 8110) == 1,
                "first floor queues exactly one 8100 and one 8110");
            Check(memory.CurrentData?.DataIndex == 8110, "reading-hold save point has current Memory node 8110");
            int shownBeforeRestore = shownNodes.Count;
            yield return ReadingHoldRoundtrip(heartbeat);
            Check(shownNodes.Skip(shownBeforeRestore).SequenceEqual(new[] { 8110 }),
                "reading-hold restore re-presents only the saved current node 8110 exactly once");
            restored8110Shows = 1;
        }
        yield return Drive(() => arrived && !V.IsWalking && Idle, "real Home route and first-floor reading finish");
        Check(walkedSteps >= 4 && V.PlayerTile == new Vector2Int(9, 7), "WalkTo traverses the whole four-tile route");
        Check(shownNodes.Count(n => n == 8100) == 1 && shownNodes.Count(n => n == 8110) - restored8110Shows == 1,
            "first floor queues exactly one 8100 and one 8110");
        Call(story, "OnFloorClicked", new Vector2Int(8, 7));
        yield return Drain("repeated floor click settles");
        Check(shownNodes.Count(n => n == 8100) == 1 && shownNodes.Count(n => n == 8110) - restored8110Shows == 1,
            "repeated floor click does not replay introduction");

        context = route + ": out-of-order kitchen evidence";
        yield return Arrive("west", new Vector2Int(6, 4));
        yield return Door("bed_door");
        Check(!S.Has("final_knock") && !S.Has("bed_open"), "bedroom stays locked before evidence");
        yield return Arrive("kitchen", new Vector2Int(7, 4));
        yield return Thing("kitchen_drawer"); yield return Thing("kitchen_screwdriver");
        Check(!S.items.Contains("screwdriver") && !S.Has("screwdriver_taken"), "screwdriver cannot be taken before seeing the locked storage door");
        yield return Thing("kitchen_calendar");
        Check(S.Has("calendar_seen") && !S.Has("morning_seen"), "calendar alone cannot satisfy morning evidence");
        yield return Roundtrip("exploration");
        yield return Thing("kitchen_coffee_machine");
        Check(S.Has("machine_seen") && S.Has("morning_seen"), "calendar AND machine produce morning evidence");
        if (withCoffee)
        {
            yield return Thing("kitchen_cup");
            Check(!S.items.Contains("coffee") && !S.Has("coffee_taken"), "empty cup cannot produce coffee");
            yield return Thing("kitchen_sink"); yield return Thing("kitchen_coffee_jar");
            yield return Thing("kitchen_coffee_machine"); yield return Thing("kitchen_cup");
            Check(S.Has("coffee_taken") && S.items.Contains("coffee") && S.coffeeStep == 5,
                "water, beans, brew, and cup create coffee through production interactions");
        }
        yield return Arrive("east", new Vector2Int(7, 4));
        yield return Door("store_door");
        Check(S.Has("store_door_seen") && !S.Has("store_open"), "storage door records the prerequisite");
        yield return Arrive("kitchen", new Vector2Int(7, 4));
        yield return Thing("kitchen_screwdriver");
        Check(S.items.Contains("screwdriver"), "returning after seeing the door permits screwdriver pickup");
        yield return Arrive("east", new Vector2Int(7, 4)); yield return Door("store_door");
        Check(S.Has("store_open") && !S.items.Contains("screwdriver"), "storage door consumes screwdriver and unlocks");
        yield return Arrive("store", new Vector2Int(7, 4)); yield return Thing("store_makeup");
        Check(S.knowAnswer == (withCoffee ? 1 : 2) && S.Has("knows_reply_done"), "diary 8 choice and its matching reply complete");
        yield return Thing("store_key");
        Check(!S.items.Contains("key") && !S.Has("key_taken"), "key remains unavailable before all evidence");
        yield return Arrive("hall", new Vector2Int(7, 5)); yield return Thing("hall_shoes");
        yield return Arrive("kitchen", new Vector2Int(7, 4)); yield return Thing("kitchen_sugar");
        Check(S.Has("deduction_done") && S.clock == 2, "shoes, calendar-and-machine, and medicine finish deduction");
        yield return Arrive("store", new Vector2Int(7, 4)); yield return Thing("store_key");
        Check(!S.items.Contains("key"), "complete evidence still cannot grant key before yard return");

        context = route + ": final knock, yard, and saved automatic return";
        yield return Arrive("west", new Vector2Int(6, 4));
        ActivateDoor("bed_door");
        if (withCoffee)
        {
            yield return Drive(() => Head("effect", "reasoning") && V.IsPlayingRules, "full reasoning effect begins");
            var reasoningShow = Get(V, "rules");
            var reasoningBeat = S.memoryQueue[0];
            var settings = os.transform.Find("Windows/WindowUnit_Option").GetComponent<OSWindowUnit>();
            settings.OpenStandalone(0f); yield return null; yield return null;
            // The Home rules own this clock; the old presentation clock stays
            // unchanged even while the new effect runs and cannot prove pause.
            float pausedClock = (float)Get(reasoningShow, "clock");
            float pauseUntil = Time.realtimeSinceStartup + .4f;
            while (Time.realtimeSinceStartup < pauseUntil) { Focus(); yield return null; }
            Check(!story.IsAvailable && P.Paused && V.MotionPaused && V.IsPlayingRules
                && ReferenceEquals(Get(V, "rules"), reasoningShow)
                && Head("effect", "reasoning") && ReferenceEquals(S.memoryQueue[0], reasoningBeat) && !reasoningBeat.complete
                && Mathf.Approximately((float)Get(reasoningShow, "clock"), pausedClock),
                "settings pauses the reasoning animation without advancing its queue");
            settings.CloseSilently();
            yield return Wait(() => story.IsAvailable && !P.Paused && !V.MotionPaused && V.IsPlayingRules
                && ReferenceEquals(Get(V, "rules"), reasoningShow)
                && (float)Get(reasoningShow, "clock") > pausedClock + .05f,
                "reasoning resumes after settings closes");
            yield return Screenshot("01-reasoning-full-duration");
        }
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Yard && Idle, "final knock reaches the yard");
        Check(V.CurrentRoom.id == "yard" && S.yardVisited.Count == 0
            && !S.Has("yard_zoom") && !S.Has("yard_zoom_done") && !V.Zoomed,
            "yard opens at full size with six unseen objects");
        string[] yard = { "yard_gate", "yard_tree", "yard_clothes", "yard_seedlings", "yard_fence", "yard_bedwindow" };
        for (int i = 0; i < yard.Length - 1; i++) yield return Thing(yard[i]);
        Check(S.yardVisited.Count == 5 && story.CurrentPhase == OSChapter03StoryDirector.Phase.Yard
            && !S.Has("yard_zoom") && !V.Zoomed, "five yard objects do not start the zoom or return");
        V.Teleport(new Vector2Int(2, 1));
        ActivateThing(yard[5]);
        Check(S.memoryQueue.Take(2).Select(b => b.type + ":" + b.key).SequenceEqual(new[] {
            "effect:zoom_in", "text:interaction.yard.window" }),
            "sixth object queues zoom-in before its inspection text");
        int resolveIndex = S.memoryQueue.FindIndex(b => b.type == "effect" && b.key == "resolve");
        Check(resolveIndex >= 0 && S.memoryQueue.Skip(resolveIndex).Select(b => b.type + ":" + b.key).SequenceEqual(new[] {
            "effect:resolve", "effect:zoom_out", "flag:yard_returned", "phase:", "text:memory.yard.return.key" })
            && S.memoryQueue[resolveIndex + 3].number == (int)OSChapter03StoryDirector.Phase.KeyReady,
            "yard return queues thoughts, zoom-out, returned flag, KeyReady, and key prompt in order");
        Check(S.memoryQueue.All(b => b.type != "live") && S.liveQueue.Count == 0,
            "yard return does not enqueue a scolding Live message");
        yield return Drive(() => Head("walk") && S.memoryQueue[0].value == "7,4" && V.IsScriptWalking,
            "sixth object starts real queued automatic walking");
        Check(S.yardVisited.Count == 6 && story.CurrentPhase == OSChapter03StoryDirector.Phase.YardReturn
            && S.Has("yard_zoom") && !S.Has("yard_zoom_done") && V.Zoomed,
            "six unique yard objects trigger the automatic return while zoomed in");
        yield return Roundtrip("yard-mid-walk");
        Check(S.Has("yard_zoom") && !S.Has("yard_zoom_done") && V.Zoomed,
            "mid-walk save restores the unfinished yard zoom");
        yield return Drive(() => Head("effect", "resolve") && P.IsAnimating && Get(P, "thoughtRoot") != null,
            "restored return reaches the thoughts inside the hall", 70f);
        var thoughtRoot = Get(P, "thoughtRoot") as RectTransform;
        Check(V.CurrentRoom.id == "hall" && V.Zoomed && !S.Has("yard_zoom_done") && !S.Has("yard_returned")
            && story.CurrentPhase == OSChapter03StoryDirector.Phase.YardReturn,
            "hall thoughts finish before zoom-out and the returned flag");
        Check(thoughtRoot != null && thoughtRoot.parent == V.transform && thoughtRoot.name == "Chapter03ThoughtStrip"
            && thoughtRoot.anchoredPosition == Vector2.zero && Mathf.Approximately(thoughtRoot.sizeDelta.y, 26f),
            "return thoughts use the centered 26-pixel strip on the Home map");
        yield return Drive(() => S.Has("yard_returned") && story.CurrentPhase == OSChapter03StoryDirector.Phase.KeyReady
            && !V.Zoomed && Idle, "restored walk, footprints, hall thoughts, and zoom-out finish", 70f);
        Check(S.Has("yard_zoom") && S.Has("yard_zoom_done") && Get(P, "thoughtRoot") == null
            && !shownLive.Contains(OSChapter03StoryDirector.LiveId("live.yard.scolding")),
            "completed return retains both zoom flags, removes the strip, and never displays scolding Live");
        Check(V.CurrentRoom.id == "hall" && S.yardFootprints.Count >= 5, "automatic return retains traversed yard footprints");
        yield return Screenshot("02-yard-return-complete");
        yield return Arrive("store", new Vector2Int(7, 4)); yield return Thing("store_key");
        Check(S.items.Contains("key") && S.Has("key_taken"), "key becomes available only after completed yard return");
        yield return Arrive("west", new Vector2Int(6, 4)); yield return Door("bed_door");
        Check(S.Has("bed_open") && !S.items.Contains("key"), "bedroom unlock consumes key");
        yield return Arrive("bed", new Vector2Int(7, 4)); yield return Thing("bed_letter");

        context = route + ": mirror choice and offline restore";
        yield return Arrive("bath", new Vector2Int(7, 4));
        // Use the lowest current production arrival, rather than GameTest's 80/10.
        // All following deductions must come from real message/effect callbacks.
        GameResourceSystem.Instance.ApplySnapshot(55, GameDataManager.Instance.OSResourceData.energy, 42);
        Check(!S.flags.Any(flag => flag.StartsWith("drain.", StringComparison.Ordinal)),
            "resource-drain total starts before every mirror/offline cue at heart 55, fault 42");
        mirrorCamera = memoryWindow.GetComponentInParent<Canvas>().rootCanvas.worldCamera;
        Check(mirrorCamera != null && !P.IsScreenZoomed, "unzoomed mirror entrance has the actual OS camera");
        defaultProjection = mirrorCamera.projectionMatrix;
        ObserveScreenZoom("before mirror", false, captureBaseline: true);
        ActivateThing("bath_mirror");
        if (withCoffee)
        {
            yield return Drive(() => Head("text", "memory.9020") && S.memoryQueue[0].begun && P.MirrorRoot != null,
                "mirror appearance begins at 9020");
            yield return Roundtrip("mirror-9020-begun");
            yield return Wait(() => P.MirrorRoot != null, "9020 restore reconstructs the mirror");
            CheckMirrorFullyRevealed("restoring begun 9020 does not replay the eyes-to-face entrance");
            yield return Drive(() => Head("text", "memory.9040") && S.memoryQueue[0].begun,
                "first mirror dialogue begins at 9040");
            yield return Roundtrip("mirror-9040-dialogue");
            yield return Wait(() => P.MirrorRoot != null, "9040 restore reconstructs the mirror");
            CheckMirrorAttention("restoring 9040 retains only the red mirror attention caption");
        }
        yield return CaptureMirrorLine("01", "03a-mirror-line01-full");
        yield return CaptureMirrorLine("07", "03b-mirror-line07-full");
        string mirrorChoice = OSChapter03StoryDirector.LiveId("live.breakdown.08");
        yield return Drive(() => Messages.DisplayedEntryId == mirrorChoice && Messages.IsWaitingForOption,
            "mirror reaches its real Live option with natural full text", 120f, holdChoice: mirrorChoice);
        yield return SettleScreenZoom();
        ObserveScreenZoom("08 before restore", true, true);
        yield return Screenshot("03c-mirror-option-full");
        Check(P.MirrorRoot != null && S.Has("mirror_visible") && !S.liveOffline,
            "mirror remains visible through Live breakdown before offline");
        Check(shownLive.Contains(OSChapter03StoryDirector.LiveId(withCoffee ? "live.breakdown.04_yes" : "live.breakdown.04_no")),
            "mirror uses the earlier yes/no branch");
        Check(!S.Has("mother_flash"), "mother line no longer triggers the removed white flash");
        yield return CheckZoomSettings();
        yield return Roundtrip("mirror-live-option", clickMirrorBeforeRestore: true);
        yield return Drive(() => Messages.DisplayedEntryId == mirrorChoice && Messages.IsWaitingForOption,
            "mirror option survives JSON session restore", holdChoice: mirrorChoice);
        CheckMirrorFullyRevealed("restoring the Live option retains the complete mirror face");
        CheckMirrorAttention("restoring the Live option retains only the red mirror attention caption");
        yield return SettleScreenZoom();
        ObserveScreenZoom("08 after restore", true, true);
        yield return Screenshot("03d-mirror-option-restored");
        Check(TryClickMirrorOption("08 after restore"), "restored mirror option accepts its real screen-coordinate raycast and pointer click");
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Offline && Idle,
            "mirror finishes 9048, 9060, 9080 and goes offline", 80f);
        Check(S.liveOffline && S.Has("mirror_done") && S.Has("mirror_playback_resumed") && P.MirrorRoot == null,
            "mirror resumes through its real button, closes after final heartbeat, and Live stays offline");
        yield return SettleScreenZoom(); ObserveScreenZoom("offline", false);
        Check(new[] { 9010, 9015, 9020, 9030, 9040, 9045, 9048, 9060, 9080 }.All(shownNodes.Contains),
            "mirror traverses every authored numbered memory node");
        string initialOfflineOffer = OSChapter03StoryDirector.LiveId("live.offline.initial_offer");
        Check(!S.Has("offline_read_receipt") && !shownLive.Contains(initialOfflineOffer)
            && !GameDataManager.Instance.OSCharacterInteractionState.HasConsumedGlobalEntry(initialOfflineOffer),
            "mirror completion does not present or consume the player reply before the first offline cry");
        yield return Roundtrip("offline");
        yield return SettleScreenZoom(); ObserveScreenZoom("offline after restore", false);
        Check(S.liveOffline && !S.Has("offline_read_receipt") && !shownLive.Contains(initialOfflineOffer)
            && !S.memoryQueue.Concat(S.liveQueue).Any(beat => beat.type == "live" && beat.key == "live.offline.initial_offer"),
            "offline restoration keeps the reply deferred until the east crying message");

        context = route + ": password and lab";
        bool leftMirror = false;
        Check(V.WalkTo(new Vector2Int(7, 5), () => leftMirror = true), "walking away from mirror begins");
        yield return Drive(() => leftMirror && Idle, "offline first step completes");
        Check(shownNodes.Contains(9200), "first offline walk triggers 9200");
        yield return Arrive("east", new Vector2Int(7, 4));
        Check(S.Has("password_ready") && shownNodes.Contains(9210), "offline right corridor enables password clue");
        Check(!S.Has("offline_east_leak") && !shownLive.Contains(initialOfflineOffer),
            "entering east alone does not present the first cry or its following reply");
        bool eastStepComplete = false;
        Check(V.WalkTo(new Vector2Int(7, 5), () => eastStepComplete = true), "real east step begins the first offline cry");
        string eastCry = OSChapter03StoryDirector.LiveId("live.offline.east");
        yield return Drive(() => eastStepComplete && Messages.DisplayedEntryId == eastCry && Messages.IsReadingHoldActive,
            "the first east crying message displays before the player reply", holdChoice: initialOfflineOffer);
        Check(shownLive.Contains(eastCry) && !shownLive.Contains(initialOfflineOffer) && !S.Has("offline_read_receipt"),
            "crying text finishes before the initial offer has appeared or recorded a reply");
        yield return Drive(() => Messages.DisplayedEntryId == initialOfflineOffer && Messages.IsWaitingForOption,
            "the initial offer follows the completed crying message", holdChoice: initialOfflineOffer);
        Check(!S.liveQueue.Any(beat => beat.key == "live.offline.east"), "cry reading is complete before the reply becomes selectable");
        ChooseCurrent(); yield return Drain("the post-cry player reply completes");
        Check(S.Has("offline_read_receipt") && GameDataManager.Instance.OSCharacterInteractionState.HasConsumedGlobalEntry(initialOfflineOffer),
            "the east reply records its read receipt and its own consumed identity");
        yield return Roundtrip("offline-after-east-offer");
        Check(S.liveOffline && S.Has("offline_read_receipt")
            && GameDataManager.Instance.OSCharacterInteractionState.HasConsumedGlobalEntry(initialOfflineOffer),
            "offline presence and the post-cry read receipt survive restore");
        ActivateDoor("lab_door");
        yield return Drive(() => Head("keypad") && P.IsModalOpen, "real keypad opens");
        yield return Wait(() => ModalReady, "keypad accepts input after entrance guard");
        Keypad("0000");
        Check(!S.Has("lab_open") && P.IsModalOpen, "wrong four-digit password keeps lab locked");
        Keypad("0710");
        yield return Drain("0710 opens lab and completes offline messages");
        Check(S.Has("lab_open") && V.CurrentRoom.id == "lab" && shownNodes.Contains(9300), "0710 unlocks lab and enters through its production queue");
        yield return Thing("lab_mask");
        Check(shownNodes.Contains(OSChapter03StoryContent.Instance.Node("interaction.lab.cast")) && !P.IsModalOpen,
            "cast interaction retains its narration without an illustration");
        if (withCoffee)
        {
            // Cover both the former title-only save and the later stable-id save.
            S.memoryQueue.Add(new OSChapter03StoryBeat("art", "ui.art.cast"));
            S.memoryQueue.Add(new OSChapter03StoryBeat("art", "os.chapter03.ui.art.cast", "plaster-face"));
            yield return Roundtrip("retired-cast-art");
            yield return Drive(() => S.memoryQueue.Count == 0 && !P.IsModalOpen,
                "older cast illustration beats resume automatically without a resource or dismissal", autoPaper: false);
        }
        ActivateThing("lab_bed");
        yield return Drain("first bedside narration and lamp response complete");
        Check(!P.IsModalOpen && S.Has("lab_lamp_on"),
            "first bedside visit keeps its narration and bench light; the hand illustration waits for the second visit");
        Check(S.Has("bed_first") && (!withCoffee || S.Has("coffee_placed") && !S.items.Contains("coffee")),
            "first bedside interaction preserves the coffee route");
        yield return Thing("lab_pillow_note");
        Check(S.papers.Contains("diary_13") && S.Has("dong_absorbed") && V.CaptureState().dongHidden,
            "diary 13 collection completes heartbeat and absorbs Dong");
        ActivateThing("lab_bed");
        yield return Drive(() => Head("art") && P.IsArtAutoClosing, "second bedside illustration owns the existing timed pause", autoPaper: false);
        Check(P.ArtId == "bed-hand" && !ModalButton("Close").gameObject.activeSelf,
            "second bedside illustration preserves automatic continuation without an extra click");
        float artRemaining = S.memoryQueue[0].remaining;
        yield return Roundtrip("bedside-timed-art");
        yield return Drive(() => Head("art") && P.IsArtAutoClosing, "timed bedside art restores", autoPaper: false);
        Check(S.memoryQueue[0].remaining <= artRemaining + .05f, "timed art restore retains remaining duration");
        yield return Drain("timed bedside illustration closes and heartbeat continues");
        Check(S.Has("bed_second_done") && !V.FindThing("lab_box").hidden, "second bedside sequence reveals the box");
        int expectedCall = OSChapter03StoryContent.Instance.Node(withCoffee
            ? "memory.lab.bed.second.call" : "memory.lab.bed.second.call_without_coffee");
        Check(shownNodes.Contains(expectedCall), "second bedside call matches coffee or no-coffee history");
        yield return Wait(() => !P.IsResourceDrainPlaying, "last offline resource pixels finish landing");
        Check(GameDataManager.Instance.OSResourceData.heart == 34 && GameDataManager.Instance.OSResourceData.error == 54,
            "real full mirror and offline route changes heart 55 to 34 and fault 42 to 54");
        string takenDrainBranch = withCoffee ? "live.breakdown.04_yes" : "live.breakdown.04_no";
        string skippedDrainBranch = withCoffee ? "live.breakdown.04_no" : "live.breakdown.04_yes";
        Check(S.Has("drain." + takenDrainBranch + ".fault.paid")
            && !S.Has("drain." + skippedDrainBranch + ".fault"),
            "only the chosen yes/no breakdown branch is charged");
        Check(new[] { "live.breakdown.01", "live.breakdown.02", "live.breakdown.03", "live.breakdown.05",
                "live.breakdown.06", "live.breakdown.07", "live.offline.east", "live.offline.diary13", "live.offline.bed" }
            .All(key => S.Has("drain." + key + ".heart.paid"))
            && new[] { "live.breakdown.02", takenDrainBranch, "live.breakdown.06", "live.breakdown.07", "failed_typing" }
            .All(key => S.Has("drain." + key + ".fault.paid")),
            "every authored resource cue has exactly-once persisted payment after the real route");

        context = route + ": reunion and final letter";
        yield return Thing("lab_box");
        Check(S.Has("live_returned") && S.Has("box_open") && !S.liveOffline, "reunion reconnects Live and opens the letter box");
        Check(new[] { "pause_after_04", "understand", "pause_after_07", "its_ok" }.All(suffix => chosenOptions.Any(o => o.EndsWith(suffix, StringComparison.Ordinal))),
            "all four reunion options use actual selection callbacks");
        ActivateThing("lab_letter");
        yield return Drive(() => Head("letter") && P.IsModalOpen && P.LetterEnvelope != null, "final letter envelope opens");
        yield return Wait(() => ModalReady && P.IsLetterEnvelopeReady, "letter envelope entrance completes");
        Check(!P.IsLetterUnfolded && !S.Has("letter_collected"), "letter waits for the player's envelope click");
        yield return ClickEnvelope();
        yield return Drive(() => P.IsLetterUnfolded, "player click and red partner press open the envelope");
        Check(S.Has("letter_unfolded") && P.LetterVisibleLineCount > 0, "unfolded letter exposes paginated readable text");
        if (P.LetterPageCount > 1)
        {
            ModalButton("Next").onClick.Invoke(); yield return null;
            Check(P.LetterPage == 1, "real Next button changes the letter page");
        }
        yield return null;
        int savedPage = P.LetterPage;
        yield return Roundtrip("unfolded-letter");
        yield return Drive(() => P.IsLetterUnfolded && P.LetterPage == savedPage, "unfolded letter restores its reading page");
        yield return Screenshot("04-letter-unfolded-restored");
        yield return Wait(() => ModalReady, "restored letter accepts page input");
        int pages = P.LetterPageCount;
        for (int i = P.LetterPage; i < pages - 1; i++) { ModalButton("Next").onClick.Invoke(); yield return null; }
        Check(P.LetterPage == pages - 1, "letter reaches its last page through page buttons");
        var closingLetterBeat = S.memoryQueue.FirstOrDefault(beat => beat.type == "letter");
        ModalButton("LetterClose").onClick.Invoke();
        Check(S.Has("letter_collected") && !S.Has("letter_collecting") && !P.IsModalOpen && !P.IsAnimating
            && closingLetterBeat?.complete == true && !os.CursorRelease.ScriptActorIsMoving
            && !Equals(Get(os.CursorRelease, "scriptActorPressed"), true),
            "LetterClose immediately hides the paper, completes its beat and releases the reading pointer");
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Complete && Idle,
            "the unchanged final story delay completes Chapter 3 after closing the letter", 30f);
        Check(S.Has("letter_collected") && !P.IsModalOpen && Mathf.Approximately(S.Progress, 1f),
            "collected letter closes modal and reports full chapter progress");
        Check(GameDataManager.Instance.CurrentChapterIndex == 3, "completion does not enter an unwritten chapter 4");
        Check(withCoffee == S.Has("coffee_taken"), "route retains its intended coffee choice through the ending");
        yield return Screenshot("05-complete");
        WriteEvidence(); UnbindEvidence();
    }

    static IEnumerator Fixture()
    {
        context = route + ": isolated fresh-entry fixture";
        UnbindEvidence(); story?.ResetPresentation(); home?.Suspend();
        // The checkpoint unloads the previous OS scene. Do not let focus/timeout
        // diagnostics touch Unity objects from the route that just completed.
        os = null; memory = null; home = null; memoryWindow = null; story = null;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03DemoEnd), "demo-end checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "fixture uses isolated save storage");
        os = OS_Handle.Instance; memory = os.MemoryWindowController;
        memoryWindow = memory != null ? memory.GetComponent<OSWindowUnit>() : null;
        home = memory != null ? memory.GetComponent<OSChapter03HomeController>() : null;
        story = os.Chapter03Story;
        Check(memory != null && memoryWindow != null && home?.View != null && story != null, "production story components resolve");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        var fixture = Clone(GameDataManager.Instance.SaveData);
        fixture.flow.language = LanguageState.zh_CN;
        fixture.osProcess.currentChapterIndex = 3;
        fixture.osProcess.memory.currentDataIndex = 7460;
        fixture.osProcess.memory.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Complete;
        fixture.osProcess.memory.chapter03EntryStep = (int)OSChapter03EntryDirector.EntryStep.AwaitMemory;
        fixture.osProcess.memory.chapter03EntryInstallAnswer = 1;
        fixture.osProcess.memory.chapter03HomeInitialized = false;
        fixture.osProcess.memory.chapter03HomeView = default;
        fixture.osProcess.memory.chapter03Story = new OSChapter03StoryState();
        fixture.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        fixture.osResume.captured = false;
        foreach (string id in new[] { "memory-repair-1", "memory-repair-2", "memory-repair-3", "memory-repair-4" })
            if (!fixture.osProcess.installedToolProgramIds.Contains(id)) fixture.osProcess.installedToolProgramIds.Add(id);
        InvokeOptional(os.Chapter03Entry, "ResetPresentation"); os.Chapter02FinalTalk.ResetPresentation();
        story.ResetPresentation(); Messages.NotifyOSHidden();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true)) if (window.IsOpen) window.CloseSilently();
        memory.PrepareRuntime(); GameDataManager.Instance.ReplaceSaveData(fixture);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        Focus(); os.Chapter03Entry.DebugStep(); yield return null;
        memory.DataShown += OnData; V.Stepped += OnStep;
        evidenceMessages = Messages;
        evidenceMessages.EntryPresentationStarted += OnLiveShown;
        Check(os.OpenMemoryWindow(), "real OpenMemoryWindow starts Chapter 3");
        yield return Wait(() => story.IsAvailable && memoryWindow.IsOpen && !memoryWindow.IsOpening,
            "Chapter 3 Memory becomes available before note 1 opens Home");
        Check(GameDataManager.Instance.CurrentOSMemoryDataIndex == 8000, "fresh opening begins at 8000");
    }

    static bool Idle => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !Messages.IsWaitingForOption
        && P?.IsModalOpen != true && P?.IsAnimating != true && !V.IsPlayingRules && !V.IsWalking && !V.IsTransitioning;
    static bool Head(string type, string key = null) => S.memoryQueue.Count > 0
        && S.memoryQueue[0].type == type && (key == null || S.memoryQueue[0].key == key);
    static bool ModalReady => P != null && (bool)typeof(OSChapter03StoryPresentation).GetProperty("CanInteract", Fields).GetValue(P);

    static IEnumerator Drive(Func<bool> done, string label, float seconds = 60f,
        bool opening = false, bool autoPaper = true, string holdChoice = null)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < until)
        { Pump(opening, autoPaper, holdChoice); yield return null; }
        Check(done(), label + " | " + Describe());
    }

    static IEnumerator Drain(string label)
    {
        // Give a just-enqueued callback one Update before testing for idle.
        Pump(); yield return null;
        yield return Drive(() => Idle, label);
    }

    static void Pump(bool opening = false, bool autoPaper = true, string holdChoice = null)
    {
        Focus();
        foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
        foreach (var beat in S.memoryQueue)
        {
            if (story.CurrentPhase != OSChapter03StoryDirector.Phase.Mirror && (beat.type == "text" || beat.type == "delay"))
                beat.remaining = Mathf.Min(beat.remaining, .04f);
            // The whole Mirror sequence retains its natural motion, focus transition and failed-typing pauses.
            if (beat.type == "effect" && story.CurrentPhase != OSChapter03StoryDirector.Phase.Mirror
                && !(coffeeRoute && beat.key == "reasoning"))
                beat.remaining = Mathf.Min(beat.remaining, .18f);
        }
        string id = Messages.DisplayedEntryId;
        if (OSChapter03StoryDirector.IsOwnEntry(id))
        {
            RecordShownLive(id); // Also observes restored messages that intentionally emit no start event.
            // Breakdown screenshots must reflect real typing (including its voiced/silent characters).
            if (!id.StartsWith(OSChapter03StoryDirector.LiveId("live.breakdown."), StringComparison.Ordinal))
            {
                Messages.TryCompleteDisplayedTextImmediately(id);
                Messages.TryCompleteDisplayedOptionTextImmediately(id);
            }
            if (Messages.IsWaitingForOption && id != holdChoice) ChooseCurrent();
        }
        ResumeMirrorIfReady();
        if (autoPaper && (Head("paper") || Head("art") && !P.IsArtAutoClosing) && P.IsModalOpen) P.HideModal(true);
        if (opening && !S.homeOpened && S.memoryQueue.Count == 0 && !P.IsModalOpen
            && memory.CurrentData != null && memory.CurrentData.DataIndex < 8080 && memory.IsCurrentTextComplete)
        {
            memoryWindow.BringToFront(); os.NotifyTaskbarProcessPressed(memoryWindow);
            EventSystem.current?.SetSelectedGameObject(null); memory.TryAdvance();
        }
    }

    static void ResumeMirrorIfReady()
    {
        if (!story.IsAvailable || !story.IsWaitingForMirrorResume || P.Paused) return;
        var button = P.MirrorResumeButton;
        if (button == null || !button.IsActive() || !button.IsInteractable()) return;
        Check(EventSystem.current != null, "mirror resume has an active EventSystem");
        // Use the actual Button pointer handler; the director owns the receipt
        // and queue advancement, just as it does for a player's click.
        button.OnPointerClick(new PointerEventData(EventSystem.current) {
            button = PointerEventData.InputButton.Left,
            position = ScreenBounds(button.transform as RectTransform).center,
            pointerId = -1, clickCount = 1, eligibleForClick = true
        });
        Check(S.Has("mirror_playback_resumed") && !story.IsWaitingForMirrorResume
            && !P.IsMirrorPlaybackPaused && !P.IsMirrorStageDimmed,
            "actual mirror Resume Button releases the wait gate and background dim");
    }

    static void ChooseCurrent()
    {
        var entry = Get(Messages, "activeEntry") as OSCharacterInteractionEntryData;
        if (entry == null || entry.Options == null || entry.Options.Count == 0) return;
        var option = entry.Options[0];
        if (entry.EntryId == OSChapter03StoryDirector.LiveId("live.store.knows_question"))
            option = entry.Options.First(o => o.OptionId.EndsWith(coffeeRoute ? "knows_yes" : "knows_no", StringComparison.Ordinal));
        if (Messages.TrySelectDisplayedOptionImmediately(Messages.DisplayedSequenceId, entry.EntryId, option.OptionId))
            chosenOptions.Add(option.OptionId);
    }

    static IEnumerator Arrive(string room, Vector2Int tile)
    {
        Check(story.IsAvailable && !story.BlocksHomeInput, "Home permits arrival in " + room);
        Check(V.EnterRoom(room, tile), "real room transition callback enters " + room);
        yield return Drain("room narration settles in " + room);
    }

    static IEnumerator Thing(string id)
    { ActivateThing(id); yield return Drain("interaction settles: " + id); }
    static void ActivateThing(string id)
    {
        Check(story.IsAvailable && !story.BlocksHomeInput, "thing input is available: " + id);
        var thing = V.CurrentRoom.FindThing(id);
        Check(thing != null && !thing.hidden, "visible thing exists in current room: " + id);
        Call(story, "OnThing", thing);
    }
    static IEnumerator Door(string id)
    { ActivateDoor(id); yield return Drain("door response settles: " + id); }
    static void ActivateDoor(string id)
    {
        Check(story.IsAvailable && !story.BlocksHomeInput, "door input is available: " + id);
        var door = V.CurrentRoom.FindDoor(id);
        Check(door != null, "door exists in current room: " + id);
        Call(story, "OnDoor", door);
    }

    static IEnumerator CaptureMirrorLine(string number, string screenshot)
    {
        string id = OSChapter03StoryDirector.LiveId("live.breakdown." + number);
        yield return Drive(() => Messages.DisplayedEntryId == id && Messages.IsReadingHoldActive
            && Messages.DisplayedTextLabel.maxVisibleCharacters == int.MaxValue,
            "breakdown " + number + " naturally finishes typing", 120f);
        yield return SettleScreenZoom();
        Compatibility(Messages.DisplayedEntryId == id, "requested complete line remains visible for screenshot " + number);
        // Line 01 completes before the close-up starts; line 07 and the later
        // choice must still retain the authored enlarged projection and pixels.
        ObserveScreenZoom("line " + number + " full text", number != "01", true);
        yield return Screenshot(screenshot);
    }

    static IEnumerator SettleScreenZoom()
    {
        Canvas.ForceUpdateCanvases(); yield return null; yield return null;
        if (mirrorCamera == null) yield break;
        float until = Time.realtimeSinceStartup + 1.25f;
        int stable = 0; Matrix4x4 previous = mirrorCamera.projectionMatrix;
        while (stable < 3 && Time.realtimeSinceStartup < until)
        {
            Focus(); yield return null;
            var next = mirrorCamera.projectionMatrix;
            stable = MatrixMatches(previous, next) ? stable + 1 : 0;
            previous = next;
        }
    }

    static IEnumerator CheckZoomSettings()
    {
        var settings = os.transform.Find("Windows/WindowUnit_Option").GetComponent<OSWindowUnit>();
        settings.OpenStandalone(0f);
        yield return Wait(() => P.Paused, "settings pauses the screen zoom");
        yield return SettleScreenZoom();
        ObserveScreenZoom("settings open", false, paused: true);
        yield return Screenshot("03c-settings-at-normal-size");
        settings.CloseSilently();
        yield return Wait(() => story.IsAvailable && !P.Paused, "closing settings resumes the mirror");
        yield return SettleScreenZoom();
        ObserveScreenZoom("settings closed", true, true);
    }

    static void ObserveScreenZoom(string label, bool zoomed, bool messageVisible = false, bool paused = false, bool captureBaseline = false)
    {
        if (!Compatibility(mirrorCamera != null, label + ": actual OS camera exists")) return;
        var canvas = memoryWindow.GetComponentInParent<Canvas>().rootCanvas;
        var matrix = mirrorCamera.projectionMatrix;
        var message = Messages.LinkWindow != null ? Messages.LinkWindow.WindowRect : null;
        var portrait = os.CharacterWindowController.Chapter03Portrait;
        Rect messageBounds = ScreenBounds(message), portraitBounds = ScreenBounds(portrait != null ? portrait.rectTransform : null);
        bool hasNose = os.CharacterWindowController.TryGetChapter03FaceWorld(out var noseWorld);
        Vector3 nose = hasNose ? mirrorCamera.WorldToScreenPoint(noseWorld) : default;
        var cameraData = mirrorCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var renderer = cameraData != null ? cameraData.scriptableRenderer : null;
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        var rendererData = Get(pipeline, "m_RendererDataList") as UnityEngine.Rendering.Universal.ScriptableRendererData[];
        var rootRect = canvas.transform as RectTransform;
        float logicalPixelScreenWidth = ScreenUnitWidth(rootRect, mirrorCamera);
        var cursorCanvas = CursorHandle.Instance != null ? CursorHandle.Instance.GetComponent<Canvas>() : null;
        var cursorIcon = Get(CursorHandle.Instance, "cursorIcon") as Image;
        var cursorRect = cursorIcon != null ? cursorIcon.rectTransform : null;
        var cursorCanvasRect = cursorCanvas != null ? cursorCanvas.transform as RectTransform : null;
        var cursorCamera = cursorCanvas != null && cursorCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? cursorCanvas.worldCamera : null;
        Rect cursorBounds = ScreenBounds(cursorRect, cursorCamera);
        if (captureBaseline)
        {
            normalLogicalPixelScreenWidth = logicalPixelScreenWidth;
            normalCursorPixelWidth = cursorBounds.width;
        }
        float expectedPixelMultiplier = zoomed ? ExpectedMirrorZoom : 1f;
        zoomObservations.Add(new {
            route, label, frame = Time.frameCount, entry = Messages.DisplayedEntryId,
            isScreenZoomed = P.IsScreenZoomed, transitioning = P.IsScreenZoomTransitioning, paused = P.Paused,
            projection = MatrixValues(matrix), defaultProjection = MatrixValues(defaultProjection),
            messageBounds = RectValues(messageBounds), portraitBounds = RectValues(portraitBounds),
            authoredNose = hasNose ? new[] { nose.x, nose.y, nose.z } : null,
            screen = new[] { Screen.width, Screen.height }, viewport = RectValues(mirrorCamera.pixelRect),
            canvas = canvas.name, canvasScale = canvas.scaleFactor, canvasRect = RectValues(rootRect.rect),
            canvasPlaneDistance = canvas.planeDistance, canvasWorldScale = VectorValues(rootRect.lossyScale),
            canvasWorldUnit = rootRect.TransformVector(Vector3.right).magnitude,
            logicalPixelScreenWidth, normalLogicalPixelScreenWidth, expectedPixelMultiplier,
            effectivePixelMultiplier = normalLogicalPixelScreenWidth > 0f ? logicalPixelScreenWidth / normalLogicalPixelScreenWidth : 0f,
            cursorIconBounds = RectValues(cursorBounds), cursorIconLocalRect = cursorRect != null ? RectValues(cursorRect.rect) : null,
            cursorIconLocalScale = cursorRect != null ? VectorValues(cursorRect.localScale) : null,
            cursorPixelWidth = cursorBounds.width, normalCursorPixelWidth,
            effectiveCursorMultiplier = normalCursorPixelWidth > 0f ? cursorBounds.width / normalCursorPixelWidth : 0f,
            cursorCanvas = cursorCanvas != null ? new {
                cursorCanvas.name, mode = cursorCanvas.renderMode.ToString(), cursorCanvas.planeDistance,
                cursorCanvas.scaleFactor, worldScale = VectorValues(cursorCanvasRect.lossyScale),
                worldUnit = cursorCanvasRect.TransformVector(Vector3.right).magnitude,
                screenUnit = ScreenUnitWidth(cursorCanvasRect, cursorCamera),
                camera = cursorCamera != null ? cursorCamera.name : null
            } : null,
            camera = mirrorCamera.name, cameraId = mirrorCamera.GetInstanceID(),
            cameraPixels = new[] { mirrorCamera.pixelWidth, mirrorCamera.pixelHeight },
            targetTexture = mirrorCamera.targetTexture != null ? mirrorCamera.targetTexture.name : null,
            renderer = renderer?.GetType().FullName, pipeline = pipeline != null ? pipeline.name : null,
            renderPostProcessing = cameraData != null && cameraData.renderPostProcessing,
            rendererAssets = rendererData?.Where(d => d != null).Select(d => new {
                d.name, features = d.rendererFeatures.Where(f => f != null).Select(f => new { f.name, type = f.GetType().FullName, f.isActive }).ToArray()
            }).ToArray(),
            note = "Portrait rectangle and authored nose are measured; no separate face rectangle is defined by production art. Screenshots decide head cropping, pixel clarity and VolFx appearance. Unfinished Ch03 Animator states are not tested."
        });
        Compatibility(paused ? P.Paused : P.IsScreenZoomed == zoomed, label + ": screen zoom target/pause matches the phase");
        Compatibility(zoomed ? !MatrixMatches(matrix, defaultProjection) : MatrixMatches(matrix, defaultProjection),
            label + ": OS projection is " + (zoomed ? "changed" : "restored to its entrance default"));
        // A custom projection alone can be cancelled by ScreenSpaceCamera Canvas geometry.
        // Validate the final projected UI pixels; the ordinary cursor keeps its entrance size.
        Compatibility(normalLogicalPixelScreenWidth > 0f
            && PixelWidthMatches(logicalPixelScreenWidth, normalLogicalPixelScreenWidth * expectedPixelMultiplier),
            label + ": actual Canvas pixel size is " + expectedPixelMultiplier + "x its normal-size baseline");
        Compatibility(cursorCanvas != null && cursorRect != null && normalCursorPixelWidth > 0f
            && PixelWidthMatches(cursorBounds.width, normalCursorPixelWidth),
            label + ": actual cursor width retains its normal-size baseline");
        if (zoomed)
        {
            Compatibility(Mathf.Abs(matrix.m00 - defaultProjection.m00 * ExpectedMirrorZoom) < .0001f
                && Mathf.Abs(matrix.m11 - defaultProjection.m11 * ExpectedMirrorZoom) < .0001f,
                label + ": OS projection uses exactly 1.075x in both axes");
            var group = os.CharacterWindowController.GetComponent<CanvasGroup>();
            Compatibility(portrait != null && portrait.gameObject.activeInHierarchy && portrait.color.a > 0f
                && (group == null || group.alpha > .99f), label + ": original Live portrait remains visible");
        }
        if (messageVisible)
        {
            Compatibility(message != null && message.gameObject.activeInHierarchy && Contains(mirrorCamera.pixelRect, messageBounds),
                label + ": complete message window fits the rendered camera viewport");
            Compatibility(hasNose && mirrorCamera.pixelRect.Contains(new Vector2(nose.x, nose.y)),
                label + ": authored Live nose stays inside the rendered viewport");
        }
    }

    static bool TryClickMirrorOption(string label)
    {
        var entry = Get(Messages, "activeEntry") as OSCharacterInteractionEntryData;
        if (!Compatibility(entry?.Options != null && entry.Options.Count > 0 && Messages.IsWaitingForOption,
            label + ": a real selectable option is displayed")) return false;
        string optionId = entry.Options[0].OptionId;
        if (!Compatibility(Messages.TryGetActiveOptionTarget(optionId, out var target) && target != null
            && EventSystem.current != null, label + ": actual option target and EventSystem exist")) return false;
        var unit = target.GetComponentInParent<OSLinkMessageSelectUnit>();
        Vector2 point = ScreenBounds(target).center;
        var pointer = new PointerEventData(EventSystem.current) {
            position = point, pressPosition = point, pointerId = -1, button = PointerEventData.InputButton.Left,
            clickCount = 1, eligibleForClick = true
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        var first = hits.Count > 0 ? hits[0] : default;
        zoomObservations.Add(new { route, label, eventType = "screen-coordinate option raycast",
            optionId, point = new[] { point.x, point.y }, optionBounds = RectValues(ScreenBounds(target)),
            cursorScriptControlled = CursorHandle.Instance?.IsScriptControlled,
            hits = hits.Take(12).Select(hit => new { target = hit.gameObject != null ? hit.gameObject.name : null,
                module = hit.module?.GetType().FullName, eventCamera = hit.module?.eventCamera?.name,
                eventCameraId = hit.module?.eventCamera != null ? hit.module.eventCamera.GetInstanceID() : 0,
                hit.sortingOrder, hit.depth, hit.distance }).ToArray() });
        bool hitOption = first.gameObject != null && first.module is GraphicRaycaster
            && first.gameObject.GetComponentInParent<OSLinkMessageSelectUnit>() == unit;
        if (!Compatibility(unit != null && unit.IsInteractable && mirrorCamera.pixelRect.Contains(point)
            && hitOption && CursorHandle.Instance?.IsScriptControlled != true,
            label + ": topmost real GraphicRaycaster hit is the visible option at its rendered screen coordinate")) return false;
        Compatibility(first.module.eventCamera == mirrorCamera, label + ": option GraphicRaycaster uses the actual OS camera");
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = first;
        // Dispatch only to the topmost object found by the real EventSystem raycast; never call
        // TrySelectDisplayedOptionImmediately or the option's Clicked delegate for this check.
        ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        var releasedHits = new List<RaycastResult>();
        Canvas.ForceUpdateCanvases(); EventSystem.current.RaycastAll(pointer, releasedHits);
        if (!Compatibility(releasedHits.Count > 0 && releasedHits[0].gameObject != null
            && releasedHits[0].gameObject.GetComponentInParent<OSLinkMessageSelectUnit>() == unit,
            label + ": the same visible option remains under the screen coordinate after pointer down/up")) return false;
        var receiver = ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        bool accepted = receiver != null && (!Messages.IsWaitingForOption || Messages.DisplayedEntryId != entry.EntryId);
        Compatibility(accepted, label + ": real raycast-dispatched pointer click selects the option");
        if (accepted) chosenOptions.Add(optionId);
        return accepted;
    }

    static bool Compatibility(bool condition, string label)
    {
        if (condition) Check(true, label);
        else
        {
            string issue = route + ": " + label;
            if (!issues.Contains(issue)) issues.Add(issue);
        }
        return condition;
    }
    static bool MatrixMatches(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .0001f) return false;
        return true;
    }
    static float[] MatrixValues(Matrix4x4 matrix) => Enumerable.Range(0, 16).Select(i => matrix[i]).ToArray();
    static float[] RectValues(Rect rect) => new[] { rect.x, rect.y, rect.width, rect.height };
    static float[] VectorValues(Vector3 vector) => new[] { vector.x, vector.y, vector.z };
    static bool PixelWidthMatches(float actual, float expected) => Mathf.Abs(actual - expected) <= Mathf.Max(.1f, expected * .01f);
    static float ScreenUnitWidth(RectTransform rect, Camera camera) => rect == null ? 0f : Vector2.Distance(
        RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.zero)),
        RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.right)));
    static Rect ScreenBounds(RectTransform rect) => ScreenBounds(rect, mirrorCamera);
    static Rect ScreenBounds(RectTransform rect, Camera camera)
    {
        if (rect == null) return default;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }
    static bool Contains(Rect outer, Rect inner) => inner.width > 0f && inner.height > 0f
        && inner.xMin >= outer.xMin - 2f && inner.yMin >= outer.yMin - 2f
        && inner.xMax <= outer.xMax + 2f && inner.yMax <= outer.yMax + 2f;

    static IEnumerator Roundtrip(string label, bool clickMirrorBeforeRestore = false)
    {
        context = route + ": restore " + label;
        home.CaptureViewState();
        OSSessionPersistence.Capture(GameDataManager.Instance.SaveData);
        GameSessionFlow.Capture(GameDataManager.Instance.SaveData);
        string json = JsonUtility.ToJson(GameDataManager.Instance.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, route + "-snapshot-" + label + ".json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        var expected = restored.osProcess.memory.chapter03Story;
        string[] flags = expected.flags.ToArray(), items = expected.items.ToArray(), papers = expected.papers.ToArray();
        int answer = expected.knowAnswer;
        Check(JsonUtility.ToJson(expected) == JsonUtility.ToJson(S), "JSON preserves story state at " + label);
        if (clickMirrorBeforeRestore)
        {
            // The JSON above still contains the unchosen option. Test the actual click, then restore
            // that earlier save so the same real raycast can be verified on both sides of a load.
            TryClickMirrorOption("08 before restore");
            yield return null;
        }
        Messages.NotifyOSHidden(); story.ResetPresentation();
        GameDataManager.Instance.ReplaceSaveData(restored); home.Suspend();
        yield return OSSessionPersistence.Restore(restored);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible(); Focus();
        yield return Wait(() => story.IsAvailable && home.HomeWindow.IsOpen, "restored session is available at " + label);
        Check(ReferenceEquals(S, expected) && S.knowAnswer == answer && flags.All(S.Has)
            && items.All(S.items.Contains) && papers.All(S.papers.Contains),
            "ReplaceSaveData restores flags, items, papers, and answer at " + label);
    }

    static IEnumerator ReadingHoldRoundtrip(string id)
    {
        context = route + ": Live reading-hold restore";
        Check(Messages.IsReadingHoldActive && S.liveQueue.Count > 0 && S.liveQueue[0].complete
            && OSChapter03StoryDirector.LiveId(S.liveQueue[0].key) == id,
            "reading-hold restore starts after the real Live text completion");
        float waitUntil = Time.realtimeSinceStartup + 1f;
        while (Time.realtimeSinceStartup < waitUntil)
        {
            if (Messages.DisplayedEntryId != id || !Messages.IsReadingHoldActive)
                throw new InvalidOperationException("Live reading hold ended before its save point");
            Focus(); yield return null;
        }
        float savedRemaining = Messages.ReadingHoldRemaining;
        string fullText = Messages.DisplayedTextLabel.text;
        Check(savedRemaining > 1f && savedRemaining < 3.5f
            && Mathf.Abs(S.liveQueue[0].remaining - savedRemaining) < .12f,
            "saved queue holds the controller's real partially elapsed reading time");
        int starts = 0, textCompletions = 0, presentationCompletions = 0;
        Action<string, OSCharacterInteractionEntryData> onStart = (sequence, entry) => { if (entry.EntryId == id) starts++; };
        Action<string, OSCharacterInteractionEntryData> onTyped = (sequence, entry) => { if (entry.EntryId == id) textCompletions++; };
        Action<string, OSCharacterInteractionEntryData> onComplete = (sequence, entry) => { if (entry.EntryId == id) presentationCompletions++; };
        Messages.EntryPresentationStarted += onStart;
        Messages.EntryTextPlaybackCompleted += onTyped;
        Messages.EntryPresentationCompleted += onComplete;
        try
        {
            yield return Roundtrip("live-reading-hold");
            yield return Wait(() => Messages.DisplayedEntryId == id && Messages.IsReadingHoldActive,
                "restoring a reading hold rebuilds the same Live message");
            float restoredRemaining = Messages.ReadingHoldRemaining;
            Check(Mathf.Abs(restoredRemaining - savedRemaining) < .18f,
                "restored Live reading time matches the saved remaining time without resetting or skipping it");
            Check(Messages.DisplayedTextLabel.text == fullText && !Messages.IsPresenting
                && Messages.DisplayedTextLabel.maxVisibleCharacters == int.MaxValue,
                "restored reading hold displays the complete already-read text");
            Check(starts == 0 && textCompletions == 0 && presentationCompletions == 0,
                "reading-hold restore does not replay presentation or completion callbacks");
            float resumedAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - resumedAt < restoredRemaining - .12f)
            {
                if (Messages.DisplayedEntryId != id || !Messages.IsReadingHoldActive || !Messages.LinkWindow.IsOpen)
                    throw new InvalidOperationException("restored Live reading hold ended before its remaining time");
                Focus(); yield return null;
            }
            yield return Wait(() => Messages.DisplayedEntryId != id && !S.liveQueue.Any(beat =>
                OSChapter03StoryDirector.LiveId(beat.key) == id), "restored Live closes and commits its queue head after the remaining time");
            float elapsedAfterRestore = Time.realtimeSinceStartup - resumedAt;
            Check(elapsedAfterRestore >= restoredRemaining - .12f,
                "restored Live honors its full remaining reading duration");
            Check(starts == 0 && textCompletions == 0 && presentationCompletions == 0
                && GameDataManager.Instance.OSCharacterInteractionState.HasConsumedGlobalEntry(id),
                "restored reading completion stays consumed without replaying side effects");
            File.WriteAllText(Path.Combine(OutputDirectory, route + "-live-reading-hold-restore.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(new { id, savedRemaining, restoredRemaining,
                    elapsedAfterRestore, starts, textCompletions, presentationCompletions }, Newtonsoft.Json.Formatting.Indented));
        }
        finally
        {
            Messages.EntryPresentationStarted -= onStart;
            Messages.EntryTextPlaybackCompleted -= onTyped;
            Messages.EntryPresentationCompleted -= onComplete;
        }
    }

    static void Keypad(string digits)
    {
        foreach (char digit in digits) ModalButton("Digit" + digit).onClick.Invoke();
        ModalButton("Submit").onClick.Invoke();
    }
    static Button ModalButton(string name)
    {
        var frame = Get(P, name == "LetterClose" ? "modalRoot" : "modalFrame") as RectTransform;
        var button = frame?.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
        if (button == null) throw new InvalidOperationException("Missing modal button " + name);
        return button;
    }

    static IEnumerator ClickEnvelope()
    {
        Canvas.ForceUpdateCanvases();
        var envelope = P.LetterEnvelope;
        var hit = envelope?.GetComponent<OSChapter03PresentationHit>();
        Check(envelope != null && hit != null && P.IsLetterEnvelopeReady, "settled envelope exposes its real click handler");
        var root = Get(P, "modalRoot") as RectTransform;
        var canvas = root.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, envelope.TransformPoint(envelope.rect.center));
        var raycast = new RaycastResult { gameObject = envelope.gameObject,
            module = envelope.GetComponentInParent<GraphicRaycaster>(), screenPosition = point };
        hit.OnPointerClick(new PointerEventData(EventSystem.current) {
            button = PointerEventData.InputButton.Left, pointerId = -1, position = point,
            pressPosition = point, pointerPressRaycast = raycast, pointerCurrentRaycast = raycast, dragging = false
        });
        yield return Wait(() => P.IsLetterUnfolded, "real envelope click waits for the red partner and completed opening", 10f);
    }

    static IEnumerator RunArtVisuals()
    {
        route = "art"; context = "authored Chapter 3 art"; coffeeRoute = false;
        yield return Fixture();
        UnbindEvidence(); story.enabled = false; storyDisabledForVisuals = true;
        Messages.SetGlobalMessagesSuppressed(true); P.Initialize(os); P.Paused = false;
        var content = OSChapter03StoryContent.Instance;
        string[] ids = { "old-leg", "hairclip", "bed-hand" };
        string[] titles = { "ui.art.old_leg", "ui.art.hairclip", "os.home.lab.bed.label" };
        foreach (string id in ids.Concat(new[] { "mirror-face", "mirror-eyes" }))
        {
            var sprite = Resources.Load<Sprite>("Chapter03Story/Art/" + id);
            Check(sprite != null && sprite.rect.width > 0f && sprite.rect.height > 0f
                && sprite.texture != null && sprite.texture.filterMode == FilterMode.Point,
                "authored Sprite has a nonzero source canvas and Point sampling: " + id);
        }
        var faceSprite = Resources.Load<Sprite>("Chapter03Story/Art/mirror-face");
        var eyesSprite = Resources.Load<Sprite>("Chapter03Story/Art/mirror-eyes");
        Check(faceSprite.rect.size == eyesSprite.rect.size && faceSprite.texture.width == eyesSprite.texture.width
            && faceSprite.texture.height == eyesSprite.texture.height,
            "mirror source face and eye canvases share the same pixel dimensions");
        for (int i = 0; i < ids.Length; i++)
        {
            int callbacks = 0;
            string id = ids[i], title = ArtTitleForQA(titles[i]);
            P.ShowArt(id, title, () => callbacks++);
            yield return Wait(() => ModalReady, "art accepts input: " + id);
            var image = P.ArtImage;
            Check(image != null && image.sprite == Resources.Load<Sprite>("Chapter03Story/Art/" + id)
                && image.rectTransform.rect.size == new Vector2(216f, 162f) && image.preserveAspect,
                "art displays the authored Sprite in the fixed 216 by 162 UI viewport: " + id);
            Check(InsideParent(image.rectTransform), "art fits its frame: " + id);
            yield return Screenshot(id);
            foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
            {
                GameDataManager.Instance.SetCurrentLanguage(language); yield return null;
                P.RefreshModalText(ArtTitleForQA(titles[i]));
                Check(ReferenceEquals(P.ArtImage, image) && callbacks == 0,
                    "language refresh preserves art and its callback: " + id + "/" + language);
            }
            GameDataManager.Instance.SetCurrentLanguage(LanguageState.zh_CN); yield return null;
            P.RefreshModalText(ArtTitleForQA(titles[i]));
            ModalButton("Close").onClick.Invoke();
            Check(callbacks == 1 && !P.IsModalOpen, "the actual art Close button closes and commits its callback: " + id);
            P.HideModal(true);
            Check(callbacks == 1 && !P.IsModalOpen, "closing an already closed art modal does not repeat its callback: " + id);
        }

        int cancelled = 0;
        P.ShowArt("old-leg", content.Text("ui.art.old_leg"), () => cancelled++);
        P.ShowArt("bed-hand", ArtTitleForQA("os.home.lab.bed.label"), dismissible: false);
        yield return Wait(() => ModalReady, "timed art input guard is ready");
        Call(P, "DismissFromInput");
        Check(cancelled == 0 && P.IsArtAutoClosing && P.IsModalOpen, "art replacement cancels the old callback and timed art ignores dismissal");
        bool missingRaised = false;
        try { P.ShowArt("qa-missing-art", string.Empty); }
        catch (InvalidOperationException error) { missingRaised = error.Message.Contains("Chapter03Story/Art/qa-missing-art"); }
        Check(missingRaised && P.ArtId == "bed-hand", "missing asset reports its resource path without destroying the current art");
        P.HideModal(false);

        P.ShowMirror(content.Text("ui.mirror.title"), string.Empty, attentionText: content.Text("ui.mirror.attention"));
        var face = P.MirrorRoot.Find("Frame/MirrorFace").GetComponent<Image>();
        var eyes = P.MirrorRoot.Find("Frame/MirrorEyes").GetComponent<Image>();
        Check(face.rectTransform.rect.size == new Vector2(144f, 176f)
            && face.rectTransform.anchoredPosition == eyes.rectTransform.anchoredPosition
            && face.rectTransform.rect.size == eyes.rectTransform.rect.size, "mirror layers share the fixed registered 144 by 176 UI viewport");
        Check(InsideParent(P.MirrorRoot) && InsideParent(face.rectTransform), "300-pixel mirror window and full face fit the authored Windows area");
        yield return Wait(() => eyes.color.a > .2f && face.color.a == 0f, "authored eyes appear before the full face");
        CheckMirrorAttention("mirror attention stays hidden during the eyes-to-face reveal", shown: false);
        P.Paused = true;
        float reveal = (float)Get(P, "mirrorRevealClock");
        yield return Screenshot("mirror-eyes-first");
        Check(Mathf.Approximately(reveal, (float)Get(P, "mirrorRevealClock")), "pausing freezes the mirror reveal");
        P.Paused = false;
        yield return Wait(() => face.color.a == 1f && eyes.color.a == 0f, "full mirror face completes its reveal");
        CheckMirrorAttention("the full red attention caption appears after the face reveal");
        yield return Screenshot("mirror-full-face");
        P.PulseMirror(.8f);
        yield return Screenshot("mirror-recognition-ripple");
        P.RefreshMirrorText(content.Text("ui.mirror.title"), string.Empty, attentionText: content.Text("ui.mirror.attention"));
        Check(ReferenceEquals(face, P.MirrorRoot.Find("Frame/MirrorFace").GetComponent<Image>()) && face.color.a == 1f,
            "mirror language refresh retains the revealed art");
        CheckMirrorAttention("mirror language refresh retains its single red attention caption");
        P.HideMirror(); yield return null;
        P.ShowMirror(content.Text("ui.mirror.title"), string.Empty, revealEyesFirst: false, attentionText: content.Text("ui.mirror.attention"));
        Check(P.MirrorRoot.Find("Frame/MirrorFace").GetComponent<Image>().color.a == 1f
            && P.MirrorRoot.Find("Frame/MirrorEyes").GetComponent<Image>().color.a == 0f,
            "restoring an already visible mirror skips the entrance reveal");
        CheckMirrorAttention("restoring an already visible mirror immediately restores its attention caption");
        P.ResetPresentation(); yield return null;
        Check(!P.IsModalOpen && P.MirrorRoot == null && ((IList)Get(P, "ownedRoots")).Count == 0,
            "art and mirror reset remove all owned presentation roots");
    }

    static string ArtTitleForQA(string key) => key.StartsWith("os.home.", StringComparison.Ordinal)
        ? GameDataManager.Instance.GetLocalizedText(key, string.Empty) : OSChapter03StoryContent.Instance.Text(key);

    static void CheckMirrorFullyRevealed(string label)
    {
        var face = P.MirrorRoot?.Find("Frame/MirrorFace")?.GetComponent<Image>();
        var eyes = P.MirrorRoot?.Find("Frame/MirrorEyes")?.GetComponent<Image>();
        Check(face != null && eyes != null && face.color.a == 1f && eyes.color.a == 0f
            && (float)Get(P, "mirrorRevealClock") < 0f, label);
    }

    static void CheckMirrorAttention(string label, bool shown = true)
    {
        var frame = P.MirrorRoot?.Find("Frame");
        var title = frame?.Find("Title")?.GetComponent<TMP_Text>();
        var attention = frame?.Find("Attention")?.GetComponent<TMP_Text>();
        string expected = OSChapter03StoryContent.Instance.Text("ui.mirror.attention");
        Check(frame != null && title != null && frame.Find("Interior") != null && frame.Find("Divider") != null
            && frame.Find("Identity") == null && frame.Find("Spoken") == null && frame.Find("Reflection") == null
            && frame.Find("RecognitionFrame") == null
            && frame.GetComponentsInChildren<TMP_Text>(true).Length == 2
            && attention != null && attention.text == expected && attention.isActiveAndEnabled == shown
            && Mathf.Approximately(attention.fontSize, 12f) && attention.color == OSPalette.Red
            && attention.maxVisibleCharacters >= attention.text.Length, label);
        if (GameDataManager.Instance.CurrentLanguageState == LanguageState.zh_CN)
            Check(expected == "..主人??", label + ": Chinese attention text matches the authored punctuation");
    }

    static bool InsideParent(RectTransform rect)
    {
        var parent = rect.parent as RectTransform;
        if (parent == null) return false;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            Vector3 point = parent.InverseTransformPoint(corner);
            if (point.x < parent.rect.xMin - .5f || point.x > parent.rect.xMax + .5f
                || point.y < parent.rect.yMin - .5f || point.y > parent.rect.yMax + .5f) return false;
        }
        return true;
    }

    // Fast visual audit, independent of the full story replay. The same production
    // presenter and current-language content create every frame. Only its director
    // is suspended so a screenshot cannot accidentally advance a narrative queue.
    static IEnumerator RunLocalizationVisuals()
    {
        route = "localization-setup"; coffeeRoute = false;
        yield return Fixture();
        UnbindEvidence(); story.enabled = false; storyDisabledForVisuals = true;
        Messages.SetGlobalMessagesSuppressed(true);
        P.Initialize(os); P.Paused = false;
        var content = OSChapter03StoryContent.Instance;
        var reports = new List<object>();
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
        {
            route = "locale-" + language;
            context = route + ": paper/keypad/letter layout";
            GameDataManager.Instance.SetCurrentLanguage(language);
            yield return null;
            P.Paused = false;
            string[] paperIds = { "note_01", "diary_02", "diary_03", "diary_04", "diary_05", "diary_06", "diary_07",
                "diary_08", "husband_letter", "note_09", "diary_10", "repair_11", "diary_13" };
            foreach (string id in paperIds)
            {
                string style = id.StartsWith("note_", StringComparison.Ordinal) ? "note" : id == "husband_letter" ? "letter" : "diary";
                P.ShowPaper((string)Call(story, "PaperTitle", id), content.PaperBody(id), null, style);
                yield return Wait(() => ModalReady, "localized " + id + " opens");
                yield return CapturePages(id, reports);
                P.HideModal(false);
            }

            string submitted = null;
            P.ShowKeypad(content.Text("ui.password.title"), content.Text("ui.password.error"), value => submitted = value);
            yield return Wait(() => ModalReady, "localized keypad opens");
            Keypad("0710");
            Check(submitted == "0710", "localized keypad still submits all four digits");
            ValidateLabels("keypad", reports);
            yield return Screenshot("keypad");
            P.HideModal(false);

            P.BeginLetter(content.Text("ui.paper.letter"), content.PaperBody("final_letter"), null);
            yield return Wait(() => ModalReady, "localized letter entrance completes");
            // Presentation-only setup for this visual audit; full-route runner
            // above separately verifies the player's actual drag interaction.
            P.RestoreUnfoldedLetter(0);
            yield return null;
            Check(P.IsLetterUnfolded, "localized letter unfolds");
            yield return CapturePages("letter", reports);
            P.HideModal(false);
        }
        File.WriteAllText(Path.Combine(OutputDirectory, "localization-layout.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(reports, Newtonsoft.Json.Formatting.Indented));
        Check(reports.Count >= 150, "all ten languages have all fourteen papers, keypad, and final letter evidence");
    }

    static IEnumerator CapturePages(string kind, List<object> reports)
    {
        int pages = (int)Get(P, "paperPages");
        Check(pages >= 1, kind + " has at least one page");
        for (int page = 0; page < pages; page++)
        {
            Canvas.ForceUpdateCanvases();
            var body = Get(P, "paperBody") as TMP_Text;
            body.ForceMeshUpdate(true);
            Check(body.textInfo.characterCount > 0, kind + " contains shaped characters");
            int visible = 0;
            var invalidGlyphs = new HashSet<string>();
            Rect bounds = body.rectTransform.rect;
            var escapedGlyphs = new HashSet<string>();
            for (int i = 0; i < body.textInfo.characterCount; i++)
            {
                var character = body.textInfo.characterInfo[i];
                if (!character.isVisible || character.pageNumber != page) continue;
                visible++;
                if (character.textElement?.glyph == null || character.textElement.glyph.index == 0)
                    invalidGlyphs.Add("U+" + ((int)character.character).ToString("X4"));
                if (character.bottomLeft.x < bounds.xMin - 1f || character.topRight.x > bounds.xMax + 1f
                    || character.bottomLeft.y < bounds.yMin - 1f || character.topRight.y > bounds.yMax + 1f)
                    escapedGlyphs.Add("U+" + ((int)character.character).ToString("X4"));
            }
            Check(visible > 0, kind + " page " + (page + 1) + " is not blank");
            Check(invalidGlyphs.Count == 0, kind + " page " + (page + 1) + " has no missing glyphs: " + string.Join(",", invalidGlyphs));
            Check(escapedGlyphs.Count == 0, kind + " page " + (page + 1) + " fits the paper reading inset: " + string.Join(",", escapedGlyphs));
            Check(Mathf.Approximately(body.fontSize, Mathf.Round(body.fontSize)) && !body.enableAutoSizing,
                kind + " uses a fixed integer font size");
            reports.Add(new { language = route, kind, page = page + 1, pages, visibleCharacters = visible,
                characters = body.textInfo.characterCount, lines = body.textInfo.lineCount,
                rect = body.rectTransform.rect.ToString(), font = body.font?.name });
            yield return Screenshot(kind + "-page-" + (page + 1).ToString("00"));
            if (page + 1 < pages)
            {
                ModalButton("Next").onClick.Invoke(); yield return null;
                Check((int)Get(P, "paperPage") == page + 1, kind + " Next button reaches page " + (page + 2));
            }
        }
    }

    static void ValidateLabels(string kind, List<object> reports)
    {
        var frame = Get(P, "modalFrame") as RectTransform;
        foreach (var label in frame.GetComponentsInChildren<TMP_Text>(true))
        {
            label.ForceMeshUpdate(true);
            Check(!label.isTextTruncated, kind + " label is not truncated: " + label.transform.parent.name + "/" + label.name
                + " text=" + label.text + " rect=" + label.rectTransform.rect);
        }
        reports.Add(new { language = route, kind, frame = frame.rect.ToString(), labels = frame.GetComponentsInChildren<TMP_Text>(true).Length });
    }

    static void OnData(OSMemoryReadData data) { if (data != null) shownNodes.Add(data.DataIndex); }
    static void OnStep(Vector2Int tile, bool water) => walkedSteps++;
    static void OnLiveShown(string sequence, OSCharacterInteractionEntryData entry) => RecordShownLive(entry?.EntryId);
    static void RecordShownLive(string id)
    {
        // A Drive predicate may become true between frames before Pump runs
        // again. Record the actual presentation event without advancing it.
        if (OSChapter03StoryDirector.IsOwnEntry(id) && !shownLive.Contains(id)) shownLive.Add(id);
    }
    static void UnbindEvidence()
    {
        if (memory != null) memory.DataShown -= OnData;
        if (home?.View != null) home.View.Stepped -= OnStep;
        if (evidenceMessages != null) evidenceMessages.EntryPresentationStarted -= OnLiveShown;
        evidenceMessages = null;
    }
    static void WriteEvidence() => File.WriteAllText(Path.Combine(OutputDirectory, route + "-evidence.json"),
        Newtonsoft.Json.JsonConvert.SerializeObject(new { route, shownNodes, shownLive, chosenOptions, walkedSteps,
            state = Newtonsoft.Json.Linq.JObject.Parse(JsonUtility.ToJson(S)) }, Newtonsoft.Json.Formatting.Indented));

    static void Focus()
    {
        testMouse?.MakeCurrent();
        if (os == null) return;
        story?.DebugSetFocus(true); os.Chapter03Entry?.DebugSetFocus(true); os.Chapter02FinalTalk?.DebugSetFocus(true);
        SetIfPresent(os.Chapter02FinalTalk?.Nightfall, "hasFocus", true);
        SetIfPresent(os.CursorRelease, "hasFocus", true);

        // Isolated background QA only: production pauses this LateUpdate timer when the
        // Editor lacks focus. Resume that same timer only when focus is its sole pause cause.
        // No completion, seeking, elapsed-time changes, or real Editor focus are used.
        if (Application.isFocused || Application.isBatchMode || GameAutosaveService.Instance?.IsTestSession != true
            || story?.IsAvailable != true || P == null || P.Paused || !os.IsOSVisible
            || Messages == null || !Messages.isActiveAndEnabled || !OSChapter03StoryDirector.IsOwnEntry(Messages.DisplayedEntryId)
            || !(bool)Get(Messages, "isOSVisible") || (bool)Get(Messages, "chapter03TextPaused")
            || (bool)Get(Messages, "collectionReadingPaused") || (bool)Call(Messages, "IsSuspendedByMinimize")
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || (bool)Call(Messages, "IsOptionCountdownSettingsOpen")) return;
        var link = Messages.LinkWindow;
        var owner = Get(Messages, "ownerCharacterWindow") as OSWindowUnit;
        if (link == null || !link.IsOpen || link.IsOpening || link.IsClosing || link.IsMinimized || !link.gameObject.activeInHierarchy
            || owner != null && (!owner.IsOpen || owner.IsOpening || owner.IsClosing || owner.IsMinimized || !owner.gameObject.activeInHierarchy)) return;
        var timer = Get(Messages, "autoCloseTween") as DG.Tweening.Tween;
        if (timer != null && DG.Tweening.TweenExtensions.IsActive(timer))
        {
            DG.Tweening.TweenExtensions.Play(timer);
            string note = route + ": QA background isolation resumes only focus-paused Live reading holds at their original LateUpdate pace";
            if (!checks.Contains(note)) checks.Add(note);
        }
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { Focus(); yield return null; }
        Check(predicate(), label + " | " + Describe());
    }
    static IEnumerator Screenshot(string name)
    {
        string path = Path.Combine(OutputDirectory, route + "-" + name + ".png");
        ScreenCapture.CaptureScreenshot(path); yield return null;
        yield return Wait(() => File.Exists(path), "screenshot saved: " + name, 8f);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        string value = route + ": " + label;
        if (!checks.Contains(value)) checks.Add(value);
    }
    static string Describe() => "route=" + route + ", phase=" + story?.CurrentPhase
        + ", node=" + memory?.CurrentData?.DataIndex + ", available=" + story?.IsAvailable
        + ", room=" + home?.View?.CurrentRoom?.id + ", tile=" + home?.View?.PlayerTile
        + ", walking=" + home?.View?.IsWalking + ", modal=" + story?.Presentation?.IsModalOpen
        + ", queue=" + (story?.State == null ? "null" : string.Join(",", story.State.memoryQueue.Take(4).Select(b => b.type + ":" + b.key + ":" + b.value)))
        + ", live=" + os?.LinkMessageController?.DisplayedEntryId;
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static void SetIfPresent(object target, string name, object value) => target?.GetType().GetField(name, Fields)?.SetValue(target, value);
    static void InvokeOptional(object target, string name) => target?.GetType().GetMethod(name, Fields)?.Invoke(target, null);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
