using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform), typeof(Image), typeof(CanvasGroup))]
public sealed class OSHeartHealBeatUnit : MonoBehaviour
{
    const int FastFlashCount = 5;
    const float BreakFeedbackDuration = 0.22f;
    const float HoldLoopFadeDuration = 0.1f;
    const string ReinforcedImageName = "ReinforcedImage";
    const string HoldMissImageName = "HoldMissImage";
    const string MissShatterImageName = "MissShatterImage";

    [SerializeField] RectTransform unitRect;
    [SerializeField] Image unitImage;
    [SerializeField] RectTransform heatRight;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Image reinforcedImage;
    [SerializeField] Image holdMissImage;
    [SerializeField] Image missShatterImage;

    OSHeartHealToolWindowController owner;
    OSHeartHealBeatData beatData;
    Material originalUnitMaterial;
    Material electricMaterial;
    Material reinforcedBreakMaterial;
    Material holdMissBreakMaterial;
    Material missShatterMaterial;
    Sequence feedbackSequence;
    Sequence reinforcedBreakSequence;
    Sequence holdMissSequence;
    Sequence missShatterSequence;
    Tween holdLoopFadeTween;
    AudioSource holdLoopSource;
    float startX;
    float endX;
    float hitX;
    float judgementX;
    float approachDuration;
    float exitDuration;
    float hitWindowSeconds;
    float spawnTime;
    float hitTime;
    float beatEndTime;
    Color runtimeMissColor = new Color(1f, 0.25f, 0.2f, 1f);
    OSHeartHealBeatMotionState runtimeState;
    float originalUnitWidth;
    float holdFullWidth;
    float holdTravelSpeed;
    float holdFailureStartTime;
    float holdFailureWidth;
    float nextHoldFeedbackTime;
    float reinforcedBreakTime;
    float reinforcedResolvedTime;
    float missShatterTime;
    Color originalUnitColor = Color.white;
    bool hasCapturedUnitWidth;
    bool hasCapturedUnitMaterial;
    bool isRunning;
    bool isResolved;
    bool hasReportedMiss;
    bool isHolding;
    bool hasStartedHold;
    bool holdHasFailed;
    bool reinforcedLayerBroken;
    bool lastHitBrokeReinforcedLayer;
    bool successfulFeedbackFinished;
    bool missShatterStarted;
    bool missShatterFinished;
    bool missShatterUsesUnitImage;
    bool holdLoopFadeStarted;
    bool holdLoopEndPlayed;
    bool continuesRestoredHold;

    public RectTransform RectTransform => unitRect;
    public bool IsAvailable => !isRunning;
    public bool IsHoldBeat => beatData != null
        && beatData.BeatType == OSHeartHealBeatType.Hold;
    public bool IsReinforcedBeat => beatData != null
        && beatData.BeatType == OSHeartHealBeatType.Reinforced;
    public OSHeartHealBeatType BeatType => beatData != null
        ? beatData.BeatType
        : OSHeartHealBeatType.Tap;
    public bool LastHitBrokeReinforcedLayer =>
        lastHitBrokeReinforcedLayer;

    internal OSHeartBeatResumeData CapturePersistentBeat(System.Collections.Generic.IReadOnlyList<OSHeartHealBeatData> chart)
    {
        if (!isRunning || beatData == null || chart == null) return null;
        int index = -1;
        for (int i = 0; i < chart.Count; i++) if (ReferenceEquals(chart[i], beatData)) { index = i;break; }
        if (index < 0) return null;
        return new OSHeartBeatResumeData {
            chartIndex = index, resolved = isResolved, reportedMiss = hasReportedMiss,
            holding = isHolding, startedHold = hasStartedHold, holdFailed = holdHasFailed,
            reinforcedBroken = reinforcedLayerBroken, holdFailureTime = holdFailureStartTime,
            holdFailureWidth = holdFailureWidth, nextHoldFeedback = nextHoldFeedbackTime,
            reinforcedBreakTime = reinforcedBreakTime, reinforcedResolvedTime = reinforcedResolvedTime,
            missShatterTime = missShatterTime
        };
    }

    internal void RestorePersistentBeat(OSHeartBeatResumeData saved)
    {
        if (saved == null || !isRunning) return;
        isResolved = saved.resolved;
        hasReportedMiss = saved.reportedMiss;
        isHolding = saved.holding;
        hasStartedHold = saved.startedHold;
        holdHasFailed = saved.holdFailed;
        reinforcedLayerBroken = saved.reinforcedBroken;
        holdFailureStartTime = saved.holdFailureTime;
        holdFailureWidth = saved.holdFailureWidth;
        nextHoldFeedbackTime = saved.nextHoldFeedback;
        reinforcedBreakTime = saved.reinforcedBreakTime;
        reinforcedResolvedTime = saved.reinforcedResolvedTime;
        missShatterTime = saved.missShatterTime;
        // The press which started this hold was already paid for. Resume that one
        // gesture through its tail; no key is physically held across process exit.
        continuesRestoredHold = isHolding;
        if (isHolding) StartHoldLoopAudio();
        if (reinforcedLayerBroken && reinforcedImage != null) reinforcedImage.gameObject.SetActive(false);
        float elapsed = owner != null ? owner.TreatmentElapsedTime : 0f;
        UpdatePosition(elapsed);
        if (IsHoldBeat) UpdateHoldWidth(elapsed);
        UpdateRuntimeState(elapsed);
    }
    bool IsFastBeat => beatData != null
        && beatData.MotionState == OSHeartHealBeatMotionState.Fast
        && !IsHoldBeat;
    public OSHeartHealBeatMotionState RuntimeState => runtimeState;
    public float TimingErrorSeconds => owner != null && beatData != null
        ? IsReinforcedBeat && reinforcedLayerBroken
            ? 0f
            : Mathf.Abs(owner.TreatmentElapsedTime - hitTime)
        : float.MaxValue;
    public Vector2 ImpactAnchoredPosition
    {
        get
        {
            Vector2 position = unitRect != null
                ? unitRect.anchoredPosition
                : Vector2.zero;
            if (beatData != null)
                position.x = judgementX;
            return position;
        }
    }
    public bool CanBeHit
    {
        get
        {
            if (!isRunning
                || isResolved
                || isHolding
                || owner == null)
            {
                return false;
            }

            float treatmentTime = owner.TreatmentElapsedTime;
            if (IsHoldBeat)
            {
                return !hasStartedHold
                       && !holdHasFailed
                       && treatmentTime >= hitTime - hitWindowSeconds
                       && treatmentTime < beatEndTime;
            }

            if (IsReinforcedBeat && reinforcedLayerBroken)
            {
                return treatmentTime >= reinforcedBreakTime
                       && treatmentTime
                       < reinforcedBreakTime
                       + beatData.ReinforcedSecondHitWindow;
            }

            return IsFastBeat
                ? treatmentTime >= hitTime
                  && treatmentTime < GetFastMissTime()
                : Mathf.Abs(treatmentTime - hitTime)
                  <= hitWindowSeconds;
        }
    }

    void Awake()
    {
        ResolveReferences();
    }

    void OnDisable()
    {
        KillTweens();
    }

    void Update()
    {
        if (!isRunning || owner == null || beatData == null)
            return;

        float treatmentTime = owner.TreatmentElapsedTime;
        UpdatePosition(treatmentTime);
        UpdateRuntimeState(treatmentTime);
        if (IsHoldBeat)
            UpdateHold(treatmentTime);
        else
            CheckForMiss(treatmentTime);

        if (hasReportedMiss
            && !missShatterStarted
            && treatmentTime >= missShatterTime)
        {
            PlayMissShatterFeedback();
        }

        if (CanReturnToPool())
            ReturnToPool(true);
    }


    void OnDestroy()
    {
        KillTweens();
        DestroyRuntimeMaterial(electricMaterial);
        DestroyRuntimeMaterial(reinforcedBreakMaterial);
        DestroyRuntimeMaterial(holdMissBreakMaterial);
        DestroyRuntimeMaterial(missShatterMaterial);
        electricMaterial = null;
        reinforcedBreakMaterial = null;
        holdMissBreakMaterial = null;
        missShatterMaterial = null;
    }

    public void PrepareForPool()
    {
        ResolveReferences();
        KillTweens();
        owner = null;
        beatData = null;
        runtimeState = OSHeartHealBeatMotionState.Normal;
        isRunning = false;
        isResolved = false;
        hasReportedMiss = false;
        isHolding = false;
        hasStartedHold = false;
        holdHasFailed = false;
        reinforcedLayerBroken = false;
        lastHitBrokeReinforcedLayer = false;
        reinforcedBreakTime = 0f;
        reinforcedResolvedTime = 0f;
        missShatterTime = 0f;
        nextHoldFeedbackTime = 0f;
        holdLoopFadeStarted = false;
        holdLoopEndPlayed = false;
        successfulFeedbackFinished = false;
        missShatterStarted = false;
        missShatterFinished = false;
        ResetVisuals();
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void Spawn(
        OSHeartHealToolWindowController controller,
        OSHeartHealBeatData data,
        float spawnPositionX,
        float endPositionX,
        float hitPositionX,
        float approachTime,
        float exitTime,
        float hitWindow,
        Color electricColor,
        Color missColor)
    {
        ResolveReferences();
        KillTweens();
        if (gameObject.activeSelf)
            gameObject.SetActive(false);

        owner = controller;
        beatData = data;
        judgementX = hitPositionX;
        float pivotX = unitRect != null ? unitRect.pivot.x : 0.5f;
        float tapPivotOffset = hasCapturedUnitWidth
            ? (pivotX - 0.5f) * originalUnitWidth
            : 0f;
        bool alignLeftEdge = data != null
                             && data.BeatType == OSHeartHealBeatType.Hold;
        startX = alignLeftEdge
            ? spawnPositionX
            : spawnPositionX + tapPivotOffset;
        endX = alignLeftEdge
            ? endPositionX
            : endPositionX + tapPivotOffset;
        hitX = alignLeftEdge
            ? hitPositionX
            : hitPositionX + tapPivotOffset;
        approachDuration = beatData != null
            ? beatData.GetApproachDuration(approachTime)
            : Mathf.Max(0.1f, approachTime);
        exitDuration = Mathf.Max(0.05f, exitTime);
        hitWindowSeconds = Mathf.Max(0.01f, hitWindow);
        spawnTime = beatData != null ? beatData.SpawnTime : 0f;
        hitTime = beatData != null
            ? beatData.SpawnTime + approachDuration
            : spawnTime + approachDuration;
        beatEndTime = beatData != null
            ? hitTime + beatData.HoldDuration
            : hitTime;
        runtimeState = OSHeartHealBeatMotionState.Normal;
        isRunning = true;
        continuesRestoredHold = false;
        isResolved = false;
        hasReportedMiss = false;
        isHolding = false;
        hasStartedHold = false;
        holdHasFailed = false;
        reinforcedLayerBroken = false;
        lastHitBrokeReinforcedLayer = false;
        reinforcedBreakTime = 0f;
        reinforcedResolvedTime = 0f;
        missShatterTime = 0f;
        nextHoldFeedbackTime = 0f;
        holdLoopFadeStarted = false;
        holdLoopEndPlayed = false;
        successfulFeedbackFinished = false;
        missShatterStarted = false;
        missShatterFinished = false;
        runtimeMissColor = missColor;
        ResetVisuals();
        ConfigureElectricMaterial(electricColor);
        ConfigureHoldVisual();
        ConfigureReinforcedVisual();

        float treatmentTime = controller != null
            ? controller.TreatmentElapsedTime
            : 0f;
        UpdatePosition(treatmentTime);
        UpdateRuntimeState(treatmentTime);
        gameObject.SetActive(true);
    }

    public bool ResolveHit()
    {
        lastHitBrokeReinforcedLayer = false;
        if (!CanBeHit)
            return false;

        if (IsHoldBeat)
        {
            float treatmentTime = owner != null
                ? owner.TreatmentElapsedTime
                : hitTime;
            if (treatmentTime > hitTime + hitWindowSeconds)
                SplitMissedHoldPrefix(treatmentTime);

            hasStartedHold = true;
            isHolding = true;
            holdHasFailed = false;
            SetPositionX(hitX);
            UpdateHoldWidth(treatmentTime);
            nextHoldFeedbackTime = owner != null
                ? treatmentTime
                    + Mathf.Max(0.02f, beatData.HoldFeedbackInterval)
                : hitTime;
            StartHoldLoopAudio();
            owner?.ReportHoldTick(this);
            PlayHitFeedback(false);
            return true;
        }

        if (IsReinforcedBeat && !reinforcedLayerBroken)
        {
            reinforcedLayerBroken = true;
            reinforcedBreakTime = owner != null
                ? owner.TreatmentElapsedTime
                : hitTime;
            lastHitBrokeReinforcedLayer = true;
            SetPositionX(hitX);
            PlayReinforcedBreakFeedback();
            return true;
        }

        isResolved = true;
        successfulFeedbackFinished = false;
        reinforcedResolvedTime = owner != null
            ? owner.TreatmentElapsedTime
            : hitTime;
        SetPositionX(hitX);
        owner?.ReportHit(this);
        PlayHitFeedback(true);
        return true;
    }

    public void PreviewHit(Color electricColor)
    {
        ResolveReferences();
        KillTweens();
        owner = null;
        isRunning = false;
        isResolved = false;
        hasReportedMiss = false;
        isHolding = false;
        hasStartedHold = false;
        holdHasFailed = false;
        reinforcedLayerBroken = false;
        lastHitBrokeReinforcedLayer = false;
        nextHoldFeedbackTime = 0f;
        holdLoopFadeStarted = false;
        holdLoopEndPlayed = false;
        successfulFeedbackFinished = false;
        missShatterStarted = false;
        missShatterFinished = false;
        gameObject.SetActive(true);
        ResetVisuals();
        ConfigureElectricMaterial(electricColor);
        PlayHitFeedback(false);
    }

    public void StopPreview()
    {
        KillTweens();
        ResetVisuals();
    }

    public void ReturnToPool(bool notifyOwner)
    {
        if (!isRunning)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        OSHeartHealToolWindowController previousOwner = owner;
        isRunning = false;
        isResolved = false;
        hasReportedMiss = false;
        isHolding = false;
        hasStartedHold = false;
        holdHasFailed = false;
        reinforcedLayerBroken = false;
        lastHitBrokeReinforcedLayer = false;
        reinforcedBreakTime = 0f;
        reinforcedResolvedTime = 0f;
        missShatterTime = 0f;
        nextHoldFeedbackTime = 0f;
        holdLoopFadeStarted = false;
        holdLoopEndPlayed = false;
        successfulFeedbackFinished = false;
        missShatterStarted = false;
        missShatterFinished = false;
        owner = null;
        beatData = null;
        runtimeState = OSHeartHealBeatMotionState.Normal;
        KillTweens();
        ResetVisuals();
        gameObject.SetActive(false);
        if (notifyOwner)
            previousOwner?.ReleaseBeatUnit(this);
    }

    void CheckForMiss(float treatmentTime)
    {
        float missTime;
        if (IsFastBeat)
        {
            missTime = GetFastMissTime();
        }
        else if (IsReinforcedBeat && reinforcedLayerBroken)
        {
            missTime = reinforcedBreakTime
                       + beatData.ReinforcedSecondHitWindow;
        }
        else
        {
            missTime = hitTime + hitWindowSeconds;
        }

        if (!isRunning
            || isResolved
            || hasReportedMiss
            || beatData == null
            || treatmentTime < missTime)
        {
            return;
        }

        ResolveMiss();
    }

    void UpdateHold(float treatmentTime)
    {
        UpdateHoldWidth(treatmentTime);
        if (!isRunning || isResolved || beatData == null)
            return;

        if (isHolding)
        {
            BeginHoldLoopEnding(treatmentTime);
            if (treatmentTime >= beatEndTime)
            {
                isHolding = false;
                isResolved = true;
                successfulFeedbackFinished = false;
                SetPositionX(hitX);
                CompleteHoldLoopAudio();
                owner?.ReportHoldCompleted(this);
                PlayHitFeedback(true);
                return;
            }

            if ((owner == null || !owner.IsShockHeld) && !continuesRestoredHold)
            {
                isHolding = false;
                holdHasFailed = true;
                FadeOutHoldLoopAudio(HoldLoopFadeDuration);
                holdFailureStartTime = treatmentTime;
                holdFailureWidth = unitRect != null
                    ? unitRect.rect.width
                    : originalUnitWidth;
                return;
            }

            float interval = Mathf.Max(
                0.02f,
                beatData.HoldFeedbackInterval);
            while (treatmentTime >= nextHoldFeedbackTime
                   && nextHoldFeedbackTime < beatEndTime)
            {
                owner.ReportHoldTick(this);
                nextHoldFeedbackTime += interval;
            }
            return;
        }

        if (treatmentTime >= beatEndTime + exitDuration)
            ResolveMiss();
    }

    void ResolveMiss()
    {
        if (isResolved || hasReportedMiss)
            return;

        hasReportedMiss = true;
        isResolved = true;
        isHolding = false;
        successfulFeedbackFinished = false;
        missShatterStarted = false;
        missShatterFinished = false;
        missShatterTime = GetMissShatterTime(
            owner != null ? owner.TreatmentElapsedTime : beatEndTime);
        FadeOutHoldLoopAudio(HoldLoopFadeDuration);
        PlayMissFeedback();
        owner?.ReportMiss(this);
    }

    void UpdatePosition(float treatmentTime)
    {
        if (unitRect == null || beatData == null)
            return;

        if (isResolved && !hasReportedMiss)
        {
            SetPositionX(hitX);
            return;
        }

        if (IsHoldBeat)
        {
            UpdateHoldPosition(treatmentTime);
            return;
        }

        if (IsFastBeat)
        {
            UpdateFastPosition(treatmentTime);
            return;
        }

        if (IsReinforcedBeat && reinforcedLayerBroken)
        {
            UpdateReinforcedPosition(treatmentTime);
            return;
        }

        float positionX;
        if (treatmentTime <= hitTime)
        {
            float approachElapsed = Mathf.Clamp(
                treatmentTime - spawnTime,
                0f,
                approachDuration);
            float progress = EvaluateApproachProgress(approachElapsed);
            positionX = Mathf.LerpUnclamped(startX, hitX, progress);
        }
        else
        {
            float progress = Mathf.Clamp01(
                (treatmentTime - beatEndTime) / exitDuration);
            positionX = Mathf.LerpUnclamped(hitX, endX, progress);
        }

        SetPositionX(positionX);
    }

    void UpdateHoldPosition(float treatmentTime)
    {
        if (treatmentTime <= hitTime && !hasStartedHold)
        {
            float approachElapsed = Mathf.Clamp(
                treatmentTime - spawnTime,
                0f,
                approachDuration);
            SetPositionX(Mathf.LerpUnclamped(
                startX,
                hitX,
                EvaluateApproachProgress(approachElapsed)));
            return;
        }

        if (isHolding)
        {
            SetPositionX(hitX);
            return;
        }

        if (isResolved && hasStartedHold && !holdHasFailed)
        {
            float exitProgress = Mathf.Clamp01(
                (treatmentTime - beatEndTime) / exitDuration);
            SetPositionX(Mathf.LerpUnclamped(
                hitX,
                endX,
                exitProgress));
            return;
        }

        float moveStartTime = holdHasFailed
            ? holdFailureStartTime
            : hitTime;
        float travelElapsed = Mathf.Max(0f, treatmentTime - moveStartTime);
        float direction = Mathf.Sign(hitX - startX);
        SetPositionX(hitX + direction * holdTravelSpeed * travelElapsed);
    }

    void UpdateReinforcedPosition(float treatmentTime)
    {
        float leaveTime;
        if (hasReportedMiss)
        {
            leaveTime = reinforcedBreakTime
                        + beatData.ReinforcedSecondHitWindow;
        }
        else if (isResolved)
        {
            leaveTime = reinforcedResolvedTime;
        }
        else
        {
            leaveTime = reinforcedBreakTime
                        + beatData.ReinforcedSecondHitWindow;
        }

        if (treatmentTime <= leaveTime)
        {
            SetPositionX(hitX);
            return;
        }

        float progress = Mathf.Clamp01(
            (treatmentTime - leaveTime) / exitDuration);
        SetPositionX(Mathf.LerpUnclamped(hitX, endX, progress));
    }

    void UpdateFastPosition(float treatmentTime)
    {
        float positionX;
        if (treatmentTime <= hitTime)
        {
            float approachElapsed = Mathf.Clamp(
                treatmentTime - spawnTime,
                0f,
                approachDuration);
            float progress = EvaluateApproachProgress(approachElapsed);
            positionX = Mathf.LerpUnclamped(startX, hitX, progress);
        }
        else if (hasReportedMiss
                 || (!isResolved && treatmentTime >= GetFastMissTime()))
        {
            positionX = GetFastMissPositionX();
        }
        else
        {
            positionX = hitX;
        }

        SetPositionX(positionX);
    }

    void SetPositionX(float positionX)
    {
        Vector2 position = unitRect.anchoredPosition;
        position.x = positionX;
        unitRect.anchoredPosition = position;
    }

    float EvaluateApproachProgress(float elapsed)
    {
        float progress = Mathf.Clamp01(elapsed / approachDuration);
        if (!IsFastBeat)
        {
            return progress;
        }

        if (progress >= 1f)
            return 1f;

        int flashIndex = Mathf.FloorToInt(
            progress * (FastFlashCount - 1));
        return flashIndex / (float)(FastFlashCount - 1);
    }

    float GetFastMissTime()
    {
        return hitTime
               + approachDuration / (FastFlashCount - 1);
    }

    float GetFastMissPositionX()
    {
        float flashDistance = (hitX - startX) / (FastFlashCount - 1);
        return hitX + flashDistance;
    }

    float GetMissShatterTime(float resolutionTime)
    {
        if (IsFastBeat)
            return resolutionTime;
        if (IsHoldBeat)
            return resolutionTime;
        if (IsReinforcedBeat && reinforcedLayerBroken)
            return resolutionTime + exitDuration;

        return Mathf.Max(
            resolutionTime,
            beatEndTime + exitDuration);
    }

    bool CanReturnToPool()
    {
        if (!isResolved || holdMissSequence != null)
            return false;

        return hasReportedMiss
            ? missShatterFinished
            : successfulFeedbackFinished;
    }

    void UpdateRuntimeState(float treatmentTime)
    {
        OSHeartHealBeatMotionState nextState =
            OSHeartHealBeatMotionState.Normal;
        float alpha = 1f;
        if (!isResolved
            && beatData != null
            && beatData.MotionState != OSHeartHealBeatMotionState.Normal
            && IsInsideSpecialState(treatmentTime, out float stateProgress))
        {
            nextState = beatData.MotionState;
            if (nextState == OSHeartHealBeatMotionState.Hidden)
                alpha = EvaluateHiddenAlpha(stateProgress);
        }

        runtimeState = nextState;
        SetCanvasAlpha(alpha);
    }

    bool IsInsideSpecialState(
        float treatmentTime,
        out float stateProgress)
    {
        float stateStart = hitTime
            - beatData.SpecialStateLeadTime;
        float duration = beatData.SpecialStateDuration;
        if (duration <= 0f
            || treatmentTime < stateStart
            || treatmentTime > stateStart + duration)
        {
            stateProgress = 0f;
            return false;
        }

        stateProgress = Mathf.Clamp01(
            (treatmentTime - stateStart) / duration);
        return true;
    }

    float EvaluateHiddenAlpha(float stateProgress)
    {
        const float fadePortion = 0.25f;
        float visibility;
        if (stateProgress < fadePortion)
        {
            visibility = stateProgress / fadePortion;
        }
        else if (stateProgress > 1f - fadePortion)
        {
            visibility = (1f - stateProgress) / fadePortion;
        }
        else
        {
            visibility = 1f;
        }

        return Mathf.Lerp(1f, beatData.HiddenAlpha, visibility);
    }

    void SetCanvasAlpha(float alpha)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = Mathf.Clamp01(alpha);
    }

    void ConfigureHoldVisual()
    {
        if (!IsHoldBeat || unitRect == null || !hasCapturedUnitWidth)
            return;

        holdTravelSpeed = Mathf.Abs(startX - hitX)
                          / Mathf.Max(0.1f, approachDuration);
        holdFullWidth = originalUnitWidth
                        + holdTravelSpeed * beatData.HoldDuration;
        holdFailureStartTime = 0f;
        holdFailureWidth = holdFullWidth;
        SetUnitWidth(holdFullWidth);
    }

    void UpdateHoldWidth(float treatmentTime)
    {
        if (!IsHoldBeat || unitRect == null || !hasCapturedUnitWidth)
            return;

        if (!hasStartedHold)
        {
            SetUnitWidth(holdFullWidth);
            return;
        }

        if (holdHasFailed)
        {
            SetUnitWidth(Mathf.Max(originalUnitWidth, holdFailureWidth));
            return;
        }

        float remainingDuration = Mathf.Max(
            0f,
            beatEndTime - treatmentTime);
        remainingDuration = Mathf.Min(
            remainingDuration,
            beatData.HoldDuration);
        SetUnitWidth(originalUnitWidth
                     + holdTravelSpeed * remainingDuration);
    }

    void SplitMissedHoldPrefix(float treatmentTime)
    {
        if (!IsHoldBeat || holdTravelSpeed <= 0f)
            return;

        float missedDuration = Mathf.Clamp(
            treatmentTime - hitTime,
            0f,
            beatData.HoldDuration);
        float missedWidth = holdTravelSpeed * missedDuration;
        if (missedWidth <= 0.01f)
            return;

        KillHoldMissSequence();
        Image image = EnsureRuntimeImage(
            ref holdMissImage,
            HoldMissImageName);
        if (image == null)
            return;

        ConfigureImageFromUnit(image);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(-missedWidth, 0f);
        rect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            missedWidth);
        rect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            unitRect.rect.height);
        rect.localScale = Vector3.one;
        image.color = unitImage != null
            ? unitImage.color
            : Color.white;
        image.material = originalUnitMaterial;
        image.gameObject.SetActive(true);

        ConfigureBreakMaterial(
            ref holdMissBreakMaterial,
            image,
            "Hold Miss Break",
            0.317f);

        Sequence sequence = DOTween.Sequence();
        holdMissSequence = sequence;
        float targetX = rect.anchoredPosition.x + endX - hitX;
        sequence.Append(rect
            .DOAnchorPosX(targetX, exitDuration)
            .SetEase(Ease.Linear));
        sequence.AppendCallback(() =>
            BeginHoldMissShatter(sequence));
        sequence.Append(DOVirtual.Float(
            0f,
            1f,
            BreakFeedbackDuration,
            value => holdMissBreakMaterial?.SetFloat(
                "_BreakProgress",
                value)));
        sequence.Join(image
            .DOFade(0f, BreakFeedbackDuration)
            .SetEase(Ease.InQuad));
        sequence.Join(rect
            .DOScale(1.08f, BreakFeedbackDuration)
            .SetEase(Ease.OutCubic));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() => FinishHoldMissSequence(sequence))
            .OnKill(() => FinishHoldMissSequence(sequence));
    }

    void BeginHoldMissShatter(Sequence sequence)
    {
        if (holdMissSequence != sequence || holdMissImage == null)
            return;

        if (isRunning && owner != null)
        {
            owner.ReportHoldSegmentMiss(
                this,
                holdMissImage.rectTransform);
        }

        Color missColor = runtimeMissColor;
        missColor.a = originalUnitColor.a;
        holdMissImage.color = missColor;
        holdMissImage.material = holdMissBreakMaterial;
        holdMissBreakMaterial?.SetFloat("_BreakProgress", 0f);
    }

    void FinishHoldMissSequence(Sequence sequence)
    {
        if (holdMissSequence != sequence)
            return;

        holdMissSequence = null;
        if (holdMissImage != null)
            holdMissImage.gameObject.SetActive(false);
    }

    void ConfigureReinforcedVisual()
    {
        if (!IsReinforcedBeat || unitRect == null || unitImage == null)
            return;

        Image image = EnsureRuntimeImage(
            ref reinforcedImage,
            ReinforcedImageName);
        if (image == null)
            return;

        ConfigureImageFromUnit(image);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            originalUnitWidth);
        rect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            unitRect.rect.height);
        rect.localScale = Vector3.one * beatData.ReinforcedOuterScale;
        image.color = unitImage.color;
        ConfigureReinforcedBreakMaterial();
        image.material = reinforcedBreakMaterial;
        image.gameObject.SetActive(true);
    }

    void PlayReinforcedBreakFeedback()
    {
        if (reinforcedImage == null)
            return;

        KillReinforcedBreakSequence();
        reinforcedImage.gameObject.SetActive(true);
        Color color = unitImage != null ? unitImage.color : Color.white;
        color.a = 1f;
        reinforcedImage.color = color;
        reinforcedBreakMaterial?.SetFloat("_BreakProgress", 0f);

        Sequence sequence = DOTween.Sequence();
        reinforcedBreakSequence = sequence;
        sequence.Join(DOVirtual.Float(
            0f,
            1f,
            BreakFeedbackDuration,
            value => reinforcedBreakMaterial?.SetFloat(
                "_BreakProgress",
                value)));
        sequence.Join(reinforcedImage
            .DOFade(0f, BreakFeedbackDuration)
            .SetEase(Ease.InQuad));
        sequence.Join(reinforcedImage.rectTransform
            .DOScale(
                beatData.ReinforcedOuterScale * 1.08f,
                BreakFeedbackDuration)
            .SetEase(Ease.OutCubic));
        sequence
            .SetUpdate(true)
            .OnComplete(() => FinishReinforcedBreakSequence(sequence))
            .OnKill(() => FinishReinforcedBreakSequence(sequence));
    }

    void FinishReinforcedBreakSequence(Sequence sequence)
    {
        if (reinforcedBreakSequence != sequence)
            return;

        reinforcedBreakSequence = null;
        if (reinforcedImage != null)
            reinforcedImage.gameObject.SetActive(false);
    }

    void ConfigureReinforcedBreakMaterial()
    {
        ConfigureBreakMaterial(
            ref reinforcedBreakMaterial,
            reinforcedImage,
            "Reinforced Break",
            0f);
    }

    void ConfigureBreakMaterial(
        ref Material material,
        Image sourceImage,
        string materialLabel,
        float seedOffset)
    {
        if (sourceImage == null || material != null)
            return;

        Shader shader = owner != null
            ? owner.ReinforcedBreakShader
            : null;
        if (shader == null)
            shader = Shader.Find("UI/HeartHealReinforcedBreak");
        if (shader == null)
            return;

        material = new Material(shader)
        {
            name = $"{name} {materialLabel} (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        material.SetFloat(
            "_Seed",
            (transform.GetSiblingIndex() + 1) * 0.173f
            + seedOffset);
        Vector4 spriteUvRect = new Vector4(0f, 0f, 1f, 1f);
        if (sourceImage.sprite != null)
        {
            Vector4 outerUv = UnityEngine.Sprites.DataUtility.GetOuterUV(
                sourceImage.sprite);
            spriteUvRect = new Vector4(
                outerUv.x,
                outerUv.y,
                Mathf.Max(0.0001f, outerUv.z - outerUv.x),
                Mathf.Max(0.0001f, outerUv.w - outerUv.y));
        }
        material.SetVector("_SpriteUvRect", spriteUvRect);
        material.SetFloat("_BreakProgress", 0f);
    }

    void PlayMissShatterFeedback()
    {
        if (missShatterStarted)
            return;

        missShatterStarted = true;
        missShatterFinished = false;
        KillFeedbackSequence();
        KillMissShatterSequence();

        bool useUnitImage = BeatType == OSHeartHealBeatType.Tap;
        Image image = useUnitImage
            ? unitImage
            : EnsureRuntimeImage(
                ref missShatterImage,
                MissShatterImageName);
        if (image == null || unitRect == null || unitImage == null)
        {
            HidePrimaryVisuals();
            missShatterFinished = true;
            return;
        }

        missShatterUsesUnitImage = useUnitImage;
        RectTransform rect = useUnitImage
            ? unitRect
            : image.rectTransform;
        if (!useUnitImage)
        {
            ConfigureImageFromUnit(image);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                unitRect.rect.width);
            rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                unitRect.rect.height);
        }
        rect.localScale = Vector3.one;
        Color missColor = runtimeMissColor;
        missColor.a = originalUnitColor.a;
        image.color = missColor;
        ConfigureBreakMaterial(
            ref missShatterMaterial,
            image,
            "Miss Shatter",
            0.631f);
        image.material = missShatterMaterial;
        missShatterMaterial?.SetFloat("_BreakProgress", 0f);
        if (useUnitImage)
        {
            image.enabled = true;
            if (heatRight != null)
                heatRight.gameObject.SetActive(false);
            if (reinforcedImage != null)
                reinforcedImage.gameObject.SetActive(false);
        }
        else
        {
            image.transform.SetAsLastSibling();
            image.gameObject.SetActive(true);
            HidePrimaryVisuals();
        }

        Sequence sequence = DOTween.Sequence();
        missShatterSequence = sequence;
        sequence.Join(DOVirtual.Float(
            0f,
            1f,
            BreakFeedbackDuration,
            value => missShatterMaterial?.SetFloat(
                "_BreakProgress",
                value)));
        sequence.Join(image
            .DOFade(0f, BreakFeedbackDuration)
            .SetEase(Ease.InQuad));
        sequence.Join(rect
            .DOScale(1.08f, BreakFeedbackDuration)
            .SetEase(Ease.OutCubic));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
                FinishMissShatterSequence(sequence, true))
            .OnKill(() =>
                FinishMissShatterSequence(sequence, false));
    }

    void FinishMissShatterSequence(
        Sequence sequence,
        bool completed)
    {
        if (missShatterSequence != sequence)
            return;

        missShatterSequence = null;
        if (missShatterUsesUnitImage)
        {
            if (unitImage != null)
                unitImage.enabled = false;
        }
        else if (missShatterImage != null)
        {
            missShatterImage.gameObject.SetActive(false);
        }
        missShatterUsesUnitImage = false;
        if (completed)
            missShatterFinished = true;
    }

    void ConfigureImageFromUnit(Image image)
    {
        if (image == null || unitImage == null)
            return;

        image.sprite = unitImage.sprite;
        image.type = unitImage.type;
        image.preserveAspect = unitImage.preserveAspect;
        image.fillCenter = unitImage.fillCenter;
        image.fillMethod = unitImage.fillMethod;
        image.fillAmount = unitImage.fillAmount;
        image.fillClockwise = unitImage.fillClockwise;
        image.fillOrigin = unitImage.fillOrigin;
        image.pixelsPerUnitMultiplier = unitImage.pixelsPerUnitMultiplier;
        image.raycastTarget = false;
        image.maskable = unitImage.maskable;
    }


    void PlayHitFeedback(bool hideOnComplete)
    {
        KillFeedbackSequence();
        Sequence sequence = DOTween.Sequence();
        feedbackSequence = sequence;
        SetElectricAmount(0f);
        sequence.Append(DOVirtual.Float(
            0f,
            1f,
            0.06f,
            SetElectricAmount));
        sequence.Join(unitRect
            .DOScale(1.25f, 0.08f)
            .SetEase(Ease.OutCubic));
        if (heatRight != null)
        {
            sequence.Join(heatRight
                .DOScale(1.1f, 0.08f)
                .SetEase(Ease.OutCubic));
        }

        sequence.Append(DOVirtual.Float(
            1f,
            0f,
            0.12f,
            SetElectricAmount));
        sequence.Join(unitRect
            .DOScale(1f, 0.12f)
            .SetEase(Ease.OutCubic));
        if (heatRight != null)
        {
            sequence.Join(heatRight
                .DOScale(1f, 0.12f)
                .SetEase(Ease.OutCubic));
        }

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (feedbackSequence != sequence)
                    return;

                feedbackSequence = null;
                SetElectricAmount(0f);
                if (hideOnComplete)
                {
                    HidePrimaryVisuals();
                    successfulFeedbackFinished = true;
                }
            })
            .OnKill(() =>
            {
                if (feedbackSequence == sequence)
                    feedbackSequence = null;
                SetElectricAmount(0f);
            });
    }

    void PlayMissFeedback()
    {
        KillFeedbackSequence();
        if (unitImage == null)
            return;

        Color original = unitImage.color;
        Sequence sequence = DOTween.Sequence();
        feedbackSequence = sequence;
        Color targetColor = runtimeMissColor;
        targetColor.a = original.a;
        sequence.Append(unitImage
            .DOColor(targetColor, 0.05f)
            .SetEase(Ease.Linear));
        sequence.Append(unitImage
            .DOColor(original, 0.12f)
            .SetEase(Ease.Linear));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (feedbackSequence == sequence)
                    feedbackSequence = null;
            });
    }

    void ConfigureElectricMaterial(Color color)
    {
        if (unitImage == null)
            return;

        if (electricMaterial == null)
        {
            Shader shader = Shader.Find("UI/HeartHealElectric");
            if (shader == null)
                return;

            electricMaterial = new Material(shader)
            {
                name = $"{name} Electric (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            unitImage.material = electricMaterial;
        }

        electricMaterial.SetColor("_ElectricColor", color * 2.2f);
        electricMaterial.SetFloat("_ElectricAmount", 0f);
    }

    void SetElectricAmount(float amount)
    {
        electricMaterial?.SetFloat(
            "_ElectricAmount",
            Mathf.Clamp01(amount));
    }

    void ResolveReferences()
    {
        if (unitRect == null)
            unitRect = transform as RectTransform;
        if (unitImage == null)
            unitImage = GetComponent<Image>();
        if (!hasCapturedUnitMaterial && unitImage != null)
        {
            hasCapturedUnitMaterial = true;
            originalUnitMaterial = unitImage.material;
            originalUnitColor = unitImage.color;
        }
        if (heatRight == null)
            heatRight = transform.Find("HeatRight") as RectTransform;
        if (reinforcedImage == null)
        {
            reinforcedImage = transform.Find(ReinforcedImageName)
                ?.GetComponent<Image>();
        }
        if (holdMissImage == null)
        {
            holdMissImage = transform.Find(HoldMissImageName)
                ?.GetComponent<Image>();
        }
        if (missShatterImage == null)
        {
            missShatterImage = transform.Find(MissShatterImageName)
                ?.GetComponent<Image>();
        }
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null && Application.isPlaying)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        CaptureUnitWidth();
    }

    void StartHoldLoopAudio()
    {
        StopHoldLoopAudioImmediate();
        holdLoopFadeStarted = false;
        holdLoopEndPlayed = false;
        holdLoopSource = owner?.StartHoldLoopSfx();
    }

    void BeginHoldLoopEnding(float treatmentTime)
    {
        if (holdLoopFadeStarted
            || beatEndTime - treatmentTime > HoldLoopFadeDuration)
        {
            return;
        }

        holdLoopFadeStarted = true;
        FadeOutHoldLoopAudio(Mathf.Max(
            0f,
            beatEndTime - treatmentTime));
    }

    void CompleteHoldLoopAudio()
    {
        StopHoldLoopAudioImmediate();
        if (holdLoopEndPlayed)
            return;

        holdLoopEndPlayed = true;
        owner?.PlayHoldLoopEndSfx();
    }

    void FadeOutHoldLoopAudio(float duration)
    {
        if (holdLoopFadeTween != null)
        {
            Tween previousTween = holdLoopFadeTween;
            holdLoopFadeTween = null;
            previousTween.Kill(false);
        }

        AudioSource source = holdLoopSource;
        if (source == null)
            return;

        float safeDuration = Mathf.Max(0f, duration);
        if (safeDuration <= 0f
            || !source.isPlaying
            || !gameObject.activeInHierarchy)
        {
            holdLoopSource = null;
            ResetLoopingSfxSource(source);
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
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        holdLoopFadeTween = tween;
        tween.OnComplete(() =>
            FinishHoldLoopFade(tween, source));
        tween.OnKill(() =>
            FinishHoldLoopFade(tween, source));
    }

    void FinishHoldLoopFade(Tween tween, AudioSource source)
    {
        if (holdLoopFadeTween != tween)
            return;

        holdLoopFadeTween = null;
        if (holdLoopSource == source)
            holdLoopSource = null;
        ResetLoopingSfxSource(source);
    }

    void StopHoldLoopAudioImmediate()
    {
        if (holdLoopFadeTween != null)
        {
            Tween tween = holdLoopFadeTween;
            holdLoopFadeTween = null;
            tween.Kill(false);
        }

        AudioSource source = holdLoopSource;
        holdLoopSource = null;
        ResetLoopingSfxSource(source);
    }

    static void ResetLoopingSfxSource(AudioSource source)
    {
        if (source == null)
            return;

        source.loop = false;
        source.Stop();
        source.clip = null;
        source.volume = 1f;
        source.pitch = 1f;
    }

    void ResetVisuals()
    {
        if (unitImage != null)
        {
            unitImage.enabled = true;
            unitImage.color = originalUnitColor;
            unitImage.material = electricMaterial != null
                ? electricMaterial
                : originalUnitMaterial;
        }
        if (unitRect != null)
        {
            unitRect.localScale = Vector3.one;
            if (hasCapturedUnitWidth)
                SetUnitWidth(originalUnitWidth);
        }
        if (heatRight != null)
        {
            heatRight.gameObject.SetActive(true);
            heatRight.localScale = Vector3.one;
        }
        if (reinforcedImage != null)
        {
            reinforcedImage.gameObject.SetActive(false);
            reinforcedImage.color = originalUnitColor;
            reinforcedImage.rectTransform.localScale = Vector3.one;
            reinforcedBreakMaterial?.SetFloat("_BreakProgress", 0f);
        }
        if (holdMissImage != null)
        {
            holdMissImage.gameObject.SetActive(false);
            holdMissImage.rectTransform.localScale = Vector3.one;
            holdMissBreakMaterial?.SetFloat("_BreakProgress", 0f);
        }
        if (missShatterImage != null)
        {
            missShatterImage.gameObject.SetActive(false);
            missShatterImage.rectTransform.localScale = Vector3.one;
            missShatterMaterial?.SetFloat("_BreakProgress", 0f);
        }
        holdFullWidth = 0f;
        holdTravelSpeed = 0f;
        holdFailureStartTime = 0f;
        holdFailureWidth = 0f;
        missShatterUsesUnitImage = false;
        SetCanvasAlpha(1f);
        SetElectricAmount(0f);
    }

    void HidePrimaryVisuals()
    {
        if (unitImage != null)
            unitImage.enabled = false;
        if (heatRight != null)
            heatRight.gameObject.SetActive(false);
        if (reinforcedImage != null)
            reinforcedImage.gameObject.SetActive(false);
    }

    void CaptureUnitWidth()
    {
        if (hasCapturedUnitWidth || unitRect == null)
            return;

        hasCapturedUnitWidth = true;
        originalUnitWidth = unitRect.rect.width;
    }

    void SetUnitWidth(float width)
    {
        if (unitRect == null)
            return;

        unitRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Max(0f, width));
    }

    Image EnsureRuntimeImage(ref Image image, string childName)
    {
        if (image != null)
            return image;

        Transform child = transform.Find(childName);
        if (child != null)
            image = child.GetComponent<Image>();
        if (image != null || !Application.isPlaying)
            return image;

        GameObject childObject = new GameObject(
            childName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rect = childObject.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        childObject.layer = gameObject.layer;
        image = childObject.GetComponent<Image>();
        image.raycastTarget = false;
        image.gameObject.SetActive(false);
        return image;
    }

    void KillTweens()
    {
        StopHoldLoopAudioImmediate();
        KillFeedbackSequence();
        KillReinforcedBreakSequence();
        KillHoldMissSequence();
        KillMissShatterSequence();
    }

    void KillFeedbackSequence()
    {
        if (feedbackSequence == null)
            return;

        Sequence sequence = feedbackSequence;
        feedbackSequence = null;
        sequence.Kill(false);
    }

    void KillReinforcedBreakSequence()
    {
        if (reinforcedBreakSequence == null)
            return;

        Sequence sequence = reinforcedBreakSequence;
        reinforcedBreakSequence = null;
        sequence.Kill(false);
        if (reinforcedImage != null)
            reinforcedImage.gameObject.SetActive(false);
    }

    void KillHoldMissSequence()
    {
        if (holdMissSequence == null)
            return;

        Sequence sequence = holdMissSequence;
        holdMissSequence = null;
        sequence.Kill(false);
        if (holdMissImage != null)
            holdMissImage.gameObject.SetActive(false);
    }

    void KillMissShatterSequence()
    {
        if (missShatterSequence == null)
            return;

        Sequence sequence = missShatterSequence;
        missShatterSequence = null;
        sequence.Kill(false);
        if (missShatterUsesUnitImage)
        {
            if (unitImage != null)
                unitImage.enabled = false;
        }
        else if (missShatterImage != null)
        {
            missShatterImage.gameObject.SetActive(false);
        }
        missShatterUsesUnitImage = false;
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
}
