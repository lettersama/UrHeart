using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSWindowButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    const string CloseUnitSfxKey = "CloseUnit";

    static readonly Color TutorialSuppressedColor =
        new Color32(0x65, 0x65, 0x65, 0xFF);

    public enum WindowAction
    {
        Close,
        BringToFront,
        Minimize,
        ToggleAlwaysOnTop
    }

    [Header("References")]
    [SerializeField] OSWindowUnit linkedWindow;
    [SerializeField] Image activeBackground;

    [Header("Always On Top Visual")]
    [SerializeField] Image stateImage;
    [SerializeField] Sprite inactiveSprite;
    [SerializeField] Sprite activeSprite;

    [Header("Action")]
    [SerializeField] WindowAction action;
    [SerializeField, Min(0f)] float fadeDuration = 0.1f;

    bool isPointerInside;
    bool isTutorialSuppressed;
    readonly List<Graphic> tutorialGraphics = new List<Graphic>();
    readonly List<Color> tutorialGraphicColors = new List<Color>();

    public OSWindowUnit LinkedWindow
    {
        get
        {
            ResolveReferences();
            return linkedWindow;
        }
    }
    public WindowAction Action => action;
    // Runtime-created windows clone the authored controls, then retarget them
    // before enabling the clone so no event remains bound to its source window.
    public void BindToWindow(OSWindowUnit window)
    {
        UnbindWindow();
        linkedWindow = window;
        ResolveReferences();
        if (isActiveAndEnabled) BindWindow();
        isPointerInside = false;
        RefreshAlwaysOnTopSprite();
        SetBackgroundAlpha(GetRestingAlpha());
    }

    public bool IsInteractable => !isTutorialSuppressed
        && OS_Handle.Instance?.IsSystemInputLocked != true
        && OS_Handle.Instance?.Chapter03Story?.BlocksWindowInteraction(LinkedWindow) != true;

    void Awake()
    {
        ResolveReferences();
        RefreshAlwaysOnTopSprite();
        SetBackgroundAlpha(GetRestingAlpha());
        ApplyTutorialSuppressedVisual();
    }

    void OnEnable()
    {
        ResolveReferences();
        BindWindow();
        isPointerInside = false;
        RefreshAlwaysOnTopSprite();
        SetBackgroundAlpha(GetRestingAlpha());
    }

    void OnDisable()
    {
        UnbindWindow();
        isPointerInside = false;
        KillFade();
        SetBackgroundAlpha(0f);
    }

    void OnDestroy()
    {
        UnbindWindow();
        KillFade();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsInteractable)
            return;

        isPointerInside = true;
        FadeBackgroundTo(1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!IsInteractable)
            return;

        isPointerInside = false;
        FadeBackgroundTo(GetRestingAlpha());
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsInteractable
            || eventData.button != PointerEventData.InputButton.Left
            || linkedWindow == null)
        {
            return;
        }

        bool interactionSucceeded = false;
        switch (action)
        {
            case WindowAction.Close:
                bool wasOpen = linkedWindow.IsOpen;
                linkedWindow.Close();
                interactionSucceeded = wasOpen && !linkedWindow.IsOpen;
                break;
            case WindowAction.BringToFront:
                int previousSiblingIndex =
                    linkedWindow.transform.GetSiblingIndex();
                linkedWindow.BringToFront();
                interactionSucceeded = previousSiblingIndex
                    != linkedWindow.transform.GetSiblingIndex();
                break;
            case WindowAction.Minimize:
                bool wasMinimized = linkedWindow.IsMinimized;
                linkedWindow.Minimize();
                interactionSucceeded = !wasMinimized
                    && linkedWindow.IsMinimized;
                break;
            case WindowAction.ToggleAlwaysOnTop:
                bool wasAlwaysOnTop = linkedWindow.IsAlwaysOnTop;
                linkedWindow.ToggleAlwaysOnTop();
                interactionSucceeded = wasAlwaysOnTop
                    != linkedWindow.IsAlwaysOnTop;
                break;
        }

        if (interactionSucceeded)
        {
            if (action == WindowAction.Close)
                AudioManager.Instance?.PlaySfx(CloseUnitSfxKey);

            OS_Handle.Instance?.PlayInteractionFeedback();
        }
    }

    public void SetTutorialSuppressed(bool isSuppressed)
    {
        if (isTutorialSuppressed == isSuppressed)
        {
            ApplyTutorialSuppressedVisual();
            return;
        }

        KillFade();
        isPointerInside = false;
        if (isSuppressed)
        {
            SetBackgroundAlpha(GetRestingAlpha());
            CacheTutorialGraphicColors();
        }

        isTutorialSuppressed = isSuppressed;
        if (isSuppressed)
            ApplyTutorialSuppressedVisual();
        else
            RestoreTutorialGraphicColors();
    }

    void BindWindow()
    {
        if (linkedWindow == null)
            return;

        linkedWindow.AlwaysOnTopChanged -= HandleAlwaysOnTopChanged;
        linkedWindow.AlwaysOnTopChanged += HandleAlwaysOnTopChanged;
    }

    void UnbindWindow()
    {
        if (linkedWindow != null)
        {
            linkedWindow.AlwaysOnTopChanged -=
                HandleAlwaysOnTopChanged;
        }
    }

    void HandleAlwaysOnTopChanged(OSWindowUnit window, bool isPinned)
    {
        if (action != WindowAction.ToggleAlwaysOnTop
            || window != linkedWindow)
        {
            return;
        }

        SetAlwaysOnTopSprite(isPinned);
        if (isPointerInside)
            return;

        FadeBackgroundTo(isPinned ? 1f : 0f);
    }

    void RefreshAlwaysOnTopSprite()
    {
        if (action != WindowAction.ToggleAlwaysOnTop)
            return;

        SetAlwaysOnTopSprite(
            linkedWindow != null && linkedWindow.IsAlwaysOnTop);
    }

    void SetAlwaysOnTopSprite(bool isPinned)
    {
        if (stateImage == null)
            return;

        Sprite targetSprite = isPinned ? activeSprite : inactiveSprite;
        if (targetSprite != null)
            stateImage.sprite = targetSprite;
    }

    float GetRestingAlpha()
    {
        return action == WindowAction.ToggleAlwaysOnTop
            && linkedWindow != null
            && linkedWindow.IsAlwaysOnTop
                ? 1f
                : 0f;
    }

    void FadeBackgroundTo(float alpha)
    {
        ResolveReferences();
        if (activeBackground == null)
            return;

        activeBackground.DOKill(false);
        float duration = Mathf.Max(0f, fadeDuration);
        if (duration <= 0f)
        {
            SetBackgroundAlpha(alpha);
            return;
        }

        activeBackground
            .DOFade(alpha, duration)
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    void ResolveReferences()
    {
        if (linkedWindow == null)
            linkedWindow = GetComponentInParent<OSWindowUnit>(true);

        if (activeBackground == null)
            activeBackground = transform.Find("Activebk")?.GetComponent<Image>();

        if (stateImage == null)
            stateImage = GetComponent<Image>();

        if (inactiveSprite == null && stateImage != null)
            inactiveSprite = stateImage.sprite;
    }

    void KillFade()
    {
        activeBackground?.DOKill(false);
    }

    void SetBackgroundAlpha(float alpha)
    {
        if (activeBackground == null)
            return;

        Color color = activeBackground.color;
        color.a = Mathf.Clamp01(alpha);
        activeBackground.color = color;
    }

    void CacheTutorialGraphicColors()
    {
        tutorialGraphics.Clear();
        tutorialGraphicColors.Clear();
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null)
                continue;

            tutorialGraphics.Add(graphic);
            tutorialGraphicColors.Add(graphic.color);
        }
    }

    void ApplyTutorialSuppressedVisual()
    {
        if (!isTutorialSuppressed)
            return;

        if (tutorialGraphics.Count == 0)
            CacheTutorialGraphicColors();

        for (int i = 0; i < tutorialGraphics.Count; i++)
        {
            Graphic graphic = tutorialGraphics[i];
            if (graphic == null)
                continue;

            Color color = TutorialSuppressedColor;
            color.a = graphic.color.a;
            graphic.color = color;
        }
    }

    void RestoreTutorialGraphicColors()
    {
        int count = Mathf.Min(
            tutorialGraphics.Count,
            tutorialGraphicColors.Count);
        for (int i = 0; i < count; i++)
        {
            if (tutorialGraphics[i] != null)
                tutorialGraphics[i].color = tutorialGraphicColors[i];
        }

        tutorialGraphics.Clear();
        tutorialGraphicColors.Clear();
        SetBackgroundAlpha(GetRestingAlpha());
    }

    void Reset()
    {
        ResolveReferences();
        RefreshAlwaysOnTopSprite();
        SetBackgroundAlpha(GetRestingAlpha());
    }
}
