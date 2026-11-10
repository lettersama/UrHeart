using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSStateCheckSectionMinButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    const float StateTransitionDuration = 0.25f;
    const float HoverTransitionDuration = 0.1f;

    static readonly Color ExpandedColor =
        new Color32(0x4A, 0x4A, 0x4A, 0xFF);
    static readonly Color CollapsedColor =
        new Color32(0xC7, 0xC4, 0xC4, 0xFF);
    static readonly Color HoverColor =
        new Color32(0xE0, 0xAB, 0x28, 0xFF);

    Image buttonImage;
    RectMask2D contentMask;
    Tween paddingTween;
    Tween colorTween;
    float collapsedBottomPadding;
    bool initialized;
    bool isExpanded = true;
    bool isInteractable = true;
    bool isPointerInside;

    public bool IsExpanded => isExpanded;
    public bool IsInteractable => isInteractable;

    void Awake()
    {
        ResolveImage();
    }

    void OnEnable()
    {
        isPointerInside = false;
        if (!initialized)
            return;

        ApplyPaddingImmediate();
        ApplyColorImmediate();
        ApplyInteractionState();
    }

    void OnDisable()
    {
        isPointerInside = false;
        KillTweens();
        if (!initialized)
            return;

        ApplyPaddingImmediate();
        ApplyColorImmediate();
    }

    void OnDestroy()
    {
        KillTweens();
    }

    public void Initialize(
        RectMask2D targetMask,
        float targetCollapsedBottomPadding)
    {
        ResolveImage();
        if (targetMask == null)
            return;

        bool targetChanged = contentMask != targetMask;
        contentMask = targetMask;
        collapsedBottomPadding = Mathf.Max(
            0f,
            targetCollapsedBottomPadding);
        if (!initialized || targetChanged)
        {
            isExpanded = contentMask.padding.y <= 0.01f;
            initialized = true;
            ApplyPaddingImmediate();
            ApplyColorImmediate();
        }

        ApplyInteractionState();
    }

    public void SetGuideLocked(bool locked, bool animate)
    {
        bool wasLocked = !isInteractable;
        isInteractable = !locked;
        if (locked)
        {
            isPointerInside = false;
            SetExpanded(false, animate);
        }
        else if (wasLocked)
        {
            SetExpanded(true, animate);
        }
        else
        {
            UpdateColor(animate);
        }

        ApplyInteractionState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!CanInteract())
            return;

        isPointerInside = true;
        UpdateColor(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        UpdateColor(true);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!CanInteract()
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        SetExpanded(!isExpanded, true);
        eventData.Use();
    }

    void SetExpanded(bool expanded, bool animate)
    {
        isExpanded = expanded;
        KillPaddingTween();
        if (contentMask == null)
            return;

        float targetBottomPadding = expanded
            ? 0f
            : collapsedBottomPadding;
        if (!animate
            || !Application.isPlaying
            || !isActiveAndEnabled)
        {
            SetBottomPadding(targetBottomPadding);
            ApplyColorImmediate();
            return;
        }

        Tween tween = null;
        tween = DOVirtual.Float(
                contentMask.padding.y,
                targetBottomPadding,
                StateTransitionDuration,
                SetBottomPadding)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() => ClearPaddingTween(tween));
        paddingTween = tween;
        UpdateColor(true);
    }

    void SetBottomPadding(float value)
    {
        if (contentMask == null)
            return;

        Vector4 padding = contentMask.padding;
        padding.y = value;
        contentMask.padding = padding;
    }

    void UpdateColor(bool animate)
    {
        ResolveImage();
        KillColorTween();
        if (buttonImage == null)
            return;

        Color targetColor = ResolveTargetColor();
        if (!animate
            || !Application.isPlaying
            || !isActiveAndEnabled)
        {
            buttonImage.color = targetColor;
            return;
        }

        Tween tween = null;
        tween = buttonImage
            .DOColor(targetColor, HoverTransitionDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() => ClearColorTween(tween));
        colorTween = tween;
    }

    Color ResolveTargetColor()
    {
        if (isInteractable && isPointerInside)
            return HoverColor;

        return isExpanded ? ExpandedColor : CollapsedColor;
    }

    bool CanInteract()
    {
        return isInteractable
            && OS_Handle.Instance?.IsSystemInputLocked != true
            && CursorHandle.Instance?.IsScriptControlled != true;
    }

    void ApplyPaddingImmediate()
    {
        SetBottomPadding(isExpanded ? 0f : collapsedBottomPadding);
    }

    void ApplyColorImmediate()
    {
        ResolveImage();
        if (buttonImage != null)
            buttonImage.color = ResolveTargetColor();
    }

    void ApplyInteractionState()
    {
        ResolveImage();
        if (buttonImage != null)
            buttonImage.raycastTarget = isInteractable;
    }

    void ResolveImage()
    {
        if (buttonImage == null)
            buttonImage = GetComponent<Image>();
    }

    void KillTweens()
    {
        KillPaddingTween();
        KillColorTween();
    }

    void KillPaddingTween()
    {
        if (paddingTween == null)
            return;

        Tween tween = paddingTween;
        paddingTween = null;
        tween.Kill(false);
    }

    void KillColorTween()
    {
        if (colorTween == null)
            return;

        Tween tween = colorTween;
        colorTween = null;
        tween.Kill(false);
    }

    void ClearPaddingTween(Tween tween)
    {
        if (paddingTween == tween)
            paddingTween = null;
    }

    void ClearColorTween(Tween tween)
    {
        if (colorTween == tween)
            colorTween = null;
    }
}
