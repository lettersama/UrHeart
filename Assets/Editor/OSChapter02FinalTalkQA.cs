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
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Step = OSChapter02FinalTalkDirector.FinalTalkStep;

// Opt-in integration QA. Uses the real message controller and save/load paths.
// All writes stay in a unique Temp directory; finish at the menu in test mode.
public static class OSChapter02FinalTalkQA
{
    const string Prefix = "chapter02_final_talk_";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly HashSet<string> visited = new HashSet<string>();
    static readonly Dictionary<string, string> snapshots = new Dictionary<string, string>();
    static readonly List<object> layouts = new List<object>();
    static OS_Handle os;
    static OSChapter02FinalTalkDirector director;
    static OSLinkMessageController link;
    static OSWindowUnit live, settings;
    static Source source;
    static string context;
    static int inputFrame;
    static int routes;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    [Serializable] sealed class Source { public SourceEntry[] entries; }
    [Serializable] sealed class SourceEntry { public string id, text; public SourceOption[] options; }
    [Serializable] sealed class SourceOption { public string id, text; }

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
        if (!GameTest.IsCheckpointAvailable(GameTestCheckpoint.Chapter02FinalTalk))
            return "Chapter02FinalTalk checkpoint is unavailable in this build mode";
        checks.Clear(); visited.Clear(); snapshots.Clear(); layouts.Clear(); routes = 0;
        context = "initialization"; inputFrame = Time.frameCount + 100;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter02FinalTalkQA", Guid.NewGuid().ToString("N")));
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
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();
            hashes = paths.Select(Fingerprint).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory);
            saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
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
                    Debug.LogError("[Chapter02 Final Talk QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " checks, " + routes + " routes, " + visited.Count + " variants";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                director?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language)
                    throw new InvalidOperationException("Original memory/language was not restored");
                if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes))
                    throw new IOException("Original save files changed");
                checks.Add("cleanup restored original memory/language and preserved save hashes");
            }
            catch (Exception cleanup) { Status = "FAILED during cleanup: " + cleanup + "\n" + Status; }
            if (Status == "running") Status = "FAILED: QA interrupted at " + context;
            var report = new {
                status = Status, context, checks, routes, variants = visited.OrderBy(x => x).ToArray(),
                snapshotNames = snapshots.Keys.ToArray(), layouts, originalFiles = paths,
                originalHashes = hashes, finalHashes = paths.Select(Fingerprint).ToArray(),
                testSession = saves.IsTestSession, note = "Leave Play Mode after this isolated run."
            };
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter02 Final Talk QA] " + Status + " | " + OutputDirectory);
            os = null; director = null; link = null; live = null; settings = null;
        }
    }

    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        context = "checkpoint";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter02FinalTalk), "final-talk checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession,
            "checkpoint succeeded without enabling player-save writes");
        Bind();
        data.SetCurrentLanguage(LanguageState.zh_CN);
        ValidateContent();
        Pump();
        var baseline = Capture();
        for (int relation = 0; relation <= 2; relation++)
        for (int comfort = 1; comfort <= 2; comfort++)
        for (int question = 1; question <= 2; question++)
        {
            context = "route " + relation + "/" + comfort + "/" + question;
            var fixture = Clone(baseline);
            fixture.flow.language = LanguageState.zh_CN;
            fixture.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
            OSChapter02FinalTalkDirector.PrepareDebugCheckpoint(fixture.osProcess.memory,
                fixture.osProcess.energyLoop, relation);
            Install(fixture);
            string balance = JsonUtility.ToJson(data.SaveData.osResources);
            int lastStep = 0;
            for (int n = 0; n < 18 && director.Step != Step.Chapter03Ready; n++)
            {
                Pump();
                int step = (int)director.Step;
                int expectedStep = lastStep == 4 && relation == 0 ? 6 : lastStep + 1;
                Check(step == expectedStep, context + " advances to step " + expectedStep);
                string suffix = Expected(step, relation, comfort, question);
                Show(suffix);
                visited.Add(suffix);
                Remember(suffix);
                if (step == 7 || step == 12) yield return Choose(suffix, step == 7
                    ? (comfort == 1 ? "together" : "silent") : (question == 1 ? "ask" : "wait"));
                else Advance(suffix);
                Check(JsonUtility.ToJson(data.SaveData.osResources) == balance,
                    context + " step " + step + " has no resource effect");
                lastStep = step;
            }
            Pump();
            Check(director.Step == Step.Chapter03Ready && data.CurrentChapterIndex == 2,
                context + " ends at the persisted chapter-three boundary");
            Check(data.OSProcessData.memory.chapter02FinalTalkComfortAnswer == comfort
                && data.OSProcessData.memory.chapter02FinalTalkQuestionAnswer == question,
                context + " retains both actual choices");
            Remember("ready");
            routes++;
            yield return null;
        }
        Check(visited.SetEquals(source.entries.Select(x => x.id)), "all nineteen authored variants were displayed");
        yield return CheckCompletingPointerGesture();

        context = "every-step full-save restore";
        foreach (var saved in snapshots.ToArray())
        {
            context = "full-save restore " + saved.Key;
            yield return Restore(saved.Value);
            var expected = JsonUtility.FromJson<GameSaveData>(saved.Value);
            CheckProgress(expected, "restored " + saved.Key);
            if (saved.Key == "ready") CheckReady("ready restored");
            else Show(saved.Key);
        }

        context = "pause and resume";
        yield return Restore(snapshots["forget"]);
        yield return CheckPause("focus", () => director.DebugSetFocus(false), () => director.DebugSetFocus(true));
        yield return CheckPause("settings", () => { settings.OpenStandalone(0f); Settle(); },
            () => { settings.CloseFromScript(); Settle(); });
        var console = GameTest.Instance.GetComponent<GameTestConsole>();
        Check(console != null, "test console exists for its real input-pause gate");
        yield return CheckPause("console", () => console.SetOpen(true), () => console.SetOpen(false));
        yield return CheckPause("hidden OS", () => SetOSPresentation(GamePhase.Prologue),
            () => SetOSPresentation(GamePhase.OSMain));
        CheckLiveReconstruction();

        yield return CheckLayouts();
        foreach (string saved in new[] { "forget", "comfort_together", "ready" })
        {
            context = "disk reconstruction " + saved;
            yield return Restore(snapshots[saved]);
            yield return DiskRoundtrip(saved);
        }
        context = "completed boundary";
        CheckReady("chapter three remains unavailable after reconstruction");
        string before = ProgressFingerprint();
        yield return new WaitForSecondsRealtime(.6f);
        Pump(); Press(); Pump();
        Check(ProgressFingerprint() == before && !link.LinkWindow.IsOpen,
            "ready boundary neither replays dialogue nor auto-enters empty chapter three");
    }

    static void Bind()
    {
        os = OS_Handle.Instance;
        director = os != null ? os.GetComponent<OSChapter02FinalTalkDirector>() : null;
        link = os?.LinkMessageController;
        live = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        settings = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        Check(director != null && link != null && live != null && settings != null, "runtime references resolve");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        director.DebugSetFocus(true);
        SetFieldIfPresent(os.Chapter02PaperPush, "hasFocus", true);
        SetFieldIfPresent(os.CursorRelease, "hasFocus", true);
        link.SetGlobalMessagesSuppressed(false);
    }

    static void Install(GameSaveData snapshot)
    {
        director.ResetPresentation(); link.NotifyOSHidden();
        GameDataManager.Instance.ReplaceSaveData(snapshot);
        director.DebugSetFocus(true);
        settings.CloseFromScript();
        os.OpenCharacterWindowForReconnect(0f);
        Settle();
        director.DebugStep();
        link.NotifyOSBecameVisible();
        Pump();
    }

    static IEnumerator Restore(string json)
    {
        director.ResetPresentation(); link.NotifyOSHidden();
        var snapshot = JsonUtility.FromJson<GameSaveData>(json);
        GameDataManager.Instance.ReplaceSaveData(snapshot);
        director.DebugSetFocus(true);
        yield return OSSessionPersistence.Restore(snapshot);
        Bind();
        link.NotifyOSBecameVisible();
        Pump();
    }

    static GameSaveData Capture()
    {
        var data = GameDataManager.Instance;
        OSSessionPersistence.Capture(data.SaveData);
        GameSessionFlow.Capture(data.SaveData);
        Check(data.SaveData.osResume.captured, "real OS presentation captured in the full save");
        return Clone(data.SaveData);
    }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static void Remember(string name)
    {
        if (snapshots.ContainsKey(name)) return;
        string json = JsonUtility.ToJson(Capture(), true);
        snapshots.Add(name, json);
        File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-" + name + ".json"), json);
    }

    static void Pump()
    {
        for (int i = 0; i < 6; i++) { director.DebugStep(); Settle(); }
    }
    static void Settle()
    {
        foreach (var window in new[] { live, settings, link?.LinkWindow })
        {
            CompleteTween(window, "openSequence"); CompleteTween(window, "closeSequence");
        }
        // Finish only window/reveal tweens. Never finish textTween here: the
        // first-confirm check must exercise the real typewriter skip path.
        CompleteTween(link, "reopenTween");
        CompleteTween(link?.WindowLayout, "heightTween");
        Canvas.ForceUpdateCanvases();
    }
    static void Press()
    {
        inputFrame = Mathf.Max(inputFrame + 2, Time.frameCount + 2);
        director.DebugConfirm(false, false, inputFrame - 1);
        director.DebugConfirm(true, true, inputFrame);
    }
    static void Show(string suffix)
    {
        Pump();
        Check(link.DisplayedEntryId == Prefix + suffix && link.LinkWindow.IsOpen
            && director.DebugMessageId == Prefix + suffix, context + " displays " + suffix);
    }
    static void Finish(string suffix)
    {
        Show(suffix);
        var before = director.Step;
        if (!director.DebugMessageTyped)
        {
            Press();
            Check(director.DebugMessageTyped && director.Step == before,
                suffix + " first confirm finishes text without advancing");
            // Repeated held input and a duplicate frame may not consume a line.
            director.DebugConfirm(true, true, inputFrame);
            director.DebugConfirm(false, true, inputFrame + 1);
            Check(director.Step == before, suffix + " held/duplicate confirm does not advance");
        }
        else Check(director.Step == before, suffix + " natural text completion retains its step");
        Pump();
    }
    static void Advance(string suffix)
    {
        Finish(suffix);
        int before = (int)director.Step;
        for (int n = 0; n < 90; n++) director.DebugStep();
        Check((int)director.Step == before, suffix + " completed ordinary text waits for manual confirmation");
        Press();
        int expected = before == 4 && GameDataManager.Instance.OSProcessData.memory.chapter02ReadAssistant == 0 ? 6 : before + 1;
        Check((int)director.Step == expected, suffix + " second confirm advances exactly one authored step");
        Pump();
    }
    static IEnumerator Choose(string suffix, string answer)
    {
        Finish(suffix);
        string sequence = link.DisplayedSequenceId;
        string entry = link.DisplayedEntryId;
        string before = ProgressFingerprint();
        Press();
        Check(ProgressFingerprint() == before && link.IsWaitingForOption,
            suffix + " generic confirm cannot choose an answer");
        Check(!link.TrySelectDisplayedOptionImmediately("wrong_sequence", entry, answer)
            && !link.TrySelectDisplayedOptionImmediately(sequence, entry + "_wrong", answer)
            && !link.TrySelectDisplayedOptionImmediately(sequence, entry, "wrong_option")
            && ProgressFingerprint() == before, suffix + " rejects wrong sequence/entry/option without progress");
        director.DebugConfirm(false, false, ++inputFrame);
        Check(link.TrySelectDisplayedOptionImmediately(sequence, entry, answer), suffix + " selects " + answer);
        string after = ProgressFingerprint();
        Check(after != before && !link.TrySelectDisplayedOptionImmediately(sequence, entry, answer)
            && ProgressFingerprint() == after, suffix + "/" + answer + " duplicate selection is inert");
        if (answer == "silent" || answer == "wait")
        {
            Settle();
            Check(!link.LinkWindow.IsOpen, suffix + " silent choice initially withholds its reply");
            float earlyUntil = Time.realtimeSinceStartup + .25f;
            while (Time.realtimeSinceStartup < earlyUntil)
            {
                MaintainSilentBeatFocus();
                yield return null;
            }
            Check(!link.LinkWindow.IsOpen && ProgressFingerprint() == after,
                suffix + " silent beat preserves its selected answer without speaking early");
            string reply = Prefix + (answer == "silent" ? "comfort_silent" : "question_wait");
            yield return Wait(() => link.DisplayedEntryId == reply && link.LinkWindow.IsOpen,
                suffix + " silent beat naturally releases the authored reply", 10f, MaintainSilentBeatFocus);
        }
        Pump();
    }

    static void MaintainSilentBeatFocus()
    {
        // The test runs while Editor tools can take window focus. Keep only
        // this natural-time story wait in its deliberate focused fixture.
        // Dedicated focus/settings/console pause checks never call this helper.
        if (GetField(director, "hasFocus") is bool focused && !focused) director.DebugSetFocus(true);
        SetFieldIfPresent(os?.Chapter02PaperPush, "hasFocus", true);
        SetFieldIfPresent(os?.CursorRelease, "hasFocus", true);
    }

    static IEnumerator CheckCompletingPointerGesture()
    {
        // Use actual Input System button state, then the production pointer-click
        // receiver. The disposable mouse stays offscreen so an automatic UI
        // raycast cannot add a second click while this test dispatches its own.
        Mouse previousMouse = Mouse.current;
        Mouse testMouse = null;
        try
        {
            testMouse = InputSystem.AddDevice<Mouse>("Chapter02FinalTalkQAMouse");
            foreach (string suffix in new[] { "forget", "question" })
            {
                context = "mouse gesture " + suffix;
                ChangeTestMouse(testMouse, false);
                testMouse.MakeCurrent();
                Install(JsonUtility.FromJson<GameSaveData>(snapshots[suffix]));
                Show(suffix);
                Check(!director.DebugMessageTyped, suffix + " gesture fixture starts while text is typing");
                string before = ProgressFingerprint();
                director.DebugConfirm(false, false, Time.frameCount);
                ChangeTestMouse(testMouse, true);
                testMouse.MakeCurrent();
                Check(Mouse.current.leftButton.isPressed, suffix + " uses a real pressed Input System mouse state");
                director.DebugConfirm(true, true, Time.frameCount);
                Check(director.DebugMessageTyped && director.IsOptionGestureBlocked
                    && ProgressFingerprint() == before, suffix + " mouse-down only completes the question text");
                string answer = suffix == "forget" ? "together" : "ask";
                Check(link.TryGetActiveOptionTarget(answer, out RectTransform target), suffix + " exposes its actual answer receiver");
                var receiver = target.GetComponentInParent<OSLinkMessageSelectUnit>();
                Check(receiver != null, suffix + " resolves the production pointer-click component");
                var click = new PointerEventData(EventSystem.current) {
                    button = PointerEventData.InputButton.Left, pointerId = -1, clickCount = 1
                };
                string interaction = JsonUtility.ToJson(GameDataManager.Instance.OSCharacterInteractionState);
                ChangeTestMouse(testMouse, false);
                testMouse.MakeCurrent();
                // EventSystem can run before or after the director. Both release
                // orderings must reject the completing gesture before receipts.
                receiver.OnPointerClick(click);
                Check(ProgressFingerprint() == before
                    && JsonUtility.ToJson(GameDataManager.Instance.OSCharacterInteractionState) == interaction,
                    suffix + " completing PointerClick before director tick cannot answer or consume");
                director.DebugStep();
                receiver.OnPointerClick(click);
                Check(director.IsOptionGestureBlocked && ProgressFingerprint() == before
                    && JsonUtility.ToJson(GameDataManager.Instance.OSCharacterInteractionState) == interaction
                    && !link.TrySelectDisplayedOptionImmediately(link.DisplayedSequenceId, Prefix + suffix, answer),
                    suffix + " completing release stays blocked for the entire release frame");
                yield return null;
                testMouse.MakeCurrent();
                director.DebugStep();
                Check(!director.IsOptionGestureBlocked, suffix + " gesture lock expires after release frame");
                director.DebugConfirm(false, false, Time.frameCount - 1);
                ChangeTestMouse(testMouse, true);
                testMouse.MakeCurrent();
                director.DebugConfirm(true, true, Time.frameCount);
                ChangeTestMouse(testMouse, false);
                receiver.OnPointerClick(click);
                Check((int)director.Step == (suffix == "forget" ? 8 : 13),
                    suffix + " next independent pointer click selects exactly once");
                string after = ProgressFingerprint();
                receiver.OnPointerClick(click);
                Check(ProgressFingerprint() == after, suffix + " duplicate pointer event cannot select twice");
                yield return null;
            }
        }
        finally
        {
            if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        }
    }

    static void ChangeTestMouse(Mouse mouse, bool pressed)
    {
        // When Editor focus is elsewhere, Change's default update type can be
        // Editor while this coroutine is reading player buffers (or vice versa).
        // Write the currently active buffer explicitly. No input settings or
        // real mouse state need to change for the disposable test device.
        InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }
            .WithButton(MouseButton.Left, pressed), InputState.currentUpdateType);
    }

    static IEnumerator CheckPause(string name, Action pause, Action resume)
    {
        context = "pause " + name;
        Show("forget");
        string before = ProgressFingerprint();
        pause(); Pump(); Press();
        yield return new WaitForSecondsRealtime(.25f);
        Pump();
        Check(director.IsPaused && ProgressFingerprint() == before, name + " pauses the exact step and answers");
        Check(!link.LinkWindow.IsOpen && !link.TrySelectDisplayedOptionImmediately(
            OSCharacterInteractionIds.GlobalSequence, Prefix + "forget", "together"),
            name + " withdraws the question and rejects stale choice input");
        resume();
        // Re-enabling the hidden OS initializes focus from the real Editor.
        // Restore this fixture's foreground after testing the pause itself.
        director.DebugSetFocus(true);
        SetFieldIfPresent(os.Chapter02PaperPush, "hasFocus", true);
        SetFieldIfPresent(os.CursorRelease, "hasFocus", true);
        Pump();
        if (name == "console")
        {
            // Console deliberately retains its input gate for the closing
            // frame, preventing the dismissing click/key from reaching story.
            Check(GameTestConsole.BlocksGameplayInput && director.IsPaused,
                "console closing frame keeps its intentional input fence");
            Press();
            Check(ProgressFingerprint() == before,
                "console dismissing gesture cannot leak into the unanswered question");
            yield return null;
            Check(!GameTestConsole.BlocksGameplayInput,
                "console input fence clears on the following real frame");
            Pump();
        }
        Show("forget");
        Check(!director.IsPaused && ProgressFingerprint() == before, name + " resumes the same unanswered question");
    }

    static void CheckLiveReconstruction()
    {
        context = "protected Live reconstruction";
        Show("forget");
        string before = ProgressFingerprint();
        live.Close(); Settle();
        Check(live.IsOpen, "normal Live close is rejected during the protected mainline");
        live.Minimize(); Settle();
        Check(!live.IsMinimized && ProgressFingerprint() == before,
            "normal Live minimize is rejected without changing progress");
        live.CloseFromScript(); Settle();
        Check(!live.IsOpen, "external script can create a missing-Live fixture");
        Pump(); Show("forget");
        Check(live.IsOpen && !live.IsMinimized && ProgressFingerprint() == before,
            "externally closed Live reconstructs the same unanswered line");
        // A loaded/script-created minimized hierarchy bypasses the protected
        // user title/taskbar action, exactly as in the preceding talk QA.
        SetField(live, "isMinimized", true); live.gameObject.SetActive(false);
        Pump(); Show("forget");
        Check(live.IsOpen && !live.IsMinimized && ProgressFingerprint() == before,
            "externally minimized Live reconstructs the same unanswered line");
    }

    static void SetOSPresentation(GamePhase phase)
    {
        var callback = typeof(OS_Handle).GetMethod("OnPhaseChanged", Private);
        if (callback == null) throw new MissingMethodException("OS_Handle", "OnPhaseChanged");
        callback.Invoke(os, new object[] { phase });
    }

    static IEnumerator DiskRoundtrip(string name)
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var flow = GameSessionFlow.Instance;
        var expected = Capture();
        var oldOS = os;
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        Check(flow.ReturnToMainMenu(), name + " returns through the actual save transaction");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot
            && !GameManager.Instance.IsTransitioning && !flow.IsBusy, name + " menu ready", 65f);
        var stored = new GameAutosaveStorage(OutputDirectory).Read();
        Check(stored.Success, name + " isolated disk snapshot is readable");
        var disk = JsonUtility.FromJson<GameSaveData>(stored.Payload);
        Check(disk.osProcess.memory.chapter02FinalTalkStep == expected.osProcess.memory.chapter02FinalTalkStep,
            name + " disk records the exact current step");
        Check(flow.ContinueGame(), name + " continues isolated disk save");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning
            && OS_Handle.Instance != null && OS_Handle.Instance.IsOSVisible, name + " reconstruction ready", 85f);
        saves.EnterTestSession();
        Bind(); Pump();
        Check(os != oldOS, name + " reconstructs a new scene and director");
        CheckProgress(expected, name + " disk roundtrip");
        if (name == "ready") CheckReady("disk-ready"); else Show(name);
    }

    static void CheckProgress(GameSaveData expected, string label)
    {
        var data = GameDataManager.Instance;
        var m = data.OSProcessData.memory;
        var saved = expected.osProcess.memory;
        Check(m.chapter02FinalTalkStep == saved.chapter02FinalTalkStep
            && m.chapter02FinalTalkComfortAnswer == saved.chapter02FinalTalkComfortAnswer
            && m.chapter02FinalTalkQuestionAnswer == saved.chapter02FinalTalkQuestionAnswer
            && m.chapter02ReadAssistant == saved.chapter02ReadAssistant,
            label + " retains step, relation, and both choice checkpoints");
        Check(data.CurrentChapterIndex == expected.osProcess.currentChapterIndex
            && m.currentDataIndex == saved.currentDataIndex
            && JsonUtility.ToJson(data.SaveData.osResources) == JsonUtility.ToJson(expected.osResources),
            label + " preserves chapter, memory node, and all resources");
    }
    static void CheckReady(string label)
    {
        Check(director.IsChapter03EntryReady && director.Step == Step.Chapter03Ready
            && GameDataManager.Instance.CurrentChapterIndex == 2 && !link.LinkWindow.IsOpen,
            label + " has no dialogue replay");
        string before = ProgressFingerprint();
        Check(!director.TryEnterChapter03() && ProgressFingerprint() == before,
            label + " rejects an empty chapter-three range without side effects");
    }

    static void ValidateContent()
    {
        source = JsonUtility.FromJson<Source>(File.ReadAllText("Tools/Chapter02FinalTalk/source.zh-CN.json"));
        Check(source?.entries != null && source.entries.Length == 19, "approved source contains nineteen variants");
        var data = GameDataManager.Instance;
        var entries = data.OSCharacterInteractionData.GlobalEntries.Where(x => x.EntryId.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
        Check(entries.Length == 19 && entries.Select(x => x.EntryId).Distinct().Count() == 19,
            "runtime content contains exactly nineteen distinct talk entries");
        foreach (var authored in source.entries)
        {
            var entry = entries.Single(x => x.EntryId == Prefix + authored.id);
            Check(entry.FallbackText == authored.text, authored.id + " preserves the approved Chinese text");
            Check(entry.AutoCloseDelay == 0f && entry.OptionTimeoutDelay == 0f && !entry.AllowUnansweredTimeout,
                authored.id + " has no automatic continuation or answer timeout");
            Check(entry.Options.Count == (authored.options?.Length ?? 0), authored.id + " has the authored option count");
            CheckTranslations(entry.LocalizationKey);
            foreach (var option in entry.Options)
            {
                Check(authored.options.Any(x => x.id == option.OptionId && x.text == option.FallbackText),
                    authored.id + "/" + option.OptionId + " preserves the approved answer");
                var change = option.ResourceChange;
                Check(change == null || change.Heart == 0f && change.Battery == 0f && change.Fault == 0f,
                    authored.id + "/" + option.OptionId + " has zero authored resource cost or reward");
                CheckTranslations(option.LocalizationKey);
            }
        }
    }
    static void CheckTranslations(string key)
    {
        var entries = GameDataManager.Instance.LocalizationData.Entries.Where(x => x.Key == key).ToArray();
        Check(entries.Length == 1, key + " has exactly one localization record");
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
            Check(entries[0].Values.Count(x => x.Language == language && !string.IsNullOrWhiteSpace(x.Text)) == 1,
                key + " has an explicit " + language + " translation (not fallback)");
    }

    static IEnumerator CheckLayouts()
    {
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
        foreach (string name in new[] { "forget", "comfort_together", "question" })
        {
            context = "layout " + language + "/" + name;
            var saved = JsonUtility.FromJson<GameSaveData>(snapshots[name]);
            saved.flow.language = language;
            Install(saved); Finish(name);
            link.WindowLayout.RefreshLayout(); Settle();
            yield return null;
            link.WindowLayout.RefreshLayout(); Settle();
            TMP_Text label = name == "comfort_together" ? link.DisplayedTextLabel
                : GetField(link, "textWithOptionsLabel") as TMP_Text;
            Check(label != null && label.gameObject.activeInHierarchy, context + " has a visible body");
            label.ForceMeshUpdate();
            float preferred = label.GetPreferredValues(label.text, label.rectTransform.rect.width, Mathf.Infinity).y;
            float height = label.rectTransform.rect.height;
            Check(preferred <= height + 2f && !label.isTextTruncated,
                context + " body fits (required " + preferred.ToString("F1") + ", available " + height.ToString("F1") + ")");
            if (name != "comfort_together")
            {
                string[] options = name == "forget" ? new[] { "together", "silent" } : new[] { "ask", "wait" };
                foreach (var option in options)
                {
                    Check(link.TryGetActiveOptionTarget(option, out RectTransform target)
                        && target.gameObject.activeInHierarchy && target.rect.width > 0 && target.rect.height > 0,
                        context + " option " + option + " has a visible hit target");
                    var text = target.GetComponent<TMP_Text>();
                    if (text != null)
                    {
                        text.ForceMeshUpdate();
                        Check(!text.isTextTruncated && text.preferredHeight <= text.rectTransform.rect.height + 2f,
                            context + " option " + option + " is not clipped");
                    }
                }
            }
            layouts.Add(new { language = language.ToString(), entry = name, preferredHeight = preferred, height });
        }
    }

    // Expected story order is deliberately independent of the director's lookup.
    static string Expected(int step, int relation, int comfort, int question)
    {
        string[] common = { null, "opening", "company", "introduction", "skills", null, "grave", "forget",
            null, "notes", "identity", "research", "question", null, "night", null };
        if (step == 5) return relation == 1 ? "role_assistant" : "role_partner";
        if (step == 8) return comfort == 1 ? "comfort_together" : "comfort_silent";
        if (step == 13) return question == 1 ? "question_ask" : "question_wait";
        if (step == 15) return relation == 1 ? "goodbye_assistant" : "goodbye_partner";
        return step >= 0 && step < common.Length ? common[step] : null;
    }
    static string ProgressFingerprint()
    {
        var d = GameDataManager.Instance;
        var m = d.OSProcessData.memory;
        return d.CurrentChapterIndex + ":" + m.currentDataIndex + ":" + m.chapter02FinalTalkStep + ":"
            + m.chapter02ReadAssistant + ":" + m.chapter02FinalTalkComfortAnswer + ":"
            + m.chapter02FinalTalkQuestionAnswer + ":" + JsonUtility.ToJson(d.SaveData.osResources);
    }
    static string Describe() => "step=" + director?.Step + ", entry=" + link?.DisplayedEntryId
        + ", typed=" + director?.DebugMessageTyped + ", paused=" + director?.IsPaused
        + ", liveOpen=" + live?.IsOpen + ", liveMinimized=" + live?.IsMinimized
        + ", editorFocus=" + Application.isFocused + ", directorFocus=" + GetField(director, "hasFocus")
        + ", silentBeat=" + GetField(director, "silentBeat") + ", osVisible=" + os?.IsOSVisible
        + ", phase=" + GameManager.Instance?.Phase + ", flowBusy=" + GameSessionFlow.Instance?.IsBusy
        + ", restoring=" + OSSessionPersistence.IsRestoring + ", consoleBlock=" + GameTestConsole.BlocksGameplayInput
        + ", baseInputLock=" + os?.IsSystemInputLockedWithoutChapter02PaperPush
        + ", settingsOpen=" + settings?.IsOpen + ", settingsMinimized=" + settings?.IsMinimized
        + ", settingsActive=" + settings?.gameObject.activeInHierarchy;
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 20f, Action maintainFixture = null)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until)
        {
            maintainFixture?.Invoke();
            yield return null;
        }
        Check(condition(), label);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        if (!checks.Contains(label)) checks.Add(label);
    }
    static object GetField(object target, string name) => target?.GetType().GetField(name, Private)?.GetValue(target);
    static void SetField(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Private);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static void SetFieldIfPresent(object target, string name, object value) => target?.GetType().GetField(name, Private)?.SetValue(target, value);
    static void CompleteTween(object target, string name)
    {
        var tween = GetField(target, name) as Tween;
        if (tween != null && tween.IsActive()) tween.Complete(true);
    }
    static string Fingerprint(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
