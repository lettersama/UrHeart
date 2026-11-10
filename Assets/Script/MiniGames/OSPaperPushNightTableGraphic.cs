using UnityEngine;
using UnityEngine.UI;

// Chapter 2 night stage, strictly two colours: black and paper ink.
//   - The room is black. Only the lamp's cone is lit, so the pawns keep their
//     black outlines against light, and everything outside the cone stays dark.
//   - The cone has a solid core and a three-step 2x2 ordered-dither edge.
//   - The table is always drawn: top edge, apron and end caps span exactly the
//     platform (pushing a pawn past an end cap is falling off), and the legs
//     dissolve into the dark.
// The lamp/background sits behind the pawns; the table sits in front of their stands.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OSPaperPushNightTableGraphic : MaskableGraphic
{
    public enum RenderLayer { Background, TableForeground }
    static readonly Color32 Paper = new Color32(135, 130, 126, 255);
    static readonly Color32 Black = new Color32(0, 0, 0, 255);
    // 2x2 Bayer ranks: level n (1..3) lights n of every 4 pixels.
    static readonly int[] Bayer = { 0, 2, 3, 1 };
    // Same geometry as Stage/Platform: 216 wide, its top 26 above the screen bottom.
    const int TableHalfWidth = 108, TableTopFromBottom = 26, ApronHeight = 8;
    [SerializeField] RenderLayer renderLayer;
    int lampOffset;
    bool[] grid;
    int gridWidth, gridHeight;

    public void SetRenderLayer(RenderLayer layer)
    {
        if (renderLayer == layer) return;
        renderLayer = layer;SetVerticesDirty();
    }

    public void SetLampOffset(int offset)
    {
        if (lampOffset == offset) return;
        lampOffset = offset;SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect bounds = rectTransform.rect;
        bool foreground = renderLayer == RenderLayer.TableForeground;
        // Opaque only below the tabletop: stands disappear behind the desk,
        // while all pixels above its top remain transparent in this layer.
        float blackHeight = foreground ? Mathf.Min(bounds.height, TableTopFromBottom) : bounds.height;
        Quad(vh, bounds.xMin, bounds.yMin, bounds.width, blackHeight, Black);
        int width = Mathf.FloorToInt(bounds.width), height = Mathf.FloorToInt(bounds.height);
        if (width <= 0 || height <= 0) return;
        if (grid == null || gridWidth != width || gridHeight != height)
        { grid = new bool[width * height];gridWidth = width;gridHeight = height; }
        else System.Array.Clear(grid, 0, grid.Length);

        // Grid coordinates: x = 0 is the screen centre column, y = 0 the bottom row.
        int centre = width / 2, top = TableTopFromBottom, apron = top - ApronHeight;
        int cordBottom = height - 12, shadeBottom = cordBottom - 7, apex = shadeBottom - 2;
        int lamp = lampOffset;

        if (!foreground)
        {
            // Cone: hinged at the table, so a knock swings only its upper part.
            float span = Mathf.Max(1f, apex - top);
            for (int y = top; y < apex; y++)
            {
                float t = (apex - y) / span;
                float sway = lamp * (1f - t), halfWidth = 9f + t * (TableHalfWidth - 12f);
                for (int x = -centre; x < width - centre; x++)
                {
                    float distance = Mathf.Abs(x + .5f - sway);
                    int level = distance < halfWidth - 9f ? 4 : distance < halfWidth - 6f ? 3
                        : distance < halfWidth - 3f ? 2 : distance < halfWidth ? 1 : 0;
                    if (On(level, x, y)) Set(x, y);
                }
            }
            // Lamp: cord, an outlined shade with a solid rim, and the bulb under it.
            for (int y = cordBottom; y < height; y++) Set(lamp, y);
            for (int row = 0; row < 8; row++)
            {
                int y = shadeBottom + row, half = 11 - row;
                if (row == 0 || row == 7) for (int x = lamp - half; x <= lamp + half; x++) Set(x, y);
                else { Set(lamp - half, y);Set(lamp + half, y); }
            }
            for (int x = lamp - 3; x <= lamp + 3; x++) Set(x, shadeBottom - 1);
            for (int x = lamp - 2; x <= lamp + 2; x++) Set(x, shadeBottom - 2);
        }
        else
        {
            // Table slab: top edge, bottom edge and end caps, with a faint lit centre on the apron.
            for (int x = -TableHalfWidth; x < TableHalfWidth; x++) { Set(x, top - 1);Set(x, apron); }
            for (int y = apron; y < top; y++) { Set(-TableHalfWidth, y);Set(TableHalfWidth - 1, y); }
            for (int y = apron + 1; y < top - 1; y++)
                for (int x = -TableHalfWidth + 1; x < TableHalfWidth - 1; x++)
                    if (Mathf.Abs(x + .5f) < 48f && On(1, x, y)) Set(x, y);
            // Legs: two rails each, thinning out on the way down.
            for (int side = -1; side <= 1; side += 2)
            {
                int rail = side < 0 ? -TableHalfWidth + 6 : TableHalfWidth - 10;
                for (int y = 0; y < apron; y++)
                {
                    float depth = (apron - y) / (float)Mathf.Max(1, apron);
                    int level = depth < .3f ? 4 : depth < .55f ? 3 : depth < .8f ? 2 : 1;
                    if (On(level, rail, y)) Set(rail, y);
                    if (On(level, rail + 3, y)) Set(rail + 3, y);
                }
            }
        }

        // One quad per horizontal run keeps the mesh well under the UI vertex limit.
        float originX = bounds.xMin + (bounds.width - width) * .5f, originY = bounds.yMin;
        for (int y = 0; y < height; y++)
        {
            int run = -1, row = y * width;
            for (int x = 0; x <= width; x++)
            {
                bool lit = x < width && grid[row + x];
                if (lit && run < 0) run = x;
                else if (!lit && run >= 0) { Quad(vh, originX + run, originY + y, x - run, 1f, Paper);run = -1; }
            }
        }

        void Set(int x, int y)
        {
            x += centre;
            if (x >= 0 && x < width && y >= 0 && y < height) grid[y * width + x] = true;
        }
    }

    static bool On(int level, int x, int y) => level >= 4 || level > 0 && Bayer[(y & 1) * 2 + (x & 1)] < level;

    static void Quad(VertexHelper vh, float x, float y, float width, float height, Color32 tint)
    {
        if (width <= 0f || height <= 0f) return;
        int first = vh.currentVertCount;
        vh.AddVert(new Vector3(x, y), tint, Vector2.zero);
        vh.AddVert(new Vector3(x, y + height), tint, Vector2.zero);
        vh.AddVert(new Vector3(x + width, y + height), tint, Vector2.zero);
        vh.AddVert(new Vector3(x + width, y), tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);vh.AddTriangle(first, first + 2, first + 3);
    }
}
