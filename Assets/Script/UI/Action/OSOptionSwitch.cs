using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSOptionSwitch : MonoBehaviour, IPointerClickHandler
{
    public enum SwitchType
    {
        FullScreen,
        OSPostProcessing
    }

    const float OffX = -22f;
    const float OnX = 22f;
    const float FoldDuration = 0.25f;
    const float ColorDuration = 0.3f;

    static readonly Color OffColor = new Color32(0x74, 0x71, 0x70, 0xff);
    static readonly Color OnColor = new Color32(0x8c, 0x17, 0x14, 0xff);

    OSOptionWindowController owner;
    SwitchType switchType;
    RectTransform fold;
    TMP_Text stateLabel;
    Graphic[] coloredGraphics;
    Tween foldTween;
    Tween[] colorTweens = new Tween[0];

    void Awake()
    {
        ResolveReferences();
    }

    void OnDisable()
    {
        KillTweens();
    }

    void OnDestroy()
    {
        KillTweens();
    }

    public void Configure(
        OSOptionWindowController controller,
        SwitchType type)
    {
        owner = controller;
        switchType = type;
        ResolveReferences();
    }

    public void SetState(bool isOn, bool animate)
    {
        ResolveReferences();
        KillTweens();

        float targetX = isOn ? OnX : OffX;
        Color targetColor = isOn ? OnColor : OffColor;
        if (stateLabel != null)
        {
            string key = isOn
                ? "ui.os.option.state.on"
                : "ui.os.option.state.off";
            string fallback = isOn ? "ON" : "OFF";
            stateLabel.text = owner != null
                ? owner.GetLocalizedText(key, fallback)
                : fallback;
        }

        if (!animate)
        {
            if (fold != null)
                SetFoldX(targetX);

            for (int i = 0; i < coloredGraphics.Length; i++)
            {
                if (coloredGraphics[i] != null)
                    coloredGraphics[i].color = targetColor;
            }

            return;
        }

        if (fold != null)
        {
            foldTween = fold
                .DOAnchorPosX(targetX, FoldDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .OnKill(() => foldTween = null)
                .OnComplete(() => foldTween = null);
        }

        colorTweens = new Tween[coloredGraphics.Length];
        for (int i = 0; i < coloredGraphics.Length; i++)
        {
            Graphic graphic = coloredGraphics[i];
            if (graphic == null)
                continue;

            int tweenIndex = i;
            colorTweens[i] = graphic
                .DOColor(targetColor, ColorDuration)
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .OnKill(() => colorTweens[tweenIndex] = null)
                .OnComplete(() => colorTweens[tweenIndex] = null);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.ToggleSwitch(switchType);
    }

    void ResolveReferences()
    {
        if (fold == null)
            fold = transform.Find("Fold") as RectTransform;

        if (stateLabel == null)
            stateLabel = transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();

        OSLocalizedFontUtility.ApplyCurrent(stateLabel);

        coloredGraphics = GetComponentsInChildren<Graphic>(true);
    }

    void SetFoldX(float x)
    {
        Vector2 position = fold.anchoredPosition;
        position.x = x;
        fold.anchoredPosition = position;
    }

    void KillTweens()
    {
        if (foldTween != null)
        {
            Tween tween = foldTween;
            foldTween = null;
            tween.Kill(false);
        }

        for (int i = 0; i < colorTweens.Length; i++)
        {
            Tween tween = colorTweens[i];
            colorTweens[i] = null;
            tween?.Kill(false);
        }
    }
}
