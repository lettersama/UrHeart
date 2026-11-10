using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSHeartHealToolWindowController : MonoBehaviour
{
    const int NoTreatmentIndex = 0;
    const int MinTreatmentIndex = 1;
    const int MaxTreatmentIndex = 4;
    const int MaxHeartState = 100;
    const int InvalidBatterySnapshot = -1;
    const int FirstTreatmentStateHintThreshold = 65;
    const float FinishTransitionDuration = 0.2f;
    // How far the chart clock may drift from the track before it is pulled back.
    const double MusicClockTolerance = 0.02;
    const float BeatTipMoveDuration = 0.4f;
    const float BeatTipFadeDelay = 0.1f;
    const float BeatTipFadeDuration = 0.3f;
    const float BeatTipDistance = 14f;
    const float HitStateTransferSquareScale = 0.75f;
    const float MissStateBurstSquareScale = 1f;
    const float HitStateTransferDurationScale = 1.4f;
    const float HitStateScatterFraction = 0.32f;
    const string StateLiquidShaderName = "UI/HeartHealStateLiquid";
    const string WaitLinkLocalizationKey =
        "ui.os.heart_heal.wait_live_reply";
    const string WaitLinkFallbackText = "等待 Live 回复";
    const string WindowSaveId = "WindowUnit_HeartHealTool";
    const string SuccessContentStateId = "heart_heal.success";
    const string FailureContentStateId = "heart_heal.failure";

    static readonly int StateLiquidFillAmountId =
        Shader.PropertyToID("_FillAmount");
    static readonly int StateLiquidSpriteUvRectId =
        Shader.PropertyToID("_SpriteUvRect");
    static readonly int StateLiquidWaveTimeId =
        Shader.PropertyToID("_WaveTime");

    [Header("State")]
    [SerializeField] Image stateActive;
    [SerializeField] RectTransform stateTip;
    [SerializeField] float stateTipMinX = -40.07f;
    [SerializeField] float stateTipMaxX = 40.07f;
    [SerializeField, Min(0f)] float stateChangeDuration = 0.18f;
    [SerializeField] Color unstableStateColor =
        new Color32(0x8c, 0x17, 0x14, 0xff);
    [SerializeField] Color stableStateColor =
        new Color32(0x4e, 0xa8, 0x3e, 0xff);

    [Header("Window State")]
    [SerializeField] CanvasGroup heartDetailGroup;
    [SerializeField] CanvasGroup playPartGroup;
    [SerializeField] OSHeartHealFinishPart finishPart;
    [SerializeField] TMP_Text indexZeroTip;
    [SerializeField] OSHeartHealStartButton startButton;
    [SerializeField] OSHeartHealClickButton clickButton;
    [SerializeField, Min(0.1f)] float indexZeroTipInterval = 0.45f;

    [Header("First Treatment Tutorial")]
    [SerializeField] GameObject tutorialDimmer;
    [SerializeField] TMP_Text waitLink;
    [SerializeField] OSLocalizedText waitLinkLocalizedText;
    [SerializeField, Min(0.05f)] float waitLinkDotInterval = 0.3f;

    [Header("Resource Cable")]
    [SerializeField] OSUICableConnectionGroup playPartPointGroup;
    [SerializeField] RectTransform playPartEnergyPoint;

    [Header("Beat Area")]
    [SerializeField] RectTransform beatPool;
    [SerializeField] Image beatLine;
    [SerializeField] OSHeartHealBeatUnit[] beatUnits =
        new OSHeartHealBeatUnit[0];
    [SerializeField] float spawnX = 120f;
    [SerializeField] float endX = -120f;
    [SerializeField] float hitMinX = -89f;
    [SerializeField] float hitMaxX = -79f;
    [SerializeField, Min(0f)] float linePulseDuration = 0.18f;
    [SerializeField] Color electricColor =
        new Color(0.45f, 0.95f, 1f, 1f);
    [SerializeField] Color missColor =
        new Color32(0xb9, 0x20, 0xf0, 0xff);

    [Header("Beat Feedback")]
    [SerializeField] RectTransform beatTipPool;
    [SerializeField] Sprite damageTipSprite;
    [SerializeField] Sprite healTipSprite;
    [SerializeField] RectTransform stateTransferPool;
    [SerializeField] Color hitStateTransferColor =
        new Color32(0x28, 0x81, 0x28, 0xff);
    [SerializeField] Color missStateTransferColor =
        new Color32(0xb9, 0x20, 0xf0, 0xff);
    [SerializeField, Min(0.05f)] float stateTransferDuration = 0.3f;
    [SerializeField, Min(0.01f)] float stateTransferFadeDuration = 0.1f;

    [Header("Treatment Audio")]
    [SerializeField, Min(0f)] float treatmentMusicFade = 0.25f;
    [SerializeField, Min(0f)] float treatmentMusicFadeOut = 0.2f;
    [SerializeField] string treatmentEcgSfxKey = "ECG_Beep";
    [SerializeField, Min(0f)] float treatmentEcgFadeOut = 0.1f;
    [SerializeField] string treatmentBeatSfxKey = "HeartBeat_ForHeal";
    [SerializeField] string treatmentShockSfxKey = "BeatClick";
    [SerializeField] string treatmentTapHitSfxKey = "BeatHit";
    [SerializeField] string treatmentBeatFailSfxKey = "BeatFail";
    [SerializeField] string treatmentHoldLoopSfxKey = "BeatLoop";
    [SerializeField] string treatmentHoldLoopEndSfxKey = "BeatLoopEnd";
    [SerializeField, Range(0.1f, 3f)] float treatmentHitSfxPitch = 1.1f;
    [SerializeField, Range(0.1f, 3f)] float treatmentMissSfxPitch = 0.9f;
    [SerializeField] string treatmentEndSfxKey = "HeartHealTool_End";
    [SerializeField] string treatmentSuccessSfxKey =
        "HeartHealTool_SuccessSFX";
    [SerializeField] string treatmentFailSfxKey =
        "HeartHealTool_FailSFX";
    [SerializeField, Min(0f)] float treatmentResultDelay = 2f;
    [SerializeField, Min(0.1f)] float treatmentEndDuration = 3.5f;
    [SerializeField, Range(10f, 22000f)]
    float unstableMusicCutoff = 1600f;
    [SerializeField, Range(0f, 1f)]
    float unstableMusicDistortion = 0.3f;
    [SerializeField, Range(10f, 22000f)]
    float stableMusicCutoff = 7200f;
    [SerializeField, Range(0f, 1f)]
    float stableMusicDistortion = 0.05f;
    [SerializeField, Range(10f, 22000f)]
    float hitMusicCutoff = 12000f;
    [SerializeField, Range(0f, 1f)]
    float hitMusicDistortion;
    [SerializeField, Range(10f, 22000f)]
    float missMusicCutoff = 850f;
    [SerializeField, Range(0f, 1f)]
    float missMusicDistortionBoost = 0.22f;
    [SerializeField, Min(0.01f)] float musicFeedbackAttack = 0.06f;
    [SerializeField, Min(0.01f)] float hitMusicRelease = 0.3f;
    [SerializeField, Min(0.01f)] float missMusicRelease = 0.45f;

    [Header("Heart Animation")]
    [SerializeField] Animator heartAnimator;
    [SerializeField] OSHeartHealHeartPresentation heartPresentation;
    [SerializeField] RuntimeAnimatorController[] heartAnimatorControllers =
        new RuntimeAnimatorController[MaxTreatmentIndex];
    [SerializeField] string treatmentStartTrigger = "TreatmentStart";
    [SerializeField] string shockTrigger = "Shock";
    [SerializeField] string hitTrigger = "Hit";
    [SerializeField] string missTrigger = "Miss";
    [SerializeField] string successTrigger = "Success";
    [SerializeField] string failureTrigger = "Failure";

    [Header("Runtime Effect")]
    [SerializeField, Range(1, 6)] int shockEffectPoolSize = 3;
    [SerializeField, Range(4, 16)] int shockParticleCount = 8;
    [SerializeField, Min(0f)] float shockEffectDuration = 0.24f;
    [SerializeField] Color shockEffectColor =
        new Color(0.34f, 0.025f, 0.02f, 0.68f);
    [SerializeField, Min(1f)] float shockGlowSize = 11f;
    [SerializeField, Min(3f)] float shockParticleSize = 5f;
    [SerializeField] Vector2 shockParticleDistance = new Vector2(9f, 17f);

    readonly List<ShockBurst> shockBursts = new List<ShockBurst>();
    Image[] beatTipImages = Array.Empty<Image>();
    Sequence[] beatTipSequences = Array.Empty<Sequence>();
    StateTransferUnit[] stateTransferUnits =
        Array.Empty<StateTransferUnit>();
    HeartHealData_SO currentData;
    OSHeartHealPuzzleData currentPattern;
    Coroutine treatmentRoutine;
    Material beatLineMaterial;
    Material glowMaterial;
    Material stateLiquidMaterial;
    Material originalStateActiveMaterial;
    Image stateLiquidImage;
    Tween stateTween;
    Tween stateColorTween;
    Tween linePulseTween;
    Tween previewCleanupTween;
    Sequence indexZeroTipSequence;
    Sequence waitLinkSequence;
    Sequence treatmentAudioFeedbackSequence;
    Tween treatmentEcgFadeTween;
    AudioSource treatmentEcgSource;
    Vector2 currentTreatmentMusicEffect;
    bool treatmentAudioActive;
    bool treatmentMusicFadeStarted;

    OSHeartHealBeatUnit previewBeatUnit;
    OSWindowUnit windowUnit;
    OS_Handle osHandle;
    OSLinkMessageController boundLinkMessageController;
    int lastShockFrame = -1;
    int nextBeatTipIndex;
    int nextStateTransferIndex;
    Vector2 previewBeatUnitPosition;
    bool previewBeatUnitWasActive;
    bool displayedStateQualified;
    bool stateColorInitialized;
    bool missingStateLiquidShaderWasLogged;
    double treatmentStartTime;
    int heartProcessIndex;
    float currentState;
    float rawTreatmentState;
    float visualStateTarget;
    int activeBeatCount;
    int resolvedBeatCount;
    int nextTreatmentBeatIndex;
    OSHeartTreatmentResumeData pendingPersistentTreatment;
    bool isTreating;
    bool isFinishingTreatment;
    bool isFirstTreatmentTutorialLocked;
    int treatmentInitialBattery = InvalidBatterySnapshot;

    public int HeartProcessIndex => heartProcessIndex;
    public int CurrentState => Mathf.RoundToInt(currentState);
    public int MaximumState => MaxHeartState;
    public bool IsTreating => isTreating;
    public bool IsFirstTreatmentTutorialLocked =>
        isFirstTreatmentTutorialLocked;
    public bool IsShockHeld => clickButton != null && clickButton.IsPressed;
    public bool CanTriggerShock => isTreating
        && !isFinishingTreatment
        && !isFirstTreatmentTutorialLocked
        && heartProcessIndex != NoTreatmentIndex
        && gameObject.activeInHierarchy
        && (windowUnit == null
            || (windowUnit.IsOpen && !windowUnit.IsMinimized))
        && (osHandle == null || (osHandle.IsOSVisible && !OSSessionPersistence.IsRestoring))
        && (playPartGroup == null
            || (playPartGroup.interactable
                && playPartGroup.blocksRaycasts))
        && HasShockEnergy();
    public IReadOnlyList<OSHeartHealPuzzleData> TreatmentPatterns =>
        currentData != null
            ? currentData.Puzzles
            : Array.Empty<OSHeartHealPuzzleData>();
    public float TreatmentElapsedTime => isTreating
        ? Mathf.Max(
            0f,
            (float)(Time.unscaledTimeAsDouble - treatmentStartTime))
        : 0f;
    public int SuccessState => currentData != null
        ? currentData.GetSuccessState(currentPattern)
        : 80;
    public float HitWindowSeconds => currentData != null
        ? currentData.HitWindowSeconds
        : 0.09f;
    internal Shader ReinforcedBreakShader =>
        currentData != null ? currentData.ReinforcedBreakShader : null;
    bool CanPreviewVisuals => Application.isPlaying
        && gameObject.activeInHierarchy
        && !isTreating;

    public event Action<int, int> HeartStateChanged;
    public event Action<int> TreatmentStarted;
    public event Action<bool> TreatmentFinished;
    public event Action<int> TreatmentCompletionConfirmed;
    public event Action<bool> ShockResolved;

    void Awake()
    {
        ResolveReferences();
        PrepareBeatUnits();
        PrepareBeatFeedbackPools();
        ConfigureStateGraphic();
        ConfigureBeatLineMaterial();
        EnsureShockEffectPool();
        ApplyTreatmentConfiguration(GetSavedTreatmentIndex(), false);
    }

    void OnEnable()
    {
        MrTGame.Core.GameEvents.LanguageChanged += OnLanguageChanged;
        ResolveReferences();
        BindFirstTreatmentTutorialEvents();
        PrepareBeatUnits();
        PrepareBeatFeedbackPools();
        ConfigureStateGraphic();
        ConfigureBeatLineMaterial();
        EnsureShockEffectPool();
        ApplyTreatmentConfiguration(GetSavedTreatmentIndex(), false);
        ApplyHeartAnimatorController();
        SetStateImmediate(currentState);
        ApplyTreatmentAvailability();
        RefreshLocalizedContent();
        RefreshFirstTreatmentTutorialState();
        RestoreSavedFinishState();
    }

    void LateUpdate()
    {
        UpdateStateLiquidTime();

        if (heartProcessIndex != NoTreatmentIndex)
            return;

        SetCanvasGroupVisible(heartDetailGroup, false);
        SetCanvasGroupVisible(playPartGroup, false);
    }

    void OnDisable()
    {
        pendingPersistentTreatment = null;
        MrTGame.Core.GameEvents.LanguageChanged -= OnLanguageChanged;
        UnbindFirstTreatmentTutorialEvents();
        SuspendFirstTreatmentTutorialPresentation();
        playPartPointGroup?.SetConnectionAvailability(false);
        finishPart?.HideImmediate();
        StopIndexZeroTipLoop();
        RestorePreviewBeatUnit();
        CancelTreatment(true, false);
        StopTreatmentEcgImmediate();
        KillVisualTweens();
        StopShockEffects();
        StopBeatFeedbackEffects();
    }

    void OnDestroy()
    {
        UnbindFirstTreatmentTutorialEvents();
        SuspendFirstTreatmentTutorialPresentation();
        StopTreatmentEcgImmediate();
        StopIndexZeroTipLoop();
        KillVisualTweens();
        StopBeatFeedbackEffects();
        DestroyRuntimeMaterial(beatLineMaterial);
        DestroyRuntimeMaterial(glowMaterial);
        ReleaseStateLiquidMaterial();
        DestroyStateTransferMaterials();
    }

    void OnValidate()
    {
        spawnX = Mathf.Max(spawnX, endX);
        hitMinX = Mathf.Min(hitMinX, hitMaxX);
        stateTipMinX = Mathf.Min(stateTipMinX, stateTipMaxX);
        stateChangeDuration = Mathf.Max(0f, stateChangeDuration);
        waitLinkDotInterval = Mathf.Max(0.05f, waitLinkDotInterval);
        treatmentMusicFade = Mathf.Max(0f, treatmentMusicFade);
        treatmentMusicFadeOut = Mathf.Max(0f, treatmentMusicFadeOut);
        treatmentEcgFadeOut = Mathf.Max(0f, treatmentEcgFadeOut);
        treatmentHitSfxPitch = Mathf.Clamp(
            treatmentHitSfxPitch,
            0.1f,
            3f);
        treatmentMissSfxPitch = Mathf.Clamp(
            treatmentMissSfxPitch,
            0.1f,
            3f);
        treatmentResultDelay = Mathf.Max(0f, treatmentResultDelay);
        treatmentEndDuration = Mathf.Max(0.1f, treatmentEndDuration);
        unstableMusicCutoff = Mathf.Clamp(
            unstableMusicCutoff,
            10f,
            22000f);
        stableMusicCutoff = Mathf.Clamp(stableMusicCutoff, 10f, 22000f);
        hitMusicCutoff = Mathf.Clamp(hitMusicCutoff, 10f, 22000f);
        missMusicCutoff = Mathf.Clamp(missMusicCutoff, 10f, 22000f);
        unstableMusicDistortion = Mathf.Clamp01(
            unstableMusicDistortion);
        stableMusicDistortion = Mathf.Clamp01(stableMusicDistortion);
        hitMusicDistortion = Mathf.Clamp01(hitMusicDistortion);
        missMusicDistortionBoost = Mathf.Clamp01(
            missMusicDistortionBoost);
        stateTransferDuration = Mathf.Max(0.05f, stateTransferDuration);
        stateTransferFadeDuration = Mathf.Clamp(
            stateTransferFadeDuration,
            0.01f,
            stateTransferDuration);
        musicFeedbackAttack = Mathf.Max(0.01f, musicFeedbackAttack);
        hitMusicRelease = Mathf.Max(0.01f, hitMusicRelease);
        missMusicRelease = Mathf.Max(0.01f, missMusicRelease);
    }

    public void PrepareRuntime()
    {
        ResolveReferences();
        BindFirstTreatmentTutorialEvents();
        PrepareBeatUnits();
        PrepareBeatFeedbackPools();
        ConfigureStateGraphic();
        ConfigureBeatLineMaterial();
        EnsureShockEffectPool();
        ApplyTreatmentConfiguration(GetSavedTreatmentIndex(), false);
    }

    public void ConfigureTreatment(int processIndex)
    {
        int clampedIndex = Mathf.Clamp(
            processIndex,
            NoTreatmentIndex,
            MaxTreatmentIndex);
        if (GetSavedTreatmentIndex() != clampedIndex)
            ClearSavedFinishState();

        ApplyTreatmentConfiguration(processIndex, true);
        RestoreSavedFinishState();
    }

    void ApplyTreatmentConfiguration(
        int processIndex,
        bool saveProcessIndex)
    {
        int clampedIndex = Mathf.Clamp(
            processIndex,
            NoTreatmentIndex,
            MaxTreatmentIndex);
        GameDataManager dataManager = GameDataManager.Instance;
        currentData = dataManager != null
            ? dataManager.HeartHealData
            : null;
        if (saveProcessIndex && dataManager != null)
        {
            clampedIndex = dataManager.SetCurrentHeartHealProcessIndex(
                clampedIndex);
        }

        CancelTreatment(false, false);
        finishPart?.HideImmediate();
        heartProcessIndex = clampedIndex;
        currentPattern = FindPattern(clampedIndex);
        currentState = currentPattern != null
            ? Mathf.Clamp(currentPattern.InitialState, 0, MaxHeartState)
            : 0;
        rawTreatmentState = currentState;
        ApplyHeartAnimatorController();
        SetStateImmediate(currentState);
        ApplyTreatmentAvailability();
        RefreshLocalizedContent();
        HeartStateChanged?.Invoke(CurrentState, MaxHeartState);
        RefreshFirstTreatmentTutorialState();
    }

    public bool StartTreatment()
    {
        return BeginTreatment();
    }

    public bool StopTreatment()
    {
        bool wasTreating = isTreating;
        CancelTreatment(true, false);
        return wasTreating;
    }

    public bool BeginTreatment()
    {
        if (isFirstTreatmentTutorialLocked
            || heartProcessIndex == NoTreatmentIndex
            || currentPattern == null)
        {
            return false;
        }

        ClearSavedFinishState();
        treatmentInitialBattery = GetCurrentBatteryValue();
        finishPart?.HideImmediate();
        FadeTreatmentContent(true);
        CancelTreatment(false, false);
        currentState = Mathf.Clamp(
            currentPattern.InitialState,
            0,
            MaxHeartState);
        rawTreatmentState = currentState;
        resolvedBeatCount = 0;
        nextTreatmentBeatIndex = 0;
        activeBeatCount = 0;
        isFinishingTreatment = false;
        treatmentStartTime = Time.unscaledTimeAsDouble;
        isTreating = true;
        startButton?.SetTreatmentActive(true);
        SetStateImmediate(currentState);
        HeartStateChanged?.Invoke(CurrentState, MaxHeartState);
        TriggerHeartAnimation(treatmentStartTrigger);
        TreatmentStarted?.Invoke(heartProcessIndex);
        StartTreatmentAudio();

        treatmentRoutine = StartCoroutine(RunTreatment(currentPattern));
        return true;
    }

    public void NotifyStartButtonPointerEntered()
    {
        if (isTreating)
            return;

        NotifyFirstTreatmentLinkMessage(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealStartHovered);
    }

    public void ResetTreatment()
    {
        ClearSavedFinishState();
        finishPart?.HideImmediate();
        CancelTreatment(true, false);
    }

    [ButtonGroup("Visual Preview")]
    [Button("Preview BeatLine Electric", ButtonSizes.Medium)]
    [EnableIf(nameof(CanPreviewVisuals))]
    public void PreviewBeatLineElectric()
    {
        if (!CanPreviewVisuals)
            return;

        ResolveReferences();
        ConfigureBeatLineMaterial();
        PulseBeatLine(electricColor);
    }

    [ButtonGroup("Visual Preview")]
    [Button("Preview First Beat Unit Hit", ButtonSizes.Medium)]
    [EnableIf(nameof(CanPreviewVisuals))]
    public void PreviewFirstBeatUnitHit()
    {
        if (!CanPreviewVisuals)
            return;

        ResolveReferences();
        ResolveBeatUnitReferences();
        ConfigureBeatLineMaterial();
        EnsureShockEffectPool();
        RestorePreviewBeatUnit();

        OSHeartHealBeatUnit unit = GetFirstBeatUnit();
        if (unit == null || unit.RectTransform == null)
            return;

        previewBeatUnit = unit;
        previewBeatUnitWasActive = unit.gameObject.activeSelf;
        previewBeatUnitPosition = unit.RectTransform.anchoredPosition;

        Vector2 previewPosition = previewBeatUnitPosition;
        previewPosition.x = beatLine != null
            ? ((RectTransform)beatLine.transform).anchoredPosition.x
            : (hitMinX + hitMaxX) * 0.5f;
        unit.RectTransform.anchoredPosition = previewPosition;
        unit.PreviewHit(electricColor);
        PulseBeatLine(electricColor);
        PlayShockEffect(previewPosition, unit.RectTransform);

        float fullDistance = Mathf.Max(
            0.01f,
            Mathf.Abs(spawnX - endX));
        float remainingDistance = Mathf.Abs(previewPosition.x - endX);
        float fullTravelDuration = currentPattern != null
            ? currentPattern.ApproachDuration + currentPattern.ExitDuration
            : 2.4f;
        float previewTravelDuration = Mathf.Max(
            shockEffectDuration + 0.04f,
            fullTravelDuration * remainingDistance / fullDistance);
        Tween travelTween = unit.RectTransform
            .DOAnchorPosX(endX, previewTravelDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        previewCleanupTween = travelTween;
        travelTween.OnComplete(() =>
        {
            if (previewCleanupTween != travelTween)
                return;

            previewCleanupTween = null;
            RestorePreviewBeatUnit();
        });
        travelTween.OnKill(() =>
        {
            if (previewCleanupTween == travelTween)
                previewCleanupTween = null;
        });
    }

    public bool TriggerShock()
    {
        TryTriggerShock(out bool didHit);
        return didHit;
    }

    public bool TryTriggerShock(out bool didHit)
    {
        didHit = false;
        if (!CanTriggerShock || lastShockFrame == Time.frameCount)
            return false;

        lastShockFrame = Time.frameCount;
        PlayConfiguredSfx(treatmentShockSfxKey);
        ConsumeShockEnergy();
        PlayEnergyCablePulse();
        TriggerHeartAnimation(shockTrigger);

        OSHeartHealBeatUnit target = isTreating
            ? FindBestHitTarget()
            : null;
        didHit = target != null && target.ResolveHit();
        bool brokeReinforcedLayer = didHit
                                    && target.LastHitBrokeReinforcedLayer;
        PulseBeatLine(didHit ? electricColor : missColor);

        if (didHit)
        {
            osHandle?.PlayInteractionFeedback();
            if (!brokeReinforcedLayer)
            {
                PlayTreatmentAudioFeedback(true);
                TriggerHeartAnimation(hitTrigger);
                if (!target.IsHoldBeat)
                {
                    PlayShockEffect(
                        target.ImpactAnchoredPosition,
                        target.RectTransform);
                }
            }
        }

        ShockResolved?.Invoke(didHit);
        return true;
    }

    bool HasShockEnergy()
    {
        GameResourceSystem resources = GameResourceSystem.Instance;
        if (resources != null)
            return resources.Get(ResourceType.Battery) > 0f;

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
            return dataManager.GetOSResourceValue(ResourceType.Battery) > 0;

        return !GameSetup.UseFormalFlow;
    }

    void ConsumeShockEnergy()
    {
        GameResourceSystem resources = GameResourceSystem.Instance;
        if (resources != null)
        {
            resources.Modify(ResourceType.Battery, -1f);
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        int previousValue = dataManager.GetOSResourceValue(
            ResourceType.Battery);
        int nextValue = dataManager.ModifyOSResourceValue(
            ResourceType.Battery,
            -1);
        if (nextValue != previousValue)
        {
            GameEvents.RaiseResourceChanged(
                ResourceType.Battery,
                nextValue,
                OSResourceSaveData.MaxValue);
        }
    }

    public int SetHeartState(int value, bool animate = true)
    {
        SetHeartState(value, animate, true);
        return CurrentState;
    }

    float SetHeartState(float value, bool animate, bool updateVisual)
    {
        rawTreatmentState = Mathf.Clamp(
            value,
            -MaxHeartState,
            MaxHeartState);
        float nextState = Mathf.Clamp(
            rawTreatmentState,
            0,
            MaxHeartState);
        if (Mathf.Approximately(nextState, currentState))
            return currentState;

        currentState = nextState;
        if (updateVisual)
        {
            if (animate)
                AnimateState(currentState);
            else
                SetStateImmediate(currentState);
        }

        HeartStateChanged?.Invoke(CurrentState, MaxHeartState);
        return currentState;
    }

    public int ModifyHeartState(int delta, bool animate = true)
    {
        SetHeartState(rawTreatmentState + delta, animate, true);
        return CurrentState;
    }

    public void TriggerHeartAnimation(string triggerName)
    {
        if (heartAnimator == null
            || heartAnimator.runtimeAnimatorController == null
            || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        int triggerHash = Animator.StringToHash(triggerName);
        AnimatorControllerParameter[] parameters = heartAnimator.parameters;
        bool hasTrigger = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.nameHash == triggerHash
                && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                hasTrigger = true;
                break;
            }
        }

        if (!hasTrigger)
            return;

        // Shock and a successful Hit can arrive in the same frame. Keep only
        // the resolved response, so the earlier trigger cannot replay afterward.
        if (heartPresentation != null)
        {
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].type == AnimatorControllerParameterType.Trigger)
                    heartAnimator.ResetTrigger(parameters[i].nameHash);
        }
        heartAnimator.SetTrigger(triggerName);
    }

    public void SetHeartAnimatorController(
        int processIndex,
        RuntimeAnimatorController controller)
    {
        if (processIndex < MinTreatmentIndex
            || processIndex > MaxTreatmentIndex)
        {
            return;
        }

        if (heartAnimatorControllers == null
            || heartAnimatorControllers.Length < MaxTreatmentIndex)
        {
            Array.Resize(
                ref heartAnimatorControllers,
                MaxTreatmentIndex);
        }

        heartAnimatorControllers[processIndex - 1] = controller;
        if (heartProcessIndex == processIndex)
            ApplyHeartAnimatorController();
    }

    internal void ReportMiss(OSHeartHealBeatUnit unit)
    {
        if (!isTreating || currentPattern == null || unit == null)
            return;

        resolvedBeatCount++;
        NotifyFirstTreatmentLinkMessage(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealFirstMiss);
        ApplyStateDelta(
            -currentPattern.MissDamage,
            unit.RectTransform,
            false);
        PlayTreatmentAudioFeedback(false);
        TriggerHeartAnimation(missTrigger);
        PulseBeatLine(missColor);
    }

    internal void ReportHoldSegmentMiss(
        OSHeartHealBeatUnit unit,
        RectTransform source)
    {
        if (!isTreating || currentPattern == null || unit == null)
            return;

        NotifyFirstTreatmentLinkMessage(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealFirstMiss);
        ApplyStateDelta(
            -currentPattern.MissDamage,
            source != null ? source : unit.RectTransform,
            false);
        PlayTreatmentAudioFeedback(false);
        TriggerHeartAnimation(missTrigger);
        PulseBeatLine(missColor);
    }

    internal void ReportHit(OSHeartHealBeatUnit unit)
    {
        if (!isTreating || currentPattern == null || unit == null)
            return;

        if (unit.BeatType == OSHeartHealBeatType.Tap)
            PlayConfiguredSfx(treatmentTapHitSfxKey);

        resolvedBeatCount++;
        NotifyFirstTreatmentLinkMessage(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealFirstHit);
        ApplyStateDelta(
            currentPattern.GetHitHeal(unit.BeatType),
            unit.RectTransform,
            true);
    }

    internal void ReportHoldTick(OSHeartHealBeatUnit unit)
    {
        if (!isTreating || currentPattern == null || unit == null)
            return;

        ApplyStateDelta(
            currentPattern.HoldTickHeal,
            unit.RectTransform,
            true);
        PulseBeatLine(electricColor);
        PlayShockEffect(
            unit.ImpactAnchoredPosition,
            unit.RectTransform);
    }

    internal void ReportHoldCompleted(OSHeartHealBeatUnit unit)
    {
        if (!isTreating || currentPattern == null || unit == null)
            return;

        resolvedBeatCount++;
        NotifyFirstTreatmentLinkMessage(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealFirstHit);
    }

    internal AudioSource StartHoldLoopSfx()
    {
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null
            || string.IsNullOrEmpty(treatmentHoldLoopSfxKey))
        {
            return null;
        }

        AudioSource source = audioManager.PlaySfx(
            treatmentHoldLoopSfxKey,
            1f,
            1f);
        if (source != null)
            source.loop = true;
        return source;
    }

    internal void PlayHoldLoopEndSfx()
    {
        if (!string.IsNullOrEmpty(treatmentHoldLoopEndSfxKey))
        {
            AudioManager.Instance?.PlaySfx(
                treatmentHoldLoopEndSfxKey,
                1f,
                1f);
        }
    }

    internal void ReleaseBeatUnit(OSHeartHealBeatUnit unit)
    {
        activeBeatCount = Mathf.Max(0, activeBeatCount - 1);
    }

    IEnumerator RunTreatment(OSHeartHealPuzzleData pattern)
    {
        IReadOnlyList<OSHeartHealBeatData> beatSequence =
            pattern.BeatSequence ?? Array.Empty<OSHeartHealBeatData>();
        while (isTreating)
        {
            SyncTreatmentClockToMusic();
            float elapsed = TreatmentElapsedTime;
            UpdateTreatmentAudioTimeline(pattern, elapsed);
            while (nextTreatmentBeatIndex < beatSequence.Count)
            {
                OSHeartHealBeatData beat = beatSequence[nextTreatmentBeatIndex];
                if (elapsed < beat.SpawnTime)
                    break;

                if (!TrySpawnBeat(beat))
                    ResolveDroppedBeat(nextTreatmentBeatIndex);

                nextTreatmentBeatIndex++;
            }

            bool reachedHardStop = pattern.HardStopTime > 0f
                                   && elapsed >= pattern.HardStopTime;
            bool chartHasEnded = pattern.ChartDuration > 0f
                ? elapsed >= pattern.ChartDuration
                : nextTreatmentBeatIndex >= beatSequence.Count;
            bool allBeatsResolved = nextTreatmentBeatIndex >= beatSequence.Count
                                    && resolvedBeatCount
                                    >= beatSequence.Count;
            bool shouldFinish = reachedHardStop
                                || allBeatsResolved
                                || (chartHasEnded && activeBeatCount == 0);
            if (shouldFinish)
            {
                bool success = currentState >= SuccessState;
                isFinishingTreatment = true;
                yield return RunTreatmentEnding();
                if (!isTreating)
                {
                    treatmentRoutine = null;
                    yield break;
                }

                treatmentRoutine = null;
                CompleteTreatment(success);
                yield break;
            }

            yield return null;
        }

        treatmentRoutine = null;
    }

    IEnumerator RunTreatmentEnding()
    {
        double delayStartTime = Time.unscaledTimeAsDouble;
        float delay = Mathf.Max(0f, treatmentResultDelay);
        while (isTreating
               && Time.unscaledTimeAsDouble - delayStartTime < delay)
        {
            yield return null;
        }

        if (!isTreating)
            yield break;

        float endDuration = Mathf.Max(0.1f, treatmentEndDuration);
        startButton?.SetTreatmentActive(false);
        DeactivateAllBeatUnits();
        StopTreatmentAudio(false, endDuration);
        PlayFixedPitchSfx(treatmentEndSfxKey);
        FadeTreatmentContent(false, endDuration);

        double endStartTime = Time.unscaledTimeAsDouble;
        while (isTreating
               && Time.unscaledTimeAsDouble - endStartTime < endDuration)
        {
            yield return null;
        }

        if (isTreating)
            FadeTreatmentContent(false, 0f);
    }

    bool TrySpawnBeat(OSHeartHealBeatData beat)
    {
        if (beat == null || currentPattern == null)
            return false;

        for (int i = 0; i < beatUnits.Length; i++)
        {
            OSHeartHealBeatUnit unit = beatUnits[i];
            if (unit == null || !unit.IsAvailable)
                continue;

            activeBeatCount++;
            unit.Spawn(
                this,
                beat,
                spawnX,
                endX,
                GetHitTargetX(),
                currentPattern.ApproachDuration,
                currentPattern.ExitDuration,
                HitWindowSeconds,
                electricColor,
                missColor);
            return true;
        }

        return false;
    }

    float GetHitTargetX()
    {
        return beatLine != null
            ? ((RectTransform)beatLine.transform).anchoredPosition.x
            : (hitMinX + hitMaxX) * 0.5f;
    }

    void ResolveDroppedBeat(int beatIndex)
    {
        resolvedBeatCount++;
        ApplyStateDelta(
            -currentPattern.MissDamage,
            beatLine != null ? beatLine.rectTransform : null,
            false);
        PlayTreatmentAudioFeedback(false);
        TriggerHeartAnimation(missTrigger);
        PulseBeatLine(missColor);
        Debug.LogWarning(
            $"[Heart Heal] Beat pool exhausted; beat {beatIndex} "
            + "was counted as a miss instead of delaying the chart.",
            this);
    }

    OSHeartHealBeatUnit FindBestHitTarget()
    {
        OSHeartHealBeatUnit bestTarget = null;
        float bestTimingError = float.MaxValue;

        for (int i = 0; i < beatUnits.Length; i++)
        {
            OSHeartHealBeatUnit unit = beatUnits[i];
            if (unit == null || !unit.CanBeHit)
                continue;

            float timingError = unit.TimingErrorSeconds;
            if (timingError >= bestTimingError)
                continue;

            bestTarget = unit;
            bestTimingError = timingError;
        }

        return bestTarget;
    }

    void ApplyStateDelta(
        float delta,
        RectTransform source,
        bool didHit)
    {
        heartPresentation?.ShowTreatmentFeedback(didHit);
        float previousState = currentState;
        float targetState = SetHeartState(
            rawTreatmentState + delta,
            true,
            false);
        float appliedDelta = targetState - previousState;
        if (previousState <= FirstTreatmentStateHintThreshold
            && targetState > FirstTreatmentStateHintThreshold)
        {
            NotifyFirstTreatmentLinkMessage(
                OSCharacterInteractionIds.Event01Signals
                    .HeartHealStateAbove65);
        }

        if (didHit)
        {
            if (!PlayStateTransferEffect(
                    source,
                    hitStateTransferColor,
                    targetState,
                    appliedDelta))
            {
                ApplyStateVisualDelta(appliedDelta);
            }

            return;
        }

        PlayMissStateBurst(missStateTransferColor);
        ApplyStateVisualDelta(appliedDelta);
    }

    void CompleteTreatment(bool success)
    {
        if (!isTreating)
            return;

        isTreating = false;
        isFinishingTreatment = false;
        startButton?.SetTreatmentActive(false);
        StopTreatmentRoutine();
        DeactivateAllBeatUnits();
        StopTreatmentAudio(true);
        TriggerHeartAnimation(success
            ? successTrigger
            : failureTrigger);
        FadeTreatmentContent(false);
        finishPart?.Show(success);
        SaveFinishState(success);
        if (success && heartProcessIndex == MinTreatmentIndex)
            MainGameHandle.Instance?.SetNpcLowHeart(false);
        PlayFixedPitchSfx(success
            ? treatmentSuccessSfxKey
            : treatmentFailSfxKey);
        NotifyFirstTreatmentLinkMessage(
            success
                ? OSCharacterInteractionIds.Event01Signals
                    .HeartHealSuccess
                : OSCharacterInteractionIds.Event01Signals
                    .HeartHealFail);
        TreatmentFinished?.Invoke(success);
    }

    public void ConfirmTreatment()
    {
        CompleteConfirmedTreatment();
    }

    public void ConfirmFalseWin()
    {
        CompleteConfirmedTreatment();
    }

    public void RetryTreatment()
    {
        RestoreInitialBatteryForRetry();
        finishPart?.HideImmediate();
        ResetTreatment();
        BeginTreatment();
    }

    void CompleteConfirmedTreatment()
    {
        int completedProcessIndex = heartProcessIndex;
        ClearSavedFinishState();
        finishPart?.HideImmediate();
        ApplyTreatmentCompletionEffect(completedProcessIndex);
        if (completedProcessIndex == MinTreatmentIndex)
            StoryAchievementProgress.Record(StoryAchievementMilestone.GravekeeperTreated);
        bool endingStarted = completedProcessIndex == MinTreatmentIndex
            && osHandle != null
            && osHandle.BeginFirstHeartHealEnding();
        if (!endingStarted)
            windowUnit?.Close();
        TreatmentCompletionConfirmed?.Invoke(completedProcessIndex);
    }

    void SaveFinishState(bool success)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        if (windowUnit != null)
            dataManager.SetOSWindowLayout(windowUnit.CaptureLayout());

        dataManager.SetOSWindowContentState(
            GetWindowSaveId(),
            success ? SuccessContentStateId : FailureContentStateId,
            heartProcessIndex,
            currentState,
            treatmentInitialBattery);
    }

    void RestoreSavedFinishState()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null
            || finishPart == null
            || !dataManager.TryGetOSWindowContentState(
                GetWindowSaveId(),
                out OSWindowLayoutSaveData savedState))
        {
            return;
        }

        bool isSuccess = string.Equals(
            savedState.contentStateId,
            SuccessContentStateId,
            StringComparison.Ordinal);
        bool isFailure = string.Equals(
            savedState.contentStateId,
            FailureContentStateId,
            StringComparison.Ordinal);
        if ((!isSuccess && !isFailure)
            || savedState.contentContextIndex != heartProcessIndex)
        {
            ClearSavedFinishState();
            return;
        }

        treatmentInitialBattery = Mathf.Clamp(
            savedState.contentCheckpointValue,
            OSResourceSaveData.MinValue,
            OSResourceSaveData.MaxValue);
        currentState = Mathf.Clamp(
            savedState.contentProgress,
            0f,
            MaxHeartState);
        rawTreatmentState = currentState;
        SetStateImmediate(currentState);
        HeartStateChanged?.Invoke(CurrentState, MaxHeartState);
        TriggerHeartAnimation(isSuccess ? successTrigger : failureTrigger);
        FadeTreatmentContent(false, 0f);
        finishPart.ShowImmediate(isSuccess);
    }

    void ClearSavedFinishState()
    {
        GameDataManager.Instance?.ClearOSWindowContentState(
            GetWindowSaveId());
        treatmentInitialBattery = InvalidBatterySnapshot;
    }

    public void CapturePersistentTreatment(OSHeartTreatmentResumeData saved)
    {
        if (saved == null || pendingPersistentTreatment != null) return;
        saved.active = isTreating;
        saved.beats.Clear();
        if (!isTreating || currentPattern == null) return;
        saved.processIndex = heartProcessIndex;
        saved.nextBeatIndex = nextTreatmentBeatIndex;
        saved.resolvedBeatCount = resolvedBeatCount;
        saved.initialBattery = treatmentInitialBattery;
        saved.elapsed = TreatmentElapsedTime;
        // Floating transfer pixels may not have arrived yet. Their logical result has.
        saved.state = rawTreatmentState;
        foreach (var unit in beatUnits)
        {
            if (unit == null) continue;
            var beat = unit.CapturePersistentBeat(currentPattern.BeatSequence);
            if (beat != null) saved.beats.Add(beat);
        }
    }

    public void RestorePersistentTreatment(OSHeartTreatmentResumeData saved)
    {
        if (saved == null || !saved.active || !gameObject.activeInHierarchy) return;
        saved.EnsureData();
        if (saved.processIndex != GetSavedTreatmentIndex()) return;
        pendingPersistentTreatment = JsonUtility.FromJson<OSHeartTreatmentResumeData>(JsonUtility.ToJson(saved));
        StartCoroutine(ResumePersistentTreatmentWhenReady());
    }

    IEnumerator ResumePersistentTreatmentWhenReady()
    {
        while (OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || (windowUnit != null && windowUnit.IsOpening)) yield return null;
        var saved = pendingPersistentTreatment;
        pendingPersistentTreatment = null;
        if (saved == null || !saved.active || !isActiveAndEnabled) yield break;
        ApplyTreatmentConfiguration(saved.processIndex, false);
        if (currentPattern == null) yield break;
        finishPart?.HideImmediate();
        FadeTreatmentContent(true, 0f);
        currentState = rawTreatmentState = Mathf.Clamp(saved.state, 0f, MaxHeartState);
        SetStateImmediate(currentState);
        treatmentInitialBattery = saved.initialBattery;
        nextTreatmentBeatIndex = Mathf.Clamp(saved.nextBeatIndex, 0, currentPattern.BeatSequence.Count);
        resolvedBeatCount = Mathf.Clamp(saved.resolvedBeatCount, 0, currentPattern.BeatSequence.Count);
        activeBeatCount = 0;
        treatmentStartTime = Time.unscaledTimeAsDouble - saved.elapsed;
        isTreating = true;
        isFinishingTreatment = false;
        startButton?.SetTreatmentActive(true);
        TriggerHeartAnimation(treatmentStartTrigger);
        foreach (var beat in saved.beats)
        {
            if (beat.chartIndex < 0 || beat.chartIndex >= nextTreatmentBeatIndex || beat.resolved) continue;
            foreach (var unit in beatUnits)
            {
                if (unit == null || !unit.IsAvailable) continue;
                unit.Spawn(this, currentPattern.BeatSequence[beat.chartIndex], spawnX, endX, GetHitTargetX(),
                    currentPattern.ApproachDuration, currentPattern.ExitDuration, HitWindowSeconds, electricColor, missColor);
                unit.RestorePersistentBeat(beat);
                activeBeatCount++;
                break;
            }
        }
        StartTreatmentAudio();
        var music = AudioManager.Instance?.CurrentMusicSource;
        if (music != null && currentPattern.MusicClip != null && music.clip == currentPattern.MusicClip)
            music.time = Mathf.Clamp(saved.elapsed, 0f, Mathf.Max(0f, music.clip.length - .01f));
        HeartStateChanged?.Invoke(CurrentState, MaxHeartState);
        treatmentRoutine = StartCoroutine(RunTreatment(currentPattern));
    }

    string GetWindowSaveId()
    {
        return windowUnit != null
               && !string.IsNullOrWhiteSpace(windowUnit.WindowId)
            ? windowUnit.WindowId
            : WindowSaveId;
    }

    int GetCurrentBatteryValue()
    {
        GameResourceSystem resources = GameResourceSystem.Instance;
        if (resources != null)
        {
            return Mathf.Clamp(
                Mathf.RoundToInt(resources.Get(ResourceType.Battery)),
                OSResourceSaveData.MinValue,
                OSResourceSaveData.MaxValue);
        }

        GameDataManager dataManager = GameDataManager.Instance;
        return dataManager != null
            ? dataManager.GetOSResourceValue(ResourceType.Battery)
            : OSResourceSaveData.MaxValue;
    }

    void RestoreInitialBatteryForRetry()
    {
        if (treatmentInitialBattery == InvalidBatterySnapshot)
            return;

        int targetValue = Mathf.Clamp(
            treatmentInitialBattery,
            OSResourceSaveData.MinValue,
            OSResourceSaveData.MaxValue);
        int currentValue = GetCurrentBatteryValue();
        if (targetValue == currentValue)
            return;

        GameResourceSystem resources = GameResourceSystem.Instance;
        if (resources != null)
        {
            resources.Modify(
                ResourceType.Battery,
                targetValue - currentValue);
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        int restoredValue = dataManager.SetOSResourceValue(
            ResourceType.Battery,
            targetValue);
        GameEvents.RaiseResourceChanged(
            ResourceType.Battery,
            restoredValue,
            OSResourceSaveData.MaxValue);
    }

    void ApplyTreatmentCompletionEffect(int completedProcessIndex)
    {
        if (completedProcessIndex != MinTreatmentIndex)
            return;

        ApplyFirstTreatmentCompletionEffect();
    }

    void ApplyFirstTreatmentCompletionEffect()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            dataManager.SetCurrentHeartHealProcessIndex(
                NoTreatmentIndex);
        }
        else
        {
            Debug.LogWarning(
                "[Heart Heal] GameDataManager is missing; "
                + "treatment process index was not reset.",
                this);
        }

        MainGameHandle mainGame = MainGameHandle.Instance;
        if (mainGame != null)
        {
            mainGame.SetNpcLowHeart(false);
            mainGame.SetEventActive("Event_02", true);
        }
        else
            Debug.LogWarning(
                "[Heart Heal] MainGameHandle is missing; Event_02 was not activated.",
                this);
    }

    void CancelTreatment(bool resetState, bool notifyFailure)
    {
        heartPresentation?.ResetPresentation();
        bool wasTreating = isTreating;
        isTreating = false;
        isFinishingTreatment = false;
        startButton?.SetTreatmentActive(false);
        StopTreatmentRoutine();
        DeactivateAllBeatUnits();

        if (wasTreating)
            StopTreatmentAudio();

        if (resetState)
        {
            currentState = currentPattern != null
                ? Mathf.Clamp(
                    currentPattern.InitialState,
                    0,
                    MaxHeartState)
                : 0;
            rawTreatmentState = currentState;
            SetStateImmediate(currentState);
            HeartStateChanged?.Invoke(CurrentState, MaxHeartState);
        }

        if (wasTreating && notifyFailure)
            TreatmentFinished?.Invoke(false);
    }

    void StartTreatmentAudio()
    {
        treatmentMusicFadeStarted = false;
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
            return;

        KillTreatmentAudioFeedback();
        // A level with its own track carries its own ECG; the loop only fills silent levels.
        if (currentPattern?.MusicClip == null)
        {
            StartTreatmentEcg(audioManager);
            return;
        }
        StopTreatmentEcgImmediate();

        currentTreatmentMusicEffect = GetTreatmentMusicBaseline();
        audioManager.BeginMusicTreatmentEffect(
            currentTreatmentMusicEffect.x,
            currentTreatmentMusicEffect.y);
        audioManager.PlayMusic(
            currentPattern.MusicClip,
            treatmentMusicFade,
            false);
        // A retry inside the last attempt's fade-out would otherwise pick the track up mid-way.
        AudioSource music = audioManager.CurrentMusicSource;
        if (music != null && music.clip == currentPattern.MusicClip)
            music.time = 0f;
        treatmentAudioActive = true;
    }

    // Beats are timed to land on the track, so once it plays its position is the clock;
    // a hitch or a late start can no longer push the hits off the music.
    void SyncTreatmentClockToMusic()
    {
        if (!treatmentAudioActive
            || treatmentMusicFadeStarted
            || currentPattern?.MusicClip == null)
        {
            return;
        }

        AudioSource music = AudioManager.Instance?.CurrentMusicSource;
        if (music == null
            || music.clip != currentPattern.MusicClip
            || !music.isPlaying
            || music.clip.frequency <= 0)
        {
            return;
        }

        double musicTime = music.timeSamples / (double)music.clip.frequency;
        double drift = musicTime
                       - (Time.unscaledTimeAsDouble - treatmentStartTime);
        if (Math.Abs(drift) > MusicClockTolerance)
            treatmentStartTime -= drift;
    }

    void PlayTreatmentAudioFeedback(bool hit)
    {
        PlayTreatmentBeatSfx(hit);
        if (!hit)
            PlayConfiguredSfx(treatmentBeatFailSfxKey);

        if (!treatmentAudioActive)
            return;

        KillTreatmentAudioFeedback();

        Vector2 baseline = GetTreatmentMusicBaseline();
        Vector2 peak = hit
            ? new Vector2(hitMusicCutoff, hitMusicDistortion)
            : new Vector2(
                missMusicCutoff,
                Mathf.Clamp01(
                    baseline.y + missMusicDistortionBoost));
        float release = hit
            ? hitMusicRelease
            : missMusicRelease;

        Sequence sequence = DOTween.Sequence();
        treatmentAudioFeedbackSequence = sequence;
        sequence.Append(DOTween
            .To(
                () => currentTreatmentMusicEffect,
                ApplyTreatmentMusicEffect,
                peak,
                musicFeedbackAttack)
            .SetEase(Ease.OutQuad));
        sequence.Append(DOTween
            .To(
                () => currentTreatmentMusicEffect,
                ApplyTreatmentMusicEffect,
                baseline,
                release)
            .SetEase(Ease.InOutSine));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (treatmentAudioFeedbackSequence == sequence)
                    treatmentAudioFeedbackSequence = null;
            });
    }

    void PlayTreatmentBeatSfx(bool hit)
    {
        if (string.IsNullOrEmpty(treatmentBeatSfxKey))
            return;

        AudioManager.Instance?.PlaySfx(
            treatmentBeatSfxKey,
            1f,
            hit ? treatmentHitSfxPitch : treatmentMissSfxPitch);
    }

    static void PlayConfiguredSfx(string keyId)
    {
        if (!string.IsNullOrEmpty(keyId))
            AudioManager.Instance?.PlaySfx(keyId);
    }

    Vector2 GetTreatmentMusicBaseline()
    {
        float health = Mathf.Clamp01(
            currentState / Mathf.Max(1f, SuccessState));
        return new Vector2(
            Mathf.Lerp(
                unstableMusicCutoff,
                stableMusicCutoff,
                health),
            Mathf.Lerp(
                unstableMusicDistortion,
                stableMusicDistortion,
                health));
    }

    void ApplyTreatmentMusicEffect(Vector2 effect)
    {
        currentTreatmentMusicEffect = new Vector2(
            Mathf.Clamp(effect.x, 10f, 22000f),
            Mathf.Clamp01(effect.y));
        AudioManager.Instance?.SetMusicTreatmentEffect(
            currentTreatmentMusicEffect.x,
            currentTreatmentMusicEffect.y);
    }

    void UpdateTreatmentAudioTimeline(
        OSHeartHealPuzzleData pattern,
        float elapsed)
    {
        if (treatmentMusicFadeStarted
            || !treatmentAudioActive
            || pattern == null
            || pattern.MusicFadeOutStartTime <= 0f
            || elapsed < pattern.MusicFadeOutStartTime)
        {
            return;
        }

        treatmentMusicFadeStarted = true;
        float fadeDuration = Mathf.Max(
            0f,
            pattern.MusicStopTime - elapsed);
        AudioManager.Instance?.StopMusic(fadeDuration);
    }

    void StopTreatmentAudio(
        bool preserveScheduledMusicFade = false,
        float musicFadeDuration = -1f)
    {
        KillTreatmentAudioFeedback();
        StopTreatmentEcg(treatmentEcgFadeOut);
        if (!treatmentAudioActive)
            return;

        bool musicFadeWasStarted = treatmentMusicFadeStarted;
        treatmentAudioActive = false;
        treatmentMusicFadeStarted = false;
        AudioManager audioManager = AudioManager.Instance;
        if (!preserveScheduledMusicFade || !musicFadeWasStarted)
        {
            float fadeDuration = musicFadeDuration >= 0f
                ? musicFadeDuration
                : treatmentMusicFadeOut;
            audioManager?.StopMusic(Mathf.Max(0f, fadeDuration));
        }
        audioManager?.EndMusicTreatmentEffect();
    }

    void PlayFixedPitchSfx(string keyId)
    {
        if (!string.IsNullOrEmpty(keyId))
            AudioManager.Instance?.PlaySfx(keyId, 1f, 1f);
    }

    void StartTreatmentEcg(AudioManager audioManager)
    {
        StopTreatmentEcgImmediate();
        if (audioManager == null || string.IsNullOrEmpty(treatmentEcgSfxKey))
            return;

        AudioSource source = audioManager.PlaySfx(treatmentEcgSfxKey);
        if (source == null)
            return;

        source.loop = true;
        treatmentEcgSource = source;
    }

    void StopTreatmentEcg(float duration)
    {
        if (treatmentEcgFadeTween != null)
        {
            Tween previousTween = treatmentEcgFadeTween;
            treatmentEcgFadeTween = null;
            previousTween.Kill(false);
        }

        AudioSource source = treatmentEcgSource;
        if (source == null)
            return;

        float safeDuration = Mathf.Max(0f, duration);
        if (safeDuration <= 0f
            || !source.isPlaying
            || !gameObject.activeInHierarchy)
        {
            treatmentEcgSource = null;
            ResetTreatmentEcgSource(source);
            return;
        }

        Tween tween = DOTween
            .To(
                () => source != null ? source.volume : 0f,
                value =>
                {
                    if (source != null)
                        source.volume = value;
                },
                0f,
                safeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true);
        treatmentEcgFadeTween = tween;
        tween.OnComplete(() =>
        {
            if (treatmentEcgFadeTween != tween)
                return;

            treatmentEcgFadeTween = null;
            if (treatmentEcgSource == source)
                treatmentEcgSource = null;
            ResetTreatmentEcgSource(source);
        });
        tween.OnKill(() =>
        {
            if (treatmentEcgFadeTween == tween)
                treatmentEcgFadeTween = null;
        });
    }

    void StopTreatmentEcgImmediate()
    {
        if (treatmentEcgFadeTween != null)
        {
            Tween tween = treatmentEcgFadeTween;
            treatmentEcgFadeTween = null;
            tween.Kill(false);
        }

        AudioSource source = treatmentEcgSource;
        treatmentEcgSource = null;
        ResetTreatmentEcgSource(source);
    }

    static void ResetTreatmentEcgSource(AudioSource source)
    {
        if (source == null)
            return;

        source.loop = false;
        source.Stop();
        source.clip = null;
        source.volume = 1f;
        source.pitch = 1f;
    }

    void KillTreatmentAudioFeedback()
    {
        if (treatmentAudioFeedbackSequence == null)
            return;

        Sequence sequence = treatmentAudioFeedbackSequence;
        treatmentAudioFeedbackSequence = null;
        sequence.Kill(false);
    }

    void StopTreatmentRoutine()
    {
        if (treatmentRoutine == null)
            return;

        Coroutine routine = treatmentRoutine;
        treatmentRoutine = null;
        StopCoroutine(routine);
    }

    void DeactivateAllBeatUnits()
    {
        if (beatUnits == null)
            return;

        for (int i = 0; i < beatUnits.Length; i++)
            beatUnits[i]?.ReturnToPool(false);

        activeBeatCount = 0;
    }

    void PrepareBeatFeedbackPools()
    {
        PrepareBeatTipPool();
        PrepareStateTransferPool();
    }

    void PrepareBeatTipPool()
    {
        StopBeatTipEffects();
        if (beatTipPool == null)
        {
            beatTipImages = Array.Empty<Image>();
            beatTipSequences = Array.Empty<Sequence>();
            return;
        }

        int count = beatTipPool.childCount;
        beatTipImages = new Image[count];
        beatTipSequences = new Sequence[count];
        nextBeatTipIndex = 0;
        for (int i = 0; i < count; i++)
        {
            Image image = beatTipPool.GetChild(i).GetComponent<Image>();
            beatTipImages[i] = image;
            if (image == null)
                continue;

            if (damageTipSprite == null && image.sprite != null)
                damageTipSprite = image.sprite;
            image.raycastTarget = false;
            image.gameObject.SetActive(false);
        }
    }

    void PrepareStateTransferPool()
    {
        StopStateTransferEffects();
        if (stateTransferPool == null)
        {
            DestroyStateTransferMaterials();
            return;
        }

        int count = stateTransferPool.childCount;
        bool needsRebuild = stateTransferUnits.Length != count;
        if (!needsRebuild)
        {
            for (int i = 0; i < count; i++)
            {
                Image image = stateTransferPool
                    .GetChild(i)
                    .GetComponent<Image>();
                if (stateTransferUnits[i] == null
                    || stateTransferUnits[i].Image != image
                    || stateTransferUnits[i].Material == null)
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (needsRebuild)
        {
            DestroyStateTransferMaterials();
            stateTransferUnits = new StateTransferUnit[count];
            Shader shader = Shader.Find("UI/HeartHealStateTransfer");
            for (int i = 0; i < count; i++)
            {
                Image image = stateTransferPool
                    .GetChild(i)
                    .GetComponent<Image>();
                if (image == null)
                    continue;

                Material material = shader != null
                    ? new Material(shader)
                    : null;
                if (material != null)
                {
                    material.name = $"Heart Heal State Transfer {i} (Runtime)";
                    material.hideFlags = HideFlags.HideAndDontSave;
                    image.material = material;
                }

                stateTransferUnits[i] = new StateTransferUnit(
                    image,
                    material);
            }
        }

        nextStateTransferIndex = 0;
        for (int i = 0; i < stateTransferUnits.Length; i++)
        {
            StateTransferUnit unit = stateTransferUnits[i];
            if (unit == null || unit.Image == null)
                continue;

            unit.Image.raycastTarget = false;
            unit.Image.gameObject.SetActive(false);
        }
    }

    void PlayBeatTip(RectTransform source, bool didHit)
    {
        if (source == null || beatTipImages.Length == 0)
            return;

        Sprite sprite = didHit ? healTipSprite : damageTipSprite;
        if (sprite == null)
            return;

        int index = nextBeatTipIndex;
        nextBeatTipIndex = (nextBeatTipIndex + 1) % beatTipImages.Length;
        Sequence previousSequence = beatTipSequences[index];
        beatTipSequences[index] = null;
        previousSequence?.Kill(false);

        Image image = beatTipImages[index];
        if (image == null)
            return;

        RectTransform rect = image.rectTransform;
        Vector3 sourcePosition = beatTipPool.InverseTransformPoint(
            source.position);
        rect.localPosition = new Vector3(
            sourcePosition.x,
            sourcePosition.y,
            0f);
        image.sprite = sprite;
        Color color = image.color;
        color.a = 1f;
        image.color = color;
        image.gameObject.SetActive(true);

        float targetY = rect.localPosition.y
            + (didHit ? BeatTipDistance : -BeatTipDistance);
        Sequence sequence = DOTween.Sequence();
        beatTipSequences[index] = sequence;
        sequence.Append(rect
            .DOLocalMoveY(targetY, BeatTipMoveDuration)
            .SetEase(Ease.OutQuad));
        sequence.Insert(
            BeatTipFadeDelay,
            image
                .DOFade(0f, BeatTipFadeDuration)
                .SetEase(Ease.Linear));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() => FinishBeatTip(index, sequence))
            .OnKill(() => FinishBeatTip(index, sequence));
    }

    void FinishBeatTip(int index, Sequence sequence)
    {
        if (index < 0
            || index >= beatTipSequences.Length
            || beatTipSequences[index] != sequence)
        {
            return;
        }

        beatTipSequences[index] = null;
        Image image = beatTipImages[index];
        if (image != null)
            image.gameObject.SetActive(false);
    }

    bool PlayStateTransferEffect(
        RectTransform source,
        Color effectColor,
        float targetState,
        float stateDelta)
    {
        if (source == null
            || stateActive == null
            || stateTransferPool == null
            || stateTransferUnits.Length == 0)
        {
            return false;
        }

        StateTransferUnit unit = AcquireStateTransferUnit();
        if (unit == null)
            return false;

        RectTransform rect = unit.Image.rectTransform;
        Vector3 startPosition = stateTransferPool.InverseTransformPoint(
            source.position);
        startPosition = new Vector3(
            startPosition.x,
            startPosition.y,
            0f);

        RectTransform stateRect = stateActive.rectTransform;
        Rect stateBounds = stateRect.rect;
        Vector3 stateLocalTarget = new Vector3(
            Mathf.Lerp(
                stateBounds.xMin,
                stateBounds.xMax,
                Mathf.Clamp01(targetState / (float)MaxHeartState)),
            stateBounds.center.y,
            0f);
        Vector3 worldTarget = stateRect.TransformPoint(stateLocalTarget);
        Vector3 targetPosition = stateTransferPool.InverseTransformPoint(
            worldTarget);
        targetPosition.z = 0f;

        ConfigureStateTransferUnit(
            unit,
            startPosition,
            effectColor,
            false,
            HitStateTransferSquareScale);

        float duration = Mathf.Max(
            0.05f,
            stateTransferDuration * HitStateTransferDurationScale);
        float scatterDuration = duration * HitStateScatterFraction;
        float absorbDuration = duration - scatterDuration;
        float fadeDuration = Mathf.Clamp(
            stateTransferFadeDuration * HitStateTransferDurationScale,
            0.01f,
            duration);
        float curveHeight = Mathf.Clamp(
            Vector3.Distance(startPosition, targetPosition) * 0.045f,
            3f,
            8f);
        Vector3 controlPosition = Vector3.Lerp(
            startPosition,
            targetPosition,
            0.5f) + Vector3.up * curveHeight;
        Sequence sequence = DOTween.Sequence();
        unit.Sequence = sequence;
        unit.PendingStateDelta = stateDelta;
        sequence.Append(DOTween
            .To(
                () => unit.Material.GetFloat("_ScatterProgress"),
                value => unit.Material.SetFloat(
                    "_ScatterProgress",
                    value),
                1f,
                scatterDuration)
            .SetEase(Ease.OutCubic));
        sequence.Append(DOVirtual
            .Float(
                0f,
                1f,
                absorbDuration,
                progress =>
                {
                    Vector3 startToControl = Vector3.LerpUnclamped(
                        startPosition,
                        controlPosition,
                        progress);
                    Vector3 controlToTarget = Vector3.LerpUnclamped(
                        controlPosition,
                        targetPosition,
                        progress);
                    rect.localPosition = Vector3.LerpUnclamped(
                        startToControl,
                        controlToTarget,
                        progress);
                })
            .SetEase(Ease.InQuad));
        sequence.Join(DOTween
            .To(
                () => unit.Material.GetFloat("_Progress"),
                value => unit.Material.SetFloat("_Progress", value),
                1f,
                absorbDuration)
            .SetEase(Ease.InQuad));
        sequence.Insert(
            duration - fadeDuration,
            unit.Image
                .DOFade(0f, fadeDuration)
                .SetEase(Ease.InQuad));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() => FinishStateTransfer(
                unit,
                sequence,
                true))
            .OnKill(() => FinishStateTransfer(
                unit,
                sequence,
                false));
        return true;
    }

    bool PlayMissStateBurst(Color effectColor)
    {
        if (stateActive == null
            || stateTransferPool == null
            || stateTransferUnits.Length == 0)
        {
            return false;
        }

        StateTransferUnit unit = AcquireStateTransferUnit();
        if (unit == null)
            return false;

        RectTransform stateRect = stateActive.rectTransform;
        Rect stateBounds = stateRect.rect;
        Vector3 stateLocalOrigin = new Vector3(
            Mathf.Lerp(
                stateBounds.xMin,
                stateBounds.xMax,
                stateActive.fillAmount),
            stateBounds.center.y,
            0f);
        Vector3 worldOrigin = stateRect.TransformPoint(stateLocalOrigin);
        Vector3 burstPosition = stateTransferPool.InverseTransformPoint(
            worldOrigin);
        burstPosition.z = 0f;

        ConfigureStateTransferUnit(
            unit,
            burstPosition,
            effectColor,
            true,
            MissStateBurstSquareScale);

        float duration = Mathf.Max(0.05f, stateTransferDuration);
        float fadeDuration = Mathf.Clamp(
            stateTransferFadeDuration,
            0.01f,
            duration);
        Sequence sequence = DOTween.Sequence();
        unit.Sequence = sequence;
        sequence.Join(DOTween
            .To(
                () => unit.Material.GetFloat("_Progress"),
                value => unit.Material.SetFloat("_Progress", value),
                1f,
                duration)
            .SetEase(Ease.OutCubic));
        sequence.Insert(
            duration - fadeDuration,
            unit.Image
                .DOFade(0f, fadeDuration)
                .SetEase(Ease.InQuad));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() => FinishStateTransfer(
                unit,
                sequence,
                false))
            .OnKill(() => FinishStateTransfer(
                unit,
                sequence,
                false));
        return true;
    }

    StateTransferUnit AcquireStateTransferUnit()
    {
        for (int i = 0; i < stateTransferUnits.Length; i++)
        {
            int index = (nextStateTransferIndex + i)
                % stateTransferUnits.Length;
            StateTransferUnit candidate = stateTransferUnits[index];
            if (candidate == null
                || candidate.Image == null
                || candidate.Material == null)
            {
                continue;
            }

            nextStateTransferIndex = (index + 1)
                % stateTransferUnits.Length;
            Sequence previousSequence = candidate.Sequence;
            float pendingStateDelta = candidate.PendingStateDelta;
            candidate.PendingStateDelta = 0f;
            candidate.Sequence = null;
            previousSequence?.Kill(false);
            ApplyStateVisualDelta(pendingStateDelta);
            return candidate;
        }

        return null;
    }

    static void ConfigureStateTransferUnit(
        StateTransferUnit unit,
        Vector3 localPosition,
        Color effectColor,
        bool burstMode,
        float squareScale)
    {
        RectTransform rect = unit.Image.rectTransform;
        rect.localPosition = localPosition;
        rect.localScale = Vector3.one;

        Color imageColor = Color.white;
        imageColor.a = 1f;
        unit.Image.color = imageColor;
        unit.Material.SetColor("_EffectColor", effectColor);
        unit.Material.SetFloat("_Progress", 0f);
        unit.Material.SetFloat("_ScatterProgress", 0f);
        unit.Material.SetFloat("_BurstMode", burstMode ? 1f : 0f);
        unit.Material.SetFloat(
            "_SquareScale",
            Mathf.Max(0.01f, squareScale));
        unit.Material.SetFloat(
            "_Seed",
            UnityEngine.Random.Range(0f, 1000f));
        unit.Image.gameObject.SetActive(true);
        unit.Image.transform.SetAsLastSibling();
    }

    void FinishStateTransfer(
        StateTransferUnit unit,
        Sequence sequence,
        bool updateStateVisual)
    {
        if (unit == null || unit.Sequence != sequence)
            return;

        unit.Sequence = null;
        float pendingStateDelta = unit.PendingStateDelta;
        unit.PendingStateDelta = 0f;
        if (unit.Image != null)
            unit.Image.gameObject.SetActive(false);
        if (updateStateVisual)
            ApplyStateVisualDelta(pendingStateDelta);
    }

    void StopBeatFeedbackEffects()
    {
        StopBeatTipEffects();
        StopStateTransferEffects();
    }

    void StopBeatTipEffects()
    {
        for (int i = 0; i < beatTipSequences.Length; i++)
        {
            Sequence sequence = beatTipSequences[i];
            beatTipSequences[i] = null;
            sequence?.Kill(false);
            Image image = i < beatTipImages.Length
                ? beatTipImages[i]
                : null;
            if (image != null)
                image.gameObject.SetActive(false);
        }
    }

    void StopStateTransferEffects()
    {
        for (int i = 0; i < stateTransferUnits.Length; i++)
        {
            StateTransferUnit unit = stateTransferUnits[i];
            if (unit == null)
                continue;

            Sequence sequence = unit.Sequence;
            unit.Sequence = null;
            unit.PendingStateDelta = 0f;
            sequence?.Kill(false);
            if (unit.Image != null)
                unit.Image.gameObject.SetActive(false);
        }
    }

    void DestroyStateTransferMaterials()
    {
        for (int i = 0; i < stateTransferUnits.Length; i++)
        {
            StateTransferUnit unit = stateTransferUnits[i];
            if (unit == null)
                continue;

            if (unit.Image != null
                && unit.Image.material == unit.Material)
            {
                unit.Image.material = null;
            }

            DestroyRuntimeMaterial(unit.Material);
        }

        stateTransferUnits = Array.Empty<StateTransferUnit>();
    }

    OSHeartHealPuzzleData FindPattern(int processIndex)
    {
        if (processIndex == NoTreatmentIndex || currentData == null)
            return null;

        return currentData.TryGetPuzzle(processIndex, out var pattern)
            ? pattern
            : null;
    }

    void ConfigureStateGraphic()
    {
        if (stateActive == null)
            return;

        if (Application.isPlaying && TryConfigureStateLiquidGraphic())
            return;

        stateActive.type = Image.Type.Filled;
        stateActive.fillMethod = Image.FillMethod.Horizontal;
        stateActive.fillOrigin = (int)Image.OriginHorizontal.Left;
        stateActive.fillClockwise = true;
    }

    bool TryConfigureStateLiquidGraphic()
    {
        if (stateLiquidMaterial != null && stateLiquidImage != stateActive)
            ReleaseStateLiquidMaterial();

        if (stateLiquidMaterial == null)
        {
            Shader shader = Shader.Find(StateLiquidShaderName);
            if (shader == null)
            {
                if (!missingStateLiquidShaderWasLogged)
                {
                    Debug.LogWarning(
                        $"[Heart Heal] Required shader '{StateLiquidShaderName}' "
                        + "was not found; StateActive is using the filled-image fallback.",
                        this);
                    missingStateLiquidShaderWasLogged = true;
                }

                return false;
            }

            stateLiquidImage = stateActive;
            originalStateActiveMaterial = stateActive.material;
            stateLiquidMaterial = new Material(shader)
            {
                name = "Heart Heal State Liquid (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        stateActive.type = Image.Type.Simple;
        stateActive.material = stateLiquidMaterial;
        ConfigureStateLiquidSpriteUv();
        stateLiquidMaterial.SetFloat(
            StateLiquidFillAmountId,
            stateActive.fillAmount);
        UpdateStateLiquidTime();
        return true;
    }

    void ConfigureStateLiquidSpriteUv()
    {
        if (stateLiquidMaterial == null || stateActive == null)
            return;

        Vector4 spriteUvRect = new Vector4(0f, 0f, 1f, 1f);
        if (stateActive.sprite != null)
        {
            Vector4 outerUv = UnityEngine.Sprites.DataUtility.GetOuterUV(
                stateActive.sprite);
            spriteUvRect = new Vector4(
                outerUv.x,
                outerUv.y,
                Mathf.Max(0.0001f, outerUv.z - outerUv.x),
                Mathf.Max(0.0001f, outerUv.w - outerUv.y));
        }

        stateLiquidMaterial.SetVector(
            StateLiquidSpriteUvRectId,
            spriteUvRect);
    }

    void UpdateStateLiquidTime()
    {
        if (stateLiquidMaterial != null)
        {
            stateLiquidMaterial.SetFloat(
                StateLiquidWaveTimeId,
                Time.unscaledTime);
        }
    }

    void ReleaseStateLiquidMaterial()
    {
        if (stateLiquidImage != null
            && stateLiquidImage.material == stateLiquidMaterial)
        {
            stateLiquidImage.material = originalStateActiveMaterial;
        }

        DestroyRuntimeMaterial(stateLiquidMaterial);
        stateLiquidMaterial = null;
        originalStateActiveMaterial = null;
        stateLiquidImage = null;
    }

    void ApplyStateVisualDelta(float delta)
    {
        if (Mathf.Approximately(delta, 0f))
            return;

        AnimateState(visualStateTarget + delta);
    }

    void AnimateState(float value)
    {
        KillStateFillTween();
        visualStateTarget = Mathf.Clamp(value, 0f, MaxHeartState);
        float target = visualStateTarget / MaxHeartState;
        float current = stateActive != null
            ? stateActive.fillAmount
            : target;
        float duration = Mathf.Max(0f, stateChangeDuration);
        if (duration <= 0f)
        {
            SetStateVisual(target);
            return;
        }

        Tween tween = DOTween
            .To(() => current, normalized =>
            {
                current = normalized;
                SetStateVisual(normalized);
            }, target, duration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        stateTween = tween;
        tween.OnKill(() =>
        {
            if (stateTween == tween)
                stateTween = null;
        });
    }

    void SetStateImmediate(float value)
    {
        KillStateFillTween();
        KillStateColorTween();
        visualStateTarget = Mathf.Clamp(value, 0f, MaxHeartState);
        SetStateVisual(
            visualStateTarget / MaxHeartState,
            false);
    }

    void SetStateVisual(
        float normalized,
        bool animateColor = true)
    {
        float clamped = Mathf.Clamp01(normalized);
        if (stateActive != null)
        {
            stateActive.fillAmount = clamped;
            stateLiquidMaterial?.SetFloat(
                StateLiquidFillAmountId,
                clamped);
            bool qualified = clamped
                >= SuccessState / (float)MaxHeartState;
            Color targetColor = qualified
                ? stableStateColor
                : unstableStateColor;
            bool shouldChangeColor = !stateColorInitialized
                || displayedStateQualified != qualified
                || (stateColorTween == null
                    && !ColorsApproximately(
                        stateActive.color,
                        targetColor));
            displayedStateQualified = qualified;
            stateColorInitialized = true;

            if (!animateColor)
            {
                KillStateColorTween();
                stateActive.color = targetColor;
            }
            else if (shouldChangeColor)
            {
                AnimateStateColor(targetColor);
            }
        }

        if (stateTip != null)
        {
            Vector2 position = stateTip.anchoredPosition;
            position.x = Mathf.Lerp(stateTipMinX, stateTipMaxX, clamped);
            stateTip.anchoredPosition = position;
        }
    }

    void AnimateStateColor(Color targetColor)
    {
        KillStateColorTween();
        if (stateActive == null)
            return;

        float duration = Mathf.Max(0f, stateChangeDuration);
        if (duration <= 0f)
        {
            stateActive.color = targetColor;
            return;
        }

        Tween tween = stateActive
            .DOColor(targetColor, duration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        stateColorTween = tween;
        tween.OnKill(() =>
        {
            if (stateColorTween == tween)
                stateColorTween = null;
        });
    }

    void KillStateColorTween()
    {
        if (stateColorTween == null)
            return;

        Tween tween = stateColorTween;
        stateColorTween = null;
        tween.Kill(false);
    }

    void ConfigureBeatLineMaterial()
    {
        if (beatLine == null || beatLineMaterial != null)
            return;

        Shader shader = Shader.Find("UI/HeartHealElectric");
        if (shader == null)
            return;

        beatLineMaterial = new Material(shader)
        {
            name = "HeartHealBeatLine (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        beatLineMaterial.SetFloat("_ElectricAmount", 0f);
        beatLine.material = beatLineMaterial;
    }

    void PulseBeatLine(Color color)
    {
        ConfigureBeatLineMaterial();
        if (beatLineMaterial == null)
        {
            beatLine?.DOKill(false);
            if (beatLine != null)
            {
                Color original = beatLine.color;
                beatLine.color = color;
                beatLine
                    .DOColor(original, Mathf.Max(0.01f, linePulseDuration))
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable);
            }
            return;
        }

        if (linePulseTween != null)
        {
            Tween previous = linePulseTween;
            linePulseTween = null;
            previous.Kill(false);
        }

        beatLineMaterial.SetColor("_ElectricColor", color * 2.2f);
        beatLineMaterial.SetFloat("_ElectricAmount", 0f);
        float duration = Mathf.Max(0.02f, linePulseDuration);
        Sequence sequence = DOTween.Sequence();
        linePulseTween = sequence;
        sequence.Append(DOVirtual.Float(
            0f,
            1f,
            duration * 0.3f,
            value => beatLineMaterial?.SetFloat("_ElectricAmount", value)));
        sequence.Append(DOVirtual.Float(
            1f,
            0f,
            duration * 0.7f,
            value => beatLineMaterial?.SetFloat("_ElectricAmount", value)));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (linePulseTween == sequence)
                    linePulseTween = null;
                beatLineMaterial?.SetFloat("_ElectricAmount", 0f);
            });
    }

    void EnsureShockEffectPool()
    {
        if (beatPool == null || shockBursts.Count > 0)
            return;

        Shader shader = Shader.Find("UI/HeartHealGlow");
        if (shader != null)
        {
            glowMaterial = new Material(shader)
            {
                name = "HeartHealGlow (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            glowMaterial.SetColor(
                "_GlowColor",
                new Color(
                    shockEffectColor.r * 2.2f,
                    shockEffectColor.g * 2.2f,
                    shockEffectColor.b * 2.2f,
                    1f));
        }

        int burstCount = Mathf.Clamp(shockEffectPoolSize, 1, 6);
        int particleCount = Mathf.Clamp(shockParticleCount, 4, 16);
        for (int i = 0; i < burstCount; i++)
            shockBursts.Add(CreateShockBurst(i, particleCount));
    }

    ShockBurst CreateShockBurst(int index, int particleCount)
    {
        GameObject rootObject = new GameObject(
            $"ShockBurst_{index}",
            typeof(RectTransform));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(beatPool, false);
        root.sizeDelta = Vector2.zero;
        root.SetAsLastSibling();

        float glowSize = Mathf.Max(1f, shockGlowSize);
        Image glow = CreateEffectImage(
            "Glow",
            root,
            new Vector2(glowSize, glowSize));
        if (glowMaterial != null)
            glow.material = glowMaterial;

        ShockParticle[] particles = new ShockParticle[particleCount];
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i] = CreateHollowParticle(
                $"Particle_{i}",
                root,
                Mathf.Max(3f, shockParticleSize));
        }

        rootObject.SetActive(false);
        return new ShockBurst(root, glow, particles);
    }

    static Image CreateEffectImage(
        string objectName,
        RectTransform parent,
        Vector2 size)
    {
        GameObject effectObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rect = effectObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        Image image = effectObject.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    static ShockParticle CreateHollowParticle(
        string objectName,
        RectTransform parent,
        float size)
    {
        GameObject particleObject = new GameObject(
            objectName,
            typeof(RectTransform));
        RectTransform particle =
            particleObject.GetComponent<RectTransform>();
        particle.SetParent(parent, false);
        particle.sizeDelta = new Vector2(size, size);

        float border = Mathf.Clamp(size * 0.2f, 0.75f, 1.25f);
        float half = size * 0.5f;
        float innerSize = Mathf.Max(0f, size - border * 2f);
        Image[] edges =
        {
            CreateParticleEdge(
                "Top",
                particle,
                new Vector2(size, border),
                new Vector2(0f, half - border * 0.5f)),
            CreateParticleEdge(
                "Bottom",
                particle,
                new Vector2(size, border),
                new Vector2(0f, -half + border * 0.5f)),
            CreateParticleEdge(
                "Left",
                particle,
                new Vector2(border, innerSize),
                new Vector2(-half + border * 0.5f, 0f)),
            CreateParticleEdge(
                "Right",
                particle,
                new Vector2(border, innerSize),
                new Vector2(half - border * 0.5f, 0f))
        };
        return new ShockParticle(particle, edges);
    }

    static Image CreateParticleEdge(
        string objectName,
        RectTransform parent,
        Vector2 size,
        Vector2 position)
    {
        Image edge = CreateEffectImage(objectName, parent, size);
        edge.rectTransform.anchoredPosition = position;
        return edge;
    }

    void PlayShockEffect(
        Vector2 anchoredPosition,
        RectTransform followTarget = null)
    {
        EnsureShockEffectPool();
        if (shockBursts.Count == 0)
            return;

        ShockBurst burst = FindAvailableShockBurst();
        burst.Sequence?.Kill(false);
        burst.Root.gameObject.SetActive(true);
        burst.Root.SetAsLastSibling();
        burst.Root.anchoredPosition = anchoredPosition;
        burst.Root.localScale = Vector3.one;

        float duration = Mathf.Max(0.05f, shockEffectDuration);
        Sequence sequence = DOTween.Sequence();
        burst.Sequence = sequence;

        RectTransform glowRect = burst.Glow.rectTransform;
        glowRect.anchoredPosition = Vector2.zero;
        glowRect.localScale = Vector3.one * 0.6f;
        burst.Glow.color = new Color(
            1f,
            1f,
            1f,
            shockEffectColor.a * 0.55f);
        sequence.Join(glowRect
            .DOScale(1.5f, duration)
            .SetEase(Ease.OutCubic));
        sequence.Join(burst.Glow
            .DOFade(0f, duration)
            .SetEase(Ease.OutQuad));

        for (int i = 0; i < burst.Particles.Length; i++)
        {
            ShockParticle particle = burst.Particles[i];
            RectTransform particleRect = particle.Root;
            float angle = 360f * i / burst.Particles.Length
                + UnityEngine.Random.Range(-12f, 12f);
            Vector2 direction = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad));
            float minimumDistance = Mathf.Min(
                shockParticleDistance.x,
                shockParticleDistance.y);
            float maximumDistance = Mathf.Max(
                shockParticleDistance.x,
                shockParticleDistance.y);
            float distance = UnityEngine.Random.Range(
                minimumDistance,
                maximumDistance);
            particleRect.anchoredPosition = Vector2.zero;
            particleRect.localRotation = Quaternion.Euler(
                0f,
                0f,
                UnityEngine.Random.Range(-30f, 30f));
            particleRect.localScale = Vector3.one
                * UnityEngine.Random.Range(0.75f, 1.15f);
            Color particleColor = shockEffectColor;
            particleColor.a = Mathf.Clamp01(shockEffectColor.a + 0.12f);
            particle.SetColor(particleColor);
            sequence.Join(particleRect
                .DOAnchorPos(direction * distance, duration)
                .SetEase(Ease.OutCubic));
            sequence.Join(DOVirtual.Float(
                particleColor.a,
                0f,
                duration,
                particle.SetAlpha)
                .SetEase(Ease.InQuad));
            sequence.Join(particleRect
                .DOScale(0.55f, duration)
                .SetEase(Ease.InQuad));
        }

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnUpdate(() =>
            {
                if (followTarget != null
                    && followTarget.gameObject.activeInHierarchy)
                {
                    burst.Root.anchoredPosition =
                        followTarget.anchoredPosition;
                }
            })
            .OnComplete(() => DeactivateShockBurst(burst, sequence))
            .OnKill(() => DeactivateShockBurst(burst, sequence));
    }

    ShockBurst FindAvailableShockBurst()
    {
        for (int i = 0; i < shockBursts.Count; i++)
        {
            ShockBurst burst = shockBursts[i];
            if (burst.Sequence == null || !burst.Sequence.IsActive())
                return burst;
        }

        return shockBursts[0];
    }

    static void DeactivateShockBurst(ShockBurst burst, Sequence sequence)
    {
        if (burst == null || burst.Sequence != sequence)
            return;

        burst.Sequence = null;
        if (burst.Root != null)
            burst.Root.gameObject.SetActive(false);
    }

    void StopShockEffects()
    {
        for (int i = 0; i < shockBursts.Count; i++)
        {
            ShockBurst burst = shockBursts[i];
            Sequence sequence = burst.Sequence;
            burst.Sequence = null;
            sequence?.Kill(false);
            if (burst.Root != null)
                burst.Root.gameObject.SetActive(false);
        }
    }

    void PrepareBeatUnits()
    {
        ResolveBeatUnitReferences();
        for (int i = 0; i < beatUnits.Length; i++)
            beatUnits[i]?.PrepareForPool();
    }

    void ResolveBeatUnitReferences()
    {
        if (beatPool == null)
        {
            beatUnits = Array.Empty<OSHeartHealBeatUnit>();
            return;
        }

        bool hasValidReference = beatUnits != null
            && beatUnits.Length > 0
            && Array.Exists(beatUnits, unit => unit != null);
        if (!hasValidReference)
        {
            beatUnits = beatPool
                .GetComponentsInChildren<OSHeartHealBeatUnit>(true);
        }
    }

    OSHeartHealBeatUnit GetFirstBeatUnit()
    {
        if (beatUnits == null)
            return null;

        for (int i = 0; i < beatUnits.Length; i++)
        {
            if (beatUnits[i] != null)
                return beatUnits[i];
        }

        return null;
    }

    void RestorePreviewBeatUnit()
    {
        if (previewCleanupTween != null)
        {
            Tween tween = previewCleanupTween;
            previewCleanupTween = null;
            tween.Kill(false);
        }

        OSHeartHealBeatUnit unit = previewBeatUnit;
        previewBeatUnit = null;
        if (unit == null || unit.RectTransform == null)
            return;

        unit.StopPreview();
        unit.RectTransform.anchoredPosition = previewBeatUnitPosition;
        unit.gameObject.SetActive(previewBeatUnitWasActive);
    }

    void ApplyHeartAnimatorController()
    {
        if (heartAnimator == null)
            return;

        RuntimeAnimatorController controller = null;
        int controllerIndex = heartProcessIndex - MinTreatmentIndex;
        if (heartAnimatorControllers != null
            && controllerIndex >= 0
            && controllerIndex < heartAnimatorControllers.Length)
        {
            controller = heartAnimatorControllers[controllerIndex];
        }

        if (heartAnimator.runtimeAnimatorController == controller)
            return;

        heartAnimator.runtimeAnimatorController = controller;
        if (controller != null)
            heartAnimator.Rebind();
    }

    int GetSavedTreatmentIndex()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        return dataManager != null
            ? dataManager.CurrentHeartHealProcessIndex
            : Mathf.Clamp(
                heartProcessIndex,
                NoTreatmentIndex,
                MaxTreatmentIndex);
    }

    void ApplyTreatmentAvailability()
    {
        bool hasTreatment = heartProcessIndex != NoTreatmentIndex;
        bool canInteract = hasTreatment
                           && !isFirstTreatmentTutorialLocked;
        SetCanvasGroupVisible(heartDetailGroup, hasTreatment);
        SetCanvasGroupVisible(playPartGroup, hasTreatment);
        SetCanvasGroupInteraction(heartDetailGroup, canInteract);
        SetCanvasGroupInteraction(playPartGroup, canInteract);
        playPartPointGroup?.SetConnectionAvailability(hasTreatment);

        if (indexZeroTip == null)
            return;

        indexZeroTip.gameObject.SetActive(!hasTreatment);
        if (!hasTreatment)
        {
            Color tipColor = indexZeroTip.color;
            tipColor.a = 1f;
            indexZeroTip.color = tipColor;
        }

        if (hasTreatment)
            StopIndexZeroTipLoop();
        else
            StartIndexZeroTipLoop();
    }

    void NotifyFirstTreatmentLinkMessage(string signalId)
    {
        if (heartProcessIndex != MinTreatmentIndex
            || string.IsNullOrWhiteSpace(signalId))
        {
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        OSCharacterInteractionSaveData saveState = dataManager != null
            ? dataManager.OSCharacterInteractionState
            : null;
        if (saveState == null)
            return;

        saveState.EnsureData();
        if (!string.Equals(
                saveState.activeSequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHeal,
                StringComparison.Ordinal)
            || !HasCompletedFirstTreatmentTutorial(saveState))
        {
            return;
        }

        if (osHandle == null)
            ResolveReferences();
        osHandle?.LinkMessageController?.NotifyExternalSignal(signalId);
    }

    void BindFirstTreatmentTutorialEvents()
    {
        if (windowUnit != null)
        {
            windowUnit.OpenStarted -= HandleFirstTreatmentWindowOpened;
            windowUnit.OpenStarted += HandleFirstTreatmentWindowOpened;
        }

        OSLinkMessageController nextController = osHandle != null
            ? osHandle.LinkMessageController
            : null;
        if (boundLinkMessageController == nextController)
            return;

        if (boundLinkMessageController != null)
        {
            boundLinkMessageController.EntryPresentationCompleted -=
                HandleFirstTreatmentLinkMessageCompleted;
        }

        boundLinkMessageController = nextController;
        if (boundLinkMessageController != null)
        {
            boundLinkMessageController.EntryPresentationCompleted +=
                HandleFirstTreatmentLinkMessageCompleted;
        }
    }

    void UnbindFirstTreatmentTutorialEvents()
    {
        if (windowUnit != null)
            windowUnit.OpenStarted -= HandleFirstTreatmentWindowOpened;

        if (boundLinkMessageController != null)
        {
            boundLinkMessageController.EntryPresentationCompleted -=
                HandleFirstTreatmentLinkMessageCompleted;
            boundLinkMessageController = null;
        }
    }

    void HandleFirstTreatmentWindowOpened(OSWindowUnit source)
    {
        if (source == windowUnit)
            RefreshFirstTreatmentTutorialState();
    }

    void HandleFirstTreatmentLinkMessageCompleted(
        string sequenceId,
        OSCharacterInteractionEntryData entry)
    {
        if (!string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHeal,
                StringComparison.Ordinal)
            || entry == null
            || !string.Equals(
                entry.PresentationCueId,
                OSCharacterInteractionIds.PresentationCues
                    .UnlockEvent01HeartHeal,
                StringComparison.Ordinal))
        {
            return;
        }

        RefreshFirstTreatmentTutorialState();
    }

    void RefreshFirstTreatmentTutorialState()
    {
        SetFirstTreatmentTutorialLocked(
            ShouldLockFirstTreatmentTutorial());
        if (isFirstTreatmentTutorialLocked)
            osHandle?.LinkMessageController?.RestoreInterruptedFirstTreatmentTutorial();
    }

    bool ShouldLockFirstTreatmentTutorial()
    {
        if (heartProcessIndex != MinTreatmentIndex
            || (windowUnit != null && !windowUnit.IsOpen)
            || osHandle == null
            || osHandle.LinkMessageController == null)
        {
            return false;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        OSCharacterInteractionSaveData saveState = dataManager != null
            ? dataManager.OSCharacterInteractionState
            : null;
        if (dataManager == null || saveState == null)
            return false;

        saveState.EnsureData();
        if (!string.Equals(
                saveState.activeSequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHeal,
                StringComparison.Ordinal)
            || !saveState.HasCompletedEntry(
                OSCharacterInteractionIds.Event01Entries
                    .OpeningRememberedFolder)
            || HasCompletedFirstTreatmentTutorial(saveState))
        {
            return false;
        }

        OSCharacterInteractionData_SO interactionData =
            dataManager.OSCharacterInteractionData;
        return interactionData != null
               && interactionData.TryGetSequence(
                   OSCharacterInteractionIds.PrologueEvent01HeartHeal,
                   out OSCharacterInteractionSequenceData sequence)
               && sequence.TryGetEntry(
                   OSCharacterInteractionIds.Event01Entries
                       .HeartHealRecallChoice,
                   out _);
    }

    static bool HasCompletedFirstTreatmentTutorial(
        OSCharacterInteractionSaveData saveState)
    {
        return saveState != null
               && (saveState.HasCompletedEntry(
                       OSCharacterInteractionIds.Event01Entries
                           .HeartHealStartAfterShock)
                   || saveState.HasCompletedEntry(
                       OSCharacterInteractionIds.Event01Entries
                           .HeartHealShockAfterStart));
    }

    void SetFirstTreatmentTutorialLocked(bool isLocked)
    {
        isFirstTreatmentTutorialLocked = isLocked;
        bool showPresentation = isLocked
                                && isActiveAndEnabled
                                && (windowUnit == null
                                    || windowUnit.IsOpen);

        if (tutorialDimmer != null
            && tutorialDimmer.activeSelf != showPresentation)
        {
            tutorialDimmer.SetActive(showPresentation);
        }

        if (showPresentation)
        {
            if (waitLink != null && !waitLink.gameObject.activeSelf)
                waitLink.gameObject.SetActive(true);
            StartWaitLinkLoop();
        }
        else
        {
            StopWaitLinkLoop();
            if (waitLink != null && waitLink.gameObject.activeSelf)
                waitLink.gameObject.SetActive(false);
        }

        ApplyTreatmentAvailability();
        osHandle?.SetEvent01HeartHealTutorialSuppressed(showPresentation);
    }

    void SuspendFirstTreatmentTutorialPresentation()
    {
        isFirstTreatmentTutorialLocked = false;
        StopWaitLinkLoop();
        if (tutorialDimmer != null && tutorialDimmer.activeSelf)
            tutorialDimmer.SetActive(false);
        if (waitLink != null && waitLink.gameObject.activeSelf)
            waitLink.gameObject.SetActive(false);
        osHandle?.SetEvent01HeartHealTutorialSuppressed(false);
    }

    void StartWaitLinkLoop()
    {
        StopWaitLinkLoop();
        if (!isFirstTreatmentTutorialLocked
            || waitLink == null
            || !waitLink.gameObject.activeInHierarchy)
        {
            return;
        }

        waitLinkLocalizedText?.SetLocalization(
            WaitLinkLocalizationKey,
            WaitLinkFallbackText,
            osHandle);
        string baseText = GetWaitLinkLocalizedText();
        SetWaitLinkDots(baseText, 1);

        float interval = Mathf.Max(0.05f, waitLinkDotInterval);
        Sequence sequence = DOTween.Sequence();
        waitLinkSequence = sequence;
        sequence
            .AppendInterval(interval)
            .AppendCallback(() => SetWaitLinkDots(baseText, 2))
            .AppendInterval(interval)
            .AppendCallback(() => SetWaitLinkDots(baseText, 3))
            .AppendInterval(interval)
            .AppendCallback(() => SetWaitLinkDots(baseText, 1))
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(waitLink.gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (waitLinkSequence == sequence)
                    waitLinkSequence = null;
            });
    }

    void StopWaitLinkLoop()
    {
        if (waitLinkSequence == null)
            return;

        Sequence sequence = waitLinkSequence;
        waitLinkSequence = null;
        sequence.Kill(false);
    }

    string GetWaitLinkLocalizedText()
    {
        if (osHandle != null)
        {
            return osHandle.GetLocalizedText(
                WaitLinkLocalizationKey,
                WaitLinkFallbackText);
        }

        GameDataManager dataManager = GameDataManager.Instance;
        return dataManager != null
            ? dataManager.GetLocalizedText(
                WaitLinkLocalizationKey,
                WaitLinkFallbackText)
            : WaitLinkFallbackText;
    }

    void SetWaitLinkDots(string baseText, int dotCount)
    {
        if (waitLink != null)
            waitLink.text = (baseText ?? WaitLinkFallbackText)
                            + new string('.', dotCount);
    }

    static void SetCanvasGroupInteraction(
        CanvasGroup group,
        bool interactable)
    {
        if (group == null)
            return;

        group.interactable = interactable;
        group.blocksRaycasts = interactable;
    }

    static void SetCanvasGroupVisible(CanvasGroup group, bool visible)
    {
        if (group == null)
            return;

        group.DOKill(false);
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }

    void FadeTreatmentContent(bool visible)
    {
        FadeTreatmentContent(visible, FinishTransitionDuration);
    }

    void FadeTreatmentContent(bool visible, float duration)
    {
        FadeCanvasGroup(
            heartDetailGroup,
            visible,
            duration);
        FadeCanvasGroup(
            playPartGroup,
            visible,
            duration);
        if (visible || duration <= 0f)
            playPartPointGroup?.SetConnectionAvailability(visible);
    }

    static void FadeCanvasGroup(
        CanvasGroup group,
        bool visible,
        float duration)
    {
        if (group == null)
            return;

        group.DOKill(false);
        group.interactable = visible;
        group.blocksRaycasts = visible;
        float targetAlpha = visible ? 1f : 0f;
        if (duration <= 0f
            || Mathf.Approximately(group.alpha, targetAlpha))
        {
            group.alpha = targetAlpha;
            return;
        }

        group
            .DOFade(targetAlpha, duration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(group.gameObject, LinkBehaviour.KillOnDisable);
    }

    void StartIndexZeroTipLoop()
    {
        StopIndexZeroTipLoop();
        if (indexZeroTip == null || !gameObject.activeInHierarchy)
            return;

        OSLocalizedFontUtility.ApplyCurrent(indexZeroTip);
        string baseText = GetNoTreatmentLocalizedText();
        float interval = Mathf.Max(0.1f, indexZeroTipInterval);
        Sequence sequence = DOTween.Sequence();
        indexZeroTipSequence = sequence;
        sequence.AppendCallback(() => SetIndexZeroTip(baseText, 1));
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetIndexZeroTip(baseText, 2));
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetIndexZeroTip(baseText, 3));
        sequence.AppendInterval(interval);
        sequence
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (indexZeroTipSequence == sequence)
                    indexZeroTipSequence = null;
            });
    }

    void StopIndexZeroTipLoop()
    {
        if (indexZeroTipSequence == null)
            return;

        Sequence sequence = indexZeroTipSequence;
        indexZeroTipSequence = null;
        sequence.Kill(false);
    }

    void SetIndexZeroTip(string baseText, int dotCount)
    {
        if (indexZeroTip != null)
            indexZeroTip.text = baseText + new string('.', dotCount);
    }

    string GetNoTreatmentLocalizedText()
    {
        const string key = "ui.os.heart_heal.no_treatment";
        const string fallback = "暂无需要治疗的心脏";
        if (osHandle != null)
            return osHandle.GetLocalizedText(key, fallback);

        GameDataManager dataManager = GameDataManager.Instance;
        return dataManager != null
            ? dataManager.GetLocalizedText(key, fallback)
            : fallback;
    }

    void RefreshLocalizedContent()
    {
        OSLocalizedText[] localizedTexts =
            GetComponentsInChildren<OSLocalizedText>(true);
        for (int i = 0; i < localizedTexts.Length; i++)
        {
            localizedTexts[i]?.Initialize();
            localizedTexts[i]?.Refresh(osHandle);
        }

        startButton?.RefreshLocalization();
        if (heartProcessIndex == NoTreatmentIndex)
            StartIndexZeroTipLoop();
        if (isFirstTreatmentTutorialLocked)
            StartWaitLinkLoop();
    }

    void OnLanguageChanged(LanguageState language)
    {
        RefreshLocalizedContent();
    }

    void ResolveReferences()
    {
        if (windowUnit == null)
            windowUnit = GetComponent<OSWindowUnit>();

        Transform heartDetail = transform.Find("HeartDetail");
        if (heartDetailGroup == null)
            heartDetailGroup = heartDetail?.GetComponent<CanvasGroup>();
        Transform state = heartDetail?.Find("bk/State");
        if (stateActive == null)
            stateActive = state?.Find("StateActive")?.GetComponent<Image>();
        if (stateTip == null)
            stateTip = state?.Find("StateTip") as RectTransform;

        if (heartAnimator == null)
        {
            heartAnimator = heartDetail
                ?.Find("bk/Heart/Image")
                ?.GetComponent<Animator>();
        }
        if (heartPresentation == null && heartAnimator != null)
            heartPresentation = heartAnimator.GetComponent<OSHeartHealHeartPresentation>();

        Transform playPart = transform.Find("PlayPart");
        if (playPartGroup == null)
            playPartGroup = playPart?.GetComponent<CanvasGroup>();
        Transform pointGroup = playPart?.Find("PointGroup");
        if (playPartPointGroup == null)
        {
            playPartPointGroup = pointGroup
                ?.GetComponent<OSUICableConnectionGroup>();
        }
        if (playPartEnergyPoint == null)
        {
            playPartEnergyPoint = pointGroup?.Find("EnergyPoint")
                as RectTransform;
        }
        Transform beatPart = playPart?.Find("BeatPart");
        if (beatPool == null)
            beatPool = beatPart?.Find("BeatPool") as RectTransform;
        if (beatLine == null)
            beatLine = beatPart?.Find("BeatLine")?.GetComponent<Image>();
        if (beatTipPool == null)
            beatTipPool = beatPart?.Find("Tips") as RectTransform;
        if (stateTransferPool == null)
            stateTransferPool = beatPart?.Find("SFX") as RectTransform;

        if (indexZeroTip == null)
            indexZeroTip = transform.Find("Index0_Tip")?.GetComponent<TMP_Text>();
        OSLocalizedFontUtility.ApplyCurrent(indexZeroTip);
        Transform tutorialDimmerTransform = transform.Find("TutorialDimmer");
        if (tutorialDimmer == null && tutorialDimmerTransform != null)
            tutorialDimmer = tutorialDimmerTransform.gameObject;
        Transform waitLinkTransform = transform.Find("WaitLink");
        if (waitLink == null)
            waitLink = waitLinkTransform?.GetComponent<TMP_Text>();
        if (waitLinkLocalizedText == null)
        {
            waitLinkLocalizedText = waitLinkTransform
                ?.GetComponent<OSLocalizedText>();
        }
        if (osHandle == null)
            osHandle = GetComponentInParent<OS_Handle>(true);

        Transform finishPartTransform = transform.Find("FinishPart");
        if (finishPart == null && finishPartTransform != null)
        {
            finishPart = finishPartTransform
                .GetComponent<OSHeartHealFinishPart>();
            if (finishPart == null && Application.isPlaying)
            {
                finishPart = finishPartTransform.gameObject
                    .AddComponent<OSHeartHealFinishPart>();
            }
        }

        finishPart?.Configure(
            ConfirmTreatment,
            ConfirmFalseWin,
            RetryTreatment);

        Transform startButtonTransform = playPart?.Find(
            "ClickPart/ClickArea/StartButton");
        if (startButton == null && startButtonTransform != null)
        {
            startButton = startButtonTransform
                .GetComponent<OSHeartHealStartButton>();
            if (startButton == null && Application.isPlaying)
            {
                startButton = startButtonTransform.gameObject
                    .AddComponent<OSHeartHealStartButton>();
            }
        }

        Transform clickButtonTransform = playPart?.Find(
            "ClickPart/ClickArea/ClickButton");
        if (clickButton == null && clickButtonTransform != null)
        {
            clickButton = clickButtonTransform
                .GetComponent<OSHeartHealClickButton>();
            if (clickButton == null && Application.isPlaying)
            {
                clickButton = clickButtonTransform.gameObject
                    .AddComponent<OSHeartHealClickButton>();
            }
        }

        startButton?.Configure(this);
        clickButton?.Configure(this);
    }

    void PlayEnergyCablePulse()
    {
        if (playPartPointGroup == null || playPartEnergyPoint == null)
            ResolveReferences();

        if (playPartPointGroup == null)
            return;

        if (playPartEnergyPoint != null)
            playPartPointGroup.PlayPointPulse(playPartEnergyPoint);
        else
            playPartPointGroup.PlayEnergyPulse();
    }

    void KillVisualTweens()
    {
        KillStateFillTween();
        KillStateColorTween();

        if (linePulseTween != null)
        {
            Tween tween = linePulseTween;
            linePulseTween = null;
            tween.Kill(false);
        }

        beatLineMaterial?.SetFloat("_ElectricAmount", 0f);
        beatLine?.DOKill(false);
    }

    void KillStateFillTween()
    {
        if (stateTween != null)
        {
            Tween tween = stateTween;
            stateTween = null;
            tween.Kill(false);
        }
    }

    static bool ColorsApproximately(Color left, Color right)
    {
        const float tolerance = 0.001f;
        return Mathf.Abs(left.r - right.r) <= tolerance
            && Mathf.Abs(left.g - right.g) <= tolerance
            && Mathf.Abs(left.b - right.b) <= tolerance
            && Mathf.Abs(left.a - right.a) <= tolerance;
    }

    static void DestroyRuntimeMaterial(Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }

    sealed class StateTransferUnit
    {
        public readonly Image Image;
        public readonly Material Material;
        public Sequence Sequence;
        public float PendingStateDelta;

        public StateTransferUnit(Image image, Material material)
        {
            Image = image;
            Material = material;
        }
    }

    sealed class ShockBurst
    {
        public readonly RectTransform Root;
        public readonly Image Glow;
        public readonly ShockParticle[] Particles;
        public Sequence Sequence;

        public ShockBurst(
            RectTransform root,
            Image glow,
            ShockParticle[] particles)
        {
            Root = root;
            Glow = glow;
            Particles = particles;
        }
    }

    sealed class ShockParticle
    {
        public readonly RectTransform Root;
        readonly Image[] edges;

        public ShockParticle(RectTransform root, Image[] particleEdges)
        {
            Root = root;
            edges = particleEdges;
        }

        public void SetColor(Color color)
        {
            for (int i = 0; i < edges.Length; i++)
                edges[i].color = color;
        }

        public void SetAlpha(float alpha)
        {
            for (int i = 0; i < edges.Length; i++)
            {
                Color color = edges[i].color;
                color.a = alpha;
                edges[i].color = color;
            }
        }
    }
}
