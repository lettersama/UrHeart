using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in Play Mode presentation checks. All fixture/save writes remain in Temp.
public static class OSWindowActivityDimmingQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string OverlayName = "ActivityDimOverlay";
    static readonly List<string> checks = new List<string>();
    static readonly List<object> observations = new List<object>();
    static OS_Handle os;
    static OSWindowActivityDimming dimming;
    static OSWindowUnit memoryWindow, stateWindow, toolWindow, childWindow;
    static GameObject childBranch;
    static Canvas liftedCanvas;
    static bool ownsLiftedCanvas, previousCanvasEnabled, previousCanvasOverride;
    static int previousCanvasLayer, previousCanvasOrder;
    static GameSetup setup;
    static Mouse testMouse;
    static EditorWindow gameView, previousEditorFocus;
    static bool cancel;
    static string context;

    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null
            || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        gameView = Resources.FindObjectsOfTypeAll<EditorWindow>()
            .FirstOrDefault(window => window.GetType().FullName == "UnityEditor.GameView");
        if (gameView == null) return "An existing Game view is required for runtime validation";
        previousEditorFocus = EditorWindow.focusedWindow;

        checks.Clear(); observations.Clear(); cancel = false;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSWindowActivityDimmingQA", Guid.NewGuid().ToString("N")));
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
        bool demo = (bool)Get(setup, "isDemo");
        var previousMouse = Mouse.current;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();
            hashes = paths.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory);
            saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Application.runInBackground = true;
            Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("WindowDimmingQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            testMouse.MakeCurrent();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    Focus();
                    if (cancel) throw new OperationCanceledException("Window dimming QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + error;
                    Debug.LogError("[Window Dimming QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "original save files remain unchanged");
            Status = "PASS";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                // A timed-out checkpoint is owned by GameTest, not this driver.
                // Stop it before giving any manager the original save object again.
                if (GameTest.Instance != null && GameTest.Instance.IsCheckpointJumpRunning)
                    GameTest.Instance.StopAllCoroutines();
                RestoreCanvas();
                if (childBranch != null) UnityEngine.Object.DestroyImmediate(childBranch);
                if (dimming != null) dimming.enabled = true;
                os?.LinkMessageController?.SetGlobalMessagesSuppressed(true);
                os?.LinkMessageController?.NotifyOSHidden();
                os?.Chapter03Story?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession();
                saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original);
                data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousEditorFocus != null) previousEditorFocus.Focus();
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo)
                    throw new InvalidOperationException("Original save object, language or mode was not restored");
                if (Application.runInBackground != background || resources != null && resources.enabled != resourcesEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse)
                    throw new InvalidOperationException("Resource, background or input settings were not restored");
                if (hashes != null && !paths.Select(Hash).SequenceEqual(hashes))
                    throw new IOException("Original save files changed");
                checks.Add("cleanup restores original save object, language, mode, resources, mouse, editor focus and file hashes");
            }
            catch (Exception cleanup) { Status = "FAILED during cleanup: " + cleanup + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, originalFiles = paths, originalHashes = hashes,
                finalHashes = paths.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Exit Play Mode after this isolated run. Chapter01BeforeToolUnlock and real Chapter03MemoryStart fixtures; ordinary child windows inherit their parent, while pinned or temporarily lifted groups do not compete for ordinary activity."
            }, Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Window Dimming QA] " + Status + " | " + OutputDirectory);
            os = null; dimming = null; memoryWindow = null; stateWindow = null; toolWindow = null;
            childWindow = null; childBranch = null; setup = null; testMouse = null;
            gameView = null; previousEditorFocus = null;
        }
    }

    static IEnumerator Run()
    {
        yield return RunPrologueFault();
        os = null; dimming = null; stateWindow = null;
        context = "Chapter01BeforeToolUnlock fixture";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeToolUnlock), "isolated Chapter 1 checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        os = OS_Handle.Instance;
        dimming = os.GetComponent<OSWindowActivityDimming>();
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession
            && os.IsOSVisible && dimming != null && dimming.enabled,
            "fixture succeeds with formal writes disabled and the production dimming component attached");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        os.LinkMessageController?.SetGlobalMessagesSuppressed(true);
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true))
            if (window.IsOpen) window.CloseSilently();
        // Unlock only the isolated fixture so the real Tool launcher can be exercised.
        var data = GameDataManager.Instance;
        data.UnlockTool();
        data.MarkToolInitialized();
        data.MarkToolProgramsUnpacked();

        context = "real launcher paths";
        bool openedMemory = os.OpenMemoryWindow();
        bool openedState = (bool)Invoke(os, "OpenStateCheckWindow");
        bool openedTool = os.OpenToolWindow();
        memoryWindow = os.MemoryWindowController?.GetComponent<OSWindowUnit>();
        stateWindow = os.StateCheckWindowController?.GetComponent<OSWindowUnit>();
        toolWindow = os.ToolWindowController?.GetComponent<OSWindowUnit>();
        Check(openedMemory && openedState && openedTool && memoryWindow != null && stateWindow != null && toolWindow != null,
            "real Memory, StateCheck and Tool launcher APIs open their authored windows");
        yield return Wait(() => Ready(memoryWindow) && Ready(stateWindow) && Ready(toolWindow), "opening animations finish");
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true))
            if (window.IsOpen && window != memoryWindow && window != stateWindow && window != toolWindow)
                window.CloseSilently();
        memoryWindow.SetAlwaysOnTop(false);
        stateWindow.SetAlwaysOnTop(false);
        toolWindow.SetAlwaysOnTop(false);
        toolWindow.BringToFront();
        Refresh();
        Check(dimming.FrontmostWindow == toolWindow && !dimming.IsDimmed(toolWindow)
            && dimming.IsDimmed(memoryWindow) && dimming.IsDimmed(stateWindow),
            "Tool is bright in front while both other ordinary windows dim");
        toolWindow.CloseSilently();

        context = "two ordinary windows and pointer focus";
        memoryWindow.WindowRect.anchoredPosition = new Vector2(-130f, 20f);
        stateWindow.WindowRect.anchoredPosition = new Vector2(130f, 20f);
        stateWindow.BringToFront();
        Refresh();
        Check(FrontIs(stateWindow, memoryWindow), "StateCheck in front is bright and background Memory is dim");
        CheckMask(memoryWindow, "Memory overlay is one black 0.75 Image, stretched to its window and non-raycastable");
        yield return Screenshot("01-statecheck-front");
        memoryWindow.BringToFront();
        Refresh();
        Check(FrontIs(memoryWindow, stateWindow), "bringing Memory forward swaps the two windows' brightness");
        CheckMask(stateWindow, "StateCheck overlay has the same full-window non-blocking presentation");
        Invoke(os, "SetMouseActiveWindow", new object[] { null });
        Refresh();
        Check(os.MouseActiveWindow == null && FrontIs(memoryWindow, stateWindow),
            "null mouse focus leaves the visually frontmost ordinary window bright");
        int memoryIndex = memoryWindow.transform.GetSiblingIndex();
        int stateIndex = stateWindow.transform.GetSiblingIndex();
        Refresh();
        Check(os.MouseActiveWindow == null && memoryWindow.transform.GetSiblingIndex() == memoryIndex
            && stateWindow.transform.GetSiblingIndex() == stateIndex,
            "refresh does not change window sibling order or mouse focus");
        yield return Screenshot("02-memory-front");

        context = "pin, minimize, restore and close";
        stateWindow.SetAlwaysOnTop(true);
        Refresh();
        Check(dimming.FrontmostWindow == memoryWindow && !dimming.IsDimmed(memoryWindow) && !dimming.IsDimmed(stateWindow),
            "pinned StateCheck and the frontmost ordinary Memory are both bright");
        stateWindow.SetAlwaysOnTop(false);
        Refresh();
        Check(FrontIs(stateWindow, memoryWindow), "unpinning the top window returns it to ordinary front-window selection");
        stateWindow.Minimize();
        Refresh();
        Check(stateWindow.IsMinimized && dimming.FrontmostWindow == memoryWindow
            && !dimming.IsDimmed(stateWindow) && !dimming.IsDimmed(memoryWindow),
            "minimizing the front window removes its mask and brightens the next window");
        stateWindow.RestoreAndBringToFront();
        Refresh();
        Check(!stateWindow.IsMinimized && FrontIs(stateWindow, memoryWindow), "restoring StateCheck reestablishes the front window");
        stateWindow.CloseSilently();
        Refresh();
        Check(!stateWindow.IsOpen && dimming.FrontmostWindow == memoryWindow
            && !dimming.IsDimmed(stateWindow) && !dimming.IsDimmed(memoryWindow),
            "closing the front window brightens Memory without retaining a visible closed-window mask");
        if (!(bool)Invoke(os, "OpenStateCheckWindow")) throw new InvalidOperationException("StateCheck failed to reopen");
        yield return Wait(() => Ready(stateWindow), "StateCheck reopens through its real launcher");
        Refresh();

        context = "dynamic parent-child windows";
        CreateChildWindow();
        yield return null; // LateUpdate must discover a window created after initialization.
        Check(dimming.IsDimmed(memoryWindow) && dimming.IsDimmed(childWindow) && !dimming.IsDimmed(stateWindow)
            && dimming.FrontmostWindow == stateWindow,
            "a runtime child is discovered automatically and shares its background parent's dim state");
        CheckMask(childWindow, "runtime child receives exactly one full-window black 0.75 non-blocking mask");
        Image parentMask = OwnMask(memoryWindow), childMask = OwnMask(childWindow);
        Check(parentMask != null && childMask != null
            && parentMask.transform.GetSiblingIndex() < childBranch.transform.GetSiblingIndex()
            && childMask.transform.GetSiblingIndex() == childWindow.WindowRect.childCount - 1,
            "parent mask draws before the child branch, so child content receives only its own 0.75 mask");
        yield return Screenshot("03-parent-child-background");
        memoryWindow.BringToFront();
        Refresh();
        Check(FrontIs(memoryWindow, stateWindow) && !dimming.IsDimmed(childWindow),
            "bringing the parent forward brightens parent and child as one group");
        memoryWindow.SetAlwaysOnTop(true);
        stateWindow.BringToFront();
        Refresh();
        Check(dimming.FrontmostWindow == stateWindow && !dimming.IsDimmed(stateWindow)
            && !dimming.IsDimmed(memoryWindow) && !dimming.IsDimmed(childWindow),
            "pinning the parent exempts the whole child group while the ordinary front window stays bright");
        memoryWindow.SetAlwaysOnTop(false);
        stateWindow.BringToFront();
        Refresh();
        Check(FrontIs(stateWindow, memoryWindow) && dimming.IsDimmed(childWindow),
            "unpinning and moving the group behind StateCheck dims parent and child together");

        context = "Canvas override sorting without pointer or hierarchy changes";
        os.NotifyWindowPointerPressed(stateWindow);
        if (os.MouseActiveWindow != stateWindow) throw new InvalidOperationException("StateCheck pointer-focus precondition failed");
        memoryIndex = memoryWindow.transform.GetSiblingIndex();
        stateIndex = stateWindow.transform.GetSiblingIndex();
        LiftWithCanvas();
        Refresh();
        Check(dimming.FrontmostWindow == stateWindow && !dimming.IsDimmed(stateWindow)
            && !dimming.IsDimmed(memoryWindow) && !dimming.IsDimmed(childWindow),
            "a Canvas-lifted parent and child stay bright without taking ordinary StateCheck activity");
        Check(memoryWindow.transform.GetSiblingIndex() == memoryIndex && stateWindow.transform.GetSiblingIndex() == stateIndex
            && os.MouseActiveWindow == stateWindow,
            "Canvas foreground presentation preserves ordinary activity, window siblings and pointer focus");
        yield return Screenshot("04-canvas-override-front");
        RestoreCanvas();
        Refresh();
        Check(FrontIs(stateWindow, memoryWindow) && dimming.IsDimmed(childWindow) && os.MouseActiveWindow == stateWindow,
            "removing the Canvas lift returns to hierarchy order while preserving pointer focus");
        LiftWithCanvas(childWindow);
        Refresh();
        Check(!HasCanvasLift(memoryWindow) && HasCanvasLift(childWindow)
            && dimming.FrontmostWindow == stateWindow && !dimming.IsDimmed(stateWindow)
            && !dimming.IsDimmed(memoryWindow) && !dimming.IsDimmed(childWindow),
            "lifting only a child Canvas exempts its whole group without taking ordinary StateCheck activity");
        RestoreCanvas();
        Refresh();
        Check(FrontIs(stateWindow, memoryWindow) && dimming.IsDimmed(childWindow) && os.MouseActiveWindow == stateWindow,
            "releasing a child-only Canvas lift restores ordinary parent-child background dimming");

        context = "dynamic cleanup and component lifetime";
        UnityEngine.Object.DestroyImmediate(childBranch);
        childBranch = null;
        childWindow = null;
        Refresh();
        parentMask = OwnMask(memoryWindow);
        Check(FrontIs(stateWindow, memoryWindow) && parentMask != null
            && parentMask.transform.GetSiblingIndex() == memoryWindow.WindowRect.childCount - 1,
            "removing the dynamic child leaves one parent mask above the remaining parent content");
        dimming.enabled = false;
        Check(dimming.FrontmostWindow == null && !dimming.IsDimmed(memoryWindow) && !dimming.IsDimmed(stateWindow),
            "disabling the presentation component hides all of its masks");
        dimming.enabled = true;
        Refresh();
        Check(FrontIs(stateWindow, memoryWindow) && OwnMask(memoryWindow) != null && OwnMask(stateWindow) != null,
            "reenabling reconstructs the correct activity state without duplicating masks");

        yield return RunChapter03Message();
    }

    static IEnumerator RunPrologueFault()
    {
        context = "PrologueFault popup activity";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.PrologueFault),
            "isolated prologue fault checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "fault checkpoint ready", 80f);
        os = OS_Handle.Instance;
        dimming = os.GetComponent<OSWindowActivityDimming>();
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession,
            "fault checkpoint succeeds with formal writes disabled");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(os.OpenStateCheck(1f), "fault StateCheck opens through its real launcher");
        stateWindow = os.StateCheckWindowController.GetComponent<OSWindowUnit>();
        yield return Wait(() => Ready(stateWindow) && os.StateCheckWindowController.IsGameplayEnabled
            && !os.StateCheckWindowController.IsFaultGuidePlaying, "fault board is ready for player input");
        var pool = os.ErrorWindowPoolController;
        for (int i = 0; i < 3; i++)
        {
            Check(pool.OpenNextWindow(), "fault popup " + i + " spawns above the desktop");
            yield return null;
            Refresh();
            Check(dimming.FrontmostWindow == stateWindow && !dimming.IsDimmed(stateWindow),
                "fault popup " + i + " does not dim the active repair board");
            Check(!pool.GetComponentsInChildren<OSWindowUnit>(false).Any(window => window.IsOpen && dimming.IsDimmed(window)),
                "fault popup effects are excluded from activity overlays");
        }
        Check(pool.IsRunning && Mathf.Approximately(GameDataManager.Instance.CurrentStateCheckProgress, 1f),
            "brightness repair preserves the running fault effect and puzzle progress");
        yield return Screenshot("00-prologue-fault-board");
    }

    static IEnumerator RunChapter03Message()
    {
        context = "real Chapter03MemoryStart fixture";
        os = null; dimming = null; memoryWindow = null; stateWindow = null; toolWindow = null;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart),
            "isolated real Chapter 3 opening checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "Chapter 3 checkpoint ready", 80f);
        os = OS_Handle.Instance;
        dimming = os.GetComponent<OSWindowActivityDimming>();
        var memory = os.MemoryWindowController;
        var story = os.Chapter03Story;
        var messages = os.LinkMessageController;
        memoryWindow = memory.GetComponent<OSWindowUnit>();
        stateWindow = os.StateCheckWindowController.GetComponent<OSWindowUnit>();
        toolWindow = os.ToolWindowController.GetComponent<OSWindowUnit>();
        var liveWindow = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        var messageWindow = messages.LinkWindow;
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        messages.SetGlobalMessagesSuppressed(false);
        messages.NotifyOSBecameVisible();
        yield return Wait(() => Ready(memoryWindow) && Ready(liveWindow) && story.IsAvailable
            && memory.CurrentData?.DataIndex == 8000, "real Chapter 3 Memory starts at 8000");
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession
            && GameDataManager.Instance.CurrentChapterIndex == 3 && GameDataManager.Instance.IsMemoryRepair04Installed
            && !story.State.homeOpened && !messageWindow.IsOpen && dimming != null,
            "Chapter 3 uses its fresh opening story, installed upgrade and isolated save");
        Check(messageWindow.transform.IsChildOf(liveWindow.transform), "authored Message is a child of Live");
        Check((bool)Invoke(os, "OpenStateCheckWindow") && os.OpenToolWindow(),
            "Chapter 3 opens real ordinary StateCheck and Tool windows behind Memory");
        yield return Wait(() => Ready(stateWindow) && Ready(toolWindow), "Chapter 3 ordinary windows finish opening");
        memoryWindow.SetAlwaysOnTop(false); stateWindow.SetAlwaysOnTop(false); toolWindow.SetAlwaysOnTop(false);
        liveWindow.SetAlwaysOnTop(false);
        messageWindow.SetAlwaysOnTop(false);
        os.NotifyWindowPointerPressed(memoryWindow);
        Refresh();
        Check(FrontIs(memoryWindow, stateWindow) && dimming.IsDimmed(toolWindow),
            "Chapter 3 Memory starts as ordinary foreground with StateCheck and Tool dimmed");
        RecordChapter03("before-message", liveWindow, messageWindow);
        int memorySibling = memoryWindow.transform.GetSiblingIndex();
        int stateSibling = stateWindow.transform.GetSiblingIndex();
        var mouseFocus = os.MouseActiveWindow;

        context = "real Chapter 3 opening message and temporary foreground lift";
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!(memory.CurrentData?.DataIndex == 8020 && memory.IsCurrentTextComplete)
            && Time.realtimeSinceStartup < deadline)
        {
            if (memory.CurrentData?.DataIndex > 8020) throw new InvalidOperationException("Opening advanced past the voice trigger");
            memory.TryAdvance();
            yield return null;
        }
        Check(memory.CurrentData?.DataIndex == 8020 && memory.IsCurrentTextComplete,
            "ordinary Memory advances through 8000 and 8010 to the real 8020 voice trigger");
        string voice = OSChapter03StoryDirector.LiveId("live.opening.voice");
        yield return Wait(() => messages.DisplayedEntryId == voice && Ready(messageWindow)
            && HasCanvasLift(liveWindow) && HasCanvasLift(messageWindow),
            "real opening voice raises Live and Message through their production Canvas lift");
        Refresh();
        Check(!liveWindow.IsAlwaysOnTop && !messageWindow.IsAlwaysOnTop
            && dimming.FrontmostWindow == memoryWindow && !dimming.IsDimmed(memoryWindow)
            && !dimming.IsDimmed(liveWindow) && !dimming.IsDimmed(messageWindow)
            && dimming.IsDimmed(stateWindow) && dimming.IsDimmed(toolWindow),
            "unpinned Live and Message temporary foreground stays bright without dimming active Memory");
        Check(os.MouseActiveWindow == mouseFocus && mouseFocus == memoryWindow
            && (memoryWindow.transform.GetSiblingIndex() < stateWindow.transform.GetSiblingIndex()) == (memorySibling < stateSibling),
            "opening Message preserves ordinary order and the player's Memory focus");
        RecordChapter03("temporary-message-foreground", liveWindow, messageWindow);
        yield return Screenshot("05-chapter03-message-foreground");

        context = "pinned Live and Message remain outside ordinary activity";
        liveWindow.SetAlwaysOnTop(true);
        Refresh();
        Check(dimming.FrontmostWindow == memoryWindow && !dimming.IsDimmed(memoryWindow)
            && !dimming.IsDimmed(liveWindow) && !dimming.IsDimmed(messageWindow)
            && dimming.IsDimmed(stateWindow) && dimming.IsDimmed(toolWindow),
            "pinning Live exempts Live and Message while ordinary Memory remains active");
        liveWindow.SetAlwaysOnTop(false);
        // Unpinning leaves Live's physical sibling order raised. The message's
        // active Canvas lift must still exclude that whole group from selection.
        messageWindow.SetAlwaysOnTop(true);
        Refresh();
        Check(dimming.FrontmostWindow == memoryWindow && !dimming.IsDimmed(memoryWindow)
            && !dimming.IsDimmed(liveWindow) && !dimming.IsDimmed(messageWindow)
            && dimming.IsDimmed(stateWindow) && dimming.IsDimmed(toolWindow),
            "pinning the Message child exempts its Live group without taking ordinary activity");
        messageWindow.SetAlwaysOnTop(false);
        Refresh();
        Check((memoryWindow.transform.GetSiblingIndex() < stateWindow.transform.GetSiblingIndex())
                == (memorySibling < stateSibling) && os.MouseActiveWindow == memoryWindow,
            "Live and Message pin changes preserve ordinary relative order and pointer focus");

        context = "ordinary window activation while Message is in front";
        os.NotifyWindowPointerPressed(stateWindow);
        Refresh();
        Check(FrontIs(stateWindow, memoryWindow) && dimming.IsDimmed(toolWindow)
            && !dimming.IsDimmed(liveWindow) && !dimming.IsDimmed(messageWindow)
            && os.MouseActiveWindow == stateWindow,
            "ordinary StateCheck activation dims Memory while Live and Message remain exempt");
        os.NotifyWindowPointerPressed(toolWindow);
        Refresh();
        Check(FrontIs(toolWindow, memoryWindow) && dimming.IsDimmed(stateWindow)
            && !dimming.IsDimmed(liveWindow) && !dimming.IsDimmed(messageWindow),
            "ordinary Tool activation updates only ordinary background dimming during Message");
        os.NotifyWindowPointerPressed(memoryWindow);
        Refresh();
        Check(FrontIs(memoryWindow, stateWindow) && dimming.IsDimmed(toolWindow)
            && !dimming.IsDimmed(liveWindow) && !dimming.IsDimmed(messageWindow),
            "Memory can regain ordinary activity while the Message stays visually in front");
        RecordChapter03("ordinary-switching-during-message", liveWindow, messageWindow);
        yield return Screenshot("06-chapter03-ordinary-memory-active");

        context = "Message natural close and foreground restoration";
        yield return Wait(() => !messageWindow.IsOpen && story.State.liveQueue.Count == 0,
            "opening Message finishes its real reading hold and closes naturally", 20f);
        yield return null;
        Refresh();
        Check(!HasCanvasLift(liveWindow) && FrontIs(memoryWindow, stateWindow) && dimming.IsDimmed(toolWindow)
            && dimming.IsDimmed(liveWindow) && !dimming.IsDimmed(messageWindow),
            "Message closure releases its foreground lift and restores ordinary background Live dimming");
        Check(os.MouseActiveWindow == memoryWindow && memory.CurrentData?.DataIndex == 8020
            && !story.State.homeOpened,
            "Message closure preserves ordinary focus and does not skip the remaining Chapter 3 opening");
        RecordChapter03("message-closed", liveWindow, messageWindow);
        yield return Screenshot("07-chapter03-message-closed");
    }

    static bool HasCanvasLift(OSWindowUnit window)
    {
        var canvas = window != null ? window.GetComponent<Canvas>() : null;
        var parent = window?.transform.parent?.GetComponentInParent<Canvas>();
        return canvas != null && canvas.isActiveAndEnabled && canvas.overrideSorting && parent != null
            && (SortingLayer.GetLayerValueFromID(canvas.sortingLayerID) > SortingLayer.GetLayerValueFromID(parent.sortingLayerID)
                || canvas.sortingLayerID == parent.sortingLayerID && canvas.sortingOrder > parent.sortingOrder);
    }

    static void RecordChapter03(string stage, OSWindowUnit live, OSWindowUnit message)
    {
        observations.Add(new { stage, frame = Time.frameCount, front = dimming.FrontmostWindow?.WindowId,
            focus = os.MouseActiveWindow?.WindowId, memory = WindowState(memoryWindow), state = WindowState(stateWindow),
            tool = WindowState(toolWindow), live = WindowState(live), message = WindowState(message),
            liveLifted = HasCanvasLift(live), messageLifted = HasCanvasLift(message),
            entry = os.LinkMessageController.DisplayedEntryId });
    }

    static void Focus()
    {
        if (gameView != null && EditorWindow.focusedWindow != gameView) gameView.Focus();
        testMouse?.MakeCurrent();
        os?.Chapter03Story?.DebugSetFocus(true);
        os?.Chapter03Entry?.DebugSetFocus(true);
    }

    static void CreateChildWindow()
    {
        childBranch = new GameObject("WindowDimmingQA-ChildBranch", typeof(RectTransform));
        childBranch.layer = memoryWindow.gameObject.layer;
        var branch = (RectTransform)childBranch.transform;
        branch.SetParent(memoryWindow.WindowRect, false);
        branch.anchorMin = Vector2.zero;
        branch.anchorMax = Vector2.one;
        branch.offsetMin = Vector2.zero;
        branch.offsetMax = Vector2.zero;
        var child = new GameObject("WindowDimmingQA-Child", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(OSWindowUnit));
        child.layer = memoryWindow.gameObject.layer;
        child.transform.SetParent(branch, false);
        child.GetComponent<Image>().color = new Color(.72f, .8f, .9f, 1f);
        childWindow = child.GetComponent<OSWindowUnit>();
        Set(childWindow, "animateOnOpen", false);
        Set(childWindow, "minimumSize", new Vector2(90f, 50f));
        childWindow.SetOpeningLayout(new Vector2(30f, -40f), new Vector2(140f, 80f));
        childWindow.ConfigureInteraction(false, false);
        var content = new GameObject("VisibleChildContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        content.layer = child.layer;
        var contentRect = (RectTransform)content.transform;
        contentRect.SetParent(child.transform, false);
        contentRect.sizeDelta = new Vector2(100f, 30f);
        content.GetComponent<Image>().color = Color.white;
        content.GetComponent<Image>().raycastTarget = false;
        childWindow.OpenStandalone(0f);
    }

    static void LiftWithCanvas(OSWindowUnit target = null)
    {
        if (target == null) target = memoryWindow;
        Canvas inherited = target.GetComponentInParent<Canvas>();
        int layer = inherited != null ? inherited.sortingLayerID : 0;
        int highest = os.GetComponentsInChildren<Canvas>(false)
            .Where(canvas => canvas.isActiveAndEnabled && canvas.sortingLayerID == layer)
            .Select(canvas => canvas.sortingOrder).DefaultIfEmpty(0).Max();
        liftedCanvas = target.GetComponent<Canvas>();
        ownsLiftedCanvas = liftedCanvas == null;
        if (ownsLiftedCanvas) liftedCanvas = target.gameObject.AddComponent<Canvas>();
        previousCanvasEnabled = liftedCanvas.enabled;
        previousCanvasOverride = liftedCanvas.overrideSorting;
        previousCanvasLayer = liftedCanvas.sortingLayerID;
        previousCanvasOrder = liftedCanvas.sortingOrder;
        liftedCanvas.enabled = true;
        liftedCanvas.overrideSorting = true;
        liftedCanvas.sortingLayerID = layer;
        liftedCanvas.sortingOrder = Mathf.Min(32767, highest + 20);
    }

    static void RestoreCanvas()
    {
        if (liftedCanvas == null) return;
        if (ownsLiftedCanvas) UnityEngine.Object.DestroyImmediate(liftedCanvas);
        else
        {
            liftedCanvas.overrideSorting = previousCanvasOverride;
            liftedCanvas.sortingLayerID = previousCanvasLayer;
            liftedCanvas.sortingOrder = previousCanvasOrder;
            liftedCanvas.enabled = previousCanvasEnabled;
        }
        liftedCanvas = null;
        ownsLiftedCanvas = false;
    }

    static Image OwnMask(OSWindowUnit window)
    {
        if (window == null) return null;
        var masks = window.WindowRect.GetComponentsInChildren<Image>(true)
            .Where(image => image.name == OverlayName && image.transform.parent == window.WindowRect).ToArray();
        return masks.Length == 1 ? masks[0] : null;
    }

    static void CheckMask(OSWindowUnit window, string label)
    {
        var mask = OwnMask(window);
        var rect = mask != null ? mask.rectTransform : null;
        Check(mask != null && mask.color == new Color(0f, 0f, 0f, .75f) && !mask.raycastTarget
            && rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one
            && rect.offsetMin.sqrMagnitude < .0001f && rect.offsetMax.sqrMagnitude < .0001f, label);
    }

    static bool FrontIs(OSWindowUnit front, OSWindowUnit back) => dimming.FrontmostWindow == front
        && !dimming.IsDimmed(front) && dimming.IsDimmed(back);
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing;
    static void Refresh() { Canvas.ForceUpdateCanvases(); dimming.RefreshPresentation(); }
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));

    static IEnumerator Screenshot(string name)
    {
        string path = Path.Combine(OutputDirectory, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        yield return null;
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 0, "screenshot " + name, 10f, false);
    }

    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f, bool record = true)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { testMouse?.MakeCurrent(); yield return null; }
        if (!predicate()) throw new TimeoutException(label);
        if (record) Check(true, label);
    }

    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        if (!checks.Contains(label)) checks.Add(label);
    }

    static string Describe() => "context=" + context + ", front=" + dimming?.FrontmostWindow?.WindowId
        + ", mouseFocus=" + os?.MouseActiveWindow?.WindowId + ", memory=" + WindowState(memoryWindow)
        + ", state=" + WindowState(stateWindow) + ", tool=" + WindowState(toolWindow) + ", child=" + WindowState(childWindow);
    static string WindowState(OSWindowUnit window) => window == null ? "null" : window.WindowId + "[open=" + window.IsOpen
        + ", minimized=" + window.IsMinimized + ", active=" + window.gameObject.activeInHierarchy
        + ", pinned=" + window.IsAlwaysOnTop + ", dim=" + dimming?.IsDimmed(window)
        + ", sibling=" + window.transform.GetSiblingIndex() + "]";
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static object Invoke(object target, string name, object[] arguments = null)
    {
        var method = target?.GetType().GetMethod(name, Fields);
        if (method == null) throw new MissingMethodException(target?.GetType().Name, name);
        return method.Invoke(target, arguments);
    }
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
