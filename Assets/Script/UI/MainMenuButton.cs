using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum MainMenuButtonState
{
    Disabled,
    Normal,
    Active
}

public enum MainMenuButtonAction
{
    ContinueGame,
    StartGame,
    Option,
    ExitGame
}

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class MainMenuButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("References")]
    [SerializeField] TextMeshProUGUI label;
    [SerializeField] Image underline;

    [Header("Action")]
    [SerializeField] MainMenuButtonAction action;

    [Header("State")]
    [SerializeField] MainMenuButtonState initialState =
        MainMenuButtonState.Normal;
    [SerializeField] Color disabledColor =
        new Color32(0x26, 0x23, 0x1A, 0xFF);
    [SerializeField] Color normalColor =
        new Color32(0x62, 0x5A, 0x45, 0xFF);
    [SerializeField] Color activeColor =
        new Color32(0x8F, 0x84, 0x69, 0xFF);

    [Header("Transition")]
    [SerializeField, Min(0f)] float colorDuration = 0.2f;
    [SerializeField, Min(0f)] float underlineDuration = 0.15f;

    public MainMenuButtonState State { get; private set; }
    public bool IsInteractable => State != MainMenuButtonState.Disabled;
    public MainMenuButtonAction Action => action;

    Tween colorTween;
    Tween underlineTween;
    bool initialized;

    void Awake()
    {
        Initialize();
    }

    void OnEnable()
    {
        if (!initialized)
        {
            Initialize();
            return;
        }

        if (State == MainMenuButtonState.Active)
            State = MainMenuButtonState.Normal;

        ApplyStateImmediate();
    }

    void OnDisable()
    {
        KillTweens();

        if (State == MainMenuButtonState.Active)
            State = MainMenuButtonState.Normal;
    }

    void OnDestroy()
    {
        KillTweens();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (State == MainMenuButtonState.Normal)
            SetState(MainMenuButtonState.Active);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (State == MainMenuButtonState.Active)
            SetState(MainMenuButtonState.Normal);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsInteractable
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        MainMenuHandle handle = MainMenuHandle.Instance;
        if (handle == null)
        {
            Debug.LogError(
                "[MainMenu] MainMenuHandle is missing; the button action cannot be dispatched.",
                this);
            return;
        }

        handle.HandleButton(action);
    }

    public void SetState(
        MainMenuButtonState newState,
        bool immediate = false)
    {
        ResolveReferences();
        ConfigureUnderline();

        State = newState;
        if (label != null)
            label.raycastTarget = IsInteractable;

        if (immediate || !isActiveAndEnabled || !Application.isPlaying)
        {
            ApplyStateImmediate();
            return;
        }

        AnimateState();
    }

    public void SetInteractable(
        bool interactable,
        bool immediate = true)
    {
        SetState(
            interactable
                ? MainMenuButtonState.Normal
                : MainMenuButtonState.Disabled,
            immediate);
    }

    void Initialize()
    {
        ResolveReferences();
        ConfigureUnderline();
        State = initialState;
        initialized = true;
        ApplyStateImmediate();
    }

    void AnimateState()
    {
        KillTweens();

        Color targetColor = GetStateColor(State);
        if (label != null)
        {
            float colorTransitionDuration = GetColorTransitionDuration(
                label.color,
                targetColor);
            if (colorTransitionDuration <= 0.0001f)
            {
                label.color = targetColor;
            }
            else
            {
                Tween tween = label
                    .DOColor(targetColor, colorTransitionDuration)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable);
                colorTween = tween;
                tween.OnKill(() => ClearColorTween(tween));
                tween.OnComplete(() => ClearColorTween(tween));
            }
        }

        if (underline == null)
            return;

        float targetFill = State == MainMenuButtonState.Active ? 1f : 0f;
        float durationScale = Mathf.Abs(targetFill - underline.fillAmount);
        float underlineTransitionDuration =
            underlineDuration * durationScale;
        if (underlineTransitionDuration <= 0.0001f)
        {
            underline.fillAmount = targetFill;
            return;
        }

        Tween underlineAnimation = underline
            .DOFillAmount(targetFill, underlineTransitionDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        underlineTween = underlineAnimation;
        underlineAnimation.OnKill(
            () => ClearUnderlineTween(underlineAnimation));
        underlineAnimation.OnComplete(
            () => ClearUnderlineTween(underlineAnimation));
    }

    void ApplyStateImmediate()
    {
        KillTweens();
        ResolveReferences();
        ConfigureUnderline();

        if (label != null)
        {
            label.color = GetStateColor(State);
            label.raycastTarget = IsInteractable;
        }

        if (underline != null)
        {
            underline.fillAmount =
                State == MainMenuButtonState.Active ? 1f : 0f;
        }
    }

    void ResolveReferences()
    {
        if (label == null)
            label = GetComponent<TextMeshProUGUI>();

        if (underline == null)
            underline = GetComponentInChildren<Image>(true);
    }

    void ConfigureUnderline()
    {
        if (underline == null)
            return;

        underline.type = Image.Type.Filled;
        underline.fillMethod = Image.FillMethod.Horizontal;
        underline.fillOrigin = (int)Image.OriginHorizontal.Left;
        underline.fillClockwise = true;
        underline.raycastTarget = false;

        Color color = underline.color;
        color.a = 1f;
        underline.color = color;
    }

    Color GetStateColor(MainMenuButtonState state)
    {
        switch (state)
        {
            case MainMenuButtonState.Disabled:
                return disabledColor;

            case MainMenuButtonState.Active:
                return activeColor;

            default:
                return normalColor;
        }
    }

    float GetColorTransitionDuration(Color current, Color target)
    {
        float currentDistance = MaxChannelDistance(current, target);
        if (currentDistance <= 0.0001f)
            return 0f;

        float fullDistance;
        if (State == MainMenuButtonState.Disabled)
        {
            fullDistance = Mathf.Max(
                MaxChannelDistance(disabledColor, normalColor),
                MaxChannelDistance(disabledColor, activeColor));
        }
        else
        {
            fullDistance = MaxChannelDistance(normalColor, activeColor);
        }

        if (fullDistance <= 0.0001f)
            return 0f;

        return colorDuration * Mathf.Clamp01(currentDistance / fullDistance);
    }

    static float MaxChannelDistance(Color first, Color second)
    {
        return Mathf.Max(
            Mathf.Abs(first.r - second.r),
            Mathf.Abs(first.g - second.g),
            Mathf.Abs(first.b - second.b),
            Mathf.Abs(first.a - second.a));
    }

    void ClearColorTween(Tween tween)
    {
        if (colorTween == tween)
            colorTween = null;
    }

    void ClearUnderlineTween(Tween tween)
    {
        if (underlineTween == tween)
            underlineTween = null;
    }

    void KillTweens()
    {
        if (colorTween != null)
        {
            Tween tween = colorTween;
            colorTween = null;
            tween.Kill(false);
        }

        if (underlineTween != null)
        {
            Tween tween = underlineTween;
            underlineTween = null;
            tween.Kill(false);
        }
    }

    void Reset()
    {
        ResolveReferences();
        ConfigureUnderline();
        State = initialState;
        ApplyStateImmediate();
    }
}
