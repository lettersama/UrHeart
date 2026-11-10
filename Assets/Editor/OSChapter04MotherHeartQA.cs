using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Opt-in Play Mode integration check of the production 39 -> key -> heart ->
/// 40 -> close -> 40a route. Only its starting save is positioned by the fixture.
/// Pointer actions use real raycasts/handlers; restores use JSON-cloned saves
/// and OSSessionPersistence. No production save, scene or authored asset is written.
/// </summary>
public static class OSChapter04MotherHeartQA
{
    const string Folder = "Tools/Chapter04MotherHeart/Validation";
    const string Prefix = OSChapter04StoryDirector.EntryPrefix;
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>(), errors = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly Dictionary<MonoBehaviour, bool> focusStates = new Dictionary<MonoBehaviour, bool>();
    static GameDataManager data;
    static GameAutosaveService saves;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04StoryDirector story, observedStory;
    static OSChapter04StoryContent.Beat heldBeat;
    static float oldAutoAdvance;
    static Mouse mouse;
    static Keyboard keyboard;
    static string context, lastLine, isolatedDirectory;
    static bool cancelled;
    static int revealTransitions, closeTransitions;
    static OSChapter04StoryState S => data?.OSProcessData.memory.chapter04Story;
    static OSLinkMessageController Link => os?.LinkMessageController;
    static OSChapter04GoldenKeyWindow Key => os?.MotherKeyController;
    static OSMotherHeartWindowController Heart => os?.MotherHeartController;
    static OSWindowUnit Live => os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
    static OSChapter04ChatView View => story?.GetComponent<OSChapter04ChatView>();
    public static string Status { get; private set; } = "idle";
    public static string CurrentStep => context;
    public static string OutputDirectory => Path.GetFullPath(Folder);
    public static void Cancel() => cancelled = true;

    [MenuItem("Tools/MrTGame/Chapter 4 Mother Heart/Run Play Mode QA")]
    static void StartMenu() => Debug.Log("[Chapter04 Mother Heart QA] " + Start());

    public static string Start()
    {
        if (Status == "running") return Status;
        saves = GameAutosaveService.Instance; data = GameDataManager.Instance;
        if (!Application.isPlaying || saves == null || data == null || GameTest.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null || SceneLoadManager.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || GameSessionFlow.Instance.IsBusy
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        setup = UnityEngine.Object.FindFirstObjectByType<GameSetup>();
        if (setup == null) return "Active GameSetup required";
        checks.Clear(); errors.Clear(); screenshots.Clear(); observations.Clear(); focusStates.Clear();
        os = null; story = observedStory = null; heldBeat = null; lastLine = null;
        cancelled = false; revealTransitions = closeTransitions = 0; context = "initialization";
        isolatedDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter04MotherHeartQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(isolatedDirectory); Directory.CreateDirectory(OutputDirectory);
        Status = "running"; data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        bool demo = GameSetup.IsDemo, background = Application.runInBackground;
        float volume = AudioListener.volume;
        var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        var random = UnityEngine.Random.state;
        string[] protectedFiles = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp",
            "Assets/Resources/Chapter04/story.json", "Assets/Data/OSCharacterInteractionData.asset", "Assets/Data/LocalizationData.asset" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles("Assets/Resources/Chapter04/FinalHeal/Heart", "heart_3_whole_idle*.png")).ToArray();
        string[] hashes = null;
        Exception failure = null;
        var work = new Stack<IEnumerator>();
        Application.logMessageReceived += CaptureError;
        try
        {
            try
            {
                saves.EnterTestSession(); hashes = protectedFiles.Select(Hash).ToArray();
                saves.UseIsolatedStorageForTests(isolatedDirectory); saves.EnterTestSession();
                data.ReplaceSaveData(Clone(original)); data.SetCurrentLanguage(LanguageState.zh_CN);
                Set(setup, "isDemo", false); Application.runInBackground = true; AudioListener.volume = 0f;
                if (resources != null) resources.enabled = false;
                mouse = InputSystem.AddDevice<Mouse>("MotherHeartQAMouse");
                keyboard = InputSystem.AddDevice<Keyboard>("MotherHeartQAKeyboard"); ReleaseInput();
                work.Push(Run());
            }
            catch (Exception error) { failure = error; }
            while (failure == null && work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancelled) throw new OperationCanceledException("Mother heart QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                ReleaseInput();
                if (observedStory != null) observedStory.ProgressChanged -= ObserveProgress;
                observedStory = null;
                if (heldBeat != null) heldBeat.autoAdvanceDelay = oldAutoAdvance;
                story?.DebugSetFocus(false); story?.ResetPresentation(); os?.Chapter04Entry?.ResetPresentation();
                Link?.NotifyOSHidden(); OSChapter04GriefPresentation.Current?.ClearAll();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                foreach (var pair in focusStates) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourceEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                Check(ReferenceEquals(data.SaveData, original) && GameSetup.IsDemo == demo,
                    "cleanup restores the original save object and Demo setting");
                Check(hashes != null && protectedFiles.Select(Hash).SequenceEqual(hashes),
                    "formal saves, scenes, dialogue assets and heart art remain unchanged");
            }
            catch (Exception error) { errors.Add("cleanup: " + error); }
            Application.logMessageReceived -= CaptureError;
            Status = failure == null && errors.Count == 0 ? "PASS: " + checks.Count + " checks" : "FAILED at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "playmode.json"), JsonConvert.SerializeObject(new {
                status = Status, time = DateTime.Now.ToString("s"), context, checks, errors,
                exception = failure?.ToString(), screenshots, observations, revealTransitions, closeTransitions,
                protectedFiles, originalHashes = hashes, finalHashes = protectedFiles.Select(Hash).ToArray(),
                note = "Production checkpoint and authored lines 39/40/40a; topmost EventSystem raycasts and production pointer handlers. "
                    + "KeyOffered, Unlocked, CloseRequested and open/minimized heart snapshots use JSON ReplaceSaveData plus OSSessionPersistence.Restore. "
                    + "Only the test start is positioned at 39; 40a auto-advance is held in memory and restored. Formal files are hash-protected; cleanup requests the main menu."
            }, Formatting.Indented));
            Debug.Log("[Chapter04 Mother Heart QA] " + Status + " | " + OutputDirectory);
        }
    }

    static IEnumerator Run()
    {
        context = "checkpoint";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "real chapter-four checkpoint accepts the jump");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint finishes", 80f);
        Bind();
        yield return Wait(() => story.IsAvailable && S.started && View?.OwnsLayout == true,
            "production Live conversation is available");
        Check(Key != null && Heart != null && os.MotherHeartDesktopIcon != null, "runtime gift windows and desktop target resolve");
        heldBeat = story.Content.Find(Prefix + "40a");
        Check(heldBeat != null, "the authored follow-up exists");
        oldAutoAdvance = heldBeat.autoAdvanceDelay; heldBeat.autoAdvanceDelay = -1f;

        context = "player window pin choices";
        foreach (var window in new[] { Live, os.TVWindow })
        {
            if (!window.IsAlwaysOnTop) continue;
            ClickWindowButton(window, OSWindowButton.WindowAction.ToggleAlwaysOnTop);
            yield return null;
            Check(!window.IsAlwaysOnTop, "the player's pin-button click unpins " + window.name);
        }

        // The shortcut retains the actual chapter setup, red companion and window
        // system. All events from the unfinished line 39 onward remain production.
        context = "line 39 / natural playback";
        story.ResetPresentation(); Link.NotifyOSHidden();
        var positioned = Clone(data.SaveData);
        positioned.osProcess.memory.chapter04Story = new OSChapter04StoryState {
            version = 3, started = true, currentEntryId = Prefix + "39", currentVisit = 39,
            theatreState = OSChapter04PresentationBridge.LabNight
        };
        positioned.osProcess.characterInteraction.presentingGlobalEntryId = null;
        positioned.osProcess.characterInteraction.pendingGlobalEntryId = null;
        foreach (string suffix in new[] { "39", "40", "40a" })
            positioned.osProcess.characterInteraction.SetGlobalEntryConsumed(Prefix + suffix, false);
        data.ReplaceSaveData(positioned); lastLine = S.currentEntryId;
        Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        yield return Wait(() => story.IsAvailable && Link.DisplayedEntryId == Prefix + "39" && Link.IsPresenting,
            "the actual unfinished 39 is being typed");
        Check(!S.currentTextCompleted && !Key.Window.IsOpen && !story.CanUseMotherKey,
            "the key is not offered before 39 finishes");
        Check(!story.TryContinue() && S.currentEntryId == Prefix + "39", "Continue cannot bypass unfinished 39");
        yield return Wait(() => S.currentTextCompleted && S.currentCueCommitted && S.currentResourcesCommitted,
            "natural typing commits line 39 and its resource receipt", 60f);
        yield return Wait(() => Key.Window.IsOpen && os.CursorRelease.OwnsScriptActor(story),
            "the red companion really owns the key-window entrance", 5f);
        Check(!story.CanUseMotherKey && !story.TryContinue(), "entrance blocks key input and the old Continue route");
        string keyOffered = Snapshot("key-offered-in-flight");
        Vector2 entrancePosition = Key.Window.WindowRect.anchoredPosition;
        yield return Hold(.15f);
        Check(Vector2.Distance(entrancePosition, Key.Window.WindowRect.anchoredPosition) > .5f,
            "the red companion moves the actual key window");
        yield return Wait(() => story.CanUseMotherKey && Ready(Key.Window), "the landed key becomes draggable", 8f);
        Check(!os.CursorRelease.OwnsScriptActor(story), "landing releases the red companion lease");
        Check(!story.TryContinue() && S.currentEntryId == Prefix + "39", "landed key still requires the player's discovery");
        yield return Shot("01-key-offered.png", Key.Window);

        context = "key drag / wrong target";
        Vector2 wrongTarget = WrongDropPoint();
        yield return DragKey(wrongTarget);
        yield return null;
        yield return Wait(() => Ready(Key.Window) && story.CanUseMotherKey, "a wrong drop offers a retry on the following update", 3f);
        Check(S.motherGiftStage == OSChapter04MotherGiftStage.KeyOffered && !os.IsMotherHeartUnlocked
            && !Key.IsDragging && Key.KeyRect.GetComponent<Image>().enabled && Get(Key, "ghostRect") == null,
            "wrong drop restores the visible key without unlocking or leaving a ghost");
        // Reopening and CompleteOpeningFromScript can finish in this update;
        // allow a render/layout pass before the next real pointer raycast.
        yield return null;

        context = "key drag / mother heart";
        yield return DragKey(ScreenRect((RectTransform)os.MotherHeartDesktopIcon.transform).center);
        Check(S.motherGiftStage == OSChapter04MotherGiftStage.Unlocked && S.currentEntryId == Prefix + "39",
            "successful drop records Unlocked while retaining 39");
        Check(!Key.Window.IsOpen && !story.CanContinue && Heart.Window.IsOpen && Heart.Window.IsOpening,
            "key is consumed but the opening heart has not passed the narrative gate");
        string unlocked = Snapshot("unlocked-during-open");
        yield return Wait(() => S.currentEntryId == Prefix + "40" && Ready(Heart.Window),
            "the fully open heart releases line 40");
        Check(S.motherGiftStage == OSChapter04MotherGiftStage.Seen && Heart.HeartImage.texture != null,
            "Seen is backed by the visible production heart texture");
        var firstFrame = Heart.HeartImage.texture;
        yield return Wait(() => Heart.HeartImage.texture != firstFrame, "the mother heart animates with its real frame clock", 2f);
        yield return Wait(() => Link.DisplayedEntryId == Prefix + "40" && S.currentTextCompleted && story.CanContinue,
            "actual line 40 finishes before the close gesture", 60f);
        yield return Shot("02-mother-heart.png", Heart.Window);

        context = "40 Continue / red companion closes";
        yield return ClickContinue();
        Check(S.currentEntryId == Prefix + "40" && S.motherGiftStage == OSChapter04MotherGiftStage.CloseRequested
            && Heart.Window.IsOpen && os.CursorRelease.OwnsScriptActor(story),
            "Continue starts the red companion close without advancing early");
        Check(!story.TryContinue(), "a second Continue cannot bypass the closing gesture");
        string closing = Snapshot("close-requested-in-flight");
        yield return Wait(() => S.currentEntryId == Prefix + "40a" && !Heart.Window.IsOpen,
            "the heart really closes before 40a", 8f);
        Check(S.motherGiftStage == OSChapter04MotherGiftStage.Closed && !os.CursorRelease.OwnsScriptActor(story),
            "closing releases the companion and records Closed");
        yield return Wait(() => Link.DisplayedEntryId == Prefix + "40a" && Link.IsPresenting,
            "the authored follow-up is displayed");
        yield return Shot("03-heart-closed.png");
        Check(revealTransitions == 1 && closeTransitions == 1, "the normal route crosses each guarded boundary exactly once");

        context = "restore / key offered in flight";
        yield return Restore(keyOffered, "key-offered");
        yield return Wait(() => story.CanUseMotherKey && Ready(Key.Window), "in-flight key save reconstructs a usable settled key");
        Check(S.currentEntryId == Prefix + "39" && S.motherGiftStage == OSChapter04MotherGiftStage.KeyOffered
            && !Heart.Window.IsOpen && !os.CursorRelease.OwnsScriptActor(story) && !story.TryContinue(),
            "restored key neither replays the red entrance nor skips discovery");

        context = "restore / unlocked during opening";
        yield return Restore(unlocked, "unlocked");
        yield return Wait(() => S.currentEntryId == Prefix + "40" && Ready(Heart.Window),
            "an interrupted heart opening resumes to the real reveal");
        Check(!Key.Window.IsOpen && S.motherGiftStage == OSChapter04MotherGiftStage.Seen,
            "unlocked restore never offers a duplicate key");

        context = "restore / close requested in flight";
        yield return Restore(closing, "close-requested");
        yield return Wait(() => S.currentEntryId == Prefix + "40a" && S.motherGiftStage == OSChapter04MotherGiftStage.Closed,
            "interrupted closing settles once into 40a");
        Check(!Heart.Window.IsOpen && !os.CursorRelease.OwnsScriptActor(story),
            "closing restore leaves neither a heart window nor a red companion lease");

        context = "desktop reopening / taskbar";
        // A player can reveal the desktop by minimizing the other apps. Do not
        // click through their rectangles just to reach the mother-heart icon.
        foreach (var window in os.WindowUnits.ToArray())
            if (window != null && window.IsOpen && window != Heart.Window && window != Key.Window) window.Minimize();
        yield return null;
        Check(os.IsMotherHeartUnlocked && !os.MotherHeartDesktopIcon.IsLocked,
            "the discovered desktop program stays unlocked after 40a");
        Click(os.MotherHeartDesktopIcon.gameObject, (RectTransform)os.MotherHeartDesktopIcon.transform,
            "desktop mother-heart launcher", 2);
        yield return Wait(() => Ready(Heart.Window), "desktop double click reopens the heart");
        Check(HeartProcess() != null, "normal desktop opening owns a real taskbar process");
        string openHeart = Snapshot("heart-open-after-story");
        ClickWindowButton(Heart.Window, OSWindowButton.WindowAction.Minimize);
        Check(Heart.Window.IsMinimized && HeartProcess() != null, "minimizing retains the heart's taskbar binding");
        string minimizedHeart = Snapshot("heart-minimized-after-story");
        ClickHeartProcess();
        yield return Wait(() => Ready(Heart.Window), "taskbar restores the minimized heart");

        context = "restore / open heart with taskbar";
        yield return Restore(openHeart, "heart-open");
        yield return Wait(() => Ready(Heart.Window), "saved open heart returns as an open window");
        Check(HeartProcess() != null, "open-window restoration rebinds the mother-heart taskbar item");
        context = "restore / minimized heart with taskbar";
        yield return Restore(minimizedHeart, "heart-minimized");
        Check(Heart.Window.IsOpen && Heart.Window.IsMinimized && HeartProcess() != null,
            "saved minimized heart retains both minimization and taskbar access");
        ClickHeartProcess();
        yield return Wait(() => Ready(Heart.Window), "restored taskbar item can reveal its minimized heart");
        Check(S.currentEntryId == Prefix + "40a" && S.motherGiftStage == OSChapter04MotherGiftStage.Closed,
            "desktop and taskbar operations do not replay or advance the gift story");
        Check(errors.Count == 0, "no runtime errors or progression-gate violations were observed");
    }

    static void Bind()
    {
        os = OS_Handle.Instance; story = os?.Chapter04Story;
        Check(os != null && story != null && Link != null && EventSystem.current != null, "production story and EventSystem resolve");
        if (observedStory != story)
        {
            if (observedStory != null) observedStory.ProgressChanged -= ObserveProgress;
            observedStory = story; observedStory.ProgressChanged += ObserveProgress;
        }
        lastLine = S?.currentEntryId;
        GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false); Focus();
    }

    static void ObserveProgress()
    {
        if (S == null || S.currentEntryId == lastLine) return;
        string previous = lastLine; lastLine = S.currentEntryId;
        bool heartReady = Ready(Heart?.Window), heartOpen = Heart?.Window.IsOpen == true;
        bool ownsActor = os?.CursorRelease?.OwnsScriptActor(story) == true;
        observations.Add(new { context, type = "story boundary", previous, current = lastLine,
            stage = S.motherGiftStage.ToString(), heartReady, heartOpen, ownsActor, frame = Time.frameCount });
        if (previous == Prefix + "39" && lastLine == Prefix + "40")
        {
            revealTransitions++;
            if (!heartReady || S.motherGiftStage != OSChapter04MotherGiftStage.Seen)
                errors.Add("39 advanced to 40 before the mother-heart window was Ready/Seen");
        }
        if (previous == Prefix + "40" && lastLine == Prefix + "40a")
        {
            closeTransitions++;
            if (heartOpen || ownsActor || S.motherGiftStage != OSChapter04MotherGiftStage.Closed)
                errors.Add("40 advanced to 40a before the heart closed and the companion lease was released");
        }
    }

    static string Snapshot(string label)
    {
        OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(isolatedDirectory, label + ".json"), json);
        observations.Add(new { context, type = "snapshot", label, S.currentEntryId, S.currentTextCompleted,
            stage = S.motherGiftStage.ToString(), heart = data.GetOSResourceValue(ResourceType.Heart),
            fault = data.GetOSResourceValue(ResourceType.Fault), history = S.history.Count });
        return json;
    }

    static IEnumerator Restore(string json, string label)
    {
        ReleaseInput(); story.ResetPresentation(); os.Chapter04Entry?.ResetPresentation(); Link.NotifyOSHidden();
        var save = JsonUtility.FromJson<GameSaveData>(json);
        data.ReplaceSaveData(save); lastLine = S.currentEntryId;
        int heart = data.GetOSResourceValue(ResourceType.Heart), fault = data.GetOSResourceValue(ResourceType.Fault);
        yield return OSSessionPersistence.Restore(save);
        Bind(); Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        yield return null; yield return null;
        Check(ReferenceEquals(data.SaveData, save) && !OSSessionPersistence.IsRestoring,
            "production persistence completes JSON-clone restoration: " + label);
        Check(data.GetOSResourceValue(ResourceType.Heart) == heart && data.GetOSResourceValue(ResourceType.Fault) == fault,
            "restoration does not charge the gift's committed resources again: " + label);
    }

    static IEnumerator DragKey(Vector2 destination)
    {
        Canvas.ForceUpdateCanvases();
        var pointer = Pointer(ScreenRect(Key.KeyRect).center);
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        GameObject receiver = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject) : null;
        RecordKeyDragRaycast(hits, pointer.position);
        Check(receiver == Key.gameObject && hits[0].gameObject.transform.IsChildOf(Key.KeyRect.parent),
            "topmost raycast reaches the real key grab area and its drag handler");
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0];
        pointer.pointerPress = receiver; pointer.pointerDrag = receiver; pointer.dragging = true;
        ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.beginDragHandler);
        var ghost = Get(Key, "ghostRect") as RectTransform;
        Check(Key.IsDragging && ghost != null && !ghost.IsChildOf(Key.transform)
            && ghost.GetComponent<CanvasGroup>()?.blocksRaycasts == false,
            "production drag creates an unclipped, nonblocking ghost outside the window");
        Check(pointer.pointerDrag == ghost.gameObject && pointer.pointerDrag.activeInHierarchy,
            "the EventSystem drag receiver transfers to the independent ghost proxy");
        Vector2 from = pointer.position;
        Rect sourceBounds = ScreenRect(Key.Window.WindowRect);
        bool detached = false;
        for (int step = 1; step <= 5; step++)
        {
            Vector2 next = Vector2.Lerp(from, destination, step / 5f);
            pointer.delta = next - pointer.position; pointer.position = next;
            Check(pointer.pointerDrag != null && pointer.pointerDrag.activeInHierarchy,
                "the live drag receiver remains available at step " + step);
            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler);
            if (!sourceBounds.Contains(ScreenRect(ghost).center))
            {
                detached = true;
                Check(!Key.Window.IsOpen && Key.IsDragging && ghost.gameObject.activeInHierarchy,
                    "leaving the source closes the little window while its ghost continues");
            }
            yield return null;
            if (detached)
                Check(!Key.Window.IsOpen && Key.IsDragging && pointer.pointerDrag != null
                    && pointer.pointerDrag.activeInHierarchy,
                    "the next update retains the detached gesture without reopening its source");
        }
        Check(detached, "the probe actually carries the key outside its source window");
        ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
        if (pointer.pointerPress != null) ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
        // Intentionally no yield here: the caller can capture the real Unlocked
        // state before the director observes the completed window opening.
    }

    static void RecordKeyDragRaycast(List<RaycastResult> hits, Vector2 point)
    {
        var window = Key.Window;
        var visual = Key.KeyRect.GetComponent<Image>();
        var hitImage = Key.KeyRect.parent.GetComponent<Image>();
        observations.Add(new { context, type = "key begin-drag raycast", frame = Time.frameCount,
            point = new[] { point.x, point.y }, windowBounds = BoundsValues(ScreenRect(window.WindowRect)),
            keyBounds = BoundsValues(ScreenRect(Key.KeyRect)), sourceIsOpen = window.IsOpen,
            sourceIsOpening = window.IsOpening, sourceIsMinimized = window.IsMinimized,
            sourceActiveSelf = window.gameObject.activeSelf, sourceActive = window.gameObject.activeInHierarchy,
            sibling = window.transform.GetSiblingIndex(), sourcePath = FullPath(window.transform),
            visual = visual != null ? new { path = FullPath(visual.transform), visual.enabled, visual.depth,
                visual.raycastTarget, culled = visual.canvasRenderer.cull, sprite = visual.sprite?.name } : null,
            hitImage = hitImage != null ? new { path = FullPath(hitImage.transform), hitImage.enabled, hitImage.depth,
                hitImage.raycastTarget, culled = hitImage.canvasRenderer.cull,
                active = hitImage.gameObject.activeInHierarchy, canvas = FullPath(hitImage.canvas?.transform),
                registered = hitImage.canvas != null && GraphicRegistry.GetGraphicsForCanvas(hitImage.canvas).Contains(hitImage) } : null,
            groups = window.GetComponentsInParent<CanvasGroup>(true)
                .Concat(window.GetComponentsInChildren<CanvasGroup>(true)).Distinct()
                .Select(group => new { path = FullPath(group.transform), group.enabled,
                    active = group.gameObject.activeInHierarchy, group.alpha, group.interactable,
                    group.blocksRaycasts, group.ignoreParentGroups }).ToArray(),
            canvases = window.GetComponentsInParent<Canvas>(true).Select(canvas => new {
                path = FullPath(canvas.transform), canvas.enabled, active = canvas.gameObject.activeInHierarchy,
                canvas.overrideSorting, canvas.sortingOrder, canvas.sortingLayerID }).ToArray(),
            hits = hits.Take(5).Select(hit => new { path = FullPath(hit.gameObject.transform),
                dragReceiver = FullPath(ExecuteEvents.GetEventHandler<IDragHandler>(hit.gameObject)?.transform),
                hit.depth, hit.sortingOrder, hit.sortingLayer, hit.distance }).ToArray() });
    }

    static float[] BoundsValues(Rect rect) => new[] { rect.xMin, rect.yMin, rect.xMax, rect.yMax };

    static string FullPath(Transform target)
    {
        if (target == null) return null;
        var parts = new Stack<string>();
        for (var current = target; current != null; current = current.parent) parts.Push(current.name);
        return string.Join("/", parts);
    }

    static Vector2 WrongDropPoint()
    {
        Rect source = ScreenRect(Key.Window.WindowRect);
        Rect target = ScreenRect((RectTransform)os.MotherHeartDesktopIcon.transform);
        float margin = Mathf.Max(30f, source.width * .2f);
        var candidates = new[] {
            new Vector2(source.xMin - margin, source.center.y), new Vector2(source.xMax + margin, source.center.y),
            new Vector2(source.center.x, source.yMin - margin), new Vector2(source.center.x, source.yMax + margin) };
        foreach (var point in candidates)
            if (point.x > 5f && point.x < Screen.width - 5f && point.y > 5f && point.y < Screen.height - 5f
                && !source.Contains(point) && !target.Contains(point)) return point;
        throw new InvalidOperationException("No on-screen wrong-drop probe lies outside both source and target");
    }

    static IEnumerator ClickContinue()
    {
        yield return null;
        var actions = Get(View, "actions") as List<Button>;
        var button = actions?.LastOrDefault(candidate => candidate != null && candidate.gameObject.activeInHierarchy);
        Check(button != null && button.IsInteractable(), "the real Live Continue control is interactable");
        Click(button.gameObject, (RectTransform)button.transform, "Live Continue");
    }

    static void ClickWindowButton(OSWindowUnit window, OSWindowButton.WindowAction action)
    {
        var button = window.GetComponentsInChildren<OSWindowButton>(true)
            .FirstOrDefault(candidate => candidate.LinkedWindow == window && candidate.Action == action);
        Check(button != null && button.IsInteractable, window.name + " authored " + action + " control is available");
        Click(button.gameObject, (RectTransform)button.transform, window.name + " " + action);
    }

    static ProcessUnit HeartProcess() => os.ProcessUnits.FirstOrDefault(unit => unit != null
        && unit.LinkedWindow == Heart.Window && unit.gameObject.activeInHierarchy);

    static void ClickHeartProcess()
    {
        var process = HeartProcess();
        Check(process != null, "a visible taskbar process is bound to the mother heart");
        Click(process.gameObject, (RectTransform)process.transform, "mother-heart taskbar item");
    }

    static void Click(GameObject expected, RectTransform target, string label, int count = 1)
    {
        Canvas.ForceUpdateCanvases();
        Rect bounds = ScreenRect(target);
        var candidates = new[] { bounds.center,
            new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, .15f), bounds.center.y),
            new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, .85f), bounds.center.y) };
        PointerEventData selected = null;
        var hits = new List<RaycastResult>();
        foreach (var point in candidates)
        {
            var pointer = Pointer(point); pointer.clickCount = count;
            hits.Clear(); EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) != expected) continue;
            pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0]; selected = pointer; break;
        }
        observations.Add(new { context, type = "pointer click", label, expected = expected.name,
            reached = selected != null, hits = hits.Take(5).Select(hit => hit.gameObject.name).ToArray() });
        Check(selected != null, "a real topmost raycast reaches " + label);
        ExecuteEvents.Execute(expected, selected, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(expected, selected, ExecuteEvents.pointerUpHandler);
        Check(ExecuteEvents.Execute(expected, selected, ExecuteEvents.pointerClickHandler), "production click handler executes: " + label);
    }

    static PointerEventData Pointer(Vector2 point) => new PointerEventData(EventSystem.current) {
        button = PointerEventData.InputButton.Left, pointerId = -1, clickCount = 1,
        position = point, pressPosition = point, eligibleForClick = true
    };

    static Rect ScreenRect(RectTransform target)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners);
        var canvas = target.GetComponentInParent<Canvas>()?.rootCanvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
            points.Max(point => point.x), points.Max(point => point.y));
    }

    static IEnumerator Shot(string name, OSWindowUnit foreground = null)
    {
        Canvas.ForceUpdateCanvases(); yield return null;
        if (foreground != null) ObserveScreenshotWindow(foreground, name);
        string path = Path.Combine(OutputDirectory, name);
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 24, "screenshot written: " + name, 10f);
        screenshots.Add(path);
    }

    static void ObserveScreenshotWindow(OSWindowUnit window, string shot)
    {
        Check(Ready(window), "screenshot window is fully open: " + shot);
        Canvas.ForceUpdateCanvases();
        Rect bounds = ScreenRect(window.WindowRect);
        float inset = Mathf.Min(3f, Mathf.Min(bounds.width, bounds.height) * .1f);
        var points = new[] { bounds.center,
            new Vector2(bounds.xMin + inset, bounds.yMin + inset),
            new Vector2(bounds.xMin + inset, bounds.yMax - inset),
            new Vector2(bounds.xMax - inset, bounds.yMax - inset),
            new Vector2(bounds.xMax - inset, bounds.yMin + inset) };
        var names = new[] { "center", "bottom-left", "top-left", "top-right", "bottom-right" };
        var hits = new List<RaycastResult>();
        for (int i = 0; i < points.Length; i++)
        {
            hits.Clear(); EventSystem.current.RaycastAll(Pointer(points[i]), hits);
            var owner = hits.Count > 0 ? hits[0].gameObject.GetComponentInParent<OSWindowUnit>() : null;
            bool onScreen = points[i].x >= 0f && points[i].x < Screen.width
                && points[i].y >= 0f && points[i].y < Screen.height;
            bool reached = onScreen && owner == window;
            observations.Add(new { context, type = "screenshot visibility", shot, probe = names[i],
                position = new[] { points[i].x, points[i].y }, expected = window.name,
                actual = owner != null ? owner.name : null, onScreen, reached,
                hits = hits.Take(5).Select(hit => hit.gameObject.name).ToArray() });
        }
        // Occlusion is recorded for visual review, not rejected: these are
        // ordinary player-managed windows, not a forced topmost presentation.
    }

    static IEnumerator Wait(Func<bool> done, string label, float seconds = 25f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < until) yield return null;
        Check(done(), label + " [" + Describe() + "]");
    }

    static IEnumerator Hold(float seconds)
    { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }

    static void Focus()
    {
        mouse?.MakeCurrent(); keyboard?.MakeCurrent();
        foreach (var target in new MonoBehaviour[] { story, os?.Chapter04Entry, os?.CursorRelease, Key, Heart })
        {
            if (target == null || !(Get(target, "hasFocus") is bool value)) continue;
            if (!focusStates.ContainsKey(target)) focusStates.Add(target, value);
            Set(target, "hasFocus", true);
        }
        story?.DebugSetFocus(true);
    }

    static void ReleaseInput()
    {
        if (mouse != null && mouse.added)
            InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
        if (keyboard != null && keyboard.added) InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType);
    }

    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening
        && !window.IsClosing && !window.IsMinimized && window.gameObject.activeInHierarchy;
    static string Describe() => "line=" + S?.currentEntryId + ", stage=" + S?.motherGiftStage
        + ", available=" + story?.IsAvailable + ", message=" + Link?.DisplayedEntryId;
    static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(context + ": " + label); checks.Add(context + ": " + label); }
    static void CaptureError(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + message); }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static object Get(object target, string name) => target?.GetType().GetField(name, Members)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
}
