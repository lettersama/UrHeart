using System;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;

/// <summary>Story-owned, no-warp pointer experiment. Art is in native top-left pixels.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed partial class OSCursorExperimentController : MonoBehaviour
{
    public enum ExperimentPhase { Inactive, Intake, RoundIntro, Playing, Collision, Recapture, Completed }
    public const int CollisionAssistThreshold = 15;
    public const int PersistenceReactionThreshold = 6;
    const float HotspotRadius = .8f;
    const float IntakeSeconds = 4f, IntroSeconds = 1.1f, ContactSeconds = .45f, RecaptureSeconds = 3.6f;
    static readonly Vector2 MazeOffset = new Vector2(20, 32);
    static readonly Vector2 CagePoint = new Vector2(121, 101);
    // Interior of the front opening in window-native coordinates. The moving
    // 9 x 9 pointer must fit as a whole, even when the front gate is open.
    static readonly Rect CageInterior = new Rect(98, 87, 44, 33);
    const float CagePointerSize = 9f;
    const float TetherPointerGain = .48f, TetherPointerSpeed = 60f;

    [Header("Native transfer claw sprites")]
    [SerializeField] Sprite transferHandleSprite;
    [SerializeField] Sprite transferHubSprite;
    [SerializeField] Sprite transferLeftFingerSprite;
    [SerializeField] Sprite transferRightFingerSprite;

    OSWindowUnit window;
    RectTransform rootRect;
    OSCursorMazeGeometry.Layout layout;
    ExperimentPhase phase;
    int round;
    float phaseTime;
    bool begun, hasFocus = true, paused, inputWarmup, completionRaised, completionPending;
    bool firstTestStoryHolding, showCageStoryWeight;
    OSMemoryProcessSaveData boundProgress;
    bool trailDirty;
    int cageAudioContactSides;
    // Maze positions are centre hotspots; cage and transfer positions use authored top-left art coordinates.
    Vector2 pointer, transferOrigin, contactPoint, contactRecoil, cagePointer = CagePoint;
    readonly List<Vector2> trail = new List<Vector2>(512);

    public event Action Completed;
    public bool IsActive => begun && phase != ExperimentPhase.Inactive
        && (phase != ExperimentPhase.Completed || completionPending);
    public bool IsBlockingInput => IsActive && !IsSettingsOpen;
    public bool IsCompleted => phase == ExperimentPhase.Completed && !completionPending;
    public bool IsPaused => paused;
    public bool IsAssistancePending => IsActive && round == 3 && Progress?.chapter02CursorExperimentAssisted == true;
    // Saved progress is authoritative even before the director's next Update,
    // so a long frame or resume cannot skip the between-round conversation.
    public bool IsSecondTestStoryHolding => Progress != null
        && Progress.chapter02CursorStoryStep >= OSCursorExperimentStoryDirector.SecondTestQuestionStep
        && Progress.chapter02CursorStoryStep <= OSCursorExperimentStoryDirector.SecondTestRulesStep
        && ((round == 1 && phase == ExperimentPhase.Recapture)
            || (round == 2 && phase == ExperimentPhase.RoundIntro));
    public bool IsThirdTestStoryHolding => Progress != null
        && Progress.chapter02CursorStoryStep >= OSCursorExperimentStoryDirector.ThirdTestReturnStep
        && Progress.chapter02CursorStoryStep <= OSCursorExperimentStoryDirector.ThirdTestRulesStep
        && ((round == 2 && phase == ExperimentPhase.Recapture)
            || (round == 3 && phase == ExperimentPhase.RoundIntro));
    /// <summary>The story owns only the first cage hold and first rule introduction.</summary>
    public bool IsFirstTestStoryHolding
    {
        get => firstTestStoryHolding;
        set
        {
            if (firstTestStoryHolding == value) return;
            firstTestStoryHolding = value;
            inputWarmup = true;
            if (value && round == 1 && phase == ExperimentPhase.Intake)
                phaseTime = Mathf.Min(phaseTime, 2.3f);
            RenderPresentation();
        }
    }
    public bool ShowCageStoryWeight
    {
        get => showCageStoryWeight;
        set
        {
            if (showCageStoryWeight == value) return;
            showCageStoryWeight = value;
            RenderPresentation();
        }
    }
    public ExperimentPhase Phase => phase;
    public int Round => round;
    public float PhaseTime => phaseTime;
    public Vector2 PointerPosition => pointer;
    public Vector2 CagePointerPosition => cagePointer;
    public Rect CageInteriorBounds => CageInterior;
    public bool IsPointerFreeInCage => (phase == ExperimentPhase.Intake && phaseTime >= 1.45f)
        || (phase == ExperimentPhase.Recapture && phaseTime >= 1.65f);
    public Vector2 WeightPosition => weightPoint;
    public OSCursorMazeGeometry.Layout MazeLayout => layout;
    public OSWindowUnit Window => window != null ? window : GetComponent<OSWindowUnit>();
    OSMemoryProcessSaveData CurrentProgress => GameDataManager.Instance != null ? GameDataManager.Instance.OSProcessData.memory : null;
    OSMemoryProcessSaveData Progress => boundProgress;

    void Awake()
    {
        window = GetComponent<OSWindowUnit>();
        rootRect = transform as RectTransform;
        InitializePresentation();
    }

    void OnEnable()
    {
        hasFocus = Application.isFocused;
        inputWarmup = true;
        GameEvents.LanguageChanged += LanguageChanged;
        RefreshLanguage();
    }

    void OnDisable()
    {
        SaveCheckpoint();
        ReleasePointer();
        RestoreSettingsPresentation();
        GameEvents.LanguageChanged -= LanguageChanged;
        paused = true;
    }

    void OnDestroy() { ReleasePointer(); RestoreSettingsPresentation(); }
    void OnApplicationFocus(bool focus)
    {
        hasFocus = focus;
        inputWarmup = true;
        if (!focus) { SaveCheckpoint(); ReleasePointer(); paused = true; }
    }

    /// <summary>Root opens the standalone window and relinquishes the old claw first.</summary>
    public void Begin(Vector2 capturedScreenPoint, bool resume)
    {
        ClearInteriorTransport();
        InitializePresentation();
        ResetFirstTestTransition();
        ResetTether();
        begun = true; completionRaised = false; completionPending = false; paused = false; inputWarmup = true;
        firstTestStoryHolding = false; showCageStoryWeight = false;
        cageAudioContactSides = 0;
        ShowCageGestureHint = false;
        hasFocus = Application.isFocused;
        boundProgress = CurrentProgress;
        var p = Progress;
        int savedRound = p != null ? p.chapter02CursorExperimentRound : 0;
        bool savedHasPosition = p != null && p.chapter02CursorExperimentHasPosition;
        Vector2 savedPosition = p != null ? p.chapter02CursorExperimentPosition : CagePoint;
        if (resume && savedRound >= 4)
        {
            round = 3; layout = OSCursorMazeGeometry.GetLayout(2);
            pointer = layout.Start;
            SetPhase(ExperimentPhase.Completed);
            RenderPresentation();
            ReleasePointer();
            return;
        }
        round = resume && savedRound > 0 ? Mathf.Clamp(savedRound, 1, 3) : 1;
        layout = OSCursorMazeGeometry.GetLayout(round - 1);
        pointer = layout.Start;
        var savedPhase = p != null ? (ExperimentPhase)p.chapter02CursorExperimentPhase : ExperimentPhase.Inactive;
        bool resumeInCage = resume && savedRound > 0 && (savedPhase == ExperimentPhase.Intake
            || (savedPhase == ExperimentPhase.Recapture && round < 3));
        if (resume && savedRound > 0 && savedPhase == ExperimentPhase.Recapture && !resumeInCage)
        {
            round = Mathf.Min(3, round + 1);
            layout = OSCursorMazeGeometry.GetLayout(round - 1);
            pointer = layout.Start;
        }
        if (resume && savedRound > 0 && savedPhase == ExperimentPhase.Playing && savedHasPosition)
        {
            if (Finite(savedPosition) && OSCursorMazeGeometry.IsPointSafe(layout, savedPosition, HotspotRadius)) pointer = savedPosition;
        }
        cagePointer = resumeInCage && savedHasPosition && Finite(savedPosition) ? ClampCagePointer(savedPosition) : CagePoint;
        ResetTrail();
        if (resume && savedPhase == ExperimentPhase.Playing && p != null) RestoreTrail(p.chapter02CursorExperimentTrail);
        transferOrigin = ScreenToNative(capturedScreenPoint);
        if (resume && savedRound > 0)
        {
            // Never replay a lunge from the unrelated physical pointer after loading.
            transferOrigin = CagePoint;
            if (resumeInCage)
            {
                SetPhase(savedPhase);
                // Resume a short closed-cage hold, with the claw already gone.
                // The normal 4.0 / 3.6 second timeline is otherwise unchanged.
                phaseTime = savedPhase == ExperimentPhase.Intake ? 2.3f : 2.45f;
                cagePointer = savedHasPosition && Finite(savedPosition) ? ClampCagePointer(savedPosition) : CagePoint;
            }
            else SetPhase(savedPhase == ExperimentPhase.Playing ? ExperimentPhase.Playing : ExperimentPhase.RoundIntro);
        }
        else SetPhase(ExperimentPhase.Intake);
        TakePointer();
        RenderPresentation();
        SaveCheckpoint();
        ResolveSettingsPause();
    }

    /// <summary>Reveal the first maze and small tether while its rule message is being read.</summary>
    public void OpenFirstTestForStory()
    {
        if (!begun || round != 1 || phase != ExperimentPhase.Intake || !IsPointerFreeInCage) return;
        firstTestStoryHolding = true;
        StartFirstTestTransition();
    }

    /// <summary>Hand control to the first test without applying a buffered mouse delta.</summary>
    public void ReleaseFirstTestForStory()
    {
        firstTestStoryHolding = false; showCageStoryWeight = false;
        inputWarmup = true;
        if (!begun || round != 1 || phase != ExperimentPhase.RoundIntro) return;
        if (firstTestTransitionActive)
        {
            firstTestReleasePending = true;
            RenderPresentation();
            return;
        }
        SetPhase(ExperimentPhase.Playing);
        RenderPresentation();
    }

    public void OpenSecondTestForStory()
    {
        if (!begun || round != 1 || phase != ExperimentPhase.Recapture || phaseTime < 2.45f) return;
        round = 2;
        layout = OSCursorMazeGeometry.GetLayout(1);
        showCageStoryWeight = false;
        ShowCageGestureHint = false;
        ResetTether();
        // Reuse the authored front-door / maze reveal, without the first round's ball.
        StartFirstTestTransition();
    }

    public void ReleaseSecondTestForStory()
    {
        if (!begun || round != 2 || phase != ExperimentPhase.RoundIntro) return;
        inputWarmup = true;
        if (firstTestTransitionActive) firstTestReleasePending = true;
        else SetPhase(ExperimentPhase.Playing);
        RenderPresentation();
    }

    public void OpenThirdTestForStory()
    {
        if (!begun || round != 2 || phase != ExperimentPhase.Recapture || phaseTime < 2.45f) return;
        round = 3;
        layout = OSCursorMazeGeometry.GetLayout(2);
        showCageStoryWeight = false;
        ShowCageGestureHint = false;
        ResetTether();
        StartFirstTestTransition();
    }

    public void ReleaseThirdTestForStory()
    {
        if (!begun || round != 3 || phase != ExperimentPhase.RoundIntro) return;
        inputWarmup = true;
        if (firstTestTransitionActive) firstTestReleasePending = true;
        else SetPhase(ExperimentPhase.Playing);
        RenderPresentation();
    }

    public void ResetPresentation()
    {
        ClearInteriorTransport();
        ReleasePointer();
        RestoreSettingsPresentation();
        ResetFirstTestTransition();
        ResetTether();
        begun = false; round = 0; phase = ExperimentPhase.Inactive; phaseTime = 0;
        paused = false; completionRaised = false; completionPending = false; trail.Clear();
        firstTestStoryHolding = false; showCageStoryWeight = false;
        ShowCageGestureHint = false;
        boundProgress = null;
        if (dynamicRoot != null) dynamicRoot.gameObject.SetActive(false);
        if (transferRoot != null) transferRoot.gameObject.SetActive(false);
        if (contactFlash != null) contactFlash.gameObject.SetActive(false);
        SetShot(-1);
    }

    void Update()
    {
        if (!IsActive) return;
        if (!ReferenceEquals(boundProgress, CurrentProgress)) { ResetPresentation(); return; }
        TickSettingsPause();
        bool available = hasFocus && !GameTestConsole.BlocksGameplayInput && !OSSessionPersistence.IsRestoring
            && OS_Handle.Instance != null && OS_Handle.Instance.IsOSVisible
            && !OS_Handle.Instance.IsSystemInputLockedWithoutCursorExperiment
            && Window.IsOpen && !Window.IsOpening && !Window.IsClosing && !Window.IsMinimized && !IsSettingsOpen;
        if (!available)
        {
            if (!paused) { SaveCheckpoint(); ReleasePointer(); }
            paused = true; inputWarmup = true;
            return;
        }
        if (!TakePointer()) { paused = true; inputWarmup = true; return; }
        paused = false;
        if (IsInteriorTransportActive || OS_Handle.Instance?.CursorExperimentInterior?.IsScreenObscured == true)
        {
            inputWarmup = true;
            RenderPresentation();
            return;
        }
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        Vector2 nativeInput = Vector2.zero;
        if (!inputWarmup) nativeInput = RawDeltaToNative(CursorHandle.Instance.RawMouseDelta);
        inputWarmup = false;
        Tick(dt, nativeInput);
        RenderPresentation();
    }

    void LateUpdate()
    {
        if (!IsActive || paused) return;
        PresentVirtualPointer();
    }

    bool TakePointer()
    {
        var cursor = CursorHandle.Instance;
        if (cursor == null || (!cursor.IsScriptControlledBy(this) && !cursor.AcquireScriptControlWithoutWarp(this))) return false;
        cursor.SetScriptCaptured(this, true);
        // The local native sprite gives the collision hotspot a consistent visual size.
        // CursorHandle still owns the real UI raycast position and suppresses other apps.
        cursor.SetScriptAppearance(this, 1f, new Color(1f, 1f, 1f, 0f));
        cursor.SetScriptPressed(this, false);
        cursor.SetScriptRotation(this, 0f);
        return true;
    }

    void ReleasePointer() => CursorHandle.Instance?.ReleaseScriptControlWithoutWarp(this);

    void Tick(float dt, Vector2 rawNativeDelta)
    {
        // Also applies to the isolated editor driver. A screen pan is not
        // elapsed cage time and cannot release the mouse or start a test.
        if (IsInteriorTransportActive || OS_Handle.Instance?.CursorExperimentInterior?.IsScreenObscured == true) return;
        // The story owns the apology and its countdown while the pointer stays captured.
        if (IsAssistancePending) return;
        float previousTime = phaseTime;
        phaseTime += dt;
        TickTransferAudio(previousTime, phaseTime);
        switch (phase)
        {
            case ExperimentPhase.Intake:
                if (IsPointerFreeInCage) MoveCagePointer(rawNativeDelta, dt);
                if (firstTestStoryHolding && round == 1) phaseTime = Mathf.Min(phaseTime, 2.3f);
                else if (phaseTime >= IntakeSeconds)
                {
                    Chapter02Audio.Play("Chapter02_CageDoor", .6f, 1.12f);
                    SetPhase(ExperimentPhase.RoundIntro);
                }
                break;
            case ExperimentPhase.RoundIntro:
                if (firstTestTransitionActive) TickFirstTestTransition(dt);
                else if ((firstTestStoryHolding && round == 1) || IsSecondTestStoryHolding || IsThirdTestStoryHolding)
                    phaseTime = Mathf.Min(phaseTime, IntroSeconds);
                else if (phaseTime >= IntroSeconds) SetPhase(ExperimentPhase.Playing);
                break;
            case ExperimentPhase.Playing:
                // Also catches an older save that already exhausted its retries.
                if (TryBeginAssistance()) break;
                MovePointer(rawNativeDelta, dt);
                break;
            case ExperimentPhase.Collision:
                if (phaseTime >= ContactSeconds)
                {
                    if (TryBeginAssistance()) break;
                    Chapter02Audio.Play("Chapter02_CursorReset", .48f, 1f, .3f);
                    pointer = layout.Start; ResetTrail(); ResetTether(); SetPhase(ExperimentPhase.Playing);
                }
                break;
            case ExperimentPhase.Recapture:
                if (IsPointerFreeInCage) MoveCagePointer(rawNativeDelta, dt);
                if (IsSecondTestStoryHolding || IsThirdTestStoryHolding) phaseTime = Mathf.Min(phaseTime, 2.45f);
                else if (phaseTime >= RecaptureSeconds)
                {
                    round++;
                    layout = OSCursorMazeGeometry.GetLayout(round - 1);
                    Chapter02Audio.Play("Chapter02_CageDoor", .6f, 1.12f);
                    pointer = layout.Start; ResetTrail(); SetPhase(ExperimentPhase.RoundIntro);
                }
                break;
            case ExperimentPhase.Completed:
                if (completionPending && phaseTime >= 1.2f) CompleteAndRelease();
                break;
        }
        TickTether(dt);
        SaveCheckpoint();
    }

    void MovePointer(Vector2 rawNativeDelta, float dt)
    {
        if (dt <= 0f || !Finite(rawNativeDelta)) return;
        // The second test has an external downward field, not a heavy tether.
        // Keep ordinary pointer response and add only the continuous pull.
        float gain = round == 3 ? .62f : TetherPointerGain;
        float speed = round == 3 ? 66f : TetherPointerSpeed;
        Vector2 movement = rawNativeDelta * gain * (round == 3 ? -1f : 1f);
        movement = Vector2.ClampMagnitude(movement, speed * dt);
        // Apply the field after input limiting, so fast upward input still has
        // to overcome the pull instead of clamping it away.
        if (round == 2) movement.y += 4.725f * dt;
        if (movement.sqrMagnitude < .000001f) return;
        Vector2 next = pointer + movement;
        if (!OSCursorMazeGeometry.TryMove(layout, pointer, next, HotspotRadius, out Vector2 hit))
        {
            pointer = hit; contactPoint = hit;
            contactRecoil = movement.sqrMagnitude > .0001f ? -movement.normalized * 2f : new Vector2(-2, 0);
            if (Progress != null)
            {
                Progress.chapter02CursorExperimentFailures++;
                if (round == 3) Progress.chapter02CursorExperimentThirdFailures++;
            }
            Chapter02Audio.Play("Chapter02_CursorImpact", .78f, 1f, .28f);
            SetPhase(ExperimentPhase.Collision);
            if (round == 3 && Progress?.chapter02CursorExperimentThirdFailures == PersistenceReactionThreshold)
                OS_Handle.Instance?.CursorExperimentStory?.BeginPersistenceReaction();
            return;
        }
        pointer = next;
        RecordTrail();
        if (!layout.Exit.Contains(pointer)) return;
        if (round == 3) Finish();
        else
        {
            transferOrigin = MazePointerTopLeft(pointer);
            Chapter02Audio.Play("Chapter02_ClawTurn", .5f, .86f);
            SetPhase(ExperimentPhase.Recapture);
        }
    }

    void MoveCagePointer(Vector2 rawNativeDelta, float dt)
    {
        if (dt <= 0f || !Finite(rawNativeDelta)) return;
        // Ordinary directions in the cage; reversed input belongs only to the
        // third maze. Clamp the front-end sprite, never the hardware pointer.
        // Attaching the ball gives the first maze's response. Until the player
        // confirms the next line, the cage remains a place to feel that weight.
        bool tethered = round == 1 && showCageStoryWeight;
        Vector2 movement = tethered
            ? Vector2.ClampMagnitude(rawNativeDelta * TetherPointerGain, TetherPointerSpeed * dt)
            : rawNativeDelta;
        Vector2 desired = cagePointer + movement;
        Vector2 next = ClampCagePointer(desired);
        int hitSides = (desired.x < next.x ? 1 : desired.x > next.x ? 2 : 0)
            | (desired.y < next.y ? 4 : desired.y > next.y ? 8 : 0);
        if ((hitSides & ~cageAudioContactSides) != 0 && movement.sqrMagnitude > 1f)
            Chapter02Audio.Play("Chapter02_CursorImpact", .3f, .78f, .35f);
        // Keep a resting edge latched. Moving along it does not chatter, and
        // moving away rearms only that edge for the next intentional bump.
        cageAudioContactSides = (next.x <= CageInterior.xMin ? 1 : next.x >= CageInterior.xMax - CagePointerSize ? 2 : 0)
            | (next.y <= CageInterior.yMin ? 4 : next.y >= CageInterior.yMax - CagePointerSize ? 8 : 0);
        cagePointer = next;
    }

    void TickTransferAudio(float before, float after)
    {
        // Timeline crossings live in Tick, never in RenderPresentation: language
        // refresh, layout previews and restored poses cannot replay an action.
        bool intake = phase == ExperimentPhase.Intake;
        if (!intake && phase != ExperimentPhase.Recapture) return;
        if (!intake && before < .4f && after >= .4f)
            Chapter02Audio.Play("Chapter02_CursorCapture", .72f, 1.05f);
        float openAt = intake ? .03f : .65f;
        float releaseAt = intake ? 1.45f : 1.65f;
        float closeAt = intake ? 2.25f : 2.45f;
        if (before < openAt && after >= openAt)
            Chapter02Audio.Play("Chapter02_CageDoor", .44f, 1.1f);
        if (before < releaseAt && after >= releaseAt)
            Chapter02Audio.Play("Chapter02_ClawGrip", .46f, 1.2f);
        if (before < closeAt && after >= closeAt)
            Chapter02Audio.Play("Chapter02_CageDoor", .65f, .9f);
    }

    static Vector2 ClampCagePointer(Vector2 position) => new Vector2(
        Mathf.Clamp(position.x, CageInterior.xMin, CageInterior.xMax - CagePointerSize),
        Mathf.Clamp(position.y, CageInterior.yMin, CageInterior.yMax - CagePointerSize));

    bool TryBeginAssistance()
    {
        if (round != 3 || Progress == null || Progress.chapter02CursorExperimentThirdFailures < CollisionAssistThreshold) return false;
        Progress.chapter02CursorExperimentAssisted = true;
        SetPhase(ExperimentPhase.Playing);
        OS_Handle.Instance?.CursorExperimentStory?.BeginAssistance();
        return true;
    }

    public void CompleteAssistance()
    {
        if (!IsAssistancePending || Progress.chapter02CursorStoryElapsed < OSCursorExperimentStoryDirector.AssistanceWaitSeconds) return;
        Finish();
    }

    void Finish()
    {
        Chapter02Audio.Play("Chapter02_CursorRelease", .86f);
        // Escape releases input in this same frame. The discarded window is
        // presentation only and must never retain the pointer during its fall.
        completionPending = false;
        SetPhase(ExperimentPhase.Completed);
        SaveCheckpoint();
        RenderPresentation();
        CompleteAndRelease();
    }

    void CompleteAndRelease()
    {
        completionPending = false;
        ReleasePointer();
        if (!completionRaised)
        {
            completionRaised = true;
            StoryAchievementProgress.Record(StoryAchievementMilestone.CursorTestsComplete);
            Completed?.Invoke();
        }
    }

    void SetPhase(ExperimentPhase next)
    {
        phase = next; phaseTime = 0f; inputWarmup = true;
        if (next == ExperimentPhase.Recapture && round == 1 && Progress != null
            && Progress.chapter02CursorStoryStep < OSCursorExperimentStoryDirector.SecondTestQuestionStep)
        {
            Progress.chapter02CursorStoryStep = OSCursorExperimentStoryDirector.SecondTestQuestionStep;
            Progress.chapter02CursorStorySecondTestAnswer = 0;
        }
        if (next == ExperimentPhase.Recapture && round == 2 && Progress != null
            && Progress.chapter02CursorStoryStep < OSCursorExperimentStoryDirector.ThirdTestReturnStep)
        {
            Progress.chapter02CursorStoryStep = OSCursorExperimentStoryDirector.ThirdTestReturnStep;
            Progress.chapter02CursorStoryThirdTestAnswer = 0;
        }
        if (next == ExperimentPhase.Intake || next == ExperimentPhase.Recapture) cagePointer = CagePoint;
        RefreshLanguage();
        SaveCheckpoint();
    }

    void SaveCheckpoint()
    {
        if (!begun || phase == ExperimentPhase.Inactive) return;
        var p = Progress;
        if (p == null || !ReferenceEquals(p, CurrentProgress)) return;
        p.chapter02CursorExperimentRound = phase == ExperimentPhase.Completed ? 4 : round;
        p.chapter02CursorExperimentPhase = (int)phase;
        p.chapter02CursorExperimentHasPosition = phase == ExperimentPhase.Playing || IsPointerFreeInCage;
        p.chapter02CursorExperimentPosition = IsPointerFreeInCage ? cagePointer : pointer;
        if (trailDirty)
        {
            p.chapter02CursorExperimentTrail = trail.ToArray();
            trailDirty = false;
        }
    }

    void ResetTrail()
    {
        trail.Clear();
        // Retain the old entry seed and movement trail for save compatibility.
        // The visible ball and chain are updated independently by Tether now.
        if (layout != null && Vector2.Distance(pointer, layout.Start) < .1f && round < 3)
            trail.Add(pointer + new Vector2(0, -7));
        trail.Add(pointer);
        weightPoint = pointer;
        trailDirty = true;
    }

    void RecordTrail()
    {
        if (trail.Count == 0 || Vector2.Distance(trail[trail.Count - 1], pointer) >= .25f)
        { trail.Add(pointer); trailDirty = true; }
        // Keep the legacy saved trail bounded, including after repeated retries.
        float distance = 0f;
        for (int i = trail.Count - 1; i > 0; i--)
        {
            distance += Vector2.Distance(trail[i], trail[i - 1]);
            if (distance > 70f && i > 1) { trail.RemoveRange(0, i - 1); break; }
        }
    }

    void RestoreTrail(Vector2[] saved)
    {
        if (saved == null || saved.Length < 1 || saved.Length > 512 || round >= 3) return;
        float distance = 0f;
        Vector2 entrySeed = layout.Start + new Vector2(0, -7);
        for (int i = 0; i < saved.Length; i++)
        {
            if (!Finite(saved[i])) return;
            bool seed = i == 0 && saved.Length > 1 && Vector2.Distance(saved[i], entrySeed) < .01f
                && Vector2.Distance(saved[1], layout.Start) < .01f;
            if (!seed && !OSCursorMazeGeometry.IsPointSafe(layout, saved[i], HotspotRadius)) return;
            if (i == 0) continue;
            distance += Vector2.Distance(saved[i - 1], saved[i]);
            bool fromSeed = i == 1 && Vector2.Distance(saved[0], entrySeed) < .01f
                && Vector2.Distance(saved[1], layout.Start) < .01f;
            if (!fromSeed && !OSCursorMazeGeometry.TryMove(layout, saved[i - 1], saved[i], HotspotRadius, out _)) return;
        }
        if (distance > 73f || Vector2.Distance(saved[saved.Length - 1], pointer) > .5f) return;
        trail.Clear(); trail.AddRange(saved); trailDirty = true;
    }

    Vector2 TrailPoint(float distance)
    {
        Vector2 end = pointer;
        for (int i = trail.Count - 1; i >= 0; i--)
        {
            float length = Vector2.Distance(end, trail[i]);
            if (length >= distance && length > .0001f) return Vector2.Lerp(end, trail[i], distance / length);
            distance -= length; end = trail[i];
        }
        return end;
    }

    static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);

#if UNITY_EDITOR
    /// <summary>Runs the real state machine/movement without OS input for isolated regression checks.</summary>
    public void DebugStep(Vector2 nativeRawDelta, float seconds)
    {
        if (!IsActive || seconds <= 0f) return;
        float remaining = seconds;
        while (remaining > .00001f && IsActive)
        {
            float dt = Mathf.Min(.02f, remaining);
            Tick(dt, nativeRawDelta * (dt / seconds));
            remaining -= dt;
        }
        RenderPresentation();
    }
#endif
}
