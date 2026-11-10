using UnityEngine;

// The house camera's on-screen display, drawn in the slide's own source pixels
// so it shares the drawing's grid: camera name, a running clock, a blinking REC
// and, while she is in view, the motion tracker's corners. The slide shader lays
// it over the treatment, so it stays lit when the picture darkens or the shutter closes.
public sealed partial class OSChapter01EndingDirector
{
    static readonly Color32 OsdInk = new Color32(135, 130, 126, 255), OsdEdge = new Color32(0, 0, 0, 255);
    const string OsdGlyphs = "0123456789:- ACEGIMNORS";
    // 5x7 glyphs, one byte per row, high bit on the left.
    static readonly byte[] OsdRows =
    {
        0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E,  0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E,
        0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F,  0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E,
        0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02,  0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E,
        0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E,  0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08,
        0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E,  0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C,
        0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x00,  0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11,
        0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E,  0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F,
        0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F,  0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E,
        0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11,  0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11,
        0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E,  0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11,
        0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E,
    };
    const int OsdMargin = 8, OsdBracket = 6, OsdTrackPad = 4;

    Texture2D osdTexture;
    Color32[] osdPixels;
    bool[] osdInk;
    string osdDrawn;

    void UpdateSlideOsd(OSChapter01EndingData.Slide slide, Sprite sprite, float elapsed, bool blink = true)
    {
        if (slideTreatment == null) return;
        bool show = slide != null && sprite != null && !string.IsNullOrEmpty(slide.osdCamera);
        slideTreatment.SetFloat("_OsdOn", show ? 1f : 0f);
        if (!show) return;

        int width = Mathf.RoundToInt(sprite.rect.width), height = Mathf.RoundToInt(sprite.rect.height);
        int clock = slide.osdClockSeconds + Mathf.FloorToInt(Mathf.Max(0f, elapsed));
        bool rec = !blink || Mathf.Repeat(slideClock, 1f) < .5f;
        string key = slide.osdCamera + "|" + clock + "|" + rec + "|" + slide.osdNightVision + "|" + slide.osdTrack + "|" + width + "x" + height;
        if (key == osdDrawn && osdTexture != null) return;
        osdDrawn = key;

        if (osdTexture == null || osdTexture.width != width || osdTexture.height != height)
        {
            if (osdTexture != null) Destroy(osdTexture);
            osdTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                { name = "Chapter01SlideOsd (runtime)", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            osdPixels = new Color32[width * height];
            osdInk = new bool[width * height];
        }
        System.Array.Clear(osdInk, 0, osdInk.Length);

        DrawOsdText(slide.osdCamera.ToUpperInvariant(), OsdMargin, OsdMargin, false);
        DrawOsdText(FormatOsdClock(clock), width - OsdMargin, OsdMargin, true);
        int bottom = height - OsdMargin - 7;
        if (rec) DrawOsdDot(OsdMargin, bottom + 1);
        DrawOsdText("REC", OsdMargin + 8, bottom, false);
        if (slide.osdNightVision) DrawOsdText("IR", width - OsdMargin, bottom, true);
        if (slide.osdTrack.width > 0f) DrawOsdTrack(slide.osdTrack);

        // Ink in paper grey with a one-pixel black edge: two colours, readable on any shot.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (height - 1 - y) * width + x;
                osdPixels[i] = osdInk[i] ? OsdInk : OsdTouchesInk(x, y) ? OsdEdge : default;
            }
        osdTexture.SetPixels32(osdPixels);
        osdTexture.Apply(false);
        slideTreatment.SetTexture("_OsdTex", osdTexture);
    }

    static string FormatOsdClock(int seconds)
    {
        seconds %= 24 * 3600;
        return (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    // Top-left origin, matching how the drawing is authored.
    void OsdPlot(int x, int y)
    {
        int width = osdTexture.width, height = osdTexture.height;
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        osdInk[(height - 1 - y) * width + x] = true;
    }

    bool OsdTouchesInk(int x, int y)
    {
        int width = osdTexture.width, height = osdTexture.height;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px >= 0 && py >= 0 && px < width && py < height && osdInk[(height - 1 - py) * width + px]) return true;
            }
        return false;
    }

    void DrawOsdText(string text, int x, int y, bool alignRight)
    {
        if (alignRight) x -= text.Length * 6 - 1;
        for (int c = 0; c < text.Length; c++)
        {
            int glyph = OsdGlyphs.IndexOf(text[c]);
            if (glyph < 0) continue;
            for (int row = 0; row < 7; row++)
            {
                int bits = OsdRows[glyph * 7 + row];
                for (int column = 0; column < 5; column++)
                    if ((bits & (0x10 >> column)) != 0) OsdPlot(x + c * 6 + column, y + row);
            }
        }
    }

    void DrawOsdDot(int x, int y)
    {
        for (int row = 0; row < 5; row++)
            for (int column = 0; column < 5; column++)
                if ((row != 0 && row != 4) || (column != 0 && column != 4)) OsdPlot(x + column, y + row);
    }

    void DrawOsdTrack(Rect track)
    {
        int width = osdTexture.width, height = osdTexture.height;
        int left = Mathf.Max(2, Mathf.RoundToInt(track.xMin) - OsdTrackPad);
        int top = Mathf.Max(2, Mathf.RoundToInt(track.yMin) - OsdTrackPad);
        int right = Mathf.Min(width - 3, Mathf.RoundToInt(track.xMax) - 1 + OsdTrackPad);
        int bottom = Mathf.Min(height - 3, Mathf.RoundToInt(track.yMax) - 1 + OsdTrackPad);
        for (int k = 0; k < OsdBracket; k++)
        {
            OsdPlot(left + k, top); OsdPlot(left, top + k);
            OsdPlot(right - k, top); OsdPlot(right, top + k);
            OsdPlot(left + k, bottom); OsdPlot(left, bottom - k);
            OsdPlot(right - k, bottom); OsdPlot(right, bottom - k);
        }
    }

    void ReleaseSlideOsd()
    {
        if (osdTexture != null) Destroy(osdTexture);
        osdTexture = null;osdPixels = null;osdInk = null;osdDrawn = null;
    }
}
