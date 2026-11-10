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

/// <summary>
/// Opt-in live-audio integration run. Never seeks or substitutes a music clock:
/// the director asks for input on the same sampled clock as its normal Update.
/// Holds only the ending's presentation clock while the real 72s loop wraps.
/// </summary>
public static class OSChapter04FinalHealMusicQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string AudioRoot = "Chapter04/FinalHeal/Audio/";
    sealed class Press
    {
        public string id;
        public double time, holdEnd;
    }
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly List<object> observations = new List<object>(), hits = new List<object>();
    static GameDataManager data;
    static GameAutosaveService saves;
    static GameSetup setup;
    static OSChapter04StoryDirector story;
    static OSChapter04FinalHealDirector director;
    static Press[] presses;
    static int nextPress, lateMotherPresses;
    static double heldUntil, maxError, lateMaxError, previousInputClock, currentInputClock;
    static float motherWaitBegan, lateFirstPress, lateLastPress, lateSecondReadyAt, endingClock;
    static bool lateRoute, cancelled;
    static string context;
    static OSChapter04FinalHealPresentation P => OSChapter04FinalHealPresentation.Current;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    [MenuItem("Tools/MrTGame/Chapter 4 Final Heal/Run Live Music QA")]
    static void StartMenu() => Debug.Log("[Final Heal Music QA] " + Start());

    public static string Start()
    {
        if (Status == "running") return Status;
        data = GameDataManager.Instance; saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || data == null || saves == null || GameTest.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null)
            return "Initialized Play Mode required";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original save path";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance?.IsBusy == true || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active scene/save operation";
        setup = UnityEngine.Object.FindFirstObjectByType<GameSetup>();
        if (setup == null) return "Active GameSetup required";
        checks.Clear(); errors.Clear(); observations.Clear(); hits.Clear();
        cancelled = false; maxError = lateMaxError = 0d; story = null; director = null;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter04FinalHealMusicQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        Status = "running"; context = "initialization";
        data.StartCoroutine(Guarded()); return Status;
    }

    public static string Poll() => JsonConvert.SerializeObject(new {
        status = Status, context, output = OutputDirectory, music = director?.MusicTime,
        hitCount = director?.HitCount, missCount = director?.MissCount, maximumHitErrorSeconds = maxError,
        maximumLateRouteHitErrorSeconds = lateMaxError,
        waiting = director?.IsWaitingForMother, lateMotherPresses, sources = AudioSnapshot()
    });

    static IEnumerator Guarded()
    {
        var original = data.SaveData; var language = data.CurrentLanguageState;
        var resourceSystem = GameResourceSystem.Instance;
        bool resourcesEnabled = resourceSystem != null && resourceSystem.enabled;
        bool background = Application.runInBackground, demo = GameSetup.IsDemo;
        int frameRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
        var random = UnityEngine.Random.state;
        string[] protectedFiles = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp",
            "Assets/Data/HeartHealData.asset", "Assets/Resources/Chapter04/FinalHeal/chart.json" }
            .Concat(Directory.GetFiles("Assets/Script/UI/Action/Chapter04FinalHeal", "*.cs"))
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] before = protectedFiles.Select(Hash).ToArray();
        var stack = new Stack<IEnumerator>(); Exception failure = null;
        Application.logMessageReceived += OnError;
        try
        {
            saves.EnterTestSession(); saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            data.SetCurrentLanguage(LanguageState.zh_CN);
            typeof(GameSetup).GetField("isDemo", Members).SetValue(setup, false);
            if (resourceSystem != null) resourceSystem.enabled = false;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object next = null;
                try
                {
                    if (cancelled) throw new OperationCanceledException("Live music QA cancelled");
                    story?.DebugSetFocus(true); director?.DebugSetFocus(true);
                    OS_Handle.Instance?.Chapter04Entry?.DebugSetFocus(true);
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null) { Status = "FAILED at " + context + ": " + failure; break; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            if (failure == null) Status = errors.Count == 0 ? "PASS" : "FAILED: runtime errors";
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            try
            {
                DetachInput(); director?.Stop();
                if (P != null) { P.SetPresentationClock(null); P.ClearAll(); }
                story?.DebugSetFocus(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                typeof(GameSetup).GetField("isDemo", Members).SetValue(setup, demo);
                if (resourceSystem != null) resourceSystem.enabled = resourcesEnabled;
                Application.runInBackground = background; Application.targetFrameRate = frameRate;
                QualitySettings.vSyncCount = vSync; UnityEngine.Random.state = random;
                Check(ReferenceEquals(data.SaveData, original) && protectedFiles.Select(Hash).SequenceEqual(before),
                    "cleanup restores the original save object and preserves all formal saves/scenes/data/frontend files");
            }
            catch (Exception exception) { Status = "FAILED during cleanup: " + exception + "\n" + Status; }
            Application.logMessageReceived -= OnError;
            if (Status == "running") Status = "FAILED: interrupted";
            if (Status == "PASS" && errors.Count != 0) Status = "FAILED: cleanup runtime errors";
            if (Status == "PASS") Status += ": " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, errors, precisionRoute = "on-time", maximumHitErrorSeconds = maxError,
                maximumLateRouteHitErrorSeconds = lateMaxError,
                hits, observations, protectedFiles, originalHashes = before, finalHashes = protectedFiles.Select(Hash).ToArray(),
                note = "Two real-time Battle runs with no seek or synthetic clock. First run hits all117 notes; second waits5s at mother. CalmLoop plays through one real sample wrap. Seam check is numerical PCM continuity, not a claim of human listening approval."
            }, Formatting.Indented));
            Debug.Log("[Final Heal Music QA] " + Status + " | " + OutputDirectory);
        }
    }

    static IEnumerator Run()
    {
        ValidateAudio();
        context = "production entry";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04FinalHeartHeal), "final-treatment checkpoint accepts entry");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance?.Chapter04Story != null
            && !GameManager.Instance.IsTransitioning, "checkpoint reconstructs OS", 90f);
        story = OS_Handle.Instance.Chapter04Story; story.DebugSetFocus(true);
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        yield return Wait(() => story.IsFailureWarningPending && OSChapter04GriefPresentation.Current?.IsFailureWarningVisible == true,
            "real warning reaches confirmation", 15f);
        OSChapter04GriefPresentation.Current.WarningButton.onClick.Invoke();
        yield return Wait(() => story.FinalHeal?.IsRunning == true, "warning starts real treatment", 15f);
        director = story.FinalHeal; director.DebugSetFocus(true);
        StartInput(false);
        context = "full117-note live Battle";
        yield return Wait(() => director.IsEnding, "all notes and both timely mother taps complete", 125f);
        FreezeEnding();
        Check(director.HitCount == 117 && director.MissCount == 0, "all117 production notes hit with zero misses");
        Check(nextPress == presses.Length, "the live test player consumes every authored input");
        Check(!director.IsWaitingForMother && Source("FinalHeal_MamaWait")?.isPlaying != true
            && Source("FinalHeal_Bloom")?.isPlaying != true, "timely mother route does not start wait or Bloom");
        observations.Add(new { route = "on-time", director.HitCount, director.MissCount, maxError, sources = AudioSnapshot() });
        yield return Wait(() => PlayingAt("FinalHeal_CalmLoop", .05d), "CalmLoop starts at scheduled111s", 8f);
        var battle = Source("FinalHeal_Battle"); var loop = Source("FinalHeal_CalmLoop");
        Check(battle != null && battle.isPlaying && battle.timeSamples / (double)battle.clip.frequency >= 111d,
            "Battle continues its natural tail while CalmLoop is audible");
        double overlapOffset = battle.timeSamples / (double)battle.clip.frequency - loop.timeSamples / (double)loop.clip.frequency;
        observations.Add(new { type = "scheduled111 overlap", overlapOffset, sources = AudioSnapshot() });
        Check(Math.Abs(overlapOffset - 111d) <= .03d, "on-time scheduled overlap aligns to111s within30ms");
        context = "CalmLoop real72s wrap";
        int previous = loop.timeSamples; bool wrapped = false;
        double loopObservedAt = AudioSettings.dspTime;
        float deadline = Time.realtimeSinceStartup + 78f;
        while (!wrapped && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            Check(loop != null && loop.isPlaying, "Loop remains active through the ending hold", false);
            int sample = loop.timeSamples;
            wrapped = sample < previous && previous > loop.clip.samples / 2;
            previous = sample;
        }
        Check(wrapped && AudioSettings.dspTime - loopObservedAt > 70d, "CalmLoop completes one actual72-second sample wrap");
        Check(Source("FinalHeal_Battle")?.isPlaying != true, "Battle ends naturally after its115-second tail");
        observations.Add(new { type = "real loop wrap", elapsedDspSeconds = AudioSettings.dspTime - loopObservedAt,
            loop.timeSamples, loop.clip.samples, sources = AudioSnapshot() });

        context = "late mother live Battle";
        DetachInput(); director.Stop(); P.SetPresentationClock(null);
        story.State.finalTreatmentStage = OSChapter04FinalTreatmentStage.FinalHeal;
        story.State.finalHealSection = 1;
        Check(director.Begin(story, true, false), "late route restarts Battle from zero using production Begin");
        director.DebugSetFocus(true); StartInput(true);
        yield return Wait(() => director.IsWaitingForMother, "missed first mother window enters wait", 115f);
        double waitClock = director.MusicTime;
        yield return Wait(() => PlayingAt("FinalHeal_MamaWait", .01d), "wait loop begins on its scheduled beat", 2f);
        yield return new WaitForSecondsRealtime(1f);
        Check(director.IsWaitingForMother && Math.Abs(director.MusicTime - waitClock) < .001d
            && Source("FinalHeal_Battle")?.isPlaying != true && !P.IsPaused,
            "waiting freezes only the score clock and fades Battle while frontend animations keep running");
        yield return Wait(() => lateMotherPresses == 1, "first delayed mother receives its own input", 7f);
        Check(!director.IsEnding && director.DebugAudioSession.Phase == OSChapter04FinalHealAudioSession.MusicPhase.Waiting
            && Source("FinalHeal_Bloom")?.isPlaying != true && P.LaneNoteCount == 1,
            "first delayed tap leaves wait music playing and does not start the final bloom");
        double lastMotherTime = director.Content.Notes.First(note => note.IsLastMother).HitTime;
        yield return Wait(() => director.MusicTime >= lastMotherTime, "remaining mother advances to its own line", 2f);
        yield return new WaitForSecondsRealtime(.15f);
        Check(!director.IsEnding && Math.Abs(director.MusicTime - lastMotherTime) < .001d && P.LaneNoteCount == 1,
            "remaining mother stops on the line while the second input is withheld");
        yield return Wait(() => director.IsEnding, "second delayed input releases the final bloom", 2f);
        FreezeEnding();
        Check(lateMotherPresses == 2 && lateLastPress - motherWaitBegan >= 5f,
            "late route waits at least5s then consumes exactly two mother inputs");
        Check(lateLastPress - lateFirstPress >= .75f,
            "late mother inputs are separated by the remaining note's travel instead of an immediate double tap");
        yield return Wait(() => PlayingAt("FinalHeal_Bloom", .02d), "second late mother begins Bloom from its start", 2f);
        var bloom = Source("FinalHeal_Bloom");
        Check(bloom.timeSamples / (double)bloom.clip.frequency < .5d, "Bloom sample clock starts near zero on late second press");
        yield return new WaitForSecondsRealtime(.3f);
        Check(Source("FinalHeal_MamaWait")?.isPlaying != true, "MamaWait fades out within its0.2-second release");
        yield return Wait(() => PlayingAt("FinalHeal_CalmLoop", .05d), "Bloom schedules CalmLoop at5.25s", 7f);
        bloom = Source("FinalHeal_Bloom"); loop = Source("FinalHeal_CalmLoop");
        double bloomOffset = bloom.timeSamples / (double)bloom.clip.frequency - loop.timeSamples / (double)loop.clip.frequency;
        Check(Math.Abs(bloomOffset - 5.25d) <= .03d, "late scheduled overlap aligns toBloom5.25s within30ms");
        observations.Add(new { route = "late", waitedSeconds = lateLastPress - motherWaitBegan,
            bloomOffset, sources = AudioSnapshot() });
        yield return Wait(() => P.EpiloguePhaseIndex > 0, "late route enters the same epilogue presentation", 3f);
        context = "menu music cleanup";
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "real session flow accepts return to menu");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameSessionFlow.Instance.IsBusy,
            "menu transition completes", 30f);
        yield return new WaitForSecondsRealtime(2.2f);
        Check(!AudioSources().Any(source => source.isPlaying), "all treatment sources stop after the menu fade");
        Check(maxError <= .02d, "each full-hit-route input lands within20ms of its authored music target");
    }

    static void StartInput(bool delayed)
    {
        lateRoute = delayed; nextPress = lateMotherPresses = 0;
        heldUntil = previousInputClock = currentInputClock = 0d;
        motherWaitBegan = lateFirstPress = lateLastPress = lateSecondReadyAt = -1f;
        var input = new List<Press>();
        foreach (var note in director.Content.Notes)
        {
            if (delayed && note.IsFinal) continue;
            input.Add(new Press { id = note.id, time = note.HitTime,
                holdEnd = note.BeatType == OSHeartHealBeatType.Hold ? note.EndTime : 0d });
            if (note.BeatType == OSHeartHealBeatType.Reinforced)
                input.Add(new Press { id = note.id + ":second", time = note.HitTime + .1875d });
        }
        presses = input.OrderBy(press => press.time).ToArray();
        director.DebugRealtimeInput = ReadInput;
        director.DebugNoteHit += OnHit;
    }
    static int ReadInput(double time)
    {
        previousInputClock = currentInputClock;
        currentInputClock = time;
        if (director.IsEnding) return 0;
        // Press on the first available frame inside the 20ms verification
        // window. Record the actual sample clock, never a target
        // timestamp. Production judgement retains its separate 90ms window.
        if (nextPress < presses.Length && time >= presses[nextPress].time - .02d)
        {
            heldUntil = Math.Max(heldUntil, presses[nextPress].holdEnd);
            nextPress++;
            return time <= heldUntil ? 3 : 1;
        }
        if (lateRoute && director.IsWaitingForMother)
        {
            if (motherWaitBegan < 0f) motherWaitBegan = Time.realtimeSinceStartup;
            float now = Time.realtimeSinceStartup;
            if (lateMotherPresses == 0 && now - motherWaitBegan >= 5f)
            { lateMotherPresses++; lateFirstPress = lateLastPress = now; return 1; }
            if (lateMotherPresses == 1 && time >= director.Content.Notes.First(note => note.IsLastMother).HitTime)
            {
                if (lateSecondReadyAt < 0f) lateSecondReadyAt = now;
                if (now - lateSecondReadyAt >= .35f)
                { lateMotherPresses++; lateLastPress = now; return 1; }
            }
        }
        return time <= heldUntil ? 2 : 0;
    }
    static void OnHit(string id, double actual, double target)
    {
        bool waiting = director.IsWaitingForMother;
        double error = actual - target;
        int frequency = director.DebugAudioSession.BattleSource.clip.frequency;
        // Reconstruct the integer source sample, but retain fractional target
        // samples (eighth/sixteenth notes can land between PCM samples). This
        // treats exactly 882/44100s as 20ms without expanding the acceptance.
        long actualSample = (long)Math.Round(actual * frequency);
        double targetSample = target * frequency;
        double measuredError = Math.Abs(actualSample - targetSample) / frequency;
        if (!waiting)
        {
            if (lateRoute) lateMaxError = Math.Max(lateMaxError, measuredError);
            else maxError = Math.Max(maxError, measuredError);
        }
        hits.Add(new { route = lateRoute ? "late" : "on-time", id, actual, target, error, waiting,
            frequency, actualSample, targetSample,
            previousInputClock, sampleAdvance = actual - previousInputClock,
            frameSeconds = Time.unscaledDeltaTime, dsp = AudioSettings.dspTime, frame = Time.frameCount });
    }
    static void FreezeEnding()
    {
        endingClock = Time.unscaledTime;
        P.SetPresentationClock(() => endingClock);
    }
    static void DetachInput()
    {
        if (director == null) return;
        director.DebugRealtimeInput = null; director.DebugNoteHit -= OnHit;
    }
    static void ValidateAudio()
    {
        context = "imported audio contract";
        var expected = new Dictionary<string, double> {
            { "FinalHeal_Battle", 115d }, { "FinalHeal_MamaWait", 3d }, { "FinalHeal_CalmLoop", 72d }
        };
        foreach (var item in expected)
        {
            var clip = Resources.Load<AudioClip>(AudioRoot + item.Key);
            Check(clip != null && Math.Abs(clip.samples / (double)clip.frequency - item.Value) < .01d,
                item.Key + " has its exact authored sample duration");
            Check(clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.preloadAudioData,
                item.Key + " is preloaded and decompressed for sample timing");
        }
        foreach (string name in new[] { "FinalHeal_Bloom", "FinalHeal_Slash", "FinalHeal_SlashZheng", "FinalHeal_SlashCrack" })
            Check(Resources.Load<AudioClip>(AudioRoot + name) != null, "required clip exists: " + name);
        foreach (string name in new[] { "FinalHeal_MamaWait", "FinalHeal_CalmLoop" })
        {
            var clip = Resources.Load<AudioClip>(AudioRoot + name);
            var head = new float[clip.channels];
            var tail = new float[clip.channels];
            Check(clip.GetData(head, 0) && clip.GetData(tail, clip.samples - 1), "loop PCM can be inspected: " + name);
            float jump = head.Zip(tail, (a, b) => Mathf.Abs(a - b)).Max();
            observations.Add(new { type = "loop PCM seam", name, channels = clip.channels,
                samples = clip.samples, frequency = clip.frequency, seamMaximumAmplitudeStep = jump });
            Check(jump < .01f, "loop boundary PCM step stays below0.01: " + name);
        }
    }
    static AudioSource[] AudioSources() => UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,
        FindObjectsSortMode.None).Where(source => source.clip != null && source.clip.name.StartsWith("FinalHeal_", StringComparison.Ordinal)).ToArray();
    static AudioSource Source(string name) => AudioSources().FirstOrDefault(source => source.clip.name == name);
    static bool PlayingAt(string name, double seconds)
    { var source = Source(name); return source != null && source.isPlaying && source.timeSamples / (double)source.clip.frequency >= seconds; }
    static object[] AudioSnapshot() => AudioSources().Select(source => (object)new { name = source.name,
        clip = source.clip.name, source.isPlaying, source.loop, source.volume, source.pitch, source.timeSamples,
        seconds = source.timeSamples / (double)source.clip.frequency }).ToArray();
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(predicate(), label);
    }
    static void Check(bool value, string label, bool record = true)
    { if (!value) throw new InvalidOperationException(label); if (record) checks.Add(context + ": " + label); }
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    static void OnError(string condition, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition + "\n" + stack); }
}
