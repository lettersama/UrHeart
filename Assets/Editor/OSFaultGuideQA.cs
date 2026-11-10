using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;

// Opt-in Play Mode checks. All disk writes use a fresh Temp directory. The
// disposable board's story-completion listener is detached; leave Play after QA.
public static class OSFaultGuideQA
{
    const int InitialMask = 0x8003, PartialMask = 0x80E5;
    static readonly List<string> checks = new List<string>();
    static OS_Handle os;
    static OSStateCheckWindowController board;
    static OSWindowUnit window;
    static int completionCount;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

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
        checks.Clear(); ReleaseBoard(); completionCount = 0;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSFaultGuideQA", Guid.NewGuid().ToString("N")));
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
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
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
                    Status = "FAILED: " + error;
                    Debug.LogError("[Fault Guide QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " fault-guide checks";
            Debug.Log("[Fault Guide QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            ReleaseBoard();
            GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
            saves.EnterTestSession(); saves.EndSession();
            GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
            if (resources != null) resources.enabled = resourcesEnabled;
            Application.runInBackground = background;
            if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes))
                Status = "FAILED: original save files changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted before completion";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        yield return FreshFault();
        string resources = JsonUtility.ToJson(data.SaveData.osResources);
        yield return Wait(() => board.IsFaultGuidePlaying, "fresh board starts its wordless guide");
        CheckMask(InitialMask, false, "demonstration keeps the real board untouched");
        Check(!board.ToggleNode(1), "real toggles are rejected during demonstration");
        Check(completionCount == 0, "demonstration never emits completion");
        yield return Wait(() => typeof(OSStateCheckWindowController)
            .GetField("faultGuideSequence", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(board) != null, "guide animation has actually started");
        int popupCount = os.ErrorWindowPoolController.ActiveWindowCount;
        Check(float.IsPositiveInfinity(os.ErrorWindowPoolController.CurrentInterval),
            "popup generation pauses during demonstration");
        yield return new WaitForSecondsRealtime(1.05f);
        Check(os.ErrorWindowPoolController.ActiveWindowCount == popupCount,
            "demonstration does not spawn additional popups");
        yield return Shot("01-guide-playing");
        yield return Wait(() => board.IsGameplayEnabled && !board.IsFaultGuidePlaying,
            "guide hands control back to the player");
        var afterGuide = Capture();
        Check(afterGuide.faultGuideSeen && !afterGuide.faultBoardInteracted,
            "guide completion persists seen without pretending the player clicked");
        CheckMask(InitialMask, false, "guide completion preserves initial mask");
        Check(board.IsNodeOpen(1) && board.IsNodePowered(1)
            && !board.IsNodeOpen(2) && !board.IsNodePowered(2),
            "initial power reaches the first adjacent node and stops at the next gate");
        Check(board.IsAwaitingFirstFaultInteraction, "first-click hint waits after demonstration");
        Check(os.ErrorWindowPoolController.CurrentInterval >= 2.5f,
            "popups remain slow until first real interaction");
        Check(completionCount == 0 && Mathf.Approximately(data.CurrentStateCheckProgress, 1f)
            && JsonUtility.ToJson(data.SaveData.osResources) == resources,
            "guide does not repair the system or change resources");
        yield return Shot("02-first-click-hint");
        var unitGrid = (RectTransform)typeof(OSStateCheckWindowController)
            .GetField("unitGrid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(board);
        var pointer = unitGrid.GetChild(1).GetComponent<OSStateCheckFaultNodePointer>();
        Check(pointer != null, "clickable node has hover relay");
        pointer.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
        yield return Shot("02b-hover-linked-nodes");
        pointer.OnPointerExit(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));

        Check(board.ToggleNode(1), "first real node accepts input");
        yield return null;
        CheckMask(0x8005, false, "first real node applies exactly one toggle mask");
        Check(Capture().faultBoardInteracted && !board.IsAwaitingFirstFaultInteraction,
            "first successful interaction stops the hint");
        Check(os.ErrorWindowPoolController.CurrentInterval < 1f,
            "first interaction restores normal popup pacing");
        window.Close();
        yield return Wait(() => !window.IsOpen, "ordinary close accepted");
        CheckMask(0x8005, false, "closed board remains capturable immediately");
        Check(os.OpenStateCheck(1f), "reopen partially changed board");
        yield return Wait(() => board.IsGameplayEnabled, "reopened board is playable");
        CheckMask(0x8005, false, "ordinary reopen preserves switches");
        Check(!board.IsFaultGuidePlaying && !board.IsAwaitingFirstFaultInteraction,
            "ordinary reopen does not replay guide or hint");

        Check(board.ToggleNode(6), "build non-completed partial board");
        var partial = Capture();
        CheckMask(PartialMask, false, "partial board fixture is correct");
        window.Minimize();
        Check(window.IsMinimized, "partial board minimizes");
        window.RestoreAndBringToFront();
        yield return null;
        CheckMask(PartialMask, false, "minimize and restore preserve switches");
        Check(board.ToggleNode(1), "change state before restoring a snapshot");
        board.RestorePersistentBoard(JsonUtility.FromJson<OSResumeSaveData>(JsonUtility.ToJson(partial)));
        yield return null;
        CheckMask(PartialMask, false, "explicit persistent restore reinstates all switches");
        Check(!board.IsFaultGuidePlaying && !board.IsAwaitingFirstFaultInteraction
            && Capture().faultGuideSeen && Capture().faultBoardInteracted && completionCount == 0,
            "snapshot restore retains guide state without completion");
        yield return Shot("03-partial-board");

        yield return DiskRoundtrip(false);
        yield return DiskRoundtrip(true);
        yield return FreshFault();
        yield return Wait(() => board.IsGameplayEnabled && !board.IsFaultGuidePlaying,
            "new checkpoint finishes a fresh guide");
        CheckMask(InitialMask, false, "second checkpoint starts a new board");
        Check(!Capture().faultBoardInteracted, "checkpoint clears previous interaction state");
        Check(board.ToggleNode(2) && board.ToggleNode(11),
            "2-11 solution connects the extended entry segment to the heart");
        yield return Wait(() => completionCount == 1, "real solution emits completion once");
        Check(Capture().faultBoardCompleted && board.IsNodePowered(15) && !board.IsGameplayEnabled,
            "real solution powers target and stops gameplay");
        yield return new WaitForSecondsRealtime(.5f);
        Check(completionCount == 1 && Mathf.Approximately(data.CurrentStateCheckProgress, 1f),
            "isolated completion does not launch the story reboot");
    }

    static IEnumerator FreshFault()
    {
        ReleaseBoard(); completionCount = 0;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.PrologueFault), "fault checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning
            && !GameManager.Instance.IsTransitioning && !SceneLoadManager.Instance.IsBusy,
            "fault checkpoint ready", 70f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession,
            "fault checkpoint succeeds with autosave isolation");
        Check(!GameDataManager.Instance.SaveData.osResume.faultGuideSeen
            && !GameDataManager.Instance.SaveData.osResume.faultBoardInteracted,
            "checkpoint clears guide and interaction checkpoints");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        BindBoard();
        Check(os.OpenStateCheck(1f), "open actual fault window");
    }

    static IEnumerator DiskRoundtrip(bool closeBeforeSaving)
    {
        string label = closeBeforeSaving ? "closed window" : "open window";
        if (closeBeforeSaving) window.Close();
        CheckMask(PartialMask, false, label + " captures the partial mask");
        var saves = GameAutosaveService.Instance;
        var flow = GameSessionFlow.Instance;
        saves.ResumeIsolatedWrites(); saves.BeginSession(GameDataManager.Instance.SaveData);
        Check(flow.ReturnToMainMenu(), label + " saves before menu");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot
            && !GameManager.Instance.IsTransitioning && !flow.IsBusy, label + " menu ready", 60f);
        ReleaseBoard();
        Check(flow.ContinueGame(), label + " continues isolated disk save");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning
            && OS_Handle.Instance != null && OS_Handle.Instance.IsOSVisible, label + " reload complete", 80f);
        BindBoard();
        if (closeBeforeSaving)
        {
            Check(!window.IsOpen, "closed saved window stays closed after continue");
            Check(os.OpenStateCheck(1f), "open previously closed saved board");
        }
        yield return Wait(() => board.IsGameplayEnabled, label + " restored board playable");
        CheckMask(PartialMask, false, label + " disk continuation retains all switches");
        var state = Capture();
        Check(state.faultGuideSeen && state.faultBoardInteracted
            && !board.IsFaultGuidePlaying && !board.IsAwaitingFirstFaultInteraction,
            label + " disk continuation does not repeat guide");
        saves.EnterTestSession();
    }

    static void BindBoard()
    {
        os = OS_Handle.Instance;
        board = os.StateCheckWindowController;
        window = board.GetComponent<OSWindowUnit>();
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        os.LinkMessageController.NotifyOSHidden();
        var handler = typeof(OS_Handle).GetMethod("HandleStateCheckPuzzleCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(handler != null, "story completion handler resolves for isolation");
        board.PuzzleCompleted -= (Action)Delegate.CreateDelegate(typeof(Action), os, handler);
        board.PuzzleCompleted += OnCompleted;
    }

    static void ReleaseBoard()
    {
        if (board != null) board.PuzzleCompleted -= OnCompleted;
        board = null; window = null; os = null;
    }
    static void OnCompleted() => completionCount++;
    static OSResumeSaveData Capture()
    {
        var saved = new OSResumeSaveData();
        board.CapturePersistentBoard(saved);
        return saved;
    }
    static void CheckMask(int mask, bool completed, string label)
    {
        var saved = Capture();
        Check(saved.hasFaultBoard && saved.faultBoardMask == mask
            && saved.faultBoardCompleted == completed, label);
    }
    static IEnumerator Shot(string name)
    {
        string path = Path.Combine(OutputDirectory, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path), "capture " + name, 10f);
    }
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Check(condition(), label);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        if (!checks.Contains(label)) checks.Add(label);
    }
    static string Fingerprint(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
