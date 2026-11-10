using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Focused Play Mode integration QA. Invoke DemoEndingQA.Start(), then inspect
// Status and OutputDirectory. Runs with cloned data and autosave writes disabled.
// Never invokes ExitGame: the caller tests actual quit separately after this run.
public static class DemoEndingQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Prefix = "ui.os.chapter01_ending.";
    static readonly List<string> checks = new List<string>();
    static readonly List<ConfigurationBinding> bindings = new List<ConfigurationBinding>();
    static readonly Dictionary<OSChapter01EndingData, OSChapter01EndingData> copies =
        new Dictionary<OSChapter01EndingData, OSChapter01EndingData>();
    static GameSetup setup;
    static bool originalDemo;
    static string sceneHookFailure;

    sealed class ConfigurationBinding
    {
        public OSChapter01EndingDirector director;
        public OSChapter01EndingData original;
    }

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
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup required";
        originalDemo = Field<bool>(setup, "isDemo");
        checks.Clear();bindings.Clear();copies.Clear();sceneHookFailure = null;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "DemoEndingQA", Guid.NewGuid().ToString("N")));
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
        var originalLanguage = data.CurrentLanguageState;
        bool background = Application.runInBackground;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            // Suppresses menu preference writes and quit callbacks too.
            // Never resume writes or restore the old writable-session state.
            saves.EnterTestSession();
            hashes = paths.Select(Fingerprint).ToArray();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
            SceneManager.sceneLoaded += OnSceneLoaded;
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (sceneHookFailure != null) throw new InvalidOperationException(sceneHookFailure);
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose();continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED: " + error;
                    Debug.LogError("[Demo Ending QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested);else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "normal save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "test session still blocks all autosave writes");
            Status = "PASS: " + checks.Count + " demo ending checks";
            Debug.Log("[Demo Ending QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            saves.EnterTestSession();saves.EndSession();
            if (setup != null) SetField(setup, "isDemo", originalDemo);
            foreach (var binding in bindings)
                if (binding.director != null) SetField(binding.director, "configuration", binding.original);
            foreach (var copy in copies.Values)
                if (copy != null) UnityEngine.Object.Destroy(copy);
            bindings.Clear();copies.Clear();
            GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
            // Scene reloads destroy old controllers. Restore the original save
            // reference, without reconnecting new controllers to old OS objects.
            data.ReplaceSaveData(original);
            data.SetCurrentLanguage(originalLanguage);
            if (resources != null) resources.enabled = resourcesEnabled;
            Application.runInBackground = background;
            if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes))
                Status = "FAILED: normal save files changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted before completion";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"),
                Status + "\n" + string.Join("\n", checks)
                + "\nExit button was inspected but intentionally not invoked.\n");
        }
    }

    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        SetField(setup, "isDemo", true);
        Check(GameSetup.IsDemo, "GameSetup isDemo=true is read dynamically");
        GameManager.Instance.OpenMainMenuWithoutSave();
        yield return WaitMenu();
        ApplyConfiguration(OS_Handle.Instance?.Chapter01Ending);
        var console = GameTest.Instance.GetComponent<GameTestConsole>();
        Check(console != null, "runtime debug console exists");
        console.SetOpen(true);yield return new WaitForEndOfFrame();yield return null;
        yield return Shot("01-console-fifth-entry");
        yield return ClickEndingCheckpoint(console, "Boot jump");
        var first = OS_Handle.Instance.Chapter01Ending;
        Check(data.OSProcessData.memory.pictureScenario.Has("robot_repaired"),
            "ending fixture has a repaired robot");
        yield return WaitDoor(first, "demo checkpoint reaches clickable door");
        // An old demo save must stop at the new boundary, not leak the full-game movie.
        RestartAt(first, OSChapter01EndingPhase.Animation);
        yield return WaitDoor(first, "legacy demo Animation save returns to Door");
        RestartAt(first, OSChapter01EndingPhase.Glitch);
        yield return WaitDoor(first, "legacy demo Glitch save returns to Door");
        RestartAt(first, OSChapter01EndingPhase.Slides);
        yield return WaitDoor(first, "normal slideshow holds its final slide and opens Door");
        CheckDoor(first);
        yield return CheckDoorHover(first);
        yield return Shot("02-first-door");

        var oldCursor = CursorHandle.Instance;
        yield return ClickEndingCheckpoint(console, "Door repeat jump");
        var second = OS_Handle.Instance.Chapter01Ending;
        Check(first == null && !ReferenceEquals(first, second), "repeat jump destroys previous director");
        Check(oldCursor == null && !ReferenceEquals(oldCursor, CursorHandle.Instance),
            "repeat jump replaces old cursor object");
        yield return WaitDoor(second, "repeated demo checkpoint reaches clickable door");
        Check(CursorHandle.Instance != null && !CursorHandle.Instance.IsScriptControlled,
            "door prompt gives the player mouse control");
        yield return ClickDemoDoorAndObserveWipe(second);
        Check(CursorHandle.Instance?.IsScriptControlled != true, "completed card releases cursor");
        CheckEndButtons(second, "demo");
        yield return ValidateCardLanguages(second, true);
        data.SetCurrentLanguage(LanguageState.zh_CN);yield return null;
        Check(Field<TMP_Text>(second, "endTitle").text == "Demo内容到此结束", "Chinese DEMO title matches requested copy");
        Check(Field<TMP_Text>(second, "endMessage").text.TrimEnd('。') == "第二章内容将在26.10.20新品节发布",
            "Chinese DEMO message announces the exact 26.10.20 release date");
        yield return Shot("03-demo-complete-zh");
        CaptureCompletion("completion-demo.json");
        RestartAt(second, OSChapter01EndingPhase.DoorKnocked);
        yield return WaitCompleted(second, "clicked-door save resumes to Demo completion without another click");
        Check(!Field<RawImage>(second, "videoImage").gameObject.activeInHierarchy,
            "resumed clicked-door save keeps the full-game movie hidden");

        Click(Field<Button>(second, "returnButton"), "demo return-to-menu button");
        yield return WaitMenu();
        Check(!second.IsPresenting && CursorHandle.Instance?.IsScriptControlled != true,
            "real return button hides ending and releases cursor");
        Check(MainMenuHandle.Instance != null && MainMenuHandle.Instance.IsMainMenuVisible,
            "real return button reveals main menu");
        yield return Shot("04-returned-menu");

        SetField(setup, "isDemo", false);
        Check(!GameSetup.IsDemo, "GameSetup isDemo=false is read dynamically");
        yield return ClickEndingCheckpoint(console, "Non-demo jump");
        var full = OS_Handle.Instance.Chapter01Ending;
        RestartAt(full, OSChapter01EndingPhase.Door);
        yield return WaitDoor(full, "full-game Door save resumes at the clickable door");
        CheckDoor(full);
        Click(Field<Button>(full, "doorButton"), "full-game wooden door");
        yield return Wait(() => full.Phase == OSChapter01EndingPhase.Animation,
            "full-game door click continues to Animation", 5f);
        Check(!Field<GameObject>(full, "endCard").activeInHierarchy,
            "full-game door click never displays the Demo ending card");
        Check(full.Configuration.chapter02MemoryIndex == -1, "non-demo fixture has no Chapter 2 destination");
        yield return WaitRecoveryCompleted(full, "non-demo repair completes without Chapter 2 memory data");
        Check(data.CurrentChapterIndex == 2, "repair enters Chapter 2 without a memory destination");
        Check(!full.IsPresenting && !Field<GameObject>(full, "endCard").activeInHierarchy
            && !OS_Handle.Instance.IsSystemInputLocked && CursorHandle.Instance?.IsScriptControlled != true,
            "full game returns OS control instead of displaying a completion card");
        Check(full.GetComponent<OSChapter02RecoveryPresentation>()?.Completed == true,
            "full game plays the repair and bug sequence");
        yield return Shot("05-non-demo-os-restored");
        CaptureCompletion("completion-non-demo.json");

    }

    static void RestartAt(OSChapter01EndingDirector ending, OSChapter01EndingPhase phase)
    {
        // The live director is the same object and uses the isolated save. This
        // exercises its real resume presentation, without editing the authored asset.
        typeof(OSChapter01EndingDirector).GetMethod("CancelPresentation", Private).Invoke(ending, null);
        var saved = GameDataManager.Instance.OSProcessData.chapter01Ending;
        saved.phase = phase;saved.slideIndex = 0;saved.completed = false;
        typeof(OSChapter01EndingDirector).GetMethod("BeginPresentation", Private).Invoke(ending, null);
    }

    static IEnumerator WaitDoor(OSChapter01EndingDirector ending, string label) => Wait(() =>
        ending != null && ending.Phase == OSChapter01EndingPhase.Door
        && Field<bool>(ending, "waitingDoor")
        && Field<Button>(ending, "doorButton").IsActive()
        && Field<Button>(ending, "doorButton").IsInteractable(), label, 8f);

    static void CheckDoor(OSChapter01EndingDirector ending)
    {
        var image = Field<Image>(ending, "doorImage");
        var window = Field<RectTransform>(ending, "doorWindow");
        Check(image != null && image.sprite != null && image.sprite.rect.height > image.sprite.rect.width,
            "door displays a portrait photograph sprite");
        Check(window != null && window.gameObject.activeInHierarchy
            && image.transform.IsChildOf(window), "door photograph belongs to the popup window");
        Check(Field<CanvasGroup>(ending, "doorWindowGroup").alpha >= .99f,
            "door popup finishes opening before accepting input");
        Check(ending.Configuration.doorSound != null, "door has a knocking sound assigned");
        Check(!Field<GameObject>(ending, "endCard").activeInHierarchy
            && !Field<RawImage>(ending, "videoImage").gameObject.activeInHierarchy,
            "door prompt leaves the ending card and movie hidden");
        Check(!Field<TMP_Text>(ending, "caption").gameObject.activeInHierarchy,
            "door interaction uses visual hints instead of an instruction caption");
        foreach (string name in new[] { "Minimize", "Close" })
        {
            var control = window.Find("Chrome/TitleBar/" + name)?.GetComponent<Button>();
            Check(control != null && control.gameObject.activeInHierarchy && !control.IsInteractable(),
                "door window " + name + " control is visible but disabled");
        }
        PointerFor(Field<Button>(ending, "doorButton"), "wooden door");
    }

    static IEnumerator CheckDoorHover(OSChapter01EndingDirector ending)
    {
        var button = Field<Button>(ending, "doorButton");
        var corners = button.transform.Find("HoverCorners")?.GetComponent<CanvasGroup>();
        var glint = button.transform.Find("HandleGlint")?.GetComponent<Image>();
        Check(corners != null && glint != null, "door has non-text corner and handle hover cues");
        var pointer = PointerFor(button, "door hover");
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        // Observe inside the short pulse, rather than requiring a permanent shine.
        yield return new WaitForSecondsRealtime(.2f);
        Check(corners.alpha > .01f && glint.color.a > .01f,
            "entering the door reveals its corners and briefly shines the handle");
        yield return Shot("02-door-hover");
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        yield return new WaitForSecondsRealtime(.25f);
        Check(corners.alpha <= .001f && glint.color.a <= .001f,
            "leaving the door clears the corner and handle cues");
        Check(ending.Phase == OSChapter01EndingPhase.Door && button.IsInteractable(),
            "hover never accepts the door or advances the ending");
    }

    static IEnumerator ClickDemoDoorAndObserveWipe(OSChapter01EndingDirector ending)
    {
        var button = Field<Button>(ending, "doorButton");
        Click(button, "demo wooden door");
        float deadline = Time.realtimeSinceStartup + 8f;
        bool sawKnock = false, sawWipe = false, sawCheckpoint = false, tookWipeShot = false;
        float previousBottom = 1f;
        while (ending.Phase != OSChapter01EndingPhase.Completed && Time.realtimeSinceStartup < deadline)
        {
            Check(ending.Phase != OSChapter01EndingPhase.Animation
                && ending.Phase != OSChapter01EndingPhase.Glitch
                && ending.Phase != OSChapter01EndingPhase.RecoveryAwaitingConfirmation
                && ending.Phase != OSChapter01EndingPhase.Chapter02Started,
                "demo click never enters full-game animation, glitch, or recovery");
            if (ending.Phase == OSChapter01EndingPhase.DoorKnocked)
            {
                if (!sawCheckpoint)
                {
                    var saved = GameDataManager.Instance.OSProcessData.chapter01Ending;
                    var restored = JsonUtility.FromJson<OSChapter01EndingSaveData>(JsonUtility.ToJson(saved));
                    restored.EnsureData();
                    Check(restored.phase == OSChapter01EndingPhase.DoorKnocked && !restored.completed,
                        "clicked-door checkpoint survives save JSON and validation");
                    sawCheckpoint = true;
                }
                Check(!button.IsInteractable(), "door cannot be clicked repeatedly during the knock");
                var audio = AudioManager.Instance;
                if (audio != null && audio.SFXPool.Any(source => source != null && source.isPlaying
                    && source.clip == ending.Configuration.doorSound)) sawKnock = true;
            }
            var black = Field<Image>(ending, "blackFade");
            var rect = black.rectTransform;
            float bottom = rect.anchorMin.y;
            if (black.color.a >= .99f && bottom > .01f && bottom < .99f)
            {
                Check(Mathf.Abs(rect.anchorMax.y - 1f) < .001f
                    && Mathf.Abs(rect.anchorMin.x) < .001f && Mathf.Abs(rect.anchorMax.x - 1f) < .001f,
                    "black wipe remains attached to the top edge and spans the full screen width");
                Check(bottom <= previousBottom + .001f, "black wipe advances from top to bottom");
                previousBottom = bottom;sawWipe = true;
                if (!tookWipeShot && bottom < .65f && bottom > .2f)
                { tookWipeShot = true;yield return Shot("03-demo-wipe"); }
            }
            yield return null;
        }
        Check(sawCheckpoint && sawKnock, "demo click persists the door checkpoint and plays knocking audio");
        Check(sawWipe, "demo ending visibly uses a top-to-bottom wipe");
        yield return WaitCompleted(ending, "demo door click completes at the new boundary");
        Check(GameDataManager.Instance.CurrentChapterIndex == 1,
            "demo completion does not unlock Chapter 2");
    }

    static IEnumerator ClickEndingCheckpoint(GameTestConsole console, string label)
    {
        console.SetOpen(true);yield return new WaitForEndOfFrame();yield return null;
        var button = console.GetComponentsInChildren<Button>(true)
            .SingleOrDefault(candidate => candidate.gameObject.name == "第一章_结尾动画");
        Check(button != null, label + ": fifth checkpoint button exists");
        Click(button, label + ": real fifth checkpoint button");
        Check(GameTest.Instance.IsCheckpointJumpRunning, label + ": click starts checkpoint reload");
        float deadline = Time.realtimeSinceStartup + 65f;
        bool visitedSlidesOrDoor = false;
        while (GameTest.Instance.IsCheckpointJumpRunning && Time.realtimeSinceStartup < deadline)
        {
            var ending = OS_Handle.Instance?.Chapter01Ending;
            if (ending != null && (ending.Phase == OSChapter01EndingPhase.Slides
                || ending.Phase == OSChapter01EndingPhase.Door)) visitedSlidesOrDoor = true;
            yield return null;
        }
        Check(!GameTest.Instance.IsCheckpointJumpRunning, label + ": reload finished");
        Check(GameTest.Instance.JumpStatus == "关键区域已就绪。", label + ": checkpoint reports success");
        yield return null; // Let GameTestConsole.Update observe successful completion.
        var current = OS_Handle.Instance?.Chapter01Ending;
        Check(current != null && current.IsPresenting, label + ": ending presentation starts");
        if (GameSetup.IsDemo)
            Check(current.Phase == OSChapter01EndingPhase.Door,
                label + ": Demo checkpoint stops at the clickable Door boundary");
        else
        {
            Check(!visitedSlidesOrDoor && current.Phase == OSChapter01EndingPhase.Animation,
                label + ": full-game checkpoint starts Animation and skips Slides/Door");
            Check(!Field<Button>(current, "doorButton").gameObject.activeInHierarchy,
                label + ": full-game debug jump keeps the door hidden");
        }
        Check(!console.IsOpen, label + ": console closes automatically");
        Check(GameAutosaveService.Instance.IsTestSession, label + ": save isolation remains active");
    }

    static IEnumerator ValidateCardLanguages(OSChapter01EndingDirector ending, bool demo)
    {
        var languages = (LanguageState[])Enum.GetValues(typeof(LanguageState));
        Check(languages.Length == 10, "project exposes ten languages");
        foreach (LanguageState language in languages)
        {
            var data = GameDataManager.Instance;
            data.SetCurrentLanguage(language);yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var text in Field<GameObject>(ending, "endCard").GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();
                Rect rect = text.rectTransform.rect;
                Bounds bounds = text.textBounds;
                const float tolerance = .75f;
                Check(!text.isTextOverflowing && bounds.min.x >= rect.xMin - tolerance
                    && bounds.max.x <= rect.xMax + tolerance && bounds.min.y >= rect.yMin - tolerance
                    && bounds.max.y <= rect.yMax + tolerance,
                    (demo ? "demo" : "non-demo") + " text bounds: " + language + " / "
                    + text.transform.parent.name + "/" + text.name);
            }
            CheckLocalized(Field<TMP_Text>(ending, "endTitle"), demo ? "demo_title" : "chapter_complete_title", language);
            CheckLocalized(Field<TMP_Text>(ending, "endMessage"), demo ? "demo_update" : "chapter_complete_body", language);
            if (demo)
            {
                string message = Field<TMP_Text>(ending, "endMessage").text;
                Check(message.Contains(language == LanguageState.zh_CN ? "26.10.20" : "2026"),
                    "localized release announcement includes the explicit year/date: " + language);
            }
            CheckLocalized(Field<Button>(ending, "returnButton").GetComponentInChildren<TMP_Text>(), "return_menu", language);
            CheckLocalized(Field<Button>(ending, "exitButton").GetComponentInChildren<TMP_Text>(), "exit_game", language);
            CheckEndButtons(ending, (demo ? "demo" : "non-demo") + " / " + language);
            yield return Shot((demo ? "demo-" : "full-") + language);
        }
    }

    static void CheckLocalized(TMP_Text label, string suffix, LanguageState language)
    {
        Check(label != null && GameDataManager.Instance.TryGetLocalizedText(Prefix + suffix, out string expected)
            && !string.IsNullOrWhiteSpace(expected) && label.text == expected,
            "localized copy: " + suffix + " / " + language);
    }

    static void CheckEndButtons(OSChapter01EndingDirector ending, string label)
    {
        Check(Field<GameObject>(ending, "endCard").activeInHierarchy, label + ": ending card visible");
        var back = Field<Button>(ending, "returnButton");
        var exit = Field<Button>(ending, "exitButton");
        Check(back != null && exit != null && back != exit, label + ": two distinct ending buttons");
        PointerFor(back, label + " return");PointerFor(exit, label + " exit");
        Check(HasListener(back.onClick, ending, "ReturnToMainMenu"), label + ": return runtime listener bound");
        Check(HasListener(exit.onClick, ending, "ExitGame"), label + ": exit runtime listener bound (not invoked)");
    }

    static PointerEventData PointerFor(Button button, string label)
    {
        Check(button != null && button.IsActive() && button.IsInteractable(), label + ": button active and interactable");
        Check(EventSystem.current != null, label + ": EventSystem exists");
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>();
        var pointer = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rect.TransformPoint(rect.rect.center))
        };
        var hits = new List<RaycastResult>();EventSystem.current.RaycastAll(pointer, hits);
        bool reachesButton = hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button;
        string diagnostic = string.Empty;
        if (!reachesButton)
        {
            var firstHits = hits.Take(3).Select(hit => HierarchyPath(hit.gameObject.transform)
                + " [depth=" + hit.depth + ", order=" + hit.sortingOrder + "]");
            diagnostic = " | position=" + pointer.position + " | expected=" + HierarchyPath(button.transform)
                + " | first hits=" + (hits.Count > 0 ? string.Join("; ", firstHits) : "none");
        }
        Check(reachesButton, label + ": real raycast reaches button first" + diagnostic);
        pointer.pointerCurrentRaycast = hits[0];
        return pointer;
    }

    static string HierarchyPath(Transform target)
    {
        string path = target.name;
        for (var parent = target.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }

    static void Click(Button button, string label)
    {
        var pointer = PointerFor(button, label);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }

    static bool HasListener(UnityEvent evt, object target, string method)
    {
        // PrepareInvoke reads the call list; it never invokes a listener.
        var prepare = typeof(UnityEventBase).GetMethod("PrepareInvoke", Private);
        if (prepare == null) throw new MissingMethodException("UnityEventBase.PrepareInvoke");
        foreach (var call in (IEnumerable)prepare.Invoke(evt, null))
            for (Type type = call.GetType(); type != null; type = type.BaseType)
                foreach (var field in type.GetFields(Private | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (field.GetValue(call) is Delegate action)
                        foreach (Delegate item in action.GetInvocationList())
                            if (ReferenceEquals(item.Target, target) && item.Method.Name == method) return true;
        return false;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var ending in root.GetComponentsInChildren<OSChapter01EndingDirector>(true))
                    ApplyConfiguration(ending);
        }
        catch (Exception error) { sceneHookFailure = "Cannot bind isolated ending configuration: " + error; }
    }

    static void ApplyConfiguration(OSChapter01EndingDirector ending)
    {
        if (ending == null || bindings.Any(binding => ReferenceEquals(binding.director, ending))) return;
        var original = ending.Configuration;
        Check(original != null, "authored ending configuration exists");
        if (!copies.TryGetValue(original, out var copy))
        {
            copy = UnityEngine.Object.Instantiate(original);
            copy.name = original.name + " (DemoEndingQA runtime copy)";
            copy.hideFlags = HideFlags.DontSave;
            copy.animationVideo = null;copy.animationFrames = Array.Empty<Sprite>();
            copy.initialBlackSeconds = .1f;copy.animationPlaceholderSeconds = 4f;
            copy.frozenSeconds = .1f;copy.glitchSeconds = .15f;copy.fadeOutSeconds = .1f;
            copy.recoveryFreezeSeconds = .15f;
            // Instantiate already isolates serialized nested slide data.
            foreach (var slide in copy.slides ?? Array.Empty<OSChapter01EndingData.Slide>())
                if (slide != null) slide.holdSeconds = .15f;
            copy.chapter02MemoryIndex = -1;
            copies.Add(original, copy);
        }
        bindings.Add(new ConfigurationBinding { director = ending, original = original });
        SetField(ending, "configuration", copy);
    }

    static void CaptureCompletion(string filename)
    {
        var data = GameDataManager.Instance;
        string json = JsonUtility.ToJson(data.SaveData, true);
        var snapshot = JsonUtility.FromJson<GameSaveData>(json);
        OSChapter01EndingPhase expected = GameSetup.IsDemo
            ? OSChapter01EndingPhase.Completed : OSChapter01EndingPhase.Chapter02Started;
        Check(snapshot.osProcess.chapter01Ending.completed
            && snapshot.osProcess.chapter01Ending.phase == expected,
            filename + ": JSON preserves the appropriate completion phase and flag");
        File.WriteAllText(Path.Combine(OutputDirectory, filename), json);
    }

    static IEnumerator WaitCompleted(OSChapter01EndingDirector ending, string label)
    {
        yield return Wait(() => ending != null && ending.Phase == OSChapter01EndingPhase.Completed
            && GameDataManager.Instance.OSProcessData.chapter01Ending.completed
            && Field<GameObject>(ending, "endCard").activeInHierarchy, label, 20f);
        // Completed and the card's SetActive are published in the same coroutine
        // frame. Let newly enabled graphics receive render depths before raycasting.
        yield return null;
        yield return new WaitForEndOfFrame();
    }
    static IEnumerator WaitRecoveryCompleted(OSChapter01EndingDirector ending, string label)
    {
        yield return Wait(() => ending != null && ending.Phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation
            && !ending.IsPresenting && GameDataManager.Instance.OSProcessData.chapter01Ending.completed,
            label + ": waits for confirmation", 35f);
        Check(GameDataManager.Instance.CurrentChapterIndex == 1,
            label + ": finishing recovery alone does not advance Chapter 1");
        yield return OSChapter02RecoveryQA.ConfirmRecoveryOption(OS_Handle.Instance, label + ": confirm click");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Chapter02Started
            && GameDataManager.Instance.CurrentChapterIndex == 2, label, 3f);
    }
    static IEnumerator WaitMenu() => Wait(() => GameManager.Instance.Phase == GamePhase.Boot
        && !GameManager.Instance.IsTransitioning && !GameSessionFlow.Instance.IsBusy
        && !SceneLoadManager.Instance.IsBusy, "main menu ready", 65f);
    static IEnumerator Wait(Func<bool> condition, string label, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    static IEnumerator Shot(string filename)
    {
        yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(OutputDirectory, filename + ".png"), texture.EncodeToPNG()); }
        finally { UnityEngine.Object.Destroy(texture); }
    }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
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

