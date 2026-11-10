using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class OSHeartHealFinishPart : MonoBehaviour
{
    const float PartRevealDuration = 0.15f;
    const float TipRevealDuration = 0.34f;
    const float SuccessParticleDuration = 0.4f;
    const float FailParticleDuration = 0.48f;
    const float ButtonRevealDelay = 0.06f;
    const float ButtonRevealDuration = 0.18f;
    const float HideDuration = 0.15f;
    const int FinishParticleCount = 8;
    const float TipStartScale = 1.15f;
    const float TipStartRotation = 2f;

    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] GameObject successAction;
    [SerializeField] TMP_Text successTip;
    [SerializeField] OSHeartHealFinishButton sureButton;
    [SerializeField] GameObject failAction;
    [SerializeField] TMP_Text failTip;
    [SerializeField] OSHeartHealFinishButton falseWinButton;
    [SerializeField] OSHeartHealFinishButton retryButton;

    Action confirmAction;
    Action falseWinAction;
    Action retryAction;
    Sequence presentationSequence;
    Tween hideTween;
    RectTransform particleRoot;
    readonly List<Image> finishParticles = new List<Image>();
    Color successTipBaseColor;
    Color failTipBaseColor;
    bool successTipColorCaptured;
    bool failTipColorCaptured;
    bool showingSuccess;
    bool isShowing;
    bool acceptsInput;

    public bool IsShowing => isShowing;

    void Awake()
    {
        ResolveReferences();
        HideImmediate();
    }

    void OnEnable()
    {
        if (!isShowing)
            HideImmediate();
    }

    void OnDisable()
    {
        isShowing = false;
        acceptsInput = false;
        KillTweens();
        SetButtonsInteractable(false);
    }

    void OnDestroy()
    {
        KillTweens();
    }

    public void Configure(
        Action onConfirm,
        Action onFalseWin,
        Action onRetry)
    {
        confirmAction = onConfirm;
        falseWinAction = onFalseWin;
        retryAction = onRetry;
        ResolveReferences();
    }

    public void Show(bool success)
    {
        ResolveReferences();
        KillTweens();

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        showingSuccess = success;
        isShowing = true;
        acceptsInput = false;

        if (successAction != null)
            successAction.SetActive(success);
        if (failAction != null)
            failAction.SetActive(!success);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;
        }

        PrepareActiveButtons();
        TMP_Text tip = success ? successTip : failTip;
        Tween tipTween = CreateTipPresentationTween(tip, success);

        Sequence sequence = DOTween.Sequence();
        presentationSequence = sequence;
        if (canvasGroup != null)
        {
            sequence.Append(canvasGroup
                .DOFade(1f, PartRevealDuration)
                .SetEase(Ease.Linear));
        }
        else
        {
            sequence.AppendInterval(PartRevealDuration);
        }

        sequence.Append(tipTween);
        sequence.AppendInterval(ButtonRevealDelay);
        AddButtonRevealTweens(sequence, sequence.Duration());
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (presentationSequence != sequence || !isShowing)
                    return;

                presentationSequence = null;
                acceptsInput = true;
                if (canvasGroup != null)
                    canvasGroup.interactable = true;
                SetButtonsInteractable(true);
            })
            .OnKill(() =>
            {
                if (presentationSequence == sequence)
                    presentationSequence = null;
            });
    }

    public void ShowImmediate(bool success)
    {
        ResolveReferences();
        KillTweens();

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        showingSuccess = success;
        isShowing = true;
        acceptsInput = true;

        if (successAction != null)
            successAction.SetActive(success);
        if (failAction != null)
            failAction.SetActive(!success);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (success)
            sureButton?.ShowImmediate();
        else
        {
            falseWinButton?.ShowImmediate();
            retryButton?.ShowImmediate();
        }

        SetButtonsInteractable(true);
    }

    public void HideImmediate()
    {
        ResolveReferences();
        isShowing = false;
        acceptsInput = false;
        KillTweens();
        SetButtonsInteractable(false);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (successAction != null)
            successAction.SetActive(false);
        if (failAction != null)
            failAction.SetActive(false);
    }

    void HandleConfirmClicked()
    {
        if (!LockInput())
            return;

        confirmAction?.Invoke();
    }

    void HandleFalseWinClicked()
    {
        if (!LockInput())
            return;

        falseWinAction?.Invoke();
    }

    void HandleRetryClicked()
    {
        if (!LockInput())
            return;

        if (canvasGroup == null)
        {
            HideImmediate();
            retryAction?.Invoke();
            return;
        }

        canvasGroup.DOKill(false);
        Tween tween = canvasGroup
            .DOFade(0f, HideDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        hideTween = tween;
        tween.OnComplete(() =>
        {
            if (hideTween != tween)
                return;

            hideTween = null;
            Action callback = retryAction;
            HideImmediate();
            callback?.Invoke();
        });
        tween.OnKill(() =>
        {
            if (hideTween == tween)
                hideTween = null;
        });
    }

    bool LockInput()
    {
        if (!isShowing || !acceptsInput)
            return false;

        acceptsInput = false;
        if (canvasGroup != null)
            canvasGroup.interactable = false;
        SetButtonsInteractable(false);
        return true;
    }

    Tween CreateTipPresentationTween(TMP_Text tip, bool success)
    {
        float particleDuration = success
            ? SuccessParticleDuration
            : FailParticleDuration;
        if (tip == null)
        {
            return DOVirtual.DelayedCall(
                Mathf.Max(TipRevealDuration, particleDuration),
                () => { },
                true);
        }

        RectTransform tipRect = tip.rectTransform;
        tip.DOKill(false);
        tipRect.DOKill(false);

        Color targetColor = GetTipBaseColor(tip);
        Color hiddenColor = targetColor;
        hiddenColor.a = 0f;
        tip.color = hiddenColor;
        tipRect.localScale = Vector3.one * TipStartScale;
        tipRect.localRotation = Quaternion.Euler(
            0f,
            0f,
            TipStartRotation);

        Sequence sequence = DOTween.Sequence();
        sequence.Join(tipRect
            .DOScale(Vector3.one, TipRevealDuration)
            .SetEase(Ease.OutCubic));
        sequence.Join(tipRect
            .DOLocalRotate(
                Vector3.zero,
                TipRevealDuration,
                RotateMode.Fast)
            .SetEase(Ease.OutCubic));
        sequence.Join(tip
            .DOFade(targetColor.a, TipRevealDuration * 0.45f)
            .SetEase(Ease.OutQuad));

        Tween particleTween = CreateFinishParticleTween(
            tip,
            targetColor,
            success);
        if (particleTween != null)
            sequence.Join(particleTween);

        return sequence;
    }

    Tween CreateFinishParticleTween(
        TMP_Text tip,
        Color effectColor,
        bool success)
    {
        EnsureParticlePool();
        if (tip == null
            || particleRoot == null
            || finishParticles.Count == 0)
        {
            return null;
        }

        particleRoot.SetAsLastSibling();
        Vector3 worldCenter = tip.rectTransform.TransformPoint(
            tip.rectTransform.rect.center);
        Vector3 localCenter = particleRoot.InverseTransformPoint(
            worldCenter);
        Vector2 center = new Vector2(localCenter.x, localCenter.y);
        float baseDuration = success
            ? SuccessParticleDuration
            : FailParticleDuration;
        float targetAlpha = effectColor.a * (success ? 0.9f : 0.82f);

        Sequence sequence = DOTween.Sequence();
        int count = Mathf.Min(FinishParticleCount, finishParticles.Count);
        for (int i = 0; i < count; i++)
        {
            Image particle = finishParticles[i];
            if (particle == null)
                continue;

            RectTransform rect = particle.rectTransform;
            float angle = Mathf.PI * 2f * (i + 0.35f) / count;
            Vector2 direction = new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle));
            float startRadius = success
                ? 6f + i % 2 * 2f
                : 8f + i % 3 * 1.5f;
            Vector2 start = center + direction * startRadius;
            Vector2 end = success
                ? center + direction * (19f + i % 3 * 3f)
                : start + new Vector2(
                    direction.x * 3f,
                    -(17f + i % 3 * 4f));
            float duration = baseDuration - 0.035f * (i % 3);
            float delay = success
                ? 0.012f * (i % 2)
                : 0.012f * i;
            float motionDelay = delay + (success ? 0f : 0.06f);
            float motionDuration = Mathf.Max(
                0.05f,
                duration - (success ? 0f : 0.06f));
            float size = 2.5f + i % 3 * 0.75f;

            rect.DOKill(false);
            particle.DOKill(false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = start;
            rect.localScale = Vector3.one * (success ? 0.75f : 1f);
            rect.localRotation = Quaternion.Euler(0f, 0f, i * 23f);

            Color particleColor = effectColor;
            particleColor.a = 0f;
            particle.color = particleColor;
            particle.gameObject.SetActive(true);

            sequence.Insert(
                motionDelay,
                rect
                    .DOAnchorPos(end, motionDuration)
                    .SetEase(success ? Ease.OutCubic : Ease.InQuad));
            sequence.Insert(
                motionDelay,
                rect
                    .DOLocalRotate(
                        new Vector3(0f, 0f, i * 23f + 90f),
                        motionDuration,
                        RotateMode.Fast)
                    .SetEase(Ease.Linear));
            sequence.Insert(
                motionDelay,
                rect
                    .DOScale(success ? 0.2f : 0.65f, motionDuration)
                    .SetEase(Ease.InQuad));
            sequence.Insert(
                delay,
                particle
                    .DOFade(targetAlpha, 0.06f)
                    .SetEase(Ease.OutQuad));
            sequence.Insert(
                delay + 0.08f,
                particle
                    .DOFade(0f, Mathf.Max(0.05f, duration - 0.08f))
                    .SetEase(success ? Ease.InQuad : Ease.InCubic));
        }

        sequence.OnComplete(HideFinishParticles);
        sequence.OnKill(HideFinishParticles);
        return sequence;
    }

    void EnsureParticlePool()
    {
        if (particleRoot == null)
        {
            GameObject rootObject = new GameObject(
                "FinishParticles",
                typeof(RectTransform));
            rootObject.layer = gameObject.layer;
            particleRoot = rootObject.GetComponent<RectTransform>();
            particleRoot.SetParent(transform, false);
            particleRoot.anchorMin = Vector2.zero;
            particleRoot.anchorMax = Vector2.one;
            particleRoot.offsetMin = Vector2.zero;
            particleRoot.offsetMax = Vector2.zero;
            particleRoot.localScale = Vector3.one;
        }

        particleRoot.SetAsLastSibling();
        while (finishParticles.Count < FinishParticleCount)
        {
            int index = finishParticles.Count;
            GameObject particleObject = new GameObject(
                $"Particle_{index:00}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            particleObject.layer = gameObject.layer;
            RectTransform rect = particleObject.GetComponent<RectTransform>();
            rect.SetParent(particleRoot, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = particleObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.maskable = true;
            particleObject.SetActive(false);
            finishParticles.Add(image);
        }
    }

    Color GetTipBaseColor(TMP_Text tip)
    {
        if (tip == successTip && successTipColorCaptured)
            return successTipBaseColor;
        if (tip == failTip && failTipColorCaptured)
            return failTipBaseColor;
        return tip != null ? tip.color : Color.white;
    }

    void HideFinishParticles()
    {
        for (int i = 0; i < finishParticles.Count; i++)
        {
            Image particle = finishParticles[i];
            if (particle != null)
                particle.gameObject.SetActive(false);
        }
    }

    void PrepareActiveButtons()
    {
        if (showingSuccess)
        {
            sureButton?.PrepareHidden();
            return;
        }

        falseWinButton?.PrepareHidden();
        retryButton?.PrepareHidden();
    }

    void AddButtonRevealTweens(Sequence sequence, float insertTime)
    {
        if (sequence == null)
            return;

        if (showingSuccess)
        {
            if (sureButton != null)
            {
                sequence.Insert(
                    insertTime,
                    sureButton.CreateRevealTween(ButtonRevealDuration));
            }

            return;
        }

        if (falseWinButton != null)
        {
            sequence.Insert(
                insertTime,
                falseWinButton.CreateRevealTween(ButtonRevealDuration));
        }

        if (retryButton != null)
        {
            sequence.Insert(
                insertTime,
                retryButton.CreateRevealTween(ButtonRevealDuration));
        }
    }

    void SetButtonsInteractable(bool interactable)
    {
        sureButton?.SetInteractable(interactable && showingSuccess);
        falseWinButton?.SetInteractable(interactable && !showingSuccess);
        retryButton?.SetInteractable(interactable && !showingSuccess);
    }

    void ResolveReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        Transform successRoot = transform.Find("SuccessAction");
        if (successAction == null && successRoot != null)
            successAction = successRoot.gameObject;
        if (successTip == null)
            successTip = successRoot?.Find("TipTMP")?.GetComponent<TMP_Text>();
        if (successTip != null && !successTipColorCaptured)
        {
            successTipBaseColor = successTip.color;
            successTipColorCaptured = true;
        }
        sureButton = ResolveButton(
            sureButton,
            successRoot,
            "Sure",
            "SureBk",
            HandleConfirmClicked);

        Transform failRoot = transform.Find("FailAction");
        if (failAction == null && failRoot != null)
            failAction = failRoot.gameObject;
        if (failTip == null)
            failTip = failRoot?.Find("TipTMP")?.GetComponent<TMP_Text>();
        if (failTip != null && !failTipColorCaptured)
        {
            failTipBaseColor = failTip.color;
            failTipColorCaptured = true;
        }
        falseWinButton = ResolveButton(
            falseWinButton,
            failRoot,
            "FalseWin",
            "FalseWinBK",
            HandleFalseWinClicked);
        retryButton = ResolveButton(
            retryButton,
            failRoot,
            "ReTry",
            "ReTryBK",
            HandleRetryClicked);
    }

    static OSHeartHealFinishButton ResolveButton(
        OSHeartHealFinishButton current,
        Transform actionRoot,
        string buttonName,
        string backgroundName,
        Action callback)
    {
        Transform buttonTransform = actionRoot?.Find(buttonName);
        if (current == null && buttonTransform != null)
        {
            current = buttonTransform.GetComponent<OSHeartHealFinishButton>();
            if (current == null && Application.isPlaying)
            {
                current = buttonTransform.gameObject
                    .AddComponent<OSHeartHealFinishButton>();
            }
        }

        RectTransform background = actionRoot?.Find(backgroundName)
            as RectTransform;
        current?.Configure(background, callback);
        return current;
    }

    void KillTweens()
    {
        if (presentationSequence != null)
        {
            Sequence sequence = presentationSequence;
            presentationSequence = null;
            sequence.Kill(false);
        }

        if (hideTween != null)
        {
            Tween tween = hideTween;
            hideTween = null;
            tween.Kill(false);
        }

        canvasGroup?.DOKill(false);
        successTip?.DOKill(false);
        failTip?.DOKill(false);
        successTip?.rectTransform.DOKill(false);
        failTip?.rectTransform.DOKill(false);
        HideFinishParticles();
        ResetTipVisual(successTip, successTipBaseColor, successTipColorCaptured);
        ResetTipVisual(failTip, failTipBaseColor, failTipColorCaptured);
    }

    static void ResetTipVisual(
        TMP_Text tip,
        Color baseColor,
        bool colorCaptured)
    {
        if (tip == null)
            return;

        tip.rectTransform.localScale = Vector3.one;
        tip.rectTransform.localRotation = Quaternion.identity;
        if (colorCaptured)
            tip.color = baseColor;
    }

}
