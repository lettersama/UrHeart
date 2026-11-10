using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.UI;

// Opt-in allocation audit. Only fixtures and Editor profiling are changed.
// No production saves, GC mode, quality settings or frame-rate limits change.
public static class OSGameGCQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly List<object> results = new List<object>();
    static readonly List<string> errors = new List<string>();
    static bool cancelled;
    public static string Status { get; private set; } = "idle";
    public static string Context { get; private set; }
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    [MenuItem("Tools/MrTGame/Performance/Run All-Chapter GC Audit")]
    static void StartMenu() => Debug.Log("[Game GC QA] " + Start());

    public static void StartValidationProbe() => GameDataManager.Instance.StartCoroutine(ValidationProbe());

    static IEnumerator ValidationProbe()
    {
        bool recording = ProfilerDriver.enabled, editor = ProfilerDriver.profileEditor;
        ProfilerDriver.profileEditor = false; ProfilerDriver.enabled = true;
        for (int i = 0; i < 60; i++) yield return null;
        var data = GameDataManager.Instance.SaveData;
        Action<string, Action> sample = (name, action) => {
            Profiler.BeginSample("QA_" + name);
            for (int i = 0; i < 100; i++) action();
            Profiler.EndSample();
        };
        sample("flow", data.flow.EnsureData);
        sample("ch04", data.osProcess.memory.chapter04Story.EnsureData);
        sample("memory", data.osProcess.memory.EnsureData);
        sample("interaction", data.osProcess.characterInteraction.EnsureData);
        sample("process", data.osProcess.EnsureData);
        sample("root", data.EnsureData);
        sample("gramophone", data.osProcess.gramophone.EnsureData);
        sample("knownAllocation", () => GC.KeepAlive(new byte[1024]));
        for (int i = 0; i < 60; i++) yield return null;
        ProfilerDriver.enabled = false;
        var rows = new List<object>();
        int end = ProfilerDriver.lastFrameIndex;
        for (int f = Math.Max(ProfilerDriver.firstFrameIndex, end - 120); f <= end; f++)
            using (var frame = ProfilerDriver.GetRawFrameDataView(f, 0))
            {
                if (!frame.valid) continue;
                for (int s = 0; s < frame.sampleCount; s++)
                {
                    string name = frame.GetSampleName(s);
                    if (!name.StartsWith("QA_", StringComparison.Ordinal)) continue;
                    long bytes = 0;
                    int stop = s + frame.GetSampleChildrenCountRecursive(s);
                    for (int k = s + 1; k <= stop; k++)
                        if (frame.GetSampleName(k) == "GC.Alloc") bytes += frame.GetSampleMetadataAsLong(k, 0);
                    rows.Add(new { name, bytesPerCall = bytes / 100, msPerCall = frame.GetSampleTimeMs(s) / 100 });
                }
            }
        File.WriteAllText("Temp/GameGCAudit/validation-probe.json", JsonConvert.SerializeObject(rows, Formatting.Indented));
        ProfilerDriver.profileEditor = editor; ProfilerDriver.enabled = recording;
    }

    public static string Start(string label = "audit")
    {
        if (Status == "running") return Status;
        if (!Application.isPlaying || GameAutosaveService.Instance == null || GameTest.Instance == null
            || GameManager.Instance == null || GameManager.Instance.IsTransitioning)
            return "Play Mode initialization required";
        if (GameAutosaveService.Instance.IsBusy || GameAutosaveService.Instance.IsWriting
            || GameTest.Instance.IsCheckpointJumpRunning) return "Wait for scene/save operation";
        OutputDirectory = Path.GetFullPath(Path.Combine("Tools", "Performance", "GameGC",
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + label));
        Directory.CreateDirectory(OutputDirectory);
        results.Clear(); errors.Clear(); cancelled = false; Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var data = GameDataManager.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        bool background = Application.runInBackground;
        float volume = AudioListener.volume;
        bool recording = ProfilerDriver.enabled, editor = ProfilerDriver.profileEditor;
        bool deep = ProfilerDriver.deepProfiling, cpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
        var focus = EditorWindow.focusedWindow;
        var setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        bool demo = setup != null && (bool)setup.GetType().GetField("isDemo", Fields).GetValue(setup);
        string[] paths = { saves.SavePath, saves.SavePath + ".bak" };
        saves.EnterTestSession();
        var hashes = paths.Select(Hash).ToArray();
        var work = new Stack<IEnumerator>();
        try
        {
            saves.UseIsolatedStorageForTests(Path.Combine(OutputDirectory, "isolated-save"));
            saves.EnterTestSession();
            Application.runInBackground = true; AudioListener.volume = 0f;
            if (setup != null) setup.GetType().GetField("isDemo", Fields).SetValue(setup, false);
            var gameView = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(w => w.GetType().FullName == "UnityEditor.GameView");
            gameView?.Focus();
            ProfilerDriver.enabled = false; ProfilerDriver.profileEditor = false;
            ProfilerDriver.deepProfiling = false; ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (cancelled) throw new OperationCanceledException("GC audit cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { errors.Add(Context + ": " + error); break; }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
        }
        finally
        {
            ProfilerDriver.enabled = false;
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) setup.GetType().GetField("isDemo", Fields).SetValue(setup, demo);
                Application.runInBackground = background; AudioListener.volume = volume;
                ProfilerDriver.profileEditor = editor; ProfilerDriver.deepProfiling = deep;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, cpu); ProfilerDriver.enabled = recording;
                focus?.Focus();
                if (!paths.Select(Hash).SequenceEqual(hashes)) errors.Add("Original save hash changed");
            }
            catch (Exception error) { errors.Add("cleanup: " + error); }
            Status = errors.Count == 0 ? "PASS: " + results.Count + " captures" : "FAILED: " + errors.Count;
            WriteResults();
        }
    }

    static IEnumerator Run()
    {
        yield return Capture("menu", null);
        var cases = new[] {
            GameTestCheckpoint.PrologueHeartHeal, GameTestCheckpoint.PrologueFault,
            GameTestCheckpoint.Chapter01BeforeInteractivePicture, GameTestCheckpoint.Chapter01EndingAnimation,
            GameTestCheckpoint.Chapter02ClawMachine, GameTestCheckpoint.Chapter02PaperPushMatch2,
            GameTestCheckpoint.Chapter02HeartTalk, GameTestCheckpoint.Chapter02FinalTalk,
            GameTestCheckpoint.Chapter03MemoryStart, GameTestCheckpoint.Chapter03Bedroom,
            GameTestCheckpoint.Chapter03LabEntrance, GameTestCheckpoint.Chapter04StoryStart,
            GameTestCheckpoint.Chapter04FinalHeartHeal };
        foreach (var checkpoint in cases)
        {
            Context = checkpoint.ToString();
            if (!GameTest.Instance.JumpToCheckpoint(checkpoint)) throw new Exception(GameTest.Instance.JumpStatus);
            yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
                && OS_Handle.Instance != null, 60f);
            if (!GameTest.Instance.JumpSucceeded) throw new Exception(GameTest.Instance.JumpStatus);
            FocusRuntime();
            Action tick = null;
            if (checkpoint == GameTestCheckpoint.Chapter03MemoryStart)
            {
                PrepareHome("study"); yield return Hold(1f);
                tick = () => WalkHome("study");
            }
            else if (checkpoint == GameTestCheckpoint.Chapter03Bedroom)
            {
                PrepareHome("yard"); yield return Hold(1f);
                tick = () => WalkHome("yard");
            }
            else if (checkpoint == GameTestCheckpoint.Chapter03LabEntrance)
            {
                PrepareHome("lab"); yield return Hold(1f);
            }
            else if (checkpoint == GameTestCheckpoint.Chapter04StoryStart) OS_Handle.Instance.OpenTVWindow();
            else if (checkpoint == GameTestCheckpoint.Chapter04FinalHeartHeal)
            {
                yield return Wait(() => OSChapter04GriefPresentation.Current?.WarningButton != null
                    && OSChapter04GriefPresentation.Current.IsFailureWarningVisible, 12f);
                OSChapter04GriefPresentation.Current.WarningButton.onClick.Invoke();
                yield return Wait(() => OS_Handle.Instance.Chapter04Story.FinalHeal?.IsRunning == true, 12f);
                var director = OS_Handle.Instance.Chapter04Story.FinalHeal;
                director.DebugSetFocus(true); director.DebugSetClock(0d);
                double clock = 8d;
                director.DebugStep(clock, false, false);
                tick = () => { clock += 1d / 60d; director.DebugStep(clock, false, false); };
            }
            yield return Capture(checkpoint.ToString(), tick);
            if (checkpoint == GameTestCheckpoint.Chapter03LabEntrance)
            {
                PrepareHome("bath"); yield return Hold(1f);
                OS_Handle.Instance.Chapter03Story.Presentation.ShowMirror("镜子", "我", true, "我");
                yield return Capture("Chapter03MirrorVisual", null);
            }
            if (checkpoint == GameTestCheckpoint.Chapter04FinalHeartHeal)
            {
                var director = OS_Handle.Instance.Chapter04Story.FinalHeal;
                double clock = director.Content.ReleaseTime + .1d;
                director.DebugStep(clock, false, false);
                yield return Capture("Chapter04FinalHealAnswers", () => {
                    clock += 1d / 60d; director.DebugStep(clock, false, false);
                });
            }
        }

        // Exercise the ending renderer independently of score judgements.
        var story = OS_Handle.Instance.Chapter04Story;
        bool enabled = story.enabled;
        story.enabled = false;
        var heal = OSChapter04FinalHealPresentation.Ensure();
        float presentationClock = 0f;
        try
        {
            heal.ClearAll();
            heal.SetPresentationClock(() => presentationClock);
            heal.SetMusicClock(() => presentationClock);
            heal.Begin(true);
            heal.PlayEpilogue(new[] {
                OSChapter04FinalHealPresentation.EpilogueStep.Choice(new[] { "继续", "再坐一会儿", "谢谢你" }, null)
            }, true);
            yield return Capture("Chapter04EpilogueChoices", () => { presentationClock += 1f / 30f; heal.Render(); });
            heal.ClearAll(); presentationClock = 0f; heal.SetPresentationClock(() => presentationClock); heal.Begin(true);
            heal.PlayEnding(new[] { "谢谢你，陪她走到了这里。" }, new[] { 100f });
            yield return Capture("Chapter04EndingText", () => { presentationClock += 1f / 30f; heal.Render(); });
        }
        finally
        {
            heal.ClearAll(); heal.SetMusicClock(null); heal.SetPresentationClock(null);
            story.enabled = enabled;
        }

        Context = "Chapter03 autosave preparation";
        GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart);
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning, 60f);
        PrepareHome("study");
        var saves = GameAutosaveService.Instance;
        saves.ResumeIsolatedWrites(); saves.BeginSession(GameDataManager.Instance.SaveData);
        try { yield return Capture("Chapter03HomeAutosave", () => WalkHome("study"), 600, 540); }
        finally { saves.EnterTestSession(); }
    }

    static void FocusRuntime()
    {
        GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
        var os = OS_Handle.Instance;
        if (os == null) return;
        foreach (var c in os.GetComponents<MonoBehaviour>())
        {
            if (c == null) continue;
            var method = c.GetType().GetMethod("DebugSetFocus", Fields, null, new[] { typeof(bool) }, null);
            method?.Invoke(c, new object[] { true });
            var field = c.GetType().GetField("hasFocus", Fields);
            if (field?.FieldType == typeof(bool)) field.SetValue(c, true);
        }
    }

    static void PrepareHome(string room)
    {
        var os = OS_Handle.Instance; var memory = os.MemoryWindowController;
        var home = memory.GetComponent<OSChapter03HomeController>();
        os.LinkMessageController.NotifyOSHidden(); os.Chapter03Story.ResetPresentation();
        foreach (var w in os.GetComponentsInChildren<OSWindowUnit>(true)) if (w.IsOpen) w.CloseSilently();
        home.ResetRuntime(); memory.PrepareRuntime();
        var save = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(GameDataManager.Instance.SaveData));
        var state = new OSChapter03StoryState { version = 1, interactionVisualVersion = 1,
            phase = (int)(room == "yard" ? OSChapter03StoryDirector.Phase.Yard : OSChapter03StoryDirector.Phase.Explore), homeOpened = true };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8020", "opening_8070",
            "opening_8080", "opening_8110", "paper_note_01", "room_" + room, "bed_open", "lab_open" }) state.Add(flag);
        state.papers.Add("note_01");
        save.osProcess.memory.chapter03Story = state; save.osProcess.memory.currentDataIndex = 8110;
        save.osProcess.memory.triggeredDataIndices = new List<int> { 8110 };
        save.osProcess.memory.chapter03HomeInitialized = true;
        save.osProcess.memory.chapter03HomeView = new OSHomeView.State {
            room = room, me = new Vector2Int(7, 5), dong = new Vector2Int(8, 5) };
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData(); save.osResume = new OSResumeSaveData();
        GameDataManager.Instance.ReplaceSaveData(save); os.LinkMessageController.SetGlobalMessagesSuppressed(false);
        os.LinkMessageController.NotifyOSBecameVisible(); FocusRuntime();
        os.OpenCharacterWindowForReconnect(0f); os.OpenMemoryWindow();
    }

    static void WalkHome(string room)
    {
        var view = OS_Handle.Instance?.Chapter03Story?.HomeView;
        if (view == null || !view.InputEnabled || view.IsWalking) return;
        var tile = new Vector2Int(view.PlayerTile.x < 7 ? 11 : 3, room == "study" ? 7 : 4);
        var rect = (RectTransform)view.transform;
        var local = new Vector2(rect.rect.xMin + (tile.x + .5f) * OSHomeLayout.Tile,
            rect.rect.yMax - (tile.y + .5f) * OSHomeLayout.Tile);
        var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local));
        var ray = new RaycastResult { gameObject = view.gameObject, module = rect.GetComponentInParent<GraphicRaycaster>(), screenPosition = point };
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = point, pointerCurrentRaycast = ray, pointerPressRaycast = ray };
        view.OnPointerEnter(pointer); view.OnPointerDown(pointer); view.OnPointerClick(pointer);
    }

    static IEnumerator Capture(string name, Action tick, int recordFrames = 150, int analyzeFrames = 120)
    {
        Context = name + " warmup";
        yield return Hold(2f, tick);
        Context = name + " capture";
        int before = ProfilerDriver.lastFrameIndex;
        ProfilerDriver.enabled = true;
        for (int i = 0; i < recordFrames; i++) { tick?.Invoke(); yield return null; }
        ProfilerDriver.enabled = false;
        int last = ProfilerDriver.lastFrameIndex - 1;
        int first = Math.Max(ProfilerDriver.firstFrameIndex, Math.Max(before + 3, last - analyzeFrames + 1));
        Context = name + " analysis";
        var frames = new List<object>(); var times = new List<double>();
        var bytesByOwner = new Dictionary<string, long>(); var timesByOwner = new Dictionary<string, double>();
        long allocated = 0, allocCount = 0; int collections = 0;
        for (int f = first; f <= last; f++)
        {
            using (var frame = ProfilerDriver.GetRawFrameDataView(f, 0))
            {
                if (!frame.valid) continue;
                long bytes = 0; double player = 0;
                var stack = new Stack<(int end, string owner, bool player)>();
                for (int s = 0; s < frame.sampleCount; s++)
                {
                    while (stack.Count > 0 && stack.Peek().end < s) stack.Pop();
                    string sample = frame.GetSampleName(s);
                    bool inPlayer = sample == "PlayerLoop" || stack.Count > 0 && stack.Peek().player;
                    string owner = stack.Count > 0 ? stack.Peek().owner : "unattributed";
                    if (sample.Contains("Assembly-CSharp.dll!"))
                    {
                        owner = sample;
                        timesByOwner.TryGetValue(owner, out double old); timesByOwner[owner] = old + frame.GetSampleTimeMs(s);
                    }
                    if (sample == "PlayerLoop") player += frame.GetSampleTimeMs(s);
                    if (sample == "GC.Collect" && inPlayer) collections++;
                    if (sample == "GC.Alloc" && inPlayer && frame.GetSampleMetadataCount(s) > 0)
                    {
                        long size = frame.GetSampleMetadataAsLong(s, 0);
                        bytes += size; allocCount++;
                        bytesByOwner.TryGetValue(owner, out long old); bytesByOwner[owner] = old + size;
                    }
                    int children = frame.GetSampleChildrenCountRecursive(s);
                    if (children > 0) stack.Push((s + children, owner, inPlayer));
                }
                allocated += bytes; times.Add(player);
                frames.Add(new { frame = f, playerMs = player, mainThreadMs = frame.frameTimeMs, gcBytes = bytes });
            }
            if ((f - first) % 15 == 14) yield return null;
        }
        times.Sort(); int count = frames.Count;
        if (count == 0) throw new Exception("No CPU Profiler frames captured for " + name);
        var os = OS_Handle.Instance;
        var report = new { name, recordFrames, requestedAnalysisFrames = analyzeFrames, frames = count, meanPlayerMs = times.Average(), p95PlayerMs = times[(int)((count - 1) * .95)],
            maxPlayerMs = times[count - 1], gcBytesPerFrame = allocated / count, allocationsPerFrame = allocCount / (double)count, collections,
            chapter = GameDataManager.Instance.CurrentChapterIndex,
            memoryNode = os?.MemoryWindowController?.CurrentData?.DataIndex,
            homeRoom = os?.Chapter03Story?.HomeView?.CurrentRoom?.id,
            activeWindows = os == null ? null : os.GetComponentsInChildren<OSWindowUnit>(true).Where(w => w.IsOpen && !w.IsMinimized).Select(w => w.name).ToArray(),
            allocationOwners = bytesByOwner.OrderByDescending(x => x.Value).Take(25).Select(x => new { source = x.Key, bytesPerFrame = x.Value / count }).ToArray(),
            cpuOwners = timesByOwner.OrderByDescending(x => x.Value).Take(20).Select(x => new { source = x.Key, msPerFrame = x.Value / count }).ToArray(), samples = frames };
        results.Add(report);
        File.WriteAllText(Path.Combine(OutputDirectory, name + ".json"), JsonConvert.SerializeObject(report, Formatting.Indented));
        WriteResults();
    }

    static IEnumerator Wait(Func<bool> ready, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready())
        {
            FocusRuntime();
            if (Time.realtimeSinceStartup > end) throw new TimeoutException(Context);
            yield return null;
        }
    }
    static IEnumerator Hold(float seconds, Action tick = null)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) { tick?.Invoke(); yield return null; }
    }
    static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream));
    }
    static void WriteResults() => File.WriteAllText(Path.Combine(OutputDirectory, "results.json"),
        JsonConvert.SerializeObject(new { status = Status, context = Context, results, errors,
            unity = Application.unityVersion, cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName,
            vSync = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
            note = "Main-thread PlayerLoop allocations only; Editor Mono, CPU Profiler on, Deep Profile off. 2-second warmup; standard cases record 150 frames/analyze the last 120, autosave records 600/analyzes up to 540. Per-case metadata gives actual counts. Home and final-score fixtures are explicitly scripted; one-shot loading and player builds require separate capture." }, Formatting.Indented));
}
