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

// Opt-in Play Mode regression runner. Only cloned saves and temporary storage are
// changed. Runs in the background with real Home events and paper callbacks.
// Existing Live holds are completed through their production completion callback;
// this runner does not verify their natural elapsed duration.
public static class OSChapter03InteractionQueueQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<int> nodes = new List<int>();
    static readonly List<string> liveEntries = new List<string>();
    static readonly string[] evidenceNames = { "shoes", "calendar", "machine", "medicine" };
    static readonly string[] evidenceFlags = { "shoes_seen", "calendar_seen", "machine_seen", "medicine_seen" };
    const string LongGuide = "interaction.west.bed_door.hint.", ShortGuide = "memory.evidence.next.";
    static GameSetup setup;
    static Mouse testMouse;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSWindowUnit memoryWindow;
    static GameSaveData baseline;
    static string context;
    static bool cancel, guidanceOnly, yardReturnAndRainOnly, evidenceRefinementOnly, repeatEvidenceOnly, repeatEvidenceRestoreOnly;
    static OSChapter03StoryState S => story.State;
    static OSHomeView V => home.View;
    static OSChapter03StoryPresentation P => story.Presentation;
    static OSLinkMessageController Messages => os.LinkMessageController;

    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start() => StartRun(false);
    public static string StartGuidance() => StartRun(true);
    public static string StartRandomGuidance() => StartRun(true);
    public static string StartYardReturnAndRain() => StartRun(false, true);
    public static string StartEvidenceRefinement() => StartRun(false, onlyEvidenceRefinement: true);
    public static string StartRepeatEvidence() => StartRun(false, onlyRepeatEvidence: true);
    public static string StartRepeatEvidenceRestore() => StartRun(false, onlyRepeatEvidenceRestore: true);

    static string StartRun(bool onlyGuidance, bool onlyYardReturnAndRain = false, bool onlyEvidenceRefinement = false, bool onlyRepeatEvidence = false, bool onlyRepeatEvidenceRestore = false)
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || SceneLoadManager.Instance == null
            || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameSessionFlow.Instance.IsBusy
            || GameTest.Instance.IsCheckpointJumpRunning) return "Wait for active save/scene operation";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); observations.Clear(); nodes.Clear(); liveEntries.Clear(); cancel = false;
        guidanceOnly = onlyGuidance;
        yardReturnAndRainOnly = onlyYardReturnAndRain;
        evidenceRefinementOnly = onlyEvidenceRefinement;
        repeatEvidenceOnly = onlyRepeatEvidence;
        repeatEvidenceRestoreOnly = onlyRepeatEvidenceRestore;
        os = null; memory = null; home = null; story = null; baseline = null;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03InteractionQueueQA", Guid.NewGuid().ToString("N")));
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
        bool demo = (bool)Get(setup, "isDemo");
        var originalRandom = UnityEngine.Random.state;
        var previousMouse = Mouse.current;
        string[] files = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Set(setup, "isDemo", false); Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03InteractionQueueQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    PrepareBackgroundTick();
                    if (cancel) throw new OperationCanceledException("Interaction queue QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + error;
                    Debug.LogError("[Chapter03 Interaction Queue QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(files.Select(Hash).SequenceEqual(hashes), "original save files remain unchanged");
            Status = "PASS";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (memory != null) memory.DataShown -= OnData;
                if (os != null) Messages.EntryPresentationStarted -= OnLive;
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                if (os != null) Messages.NotifyOSHidden();
                story?.ResetPresentation(); home?.Suspend();
                os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                UnityEngine.Random.state = originalRandom;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || resources != null && resources.enabled != resourcesEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse)
                    throw new InvalidOperationException("Original save, language, mode, resources, background or input settings were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save files changed");
                if (!UnityEngine.Random.state.Equals(originalRandom)) throw new InvalidOperationException("Original random state was not restored");
                checks.Add("cleanup restores original save object, language, options, mode, resources, mouse, random state and file hashes");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, mode = repeatEvidenceRestoreOnly ? "repeat-evidence-restore" : repeatEvidenceOnly ? "repeat-evidence" : evidenceRefinementOnly ? "evidence-refinement" : yardReturnAndRainOnly ? "yard-return-and-rain" : guidanceOnly ? "random-guidance" : "interaction-dedup", context,
                checks, observations, originalFiles = files, originalHashes = hashes,
                finalHashes = files.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Background run: never activates or focuses the Editor. Typing is completed directly. Drain simulates an existing Live countdown ending through its production completion callback; natural four-second timing is NOT verified. The rain probe is muted and advances production TickRain with simulated 0.05-second steps; it does not verify listening quality. Authored Memory queue delays and legacy source-free beats are preserved. Exit Play Mode after this isolated run."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Interaction Queue QA] " + Status + " | " + OutputDirectory);
            os = null; memory = null; home = null; story = null; memoryWindow = null; baseline = null;
            setup = null; testMouse = null;
        }
    }

    static IEnumerator Run()
    {
        context = "checkpoint";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart), "real Chapter 3 opening checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        os = OS_Handle.Instance; memory = os.MemoryWindowController;
        home = memory.GetComponent<OSChapter03HomeController>(); story = os.Chapter03Story;
        memoryWindow = memory.GetComponent<OSWindowUnit>();
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession
            && home?.View != null && story != null, "production Home and story resolve in isolated storage");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        baseline = Clone(GameDataManager.Instance.SaveData);
        memory.DataShown += OnData; Messages.EntryPresentationStarted += OnLive;
        if (repeatEvidenceRestoreOnly)
        {
            yield return RepeatedEvidenceRestore();
            yield break;
        }
        if (repeatEvidenceOnly)
        {
            yield return RepeatedEvidenceRoute();
            yield return SoleRemainingEvidence();
            yield return RepeatedEvidenceRestore();
            yield break;
        }
        if (evidenceRefinementOnly)
        {
            yield return SharedHallEvidence();
            yield return FinalEvidenceCloses();
            yield return MorningVisitOrder(true);
            yield return MorningVisitOrder(false);
            yield return MorningUnspokenSave();
            yield return MorningShownLegacySave();
            yield return MorningPendingLegacySave(false);
            yield return MorningPendingLegacySave(true);
            yield break;
        }
        if (yardReturnAndRainOnly)
        {
            yield return YardReturnAndKeyPrompt();
            yield return AllGuideTextColors();
            RainClipTransitions();
            yield return null;
            yield break;
        }
        if (guidanceOnly)
        {
            yield return RandomDoorQuestions(false);
            yield return RandomDoorQuestions(true);
            yield return RandomSelectionRules();
            yield return RandomEvidenceRoute();
            yield return AllGuideTextColors();
            yield return DeferredMirrorAnswer();
            yield break;
        }
        yield return BurstAndFifo();
        yield return MultiBeat();
        yield return SharedText();
        yield return DoorsAndScope();
        yield return RejectedSideEffects();
        yield return SavedQueues();
        yield return PaperAndLive();
        yield return LegacyQueues();
    }

    static IEnumerator Prepare(string name, string room, Action<OSChapter03StoryState> configure = null)
    {
        context = name;
        Messages.NotifyOSHidden(); story.ResetPresentation();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true))
            if (window.IsOpen) window.CloseSilently();
        home.ResetRuntime(); memory.PrepareRuntime();
        var save = Clone(baseline);
        save.flow.language = LanguageState.zh_CN;
        save.osProcess.memory.currentDataIndex = 8110;
        save.osProcess.memory.chapter03HomeInitialized = true;
        save.osProcess.memory.chapter03HomeView = new OSHomeView.State {
            room = room, me = new Vector2Int(7, 5), dong = new Vector2Int(8, 5), dongHidden = false
        };
        var state = new OSChapter03StoryState { version = 1, phase = (int)OSChapter03StoryDirector.Phase.Explore, homeOpened = true };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8020", "opening_8070", "opening_8080", "opening_8110", "paper_note_01" }) state.Add(flag);
        foreach (string id in new[] { "study", "west", "hall", "kitchen", "east", "store", "bed", "bath", "lab" }) state.Add("room_" + id);
        state.papers.Add("note_01"); configure?.Invoke(state);
        save.osProcess.memory.chapter03Story = state;
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        save.osResume = new OSResumeSaveData();
        GameDataManager.Instance.ReplaceSaveData(save);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f);
        Check(os.OpenMemoryWindow(), "opens the real Memory window");
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && V.CurrentRoom?.id == room
            && V.InputEnabled && memory.CurrentData?.DataIndex == 8110, "minimal exploration fixture is ready");
        CompleteMemory();
        nodes.Clear(); liveEntries.Clear();
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !story.BlocksHomeInput, "starts with no queued interaction");
    }

    static readonly string[] hallEvidence = { "hall_shoes", "hall_coat", "hall_umbrella" };
    const string MorningSummary = "memory.evidence.morning", CompleteEvidence = "memory.evidence.complete", ReturnToDoor = "memory.evidence.return_to_door";
    static string HallDescription(string id) => "interaction." + id.Replace('_', '.');

    static IEnumerator SharedHallEvidence()
    {
        foreach (string first in hallEvidence)
        {
            yield return Prepare("shared hall evidence first: " + first, "hall");
            Thing(first);
            string hint = RandomQuestion(ShortGuide, "thing:" + first, HallDescription(first), "memory.evidence.shoes");
            string[] later = hallEvidence.Where(id => id != first).ToArray();
            var expectedKeys = new List<string> { HallDescription(first), "memory.evidence.shoes", hint };
            var expectedSources = Enumerable.Repeat("thing:" + first, 3).ToList();
            foreach (string id in later)
            {
                Thing(id);
                expectedKeys.AddRange(new[] { HallDescription(id), RandomQuestionKey(ShortGuide) });
                expectedSources.AddRange(Enumerable.Repeat("thing:" + id, 2));
            }
            Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(expectedKeys)
                && S.memoryQueue.Select(Source).SequenceEqual(expectedSources) && S.Has("shoes_seen")
                && Near(S.Progress, .205f) && hint != ShortGuide + "shoes",
                "the first hall object owns one clue summary; every hall object appends its own description and unchecked-evidence hint in FIFO order");
            Reject(() => { foreach (string id in hallEvidence) Thing(id); },
                "100 interleaved clicks cannot duplicate or reroll any pending hall-object response");
            yield return Drive(() => S.memoryQueue.Count == 6 && S.memoryQueue[0].key == "memory.evidence.shoes",
                "the first description finishes while its evidence, hint and other objects remain queued");
            Reject(() => Thing(first), "the first object remains reserved until its whole evidence-and-hint batch finishes");
            yield return Drain("all three distinct hall objects finish in first-click order");
            Check(nodes.SequenceEqual(expectedKeys.Select(Node)) && S.flags.Count(flag => flag == "shoes_seen") == 1
                && !S.Has("doubt_raised"), "actual Memory events preserve one shared evidence summary and a fresh hint for every hall-object interaction");

            int before = nodes.Count;
            var repeatedKeys = new List<string>();
            foreach (string id in hallEvidence)
            {
                Thing(id);
                repeatedKeys.AddRange(new[] { HallDescription(id), RandomQuestionKey(ShortGuide) });
            }
            Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(repeatedKeys)
                && S.memoryQueue.Select(Source).SequenceEqual(hallEvidence.SelectMany(id => Enumerable.Repeat("thing:" + id, 2))),
                "finished hall objects accept fresh clicks with individual descriptions and new unchecked-evidence hints");
            yield return Drain("revisited hall objects finish without repeating the shared evidence summary");
            Check(nodes.Skip(before).SequenceEqual(repeatedKeys.Select(Node))
                && nodes.Count(node => node == Node("memory.evidence.shoes")) == 1 && Near(S.Progress, .205f),
                "revisiting all three objects repeats guidance but never the clue summary or progress increment");
            Mark("shared-hall-evidence-complete");
        }
    }

    static IEnumerator FinalEvidenceCloses()
    {
        foreach (string last in hallEvidence.Concat(new[] { "kitchen_coffee_machine", "kitchen_calendar", "kitchen_sugar" }))
        {
            bool hall = last.StartsWith("hall_", StringComparison.Ordinal);
            yield return Prepare("last evidence: " + last, hall ? "hall" : "kitchen", state => {
                if (hall)
                    foreach (string flag in new[] { "calendar_seen", "machine_seen", "morning_seen", "medicine_seen" }) state.Add(flag);
                else
                {
                    state.Add("shoes_seen");
                    if (last != "kitchen_sugar") state.Add("medicine_seen");
                    if (last != "kitchen_calendar") state.Add("calendar_seen");
                    if (last != "kitchen_coffee_machine") state.Add("machine_seen");
                    if (last == "kitchen_sugar") state.Add("morning_seen");
                }
            });
            string description = hall ? HallDescription(last) : last == "kitchen_coffee_machine"
                ? "interaction.kitchen.machine.cold" : last == "kitchen_calendar" ? "interaction.kitchen.calendar" : "interaction.kitchen.sugar";
            Thing(last);
            GuideQueue("thing:" + last, description);
            Check(Near(S.Progress, .375f) && !S.Has("morning_said"),
                "the final clue preserves its own description while suppressing evidence summaries and random guidance");
            yield return Wait(() => S.Has("deduction_queued"), "the real update queues the final evidence conclusion");
            Check(S.memoryQueue.Select(beat => beat.type + ":" + beat.key).SequenceEqual(new[] {
                    "text:" + description, "text:" + CompleteEvidence, "text:" + ReturnToDoor, "flag:deduction_done" })
                && Source(S.memoryQueue[0]) == "thing:" + last && S.memoryQueue.Skip(1).All(beat => string.IsNullOrEmpty(Source(beat))),
                "the final object's own paragraph is followed directly by complete, return-to-door and the completion flag");
            var complete = S.memoryQueue.Single(beat => beat.key == CompleteEvidence);
            Check(!complete.begun && Near(complete.remaining, .9f), "the untouched complete beat stores the authored 0.9-second interval");
            observations.Add(new { context, label = "final-evidence-queued", last, description, completionInterval = complete.remaining,
                queued = S.memoryQueue.Select(beat => beat.type + ":" + beat.key).ToArray() });
            Reject(() => Thing(last), "pending final-evidence spam cannot duplicate its paragraph or conclusion");
            yield return Drain("final evidence and the return-to-door conclusion finish");
            Check(nodes.SequenceEqual(new[] { Node(description), Node(CompleteEvidence), Node(ReturnToDoor) })
                && S.Has("deduction_done") && Near(S.Progress, .4f) && NoStorageEvidence,
                "actual final-evidence presentation contains only its own paragraph, complete and return-to-door");

            int before = nodes.Count;
            Check(V.EnterRoom("kitchen", new Vector2Int(7, 5)), "revisits the coffee machine after evidence is complete");
            Thing("kitchen_coffee_machine");
            GuideQueue("thing:kitchen_coffee_machine", "interaction.kitchen.machine.cold");
            yield return Drain("post-deduction machine revisit finishes");
            Check(nodes.Skip(before).SequenceEqual(new[] { Node("interaction.kitchen.machine.cold") }) && !S.Has("morning_said"),
                "completed evidence never backfills an omitted morning summary or repeats the conclusion");
            Mark("final-evidence-short-conclusion-complete");
        }
    }

    static IEnumerator MorningVisitOrder(bool calendarFirst)
    {
        yield return Prepare(calendarFirst ? "morning: calendar then machine" : "morning: machine then calendar then machine", "kitchen");
        string first = calendarFirst ? "kitchen_calendar" : "kitchen_coffee_machine";
        string second = calendarFirst ? "kitchen_coffee_machine" : "kitchen_calendar";
        string firstDescription = calendarFirst ? "interaction.kitchen.calendar" : "interaction.kitchen.machine.cold";
        string secondDescription = calendarFirst ? "interaction.kitchen.machine.cold" : "interaction.kitchen.calendar";
        Thing(first);
        string firstHint = RandomQuestion(ShortGuide, "thing:" + first, firstDescription);
        Check(!S.Has("morning_seen") && !S.Has("morning_said"), "one morning object alone neither completes nor summarizes the morning clue");
        yield return Drain("the first morning object and its hint finish");
        Check(nodes.SequenceEqual(new[] { Node(firstDescription), Node(firstHint) }), "the first morning object presents only its own description and hint");

        int beforeSecond = nodes.Count;
        Thing(second);
        string[] descriptions = calendarFirst ? new[] { secondDescription, MorningSummary } : new[] { secondDescription };
        string secondHint = RandomQuestion(ShortGuide, "thing:" + second, descriptions);
        Check(S.Has("morning_seen") && S.Has("morning_said") == calendarFirst && Near(S.Progress, .205f),
            "both objects complete the morning clue, but only a machine visit claims its spoken summary");
        Reject(() => Thing(second), "pending second-object spam preserves its summary decision and random hint");
        yield return Drain("the second morning object finishes with the appropriate summary policy");
        Check(nodes.Skip(beforeSecond).SequenceEqual(descriptions.Concat(new[] { secondHint }).Select(Node)),
            "actual presentation gives the machine its summary and keeps the calendar to its description and hint");
        if (!calendarFirst)
        {
            int beforeReturn = nodes.Count;
            Thing("kitchen_coffee_machine");
            string returnHint = RandomQuestion(ShortGuide, "thing:kitchen_coffee_machine", "interaction.kitchen.machine.cold", MorningSummary);
            Check(S.Has("morning_said"), "returning to the machine claims the previously unspoken morning summary");
            Reject(() => Thing("kitchen_coffee_machine"), "a pending machine return keeps one summary");
            yield return Drain("the returning machine presents its one deferred summary and a new unchecked-evidence hint");
            Check(nodes.Skip(beforeReturn).SequenceEqual(new[] { Node("interaction.kitchen.machine.cold"), Node(MorningSummary), Node(returnHint) }),
                "a previously checked machine adds its deferred summary once and retains repeat guidance");
        }
        int beforeRepeat = nodes.Count;
        Thing("kitchen_calendar"); string calendarHint = RandomQuestionKey(ShortGuide);
        Thing("kitchen_coffee_machine"); string machineHint = RandomQuestionKey(ShortGuide);
        string[] repeatKeys = { "interaction.kitchen.calendar", calendarHint, "interaction.kitchen.machine.cold", machineHint };
        Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(repeatKeys)
            && S.memoryQueue.Select(Source).SequenceEqual(new[] { "thing:kitchen_calendar", "thing:kitchen_calendar", "thing:kitchen_coffee_machine", "thing:kitchen_coffee_machine" }),
            "later calendar and machine visits retain FIFO with their own paragraphs and unchecked-evidence hints");
        yield return Drain("repeated morning objects finish with new hints and without another summary");
        Check(nodes.Skip(beforeRepeat).SequenceEqual(repeatKeys.Select(Node))
            && nodes.Count(node => node == Node(MorningSummary)) == 1,
            "either visit order produces the morning summary exactly once and always at the machine");
        Mark("morning-visit-order-complete");
    }

    static void MorningSeenState(OSChapter03StoryState state)
    {
        foreach (string flag in new[] { "calendar_seen", "machine_seen", "morning_seen" }) state.Add(flag);
    }

    static IEnumerator MorningUnspokenSave()
    {
        yield return Prepare("saved morning clue with summary still unspoken", "kitchen", MorningSeenState);
        yield return Roundtrip("morning-unspoken-before-machine");
        Check(S.Has("morning_seen") && !S.Has("morning_said")
            && !GameDataManager.Instance.TriggeredOSMemoryDataIndices.Contains(Node(MorningSummary)),
            "restored morning_seen alone has neither a spoken flag nor summary history");
        S.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 3f));
        Thing("kitchen_coffee_machine");
        string pendingHint = RandomQuestionKey(ShortGuide);
        Check(S.Has("morning_said") && S.memoryQueue.Skip(1).Select(beat => beat.key).SequenceEqual(new[] { "interaction.kitchen.machine.cold", MorningSummary, pendingHint })
            && S.memoryQueue.Skip(1).All(beat => Source(beat) == "thing:kitchen_coffee_machine"),
            "an unspoken saved clue receives its first summary at the machine");
        yield return Roundtrip("morning-once-pending");
        Reject(() => Thing("kitchen_coffee_machine"), "loading the pending machine batch preserves its once flag and deduplication");
        yield return Drain("the restored machine batch presents its pending summary once");
        Check(nodes.Where(node => node != 8110).SequenceEqual(new[] { Node("interaction.kitchen.machine.cold"), Node(MorningSummary), Node(pendingHint) }),
            "JSON restore presents the pending morning summary and its selected hint exactly once");
        yield return Roundtrip("morning-once-finished");
        int before = nodes.Count; // Restore may legitimately redisplay the current summary node.
        Thing("kitchen_coffee_machine"); string revisitHint = RandomQuestion(ShortGuide, "thing:kitchen_coffee_machine", "interaction.kitchen.machine.cold");
        yield return Drain("a machine revisit after completed-summary restore finishes");
        Check(nodes.Skip(before).SequenceEqual(new[] { Node("interaction.kitchen.machine.cold"), Node(revisitHint) }) && S.Has("morning_said"),
            "the durable once flag prevents summary replay while a fresh click still receives guidance after restore");
        Mark("unspoken-morning-and-json-once-complete");
    }

    static IEnumerator MorningShownLegacySave()
    {
        yield return Prepare("legacy morning summary already shown without once flag", "kitchen", MorningSeenState);
        Check(memory.ShowInteractionData(Node(MorningSummary)), "real Memory records the old save's already-shown morning summary");
        CompleteMemory(); yield return null;
        Check(!S.Has("morning_said") && GameDataManager.Instance.TriggeredOSMemoryDataIndices.Contains(Node(MorningSummary)),
            "the legacy fixture has real summary history and no new once flag");
        yield return Roundtrip("morning-legacy-shown");
        int before = nodes.Count;
        Thing("kitchen_coffee_machine"); string hint = RandomQuestion(ShortGuide, "thing:kitchen_coffee_machine", "interaction.kitchen.machine.cold");
        Check(S.Has("morning_said"), "the machine adopts the once flag from the old save's already-shown summary");
        yield return Drain("machine revisit with legacy summary history finishes");
        Check(nodes.Skip(before).SequenceEqual(new[] { Node("interaction.kitchen.machine.cold"), Node(hint) }),
            "an old save's previously shown morning summary is not repeated and the fresh click still receives guidance");
        Mark("legacy-shown-morning-compatible");
    }

    static IEnumerator MorningPendingLegacySave(bool fullKey)
    {
        yield return Prepare(fullKey ? "legacy pending morning summary with full key" : "legacy pending morning summary with short key", "kitchen", MorningSeenState);
        string savedKey = fullKey ? OSChapter03StoryContent.FullKey(MorningSummary) : MorningSummary;
        S.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 3f));
        S.memoryQueue.Add(new OSChapter03StoryBeat("text", savedKey, delay: .65f));
        yield return Roundtrip(fullKey ? "morning-legacy-pending-full" : "morning-legacy-pending-short");
        Check(!S.Has("morning_said") && S.memoryQueue.Count(beat => beat.key == savedKey) == 1
            && !GameDataManager.Instance.TriggeredOSMemoryDataIndices.Contains(Node(MorningSummary)),
            "the old save retains one unplayed summary and no new once flag");
        Thing("kitchen_coffee_machine");
        string hint = RandomQuestionKey(ShortGuide);
        Check(S.Has("morning_said") && S.memoryQueue.Where(beat => beat.type == "text").Select(beat => beat.key)
                .SequenceEqual(new[] { savedKey, "interaction.kitchen.machine.cold", hint })
            && string.IsNullOrEmpty(Source(S.memoryQueue.First(beat => beat.key == savedKey)))
            && Source(S.memoryQueue.Last()) == "thing:kitchen_coffee_machine",
            "the machine adopts the once flag while preserving the legacy summary, new description and their separate ownership");
        Reject(() => Thing("kitchen_coffee_machine"), "a new machine response cannot duplicate the already-pending legacy summary");
        yield return Drain("the legacy summary and new machine description finish in FIFO order");
        Check(nodes.Where(node => node != 8110).SequenceEqual(new[] { Node(MorningSummary), Node("interaction.kitchen.machine.cold"), Node(hint) }),
            "legacy pending summary plays once in its original position and the machine adds its description and fresh hint");
        Mark("legacy-pending-morning-compatible");
    }

    static string[] MissingEvidence() => evidenceNames.Where((name, i) => !S.Has(evidenceFlags[i])).ToArray();

    static string RandomQuestionKey(string prefix)
    {
        string hint = S.memoryQueue.LastOrDefault()?.key;
        Check(hint != null && hint.StartsWith(prefix, StringComparison.Ordinal)
            && MissingEvidence().Contains(hint.Substring(prefix.Length)),
            "random question names one currently unchecked piece of evidence");
        return hint;
    }

    static string RandomQuestion(string prefix, string source, params string[] descriptions)
    {
        string hint = RandomQuestionKey(prefix);
        GuideQueue(source, descriptions.Concat(new[] { hint }).ToArray());
        return hint;
    }

    static IEnumerator RandomDoorQuestions(bool coffee)
    {
        yield return Prepare(coffee ? "random door guidance with coffee" : "random door guidance without coffee", "west", state => {
            state.Add("shoes_seen");
            foreach (string name in evidenceNames) state.Add("guided_" + name); // Legacy once flags must be ignored.
            if (coffee) state.items.Add("coffee");
        });
        Door("bed_door");
        GuideQueue("door:bed_door", "interaction.west.bed_door.first");
        Check(S.Has("bed_knocked") && !S.Has("doubt_raised") && !S.Has("coffee_knocked"),
            "first knock raises neither doubt nor a question, with or without coffee");
        yield return Drain("first knock finishes with its original sentence only");
        Check(nodes.SequenceEqual(new[] { Node("interaction.west.bed_door.first") }), "first knock actually presents only its original sentence");

        UnityEngine.Random.InitState(coffee ? 102 : 101);
        string branch = coffee ? "interaction.west.bed_door.coffee" : "interaction.west.bed_door.repeat";
        Door("bed_door");
        string second = RandomQuestion(LongGuide, "door:bed_door", branch, "memory.evidence.doubt");
        Reject(() => Door("bed_door"), "second-knock spam retains one response, one doubt and its chosen question");
        yield return Drain("second knock and random long question finish");
        Check(nodes.Skip(1).SequenceEqual(new[] { Node(branch), Node("memory.evidence.doubt"), Node(second) })
            && S.Has("doubt_raised") && S.Has("coffee_knocked") == coffee,
            "second knock presents its coffee branch, doubt and the selected unchecked question in order");
        GuidePlain("memory.evidence.doubt"); GuideRed(second);

        UnityEngine.Random.InitState(coffee ? 202 : 201);
        Door("bed_door");
        string third = RandomQuestion(LongGuide, "door:bed_door", "interaction.west.bed_door.repeat");
        yield return Drain("later knock repeats a random question without repeating doubt");
        Check(nodes.SequenceEqual(new[] { Node("interaction.west.bed_door.first"), Node(branch), Node("memory.evidence.doubt"), Node(second),
                Node("interaction.west.bed_door.repeat"), Node(third) }) && S.items.Contains("coffee") == coffee,
            "three real knocks preserve exact order, only one doubt and the original coffee inventory");
        Mark("random-door-knocks-presented");
        if (!coffee)
        {
            var chosen = new HashSet<string>(); var samples = new List<object>(); bool valid = true;
            for (int seed = 0; seed < 64; seed++)
            {
                UnityEngine.Random.InitState(seed); Door("bed_door");
                string key = S.memoryQueue.LastOrDefault()?.key;
                valid &= S.memoryQueue.Count == 2 && S.memoryQueue[0].key == "interaction.west.bed_door.repeat"
                    && key != null && key.StartsWith(LongGuide, StringComparison.Ordinal)
                    && MissingEvidence().Contains(key.Substring(LongGuide.Length))
                    && S.memoryQueue.All(beat => Source(beat) == "door:bed_door" && !beat.begun);
                chosen.Add(key); samples.Add(new { seed, key });
                // Selection-only samples run in this same frame. The three real
                // door sequences above verify presentation and completion.
                S.memoryQueue.Clear();
            }
            observations.Add(new { context, label = "seeded-door-selection-only", samples, playedBack = false });
            Check(valid && chosen.SetEquals(MissingEvidence().Select(name => LongGuide + name)),
                "64 seeded door samples cover every missing candidate, exclude checked shoes and ignore legacy guided flags");
        }
    }

    static IEnumerator RandomSelectionRules()
    {
        yield return Prepare("repeated short question without doubt", "kitchen", state => {
            foreach (string flag in new[] { "shoes_seen", "calendar_seen", "machine_seen", "morning_seen", "guided_medicine" }) state.Add(flag);
        });
        Call(story, "GuideAfterEvidence"); Call(story, "GuideAfterEvidence");
        GuideQueue(null, ShortGuide + "medicine", ShortGuide + "medicine");
        Check(!S.Has("doubt_raised"), "short guidance works before doubt and ignores a legacy once flag");
        yield return Drain("the sole missing-evidence question can be presented twice");
        Check(nodes.SequenceEqual(new[] { Node(ShortGuide + "medicine"), Node(ShortGuide + "medicine") }),
            "the same unchecked target remains eligible for later short questions");
        S.Add("medicine_seen");
        Call(story, "GuideAfterEvidence");
        Check(S.memoryQueue.Count == 0 && (int)Call(story, "RandomOpenEvidence") == -1,
            "completed evidence has no random candidate and queues no extra hint");
    }

    static readonly string[] evidenceObjects = {
        "hall_shoes", "hall_coat", "hall_umbrella", "kitchen_calendar", "kitchen_coffee_machine", "kitchen_sugar"
    };
    static int EvidenceIndex(string id) => id.StartsWith("hall_", StringComparison.Ordinal) ? 0
        : id == "kitchen_calendar" ? 1 : id == "kitchen_coffee_machine" ? 2 : 3;
    static string EvidenceDescription(string id) => id.StartsWith("hall_", StringComparison.Ordinal) ? HallDescription(id)
        : id == "kitchen_coffee_machine" ? "interaction.kitchen.machine.cold"
        : id == "kitchen_calendar" ? "interaction.kitchen.calendar" : "interaction.kitchen.sugar";
    static void EnterEvidenceRoom(string id)
    {
        string room = id.StartsWith("hall_", StringComparison.Ordinal) ? "hall" : "kitchen";
        if (V.CurrentRoom?.id != room) Check(V.EnterRoom(room, new Vector2Int(7, 5)), "enters the evidence object's room: " + id);
    }

    static IEnumerator RepeatedEvidenceRoute()
    {
        yield return Prepare("repeat evidence after two and three inspected objects", "hall");
        Thing("hall_shoes");
        RandomQuestion(ShortGuide, "thing:hall_shoes", "interaction.hall.shoes", "memory.evidence.shoes");
        yield return Drain("first shoes evidence and its initial hint finish");
        EnterEvidenceRoom("kitchen_calendar");
        Thing("kitchen_calendar");
        RandomQuestion(ShortGuide, "thing:kitchen_calendar", "interaction.kitchen.calendar");
        yield return Drain("second inspected object and its initial hint finish");
        Check(MissingEvidence().SequenceEqual(new[] { "machine", "medicine" }), "two inspected objects leave the machine and medicine as candidates");

        // Selection-only samples use real frontend clicks without advancing a frame.
        // The following batches also verify actual Memory presentation and completion.
        var choices = new HashSet<string>(); bool valid = true;
        string flagsBefore = JsonConvert.SerializeObject(S.flags);
        bool sfxPausedBefore = (bool)Get(story, "sfxPaused");
        Set(story, "sfxPaused", true);
        try
        {
            for (int seed = 0; seed < 64; seed++)
            {
                UnityEngine.Random.InitState(seed); Thing("kitchen_calendar");
                string hint = S.memoryQueue.LastOrDefault()?.key;
                valid &= S.memoryQueue.Count == 2 && S.memoryQueue[0].key == "interaction.kitchen.calendar"
                    && hint != null && hint.StartsWith(ShortGuide, StringComparison.Ordinal)
                    && MissingEvidence().Contains(hint.Substring(ShortGuide.Length))
                    && S.memoryQueue.All(beat => !beat.begun && Source(beat) == "thing:kitchen_calendar");
                choices.Add(hint);
                S.memoryQueue.Clear();
            }
        }
        finally { Set(story, "sfxPaused", sfxPausedBefore); }
        Check(valid && choices.SetEquals(new[] { ShortGuide + "machine", ShortGuide + "medicine" })
            && JsonConvert.SerializeObject(S.flags) == flagsBefore,
            "64 repeat-calendar samples choose both remaining targets, never checked evidence, and do not change clue flags");

        int beforeRepeat = nodes.Count;
        var repeatedKeys = new List<string>(); var repeatedSources = new List<string>();
        foreach (string id in new[] { "kitchen_calendar", "hall_shoes", "hall_coat", "hall_umbrella" })
        {
            EnterEvidenceRoom(id); Thing(id);
            repeatedKeys.AddRange(new[] { EvidenceDescription(id), RandomQuestionKey(ShortGuide) });
            repeatedSources.AddRange(Enumerable.Repeat("thing:" + id, 2));
            Reject(() => Thing(id), "pending repeat keeps exactly its chosen hint: " + id);
        }
        Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(repeatedKeys)
            && S.memoryQueue.Select(Source).SequenceEqual(repeatedSources),
            "the repeated calendar and all three shared hall objects own one description and one hint each");
        yield return Drain("known evidence presents its new hints after two inspected objects");
        Check(nodes.Skip(beforeRepeat).SequenceEqual(repeatedKeys.Select(Node))
            && nodes.Count(node => node == Node("memory.evidence.shoes")) == 1,
            "real repeat interactions present every selected hint without replaying the first shoes summary");

        EnterEvidenceRoom("kitchen_coffee_machine"); Thing("kitchen_coffee_machine");
        Check(RandomQuestion(ShortGuide, "thing:kitchen_coffee_machine", "interaction.kitchen.machine.cold", MorningSummary)
            == ShortGuide + "medicine", "the third inspected object must point to the sole remaining sugar jar");
        yield return Drain("the machine's first morning summary and sole-target hint finish");
        foreach (string id in new[] { "kitchen_calendar", "kitchen_coffee_machine" })
        {
            int before = nodes.Count; Thing(id);
            string hint = RandomQuestion(ShortGuide, "thing:" + id, EvidenceDescription(id));
            Check(hint == ShortGuide + "medicine", "repeated evidence after two complete clue groups still points to medicine");
            yield return Drain("known evidence remains useful with only one target left: " + id);
            Check(nodes.Skip(before).SequenceEqual(new[] { Node(EvidenceDescription(id)), Node(hint) }),
                "a repeated object presents only its description and the sole remaining hint");
        }
        Check(nodes.Count(node => node == Node(MorningSummary)) == 1 && nodes.Count(node => node == Node("memory.evidence.shoes")) == 1,
            "first shoes and morning summaries each remain one-shot across all revisits");

        Thing("kitchen_sugar"); GuideQueue("thing:kitchen_sugar", "interaction.kitchen.sugar");
        yield return Drain("last evidence runs the complete and return-to-door sequence once");
        Check(S.Has("deduction_done") && nodes.Count(node => node == Node(CompleteEvidence)) == 1
            && nodes.Count(node => node == Node(ReturnToDoor)) == 1,
            "the actual completion sequence is reached exactly once");
        int afterComplete = nodes.Count;
        foreach (string id in evidenceObjects) { EnterEvidenceRoom(id); Thing(id); }
        Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(evidenceObjects.Select(EvidenceDescription)),
            "once all evidence is complete, every revisited object queues only its own description");
        yield return Drain("all known evidence remains quiet about guidance after completion");
        Check(nodes.Skip(afterComplete).SequenceEqual(evidenceObjects.Select(id => Node(EvidenceDescription(id))))
            && nodes.Count(node => node == Node(CompleteEvidence)) == 1 && nodes.Count(node => node == Node(ReturnToDoor)) == 1
            && nodes.Count(node => node == Node(MorningSummary)) == 1 && nodes.Count(node => node == Node("memory.evidence.shoes")) == 1,
            "post-completion revisits replay neither random hints, first summaries nor the completion sequence");
        Mark("repeat-evidence-route-complete");
    }

    static IEnumerator SoleRemainingEvidence()
    {
        for (int missing = 0; missing < evidenceFlags.Length; missing++)
        {
            int missingIndex = missing;
            yield return Prepare("sole remaining evidence: " + evidenceNames[missingIndex], "kitchen", state => {
                for (int i = 0; i < evidenceFlags.Length; i++) if (i != missingIndex) state.Add(evidenceFlags[i]);
                if (state.Has("calendar_seen") && state.Has("machine_seen"))
                { state.Add("morning_seen"); state.Add("morning_said"); }
            });
            var expectedKeys = new List<string>();
            foreach (string id in evidenceObjects.Where(id => EvidenceIndex(id) != missingIndex))
            {
                EnterEvidenceRoom(id); Thing(id);
                string hint = RandomQuestionKey(ShortGuide);
                Check(hint == ShortGuide + evidenceNames[missingIndex], "every known evidence object chooses the sole unchecked target: " + id);
                expectedKeys.AddRange(new[] { EvidenceDescription(id), hint });
                Reject(() => Thing(id), "a pending sole-target hint does not duplicate on rapid repeat: " + id);
            }
            Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(expectedKeys)
                && !S.memoryQueue.Any(beat => beat.key == MorningSummary || beat.key == "memory.evidence.shoes"),
                "three already-inspected evidence targets add repeat guidance without new first-time summaries");
            yield return Drain("sole-target repeat hints are actually presented");
            Check(nodes.SequenceEqual(expectedKeys.Select(Node)) && !S.Has("deduction_queued"),
                "one remaining target stays pending and every repeat hint is presented in FIFO order");
        }
    }

    static IEnumerator RepeatedEvidenceRestore()
    {
        yield return Prepare("repeat guidance survives save and view rebuild without replay", "kitchen", state => {
            state.Add("shoes_seen"); state.Add("calendar_seen");
        });
        S.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 3f));
        Thing("kitchen_calendar");
        string selected = RandomQuestionKey(ShortGuide);
        // JsonUtility normalizes null string fields on source-free delay beats to
        // empty strings. Compare serialized identity, not null versus empty.
        Func<OSChapter03StoryBeat, string> identity = beat => beat.type + ":" + beat.key + ":" + Source(beat);
        string[] queued = S.memoryQueue.Select(identity).ToArray();
        yield return Roundtrip("repeat-evidence-pending-hint");
        UnityEngine.Random.InitState(9999);
        Check(S.memoryQueue.Select(identity).SequenceEqual(queued)
            && S.memoryQueue.Count(beat => beat.key == selected) == 1,
            "restoring a repeated interaction preserves its chosen hint without adding or rerolling guidance");
        story.RebuildHomeState();
        Check(S.memoryQueue.Select(identity).SequenceEqual(queued), "rebuilding the Home view does not append evidence guidance");
        var randomBefore = UnityEngine.Random.state;
        bool oldRebinding = (bool)Get(story, "rebinding");
        Set(story, "rebinding", true);
        try { Call(story, "GuideAfterEvidence"); }
        finally { Set(story, "rebinding", oldRebinding); }
        Check(S.memoryQueue.Select(identity).SequenceEqual(queued) && UnityEngine.Random.state.Equals(randomBefore),
            "the restore guard neither enqueues a hint nor consumes a random choice");
        Reject(() => Thing("kitchen_calendar"), "a restored pending repeat cannot duplicate or reroll its hint");
        yield return Drain("the saved repeated description and selected hint finish once");
        Check(nodes.Where(node => node != 8110).SequenceEqual(new[] { Node("interaction.kitchen.calendar"), Node(selected) }),
            "only the saved repeated description and its original selected hint are presented");
        yield return Roundtrip("repeat-evidence-empty-queue");
        story.RebuildHomeState();
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "restoring completed interactions creates no catch-up guidance");
        Thing("kitchen_calendar");
        RandomQuestion(ShortGuide, "thing:kitchen_calendar", "interaction.kitchen.calendar");
        yield return Drain("a new click after restore can request a fresh unchecked-evidence hint");
        Mark("repeat-evidence-restore-complete");
    }

    static IEnumerator RandomEvidenceRoute()
    {
        yield return Prepare("random evidence route without storage", "hall", state => {
            foreach (string name in evidenceNames) state.Add("guided_" + name);
        });
        Check(Near(S.Progress, .12f) && !DoorSlitPulses && NoStorageEvidence,
            "fresh evidence starts at 0.12 with no storage, diary or answer prerequisites");
        S.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 3f));
        UnityEngine.Random.InitState(31); Thing("hall_shoes");
        string shoesHint = S.memoryQueue.Last().key;
        Check(S.memoryQueue.Skip(1).Select(beat => beat.key).SequenceEqual(new[] { "interaction.hall.shoes", "memory.evidence.shoes", shoesHint })
            && shoesHint.StartsWith(ShortGuide, StringComparison.Ordinal) && MissingEvidence().Contains(shoesHint.Substring(ShortGuide.Length))
            && S.memoryQueue.Skip(1).All(beat => Source(beat) == "thing:hall_shoes"),
            "a fresh shoes click owns both descriptions and one deployed random short hint");
        Reject(() => Thing("hall_shoes"), "shoe spam cannot reroll its pending random question");
        yield return Roundtrip("random-guidance-choice");
        UnityEngine.Random.InitState(9999);
        Check(S.memoryQueue.Count(beat => beat.key == shoesHint) == 1 && S.memoryQueue.Last().key == shoesHint,
            "JSON restore keeps the exact selected hint even after the random seed changes");
        Reject(() => Thing("hall_shoes"), "restored shoe spam cannot add or reroll its question");
        yield return Drain("restored shoes evidence and chosen short hint finish");
        // Restoring the Memory window may re-emit its saved current node 8110.
        // That rebuilt display is not a replay of the pending interaction batch.
        Check(nodes.Where(node => node != 8110).SequenceEqual(new[] { Node("interaction.hall.shoes"), Node("memory.evidence.shoes"), Node(shoesHint) })
            && Near(S.Progress, .205f) && !S.Has("doubt_raised"),
            "shoes trigger their short hint before doubt and advance one evidence group to 0.205");

        Check(V.EnterRoom("kitchen", new Vector2Int(7, 5)), "enters the kitchen for the remaining three evidence objects");
        Thing("kitchen_calendar");
        string calendarHint = RandomQuestion(ShortGuide, "thing:kitchen_calendar", "interaction.kitchen.calendar");
        yield return Drain("calendar and its random short hint finish");
        Check(Near(S.Progress, .205f) && !S.Has("morning_seen"), "calendar alone does not complete the morning evidence group");
        int beforeRevisit = nodes.Count;
        Thing("kitchen_calendar"); string calendarRevisitHint = RandomQuestion(ShortGuide, "thing:kitchen_calendar", "interaction.kitchen.calendar");
        yield return Drain("known calendar repeats its description and supplies a new unchecked-evidence hint");
        Check(nodes.Skip(beforeRevisit).SequenceEqual(new[] { Node("interaction.kitchen.calendar"), Node(calendarRevisitHint) }),
            "new and already-known evidence objects use the same short-hint path");

        Thing("kitchen_coffee_machine");
        string machineHint = RandomQuestion(ShortGuide, "thing:kitchen_coffee_machine", "interaction.kitchen.machine.cold", "memory.evidence.morning");
        Check(machineHint == ShortGuide + "medicine", "the only remaining unchecked object is the sugar jar");
        yield return Drain("machine completes morning evidence and points to the sugar jar");
        Check(S.Has("morning_seen") && Near(S.Progress, .29f), "calendar and machine together advance the second evidence group to 0.29");

        int beforeSugar = nodes.Count;
        Thing("kitchen_sugar"); GuideQueue("thing:kitchen_sugar", "interaction.kitchen.sugar");
        Check(Near(S.Progress, .375f) && NoStorageEvidence && !S.Has("doubt_raised"),
            "three evidence groups reach 0.375 immediately without storage or doubt and without another hint");
        yield return Drain("real automatic deduction completes after the final evidence object");
        Check(nodes.Skip(beforeSugar).SequenceEqual(new[] { Node("interaction.kitchen.sugar"), Node("memory.evidence.complete"), Node("memory.evidence.return_to_door") })
            && S.Has("deduction_done") && Near(S.Progress, .4f) && NoStorageEvidence && DoorSlitPulses,
            "three groups alone produce completion then return-to-door, progress 0.4 and the bed-door slit pulse");
        GuideRed("memory.evidence.return_to_door");
        Check(V.EnterRoom("west", new Vector2Int(7, 5)), "returns to the locked bedroom door");
        Door("bed_door");
        Check(S.Has("final_knock") && story.CurrentPhase == OSChapter03StoryDirector.Phase.FinalKnock,
            "the real final knock is available without makeup, diary 08 or the knows answer");
        yield return Wait(() => !DoorSlitPulses, "final knock releases the bed-door slit pulse", 3f);
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Yard && Idle,
            "real final-knock reasoning and yard arrival finish without storage", 70f, finishLiveHold: true);
        Check(V.CurrentRoom?.id == "yard" && NoStorageEvidence && S.yardVisited.Count == 0
            && !nodes.Contains(Node("interaction.store.makeup"))
            && liveEntries.All(id => !id.StartsWith(OSChapter03StoryDirector.LiveId("live.store.knows_"), StringComparison.Ordinal)),
            "the real exploration route reaches the yard before any storage evidence or knows conversation");
        observations.Add(new { context, label = "random-evidence-choices", shoesHint, calendarHint, machineHint });
        Mark("three-evidence-groups-reach-yard-without-storage");
    }

    static bool NoStorageEvidence => !S.Has("makeup_seen") && !S.papers.Contains("diary_08")
        && !S.Has("knows_reply_done") && S.knowAnswer == 0 && !S.Has("store_open");

    static IEnumerator DeferredMirrorAnswer()
    {
        yield return Prepare("deferred mirror answer before playback", "bath");
        const string lead = "live.study.unfinished_page", yes = "live.breakdown.04_yes", no = "live.breakdown.04_no";
        S.liveQueue.Add(new OSChapter03StoryBeat("live", lead));
        var pending = new OSChapter03StoryBeat("live", no);
        S.liveQueue.Add(pending);
        yield return Drive(() => Messages.DisplayedEntryId == OSChapter03StoryDirector.LiveId(lead) && Messages.IsReadingHoldActive,
            "a real preceding Live hold keeps the unresolved mirror reply pending");
        Check(!pending.begun && pending.key == no && S.knowAnswer == 0, "mirror reply has not started while its answer is still unresolved");
        S.knowAnswer = 1;
        yield return Drive(() => liveEntries.Contains(OSChapter03StoryDirector.LiveId(yes)),
            "pending mirror reply resolves to the later yes answer when playback starts", finishLiveHold: true);
        Check(pending.key == yes && !liveEntries.Contains(OSChapter03StoryDirector.LiveId(no)),
            "the queued default no is replaced before presentation and is never shown");
        yield return Drain("deferred yes reply finishes");

        yield return Prepare("already-started mirror reply compatibility", "bath");
        S.knowAnswer = 1;
        S.liveQueue.Add(new OSChapter03StoryBeat("live", no) { begun = true });
        yield return Roundtrip("begun-mirror-reply");
        yield return Drive(() => liveEntries.Contains(OSChapter03StoryDirector.LiveId(no)),
            "restored already-started no reply preserves its saved branch", finishLiveHold: true);
        Check(!liveEntries.Contains(OSChapter03StoryDirector.LiveId(yes)), "restoring a begun reply does not rewrite previously presented content");
        yield return Drain("restored begun no reply finishes");
        Mark("deferred-mirror-answer-and-begun-compatibility");
    }

    static bool DoorSlitPulses => (Get(V, "pulsingLights") as HashSet<string>)?.Contains("west_bed_slit") == true;
    static void GuideQueue(string source, params string[] keys)
    {
        Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(keys) && S.liveQueue.Count == 0,
            "queued guide paragraphs preserve exact authored order: " + string.Join(" -> ", keys));
        Check(S.memoryQueue.All(beat => string.IsNullOrEmpty(source) ? string.IsNullOrEmpty(Source(beat)) : Source(beat) == source),
            "all guide paragraphs retain their originating interaction source");
    }
    static void GuideRed(string key)
    {
        var unit = memory.TextUnits.LastOrDefault(item => item != null && item.DataIndex == Node(key));
        Check(unit?.Label != null && unit.BoundData != null, "real Memory has the rendered guide: " + key);
        string expected = OSPalette.TintRichText(unit.BoundData.Text);
        string opening = "<color=" + OSPalette.RedHex + ">";
        int start = expected.IndexOf(opening, StringComparison.Ordinal);
        int end = start < 0 ? -1 : expected.IndexOf("</color>", start, StringComparison.Ordinal);
        Check(unit.Label.text == expected && start >= 0 && end > start + opening.Length,
            "real Memory preserves the authored red span without a whole-line wrapper: " + key);
        start += opening.Length;
        unit.Label.ForceMeshUpdate(true, true);
        int inside = 0, outside = 0, wrongInside = 0, wrongOutside = 0;
        for (int i = 0; i < unit.Label.textInfo.characterCount; i++)
        {
            var character = unit.Label.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            bool marked = character.index >= start && character.index < end;
            bool red = IsGuideRed(character.color);
            if (marked) { inside++; if (!red) wrongInside++; }
            else { outside++; if (red) wrongOutside++; }
        }
        Check(inside > 0 && wrongInside == 0 && wrongOutside == 0
            && (key == "memory.evidence.return_to_door" ? outside == 0 : outside > 0),
            "actual TMP characters use OS red only within the intended guiding span: " + key);
        observations.Add(new { context, label = "rendered-guide-color", key, inside, outside, wrongInside, wrongOutside });
    }

    static bool IsGuideRed(Color32 color) => color.r == OSPalette.Red.r && color.g == OSPalette.Red.g && color.b == OSPalette.Red.b;

    static void GuidePlain(string key)
    {
        var unit = memory.TextUnits.LastOrDefault(item => item != null && item.DataIndex == Node(key));
        Check(unit?.Label != null && unit.BoundData != null
            && unit.Label.text == OSPalette.TintRichText(unit.BoundData.Text), "ordinary Memory text has no added guide wrapper: " + key);
        unit.Label.ForceMeshUpdate(true, true);
        int visible = 0, red = 0;
        for (int i = 0; i < unit.Label.textInfo.characterCount; i++)
        {
            var character = unit.Label.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            visible++; if (IsGuideRed(character.color)) red++;
        }
        Check(visible > 0 && red == 0, "actual ordinary TMP characters remain outside OS red: " + key);
        observations.Add(new { context, label = "rendered-plain-color", key, visible, red });
    }

    static IEnumerator YardReturnAndKeyPrompt()
    {
        yield return Prepare("yard return and spare-key prompt", "yard", state => {
            state.phase = (int)OSChapter03StoryDirector.Phase.Yard;
            foreach (string flag in new[] { "room_yard", "deduction_done", "final_knock", "store_open", "makeup_seen" }) state.Add(flag);
            state.yardVisited.AddRange(new[] { "yard_gate", "yard_tree", "yard_clothes", "yard_seedlings", "yard_fence" });
        });
        var sequence = JObject.Parse(File.ReadAllText(Path.Combine("Tools", "Chapter03Story", "runtime.zh-CN.json")))["sequences"]?[9];
        string keyPrompt = "memory.yard.return.key";
        Check(sequence != null && (string)sequence["id"] == "yard_center"
            && (string)sequence["thenReturnMemoryKey"] == OSChapter03StoryContent.FullKey(keyPrompt),
            "authored yard sequence 9 ends with the spare-key Memory prompt");
        Check(sequence["keys"].Values<string>().SequenceEqual(new[] {
                "os.chapter03.memory.yard.center.rain", "os.chapter03.memory.yard.center.scolding", "os.chapter03.memory.yard.center.footprints" })
            && sequence["thenDesktopThoughtKeys"].Values<string>().SequenceEqual(Enumerable.Range(1, 5).Select(i => "os.chapter03.ui.resolve." + i.ToString("00"))),
            "yard sequence keeps its three walking paragraphs and all five return-home thoughts");
        var night = os.GetComponent<OSChapter02NightfallPresentation>();
        yield return Wait(() => night != null && (bool)Get(night, "chapter03Rain") && (bool)Get(night, "chapter03Outdoors")
            && (bool)Get(night, "chapter03HomeOpened"), "real story director selects opened-home outdoor rain in the yard");
        Check(S.yardVisited.Count == 5 && !S.Has("yard_returned") && !S.items.Contains("key")
            && !S.Has("yard_zoom") && !V.Zoomed, "five inspected yard objects do not prematurely zoom, return or grant the key");
        V.Teleport(new Vector2Int(2, 1));
        GameSaveData closeSave = null, returnedSave = null;
        int keyNode = Node(keyPrompt), keyShows = 0;
        bool keyShownAfterReturn = true;
        Action<OSMemoryReadData> observeKey = data => {
            if (data == null || data.DataIndex != keyNode) return;
            keyShows++;
            keyShownAfterReturn &= S.Has("yard_returned") && story.CurrentPhase == OSChapter03StoryDirector.Phase.KeyReady
                && V.CurrentRoom?.id == "hall" && S.liveQueue.Count == 0;
            observations.Add(new { context, label = "key-prompt-presentation", node = keyNode,
                returned = S.Has("yard_returned"), phase = story.CurrentPhase.ToString(), room = V.CurrentRoom?.id,
                liveQueueCount = S.liveQueue.Count });
        };
        memory.DataShown += observeKey;
        try
        {
            Thing("yard_bedwindow");
            var tail = S.memoryQueue.Skip(S.memoryQueue.Count - 4).ToArray();
            Check(S.yardVisited.Count == 6 && S.Has("yard_all_seen") && story.CurrentPhase == OSChapter03StoryDirector.Phase.YardReturn,
                "sixth real yard interaction begins the automatic return");
            Check(S.memoryQueue[0].type == "effect" && S.memoryQueue[0].key == "zoom_in"
                && S.memoryQueue[1].type == "text" && S.memoryQueue[1].key == "interaction.yard.window",
                "the last yard inspection queues zoom-in before its description");
            Check(tail.Length == 4 && tail[0].type == "effect" && tail[0].key == "zoom_out"
                && tail[1].type == "flag" && tail[1].key == "yard_returned"
                && tail[2].type == "phase" && tail[2].number == (int)OSChapter03StoryDirector.Phase.KeyReady
                && tail[3].type == "text" && tail[3].key == keyPrompt,
                "durable return queue zooms out before the return flag, KeyReady phase and key prompt");
            Check(S.memoryQueue.Where(beat => beat.type == "effect").Select(beat => beat.key)
                    .SequenceEqual(new[] { "zoom_in", "resolve", "zoom_out" })
                && S.memoryQueue.All(beat => beat.type != "live") && S.liveQueue.Count == 0,
                "yard return contains the close-up, thoughts and zoom-out with no Live scolding beat");
            string[] expectedText = new[] { "os.chapter03.interaction.yard.window" }
                .Concat(sequence["keys"].Values<string>()).Concat(new[] { OSChapter03StoryContent.FullKey(keyPrompt) }).ToArray();
            Check(S.memoryQueue.Where(beat => beat.type == "text").Select(beat => OSChapter03StoryContent.FullKey(beat.key)).SequenceEqual(expectedText)
                && S.memoryQueue.All(beat => Source(beat) == "thing:yard_bedwindow"),
                "real return queue matches the full authored paragraph order and keeps its interaction source");
            yield return Drive(() => V.CurrentRoom?.id == "yard" && S.Has("yard_zoom")
                && V.Zoomed && (float)Get(V, "zoomBlink") < 0f,
                "the last yard inspection reaches its stable close-up");
            CheckZoom(true, "yard close-up shows exactly one quarter of the map at two times scale");
            closeSave = YardSnapshot("yard-zoom-pending");
            yield return Screenshot("yard-zoom-2x");
            yield return Drive(() => V.CurrentRoom?.id == "hall" && Get(P, "thoughtText") is TMPro.TMP_Text text
                && (int)Get(P, "thoughtIndex") == 2 && text.alpha > .95f
                && text.maxVisibleCharacters >= text.textInfo.characterCount,
                "return-home white strip reaches the fully shown middle thought", 70f);
            CheckThoughtStrip(sequence["thenDesktopThoughtKeys"].Values<string>().ToArray());
            yield return Screenshot("hall-white-strip-middle");
            yield return Drive(() => Idle && S.Has("yard_returned") && S.Has("yard_zoom_done") && !V.Zoomed
                && story.CurrentPhase == OSChapter03StoryDirector.Phase.KeyReady,
                "walking, hall return, all thoughts, zoom-out and key prompt finish", 70f);
            Check(nodes.SequenceEqual(expectedText.Select(Node)) && keyShows == 1 && keyShownAfterReturn,
                "real Memory events show the key prompt once only after the return flag and KeyReady phase");
            Check(V.CurrentRoom?.id == "hall" && S.yardFootprints.Count >= 5 && !V.IsWalking,
                "completed automatic return preserves its real yard walk and finishes in the hall");
            Check(liveEntries.Count == 0 && S.liveQueue.Count == 0,
                "the complete return route presents no Live entry");
            CheckZoom(false, "completed return restores the entire map after the white strip closes");
            Check(Get(P, "thoughtRoot") as RectTransform == null, "the completed monologue removes its white strip");
            returnedSave = YardSnapshot("yard-zoom-done");
            GuideRed(keyPrompt);
            Check((bool)Get(night, "chapter03Rain") && !(bool)Get(night, "chapter03Outdoors")
                && (bool)Get(night, "chapter03HomeOpened"), "real story director restores indoor rain after returning to the hall");
            Mark("yard-return-and-key-prompt-complete");
        }
        finally { memory.DataShown -= observeKey; }
        Check(V.EnterRoom("store", new Vector2Int(7, 4)), "enters storage after the key guide");
        Thing("store_key");
        yield return Drain("guided spare-key pickup finishes");
        Check(S.Has("key_taken") && S.items.Contains("key"), "the guided spare key is actually available after the completed yard return");
        yield return Restore(closeSave);
        Check(S.Has("yard_zoom") && !S.Has("yard_zoom_done") && V.CurrentRoom?.id == "yard",
            "an in-progress return restores its durable close-up flags and yard room");
        CheckZoom(true, "restoring yard_zoom immediately restores the two-times crop");
        yield return Restore(returnedSave);
        Check(S.Has("yard_zoom") && S.Has("yard_zoom_done") && S.Has("yard_returned") && V.CurrentRoom?.id == "hall",
            "a completed return retains both zoom flags and the hall checkpoint");
        CheckZoom(false, "yard_zoom_done wins over yard_zoom after a JSON save restoration");
        Mark("yard-zoom-save-restoration-complete");
    }

    static void CheckZoom(bool close, string label)
    {
        var image = Get(V, "image") as UnityEngine.UI.RawImage;
        var rect = image != null ? image.uvRect : default;
        float size = close ? .5f : 1f;
        Check(image != null && V.Zoomed == close && Near(rect.width, size) && Near(rect.height, size)
            && rect.xMin >= 0f && rect.yMin >= 0f && rect.xMax <= 1f && rect.yMax <= 1f, label);
        observations.Add(new { context, label = "home-map-crop", close, rect.x, rect.y, rect.width, rect.height,
            room = V.CurrentRoom?.id, player = V.PlayerTile });
    }

    static void CheckThoughtStrip(string[] thoughtKeys)
    {
        var root = Get(P, "thoughtRoot") as RectTransform;
        var strip = Get(P, "thoughtStrip") as UnityEngine.UI.Image;
        var text = Get(P, "thoughtText") as TMPro.TMP_Text;
        var group = Get(P, "thoughtGroup") as CanvasGroup;
        var lines = Get(P, "thoughtLines") as string[];
        var map = V.transform as RectTransform;
        Check(root != null && root.parent == V.transform && root.name == "Chapter03ThoughtStrip"
            && strip != null && text != null && group != null && !group.blocksRaycasts && !group.interactable,
            "the return monologue uses a non-interactive white strip parented to the Home map");
        Check(V.Zoomed && S.Has("yard_zoom") && !S.Has("yard_zoom_done") && !S.Has("yard_returned")
            && lines != null && lines.SequenceEqual(thoughtKeys.Select(OSChapter03StoryContent.Instance.Text)),
            "all five localized thoughts run before zoom-out and the return checkpoint");
        Check(Near(root.anchoredPosition.x, 0f) && Near(root.anchoredPosition.y, 0f)
            && Near(root.sizeDelta.y, 26f) && Mathf.Abs(root.rect.width - map.rect.width) <= 1f
            && Mathf.Abs(strip.rectTransform.rect.width - root.rect.width) <= 1f
            && strip.color.r > .8f && strip.color.g > .8f && strip.color.b > .8f
            && Near(text.color.r, 0f) && Near(text.color.g, 0f) && Near(text.color.b, 0f)
            && text.text == OSPalette.TintRichText(lines[2]),
            "the middle thought is black text on the fully opened light strip across the map centre");
        CheckZoom(true, "the hall monologue retains the two-times crop until every thought finishes");
        observations.Add(new { context, label = "return-home-thought-strip", index = (int)Get(P, "thoughtIndex"),
            text = text.text, width = strip.rectTransform.rect.width, height = root.rect.height,
            alpha = text.alpha, visibleCharacters = text.maxVisibleCharacters });
    }

    static GameSaveData YardSnapshot(string name)
    {
        home.CaptureViewState(); OSSessionPersistence.Capture(GameDataManager.Instance.SaveData);
        GameSessionFlow.Capture(GameDataManager.Instance.SaveData);
        string json = JsonUtility.ToJson(GameDataManager.Instance.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, name + ".json"), json);
        return JsonUtility.FromJson<GameSaveData>(json);
    }

    static IEnumerator Screenshot(string name)
    {
        string path = Path.Combine(OutputDirectory, name + ".png");
        ScreenCapture.CaptureScreenshot(path); yield return null;
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 0,
            "background screenshot saved: " + name, 8f);
        observations.Add(new { context, label = "background-screenshot", path, applicationFocused = Application.isFocused });
    }

    static IEnumerator AllGuideTextColors()
    {
        context = "all ten partial red guide nodes";
        string[] keys = evidenceNames.Select(name => LongGuide + name).Concat(evidenceNames.Select(name => ShortGuide + name))
            .Concat(new[] { "memory.evidence.return_to_door", "memory.yard.return.key" }).ToArray();
        Check(OSMemoryTextUnit.HighlightProvider != null
            && ReferenceEquals(OSMemoryTextUnit.HighlightProvider.Target, story)
            && keys.All(key => OSMemoryTextUnit.HighlightProvider(Node(key))), "the active director registers all ten deployed guide nodes");
        foreach (string key in keys)
        {
            Check(memory.ShowInteractionData(Node(key)), "real Memory accepts guide node: " + key);
            CompleteMemory(); yield return null;
            GuideRed(key);
        }
        foreach (string key in new[] { "memory.evidence.doubt", LongGuide + "key", "interaction.kitchen.table" })
        {
            Check(!OSMemoryTextUnit.HighlightProvider(Node(key)), "doubt, the unused key hint and ordinary prose are outside the guide provider: " + key);
            Check(memory.ShowInteractionData(Node(key)), "real Memory accepts the non-guide node: " + key);
            CompleteMemory(); yield return null;
            GuidePlain(key);
        }
        Mark("all-ten-guide-nodes-rendered");
    }

    static void RainClipTransitions()
    {
        context = "muted background rain transitions";
        var shared = GameDataManager.Instance.AudioData.AMBIENT.Find(item => item != null && item.keyid == "Rain")?.audioclip;
        var homeClip = Resources.Load<AudioClip>("Chapter03Story/Audio/RainHome");
        Check(shared != null && homeClip != null && shared != homeClip, "home rain is a separate clip from the shared prologue and chapter-two Rain asset");
        var ambient = new[] { AudioManager.Instance?.AmbientSource_00, AudioManager.Instance?.AmbientSource_01 };
        string ambientBefore = AmbientConfiguration(ambient);
        var trace = new List<object>();
        GameObject probeObject = null;
        try
        {
            probeObject = new GameObject("Chapter03RainQA_MutedProbe");
            probeObject.SetActive(false);
            var probe = probeObject.AddComponent<OSChapter02NightfallPresentation>();
            probe.enabled = false; // Only the real rain method is ticked; no overlay or frame Update.
            probeObject.SetActive(true);
            Call(probe, "TickRain", 0f);
            var rain = Get(probe, "rain") as AudioSource;
            Check(rain != null, "production rain initialization creates an independent AudioSource");
            rain.mute = true;
            for (int i = 0; i < 80; i++) Call(probe, "TickRain", .05f);
            Check(rain.clip == shared && Near(rain.volume, .4f) && Near((float)Get(probe, "chapter03RainGain"), 1f),
                "default non-third-chapter night uses the original shared rain at gain one");
            Check(rain.outputAudioMixerGroup == AudioManager.Instance?.AmbientMixerGroup && rain.loop && rain.mute,
                "independent rain keeps the ambient mixer and loop settings while the QA probe stays muted");
            // Model an already-started loop paused in the background. No focus is
            // faked, and every simulated playback call is muted at the source.
            rain.Play(); rain.Pause(); Set(probe, "rainStarted", true); Set(probe, "rainPaused", true);
            RainEnvelope(probe, rain, trace, "before-home", 80, false, true, false);
            Check(rain.clip == homeClip && Near(rain.volume, 0f) && Near((float)Get(probe, "chapter03RainGain"), 0f),
                "chapter three changes to its home clip at silence and remains silent before Home opens");
            RainEnvelope(probe, rain, trace, "indoors", 120, false, true, true);
            var filter = Get(probe, "rainFilter") as AudioLowPassFilter;
            Check(rain.clip == homeClip && Near((float)Get(probe, "chapter03RainGain"), .33f) && Near(rain.volume, .132f)
                && filter != null && filter.enabled && Near(filter.cutoffFrequency, 2200f),
                "opened indoor Home reaches gain 0.33, volume 0.132 and the 2200-Hz low-pass");
            RainEnvelope(probe, rain, trace, "yard", 40, true, true, true);
            Check(rain.clip == homeClip && Near((float)Get(probe, "chapter03RainGain"), .6f) && Near(rain.volume, .24f) && !filter.enabled,
                "the yard reaches gain 0.6 and volume 0.24 with its low-pass disabled");
            RainEnvelope(probe, rain, trace, "back-indoors", 40, false, true, true);
            Check(Near((float)Get(probe, "chapter03RainGain"), .33f) && Near(rain.volume, .132f)
                && filter.enabled && Near(filter.cutoffFrequency, 2200f),
                "returning indoors restores the indoor gain and low-pass");
            RainEnvelope(probe, rain, trace, "leave-third-chapter", 180, false, false, false);
            Check(rain.clip == shared && Near((float)Get(probe, "chapter03RainGain"), 1f) && Near(rain.volume, .4f) && !filter.enabled,
                "repeated inactive requests fade back to the original shared rain, gain one and no low-pass");
            RainEnvelope(probe, rain, trace, "non-third-chapter-stable", 20, false, false, false);
            Check(rain.clip == shared && Near(rain.volume, .4f) && !filter.enabled,
                "subsequent prologue and chapter-two mode requests retain their shared-rain baseline");
            Check(GameDataManager.Instance.AudioData.AMBIENT.Find(item => item != null && item.keyid == "Rain")?.audioclip == shared
                && AmbientConfiguration(ambient) == ambientBefore,
                "the probe does not modify the shared Rain asset binding or either global ambient source");
        }
        finally
        {
            File.WriteAllText(Path.Combine(OutputDirectory, "rain-envelope.json"), JsonConvert.SerializeObject(trace, Formatting.Indented));
            if (probeObject != null) UnityEngine.Object.Destroy(probeObject);
        }
    }

    static void RainEnvelope(OSChapter02NightfallPresentation probe, AudioSource rain, List<object> trace,
        string stage, int ticks, bool outdoors, bool active, bool homeOpened)
    {
        float maxStep = 0f, maxSwitchVolume = 0f; int switches = 0;
        for (int i = 0; i < ticks; i++)
        {
            float before = rain.volume; var previousClip = rain.clip;
            probe.SetChapter03Rain(outdoors, active, homeOpened);
            Call(probe, "TickRain", .05f);
            maxStep = Mathf.Max(maxStep, Mathf.Abs(rain.volume - before));
            if (rain.clip != previousClip) { switches++; maxSwitchVolume = Mathf.Max(maxSwitchVolume, before); }
            trace.Add(new { stage, tick = i, clip = rain.clip?.name, volume = rain.volume,
                gain = (float)Get(probe, "chapter03RainGain"), baseGain = (float)Get(probe, "rainGain"),
                muted = rain.mute, applicationFocused = Application.isFocused });
        }
        observations.Add(new { context, label = "rain-envelope", stage, ticks, simulatedStepSeconds = .05f,
            maxVolumeStep = maxStep, switches, maxVolumeBeforeSwitch = maxSwitchVolume, clip = rain.clip?.name, volume = rain.volume });
        Check(maxStep <= .0082f, stage + ": repeated frame requests preserve the authored volume ramp without a jump");
        Check(switches == 0 || switches == 1 && maxSwitchVolume <= .0082f, stage + ": clips change only once and below the silent-switch threshold");
    }
    static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) <= .001f;
    static string AmbientConfiguration(AudioSource[] sources) => JsonConvert.SerializeObject(sources.Select(source => source == null ? null : new {
        id = source.GetInstanceID(), clip = source.clip != null ? source.clip.GetInstanceID() : 0, source.volume,
        source.loop, source.mute, source.pitch, mixer = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.GetInstanceID() : 0
    }));

    static IEnumerator BurstAndFifo()
    {
        yield return Prepare("burst, FIFO and active repeat", "kitchen", state => state.Add("medicine_seen"));
        Thing("kitchen_table");
        Reject(() => Thing("kitchen_table"), "100 immediate repeats leave the first table response unchanged");
        Thing("kitchen_sugar"); string sugarHint = RandomQuestionKey(ShortGuide);
        Thing("kitchen_drawer");
        Reject(() => { Thing("kitchen_table"); Thing("kitchen_sugar"); Thing("kitchen_drawer"); },
            "interleaved repeats do not move or multiply existing interactions");
        Check(S.memoryQueue.Select(Source).SequenceEqual(new[] { "thing:kitchen_table", "thing:kitchen_sugar", "thing:kitchen_sugar", "thing:kitchen_drawer" }),
            "different objects keep their first-click FIFO order");
        Mark("three-objects-pending");
        yield return Wait(() => S.memoryQueue[0].begun, "first table response begins");
        Reject(() => Thing("kitchen_table"), "an executing response also rejects another copy");
        yield return Drain("the three distinct responses finish");
        Check(nodes.SequenceEqual(new[] { Node("interaction.kitchen.table"), Node("interaction.kitchen.sugar"), Node(sugarHint), Node("interaction.kitchen.drawer") }),
            "real DataShown events follow the three-object FIFO exactly once");
        Thing("kitchen_table");
        Check(S.memoryQueue.Count == 1 && Source(S.memoryQueue[0]) == "thing:kitchen_table", "finished object accepts a fresh interaction");
        yield return Drain("repeated table interaction finishes");
        Check(nodes.Count(node => node == Node("interaction.kitchen.table")) == 2, "table is actually presented again after completion");
    }

    static IEnumerator MultiBeat()
    {
        yield return Prepare("whole multi-beat interaction", "hall");
        Thing("hall_shoes");
        string hint = RandomQuestion(ShortGuide, "thing:hall_shoes", "interaction.hall.shoes", "memory.evidence.shoes");
        Reject(() => Thing("hall_shoes"), "first shoes click retains one description, evidence and chosen short hint");
        Check(S.Has("shoes_seen"), "the object owns its complete description, evidence and random-guidance batch");
        yield return Drive(() => S.memoryQueue.Count == 2 && S.memoryQueue[0].key == "memory.evidence.shoes",
            "description leaves while the later evidence beat remains");
        Reject(() => Thing("hall_shoes"), "remaining evidence keeps the object reserved after its first beat ends");
        yield return Drain("shoes evidence finishes");
        Check(nodes.SequenceEqual(new[] { Node("interaction.hall.shoes"), Node("memory.evidence.shoes"), Node(hint) }),
            "deduplication preserves the authored shoes paragraphs and their selected short hint");
    }

    static IEnumerator SharedText()
    {
        yield return Prepare("different objects sharing text", "study");
        Thing("study_clock");
        Check(V.EnterRoom("hall", new Vector2Int(7, 5)), "enters the second clock's room");
        Thing("hall_clock");
        Check(S.memoryQueue.Count == 2 && S.memoryQueue[0].key == S.memoryQueue[1].key
            && S.memoryQueue.Select(Source).SequenceEqual(new[] { "thing:study_clock", "thing:hall_clock" }),
            "two clocks with identical text remain two distinct object interactions");
        yield return Drain("both clocks finish");
        Check(nodes.Count == 2 && nodes[0] == nodes[1], "both identical clock paragraphs are actually shown");
    }

    static IEnumerator DoorsAndScope()
    {
        yield return Prepare("doors and unowned room events", "east");
        Door("stuck_door");
        Reject(() => Door("stuck_door"), "a locked door cannot flood its pending response");
        Door("store_door");
        Check(S.memoryQueue.Select(Source).SequenceEqual(new[] { "door:stuck_door", "door:store_door" }),
            "different locked doors retain FIFO and distinct door sources");
        S.flags.Remove("room_hall");
        Check(V.EnterRoom("hall", new Vector2Int(7, 5)), "real room entry emits its ordinary narration");
        Check(S.memoryQueue.Where(beat => beat.key == "memory.hall.enter" || beat.key == "memory.hall.heartbeat")
            .Count(beat => string.IsNullOrEmpty(Source(beat))) == 2,
            "room narration does not inherit the preceding clicked-door scope");
        S.flags.Remove("first_floor");
        Check(V.WalkTo(new Vector2Int(7, 6)), "real Home step starts outside an interaction scope");
        yield return Wait(() => !V.IsWalking, "Home step finishes");
        Check(S.memoryQueue.Where(beat => beat.key == "memory.8100" || beat.key == "memory.8110")
            .Count(beat => string.IsNullOrEmpty(Source(beat))) == 2,
            "first-step narration does not inherit an earlier interaction source");
        yield return Drain("door and unowned narration finish");
        Check(nodes.Take(2).SequenceEqual(new[] { Node("interaction.east.stuck_door"), Node("interaction.east.store_door") }),
            "door responses are actually shown in click order");
    }

    static IEnumerator RejectedSideEffects()
    {
        yield return Prepare("rejected click side effects", "kitchen", state => {
            state.Add("machine_seen"); state.Add("coffee_water"); state.Add("coffee_beans"); state.coffeeStep = 3;
        });
        Thing("kitchen_cup"); Thing("kitchen_coffee_machine");
        string machineHint = RandomQuestionKey(ShortGuide);
        Check(S.Has("coffee_brewed") && S.coffeeStep == 4 && !S.Has("coffee_taken"), "another object changes coffee state while the empty-cup response waits");
        Reject(() => Thing("kitchen_cup"), "rejected cup click cannot take coffee or enqueue its new branch");
        Check(!S.items.Contains("coffee") && !S.Has("coffee_taken") && !S.Has("paper_requested_diary_03") && S.coffeeStep == 4,
            "rejected interaction preserves inventory, collection flags and coffee progress");
        Check(S.memoryQueue.Select(beat => beat.key).SequenceEqual(new[] { "interaction.kitchen.cup.empty", "interaction.kitchen.machine.brew", machineHint }),
            "state-changing interactions keep the original pending branch and FIFO");
        yield return Drain("cup and brewing responses finish");
        Thing("kitchen_cup");
        Check(S.items.Contains("coffee") && S.Has("coffee_taken") && S.coffeeStep == 5,
            "cup can execute its newly available branch after the earlier interaction finishes");
        yield return Drain("accepted coffee collection finishes", autoPaper: true);
    }

    static IEnumerator SavedQueues()
    {
        yield return Prepare("serialized interaction ownership", "study");
        // A valid unowned delay keeps Memory requests pending while session UI is rebuilt.
        S.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 3f));
        Thing("study_page"); Thing("study_lamp");
        Check(S.liveQueue.Count == 1 && Source(S.liveQueue[0]) == "thing:study_page", "one object can own both Memory and Live outputs");
        yield return Roundtrip("pending-memory-and-live");
        Check(S.memoryQueue.Any(beat => Source(beat) == "thing:study_page")
            && S.memoryQueue.Any(beat => Source(beat) == "thing:study_lamp")
            && S.liveQueue.Any(beat => Source(beat) == "thing:study_page"),
            "loaded Memory and Live queues retain their object sources");
        Reject(() => { Thing("study_page"); Thing("study_lamp"); }, "loading does not permit duplicate pending clicks");
        yield return Drain("restored queues finish");
        Check(nodes.Count(node => node == Node("interaction.study.page")) == 1
            && nodes.Count(node => node == Node("interaction.study.lamp")) == 1,
            "restored pending object responses execute once each");
    }

    static IEnumerator PaperAndLive()
    {
        yield return Prepare("paper callback and Live-only ownership", "study");
        Thing("study_desk");
        Reject(() => Thing("study_desk"), "desk spam preserves one introduction and one paper");
        yield return Wait(() => P.IsModalOpen, "real diary paper opens");
        P.HideModal(true);
        Check(S.papers.Contains("diary_07") && S.memoryQueue.Any(beat => beat.key == "memory.paper_collected"
            && Source(beat) == "thing:study_desk") && S.liveQueue.Any(beat => beat.key == "live.paper.diary07"
            && Source(beat) == "thing:study_desk"), "paper-close callback propagates source to collection narration and Live");
        yield return Drive(() => S.memoryQueue.Count == 0 && S.liveQueue.Count == 1
            && S.liveQueue[0].complete && Messages.IsReadingHoldActive,
            "only the real Live reading hold remains after diary collection");
        Check(!story.BlocksHomeInput, "Home remains interactive during the Live-only reservation");
        Reject(() => Thing("study_desk"), "completed text still reserves its object until the Live hold leaves the queue");
        Mark("live-only-reading-hold");
        yield return Drain("diary Live leaves after simulated reading-countdown completion");
        Thing("study_desk");
        Check(S.memoryQueue.Count == 1 && S.memoryQueue[0].key == "interaction.study.desk" && S.liveQueue.Count == 0,
            "collected desk becomes reusable without collecting or speaking the diary again");
        yield return Drain("collected desk's fresh description finishes");
    }

    static IEnumerator LegacyQueues()
    {
        yield return Prepare("legacy source-free queue compatibility", "kitchen");
        S.memoryQueue.Add(new OSChapter03StoryBeat("text", "interaction.kitchen.table", delay: .65f));
        S.memoryQueue.Add(new OSChapter03StoryBeat("text", "interaction.kitchen.table", delay: .65f));
        S.memoryQueue.Add(new OSChapter03StoryBeat("flag", "qa_legacy_sequence_finished"));
        var json = JObject.Parse(JsonUtility.ToJson(GameDataManager.Instance.SaveData));
        foreach (var beat in json["osProcess"]["memory"]["chapter03Story"]["memoryQueue"].Children<JObject>())
            beat.Property("interactionSource")?.Remove();
        var legacy = JsonUtility.FromJson<GameSaveData>(json.ToString(Formatting.None));
        legacy.EnsureData();
        Check(legacy.osProcess.memory.chapter03Story.memoryQueue.Count == 3
            && legacy.osProcess.memory.chapter03Story.memoryQueue.All(beat => string.IsNullOrEmpty(Source(beat))),
            "old JSON with no source keeps repeated text and later authored beats intact");
        yield return Restore(legacy);
        yield return Drain("legacy queue finishes without migration loss");
        Check(nodes.Count(node => node == Node("interaction.kitchen.table")) == 2 && S.Has("qa_legacy_sequence_finished"),
            "legacy repeated text still plays twice and reaches its following beat");
    }

    static IEnumerator Roundtrip(string name)
    {
        home.CaptureViewState(); OSSessionPersistence.Capture(GameDataManager.Instance.SaveData);
        GameSessionFlow.Capture(GameDataManager.Instance.SaveData);
        string json = JsonUtility.ToJson(GameDataManager.Instance.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, name + ".json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        Check(JsonUtility.ToJson(restored.osProcess.memory.chapter03Story) == JsonUtility.ToJson(S), "JSON preserves complete pending story state");
        yield return Restore(restored);
        Mark(name + "-restored");
    }

    static IEnumerator Restore(GameSaveData save)
    {
        Messages.NotifyOSHidden(); story.ResetPresentation();
        GameDataManager.Instance.ReplaceSaveData(save); home.Suspend();
        yield return OSSessionPersistence.Restore(save);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        if (!memoryWindow.IsOpen) os.OpenMemoryWindow();
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow), "restored Home becomes available");
    }

    static void Thing(string id)
    {
        if (!story.IsAvailable || story.BlocksHomeInput) throw new InvalidOperationException("Thing input unavailable: " + id);
        var thing = V.CurrentRoom?.FindThing(id);
        if (thing == null || thing.hidden || !thing.interactive) throw new InvalidOperationException("Visible interactive thing missing: " + id);
        Call(V, "Activate", thing); // Real frontend event -> production OnThing subscriber.
    }
    static void Door(string id)
    {
        if (!story.IsAvailable || story.BlocksHomeInput) throw new InvalidOperationException("Door input unavailable: " + id);
        var door = V.CurrentRoom?.FindDoor(id);
        if (door == null || !door.locked) throw new InvalidOperationException("Locked test door missing: " + id);
        Call(V, "ArriveAtDoor", door);
    }
    static void Reject(Action clicks, string label)
    {
        string before = JsonUtility.ToJson(S);
        for (int i = 0; i < 100; i++) clicks();
        Check(JsonUtility.ToJson(S) == before, label);
    }
    static string Source(OSChapter03StoryBeat beat) => beat.interactionSource;
    static int Node(string key)
    {
        int node = OSChapter03StoryContent.Instance.Node(key);
        if (node < 0) throw new InvalidOperationException("Missing real story node: " + key);
        return node;
    }
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing;
    static bool Idle => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !Messages.IsReadingHoldActive
        && !Messages.IsWaitingForOption && P?.IsModalOpen != true && !V.IsWalking;
    static void CompleteMemory() { foreach (var unit in memory.TextUnits) if (unit != null) unit.CompleteImmediately(); }
    static void Pump(bool autoPaper, bool finishLiveHold = false)
    {
        CompleteMemory();
        string id = Messages.DisplayedEntryId;
        if (OSChapter03StoryDirector.IsOwnEntry(id)) Messages.TryCompleteDisplayedTextImmediately(id);
        if (finishLiveHold && OSChapter03StoryDirector.IsOwnEntry(id) && Messages.IsReadingHoldActive)
        {
            var timer = Get(Messages, "autoCloseTween") as Tween;
            Check(timer != null && timer.IsActive(), "an existing Live countdown is available for simulated completion");
            observations.Add(new { context, label = "simulated-reading-hold-completion", entry = id,
                remaining = Messages.ReadingHoldRemaining, applicationFocused = Application.isFocused,
                naturalTimingVerified = false });
            // Execute the existing delayed call, including its production window
            // cleanup. Do not mutate the durable beat or change timer settings.
            timer.Complete(true);
            Check(!Messages.IsReadingHoldActive, "production completion callback releases the simulated Live countdown");
        }
        if (autoPaper && P?.IsModalOpen == true) P.HideModal(true);
    }
    static IEnumerator Drive(Func<bool> predicate, string label, float seconds = 25f, bool autoPaper = false, bool finishLiveHold = false)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { Pump(autoPaper, finishLiveHold); yield return null; }
        Check(predicate(), label);
    }
    static IEnumerator Drain(string label, bool autoPaper = false)
    {
        Pump(autoPaper, finishLiveHold: true); yield return null;
        yield return Drive(() => Idle, label, autoPaper: autoPaper, finishLiveHold: true);
        Mark(label);
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static void PrepareBackgroundTick()
    {
        testMouse?.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
    }
    static void OnData(OSMemoryReadData data) { if (data != null) nodes.Add(data.DataIndex); }
    static void OnLive(string sequence, OSCharacterInteractionEntryData entry) { if (entry != null) liveEntries.Add(entry.EntryId); }
    static void Mark(string label) => observations.Add(new { context, label, frame = Time.frameCount,
        nodes = nodes.ToArray(), liveEntries = liveEntries.ToArray(), story = JObject.Parse(JsonUtility.ToJson(S)) });
    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label + " | " + Describe());
        checks.Add(context + ": " + label);
    }
    static string Describe() => "room=" + home?.View?.CurrentRoom?.id + ", available=" + story?.IsAvailable
        + ", blocked=" + story?.BlocksHomeInput + ", memory=" + memory?.CurrentData?.DataIndex
        + ", queue=" + (story?.State == null ? "null" : string.Join(",", S.memoryQueue.Select(beat => beat.type + ":" + beat.key + ":" + Source(beat))))
        + ", live=" + (os != null ? Messages.DisplayedEntryId : "null");
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static object Call(object target, string name, params object[] args)
    {
        var method = target?.GetType().GetMethod(name, Fields);
        if (method == null) throw new MissingMethodException(target?.GetType().Name, name);
        return method.Invoke(target, args);
    }
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
