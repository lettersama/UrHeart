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
/// Opt-in integration QA of the production final-treatment director. Uses the
/// authored score, actual warning/pointer/keyboard handlers, and real isolated
/// disk saves through ReturnToMainMenu/ContinueGame. The editor clock controls
/// only judgement probes; it never substitutes a QA chart for production data.
/// </summary>
public static class OSChapter04FinalHealBackendQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly List<object> observations = new List<object>();
    static GameDataManager data;
    static GameAutosaveService saves;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04StoryDirector story;
    static OSChapter04FinalHealDirector director;
    static Mouse mouse;
    static Keyboard keyboard;
    static bool cancelled, achievementHooks;
    static string context;
    static float entryHeart, endingClock;
    static int finalRequests;
    static OSChapter04FinalHealContent Score => director.Content;
    static OSChapter04FinalHealPresentation P => OSChapter04FinalHealPresentation.Current;
    static OSChapter04StoryState S => data.OSProcessData.memory.chapter04Story;
    static int Heart => data.GetOSResourceValue(ResourceType.Heart);
    static int Fault => data.GetOSResourceValue(ResourceType.Fault);
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    [MenuItem("Tools/MrTGame/Chapter 4 Final Heal/Run Backend QA")]
    static void StartMenu() => Debug.Log("[Final Heal Backend QA] " + Start());

    public static string Start() => Start(false);
    public static string StartAchievementHooks() => Start(true);

    static string Start(bool achievementsOnly)
    {
        if (Status == "running") return Status;
        saves = GameAutosaveService.Instance; data = GameDataManager.Instance;
        if (!Application.isPlaying || saves == null || data == null || GameManager.Instance == null
            || GameTest.Instance == null || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        setup = UnityEngine.Object.FindFirstObjectByType<GameSetup>();
        if (setup == null) return "Active GameSetup required";
        achievementHooks = achievementsOnly;
        checks.Clear(); errors.Clear(); observations.Clear(); cancelled = false; finalRequests = 0;
        os = null; story = null; director = null; context = "initialization";
        OutputDirectory = Path.GetFullPath(achievementHooks
            ? Path.Combine("Tools", "SteamAchievements", "Validation", "2026-11-03", "ending-" + Guid.NewGuid().ToString("N"))
            : Path.Combine("Temp", "OSChapter04FinalHealBackendQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
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
            "Assets/Data/HeartHealData.asset", "Assets/Data/LocalizationData.asset",
            "Assets/Resources/Chapter04/FinalHeal/chart.json" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles("Assets/Script/UI/Action/Chapter04FinalHeal", "*.cs")).ToArray();
        string[] originalHashes = null;
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        Application.logMessageReceived += CaptureError;
        try
        {
            saves.EnterTestSession(); originalHashes = protectedFiles.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); data.SetCurrentLanguage(LanguageState.zh_CN);
            Set(setup, "isDemo", false); Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            mouse = InputSystem.AddDevice<Mouse>("FinalHealBackendQAMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("FinalHealBackendQAKeyboard"); ReleaseInput();
            work.Push(achievementHooks ? RunAchievementHooks() : Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancelled) throw new OperationCanceledException("Final heal backend QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + failure;
                    Debug.LogError("[Final Heal Backend QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null)
            {
                Check(protectedFiles.Select(Hash).SequenceEqual(originalHashes),
                    "formal saves, scenes, frontend sources and authored data stay unchanged");
                Status = errors.Count == 0 ? "PASS" : "FAILED: " + errors.Count + " runtime errors";
            }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                ReleaseInput(); director?.Stop();
                if (P != null) { P.ClearAll(); P.SetPresentationClock(null); }
                if (story != null) { story.FinalTreatmentRequested -= Requested; story.DebugSetFocus(false); }
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourceEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || GameSetup.IsDemo != demo
                    || !protectedFiles.Select(Hash).SequenceEqual(originalHashes))
                    throw new InvalidOperationException("Original save object, settings or protected files changed");
                checks.Add("cleanup: original save, settings, audio, input devices and protected files restored");
            }
            catch (Exception exception) { Status = "FAILED during cleanup: " + exception + "\n" + Status; }
            Application.logMessageReceived -= CaptureError;
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS" && errors.Count != 0) Status = "FAILED: cleanup logged runtime errors";
            if (Status == "PASS") Status += ": " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, achievementHooks, checks, errors, observations, protectedFiles, originalHashes,
                finalHashes = protectedFiles.Select(Hash).ToArray(),
                note = achievementHooks
                    ? "Production treatment and ending achievement hooks. Virtual clocks drive actual judgement and animation; final receipt is flushed to isolated disk by the real return-to-menu path. No real Steam submission."
                    : "Production score and real backend. Fixed music-clock probes drive judgement; four saves use isolated disk and real scene reconstruction. Frontend drawing files are protected by hashes."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Final Heal Backend QA] " + Status + " | " + OutputDirectory);
        }
    }

    static void RestartBoundaryProbe(string label, float expectedEntryHeart)
    {
        context = "boundary regression / " + label;
        ReleaseInput();
        director.Stop();
        P?.SetPresentationClock(null);
        Check(director.Begin(story, true, false), "fresh production treatment starts");
        director.DebugSetFocus(true);
        director.DebugSetClock(0d);
        Step(0d);
        P.Render();
        Check(Mathf.Approximately(entryHeart, expectedEntryHeart)
            && S.finalHealInitialHeartCaptured
            && Mathf.Approximately(S.finalHealInitialHeart, expectedEntryHeart)
            && Mathf.Approximately(director.HeartValue, expectedEntryHeart)
            && Heart == Mathf.RoundToInt(expectedEntryHeart),
            "restart preserves the captured entrance heart instead of a transient section value");
        Check(director.IsRunning && !director.IsEnding && !director.IsWaitingForMother
            && director.MusicTime == 0d && director.HitCount == 0
            && director.MissCount == 0 && director.HoldTickCount == 0
            && P.LivePhraseCount == 0 && P.LaneNoteCount == 0 && !P.IsFrozen,
            "restart clears runtime judgement, words and lane without retaining prior boundaries");
    }

    static IEnumerator BoundaryRegression()
    {
        float expectedEntryHeart = entryHeart;

        // Covers duplicate authored phrase ids across the first and second
        // sections. Old first-section misses must not delete the new targets.
        RestartBoundaryProbe("single jump from zero to freeze", expectedEntryHeart);
        yield return null;
        Step(Score.FreezeTime + .01d);
        P.Render();
        Check(P.IsFrozen && P.FrozenPhraseCount == 10
            && Score.Notes.Where(note => note.IsFrozenTarget)
                .All(note => P.IsPhraseFrozen(note.VisualId)),
            "one jump across the entire struggle retains all ten frozen targets");
        Check(Score.Notes.Where(note => note.section == 1 && note.HasText)
                .All(note => !P.IsPhraseAlive(note.VisualId))
            && P.LaneNoteCount == 0,
            "old first-section words are resolved and the frozen lane is empty");

        // Covers the original ordering bug: FreezeAll must happen before C2
        // is spawned. Render first because it also prunes expired lane notes.
        RestartBoundaryProbe("single jump through freeze into the first cut", expectedEntryHeart);
        yield return null;
        var firstCut = Score.Notes.Where(note => note.IsAnswer)
            .OrderBy(note => note.HitTime).First();
        Step(firstCut.SpawnTime + .01d);
        P.Render();
        Check(!P.IsFrozen && P.FrozenPhraseCount == 10
            && Score.Notes.Where(note => note.IsFrozenTarget)
                .All(note => P.IsPhraseAlive(note.VisualId))
            && P.LaneNoteCount == 1,
            "release precedes the first cut and FreezeAll does not clear its new lane note");
        int hits = director.HitCount;
        int laneHits = P.LaneHitEffects;
        Step(firstCut.HitTime, true, false);
        P.Render();
        Check(director.HitCount == hits + 1 && P.LaneHitEffects == laneHits + 1
            && P.SlashCount > 0 && P.IsHitStopped && P.LaneNoteCount == 0,
            "the surviving first cut judges once and produces its actual lane hit and slash");

        // Covers an entire-score jump and the final indefinite wait. This
        // intentionally does not press D2, keeping the saved phase in treatment.
        RestartBoundaryProbe("single jump through all sections to the mother gate", expectedEntryHeart);
        yield return null;
        var mother = Score.Notes.Where(note => note.IsFinal).OrderBy(note => note.HitTime).First();
        Step(Score.FinalNoteTime + 30d);
        P.Render();
        Check(director.IsRunning && director.IsWaitingForMother && !director.IsEnding
            && Math.Abs(director.MusicTime - mother.HitTime) < .001d
            && !P.IsFrozen && P.IsPhraseAlive(mother.VisualId)
            && !P.IsPhraseFrozen(mother.VisualId) && P.LaneNoteCount == 2,
            "both closing taps survive the crossed freeze while the first waits on its judgement line");
        int misses = director.MissCount;
        Step(Score.FinalNoteTime + 60d);
        P.Render();
        Check(director.IsWaitingForMother && !director.IsEnding
            && director.MissCount == misses
            && Math.Abs(director.MusicTime - mother.HitTime) < .001d
            && P.IsPhraseAlive(mother.VisualId) && P.LaneNoteCount == 2,
            "another large step preserves both closing taps without repeated miss penalties");

        RestartBoundaryProbe("timely first mother and independent second window", expectedEntryHeart);
        yield return null;
        var mothers = Score.Notes.Where(note => note.IsFinal).OrderBy(note => note.HitTime).ToArray();
        Step(mothers[0].HitTime, true, false);
        Check(!director.IsWaitingForMother && !director.IsEnding && director.HitCount == 1,
            "first mother hit at105 resolves once without entering indefinite wait or curing");
        yield return null;
        Step(mothers[1].HitTime + director.HitWindow - .001d);
        Check(!director.IsWaitingForMother && !director.IsEnding,
            "second mother retains its full independent105.75 judgement window");
        Step(mothers[1].HitTime + director.HitWindow + .001d);
        Check(director.IsWaitingForMother && !director.IsEnding
            && Math.Abs(director.MusicTime - mothers[1].HitTime) < .001d,
            "only after the second window expires does its remaining mother wait on the line");

        RestartBoundaryProbe("restore original judgement starting conditions", expectedEntryHeart);
        yield return null;
    }

    static IEnumerator RunAchievementHooks()
    {
        yield return Entrance();
        context = "treatment achievement boundary";
        Check(StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.Chapter03Complete,
            "opening the final treatment does not grant either ending achievement");
        director.DebugSetClock(0d);
        var mothers = Score.Notes.Where(note => note.IsFinal).OrderBy(note => note.HitTime).ToArray();
        Step(Score.FinalNoteTime + 30d);
        Check(director.IsWaitingForMother && !director.IsEnding
            && StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.Chapter03Complete,
            "earlier sections and indefinite mother wait do not grant treatment completion");
        endingClock = Time.unscaledTime;
        P.SetPresentationClock(() => endingClock);
        Step(director.MusicTime, true, false);
        Check(!director.IsEnding && StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.Chapter03Complete,
            "the first mother tap does not grant treatment completion");
        yield return null;
        Step(mothers[1].HitTime + 5d);
        Step(director.MusicTime, true, false);
        Check(director.IsEnding && Heart == 100 && Fault == 0
            && StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.GameComplete,
            "only the successful final mother tap grants GameComplete before the epilogue");
        Step(director.MusicTime + Score.EndingDelay + .01d);
        yield return Wait(() => P != null && P.EpiloguePhaseIndex > 0, "production epilogue starts", 8f);
        yield return SaveAndContinue("achievement-ending", true);
        Check(StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.GameComplete
            && StoryAchievementProgress.GetReached(ReadSnapshot()) == (int)StoryAchievementMilestone.GameComplete
            && !P.IsPostcardCollected,
            "real Continue preserves treatment completion without inventing postcard collection");
        context = "postcard achievement boundary";
        var presentation = P;
        int collectedSignals = 0, finishedSignals = 0;
        Action collected = () => collectedSignals++;
        Action finished = () => finishedSignals++;
        presentation.PostcardCollected += collected;
        presentation.EndingFinished += finished;
        try
        {
            for (int guard = 0; guard < 1000 && !presentation.IsPostcardWaiting; guard++)
            {
                endingClock += .25f; presentation.Render();
                if (presentation.IsEpilogueAwaitingChoice) presentation.ChooseEpilogueOption(0);
                Check(StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.GameComplete,
                    "dialogue and postcard entrance do not grant MyDaughter");
                yield return null;
            }
            Check(presentation.IsPostcardWaiting && !presentation.IsPostcardCollected && collectedSignals == 0,
                "fully displayed postcard still awaits collection");
            yield return Shot("achievement-postcard-before-collection");
            presentation.PlayPress(); presentation.Render();
            Check(!presentation.IsPostcardWaiting && !presentation.IsPostcardCollected && collectedSignals == 0
                && StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.GameComplete,
                "clicking starts collection but does not grant before its animation finishes");
            endingClock += .4f; presentation.Render();
            Check(!presentation.IsPostcardCollected && collectedSignals == 0
                && StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.GameComplete,
                "the midpoint of postcard collection does not grant MyDaughter");
            endingClock += .5f; presentation.Render();
            Check(presentation.IsPostcardCollected && collectedSignals == 1
                && StoryAchievementProgress.GetReached(data.SaveData) == (int)StoryAchievementMilestone.MyDaughter,
                "the completed postcard animation grants MyDaughter exactly once");
            presentation.PlayPress(); presentation.Render();
            Check(collectedSignals == 1, "additional postcard presses cannot repeat collection");
            saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (GameManager.Instance.Phase != GamePhase.Boot && Time.realtimeSinceStartup < deadline)
            {
                endingClock += .5f; presentation.Render(); yield return null;
            }
            yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning,
                "Staff completion uses the real return-to-menu path", 15f);
            var ended = ReadSnapshot();
            Check(collectedSignals == 1 && finishedSignals == 1
                && StoryAchievementProgress.GetReached(ended) == (int)StoryAchievementMilestone.MyDaughter
                && ended.osProcess.memory.chapter04Story.finalTreatmentStage == OSChapter04FinalTreatmentStage.Completed,
                "Staff completes once and persists both ending achievements without granting the restart-only gift");
            observations.Add(new { type = "ending achievement receipts", collectedSignals, finishedSignals,
                milestone = ended.storyAchievementMilestone, version = ended.storyAchievementVersion });
        }
        finally
        {
            if (presentation != null) { presentation.PostcardCollected -= collected; presentation.EndingFinished -= finished; }
            saves.EnterTestSession();
        }
    }

    static IEnumerator Run()
    {
        yield return Entrance();
        ContentContract();
        yield return RealClockAndPause();
        yield return SaveAndContinue("mid-treatment", false);
        yield return BoundaryRegression();
        yield return Judgement();
        context = "second section";
        yield return AdvanceTo(Score.SinkStartTime - .01d);
        Check(director.IsRunning && !director.IsEnding && director.HeartValue >= 1f,
            "all remaining struggle misses keep at least one heart and never end in failure");
        yield return AdvanceTo(Score.FreezeTime + .01d);
        Check(director.HeartValue == 0f && Heart == 0 && P.IsFrozen && P.FrozenPhraseCount == 10,
            "sinking reaches zero and freezes exactly the nine returning phrases plus B1");
        Check(GameManager.Instance.Phase == GamePhase.OSMain && director.IsRunning,
            "authored zero heart does not enter GameOver or a retry loop");
        yield return Shot("02-freeze");
        yield return SaveAndContinue("frozen", false);
        context = "third section";
        yield return AdvanceTo(Score.ReleaseTime + .01d);
        Check(!P.IsFrozen && director.IsRunning, "release resumes the ten-cut section");
        var cuts = Score.Notes.Where(note => note.IsAnswer).OrderBy(note => note.HitTime).ToArray();
        float beforeMiss = director.HeartValue;
        yield return AdvanceTo(cuts[0].EndTime + .2d);
        Check(director.HeartValue > beforeMiss && director.MissCount > 0,
            "missing the first answer still grows the heart by progress");
        for (int i = 1; i <= 3; i++) yield return Hit(cuts[i]);
        Check(P.HeartStage >= 1 && director.HeartValue > beforeMiss,
            "one missed cut plus three successful cuts cross the first recovery threshold");
        yield return Shot("03-recovery");
        yield return SaveAndContinue("third-section", false);
        context = "full recovery and mother gate";
        yield return AdvanceTo(Score.ReleaseTime + .01d);
        cuts = Score.Notes.Where(note => note.IsAnswer).OrderBy(note => note.HitTime).ToArray();
        foreach (var cut in cuts) yield return Hit(cut);
        Check(P.HeartStage == 2 && director.IsRunning && !director.IsEnding,
            "ten successful cuts reach stable heart but reserve wholeness for the final word");
        var mother = Score.Notes.Where(note => note.IsFinal).OrderBy(note => note.HitTime).First();
        yield return AdvanceTo(mother.HitTime + 30d);
        int misses = director.MissCount;
        Check(director.IsWaitingForMother && director.IsRunning && !director.IsEnding,
            "the final mother beat waits thirty seconds without ending or repeating bars");
        Check(Math.Abs(director.MusicTime - mother.HitTime) < .001d,
            "the final note remains on its judgement line while the music clock is held");
        Step(director.MusicTime + 5d);
        Check(director.IsWaitingForMother && director.MissCount == misses,
            "waiting at the final word does not accumulate repeated miss penalties");
        endingClock = Time.unscaledTime;
        P.SetPresentationClock(() => endingClock);
        var lastMother = Score.Notes.First(note => note.IsLastMother);
        int secondHeadBefore = LaneHead(lastMother.id);
        var tapClip = data.AudioData.SFX.First(item => item.keyid == "BeatHit").audioclip;
        int tapSoundsBefore = AudioManager.Instance.SFXPool.Count(source => source != null && source.isPlaying && source.clip == tapClip);
        Step(director.MusicTime, true, true);
        bool firstMotherWaits = director.IsWaitingForMother && !director.IsEnding && Heart < 100;
        int hitsAfterFirst = director.HitCount;
        Check(AudioManager.Instance.SFXPool.Count(source => source != null && source.isPlaying && source.clip == tapClip) == tapSoundsBefore + 1,
            "first mother emits one small BeatHit even while the ordinary lane sounds are muted");
        Check(director.DebugAudioSession.Phase == OSChapter04FinalHealAudioSession.MusicPhase.Waiting
            && !director.DebugAudioSession.BloomSource.isPlaying && P.LaneNoteCount == 1,
            "first late mother leaves the remaining note and waiting music without starting the bloom");
        Step(director.MusicTime, true, true);
        Check(director.HitCount == hitsAfterFirst && !director.IsEnding,
            "an immediate repeat press cannot consume the second mother while it is still approaching");
        Step(director.MusicTime + .3d);
        int secondHeadMoving = LaneHead(lastMother.id);
        Check(director.MusicTime > mother.HitTime && director.MusicTime < lastMother.HitTime
            && secondHeadMoving < secondHeadBefore && !director.IsEnding,
            "after the first late tap the remaining mother visibly advances toward its own judgement line");
        double beforePause = director.MusicTime;
        director.DebugSetFocus(false);
        Step(beforePause + .2d);
        Check(Math.Abs(director.MusicTime - beforePause) < .001d,
            "losing focus pauses the second mother approach without consuming travel time");
        director.DebugSetFocus(true);
        yield return null;
        Step(director.MusicTime + 5d);
        firstMotherWaits &= director.IsWaitingForMother && !director.IsEnding;
        int secondHeadAtLine = LaneHead(lastMother.id);
        Check(Math.Abs(director.MusicTime - lastMother.HitTime) < .001d
            && secondHeadAtLine < secondHeadMoving && director.MissCount == misses,
            "second mother reaches105.75 and stops there without a miss or automatic ending");
        Step(director.MusicTime + 5d);
        Check(LaneHead(lastMother.id) == secondHeadAtLine && Math.Abs(director.MusicTime - lastMother.HitTime) < .001d,
            "second mother stays on its line indefinitely until another valid input");
        Step(director.MusicTime, true, true);
        Check(firstMotherWaits && Heart == 100 && Fault == 0 && P.HeartStage == 3,
            "mother waits after its first press and only the second makes the heart whole with HUD heart100/fault0");
        Step(director.MusicTime + Score.EndingDelay + .01d);
        yield return Wait(() => director.IsEnding && P.EpiloguePhaseIndex > 0, "healed state enters the production epilogue", 8f);
        var script = typeof(OSChapter04FinalHealPresentation).GetField("epScript", Members).GetValue(P)
            as IList<OSChapter04FinalHealPresentation.EpilogueStep>;
        Check(script != null && script.Count == 9
            && script.Where(step => step.IsChoice).Count() == 3
            && script.Where(step => step.IsChoice).All(step => step.options.Length == 1
                && (step.replies == null || step.replies.Length == 0))
            && script.Where(step => !step.IsChoice).All(step => Mathf.Approximately(step.holdSeconds, 1.2f)),
            "production epilogue has six default-held lines and three single-option choices without replies");
        string[] stepKeys = { "line_01", "choice_01", "line_02", "line_03", "choice_02",
            "line_04", "line_05", "choice_03", "line_06" };
        Check(script.Select(step => step.IsChoice ? step.options[0] : step.text)
            .SequenceEqual(stepKeys.Select(key => data.GetLocalizedText("chapter04.final_heal.epilogue." + key))),
            "all nine production steps use the authored localized order");
        yield return AdvanceToEpilogueSavePoint();
        yield return Shot("04-epilogue-mid-dialogue");
        yield return SaveAndContinue("ending", true);
        context = "ending completion";
        endingClock = Time.unscaledTime;
        P.SetPresentationClock(() => endingClock);
        int endingSignals = 0;
        int postcardClicks = 0;
        var visitedSteps = new HashSet<int>();
        var chosenSteps = new List<int>();
        Action finished = () => endingSignals++;
        Action<int, int> chosen = (step, option) => chosenSteps.Add(option == 0 ? step : -1);
        P.EndingFinished += finished;
        P.EpilogueChoice += chosen;
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        float deadline = Time.realtimeSinceStartup + 40f;
        while (GameManager.Instance.Phase != GamePhase.Boot && Time.realtimeSinceStartup < deadline)
        {
            endingClock += .5f;
            P?.Render();
            if (P != null && P.EpiloguePhaseIndex == 3) visitedSteps.Add(P.EpilogueStepIndex);
            if (P != null && P.IsEpilogueAwaitingChoice) P.ChooseEpilogueOption(0);
            if (P != null && P.IsPostcardWaiting) { postcardClicks++; P.PlayPress(); }
            yield return null;
        }
        if (P != null) { P.EndingFinished -= finished; P.EpilogueChoice -= chosen; }
        Check(visitedSteps.SetEquals(Enumerable.Range(0, 9))
            && chosenSteps.SequenceEqual(new[] { 1, 4, 7 }) && postcardClicks == 1,
            "restored epilogue runs all nine steps, three choices and one postcard click before credits");
        Check(endingSignals == 1, "actual frontend EndingFinished is raised once after credits");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning,
            "actual ending callback returns to the main menu", 15f);
        var ended = ReadSnapshot();
        Check(ended.osResources.heart == 100 && ended.osResources.error == 0
            && ended.osProcess.memory.chapter04Story.finalTreatmentStage.ToString() == "Completed",
            "the healed values and completed treatment are durable before the menu appears");
        observations.Add(new { type = "finished", stage = ended.osProcess.memory.chapter04Story.finalTreatmentStage.ToString(),
            heart = ended.osResources.heart, fault = ended.osResources.error, endingSignals,
            visitedSteps = visitedSteps.OrderBy(value => value).ToArray(), chosenSteps, postcardClicks });
        saves.EnterTestSession();
    }

    static IEnumerator Entrance()
    {
        context = "grave16 warning entry";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04FinalHeartHeal),
            "console checkpoint20 accepts the final-treatment route");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning, "checkpoint reconstructs the fourth-chapter OS", 80f);
        Resolve(); story.FinalTreatmentRequested += Requested;
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        yield return Wait(() => story.IsAvailable && S.currentEntryId == "chapter04_story_grave_16",
            "checkpoint starts on the authored grave16 line", 15f);
        Check(S.currentTextCompleted && S.currentResourcesCommitted && S.completed && finalRequests == 0
            && story.IsWaitingForFailureWarning,
            "checkpoint preserves the completed final line and unconfirmed warning-delay state");
        float alreadyRead = S.currentReadElapsed;
        float began = Time.realtimeSinceStartup;
        Check(OSChapter04GriefPresentation.Current != null && !OSChapter04GriefPresentation.Current.IsFailureWarningVisible,
            "the warning stays hidden at the start of the two-second reading delay");
        yield return Wait(() => story.IsFailureWarningPending
            && OSChapter04GriefPresentation.Current.IsFailureWarningVisible, "two-second delay opens the real warning", 8f);
        Check(Time.realtimeSinceStartup - began + alreadyRead >= 1.8f && finalRequests == 0,
            "warning does not auto-confirm or bypass the authored reading hold");
        var warning = OSChapter04GriefPresentation.Current;
        if (achievementHooks)
        {
            // Seed only the isolated checkpoint before the actual confirmation,
            // so opening treatment is also checked for premature achievement.
            data.SaveData.storyAchievementVersion = 2;
            data.SaveData.storyAchievementMilestone = (int)StoryAchievementMilestone.Chapter03Complete;
        }
        yield return ClickButton(warning.WarningButton);
        yield return Wait(() => story.FinalHeal != null && story.FinalHeal.IsRunning, "warning click starts the production backend", 12f);
        director = story.FinalHeal; director.DebugSetFocus(true);
        entryHeart = director.HeartValue;
        Check(finalRequests == 1 && P != null && P.IsVisible && !warning.IsFailureWarningVisible && warning.Tier == 0,
            "one real warning click clears grief before showing final treatment");
        Check(!os.HeartHealToolWindowController.GetComponent<OSWindowUnit>().IsOpen,
            "the old fourth treatment tool remains closed");
        Check(Mathf.Approximately(entryHeart, Heart) && entryHeart > 0 && entryHeart < 40,
            "entry heart starts from the actual grave HUD value");
        yield return Shot("01-entry");
    }

    static void ContentContract()
    {
        context = "authored score contract";
        Check(Score.Available && Score.Validate().Count == 0, "production content validates without QA fixtures");
        Check(Score.Notes.Count == 117 && Score.Bpm == 80f, "the music-aligned score contains117 notes at80 BPM");
        Check(Score.Notes.Count(note => note.section == 1) == 57
            && Score.Notes.Count(note => note.section == 2) == 48
            && Score.Notes.Count(note => note.section == 3) == 10
            && Score.Notes.Count(note => note.section == 4) == 2,
            "formal music sections contain57/48/10/2 notes");
        var mothers = Score.Notes.Where(note => note.IsFinal).OrderBy(note => note.HitTime).ToArray();
        Check(Math.Abs(mothers[0].HitTime - 105d) < .001d
            && Math.Abs(mothers[1].HitTime - 105.75d) < .001d
            && mothers.All(note => note.BeatType == OSHeartHealBeatType.Tap),
            "mother uses two independent tap windows at105 and105.75 seconds");
        Check(Score.Notes.Count(note => note.section == 1 && note.HasText) == 9
            && Score.Notes.Count(note => note.IsFrozenTarget) == 10, "struggle has9 phrases and sinking retains10 targets");
        Check(Score.Notes.Count(note => note.IsAnswer) == 10
            && Score.Notes.Where(note => note.section == 3).All(note => note.IsAnswer
                && note.BeatType == OSHeartHealBeatType.Tap && note.HoldDuration == 0f),
            "section3 contains exactly ten single-tap answer cuts and zero ordinary beats");
        Check(Score.Notes.Where(note => note.IsAnswer).All(note => Mathf.Abs(note.ApproachDuration - .6f) < .001f)
            && !Score.Notes.Any(note => note.phraseId == "B2" || note.pairId == "B2"),
            "cuts use the approved0.6-second approach and deleted B2 never appears");
        var languages = Enum.GetValues(typeof(LanguageState)).Cast<LanguageState>().ToArray();
        var epilogueKeys = Enumerable.Range(1, 6).Select(index => "chapter04.final_heal.epilogue.line_" + index.ToString("D2"))
            .Concat(Enumerable.Range(1, 3).Select(index => "chapter04.final_heal.epilogue.choice_" + index.ToString("D2")))
            .Concat(new[] { OSChapter04FinalHealPresentation.PostcardHintKey }).ToArray();
        Check(languages.Length == 10 && data.LocalizationData != null && epilogueKeys.All(key => {
            var entry = data.LocalizationData.Entries.SingleOrDefault(value => value.Key == key);
            return entry != null && entry.Values.Count == 10 && languages.All(language =>
                entry.Values.Count(value => value.Language == language && !string.IsNullOrWhiteSpace(value.Text)) == 1);
        }), "all nine epilogue steps and the postcard hint have ten nonempty language slots");
    }

    static IEnumerator RealClockAndPause()
    {
        context = "music clock and pause";
        double before = director.MusicTime;
        yield return new WaitForSecondsRealtime(.2f);
        Check(director.MusicTime > before, "unmodified production audio clock advances in real time");
        var music = typeof(OSChapter04FinalHealDirector).GetField("musicSource", Members)?.GetValue(director) as AudioSource;
        Check(music != null && music.clip != null && music.isPlaying
            && Math.Abs(director.MusicTime - music.timeSamples / (double)music.clip.frequency) < .12d,
            "production judgement clock agrees with the playing track's sample position");
        var console = GameTest.Instance.GetComponent<GameTestConsole>();
        console.SetOpen(true); yield return null; yield return null;
        before = director.MusicTime;
        yield return new WaitForSecondsRealtime(.15f);
        Check(director.IsPaused && P.IsPaused && Math.Abs(before - director.MusicTime) < .02d,
            "console pauses both treatment audio clock and frontend animation");
        console.SetOpen(false); yield return null; yield return null;
        Check(!director.IsPaused && !P.IsPaused, "closing the console resumes both clocks");
        yield return SettingsMenu();
        // Exercise physical-key polling on the normal audio clock before the
        // deterministic judgement probes disable Update's live input polling.
        var spaceTap = Score.Notes.Where(note => note.section == 1 && note.BeatType == OSHeartHealBeatType.Tap
            && note.HitTime > director.MusicTime + .5d).OrderBy(note => note.HitTime).First();
        yield return Wait(() => director.MusicTime >= spaceTap.HitTime - data.HeartHealData.HitWindowSeconds * .5d,
            "real music reaches the keyboard probe", 20f);
        int hits = director.HitCount;
        int presses = director.PressCount;
        var pollInput = typeof(OSChapter04FinalHealDirector).GetMethod("Update", Members);
        Check(pollInput != null && keyboard != null && keyboard.added,
            "the real polling Update and isolated keyboard exist");
        keyboard.MakeCurrent();
        // Match the existing Mirror/Entry background-keyboard fixture. Editor
        // focus may filter queued device events, so write the isolated device
        // state and call the real polling Update in that same input update.
        // This keeps its actual Space branch and sample-based music clock.
        InputState.Change(keyboard, new KeyboardState(Key.Space), InputState.currentUpdateType);
        try
        {
            observations.Add(new { type = "native space probe", frame = Time.frameCount,
                keyboardEnabled = keyboard.enabled, currentKeyboard = Keyboard.current == keyboard,
                pressed = keyboard.spaceKey.isPressed, pressedThisFrame = keyboard.spaceKey.wasPressedThisFrame,
                focused = Application.isFocused, updateType = InputState.currentUpdateType.ToString(),
                musicBefore = director.MusicTime, target = spaceTap.HitTime, hitsBefore = hits, pressesBefore = presses,
                samplePosition = music.timeSamples / (double)music.clip.frequency,
                note = "InputState.Change followed immediately by real OSChapter04FinalHealDirector.Update polling" });
            Check(Keyboard.current == keyboard && keyboard.spaceKey.wasPressedThisFrame,
                "the isolated Space edge is visible to the real keyboard polling branch");
            pollInput.Invoke(director, null);
            Check(director.HitCount == hits + 1 && director.PressCount == presses + 1,
                "actual space input resolves exactly one tap on the playing music clock");
            pollInput.Invoke(director, null);
            Check(director.HitCount == hits + 1 && director.PressCount == presses + 1,
                "a second real polling call in the same frame cannot count the same Space press twice");
        }
        finally { InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType); }
        ReleaseInput(); yield return null;
    }

    static IEnumerator SettingsMenu()
    {
        var window = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        var launcher = UnityEngine.Object.FindObjectsByType<OSOptionLauncherButton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.isActiveAndEnabled
                && item.GetType().GetField("optionWindow", Members).GetValue(item) as OSWindowUnit == window);
        Check(window != null && launcher != null, "the production settings window and launcher are available");
        var priorCanvas = window.GetComponent<Canvas>();
        bool priorOverride = priorCanvas != null && priorCanvas.overrideSorting;
        bool priorEnabled = priorCanvas != null && priorCanvas.enabled;
        int priorOrder = priorCanvas != null ? priorCanvas.sortingOrder : 0;
        Check(launcher.ToggleOptionWindow(), "the normal settings toggle opens the pause menu");
        yield return Wait(() => window.IsOpen && !window.IsMinimized && !window.IsOpening
            && director.IsPaused && P.IsPaused, "settings opens while both treatment clocks are paused", 8f);
        yield return null;
        var raised = window.GetComponent<Canvas>();
        Check(raised != null && raised.enabled && raised.overrideSorting
            && raised.sortingOrder > OSChapter04FinalHealPresentation.SortingOrder,
            "the actual settings canvas sorts above the full-screen treatment layer");
        double pausedAt = director.MusicTime;
        yield return new WaitForSecondsRealtime(.15f);
        Check(Math.Abs(director.MusicTime - pausedAt) < .02d,
            "music sample time remains paused while settings is open");
        var controller = window.GetComponent<OSOptionWindowController>();
        Check(controller != null, "the real settings page controller is present");
        int originalPage = (int)controller.GetType().GetField("currentPageIndex", Members).GetValue(controller);
        int targetPage = originalPage == 0 ? 1 : 0;
        var action = targetPage == 0 ? OSOptionImageButton.ButtonAction.AudioPage : OSOptionImageButton.ButtonAction.ScreenPage;
        var button = window.GetComponentsInChildren<OSOptionImageButton>(true).FirstOrDefault(item => item.isActiveAndEnabled
            && (OSOptionImageButton.ButtonAction)item.GetType().GetField("action", Members).GetValue(item) == action);
        Check(button != null, "a real settings page button is available for the pointer probe");
        float deadline = Time.realtimeSinceStartup + 2f;
        while (!TryPointer(button.gameObject, out _, false, true) && Time.realtimeSinceStartup < deadline)
            yield return null;
        var pointer = Pointer(button.gameObject, true);
        Check(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler)
            && (int)controller.GetType().GetField("currentPageIndex", Members).GetValue(controller) == targetPage
            && director.IsPaused && P.IsPaused,
            "a topmost real pointer click changes the settings page without resuming treatment");
        yield return null;
        Check(launcher.ToggleOptionWindow(), "the normal settings toggle dismisses the pause menu");
        yield return Wait(() => (!window.IsOpen || window.IsMinimized) && !director.IsPaused && !P.IsPaused,
            "dismissing settings resumes the playable treatment", 8f);
        yield return null; yield return null;
        var restoredCanvas = window.GetComponent<Canvas>();
        Check(priorCanvas == null ? restoredCanvas == null
            : restoredCanvas == priorCanvas && restoredCanvas.overrideSorting == priorOverride
                && restoredCanvas.sortingOrder == priorOrder && restoredCanvas.enabled == priorEnabled,
            "dismissing settings restores its original canvas sorting and component ownership");
        double resumedAt = director.MusicTime;
        yield return new WaitForSecondsRealtime(.15f);
        Check(director.MusicTime > resumedAt, "music samples resume after the pause menu is dismissed");
    }

    static IEnumerator Judgement()
    {
        context = "production input judgement";
        float window = data.HeartHealData.HitWindowSeconds;
        Check(Mathf.Abs(window - .09f) < .001f && Mathf.Approximately(director.HitWindow, window),
            "new backend reads the old treatment's0.09-second window");
        double clockBeforeFocusLoss = director.MusicTime;
        int pressesBeforeFocusLoss = director.PressCount;
        director.DebugSetFocus(false);
        Step(clockBeforeFocusLoss + 1d, true, true);
        Check(director.IsPaused && P.IsPaused && director.MusicTime == clockBeforeFocusLoss
            && director.PressCount == pressesBeforeFocusLoss, "focus loss pauses clocks and rejects gameplay input");
        director.DebugSetFocus(true); Step(clockBeforeFocusLoss);
        var taps = Score.Notes.Where(note => note.section == 1 && note.BeatType == OSHeartHealBeatType.Tap)
            .OrderBy(note => note.HitTime).ToArray();
        yield return AdvanceTo(taps[0].HitTime - window - .01d);
        int hits = director.HitCount;
        int presses = P.PressCount;
        Step(director.MusicTime, true, false);
        Check(director.HitCount == hits && P.PressCount == presses + 1,
            "a press just before the early window is empty but still plays the frontend press feedback");
        Step(taps[0].HitTime - window + .001d, true, false);
        Check(director.HitCount == hits + 1, "a press inside the early window is accepted");
        yield return AdvanceTo(taps[1].HitTime + window - .001d);
        Step(director.MusicTime, true, false);
        Check(director.HitCount == hits + 2, "a press inside the late window is accepted");
        yield return AdvanceTo(taps[2].HitTime + window + .01d);
        int missed = director.MissCount;
        Step(director.MusicTime, true, false);
        Check(director.HitCount == hits + 2 && missed > 0, "a late out-of-window press cannot recover a missed tap");
        yield return AdvanceTo(taps[3].HitTime);
        hits = director.HitCount;
        PointerDown(P.LayerRect.gameObject);
        yield return null;
        Check(director.HitCount == hits + 1, "the actual full-screen pointer handler resolves one tap");
        var hold = Score.Notes.Where(note => note.section == 1 && note.BeatType == OSHeartHealBeatType.Hold
            && note.HitTime > director.MusicTime)
            .OrderBy(note => note.HitTime).First();
        yield return AdvanceTo(hold.HitTime);
        int ticks = director.HoldTickCount;
        Step(hold.HitTime, true, true);
        Check(director.HoldTickCount == ticks + 1, "starting a real hold awards its first chart tick immediately");
        yield return AdvanceTo(hold.HitTime + .31d, true);
        Check(director.HoldTickCount >= ticks + 4, "sustained hold uses the legacy0.1-second tick cadence");
        ticks = director.HoldTickCount; missed = director.MissCount;
        Step(hold.HitTime + .35d, false, false);
        Check(director.MissCount == missed + 1, "releasing a hold early records its failed tail once");
        yield return AdvanceTo(hold.HitTime + .8d);
        Check(director.HoldTickCount == ticks && director.MissCount == missed + 1,
            "released hold neither awards more ticks nor repeats its penalty");
        var heavy = Score.Notes.First(note => note.section == 1 && note.BeatType == OSHeartHealBeatType.Reinforced
            && note.HitTime > director.MusicTime);
        yield return AdvanceTo(heavy.HitTime);
        hits = director.HitCount;
        float heartBeforeShell = director.HeartValue;
        int laneHitsBeforeShell = P.LaneHitEffects;
        Step(heavy.HitTime, true, true);
        bool shellOnly = director.HitCount == hits && director.HeartValue == heartBeforeShell
            && P.LaneHitEffects == laneHitsBeforeShell;
        yield return null;
        Step(heavy.HitTime + .2d, true, true);
        Check(shellOnly && director.HitCount == hits + 1 && P.LaneHitEffects == laneHitsBeforeShell + 1,
            "heavy first press only cracks the shell; a second outside the first-hit window resolves once");
        Step(heavy.HitTime + .21d, false, false);
    }

    static IEnumerator AdvanceToEpilogueSavePoint()
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (Time.realtimeSinceStartup < deadline)
        {
            endingClock += .5f;
            P.Render();
            if (P.IsEpilogueAwaitingChoice)
            {
                if (P.EpilogueStepIndex == 4) break;
                Check(P.EpilogueStepIndex == 1 && P.ChooseEpilogueOption(0),
                    "the first production reply advances to the middle of the epilogue");
            }
            yield return null;
        }
        Check(P.IsEpilogueAwaitingChoice && P.EpilogueStepIndex == 4 && P.EpilogueChoicesMade == 1,
            "the ending disk save interrupts the second choice rather than the beginning of the dialogue");
    }

    static IEnumerator SaveAndContinue(string label, bool ending)
    {
        context = "disk save / " + label;
        ReleaseInput();
        var old = director;
        double savedTime = director.MusicTime;
        var history = SnapshotHistory(S.history);
        int heartWeight = S.heartWeightApplied, faultWeight = S.faultWeightApplied;
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        Check(saves.FlushNow(), "actual isolated disk flush succeeds");
        saves.EndSession();
        var snapshot = ReadSnapshot();
        File.WriteAllText(Path.Combine(OutputDirectory, label + "-logical-save.json"), JsonUtility.ToJson(snapshot, true));
        Check(snapshot.osProcess.memory.chapter04Story.finalTreatmentStage == S.finalTreatmentStage,
            "disk retains the actual treatment-versus-ending stage");
        Check(GameSessionFlow.Instance.ReturnToMainMenu(), "real session flow accepts return to menu");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !GameSessionFlow.Instance.IsBusy, "menu transition finishes", 60f);
        Check(GameSessionFlow.Instance.ContinueGame(), "Continue accepts the saved isolated file");
        yield return Wait(() => !GameSessionFlow.Instance.IsBusy && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null && saves.SessionActive,
            "Continue rebuilds the actual OS scene", 90f);
        Resolve();
        yield return Wait(() => story.FinalHeal != null && (ending ? story.FinalHeal.IsEnding : story.FinalHeal.IsRunning),
            "the new director restores the saved route", 15f);
        director = story.FinalHeal; director.DebugSetFocus(true);
        if (ending)
        {
            endingClock = Time.unscaledTime;
            P.SetPresentationClock(() => endingClock);
        }
        Check(old == null && director != null, "Continue destroys the old director and creates a new owner");
        Check(director.RestoredOnBegin, "disk reload selects the frontend's immediate Begin(true) restoration");
        var restoredHistory = SnapshotHistory(S.history);
        bool historyMatches = SameHistory(history, restoredHistory);
        observations.Add(new { type = "disk restore ledger", label, historyMatches,
            historyBefore = history, historyOnDisk = snapshot.osProcess.memory.chapter04Story.history,
            historyAfter = restoredHistory, heartWeightBefore = heartWeight, heartWeightAfter = S.heartWeightApplied,
            faultWeightBefore = faultWeight, faultWeightAfter = S.faultWeightApplied });
        Check(historyMatches && S.heartWeightApplied == heartWeight
            && S.faultWeightApplied == faultWeight, "restore does not replay grave history or resource receipts");
        // Keep the restored production clock live until its scheduled lead has
        // elapsed. Guarded/Focus keeps the rebuilt owner focused in background.
        var restoredAudio = director.DebugAudioSession;
        Check(restoredAudio != null, "disk restore creates a fresh audio session");
        var restoredSource = ending ? restoredAudio.LoopSource : restoredAudio.BattleSource;
        yield return Wait(() => !director.IsPaused && !restoredAudio.IsPaused
            && restoredSource != null && restoredSource.clip != null
            && restoredSource.isPlaying && restoredSource.timeSamples > 0,
            "restored audio passes its scheduled lead and advances real samples", 2f);
        double restoredAudioTime = restoredSource.timeSamples / (double)restoredSource.clip.frequency;
        Check(restoredAudioTime < 2d
            && restoredSource.clip == Resources.Load<AudioClip>(ending ? Score.CalmLoopResource : Score.MusicResource)
            && restoredAudio.Phase == (ending ? OSChapter04FinalHealAudioSession.MusicPhase.CalmLoop
                : OSChapter04FinalHealAudioSession.MusicPhase.Battle),
            ending ? "ending reload starts CalmLoop near sample zero" : "treatment reload starts Battle near sample zero");
        if (ending) Check(!restoredAudio.BattleSource.isPlaying && restoredAudio.BattleSource.timeSamples == 0,
            "ending reload does not start Battle");
        observations.Add(new { type = "disk restore audio", label, ending,
            phase = restoredAudio.Phase.ToString(), clip = restoredSource.clip.name,
            sample = restoredSource.timeSamples, frequency = restoredSource.clip.frequency,
            seconds = restoredAudioTime, musicClock = director.MusicTime,
            battleClip = restoredAudio.BattleSource.clip.name, battleSample = restoredAudio.BattleSource.timeSamples,
            battlePlaying = restoredAudio.BattleSource.isPlaying,
            loopClip = restoredAudio.LoopSource.clip.name, loopSample = restoredAudio.LoopSource.timeSamples,
            loopPlaying = restoredAudio.LoopSource.isPlaying });
        if (ending)
        {
            Check(director.IsEnding && !director.IsWaitingForMother && Heart == 100 && Fault == 0 && P.HeartStage == 3,
                "ending save lands directly in healed ending without another rhythm round");
            Check(P.EpiloguePhaseIndex == 3 && P.EpilogueStepIndex == 0 && P.EpilogueChoicesMade == 0
                && P.EpilogueSinkProgress == 1f
                && P.EpilogueText == data.GetLocalizedText("chapter04.final_heal.epilogue.line_01"),
                "mid-dialogue disk reload opens Live on full black and restarts the first line");
        }
        else
        {
            Check(!director.IsEnding && !P.IsFrozen && director.MusicTime < 2d && director.HitCount == 0
                && director.MissCount == 0 && director.HoldTickCount == 0,
                "mid-treatment save restarts the short treatment at its beginning with fresh judgement");
            Check(Mathf.Approximately(director.HeartValue, entryHeart) && Heart == Mathf.RoundToInt(entryHeart),
                "restart uses the original entry heart rather than the saved transient heart");
            director.DebugSetClock(0d);
        }
        Check(!os.HeartHealToolWindowController.GetComponent<OSWindowUnit>().IsOpen,
            "restore never opens the legacy fourth treatment window");
        saves.EnterTestSession();
        observations.Add(new { type = "disk restore", label, savedTime, restoredTime = director.MusicTime,
            stage = S.finalTreatmentStage.ToString(), heart = Heart, fault = Fault, historyCount = S.history.Count });
        yield return Shot(label + "-restored");
    }

    static OSChapter04StoryRecord[] SnapshotHistory(IReadOnlyList<OSChapter04StoryRecord> history)
        => history.Select(record => record == null ? null : new OSChapter04StoryRecord {
            visit = record.visit, entryId = record.entryId, optionId = record.optionId
        }).ToArray();

    static bool SameHistory(IReadOnlyList<OSChapter04StoryRecord> before, IReadOnlyList<OSChapter04StoryRecord> after)
    {
        if (before.Count != after.Count) return false;
        for (int i = 0; i < before.Count; i++)
        {
            var a = before[i]; var b = after[i];
            if (a == null || b == null) { if (a != b) return false; else continue; }
            // Unity JsonUtility round-trips null string fields as empty strings.
            if (a.visit != b.visit || (a.entryId ?? string.Empty) != (b.entryId ?? string.Empty)
                || (a.optionId ?? string.Empty) != (b.optionId ?? string.Empty)) return false;
        }
        return true;
    }

    static IEnumerator Hit(OSChapter04FinalHealContent.Note note)
    {
        yield return AdvanceTo(note.HitTime);
        int hits = director.HitCount;
        Step(note.HitTime, true, note.HoldDuration > 0f);
        if (note.HoldDuration > 0f) yield return AdvanceTo(note.EndTime + .001d, true);
        Check(director.HitCount == hits + 1, "authored answer resolves once: " + note.phraseId);
        Step(director.MusicTime, false, false);
    }

    static IEnumerator AdvanceTo(double target, bool held = false)
    {
        int steps = 0;
        double probe = director.MusicTime;
        while (probe + .00001d < target)
        {
            probe = Math.Min(target, probe + .05d);
            Step(probe, false, held);
            if (++steps % 8 == 0) yield return null;
        }
    }
    static void Step(double time, bool pressed = false, bool held = false) => director.DebugStep(time, pressed, held);
    static int LaneHead(string id)
    {
        var type = typeof(OSChapter04FinalHealPresentation);
        var note = type.GetMethod("FindNote", Members).Invoke(P, new object[] { id });
        return (int)type.GetMethod("NoteHead", Members).Invoke(P, new object[] { note, director.MusicTime });
    }
    static void Resolve()
    {
        os = OS_Handle.Instance; story = os == null ? null : os.Chapter04Story;
        director = story?.FinalHeal; Focus();
    }
    static void Focus()
    {
        if (mouse != null && mouse.added) mouse.MakeCurrent();
        if (keyboard != null && keyboard.added) keyboard.MakeCurrent();
        var current = OS_Handle.Instance?.Chapter04Story;
        current?.DebugSetFocus(true); current?.FinalHeal?.DebugSetFocus(true);
    }
    static void Requested() => finalRequests++;
    static void ReleaseInput()
    {
        if (mouse != null && mouse.added) InputSystem.QueueStateEvent(mouse, new MouseState());
        if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        InputSystem.Update();
    }
    static IEnumerator ClickButton(Button button)
    {
        Check(button != null && button.interactable, "the actual warning button is interactable");
        // ShowFailureWarning builds its Canvas/Graphics on the frame it first
        // becomes visible. Wait for normal graphic registration and depth
        // calculation before asking whether a real player raycast can hit it.
        yield return null;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (!TryPointer(button.gameObject, out _, false) && Time.realtimeSinceStartup < deadline)
            yield return null;
        var pointer = Pointer(button.gameObject);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        yield return null;
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }
    static void PointerDown(GameObject target) => ExecuteEvents.Execute(target, Pointer(target), ExecuteEvents.pointerDownHandler);
    static PointerEventData Pointer(GameObject target, bool clickHandler = false)
    {
        bool reachesTarget = TryPointer(target, out var pointer, true, clickHandler);
        Check(reachesTarget, "pointer raycast reaches the intended topmost target: " + target.name);
        return pointer;
    }
    static bool TryPointer(GameObject target, out PointerEventData pointer, bool record, bool clickHandler = false)
    {
        Canvas.ForceUpdateCanvases();
        var rect = target.GetComponent<RectTransform>(); var canvas = target.GetComponentInParent<Canvas>();
        // The warning/treatment creates a nested sorting Canvas. Its camera
        // can be null even when its root is ScreenSpaceCamera; use the same
        // root-canvas camera as the production deployment pointer checks.
        var root = canvas.rootCanvas;
        var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        var position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = position };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var receiver = hits.Count == 0 ? null : clickHandler
            ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)
            : ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject);
        if (record) observations.Add(new { type = "pointer raycast", expected = target.name, actual = receiver?.name,
            eventType = clickHandler ? "click" : "down",
            point = new[] { position.x, position.y }, screen = new[] { Screen.width, Screen.height },
            canvas = canvas.name, root = root.name, rootMode = root.renderMode.ToString(), camera = camera?.name,
            canvases = target.GetComponentsInParent<Canvas>(true).Select(item => new { item.name, item.enabled,
                active = item.gameObject.activeInHierarchy, mode = item.renderMode.ToString(), camera = item.worldCamera?.name,
                item.overrideSorting, item.sortingOrder }).ToArray(),
            graphics = target.GetComponentsInParent<Graphic>(true).Concat(target.GetComponentsInChildren<Graphic>(true))
                .Distinct().Select(item => new { item.name, item.enabled, active = item.gameObject.activeInHierarchy,
                    item.depth, item.raycastTarget, culled = item.canvasRenderer.cull,
                    canvas = item.canvas?.name }).ToArray(),
            groups = target.GetComponentsInParent<CanvasGroup>(true).Select(item => new { item.name, item.enabled,
                active = item.gameObject.activeInHierarchy, item.alpha, item.blocksRaycasts,
                item.interactable, item.ignoreParentGroups }).ToArray(),
            hits = hits.Take(8).Select(hit => new { name = hit.gameObject.name,
                receiver = clickHandler ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject)?.name
                    : ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject)?.name,
                hit.sortingOrder, hit.depth }).ToArray() });
        return receiver == target;
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 15f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(predicate(), label);
    }
    static IEnumerator Shot(string label)
    {
        P?.Render(); Canvas.ForceUpdateCanvases();
        string path = Path.Combine(OutputDirectory, label + ".png");
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 24, "screenshot written: " + label, 10f);
    }
    static GameSaveData ReadSnapshot()
    {
        var read = new GameAutosaveStorage(OutputDirectory).Read();
        if (!read.Success) throw new IOException("Read isolated final-treatment save: " + read.Error);
        return JsonUtility.FromJson<GameSaveData>(read.Payload);
    }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    static string Hash(string path)
    {
        if (!File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static string Describe() => "phase=" + GameManager.Instance?.Phase + ", stage=" + S?.finalTreatmentStage
        + ", music=" + director?.MusicTime + ", heart=" + Heart + ", fault=" + Fault
        + ", running=" + director?.IsRunning + ", ending=" + director?.IsEnding;
    static void CaptureError(string condition, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition + "\n" + trace);
    }
}
