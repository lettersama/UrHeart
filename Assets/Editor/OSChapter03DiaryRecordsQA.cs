using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.OS;
using MrTGame.Systems;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in Play Mode integration checks. Only cloned saves in isolated temporary
// storage are changed. Story execution is disabled after the real Home is ready:
// CollectPaper still drives the production record/toast APIs, while its authored
// queues are cleared synchronously so diary 13 cannot start a later sequence.
public static class OSChapter03DiaryRecordsQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] FoundOrder = { "diary_07", "diary_02", "diary_08", "diary_06", "diary_05", "diary_10", "diary_13", "diary_04", "diary_03" };
    static readonly string[] DateOrder = { "diary_02", "diary_03", "diary_04", "diary_08", "diary_07", "diary_06", "diary_05", "diary_10", "diary_13" };
    static readonly List<string> checks = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<int> unlocked = new List<int>(), updated = new List<int>();
    static readonly List<string> screenshots = new List<string>();
    static GameSetup setup;
    static Mouse testMouse;
    static GameDataManager data;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSRecordWindowController records;
    static OSWindowUnit recordWindow;
    static GameSaveData baseline;
    static bool cancel, storyWasEnabled;
    static string context;
    static OSChapter03StoryState S => data.SaveData.osProcess.memory.chapter03Story;
    static OSChapter03StoryContent Content => OSChapter03StoryContent.Instance;
    static AdditionHandle Tips => AdditionHandle.Instance;
    static TMP_Text Body => Get(records, "detailText") as TMP_Text;
    static TMP_Text Title => Get(records, "detailTitle") as TMP_Text;
    static ScrollRect Scroll => Body.GetComponentInParent<ScrollRect>(true);
    static int Diary => OSChapter03DiaryRecord.RecordIndex;

    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start()
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
        checks.Clear(); observations.Clear(); unlocked.Clear(); updated.Clear(); screenshots.Clear();
        cancel = false; data = GameDataManager.Instance;
        os = null; memory = null; home = null; story = null; records = null; baseline = null;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03DiaryRecordsQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded());
        return Status;
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
        var previousMouse = Mouse.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp", "Assets/Data/RecordSO.asset" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Set(setup, "isDemo", false); Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03DiaryRecordsQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            data.RecordUnlocked += OnUnlocked; data.RecordUpdated += OnUpdated;
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    testMouse?.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
                    if (cancel) throw new OperationCanceledException("Diary record QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    try { if (records != null) Mark("failure-state"); } catch { }
                    Status = "FAILED at " + context + "\n" + error;
                    Debug.LogError("[Chapter03 Diary Records QA] " + Status); yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(files.Select(Hash).SequenceEqual(hashes), "real save files, all scene files and RecordSO asset remain unchanged");
            Status = "PASS";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                data.RecordUnlocked -= OnUnlocked; data.RecordUpdated -= OnUpdated;
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                os?.LinkMessageController?.NotifyOSHidden(); story?.ResetPresentation(); home?.Suspend();
                os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                if (story != null) story.enabled = storyWasEnabled;
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume
                    || resources != null && resources.enabled != resourcesEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse)
                    throw new InvalidOperationException("Original save or runtime settings were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes))
                    throw new IOException("Original save, scene or RecordSO file changed");
                checks.Add("cleanup restores original save object, language, mode, resources, input, random, listener and background settings");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, screenshots,
                originalFiles = files, originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                note = "Muted background Play Mode using cloned saves and isolated autosave storage. Real checkpoint, record windows, CollectPaper, unlock/update events, toast queue, localization and OS session restore are exercised. Story execution is disabled after fixture setup and its narrative queues are cleared after synchronous collection. Existing tip tween completion is simulated; natural tip duration is not tested. Screenshots capture only the Unity Game view. Exit Play Mode after this isolated run."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Diary Records QA] " + Status + " | " + OutputDirectory);
            data = null; os = null; memory = null; home = null; story = null; records = null;
            recordWindow = null; baseline = null; setup = null; testMouse = null;
        }
    }

    static IEnumerator Run()
    {
        context = "real checkpoint and empty diary fixture";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart), "Chapter 3 checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        os = OS_Handle.Instance; memory = os.MemoryWindowController; records = os.RecordWindowController;
        home = memory.GetComponent<OSChapter03HomeController>(); story = os.Chapter03Story;
        recordWindow = records.GetComponent<OSWindowUnit>(); storyWasEnabled = story.enabled;
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession
            && home?.View != null && records != null, "production Home and records resolve in isolated storage");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        baseline = Clone(data.SaveData);
        yield return Prepare();
        yield return CollectAndAppend();
        yield return SavedAndLocalized();
        yield return LegacyAndEmpty();
    }

    static IEnumerator Prepare()
    {
        os.LinkMessageController.NotifyOSHidden(); story.ResetPresentation();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true)) if (window.IsOpen) window.CloseSilently();
        home.ResetRuntime(); memory.PrepareRuntime(); records.PrepareRuntime();
        var save = Clone(baseline); save.flow.language = LanguageState.zh_CN;
        save.unlockedRecordDataIndices.RemoveAll(id => id == Diary);
        save.osProcess.memory.currentDataIndex = 8110; save.osProcess.memory.chapter03HomeInitialized = true;
        save.osProcess.memory.chapter03HomeView = new OSHomeView.State {
            room = "study", me = new Vector2Int(7, 5), dong = new Vector2Int(8, 5), dongHidden = false
        };
        var state = new OSChapter03StoryState { version = 1, phase = (int)OSChapter03StoryDirector.Phase.Explore, homeOpened = true };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8020", "opening_8070", "opening_8080", "opening_8110", "paper_note_01" }) state.Add(flag);
        foreach (string id in new[] { "study", "west", "hall", "kitchen", "east", "store", "bed", "bath", "lab", "yard", "junk" }) state.Add("room_" + id);
        state.papers.Add("note_01"); save.osProcess.memory.chapter03Story = state;
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData(); save.osResume = new OSResumeSaveData();
        data.ReplaceSaveData(save);
        os.LinkMessageController.SetGlobalMessagesSuppressed(false); os.LinkMessageController.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f); Check(os.OpenMemoryWindow(), "real Memory window opens");
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && home.View.CurrentRoom?.id == "study"
            && memory.CurrentData?.DataIndex == 8110, "real Home fixture becomes available");
        foreach (var unit in memory.TextUnits) if (unit != null) unit.CompleteImmediately();
        story.enabled = false; S.memoryQueue.Clear(); S.liveQueue.Clear();
        data.UnlockRecordData(0);
        Check((bool)Call(os, "OpenRecordWindow"), "record window opens through desktop process");
        yield return Wait(() => Ready(recordWindow) && records.CanInteract, "record window is ready");
        PrepareRecordForReading();
        DrainTips(); unlocked.Clear(); updated.Clear();
        Check(!data.IsRecordDataUnlocked(Diary) && !records.RecordUnits.Any(unit => unit.RecordIndex == Diary), "zero collected diary pages creates no diary entry");
        Check(OSChapter03DiaryRecord.BuildBody(S) == string.Empty, "zero collected diary pages exposes no diary text");
        records.SelectRecord(records.RecordUnits.First(unit => unit.RecordIndex == 0));
    }

    static IEnumerator CollectAndAppend()
    {
        context = "first collection and open-window updates";
        string previousBody = Body.text;
        Collect("diary_07");
        Check(unlocked.Count(id => id == Diary) == 1 && updated.Count(id => id == Diary) == 0, "first diary grants one real unlock event");
        UniqueDiary();
        Check(records.SelectedRecord.RecordIndex == 0 && Body.text == previousBody, "collecting a diary preserves the other record currently being read");
        Check(PendingTips().SequenceEqual(new[] { "日记#7已收录记录" }), "first collection queues exactly the requested Chinese tip, without a generic extra acquisition tip");
        SelectDiary(); AssertPages(new[] { "diary_07" });
        Check(records.CurrentData.RecordIcon != null && records.CurrentData.RecordIcon.rect.size == new Vector2(24, 24), "diary uses its native 24px icon");
        Check(!(Get(records, "attachmentsRoot") as RectTransform).gameObject.activeSelf && !records.TryOpenAttachment(), "diary is read directly in the record details");
        yield return ForSeconds(.35f); yield return Capture("01-first-diary-and-tip.png"); DrainTips();

        var found = new List<string> { "diary_07" };
        foreach (string id in FoundOrder.Skip(1))
        {
            ForceLayout();
            bool canScroll = Scroll.content.rect.height > Scroll.viewport.rect.height + 2f;
            if (canScroll) { Scroll.StopMovement(); Scroll.verticalNormalizedPosition = .37f; }
            float position = Scroll.verticalNormalizedPosition;
            int updateCount = updated.Count(value => value == Diary);
            Collect(id); found.Add(id);
            Check(updated.Count(value => value == Diary) == updateCount + 1 && unlocked.Count(value => value == Diary) == 1,
                id + " updates the existing record through the production event");
            UniqueDiary();
            Check(records.SelectedRecord.RecordIndex == Diary, "append preserves selected diary: " + id);
            AssertPages(found);
            if (canScroll) Check(Mathf.Abs(Scroll.verticalNormalizedPosition - position) < .02f, "append preserves the reading scroll position: " + id);
            Check(PendingTips().SequenceEqual(new[] { ExpectedTip(id) }), "new page has exactly one numbered collection tip: " + id);
            DrainTips();
        }
        Check(OSChapter03DiaryRecord.GetCollectedPaperIds(S).SequenceEqual(DateOrder), "all nine pages sort by source dates, with 03 before 04 on their shared date");
        string complete = Body.text; int updateBefore = updated.Count, unlockBefore = unlocked.Count;
        foreach (string id in FoundOrder) Collect(id);
        Check(Body.text == complete && updated.Count == updateBefore && unlocked.Count == unlockBefore && PendingTips().Length == 0,
            "rereading every collected diary adds no record, refresh event or tip");
        Check(S.papers.Count(id => id.StartsWith("diary_", StringComparison.Ordinal)) == 9, "rereading does not duplicate saved paper IDs");
        foreach (string id in new[] { "note_09", "repair_11", "husband_letter" }) Collect(id);
        Check(Body.text == complete && updated.Count == updateBefore && PendingTips().Length == 0,
            "instruction notes, repair record and husband letter do not enter diary text or issue diary tips");
        AssertPages(DateOrder); ForceLayout();
        Check(Scroll.vertical && Scroll.content.rect.height > Scroll.viewport.rect.height + 64f, "complete diary is a genuinely scrollable long document");
        Scroll.StopMovement(); Scroll.verticalNormalizedPosition = 1f;
        yield return Capture("02-complete-diary-top.png");
        float top = Scroll.content.anchoredPosition.y;
        Scroll.verticalNormalizedPosition = 0f; ForceLayout();
        Check(Mathf.Abs(Scroll.content.anchoredPosition.y - top) > 64f, "scrolling to the bottom moves the actual content");
        yield return Capture("03-complete-diary-bottom.png");
        Mark("collected out of order, duplicates and non-diary papers checked");
    }

    static IEnumerator SavedAndLocalized()
    {
        context = "real OS session restore";
        DrainTips(); string[] papers = S.papers.ToArray(); int u = unlocked.Count, d = updated.Count;
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "collected-diary-save.json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        os.LinkMessageController.NotifyOSHidden(); story.ResetPresentation(); data.ReplaceSaveData(restored); home.Suspend();
        yield return OSSessionPersistence.Restore(restored);
        os.LinkMessageController.SetGlobalMessagesSuppressed(false); os.LinkMessageController.NotifyOSBecameVisible();
        if (!recordWindow.IsOpen) Check((bool)Call(os, "OpenRecordWindow"), "saved record window reopens");
        yield return Wait(() => Ready(recordWindow) && records.RecordUnits.Any(unit => unit.RecordIndex == Diary), "record list rebuilds after session restoration");
        PrepareRecordForReading();
        SelectDiary(); UniqueDiary(); AssertPages(DateOrder);
        Check(S.papers.SequenceEqual(papers) && PendingTips().Length == 0 && unlocked.Count == u && updated.Count == d,
            "save restoration preserves collected pages and produces no duplicate unlock, update or tip");
        Check(!story.enabled && S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "diary 13 narrative has not advanced during the record-only fixture");
        Mark("restored diary");

        context = "live record localization";
        string chinese = Body.text;
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
        {
            data.SetCurrentLanguage(language);
            yield return Wait(() => Body.text == ExpectedBody(DateOrder), "open diary refreshes through language change event: " + language);
            AssertPages(DateOrder);
            Check(Title.text == data.GetLocalizedText("os.record.5.name", "MISSING"), "diary record title is localized: " + language);
            Check(PendingTips().Length == 0, "language switching produces no collection tip: " + language);
            if (language == LanguageState.en)
            {
                Check(Body.text != chinese, "English uses localized PaperBody values rather than cached Chinese text");
                Scroll.verticalNormalizedPosition = 1f; yield return Capture("04-english-diary.png");
            }
        }
        data.SetCurrentLanguage(LanguageState.zh_CN);
        yield return Wait(() => Body.text == ExpectedBody(DateOrder), "Chinese diary restored after locale sweep");
    }

    static IEnumerator LegacyAndEmpty()
    {
        context = "legacy reconciliation and empty-record guard";
        DrainTips();
        var legacy = Clone(data.SaveData); legacy.unlockedRecordDataIndices.RemoveAll(id => id == Diary);
        data.ReplaceSaveData(legacy);
        Check(!data.IsRecordDataUnlocked(Diary), "legacy fixture contains collected papers but no diary unlock");
        int u = unlocked.Count, d = updated.Count;
        // Run the live list's own initialization while its event handlers are
        // subscribed. Its silent reconciliation emits the first unlock reentrantly.
        records.InitializeFromSave();
        Check(unlocked.Count == u + 1 && updated.Count == d && data.IsRecordDataUnlocked(Diary), "open-list initialization silently repairs the legacy unlock exactly once");
        UniqueDiary(); SelectDiary(); AssertPages(DateOrder);
        for (int i = 0; i < 5; i++) OSStoryRecordUnlocks.RefreshDiary(data, newlyCollected: false);
        Check(unlocked.Count == u + 1 && updated.Count == d && PendingTips().Length == 0, "repeated legacy reconciliation is silent and idempotent");
        Mark("legacy papers repaired without duplicate list units");

        var empty = Clone(data.SaveData);
        empty.osProcess.memory.chapter03Story.papers = new List<string> { "note_01", "note_09", "repair_11", "husband_letter", "unknown_qa_paper" };
        data.ReplaceSaveData(empty); records.InitializeFromSave();
        Check(data.IsRecordDataUnlocked(Diary) && !records.RecordUnits.Any(unit => unit.RecordIndex == Diary), "an old/manual unlock with zero actual diary pages stays hidden");
        Check(OSChapter03DiaryRecord.BuildBody(S) == string.Empty && OSChapter03DiaryRecord.GetCollectedPaperIds(S).Count == 0
            && PendingTips().Length == 0, "non-diary and unknown IDs reveal no diary body or tip");
        Mark("empty diary guard");
        yield return null;
    }

    static void Collect(string id)
    {
        Check(!story.enabled, "story playback remains suspended during direct collection: " + id);
        int phase = S.phase;
        Call(story, "CollectPaper", id);
        S.memoryQueue.Clear(); S.liveQueue.Clear(); S.phase = phase;
    }
    static void SelectDiary() => records.SelectRecord(records.RecordUnits.Single(unit => unit.RecordIndex == Diary));
    static void UniqueDiary() => Check(data.UnlockedRecordDataIndices.Count(id => id == Diary) == 1
        && records.RecordUnits.Count(unit => unit.RecordIndex == Diary) == 1, "one saved diary unlock and one live list entry");
    static string ExpectedTip(string id) => "日记#" + int.Parse(id.Substring(6), CultureInfo.InvariantCulture) + "已收录记录";
    static string ExpectedBody(IEnumerable<string> collected)
    {
        var found = new HashSet<string>(collected, StringComparer.Ordinal);
        string format = data.GetLocalizedText("os.chapter03.ui.diary_record.entry_title", "MISSING");
        return string.Join("\n\n\n", DateOrder.Where(found.Contains).Select(id =>
            string.Format(CultureInfo.InvariantCulture, format, int.Parse(id.Substring(6), CultureInfo.InvariantCulture)) + "\n\n" + Content.PaperBody(id)));
    }
    static void AssertPages(IEnumerable<string> collected)
    {
        string[] ids = DateOrder.Where(new HashSet<string>(collected, StringComparer.Ordinal).Contains).ToArray();
        Check(OSChapter03DiaryRecord.GetCollectedPaperIds(S).SequenceEqual(ids), "only collected diary IDs appear in chronological order");
        Check(Body.text == ExpectedBody(ids), "live record text contains exactly the collected dates and complete localized original bodies");
        Check(!Body.text.Contains("MISSING"), "entry-heading localization resolves");
    }
    static string[] PendingTips()
    {
        var messages = new List<string>();
        if (Tips == null) return messages.ToArray();
        if ((bool)Get(Tips, "isTipTextPlaying")) messages.Add(Tips.TipTextLabel.text);
        foreach (object request in (IEnumerable)Get(Tips, "tipTextQueue")) messages.Add((string)Get(request, "Message"));
        return messages.ToArray();
    }
    static void DrainTips()
    {
        for (int i = 0; i < 32 && PendingTips().Length > 0; i++)
        {
            var sequence = Get(Tips, "tipTextSequence") as Tween;
            if (sequence == null || !sequence.IsActive()) throw new InvalidOperationException("Queued tip has no running production tween");
            sequence.Complete(true);
        }
        Check(PendingTips().Length == 0, "existing tip queue drained through production completion callbacks");
    }
    static void ForceLayout()
    {
        Canvas.ForceUpdateCanvases();
        if (Scroll?.content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(Scroll.content);
        Body?.ForceMeshUpdate();
    }
    static void PrepareRecordForReading()
    {
        // The real checkpoint pins Memory. Follow the same unpin/focus operations
        // available to a player, only in the cloned fixture; do not bypass sorting,
        // add a Canvas, register a test glow, or alter production pin rules.
        var memoryFrame = memory.GetComponent<OSWindowUnit>();
        bool wasPinned = memoryFrame.IsAlwaysOnTop;
        memoryFrame.SetAlwaysOnTop(false);
        recordWindow.RestoreAndBringToFront();
        Check(!memoryFrame.IsAlwaysOnTop && !recordWindow.IsMinimized && recordWindow.gameObject.activeInHierarchy,
            "normal unpin and focus APIs expose the record window");
        observations.Add(new { context, label = "record reading setup", memoryWasPinned = wasPinned,
            memoryPinned = memoryFrame.IsAlwaysOnTop, recordPinned = recordWindow.IsAlwaysOnTop });
    }
    static IEnumerator Capture(string name)
    {
        ForceLayout(); yield return null; ForceLayout(); yield return null;
        // Every append rebuilds list units and starts their normal .15s fade.
        // The synchronous collection loop can reach this point in two frames.
        float start = Time.realtimeSinceStartup;
        if (!CaptureGraphicsReady()) MarkGraphics(name, "before natural fade wait", 0f);
        while (!CaptureGraphicsReady() && Time.realtimeSinceStartup - start < 1f)
            yield return null;
        bool readyAtCapture = CaptureGraphicsReady();
        MarkGraphics(name, "at capture", Time.realtimeSinceStartup - start);
        string path = Path.Combine(OutputDirectory, name); screenshots.Add(path);
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 100, "Game-view screenshot is written: " + name, 10f);
        // Preserve the state at capture even if another frame completes the fade
        // while the PNG is being written. A failure now keeps its visual evidence.
        Check(readyAtCapture, name + ": body and icon become fully visible within the natural fade wait");
        AssertVisibleRecord(name);
    }
    static bool CaptureGraphicsReady() => GraphicVisible(Body) && GraphicVisible(records.SelectedRecord?.RecordIcon)
        && Body.textInfo.characterCount > 0;
    static void MarkGraphics(string name, string stage, float waited) => observations.Add(new {
        context, label = "capture graphics", screenshot = name, stage, frame = Time.frameCount, waitedSeconds = waited,
        body = GraphicState(Body), icon = GraphicState(records.SelectedRecord?.RecordIcon),
        bodyMeshCharacters = Body?.textInfo.characterCount, scrollPosition = Scroll?.verticalNormalizedPosition,
        contentHeight = Scroll?.content?.rect.height, viewportHeight = Scroll?.viewport?.rect.height });
    static object GraphicState(Graphic graphic)
    {
        if (graphic == null) return new { missing = true };
        var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
        return new { name = graphic.name, instanceId = graphic.GetInstanceID(), parent = TransformPath(graphic.transform.parent),
            activeSelf = graphic.gameObject.activeSelf, activeInHierarchy = graphic.gameObject.activeInHierarchy,
            enabled = graphic.enabled, canvasEnabled = graphic.canvas != null && graphic.canvas.enabled,
            culled = graphic.canvasRenderer.cull, colorAlpha = graphic.color.a, rendererAlpha = graphic.canvasRenderer.GetAlpha(),
            worldRect = new[] { corners.Min(point => point.x), corners.Min(point => point.y), corners.Max(point => point.x), corners.Max(point => point.y) },
            groups = graphic.GetComponentsInParent<CanvasGroup>(true).Select(group => new {
                path = TransformPath(group.transform), alpha = group.alpha, ignoreParents = group.ignoreParentGroups }).ToArray() };
    }
    static string TransformPath(Transform node) => node == null ? "none"
        : string.Join("/", node.GetComponentsInParent<Transform>(true).Reverse().Select(parent => parent.name));
    static void AssertVisibleRecord(string name)
    {
        var icon = records.SelectedRecord?.RecordIcon;
        Check(Ready(recordWindow) && records.CanInteract && records.SelectedRecord.RecordIndex == Diary,
            name + ": diary window remains open and interactive at capture");
        Check(GraphicVisible(Body) && GraphicVisible(icon) && Body.textInfo.characterCount > 0,
            name + ": body and icon have visible renderers and ancestor alpha");
        Check(icon.sprite == records.CurrentData.RecordIcon && icon.sprite.rect.size == new Vector2(24, 24),
            name + ": the rendered list icon uses the native diary sprite");
        Check(EventSystem.current != null && RecordIsForegroundAt(Scroll.viewport) && RecordIsForegroundAt(icon.rectTransform),
            name + ": record receives foreground UI raycasts at the body viewport and icon");
        var night = os.GetComponent<OSChapter02NightfallPresentation>();
        var mask = Get(night, "image") as RawImage;
        var glows = Get(night, "extraGlows") as IEnumerable;
        Check(night != null && night.IsNight && mask != null && mask.isActiveAndEnabled && mask.texture != null
            && glows != null && glows.Cast<object>().Any(glow => ReferenceEquals(Get(glow, "window"), recordWindow)
                && (float)Get(glow, "light") >= .99f),
            name + ": active production nightfall registers the record window at full glow");
        Mark("visible screenshot: " + name);
    }
    static bool GraphicVisible(Graphic graphic)
    {
        if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvas == null || !graphic.canvas.enabled
            || graphic.canvasRenderer.cull || graphic.color.a < .99f || graphic.canvasRenderer.GetAlpha() < .99f) return false;
        float alpha = 1f;
        for (Transform node = graphic.transform; node != null; node = node.parent)
        {
            if (!node.TryGetComponent(out CanvasGroup group)) continue;
            alpha *= group.alpha;
            if (group.ignoreParentGroups) break;
        }
        return alpha > .99f;
    }
    static bool RecordIsForegroundAt(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        if (point.x < 0f || point.y < 0f || point.x >= Screen.width || point.y >= Screen.height) return false;
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        return hits.Count > 0 && hits[0].gameObject.GetComponentInParent<OSWindowUnit>() == recordWindow;
    }
    static void OnUnlocked(int id) => unlocked.Add(id);
    static void OnUpdated(int id) => updated.Add(id);
    static void Mark(string label) => observations.Add(new { context, label, frame = Time.frameCount,
        selected = records?.SelectedRecord?.RecordIndex, paperIds = S.papers.ToArray(), diaryIds = OSChapter03DiaryRecord.GetCollectedPaperIds(S),
        listIds = records.RecordUnits.Select(unit => unit.RecordIndex).ToArray(), title = Title?.text, body = Body?.text,
        tipMessages = PendingTips(), unlockEvents = unlocked.ToArray(), updateEvents = updated.ToArray(),
        contentHeight = Scroll?.content?.rect.height, viewportHeight = Scroll?.viewport?.rect.height,
        scrollPosition = Scroll?.verticalNormalizedPosition });
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing;
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static IEnumerator ForSeconds(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        checks.Add(context + ": " + label);
    }
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Fields)?.GetValue(target);
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
