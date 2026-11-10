using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSHeartHealClickButton : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    const string ClickSfxKey = "HeartClickButton";
    const float TransitionDuration = 0.1f;
    const float HoverBrightness = 0.7f;
    const float CostTipStartY = 5f;
    const float CostTipMinX = -2f;
    const float CostTipMaxX = 6f;
    const float CostTipMinDistance = 10f;
    const float CostTipMaxDistance = 12f;
    const float CostTipMoveDuration = 0.4f;
    const float CostTipFadeDelay = 0.3f;
    const float CostTipFadeDuration = 0.1f;
    const float CostTipDurationScale = 1.75f;

    [SerializeField] Image targetImage;
    [SerializeField] Sprite normalSprite;
    [SerializeField] Sprite activeSprite;
    [SerializeField] OSHeartHealToolWindowController owner;
    [SerializeField] RectTransform costTipPool;
    [Header("Physical Key")]
    [Tooltip("Swap sprites at once instead of the fade blink, so the key reads as pressed.")]
    [SerializeField] bool instantPressVisual;
    [Tooltip("Content that sinks with the key face, such as the label.")]
    [SerializeField] RectTransform pressedContent;
    [SerializeField] float pressedContentOffset = 3f;

    Sequence transitionSequence;
    RectTransform[] costTipUnits = new RectTransform[0];
    Graphic[][] costTipGraphics = new Graphic[0][];
    Sequence[] costTipSequences = new Sequence[0];
    Color restingColor = Color.white;
    float restingAlpha = 1f;
    int nextCostTipIndex;
    bool isPointerPressed;
    bool isKeyboardPressed;
    bool isPointerInside;
    bool hasCapturedRestingColor;
    Vector2 pressedContentRestPosition;
    bool hasPressedContentRest;

    public bool IsPressed => isPointerPressed || isKeyboardPressed;

    void Awake()
    {
        ResolveReferences();
        PrepareCostTipPool();
        CaptureRestingVisual();
        isPointerInside = false;
        SetSprite(false);
        ApplyHoverVisual();
    }

    void OnEnable()
    {
        ResolveReferences();
        PrepareCostTipPool();
        CaptureRestingVisual();
        isPointerPressed = false;
        isKeyboardPressed = false;
        isPointerInside = false;
        SetSprite(false);
        ApplyHoverVisual();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || GameTestConsole.BlocksGameplayInput)
        {
            if (isKeyboardPressed)
            {
                isKeyboardPressed = false;
                TransitionTo(isPointerPressed);
            }

            return;
        }

        if (keyboard.spaceKey.wasPressedThisFrame
            && owner != null
            && owner.CanTriggerShock)
        {
            isKeyboardPressed = true;
            TransitionTo(true);
            TryTriggerShock();
        }

        if (!isKeyboardPressed || keyboard.spaceKey.isPressed)
            return;

        isKeyboardPressed = false;
        TransitionTo(isPointerPressed);
    }

    void OnDisable()
    {
        isPointerPressed = false;
        isKeyboardPressed = false;
        isPointerInside = false;
        KillTransition();
        KillCostTips();
        SetSprite(false);
        ApplyHoverVisual();
    }

    void OnDestroy()
    {
        KillTransition();
        KillCostTips();
    }

    public void Configure(OSHeartHealToolWindowController controller)
    {
        owner = controller;
        ResolveReferences();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (owner == null || !owner.CanTriggerShock)
            return;

        isPointerPressed = true;
        TransitionTo(true);
        TryTriggerShock();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        isPointerPressed = false;
        TransitionTo(isKeyboardPressed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        ApplyHoverVisual();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        ApplyHoverVisual();
    }

    void TransitionTo(bool active)
    {
        ResolveReferences();
        if (targetImage == null)
            return;

        KillTransition();
        if (instantPressVisual)
        {
            SetSprite(active);
            return;
        }

        float halfDuration = TransitionDuration * 0.5f;
        Sequence sequence = DOTween.Sequence();
        transitionSequence = sequence;
        sequence.Append(targetImage
            .DOFade(0f, halfDuration)
            .SetEase(Ease.Linear));
        sequence.AppendCallback(() => SetSprite(active));
        sequence.Append(targetImage
            .DOFade(restingAlpha, halfDuration)
            .SetEase(Ease.Linear));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (transitionSequence == sequence)
                    transitionSequence = null;
            });
    }

    void SetSprite(bool active)
    {
        if (targetImage == null)
            return;

        Sprite sprite = active ? activeSprite : normalSprite;
        if (sprite != null)
            targetImage.sprite = sprite;

        Color color = targetImage.color;
        color.a = restingAlpha;
        targetImage.color = color;
        ApplyPressedContent(active);
    }

    void ApplyPressedContent(bool active)
    {
        if (pressedContent == null)
            return;

        if (!hasPressedContentRest)
        {
            pressedContentRestPosition = pressedContent.anchoredPosition;
            hasPressedContentRest = true;
        }

        pressedContent.anchoredPosition = pressedContentRestPosition
            + (active ? Vector2.down * pressedContentOffset : Vector2.zero);
    }

    void ResolveReferences()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
        if (owner == null)
            owner = GetComponentInParent<OSHeartHealToolWindowController>(true);
        if (costTipPool == null)
            costTipPool = transform.Find("CostTipPool") as RectTransform;
        if (normalSprite == null && targetImage != null)
            normalSprite = targetImage.sprite;
    }

    void PrepareCostTipPool()
    {
        if (costTipPool == null)
        {
            costTipUnits = new RectTransform[0];
            costTipGraphics = new Graphic[0][];
            costTipSequences = new Sequence[0];
            return;
        }

        int unitCount = costTipPool.childCount;
        costTipUnits = new RectTransform[unitCount];
        costTipGraphics = new Graphic[unitCount][];
        costTipSequences = new Sequence[unitCount];
        nextCostTipIndex = 0;

        for (int i = 0; i < unitCount; i++)
        {
            RectTransform unit = costTipPool.GetChild(i) as RectTransform;
            costTipUnits[i] = unit;
            costTipGraphics[i] = unit != null
                ? unit.GetComponentsInChildren<Graphic>(true)
                : new Graphic[0];
            if (unit != null)
                unit.gameObject.SetActive(false);
        }
    }

    void TryTriggerShock()
    {
        if (owner == null || !owner.TryTriggerShock(out _))
            return;

        AudioManager.Instance?.PlaySfx(ClickSfxKey);
        PlayCostTip();
    }

    void PlayCostTip()
    {
        if (costTipUnits.Length == 0)
            return;

        int index = nextCostTipIndex;
        nextCostTipIndex = (nextCostTipIndex + 1) % costTipUnits.Length;

        Sequence previousSequence = costTipSequences[index];
        costTipSequences[index] = null;
        previousSequence?.Kill(false);

        RectTransform unit = costTipUnits[index];
        if (unit == null)
            return;

        Vector3 startPosition = unit.localPosition;
        startPosition.x = UnityEngine.Random.Range(CostTipMinX, CostTipMaxX);
        startPosition.y = CostTipStartY;
        unit.localPosition = startPosition;
        SetCostTipAlpha(costTipGraphics[index], 1f);
        unit.gameObject.SetActive(true);

        float targetY = CostTipStartY + UnityEngine.Random.Range(
            CostTipMinDistance,
            CostTipMaxDistance);
        Sequence sequence = DOTween.Sequence();
        costTipSequences[index] = sequence;
        sequence.Append(unit
            .DOLocalMoveY(
                targetY,
                CostTipMoveDuration * CostTipDurationScale)
            .SetEase(Ease.OutQuad));

        Graphic[] graphics = costTipGraphics[index];
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null)
                continue;

            sequence.Insert(
                CostTipFadeDelay * CostTipDurationScale,
                graphic
                    .DOFade(
                        0f,
                        CostTipFadeDuration * CostTipDurationScale)
                    .SetEase(Ease.Linear));
        }

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() => FinishCostTip(index, sequence))
            .OnKill(() => FinishCostTip(index, sequence));
    }

    void FinishCostTip(int index, Sequence sequence)
    {
        if (index < 0
            || index >= costTipSequences.Length
            || costTipSequences[index] != sequence)
        {
            return;
        }

        costTipSequences[index] = null;
        RectTransform unit = costTipUnits[index];
        if (unit != null)
            unit.gameObject.SetActive(false);
    }

    static void SetCostTipAlpha(Graphic[] graphics, float alpha)
    {
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null)
                continue;

            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }

    void CaptureRestingVisual()
    {
        if (targetImage == null)
            return;

        if (!hasCapturedRestingColor)
        {
            restingColor = targetImage.color;
            hasCapturedRestingColor = true;
        }

        restingAlpha = restingColor.a;
    }

    void ApplyHoverVisual()
    {
        if (targetImage == null || !hasCapturedRestingColor)
            return;

        float brightness = isPointerInside ? HoverBrightness : 1f;
        Color color = targetImage.color;
        color.r = restingColor.r * brightness;
        color.g = restingColor.g * brightness;
        color.b = restingColor.b * brightness;
        targetImage.color = color;
    }

    void KillTransition()
    {
        if (transitionSequence == null)
            return;

        Sequence sequence = transitionSequence;
        transitionSequence = null;
        sequence.Kill(false);
    }

    void KillCostTips()
    {
        for (int i = 0; i < costTipSequences.Length; i++)
        {
            Sequence sequence = costTipSequences[i];
            costTipSequences[i] = null;
            sequence?.Kill(false);

            RectTransform unit = costTipUnits[i];
            if (unit != null)
                unit.gameObject.SetActive(false);
        }
    }
}
