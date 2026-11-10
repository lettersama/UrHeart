using UnityEngine;

/// <summary>
/// Close-up on 「我」 for the yard's last scene (pancake 10-25): the map shows a
/// quarter of itself at exactly 2x, the crop following 「我」 pixel by pixel and
/// stopping at the edges. It never passes through an in-between scale: the cut in
/// or out happens behind a short Bayer blink to black. Input is off while close.
/// </summary>
public sealed partial class OSHomeView
{
    const int ZoomFactor = 2;
    const float ZoomBlinkSeconds = .32f;

    bool zoomTarget, zoomShown;
    float zoomBlink = -1f;

    /// <summary>True from the call until the view is back at full size.</summary>
    public bool Zoomed => zoomTarget || zoomShown;

    /// <summary>Blink to the 2x close-up or back; `instant` skips the blink (restoring a save).</summary>
    public void SetZoom(bool on, bool instant = false)
    {
        if (instant) { zoomTarget = zoomShown = on; zoomBlink = -1f; ApplyZoomRect(); return; }
        if (on == zoomTarget && zoomBlink < 0f && zoomShown == on) return;
        zoomTarget = on;
        if (zoomBlink < 0f) zoomBlink = 0f;
    }

    // After Paint, before Upload: blink, swap halfway, keep the crop on 「我」.
    void TickZoom()
    {
        if (zoomBlink >= 0f)
        {
            zoomBlink += dt;
            float half = ZoomBlinkSeconds * .5f;
            bool swapped = zoomBlink >= half && zoomShown != zoomTarget;
            if (swapped) zoomShown = zoomTarget;
            // 0 → 1 → 0 over the blink: how much of the picture the dither has eaten.
            float cover = zoomBlink < half ? zoomBlink / half : 1f - (zoomBlink - half) / half;
            // A frame can step across the exact midpoint. Hide the UV cut fully
            // on that frame instead of exposing it through a partly faded dither.
            if (swapped) cover = 1f;
            if (zoomBlink >= ZoomBlinkSeconds) { zoomBlink = -1f; cover = 0f; }
            BlinkToBlack(Mathf.Clamp01(cover));
        }
        ApplyZoomRect();
    }

    void BlinkToBlack(float cover)
    {
        int strength = Mathf.RoundToInt(cover * 16f);
        if (strength <= 0) return;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (Bayer[(y & 3) * 4 + (x & 3)] < strength) ink[y * W + x] = Black;
    }

    void ApplyZoomRect()
    {
        if (image == null) return;
        if (!zoomShown) { image.uvRect = new Rect(0f, 0f, 1f, 1f); return; }
        int w = W / ZoomFactor, h = H / ZoomFactor;
        Vector2 me = MePixel() + new Vector2(T * .5f, T * .5f);
        int x0 = Mathf.Clamp(Mathf.RoundToInt(me.x) - w / 2, 0, W - w);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(me.y) - h / 2, 0, H - h); // rows from the top
        image.uvRect = new Rect(x0 / (float)W, (H - y0 - h) / (float)H, w / (float)W, h / (float)H);
    }
}
