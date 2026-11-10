using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

// Round 8 Play Mode check of the grave grief presentation on the real chapter-4
// screen (console checkpoint 19, isolated save storage): the typewriter rhythm
// per tier on QA test lines, the chat jolt, the screen dither, the red pointer,
// the heartbeat, the TV, the failure warning and every restore. The story's own
// lines are never changed; the test lines live only here.
public static class OSChapter04GriefQA
{
    const string OutputFolder = "Docs/Chapter04Theater/qa_grief";
    const string ShotFolder = "Docs/Chapter04Theater";
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    // QA test lines (the author's lines 3, 10, 11 and 13); not written to story.json.
    const string Line3 = "再然后..就是你知道的那样了，今天我走到大半路就开始下雨...心脏总是咚咚地跳。";
    const string Line10 = "我多想为她多做点什么，我..我好想再抱住她。";
    const string Line11 = "我的脸，我的身体，我的心...";
    const string Line13 = "我好难过..我的心好痛....";
    const string RichLine11 = "我的脸，我的身体，<color=#D8343A>我的心</color>...";
    const string RichLine13 = "我好难过..<color=#D8343A>我的心好痛</color>....";
    // Every eligible middle Han glyph is inside the tag; the two untagged
    // glyphs at each end are excluded by the production typo selection rule.
    const string RichInsideTypo = "前文<color=#D8343A>我的心好痛</color>完了";

    static readonly List<string> checks = new List<string>(), failures = new List<string>(), errors = new List<string>();
    static readonly List<object> observations = new List<object>();
    static GameDataManager data;
    static OS_Handle os;
    static OSChapter04StoryDirector story;
    static OSChapter04GriefPresentation grief;
    static OSChapter04TheaterView view;
    static OSChapter04StoryContent.Beat heldBeat;
    static float heldAutoAdvanceDelay;
    static string heldEntryId;
    static int heldVisit;
    static int confirmed;
    public static string Status { get; private set; } = "idle";

    public static string Start()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameTest.Instance == null
            || GameManager.Instance == null) return "Play Mode initialization required";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        checks.Clear(); failures.Clear(); errors.Clear(); observations.Clear(); confirmed = 0;
        heldBeat = null; heldEntryId = null; heldVisit = -1;
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
        string isolated = Path.GetFullPath(Path.Combine("Temp", "OSChapter04GriefQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(isolated);
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        Application.logMessageReceived += Capture;
        saves.EnterTestSession(); saves.UseIsolatedStorageForTests(isolated); saves.EnterTestSession();
        data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
        data.SetCurrentLanguage(LanguageState.zh_CN);
        if (setup != null) setup.GetType().GetField("isDemo", Members).SetValue(setup, false);
        Application.runInBackground = true; AudioListener.volume = 0f;
        try
        {
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (!work.Peek().MoveNext()) { work.Pop(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
        }
        finally
        {
            if (heldBeat != null) heldBeat.autoAdvanceDelay = heldAutoAdvanceDelay;
        }
        try
        {
            if (grief != null) { grief.FailureWarningConfirmed -= OnConfirmed; grief.ClearAll(); }
            if (view != null && view.IsPreviewing) view.PreviewStop();
            story?.DebugSetFocus(false); story?.ResetPresentation();
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
            note = "Console checkpoint 19 in isolated storage; QA test lines only. The initial authored line is completed and its in-memory auto-advance delay is temporarily held, then restored in finally. No story text, asset or formal save is changed."
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

    static IEnumerator Seconds(float seconds)
    {
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < seconds) yield return null;
    }

    static void Focus()
    {
        foreach (var target in new MonoBehaviour[] { story, os?.CursorRelease })
        {
            var field = target?.GetType().GetField("hasFocus", Members);
            if (field != null && field.FieldType == typeof(bool)) field.SetValue(target, true);
        }
        story?.DebugSetFocus(true);
    }

    static void OnConfirmed() => confirmed++;

    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        string path = Path.Combine(ShotFolder, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 24, 10f);
        Check(File.Exists(path), "full-screen screenshot written: " + name);
    }

    static IEnumerator Run()
    {
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "console checkpoint 19 accepts the jump");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && OS_Handle.Instance != null, 80f);
        os = OS_Handle.Instance; story = os?.Chapter04Story;
        yield return Wait(() => story != null && story.IsAvailable && story.State?.started == true
            && story.GetComponent<OSChapter04ChatView>()?.OwnsLayout == true, 30f);
        var chat = story.GetComponent<OSChapter04ChatView>();
        Check(chat != null && chat.OwnsLayout && data.CurrentChapterIndex == 4, "chapter four is running with the Live chat window");
        heldEntryId = story.State.currentEntryId; heldVisit = story.State.currentVisit;
        Check(heldEntryId == story.Content.FirstEntryId, "the manual grief fixture holds the checkpoint's initial authored entry");
        heldBeat = story.Content.Find(heldEntryId);
        if (heldBeat == null) throw new InvalidOperationException("The current authored beat must exist before the grief fixture is held.");
        heldAutoAdvanceDelay = heldBeat.autoAdvanceDelay;
        heldBeat.autoAdvanceDelay = -1f;
        yield return Wait(() => story.IsAvailable && os.LinkMessageController.IsPresenting
            && os.LinkMessageController.DisplayedEntryId == heldEntryId, 20f);
        if (!story.IsAvailable || !os.LinkMessageController.IsPresenting || os.LinkMessageController.DisplayedEntryId != heldEntryId)
            throw new InvalidOperationException("The initial authored playback did not become ready before grief QA.");
        if (!story.State.currentTextCompleted)
            Check(story.TryCompleteCurrentText(), "complete the real initial line before manual grief presentation checks");
        yield return Wait(HeldLineStable, 20f);
        Check(HeldLineStable(), "initial authored line and completion receipts are stable with automatic continuation held");
        if (!HeldLineStable()) throw new InvalidOperationException("The authored line could not be isolated from manual grief checks.");
        grief = OSChapter04GriefPresentation.Ensure();
        Check(grief != null && OSChapter04GriefPresentation.Current == grief, "the grief presentation lives on the OS handle");
        grief.FailureWarningConfirmed += OnConfirmed;

        yield return Typing();
        Check(HeldLineStable(), "typing fixtures leave the same completed authored entry and visit in place");

        // The TV shows the grave while the completed authored line is held.
        view = os.TVController?.Screen?.GetComponent<OSChapter04TheaterView>();
        Check(view != null, "the real TV theatre view resolves");
        view.PreviewScene(OSChapter04PresentationBridge.GraveBreakdown, true);
        yield return Seconds(.3f);
        var wave = os.HeartbeatWaveform;
        float naturalInterval = wave != null ? wave.NaturalBeatInterval : -1f;

        // ---- tier 1
        Vector2 rootBefore = chat.Root.anchoredPosition;
        grief.SetGriefTier(1, false);
        yield return null;
        Check(grief.Tier == 1 && view.Stage.GriefTier == 1, "tier 1 reaches the TV");
        grief.NotifyHeartCue();
        yield return null; yield return null;
        bool jolted = grief.IsChatShaking && Mathf.Approximately((chat.Root.anchoredPosition - rootBefore).magnitude, 1f);
        yield return Seconds(.25f);
        Check(jolted && chat.Root.anchoredPosition == rootBefore, "a heart cue jolts the chat window by 1 px and it settles back");
        bool pointerUnlocked = grief.OwnsPointer;
        Check(os.CursorRelease != null, "the red pointer exists in chapter four (" + (pointerUnlocked ? "held by the grief" : "not unlocked in this save") + ")");
        Vector2 p0 = os.CursorRelease != null ? os.CursorRelease.CompanionScreenPosition : Vector2.zero;
        var jitter = new HashSet<Vector2>();
        // Tier 1 changes position every 0.09s. A fixed 40-frame sample can
        // finish before two changes on an uncapped Editor; observe real time.
        float jitterStartedAt = Time.unscaledTime;
        int jitterSampleFrames = 0;
        while (Time.unscaledTime - jitterStartedAt < .75f)
        {
            yield return null;
            jitterSampleFrames++;
            if (os.CursorRelease != null) jitter.Add(os.CursorRelease.CompanionScreenPosition);
        }
        observations.Add(new { pointerJitter = new { elapsedSeconds = Time.unscaledTime - jitterStartedAt,
            sampleFrames = jitterSampleFrames, positionCount = jitter.Count } });
        Check(!grief.OwnsPointer || jitter.Count > 2, "tier 1: the pointer trembles in place (" + jitter.Count + " positions)");
        yield return WaitTv(15);
        yield return Shot("58_grief1_screen");

        // ---- tier 2
        grief.SetGriefTier(2, false);
        grief.NotifyLineTyped();
        yield return null;
        Check(grief.DarkLevel == 1, "tier 2: a finished line dims the whole screen one dither step");
        yield return Shot("59_grief2_screen");
        yield return Seconds(1.2f);
        Check(grief.DarkLevel == 0, "tier 2: the screen comes back within about a second");
        yield return Seconds(1.5f);
        Check(wave == null || !Mathf.Approximately(wave.NaturalBeatInterval, naturalInterval), "tier 2: the heartbeat panel turns irregular");
        Vector2 p2 = os.CursorRelease != null ? os.CursorRelease.CompanionScreenPosition : Vector2.zero;
        Check(!grief.OwnsPointer || p2.y < p0.y - .5f, "tier 2: the pointer sinks (" + p0.y.ToString("0") + " -> " + p2.y.ToString("0") + ")");
        Check(view.Stage.GriefAngle == 30 && view.Stage.IsGriefLayerFlat(0), "tier 2 on the TV: folded back 30 degrees, the far layer down");

        // ---- tier 3
        grief.SetGriefTier(3, false, 0);
        grief.NotifyLineTyped(); grief.NotifyLineTyped();
        Check(grief.DarkLevel == 2, "tier 3: each finished line darkens one more step and stays (" + grief.DarkLevel + ")");
        grief.SetGriefTier(3, false, 3);
        grief.NotifyLineTyped();
        bool glitched = false;
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < 5f) { glitched |= grief.IsGlitchVisible; yield return null; }
        Check(glitched, "tier 3: a scan line slips on the chat edge");
        Check(grief.DarkLevel == 3, "tier 3: three lines, three steps darker");
        Check(!grief.OwnsPointer || grief.PointerSlide > .99f, "tier 3: the pointer has slid down beside the Live portrait (" + grief.PointerSlide.ToString("0.00") + ")");
        Check(view.Stage.GriefAngle == 45 && view.Stage.IsGriefLayerFlat(5), "tier 3 on the TV: only her, the stone and the flowers");
        yield return Shot("60_grief3_screen");

        // ---- failure warning
        grief.ShowFailureWarning(false);
        yield return null;
        bool skipped = wave == null || !(bool)typeof(HeartbeatWaveformUI).GetField("beatSfxEnabled", Members).GetValue(wave);
        Check(grief.IsFailureWarningVisible && grief.WarningPanel != null && grief.WarningButton != null, "the failure warning appears");
        Check(skipped, "the heartbeat sound holds one beat as it appears");
        var title = grief.WarningPanel.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Title");
        var body = grief.WarningPanel.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Body");
        Check(title != null && title.color == (Color)OSPalette.Red && title.text == "警告" && body != null
            && body.text == "机体故障：心脏负荷异常" && body.color != (Color)OSPalette.Red,
            "title in red, body and button in ink (" + title?.text + " / " + body?.text + ")");
        Check(grief.WarningPanel.parent.GetComponent<UnityEngine.UI.Image>()?.raycastTarget == true
            && grief.WarningPanel.GetComponentInParent<OSWindowUnit>() == null, "modal: a blocker catches every click; no window frame to close or drag");
        yield return Shot("61_failure_warning");
        yield return Seconds(1.6f);
        Check(wave == null || (bool)typeof(HeartbeatWaveformUI).GetField("beatSfxEnabled", Members).GetValue(wave), "the heartbeat sound resumes after one beat");
        grief.WarningButton.onClick.Invoke();
        yield return null;
        Check(confirmed == 1 && !grief.IsFailureWarningVisible, "the button closes it and raises FailureWarningConfirmed once");
        grief.ShowFailureWarning(true);
        Check(grief.IsFailureWarningVisible, "restoring shows the warning again (no beat skipped)");
        grief.HideFailureWarning();

        // ---- restores
        grief.SetGriefTier(0, false);
        grief.SetGriefTier(3, true, 2);
        int expected = 2 + (story.State.currentTextCompleted ? 1 : 0);
        yield return null;
        Check(grief.DarkLevel == expected && view.Stage.GriefAngle == 45 && view.Stage.IsGriefLayerFlat(5),
            "restore tier 3 (third line): dark step " + grief.DarkLevel + ", TV laid flat at once");
        Check(!grief.OwnsPointer || Mathf.Abs(grief.PointerSlide - 2f / 3f) < .01f, "restore tier 3: the pointer is placed on its slide");
        grief.SetGriefTier(2, true);
        yield return null;
        Check(grief.DarkLevel == 0 && view.Stage.GriefAngle == 30 && view.Stage.IsGriefLayerFlat(0) && !view.Stage.IsGriefLayerFlat(5),
            "restore tier 2: no lasting dark, TV at 30 degrees with the far layer down");
        grief.SetGriefTier(0, false);
        yield return null; yield return null;
        Check(grief.DarkLevel == 0 && !grief.OwnsPointer && !grief.IsGlitchVisible && view.Stage.GriefTier == 0
            && (wave == null || Mathf.Approximately(wave.NaturalBeatInterval, naturalInterval)),
            "tier 0 clears every effect (screen, pointer, scan line, heartbeat, TV)");
        Check(HeldLineStable(), "manual grief effects finish on the same completed authored entry without a natural text-completion event");
        observations.Add(new { pointerUnlocked, naturalInterval, heldEntryId, heldVisit, heldAutoAdvanceDelay });
    }

    static bool HeldLineStable() => story?.State != null && story.State.currentEntryId == heldEntryId
        && story.State.currentVisit == heldVisit && story.State.currentTextCompleted && story.State.currentCueCommitted
        && story.State.currentResourcesCommitted && heldBeat != null && heldBeat.autoAdvanceDelay == -1f;

    static IEnumerator WaitTv(int frames)
    {
        int start = view.Stage.PhaseFrame;
        yield return Wait(() => view.Stage.PhaseFrame >= start + frames, 5f);
    }

    // ================================================================ typewriter

    sealed class Typed
    {
        public readonly List<float> times = new List<float>();
        public readonly List<int> visible = new List<int>();
        public string typo; public int typoAt = -1; public float total; public string final;
        public int typoSourceAt = -1, typoEpisodes;
        public bool sourceIsVisible = true, tagsIntact = true, fixedClock;
        public readonly HashSet<int> redVisibleCounts = new HashSet<int>();
    }

    static IEnumerator Typing()
    {
        var node = new GameObject("GriefQALabel", typeof(RectTransform));
        var host = story.GetComponent<OSChapter04ChatView>().Root;
        node.transform.SetParent(host, false);
        var rect = (RectTransform)node.transform;
        rect.anchoredPosition = new Vector2(-4000f, 0f); rect.sizeDelta = new Vector2(400f, 40f);
        var label = node.AddComponent<TextMeshProUGUI>();
        var template = os.LinkMessageController.DisplayedTextLabel;
        label.font = template.font; label.fontSharedMaterial = template.fontSharedMaterial; label.fontSize = 10f;
        var results = new Dictionary<string, Typed>();
        foreach (var run in new[] { (1, Line3, "t1"), (2, Line10, "t2"), (2, Line10, "t2again"), (2, Line11, "t2line11"), (3, Line13, "t3") })
        {
            var typed = new Typed();
            results[run.Item3] = typed;
            yield return Type(label, run.Item2, run.Item1, typed);
        }
        // A real-time frame sample can see the comma late and the next glyph
        // early, measuring less than the authored 0.35 s interval. Keep the
        // natural playback above, but judge this tight timing boundary on the
        // actual production tween with the same fixed sampling as rich text.
        var timedComma = new Typed();
        yield return Type(label, Line10, 2, timedComma, true);
        var timedLine11 = new Typed();
        yield return Type(label, Line11, 2, timedLine11, true);
        var richRuns = new List<Typed>();
        for (int run = 0; run < 20; run++)
        {
            var typed = new Typed(); richRuns.Add(typed);
            yield return Type(label, RichLine11, 2, typed, true);
            Check(typed.final == RichLine11 && typed.tagsIntact,
                "tagged grave_11 run " + (run + 1) + ": tags stay intact and final source text is restored exactly");
            Check(typed.typoEpisodes <= 1 && typed.sourceIsVisible,
                "tagged grave_11 run " + (run + 1) + ": at most one typo, selected from a rendered glyph source index");
        }
        Check(richRuns.Any(run => run.typoAt >= 0), "tagged grave_11 produces a visible typo in twenty tier-2 runs");
        Check(richRuns.All(run => run.redVisibleCounts.Contains(1) && run.redVisibleCounts.Contains(2)),
            "tagged grave_11 reveals the three red characters one at a time in every run");
        Check(richRuns.All(run => EveryGlyphObserved(run, Line11.Length)),
            "tagged grave_11 fixed-clock samples contain every character boundary without a multi-character reveal jump");
        int[] commaCounts = Enumerable.Range(0, Line11.Length).Where(i => Line11[i] == '，').Select(i => i + 2).ToArray();
        Check(richRuns.All(run => commaCounts.All(count => Gap(run, count) >= .35f)),
            "tagged grave_11 retains both comma holds and the three phrase groups in every run");
        var insideTypo = new Typed();
        yield return Type(label, RichInsideTypo, 2, insideTypo, true);
        int insideStart = RichInsideTypo.IndexOf('>') + 1;
        int insideEnd = RichInsideTypo.IndexOf("</color>", StringComparison.Ordinal);
        Check(insideTypo.typoEpisodes == 1 && insideTypo.typoAt >= 2 && insideTypo.typoAt < 7
            && insideTypo.typoSourceAt >= insideStart && insideTypo.typoSourceAt < insideEnd
            && insideTypo.sourceIsVisible && insideTypo.tagsIntact && insideTypo.final == RichInsideTypo,
            "QA-only tier-2 line selects a visible character inside the red span, keeps both tags intact and restores exact source text");
        observations.Add(new { taggedInteriorTypo = new { insideTypo.typo, insideTypo.typoAt,
            insideTypo.typoSourceAt, insideStart, insideEnd, insideTypo.typoEpisodes, insideTypo.sourceIsVisible,
            insideTypo.tagsIntact, insideTypo.fixedClock, finalMatches = insideTypo.final == RichInsideTypo } });
        var richTier3 = new Typed();
        yield return Type(label, RichLine13, 3, richTier3, true);
        Check(richTier3.final == RichLine13 && richTier3.tagsIntact && richTier3.sourceIsVisible,
            "tagged grave_13 tier 3 restores the exact source without selecting tag characters");
        Check(Enumerable.Range(1, 5).All(richTier3.redVisibleCounts.Contains) && EveryGlyphObserved(richTier3, Line13.Length),
            "tagged grave_13 red phrase and surrounding text reveal at every individual character boundary");
        observations.Add(new { fixedClock = "Comma timing probes and rich-text cases pause the production Tween and advance GotoWithCallbacks in 0.005-second timeline steps (eight per frame); TMP is rebuilt and sampled at every step. Times and total use tween timeline, independent of editor frame stalls. The original five baseline cases still play naturally in real time.",
            richGrave13 = new { richTier3.total, richTier3.fixedClock, visible = richTier3.visible.ToArray(),
                times = richTier3.times.ToArray(), redVisibleCounts = richTier3.redVisibleCounts.OrderBy(v => v).ToArray() },
            richGrave11Runs = richRuns.Select((run, i) => new { run = i + 1, run.total, run.fixedClock,
            run.typo, run.typoAt, run.typoSourceAt, run.typoEpisodes, run.sourceIsVisible, run.tagsIntact,
            finalMatches = run.final == RichLine11, redVisibleCounts = run.redVisibleCounts.OrderBy(v => v).ToArray(),
            commaGaps = commaCounts.Select(count => Gap(run, count)).ToArray() }).ToArray() });
        var localizedRuns = new List<object>();
        var chapterKeys = data.LocalizationData.Entries.Where(entry => entry.Key.Contains("chapter04")
            || entry.Key == "ui.os.heart_heal.title").Select(entry => entry.Key).ToArray();
        var originalLanguage = data.CurrentLanguageState;
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
        {
            data.SetCurrentLanguage(language);
            yield return null; yield return null;
            template = os.LinkMessageController.DisplayedTextLabel;
            label.font = template.font; label.fontSharedMaterial = template.fontSharedMaterial;
            string localized = data.GetLocalizedText("character.chapter04_story.grave_11");
            var typed = new Typed();
            yield return Type(label, localized, 2, typed, true);
            Check(typed.typoEpisodes == 1 && typed.tagsIntact && typed.sourceIsVisible && typed.final == localized,
                language + ": localized grief has one visible typo, retains rich-text tags and restores the exact line");
            char slip = string.IsNullOrEmpty(typed.typo) ? '\0' : typed.typo[0];
            bool scriptMatches = language == LanguageState.zh_CN || language == LanguageState.zh_TW
                ? slip >= '\u4e00' && slip <= '\u9fff'
                : language == LanguageState.ja ? (slip >= '\u3040' && slip <= '\u30ff') || (slip >= '\u4e00' && slip <= '\u9fff')
                : language == LanguageState.ko ? slip >= '\uac00' && slip <= '\ud7af'
                : language == LanguageState.ru ? slip >= '\u0400' && slip <= '\u04ff'
                : (slip >= 'A' && slip <= 'Z') || (slip >= 'a' && slip <= 'z');
            Check(scriptMatches && label.font.HasCharacters(typed.typo ?? "", out uint[] typoMissing, true, true),
                language + ": the grief typo uses the localized writing system and an available glyph");
            var dateLabel = story.GetComponent<OSChapter04ChatView>().ConversationDateText;
            string date = data.GetLocalizedText(OSChapter04ChatView.ConversationDateKey);
            Check(dateLabel != null && dateLabel.text == date && date != OSChapter04ChatView.ConversationDateKey,
                language + ": the actual chat date refreshes from its localization key");
            var missing = new SortedSet<string>();
            var empty = new List<string>();
            foreach (string key in chapterKeys)
            {
                var entry = data.LocalizationData.Entries.First(item => item.Key == key);
                string value = entry.Values.FirstOrDefault(item => item.Language == language)?.Text;
                if (string.IsNullOrWhiteSpace(value)) { empty.Add(key); continue; }
                string plain = System.Text.RegularExpressions.Regex.Replace(value, "<[^>]*>", "");
                plain = new string(plain.Where(c => !char.IsWhiteSpace(c)).ToArray());
                if (!label.font.HasCharacters(plain, out uint[] absent, true, true))
                    foreach (uint c in absent) missing.Add(char.ConvertFromUtf32((int)c));
            }
            Check(empty.Count == 0 && missing.Count == 0,
                language + ": all " + chapterKeys.Length + " chapter-four/ending keys have direct translations and available glyphs");
            localizedRuns.Add(new { language = language.ToString(), date, font = label.font.name,
                typed.typo, typed.typoEpisodes, keysChecked = chapterKeys.Length, empty, missing = missing.ToArray() });
        }
        data.SetCurrentLanguage(originalLanguage);
        yield return null; yield return null;
        observations.Add(new { localizedGriefAndChapterGlyphs = localizedRuns });
        UnityEngine.Object.Destroy(node);

        var t1 = results["t1"];
        Check(t1.final == Line3 && t1.typoAt < 0, "tier 1: the line ends exactly as written, without a typo");
        Check(Gap(t1, Line3.IndexOf("..") + 3) >= .55f, "tier 1: '..' holds longer before the next word (" + Gap(t1, Line3.IndexOf("..") + 3).ToString("0.00") + " s)");
        var t2 = results["t2"];
        Check(t2.final == Line10 && t2.typoAt >= 0, "tier 2: one character mistyped, erased and retyped; the final text is unchanged ("
            + t2.typo + " at " + t2.typoAt + ")");
        Check(t2.typoAt == results["t2again"].typoAt && t2.typo == results["t2again"].typo, "tier 2: the same line slips on the same character every time");
        int commaNextCount = Line10.IndexOf('，') + 2;
        Check(Gap(timedComma, commaNextCount) >= .35f, "tier 2: a comma holds a beat");
        observations.Add(new { tier2CommaTiming = new { fixedGap = Gap(timedComma, commaNextCount),
            realTimeGap = Gap(t2, commaNextCount), timedComma.fixedClock,
            fixedVisible = timedComma.visible.ToArray(), fixedTimes = timedComma.times.ToArray(),
            realTimeVisible = t2.visible.ToArray(), realTimeTimes = t2.times.ToArray() } });
        var t11 = results["t2line11"];
        var commaGaps = commaCounts.Select(count => Gap(timedLine11, count)).ToArray();
        var realTimeCommaGaps = commaCounts.Select(count => Gap(t11, count)).ToArray();
        Check(t11.final == Line11, "line 11 natural playback restores the exact authored text");
        Check(EveryGlyphObserved(timedLine11, Line11.Length),
            "line 11 fixed-clock samples observe every character boundary without a multi-character reveal jump");
        Check(timedLine11.final == Line11 && commaGaps.Length == 2 && commaGaps.All(g => g >= .35f),
            "line 11 falls into three pieces at its commas (" + string.Join(", ", commaGaps.Select(g => g.ToString("0.00"))) + " s)");
        observations.Add(new { line11CommaTiming = new { fixedGaps = commaGaps, realTimeGaps = realTimeCommaGaps,
            timedLine11.fixedClock, fixedTotal = timedLine11.total, realTimeTotal = t11.total,
            fixedVisible = timedLine11.visible.ToArray(), fixedTimes = timedLine11.times.ToArray(),
            realTimeVisible = t11.visible.ToArray(), realTimeTimes = t11.times.ToArray() } });
        var t3 = results["t3"];
        int firstDots = Line13.IndexOf("..");
        float dotGap = Gap(t3, firstDots + 1);
        var letterGaps = new List<float>();
        for (int i = 1; i < firstDots; i++) letterGaps.Add(Gap(t3, i));
        float spread = letterGaps.Count > 1 ? letterGaps.Max() - letterGaps.Min() : 0f;
        Check(t3.final == Line13 && dotGap >= .33f, "tier 3: the dots come out one by one (" + dotGap.ToString("0.00") + " s each)");
        Check(spread > .03f, "tier 3: letters squeeze out at uneven intervals (spread " + spread.ToString("0.000") + " s)");
        observations.Add(new { typing = results.ToDictionary(r => r.Key, r => new { r.Value.total, r.Value.typo, r.Value.typoAt }) });
    }

    /// <summary>Seconds between the reveal of character index-1 and index (index = how many are visible).</summary>
    static float Gap(Typed typed, int count)
    {
        int at = typed.visible.FindIndex(v => v >= count), before = typed.visible.FindIndex(v => v >= count - 1);
        if (at < 0 || before < 0) return 0f;
        return typed.times[at] - typed.times[before];
    }

    static bool EveryGlyphObserved(Typed typed, int total) => Enumerable.Range(0, total + 1).All(typed.visible.Contains)
        && typed.visible.Zip(typed.visible.Skip(1), (before, after) => after - before).All(delta => delta <= 1);

    static IEnumerator Type(TMP_Text label, string text, int tier, Typed typed, bool fixedClock = false)
    {
        float start = Time.unscaledTime;
        var tween = OSLinkMessageController.CreateGriefTypewriter(label, text, tier, 30f).SetUpdate(true);
        typed.fixedClock = fixedClock;
        if (fixedClock) tween.SetAutoKill(false).Pause();
        float timeline = 0f, duration = tween.Duration();
        const float sampleStep = .005f;
        int samplesThisFrame = 0;
        // TMP clears isVisible on hidden characters. Snapshot the fully parsed
        // source map before observing the actual zero-to-full reveal.
        label.maxVisibleCharacters = int.MaxValue;
        label.ForceMeshUpdate(true);
        var sourceIndices = Enumerable.Range(0, label.textInfo.characterCount)
            .Where(i => label.textInfo.characterInfo[i].isVisible)
            .ToDictionary(i => label.textInfo.characterInfo[i].index, i => i);
        label.maxVisibleCharacters = 0;
        label.ForceMeshUpdate(true);
        string tags = string.Join("", Regex.Matches(text, "<[^>]*>").Cast<Match>().Select(match => match.Value));
        bool wasWrong = false;
        int last = -1;
        while (tween.IsActive())
        {
            // Natural baseline sampling is unchanged. Fixed-clock cases also
            // sample the completed endpoint before explicitly killing the tween.
            if (!fixedClock && tween.IsComplete()) break;
            int count = label.maxVisibleCharacters;
            if (fixedClock && count >= 100000) count = label.textInfo.characterCount;
            bool wrong = label.text != text;
            if (wrong && !wasWrong)
            {
                typed.typoEpisodes++;
                int offset = 0;
                while (offset < text.Length && offset < label.text.Length && text[offset] == label.text[offset]) offset++;
                bool insertion = label.text.Length == text.Length + 1 && offset < label.text.Length
                    && label.text.Remove(offset, 1) == text;
                bool replacement = label.text.Length == text.Length && offset < text.Length
                    && label.text.Remove(offset, 1) == text.Remove(offset, 1);
                bool sourceVisible = sourceIndices.TryGetValue(offset, out int visibleIndex);
                typed.sourceIsVisible &= (insertion || replacement) && sourceVisible;
                typed.typoSourceAt = offset; typed.typoAt = sourceVisible ? visibleIndex : -1;
                typed.typo = offset < label.text.Length ? label.text[offset].ToString() : "";
            }
            wasWrong = wrong;
            typed.tagsIntact &= tags == string.Join("", Regex.Matches(label.text, "<[^>]*>").Cast<Match>().Select(match => match.Value));
            label.ForceMeshUpdate(true);
            int redCount = 0;
            for (int i = 0; i < Mathf.Min(count, label.textInfo.characterCount); i++)
            {
                var character = label.textInfo.characterInfo[i];
                Color32 color = character.color;
                if (character.isVisible && color.r == 0xd8 && color.g == 0x34 && color.b == 0x3a) redCount++;
            }
            typed.redVisibleCounts.Add(redCount);
            if (count != last && count < 100000)
            { typed.times.Add(fixedClock ? timeline : Time.unscaledTime - start); typed.visible.Add(count); last = count; }
            if (fixedClock)
            {
                if (tween.IsComplete()) break;
                timeline = Mathf.Min(duration, timeline + sampleStep);
                // This sequence implements reveals as callbacks, so seek with
                // callbacks enabled; ordinary Goto would skip the actual text.
                tween.GotoWithCallbacks(timeline, false);
                if (++samplesThisFrame < 8) continue;
                samplesThisFrame = 0;
            }
            yield return null;
        }
        typed.total = fixedClock ? timeline : Time.unscaledTime - start;
        typed.final = label.text;
        if (fixedClock && tween.IsActive()) tween.Kill(false);
    }
}
