#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using MrTGame.Core;
using UnityEngine;

/// <summary>
/// Console-only, automatic preview of the round-11b final-treatment screen.
/// The 80 BPM chart and words mirror OSChapter04FinalHealQA's all-hit run.
/// This is not a gameplay director: it never changes story state, resources,
/// saves, Demo settings or judgement data. The caller prepares the checkpoint.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class OSChapter04FinalHealDebugPreview : MonoBehaviour
{
    const double Beat = .75d, Bar = 3d;
    const double WordApproach = 3d, HeartRiseApproach = 1.5d, LaneLead = 2.2d;
    const string ThankYou = "谢谢你。"; // Same QA placeholder; production copy is not supplied here.

    OS_Handle os;
    OSWindowUnit settingsWindow;
    OSChapter04FinalHealPresentation heal;
    List<Item> chart;
    double elapsed, music, cutProgress, endingStart;
    bool running, ownsPresentation, stage1, frozenSeen, released, endingStarted, returnPending;

    public bool IsRunning => running;
    public double MusicTime => music;

    /// <summary>
    /// Starts or restarts the preview on the current chapter-four OS. The
    /// console must have entered a protected test session before calling this.
    /// </summary>
    public static bool Begin()
    {
        var host = OS_Handle.Instance;
        if (!Application.isPlaying || GameSetup.IsDemo || host == null || !host.isActiveAndEnabled
            || GameDataManager.Instance?.CurrentChapterIndex != 4
            || GameAutosaveService.Instance?.IsTestSession != true) return false;

        var preview = host.GetComponent<OSChapter04FinalHealDebugPreview>();
        if (preview == null) preview = host.gameObject.AddComponent<OSChapter04FinalHealDebugPreview>();
        return preview.BeginOn(host);
    }

    bool BeginOn(OS_Handle host)
    {
        Stop();
        os = host;
        heal = OSChapter04FinalHealPresentation.Ensure();
        if (heal == null) return false;
        settingsWindow = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        chart = BuildChart();
        elapsed = cutProgress = 0d;
        music = -At(2, 0);
        endingStart = double.MaxValue;
        stage1 = frozenSeen = released = endingStarted = returnPending = false;

        ownsPresentation = true;
        heal.ClearAll();
        // Actual elapsed unscaled time, paused together while the console or
        // settings are open. No QA virtual-frame loop or coroutine is started.
        heal.SetMusicClock(() => music);
        heal.SetPresentationClock(() => 100f + (float)elapsed);
        heal.EndingFinished += OnEndingFinished;
        running = true;
        enabled = true;
        heal.Begin(false);
        heal.SetHeartStage(0, false);
        if (heal.IsVisible) return true;
        Stop();
        return false;
    }

    /// <summary>Stops the preview and releases both clocks and the full-screen layer.</summary>
    public void Stop()
    {
        running = returnPending = false;
        chart?.Clear();
        chart = null;
        if (heal != null && ownsPresentation)
        {
            ownsPresentation = false;
            heal.EndingFinished -= OnEndingFinished;
            heal.ClearAll();
            heal.SetMusicClock(null);
            heal.SetPresentationClock(null);
        }
        ownsPresentation = false;
        heal = null;
        os = null;
        settingsWindow = null;
    }

    void OnDisable() => Stop();
    void OnDestroy() => Stop();
    void OnEndingFinished() => returnPending = running;

    void Update()
    {
        if (!running) return;
        // The test-session guard remains mandatory for the entire preview.
        // Leaving the OS or loading another chapter also releases our layer.
        if (GameAutosaveService.Instance?.IsTestSession != true
            || GameDataManager.Instance?.CurrentChapterIndex != 4
            || os == null || os != OS_Handle.Instance || !os.IsOSVisible
            || heal == null || !heal.IsVisible)
        {
            Stop();
            return;
        }
        bool paused = GameTestConsole.BlocksGameplayInput || OSSessionPersistence.IsRestoring
            || GameSessionFlow.Instance?.IsBusy == true || GameManager.Instance?.IsTransitioning == true
            || settingsWindow != null && settingsWindow.IsOpen && !settingsWindow.IsMinimized;
        heal.SetPaused(paused);
        if (paused) return;
        if (returnPending)
        {
            // Let the presentation finish its Render/event dispatch first;
            // returning here avoids changing the OS inside that callback.
            Stop();
            GameManager.Instance?.OpenMainMenuWithoutSave();
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        music = elapsed - At(2, 0);
        AdvanceChart();
    }

    void AdvanceChart()
    {
        if (music >= At(15, 0) && music < FreezeTime)
            heal.SetSinkProgress((float)((music - At(15, 0)) / (FreezeTime - At(15, 0))));
        if (!stage1 && music >= At(9, 0)) { stage1 = true; heal.SetHeartStage(1, true); }
        if (music >= At(17, 0) && heal.HeartStage == 1 && !frozenSeen) heal.SetHeartStage(0, true);
        if (!frozenSeen && music >= FreezeTime) { frozenSeen = true; heal.FreezeAll(); }
        if (!released && music >= ReleaseTime) { released = true; heal.ReleaseFreeze(); heal.SetSinkProgress(0f); }

        foreach (var item in chart)
        {
            if (!item.spawned && music >= item.Spawn)
            {
                item.spawned = true;
                if (item.Answer)
                    heal.BindAnswer(item.id, item.pair, item.text, item.type, item.time, item.approach, item.hold);
                else if (item.Words)
                    heal.SpawnPhrase(item.id, item.text, item.type, item.time, item.approach, item.side, item.hold);
                else heal.SpawnBeat(item.id, item.type, item.time, item.hold);
            }
            if (item.done || !item.spawned) continue;
            // During the authored sinking section, words accumulate and hang
            // instead of resolving. FreezeAll clears its remaining lane notes.
            if (item.part == 2 && item.Words)
            {
                if (music >= FreezeTime) item.done = true;
                continue;
            }
            if (item.part == 2 && music >= FreezeTime) { item.done = true; continue; }
            if (music < item.time) continue;
            if (item.hold > 0d)
            {
                float progress = Mathf.Clamp01((float)((music - item.time) / item.hold));
                heal.SetHoldProgress(item.id, progress);
                if (progress < 1f) continue;
            }

            heal.ResolveHit(item.id);
            item.done = true;
            if (item.Answer) { cutProgress += 1d; continue; }
            if (!string.IsNullOrEmpty(item.pair)) heal.KnockDown(item.pair);
            if (item.id == "D2")
            {
                heal.SetHeartStage(3, true);
                endingStart = music + 1.2d;
            }
        }
        if (cutProgress >= 3d && heal.HeartStage < 1 && released) heal.SetHeartStage(1, true);
        if (cutProgress >= 7d && heal.HeartStage < 2) heal.SetHeartStage(2, true);
        if (!endingStarted && music >= endingStart)
        {
            endingStarted = true;
            heal.PlayEnding(ThankYou);
        }
    }

    // Copied from OSChapter04FinalHealQA's round-11b chart. Keep this preview
    // sequence separate from production data until the real backend is wired.
    sealed class Item
    {
        public string id, text, pair;
        public int type, side, part;
        public double time, hold, approach;
        public bool Words => text != null;
        public bool Answer => part == 3 && Words && pair != null;
        public bool spawned, done;
        public double Spawn => time - (Words ? approach : LaneLead);
    }

    static double At(int bar, double beat) => (bar - 1) * Bar + beat * Beat;
    static double HoldOf(int bars) => bars * Bar - 2 * Beat;
    static double AtBar(double bar) => (bar - 1d) * Bar;
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
    static readonly double FreezeTime = At(20, 3), ReleaseTime = At(23, 0), EndingWord = AtBar(36d);

    static List<Item> BuildChart()
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
        Words("A2", 3, 1, 0, "弄丢了", R, 1);
        Words("A3", 4, 2, 1, "连妈妈的事都记不住", L, 1);
        Words("A4", 6, 1, 0, "保姆", T, 1);
        Words("A5", 7, 1, 0, "替代品", R, 1);
        Words("A6", 8, 1, 2, "记不住", L, 1);
        Words("A7", 9, 2, 1, "我只会叫她主人", R, 1);
        Words("A8", 11, 1, 0, "不划算", T, 1);
        Words("A9", 12, 1, 0, "没心", L, 1);
        Words("A10", 13, 2, 2, "机器", R, 1);

        var back = new[] { ("A2", "弄丢了", 0), ("A3", "连妈妈的事都记不住", 1), ("A4", "保姆", 0), ("A5", "替代品", 0),
            ("A6", "记不住", 2), ("A7", "我只会叫她主人", 1), ("A8", "不划算", 0), ("A9", "没心", 0), ("A10", "机器", 2) };
        var sides = new[] { L, R, T, R, L, T, L, R, T };
        var reached = new[] { .45f, .7f, .55f, .8f, .4f, .65f, .5f, .75f, .6f };
        for (int i = 0; i < back.Length; i++)
        {
            double spawn = At(15, 0) + i * 1.5d;
            double approach = (FreezeTime - spawn) / reached[i];
            items.Add(new Item { id = back[i].Item1, text = back[i].Item2, type = back[i].Item3, side = sides[i], part = 2,
                time = spawn + approach, approach = approach, hold = back[i].Item3 == 1 ? HoldOf(1) : 0d });
        }
        foreach (var (id, bar, text, side, reach) in new[] { ("B1", 17, "我真的是她合格的女儿吗", R, .6f), ("B2", 19, "我连一句妈妈都没叫过她", L, .5f) })
        {
            double spawn = At(bar, 0), approach = (FreezeTime - spawn) / reach;
            items.Add(new Item { id = id, text = text, type = 1, side = side, part = 2,
                time = spawn + approach, approach = approach, hold = HoldOf(2) });
        }
        foreach (var cut in Cuts)
            items.Add(new Item { id = cut.id, text = cut.text, type = cut.type, side = B, part = 3, pair = cut.pair,
                time = AtBar(cut.bar), hold = cut.hold, approach = OSChapter04FinalHealPresentation.AnswerLeadSeconds });
        items.Add(new Item { id = "D2", text = "妈妈", type = 2, side = OSChapter04FinalHealPresentation.SideHeart,
            part = 4, pair = "B2", time = EndingWord, approach = HeartRiseApproach });

        var busy = items.Where(i => i.part != 2).Select(i => (from: i.time - Beat * .5d, to: i.time + i.hold + Beat * .5d)).ToList();
        bool Free(double time) => !busy.Any(b => time > b.from && time < b.to);
        int n = 0;
        void Plain(double time, int type = 0)
        {
            if (Free(time)) items.Add(new Item { id = "n" + n++, time = time, type = type, part = PartOf(time) });
        }
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
}
#endif
