using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Opt-in isolated integration probe. Only the real checkpoint installs a story
// snapshot; doors, choices, papers and the envelope then use production input.
// Does not focus the Editor, alter authored timing, or write scenes/save files.
public static class OSChapter03EnvelopeQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>(), issues = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<Frame> frames = new List<Frame>();
    static readonly List<VideoFrame> videoFrames = new List<VideoFrame>();
    static readonly List<SfxEvent> sfx = new List<SfxEvent>();
    static readonly Dictionary<OSCursorReleaseDirector, bool> actorFocus = new Dictionary<OSCursorReleaseDirector, bool>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSWindowUnit memoryWindow, liveWindow, settingsWindow;
    static Mouse mouse;
    static Camera camera;
    static bool cancel, videoOnly, sampling, recording;
    static int lastFrame, episode, videoIndex;
    static float nextVideoAt, videoStart;
    static Exception samplingFailure;
    static string context;
    static OSChapter03StoryState S => story?.State;
    static OSChapter03StoryPresentation P => story?.Presentation;
    static OSHomeView V => home?.View;
    static OSLinkMessageController Messages => os?.LinkMessageController;
    static bool Idle => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !Messages.IsWaitingForOption
        && !Messages.IsReadingHoldActive && !P.IsModalOpen;
    static bool ModalReady => P != null && (bool)Get(P, "CanInteract");
    static RectTransform ModalRoot => Get(P, "modalRoot") as RectTransform;
    static Button CornerClose => Get(P, "letterScreenClose") as Button;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;
    public static string Start() => StartRun(false);
    public static string StartVideo() => StartRun(true);

    sealed class Frame
    {
        public int frame, episode, page, pageCount, envelopeOrder, nightOrder, redOrder, ordinaryOrder;
        public float time, dt, entranceClock, openClock, collectionClock, effectClock, partnerReady;
        public float envelopeAlpha, closeAlpha, sheetAlpha, redAlpha, envelopeAngle, partnerScreenError;
        public bool paused, presentationPaused, storyAvailable, settingsOpen, restoring, entering, opening, ready, unfolded, collecting, playerClicked;
        public bool envelopePressed, redPressed, ordinaryPressed, envelopeVisible, closeVisible, sheetVisible;
        public bool redActive, redCulled, redOnscreen, redVisible;
        public bool savedUnfolded, savedCollecting, savedCollected;
        public float[] envelopePosition, envelopeSize, paperPosition, paperScale, partnerPoint, redPoint, redBounds, ordinaryPoint, windowSize;
    }
    sealed class VideoFrame
    {
        public int index, frame, sourceWidth, sourceHeight;
        public float seconds;
        public string file;
    }
    sealed class SfxEvent
    {
        public string key;
        public int frame, episode;
        public float time, volume, pitch;
    }

    static string StartRun(bool video)
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameManager.Instance == null
            || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || SceneLoadManager.Instance.IsBusy
            || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning) return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to hash the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        data = GameDataManager.Instance; videoOnly = video; cancel = sampling = recording = false;
        lastFrame = -1; episode = 1; videoIndex = 0; samplingFailure = null; context = "initialization";
        checks.Clear(); issues.Clear(); screenshots.Clear(); observations.Clear(); frames.Clear(); videoFrames.Clear(); sfx.Clear(); actorFocus.Clear();
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", video ? "OSChapter03EnvelopeVideo" : "OSChapter03EnvelopeQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        if (video) Directory.CreateDirectory(Path.Combine(OutputDirectory, "frames"));
        Status = "running"; data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance; var original = data.SaveData;
        var language = data.CurrentLanguageState; var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume; var random = UnityEngine.Random.state; var previousMouse = Mouse.current;
        string[] paths = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null; var work = new Stack<IEnumerator>(); Exception failure = null;
        try
        {
            saves.EnterTestSession(); hashes = paths.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); data.SetCurrentLanguage(LanguageState.zh_CN); Set(setup, "isDemo", false);
            Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            mouse = InputSystem.AddDevice<Mouse>("Chapter03EnvelopeQAMouse"); MoveMouse(new Vector2(-10000f, -10000f));
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancel) throw new OperationCanceledException("Envelope QA cancelled");
                    if (samplingFailure != null) throw new InvalidOperationException("Envelope frame capture failed", samplingFailure);
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + failure;
                    string shot = Path.Combine(OutputDirectory, "failure.png"); screenshots.Add(shot); ScreenCapture.CaptureScreenshot(shot);
                    yield return null; yield return new WaitForSecondsRealtime(.25f);
                    Debug.LogError("[Chapter03 Envelope QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null)
            {
                Check(paths.Select(Hash).SequenceEqual(hashes), "original saves and scenes remain unchanged");
                Status = issues.Count == 0 ? "PASS" : "FAILED: " + issues.Count + " measured issues";
            }
        }
        finally
        {
            sampling = recording = false; RenderPipelineManager.endCameraRendering -= SampleFrame;
            if (story != null) story.DebugSfxPlayed -= OnSfxPlayed;
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                foreach (var pair in actorFocus) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                bool restoredActorFocus = actorFocus.All(pair => pair.Key == null || Equals(Get(pair.Key, "hasFocus"), pair.Value));
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                Messages?.NotifyOSHidden(); story?.ResetPresentation(); home?.Suspend();
                story?.DebugSetFocus(false); os?.Chapter03Entry?.DebugSetFocus(false);
                os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume
                    || resources != null && resources.enabled != resourcesEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse
                    || !UnityEngine.Random.state.Equals(random) || !restoredActorFocus)
                    throw new InvalidOperationException("Original save/settings/input/focus state was not restored");
                if (hashes != null && !paths.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save or scene files changed");
                checks.Add("cleanup restores original save, language, Demo, resources, audio, background, mouse, actor focus, random state and file hashes");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, videoOnly, checks, issues, observations, screenshots, frames, sfx,
                video = new { width = 1280, height = 720, requestedFPS = 30, frames = videoFrames },
                originalFiles = paths, originalHashes = hashes, finalHashes = paths.Select(Hash).ToArray(),
                note = "Muted isolated background Play Mode without Editor focus. Real laboratory checkpoint, 0710, bedside, diary, box/reunion and final-letter input. Only pre-envelope text/reading holds are completed immediately. Envelope, pointer, opening and pause clocks keep production timing. First, middle and last page X clicks close in the same callback; a cloned legacy collecting save also completes without replaying collection. Video uses full GameView captures with real timestamps, then point-filter downsampling after the performance; no captureFramerate or resolution changes."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks) + "\n" + string.Join("\n", issues));
            Debug.Log("[Chapter03 Envelope QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; memory = null; home = null; story = null; mouse = null; camera = null;
            memoryWindow = liveWindow = settingsWindow = null;
        }
    }

    static void Focus()
    {
        mouse?.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
        var actor = os?.CursorRelease;
        if (actor == null || GameAutosaveService.Instance?.IsTestSession != true) return;
        if (!actorFocus.ContainsKey(actor)) actorFocus.Add(actor, (bool)Get(actor, "hasFocus"));
        Set(actor, "hasFocus", true);
    }

    static IEnumerator PrepareLetterRoute()
    {
        context = "real laboratory console checkpoint";
        var console = GameTest.Instance.GetComponent<GameTestConsole>(); Check(console != null, "real console exists");
        console.SetOpen(true); yield return Settled();
        var entry = console.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == "第三章_实验室门口");
        Check(entry != null && entry.IsInteractable(), "real laboratory entrance row is available");
        entry.onClick.Invoke(); Check(GameTest.Instance.IsCheckpointJumpRunning, "laboratory console callback starts checkpoint");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "laboratory scene reload completes", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint succeeds in isolated storage");
        os = OS_Handle.Instance; memory = os.MemoryWindowController; home = memory.GetComponent<OSChapter03HomeController>();
        story = os.Chapter03Story; memoryWindow = memory.GetComponent<OSWindowUnit>();
        liveWindow = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        settingsWindow = os.transform.Find("Windows/WindowUnit_Option").GetComponent<OSWindowUnit>();
        camera = memoryWindow.GetComponentInParent<Canvas>().rootCanvas.worldCamera;
        yield return Wait(() => !console.IsOpen && story.IsAvailable && V.CurrentRoom?.id == "east"
            && (bool)Call(V, "AcceptsInput"), "laboratory entrance accepts real input");
        Check(S.liveOffline && S.Has("password_ready") && !S.Has("lab_open"), "entrance retains the locked laboratory and offline prerequisite");
        Call(V, "Click", V.CurrentRoom.FindDoor("lab_door").tile);
        yield return Drive(() => Head("keypad") && ModalReady, "real door opens its keypad");
        foreach (char digit in "0710") ModalButton("Digit" + digit).onClick.Invoke();
        ModalButton("Submit").onClick.Invoke();
        yield return Drive(() => V.CurrentRoom?.id == "lab" && Idle, "0710 and the real offline exchange enter the laboratory", 100f);
        ActivateThing("lab_bed");
        yield return Drive(() => P.ArtId == "bed-hand" && ModalReady, "first bedside illustration opens");
        ModalButton("Close").onClick.Invoke(); yield return Drive(() => Idle, "first bedside visit places the coffee");
        ActivateThing("lab_pillow_note"); yield return Drive(() => Idle, "diary thirteen and companion absorption finish", 120f);
        Check(S.papers.Contains("diary_13") && S.Has("dong_absorbed"), "the real diary callback completes the laboratory prerequisite");
        ActivateThing("lab_bed"); yield return Drive(() => S.Has("bed_second_done") && Idle, "second bedside sequence reveals the box", 120f);
        ActivateThing("lab_box"); yield return Drive(() => S.Has("live_returned") && S.Has("box_open") && Idle,
            "the box and all real reunion choices finish", 180f);
        Check(!S.liveOffline && !S.Has("letter_started"), "Live is reconnected and the final letter remains unopened");
    }

    static IEnumerator Run()
    {
        yield return PrepareLetterRoute();
        story.DebugSfxPlayed += OnSfxPlayed;
        sampling = true; RenderPipelineManager.endCameraRendering += SampleFrame;
        MoveMouse(new Vector2(Screen.width * .72f, Screen.height * .5f));
        ActivateThing("lab_letter");
        yield return Drive(() => P.LetterEnvelope != null, "real letter sequence creates the entering envelope", 60f);
        if (videoOnly) { yield return RunVideo(); yield break; }

        context = "envelope entrance and pause";
        yield return Wait(() => (float)Get(P, "letterEntranceClock") >= .2f, "envelope reaches an intermediate entrance frame");
        CheckLayers(); yield return CheckSettingsPause("entering");
        yield return Capture("01-envelope-entering.png");
        yield return Wait(() => P.IsLetterEnvelopeReady, "envelope completes its authored entrance");
        yield return CheckIdleTaps();
        CheckLetterSfx(false);
        yield return CheckSettingsPause("waiting for the player");
        yield return Capture("02-envelope-waits-for-player.png");
        yield return Roundtrip("before-opening");
        yield return Wait(() => P.IsLetterEnvelopeReady && (float)Get(P, "letterPartnerReady") < .98f,
            "restored envelope settles before the red pointer arrives");
        int openEpisode = episode;
        yield return ClickEnvelope();
        Verify((bool)Get(P, "letterPlayerClicked") && !P.IsLetterUnfolded && !(bool)Get(P, "letterOpening"),
            "an early player click waits for the red partner instead of opening alone");
        yield return Wait(() => P.IsLetterEnvelopePressed, "both pointers reach the envelope's shared press");
        yield return Capture("03-both-pointers-press-envelope.png");
        yield return Wait(() => P.IsLetterUnfolded && ModalReady, "paper rises and envelope falls through the complete opening");
        yield return Settled(); ValidateOpening(openEpisode);
        CheckLetterSfx(true);
        Verify(P.LetterPageCount > 1 && P.LetterPage == 0 && S.Has("letter_unfolded") && !S.Has("letter_collecting"),
            "opening produces the real multipage letter on its first page");
        Check(P.LetterPageCount > 2, "the authored Chinese letter has first, middle and last pages for close coverage");
        CheckLayers(); yield return Capture("04-letter-first-page.png");
        string firstPageSnapshot = CaptureSnapshot("first-page");
        yield return CloseLetterImmediately("first page");
        yield return RestoreSnapshot(firstPageSnapshot, "reopen first-page snapshot");
        yield return Wait(() => ModalReady, "restored first page accepts page input");
        yield return ClickButton(ModalButton("Next"), "real next-page button"); yield return null;
        Verify(P.LetterPage == 1 && !S.Has("letter_collected"), "the Next button still turns a page without closing the letter");
        yield return Roundtrip("page-two");
        yield return Wait(() => P.IsLetterUnfolded && ModalReady, "saved reading page restores directly as unfolded paper");
        Verify(P.LetterPage == 1 && P.LetterEnvelope != null && !P.LetterEnvelope.gameObject.activeInHierarchy
            && CornerClose != null && CornerClose.gameObject.activeInHierarchy, "page two restores with the corner X and no envelope entrance");
        yield return CheckSettingsPause("reading page two");
        yield return ClickOutside();
        Verify(P.IsLetterUnfolded && P.LetterPage == 1 && !S.Has("letter_collecting"), "clicking the outside shade leaves the letter open on the same page");
        yield return ClickButton(ModalButton("Previous"), "real previous-page button");
        Check(P.LetterPage == 0, "real Previous returns to the first page for the paper click test");
        yield return ClickPaper(); yield return null;
        Verify(P.LetterPage == 1 && !S.Has("letter_collected"), "clicking the paper itself turns to the next page");
        Check(P.LetterPage > 0 && P.LetterPage < P.LetterPageCount - 1, "the middle-page X test uses a nonterminal page");
        yield return CloseLetterImmediately("middle page");
        yield return RestoreSnapshot(firstPageSnapshot, "reopen snapshot for the last page");
        yield return Wait(() => ModalReady, "restored paper accepts last-page navigation");
        while (P.LetterPage < P.LetterPageCount - 1)
        {
            int previous = P.LetterPage; yield return ClickButton(ModalButton("Next"), "next-page navigation"); yield return null;
            Verify(P.LetterPage == previous + 1 && !S.Has("letter_collected"), "page navigation advances exactly one page and keeps the letter open");
        }
        yield return Capture("05-letter-last-page.png");
        string lastPageSnapshot = CaptureSnapshot("last-page");
        yield return CloseLetterImmediately("last page");
        yield return Wait(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Complete, "the original final delay completes Chapter Three", 10f);
        Verify(Mathf.Approximately(S.Progress, 1f) && data.CurrentChapterIndex == 3, "closing the letter completes the chapter without entering unwritten Chapter Four");
        yield return Capture("06-letter-closed.png");
        yield return RestoreLegacyCollection(lastPageSnapshot);
        yield return Capture("07-legacy-collection-restored-complete.png");
        sampling = false; RenderPipelineManager.endCameraRendering -= SampleFrame;
        ValidateEntrance(1);
        Verify(frames.All(frame => !frame.collecting && frame.collectionClock == 0f),
            "neither current X clicks nor the legacy save play a fold or flight collection animation");
        ValidatePointerLayers();
        CheckLetterSfx(true);
    }

    static IEnumerator RunVideo()
    {
        context = "natural envelope video";
        yield return Wait(() => P.IsLetterEnvelopeReady, "video envelope finishes its entrance");
        yield return Wait(() => (float)Get(story, "effectClock") >= 6.3f, "video records two unprompted red-pointer taps", 15f);
        CheckLetterSfx(false);
        yield return ClickEnvelope();
        yield return Wait(() => P.IsLetterUnfolded && ModalReady, "video records the complete natural opening");
        while (P.LetterPage < P.LetterPageCount - 1)
        {
            yield return ActiveHold(1.8f); yield return ClickPaper();
        }
        yield return ActiveHold(2.1f); yield return CloseLetterImmediately("video final page");
        yield return ActiveHold(.6f);
        recording = sampling = false; RenderPipelineManager.endCameraRendering -= SampleFrame;
        ValidateEntrance(1); ValidateOpening(1);
        Verify(frames.All(frame => !frame.collecting && frame.collectionClock == 0f), "video closes the letter without a fold or flight animation");
        ValidatePointerLayers();
        CheckLetterSfx(true);
        yield return FinishVideoFrames();
    }

    static void OnSfxPlayed(string key, float volume, float pitch)
    {
        sfx.Add(new SfxEvent { key = key, frame = Time.frameCount, episode = episode,
            time = Time.realtimeSinceStartup, volume = volume, pitch = pitch });
    }
    static void CheckLetterSfx(bool opened)
    {
        Verify(sfx.Count(item => item.key == "Chapter02_PaperRustle") == 1,
            "the actual envelope entrance emits exactly one paper-rustle sound, including across restore");
        Verify(sfx.Count(item => item.key == "Chapter03_EnvelopeOpen") == (opened ? 1 : 0), opened
            ? "one player opening emits exactly one envelope-open sound without replay on page or legacy-save restore"
            : "the waiting red-pointer taps never emit the envelope-open sound");
    }

    static IEnumerator Roundtrip(string label)
    {
        yield return RestoreSnapshot(CaptureSnapshot(label), label);
    }
    static string CaptureSnapshot(string label)
    {
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true); File.WriteAllText(Path.Combine(OutputDirectory, label + "-save.json"), json);
        return json;
    }
    static IEnumerator RestoreSnapshot(string json, string label, bool legacyCollecting = false)
    {
        context = "JSON/session restore: " + label;
        var restored = JsonUtility.FromJson<GameSaveData>(json); var expected = restored.osProcess.memory.chapter03Story;
        int page = expected.letterPage; string[] flags = expected.flags.ToArray();
        Messages.NotifyOSHidden(); story.ResetPresentation(); home.Suspend(); data.ReplaceSaveData(restored); episode++;
        yield return OSSessionPersistence.Restore(restored);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        yield return Wait(() => story.IsAvailable && (legacyCollecting ? S.Has("letter_collected") && !P.IsModalOpen : P.IsModalOpen && Head("letter")),
            legacyCollecting ? "legacy collection restores directly as completed" : "actual session restore rebuilds the letter beat");
        Check(ReferenceEquals(S, expected) && flags.Where(flag => !legacyCollecting || flag != "letter_collecting").All(S.Has)
            && (legacyCollecting || S.letterPage == page),
            "JSON restoration retains letter flags and the saved reading page: " + label);
        observations.Add(new { context, episode, savedPage = page, legacyCollecting, flags });
    }
    static IEnumerator RestoreLegacyCollection(string lastPageSnapshot)
    {
        // Model the obsolete save state in a detached JSON clone. Current input
        // cannot produce this flag and the active story is never edited in place.
        var legacy = JsonUtility.FromJson<GameSaveData>(lastPageSnapshot);
        var legacyStory = legacy.osProcess.memory.chapter03Story;
        Check(legacyStory.Has("letter_unfolded") && !legacyStory.Has("letter_collected")
            && legacyStory.memoryQueue.Any(beat => beat.type == "letter" && !beat.complete),
            "legacy compatibility starts from the actual last-page reading snapshot");
        legacyStory.Add("letter_collecting");
        string json = JsonUtility.ToJson(legacy, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "legacy-collecting-save.json"), json);
        yield return RestoreSnapshot(json, "legacy collecting", true);
        int legacyEpisode = episode;
        Check(S.Has("letter_collected") && !P.IsModalOpen && !P.IsAnimating
            && !os.CursorRelease.ScriptActorIsMoving && !Equals(Get(os.CursorRelease, "scriptActorPressed"), true),
            "an old collecting save immediately completes the letter without rebuilding a visible collection");
        yield return Wait(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Complete,
            "the compatible old save reaches Chapter Three completion", 10f);
        Verify(frames.Where(frame => frame.episode == legacyEpisode && !frame.restoring)
            .All(frame => !frame.envelopeVisible && !frame.sheetVisible && !frame.closeVisible && !frame.collecting),
            "no restored old-collection frame shows an envelope, paper, X or collection animation");
        Verify(Mathf.Approximately(S.Progress, 1f) && data.CurrentChapterIndex == 3,
            "the legacy save finishes with full progress inside Chapter Three");
    }

    static IEnumerator CheckSettingsPause(string label)
    {
        context = "settings pause: " + label;
        settingsWindow.OpenStandalone(0f);
        yield return Wait(() => P.Paused && Ready(settingsWindow), "settings opens and pauses the real presentation");
        yield return null;
        float entrance = (float)Get(P, "letterEntranceClock"), opening = (float)Get(P, "letterOpenClock");
        float effect = (float)Get(story, "effectClock"), collect = (float)Get(P, "letterCollectionClock");
        Vector3 position = P.LetterEnvelope != null ? P.LetterEnvelope.localPosition : Vector3.zero;
        int page = P.LetterPage;
        if (label == "entering") Verify((bool)Get(P, "letterEntering"), "the entrance pause is exercised before the envelope settles");
        yield return Capture("paused-" + label.Replace(' ', '-') + ".png");
        float until = Time.realtimeSinceStartup + .45f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Verify(Mathf.Abs((float)Get(P, "letterEntranceClock") - entrance) < .001f
            && Mathf.Abs((float)Get(P, "letterOpenClock") - opening) < .001f
            && Mathf.Abs((float)Get(story, "effectClock") - effect) < .001f
            && Mathf.Abs((float)Get(P, "letterCollectionClock") - collect) < .001f && P.LetterPage == page
            && (P.LetterEnvelope == null || Vector3.Distance(P.LetterEnvelope.localPosition, position) < .001f),
            "settings freezes envelope, red-pointer, collection clocks and reading page during " + label);
        Verify(RenderedAlpha(P.LetterEnvelope?.GetComponent<Graphic>()) <= .001f
            && RenderedButtonAlpha(CornerClose) <= .001f
            && RenderedAlpha(Get(P, "paperSheet") as Graphic) <= .001f,
            "settings hides the actual envelope, corner X and paper despite the envelope's overriding Canvas");
        settingsWindow.CloseSilently(); yield return Wait(() => !P.Paused && story.IsAvailable, "closing settings resumes the real letter");
        yield return Settled();
        Verify(P.IsModalOpen && (P.IsLetterUnfolded ? RenderedAlpha(Get(P, "paperSheet") as Graphic) > .99f
            && RenderedButtonAlpha(CornerClose) > .99f : RenderedAlpha(P.LetterEnvelope?.GetComponent<Graphic>()) > .99f),
            "the same letter presentation becomes visible again after settings: " + label);
    }

    static IEnumerator CheckIdleTaps()
    {
        context = "red pointer waits for the player";
        int start = Time.frameCount;
        yield return Wait(() => (float)Get(story, "effectClock") >= 6.1f, "unopened envelope records two authored idle taps", 15f);
        var waiting = frames.Where(frame => frame.frame >= start && frame.episode == episode && frame.ready && !frame.paused).ToArray();
        var taps = waiting.Where((frame, index) => frame.redPressed && (index == 0 || !waiting[index - 1].redPressed)).ToArray();
        Verify(waiting.Length > 0 && waiting.All(frame => !frame.unfolded && !frame.opening && !frame.playerClicked)
            && !S.Has("letter_unfolded"), "waiting and red-pointer hints never open the envelope without a player click");
        Verify(taps.Length >= 2 && taps[0].effectClock >= 3f && taps[0].effectClock <= 3.15f
            && Mathf.Abs(taps[1].effectClock - taps[0].effectClock - 2.6f) <= .15f,
            "the red pointer first hints after three seconds and repeats 2.6 seconds later");
        var held = waiting.Where(frame => frame.partnerReady >= .98f).ToArray();
        Verify(held.Length > 0 && held.All(frame => frame.partnerScreenError <= LogicalPixelScreenWidth() + 1f),
            "the ready red pointer stays at the wax-seal partner point within normal pixel snapping");
        observations.Add(new { context, taps = taps.Select(frame => new { frame.frame, frame.effectClock }).ToArray() });
    }

    static void CheckLayers()
    {
        var envelopeCanvas = EffectiveCanvas(P.LetterEnvelope); var night = os.GetComponent<OSChapter02NightfallPresentation>();
        var nightCanvas = (Get(night, "overlay") as Component)?.GetComponentInParent<Canvas>();
        var red = Get(os.CursorRelease, "cursorImage") as Image;
        var ordinary = Get(CursorHandle.Instance, "cursorIcon") as Image;
        Verify(envelopeCanvas != null && envelopeCanvas.overrideSorting && envelopeCanvas.sortingOrder == 1201
            && nightCanvas != null && nightCanvas.sortingOrder == 1200 && night.IsNight,
            "the envelope is above the active 1200 night mask on its own 1201 Canvas");
        Verify(EffectiveCanvas(red?.transform)?.sortingOrder == 1202
            && EffectiveCanvas(ordinary?.transform)?.sortingOrder > 1201,
            "both the 1202 red pointer and the ordinary cursor render above the envelope");
        Verify((Get(P, "modalShade") as OSDitherFill)?.Level == 7, "the envelope uses the authored seven-level outside shade");
        Verify((Get(P, "closeButton") as Button)?.gameObject.activeSelf == false,
            "the paper's old small close button stays hidden");
        if (CornerClose != null) Verify(EffectiveCanvas(CornerClose.transform)?.sortingOrder == 1201,
            "the new corner X shares the envelope's 1201 layer");
    }

    static void SampleFrame(ScriptableRenderContext renderContext, Camera renderedCamera)
    {
        if (!sampling || samplingFailure != null || renderedCamera != camera || lastFrame == Time.frameCount || P == null) return;
        lastFrame = Time.frameCount;
        try
        {
            var envelope = P.LetterEnvelope;
            var sheet = Get(P, "paperSheet") as Graphic;
            var red = Get(os.CursorRelease, "cursorImage") as Image;
            var ordinary = Get(CursorHandle.Instance, "cursorIcon") as Image;
            var night = os.GetComponent<OSChapter02NightfallPresentation>();
            var nightCanvas = (Get(night, "overlay") as Component)?.GetComponentInParent<Canvas>();
            Vector2 partner = P.LetterPartnerPoint != null ? RectTransformUtility.WorldToScreenPoint(camera, P.LetterPartnerPoint.position) : Vector2.zero;
            Vector2 redPoint = red != null ? RectTransformUtility.WorldToScreenPoint(camera, red.rectTransform.position) : Vector2.zero;
            var windows = Get(P, "windowParent") as RectTransform;
            float envelopeAlpha = RenderedAlpha(envelope?.GetComponent<Graphic>());
            float closeAlpha = RenderedButtonAlpha(CornerClose), sheetAlpha = RenderedAlpha(sheet);
            float redAlpha = RenderedAlpha(red);
            Rect redBounds = red != null ? ScreenBounds(red.rectTransform) : default;
            bool redActive = red != null && red.isActiveAndEnabled;
            bool redCulled = red != null && red.canvasRenderer.cull;
            bool redOnscreen = red != null && redBounds.Overlaps(camera.pixelRect);
            bool presentationPaused = P.Paused, storyAvailable = story.IsAvailable;
            var frame = new Frame {
                frame = Time.frameCount, episode = episode, time = Time.realtimeSinceStartup,
                dt = !presentationPaused && storyAvailable ? Mathf.Min(Time.unscaledDeltaTime, .1f) : 0f,
                // A settings click can precede the director's next Update in
                // this rendered frame. Its availability guard already pauses
                // gameplay; retain both raw states instead of calling it active.
                paused = presentationPaused || !storyAvailable, presentationPaused = presentationPaused, storyAvailable = storyAvailable,
                settingsOpen = settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized,
                restoring = OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true,
                entering = Equals(Get(P, "letterEntering"), true), opening = Equals(Get(P, "letterOpening"), true),
                collecting = Equals(Get(P, "letterCollecting"), true), playerClicked = Equals(Get(P, "letterPlayerClicked"), true),
                ready = P.IsLetterEnvelopeReady, unfolded = P.IsLetterUnfolded,
                entranceClock = (float)Get(P, "letterEntranceClock"), openClock = (float)Get(P, "letterOpenClock"),
                collectionClock = (float)Get(P, "letterCollectionClock"), effectClock = (float)Get(story, "effectClock"),
                partnerReady = (float)Get(P, "letterPartnerReady"), page = P.LetterPage, pageCount = P.LetterPageCount,
                savedUnfolded = S.Has("letter_unfolded"), savedCollecting = S.Has("letter_collecting"), savedCollected = S.Has("letter_collected"),
                envelopePressed = P.IsLetterEnvelopePressed, redPressed = Equals(Get(os.CursorRelease, "scriptActorPressed"), true),
                ordinaryPressed = CursorHandle.Instance.CurrentState == CursorVisualState.Pressed,
                envelopeAlpha = envelopeAlpha, closeAlpha = closeAlpha, sheetAlpha = sheetAlpha,
                redAlpha = redAlpha, redActive = redActive, redCulled = redCulled, redOnscreen = redOnscreen,
                redVisible = redActive && !redCulled && redOnscreen && redAlpha > .001f,
                envelopeVisible = envelopeAlpha > .99f, closeVisible = closeAlpha > .99f, sheetVisible = sheetAlpha > .99f,
                envelopeOrder = EffectiveCanvas(envelope)?.sortingOrder ?? -1,
                nightOrder = nightCanvas != null ? nightCanvas.sortingOrder : -1,
                redOrder = EffectiveCanvas(red?.transform)?.sortingOrder ?? -1,
                ordinaryOrder = EffectiveCanvas(ordinary?.transform)?.sortingOrder ?? -1,
                envelopePosition = envelope != null ? Pair(envelope.anchoredPosition) : null,
                envelopeSize = envelope != null ? Pair(envelope.rect.size) : null,
                envelopeAngle = envelope != null ? Mathf.DeltaAngle(0f, envelope.localEulerAngles.z) : 0f,
                paperPosition = P.PaperRoot != null ? Pair(P.PaperRoot.anchoredPosition) : null,
                paperScale = P.PaperRoot != null ? Pair(P.PaperRoot.localScale) : null,
                partnerPoint = P.LetterPartnerPoint != null ? Pair(partner) : null,
                redPoint = red != null ? Pair(redPoint) : null,
                redBounds = red != null ? new[] { redBounds.x, redBounds.y, redBounds.width, redBounds.height } : null,
                ordinaryPoint = Pair(CursorHandle.Instance.DisplayedScreenPosition),
                partnerScreenError = P.LetterPartnerPoint != null && red != null ? Vector2.Distance(partner, redPoint) : -1f,
                windowSize = windows != null ? Pair(windows.rect.size) : null
            };
            frames.Add(frame);
            if (videoOnly && !recording && videoFrames.Count == 0 && envelope != null)
            { recording = true; videoStart = nextVideoAt = Time.realtimeSinceStartup; }
            if (recording && Time.realtimeSinceStartup >= nextVideoAt)
            {
                string path = Path.Combine(OutputDirectory, "frames", videoIndex.ToString("D6") + ".png");
                videoFrames.Add(new VideoFrame { index = videoIndex++, frame = Time.frameCount,
                    seconds = Time.realtimeSinceStartup - videoStart, sourceWidth = Screen.width, sourceHeight = Screen.height, file = path });
                // CaptureScreenshot completes after the whole GameView, including
                // overlay cursor canvases. Do not read a half-rendered camera here.
                ScreenCapture.CaptureScreenshot(path);
                nextVideoAt += 1f / 30f;
                if (nextVideoAt < Time.realtimeSinceStartup) nextVideoAt = Time.realtimeSinceStartup + 1f / 30f;
            }
        }
        catch (Exception error) { samplingFailure = error; }
    }

    static void ValidateEntrance(int observedEpisode)
    {
        context = "measured envelope entrance";
        var all = frames.Where(frame => frame.episode == observedEpisode && frame.envelopePosition != null && !frame.restoring).ToArray();
        var entering = all.Where(frame => frame.entering && !frame.paused).ToArray();
        var settled = all.FirstOrDefault(frame => frame.ready && !frame.paused);
        Verify(entering.Length >= 3 && settled != null && entering[0].entranceClock <= .15f
            && settled.entranceClock >= .75f && settled.entranceClock <= .9f,
            "the envelope's rendered entrance traverses the authored 0.75-second interval");
        if (entering.Length == 0 || settled == null) return;
        float activeSeconds = all.Where(frame => frame.frame > entering[0].frame && frame.frame <= settled.frame).Sum(frame => frame.dt)
            + entering[0].entranceClock;
        float maximumPositionError = 0f, maximumAngleError = 0f;
        foreach (var frame in entering)
        {
            float t = Mathf.Clamp01(frame.entranceClock / .75f), ease = 1f - Mathf.Pow(1f - t, 3f);
            float startY = -(frame.windowSize[1] + frame.envelopeSize[1]) * .5f - 16f;
            Vector2 expected = Vector2.Lerp(new Vector2(56f, startY), Vector2.zero, ease);
            maximumPositionError = Mathf.Max(maximumPositionError, Vector2.Distance(new Vector2(frame.envelopePosition[0], frame.envelopePosition[1]), expected));
            maximumAngleError = Mathf.Max(maximumAngleError, Mathf.Abs(frame.envelopeAngle - Mathf.Lerp(-12f, 0f, ease)));
        }
        Verify(Mathf.Abs(activeSeconds - .75f) <= .15f && maximumPositionError <= 1.5f && maximumAngleError <= .2f,
            "real rendered entrance moves from below-right by 56 pixels and -12 degrees to the center with natural timing");
        Verify(Mathf.Abs(settled.envelopePosition[0]) <= .6f && Mathf.Abs(settled.envelopePosition[1]) <= .6f
            && Mathf.Abs(settled.envelopeAngle) <= .01f, "the settled envelope is centered and upright");
        Verify(entering.All(frame => frame.envelopeOrder == 1201 && frame.nightOrder == 1200
            && (!frame.redVisible || frame.redOrder == 1202) && frame.ordinaryOrder > 1201),
            "all sampled entrance frames keep the envelope and visible cursors above their intended layers");
        observations.Add(new { context, observedEpisode, activeSeconds, maximumPositionError, maximumAngleError,
            firstFrame = entering[0].frame, settledFrame = settled.frame, samples = entering.Length });
    }

    static void ValidateOpening(int observedEpisode)
    {
        context = "measured simultaneous press and opening";
        var all = frames.Where(frame => frame.episode == observedEpisode && !frame.paused && !frame.restoring).ToArray();
        var opening = all.Where(frame => frame.opening).ToArray();
        var pressed = opening.Where(frame => frame.envelopePressed).ToArray();
        var firstPaper = all.FirstOrDefault(frame => frame.sheetVisible && frame.openClock >= .18f && !frame.collecting);
        var unfolded = all.FirstOrDefault(frame => frame.unfolded && !frame.collecting);
        Verify(pressed.Length >= 2 && pressed.All(frame => frame.redPressed && frame.ordinaryPressed),
            "the envelope, red pointer and ordinary cursor are pressed together in every rendered shared-press frame");
        Verify(pressed.Length > 0 && pressed.All(frame => frame.openClock < .18f
            && frame.envelopePosition != null && Mathf.Abs(frame.envelopePosition[1] + 1f) <= .6f),
            "the shared 0.18-second press lowers the envelope by one logical pixel");
        Verify(firstPaper != null && firstPaper.closeVisible && firstPaper.openClock <= .32f,
            "the screen-corner X is visible on the very first rendered frame where the paper rises");
        Verify(unfolded != null && unfolded.openClock >= 1.05f && unfolded.openClock <= 1.2f
            && opening.Length >= 5 && opening[0].openClock <= .15f,
            "the complete shared press, paper rise and envelope drop retain their authored 1.05 seconds");
        if (opening.Length == 0 || unfolded == null) return;
        float activeSeconds = all.Where(frame => frame.frame > opening[0].frame && frame.frame <= unfolded.frame).Sum(frame => frame.dt)
            + opening[0].openClock;
        float riseError = 0f, fallError = 0f;
        foreach (var frame in opening.Where(frame => frame.openClock >= .18f && frame.paperPosition != null))
        {
            float t = Mathf.Clamp01((frame.openClock - .18f) / (1.05f - .18f));
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .7f));
            riseError = Mathf.Max(riseError, Mathf.Abs(frame.paperPosition[1] - (-34f * (1f - rise))));
            float drop = (frame.windowSize[1] + frame.envelopeSize[1]) * .5f + 24f;
            fallError = Mathf.Max(fallError, Vector2.Distance(new Vector2(frame.envelopePosition[0], frame.envelopePosition[1]),
                new Vector2(22f * t * t, -drop * t * t)));
        }
        Verify(Mathf.Abs(activeSeconds - 1.05f) <= .15f && riseError <= 1f && fallError <= 1.5f,
            "rendered paper rises through 34 pixels while the envelope drops below the screen with its authored trajectory");
        Verify(!unfolded.envelopeVisible && unfolded.sheetVisible && unfolded.closeVisible
            && unfolded.paperPosition != null && Mathf.Abs(unfolded.paperPosition[1]) <= .6f,
            "opening ends with centered paper and the corner X while the envelope is hidden");
        var mismatches = pressed.Where(frame => !frame.redPressed || !frame.ordinaryPressed)
            .Select(frame => new { frame.frame, frame.openClock, frame.envelopePressed, frame.redPressed, frame.ordinaryPressed }).ToArray();
        observations.Add(new { context, observedEpisode, activeSeconds, riseError, fallError, pressSamples = pressed.Length,
            firstPressFrame = pressed.FirstOrDefault()?.frame, firstSheetFrame = firstPaper?.frame,
            firstCloseFrame = all.FirstOrDefault(frame => frame.closeVisible)?.frame, pressMismatches = mismatches });
    }

    static void ValidatePointerLayers()
    {
        context = "visible red-pointer layers across settings and restores";
        var active = frames.Where(frame => !frame.paused && !frame.restoring && (frame.envelopeVisible || frame.sheetVisible)).ToArray();
        var visible = active.Where(frame => frame.redVisible).ToArray();
        var faults = visible.Where(frame => frame.redOrder != 1202).ToArray();
        Verify(visible.Length > 0 && faults.Length == 0,
            "every active onscreen red-pointer frame stays above the envelope on layer 1202, including after settings resumes");
        observations.Add(new { context, activeFrames = active.Length, visibleRedFrames = visible.Length,
            pauseRequestBoundaryFrames = frames.Count(frame => frame.settingsOpen && !frame.storyAvailable && !frame.presentationPaused),
            inactiveRedFrames = active.Count(frame => !frame.redActive), transparentRedFrames = active.Count(frame => frame.redAlpha <= .001f),
            culledRedFrames = active.Count(frame => frame.redCulled), offscreenRedFrames = active.Count(frame => !frame.redOnscreen),
            incorrectVisibleLayerFrames = faults.Length,
            examples = faults.Take(12).Select(frame => new { frame.frame, frame.episode, frame.redOrder, frame.redAlpha,
                frame.redActive, frame.redCulled, frame.redOnscreen, frame.redBounds, frame.redPoint,
                frame.entering, frame.ready, frame.opening, frame.unfolded, frame.collecting }).ToArray(),
            note = "Active gameplay requires both story availability and an unpaused presentation. Raw settings, availability and presentation-paused states preserve request-boundary evidence. Visibility means an active, nontransparent, nonculled Graphic intersecting the screen before sorting occlusion; a low layer still fails even when the envelope hides its pixels." });
    }

    static IEnumerator FinishVideoFrames()
    {
        context = "finalize captured video frames";
        Check(videoFrames.Count > 0, "the natural video recorded full GameView frames");
        yield return Wait(() => videoFrames.All(frame => File.Exists(frame.file) && new FileInfo(frame.file).Length > 100),
            "all asynchronous full GameView screenshots finish writing", 40f);
        // Do not spend GPU/PNG conversion time during the performance itself.
        // Point sampling retains the original pixel edges at exactly 1280x720.
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var target = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var render = RenderTexture.GetTemporary(1280, 720, 0, RenderTextureFormat.ARGB32);
        source.filterMode = target.filterMode = render.filterMode = FilterMode.Point;
        var previous = RenderTexture.active;
        try
        {
            for (int i = 0; i < videoFrames.Count; i++)
            {
                var frame = videoFrames[i];
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(frame.file)))
                    throw new IOException("Captured PNG could not decode: " + frame.index);
                Graphics.Blit(source, render); RenderTexture.active = render;
                target.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0); target.Apply(false, false);
                File.WriteAllBytes(frame.file, target.EncodeToPNG());
                if (i % 4 == 3) { RenderTexture.active = previous; yield return null; }
            }
        }
        finally
        {
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(render);
            UnityEngine.Object.Destroy(source); UnityEngine.Object.Destroy(target);
        }
        File.WriteAllText(Path.Combine(OutputDirectory, "video-frames.json"), JsonConvert.SerializeObject(new {
            width = 1280, height = 720, requestedFPS = 30, frames = videoFrames,
            note = "Times are actual realtime capture-request timestamps. Use variable durations before converting the encoded output to 30 FPS."
        }, Formatting.Indented));
        observations.Add(new { context, videoFrames = videoFrames.Count,
            duration = videoFrames.Last().seconds, measuredFPS = (videoFrames.Count - 1) / Mathf.Max(.001f, videoFrames.Last().seconds),
            sourceWidth = videoFrames[0].sourceWidth, sourceHeight = videoFrames[0].sourceHeight, outputWidth = 1280, outputHeight = 720 });
        Check(videoFrames.Count >= 30, "the complete natural performance has at least thirty captured frames");
    }

    static IEnumerator ClickEnvelope()
    {
        Check(P.LetterEnvelope != null && P.IsLetterEnvelopeReady, "the real envelope is ready for a player click");
        yield return ClickHit(P.LetterEnvelope.GetComponent<OSChapter03PresentationHit>(), Centre(P.LetterEnvelope), "envelope");
    }
    static IEnumerator CloseLetterImmediately(string label)
    {
        context = "immediate letter close: " + label;
        Check(Head("letter") && P.IsLetterUnfolded && ModalReady, "the letter accepts its corner X on " + label);
        var beat = S.memoryQueue[0]; var root = ModalRoot;
        int page = P.LetterPage, pages = P.LetterPageCount;
        yield return ClickButton(CornerClose, "screen-corner X on " + label, () =>
        {
            int clickFrame = Time.frameCount;
            Check(!P.IsModalOpen && P.PaperRoot == null && P.LetterEnvelope == null && !P.IsAnimating
                && (root == null || !root.gameObject.activeInHierarchy) && S.Has("letter_collected")
                && !S.Has("letter_collecting") && beat.complete && !(bool)Get(P, "letterCollecting"),
                "the same X-click callback hides the entire letter and commits completion on " + label);
            Check(!os.CursorRelease.ScriptActorIsMoving && !Equals(Get(os.CursorRelease, "scriptActorPressed"), true),
                "closing immediately releases the red pointer's press and stops its old reading movement");
            observations.Add(new { context, type = "same-callback letter close", clickFrame, observedFrame = Time.frameCount,
                episode, page, pageCount = pages, modalOpen = P.IsModalOpen, collected = S.Has("letter_collected"),
                beatComplete = beat.complete, actorMoving = os.CursorRelease.ScriptActorIsMoving });
        });
        Verify(!P.IsModalOpen && !P.IsAnimating && !(bool)Get(P, "letterCollecting"),
            "the next frame keeps the letter closed without starting a collection animation");
    }
    static IEnumerator ClickPaper()
    {
        var sheet = Get(P, "paperSheet") as Graphic; Check(sheet != null, "the real paper sheet exists");
        var rect = sheet.rectTransform;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector2(rect.rect.xMin + 7f, 0f)));
        yield return ClickHit(sheet.GetComponent<OSChapter03PresentationHit>(), point, "letter paper");
    }
    static IEnumerator ClickOutside()
    {
        var hit = ModalRoot.Find("Outside")?.GetComponent<OSChapter03PresentationHit>();
        yield return ClickHit(hit, new Vector2(Screen.width * .08f, Screen.height * .2f), "outside shade");
    }
    static IEnumerator ClickHit(OSChapter03PresentationHit hit, Vector2 point, string label)
    {
        Check(hit != null, "real pointer relay exists: " + label); MoveMouse(point); yield return null;
        var target = RaycastClickTarget(point, label);
        Check(target == hit.gameObject, "the topmost screen-coordinate raycast reaches " + label);
        ExecuteEvents.Execute(target, Pointer(point), ExecuteEvents.pointerClickHandler); yield return null;
    }
    static IEnumerator ClickButton(Button button, string label, Action afterClick = null)
    {
        Check(button != null && button.IsInteractable() && button.gameObject.activeInHierarchy, "real button is available: " + label);
        Vector2 point = Centre((RectTransform)button.transform); MoveMouse(point); yield return null;
        var target = RaycastClickTarget(point, label);
        Check(target == button.gameObject, "the topmost screen-coordinate raycast reaches " + label);
        ExecuteEvents.Execute(target, Pointer(point), ExecuteEvents.pointerClickHandler);
        afterClick?.Invoke(); yield return null;
    }
    static GameObject RaycastClickTarget(Vector2 point, string label)
    {
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(Pointer(point), hits);
        observations.Add(new { context, type = "real screen-coordinate raycast", label, point = Pair(point),
            hits = hits.Take(6).Select(hit => new { name = hit.gameObject?.name, hit.sortingOrder, hit.depth, camera = hit.module?.eventCamera?.name }).ToArray() });
        Check(hits.Count > 0, "the actual UI raycast hits a graphic: " + label);
        return ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
    }
    static PointerEventData Pointer(Vector2 point) => new PointerEventData(EventSystem.current)
        { position = point, button = PointerEventData.InputButton.Left, clickCount = 1 };
    static void MoveMouse(Vector2 point)
    { mouse?.MakeCurrent(); if (mouse != null) InputState.Change(mouse, new MouseState { position = point }, InputState.currentUpdateType); }
    static Vector2 Centre(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
    static Rect ScreenBounds(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
    }
    static float LogicalPixelScreenWidth()
    {
        var root = (RectTransform)memoryWindow.GetComponentInParent<Canvas>().rootCanvas.transform;
        return Vector2.Distance(RectTransformUtility.WorldToScreenPoint(camera, root.TransformPoint(Vector3.zero)),
            RectTransformUtility.WorldToScreenPoint(camera, root.TransformPoint(Vector3.right)));
    }
    static Canvas EffectiveCanvas(Transform node) => node?.GetComponentsInParent<Canvas>(true)
        .FirstOrDefault(item => item.isActiveAndEnabled && (item.isRootCanvas || item.overrideSorting));
    static float RenderedAlpha(Graphic graphic) => graphic != null && graphic.isActiveAndEnabled
        ? graphic.color.a * graphic.canvasRenderer.GetInheritedAlpha() : 0f;
    static float RenderedButtonAlpha(Button button) => button != null
        ? button.GetComponentsInChildren<Graphic>(true).Select(RenderedAlpha).DefaultIfEmpty(0f).Max() : 0f;
    static void ActivateThing(string id)
    {
        Check(story.IsAvailable && !story.BlocksHomeInput, "real story input accepts " + id);
        var thing = V.CurrentRoom.FindThing(id); Check(thing != null && !thing.hidden, "real visible thing exists: " + id);
        Call(story, "OnThing", thing);
    }
    static Button ModalButton(string name)
    {
        var frame = Get(P, "modalFrame") as RectTransform;
        var button = frame?.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == name);
        Check(button != null, "real modal button exists: " + name); return button;
    }
    static void Pump()
    {
        // Once the envelope exists, its entire performance runs at natural speed.
        if (P.LetterEnvelope != null || S.Has("letter_unfolded")) return;
        foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
        string id = Messages.DisplayedEntryId;
        if (OSChapter03StoryDirector.IsOwnEntry(id))
        {
            Messages.TryCompleteDisplayedTextImmediately(id); Messages.TryCompleteDisplayedOptionTextImmediately(id);
            if (Messages.IsWaitingForOption)
            {
                var entry = Get(Messages, "activeEntry") as OSCharacterInteractionEntryData;
                Check(entry?.Options != null && entry.Options.Count > 0 && Messages.TrySelectDisplayedOptionImmediately(
                    Messages.DisplayedSequenceId, entry.EntryId, entry.Options[0].OptionId), "real pre-letter Live choice is selected");
            }
            else if (Messages.IsReadingHoldActive)
            {
                var timer = Get(Messages, "autoCloseTween") as Tween;
                if (timer != null && timer.IsActive()) timer.Complete(true);
            }
        }
        if (Head("paper") && ModalReady) ModalButton("Close").onClick.Invoke();
        if (Head("art") && P.ArtId == "hairclip" && ModalReady) ModalButton("Close").onClick.Invoke();
    }
    static bool Head(string type) => S.memoryQueue.Count > 0 && S.memoryQueue[0].type == type;
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing
        && !window.IsMinimized && window.gameObject.activeInHierarchy;
    static IEnumerator Drive(Func<bool> predicate, string label, float seconds = 70f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { Pump(); yield return null; }
        Check(predicate(), label);
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static IEnumerator ActiveHold(float seconds)
    {
        float elapsed = 0f, deadline = Time.realtimeSinceStartup + seconds * 5f + 5f;
        while (elapsed < seconds && Time.realtimeSinceStartup < deadline)
        { yield return null; if (story.IsAvailable && !P.Paused) elapsed += Mathf.Min(Time.unscaledDeltaTime, .1f); }
        Check(elapsed >= seconds, "natural active hold finishes: " + seconds);
    }
    static IEnumerator Settled() { Canvas.ForceUpdateCanvases(); yield return null; yield return null; }
    static IEnumerator Capture(string name)
    {
        string path = Path.Combine(OutputDirectory, name); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 100, "screenshot written: " + name, 10f);
    }
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static void Verify(bool value, string label)
    { if (value) checks.Add(context + ": " + label); else if (!issues.Contains(label)) issues.Add(label); }
    static float[] Pair(Vector2 value) => new[] { value.x, value.y };
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
