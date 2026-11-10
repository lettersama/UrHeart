using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSUIEnergyClaimEffect : MonoBehaviour
{
    [SerializeField] RectTransform space;
    [SerializeField] RectTransform viewport;
    [SerializeField] RectTransform energyTarget;
    [SerializeField] Transform windowsRoot;
    readonly OSUIEnergyRoute router = new OSUIEnergyRoute();
    readonly List<Rect> obstacles = new List<Rect>();
    readonly List<Rect> sourceCovers = new List<Rect>();
    readonly List<OSWindowUnit> visibleWindows = new List<OSWindowUnit>();
    readonly List<Vector2> route = new List<Vector2>();
    readonly List<Vector2> flightPath = new List<Vector2>();
    readonly List<Vector2> curveSamples = new List<Vector2>();
    readonly List<Image> particles = new List<Image>();
    readonly Vector3[] corners = new Vector3[4];
    OSWindowUnit owner;
    Vector3 originWorld;
    float elapsed, nextRoute, routeLength;
    bool playing;
    public bool IsPlaying => playing;
    public IReadOnlyList<Vector2> FlightPath => flightPath;

    // Energy is settled by the data manager before this purely visual feedback starts.
    public void Play(OSWindowUnit window, Vector3 origin)
    {
        if (space == null || energyTarget == null || viewport == null || windowsRoot == null) return;
        owner = window;originWorld = origin;elapsed = 0f;nextRoute = 0f;playing = true;
        space.SetAsLastSibling();
        while (particles.Count < 14)
        {
            var go = new GameObject("ClaimPixel_" + particles.Count, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = space.gameObject.layer;go.transform.SetParent(space, false);
            var pixel = go.GetComponent<Image>();pixel.raycastTarget = false;
            pixel.color = new Color32(106, 187, 204, 255);
            pixel.rectTransform.sizeDelta = Vector2.one * (particles.Count % 3 == 0 ? 4f : 3f);
            pixel.enabled = false;particles.Add(pixel);
        }
        RefreshRoute();
    }

    void Update()
    {
        if (!playing) return;
        if (OS_Handle.Instance?.IsOSVisible != true) { Stop();return; }
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= nextRoute) { RefreshRoute();nextRoute = elapsed + .15f; }
        for (int i = 0; i < particles.Count; i++)
        {
            float t = (elapsed - i * .035f) / 1.15f;
            var pixel = particles[i];
            pixel.enabled = t >= 0f && t < 1f && flightPath.Count > 1;
            if (!pixel.enabled) continue;
            Vector2 position = SampleRoute(Mathf.SmoothStep(0f, 1f, t));
            pixel.rectTransform.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y)) - space.rect.center;
            var color = pixel.color;color.a = Mathf.Clamp01((1f - t) * 10f);pixel.color = color;
        }
        if (elapsed >= 1.65f) Stop();
    }

    void RefreshRoute()
    {
        Vector2 target = space.InverseTransformPoint(energyTarget.TransformPoint(energyTarget.rect.center));
        Vector2 origin = space.InverseTransformPoint(originWorld);
        Rect screen = Bounds(viewport);
        screen = Rect.MinMaxRect(screen.xMin + 4f, screen.yMin + 4f, screen.xMax - 4f, screen.yMax - 4f);
        origin = new Vector2(Mathf.Clamp(origin.x, screen.xMin, screen.xMax), Mathf.Clamp(origin.y, screen.yMin, screen.yMax));
        obstacles.Clear();sourceCovers.Clear();
        Rect ownerBounds = owner != null ? Bounds(owner.WindowRect) : new Rect();
        windowsRoot.GetComponentsInChildren(false, visibleWindows);
        foreach (var other in visibleWindows)
        {
            if (!other.IsOpen || other.IsMinimized || !other.gameObject.activeInHierarchy) continue;
            Rect r = Bounds(other.WindowRect);
            r = Rect.MinMaxRect(r.xMin - 4f, r.yMin - 4f, r.xMax + 4f, r.yMax + 4f);
            sourceCovers.Add(r);
            if (other == owner) continue;
            if (owner != null && owner.IsOpen && !IsDrawnAbove(other.transform, owner.transform))
                OSUIEnergyRoute.AddOutsideOwner(r, ownerBounds, obstacles);
            else obstacles.Add(r);
        }
        // Reverse the existing HUD-to-option route: the exposed edge of the entire
        // covering window stack becomes the entry point toward the hidden battery HUD.
        router.Build(target, origin, screen, obstacles, route, sourceCovers);
        route.Reverse();BuildFlightPath(screen);
        routeLength = 0f;
        for (int i = 1; i < flightPath.Count; i++) routeLength += Vector2.Distance(flightPath[i - 1], flightPath[i]);
    }

    void Stop() { playing = false;foreach (var pixel in particles) if (pixel != null) pixel.enabled = false; }
    void OnDisable() => Stop();
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
