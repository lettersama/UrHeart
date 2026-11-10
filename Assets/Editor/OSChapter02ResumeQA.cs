using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MrTGame.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Opt-in regression for the player's chapter-two save. All writes use a copy.
public static class OSChapter02ResumeQA
{
    static readonly List<string> checks = new List<string>();
    static EditorWindow gameView;
    static string context;
    static OS_Handle os;
    static OSChapter02FinaleDirector finale;
    static OSChapter02HiddenMemoryPresentation hidden;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        if (!Application.isPlaying || GameAutosaveService.Instance == null
            || GameSessionFlow.Instance == null || GameManager.Instance.IsTransitioning)
            return "Initialized Play Mode required";
        gameView = Resources.FindObjectsOfTypeAll<EditorWindow>()
            .FirstOrDefault(w => w.GetType().FullName == "UnityEditor.GameView");
        if (gameView == null) return "Game view required";
        checks.Clear();
        OutputDirectory = Path.GetFullPath("Temp/Chapter02ResumeQA/run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(OutputDirectory);
        Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var original = GameDataManager.Instance.SaveData;
        var paths = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        var hashes = paths.Select(Hash).ToArray();
        bool background = Application.runInBackground;
        var oldFocus = EditorWindow.focusedWindow;
        var stack = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();
            File.Copy(paths[0], Path.Combine(OutputDirectory, "autosave.json"));
            saves.UseIsolatedStorageForTests(OutputDirectory);
            saves.EnterTestSession();
            Application.runInBackground = true;
            stack.Push(Run());
            while (stack.Count > 0)
            {
                gameView.Focus();
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED at " + context + ": " + error;
                    ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "failure.png"));
                    yield break;
                }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "player save files unchanged");
            Status = "PASS: " + checks.Count + " checks";
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            saves.EnterTestSession(); saves.EndSession();
            GameManager.Instance.OpenMainMenuWithoutSave();
            GameDataManager.Instance.ReplaceSaveData(original);
            Application.runInBackground = background;
            if (oldFocus != null) oldFocus.Focus();
            if (!paths.Select(Hash).SequenceEqual(hashes)) Status = "FAILED: player save files changed";
            if (Status == "running") Status = "Interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        var saves = GameAutosaveService.Instance;
        var flow = GameSessionFlow.Instance;
        var fixture = ReadCopy();
        Check(fixture.osProcess.memory.chapter02FinaleStep == 2, "input save is inside mother's old memory");
        context = "load original save";
        yield return Menu();
        yield return Load();
        CheckLayouts(fixture);
        Check(finale.Step == OSChapter02FinaleDirector.FinaleStep.HiddenText
            && hidden.CurrentBeatIndex == fixture.osProcess.memory.chapter02FinaleHiddenLine,
            "Continue restores original hidden-memory checkpoint");
        yield return Wait(() => hidden.DebugPhase == "Text", "old memory renders", 15f);
        yield return Hold(1f);
        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "original_save_loaded.png"));

        context = "save and reload mid-memory";
        float deadline = Time.realtimeSinceStartup + 20f;
        while (hidden.CurrentBeatIndex < 3 && Time.realtimeSinceStartup < deadline)
        {
            hidden.HandleContinue();
            yield return Hold(.3f);
        }
        Check(hidden.CurrentBeatIndex == 3, "old memory progresses through normal continue handler");
        saves.ResumeIsolatedWrites();
        Check(saves.FlushNow(), "mid-memory snapshot written to isolated storage");
        var saved = ReadCopy();
        int beat = saved.osProcess.memory.chapter02FinaleHiddenLine;
        Check(beat == 3 && saved.osProcess.memory.chapter02FinaleStep == 2, "disk retains hidden-memory line and phase");
        saves.EnterTestSession();
        yield return Menu();
        yield return Load();
        yield return Wait(() => hidden.DebugPhase == "Text", "resumed old memory renders", 15f);
        Check(hidden.CurrentBeatIndex == beat, "scene reconstruction resumes the saved line without restarting");
        CheckLayouts(saved);

        context = "last memory line continue indicator";
        yield return Menu();
        fixture.osProcess.memory.chapter02FinaleStep = 0;
        fixture.osProcess.memory.chapter02FinaleHiddenLine = 0;
        var storage = new GameAutosaveStorage(OutputDirectory);
        Check(storage.Write(JsonUtility.ToJson(fixture)).Success, "pre-finale fixture written only to isolated storage");
        saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
        yield return Load();
        Check(os.OpenMemoryWindow(), "memory window opens at final line");
        var memory = os.MemoryWindowController;
        yield return Wait(() => memory.CurrentData?.DataIndex == 7460 && memory.IsCurrentTextComplete
            && !memory.GetComponent<OSWindowUnit>().IsOpening, "last line finishes typing", 15f);
        var tip = memory.transform.Find("TextArea/ContinueTip").GetComponent<Image>();
        yield return Wait(() => memory.CanContinue && tip.color.a > .9f, "continue arrow becomes visible", 5f);
        yield return Hold(1.4f);
        Check(finale.Step == OSChapter02FinaleDirector.FinaleStep.AwaitingBoundary && !hidden.IsPresenting,
            "final line waits for deliberate input");
        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "last_line_continue_arrow.png"));
        yield return null;
        os.NotifyWindowPointerPressed(memory.GetComponent<OSWindowUnit>());
        Check(memory.TryAdvance(), "ordinary memory advance accepts the finale transition");
        Check(!memory.CanContinue && !memory.TryAdvance(), "repeated click cannot enqueue a second transition");
        yield return Wait(() => hidden.DebugPhase == "Text", "click enters mother's old memory", 15f);
        Check(hidden.CurrentBeatIndex == 0 && finale.Step == OSChapter02FinaleDirector.FinaleStep.HiddenText,
            "entry starts at the first line without skipping it");
        Check(tip.color.a < .01f, "ordinary memory arrow hides during the old memory");
    }

    static IEnumerator Menu()
    {
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "return to menu accepted");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot
            && !GameManager.Instance.IsTransitioning && !GameSessionFlow.Instance.IsBusy,
            "menu ready", 30f);
    }

    static IEnumerator Load()
    {
        Check(GameSessionFlow.Instance.ContinueGame(), "Continue accepted");
        yield return Wait(() => !GameSessionFlow.Instance.IsBusy && !GameManager.Instance.IsTransitioning
            && GameManager.Instance.Phase == GamePhase.OSMain && OS_Handle.Instance != null,
            "session reconstructed", 45f);
        os = OS_Handle.Instance;
        finale = os.GetComponent<OSChapter02FinaleDirector>();
        hidden = os.GetComponent<OSChapter02HiddenMemoryPresentation>();
    }

    static void CheckLayouts(GameSaveData snapshot)
    {
        var grids = os.GetComponentsInChildren<OSDesktopProcessGrid>(true);
        var units = os.GetComponentsInChildren<DesktopProcessUnit>(true);
        foreach (var saved in snapshot.osProcess.desktopProcessLayouts)
        {
            var grid = grids.FirstOrDefault(g => g.ContainerId == saved.containerId);
            var unit = units.FirstOrDefault(u => u.ProcessKey == saved.processKey);
            Check(grid != null && unit != null && unit.transform.parent == grid.transform
                && grid.TryGetSlot(unit, out int slot) && slot == saved.gridIndex,
                "saved icon layout preserved: " + saved.processKey);
        }
    }

    static GameSaveData ReadCopy() => JsonUtility.FromJson<GameSaveData>(new GameAutosaveStorage(OutputDirectory).Read().Payload);
    static IEnumerator Hold(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
    static IEnumerator Wait(Func<bool> condition, string label, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > until) throw new TimeoutException(label);
            yield return null;
        }
        Check(true, label);
    }
    static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException(label);
        checks.Add(label);
    }
    static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
