using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSOptionImageButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    public enum ButtonAction
    {
        AudioPage,
        ScreenPage,
        PreviousTextSpeed,
        NextTextSpeed,
        SaveAndReturnToMainMenu,
        ExitGame
    }

    Image targetImage;
    OSOptionWindowController owner;
    ButtonAction action;
    float hoverAlpha;
    Tween fadeTween;
    bool isSelected;
    bool isPointerInside;

    void Awake()
    {
        targetImage = GetComponent<Image>();
    }

    void OnDisable()
    {
        isPointerInside = false;
        KillFade();
        SetAlpha(0f);
    }

    void OnDestroy()
    {
        KillFade();
    }

    public void Configure(
        OSOptionWindowController controller,
        ButtonAction buttonAction,
        float targetHoverAlpha)
    {
        owner = controller;
        action = buttonAction;
        hoverAlpha = Mathf.Clamp01(targetHoverAlpha);
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        isPointerInside = false;
        SetAlpha(GetVisualAlpha());
    }

    public void SetSelected(bool selected, bool animate)
    {
        isSelected = selected;
        float targetAlpha = GetVisualAlpha();
        if (animate)
            FadeTo(targetAlpha);
        else
        {
            KillFade();
            SetAlpha(targetAlpha);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        FadeTo(GetVisualAlpha());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        FadeTo(GetVisualAlpha());
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.HandleButton(action);
    }

    void FadeTo(float alpha)
    {
        if (targetImage == null)
            return;

        KillFade();
        fadeTween = targetImage
            .DOFade(alpha, 0.1f)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() => fadeTween = null)
            .OnComplete(() => fadeTween = null);
    }

    void KillFade()
    {
        if (fadeTween == null)
            return;

        Tween tween = fadeTween;
        fadeTween = null;
        tween.Kill(false);
    }

    void SetAlpha(float alpha)
    {
        if (targetImage == null)
            return;

        Color color = targetImage.color;
        color.a = Mathf.Clamp01(alpha);
        targetImage.color = color;
    }

    float GetVisualAlpha()
    {
        return isSelected || isPointerInside ? hoverAlpha : 0f;
    }
}
