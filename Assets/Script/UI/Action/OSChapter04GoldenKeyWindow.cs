using System;
using UnityEngine;

/// <summary>The small key window owns only a drag gesture; the story decides whether it unlocks.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSChapter04GoldenKeyWindow : MonoBehaviour,
    UnityEngine.EventSystems.IBeginDragHandler,
    UnityEngine.EventSystems.IDragHandler,
    UnityEngine.EventSystems.IEndDragHandler
{
    const int GhostSortingOrder = 32760;
    OSWindowUnit window;
    UnityEngine.UI.Image keyImage;
    RectTransform keyHitRect, titleGrip, unlockTarget, ghostRect, ghostParent;
    Vector2 pointerOffset;
    Camera dragCamera;
    UnityEngine.EventSystems.PointerEventData dragEventData;
    int dragPointerId;
    bool dragging, detachedDrag, inputEnabled = true, hasFocus = true;

    public Func<bool> CanUseKey { get; set; }
    public Func<bool> TryUnlock { get; set; }
    public OSWindowUnit Window => window != null ? window : (window = GetComponent<OSWindowUnit>());
    public RectTransform KeyRect => keyImage != null ? keyImage.rectTransform : null;
    public RectTransform TitleGrip => titleGrip;
    public bool IsDragging => dragging;

    public static OSChapter04GoldenKeyWindow Create(Transform parent, OSWindowUnit frameTemplate,
        Sprite keySprite, RectTransform unlockTarget)
    {
        if (parent == null) return null;
        var window = OSChapter04SmallWindowFrame.Create("WindowUnit_Chapter04GoldenKey", parent, frameTemplate,
            new Vector2(110f, 62f), Vector2.zero, false, out var body, out var grip);
        var hit = OSChapter04SmallWindowFrame.Image("KeyDragArea", body, Color.clear, true);
        hit.rectTransform.anchorMin = hit.rectTransform.anchorMax = hit.rectTransform.pivot = new Vector2(.5f, .5f);
        hit.rectTransform.sizeDelta = new Vector2(44f, 30f);
        var key = OSChapter04SmallWindowFrame.Image("GoldenKey", hit.transform, Color.white);
        key.rectTransform.anchorMin = key.rectTransform.anchorMax = key.rectTransform.pivot = new Vector2(.5f, .5f);
        key.rectTransform.sizeDelta = new Vector2(20f, 9f);
        key.sprite = keySprite;
        key.preserveAspect = true;
        var controller = window.gameObject.AddComponent<OSChapter04GoldenKeyWindow>();
        controller.window = window;
        controller.keyImage = key;
        controller.keyHitRect = hit.rectTransform;
        controller.titleGrip = grip;
        controller.unlockTarget = unlockTarget;
        window.PrepareRuntimeClosed();
        return controller;
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled) CancelDrag();
    }

    bool IsSourceReady
    {
        get
        {
            var current = Window;
            return isActiveAndEnabled && current != null && current.IsOpen
                && !current.IsOpening && !current.IsClosing && !current.IsMinimized;
        }
    }

    bool IsDragAvailable => inputEnabled && hasFocus
        && OS_Handle.Instance?.IsOSVisible != false && OS_Handle.Instance?.IsSystemInputLocked != true
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && keyImage != null && keyImage.sprite != null
        && unlockTarget != null && unlockTarget.gameObject.activeInHierarchy
        && CanUseKey != null && CanUseKey();

    // Once the key has left its window, the independent ghost owns the gesture.
    bool CanDrag => IsDragAvailable && (detachedDrag || IsSourceReady);

    void OnEnable() => hasFocus = Application.isFocused;
    void OnDisable()
    {
        if (!detachedDrag) CancelDrag();
    }
    void OnDestroy() => CancelDrag();
    void OnApplicationFocus(bool focused) => SetDragFocus(focused);
    void Update() => TickDrag();

    internal void SetDragFocus(bool focused)
    {
        hasFocus = focused;
        if (!focused) CancelDrag();
    }

    internal void TickDrag()
    {
        if (dragging && !CanDrag) CancelDrag();
    }

    public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (eventData == null || eventData.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left
            || !IsDragAvailable || !IsSourceReady || keyHitRect == null) return;
        // This component shares the window root with OSWindowUnit. Title drags remain window drags.
        var pressed = eventData.pointerPressRaycast.gameObject;
        if (pressed == null || (pressed.transform != keyHitRect && !pressed.transform.IsChildOf(keyHitRect))) return;
        CancelDrag();
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null || canvas.rootCanvas == null) return;
        canvas = canvas.rootCanvas;
        ghostParent = canvas.transform as RectTransform;
        if (ghostParent == null) return;
        dragCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (dragCamera == null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            dragCamera = eventData.pressEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(ghostParent, eventData.position,
                dragCamera, out var pointer)) return;
        var sourceLocal = (Vector2)ghostParent.InverseTransformPoint(KeyRect.position);
        pointerOffset = sourceLocal - pointer;

        var ghost = new GameObject("Chapter04GoldenKeyDragGhost", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasGroup), typeof(UnityEngine.UI.Image), typeof(OSChapter04GoldenKeyDragProxy));
        ghost.layer = gameObject.layer;
        ghostRect = (RectTransform)ghost.transform;
        ghostRect.SetParent(ghostParent, false);
        ghostRect.anchorMin = ghostRect.anchorMax = ghostRect.pivot = new Vector2(.5f, .5f);
        ghostRect.sizeDelta = new Vector2(20f, 9f);
        var sorting = ghost.GetComponent<Canvas>();
        sorting.overrideSorting = true;
        sorting.sortingLayerID = canvas.sortingLayerID;
        sorting.sortingOrder = GhostSortingOrder;
        var group = ghost.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        var visual = ghost.GetComponent<UnityEngine.UI.Image>();
        visual.sprite = keyImage.sprite;
        visual.color = keyImage.color;
        visual.preserveAspect = true;
        visual.raycastTarget = false;
        visual.maskable = false;
        dragging = true;
        dragPointerId = eventData.pointerId;
        dragEventData = eventData;
        ghost.GetComponent<OSChapter04GoldenKeyDragProxy>().Attach(this);
        // EventSystem must keep delivering drag/end-drag after the source root closes.
        eventData.pointerDrag = ghost;
        keyImage.enabled = false;
        MoveGhost(eventData.position);
    }

    public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (!dragging || eventData == null || eventData.pointerId != dragPointerId) return;
        if (!CanDrag) { CancelDrag(); return; }
        MoveGhost(eventData.position);
    }

    public void OnEndDrag(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (!dragging || eventData == null || eventData.pointerId != dragPointerId) return;
        if (!CanDrag) { CancelDrag(); return; }
        bool unlocked = false;
        try
        {
            MoveGhost(eventData.position);
            if (CanDrag && IsOverTarget(eventData.position)) unlocked = TryUnlock != null && TryUnlock();
        }
        finally { CancelDrag(); }
        if (unlocked && this != null && Window != null) Window.CloseSilently();
    }

    bool IsOverTarget(Vector2 pointer)
    {
        if (unlockTarget == null) return false;
        var canvas = unlockTarget.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.rootCanvas.worldCamera : null;
        if (RectTransformUtility.RectangleContainsScreenPoint(unlockTarget, pointer, camera)) return true;
        // A wide transparent grab area can put the pointer beside the visible key.
        return ghostRect != null && RectTransformUtility.RectangleContainsScreenPoint(unlockTarget,
            RectTransformUtility.WorldToScreenPoint(dragCamera, ghostRect.position), camera);
    }

    void MoveGhost(Vector2 screenPoint)
    {
        if (ghostRect == null || ghostParent == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(ghostParent, screenPoint,
                dragCamera, out var pointer)) return;
        var position = pointer + pointerOffset;
        // The key is 20x9, so its vertical center sits on a half-pixel for integer pixel edges.
        ghostRect.localPosition = new Vector3(Mathf.Round(position.x), Mathf.Round(position.y - .5f) + .5f, 0f);
        if (!dragging || detachedDrag) return;
        var sourceRect = Window != null ? Window.WindowRect : null;
        if (sourceRect == null || sourceRect.rect.Contains((Vector2)sourceRect.InverseTransformPoint(ghostRect.position)))
            return;
        // Set this before disabling the root so OnDisable preserves the live gesture.
        detachedDrag = true;
        Window.CloseSilently();
    }

    internal void CancelDrag()
    {
        dragging = false;
        detachedDrag = false;
        if (keyImage != null) keyImage.enabled = true;
        var previousGhost = ghostRect;
        ghostRect = ghostParent = null;
        dragCamera = null;
        pointerOffset = Vector2.zero;
        if (dragEventData != null && previousGhost != null && dragEventData.pointerDrag == previousGhost.gameObject)
        {
            dragEventData.pointerDrag = null;
            dragEventData.dragging = false;
        }
        dragEventData = null;
        if (previousGhost != null)
        {
            previousGhost.GetComponent<OSChapter04GoldenKeyDragProxy>()?.Attach(null);
            previousGhost.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(previousGhost.gameObject);
            else DestroyImmediate(previousGhost.gameObject);
        }
    }
}

/// <summary>Keeps the key gesture alive while its source window is inactive.</summary>
[AddComponentMenu("")]
public sealed class OSChapter04GoldenKeyDragProxy : MonoBehaviour,
    UnityEngine.EventSystems.IDragHandler,
    UnityEngine.EventSystems.IEndDragHandler,
    UnityEngine.EventSystems.ICancelHandler
{
    OSChapter04GoldenKeyWindow owner;

    internal void Attach(OSChapter04GoldenKeyWindow value) => owner = value;
    public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (owner != null) owner.OnDrag(eventData);
    }
    public void OnEndDrag(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (owner != null) owner.OnEndDrag(eventData);
    }
    public void OnCancel(UnityEngine.EventSystems.BaseEventData eventData) => Cancel();
    void Update()
    {
        if (owner != null) owner.TickDrag();
    }
    void OnApplicationFocus(bool focused)
    {
        if (owner != null) owner.SetDragFocus(focused);
    }
    void OnApplicationPause(bool paused)
    {
        if (paused) Cancel();
    }
    void OnDisable() => Cancel();
    void OnDestroy() => Cancel();
    void Cancel()
    {
        if (owner != null) owner.CancelDrag();
    }
}
