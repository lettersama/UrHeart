using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum MainMenuOptionAction
{
    None,
    Language,
    ScreenMode,
    PostProcessing,
    Return
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class MainMenuOptionActionButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    const float TransitionDuration = 0.1f;

    static readonly Color NormalColor =
        new Color32(0xAF, 0xA6, 0x92, 0xFF);
    static readonly Color ActiveColor =
        new Color32(0xAE, 0x7C, 0x30, 0xFF);

    [SerializeField] MainMenuOptionAction action;
    [SerializeField] Image raycastImage;
    [SerializeField] Graphic raycastTip;
    [SerializeField] TMP_Text valueLabel;

    public MainMenuOptionAction Action => action;

    MainMenuOptionPanel owner;
    TMP_Text[] coloredTexts = Array.Empty<TMP_Text>();
    Tween hoverTween;

    void Awake()
    {
        ResolveReferences();
        ApplyStateImmediate(false);
    }

    void OnEnable()
    {
        ResolveReferences();
        ApplyStateImmediate(false);
    }

    void OnDisable()
    {
        KillHoverTween();
        ApplyStateImmediate(false);
    }

    void OnDestroy()
    {
        KillHoverTween();
    }

    public void Configure(
        MainMenuOptionPanel panel,
        MainMenuOptionAction buttonAction,
        TMP_Text[] rowTexts,
        Graphic tip,
        TMP_Text currentValueLabel)
    {
        owner = panel;
        action = buttonAction;
        coloredTexts = rowTexts ?? Array.Empty<TMP_Text>();
        raycastTip = tip;
        valueLabel = currentValueLabel;
        ResolveReferences();
        ApplyStateImmediate(false);
    }

    public void SetValueText(string value)
    {
        OSLocalizedFontUtility.ApplyCurrent(valueLabel);
        if (valueLabel != null)
            valueLabel.text = value ?? string.Empty;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        AnimateState(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        AnimateState(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (owner == null)
            owner = GetComponentInParent<MainMenuOptionPanel>(true);

        owner?.HandleAction(action);
    }

    void AnimateState(bool hovered)
    {
        ResolveReferences();
        KillHoverTween();

        Sequence sequence = DOTween.Sequence();
        Color targetColor = hovered ? ActiveColor : NormalColor;
        for (int i = 0; i < coloredTexts.Length; i++)
        {
            TMP_Text text = coloredTexts[i];
            if (text != null)
            {
                sequence.Join(text
                    .DOColor(targetColor, TransitionDuration)
                    .SetEase(Ease.Linear));
            }
        }

        if (raycastTip != null)
        {
            sequence.Join(raycastTip
                .DOFade(hovered ? 1f : 0f, TransitionDuration)
                .SetEase(Ease.Linear));
        }

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        hoverTween = sequence;
        sequence.OnKill(() => ClearHoverTween(sequence));
        sequence.OnComplete(() => ClearHoverTween(sequence));
    }

    void ApplyStateImmediate(bool hovered)
    {
        KillHoverTween();
        Color targetColor = hovered ? ActiveColor : NormalColor;
        for (int i = 0; i < coloredTexts.Length; i++)
        {
            if (coloredTexts[i] != null)
                coloredTexts[i].color = targetColor;
        }

        if (raycastTip != null)
        {
            Color tipColor = raycastTip.color;
            tipColor.a = hovered ? 1f : 0f;
            raycastTip.color = tipColor;
            raycastTip.raycastTarget = false;
        }
    }

    void ResolveReferences()
    {
        if (raycastImage == null)
            raycastImage = GetComponent<Image>();

        if (raycastImage != null)
            raycastImage.raycastTarget = true;

        Transform row = transform.parent;
        if (coloredTexts.Length == 0 && row != null)
            coloredTexts = row.GetComponentsInChildren<TMP_Text>(true);

        if (raycastTip == null && row != null)
            raycastTip = row.Find("RaycastTip")?.GetComponent<Graphic>();

        if (valueLabel == null && row != null)
        {
            for (int i = 0; i < coloredTexts.Length; i++)
            {
                TMP_Text text = coloredTexts[i];
                if (text != null && text.transform != row)
                {
                    valueLabel = text;
                    break;
                }
            }
        }

        OSLocalizedFontUtility.ApplyCurrent(valueLabel);

        if (raycastTip != null)
            raycastTip.raycastTarget = false;
    }

    void KillHoverTween()
    {
        if (hoverTween == null)
            return;

        Tween tween = hoverTween;
        hoverTween = null;
        tween.Kill(false);
    }

    void ClearHoverTween(Tween tween)
    {
        if (hoverTween == tween)
            hoverTween = null;
    }
}
