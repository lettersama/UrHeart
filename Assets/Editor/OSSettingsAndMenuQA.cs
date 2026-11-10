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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in Play Mode regression checks; fixture writes are isolated under Temp.
public static class OSSettingsAndMenuQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static OS_Handle os;
    static OSWindowUnit settings;
    static RectTransform desktop;
    static Vector2 originalDesktopSize;
    static Canvas liftedCanvas;
    static bool ownsCanvas, canvasOverride, canvasEnabled;
    static int canvasOrder, canvasLayer;
    static GameSetup setup;
    static Mouse testMouse;
    static string context;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null
            || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSSettingsAndMenuQA", Guid.NewGuid().ToString("N")));
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
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        var previousMouse = Mouse.current;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = paths.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            Application.runInBackground = true; Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("SettingsMenuQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            testMouse.MakeCurrent(); work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { Status = "FAILED at " + context + "\n" + error; Debug.LogError("[Settings/Menu QA] " + Status); yield break; }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "formal save file hashes unchanged");
            Status = "PASS";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (GameTest.Instance != null && GameTest.Instance.IsCheckpointJumpRunning) GameTest.Instance.StopAllCoroutines();
                RestoreDesktop(); RestoreCanvas();
                os?.LinkMessageController?.SetGlobalMessagesSuppressed(true);
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                Check(ReferenceEquals(data.SaveData, original) && data.CurrentLanguageState == language && GameSetup.IsDemo == demo
                    && hashes != null && paths.Select(Hash).SequenceEqual(hashes), "cleanup restores original save object, language, mode and formal files");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                status = Status, context, checks, originalFiles = paths, originalHashes = hashes,
                finalHashes = paths.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Exit Play Mode after the isolated run; no formal save writes."
            }, Newtonsoft.Json.Formatting.Indented));
            Debug.Log("[Settings/Menu QA] " + Status + " | " + OutputDirectory);
            os = null; settings = null; desktop = null; liftedCanvas = null; setup = null; testMouse = null;
        }
    }

    static IEnumerator Run()
    {
        context = "Chapter 1 fixture";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeToolUnlock), "Chapter 1 fixture accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "fixture ready", 80f);
        os = OS_Handle.Instance;
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession && os.IsOSVisible, "fixture isolated and visible");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        settings = os.transform.Find("Windows/WindowUnit_Option").GetComponent<OSWindowUnit>();
        desktop = settings.WindowRect.parent as RectTransform; originalDesktopSize = desktop.sizeDelta;
        var launcher = os.GetComponentInChildren<OSOptionLauncherButton>(true);
        Check(settings.GetComponent<OSOptionWindowController>() != null && launcher != null, "authored settings and power launcher found");

        context = "settings position recovery";
        settings.CloseSilently(); settings.WindowRect.anchoredPosition = new Vector2(1000f, 1000f);
        Check(launcher.ToggleOptionWindow(), "power launcher opens settings");
        yield return Wait(() => Ready(settings), "settings opening completes"); yield return null;
        CheckAccessible("corrupt opening position corrected");
        settings.WindowRect.anchoredPosition = Vector2.zero;
        var validPosition = settings.WindowRect.anchoredPosition; yield return null;
        Check((settings.WindowRect.anchoredPosition - validPosition).sqrMagnitude < .0001f, "valid placement remains unchanged");
        var savedLayout = settings.CaptureLayout(); savedLayout.anchoredPosition = new Vector2(-1500f, 1800f);
        GameDataManager.Instance.SetOSWindowLayout(savedLayout); os.RestoreWindowLayouts(); yield return null;
        CheckAccessible("offscreen saved layout corrected");
        Check(GameDataManager.Instance.TryGetOSWindowLayout(settings.WindowId, out var corrected)
            && (corrected.anchoredPosition - settings.WindowRect.anchoredPosition).sqrMagnitude < .001f, "corrected position replaces invalid saved coordinates");

        context = "settings pointer drag";
        var dragArea = (RectTransform)Get(settings, "dragArea");
        var raycaster = settings.GetComponentInParent<GraphicRaycaster>();
        if (raycaster == null) raycaster = os.GetComponentInChildren<GraphicRaycaster>(true);
        var eventCamera = raycaster != null ? raycaster.eventCamera : null;
        Vector2 start = RectTransformUtility.WorldToScreenPoint(eventCamera, dragArea.TransformPoint(dragArea.rect.center));
        var hit = new RaycastResult { gameObject = dragArea.gameObject, module = raycaster };
        var pointer = new PointerEventData(EventSystem.current) { position = start, pressPosition = start, button = PointerEventData.InputButton.Left,
            pointerCurrentRaycast = hit, pointerPressRaycast = hit };
        settings.OnPointerDown(pointer); settings.OnBeginDrag(pointer);
        Check(settings.IsPointerManipulating, "actual title drag begins");
        pointer.position = start + new Vector2(Screen.width * 3f, Screen.height * 3f); pointer.delta = pointer.position - start;
        settings.OnDrag(pointer); yield return null; CheckAccessible("drag beyond upper right keeps controls accessible");
        settings.OnEndDrag(pointer); settings.OnPointerUp(pointer);
        settings.Minimize(); settings.WindowRect.anchoredPosition = new Vector2(2000f, -1000f);
        settings.RestoreAndBringToFront(); yield return null; CheckAccessible("minimized offscreen settings recover on restore");

        context = "viewport and nested canvas";
        liftedCanvas = settings.GetComponent<Canvas>(); ownsCanvas = liftedCanvas == null;
        if (ownsCanvas) liftedCanvas = settings.gameObject.AddComponent<Canvas>();
        canvasOverride = liftedCanvas.overrideSorting; canvasEnabled = liftedCanvas.enabled;
        canvasOrder = liftedCanvas.sortingOrder; canvasLayer = liftedCanvas.sortingLayerID;
        liftedCanvas.enabled = true; liftedCanvas.overrideSorting = true; liftedCanvas.sortingOrder = 180;
        settings.WindowRect.anchoredPosition = new Vector2(1000f, 1000f); yield return null;
        CheckAccessible("nested sorting Canvas uses desktop coordinates");
        desktop.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 360f);
        desktop.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 260f); yield return null;
        CheckAccessible("viewport shrink keeps settings reachable");
        desktop.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, settings.WindowRect.rect.width - 40f);
        desktop.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, settings.WindowRect.rect.height - 40f); yield return null;
        CheckAccessible("undersized viewport prioritizes upper right controls");
        RestoreDesktop(); RestoreCanvas(); yield return null; CheckAccessible("restored viewport recovers full settings window");

        context = "processing countdown pauses and resumes";
        var state = GameDataManager.Instance.OSCharacterInteractionState;
        state.BeginPresentation(OSCharacterInteractionIds.Chapter01Tool.FilesFound);
        state.CompletePresentation(OSCharacterInteractionIds.Chapter01Tool.FilesFound);
        Check(os.LinkMessageController.TryPresentEntry(OSCharacterInteractionIds.Chapter01Tool.Processing), "real processing message starts");
        yield return Wait(() => state.nextEntryCountdownKey != null && state.nextEntryCountdownRemaining > 0f, "processing text arms authored countdown");
        float remaining = state.nextEntryCountdownRemaining;
        yield return new WaitForSecondsRealtime(.5f);
        Check(Mathf.Abs(state.nextEntryCountdownRemaining - remaining) < .001f, "settings preserves paused processing countdown");
        Check(launcher.ToggleOptionWindow() && settings.IsMinimized, "power button hides reachable settings");
        yield return Wait(() => state.presentingEntryId == OSCharacterInteractionIds.Chapter01Tool.AwaitShortcut, "closing settings resumes to Ctrl+R prompt", 12f);

        context = "real Russian Continue and menu return";
        var data = GameDataManager.Instance; var saves = GameAutosaveService.Instance;
        data.SetCurrentLanguage(LanguageState.ru); saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        Check(saves.FlushNow() && saves.HasSave, "Russian fixture autosave written to isolated storage");
        var previousMenu = MainMenuHandle.Instance;
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "real return-to-menu request accepted");
        yield return Wait(() => !GameManager.Instance.IsTransitioning && MainMenuHandle.Instance.IsMainMenuVisible, "first menu visible");
        VerifyMenu(LanguageState.ru);
        Check(GameSessionFlow.Instance.ContinueGame(), "real Continue request accepted");
        yield return Wait(() => !GameSessionFlow.Instance.IsBusy && !GameManager.Instance.IsTransitioning && OS_Handle.Instance != null, "Continue reload complete", 80f);
        os = OS_Handle.Instance; os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        Check(MainMenuHandle.Instance != previousMenu && data.CurrentLanguageState == LanguageState.ru, "Continue rebuilt menu without language change");
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "return after Continue accepted");
        yield return Wait(() => !GameManager.Instance.IsTransitioning && MainMenuHandle.Instance.IsMainMenuVisible, "rebuilt menu visible");
        VerifyMenu(LanguageState.ru);
        saves.EnterTestSession();
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
        {
            data.SetCurrentLanguage(language);
            var menu = MainMenuHandle.Instance;
            foreach (var text in menu.GetComponentsInChildren<OSLocalizedText>(true))
                if (text.TargetText != null) text.TargetText.text = "中文默认值";
            Invoke(menu, "ApplyPresentation", new object[] { true });
            VerifyMenu(language);
        }
    }

    static void VerifyMenu(LanguageState language)
    {
        var data = GameDataManager.Instance;
        var labels = MainMenuHandle.Instance.GetComponentsInChildren<OSLocalizedText>(true).Where(t => t.TargetText != null
            && !string.IsNullOrEmpty(t.LocalizationKey) && (t.LocalizationKey.StartsWith("ui.main_menu.")
                || t.LocalizationKey.StartsWith("ui.os.option.")) && !t.LocalizationKey.EndsWith(".current")).ToArray();
        Check(labels.Length >= 4 && data.CurrentLanguageState == language, "menu has localized labels in " + language);
        foreach (var label in labels)
        {
            string expected = data.LocalizationData.GetText(label.LocalizationKey, language, (string)Get(label, "fallbackText"));
            var args = (string[])Get(label, "formatArguments");
            if (args != null && args.Length > 0) expected = string.Format(expected, args.Cast<object>().ToArray());
            Check(label.TargetText.text == OSPalette.TintRichText(expected), "menu " + language + " / " + label.LocalizationKey);
            if (language == LanguageState.ko && !label.PreserveAuthoringFont)
                Check(label.TargetText.font == data.LocalizationData.KoreanFont, "Korean menu font / " + label.LocalizationKey);
        }
    }

    static void CheckAccessible(string label)
    {
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4]; settings.WindowRect.GetWorldCorners(corners);
        var points = corners.Select(desktop.InverseTransformPoint).ToArray();
        float left = points.Min(p => p.x), right = points.Max(p => p.x), bottom = points.Min(p => p.y), top = points.Max(p => p.y);
        var safe = desktop.rect; safe.xMin += 2f; safe.xMax -= 2f; safe.yMin += 2f; safe.yMax -= 2f;
        bool x = right - left <= safe.width + .01f ? left >= safe.xMin - .1f && right <= safe.xMax + .1f : Mathf.Abs(right - safe.xMax) < .1f;
        bool y = top - bottom <= safe.height + .01f ? bottom >= safe.yMin - .1f && top <= safe.yMax + .1f : Mathf.Abs(top - safe.yMax) < .1f;
        Check(x && y, label + " [window=" + new Rect(left, bottom, right - left, top - bottom) + ", desktop=" + safe + "]");
    }
    static void RestoreDesktop() { if (desktop != null) desktop.sizeDelta = originalDesktopSize; }
    static void RestoreCanvas()
    {
        if (liftedCanvas == null) return;
        if (ownsCanvas) UnityEngine.Object.DestroyImmediate(liftedCanvas);
        else { liftedCanvas.overrideSorting = canvasOverride; liftedCanvas.enabled = canvasEnabled; liftedCanvas.sortingOrder = canvasOrder; liftedCanvas.sortingLayerID = canvasLayer; }
        liftedCanvas = null;
    }
    static bool Ready(OSWindowUnit w) => w != null && w.IsOpen && !w.IsOpening && !w.IsClosing;
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { testMouse?.MakeCurrent(); yield return null; }
        if (!predicate()) throw new TimeoutException(label);
        Check(true, label);
    }
    static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); if (!checks.Contains(label)) checks.Add(label); }
    static object Get(object target, string field) => target.GetType().GetField(field, Fields).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    static object Invoke(object target, string method, object[] args) => target.GetType().GetMethod(method, Fields).Invoke(target, args);
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
