using System.Collections.Generic;
using UnityEngine;

// Reference-pixel visibility graph, using the same rectangle-interior rule as OSUICableGraphic.
// Kept independent of cable simulation: moving a window must not pull particles through it.
public sealed class OSUIEnergyRoute
{
    readonly List<Vector2> nodes = new List<Vector2>(96);
    readonly List<Vector2> edgeSpans = new List<Vector2>(16);
    readonly List<int> exitNodes = new List<int>(32);
    float[] distance;
    int[] previous;
    bool[] visited;
    bool[] available;

    public bool Build(Vector2 source, Vector2 target, Rect viewport,
        IReadOnlyList<Rect> obstacles, List<Vector2> result, IReadOnlyList<Rect> sourceCovers)
    {
        result.Clear();
        nodes.Clear();
        exitNodes.Clear();
        nodes.Add(target);
        if (!IsAvailable(target, viewport, obstacles))
            return false;

        bool sourceVisible = IsAvailable(source, viewport, sourceCovers);
        if (sourceVisible)
        {
            exitNodes.Add(nodes.Count);
            nodes.Add(source);
        }
        else
        {
            // Only exposed portions of the combined silhouette can emit particles.
            // A window corner/projection inside another window is not an exit.
            for (int i = 0; i < sourceCovers.Count; i++)
                for (int edge = 0; edge < 4; edge++)
                    AddExposedEdge(i, edge, source, viewport, sourceCovers);
        }
        if (exitNodes.Count == 0) return false;
        for (int i = 0; i < obstacles.Count; i++)
        {
            Rect r = obstacles[i];
            nodes.Add(new Vector2(r.xMin, r.yMin));
            nodes.Add(new Vector2(r.xMin, r.yMax));
            nodes.Add(new Vector2(r.xMax, r.yMax));
            nodes.Add(new Vector2(r.xMax, r.yMin));
        }
        nodes.Add(new Vector2(viewport.xMin, viewport.yMin));
        nodes.Add(new Vector2(viewport.xMax, viewport.yMin));
        nodes.Add(new Vector2(viewport.xMax, viewport.yMax));
        nodes.Add(new Vector2(viewport.xMin, viewport.yMax));

        int count = nodes.Count;
        if (distance == null || distance.Length < count)
        {
            distance = new float[count];
            previous = new int[count];
            visited = new bool[count];
            available = new bool[count];
        }
        for (int i = 0; i < count; i++)
        {
            available[i] = IsAvailable(nodes[i], viewport, obstacles);
            visited[i] = false;
            previous[i] = -1;
            distance[i] = float.PositiveInfinity;
        }
        // Solve reachability from the battery; exit selection below is independent
        // of the length of the visible flight, so a distant corner cannot win.
        distance[0] = 0f;

        for (int iteration = 0; iteration < count; iteration++)
        {
            int current = -1;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!visited[i] && available[i] && distance[i] < best)
                {
                    current = i;
                    best = distance[i];
                }
            }
            if (current < 0)
                break;
            visited[current] = true;
            for (int next = 0; next < count; next++)
            {
                if (visited[next] || !available[next] || current == next
                    || !IsSegmentClear(nodes[current], nodes[next], obstacles))
                    continue;
                float candidate = best + Vector2.Distance(nodes[current], nodes[next]);
                if (candidate >= distance[next])
                    continue;
                distance[next] = candidate;
                previous[next] = current;
            }
        }
        float nearest = float.PositiveInfinity;
        foreach (int exit in exitNodes)
            if (!float.IsPositiveInfinity(distance[exit]))
                nearest = Mathf.Min(nearest, Vector2.Distance(source, nodes[exit]));
        int chosen = -1;
        bool chosenPreferred = false;
        float chosenDistance = float.PositiveInfinity;
        foreach (int exit in exitNodes)
        {
            if (float.IsPositiveInfinity(distance[exit])) continue;
            Vector2 delta = nodes[exit] - source;
            float separation = delta.magnitude;
            // Bottom/right is a local preference (within four reference pixels),
            // never a screen-sized penalty that forces a trip around the stack.
            if (separation > nearest + 4f) continue;
            bool preferred = delta.x >= -0.1f && delta.y <= 0.1f;
            if (chosen >= 0 && (chosenPreferred && !preferred
                || chosenPreferred == preferred && separation >= chosenDistance)) continue;
            chosen = exit;
            chosenPreferred = preferred;
            chosenDistance = separation;
        }
        if (chosen < 0) return false;
        for (int node = chosen; node >= 0; node = previous[node])
            result.Add(nodes[node]);
        return result.Count >= 2;
    }

    void AddExposedEdge(int index, int edge, Vector2 source, Rect viewport, IReadOnlyList<Rect> covers)
    {
        Rect r = covers[index];
        bool vertical = edge < 2;
        bool positive = edge == 1 || edge == 3;
        float fixedAxis = vertical ? (positive ? r.xMax : r.xMin) : (positive ? r.yMax : r.yMin);
        float probe = fixedAxis + (positive ? 0.02f : -0.02f);
        if (probe < (vertical ? viewport.xMin : viewport.yMin)
            || probe > (vertical ? viewport.xMax : viewport.yMax)) return;
        float from = Mathf.Max(vertical ? r.yMin : r.xMin, vertical ? viewport.yMin : viewport.xMin);
        float to = Mathf.Min(vertical ? r.yMax : r.xMax, vertical ? viewport.yMax : viewport.xMax);
        if (to <= from) return;
        edgeSpans.Clear();
        edgeSpans.Add(new Vector2(from, to));
        for (int i = 0; i < covers.Count; i++)
        {
            if (i == index) continue;
            Rect other = covers[i];
            if (probe <= (vertical ? other.xMin : other.yMin)
                || probe >= (vertical ? other.xMax : other.yMax)) continue;
            float low = vertical ? other.yMin : other.xMin;
            float high = vertical ? other.yMax : other.xMax;
            for (int j = edgeSpans.Count - 1; j >= 0; j--)
            {
                Vector2 span = edgeSpans[j];
                if (high <= span.x || low >= span.y) continue;
                edgeSpans.RemoveAt(j);
                if (low > span.x) edgeSpans.Add(new Vector2(span.x, low));
                if (high < span.y) edgeSpans.Add(new Vector2(high, span.y));
            }
        }
        foreach (Vector2 span in edgeSpans)
        {
            float along = Mathf.Clamp(vertical ? source.y : source.x, span.x, span.y);
            Vector2 point = vertical ? new Vector2(fixedAxis, along) : new Vector2(along, fixedAxis);
            if (!IsAvailable(point, viewport, covers)) continue;
            exitNodes.Add(nodes.Count);
            nodes.Add(point);
        }
    }

    static bool IsAvailable(Vector2 point, Rect viewport, IReadOnlyList<Rect> obstacles)
    {
        if (point.x < viewport.xMin || point.x > viewport.xMax
            || point.y < viewport.yMin || point.y > viewport.yMax)
            return false;
        for (int i = 0; i < obstacles.Count; i++)
        {
            Rect r = obstacles[i];
            if (point.x > r.xMin + 0.01f && point.x < r.xMax - 0.01f
                && point.y > r.yMin + 0.01f && point.y < r.yMax - 0.01f)
                return false;
        }
        return true;
    }

    public static bool IsSegmentClear(Vector2 start, Vector2 end, IReadOnlyList<Rect> obstacles)
    {
        Vector2 delta = end - start;
        for (int i = 0; i < obstacles.Count; i++)
        {
            Rect r = obstacles[i];
            float entry = 0f, exit = 1f;
            if (Clip(-delta.x, start.x - r.xMin - 0.01f, ref entry, ref exit)
                && Clip(delta.x, r.xMax - 0.01f - start.x, ref entry, ref exit)
                && Clip(-delta.y, start.y - r.yMin - 0.01f, ref entry, ref exit)
                && Clip(delta.y, r.yMax - 0.01f - start.y, ref entry, ref exit))
                return false;
        }
        return true;
    }

    static bool Clip(float direction, float distance, ref float entry, ref float exit)
    {
        if (Mathf.Abs(direction) < 0.00001f)
            return distance >= 0f;
        float ratio = distance / direction;
        if (direction < 0f)
        {
            if (ratio > exit) return false;
            entry = Mathf.Max(entry, ratio);
        }
        else
        {
            if (ratio < entry) return false;
            exit = Mathf.Min(exit, ratio);
        }
        return true;
    }

    // Lower windows are only obstacles where they protrude outside the receiving window.
    public static void AddOutsideOwner(Rect bounds, Rect owner, List<Rect> result)
    {
        float left = Mathf.Max(bounds.xMin, owner.xMin);
        float right = Mathf.Min(bounds.xMax, owner.xMax);
        float bottom = Mathf.Max(bounds.yMin, owner.yMin);
        float top = Mathf.Min(bounds.yMax, owner.yMax);
        if (left >= right || bottom >= top)
        {
            result.Add(bounds);
            return;
        }
        Add(Rect.MinMaxRect(bounds.xMin, bounds.yMin, left, bounds.yMax), result);
        Add(Rect.MinMaxRect(right, bounds.yMin, bounds.xMax, bounds.yMax), result);
        Add(Rect.MinMaxRect(left, bounds.yMin, right, bottom), result);
        Add(Rect.MinMaxRect(left, top, right, bounds.yMax), result);
    }

    static void Add(Rect rect, List<Rect> result)
    {
        if (rect.width > 0.01f && rect.height > 0.01f)
            result.Add(rect);
    }
}
