using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSOptionVolumeControl : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    const float MinHandX = -60f;
    const float MaxHandX = 60f;
    const float ColorDuration = 0.1f;

    static readonly Color HoverColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    static readonly Color PressedColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    OSOptionWindowController owner;
    GameOptionVolumeType volumeType;
    RectTransform hand;
    RectTransform sliderRoot;
    Image activeImage;
    Image handImage;
    TMP_Text valueLabel;
    Color normalColor = Color.white;
    Tween colorTween;
    bool hasCapturedNormalColor;
    bool isPointerInside;
    bool isPressed;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        isPointerInside = false;
        isPressed = false;
        SetHandColor(normalColor);
    }

    void OnDisable()
    {
        isPointerInside = false;
        isPressed = false;
        KillColorTween();
        SetHandColor(normalColor);
    }

    void OnDestroy()
    {
        KillColorTween();
    }

    public void Configure(
        OSOptionWindowController controller,
        GameOptionVolumeType type)
    {
        owner = controller;
        volumeType = type;
        ResolveReferences();
    }

    public void SetValue(int value)
    {
        ResolveReferences();
        int clampedValue = Mathf.Clamp(
            value,
            GameOptionSaveData.MinVolume,
            GameOptionSaveData.MaxVolume);
        float normalizedValue = clampedValue / 100f;

        if (hand != null)
        {
            Vector2 position = hand.anchoredPosition;
            position.x = Mathf.Lerp(MinHandX, MaxHandX, normalizedValue);
            hand.anchoredPosition = position;
        }

        if (activeImage != null)
        {
            activeImage.type = Image.Type.Filled;
            activeImage.fillMethod = Image.FillMethod.Horizontal;
            activeImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            activeImage.fillClockwise = true;
            activeImage.fillAmount = normalizedValue;
        }

        if (valueLabel != null)
            valueLabel.text = clampedValue.ToString();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData != null
            && eventData.button == PointerEventData.InputButton.Left)
        {
            isPressed = true;
            RefreshHandColor();
        }

        UpdateFromPointer(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        UpdateFromPointer(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isPressed = false;
        RefreshHandColor();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        RefreshHandColor();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        RefreshHandColor();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        isPressed = true;
        RefreshHandColor();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        isPressed = false;
        RefreshHandColor();
    }

    void UpdateFromPointer(PointerEventData eventData)
    {
        if (eventData == null || sliderRoot == null)
            return;

        Camera eventCamera = eventData.pressEventCamera != null
            ? eventData.pressEventCamera
            : eventData.enterEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                sliderRoot,
                eventData.position,
                eventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        float normalizedValue = Mathf.InverseLerp(
            MinHandX,
            MaxHandX,
            Mathf.Clamp(localPoint.x, MinHandX, MaxHandX));
        int value = Mathf.RoundToInt(normalizedValue * 100f);
        owner?.SetVolume(volumeType, value);
    }

    void ResolveReferences()
    {
        if (hand == null)
            hand = transform as RectTransform;

        if (handImage == null)
            handImage = GetComponent<Image>();

        if (!hasCapturedNormalColor && handImage != null)
        {
            normalColor = handImage.color;
            hasCapturedNormalColor = true;
        }

        if (sliderRoot == null)
            sliderRoot = transform.parent as RectTransform;

        if (sliderRoot == null)
            return;

        if (activeImage == null)
        {
            activeImage = sliderRoot
                .Find("FloatAcitve")
                ?.GetComponent<Image>();
        }

        if (valueLabel == null)
        {
            valueLabel = sliderRoot
                .Find("VolumeNum")
                ?.GetComponent<TMP_Text>();
        }
    }

    void RefreshHandColor()
    {
        Color targetColor = isPressed
            ? PressedColor
            : isPointerInside
                ? HoverColor
                : normalColor;
        FadeHandTo(targetColor);
    }

    void FadeHandTo(Color color)
    {
        ResolveReferences();
        if (handImage == null)
            return;

        KillColorTween();
        Tween tween = handImage
            .DOColor(color, ColorDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        colorTween = tween;
        tween.OnKill(() =>
        {
            if (colorTween == tween)
                colorTween = null;
        });
    }

    void KillColorTween()
    {
        if (colorTween == null)
            return;

        Tween tween = colorTween;
        colorTween = null;
        tween.Kill(false);
    }

    void SetHandColor(Color color)
    {
        if (handImage != null)
            handImage.color = color;
    }
}
