using System;
using TMPro;
using UnityEngine;

public sealed partial class OSChapter03StoryPresentation
{
    const string UiResourcePath = "Chapter03Story/UI/";
    static readonly Color ModalInk = new Color32(0x87, 0x82, 0x7E, 255);
    Material pixelImageMaterial;

    Material RequirePixelImageMaterial()
    {
        if (pixelImageMaterial != null) return pixelImageMaterial;
        const string resource = "Chapter03Story/Chapter03PixelImage";
        var shader = Resources.Load<Shader>(resource);
        if (shader == null) throw new InvalidOperationException("Missing Chapter 3 modal image shader: " + resource);
        pixelImageMaterial = new Material(shader)
        {
            name = "Chapter03 modal pixels (owned)",
            hideFlags = HideFlags.HideAndDontSave
        };
        return pixelImageMaterial;
    }

    void ReleasePixelImageMaterial()
    {
        if (pixelImageMaterial != null) Destroy(pixelImageMaterial);
        pixelImageMaterial = null;
    }

    static void ValidatePaperStyle(string style)
    {
        if (style != "diary" && style != "note" && style != "letter")
            throw new ArgumentException("Unknown Chapter 3 paper style: " + style, nameof(style));
    }

    static Sprite RequireUiSprite(string id)
    {
        var sprite = Resources.Load<Sprite>(UiResourcePath + id);
        if (sprite == null)
            throw new InvalidOperationException("Missing Chapter 3 UI Sprite: Assets/Resources/" + UiResourcePath + id + ".png");
        return sprite;
    }

    // Align rectangle edges, including one-pixel strokes, to logical pixels.
    // Odd-size rectangles correctly have half-pixel centres.
    static Vector2 PixelCentre(Vector2 position, Vector2 size)
    {
        return new Vector2(Mathf.Round(position.x - size.x * .5f) + size.x * .5f,
            Mathf.Round(position.y - size.y * .5f) + size.y * .5f);
    }

    UnityEngine.UI.Image PixelImage(RectTransform parent, string name, Color color, Vector2 position, Vector2 size)
    {
        size = new Vector2(Mathf.Round(size.x), Mathf.Round(size.y));
        var image = Image(parent, name, color, PixelCentre(position, size), size);
        image.material = RequirePixelImageMaterial();
        return image;
    }

    UnityEngine.UI.Image PaperImage(RectTransform parent, string name, Sprite skin, Vector2 position, Vector2 size)
    {
        // White is an identity tint: the authored black/#87827e pixels pass
        // through unchanged. Importers provide Point filtering and no mipmaps.
        var image = PixelImage(parent, name, Color.white, position, size);
        image.sprite = skin; image.type = UnityEngine.UI.Image.Type.Simple;
        image.preserveAspect = false;
        return image;
    }

    TMP_Text ModalText(RectTransform parent, string name, string value, Vector2 position, Vector2 size, int em, Color color)
    {
        var label = Text(parent, name, value, PixelCentre(position, size), size, em, color);
        label.enableAutoSizing = false;
        label.gameObject.AddComponent<OSChapter03PixelText>().RefreshMaterial();
        return label;
    }

    static void FitModalText(TMP_Text label, int minimum, int maximum)
    {
        if (label == null) return;
        label.enableAutoSizing = false;
        var size = label.rectTransform.rect.size;
        for (int em = maximum; em >= minimum; em--)
        {
            label.fontSize = em;
            var preferred = label.GetPreferredValues(label.text, size.x, float.PositiveInfinity);
            if (preferred.x <= size.x + .01f && preferred.y <= size.y + .01f) break;
        }
    }

    void RefreshModalTypography()
    {
        if (modalFrame == null) return;
        foreach (var pixelText in modalFrame.GetComponentsInChildren<OSChapter03PixelText>(true)) pixelText.RefreshMaterial();
        FitModalText(modalTitle, 8, 11);
        FitModalText(keypadFeedback, 8, 11);
    }

    RectTransform ModalFrame(RectTransform parent, string name, string title, Vector2 size, Vector2 position)
    {
        var frame = PixelImage(parent, name, ModalInk, position, size); frame.raycastTarget = true;
        PixelImage(frame.rectTransform, "Interior", Color.black, Vector2.zero, size - Vector2.one * 2f);
        var label = ModalText(frame.rectTransform, "Title", title, new Vector2(-8f, size.y * .5f - 10f),
            new Vector2(size.x - 34f, 18f), 11, ModalInk);
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        FitModalText(label, 8, 11);
        PixelImage(frame.rectTransform, "Divider", ModalInk, new Vector2(0f, size.y * .5f - 20f), new Vector2(size.x - 2f, 1f));
        return frame.rectTransform;
    }

    UnityEngine.UI.Button PixelButton(RectTransform parent, string name, string caption, Vector2 position, Vector2 size, Action clicked)
    {
        var edge = PixelImage(parent, name, ModalInk, position, size); edge.raycastTarget = true;
        PixelImage(edge.rectTransform, "Interior", Color.black, Vector2.zero, size - Vector2.one * 2f);
        var content = Rect("Pixels", edge.rectTransform, Vector2.zero, size);
        var button = edge.gameObject.AddComponent<OSChapter03PixelButton>();
        button.targetGraphic = edge;
        button.SetPixelContent(content);
        if (!string.IsNullOrEmpty(caption))
        {
            if (caption.Length == 1 && caption[0] >= '0' && caption[0] <= '9') DrawDigit(content, caption[0] - '0');
            else ModalText(content, "Label", caption, Vector2.zero, size, 14, ModalInk);
        }
        button.onClick.AddListener(() => clicked?.Invoke());
        return button;
    }

    static RectTransform PixelContent(UnityEngine.UI.Button button) => button.transform.Find("Pixels") as RectTransform;

    /// <summary>A bare pixel icon that presses down one pixel, with no box around it.</summary>
    UnityEngine.UI.Button GlyphButton(RectTransform parent, string name, Vector2 position, Vector2 size, Action clicked)
    {
        var hit = Image(parent, name, new Color(0f, 0f, 0f, 0f), PixelCentre(position, size), size);
        hit.raycastTarget = true;
        var content = Rect("Pixels", hit.rectTransform, Vector2.zero, size);
        var button = hit.gameObject.AddComponent<OSChapter03PixelButton>();
        button.targetGraphic = hit;
        button.SetPixelContent(content);
        button.onClick.AddListener(() => clicked?.Invoke());
        return button;
    }

    void DrawPixelGlyph(RectTransform parent, string name, string[] rows, int scale = 1) =>
        DrawPixelGlyph(parent, name, rows, scale, ModalInk);

    void DrawPixelGlyph(RectTransform parent, string name, string[] rows, int scale, Color color)
    {
        // Whole-pixel corner, so odd-sized glyphs land on the grid instead of on half pixels.
        float left = -Mathf.Floor(rows[0].Length * scale * .5f), top = Mathf.Floor(rows.Length * scale * .5f);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] != '.')
                    PixelImage(parent, name, color, new Vector2(left + (x + .5f) * scale, top - (y + .5f) * scale),
                        new Vector2(scale, scale));
    }

    void DrawArrow(RectTransform parent, bool right) => DrawArrow(parent, right, ModalInk);

    void DrawArrow(RectTransform parent, bool right, Color color)
    {
        DrawPixelGlyph(parent, "ArrowPixel", right
            ? new[] { "#....", ".#...", "..#..", "...#.", "..#..", ".#...", "#...." }
            : new[] { "....#", "...#.", "..#..", ".#...", "..#..", "...#.", "....#" }, 1, color);
    }

    void DrawClose(RectTransform parent) => DrawClose(parent, ModalInk);

    void DrawClose(RectTransform parent, Color color)
    {
        DrawPixelGlyph(parent, "CrossPixel", new[] { "#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#" }, 1, color);
    }

    void DrawDigit(RectTransform parent, int digit, int scale = 2)
    {
        var digits = new[] {
            new[] { "###", "#.#", "#.#", "#.#", "###" }, new[] { ".#.", "##.", ".#.", ".#.", "###" },
            new[] { "###", "..#", "###", "#..", "###" }, new[] { "###", "..#", "###", "..#", "###" },
            new[] { "#.#", "#.#", "###", "..#", "..#" }, new[] { "###", "#..", "###", "..#", "###" },
            new[] { "###", "#..", "###", "#.#", "###" }, new[] { "###", "..#", ".#.", ".#.", ".#." },
            new[] { "###", "#.#", "###", "#.#", "###" }, new[] { "###", "#.#", "###", "..#", "###" }
        };
        DrawPixelGlyph(parent, "DigitPixel", digits[digit], scale);
    }
}
