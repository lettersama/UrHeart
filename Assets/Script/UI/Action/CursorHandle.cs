using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum CursorVisualState
{
    Normal,
    ResizeVertical,
    ResizeHorizontal,
    ResizeDiagonalBackslash,
    Pressed,
    ResizeDiagonalSlash
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed partial class CursorHandle : Singleton<CursorHandle>
{
    // The player shares the OS camera with papers (1190), night (1200),
    // and her red pointer (1202). Keep input visible above those surfaces,
    // while the authored OS fade (2000) and session curtain can still cover it.
    const int MinimumForegroundSortingOrder = 1500;

    [Header("References")]
    [SerializeField] Image cursorIcon;
    [SerializeField] string cameraPresentationLayerName = "OSUI";

    [Header("State Sprites")]
    [SerializeField] Sprite normalSprite;
    [SerializeField] Sprite resizeVerticalSprite;
    [SerializeField] Sprite resizeHorizontalSprite;
    [SerializeField] Sprite resizeDiagonalBackslashSprite;
    [SerializeField] Sprite pressedSprite;
    [SerializeField] Sprite resizeDiagonalSlashSprite;

    [Header("Hotspots")]
    [SerializeField] Vector2 pointerHotspot =
        new Vector2(0.05f, 0.95f);
    [SerializeField] Vector2 resizeHotspot =
        new Vector2(0.5f, 0.5f);

    Canvas cursorCanvas;
    RectTransform canvasRect;
    RectTransform cursorRect;
    UnityEngine.Object contextOwner;
    CursorVisualState contextState;
    CursorVisualState temporaryState;
    float temporaryStateEndTime;
    bool hasTemporaryState;
    bool isPressed;
    bool visualInitialized;
    UnityEngine.Object scriptedInputOwner;
    Vector2 scriptedScreenPosition;
    bool scriptedPressed;
    bool scriptedOverlayPresentation;
    Color authoredIconColor = Color.white;
    Vector3 authoredIconScale = Vector3.one;
    bool hasAuthoredAppearance;
    UnityEngine.Object positionConstraintOwner;
    RectTransform positionConstraintBounds;
    Camera positionConstraintCamera;
    float positionConstraintMargin;
    readonly Dictionary<UnityEngine.Object, Camera>
        cameraPresentationRequests =
            new Dictionary<UnityEngine.Object, Camera>();
    readonly Dictionary<Transform, int> originalPresentationLayers =
        new Dictionary<Transform, int>();
    readonly List<UnityEngine.Object> invalidPresentationOwners =
        new List<UnityEngine.Object>();
    RenderMode originalRenderMode;
    Camera originalWorldCamera;
    float originalPlaneDistance;
    bool hasOriginalPresentationState;
    bool hasWarnedMissingPresentationLayer;

    public CursorVisualState CurrentState { get; private set; }
    public bool IsScriptControlled => scriptedInputOwner != null;
    public float ScriptScaleMultiplier { get; private set; } = 1f;
    public Color ScriptTint { get; private set; } = Color.white;
    public Vector2 DisplayedScreenPosition => GetCurrentScreenPosition();
    public Sprite NormalSprite => normalSprite;
    public Sprite PressedSprite => pressedSprite;
    public event Action<CursorVisualState> StateChanged;

    protected override void Awake()
    {
        base.Awake();
        if (!IsSingletonInstance)
            return;

        ResolveReferences();
        EnsureForegroundSorting();
        CaptureAuthoredAppearance();
        ApplyVisual(true);
    }

    void OnEnable()
    {
        if (!IsSingletonInstance)
            return;

        Canvas.willRenderCanvases += RefreshPointerBeforeCanvasRender;
        Cursor.visible = false;
        ResolveReferences();
        EnsureForegroundSorting();
        CaptureAuthoredAppearance();
        ApplyRequestedCameraPresentation();
        UpdatePointerPosition();
        ApplyVisual(true);
    }

    void Update()
    {
        TickVirtualCaptureLifetime();
        if (GameTestConsole.BlocksGameplayInput)
            return;
        if (scriptedInputOwner != null)
        {
            UpdatePointerPosition(scriptedScreenPosition);
            if (isPressed != scriptedPressed)
            {
                isPressed = scriptedPressed;
                ApplyVisual();
            }
        }
        else
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                UpdatePointerPosition(
                    GetDisplayedScreenPosition(mouse.position.ReadValue()));

                bool nextPressed = mouse.leftButton.isPressed;
                if (isPressed != nextPressed)
                {
                    isPressed = nextPressed;
                    ApplyVisual();
                }
            }
        }

        if (hasTemporaryState
            && Time.unscaledTime >= temporaryStateEndTime)
        {
            ClearTemporaryState();
        }
    }

    void OnDisable()
    {
        if (!IsSingletonInstance)
            return;

        Canvas.willRenderCanvases -= RefreshPointerBeforeCanvasRender;
        Cursor.visible = true;
        contextOwner = null;
        contextState = CursorVisualState.Normal;
        hasTemporaryState = false;
        isPressed = false;
        ClearScriptControlState(true);
        ClearPositionConstraintState();
        RestoreCanvasPresentation();
    }

    void RefreshPointerBeforeCanvasRender()
    {
        // Story cameras can move their projection in LateUpdate. Resolve the
        // pointer again after Canvas adaptation so the rendered hotspot keeps
        // matching the physical mouse throughout the camera transition.
        if (GameTestConsole.BlocksGameplayInput || cursorCanvas == null
            || cursorCanvas.renderMode != RenderMode.ScreenSpaceCamera
            || cameraPresentationRequests.Count == 0) return;
        UpdatePointerPosition();
    }

    protected override void OnDestroy()
    {
        if (IsSingletonInstance)
        {
            ClearVirtualCaptureState(true);
            Cursor.visible = true;
            RestoreCanvasPresentation();
            cameraPresentationRequests.Clear();
        }

        base.OnDestroy();
    }

    public void SetState(CursorVisualState state)
    {
        contextOwner = null;
        contextState = state;
        ApplyVisual();
    }

    public bool AcquireScriptControl(UnityEngine.Object owner)
    {
        if (owner == null)
            return false;
        if (scriptedInputOwner != null && scriptedInputOwner != owner)
            return false;

        ResolveReferences();
        CaptureAuthoredAppearance();
        scriptedScreenPosition = GetCurrentScreenPosition();
        scriptedInputOwner = owner;
        scriptedPressed = false;
        isPressed = false;
        contextOwner = null;
        contextState = CursorVisualState.Normal;
        hasTemporaryState = false;
        ClearPositionConstraintState();
        ApplyVisual();
        return true;
    }

    public bool IsScriptControlledBy(UnityEngine.Object owner)
    {
        return owner != null && scriptedInputOwner == owner;
    }

    public void SetScriptScreenPosition(
        UnityEngine.Object owner,
        Vector2 screenPosition)
    {
        if (!IsScriptControlledBy(owner))
            return;

        scriptedScreenPosition = screenPosition;
        UpdatePointerPosition(scriptedScreenPosition);
    }

    public void SetScriptPressed(UnityEngine.Object owner, bool pressed)
    {
        if (!IsScriptControlledBy(owner))
            return;

        scriptedPressed = pressed;
        if (isPressed == scriptedPressed)
            return;

        isPressed = scriptedPressed;
        ApplyVisual();
    }

    public void SetScriptAppearance(
        UnityEngine.Object owner,
        float scaleMultiplier,
        Color tint)
    {
        if (!IsScriptControlledBy(owner))
            return;

        ResolveReferences();
        CaptureAuthoredAppearance();
        ScriptScaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
        ScriptTint = tint;
        if (cursorRect != null)
            cursorRect.localScale = authoredIconScale * ScriptScaleMultiplier;
        if (cursorIcon != null)
        {
            cursorIcon.color = new Color(
                ScriptTint.r,
                ScriptTint.g,
                ScriptTint.b,
                authoredIconColor.a * ScriptTint.a);
        }
    }

    public void ReleaseScriptControl(
        UnityEngine.Object owner,
        bool restoreAppearance = true,
        bool syncToPhysicalMouse = true)
    {
        if (!IsScriptControlledBy(owner))
            return;

        ClearScriptControlState(restoreAppearance);
        if (syncToPhysicalMouse)
            UpdatePointerPosition();
        ApplyVisual();
    }

    // A screenshot overlay must not cover a story-controlled pointer. Restore
    // the existing camera presentation as soon as this script owner releases it.
    public void SetScriptOverlayPresentation(UnityEngine.Object owner, bool active)
    {
        if (!IsScriptControlledBy(owner)) return;
        scriptedOverlayPresentation = active;
        ApplyRequestedCameraPresentation();
        UpdatePointerPosition();
    }

    public void ReleaseScriptControlAtDisplayedPosition(
        UnityEngine.Object owner,
        bool restoreAppearance = true)
    {
        if (!IsScriptControlledBy(owner))
            return;

        if (noWarpScriptControl)
        {
            ReleaseScriptControlWithoutWarp(owner, restoreAppearance);
            return;
        }

        Vector2 releasePosition = scriptedScreenPosition;
        Mouse.current?.WarpCursorPosition(releasePosition);
        ClearScriptControlState(restoreAppearance);
        UpdatePointerPosition(releasePosition);
        ApplyVisual();
    }

    public void SetCameraPresentation(
        UnityEngine.Object owner,
        Camera presentationCamera,
        bool active)
    {
        if (owner == null)
            return;

        if (active && presentationCamera != null)
            cameraPresentationRequests[owner] = presentationCamera;
        else
            cameraPresentationRequests.Remove(owner);

        ApplyRequestedCameraPresentation();
    }

    public void SetPositionConstraint(
        UnityEngine.Object owner,
        RectTransform bounds,
        float margin,
        Camera eventCamera)
    {
        if (owner == null || bounds == null)
            return;

        positionConstraintOwner = owner;
        positionConstraintBounds = bounds;
        positionConstraintCamera = eventCamera;
        positionConstraintMargin = Mathf.Max(0f, margin);
        UpdatePointerPosition();
    }

    public void ClearPositionConstraint(UnityEngine.Object owner)
    {
        if (owner == null || positionConstraintOwner != owner)
            return;

        ClearPositionConstraintState();
        UpdatePointerPosition();
    }

    public static Vector2 ClampScreenPosition(
        RectTransform bounds,
        Vector2 screenPosition,
        Camera eventCamera,
        float margin)
    {
        if (bounds == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                bounds,
                screenPosition,
                eventCamera,
                out Vector2 localPosition))
        {
            return screenPosition;
        }

        Rect rect = bounds.rect;
        float horizontalMargin = Mathf.Min(
            Mathf.Max(0f, margin),
            rect.width * 0.5f);
        float verticalMargin = Mathf.Min(
            Mathf.Max(0f, margin),
            rect.height * 0.5f);
        Vector2 clampedLocalPosition = new Vector2(
            Mathf.Clamp(
                localPosition.x,
                rect.xMin + horizontalMargin,
                rect.xMax - horizontalMargin),
            Mathf.Clamp(
                localPosition.y,
                rect.yMin + verticalMargin,
                rect.yMax - verticalMargin));

        if (clampedLocalPosition == localPosition)
            return screenPosition;

        Vector3 worldPosition = bounds.TransformPoint(clampedLocalPosition);
        return RectTransformUtility.WorldToScreenPoint(
            eventCamera,
            worldPosition);
    }

    public void ResetState()
    {
        contextOwner = null;
        contextState = CursorVisualState.Normal;
        ApplyVisual();
    }

    public void SetContextState(
        UnityEngine.Object owner,
        CursorVisualState state)
    {
        if (owner == null)
            return;

        contextOwner = owner;
        contextState = state;
        ApplyVisual();
    }

    public void ClearContextState(UnityEngine.Object owner)
    {
        if (owner == null || contextOwner != owner)
            return;

        contextOwner = null;
        contextState = CursorVisualState.Normal;
        ApplyVisual();
    }

    public void ShowTemporaryState(
        CursorVisualState state,
        float duration)
    {
        temporaryState = state;
        temporaryStateEndTime =
            Time.unscaledTime + Mathf.Max(0f, duration);
        hasTemporaryState = true;
        ApplyVisual();
    }

    public void ClearTemporaryState()
    {
        if (!hasTemporaryState)
            return;

        hasTemporaryState = false;
        ApplyVisual();
    }

    void UpdatePointerPosition()
    {
        if (scriptedInputOwner != null)
        {
            UpdatePointerPosition(scriptedScreenPosition);
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            UpdatePointerPosition(
                GetDisplayedScreenPosition(mouse.position.ReadValue()));
        }
    }

    Vector2 GetDisplayedScreenPosition(Vector2 screenPosition)
    {
        if (positionConstraintOwner == null
            || positionConstraintBounds == null)
        {
            ClearPositionConstraintState();
            return screenPosition;
        }

        return ClampScreenPosition(
            positionConstraintBounds,
            screenPosition,
            positionConstraintCamera,
            positionConstraintMargin);
    }

    void ClearPositionConstraintState()
    {
        positionConstraintOwner = null;
        positionConstraintBounds = null;
        positionConstraintCamera = null;
        positionConstraintMargin = 0f;
    }

    Vector2 GetCurrentScreenPosition()
    {
        ResolveReferences();
        if (cursorRect == null)
            return scriptedScreenPosition;

        Camera eventCamera = cursorCanvas != null
            && cursorCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? cursorCanvas.worldCamera
            : null;
        return RectTransformUtility.WorldToScreenPoint(
            eventCamera,
            cursorRect.position);
    }

    void CaptureAuthoredAppearance()
    {
        if (hasAuthoredAppearance || cursorIcon == null || cursorRect == null)
            return;

        authoredIconColor = cursorIcon.color;
        authoredIconScale = cursorRect.localScale;
        hasAuthoredAppearance = true;
    }

    void ClearScriptControlState(bool restoreAppearance)
    {
        ClearVirtualCaptureState(restoreAppearance);
        bool restorePresentation = scriptedOverlayPresentation;
        scriptedOverlayPresentation = false;
        scriptedInputOwner = null;
        scriptedPressed = false;
        if (restorePresentation) ApplyRequestedCameraPresentation();
        ScriptScaleMultiplier = 1f;
        ScriptTint = Color.white;
        if (!restoreAppearance || !hasAuthoredAppearance)
            return;

        if (cursorRect != null)
            cursorRect.localScale = authoredIconScale;
        if (cursorIcon != null)
            cursorIcon.color = authoredIconColor;
    }

    void ApplyRequestedCameraPresentation()
    {
        RemoveInvalidCameraPresentationRequests();
        if (scriptedOverlayPresentation && isActiveAndEnabled)
        {
            ResolveReferences();
            if (cursorCanvas != null)
            {
                CaptureOriginalPresentationState();
                cursorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                cursorCanvas.worldCamera = null;
            }
            return;
        }
        if (!isActiveAndEnabled
            || !TryGetPresentationCamera(out Camera presentationCamera))
        {
            RestoreCanvasPresentation();
            return;
        }

        ResolveReferences();
        if (cursorCanvas == null)
            return;

        int presentationLayer = LayerMask.NameToLayer(
            cameraPresentationLayerName);
        if (presentationLayer < 0)
        {
            if (!hasWarnedMissingPresentationLayer)
            {
                hasWarnedMissingPresentationLayer = true;
                Debug.LogWarning(
                    $"[Cursor] Layer '{cameraPresentationLayerName}' "
                    + "does not exist; keeping CursorCanvas in its "
                    + "original presentation mode.",
                    this);
            }

            RestoreCanvasPresentation();
            return;
        }

        hasWarnedMissingPresentationLayer = false;
        CaptureOriginalPresentationState();
        SetPresentationLayerRecursively(presentationLayer);

        cursorCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        cursorCanvas.worldCamera = presentationCamera;
        cursorCanvas.planeDistance = GetValidPlaneDistance(
            presentationCamera,
            GetPresentationPlaneDistance(presentationCamera));
        UpdatePointerPosition();
    }

    float GetPresentationPlaneDistance(Camera presentationCamera)
    {
        var osCanvas = OS_Handle.Instance != null
            ? OS_Handle.Instance.GetComponent<Canvas>()
            : null;
        if (osCanvas != null
            && osCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            && osCanvas.worldCamera == presentationCamera)
        {
            // A custom projection makes ScreenSpaceCamera Canvas adaptation
            // depend on plane distance. Reusing the overlay's original 100-unit
            // distance can make this 9px cursor roughly 100 times the OS UI.
            return osCanvas.planeDistance;
        }

        return originalPlaneDistance;
    }

    bool TryGetPresentationCamera(out Camera presentationCamera)
    {
        foreach (KeyValuePair<UnityEngine.Object, Camera> request
                 in cameraPresentationRequests)
        {
            if (request.Key == null || request.Value == null)
                continue;

            presentationCamera = request.Value;
            return true;
        }

        presentationCamera = null;
        return false;
    }

    void RemoveInvalidCameraPresentationRequests()
    {
        invalidPresentationOwners.Clear();
        foreach (KeyValuePair<UnityEngine.Object, Camera> request
                 in cameraPresentationRequests)
        {
            if (request.Key == null || request.Value == null)
                invalidPresentationOwners.Add(request.Key);
        }

        for (int i = 0; i < invalidPresentationOwners.Count; i++)
            cameraPresentationRequests.Remove(invalidPresentationOwners[i]);
        invalidPresentationOwners.Clear();
    }

    void CaptureOriginalPresentationState()
    {
        if (hasOriginalPresentationState || cursorCanvas == null)
            return;

        originalRenderMode = cursorCanvas.renderMode;
        originalWorldCamera = cursorCanvas.worldCamera;
        originalPlaneDistance = cursorCanvas.planeDistance;
        originalPresentationLayers.Clear();

        Transform[] hierarchy = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < hierarchy.Length; i++)
        {
            Transform item = hierarchy[i];
            if (item != null)
                originalPresentationLayers[item] = item.gameObject.layer;
        }

        hasOriginalPresentationState = true;
    }

    void SetPresentationLayerRecursively(int layer)
    {
        foreach (KeyValuePair<Transform, int> item
                 in originalPresentationLayers)
        {
            if (item.Key != null)
                item.Key.gameObject.layer = layer;
        }
    }

    void RestoreCanvasPresentation()
    {
        if (!hasOriginalPresentationState)
            return;

        ResolveReferences();
        if (cursorCanvas != null)
        {
            cursorCanvas.renderMode = originalRenderMode;
            cursorCanvas.worldCamera = originalWorldCamera;
            cursorCanvas.planeDistance = originalPlaneDistance;
        }

        foreach (KeyValuePair<Transform, int> item
                 in originalPresentationLayers)
        {
            if (item.Key != null)
                item.Key.gameObject.layer = item.Value;
        }

        originalPresentationLayers.Clear();
        hasOriginalPresentationState = false;
        UpdatePointerPosition();
    }

    static float GetValidPlaneDistance(Camera camera, float preferred)
    {
        float minimum = camera.nearClipPlane + 0.01f;
        float maximum = camera.farClipPlane - 0.01f;
        if (maximum <= minimum)
            return Mathf.Max(0.01f, camera.nearClipPlane);

        return Mathf.Clamp(preferred, minimum, maximum);
    }

    void UpdatePointerPosition(Vector2 screenPosition)
    {
        if (canvasRect == null || cursorRect == null)
            return;

        Camera eventCamera = cursorCanvas != null
            && cursorCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? cursorCanvas.worldCamera
            : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition,
                eventCamera,
                out Vector2 localPosition))
        {
            return;
        }

        Vector3 nextPosition = cursorRect.localPosition;
        nextPosition.x = localPosition.x;
        nextPosition.y = localPosition.y;
        cursorRect.localPosition = nextPosition;
    }

    void ApplyVisual(bool force = false)
    {
        CursorVisualState nextState = ResolveDisplayedState();
        if (!force && visualInitialized && CurrentState == nextState)
            return;

        ResolveReferences();

        Sprite nextSprite = GetSprite(nextState);
        if (nextSprite == null)
            nextSprite = normalSprite;

        if (cursorIcon != null && nextSprite != null)
        {
            cursorIcon.sprite = nextSprite;

            if (cursorRect != null)
            {
                cursorRect.pivot = UsesPointerHotspot(nextState)
                    ? pointerHotspot
                    : resizeHotspot;

                cursorRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    nextSprite.rect.width);
                cursorRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    nextSprite.rect.height);
            }
        }

        bool stateChanged =
            !visualInitialized || CurrentState != nextState;
        CurrentState = nextState;
        visualInitialized = true;

        if (stateChanged)
            StateChanged?.Invoke(CurrentState);
    }

    CursorVisualState ResolveDisplayedState()
    {
        if (hasTemporaryState)
            return temporaryState;

        bool keepResizeState =
            contextState == CursorVisualState.ResizeVertical
            || contextState == CursorVisualState.ResizeHorizontal
            || contextState
                == CursorVisualState.ResizeDiagonalBackslash
            || contextState == CursorVisualState.ResizeDiagonalSlash;
        if (keepResizeState)
            return contextState;

        if (isPressed)
            return CursorVisualState.Pressed;

        return contextState;
    }

    Sprite GetSprite(CursorVisualState state)
    {
        switch (state)
        {
            case CursorVisualState.ResizeVertical:
                return resizeVerticalSprite;
            case CursorVisualState.ResizeHorizontal:
                return resizeHorizontalSprite;
            case CursorVisualState.ResizeDiagonalBackslash:
                return resizeDiagonalBackslashSprite;
            case CursorVisualState.Pressed:
                return pressedSprite;
            case CursorVisualState.ResizeDiagonalSlash:
                return resizeDiagonalSlashSprite;
            default:
                return normalSprite;
        }
    }

    static bool UsesPointerHotspot(CursorVisualState state)
    {
        return state == CursorVisualState.Normal
            || state == CursorVisualState.Pressed;
    }

    void ResolveReferences()
    {
        if (cursorCanvas == null)
            cursorCanvas = GetComponent<Canvas>();

        if (canvasRect == null)
            canvasRect = transform as RectTransform;

        if (cursorIcon == null)
        {
            cursorIcon = transform.Find("CursorIcon")
                ?.GetComponent<Image>();
        }

        if (cursorIcon != null)
        {
            cursorIcon.raycastTarget = false;
            cursorRect = cursorIcon.rectTransform;
        }
    }

    void EnsureForegroundSorting()
    {
        if (cursorCanvas != null)
            cursorCanvas.sortingOrder = Mathf.Max(cursorCanvas.sortingOrder, MinimumForegroundSortingOrder);
    }

    void OnValidate()
    {
        pointerHotspot.x = Mathf.Clamp01(pointerHotspot.x);
        pointerHotspot.y = Mathf.Clamp01(pointerHotspot.y);
        resizeHotspot.x = Mathf.Clamp01(resizeHotspot.x);
        resizeHotspot.y = Mathf.Clamp01(resizeHotspot.y);
    }
}
