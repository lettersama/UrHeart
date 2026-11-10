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
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Opt-in integration test. Only the initial checkpoint prepares save state;
// thereafter production controls drive Tool, Energy, PaperPush and Memory.
// No messages are directly presented and no interaction receipts are edited.
public static class OSChapter01ToolFlowQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> trace = new List<object>();
    static readonly Dictionary<string, int> starts = new Dictionary<string, int>();
    static OS_Handle os;
    static OSLinkMessageController live;
    static OSToolWindowController tool;
    static EditorWindow gameView;
    static string context, lastSnapshot;
    static bool cancelled;
    static float nextSample;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static string CurrentStep => context;
    public static void Cancel() => cancelled = true;

    public static string Start()
    {
        if (Status == "running") return Status;
        if (!Application.isPlaying || GameDataManager.Instance == null || GameTest.Instance == null
            || GameAutosaveService.Instance == null || GameManager.Instance.IsTransitioning)
            return "Initialized Play Mode required";
        gameView = Resources.FindObjectsOfTypeAll<EditorWindow>()
            .FirstOrDefault(w => w.GetType().FullName == "UnityEditor.GameView");
        if (gameView == null) return "Open Game view required";
        checks.Clear(); trace.Clear(); starts.Clear(); cancelled = false; lastSnapshot = null;
        os = null; live = null; tool = null; context = "checkpoint"; nextSample = 0f;
        OutputDirectory = Path.GetFullPath("Tools/Chapter01Tools/Validation/" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
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
        bool background = Application.runInBackground;
        var oldFocus = EditorWindow.focusedWindow;
        var paths = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        var hashes = paths.Select(Hash).ToArray();
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();
            string storage = Path.GetFullPath("Temp/OSChapter01ToolFlowQA/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storage);
            saves.UseIsolatedStorageForTests(storage); saves.EnterTestSession();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            Application.runInBackground = true; gameView.Focus();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (cancelled) throw new OperationCanceledException();
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Sample(true);
                    ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "failure.png"));
                    Status = "FAILED at " + context + ": " + error;
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " checks";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (live != null)
                {
                    live.EntryPresentationStarted -= OnStarted;
                    live.EntryTextPlaybackCompleted -= OnTyped;
                    live.NotifyOSHidden();
                }
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                Application.runInBackground = background;
                if (oldFocus != null) oldFocus.Focus();
                Check(paths.Select(Hash).SequenceEqual(hashes), "original save hashes preserved after cleanup");
            }
            catch (Exception error) { Status += "\nCleanup: " + error; }
            if (Status == "running") Status = "Interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(
                new { status = Status, context, checks, trace, originalHashes = hashes, finalHashes = paths.Select(Hash).ToArray() }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeToolUnlock), "tool-unlock checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning && !SceneLoadManager.Instance.IsBusy, "checkpoint settled", 65f);
        Check(GameTest.Instance.JumpSucceeded, "checkpoint succeeded");
        os = OS_Handle.Instance; live = os.LinkMessageController;
        tool = os.GetComponentInChildren<OSToolWindowController>(true);
        live.EntryPresentationStarted += OnStarted;
        live.EntryTextPlaybackCompleted += OnTyped;
        Check(!GameDataManager.Instance.IsToolUnlocked, "Tool starts locked");
        context = "memory 1260 / Tool introduction";
        yield return Wait(() => live.DisplayedEntryId == OSCharacterInteractionIds.Chapter01Tool.FilesFound
            && live.IsWaitingForOption, "files-found question appears", 25f);
        ClickLive("start_processing");
        yield return Wait(() => live.DisplayedEntryId == OSCharacterInteractionIds.Chapter01Tool.AwaitShortcut,
            "processing naturally reaches Ctrl+R hint", 25f);
        Check(os.TryOpenToolFromShortcut(), "ordinary Ctrl+R handler opens Tool");
        yield return Wait(() => tool.IsReady, "Tool initialization completes", 30f);
        yield return Wait(() => live.DisplayedEntryId == OSCharacterInteractionIds.Chapter01Tool.FollowUp,
            "unpack command hint appears", 25f);
        yield return InstallCommand("tool unpack --memory");
        yield return Wait(() => tool.ProgramLibrary.HasUnpackedThisSession, "archive unpack finishes", 15f);
        yield return Wait(() => live.DisplayedEntryId == OSCharacterInteractionIds.Chapter01Tool.DecorationPrompt
            && live.IsWaitingForOption, "decoration choice appears", 20f);
        ClickLive("skip_decoration");
        yield return Install(OSToolProgramIds.MemoryRepair01);

        context = "gramophone installed / first charging overlaps Live";
        yield return Install(OSToolProgramIds.Gramophone);
        yield return Wait(() => live.DisplayedEntryId == "gramophone_first_open", "gramophone installation speaks without opening player", 18f);
        Check(!((OSWindowUnit)Get(os, "gramophoneWindow")).IsOpen, "gramophone message precedes first player window open");
        yield return Install(OSToolProgramIds.EnergyLoop);
        var energyIcon = (DesktopProcessUnit)Get(os, "energyLoopDesktopProcess");
        energyIcon.OnPointerClick(Pointer(2));
        var energy = os.GetComponent<OSEnergyLoopController>();
        var energyWindow = (OSWindowUnit)Get(energy, "window");
        yield return Wait(() => energyWindow.IsOpen && !energyWindow.IsOpening, "Energy opens from desktop");
        context = "first charge / discover PaperPush";
        ClickButton((Button)Get(energy, "startButton"));
        Check(GameDataManager.Instance.OSEnergyLoopState.isCharging, "first charge starts via button");
        var state = GameDataManager.Instance.OSCharacterInteractionState;
        Check(state.presentingGlobalEntryId == OSEnergyLoopController.DiscoveryEntry
            || state.pendingGlobalEntryId == OSEnergyLoopController.DiscoveryEntry
            || state.optionCountdownQueue.Any(q => q.entryId == OSEnergyLoopController.DiscoveryEntry),
            "first charge submits discovery in the same frame");
        yield return Wait(() => live.DisplayedEntryId == OSEnergyLoopController.DiscoveryEntry
            && live.IsWaitingForOption, "charging discovery reaches selectable Live message", 25f);
        yield return Capture("first_charge_live.png");
        ClickLive(OSEnergyLoopController.FindOption);
        yield return Wait(() => energy.IsDiscoveryVisible, "Find reveals the game entry");
        ClickButton((Button)Get(energy, "discoveryButton"));
        var paper = os.GetComponent<OSPaperPushWindowController>();
        var paperWindow = (OSWindowUnit)Get(paper, "window");
        yield return Wait(() => paperWindow.IsOpen && !paperWindow.IsOpening, "discovered PaperPush opens");
        Check(GameDataManager.Instance.IsPaperPushUnlocked, "PaperPush desktop entry unlocked");
        ClickButton((Button)Get(paper, "startButton"));
        yield return Wait(() => paper.Phase == OSPaperPushWindowController.MatchPhase.Choosing, "first match reaches move choice", 20f);
        Check(paper.MatchStage == 1 && GameDataManager.Instance.CurrentChapterIndex == 1,
            "charging launches chapter-one first match");
        yield return Capture("first_paper_push.png");
        ClickButton((Button)Get(paper, "heavyButton"));
        yield return Wait(() => paper.RoundNumber >= 2 || paper.Phase == OSPaperPushWindowController.MatchPhase.Finished,
            "first PaperPush round resolves through gameplay", 25f);
        context = "complete first PaperPush match / reaction queue";
        int rounds = 0;
        while (paper.Phase != OSPaperPushWindowController.MatchPhase.Finished && rounds++ < 24)
        {
            yield return Wait(() => paper.Phase == OSPaperPushWindowController.MatchPhase.Choosing
                || paper.Phase == OSPaperPushWindowController.MatchPhase.Finished,
                "next first-match choice becomes available", 20f);
            if (paper.Phase == OSPaperPushWindowController.MatchPhase.Finished) break;
            int round = paper.RoundNumber;
            // A fixed player strategy exercises real combat and chapter-one
            // assistance without inspecting or changing the opponent's move.
            ClickButton((Button)Get(paper, "heavyButton"));
            yield return Wait(() => paper.RoundNumber > round
                || paper.Phase == OSPaperPushWindowController.MatchPhase.Finished,
                "first-match round resolves: " + round, 20f);
        }
        var energyState = GameDataManager.Instance.OSEnergyLoopState;
        Check(paper.Phase == OSPaperPushWindowController.MatchPhase.Finished
            && energyState.chapter01PaperPushWins == 1, "first match completes with a recorded victory");
        string defeat = OSPaperPushStoryDirector.EntryId(1, "defeat");
        yield return Wait(() => live.DisplayedEntryId == defeat && !live.IsPresenting,
            "first-match ending reaction finishes typing", 45f);
        yield return Capture("first_paper_push_victory_live.png");
        yield return Wait(() => energyState.paperPushReactionQueue.Count == 0
            && !live.IsPresenting && !live.LinkWindow.IsOpen, "first-match reaction queue drains", 25f);
        Check(Count(OSPaperPushStoryDirector.EntryId(1, "counter")) == 1 && Count(defeat) == 1,
            "first-match counter and ending reactions each play once");
        Check(energyState.paperPushReactionHistory.All(id => Count(id) == 1),
            "all triggered first-match reactions play exactly once");
        paperWindow.Close();
        yield return Wait(() => Count(OSPaperPushStoryDirector.EntryId(1, "reopen")) == 1,
            "closing the finished first match triggers the rematch invitation", 20f);
        var paperStory = os.GetComponent<OSPaperPushStoryDirector>();
        yield return Wait(() => energyState.paperPushFirstRematchDone && !paperStory.IsTakingOver
            && paper.Phase == OSPaperPushWindowController.MatchPhase.Choosing,
            "Live completes the authored reopen-and-start handoff", 35f);
        Check(paper.MatchStage == 2, "rematch handoff advances to stage two");
        paperWindow.Close(); energyWindow.Close();

        context = "normal memory reading to upgrade 2";
        Check(os.OpenMemoryWindow() || os.MemoryWindowController.GetComponent<OSWindowUnit>().IsOpen, "Memory reopened");
        var memory = os.MemoryWindowController;
        float readingBudget = 160f;
        while (memory.CurrentData.DataIndex != 2670 && readingBudget > 0f)
        {
            os.NotifyWindowPointerPressed(memory.GetComponent<OSWindowUnit>());
            if (live.IsWaitingForOption) ClickFirstLive();
            if (!memory.IsCurrentTextComplete || memory.CanContinue) memory.TryAdvance();
            else if (!memory.IsSubwindowPresentationPending && !memory.IsSubwindowInputLocked)
            {
                var option = memory.SelectUnits.FirstOrDefault(u => u.gameObject.activeInHierarchy && u.IsInteractable);
                if (option != null) option.OnPointerClick(Pointer());
            }
            yield return Hold(.18f);
            readingBudget -= .18f;
        }
        Check(memory.CurrentData.DataIndex == 2670, "actual memory links/options reach upgrade-2 gate");
        os.NotifyWindowPointerPressed(memory.GetComponent<OSWindowUnit>());
        memory.TryAdvance();
        // Reaching the gate while the preceding optional memory question is
        // still typing is a real reading route. Dismiss that question as a
        // player can, without selecting a reply or awarding its resources.
        yield return Wait(() => live.IsWaitingForOption || Count("chapter01_memory_upgrade_02") > 0,
            "preceding memory commentary finishes typing", 15f);
        if (live.IsWaitingForOption)
        {
            int heart = GameDataManager.Instance.GetOSResourceValue(ResourceType.Heart);
            live.LinkWindow.Close();
            Check(GameDataManager.Instance.GetOSResourceValue(ResourceType.Heart) == heart,
                "dismissing optional memory question gives no answer reward");
        }
        yield return Wait(() => Count("chapter01_memory_upgrade_02") > 0, "upgrade-2 gate prompts Tool", 20f);
        yield return Wait(() => live.DisplayedEntryId == "chapter01_memory_upgrade_02" && !live.IsPresenting,
            "upgrade-2 guidance finishes typing", 15f);
        yield return Capture("upgrade2_live.png");
        Check(os.OpenToolWindow() || tool.GetComponent<OSWindowUnit>().IsOpen, "Tool reopened for upgrade");
        os.NotifyWindowLauncherPressed(tool.GetComponent<OSWindowUnit>());
        yield return Wait(() => Count("chapter01_memory_upgrade_02_tool_opened") > 0, "Tool opening prompts upgrade energy cost", 20f);
        yield return Install(OSToolProgramIds.MemoryRepair02);
        yield return Wait(() => Count("chapter01_memory_upgrade_02_installed") > 0, "upgrade installation confirmation reaches Live", 20f);
        yield return Wait(() => !live.LinkWindow.IsOpen && !live.IsPresenting, "final reading hold closes", 25f);
        yield return Hold(1f);
        state = GameDataManager.Instance.OSCharacterInteractionState;
        Check(state.optionCountdownQueue.Count == 0 && string.IsNullOrEmpty(state.pendingEntryId)
            && string.IsNullOrEmpty(state.pendingGlobalEntryId) && string.IsNullOrEmpty(state.optionCountdownReleaseKey),
            "Live queue and both pending slots drain before leaving chapter one");
        Check(Count("gramophone_first_open") == 1 && Count(OSEnergyLoopController.DiscoveryEntry) == 1,
            "gramophone and first-charge messages occur once each");
        Sample(true);
    }

    static IEnumerator Install(string id)
    {
        context = "install " + id;
        GameDataManager.Instance.OSToolProgramData.TryGetProgram(id, out var program);
        Check(program != null, "package exists: " + id);
        tool.ProgramLibrary.SelectProgram(program);
        yield return InstallCommand(program.InstallCommand);
        yield return Wait(() => GameDataManager.Instance.IsToolProgramInstalled(id) && !tool.ProgramLibrary.IsInstalling,
            "installation completes: " + id, 12f);
    }
    static IEnumerator InstallCommand(string command)
    {
        os.NotifyWindowPointerPressed(tool.GetComponent<OSWindowUnit>());
        yield return Hold(.2f);
        Check(tool.SubmitCommand(command), "normal terminal accepts " + command);
    }
    static PointerEventData Pointer(int count = 1) => new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left, clickCount = count };
    static void ClickButton(Button button)
    {
        Check(button != null && button.gameObject.activeInHierarchy && button.IsInteractable(), "button active: " + button?.name);
        button.OnPointerClick(Pointer());
    }
    static void ClickLive(string optionId)
    {
        var options = (Dictionary<OSLinkMessageSelectUnit, OSCharacterInteractionOptionData>)Get(live, "activeOptions");
        var target = options.FirstOrDefault(x => x.Value.OptionId == optionId).Key;
        Check(live.IsWaitingForOption && target != null && target.IsInteractable && target.gameObject.activeInHierarchy,
            "Live choice available: " + optionId);
        target.OnPointerClick(Pointer());
    }
    static void ClickFirstLive()
    {
        var options = (Dictionary<OSLinkMessageSelectUnit, OSCharacterInteractionOptionData>)Get(live, "activeOptions");
        var target = options.Keys.FirstOrDefault(x => x.gameObject.activeInHierarchy && x.IsInteractable);
        if (target != null) target.OnPointerClick(Pointer());
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 10f)
    {
        while (!predicate() && seconds > 0f)
        { yield return Frame(); seconds -= Time.unscaledDeltaTime; }
        Check(predicate(), label);
    }
    static IEnumerator Hold(float seconds)
    {
        while (seconds > 0f) { yield return Frame(); seconds -= Time.unscaledDeltaTime; }
    }
    static IEnumerator Frame()
    {
        do { yield return null; Sample(); } while (!Application.isFocused);
    }
    static void Sample(bool force = false)
    {
        if (live == null || (!force && Time.realtimeSinceStartup < nextSample)) return;
        nextSample = Time.realtimeSinceStartup + .1f;
        var s = GameDataManager.Instance.OSCharacterInteractionState;
        string snap = JsonConvert.SerializeObject(new { context, entry = live.DisplayedEntryId, open = live.LinkWindow.IsOpen,
            s.activeSequenceId, s.presentingEntryId, s.presentingGlobalEntryId, s.pendingEntryId, s.pendingGlobalEntryId,
            s.optionCountdownQueue, s.optionCountdownReleaseKey, s.nextEntryCountdownKey,
            memory = os.MemoryWindowController.CurrentData?.DataIndex, live.IsWaitingForOption,
            live.IsWaitingForExternalCompletion, locked = os.IsSystemInputLocked,
            focused = Application.isFocused, readingHold = Mathf.Round(live.ReadingHoldRemaining * 10f) / 10f });
        if (force || snap != lastSnapshot)
        { trace.Add(new { kind = "state", time = Time.realtimeSinceStartup, state = JsonConvert.DeserializeObject(snap) }); lastSnapshot = snap; }
    }
    static void OnStarted(string sequence, OSCharacterInteractionEntryData entry)
    { starts[entry.EntryId] = Count(entry.EntryId) + 1; trace.Add(new { kind = "started", time = Time.realtimeSinceStartup, context, id = entry.EntryId }); }
    static void OnTyped(string sequence, OSCharacterInteractionEntryData entry)
        => trace.Add(new { kind = "typed", time = Time.realtimeSinceStartup, context, id = entry.EntryId });
    static int Count(string id) => starts.TryGetValue(id, out int n) ? n : 0;
    static IEnumerator Capture(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, name));
        yield return new WaitForEndOfFrame();
        yield return null;
    }
    static object Get(object obj, string field) => obj.GetType().GetField(field, Fields).GetValue(obj);
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
    static string Hash(string path)
    { if (!File.Exists(path)) return "missing"; using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
}
