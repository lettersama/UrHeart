using UnityEngine;

// Reads physical deltas, not the cage-clamped pointer. Three deliberate strokes
// (two reversals) distinguish an answer from simply moving to one side.
public sealed class OSCursorShakeRecognizer
{
    const float StrokeDistance = 8f;
    int axis, direction, reversals;
    float stroke, idle;

    public void Reset()
    {
        axis = direction = reversals = 0;
        stroke = idle = 0f;
    }

    public int Push(Vector2 delta, float seconds)
    {
        idle += Mathf.Max(0f, seconds);
        if (idle > .75f) Reset();
        float x = Mathf.Abs(delta.x), y = Mathf.Abs(delta.y);
        if (Mathf.Max(x, y) < .1f) return 0;
        int nextAxis = x > y * 1.25f ? 1 : y > x * 1.25f ? 2 : 0;
        if (nextAxis == 0) return 0;
        idle = 0f;
        float distance = nextAxis == 1 ? delta.x : delta.y;
        int nextDirection = distance > 0 ? 1 : -1;
        if (axis != nextAxis)
        {
            axis = nextAxis;
            direction = nextDirection;
            reversals = 0;
            stroke = 0;
        }
        if (direction != nextDirection)
        {
            reversals = stroke >= StrokeDistance ? reversals + 1 : 0;
            direction = nextDirection;
            stroke = 0;
        }
        stroke += Mathf.Abs(distance);
        if (reversals < 2 || stroke < StrokeDistance) return 0;
        int answer = axis;
        Reset();
        return answer;
    }
}
