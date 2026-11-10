using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Wheel input travels a consistent screen distance and eases over a few frames.
/// Dragging and scrollbars retain ScrollRect's normal direct manipulation.
/// </summary>
[AddComponentMenu("UI/OS Smooth Scroll Rect")]
[DisallowMultipleComponent]
public sealed class OSSmoothScrollRect : ScrollRect
{
    readonly OSWheelScrollMotion wheel = new OSWheelScrollMotion();
    bool dragging;

    public bool IsWheelScrolling => wheel.IsMoving;

    public override void OnScroll(PointerEventData eventData)
    {
        if (eventData == null || !IsActive()) return;
        // This policy belongs to vertical lists. Keep any separately authored
        // horizontal or two-axis controls on their original behavior.
        if (!vertical || horizontal)
        {
            wheel.Clear();
            base.OnScroll(eventData);
            return;
        }
        if (dragging) return;
        if (!CanvasUpdateRegistry.IsRebuildingLayout()) Canvas.ForceUpdateCanvases();
        UpdateBounds();
        if (m_ContentBounds.size.y <= viewRect.rect.height + .01f)
        {
            wheel.Clear();
            return;
        }

        float delta = eventData.scrollDelta.y;
        if (Mathf.Abs(eventData.scrollDelta.x) > Mathf.Abs(delta))
            delta = -eventData.scrollDelta.x;
        // Cancel drag inertia without throwing away earlier wheel increments.
        base.StopMovement();
        wheel.AddWheel(delta, content.parent as RectTransform);
        eventData.Use();
    }

    protected override void LateUpdate()
    {
        if (!IsActive() || !vertical || horizontal || dragging)
            wheel.Clear();
        else if (wheel.IsMoving)
        {
            UpdateBounds();
            float hiddenHeight = m_ContentBounds.size.y - viewRect.rect.height;
            if (hiddenHeight <= .01f) wheel.Clear();
            else
            {
                float step = wheel.Step(Time.unscaledDeltaTime);
                Vector3 worldStep = content.parent.TransformVector(new Vector3(0f, step, 0f));
                float viewportStep = viewRect.InverseTransformVector(worldStep).y;
                float before = verticalNormalizedPosition;
                float next = Mathf.Clamp01(before - viewportStep / hiddenHeight);
                base.StopMovement();
                // Bypass our public-jump cancellation for this wheel step.
                base.SetNormalizedPosition(next, 1);
                if ((next <= 0f && step > 0f) || (next >= 1f && step < 0f))
                    wheel.Clear();
            }
        }
        // Let ScrollRect publish the final position and refresh its scrollbar
        // in this same frame, including after our smooth movement.
        base.LateUpdate();
    }

    public override void StopMovement()
    {
        wheel.Clear();
        base.StopMovement();
    }

    protected override void SetNormalizedPosition(float value, int axis)
    {
        // Programmatic jumps and scrollbar gestures take over immediately.
        wheel.Clear();
        base.SetNormalizedPosition(value, axis);
    }

    public override void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) wheel.Clear();
        base.OnInitializePotentialDrag(eventData);
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && IsActive())
        {
            wheel.Clear();
            dragging = true;
        }
        base.OnBeginDrag(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) dragging = false;
        base.OnEndDrag(eventData);
    }

    protected override void OnDisable()
    {
        wheel.Clear();
        dragging = false;
        base.OnDisable();
    }
}

/// <summary>
/// Shared wheel motion for ScrollRects and Memory's recycled history pool.
/// It stores remaining displacement, so recycling rows cannot invalidate an
/// absolute target. Positive Step values increase the content's anchoredPosition.y.
/// </summary>
public sealed class OSWheelScrollMotion
{
    public const float ReferenceHeight = 360f;
    public const float DistancePerTick = 28f;
    public const float Response = 18f;
    const float SettledDistance = .001f;
    float remaining;

    public bool IsMoving => Mathf.Abs(remaining) > SettledDistance;

    public void AddWheel(float deltaY, RectTransform motionSpace)
    {
        if (float.IsNaN(deltaY) || float.IsInfinity(deltaY) || Mathf.Approximately(deltaY, 0f)) return;
        float tickSize = 1f;
        if (EventSystem.current?.currentInputModule is InputSystemUIInputModule module)
            // Preserve an intentionally inverted module's direction while
            // removing its speed multiplier. Fractional trackpad ticks remain.
            tickSize = Mathf.Max(.0001f, Mathf.Abs(module.scrollDeltaPerTick));
        float distance = -deltaY / tickSize * DistancePerTick * LocalScale(motionSpace);
        if (float.IsNaN(distance) || float.IsInfinity(distance)) return;
        if (remaining * distance < 0f) remaining = 0f;
        remaining += distance;
    }

    public float Step(float unscaledDeltaTime)
    {
        if (!IsMoving || unscaledDeltaTime <= 0f) return 0f;
        float movement = remaining * (1f - Mathf.Exp(-Response * unscaledDeltaTime));
        remaining -= movement;
        if (Mathf.Abs(remaining) <= SettledDistance)
        {
            movement += remaining;
            remaining = 0f;
        }
        return movement;
    }

    public void Clear() => remaining = 0f;

    static float LocalScale(RectTransform motionSpace)
    {
        if (motionSpace == null || Screen.height <= 0) return 1f;
        var canvas = motionSpace.GetComponentInParent<Canvas>();
        if (canvas == null) return 1f;
        canvas = canvas.rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, motionSpace.TransformPoint(Vector3.zero));
        Vector2 up = RectTransformUtility.WorldToScreenPoint(camera, motionSpace.TransformPoint(Vector3.up));
        float pixelsPerUnit = Vector2.Distance(origin, up);
        return pixelsPerUnit > .0001f ? Screen.height / ReferenceHeight / pixelsPerUnit : 1f;
    }
}
