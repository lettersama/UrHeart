using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The key-item row right under the chapter-3 「家」 view. Every held item is one unit: its word in the
/// map's glyphs, inks and boil, inside dim corner brackets. Units stand in a left-anchored
/// HorizontalLayoutGroup behind a RectMask2D viewport, which shows a fixed window of the row; the mouse
/// wheel over the bar scrolls it sideways. Units are packed tightly: a word in whole tiles, an optional
/// tag such as 「#5」 in small capitals after it, and a few pixels either side. Lower `order` sits further left.
/// Presentation only. The backend decides when an item is picked up or used and calls Add / Remove;
/// when rebuilding from a save it calls them with animate = false.
/// </summary>
[RequireComponent(typeof(RawImage))]
public sealed class OSHomeItemBar : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerDownHandler, IPointerClickHandler,
    IScrollHandler
{
    public const int Height = 26;
    const int W = OSHomeLayout.PixelWidth, H = Height, T = OSHomeLayout.Tile;
    // Inside a unit the word starts a few pixels in and as many follow it (pancake 10-24: fit more in a row).
    const int WordLeft = 5, WordTop = 6, Pad = 2, TagGap = 2;
    const string Letters = "letters";
    static readonly byte[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    [Serializable]
    public sealed class Item
    {
        public string id;
        public string glyphKey;
        [Tooltip("Chinese fallback for the short word, one character per tile.")]
        public string glyph;
        public string labelKey;
        public string label;
        public OSHomeInk ink;
        [Tooltip("Tiles a foreign word takes (up to 8 small letters on 2). Chinese, Japanese and Korean words take one tile per character.")]
        public int width = 2;
        [Tooltip("Optional small text after the word, in 3x5 capitals, such as #5 for a diary page.")]
        public string tag;
        [Tooltip("Place in the row: lower sits further left. Equal values keep the order they arrived in.")]
        public int order;
    }

    [Header("Data")]
    [Tooltip("Optional; found among the siblings when empty. Supplies glyphs and input gating.")]
    [SerializeField] OSHomeView view;
    [Tooltip("Items the backend adds by id. Screwdriver and key reuse the map's localization keys.")]
    [SerializeField] List<Item> catalogue = new List<Item>
    {
        new Item { id = "coffee", glyphKey = "os.home.item.coffee.glyph", glyph = "咖啡",
            labelKey = "os.home.item.coffee.label", label = "主人的咖啡", ink = OSHomeInk.Gray },
        new Item { id = "screwdriver", glyphKey = "os.home.kitchen.screwdriver.glyph", glyph = "起子",
            labelKey = "os.home.kitchen.screwdriver.label", label = "起子", ink = OSHomeInk.Blue },
        new Item { id = "key", glyphKey = "os.home.store.key.glyph", glyph = "钥匙",
            labelKey = "os.home.store.key.label", label = "钥匙", ink = OSHomeInk.Yellow },
        new Item { id = Letters, glyphKey = "os.home.item.letters.glyph", glyph = "信",
            labelKey = "os.home.item.letters.label", label = "收起的信件", ink = OSHomeInk.Gray },
    };

    [Header("Row (logical px)")]
    [Tooltip("Margin left and right of the viewport; the scroll arrows sit in it.")]
    [SerializeField, Min(0)] int edge = 8;
    [Tooltip("How far one wheel notch moves the row.")]
    [SerializeField, Min(1)] int scrollStep = T;
    [SerializeField, Min(1f)] float scrollSharpness = 18f;

    [Header("Motion (seconds, unscaled)")]
    [SerializeField] float appearSeconds = .45f;
    [SerializeField] float vanishSeconds = .45f;
    [Tooltip("A unit opens to its width, or closes after being used up, at this pace.")]
    [SerializeField] float slideSeconds = .2f;
    [Tooltip("A new item's brackets light up this long.")]
    [SerializeField] float flashSeconds = .6f;
    [SerializeField] float clickFlashSeconds = .12f;
    [SerializeField] float boilSeconds = .2f;

    [Header("Palette")]
    [SerializeField] Color32 dim = new Color32(0x4A, 0x47, 0x44, 0xFF);
    [SerializeField] Color32 gray = new Color32(0x87, 0x82, 0x7E, 0xFF);
    [SerializeField] Color32 yellow = new Color32(0xC8, 0xB0, 0x48, 0xFF);
    [SerializeField] Color32 blue = new Color32(0x58, 0x88, 0xC0, 0xFF);
    [SerializeField] Color32 clickInk = new Color32(0xDC, 0xD8, 0xD4, 0xFF);

    [Header("Hover label (optional; created when empty)")]
    [SerializeField] RectTransform tooltip;
    [SerializeField] TMP_Text tooltipLabel;
    [SerializeField] float tooltipFontSize = 12f;

    // Blank is see-through (the bar's own black shows behind the units); Shade is opaque black for the edge fades.
    const byte Blank = 0, Dim = 1, Gray = 2, Red = 3, Yellow = 4, Blue = 5, ClickInk = 6, Shade = 7;

    sealed class Entry
    {
        public Item item;
        public RectTransform rect;
        public LayoutElement layout;
        public RectTransform glyphRect;
        public RawImage glyphImage;
        public Texture2D texture;
        public Color32[] pixels;
        public byte[] ink;
        public int tiles, span; // span: the word and its tag, in pixels
        public float appearStart = -1f, vanishStart = -1f, flashUntil = -1f, openStart = -1f, closeStart = -1f;
        public bool Leaving => vanishStart >= 0f;
        public int Width => span + 2 * WordLeft;
    }

    /// <summary>The player clicked an item. Optional for the backend.</summary>
    public event Action<string> ItemClicked;

    readonly List<Entry> entries = new List<Entry>();
    readonly List<string> ids = new List<string>();
    RawImage image;
    RectTransform viewport, content;
    RawImage hints;
    Texture2D hintTexture;
    Color32[] hintPixels;
    int hintState = -1;
    Color32[] palette;
    float now, dt;
    int boilFrame;
    float scroll, scrollTarget;
    bool pointerInside;
    Vector2 pointerScreen;
    Camera pointerCamera;
    Entry hoverEntry, clickEntry;
    float clickUntil = -1f;
    string tooltipFor;
    OSWindowUnit window;

    /// <summary>Ids from left to right, items being used up excluded. For save checks; flags stay the backend's.</summary>
    public IReadOnlyList<string> Ids
    {
        get
        {
            ids.Clear();
            foreach (var e in entries) if (!e.Leaving) ids.Add(e.item.id);
            return ids;
        }
    }

    public bool IsAnimating
    {
        get
        {
            if (Mathf.Abs(scroll - scrollTarget) > .5f) return true;
            foreach (var e in entries)
                if (e.Leaving || e.openStart >= 0f || (e.appearStart >= 0f && now - e.appearStart < appearSeconds)) return true;
            return false;
        }
    }

    public OSHomeView View => view;
    public bool Has(string id) => Find(id) != null;
    /// <summary>The held item, to rename it in place (its picture follows); null when not held.</summary>
    public Item GetItem(string id) => Find(id)?.item;
    static int OrderOf(Item item) => item.id == Letters ? int.MaxValue : item.order;

    /// <summary>Position in the row from the left (the letters are last); -1 when not held.</summary>
    public int GetSlot(string id)
    {
        int index = 0;
        foreach (var e in entries)
        {
            if (e.Leaving) continue;
            if (e.item.id == id) return index;
            index++;
        }
        return -1;
    }

    /// <summary>Put a catalogue item in the row by its order. False if unknown or already held.</summary>
    public bool Add(string id, bool animate = true)
    {
        EnsureLettersCatalogue();
        foreach (var item in catalogue)
            if (item != null && item.id == id) return Add(item, animate);
        Debug.LogWarning($"[HomeItems] No catalogue item '{id}'.", this);
        return false;
    }

    public bool Add(Item item, bool animate = true)
    {
        if (item == null || string.IsNullOrEmpty(item.id) || Has(item.id)) return false;
        EnsureStructure();
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].Leaving && entries[i].item.id == item.id) Drop(entries[i]);

        int at = entries.Count;
        for (int i = 0; i < entries.Count; i++)
            if (OrderOf(entries[i].item) > OrderOf(item)) { at = i; break; }
        var entry = CreateUnit(item);
        entries.Insert(at, entry);
        for (int i = 0; i < entries.Count; i++) entries[i].rect.SetSiblingIndex(i);
        if (!animate) return true;
        entry.appearStart = now;
        entry.flashUntil = now + flashSeconds;
        entry.openStart = now;
        entry.layout.preferredWidth = 0f;
        Reveal(at);
        return true;
    }

    /// <summary>Use up an item: it dissolves, then its place closes and the items after it move up.</summary>
    public bool Remove(string id, bool animate = true)
    {
        var entry = Find(id);
        if (entry == null) return false;
        if (animate) { entry.vanishStart = now; return true; }
        Drop(entry);
        return true;
    }

    public void Clear()
    {
        for (int i = entries.Count - 1; i >= 0; i--) Drop(entries[i]);
        scroll = scrollTarget = 0f;
        ApplyScroll();
        clickEntry = hoverEntry = null;
        HideTooltip();
    }

    /// <summary>Add a bar right under `view`, same parent and width; the window body must be Height (26) px taller.</summary>
    public static OSHomeItemBar CreateBelow(OSHomeView view, float gap = 0f)
    {
        var viewRect = (RectTransform)view.transform;
        var node = new GameObject("Chapter03HomeItems", typeof(RectTransform), typeof(RawImage));
        var rect = (RectTransform)node.transform;
        rect.SetParent(viewRect.parent, false);
        rect.anchorMin = viewRect.anchorMin; rect.anchorMax = viewRect.anchorMax;
        rect.pivot = new Vector2(.5f, 1f);
        rect.sizeDelta = new Vector2(W, H);
        Vector2 bottom = viewRect.anchoredPosition + new Vector2((.5f - viewRect.pivot.x) * viewRect.rect.width, -viewRect.pivot.y * viewRect.rect.height);
        rect.anchoredPosition = bottom - new Vector2(0f, gap);
        rect.SetSiblingIndex(viewRect.GetSiblingIndex() + 1);
        node.layer = viewRect.gameObject.layer;
        var bar = node.AddComponent<OSHomeItemBar>();
        bar.view = view;
        return bar;
    }

    // ================================================================ lifecycle

    void Awake()
    {
        EnsureLettersCatalogue();
        RefreshPalette();
        EnsureStructure();
    }

    void OnDisable()
    {
        pointerInside = false;
        HideTooltip();
    }

    void OnDestroy()
    {
        foreach (var e in entries) if (e.texture != null) Destroy(e.texture);
        if (hintTexture != null) Destroy(hintTexture);
    }

    void OnValidate() => RefreshPalette();

    void LateUpdate()
    {
        OS_Handle os = OS_Handle.Instance;
        dt = os != null && !os.IsOSVisible ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        now += dt;
        boilFrame = Mathf.FloorToInt(now / boilSeconds) % 3;
        EnsureStructure();
        if (view == null && transform.parent != null) view = transform.parent.GetComponentInChildren<OSHomeView>(true);
        Tick();
        UpdateHover();
        bool click = now < clickUntil;
        foreach (var e in entries) PaintUnit(e, !click && e == hoverEntry, click && e == clickEntry);
        PaintHints();
    }

    void Tick()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (e.openStart >= 0f)
            {
                float p = Ease((now - e.openStart) / slideSeconds);
                e.layout.preferredWidth = Mathf.Round(e.Width * p);
                if (p >= 1f) e.openStart = -1f;
            }
            if (!e.Leaving) continue;
            if (e.closeStart < 0f && now - e.vanishStart >= vanishSeconds) e.closeStart = now;
            if (e.closeStart < 0f) continue;
            float q = Ease((now - e.closeStart) / slideSeconds);
            e.layout.preferredWidth = Mathf.Round(e.Width * (1f - q));
            if (q >= 1f) Drop(e);
        }

        scrollTarget = Mathf.Clamp(scrollTarget, 0f, MaxScroll());
        scroll = Mathf.Abs(scrollTarget - scroll) < .5f ? scrollTarget
            : Mathf.Lerp(scroll, scrollTarget, 1f - Mathf.Exp(-scrollSharpness * dt));
        ApplyScroll();
    }

    static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    Entry Find(string id)
    {
        foreach (var e in entries) if (!e.Leaving && e.item.id == id) return e;
        return null;
    }

    void Drop(Entry e)
    {
        entries.Remove(e);
        if (hoverEntry == e) hoverEntry = null;
        if (clickEntry == e) clickEntry = null;
        if (e.texture != null) Destroy(e.texture);
        if (e.rect != null)
        {
            // Inactive at once, so the row closes up this frame rather than after Destroy runs.
            e.rect.gameObject.SetActive(false);
            Destroy(e.rect.gameObject);
        }
    }

    void EnsureLettersCatalogue()
    {
        if (catalogue == null) catalogue = new List<Item>();
        foreach (var item in catalogue) if (item != null && item.id == Letters) return;
        // Existing serialized bars predate this entry, so the field initializer alone is insufficient.
        catalogue.Add(new Item { id = Letters, glyphKey = "os.home.item.letters.glyph", glyph = "信",
            labelKey = "os.home.item.letters.label", label = "收起的信件", ink = OSHomeInk.Gray });
    }

    // ================================================================ scrolling

    float ViewportWidth => W - 2 * edge;

    float ContentWidth()
    {
        float width = 0f;
        foreach (var e in entries) width += e.layout.preferredWidth;
        return width;
    }

    float MaxScroll() => Mathf.Max(0f, ContentWidth() - ViewportWidth);

    void ApplyScroll()
    {
        if (content != null) content.anchoredPosition = new Vector2(-Mathf.Round(scroll), 0f);
    }

    /// <summary>Scroll just enough to show the whole unit at `index`, at its final width.</summary>
    void Reveal(int index)
    {
        float left = 0f;
        for (int i = 0; i < index; i++) left += entries[i].layout.preferredWidth;
        float right = left + entries[index].Width;
        float total = ContentWidth() - entries[index].layout.preferredWidth + entries[index].Width;
        if (left < scrollTarget) scrollTarget = left;
        else if (right > scrollTarget + ViewportWidth) scrollTarget = right - ViewportWidth;
        scrollTarget = Mathf.Clamp(scrollTarget, 0f, Mathf.Max(0f, total - ViewportWidth));
    }

    public void OnScroll(PointerEventData e)
    {
        Track(e);
        if (!AcceptsInput()) return;
        Vector2 d = e.scrollDelta;
        // Wheel down or right moves on to later items; one notch is one tile, whatever the device reports.
        float step = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? d.x : -d.y;
        if (step == 0f) return;
        scrollTarget = Mathf.Clamp(scrollTarget + Mathf.Sign(step) * scrollStep, 0f, MaxScroll());
        e.Use();
    }

    // ================================================================ input

    public void OnPointerEnter(PointerEventData e) { pointerInside = true; Track(e); }
    public void OnPointerExit(PointerEventData e) { pointerInside = false; hoverEntry = null; HideTooltip(); }
    public void OnPointerMove(PointerEventData e) => Track(e);

    public void OnPointerDown(PointerEventData e)
    {
        Track(e);
        if (e.button == PointerEventData.InputButton.Left && window != null)
            OS_Handle.Instance?.NotifyWindowPointerPressed(window);
        e.Use();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
        Track(e);
        var entry = EntryAt(e.position, e.pressEventCamera);
        if (!AcceptsInput() || entry == null) return;
        clickEntry = entry; clickUntil = now + clickFlashSeconds;
        ItemClicked?.Invoke(entry.item.id);
    }

    void Track(PointerEventData e)
    {
        pointerScreen = e.position;
        pointerCamera = e.enterEventCamera != null ? e.enterEventCamera : e.pressEventCamera;
    }

    bool AcceptsInput()
    {
        if (view != null && !view.InputEnabled) return false;
        OS_Handle os = OS_Handle.Instance;
        if (os != null && (!os.IsOSVisible || os.IsSystemInputLocked)) return false;
        return window == null || (window.IsOpen && !window.IsMinimized && !window.IsOpening && !window.IsClosing);
    }

    /// <summary>The settled unit whose bracket box is under the pointer, inside the visible window.</summary>
    Entry EntryAt(Vector2 screen, Camera camera)
    {
        if (viewport == null || !RectTransformUtility.RectangleContainsScreenPoint(viewport, screen, camera)) return null;
        foreach (var e in entries)
        {
            if (e.Leaving || e.openStart >= 0f) continue;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(e.rect, screen, camera, out Vector2 local)) continue;
            Rect r = e.rect.rect;
            float px = local.x - r.xMin, py = r.yMax - local.y;
            if (px >= WordLeft - Pad && px < WordLeft + e.span + Pad && py >= WordTop - Pad && py < WordTop + T + Pad)
                return e;
        }
        return null;
    }

    // ================================================================ painting

    void PaintUnit(Entry e, bool hovered, bool clicked)
    {
        if (view == null) return;
        var item = e.item;
        string text = Localize(item.glyphKey, item.glyph);
        int tiles = TilesFor(text, item);
        var tag = view.Glyphs.LayoutTag(item.tag);
        int span = SpanFor(tiles, tag);
        if (tiles != e.tiles || span != e.span) Resize(e, tiles, span);
        var word = view.Glyphs.Layout(text, tiles);
        Array.Clear(e.ink, 0, e.ink.Length);
        int w = e.Width;

        // Appear: pixels switch on through the 4x4 threshold; vanish runs it backwards.
        int threshold = 16;
        if (e.Leaving) threshold = 16 - Mathf.Clamp(Mathf.RoundToInt((now - e.vanishStart) / vanishSeconds * 16f), 0, 16);
        else if (e.appearStart >= 0f) threshold = Mathf.Clamp(Mathf.RoundToInt((now - e.appearStart) / appearSeconds * 16f), 0, 16);
        if (!e.Leaving && threshold >= 16) e.appearStart = -1f;

        int x0 = WordLeft - Pad, x1 = WordLeft + span + Pad - 1, y0 = WordTop - Pad, y1 = WordTop + T + Pad - 1;
        if (clicked || hovered)
        {
            // As on the map: hover = dotted box, click = solid box.
            byte color = clicked ? ClickInk : Gray;
            for (int x = x0; x <= x1; x++)
                if (clicked || ((x - x0) & 1) == 0) { Put(e, w, x, y0, color, 16); Put(e, w, x, y1, color, 16); }
            for (int y = y0; y <= y1; y++)
                if (clicked || ((y - y0) & 1) == 0) { Put(e, w, x0, y, color, 16); Put(e, w, x1, y, color, 16); }
        }
        else
        {
            byte frame = !e.Leaving && now < e.flashUntil && Blink(e) ? ClickInk : Dim;
            const int arm = 3;
            for (int i = 0; i < arm; i++)
            {
                Put(e, w, x0 + i, y0, frame, threshold); Put(e, w, x1 - i, y0, frame, threshold);
                Put(e, w, x0 + i, y1, frame, threshold); Put(e, w, x1 - i, y1, frame, threshold);
                Put(e, w, x0, y0 + i, frame, threshold); Put(e, w, x1, y0 + i, frame, threshold);
                Put(e, w, x0, y1 - i, frame, threshold); Put(e, w, x1, y1 - i, frame, threshold);
            }
        }

        byte ink = InkOf(item.ink);
        uint seed = Hash(item.id);
        for (int tile = 0; tile < tiles; tile++)
        {
            uint boil = Hash(seed + (uint)tile * 7919u, boilFrame);
            for (int y = 0; y < T; y++)
            {
                int shift = BoilShift(boil, y);
                for (int x = 0; x < T; x++)
                {
                    int wx = tile * T + x;
                    if (word.Get(wx, y)) Put(e, w, WordLeft + wx + shift, WordTop + y, ink, threshold);
                }
            }
        }
        int tagLeft = WordLeft + tiles * T + TagGap;
        for (int y = 0; y < tag.height; y++)
            for (int x = 0; x < tag.width; x++)
                if (tag.Get(x, y)) Put(e, w, tagLeft + x, WordTop + y, ink, threshold);

        for (int y = 0; y < H; y++)
        {
            int src = y * w, dst = (H - 1 - y) * w;
            for (int x = 0; x < w; x++) e.pixels[dst + x] = palette[e.ink[src + x]];
        }
        e.texture.SetPixels32(e.pixels);
        e.texture.Apply(false, false);
    }

    static void Put(Entry e, int w, int x, int y, byte color, int threshold)
    {
        if (x < 0 || y < 0 || x >= w || y >= H || Bayer[(y & 3) * 4 + (x & 3)] >= threshold) return;
        e.ink[y * w + x] = color;
    }

    /// <summary>Brackets lit during the flash, in short pulses that slow down (the 「咚」 rhythm, quieter).</summary>
    bool Blink(Entry e)
    {
        float t = now - (e.flashUntil - flashSeconds);
        return t < .12f || (t > .24f && t < .36f) || t > .5f;
    }

    /// <summary>
    /// Cut edges: when part of the row is out of the window on a side, the last 6 px fade out through
    /// black dither and a small arrow sits in the margin. Repainted only when that changes.
    /// </summary>
    void PaintHints()
    {
        if (hints == null) return;
        bool left = scroll > .5f, right = scroll < MaxScroll() - .5f;
        int state = (left ? 1 : 0) | (right ? 2 : 0);
        if (state == hintState) return;
        hintState = state;
        for (int i = 0; i < hintPixels.Length; i++) hintPixels[i] = palette[Blank];
        const int fade = 6, mid = WordTop + T / 2 - 1;
        for (int i = 0; i < fade; i++)
        {
            int level = (fade - i) * 16 / (fade + 1);
            for (int y = 0; y < H; y++)
            {
                if (left) HintShade(edge + i, y, level);
                if (right) HintShade(W - edge - 1 - i, y, level);
            }
        }
        for (int dx = 0; dx < 3; dx++)
            for (int dy = -dx; dy <= dx; dy++)
            {
                if (left) HintPixel(edge / 2 - 1 + dx, mid + dy, palette[Gray]);
                if (right) HintPixel(W - edge / 2 - dx, mid + dy, palette[Gray]);
            }
        hintTexture.SetPixels32(hintPixels);
        hintTexture.Apply(false, false);
    }

    void HintShade(int x, int y, int level)
    {
        if (Bayer[(y & 3) * 4 + (x & 3)] < level) HintPixel(x, y, palette[Shade]);
    }

    void HintPixel(int x, int y, Color32 color)
    {
        if (x >= 0 && y >= 0 && x < W && y < H) hintPixels[(H - 1 - y) * W + x] = color;
    }

    /// <summary>Same boil as the map: 1-2 bands of 3-6 rows slip by one pixel; three frames.</summary>
    static int BoilShift(uint boil, int row)
    {
        int bands = (boil & 3) == 0 ? 2 : 1;
        for (int b = 0; b < bands; b++)
        {
            uint h = Hash(boil, b + 11);
            int start = (int)(h % 13), length = 3 + (int)((h >> 8) % 4);
            if (row >= start && row < start + length) return ((h >> 16) & 1) == 0 ? -1 : 1;
        }
        return 0;
    }

    static int TilesFor(string text, Item item)
    {
        if (!string.IsNullOrEmpty(text) && OSHomeGlyphBook.IsWide(text))
            return Mathf.Max(1, new StringInfo(text).LengthInTextElements);
        return Mathf.Max(1, item.width);
    }

    // ================================================================ hover label

    void UpdateHover()
    {
        hoverEntry = pointerInside && AcceptsInput() ? EntryAt(pointerScreen, pointerCamera) : null;
        if (hoverEntry == null) { HideTooltip(); return; }
        var item = hoverEntry.item;
        ShowTooltip(hoverEntry, Localize(item.labelKey, item.label));
    }

    void ShowTooltip(Entry entry, string text)
    {
        if (string.IsNullOrEmpty(text) || !EnsureTooltip()) { HideTooltip(); return; }
        if (tooltipFor != entry.item.id || tooltipLabel.text != text)
        {
            tooltipFor = entry.item.id;
            OSLocalizedFontUtility.ApplyCurrent(tooltipLabel);
            tooltipLabel.text = text;
            Vector2 size = tooltipLabel.GetPreferredValues(text);
            tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Ceil(size.x) + 8f);
            tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(13f, Mathf.Ceil(size.y) + 3f));
        }
        // Centred over the unit's brackets, over the bottom of the room: the bar is too low to hold it.
        var root = (RectTransform)transform;
        Rect r = entry.rect.rect, bar = root.rect;
        Vector3 world = entry.rect.TransformPoint(new Vector3(r.xMin + WordLeft + entry.span * .5f, r.yMax - (WordTop - Pad), 0f));
        Vector2 local = root.InverseTransformPoint(world);
        float half = tooltip.rect.width * .5f;
        float cx = Mathf.Clamp(local.x, bar.xMin + half, bar.xMax - half);
        // anchoredPosition counts from the anchor point, not from the bar's pivot (which is at its top).
        Vector2 anchor = new Vector2(Mathf.Lerp(bar.xMin, bar.xMax, tooltip.anchorMin.x), Mathf.Lerp(bar.yMin, bar.yMax, tooltip.anchorMin.y));
        tooltip.anchoredPosition = new Vector2(Mathf.Round(cx - anchor.x), Mathf.Round(local.y + 2f - anchor.y));
        if (!tooltip.gameObject.activeSelf) tooltip.gameObject.SetActive(true);
    }

    void HideTooltip()
    {
        tooltipFor = null;
        if (tooltip != null && tooltip.gameObject.activeSelf) tooltip.gameObject.SetActive(false);
    }

    bool EnsureTooltip()
    {
        if (tooltip != null && tooltipLabel != null) return true;
        if (view == null || view.Glyphs.Font == null) return false;
        var frame = new GameObject("HoverLabel", typeof(RectTransform), typeof(Image));
        frame.layer = gameObject.layer;
        tooltip = (RectTransform)frame.transform;
        tooltip.SetParent(transform, false);
        tooltip.SetAsLastSibling();
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(.5f, .5f);
        tooltip.pivot = new Vector2(.5f, 0f);
        var border = frame.GetComponent<Image>();
        border.color = gray; border.raycastTarget = false;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.layer = gameObject.layer;
        var fillRect = (RectTransform)fill.transform;
        fillRect.SetParent(tooltip, false);
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.one; fillRect.offsetMax = -Vector2.one;
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = Color.black; fillImage.raycastTarget = false;
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.layer = gameObject.layer;
        var labelRect = (RectTransform)label.transform;
        labelRect.SetParent(tooltip, false);
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        tooltipLabel = label.GetComponent<TextMeshProUGUI>();
        tooltipLabel.font = view.Glyphs.Font;
        tooltipLabel.fontSize = tooltipFontSize;
        tooltipLabel.color = gray;
        tooltipLabel.alignment = TextAlignmentOptions.Center;
        tooltipLabel.textWrappingMode = TextWrappingModes.NoWrap;
        tooltipLabel.raycastTarget = false;
        frame.SetActive(false);
        return true;
    }

    // ================================================================ setup

    /// <summary>
    /// Bar (black; takes the pointer and the wheel) > Viewport (RectMask2D, inset by `edge`) > Content
    /// (HorizontalLayoutGroup + ContentSizeFitter, anchored left) > one unit per item. ScrollHints and the
    /// hover label sit above the viewport.
    /// </summary>
    void EnsureStructure()
    {
        if (palette == null) RefreshPalette();
        if (viewport != null && content != null && hints != null) return;
        image = GetComponent<RawImage>();
        image.texture = null;
        image.color = Color.black;
        image.raycastTarget = true;
        window = GetComponentInParent<OSWindowUnit>();
        var root = (RectTransform)transform;

        viewport = root.Find("Viewport") as RectTransform;
        if (viewport == null)
        {
            viewport = NewChild("Viewport", root);
            viewport.gameObject.AddComponent<RectMask2D>();
        }
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.pivot = new Vector2(.5f, .5f);
        viewport.offsetMin = new Vector2(edge, 0f); viewport.offsetMax = new Vector2(-edge, 0f);
        viewport.SetSiblingIndex(0);

        content = viewport.Find("Content") as RectTransform;
        if (content == null)
        {
            content = NewChild("Content", viewport);
            var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.spacing = 0f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }
        content.anchorMin = Vector2.zero; content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, .5f);
        content.sizeDelta = new Vector2(content.sizeDelta.x, 0f);
        ApplyScroll();

        var hintRect = root.Find("ScrollHints") as RectTransform;
        if (hintRect == null) hintRect = NewChild("ScrollHints", root);
        hintRect.anchorMin = Vector2.zero; hintRect.anchorMax = Vector2.one;
        hintRect.offsetMin = hintRect.offsetMax = Vector2.zero;
        hintRect.SetSiblingIndex(1);
        hints = hintRect.GetComponent<RawImage>();
        if (hints == null) hints = hintRect.gameObject.AddComponent<RawImage>();
        if (hintTexture == null) hintTexture = NewTexture(W, "Chapter03HomeItemHints");
        hintPixels = new Color32[W * H];
        hintState = -1;
        hints.texture = hintTexture;
        hints.color = Color.white;
        hints.raycastTarget = false;
    }

    Entry CreateUnit(Item item)
    {
        var e = new Entry { item = item };
        e.rect = NewChild("Item_" + item.id, content);
        e.layout = e.rect.gameObject.AddComponent<LayoutElement>();
        e.layout.preferredHeight = H;
        e.layout.flexibleWidth = 0f;
        e.glyphRect = NewChild("Glyph", e.rect);
        e.glyphRect.anchorMin = e.glyphRect.anchorMax = new Vector2(0f, .5f);
        e.glyphRect.pivot = new Vector2(0f, .5f);
        e.glyphRect.anchoredPosition = Vector2.zero;
        e.glyphImage = e.glyphRect.gameObject.AddComponent<RawImage>();
        e.glyphImage.raycastTarget = false;
        e.glyphImage.color = Color.white;
        int tiles = TilesFor(Localize(item.glyphKey, item.glyph), item);
        Resize(e, tiles, SpanFor(tiles, view != null ? view.Glyphs.LayoutTag(item.tag) : null));
        return e;
    }

    static int SpanFor(int tiles, OSHomeGlyphBook.Word tag) =>
        tiles * T + (tag != null && tag.width > 0 ? TagGap + tag.width : 0);

    /// <summary>A unit is its span plus a margin either side; its picture keeps that size while the unit opens or closes.</summary>
    void Resize(Entry e, int tiles, int span)
    {
        e.tiles = tiles; e.span = span;
        int w = e.Width;
        if (e.texture != null) Destroy(e.texture);
        e.texture = NewTexture(w, "Chapter03HomeItem_" + e.item.id);
        e.pixels = new Color32[w * H];
        e.ink = new byte[w * H];
        e.glyphImage.texture = e.texture;
        e.glyphRect.sizeDelta = new Vector2(w, H);
        if (e.openStart < 0f && e.closeStart < 0f) e.layout.preferredWidth = w;
    }

    RectTransform NewChild(string name, Transform parent)
    {
        var node = new GameObject(name, typeof(RectTransform));
        node.layer = gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    static Texture2D NewTexture(int width, string name) =>
        new Texture2D(width, H, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };

    void RefreshPalette()
    {
        if (palette == null || palette.Length != 8) palette = new Color32[8];
        palette[Blank] = new Color32(0, 0, 0, 0);
        palette[Dim] = dim;
        palette[Gray] = gray;
        palette[Red] = OSPalette.Red;
        palette[Yellow] = yellow;
        palette[Blue] = blue;
        palette[ClickInk] = clickInk;
        palette[Shade] = new Color32(0, 0, 0, 255);
        hintState = -1;
    }

    static byte InkOf(OSHomeInk value)
    {
        switch (value)
        {
            case OSHomeInk.Red: return Red;
            case OSHomeInk.Yellow: return Yellow;
            case OSHomeInk.Blue: return Blue;
            default: return Gray;
        }
    }

    static string Localize(string key, string fallback)
    {
        var data = GameDataManager.Instance;
        if (!string.IsNullOrEmpty(key) && data != null && data.TryGetLocalizedText(key, out string text)
            && !string.IsNullOrEmpty(text)) return text;
        return fallback ?? key ?? string.Empty;
    }

    static uint Hash(string text, int salt = 0)
    {
        uint h = 2166136261u;
        if (text != null) foreach (char c in text) { h ^= c; h *= 16777619u; }
        return Hash(h, salt);
    }

    static uint Hash(uint value, int salt)
    {
        uint h = value ^ (uint)salt * 0x9E3779B9u;
        h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
        return h;
    }
}
