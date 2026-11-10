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
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// Opt-in Play Mode integration checks. Fixtures and any save writes stay in Temp.
public static class OSChapter03HomeQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSWindowUnit memoryWindow;
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
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear();
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03HomeQA", Guid.NewGuid().ToString("N")));
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
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03HomeQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            testMouse.MakeCurrent();
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
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + error;
                    Debug.LogError("[Chapter03 Home QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " checks";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                home?.Suspend();
                InvokeOptional(os?.Chapter03Entry, "ResetPresentation");
                os?.Chapter02FinalTalk?.ResetPresentation();
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
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo)
                    throw new InvalidOperationException("Original memory/language/demo mode was not restored");
                if (hashes != null && !paths.Select(Hash).SequenceEqual(hashes))
                    throw new IOException("Original save files changed");
                checks.Add("cleanup restored original save object, language, mode, resources, mouse, and file hashes");
            }
            catch (Exception cleanup) { Status = "FAILED during cleanup: " + cleanup + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                status = Status, context, checks, originalFiles = paths, originalHashes = hashes,
                finalHashes = paths.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Exit Play Mode after this isolated run. No full Chapter 3 entry replay is performed."
            }, Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Home QA] " + Status + " | " + OutputDirectory);
            os = null; memory = null; home = null; memoryWindow = null; setup = null; testMouse = null;
        }
    }

    static IEnumerator Run()
    {
        context = "Chapter03DemoEnd fixture";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03DemoEnd), "Demo-end checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession,
            "checkpoint succeeds with formal-save writes disabled");
        os = OS_Handle.Instance;
        memory = os.MemoryWindowController;
        memoryWindow = memory != null ? memory.GetComponent<OSWindowUnit>() : null;
        home = memory != null ? memory.GetComponent<OSChapter03HomeController>() : null;
        Check(memory != null && memoryWindow != null && home != null && home.View != null && home.HomeWindow != null,
            "authored Home controller, view, and child window resolve");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        var data = GameDataManager.Instance;
        var fixture = Clone(data.SaveData);
        fixture.flow.language = LanguageState.zh_CN;
        fixture.osProcess.currentChapterIndex = 3;
        fixture.osProcess.memory.currentDataIndex = 7460;
        fixture.osProcess.memory.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Complete;
        fixture.osProcess.memory.chapter03EntryStep = (int)OSChapter03EntryDirector.EntryStep.AwaitMemory;
        fixture.osProcess.memory.chapter03EntryInstallAnswer = 1;
        fixture.osProcess.memory.chapter03HomeInitialized = false;
        fixture.osProcess.memory.chapter03HomeView = default;
        fixture.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        fixture.osResume.captured = false;
        foreach (string id in new[] { "memory-repair-1", "memory-repair-2", "memory-repair-3", "memory-repair-4" })
            if (!fixture.osProcess.installedToolProgramIds.Contains(id)) fixture.osProcess.installedToolProgramIds.Add(id);
        InvokeOptional(os.Chapter03Entry, "ResetPresentation");
        os.Chapter02FinalTalk.ResetPresentation();
        os.LinkMessageController.NotifyOSHidden();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true))
            if (window.IsOpen) window.CloseSilently();
        memory.PrepareRuntime();
        data.ReplaceSaveData(fixture);
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        os.LinkMessageController.NotifyOSBecameVisible();
        Focus();
        os.Chapter03Entry.DebugStep();
        yield return null;
        Check(data.CurrentOSMemoryDataIndex == 7460 && data.IsMemoryRepair04Installed
            && os.Chapter03Entry.Step == OSChapter03EntryDirector.EntryStep.AwaitMemory,
            "installed-upgrade fixture still waits outside Chapter 3 memory");
        var ordinary = JsonUtility.FromJson<OSWindowLayoutSaveData>(JsonUtility.ToJson(memoryWindow.CaptureLayout()));

        context = "real memory launch and paired layout";
        Check(os.OpenMemoryWindow(), "real OpenMemoryWindow launches Chapter 3");
        Check(data.CurrentOSMemoryDataIndex == 8000
            && os.Chapter03Entry.Step == OSChapter03EntryDirector.EntryStep.Complete,
            "launch enters 8000 and finishes the entry handoff");
        yield return Wait(() => memoryWindow.IsOpen && !memoryWindow.IsOpening
            && home.HomeWindow.IsOpen && !home.HomeWindow.IsOpening, "both opening animations finish");
        Canvas.ForceUpdateCanvases();
        Check(memoryWindow.HasTemporaryOpeningLayout
            && Near(memoryWindow.WindowRect.rect.size, new Vector2(175f, 280f))
            && Near(memoryWindow.WindowRect.anchoredPosition, new Vector2(-222f, 12f)),
            "Memory uses the narrow left-side temporary layout");
        Check(Near(((RectTransform)home.View.transform).rect.size, new Vector2(240f, 160f))
            && !home.HomeWindow.AllowsMove && !home.HomeWindow.AllowsResize,
            "Home keeps its 240x160 view with fixed child-window interaction");
        Rect left = ScreenRect(memoryWindow.WindowRect), right = ScreenRect(home.HomeWindow.WindowRect);
        Check(right.xMin >= left.xMax - 1f, "Home appears to the right without covering the Memory text window");
        Check(!home.View.InputEnabled, "Home cannot walk while opening paragraphs still require reading");
        Check(home.View.CurrentRoom?.id == "study" && home.View.PlayerTile == new Vector2Int(5, 7)
            && home.View.DongTile == new Vector2Int(6, 7), "first entry starts in the authored study positions");
        context = "fresh dynamic font glyph regression";
        CheckDynamicGlyphs();
        yield return Screenshot("01-opening-reading");

        context = "read through the current Chapter 3 endpoint";
        int advances = 0;
        while (memory.CurrentData == null || memory.CurrentData.DataIndex != 8110 || !memory.IsCurrentTextComplete)
        {
            if (++advances > 40) throw new InvalidOperationException("Reading did not reach completed node 8110");
            Check(memory.CurrentData != null, "opening paragraph initializes");
            FocusMemory();
            Check(memory.TryAdvance(), "production reading input completes or advances the current paragraph");
            yield return null;
            if (memory.CurrentData.DataIndex != 8110 || !memory.IsCurrentTextComplete)
                Check(!home.View.InputEnabled, "Home stays locked before the completed free-walk endpoint");
        }
        yield return Wait(() => home.View.InputEnabled, "Home enables after full text at 8110");
        Check(!memory.CanContinue && memory.CurrentData.Options.Count == 0,
            "free walking starts only when no continuation or choice is pending");

        context = "continuous pointer and scripted walking";
        int stepped = 0;
        Action<Vector2Int, bool> countStep = (tile, water) => stepped++;
        home.View.Stepped += countStep;
        try
        {
            ClickTile(new Vector2Int(9, 7));
            Check(home.View.IsWalking, "production Home pointer click creates a route");
            yield return Wait(() => !home.View.IsWalking && home.View.PlayerTile == new Vector2Int(9, 7),
                "pointer route reaches its destination across several frames", 8f);
            Check(stepped >= 4, "pointer route completes all four tiles without per-frame interruption");
        }
        finally { if (home != null && home.View != null) home.View.Stepped -= countStep; }
        bool arrived = false;
        Check(home.View.WalkTo(new Vector2Int(5, 7), () => arrived = true), "scripted return route is accepted");
        yield return Wait(() => arrived && !home.View.IsWalking, "WalkTo invokes completion after its whole route", 8f);
        Check(home.View.PlayerTile == new Vector2Int(5, 7), "scripted walking reaches the requested tile");
        ClickTile(new Vector2Int(3, 9));
        yield return Wait(() => home.View.CurrentRoom?.id == "west" && !home.View.IsTransitioning,
            "the study door performs the existing frontend room transition", 8f);
        Check(data.OSProcessData.memory.chapter03HomeView.room == "west", "room entry persists the new room");
        Check(home.View.WalkTo(new Vector2Int(6, 4)), "corridor route is accepted");
        yield return Wait(() => !home.View.IsWalking && home.View.PlayerTile == new Vector2Int(6, 4),
            "corridor walk finishes", 8f);
        var expected = home.View.CaptureState();
        Check(Same(expected, data.OSProcessData.memory.chapter03HomeView), "completed steps persist room and both positions");
        yield return Screenshot("02-walked-to-corridor");

        context = "close and reopen";
        memoryWindow.Close();
        Check(!memoryWindow.IsOpen && !home.HomeWindow.IsOpen && !home.View.InputEnabled,
            "closing Memory closes and disables its Home child");
        Check(Same(expected, data.OSProcessData.memory.chapter03HomeView), "closing preserves the last room and positions");
        Check(os.OpenMemoryWindow(), "Memory reopens through its real launcher API");
        yield return Wait(() => home.View.InputEnabled && !home.HomeWindow.IsOpening, "reopened Home becomes usable");
        Check(Same(expected, home.View.CaptureState()) && data.CurrentOSMemoryDataIndex == 8110,
            "reopen restores the room without restarting text or walking");

        context = "minimize and restore";
        memoryWindow.Minimize();
        Check(memoryWindow.IsMinimized && !home.HomeWindow.IsOpen && !home.View.InputEnabled,
            "minimizing Memory suspends Home");
        memoryWindow.RestoreAndBringToFront();
        yield return Wait(() => home.View.InputEnabled, "restoring Memory reopens Home without another parent open event");
        Check(Same(expected, home.View.CaptureState()), "minimize and restore preserve both character positions");

        context = "JSON and ReplaceSaveData restoration";
        home.CaptureViewState();
        OSSessionPersistence.Capture(data.SaveData);
        GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-home.json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        Check(Same(expected, restored.osProcess.memory.chapter03HomeView), "JSON roundtrip preserves room and positions");
        home.View.Teleport(new Vector2Int(8, 4));
        home.CaptureViewState();
        Check(!Same(expected, home.View.CaptureState()), "old runtime differs from the saved restore target");
        os.LinkMessageController.NotifyOSHidden();
        data.ReplaceSaveData(restored);
        home.Suspend();
        Check(Same(expected, restored.osProcess.memory.chapter03HomeView),
            "old-session Suspend cannot overwrite the replacement save");
        yield return OSSessionPersistence.Restore(restored);
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        os.LinkMessageController.NotifyOSBecameVisible();
        yield return Wait(() => home.View.InputEnabled && !memoryWindow.IsOpening, "session restoration reconstructs usable Home");
        Check(ReferenceEquals(data.SaveData, restored) && Same(expected, home.View.CaptureState()),
            "restored live view matches the replacement save instead of the old runtime");
        Check(memoryWindow.HasTemporaryOpeningLayout && !home.View.IsWalking && !home.View.IsTransitioning,
            "restored Home has the paired layout and no stale route or doorway fade");
        yield return Screenshot("03-restored-home");

        context = "leave Chapter 3";
        var other = Clone(data.SaveData);
        other.osProcess.currentChapterIndex = 2;
        other.osProcess.memory.currentDataIndex = 7460;
        other.osProcess.memory.chapter03EntryStep = (int)OSChapter03EntryDirector.EntryStep.Inactive;
        other.osProcess.memory.interactiveSceneDataIndex = OSMemoryProcessSaveData.InvalidDataIndex;
        data.ReplaceSaveData(other);
        memory.InitializeFromSave();
        yield return null;
        Check(!data.IsChapter03HomeActive && !home.HomeWindow.IsOpen && !home.View.InputEnabled,
            "another chapter closes and disables Home");
        Check(!memoryWindow.HasTemporaryOpeningLayout && Near(memoryWindow.WindowRect.rect.size, ordinary.size)
            && Near(memoryWindow.WindowRect.anchoredPosition, ordinary.anchoredPosition),
            "leaving Chapter 3 restores the original ordinary Memory layout");

        context = "Chapter 1 interactive-picture compatibility";
        var picture = memory.GetComponentInChildren<OSMemoryPicController>(true);
        Check(picture != null && picture.scenario != null && picture.pictureWindow != null,
            "original Chapter 1 picture and scenario references remain available");
        var chapterOne = Clone(data.SaveData);
        chapterOne.osProcess.currentChapterIndex = 1;
        chapterOne.osProcess.chapter01Ending = new OSChapter01EndingSaveData();
        chapterOne.osProcess.memory.currentDataIndex = picture.scenario.entryMemoryIndex;
        chapterOne.osProcess.memory.interactiveSceneDataIndex = picture.scenario.entryMemoryIndex;
        chapterOne.osProcess.memory.pictureScenario = new OSMemoryPicSaveData {
            scenarioId = picture.scenario.scenarioId, currentSceneId = picture.scenario.firstSceneId,
            initialized = true, exited = false
        };
        chapterOne.osProcess.memory.pictureScenario.inventory.AddRange(picture.scenario.initialItems);
        chapterOne.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        data.ReplaceSaveData(chapterOne);
        memory.InitializeFromSave();
        yield return Wait(() => picture.pictureWindow.IsOpen && !picture.pictureWindow.IsOpening
            && picture.CurrentScene != null, "Chapter 1 restores its existing interactive picture");
        Check(data.UsesMemoryInteractiveLayout && memoryWindow.HasTemporaryOpeningLayout
            && Near(memoryWindow.WindowRect.rect.size, new Vector2(175f, 280f))
            && Near(memoryWindow.WindowRect.anchoredPosition, new Vector2(-222f, 12f)),
            "Chapter 1 retains its original narrow left-side layout");
        Check(picture.CurrentScene.sceneId == picture.scenario.firstSceneId
            && !home.HomeWindow.IsOpen && !home.View.InputEnabled,
            "Chapter 1 shows its old Pic scene with Chapter 3 Home closed");
        yield return Screenshot("04-chapter1-compatibility");

        context = "Chapter 2 breakfast-picture compatibility";
        var breakfast = Clone(data.SaveData);
        breakfast.osProcess.currentChapterIndex = 2;
        breakfast.osProcess.memory.currentDataIndex = 4320;
        breakfast.osProcess.memory.interactiveSceneDataIndex = OSMemoryProcessSaveData.InvalidDataIndex;
        data.ReplaceSaveData(breakfast);
        memory.InitializeFromSave();
        var illustrations = memory.GetComponent<OSMemorySubwindowPlayer>();
        Check(illustrations != null, "Chapter 2 illustration player remains available");
        Check(data.TryGetOSMemoryData(4320, out var breakfastData)
            && breakfastData.SubwindowSequence != null, "Chapter 2 breakfast illustration remains authored");
        yield return Wait(() => illustrations.IsPersistentDisplay && illustrations.CurrentContent != null
            && illustrations.CurrentSequence == breakfastData.SubwindowSequence,
            "Chapter 2 restores its existing breakfast illustration at 4320");
        Check(memoryWindow.HasTemporaryOpeningLayout
            && Near(memoryWindow.WindowRect.rect.size, new Vector2(340f, 280f))
            && Near(memoryWindow.WindowRect.anchoredPosition, new Vector2(-88f, 12f)),
            "Chapter 2 retains its original breakfast illustration layout");
        Check(!picture.pictureWindow.IsOpen && !home.HomeWindow.IsOpen && !home.View.InputEnabled,
            "Chapter 2 illustration closes both interactive Home windows");
        yield return Screenshot("05-chapter2-compatibility");
    }

    static void CheckDynamicGlyphs()
    {
        TMP_Text label = memory.TextUnits.FirstOrDefault(unit => unit != null && unit.Label != null)?.Label;
        Font source = label != null && label.font != null ? label.font.sourceFontFile : null;
        Check(source != null, "current Memory text exposes the real source font for a fresh atlas");
        TMP_FontAsset font = null;
        try
        {
            font = TMP_FontAsset.CreateFontAsset(source, 32, 5, GlyphRenderMode.SDFAA, 512, 512,
                AtlasPopulationMode.Dynamic, true);
            Check(font != null, "fresh dynamic TMP font is created without an asset file");
            font.name = "Chapter03HomeQA-FreshGlyphs";
            font.hideFlags = HideFlags.HideAndDontSave;
            var book = new OSHomeGlyphBook { Font = font, EmPixels = 13f, Coverage = .34f };
            var originalBits = new Dictionary<string, bool[]>();
            // Keep the CPU atlas cache alive across all four first-time additions.
            foreach (string glyph in new[] { "我", "咚", "镜", "柜" })
            {
                var word = book.Layout(glyph, 1);
                Check(font.characterLookupTable.ContainsKey(glyph[0]), glyph + " resolves to a real source-font glyph");
                Check(word.bits.Any(bit => bit), glyph + " is visible on its first dynamic-atlas render");
                originalBits[glyph] = (bool[])word.bits.Clone();
            }
            book.Clear();
            foreach (var pair in originalBits)
                Check(pair.Value.SequenceEqual(book.Layout(pair.Key, 1).bits),
                    pair.Key + " has identical bits before and after Clear");

            const string externalGlyph = "雨";
            Check(!font.characterLookupTable.ContainsKey(externalGlyph[0]),
                "external TMP addition starts with a missing character");
            int oldGlyphCount = font.glyphTable.Count;
            Check(font.TryAddCharacters(externalGlyph) && font.glyphTable.Count > oldGlyphCount,
                "external TMP API grows the already-cached font atlas");
            bool[] externallyAdded = (bool[])book.Layout(externalGlyph, 1).bits.Clone();
            Check(externallyAdded.Any(bit => bit), "externally added glyph renders without manually clearing the Home cache");
            book.Clear();
            Check(externallyAdded.SequenceEqual(book.Layout(externalGlyph, 1).bits),
                "externally added glyph matches a clean atlas read");
        }
        finally
        {
            // TMP destroys its atlas/material from OnDestroy. Retain the created
            // references and explicitly remove any remaining runtime objects.
            Texture2D[] textures = font != null ? font.atlasTextures.ToArray() : Array.Empty<Texture2D>();
            Material material = font != null ? font.material : null;
            if (font != null) UnityEngine.Object.DestroyImmediate(font);
            foreach (var texture in textures)
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    static void Focus()
    {
        testMouse?.MakeCurrent();
        if (os == null) return;
        os.Chapter03Entry?.DebugSetFocus(true);
        os.Chapter02FinalTalk?.DebugSetFocus(true);
        SetIfPresent(os.Chapter02FinalTalk?.Nightfall, "hasFocus", true);
        SetIfPresent(os.CursorRelease, "hasFocus", true);
    }

    static void FocusMemory()
    {
        Focus();
        memoryWindow.BringToFront();
        os.NotifyTaskbarProcessPressed(memoryWindow);
        EventSystem.current?.SetSelectedGameObject(null);
    }

    static void ClickTile(Vector2Int tile)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)home.View.transform;
        Vector2 local = new Vector2(rect.rect.xMin + (tile.x + .5f) * OSHomeLayout.Tile,
            rect.rect.yMax - (tile.y + .5f) * OSHomeLayout.Tile);
        var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local));
        var raycast = new RaycastResult {
            gameObject = home.View.gameObject, module = rect.GetComponentInParent<GraphicRaycaster>(), screenPosition = screen
        };
        var pointer = new PointerEventData(EventSystem.current) {
            button = PointerEventData.InputButton.Left, pointerId = -1, clickCount = 1, position = screen,
            pointerPressRaycast = raycast, pointerCurrentRaycast = raycast
        };
        home.View.OnPointerDown(pointer);
        home.View.OnPointerClick(pointer);
    }

    static Rect ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }

    static bool Same(OSHomeView.State a, OSHomeView.State b) => a.room == b.room && a.me == b.me
        && a.dong == b.dong && a.dongHidden == b.dongHidden;
    static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1f;
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static IEnumerator Screenshot(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, name + ".png"));
        yield return null;
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { Focus(); yield return null; }
        Check(predicate(), label);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        if (!checks.Contains(label)) checks.Add(label);
    }
    static string Describe() => "context=" + context + ", node=" + GameDataManager.Instance?.CurrentOSMemoryDataIndex
        + ", chapter=" + GameDataManager.Instance?.CurrentChapterIndex + ", memoryOpen=" + memoryWindow?.IsOpen
        + ", homeOpen=" + home?.HomeWindow?.IsOpen + ", input=" + home?.View?.InputEnabled
        + ", room=" + home?.View?.CurrentRoom?.id + ", tile=" + home?.View?.PlayerTile
        + ", walking=" + home?.View?.IsWalking + ", message=" + os?.LinkMessageController?.DisplayedEntryId;
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static void SetIfPresent(object target, string name, object value) => target?.GetType().GetField(name, Fields)?.SetValue(target, value);
    static void InvokeOptional(object target, string name) => target?.GetType().GetMethod(name, Fields)?.Invoke(target, null);
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
