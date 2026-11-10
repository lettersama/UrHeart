using UnityEngine;

/// <summary>
/// Replaceable storyboard blocking, not final chapter artwork. The director owns
/// timing and localized captions; this graphic only draws a 320 x 180 layout.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OSChapter01EndingPlaceholder : UnityEngine.UI.MaskableGraphic
{
    public enum Shot { Room, Corridor, Doorway, Animation }

    [SerializeField] Shot shot;
    [SerializeField, Range(0f, 1f)] float progress;
    Rect drawingRect;

    public void SetShot(Shot shot, float progress = 0f)
    {
        float nextProgress = float.IsNaN(progress) ? 0f : Mathf.Clamp01(progress);
        if (this.shot == shot && Mathf.Approximately(this.progress, nextProgress)) return;
        this.shot = shot;
        this.progress = nextProgress;
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        drawingRect = GetPixelAdjustedRect();
        if (drawingRect.width <= 0f || drawingRect.height <= 0f) return;

        Box(mesh, 0, 0, 320, 180, .10f);
        switch (shot)
        {
            case Shot.Room: DrawRoom(mesh); break;
            case Shot.Corridor: DrawCorridor(mesh); break;
            case Shot.Doorway: DrawDoorway(mesh, false); break;
            case Shot.Animation: DrawDoorway(mesh, true); break;
        }
    }

    void DrawRoom(UnityEngine.UI.VertexHelper mesh)
    {
        Box(mesh, 12, 55, 296, 113, .15f);
        Line(mesh, 12, 55, 308, 55, .31f);
        Line(mesh, 12, 168, 308, 168, .24f);
        Line(mesh, 12, 55, 0, 29, .23f);
        Line(mesh, 308, 55, 320, 29, .23f);
        Frame(mesh, 34, 101, 53, 43, .26f);
        Line(mesh, 60, 101, 60, 144, .23f);
        Line(mesh, 34, 122, 87, 122, .23f);
        Box(mesh, 33, 69, 94, 5, .30f);
        Line(mesh, 40, 34, 40, 69, .29f, 2f);
        Line(mesh, 120, 34, 120, 69, .29f, 2f);
        Box(mesh, 51, 74, 24, 14, .22f);
        Door(mesh, 235, 55, 54, 106, .18f);
        Mother(mesh, Mathf.Lerp(151f, 208f, progress), 40f, 1f, progress);
    }

    void DrawCorridor(UnityEngine.UI.VertexHelper mesh)
    {
        Box(mesh, 109, 53, 125, 112, .15f);
        Line(mesh, 0, 180, 109, 165, .25f);
        Line(mesh, 320, 180, 234, 165, .25f);
        Line(mesh, 0, 0, 109, 53, .28f);
        Line(mesh, 320, 0, 234, 53, .28f);
        Line(mesh, 109, 53, 109, 165, .23f);
        Line(mesh, 234, 53, 234, 165, .23f);
        Line(mesh, 109, 53, 234, 53, .28f);
        Line(mesh, 34, 18, 34, 173, .23f);
        Line(mesh, 73, 36, 73, 169, .23f);
        Line(mesh, 283, 22, 283, 173, .23f);
        Line(mesh, 0, 34, 320, 34, .16f);
        Door(mesh, 165, 53, 48, 103, .19f);
        Mother(mesh, Mathf.Lerp(133f, 144f, progress), Mathf.Lerp(24f, 46f, progress),
            Mathf.Lerp(1.16f, .91f, progress), progress);
    }

    void DrawDoorway(UnityEngine.UI.VertexHelper mesh, bool animation)
    {
        Box(mesh, 12, 35, 296, 133, .15f);
        Line(mesh, 12, 35, 308, 35, .29f);
        Line(mesh, 12, 168, 308, 168, .24f);
        Line(mesh, 12, 35, 0, 10, .22f);
        Line(mesh, 308, 35, 320, 10, .22f);

        // Shared framing keeps the eventual door interaction in a stable region.
        // Animation is neutral blocking: no new event, threat, or story outcome.
        float light = animation ? Mathf.SmoothStep(0f, 1f, progress) : 0f;
        Door(mesh, 190, 35, 78, 125, .18f + light * .025f);
        Box(mesh, 194, 36, 70, 1.2f, .25f + light * .06f);
        Mother(mesh, 149, 29, 1.12f, 0f);
    }

    void Door(UnityEngine.UI.VertexHelper mesh, float x, float y, float width, float height, float shade)
    {
        Box(mesh, x, y, width, height, .12f);
        Box(mesh, x + 4, y + 2, width - 8, height - 6, shade);
        Frame(mesh, x, y, width, height, .34f, 2f);
        Frame(mesh, x + 10, y + 15, width - 20, height - 31, .23f);
        Box(mesh, x + width - 13, y + height * .44f, 5, 2, .43f);
        Box(mesh, x - 6, y - 2, width + 12, 2, .27f);
    }

    void Mother(UnityEngine.UI.VertexHelper mesh, float x, float y, float scale, float step)
    {
        float stride = Mathf.Sin(step * Mathf.PI * 4f) * 3f * scale;
        float body = .35f;
        float foot = .29f;
        Box(mesh, x - 14 * scale, y - scale, 28 * scale, 2 * scale, .12f);
        Line(mesh, x - 4 * scale, y + 21 * scale, x - 5 * scale - stride, y + 2 * scale,
            foot, 5 * scale);
        Line(mesh, x + 4 * scale, y + 21 * scale, x + 5 * scale + stride, y + 2 * scale,
            foot, 5 * scale);
        Quad(mesh, new Vector2(x - 8 * scale, y + 57 * scale),
            new Vector2(x + 7 * scale, y + 57 * scale),
            new Vector2(x + 11 * scale, y + 18 * scale),
            new Vector2(x - 12 * scale, y + 18 * scale), body);
        Line(mesh, x - 8 * scale, y + 51 * scale, x - 13 * scale + stride * .5f, y + 29 * scale,
            body, 4 * scale);
        Line(mesh, x + 7 * scale, y + 51 * scale, x + 12 * scale - stride * .5f, y + 31 * scale,
            body, 4 * scale);
        Box(mesh, x - 3 * scale, y + 54 * scale, 6 * scale, 7 * scale, body);
        Ellipse(mesh, x, y + 67 * scale, 8 * scale, 10 * scale, .32f);
        // A small tied-hair shape is enough for the temporary mother silhouette.
        Ellipse(mesh, x - 8 * scale, y + 66 * scale, 3 * scale, 4 * scale, .30f);
    }

    void Frame(UnityEngine.UI.VertexHelper mesh, float x, float y, float width, float height,
        float shade, float thickness = 1f)
    {
        Box(mesh, x, y, width, thickness, shade);
        Box(mesh, x, y + height - thickness, width, thickness, shade);
        Box(mesh, x, y, thickness, height, shade);
        Box(mesh, x + width - thickness, y, thickness, height, shade);
    }

    void Box(UnityEngine.UI.VertexHelper mesh, float x, float y, float width, float height, float shade)
    {
        Quad(mesh, new Vector2(x, y), new Vector2(x, y + height),
            new Vector2(x + width, y + height), new Vector2(x + width, y), shade);
    }

    void Line(UnityEngine.UI.VertexHelper mesh, float ax, float ay, float bx, float by,
        float shade, float thickness = 1f)
    {
        Vector2 a = new Vector2(ax, ay), b = new Vector2(bx, by);
        Vector2 direction = (b - a).normalized;
        Vector2 side = new Vector2(-direction.y, direction.x) * (thickness * .5f);
        Quad(mesh, a - side, a + side, b + side, b - side, shade);
    }

    void Ellipse(UnityEngine.UI.VertexHelper mesh, float x, float y, float radiusX, float radiusY, float shade)
    {
        const int segments = 12;
        int first = mesh.currentVertCount;
        AddVertex(mesh, new Vector2(x, y), shade);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / segments);
            AddVertex(mesh, new Vector2(x + Mathf.Cos(angle) * radiusX, y + Mathf.Sin(angle) * radiusY), shade);
        }
        for (int i = 0; i < segments; i++)
            mesh.AddTriangle(first, first + 1 + (i + 1) % segments, first + 1 + i);
    }

    void Quad(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float shade)
    {
        int first = mesh.currentVertCount;
        AddVertex(mesh, a, shade); AddVertex(mesh, b, shade);
        AddVertex(mesh, c, shade); AddVertex(mesh, d, shade);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first + 2, first + 3, first);
    }

    void AddVertex(UnityEngine.UI.VertexHelper mesh, Vector2 point, float shade)
    {
        Vector3 position = new Vector3(drawingRect.xMin + point.x / 320f * drawingRect.width,
            drawingRect.yMin + point.y / 180f * drawingRect.height, 0f);
        Color tint = color;
        tint.r *= shade; tint.g *= shade; tint.b *= shade;
        mesh.AddVert(position, tint, Vector2.zero);
    }
}
