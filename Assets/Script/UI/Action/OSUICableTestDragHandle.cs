using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSUICableTestDragHandle : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [SerializeField] RectTransform handleRect;
    [SerializeField] RectTransform dragBounds;
    [SerializeField] RectTransform fixedPoint;
    [SerializeField] OSUICableGraphic cable;
    [SerializeField, Min(0f)] float returnDuration = 0.25f;
    [SerializeField, Min(0f)] float boundsPadding = 4f;

    Coroutine returnRoutine;
    Vector2 unsnappedDragPosition;
    bool isDragging;

    void Awake()
    {
        if (handleRect == null)
            handleRect = transform as RectTransform;
        if (dragBounds == null)
            dragBounds = handleRect != null
                ? handleRect.parent as RectTransform
                : null;
    }

    void OnDisable()
    {
        isDragging = false;
        StopReturn();
    }

    public void Configure(
        RectTransform handle,
        RectTransform bounds,
        RectTransform fixedEndpoint,
        OSUICableGraphic cableGraphic,
        float retractDuration)
    {
        handleRect = handle;
        dragBounds = bounds;
        fixedPoint = fixedEndpoint;
        cable = cableGraphic;
        returnDuration = Mathf.Max(0f, retractDuration);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        StopReturn();
        if (handleRect == null)
            return;

        unsnappedDragPosition = handleRect.anchoredPosition;
        isDragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (handleRect == null)
            return;

        RectTransform parent = handleRect.parent as RectTransform;
        if (parent == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 currentLocal)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                eventData.position - eventData.delta,
                eventData.pressEventCamera,
                out Vector2 previousLocal))
        {
            return;
        }

        if (!isDragging)
        {
            unsnappedDragPosition = handleRect.anchoredPosition;
            isDragging = true;
        }

        unsnappedDragPosition += currentLocal - previousLocal;
        unsnappedDragPosition = ClampInsideBounds(
            unsnappedDragPosition);
        handleRect.anchoredPosition = SnapAndClampToReferencePixel(
            unsnappedDragPosition);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        BeginReturnIfStretched();
    }

    void BeginReturnIfStretched()
    {
        if (handleRect == null
            || fixedPoint == null
            || cable == null)
        {
            return;
        }

        RectTransform parent = handleRect.parent as RectTransform;
        if (parent == null)
            return;

        Vector2 start = GetCenterIn(fixedPoint, parent);
        Vector2 end = GetCenterIn(handleRect, parent);
        Vector2 fromStart = end - start;
        float distance = fromStart.magnitude;
        if (distance <= cable.MaximumLength || distance <= 0.0001f)
            return;

        Vector2 targetCenter = start
                               + fromStart / distance
                               * cable.MaximumLength;
        Vector2 targetPosition = handleRect.anchoredPosition
                                 + targetCenter - end;
        targetPosition = ResolveRetractGridPosition(
            start,
            end,
            targetPosition);
        StopReturn();
        returnRoutine = StartCoroutine(ReturnToLength(targetPosition));
    }

    Vector2 ResolveRetractGridPosition(
        Vector2 startCenter,
        Vector2 currentEndCenter,
        Vector2 targetPosition)
    {
        Vector2 roundedTarget = SnapAndClampToReferencePixel(
            targetPosition);
        Vector2 currentPosition = handleRect.anchoredPosition;
        float maximumDistanceSquared = cable.MaximumLength
                                       * cable.MaximumLength;
        float closestTargetDistanceSquared = float.PositiveInfinity;
        Vector2 bestPosition = roundedTarget;
        bool found = false;

        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                Vector2 candidate = SnapAndClampToReferencePixel(
                    roundedTarget + new Vector2(x, y));
                Vector2 candidateCenter = currentEndCenter
                                          + candidate
                                          - currentPosition;
                if ((candidateCenter - startCenter).sqrMagnitude
                    > maximumDistanceSquared + 0.0001f)
                {
                    continue;
                }

                float targetDistanceSquared =
                    (candidate - targetPosition).sqrMagnitude;
                if (targetDistanceSquared
                    >= closestTargetDistanceSquared)
                {
                    continue;
                }

                closestTargetDistanceSquared = targetDistanceSquared;
                bestPosition = candidate;
                found = true;
            }
        }

        if (found)
            return bestPosition;

        Vector2 direction = (currentEndCenter - startCenter).normalized;
        Vector2 fallbackCenter = startCenter
                                 + direction
                                 * Mathf.Max(0f, cable.MaximumLength - 1f);
        return SnapAndClampToReferencePixel(
            currentPosition + fallbackCenter - currentEndCenter);
    }

    IEnumerator ReturnToLength(Vector2 targetPosition)
    {
        Vector2 startPosition = handleRect.anchoredPosition;
        float duration = Mathf.Max(0f, returnDuration);
        if (duration <= 0f)
        {
            handleRect.anchoredPosition = SnapAndClampToReferencePixel(
                targetPosition);
            returnRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            handleRect.anchoredPosition = SnapAndClampToReferencePixel(
                Vector2.LerpUnclamped(
                    startPosition,
                    targetPosition,
                    eased));
            yield return null;
        }

        handleRect.anchoredPosition = SnapAndClampToReferencePixel(
            targetPosition);
        returnRoutine = null;
    }

    Vector2 ClampInsideBounds(Vector2 position)
    {
        if (!TryGetPositionBounds(out Vector2 minimum, out Vector2 maximum))
            return position;

        position.x = Mathf.Clamp(
            position.x,
            minimum.x,
            maximum.x);
        position.y = Mathf.Clamp(
            position.y,
            minimum.y,
            maximum.y);
        return position;
    }

    Vector2 SnapAndClampToReferencePixel(Vector2 position)
    {
        Vector2 rounded = RoundToReferencePixel(position);
        if (!TryGetPositionBounds(out Vector2 minimum, out Vector2 maximum))
            return rounded;

        float minimumX = Mathf.Ceil(minimum.x);
        float maximumX = Mathf.Floor(maximum.x);
        float minimumY = Mathf.Ceil(minimum.y);
        float maximumY = Mathf.Floor(maximum.y);
        if (minimumX <= maximumX)
            rounded.x = Mathf.Clamp(rounded.x, minimumX, maximumX);
        if (minimumY <= maximumY)
            rounded.y = Mathf.Clamp(rounded.y, minimumY, maximumY);
        return rounded;
    }

    bool TryGetPositionBounds(out Vector2 minimum, out Vector2 maximum)
    {
        minimum = default;
        maximum = default;
        if (handleRect == null
            || dragBounds == null
            || handleRect.parent != dragBounds)
        {
            return false;
        }

        Rect bounds = dragBounds.rect;
        Vector2 size = handleRect.rect.size;
        Vector2 pivot = handleRect.pivot;
        float padding = Mathf.Max(0f, boundsPadding);
        minimum = new Vector2(
            bounds.xMin + size.x * pivot.x + padding,
            bounds.yMin + size.y * pivot.y + padding);
        maximum = new Vector2(
            bounds.xMax - size.x * (1f - pivot.x) - padding,
            bounds.yMax - size.y * (1f - pivot.y) - padding);
        return true;
    }

    void StopReturn()
    {
        if (returnRoutine == null)
            return;

        StopCoroutine(returnRoutine);
        returnRoutine = null;
    }

    static Vector2 GetCenterIn(
        RectTransform source,
        RectTransform targetSpace)
    {
        Vector3 worldCenter = source.TransformPoint(source.rect.center);
        return targetSpace.InverseTransformPoint(worldCenter);
    }

    static Vector2 RoundToReferencePixel(Vector2 position)
    {
        return new Vector2(
            Mathf.Round(position.x),
            Mathf.Round(position.y));
    }
}
