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
using UnityEngine.EventSystems;

// Opt-in Play Mode integration run. Uses a disposable save and isolated Temp
// storage. Inspect Status and OutputDirectory/results.txt; leave Play Mode after.
public static class FirstTreatmentTutorialQA
{
    const string Sequence = OSCharacterInteractionIds.PrologueEvent01HeartHeal;
    const string Recall = OSCharacterInteractionIds.Event01Entries.HeartHealRecallChoice;
    const string ShockFirst = OSCharacterInteractionIds.Event01Entries.HeartHealShockFirst;
    const string StartAfterShock = OSCharacterInteractionIds.Event01Entries.HeartHealStartAfterShock;
    const string StartFirst = OSCharacterInteractionIds.Event01Entries.HeartHealStartFirst;
    const string ShockAfterStart = OSCharacterInteractionIds.Event01Entries.HeartHealShockAfterStart;
    static readonly string[] DesktopKeys = { "state_check", "character", "file" };
    static readonly List<string> checks = new List<string>();
    static OS_Handle os;
    static OSLinkMessageController link;
    static OSHeartHealToolWindowController treatment;
    static OSWindowUnit treatmentWindow;

    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null
            || GameTest.Instance == null || SceneLoadManager.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || GameSessionFlow.Instance.IsBusy || SceneLoadManager.Instance.IsBusy
            || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        checks.Clear(); os = null; link = null; treatment = null; treatmentWindow = null;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "FirstTreatmentTutorialQA", Guid.NewGuid().ToString("N")));
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
                    Debug.LogError("[First Treatment Tutorial QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "production save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "autosave writes remain disabled");
            Check(string.Equals(Path.GetDirectoryName(saves.SavePath), OutputDirectory, StringComparison.OrdinalIgnoreCase),
                "save service remains bound to isolated storage");
            Status = "PASS: " + checks.Count + " first-treatment tutorial checks";
            Debug.Log("[First Treatment Tutorial QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            treatment?.StopTreatment();
            link?.NotifyOSHidden();
            GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
            saves.EnterTestSession(); saves.EndSession();
            GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
            if (resources != null) resources.enabled = resourcesEnabled;
            Application.runInBackground = background;
            if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes))
                Status = "FAILED: production save files changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted before completion";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.PrologueHeartHeal), "isolated prologue checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning && !SceneLoadManager.Instance.IsBusy,
            "prologue checkpoint ready", 65f);
        Check(GameTest.Instance.JumpSucceeded, "prologue checkpoint succeeded");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        os = OS_Handle.Instance;
        link = os.LinkMessageController;
        treatment = os.HeartHealToolWindowController;
        treatmentWindow = treatment != null ? treatment.GetComponent<OSWindowUnit>() : null;
        Check(os.IsOSVisible && link != null && treatmentWindow != null, "real OS tutorial controllers available");

        PrepareFixture("shock route");
        yield return OpenTreatment("shock route");
        yield return ExerciseWindowRecovery();
        ExerciseActualDesktopClick();
        yield return FinishFrom(Recall, true, "shock route");
        CheckDesktopHintsResume("shock route");

        PrepareFixture("start route");
        yield return OpenTreatment("start route");
        yield return FinishFrom(Recall, false, "start route");
        CheckDesktopHintsResume("start route");

        yield return ExercisePendingTutorial();
        foreach (string interrupted in new[] { Recall, ShockFirst, StartAfterShock, StartFirst, ShockAfterStart })
            yield return ExerciseLegacyInterruption(interrupted);
    }

    static OSCharacterInteractionSaveData Save => GameDataManager.Instance.OSCharacterInteractionState;

    static void PrepareFixture(string label)
    {
        treatment.StopTreatment();
        treatmentWindow.CloseSilently();
        link.NotifyOSHidden();
        link.SetGlobalMessagesSuppressed(false);
        Save.ActivateSequence(null);
        Save.ActivateSequence(Sequence);
        CompleteFixtureEntry(OSCharacterInteractionIds.Event01Entries.OpeningFindToolChoice);
        CompleteFixtureEntry(OSCharacterInteractionIds.Event01Entries.OpeningRememberedFolder);
        os.ConfigureHeartHealTreatment(1);
        Check(link.ActivateSequence(Sequence), label + ": activate tutorial sequence");
        os.OpenCharacterWindowForReconnect(0f);
        link.NotifyOSBecameVisible();
        Check(!Save.HasConsumedEntry(Recall) && !Save.HasConsumedEntry(OSCharacterInteractionIds.Event01Entries.HoverStateCheckFirst),
            label + ": tutorial and interfering hints start unconsumed");
    }

    static void CompleteFixtureEntry(string entry)
    {
        Save.BeginPresentation(entry);
        Save.CompletePresentation(entry);
    }

    static IEnumerator OpenTreatment(string label, string expected = Recall)
    {
        Check(os.OpenHeartHealTool(1), label + ": open real treatment window");
        yield return Wait(() => link.IsPresenting && link.DisplayedEntryId == expected,
            label + ": expected tutorial is presented");
        Check(treatment.IsFirstTreatmentTutorialLocked, label + ": treatment waits for tutorial confirmation");
        Check(!treatment.BeginTreatment(), label + ": chart cannot start before confirmation");
    }

    static IEnumerator FinishFrom(string entry, bool shockFirst, string label)
    {
        if (entry == Recall)
        {
            yield return Choose(Recall, shockFirst ? "shock" : "start", label);
            entry = shockFirst ? ShockFirst : StartFirst;
        }
        if (entry == ShockFirst || entry == StartFirst)
        {
            bool selectedShockFirst = entry == ShockFirst;
            yield return Choose(entry, selectedShockFirst ? "start" : "shock", label);
            entry = selectedShockFirst ? StartAfterShock : ShockAfterStart;
        }
        Check(entry == StartAfterShock || entry == ShockAfterStart, label + ": final tutorial node reached");
        yield return Choose(entry, "start_treatment", label);
        yield return Wait(() => !treatment.IsFirstTreatmentTutorialLocked && !treatmentWindow.IsOpening,
            label + ": tutorial unlocks actual treatment");
        Check(Save.HasCompletedEntry(entry), label + ": final confirmation is completed in save");
        Check(treatment.BeginTreatment() && treatment.IsTreating, label + ": actual chart can start after tutorial");
        Check(treatment.StopTreatment(), label + ": stop disposable chart");
    }

    static IEnumerator Choose(string entry, string option, string label)
    {
        string step = label + "/" + entry;
        yield return Wait(() => link.IsPresenting && link.DisplayedEntryId == entry, step + ": active entry ready");
        ProbeDesktopHints(entry, step + ": before text completion");
        yield return Wait(() => link.IsWaitingForOption && link.DisplayedEntryId == entry, step + ": choice is interactive");
        ProbeDesktopHints(entry, step + ": waiting for option");
        Check(link.TryGetActiveOptionTarget(option, out var target) && target.gameObject.activeInHierarchy,
            step + ": expected option remains visible");
        Check(treatment.IsFirstTreatmentTutorialLocked, step + ": treatment remains locked before selection");
        Check(link.TrySelectDisplayedOptionImmediately(Sequence, entry, option), step + ": real option selected");
        Check(Save.HasCompletedEntry(entry), step + ": selection completes entry");
    }

    static void ProbeDesktopHints(string expected, string label)
    {
        foreach (string key in DesktopKeys)
        {
            link.NotifyDesktopProcessPointerEnter(key);
            AssertCurrent(expected, label + ": hover " + key);
            link.NotifyDesktopProcessClicked(key);
            AssertCurrent(expected, label + ": click " + key);
        }
    }

    static void AssertCurrent(string expected, string label)
    {
        Check(link.IsPresenting && link.DisplayedSequenceId == Sequence && link.DisplayedEntryId == expected
            && Save.presentingEntryId == expected && !Save.HasCompletedEntry(expected), label + " preserves unanswered tutorial");
    }

    static IEnumerator ExerciseWindowRecovery()
    {
        yield return Wait(() => !treatmentWindow.IsOpening, "window recovery: initial open finished");
        treatmentWindow.Close();
        Check(!treatmentWindow.IsOpen, "window recovery: treatment closes normally");
        ProbeDesktopHints(Recall, "window recovery: treatment closed");
        yield return OpenTreatment("window recovery: reopen");
        yield return Wait(() => !treatmentWindow.IsOpening, "window recovery: reopen finished");
        treatmentWindow.Minimize();
        Check(treatmentWindow.IsMinimized, "window recovery: treatment minimizes normally");
        ProbeDesktopHints(Recall, "window recovery: treatment minimized");
        Check(os.OpenHeartHealTool(1), "window recovery: launcher restores treatment");
        yield return Wait(() => treatmentWindow.IsOpen && !treatmentWindow.IsMinimized
            && treatment.IsFirstTreatmentTutorialLocked, "window recovery: restored treatment still waits for tutorial");
        AssertCurrent(Recall, "window recovery: after restore");
    }

    static void ExerciseActualDesktopClick()
    {
        var desktop = Field<DesktopProcessUnit>(os, "fileDesktopProcess");
        var folder = Field<OSWindowUnit>(os, "folderWindow");
        Check(EventSystem.current != null && desktop != null && desktop.IsInteractable && folder != null,
            "actual desktop: folder launcher is interactable");
        folder.CloseSilently();
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        desktop.OnPointerEnter(pointer);
        AssertCurrent(Recall, "actual desktop: folder pointer enter");
        desktop.OnPointerClick(pointer);
        Check(folder.IsOpen, "actual desktop: folder click still opens its real window");
        AssertCurrent(Recall, "actual desktop: folder pointer click");
        desktop.OnPointerExit(pointer);
    }

    static IEnumerator ExercisePendingTutorial()
    {
        const string label = "pending tutorial";
        PrepareFixture(label);
        Check(link.TryPresentEntry(OSCharacterInteractionIds.Event01Entries.HoverHeartHealTool),
            label + ": begin genuine preceding desktop text");
        // Queue the real WindowOpened trigger before the typewriter completes.
        link.NotifyWindowOpened(treatmentWindow.WindowId);
        Check(Save.pendingEntryId == Recall, label + ": recall waits behind current text");
        foreach (string key in DesktopKeys)
        {
            link.NotifyDesktopProcessPointerEnter(key);
            Check(Save.pendingEntryId == Recall, label + ": hover " + key + " retains pending recall");
            link.NotifyDesktopProcessClicked(key);
            Check(Save.pendingEntryId == Recall, label + ": click " + key + " retains pending recall");
        }
        Check(os.OpenHeartHealTool(1), label + ": open actual window with recall pending");
        Check(Save.pendingEntryId == Recall || Save.presentingEntryId == Recall,
            label + ": actual window opening retains tutorial");
        yield return FinishFrom(Recall, true, label);
        CheckDesktopHintsResume(label);
    }

    static IEnumerator ExerciseLegacyInterruption(string interrupted)
    {
        string label = "legacy recovery/" + interrupted;
        PrepareFixture(label);
        if (interrupted != Recall) CompleteFixtureEntry(Recall);
        if (interrupted == StartAfterShock) CompleteFixtureEntry(ShockFirst);
        if (interrupted == ShockAfterStart) CompleteFixtureEntry(StartFirst);
        // Reproduce the old cancellation: consumed but not completed, with no
        // current or pending entry for the normal presentation resume to find.
        Save.BeginPresentation(interrupted);
        Save.CancelPresentation();
        Check(Save.HasConsumedEntry(interrupted) && !Save.HasCompletedEntry(interrupted)
            && string.IsNullOrEmpty(Save.presentingEntryId) && string.IsNullOrEmpty(Save.pendingEntryId),
            label + ": reproduce lost consumed choice");
        File.WriteAllText(Path.Combine(OutputDirectory, interrupted + "-interrupted.json"), JsonUtility.ToJson(Save, true));
        // Do not call the repair API: opening the real window must invoke it.
        yield return OpenTreatment(label, interrupted);
        yield return FinishFrom(interrupted, interrupted != StartFirst && interrupted != ShockAfterStart, label);
        CheckDesktopHintsResume(label);
    }

    static void CheckDesktopHintsResume(string label)
    {
        Check(link.NotifyDesktopProcessPointerEnter("state_check"), label + ": desktop hints resume after tutorial");
        Check(link.DisplayedEntryId == OSCharacterInteractionIds.Event01Entries.HoverStateCheckFirst,
            label + ": normal desktop response displays after unlock");
    }

    static T Field<T>(object owner, string name) where T : class
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(owner.GetType().Name, name);
        return field.GetValue(owner) as T;
    }

    static IEnumerator Wait(Func<bool> condition, string label, float timeout = 20f)
    {
        float until = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Check(condition(), label);
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
