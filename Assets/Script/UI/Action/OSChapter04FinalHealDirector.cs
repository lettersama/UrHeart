using System;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The playable final treatment. Owns the score, judgement and durable phase;
/// the Chapter04FinalHeal folder remains a presentation-only consumer.
/// A saved treatment always restarts at its captured entrance health. A cured
/// heart resumes the ending, never the chart or a second reward.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed partial class OSChapter04FinalHealDirector : MonoBehaviour
{
    sealed class LiveNote
    {
        public OSChapter04FinalHealContent.Note data;
        public bool spawned, resolved, holding, shellCracked;
        public double nextTick, crackedAt;
    }

    OSChapter04StoryDirector story;
    OSChapter04StoryState saved;
    OS_Handle os;
    OSWindowUnit settings;
    OSChapter04FinalHealPresentation view;
    readonly List<LiveNote> notes = new List<LiveNote>();
    LiveNote heldNote;
    double musicTime, endingAt;
    float heart, sinkStartHeart, cutProgress, activeSeconds;
    bool running, ending, endingShown, frozen, released, sinkStarted, hasFocus;
    bool paused, returnPending, returnAttempted;
    int lastPressFrame = -1;
    float hitHeal = 1.5f, missDamage = 1f, holdHeal = .25f;

    public bool IsRunning => running;
    public bool IsEnding => running && ending;
    public bool IsPaused => paused;
    public bool IsWaitingForMother { get; private set; }
    public bool RestoredOnBegin { get; private set; }
    public double MusicTime => musicTime;
    public float HeartValue => heart;
    public float CutProgress => cutProgress;
    public int HitCount { get; private set; }
    public int MissCount { get; private set; }
    public int HoldTickCount { get; private set; }
    public int PressCount { get; private set; }
    public float HitWindow => GameDataManager.Instance?.HeartHealData?.HitWindowSeconds ?? .09f;
    public OSChapter04FinalHealContent Content { get; private set; }
    public event Action Healed;
    public event Action EndingCompleted;

#if UNITY_EDITOR
    bool debugClock;
    bool? debugFocus;
    public Func<double, int> DebugRealtimeInput;
    public event Action<string, double, double> DebugNoteHit;
#endif

    void Awake() => hasFocus = Application.isFocused;
    void OnApplicationFocus(bool value) => hasFocus = value;
    void OnDisable() => Stop();
    void OnDestroy() => Stop();

    public bool Begin(OSChapter04StoryDirector owner, bool restoring, bool endingOnly)
    {
        if (owner == null || !owner.IsActive || owner.State == null) return false;
        if (running && story == owner && ReferenceEquals(saved, owner.State)) return true;
        Stop();
        Content = OSChapter04FinalHealContent.Instance;
        if (!Content.Available)
        {
            Debug.LogError("[FinalHeal] " + string.Join("; ", Content.Validate()), this);
            return false;
        }
        story = owner; saved = owner.State; os = GetComponent<OS_Handle>();
        view = OSChapter04FinalHealPresentation.Ensure();
        if (os == null || view == null) return false;
        settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        RestoredOnBegin = restoring;
        if (!saved.finalHealInitialHeartCaptured)
        {
            saved.finalHealInitialHeart = Mathf.Clamp(GameDataManager.Instance.GetOSResourceValue(ResourceType.Heart), 1, 100);
            saved.finalHealInitialHeartCaptured = true;
        }
        var rules = GameDataManager.Instance.HeartHealData;
        if (rules != null && rules.TryGetPuzzle(1, out var first))
        { hitHeal = first.HitHeal; missDamage = first.MissDamage; holdHeal = first.HoldTickHeal; }
        heart = endingOnly ? 100f : saved.finalHealInitialHeart;
        musicTime = activeSeconds = cutProgress = 0;
        endingAt = double.MaxValue;
        ending = endingOnly;
        HitCount = MissCount = HoldTickCount = PressCount = 0;
        lastPressFrame = -1;
        frozen = released = sinkStarted = endingShown = returnPending = returnAttempted = false;
        IsWaitingForMother = false;
        foreach (var note in Content.Notes) notes.Add(new LiveNote { data = note });
        view.ClearAll();
        view.LaneSoundsEnabled = true;
        view.SetMusicClock(() => musicTime);
        view.Pressed += OnPressed;
        view.PostcardCollected += OnPostcardCollected;
        view.EndingFinished += OnEndingFinished;
        running = true;
        view.Begin(restoring || endingOnly);
        view.SetHeartStage(endingOnly ? 3 : StageForHeart(heart), false);
        if (endingOnly)
        {
            os.HeartbeatWaveform?.StopCurrentBeatSounds();
            ApplyCuredResources();
            ShowEnding(true);
        }
        else
        {
            PublishHeart();
            StartTreatmentMusic();
        }
        SetPaused(!Available);
        return view.IsVisible;
    }

    public void Stop()
    {
        running = false;
        if (view != null)
        {
            view.Pressed -= OnPressed;
            view.PostcardCollected -= OnPostcardCollected;
            view.EndingFinished -= OnEndingFinished;
            view.ClearAll();
            view.SetMusicClock(null);
        }
        StopOwnedMusic();
        RestoreSettingsLayer();
        notes.Clear(); heldNote = null;
        view = null; story = null; saved = null; os = null; settings = null;
        paused = ending = endingShown = returnPending = returnAttempted = false;
        IsWaitingForMother = false;
#if UNITY_EDITOR
        debugClock = false;
        DebugRealtimeInput = null;
#endif
    }

    bool Focused
    {
        get
        {
#if UNITY_EDITOR
            if (debugFocus.HasValue) return debugFocus.Value;
#endif
            return hasFocus;
        }
    }
    bool Available => Focused && os != null && os.IsOSVisible
        && !GameTestConsole.BlocksGameplayInput && !OSSessionPersistence.IsRestoring
        && GameSessionFlow.Instance?.IsBusy != true && GameManager.Instance?.IsTransitioning != true
        && !(settings != null && settings.IsOpen && !settings.IsMinimized);

    void Update()
    {
        if (!running) return;
        if (story == null || !story.IsActive || !ReferenceEquals(saved, story.State)
            || os == null || !os.IsOSVisible)
        { Stop(); return; }
        RefreshSettingsLayer();
        SetPaused(!Available);
        if (paused) return;
        if (returnPending)
        {
            bool retryPressed = Keyboard.current?.spaceKey.wasPressedThisFrame == true
                || Mouse.current?.leftButton.wasPressedThisFrame == true;
            if (retryPressed && lastPressFrame != Time.frameCount)
            { lastPressFrame = Time.frameCount; returnAttempted = false; }
            TryReturnToMenu();
            return;
        }
#if UNITY_EDITOR
        if (debugClock) return;
#endif
        bool pressed = Keyboard.current?.spaceKey.wasPressedThisFrame == true
            || Mouse.current?.leftButton.wasPressedThisFrame == true;
        bool held = Keyboard.current?.spaceKey.isPressed == true || Mouse.current?.leftButton.isPressed == true;
        double now = ReadMusicTime();
#if UNITY_EDITOR
        if (DebugRealtimeInput != null)
        {
            int input = DebugRealtimeInput(now);
            pressed = (input & 1) != 0;
            held = (input & 2) != 0;
        }
#endif
        bool takePress = pressed && lastPressFrame != Time.frameCount;
        if (takePress) lastPressFrame = Time.frameCount;
        Advance(now, takePress, held, Time.unscaledDeltaTime);
    }

    void OnPressed(Vector2 position)
    {
        // The frontend reports positions, not mouse buttons. Ignore real
        // right/middle presses; left-button events also work for UI test input.
        if (!running || !Available || lastPressFrame == Time.frameCount
            || Mouse.current?.rightButton.wasPressedThisFrame == true
            || Mouse.current?.middleButton.wasPressedThisFrame == true) return;
        lastPressFrame = Time.frameCount;
        if (returnPending) { returnAttempted = false; TryReturnToMenu(); return; }
        Advance(ReadMusicTime(), true, true, 0f);
    }

    void SetPaused(bool value)
    {
        paused = value;
        view?.SetPaused(value);
        RefreshMusicPause();
    }

    void Advance(double time, bool pressed, bool held, float delta)
    {
        if (!running) return;
        // Cross phase boundaries in order even if the editor stalls or a
        // test advances by several bars. FreezeAll must run before any third-
        // section note is spawned, otherwise it would clear that new lane.
        if (!ending && time > musicTime)
        {
            AdvanceBoundary(Content.SinkStartTime, time, held, ref delta);
            AdvanceBoundary(Content.FreezeTime, time, held, ref delta);
            AdvanceBoundary(Content.ReleaseTime, time, held, ref delta);
        }
        activeSeconds += Mathf.Max(0f, delta);
        if (!IsWaitingForMother) musicTime = Math.Max(musicTime, time);
        else if (!ending)
        {
            // A late first tap releases the remaining note's approach. Keep
            // the waiting audio route, but let that note reach its own line
            // before holding there for the player's second deliberate press.
            var mother = NextMother();
            if (mother != null && mother.data.IsLastMother)
                musicTime = Math.Min(mother.data.HitTime, musicTime + Math.Max(0f, delta));
        }
        if (!ending && !IsWaitingForMother)
        {
            var mother = NextMother();
            if (mother != null && musicTime > mother.data.HitTime + HitWindow)
            {
                double battleTime = musicTime;
                musicTime = mother.data.HitTime;
                IsWaitingForMother = true;
                BeginMotherWait(battleTime);
            }
        }
        view.LaneSoundsEnabled = musicTime < Content.ReleaseTime && !ending;
        if (pressed) { PressCount++; view.PlayPress(); }
        if (ending)
        {
            if (!endingShown && activeSeconds >= endingAt) ShowEnding();
            UpdateMusicEffect();
            return;
        }

        // Spawn every in-flight second-section target before freezing. Setting
        // the clock to the authored freeze beat makes a hitch deterministic.
        foreach (var note in notes)
        {
            if (note.spawned || (note.data.SpawnTime > musicTime && !(note.data.IsFinal && IsWaitingForMother))) continue;
            note.spawned = true;
            var n = note.data;
            // Each mother tap has its own incoming lane note; both taps still
            // consume the same large two-letter phrase.
            if (n.IsLastMother)
            {
                view.SpawnBeat(n.id, OSChapter04FinalHealPresentation.TypeTap, n.HitTime);
                continue;
            }
            string text = Localize(n.textKey, n.text);
            if (n.IsAnswer)
                view.BindAnswer(n.VisualId, n.pairId, text, (int)n.TextBeatType, n.HitTime, n.ApproachDuration, 0d);
            else if (n.IsFrozenTarget)
            {
                // Keep the authored hanging composition while the actual
                // musical beat is judged independently on the lane.
                view.SpawnPhrase(n.VisualId, text, (int)n.TextBeatType, n.VisualHitTime, n.VisualApproachDuration, n.side, 0d);
                view.FitPhraseAtTime(n.VisualId, Content.FreezeTime);
                view.SpawnBeat(n.VisualId, (int)n.TextBeatType, Content.FreezeTime + 3d);
                view.SpawnBeat(n.LaneId, (int)n.BeatType, n.HitTime, n.HoldDuration);
            }
            else if (n.HasText)
                view.SpawnPhrase(n.VisualId, text, (int)n.TextBeatType, n.HitTime, n.ApproachDuration, n.side, n.HoldDuration);
            else view.SpawnBeat(n.VisualId, (int)n.BeatType, n.HitTime, n.HoldDuration);
            if (n.IsFirstMother)
                view.SpawnBeat(n.VisualId, OSChapter04FinalHealPresentation.TypeTap, n.HitTime);
        }

        if (pressed)
        {
            LiveNote best = null;
            double nearest = double.MaxValue;
            foreach (var note in notes)
            {
                var n = note.data;
                if (!note.spawned || note.resolved || note.holding) continue;
                if (n.IsFinal && note != NextMother()) continue;
                bool canHit = note.shellCracked
                    ? musicTime >= note.crackedAt
                        && musicTime <= note.crackedAt + n.rhythm.ReinforcedSecondHitWindow
                    : n.IsFinal && IsWaitingForMother && musicTime >= n.HitTime - HitWindow
                    || n.BeatType == OSHeartHealBeatType.Hold
                        && musicTime >= n.HitTime - HitWindow && musicTime < n.EndTime
                    || n.BeatType != OSHeartHealBeatType.Hold && Math.Abs(musicTime - n.HitTime) <= HitWindow;
                // The old tool treats a waiting second press as zero error.
                double error = note.shellCracked ? 0d : Math.Abs(musicTime - n.HitTime);
                if (canHit && error < nearest) { best = note; nearest = error; }
            }
            if (best != null)
            {
#if UNITY_EDITOR
                DebugNoteHit?.Invoke(best.data.id + (best.shellCracked ? ":second" : ""), musicTime,
                    best.data.HitTime + (best.shellCracked ? .1875d : 0d));
#endif
                if (best.data.BeatType == OSHeartHealBeatType.Hold)
                {
                    if (heldNote != null && heldNote != best) Resolve(heldNote, false);
                    best.holding = true; heldNote = best;
                    // The first treatment awards a tick on the actual press,
                    // then schedules from that press at the authored interval.
                    AwardHoldTick(best);
                    best.nextTick = musicTime + Math.Max(.02d, best.data.HoldFeedbackInterval);
                    view.SetHoldProgress(best.data.LaneId, HoldProgress(best));
                }
                else if (best.data.BeatType == OSHeartHealBeatType.Reinforced
                    && !best.data.IsAnswer && !best.data.IsFinal && !best.shellCracked)
                {
                    // Breaking the shell is feedback only. Healing, BeatHit
                    // and completion belong to the player's second press.
                    if (view.CrackShell(best.data.LaneId))
                    { best.shellCracked = true; best.crackedAt = musicTime; }
                }
                else Resolve(best, true);
            }
        }
        foreach (var note in notes)
        {
            var n = note.data;
            if (!note.spawned || note.resolved || n.IsFinal) continue;
            if (note.holding)
            {
                if (!held && musicTime < n.EndTime) { Resolve(note, false); continue; }
                while (musicTime >= note.nextTick && note.nextTick < n.EndTime)
                {
                    AwardHoldTick(note);
                    note.nextTick += Math.Max(.02d, n.HoldFeedbackInterval);
                }
                view.SetHoldProgress(n.LaneId, HoldProgress(note));
                if (musicTime >= n.EndTime) Resolve(note, true);
            }
            else if (musicTime > (note.shellCracked
                ? note.crackedAt + n.rhythm.ReinforcedSecondHitWindow
                : n.BeatType == OSHeartHealBeatType.Hold ? n.EndTime : n.HitTime + HitWindow))
                Resolve(note, false);
        }
        AdvanceSections();
        UpdateMusicEffect();
    }

    void AdvanceBoundary(double boundary, double target, bool held, ref float delta)
    {
        if (musicTime >= boundary || target <= boundary) return;
        float part = delta * (float)((boundary - musicTime) / (target - musicTime));
        Advance(boundary, false, held, part);
        delta -= part;
    }

    float HoldProgress(LiveNote note) => Mathf.Clamp01((float)((musicTime - note.data.HitTime) / note.data.HoldDuration));

    LiveNote NextMother()
    {
        foreach (var note in notes) if (note.data.IsFinal && !note.resolved) return note;
        return null;
    }

    void AwardHoldTick(LiveNote note)
    {
        HoldTickCount++;
        if (note.data.section == 1) ChangeStruggleHeart(holdHeal);
        PulseMusicEffect(true);
    }

    void Resolve(LiveNote note, bool hit)
    {
        if (note.resolved) return;
        note.resolved = true; note.holding = false;
        if (heldNote == note) heldNote = null;
        var n = note.data;
        if (hit)
        {
            HitCount++;
            if (n.IsFinal) view.ResolveMotherTap(n.VisualId, n.IsLastMother ? n.id : n.VisualId, n.IsLastMother);
            else view.ResolveHit(n.LaneId);
            if (n.IsFirstMother) PlayMotherTapHit();
            if (n.IsAnswer) PlaySlash();
        }
        else { MissCount++; view.ResolveMiss(n.LaneId); }
        PulseMusicEffect(hit);
        if (n.section == 1)
        {
            if (!hit) ChangeStruggleHeart(-missDamage);
            else if (n.BeatType != OSHeartHealBeatType.Hold)
                ChangeStruggleHeart(hitHeal * (n.BeatType == OSHeartHealBeatType.Reinforced ? 2f : 1f));
        }
        else if (n.section == 3)
        {
            cutProgress += hit ? 1f : .5f;
            // Every answer helps, including a missed cut. Full recovery is
            // reserved for the player's final deliberate press on "妈妈".
            heart = Mathf.Min(95f, cutProgress * 9.5f);
            PublishHeart();
            view.SetHeartStage(cutProgress >= 7f ? 2 : cutProgress >= 3f ? 1 : 0, true);
        }
        else if (n.IsLastMother && hit)
        {
            bool waited = IsWaitingForMother;
            ending = true; IsWaitingForMother = false;
            os.HeartbeatWaveform?.StopCurrentBeatSounds();
            CompleteMotherMusic(waited);
            // Durable intent precedes resource callbacks. A save taken by a
            // listener after either HUD change resumes this idempotent ending.
            saved.finalTreatmentStage = OSChapter04FinalTreatmentStage.Ending;
            saved.finalHealSection = 4;
            StoryAchievementProgress.Record(StoryAchievementMilestone.GameComplete);
            ApplyCuredResources();
            view.SetHeartStage(3, true);
            endingAt = activeSeconds + Content.EndingDelay;
            RefreshMusicPause();
            Healed?.Invoke();
        }
    }

    void AdvanceSections()
    {
        if (ending) return;
        if (musicTime < Content.SinkStartTime) { saved.finalHealSection = 1; return; }
        if (!sinkStarted) { sinkStarted = true; sinkStartHeart = heart; }
        if (musicTime < Content.ReleaseTime)
        {
            saved.finalHealSection = 2;
            float t = Mathf.Clamp01((float)((musicTime - Content.SinkStartTime) / (Content.FreezeTime - Content.SinkStartTime)));
            heart = Mathf.Lerp(sinkStartHeart, 0f, t);
            PublishHeart();
            view.SetSinkProgress(t);
            view.SetHeartStage(StageForHeart(heart), true);
        }
        if (!frozen && musicTime >= Content.FreezeTime)
        {
            frozen = true;
            double now = musicTime; musicTime = Content.FreezeTime;
            view.FreezeAll(); musicTime = now;
            heart = 0; PublishHeart(); view.SetHeartStage(0, true);
        }
        if (!released && musicTime >= Content.ReleaseTime)
        { released = true; view.ReleaseFreeze(); view.SetSinkProgress(0f); }
        if (released) saved.finalHealSection = 3;
    }

    void ChangeStruggleHeart(float delta)
    {
        heart = Mathf.Clamp(heart + delta, 1f, 99f);
        PublishHeart(); view.SetHeartStage(StageForHeart(heart), true);
    }
    static int StageForHeart(float value) => value >= 70f ? 2 : value >= 35f ? 1 : 0;

    void PublishHeart() => SetNarrativeResource(ResourceType.Heart, Mathf.RoundToInt(heart));
    static void SetNarrativeResource(ResourceType type, int value)
    {
        var data = GameDataManager.Instance;
        if (data == null || data.GetOSResourceValue(type) == value) return;
        int actual = data.SetOSResourceValue(type, value);
        // This authored scene cannot fail. In particular the sinking zero is
        // a narrative state, not GameResourceSystem's general death condition.
        GameEvents.RaiseResourceChanged(type, actual, OSResourceSaveData.MaxValue);
    }
    void ApplyCuredResources()
    {
        heart = 100f;
        PublishHeart();
        SetNarrativeResource(ResourceType.Fault, 0);
    }

    void ShowEnding(bool restoring = false)
    {
        if (endingShown) return;
        endingShown = true;
        StartEndingMusic();
        var credits = Content.Credits;
        var staff = new List<string>();
        for (int i = 0; i < credits.staffKeys.Length; i++) staff.Add(Localize(credits.staffKeys[i], credits.staffFallback[i]));
        var language = GameDataManager.Instance?.CurrentLanguageState ?? LanguageState.zh_CN;
        bool chinese = language == LanguageState.zh_CN || language == LanguageState.zh_TW;
        string logoPath = chinese && !string.IsNullOrEmpty(credits.chineseLogoResource)
            ? credits.chineseLogoResource : credits.logoResource;
        var logo = string.IsNullOrEmpty(logoPath) ? null : Resources.Load<Texture2D>(logoPath);
        view.SetEndingCredits(logo, staff, credits.studioName, credits.logoPixelSize);
        view.PlayEpilogue(BuildEpilogueScript(), restoring);
    }

    static OSChapter04FinalHealPresentation.EpilogueStep[] BuildEpilogueScript()
    {
        const string key = "chapter04.final_heal.epilogue.";
        return new[]
        {
            OSChapter04FinalHealPresentation.EpilogueStep.Line(Localize(key + "line_01", "......妈妈，是她认可我了吗？")),
            OSChapter04FinalHealPresentation.EpilogueStep.Choice(new[] { Localize(key + "choice_01", "她一直都爱着你。") }),
            OSChapter04FinalHealPresentation.EpilogueStep.Line(Localize(key + "line_02", "爱...吗？")),
            OSChapter04FinalHealPresentation.EpilogueStep.Line(Localize(key + "line_03", "我也爱着她...我多希望她能亲身听见我叫她一声妈妈。")),
            OSChapter04FinalHealPresentation.EpilogueStep.Choice(new[] { Localize(key + "choice_02", "她听见了，只要你记着她，她就一直在你身边。") }),
            OSChapter04FinalHealPresentation.EpilogueStep.Line(Localize(key + "line_04", "我想我大概懂一点了。")),
            OSChapter04FinalHealPresentation.EpilogueStep.Line(Localize(key + "line_05", "谢谢你...没有你陪我把这些记忆一点点修好，我可能一辈子都活在遗憾里...")),
            OSChapter04FinalHealPresentation.EpilogueStep.Choice(new[] { Localize(key + "choice_03", "慢慢来，爱就是这样一点点浸润着你的。") }),
            OSChapter04FinalHealPresentation.EpilogueStep.Line(Localize(key + "line_06", "嗯。"))
        };
    }

    static string Localize(string key, string fallback) => GameDataManager.Instance?.GetLocalizedText(key, fallback) ?? fallback ?? string.Empty;

    void OnPostcardCollected()
    {
        if (!running || !ending || saved == null) return;
        StoryAchievementProgress.Record(StoryAchievementMilestone.MyDaughter);
    }

    void OnEndingFinished()
    {
        if (!running || !ending || returnPending) return;
        saved.finalTreatmentStage = OSChapter04FinalTreatmentStage.Completed;
        returnPending = true;
        EndingCompleted?.Invoke();
    }
    void TryReturnToMenu()
    {
        if (returnAttempted) return;
        returnAttempted = true;
        if (GameSessionFlow.Instance?.ReturnToMainMenu() == true)
        {
            PostGameFarewellService.Current?.NotifyCompletedReturnToMenu();
            BeginMenuMusicFade();
            Stop();
        }
        else
        {
            // The ordinary OS save-error toast is underneath the ending layer.
            // Keep the completed ending and expose an explicit retry here;
            // never leave the session without its successful final save.
            view?.SetMenuReturnRetryVisible(true);
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool DebugSkipToMother()
    {
        if (!running || ending || audioSession == null) return false;
        var mother = notes.Find(note => note.data.IsFirstMother);
        if (mother == null) return false;
        // Leave the full approach plus a short breath before the two real taps.
        double start = Math.Max(Content.ReleaseTime, mother.data.SpawnTime - .75d);
        if (!audioSession.DebugStartBattleAt(start)) return false;
        // Rebuild the ten cut strings and their paired green replies. The final
        // two taps must still shake/drop them and reveal the authored answers.
        float previewTime = Time.unscaledTime - 2f;
        view.SetPresentationClock(() => previewTime);
        view.LaneSoundsEnabled = false;
        try
        {
            musicTime = Content.FreezeTime;
            foreach (var note in notes)
            {
                var n = note.data;
                if (n.IsFrozenTarget)
                {
                    view.SpawnPhrase(n.VisualId, Localize(n.textKey, n.text), (int)n.TextBeatType,
                        n.VisualHitTime, n.VisualApproachDuration, n.side, 0d);
                    view.FitPhraseAtTime(n.VisualId, Content.FreezeTime);
                }
            }
            view.FreezeAll();
            view.ReleaseFreeze();
            foreach (var note in notes)
            {
                var n = note.data;
                if (!n.IsAnswer) continue;
                musicTime = n.SpawnTime;
                view.BindAnswer(n.VisualId, n.pairId, Localize(n.textKey, n.text),
                    (int)n.TextBeatType, n.HitTime, n.ApproachDuration, 0d);
                musicTime = n.HitTime;
                view.ResolveHit(n.LaneId);
            }
            previewTime += 1f;
            view.Render(); // settle the cuts before the shortcut becomes visible
        }
        finally { view.SetPresentationClock(null); }
        foreach (var note in notes)
            if (!note.data.IsFinal) note.spawned = note.resolved = true;
        heldNote = null;
        sinkStarted = frozen = released = true;
        cutProgress = notes.FindAll(note => note.data.IsAnswer).Count;
        heart = Mathf.Min(95f, cutProgress * 9.5f);
        musicTime = start;
        saved.finalHealSection = 3;
        PublishHeart();
        view.SetSinkProgress(0f);
        view.ReleaseFreeze();
        view.SetHeartStage(2, false);
        view.LaneSoundsEnabled = false;
        audioSession.ClearTreatmentEffect();
        view.SetPaused(paused);
        RefreshMusicPause();
        view.Render();
        return true;
    }
#endif

#if UNITY_EDITOR
    // Injection is editor-only. Tests drive the same Advance/Resolve logic;
    // neither a test score nor an automatic hit path ships in the player.
    public void DebugSetFocus(bool value) => debugFocus = value;
    public void DebugReleaseFocus() => debugFocus = null;
    public void DebugSetClock(double time)
    {
        debugClock = true;
        musicTime = Math.Max(0d, time);
        RefreshMusicPause();
    }
    public void DebugReleaseClock()
    {
        if (musicSource != null && musicSource.clip != null)
            musicSource.timeSamples = Mathf.Clamp((int)(musicTime * musicSource.clip.frequency), 0, musicSource.clip.samples - 1);
        debugClock = false; RefreshMusicPause();
    }
    public void DebugStep(double time, bool pressed, bool held)
    {
        debugClock = true; SetPaused(!Available);
        if (paused) return;
        float delta = (float)Math.Max(0d, time - musicTime);
        Advance(time, pressed, held, delta);
    }
#endif
}
