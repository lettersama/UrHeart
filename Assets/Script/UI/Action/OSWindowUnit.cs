using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSWindowUnit : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerEnterHandler,
    IPointerMoveHandler,
    IPointerExitHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    const float DragWindowSafeInset = 10f;
    const float MinimumVisibleDragSpan = 32f;

    [Header("References")]
    [SerializeField] RectTransform windowRect;
    [SerializeField] RectTransform dragArea;

    [SerializeField] string windowId;
    [SerializeField] CanvasGroup[] contentGroups = new CanvasGroup[0];

    [Header("Window Size")]
    [SerializeField] Vector2 recordedSize = new Vector2(340f, 280f);
    [SerializeField] Vector2 minimumSize = new Vector2(200f, 120f);
    [SerializeField, Min(0f)] float collapsedHeight = 30f;
    [SerializeField, Min(0f)] float resizeBorder = 6f;
    [SerializeField] bool allowMove = true;
    [SerializeField] bool allowResize = true;
    [SerializeField] bool forceTopmost;

    [Header("Open Effect")]
    [SerializeField] bool animateOnOpen = true;
    [SerializeField, Min(0f)] float openDuration = 0.25f;
    [SerializeField, Min(0f)] float contentRevealDelay = 0.1f;
    [SerializeField, Min(0f)] float contentFadeDuration = 0.15f;

    ProcessUnit linkedProcessUnit;
    Sequence openSequence;
    Sequence closeSequence;
    CanvasGroup closeEffectGroup;
    Vector3 scaleBeforeClose;
    float alphaBeforeClose;
    bool interactableBeforeClose;
    bool raycastsBeforeClose;
    bool isOpen;
    bool isMinimized;
    bool isAlwaysOnTop;
    bool isInitializing;
    bool moveWindow;
    object presentationOffsetOwner;
    object presentationBackgroundOwner;
    bool presentationOffsetAllowsPointerInteraction;
    Vector2 presentationOffset;
    Vector2 presentationOffsetPosition;
    OSWindowLayoutSaveData layoutBeforeTemporaryOverride;
    Vector2 minimumSizeBeforeTemporaryOverride;
    bool allowMoveBeforeTemporaryOverride;
    bool allowResizeBeforeTemporaryOverride;

    bool resizeLeft;
    bool resizeRight;
    bool resizeTop;
    bool resizeBottom;
    readonly Vector3[] dragWindowCorners = new Vector3[4];

    public bool IsOpen => isOpen;
    public bool IsOpening => isInitializing;
    public float OpenAnimationDuration => animateOnOpen
        ? Mathf.Max(Mathf.Max(0f, openDuration),
            Mathf.Max(0f, contentRevealDelay) + Mathf.Max(0f, contentFadeDuration))
        : 0f;
    public bool IsClosing => closeSequence != null;
    public bool IsMinimized => isMinimized;
    public bool IsAlwaysOnTop => isAlwaysOnTop;
    public bool IsForceTopmost => forceTopmost;
    public bool AllowsMove => allowMove;
    public bool AllowsResize => allowResize;
    public Vector2 RecordedSize => recordedSize;
    public bool HasTemporaryOpeningLayout => layoutBeforeTemporaryOverride != null;
    public bool IsPointerManipulating => HasActivePointerInteraction();
    public Vector2 PresentationOffset => HasCurrentPresentationOffset ? presentationOffset : Vector2.zero;
    public Vector3 PresentationWorldOffset => windowRect != null && windowRect.parent != null
        ? windowRect.parent.TransformVector(PresentationOffset) : (Vector3)PresentationOffset;
    bool HasCurrentPresentationOffset => presentationOffsetOwner != null && windowRect != null
        && (windowRect.anchoredPosition - presentationOffsetPosition).sqrMagnitude < .0001f;
    public RectTransform WindowRect
    {
        get
        {
            if (windowRect == null)
                ResolveReferences();

            return windowRect;
        }
    }
    public ProcessUnit LinkedProcessUnit => linkedProcessUnit;
    public string WindowId => string.IsNullOrWhiteSpace(windowId)
        ? gameObject.name
        : windowId;

    public event Action<OSWindowUnit> ContentInitializationRequested;
    public event Action<OSWindowUnit> OpenStarted;
    public event Action<OSWindowUnit> OpenAnimationCompleted;
    public event Action<OSWindowUnit> Closed;
    public event Action<OSWindowUnit> LayoutChanged;
    public event Action<OSWindowUnit> MoveStarted;
    public event Action<OSWindowUnit, bool> AlwaysOnTopChanged;
    public event Action<OSWindowUnit, bool> AlwaysOnTopToggled;

    void Awake()
    {
        ResolveReferences();
        SanitizeSettings();
    }

    void OnDisable()
    {
        ClearPresentationOffset();
        CancelCloseAnimation();
        KillOpenSequence();
        CursorHandle.Instance?.ClearContextState(this);
        CursorHandle.Instance?.ClearPositionConstraint(this);
        ClearResizeState();
        OS_Handle.Instance?.NotifyWindowUnavailable(this);
    }

    void OnDestroy()
    {
        ClearPresentationOffset();
        CancelCloseAnimation();
        KillOpenSequence();
        linkedProcessUnit?.Release(this);
        linkedProcessUnit = null;
        CursorHandle.Instance?.ClearPositionConstraint(this);
        OS_Handle.Instance?.NotifyWindowUnavailable(this);
    }

    void LateUpdate()
    {
        if (presentationBackgroundOwner != null || !forceTopmost
            || !isOpen
            || transform.parent == null
            || transform.GetSiblingIndex() == transform.parent.childCount - 1)
        {
            return;
        }

        transform.SetAsLastSibling();
        NotifyLayoutChanged();
    }

    public void PrepareRuntimeClosed()
    {
        ClearPresentationOffset();
        CancelCloseAnimation();
        ResolveReferences();
        SanitizeSettings();
        KillOpenSequence();
        isOpen = false;
        isMinimized = false;
        // A runtime reset may run on a restored window, not only during startup.
        // Release both ends of its taskbar binding before forgetting it.
        linkedProcessUnit?.Release(this);
        linkedProcessUnit = null;
        SetAlwaysOnTop(false);
        SetContentAlpha(0f);

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void Open(ProcessUnit processUnit)
    {
        if (processUnit == null)
            return;

        OpenInternal(processUnit, null);
    }

    public void Open(ProcessUnit processUnit, float durationOverride)
    {
        if (processUnit == null)
            return;

        OpenInternal(processUnit, Mathf.Max(0f, durationOverride));
    }

    public void OpenStandalone()
    {
        if (isOpen)
        {
            RestoreAndBringToFront();
            return;
        }

        OpenInternal(null, null);
    }

    // Story stages can already be present when the view arrives at them.
    public void OpenStandalone(float durationOverride)
    {
        if (isOpen) { RestoreAndBringToFront(); return; }
        OpenInternal(null, Mathf.Max(0f, durationOverride));
    }

    /// <summary>Resume a story-owned window at its saved pose without replaying its opening tween.</summary>
    public void CompleteOpeningFromScript()
    {
        if (isInitializing) CompleteOpenVisuals();
    }

    void OpenInternal(ProcessUnit processUnit, float? durationOverride)
    {
        ClearPresentationOffset();
        CancelCloseAnimation();
        ResolveReferences();
        SanitizeSettings();
        linkedProcessUnit = processUnit;
        isOpen = true;
        isMinimized = false;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        BringToFront();
        OpenStarted?.Invoke(this);
        if (!isOpen || !gameObject.activeInHierarchy)
            return;

        if (animateOnOpen)
            PlayOpenAnimation(durationOverride);
        else
            OpenImmediately();
    }

    public void Close()
    {
        if (OS_Handle.Instance?.IsWindowDismissalBlocked(this) == true)
            return;
        CloseInternal(true);
    }

    public void CloseSilently()
    {
        CloseInternal(false);
    }

    // Story-owned dismissal bypasses the player's close guard, but keeps
    // the Closed event that subsequent scripted interactions wait for.
    public void CloseFromScript()
    {
        CloseInternal(true);
    }

    // Explicit opt-in: the close button continues to use immediate Close().
    public void CloseAnimated() => CloseAnimated(0.32f);

    public void CloseAnimated(float duration, bool notifyClosed = true)
    {
        if (!isOpen || IsClosing)
            return;
        ClearPresentationOffset();
        if (!gameObject.activeInHierarchy || duration <= 0f
            || float.IsNaN(duration) || float.IsInfinity(duration))
        {
            CloseInternal(notifyClosed);
            return;
        }

        if (isInitializing)
            CompleteOpenVisuals();
        RecordCurrentSize();
        ClearResizeState();
        closeEffectGroup = GetComponent<CanvasGroup>();
        if (closeEffectGroup == null)
            closeEffectGroup = gameObject.AddComponent<CanvasGroup>();
        scaleBeforeClose = transform.localScale;
        alphaBeforeClose = closeEffectGroup.alpha;
        interactableBeforeClose = closeEffectGroup.interactable;
        raycastsBeforeClose = closeEffectGroup.blocksRaycasts;
        closeEffectGroup.interactable = false;
        closeEffectGroup.blocksRaycasts = false;

        // A small inward contraction and whole-window fade leave layout and anchors intact.
        Sequence sequence = DOTween.Sequence();
        closeSequence = sequence;
        sequence.Append(transform.DOScale(
            Vector3.Scale(scaleBeforeClose, new Vector3(0.94f, 0.86f, 1f)), duration)
            .SetEase(Ease.InCubic));
        sequence.Join(closeEffectGroup.DOFade(0f, duration).SetEase(Ease.InQuad));
        sequence.SetUpdate(true).OnComplete(() =>
        {
            if (closeSequence == sequence)
                CloseInternal(notifyClosed);
        }).OnKill(() =>
        {
            if (closeSequence == sequence)
                CancelCloseAnimation();
        });
    }

    void CancelCloseAnimation()
    {
        if (closeSequence == null)
            return;
        Sequence sequence = closeSequence;
        closeSequence = null;
        sequence.Kill(false);
        transform.localScale = scaleBeforeClose;
        if (closeEffectGroup != null)
        {
            closeEffectGroup.alpha = alphaBeforeClose;
            closeEffectGroup.interactable = interactableBeforeClose;
            closeEffectGroup.blocksRaycasts = raycastsBeforeClose;
        }
    }

    void CloseInternal(bool notifyClosed)
    {
        ClearPresentationOffset();
        CancelCloseAnimation();
        if (!isOpen)
            return;

        if (isInitializing)
            CompleteOpenVisuals();
        else
            RecordCurrentSize();

        isOpen = false;
        isMinimized = false;
        SetAlwaysOnTop(false);
        ProcessUnit processUnit = linkedProcessUnit;
        linkedProcessUnit = null;
        SetContentAlpha(0f);
        NotifyLayoutChanged();
        if (notifyClosed)
            Closed?.Invoke(this);
        gameObject.SetActive(false);
        processUnit?.Release(this);
    }

    public void Minimize()
    {
        // Taskbar clicks bypass the title-bar buttons, so guard the window operation too.
        if (OS_Handle.Instance?.IsWindowDismissalBlocked(this) == true)
            return;
        CancelCloseAnimation();
        if (!isOpen || isMinimized)
            return;

        ClearPresentationOffset();

        if (isInitializing)
            CompleteOpenVisuals();

        isMinimized = true;
        NotifyLayoutChanged();
        gameObject.SetActive(false);
    }

    public void RestoreAndBringToFront()
    {
        var os = OS_Handle.Instance;
        if (os?.Chapter03Entry?.BlocksMemory == true
            && os.MemoryWindowController != null
            && os.MemoryWindowController.GetComponent<OSWindowUnit>() == this) return;
        CancelCloseAnimation();
        if (!isOpen)
            return;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        isMinimized = false;
        SetContentAlpha(1f);
        BringToFront();
    }

    public void BringToFront()
    {
        if (presentationBackgroundOwner != null || !isOpen || !gameObject.activeSelf)
            return;

        transform.SetAsLastSibling();
        ReapplyAlwaysOnTopSiblings(transform.parent, this);
        NotifyLayoutChanged();
    }

    /// <summary>Temporarily keep a story's frozen window behind usable windows without changing its saved pin.</summary>
    public bool SetPresentationBackground(object owner, bool background)
    {
        if (owner == null) return false;
        if (background)
        {
            if (presentationBackgroundOwner != null)
                return ReferenceEquals(presentationBackgroundOwner, owner);
            presentationBackgroundOwner = owner;
            if (transform.parent != null) transform.SetAsFirstSibling();
        }
        else
        {
            if (!ReferenceEquals(presentationBackgroundOwner, owner)) return false;
            presentationBackgroundOwner = null;
            if (isOpen) ReapplyAlwaysOnTopSiblings(transform.parent, this);
        }
        NotifyLayoutChanged();
        return true;
    }

    public void ToggleAlwaysOnTop()
    {
        SetAlwaysOnTop(!isAlwaysOnTop);
        AlwaysOnTopToggled?.Invoke(this, isAlwaysOnTop);
    }

    public void SetAlwaysOnTop(bool alwaysOnTop)
    {
        if (isAlwaysOnTop == alwaysOnTop)
            return;

        isAlwaysOnTop = alwaysOnTop;
        if (isAlwaysOnTop)
        {
            BringToFront();
        }
        else
        {
            ReapplyAlwaysOnTopSiblings(transform.parent, this);
            NotifyLayoutChanged();
        }

        AlwaysOnTopChanged?.Invoke(this, isAlwaysOnTop);
    }

    static void ReapplyAlwaysOnTopSiblings(
        Transform parent,
        OSWindowUnit source)
    {
        if (parent == null)
            return;

        List<OSWindowUnit> pinnedWindows = new List<OSWindowUnit>();
        List<OSWindowUnit> forcedWindows = new List<OSWindowUnit>();
        for (int i = 0; i < parent.childCount; i++)
        {
            OSWindowUnit window = parent.GetChild(i)
                .GetComponent<OSWindowUnit>();
            if (window != null && window.isOpen && window.isAlwaysOnTop
                && window.presentationBackgroundOwner == null)
            {
                if (window.forceTopmost)
                    forcedWindows.Add(window);
                else
                    pinnedWindows.Add(window);
            }
        }

        MoveWindowsToFront(pinnedWindows, source);
        MoveWindowsToFront(forcedWindows, source);
    }

    static void MoveWindowsToFront(
        List<OSWindowUnit> windows,
        OSWindowUnit source)
    {
        for (int i = 0; i < windows.Count; i++)
        {
            OSWindowUnit window = windows[i];
            int previousIndex = window.transform.GetSiblingIndex();
            window.transform.SetAsLastSibling();
            if (window != source
                && window.transform.GetSiblingIndex() != previousIndex)
            {
                window.NotifyLayoutChanged();
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (presentationBackgroundOwner != null) return;
        if (IsSystemInputLocked())
        {
            ClearResizeState();
            return;
        }

        // A real pointer operation takes over before hit testing or saving its layout.
        ClearPresentationOffset();

        OS_Handle osHandle = OS_Handle.Instance;
        if (osHandle != null)
            osHandle.NotifyWindowPointerPressed(this);
        else
            BringToFront();
        ClearResizeState();

        if (!isOpen || isInitializing || windowRect == null)
            return;

        GameObject pointerTarget =
            eventData.pointerCurrentRaycast.gameObject;
        if (pointerTarget == null)
            pointerTarget = eventData.pointerPressRaycast.gameObject;

        if (TryGetResizeDirections(
                eventData,
                out resizeLeft,
                out resizeRight,
                out resizeTop,
                out resizeBottom)
            && HasResizeDirection())
        {
            UpdateCursorState(
                GetResizeCursorState(
                    resizeLeft,
                    resizeRight,
                    resizeTop,
                    resizeBottom));
            return;
        }

        if (allowMove && IsDragAreaTarget(pointerTarget))
            moveWindow = true;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsSystemInputLocked())
        {
            CursorHandle.Instance?.ClearContextState(this);
            return;
        }

        if (!HasActivePointerInteraction())
            UpdateCursorState(eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (IsSystemInputLocked())
        {
            CursorHandle.Instance?.ClearContextState(this);
            return;
        }

        if (!HasActivePointerInteraction())
            UpdateCursorState(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!HasActivePointerInteraction())
            CursorHandle.Instance?.ClearContextState(this);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsSystemInputLocked())
        {
            ClearResizeState();
            return;
        }

        if (!HasActivePointerInteraction())
            return;

        ClearResizeState();

        GameObject pointerTarget =
            eventData.pointerCurrentRaycast.gameObject;
        if (IsWindowTarget(pointerTarget))
            UpdateCursorState(eventData);
        else
            CursorHandle.Instance?.ClearContextState(this);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsSystemInputLocked() || !moveWindow)
            return;

        MoveStarted?.Invoke(this);
        // Clamp the window, not the pointer. A pointer outside the desktop must
        // still be able to pull a window back immediately.
        CursorHandle.Instance?.ClearPositionConstraint(this);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsSystemInputLocked()
            || (!moveWindow && !HasResizeDirection())
            || windowRect == null)
        {
            return;
        }

        RectTransform parentRect = windowRect.parent as RectTransform;
        if (parentRect == null)
            return;

        Vector2 currentScreenPosition = eventData.position;
        Vector2 previousScreenPosition =
            currentScreenPosition - eventData.delta;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                currentScreenPosition,
                eventData.pressEventCamera,
                out Vector2 currentLocal)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                previousScreenPosition,
                eventData.pressEventCamera,
                out Vector2 previousLocal))
        {
            return;
        }

        Vector2 localDelta = currentLocal - previousLocal;
        if (moveWindow)
        {
            bool carriesOffset = presentationOffsetAllowsPointerInteraction && HasCurrentPresentationOffset;
            Vector2 previousPosition = windowRect.anchoredPosition;
            windowRect.anchoredPosition += localDelta;
            ClampDraggedWindowToBounds(parentRect);
            if (carriesOffset) presentationOffsetPosition += windowRect.anchoredPosition - previousPosition;
        }
        else
            ResizeBy(localDelta);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (IsSystemInputLocked())
        {
            ClearResizeState();
            return;
        }

        RecordCurrentSize();
        CursorHandle.Instance?.ClearPositionConstraint(this);
        ClearResizeState();
        NotifyLayoutChanged();

        GameObject pointerTarget =
            eventData.pointerCurrentRaycast.gameObject;
        if (IsWindowTarget(pointerTarget))
            UpdateCursorState(eventData);
        else
            CursorHandle.Instance?.ClearContextState(this);
    }

    public OSWindowLayoutSaveData CaptureLayout()
    {
        ResolveReferences();
        if (windowRect == null)
            return null;

        // A story composition must not replace the player's ordinary window layout in saves.
        if (layoutBeforeTemporaryOverride != null)
            return layoutBeforeTemporaryOverride;

        Vector2 size = !allowResize || isInitializing
            ? recordedSize
            : windowRect.rect.size;
        return new OSWindowLayoutSaveData
        {
            windowId = WindowId,
            anchoredPosition = windowRect.anchoredPosition - PresentationOffset,
            size = size,
            siblingIndex = transform.GetSiblingIndex()
        };
    }

    public void ConfigureInteraction(bool canMove, bool canResize)
    {
        allowMove = canMove;
        allowResize = canResize;
        ClearResizeState();
    }

    public void ApplyLayout(OSWindowLayoutSaveData layout)
    {
        if (layout == null)
            return;

        ClearPresentationOffset();

        if (layoutBeforeTemporaryOverride != null)
        {
            layoutBeforeTemporaryOverride = layout;
            return;
        }

        CancelCloseAnimation();

        ResolveReferences();
        if (windowRect == null)
            return;

        if (allowResize)
        {
            recordedSize = new Vector2(
                Mathf.Max(minimumSize.x, layout.size.x),
                Mathf.Max(minimumSize.y, layout.size.y));
        }

        windowRect.sizeDelta = recordedSize;
        if (allowMove || !forceTopmost)
            windowRect.anchoredPosition = layout.anchoredPosition;

        if (transform.parent != null && !forceTopmost)
        {
            int maxIndex = Mathf.Max(0, transform.parent.childCount - 1);
            transform.SetSiblingIndex(
                Mathf.Clamp(layout.siblingIndex, 0, maxIndex));
        }
    }

    public void SetTemporaryOpeningLayout(Vector2 minimum, Vector2 position, Vector2 size)
    {
        // Reopening/minimizing the same story layout must keep the player's temporary move.
        if (HasTemporaryOpeningLayout)
            return;
        layoutBeforeTemporaryOverride = CaptureLayout();
        minimumSizeBeforeTemporaryOverride = minimumSize;
        allowMoveBeforeTemporaryOverride = allowMove;
        allowResizeBeforeTemporaryOverride = allowResize;
        minimumSize = new Vector2(Mathf.Max(1f, minimum.x), Mathf.Max(1f, minimum.y));
        SetOpeningLayout(position, size);
    }

    public void ClearTemporaryOpeningLayout()
    {
        if (layoutBeforeTemporaryOverride == null)
            return;
        // An explicit runtime reset can happen during the opening tween.
        // Finish it first so it cannot write the temporary size back after restoration.
        if (isInitializing)
            CompleteOpenVisuals();
        OSWindowLayoutSaveData layout = layoutBeforeTemporaryOverride;
        layoutBeforeTemporaryOverride = null;
        minimumSize = minimumSizeBeforeTemporaryOverride;
        ConfigureInteraction(allowMoveBeforeTemporaryOverride, allowResizeBeforeTemporaryOverride);
        // Non-resizable windows still need their original size after a story
        // expansion; ApplyLayout intentionally ignores size for ordinary restores.
        recordedSize = layout.size;
        ApplyLayout(layout);
    }

    public void SetOpeningLayout(
        Vector2 anchoredPosition,
        Vector2 size)
    {
        ClearPresentationOffset();
        CancelCloseAnimation();
        ResolveReferences();
        if (windowRect == null)
            return;

        recordedSize = new Vector2(
            Mathf.Max(minimumSize.x, size.x),
            Mathf.Max(minimumSize.y, size.y));
        windowRect.sizeDelta = recordedSize;
        windowRect.anchoredPosition = anchoredPosition;
        Vector3 localPosition = windowRect.localPosition;
        localPosition.z = 0f;
        windowRect.localPosition = localPosition;
    }

    /// <summary>Temporary whole-window motion; CaptureLayout always returns the player's base position.</summary>
    public bool TrySetPresentationOffset(object owner, Vector2 offset, bool allowPointerInteraction = false)
    {
        ResolveReferences();
        if (owner == null || windowRect == null || !isOpen || isMinimized || isInitializing
            || IsClosing || (!allowPointerInteraction && HasActivePointerInteraction()) || !gameObject.activeInHierarchy
            || float.IsNaN(offset.x) || float.IsNaN(offset.y)
            || float.IsInfinity(offset.x) || float.IsInfinity(offset.y)) return false;
        if (presentationOffsetOwner != null && !ReferenceEquals(presentationOffsetOwner, owner)) return false;
        if (presentationOffsetOwner != null && !HasCurrentPresentationOffset)
        {
            // An external layout writer wins. Never subtract an old offset from its new position.
            ClearPresentationOffset();
            return false;
        }
        Vector2 basePosition = windowRect.anchoredPosition - PresentationOffset;
        presentationOffsetOwner = owner;
        presentationOffsetAllowsPointerInteraction = allowPointerInteraction;
        presentationOffset = new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));
        presentationOffsetPosition = basePosition + presentationOffset;
        windowRect.anchoredPosition = presentationOffsetPosition;
        return true;
    }

    public bool OwnsPresentationOffset(object owner) => ReferenceEquals(presentationOffsetOwner, owner)
        && owner != null && HasCurrentPresentationOffset;

    public void ClearPresentationOffset(object owner)
    {
        if (owner != null && ReferenceEquals(presentationOffsetOwner, owner)) ClearPresentationOffset();
    }

    void ClearPresentationOffset()
    {
        if (HasCurrentPresentationOffset) windowRect.anchoredPosition -= presentationOffset;
        presentationOffsetOwner = null;
        presentationOffsetAllowsPointerInteraction = false;
        presentationOffset = Vector2.zero;
    }

    void PlayOpenAnimation(float? durationOverride)
    {
        KillOpenSequence();

        float targetHeight = recordedSize.y;
        float topEdge = windowRect.anchoredPosition.y
                        + targetHeight * (1f - windowRect.pivot.y);

        Vector2 collapsedSize = recordedSize;
        collapsedSize.y = Mathf.Max(0f, collapsedHeight);
        windowRect.sizeDelta = collapsedSize;
        SetHeightKeepingTop(collapsedSize.y, topEdge);
        SetContentAlpha(0f);
        isInitializing = true;

        Sequence sequence = DOTween.Sequence();
        openSequence = sequence;
        sequence.Append(DOTween
            .To(
                () => windowRect.sizeDelta.y,
                height => SetHeightKeepingTop(height, topEdge),
                targetHeight,
                durationOverride ?? Mathf.Max(0f, openDuration))
            .SetEase(Ease.OutCubic));
        sequence.InsertCallback(
            Mathf.Max(0f, contentRevealDelay),
            () =>
            {
                if (openSequence == sequence)
                    ContentInitializationRequested?.Invoke(this);
            });

        float fadeDuration = Mathf.Max(0f, contentFadeDuration);
        for (int i = 0; i < contentGroups.Length; i++)
        {
            CanvasGroup group = contentGroups[i];
            if (group != null)
            {
                sequence.Insert(
                    Mathf.Max(0f, contentRevealDelay),
                    group.DOFade(1f, fadeDuration).SetEase(Ease.Linear));
            }
        }

        sequence.InsertCallback(
            Mathf.Max(0f, contentRevealDelay) + fadeDuration,
            () =>
            {
                if (openSequence == sequence)
                    SetContentInteraction(true);
            });

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (openSequence != sequence)
                    return;

                openSequence = null;
                isInitializing = false;
                OpenAnimationCompleted?.Invoke(this);
            })
            .OnKill(() =>
            {
                if (openSequence != sequence)
                    return;

                openSequence = null;
                isInitializing = false;
            });
    }

    void OpenImmediately()
    {
        KillOpenSequence();
        ApplyRecordedSize();
        SetContentAlpha(1f);
        ContentInitializationRequested?.Invoke(this);
        OpenAnimationCompleted?.Invoke(this);
    }

    void CompleteOpenVisuals()
    {
        float topEdge = windowRect.anchoredPosition.y
            + windowRect.sizeDelta.y * (1f - windowRect.pivot.y);
        KillOpenSequence();
        ApplyRecordedSize();
        SetHeightKeepingTop(recordedSize.y, topEdge);
        SetContentAlpha(1f);
    }

    void RecordCurrentSize()
    {
        if (!allowResize || windowRect == null)
            return;

        recordedSize = new Vector2(
            Mathf.Max(minimumSize.x, windowRect.rect.width),
            Mathf.Max(minimumSize.y, windowRect.rect.height));
    }

    void ApplyRecordedSize()
    {
        if (windowRect == null)
            return;

        windowRect.sizeDelta = recordedSize;
    }

    void SetHeightKeepingTop(float height, float topEdge)
    {
        Vector2 size = windowRect.sizeDelta;
        size.y = height;
        windowRect.sizeDelta = size;

        Vector2 position = windowRect.anchoredPosition;
        position.y = topEdge - height * (1f - windowRect.pivot.y);
        windowRect.anchoredPosition = position;
    }

    void ResizeBy(Vector2 delta)
    {
        Vector2 oldSize = windowRect.sizeDelta;
        Vector2 newSize = oldSize;
        Vector2 position = windowRect.anchoredPosition;
        Vector2 pivot = windowRect.pivot;

        if (resizeRight)
        {
            newSize.x = Mathf.Max(minimumSize.x, oldSize.x + delta.x);
            position.x += (newSize.x - oldSize.x) * (1f - pivot.x);
        }
        else if (resizeLeft)
        {
            newSize.x = Mathf.Max(minimumSize.x, oldSize.x - delta.x);
            position.x -= (newSize.x - oldSize.x) * pivot.x;
        }

        if (resizeTop)
        {
            newSize.y = Mathf.Max(minimumSize.y, oldSize.y + delta.y);
            position.y += (newSize.y - oldSize.y) * (1f - pivot.y);
        }
        else if (resizeBottom)
        {
            newSize.y = Mathf.Max(minimumSize.y, oldSize.y - delta.y);
            position.y -= (newSize.y - oldSize.y) * pivot.y;
        }

        windowRect.sizeDelta = newSize;
        windowRect.anchoredPosition = position;
    }

    void ResolveReferences()
    {
        if (windowRect == null)
            windowRect = transform as RectTransform;

        if (dragArea == null)
            dragArea = transform.Find("DragArea") as RectTransform;

        if (contentGroups != null
            && contentGroups.Length > 0
            && Array.Exists(contentGroups, group => group != null))
        {
            return;
        }

        CanvasGroup[] resolvedGroups =
        {
            transform.Find("TextArea")?.GetComponent<CanvasGroup>(),
            transform.Find("SelectArea")?.GetComponent<CanvasGroup>(),
            transform.Find("RecordsArea")?.GetComponent<CanvasGroup>(),
            transform.Find("DetailsArea")?.GetComponent<CanvasGroup>(),
            transform.Find("FolderGridArea")?.GetComponent<CanvasGroup>(),
            transform.Find("CharacterMask")?.GetComponent<CanvasGroup>()
        };
        contentGroups = Array.FindAll(
            resolvedGroups,
            group => group != null);
    }

    RectTransform ResolveDragBounds()
    {
        RectTransform current = windowRect != null
            ? windowRect.parent as RectTransform
            : null;
        RectTransform outermostBeforeCanvas = current;
        while (current != null)
        {
            // Nested canvases only change drawing order (for example Live's
            // message front lift). They do not replace the desktop drag bounds.
            Canvas canvas = current.GetComponent<Canvas>();
            if (canvas != null && canvas.isRootCanvas)
                break;

            outermostBeforeCanvas = current;
            current = current.parent as RectTransform;
        }

        return outermostBeforeCanvas;
    }

    void ClampDraggedWindowToBounds(RectTransform parentRect)
    {
        RectTransform bounds = ResolveDragBounds();
        if (bounds == null || bounds.rect.width <= 0f || bounds.rect.height <= 0f)
            return;

        // Desktop coordinates are the game's reference pixels, independent of
        // the canvas scale, window pivot, and nested sorting canvases.
        Rect available = bounds.rect;
        float insetX = Mathf.Min(DragWindowSafeInset, available.width * .5f);
        float insetY = Mathf.Min(DragWindowSafeInset, available.height * .5f);
        available.xMin += insetX;
        available.xMax -= insetX;
        available.yMin += insetY;
        available.yMax -= insetY;
        windowRect.GetWorldCorners(dragWindowCorners);
        Vector2 min = bounds.InverseTransformPoint(dragWindowCorners[0]);
        Vector2 max = min;
        for (int i = 1; i < dragWindowCorners.Length; i++)
        {
            Vector2 corner = bounds.InverseTransformPoint(dragWindowCorners[i]);
            min = Vector2.Min(min, corner);
            max = Vector2.Max(max, corner);
        }

        // Tilted or resized windows can be larger than the desktop. Keep their
        // drag bar reachable without locking that axis when the pointer reverses.
        Vector2 dragMin = min;
        Vector2 dragMax = max;
        if (dragArea != null && (max.x - min.x > available.width || max.y - min.y > available.height))
        {
            dragArea.GetWorldCorners(dragWindowCorners);
            dragMin = bounds.InverseTransformPoint(dragWindowCorners[0]);
            dragMax = dragMin;
            for (int i = 1; i < dragWindowCorners.Length; i++)
            {
                Vector2 corner = bounds.InverseTransformPoint(dragWindowCorners[i]);
                dragMin = Vector2.Min(dragMin, corner);
                dragMax = Vector2.Max(dragMax, corner);
            }
        }
        Vector2 correction = new Vector2(
            ClampDragAxis(available.xMin, available.xMax, min.x, max.x, dragMin.x, dragMax.x),
            ClampDragAxis(available.yMin, available.yMax, min.y, max.y, dragMin.y, dragMax.y));
        if (correction.sqrMagnitude > .0001f)
            windowRect.anchoredPosition += (Vector2)parentRect.InverseTransformVector(
                bounds.TransformVector(correction));
    }

    static float ClampDragAxis(float minimum, float maximum, float windowMin, float windowMax,
        float dragMin, float dragMax)
    {
        if (windowMax - windowMin <= maximum - minimum)
            return Mathf.Clamp(0f, minimum - windowMin, maximum - windowMax);
        if (dragMax - dragMin <= maximum - minimum)
            return Mathf.Clamp(0f, minimum - dragMin, maximum - dragMax);
        float visible = Mathf.Min(MinimumVisibleDragSpan, (maximum - minimum) * .5f);
        return Mathf.Clamp(0f, minimum + visible - dragMax, maximum - visible - dragMin);
    }

    void SetContentAlpha(float alpha)
    {
        if (contentGroups == null)
            return;

        float clampedAlpha = Mathf.Clamp01(alpha);
        bool enableInteraction = clampedAlpha >= 0.999f;
        for (int i = 0; i < contentGroups.Length; i++)
        {
            CanvasGroup group = contentGroups[i];
            if (group == null)
                continue;

            group.alpha = clampedAlpha;
            group.interactable = enableInteraction;
            group.blocksRaycasts = enableInteraction;
        }
    }

    void SetContentInteraction(bool isEnabled)
    {
        if (contentGroups == null)
            return;

        for (int i = 0; i < contentGroups.Length; i++)
        {
            CanvasGroup group = contentGroups[i];
            if (group == null)
                continue;

            group.interactable = isEnabled;
            group.blocksRaycasts = isEnabled;
        }
    }

    void KillOpenSequence()
    {
        if (openSequence != null)
        {
            Sequence sequence = openSequence;
            openSequence = null;
            sequence.Kill(false);
        }

        isInitializing = false;
    }

    void NotifyLayoutChanged()
    {
        LayoutChanged?.Invoke(this);
    }

    public bool IsDragAreaTarget(GameObject target)
    {
        if (dragArea == null)
            ResolveReferences();

        if (dragArea == null || target == null)
            return false;

        Transform targetTransform = target.transform;
        return targetTransform == dragArea
            || targetTransform.IsChildOf(dragArea);
    }

    void UpdateCursorState(PointerEventData eventData)
    {
        if (!isOpen || isInitializing || windowRect == null)
        {
            CursorHandle.Instance?.ClearContextState(this);
            return;
        }

        CursorVisualState state = CursorVisualState.Normal;
        if (TryGetResizeDirections(
                eventData,
                out bool left,
                out bool right,
                out bool top,
                out bool bottom))
        {
            state = GetResizeCursorState(left, right, top, bottom);
        }

        UpdateCursorState(state);
    }

    void UpdateCursorState(CursorVisualState state)
    {
        CursorHandle.Instance?.SetContextState(this, state);
    }

    bool TryGetResizeDirections(
        PointerEventData eventData,
        out bool left,
        out bool right,
        out bool top,
        out bool bottom)
    {
        left = false;
        right = false;
        top = false;
        bottom = false;

        if (!allowResize || eventData == null || windowRect == null)
            return false;

        Camera eventCamera = eventData.pressEventCamera != null
            ? eventData.pressEventCamera
            : eventData.enterEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                windowRect,
                eventData.position,
                eventCamera,
                out Vector2 localPoint))
        {
            return false;
        }

        Rect rect = windowRect.rect;
        if (!rect.Contains(localPoint))
            return false;

        float border = Mathf.Max(0f, resizeBorder);
        left = localPoint.x <= rect.xMin + border;
        right = localPoint.x >= rect.xMax - border;
        bottom = localPoint.y <= rect.yMin + border;
        top = localPoint.y >= rect.yMax - border;
        return true;
    }

    static CursorVisualState GetResizeCursorState(
        bool left,
        bool right,
        bool top,
        bool bottom)
    {
        bool horizontal = left || right;
        bool vertical = top || bottom;

        if (horizontal && vertical)
        {
            return (left && top) || (right && bottom)
                ? CursorVisualState.ResizeDiagonalBackslash
                : CursorVisualState.ResizeDiagonalSlash;
        }

        if (horizontal)
            return CursorVisualState.ResizeHorizontal;

        if (vertical)
            return CursorVisualState.ResizeVertical;

        return CursorVisualState.Normal;
    }

    bool IsWindowTarget(GameObject target)
    {
        if (target == null)
            return false;

        Transform targetTransform = target.transform;
        return targetTransform == transform
            || targetTransform.IsChildOf(transform);
    }

    bool HasActivePointerInteraction()
    {
        return moveWindow || HasResizeDirection();
    }

    bool HasResizeDirection()
    {
        return resizeLeft || resizeRight || resizeTop || resizeBottom;
    }

    bool IsSystemInputLocked()
    {
        return IsClosing || OS_Handle.Instance?.IsSystemInputLocked == true
            || OS_Handle.Instance?.Chapter03Story?.BlocksWindowInteraction(this) == true;
    }

    void ClearResizeState()
    {
        CursorHandle.Instance?.ClearPositionConstraint(this);
        moveWindow = false;
        resizeLeft = false;
        resizeRight = false;
        resizeTop = false;
        resizeBottom = false;
    }

    void SanitizeSettings()
    {
        minimumSize.x = Mathf.Max(1f, minimumSize.x);
        minimumSize.y = Mathf.Max(1f, minimumSize.y);
        recordedSize.x = Mathf.Max(minimumSize.x, recordedSize.x);
        recordedSize.y = Mathf.Max(minimumSize.y, recordedSize.y);
        collapsedHeight = Mathf.Max(0f, collapsedHeight);
    }

    void Reset()
    {
        ResolveReferences();
        if (windowRect != null)
            recordedSize = windowRect.rect.size;
        SanitizeSettings();
    }
}
