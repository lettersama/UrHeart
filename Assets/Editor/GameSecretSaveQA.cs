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
public static class GameSecretSaveQA
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
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "GameSecretSaveQA", Guid.NewGuid().ToString("N")));
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
            Status = "PASS: " + checks.Count + " secret resume checks";
            Debug.Log("[Secret Save QA] " + Status);
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
        data.SaveData.osResume = new OSResumeSaveData();
        data.SaveData.unlockedRecordDataIndices.Remove(1);
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.PrologueHeartHeal), "prepare isolated first-treatment fixture");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning, "secret fixture ready", 60f);
        var os = OS_Handle.Instance;
        var link = os.LinkMessageController;
        data.OSCharacterInteractionState.ActivateSequence(null);
        link.SetGlobalMessagesSuppressed(true);link.NotifyOSHidden();
        Check(os.OpenHeartHealTool(1), "open first treatment confirmation entry");
        var treatment = os.HeartHealToolWindowController;
        var window = treatment.GetComponent<OSWindowUnit>();
        yield return Wait(() => !window.IsOpening, "treatment window ready");
        int confirmations = 0;
        treatment.TreatmentCompletionConfirmed += _ => confirmations++;
        // Exercise the real confirmation transaction without repeating the separate chart QA.
        treatment.ConfirmTreatment();
        link.NotifyOSBecameVisible();
        Check(confirmations == 1 && os.IsFirstHeartHealEndingActive && data.CurrentHeartHealProcessIndex == 0,
            "first-treatment confirmation settles once and enters secret");
        yield return AdvanceHumanChoicesUntil(() => CursorHandle.Instance?.IsScriptControlledBy(os) == true,
            "secret reached actual cursor takeover", 60f);
        Check(os.IsSystemInputLocked, "secret intentionally locks normal desktop input");
        int energy = data.GetOSResourceValue(ResourceType.Battery);
        int heart = data.GetOSResourceValue(ResourceType.Heart);
        int fault = data.GetOSResourceValue(ResourceType.Fault);
        Check(data.TryGetEventState("Event_02", out bool active, out bool completed) && active && !completed,
            "first-treatment continuation event active before save");
        saves.ResumeIsolatedWrites();saves.BeginSession(data.SaveData);
        Check(flow.ReturnToMainMenu(), "save during scripted mouse takeover and return to menu");
        yield return WaitMenu();
        Check(CursorHandle.Instance?.IsScriptControlled != true, "menu releases the scripted mouse owner");
        Check(!os.IsFirstHeartHealEndingActive, "old hidden secret presentation is stopped");
        var before = ReadSnapshot();
        Check(before.osResume.secretInProgress && !before.osResume.treatment.active
            && !before.osResume.giftStarted && !before.osResume.giftClosed,
            "disk retains stable secret entrance without an active treatment or gift");
        File.WriteAllText(Path.Combine(OutputDirectory, "before-secret.json"), JsonUtility.ToJson(before, true));
        Check(flow.ContinueGame(), "continue one secret autosave roundtrip");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning, "secret scene reloaded", 75f);
        var restored = OS_Handle.Instance;
        Check(restored != null && restored != os && restored.IsFirstHeartHealEndingActive,
            "new scene rebuilds the secret entrance");
        restored.HeartHealToolWindowController.TreatmentCompletionConfirmed += _ => confirmations++;
        Check(CursorHandle.Instance?.IsScriptControlled != true, "old mouse takeover was not carried into entrance");
        Check(data.GetOSResourceValue(ResourceType.Battery) == energy
            && data.GetOSResourceValue(ResourceType.Heart) == heart
            && data.GetOSResourceValue(ResourceType.Fault) == fault,
            "secret reconstruction does not repay or charge resources");
        Check(data.CurrentHeartHealProcessIndex == 0 && !restored.HeartHealToolWindowController.IsTreating,
            "first treatment stays settled on reconstruction");
        yield return Wait(() => restored.LinkMessageController.DisplayedEntryId == OSCharacterInteractionIds.Event01EndingEntries.Fear,
            "secret restarts with its first playable message", 30f);
        Check(!data.OSCharacterInteractionState.HasCompletedEntry(OSCharacterInteractionIds.Event01EndingEntries.KeyStuck),
            "transient secret message history restarted consistently");
        yield return AdvanceHumanChoicesUntil(() => restored.LinkMessageController.DisplayedEntryId == OSCharacterInteractionIds.Event01EndingEntries.Return,
            "replayed secret reaches its normal return option", 100f);
        Check(CursorHandle.Instance?.IsScriptControlled != true, "normal relock animation returned mouse control");
        Check(restored.LinkMessageController.TrySelectDisplayedOptionImmediately(
            OSCharacterInteractionIds.PrologueEvent01HeartHealEnding,
            OSCharacterInteractionIds.Event01EndingEntries.Return, "close_os"), "choose normal secret exit");
        yield return Wait(() => !GameManager.Instance.IsTransitioning && GameManager.Instance.Phase == GamePhase.Prologue,
            "secret returns to side-scroll", 30f);
        Check(!restored.IsFirstHeartHealEndingActive && !restored.IsSystemInputLocked
            && CursorHandle.Instance?.IsScriptControlled != true, "secret exit clears mouse and desktop input locks");
        Check(confirmations == 1 && data.CurrentHeartHealProcessIndex == 0,
            "replay never invokes treatment confirmation again");
        OSSessionPersistence.Capture(data.SaveData);
        var after = data.SaveData.osResume;
        Check(!after.secretInProgress && !after.giftStarted && !after.giftLanded && !after.giftClosed
            && !data.IsRecordDataUnlocked(1), "secret leaves no premature gift or chapter reward");
        File.WriteAllText(Path.Combine(OutputDirectory, "after-secret.json"), JsonUtility.ToJson(after, true));
        Check(flow.ReturnToMainMenu(), "finish at main menu");yield return WaitMenu();
    }

    static IEnumerator AdvanceHumanChoicesUntil(Func<bool> finished, string label, float duration)
    {
        float deadline = Time.realtimeSinceStartup + duration;
        while (!finished() && Time.realtimeSinceStartup < deadline)
        {
            var link = OS_Handle.Instance?.LinkMessageController;
            string entry = link != null ? link.DisplayedEntryId : null;
            string option = entry == OSCharacterInteractionIds.Event01EndingEntries.Fear
                || entry == OSCharacterInteractionIds.Event01EndingEntries.Searching ? "continue"
                : entry == OSCharacterInteractionIds.Event01EndingEntries.KeyStuck ? "click_me" : null;
            if (option != null)
                link.TrySelectDisplayedOptionImmediately(OSCharacterInteractionIds.PrologueEvent01HeartHealEnding, entry, option);
            yield return null;
        }
        Check(finished(), label);
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
