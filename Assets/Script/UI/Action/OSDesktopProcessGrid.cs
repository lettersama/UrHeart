using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSDesktopProcessGrid : MonoBehaviour
{
    const float LayoutEpsilon = 0.001f;
    static int layoutReparentDepth;

    [SerializeField] string containerId;
    [SerializeField] RectOffset padding = new RectOffset();
    [SerializeField] Vector2 cellSize = new Vector2(35f, 35.5f);
    [SerializeField] Vector2 spacing;
    [SerializeField] GridLayoutGroup.Corner startCorner =
        GridLayoutGroup.Corner.UpperLeft;
    [SerializeField] GridLayoutGroup.Axis startAxis =
        GridLayoutGroup.Axis.Horizontal;

    readonly Dictionary<DesktopProcessUnit, int> slots =
        new Dictionary<DesktopProcessUnit, int>();
    readonly List<DesktopProcessUnit> invalidUnits =
        new List<DesktopProcessUnit>();
    readonly List<KeyValuePair<DesktopProcessUnit, int>> orderedSlots =
        new List<KeyValuePair<DesktopProcessUnit, int>>();

    RectTransform _rectTransform;

    public string ContainerId => containerId;
    public RectOffset Padding => padding;
    public Vector2 CellSize => cellSize;
    public Vector2 Spacing => spacing;
    public GridLayoutGroup.Corner StartCorner => startCorner;
    public GridLayoutGroup.Axis StartAxis => startAxis;
    public RectTransform RectTransform => ResolveRectTransform();
    public int Capacity
    {
        get
        {
            GetGridDimensions(out int columns, out int rows);
            return columns * rows;
        }
    }

    public void Configure(
        string newContainerId,
        RectOffset newPadding,
        Vector2 newCellSize,
        Vector2 newSpacing,
        GridLayoutGroup.Corner newStartCorner,
        GridLayoutGroup.Axis newStartAxis)
    {
        containerId = string.IsNullOrWhiteSpace(newContainerId)
            ? string.Empty
            : newContainerId.Trim();
        padding = newPadding == null
            ? new RectOffset()
            : new RectOffset(
                newPadding.left,
                newPadding.right,
                newPadding.top,
                newPadding.bottom);
        cellSize = new Vector2(
            Mathf.Max(LayoutEpsilon, newCellSize.x),
            Mathf.Max(LayoutEpsilon, newCellSize.y));
        spacing = new Vector2(
            Mathf.Max(0f, newSpacing.x),
            Mathf.Max(0f, newSpacing.y));
        startCorner = newStartCorner;
        startAxis = newStartAxis;
        ApplyLayout();
    }

    public void ClearRuntimeSlots()
    {
        slots.Clear();
    }

    public bool Contains(DesktopProcessUnit unit)
    {
        return unit != null && slots.ContainsKey(unit);
    }

    public bool TryGetSlot(DesktopProcessUnit unit, out int gridIndex)
    {
        if (unit == null)
        {
            gridIndex = -1;
            return false;
        }

        return slots.TryGetValue(unit, out gridIndex);
    }

    public bool TryPlace(
        DesktopProcessUnit unit,
        int requestedIndex,
        out int resolvedIndex)
    {
        resolvedIndex = -1;
        if (unit == null)
            return false;

        slots.Remove(unit);
        resolvedIndex = FindFirstAvailableSlot(requestedIndex, true);
        if (resolvedIndex < 0)
            return false;

        slots[unit] = resolvedIndex;
        return true;
    }

    public bool TryInsert(DesktopProcessUnit unit, int targetIndex)
    {
        if (unit == null)
            return false;

        int capacity = Capacity;
        if (capacity <= 0)
            return false;

        targetIndex = Mathf.Clamp(targetIndex, 0, capacity - 1);
        slots.Remove(unit);

        int freeIndex = -1;
        for (int index = targetIndex; index < capacity; index++)
        {
            if (GetUnitAt(index) == null)
            {
                freeIndex = index;
                break;
            }
        }

        if (freeIndex < 0)
            return false;

        for (int index = freeIndex - 1; index >= targetIndex; index--)
        {
            DesktopProcessUnit occupant = GetUnitAt(index);
            if (occupant != null)
                slots[occupant] = index + 1;
        }

        slots[unit] = targetIndex;
        return true;
    }

    public bool Remove(DesktopProcessUnit unit)
    {
        return unit != null && slots.Remove(unit);
    }

    public int FindFirstAvailableSlot(int requestedIndex = 0)
    {
        return FindFirstAvailableSlot(requestedIndex, true);
    }

    public bool TryFindNearestAvailableSlot(
        Vector2 screenPoint,
        Camera eventCamera,
        DesktopProcessUnit ignoredUnit,
        out int gridIndex,
        IReadOnlyList<Rect> coveredScreenAreas = null)
    {
        gridIndex = -1;
        RectTransform rectTransform = ResolveRectTransform();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                screenPoint,
                eventCamera,
                out Vector2 localPoint))
        {
            return false;
        }

        GetGridDimensions(out int columns, out int rows);
        int capacity = columns * rows;
        float closestDistance = float.PositiveInfinity;
        for (int index = 0; index < capacity; index++)
        {
            DesktopProcessUnit occupant = GetUnitAt(index);
            if (occupant != null && occupant != ignoredUnit)
                continue;

            Vector2 slotCenter = GetSlotLocalCenter(
                index,
                columns,
                rows);
            if (coveredScreenAreas != null)
            {
                Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(eventCamera, rectTransform.TransformPoint(slotCenter));
                bool covered = false;
                for (int area = 0; area < coveredScreenAreas.Count; area++)
                    if (coveredScreenAreas[area].Contains(screenCenter)) { covered = true;break; }
                if (covered) continue;
            }
            float distance = (slotCenter - localPoint).sqrMagnitude;
            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            gridIndex = index;
        }

        return gridIndex >= 0;
    }

    public bool ContainsScreenPoint(Vector2 screenPoint, Camera eventCamera)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(
            ResolveRectTransform(),
            screenPoint,
            eventCamera);
    }

    public bool TryGetGridIndex(
        Vector2 screenPoint,
        Camera eventCamera,
        out int gridIndex)
    {
        gridIndex = -1;
        RectTransform rectTransform = ResolveRectTransform();
        if (!RectTransformUtility.RectangleContainsScreenPoint(
                rectTransform,
                screenPoint,
                eventCamera)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                screenPoint,
                eventCamera,
                out Vector2 localPoint))
        {
            return false;
        }

        GetGridDimensions(out int columns, out int rows);
        Rect rect = rectTransform.rect;
        float xFromLeft = localPoint.x + rect.width * rectTransform.pivot.x;
        float yFromTop = rect.height * (1f - rectTransform.pivot.y)
            - localPoint.y;
        float xPitch = cellSize.x + spacing.x;
        float yPitch = cellSize.y + spacing.y;
        int physicalColumn = Mathf.Clamp(
            Mathf.RoundToInt(
                (xFromLeft - padding.left - cellSize.x * 0.5f) / xPitch),
            0,
            columns - 1);
        int physicalRow = Mathf.Clamp(
            Mathf.RoundToInt(
                (yFromTop - padding.top - cellSize.y * 0.5f) / yPitch),
            0,
            rows - 1);

        int column = IsRightCorner
            ? columns - 1 - physicalColumn
            : physicalColumn;
        int row = IsLowerCorner
            ? rows - 1 - physicalRow
            : physicalRow;
        gridIndex = startAxis == GridLayoutGroup.Axis.Horizontal
            ? row * columns + column
            : column * rows + row;
        return true;
    }

    public bool NormalizeSlotsToCapacity()
    {
        int capacity = Capacity;
        if (slots.Count == 0 || capacity <= 0)
            return false;

        orderedSlots.Clear();
        foreach (KeyValuePair<DesktopProcessUnit, int> entry in slots)
        {
            if (entry.Key != null)
                orderedSlots.Add(entry);
        }

        orderedSlots.Sort((left, right) =>
            left.Value.CompareTo(right.Value));

        bool changed = orderedSlots.Count != slots.Count;
        slots.Clear();
        for (int i = 0; i < orderedSlots.Count; i++)
        {
            KeyValuePair<DesktopProcessUnit, int> entry = orderedSlots[i];
            int requestedIndex = Mathf.Clamp(entry.Value, 0, capacity - 1);
            int resolvedIndex = FindFirstAvailableSlot(requestedIndex, true);
            if (resolvedIndex < 0)
                break;

            slots.Add(entry.Key, resolvedIndex);
            changed |= resolvedIndex != entry.Value;
        }

        return changed;
    }

    public void ApplyLayout()
    {
        if (slots.Count == 0)
            return;

        invalidUnits.Clear();
        foreach (KeyValuePair<DesktopProcessUnit, int> entry in slots)
        {
            DesktopProcessUnit unit = entry.Key;
            if (unit == null)
            {
                invalidUnits.Add(unit);
                continue;
            }

            ApplyUnitLayout(unit, entry.Value);
        }

        for (int i = 0; i < invalidUnits.Count; i++)
            slots.Remove(invalidUnits[i]);
    }

    public void ArrangeDirectChildrenInSiblingOrder()
    {
        ClearRuntimeSlots();
        EnsureDirectChildrenRegistered();
    }

    public void EnsureDirectChildrenRegistered()
    {
        if (layoutReparentDepth > 0)
            return;

        bool layoutChanged = false;
        for (int i = 0; i < transform.childCount; i++)
        {
            DesktopProcessUnit unit =
                transform.GetChild(i).GetComponent<DesktopProcessUnit>();
            if (unit == null || !unit.gameObject.activeSelf || slots.ContainsKey(unit))
                continue;

            int gridIndex = FindFirstAvailableSlot(0, true);
            if (gridIndex < 0)
                break;

            slots.Add(unit, gridIndex);
            layoutChanged = true;
        }

        if (layoutChanged)
            ApplyLayout();
    }

    void OnRectTransformDimensionsChange()
    {
        if (!Application.isPlaying || layoutReparentDepth > 0 || slots.Count == 0)
            return;

        NormalizeSlotsToCapacity();
        ApplyLayout();
    }

    void OnTransformChildrenChanged()
    {
        // Both source and destination already own their intended slot maps.
        // A layout reparent must not prune incoming icons that have yet to move,
        // or register outgoing icons again midway through a saved-layout restore.
        if (!Application.isPlaying || layoutReparentDepth > 0)
            return;

        invalidUnits.Clear();
        foreach (KeyValuePair<DesktopProcessUnit, int> entry in slots)
        {
            if (entry.Key == null || entry.Key.transform.parent != transform)
                invalidUnits.Add(entry.Key);
        }

        for (int i = 0; i < invalidUnits.Count; i++)
            slots.Remove(invalidUnits[i]);

        EnsureDirectChildrenRegistered();
    }

    int FindFirstAvailableSlot(int requestedIndex, bool wrap)
    {
        int capacity = Capacity;
        if (capacity <= 0 || slots.Count >= capacity)
            return -1;

        int startIndex = Mathf.Clamp(requestedIndex, 0, capacity - 1);
        for (int index = startIndex; index < capacity; index++)
        {
            if (GetUnitAt(index) == null)
                return index;
        }

        if (!wrap)
            return -1;

        for (int index = 0; index < startIndex; index++)
        {
            if (GetUnitAt(index) == null)
                return index;
        }

        return -1;
    }

    DesktopProcessUnit GetUnitAt(int gridIndex)
    {
        foreach (KeyValuePair<DesktopProcessUnit, int> entry in slots)
        {
            if (entry.Key != null && entry.Value == gridIndex)
                return entry.Key;
        }

        return null;
    }

    void ApplyUnitLayout(DesktopProcessUnit unit, int gridIndex)
    {
        RectTransform unitRect = unit.transform as RectTransform;
        if (unitRect == null)
            return;

        if (unitRect.parent != transform)
        {
            layoutReparentDepth++;
            try { unitRect.SetParent(transform, false); }
            finally { layoutReparentDepth--; }
        }

        GetGridDimensions(out int columns, out int rows);
        int column;
        int row;
        if (startAxis == GridLayoutGroup.Axis.Horizontal)
        {
            column = gridIndex % columns;
            row = gridIndex / columns;
        }
        else
        {
            column = gridIndex / rows;
            row = gridIndex % rows;
        }

        if (IsRightCorner)
            column = columns - 1 - column;
        if (IsLowerCorner)
            row = rows - 1 - row;

        unitRect.anchorMin = new Vector2(0f, 1f);
        unitRect.anchorMax = new Vector2(0f, 1f);
        unitRect.sizeDelta = cellSize;
        unitRect.localRotation = Quaternion.identity;
        unitRect.localScale = Vector3.one;
        unitRect.anchoredPosition = new Vector2(
            padding.left
                + column * (cellSize.x + spacing.x)
                + cellSize.x * unitRect.pivot.x,
            -padding.top
                - row * (cellSize.y + spacing.y)
                - cellSize.y * (1f - unitRect.pivot.y));
    }

    Vector2 GetSlotLocalCenter(
        int gridIndex,
        int columns,
        int rows)
    {
        int column;
        int row;
        if (startAxis == GridLayoutGroup.Axis.Horizontal)
        {
            column = gridIndex % columns;
            row = gridIndex / columns;
        }
        else
        {
            column = gridIndex / rows;
            row = gridIndex % rows;
        }

        if (IsRightCorner)
            column = columns - 1 - column;
        if (IsLowerCorner)
            row = rows - 1 - row;

        Rect rect = ResolveRectTransform().rect;
        return new Vector2(
            rect.xMin
                + padding.left
                + column * (cellSize.x + spacing.x)
                + cellSize.x * 0.5f,
            rect.yMax
                - padding.top
                - row * (cellSize.y + spacing.y)
                - cellSize.y * 0.5f);
    }

    void GetGridDimensions(out int columns, out int rows)
    {
        Rect rect = ResolveRectTransform().rect;
        float xPitch = Mathf.Max(LayoutEpsilon, cellSize.x + spacing.x);
        float yPitch = Mathf.Max(LayoutEpsilon, cellSize.y + spacing.y);
        float availableWidth = Mathf.Max(0f, rect.width - padding.horizontal);
        float availableHeight = Mathf.Max(0f, rect.height - padding.vertical);
        columns = Mathf.Max(
            1,
            Mathf.FloorToInt(
                (availableWidth + spacing.x + LayoutEpsilon) / xPitch));
        rows = Mathf.Max(
            1,
            Mathf.FloorToInt(
                (availableHeight + spacing.y + LayoutEpsilon) / yPitch));
    }

    RectTransform ResolveRectTransform()
    {
        if (_rectTransform == null)
            _rectTransform = transform as RectTransform;
        return _rectTransform;
    }

    bool IsRightCorner => startCorner == GridLayoutGroup.Corner.UpperRight
        || startCorner == GridLayoutGroup.Corner.LowerRight;

    bool IsLowerCorner => startCorner == GridLayoutGroup.Corner.LowerLeft
        || startCorner == GridLayoutGroup.Corner.LowerRight;

    void OnValidate()
    {
        if (padding == null)
            padding = new RectOffset();

        cellSize.x = Mathf.Max(LayoutEpsilon, cellSize.x);
        cellSize.y = Mathf.Max(LayoutEpsilon, cellSize.y);
        spacing.x = Mathf.Max(0f, spacing.x);
        spacing.y = Mathf.Max(0f, spacing.y);
    }
}
