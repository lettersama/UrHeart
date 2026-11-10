using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// Presentation only. The caller owns resource payment and narrative progression.
[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSUIEnergySpendEffect : MonoBehaviour
{
    // External captions reserve the battery's footprint, including its 1.25x exit pulse.
    public const float OutsideRightSpace = 34f;
    const float OutsideRightOffset = 18f;

    [SerializeField] Sprite batteryFrame;
    [SerializeField] RectTransform energySource;
    [SerializeField] RectTransform windowsRoot;
    [SerializeField, Min(0.1f)] float flightDuration = 0.48f;
    [SerializeField, Min(0.05f)] float emissionDuration = 0.3f;
    [SerializeField, Range(4, 20)] int particleCount = 12;
    [SerializeField] Color energyColor = new Color32(0x44, 0x96, 0xB7, 0xFF);
    [SerializeField, Min(0.01f)] float expandDuration = 0.12f;
    [SerializeField, Min(0.01f)] float disappearDuration = 0.2f;

    readonly OSUIEnergyRoute router = new OSUIEnergyRoute();
    readonly List<Vector2> route = new List<Vector2>(20);
    readonly List<Vector2> flightPath = new List<Vector2>(192);
    readonly List<Vector2> curveSamples = new List<Vector2>(48);
    readonly List<Rect> obstacles = new List<Rect>(32);
    readonly List<Rect> previousObstacles = new List<Rect>(32);
    readonly List<Rect> sourceCovers = new List<Rect>(32);
    readonly List<Rect> previousSourceCovers = new List<Rect>(32);
    readonly List<OSWindowUnit> visibleWindows = new List<OSWindowUnit>(32);
    readonly List<UnityEngine.UI.Image> particles = new List<UnityEngine.UI.Image>(20);
    readonly Vector3[] corners = new Vector3[4];
    OSWindowUnit window;
    RectTransform space;
    RectTransform battery;
    RectTransform fill;
    CanvasGroup batteryGroup;
    RectTransform viewport;
    Sequence exitSequence;
    float elapsed;
    float routeLength;
    bool charging;
    bool hasRouteSnapshot;
    Vector2 previousSource;
    Vector2 previousDestination;
    Rect previousViewport;
    Vector2 batteryPosition;

    public bool IsCharging => charging;
    public bool IsPlaying => charging || exitSequence != null;
    public float TotalDuration => flightDuration + emissionDuration + expandDuration + disappearDuration;
    public float FillProgress { get; private set; }
    public IReadOnlyList<Vector2> Route => route;

    void Awake() => ResolveReferences();

    public void Configure(Sprite frame, RectTransform source, RectTransform windowStack)
    {
        batteryFrame = frame;
        energySource = source;
        windowsRoot = windowStack;
    }

    void ResolveReferences()
    {
        if (window == null)
            window = GetComponent<OSWindowUnit>();
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        if (viewport == null)
            viewport = canvas.rootCanvas.transform as RectTransform;
        if (energySource == null)
            energySource = viewport.Find("HeartBeatPanel/State/Energe/ShowPanel") as RectTransform;
        if (windowsRoot == null)
            windowsRoot = viewport.Find("Windows") as RectTransform;
    }

    public bool Play(RectTransform optionRect, bool outsideRight = false,
        TMPro.TMP_Text textAnchor = null)
    {
        ResolveReferences();
        if (!isActiveAndEnabled || optionRect == null || batteryFrame == null
            || viewport == null || energySource == null || windowsRoot == null)
            return false;
        Cancel();
        EnsureVisuals();
        elapsed = 0f;
        charging = true;
        hasRouteSnapshot = false;
        space.gameObject.SetActive(true);
        space.SetAsLastSibling();
        battery.localScale = Vector3.one;
        batteryGroup.alpha = 1f;
        // Snapshot the row before the caller immediately advances/rebinds its options.
        Rect row = Bounds(optionRect);
        if (textAnchor != null && TryGetLastVisibleLineBounds(textAnchor, out Rect textBounds))
            row = textBounds;
        Rect ownerBounds = Bounds(window.WindowRect);
        batteryPosition = new Vector2(
            Mathf.Round(Mathf.Clamp(row.xMax + (outsideRight ? OutsideRightOffset : -17f),
                ownerBounds.xMin + 15f, ownerBounds.xMax - 15f)),
            Mathf.Round(Mathf.Clamp(row.center.y, ownerBounds.yMin + 8f, ownerBounds.yMax - 8f)));
        battery.anchoredPosition = batteryPosition - space.rect.center;
        SetFill(0f);
        RefreshPositionAndRoute();
        RenderParticles();
        return true;
    }

    bool TryGetLastVisibleLineBounds(TMPro.TMP_Text label, out Rect bounds)
    {
        bounds = default;
        label.ForceMeshUpdate();
        var info = label.textInfo;
        int lastLine = -1;
        for (int i = info.characterCount - 1; i >= 0; i--)
        {
            if (!info.characterInfo[i].isVisible) continue;
            lastLine = info.characterInfo[i].lineNumber;
            break;
        }
        if (lastLine < 0) return false;

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (!character.isVisible || character.lineNumber != lastLine) continue;
            // Read rendered vertices so inline resource icons retain their adjusted
            // vertical alignment. The label Rect itself may span the entire window.
            var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
            for (int v = 0; v < 4; v++)
            {
                Vector2 point = space.InverseTransformPoint(
                    label.transform.TransformPoint(vertices[character.vertexIndex + v]));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    // Cancelling only affects presentation; payment and progression already happened.
    public void Cancel()
    {
        charging = false;
        if (exitSequence != null)
        {
            exitSequence.Kill();
            exitSequence = null;
        }
        if (space != null)
            space.gameObject.SetActive(false);
    }

    void OnDisable() => Cancel();

    void LateUpdate()
    {
        if (!IsPlaying)
            return;
        if (window == null || !window.IsOpen || window.IsMinimized || window.IsClosing)
        {
            Cancel();
            return;
        }
        if (!charging)
            return;
        RefreshPositionAndRoute();
        elapsed += Time.unscaledDeltaTime;
        RenderParticles();
        if (elapsed >= flightDuration + emissionDuration)
            FinishCharging();
    }

    void FinishCharging()
    {
        if (!charging)
            return;
        charging = false;
        SetFill(1f);
        for (int i = 0; i < particles.Count; i++)
            particles[i].enabled = false;
        exitSequence = DOTween.Sequence().SetUpdate(true);
        exitSequence.Append(battery.DOScale(1.25f, expandDuration).SetEase(Ease.OutQuad));
        exitSequence.Append(battery.DOScale(0.3f, disappearDuration).SetEase(Ease.InQuad));
        exitSequence.Join(batteryGroup.DOFade(0f, disappearDuration).SetEase(Ease.InQuad));
        exitSequence.OnComplete(() =>
        {
            exitSequence = null;
            if (space != null)
                space.gameObject.SetActive(false);
        });
    }

    void EnsureVisuals()
    {
        if (space != null)
            return;
        space = CreateRect("EnergySpendEffect", transform);
        space.anchorMin = Vector2.zero;
        space.anchorMax = Vector2.one;
        space.offsetMin = space.offsetMax = Vector2.zero;
        var group = space.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        for (int i = 0; i < particleCount; i++)
        {
            var particle = CreateImage("Pixel_" + i, space);
            particle.rectTransform.sizeDelta = Vector2.one * (i % 3 == 0 ? 3f : 1.5f);
            particle.color = energyColor;
            particle.enabled = false;
            particles.Add(particle);
        }
        battery = CreateRect("Battery", space);
        battery.sizeDelta = new Vector2(23f, 10f);
        batteryGroup = battery.gameObject.AddComponent<CanvasGroup>();
        var fillImage = CreateImage("Fill", battery);
        fillImage.color = energyColor;
        fill = fillImage.rectTransform;
        fill.anchorMin = fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = new Vector2(2f, 0f);
        fill.sizeDelta = new Vector2(0f, 6f);
        var frame = CreateImage("Frame", battery);
        frame.sprite = batteryFrame;
        frame.rectTransform.sizeDelta = battery.sizeDelta;
    }

    RectTransform CreateRect(string objectName, Transform parent)
    {
        var obj = new GameObject(objectName, typeof(RectTransform));
        obj.layer = gameObject.layer;
        var rect = (RectTransform)obj.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }

    UnityEngine.UI.Image CreateImage(string objectName, Transform parent)
    {
        var image = CreateRect(objectName, parent).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        return image;
    }

    void SetFill(float progress)
    {
        FillProgress = Mathf.Clamp01(progress);
        fill.sizeDelta = new Vector2(Mathf.Round(18f * FillProgress), 6f);
    }

    void RefreshPositionAndRoute()
    {
        Vector2 source = space.InverseTransformPoint(energySource.TransformPoint(energySource.rect.center));
        Vector2 destination = batteryPosition + new Vector2(-9f, 0f);
        Rect screen = Bounds(viewport);
        // Leave enough room for rounded 1.5-3 pixel blocks and their small drift.
        screen = Rect.MinMaxRect(screen.xMin + 3f, screen.yMin + 3f, screen.xMax - 3f, screen.yMax - 3f);
        obstacles.Clear();
        sourceCovers.Clear();
        Rect ownerBounds = Bounds(window.WindowRect);
        // Include picture/subwindows: their visible extent can protrude beyond
        // their parent, and must count when finding the combined exposed edge.
        windowsRoot.GetComponentsInChildren<OSWindowUnit>(false, visibleWindows);
        for (int i = 0; i < visibleWindows.Count; i++)
        {
            var other = visibleWindows[i];
            if (!other.IsOpen || other.IsMinimized || !other.gameObject.activeInHierarchy)
                continue;
            Rect r = Bounds(other.WindowRect);
            r = Rect.MinMaxRect(r.xMin - 3f, r.yMin - 3f, r.xMax + 3f, r.yMax + 3f);
            sourceCovers.Add(r);
            if (other == window) continue;
            if (!IsDrawnAbove(other.transform, window.transform))
                OSUIEnergyRoute.AddOutsideOwner(r, ownerBounds, obstacles);
            else
                obstacles.Add(r);
        }
        bool changed = !hasRouteSnapshot || source != previousSource || destination != previousDestination
            || screen != previousViewport
            || obstacles.Count != previousObstacles.Count
            || sourceCovers.Count != previousSourceCovers.Count;
        if (!changed)
            for (int i = 0; i < obstacles.Count; i++)
                if (obstacles[i] != previousObstacles[i]) { changed = true; break; }
        if (!changed)
            for (int i = 0; i < sourceCovers.Count; i++)
                if (sourceCovers[i] != previousSourceCovers[i]) { changed = true; break; }
        if (!changed)
            return;
        previousSource = source;
        previousDestination = destination;
        previousViewport = screen;
        previousObstacles.Clear();
        previousObstacles.AddRange(obstacles);
        previousSourceCovers.Clear();
        previousSourceCovers.AddRange(sourceCovers);
        hasRouteSnapshot = true;
        router.Build(source, destination, screen, obstacles, route, sourceCovers);
        BuildFlightPath(screen);
        routeLength = 0f;
        for (int i = 1; i < flightPath.Count; i++)
            routeLength += Vector2.Distance(flightPath[i - 1], flightPath[i]);
    }

    void BuildFlightPath(Rect screen)
    {
        flightPath.Clear();
        if (route.Count < 2) return;
        flightPath.Add(route[0]);
        for (int i = 1; i < route.Count; i++)
        {
            Vector2 corner = route[i];
            if (i + 1 < route.Count)
            {
                Vector2 incoming = route[i - 1] - corner;
                Vector2 outgoing = route[i + 1] - corner;
                float radius = Mathf.Min(10f, Mathf.Min(incoming.magnitude, outgoing.magnitude) * .25f);
                Vector2 before = corner + incoming.normalized * radius;
                Vector2 after = corner + outgoing.normalized * radius;
                // Round a turn only where the resulting arc still clears the window.
                if (radius > .5f && TryCurve(before, corner, after, screen, false))
                {
                    AppendFlightArc(before, screen);
                    TryCurve(before, corner, after, screen, true);
                    continue;
                }
            }
            AppendFlightArc(corner, screen);
        }
    }

    void AppendFlightArc(Vector2 end, Rect screen)
    {
        Vector2 start = flightPath[flightPath.Count - 1];
        Vector2 delta = end - start;
        if (delta.sqrMagnitude < .01f) return;
        Vector2 normal = new Vector2(-delta.y, delta.x).normalized;
        if (normal.y < 0f) normal = -normal;
        float bend = Mathf.Min(24f, delta.magnitude * .22f);
        // Prefer a light upward bow. Try the other side or a shallower curve in
        // tight gaps; the geometric route remains the final safe fallback.
        for (int attempt = 0; attempt < 4; attempt++, bend *= .5f)
            for (int side = 0; side < 2; side++)
            {
                Vector2 control = (start + end) * .5f + normal * bend * (side == 0 ? 1f : -1f);
                if (TryCurve(start, control, end, screen, true)) return;
            }
        flightPath.Add(end);
    }

    bool TryCurve(Vector2 start, Vector2 control, Vector2 end, Rect screen, bool append)
    {
        curveSamples.Clear();
        int steps = Mathf.Clamp(Mathf.CeilToInt((Vector2.Distance(start, control)
            + Vector2.Distance(control, end)) / 4f), 6, 48);
        Vector2 previous = start;
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector2 point = (1f - t) * (1f - t) * start
                + 2f * (1f - t) * t * control + t * t * end;
            if (!screen.Contains(point) || !OSUIEnergyRoute.IsSegmentClear(previous, point, obstacles))
                return false;
            curveSamples.Add(point);
            previous = point;
        }
        if (append) flightPath.AddRange(curveSamples);
        return true;
    }

    static bool IsDrawnAbove(Transform other, Transform owner)
    {
        // The effect is the owner's last child, above its content and descendants.
        if (other.IsChildOf(owner) || owner.IsChildOf(other)) return false;
        Transform common = owner.parent;
        while (common != null && !other.IsChildOf(common)) common = common.parent;
        if (common == null) return false;
        while (other.parent != common) other = other.parent;
        while (owner.parent != common) owner = owner.parent;
        return other.GetSiblingIndex() > owner.GetSiblingIndex();
    }

    Rect Bounds(RectTransform rect)
    {
        rect.GetWorldCorners(corners);
        Vector2 min = space.InverseTransformPoint(corners[0]);
        Vector2 max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 p = space.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    void RenderParticles()
    {
        int arrived = 0;
        for (int i = 0; i < particles.Count; i++)
        {
            float delay = emissionDuration * i / Mathf.Max(1, particles.Count - 1);
            float t = (elapsed - delay) / flightDuration;
            if (t >= 1f) arrived++;
            bool visible = t >= 0f && t < 1f && flightPath.Count >= 2;
            particles[i].enabled = visible;
            if (!visible) continue;
            Vector2 p = SampleRoute(Mathf.SmoothStep(0f, 1f, t));
            float drift = Mathf.Sin(t * Mathf.PI) * (i % 3 - 1);
            p += new Vector2(drift, -drift);
            particles[i].rectTransform.anchoredPosition = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y)) - space.rect.center;
        }
        SetFill((float)arrived / Mathf.Max(1, particles.Count));
    }

    Vector2 SampleRoute(float progress)
    {
        float remaining = routeLength * progress;
        for (int i = 1; i < flightPath.Count; i++)
        {
            float length = Vector2.Distance(flightPath[i - 1], flightPath[i]);
            if (remaining <= length)
                return Vector2.Lerp(flightPath[i - 1], flightPath[i], length > 0f ? remaining / length : 1f);
            remaining -= length;
        }
        return flightPath[flightPath.Count - 1];
    }
}
