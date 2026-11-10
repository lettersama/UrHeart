using System;
using System.Collections;
using UnityEngine;

public sealed partial class OSPaperPushWindowController
{
    [SerializeField] bool chapter02ShowPreparationTell = true;
    [SerializeField, Min(0f)] float chapter02SlideAcceleration = 150f;
    [SerializeField, Min(0f)] float chapter02SlideMaxSpeed = 48f;
    [SerializeField] float chapter02PawnYOffset = -12f;
    OSPaperPushWindowTilt chapter02Tilt;
    OSWindowUnit chapter02SettingsWindow;
    bool chapter02Paused, chapter02BraceReleased, chapter02ResultPending;
    bool chapter02CueAnimationPending;
    bool chapter02HasFocus = true;
    float chapter02SlideVelocity;

    public event Action<int> Chapter02MatchStarted;
    public event Action<int, bool> Chapter02MatchFinished;
    public event Action<string> Chapter02Cue;
    public int MatchChapter => matchChapter;
    public RectTransform PlayerRect => playerImage != null ? playerImage.rectTransform : null;
    public RectTransform CharacterRect => characterImage != null ? characterImage.rectTransform : null;
    public RectTransform WindowRect => window != null ? window.WindowRect : null;
    public OSPaperPushWindowTilt WindowTilt => chapter02Tilt;
    public bool IsChapter02Bracing => IsChapter02GameActive && matchStage == 2;
    public bool IsChapter02BraceReleased => false;
    public bool IsChapter02GameActive => matchChapter == 2 && phase != MatchPhase.Title && phase != MatchPhase.Finished;
    public bool IsChapter02CueSafe => matchChapter == 2 && !chapter02CueAnimationPending && (phase == MatchPhase.Pushing
        || phase == MatchPhase.Resolving || phase == MatchPhase.Finished || phase == MatchPhase.Title || phase == MatchPhase.Sliding);
    bool CanChapter02Simulate => !chapter02Paused && chapter02HasFocus && !GameTestConsole.BlocksGameplayInput
        && !OSSessionPersistence.IsRestoring && !(chapter02SettingsWindow != null
            && chapter02SettingsWindow.IsOpen && !chapter02SettingsWindow.IsMinimized);
    bool CanBeginChapter02Match
    {
        get
        {
            var save = GameDataManager.Instance?.OSEnergyLoopState;
            if (save == null || chapter02Paused || chapter02ResultPending) return false;
            // An active legacy save may still have its old tilt result marker.
            if (save.chapter02PaperPushTiltWin > 0 && !save.paperPushMatch.active) return false;
            return save.chapter02PaperPushStep >= 2 && save.chapter02PaperPushStep <= 5;
        }
    }

    void OnApplicationFocus(bool focused) => chapter02HasFocus = focused;

    int ResolveChapter02MatchStage(OSPaperPushMatchSaveData saved)
    {
        if (saved != null && saved.active && saved.chapter == 2) return Mathf.Clamp(saved.matchStage, 1, 2);
        return GameDataManager.Instance?.OSEnergyLoopState.chapter02PaperPushStep >= 4 ? 2 : 1;
    }

    public void SetChapter02InteractionPaused(bool paused)
    {
        chapter02Paused = paused;
        if (matchChapter == 2 && phase == MatchPhase.Choosing) SetChoicesEnabled(!paused);
    }

    public void PrepareChapter02Title(int stageNumber)
    {
        if (GameDataManager.Instance?.CurrentChapterIndex != 2) return;
        StopAllCoroutines();chapter02ResultPending = false;chapter02Paused = false;
        matchChapter = 2;matchStage = Mathf.Clamp(stageNumber, 1, 2);
        ShowTitle();
    }

    void BeginChapter02Runtime(bool restoring, OSPaperPushMatchSaveData saved)
    {
        ResetChapter02Tilt();
        if (matchChapter != 2) return;
        chapter02HasFocus = Application.isFocused;
        chapter02SettingsWindow = OS_Handle.Instance != null
            ? OS_Handle.Instance.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>() : null;
        if (chapter02SettingsWindow == null && OS_Handle.Instance != null)
            foreach (var candidate in OS_Handle.Instance.GetComponentsInChildren<OSWindowUnit>(true))
                if (candidate.WindowId.IndexOf("setting", StringComparison.OrdinalIgnoreCase) >= 0)
                { chapter02SettingsWindow = candidate;break; }
        chapter02ResultPending = chapter02CueAnimationPending = chapter02BraceReleased = false;
        chapter02GearRevealed = matchStage == 2 && (observedSave.chapter02PaperPushHintStage > 0
            || observedSave.chapter02PaperPushCueHistory.Contains("m2_hint1") || restoring && saved.phase == 4);
        if (restoring && matchStage == 2)
        {
            saved.chapter02BraceReleased = false;observedSave.chapter02PaperPushTiltWin = 0;
            // Older saves counted pushes only. Every fully resolved round now
            // counts, including defence and draws; an in-flight round does not.
            int completedRounds = Mathf.Max(0, saved.round - (saved.phase == 1 || saved.phase == 2 ? 1 : 0));
            saved.chapter02PushAttempts = Mathf.Min(2, Mathf.Max(saved.chapter02PushAttempts, completedRounds));
            saved.chapter02LastCountedPushRound = Mathf.Max(saved.chapter02LastCountedPushRound, completedRounds);
            if (saved.chapter02PushAttempts >= 2) RequestChapter02GearHint();
        }
        if (!restoring && matchStage == 2)
        {
            observedSave.chapter02PaperPushHintStage = chapter02GearRevealed ? 1 : 0;
            observedSave.chapter02PaperPushTiltWin = 0;
            saved.chapter02PushAttempts = saved.chapter02LastCountedPushRound = 0;
            saved.chapter02TiltDegrees = saved.chapter02SlideVelocity = 0f;
        }
        if (chapter02GearRevealed) saved.chapter02PushAttempts = 2;
        observedSave.chapter02PaperPushStep = matchStage == 1 ? 3 : 5;
        if (matchStage == 2 && window != null)
        {
            chapter02Tilt = window.GetComponent<OSPaperPushWindowTilt>() ?? window.gameObject.AddComponent<OSPaperPushWindowTilt>();
            chapter02Tilt.Configure(window);
        }
        Chapter02MatchStarted?.Invoke(matchStage);
        if (!restoring) Chapter02Cue?.Invoke(matchStage == 1 ? "m1_open" : "m2_hold");
    }

    void ResetChapter02Tilt()
    {
        chapter02Tilt?.ResetTilt();chapter02CueAnimationPending = false;
        if (chapter02Gear != null) chapter02Gear.Hide();
    }

    IEnumerator Chapter02PreparationTell()
    {
        SetChoicesEnabled(false);
        SetPoses(OSPaperPushPose.Straining, PreparationPose(previousPlayerMove));
        yield return Wait(.25f);
        if (phase != MatchPhase.Choosing) yield break;
        SetPoses(OSPaperPushPose.Straining, OSPaperPushPose.Straining);
        SetChoicesEnabled(!chapter02Paused);
    }

    void RequestChapter02GearHint()
    {
        var save = GameDataManager.Instance.OSEnergyLoopState;
        chapter02GearRevealed = true;
        if (save.chapter02PaperPushHintStage > 0) return;
        save.chapter02PaperPushHintStage = 1;
        Chapter02Cue?.Invoke("m2_hint1");
    }

    IEnumerator Chapter02BlockedPush()
    {
        sound.Play(sound.guard, .7f);presentation?.TableKnock();
        SetPoses(OSPaperPushPose.Straining, OSPaperPushPose.Straining);
        SetStates(OSPaperPushActionState.Pushing, OSPaperPushActionState.Pushing);
        if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, Chapter02Text("stuck", "不行，推不动"));
        chapter02CueAnimationPending = true;
        float lastTick = Time.unscaledTime;
        for (float elapsed = 0f; elapsed < .4f;)
        {
            yield return null;elapsed += FrameDelta(ref lastTick);
            characterTilt = Mathf.Round(Mathf.Sin(Mathf.Clamp01(elapsed / .4f) * Mathf.PI * 4f) * 6f);
        }
        characterTilt = 0f;
        chapter02CueAnimationPending = false;
    }

    void Chapter02RoundCompleted()
    {
        if (!IsChapter02Bracing) return;
        var saved = GameDataManager.Instance.OSEnergyLoopState.paperPushMatch;
        // Count each completed round once, irrespective of its outcome or move.
        // Waiting/hopping and a replay of the same saved resolution do not count.
        if (saved.chapter02LastCountedPushRound != round)
        {
            saved.chapter02LastCountedPushRound = round;
            saved.chapter02PushAttempts = Mathf.Min(2, saved.chapter02PushAttempts + 1);
        }
        if (saved.chapter02PushAttempts >= 2) RequestChapter02GearHint();
    }

    void BeginChapter02Sliding()
    {
        if (matchChapter != 2 || matchStage != 2 || phase == MatchPhase.Finished) return;
        var saved = GameDataManager.Instance.OSEnergyLoopState.paperPushMatch;
        if (saved.phase != 4) saved.chapter02TiltDegrees = saved.chapter02SlideVelocity = 0f;
        chapter02SlideVelocity = saved.chapter02SlideVelocity;
        chapter02GearRevealed = true;
        phase = MatchPhase.Sliding;
        hopPlayer = hopCharacter = playerTilt = characterTilt = stamina = 0f;
        playerMotion.Reset();characterMotion.Reset();sound.StopAll();
        choicesRoot?.SetActive(false);
        SetStates(OSPaperPushActionState.Ready, OSPaperPushActionState.Ready);
        SetPoses(OSPaperPushPose.Idle, OSPaperPushPose.Idle);
        SaveMatch(4);
        if (statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, Text("title", "纸偶对推"));
        if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel,
            Chapter02Text("tilt_tip", "按住左上齿轮左右拖动，倾斜窗口，让纸偶滑向桌边。"));
        TickChapter02Gear(false);
        chapter02Gear?.RestoreTilt(saved.chapter02TiltDegrees);
        chapter02Tilt?.RestoreTilt(saved.chapter02TiltDegrees);
    }

    void TickChapter02Rematch()
    {
        if (matchChapter != 2 || matchStage != 2) return;
        bool active = phase == MatchPhase.Sliding && CanSimulate;
        bool canDrag = active && !window.IsOpening && !window.IsClosing;
        TickChapter02Gear(canDrag);
        if (!canDrag || chapter02Gear == null || chapter02Tilt == null) return;
        float delta = Mathf.Min(.05f, Time.unscaledDeltaTime);
        chapter02Tilt.SetHeld(chapter02Gear.IsHeld, chapter02Gear.TiltDegrees);
        chapter02Tilt.Tick(delta, true);
        float angle = chapter02Tilt.TiltDegrees;
        if (Mathf.Abs(angle) < .2f) chapter02SlideVelocity = Mathf.MoveTowards(chapter02SlideVelocity, 0f, 80f * delta);
        else chapter02SlideVelocity = Mathf.Clamp(chapter02SlideVelocity
            - Mathf.Sin(angle * Mathf.Deg2Rad) * chapter02SlideAcceleration * delta,
            -chapter02SlideMaxSpeed, chapter02SlideMaxSpeed);
        float slide = chapter02SlideVelocity * delta;
        playerX += slide;characterX += slide;
        var progress = GameDataManager.Instance.OSEnergyLoopState.paperPushMatch;
        progress.playerX = playerX;progress.characterX = characterX;
        progress.chapter02TiltDegrees = angle;progress.chapter02SlideVelocity = chapter02SlideVelocity;
        CheckOut();
    }

    void FinishChapter02Runtime(bool playerWon)
    {
        if (matchChapter != 2) return;
        chapter02ResultPending = true;chapter02Gear?.Hide();
        var save = GameDataManager.Instance.OSEnergyLoopState;
        if (matchStage == 1) save.chapter02PaperPushMatch1Won = playerWon ? 1 : 2;
        else save.chapter02PaperPushTiltWin = playerWon ? 1 : 2;
        if (matchStage == 2) StoryAchievementProgress.Record(StoryAchievementMilestone.PaperPushGearUsed);
        Chapter02MatchFinished?.Invoke(matchStage, playerWon);
    }

    static OSLocalizedText.Line Chapter02Text(string key, string fallback) =>
        new OSLocalizedText.Line("ui.os.paper_push_ch02." + key, fallback);
}
