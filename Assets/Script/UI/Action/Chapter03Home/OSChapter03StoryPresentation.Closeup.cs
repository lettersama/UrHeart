using UnityEngine;
using UnityEngine.UI;

// The mirror performance eases the OS camera into a subtle focus. Temporarily pin
// the root Canvas in world space so its automatic camera adaptation cannot cancel
// the zoom. Windows keep their authored local layout and their real raycasters.
public sealed partial class OSChapter03StoryPresentation
{
    const float ScreenZoomFactor = 1.075f;
    const float ScreenZoomInSeconds = 15f, ScreenZoomOutSeconds = .6f, ScreenZoomPanSpeed = 120f;
    static readonly Vector3[] zoomCorners = new Vector3[4];

    RectTransform zoomFocus;
    Camera zoomCamera;
    Vector2 zoomOrigin; // bottom-left of the visible area, root canvas units
    bool zoomTarget, zoomShown, zoomApplied;
    float zoomProgress, zoomPanRemainder;
    Canvas pinnedZoomCanvas;
    CanvasScaler pinnedZoomScaler;
    bool zoomScalerWasEnabled;
    RenderMode zoomOriginalRenderMode;
    float zoomOriginalPlaneDistance, zoomOriginalScaleFactor, zoomOriginalReferencePixelsPerUnit;
    Vector2 zoomOriginalSizeDelta;
    Vector3 zoomOriginalLocalPosition, zoomOriginalLocalScale;
    Quaternion zoomOriginalLocalRotation;

    /// <summary>True from the call until the screen is back at full size.</summary>
    public bool IsScreenZoomed => zoomTarget || zoomShown;
    public bool IsScreenZoomTransitioning => !Mathf.Approximately(zoomProgress, zoomTarget ? 1f : 0f);
    // Kept for existing callers that wait for the close-up transition to finish.
    public bool IsScreenZoomBlinking => IsScreenZoomTransitioning;
    public float ScreenZoomProgress => zoomProgress;

    /// <summary>mirror-face.png's nose sits at (105, 150) of its 216 x 264 canvas, drawn into the 144 x 176 glass.</summary>
    public bool TryGetMirrorFaceWorld(out Vector3 world)
    {
        world = default;
        if (mirrorFace == null) return false;
        var rect = mirrorFace.rectTransform;
        world = rect.TransformPoint(rect.rect.center + new Vector2(-2f, -12f));
        return true;
    }

    /// <summary>Ease into the centre of Live's window, or return to full size.
    /// Restored saves use the settled view immediately.</summary>
    public void SetScreenZoom(bool on, RectTransform focus = null, bool instant = false)
    {
        RequireInitialized();
        if (focus != null) zoomFocus = focus;
        if (instant)
        {
            bool cut = zoomShown != on;
            zoomTarget = zoomShown = on; zoomProgress = on ? 1f : 0f;
            if (cut && on) zoomOrigin = ZoomOriginTarget();
            return;
        }
        if (on == zoomTarget) return;
        if (on && zoomProgress <= 0f) zoomOrigin = ZoomOriginTarget();
        zoomTarget = on;
    }

    // From ResetEffects: back to full size with no blink and the camera's own projection.
    void HideCloseup()
    {
        zoomTarget = zoomShown = false; zoomProgress = zoomPanRemainder = 0f;
        zoomFocus = null;
        ApplyScreenZoom(false);
    }

    // From TickEffects: does not run while paused.
    void TickCloseup(float dt)
    {
        float seconds = zoomTarget ? ScreenZoomInSeconds : ScreenZoomOutSeconds;
        zoomProgress = Mathf.MoveTowards(zoomProgress, zoomTarget ? 1f : 0f, dt / seconds);
        zoomShown = zoomProgress > 0f;
    }

    // From LateUpdate, after layout and the story owner's window moves, before the camera renders.
    void LateTickCloseup()
    {
        bool visible = isActiveAndEnabled && !paused && os != null && os.IsOSVisible;
        // Settings uses the ordinary Canvas and camera; transition progress waits.
        if (!zoomShown || !visible) { ApplyScreenZoom(false); return; }
        if (zoomTarget)
        {
            Vector2 target = ZoomOriginTarget();
            zoomPanRemainder += ScreenZoomPanSpeed * Mathf.Min(Time.unscaledDeltaTime, .1f);
            float step = Mathf.Floor(zoomPanRemainder);
            zoomPanRemainder -= step;
            zoomOrigin = new Vector2(Mathf.MoveTowards(zoomOrigin.x, target.x, step), Mathf.MoveTowards(zoomOrigin.y, target.y, step));
        }
        ApplyScreenZoom(true);
    }

    RectTransform ZoomRoot => rootCanvas != null ? rootCanvas.transform as RectTransform : null;

    // The Live window itself owns the camera centre. Opening or growing her
    // message window must not pull the focus away from her.
    Vector2 ZoomOriginTarget()
    {
        var root = ZoomRoot;
        if (root == null) return zoomOrigin;
        UnityEngine.Rect screen = root.rect;
        Vector2 size = screen.size / ScreenZoomFactor;
        if (!TryGetZoomRect(zoomFocus, out UnityEngine.Rect frame)) return zoomOrigin;
        float x = frame.center.x - size.x * .5f, y = frame.center.y - size.y * .5f;
        x = Mathf.Clamp(x, screen.xMin, screen.xMax - size.x);
        y = Mathf.Clamp(y, screen.yMin, screen.yMax - size.y);
        return new Vector2(x, y);
    }

    bool TryGetZoomRect(RectTransform rect, out UnityEngine.Rect area)
    {
        area = default;
        var root = ZoomRoot;
        if (rect == null || root == null || !rect.gameObject.activeInHierarchy) return false;
        rect.GetWorldCorners(zoomCorners);
        Vector2 a = root.InverseTransformPoint(zoomCorners[0]), b = root.InverseTransformPoint(zoomCorners[2]);
        area = UnityEngine.Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return area.width > 0f && area.height > 0f;
    }

    // Ease both magnification and focus centre on top of the camera's own matrix.
    void ApplyScreenZoom(bool on)
    {
        var camera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
        if (zoomApplied && (!on || camera != zoomCamera))
        {
            if (zoomCamera != null) zoomCamera.ResetProjectionMatrix();
            zoomApplied = false; zoomCamera = null;
        }
        if (!on || camera == null) RestoreZoomCanvas();
        var root = ZoomRoot;
        if (!on || camera == null || root == null) return;
        UnityEngine.Rect screen = root.rect;
        if (screen.width <= 0f || screen.height <= 0f) return;
        camera.ResetProjectionMatrix();
        PinZoomCanvas();
        float eased = Mathf.SmoothStep(0f, 1f, zoomProgress);
        Vector2 focusCenter = zoomOrigin + screen.size / (ScreenZoomFactor * 2f);
        Vector2 center = Vector2.Lerp(screen.center, focusCenter, eased);
        Vector2 centerNdc = new Vector2((center.x - screen.xMin) / screen.width, (center.y - screen.yMin) / screen.height) * 2f - Vector2.one;
        float k = Mathf.Lerp(1f, ScreenZoomFactor, eased);
        var zoom = Matrix4x4.identity;
        zoom.m00 = k; zoom.m03 = -k * centerNdc.x;
        zoom.m11 = k; zoom.m13 = -k * centerNdc.y;
        camera.projectionMatrix = zoom * camera.projectionMatrix;
        zoomCamera = camera; zoomApplied = true;
    }

    void PinZoomCanvas()
    {
        if (pinnedZoomCanvas != null || rootCanvas == null || rootCanvas.renderMode != RenderMode.ScreenSpaceCamera) return;
        // Capture normal camera-adapted geometry before taking ownership.
        Canvas.ForceUpdateCanvases();
        pinnedZoomCanvas = rootCanvas;
        var root = ZoomRoot;
        zoomOriginalRenderMode = rootCanvas.renderMode;
        zoomOriginalPlaneDistance = rootCanvas.planeDistance;
        zoomOriginalScaleFactor = rootCanvas.scaleFactor;
        zoomOriginalReferencePixelsPerUnit = rootCanvas.referencePixelsPerUnit;
        zoomOriginalSizeDelta = root.sizeDelta;
        zoomOriginalLocalPosition = root.localPosition;
        zoomOriginalLocalRotation = root.localRotation;
        zoomOriginalLocalScale = root.localScale;
        pinnedZoomScaler = rootCanvas.GetComponent<CanvasScaler>();
        zoomScalerWasEnabled = pinnedZoomScaler != null && pinnedZoomScaler.enabled;
        if (zoomScalerWasEnabled) pinnedZoomScaler.enabled = false;
        rootCanvas.renderMode = RenderMode.WorldSpace;
        rootCanvas.scaleFactor = zoomOriginalScaleFactor;
        rootCanvas.referencePixelsPerUnit = zoomOriginalReferencePixelsPerUnit;
        root.sizeDelta = zoomOriginalSizeDelta;
        root.localPosition = zoomOriginalLocalPosition;
        root.localRotation = zoomOriginalLocalRotation;
        root.localScale = zoomOriginalLocalScale;
    }

    void RestoreZoomCanvas()
    {
        if (pinnedZoomCanvas != null)
        {
            var root = (RectTransform)pinnedZoomCanvas.transform;
            pinnedZoomCanvas.renderMode = zoomOriginalRenderMode;
            pinnedZoomCanvas.planeDistance = zoomOriginalPlaneDistance;
            pinnedZoomCanvas.scaleFactor = zoomOriginalScaleFactor;
            pinnedZoomCanvas.referencePixelsPerUnit = zoomOriginalReferencePixelsPerUnit;
            root.sizeDelta = zoomOriginalSizeDelta;
            root.localPosition = zoomOriginalLocalPosition;
            root.localRotation = zoomOriginalLocalRotation;
            root.localScale = zoomOriginalLocalScale;
        }
        if (pinnedZoomScaler != null) pinnedZoomScaler.enabled = zoomScalerWasEnabled;
        pinnedZoomCanvas = null; pinnedZoomScaler = null;
    }
}
