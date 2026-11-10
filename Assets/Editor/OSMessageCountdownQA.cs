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
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in Play Mode regression for the visible Message reading clock. The caller
// owns Play Mode. All test entries belong to a disposable runtime data clone.
public static class OSMessageCountdownQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Sequence = "qa.message_countdown";
    const float ReadingSeconds = 2.4f;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> samples = new List<object>();
    static readonly List<object> events = new List<object>();
    static readonly Dictionary<string, float> typedAt = new Dictionary<string, float>();
    static readonly Dictionary<string, float> startedAt = new Dictionary<string, float>();
    static OS_Handle os;
    static OSLinkMessageController messages;
    static OSWindowUnit settings;
    static OSCharacterInteractionData_SO runtimeData;
    static EditorWindow oldFocus, gameView;
    static Mouse testMouse;
    static GameSetup setup;
    static string context;
    static bool cancel;
    static float lastClosedAt;
    static OSWindowUnit Window => messages?.LinkWindow;
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
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        gameView = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().FullName == "UnityEditor.GameView");
        if (setup == null || gameView == null) return "Active GameSetup and an existing Game view are required";
        oldFocus = EditorWindow.focusedWindow;
        checks.Clear(); samples.Clear(); events.Clear(); typedAt.Clear(); startedAt.Clear();
        os = null; messages = null; settings = null; runtimeData = null; cancel = false;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Tools", "MessageCountdown", "Validation",
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
        var originalData = data.OSCharacterInteractionData;
        var language = data.CurrentLanguageState;
        bool background = Application.runInBackground;
        bool demo = (bool)Get(setup, "isDemo");
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        var previousMouse = Mouse.current;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = paths.Select(Hash).ToArray();
            string storage = Path.GetFullPath(Path.Combine("Temp", "OSMessageCountdownQA", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(storage);
            saves.UseIsolatedStorageForTests(storage); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); Application.runInBackground = true;
            Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("OSMessageCountdownQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            testMouse.MakeCurrent(); gameView.Focus();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (cancel) throw new OperationCanceledException("QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = (error is OperationCanceledException ? "CANCELLED" : "FAILED") + " at " + context + "\n" + error;
                    Debug.LogError("[Message Countdown QA] " + Status); yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " countdown checks";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (messages != null)
                {
                    messages.EntryPresentationStarted -= OnStarted;
                    messages.EntryTextPlaybackCompleted -= OnTyped;
                    Window.Closed -= OnClosed;
                    messages.NotifyOSHidden();
                }
                if (settings != null && settings.IsOpen) settings.CloseSilently();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                Set(data, "osCharacterInteractionData", originalData);
                if (runtimeData != null) UnityEngine.Object.Destroy(runtimeData);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (oldFocus != null) oldFocus.Focus();
                Check(ReferenceEquals(data.SaveData, original) && data.CurrentLanguageState == language
                    && ReferenceEquals(data.OSCharacterInteractionData, originalData) && GameSetup.IsDemo == demo,
                    "restored original save, language, interaction data reference and demo mode");
                Check(hashes == null || paths.Select(Hash).SequenceEqual(hashes), "original save hashes preserved after cleanup");
                checks.Add("restored resource component, input device and editor focus; storage remains isolated until Play Mode exits");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new
            {
                status = Status, context, authoredReadingSeconds = ReadingSeconds, checks, events,
                samples, originalFiles = paths, originalHashes = hashes, finalHashes = paths.Select(Hash).ToArray()
            }, Formatting.Indented));
        }
    }

    static IEnumerator Run()
    {
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture), "isolated checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy, "checkpoint completed", 65f);
        Check(GameTest.Instance.JumpSucceeded, "checkpoint succeeded");
        os = OS_Handle.Instance; messages = os?.LinkMessageController;
        Check(messages != null && os.IsOSVisible, "production OS and Message are available");
        messages.NotifyOSHidden();
        os.MemoryWindowController?.GetComponent<OSWindowUnit>().CloseSilently();
        BuildRuntimeData();
        GameDataManager.Instance.SetCurrentLanguage(LanguageState.zh_CN);
        messages.EntryPresentationStarted += OnStarted;
        messages.EntryTextPlaybackCompleted += OnTyped;
        Window.Closed += OnClosed;
        settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();

        yield return VerifyQueueArrivals();

        context = "ordinary auto-close";
        yield return Present("plain");
        yield return Wait(() => typedAt.ContainsKey(Id("plain")), "plain text finishes");
        yield return AssertSameUntil("plain", typedAt[Id("plain")] + ReadingSeconds - .08f);
        yield return Wait(() => !Window.IsOpen, "plain message closes after reading");
        Check(lastClosedAt - typedAt[Id("plain")] >= ReadingSeconds - .08f, "auto-close retains the complete authored reading hold");

        context = "sequence arrival during reading";
        yield return Present("plain");
        yield return Wait(() => typedAt.ContainsKey(Id("plain")), "sequence source text finishes");
        yield return Hold(.35f);
        Check(messages.TryNotifyExternalSignal(Signal("incoming")), "ordinary sequence arrival accepted");
        yield return AssertSameUntil("plain", typedAt[Id("plain")] + ReadingSeconds - .08f);
        yield return Wait(() => messages.DisplayedEntryId == Id("incoming"), "queued sequence arrival is released");
        Check(startedAt[Id("incoming")] - typedAt[Id("plain")] >= ReadingSeconds - .08f, "ordinary sequence arrival cannot steal a visible reading countdown");

        context = "global arrival during reading";
        yield return Present("plain");
        yield return Wait(() => typedAt.ContainsKey(Id("plain")), "global source text finishes");
        yield return Hold(.35f);
        Check(messages.NotifyGlobalExternalSignal(Signal("global")), "ordinary global arrival accepted");
        yield return AssertSameUntil("plain", typedAt[Id("plain")] + ReadingSeconds - .08f);
        yield return Wait(() => messages.DisplayedEntryId == Id("global"), "queued global arrival is released");
        Check(startedAt[Id("global")] - typedAt[Id("plain")] >= ReadingSeconds - .08f, "ordinary global arrival cannot steal a visible reading countdown");

        context = "timed next-entry continuation";
        yield return Present("next");
        yield return Wait(() => typedAt.ContainsKey(Id("next")), "continuation source text finishes");
        float nextDeadline = typedAt[Id("next")] + ReadingSeconds;
        yield return AssertSameUntil("next", nextDeadline - .08f, true);
        yield return Wait(() => messages.DisplayedEntryId == Id("terminal"), "timed continuation advances");
        Check(startedAt[Id("terminal")] >= nextDeadline - .08f, "NextEntryDelay uses the visible countdown without early advancement");

        context = "timed continuation with ambient arrival";
        yield return Present("next");
        yield return Wait(() => typedAt.ContainsKey(Id("next")), "reserved continuation source text finishes");
        yield return Hold(.35f);
        Check(messages.TryNotifyExternalSignal(Signal("incoming")), "ambient arrival during NextEntryDelay is accepted");
        yield return AssertSameUntil("next", typedAt[Id("next")] + ReadingSeconds - .08f, true);
        yield return Wait(() => messages.DisplayedEntryId == Id("terminal"), "authored successor passes the queued ambient message");
        Check(!startedAt.ContainsKey(Id("incoming")), "ambient arrival does not split the authored continuation chain");
        yield return Wait(() => typedAt.ContainsKey(Id("terminal")), "terminal continuation text finishes");
        yield return AssertSameUntil("terminal", typedAt[Id("terminal")] + ReadingSeconds - .08f);
        yield return Wait(() => messages.DisplayedEntryId == Id("incoming"), "ambient FIFO drains after the terminal reading hold");
        Check(startedAt[Id("incoming")] - typedAt[Id("terminal")] >= ReadingSeconds - .08f,
            "queued ambient arrival follows the entire authored chain and its final countdown");

        yield return VerifyNextEntryPause(false);
        yield return VerifyNextEntryPause(true);

        context = "delayed continuation with two ambient arrivals";
        yield return Present("next_delayed");
        yield return Wait(() => typedAt.ContainsKey(Id("next_delayed")), "delayed continuation source text finishes");
        yield return Hold(.35f);
        Check(messages.TryNotifyExternalSignal(Signal("incoming")), "first ambient arrival is queued during NextEntryDelay");
        yield return AssertSameUntil("next_delayed", typedAt[Id("next_delayed")] + ReadingSeconds - .08f, true);
        yield return Wait(() => GameDataManager.Instance.OSCharacterInteractionState.pendingEntryId == Id("delayed_terminal"),
            "authored successor reaches its real TriggerDelay");
        float triggerDelayStartedAt = Time.realtimeSinceStartup;
        Check(!startedAt.ContainsKey(Id("delayed_terminal")), "delayed successor has not presented before its trigger delay");
        Check(messages.TryNotifyExternalSignal(Signal("late_incoming")), "second ambient arrival is queued during TriggerDelay");
        yield return Wait(() => messages.DisplayedEntryId == Id("delayed_terminal"), "delayed authored successor retains the reserved slot");
        Check(startedAt[Id("delayed_terminal")] - triggerDelayStartedAt >= .35f,
            "authored successor honors its 0.45-second TriggerDelay");
        Check(!startedAt.ContainsKey(Id("incoming")) && !startedAt.ContainsKey(Id("late_incoming")),
            "neither ambient arrival bypasses the delayed authored successor");
        yield return Wait(() => typedAt.ContainsKey(Id("delayed_terminal")), "delayed terminal text finishes");
        yield return AssertSameUntil("delayed_terminal", typedAt[Id("delayed_terminal")] + ReadingSeconds - .08f);
        yield return Wait(() => messages.DisplayedEntryId == Id("incoming"), "first ambient arrival follows the delayed terminal reading hold");
        Check(!startedAt.ContainsKey(Id("late_incoming")), "later ambient arrival preserves FIFO order");
        yield return Wait(() => typedAt.ContainsKey(Id("incoming")), "first ambient text finishes");
        yield return AssertSameUntil("incoming", typedAt[Id("incoming")] + ReadingSeconds - .08f);
        yield return Wait(() => messages.DisplayedEntryId == Id("late_incoming"), "second ambient arrival is eventually released");
        Check(startedAt[Id("late_incoming")] - typedAt[Id("incoming")] >= ReadingSeconds - .08f,
            "both ambient arrivals retain their own reading time after the delayed chain");

        context = "explicit user close";
        yield return Present("plain");
        yield return Wait(() => typedAt.ContainsKey(Id("plain")), "manual-close source text finishes");
        yield return Hold(.35f);
        Check(LineFraction() > .5f, "manual close occurs while the reading line has time remaining");
        Window.Close();
        yield return Frame();
        Check(!Window.IsOpen, "explicit user close remains allowed before automatic expiry");

        context = "settings pause";
        Check(settings != null, "real settings window resolves");
        yield return Present("plain");
        yield return Wait(() => typedAt.ContainsKey(Id("plain")), "pause source text finishes");
        yield return Hold(.35f);
        settings.OpenStandalone(0f);
        yield return Frame(); yield return Frame();
        float pausedFraction = LineFraction();
        Check(pausedFraction > .5f, "settings pause begins with visible time remaining");
        float pauseStart = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - pauseStart < ReadingSeconds + .3f)
        {
            CheckVisible(Id("plain"));
            if (Mathf.Abs(LineFraction() - pausedFraction) > .035f)
                throw new InvalidOperationException("settings pause changed the visible reading countdown");
            yield return Frame();
        }
        checks.Add("settings pause retains the same message and visible countdown beyond its original expiry");
        settings.CloseSilently(); gameView.Focus();
        float resumedAt = Time.realtimeSinceStartup;
        yield return AssertSameUntil("plain", resumedAt + ReadingSeconds * pausedFraction - .12f);
        yield return Wait(() => !Window.IsOpen, "reading countdown closes after settings resume");
        Check(lastClosedAt - resumedAt >= ReadingSeconds * pausedFraction - .12f, "resume closes after the remaining visible time");

        context = "minimize and restore";
        yield return Present("plain");
        yield return Wait(() => typedAt.ContainsKey(Id("plain")), "minimize source text finishes");
        yield return Hold(.35f);
        float beforeMinimize = LineFraction();
        Window.Minimize();
        Check(Window.IsMinimized && Window.IsOpen, "Message minimizes without being closed");
        yield return Hold(ReadingSeconds + .3f);
        Check(Window.IsOpen && Window.IsMinimized, "hidden reading countdown cannot close the minimized window");
        Window.RestoreAndBringToFront();
        yield return Frame(); yield return Frame();
        float afterRestore = LineFraction();
        Check(afterRestore >= beforeMinimize - .04f, "restored visible reading time was not consumed while minimized");
        resumedAt = Time.realtimeSinceStartup;
        yield return AssertSameUntil("plain", resumedAt + ReadingSeconds * afterRestore - .12f);
        yield return Wait(() => !Window.IsOpen, "restored reading countdown completes");
        checks.Add("minimize restoration keeps the line and close timer consistent");
    }

    static IEnumerator VerifyNextEntryPause(bool minimize)
    {
        context = minimize ? "next-entry minimize pause" : "next-entry settings pause";
        Check(settings != null, "settings window resolves for continuation pause");
        yield return Present("next");
        yield return Wait(() => typedAt.ContainsKey(Id("next")), "continuation pause source text finishes");
        yield return Hold(.35f);
        if (minimize) Window.Minimize(); else settings.OpenStandalone(0f);
        yield return Frame(); yield return Frame();
        var state = GameDataManager.Instance.OSCharacterInteractionState;
        float pausedRemaining = state.nextEntryCountdownRemaining;
        Check(pausedRemaining > ReadingSeconds * .5f, "continuation pause begins with most reading time remaining");
        float pauseStart = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - pauseStart < ReadingSeconds + .3f)
        {
            if (minimize)
            {
                if (!Window.IsOpen || !Window.IsMinimized)
                    throw new InvalidOperationException("NextEntryDelay closed or reopened the minimized Message");
            }
            else CheckVisible(Id("next"));
            if (Mathf.Abs(state.nextEntryCountdownRemaining - pausedRemaining) > .04f)
                throw new InvalidOperationException("NextEntryDelay consumed time while suspended");
            if (!minimize && Mathf.Abs(LineFraction() - pausedRemaining / ReadingSeconds) > .04f)
                throw new InvalidOperationException("suspended NextEntryDelay and its visible line disagree");
            if (startedAt.ContainsKey(Id("terminal")))
                throw new InvalidOperationException("NextEntryDelay presented its successor while suspended");
            yield return Frame();
        }
        checks.Add(context + " preserves the saved clock beyond its original deadline");
        float resumedAt = Time.realtimeSinceStartup;
        if (minimize) Window.RestoreAndBringToFront(); else settings.CloseSilently();
        gameView.Focus();
        yield return AssertSameUntil("next", resumedAt + pausedRemaining - .12f, true);
        yield return Wait(() => messages.DisplayedEntryId == Id("terminal"), "continuation advances after resume");
        Check(startedAt[Id("terminal")] - resumedAt >= pausedRemaining - .12f,
            context + " honors its remaining time after resume");
    }

    static IEnumerator VerifyQueueArrivals()
    {
        context = "global arrivals during initial trigger delay";
        yield return Present("plain");
        Window.Close(); yield return Frame();
        Check(messages.NotifyGlobalExternalSignal(Signal("delayed_global")), "delayed global arrival accepted");
        Check(messages.NotifyGlobalExternalSignal(Signal("global")), "second global arrival accepted");
        Check(!startedAt.ContainsKey(Id("global")), "new global cannot overtake the initial delayed global");
        yield return Wait(() => typedAt.ContainsKey(Id("delayed_global")), "initial delayed global finishes typing");
        yield return Wait(() => startedAt.ContainsKey(Id("global")), "second global drains after the first");
        Check(startedAt[Id("global")] >= typedAt[Id("delayed_global")] + ReadingSeconds - .08f,
            "initial delayed global retains its full reading hold");

        context = "global arrival during sequence typewriter";
        yield return Present("plain");
        Check(!typedAt.ContainsKey(Id("plain")), "sequence fixture is still typing");
        Check(messages.NotifyGlobalExternalSignal(Signal("global")), "global arrival during typing accepted");
        Check(messages.DisplayedEntryId == Id("plain"), "global cannot consume/replace an unfinished sequence line");
        yield return Wait(() => typedAt.ContainsKey(Id("plain")), "uninterrupted sequence text finishes");
        yield return AssertSameUntil("plain", typedAt[Id("plain")] + ReadingSeconds - .08f);
        yield return Wait(() => messages.DisplayedEntryId == Id("global"), "global drains after typewriter plus reading hold");

        context = "completed untimed text yields to its queued successor";
        yield return Present("untimed");
        Check(messages.NotifyGlobalExternalSignal(Signal("global")), "global arrival behind untimed text accepted");
        Check(messages.DisplayedEntryId == Id("untimed"), "untimed line retains its typewriter");
        yield return Wait(() => messages.DisplayedEntryId == Id("global"), "untimed line releases queued successor without X");

        context = "unanswered choice and its reply precede ambient arrivals";
        yield return Present("plain"); Window.Close(); yield return Frame();
        Check(messages.NotifyGlobalExternalSignal(Signal("question")), "non-expiring choice accepted");
        yield return Wait(() => messages.IsWaitingForOption, "non-expiring choice is selectable");
        Check(messages.NotifyGlobalExternalSignal(Signal("global")), "ambient global during choice accepted");
        Check(messages.DisplayedEntryId == Id("question") && messages.IsWaitingForOption,
            "ambient global cannot consume/replace an unanswered choice");
        Check(messages.TrySelectDisplayedOptionImmediately("global", Id("question"), "answer"), "player can still answer the choice");
        Check(messages.DisplayedEntryId == Id("reply"), "answer reply precedes the older queued ambient message");
        yield return Wait(() => typedAt.ContainsKey(Id("reply")), "answer reply finishes");
        yield return Wait(() => messages.DisplayedEntryId == Id("global"), "ambient queue drains after reply");
        Check(startedAt[Id("global")] >= typedAt[Id("reply")] + ReadingSeconds - .08f,
            "answer reply retains its full reading hold");
    }

    static void BuildRuntimeData()
    {
        var data = GameDataManager.Instance;
        runtimeData = UnityEngine.Object.Instantiate(data.OSCharacterInteractionData);
        runtimeData.name = "OSMessageCountdownQA_RuntimeOnly";
        runtimeData.hideFlags = HideFlags.DontSave;
        var sequence = new OSCharacterInteractionSequenceData();
        Set(sequence, "sequenceId", Sequence);
        var entries = new List<OSCharacterInteractionEntryData>
        {
            Entry("plain", ReadingSeconds), Entry("incoming", ReadingSeconds), Entry("untimed", 0f),
            Entry("next", 0f, Id("terminal")), Entry("terminal", ReadingSeconds),
            Entry("next_delayed", 0f, Id("delayed_terminal")),
            Entry("delayed_terminal", ReadingSeconds, triggerDelay: .45f), Entry("late_incoming", ReadingSeconds)
        };
        Set(sequence, "entries", entries);
        ((List<OSCharacterInteractionSequenceData>)Get(runtimeData, "sequences")).Add(sequence);
        // Ambient arrivals would make a controlled timing experiment ambiguous.
        // Only the disposable clone is changed; all production assets stay intact.
        var question = Entry("question", 0f);
        Set(question, "partType", OSCharacterInteractionPartType.TextWithOptions);
        var answer = new OSCharacterInteractionOptionData();
        Set(answer, "optionId", "answer"); Set(answer, "fallbackText", "继续"); Set(answer, "nextEntryId", Id("reply"));
        Set(question, "options", new List<OSCharacterInteractionOptionData> { answer });
        Set(runtimeData, "globalEntries", new List<OSCharacterInteractionEntryData>
        {
            Entry("global", ReadingSeconds), Entry("delayed_global", ReadingSeconds, triggerDelay: .45f),
            question, Entry("reply", ReadingSeconds)
        });
        Set(data, "osCharacterInteractionData", runtimeData);
        Check(!ReferenceEquals(runtimeData, UnityEditor.AssetDatabase.LoadAssetAtPath<OSCharacterInteractionData_SO>(
            "Assets/Data/OSCharacterInteractionData.asset")), "all fixture edits belong to a runtime clone");
    }

    static OSCharacterInteractionEntryData Entry(string name, float autoClose, string next = null, float triggerDelay = 0f)
    {
        var entry = new OSCharacterInteractionEntryData();
        Set(entry, "entryId", Id(name)); Set(entry, "triggerType", OSCharacterInteractionTriggerType.ExternalSignal);
        Set(entry, "triggerTargetId", Signal(name)); Set(entry, "partType", OSCharacterInteractionPartType.Text);
        Set(entry, "fallbackText", "阅读倒计时验证：" + name + "。这句话读完以后，底部细线应与实际剩余时间同步。");
        Set(entry, "replacementPolicy", OSCharacterInteractionReplacementPolicy.WaitForCurrentTextComplete);
        Set(entry, "autoCloseDelay", autoClose); Set(entry, "repeatable", true);
        Set(entry, "triggerDelay", triggerDelay);
        if (next != null) { Set(entry, "nextEntryId", next); Set(entry, "nextEntryDelay", ReadingSeconds); }
        return entry;
    }

    static IEnumerator Present(string name)
    {
        if (settings != null && settings.IsOpen) settings.CloseSilently();
        messages.NotifyOSHidden();
        var state = GameDataManager.Instance.OSCharacterInteractionState;
        state.ActivateSequence("qa.message_reset"); state.ActivateSequence(Sequence);
        state.pendingGlobalEntryId = state.presentingGlobalEntryId = null;
        state.optionCountdownQueue.Clear(); state.optionCountdownKey = state.optionCountdownReplyKey = state.optionCountdownReleaseKey = null;
        typedAt.Clear(); startedAt.Clear(); lastClosedAt = -1f;
        messages.SetGlobalMessagesSuppressed(false);
        os.OpenCharacterWindowForReconnect(0f);
        messages.NotifyOSBecameVisible(); gameView.Focus();
        yield return Frame();
        Check(messages.TryPresentEntry(Id(name)), context + " starts through the production entry point");
        yield return Wait(() => messages.DisplayedEntryId == Id(name) && Window.IsOpen && !Window.IsOpening,
            context + " is visible");
    }

    static IEnumerator AssertSameUntil(string name, float deadline, bool continuation = false)
    {
        while (Time.realtimeSinceStartup < deadline)
        {
            CheckVisible(Id(name));
            if (continuation && LineFraction() >= 0f)
            {
                var state = GameDataManager.Instance.OSCharacterInteractionState;
                float expected = Mathf.Clamp01(state.nextEntryCountdownRemaining / ReadingSeconds);
                if (Mathf.Abs(LineFraction() - expected) > .04f)
                    throw new InvalidOperationException("NextEntryDelay and its visible line disagree");
            }
            yield return Frame();
        }
        checks.Add(context + " remains visible for its remaining reading time");
    }

    static void CheckVisible(string id)
    {
        if (Window == null || !Window.IsOpen || Window.IsClosing || messages.DisplayedEntryId != id)
            throw new InvalidOperationException("Message ended or was replaced before its visible countdown: expected "
                + id + ", actual " + messages.DisplayedEntryId + ", open=" + Window?.IsOpen + ", line=" + LineFraction());
    }

    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 8f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return Frame();
        Check(condition(), label);
    }
    static IEnumerator Hold(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline) yield return Frame();
    }
    static IEnumerator Frame()
    {
        yield return new WaitForEndOfFrame();
        if (messages == null) yield break;
        var state = GameDataManager.Instance.OSCharacterInteractionState;
        samples.Add(new { context, frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
            entry = messages.DisplayedEntryId, open = Window.IsOpen, minimized = Window.IsMinimized,
            presenting = messages.IsPresenting, line = LineFraction(),
            nextKey = state.nextEntryCountdownKey, nextRemaining = state.nextEntryCountdownRemaining });
    }
    static float LineFraction()
    {
        if (messages == null) return -1f;
        var root = Get(messages, "waitLineRoot") as RectTransform;
        var fill = Get(messages, "waitLineFill") as Image;
        if (root == null || fill == null || !root.gameObject.activeInHierarchy || root.rect.width <= 0f) return -1f;
        return fill.enabled ? fill.rectTransform.rect.width / root.rect.width : 0f;
    }
    static void OnStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        startedAt[entry.EntryId] = Time.realtimeSinceStartup;
        events.Add(new { context, kind = "started", entry = entry.EntryId, realtime = Time.realtimeSinceStartup });
    }
    static void OnTyped(string sequence, OSCharacterInteractionEntryData entry)
    {
        typedAt[entry.EntryId] = Time.realtimeSinceStartup;
        events.Add(new { context, kind = "typed", entry = entry.EntryId, realtime = Time.realtimeSinceStartup });
    }
    static void OnClosed(OSWindowUnit window)
    {
        lastClosedAt = Time.realtimeSinceStartup;
        events.Add(new { context, kind = "closed", entry = messages.DisplayedEntryId, realtime = lastClosedAt, line = LineFraction() });
    }
    static string Id(string name) => "qa_countdown_" + name;
    static string Signal(string name) => "qa.countdown." + name;
    static object Get(object value, string name) => value.GetType().GetField(name, Fields)?.GetValue(value);
    static void Set(object value, string name, object fieldValue)
    {
        var field = value.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(value.GetType().Name, name);
        field.SetValue(value, fieldValue);
    }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
    static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
