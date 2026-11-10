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
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in production-script regression. Runs the actual console checkpoint and
// Live input contract against isolated saves. No fixture dialogue or stage driver.
public static class OSChapter04DeploymentQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<OSChapter04StoryDirector.ResourceCue> costs = new List<OSChapter04StoryDirector.ResourceCue>();
    static readonly List<OSChapter04PresentationBridge.PresentationRequest> requests = new List<OSChapter04PresentationBridge.PresentationRequest>();
    static readonly HashSet<string> visited = new HashSet<string>();
    static readonly Dictionary<MonoBehaviour, bool> focusStates = new Dictionary<MonoBehaviour, bool>();
    static readonly Dictionary<OSChapter04StoryContent.Beat, float> delays = new Dictionary<OSChapter04StoryContent.Beat, float>();
    static readonly Dictionary<int, string> graveSnapshots = new Dictionary<int, string>();
    static readonly int[] graveRestoreLines = { 3, 4, 6, 7, 10, 13, 16 };
    static readonly int[] graveHeartWeights = { 0, 0, 0, 0, 0, 0, 1, 0, 2, 1, 2, 2, 3, 3, 0, 0 };
    static readonly int[] graveFaultWeights = { 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 2, 2, 3, 3, 4 };
    static readonly string[] LegacyGraveSuffixes = { "45", "46", "47", "48", "49", "50", "50a", "50b", "51", "52", "53" };
    static readonly string[] section2Order = { "11", "12", "13", "13a", "13b", "13c", "13d", "14", "15", "17", "18", "19" };
    static readonly Dictionary<string, string> section2Options = new Dictionary<string, string> {
        { "12", "hard" }, { "13", "explain" }, { "13b", "unfamiliar" },
        { "13d", "intruder" }, { "14", "care" }, { "15", "memories" }
    };
    static readonly string[] section3Order = { "20", "21", "22", "22a", "23" };
    static readonly Dictionary<string, string> section3Options = new Dictionary<string, string> {
        { "22", "later" }, { "22a", "efficacy" }
    };
    static readonly string[] section4Order = { "23", "23a", "23b", "23c", "24", "24a", "25", "25a", "25b", "25c", "25d", "25e", "26" };
    static readonly Dictionary<string, string> section4Options = new Dictionary<string, string> {
        { "23a", "unseen" }, { "23c", "later" }, { "24a", "reassure" }, { "25a", "protect" },
        { "25b", "story" }, { "25c", "hacker" }, { "25d", "decline" }, { "26", "leave" }
    };
    static readonly string[] section5Order = { "27", "28", "29", "31", "32", "34", "35" };
    static readonly Dictionary<string, string> section5Options = new Dictionary<string, string> {
        { "28", "think" }, { "29", "understand" }, { "31", "proud" }
    };
    static readonly string[] section6Order = { "36", "37", "38", "39", "40", "40a", "41", "43" };
    static readonly Dictionary<string, string> section6Options = new Dictionary<string, string> {
        { "36", "rapport" }, { "38", "question" }
    };
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04StoryDirector story, observedStory;
    static OSChapter04PresentationBridge observedBridge;
    static Mouse mouse;
    static Keyboard keyboard;
    static bool cancelled;
    static string context, pendingSnapshot, legacySourceSnapshot, legacy30SourceSnapshot, legacy33SourceSnapshot;
    static string legacy42SourceSnapshot, legacy44SourceSnapshot;
    static float graveFinalCompletedAt;
    static int finalRequests, finalCompletions;
    static int round6SceneChecks;
    static bool round6TransitionRestored;
    static OSChapter04StoryState State => story.State;
    static OSLinkMessageController Link => os.LinkMessageController;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    public static string Start()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameTest.Instance == null
            || GameManager.Instance == null || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || SceneLoadManager.Instance.IsBusy
            || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); errors.Clear(); observations.Clear(); costs.Clear(); requests.Clear(); visited.Clear();
        focusStates.Clear(); delays.Clear(); graveSnapshots.Clear(); cancelled = false; finalRequests = finalCompletions = 0;
        round6SceneChecks = 0; round6TransitionRestored = false;
        pendingSnapshot = legacySourceSnapshot = legacy30SourceSnapshot = legacy33SourceSnapshot = null;
        legacy42SourceSnapshot = legacy44SourceSnapshot = null;
        graveFinalCompletedAt = 0f;
        context = "initialization"; data = GameDataManager.Instance;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter04DeploymentQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp",
            "Assets/Resources/Chapter04/story.json", "Assets/Data/OSCharacterInteractionData.asset", "Assets/Data/LocalizationData.asset" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        Application.logMessageReceived += CaptureError;
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            data.SetCurrentLanguage(LanguageState.zh_CN); Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            Application.runInBackground = true; AudioListener.volume = 0f;
            mouse = InputSystem.AddDevice<Mouse>("Chapter04DeploymentQAMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("Chapter04DeploymentQAKeyboard"); ReleaseInput();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancelled) throw new OperationCanceledException("Chapter04 deployment QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + failure;
                    Debug.LogError("[Chapter04 Deployment QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null)
            {
                Check(files.Select(Hash).SequenceEqual(hashes), "formal saves, script, data assets and scenes remain unchanged");
                Status = errors.Count == 0 ? "PASS" : "FAILED: " + errors.Count + " runtime errors";
            }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                Unbind();
                foreach (var pair in delays) pair.Key.autoAdvanceDelay = pair.Value;
                story?.DebugSetFocus(false); story?.FinalHeal?.DebugReleaseFocus(); story?.ResetPresentation(); os?.Chapter04Entry?.ResetPresentation();
                os?.Chapter03Story?.ResetPresentation();
                os?.MemoryWindowController?.GetComponent<OSChapter03HomeController>()?.Suspend();
                os?.LinkMessageController?.NotifyOSHidden();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                foreach (var pair in focusStates) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume
                    || resources != null && resources.enabled != resourcesEnabled
                    || hashes != null && !files.Select(Hash).SequenceEqual(hashes))
                    throw new InvalidOperationException("Original data / session settings were not restored");
                checks.Add("cleanup: original data, files, language, Demo, resource ticking, audio, input and focus restored");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            Application.logMessageReceived -= CaptureError;
            if (Status == "PASS" && errors.Count != 0) Status = "FAILED: cleanup logged runtime errors";
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status += ": " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, errors, visited = visited.OrderBy(x => x).ToArray(),
                originalFiles = files, originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                note = "Actual console checkpoint 19 and the production 70-entry route. Earlier section, layout and save regressions remain covered. The 16-line grave section uses its authored manual input, grief tiers and real two-second warning delay, with production TV and resource receipts checked across saves. Legacy grave IDs migrate through the manifest mappings. The real warning button starts the final-treatment backend; fixed-clock no-input judgement verifies the no-loss route and the final mother input enters the durable ending."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter04 Deployment QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; story = null; mouse = null; keyboard = null;
        }
    }

    static IEnumerator Run()
    {
        context = "production data";
        var content = OSChapter04StoryContent.Instance;
        Check(content.Available && content.Validate(data.OSCharacterInteractionData).Count == 0, "production manifest resolves every real message and route");
        Check(content.Beats.Count == 85 && content.Beats.Count(beat => !beat.legacyResumeOnly) == 70
            && content.Beats.Where(beat => beat.legacyResumeOnly).Select(beat => beat.entryId).OrderBy(id => id)
                .SequenceEqual(new[] { "16", "30", "33", "42", "44", "45", "46", "47", "48", "49", "50a", "50b", "51", "52", "53" }
                    .Select(suffix => OSChapter04StoryContent.EntryPrefix + suffix).OrderBy(id => id)),
            "production script contains 70 active entries and 15 retained historical entries");
        Check(content.Beats.Sum(beat => beat.options.Length) == 27
            && content.Beats.Where(beat => !beat.legacyResumeOnly).Sum(beat => beat.options.Length) == 23,
            "production script contains 23 active options and four retained historical options");
        CheckSection2Routes(content);
        CheckGraveRoutes(content);
        Check(content.HeartWeightBudget == 17 && content.Beats.Count(b => b.heartWeight > 0) == 10
            && content.Beats.Sum(b => b.heartWeight) == 17, "ten heart cues use the complete seventeen-weight budget");
        Check(content.FaultWeightBudget == 19 && content.Beats.Count(b => b.faultWeight > 0) == 10
            && content.Beats.Sum(b => b.faultWeight) == 19, "ten fault cues use the complete nineteen-weight budget");
        Check(string.IsNullOrEmpty(content.DateLocalizationKey) && string.IsNullOrEmpty(content.DateFallback)
            && content.Beats.All(beat => string.IsNullOrEmpty(beat.dateLocalizationKey) && string.IsNullOrEmpty(beat.dateFallback)),
            "the authored timeline has no historical date keys or fallback labels");
        Check(new[] { "chapter04_story_39", "chapter04_story_40" }.All(id => content.Find(id).autoAdvanceDelay == -1f
            && content.Find(id).options.Length == 0),
            "production lines 39 and 40 use plain text with explicit mother-key reveal and heart-close gates, without timed auto-advance or options");
        foreach (var beat in content.Beats)
        {
            // The new grave section remains untouched: its manual lines and
            // dedicated warning timer are part of this production regression.
            if (GraveLine(beat.entryId) > 0) continue;
            delays.Add(beat, beat.autoAdvanceDelay); beat.autoAdvanceDelay = -1f;
        }
        Check(OSChapter04StoryDirector.CalculateHeartTarget(34, 17, 17) == 24
            && OSChapter04StoryDirector.CalculateHeartTarget(10, 17, 17) == 10,
            "heart budget reaches 24 without healing a lower starting value");
        Check(OSChapter04StoryDirector.CalculateFaultTarget(54, 19, 19) == 74
            && OSChapter04StoryDirector.CalculateFaultTarget(90, 19, 19) == 90,
            "fault budget reaches 74 without lowering an already higher value");
        context = "console checkpoint 19";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "real Chapter04StoryStart checkpoint accepts the jump");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint finished", 80f);
        Bind();
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession && data.CurrentChapterIndex == 4,
            "checkpoint establishes chapter four in isolated storage");
        yield return Wait(() => story.IsAvailable && State.started && State.currentEntryId == content.FirstEntryId,
            "checkpoint reaches the actual first chapter-four line");
        Check(State.resourceBaselineCaptured, "entry captured the source save's resource baseline");
        Check(State.finalTreatmentStage == OSChapter04FinalTreatmentStage.None, "checkpoint has no inherited final-treatment state");
        observations.Add(new { type = "chapter resource baseline", State.initialHeart, State.initialFault, heart = Heart(), fault = Fault() });
        yield return RunRoute(true);
        Check(round6SceneChecks == 5 && round6TransitionRestored,
            "all five round-six TV checkpoints and a mid-transition save were verified through the production route");
        Check(costs.Count(c => c.HeartWeight > 0) == 10 && costs.Count(c => c.FaultWeight > 0) == 10,
            "the complete route emits exactly ten heart and ten fault presentation cues");
        Check(graveRestoreLines.All(line => graveSnapshots.ContainsKey(line)),
            "all seven grave checkpoint snapshots were captured from the production route");
        yield return VerifyFailureWarning();
        yield return VerifyTreatment("grave");
        Check(visited.Count == 70 && content.Beats.Where(beat => !beat.legacyResumeOnly).All(beat => visited.Contains(beat.entryId))
            && content.Beats.Where(beat => beat.legacyResumeOnly).All(beat => !visited.Contains(beat.entryId)),
            "the single route covers all 70 active entries without presenting any historical-only line");
        Check(finalRequests == 1 && finalCompletions == 1,
            "the warning-confirmed route requests and completes treatment once; reloads repeat neither event");
        yield return VerifyLegacyResume(legacySourceSnapshot, "16", "17", "memories");
        yield return VerifyLegacyResume(legacy30SourceSnapshot, "30", "31", null);
        yield return VerifyLegacyResume(legacy33SourceSnapshot, "33", "34", "answer");
        yield return VerifyLegacyResume(legacy42SourceSnapshot, "42", "43", null);
        yield return VerifyLegacyResume(legacy44SourceSnapshot, "44", "grave_01", null);
        yield return VerifyLegacyGraveRoutes();
    }

    static IEnumerator RunRoute(bool exerciseRestore)
    {
        int steps = 0;
        bool reopened = false, restoredDoor = false, restoredCost = false;
        var section2Visited = new List<string>();
        var section2Selected = new List<string>();
        var section3Visited = new List<string>();
        var section3Selected = new List<string>();
        var section4Visited = new List<string>();
        var section4Selected = new List<string>();
        var section5Visited = new List<string>();
        var section5Selected = new List<string>();
        var section6Visited = new List<string>();
        var section6Selected = new List<string>();
        var graveVisited = new List<int>();
        float singleReplyFooterHeight = 0f;
        bool section4Rejoined = false, section5Rejoined = false, section6Rejoined = false;
        while (!State.completed)
        {
            Check(++steps <= story.Content.Beats.Count, "route remains finite within the authored entry count");
            string id = State.currentEntryId;
            context = "production route / " + id;
            yield return Wait(() => story.IsAvailable && Link.DisplayedEntryId == id && Link.IsPresenting && Ready(Link.LinkWindow),
                "real Live message is ready: " + id);
            var beat = story.Content.Find(id);
            Check(beat != null && !string.IsNullOrEmpty(story.GetLineText(id)), "authored text is available");
            Check(!beat.legacyResumeOnly, "the new route never enters a legacy-only line");
            string suffix = id.Substring(OSChapter04StoryContent.EntryPrefix.Length);
            int graveLine = GraveLine(id);
            if (graveLine > 0)
            {
                graveVisited.Add(graveLine);
                Check(State.griefTier == beat.griefTier && State.griefLineStep == beat.griefLineStep
                    && (OSChapter04GriefPresentation.Current?.Tier ?? 0) == beat.griefTier,
                    "the authored grief tier and step are applied before the line's typing input: " + graveLine);
                Check(story.CurrentEntry.PartType == OSCharacterInteractionPartType.Text && !Link.IsWaitingForOption,
                    "the grave section presents ordinary text without a player option: " + graveLine);
                if (exerciseRestore && graveLine == 13 && !State.currentTextCompleted)
                    yield return VerifyGraveTypingRestore();
            }
            if (section2Order.Contains(suffix)) section2Visited.Add(suffix);
            int section3Index = Array.IndexOf(section3Order, suffix);
            if (section3Index >= 0)
            {
                section3Visited.Add(suffix);
                if (section3Index < section3Order.Length - 1)
                {
                    string next = OSChapter04StoryContent.EntryPrefix + section3Order[section3Index + 1];
                    Check(section3Options.TryGetValue(suffix, out string expected)
                        ? beat.options.Length == 1 && beat.options[0].optionId == expected && beat.options[0].nextEntryId == next
                        : beat.options.Length == 0 && beat.nextEntryId == next,
                        "the third-section route reaches the exact next authored line after " + suffix);
                }
            }
            int section4Index = Array.IndexOf(section4Order, suffix);
            if (section4Index >= 0)
            {
                section4Visited.Add(suffix);
                string next = OSChapter04StoryContent.EntryPrefix + (section4Index + 1 < section4Order.Length ? section4Order[section4Index + 1] : "27");
                Check(section4Options.TryGetValue(suffix, out string expected)
                    ? beat.options.Length == 1 && beat.options[0].optionId == expected && beat.options[0].nextEntryId == next
                    : beat.options.Length == 0 && beat.nextEntryId == next,
                    "the first-settlement route reaches the exact next authored line after " + suffix);
                Check(beat.heartWeight == 0 && beat.faultWeight == 0, "the first-settlement line does not charge either resource: " + suffix);
            }
            int section5Index = Array.IndexOf(section5Order, suffix);
            if (section5Index >= 0)
            {
                section5Visited.Add(suffix);
                string next = OSChapter04StoryContent.EntryPrefix + (section5Index + 1 < section5Order.Length ? section5Order[section5Index + 1] : "36");
                Check(section5Options.TryGetValue(suffix, out string expected)
                    ? beat.options.Length == 1 && beat.options[0].optionId == expected && beat.options[0].nextEntryId == next
                    : beat.options.Length == 0 && beat.nextEntryId == next,
                    "the second-settlement route reaches the exact next authored line after " + suffix);
                Check(beat.heartWeight == (suffix == "31" || suffix == "35" ? 1 : 0) && beat.faultWeight == 0
                    && (suffix == "27" ? beat.theatreStateAfterText == OSChapter04PresentationBridge.NorthSettlement
                        : string.IsNullOrEmpty(beat.theatreStateAfterText)),
                    "the second settlement retains only its two heart charges and NorthSettlement entry cue: " + suffix);
            }
            int section6Index = Array.IndexOf(section6Order, suffix);
            if (section6Index >= 0)
            {
                section6Visited.Add(suffix);
                string next = OSChapter04StoryContent.EntryPrefix + (section6Index + 1 < section6Order.Length ? section6Order[section6Index + 1] : "grave_01");
                Check(section6Options.TryGetValue(suffix, out string expected)
                    ? beat.options.Length == 1 && beat.options[0].optionId == expected && beat.options[0].nextEntryId == next
                    : beat.options.Length == 0 && beat.nextEntryId == next,
                    "the box section reaches the exact next authored line after " + suffix);
                string expectedStage = suffix == "36" ? OSChapter04PresentationBridge.LabNight
                    : suffix == "43" ? OSChapter04PresentationBridge.FlowerWalk : string.Empty;
                Check(beat.heartWeight == (suffix == "39" ? 1 : 0) && beat.faultWeight == (suffix == "41" ? 1 : 0)
                    && (beat.theatreStateAfterText ?? string.Empty) == expectedStage,
                    "the box section retains its two charges and only the LabNight/FlowerWalk cues: " + suffix);
            }
            Check(string.IsNullOrEmpty(story.GetDateText(id)) && string.IsNullOrEmpty(story.GetDateKey(id)), "current line resolves no historical date");
            int beforeRequests = requests.Count;
            string priorState = State.theatreState;
            int beforeCosts = costs.Count;
            bool alreadyComplete = State.currentTextCompleted;
            if (!alreadyComplete) Check(story.TryCompleteCurrentText(), "complete input is accepted by the original Live typewriter");
            // Capture while the real transition is still in flight, before waiting for the input guard.
            if (exerciseRestore && id == "chapter04_story_20")
                yield return VerifyRound6Transition(id, OSChapter04PresentationBridge.WastelandWalk);
            yield return Wait(() => State.currentTextCompleted && State.currentCueCommitted && State.currentResourcesCommitted
                && !story.IsOptionGestureBlocked, "text, stage and resource receipts finish");
            if (exerciseRestore && (suffix == "21" || suffix == "25a" || suffix == "35" || suffix == "40a"))
            {
                yield return null;
                if (suffix == "35" || suffix == "40a") yield return ScreenshotDialogue(id,
                    suffix == "35" ? "section5-35-long-line.png" : "section6-40a-long-line.png");
                else yield return ScreenshotRound6(id, suffix == "21" ? OSChapter04PresentationBridge.WastelandWalk : OSChapter04PresentationBridge.TentCare,
                    suffix == "21" ? "section3-21-long-line.png" : "section4-25a-long-reply-normal.png");
                var label = story.GetComponent<OSChapter04ChatView>()?.CurrentBubble;
                Check(label != null && label.gameObject.activeInHierarchy && label.text == story.GetLineText(id),
                    "line " + suffix + " uses its complete authored text in the real current chat bubble");
                label.ForceMeshUpdate();
                observations.Add(new { context, type = "actual long dialogue line", id, label.text, label.maxVisibleCharacters,
                    characters = label.textInfo.characterCount, label.isTextOverflowing, label.preferredHeight,
                    width = label.rectTransform.rect.width, height = label.rectTransform.rect.height });
                Check(label.maxVisibleCharacters >= label.textInfo.characterCount && !label.isTextOverflowing
                    && label.preferredHeight <= label.rectTransform.rect.height + 1f,
                    "line " + suffix + " reveals every character and fits the full text without truncation or overflow");
            }
            visited.Add(id);
            Check(State.history.Count(r => r != null && r.visit == State.currentVisit) == 1, "current visit has exactly one chat-history record");
            if (!alreadyComplete && !string.IsNullOrEmpty(beat.theatreStateAfterText))
                Check(priorState == beat.theatreStateAfterText || requests.Skip(beforeRequests).Any(r => !r.IsRestore && r.StateId == beat.theatreStateAfterText),
                    "text completion emits its authored stage request: " + beat.theatreStateAfterText);
            if (!alreadyComplete && (beat.heartWeight > 0 || beat.faultWeight > 0))
            {
                Check(costs.Count == beforeCosts + 1, "weighted text emits one resource presentation event");
                var cue = costs[costs.Count - 1];
                Check(cue.EntryId == id && cue.Visit == State.currentVisit && cue.HeartDelta <= 0 && cue.FaultDelta >= 0,
                    "resource event identifies the committed visit and moves in the intended directions");
                Check(Heart() <= State.initialHeart && Fault() >= State.initialFault, "resource budget never reverses the incoming values");
                Check(Heart() >= Math.Min(State.initialHeart, OSResourceRuntimeState.LowResourceThreshold + 1)
                    && Fault() <= Math.Max(State.initialFault, OSResourceRuntimeState.HighFaultThreshold - 1),
                    "resource costs do not cross the real heart/fault warning limits");
                observations.Add(new { id, State.currentVisit, heart = Heart(), fault = Fault(), State.heartWeightApplied, State.faultWeightApplied });
            }
            string round6State = exerciseRestore ? Round6SceneFor(id) : null;
            if (round6State != null) yield return VerifyRound6Scene(id, round6State);
            if (exerciseRestore && suffix == "32") legacy33SourceSnapshot = Capture("section5-32-legacy-baseline");
            if (exerciseRestore && suffix == "35")
                yield return RestoreCompletedLine(id, "section5-35-completed", OSChapter04PresentationBridge.NorthSettlement, false);
            if (exerciseRestore && suffix == "41")
            {
                yield return RestoreCompletedLine(id, "section6-41-paid", OSChapter04PresentationBridge.LabNight, false);
                legacy42SourceSnapshot = Capture("section6-41-legacy-baseline");
            }
            if (exerciseRestore && suffix == "43") legacy44SourceSnapshot = Capture("section6-43-legacy-baseline");
            if (exerciseRestore && !restoredDoor && beat.theatreStateAfterText == OSChapter04PresentationBridge.CityArrive)
            {
                string snapshot = Capture("door");
                yield return Restore(snapshot, "door");
                yield return Wait(() => story.CanContinue || story.CurrentEntry?.PartType == OSCharacterInteractionPartType.TextWithOptions,
                    "door save restores the same dialogue");
                Check(State.theatreState == OSChapter04PresentationBridge.AtDoor, "arrival save restores to the waiting-at-door state");
                restoredDoor = true;
            }
            if (exerciseRestore && !reopened && beat.heartWeight > 0)
            {
                int heart = Heart(), fault = Fault(), count = costs.Count;
                os.TVWindow.Close();
                yield return Wait(() => !os.TVWindow.IsOpen && !os.TVWindow.IsClosing, "TV closes on a paid resource beat");
                Check(os.OpenTVWindow(), "TV reopens through the normal OS entry");
                yield return Wait(() => story.IsAvailable && Ready(os.TVWindow), "reopened TV resumes the current line");
                Check(Heart() == heart && Fault() == fault && costs.Count == count, "reopening does not repay or re-emit the resource cue");
                reopened = true;
            }
            if (exerciseRestore && !restoredCost && beat.heartWeight > 0)
            {
                int heart = Heart(), fault = Fault(), count = costs.Count;
                string snapshot = Capture("paid-resource");
                yield return Restore(snapshot, "paid-resource");
                yield return Wait(() => story.IsAvailable && State.currentResourcesCommitted, "paid resource save resumes");
                Check(Heart() == heart && Fault() == fault && costs.Count == count, "loading a paid beat changes neither values nor presentation receipts");
                Check(string.IsNullOrEmpty(story.GetDateText(id)) && string.IsNullOrEmpty(story.GetDateKey(id)),
                    "loading a resource beat does not restore a historical date");
                restoredCost = true;
            }
            if (graveLine > 0)
            {
                yield return VerifyGraveLine(graveLine, exerciseRestore);
                if (graveLine == 16) break; // The final text owns a timed warning, never Continue.
            }
            if (beat.options.Length > 0)
            {
                Check(beat.options.Length == 1, "the production route has one authored response at each remaining choice");
                string option = beat.options[0].optionId;
                bool selectedByPointer = false;
                yield return Wait(() => !story.IsOptionGestureBlocked && Link.IsWaitingForOption, "authored choices are ready");
                if (section2Options.TryGetValue(suffix, out string expectedOption))
                {
                    Check(option == expectedOption, "section two selects the authored response: " + suffix + "/" + option);
                    section2Selected.Add(suffix + "/" + option);
                    yield return null;
                    CheckReplyLayout(beat);
                    if (exerciseRestore && suffix == "13b")
                    {
                        int visit = State.currentVisit, historyCount = State.history.Count, heart = Heart(), fault = Fault(), count = costs.Count;
                        string snapshot = Capture("section2-13b-awaiting-option");
                        yield return Restore(snapshot, "section2-13b-awaiting-option");
                        yield return Wait(() => story.IsAvailable && State.currentEntryId == id && Link.DisplayedEntryId == id
                            && Link.IsWaitingForOption && !story.IsOptionGestureBlocked, "13b restores its real awaiting-option presentation");
                        Check(State.currentVisit == visit && State.history.Count == historyCount && State.currentTextCompleted
                            && State.currentCueCommitted && State.currentResourcesCommitted && Heart() == heart && Fault() == fault && costs.Count == count,
                            "13b option save restores the same visit without repeated history, cues or resource charges");
                        CheckReplyLayout(beat);
                    }
                    if (exerciseRestore && suffix == "15") legacySourceSnapshot = Capture("section2-15-legacy-baseline");
                }
                if (section3Options.TryGetValue(suffix, out string section3Option))
                {
                    Check(option == section3Option, "section three selects the authored response: " + suffix + "/" + option);
                    section3Selected.Add(suffix + "/" + option);
                    yield return null;
                    if (exerciseRestore && suffix == "22a")
                        yield return ScreenshotRound6(id, OSChapter04PresentationBridge.SettlementWait, "section3-22a-awaiting-option.png");
                    CheckReplyLayout(beat, "section three");
                    if (exerciseRestore && suffix == "22a")
                    {
                        int visit = State.currentVisit, heart = Heart(), fault = Fault(), count = costs.Count;
                        int heartWeight = State.heartWeightApplied, faultWeight = State.faultWeightApplied;
                        string history = Round6History();
                        string snapshot = Capture("section3-22a-awaiting-option");
                        yield return Restore(snapshot, "section3-22a-awaiting-option");
                        yield return Wait(() => story.IsAvailable && State.currentEntryId == id && Link.DisplayedEntryId == id
                            && Link.IsWaitingForOption && !story.IsOptionGestureBlocked, "22a restores its real awaiting-option presentation");
                        Check(State.currentVisit == visit && Round6History() == history && State.currentTextCompleted
                            && State.currentCueCommitted && State.currentResourcesCommitted && Heart() == heart && Fault() == fault && costs.Count == count
                            && State.heartWeightApplied == heartWeight && State.faultWeightApplied == faultWeight
                            && State.theatreState == OSChapter04PresentationBridge.SettlementWait,
                            "22a option save restores the same history, paid resources and inherited settlement scene");
                        CheckReplyLayout(beat, "section three");
                    }
                }
                if (section4Options.TryGetValue(suffix, out string section4Option))
                {
                    Check(option == section4Option, "the first settlement selects the authored response: " + suffix + "/" + option);
                    section4Selected.Add(suffix + "/" + option);
                    yield return null;
                    CheckReplyLayout(beat, "section four");
                    var button = ReplyButton();
                    if (suffix == "23c") singleReplyFooterHeight = ((RectTransform)button.transform.parent).rect.height;
                    if (exerciseRestore && suffix == "25a")
                    {
                        yield return RestoreCompletedLine(id, "section4-25a-protect", OSChapter04PresentationBridge.TentCare);
                        CheckReplyLayout(beat, "section four after restore");
                        button = ReplyButton();
                        yield return CheckLongReplyHover(button);
                        int visit = State.currentVisit;
                        ClickReply(button);
                        Check(State.currentEntryId == "chapter04_story_25b" && State.currentVisit == visit + 1
                            && State.history.Single(record => record != null && record.visit == visit).optionId == "protect",
                            "the real multiline protect button click records one reply and continues to 25b");
                        selectedByPointer = true;
                    }
                    if (suffix == "25b")
                    {
                        var label = button.transform.Find("Label").GetComponent<TMP_Text>();
                        label.ForceMeshUpdate();
                        Check(label.textInfo.lineCount == 1 && Mathf.Abs(((RectTransform)button.transform).rect.height - 20f) < .01f
                            && singleReplyFooterHeight > 0f && Mathf.Abs(((RectTransform)button.transform.parent).rect.height - singleReplyFooterHeight) < .01f,
                            "the short story reply after protect restores the original 20-pixel button and single-row footer");
                    }
                    if (exerciseRestore && suffix == "26")
                        yield return RestoreCompletedLine(id, "section4-26-leave", OSChapter04PresentationBridge.WastelandWalk);
                }
                if (section5Options.TryGetValue(suffix, out string section5Option))
                {
                    Check(option == section5Option, "the second settlement selects the authored response: " + suffix + "/" + option);
                    section5Selected.Add(suffix + "/" + option);
                    yield return null;
                    CheckReplyLayout(beat, "section five");
                    if (exerciseRestore && suffix == "29") legacy30SourceSnapshot = Capture("section5-29-legacy-baseline");
                    if (exerciseRestore && suffix == "31")
                    {
                        yield return RestoreCompletedLine(id, "section5-31-proud", OSChapter04PresentationBridge.NorthSettlement);
                        yield return ScreenshotDialogue(id, "section5-31-long-reply-normal.png");
                        CheckReplyLayout(beat, "section five after restore");
                        var button = ReplyButton();
                        yield return CheckLongReplyHover(button, id, OSChapter04PresentationBridge.NorthSettlement, "section5-31-long-reply-hover.png");
                        int visit = State.currentVisit;
                        ClickReply(button);
                        Check(State.currentEntryId == "chapter04_story_32" && State.currentVisit == visit + 1
                            && State.history.Single(record => record != null && record.visit == visit).optionId == "proud",
                            "the restored multiline proud response records one reply and continues directly to 32");
                        selectedByPointer = true;
                    }
                }
                if (section6Options.TryGetValue(suffix, out string section6Option))
                {
                    Check(option == section6Option, "the box section selects the authored response: " + suffix + "/" + option);
                    section6Selected.Add(suffix + "/" + option);
                    yield return null;
                    CheckReplyLayout(beat, "section six");
                    if (exerciseRestore && suffix == "36")
                    {
                        yield return ScreenshotDialogue(id, "section6-36-rapport-reply.png");
                        var button = ReplyButton();
                        var label = button.transform.Find("Label").GetComponent<TMP_Text>();
                        label.ForceMeshUpdate();
                        bool needsWrap = label.GetPreferredValues(label.text, float.PositiveInfinity, float.PositiveInfinity).x > label.rectTransform.rect.width;
                        Check(!needsWrap || label.enableWordWrapping && label.textInfo.lineCount > 1,
                            "rapport uses complete wrapped text whenever its authored width exceeds the reply rectangle");
                        int visit = State.currentVisit;
                        ClickReply(button);
                        Check(State.currentEntryId == "chapter04_story_37" && State.currentVisit == visit + 1
                            && State.history.Single(record => record != null && record.visit == visit).optionId == "rapport",
                            "the real rapport button click records one reply and continues directly to 37");
                        selectedByPointer = true;
                    }
                }
                int selectedVisit = State.currentVisit;
                if (!selectedByPointer) Check(story.TrySelectOption(option), "actual option route is accepted: " + option);
                if (suffix == "26")
                {
                    yield return Wait(() => State.currentEntryId == "chapter04_story_27" && Link.DisplayedEntryId == State.currentEntryId,
                        "the restored leave option joins the next section at the unchanged line 27");
                    Check(State.currentVisit == selectedVisit + 1
                        && State.history.Single(record => record != null && record.visit == selectedVisit).optionId == "leave",
                        "leaving the first settlement adds one reply receipt without replaying an earlier line");
                    section4Rejoined = true;
                }
            }
            else if (suffix == "39" || suffix == "40")
            {
                yield return VerifyMotherGiftGate(id, exerciseRestore);
            }
            else
            {
                yield return Wait(() => story.CanContinue, "continue input is ready");
                int continuedVisit = State.currentVisit;
                Check(story.TryContinue(), "actual next-line route is accepted");
                if (suffix == "35")
                {
                    yield return Wait(() => State.currentEntryId == "chapter04_story_36" && Link.DisplayedEntryId == State.currentEntryId,
                        "the restored final second-settlement line continues to the box section at its stable entry 36");
                    Check(State.currentVisit == continuedVisit + 1, "leaving the second settlement advances exactly one visit");
                    section5Rejoined = true;
                }
                if (suffix == "43")
                {
                    yield return Wait(() => State.currentEntryId == GraveId(1) && Link.DisplayedEntryId == State.currentEntryId,
                        "the box section reaches the new grave section directly at its stable first ID");
                    Check(State.currentVisit == continuedVisit + 1, "leaving the box section advances exactly one visit");
                    section6Rejoined = true;
                }
            }
            yield return null;
        }
        Check(graveVisited.SequenceEqual(Enumerable.Range(1, 16)), "the final section visits all sixteen lines once in author order");
        Check(State.heartWeightApplied == 17 && State.faultWeightApplied == 19, "the route consumes the complete 17/19 resource budget");
        Check(Heart() == Math.Min(State.initialHeart, 24) && Fault() == Math.Max(State.initialFault, 74),
            "terminal values reach heart 24 / fault 74 without reversing exceptional incoming values");
        observations.Add(new { type = "chapter resource outcome", State.initialHeart, State.initialFault,
            finalHeart = Heart(), finalFault = Fault(), State.heartWeightApplied, State.faultWeightApplied,
            heartCues = costs.Count(cue => cue.HeartWeight > 0), faultCues = costs.Count(cue => cue.FaultWeight > 0) });
        if (exerciseRestore)
        {
            Check(section2Visited.SequenceEqual(section2Order), "the played second section follows all twelve active lines in author order");
            Check(section2Selected.SequenceEqual(section2Order.Where(id => section2Options.ContainsKey(id)).Select(id => id + "/" + section2Options[id])),
                "the played second section consumes exactly its six authored player responses");
            Check(section3Visited.SequenceEqual(section3Order), "the played third section follows 20, 21, 22, 22a and rejoins the unchanged line 23");
            Check(section3Selected.SequenceEqual(new[] { "22/later", "22a/efficacy" }),
                "the played third section consumes its two authored player responses in order");
            Check(section4Visited.SequenceEqual(section4Order), "the first settlement plays all thirteen authored lines in order");
            Check(section4Selected.SequenceEqual(section4Order.Where(id => section4Options.ContainsKey(id)).Select(id => id + "/" + section4Options[id]))
                && section4Rejoined, "the first settlement consumes eight authored responses and joins line 27");
            Check(section5Visited.SequenceEqual(section5Order), "the second settlement plays all seven authored lines in order");
            Check(section5Selected.SequenceEqual(new[] { "28/think", "29/understand", "31/proud" }) && section5Rejoined,
                "the second settlement consumes three authored responses and joins line 36");
            Check(section6Visited.SequenceEqual(section6Order), "the box section plays all eight authored lines in order");
            Check(section6Selected.SequenceEqual(new[] { "36/rapport", "38/question" }) && section6Rejoined,
                "the box section consumes two authored responses and joins the grave section at grave_01");
        }
    }

    static IEnumerator VerifyMotherGiftGate(string id, bool exerciseRestore)
    {
        bool isKey = id == "chapter04_story_39";
        var expectedStage = isKey ? OSChapter04MotherGiftStage.KeyOffered : OSChapter04MotherGiftStage.Seen;
        string next = isKey ? "chapter04_story_40" : "chapter04_story_40a";
        yield return Wait(() => State.currentEntryId == id && story.MotherGiftStage == expectedStage
            && (isKey ? story.CanUseMotherKey && Ready(os.MotherKeyController?.Window)
                : story.CanContinue && Ready(os.MotherHeartController?.Window)),
            isKey ? "line 39 offers the usable real mother key" : "line 40 waits with the discovered heart fully open");
        Check(story.CurrentEntry.PartType == OSCharacterInteractionPartType.Text && !Link.IsWaitingForOption,
            "the mother-gift gate retains its authored plain text without adding a reply option");
        Check(isKey ? !story.CanContinue && !story.TryContinue() && !os.IsMotherHeartUnlocked
                && os.MotherHeartController?.Window.IsOpen != true
            : os.IsMotherHeartUnlocked && !os.MotherKeyController.Window.IsOpen
                && os.MotherHeartController.HeartImage?.texture != null,
            isKey ? "Continue cannot skip the unopened mother heart" : "Seen is backed by the visible production heart and a consumed key");
        if (exerciseRestore)
        {
            int waitingVisit = State.currentVisit;
            string waitingHistory = Round6History();
            yield return Hold(.5f);
            Check(State.currentEntryId == id && State.currentVisit == waitingVisit && Round6History() == waitingHistory
                && story.MotherGiftStage == expectedStage && (isKey ? story.CanUseMotherKey : story.CanContinue),
                "the completed mother-gift line waits for its actual player action without advancing itself");
            yield return RestoreCompletedLine(id, isKey ? "section6-39-key-offered" : "section6-40-heart-seen",
                OSChapter04PresentationBridge.LabNight, false, isKey);
            Check(story.MotherGiftStage == expectedStage && !os.CursorRelease.OwnsScriptActor(story)
                && (isKey ? Ready(os.MotherKeyController?.Window) && !story.CanContinue
                        && os.MotherHeartController?.Window.IsOpen != true
                    : Ready(os.MotherHeartController?.Window) && !os.MotherKeyController.Window.IsOpen
                        && os.MotherHeartController.HeartImage?.texture != null),
                "the saved gift phase restores its real window without replaying the companion entrance or bypassing discovery");
        }

        int visit = State.currentVisit, heart = Heart(), fault = Fault(), count = costs.Count;
        int heartWeight = State.heartWeightApplied, faultWeight = State.faultWeightApplied;
        bool boundaryObserved = false, boundaryValid = false;
        Action observeBoundary = () => {
            if (boundaryObserved || State.currentEntryId != next) return;
            boundaryObserved = true;
            boundaryValid = State.currentVisit == visit + 1
                && (isKey ? story.MotherGiftStage == OSChapter04MotherGiftStage.Seen && Ready(os.MotherHeartController?.Window)
                    : story.MotherGiftStage == OSChapter04MotherGiftStage.Closed
                        && os.MotherHeartController?.Window.IsOpen != true && os.MotherHeartController?.Window.IsClosing != true
                        && !os.CursorRelease.OwnsScriptActor(story));
            observations.Add(new { type = "mother-gift story boundary", previous = id, current = State.currentEntryId,
                State.currentVisit, stage = story.MotherGiftStage.ToString(), boundaryValid, frame = Time.frameCount });
        };
        story.ProgressChanged += observeBoundary;
        try
        {
            if (isKey)
            {
                Check(story.TryUnlockMotherHeart(), "the real mother-key handler opens the heart window");
                Check(State.currentEntryId == id && State.currentVisit == visit
                    && story.MotherGiftStage == OSChapter04MotherGiftStage.Unlocked
                    && os.MotherHeartController.Window.IsOpen && !os.MotherKeyController.Window.IsOpen
                    && !story.CanContinue && !story.TryUnlockMotherHeart(),
                    "unlock consumes the key once and retains line 39 until the heart is actually Ready/Seen");
            }
            else
            {
                Check(story.TryContinue(), "line 40 Continue starts the production companion close gesture");
                Check(State.currentEntryId == id && State.currentVisit == visit
                    && story.MotherGiftStage == OSChapter04MotherGiftStage.CloseRequested
                    && os.MotherHeartController.Window.IsOpen && !story.CanContinue && !story.TryContinue(),
                    "CloseRequested retains line 40 and blocks repeated Continue while the real heart is still open");
            }
            yield return Wait(() => State.currentEntryId == next && boundaryObserved,
                isKey ? "the real heart reveal advances to line 40" : "the actual heart close advances to line 40a", 15f);
            Check(boundaryValid && State.currentVisit == visit + 1,
                isKey ? "39 advances exactly once only after the heart is Ready/Seen"
                    : "40 advances exactly once only after the heart is Closed and the companion lease is released");
            Check(Heart() == heart && Fault() == fault && costs.Count == count
                && State.heartWeightApplied == heartWeight && State.faultWeightApplied == faultWeight,
                "the mother-gift window boundary preserves already-paid resources and emits no duplicate cost");
        }
        finally { story.ProgressChanged -= observeBoundary; }
    }

    static void CheckSection2Routes(OSChapter04StoryContent content)
    {
        for (int index = 0; index < section2Order.Length; index++)
        {
            string suffix = section2Order[index];
            var beat = content.Find(OSChapter04StoryContent.EntryPrefix + suffix);
            string next = OSChapter04StoryContent.EntryPrefix + (index + 1 < section2Order.Length ? section2Order[index + 1] : "20");
            Check(beat != null && !beat.legacyResumeOnly, "section two has active line " + suffix);
            if (section2Options.TryGetValue(suffix, out string option))
                Check(beat.options.Length == 1 && beat.options[0].optionId == option && beat.options[0].nextEntryId == next,
                    "section-two option route is exact: " + suffix + "/" + option);
            else Check(beat.options.Length == 0 && beat.nextEntryId == next, "section-two plain route is exact: " + suffix);
        }
        Check(content.Find("chapter04_story_15").faultWeight == 1 && content.Find("chapter04_story_18").faultWeight == 1
            && content.Find("chapter04_story_19").theatreStateAfterText == OSChapter04PresentationBridge.MedicineReady,
            "section two retains the two authored fault cues and medicine-ready ending");
        var legacy = content.Find("chapter04_story_16");
        Check(legacy.heartWeight == 0 && legacy.faultWeight == 0 && string.IsNullOrEmpty(legacy.theatreStateAfterText)
            && legacy.options.Length == 1 && legacy.options[0].optionId == "memories" && legacy.options[0].nextEntryId == "chapter04_story_17",
            "legacy line 16 can only resume through memories to 17 without charging resources or replaying a stage cue");
        var legacy30 = content.Find("chapter04_story_30");
        var legacy33 = content.Find("chapter04_story_33");
        Check(legacy30.legacyResumeOnly && legacy30.options.Length == 0 && legacy30.nextEntryId == "chapter04_story_31"
            && legacy33.legacyResumeOnly && legacy33.options.Length == 1 && legacy33.options[0].optionId == "answer"
            && legacy33.options[0].nextEntryId == "chapter04_story_34"
            && content.Beats.Where(beat => beat.legacyResumeOnly).All(beat => beat.heartWeight == 0 && beat.faultWeight == 0
                && string.IsNullOrEmpty(beat.theatreStateAfterText) && string.IsNullOrEmpty(beat.endAction)),
            "legacy lines 30 and 33 retain their exit routes and all legacy entries have no resource, stage or terminal side effects");
        Check(new[] { "42", "44" }.All(suffix => {
            var beat = content.Find(OSChapter04StoryContent.EntryPrefix + suffix);
            return beat.legacyResumeOnly && beat.options.Length == 0
                && beat.nextEntryId == OSChapter04StoryContent.EntryPrefix + (suffix == "42" ? "43" : "grave_01");
        }), "legacy lines 42 and 44 retain a plain continuation into their current successor sections");
        var path = new List<OSChapter04StoryContent.Beat>();
        string id = content.FirstEntryId;
        while (!string.IsNullOrEmpty(id) && path.Count <= content.Beats.Count)
        {
            var beat = content.Find(id);
            Check(beat != null && !beat.legacyResumeOnly && !path.Contains(beat) && beat.options.Length <= 1,
                "the authored route remains active and finite with no retired yes/no branch");
            path.Add(beat);
            id = beat.options.Length > 0 ? beat.options[0].nextEntryId : beat.nextEntryId;
        }
        Check(path.Count == 70 && path.Sum(beat => beat.heartWeight) == 17 && path.Sum(beat => beat.faultWeight) == 19
            && path[path.Count - 1].endAction == OSChapter04StoryContent.FailureWarningAction,
            "the complete route has 70 active lines, the full 17/19 budget and the player-confirmed warning ending");
    }

    static void CheckGraveRoutes(OSChapter04StoryContent content)
    {
        for (int line = 1; line <= 16; line++)
        {
            var beat = content.Find(GraveId(line));
            Check(beat != null && !beat.legacyResumeOnly && beat.options.Length == 0 && beat.autoAdvanceDelay == -1f
                && beat.nextEntryId == (line < 16 ? GraveId(line + 1) : string.Empty),
                "grave line " + line + " has a stable ID, no options and an explicit manual successor");
            Check(beat.griefTier == GraveTier(line) && (line < 13 || beat.griefLineStep == line - 13)
                && beat.heartWeight == graveHeartWeights[line - 1] && beat.faultWeight == graveFaultWeights[line - 1],
                "grave line " + line + " carries the exact authored tier, step and resource weights");
            string cue = line == 3 ? OSChapter04TheaterStage.FlowerWalkRain
                : line == 4 ? OSChapter04PresentationBridge.Grave : line == 6 ? OSChapter04TheaterStage.GravePlaceFlowers
                : line == 7 ? OSChapter04PresentationBridge.GraveBreakdown : string.Empty;
            Check((beat.theatreStateAfterText ?? string.Empty) == cue
                && (beat.endAction ?? string.Empty) == (line == 16 ? OSChapter04StoryContent.FailureWarningAction : string.Empty),
                "grave line " + line + " has only its authored post-text stage cue or terminal warning action");
        }
        foreach (string suffix in LegacyGraveSuffixes)
        {
            var route = content.FindResumeRoute(OSChapter04StoryContent.EntryPrefix + suffix);
            int next = suffix == "45" ? 4 : suffix == "46" ? 6 : 7;
            Check(route != null && route.nextEntryId == GraveId(next)
                && route.heartWeightBefore == 3 && route.faultWeightBefore == 3
                && OSChapter04PresentationBridge.IsLoopState(route.theatreState),
                "retired grave line " + suffix + " maps to the declared stable line and its preceding resource ledger");
        }
    }

    static int GraveLine(string id)
    {
        const string prefix = "chapter04_story_grave_";
        return id != null && id.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(id.Substring(prefix.Length), out int line) && line >= 1 && line <= 16 ? line : 0;
    }

    static string GraveId(int line) => OSChapter04StoryContent.EntryPrefix + "grave_" + line.ToString("00");
    static int GraveTier(int line) => line < 7 ? 0 : line < 10 ? 1 : line < 13 ? 2 : 3;
    static string GraveScene(int line) => line < 3 ? OSChapter04PresentationBridge.FlowerWalk
        : line == 3 ? OSChapter04TheaterStage.FlowerWalkRain : line < 6 ? OSChapter04PresentationBridge.Grave
        : line == 6 ? OSChapter04TheaterStage.GravePlaceFlowers : OSChapter04PresentationBridge.GraveBreakdown;

    static bool GraveViewReady(int line)
    {
        var view = Round6View;
        string expected = GraveScene(line);
        int tier = GraveTier(line);
        if (view == null || !view.isActiveAndEnabled || !view.IsTakenOver || view.IsPreviewing
            || view.IsAnimationPaused || view.UsesPendingSceneArtwork || !Ready(os.TVWindow)
            || !story.IsAvailable || State.currentEntryId != GraveId(line) || State.theatreState != expected
            || story.Presentation.CurrentState != expected || view.RequestedStateId != expected) return false;
        var stage = view.Stage;
        return !stage.IsHalted && !stage.HasQueuedRequest && stage.StateId == expected && stage.SceneState == expected
            && stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && stage.IsPuppetDrawn
            && stage.IsRaining == (line >= 3) && stage.FlowersPlaced == (line >= 6)
            && stage.GriefTier == tier && (tier == 0 || stage.GriefAngle == tier * 15)
            && (tier < 2 || stage.IsGriefLayerFlat(0))
            && (tier < 3 || stage.IsGriefLayerFlat(2) && stage.IsGriefLayerFlat(5));
    }

    static IEnumerator VerifyGraveLine(int line, bool exerciseRestore)
    {
        var beat = story.Content.Find(GraveId(line));
        Check(State.griefTier == beat.griefTier && State.griefLineStep == beat.griefLineStep
            && (OSChapter04GriefPresentation.Current?.Tier ?? 0) == beat.griefTier,
            "the completed grave line retains its authored frontend tier: " + line);
        if (line >= 13)
            yield return Wait(() => OSChapter04GriefPresentation.Current?.DarkLevel == line - 12,
                "tier three adds one persistent darkness step for completed grave line " + line, 3f);
        if (line == 16)
        {
            Check(State.completed && story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.WaitingForWarning
                && finalRequests == 0 && !story.CanContinue && !story.CanOpenFinalHeartHeal,
                "the sixteenth typed line enters the saved warning delay without opening treatment");
            Check(!story.TryContinue() && !story.TryOpenFinalHeartHeal(),
                "Continue and treatment reopen cannot skip the two-second warning gate");
            graveSnapshots[line] = Capture("grave-16-warning-delay");
            yield return null;
            CheckNoFinalContinue();
            yield return ScreenshotDialogue(GraveId(16), "grave-16-final-line.png");
            yield break;
        }
        if (!exerciseRestore || !graveRestoreLines.Contains(line)) yield break;
        yield return Wait(() => GraveViewReady(line), "the real grave TV reaches its final picture: " + line, 20f);
        string label = "grave-" + line.ToString("00") + "-completed";
        graveSnapshots[line] = Capture(label);
        yield return RestoreGraveSnapshot(graveSnapshots[line], line, label);
        if (line == 13) yield return ScreenshotDialogue(GraveId(line), "grave-13-third-tier.png");
    }

    static IEnumerator RestoreGraveSnapshot(string json, int line, string label)
    {
        int heart = Heart(), fault = Fault(), count = costs.Count, requestCount = requests.Count, treatmentRequests = finalRequests;
        var expected = JsonUtility.FromJson<GameSaveData>(json).osProcess.memory.chapter04Story;
        string history = HistoryJson(expected);
        yield return Restore(json, label);
        yield return Wait(() => GraveViewReady(line) && State.currentTextCompleted
            && (line == 16 || story.CanContinue), "the saved grave checkpoint restores its settled TV and input state: " + label, 20f);
        yield return Hold(.12f);
        int dark = line >= 13 ? line - 12 : 0;
        Check(State.currentEntryId == expected.currentEntryId && State.currentVisit == expected.currentVisit
            && State.currentCueCommitted && State.currentResourcesCommitted && Round6History() == history
            && State.griefTier == expected.griefTier && State.griefLineStep == expected.griefLineStep
            && (OSChapter04GriefPresentation.Current?.DarkLevel ?? 0) == dark,
            "the restored grave checkpoint retains its exact history, tier, step and darkness: " + label);
        Check(Heart() == heart && Fault() == fault && costs.Count == count && finalRequests == treatmentRequests
            && State.heartWeightApplied == expected.heartWeightApplied && State.faultWeightApplied == expected.faultWeightApplied,
            "the restored grave checkpoint repeats no resource charge or treatment request: " + label);
        Check(story.Presentation.CurrentRequest.IsRestore
            && requests.Skip(requestCount).All(request => request.IsRestore && request.StateId == GraveScene(line)),
            "the grave restore lands directly in the final picture without a fresh narrative cue: " + label);
        if (line == 3)
            Check(Round6View.Stage.SpireX == OSChapter04TheaterStage.SpireStopX,
                "the rainy walk restore lands with the distant spire already settled");
        if (line == 16)
        {
            Check(State.completed && State.finalTreatmentStage == expected.finalTreatmentStage,
                "the terminal grave save preserves its waiting-versus-visible warning phase: " + label);
            CheckNoFinalContinue();
        }
        observations.Add(new { type = "grave checkpoint restore", line, label, State.currentEntryId, State.currentVisit,
            State.theatreState, State.griefTier, State.griefLineStep, dark,
            finalTreatmentStage = State.finalTreatmentStage.ToString(), heart = Heart(), fault = Fault(), resourceEvents = costs.Count,
            stageAngle = Round6View.Stage.GriefAngle, raining = Round6View.Stage.IsRaining, flowersPlaced = Round6View.Stage.FlowersPlaced });
    }

    static IEnumerator VerifyGraveTypingRestore()
    {
        int visit = State.currentVisit, heart = Heart(), fault = Fault(), events = costs.Count, requestCount = requests.Count;
        int heartWeight = State.heartWeightApplied, faultWeight = State.faultWeightApplied;
        string history = Round6History();
        string snapshot = Capture("grave-13-typing");
        yield return Restore(snapshot, "grave-13-typing");
        yield return Wait(() => GraveViewReady(13) && Link.DisplayedEntryId == GraveId(13),
            "unfinished grave line thirteen restores its actual TV and typing presentation");
        Check(!State.currentTextCompleted && !State.currentCueCommitted && !State.currentResourcesCommitted
            && State.currentVisit == visit && State.griefTier == 3 && State.griefLineStep == 0
            && OSChapter04GriefPresentation.Current.DarkLevel == 0 && Round6History() == history,
            "unfinished tier-three restore has no completed-line darkness or extra history receipt");
        Check(Heart() == heart && Fault() == fault && costs.Count == events && State.heartWeightApplied == heartWeight
            && State.faultWeightApplied == faultWeight
            && requests.Skip(requestCount).All(request => request.IsRestore && request.StateId == OSChapter04PresentationBridge.GraveBreakdown),
            "unfinished grave-line restore emits no charge or fresh stage command");
    }

    static void CheckNoFinalContinue()
    {
        var view = story.GetComponent<OSChapter04ChatView>();
        var footer = Get(view, "footer") as RectTransform;
        Check(!story.CanContinue && !story.CanOpenFinalHeartHeal && footer != null
            && !footer.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(button => button.gameObject.activeInHierarchy),
            "the final waiting/warning state shows no Continue or treatment-reopen button");
    }

    static IEnumerator VerifyFailureWarning()
    {
        context = "grave failure warning";
        yield return Wait(() => story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FailureWarning
            && OSChapter04GriefPresentation.Current?.IsFailureWarningVisible == true,
            "the final two-second delay opens the production modal warning", 6f);
        float elapsed = Time.realtimeSinceStartup - graveFinalCompletedAt;
        Check(graveFinalCompletedAt > 0f && elapsed >= 1.9f && finalRequests == 0,
            "the warning follows two seconds of final-line reading and requests no treatment by itself");
        Check(OSChapter04GriefPresentation.Current.Tier == 3 && OSChapter04GriefPresentation.Current.DarkLevel == 4,
            "the visible warning sits over the final tier-three darkness");
        CheckNoFinalContinue();
        string warning = Capture("grave-failure-warning");
        observations.Add(new { type = "grave warning delay", elapsed, State.currentReadElapsed, heart = Heart(), fault = Fault() });
        yield return Hold(.35f);
        Check(story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FailureWarning && finalRequests == 0
            && !story.TryContinue() && !story.TryOpenFinalHeartHeal(),
            "an untouched warning remains modal and cannot open treatment through ordinary story inputs");

        yield return RestoreGraveSnapshot(graveSnapshots[16], 16, "grave-16-warning-delay");
        Check(!OSChapter04GriefPresentation.Current.IsFailureWarningVisible,
            "restoring the pre-warning save hides the warning until its saved timer expires");
        yield return Wait(() => story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FailureWarning
            && OSChapter04GriefPresentation.Current.IsFailureWarningVisible,
            "the saved final-line delay resumes and reaches the warning", 6f);
        yield return RestoreGraveSnapshot(warning, 16, "grave-visible-warning");
        yield return Wait(() => OSChapter04GriefPresentation.Current.IsFailureWarningVisible,
            "the warning save restores its visible modal without replaying the text");
        yield return ScreenshotDialogue(GraveId(16), "grave-failure-warning.png");
        Check(finalRequests == 0, "neither terminal restore has requested final treatment");

        var grief = OSChapter04GriefPresentation.Current;
        var button = grief.WarningButton;
        Check(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null,
            "the warning exposes its real enabled treatment button");
        Canvas.ForceUpdateCanvases();
        var pointer = new PointerEventData(EventSystem.current) {
            button = PointerEventData.InputButton.Left, position = ReplyScreenRect((RectTransform)button.transform).center
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject,
            "the warning button receives pointer input above the dark overlay and pinned windows");
        Check(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler),
            "a real warning-button pointer click requests the final treatment");
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(finalRequests == 1 && !grief.IsFailureWarningVisible && grief.Tier == 0 && grief.DarkLevel == 0
            && !grief.OwnsPointer && !grief.IsGlitchVisible,
            "confirmation requests treatment once, ignores a duplicate click and clears grief presentation");
    }

    static void CheckReplyLayout(OSChapter04StoryContent.Beat beat, string section = "section two")
    {
        Canvas.ForceUpdateCanvases();
        var view = story.GetComponent<OSChapter04ChatView>();
        var footer = Get(view, "footer") as RectTransform;
        Check(view != null && view.OwnsLayout && footer != null, "the authored choice uses the real Live replies footer");
        for (int index = 0; index < beat.options.Length; index++)
        {
            var label = footer.Find("Reply" + index + "/Label")?.GetComponent<TMP_Text>();
            string expected = System.Text.RegularExpressions.Regex.Replace(story.GetOptionText(beat.entryId, beat.options[index].optionId) ?? string.Empty, @"^\s*\d+\.\s*", string.Empty);
            Check(label != null && label.gameObject.activeInHierarchy && label.text == expected,
                "the real reply label shows the exact authored option: " + beat.options[index].optionId);
            label.ForceMeshUpdate();
            float width = label.rectTransform.rect.width, height = label.rectTransform.rect.height;
            Vector2 preferred = label.GetPreferredValues(label.text, label.enableWordWrapping ? width : float.PositiveInfinity, float.PositiveInfinity);
            bool fits = !label.isTextOverflowing && (label.enableWordWrapping
                ? preferred.y <= height + 1f : label.preferredWidth <= width + 1f && label.preferredHeight <= height + 1f);
            observations.Add(new { context, type = section + " actual reply layout", beat.entryId, option = beat.options[index].optionId,
                label.text, width, height, label.preferredWidth, label.preferredHeight, wrapped = label.enableWordWrapping, fits });
            Check(fits, "the complete player reply fits its real label rectangle: " + beat.options[index].optionId);
        }
    }

    static IEnumerator RestoreCompletedLine(string id, string label, string expectedScene, bool waitsForOption = true,
        bool waitsForMotherKey = false)
    {
        int visit = State.currentVisit, heart = Heart(), fault = Fault(), count = costs.Count, requestCount = requests.Count;
        int heartWeight = State.heartWeightApplied, faultWeight = State.faultWeightApplied;
        string history = Round6History();
        string snapshot = Capture(label);
        yield return Restore(snapshot, label);
        yield return Wait(() => story.IsAvailable && State.currentEntryId == id && Link.DisplayedEntryId == id
            && (waitsForMotherKey ? story.CanUseMotherKey : waitsForOption ? Link.IsWaitingForOption : story.CanContinue)
            && !story.IsOptionGestureBlocked,
            "the saved completed line resumes its original input at the exact entry: " + label);
        Check(State.currentVisit == visit && Round6History() == history && State.currentTextCompleted
            && State.currentCueCommitted && State.currentResourcesCommitted && Heart() == heart && Fault() == fault && costs.Count == count
            && State.heartWeightApplied == heartWeight && State.faultWeightApplied == faultWeight && State.theatreState == expectedScene
            && requests.Skip(requestCount).All(request => request.IsRestore && request.StateId == expectedScene),
            "the completed-line save keeps its current ID, history, resources and stage without replaying earlier dialogue: " + label);
    }

    static UnityEngine.UI.Button ReplyButton()
    {
        var footer = Get(story.GetComponent<OSChapter04ChatView>(), "footer") as RectTransform;
        var button = footer != null ? footer.Find("Reply0")?.GetComponent<UnityEngine.UI.Button>() : null;
        Check(button != null && button.IsActive() && button.IsInteractable(), "the real current reply button is visible and interactive");
        return button;
    }

    static IEnumerator CheckLongReplyHover(UnityEngine.UI.Button button, string id = "chapter04_story_25a",
        string expectedScene = OSChapter04PresentationBridge.TentCare, string screenshotName = "section4-25a-long-reply-hover.png")
    {
        Check(State.currentEntryId == id && State.theatreState == expectedScene, "the long reply belongs to the expected current dialogue and logical stage");
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
        var fill = button.transform.Find("HoverFill")?.GetComponent<UnityEngine.UI.Image>();
        var covered = fill != null ? fill.transform.Find("BlackLabel")?.GetComponent<TMP_Text>() : null;
        var hover = button.GetComponent<OSChapter04ChatReplyHover>();
        Check(label != null && covered != null && hover != null && fill.GetComponent<UnityEngine.UI.RectMask2D>() != null,
            "the multiline reply keeps its clipped contrast-text hover components");
        label.ForceMeshUpdate(); covered.ForceMeshUpdate();
        Color normal = new Color32(0x87, 0x82, 0x7e, 255);
        Check(rect.rect.height > 20f && label.enableWordWrapping && label.textInfo.lineCount > 1
            && label.maxVisibleCharacters >= label.textInfo.characterCount && !label.isTextOverflowing
            && label.color == normal && hover.FillAmount <= .001f && fill.rectTransform.rect.width <= .01f,
            "the long response wraps completely inside a taller button in its normal state");
        var pointer = ReplyPointer(button);
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerEnterHandler>(hits[0].gameObject) == button.gameObject
            && ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler),
            "the real raycast reaches the multiline reply hover handler");
        yield return Wait(() => hover.FillAmount >= .999f, "the multiline reply hover fills its interior", 3f);
        Canvas.ForceUpdateCanvases();
        label.ForceMeshUpdate(); covered.ForceMeshUpdate();
        if (expectedScene == OSChapter04PresentationBridge.NorthSettlement) yield return ScreenshotDialogue(id, screenshotName);
        else yield return ScreenshotRound6(id, expectedScene, screenshotName);
        Check(Mathf.Abs(fill.rectTransform.rect.width - (rect.rect.width - 2f)) < .01f
            && Mathf.Abs(fill.rectTransform.rect.height - (rect.rect.height - 2f)) < .01f
            && Vector2.Distance(fill.rectTransform.anchoredPosition, new Vector2(1f, -1f)) < .01f
            && !fill.raycastTarget && button.transition == UnityEngine.UI.Selectable.Transition.None,
            "multiline hover preserves the one-pixel border on all four sides without intercepting clicks");
        Check(label.color == Color.black && covered.color == Color.black && covered.text == label.text
            && covered.enableWordWrapping == label.enableWordWrapping && covered.textInfo.lineCount == label.textInfo.lineCount
            && covered.maxVisibleCharacters >= covered.textInfo.characterCount && !covered.isTextOverflowing
            && Vector2.Distance(label.rectTransform.rect.size, covered.rectTransform.rect.size) < .01f
            && Vector2.Distance(ReplyScreenRect(label.rectTransform).position, ReplyScreenRect(covered.rectTransform).position) < 1f,
            "the black hover copy matches the complete text, wrapping, width, height and position");
        observations.Add(new { context, type = "multiline reply hover", text = label.text, lines = label.textInfo.lineCount,
            buttonHeight = rect.rect.height, labelHeight = label.rectTransform.rect.height, labelWidth = label.rectTransform.rect.width,
            fillWidth = fill.rectTransform.rect.width, fillHeight = fill.rectTransform.rect.height });
        Check(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler), "the multiline reply handles pointer exit");
        yield return Wait(() => hover.FillAmount <= .001f && fill.rectTransform.rect.width <= .01f && label.color == normal,
            "leaving the multiline reply restores the empty fill and normal text color", 3f);
    }

    static void ClickReply(UnityEngine.UI.Button button)
    {
        Canvas.ForceUpdateCanvases();
        var pointer = ReplyPointer(button);
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var actual = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
        observations.Add(new { context, type = "multiline reply pointer click", expected = button.name, actual = actual?.name,
            hits = hits.Take(5).Select(hit => hit.gameObject.name).ToArray() });
        Check(actual == button.gameObject && ExecuteEvents.Execute(actual, pointer, ExecuteEvents.pointerClickHandler),
            "the topmost multiline reply receives its real pointer click");
    }

    static PointerEventData ReplyPointer(UnityEngine.UI.Button button) => new PointerEventData(EventSystem.current) {
        position = ReplyScreenRect((RectTransform)button.transform).center, button = PointerEventData.InputButton.Left, clickCount = 1
    };

    static Rect ReplyScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
    }

    static IEnumerator VerifyLegacyGraveRoutes()
    {
        foreach (string suffix in LegacyGraveSuffixes)
        {
            context = "legacy grave migration / " + suffix;
            int target = suffix == "45" ? 4 : suffix == "46" ? 6 : 7;
            int sourceLine = suffix == "45" ? 3 : target;
            var saved = JsonUtility.FromJson<GameSaveData>(graveSnapshots[sourceLine]);
            var legacy = saved.osProcess.memory.chapter04Story;
            var route = story.Content.FindResumeRoute(OSChapter04StoryContent.EntryPrefix + suffix);
            string oldId = OSChapter04StoryContent.EntryPrefix + suffix;
            legacy.currentEntryId = oldId;
            legacy.currentVisit++;
            legacy.completed = false;
            legacy.finalTreatmentStage = OSChapter04FinalTreatmentStage.None;
            legacy.currentTextCompleted = legacy.currentCueCommitted = true;
            legacy.currentResourcesPrepared = legacy.currentResourcesCommitted = true;
            // These obsolete prepared destinations must never be applied after
            // the save moves to a different authored line.
            legacy.currentHeartTarget = 0; legacy.currentFaultTarget = 100;
            legacy.currentReadElapsed = 1f;
            legacy.history.Add(new OSChapter04StoryRecord {
                visit = legacy.currentVisit, entryId = oldId, optionId = suffix == "49" ? "yes" : string.Empty
            });
            saved.osProcess.characterInteraction.BeginGlobalPresentation(oldId);
            int expectedVisit = legacy.currentVisit + 1;
            int heart = saved.osResources.Get(ResourceType.Heart), fault = saved.osResources.Get(ResourceType.Fault);
            string history = HistoryJson(legacy);
            string json = JsonUtility.ToJson(saved, true);
            File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-legacy-grave-" + suffix + "-input.json"), json);
            int count = costs.Count, presentationCount = requests.Count;
            int treatmentRequests = finalRequests, treatmentCompletions = finalCompletions;
            yield return Restore(json, "legacy-grave-" + suffix);
            yield return Wait(() => story.IsAvailable && State.currentEntryId == GraveId(target)
                && Link.DisplayedEntryId == GraveId(target) && !story.IsOptionGestureBlocked,
                "the retired grave ID resumes at its mapped active line: " + suffix);
            Check(State.currentVisit == expectedVisit && !State.currentTextCompleted && !State.currentCueCommitted
                && !State.currentResourcesPrepared && !State.currentResourcesCommitted && State.currentReadElapsed == 0f
                && State.heartWeightApplied == route.heartWeightBefore && State.faultWeightApplied == route.faultWeightBefore,
                "grave migration makes one new visit and resets only the target line's receipt ledger: " + suffix);
            Check(State.theatreState == route.theatreState && State.griefTier == GraveTier(target)
                && State.griefLineStep == story.Content.Find(GraveId(target)).griefLineStep
                && Round6History() == history && Heart() == heart && Fault() == fault && costs.Count == count,
                "grave migration preserves transcript and actual resources while restoring the target tier: " + suffix);
            Check(requests.Skip(presentationCount).All(request => request.IsRestore && request.StateId == route.theatreState)
                && finalRequests == treatmentRequests && finalCompletions == treatmentCompletions,
                "grave migration emits only the restored picture and no replayed treatment event: " + suffix);
            if (suffix == "49")
                Check(!string.IsNullOrWhiteSpace(story.GetOptionText(oldId, "yes"))
                    && !string.IsNullOrWhiteSpace(story.GetOptionText(oldId, "no")),
                    "both retired yes/no answer texts remain resolvable in historical chat records");
            Check(story.TryCompleteCurrentText(), "the mapped grave line types through the actual Live input: " + suffix);
            yield return Wait(() => State.currentTextCompleted && State.currentCueCommitted && State.currentResourcesCommitted
                && story.CanContinue, "the migrated grave line finishes with its new receipts: " + suffix);
            Check(Heart() == heart && Fault() == fault && costs.Count == count + (target == 7 ? 1 : 0),
                "finishing the migrated line does not charge resource values already paid in the source save: " + suffix);
            yield return RestoreCompletedLine(GraveId(target), "legacy-grave-" + suffix + "-migrated",
                GraveScene(target), false);
            Check(State.currentVisit == expectedVisit && State.currentEntryId == GraveId(target),
                "saving the migrated active ID prevents a second migration or extra visit: " + suffix);
            observations.Add(new { type = "legacy grave migration", oldId, targetId = GraveId(target),
                State.currentVisit, State.theatreState, State.griefTier, State.griefLineStep, heart = Heart(), fault = Fault() });
        }
    }

    static IEnumerator VerifyLegacyResume(string sourceSnapshot, string suffix, string successor, string optionId)
    {
        context = "legacy line " + suffix;
        string id = OSChapter04StoryContent.EntryPrefix + suffix, next = OSChapter04StoryContent.EntryPrefix + successor;
        string retiredReplyId = suffix == "30" || suffix == "33" ? "chapter04_story_29"
            : suffix == "42" || suffix == "44" ? "chapter04_story_39" : null;
        bool waitsForOption = !string.IsNullOrEmpty(optionId);
        Check(!string.IsNullOrEmpty(sourceSnapshot), "a production resource ledger is available for the isolated legacy-save fixture: " + suffix);
        var saved = JsonUtility.FromJson<GameSaveData>(sourceSnapshot);
        var legacy = saved.osProcess.memory.chapter04Story;
        legacy.currentEntryId = id; legacy.currentVisit++;
        legacy.currentTextCompleted = legacy.currentCueCommitted = legacy.currentResourcesPrepared = legacy.currentResourcesCommitted = true;
        legacy.currentReadElapsed = 0f;
        // Historical saves beyond the original line 30 already own this stage;
        // removing its new-route cue must not rewrite their saved stage or ledger.
        if (suffix == "30" || suffix == "33")
            legacy.theatreState = OSChapter04PresentationBridge.GirlHome;
        if (retiredReplyId != null)
            legacy.history.Single(record => record != null && record.entryId == retiredReplyId).optionId = "silence";
        legacy.history.Add(new OSChapter04StoryRecord { visit = legacy.currentVisit, entryId = legacy.currentEntryId });
        saved.osProcess.characterInteraction.BeginGlobalPresentation(legacy.currentEntryId);
        int eventsBeforeRestore = costs.Count;
        yield return Restore(JsonUtility.ToJson(saved), "legacy-" + suffix + "-fixture");
        yield return Wait(() => story.IsAvailable && State.currentEntryId == id && Link.DisplayedEntryId == id
            && (waitsForOption ? Link.IsWaitingForOption : story.CanContinue) && !story.IsOptionGestureBlocked,
            "the legacy line restores its real authored input state: " + suffix);
        Check(costs.Count == eventsBeforeRestore && Heart() == legacy.currentHeartTarget && Fault() == legacy.currentFaultTarget
            && State.heartWeightApplied == legacy.heartWeightApplied && State.faultWeightApplied == legacy.faultWeightApplied,
            "the first legacy restore preserves the saved resource targets without replaying a resource event");
        if (retiredReplyId != null)
        {
            yield return null;
            var root = Get(story.GetComponent<OSChapter04ChatView>(), "Root") as RectTransform;
            var replies = root != null ? root.GetComponentsInChildren<TMP_Text>().Where(text => text.name == "Text"
                && text.transform.parent?.parent?.name == "PlayerReply").ToArray() : null;
            Check(story.GetOptionText(retiredReplyId, "silence") == "...." && replies != null
                && replies.Any(text => text.text == "....") && replies.All(text => !string.IsNullOrWhiteSpace(text.text)),
                "the retired silence response resolves its original dots and restores no empty player bubbles: " + retiredReplyId);
        }
        int heart = Heart(), fault = Fault(), count = costs.Count, visit = State.currentVisit;
        int heartWeight = State.heartWeightApplied, faultWeight = State.faultWeightApplied;
        yield return RestoreCompletedLine(id, "legacy-" + suffix + (waitsForOption ? "-awaiting-option" : "-completed"), legacy.theatreState, waitsForOption);
        Check(waitsForOption ? story.TrySelectOption(optionId) : story.TryContinue(), "the actual legacy continuation is accepted: " + suffix);
        var resumedRecord = State.history.Single(item => item != null && item.visit == visit);
        Check(State.currentEntryId == next && State.currentVisit == visit + 1
            && (waitsForOption ? resumedRecord.optionId == optionId : string.IsNullOrEmpty(resumedRecord.optionId))
            && Heart() == heart && Fault() == fault && costs.Count == count
            && State.heartWeightApplied == heartWeight && State.faultWeightApplied == faultWeight,
            "legacy continuation rejoins the intended active line once and leaves already-paid resources unchanged: " + suffix + " -> " + successor);
        yield return Wait(() => State.currentEntryId == next && Link.DisplayedEntryId == next,
            "the legacy successor is presented by the real Live typewriter: " + successor);
    }


    static string Round6SceneFor(string id)
    {
        switch (id)
        {
            case "chapter04_story_19": return OSChapter04PresentationBridge.MedicineReady;
            case "chapter04_story_20": return OSChapter04PresentationBridge.WastelandWalk;
            case "chapter04_story_22": return OSChapter04PresentationBridge.SettlementWait;
            case "chapter04_story_24": return OSChapter04PresentationBridge.TentCare;
            case "chapter04_story_26": return OSChapter04PresentationBridge.WastelandWalk;
            default: return null;
        }
    }

    // Resolve the actual TV component again after every session restore; never install a QA bridge or preview.
    static OSChapter04TheaterView Round6View => os?.TVController?.Screen?.GetComponent<OSChapter04TheaterView>();

    static bool Round6Ready(string id, string expected)
    {
        var view = Round6View;
        if (view == null || !view.isActiveAndEnabled || !view.IsTakenOver || view.IsPreviewing
            || view.IsAnimationPaused || view.UsesPendingSceneArtwork || !Ready(os.TVWindow)
            || !story.IsAvailable || State.currentEntryId != id || State.theatreState != expected
            || story.Presentation.CurrentState != expected || view.RequestedStateId != expected) return false;
        var stage = view.Stage;
        return !stage.IsHalted && !stage.HasQueuedRequest && stage.StateId == expected && stage.SceneState == expected
            && stage.IsPuppetDrawn && (expected == OSChapter04PresentationBridge.MedicineReady
                ? stage.CurrentPhase == OSChapter04TheaterStage.Phase.Medicine && stage.IsHoldingVial
                    && stage.LastPuppetArt == OSChapter04TheaterStage.PuppetHoldVial
                : stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene);
    }

    static IEnumerator WaitRound6Scene(string id, string expected, string label)
    {
        yield return Wait(() => Round6Ready(id, expected), "real TV reaches its stable scene: " + label, 20f);
        int frame = Round6View.Stage.PhaseFrame;
        yield return Wait(() => Round6Ready(id, expected) && Round6View.Stage.PhaseFrame >= frame + 3,
            "real TV keeps animating its stable scene without fallback: " + label, 10f);
        RecordRound6Scene(id, label);
    }

    static void RecordRound6Scene(string id, string label)
    {
        var view = Round6View;
        var stage = view.Stage;
        observations.Add(new { type = "round6 production TV", id, label, State.currentVisit,
            State.theatreState, bridgeState = story.Presentation.CurrentState, view.RequestedStateId,
            view.UsesPendingSceneArtwork, phase = stage.CurrentPhase.ToString(), stage.PhaseFrame,
            stage.StateId, stage.SceneState, stage.QueuedState, stage.IsHoldingVial,
            stage.PuppetX, stage.LastPuppetArt, view.PixelScale,
            screenPixels = view.ScreenPixelSize.ToString(), historyCount = State.history.Count, resourceEvents = costs.Count });
    }

    static IEnumerator VerifyRound6Transition(string id, string expected)
    {
        yield return Wait(() => {
            var view = Round6View;
            return view != null && view.RequestedStateId == expected && !view.UsesPendingSceneArtwork
                && State.currentTextCompleted && State.currentCueCommitted && State.currentResourcesCommitted
                && (view.Stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut
                    || view.Stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneIn);
        }, "line 20 reaches the real scene transition before capture", 10f);
        RecordRound6Scene(id, "transition-before-restore");
        string snapshot = Capture("round6-20-transition");
        // No frame yield between Capture and Restore: this save genuinely interrupts the fold/rise.
        yield return RestoreRound6Scene(snapshot, id, expected, "round6-20-transition");
        round6TransitionRestored = true;
    }

    static IEnumerator VerifyRound6Scene(string id, string expected)
    {
        string label = "round6-" + id.Substring("chapter04_story_".Length) + "-" + expected;
        Check(story.Content.Find(id).theatreStateAfterText == expected,
            "round-six checkpoint matches its authored text cue: " + expected);
        yield return WaitRound6Scene(id, expected, label + "-before");
        yield return ScreenshotRound6(id, expected, label + "-before.png");
        string snapshot = Capture(label + "-stable");
        yield return RestoreRound6Scene(snapshot, id, expected, label + "-stable");
        yield return ScreenshotRound6(id, expected, label + "-restored.png");
        round6SceneChecks++;
    }

    static IEnumerator RestoreRound6Scene(string snapshot, string id, string expected, string label)
    {
        int visit = State.currentVisit, heart = Heart(), fault = Fault(), resourceEvents = costs.Count;
        int heartWeight = State.heartWeightApplied, faultWeight = State.faultWeightApplied;
        int presentationEvents = requests.Count;
        string history = Round6History();
        yield return Restore(snapshot, label); // Includes the existing Bind() and Live visibility restoration.
        yield return WaitRound6Scene(id, expected, label + "-restored");
        Check(State.currentEntryId == id && State.currentVisit == visit && State.currentTextCompleted
            && State.currentCueCommitted && State.currentResourcesCommitted,
            "scene restore retains the completed line and all committed receipts: " + label);
        Check(Round6History() == history
            && State.history.Count(record => record != null && record.visit == visit) == 1,
            "scene restore preserves exact chat history without a duplicate visit: " + label);
        Check(Heart() == heart && Fault() == fault && costs.Count == resourceEvents
            && State.heartWeightApplied == heartWeight && State.faultWeightApplied == faultWeight,
            "scene restore preserves resource values, weights and event count: " + label);
        Check(story.Presentation.CurrentRequest.IsRestore
            && requests.Skip(presentationEvents).All(request => request.IsRestore && request.StateId == expected),
            "scene restore emits no fresh narrative stage cue: " + label);
    }

    // JsonUtility restores a null string as empty. Compare the meaningful
    // visit/entry/option sequence while preserving order and duplicate checks.
    static string Round6History() => HistoryJson(State);
    static string HistoryJson(OSChapter04StoryState saved) => JsonConvert.SerializeObject(saved.history.Select(record => record == null ? null
        : new { record.visit, entryId = record.entryId ?? string.Empty, optionId = record.optionId ?? string.Empty }));

    static IEnumerator ScreenshotRound6(string id, string expected, string name)
    {
        Check(Round6Ready(id, expected), "real TV is stable before its full Game-view screenshot: " + name);
        Canvas.ForceUpdateCanvases();
        string path = Path.Combine(OutputDirectory, name);
        ScreenCapture.CaptureScreenshot(path);
        // Screenshot capture is asynchronous. Do not start Restore until the rendered file exists.
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 24,
            "full Game-view screenshot is written: " + name, 10f);
        Check(Round6Ready(id, expected), "real TV stays in the intended scene through screenshot capture: " + name);
        observations.Add(new { type = "round6 screenshot", id, expected, path, bytes = new FileInfo(path).Length });
    }

    static IEnumerator ScreenshotDialogue(string id, string name)
    {
        Check(story.IsAvailable && State.currentEntryId == id && State.currentTextCompleted
            && story.GetComponent<OSChapter04ChatView>()?.OwnsLayout == true,
            "the completed production dialogue is visible before its screenshot: " + name);
        Canvas.ForceUpdateCanvases();
        string path = Path.Combine(OutputDirectory, name);
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 24,
            "the actual dialogue screenshot is written: " + name, 10f);
        Check(story.IsAvailable && State.currentEntryId == id && State.currentTextCompleted,
            "the dialogue stays on the intended completed line through capture: " + name);
        observations.Add(new { type = "dialogue screenshot", id, path, State.theatreState,
            usesExistingPlaceholderArtwork = Round6View?.UsesPendingSceneArtwork, bytes = new FileInfo(path).Length });
    }

    static IEnumerator VerifyTreatment(string branch)
    {
        context = "final treatment / " + branch;
        Check(!string.IsNullOrEmpty(pendingSnapshot)
            && JsonUtility.FromJson<GameSaveData>(pendingSnapshot).osProcess.memory.chapter04Story.finalTreatmentStage == OSChapter04FinalTreatmentStage.Pending,
            "terminal action is saved as pending before the final-treatment screen opens");
        yield return Wait(() => story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FinalHeal,
            "terminal action opens the production final treatment");
        var treatment = story.FinalHeal;
        Check(treatment != null && treatment.IsRunning && !treatment.IsEnding
            && OSChapter04FinalHealPresentation.Current?.IsVisible == true,
            "the production final-treatment backend owns the full-screen presentation");
        Check(os.HeartHealToolWindowController?.GetComponent<OSWindowUnit>()?.IsOpen != true,
            "the legacy treatment tool stays closed during the final treatment");
        yield return null;
        CheckTreatmentInput();
        double runningTime = treatment.MusicTime;
        os.TVWindow.CloseSilently();
        Check(!os.TVWindow.IsOpen && story.CanOpenFinalHeartHeal && story.TryOpenFinalHeartHeal(),
            "the final-treatment reopen action accepts a closed TV background");
        Check(ReferenceEquals(treatment, story.FinalHeal) && treatment.IsRunning && treatment.MusicTime >= runningTime,
            "reopening a running final treatment preserves its director and music position");
        yield return Wait(() => Ready(os.TVWindow)
            && story.GetComponent<OSChapter04ChatView>()?.OwnsLayout == true,
            "the final-treatment reopen action restores both TV and the completed chat background");
        int requestsBefore = finalRequests;
        yield return Restore(pendingSnapshot, "pending-" + branch);
        yield return Wait(() => story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FinalHeal
            && story.FinalHeal?.IsRunning == true, "pending save opens final treatment after restoration");
        Check(finalRequests == requestsBefore, "restoring pending treatment does not replay the terminal request event");
        string opened = Capture("opened-" + branch);
        yield return Restore(opened, "opened-" + branch);
        treatment = story.FinalHeal;
        Check(story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.FinalHeal && treatment?.IsRunning == true,
            "the durable final-treatment state resumes through ordinary session restoration");
        // Drive real judgement with no input. The authored section-two targets
        // remain hanging, section three still recovers, and the last word must
        // wait for the player rather than automatically completing the chapter.
        double finalTime = treatment.Content.FinalNoteTime;
        for (double time = 0d; time <= finalTime + .5d; time += .05d)
            treatment.DebugStep(time, false, false);
        Check(treatment.IsWaitingForMother && !treatment.IsEnding && !story.IsFinalTreatmentComplete,
            "missing the entire treatment remains playable and waits for the final mother beat");
        var mothers = treatment.Content.Notes.Where(note => note.IsFinal).OrderBy(note => note.HitTime).ToArray();
        Check(mothers.Length == 2 && Math.Abs(mothers[0].HitTime - 105d) < .001d
            && Math.Abs(mothers[1].HitTime - 105.75d) < .001d,
            "production deployment uses two distinct music-aligned mother notes");
        int hitsBeforeMother = treatment.HitCount;
        treatment.DebugStep(finalTime + .6d, true, true);
        Check(treatment.IsWaitingForMother && !treatment.IsEnding && !story.IsFinalTreatmentComplete
            && treatment.HitCount == hitsBeforeMother + 1 && Heart() < 100,
            "the first waiting mother input resolves one note without curing or committing the ending");
        yield return null;
        treatment.DebugStep(finalTime + 1.6d, true, true);
        yield return Wait(() => story.IsFinalTreatmentComplete && story.FinalTreatmentStage == OSChapter04FinalTreatmentStage.Ending
            && treatment.IsEnding, "the second mother input completes treatment and enters the durable ending");
        Check(Heart() == 100 && Fault() == 0,
            "the real final-treatment completion fills the HUD heart and clears fault");
        int completionsBefore = finalCompletions;
        string completed = Capture("treatment-complete-" + branch);
        yield return Restore(completed, "treatment-complete-" + branch);
        yield return Wait(() => story.IsFinalTreatmentComplete && story.FinalHeal?.IsEnding == true,
            "a healed save enters the ending directly after restoration");
        Check(!story.TryOpenFinalHeartHeal() && finalCompletions == completionsBefore
            && Heart() == 100 && Fault() == 0,
            "a healed save cannot restart treatment, replay its completion, or duplicate settlement");
        var epilogue = OSChapter04FinalHealPresentation.Current;
        Check(epilogue != null && epilogue.EpiloguePhaseIndex == 3 && epilogue.EpilogueStepIndex == 0
            && epilogue.EpilogueChoicesMade == 0 && epilogue.EpilogueSinkProgress == 1f
            && epilogue.EpilogueText == data.GetLocalizedText("chapter04.final_heal.epilogue.line_01"),
            "healed deployment restore starts the first epilogue line on full black with Live open");
    }

    static void CheckTreatmentInput()
    {
        Canvas.ForceUpdateCanvases();
        var presentation = OSChapter04FinalHealPresentation.Current;
        var rect = presentation?.LayerRect;
        Check(rect != null && EventSystem.current != null, "final treatment has its real full-screen input layer");
        var canvas = rect.GetComponentInParent<Canvas>();
        var rootCanvas = canvas != null ? canvas.rootCanvas : null;
        var camera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
        var pointer = new PointerEventData(EventSystem.current) {
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)),
            button = PointerEventData.InputButton.Left
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        observations.Add(new { context, type = "treatment input", position = pointer.position.ToString(),
            camera = camera != null ? camera.name : null,
            hits = hits.Take(10).Select(hit => new { name = hit.gameObject.name,
                parent = hit.gameObject.transform.parent.name, hit.sortingOrder, hit.depth,
                receiver = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject)?.name }).ToArray(),
            canvases = os.CharacterWindowController.GetComponentsInParent<Canvas>(true)
                .Select(value => new { value.name, value.overrideSorting, value.sortingOrder }).ToArray() });
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject) == rect.gameObject,
            "the final-treatment layer receives pointer input above the attached Live conversation");
        int presses = 0;
        Action<Vector2> onPress = _ => presses++;
        presentation.Pressed += onPress;
        try
        {
            Check(ExecuteEvents.Execute(rect.gameObject, pointer, ExecuteEvents.pointerDownHandler) && presses == 1,
                "a real pointer-down reaches the final-treatment Pressed event once");
        }
        finally { presentation.Pressed -= onPress; }
    }

    static void Bind()
    {
        os = OS_Handle.Instance; story = os?.Chapter04Story;
        Check(os != null && story != null && os.Chapter04Entry != null && os.TVWindow != null, "chapter runtime resolves");
        if (observedStory != story)
        {
            Unbind(); observedStory = story;
            story.ResourceCueCommitted += OnCost;
            story.FinalTreatmentRequested += OnFinalRequested;
            story.FinalTreatmentCompleted += OnFinalCompleted;
        }
        if (observedBridge != story.Presentation)
        {
            if (observedBridge != null) observedBridge.PresentationRequested -= OnRequest;
            observedBridge = story.Presentation;
            if (observedBridge != null) observedBridge.PresentationRequested += OnRequest;
        }
        story.DebugSetContent(null);
        GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false); Focus();
    }
    static void Unbind()
    {
        if (observedStory != null)
        {
            observedStory.ResourceCueCommitted -= OnCost;
            observedStory.FinalTreatmentRequested -= OnFinalRequested;
            observedStory.FinalTreatmentCompleted -= OnFinalCompleted;
        }
        if (observedBridge != null) observedBridge.PresentationRequested -= OnRequest;
        observedStory = null; observedBridge = null;
    }
    static void OnCost(OSChapter04StoryDirector.ResourceCue cue)
    {
        costs.Add(cue);
        if (cue.EntryId == GraveId(16) && graveFinalCompletedAt <= 0f)
            graveFinalCompletedAt = Time.realtimeSinceStartup;
    }
    static void OnRequest(OSChapter04PresentationBridge.PresentationRequest request)
    {
        requests.Add(request);
        observations.Add(new { context, request.StateId, request.IsRestore, request.Revision });
    }
    static void OnFinalRequested() { finalRequests++; pendingSnapshot = Capture("pending-request-" + finalRequests); }
    static void OnFinalCompleted() => finalCompletions++;
    static string Capture(string label)
    {
        OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-" + label + ".json"), json); return json;
    }
    static IEnumerator Restore(string json, string label)
    {
        ReleaseInput(); story.ResetPresentation(); os.Chapter04Entry.ResetPresentation(); Link.NotifyOSHidden();
        var saved = JsonUtility.FromJson<GameSaveData>(json);
        data.ReplaceSaveData(saved); yield return OSSessionPersistence.Restore(saved); Bind();
        Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        yield return null; yield return null;
        Check(ReferenceEquals(State, saved.osProcess.memory.chapter04Story) && !OSSessionPersistence.IsRestoring,
            "real session restoration completed: " + label);
    }
    static void Focus()
    {
        mouse?.MakeCurrent(); keyboard?.MakeCurrent();
        foreach (var target in new MonoBehaviour[] { story, story?.FinalHeal, os?.Chapter04Entry, os?.Chapter03Story, os?.Chapter03Entry,
            os?.Chapter02FinalTalk, os?.CursorRelease, os?.GetComponent<OSChapter02NightfallPresentation>() })
        {
            if (target == null || !(Get(target, "hasFocus") is bool value)) continue;
            if (!focusStates.ContainsKey(target)) focusStates.Add(target, value);
            Set(target, "hasFocus", true);
        }
        story?.DebugSetFocus(true);
        story?.FinalHeal?.DebugSetFocus(true);
    }
    static void ReleaseInput()
    {
        if (mouse != null) InputState.Change(mouse, new MouseState { position = new Vector2(-10000, -10000) }, InputState.currentUpdateType);
        if (keyboard != null) InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType);
    }
    static int Heart() => data.GetOSResourceValue(ResourceType.Heart);
    static int Fault() => data.GetOSResourceValue(ResourceType.Fault);
    static IEnumerator Hold(float seconds)
    { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
        Check(predicate(), label);
    }
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing && !window.IsMinimized;
    static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static void CaptureError(string condition, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + condition + "\n" + trace); }
    static string Describe() => "entry=" + story?.State?.currentEntryId + ", displayed=" + os?.LinkMessageController?.DisplayedEntryId
        + ", available=" + story?.IsAvailable + ", stage=" + story?.State?.theatreState + ", content=" + story?.ContentError;
    static object Get(object target, string name) => target?.GetType().GetField(name, Members)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Members)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
}
