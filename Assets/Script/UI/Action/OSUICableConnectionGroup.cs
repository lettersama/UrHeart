using System.Collections.Generic;
using UnityEngine;

public enum OSUICableLayerMode
{
    OwnerWindowToHeartbeat = 0,
    HeartbeatOnly = 1
}

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSUICableConnectionGroup : MonoBehaviour
{
    static readonly OSUICableResourceType[] ResourceTypes =
    {
        OSUICableResourceType.Heart,
        OSUICableResourceType.Energy,
        OSUICableResourceType.Error
    };

    [Header("References")]
    [SerializeField] OSUICablePoolController cablePool;
    [SerializeField] OSWindowUnit ownerWindow;
    [SerializeField] RectTransform targetPointGroup;

    [Header("Presentation")]
    [SerializeField] OSUICableLayerMode layerMode =
        OSUICableLayerMode.OwnerWindowToHeartbeat;

    static readonly Color HeartPulseFallback =
        new Color32(0xE0, 0x29, 0x20, 0xFF);
    static readonly Color EnergyPulseFallback =
        new Color32(0x44, 0x96, 0xB7, 0xFF);
    static readonly Color ErrorPulseFallback =
        new Color32(0xCC, 0xA7, 0x3B, 0xFF);

    readonly Dictionary<OSUICableResourceType, OSUICableGraphic>
        activeCables =
            new Dictionary<OSUICableResourceType, OSUICableGraphic>(3);

    OSWindowUnit boundWindow;
    bool hasStarted;
    bool warnedMissingReference;
    bool connectionsAvailable = true;

    public int ActiveConnectionCount => activeCables.Count;
    public OSUICableLayerMode LayerMode => layerMode;
    public OSWindowUnit OwnerWindow
    {
        get
        {
            ResolveReferences();
            return ownerWindow;
        }
    }

    void Awake()
    {
        ResolveReferences();
    }

    void Start()
    {
        hasStarted = true;
        BindWindow();
        RefreshConnections();
    }

    void OnEnable()
    {
        if (!hasStarted)
            return;

        ResolveReferences();
        BindWindow();
        RefreshConnections();
    }

    void OnDisable()
    {
        UnbindWindow();
        ReleaseConnections();
    }

    void OnDestroy()
    {
        UnbindWindow();
        ReleaseConnections();
    }

    public void RefreshConnections()
    {
        ResolveReferences();
        if (hasStarted)
            BindWindow();
        if (!connectionsAvailable
            || !isActiveAndEnabled
            || ownerWindow == null
            || !ownerWindow.IsOpen)
        {
            ReleaseConnections();
            return;
        }

        Connect();
    }

    public void ReleaseConnections()
    {
        if (cablePool != null)
            cablePool.ReleaseOwner(this);
        else
        {
            foreach (KeyValuePair<
                         OSUICableResourceType,
                         OSUICableGraphic> pair in activeCables)
            {
                OSUICableGraphic cable = pair.Value;
                if (cable != null)
                    cable.gameObject.SetActive(false);
            }
        }

        activeCables.Clear();
    }

    public void SetConnectionAvailability(bool available)
    {
        connectionsAvailable = available;
        if (available)
            RefreshConnections();
        else
            ReleaseConnections();
    }

    public bool PlayResourcePulse(
        OSUICableResourceType resourceType,
        bool towardFixedPoint = true)
    {
        if (!activeCables.TryGetValue(
                resourceType,
                out OSUICableGraphic cable)
            || cable == null
            || !cable.gameObject.activeInHierarchy)
        {
            RefreshConnections();
            if (!activeCables.TryGetValue(resourceType, out cable)
                || cable == null
                || !cable.gameObject.activeInHierarchy)
            {
                return false;
            }
        }

        Color pulseColor = cablePool != null
            ? cablePool.GetResourcePulseColor(resourceType)
            : GetFallbackPulseColor(resourceType);
        int pulseLength = cablePool != null
            ? cablePool.PulseLengthPixels
            : 6;
        float pulseSpeed = cablePool != null
            ? cablePool.PulsePixelsPerSecond
            : 240f;
        return cable.PlayColorPulse(
            pulseColor,
            pulseLength,
            pulseSpeed,
            towardFixedPoint);
    }

    public bool PlayPointPulse(
        RectTransform point,
        bool towardFixedPoint = true)
    {
        ResolveReferences();
        if (point == null
            || targetPointGroup == null
            || point.parent != targetPointGroup
            || !TryGetResourceType(point.name, out OSUICableResourceType type))
        {
            return false;
        }

        return PlayResourcePulse(type, towardFixedPoint);
    }

    public bool PlayHeartPulse()
    {
        return PlayResourcePulse(OSUICableResourceType.Heart);
    }

    public bool PlayEnergyPulse()
    {
        return PlayResourcePulse(OSUICableResourceType.Energy);
    }

    public bool PlayErrorPulse()
    {
        return PlayResourcePulse(OSUICableResourceType.Error);
    }

    void Connect()
    {
        if (HasCompleteConnection())
            return;

        ReleaseConnections();
        RectTransform fixedPointGroup = cablePool != null
            ? cablePool.FixedPointGroup
            : null;
        if (cablePool == null
            || fixedPointGroup == null
            || targetPointGroup == null)
        {
            WarnMissingReference("pool or point group");
            return;
        }

        for (int i = 0; i < ResourceTypes.Length; i++)
        {
            OSUICableResourceType resourceType = ResourceTypes[i];
            string pointName = GetPointName(resourceType);
            RectTransform startPoint = fixedPointGroup.Find(pointName)
                as RectTransform;
            RectTransform endPoint = targetPointGroup.Find(pointName)
                as RectTransform;
            if (endPoint == null || !endPoint.gameObject.activeSelf)
                continue;

            if (startPoint == null)
            {
                WarnMissingReference(pointName);
                ReleaseConnections();
                return;
            }

            OSUICableGraphic cable = cablePool.Acquire(
                this,
                resourceType,
                startPoint,
                endPoint,
                layerMode,
                ownerWindow != null ? ownerWindow.WindowRect : null);
            if (cable == null)
            {
                ReleaseConnections();
                return;
            }

            activeCables.Add(resourceType, cable);
        }

        warnedMissingReference = false;
    }

    bool HasCompleteConnection()
    {
        int expectedConnectionCount = 0;
        for (int i = 0; i < ResourceTypes.Length; i++)
        {
            OSUICableResourceType resourceType = ResourceTypes[i];
            RectTransform point = targetPointGroup != null
                ? targetPointGroup.Find(GetPointName(resourceType))
                    as RectTransform
                : null;
            if (point == null || !point.gameObject.activeSelf)
                continue;

            expectedConnectionCount++;
            if (!activeCables.TryGetValue(
                    resourceType,
                    out OSUICableGraphic cable)
                || cable == null
                || !cable.gameObject.activeSelf)
            {
                return false;
            }
        }

        return activeCables.Count == expectedConnectionCount;
    }

    void ResolveReferences()
    {
        if (targetPointGroup == null)
            targetPointGroup = transform as RectTransform;

        OSWindowUnit nearestOwnerWindow =
            GetComponentInParent<OSWindowUnit>();
        if (nearestOwnerWindow != null
            && (ownerWindow == null
                || !transform.IsChildOf(ownerWindow.transform)))
        {
            ownerWindow = nearestOwnerWindow;
        }

        if (cablePool != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            cablePool = canvas.GetComponentInChildren<
                OSUICablePoolController>(true);
        }
    }

    void BindWindow()
    {
        if (boundWindow == ownerWindow)
            return;

        UnbindWindow();
        boundWindow = ownerWindow;
        if (boundWindow == null)
            return;

        boundWindow.OpenStarted += HandleWindowOpened;
        boundWindow.Closed += HandleWindowClosed;
    }

    void UnbindWindow()
    {
        if (boundWindow == null)
            return;

        boundWindow.OpenStarted -= HandleWindowOpened;
        boundWindow.Closed -= HandleWindowClosed;
        boundWindow = null;
    }

    void HandleWindowOpened(OSWindowUnit window)
    {
        if (window == ownerWindow
            && connectionsAvailable
            && isActiveAndEnabled)
            Connect();
    }

    void HandleWindowClosed(OSWindowUnit window)
    {
        if (window == ownerWindow)
            ReleaseConnections();
    }

    void WarnMissingReference(string referenceName)
    {
        if (warnedMissingReference)
            return;

        warnedMissingReference = true;
        Debug.LogWarning(
            $"[OS Cable Connection] Missing {referenceName} for {name}.",
            this);
    }

    static string GetPointName(OSUICableResourceType resourceType)
    {
        switch (resourceType)
        {
            case OSUICableResourceType.Heart:
                return "HeartPoint";
            case OSUICableResourceType.Energy:
                return "EnergyPoint";
            case OSUICableResourceType.Error:
                return "ErrorPoint";
            default:
                return string.Empty;
        }
    }

    static bool TryGetResourceType(
        string pointName,
        out OSUICableResourceType resourceType)
    {
        for (int i = 0; i < ResourceTypes.Length; i++)
        {
            OSUICableResourceType candidate = ResourceTypes[i];
            if (GetPointName(candidate) != pointName)
                continue;

            resourceType = candidate;
            return true;
        }

        resourceType = default;
        return false;
    }

    static Color GetFallbackPulseColor(
        OSUICableResourceType resourceType)
    {
        switch (resourceType)
        {
            case OSUICableResourceType.Heart:
                return HeartPulseFallback;
            case OSUICableResourceType.Energy:
                return EnergyPulseFallback;
            case OSUICableResourceType.Error:
                return ErrorPulseFallback;
            default:
                return Color.white;
        }
    }

    void Reset()
    {
        ResolveReferences();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ResolveReferences();
    }
#endif
}
