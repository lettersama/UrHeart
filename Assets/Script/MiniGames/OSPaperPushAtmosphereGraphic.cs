using UnityEngine;
using UnityEngine.UI;

// Sparse native-pixel meshes: no per-frame textures, particles or extra canvases.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OSPaperPushAtmosphereGraphic : MaskableGraphic
{
    public enum Effect { Rain, Ribbons }
    [SerializeField] Effect effect;
    float clock, flash;
    public void SetFrame(float seconds, float lightning = 0f)
    { clock = seconds;flash = lightning;SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r = rectTransform.rect;
        if (effect == Effect.Rain)
        {
            for (int i = 0; i < 33; i++)
            {
                float speed = 42f + i % 5 * 5f;
                float y = r.yMax - Mathf.Repeat(clock * speed + i * 31.7f, r.height + 8f);
                float x = r.xMin + Mathf.Repeat(i * 79.3f - clock * 6f, r.width);
                Pixel(vh, r, x, y, 1f, 3 + i % 4, .18f + flash * .15f);
                if (i % 3 == 0) Pixel(vh, r, x + 1, y + 3, 1, 2, .12f);
            }
            if (flash > .01f)
            {
                // One thin, forked paper cut; flash stays inside the game screen.
                Vector2[] p = { new Vector2(53,r.height), new Vector2(48,r.height-15), new Vector2(57,r.height-15),
                    new Vector2(43,r.height-32), new Vector2(49,r.height-32), new Vector2(37,r.height-49) };
                for (int i = 1; i < p.Length; i++)
                    Line(vh, r, p[i - 1] + r.min, p[i] + r.min, flash);
            }
        }
        else
        {
            // Two pull-string bursts. Negative ages emit nothing; no ribbons hang in
            // the upper window before the third victory or before the cannon fires.
            for (int i = 0; i < 40; i++)
            {
                bool left = i % 2 == 0;
                float t = clock - (left ? .14f : .23f) - i / 2 % 4 * .018f;
                if (t < 0 || t > 3.5f) continue;
                float originX = left ? r.xMin + 26 : r.xMax - 26;
                float inward = left ? 1 : -1;
                float vx = 40 + (i * 17 % 89), vy = 152 + (i * 13 % 69);
                float x = originX + inward * vx * (1 - Mathf.Exp(-t * .55f)) / .55f;
                float y = r.yMin + 25 + vy * t - 80 * t * t;
                float flutter = Mathf.Sin(t * 7 + i) * Mathf.Min(t * 3, 3);
                x += flutter;
                float alpha = Mathf.Clamp01((3.5f - t) * 2) * .9f;
                int turn = Mathf.FloorToInt(t * 9 + i) % 4;
                float w = turn % 2 == 0 ? 1 : 3;
                float h = i % 4 == 0 ? 7 : 4;
                Pixel(vh, r, x, y, w, h, alpha);
                if (i % 4 == 0)
                    Pixel(vh, r, x + (turn < 2 ? 2 : -2), y + h - 2, 3, 1, alpha);
            }
            for (int side = 0; side < 2; side++)
            {
                float t = clock - (side == 0 ? .14f : .23f);
                if (t < 0 || t > .12f) continue;
                Vector2 origin = new Vector2(side == 0 ? r.xMin + 26 : r.xMax - 26, r.yMin + 25);
                float inward = side == 0 ? 1 : -1;
                for (int ray = 0; ray < 3; ray++)
                {
                    var direction = new Vector2(inward * (ray + 1) * 2, 7 - ray);
                    Line(vh, r, origin + direction * (1 + t * 12), origin + direction * (1.8f + t * 12), 1 - t / .12f);
                }
            }
        }
    }

    void Line(VertexHelper vh, Rect bounds, Vector2 a, Vector2 b, float alpha)
    {
        int count = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)));
        for (int i = 0; i <= count; i++)
        { Vector2 p = Vector2.Lerp(a, b, (float)i / Mathf.Max(1, count));Pixel(vh, bounds, p.x, p.y, 1, 1, alpha); }
    }

    void Pixel(VertexHelper vh, Rect bounds, float x, float y, float width, float height, float alpha)
    {
        float left = Mathf.Max(bounds.xMin, Mathf.Round(x)), bottom = Mathf.Max(bounds.yMin, Mathf.Round(y));
        float right = Mathf.Min(bounds.xMax, Mathf.Round(x) + width), top = Mathf.Min(bounds.yMax, Mathf.Round(y) + height);
        if (right <= left || top <= bottom) return;
        Color c = color;c.a *= alpha;
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(left, bottom), c, Vector2.zero);vh.AddVert(new Vector3(left, top), c, Vector2.zero);
        vh.AddVert(new Vector3(right, top), c, Vector2.zero);vh.AddVert(new Vector3(right, bottom), c, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);vh.AddTriangle(start, start + 2, start + 3);
    }
}
