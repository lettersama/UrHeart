using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class ProcessUnit : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("References")]
    [SerializeField] Image hoverImage;
    [SerializeField] Image processIcon;

    [Header("Hover")]
    [SerializeField, Range(0f, 1f)] float activeWindowAlpha = 0.6f;
    [SerializeField, Range(0f, 1f)] float alwaysOnTopWindowAlpha = 0.45f;
    [SerializeField, Min(0f)] float fadeDuration = 0.15f;

    OSWindowUnit linkedWindow;
    Vector2 processIconSizeDelta;
    bool hasCachedProcessIconSize;
    bool isPointerInside;
    bool isLinkedWindowMouseActive;
    bool isLinkedWindowAlwaysOnTop;

    public bool IsAvailable => linkedWindow == null;
    public OSWindowUnit LinkedWindow => linkedWindow;
    public Image ProcessIcon => processIcon;

    void Awake()
    {
        ResolveReferences();
        CacheProcessIconSize();
        RestoreProcessIconSize();
        SetHoverAlpha(GetRestingAlpha());
    }

    void OnEnable()
    {
        ResolveReferences();
        CacheProcessIconSize();
        RestoreProcessIconSize();
        isPointerInside = false;
        SetHoverAlpha(GetRestingAlpha());
    }

    void OnDisable()
    {
        KillFade();
        isPointerInside = false;
        SetHoverAlpha(0f);
    }

    void OnDestroy()
    {
        KillFade();
    }

    public void Initialize(OSWindowUnit window, Image sourceIcon)
    {
        if (window == null)
            return;

        linkedWindow = window;
        isLinkedWindowMouseActive = false;
        isLinkedWindowAlwaysOnTop = window.IsAlwaysOnTop;
        isPointerInside = false;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResolveReferences();
        CacheProcessIconSize();
        RestoreProcessIconSize();
        if (processIcon != null)
        {
            processIcon.sprite = sourceIcon != null ? sourceIcon.sprite : null;
            processIcon.enabled = processIcon.sprite != null;
        }

        SetHoverAlpha(GetRestingAlpha());
    }

    public void PrepareForPool()
    {
        linkedWindow = null;
        isLinkedWindowMouseActive = false;
        isLinkedWindowAlwaysOnTop = false;
        isPointerInside = false;
        KillFade();
        RestoreProcessIconSize();
        SetHoverAlpha(0f);
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void Release(OSWindowUnit window)
    {
        if (linkedWindow != window)
            return;

        PrepareForPool();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (OS_Handle.Instance?.IsSystemInputLocked == true)
            return;

        isPointerInside = true;
        FadeHoverTo(1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        FadeHoverTo(GetRestingAlpha());
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (OS_Handle.Instance?.IsSystemInputLocked == true
            || eventData.button != PointerEventData.InputButton.Left)
            return;

        if (linkedWindow == null)
            return;

        OS_Handle osHandle = OS_Handle.Instance;
        bool isMouseActive = osHandle != null
            ? osHandle.IsWindowMouseActive(linkedWindow)
            : isLinkedWindowMouseActive;
        bool wasMinimized = linkedWindow.IsMinimized;
        if (linkedWindow.IsOpen
            && !linkedWindow.IsMinimized
            && isMouseActive)
        {
            linkedWindow.Minimize();
        }
        else
        {
            linkedWindow.RestoreAndBringToFront();
            osHandle?.NotifyTaskbarProcessPressed(linkedWindow);
        }

        if (linkedWindow.IsOpen
            && wasMinimized != linkedWindow.IsMinimized)
        {
            osHandle?.PlayInteractionFeedback();
        }
    }

    public void SetLinkedWindowMouseActive(bool isActive)
    {
        SetLinkedWindowTaskbarState(
            isActive,
            linkedWindow != null && linkedWindow.IsAlwaysOnTop);
    }

    public void SetLinkedWindowTaskbarState(
        bool isMouseActive,
        bool isAlwaysOnTop)
    {
        bool hasLinkedWindow = linkedWindow != null;
        isLinkedWindowMouseActive = isMouseActive && hasLinkedWindow;
        isLinkedWindowAlwaysOnTop = isAlwaysOnTop && hasLinkedWindow;
        if (!isActiveAndEnabled)
            return;

        KillFade();
        SetHoverAlpha(isPointerInside ? 1f : GetRestingAlpha());
    }

    void FadeHoverTo(float alpha)
    {
        ResolveReferences();
        if (hoverImage == null)
            return;

        hoverImage.DOKill(false);
        float duration = Mathf.Max(0f, fadeDuration);
        if (duration <= 0f)
        {
            SetHoverAlpha(alpha);
            return;
        }

        hoverImage
            .DOFade(alpha, duration)
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    void ResolveReferences()
    {
        if (hoverImage == null)
            hoverImage = GetComponent<Image>();

        if (processIcon == null)
            processIcon = transform.Find("ProcessIcon")?.GetComponent<Image>();
    }

    void CacheProcessIconSize()
    {
        if (hasCachedProcessIconSize || processIcon == null)
            return;

        processIconSizeDelta = processIcon.rectTransform.sizeDelta;
        hasCachedProcessIconSize = true;
    }

    void RestoreProcessIconSize()
    {
        if (!hasCachedProcessIconSize || processIcon == null)
            return;

        processIcon.rectTransform.sizeDelta = processIconSizeDelta;
    }

    void KillFade()
    {
        hoverImage?.DOKill(false);
    }

    void SetHoverAlpha(float alpha)
    {
        if (hoverImage == null)
            return;

        Color color = hoverImage.color;
        color.a = Mathf.Clamp01(alpha);
        hoverImage.color = color;
    }

    float GetRestingAlpha()
    {
        if (isLinkedWindowMouseActive)
            return Mathf.Clamp01(activeWindowAlpha);

        return isLinkedWindowAlwaysOnTop
            ? Mathf.Clamp01(alwaysOnTopWindowAlpha)
            : 0f;
    }

    void Reset()
    {
        ResolveReferences();
        SetHoverAlpha(0f);
    }
}
