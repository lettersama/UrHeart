using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Step = OSChapter02FinalTalkDirector.FinalTalkStep;

// Opt-in integration checks. The real return/continue transaction writes only
// beneath Temp. Cleanup leaves Play Mode at the menu with player writes off.
public static class OSChapter02NightfallQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> layouts = new List<object>();
    static OS_Handle os;
    static OSChapter02FinalTalkDirector talk;
    static OSChapter02NightfallPresentation night;
    static OSChapter02DemoEnding ending;
    static OSLinkMessageController link;
    static OSWindowUnit live, settings, stateWindow;
    static DesktopProcessUnit stateIcon;
    static GameSetup setup;
    static string context;
    static int inputFrame;
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
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); layouts.Clear(); context = "initialization";
        inputFrame = Time.frameCount + 100;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter02NightfallQA", Guid.NewGuid().ToString("N")));
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
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = paths.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Application.runInBackground = true;
            Set(setup, "isDemo", true);
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
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + error;
                    Debug.LogError("[Chapter02 Nightfall QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " checks, " + layouts.Count + " localized labels";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                talk?.ResetPresentation(); night?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo)
                    throw new InvalidOperationException("Original memory/language/demo mode was not restored");
                if (hashes != null && !paths.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save files changed");
                checks.Add("cleanup restored original memory, language, demo mode, resources, and save hashes");
            }
            catch (Exception cleanup) { Status = "FAILED during cleanup: " + cleanup + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                status = Status, context, checks, layouts, originalFiles = paths, originalHashes = hashes,
                finalHashes = paths.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Exit callback is inspected, not invoked. First-chapter checks stop before video playback."
            }, Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter02 Nightfall QA] " + Status + " | " + OutputDirectory);
            os = null; talk = null; night = null; ending = null; link = null;
            live = settings = stateWindow = null; stateIcon = null; setup = null;
        }
    }

    static IEnumerator Run()
    {
        context = "checkpoint";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter02FinalTalk), "final-talk checkpoint accepted in demo");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint succeeds with formal save writes off");
        Bind();
        GameDataManager.Instance.SetCurrentLanguage(LanguageState.zh_CN);
        Pump();
        var baseline = Capture();
        var fixture = Clone(baseline);
        OSChapter02FinalTalkDirector.PrepareDebugCheckpoint(fixture.osProcess.memory, fixture.osProcess.energyLoop, 1);
        fixture.osProcess.memory.chapter02FinalTalkComfortAnswer = 1;
        fixture.osProcess.memory.chapter02FinalTalkQuestionAnswer = 1;
        fixture.osProcess.memory.chapter02FinalTalkStep = (int)Step.Night;
        fixture.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        Install(fixture);

        context = "night starts during its text";
        Check(talk.Step == Step.Night && !talk.DebugMessageTyped && night.IsNight, "night presentation starts with the untyped night line");
        yield return Wait(() => night.DebugDark > 0f, "dither begins while night line is visible");
        Check(!talk.DebugMessageTyped, "night darkness does not wait for text completion");
        var rain = Get(night, "rain") as AudioSource;
        var clip = GameDataManager.Instance.AudioData.AMBIENT.Find(x => x != null && x.keyid == "Rain")?.audioclip;
        Check(rain != null && clip != null && rain.clip == clip && rain.loop && rain.volume > 0f
            && rain.outputAudioMixerGroup == AudioManager.Instance.AmbientMixerGroup,
            "night uses a fading Rain voice on the ambient mixer");
        Advance();

        context = "goodbye leads before typing completes";
        Check(talk.Step == Step.Goodbye && !talk.DebugMessageTyped && talk.CanLeadToStateCheck,
            "the opened untyped goodbye permits the red pointer lead");
        Invoke(os.Chapter02PaperPush, "Update");
        Check((bool)Get(os.Chapter02PaperPush, "choreoLeading") && os.CursorRelease.ScriptActorIsMoving,
            "production paper-push update starts its flight during goodbye");
        string before = Progress();
        DesktopClick(); Settle();
        Check(!stateWindow.IsOpen && !night.IsThundering && Progress() == before,
            "premature goodbye desktop click opens neither StateCheck nor thunder");
        yield return Wait(() => !os.CursorRelease.ScriptActorIsMoving, "red pointer reaches StateCheck", 20f);
        Check(talk.Step == Step.Goodbye && (bool)Get(os.Chapter02PaperPush, "choreoLeading"),
            "red pointer waits at StateCheck while goodbye still awaits confirmation");

        context = "independent ready click";
        var previousMouse = Mouse.current;
        Mouse mouse = null;
        try
        {
            mouse = InputSystem.AddDevice<Mouse>("Chapter02NightfallQAMouse");
            ChangeMouse(mouse, false);
            if (!talk.DebugMessageTyped) Press();
            Pump();
            talk.DebugConfirm(false, false, Time.frameCount - 1);
            ChangeMouse(mouse, true);
            talk.DebugConfirm(true, true, Mathf.Max(++inputFrame, Time.frameCount + 1));
            Settle(); talk.DebugStep();
            Check(talk.Step == Step.Chapter03Ready && !link.LinkWindow.IsOpen, "goodbye confirmation closes at Ready");
            ChangeMouse(mouse, false);
            DesktopClick(); talk.DebugStep(); DesktopClick();
            Check(!night.IsThundering && !stateWindow.IsOpen,
                "the same confirming gesture cannot start thunder in either release ordering");
            yield return null;
            ChangeMouse(mouse, false);
            yield return null;
            mouse.MakeCurrent(); talk.DebugStep();
            Check((bool)Get(talk, "nightfallClickArmed"), "a real idle frame arms an independent click");
            var unfinished = Capture();
            File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-ready-unfinished.json"), JsonUtility.ToJson(unfinished, true));
            before = Progress();
            int endings = 0;
            Action reached = () => endings++;
            night.EndReached += reached;
            try
            {
                ChangeMouse(mouse, true); ChangeMouse(mouse, false);
                DesktopClick(); DesktopClick();
                Check(night.IsThundering && !night.HasEnded && !stateWindow.IsOpen,
                    "an independent desktop click starts thunder without launching StateCheck");
                Check(os.IsSystemInputLocked, "thunder locks conflicting OS input");
                yield return ObserveDemoThunder();
                Check(endings == 1 && night.HasEnded && GameDataManager.Instance.OSProcessData.memory.chapter02DemoEndReached,
                    "one EndReached event marks the separate saved demo completion flag");
                Check(Progress() == before && talk.Step == Step.Chapter03Ready && GameDataManager.Instance.CurrentChapterIndex == 2,
                    "ending preserves Ready, chapter two, its memory node, and resources");
                DesktopClick();
                yield return new WaitForSecondsRealtime(.25f);
                Check(endings == 1, "repeated clicks and later frames cannot raise EndReached again");
            }
            finally { if (night != null) night.EndReached -= reached; }
            yield return CheckCard();
            yield return DiskRoundtrip();

            context = "unfinished save replaces completed save";
            yield return Restore(Clone(unfinished));
            yield return null;
            Check(!ending.IsPresenting && !night.HasEnded && !night.IsThundering
                && !GameDataManager.Instance.OSProcessData.memory.chapter02DemoEndReached,
                "an unfinished Ready save discards the previous completion card and thunder state");

            context = "full release boundary";
            Set(setup, "isDemo", false);
            yield return Restore(Clone(unfinished));
            ChangeMouse(mouse, false); yield return null; yield return null;
            mouse.MakeCurrent(); talk.DebugStep();
            before = Progress();
            int fullEndings = 0;
            Action fullReached = () => fullEndings++;
            night.EndReached += fullReached;
            try
            {
                DesktopClick();
                Check(night.IsThundering, "full release uses the same real desktop thunder entry");
                yield return Wait(() => night.HasEnded, "full release reaches the thunder boundary", 20f);
                yield return null;
                Check(fullEndings == 1 && !ending.IsPresenting
                    && !GameDataManager.Instance.OSProcessData.memory.chapter02DemoEndReached
                    && GameDataManager.Instance.CurrentChapterIndex == 3
                    && os.Chapter03Entry.Step == OSChapter03EntryDirector.EntryStep.Important,
                    "full release emits its ending once and enters Chapter3's unlock handoff without a demo flag or card");
            }
            finally { if (night != null) night.EndReached -= fullReached; }
            Set(setup, "isDemo", true);
            CheckFirstChapterFrontiers();
        }
        finally
        {
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        }
    }

    static IEnumerator ObserveDemoThunder()
    {
        context = "demo single-flash timing";
        const float flashEnds = .12f, blackStarts = .32f, cardStarts = .65f, tolerance = .0001f;
        var samples = new List<object>();
        float lastClock = -1f, firstBlackClock = -1f, cardClock = -1f;
        int brightPulses = 0, firstBlackFrame = -1, cardFrame = -1;
        bool wasBright = false, flashShot = false, blackShot = false, cardShot = false;
        try
        {
            yield return Wait(() =>
            {
                // LateUpdate advances this clock and paints alpha together. On
                // the following coroutine frame they describe the same image;
                // duplicate/frozen frames must not count as another flash.
                float clock = night.DebugThunderClock;
                if (clock > 0f && clock > lastClock + tolerance)
                {
                    lastClock = clock;
                    var pixels = Get(night, "alpha") as byte[];
                    Check(pixels != null && pixels.Length > 0, "demo thunder exposes its painted alpha mask");
                    float lightScale = (float)Get(night, "lightScale");
                    int transparent = 0, opaque = 0;
                    foreach (byte pixel in pixels)
                    {
                        if (pixel == 0) transparent++;
                        else if (pixel == 255) opaque++;
                    }
                    bool bright = transparent == pixels.Length;
                    bool black = opaque == pixels.Length && lightScale <= tolerance;
                    if (bright && !wasBright) brightPulses++;
                    wasBright = bright;
                    bool card = ending.IsPresenting;
                    samples.Add(new { frame = Time.frameCount, clock, transparent, opaque, total = pixels.Length,
                        lightScale, bright, black, card });
                    Check(clock >= flashEnds - tolerance || bright, "the opening demo flash fully reveals the OS");
                    Check(clock <= blackStarts + tolerance || black, "the demo mask is fully black with all lights off after 0.32 seconds");
                    Check(!card || clock >= cardStarts - tolerance, "the demo card never appears before 0.65 thunder seconds");
                    if (black && !card && firstBlackClock < 0f)
                    {
                        firstBlackClock = clock; firstBlackFrame = Time.frameCount;
                    }
                    // CaptureScreenshot queues an end-of-frame capture without
                    // yielding. The clock advances by at most .05 per frame, so
                    // only request the flash while the next paint is still bright.
                    if (bright && !flashShot && clock < flashEnds - .05f - tolerance)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "demo-single-flash.png"));
                        flashShot = true;
                    }
                    if (black && !card && !blackShot && clock < cardStarts - .05f - tolerance)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "demo-black-before-card.png"));
                        blackShot = true;
                    }
                    if (card && !cardShot)
                    {
                        cardClock = clock; cardFrame = Time.frameCount;
                        // Run is still in zh_CN here; CheckCard's ten-language
                        // loop begins only after the existing .25-second hold.
                        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "demo-card.zh-CN.png"));
                        cardShot = true;
                    }
                }
                return ending.IsPresenting;
            }, "thunder produces the demo card", 20f);
            Check(brightPulses == 1, "demo thunder paints exactly one full-bright pulse");
            Check(firstBlackClock >= blackStarts - tolerance && firstBlackClock < cardClock
                && firstBlackFrame < cardFrame, "a fully black frame precedes the demo card");
        }
        finally
        {
            File.WriteAllText(Path.Combine(OutputDirectory, "demo-thunder-samples.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    brightPulses, firstBlackClock, cardClock, flashShot, blackShot, cardShot, samples
                }, Newtonsoft.Json.Formatting.Indented));
        }
    }

    static IEnumerator CheckCard()
    {
        context = "demo card";
        var card = Get(ending, "card") as GameObject;
        var canvas = card?.GetComponent<Canvas>();
        var cursor = CursorHandle.Instance?.GetComponent<Canvas>();
        // Root canvases sort directly; overrideSorting only applies to nested
        // canvases. Retain the strict independent-order check for nested cards.
        Check(card != null && card.activeInHierarchy && canvas != null && canvas.enabled
            && (canvas.isRootCanvas || canvas.overrideSorting) && canvas.sortingOrder > 1202,
            "demo card is visible above night and red-pointer canvases | " + CanvasState(canvas));
        Check(cursor != null && cursor.enabled && cursor.gameObject.activeInHierarchy
            && (cursor.isRootCanvas || cursor.overrideSorting)
            && cursor.sortingLayerID == canvas.sortingLayerID && cursor.sortingOrder > canvas.sortingOrder,
            "the interactive player cursor is visible above the card | " + CanvasState(cursor));
        var returnButton = Get(ending, "returnButton") as Button;
        var exitButton = Get(ending, "exitButton") as Button;
        Check(returnButton != null && returnButton.interactable && exitButton != null && exitButton.interactable,
            "both demo actions are interactable");
        Check(HasRuntimeListener(returnButton.onClick, ending, "ReturnToMainMenu")
            && HasRuntimeListener(exitButton.onClick, ending, "ExitGame")
            && returnButton.onClick.GetPersistentEventCount() == 0 && exitButton.onClick.GetPersistentEventCount() == 0,
            "buttons bind the chapter-two actions without inherited chapter-one listeners");
        string[] fields = { "title", "message", "returnLabel", "exitLabel" };
        string[] keys = { "demo_title", "demo_message", "return_menu", "exit" };
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
        {
            context = "card language " + language;
            GameDataManager.Instance.SetCurrentLanguage(language);
            Canvas.ForceUpdateCanvases(); yield return null;
            for (int i = 0; i < fields.Length; i++)
            {
                string key = "ui.os.chapter02_ending." + keys[i];
                var entries = GameDataManager.Instance.LocalizationData.Entries.Where(x => x.Key == key).ToArray();
                Check(entries.Length == 1 && entries[0].Values.Count(x => x.Language == language && !string.IsNullOrWhiteSpace(x.Text)) == 1,
                    key + " explicitly translates " + language);
                var label = Get(ending, fields[i]) as TMP_Text;
                Check(label != null && label.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(label.text),
                    language + "/" + fields[i] + " is visible");
                label.ForceMeshUpdate();
                float preferred = label.GetPreferredValues(label.text, label.rectTransform.rect.width, Mathf.Infinity).y;
                Check(!label.isTextTruncated && preferred <= label.rectTransform.rect.height + 2f,
                    language + "/" + fields[i] + " fits its authored rectangle");
                layouts.Add(new { language = language.ToString(), label = fields[i], text = label.text,
                    requiredHeight = preferred, height = label.rectTransform.rect.height });
            }
        }
    }

    static IEnumerator DiskRoundtrip()
    {
        context = "end-card return and disk continue";
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var flow = GameSessionFlow.Instance;
        var expected = Capture();
        string before = Progress();
        var previousOS = os;
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        (Get(ending, "returnButton") as Button).onClick.Invoke();
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !flow.IsBusy, "the real return button finishes the save/menu transaction", 65f);
        var stored = new GameAutosaveStorage(OutputDirectory).Read();
        Check(stored.Success, "end-card return creates a readable isolated disk save");
        var disk = JsonUtility.FromJson<GameSaveData>(stored.Payload);
        Check(disk.osProcess.memory.chapter02DemoEndReached
            && disk.osProcess.memory.chapter02FinalTalkStep == (int)Step.Chapter03Ready
            && disk.osProcess.currentChapterIndex == 2
            && disk.osProcess.memory.currentDataIndex == expected.osProcess.memory.currentDataIndex
            && JsonUtility.ToJson(disk.osResources) == JsonUtility.ToJson(expected.osResources),
            "disk stores completion independently of the unchanged story/resources boundary");
        Check(flow.ContinueGame(), "continue accepts the completed isolated save");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning
            && OS_Handle.Instance != null && OS_Handle.Instance.IsOSVisible, "completed save reconstructs the OS", 85f);
        saves.EnterTestSession(); Bind();
        int replayed = 0;
        Action reached = () => replayed++;
        night.EndReached += reached;
        try
        {
            yield return Wait(() => ending.IsPresenting, "completed Ready restores the card immediately");
            Check(os != previousOS && Progress() == before && night.HasEnded && night.DebugThunderClock >= .65f,
                "continued scene restores the terminal thunder state without replaying its timeline");
            yield return new WaitForSecondsRealtime(.3f);
            Check(replayed == 0 && night.HasEnded, "continued ending does not emit a second EndReached");
        }
        finally { night.EndReached -= reached; }
    }

    static void CheckFirstChapterFrontiers()
    {
        context = "first chapter demo frontiers";
        var first = os.Chapter01Ending;
        Check(first != null && first.Configuration != null && GameSetup.IsDemo, "first-chapter demo configuration is available");
        var originalState = Get(first, "state");
        var originalConfig = first.Configuration;
        var copy = UnityEngine.Object.Instantiate(originalConfig);
        IEnumerator routine = null;
        try
        {
            copy.initialBlackSeconds = copy.doorTransitionSeconds = 0f;
            Set(first, "configuration", copy);
            var oldCompleted = new OSChapter01EndingSaveData { phase = OSChapter01EndingPhase.Completed, completed = true };
            Set(first, "state", oldCompleted);
            routine = (IEnumerator)Invoke(first, "Present");
            Check(routine.MoveNext() && oldCompleted.phase == OSChapter01EndingPhase.Slides
                && oldCompleted.slideIndex == 0 && !oldCompleted.completed,
                "legacy first-chapter demo completion replays the slides and film instead of its old end card");
            (routine as IDisposable)?.Dispose();
            var accepted = new OSChapter01EndingSaveData { phase = OSChapter01EndingPhase.DoorKnocked };
            Set(first, "state", accepted);
            routine = (IEnumerator)Invoke(first, "Present");
            Check(routine.MoveNext() && routine.MoveNext() && routine.Current is IEnumerator,
                "accepted first-chapter door reaches its transition branch in demo");
            // Do not run nested FadeBlack or PlayAnimation: this verifies the
            // authored branch frontier without replaying the film or editing assets.
            Check(routine.MoveNext() && accepted.phase == OSChapter01EndingPhase.Animation
                && routine.Current is IEnumerator && !accepted.completed,
                "accepted door continues to the animation frontier without marking demo completion");
        }
        finally
        {
            (routine as IDisposable)?.Dispose();
            Set(first, "state", originalState); Set(first, "configuration", originalConfig);
            UnityEngine.Object.Destroy(copy);
        }
    }

    static void Bind()
    {
        os = OS_Handle.Instance; talk = os?.Chapter02FinalTalk; ending = os?.Chapter02DemoEnding;
        night = talk?.Nightfall; link = os?.LinkMessageController;
        live = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        settings = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        stateIcon = os?.DesktopProcessUnits.FirstOrDefault(x => x != null && x.ProcessKey == OSDesktopProcessKeys.StateCheck);
        stateWindow = stateIcon != null ? Invoke(os, "GetDesktopProcessWindow", stateIcon) as OSWindowUnit : null;
        Check(os != null && talk != null && ending != null && night != null && link != null && live != null
            && settings != null && stateIcon != null && stateWindow != null, "nightfall runtime references resolve");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Focus();
    }
    static void Focus()
    {
        talk.DebugSetFocus(true); Set(night, "hasFocus", true);
        os.Chapter03Entry?.DebugSetFocus(true);
        Set(os.Chapter02PaperPush, "hasFocus", true); Set(os.CursorRelease, "hasFocus", true);
    }
    static void Install(GameSaveData snapshot)
    {
        talk.ResetPresentation(); link.NotifyOSHidden();
        GameDataManager.Instance.ReplaceSaveData(snapshot);
        night.ResetPresentation(); Invoke(ending, "Update"); Focus();
        settings.CloseFromScript(); stateWindow.CloseFromScript();
        os.OpenCharacterWindowForReconnect(0f); Settle();
        link.SetGlobalMessagesSuppressed(false); link.NotifyOSBecameVisible(); Pump();
    }
    static IEnumerator Restore(GameSaveData snapshot)
    {
        talk.ResetPresentation(); link.NotifyOSHidden();
        GameDataManager.Instance.ReplaceSaveData(snapshot);
        yield return OSSessionPersistence.Restore(snapshot);
        Bind(); Invoke(ending, "Update");
        link.SetGlobalMessagesSuppressed(false); link.NotifyOSBecameVisible(); Pump();
    }
    static GameSaveData Capture()
    {
        var save = GameDataManager.Instance.SaveData;
        OSSessionPersistence.Capture(save); GameSessionFlow.Capture(save);
        return Clone(save);
    }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static void Advance() { if (!talk.DebugMessageTyped) Press(); Pump(); Press(); Pump(); }
    static void Press()
    {
        inputFrame = Mathf.Max(inputFrame + 2, Time.frameCount + 2);
        talk.DebugConfirm(false, false, inputFrame - 1); talk.DebugConfirm(true, true, inputFrame);
    }
    static void Pump() { for (int i = 0; i < 6; i++) { talk.DebugStep(); Settle(); } }
    static void Settle()
    {
        foreach (var window in new[] { live, settings, stateWindow, link?.LinkWindow })
        { Complete(window, "openSequence"); Complete(window, "closeSequence"); }
        Complete(link, "reopenTween"); Complete(link?.WindowLayout, "heightTween");
        Canvas.ForceUpdateCanvases();
    }
    static void DesktopClick() => Invoke(os, "HandleDesktopProcessClicked", stateIcon);
    static void ChangeMouse(Mouse mouse, bool pressed)
    {
        InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }.WithButton(MouseButton.Left, pressed),
            InputState.currentUpdateType);
        mouse.MakeCurrent();
    }
    static bool HasRuntimeListener(UnityEvent evt, object target, string method)
    {
        var calls = typeof(UnityEventBase).GetMethod("PrepareInvoke", Private)?.Invoke(evt, null) as IEnumerable;
        if (calls == null) return false;
        foreach (var call in calls)
        for (var type = call.GetType(); type != null; type = type.BaseType)
        foreach (var field in type.GetFields(Private | BindingFlags.DeclaredOnly))
            if (field.GetValue(call) is Delegate action && action.GetInvocationList().Any(x => ReferenceEquals(x.Target, target) && x.Method.Name == method))
                return true;
        return false;
    }
    static string CanvasState(Canvas canvas)
    {
        if (canvas == null) return "canvas=null";
        var parent = canvas.transform.parent?.GetComponentInParent<Canvas>(true);
        var root = canvas.rootCanvas;
        return "name=" + canvas.name + ", active=" + canvas.gameObject.activeInHierarchy
            + ", enabled=" + canvas.enabled + ", parent=" + parent?.name + ", parentEnabled=" + parent?.enabled
            + ", isRoot=" + canvas.isRootCanvas + ", root=" + root?.name + ", rootEnabled=" + root?.enabled
            + ", override=" + canvas.overrideSorting + ", layer=" + canvas.sortingLayerID
            + ", order=" + canvas.sortingOrder + ", rootOrder=" + root?.sortingOrder + ", mode=" + canvas.renderMode;
    }
    static string Progress()
    {
        var data = GameDataManager.Instance; var m = data.OSProcessData.memory;
        return data.CurrentChapterIndex + ":" + m.currentDataIndex + ":" + m.chapter02FinalTalkStep + ":"
            + m.chapter02ReadAssistant + ":" + m.chapter02FinalTalkComfortAnswer + ":" + m.chapter02FinalTalkQuestionAnswer
            + ":" + JsonUtility.ToJson(data.SaveData.osResources);
    }
    static string Describe() => "step=" + talk?.Step + ", typed=" + talk?.DebugMessageTyped + ", entry=" + link?.DisplayedEntryId
        + ", dark=" + night?.DebugDark + ", thunder=" + night?.DebugThunderClock + ", ended=" + night?.HasEnded + ", card=" + ending?.IsPresenting;
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Check(condition(), label);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        if (!checks.Contains(label)) checks.Add(label);
    }
    static object Get(object target, string name) => target?.GetType().GetField(name, Private)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Private);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static object Invoke(object target, string method, params object[] args)
    {
        var callback = target?.GetType().GetMethod(method, Private);
        if (callback == null) throw new MissingMethodException(target?.GetType().Name, method);
        return callback.Invoke(target, args);
    }
    static void Complete(object target, string field)
    { var tween = Get(target, field) as Tween; if (tween != null && tween.IsActive()) tween.Complete(true); }
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
