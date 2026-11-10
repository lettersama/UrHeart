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

// Focused Play Mode check. EnterTestSession blocks normal save writes throughout.
public static class Chapter01TaskbarReportQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameTest.Instance == null || SceneLoadManager.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        checks.Clear();Status = "running";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "Chapter01TaskbarReportQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        GameDataManager.Instance.StartCoroutine(Guarded());return Status;
    }

    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;var saves = GameAutosaveService.Instance;
        var original = data.SaveData;var language = data.CurrentLanguageState;
        bool background = Application.runInBackground;
        var resources = GameResourceSystem.Instance;bool enabled = resources != null && resources.enabled;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();hashes = paths.Select(Fingerprint).ToArray();
            data.ReplaceSaveData(Clone(original));Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose();continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { Status = "FAILED: " + error;Debug.LogError("[Chapter 1 Taskbar QA] " + Status);yield break; }
                if (next is IEnumerator nested) work.Push(nested);else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "normal save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "test session blocks autosave writes");
            Status = "PASS: " + checks.Count + " taskbar/story checks";
            Debug.Log("[Chapter 1 Taskbar QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            saves.EnterTestSession();saves.EndSession();GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original);data.SetCurrentLanguage(language);
            if (resources != null) resources.enabled = enabled;
            Application.runInBackground = background;
            if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes)) Status = "FAILED: normal save files changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture), "isolated chapter-one checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning, "checkpoint finished", 65);
        Check(GameTest.Instance.JumpSucceeded, "checkpoint succeeded");
        var os = OS_Handle.Instance;var data = GameDataManager.Instance;
        Check(os != null && os.IsOSVisible, "OS is ready");
        var link = Field<OSLinkMessageController>(os, "linkMessageController");
        link.NotifyOSHidden();link.SetGlobalMessagesSuppressed(true);
        data.OSCharacterInteractionState.ActivateSequence("qa.taskbar_report");
        os.MemoryWindowController.GetComponent<OSWindowUnit>().CloseSilently();
        ValidateStory(data);

        var keyWindow = Field<OSWindowUnit>(os, "chapter01KeyWindow");
        var keyIcon = Field<DesktopProcessUnit>(os, "chapter01KeyDesktopProcess");
        Check(keyWindow != null && keyIcon != null, "key window and draggable key exist");
        data.SetChapter01KeyXAcquired(false);
        Invoke(os, "InitializeChapter01KeyRuntime");Invoke(os, "OpenChapter01KeyWindow");
        yield return Wait(() => keyWindow.IsOpen && !keyWindow.IsOpening, "uncollected key container opened");
        Check(keyWindow.LinkedProcessUnit == null, "new key container is standalone");
        var keySnapshot = Capture(data);
        Check(keySnapshot.osResume.windows.Any(w => w.windowId == keyWindow.WindowId), "uncollected key included in session snapshot");
        yield return Reload(keySnapshot);
        Check(keyWindow.IsOpen && !keyWindow.IsMinimized && keyWindow.LinkedProcessUnit == null,
            "uncollected key restores visibly without allocating a taskbar process");
        Check(keyIcon.gameObject.activeInHierarchy && keyIcon.transform.IsChildOf(keyWindow.transform), "restored key remains inside its visible container");
        Check(os.ProcessUnits.All(p => p.LinkedWindow != keyWindow), "no taskbar item targets key container");
        CheckTaskbar(os);

        var acquiredSnapshot = Clone(keySnapshot);
        acquiredSnapshot.osProcess.chapter01KeyXAcquired = true;
        yield return Reload(acquiredSnapshot);
        Check(!keyWindow.IsOpen && !keyWindow.gameObject.activeSelf && keyWindow.LinkedProcessUnit == null,
            "legacy saved key window is skipped after key acquisition");
        Check(os.ProcessUnits.All(p => p.LinkedWindow != keyWindow), "acquired-key reload leaves no ghost key process");

        Check(os.OpenToolWindow(), "real Toolbox application opens");
        var tool = Field<OSWindowUnit>(os, "toolWindow");
        yield return Wait(() => tool.IsOpen && !tool.IsOpening, "Toolbox finished opening");
        tool.Minimize();Check(tool.IsMinimized, "Toolbox fixture minimized");
        var minimizedSnapshot = Capture(data);
        Check(minimizedSnapshot.osResume.windows.Any(w => w.windowId == tool.WindowId && w.minimized), "minimized app captured");
        yield return Reload(minimizedSnapshot);
        var process = tool.LinkedProcessUnit;
        Check(tool.IsOpen && tool.IsMinimized && process != null && process.LinkedWindow == tool,
            "minimized real app keeps its bidirectional taskbar binding after reload");
        Check(process.gameObject.activeInHierarchy && process.ProcessIcon != null && process.ProcessIcon.enabled
            && process.ProcessIcon.sprite != null, "restored app has a visible taskbar icon");
        yield return Wait(() => !os.IsSystemInputLocked, "taskbar input available");
        process.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
        yield return Wait(() => tool.IsOpen && !tool.IsMinimized && !tool.IsOpening, "actual taskbar click restores minimized app");
        tool.PrepareRuntimeClosed();
        Check(!tool.IsOpen && tool.LinkedProcessUnit == null && process.IsAvailable && process.LinkedWindow == null
            && !process.gameObject.activeSelf, "PrepareRuntimeClosed releases the linked taskbar slot");
        CheckTaskbar(os);
    }

    static void ValidateStory(GameDataManager data)
    {
        var memory = data.OSMemoryData;
        Check(memory.TryGetData(1650, out var voice) && !voice.SourceText.StartsWith("<color=red>")
            && voice.SourceText.Contains("<color=red>\"") && voice.SourceText.EndsWith("\"</color>"), "1650 colors only the quoted inner voice");
        Check(memory.TryGetData(1920, out var robot) && robot.NextDataIndex == 1925
            && !robot.SourceText.Contains("不是"), "1920 contains only robot speech and leads to 1925");
        Check(memory.TryGetData(1925, out var mother) && mother.NextDataIndex == 1930
            && mother.SourceText.StartsWith("不是") && mother.Options.Count == 0 && mother.Links.Count == 0
            && mother.OnEnterActions.Count == 0 && memory.TryGetData(1930, out _), "1925 is clean mother narration leading to existing 1930");
        foreach (int index in new[] { 1650, 1920, 1925 })
        {
            string key = "os.memory." + index + ".text";
            var entry = data.LocalizationData.Entries.SingleOrDefault(e => e.Key == key);
            Check(entry != null && entry.Values.Count == 10 && entry.Values.Select(v => v.Language).Distinct().Count() == 10,
                key + " has ten explicit translations");
            foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
            {
                string text = entry.Values.Single(v => v.Language == language).Text;
                Check(!string.IsNullOrWhiteSpace(text) && (index != 1650 || (!text.StartsWith("<color=red>")
                    && text.Contains("<color=red>") && text.EndsWith("</color>"))), key + " / " + language + " valid");
            }
        }
    }

    static GameSaveData Capture(GameDataManager data) { OSSessionPersistence.Capture(data.SaveData);return Clone(data.SaveData); }
    static IEnumerator Reload(GameSaveData snapshot)
    {
        var data = GameDataManager.Instance;data.ReplaceSaveData(Clone(snapshot));
        yield return OSSessionPersistence.Restore(data.SaveData);yield return null;
    }
    static void CheckTaskbar(OS_Handle os) => Check(os.ProcessUnits.All(p => !p.gameObject.activeSelf
        || (p.LinkedWindow != null && p.LinkedWindow.LinkedProcessUnit == p && p.ProcessIcon != null
            && p.ProcessIcon.enabled && p.ProcessIcon.sprite != null)), "every active taskbar slot has a valid window and visible icon");
    static IEnumerator Wait(Func<bool> ready, string label, float seconds = 6)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready() && Time.realtimeSinceStartup < end) yield return null;
        Check(ready(), label);
    }
    static GameSaveData Clone(GameSaveData data) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));
    static T Field<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
    static void Invoke(object owner, string method) => owner.GetType().GetMethod(method, Private).Invoke(owner, null);
    static void Check(bool success, string label) { if (!success) throw new InvalidOperationException(label);checks.Add("PASS " + label); }
    static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
