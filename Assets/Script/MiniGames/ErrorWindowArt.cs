using UnityEngine;

/// <summary>
/// Procedural 1-bit "error window" sprites for the quake. Each seed combines a size,
/// a title-bar style, an icon, a body layout and an optional damaged edge, so the
/// falling windows read as many different broken system dialogs. Ink #87827e on black.
/// </summary>
public static class ErrorWindowArt
{
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 0xff);

    sealed class Canvas
    {
        public readonly int W, H;
        readonly Color[] px;
        public Canvas(int w, int h) { W = w; H = h; px = new Color[w * h]; }
        // top-down coordinates
        public void Set(int x, int y, bool ink) { if (x >= 0 && y >= 0 && x < W && y < H) px[(H - 1 - y) * W + x] = ink ? Ink : Color.black; }
        public void Clear(int x, int y) { if (x >= 0 && y >= 0 && x < W && y < H) px[(H - 1 - y) * W + x] = Color.clear; }
        public void Fill(int x0, int y0, int x1, int y1, bool ink) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, ink); }
        public void HLine(int x0, int x1, int y, int step = 1) { for (int x = x0; x <= x1; x += step) Set(x, y, true); }
        public void VLine(int x, int y0, int y1, int step = 1) { for (int y = y0; y <= y1; y += step) Set(x, y, true); }
        public void Rect(int x0, int y0, int x1, int y1) { HLine(x0, x1, y0); HLine(x0, x1, y1); VLine(x0, y0, y1); VLine(x1, y0, y1); }
        public void Stamp(string[] rows, int ox, int oy) { for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++) if (rows[y][x] == 'I') Set(ox + x, oy + y, true); }
        public Sprite ToSprite(string name)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            tex.SetPixels(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
        }
    }

    static readonly string[][] Icons =
    {
        new[] { ".II.", ".II.", ".II.", ".II.", ".II.", "....", ".II." },                    // !
        new[] { ".III.", "I...I", "II.II", "I.I.I", "II.II", "I...I", ".III." },              // circled cross
        new[] { ".III.", "I...I", "...I.", "..I..", "..I..", ".....", "..I.." },              // ?
        new[] { "...I...", "..I.I..", "..I.I..", ".I.I.I.", ".I...I.", "I..I..I", "IIIIIII" }, // warning triangle
        new[] { "IIIII", ".I.I.", "..I..", ".I.I.", "IIIII" },                                // hourglass
        new[] { "III..", "I..I.", "I...I", "I.I.I", "I..II", "I.I.I", "IIIII" },              // torn file
        new[] { "I.I..", "I.I..", "IIIII", ".III.", "..I..", ".....", "..I.I" },              // unplugged
    };

    public static Sprite Build(int seed, int index)
    {
        var r = new System.Random(seed);
        int w = 30 + r.Next(14) * 2 + (index % 3) * 4, h = 18 + r.Next(8) * 2;
        var c = new Canvas(w, h);
        c.Fill(0, 0, w - 1, h - 1, false);
        c.Rect(0, 0, w - 1, h - 1);

        // Title bar: solid with black dots, dotted dither, or plain line with control squares.
        int titleStyle = index % 3, bar = 6;
        if (titleStyle == 0)
        {
            c.Fill(1, 1, w - 2, bar - 1, true);
            for (int x = 3; x < w - 10; x += 2) c.Set(x, 3, false);
        }
        else if (titleStyle == 1)
        {
            for (int y = 1; y < bar; y++) for (int x = 1; x < w - 1; x++) if ((x + y) % 2 == 0) c.Set(x, y, true);
        }
        else
        {
            for (int i = 0; i < 3; i++) c.Fill(w - 5 - i * 3, 2, w - 4 - i * 3, 3, true);
            c.HLine(3, 3 + r.Next(6, 12), 3);
        }
        c.HLine(0, w - 1, bar);
        if (titleStyle != 2)
        {
            // close box
            for (int i = 0; i < 3; i++) { c.Set(w - 6 + i, 2 + i, titleStyle == 0 ? false : true); c.Set(w - 4 - i, 2 + i, titleStyle == 0 ? false : true); }
        }

        // Icon on the left of the body.
        var icon = Icons[(index + r.Next(2)) % Icons.Length];
        int bodyTop = bar + 3;
        c.Stamp(icon, 3, bodyTop);
        int textX = 3 + icon[0].Length + 3;

        // Body: garbled text lines, a stuck progress bar, a hex/memory grid, or a button row.
        int body = (index / 3 + r.Next(2)) % 4;
        int bottom = h - 3;
        if (body == 0 || bottom - bodyTop < 7)
        {
            for (int y = bodyTop + 1, l = 0; y < bottom; y += 3, l++)
            {
                int end = w - 4 - (r.Next(3) == 0 ? r.Next(4, 12) : 0);
                for (int x = textX; x < end; x++) if (r.Next(6) != 0) c.Set(x, y, true);
            }
        }
        else if (body == 1)
        {
            int x0 = textX, x1 = w - 5, y0 = bodyTop + 1;
            for (int x = x0; x < x1 - 6; x++) if (r.Next(5) != 0) c.Set(x, y0, true);
            c.Rect(x0, y0 + 3, x1, y0 + 7);
            int filled = x0 + 2 + (int)((x1 - x0 - 4) * (.3f + .5f * (float)r.NextDouble()));
            for (int y = y0 + 5; y <= y0 + 5; y++) for (int x = x0 + 2; x <= filled; x++) c.Set(x, y, true);
            for (int x = filled + 1; x < x1 - 1; x += 2) c.Set(x, y0 + 5, true);
        }
        else if (body == 2)
        {
            for (int y = bodyTop; y < bottom; y += 2)
                for (int x = textX; x < w - 3; x += 2)
                    if (r.Next(3) != 0) c.Set(x, y, true);
        }
        else
        {
            for (int x = textX; x < w - 4; x++) if (r.Next(5) != 0) c.Set(x, bodyTop + 1, true);
            int buttons = w > 46 ? 2 : 1, bw = 11, by = bottom - 4;
            for (int b = 0; b < buttons; b++)
            {
                int bx = w - 4 - (b + 1) * (bw + 2);
                c.Rect(bx, by, bx + bw, by + 4);
                c.HLine(bx + 3, bx + bw - 3, by + 2);
            }
        }

        // Damage: some windows lose a corner or a stretch of frame, like the wallpaper fragments.
        int damage = r.Next(4);
        if (damage == 1)
        {
            int size = 4 + r.Next(5);
            bool right = r.Next(2) == 0;
            for (int y = h - size; y < h; y++)
                for (int x = 0; x < size - (h - 1 - y); x++)
                    c.Clear(right ? w - 1 - x : x, y);
        }
        else if (damage == 2)
        {
            int start = 4 + r.Next(Mathf.Max(1, w - 16)), len = 5 + r.Next(6);
            for (int x = start; x < start + len && x < w - 1; x++) c.Clear(x, h - 1);
        }
        else if (damage == 3)
        {
            // one glitched row slid sideways
            int y = bodyTop + r.Next(Mathf.Max(1, bottom - bodyTop));
            int shift = r.Next(2) == 0 ? 2 : -2;
            var row = new bool[w];
            for (int x = 0; x < w; x++) row[x] = false;
            for (int x = 1; x < w - 1; x++) c.Set(Mathf.Clamp(x + shift, 1, w - 2), y, (x * 3 + y) % 4 != 0);
        }
        return c.ToSprite("QuakeError" + index);
    }
}
