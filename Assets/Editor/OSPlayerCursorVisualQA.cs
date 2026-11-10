#if UNITY_EDITOR
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
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Opt-in production cursor/paper capture. Uses the existing isolated checkpoint
/// harness, returns to the main menu, restores the original save object, and
/// leaves Play Mode running with real save writes still isolated.
/// No cursor sorting, camera, sprite, scale, tint, or Canvas is fabricated here.
/// </summary>
public static class OSPlayerCursorVisualQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string PaperId = "diary_02";
    static readonly string[] AssetPaths = {
        "Assets/Scenes/GameUI.unity", "Assets/Data/LocalizationData.asset",
        "Assets/Data/OSMemoryData.asset", "Assets/Data/OSCharacterInteractionData.asset",
        "Assets/Resources/Chapter03Story/content.json"
    };
    static readonly List<string> checks = new List<string>();
    static readonly List<object> captures = new List<object>();
    sealed class FieldSnapshot { public UnityEngine.Object target; public string name; public object value; }
    static readonly List<FieldSnapshot> focusSnapshots = new List<FieldSnapshot>();
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSChapter03StoryPresentation paper;
    static OSChapter02NightfallPresentation night;
    static CursorHandle cursor;
    static GameObject pointerOwner;
    static GameSetup setup;
    static Mouse testMouse;
    static EditorWindow gameView, oldFocus;
    static bool cancel, storyEnabled, nightEnabled, capturedComponentState;
    static bool originalMessageSuppression;
    static string context;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null
            || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        foreach (string typeName in new[] { "OSMessageCountdownQA", "Chapter03FrontendCaptureQA", "OSChapter03StoryQA" })
        {
            var type = typeof(OSPlayerCursorVisualQA).Assembly.GetType(typeName);
            if (type?.GetProperty("Status")?.GetValue(null) as string == "running")
                return "Wait for " + typeName + " to finish";
        }
        if (CursorHandle.Instance != null && CursorHandle.Instance.IsScriptControlled)
            return "Wait for the current story cursor owner to release control";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        gameView = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().FullName == "UnityEditor.GameView");
        if (gameView == null) return "An existing Game view is required for end-of-frame captures";
        oldFocus = EditorWindow.focusedWindow;
        checks.Clear(); captures.Clear(); focusSnapshots.Clear(); cancel = false; capturedComponentState = false;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Tools", "Chapter03Story", "Validation", "PlayerCursor",
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)));
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
        string originalJson = JsonUtility.ToJson(original);
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground;
        bool demo = (bool)Get(setup, "isDemo");
        var previousMouse = Mouse.current;
        string[] savePaths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] saveHashes = null;
        string[] assetHashes = AssetPaths.Select(Hash).ToArray();
        var assets = AssetPaths.Select(AssetDatabase.LoadMainAssetAtPath).ToArray();
        bool[] dirty = assets.Select(a => a != null && EditorUtility.IsDirty(a)).ToArray();
        var work = new Stack<IEnumerator>();
        bool cleanupPhase = false;
        try
        {
            saves.EnterTestSession(); saveHashes = savePaths.Select(Hash).ToArray();
            string storage = Path.GetFullPath(Path.Combine("Temp", "OSPlayerCursorVisualQA", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(storage);
            saves.UseIsolatedStorageForTests(storage); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Application.runInBackground = true; Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("OSPlayerCursorVisualQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            testMouse.MakeCurrent(); gameView.Focus();
            work.Push(Run());
            while (work.Count > 0 || !cleanupPhase)
            {
                if (work.Count == 0) { cleanupPhase = true; work.Push(ReturnToMenu()); }
                object next;
                try
                {
                    if (cancel && !cleanupPhase) throw new OperationCanceledException("Cursor capture cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = (error is OperationCanceledException ? "CANCELLED" : "FAILED") + " at " + context + "\n" + error;
                    Debug.LogError("[Player Cursor Visual QA] " + Status);
                    while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
                    if (cleanupPhase) break;
                    continue;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (Status == "running")
            {
                Check(captures.Count == 2, "both production paper captures completed");
                Status = "PASS: diary and night diary cursor captures";
            }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            var cleanupErrors = new List<string>();
            void Restore(Action action)
            {
                try { action(); }
                catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            }
            Restore(() => {
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                paper?.HideModal(false); story?.ResetPresentation(); home?.Suspend(); night?.ResetPresentation();
                InvokeOptional(os?.Chapter03Entry, "ResetPresentation");
                os?.Chapter02FinalTalk?.ResetPresentation();
                os?.LinkMessageController?.NotifyOSHidden();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
            });
            Restore(() => { saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave(); });
            Restore(() => {
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                Set(setup, "isDemo", demo);
                if (capturedComponentState)
                {
                    if (story != null) story.enabled = storyEnabled;
                    if (night != null) night.enabled = nightEnabled;
                    os?.LinkMessageController?.SetGlobalMessagesSuppressed(originalMessageSuppression);
                }
                foreach (var snapshot in focusSnapshots)
                    if (snapshot.target != null) Set(snapshot.target, snapshot.name, snapshot.value);
            });
            Restore(() => {
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (cursor != null && pointerOwner != null)
                    cursor.ReleaseScriptControl(pointerOwner, true, true);
                if (pointerOwner != null) UnityEngine.Object.Destroy(pointerOwner);
                if (oldFocus != null) oldFocus.Focus();
            });
            Restore(() => {
                Check(ReferenceEquals(data.SaveData, original) && JsonUtility.ToJson(original) == originalJson,
                    "original save object and serialized contents restored exactly");
                Check(data.CurrentLanguageState == language && GameSetup.IsDemo == demo, "language and demo mode restored");
                Check(saveHashes == null || savePaths.Select(Hash).SequenceEqual(saveHashes), "original save file hashes unchanged");
                Check(AssetPaths.Select(Hash).SequenceEqual(assetHashes), "production asset and scene file hashes unchanged");
                Check(assets.Select(a => a != null && EditorUtility.IsDirty(a)).SequenceEqual(dirty), "production asset dirty flags unchanged");
                Check(cursor == null || pointerOwner == null || !cursor.IsScriptControlledBy(pointerOwner), "test cursor control released");
                Check(Application.isPlaying && saves.IsTestSession, "Play Mode remains active with real writes isolated");
            });
            if (cleanupErrors.Count > 0) Status = "FAILED during cleanup:\n" + string.Join("\n", cleanupErrors) + "\n" + Status;
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, paperId = PaperId, language = language.ToString(), checks, captures,
                originalSaveFiles = savePaths, originalSaveHashes = saveHashes, finalSaveHashes = savePaths.Select(Hash).ToArray(),
                assets = AssetPaths, originalAssetHashes = assetHashes, finalAssetHashes = AssetPaths.Select(Hash).ToArray(),
                originalSaveJsonRestored = JsonUtility.ToJson(original) == originalJson,
                testSession = saves.IsTestSession, playMode = Application.isPlaying,
                note = "Returns to the main menu in Play Mode. Original save and settings are restored; storage remains isolated. PNGs are native Game-view captures. Layer PASS is structural evidence; inspect the two PNGs for visual evidence."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Player Cursor Visual QA] " + Status + " | " + OutputDirectory);
            os = null; memory = null; home = null; story = null; paper = null; night = null; cursor = null;
            pointerOwner = null; setup = null; testMouse = null; gameView = oldFocus = null;
        }
    }

    static IEnumerator ReturnToMenu()
    {
        context = "return to main menu before restoring original save";
        paper?.HideModal(false); story?.ResetPresentation(); home?.Suspend(); night?.ResetPresentation();
        InvokeOptional(os?.Chapter03Entry, "ResetPresentation");
        os?.Chapter02FinalTalk?.ResetPresentation(); os?.LinkMessageController?.NotifyOSHidden();
        if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
        GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
        var saves = GameAutosaveService.Instance;
        saves.EnterTestSession(); saves.EndSession();
        yield return Wait(() => !GameManager.Instance.IsTransitioning && !SceneLoadManager.Instance.IsBusy,
            "fixture transition settled before cleanup", 30f);
        GameManager.Instance.OpenMainMenuWithoutSave();
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy, "main menu transition completed before original save restoration", 30f);
        yield return null;
        Check(GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning,
            "main menu remains settled on the next frame");
    }

    static IEnumerator Run()
    {
        yield return Fixture();
        storyEnabled = story.enabled; nightEnabled = night.enabled;
        capturedComponentState = true;
        story.enabled = false;
        os.LinkMessageController.NotifyOSHidden();
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        paper.Initialize(os); paper.Paused = false;
        night.enabled = false; // Bright reference only; the same production paper is used in both captures.
        yield return null;
        var content = OSChapter03StoryContent.Instance;
        Check(content.FindPaper(PaperId)?.type == "diary" && !string.IsNullOrEmpty(content.PaperBody(PaperId)),
            "authored diary data resolves");
        paper.ShowPaper(content.Text("ui.paper.diary"), content.PaperBody(PaperId), null, "diary");
        yield return Wait(() => paper.PaperRoot != null && paper.IsModalOpen && !paper.Paused
            && (float)Get(paper, "modalAge") >= .2f, "production diary opened");
        cursor = CursorHandle.Instance;
        Check(cursor != null && !cursor.IsScriptControlled, "real player cursor is available");
        pointerOwner = new GameObject("OSPlayerCursorVisualQA owner") { hideFlags = HideFlags.HideAndDontSave };
        Check(cursor.AcquireScriptControl(pointerOwner), "temporary public cursor control acquired");
        PositionPointer();
        yield return Capture("01-diary-player-cursor", false);
        night.enabled = true; Focus(); night.RestoreEntryNight();
        yield return Wait(() => night.IsNight && night.HasEnded && night.DebugDark >= 16f,
            "production settled entry night restored");
        PositionPointer();
        yield return Capture("02-night-diary-player-cursor", true);
    }

    static IEnumerator Fixture()
    {
        context = "isolated chapter-three fixture";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03DemoEnd), "checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "isolated checkpoint succeeded");
        os = OS_Handle.Instance; memory = os.MemoryWindowController;
        home = memory != null ? memory.GetComponent<OSChapter03HomeController>() : null;
        story = os.Chapter03Story; night = os.Chapter02FinalTalk?.Nightfall;
        Check(memory != null && home?.View != null && story != null && night != null, "production components resolved");
        originalMessageSuppression = os.LinkMessageController.GlobalMessagesSuppressed;
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        var fixture = Clone(GameDataManager.Instance.SaveData);
        fixture.osProcess.currentChapterIndex = 3;
        fixture.osProcess.memory.currentDataIndex = 8110;
        fixture.osProcess.memory.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Complete;
        fixture.osProcess.memory.chapter03EntryStep = (int)OSChapter03EntryDirector.EntryStep.Complete;
        fixture.osProcess.memory.chapter03EntryInstallAnswer = 1;
        fixture.osProcess.memory.chapter03HomeInitialized = true;
        fixture.osProcess.memory.chapter03HomeView = new OSHomeView.State {
            room = "study", me = new Vector2Int(9, 7), dong = new Vector2Int(8, 7), dongHidden = false };
        var state = new OSChapter03StoryState { version = 1, phase = (int)OSChapter03StoryDirector.Phase.Explore,
            homeOpened = true, knowAnswer = 1, coffeeStep = 5 };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8080", "paper_note_01", "opening_8110", "coffee_taken" })
            state.Add(flag);
        fixture.osProcess.memory.chapter03Story = state;
        fixture.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        fixture.osResume.captured = false;
        foreach (string id in new[] { "memory-repair-1", "memory-repair-2", "memory-repair-3", "memory-repair-4" })
            if (!fixture.osProcess.installedToolProgramIds.Contains(id)) fixture.osProcess.installedToolProgramIds.Add(id);
        InvokeOptional(os.Chapter03Entry, "ResetPresentation"); os.Chapter02FinalTalk.ResetPresentation();
        story.ResetPresentation(); os.LinkMessageController.NotifyOSHidden();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true)) if (window.IsOpen) window.CloseSilently();
        memory.PrepareRuntime(); GameDataManager.Instance.ReplaceSaveData(fixture);
        os.LinkMessageController.SetGlobalMessagesSuppressed(false); os.LinkMessageController.NotifyOSBecameVisible();
        Focus(); yield return null;
        Check(os.OpenMemoryWindow(), "production Memory window opens");
        yield return Wait(() => story.IsAvailable && story.Presentation != null, "production story presentation available");
        paper = story.Presentation;
        foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
    }

    static void PositionPointer()
    {
        Canvas.ForceUpdateCanvases();
        var rect = paper.PaperRoot;
        var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 local = rect.rect.center + new Vector2(-rect.rect.width * .22f, rect.rect.height * .12f);
        cursor.SetScriptScreenPosition(pointerOwner, RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local)));
    }

    static IEnumerator Capture(string name, bool expectNight)
    {
        context = name; Focus(); PositionPointer();
        yield return Wait(() => Application.isFocused, "Unity Game view has application focus", 10f);
        context = name;
        yield return null;
        Focus(); PositionPointer(); yield return new WaitForEndOfFrame();
        var report = JObject.Parse(OSPlayerCursorLayerQA.Run());
        Check(Application.isFocused, name + " captured while Unity has actual application focus");
        var source = ScreenCapture.CaptureScreenshotAsTexture();
        Check(source != null, name + " screenshot captured");
        try
        {
            File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), source.EncodeToPNG());
            File.WriteAllText(Path.Combine(OutputDirectory, name + "-layers.json"), report.ToString(Formatting.Indented));
            captures.Add(new { name, png = name + ".png", width = source.width, height = source.height,
                unityFrame = Time.frameCount, language = GameDataManager.Instance.CurrentLanguageState.ToString(),
                expectNight, night = night.IsNight, darkness = night.DebugDark,
                pointer = new[] { cursor.DisplayedScreenPosition.x, cursor.DisplayedScreenPosition.y }, layerReport = report });
            Check((string)report["status"] == "PASS", name + " real Canvas checks PASS: " + report["failures"]);
            Check((bool?)report["pointerInsidePaper"] == true, name + " player pointer lies inside the diary");
            Check(expectNight == night.IsNight, name + " expected production night state");
            if (expectNight) Check(report["night"]?.Type == JTokenType.Object,
                name + " active production night Canvas inspected");
        }
        finally { UnityEngine.Object.Destroy(source); }
    }

    static void Focus()
    {
        testMouse?.MakeCurrent();
        if (gameView != null && EditorWindow.focusedWindow != gameView) gameView.Focus();
        RememberFocus(story, "debugIgnoreFocus"); RememberFocus(os?.Chapter03Entry, "hasFocus");
        RememberFocus(os?.Chapter02FinalTalk, "hasFocus"); RememberFocus(night, "hasFocus");
        story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
        os?.Chapter02FinalTalk?.DebugSetFocus(true);
        if (night != null) Set(night, "hasFocus", true);
    }
    static void RememberFocus(UnityEngine.Object target, string name)
    {
        if (target == null || focusSnapshots.Any(s => s.target == target && s.name == name)) return;
        focusSnapshots.Add(new FieldSnapshot { target = target, name = name, value = Get(target, name) });
    }
    static IEnumerator Wait(Func<bool> ready, string label, float seconds = 20f)
    {
        context = label; float until = Time.realtimeSinceStartup + seconds;
        while (!ready() && Time.realtimeSinceStartup < until) { Focus(); yield return null; }
        Check(ready(), label);
    }
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static void InvokeOptional(object target, string name) => target?.GetType().GetMethod(name, Fields)?.Invoke(target, null);
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static void Check(bool passed, string label)
    { if (!passed) throw new InvalidOperationException(label); if (!checks.Contains(label)) checks.Add(label); }
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
#endif
