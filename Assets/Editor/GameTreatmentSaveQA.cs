using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;

// Explicit Play Mode integration run. It reloads both presentation scenes and
// writes only to a unique project Temp directory; leave Play Mode after the run.
public static class GameTreatmentSaveQA
{
    static readonly List<string> checks = new List<string>();
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameSessionFlow.Instance == null || GameTest.Instance == null) return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning) return "Wait for active save/transition";
        checks.Clear();Status = "running";
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
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        string[] originalFiles = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        saves.EnterTestSession();
        string[] hashes = originalFiles.Select(Fingerprint).ToArray();
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "GameTreatmentSaveQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        saves.UseIsolatedStorageForTests(OutputDirectory);saves.EnterTestSession();
        data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
        Application.runInBackground = true;
        if (resources != null) resources.enabled = false;
        var stack = new Stack<IEnumerator>();stack.Push(Run());
        try
        {
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose();continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error) { Status = "FAILED: " + error;Debug.LogError(Status);yield break; }
                if (next is IEnumerator nested) stack.Push(nested);else yield return next;
            }
            Check(originalFiles.Select(Fingerprint).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " treatment/board checks";
            Debug.Log("[Treatment Save QA] " + Status);
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            saves.EnterTestSession();saves.EndSession();
            data.ReplaceSaveData(original);data.SetCurrentLanguage(language);
            if (resources != null) resources.enabled = resourceEnabled;
            Application.runInBackground = background;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var flow = GameSessionFlow.Instance;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.PrologueHeartHeal), "prepare isolated treatment fixture");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning, "treatment fixture ready", 60f);
        var os = OS_Handle.Instance;
        data.OSCharacterInteractionState.ActivateSequence(null);
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);os.LinkMessageController.NotifyOSHidden();
        Check(os.OpenHeartHealTool(1), "open first treatment");
        var treatment = os.HeartHealToolWindowController;
        var window = treatment.GetComponent<OSWindowUnit>();
        yield return Wait(() => !window.IsOpening && !treatment.IsFirstTreatmentTutorialLocked, "treatment ready");
        int initialEnergy = data.GetOSResourceValue(ResourceType.Battery);
        Check(treatment.BeginTreatment(), "start actual chart");
        int paidShocks = 0;
        float deadline = Time.realtimeSinceStartup + 25f;
        while (treatment.IsTreating && treatment.TreatmentElapsedTime < 15f && Time.realtimeSinceStartup < deadline)
        {
            var beats = treatment.GetComponentsInChildren<OSHeartHealBeatUnit>(true);
            if (paidShocks < 5 && beats.Any(x => x.BeatType == OSHeartHealBeatType.Tap && x.CanBeHit))
            {
                bool hit;
                if (treatment.TryTriggerShock(out hit)) { paidShocks++;Check(hit, "actual tap landed " + paidShocks); }
            }
            yield return null;
        }
        Check(treatment.IsTreating && treatment.TreatmentElapsedTime >= 15f, "chart advanced fifteen real seconds");
        Check(paidShocks > 0 && data.GetOSResourceValue(ResourceType.Battery) == initialEnergy - paidShocks,
            "actual shocks consumed exactly one energy each");
        saves.ResumeIsolatedWrites();saves.BeginSession(data.SaveData);
        Check(flow.ReturnToMainMenu(), "save live treatment and return to menu");
        yield return WaitMenu();
        GameSaveData before = ReadSnapshot();
        var chart = before.osResume.treatment;
        Check(chart.active && chart.elapsed >= 15f && chart.nextBeatIndex > 0 && chart.beats.Any(x => !x.resolved),
            "disk includes live chart and active beats");
        Check(chart.initialBattery == initialEnergy, "retry energy baseline persisted");
        File.WriteAllText(Path.Combine(OutputDirectory, "before-treatment.json"), JsonUtility.ToJson(before, true));
        Check(flow.ContinueGame(), "continue actual treatment disk save");
        float loadDeadline = Time.realtimeSinceStartup + 75f;
        while (flow.IsBusy && Time.realtimeSinceStartup < loadDeadline)
        {
            var pending = OS_Handle.Instance?.HeartHealToolWindowController;
            if (pending != null && pending != treatment)
                Check(!pending.IsTreating, "loading curtain does not run restored chart");
            yield return null;
        }
        Check(!flow.IsBusy, "treatment restore completed before timeout");
        treatment = OS_Handle.Instance.HeartHealToolWindowController;
        yield return Wait(() => treatment.IsTreating, "chart resumes after load curtain", 10f);
        var resumed = new OSHeartTreatmentResumeData();treatment.CapturePersistentTreatment(resumed);
        Check(data.GetOSResourceValue(ResourceType.Battery) == before.osResources.energy, "reload neither refunds nor spends energy");
        Check(resumed.elapsed >= chart.elapsed && resumed.elapsed < chart.elapsed + .5f, "load duration is excluded from treatment clock");
        Check(Mathf.Abs(resumed.state - chart.state) < .01f, "logical treatment state survives reload");
        Check(resumed.resolvedBeatCount == chart.resolvedBeatCount && resumed.nextBeatIndex >= chart.nextBeatIndex,
            "beat judgement counter preserved and chart continues forward");
        Check(resumed.beats.Any(x => !x.resolved) && resumed.initialBattery == chart.initialBattery,
            "active beat instances and retry baseline reconstructed");
        File.WriteAllText(Path.Combine(OutputDirectory, "after-treatment.json"), JsonUtility.ToJson(resumed, true));
        Check(flow.ReturnToMainMenu(), "leave resumed treatment");yield return WaitMenu();

        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.PrologueFault), "prepare isolated fault-board fixture");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning, "fault fixture ready", 60f);
        os = OS_Handle.Instance;
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);os.LinkMessageController.NotifyOSHidden();
        Check(os.OpenStateCheck(1f), "open fault board");
        var board = os.StateCheckWindowController;
        yield return Wait(() => board.IsGameplayEnabled, "fault board interactive");
        Check(board.ToggleNode(1) && board.ToggleNode(6), "toggle actual fault-board nodes");
        var expectedBoard = new OSResumeSaveData();board.CapturePersistentBoard(expectedBoard);
        Check(expectedBoard.hasFaultBoard && !expectedBoard.faultBoardCompleted, "partially repaired board fixture");
        saves.ResumeIsolatedWrites();saves.BeginSession(data.SaveData);
        Check(flow.ReturnToMainMenu(), "save fault board and return to menu");yield return WaitMenu();
        Check(flow.ContinueGame(), "continue fault board save");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning, "fault board reloaded", 75f);
        var actualBoard = new OSResumeSaveData();OS_Handle.Instance.StateCheckWindowController.CapturePersistentBoard(actualBoard);
        Check(actualBoard.hasFaultBoard && actualBoard.faultBoardMask == expectedBoard.faultBoardMask
            && actualBoard.faultBoardCompleted == expectedBoard.faultBoardCompleted, "all sixteen node states restored");
        Check(flow.ReturnToMainMenu(), "finish at main menu");yield return WaitMenu();
    }

    static IEnumerator WaitMenu() => Wait(() => GameManager.Instance.Phase == GamePhase.Boot
        && !GameManager.Instance.IsTransitioning && !GameSessionFlow.Instance.IsBusy, "menu ready", 60f);
    static IEnumerator Wait(Func<bool> condition, string label, float duration = 20f)
    {
        float until = Time.realtimeSinceStartup + duration;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Check(condition(), label);
    }
    static GameSaveData ReadSnapshot()
    {
        var read = new GameAutosaveStorage(OutputDirectory).Read();
        if (!read.Success) throw new IOException(read.Error);
        return JsonUtility.FromJson<GameSaveData>(read.Payload);
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        if (!checks.Contains(label)) checks.Add(label);
    }
    static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
