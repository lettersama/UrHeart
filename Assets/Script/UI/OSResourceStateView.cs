using System.Collections.Generic;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSResourceStateView : MonoBehaviour
{
    const int ExpectedUnitCount = 10;
    const float UnitTransitionOffsetY = 2f;
    const float MaximumGlowDistance = 1f;
    const float MaximumGlowBounds = 9f;

    [Header("Data")]
    [SerializeField] ResourceType resourceType;
    [SerializeField] string localizationKey;
    [SerializeField] string fallbackName;

    [Header("Labels")]
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] TMP_Text valueLabel;
    [SerializeField, TextArea] string valueSuffix;

    [Header("Visuals")]
    [SerializeField] RectTransform icon;
    [SerializeField] Image iconImage;
    [SerializeField] Image[] units = new Image[0];
    [SerializeField] Sprite fullSprite;
    [SerializeField] Sprite halfSprite;
    [SerializeField] Sprite emptySprite;

    [Header("Animation")]
    [SerializeField, Min(0f)] float counterDuration = 0.15f;
    [SerializeField, Min(0f)] float unitTransitionDuration = 0.15f;
    [SerializeField, FormerlySerializedAs("iconJumpDuration"), Min(0f)]
    float iconGlowDuration = 0.15f;
    [SerializeField] Color iconGlowColor =
        new Color(1f, 1f, 1f, 0.85f);
    [SerializeField, FormerlySerializedAs("iconShakeDuration"), Min(0f)]
    float iconGlitchDuration = 0.15f;
    [SerializeField, FormerlySerializedAs("iconShakeStrength"), Min(0f)]
    float iconGlitchDistance = 1f;

    readonly Queue<ResourceAnimationRequest> animationQueue =
        new Queue<ResourceAnimationRequest>();

    Sequence activeSequence;
    Tween subtleShake;
    Vector2 positionBeforeSubtleShake;
    Outline iconGlow;
    Vector2 iconRestingPosition;
    Vector2[] unitRestingPositions = new Vector2[0];
    float[] unitRestingAlphas = new float[0];
    int displayedValue;
    int queuedValue;
    float displayMax = OSResourceSaveData.MaxValue;
    bool hasInitializedValue;
    bool hasCapturedRestingVisuals;
    bool hasValueDisplayOverride;
    string valueDisplayOverride;

    public ResourceType ResourceType => resourceType;
    public RectTransform IconRect => icon != null ? icon : transform as RectTransform;
    public bool IsSubtleShakePlaying => subtleShake != null && subtleShake.IsActive();
    public int DisplayedValue => displayedValue;
    public int QueuedValue => queuedValue;
    public bool IsAnimating => activeSequence != null;
    public int PendingAnimationCount =>
        animationQueue.Count + (activeSequence != null ? 1 : 0);

    void Awake()
    {
        Initialize();
    }

    void OnDisable()
    {
        StopAnimations();

        int value = queuedValue;
        float max = displayMax;
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            value = dataManager.GetOSResourceValue(resourceType);
            max = OSResourceSaveData.MaxValue;
        }
        else
        {
            GameResourceSystem resources = GameResourceSystem.Instance;
            if (resources != null)
            {
                value = Mathf.RoundToInt(resources.Get(resourceType));
                max = resources.GetMax(resourceType);
            }
        }

        SetVisualState(value, max);
        queuedValue = displayedValue;
    }

    void OnDestroy()
    {
        StopAnimations();
    }

    void Reset()
    {
        ResolveReferences();
        CaptureValueSuffix();
        CaptureRestingVisuals();
    }

    void OnValidate()
    {
        counterDuration = Mathf.Max(0f, counterDuration);
        unitTransitionDuration = Mathf.Max(0f, unitTransitionDuration);
        iconGlowDuration = Mathf.Max(0f, iconGlowDuration);
        iconGlitchDuration = Mathf.Max(0f, iconGlitchDuration);
        iconGlitchDistance = Mathf.Max(0f, iconGlitchDistance);

        ResolveReferences();
        CaptureValueSuffix();
        CaptureRestingVisuals();
    }

    public void Initialize()
    {
        ResolveReferences();
        CaptureValueSuffix();
        CaptureRestingVisuals();

        if (hasInitializedValue)
            return;

        displayedValue = ReadValueLabel();
        queuedValue = displayedValue;
        hasInitializedValue = true;
        SetVisualState(displayedValue, displayMax);
    }

    public void RefreshName(GameDataManager dataManager)
    {
        if (nameLabel == null)
            return;

        OSLocalizedFontUtility.ApplyCurrent(nameLabel);
        string currentFallback = string.IsNullOrEmpty(fallbackName)
            ? nameLabel.text
            : fallbackName;
        nameLabel.text = dataManager != null
            ? dataManager.GetLocalizedText(localizationKey, currentFallback)
            : currentFallback;
    }

    public void RefreshValue(float value, float max)
    {
        Initialize();
        StopAnimations();

        displayMax = SanitizeMax(max);
        int targetValue = SanitizeValue(value);
        SetVisualState(targetValue, displayMax);
        queuedValue = targetValue;
    }

    public void EnqueueValue(float value, float max)
    {
        Initialize();
        displayMax = SanitizeMax(max);
        EnqueueTargetValue(SanitizeValue(value), displayMax);
    }

    public void SetValueDisplayOverride(string displayText)
    {
        Initialize();
        StopAnimations();
        hasValueDisplayOverride = true;
        valueDisplayOverride = displayText ?? string.Empty;
        if (valueLabel != null)
            valueLabel.text = valueDisplayOverride;
    }

    public void ClearValueDisplayOverride()
    {
        if (!hasValueDisplayOverride)
            return;

        hasValueDisplayOverride = false;
        valueDisplayOverride = null;
        SetValueLabel(displayedValue);
    }

    public int ApplyStoredChange(int delta)
    {
        Initialize();

        GameResourceSystem resources = GameResourceSystem.Instance;
        if (resources != null)
        {
            int before = Mathf.RoundToInt(resources.Get(resourceType));
            resources.Modify(resourceType, delta);
            int after = Mathf.RoundToInt(resources.Get(resourceType));
            if (resourceType == ResourceType.Battery && after < before)
                OSUIEnergySpendPresenter.Show(this);
            return after;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
        {
            Debug.LogWarning(
                "[OSResourceStateView] GameDataManager is missing; resource change was ignored.",
                this);
            return queuedValue;
        }

        int previousValue = dataManager.GetOSResourceValue(resourceType);
        int nextValue = dataManager.ModifyOSResourceValue(resourceType, delta);
        if (previousValue == nextValue)
            return nextValue;

        GameEvents.RaiseResourceChanged(
            resourceType,
            nextValue,
            OSResourceSaveData.MaxValue);

        if (resourceType == ResourceType.Battery && nextValue < previousValue)
            OSUIEnergySpendPresenter.Show(this);

        return nextValue;
    }

    void EnqueueTargetValue(int targetValue, float max)
    {
        if (targetValue == queuedValue)
            return;

        animationQueue.Enqueue(new ResourceAnimationRequest(
            queuedValue,
            targetValue,
            max));
        queuedValue = targetValue;

        if (activeSequence == null)
            PlayNextAnimation();
    }

    void PlayNextAnimation()
    {
        if (animationQueue.Count == 0)
        {
            activeSequence = null;
            return;
        }

        ResourceAnimationRequest request = animationQueue.Dequeue();
        Sequence sequence = DOTween.Sequence();
        activeSequence = sequence;

        Tween counterTween = CreateCounterTween(request);
        if (counterTween != null)
            sequence.Join(counterTween);

        Sequence unitSequence = CreateUnitSequence(request);
        if (unitSequence != null)
            sequence.Join(unitSequence);

        Tween iconTween = CreateIconTween(request);
        if (iconTween != null)
            sequence.Join(iconTween);

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (activeSequence != sequence)
                    return;

                SetVisualState(request.ToValue, request.MaxValue);
                activeSequence = null;
                PlayNextAnimation();
            })
            .OnKill(() =>
            {
                if (activeSequence == sequence)
                    activeSequence = null;
            });
    }

    Tween CreateCounterTween(ResourceAnimationRequest request)
    {
        if (valueLabel == null)
            return null;

        SetValueLabel(request.FromValue);
        return valueLabel
            .DOCounter(
                request.FromValue,
                request.ToValue,
                SanitizeAnimationDuration(counterDuration),
                false)
            .SetEase(Ease.Linear)
            .OnUpdate(FormatCounterLabel)
            .OnComplete(() => SetValueLabel(request.ToValue));
    }

    Sequence CreateUnitSequence(ResourceAnimationRequest request)
    {
        Sequence sequence = null;
        if (request.ToValue > request.FromValue)
        {
            for (int i = 0; i < units.Length; i++)
                AppendUnitTransition(ref sequence, request, i);
        }
        else
        {
            for (int i = units.Length - 1; i >= 0; i--)
                AppendUnitTransition(ref sequence, request, i);
        }

        return sequence;
    }

    void AppendUnitTransition(
        ref Sequence sequence,
        ResourceAnimationRequest request,
        int unitIndex)
    {
        Image unit = units[unitIndex];
        if (unit == null)
            return;

        Sprite fromSprite = GetUnitSprite(
            request.FromValue,
            request.MaxValue,
            unitIndex);
        Sprite toSprite = GetUnitSprite(
            request.ToValue,
            request.MaxValue,
            unitIndex);
        if (fromSprite == toSprite)
            return;

        if (sequence == null)
            sequence = DOTween.Sequence();

        float duration = SanitizeAnimationDuration(
            unitTransitionDuration);
        float halfDuration = duration * 0.5f;
        float restingAlpha = GetUnitRestingAlpha(unitIndex);
        Vector2 restingPosition = GetUnitRestingPosition(unitIndex);
        bool isIncrease = request.ToValue > request.FromValue;
        bool isAppearing = isIncrease
            && fromSprite == emptySprite
            && toSprite != emptySprite;
        bool isDisappearing = !isIncrease
            && fromSprite != emptySprite
            && toSprite == emptySprite;
        Sequence transition = DOTween.Sequence();
        if (duration <= 0f)
        {
            transition.AppendCallback(() =>
            {
                unit.sprite = toSprite;
                unit.rectTransform.anchoredPosition = restingPosition;
                SetImageAlpha(unit, restingAlpha);
            });
        }
        else if (isAppearing)
        {
            transition.AppendCallback(() =>
            {
                unit.sprite = toSprite;
                unit.rectTransform.anchoredPosition =
                    restingPosition + Vector2.up * UnitTransitionOffsetY;
                SetImageAlpha(unit, 0f);
            });
            transition.Append(unit.rectTransform
                .DOAnchorPos(restingPosition, duration)
                .SetEase(Ease.OutCubic));
            transition.Join(unit
                .DOFade(restingAlpha, duration)
                .SetEase(Ease.OutQuad));
        }
        else if (isDisappearing)
        {
            transition.Append(unit.rectTransform
                .DOAnchorPos(
                    restingPosition + Vector2.up * UnitTransitionOffsetY,
                    duration)
                .SetEase(Ease.InCubic));
            transition.Join(unit
                .DOFade(0f, duration)
                .SetEase(Ease.InQuad));
            transition.AppendCallback(() =>
            {
                unit.sprite = toSprite;
                unit.rectTransform.anchoredPosition = restingPosition;
                SetImageAlpha(unit, restingAlpha);
            });
        }
        else
        {
            transition.Append(unit
                .DOFade(0f, halfDuration)
                .SetEase(Ease.Linear));
            transition.AppendCallback(() => unit.sprite = toSprite);
            transition.Append(unit
                .DOFade(restingAlpha, halfDuration)
                .SetEase(Ease.Linear));
        }

        sequence.Join(transition);
    }

    Tween CreateIconTween(ResourceAnimationRequest request)
    {
        if (icon == null || request.ToValue == request.FromValue)
            return null;

        icon.anchoredPosition = iconRestingPosition;
        if (request.ToValue > request.FromValue)
        {
            PrepareIconGlow();
            if (iconGlow == null)
                return null;

            float duration = SanitizeAnimationDuration(iconGlowDuration);
            Color hiddenColor = iconGlowColor;
            hiddenColor.a = 0f;
            iconGlow.enabled = true;
            iconGlow.effectColor = hiddenColor;
            if (duration <= 0f)
            {
                DisableIconGlow();
                return null;
            }

            Sequence glowSequence = DOTween.Sequence();
            glowSequence.Append(DOTween.To(
                () => iconGlow.effectColor,
                color => iconGlow.effectColor = color,
                iconGlowColor,
                duration * 0.35f)
                .SetEase(Ease.OutQuad));
            glowSequence.Append(DOTween.To(
                () => iconGlow.effectColor,
                color => iconGlow.effectColor = color,
                hiddenColor,
                duration * 0.65f)
                .SetEase(Ease.InQuad));
            return glowSequence;
        }

        float glitchDuration = SanitizeAnimationDuration(
            iconGlitchDuration);
        if (glitchDuration <= 0f)
            return null;

        float distance = Mathf.Clamp(
            iconGlitchDistance,
            0f,
            MaximumGlowDistance);
        float stepDuration = glitchDuration / 6f;
        Sequence glitchSequence = DOTween.Sequence();
        AppendIconGlitchStep(glitchSequence, distance, stepDuration);
        AppendIconGlitchStep(glitchSequence, -distance, stepDuration);
        AppendIconGlitchStep(glitchSequence, distance, stepDuration);
        AppendIconGlitchStep(glitchSequence, -distance, stepDuration);
        AppendIconGlitchStep(glitchSequence, distance, stepDuration);
        AppendIconGlitchStep(glitchSequence, 0f, stepDuration);
        glitchSequence.AppendCallback(
            () => icon.anchoredPosition = iconRestingPosition);
        return glitchSequence;
    }

    void AppendIconGlitchStep(
        Sequence sequence,
        float horizontalOffset,
        float duration)
    {
        sequence.AppendCallback(() =>
        {
            Vector2 position = iconRestingPosition;
            position.x += horizontalOffset;
            icon.anchoredPosition = position;
        });
        sequence.AppendInterval(duration);
    }

    void SetVisualState(int value, float max)
    {
        displayedValue = SanitizeValue(value);
        displayMax = SanitizeMax(max);
        SetValueLabel(displayedValue);

        for (int i = 0; i < units.Length; i++)
        {
            Image unit = units[i];
            if (unit == null)
                continue;

            unit.sprite = GetUnitSprite(displayedValue, displayMax, i);
            unit.rectTransform.anchoredPosition =
                GetUnitRestingPosition(i);
            SetImageAlpha(unit, GetUnitRestingAlpha(i));
        }

        if (icon != null)
            icon.anchoredPosition = iconRestingPosition;
        DisableIconGlow();
    }

    Sprite GetUnitSprite(int value, float max, int unitIndex)
    {
        int percent = max > 0f
            ? Mathf.RoundToInt(Mathf.Clamp01(value / max) * 100f)
            : 0;
        int fullCount = Mathf.Clamp(
            percent / 10,
            0,
            ExpectedUnitCount);
        bool hasHalfUnit = fullCount < ExpectedUnitCount
            && percent % 10 != 0;

        if (unitIndex < fullCount)
            return fullSprite;
        if (unitIndex == fullCount && hasHalfUnit)
            return halfSprite;
        return emptySprite;
    }

    void FormatCounterLabel()
    {
        if (valueLabel == null)
            return;

        string currentText = valueLabel.text;
        int digitCount = 0;
        while (digitCount < currentText.Length
            && char.IsDigit(currentText[digitCount]))
        {
            digitCount++;
        }

        if (digitCount > 0
            && int.TryParse(
                currentText.Substring(0, digitCount),
                out int counterValue))
        {
            SetValueLabel(counterValue);
        }
    }

    void SetValueLabel(int value)
    {
        if (valueLabel == null)
            return;

        valueLabel.text = hasValueDisplayOverride
            ? valueDisplayOverride ?? string.Empty
            : SanitizeValue(value).ToString("000") + valueSuffix;
    }

    public void PlaySubtleShake()
    {
        if (!isActiveAndEnabled) return;
        StopSubtleShake();
        var rect = transform as RectTransform;
        if (rect == null) return;
        positionBeforeSubtleShake = rect.anchoredPosition;
        subtleShake = rect.DOShakeAnchorPos(0.24f, new Vector2(0.45f, 0.2f), 5, 0f, false, true)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                subtleShake = null;
                rect.anchoredPosition = positionBeforeSubtleShake;
            });
    }

    void StopSubtleShake()
    {
        if (subtleShake == null) return;
        subtleShake.Kill(false);
        subtleShake = null;
        if (transform is RectTransform rect)
            rect.anchoredPosition = positionBeforeSubtleShake;
    }

    void StopAnimations()
    {
        StopSubtleShake();
        Sequence sequence = activeSequence;
        activeSequence = null;
        if (sequence != null && sequence.IsActive())
            sequence.Kill(false);

        animationQueue.Clear();
        DisableIconGlow();
    }

    void ResolveReferences()
    {
        if (nameLabel == null)
            nameLabel = transform.Find("Name")?.GetComponent<TMP_Text>();

        if (valueLabel == null)
            valueLabel = transform.Find("Num")?.GetComponent<TMP_Text>();

        if (icon == null)
            icon = transform.Find("Icon") as RectTransform;
        if (iconImage == null && icon != null)
            iconImage = icon.GetComponent<Image>();

        Transform showPanel = transform.Find("ShowPanel");
        if (showPanel == null)
            return;

        bool shouldRebuildUnits = units == null
            || units.Length != showPanel.childCount;
        if (!shouldRebuildUnits)
        {
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] == null
                    || units[i].transform != showPanel.GetChild(i))
                {
                    shouldRebuildUnits = true;
                    break;
                }
            }
        }

        if (!shouldRebuildUnits)
            return;

        units = new Image[showPanel.childCount];
        for (int i = 0; i < showPanel.childCount; i++)
            units[i] = showPanel.GetChild(i).GetComponent<Image>();
    }

    void CaptureValueSuffix()
    {
        if (!string.IsNullOrEmpty(valueSuffix)
            || valueLabel == null
            || valueLabel.text.Length < 3)
        {
            return;
        }

        valueSuffix = valueLabel.text.Substring(3);
    }

    void CaptureRestingVisuals()
    {
        if (Application.isPlaying && hasCapturedRestingVisuals)
            return;

        if (icon != null)
            iconRestingPosition = icon.anchoredPosition;

        if (units == null)
            return;

        if (unitRestingAlphas == null
            || unitRestingAlphas.Length != units.Length)
        {
            unitRestingAlphas = new float[units.Length];
        }
        if (unitRestingPositions == null
            || unitRestingPositions.Length != units.Length)
        {
            unitRestingPositions = new Vector2[units.Length];
        }

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == null)
                continue;

            unitRestingAlphas[i] = units[i].color.a;
            unitRestingPositions[i] =
                units[i].rectTransform.anchoredPosition;
        }

        hasCapturedRestingVisuals = true;
    }

    int ReadValueLabel()
    {
        if (valueLabel == null || string.IsNullOrEmpty(valueLabel.text))
            return OSResourceSaveData.MinValue;

        int digitCount = 0;
        while (digitCount < valueLabel.text.Length
            && char.IsDigit(valueLabel.text[digitCount]))
        {
            digitCount++;
        }

        return digitCount > 0
            && int.TryParse(
                valueLabel.text.Substring(0, digitCount),
                out int value)
                    ? SanitizeValue(value)
                    : OSResourceSaveData.MinValue;
    }

    float GetUnitRestingAlpha(int unitIndex)
    {
        return unitRestingAlphas != null
            && unitIndex >= 0
            && unitIndex < unitRestingAlphas.Length
                ? unitRestingAlphas[unitIndex]
                : 1f;
    }

    Vector2 GetUnitRestingPosition(int unitIndex)
    {
        return unitRestingPositions != null
            && unitIndex >= 0
            && unitIndex < unitRestingPositions.Length
                ? unitRestingPositions[unitIndex]
                : Vector2.zero;
    }

    void PrepareIconGlow()
    {
        if (!Application.isPlaying || iconImage == null)
            return;

        if (iconGlow != null)
            return;

        iconGlow = iconImage.gameObject.AddComponent<Outline>();

        iconGlow.useGraphicAlpha = true;
        Vector2 iconSize = icon != null
            ? icon.rect.size
            : Vector2.zero;
        iconGlow.effectDistance = new Vector2(
            CalculateGlowDistance(iconSize.x),
            -CalculateGlowDistance(iconSize.y));
        Color hiddenColor = iconGlowColor;
        hiddenColor.a = 0f;
        iconGlow.effectColor = hiddenColor;
        iconGlow.enabled = false;
    }

    void DisableIconGlow()
    {
        if (iconGlow == null)
            return;

        Color color = iconGlow.effectColor;
        color.a = 0f;
        iconGlow.effectColor = color;
        iconGlow.enabled = false;
    }

    static int SanitizeValue(float value)
    {
        return Mathf.Clamp(
            Mathf.RoundToInt(value),
            OSResourceSaveData.MinValue,
            OSResourceSaveData.MaxValue);
    }

    static float SanitizeMax(float max)
    {
        return max > 0f ? max : OSResourceSaveData.MaxValue;
    }

    static float SanitizeAnimationDuration(float duration)
    {
        return duration <= 0f
            ? 0f
            : Mathf.Clamp(duration, 0.1f, 0.2f);
    }

    static float CalculateGlowDistance(float iconSize)
    {
        float availableDistance = Mathf.Floor(
            (MaximumGlowBounds - Mathf.Max(0f, iconSize)) * 0.5f);
        return Mathf.Clamp(
            availableDistance,
            0f,
            MaximumGlowDistance);
    }

    static void SetImageAlpha(Image image, float alpha)
    {
        Color color = image.color;
        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }

    readonly struct ResourceAnimationRequest
    {
        public readonly int FromValue;
        public readonly int ToValue;
        public readonly float MaxValue;

        public ResourceAnimationRequest(
            int fromValue,
            int toValue,
            float maxValue)
        {
            FromValue = fromValue;
            ToValue = toValue;
            MaxValue = maxValue;
        }
    }
}
