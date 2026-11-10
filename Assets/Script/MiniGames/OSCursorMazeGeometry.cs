using System;
using UnityEngine;

/// <summary>
/// Collision geometry for the approved 216 x 144 maze artwork. Coordinates are
/// native pixels, x right and y down, relative to the Walls image at window (20,32).
/// Only the pointer hotspot collides; its chain and weight are presentation.
/// </summary>
public static class OSCursorMazeGeometry
{
    public sealed class Layout
    {
        public readonly Rect Bounds;
        public readonly Vector2 Start;
        public readonly Rect Exit;
        public readonly Rect[] Walls;
        public readonly Vector2[] SolutionPath;
        internal readonly Rect ExitCorridor;

        internal Layout(Rect bounds, Vector2 start, Rect exit, Rect[] walls, Vector2[] solutionPath)
        {
            Bounds = bounds;
            Start = start;
            Exit = exit;
            Walls = walls;
            SolutionPath = solutionPath;
            // This second allowed rectangle overlaps the whole board and extends
            // only through the intended exit. The entrance cannot be used to walk
            // around the outside of the maze.
            ExitCorridor = exit.yMin >= bounds.yMax
                ? new Rect(exit.xMin, bounds.yMin, exit.width, exit.yMax - bounds.yMin)
                : new Rect(bounds.xMin, exit.yMin, exit.xMax - bounds.xMin, exit.height);
        }
    }

    public const int RoundCount = 3;
    const float Epsilon = .000001f;

    /// <param name="zeroBasedRound">0 = small weight, 1 = downward attraction, 2 = reversed axes.</param>
    public static Layout GetLayout(int zeroBasedRound)
    {
        if (zeroBasedRound < 0 || zeroBasedRound >= Layouts.Length)
            throw new ArgumentOutOfRangeException(nameof(zeroBasedRound));
        return Layouts[zeroBasedRound];
    }

    /// <summary>
    /// Tests the entire movement, not just its endpoint. On collision, hitPoint is
    /// the first contact along the segment. Radius is a square hotspot half-extent,
    /// conservatively rounded around wall corners; use .8 native pixels in play.
    /// </summary>
    public static bool TryMove(Layout layout, Vector2 from, Vector2 to, float radius, out Vector2 hitPoint)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        hitPoint = IsFinite(from) ? from : layout.Start;
        if (!ValidRadius(radius) || !IsFinite(from) || !IsFinite(to) || !IsPointSafe(layout, from, radius))
            return false;

        float boundaryEnd = AllowedSegmentEnd(layout, from, to, radius);
        bool collided = boundaryEnd < 1f;
        float firstContact = boundaryEnd;
        for (int i = 0; i < layout.Walls.Length; i++)
        {
            if (SegmentInterval(Expand(layout.Walls[i], radius), from, to, out float enter, out _)
                && enter <= firstContact)
            {
                firstContact = enter;
                collided = true;
            }
        }

        if (!collided)
        {
            hitPoint = to;
            return true;
        }
        hitPoint = from + (to - from) * Math.Max(0f, Math.Min(1f, firstContact));
        return false;
    }

    public static bool IsPointSafe(Layout layout, Vector2 point, float radius)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (!ValidRadius(radius) || !IsFinite(point)) return false;
        if (!ContainsClosed(Inset(layout.Bounds, radius), point)
            && !ContainsClosed(Inset(layout.ExitCorridor, radius), point)) return false;
        for (int i = 0; i < layout.Walls.Length; i++)
            if (ContainsClosed(Expand(layout.Walls[i], radius), point)) return false;
        return true;
    }

    static float AllowedSegmentEnd(Layout layout, Vector2 from, Vector2 to, float radius)
    {
        bool board = SegmentInterval(Inset(layout.Bounds, radius), from, to, out float boardIn, out float boardOut);
        bool exit = SegmentInterval(Inset(layout.ExitCorridor, radius), from, to, out float exitIn, out float exitOut);
        float reach = 0f;
        if (board && boardIn <= Epsilon) reach = boardOut;
        if (exit && exitIn <= Epsilon) reach = Math.Max(reach, exitOut);
        // An interval beginning after reach is a corner-cut through the exterior,
        // even if the final point happens to land inside the exit corridor.
        if (board && boardIn <= reach + Epsilon) reach = Math.Max(reach, boardOut);
        if (exit && exitIn <= reach + Epsilon) reach = Math.Max(reach, exitOut);
        return reach;
    }

    static bool SegmentInterval(Rect rect, Vector2 from, Vector2 to, out float enter, out float leave)
    {
        enter = 0f;
        leave = 1f;
        if (rect.width < 0f || rect.height < 0f) return false;
        return ClipAxis(from.x, to.x - from.x, rect.xMin, rect.xMax, ref enter, ref leave)
            && ClipAxis(from.y, to.y - from.y, rect.yMin, rect.yMax, ref enter, ref leave);
    }

    static bool ClipAxis(float start, float delta, float minimum, float maximum, ref float enter, ref float leave)
    {
        if (Math.Abs(delta) < Epsilon) return start >= minimum && start <= maximum;
        float a = (minimum - start) / delta;
        float b = (maximum - start) / delta;
        if (a > b) { float swap = a; a = b; b = swap; }
        enter = Math.Max(enter, a);
        leave = Math.Min(leave, b);
        return enter <= leave;
    }

    static Rect Expand(Rect rect, float amount) => new Rect(rect.xMin - amount, rect.yMin - amount,
        rect.width + amount * 2f, rect.height + amount * 2f);
    static Rect Inset(Rect rect, float amount) => Expand(rect, -amount);
    static bool ContainsClosed(Rect rect, Vector2 point) => rect.width >= 0f && rect.height >= 0f
        && point.x >= rect.xMin && point.x <= rect.xMax && point.y >= rect.yMin && point.y <= rect.yMax;
    static bool IsFinite(Vector2 point) => !float.IsNaN(point.x) && !float.IsNaN(point.y)
        && !float.IsInfinity(point.x) && !float.IsInfinity(point.y);
    static bool ValidRadius(float radius) => radius >= 0f && !float.IsNaN(radius) && !float.IsInfinity(radius);

    // Exact source occupancy and a BFS solution, regenerated by
    // Tools/Chapter02CursorExperiment/Runtime/generate_geometry.py.
    // Source attribution is retained per layout; source art is CC0-1.0.
    // BEGIN GENERATED LAYOUTS
    static readonly Layout[] Layouts =
    {
        // Stage 1: https://commons.wikimedia.org/wiki/File:Maze_simple.svg (CC0-1.0).
        new Layout(
            new Rect(11f, 7f, 193f, 130f), new Vector2(108f, 22f), new Rect(103f, 137f, 10f, 6f),
            new Rect[]
            {
                new Rect(68f, 10f, 3f, 24f),
                new Rect(125f, 10f, 3f, 24f),
                new Rect(30f, 34f, 3f, 3f),
                new Rect(33f, 34f, 16f, 3f),
                new Rect(49f, 34f, 3f, 3f),
                new Rect(68f, 34f, 3f, 3f),
                new Rect(87f, 34f, 3f, 3f),
                new Rect(90f, 34f, 16f, 3f),
                new Rect(106f, 34f, 3f, 3f),
                new Rect(125f, 34f, 3f, 3f),
                new Rect(144f, 34f, 3f, 3f),
                new Rect(147f, 34f, 16f, 3f),
                new Rect(163f, 34f, 3f, 3f),
                new Rect(166f, 34f, 16f, 3f),
                new Rect(182f, 34f, 3f, 3f),
                new Rect(30f, 37f, 3f, 21f),
                new Rect(68f, 37f, 3f, 21f),
                new Rect(106f, 37f, 3f, 21f),
                new Rect(144f, 37f, 3f, 21f),
                new Rect(163f, 37f, 3f, 21f),
                new Rect(30f, 58f, 3f, 3f),
                new Rect(33f, 58f, 16f, 3f),
                new Rect(49f, 58f, 3f, 3f),
                new Rect(52f, 58f, 16f, 3f),
                new Rect(68f, 58f, 3f, 3f),
                new Rect(71f, 58f, 16f, 3f),
                new Rect(87f, 58f, 3f, 3f),
                new Rect(106f, 58f, 3f, 3f),
                new Rect(109f, 58f, 16f, 3f),
                new Rect(125f, 58f, 3f, 3f),
                new Rect(144f, 58f, 3f, 3f),
                new Rect(163f, 58f, 3f, 3f),
                new Rect(182f, 58f, 3f, 3f),
                new Rect(185f, 58f, 16f, 3f),
                new Rect(49f, 61f, 3f, 21f),
                new Rect(87f, 61f, 3f, 21f),
                new Rect(125f, 61f, 3f, 21f),
                new Rect(163f, 61f, 3f, 21f),
                new Rect(182f, 61f, 3f, 21f),
                new Rect(14f, 82f, 16f, 3f),
                new Rect(30f, 82f, 3f, 3f),
                new Rect(49f, 82f, 3f, 3f),
                new Rect(68f, 82f, 3f, 3f),
                new Rect(71f, 82f, 16f, 3f),
                new Rect(87f, 82f, 3f, 3f),
                new Rect(106f, 82f, 3f, 3f),
                new Rect(125f, 82f, 3f, 3f),
                new Rect(128f, 82f, 16f, 3f),
                new Rect(144f, 82f, 3f, 3f),
                new Rect(147f, 82f, 16f, 3f),
                new Rect(163f, 82f, 3f, 3f),
                new Rect(182f, 82f, 3f, 3f),
                new Rect(68f, 85f, 3f, 21f),
                new Rect(106f, 85f, 3f, 21f),
                new Rect(144f, 85f, 3f, 21f),
                new Rect(30f, 106f, 3f, 3f),
                new Rect(33f, 106f, 16f, 3f),
                new Rect(49f, 106f, 3f, 3f),
                new Rect(52f, 106f, 16f, 3f),
                new Rect(68f, 106f, 3f, 3f),
                new Rect(87f, 106f, 3f, 3f),
                new Rect(106f, 106f, 3f, 3f),
                new Rect(109f, 106f, 16f, 3f),
                new Rect(125f, 106f, 3f, 3f),
                new Rect(128f, 106f, 16f, 3f),
                new Rect(144f, 106f, 3f, 3f),
                new Rect(163f, 106f, 3f, 3f),
                new Rect(166f, 106f, 16f, 3f),
                new Rect(182f, 106f, 3f, 3f),
                new Rect(185f, 106f, 16f, 3f),
                new Rect(87f, 109f, 3f, 25f),
                new Rect(125f, 109f, 3f, 25f),
                new Rect(11f, 7f, 3f, 130f),
                new Rect(201f, 7f, 3f, 130f),
                new Rect(11f, 7f, 92f, 3f),
                new Rect(113f, 7f, 91f, 3f),
                new Rect(11f, 134f, 92f, 3f),
                new Rect(113f, 134f, 91f, 3f),
            },
            new Vector2[]
            {
                new Vector2(108f, 22f),
                new Vector2(108f, 32f),
                new Vector2(85f, 32f),
                new Vector2(85f, 56f),
                new Vector2(92f, 56f),
                new Vector2(92f, 132f),
                new Vector2(105f, 132f),
                new Vector2(105f, 140f),
                new Vector2(108f, 140f),
            }),
        // Stage 2: https://commons.wikimedia.org/wiki/File:Maze-stage.png (CC0-1.0).
        new Layout(
            new Rect(10f, 7f, 195f, 130f), new Vector2(108f, 22f), new Rect(103f, 137f, 10f, 6f),
            new Rect[]
            {
                new Rect(130f, 10f, 3f, 16f),
                new Rect(34f, 26f, 3f, 3f),
                new Rect(37f, 26f, 21f, 3f),
                new Rect(58f, 26f, 3f, 3f),
                new Rect(61f, 26f, 21f, 3f),
                new Rect(82f, 26f, 3f, 3f),
                new Rect(85f, 26f, 21f, 3f),
                new Rect(106f, 26f, 3f, 3f),
                new Rect(130f, 26f, 3f, 3f),
                new Rect(154f, 26f, 3f, 3f),
                new Rect(157f, 26f, 21f, 3f),
                new Rect(178f, 26f, 3f, 3f),
                new Rect(106f, 29f, 3f, 19f),
                new Rect(178f, 29f, 3f, 19f),
                new Rect(34f, 48f, 3f, 3f),
                new Rect(37f, 48f, 21f, 3f),
                new Rect(58f, 48f, 3f, 3f),
                new Rect(61f, 48f, 21f, 3f),
                new Rect(82f, 48f, 3f, 3f),
                new Rect(85f, 48f, 21f, 3f),
                new Rect(106f, 48f, 3f, 3f),
                new Rect(109f, 48f, 21f, 3f),
                new Rect(130f, 48f, 3f, 3f),
                new Rect(133f, 48f, 21f, 3f),
                new Rect(154f, 48f, 3f, 3f),
                new Rect(157f, 48f, 21f, 3f),
                new Rect(178f, 48f, 3f, 3f),
                new Rect(34f, 51f, 3f, 19f),
                new Rect(130f, 51f, 3f, 19f),
                new Rect(34f, 70f, 3f, 3f),
                new Rect(58f, 70f, 3f, 3f),
                new Rect(61f, 70f, 21f, 3f),
                new Rect(82f, 70f, 3f, 3f),
                new Rect(106f, 70f, 3f, 3f),
                new Rect(109f, 70f, 21f, 3f),
                new Rect(130f, 70f, 3f, 3f),
                new Rect(154f, 70f, 3f, 3f),
                new Rect(178f, 70f, 3f, 3f),
                new Rect(181f, 70f, 21f, 3f),
                new Rect(58f, 73f, 3f, 19f),
                new Rect(106f, 73f, 3f, 19f),
                new Rect(154f, 73f, 3f, 19f),
                new Rect(178f, 73f, 3f, 19f),
                new Rect(13f, 92f, 21f, 3f),
                new Rect(34f, 92f, 3f, 3f),
                new Rect(37f, 92f, 21f, 3f),
                new Rect(58f, 92f, 3f, 3f),
                new Rect(61f, 92f, 21f, 3f),
                new Rect(82f, 92f, 3f, 3f),
                new Rect(106f, 92f, 3f, 3f),
                new Rect(130f, 92f, 3f, 3f),
                new Rect(133f, 92f, 21f, 3f),
                new Rect(154f, 92f, 3f, 3f),
                new Rect(178f, 92f, 3f, 3f),
                new Rect(82f, 95f, 3f, 19f),
                new Rect(106f, 95f, 3f, 19f),
                new Rect(130f, 95f, 3f, 19f),
                new Rect(178f, 95f, 3f, 19f),
                new Rect(34f, 114f, 3f, 3f),
                new Rect(37f, 114f, 21f, 3f),
                new Rect(58f, 114f, 3f, 3f),
                new Rect(82f, 114f, 3f, 3f),
                new Rect(106f, 114f, 3f, 3f),
                new Rect(130f, 114f, 3f, 3f),
                new Rect(154f, 114f, 3f, 3f),
                new Rect(157f, 114f, 21f, 3f),
                new Rect(178f, 114f, 3f, 3f),
                new Rect(34f, 117f, 3f, 17f),
                new Rect(10f, 7f, 3f, 130f),
                new Rect(202f, 7f, 3f, 130f),
                new Rect(10f, 7f, 93f, 3f),
                new Rect(113f, 7f, 92f, 3f),
                new Rect(10f, 134f, 93f, 3f),
                new Rect(113f, 134f, 92f, 3f),
            },
            new Vector2[]
            {
                new Vector2(108f, 22f),
                new Vector2(108f, 24f),
                new Vector2(111f, 24f),
                new Vector2(111f, 31f),
                new Vector2(152f, 31f),
                new Vector2(152f, 24f),
                new Vector2(183f, 24f),
                new Vector2(183f, 68f),
                new Vector2(176f, 68f),
                new Vector2(176f, 112f),
                new Vector2(152f, 112f),
                new Vector2(152f, 132f),
                new Vector2(111f, 132f),
                new Vector2(111f, 140f),
                new Vector2(108f, 140f),
            }),
        // Stage 3: https://commons.wikimedia.org/wiki/File:Simple_maze_on_grid.svg (CC0-1.0).
        new Layout(
            new Rect(43f, 7f, 130f, 130f), new Vector2(108f, 22f), new Rect(103f, 137f, 10f, 6f),
            new Rect[]
            {
                new Rect(43f, 7f, 10f, 10f),
                new Rect(53f, 7f, 10f, 10f),
                new Rect(63f, 7f, 10f, 10f),
                new Rect(73f, 7f, 10f, 10f),
                new Rect(83f, 7f, 10f, 10f),
                new Rect(93f, 7f, 10f, 10f),
                new Rect(113f, 7f, 10f, 10f),
                new Rect(123f, 7f, 10f, 10f),
                new Rect(133f, 7f, 10f, 10f),
                new Rect(143f, 7f, 10f, 10f),
                new Rect(153f, 7f, 10f, 10f),
                new Rect(163f, 7f, 10f, 10f),
                new Rect(43f, 17f, 10f, 10f),
                new Rect(83f, 17f, 10f, 10f),
                new Rect(163f, 17f, 10f, 10f),
                new Rect(43f, 27f, 10f, 10f),
                new Rect(63f, 27f, 10f, 10f),
                new Rect(103f, 27f, 10f, 10f),
                new Rect(113f, 27f, 10f, 10f),
                new Rect(123f, 27f, 10f, 10f),
                new Rect(133f, 27f, 10f, 10f),
                new Rect(143f, 27f, 10f, 10f),
                new Rect(163f, 27f, 10f, 10f),
                new Rect(43f, 37f, 10f, 10f),
                new Rect(63f, 37f, 10f, 10f),
                new Rect(83f, 37f, 10f, 10f),
                new Rect(103f, 37f, 10f, 10f),
                new Rect(143f, 37f, 10f, 10f),
                new Rect(163f, 37f, 10f, 10f),
                new Rect(43f, 47f, 10f, 10f),
                new Rect(63f, 47f, 10f, 10f),
                new Rect(73f, 47f, 10f, 10f),
                new Rect(83f, 47f, 10f, 10f),
                new Rect(93f, 47f, 10f, 10f),
                new Rect(103f, 47f, 10f, 10f),
                new Rect(113f, 47f, 10f, 10f),
                new Rect(123f, 47f, 10f, 10f),
                new Rect(163f, 47f, 10f, 10f),
                new Rect(43f, 57f, 10f, 10f),
                new Rect(63f, 57f, 10f, 10f),
                new Rect(83f, 57f, 10f, 10f),
                new Rect(123f, 57f, 10f, 10f),
                new Rect(133f, 57f, 10f, 10f),
                new Rect(143f, 57f, 10f, 10f),
                new Rect(163f, 57f, 10f, 10f),
                new Rect(43f, 67f, 10f, 10f),
                new Rect(53f, 67f, 10f, 10f),
                new Rect(63f, 67f, 10f, 10f),
                new Rect(83f, 67f, 10f, 10f),
                new Rect(103f, 67f, 10f, 10f),
                new Rect(123f, 67f, 10f, 10f),
                new Rect(163f, 67f, 10f, 10f),
                new Rect(43f, 77f, 10f, 10f),
                new Rect(83f, 77f, 10f, 10f),
                new Rect(93f, 77f, 10f, 10f),
                new Rect(103f, 77f, 10f, 10f),
                new Rect(143f, 77f, 10f, 10f),
                new Rect(153f, 77f, 10f, 10f),
                new Rect(163f, 77f, 10f, 10f),
                new Rect(43f, 87f, 10f, 10f),
                new Rect(63f, 87f, 10f, 10f),
                new Rect(73f, 87f, 10f, 10f),
                new Rect(83f, 87f, 10f, 10f),
                new Rect(103f, 87f, 10f, 10f),
                new Rect(123f, 87f, 10f, 10f),
                new Rect(133f, 87f, 10f, 10f),
                new Rect(143f, 87f, 10f, 10f),
                new Rect(163f, 87f, 10f, 10f),
                new Rect(43f, 97f, 10f, 10f),
                new Rect(63f, 97f, 10f, 10f),
                new Rect(103f, 97f, 10f, 10f),
                new Rect(143f, 97f, 10f, 10f),
                new Rect(163f, 97f, 10f, 10f),
                new Rect(43f, 107f, 10f, 10f),
                new Rect(63f, 107f, 10f, 10f),
                new Rect(83f, 107f, 10f, 10f),
                new Rect(103f, 107f, 10f, 10f),
                new Rect(113f, 107f, 10f, 10f),
                new Rect(123f, 107f, 10f, 10f),
                new Rect(143f, 107f, 10f, 10f),
                new Rect(163f, 107f, 10f, 10f),
                new Rect(43f, 117f, 10f, 10f),
                new Rect(83f, 117f, 10f, 10f),
                new Rect(163f, 117f, 10f, 10f),
                new Rect(43f, 127f, 10f, 10f),
                new Rect(53f, 127f, 10f, 10f),
                new Rect(63f, 127f, 10f, 10f),
                new Rect(73f, 127f, 10f, 10f),
                new Rect(83f, 127f, 10f, 10f),
                new Rect(93f, 127f, 10f, 10f),
                new Rect(113f, 127f, 10f, 10f),
                new Rect(123f, 127f, 10f, 10f),
                new Rect(133f, 127f, 10f, 10f),
                new Rect(143f, 127f, 10f, 10f),
                new Rect(153f, 127f, 10f, 10f),
                new Rect(163f, 127f, 10f, 10f),
            },
            new Vector2[]
            {
                new Vector2(108f, 22f),
                new Vector2(108f, 25f),
                new Vector2(155f, 25f),
                new Vector2(155f, 75f),
                new Vector2(141f, 75f),
                new Vector2(141f, 85f),
                new Vector2(121f, 85f),
                new Vector2(121f, 105f),
                new Vector2(135f, 105f),
                new Vector2(135f, 125f),
                new Vector2(111f, 125f),
                new Vector2(111f, 140f),
                new Vector2(108f, 140f),
            }),
    };
    // END GENERATED LAYOUTS
}
