using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSErrorWindowPoolController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] RectTransform poolRect;
    [SerializeField] RectTransform protectedCharacterRect;
    [SerializeField] RectTransform protectedFileDesktopProcessRect;
    [SerializeField] RectTransform protectedStateCheckDesktopProcessRect;

    [Header("Spawn Timing")]
    [SerializeField, Min(1)] int capacity = 20;
    [SerializeField, Min(0.01f)] float initialInterval = 1.2f;
    [SerializeField, Min(0.01f)] float finalInterval = 0.4f;
    [SerializeField, Min(0.01f)] float accelerationDuration = 60f;
    [SerializeField, Min(0.1f)] float firstInteractionInterval = 2.5f;

    enum SpawnPacing { Normal, GuidePaused, AwaitingFirstInteraction }

    readonly List<OSWindowUnit> poolWindows = new List<OSWindowUnit>();
    readonly LinkedList<OSWindowUnit> activationOrder =
        new LinkedList<OSWindowUnit>();
    readonly List<SpawnArea> spawnAreas = new List<SpawnArea>(4);
    readonly Vector3[] worldCorners = new Vector3[4];

    Coroutine spawnRoutine;
    OS_Handle osHandle;
    OSStateCheckWindowController stateCheckController;
    float effectStartedAt;
    bool prepared;
    bool warnedMissingWindows;
    bool warnedNoSpawnArea;

    public bool IsRunning => spawnRoutine != null;
    public int PoolWindowCount => poolWindows.Count;
    public int ActiveWindowCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < poolWindows.Count; i++)
            {
                if (poolWindows[i] != null && poolWindows[i].IsOpen)
                    count++;
            }

            return count;
        }
    }

    public float CurrentInterval
    {
        get
        {
            return IsRunning
                ? GetSpawnInterval(GetSpawnPacing())
                : EvaluateInterval(0f);
        }
    }

    void Awake()
    {
        PrepareRuntime();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
            StopEffect();
    }

    public void PrepareRuntime()
    {
        StopSpawnRoutine();
        ResolveReferences();
        RebuildWindowCache();
        CloseAllWindows();
        activationOrder.Clear();
        warnedNoSpawnArea = false;
        prepared = true;
    }

    public void StartEffect()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[OS ErrorPool] The effect can only run in Play Mode.",
                this);
            return;
        }

        if (!prepared)
            PrepareRuntime();

        if (spawnRoutine != null)
            return;

        if (poolWindows.Count == 0)
        {
            WarnMissingWindows();
            return;
        }

        effectStartedAt = Time.realtimeSinceStartup;
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    public void StopEffect()
    {
        StopSpawnRoutine();
        CloseAllWindows();
        activationOrder.Clear();
        warnedNoSpawnArea = false;
    }

    public int CloseWindowsOverlappingProtectedCharacterArea()
    {
        if (!prepared)
        {
            ResolveReferences();
            RebuildWindowCache();
            prepared = true;
        }

        if (!TryGetRectInPool(protectedCharacterRect, out Rect protectedRect))
            return 0;

        int closedCount = 0;
        for (int i = 0; i < poolWindows.Count; i++)
        {
            OSWindowUnit window = poolWindows[i];
            RectTransform windowRect = window != null
                ? window.transform as RectTransform
                : null;
            if (window == null
                || !window.IsOpen
                || !TryGetRectInPool(windowRect, out Rect activeWindowRect)
                || !activeWindowRect.Overlaps(protectedRect))
            {
                continue;
            }

            window.Close();
            closedCount++;
        }

        if (closedCount > 0)
            RemoveClosedWindowsFromOrder();

        return closedCount;
    }

    public bool OpenNextWindow()
    {
        if (!prepared)
            PrepareRuntime();

        if (GetSpawnPacing() == SpawnPacing.GuidePaused)
            return false;

        RemoveClosedWindowsFromOrder();
        OSWindowUnit window = FindAvailableWindow();
        if (window == null)
            window = ReclaimOldestWindow();

        if (window == null)
        {
            WarnMissingWindows();
            return false;
        }

        if (!TryPlaceWindow(window))
        {
            if (!warnedNoSpawnArea)
            {
                warnedNoSpawnArea = true;
                Debug.LogWarning(
                    "[OS ErrorPool] No position can fit without covering a protected OS area.",
                    this);
            }

            return false;
        }

        warnedNoSpawnArea = false;
        window.OpenStandalone();
        window.transform.SetAsLastSibling();
        activationOrder.AddLast(window);
        return window.IsOpen;
    }

    IEnumerator SpawnLoop()
    {
        SpawnPacing pacing = GetSpawnPacing();
        float nextSpawnAt = Time.realtimeSinceStartup + GetSpawnInterval(pacing);
        while (true)
        {
            yield return null;
            float now = Time.realtimeSinceStartup;
            SpawnPacing currentPacing = GetSpawnPacing();
            if (currentPacing != pacing)
            {
                pacing = currentPacing;
                // Start a fresh wait on every guide transition. Pausing never
                // leaves overdue windows to appear together when play resumes.
                nextSpawnAt = now + GetSpawnInterval(pacing);
            }

            if (pacing == SpawnPacing.GuidePaused || now < nextSpawnAt)
                continue;

            OpenNextWindow();
            nextSpawnAt = now + GetSpawnInterval(pacing);
        }
    }

    SpawnPacing GetSpawnPacing()
    {
        // OS references may finish resolving after this pool's Awake. Reading
        // the cached parent's property avoids a hierarchy search every frame.
        if (stateCheckController == null && osHandle != null)
            stateCheckController = osHandle.StateCheckWindowController;
        if (stateCheckController == null)
            return SpawnPacing.Normal;
        if (stateCheckController.IsFaultGuidePlaying)
            return SpawnPacing.GuidePaused;
        return stateCheckController.IsAwaitingFirstFaultInteraction
            ? SpawnPacing.AwaitingFirstInteraction
            : SpawnPacing.Normal;
    }

    float GetSpawnInterval(SpawnPacing pacing)
    {
        if (pacing == SpawnPacing.GuidePaused)
            return float.PositiveInfinity;
        float interval = EvaluateInterval(Mathf.Max(0f, Time.realtimeSinceStartup - effectStartedAt));
        return pacing == SpawnPacing.AwaitingFirstInteraction
            ? Mathf.Max(interval, firstInteractionInterval)
            : interval;
    }

    float EvaluateInterval(float elapsed)
    {
        float duration = Mathf.Max(0.01f, accelerationDuration);
        float progress = Mathf.Clamp01(elapsed / duration);
        return Mathf.Lerp(
            Mathf.Max(0.01f, initialInterval),
            Mathf.Max(0.01f, finalInterval),
            progress);
    }

    OSWindowUnit FindAvailableWindow()
    {
        for (int i = 0; i < poolWindows.Count; i++)
        {
            OSWindowUnit window = poolWindows[i];
            if (window != null && !window.IsOpen)
                return window;
        }

        return null;
    }

    OSWindowUnit ReclaimOldestWindow()
    {
        while (activationOrder.First != null)
        {
            OSWindowUnit oldest = activationOrder.First.Value;
            activationOrder.RemoveFirst();
            if (oldest == null || !oldest.IsOpen)
                continue;

            oldest.Close();
            return oldest;
        }

        for (int i = 0; i < poolWindows.Count; i++)
        {
            OSWindowUnit window = poolWindows[i];
            if (window == null)
                continue;

            window.Close();
            return window;
        }

        return null;
    }

    void RemoveClosedWindowsFromOrder()
    {
        LinkedListNode<OSWindowUnit> node = activationOrder.First;
        while (node != null)
        {
            LinkedListNode<OSWindowUnit> next = node.Next;
            if (node.Value == null || !node.Value.IsOpen)
                activationOrder.Remove(node);
            node = next;
        }
    }

    bool TryPlaceWindow(OSWindowUnit window)
    {
        RectTransform windowRect = window != null
            ? window.transform as RectTransform
            : null;
        if (poolRect == null || windowRect == null)
            return false;

        Vector2 scale = new Vector2(
            Mathf.Abs(windowRect.localScale.x),
            Mathf.Abs(windowRect.localScale.y));
        Vector2 size = Vector2.Scale(window.RecordedSize, scale);
        Vector2 pivot = windowRect.pivot;
        Rect bounds = poolRect.rect;

        float minX = bounds.xMin + size.x * pivot.x;
        float maxX = bounds.xMax - size.x * (1f - pivot.x);
        float minY = bounds.yMin + size.y * pivot.y;
        float maxY = bounds.yMax - size.y * (1f - pivot.y);
        if (minX > maxX || minY > maxY)
            return false;

        Vector2 position;
        if (!TryGetProtectedRect(out Rect protectedRect))
        {
            position = RandomPosition(minX, maxX, minY, maxY);
        }
        else
        {
            BuildSpawnAreas(
                minX,
                maxX,
                minY,
                maxY,
                size,
                pivot,
                protectedRect);
            if (!TryChooseSpawnPosition(out position))
                return false;

            Rect candidateRect = Rect.MinMaxRect(
                position.x - size.x * pivot.x,
                position.y - size.y * pivot.y,
                position.x + size.x * (1f - pivot.x),
                position.y + size.y * (1f - pivot.y));
            if (candidateRect.Overlaps(protectedRect))
                return false;
        }

        Vector3 localPosition = windowRect.localPosition;
        localPosition.x = position.x;
        localPosition.y = position.y;
        localPosition.z = 0f;
        windowRect.localPosition = localPosition;
        return true;
    }

    bool TryGetProtectedRect(out Rect protectedRect)
    {
        protectedRect = default;
        if (TryGetRectInPool(protectedCharacterRect, out protectedRect))
            return true;

        bool hasFileRect = TryGetRectInPool(
            protectedFileDesktopProcessRect,
            out Rect fileRect);
        bool hasStateCheckRect = TryGetRectInPool(
            protectedStateCheckDesktopProcessRect,
            out Rect stateCheckRect);
        if (!hasFileRect)
        {
            protectedRect = stateCheckRect;
            return hasStateCheckRect;
        }

        if (!hasStateCheckRect)
        {
            protectedRect = fileRect;
            return true;
        }

        protectedRect = Rect.MinMaxRect(
            Mathf.Min(fileRect.xMin, stateCheckRect.xMin),
            Mathf.Min(fileRect.yMin, stateCheckRect.yMin),
            Mathf.Max(fileRect.xMax, stateCheckRect.xMax),
            Mathf.Max(fileRect.yMax, stateCheckRect.yMax));
        return true;
    }

    bool TryGetRectInPool(RectTransform target, out Rect targetRect)
    {
        targetRect = default;
        if (poolRect == null
            || target == null
            || !target.gameObject.activeInHierarchy)
        {
            return false;
        }

        target.GetWorldCorners(worldCorners);
        Vector3 first = poolRect.InverseTransformPoint(worldCorners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;
        for (int i = 1; i < worldCorners.Length; i++)
        {
            Vector3 point = poolRect.InverseTransformPoint(worldCorners[i]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        targetRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return targetRect.width > 0f && targetRect.height > 0f;
    }

    void BuildSpawnAreas(
        float minX,
        float maxX,
        float minY,
        float maxY,
        Vector2 size,
        Vector2 pivot,
        Rect protectedRect)
    {
        spawnAreas.Clear();

        float leftLimit = protectedRect.xMin
            - size.x * (1f - pivot.x);
        float rightLimit = protectedRect.xMax + size.x * pivot.x;
        float bottomLimit = protectedRect.yMin
            - size.y * (1f - pivot.y);
        float topLimit = protectedRect.yMax + size.y * pivot.y;

        AddSpawnArea(minX, Mathf.Min(maxX, leftLimit), minY, maxY);
        AddSpawnArea(Mathf.Max(minX, rightLimit), maxX, minY, maxY);

        float middleMinX = Mathf.Max(minX, leftLimit);
        float middleMaxX = Mathf.Min(maxX, rightLimit);
        AddSpawnArea(
            middleMinX,
            middleMaxX,
            minY,
            Mathf.Min(maxY, bottomLimit));
        AddSpawnArea(
            middleMinX,
            middleMaxX,
            Mathf.Max(minY, topLimit),
            maxY);
    }

    void AddSpawnArea(float minX, float maxX, float minY, float maxY)
    {
        if (maxX <= minX || maxY <= minY)
            return;

        spawnAreas.Add(new SpawnArea(minX, maxX, minY, maxY));
    }

    bool TryChooseSpawnPosition(out Vector2 position)
    {
        position = default;
        float totalArea = 0f;
        for (int i = 0; i < spawnAreas.Count; i++)
            totalArea += spawnAreas[i].Area;

        if (totalArea <= 0f)
            return false;

        float sample = Random.value * totalArea;
        for (int i = 0; i < spawnAreas.Count; i++)
        {
            SpawnArea area = spawnAreas[i];
            if (sample <= area.Area || i == spawnAreas.Count - 1)
            {
                position = RandomPosition(
                    area.MinX,
                    area.MaxX,
                    area.MinY,
                    area.MaxY);
                return true;
            }

            sample -= area.Area;
        }

        return false;
    }

    static Vector2 RandomPosition(
        float minX,
        float maxX,
        float minY,
        float maxY)
    {
        return new Vector2(
            Random.Range(minX, maxX),
            Random.Range(minY, maxY));
    }

    void ResolveReferences()
    {
        if (poolRect == null)
            poolRect = transform as RectTransform;
        if (osHandle == null)
            osHandle = GetComponentInParent<OS_Handle>(true);
        if (stateCheckController == null && osHandle != null)
            stateCheckController = osHandle.StateCheckWindowController;

        if (protectedCharacterRect == null)
        {
            if (osHandle != null)
            {
                protectedCharacterRect = osHandle.transform.Find(
                    "Windows/WindowUnit_StateCheck/ErrorPart/Character")
                    as RectTransform;
            }
        }
        if (stateCheckController == null && protectedCharacterRect != null)
            stateCheckController = protectedCharacterRect.GetComponentInParent<OSStateCheckWindowController>(true);

        if (protectedFileDesktopProcessRect != null
            && protectedStateCheckDesktopProcessRect != null)
        {
            return;
        }

        Transform desktopRoot = osHandle != null
            ? osHandle.transform.Find("DesktopProcessGroup")
            : null;
        if (desktopRoot == null)
            return;

        if (protectedFileDesktopProcessRect == null)
        {
            protectedFileDesktopProcessRect = desktopRoot.Find(
                "DesktopProcessUnit_File") as RectTransform;
        }

        if (protectedStateCheckDesktopProcessRect == null)
        {
            protectedStateCheckDesktopProcessRect = desktopRoot.Find(
                "DesktopProcessUnit_StateCheck") as RectTransform;
            if (protectedStateCheckDesktopProcessRect == null)
            {
                protectedStateCheckDesktopProcessRect = desktopRoot.Find(
                    "DesktopProcessUnit_Fix") as RectTransform;
            }
        }
    }

    void RebuildWindowCache()
    {
        poolWindows.Clear();
        int maxCount = Mathf.Max(1, capacity);
        for (int i = 0; i < transform.childCount; i++)
        {
            OSWindowUnit window = transform.GetChild(i)
                .GetComponent<OSWindowUnit>();
            if (window == null)
                continue;

            poolWindows.Add(window);
            if (poolWindows.Count >= maxCount)
                break;
        }

        warnedMissingWindows = false;
    }

    void CloseAllWindows()
    {
        for (int i = 0; i < poolWindows.Count; i++)
        {
            OSWindowUnit window = poolWindows[i];
            if (window == null)
                continue;

            if (window.IsOpen)
                window.Close();
            else
                window.PrepareRuntimeClosed();
        }
    }

    void StopSpawnRoutine()
    {
        if (spawnRoutine == null)
            return;

        StopCoroutine(spawnRoutine);
        spawnRoutine = null;
    }

    void WarnMissingWindows()
    {
        if (warnedMissingWindows)
            return;

        warnedMissingWindows = true;
        Debug.LogWarning(
            "[OS ErrorPool] No pooled OSWindowUnit children were found.",
            this);
    }

    void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        initialInterval = Mathf.Max(0.01f, initialInterval);
        finalInterval = Mathf.Clamp(finalInterval, 0.01f, initialInterval);
        accelerationDuration = Mathf.Max(0.01f, accelerationDuration);
        firstInteractionInterval = Mathf.Max(0.1f, firstInteractionInterval);
        ResolveReferences();
    }

    void Reset()
    {
        ResolveReferences();
    }

    readonly struct SpawnArea
    {
        public SpawnArea(
            float minX,
            float maxX,
            float minY,
            float maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public float MinX { get; }
        public float MaxX { get; }
        public float MinY { get; }
        public float MaxY { get; }
        public float Area => (MaxX - MinX) * (MaxY - MinY);
    }
}
