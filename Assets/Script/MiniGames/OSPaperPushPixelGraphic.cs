using UnityEngine;
using UnityEngine.UI;

// Small UI symbols drawn on the same integer grid as the paper pawns.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OSPaperPushPixelGraphic : MaskableGraphic
{
    public enum Shape { CycleArrows, PlayerMarker, Shield, LightPush, HeavyPush, Record, MutedSlash, MusicNote }
    [SerializeField] Shape shape;
    public Shape Symbol { get => shape; set { shape = value;SetVerticesDirty(); } }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (shape == Shape.Shield)
        {
            Bitmap(mesh, new[] { "001111100", "111000111", "100000001", "100010001", "100111001", "100010001", "010000010", "001000100", "000111000" });return;
        }
        if (shape == Shape.LightPush || shape == Shape.HeavyPush)
        {
            int count = shape == Shape.HeavyPush ? 2 : 1;
            for (int i = 0; i < count; i++)
            { int x = i * 5 - (count == 2 ? 4 : 1);Line(mesh, x - 2, 4, x + 2, 0);Line(mesh, x + 2, 0, x - 2, -4); }
            Line(mesh, -6, -4, -6, 4);return;
        }
        if (shape == Shape.Record)
        {
            Bitmap(mesh, new[] {
                "0000011111100000", "0001100000011000", "0010000000000100", "0100000000000010",
                "0100000000000010", "1000000000000001", "1000000000000001", "1000000000000001",
                "1000000000000001", "1000000000000001", "1000000000000001", "0100000000000010",
                "0100000000000010", "0010000000000100", "0001100000011000", "0000011111100000"
            });
            return;
        }
        if (shape == Shape.MutedSlash)
        { Line(mesh, -7, -7, 7, 7);Line(mesh, -7, -6, 6, 7);return; }
        if (shape == Shape.MusicNote)
        { Bitmap(mesh, new[] { "00111110", "00100010", "00100010", "00100010", "11100110", "11101110", "11001100" });return; }
        if (shape == Shape.PlayerMarker)
        {
            for (int row = 0; row < 4; row++)
                for (int x = -3 + row; x <= 3 - row; x++) Pixel(mesh, x, 2 - row);
            return;
        }
        // Guard (top) -> Light (bottom left) -> Heavy (bottom right) -> Guard.
        Arrow(mesh, -9, 10, -20, -4, -19, 2, -14, -2);
        Arrow(mesh, -12, -15, 12, -15, 8, -12, 8, -18);
        Arrow(mesh, 20, -4, 9, 10, 15, 8, 10, 4);
    }

    void Bitmap(VertexHelper mesh, string[] rows)
    {
        for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++)
            if (rows[y][x] == '1') Pixel(mesh, x - rows[y].Length / 2, (rows.Length - 1) / 2 - y);
    }

    void Arrow(VertexHelper mesh, int x0, int y0, int x1, int y1, int ax, int ay, int bx, int by)
    {
        Line(mesh, x0, y0, x1, y1);
        Line(mesh, x1, y1, ax, ay);
        Line(mesh, x1, y1, bx, by);
    }

    void Line(VertexHelper mesh, int x0, int y0, int x1, int y1)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            Pixel(mesh, x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int twice = 2 * error;
            if (twice >= dy) { error += dy;x0 += sx; }
            if (twice <= dx) { error += dx;y0 += sy; }
        }
    }

    void Pixel(VertexHelper mesh, float x, float y)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(new Vector3(x, y), color, Vector2.zero);
        mesh.AddVert(new Vector3(x, y + 1), color, Vector2.zero);
        mesh.AddVert(new Vector3(x + 1, y + 1), color, Vector2.zero);
        mesh.AddVert(new Vector3(x + 1, y), color, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }
}
