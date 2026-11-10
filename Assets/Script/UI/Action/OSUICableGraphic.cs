using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum OSUICableRenderSection
{
    Full = 0,
    FixedPointToLayerSplit = 1,
    LayerSplitToMovingPoint = 2
}

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OSUICableGraphic : MaskableGraphic
{
    const float MinimumStep = 1f / 240f;
    const int MaximumConcurrentPulses = 8;
    const float LayerRouteSagDepthPixels = 1f;

    [Header("Endpoints")]
    [SerializeField] RectTransform startPoint;
    [SerializeField] RectTransform endPoint;

    [Header("Cable")]
    [SerializeField, Min(1f)] float maximumLength = 230f;
    [SerializeField, Min(1f)] float lineWidth = 1f;
    [SerializeField, Min(0)] int startLeadPixels;
    [SerializeField] bool fixedLength;
    [SerializeField, Min(0)] int hollowCoreWidth;

    [Header("Reference Pixel Density")]
    [SerializeField] bool deriveSegmentsFromReferencePixels = true;
    [SerializeField, Min(0.5f)] float referencePixelsPerParticle = 8f;
    [SerializeField, Range(3, 256)] int segmentCount = 18;
    [SerializeField, Min(0.25f)] float renderSampleSpacing = 1f;
    [SerializeField, Range(16, 2048)] int maximumRenderSegments = 1024;

    [Header("Simulation")]
    [SerializeField] Vector2 gravity = new Vector2(0f, -240f);
    [SerializeField, Range(0f, 1f)] float damping = 0.95f;
    [SerializeField, Range(1, 24)] int constraintIterations = 16;
    [SerializeField, Range(30f, 240f)] float simulationRate = 90f;

    Vector2[] positions;
    Vector2[] previousPositions;
    readonly List<Vector2> renderPositions = new List<Vector2>(256);
    readonly List<Vector2Int> pixelPositions = new List<Vector2Int>(256);
    readonly HashSet<Vector2Int> pixelLookup = new HashSet<Vector2Int>();
    readonly List<RectTransform> avoidanceRects =
        new List<RectTransform>(12);
    readonly List<Rect> avoidanceBounds = new List<Rect>(12);
    readonly List<Vector2> visibilityNodes = new List<Vector2>(50);
    readonly List<Vector2> routePoints = new List<Vector2>(16);
    readonly List<Vector2> startEscapeCandidates =
        new List<Vector2>(16);
    readonly List<Vector3> reparentPositionBuffer =
        new List<Vector3>(257);
    readonly List<Vector3> reparentPreviousPositionBuffer =
        new List<Vector3>(257);
    readonly Vector3[] rectWorldCorners = new Vector3[4];
    readonly List<CableColorPulse> colorPulses =
        new List<CableColorPulse>(4);
    float[] visibilityDistances;
    int[] visibilityPrevious;
    bool[] visibilityVisited;
    bool[] visibilityNodeAvailable;
    Vector2Int renderStartPixel;
    Vector2Int simulationStartPixel;
    float avoidancePadding;
    float simulationAccumulator;
    bool simulationReady;
    bool hasStartLead;
    bool avoidanceRouteClipsStart;
    bool renderDataDirty = true;

    OSUICableGraphic renderSource;
    OSUICableRenderSection renderSection =
        OSUICableRenderSection.Full;
    RectTransform layerSplitSpace;
    float layerSplitY;
    bool hasLayerSplit;

    public float MaximumLength => Mathf.Max(1f, maximumLength);
    public float MotionRetention => damping;
    public int StartLeadPixels => Mathf.Max(0, startLeadPixels);
    public int ActiveColorPulseCount => colorPulses.Count;

    public bool IsStretched
    {
        get
        {
            return TryGetEndpointPositions(out Vector2 start, out Vector2 end)
                   && Vector2.Distance(start, end) > MaximumLength;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        renderDataDirty = true;
        if (renderSource == null)
            ResetSimulation();
        else
            SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        simulationAccumulator = 0f;
        simulationReady = false;
        hasStartLead = false;
        avoidanceRouteClipsStart = false;
        renderDataDirty = true;
        ClearColorPulses(false);
        base.OnDisable();
    }

    void LateUpdate()
    {
        if (renderSource != null)
        {
            SetVerticesDirty();
            return;
        }

        if (!TryGetEndpointPositions(out Vector2 start, out Vector2 end))
        {
            simulationReady = false;
            renderDataDirty = true;
            SetVerticesDirty();
            return;
        }

        if (!Application.isPlaying || fixedLength)
        {
            // Fixed cables use their equilibrium curve, so moving an endpoint
            // cannot stretch the rope or introduce spring-like length changes.
            if (fixedLength && simulationReady && positions != null
                && positions[0] == start && positions[positions.Length - 1] == end)
                return;
            BuildPreview(start, end);
            simulationReady = true;
            renderDataDirty = true;
            EnsureRenderData();
            SetVerticesDirty();
            return;
        }

        EnsureSimulation(start, end);
        float step = Mathf.Max(MinimumStep, 1f / simulationRate);
        simulationAccumulator += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        simulationAccumulator = Mathf.Min(simulationAccumulator, step * 4f);

        while (simulationAccumulator >= step)
        {
            SimulateStep(start, end, step);
            simulationAccumulator -= step;
        }

        positions[0] = start;
        positions[positions.Length - 1] = end;
        renderDataDirty = true;
        EnsureRenderData();
        AdvanceColorPulses(Time.unscaledDeltaTime);
        SetVerticesDirty();
    }

    public void Configure(
        RectTransform start,
        RectTransform end,
        float cableMaximumLength)
    {
        Configure(start, end, cableMaximumLength, 0);
    }

    public void Configure(
        RectTransform start,
        RectTransform end,
        float cableMaximumLength,
        int cableStartLeadPixels)
    {
        fixedLength = false;
        hollowCoreWidth = 0;
        renderSource = null;
        renderSection = OSUICableRenderSection.Full;
        layerSplitSpace = null;
        hasLayerSplit = false;
        startPoint = start;
        endPoint = end;
        maximumLength = Mathf.Max(1f, cableMaximumLength);
        startLeadPixels = Mathf.Max(0, cableStartLeadPixels);
        ClearColorPulses(false);
        ResetSimulation();
    }

    public void ConfigureFixedLength(RectTransform start, RectTransform end, float length)
    {
        Configure(start, end, length);
        fixedLength = true;
        lineWidth = 4f;
        hollowCoreWidth = 2;
        ResetSimulation();
    }

    public void ConfigureRenderFollower(
        OSUICableGraphic source,
        OSUICableRenderSection section)
    {
        renderSource = source == this ? null : source;
        renderSection = renderSource != null
            ? section
            : OSUICableRenderSection.Full;
        startPoint = null;
        endPoint = null;
        simulationAccumulator = 0f;
        simulationReady = false;
        hasStartLead = false;
        renderDataDirty = true;
        ClearColorPulses(false);
        SetVerticesDirty();
    }

    public void SetRenderSection(OSUICableRenderSection section)
    {
        if (renderSection == section)
            return;

        renderSection = section;
        SetVerticesDirty();
    }

    public void SetLayerSplit(RectTransform splitSpace, float splitY)
    {
        bool nextHasSplit = splitSpace != null;
        if (layerSplitSpace == splitSpace
            && hasLayerSplit == nextHasSplit
            && Mathf.Approximately(layerSplitY, splitY))
        {
            return;
        }

        layerSplitSpace = splitSpace;
        layerSplitY = splitY;
        hasLayerSplit = nextHasSplit;
        renderDataDirty = true;
        SetVerticesDirty();
    }

    public void ClearLayeredRendering()
    {
        renderSource = null;
        renderSection = OSUICableRenderSection.Full;
        layerSplitSpace = null;
        hasLayerSplit = false;
        renderDataDirty = true;
        SetVerticesDirty();
    }

    public bool PlayColorPulse(
        Color pulseColor,
        int lengthPixels = 6,
        float pixelsPerSecond = 240f,
        bool towardStartPoint = true)
    {
        if (!Application.isPlaying
            || !isActiveAndEnabled
            || startPoint == null
            || endPoint == null)
        {
            return false;
        }

        if (colorPulses.Count >= MaximumConcurrentPulses)
            colorPulses.RemoveAt(0);

        colorPulses.Add(new CableColorPulse(
            pulseColor,
            Mathf.Max(1, lengthPixels),
            Mathf.Max(1f, pixelsPerSecond),
            towardStartPoint));
        SetVerticesDirty();
        return true;
    }

    public void ClearColorPulses()
    {
        ClearColorPulses(true);
    }

    public void SetMotionRetention(float retention)
    {
        damping = Mathf.Clamp01(retention);
    }

    public void SetRenderParent(
        RectTransform parent,
        int siblingIndex = -1)
    {
        if (parent == null || rectTransform == null)
            return;

        if (rectTransform.parent == parent)
        {
            ApplySiblingIndex(siblingIndex);
            return;
        }

        CacheSimulationWorldPositions();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
        RestoreSimulationLocalPositions();
        ApplySiblingIndex(siblingIndex);
        SetVerticesDirty();
    }

    public void SetAvoidanceRects(
        IReadOnlyList<RectTransform> rects,
        float padding)
    {
        avoidanceRects.Clear();
        if (rects != null)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                RectTransform avoidanceRect = rects[i];
                if (avoidanceRect != null
                    && !avoidanceRects.Contains(avoidanceRect))
                {
                    avoidanceRects.Add(avoidanceRect);
                }
            }
        }

        avoidancePadding = Mathf.Max(0f, padding);
        renderDataDirty = true;
        SetVerticesDirty();
    }

    void CacheSimulationWorldPositions()
    {
        reparentPositionBuffer.Clear();
        reparentPreviousPositionBuffer.Clear();
        if (positions != null)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                reparentPositionBuffer.Add(
                    rectTransform.TransformPoint(positions[i]));
            }
        }

        if (previousPositions == null)
            return;

        for (int i = 0; i < previousPositions.Length; i++)
        {
            reparentPreviousPositionBuffer.Add(
                rectTransform.TransformPoint(previousPositions[i]));
        }
    }

    void RestoreSimulationLocalPositions()
    {
        if (positions != null
            && reparentPositionBuffer.Count == positions.Length)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = rectTransform.InverseTransformPoint(
                    reparentPositionBuffer[i]);
            }
        }

        if (previousPositions != null
            && reparentPreviousPositionBuffer.Count
                == previousPositions.Length)
        {
            for (int i = 0; i < previousPositions.Length; i++)
            {
                previousPositions[i] = rectTransform.InverseTransformPoint(
                    reparentPreviousPositionBuffer[i]);
            }
        }

        reparentPositionBuffer.Clear();
        reparentPreviousPositionBuffer.Clear();
    }

    void ApplySiblingIndex(int siblingIndex)
    {
        if (rectTransform.parent == null)
            return;

        if (siblingIndex < 0)
        {
            if (rectTransform.GetSiblingIndex()
                == rectTransform.parent.childCount - 1)
            {
                return;
            }

            rectTransform.SetAsLastSibling();
            return;
        }

        int targetIndex = Mathf.Clamp(
            siblingIndex,
            0,
            rectTransform.parent.childCount - 1);
        if (rectTransform.GetSiblingIndex() == targetIndex)
            return;

        rectTransform.SetSiblingIndex(targetIndex);
    }

    public void ResetSimulation()
    {
        simulationAccumulator = 0f;
        simulationReady = false;
        renderDataDirty = true;

        if (renderSource != null)
        {
            SetVerticesDirty();
            return;
        }

        if (TryGetEndpointPositions(out Vector2 start, out Vector2 end))
        {
            BuildPreview(start, end);
            simulationReady = Application.isPlaying;
        }
        else
        {
            hasStartLead = false;
        }

        SetVerticesDirty();
    }

    void EnsureSimulation(Vector2 start, Vector2 end)
    {
        int pointCount = ResolveSimulationSegmentCount() + 1;
        if (simulationReady
            && positions != null
            && previousPositions != null
            && positions.Length == pointCount
            && previousPositions.Length == pointCount)
        {
            return;
        }

        BuildPreview(start, end);
        simulationReady = true;
    }

    void BuildPreview(Vector2 start, Vector2 end)
    {
        int pointCount = ResolveSimulationSegmentCount() + 1;
        if (positions == null || positions.Length != pointCount)
            positions = new Vector2[pointCount];
        if (previousPositions == null
            || previousPositions.Length != pointCount)
        {
            previousPositions = new Vector2[pointCount];
        }

        if (fixedLength)
        {
            BuildFixedLengthCurve(start, end);
            return;
        }

        float distance = Vector2.Distance(start, end);
        float effectiveLength = Mathf.Max(MaximumLength, distance);
        float sag = Mathf.Sqrt(Mathf.Max(
                0f,
                effectiveLength * effectiveLength - distance * distance))
            * 0.35f;
        Vector2 gravityDirection = gravity.sqrMagnitude > 0.0001f
            ? gravity.normalized
            : Vector2.down;

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float sagProfile = 4f * t * (1f - t);
            Vector2 point = Vector2.Lerp(start, end, t)
                            + gravityDirection * sag * sagProfile;
            positions[i] = point;
            previousPositions[i] = point;
        }

        positions[0] = previousPositions[0] = start;
        positions[pointCount - 1] = previousPositions[pointCount - 1] = end;
    }

    int ResolveSimulationSegmentCount()
    {
        if (fixedLength) return 128;
        if (!deriveSegmentsFromReferencePixels)
            return Mathf.Clamp(segmentCount, 3, 256);

        float particleSpacing = Mathf.Max(
            0.5f,
            referencePixelsPerParticle);
        return Mathf.Clamp(
            Mathf.CeilToInt(MaximumLength / particleSpacing),
            3,
            256);
    }

    void SimulateStep(Vector2 start, Vector2 end, float step)
    {
        int lastIndex = positions.Length - 1;
        Vector2 gravityStep = gravity * (step * step);
        for (int i = 1; i < lastIndex; i++)
        {
            Vector2 current = positions[i];
            Vector2 velocity = (current - previousPositions[i]) * damping;
            previousPositions[i] = current;
            positions[i] = current + velocity + gravityStep;
        }

        positions[0] = start;
        positions[lastIndex] = end;

        // Beyond the configured length the cable becomes taut while the
        // external drag controller returns its endpoint after release.
        float effectiveLength = Mathf.Max(
            MaximumLength,
            Vector2.Distance(start, end));
        float segmentLength = effectiveLength / lastIndex;
        int iterations = Mathf.Clamp(constraintIterations, 1, 24);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            positions[0] = start;
            positions[lastIndex] = end;
            for (int i = 0; i < lastIndex; i++)
                SolveSegmentConstraint(i, segmentLength, lastIndex);
        }

        positions[0] = start;
        positions[lastIndex] = end;
    }

    void SolveSegmentConstraint(
        int index,
        float segmentLength,
        int lastIndex)
    {
        int nextIndex = index + 1;
        Vector2 delta = positions[nextIndex] - positions[index];
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return;

        Vector2 correction = delta * ((distance - segmentLength) / distance);
        if (index == 0)
        {
            positions[nextIndex] -= correction;
        }
        else if (nextIndex == lastIndex)
        {
            positions[index] += correction;
        }
        else
        {
            positions[index] += correction * 0.5f;
            positions[nextIndex] -= correction * 0.5f;
        }
    }

    bool TryGetEndpointPositions(out Vector2 start, out Vector2 end)
    {
        start = default;
        end = default;
        if (startPoint == null || endPoint == null || rectTransform == null)
            return false;

        Vector3 startWorld = startPoint.TransformPoint(Vector3.zero);
        Vector3 endWorld = endPoint.TransformPoint(Vector3.zero);
        Vector2 rawStart = rectTransform.InverseTransformPoint(startWorld);
        renderStartPixel = RoundToReferencePixel(rawStart);
        simulationStartPixel = renderStartPixel
                               + ResolveStartLeadDirection()
                               * StartLeadPixels;
        start = simulationStartPixel;
        end = rectTransform.InverseTransformPoint(endWorld);
        if (fixedLength) end = start + Vector2.ClampMagnitude(end - start, MaximumLength);
        hasStartLead = true;
        return true;
    }

    // Arc-length parametrized catenary. Gravity points down in reference pixels.
    // All endpoint positions share the same authored length, including while held.
    void BuildFixedLengthCurve(Vector2 start, Vector2 end)
    {
        double length = MaximumLength;
        double horizontal = System.Math.Abs(end.x - start.x);
        double vertical = end.y - start.y;
        int last = positions.Length - 1;
        if (Vector2.Distance(start, end) >= length - .001)
        {
            for (int i = 0; i <= last; i++) positions[i] = Vector2.Lerp(start, end, i / (float)last);
        }
        else if (horizontal < .01)
        {
            // The vertical limit folds below the two endpoints, not above them.
            double drop = (length - vertical) * .5;
            for (int i = 0; i <= last; i++)
            {
                double arc = length * i / last;
                positions[i] = new Vector2(Mathf.Lerp(start.x, end.x, i / (float)last),
                    start.y + (float)(arc <= drop ? -arc : arc - 2 * drop));
            }
        }
        else
        {
            double target = System.Math.Sqrt(length * length - vertical * vertical) / horizontal;
            double low = 0, high = 24;
            for (int n = 0; n < 48; n++)
            {
                double u = (low + high) * .5;
                if (System.Math.Sinh(u) / u < target) low = u; else high = u;
            }
            double a = horizontal / (low + high);
            double offset = horizontal * .5 - a * .5 * System.Math.Log((length + vertical) / (length - vertical));
            double initialSlope = System.Math.Sinh(-offset / a);
            double initialHeight = System.Math.Cosh(-offset / a);
            float direction = end.x >= start.x ? 1f : -1f;
            for (int i = 0; i <= last; i++)
            {
                double slope = initialSlope + length * i / last / a;
                double root = System.Math.Sqrt(1 + slope * slope);
                // Stable asinh for negative slopes as well as positive ones.
                double inverse = System.Math.Sign(slope) * System.Math.Log(System.Math.Abs(slope) + root);
                positions[i] = new Vector2(start.x + direction * (float)(offset + a * inverse),
                    start.y + (float)(a * (root - initialHeight)));
            }
        }
        positions[0] = start;
        positions[last] = end;
        System.Array.Copy(positions, previousPositions, positions.Length);
    }

    Vector2Int ResolveStartLeadDirection()
    {
        Vector3 outwardWorld = startPoint.TransformVector(Vector3.left);
        Vector3 outwardLocal = rectTransform.InverseTransformVector(
            outwardWorld);
        if (Mathf.Abs(outwardLocal.x) >= Mathf.Abs(outwardLocal.y))
        {
            return new Vector2Int(outwardLocal.x >= 0f ? 1 : -1, 0);
        }

        return new Vector2Int(0, outwardLocal.y >= 0f ? 1 : -1);
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        OSUICableGraphic dataSource = renderSource != null
            ? renderSource
            : this;
        if (dataSource == null
            || !dataSource.gameObject.activeInHierarchy
            || dataSource.positions == null
            || dataSource.positions.Length < 2)
        {
            return;
        }

        dataSource.EnsureRenderData();
        if (dataSource.pixelPositions.Count == 0)
            return;

        dataSource.ResolveRenderPixelRange(
            renderSection,
            out int firstPixel,
            out int lastPixel);
        if (firstPixel < 0 || lastPixel < firstPixel)
            return;

        if (dataSource.hollowCoreWidth > 0)
        {
            // Whole outer contour first, then the core. Segment-by-segment
            // outlines would paint red seams across the black centre at bends.
            AddCableBand(vertexHelper, dataSource, firstPixel, lastPixel,
                Mathf.RoundToInt(dataSource.lineWidth), dataSource.color);
            AddCableBand(vertexHelper, dataSource, firstPixel, lastPixel,
                dataSource.hollowCoreWidth, new Color(0, 0, 0, dataSource.color.a));
            return;
        }

        float pixelWidth = Mathf.Max(
            1f,
            Mathf.Round(dataSource.lineWidth));
        float halfWidth = pixelWidth * 0.5f;
        Color32 baseVertexColor = dataSource.color;
        int renderedPixel = 0;
        for (int i = firstPixel; i <= lastPixel; i++)
        {
            Vector2 center = TransformSourcePixel(
                dataSource,
                dataSource.pixelPositions[i]);
            Color32 vertexColor = dataSource.ResolvePixelColor(
                i,
                baseVertexColor);
            int vertex = renderedPixel * 4;
            vertexHelper.AddVert(
                center + new Vector2(-halfWidth, -halfWidth),
                vertexColor,
                new Vector2(0f, 0f));
            vertexHelper.AddVert(
                center + new Vector2(-halfWidth, halfWidth),
                vertexColor,
                new Vector2(0f, 1f));
            vertexHelper.AddVert(
                center + new Vector2(halfWidth, halfWidth),
                vertexColor,
                new Vector2(1f, 1f));
            vertexHelper.AddVert(
                center + new Vector2(halfWidth, -halfWidth),
                vertexColor,
                new Vector2(1f, 0f));
            vertexHelper.AddTriangle(vertex, vertex + 1, vertex + 2);
            vertexHelper.AddTriangle(vertex, vertex + 2, vertex + 3);
            renderedPixel++;
        }
    }

    void EnsureRenderData()
    {
        if (renderSource != null)
        {
            renderSource.EnsureRenderData();
            return;
        }

        if (!renderDataDirty)
            return;

        BuildRenderPositions();
        BuildPixelPositions();
        renderDataDirty = false;
    }

    void AddCableBand(VertexHelper mesh, OSUICableGraphic source, int first, int last, int width, Color32 tint)
    {
        for (int i = first; i <= last; i++)
        {
            Vector2 center = TransformSourcePixel(source, source.pixelPositions[i]);
            Vector2 bottom = center - Vector2.one * (width / 2);
            int vertex = mesh.currentVertCount;
            mesh.AddVert(bottom, tint, Vector2.zero);
            mesh.AddVert(bottom + new Vector2(0, width), tint, Vector2.up);
            mesh.AddVert(bottom + Vector2.one * width, tint, Vector2.one);
            mesh.AddVert(bottom + new Vector2(width, 0), tint, Vector2.right);
            mesh.AddTriangle(vertex, vertex + 1, vertex + 2);
            mesh.AddTriangle(vertex, vertex + 2, vertex + 3);
        }
    }

    Vector2 TransformSourcePixel(
        OSUICableGraphic dataSource,
        Vector2 sourcePixel)
    {
        if (dataSource == this)
            return sourcePixel;

        Vector3 worldPoint = dataSource.rectTransform.TransformPoint(
            sourcePixel);
        return RoundToReferencePixel(
            rectTransform.InverseTransformPoint(worldPoint));
    }

    void ResolveRenderPixelRange(
        OSUICableRenderSection section,
        out int firstPixel,
        out int lastPixel)
    {
        firstPixel = 0;
        lastPixel = pixelPositions.Count - 1;
        if (lastPixel < 0
            || section == OSUICableRenderSection.Full
            || !hasLayerSplit
            || layerSplitSpace == null)
        {
            return;
        }

        int splitPixel = ResolveLayerSplitPixel();
        if (section == OSUICableRenderSection.FixedPointToLayerSplit)
            lastPixel = splitPixel;
        else
            firstPixel = splitPixel;
    }

    int ResolveLayerSplitPixel()
    {
        int lastPixel = pixelPositions.Count - 1;
        for (int i = lastPixel; i >= 0; i--)
        {
            Vector2 sourcePixel = pixelPositions[i];
            Vector3 worldPoint = rectTransform.TransformPoint(
                sourcePixel);
            Vector2 splitLocal = layerSplitSpace.InverseTransformPoint(
                worldPoint);
            if (splitLocal.y <= layerSplitY + 0.001f)
                return i;
        }

        return 0;
    }

    void BuildRenderPositions()
    {
        renderPositions.Clear();
        avoidanceRouteClipsStart = false;
        if (positions == null || positions.Length < 2)
            return;

        if (fixedLength)
        {
            renderPositions.AddRange(positions);
            return;
        }

        float approximateLength = 0f;
        for (int i = 0; i < positions.Length - 1; i++)
            approximateLength += Vector2.Distance(positions[i], positions[i + 1]);

        int simulationSegments = positions.Length - 1;
        int renderSegmentLimit = Mathf.Max(
            simulationSegments,
            Mathf.Clamp(maximumRenderSegments, 16, 2048));
        int renderSegments = Mathf.Clamp(
            Mathf.CeilToInt(
                approximateLength / Mathf.Max(0.25f, renderSampleSpacing)),
            simulationSegments,
            renderSegmentLimit);

        for (int i = 0; i <= renderSegments; i++)
        {
            float simulationPosition = i / (float)renderSegments
                                       * simulationSegments;
            int segmentIndex = Mathf.Min(
                Mathf.FloorToInt(simulationPosition),
                simulationSegments - 1);
            float t = i == renderSegments
                ? 1f
                : simulationPosition - segmentIndex;
            renderPositions.Add(EvaluateCatmullRom(segmentIndex, t));
        }

        if (ApplyLayerClearanceSag())
            return;

        if (!BuildAvoidanceBounds()
            || !DoesPathIntersectAvoidance(renderPositions)
            || !TryBuildShortestAvoidanceRoute(
                renderPositions[0],
                renderPositions[renderPositions.Count - 1]))
        {
            return;
        }

        BuildRoutedRenderPositions();
    }

    bool ApplyLayerClearanceSag()
    {
        if (!hasLayerSplit
            || layerSplitSpace == null
            || rectTransform == null
            || renderPositions.Count < 3)
        {
            return false;
        }

        float targetY = layerSplitY - LayerRouteSagDepthPixels;
        float requiredSag = float.PositiveInfinity;
        int lastIndex = renderPositions.Count - 1;
        // sin^2 keeps both endpoint tangents intact while the minimum ratio
        // adds only enough sag for the curve to clear the window stack.
        for (int i = 1; i < lastIndex; i++)
        {
            float profile = ResolveLayerSagProfile(i / (float)lastIndex);
            if (profile <= 0.0001f)
                continue;

            Vector3 worldPoint = rectTransform.TransformPoint(
                renderPositions[i]);
            Vector3 splitLocal =
                layerSplitSpace.InverseTransformPoint(worldPoint);
            if (splitLocal.y <= targetY)
                return true;

            requiredSag = Mathf.Min(
                requiredSag,
                (splitLocal.y - targetY) / profile);
        }

        if (float.IsPositiveInfinity(requiredSag))
            return true;

        requiredSag += 0.01f;
        for (int i = 1; i < lastIndex; i++)
        {
            float profile = ResolveLayerSagProfile(i / (float)lastIndex);
            Vector3 worldPoint = rectTransform.TransformPoint(
                renderPositions[i]);
            Vector3 splitLocal =
                layerSplitSpace.InverseTransformPoint(worldPoint);
            splitLocal.y -= requiredSag * profile;
            renderPositions[i] = rectTransform.InverseTransformPoint(
                layerSplitSpace.TransformPoint(splitLocal));
        }

        return true;
    }

    static float ResolveLayerSagProfile(float progress)
    {
        float sine = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
        return sine * sine;
    }

    bool BuildAvoidanceBounds()
    {
        avoidanceBounds.Clear();
        if (avoidanceRects.Count == 0 || rectTransform == null)
            return false;

        float padding = Mathf.Max(0f, avoidancePadding);
        for (int i = 0; i < avoidanceRects.Count; i++)
        {
            RectTransform avoidanceRect = avoidanceRects[i];
            if (avoidanceRect == null
                || !avoidanceRect.gameObject.activeInHierarchy)
            {
                continue;
            }

            avoidanceRect.GetWorldCorners(rectWorldCorners);
            Vector2 first = rectTransform.InverseTransformPoint(
                rectWorldCorners[0]);
            float xMin = first.x;
            float xMax = first.x;
            float yMin = first.y;
            float yMax = first.y;
            for (int corner = 1; corner < rectWorldCorners.Length; corner++)
            {
                Vector2 localCorner = rectTransform.InverseTransformPoint(
                    rectWorldCorners[corner]);
                xMin = Mathf.Min(xMin, localCorner.x);
                xMax = Mathf.Max(xMax, localCorner.x);
                yMin = Mathf.Min(yMin, localCorner.y);
                yMax = Mathf.Max(yMax, localCorner.y);
            }

            AddVisibleAvoidanceBounds(Rect.MinMaxRect(
                xMin - padding,
                yMin - padding,
                xMax + padding,
                yMax + padding));
        }

        return avoidanceBounds.Count > 0;
    }

    void AddVisibleAvoidanceBounds(Rect bounds)
    {
        Rect activeWindowBounds = rectTransform.rect;
        float activeCoveragePadding = Mathf.Max(2f, avoidancePadding);
        activeWindowBounds = Rect.MinMaxRect(
            activeWindowBounds.xMin - activeCoveragePadding,
            activeWindowBounds.yMin - activeCoveragePadding,
            activeWindowBounds.xMax + activeCoveragePadding,
            activeWindowBounds.yMax + activeCoveragePadding);
        float intersectionXMin = Mathf.Max(
            bounds.xMin,
            activeWindowBounds.xMin);
        float intersectionXMax = Mathf.Min(
            bounds.xMax,
            activeWindowBounds.xMax);
        float intersectionYMin = Mathf.Max(
            bounds.yMin,
            activeWindowBounds.yMin);
        float intersectionYMax = Mathf.Min(
            bounds.yMax,
            activeWindowBounds.yMax);
        if (intersectionXMin >= intersectionXMax
            || intersectionYMin >= intersectionYMax)
        {
            avoidanceBounds.Add(bounds);
            return;
        }

        AddAvoidanceStrip(Rect.MinMaxRect(
            bounds.xMin,
            bounds.yMin,
            intersectionXMin,
            bounds.yMax));
        AddAvoidanceStrip(Rect.MinMaxRect(
            intersectionXMax,
            bounds.yMin,
            bounds.xMax,
            bounds.yMax));
        AddAvoidanceStrip(Rect.MinMaxRect(
            intersectionXMin,
            bounds.yMin,
            intersectionXMax,
            intersectionYMin));
        AddAvoidanceStrip(Rect.MinMaxRect(
            intersectionXMin,
            intersectionYMax,
            intersectionXMax,
            bounds.yMax));
    }

    void AddAvoidanceStrip(Rect bounds)
    {
        if (bounds.width > 0.01f && bounds.height > 0.01f)
            avoidanceBounds.Add(bounds);
    }

    bool DoesPathIntersectAvoidance(IReadOnlyList<Vector2> path)
    {
        if (path == null || path.Count < 2)
            return false;

        for (int i = 0; i < path.Count - 1; i++)
        {
            if (!IsSegmentVisible(path[i], path[i + 1]))
                return true;
        }

        return false;
    }

    bool TryBuildShortestAvoidanceRoute(Vector2 start, Vector2 end)
    {
        Vector2 routeStart = start;
        bool clipsStart = false;
        if (IsPointInsideAvoidanceInterior(start))
        {
            if (!TryResolveVisibleRouteStart(start, end, out routeStart))
                return false;

            clipsStart = true;
        }

        visibilityNodes.Clear();
        visibilityNodes.Add(routeStart);
        visibilityNodes.Add(end);
        for (int i = 0; i < avoidanceBounds.Count; i++)
        {
            Rect bounds = avoidanceBounds[i];
            visibilityNodes.Add(new Vector2(bounds.xMin, bounds.yMin));
            visibilityNodes.Add(new Vector2(bounds.xMin, bounds.yMax));
            visibilityNodes.Add(new Vector2(bounds.xMax, bounds.yMax));
            visibilityNodes.Add(new Vector2(bounds.xMax, bounds.yMin));
        }

        int nodeCount = visibilityNodes.Count;
        EnsureVisibilityCapacity(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            visibilityDistances[i] = float.PositiveInfinity;
            visibilityPrevious[i] = -1;
            visibilityVisited[i] = false;
            visibilityNodeAvailable[i] = i < 2
                || !IsPointInsideAvoidanceInterior(visibilityNodes[i]);
        }

        visibilityDistances[0] = 0f;
        for (int iteration = 0; iteration < nodeCount; iteration++)
        {
            int current = -1;
            float currentDistance = float.PositiveInfinity;
            for (int i = 0; i < nodeCount; i++)
            {
                if (!visibilityVisited[i]
                    && visibilityNodeAvailable[i]
                    && visibilityDistances[i] < currentDistance)
                {
                    current = i;
                    currentDistance = visibilityDistances[i];
                }
            }

            if (current < 0)
                break;

            visibilityVisited[current] = true;
            if (current == 1)
                break;

            Vector2 currentPoint = visibilityNodes[current];
            for (int candidate = 0; candidate < nodeCount; candidate++)
            {
                if (candidate == current
                    || visibilityVisited[candidate]
                    || !visibilityNodeAvailable[candidate]
                    || !IsSegmentVisible(
                        currentPoint,
                        visibilityNodes[candidate]))
                {
                    continue;
                }

                float nextDistance = currentDistance + Vector2.Distance(
                    currentPoint,
                    visibilityNodes[candidate]);
                if (nextDistance >= visibilityDistances[candidate])
                    continue;

                visibilityDistances[candidate] = nextDistance;
                visibilityPrevious[candidate] = current;
            }
        }

        if (float.IsPositiveInfinity(visibilityDistances[1]))
            return false;

        routePoints.Clear();
        int routeNode = 1;
        while (routeNode >= 0)
        {
            routePoints.Add(visibilityNodes[routeNode]);
            if (routeNode == 0)
                break;

            routeNode = visibilityPrevious[routeNode];
        }

        if (routePoints.Count == 0
            || routePoints[routePoints.Count - 1] != routeStart)
        {
            routePoints.Clear();
            return false;
        }

        routePoints.Reverse();
        RemoveRedundantRoutePoints();
        avoidanceRouteClipsStart = clipsStart;
        return routePoints.Count >= 2;
    }

    bool TryResolveVisibleRouteStart(
        Vector2 obscuredStart,
        Vector2 end,
        out Vector2 visibleStart)
    {
        startEscapeCandidates.Clear();
        for (int i = 0; i < avoidanceBounds.Count; i++)
        {
            Rect bounds = avoidanceBounds[i];
            if (obscuredStart.x <= bounds.xMin
                || obscuredStart.x >= bounds.xMax
                || obscuredStart.y <= bounds.yMin
                || obscuredStart.y >= bounds.yMax)
            {
                continue;
            }

            float clampedX = Mathf.Clamp(
                obscuredStart.x,
                bounds.xMin,
                bounds.xMax);
            float clampedY = Mathf.Clamp(
                obscuredStart.y,
                bounds.yMin,
                bounds.yMax);
            startEscapeCandidates.Add(
                new Vector2(bounds.xMin, clampedY));
            startEscapeCandidates.Add(
                new Vector2(bounds.xMax, clampedY));
            startEscapeCandidates.Add(
                new Vector2(clampedX, bounds.yMin));
            startEscapeCandidates.Add(
                new Vector2(clampedX, bounds.yMax));
        }

        visibleStart = default;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < startEscapeCandidates.Count; i++)
        {
            Vector2 candidate = startEscapeCandidates[i];
            if (IsPointInsideAvoidanceInterior(candidate))
                continue;

            float score = Vector2.Distance(obscuredStart, candidate)
                          + Vector2.Distance(candidate, end);
            if (score >= bestScore)
                continue;

            bestScore = score;
            visibleStart = candidate;
        }

        return !float.IsPositiveInfinity(bestScore);
    }

    void BuildRoutedRenderPositions()
    {
        int sectionCount = routePoints.Count - 1;
        if (sectionCount <= 0)
            return;

        float routeLength = 0f;
        for (int i = 0; i < sectionCount; i++)
            routeLength += Vector2.Distance(routePoints[i], routePoints[i + 1]);
        if (routeLength <= 0.0001f)
            return;

        int maximumSegments = Mathf.Clamp(
            maximumRenderSegments,
            16,
            2048);
        int totalSegments = Mathf.Clamp(
            Mathf.CeilToInt(
                routeLength / Mathf.Max(0.25f, renderSampleSpacing)),
            sectionCount,
            maximumSegments);
        float extraLength = Mathf.Max(0f, MaximumLength - routeLength);
        Vector2 gravityDirection = gravity.sqrMagnitude > 0.0001f
            ? gravity.normalized
            : Vector2.down;

        renderPositions.Clear();
        int usedSegments = 0;
        for (int section = 0; section < sectionCount; section++)
        {
            Vector2 sectionStart = routePoints[section];
            Vector2 sectionEnd = routePoints[section + 1];
            float distance = Vector2.Distance(sectionStart, sectionEnd);
            float share = distance / routeLength;
            float sectionLength = distance + extraLength * share;
            float requestedSag = Mathf.Sqrt(Mathf.Max(
                    0f,
                    sectionLength * sectionLength - distance * distance))
                * 0.35f;
            float safeSag = FindMaximumClearSag(
                sectionStart,
                sectionEnd,
                gravityDirection,
                requestedSag);

            int remainingSections = sectionCount - section;
            int remainingSegments = totalSegments - usedSegments;
            int sectionSegments = section == sectionCount - 1
                ? remainingSegments
                : Mathf.Clamp(
                    Mathf.RoundToInt(totalSegments * share),
                    1,
                    remainingSegments - (remainingSections - 1));
            usedSegments += sectionSegments;

            int firstSample = section == 0 ? 0 : 1;
            for (int sample = firstSample; sample <= sectionSegments; sample++)
            {
                float t = sample / (float)sectionSegments;
                float sagProfile = 4f * t * (1f - t);
                renderPositions.Add(Vector2.Lerp(
                        sectionStart,
                        sectionEnd,
                        t)
                    + gravityDirection * safeSag * sagProfile);
            }
        }
    }

    float FindMaximumClearSag(
        Vector2 start,
        Vector2 end,
        Vector2 gravityDirection,
        float requestedSag)
    {
        if (requestedSag <= 0.001f)
            return 0f;
        if (IsSagCurveClear(start, end, gravityDirection, requestedSag))
            return requestedSag;

        float clearSag = 0f;
        float blockedSag = requestedSag;
        for (int i = 0; i < 7; i++)
        {
            float candidateSag = (clearSag + blockedSag) * 0.5f;
            if (IsSagCurveClear(
                    start,
                    end,
                    gravityDirection,
                    candidateSag))
            {
                clearSag = candidateSag;
            }
            else
            {
                blockedSag = candidateSag;
            }
        }

        return clearSag;
    }

    bool IsSagCurveClear(
        Vector2 start,
        Vector2 end,
        Vector2 gravityDirection,
        float sag)
    {
        int samples = Mathf.Clamp(
            Mathf.CeilToInt(Vector2.Distance(start, end) * 2f),
            2,
            1024);
        Vector2 previous = start;
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            float sagProfile = 4f * t * (1f - t);
            Vector2 current = Vector2.Lerp(start, end, t)
                              + gravityDirection * sag * sagProfile;
            if (!IsSegmentVisible(previous, current))
                return false;

            previous = current;
        }

        return true;
    }

    bool IsSegmentVisible(Vector2 start, Vector2 end)
    {
        for (int i = 0; i < avoidanceBounds.Count; i++)
        {
            if (SegmentIntersectsRectInterior(
                    start,
                    end,
                    avoidanceBounds[i]))
            {
                return false;
            }
        }

        return true;
    }

    bool IsPointInsideAvoidanceInterior(Vector2 point)
    {
        const float interiorInset = 0.01f;
        for (int i = 0; i < avoidanceBounds.Count; i++)
        {
            Rect bounds = avoidanceBounds[i];
            if (point.x > bounds.xMin + interiorInset
                && point.x < bounds.xMax - interiorInset
                && point.y > bounds.yMin + interiorInset
                && point.y < bounds.yMax - interiorInset)
            {
                return true;
            }
        }

        return false;
    }

    static bool SegmentIntersectsRectInterior(
        Vector2 start,
        Vector2 end,
        Rect bounds)
    {
        const float interiorInset = 0.01f;
        Rect interior = Rect.MinMaxRect(
            bounds.xMin + interiorInset,
            bounds.yMin + interiorInset,
            bounds.xMax - interiorInset,
            bounds.yMax - interiorInset);
        if (interior.width <= 0f || interior.height <= 0f)
            return false;

        Vector2 delta = end - start;
        float entry = 0f;
        float exit = 1f;
        return ClipLine(-delta.x, start.x - interior.xMin, ref entry, ref exit)
            && ClipLine(delta.x, interior.xMax - start.x, ref entry, ref exit)
            && ClipLine(-delta.y, start.y - interior.yMin, ref entry, ref exit)
            && ClipLine(delta.y, interior.yMax - start.y, ref entry, ref exit);
    }

    static bool ClipLine(
        float direction,
        float distance,
        ref float entry,
        ref float exit)
    {
        if (Mathf.Abs(direction) <= 0.00001f)
            return distance >= 0f;

        float ratio = distance / direction;
        if (direction < 0f)
        {
            if (ratio > exit)
                return false;
            if (ratio > entry)
                entry = ratio;
        }
        else
        {
            if (ratio < entry)
                return false;
            if (ratio < exit)
                exit = ratio;
        }

        return true;
    }

    void EnsureVisibilityCapacity(int count)
    {
        if (visibilityDistances != null
            && visibilityDistances.Length >= count)
        {
            return;
        }

        visibilityDistances = new float[count];
        visibilityPrevious = new int[count];
        visibilityVisited = new bool[count];
        visibilityNodeAvailable = new bool[count];
    }

    void RemoveRedundantRoutePoints()
    {
        for (int i = routePoints.Count - 2; i > 0; i--)
        {
            Vector2 incoming = routePoints[i] - routePoints[i - 1];
            Vector2 outgoing = routePoints[i + 1] - routePoints[i];
            float cross = incoming.x * outgoing.y
                          - incoming.y * outgoing.x;
            if (Mathf.Abs(cross) <= 0.001f
                && Vector2.Dot(incoming, outgoing) >= 0f)
            {
                routePoints.RemoveAt(i);
            }
        }
    }

    void BuildPixelPositions()
    {
        pixelPositions.Clear();
        pixelLookup.Clear();
        if (renderPositions.Count == 0)
            return;

        Vector2Int previous;
        if (hasStartLead && !avoidanceRouteClipsStart)
        {
            previous = renderStartPixel;
            AddPixel(previous);
            RasterizePixelLine(previous, simulationStartPixel);
            previous = simulationStartPixel;
            Vector2Int firstCurvePixel = RoundToReferencePixel(
                renderPositions[0]);
            RasterizePixelLine(previous, firstCurvePixel);
            previous = firstCurvePixel;
        }
        else
        {
            previous = RoundToReferencePixel(renderPositions[0]);
            AddPixel(previous);
        }

        for (int i = 1; i < renderPositions.Count; i++)
        {
            Vector2Int current = RoundToReferencePixel(renderPositions[i]);
            RasterizePixelLine(previous, current);
            previous = current;
        }
    }

    void RasterizePixelLine(Vector2Int start, Vector2Int end)
    {
        int x = start.x;
        int y = start.y;
        int deltaX = Mathf.Abs(end.x - start.x);
        int deltaY = Mathf.Abs(end.y - start.y);
        int stepX = start.x < end.x ? 1 : -1;
        int stepY = start.y < end.y ? 1 : -1;
        int error = deltaX - deltaY;

        while (true)
        {
            AddPixel(new Vector2Int(x, y));
            if (x == end.x && y == end.y)
                break;

            int doubledError = error * 2;
            if (doubledError > -deltaY)
            {
                error -= deltaY;
                x += stepX;
            }

            if (doubledError < deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }
    }

    void AddPixel(Vector2Int pixel)
    {
        if (pixelLookup.Add(pixel))
            pixelPositions.Add(pixel);
    }

    void AdvanceColorPulses(float deltaTime)
    {
        if (colorPulses.Count == 0)
            return;

        float clampedDelta = Mathf.Min(Mathf.Max(0f, deltaTime), 0.05f);
        int pathLengthPixels = Mathf.Max(0, pixelPositions.Count - 1);
        for (int i = colorPulses.Count - 1; i >= 0; i--)
        {
            CableColorPulse pulse = colorPulses[i];
            pulse.TraveledPixels += pulse.PixelsPerSecond * clampedDelta;
            if (pathLengthPixels > 0
                && pulse.TraveledPixels
                > pathLengthPixels + pulse.LengthPixels)
            {
                colorPulses.RemoveAt(i);
            }
        }
    }

    Color32 ResolvePixelColor(
        int pixelIndex,
        Color32 baseVertexColor)
    {
        int lastPixelIndex = pixelPositions.Count - 1;
        for (int i = colorPulses.Count - 1; i >= 0; i--)
        {
            CableColorPulse pulse = colorPulses[i];
            float distanceFromOrigin = pulse.TowardStartPoint
                ? lastPixelIndex - pixelIndex
                : pixelIndex;
            float pulseTail = pulse.TraveledPixels - pulse.LengthPixels;
            if (distanceFromOrigin <= pulse.TraveledPixels
                && distanceFromOrigin > pulseTail)
            {
                return pulse.Color;
            }
        }

        return baseVertexColor;
    }

    void ClearColorPulses(bool markVerticesDirty)
    {
        if (colorPulses.Count == 0)
            return;

        colorPulses.Clear();
        if (markVerticesDirty)
            SetVerticesDirty();
    }

    static Vector2Int RoundToReferencePixel(Vector2 position)
    {
        return new Vector2Int(
            Mathf.RoundToInt(position.x),
            Mathf.RoundToInt(position.y));
    }

    Vector2 EvaluateCatmullRom(int segmentIndex, float t)
    {
        int lastIndex = positions.Length - 1;
        Vector2 p0 = positions[Mathf.Max(0, segmentIndex - 1)];
        Vector2 p1 = positions[segmentIndex];
        Vector2 p2 = positions[Mathf.Min(lastIndex, segmentIndex + 1)];
        Vector2 p3 = positions[Mathf.Min(lastIndex, segmentIndex + 2)];
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    sealed class CableColorPulse
    {
        public readonly Color32 Color;
        public readonly int LengthPixels;
        public readonly float PixelsPerSecond;
        public readonly bool TowardStartPoint;
        public float TraveledPixels;

        public CableColorPulse(
            Color color,
            int lengthPixels,
            float pixelsPerSecond,
            bool towardStartPoint)
        {
            Color = color;
            LengthPixels = lengthPixels;
            PixelsPerSecond = pixelsPerSecond;
            TowardStartPoint = towardStartPoint;
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        maximumLength = Mathf.Max(1f, maximumLength);
        lineWidth = Mathf.Max(1f, Mathf.Round(lineWidth));
        startLeadPixels = Mathf.Max(0, startLeadPixels);
        referencePixelsPerParticle = Mathf.Max(
            0.5f,
            referencePixelsPerParticle);
        segmentCount = Mathf.Clamp(segmentCount, 3, 256);
        renderSampleSpacing = Mathf.Max(0.25f, renderSampleSpacing);
        maximumRenderSegments = Mathf.Clamp(
            maximumRenderSegments,
            16,
            2048);
        damping = Mathf.Clamp01(damping);
        constraintIterations = Mathf.Clamp(constraintIterations, 1, 24);
        simulationRate = Mathf.Clamp(simulationRate, 30f, 240f);
        ResetSimulation();
    }
#endif
}
