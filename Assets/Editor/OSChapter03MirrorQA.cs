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

// Opt-in, isolated Play Mode regression for the bathroom mirror. Keeps Memory
// pinned and exercises the real paper, story queues, window APIs and restoration.
// Never focuses the Editor or changes production drawing/sorting/animation state.
public static class OSChapter03MirrorQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const float ExpectedPerformanceZoom = 1.075f;
    const float ExpectedMirrorPointerTravelSeconds = .62f / .75f;
    const float ExpectedPerformanceZoomInSeconds = 15f;
    const float ExpectedBreakdownMusicFadeSeconds = 6f;
    static readonly List<string> checks = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<object> entryMotionFrames = new List<object>();
    static GameDataManager data;
    static GameSetup setup;
    static Mouse testMouse;
    static Keyboard testKeyboard;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSWindowUnit memoryWindow, liveWindow, settingsWindow;
    static bool cancel, bedroomCheckpoint, cursorProjection, labEntranceCheckpoint;
    static bool mirrorEntryMotion, assertEntryMotionStable, entryMotionRecording, entryMotionFinalCapture;
    static bool entryMotionBeforeShot, entryMotionAfterShot, entryMotionLineShot, entryMotionStableShot, entryMotionHasReference;
    static int entryMotionLastFrame, entryMotionBlackFrames, entryMotionUncoveredBlackFrames;
    static float entryMotionReferenceX, entryMotionMaxHorizontalDrift;
    static Exception entryMotionSamplingError;
    static bool mirrorPerformance, performanceRecording;
    static int performanceLastFrame, performanceMouseChangedFrame;
    static Exception performanceSamplingError;
    static Canvas performanceCanvas;
    static Camera performanceCamera;
    static CanvasScaler performanceScaler;
    static bool performanceScalerWasEnabled;
    static float performanceBasePixel, performanceBaseCursorWidth;
    static Matrix4x4 performanceBaseProjection;
    static readonly List<PerformanceFrame> performanceFrames = new List<PerformanceFrame>();
    static readonly List<string> performanceIssues = new List<string>();
    static readonly HashSet<string> performanceShots = new HashSet<string>();
    static readonly Dictionary<OSCursorReleaseDirector, bool> performanceActorFocus = new Dictionary<OSCursorReleaseDirector, bool>();
    static readonly Dictionary<int, float[]> performanceAttentionRestVertices = new Dictionary<int, float[]>();
    static int performanceAttentionInstance;
    static string performanceAttentionText;
    static GroupSample performanceMemoryBefore, performanceHomeBefore;
    sealed class GroupSample
    {
        public float alpha = 1f;
        public bool interactable = true, raycasts = true, dimOverlayActive, dimOverlayValid;
    }
    sealed class PerformanceFrame
    {
        public int frame, memoryNode, animatorState, visibleCharacters, effectStage;
        public float time, unscaledDeltaTime, zoom, pixelWidth, pixelRatio, cursorWidth, cursorSpriteWidth, cursorHotspotError, faceAlpha, signalProgress, referencePixelsPerUnit;
        public float effectClock, resumeCenterError, redMoveTime, redMoveDuration;
        public bool paused, transitioning, locked, sequence, focus, backHome, offline, mirror, attentionVisible, shaking;
        public bool lowerLabelsVisible, recognitionVisible, dimmed, signalIn, noSignal, anima, signalOwnsAnimation, scalerEnabled;
        public bool inputChangedThisFrame, cursorScriptControlled, beatFlashPresent;
        public bool zoomTarget, zoomOpened, zoomClosed, sessionRestoring;
        public bool pauseStarted, playbackPaused, playbackResumed, waitingResume, resumeIconVisible, resumeInteractable;
        public bool redMoving, redPressed, redVisible, rememberedNotePresent, firstLineComplete;
        public bool musicPaused, musicFading, musicAvailable;
        public int musicSource, fadingMusicSource;
        public float musicVolume, fadingMusicVolume, musicFadeRate, musicActiveDeltaTime;
        public string musicCue, musicClip, fadingMusicClip;
        public string phase, memoryHead, messageId, canvasMode, attentionText, extendedAnimation, shader;
        public float attentionFontSize;
        public float[] attentionPosition, attentionScale, attentionColor, rootScale, rootPosition, rootRect, projection, zoomOrigin, zoomOriginTarget, mousePoint, cursorPoint;
        public float[] redPoint, redLocalPoint, redFrom, redTo, mirrorFacePoint, liveTitlePoint, livePosition;
        public GlyphSample[] attentionGlyphs;
        public GroupSample memoryGroup, homeGroup;
    }
    sealed class GlyphSample
    {
        public int index;
        public float[] vertices, restVertices, animatorOffset;
        public float restError;
    }
    static string context;
    static OSChapter03StoryState S => story?.State;
    static OSChapter03StoryPresentation P => story?.Presentation;
    static OSHomeView V => home?.View;
    static OSLinkMessageController Messages => os?.LinkMessageController;
    static string MirrorChoice => OSChapter03StoryDirector.LiveId("live.breakdown.08");
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start() => StartRun(false);
    public static string StartBedroomCheckpoint() => StartRun(true);
    public static string StartCursorProjection() => StartRun(true, true);
    // The retired blink-entry probe now runs the current smooth-focus regression.
    public static string StartMirrorEntryMotion(bool assertStable = false) => StartMirrorPerformance();
    public static string StartMirrorPerformance() => StartRun(true, performance: true);
    public static string StartLabEntranceCheckpoint() => StartRun(false, labEntrance: true);

    static string StartRun(bool bedroom, bool cursor = false, bool entryMotion = false, bool assertStable = false, bool performance = false, bool labEntrance = false)
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
        data = GameDataManager.Instance; cancel = false; bedroomCheckpoint = bedroom; cursorProjection = cursor; context = "initialization";
        labEntranceCheckpoint = labEntrance;
        mirrorEntryMotion = entryMotion; assertEntryMotionStable = assertStable;
        mirrorPerformance = performance; performanceRecording = false; performanceLastFrame = -1; performanceSamplingError = null;
        performanceMouseChangedFrame = -1;
        performanceFrames.Clear(); performanceIssues.Clear(); performanceShots.Clear();
        performanceActorFocus.Clear();
        performanceAttentionRestVertices.Clear(); performanceAttentionInstance = 0; performanceAttentionText = null;
        performanceCanvas = null; performanceCamera = null; performanceScaler = null;
        entryMotionRecording = entryMotionFinalCapture = entryMotionBeforeShot = entryMotionAfterShot = false;
        entryMotionLineShot = entryMotionStableShot = entryMotionHasReference = false;
        entryMotionLastFrame = -1; entryMotionMaxHorizontalDrift = 0f; entryMotionSamplingError = null;
        entryMotionBlackFrames = entryMotionUncoveredBlackFrames = 0;
        checks.Clear(); screenshots.Clear(); observations.Clear();
        entryMotionFrames.Clear();
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", labEntrance ? "OSChapter03LabEntranceQA" : performance ? "OSChapter03MirrorPerformanceQA" : entryMotion ? "OSChapter03MirrorEntryMotionQA" : cursor ? "OSChapter03CursorProjectionQA" : bedroom ? "OSChapter03BedroomQA" : "OSChapter03MirrorQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current;
        var previousKeyboard = Keyboard.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); Set(setup, "isDemo", false);
            Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03MirrorQAMouse");
            if (mirrorPerformance) testKeyboard = InputSystem.AddDevice<Keyboard>("Chapter03MirrorQAKeyboard");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            work.Push(labEntranceCheckpoint ? RunLabEntranceCheckpoint() : mirrorPerformance ? RunMirrorPerformance() : mirrorEntryMotion ? RunMirrorEntryMotion() : cursorProjection ? RunCursorProjection() : bedroomCheckpoint ? RunBedroomCheckpoint() : Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    testMouse?.MakeCurrent(); testKeyboard?.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
                    if (mirrorPerformance) ResumeBackgroundActorFocus();
                    if (cursorProjection || mirrorEntryMotion || mirrorPerformance) ResumeBackgroundReadingHold();
                    if (entryMotionSamplingError != null) throw new InvalidOperationException("Mirror entry frame sampling failed", entryMotionSamplingError);
                    if (performanceSamplingError != null) throw new InvalidOperationException("Mirror performance frame sampling failed", performanceSamplingError);
                    if (cancel) throw new OperationCanceledException("Mirror QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + failure;
                    try { Mark("failure"); } catch { }
                    string path = Path.Combine(OutputDirectory, "failure.png"); screenshots.Add(path);
                    ScreenCapture.CaptureScreenshot(path);
                    yield return null; yield return new WaitForSecondsRealtime(.25f);
                    Debug.LogError("[Chapter03 Mirror QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null)
            {
                Check(files.Select(Hash).SequenceEqual(hashes), "original save files and all scene files remain unchanged");
                Status = "PASS";
            }
        }
        finally
        {
            entryMotionRecording = false;
            performanceRecording = false;
            RenderPipelineManager.endCameraRendering -= SampleMirrorEntryFrame;
            RenderPipelineManager.endCameraRendering -= SampleMirrorPerformanceFrame;
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                foreach (var pair in performanceActorFocus)
                    if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                bool actorFocusRestored = performanceActorFocus.All(pair => pair.Key == null || Equals(Get(pair.Key, "hasFocus"), pair.Value));
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                os?.LinkMessageController?.NotifyOSHidden(); story?.ResetPresentation(); home?.Suspend();
                story?.DebugSetFocus(false); os?.Chapter03Entry?.DebugSetFocus(false);
                os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume
                    || resources != null && resources.enabled != resourcesEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse
                    || previousKeyboard != null && previousKeyboard.added && Keyboard.current != previousKeyboard
                    || !UnityEngine.Random.state.Equals(random) || !actorFocusRestored) throw new InvalidOperationException("Original save/settings were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save or scene files changed");
                checks.Add("cleanup restores original save, language, mode, listener, resources, background, input and random state");
                if (mirrorPerformance) checks.Add("cleanup restores the red actor's original focus flag without focusing the Editor");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, screenshots, originalFiles = files,
                originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                mode = labEntranceCheckpoint ? "laboratory entrance checkpoint" : mirrorPerformance ? "mirror performance" : mirrorEntryMotion ? "mirror entry motion" : cursorProjection ? "cursor projection" : bedroomCheckpoint ? "bedroom checkpoint" : "bathroom mirror",
                performance = mirrorPerformance ? new { baselinePixelWidth = performanceBasePixel,
                    baselineCursorWidth = performanceBaseCursorWidth, issues = performanceIssues, frames = performanceFrames } : null,
                entryMotion = mirrorEntryMotion ? new { assertStable = assertEntryMotionStable,
                    maximumVisibleZoomHorizontalDriftPixels = entryMotionMaxHorizontalDrift,
                    blackFrames = entryMotionBlackFrames, uncoveredBlackFrames = entryMotionUncoveredBlackFrames,
                    frames = entryMotionFrames } : null,
                note = labEntranceCheckpoint
                    ? "Muted isolated background Play Mode; no Editor focus. Uses the real laboratory entrance console row for Demo denial, a fresh east-corridor snapshot, JSON/session restoration, the real 0710 keypad path, and repeated checkpoint cleanup from the entered laboratory and an open keypad. Original save files, scenes, input, listener, settings and random state are checked and restored. Typing and reading holds are accelerated in this independent checkpoint probe."
                    : mirrorPerformance
                    ? "Muted isolated background Play Mode; no Editor focus. Real bedroom checkpoint, mirror, offline replies, password/lab/bed/diary/box progression and first reunion line. Measures actual projected UI pixels, second-line focus entering over 15 active seconds while dialogue continues, constant ordinary cursor size, reveal/dimming, per-character rendered TMP meshes with a fixed text transform and settings pause, two complete 0.827-second red-pointer movements at 75% of their previous speed and the independent 0.4-second first line, player-only mirror resume before and after JSON restoration, real Escape settings and option raycasts, offline NoSignal, and reconnect signal material without OpenLive. Original mouse/keyboard devices and red actor focus flags are restored. First breakdown line types naturally throughout; other typing may be completed, while reading holds and authored effects retain their timing. No unfinished Ch03 portrait animation states are asserted."
                    : mirrorEntryMotion
                    ? "Muted isolated background Play Mode; no Editor focus. Real bedroom console checkpoint and bathroom door lead into the mirror. Samples each actual OS camera endCameraRendering without forcing Canvas updates; first Live line types naturally, then remains observed for two seconds or until line 02 begins. Four event screenshots cover pre/post zoom cut, first line opening and final observation. Default diagnostic mode asserts acquisition only, not screen stability; optional assertStable checks full viewport coverage on every strength-16 black frame and the mirror's screen X drift after the blink becomes uncovered."
                    : cursorProjection
                    ? "Muted isolated background Play Mode; no Editor focus. Starts through the real bedroom console checkpoint and bathroom door. Tests cursor pixels against the main Canvas's actual projected pixels, input hotspot alignment, settings, mirror option JSON restoration, real screen-coordinate GraphicRaycaster selection and offline recovery. Typing is accelerated; reading holds keep their original LateUpdate timing with only focus-only pause resumed. This does not assert whole-screen 2x rendering or unfinished Animator states. Exit Play Mode after this isolated run."
                    : bedroomCheckpoint
                    ? "Muted background Play Mode; no Editor focus. Invokes the real bedroom console button, walks through its real bathroom door, starts the real mirror story through breakdown.01 and whole-screen zoom, then invokes the same checkpoint again to verify cleanup. Typing and existing Live countdown callbacks are accelerated; authored animations/delays are unchanged. Exit Play Mode after this isolated run."
                    : "Muted background Play Mode; no Editor focus. A cloned real checkpoint supplies the bathroom fixture. Memory remains pinned. Real mirror story and window APIs run with authored delays/animations; typing and existing Live countdown completion callbacks are accelerated. Countdown duration is not tested. Exit Play Mode after this isolated run."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Mirror QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; testMouse = null; testKeyboard = null; os = null; memory = null; home = null; story = null;
            memoryWindow = liveWindow = settingsWindow = null;
        }
    }

    static IEnumerator RunMirrorPerformance()
    {
        data.SetCurrentLanguage(LanguageState.zh_CN);
        yield return BedroomConsoleJump(null);
        yield return CheckBedroomReady("mirror performance bedroom checkpoint");
        performanceCanvas = memoryWindow.GetComponentInParent<Canvas>().rootCanvas;
        performanceCamera = performanceCanvas.worldCamera;
        performanceScaler = performanceCanvas.GetComponent<CanvasScaler>();
        performanceScalerWasEnabled = performanceScaler != null && performanceScaler.enabled;
        performanceBaseProjection = performanceCamera.projectionMatrix;
        performanceBasePixel = PerformancePixelWidth();
        MoveMouse(new Vector2(Screen.width * .9f, Screen.height * .5f)); yield return Settled();
        performanceBaseCursorWidth = PerformanceCursorBounds().width;
        performanceMemoryBefore = SampleGroups(memoryWindow.transform); performanceHomeBefore = SampleGroups(home.HomeWindow.transform);
        Check(performanceBasePixel > 0f && performanceBaseCursorWidth > 0f, "actual UI and ordinary cursor baselines are available");

        var door = V.CurrentRoom.FindDoor("bed_bath");
        Check(door != null && !door.locked && (bool)Call(V, "AcceptsInput"), "real bedroom bathroom door is available");
        Call(V, "Click", door.tile);
        yield return Wait(() => V.CurrentRoom?.id == "bath" && !V.IsWalking && (bool)Call(V, "AcceptsInput"), "real bathroom door walk completes");
        yield return Drive(() => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "bathroom arrival finishes");
        performanceRecording = true; RenderPipelineManager.endCameraRendering += SampleMirrorPerformanceFrame;
        Call(story, "OnThing", V.CurrentRoom.FindThing("bath_mirror"));
        yield return Drive(() => P.MirrorRoot != null && MirrorFaceAlpha() >= .999f, "mirror face completes its authored reveal", 60f);
        yield return Settled();
        var attention = P.MirrorRoot.Find("Frame/Attention")?.GetComponent<TMPro.TMP_Text>();
        PerfCheck(attention != null && attention.isActiveAndEnabled && attention.text == "..主人??"
            && Mathf.Abs(attention.fontSize - 12f) < .01f && attention.color == OSPalette.Red
            && attention.maxVisibleCharacters >= attention.text.Length, "complete red 12-point attention caption appears immediately after face reveal");
        yield return Capture("02-mirror-attention.png");
        CheckPerformanceWindowGuards("mirror reveal");
        yield return CheckPerformanceAttentionPause();

        yield return Drive(() => story.IsMirrorPlaybackPaused, "red cursor presses the reflected face and pauses its playback", 60f);
        yield return Settled(); CheckPerformancePlaybackControl(false, "red pointer pauses Memory");
        CheckPerformanceGroups(true, "red pointer pauses Memory");
        yield return Drive(() => S.Has("mirror_live_focus"), "red cursor begins carrying Live", 60f);
        yield return Settled();
        PerfCheck(!MirrorAttentionShaking(), "mirror caption shake stops when the red cursor starts carrying Live");
        CheckPerformanceGroups(true, "Live carry begins");
        yield return CheckPerformanceInputShield();
        yield return Capture("03-live-carry-dimmed-background.png");
        string first = OSChapter03StoryDirector.LiveId("live.breakdown.01");
        yield return Drive(() => performanceFrames.Any(frame => frame.firstLineComplete) && P.IsScreenZoomed
            && S.Has("closeup_open") && !P.IsScreenZoomTransitioning,
            "first breakdown line completes naturally and the second-line focus finishes its 15-second entry", 120f, naturalEntry: first);
        // Update can finish the zoom before LateUpdate applies its final camera
        // projection; the dialogue continues independently during the slow entry.
        yield return Settled();
        CheckPerformanceZoom(true, "settled focus triggered by the second breakdown line");
        Vector2 settledLiveCenter = PerformanceLiveCenterInCanvas();
        Vector2 settledFocusTarget = (Vector2)Call(P, "ZoomOriginTarget");
        Vector2 settledMessageSize = ((RectTransform)Messages.LinkWindow.transform).rect.size;
        yield return Capture("04-settled-light-focus.png");
        CheckPerformanceWindowGuards("settled second-line focus");
        yield return ClickPerformanceResume(false);
        yield return CheckPerformanceEscape();

        yield return Drive(() => Messages.DisplayedEntryId == MirrorChoice && Messages.IsWaitingForOption,
            "real breakdown reaches its selectable eighth line", 150f);
        Messages.TryCompleteDisplayedOptionTextImmediately(MirrorChoice);
        yield return Settled(); CheckPerformanceZoom(true, "eighth breakdown option");
        Vector2 optionLiveCenter = PerformanceLiveCenterInCanvas();
        Vector2 optionFocusTarget = (Vector2)Call(P, "ZoomOriginTarget");
        observations.Add(new { context, type = "Live-only focus target across message changes",
            settledLiveCenter = MotionVector(settledLiveCenter), optionLiveCenter = MotionVector(optionLiveCenter),
            settledFocusTarget = MotionVector(settledFocusTarget), optionFocusTarget = MotionVector(optionFocusTarget),
            settledMessageSize = MotionVector(settledMessageSize), optionMessageSize = MotionVector(((RectTransform)Messages.LinkWindow.transform).rect.size) });
        PerfCheck(Vector2.Distance(settledLiveCenter, optionLiveCenter) <= .01f
            && Vector2.Distance(settledFocusTarget, optionFocusTarget) <= .001f,
            "settled second-line focus and the eighth option keep the same target while Live's own center is unchanged");
        CheckPerformanceWindowGuards("zoomed option");
        yield return CheckPerformanceChoiceMusic("original eighth option");
        yield return Roundtrip();
        yield return Wait(() => P.IsScreenZoomed && !P.IsScreenZoomTransitioning && !P.Paused, "saved eighth option restores settled focus");
        yield return Settled(); CheckPerformanceZoom(true, "restored eighth option"); CheckPerformanceGroups(true, "restored performance");
        PerfCheck(!MirrorAttentionShaking(), "restored performance retains its stopped mirror caption shake");
        yield return CheckPerformanceChoiceMusic("JSON-restored eighth option");
        yield return ClickMirrorOptionAtScreenPoint("performance eighth option", "07-eighth-option-click.png");

        yield return Drive(() => story.IsWaitingForMirrorResume, "finished Live waits for the player's mirror resume click", 150f);
        yield return Settled(); CheckPerformanceGroups(true, "awaiting player resume"); CheckPerformanceZoom(false, "awaiting player resume");
        CheckPerformancePlaybackControl(true, "awaiting player resume");
        yield return CheckPerformanceResumeWait("before waiting-state save");
        yield return Capture("08a-awaiting-player-resume.png");
        yield return RoundtripMirrorResume();
        yield return CheckPerformanceResumeWait("restored waiting state");
        yield return Capture("08b-restored-awaiting-player-resume.png");
        yield return ClickPerformanceResume(true);
        yield return Drive(() => S.Has("mirror_playback_resumed") && memory.CurrentData?.DataIndex == 9048,
            "one player click releases the wait and starts Memory 9048", 30f);
        yield return Settled(); CheckPerformanceGroups(false, "player resumed Memory"); CheckPerformanceZoom(false, "player resumed Memory");
        PerfCheck(!story.IsMirrorPlaybackPaused && !P.IsMirrorPlaybackPaused
            && (P.MirrorResumeButton == null || !P.MirrorResumeButton.gameObject.activeInHierarchy),
            "one player resume click removes both the playback pause and its icon");
        yield return Capture("08c-memory-resumed-by-player.png");
        yield return CheckOfflineOfferAfterCry();

        context = "real lab route to final reconnect";
        Check(S.Has("password_ready"), "real offline corridor enables the password");
        Call(story, "OnDoor", V.CurrentRoom.FindDoor("lab_door"));
        yield return Drive(() => Head("keypad") && P.IsModalOpen, "real laboratory keypad opens");
        yield return Wait(() => (bool)Get(P, "CanInteract"), "keypad entrance permits actual buttons");
        foreach (char digit in "0710") PerformanceModalButton("Digit" + digit).onClick.Invoke();
        PerformanceModalButton("Submit").onClick.Invoke();
        string labOffer = OSChapter03StoryDirector.LiveId("live.offline.lab_offer");
        yield return Drive(() => V.CurrentRoom?.id == "lab" && Messages.DisplayedEntryId == labOffer && Messages.IsWaitingForOption,
            "correct keypad enters laboratory and its real offline offer", 100f);
        Messages.TryCompleteDisplayedOptionTextImmediately(labOffer); yield return Settled(); yield return CheckPerformanceNoSignal("laboratory offline reply");
        yield return ClickMirrorOptionAtScreenPoint("laboratory offline reply", "10-no-signal-lab-reply.png");
        yield return Drive(PerformanceIdle, "laboratory reply finishes", 80f);

        PerformanceActivateThing("lab_bed");
        yield return Drive(() => P.IsModalOpen && P.ArtId == "bed-hand", "real first bedside illustration opens");
        yield return Wait(() => (bool)Get(P, "CanInteract"), "bedside illustration permits Close");
        PerformanceModalButton("Close").onClick.Invoke();
        yield return Drive(PerformanceIdle, "first bedside visit finishes");
        PerformanceActivateThing("lab_pillow_note");
        yield return Drive(PerformanceIdle, "real diary thirteen reading and absorption finish", 120f);
        Check(S.papers.Contains("diary_13") && S.Has("dong_absorbed"), "diary thirteen was collected through the real paper callback");
        PerformanceActivateThing("lab_bed");
        yield return Drive(() => S.Has("bed_second_done") && PerformanceIdle(), "real second bedside sequence reveals the box", 120f);
        PerformanceActivateThing("lab_box");
        string reunion = OSChapter03StoryDirector.LiveId("live.return.01");
        yield return Drive(() => Messages.DisplayedEntryId == reunion && Messages.IsReadingHoldActive
            && Messages.DisplayedTextLabel.maxVisibleCharacters == int.MaxValue, "real box reconnect reaches its first complete Live line", 100f);
        yield return Settled();
        var character = os.CharacterWindowController;
        PerfCheck(!S.liveOffline && character.CurrentState == OSCharacterWindowState.Anima && !character.IsSignalInActive
            && character.Chapter03Portrait.material?.shader?.name != "UI/OS/CameraSignalIn", "reconnect restores ordinary Live and releases the signal material");
        yield return Capture("12-reconnected-first-line.png");
        performanceRecording = false; RenderPipelineManager.endCameraRendering -= SampleMirrorPerformanceFrame;
        ValidatePerformanceFrames();
        if (performanceIssues.Count > 0) throw new InvalidOperationException("Mirror performance issues:\n" + string.Join("\n", performanceIssues));
    }

    static void ResumeBackgroundActorFocus()
    {
        var actor = os?.CursorRelease;
        if (actor == null || GameAutosaveService.Instance?.IsTestSession != true) return;
        if (!performanceActorFocus.ContainsKey(actor)) performanceActorFocus.Add(actor, (bool)Get(actor, "hasFocus"));
        Set(actor, "hasFocus", true);
    }
    static float PerformanceResumeCenterError()
    {
        var button = P.MirrorResumeButton;
        var face = Get(P, "mirrorFace") as Image;
        if (button == null || face == null) return -1f;
        var rect = (RectTransform)button.transform;
        return Vector2.Distance(RectTransformUtility.WorldToScreenPoint(performanceCamera, rect.TransformPoint(rect.rect.center)),
            RectTransformUtility.WorldToScreenPoint(performanceCamera, face.rectTransform.TransformPoint(face.rectTransform.rect.center)));
    }
    static void CheckPerformancePlaybackControl(bool canResume, string label)
    {
        var button = P.MirrorResumeButton;
        PerfCheck(story.IsMirrorPlaybackPaused && P.IsMirrorPlaybackPaused && button != null && button.gameObject.activeInHierarchy
            && button.IsInteractable() == canResume, label + ": the paused mirror icon has the correct player input state");
        if (button == null) return;
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>();
        PerfCheck(Vector2.Distance(rect.rect.size, new Vector2(36f, 36f)) <= .01f
            && PerformanceResumeCenterError() >= 0f && PerformanceResumeCenterError() <= 1f,
            label + ": the 36-pixel pause control is centered on the mirror face within one screen pixel");
        PerfCheck(canvas != null && canvas.overrideSorting == canResume
            && (canResume ? canvas.sortingOrder == performanceCanvas.sortingOrder + 6 : canvas.transform.parent == P.MirrorRoot)
            && canvas.worldCamera == performanceCamera,
            label + ": the pause control remains behind Live during her performance and rises above the shield for the resume offer");
    }
    static string PerformanceMemoryTextSnapshot() => JsonConvert.SerializeObject(memory.TextUnits.Where(item => item != null)
        .Select(item => new { item.DataIndex, text = item.Label?.text, visible = item.Label != null ? item.Label.maxVisibleCharacters : -1 }));
    static IEnumerator CheckPerformanceResumeWait(string label)
    {
        context = label;
        Check(story.IsWaitingForMirrorResume, "the actual story is waiting for a player resume");
        int node = memory.CurrentData?.DataIndex ?? -1;
        string queue = JsonConvert.SerializeObject(S.memoryQueue), text = PerformanceMemoryTextSnapshot();
        int firstFrame = Time.frameCount;
        float until = Time.realtimeSinceStartup + .6f;
        while (Time.realtimeSinceStartup < until) yield return null;
        observations.Add(new { context, type = "mirror remains paused without player input", firstFrame, lastFrame = Time.frameCount,
            firstNode = node, lastNode = memory.CurrentData?.DataIndex, sameQueue = queue == JsonConvert.SerializeObject(S.memoryQueue),
            sameText = text == PerformanceMemoryTextSnapshot(), story.IsWaitingForMirrorResume, story.IsMirrorPlaybackPaused });
        PerfCheck(story.IsWaitingForMirrorResume && Head("effect", "wait_mirror_resume") && (memory.CurrentData?.DataIndex ?? -1) == node
            && queue == JsonConvert.SerializeObject(S.memoryQueue) && text == PerformanceMemoryTextSnapshot() && !S.Has("mirror_playback_resumed"),
            label + ": Memory text and its queue do not advance while waiting for the player's click");
        CheckPerformanceGroups(true, label); CheckPerformancePlaybackControl(true, label);
    }
    static IEnumerator RoundtripMirrorResume()
    {
        context = "mirror player-resume wait restore";
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "mirror-awaiting-player-resume-save.json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        Messages.NotifyOSHidden(); story.ResetPresentation(); home.Suspend(); data.ReplaceSaveData(restored);
        yield return OSSessionPersistence.Restore(restored);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        yield return Drive(() => story.IsAvailable && story.IsWaitingForMirrorResume && P.IsMirrorPlaybackPaused
            && P.MirrorResumeButton != null && P.MirrorResumeButton.IsInteractable(),
            "JSON and real OS session restoration recover the player-resume wait");
        yield return Settled();
        Check(S.Has("mirror_pause_started") && S.Has("mirror_playback_paused") && !S.Has("mirror_playback_resumed")
            && S.liveOffline && S.Has("live_back_home"), "restored wait retains its pause and offline progression flags");
        CheckPerformanceZoom(false, "restored player-resume wait");
        CheckPerformancePlaybackControl(true, "restored player-resume wait");
    }
    static IEnumerator ClickPerformanceResume(bool shouldResume)
    {
        context = shouldResume ? "player resumes the mirror" : "early player click cannot resume the performance";
        var button = P.MirrorResumeButton;
        Check(button != null && button.gameObject.activeInHierarchy && EventSystem.current != null,
            "the actual pause control and EventSystem exist");
        Vector2 point = CursorScreenBounds((RectTransform)button.transform, performanceCamera).center;
        MoveMouse(point); yield return Settled(); ObservePerformanceCursor(point);
        var pointer = new PointerEventData(EventSystem.current) { position = point, pressPosition = point,
            pointerId = -1, button = PointerEventData.InputButton.Left, clickCount = 1, eligibleForClick = true };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var hit = hits.Count > 0 ? hits[0] : default;
        observations.Add(new { context, type = "player cursor raycasts mirror resume", point = MotionVector(point), shouldResume,
            interactable = button.IsInteractable(), hits = hits.Take(8).Select(item => new {
                target = item.gameObject?.name, module = item.module?.GetType().Name, camera = item.module?.eventCamera?.name, item.sortingOrder }).ToArray() });
        Check(hit.gameObject != null && hit.module is GraphicRaycaster && hit.module.eventCamera == performanceCamera
            && (!shouldResume || hit.gameObject.GetComponentInParent<Button>() == button) && button.IsInteractable() == shouldResume,
            shouldResume ? "the actual topmost raycast reaches the enabled pause control"
                : "an actual topmost UI hit is available while the pause control remains disabled");
        if (shouldResume) yield return Capture("08c-player-cursor-on-resume.png");
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        hits.Clear(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && (shouldResume ? hits[0].gameObject.GetComponentInParent<Button>() == button : hits[0].gameObject == hit.gameObject),
            "the intended topmost target remains under the same player cursor after pointer down and up");
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        if (shouldResume)
            Check(S.Has("mirror_playback_resumed") && !story.IsMirrorPlaybackPaused && !P.IsMirrorPlaybackPaused,
                "one real player pointer click resumes mirror playback");
        else PerfCheck(!S.Has("mirror_playback_resumed") && story.IsMirrorPlaybackPaused && P.IsMirrorPlaybackPaused && P.IsMirrorStageDimmed,
            "the disabled pause control ignores an early player click while Live performs");
        yield return null;
    }

    static bool PerformanceIdle() => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0
        && !Messages.IsWaitingForOption && !Messages.IsReadingHoldActive && !P.IsModalOpen;
    const string InitialOfflineOfferKey = "live.offline.initial_offer", EastOfflineCryKey = "live.offline.east";
    static int PendingInitialOfferCount() => S.memoryQueue.Concat(S.liveQueue).Count(beat => beat.type == "live" && beat.key == InitialOfflineOfferKey);
    static IEnumerator CheckOfflineOfferAfterCry()
    {
        context = "first offline cry before the player reply";
        string offer = OSChapter03StoryDirector.LiveId(InitialOfflineOfferKey), cry = OSChapter03StoryDirector.LiveId(EastOfflineCryKey);
        yield return Drive(() => S.liveOffline && story.CurrentPhase == OSChapter03StoryDirector.Phase.Offline && PerformanceIdle(),
            "mirror narration ends with offline Live and no pending player reply", 150f);
        Check(!S.Has("offline_read_receipt") && PendingInitialOfferCount() == 0 && Messages.DisplayedEntryId != offer
            && !data.OSCharacterInteractionState.HasConsumedGlobalEntry(offer), "the initial offer has not appeared at the end of the mirror");
        CheckOfflineOfferCompatibility();
        var controller = Messages;
        int cryStarts = 0, offerStarts = 0;
        Action<string, OSCharacterInteractionEntryData> started = (sequence, entry) =>
        { if (entry.EntryId == cry) cryStarts++; if (entry.EntryId == offer) offerStarts++; };
        controller.EntryPresentationStarted += started;
        try
        {
            bool walked = false;
            Check(V.WalkTo(new Vector2Int(7, 5), () => walked = true), "real first offline step begins");
            yield return Drive(() => walked && PerformanceIdle(), "first offline step and narration finish", 80f);
            Check(offerStarts == 0 && PendingInitialOfferCount() == 0, "walking away from the mirror does not offer a reply yet");
            Check(V.EnterRoom("east", new Vector2Int(7, 4)), "real room callback enters the right corridor");
            yield return Drive(PerformanceIdle, "right corridor password clue completes", 80f);
            Check(S.Has("offline_east") && !S.Has("offline_east_leak") && offerStarts == 0,
                "entering east shows its clue but does not skip the first crying step");
            walked = false;
            Check(V.WalkTo(new Vector2Int(7, 5), () => walked = true), "a real step in east triggers the first offline cry");
            yield return Drive(() => walked && Messages.DisplayedEntryId == cry && Messages.IsReadingHoldActive,
                "east's first crying message actually displays and finishes its text", 80f, naturalEntry: cry);
            Check(S.Has("offline_east_leak") && cryStarts == 1 && offerStarts == 0 && PendingInitialOfferCount() == 1
                && S.liveQueue.Count >= 2 && S.liveQueue[0].key == EastOfflineCryKey && S.liveQueue[1].key == InitialOfflineOfferKey,
                "one initial offer waits directly behind the completed crying text and its reading hold");
            yield return Capture("08d-first-offline-cry.png");
            yield return RoundtripOfflineCry(cry, offer);
            float remaining = Messages.ReadingHoldRemaining, resumedAt = Time.realtimeSinceStartup;
            yield return Drive(() => Messages.DisplayedEntryId == offer && Messages.IsWaitingForOption,
                "the initial player reply appears only after the restored cry finishes reading", 80f, naturalEntry: cry);
            Check(Time.realtimeSinceStartup - resumedAt >= remaining - .2f && cryStarts == 1 && offerStarts == 1
                && !S.liveQueue.Any(beat => beat.key == EastOfflineCryKey),
                "restoring during the cry preserves its remaining reading time and starts the following offer exactly once");
            Messages.TryCompleteDisplayedOptionTextImmediately(offer); yield return Settled();
            yield return CheckPerformanceNoSignal("first offline reply after crying");
            yield return ClickMirrorOptionAtScreenPoint("first offline reply after crying", "09-no-signal-with-reply.png");
            yield return Drive(PerformanceIdle, "first offline reply completes its read receipt", 80f);
            Check(S.Has("offline_read_receipt") && data.OSCharacterInteractionState.HasConsumedGlobalEntry(offer),
                "the real reply click records its read receipt and its own consumed entry");
            foreach (var tile in new[] { new Vector2Int(7, 4), new Vector2Int(7, 5) })
            {
                walked = false; Check(V.WalkTo(tile, () => walked = true), "another real east step begins");
                yield return Drive(() => walked && PerformanceIdle(), "repeat east step stays idle", 80f);
            }
            Check(cryStarts == 1 && offerStarts == 1 && PendingInitialOfferCount() == 0,
                "later east steps do not replay either the cry or its consumed initial offer");
            observations.Add(new { context, type = "offline cry then reply", cryStarts, offerStarts, restoredReadingSeconds = remaining,
                initialOfferConsumed = data.OSCharacterInteractionState.HasConsumedGlobalEntry(offer) });
        }
        finally { controller.EntryPresentationStarted -= started; }
    }
    static IEnumerator RoundtripOfflineCry(string cry, string offer)
    {
        context = "JSON restore during the first offline cry";
        float remaining = Messages.ReadingHoldRemaining;
        Check(remaining > .5f && S.liveQueue[0].key == EastOfflineCryKey && S.liveQueue[0].complete,
            "cry restore starts during its actual completed-text reading hold");
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "offline-cry-before-initial-offer-save.json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        Messages.NotifyOSHidden(); story.ResetPresentation(); home.Suspend(); data.ReplaceSaveData(restored);
        yield return OSSessionPersistence.Restore(restored);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        yield return Wait(() => story.IsAvailable && Messages.DisplayedEntryId == cry && Messages.IsReadingHoldActive,
            "real JSON session restoration rebuilds the same first crying message");
        Check(Messages.ReadingHoldRemaining > 0f && Messages.ReadingHoldRemaining <= remaining + .15f
            && S.Has("offline_east_leak") && PendingInitialOfferCount() == 1
            && S.liveQueue.Count >= 2 && S.liveQueue[0].key == EastOfflineCryKey && S.liveQueue[1].key == InitialOfflineOfferKey
            && !data.OSCharacterInteractionState.HasConsumedGlobalEntry(offer),
            "cry restoration retains the ordered single pending offer without presenting or consuming it early");
    }
    static void CheckOfflineOfferCompatibility()
    {
        // These fixtures run synchronously inside the isolated session; no Update
        // can present their temporary queue entries or consume their options.
        var memoryBefore = S.memoryQueue.ToArray(); var liveBefore = S.liveQueue.ToArray(); var flagsBefore = S.flags.ToArray();
        var interaction = data.OSCharacterInteractionState; string interactionBefore = JsonUtility.ToJson(interaction);
        string offer = OSChapter03StoryDirector.LiveId(InitialOfflineOfferKey);
        try
        {
            S.memoryQueue.Clear(); S.liveQueue.Clear(); S.flags.Remove("offline_east_leak");
            var memoryPending = new OSChapter03StoryBeat("live", InitialOfflineOfferKey);
            var livePending = new OSChapter03StoryBeat("live", InitialOfflineOfferKey);
            var begun = new OSChapter03StoryBeat("live", InitialOfflineOfferKey) { begun = true };
            var complete = new OSChapter03StoryBeat("live", InitialOfflineOfferKey) { complete = true };
            S.memoryQueue.Add(memoryPending); S.memoryQueue.Add(begun); S.liveQueue.Add(livePending); S.liveQueue.Add(complete);
            Call(story, "DeferPendingInitialOfflineOffer");
            Check(!S.memoryQueue.Contains(memoryPending) && !S.liveQueue.Contains(livePending)
                && S.memoryQueue.Contains(begun) && S.liveQueue.Contains(complete),
                "legacy migration defers only unstarted offers in both queues and preserves begun or completed offers");
            S.memoryQueue.Clear(); S.liveQueue.Clear(); S.Add("offline_east_leak"); S.liveQueue.Add(livePending);
            Call(story, "DeferPendingInitialOfflineOffer");
            Check(S.liveQueue.Contains(livePending), "migration retains the correctly placed offer after the east cry has triggered");
            S.memoryQueue.Clear(); S.liveQueue.Clear(); interaction.SetGlobalEntryConsumed(offer, false); S.Add("offline_read_receipt");
            Call(story, "QueueInitialOfflineOfferAfterCry"); Call(story, "QueueInitialOfflineOfferAfterCry");
            Check(PendingInitialOfferCount() == 1, "an unrelated offline read receipt cannot suppress this offer and repeated queueing cannot duplicate it");
            S.liveQueue.Clear(); S.memoryQueue.Add(memoryPending); Call(story, "QueueInitialOfflineOfferAfterCry");
            Check(PendingInitialOfferCount() == 1 && S.liveQueue.Count == 0, "an existing offer in the Memory queue also prevents duplicates");
            S.memoryQueue.Clear(); interaction.SetGlobalEntryConsumed(offer, true); S.flags.Remove("offline_read_receipt");
            Call(story, "QueueInitialOfflineOfferAfterCry");
            Check(PendingInitialOfferCount() == 0, "the initial offer's own consumed record prevents replay even without the shared read receipt");
        }
        finally
        {
            S.memoryQueue.Clear(); S.memoryQueue.AddRange(memoryBefore); S.liveQueue.Clear(); S.liveQueue.AddRange(liveBefore);
            S.flags.Clear(); S.flags.AddRange(flagsBefore); JsonUtility.FromJsonOverwrite(interactionBefore, interaction);
        }
        Check(S.memoryQueue.SequenceEqual(memoryBefore) && S.liveQueue.SequenceEqual(liveBefore) && S.flags.SequenceEqual(flagsBefore)
            && JsonUtility.ToJson(interaction) == interactionBefore, "compatibility fixtures restore every temporary queue, flag and consumed-entry change");
    }
    static void PerformanceActivateThing(string id)
    {
        Check(story.IsAvailable && !story.BlocksHomeInput, "real thing input is available: " + id);
        var thing = V.CurrentRoom.FindThing(id); Check(thing != null && !thing.hidden, "real visible thing exists: " + id);
        Call(story, "OnThing", thing);
    }
    static Button PerformanceModalButton(string name)
    {
        var frame = Get(P, "modalFrame") as RectTransform;
        var button = frame?.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == name);
        Check(button != null, "real modal button exists: " + name); return button;
    }
    static void PerfCheck(bool value, string label)
    {
        if (value) checks.Add(context + ": " + label);
        else if (!performanceIssues.Contains(label)) performanceIssues.Add(label);
    }

    static IEnumerator CheckPerformanceChoiceMusic(string label)
    {
        context = label + " music";
        yield return Wait(() => story.IsAvailable && !Equals(Get(story, "musicPaused"), true)
            && Get(story, "storyMusic") is AudioSource, "eighth option has an active music source");
        var source = (AudioSource)Get(story, "storyMusic");
        int firstFrame = Time.frameCount;
        float activeSeconds = 0f, deadline = Time.realtimeSinceStartup + 15f;
        while (activeSeconds < 2f && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            if (story.IsAvailable && !Equals(Get(story, "musicPaused"), true))
                activeSeconds += Mathf.Min(Time.unscaledDeltaTime, .1f);
        }
        var frames = performanceFrames.Where(frame => frame.frame > firstFrame).ToArray();
        observations.Add(new { context, type = "eighth option keeps its music before choosing",
            activeSeconds, source = source != null ? source.GetInstanceID() : 0, clip = source?.clip?.name,
            volume = source != null ? source.volume : 0f, firstFrame, lastFrame = Time.frameCount });
        PerfCheck(source != null && activeSeconds >= 2f && frames.Length > 0 && frames.All(frame => frame.messageId == MirrorChoice
            && !frame.musicFading && frame.fadingMusicSource == 0 && frame.musicSource == source.GetInstanceID()
            && frame.musicCue == "breakdown" && !string.IsNullOrEmpty(frame.musicClip)),
            label + ": the same breakdown source keeps playing while the eighth option waits for the player");
        PerfCheck(source != null && source.volume >= .3f && !S.Has("breakdown_music_fading") && Messages.IsWaitingForOption,
            label + ": music remains at its normal level without starting the final-message fade");
    }

    static void SampleMirrorPerformanceFrame(ScriptableRenderContext renderContext, Camera camera)
    {
        if (!performanceRecording || performanceSamplingError != null || P == null || camera != performanceCamera
            || performanceLastFrame == Time.frameCount) return;
        performanceLastFrame = Time.frameCount;
        try
        {
            var root = (RectTransform)performanceCanvas.transform;
            var attention = P.MirrorRoot?.Find("Frame/Attention")?.GetComponent<TMPro.TMP_Text>();
            var character = os.CharacterWindowController; var portrait = character.Chapter03Portrait;
            var material = portrait != null ? portrait.material : null;
            var cursorIcon = Get(CursorHandle.Instance, "cursorIcon") as Image;
            var actor = os.CursorRelease;
            var redImage = Get(actor, "cursorImage") as Image;
            var playbackButton = P.MirrorResumeButton;
            var mirrorFace = Get(P, "mirrorFace") as Image;
            var effectParent = Get(P, "windowParent") as Transform;
            var animator = character.CharacterAnimator;
            var music = Get(story, "storyMusic") as AudioSource;
            var fadingMusic = Get(story, "fadingMusic") as AudioSource;
            bool musicPaused = Equals(Get(story, "musicPaused"), true);
            float pixels = PerformancePixelWidth(); var projection = camera.projectionMatrix;
            var frame = new PerformanceFrame {
                frame = Time.frameCount, time = Time.realtimeSinceStartup, unscaledDeltaTime = Time.unscaledDeltaTime,
                phase = story.CurrentPhase.ToString(),
                musicSource = music != null ? music.GetInstanceID() : 0,
                fadingMusicSource = fadingMusic != null ? fadingMusic.GetInstanceID() : 0,
                musicClip = music?.clip?.name, fadingMusicClip = fadingMusic?.clip?.name,
                musicVolume = music != null ? music.volume : 0f, fadingMusicVolume = fadingMusic != null ? fadingMusic.volume : 0f,
                musicCue = Get(story, "musicCue") as string, musicFadeRate = (float)Get(story, "fadingPerSecond"),
                musicPaused = musicPaused, musicAvailable = story.IsAvailable, musicFading = S.Has("breakdown_music_fading"),
                musicActiveDeltaTime = story.IsAvailable && !musicPaused ? Mathf.Min(Time.unscaledDeltaTime, .1f) : 0f,
                memoryNode = memory.CurrentData?.DataIndex ?? -1,
                memoryHead = S.memoryQueue.Count > 0 ? S.memoryQueue[0].type + ":" + S.memoryQueue[0].key : null,
                messageId = Messages.DisplayedEntryId, zoom = P.ScreenZoomProgress, transitioning = P.IsScreenZoomTransitioning,
                zoomTarget = Equals(Get(P, "zoomTarget"), true), zoomOpened = S.Has("closeup_open"), zoomClosed = S.Has("closeup_closed"),
                sessionRestoring = OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true,
                paused = P.Paused, locked = story.IsMirrorPerformanceLocked, sequence = story.IsMirrorSequenceActive,
                focus = S.Has("mirror_live_focus"), backHome = S.Has("live_back_home"), offline = S.liveOffline,
                pauseStarted = S.Has("mirror_pause_started"), playbackPaused = story.IsMirrorPlaybackPaused,
                playbackResumed = S.Has("mirror_playback_resumed"), waitingResume = story.IsWaitingForMirrorResume,
                effectClock = (float)Get(story, "effectClock"), effectStage = (int)Get(story, "effectStage"),
                resumeCenterError = PerformanceResumeCenterError(),
                resumeIconVisible = playbackButton != null && playbackButton.gameObject.activeInHierarchy,
                resumeInteractable = playbackButton != null && playbackButton.IsInteractable(),
                redMoving = actor != null && actor.ScriptActorIsMoving, redPressed = Equals(Get(actor, "scriptActorPressed"), true),
                redMoveTime = actor != null ? (float)Get(actor, "scriptActorMoveTime") : -1f,
                redMoveDuration = actor != null ? (float)Get(actor, "scriptActorMoveDuration") : -1f,
                redLocalPoint = actor != null ? MotionVector((Vector2)Get(actor, "scriptActorPosition")) : null,
                redFrom = actor != null ? MotionVector((Vector2)Get(actor, "scriptActorFrom")) : null,
                redTo = actor != null ? MotionVector((Vector2)Get(actor, "scriptActorTo")) : null,
                redVisible = redImage != null && redImage.isActiveAndEnabled && redImage.color.a > .99f,
                redPoint = redImage != null ? MotionVector(RectTransformUtility.WorldToScreenPoint(camera, redImage.rectTransform.position)) : null,
                mirrorFacePoint = mirrorFace != null ? MotionVector(RectTransformUtility.WorldToScreenPoint(camera,
                    mirrorFace.rectTransform.TransformPoint(mirrorFace.rectTransform.rect.center))) : null,
                liveTitlePoint = liveWindow != null ? MotionVector(RectTransformUtility.WorldToScreenPoint(camera,
                    liveWindow.WindowRect.TransformPoint(new Vector2(liveWindow.WindowRect.rect.center.x, liveWindow.WindowRect.rect.yMax - 8f)))) : null,
                livePosition = liveWindow != null ? MotionVector(liveWindow.WindowRect.anchoredPosition) : null,
                firstLineComplete = Messages.DisplayedEntryId == OSChapter03StoryDirector.LiveId("live.breakdown.01")
                    && Messages.IsReadingHoldActive && Messages.DisplayedTextLabel != null
                    && Messages.DisplayedTextLabel.maxVisibleCharacters == int.MaxValue,
                pixelWidth = pixels, pixelRatio = pixels / performanceBasePixel,
                cursorWidth = PerformanceCursorBounds().width, cursorSpriteWidth = cursorIcon?.sprite != null ? cursorIcon.sprite.rect.width : 0f,
                mousePoint = MotionVector(testMouse.position.ReadValue()), cursorPoint = MotionVector(CursorHandle.Instance.DisplayedScreenPosition),
                cursorHotspotError = Vector2.Distance(testMouse.position.ReadValue(), CursorHandle.Instance.DisplayedScreenPosition),
                inputChangedThisFrame = performanceMouseChangedFrame == Time.frameCount, cursorScriptControlled = CursorHandle.Instance.IsScriptControlled,
                canvasMode = performanceCanvas.renderMode.ToString(), scalerEnabled = performanceScaler != null && performanceScaler.enabled,
                rootScale = MotionVector(root.localScale), rootPosition = MotionVector(root.localPosition),
                rootRect = new[] { root.rect.x, root.rect.y, root.rect.width, root.rect.height },
                referencePixelsPerUnit = performanceCanvas.referencePixelsPerUnit,
                projection = new[] { projection.m00, projection.m03, projection.m11, projection.m13 },
                zoomOrigin = MotionVector((Vector2)Get(P, "zoomOrigin")), zoomOriginTarget = MotionVector((Vector2)Call(P, "ZoomOriginTarget")),
                mirror = P.MirrorRoot != null, faceAlpha = MirrorFaceAlpha(), attentionVisible = attention != null && attention.isActiveAndEnabled,
                attentionText = attention?.text, attentionFontSize = attention != null ? attention.fontSize : 0f,
                visibleCharacters = attention != null ? attention.maxVisibleCharacters : 0,
                attentionPosition = attention != null ? MotionVector(attention.rectTransform.anchoredPosition) : null,
                attentionScale = attention != null ? MotionVector(attention.rectTransform.localScale) : null,
                attentionGlyphs = SamplePerformanceGlyphs(attention),
                attentionColor = attention != null ? new[] { attention.color.r, attention.color.g, attention.color.b, attention.color.a } : null,
                shaking = MirrorAttentionShaking(), lowerLabelsVisible = MirrorNamedVisible("Identity") || MirrorNamedVisible("Spoken") || MirrorNamedVisible("Reflection"),
                recognitionVisible = MirrorNamedVisible("RecognitionFrame"), dimmed = P.IsMirrorStageDimmed,
                beatFlashPresent = effectParent?.Find("Chapter03BeatFlash") != null,
                rememberedNotePresent = effectParent?.Find("Chapter03RememberedNote") != null,
                memoryGroup = SampleGroups(memoryWindow.transform), homeGroup = SampleGroups(home.HomeWindow.transform),
                signalIn = character.IsSignalInActive, noSignal = (Get(character, "voidMessageState") as GameObject)?.activeInHierarchy == true,
                anima = (Get(character, "animaState") as GameObject)?.activeInHierarchy == true,
                extendedAnimation = character.CurrentExtendedAnimation.ToString(), signalOwnsAnimation = (bool)Get(character, "cameraSignalOwnsAnimation"),
                animatorState = animator != null && animator.isActiveAndEnabled ? animator.GetCurrentAnimatorStateInfo(0).shortNameHash : 0,
                shader = material?.shader?.name, signalProgress = material != null && material.HasProperty("_Progress") ? material.GetFloat("_Progress") : -1f
            };
            performanceFrames.Add(frame);
            if (frame.mirror && frame.faceAlpha > .1f && frame.faceAlpha < .8f) PerformanceEventShot("01-mirror-face-revealing.png");
            if (frame.memoryHead == "effect:mirror_pause")
            {
                if (frame.effectStage == 0 && frame.redMoving && frame.redMoveTime >= .12f
                    && frame.redMoveTime < .5f && frame.redVisible)
                    PerformanceEventShot("03a-red-pointer-approaches-memory.png");
                if (frame.redPressed && frame.playbackPaused && frame.redVisible)
                    PerformanceEventShot("03b-red-pointer-pauses-memory.png");
            }
            if (frame.messageId == OSChapter03StoryDirector.LiveId("live.breakdown.01"))
                PerformanceEventShot("03c-first-message-during-pause-action.png");
            if (frame.firstLineComplete) PerformanceEventShot("03d-first-line-naturally-complete.png");
            if (frame.messageId == OSChapter03StoryDirector.LiveId("live.breakdown.02") && frame.zoomTarget && !frame.paused)
                PerformanceEventShot("03e-second-line-focus-start.png");
            if (frame.zoomTarget && frame.zoom >= .45f && frame.zoom <= .55f && !frame.paused)
                PerformanceEventShot("03f-focus-midway.png");
            if (frame.signalIn && frame.signalProgress > .2f && frame.signalProgress < .7f) PerformanceEventShot("11-old-signal-reconnect.png");
        }
        catch (Exception error) { performanceSamplingError = error; }
    }
    static void PerformanceEventShot(string name)
    { if (performanceShots.Add(name)) CaptureMotionEvent(name); }
    static float MirrorFaceAlpha() => (Get(P, "mirrorFace") as Image)?.color.a ?? -1f;
    static bool MirrorAttentionShaking()
    {
        var glyphs = Get(P, "mirrorAttentionGlyphShakes") as ICollection;
        return Get(P, "mirrorAttentionAnimator") is DOTweenTMPAnimator && glyphs != null && glyphs.Count > 0;
    }
    static GlyphSample[] SamplePerformanceGlyphs(TMPro.TMP_Text attention)
    {
        if (attention == null || attention.textInfo == null) return Array.Empty<GlyphSample>();
        if (performanceAttentionInstance != attention.GetInstanceID() || performanceAttentionText != attention.text)
        {
            performanceAttentionRestVertices.Clear(); performanceAttentionInstance = attention.GetInstanceID();
            performanceAttentionText = attention.text;
        }
        var animator = Get(P, "mirrorAttentionAnimator") as DOTweenTMPAnimator;
        var rest = Get(animator, "_cachedMeshInfos") as TMPro.TMP_MeshInfo[];
        var meshVertices = new Dictionary<int, Vector3[]>();
        var result = new List<GlyphSample>();
        for (int i = 0; i < attention.textInfo.characterCount; i++)
        {
            var character = attention.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            int material = character.materialReferenceIndex, vertex = character.vertexIndex;
            if (material < 0 || material >= attention.textInfo.meshInfo.Length) continue;
            if (!meshVertices.TryGetValue(material, out var vertices))
            {
                var mesh = attention.textInfo.meshInfo[material].mesh;
                // Read the mesh DOTween actually submits; never regenerate TMP geometry in QA.
                vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
                meshVertices.Add(material, vertices);
            }
            if (vertex < 0 || vertex + 3 >= vertices.Length) continue;
            var actual = GlyphVertices(vertices, vertex);
            if (rest != null && material < rest.Length && rest[material].vertices != null
                && vertex + 3 < rest[material].vertices.Length)
                performanceAttentionRestVertices[i] = GlyphVertices(rest[material].vertices, vertex);
            else if (!performanceAttentionRestVertices.ContainsKey(i) && !MirrorAttentionShaking())
                performanceAttentionRestVertices[i] = actual.ToArray();
            performanceAttentionRestVertices.TryGetValue(i, out var baseline);
            result.Add(new GlyphSample { index = i, vertices = actual, restVertices = baseline,
                animatorOffset = animator != null ? MotionVector(animator.GetCharOffset(i)) : null,
                restError = baseline != null ? VertexDifference(actual, baseline) : -1f });
        }
        return result.ToArray();
    }
    static float[] GlyphVertices(Vector3[] vertices, int start)
    {
        var result = new float[12];
        for (int i = 0; i < 4; i++)
        { result[i * 3] = vertices[start + i].x; result[i * 3 + 1] = vertices[start + i].y; result[i * 3 + 2] = vertices[start + i].z; }
        return result;
    }
    static float VertexDifference(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return float.PositiveInfinity;
        float result = 0f;
        for (int i = 0; i < a.Length; i++) result = Mathf.Max(result, Mathf.Abs(a[i] - b[i]));
        return result;
    }
    static Vector3 GlyphDisplacement(GlyphSample glyph) => glyph?.vertices == null || glyph.restVertices == null
        ? Vector3.zero : new Vector3(glyph.vertices[0] - glyph.restVertices[0],
            glyph.vertices[1] - glyph.restVertices[1], glyph.vertices[2] - glyph.restVertices[2]);
    static bool MirrorNamedVisible(string name)
    { var node = P.MirrorRoot?.Find("Frame/" + name); return node != null && node.gameObject.activeInHierarchy; }
    static float PerformancePixelWidth()
    {
        var root = (RectTransform)performanceCanvas.transform;
        return Vector2.Distance(RectTransformUtility.WorldToScreenPoint(performanceCamera, root.TransformPoint(Vector3.zero)),
            RectTransformUtility.WorldToScreenPoint(performanceCamera, root.TransformPoint(Vector3.right)));
    }
    static Rect PerformanceCursorBounds()
    {
        var cursor = CursorHandle.Instance; var icon = Get(cursor, "cursorIcon") as Image; var canvas = Get(cursor, "cursorCanvas") as Canvas;
        return icon != null && canvas != null ? CursorScreenBounds(icon.rectTransform,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera) : default;
    }
    static GroupSample SampleGroups(Transform target)
    {
        var result = new GroupSample(); bool stop = false;
        // Home is nested inside Memory, so Memory's final overlay also shades Home.
        for (var ancestor = target; ancestor != null; ancestor = ancestor.parent)
        {
            var shade = ancestor.Find("Chapter03MirrorDim")?.GetComponent<Image>();
            if (shade == null || !shade.isActiveAndEnabled) continue;
            result.dimOverlayActive = true;
            var rect = shade.rectTransform; var color = shade.color; var expected = OSWindowActivityDimming.DimColor;
            result.dimOverlayValid |= Mathf.Abs(color.r - expected.r) < .001f && Mathf.Abs(color.g - expected.g) < .001f
                && Mathf.Abs(color.b - expected.b) < .001f && Mathf.Abs(color.a - expected.a) < .001f && !shade.raycastTarget
                && rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one
                && rect.offsetMin.sqrMagnitude < .0001f && rect.offsetMax.sqrMagnitude < .0001f
                && rect.GetSiblingIndex() == ancestor.childCount - 1;
        }
        for (var current = target; current != null && !stop; current = current.parent)
            foreach (var group in current.GetComponents<CanvasGroup>())
            {
                if (!group.isActiveAndEnabled) continue;
                result.alpha *= group.alpha; result.interactable &= group.interactable; result.raycasts &= group.blocksRaycasts;
                stop |= group.ignoreParentGroups;
            }
        return result;
    }
    static bool GroupsMatch(GroupSample current, GroupSample baseline, bool dimmed = false)
        => Mathf.Abs(current.alpha - baseline.alpha) <= .015f
            && current.interactable == (!dimmed && baseline.interactable)
            && current.raycasts == (!dimmed && baseline.raycasts)
            && (dimmed ? current.dimOverlayValid : current.dimOverlayActive == baseline.dimOverlayActive);
    static void CheckPerformanceGroups(bool dimmed, string label)
    {
        PerfCheck(P.IsMirrorStageDimmed == dimmed && GroupsMatch(SampleGroups(memoryWindow.transform), performanceMemoryBefore, dimmed)
            && GroupsMatch(SampleGroups(home.HomeWindow.transform), performanceHomeBefore, dimmed),
            label + ": Memory and Home preserve their group alpha with the expected black overlays and interaction state");
    }
    static void CheckPerformanceZoom(bool zoomed, string label)
    {
        float ratio = PerformancePixelWidth() / performanceBasePixel;
        PerfCheck(Mathf.Abs(ratio - (zoomed ? ExpectedPerformanceZoom : 1f)) <= .01f, label + ": actual UI pixels have the requested screen magnification");
        PerfCheck(performanceCanvas.renderMode == (zoomed ? RenderMode.WorldSpace : RenderMode.ScreenSpaceCamera)
            && (performanceScaler == null || performanceScaler.enabled == (!zoomed && performanceScalerWasEnabled)),
            label + ": Canvas render mode and scaler ownership match the view");
        if (!zoomed) PerfCheck(MatrixMatches(performanceCamera.projectionMatrix, performanceBaseProjection), label + ": ordinary camera projection is restored");
        else
        {
            var liveRect = (RectTransform)liveWindow.transform;
            Vector2 liveCenter = RectTransformUtility.WorldToScreenPoint(performanceCamera, liveRect.TransformPoint(liveRect.rect.center));
            Vector2 viewportCenter = performanceCamera.pixelRect.center;
            float centerError = Vector2.Distance(liveCenter, viewportCenter);
            var root = (RectTransform)performanceCanvas.transform;
            Vector2 viewSize = root.rect.size / ExpectedPerformanceZoom;
            Vector2 origin = PerformanceLiveCenterInCanvas() - viewSize * .5f;
            origin.x = Mathf.Clamp(origin.x, root.rect.xMin, root.rect.xMax - viewSize.x);
            origin.y = Mathf.Clamp(origin.y, root.rect.yMin, root.rect.yMax - viewSize.y);
            Vector2 boundedCenter = RectTransformUtility.WorldToScreenPoint(performanceCamera,
                root.TransformPoint(origin + viewSize * .5f));
            float boundedCenterError = Vector2.Distance(boundedCenter, viewportCenter);
            Vector2 lower = RectTransformUtility.WorldToScreenPoint(performanceCamera, root.TransformPoint(root.rect.min));
            Vector2 upper = RectTransformUtility.WorldToScreenPoint(performanceCamera, root.TransformPoint(root.rect.max));
            Rect viewport = performanceCamera.pixelRect;
            observations.Add(new { context, label, type = "Live-only focus center", frame = Time.frameCount,
                liveCenter = MotionVector(liveCenter), viewportCenter = MotionVector(viewportCenter), centerError,
                boundedCenter = MotionVector(boundedCenter), boundedCenterError });
            // A smaller zoom may hit the desktop boundary before Live can be
            // exactly centered. Verify that limit and actual screen coverage.
            PerfCheck(boundedCenterError <= 1f && lower.x <= viewport.xMin + 1f && lower.y <= viewport.yMin + 1f
                && upper.x >= viewport.xMax - 1f && upper.y >= viewport.yMax - 1f,
                label + ": Live focus respects the desktop boundary and keeps the whole viewport covered");
        }
        ObservePerformanceCursor(null);
    }
    static Vector2 PerformanceLiveCenterInCanvas()
    {
        var rect = (RectTransform)liveWindow.transform;
        return performanceCanvas.transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
    }
    static void ObservePerformanceCursor(Vector2? intendedPoint)
    {
        var cursor = CursorHandle.Instance; var icon = Get(cursor, "cursorIcon") as Image;
        Rect bounds = PerformanceCursorBounds();
        float expected = icon?.sprite != null ? icon.sprite.rect.width * performanceBasePixel : 0f;
        PerfCheck(cursor != null && !cursor.IsScriptControlled && expected > 0f && Mathf.Abs(bounds.width - expected) <= Mathf.Max(.2f, expected * .01f),
            "ordinary cursor keeps its unzoomed screen pixel size");
        PerfCheck(icon != null && !icon.raycastTarget && (Get(cursor, "cursorCanvas") as Canvas)?.sortingOrder == 1500,
            "ordinary cursor remains above the UI without intercepting rays");
        if (intendedPoint.HasValue)
        {
            var sample = performanceFrames.LastOrDefault();
            float[] intended = MotionVector(intendedPoint.Value);
            observations.Add(new { context, type = "rendered cursor at requested input", requestFrame = Time.frameCount,
                inputChangedFrame = performanceMouseChangedFrame, renderFrame = sample?.frame, intended,
                currentMouseId = Mouse.current?.deviceId, testMouseId = testMouse?.deviceId,
                cursorNow = cursor != null ? MotionVector(cursor.DisplayedScreenPosition) : null,
                renderMouse = sample?.mousePoint, renderCursor = sample?.cursorPoint });
            PerfCheck(sample != null && sample.frame > performanceMouseChangedFrame
                && FramePointDistance(sample.mousePoint, intended) <= 1f && FramePointDistance(sample.cursorPoint, intended) <= 1f,
                "ordinary cursor hotspot matches the actual intended input coordinate in a later rendered frame");
        }
    }
    static IEnumerator CheckPerformanceNoSignal(string label)
    {
        observations.Add(new { context, label, type = "offline windows before opening settles", frame = Time.frameCount,
            live = PerformanceWindowState(liveWindow), link = PerformanceWindowState(Messages.LinkWindow), waiting = Messages.IsWaitingForOption });
        // Immediate empty offers expose their option while the normal opening tween is still running.
        yield return Wait(() => Ready(liveWindow) && Ready(Messages.LinkWindow) && Messages.IsWaitingForOption,
            label + ": real offline windows finish opening before presentation and input checks");
        observations.Add(new { context, label, type = "offline windows ready", frame = Time.frameCount,
            live = PerformanceWindowState(liveWindow), link = PerformanceWindowState(Messages.LinkWindow), waiting = Messages.IsWaitingForOption });
        var character = os.CharacterWindowController;
        var noSignal = Get(character, "voidMessageState") as GameObject; var anima = Get(character, "animaState") as GameObject;
        PerfCheck(character.CurrentState == OSCharacterWindowState.VoidMessage && noSignal != null && noSignal.activeInHierarchy
            && noSignal.GetComponentInChildren<OSNoSignalGlitch>(true) != null && anima != null && !anima.activeInHierarchy,
            label + ": the existing NoSignal presentation is actually visible");
        PerfCheck(Ready(liveWindow) && Ready(Messages.LinkWindow) && Messages.IsWaitingForOption,
            label + ": Live and its normal selectable message stay open while offline");
    }
    static object PerformanceWindowState(OSWindowUnit window) => window == null ? null : new {
        window.WindowId, window.IsOpen, window.IsOpening, window.IsClosing, window.IsMinimized, active = window.gameObject.activeInHierarchy
    };
    static void ValidatePerformanceZoomTiming()
    {
        string firstId = OSChapter03StoryDirector.LiveId("live.breakdown.01");
        string secondId = OSChapter03StoryDirector.LiveId("live.breakdown.02");
        var firstLine = performanceFrames.Where(frame => frame.messageId == firstId).ToArray();
        PerfCheck(firstLine.Length > 0 && firstLine.All(frame => frame.zoom <= .0001f && !frame.zoomTarget && !frame.zoomOpened
            && Mathf.Abs(frame.pixelRatio - 1f) <= .01f),
            "the entire first breakdown message stays at normal scale, including its natural reading hold");
        var second = performanceFrames.FirstOrDefault(frame => frame.messageId == secondId);
        var start = performanceFrames.FirstOrDefault(frame => frame.mirror && frame.zoomTarget && !frame.sessionRestoring);
        PerfCheck(second != null && start != null && start.messageId == secondId && Mathf.Abs(start.frame - second.frame) <= 1
            && performanceFrames.Where(frame => frame.mirror && frame.frame < second.frame)
                .All(frame => frame.zoom <= .0001f && !frame.zoomTarget && !frame.zoomOpened),
            "focus starts with the second message and never during the preceding mouse action or first message");
        if (start == null) return;
        var end = performanceFrames.FirstOrDefault(frame => frame.frame >= start.frame && frame.zoomTarget
            && !frame.paused && !frame.sessionRestoring && !frame.transitioning && frame.zoom >= .99999f);
        PerfCheck(end != null, "the second-line focus reaches its full settled magnification");
        if (end == null) return;
        var entry = performanceFrames.Where(frame => frame.frame >= start.frame && frame.frame <= end.frame
            && !frame.paused && !frame.sessionRestoring).ToArray();
        // Presentation caps long unscaled frames at .1s. Record both clocks so a
        // stalled Editor frame cannot masquerade as an authored short transition.
        float activeSeconds = entry.Sum(frame => Mathf.Min(frame.unscaledDeltaTime, .1f));
        float unscaledSeconds = entry.Sum(frame => frame.unscaledDeltaTime);
        float wallSeconds = end.time - start.time;
        bool monotonic = entry.Skip(1).Select((frame, index) => frame.zoom + .0001f >= entry[index].zoom).All(value => value);
        PerfCheck(entry.Length >= 3 && monotonic && Mathf.Abs(activeSeconds - ExpectedPerformanceZoomInSeconds) <= .2f
            && wallSeconds >= ExpectedPerformanceZoomInSeconds - .25f,
            "focus progresses continuously for approximately 15 active seconds before settling");
        var messagesDuringEntry = entry.Where(frame => frame.zoom < .999f && !string.IsNullOrEmpty(frame.messageId)
            && frame.messageId.StartsWith(OSChapter03StoryDirector.LiveId("live.breakdown."), StringComparison.Ordinal))
            .Select(frame => frame.messageId).Distinct().ToArray();
        PerfCheck(messagesDuringEntry.Contains(secondId) && messagesDuringEntry.Any(id => id != firstId && id != secondId),
            "later Live messages continue while the 15-second zoom is still entering");
        var close = performanceFrames.FirstOrDefault(frame => frame.frame > end.frame && frame.zoomClosed);
        var held = performanceFrames.Where(frame => frame.frame >= end.frame && (close == null || frame.frame < close.frame)
            && frame.mirror && frame.zoomOpened && !frame.zoomClosed && !frame.paused && !frame.sessionRestoring).ToArray();
        PerfCheck(close != null && held.Length >= 3 && held.All(frame => frame.zoomTarget && !frame.transitioning
            && frame.zoom >= .99999f && Mathf.Abs(frame.pixelRatio - ExpectedPerformanceZoom) <= .01f),
            "completed focus stays at 1.075x until the ordinary closing transition begins");
        observations.Add(new { context, type = "second-line focus timing", triggerFrame = start.frame,
            firstSecondMessageFrame = second?.frame, settledFrame = end.frame, closingFrame = close?.frame,
            activePresentationSeconds = activeSeconds, unscaledSeconds, wallSeconds, messagesDuringEntry,
            settledHoldFrames = held.Length, expectedEntrySeconds = ExpectedPerformanceZoomInSeconds });
        PerfCheck(new[] { "03e-second-line-focus-start.png", "03f-focus-midway.png" }
            .All(name => File.Exists(Path.Combine(OutputDirectory, name))), "second-line entry and midway focus screenshots were captured");
    }
    static void ValidatePerformanceMusic()
    {
        string finalId = OSChapter03StoryDirector.LiveId("live.breakdown.09");
        var first = performanceFrames.FirstOrDefault(frame => frame.messageId == finalId);
        var previous = first != null ? performanceFrames.LastOrDefault(frame => frame.frame < first.frame
            && frame.messageId == MirrorChoice && frame.musicSource != 0) : null;
        PerfCheck(first != null && previous != null && first.musicFading && first.musicSource == 0
            && first.fadingMusicSource == previous.musicSource && first.fadingMusicClip == previous.musicClip
            && first.fadingMusicVolume > 0f && string.IsNullOrEmpty(first.musicCue),
            "the first rendered final-message frame transfers the existing breakdown source into its fade without restarting it");
        if (first == null || first.fadingMusicSource == 0 || first.musicFadeRate <= 0f) return;
        int source = first.fadingMusicSource;
        var fading = performanceFrames.Where(frame => frame.frame >= first.frame && frame.fadingMusicSource == source).ToArray();
        var silent = performanceFrames.FirstOrDefault(frame => frame.frame > first.frame && frame.fadingMusicSource == 0);
        float activeSeconds = silent != null ? performanceFrames.Where(frame => frame.frame > first.frame
            && frame.frame <= silent.frame).Sum(frame => frame.musicActiveDeltaTime) : -1f;
        float initialVolume = previous != null ? previous.musicVolume : first.fadingMusicVolume;
        float expectedRate = initialVolume / ExpectedBreakdownMusicFadeSeconds;
        PerfCheck(fading.Length >= 3 && fading.All(frame => frame.musicSource == 0 && frame.musicFading
            && Mathf.Abs(frame.musicFadeRate - expectedRate) <= .001f && frame.fadingMusicClip == first.fadingMusicClip),
            "the final-message fade keeps one source and the same six-second volume slope throughout");
        float maximumStepError = 0f;
        for (int i = 1; i < fading.Length; i++)
        {
            float predicted = Mathf.Max(0f, fading[i - 1].fadingMusicVolume - fading[i].musicFadeRate * fading[i].musicActiveDeltaTime);
            maximumStepError = Mathf.Max(maximumStepError, Mathf.Abs(fading[i].fadingMusicVolume - predicted));
        }
        PerfCheck(fading.Length >= 3 && maximumStepError <= .002f && fading.Any(frame => frame.fadingMusicVolume > initialVolume * .7f)
            && fading.Any(frame => frame.fadingMusicVolume < initialVolume * .3f),
            "rendered audio-source samples decrease continuously through the fade's upper and lower volume ranges");
        PerfCheck(silent != null && Mathf.Abs(activeSeconds - ExpectedBreakdownMusicFadeSeconds) <= .18f,
            "breakdown music reaches silence after six active story seconds from the final message");
        var offline = performanceFrames.Where(frame => frame.offline && frame.phase != OSChapter03StoryDirector.Phase.Reunion.ToString()
            && frame.frame >= first.frame).ToArray();
        PerfCheck(offline.Length > 0 && offline.All(frame => frame.musicSource == 0 && string.IsNullOrEmpty(frame.musicCue)
            && (frame.fadingMusicSource == 0 || frame.fadingMusicSource == source
                && Mathf.Abs(frame.musicFadeRate - expectedRate) <= .001f)),
            "going offline never starts replacement music or shortens any remaining final-message fade");
        observations.Add(new { context, type = "breakdown final-message music fade", firstFrame = first.frame,
            firstMessage = first.messageId, source, clip = first.fadingMusicClip, initialVolume, expectedRate,
            fadeSamples = fading.Length, activeSeconds, silentFrame = silent?.frame, maximumStepError,
            offlineOverlapsFade = offline.Any(frame => frame.fadingMusicSource == source),
            note = "No forced go_offline event is injected; overlap is reported only if the authored flow reaches it before silence." });
    }

    static void ValidatePerformanceFrames()
    {
        ValidatePerformanceMusic();
        ValidatePerformanceZoomTiming();
        var zoom = performanceFrames.Where(frame => frame.mirror && !frame.paused && frame.zoom > .03f && frame.zoom < .97f).ToArray();
        PerfCheck(zoom.Length >= 3 && zoom.Any(frame => frame.pixelRatio > Mathf.Lerp(1f, ExpectedPerformanceZoom, .15f)
            && frame.pixelRatio < Mathf.Lerp(1f, ExpectedPerformanceZoom, .85f)),
            "focus records multiple intermediate rendered scales rather than an instant jump");
        PerfCheck(zoom.Where(frame => !frame.inputChangedThisFrame && !frame.cursorScriptControlled).All(frame => frame.cursorHotspotError <= 1f),
            "ordinary cursor hotspot stays within one pixel of stationary mouse input throughout the zoom transition");
        var settled = performanceFrames.Where(frame => frame.mirror && !frame.paused && !frame.transitioning && frame.zoom >= .999f).ToArray();
        PerfCheck(settled.Length > 0 && settled.All(frame => Mathf.Abs(frame.pixelRatio - ExpectedPerformanceZoom) <= .01f),
            "all settled unpaused focus samples render actual 1.075x UI pixels");
        var mirror = performanceFrames.Where(frame => frame.mirror || frame.sequence).ToArray();
        PerfCheck(mirror.Length > 0 && mirror.All(frame => !frame.beatFlashPresent), "mirror performance never creates a Chapter03BeatFlash overlay");
        PerfCheck(mirror.Length > 0 && mirror.All(frame => !frame.rememberedNotePresent), "mirror breakdown never flashes the remembered note");
        var pauseAction = mirror.Where(frame => frame.memoryHead == "effect:mirror_pause").ToArray();
        var approach = pauseAction.Where(frame => frame.effectStage == 0 && frame.redMoving && frame.redVisible && frame.redPoint != null).ToArray();
        var depart = pauseAction.Where(frame => frame.effectStage == 3 && frame.redMoving && frame.redVisible && frame.redPoint != null).ToArray();
        ValidatePerformancePointerTravel(approach, "approach to the mirror face");
        ValidatePerformancePointerTravel(depart, "return to Live's title bar");
        var hover = pauseAction.FirstOrDefault(frame => frame.effectStage == 1);
        PerfCheck(hover != null && !hover.redMoving && !hover.redPressed && !hover.playbackPaused
            && hover.redMoveTime >= ExpectedMirrorPointerTravelSeconds - .001f
            && FramePointDistance(hover.redLocalPoint, hover.redTo) <= .001f
            && FramePointDistance(hover.redPoint, hover.mirrorFacePoint) <= 1f,
            "the entire approach finishes at the mirror face before the hover and press");
        var pressed = pauseAction.Where(frame => frame.effectStage == 2 && frame.redPressed && frame.playbackPaused && frame.redVisible).ToArray();
        PerfCheck(pressed.Length > 0 && pressed.All(frame => FramePointDistance(frame.redPoint, frame.mirrorFacePoint) <= 1f),
            "the red actor visibly presses the mirror face center within one screen pixel before leaving for Live");
        var firstCarry = mirror.FirstOrDefault(frame => frame.memoryHead == "effect:live_to_center" && frame.focus);
        var returned = mirror.FirstOrDefault(frame => frame.memoryHead == "effect:live_to_center" && !frame.focus);
        // The actor is drawn on the canvas pixel grid (about six screen pixels
        // here). Verify exact arrival in its raw coordinates before carry moves
        // the title, then allow the normal pixel snap in the rendered grip.
        PerfCheck(returned != null && !returned.redMoving && returned.redMoveTime >= ExpectedMirrorPointerTravelSeconds - .001f
            && FramePointDistance(returned.redLocalPoint, returned.redTo) <= .001f
            && firstCarry != null && !firstCarry.redMoving && firstCarry.redPressed
            && firstCarry.redMoveTime >= ExpectedMirrorPointerTravelSeconds - .001f
            && FramePointDistance(firstCarry.redPoint, firstCarry.liveTitlePoint) <= performanceBasePixel,
            "the full return movement finishes before Live's carry begins, without a forced early jump");
        var firstMessage = mirror.FirstOrDefault(frame => frame.messageId == OSChapter03StoryDirector.LiveId("live.breakdown.01"));
        PerfCheck(firstMessage != null && firstMessage.effectClock >= .35f && firstMessage.effectClock <= .5f
            && firstMessage.memoryHead == "effect:mirror_pause" && firstMessage.effectStage == 0
            && firstMessage.redMoving && !firstMessage.playbackPaused && !firstMessage.focus,
            "the independent 0.4-second first-line delay expires during the normal approach, before the mirror is paused");
        PerfCheck(new[] { "03a-red-pointer-approaches-memory.png", "03b-red-pointer-pauses-memory.png", "03c-first-message-during-pause-action.png", "03d-first-line-naturally-complete.png" }
            .All(name => File.Exists(Path.Combine(OutputDirectory, name))), "red pointer approach, press and concurrent first-line screenshots were captured");
        var pausedPlayback = mirror.Where(frame => frame.mirror && frame.playbackPaused && !frame.paused).ToArray();
        PerfCheck(pausedPlayback.Length > 0 && pausedPlayback.All(frame => frame.dimmed && frame.resumeIconVisible && frame.resumeCenterError >= 0f
            && frame.resumeCenterError <= 1f && (frame.waitingResume || !frame.resumeInteractable)),
            "the paused mirror retains its centered icon and shade, and cannot resume during Live's performance");
        var revealing = performanceFrames.Where(frame => frame.mirror && frame.faceAlpha >= 0f && frame.faceAlpha < 1f).ToArray();
        PerfCheck(revealing.Length > 0 && revealing.All(frame => !frame.attentionVisible && !frame.lowerLabelsVisible && !frame.recognitionVisible),
            "face reveal has no premature attention, lower labels or recognition frame");
        var fullFace = performanceFrames.FirstOrDefault(frame => frame.mirror && frame.faceAlpha >= 1f);
        PerfCheck(fullFace != null && fullFace.attentionVisible && fullFace.attentionText == "..主人??" && fullFace.visibleCharacters >= 6,
            "the first fully revealed rendered face already has the complete attention caption");
        var shaking = performanceFrames.Where(frame => frame.mirror && frame.attentionVisible && !frame.focus && frame.shaking).ToArray();
        var glyphs = shaking.SelectMany(frame => frame.attentionGlyphs ?? Array.Empty<GlyphSample>()).GroupBy(glyph => glyph.index).ToArray();
        PerfCheck(shaking.Length >= 3 && glyphs.Length >= 2 && glyphs.All(group => group.All(glyph => glyph.restError >= 0f)
            && group.Any(glyph => glyph.restError > .01f)
            && group.Any(glyph => VertexDifference(glyph.vertices, group.First().vertices) > .01f)),
            "every visible attention character has observed displacement in its rendered TMP mesh");
        PerfCheck(shaking.Any(frame => frame.attentionGlyphs != null && frame.attentionGlyphs.Length >= 2
            && frame.attentionGlyphs.Skip(1).Any(glyph => Vector3.Distance(GlyphDisplacement(glyph), GlyphDisplacement(frame.attentionGlyphs[0])) > .01f)),
            "attention characters shake by different offsets rather than moving as one text block");
        CheckAttentionDoesNotRepeat(shaking);
        PerfCheck(shaking.Length > 0 && shaking.All(frame => VertexDifference(frame.attentionPosition, shaking[0].attentionPosition) <= .0001f
            && VertexDifference(frame.attentionScale, shaking[0].attentionScale) <= .0001f),
            "the attention text RectTransform stays fixed while individual glyphs shake");
        PerfCheck(performanceFrames.Where(frame => frame.mirror && frame.focus).All(frame => !frame.shaking), "caption shake remains stopped after Live takes focus");
        var stoppedGlyphs = performanceFrames.Where(frame => frame.mirror && frame.focus && frame.attentionVisible)
            .SelectMany(frame => frame.attentionGlyphs ?? Array.Empty<GlyphSample>()).ToArray();
        PerfCheck(stoppedGlyphs.Length > 0 && stoppedGlyphs.All(glyph => glyph.restError >= 0f && glyph.restError <= .001f),
            "stopping the attention shake restores every rendered character to its undeformed mesh");
        var dimmed = performanceFrames.Where(frame => frame.dimmed).ToArray();
        PerfCheck(OSWindowActivityDimming.DimColor == new Color(0f, 0f, 0f, .75f), "mirror and ordinary inactive windows share the authored black 75% shade");
        PerfCheck(dimmed.Length > 0 && dimmed.All(frame => GroupsMatch(frame.memoryGroup, performanceMemoryBefore, true)
            && GroupsMatch(frame.homeGroup, performanceHomeBefore, true)),
            "dimmed background samples keep original group alpha, the shared inactive-window shade and blocked interaction");
        var resumed = performanceFrames.Where(frame => frame.memoryNode == 9048 || frame.memoryNode == 9060 || frame.memoryNode == 9080).ToArray();
        PerfCheck(resumed.Length > 0 && resumed.All(frame => frame.backHome && frame.playbackResumed && !frame.playbackPaused && !frame.resumeIconVisible && !frame.locked && !frame.dimmed
            && GroupsMatch(frame.memoryGroup, performanceMemoryBefore) && GroupsMatch(frame.homeGroup, performanceHomeBefore)),
            "Memory's remaining mirror narration starts only after the player's resume click and background restoration");
        var reunion = performanceFrames.Where(frame => frame.phase == OSChapter03StoryDirector.Phase.Reunion.ToString()).ToArray();
        var signal = reunion.Where(frame => frame.signalIn && frame.shader == "UI/OS/CameraSignalIn").ToArray();
        PerfCheck(signal.Length >= 3 && signal.Min(frame => frame.signalProgress) < .25f && signal.Max(frame => frame.signalProgress) > .75f,
            "real reunion visibly traverses the existing signal shader transition");
        PerfCheck(reunion.Length > 0 && reunion.All(frame => frame.extendedAnimation != OSCharacterExtendedAnimation.OpenLive.ToString()
            && !frame.signalOwnsAnimation && frame.animatorState != Animator.StringToHash("Player_UI_State_OpenLive")),
            "no rendered reunion frame enters the OpenLive boot animation");
    }
    static float FramePointDistance(float[] a, float[] b) => a == null || b == null ? float.PositiveInfinity
        : Vector2.Distance(new Vector2(a[0], a[1]), new Vector2(b[0], b[1]));
    static void CheckAttentionDoesNotRepeat(PerformanceFrame[] shaking)
    {
        var frames = shaking.Where(frame => !frame.paused && frame.attentionGlyphs != null && frame.attentionGlyphs.Length >= 2).ToArray();
        var clocks = new float[frames.Length];
        for (int i = 1; i < frames.Length; i++) clocks[i] = clocks[i - 1] + Mathf.Min(frames[i].unscaledDeltaTime, .1f);
        int compared = 0, different = 0; float nextProbe = .12f;
        var pairs = new List<object>();
        for (int i = 0; i < frames.Length && compared < 8; i++)
        {
            if (clocks[i] < nextProbe) continue;
            float target = clocks[i] + .45f;
            int after = i + 1;
            while (after < frames.Length && clocks[after] < target) after++;
            if (after >= frames.Length) break;
            int before = after - 1;
            float blend = Mathf.InverseLerp(clocks[before], clocks[after], target);
            int changedGlyphs = 0; float largestDifference = 0f;
            foreach (var glyph in frames[i].attentionGlyphs)
            {
                var a = frames[before].attentionGlyphs.FirstOrDefault(item => item.index == glyph.index);
                var b = frames[after].attentionGlyphs.FirstOrDefault(item => item.index == glyph.index);
                if (a == null || b == null) continue;
                // Interpolate the actual mesh offsets to the same phase of the
                // old .45s loop, rather than comparing mismatched frame times.
                float distance = Vector3.Distance(GlyphDisplacement(glyph),
                    Vector3.Lerp(GlyphDisplacement(a), GlyphDisplacement(b), blend));
                largestDifference = Mathf.Max(largestDifference, distance);
                if (distance > .15f) changedGlyphs++;
            }
            compared++; if (changedGlyphs >= 2) different++;
            pairs.Add(new { firstFrame = frames[i].frame, laterFrames = new[] { frames[before].frame, frames[after].frame },
                activeSeconds = clocks[i], interval = .45f, changedGlyphs, largestDifference });
            nextProbe = clocks[i] + .13f;
        }
        observations.Add(new { context, type = "attention geometry across the former repeat interval", compared, different, pairs });
        PerfCheck(compared >= 4 && different >= 3,
            "multiple rendered glyph groups differ clearly after 0.45 seconds instead of replaying the same loop");
    }
    static float RenderedPointerTravel(PerformanceFrame[] frames) => frames.Length < 2 ? 0f
        : frames.Max(frame => FramePointDistance(frame.redPoint, frames[0].redPoint));
    static void ValidatePerformancePointerTravel(PerformanceFrame[] frames, string label)
    {
        PerfCheck(frames.Length >= 3 && frames.All(frame => Mathf.Abs(frame.redMoveDuration - ExpectedMirrorPointerTravelSeconds) <= .001f)
            && frames.Min(frame => frame.redMoveTime) <= .06f
            && frames.Max(frame => frame.redMoveTime) - frames.Min(frame => frame.redMoveTime) >= ExpectedMirrorPointerTravelSeconds - .12f
            && RenderedPointerTravel(frames) > performanceBasePixel,
            label + " visibly traverses the authored 0.827-second movement at 75% of its previous speed");
    }

    static void CheckPerformanceWindowGuards(string label)
    {
        context = label;
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true).Where(item => item != settingsWindow && item.IsOpen && !item.IsMinimized))
        {
            PerfCheck(os.IsWindowDismissalBlocked(window), label + ": player dismissal is guarded for " + window.WindowId);
            var button = window.GetComponentsInChildren<OSWindowButton>(true).FirstOrDefault(item => item.LinkedWindow == window && item.Action == OSWindowButton.WindowAction.Close);
            if (button != null) ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            window.Close(); window.Minimize();
            Check(window.IsOpen && !window.IsMinimized, label + ": close-button callback, Close and Minimize cannot dismiss " + window.WindowId);
        }
        PerfCheck(!os.IsWindowDismissalBlocked(settingsWindow), label + ": settings remains dismissible");
    }
    static IEnumerator CheckPerformanceInputShield()
    {
        var shield = Get(P, "mirrorInputShield") as RectTransform;
        Check(shield != null && shield.gameObject.activeInHierarchy, "performance input shield is present");
        Vector2 point = performanceCamera.pixelRect.min + new Vector2(8f, 8f);
        MoveMouse(point); yield return Settled();
        var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        PerfCheck(hits.Count > 0 && hits[0].gameObject != null && (hits[0].gameObject.transform == shield || hits[0].gameObject.transform.IsChildOf(shield)),
            "the real topmost background raycast hits the performance shield");
        Vector2Int before = V.PlayerTile;
        if (hits.Count > 0) ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        V.OnPointerClick(pointer); // Also exercise Home's own input guard behind the shield.
        yield return null;
        PerfCheck(V.PlayerTile == before && !V.IsWalking && !(bool)Call(V, "AcceptsInput"), "blocked background/Home clicks cannot start movement");
    }
    static IEnumerator PulsePerformanceEscape()
    {
        var launcher = UnityEngine.Object.FindObjectsByType<OSOptionLauncherButton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.isActiveAndEnabled && Get(item, "optionWindow") as OSWindowUnit == settingsWindow);
        Check(launcher != null && testKeyboard != null && testKeyboard.added, "real settings keyboard consumer and isolated keyboard exist");
        testKeyboard.MakeCurrent();
        // Match EntryQA's background keyboard fixture: queued keyboard events can be
        // filtered by Editor focus. Write isolated device state and invoke the real
        // polling Update in that same input update; never bypass its Escape branch.
        InputState.Change(testKeyboard, new KeyboardState(Key.Escape), InputState.currentUpdateType);
        try
        {
            Check(testKeyboard.escapeKey.wasPressedThisFrame, "isolated Escape state is visible to the real keyboard polling branch");
            observations.Add(new { context, type = "isolated Escape keyboard injection", frame = Time.frameCount,
                keyboardEnabled = testKeyboard.enabled, currentKeyboard = Keyboard.current == testKeyboard,
                pressed = testKeyboard.escapeKey.isPressed, pressedThisFrame = testKeyboard.escapeKey.wasPressedThisFrame,
                focused = Application.isFocused, updateType = InputState.currentUpdateType.ToString(),
                settingsOpenBefore = settingsWindow.IsOpen, settingsMinimizedBefore = settingsWindow.IsMinimized,
                note = "InputState.Change followed by the real OSOptionLauncherButton.Update keyboard consumer" });
            Call(launcher, "Update");
        }
        finally { InputState.Change(testKeyboard, new KeyboardState(), InputState.currentUpdateType); }
        yield return null;
    }

    static IEnumerator CheckPerformanceSettingsRaycast()
    {
        var controller = settingsWindow.GetComponent<OSOptionWindowController>();
        Check(controller != null, "real settings controller exists for interaction verification");
        var shield = Get(P, "mirrorInputShield") as RectTransform;
        PerfCheck(shield == null || !shield.gameObject.activeInHierarchy, "performance shield is inactive while settings pauses the story");
        int originalPage = (int)Get(controller, "currentPageIndex");
        foreach (int page in new[] { originalPage == 0 ? 1 : 0, originalPage })
        {
            var action = page == 0 ? OSOptionImageButton.ButtonAction.AudioPage : OSOptionImageButton.ButtonAction.ScreenPage;
            var button = settingsWindow.GetComponentsInChildren<OSOptionImageButton>(true)
                .FirstOrDefault(item => item.isActiveAndEnabled && (OSOptionImageButton.ButtonAction)Get(item, "action") == action);
            Check(button != null, "real settings page button is available: " + action);
            Vector2 point = CursorScreenBounds((RectTransform)button.transform, performanceCamera).center;
            MoveMouse(point); yield return Settled();
            var pointer = new PointerEventData(EventSystem.current) { position = point, pressPosition = point,
                button = PointerEventData.InputButton.Left, pointerId = -1, clickCount = 1, eligibleForClick = true };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            var first = hits.Count > 0 ? hits[0] : default;
            observations.Add(new { context, type = "paused settings page raycast", action = action.ToString(), point = MotionVector(point),
                hits = hits.Take(8).Select(hit => new { target = hit.gameObject?.name, camera = hit.module?.eventCamera?.name }).ToArray() });
            Check(first.gameObject != null && first.module is GraphicRaycaster
                && first.gameObject.GetComponentInParent<OSOptionImageButton>() == button,
                "actual topmost settings page hit is reachable through the paused shield");
            pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = first;
            ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            var receiver = ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Check(receiver != null && (int)Get(controller, "currentPageIndex") == page && P.Paused,
                "real raycast click changes the settings page without resuming the performance");
        }
    }
    static IEnumerator CheckPerformanceEscape()
    {
        context = "actual Escape settings during focused performance";
        var windows = new[] { memoryWindow, home.HomeWindow, liveWindow, Messages.LinkWindow };
        var open = windows.Select(window => window.IsOpen).ToArray();
        yield return PulsePerformanceEscape();
        yield return Wait(() => Ready(settingsWindow) && P.Paused, "actual keyboard Escape opens settings while preserving performance windows");
        yield return Settled(); CheckPerformanceZoom(false, "Escape settings");
        yield return CheckPerformanceSettingsRaycast();
        var hold = Get(Messages, "autoCloseTween") as Tween;
        float elapsed = hold != null && hold.IsActive() ? hold.Elapsed() : -1f;
        float progress = P.ScreenZoomProgress;
        yield return Capture("05-escape-settings-at-normal-scale.png");
        float until = Time.realtimeSinceStartup + .3f;
        while (Time.realtimeSinceStartup < until) yield return null;
        PerfCheck(Mathf.Abs(P.ScreenZoomProgress - progress) < .0001f
            && (elapsed < 0f || hold != null && hold.IsActive() && Mathf.Abs(hold.Elapsed() - elapsed) < .05f),
            "settings pauses focus progress and the current natural Live reading hold");
        PerfCheck(windows.Select(window => window.IsOpen).SequenceEqual(open), "Escape does not close a story window before opening settings");
        yield return PulsePerformanceEscape();
        yield return Wait(() => !P.Paused && P.IsScreenZoomed && !P.IsScreenZoomTransitioning, "second actual Escape resumes focused performance");
        yield return Settled(); CheckPerformanceZoom(true, "Escape resume");
        yield return Capture("06-escape-resumed-light-focus.png");
    }

    static IEnumerator CheckPerformanceAttentionPause()
    {
        context = "settings during the mirror attention shake";
        Check(MirrorAttentionShaking(), "per-character attention shake is active before settings opens");
        yield return PulsePerformanceEscape();
        yield return Wait(() => Ready(settingsWindow) && P.Paused, "Escape pauses the attention caption before the red pointer action");
        yield return Settled();
        var attention = P.MirrorRoot?.Find("Frame/Attention")?.GetComponent<TMPro.TMP_Text>();
        var before = SamplePerformanceGlyphs(attention);
        float until = Time.realtimeSinceStartup + .2f;
        while (Time.realtimeSinceStartup < until) yield return null;
        var after = SamplePerformanceGlyphs(attention);
        PerfCheck(before.Length >= 2 && SameGlyphGeometry(before, after),
            "settings freezes the actual per-character mesh without resetting its current shake pose");
        observations.Add(new { context, type = "attention glyph pause", before, after });
        yield return PulsePerformanceEscape();
        yield return Wait(() => !P.Paused && settingsWindow.IsMinimized, "second Escape resumes the attention caption");
        until = Time.realtimeSinceStartup + .6f;
        bool moved = false;
        while (!moved && Time.realtimeSinceStartup < until)
        { yield return null; moved = !SameGlyphGeometry(after, SamplePerformanceGlyphs(attention)); }
        PerfCheck(moved && MirrorAttentionShaking(), "the same character shake resumes after settings closes");
    }
    static bool SameGlyphGeometry(GlyphSample[] a, GlyphSample[] b) => a.Length == b.Length
        && a.All(glyph => b.Any(other => other.index == glyph.index && VertexDifference(glyph.vertices, other.vertices) <= .0001f));

    static IEnumerator RunMirrorEntryMotion()
    {
        data.SetCurrentLanguage(LanguageState.zh_CN);
        yield return BedroomConsoleJump(null);
        yield return CheckBedroomReady("mirror entry motion bedroom checkpoint");
        context = "mirror entry motion real bathroom door";
        var door = V.CurrentRoom.FindDoor("bed_bath");
        Check(door != null && !door.locked && door.targetRoom == "bath" && (bool)Call(V, "AcceptsInput"),
            "real bedroom bathroom door accepts walking");
        Call(V, "Click", door.tile);
        yield return Wait(() => V.CurrentRoom?.id == "bath" && !V.IsWalking && (bool)Call(V, "AcceptsInput"),
            "actual bedroom door walk reaches the bathroom");
        yield return Drive(() => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "bathroom arrival narration finishes");
        Check(story.IsAvailable && !story.BlocksHomeInput, "mirror interaction is available before frame recording");

        context = "mirror entry frame recording";
        RenderPipelineManager.endCameraRendering += SampleMirrorEntryFrame;
        entryMotionRecording = true;
        yield return null; yield return null;
        Check(entryMotionFrames.Count > 0, "actual OS camera render callback supplies the pre-mirror baseline");
        Call(story, "OnThing", V.CurrentRoom.FindThing("bath_mirror"));
        string first = OSChapter03StoryDirector.LiveId("live.breakdown.01");
        string second = OSChapter03StoryDirector.LiveId("live.breakdown.02");
        yield return Drive(() => Messages.DisplayedEntryId == first && Messages.IsReadingHoldActive
            && Messages.DisplayedTextLabel.maxVisibleCharacters == int.MaxValue,
            "mirror entry reaches naturally complete first Live line", 150f, naturalEntry: first);
        float until = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartup < until && Messages.DisplayedEntryId != second) yield return null;
        entryMotionFinalCapture = true;
        yield return Wait(() => entryMotionStableShot, "final first-line frame is captured after the observation period");
        entryMotionRecording = false;
        RenderPipelineManager.endCameraRendering -= SampleMirrorEntryFrame;
        Check(entryMotionBeforeShot && entryMotionAfterShot && entryMotionLineShot && entryMotionStableShot,
            "all four mirror entry event screenshots were requested from rendered frames");
        yield return Wait(() => screenshots.All(path => File.Exists(path) && new FileInfo(path).Length > 100),
            "all mirror entry event screenshots are written", 10f);
        if (assertEntryMotionStable)
        {
            Check(entryMotionBlackFrames > 0 && entryMotionUncoveredBlackFrames == 0,
                "every full-black switching frame covers all four viewport edges within one screen pixel");
            Check(entryMotionHasReference && entryMotionMaxHorizontalDrift <= 1f,
                "after the black blink uncovers, the mirror's rendered X stays within one screen pixel");
        }
    }

    static void SampleMirrorEntryFrame(ScriptableRenderContext renderContext, Camera camera)
    {
        if (!entryMotionRecording || entryMotionSamplingError != null || P == null || memoryWindow == null) return;
        var canvas = memoryWindow.GetComponentInParent<Canvas>().rootCanvas;
        if (camera != canvas.worldCamera || entryMotionLastFrame == Time.frameCount) return;
        entryMotionLastFrame = Time.frameCount;
        try
        {
            // This callback follows the real camera's Canvas rendering. Do not force
            // layout here: the one-frame native Canvas/projection order is the subject.
            var root = (RectTransform)canvas.transform;
            var projection = camera.projectionMatrix;
            var origin = (Vector2)Get(P, "zoomOrigin");
            var target = (Vector2)Call(P, "ZoomOriginTarget");
            var focus = Get(P, "zoomFocus") as RectTransform;
            var extra = Get(P, "zoomExtra") as RectTransform;
            bool shown = (bool)Get(P, "zoomShown"), wanted = (bool)Get(P, "zoomTarget");
            float blink = (float)Get(P, "zoomBlink");
            int blinkStrength = (int)Get(P, "zoomBlinkStrength");
            var blinkRect = Get(P, "zoomBlinkRect") as RectTransform;
            Rect blinkBounds = blinkRect != null ? CursorScreenBounds(blinkRect, camera) : default;
            Rect viewport = camera.pixelRect;
            bool blinkCoversViewport = blinkRect != null && blinkRect.gameObject.activeInHierarchy
                && blinkBounds.width > 0f && blinkBounds.height > 0f
                && blinkBounds.xMin <= viewport.xMin + 1f && blinkBounds.yMin <= viewport.yMin + 1f
                && blinkBounds.xMax >= viewport.xMax - 1f && blinkBounds.yMax >= viewport.yMax - 1f;
            if (blinkStrength == 16)
            {
                entryMotionBlackFrames++;
                if (!blinkCoversViewport) entryMotionUncoveredBlackFrames++;
            }
            var head = S.memoryQueue.Count > 0 ? S.memoryQueue[0] : null;
            entryMotionFrames.Add(new {
                frame = Time.frameCount, time = Time.realtimeSinceStartup, samplePoint = "OS camera endCameraRendering",
                phase = story.CurrentPhase.ToString(), memoryNode = memory.CurrentData?.DataIndex,
                memoryHead = head == null ? null : new { head.type, head.key, head.begun, head.remaining, head.complete },
                effectClock = Get(story, "effectClock"), ownsWindowPosition = Get(story, "ownsWindowPosition"),
                windowFrom = MotionVector((Vector2)Get(story, "windowFrom")), windowTo = MotionVector((Vector2)Get(story, "windowTo")),
                messageId = Messages.DisplayedEntryId, readingHold = Messages.IsReadingHoldActive,
                rootRect = new[] { root.rect.x, root.rect.y, root.rect.width, root.rect.height },
                rootLocalScale = MotionVector(root.localScale), rootLocalPosition = MotionVector(root.localPosition),
                rootWorldPosition = MotionVector(root.position), rootWorldScale = MotionVector(root.lossyScale), canvas.scaleFactor,
                camera = camera.name, projection.m00, projection.m03, projection.m11, projection.m13,
                zoomOrigin = MotionVector(origin), zoomOriginTarget = MotionVector(target), zoomShown = shown,
                zoomTarget = wanted, zoomApplied = Get(P, "zoomApplied"), zoomBlink = blink, zoomBlinkStrength = blinkStrength,
                zoomBlinkRect = MotionRect(blinkRect, camera), blinkCoversViewport,
                cameraPixelRect = new[] { viewport.x, viewport.y, viewport.width, viewport.height },
                zoomFocus = MotionRect(focus, camera), zoomExtra = MotionRect(extra, camera),
                mirror = MotionRect(P.MirrorRoot, camera), live = MotionRect(liveWindow?.WindowRect, camera),
                link = MotionRect(Messages.LinkWindow?.WindowRect, camera),
                linkOpen = Messages.LinkWindow?.IsOpen, linkOpening = Messages.LinkWindow?.IsOpening,
                mirrorVisible = S.Has("mirror_visible"), liveMovedCenter = S.Has("live_moved_center")
            });
            if (shown && blinkStrength <= 0 && P.MirrorRoot != null)
            {
                float x = CursorScreenBounds(P.MirrorRoot, camera).center.x;
                if (!entryMotionHasReference) { entryMotionReferenceX = x; entryMotionHasReference = true; }
                entryMotionMaxHorizontalDrift = Mathf.Max(entryMotionMaxHorizontalDrift, Mathf.Abs(x - entryMotionReferenceX));
            }
            if (!entryMotionBeforeShot && wanted && !shown)
            { entryMotionBeforeShot = true; CaptureMotionEvent("01-before-zoom-shown.png"); }
            if (!entryMotionAfterShot && shown)
            { entryMotionAfterShot = true; CaptureMotionEvent("02-first-zoom-shown.png"); }
            if (!entryMotionLineShot && Messages.DisplayedEntryId == OSChapter03StoryDirector.LiveId("live.breakdown.01"))
            { entryMotionLineShot = true; CaptureMotionEvent("03-first-line-open.png"); }
            if (entryMotionFinalCapture && !entryMotionStableShot)
            { entryMotionStableShot = true; CaptureMotionEvent("04-first-line-stable.png"); }
        }
        catch (Exception error) { entryMotionSamplingError = error; }
    }

    static void CaptureMotionEvent(string name)
    {
        observations.Add(new { context, name, frame = Time.frameCount, time = Time.realtimeSinceStartup,
            note = "Screenshot requested at actual OS camera endCameraRendering; no forced layout or extra settle frames" });
        string path = Path.Combine(OutputDirectory, name); screenshots.Add(path);
        ScreenCapture.CaptureScreenshot(path);
    }

    static object MotionRect(RectTransform rect, Camera camera)
    {
        if (rect == null) return null;
        Rect bounds = CursorScreenBounds(rect, camera);
        return new { name = rect.name, instanceId = rect.GetEntityId().ToString(), parent = rect.parent?.name,
            parentId = rect.parent != null ? rect.parent.GetEntityId().ToString() : string.Empty, active = rect.gameObject.activeInHierarchy,
            anchored = MotionVector(rect.anchoredPosition), local = MotionVector(rect.localPosition),
            localScale = MotionVector(rect.localScale), size = MotionVector(rect.rect.size), world = MotionVector(rect.position),
            screenCenter = MotionVector(bounds.center), screenBounds = new[] { bounds.x, bounds.y, bounds.width, bounds.height } };
    }
    static float[] MotionVector(Vector2 value) => new[] { value.x, value.y };
    static float[] MotionVector(Vector3 value) => new[] { value.x, value.y, value.z };

    static IEnumerator RunCursorProjection()
    {
        data.SetCurrentLanguage(LanguageState.zh_CN);
        yield return BedroomConsoleJump(null);
        yield return CheckBedroomReady("cursor bedroom checkpoint");
        yield return CaptureCursor("01-default-small-cursor.png");

        context = "cursor real bathroom entry";
        var door = V.CurrentRoom.FindDoor("bed_bath");
        Check(door != null && !door.locked && door.targetRoom == "bath" && (bool)Call(V, "AcceptsInput"),
            "real bedroom bathroom door accepts walking");
        Call(V, "Click", door.tile);
        yield return Wait(() => V.CurrentRoom?.id == "bath" && !V.IsWalking && (bool)Call(V, "AcceptsInput"),
            "real door walk reaches the bathroom");
        yield return Drive(() => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "bathroom arrival narration finishes");
        Check(story.IsAvailable && !story.BlocksHomeInput, "mirror interaction is available");
        Call(story, "OnThing", V.CurrentRoom.FindThing("bath_mirror"));
        yield return Drive(() => Messages.DisplayedEntryId == MirrorChoice && Messages.IsWaitingForOption,
            "real mirror story reaches breakdown.08", 150f);
        Messages.TryCompleteDisplayedOptionTextImmediately(MirrorChoice);
        yield return Wait(() => P.IsScreenZoomed && !P.IsScreenZoomBlinking && !P.Paused,
            "mirror option has stable screen zoom");
        yield return CaptureCursor("02-zoom-small-cursor.png");

        context = "cursor settings pause";
        settingsWindow.OpenStandalone(0f);
        yield return Wait(() => P.Paused && Ready(settingsWindow) && !P.IsScreenZoomBlinking,
            "settings pause the mirror zoom");
        yield return CaptureCursor("03-settings-small-cursor.png");
        settingsWindow.CloseSilently();
        yield return Wait(() => !P.Paused && P.IsScreenZoomed && !P.IsScreenZoomBlinking,
            "closing settings resumes the mirror zoom");
        yield return CaptureCursor("04-resumed-small-cursor.png");

        yield return Roundtrip();
        yield return Wait(() => P.IsScreenZoomed && !P.IsScreenZoomBlinking && !P.Paused,
            "restored option has stable screen zoom");
        yield return CaptureCursor("05-restored-small-cursor.png");
        yield return ClickMirrorOptionAtScreenPoint();

        context = "cursor offline recovery";
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Offline && S.memoryQueue.Count == 0
            && S.liveQueue.Count == 0 && !Messages.IsWaitingForOption && !Messages.IsReadingHoldActive
            && (Messages.LinkWindow == null || !Messages.LinkWindow.IsOpen),
            "real selected mirror story reaches offline with its reading holds completed", 150f);
        Check(S.Has("mirror_done") && S.liveOffline && P.MirrorRoot == null && !P.IsScreenZoomed,
            "offline releases the mirror and screen zoom");
        yield return CaptureCursor("07-offline-small-cursor.png");
    }

    static IEnumerator CaptureCursor(string screenshot)
    {
        context = screenshot;
        var rootCanvas = memoryWindow.GetComponentInParent<Canvas>().rootCanvas;
        var camera = rootCanvas.worldCamera;
        Check(camera != null, "actual main OS Canvas has a camera");
        var viewport = camera.pixelRect;
        Vector2 point = new Vector2(viewport.xMin + viewport.width * .82f, viewport.yMin + viewport.height * .5f);
        MoveMouse(point);
        yield return Settled();
        ObserveCursor(point, true);
        yield return Capture(screenshot);
    }

    static void MoveMouse(Vector2 point)
    {
        Check(testMouse != null && testMouse.added, "isolated input mouse is available");
        InputState.Change(testMouse, new MouseState { position = point }, InputState.currentUpdateType);
        performanceMouseChangedFrame = Time.frameCount;
        testMouse.MakeCurrent();
    }

    static void ObserveCursor(Vector2 point, bool normalSprite)
    {
        var cursor = CursorHandle.Instance;
        var main = memoryWindow.GetComponentInParent<Canvas>().rootCanvas;
        var canvas = cursor != null ? Get(cursor, "cursorCanvas") as Canvas : null;
        var icon = cursor != null ? Get(cursor, "cursorIcon") as Image : null;
        Check(cursor != null && canvas != null && icon != null && icon.sprite != null && main.worldCamera != null,
            "real cursor, sprite and both Canvases exist");
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var mainRect = (RectTransform)main.transform;
        float mainPixel = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(main.worldCamera, mainRect.TransformPoint(Vector3.zero)),
            RectTransformUtility.WorldToScreenPoint(main.worldCamera, mainRect.TransformPoint(Vector3.right)));
        Rect bounds = CursorScreenBounds(icon.rectTransform, camera);
        float cursorPixel = main.scaleFactor;
        float expectedWidth = icon.sprite.rect.width * cursorPixel;
        Vector2 actualPoint = cursor.DisplayedScreenPosition;
        observations.Add(new {
            context, type = "actual cursor projection", frame = Time.frameCount, zoom = P.IsScreenZoomed, paused = P.Paused,
            inputPoint = new[] { point.x, point.y }, mousePoint = new[] { testMouse.position.ReadValue().x, testMouse.position.ReadValue().y },
            displayedPoint = new[] { actualPoint.x, actualPoint.y }, mainPixel, cursorPixel, expectedWidth,
            cursorBounds = new[] { bounds.x, bounds.y, bounds.width, bounds.height }, spriteWidth = icon.sprite.rect.width,
            cursorLocalScale = new[] { icon.rectTransform.localScale.x, icon.rectTransform.localScale.y },
            cursorMode = canvas.renderMode.ToString(), cursorCamera = camera?.name, mainCamera = main.worldCamera.name,
            mainPlane = main.planeDistance, cursorPlane = canvas.planeDistance, canvas.sortingOrder, icon.raycastTarget,
            cursorState = cursor.CurrentState.ToString(), cursor.IsScriptControlled, cursor.ScriptScaleMultiplier,
            screen = new[] { Screen.width, Screen.height },
            projection = Enumerable.Range(0, 16).Select(i => main.worldCamera.projectionMatrix[i]).ToArray()
        });
        Check(canvas.renderMode == RenderMode.ScreenSpaceCamera && camera == main.worldCamera
            && Mathf.Abs(canvas.planeDistance - main.planeDistance) < .001f,
            "cursor uses the actual OS camera at the main Canvas plane");
        Check(canvas.sortingOrder == 1500 && !icon.raycastTarget, "cursor retains foreground sorting 1500 and does not intercept raycasts");
        Check(!cursor.IsScriptControlled && icon.isActiveAndEnabled && icon.color.a > .99f,
            "ordinary visible cursor is controlled by mouse input");
        if (normalSprite) Check(icon.sprite == cursor.NormalSprite && Mathf.Abs(icon.sprite.rect.width - 9f) < .01f,
            "ordinary cursor retains its authored 9-pixel sprite");
        Check(cursorPixel > 0f && expectedWidth > 0f && Mathf.Abs(bounds.width - expectedWidth) <= Mathf.Max(.2f, expectedWidth * .01f),
            "actual cursor width matches sprite pixels times the unzoomed Canvas pixel scale");
        Check(Vector2.Distance(testMouse.position.ReadValue(), point) <= .01f && Vector2.Distance(actualPoint, point) <= 1f,
            "actual input position and rendered cursor hotspot match the intended screen point");
        Check(main.worldCamera.pixelRect.Contains(point) && bounds.xMin >= main.worldCamera.pixelRect.xMin
            && bounds.xMax <= main.worldCamera.pixelRect.xMax && bounds.yMin >= main.worldCamera.pixelRect.yMin
            && bounds.yMax <= main.worldCamera.pixelRect.yMax, "small cursor is fully visible inside the captured viewport");
    }

    static IEnumerator ClickMirrorOptionAtScreenPoint(string label = "restored mirror option real input coordinate", string screenshot = "06-cursor-on-real-option.png")
    {
        context = label;
        var entry = Get(Messages, "activeEntry") as OSCharacterInteractionEntryData;
        Check(entry?.Options != null && entry.Options.Count > 0 && Messages.IsWaitingForOption,
            "restored real mirror option is waiting");
        Check(Messages.TryGetActiveOptionTarget(entry.Options[0].OptionId, out var target) && target != null && EventSystem.current != null,
            "actual option target and EventSystem exist");
        var camera = memoryWindow.GetComponentInParent<Canvas>().rootCanvas.worldCamera;
        var unit = target.GetComponentInParent<OSLinkMessageSelectUnit>();
        Vector2 point = CursorScreenBounds(target, camera).center;
        MoveMouse(point); yield return Settled();
        if (mirrorPerformance) ObservePerformanceCursor(point); else ObserveCursor(point, false);
        yield return Capture(screenshot);
        var pointer = new PointerEventData(EventSystem.current) {
            position = point, pressPosition = point, pointerId = -1, button = PointerEventData.InputButton.Left,
            clickCount = 1, eligibleForClick = true
        };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var first = hits.Count > 0 ? hits[0] : default;
        observations.Add(new { context, type = "real option screen-coordinate raycast", point = new[] { point.x, point.y },
            hits = hits.Take(12).Select(hit => new { target = hit.gameObject?.name, module = hit.module?.GetType().FullName,
                camera = hit.module?.eventCamera?.name, hit.sortingOrder, hit.depth, hit.distance }).ToArray() });
        Check(unit != null && unit.IsInteractable && first.gameObject != null && first.module is GraphicRaycaster
            && first.module.eventCamera == camera && first.gameObject.GetComponentInParent<OSLinkMessageSelectUnit>() == unit,
            "topmost actual GraphicRaycaster hit is the displayed option at the cursor hotspot");
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = first;
        ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        hits.Clear(); Canvas.ForceUpdateCanvases(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject != null && hits[0].gameObject.GetComponentInParent<OSLinkMessageSelectUnit>() == unit,
            "real option stays under the same cursor point after pointer down/up");
        var receiver = ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(receiver != null && (!Messages.IsWaitingForOption || Messages.DisplayedEntryId != entry.EntryId),
            "real raycast-dispatched pointer click selects breakdown.08");
    }

    static Rect CursorScreenBounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
    }

    static IEnumerator RunLabEntranceCheckpoint()
    {
        data.SetCurrentLanguage(LanguageState.zh_CN);
        Set(setup, "isDemo", true);
        yield return LabEntranceConsoleJump(null, true);
        Set(setup, "isDemo", false);
        yield return LabEntranceConsoleJump("01-console-laboratory-entry.png");
        yield return CheckLabEntranceReady("first laboratory entrance");
        yield return Capture("02-east-corridor-before-password.png");

        context = "laboratory entrance JSON restore";
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "laboratory-entrance-save.json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        Messages.NotifyOSHidden(); story.ResetPresentation(); home.Suspend(); data.ReplaceSaveData(restored);
        yield return OSSessionPersistence.Restore(restored);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        yield return CheckLabEntranceReady("JSON-restored laboratory entrance");

        yield return OpenLabEntranceKeypad();
        yield return Capture("03-real-laboratory-keypad.png");
        foreach (char digit in "0710") PerformanceModalButton("Digit" + digit).onClick.Invoke();
        PerformanceModalButton("Submit").onClick.Invoke();
        yield return Drive(() => V.CurrentRoom?.id == "lab" && PerformanceIdle(),
            "0710 enters the laboratory and completes its real offline exchange", 100f, chooseOptions: true);
        Check(story.CurrentPhase == OSChapter03StoryDirector.Phase.Lab && S.Has("lab_open")
            && !V.Layout.FindRoom("east").FindDoor("lab_door").locked && S.liveOffline,
            "correct input unlocks the door and reaches the actual offline laboratory phase");
        Check(S.items.Contains("coffee") && !S.Has("coffee_placed") && !S.Has("bed_first")
            && !S.papers.Contains("diary_13"), "laboratory entry retains the cup and leaves bedside progression for the player");
        yield return Capture("04-laboratory-after-correct-password.png");
        var previousStory = story;
        yield return LabEntranceConsoleJump(null);
        yield return CheckLabEntranceReady("repeated checkpoint from inside the laboratory");
        Check(previousStory == null, "checkpoint reload destroys the previous laboratory story instance");

        yield return OpenLabEntranceKeypad();
        var previousModal = Get(P, "modalRoot") as RectTransform;
        previousStory = story;
        yield return LabEntranceConsoleJump(null);
        yield return CheckLabEntranceReady("repeated checkpoint from an open keypad");
        Check(previousStory == null && previousModal == null, "checkpoint reload destroys the open keypad and its previous story instance");
        yield return Capture("05-clean-laboratory-entrance-after-reset.png");
    }

    static IEnumerator LabEntranceConsoleJump(string screenshot, bool expectDemoDenial = false)
    {
        context = expectDemoDenial ? "laboratory console Demo guard" : "actual laboratory entrance console entry";
        var console = GameTest.Instance.GetComponent<GameTestConsole>();
        Check(console != null, "real GameTestConsole exists");
        console.SetOpen(true); yield return Settled();
        var button = console.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == "第三章_实验室门口");
        Check(button != null && button.IsInteractable() && button.gameObject.activeInHierarchy,
            "laboratory entrance console row is active and clickable");
        Check(button.transform.Find("Title")?.GetComponent<TMPro.TMP_Text>()?.text == "实验室门口 · 输入密码前",
            "laboratory entrance row displays its Chinese title");
        var scroll = button.GetComponentInParent<ScrollRect>();
        Check(scroll != null, "laboratory row belongs to the real console scroll area");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f; yield return Settled();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, button.transform);
        Check(scroll.viewport.rect.Contains(new Vector2(bounds.center.x, bounds.center.y)),
            "laboratory row is visible within the scrolled viewport");
        if (screenshot != null) yield return Capture(screenshot);
        var before = data.SaveData;
        string beforeJson = expectDemoDenial ? JsonUtility.ToJson(before) : null;
        button.onClick.Invoke();
        if (expectDemoDenial)
        {
            Check(!GameTest.Instance.IsCheckpointJumpRunning && ReferenceEquals(data.SaveData, before)
                && beforeJson == JsonUtility.ToJson(data.SaveData)
                && GameTest.Instance.JumpStatusKey == "ui.debug_console.status_chapter03_full_game_required",
                "the real laboratory button rejects Demo mode before changing any snapshot data");
            console.SetOpen(false); yield return Settled(); yield break;
        }
        Check(GameTest.Instance.IsCheckpointJumpRunning, "the real laboratory button starts its checkpoint jump");
        os = null; memory = null; home = null; story = null; memoryWindow = liveWindow = settingsWindow = null;
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "laboratory checkpoint reload finishes", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession,
            "laboratory jump succeeds in isolated storage");
        ResolveWindows();
        yield return Wait(() => !console.IsOpen, "successful laboratory checkpoint closes its console");
    }

    static IEnumerator CheckLabEntranceReady(string label)
    {
        context = label;
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && V.CurrentRoom?.id == "east"
            && V.InputEnabled && (bool)Call(V, "AcceptsInput"), "east corridor is ready for ordinary player input");
        foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
        yield return Settled();
        Check(memory.CurrentData?.DataIndex == OSChapter03StoryContent.Instance.Node("memory.9210")
            && V.PlayerTile == new Vector2Int(13, 4) && V.DongTile == new Vector2Int(12, 4) && V.Dong == OSHomeView.DongMode.Follow,
            "checkpoint selects 9210 beside the laboratory door with the companion visible");
        Check(story.CurrentPhase == OSChapter03StoryDirector.Phase.Offline && S.liveOffline && S.clock == 3
            && S.knowAnswer == 1 && S.homeOpened && !story.IsCinematic && !story.IsMirrorPlaybackPaused,
            "checkpoint starts after the mirror and Live breakdown in the intended offline phase");
        Check(new[] { "password_ready", "mirror_done", "mirror_playback_resumed", "live_back_home", "closeup_closed",
            "deduction_done", "yard_returned", "yard_zoom_done", "offline_east", "offline_east_leak", "lab_door_seen" }.All(S.Has),
            "mirror, yard, corridor and password prerequisites are complete");
        Check(S.Has("bed_open") && S.Has("store_open") && !V.Layout.FindRoom("west").FindDoor("bed_door").locked
            && !V.CurrentRoom.FindDoor("store_door").locked && V.CurrentRoom.FindDoor("lab_door").locked,
            "prior doors stay unlocked while the laboratory still requires its password");
        Check(!S.Has("lab_open") && !S.Has("room_lab") && !S.Has("dong_absorbed") && !S.Has("bed_first")
            && !S.Has("bed_second_done") && !S.Has("coffee_placed") && !S.papers.Contains("diary_13"),
            "laboratory, bedside and later diary progression have not started");
        var expectedPapers = new[] { "note_01", "diary_02", "diary_03", "diary_04", "diary_05", "diary_06",
            "diary_07", "diary_08", "husband_letter", "note_09" };
        Check(S.papers.Count == expectedPapers.Length && expectedPapers.All(S.papers.Contains)
            && data.IsRecordDataUnlocked(OSChapter03DiaryRecord.RecordIndex)
            && OSChapter03DiaryRecord.GetCollectedPaperIds(S).OrderBy(id => id).SequenceEqual(expectedPapers.Where(id => id.StartsWith("diary_")).OrderBy(id => id)),
            "all ten outside papers are collected and Record 5 exposes only diary pages 02 through 08");
        Check(S.coffeeStep == 5 && new[] { "coffee_water", "coffee_beans", "coffee_brewed", "coffee_taken" }.All(S.Has)
            && S.items.Contains("coffee") && S.items.Contains("letters") && !S.items.Contains("key") && !S.items.Contains("screwdriver"),
            "the prepared coffee and letters remain available while the key and screwdriver have been consumed");
        var interaction = data.OSCharacterInteractionState;
        Check(interaction.HasConsumedGlobalEntry(OSChapter03StoryDirector.LiveId("live.offline.east"))
            && interaction.HasConsumedGlobalEntry(OSChapter03StoryDirector.LiveId("live.offline.initial_offer"))
            && string.IsNullOrEmpty(interaction.presentingGlobalEntryId) && string.IsNullOrEmpty(interaction.pendingGlobalEntryId),
            "the corridor cry and reply are consumed with no active or pending global message");
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && string.IsNullOrEmpty(Messages.DisplayedEntryId)
            && !Messages.IsWaitingForOption && !Messages.IsReadingHoldActive,
            "the entrance snapshot has no queued narration, reply or pre-opened keypad");
        Check(P.MirrorRoot == null && !P.IsScreenZoomed && !P.IsModalOpen && !V.Zoomed && !V.IsPlayingRules
            && !story.IsMirrorSequenceActive && !story.IsMirrorPerformanceLocked && !story.IsWaitingForMirrorResume,
            "no mirror, zoom, pause control, keypad or old cinematic remains active");
        Check(os.CharacterWindowController.CurrentState == OSCharacterWindowState.VoidMessage
            && (Get(os.CharacterWindowController, "voidMessageState") as GameObject)?.activeInHierarchy == true,
            "Live visibly remains in NoSignal at the laboratory entrance");
        Check((Get(story, "storyMusic") as AudioSource) == null && (Get(story, "fadingMusic") as AudioSource) == null,
            "the completed breakdown music does not restart at the entrance");
        observations.Add(new { context, room = V.CurrentRoom.id, player = V.PlayerTile.ToString(), companion = V.DongTile.ToString(),
            node = memory.CurrentData?.DataIndex, phase = story.CurrentPhase.ToString(), S.liveOffline, S.coffeeStep,
            papers = S.papers.ToArray(), items = S.items.ToArray(), flags = S.flags.ToArray() });
    }

    static IEnumerator OpenLabEntranceKeypad()
    {
        context = "actual laboratory door and keypad";
        var door = V.CurrentRoom.FindDoor("lab_door");
        Check(door != null && door.locked && !story.BlocksHomeInput && (bool)Call(V, "AcceptsInput"),
            "the real locked laboratory door accepts player input");
        Call(V, "Click", door.tile);
        yield return Drive(() => Head("keypad") && P.IsModalOpen, "a real Home door click opens the laboratory keypad");
        yield return Wait(() => (bool)Get(P, "CanInteract"), "keypad entrance animation completes before button input");
    }

    static IEnumerator RunBedroomCheckpoint()
    {
        context = "bedroom checkpoint Demo guard";
        data.SetCurrentLanguage(LanguageState.zh_CN);
        var beforeDemo = data.SaveData;
        Set(setup, "isDemo", true);
        Check(!GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03Bedroom)
            && !GameTest.Instance.IsCheckpointJumpRunning && ReferenceEquals(data.SaveData, beforeDemo)
            && GameTest.Instance.JumpStatusKey == "ui.debug_console.status_chapter03_full_game_required",
            "bedroom shortcut rejects Demo mode before changing its snapshot");
        Set(setup, "isDemo", false);

        yield return BedroomConsoleJump("01-console-bedroom-entry.png");
        yield return CheckBedroomReady("first bedroom entry");
        yield return Capture("02-bedroom-yellow-door.png");
        CheckYellowDoor("bed_bath", "bedroom-home-texture.png");

        context = "real bedroom to bathroom door walking";
        var door = V.CurrentRoom.FindDoor("bed_bath");
        Check(door != null && !door.locked && door.targetRoom == "bath", "bedroom bathroom door is traversable");
        Check((bool)Call(V, "AcceptsInput"), "real Home click input accepts door walking");
        Call(V, "Click", door.tile);
        yield return Wait(() => V.CurrentRoom?.id == "bath" && !V.IsWalking && (bool)Call(V, "AcceptsInput"),
            "actual door click walks and transitions into the bathroom");
        yield return Drive(() => S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "bathroom arrival narration finishes");
        Check(story.CurrentPhase == OSChapter03StoryDirector.Phase.KeyReady && !story.IsCinematic && !P.IsScreenZoomed,
            "bathroom arrival does not start the breakdown before mirror interaction");
        // Leave the threshold so neither character covers the bathroom's return-door glyph.
        Call(V, "Click", new Vector2Int(7, 5));
        yield return Wait(() => !V.IsWalking && V.PlayerTile == new Vector2Int(7, 5), "real floor click walks clear of the yellow door");
        yield return Capture("03-bathroom-yellow-door.png");
        CheckYellowDoor("bath_door", "bathroom-home-texture.png");

        context = "bedroom shortcut reaches the real mirror breakdown";
        Check(story.IsAvailable && !story.BlocksHomeInput, "mirror interaction is available after the door walk");
        var camera = memoryWindow.GetComponentInParent<Canvas>().rootCanvas.worldCamera;
        Check(camera != null, "bedroom fixture has the real OS camera");
        Matrix4x4 defaultProjection = camera.projectionMatrix;
        Call(story, "OnThing", V.CurrentRoom.FindThing("bath_mirror"));
        string firstLine = OSChapter03StoryDirector.LiveId("live.breakdown.01");
        yield return Drive(() => Messages.DisplayedEntryId == firstLine && Messages.IsReadingHoldActive
            && Messages.DisplayedTextLabel.maxVisibleCharacters == int.MaxValue && P.IsScreenZoomed
            && !P.IsScreenZoomBlinking, "real mirror interaction reaches naturally complete breakdown.01 and whole-screen zoom",
            naturalEntry: firstLine);
        yield return Capture("04-first-breakdown-screen-zoom.png");
        Check(!MatrixMatches(camera.projectionMatrix, defaultProjection), "screen zoom changes the actual OS camera projection");
        Check(S.papers.Contains("note_09") && S.Has("closeup_open") && !S.Has("mirror_done") && P.MirrorRoot != null,
            "note 9 close callback starts the unfinished mirror and zoom sequence");
        var previousStory = story;
        var previousMirror = P.MirrorRoot;

        yield return BedroomConsoleJump(null);
        yield return CheckBedroomReady("repeated bedroom entry");
        yield return Capture("05-bedroom-after-checkpoint-reset.png");
        Check(previousStory == null && previousMirror == null, "checkpoint reload destroys the previous mirror story and illustration");
        Check(!P.IsScreenZoomed && MatrixMatches(memoryWindow.GetComponentInParent<Canvas>().rootCanvas.worldCamera.projectionMatrix, defaultProjection),
            "repeated checkpoint returns its OS camera to the original projection");
        Check(!S.Has("paper_requested_note_09") && !S.papers.Contains("note_09") && !S.Has("closeup_open")
            && !S.Has("mirror_visible") && !S.Has("mirror_done"), "repeated checkpoint removes every started mirror progress flag");
    }

    static IEnumerator BedroomConsoleJump(string screenshot)
    {
        context = screenshot != null ? "actual bedroom console entry" : "same bedroom console entry after screen zoom";
        var console = GameTest.Instance.GetComponent<GameTestConsole>();
        Check(console != null, "real GameTestConsole exists");
        console.SetOpen(true);
        yield return Settled();
        var button = console.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "第三章_妈妈卧室");
        Check(button != null && button.IsInteractable() && button.gameObject.activeInHierarchy, "new bedroom console row is active and clickable");
        var title = button.transform.Find("Title")?.GetComponent<TMPro.TMP_Text>();
        Check(title != null && title.text == "妈妈卧室 · Live崩溃前", "console row displays its Chinese title");
        var scroll = button.GetComponentInParent<ScrollRect>();
        Check(scroll != null, "bedroom row belongs to the real console scroll area");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f;
        yield return Settled();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, button.transform);
        Check(scroll.viewport.rect.Contains(new Vector2(bounds.center.x, bounds.center.y)), "bedroom entry is visible within the scrolled viewport");
        if (screenshot != null) yield return Capture(screenshot);
        button.onClick.Invoke();
        Check(GameTest.Instance.IsCheckpointJumpRunning, "actual bedroom button callback starts its checkpoint jump");
        // The checkpoint reloads OS; never retain or pump its outgoing components during that reload.
        os = null; memory = null; home = null; story = null; memoryWindow = liveWindow = settingsWindow = null;
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "bedroom checkpoint reload finishes", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "bedroom jump succeeds in isolated storage");
        ResolveWindows();
        yield return Wait(() => !console.IsOpen, "successful checkpoint closes its console");
    }

    static IEnumerator CheckBedroomReady(string label)
    {
        context = label;
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && V.CurrentRoom?.id == "bed"
            && V.InputEnabled && (bool)Call(V, "AcceptsInput"), "bedroom Home is ready for ordinary walking");
        foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
        yield return Settled();
        Check(memory.CurrentData?.DataIndex == OSChapter03StoryContent.Instance.Node("memory.bed.enter")
            && V.PlayerTile == new Vector2Int(6, 1), "checkpoint selects bedroom narration and the bathroom-side starting tile");
        Check(story.CurrentPhase == OSChapter03StoryDirector.Phase.KeyReady && !story.IsCinematic
            && S.clock == 3 && S.knowAnswer == 1 && S.homeOpened, "checkpoint starts at the intended pre-breakdown story phase");
        Check(S.Has("bed_open") && !V.Layout.FindRoom("west").FindDoor("bed_door").locked
            && S.Has("store_open") && !V.Layout.FindRoom("east").FindDoor("store_door").locked,
            "key progression has unlocked the bedroom and storage doors");
        Check(S.Has("deduction_done") && S.Has("yard_returned") && S.Has("yard_zoom_done")
            && !V.Zoomed && !V.IsPlayingRules, "completed deduction and yard presentation do not replay");
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && string.IsNullOrEmpty(Messages.DisplayedEntryId)
            && !Messages.IsWaitingForOption && !Messages.IsReadingHoldActive, "checkpoint has no prior Memory or Live queue/message");
        var liveGroup = liveWindow.GetComponent<CanvasGroup>();
        Check(P.MirrorRoot == null && !P.IsScreenZoomed && !P.IsModalOpen && !S.liveOffline
            && Ready(liveWindow) && (liveGroup == null || liveGroup.alpha == 1f),
            "mirror and screen zoom are absent and ordinary Live is visible");
        // Walking is observed through the same tile-click path used by Home's pointer handler.
        Call(V, "Click", new Vector2Int(5, 1));
        yield return Wait(() => !V.IsWalking && V.PlayerTile == new Vector2Int(5, 1), "ordinary bedroom floor click completes a real step");
        Call(V, "Click", new Vector2Int(6, 1));
        yield return Wait(() => !V.IsWalking && V.PlayerTile == new Vector2Int(6, 1), "ordinary walking returns beside the bathroom door");
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && story.CurrentPhase == OSChapter03StoryDirector.Phase.KeyReady,
            "walking does not replay the opening, deduction or yard sequences");
    }

    static void CheckYellowDoor(string id, string imageName)
    {
        var door = V.CurrentRoom.FindDoor(id);
        Check(door != null && door.ink == OSHomeInk.Yellow && !door.locked, "door is yellow and unlocked: " + id);
        var ink = (byte[])Get(V, "ink"); int yellow = 0, tile = OSHomeLayout.Tile;
        for (int y = door.tile.y * tile; y < (door.tile.y + 1) * tile; y++)
            for (int x = door.tile.x * tile; x < (door.tile.x + 1) * tile; x++)
                if (ink[y * OSHomeLayout.PixelWidth + x] == 3) yellow++;
        Check(yellow > 0, "actual rendered door glyph contains visible yellow pixels: " + id);
        observations.Add(new { context, door = id, yellowPixels = yellow, player = V.PlayerTile.ToString() });
        string path = Path.Combine(OutputDirectory, imageName);
        File.WriteAllBytes(path, ((Texture2D)Get(V, "texture")).EncodeToPNG()); screenshots.Add(path);
    }

    static void ResolveWindows()
    {
        os = OS_Handle.Instance; memory = os.MemoryWindowController; home = memory.GetComponent<OSChapter03HomeController>();
        story = os.Chapter03Story; memoryWindow = memory.GetComponent<OSWindowUnit>();
        liveWindow = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        settingsWindow = os.transform.Find("Windows/WindowUnit_Option").GetComponent<OSWindowUnit>();
    }

    static IEnumerator Run()
    {
        yield return Fixture();
        yield return WallMirror();
        yield return Capture("01-bathroom-before-mirror.png");
        context = "real mirror story, message gap";
        Call(story, "OnThing", V.CurrentRoom.FindThing("bath_mirror"));
        yield return Drive(() => Head("text", "memory.9040") && S.memoryQueue[0].begun && P.MirrorRoot != null,
            "real mirror pickup and note close reach 9040");
        yield return Capture("02-mirror-without-live-message.png");
        Check(!Ready(Messages.LinkWindow), "9040 tests a gap with no Live message window");
        AssertLayers(false);
        memoryWindow.BringToFront(); yield return Settled(); AssertLayers(false);

        context = "settings pause";
        settingsWindow.OpenStandalone(0f);
        yield return Wait(() => P.Paused && Ready(settingsWindow), "settings pause the active mirror");
        yield return Capture("03-settings-over-mirror.png");
        Check(CompareDraw(P.MirrorRoot, settingsWindow.transform) < 0, "paused mirror stays behind settings");
        settingsWindow.CloseSilently();
        yield return Wait(() => !P.Paused && story.IsAvailable, "closing settings resumes the mirror");
        yield return Settled(); AssertLayers(false);

        context = "Live message and window focus";
        yield return Drive(() => Messages.DisplayedEntryId == MirrorChoice && Messages.IsWaitingForOption,
            "mirror reaches its real Live option");
        Messages.TryCompleteDisplayedOptionTextImmediately(MirrorChoice);
        yield return Capture("04-live-and-message-in-front.png"); AssertLayers(true);
        foreach (var window in new[] { memoryWindow, home.HomeWindow, liveWindow, Messages.LinkWindow })
        {
            window.BringToFront(); yield return Settled();
            Mark("after BringToFront: " + window.WindowId); AssertLayers(true);
        }
        yield return Roundtrip();
        yield return Capture("05-restored-live-option.png"); AssertLayers(true);
        Check(P.MirrorRoot.Find("Frame/MirrorFace").GetComponent<Image>().color.a == 1f,
            "restored mirror retains the fully revealed face");

        context = "mirror completion and layer release";
        ChooseCurrent();
        yield return Drive(() => story.CurrentPhase == OSChapter03StoryDirector.Phase.Offline && S.memoryQueue.Count == 0
            && S.liveQueue.Count == 0 && !Messages.IsWaitingForOption && !Messages.IsReadingHoldActive
            && (Messages.LinkWindow == null || !Messages.LinkWindow.IsOpen), "real mirror sequence finishes and closes its final message", chooseOptions: true);
        yield return Capture("06-after-mirror-sequence.png");
        Check(S.Has("mirror_done") && S.Has("live_back_home") && S.liveOffline && P.MirrorRoot == null,
            "completion removes the mirror and returns Live home offline");
        Check(memoryWindow.IsAlwaysOnTop && data.OSProcessData.memory.interactiveWindowPinned,
            "Memory remains pinned through completion");
        Check(EffectiveCanvas(liveWindow.transform) == EffectiveCanvas(memoryWindow.transform),
            "mirror and message temporary Live drawing lifts release after completion");
    }

    static IEnumerator Fixture()
    {
        context = "real checkpoint and pinned bathroom fixture";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart), "real Chapter 3 checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        ResolveWindows();
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint runs in isolated storage");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Messages.NotifyOSHidden(); story.ResetPresentation();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true)) if (window.IsOpen) window.CloseSilently();
        home.ResetRuntime(); memory.PrepareRuntime();
        var save = Clone(data.SaveData); save.flow.language = LanguageState.zh_CN;
        save.osProcess.memory.currentDataIndex = 8110; save.osProcess.memory.chapter03HomeInitialized = true;
        save.osProcess.memory.interactiveWindowPinned = true;
        save.osProcess.memory.chapter03HomeView = new OSHomeView.State {
            room = "bath", me = new Vector2Int(6, 5), dong = new Vector2Int(6, 6), dongHidden = false };
        var state = new OSChapter03StoryState { version = 1, phase = (int)OSChapter03StoryDirector.Phase.Explore, homeOpened = true, knowAnswer = 1 };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8020", "opening_8070", "opening_8080", "opening_8110", "paper_note_01" }) state.Add(flag);
        foreach (string id in new[] { "study", "west", "hall", "kitchen", "east", "store", "bed", "bath", "lab", "yard", "junk" }) state.Add("room_" + id);
        state.papers.Add("note_01"); save.osProcess.memory.chapter03Story = state;
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData(); save.osResume = new OSResumeSaveData();
        data.ReplaceSaveData(save); Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f); Check(os.OpenMemoryWindow(), "real Memory window opens");
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && V.CurrentRoom?.id == "bath" && V.InputEnabled,
            "real bathroom becomes available");
        foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
        Check(memoryWindow.IsAlwaysOnTop && data.OSProcessData.memory.interactiveWindowPinned,
            "fixture starts with the real Memory pin active");
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "fixture starts before mirror interaction with empty story queues");
    }

    static IEnumerator WallMirror()
    {
        context = "wall mirror pixels";
        var mirror = V.CurrentRoom.FindThing("bath_mirror");
        Check(mirror != null && mirror.interactive && mirror.style == OSHomeThingStyle.WallMirror, "bathroom mirror is interactive");
        var centres = new List<float>();
        foreach (int x in new[] { 5, 6, 7 })
        {
            Check(V.WalkTo(new Vector2Int(x, 5)), "real Home walking reaches mirror position " + x);
            yield return Wait(() => !V.IsWalking && V.PlayerTile == new Vector2Int(x, 5), "mirror walk completes");
            yield return Settled();
            var ink = (byte[])Get(V, "ink"); var texture = (Texture2D)Get(V, "texture");
            int tile = OSHomeLayout.Tile, width = OSHomeLayout.PixelWidth;
            int left = mirror.tile.x * tile, top = mirror.tile.y * tile, right = left + mirror.Width * tile - 1;
            var reflectionX = new List<int>(); int redFrame = 0, otherFrame = 0;
            for (int py = top; py < top + tile; py++) for (int px = left; px <= right; px++)
            {
                byte value = ink[py * width + px];
                bool border = (py == top || py == top + tile - 1) ? px > left && px < right : px == left || px == right;
                if (border && value != 0) { if (value == 2) redFrame++; else otherFrame++; }
                if (py > top && py < top + tile - 1 && px > left && px < right && value == 1) reflectionX.Add(px);
            }
            string path = Path.Combine(OutputDirectory, "wall-mirror-x" + x + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG()); screenshots.Add(path);
            Check(redFrame > 10 && otherFrame == 0, "visible interactive mirror border pixels are red at player x=" + x);
            Check(reflectionX.Count > 8, "wall mirror contains a visible gray reflection at player x=" + x);
            float centre = (float)reflectionX.Average(); centres.Add(centre);
            observations.Add(new { context, playerX = x, reflectionCentreX = centre, reflectionPixels = reflectionX.Count, redFramePixels = redFrame, otherFramePixels = otherFrame });
        }
        Check(centres[1] > centres[0] + 8f && centres[2] > centres[1] + 8f,
            "actual gray reflection pixels move right with the player across all three mirror positions");
        Check(V.WalkTo(new Vector2Int(7, 4)), "player walks beside the mirror before activation");
        yield return Wait(() => !V.IsWalking && V.PlayerTile == new Vector2Int(7, 4), "player reaches the mirror interaction tile");
    }

    static IEnumerator Roundtrip()
    {
        context = "mirror option restore";
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "mirror-option-save.json"), json);
        var restored = JsonUtility.FromJson<GameSaveData>(json);
        Messages.NotifyOSHidden(); story.ResetPresentation(); home.Suspend(); data.ReplaceSaveData(restored);
        yield return OSSessionPersistence.Restore(restored);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        yield return Drive(() => story.IsAvailable && P.MirrorRoot != null && Messages.DisplayedEntryId == MirrorChoice
            && Messages.IsWaitingForOption, "JSON and real OS session restoration recover the mirror option");
        Messages.TryCompleteDisplayedOptionTextImmediately(MirrorChoice);
        Check(S.Has("mirror_visible") && S.Has("live_moved_center") && !S.Has("mirror_done"),
            "restoration retains the unfinished mirror sequence flags");
    }

    static void AssertLayers(bool withMessage)
    {
        Check(memoryWindow.IsAlwaysOnTop && data.OSProcessData.memory.interactiveWindowPinned,
            "Memory pin remains active while checking mirror layers");
        Check(P.MirrorRoot != null && P.MirrorRoot.gameObject.activeInHierarchy && !P.Paused,
            "mirror is rendered and unpaused");
        Check(CompareDraw(home.HomeWindow.transform, P.MirrorRoot) < 0 && CompareDraw(memoryWindow.transform, P.MirrorRoot) < 0,
            "mirror draws above the pinned Memory and Home windows");
        Check(CompareDraw(P.MirrorRoot, liveWindow.transform) < 0, "Live draws above the mirror, including message gaps");
        if (withMessage)
            Check(Ready(Messages.LinkWindow) && CompareDraw(liveWindow.transform, Messages.LinkWindow.transform) < 0,
                "visible Live message draws above Live and mirror");
    }
    static Canvas EffectiveCanvas(Transform node) => node.GetComponentsInParent<Canvas>(true)
        .FirstOrDefault(canvas => canvas.isActiveAndEnabled && (canvas.isRootCanvas || canvas.overrideSorting));
    static int CompareDraw(Transform a, Transform b)
    {
        var ac = EffectiveCanvas(a); var bc = EffectiveCanvas(b);
        if (ac == null || bc == null) throw new InvalidOperationException("Missing effective Canvas");
        int order = SortingLayer.GetLayerValueFromID(ac.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(bc.sortingLayerID));
        if (order == 0) order = ac.sortingOrder.CompareTo(bc.sortingOrder);
        if (order != 0) return order;
        var ap = a.GetComponentsInParent<Transform>(true).Reverse().ToArray();
        var bp = b.GetComponentsInParent<Transform>(true).Reverse().ToArray();
        for (int i = 0; i < Math.Min(ap.Length, bp.Length); i++)
            if (ap[i] != bp[i]) return ap[i].GetSiblingIndex().CompareTo(bp[i].GetSiblingIndex());
        return ap.Length.CompareTo(bp.Length);
    }
    static object OrderState(Transform node)
    {
        if (node == null) return null;
        var canvas = EffectiveCanvas(node); var rect = node as RectTransform;
        return new { name = node.name, active = node.gameObject.activeInHierarchy, sibling = node.GetSiblingIndex(),
            canvas = canvas?.name, sortingOrder = canvas?.sortingOrder, sortingLayer = canvas?.sortingLayerID,
            anchoredPosition = rect != null ? new[] { rect.anchoredPosition.x, rect.anchoredPosition.y } : null };
    }
    static void Mark(string label) => observations.Add(new { context, label, frame = Time.frameCount,
        phase = story?.CurrentPhase.ToString(), node = memory?.CurrentData?.DataIndex, liveEntry = Messages?.DisplayedEntryId,
        mirrorPaused = P?.Paused, memoryPinned = memoryWindow?.IsAlwaysOnTop,
        memory = OrderState(memoryWindow?.transform), home = OrderState(home?.HomeWindow?.transform),
        mirror = OrderState(P?.MirrorRoot), live = OrderState(liveWindow?.transform), link = OrderState(Messages?.LinkWindow?.transform) });
    static IEnumerator Capture(string name)
    {
        yield return Settled(); Mark(name);
        string path = Path.Combine(OutputDirectory, name); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 100, "screenshot written: " + name, 10f);
    }
    static IEnumerator Settled() { Canvas.ForceUpdateCanvases(); yield return null; yield return null; }
    static void Pump(bool chooseOptions, string naturalEntry = null)
    {
        // Let the face reveal finish before accelerating the accompanying Memory line.
        // Live's first breakdown line is additionally protected by naturalEntry.
        if (!mirrorPerformance || P.MirrorRoot == null || MirrorFaceAlpha() >= 1f)
            foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
        string id = Messages.DisplayedEntryId;
        bool naturalFirstBreakdown = mirrorPerformance && id == OSChapter03StoryDirector.LiveId("live.breakdown.01");
        if (OSChapter03StoryDirector.IsOwnEntry(id) && id != naturalEntry && !naturalFirstBreakdown)
        {
            Messages.TryCompleteDisplayedTextImmediately(id); Messages.TryCompleteDisplayedOptionTextImmediately(id);
            if (chooseOptions && Messages.IsWaitingForOption) ChooseCurrent();
            if (Messages.IsReadingHoldActive && !cursorProjection && !mirrorEntryMotion && !mirrorPerformance)
            {
                var timer = Get(Messages, "autoCloseTween") as Tween;
                if (timer == null || !timer.IsActive()) throw new InvalidOperationException("Live countdown missing");
                timer.Complete(true);
            }
        }
        if (Head("paper") && P.IsModalOpen) P.HideModal(true);
    }
    static void ResumeBackgroundReadingHold()
    {
        // Keep the production LateUpdate clock intact. Only the Editor's missing
        // foreground focus may be bypassed in this opt-in isolated QA mode.
        if (Application.isFocused || Application.isBatchMode || GameAutosaveService.Instance?.IsTestSession != true
            || story?.IsAvailable != true || P == null || P.Paused || os == null || !os.IsOSVisible
            || Messages == null || !Messages.isActiveAndEnabled || !OSChapter03StoryDirector.IsOwnEntry(Messages.DisplayedEntryId)
            || !(bool)Get(Messages, "isOSVisible") || (bool)Get(Messages, "chapter03TextPaused")
            || (bool)Get(Messages, "collectionReadingPaused") || (bool)Call(Messages, "IsSuspendedByMinimize")
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || (bool)Call(Messages, "IsOptionCountdownSettingsOpen")) return;
        var link = Messages.LinkWindow;
        var owner = Get(Messages, "ownerCharacterWindow") as OSWindowUnit;
        if (!Ready(link) || owner != null && !Ready(owner)) return;
        var timer = Get(Messages, "autoCloseTween") as Tween;
        if (timer != null && timer.IsActive())
        {
            timer.Play();
            const string note = "QA background isolation resumes only focus-paused Live reading holds at their original LateUpdate pace";
            if (!checks.Contains(note)) checks.Add(note);
        }
    }
    static void ChooseCurrent()
    {
        var entry = Get(Messages, "activeEntry") as OSCharacterInteractionEntryData;
        Check(entry?.Options != null && entry.Options.Count > 0 && Messages.TrySelectDisplayedOptionImmediately(
            Messages.DisplayedSequenceId, entry.EntryId, entry.Options[0].OptionId), "real displayed Live option is selected");
    }
    static bool Head(string type, string key = null) => S.memoryQueue.Count > 0 && S.memoryQueue[0].type == type
        && (key == null || S.memoryQueue[0].key == key);
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing
        && !window.IsMinimized && window.gameObject.activeInHierarchy;
    static IEnumerator Drive(Func<bool> predicate, string label, float seconds = 70f, bool chooseOptions = false, string naturalEntry = null)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { Pump(chooseOptions, naturalEntry); yield return null; }
        Check(predicate(), label);
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static bool MatrixMatches(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .0001f) return false;
        return true;
    }
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
