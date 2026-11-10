using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in, isolated Play Mode regression. Fixtures supply prerequisites; every
// inspected object emits the real Home event. No production save/scene is edited.
public static class OSChapter03InteractionVisualsQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int DimInk = 7;
    static readonly List<string> checks = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly HashSet<string> covered = new HashSet<string>();
    static readonly Dictionary<OSCursorReleaseDirector, bool> actorFocus = new Dictionary<OSCursorReleaseDirector, bool>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSWindowUnit memoryWindow;
    static GameSaveData baseline;
    static Mouse mouse;
    static string context;
    static bool cancel;
    static int plate;
    static OSChapter03StoryState S => story.State;
    static OSHomeView V => home.View;
    static OSChapter03StoryPresentation P => story.Presentation;
    static OSLinkMessageController Messages => os.LinkMessageController;
    static bool ModalReady => P != null && P.IsModalOpen && (bool)Get(P, "CanInteract");
    static bool Idle => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !Messages.IsReadingHoldActive
        && !Messages.IsWaitingForOption && !P.IsModalOpen && !P.IsAnimating && !V.IsWalking && !V.IsTransitioning;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameManager.Instance == null
            || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || SceneLoadManager.Instance.IsBusy
            || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning) return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to hash the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        data = GameDataManager.Instance; cancel = false; plate = 0; context = "initialization";
        checks.Clear(); screenshots.Clear(); observations.Clear(); covered.Clear(); actorFocus.Clear();
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03InteractionVisualsQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance; var original = data.SaveData;
        var language = data.CurrentLanguageState; var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume; var random = UnityEngine.Random.state; var previousMouse = Mouse.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null; var work = new Stack<IEnumerator>(); Exception failure = null;
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); data.SetCurrentLanguage(LanguageState.zh_CN); Set(setup, "isDemo", false);
            Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            mouse = InputSystem.AddDevice<Mouse>("Chapter03InteractionVisualsQAMouse");
            InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancel) throw new OperationCanceledException("Interaction visuals QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + failure;
                    string path = Path.Combine(OutputDirectory, "failure.png"); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
                    yield return null; yield return new WaitForSecondsRealtime(.25f);
                    Debug.LogError("[Chapter03 Interaction Visuals QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null) { Check(files.Select(Hash).SequenceEqual(hashes), "original save and scene hashes are unchanged"); Status = "PASS"; }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                foreach (var pair in actorFocus) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                bool focusRestored = actorFocus.All(pair => pair.Key == null || Equals(Get(pair.Key, "hasFocus"), pair.Value));
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                os?.LinkMessageController?.NotifyOSHidden(); story?.ResetPresentation(); home?.Suspend();
                story?.DebugSetFocus(false); os?.Chapter03Entry?.DebugSetFocus(false);
                os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourceEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume || !focusRestored
                    || resources != null && resources.enabled != resourceEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse
                    || !UnityEngine.Random.state.Equals(random)) throw new InvalidOperationException("Original session settings were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save or scene changed");
                checks.Add("cleanup restores original save object, language, Demo, resources, audio, background, input, focus, random state and hashes");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, covered = covered.OrderBy(id => id).ToArray(), screenshots,
                originalFiles = files, originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                note = "Isolated, muted, background Play Mode. Object input uses V.Activate -> ThingActivated -> production OnThing. Real modal Close/buttons and JSON/session restore. Typing and existing Live reading timers are completed immediately. Flags/items in named stage fixtures supply prerequisites. Native 240x160 QA plates temporarily add full-room light and hide actors for palette counts, then restore presentation immediately; natural screenshots use the unchanged lighting. Mirror/reunion/final-letter cinematics are covered by stable stage fixtures, not replayed. No door visit semantics or natural timing are asserted. Exit Play Mode after this run."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Interaction Visuals QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; memory = null; home = null; story = null; memoryWindow = null; baseline = null; mouse = null;
        }
    }

    static IEnumerator Run()
    {
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart), "real Chapter 3 checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        os = OS_Handle.Instance; memory = os.MemoryWindowController; home = memory.GetComponent<OSChapter03HomeController>();
        story = os.Chapter03Story; memoryWindow = memory.GetComponent<OSWindowUnit>();
        Check(GameTest.Instance.JumpSucceeded && savesIsolated() && home?.View != null && story != null, "real Home resolves in isolated session");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false); baseline = Clone(data.SaveData);
        yield return OrdinaryThings();
        yield return PaperCollection();
        yield return CoffeeStages();
        yield return OtherStages();
        yield return SpecialThings();
        yield return YardThings();
        yield return LegacySaves();
        var catalogue = V.Layout.rooms.SelectMany(room => room.things).Where(thing => thing != null).ToArray();
        string[] untested = catalogue.Where(thing => !covered.Contains(thing.id)).Select(thing => thing.id).ToArray();
        observations.Add(new { context = "coverage", totalThings = catalogue.Length, observedThings = covered.Count, untested });
        Check(untested.Length == 0, "every authored Thing has a renderer/state observation");
    }
    static bool savesIsolated() => GameAutosaveService.Instance.IsTestSession;

    static IEnumerator Prepare(string name, string room, Action<OSChapter03StoryState> configure = null)
    {
        context = name; Messages.NotifyOSHidden(); story.ResetPresentation();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true)) if (window.IsOpen) window.CloseSilently();
        home.ResetRuntime(); memory.PrepareRuntime();
        var save = Clone(baseline); save.flow.language = LanguageState.zh_CN;
        save.osProcess.memory.currentDataIndex = 8110;
        save.osProcess.memory.triggeredDataIndices = new List<int> { 8110 };
        save.osProcess.memory.chapter03HomeInitialized = true;
        save.osProcess.memory.chapter03HomeView = new OSHomeView.State { room = room, me = new Vector2Int(7, 5), dong = new Vector2Int(8, 5) };
        var state = new OSChapter03StoryState { version = 1, interactionVisualVersion = 1,
            phase = (int)OSChapter03StoryDirector.Phase.Explore, homeOpened = true };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8020", "opening_8070", "opening_8080", "opening_8110", "paper_note_01" }) state.Add(flag);
        foreach (string id in new[] { "study", "west", "hall", "kitchen", "east", "store", "bed", "bath", "junk", "lab", "yard" }) state.Add("room_" + id);
        if (room == "bed" || room == "bath" || room == "lab") state.Add("bed_open");
        if (room == "lab") { state.phase = (int)OSChapter03StoryDirector.Phase.Lab; state.Add("lab_open"); state.Add("mirror_done"); }
        state.papers.Add("note_01"); configure?.Invoke(state); save.osProcess.memory.chapter03Story = state;
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData(); save.osResume = new OSResumeSaveData();
        data.ReplaceSaveData(save); Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f); Check(os.OpenMemoryWindow(), "opens the real Memory window");
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && !home.IsRestoringView
            && V.CurrentRoom?.id == room && V.InputEnabled && memory.CurrentData?.DataIndex == 8110, "fixture becomes available");
        CompleteMemory(); yield return Settled();
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "fixture has no pending story response");
    }

    static IEnumerator OrdinaryThings()
    {
        var rooms = new[] {
            ("study", new[] { "study_shelf", "study_lamp", "study_page", "study_dock", "study_curtain", "study_chair" }),
            ("hall", new[] { "hall_photo", "hall_sofa", "hall_phone", "hall_lamp", "hall_curtain", "hall_shoes", "hall_coat", "hall_umbrella" }),
            ("kitchen", new[] { "kitchen_calendar", "kitchen_sugar", "kitchen_table", "kitchen_drawer" }),
            ("store", new[] { "store_boxes", "store_old_leg", "store_tube", "store_toolbox", "store_trail" }),
            ("bed", new[] { "bed_lamp", "bed_bed", "bed_curtain", "bed_wardrobe" }),
            ("junk", new[] { "junk_boxes", "junk_wheelchair", "junk_medicine" }),
            ("lab", new[] { "lab_mask", "lab_bench" })
        };
        foreach (var group in rooms)
        {
            yield return Prepare("ordinary objects in " + group.Item1, group.Item1);
            foreach (string id in group.Item2) AssertThing(id, false);
            Plate(group.Item1 + "-before");
            foreach (string id in group.Item2) Thing(id);
            yield return Settled();
            foreach (string id in group.Item2) AssertThing(id, true);
            yield return Drain(); Plate(group.Item1 + "-after");
            yield return Roundtrip("ordinary-" + group.Item1);
            foreach (string id in group.Item2) AssertThing(id, true);
            if (group.Item1 == "study") yield return Screenshot("01-study-after-natural.png");
        }
    }

    static IEnumerator PaperCollection()
    {
        var cases = new[] {
            ("study", "study_desk", "diary_07", false), ("west", "west_note", "diary_05", true),
            ("hall", "hall_paper", "diary_04", true), ("kitchen", "kitchen_coffee_jar", "diary_02", false),
            ("kitchen", "kitchen_cup", "diary_03", false), ("kitchen", "kitchen_fridge", "diary_06", false),
            ("store", "store_makeup", "diary_08", false), ("bed", "bed_letter", "husband_letter", true),
            ("lab", "lab_mask_note", "diary_10", true), ("lab", "lab_pillow_note", "diary_13", true),
            ("lab", "lab_log", "repair_11", false)
        };
        foreach (var item in cases)
        {
            bool wallRecord = item.Item3 == "repair_11";
            yield return Prepare("paper: " + item.Item3, item.Item1, state => {
                if (item.Item2 == "kitchen_cup") state.Add("coffee_brewed");
                if (item.Item2 == "store_makeup") { state.Add("knows_question"); state.Add("knows_reply_done"); state.knowAnswer = 1; }
            });
            AssertThing(item.Item2, false, paper: !wallRecord);
            Thing(item.Item2); yield return WaitModal();
            Check(S.Has("paper_requested_" + item.Item3) && !S.papers.Contains(item.Item3), "requested paper remains uncollected while its modal is open");
            AssertThing(item.Item2, true, paper: !wallRecord);
            if (item.Item2 == "study_desk" || item.Item2 == "west_note")
            {
                Plate(item.Item2 + "-pending"); yield return Roundtrip(item.Item2 + "-pending"); yield return WaitModal();
                AssertThing(item.Item2, true, paper: true);
            }
            CloseModal(); yield return Drain();
            Check(S.papers.Contains(item.Item3), "real Close callback collects " + item.Item3);
            AssertThing(item.Item2, true, hidden: item.Item4); Plate(item.Item2 + "-collected");
            yield return Roundtrip(item.Item2 + "-collected");
            AssertThing(item.Item2, true, hidden: item.Item4);
        }
    }

    static IEnumerator CoffeeStages()
    {
        yield return Prepare("actual coffee interaction stages", "kitchen");
        Thing("kitchen_sink"); yield return Drain(); AssertThing("kitchen_sink", true);
        Thing("kitchen_cup"); yield return Drain(); AssertThing("kitchen_cup", true);
        Thing("kitchen_coffee_machine"); yield return Drain();
        AssertThing("kitchen_coffee_machine", true); AssertThing("kitchen_sink", false); AssertThing("kitchen_cup", true);
        yield return Roundtrip("coffee-sink-new-action"); AssertThing("kitchen_sink", false);
        Thing("kitchen_sink"); yield return Drain();
        Check(S.Has("coffee_water"), "real sink interaction fills the machine");
        AssertThing("kitchen_sink", true); AssertThing("kitchen_coffee_machine", false);
        Thing("kitchen_coffee_machine"); yield return Drain(); AssertThing("kitchen_coffee_machine", true);
        Thing("kitchen_coffee_jar"); yield return WaitModal(); CloseModal(); yield return Drain();
        Check(S.Has("coffee_beans"), "real coffee-jar interaction supplies beans"); AssertThing("kitchen_coffee_machine", false);
        Thing("kitchen_coffee_machine"); yield return Drain();
        Check(S.Has("coffee_brewed"), "real machine interaction brews coffee");
        AssertThing("kitchen_coffee_machine", true); AssertThing("kitchen_cup", false, paper: true);
        Plate("coffee-cup-new-action");
        Thing("kitchen_cup"); yield return WaitModal(); AssertThing("kitchen_cup", true, paper: true);
        CloseModal(); yield return Drain(); AssertThing("kitchen_cup", true);
        Check(S.items.Contains("coffee") && S.papers.Contains("diary_03"), "real cup interaction keeps coffee and diary");
        yield return Roundtrip("coffee-finished"); AssertThing("kitchen_cup", true); AssertThing("kitchen_coffee_machine", true);
        yield return Screenshot("02-coffee-finished-natural.png");
    }

    static IEnumerator OtherStages()
    {
        foreach (string room in new[] { "study", "hall", "kitchen" })
        {
            yield return Prepare("clock stages in " + room, room);
            string id = room + "_clock"; Thing(id); yield return Drain(); AssertThing(id, true);
            S.clock = 2; yield return Settled(); AssertThing(id, false);
            Thing(id); yield return Drain(); AssertThing(id, true);
            S.clock = 3; yield return Settled(); AssertThing(id, false);
            yield return Roundtrip(room + "-clock-new-time"); AssertThing(id, false);
            Thing(id); yield return Drain(); AssertThing(id, true);
        }
        yield return Prepare("screwdriver becomes useful", "kitchen", state => state.Add("drawer_seen"));
        Thing("kitchen_screwdriver"); yield return Drain(); AssertThing("kitchen_screwdriver", true);
        S.Add("store_door_seen"); yield return Settled(); AssertThing("kitchen_screwdriver", false);
        Thing("kitchen_screwdriver"); yield return Drain(); AssertThing("kitchen_screwdriver", true, hidden: true);
        Check(S.items.Contains("screwdriver"), "real newly available pickup takes the screwdriver");
        yield return Roundtrip("screwdriver-taken"); AssertThing("kitchen_screwdriver", true, hidden: true);

        yield return Prepare("key has three response stages", "store", state => state.Add("makeup_seen"));
        Thing("store_key"); yield return Drain(); AssertThing("store_key", true);
        foreach (string flag in new[] { "shoes_seen", "morning_seen", "medicine_seen", "deduction_queued", "deduction_done" }) S.Add(flag);
        yield return Settled(); AssertThing("store_key", false);
        Thing("store_key"); yield return Drain(); AssertThing("store_key", true);
        S.Add("yard_returned"); yield return Settled(); AssertThing("store_key", false);
        Thing("store_key"); yield return Drain(); AssertThing("store_key", true, hidden: true);
        Check(S.items.Contains("key"), "real newly available pickup takes the key");
        yield return Roundtrip("key-taken"); AssertThing("store_key", true, hidden: true);

        yield return Prepare("nightstand gains coffee", "bed");
        Thing("bed_nightstand"); yield return Drain(); AssertThing("bed_nightstand", true);
        S.items.Add("coffee"); yield return Settled(); AssertThing("bed_nightstand", false);
        Thing("bed_nightstand"); yield return Drain(); AssertThing("bed_nightstand", true);
        Check(S.Has("coffee_placed") && !S.items.Contains("coffee"), "real nightstand callback places the coffee");
        yield return Roundtrip("coffee-on-nightstand"); AssertThing("bed_nightstand", true);

        yield return Prepare("lab bed gains its second interaction", "lab");
        Thing("lab_bed"); yield return Drain(); AssertThing("lab_bed", true);
        Thing("lab_pillow_note"); yield return WaitModal(); CloseModal(); yield return Drain();
        AssertThing("lab_bed", false); yield return Roundtrip("lab-bed-second-available"); AssertThing("lab_bed", false);
        Thing("lab_bed"); yield return Settled();
        Check(S.Has("bed_second_started"), "the real second bed interaction starts after diary collection"); AssertThing("lab_bed", true);
        Plate("lab-bed-second-requested"); // The next fixture cancels this isolated cinematic; it is not a timing test.
    }

    static IEnumerator SpecialThings()
    {
        yield return Prepare("mirror pending is still available", "bath");
        AssertThing("bath_mirror", false); Thing("bath_mirror"); yield return WaitModal(); AssertThing("bath_mirror", false);
        Check(!S.Has("mirror_done"), "opening the mirror note does not complete the mirror");
        yield return Prepare("mirror finished", "bath", state => { state.Add("mirror_done"); state.papers.Add("note_09"); });
        AssertThing("bath_mirror", true); Plate("mirror-finished");
        yield return Roundtrip("mirror-finished"); AssertThing("bath_mirror", true);

        yield return Prepare("keypad stays available until accepted", "east");
        AssertThing("east_keypad", false); Thing("east_keypad"); yield return WaitModal(); AssertThing("east_keypad", false);
        CloseModal(); yield return Drain(); AssertThing("east_keypad", false);
        S.Add("password_ready"); Thing("east_keypad"); yield return WaitModal();
        foreach (char digit in "0710") ModalButton("Digit" + digit).onClick.Invoke();
        ModalButton("Submit").onClick.Invoke(); yield return Settled();
        Check(S.Has("lab_open"), "real 0710 submission completes the keypad"); AssertThing("east_keypad", true);
        Plate("keypad-finished");

        yield return Prepare("lab box waiting", "lab", state => state.Add("bed_second_done"));
        AssertThing("lab_box", false); Plate("lab-box-waiting");
        yield return Prepare("lab box open and hairclip waiting", "lab", OpenBox);
        AssertThing("lab_box", true); Check(!V.FindThing("lab_box").interactive, "opened box remains dark after its interaction is disabled");
        AssertThing("lab_hairclip", false); AssertThing("lab_letter", false);
        Thing("lab_hairclip"); yield return Drain();
        AssertThing("lab_hairclip", true); AssertThing("lab_letter", false); Plate("hairclip-seen-letter-waiting");
        yield return Roundtrip("hairclip-seen"); AssertThing("lab_hairclip", true); AssertThing("lab_box", true);
        yield return Prepare("final letter collected", "lab", state => { OpenBox(state); state.Add("hairclip_seen"); state.Add("letter_started"); state.Add("letter_collected"); });
        AssertThing("lab_letter", true, hidden: true); Plate("final-letter-gone");
        yield return Roundtrip("final-letter-collected"); AssertThing("lab_letter", true, hidden: true);
        yield return Screenshot("03-final-letter-gone-natural.png");

        foreach (var pair in new[] { ("west", "west_painting"), ("hall", "hall_table"), ("bed", "bed_pills"),
            ("bath", "bath_sink"), ("bath", "bath_towel"), ("lab", "lab_or_lamp"), ("lab", "lab_bench_lamp") })
        {
            yield return Prepare("decoration " + pair.Item2, pair.Item1);
            Check(!V.FindThing(pair.Item2).interactive, "decoration has no gameplay interaction"); AssertThing(pair.Item2, false);
        }
    }
    static void OpenBox(OSChapter03StoryState state)
    { foreach (string flag in new[] { "bed_second_done", "reunion_started", "live_returned", "box_open" }) state.Add(flag); }

    static IEnumerator YardThings()
    {
        foreach (string id in new[] { "yard_bedwindow", "yard_clothes", "yard_tree", "yard_fence", "yard_gate", "yard_seedlings" })
        {
            yield return Prepare("yard observation " + id, "yard", state => state.phase = (int)OSChapter03StoryDirector.Phase.Yard);
            AssertThing(id, false); Thing(id); yield return Drain(); AssertThing(id, true);
            Check(S.yardVisited.SequenceEqual(new[] { id }), "yard objective keeps its independent one-object count");
            if (id == "yard_fence") { Plate("yard-fence-seen"); yield return Roundtrip("yard-fence-seen"); AssertThing(id, true); }
        }
    }

    static IEnumerator LegacySaves()
    {
        yield return Prepare("legacy exact evidence migration", "study");
        var legacy = CaptureSave(); var state = legacy.osProcess.memory.chapter03Story;
        foreach (string flag in new[] { "drawer_seen", "calendar_seen", "medicine_seen", "makeup_seen", "shoes_seen" }) state.Add(flag);
        state.papers.Add("diary_07"); state.papers.Add("diary_05");
        foreach (string key in new[] { "interaction.study.shelf", "interaction.hall.coat", "interaction.lab.cast" })
            legacy.osProcess.memory.triggeredDataIndices.Add(OSChapter03StoryContent.Instance.Node(key));
        state.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 30f));
        state.memoryQueue.Add(new OSChapter03StoryBeat("text", "interaction.kitchen.table") { interactionSource = "thing:kitchen_table" });
        yield return Restore(RemoveVisualFields(legacy), "legacy-exact");
        Check(S.interactionVisualVersion == 1 && S.inspectedThings.Count > 0, "missing legacy fields migrate exactly once");
        AssertThing("study_shelf", true); AssertThing("study_desk", true); AssertThing("study_lamp", false);
        yield return Enter("hall"); AssertThing("hall_coat", true); AssertThing("hall_shoes", false); AssertThing("hall_umbrella", false);
        yield return Enter("kitchen"); AssertThing("kitchen_drawer", true); AssertThing("kitchen_calendar", true);
        AssertThing("kitchen_sugar", true); AssertThing("kitchen_table", true);
        yield return Enter("west"); AssertThing("west_note", true, hidden: true);
        int count = S.inspectedThings.Count; yield return Roundtrip("legacy-migrated-again");
        Check(S.inspectedThings.Count == count && S.inspectedThings.Distinct().Count() == count, "migration is idempotent after another JSON restore");

        yield return Prepare("legacy older stages do not hide new actions", "kitchen");
        legacy = CaptureSave(); state = legacy.osProcess.memory.chapter03Story;
        foreach (string flag in new[] { "machine_seen", "coffee_water", "coffee_beans", "drawer_seen", "store_door_seen" }) state.Add(flag);
        foreach (string key in new[] { "interaction.kitchen.machine.cold", "interaction.kitchen.cup.empty", "interaction.kitchen.screwdriver.before_door", "interaction.clock.1452" })
            legacy.osProcess.memory.triggeredDataIndices.Add(OSChapter03StoryContent.Instance.Node(key));
        state.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 30f) { interactionSource = "thing:kitchen_coffee_machine" });
        yield return Restore(RemoveVisualFields(legacy), "legacy-new-actions");
        AssertThing("kitchen_coffee_machine", false); AssertThing("kitchen_screwdriver", false);
        AssertThing("kitchen_cup", true); AssertThing("kitchen_clock", false);
        S.Add("coffee_brewed"); yield return Settled(); AssertThing("kitchen_cup", false, paper: true);
        yield return Roundtrip("legacy-new-actions-retained");
        AssertThing("kitchen_coffee_machine", false); AssertThing("kitchen_screwdriver", false); AssertThing("kitchen_cup", false, paper: true);
    }

    // Palette evidence is read from the actual uploaded texture. Full illumination
    // isolates object color from authored darkness, Bayer clipping and actor overlap.
    // The temporary light and display switches are restored before returning.
    static void AssertThing(string id, bool dim, bool hidden = false, bool paper = false)
    {
        var thing = V.CurrentRoom.FindThing(id); Check(thing != null, "authored Thing exists: " + id); covered.Add(id);
        Check(thing.hidden == hidden, id + (hidden ? " is hidden" : " remains visible"));
        Check(V.IsThingVisited(id) == dim, id + (dim ? " is complete in this stage" : " is available in this stage"));
        var marks = (IDictionary)Get(V, "paperMarks"); Check(marks.Contains(id) == paper, id + (paper ? " retains its paper overlay" : " has no paper overlay"));
        int resolved = Convert.ToInt32(Call(V, "ThingInk", thing));
        int baseInk = thing.ink == OSHomeInk.Red ? 2 : thing.ink == OSHomeInk.Yellow ? 3 : thing.ink == OSHomeInk.Blue ? 4 : 1;
        Check(resolved == (dim ? DimInk : baseInk), id + " resolves the expected object palette entry");
        int expected = paper ? 2 : thing.style == OSHomeThingStyle.WallMirror && !dim && thing.interactive ? 2 : resolved;
        int x = thing.tile.x * 16, width = thing.Width * 16;
        if (paper) { var mark = marks[id]; x = (int)Get(mark, "left") * 16; width = (int)Get(mark, "width") * 16; }
        int[] counts = null;
        WithPlate(() => {
            var texture = (Texture2D)Get(V, "texture"); var pixels = texture.GetPixels32(); var palette = (Color32[])Get(V, "palette");
            counts = new int[palette.Length];
            for (int y = Mathf.Max(0, thing.tile.y * 16); y < Mathf.Min(160, thing.tile.y * 16 + 16); y++)
                for (int X = Mathf.Max(0, x); X < Mathf.Min(240, x + width); X++)
                {
                    Color32 color = pixels[(159 - y) * 240 + X];
                    for (int i = 0; i < palette.Length; i++) if (color.Equals(palette[i])) counts[i]++;
                }
        });
        if (!hidden) Check(counts[expected] > 0, id + " actual texture contains its expected ink");
        else Check(counts[2] == 0 && counts[DimInk] == 0, id + " actual texture has no remaining paper/object ink");
        observations.Add(new { context, id, room = V.CurrentRoom.id, frame = Time.frameCount, dim, hidden, paper, resolvedInk = resolved,
            renderedInk = expected, pixelCounts = counts, phase = S.phase, clock = S.clock, flags = S.flags.ToArray(), papers = S.papers.ToArray() });
    }
    static void WithPlate(Action inspect)
    {
        var room = V.CurrentRoom;
        var light = new OSHomeLight { id = "qa_interaction_plate", kind = OSHomeLightKind.Strip, strip = new RectInt(0, 0, 240, 160), band = 0f };
        bool lamps = V.AllLightsOn, player = V.PlayerVisible, input = V.InputEnabled;
        object dong = Get(V, "dongMode"); float fade = (float)Get(V, "lightScale");
        room.lights.Add(light); V.SetAllLightsOn(true); V.SetPlayerVisible(false); V.InputEnabled = false;
        Set(V, "dongMode", OSHomeView.DongMode.Hidden); Set(V, "lightScale", 1f);
        try { Call(V, "Paint"); Call(V, "Upload"); inspect(); }
        finally
        {
            room.lights.Remove(light); V.SetAllLightsOn(lamps); V.SetPlayerVisible(player); V.InputEnabled = input;
            Set(V, "dongMode", dong); Set(V, "lightScale", fade); Call(V, "Paint"); Call(V, "Upload");
        }
    }
    static void Plate(string label)
    {
        string path = Path.Combine(OutputDirectory, (++plate).ToString("D2") + "-" + label + "-plate.png");
        WithPlate(() => File.WriteAllBytes(path, ((Texture2D)Get(V, "texture")).EncodeToPNG()));
        screenshots.Add(path);
    }
    static IEnumerator Screenshot(string name)
    {
        yield return Settled(); string path = Path.Combine(OutputDirectory, name); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 100, "natural screenshot written", 10f);
    }
    static IEnumerator Enter(string room)
    {
        Check(V.EnterRoom(room, new Vector2Int(7, 5)), "real Home room entry: " + room);
        yield return Settled(); Check(V.CurrentRoom.id == room, "room entry settled");
    }
    static GameSaveData CaptureSave()
    {
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        return Clone(data.SaveData);
    }
    static IEnumerator Roundtrip(string name)
    {
        var save = CaptureSave(); string json = JsonUtility.ToJson(save, true);
        File.WriteAllText(Path.Combine(OutputDirectory, name + ".json"), json);
        Check(JsonUtility.ToJson(save.osProcess.memory.chapter03Story) == JsonUtility.ToJson(S), "JSON preserves all interaction and queued story state");
        yield return Restore(JsonUtility.FromJson<GameSaveData>(json), name);
    }
    static GameSaveData RemoveVisualFields(GameSaveData save)
    {
        var json = JObject.Parse(JsonUtility.ToJson(save)); var state = (JObject)json["osProcess"]["memory"]["chapter03Story"];
        state.Property("inspectedThings")?.Remove(); state.Property("interactionVisualVersion")?.Remove();
        File.WriteAllText(Path.Combine(OutputDirectory, "legacy-input-" + plate + ".json"), json.ToString(Formatting.Indented));
        return JsonUtility.FromJson<GameSaveData>(json.ToString(Formatting.None));
    }
    static IEnumerator Restore(GameSaveData save, string label)
    {
        string room = save.osProcess.memory.chapter03HomeView.room;
        Messages.NotifyOSHidden(); story.ResetPresentation(); data.ReplaceSaveData(save); home.Suspend();
        yield return OSSessionPersistence.Restore(save);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        if (!memoryWindow.IsOpen) Check(os.OpenMemoryWindow(), "reopens Memory after restore");
        yield return Wait(() => ReferenceEquals(S, save.osProcess.memory.chapter03Story) && story.IsAvailable
            && Ready(home.HomeWindow) && !home.IsRestoringView && V.CurrentRoom?.id == room, "real session restore completes: " + label);
        yield return Settled();
    }
    static void Thing(string id)
    {
        Check(story.IsAvailable && !story.BlocksHomeInput, "production input accepts " + id);
        var thing = V.CurrentRoom.FindThing(id);
        Check(thing != null && !thing.hidden && thing.interactive, "visible interactive target: " + id);
        Call(V, "Activate", thing);
    }
    static Button ModalButton(string name)
    {
        var frame = Get(P, "modalFrame") as RectTransform;
        var button = frame?.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == name);
        Check(button != null && button.IsInteractable() && button.gameObject.activeInHierarchy, "real modal button available: " + name);
        return button;
    }
    static void CloseModal() { Check(ModalReady, "modal accepts close"); ModalButton("Close").onClick.Invoke(); }
    static IEnumerator WaitModal() => Drive(() => ModalReady, "real paper/art/keypad modal is ready", false);
    static void CompleteMemory() { foreach (var unit in memory.TextUnits) if (unit != null) unit.CompleteImmediately(); }
    static void Pump(bool closeModals)
    {
        CompleteMemory(); string id = Messages.DisplayedEntryId;
        if (OSChapter03StoryDirector.IsOwnEntry(id))
        {
            Messages.TryCompleteDisplayedTextImmediately(id); Messages.TryCompleteDisplayedOptionTextImmediately(id);
            if (Messages.IsWaitingForOption)
            {
                var entry = Get(Messages, "activeEntry") as OSCharacterInteractionEntryData;
                Check(entry?.Options != null && entry.Options.Count > 0 && Messages.TrySelectDisplayedOptionImmediately(
                    Messages.DisplayedSequenceId, entry.EntryId, entry.Options[0].OptionId), "real Live option is selected");
            }
            else if (Messages.IsReadingHoldActive)
            {
                var timer = Get(Messages, "autoCloseTween") as Tween; if (timer != null && timer.IsActive()) timer.Complete(true);
            }
        }
        if (closeModals && ModalReady && (Get(P, "modalKind").ToString() == "Paper" || Get(P, "modalKind").ToString() == "Art")) CloseModal();
    }
    static IEnumerator Drain() => Drive(() => Idle, "interaction response finishes through production callbacks", true);
    static IEnumerator Drive(Func<bool> predicate, string label, bool closeModals, float seconds = 40f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        do { Pump(closeModals); yield return null; } while (!predicate() && Time.realtimeSinceStartup < until);
        Check(predicate(), label); yield return Settled();
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static IEnumerator Settled() { Canvas.ForceUpdateCanvases(); yield return null; yield return null; }
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing && !window.IsMinimized;
    static void Focus()
    {
        mouse?.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
        var actor = os?.CursorRelease;
        if (actor == null || !savesIsolated()) return;
        if (!actorFocus.ContainsKey(actor)) actorFocus.Add(actor, (bool)Get(actor, "hasFocus")); Set(actor, "hasFocus", true);
    }
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static string Describe() => "room=" + home?.View?.CurrentRoom?.id + ", available=" + story?.IsAvailable + ", blocked=" + story?.BlocksHomeInput
        + ", queue=" + (story?.State == null ? "null" : string.Join(",", S.memoryQueue.Select(beat => beat.type + ":" + beat.key)));
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Members)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Members)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members).Invoke(target, args);
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
}
