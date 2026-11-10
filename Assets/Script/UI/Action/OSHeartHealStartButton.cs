using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSHeartHealStartButton : MonoBehaviour,
    IPointerClickHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    const string ActiveSpriteName = "OSList_02_13";
    const string ButtonSfxKey = "HeartStartButton";
    const float HoverBrightness = 0.7f;
    const string StartKey = "ui.os.heart_heal.start";
    const string StopKey = "ui.os.heart_heal.stop";
    const string StartFallback = "开始";
    const string StopFallback = "停止";

    [SerializeField] Image targetImage;
    [SerializeField] Sprite normalSprite;
    [SerializeField] Sprite activeSprite;
    [SerializeField] TMP_Text stateLabel;
    [SerializeField] OSLocalizedText localizedLabel;
    [SerializeField] OSHeartHealToolWindowController owner;
    [Header("Physical Key")]
    [Tooltip("Content that sinks with the key face while treatment runs, such as the label.")]
    [SerializeField] RectTransform pressedContent;
    [SerializeField] float pressedContentOffset = 3f;

    OS_Handle osHandle;
    Color restingColor = Color.white;
    bool ownsActiveSprite;
    bool isPointerInside;
    bool hasCapturedRestingColor;
    Vector2 pressedContentRestPosition;
    bool hasPressedContentRest;

    void Awake()
    {
        ResolveReferences();
        CaptureRestingVisual();
        isPointerInside = false;
        SetTreatmentActive(false);
        ApplyHoverVisual();
    }

    void OnEnable()
    {
        ResolveReferences();
        CaptureRestingVisual();
        isPointerInside = false;
        SetTreatmentActive(owner != null && owner.IsTreating);
        ApplyHoverVisual();
    }

    void OnDisable()
    {
        isPointerInside = false;
        ApplyHoverVisual();
    }

    void OnDestroy()
    {
        if (!ownsActiveSprite || activeSprite == null)
            return;

        if (Application.isPlaying)
            Destroy(activeSprite);
        else
            DestroyImmediate(activeSprite);
    }

    public void Configure(OSHeartHealToolWindowController controller)
    {
        owner = controller;
        ResolveReferences();
        SetTreatmentActive(owner != null && owner.IsTreating);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left
            || owner == null)
        {
            return;
        }

        bool actionAccepted = owner.IsTreating
            ? owner.StopTreatment()
            : owner.StartTreatment();
        if (actionAccepted)
            AudioManager.Instance?.PlaySfx(ButtonSfxKey);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        ApplyHoverVisual();
        owner?.NotifyStartButtonPointerEntered();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        ApplyHoverVisual();
    }

    public void SetTreatmentActive(bool active)
    {
        ResolveReferences();
        if (targetImage != null)
        {
            Sprite nextSprite = active ? activeSprite : normalSprite;
            if (nextSprite != null)
                targetImage.sprite = nextSprite;
        }

        if (pressedContent != null)
        {
            if (!hasPressedContentRest)
            {
                pressedContentRestPosition = pressedContent.anchoredPosition;
                hasPressedContentRest = true;
            }

            pressedContent.anchoredPosition = pressedContentRestPosition
                + (active ? Vector2.down * pressedContentOffset : Vector2.zero);
        }

        localizedLabel?.SetLocalization(
            active ? StopKey : StartKey,
            active ? StopFallback : StartFallback,
            osHandle);
        ApplyHoverVisual();
    }

    public void RefreshLocalization()
    {
        SetTreatmentActive(owner != null && owner.IsTreating);
    }

    void ResolveReferences()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
        if (normalSprite == null && targetImage != null)
            normalSprite = targetImage.sprite;
        if (owner == null)
            owner = GetComponentInParent<OSHeartHealToolWindowController>(true);
        if (osHandle == null)
            osHandle = GetComponentInParent<OS_Handle>(true);
        if (stateLabel == null)
            stateLabel = GetComponentInChildren<TMP_Text>(true);
        if (localizedLabel == null && stateLabel != null)
            localizedLabel = stateLabel.GetComponent<OSLocalizedText>();
        if (localizedLabel == null && stateLabel != null)
            localizedLabel = stateLabel.gameObject.AddComponent<OSLocalizedText>();

        localizedLabel?.Initialize();
        ResolveActiveSprite();
    }

    void CaptureRestingVisual()
    {
        if (targetImage == null || hasCapturedRestingColor)
            return;

        restingColor = targetImage.color;
        hasCapturedRestingColor = true;
    }

    void ApplyHoverVisual()
    {
        if (targetImage == null || !hasCapturedRestingColor)
            return;

        float brightness = isPointerInside ? HoverBrightness : 1f;
        targetImage.color = new Color(
            restingColor.r * brightness,
            restingColor.g * brightness,
            restingColor.b * brightness,
            restingColor.a);
    }

    void ResolveActiveSprite()
    {
        if (activeSprite != null || normalSprite == null)
            return;

        Sprite[] loadedSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < loadedSprites.Length; i++)
        {
            Sprite candidate = loadedSprites[i];
            if (candidate != null && candidate.name == ActiveSpriteName)
            {
                activeSprite = candidate;
                return;
            }
        }

        Rect normalRect = normalSprite.rect;
        Rect activeRect = new Rect(
            normalRect.x + normalRect.width + 1f,
            normalRect.y,
            normalRect.width,
            normalRect.height);
        if (activeRect.xMax > normalSprite.texture.width
            || activeRect.yMax > normalSprite.texture.height)
        {
            return;
        }

        Vector2 pivot = new Vector2(
            normalSprite.pivot.x / normalRect.width,
            normalSprite.pivot.y / normalRect.height);
        activeSprite = Sprite.Create(
            normalSprite.texture,
            activeRect,
            pivot,
            normalSprite.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            normalSprite.border);
        activeSprite.name = ActiveSpriteName + " (Runtime)";
        ownsActiveSprite = true;
    }
}
