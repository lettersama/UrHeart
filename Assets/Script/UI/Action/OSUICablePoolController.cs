using System.Collections.Generic;
using UnityEngine;

public enum OSUICableResourceType
{
    Heart = 0,
    Energy = 1,
    Error = 2
}

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSUICablePoolController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] RectTransform fixedPointGroup;

    [Header("Pool")]
    [SerializeField, Min(1)] int capacity = 20;
    [SerializeField, Min(1f)] float cableLength = 150f;

    [Header("Cable Response")]
    [SerializeField, Range(0f, 1f)] float motionRetention = 0.95f;
    [SerializeField, Min(0f)] float layerSwitchOffsetPixels = 1f;

    [Header("Fixed Point Leads (Reference Pixels)")]
    [SerializeField, Min(0)] int heartLeadPixels = 1;
    [SerializeField, Min(0)] int energyLeadPixels = 2;
    [SerializeField, Min(0)] int errorLeadPixels = 3;

    [Header("Resource Colors")]
    [SerializeField] Color heartColor =
        new Color32(0x70, 0x08, 0x0A, 0xFF);
    [SerializeField] Color energyColor =
        new Color32(0x1D, 0x37, 0x70, 0xFF);
    [SerializeField] Color errorColor =
        new Color32(0x70, 0x41, 0x1A, 0xFF);

    [Header("Resource Pulse")]
    [SerializeField] Color heartPulseColor =
        new Color32(0xE0, 0x29, 0x20, 0xFF);
    [SerializeField] Color energyPulseColor =
        new Color32(0x44, 0x96, 0xB7, 0xFF);
    [SerializeField] Color errorPulseColor =
        new Color32(0xCC, 0xA7, 0x3B, 0xFF);
    [SerializeField, Min(1)] int pulseLengthPixels = 6;
    [SerializeField, Min(1f)] float pulsePixelsPerSecond = 240f;

    readonly List<OSUICableGraphic> pooledCables =
        new List<OSUICableGraphic>(20);
    readonly List<OSUICableGraphic> pooledForegrounds =
        new List<OSUICableGraphic>(20);
    readonly Dictionary<OSUICableGraphic, Object> activeOwners =
        new Dictionary<OSUICableGraphic, Object>(20);
    readonly Dictionary<OSUICableGraphic, OSUICableGraphic>
        activeForegrounds =
            new Dictionary<OSUICableGraphic, OSUICableGraphic>(20);
    readonly List<OSUICableGraphic> releaseBuffer =
        new List<OSUICableGraphic>(20);
    readonly List<PendingForegroundMove> pendingForegroundMoves =
        new List<PendingForegroundMove>(20);
    readonly Vector3[] rectWorldCorners = new Vector3[4];

    bool warnedPoolExhausted;

    public RectTransform FixedPointGroup
    {
        get
        {
            ResolveReferences();
            return fixedPointGroup;
        }
    }

    public int Capacity => Mathf.Max(1, capacity);
    public int PooledCableCount => pooledCables.Count;
    public int ActiveCableCount => activeOwners.Count;
    public float CableLength => Mathf.Max(1f, cableLength);
    public float MotionRetention => Mathf.Clamp01(motionRetention);
    public float LayerSwitchOffsetPixels =>
        Mathf.Max(0f, layerSwitchOffsetPixels);
    public int PulseLengthPixels => Mathf.Max(1, pulseLengthPixels);
    public float PulsePixelsPerSecond =>
        Mathf.Max(1f, pulsePixelsPerSecond);

    void Awake()
    {
        PreparePool();
        ReleaseAll();
    }

    void LateUpdate()
    {
        ProcessPendingForegroundMoves();
        if (activeOwners.Count > 0)
            RefreshCablePresentation();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
            ReleaseAll();
    }

    void OnDestroy()
    {
        activeOwners.Clear();
        activeForegrounds.Clear();
        pooledCables.Clear();
        pooledForegrounds.Clear();
        releaseBuffer.Clear();
        pendingForegroundMoves.Clear();
    }

    public void PreparePool()
    {
        ResolveReferences();
        RebuildPoolCache();
        while (pooledCables.Count < Capacity)
            pooledCables.Add(CreateCable());
        while (pooledForegrounds.Count < Capacity)
        {
            pooledForegrounds.Add(CreateForegroundGraphic(
                pooledForegrounds.Count));
        }
    }

    public OSUICableGraphic Acquire(
        Object owner,
        OSUICableResourceType resourceType,
        RectTransform startPoint,
        RectTransform endPoint,
        OSUICableLayerMode layerMode =
            OSUICableLayerMode.HeartbeatOnly,
        RectTransform ownerRenderParent = null)
    {
        if (owner == null || startPoint == null || endPoint == null)
            return null;

        if (pooledCables.Count < Capacity
            || pooledForegrounds.Count < Capacity)
        {
            PreparePool();
        }

        OSUICableGraphic cable = FindAvailableCable();
        bool useLayeredRendering =
            layerMode == OSUICableLayerMode.OwnerWindowToHeartbeat
            && ownerRenderParent != null;
        OSUICableGraphic foreground = useLayeredRendering
            ? FindAvailableForeground()
            : null;
        if (cable == null || (useLayeredRendering && foreground == null))
        {
            WarnPoolExhausted();
            return null;
        }

        activeOwners[cable] = owner;
        Color resourceColor = GetResourceColor(resourceType);
        RectTransform poolParent = transform as RectTransform;
        if (cable.rectTransform.parent != poolParent)
            cable.SetRenderParent(poolParent, -1);
        cable.color = resourceColor;
        cable.raycastTarget = false;
        cable.SetMotionRetention(MotionRetention);
        cable.Configure(
            startPoint,
            endPoint,
            CableLength,
            GetStartLeadPixels(resourceType));

        if (useLayeredRendering)
        {
            cable.SetRenderSection(
                OSUICableRenderSection.FixedPointToLayerSplit);
            foreground.SetRenderParent(ownerRenderParent, 0);
            foreground.enabled = true;
            foreground.color = resourceColor;
            foreground.raycastTarget = false;
            foreground.ConfigureRenderFollower(
                cable,
                OSUICableRenderSection.LayerSplitToMovingPoint);
            activeForegrounds[cable] = foreground;
            foreground.gameObject.SetActive(true);
        }
        else
        {
            cable.SetRenderSection(OSUICableRenderSection.Full);
            cable.SetLayerSplit(null, 0f);
        }

        cable.gameObject.SetActive(true);
        RefreshCablePresentation();
        warnedPoolExhausted = false;
        return cable;
    }

    public void Release(OSUICableGraphic cable)
    {
        if (cable == null || !activeOwners.Remove(cable))
            return;

        ReleaseForeground(cable);
        RestoreCableToPool(cable);
        cable.gameObject.SetActive(false);
        cable.Configure(null, null, CableLength, 0);
        warnedPoolExhausted = false;
    }

    public void ReleaseOwner(Object owner)
    {
        if (owner == null || activeOwners.Count == 0)
            return;

        releaseBuffer.Clear();
        foreach (KeyValuePair<OSUICableGraphic, Object> pair in activeOwners)
        {
            if (pair.Value == owner)
                releaseBuffer.Add(pair.Key);
        }

        for (int i = 0; i < releaseBuffer.Count; i++)
            Release(releaseBuffer[i]);
        releaseBuffer.Clear();
    }

    public void ReleaseAll()
    {
        activeOwners.Clear();
        activeForegrounds.Clear();
        for (int i = 0; i < pooledCables.Count; i++)
        {
            OSUICableGraphic cable = pooledCables[i];
            if (cable == null)
                continue;

            RestoreCableToPool(cable);
            cable.gameObject.SetActive(false);
            cable.Configure(null, null, CableLength, 0);
        }

        for (int i = 0; i < pooledForegrounds.Count; i++)
            RestoreForegroundToPool(pooledForegrounds[i]);

        warnedPoolExhausted = false;
    }

    // Kept for OS_Handle compatibility. Cable layers no longer depend on
    // which window most recently received a pointer press.
    public void SetMouseActiveWindow(OSWindowUnit window)
    {
        RefreshCablePresentation();
    }

    public void RefreshCablePresentation()
    {
        RectTransform splitSpace = transform as RectTransform;
        foreach (KeyValuePair<OSUICableGraphic, Object> pair in activeOwners)
        {
            OSUICableGraphic cable = pair.Key;
            OSUICableConnectionGroup connectionGroup =
                pair.Value as OSUICableConnectionGroup;
            if (cable == null || connectionGroup == null)
                continue;

            bool hasForeground = activeForegrounds.TryGetValue(
                cable,
                out OSUICableGraphic foreground);
            if (connectionGroup.LayerMode
                    == OSUICableLayerMode.HeartbeatOnly
                || !hasForeground)
            {
                if (foreground != null)
                    foreground.enabled = false;
                cable.SetRenderSection(OSUICableRenderSection.Full);
                cable.SetLayerSplit(null, 0f);
                continue;
            }

            OSWindowUnit ownerWindow = ResolveTopLevelWindow(
                connectionGroup.OwnerWindow);
            if (!TryResolveLayerSplitY(
                    ownerWindow,
                    splitSpace,
                    out float splitY))
            {
                foreground.enabled = false;
                cable.SetRenderSection(OSUICableRenderSection.Full);
                cable.SetLayerSplit(null, 0f);
                continue;
            }

            foreground.enabled = true;
            cable.SetRenderSection(
                OSUICableRenderSection.FixedPointToLayerSplit);
            cable.SetLayerSplit(splitSpace, splitY);
        }
    }

    public Color GetResourceColor(OSUICableResourceType resourceType)
    {
        switch (resourceType)
        {
            case OSUICableResourceType.Heart:
                return heartColor;
            case OSUICableResourceType.Energy:
                return energyColor;
            case OSUICableResourceType.Error:
                return errorColor;
            default:
                return Color.white;
        }
    }

    public Color GetResourcePulseColor(
        OSUICableResourceType resourceType)
    {
        switch (resourceType)
        {
            case OSUICableResourceType.Heart:
                return heartPulseColor;
            case OSUICableResourceType.Energy:
                return energyPulseColor;
            case OSUICableResourceType.Error:
                return errorPulseColor;
            default:
                return Color.white;
        }
    }

    public int GetStartLeadPixels(OSUICableResourceType resourceType)
    {
        switch (resourceType)
        {
            case OSUICableResourceType.Heart:
                return Mathf.Max(0, heartLeadPixels);
            case OSUICableResourceType.Energy:
                return Mathf.Max(0, energyLeadPixels);
            case OSUICableResourceType.Error:
                return Mathf.Max(0, errorLeadPixels);
            default:
                return 0;
        }
    }

    bool TryResolveLayerSplitY(
        OSWindowUnit ownerWindow,
        RectTransform splitSpace,
        out float splitY)
    {
        splitY = 0f;
        if (!IsWindowAvailable(ownerWindow) || splitSpace == null)
            return false;

        RectTransform ownerRect = ownerWindow.WindowRect;
        if (ownerRect == null)
            return false;

        float lowestBottom = GetRectBottomInSpace(ownerRect, splitSpace);
        Transform stackingParent = ownerWindow.transform.parent;
        if (stackingParent != null)
        {
            int ownerSiblingIndex = ownerWindow.transform.GetSiblingIndex();
            for (int i = 0; i < ownerSiblingIndex; i++)
            {
                OSWindowUnit lowerWindow = stackingParent.GetChild(i)
                    .GetComponent<OSWindowUnit>();
                if (!IsWindowAvailable(lowerWindow))
                    continue;

                RectTransform lowerRect = lowerWindow.WindowRect;
                if (lowerRect == null)
                    continue;

                lowestBottom = Mathf.Min(
                    lowestBottom,
                    GetRectBottomInSpace(lowerRect, splitSpace));
            }
        }

        splitY = Mathf.Floor(lowestBottom)
                 - LayerSwitchOffsetPixels;
        return true;
    }

    float GetRectBottomInSpace(
        RectTransform sourceRect,
        RectTransform targetSpace)
    {
        sourceRect.GetWorldCorners(rectWorldCorners);
        float bottom = float.PositiveInfinity;
        for (int i = 0; i < rectWorldCorners.Length; i++)
        {
            Vector2 localCorner = targetSpace.InverseTransformPoint(
                rectWorldCorners[i]);
            bottom = Mathf.Min(bottom, localCorner.y);
        }

        return bottom;
    }

    OSUICableGraphic FindAvailableCable()
    {
        int usableCount = Mathf.Min(Capacity, pooledCables.Count);
        for (int i = 0; i < usableCount; i++)
        {
            OSUICableGraphic cable = pooledCables[i];
            if (cable != null
                && !activeOwners.ContainsKey(cable)
                && !cable.gameObject.activeSelf)
            {
                return cable;
            }
        }

        return null;
    }

    OSUICableGraphic FindAvailableForeground()
    {
        int usableCount = Mathf.Min(Capacity, pooledForegrounds.Count);
        for (int i = 0; i < usableCount; i++)
        {
            OSUICableGraphic foreground = pooledForegrounds[i];
            if (foreground != null
                && !foreground.gameObject.activeSelf
                && !IsForegroundReserved(foreground))
            {
                return foreground;
            }
        }

        return null;
    }

    void ReleaseForeground(OSUICableGraphic cable)
    {
        if (!activeForegrounds.TryGetValue(
                cable,
                out OSUICableGraphic foreground))
        {
            return;
        }

        activeForegrounds.Remove(cable);
        RestoreForegroundToPool(foreground);
    }

    void RestoreForegroundToPool(OSUICableGraphic foreground)
    {
        if (foreground == null)
            return;

        if (foreground.gameObject.activeInHierarchy)
            foreground.gameObject.SetActive(false);
        foreground.enabled = true;
        foreground.ConfigureRenderFollower(
            null,
            OSUICableRenderSection.Full);
        RectTransform poolParent = transform as RectTransform;
        if (foreground.rectTransform.parent != poolParent)
        {
            QueueForegroundMove(
                foreground,
                poolParent,
                -1,
                null,
                false);
        }
        else
        {
            CancelPendingForegroundMove(foreground);
        }
    }

    void RestoreCableToPool(OSUICableGraphic cable)
    {
        if (cable == null)
            return;

        cable.SetAvoidanceRects(null, 0f);
        cable.ClearLayeredRendering();
        RectTransform poolParent = transform as RectTransform;
        if (cable.rectTransform.parent != poolParent)
            cable.SetRenderParent(poolParent, -1);
    }

    bool IsForegroundReserved(OSUICableGraphic foreground)
    {
        foreach (KeyValuePair<
                     OSUICableGraphic,
                     OSUICableGraphic> pair in activeForegrounds)
        {
            if (pair.Value == foreground)
                return true;
        }

        for (int i = 0; i < pendingForegroundMoves.Count; i++)
        {
            if (pendingForegroundMoves[i].Foreground == foreground)
                return true;
        }

        return false;
    }

    void QueueForegroundMove(
        OSUICableGraphic foreground,
        RectTransform targetParent,
        int siblingIndex,
        OSUICableGraphic sourceCable,
        bool activateAfterMove)
    {
        PendingForegroundMove move = new PendingForegroundMove(
            foreground,
            targetParent,
            siblingIndex,
            sourceCable,
            activateAfterMove);
        for (int i = pendingForegroundMoves.Count - 1; i >= 0; i--)
        {
            if (pendingForegroundMoves[i].Foreground != foreground)
                continue;

            pendingForegroundMoves[i] = move;
            return;
        }

        pendingForegroundMoves.Add(move);
    }

    void CancelPendingForegroundMove(OSUICableGraphic foreground)
    {
        for (int i = pendingForegroundMoves.Count - 1; i >= 0; i--)
        {
            if (pendingForegroundMoves[i].Foreground == foreground)
                pendingForegroundMoves.RemoveAt(i);
        }
    }

    void ProcessPendingForegroundMoves()
    {
        RectTransform poolParent = transform as RectTransform;
        for (int i = pendingForegroundMoves.Count - 1; i >= 0; i--)
        {
            PendingForegroundMove move = pendingForegroundMoves[i];
            OSUICableGraphic foreground = move.Foreground;
            if (foreground == null)
            {
                pendingForegroundMoves.RemoveAt(i);
                continue;
            }

            bool hasActiveSource = move.ActivateAfterMove
                && move.SourceCable != null
                && move.SourceCable.gameObject.activeSelf
                && activeOwners.ContainsKey(move.SourceCable)
                && activeForegrounds.TryGetValue(
                    move.SourceCable,
                    out OSUICableGraphic activeForeground)
                && activeForeground == foreground;
            if (hasActiveSource
                && (move.TargetParent == null
                    || !move.TargetParent.gameObject.activeInHierarchy))
            {
                continue;
            }

            RectTransform targetParent = hasActiveSource
                ? move.TargetParent
                : poolParent;
            int siblingIndex = hasActiveSource
                ? move.SiblingIndex
                : -1;
            if (targetParent == null)
            {
                pendingForegroundMoves.RemoveAt(i);
                continue;
            }

            if (foreground.gameObject.activeSelf)
                foreground.gameObject.SetActive(false);
            if (!hasActiveSource)
            {
                foreground.ConfigureRenderFollower(
                    null,
                    OSUICableRenderSection.Full);
            }

            foreground.SetRenderParent(targetParent, siblingIndex);
            foreground.enabled = true;
            if (hasActiveSource)
                foreground.gameObject.SetActive(true);

            pendingForegroundMoves.RemoveAt(i);
        }
    }

    void WarnPoolExhausted()
    {
        if (warnedPoolExhausted)
            return;

        warnedPoolExhausted = true;
        Debug.LogWarning(
            $"[OS Cable Pool] Pool capacity {Capacity} is exhausted.",
            this);
    }

    static OSWindowUnit ResolveTopLevelWindow(OSWindowUnit window)
    {
        if (window == null)
            return null;

        OSWindowUnit topLevelWindow = window;
        Transform current = window.transform.parent;
        while (current != null)
        {
            OSWindowUnit parentWindow = current.GetComponent<OSWindowUnit>();
            if (parentWindow != null)
                topLevelWindow = parentWindow;

            current = current.parent;
        }

        return topLevelWindow;
    }

    static bool IsWindowAvailable(OSWindowUnit window)
    {
        return window != null
            && window.IsOpen
            && !window.IsMinimized
            && window.gameObject.activeInHierarchy;
    }

    void ResolveReferences()
    {
        if (fixedPointGroup != null || transform.parent == null)
            return;

        fixedPointGroup = transform.parent.Find("PointGroup")
            as RectTransform;
    }

    void RebuildPoolCache()
    {
        pooledCables.RemoveAll(cable => cable == null);
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("Cable_"))
                continue;

            OSUICableGraphic cable = child.GetComponent<OSUICableGraphic>();
            if (cable != null && !pooledCables.Contains(cable))
                pooledCables.Add(cable);
        }
    }

    OSUICableGraphic CreateCable()
    {
        int cableIndex = FindAvailableCableIndex();
        return CreateGraphic($"Cable_{cableIndex:00}");
    }

    OSUICableGraphic CreateForegroundGraphic(int index)
    {
        return CreateGraphic($"CableForeground_{index:00}");
    }

    OSUICableGraphic CreateGraphic(string objectName)
    {
        GameObject cableObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(OSUICableGraphic));
        cableObject.layer = gameObject.layer;

        RectTransform cableRect = cableObject.transform as RectTransform;
        cableRect.SetParent(transform, false);
        cableRect.anchorMin = Vector2.zero;
        cableRect.anchorMax = Vector2.one;
        cableRect.anchoredPosition = Vector2.zero;
        cableRect.sizeDelta = Vector2.zero;
        cableRect.localScale = Vector3.one;

        OSUICableGraphic cable =
            cableObject.GetComponent<OSUICableGraphic>();
        cable.raycastTarget = false;
        cableObject.SetActive(false);
        return cable;
    }

    int FindAvailableCableIndex()
    {
        int index = 0;
        while (pooledCables.Exists(cable =>
                   cable != null && cable.name == $"Cable_{index:00}"))
        {
            index++;
        }

        return index;
    }

    readonly struct PendingForegroundMove
    {
        public readonly OSUICableGraphic Foreground;
        public readonly RectTransform TargetParent;
        public readonly int SiblingIndex;
        public readonly OSUICableGraphic SourceCable;
        public readonly bool ActivateAfterMove;

        public PendingForegroundMove(
            OSUICableGraphic foreground,
            RectTransform targetParent,
            int siblingIndex,
            OSUICableGraphic sourceCable,
            bool activateAfterMove)
        {
            Foreground = foreground;
            TargetParent = targetParent;
            SiblingIndex = siblingIndex;
            SourceCable = sourceCable;
            ActivateAfterMove = activateAfterMove;
        }
    }

    void Reset()
    {
        ResolveReferences();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        cableLength = Mathf.Max(1f, cableLength);
        motionRetention = Mathf.Clamp01(motionRetention);
        layerSwitchOffsetPixels = Mathf.Max(0f, layerSwitchOffsetPixels);
        heartLeadPixels = Mathf.Max(0, heartLeadPixels);
        energyLeadPixels = Mathf.Max(0, energyLeadPixels);
        errorLeadPixels = Mathf.Max(0, errorLeadPixels);
        pulseLengthPixels = Mathf.Max(1, pulseLengthPixels);
        pulsePixelsPerSecond = Mathf.Max(1f, pulsePixelsPerSecond);
        ResolveReferences();
    }
#endif
}
