using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSHeartHealFinishButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    const float HoverDuration = 0.15f;
    const float ExpandedWidth = 100f;

    static readonly Color NormalColor =
        new Color32(0x86, 0x85, 0x84, 0xff);

    [SerializeField] TMP_Text label;
    [SerializeField] RectTransform background;
    [SerializeField] Image backgroundImage;

    Action clickAction;
    bool isInteractable;
    bool isPointerInside;

    void Awake()
    {
        ResolveReferences();
    }

    void OnDisable()
    {
        isInteractable = false;
        isPointerInside = false;
        KillTweens();
        SetHoverImmediate(false);
    }

    void OnDestroy()
    {
        KillTweens();
    }

    public void Configure(RectTransform backgroundRect, Action onClick)
    {
        background = backgroundRect;
        backgroundImage = background != null
            ? background.GetComponent<Image>()
            : null;
        clickAction = onClick;
        ResolveReferences();
    }

    public void PrepareHidden()
    {
        ResolveReferences();
        isInteractable = false;
        KillTweens();

        if (label != null)
        {
            Color color = NormalColor;
            color.a = 0f;
            label.color = color;
        }

        SetBackgroundWidth(0f);
        if (backgroundImage != null)
        {
            Color color = backgroundImage.color;
            color.a = 0f;
            backgroundImage.color = color;
        }
    }

    public Tween CreateRevealTween(float duration)
    {
        ResolveReferences();
        float safeDuration = Mathf.Max(0.01f, duration);
        Sequence sequence = DOTween.Sequence();

        if (label != null)
        {
            sequence.Join(label
                .DOFade(1f, safeDuration)
                .SetEase(Ease.Linear));
        }

        if (backgroundImage != null)
        {
            sequence.Join(backgroundImage
                .DOFade(1f, safeDuration)
                .SetEase(Ease.Linear));
        }

        return sequence;
    }

    public void ShowImmediate()
    {
        ResolveReferences();
        isInteractable = false;
        KillTweens();

        if (label != null)
        {
            Color color = NormalColor;
            color.a = 1f;
            label.color = color;
        }

        SetBackgroundWidth(0f);
        if (backgroundImage != null)
        {
            Color color = backgroundImage.color;
            color.a = 1f;
            backgroundImage.color = color;
        }
    }

    public void SetInteractable(bool interactable)
    {
        bool wasInteractable = isInteractable;
        isInteractable = interactable;
        if (interactable)
        {
            if (isPointerInside)
                AnimateHover(true);
            return;
        }

        if (wasInteractable && gameObject.activeInHierarchy)
            AnimateHover(false);
        else
        {
            KillTweens();
            SetHoverImmediate(false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        if (isInteractable)
            AnimateHover(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        if (isInteractable)
            AnimateHover(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isInteractable
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        isInteractable = false;
        clickAction?.Invoke();
    }

    void AnimateHover(bool hovered)
    {
        ResolveReferences();

        if (label != null)
        {
            label.DOKill(false);
            label
                .DOColor(hovered ? Color.black : NormalColor, HoverDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        if (background != null)
        {
            background.DOKill(false);
            Vector2 targetSize = background.sizeDelta;
            targetSize.x = hovered ? ExpandedWidth : 0f;
            background
                .DOSizeDelta(targetSize, HoverDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
    }

    void SetHoverImmediate(bool hovered)
    {
        ResolveReferences();
        if (label != null)
        {
            Color color = hovered ? Color.black : NormalColor;
            color.a = label.color.a;
            label.color = color;
        }

        SetBackgroundWidth(hovered ? ExpandedWidth : 0f);
    }

    void SetBackgroundWidth(float width)
    {
        if (background == null)
            return;

        Vector2 size = background.sizeDelta;
        size.x = width;
        background.sizeDelta = size;
    }

    void ResolveReferences()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();
        if (backgroundImage == null && background != null)
            backgroundImage = background.GetComponent<Image>();
    }

    void KillTweens()
    {
        label?.DOKill(false);
        background?.DOKill(false);
        backgroundImage?.DOKill(false);
    }
}
