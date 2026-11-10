using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Opt-in QA for chapter-four TV music. Uses the production story, audio clocks
/// and clips; no seeks, accelerated audio, fixture dialogue or treatment chart.
/// Runtime saves use an isolated directory and the real menu/Continue flow.
/// A PCM seam check is numerical evidence, not a claim of listening approval.
/// </summary>
public static class OSChapter04MusicQA
{
    const string AudioRoot = "Chapter04/Audio/";
    const string RainResource = "Chapter03Story/Audio/RainHome";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] ClipNames = {
        "Ch04_Theme_Melody", "Ch04_Theme_Accomp", "Ch04_Amb_CityLab",
        "Ch04_Amb_Wasteland", "Ch04_Amb_LabNight", "Ch04_Grief_LowDrone"
    };
    static readonly double[] ClipSeconds = { 120d, 120d, 60d, 60d, 60d, 24d };
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
    static readonly HashSet<string> heardStates = new HashSet<string>(StringComparer.Ordinal);
    static readonly Dictionary<MonoBehaviour, bool> focusStates = new Dictionary<MonoBehaviour, bool>();
    static readonly Dictionary<OSChapter04StoryContent.Beat, float> delays = new Dictionary<OSChapter04StoryContent.Beat, float>();
    static GameDataManager data;
    static GameAutosaveService saves;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04StoryDirector story;
    static OSChapter04Music music;
    static Mouse mouse;
    static Keyboard keyboard;
    static GameObject leaseProbe;
    static bool cancelled, monitor, monitoredPaused, restoredWarpExpected;
    static bool includeNaturalLoops;
    static string context, saveRoot, isolatedDirectory;
    static OSChapter04Music monitoredMusic;
    static string monitoredState;
    static OSChapter04Music.Ambience monitoredAmbience;
    static int monitoredThemeStarts, monitoredAmbienceStarts;
    static double monitoredThemeDsp;
    static int observedInitialStarts;
    static float observedInitialMelody = -1f, observedInitialAccompaniment = -1f;
    static double beganAt;
    sealed class ThemeSyncProbe
    {
        public readonly string Label;
        public readonly int OutputSampleRate, BufferLength, BufferCount;
        public readonly double BufferSeconds, MismatchLimitSeconds;
        public readonly Queue<ThemeSyncSample> Recent = new Queue<ThemeSyncSample>();
        public int Observations, ExactZeroObservations, DistinctZeroPositions, NonzeroObservations, MismatchRuns;
        public int MaximumAbsoluteDifference, LastZeroSample = -1;
        public double FirstZeroDsp = double.NaN, LastZeroDsp = double.NaN, MismatchSince = double.NaN, LongestMismatchSeconds;
        public ThemeSyncProbe(string label)
        {
            Label = label; OutputSampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int length, out int count);
            BufferLength = length; BufferCount = count;
            BufferSeconds = length / (double)Math.Max(1, OutputSampleRate);
            MismatchLimitSeconds = Math.Min(.1d, 3d * BufferSeconds);
        }
        public bool IsMismatching => !double.IsNaN(MismatchSince);
        public bool HasRepeatedExactZero => DistinctZeroPositions >= 2 && LastZeroDsp - FirstZeroDsp >= BufferSeconds;
    }
    struct ThemeSyncSample
    {
        public int frame, melody, accompaniment, difference;
        public double dspBefore, dspAfter;
    }
    static OSChapter04StoryState State => data?.OSProcessData?.memory?.chapter04Story;
    static OSLinkMessageController Link => os?.LinkMessageController;
    static OSChapter04PresentationBridge Bridge => story?.Presentation;

    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;
    public static string Poll() => JsonConvert.SerializeObject(new {
        status = Status, context, output = OutputDirectory,
        elapsedSeconds = Application.isPlaying ? Time.realtimeSinceStartupAsDouble - beganAt : 0d,
        entry = State?.currentEntryId, visited = visited.Count,
        music = MusicSnapshot()
    });

    [MenuItem("Tools/MrTGame/Chapter 4 Music/Validate Assets (Edit Mode)")]
    static void AssetMenu() => Debug.Log(RunAssetChecks());

    [MenuItem("Tools/MrTGame/Chapter 4 Music/Run Production Music QA (Fresh Play Mode)")]
    static void RuntimeMenu() => Debug.Log("[Chapter04 Music QA] " + Start());

    public static string RunAssetChecks()
    {
        if (Status == "running") return "Music QA is already running";
        if (Application.isPlaying) return "RunAssetChecks requires Edit Mode; Start includes the same checks in Play Mode";
        ResetResults(); context = "audio assets";
        try { ValidateAssets(); Status = "PASS: " + checks.Count + " checks"; }
        catch (Exception exception) { errors.Add(exception.ToString()); Status = "FAILED: " + exception.Message; }
        string report = JsonConvert.SerializeObject(new { status = Status, environmentGain = OSChapter04Music.EnvironmentGain, checks, errors, observations,
            note = "Read-only imported-asset and numerical PCM checks; no game session or player save is opened." }, Formatting.Indented);
        File.WriteAllText(Path.Combine(OutputDirectory, "assets-results.json"), report);
        return report;
    }

    public static string Start() => Start(true);
    // For focused reruns after a menu/flow-only fix. Reports explicitly omit
    // long-loop coverage; use a completed full run for that evidence.
    public static string StartFlowChecks() => Start(false);

    static string Start(bool completeLoops)
    {
        if (Status == "running") return Status;
        data = GameDataManager.Instance; saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || data == null || saves == null || GameTest.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null || SceneLoadManager.Instance == null)
            return "Initialized Play Mode required";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original save path";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || GameSessionFlow.Instance.IsBusy || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active scene/save operation";
        setup = UnityEngine.Object.FindFirstObjectByType<GameSetup>();
        if (setup == null) return "Active GameSetup required";
        ResetResults(); Status = "running"; context = "initialization";
        includeNaturalLoops = completeLoops;
        beganAt = Time.realtimeSinceStartupAsDouble;
        data.StartCoroutine(Guarded()); return Status;
    }

    static void ResetResults()
    {
        checks.Clear(); errors.Clear(); observations.Clear(); delays.Clear(); visited.Clear(); heardStates.Clear(); focusStates.Clear();
        cancelled = monitor = monitoredPaused = restoredWarpExpected = false; os = null; story = null; music = null;
        monitoredMusic = null; monitoredState = null; observedInitialStarts = 0;
        observedInitialMelody = observedInitialAccompaniment = -1f;
        OutputDirectory = Path.GetFullPath(Path.Combine("Tools", "Chapter04Music", "Validation", Guid.NewGuid().ToString("N")));
        isolatedDirectory = Path.Combine(OutputDirectory, "isolated-save");
        Directory.CreateDirectory(OutputDirectory);
    }

    static IEnumerator Guarded()
    {
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = GameSetup.IsDemo;
        int frameRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        saveRoot = Path.GetDirectoryName(Path.GetFullPath(saves.SavePath));
        string[] protectedFiles = ProtectedFiles();
        Dictionary<string, string> originalFiles = null, originalSaveDirectory = null;
        var stack = new Stack<IEnumerator>();
        Exception failure = null;
        Application.logMessageReceived += CaptureError;
        try
        {
            // Drain any previous writer before recording the protected baseline.
            saves.EnterTestSession();
            originalFiles = HashFiles(protectedFiles); originalSaveDirectory = HashDirectory(saveRoot);
            saves.UseIsolatedStorageForTests(isolatedDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); data.SetCurrentLanguage(LanguageState.zh_CN);
            Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            mouse = InputSystem.AddDevice<Mouse>("Chapter04MusicQAMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("Chapter04MusicQAKeyboard"); ReleaseInput();
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object next = null;
                try
                {
                    Focus(); ObserveFirstStart();
                    if (monitor) MonitorMusic();
                    if (cancelled) throw new OperationCanceledException("Chapter-four music QA cancelled");
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null) { errors.Add(context + ": " + failure); Status = "FAILED at " + context; break; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            if (failure == null) Status = errors.Count == 0 ? "PASS" : "FAILED: runtime errors";
        }
        finally
        {
            monitor = false;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            foreach (var pair in delays) pair.Key.autoAdvanceDelay = pair.Value;
            delays.Clear();
            try
            {
                ReleaseInput(); story?.FinalHeal?.Stop(); story?.DebugSetFocus(false);
                if (music != null) music.enabled = false;
                ReleaseLeaseProbe();
                os?.Chapter04Entry?.DebugSetFocus(false);
                foreach (var pair in focusStates) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourceEnabled;
                Application.runInBackground = background; Application.targetFrameRate = frameRate;
                QualitySettings.vSyncCount = vSync; UnityEngine.Random.state = random;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                Check(ReferenceEquals(data.SaveData, original) && GameSetup.IsDemo == demo,
                    "cleanup restores the original save object and release configuration");
                Check(SameHashes(originalFiles, HashFiles(protectedFiles))
                    && SameHashes(originalSaveDirectory, HashDirectory(saveRoot)),
                    "all original save files, scenes, story data and FinalHeal audio originals remain byte-identical");
            }
            catch (Exception exception) { errors.Add("cleanup: " + exception); Status = "FAILED during cleanup"; }
            Application.logMessageReceived -= CaptureError;
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS" && errors.Count != 0) Status = "FAILED: cleanup runtime errors";
            if (Status == "PASS") Status += ": " + checks.Count + " checks";
            var report = new { status = Status, context, environmentGain = OSChapter04Music.EnvironmentGain,
                elapsedSeconds = Time.realtimeSinceStartupAsDouble - beganAt, includeNaturalLoops,
                checks, errors, observations, visited = visited.OrderBy(value => value).ToArray(),
                theatreStates = heardStates.OrderBy(value => value).ToArray(), protectedFiles, originalFiles,
                finalFiles = HashFiles(protectedFiles), originalSaveDirectory, finalSaveDirectory = HashDirectory(saveRoot),
                note = "Production story and real audio clocks. includeNaturalLoops states whether this run measures full natural loops without seeking. LabNight, Grave, grief tiers 2/3 and the warning use isolated disk plus real menu/Continue. Automatic reading waits are temporarily manual and restored; no text or assets are edited. PCM seam evidence does not replace listening." };
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter04 Music QA] " + Status + " | " + OutputDirectory);
        }
    }

    static IEnumerator Run()
    {
        context = "audio assets"; ValidateAssets();
        var content = OSChapter04StoryContent.Instance;
        Check(content.Available && content.Validate(data.OSCharacterInteractionData).Count == 0,
            "the production story resolves before its audio route is exercised");
        foreach (var beat in content.Beats)
        {
            if (beat.endAction == OSChapter04StoryContent.FailureWarningAction) continue;
            delays.Add(beat, beat.autoAdvanceDelay); beat.autoAdvanceDelay = -1f;
        }
        context = "first TV opening / CityWalk";
        observedInitialStarts = 0; observedInitialMelody = observedInitialAccompaniment = -1f;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "production console checkpoint 19 accepts the route");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance?.Chapter04Story?.IsAvailable == true,
            "checkpoint opens the real TV and first Live line", 90f);
        Resolve();
        yield return Wait(() => music != null && music.IsRunning && !music.IsPaused
            && Playing(music.MelodySource) && Playing(music.AccompanimentSource), "both theme tracks start", 8f);
        Check(State.currentEntryId == content.FirstEntryId && Bridge.CurrentState == OSChapter04PresentationBridge.CityWalk
            && music.TheatreState == OSChapter04PresentationBridge.CityWalk,
            "actual first CityWalk starts music before CityArrive is requested");
        Check(music.ThemeStarts == 1 && observedInitialStarts == 1 && music.ThemeStartDsp > 0d,
            "the first TV opening creates one shared theme scheduling epoch");
        Check(observedInitialMelody >= 0f && observedInitialMelody < EnvironmentLevel(.49f)
            && observedInitialAccompaniment >= 0f && observedInitialAccompaniment < EnvironmentLevel(.49f),
            "both theme gains are observed during their initial fade-in");
        CheckThemeSources(); yield return CheckSynchronized("initial scheduled playback");
        yield return Wait(() => Near(music.MelodySource.volume, EnvironmentLevel(.5f))
            && Near(music.AccompanimentSource.volume, EnvironmentLevel(.5f)),
            "both themes reach 0.5 times EnvironmentGain after the two-second fade", 3f);
        yield return SettledAmbience(OSChapter04Music.Ambience.CityLab, EnvironmentLevel(.35f));
        ResetMonitor();
        yield return VerifyPaperPushSuppression();
        yield return VerifyWindowPauses();
        yield return FullLoops("theme pair and CityLab", music.MelodySource, music.AccompanimentSource, music.AmbienceSource);
        yield return CheckSynchronized("after an entire natural 120-second theme loop");
        yield return ProductionRoute();
    }

    static IEnumerator ProductionRoute()
    {
        bool wastelandLooped = false, nightLooped = false, rainLooped = false;
        for (int guard = 0; guard < 150 && !State.completed; guard++)
        {
            string id = State.currentEntryId;
            context = "production line / " + id;
            yield return Wait(() => story.IsAvailable && Link != null && Link.IsPresenting
                && Link.DisplayedEntryId == id && !story.IsOptionGestureBlocked,
                "actual Live line is ready: " + id, 25f);
            var beat = story.Content.Find(id);
            Check(beat != null && !beat.legacyResumeOnly, "route uses a current authored entry");
            Check(visited.Add(id), "the production route visits this entry once");
            if (id == "chapter04_story_grave_07") yield return TierOne();
            if (id == "chapter04_story_grave_10")
            {
                yield return TierTwo();
                yield return SaveAndContinue("grief-tier-2", OSChapter04Music.Ambience.Rain, 2);
            }
            if (id == "chapter04_story_grave_13")
            {
                yield return TierThree();
                yield return FullLoops("grief low drone", music.DroneSource);
                yield return SaveAndContinue("grief-tier-3", OSChapter04Music.Ambience.Rain, 3);
            }
            if (id == "chapter04_story_grave_16")
            {
                yield return WarningAndTreatment();
                Check(heardStates.Contains("CityWalk") && heardStates.Contains("WastelandWalk")
                    && heardStates.Contains("LabNight") && heardStates.Contains("Grave") && heardStates.Contains("GraveBreakdown"),
                    "the natural route exercised city, wasteland, night lab, grave and breakdown music");
                yield break;
            }
            var priorAmbience = music.CurrentAmbience;
            int priorAmbienceStarts = music.AmbienceStarts;
            var priorSource = music.AmbienceSource;
            if (!State.currentTextCompleted) Check(story.TryCompleteCurrentText(), "real Live completion input is accepted");
            yield return Wait(() => State.currentTextCompleted && State.currentCueCommitted && State.currentResourcesCommitted,
                "text and authored cue receipts finish", 10f);
            if (!string.IsNullOrEmpty(beat.theatreStateAfterText))
            {
                string target = beat.theatreStateAfterText;
                var expected = ExpectedAmbience(target);
                yield return Wait(() => music.CurrentAmbience == expected, "audio follows the authored stage request: " + target, 3f);
                if (expected != priorAmbience)
                {
                    Check(music.AmbienceStarts == priorAmbienceStarts + 1 && music.AmbienceSource != priorSource,
                        "an environment change starts exactly one different ambience source");
                    Check(priorSource != null && priorSource.isPlaying && priorSource.volume > 0f
                        && music.AmbienceSource.volume < AmbienceGain(target) && music.FadingAmbienceSource == priorSource,
                        "old and new ambience coexist during the crossfade");
                    observations.Add(new { type = "ambience crossfade", from = priorAmbience.ToString(), to = expected.ToString(),
                        oldSource = SourceSnapshot(priorSource), newSource = SourceSnapshot(music.AmbienceSource) });
                    yield return SettledAmbience(expected, AmbienceGain(target));
                    // A gain tolerance can be reached before the fade's final
                    // frame, especially after the environmental trim. Observe
                    // the real stop instead of treating near-zero as finished.
                    yield return Wait(() => !priorSource.isPlaying && priorSource.volume == 0f,
                        "the old ambience stops after its crossfade", .2f);
                }
                else Check(music.AmbienceStarts == priorAmbienceStarts, "a new stage with the same ambience does not replay it");
                if (target == OSChapter04PresentationBridge.CityArrive)
                    yield return Wait(() => Bridge.CurrentState == OSChapter04PresentationBridge.AtDoor, "natural arrival reaches AtDoor", 20f);
                if (target == OSChapter04PresentationBridge.EnterDoor)
                    yield return Wait(() => Bridge.CurrentState == OSChapter04PresentationBridge.LabRummage, "natural door sequence reaches the lab", 12f);
                if (expected == OSChapter04Music.Ambience.Wasteland && !wastelandLooped)
                { wastelandLooped = true; yield return FullLoops("Wasteland ambience", music.AmbienceSource); }
                if (expected == OSChapter04Music.Ambience.LabNight && !nightLooped)
                {
                    nightLooped = true; yield return FullLoops("LabNight ambience", music.AmbienceSource);
                    yield return SaveAndContinue("lab-night", OSChapter04Music.Ambience.LabNight, 0);
                }
                if (expected == OSChapter04Music.Ambience.Rain && !rainLooped)
                { rainLooped = true; yield return FullLoops("reused rain ambience", music.AmbienceSource); }
                if (target == OSChapter04PresentationBridge.Grave)
                {
                    yield return SettledAmbience(OSChapter04Music.Ambience.Rain, EnvironmentLevel(.12f));
                    yield return SaveAndContinue("grave", OSChapter04Music.Ambience.Rain, 0);
                }
            }
            if (id == "chapter04_story_39")
            {
                yield return Wait(() => story.CanUseMotherKey || State.currentEntryId != id, "authored mother-key gate becomes usable", 15f);
                if (State.currentEntryId == id) Check(story.TryUnlockMotherHeart(), "the real mother-key handler opens the heart window");
                yield return Wait(() => State.currentEntryId != id, "opening the real mother heart advances the authored gate", 15f);
            }
            else
            {
                yield return Wait(() => !story.IsOptionGestureBlocked && (beat.options.Length > 0 || story.CanContinue),
                    "normal continuation is ready", 15f);
                Check(beat.options.Length > 0 ? story.TrySelectOption(beat.options[0].optionId) : story.TryContinue(),
                    "the normal authored option or Continue handler accepts input");
                yield return Wait(() => State.currentEntryId != id || State.completed, "the authored successor is reached", 15f);
            }
            yield return null;
        }
        throw new InvalidOperationException("Production route ended before the final failure warning");
    }

    static IEnumerator TierOne()
    {
        context = "natural grief tier 1";
        yield return Wait(() => music.GriefTier == 1, "tier 1 is reflected by music", 2f);
        float at = music.MusicClock, initial = music.MelodySource.volume;
        yield return Hold(.65f);
        Check(music.MelodySource.volume < initial && music.MelodySource.volume > 0f
            && Near(music.AccompanimentSource.volume, EnvironmentLevel(.5f)), "only melody fades during tier 1");
        yield return Wait(() => music.MelodySource.volume == 0f && !music.MelodySource.isPlaying,
            "melody reaches zero and stops within its two-second transition", 2f);
        Check(music.MusicClock - at <= 2.2f && Near(music.AccompanimentSource.volume, EnvironmentLevel(.5f)),
            "tier 1 preserves full accompaniment and completes its authored fade");
        Check(!music.MelodySource.isPlaying && Playing(music.AccompanimentSource),
            "the inaudible melody stops while the accompaniment continues");
    }

    static IEnumerator TierTwo()
    {
        context = "natural grief tier 2";
        yield return Wait(() => music.GriefTier == 2, "tier 2 begins at grave line 10", 2f);
        yield return Hold(.4f);
        Check(music.AccompanimentLowPass != null && music.AccompanimentLowPass.cutoffFrequency > 1500f
            && music.AccompanimentLowPass.cutoffFrequency < 18000f, "tier 2 begins the gradual low-pass transition");
        yield return SettingsPause("tier-2 warp and filter pause");
        var pitches = new List<float>();
        float until = Time.realtimeSinceStartup + 5f;
        while (music.GriefWarpSeconds < 4.1f && Time.realtimeSinceStartup < until)
        {
            CheckWarp(); pitches.Add(music.AccompanimentSource.pitch); yield return null;
        }
        Check(music.GriefWarpSeconds >= 4f && Mathf.Abs(music.AccompanimentLowPass.cutoffFrequency - 1500f) < 2f,
            "tier 2 reaches r=1 and the 1500 Hz terminal filter");
        Check(pitches.Count > 5 && pitches.Max() - pitches.Min() > .001f,
            "the accompaniment pitch actually varies while the tape warp grows");
        observations.Add(new { type = "natural tier 2", warpSeconds = music.GriefWarpSeconds,
            minimumPitch = pitches.Min(), maximumPitch = pitches.Max(), sourcePitch = music.AuthoredSourcePitch,
            cutoff = music.AccompanimentLowPass.cutoffFrequency });
    }

    static IEnumerator TierThree()
    {
        context = "natural grief tier 3";
        yield return Wait(() => music.GriefTier == 3, "tier 3 begins at grave line 13", 2f);
        float at = music.MusicClock;
        yield return Hold(.65f);
        Check(music.AccompanimentSource.volume > 0f && music.AccompanimentSource.volume < EnvironmentLevel(.5f)
            && music.DroneSource.volume > 0f && music.DroneSource.volume < EnvironmentLevel(.25f),
            "tier 3 crossfades accompaniment into the low drone");
        CheckWarp();
        yield return Wait(() => music.AccompanimentSource.volume == 0f && !music.AccompanimentSource.isPlaying
            && Near(music.DroneSource.volume, EnvironmentLevel(.25f)),
            "tier 3 reaches its terminal mix after three seconds", 3.5f);
        Check(music.MusicClock - at <= 3.25f && music.MelodySource.volume < .001f
            && Near(music.AmbienceSource.volume, EnvironmentLevel(.24f)), "tier 3 leaves only low drone and full grave rain audible");
    }

    static IEnumerator VerifyWindowPauses()
    {
        yield return SettingsPause("settings pause");
        var live = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        context = "TV minimize pause"; os.TVWindow.Minimize(); yield return PausedSamples(); os.OpenTVWindow(); yield return ResumedSamples();
        context = "TV close pause"; os.TVWindow.Close(); yield return PausedSamples(); os.OpenTVWindow(); yield return ResumedSamples();
        context = "Live minimize pause"; live.Minimize(); yield return PausedSamples(); os.OpenCharacterWindowForReconnect(0f); yield return ResumedSamples();
        context = "Live close pause"; live.Close(); yield return PausedSamples(); os.OpenCharacterWindowForReconnect(0f); yield return ResumedSamples();
    }

    static IEnumerator VerifyPaperPushSuppression()
    {
        string before = context; context = "TV ownership / PaperPush window";
        var paper = os.GetComponent<OSPaperPushMusic>();
        var window = os.transform.Find("Windows/WindowUnit_PaperPush")?.GetComponent<OSWindowUnit>();
        Check(paper != null && window != null && HasBackgroundLease(music),
            "the real PaperPush audio owner and window resolve while TV holds its lease");
        int themeStarts = music.ThemeStarts;
        Check(os.OpenPaperPushWindow(), "the real PaperPush window can open during the TV chapter");
        try
        {
            yield return Wait(() => window.IsOpen && !window.IsOpening && !window.IsClosing
                && !window.IsMinimized && window.gameObject.activeInHierarchy, "PaperPush finishes opening normally", 8f);
            float until = Time.realtimeSinceStartup + .4f;
            while (Time.realtimeSinceStartup < until)
            {
                Check(paper.Source == null || !paper.Source.isPlaying,
                    "PaperPush's independent source stays stopped while the TV owns background audio", false);
                Check(music.IsRunning && !music.IsPaused && Playing(music.MelodySource)
                    && Playing(music.AccompanimentSource) && music.ThemeStarts == themeStarts,
                    "opening PaperPush neither stops nor restarts the TV theme", false);
                yield return null;
            }
            Check(paper.Source == null || !paper.Source.isPlaying,
                "the usable PaperPush window cannot introduce an overlapping independent song");
            observations.Add(new { type = "PaperPush under TV ownership", windowOpen = window.IsOpen,
                paperSource = SourceSnapshot(paper.Source), themeStarts, music = MusicSnapshot() });
        }
        finally { if (window != null) window.Close(); }
        yield return Wait(() => !window.IsOpen && !window.IsClosing && story.IsAvailable && !music.IsPaused,
            "closing the probe window restores the normal TV flow", 8f);
        context = before;
    }

    static IEnumerator SettingsPause(string label)
    {
        string before = context; context = label;
        var settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        Check(settings != null, "the real settings window resolves");
        settings.OpenStandalone(); yield return PausedSamples();
        settings.CloseFromScript(); yield return ResumedSamples(); context = before;
    }

    static IEnumerator PausedSamples()
    {
        yield return Wait(() => music.IsPaused, "music pauses with the real gameplay window state", 5f);
        yield return Hold(.12f); // Allow the audio thread to consume the pause command.
        var sources = Sources(); var samples = sources.Select(source => source.timeSamples).ToArray();
        var volumes = sources.Select(source => source.volume).ToArray();
        float clock = music.MusicClock, warp = music.GriefWarpSeconds;
        int starts = music.ThemeStarts, ambienceStarts = music.AmbienceStarts;
        yield return Hold(.4f);
        Check(music.IsPaused && sources.All(source => !source.isPlaying)
            && sources.Select(source => source.timeSamples).SequenceEqual(samples), "all chapter music samples remain frozen while paused");
        Check(Mathf.Abs(music.MusicClock - clock) < .001f && Mathf.Abs(music.GriefWarpSeconds - warp) < .001f
            && sources.Select(source => source.volume).SequenceEqual(volumes), "pause freezes the music clock, warp and fades");
        Check(music.ThemeStarts == starts && music.AmbienceStarts == ambienceStarts, "pausing does not create new playback starts");
    }

    static IEnumerator ResumedSamples()
    {
        int themeStarts = music.ThemeStarts, ambienceStarts = music.AmbienceStarts;
        yield return Wait(() => story.IsAvailable && !music.IsPaused && Playing(music.AccompanimentSource),
            "music resumes after the window is restored", 8f);
        int sample = music.AccompanimentSource.timeSamples; float clock = music.MusicClock;
        yield return Hold(.2f);
        Check(music.AccompanimentSource.timeSamples != sample && music.MusicClock > clock
            && music.ThemeStarts == themeStarts && music.AmbienceStarts == ambienceStarts,
            "resume advances the existing tracks without a restart");
        if (music.GriefTier < 2) yield return CheckSynchronized("pause/resume preserves the theme sample alignment");
    }

    static IEnumerator FullLoops(string label, params AudioSource[] sources)
    {
        if (!includeNaturalLoops)
        {
            observations.Add(new { type = "natural loops omitted in focused flow rerun", label });
            yield break;
        }
        string before = context; context = "natural complete loops / " + label;
        Check(sources.Length > 0 && sources.All(source => source != null && source.clip != null && source.loop && source.isPlaying),
            "all requested production sources are playing and looped");
        var clips = sources.Select(source => source.clip).ToArray();
        var previous = sources.Select(source => source.timeSamples).ToArray();
        var accumulated = new long[sources.Length]; var wraps = new int[sources.Length];
        var sync = music.GriefTier == 0 ? new ThemeSyncProbe("natural loop theme alignment / " + label) : null;
        double started = AudioSettings.dspTime;
        double allowance = sources.Max(source => source.clip.samples / (double)source.clip.frequency / Math.Max(.1f, source.pitch)) + 12d;
        float deadline = Time.realtimeSinceStartup + (float)allowance;
        while (accumulated.Where((value, i) => value < clips[i].samples).Any() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            for (int i = 0; i < sources.Length; i++)
            {
                Check(sources[i] != null && sources[i].clip == clips[i] && sources[i].isPlaying && sources[i].loop,
                    "a complete loop is never replaced, stopped or paused", false);
                int current = sources[i].timeSamples, delta = current - previous[i];
                if (delta < 0) { delta += clips[i].samples; wraps[i]++; }
                accumulated[i] += delta; previous[i] = current;
            }
            if (sync != null) ObserveThemeSync(sync);
        }
        Check(accumulated.Where((value, i) => value < clips[i].samples || wraps[i] < 1).Any() == false,
            "each requested source plays an entire clip and crosses a real sample wrap without seeking");
        if (sync != null)
        {
            // Do not end a successful loop on an unresolved nonzero sample.
            // The same watchdog still rejects a persistent offset during this tail.
            while (sync.IsMismatching) { yield return null; ObserveThemeSync(sync); }
            Check(sync.HasRepeatedExactZero, "the natural loop repeatedly observes exact zero sample difference at advancing positions");
            observations.Add(ThemeSyncSnapshot(sync));
        }
        observations.Add(new { type = "natural complete loops", label, elapsedDspSeconds = AudioSettings.dspTime - started,
            clips = clips.Select(clip => clip.name).ToArray(), samples = clips.Select(clip => clip.samples).ToArray(), accumulatedSamples = accumulated, wraps });
        context = before;
    }

    static IEnumerator SaveAndContinue(string label, OSChapter04Music.Ambience ambience, int tier, bool warning = false)
    {
        context = "isolated disk save and Continue / " + label;
        monitor = false; ReleaseInput();
        string id = State.currentEntryId; int visit = State.currentVisit;
        string history = HistorySnapshot(State);
        var oldMusic = music; var oldSources = Sources();
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        Check(saves.FlushNow(), "actual isolated disk flush succeeds"); saves.EndSession();
        var stored = ReadSnapshot();
        Check(stored.osProcess.memory.chapter04Story.currentEntryId == id
            && stored.osProcess.memory.chapter04Story.griefTier == tier, "disk stores the actual story line and grief tier");
        File.WriteAllText(Path.Combine(OutputDirectory, label + "-logical-save.json"), JsonUtility.ToJson(stored, true));
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "real return-to-menu accepts the saved session");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !GameSessionFlow.Instance.IsBusy, "menu transition completes", 60f);
        Check(oldSources.All(source => source == null || !source.isPlaying), "old chapter sources stop before Continue");
        yield return Wait(() => !HasBackgroundLease(oldMusic) && !HasSfxLease(oldMusic),
            "the old owner releases its music and SFX leases at the menu", .25f);
        yield return Wait(IsMenuAmbientPlaying,
            "the menu resumes its actual Main_Core ambience after chapter ownership ends", 2f);
        Check(GameSessionFlow.Instance.ContinueGame(), "real Continue accepts the isolated on-disk save");
        yield return Wait(() => !GameSessionFlow.Instance.IsBusy && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance?.Chapter04Story != null && saves.SessionActive,
            "Continue reconstructs the actual OS scene", 90f);
        Resolve();
        restoredWarpExpected = tier >= 2;
        yield return Wait(() => music != null && music.GriefTier == tier
            && (warning ? music.IsWarningSilent : music.IsRunning && !music.IsPaused && Playing(tier == 3 ? music.DroneSource : music.AccompanimentSource)),
            "the new audio owner restores the saved music state", 10f);
        Check(oldMusic == null && music != null && music != oldMusic, "Continue destroys the old music owner and creates a new one");
        Check(State.currentEntryId == id && State.currentVisit == visit && HistorySnapshot(State) == history,
            "audio restoration does not replay the saved story visit or history");
        if (warning)
        {
            Check(music.IsWarningSilent && Sources().All(source => !source.isPlaying || source.volume < .001f),
                "restored warning starts silent without a musical transition");
            int heartbeatPlays = music.WarningHeartbeatPlays;
            yield return Hold(.9f);
            Check(music.WarningHeartbeatPlays == heartbeatPlays && heartbeatPlays == 0,
                "restoring the visible warning never repeats its one-shot heartbeat");
        }
        else
        {
            Check(music.CurrentAmbience == ambience && Near(music.MelodySource.volume, tier >= 1 ? 0f : EnvironmentLevel(.5f))
                && Near(music.AccompanimentSource.volume, tier == 3 ? 0f : EnvironmentLevel(.5f))
                && Near(music.DroneSource.volume, tier == 3 ? EnvironmentLevel(.25f) : 0f),
                "restoration applies the terminal tier mix immediately instead of replaying fades");
            Check(Near(music.AmbienceSource.volume, AmbienceGain(music.TheatreState)),
                "restoration selects the correct ambience and rain strength");
            if (tier < 3)
            {
                var source = music.AccompanimentSource;
                Check(source.timeSamples / (double)source.clip.frequency < 2d && music.ThemeStarts == 1,
                    "a restored theme begins at the start of the file exactly once");
            }
            if (tier >= 2)
            {
                Check(Mathf.Abs(music.AccompanimentLowPass.cutoffFrequency - 1500f) < 2f,
                    "restored grief uses full warp and the terminal 1500 Hz low-pass");
                CheckWarp();
            }
            else yield return CheckSynchronized("restored normal theme starts in sample alignment");
            int starts = music.ThemeStarts, ambienceStarts = music.AmbienceStarts;
            yield return Hold(.4f);
            Check(music.ThemeStarts == starts && music.AmbienceStarts == ambienceStarts,
                "the same restore revision is consumed once instead of restarting every frame");
            CheckAmbienceFilters(music.AmbienceSource); ResetMonitor();
        }
        observations.Add(new { type = "real disk Continue", label, id, tier, music = MusicSnapshot() });
        saves.EnterTestSession();
    }

    static IEnumerator WarningAndTreatment()
    {
        context = "natural warning silence and heartbeat";
        int previousPlays = music.WarningHeartbeatPlays;
        if (!State.currentTextCompleted) Check(story.TryCompleteCurrentText(), "the actual last grave line completes");
        yield return Wait(() => OSChapter04GriefPresentation.Current?.IsFailureWarningVisible == true,
            "the authored warning delay shows its real window", 8f);
        double seenAt = AudioSettings.dspTime;
        float maximumFrameAllowance = Mathf.Max(.025f, Time.unscaledDeltaTime * 2f);
        yield return Wait(() => music.IsWarningSilent && BedSources().All(source => source == null || source.volume < .001f),
            "the warning silences theme, drone and rain", .2f);
        double silenceElapsed = music.WarningSilencedDsp - music.WarningStartedDsp;
        observations.Add(new { type = "warning silence command timing", music.WarningStartedDsp, music.WarningSilencedDsp,
            silenceElapsed, qaFirstObservedWarningDsp = seenAt, qaObservedSilenceDsp = AudioSettings.dspTime });
        Check(!double.IsNaN(silenceElapsed) && silenceElapsed >= 0d && silenceElapsed <= .05d
            && BedSources().All(source => source == null || !source.isPlaying),
            "all TV voices are zeroed and stopped within 50 ms of the actual warning trigger");
        double heartbeatAt = -1d;
        float deadline = Time.realtimeSinceStartup + 1.3f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (music.WarningHeartbeatPlays > previousPlays && heartbeatAt < 0d)
            {
                heartbeatAt = AudioSettings.dspTime;
                Check(music.WarningHeartbeatSource != null && music.WarningHeartbeatSource.isPlaying,
                    "the delayed warning heartbeat is an actual playing source");
                Check(Near(music.WarningHeartbeatSource.volume, .45f)
                    && music.WarningHeartbeatSource.outputAudioMixerGroup == AudioManager.Instance.SFXMixerGroup,
                    "the warning heartbeat keeps its untrimmed 0.45 gain and SFX mixer routing");
            }
            yield return null;
        }
        Check(music.WarningHeartbeatPlays == previousPlays + 1 && heartbeatAt > 0d
            && heartbeatAt - music.WarningStartedDsp >= .6d - maximumFrameAllowance - .025d
            && heartbeatAt - music.WarningStartedDsp <= .6d + maximumFrameAllowance + .05d,
            "the natural warning plays exactly one heartbeat around its authored 0.6-second offset");
        observations.Add(new { type = "natural warning timing", silenceElapsed, observedHeartbeatOffset = heartbeatAt - music.WarningStartedDsp,
            maximumFrameAllowance, nominalSilenceSeconds = .05f, nominalHeartbeatSeconds = .6f });
        CheckWarningSfxGate();
        yield return SaveAndContinue("visible-warning", OSChapter04Music.Ambience.Rain, 3, true);
        context = "warning-to-treatment audio handoff";
        var grief = OSChapter04GriefPresentation.Current;
        yield return Wait(() => grief != null && grief.IsFailureWarningVisible && grief.WarningButton != null
            && grief.WarningButton.interactable, "the restored warning accepts its actual button", 10f);
        CheckWarningSfxGate();
        // A second, temporary owner proves that stopping this music component
        // releases only its own leases rather than clearing another owner's.
        leaseProbe = new GameObject("Chapter04MusicQA Lease Probe");
        AudioManager.Instance.SetBackgroundAudioSuppressed(leaseProbe, true, 0f);
        AudioManager.Instance.SetSfxClipsSuppressed(leaseProbe, true, SfxClip("HeartBeat00"));
        var oldSources = Sources();
        grief.WarningButton.onClick.Invoke();
        yield return Wait(() => story.FinalHeal?.IsRunning == true, "the real warning button starts the existing final treatment", 10f);
        yield return Wait(() => !music.IsRunning && oldSources.All(source => source == null || !source.isPlaying),
            "all TV music and warning sources Stop when final treatment begins", 1f);
        Check(!HasBackgroundLease(music) && !HasSfxLease(music)
            && HasBackgroundLease(leaseProbe) && HasSfxLease(leaseProbe),
            "treatment handoff releases only the chapter music owner's leases");
        CheckHealHeartbeatPlayable();
        ReleaseLeaseProbe();
        Check(!HasSfxLease(music), "the stopped chapter owner leaves no stale heartbeat gate");
        var battle = story.FinalHeal.DebugAudioSession?.BattleSource;
        yield return Wait(() => battle != null && battle.isPlaying && battle.timeSamples > 0,
            "the untouched final-treatment Battle source starts normally", 4f);
        Check(battle.clip == Resources.Load<AudioClip>(story.FinalHeal.Content.MusicResource),
            "the handoff uses the authored final-treatment resource");
        Check(AudioManager.Instance.MusicMixerGroup != null && battle.outputAudioMixerGroup == AudioManager.Instance.MusicMixerGroup,
            "final-treatment Battle keeps its existing Music mixer routing");
        observations.Add(new { type = "treatment handoff", chapterSources = oldSources.Select(SourceSnapshot).ToArray(),
            battle = SourceSnapshot(battle), finalTreatmentStage = State.finalTreatmentStage.ToString(),
            chapterBackgroundLease = HasBackgroundLease(music), chapterHeartbeatLease = HasSfxLease(music),
            treatmentBackgroundSuppressionStillHeld = AudioManager.Instance.IsBackgroundAudioSuppressed });
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "the final menu cleanup uses the real session flow");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !GameSessionFlow.Instance.IsBusy, "the final menu return completes", 60f);
        yield return Wait(() => oldSources.All(source => source == null || !source.isPlaying)
            && (battle == null || !battle.isPlaying),
            "leaving the chapter stops its music and completes the treatment's authored two-second menu fade", 3f);
        yield return Wait(IsMenuAmbientPlaying, "the final return restores the real menu ambience", 2f);
    }

    static bool IsMenuAmbientPlaying()
    {
        var audio = AudioManager.Instance;
        var clip = data?.AudioData?.AMBIENT?.Find(item => item != null && item.keyid == "Main_Core")?.audioclip;
        return clip != null && audio != null && new[] { audio.AmbientSource_00, audio.AmbientSource_01 }
            .Any(source => source != null && source.clip == clip && source.isPlaying && source.volume > 0f
                && source.outputAudioMixerGroup == audio.AmbientMixerGroup);
    }

    // Match the production save serializer: JsonUtility restores null strings
    // as empty. Keep the exact visit/order/options comparison, treating only
    // that representation change as equivalent (as the deployment QA does).
    static string HistorySnapshot(OSChapter04StoryState state) => JsonConvert.SerializeObject(
        state.history.Select(record => record == null ? null : new {
            record.visit, entryId = record.entryId ?? string.Empty, optionId = record.optionId ?? string.Empty }));

    static void ValidateAssets()
    {
        Check(Mathf.Abs(OSChapter04Music.EnvironmentGain - .27f) < .00001f,
            "the TV environment trim uses the confirmed 0.27 gramophone calibration");
        for (int i = 0; i < ClipNames.Length; i++)
        {
            string name = ClipNames[i]; var clip = Resources.Load<AudioClip>(AudioRoot + name);
            Check(clip != null, "required chapter clip loads: " + name);
            string path = AssetDatabase.GetAssetPath(clip);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            Check(path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && importer != null,
                "the loop remains an imported WAV: " + name);
            Check(clip.preloadAudioData && (clip.loadType == AudioClipLoadType.DecompressOnLoad
                || clip.loadType == AudioClipLoadType.CompressedInMemory), "the authored clip is preloaded in memory: " + name);
            Check(!importer.forceToMono && Regex.IsMatch(File.ReadAllText(path + ".meta"), @"(?m)^\s*normalize:\s*0\s*$"),
                "import preserves the authored channels and disables normalization: " + name);
            Check(Math.Abs(clip.samples / (double)clip.frequency - ClipSeconds[i]) < .01d,
                "the imported sample duration matches the authored loop: " + name);
            if (i < 2) Check(clip.samples == 5292000 && clip.frequency == 44100,
                "the theme has exactly 5,292,000 frames at 44.1 kHz: " + name);
            var head = new float[clip.channels]; var tail = new float[clip.channels];
            Check(clip.GetData(head, 0) && clip.GetData(tail, clip.samples - 1), "loop endpoint PCM is readable: " + name);
            int windowFrames = Mathf.Min(clip.frequency, clip.samples / 2);
            var headWindow = new float[windowFrames * clip.channels];
            var tailWindow = new float[windowFrames * clip.channels];
            Check(clip.GetData(headWindow, 0) && clip.GetData(tailWindow, clip.samples - windowFrames),
                "the first and last second of ordinary PCM are readable: " + name);
            Check(head.Concat(tail).Concat(headWindow).Concat(tailWindow)
                .All(value => !float.IsNaN(value) && !float.IsInfinity(value)),
                "the loop endpoint and comparison PCM samples are finite: " + name);
            var seamSteps = head.Zip(tail, (a, b) => Mathf.Abs(a - b)).ToArray();
            var localP99 = new float[clip.channels];
            var seamToP99 = new float?[clip.channels];
            for (int channel = 0; channel < clip.channels; channel++)
            {
                var ordinaryDifferences = new float[2 * (windowFrames - 1)];
                for (int frame = 1; frame < windowFrames; frame++)
                {
                    int sample = frame * clip.channels + channel;
                    ordinaryDifferences[frame - 1] = Mathf.Abs(headWindow[sample] - headWindow[sample - clip.channels]);
                    ordinaryDifferences[windowFrames - 1 + frame - 1] = Mathf.Abs(tailWindow[sample] - tailWindow[sample - clip.channels]);
                }
                Array.Sort(ordinaryDifferences);
                localP99[channel] = ordinaryDifferences[Mathf.Clamp(
                    Mathf.CeilToInt(ordinaryDifferences.Length * .99f) - 1, 0, ordinaryDifferences.Length - 1)];
                seamToP99[channel] = localP99[channel] > 0f ? seamSteps[channel] / localP99[channel] : (float?)null;
            }
            observations.Add(new { type = "import and PCM seam", name, path, clip.channels, clip.frequency, clip.samples,
                duration = clip.samples / (double)clip.frequency, loadType = clip.loadType.ToString(),
                maxEndpointStep = seamSteps.Max(), endpointStepPerChannel = seamSteps,
                localOrdinaryDifferenceP99PerChannel = localP99, seamToLocalP99RatioPerChannel = seamToP99,
                comparisonWindowSecondsPerEnd = windowFrames / (double)clip.frequency,
                seamAssessment = "Measurement only: ordinary adjacent differences from the first/last second exclude the loop seam. No absolute jump or ratio is an authored audio-quality pass threshold, and these numbers do not establish listening approval." });
        }
        var melody = Resources.Load<AudioClip>(AudioRoot + ClipNames[0]);
        var accompaniment = Resources.Load<AudioClip>(AudioRoot + ClipNames[1]);
        Check(melody.samples == accompaniment.samples && melody.frequency == accompaniment.frequency
            && melody.channels == accompaniment.channels, "theme stems have identical sample grids and channel counts");
        Check(Resources.Load<AudioClip>(RainResource) != null, "chapter four reuses the existing chapter-three RainHome resource");
    }

    static void CheckThemeSources()
    {
        Check(music.MelodySource != null && music.AccompanimentSource != null && music.MelodySource != music.AccompanimentSource
            && music.MelodySource.clip == Resources.Load<AudioClip>(AudioRoot + ClipNames[0])
            && music.AccompanimentSource.clip == Resources.Load<AudioClip>(AudioRoot + ClipNames[1])
            && music.MelodySource.loop && music.AccompanimentSource.loop,
            "two distinct looped sources hold the authored theme stems");
        CheckEnvironmentRouting();
        Check(Near(music.MelodySource.pitch, music.AuthoredSourcePitch)
            && Near(music.AccompanimentSource.pitch, music.AuthoredSourcePitch), "both normal theme stems use the same authored source pitch");
        Check(music.AccompanimentLowPass != null && (!music.AccompanimentLowPass.enabled
            || music.AccompanimentLowPass.cutoffFrequency >= 17990f),
            "the normal accompaniment starts unfiltered or with an open 18000 Hz low-pass");
        var highPass = music.MelodySource.GetComponent<AudioHighPassFilter>();
        var lowPass = music.MelodySource.GetComponent<AudioLowPassFilter>();
        Check((highPass == null || !highPass.enabled) && (lowPass == null || !lowPass.enabled),
            "the melody is not given the TV ambience filter");
    }

    static void CheckWarningSfxGate()
    {
        Check(HasBackgroundLease(music) && HasSfxLease(music), "the visible warning holds only owner-scoped background and heartbeat leases");
        var panelBeat = SfxClip("HeartBeat00");
        Check(panelBeat != null && AudioManager.Instance.PlaySfx(panelBeat, 0f) == null,
            "the actual SFX entry point suppresses ordinary panel heartbeat during the warning");
        CheckHealHeartbeatPlayable();
    }

    static void CheckEnvironmentRouting()
    {
        var audio = AudioManager.Instance;
        var loops = BedSources();
        Check(audio != null && audio.AmbientMixerGroup != null && loops.Length == 5
            && loops.All(source => source.loop && source.outputAudioMixerGroup == audio.AmbientMixerGroup),
            "all five TV loop sources, including both theme stems and the drone, use the Ambient mixer");
        Check(music.WarningHeartbeatSource != null && audio.SFXMixerGroup != null
            && music.WarningHeartbeatSource.outputAudioMixerGroup == audio.SFXMixerGroup,
            "the separate warning heartbeat uses the SFX mixer");
    }

    static void CheckHealHeartbeatPlayable()
    {
        var clip = SfxClip("HeartBeat_ForHeal");
        Check(clip != null && clip != SfxClip("HeartBeat00"), "treatment heartbeat has its own authored clip");
        // This zero-volume probe owns only the returned pooled source. It does
        // not alter, stop, or replace any running treatment source.
        AudioSource probe = null;
        try
        {
            probe = AudioManager.Instance.PlaySfx(clip, 0f);
            Check(probe != null && probe.isPlaying && probe.clip == clip,
                "the owner-scoped warning gate still allows HeartBeat_ForHeal through the real SFX API");
        }
        finally { if (probe != null) probe.Stop(); }
    }

    static AudioClip SfxClip(string key) => data?.AudioData?.SFX?.Find(item => item != null && item.keyid == key)?.audioclip;
    static bool HasBackgroundLease(UnityEngine.Object owner)
    {
        var audio = AudioManager.Instance;
        var owners = audio == null ? null : typeof(AudioManager).GetField("backgroundAudioSuppressors", Fields)?.GetValue(audio) as IEnumerable;
        if (owners == null) throw new InvalidOperationException("The background lease registry cannot be inspected");
        return owners.Cast<object>().Any(value => ReferenceEquals(value, owner));
    }
    static bool HasSfxLease(UnityEngine.Object owner)
    {
        var audio = AudioManager.Instance;
        var leases = audio == null ? null : typeof(AudioManager).GetField("sfxClipSuppressions", Fields)?.GetValue(audio) as IEnumerable;
        if (leases == null) throw new InvalidOperationException("The SFX lease registry cannot be inspected");
        return leases.Cast<object>().Any(value => ReferenceEquals(value.GetType().GetProperty("Owner", Fields)?.GetValue(value), owner));
    }
    static void ReleaseLeaseProbe()
    {
        if (leaseProbe == null) return;
        AudioManager.Instance?.SetBackgroundAudioSuppressed(leaseProbe, false);
        AudioManager.Instance?.SetSfxClipsSuppressed(leaseProbe, false);
        UnityEngine.Object.Destroy(leaseProbe); leaseProbe = null;
    }

    static IEnumerator CheckSynchronized(string label)
    {
        var probe = new ThemeSyncProbe(label);
        float deadline = Time.realtimeSinceStartup + .2f;
        do
        {
            ObserveThemeSync(probe);
            if (!probe.IsMismatching && probe.HasRepeatedExactZero)
            {
                Check(true, label + ": exact zero sample difference repeats across advancing DSP positions");
                observations.Add(ThemeSyncSnapshot(probe));
                yield break;
            }
            yield return null;
        }
        while (Time.realtimeSinceStartup < deadline);
        observations.Add(ThemeSyncSnapshot(probe));
        throw new InvalidOperationException(label + ": no repeated exact-zero observation across advancing samples within 0.2 seconds");
    }

    static void ObserveThemeSync(ThemeSyncProbe probe)
    {
        // AudioSource.timeSamples values are separate reads, not one atomic
        // multi-source snapshot. A suspicious read must recover to EXACT zero
        // on later frames; buffer-sized differences are never accepted as zero.
        double before = AudioSettings.dspTime;
        int melody = music.MelodySource.timeSamples;
        int accompaniment = music.AccompanimentSource.timeSamples;
        double after = AudioSettings.dspTime;
        int difference = melody - accompaniment;
        int length = music.MelodySource.clip.samples;
        if (difference > length / 2) difference -= length;
        else if (difference < -length / 2) difference += length;
        var sample = new ThemeSyncSample { frame = Time.frameCount, melody = melody, accompaniment = accompaniment,
            difference = difference, dspBefore = before, dspAfter = after };
        if (probe.Recent.Count == 24) probe.Recent.Dequeue();
        probe.Recent.Enqueue(sample); probe.Observations++;
        probe.MaximumAbsoluteDifference = Math.Max(probe.MaximumAbsoluteDifference, Math.Abs(difference));
        if (melody == accompaniment)
        {
            probe.ExactZeroObservations++;
            if (melody != probe.LastZeroSample) { probe.DistinctZeroPositions++; probe.LastZeroSample = melody; }
            if (double.IsNaN(probe.FirstZeroDsp)) probe.FirstZeroDsp = after;
            probe.LastZeroDsp = after;
            if (probe.IsMismatching)
                probe.LongestMismatchSeconds = Math.Max(probe.LongestMismatchSeconds, after - probe.MismatchSince);
            probe.MismatchSince = double.NaN;
            return;
        }
        probe.NonzeroObservations++;
        if (!probe.IsMismatching) { probe.MismatchSince = after; probe.MismatchRuns++; }
        double elapsed = after - probe.MismatchSince;
        probe.LongestMismatchSeconds = Math.Max(probe.LongestMismatchSeconds, elapsed);
        if (elapsed > probe.MismatchLimitSeconds)
        {
            observations.Add(ThemeSyncSnapshot(probe));
            throw new InvalidOperationException(probe.Label + ": persistent nonzero theme offset for "
                + elapsed.ToString("F6") + " DSP seconds (limit " + probe.MismatchLimitSeconds.ToString("F6")
                + "); exact samples " + melody + "/" + accompaniment + ", circular difference " + difference);
        }
    }

    static object ThemeSyncSnapshot(ThemeSyncProbe probe) => new {
        type = "theme sample alignment", label = probe.Label,
        outputSampleRate = probe.OutputSampleRate, dspBufferLength = probe.BufferLength, dspBufferCount = probe.BufferCount,
        dspBufferSeconds = probe.BufferSeconds, maximumAllowedNonzeroSeconds = probe.MismatchLimitSeconds,
        clipSampleRate = music.MelodySource.clip.frequency,
        clipFramesPerDspBufferAtUnitPitch = probe.BufferLength * music.MelodySource.clip.frequency / (double)Math.Max(1, probe.OutputSampleRate),
        probe.Observations, probe.ExactZeroObservations, probe.DistinctZeroPositions, probe.NonzeroObservations,
        probe.MismatchRuns, probe.MaximumAbsoluteDifference, probe.LongestMismatchSeconds,
        recoveredToExactZero = !probe.IsMismatching, recent = probe.Recent.ToArray(),
        music.ThemeStartDsp, music.ThemeStarts,
        measurementPolicy = "Only exact equal samples are zero. Nonzero reads are observed on later frames and fail if persistent beyond three DSP buffers, capped at 100 ms. Checkpoints require repeated exact-zero observations at advancing sample positions. No seek, reschedule, correction or buffer-sized alignment tolerance is applied."
    };

    static void CheckWarp()
    {
        float t = music.GriefWarpSeconds, r = restoredWarpExpected ? 1f : Mathf.Clamp01(t / 4f);
        float expected = (1f - .006f * r - .008f * r * Mathf.Sin(2f * Mathf.PI * .4f * t)
            - .003f * r * Mathf.Sin(2f * Mathf.PI * 1.7f * t)) * music.AuthoredSourcePitch;
        Check(Mathf.Abs(music.AccompanimentSource.pitch - expected) < .0003f,
            "the accompaniment follows the authored two-sine tape-warp formula", false);
    }

    static IEnumerator SettledAmbience(OSChapter04Music.Ambience expected, float gain)
    {
        yield return Wait(() => music.CurrentAmbience == expected && music.AmbienceSource != null
            && music.AmbienceSource.isPlaying && Mathf.Approximately(music.AmbienceSource.volume, gain),
            "ambience reaches its authored terminal gain: " + expected + "/" + gain, 2.5f);
        Check(music.AmbienceSource.clip == ExpectedClip(expected) && music.AmbienceSource.loop,
            "the requested ambience uses the correct loop resource");
        CheckEnvironmentRouting();
        CheckAmbienceFilters(music.AmbienceSource);
    }

    static void CheckAmbienceFilters(AudioSource source)
    {
        var hp = source?.GetComponent<AudioHighPassFilter>(); var lp = source?.GetComponent<AudioLowPassFilter>();
        Check(hp != null && hp.enabled && Mathf.Abs(hp.cutoffFrequency - 300f) < 1f
            && lp != null && lp.enabled && Mathf.Abs(lp.cutoffFrequency - 4000f) < 1f,
            "ambience, including rain, has 300 Hz high-pass and 4000 Hz low-pass TV filtering");
    }

    static OSChapter04Music.Ambience ExpectedAmbience(string state)
    {
        switch (state)
        {
            case "CityWalk": case "CityArrive": case "AtDoor": case "EnterDoor": case "DoorLocked":
            case "FoldToLab": case "LabRummage": case "LabTidy": case "MedicineReady": return OSChapter04Music.Ambience.CityLab;
            case "WastelandWalk": case "SettlementWait": case "TentCare": case "NorthSettlement":
            case "GirlHome": case "FlowerWalk": return OSChapter04Music.Ambience.Wasteland;
            case "LabNight": return OSChapter04Music.Ambience.LabNight;
            case "FlowerWalkRain": case "Grave": case "GravePlaceFlowers": case "GraveBreakdown": return OSChapter04Music.Ambience.Rain;
            default: return OSChapter04Music.Ambience.None;
        }
    }

    static AudioClip ExpectedClip(OSChapter04Music.Ambience ambience)
    {
        string name = ambience == OSChapter04Music.Ambience.CityLab ? ClipNames[2]
            : ambience == OSChapter04Music.Ambience.Wasteland ? ClipNames[3]
            : ambience == OSChapter04Music.Ambience.LabNight ? ClipNames[4] : null;
        return Resources.Load<AudioClip>(ambience == OSChapter04Music.Ambience.Rain ? RainResource : AudioRoot + name);
    }

    static float EnvironmentLevel(float authoredGain) => authoredGain * OSChapter04Music.EnvironmentGain;
    static float AmbienceGain(string state) => EnvironmentLevel(state == "FlowerWalkRain" ? .08f : state == "Grave" ? .12f
        : state == "GravePlaceFlowers" ? .18f : state == "GraveBreakdown" ? .24f : .35f);

    static void MonitorMusic()
    {
        if (music == null || !music.IsRunning || music.IsWarningSilent) return;
        if (monitoredMusic != music) { ResetMonitor(); return; }
        // Resuming shifts the scheduled epoch by the pause duration. A stage
        // change must not introduce any other scheduling change or extra start.
        if (music.IsPaused || monitoredPaused) monitoredThemeDsp = music.ThemeStartDsp;
        monitoredPaused = music.IsPaused;
        Check(music.ThemeStarts == monitoredThemeStarts && music.ThemeStartDsp.Equals(monitoredThemeDsp),
            "scene changes never restart the existing chapter theme", false);
        if (music.TheatreState == monitoredState) return;
        var expected = ExpectedAmbience(music.TheatreState);
        Check(expected != OSChapter04Music.Ambience.None && music.CurrentAmbience == expected,
            "state-to-ambience mapping: " + music.TheatreState + " -> " + expected);
        if (expected == monitoredAmbience)
            Check(music.AmbienceStarts == monitoredAmbienceStarts, "same-environment stage change preserves the current ambience playback");
        heardStates.Add(music.TheatreState);
        observations.Add(new { type = "natural music state", entry = State?.currentEntryId, music = MusicSnapshot() });
        monitoredState = music.TheatreState; monitoredAmbience = music.CurrentAmbience; monitoredAmbienceStarts = music.AmbienceStarts;
    }

    static void ResetMonitor()
    {
        monitoredMusic = music; monitoredState = music.TheatreState; monitoredAmbience = music.CurrentAmbience;
        monitoredThemeStarts = music.ThemeStarts; monitoredAmbienceStarts = music.AmbienceStarts; monitoredThemeDsp = music.ThemeStartDsp;
        monitoredPaused = music.IsPaused;
        heardStates.Add(music.TheatreState); monitor = true;
    }

    static void ObserveFirstStart()
    {
        if (observedInitialStarts != 0 || music == null || music.ThemeStarts < 1) return;
        observedInitialStarts = music.ThemeStarts;
        observedInitialMelody = music.MelodySource != null ? music.MelodySource.volume : -1f;
        observedInitialAccompaniment = music.AccompanimentSource != null ? music.AccompanimentSource.volume : -1f;
        observations.Add(new { type = "first theme start", music = MusicSnapshot() });
    }

    static void Resolve()
    {
        os = OS_Handle.Instance; story = os?.Chapter04Story; music = os?.GetComponent<OSChapter04Music>();
        Check(os != null && story != null && music != null && Bridge != null, "the production story, bridge and music owner resolve");
        GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false); Focus();
    }

    static void Focus()
    {
        mouse?.MakeCurrent(); keyboard?.MakeCurrent();
        if (OS_Handle.Instance != null)
        {
            os = OS_Handle.Instance; story = os.Chapter04Story; music = os.GetComponent<OSChapter04Music>();
            story?.DebugSetFocus(true); story?.FinalHeal?.DebugSetFocus(true); os.Chapter04Entry?.DebugSetFocus(true);
            foreach (var owner in new MonoBehaviour[] { os.CursorRelease, os.Chapter03Story, os.Chapter03Entry,
                os.Chapter02FinalTalk, os.GetComponent<OSChapter02NightfallPresentation>() })
            {
                if (owner == null) continue;
                var field = owner.GetType().GetField("hasFocus", Fields);
                if (field == null || field.FieldType != typeof(bool)) continue;
                if (!focusStates.ContainsKey(owner)) focusStates.Add(owner, (bool)field.GetValue(owner));
                field.SetValue(owner, true);
            }
            GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
        }
    }

    static void ReleaseInput()
    {
        if (mouse != null) InputState.Change(mouse, new MouseState { position = new Vector2(-10000, -10000) }, InputState.currentUpdateType);
        if (keyboard != null) InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType);
    }

    static AudioSource[] BedSources() => music == null ? new AudioSource[0] : new[] { music.MelodySource,
        music.AccompanimentSource, music.DroneSource, music.AmbienceSource, music.FadingAmbienceSource }.Where(source => source != null).Distinct().ToArray();
    static AudioSource[] Sources() => music == null ? new AudioSource[0] : BedSources().Concat(new[] { music.WarningHeartbeatSource })
        .Where(source => source != null).Distinct().ToArray();
    static object SourceSnapshot(AudioSource source) => source == null ? null : new {
        source.name, clip = source.clip != null ? source.clip.name : null, source.isPlaying, source.loop, source.volume,
        source.pitch, source.timeSamples, sourceId = source.GetInstanceID(),
        mixerGroup = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : null
    };
    static object MusicSnapshot() => music == null ? null : new {
        music.IsRunning, music.IsPaused, music.IsWarningSilent, music.TheatreState, music.GriefTier,
        ambience = music.CurrentAmbience.ToString(), music.ThemeStartDsp, music.ThemeStarts, music.AmbienceStarts,
        music.WarningHeartbeatPlays, music.MusicClock, music.GriefWarpSeconds, music.AuthoredSourcePitch,
        music.WarningStartedDsp, music.WarningSilencedDsp,
        environmentGain = OSChapter04Music.EnvironmentGain,
        lowPass = music.AccompanimentLowPass != null ? music.AccompanimentLowPass.cutoffFrequency : 0f,
        sources = Sources().Select(SourceSnapshot).ToArray()
    };
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(predicate(), label);
    }
    static IEnumerator Hold(float seconds)
    { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    static bool Playing(AudioSource source) => source != null && source.isPlaying && source.clip != null && source.timeSamples > 0;
    static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .003f;
    static void Check(bool value, string description, bool record = true)
    { if (!value) throw new InvalidOperationException(description); if (record) checks.Add(context + ": " + description); }
    static void CaptureError(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static GameSaveData ReadSnapshot()
    {
        var read = new GameAutosaveStorage(isolatedDirectory).Read();
        if (!read.Success) throw new IOException("Read isolated music save: " + read.Error);
        return JsonUtility.FromJson<GameSaveData>(read.Payload);
    }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Fields).SetValue(owner, value);
    static string[] ProtectedFiles() => new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp",
        "Assets/Resources/Chapter04/story.json", "Assets/Resources/Chapter04/FinalHeal/chart.json",
        "Assets/Data/OSCharacterInteractionData.asset", "Assets/Data/LocalizationData.asset", "Assets/Data/HeartHealData.asset" }
        .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories))
        .Concat(Directory.GetFiles("Assets/Resources/Chapter04/FinalHeal/Audio", "*", SearchOption.AllDirectories))
        .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
    static Dictionary<string, string> HashDirectory(string path) => HashFiles(Directory.Exists(path)
        ? Directory.GetFiles(path, "*", SearchOption.AllDirectories) : new string[0]);
    static Dictionary<string, string> HashFiles(IEnumerable<string> paths) => paths.Distinct(StringComparer.OrdinalIgnoreCase)
        .ToDictionary(Path.GetFullPath, Hash, StringComparer.OrdinalIgnoreCase);
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    static bool SameHashes(Dictionary<string, string> a, Dictionary<string, string> b) => a != null && b != null
        && a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string hash) && hash == pair.Value);
}
