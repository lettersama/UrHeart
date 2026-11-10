using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using UnityEngine;

// Round 11 Play Mode check of the final heart treatment screen on the real
// chapter-4 OS (console checkpoint 19, isolated save storage). The test chart
// (an 80 BPM metronome laid out from Docs/Chapter04FinalHeal_WordFlow_v1.md)
// and its words live only here. The presentation is driven like the backend
// would drive it; a virtual clock steps 1/30 s per rendered frame so every run
// is the same. Run 1 hits everything and takes the screenshots; run 2 misses
// half of the first part, misses C7 and lets go of the C3 hold early.
// Round 11c: StartManual() plays the same chart by hand (space or left click,
// held for holds) with a stand-in judgement of +-0.12 s; the real judgement
// comes later from the backend. Menu: Tools/MrTGame/Chapter 4 Final Heal.
// Round 11b: the third part answers the frozen strings in place (BindAnswer):
// only word beats, two quiet bars first, then cuts that come faster and faster.
public static class OSChapter04FinalHealQA
{
    const string OutputFolder = "Docs/Chapter04Theater/qa_finalheal";
    const string ShotFolder = "Docs/Chapter04Theater";
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const double Beat = .75d, Bar = 3d, Step = 1d / 30d;
    const double WordApproach = 3d, PlayerApproach = 2.5d, HeartRiseApproach = 1.5d, LaneLead = 2.2d;
    // QA placeholders: three thank-you lines (the author writes the real ones)
    static readonly string[] ThankLines = { "谢谢你。（占位 1）", "这是第二句占位。", "第三句占位，最后一句。" };
    // Round 11h epilogue: pancake's lines (QA only; the real lines go into localization)
    static OSChapter04FinalHealPresentation.EpilogueStep L(string text) => OSChapter04FinalHealPresentation.EpilogueStep.Line(text);
    static OSChapter04FinalHealPresentation.EpilogueStep C(string[] options, string[] replies) => OSChapter04FinalHealPresentation.EpilogueStep.Choice(options, replies);
    static readonly OSChapter04FinalHealPresentation.EpilogueStep[] EpilogueScript =
    {
        L("......妈妈，是她认可我了吗？"),
        C(new[] { "她一直都爱着你。" }, null),
        L("爱...吗？"),
        L("我也爱着她...我多希望她能亲身听见我叫她一声妈妈。"),
        C(new[] { "她听见了，只要你记着她，她就一直在你身边。" }, null),
        L("我想我大概懂一点了。"),
        L("谢谢你...没有你陪我把这些记忆一点点修好，我可能一辈子都活在遗憾里..."),
        C(new[] { "慢慢来，爱就是这样一点点浸润着你的。" }, null),
        L("嗯。"),
    };

    static readonly List<string> checks = new List<string>(), failures = new List<string>(), errors = new List<string>();
    static readonly List<object> observations = new List<object>();
    static GameDataManager data;
    static OSChapter04FinalHealPresentation heal;
    static double music;
    static float clock;
    static int endings;
    static bool manual, stopManual;
    const double ManualWindow = .12d;
    public static string Status { get; private set; } = "idle";

    [UnityEditor.MenuItem("Tools/MrTGame/Chapter 4 Final Heal/Run QA (Play Mode)")]
    static void MenuRun() => Debug.Log("OSChapter04FinalHealQA: " + Start());
    [UnityEditor.MenuItem("Tools/MrTGame/Chapter 4 Final Heal/Play By Hand (Play Mode)")]
    static void MenuManual() => Debug.Log("OSChapter04FinalHealQA: " + StartManual());
    [UnityEditor.MenuItem("Tools/MrTGame/Chapter 4 Final Heal/Stop Playing By Hand")]
    static void MenuStop() => StopManual();

    /// <summary>
    /// Play the QA chart by hand: space or the left mouse button beats, held for
    /// holds; a stand-in judgement of +-0.12 s (outside it is a miss). Same
    /// isolated save session as the automatic run; returns to the main menu at the end.
    /// </summary>
    public static string StartManual() => Start(true);
    public static void StopManual() => stopManual = true;

    public static string Start() => Start(false);

    static string Start(bool byHand)
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameTest.Instance == null
            || GameManager.Instance == null) return "Play Mode initialization required";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        checks.Clear(); failures.Clear(); errors.Clear(); observations.Clear(); endings = 0;
        manual = byHand; stopManual = false;
        data = GameDataManager.Instance; Status = "running";
        data.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        bool demo = setup != null && (bool)setup.GetType().GetField("isDemo", Members).GetValue(setup);
        bool background = Application.runInBackground;
        float volume = AudioListener.volume;
        string isolated = Path.GetFullPath(Path.Combine("Temp", "OSChapter04FinalHealQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(isolated);
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        Application.logMessageReceived += Capture;
        saves.EnterTestSession(); saves.UseIsolatedStorageForTests(isolated); saves.EnterTestSession();
        data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
        data.SetCurrentLanguage(LanguageState.zh_CN);
        if (setup != null) setup.GetType().GetField("isDemo", Members).SetValue(setup, false);
        Application.runInBackground = true; if (!manual) AudioListener.volume = 0f;
        work.Push(Run());
        while (work.Count > 0)
        {
            object next = null;
            try
            {
                if (!work.Peek().MoveNext()) { work.Pop(); continue; }
                next = work.Peek().Current;
            }
            catch (Exception error) { failure = error; }
            if (failure != null) break;
            if (next is IEnumerator nested) work.Push(nested); else yield return next;
        }
        try
        {
            if (heal != null)
            {
                heal.EndingFinished -= OnEnding;
                heal.ClearAll(); heal.SetMusicClock(null); heal.SetPresentationClock(null);
            }
            if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
            saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
            if (setup != null) setup.GetType().GetField("isDemo", Members).SetValue(setup, demo);
            Application.runInBackground = background; AudioListener.volume = volume;
        }
        catch (Exception error) { failures.Add("cleanup: " + error.Message); }
        Application.logMessageReceived -= Capture;
        if (failure != null) failures.Add("exception: " + failure);
        Status = failures.Count == 0 && errors.Count == 0 ? "PASS: " + checks.Count + " checks"
            : "FAIL: " + failures.Count + " failed, " + errors.Count + " runtime errors";
        Directory.CreateDirectory(OutputFolder);
        File.WriteAllText(Path.Combine(OutputFolder, "results.json"), JsonConvert.SerializeObject(new
        {
            status = Status, time = DateTime.Now.ToString("s"), checks, failures, runtimeErrors = errors, observations,
            note = "Console checkpoint 19 in isolated storage; QA test chart and words only (from Chapter04FinalHeal_WordFlow_v1). Virtual clock, 1/30 s per frame. No story text, asset or formal save is changed."
        }, Formatting.Indented));
    }

    static void Capture(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
    }

    static void Check(bool ok, string label)
    {
        checks.Add((ok ? "ok   " : "FAIL ") + label);
        if (!ok) failures.Add(label);
    }

    static IEnumerator Wait(Func<bool> done, float seconds)
    {
        float start = Time.unscaledTime;
        while (!done() && Time.unscaledTime - start < seconds) yield return null;
    }

    static void OnEnding() => endings++;

    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        string path = Path.Combine(ShotFolder, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 24, 10f);
        Check(File.Exists(path), "full-screen screenshot written: " + name);
    }

    // ================================================================ test chart

    sealed class Item
    {
        public string id, text, pair;
        public int type, side, part;
        public double time, hold, approach;
        public bool Words => text != null;
        public bool Answer => part == 3 && Words && pair != null;
        public bool spawned, done, missed;
        public double Spawn => time - (Words ? approach : LaneLead);
    }

    static double At(int bar, double beat) => (bar - 1) * Bar + beat * Beat;
    static double HoldOf(int bars) => bars * Bar - 2 * Beat;
    static double AtBar(double bar) => (bar - 1d) * Bar;
    // The third part: bars 23-24 quiet, then a cut every 2 bars, quickening to every half bar.
    static readonly (string id, double bar, int type, double hold, string text, string pair)[] Cuts =
    {
        ("C2", 25d, 0, 0d, "找回来了", "A2"),
        ("C3", 27d, 1, Bar, "她爱的就是你", "A5"),
        ("C4", 29d, 0, 0d, "小家伙", "A4"),
        ("C5", 30.5d, 2, 0d, "你来看她了", "A6"),
        ("C6", 31.5d, 1, 2 * Beat, "她早就不把你当机器了", "A7"),
        ("C7", 32.5d, 0, 0d, "她觉得值得", "A8"),
        ("C8", 33.25d, 0, 0d, "你会哭啊", "A10"),
        ("C9", 33.75d, 1, Beat, "你记住了，你把她都记在心里", "A3"),
        ("C10", 34.25d, 1, Beat, "你是她的好女儿", "B1"),
        ("C11", 34.75d, 2, 0d, "你有心", "A9"),
    };
    static double CutTime(string id) => AtBar(Cuts.First(c => c.id == id).bar);
    // The second part ends: the last beat of bar 20.
    static readonly double FreezeTime = At(20, 3), ReleaseTime = At(23, 0), EndingWord = AtBar(36d);

    static List<Item> Chart()
    {
        var items = new List<Item>();
        void Words(string id, int bar, int bars, int type, string text, int side, int part, string pair = null)
        {
            double approach = side == OSChapter04FinalHealPresentation.SideHeart ? HeartRiseApproach
                : side == OSChapter04FinalHealPresentation.SideBottom ? LaneLead : WordApproach;
            items.Add(new Item { id = id, text = text, type = type, side = side, part = part, pair = pair,
                time = At(bar, 0), hold = type == 1 ? HoldOf(bars) : 0d, approach = approach });
        }
        const int L = 0, R = 1, T = 2, B = 3;
        // first part, bars 3-14 (bars 1-2: heartbeat only)
        Words("A2", 3, 1, 0, "弄丢了", R, 1);
        Words("A3", 4, 2, 1, "连妈妈的事都记不住", L, 1);
        Words("A4", 6, 1, 0, "保姆", T, 1);
        Words("A5", 7, 1, 0, "替代品", R, 1);
        Words("A6", 8, 1, 2, "记不住", L, 1);
        Words("A7", 9, 2, 1, "我只会叫她主人", R, 1);
        Words("A8", 11, 1, 0, "不划算", T, 1);
        Words("A9", 12, 1, 0, "没心", L, 1);
        Words("A10", 13, 2, 2, "机器", R, 1);
        // second part: the nine come back from every side with B1, still in the air at the freeze
        var back = new[] { ("A2", "弄丢了", 0), ("A3", "连妈妈的事都记不住", 1), ("A4", "保姆", 0), ("A5", "替代品", 0),
            ("A6", "记不住", 2), ("A7", "我只会叫她主人", 1), ("A8", "不划算", 0), ("A9", "没心", 0), ("A10", "机器", 2) };
        var sides = new[] { L, R, T, R, L, T, L, R, T };
        var reached = new[] { .45f, .7f, .55f, .8f, .4f, .65f, .5f, .75f, .6f };   // how far each is at the freeze
        for (int i = 0; i < back.Length; i++)
        {
            double spawn = At(15, 0) + i * 1.5d;
            double approach = (FreezeTime - spawn) / reached[i];
            items.Add(new Item { id = back[i].Item1, text = back[i].Item2, type = back[i].Item3, side = sides[i], part = 2,
                time = spawn + approach, approach = approach, hold = back[i].Item3 == 1 ? HoldOf(1) : 0d });
        }
        // B2 was cut by the author (11-01): only B1 joins the nine
        foreach (var (id, bar, text, side, reach) in new[] { ("B1", 17, "我真的是她合格的女儿吗", R, .6f) })
        {
            double spawn = At(bar, 0), approach = (FreezeTime - spawn) / reach;
            items.Add(new Item { id = id, text = text, type = 1, side = side, part = 2, time = spawn + approach, approach = approach, hold = HoldOf(2) });
        }
        // third part: word beats only, each bound to its frozen string, which charges the heart
        foreach (var cut in Cuts)
            items.Add(new Item { id = cut.id, text = cut.text, type = cut.type, side = B, part = 3, pair = cut.pair,
                time = AtBar(cut.bar), hold = cut.hold, approach = OSChapter04FinalHealPresentation.AnswerLeadSeconds });
        // the close: one heavy beat, "妈妈", rising from under the heart (it knocks nothing down)
        items.Add(new Item { id = "D2", text = "妈妈", type = 2, side = OSChapter04FinalHealPresentation.SideHeart, part = 4,
            time = EndingWord, approach = HeartRiseApproach });

        // plain beats under the words: quarters, eighths in the second half of each part, none under a word or a hold
        var busy = items.Where(i => i.part != 2).Select(i => (from: i.time - Beat * .5d, to: i.time + i.hold + Beat * .5d)).ToList();
        bool Free(double time) => !busy.Any(b => time > b.from && time < b.to);
        int n = 0;
        void Plain(double time, int type = 0) { if (Free(time)) items.Add(new Item { id = "n" + n++, time = time, type = type, part = PartOf(time) }); }
        for (int bar = 3; bar <= 14; bar++)
        {
            for (int beat = 0; beat < 4; beat++) Plain(At(bar, beat));
            if (bar >= 11) Plain(At(bar, 1.5d));
        }
        for (int bar = 15; bar <= 20; bar++)
            for (double beat = 0; beat < 4; beat += bar < 17 ? 1d : .5d)
                if (At(bar, beat) < FreezeTime - .01d) Plain(At(bar, beat), bar == 20 && beat == 0 ? 2 : 0);
        return items.OrderBy(i => i.time).ToList();
    }

    static int PartOf(double time) => time < At(15, 0) ? 1 : time < ReleaseTime ? 2 : time < EndingWord ? 3 : 4;

    // ================================================================ run

    static IEnumerator Run()
    {
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "console checkpoint 19 accepts the jump");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && OS_Handle.Instance != null && OS_Handle.Instance.LinkMessageController?.DisplayedTextLabel != null, 80f);
        var os = OS_Handle.Instance;
        Check(os != null && data.CurrentChapterIndex == 4, "chapter four OS is up");
        yield return null;
        heal = OSChapter04FinalHealPresentation.Ensure();
        Check(heal != null && OSChapter04FinalHealPresentation.Current == heal && heal.gameObject == os.gameObject,
            "the final heal presentation lives on the OS handle (Ensure)");
        heal.EndingFinished += OnEnding;
        heal.SetMusicClock(() => music);
        heal.SetPresentationClock(() => clock);

        var chart = Chart();
        observations.Add(new
        {
            chart = "80 BPM metronome, QA only",
            notes = chart.Count,
            words = chart.Count(i => i.Words),
            perPart = Enumerable.Range(1, 4).Select(p => new { part = p, notes = chart.Count(i => i.part == p), words = chart.Count(i => i.part == p && i.Words) })
        });
        if (manual) { yield return Play(chart, false, false); yield break; }
        Check(chart.Count >= 90 && chart.Count <= 120 && chart.Count(i => i.Words) == 30 && chart.Count(i => i.part == 3) == 10,
            "test chart: about 100 notes, 30 word beats (9 + 10 + 10 + 1), the third part only its 10 cuts");

        yield return Play(chart, false, true);
        heal.ClearAll();
        Check(!heal.IsVisible && heal.LivePhraseCount == 0 && heal.ParticleCount == 0 && heal.LaneNoteCount == 0, "ClearAll empties the screen");
        yield return Play(Chart(), true, false);
    }

    static readonly Dictionary<double, string> ShotTimes = new Dictionary<double, string>();

    /// <summary>One whole treatment. missHalf: miss every other note of the first part.</summary>
    static IEnumerator Play(List<Item> chart, bool missHalf, bool shots)
    {
        string run = manual ? "by hand" : missHalf ? "run 2 (half of part one missed)" : "run 1 (all hit)";
        music = -At(2, 0); clock = 100f;           // two bars of heartbeat before the first note
        endings = 0;
        heal.Begin(false);
        heal.SetHeartStage(0, false);
        // let the 0.4 s dimming fade in before looking at the layer
        clock += .5f; heal.Render();
        if (!manual) CheckLayer(run);
        if (!manual)
        {
            // an empty press (no note near): the line bumps, the heart swells, nothing red
            int ringsBefore = heal.RingCount;
            heal.PlayPress(); heal.Render();
            Check(heal.JudgeBump == 2 && Same(heal.GetPixel(OSChapter04FinalHealPresentation.JudgeX, OSChapter04FinalHealPresentation.LaneInnerTop - 1), OSChapter04FinalHealPresentation.Light)
                && heal.LastHeartFrame.EndsWith("+press") && heal.RingCount == ringsBefore && !heal.IsEdgeFlashing,
                run + ": an empty press bumps the judgement line up 2 px in light gray and swells the heart, no red");
            clock += (float)Step; heal.Render();
            bool settling = heal.JudgeBump == 1 && !heal.LastHeartFrame.EndsWith("+press");
            clock += (float)Step; heal.Render();
            clock += (float)Step; heal.Render();
            Check(settling && heal.JudgeBump == 0 && Same(heal.GetPixel(OSChapter04FinalHealPresentation.JudgeX, OSChapter04FinalHealPresentation.LaneInnerTop + 1), OSChapter04FinalHealPresentation.Gray),
                run + ": the bump settles back over three 30 fps steps");
        }
        var firstLateTap = chart.First(i => i.part == 1 && !i.Words && i.time > At(10, 0));
        var firstMissedTap = chart.First(i => i.part == 1 && !i.Words && chart.IndexOf(i) % 2 == 0);
        var shotList = manual ? new List<(double time, string name)>() : shots ? new List<(double time, string name)>
        {
            (firstLateTap.time + .04d, "82_finalheal_lane_hit"),
            (At(9, 0) + 1.03d, "83_finalheal_lane_hold"),
            (At(4, 0) + 2d, "79_finalheal_hold_eating"),
            (At(7, 0) + .12d, "71_finalheal_selfblame_falling"),
            (At(8, 0) - 1.2d, "70_finalheal_part1"),
            (At(9, 0) + .2d, "77_finalheal_stage_dissolve"),
            (FreezeTime + 2.5d, "72_finalheal_part2_frozen"),
            (CutTime("C5") + .02d, "73_finalheal_part3_cut"),
            (CutTime("C8") + .6d, "80_finalheal_part3_half_green"),
            (EndingWord + .45d, "74_finalheal_heart_whole"),
        } : new List<(double time, string name)>
        {
            (firstMissedTap.time + .12d + .04d, "84_finalheal_lane_miss"),
            (CutTime("C7") + .12d + .04d, "81_finalheal_answer_miss_crash"),
        };
        shotList = shotList.OrderBy(shot => shot.time).ToList();
        int hitCalls = 0, missCalls = 0, postcardClicked = 0;
        float choiceSeenAt = -1f, postcardSeenAt = -1f;
        bool dialogueShot = false, sinkShot = false, longShot = false, celebrateShot = false;
        bool peakShot = false;
        Item manualHold = null;
        var manualCracked = new Dictionary<Item, double>();
        const double SecondHitWindow = .35d;     // the old tool's ReinforcedSecondHitWindow
        int shotIndex = 0;
        bool frozenSeen = false, released = false, stage1 = false, ended = false, dissolveSeen = false, missShot = false;
        int paletteChecks = 0, paletteBad = 0, knockOk = 0, knockTried = 0, perspectiveOk = 0, perspectiveTried = 0;
        int answersOk = 0, answersTried = 0, crashOk = 0;
        bool edgeSeen = false, answersCounted = false, fadedChecked = false;
        var crashChecks = new List<(double time, Item item)>();
        var answerChecks = new List<(double time, Item item)>();
        int hitStops = 0, slashSeen = 0;
        double cutProgress = 0d;
        double endingStart = double.MaxValue;
        float sinkStart = -1f;
        var holding = new Dictionary<string, Item>();
        int frame = 0;
        while (true)
        {
            double dt = manual ? Time.unscaledDeltaTime : Step;
            music += dt; clock += (float)dt; frame++;
            if (manual && stopManual) break;
            // the backend's second part: rings shrink, the heart falls back, everything stops
            if (music >= At(15, 0) && music < FreezeTime) heal.SetSinkProgress((float)((music - At(15, 0)) / (FreezeTime - At(15, 0))));
            if (!stage1 && music >= At(9, 0)) { stage1 = true; heal.SetHeartStage(1, true); }
            if (music >= At(17, 0) && heal.HeartStage == 1 && !frozenSeen) heal.SetHeartStage(0, true);
            if (!frozenSeen && music >= FreezeTime)
            {
                frozenSeen = true;
                heal.FreezeAll();
                Check(heal.IsFrozen && heal.FrozenPhraseCount == 10, run + ": FreezeAll leaves 10 strings hanging (got " + heal.FrozenPhraseCount + ")");
                Check(heal.LaneNoteCount == chart.Count(i => i.missed && i.part == 2 && !i.done) || heal.LaneNoteCount == 0,
                    run + ": FreezeAll clears the waiting lane notes");
            }
            if (!released && music >= ReleaseTime) { released = true; heal.ReleaseFreeze(); heal.SetSinkProgress(0f); }

            foreach (var item in chart)
            {
                if (!item.spawned && music >= item.Spawn)
                {
                    item.spawned = true;
                    if (item.Answer)
                    {
                        Check(heal.IsPhraseFrozen(item.pair), run + ": " + item.id + " binds to " + item.pair + ", still hanging");
                        heal.BindAnswer(item.id, item.pair, item.text, item.type, item.time, item.approach, item.hold);
                    }
                    else if (item.Words) heal.SpawnPhrase(item.id, item.text, item.type, item.time, item.approach, item.side, item.hold);
                    else heal.SpawnBeat(item.id, item.type, item.time, item.hold);
                }
                if (item.done || !item.spawned) continue;
                if (item.part == 2 && item.Words) { if (music >= FreezeTime) item.done = true; continue; }   // never resolved: they hang
                if (item.part == 2 && music >= FreezeTime) { item.done = true; continue; }                  // cleared by the freeze
                if (manual) continue;                                                                     // judged below by hand
                bool miss = missHalf && (item.part == 1 && chart.IndexOf(item) % 2 == 0 || item.id == "C7" || item.id == "C3");
                if (music < item.time) continue;
                if (item.hold > 0d)
                {
                    float t = (float)((music - item.time) / item.hold);
                    if (miss && t >= .4f) { heal.ResolveMiss(item.id); missCalls++; item.done = item.missed = true; continue; }
                    if (t < 1f) { heal.SetHoldProgress(item.id, t); holding[item.id] = item; continue; }
                    heal.SetHoldProgress(item.id, 1f);
                }
                else if (miss && music < item.time + .12d) continue;
                if (miss)
                {
                    bool pairUp = item.Answer && heal.IsPhraseAlive(item.pair);
                    heal.ResolveMiss(item.id); missCalls++; item.done = item.missed = true;
                    if (item.Answer) cutProgress += .5d;          // a miss still grows the heart, less
                    if (heal.IsEdgeFlashing) edgeSeen = true;
                    if (item.Answer && item.hold <= 0d) crashChecks.Add((music + .3d, item));
                    if (item.Answer && item.hold > 0d)
                        Check(heal.IsPhraseAlive(item.pair) && !heal.IsAnswerShown(item.id),
                            run + ": letting go of " + item.id + " early keeps its cut string up and its line for 「妈妈」");
                    else if (item.Answer) Check(pairUp && heal.IsPhraseAlive(item.pair), run + ": a missed " + item.id + " first drives " + item.pair + " into the heart");
                    continue;
                }
                if (item.Answer)
                {
                    bool tap = item.hold <= 0d;
                    heal.ResolveHit(item.id); hitCalls++;
                    item.done = true;
                    answersTried++;
                    if (tap) { if (heal.IsHitStopped) hitStops++; if (heal.SlashCount > 0) slashSeen++; }
                    answerChecks.Add((music + .3d, item));
                    cutProgress += 1d;
                    continue;
                }
                if (item.id == "D2" && !answersCounted)
                {
                    answersCounted = true;
                    Check(heal.ShownAnswerCount == 0, run + ": no player line shows before the closing beat (" + heal.ShownAnswerCount + ")");
                    Check(heal.WreckCount == 10 && heal.IsPhraseAlive("D2"), run + ": all ten cut or missed strings stand by the heart before the closing beat (" + heal.WreckCount + ")");
                }
                heal.ResolveHit(item.id); hitCalls++;
                item.done = true;
                if (!string.IsNullOrEmpty(item.pair))
                {
                    bool wasUp = heal.IsPhraseAlive(item.pair) && heal.IsPhraseFrozen(item.pair);
                    bool knocked = heal.KnockDown(item.pair);
                    knockTried++;
                    if (wasUp && knocked && !heal.IsPhraseAlive(item.pair) && heal.KnockedDown.LastOrDefault() == item.pair) knockOk++;
                    else observations.Add(new { run, knockDownFailed = item.pair, wasUp, knocked });
                }
                if (item.id == "D2") { heal.SetHeartStage(3, true); endingStart = music + 1.2d; }
            }
            if (manual)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                bool pressed = keyboard != null && keyboard.spaceKey.wasPressedThisFrame || mouse != null && mouse.leftButton.wasPressedThisFrame;
                bool held = keyboard != null && keyboard.spaceKey.isPressed || mouse != null && mouse.leftButton.isPressed;
                if (pressed) heal.PlayPress();
                void Hit(Item item)
                {
                    heal.ResolveHit(item.id); item.done = true;
                    if (item.Answer) cutProgress += 1d;
                    if (item.id == "D2") { heal.SetHeartStage(3, true); endingStart = music + 1.2d; }
                }
                void Miss(Item item)
                {
                    heal.ResolveMiss(item.id); item.done = item.missed = true;
                    if (item.Answer) cutProgress += .5d;
                }
                if (manualHold != null)
                {
                    float t = (float)((music - manualHold.time) / manualHold.hold);
                    if (t >= 1f) { heal.SetHoldProgress(manualHold.id, 1f); Hit(manualHold); manualHold = null; }
                    else if (!held && music < manualHold.time + manualHold.hold - ManualWindow) { Miss(manualHold); manualHold = null; }
                    else heal.SetHoldProgress(manualHold.id, Mathf.Max(0f, t));
                }
                bool Judgeable(Item item) => item.spawned && !item.done && !(item.part == 2 && item.Words) && item != manualHold
                    && !manualCracked.ContainsKey(item);
                // a cracked heavy beat: the second press within 0.35 s settles it (the closing beat waits)
                Item crackedTarget = pressed ? manualCracked.Keys.FirstOrDefault(i => !i.done) : null;
                if (crackedTarget != null)
                {
                    manualCracked.Remove(crackedTarget);
                    Hit(crackedTarget);
                    pressed = false;
                }
                foreach (var pair in manualCracked.ToArray())
                    if (!pair.Key.done && pair.Key.id != "D2" && music > pair.Value + SecondHitWindow) { manualCracked.Remove(pair.Key); Miss(pair.Key); }
                if (pressed && manualHold == null)
                {
                    var target = chart.Where(i => Judgeable(i) && Math.Abs(music - i.time) <= ManualWindow
                        || Judgeable(i) && i.id == "D2" && music >= i.time - ManualWindow).OrderBy(i => i.time).FirstOrDefault();
                    if (target != null && target.hold > 0d) { manualHold = target; heal.SetHoldProgress(target.id, 0f); }
                    else if (target != null && target.type == 2 && !target.Answer && heal.CrackShell(target.id)) manualCracked[target] = music;
                    else if (target != null) Hit(target);
                }
                // too late: a miss (the closing beat waits for its press)
                foreach (var item in chart)
                    if (Judgeable(item) && item.id != "D2" && music > item.time + ManualWindow) Miss(item);
                if (cutProgress >= 3d && heal.HeartStage < 1 && released) heal.SetHeartStage(1, true);
                if (cutProgress >= 7d && heal.HeartStage < 2) heal.SetHeartStage(2, true);
                if (!ended && music >= endingStart) { ended = true; heal.PlayEpilogue(EpilogueScript); }
                heal.Render();
                if (heal.IsEndingFinished && heal.ParticleCount == 0) break;
                yield return null;
                continue;
            }
            // perspective: a string from the side is smaller at the heart's end
            foreach (var id in new[] { "A2", "A3", "A5", "A7" })
                if (music < At(15, 0) && heal.IsPhraseAlive(id))
                {
                    var item = chart.First(i => i.id == id && i.part == 1);
                    double progress = (music - (item.time - item.approach)) / item.approach;
                    if (progress > .55d && progress < .6d)
                    {
                        heal.Render();
                        var h = heal.PhraseEndHeights(id);
                        perspectiveTried++;
                        if (h.x > 0 && h.y > h.x) perspectiveOk++;
                        else observations.Add(new { run, perspective = id, nearHeight = h.x, farHeight = h.y });
                    }
                }
            foreach (var (time, item) in crashChecks.ToArray())
                if (music >= time)
                {
                    crashChecks.Remove((time, item));
                    if (heal.IsPhraseWreck(item.pair) && !heal.IsAnswerShown(item.id)) crashOk++;
                    else observations.Add(new { run, crashFailed = item.id, item.pair });
                }
            foreach (var (time, item) in answerChecks.ToArray())
                if (music >= time)
                {
                    answerChecks.Remove((time, item));
                    if (heal.IsPhraseWreck(item.pair) && !heal.IsAnswerShown(item.id)) answersOk++;
                    else observations.Add(new { run, answerFailed = item.id, item.pair });
                }
            // the heart grows with the third part's progress (a hit more than a miss), whole only at D2
            if (cutProgress >= 3d && heal.HeartStage < 1 && released) heal.SetHeartStage(1, true);
            if (cutProgress >= 7d && heal.HeartStage < 2) heal.SetHeartStage(2, true);
            if (heal.IsEdgeFlashing) edgeSeen = true;
            if (!fadedChecked && endingStart < double.MaxValue && music >= endingStart - .1d)
            {
                fadedChecked = true;
                Check(heal.ShownAnswerCount == 10 && heal.WreckCount == 0, run + ": on the closing beat the stopped strings drop and all ten green lines rise");
            }
            if (!ended && music >= endingStart) { ended = true; heal.PlayEpilogue(EpilogueScript); }
            heal.Render();
            if (heal.LastDissolveOldPixels > 0) dissolveSeen = true;
            if (frame % 15 == 0) { paletteChecks++; if (!PaletteOk()) paletteBad++; }

            if (shotIndex < shotList.Count && music >= shotList[shotIndex].time)
            {
                yield return Shot(shotList[shotIndex].name);
                shotIndex++;
            }
            if (missHalf && !peakShot && heal.EdgeFlashIntensity >= .95f)
            {
                peakShot = true;
                Check(Same(heal.GetPixel(0, 0), OSChapter04FinalHealPresentation.MissRed) && !Same(heal.GetPixel(320, 180), OSChapter04FinalHealPresentation.MissRed)
                    && !Same(heal.GetPixel(320, 60), OSChapter04FinalHealPresentation.MissRed),
                    run + ": at its peak the dark red miss vignette fills the screen corner and leaves the wide middle open");
                yield return Shot("85_finalheal_miss_vignette_peak");
            }
            if (!shots && !missShot && heal.SpecksMade > 0 && music >= At(3, 0) + .15d)
            {
                missShot = true;
                yield return Shot("78_finalheal_miss_specks");
            }
            if (ended && !manual)
            {
                if (heal.IsEpilogueAwaitingChoice)
                {
                    if (choiceSeenAt < 0f) choiceSeenAt = clock;
                    if (shots && !dialogueShot) { dialogueShot = true; yield return Shot("86_finalheal_epilogue_dialogue"); yield return Shot("90_finalheal_epilogue_tmp_text"); }
                    if (shots && !longShot && heal.EpilogueStepIndex == 4) { longShot = true; yield return Shot("88_finalheal_epilogue_long_reply"); }
                    if (clock - choiceSeenAt >= .4f) { if (!heal.ChooseEpilogueOption(missHalf ? 1 : 0)) heal.ChooseEpilogueOption(0); choiceSeenAt = -1f; }
                }
                if (shots && !celebrateShot && heal.EpiloguePhaseIndex == 1 && heal.EpilogueSinkProgress >= .1f)
                {
                    celebrateShot = true;
                    yield return Shot("89_finalheal_celebrate_ring");
                }
                if (shots && !sinkShot && heal.EpiloguePhaseIndex == 1 && heal.EpilogueSinkProgress >= .5f)
                {
                    sinkShot = true;
                    yield return Shot("87_finalheal_epilogue_sinking");
                }
                if (heal.IsPostcardWaiting)
                {
                    if (postcardSeenAt < 0f) postcardSeenAt = clock;
                    if (clock - postcardSeenAt >= .6f) { heal.PlayPress(); postcardSeenAt = -1f; postcardClicked++; }
                }
            }
            if (ended && shots)
            {
                if (!tossShot && heal.IsPostcardMoving && heal.PostcardAngle > -12f && heal.PostcardAngle < -7f)
                {
                    tossShot = true;
                    yield return Shot("91_finalheal_postcard_toss");
                }
                if (heal.EndingPhase == 4 && heal.IsPostcardWaiting && heal.PostcardTop == (OSChapter04FinalHealPresentation.Height - 200) / 2 && !postcardShot)
                {
                    postcardShot = true;
                    var artwork = Resources.Load<Texture2D>("Chapter04/FinalHeal/EndingPostcard");
                    int left = (OSChapter04FinalHealPresentation.Width - 340) / 2;
                    // round 11j: it lands tossed, at its rest angle; its center pixel stays put under the turn
                    Check(artwork != null && artwork.width == 340 && artwork.height == 200 && artwork.isReadable
                        && Mathf.Abs(heal.PostcardAngle - OSChapter04FinalHealPresentation.PostcardRestAngle) < .01f
                        && Same(heal.GetPixel(left + 170, heal.PostcardTop + 100), PostcardOnBlack((Color32)artwork.GetPixel(170, 99))),
                        run + ": the authored 340x200 postcard lands in the middle at its rest angle (" + heal.PostcardAngle.ToString("0.0") + " deg) with its own pixels");
                    yield return Shot("75_finalheal_postcard");
                    yield return Shot("92_finalheal_postcard_landed");
                }
                if (heal.EndingPhase == 5 && heal.CreditsScroll > 200 && !creditsShot)
                {
                    creditsShot = true;
                    yield return Shot("76_finalheal_staff_scroll");
                }
            }
            if (heal.IsEndingFinished && heal.ParticleCount == 0) break;
            if (music > EndingWord + 120d) { Check(false, run + ": the ending finished within two minutes of the last beat"); break; }
            yield return null;
        }
        postcardShot = creditsShot = tossShot = false;
        if (manual)
        {
            observations.Add(new { run, frames = frame, stopped = stopManual, hit = chart.Count(i => i.done && !i.missed), missed = chart.Count(i => i.missed) });
            yield break;
        }
        Check(heal.LaneHitEffects == hitCalls && heal.LaneMissEffects == missCalls,
            run + ": every hit and every miss sets off the lane's feedback (" + heal.LaneHitEffects + "/" + hitCalls + ", " + heal.LaneMissEffects + "/" + missCalls + ")");
        Check(heal.HoldTicks > 0, run + ": held notes tick on the judgement line (" + heal.HoldTicks + ")");

        Check(heal.ShownStages.Count == 4, run + ": all four heart stages were drawn (" + string.Join(",", heal.ShownStages) + ")");
        Check(dissolveSeen, run + ": stage changes dissolve through Bayer frames that still hold the old stage");
        Check(paletteChecks > 100 && paletteBad == 0, run + ": every sampled frame keeps the treatment palette outside the postcard and exact authored pixels inside it (" + paletteChecks + " frames)");
        Check(perspectiveTried >= 4 && perspectiveOk == perspectiveTried, run + ": side strings are smaller at the heart's end (" + perspectiveOk + "/" + perspectiveTried + ")");
        Check(knockTried == 0, run + ": the closing beat knocks nothing down (B2 is cut)");
        int answerMisses = missHalf ? 2 : 0;      // run 2 misses the C7 tap and lets go of the C3 hold early
        int taps = Cuts.Count(c => c.hold <= 0d) - (missHalf ? 1 : 0);
        Check(hitStops == taps && slashSeen == taps, run + ": every cut holds the layer still and draws the red slash (" + hitStops + ", " + slashSeen + " of " + taps + ")");
        Check(answersTried == 10 - answerMisses && answersOk == answersTried,
            run + ": every third-part hit splits its own frozen string and keeps it beside the heart, the line waiting (" + answersOk + "/" + answersTried + ")");
        if (missHalf)
        {
            Check(crashOk == 1, run + ": a missed answer's string drives into the heart and stays on it (" + crashOk + "/1)");
            Check(edgeSeen, run + ": misses flash the screen edge red");
        }
        Check(heal.FallingLetters > 0 && heal.FallenOffscreen == heal.FallingLetters,
            run + ": every falling letter left the screen (" + heal.FallenOffscreen + "/" + heal.FallingLetters + ")");
        Check(heal.FlownIntoHeart > 0, run + ": the player's letters flew into the heart (" + heal.FlownIntoHeart + ")");
        if (missHalf) Check(heal.SpecksMade > 0, run + ": missed words broke into specks (" + heal.SpecksMade + ")");
        Check(endings == 1 && heal.IsEndingFinished && heal.EndingPhase == 6, run + ": the ending raised EndingFinished once");
        Check(heal.EpilogueChoicesMade == 3 && postcardClicked == 1,
            run + ": the epilogue ran through its three reply sets and the postcard waited for one click (" + heal.EpilogueChoicesMade + ", " + postcardClicked + ")");
        Check(heal.GetPixel(10, 10).a == 255 && Same(heal.GetPixel(10, 10), OSChapter04FinalHealPresentation.Black), run + ": the screen stays black after the ending");
        observations.Add(new
        {
            run, frames = frame, heal.FallingLetters, heal.FallenOffscreen, heal.FlownIntoHeart, heal.SpecksMade,
            knocked = heal.KnockedDown.ToArray(), stages = heal.ShownStages.ToArray(), paletteChecks
        });
    }

    static bool postcardShot, creditsShot, tossShot;

    static void CheckLayer(string run)
    {
        var rect = heal.LayerRect;
        var canvas = heal.LayerCanvas;
        var texture = heal.LayerTexture;
        var root = canvas != null ? canvas.rootCanvas : null;
        var rootRect = rect != null ? rect.parent as RectTransform : null;   // the OS root canvas the layer sits on
        observations.Add(new { run, layerSize = rect?.rect.size.ToString("F4"), layerPos = rect?.anchoredPosition.ToString(), rootSize = rootRect?.rect.size.ToString("F4"), rootName = rootRect?.name,
            layerScale = rect?.lossyScale.ToString("F5"), rootScale = rootRect?.lossyScale.ToString("F5") });
        Check(rect != null && Near(rect.rect.size, new Vector2(640f, 360f)) && Near(rect.anchoredPosition, Vector2.zero)
            && rootRect != null && Near(rootRect.rect.size, new Vector2(640f, 360f)) && Mathf.Abs(rect.lossyScale.x / rootRect.lossyScale.x - 1f) < 1e-4f && Mathf.Abs(rect.lossyScale.y / rootRect.lossyScale.y - 1f) < 1e-4f,
            run + ": the layer is exactly the 640 x 360 OS canvas, 1:1");
        Check(texture != null && texture.width == 640 && texture.height == 360 && texture.filterMode == FilterMode.Point,
            run + ": one 640 x 360 point texture: every pixel on the OS grid, no smoothing");
        var grief = OS_Handle.Instance.GetComponent<OSChapter04GriefPresentation>();
        Check(canvas != null && canvas.overrideSorting && canvas.sortingOrder == OSChapter04FinalHealPresentation.SortingOrder
            && canvas.sortingOrder > 32000 && canvas.sortingOrder < 32100, run + ": sorted over the grave grief dither (32000), under its warning (32100)");
        var cursor = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(c => c.isRootCanvas && c.name == "CursorCanvas");
        observations.Add(new { run, layerOrder = canvas?.sortingOrder, cursorCanvasOrder = cursor?.sortingOrder, griefPresent = grief != null });
        int dark = 0;
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) if (heal.GetPixel(4 + x, 4 + y).a == 255) dark++;
        Check(dark == 10, run + ": Begin dims the OS with Bayer dots one step past the grave's tier 3 (10/16, got " + dark + ")");
        Check(Same(heal.GetPixel(320, 132), OSChapter04FinalHealPresentation.Black) || heal.GetPixel(320, 132).a == 255,
            run + ": the heart is drawn at its center (320, 132)");
        Check(Same(heal.GetPixel(320, 297), OSChapter04FinalHealPresentation.Red) && Same(heal.GetPixel(321, 298), OSChapter04FinalHealPresentation.Red),
            run + ": the judgement line carries a 2 x 2 red dot");
    }

    static bool Near(Vector2 a, Vector2 b) => Mathf.Abs(a.x - b.x) < .01f && Mathf.Abs(a.y - b.y) < .01f;

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    // The ending is opaque black; transparent postcard pixels leave that background visible.
    static Color32 PostcardOnBlack(Color32 source) => source.a >= 128 ? source : OSChapter04FinalHealPresentation.Black;

    static bool PaletteOk()
    {
        var allowed = new[] { OSChapter04FinalHealPresentation.Black, OSChapter04FinalHealPresentation.Gray,
            OSChapter04FinalHealPresentation.Light, OSChapter04FinalHealPresentation.Red, OSChapter04FinalHealPresentation.Green,
            OSChapter04FinalHealPresentation.MissRed };
        int cardTop = heal.PostcardTop;
        var postcard = cardTop != int.MinValue ? Resources.Load<Texture2D>("Chapter04/FinalHeal/EndingPostcard") : null;
        var postcardPixels = postcard != null && postcard.isReadable && postcard.width == 340 && postcard.height == 200
            ? postcard.GetPixels32() : null;
        bool square = Mathf.Abs(heal.PostcardAngle) < .05f && !heal.IsPostcardMoving;
        for (int y = 0; y < 360; y += 1)
            for (int x = 0; x < 640; x += 1)
            {
                var c = heal.GetPixel(x, y);
                // The postcard has its own warm gray and dark red. Restrict the
                // exception to its actual moving rectangle and source pixels on black.
                if (heal.IsPostcardPixel(x, y)) continue;      // the card's own pixels, turned (round 11j)
                // the exact-source rectangle only holds while the card lies square and still
                if (postcardPixels != null && square && x >= 150 && x < 490 && y >= cardTop && y < cardTop + 200)
                {
                    if (!Same(c, PostcardOnBlack(postcardPixels[(199 - (y - cardTop)) * 340 + x - 150]))) return false;
                    continue;
                }
                if (c.a == 0) continue;
                if (c.a != 255 || !allowed.Any(p => Same(p, c))) return false;
            }
        return true;
    }
}
