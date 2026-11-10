using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The diary pages, notes and the final letter. The sheet is drawn here
/// instead of imported: it is cut to the translated text, and the diary's ruled
/// lines follow the shaped baselines of the page actually on screen.</summary>
public sealed partial class OSChapter03StoryPresentation
{
    const int PaperSide = 16, PaperDiaryLeft = 26, PaperTop = 30, PaperBottom = 14, PaperFooterHeight = 16;
    const int PaperMaxHeight = 272, PaperMinWidth = 152, PaperTextSize = 14;
    static readonly byte[] PaperBayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    RawImage paperSheet;
    Texture2D paperTexture, transientTexture;
    RectTransform paperTitleTab, paperTitleRect;
    TMP_Text modalTitle;
    string paperStyle = "diary";
    int paperSeed;
    readonly List<int> paperRules = new List<int>();

    static int PaperWidth(string style) => style == "note" ? 216 : style == "letter" ? 288 : 264;
    static int PaperLeftMargin(string style) => style == "diary" ? PaperDiaryLeft : PaperSide;
    static int PaperMinHeight(string style) => style == "note" ? 64 : 120;
    static int Even(float value) => Mathf.RoundToInt(value * .5f) * 2;

    /// <summary>Builds the sheet: paper, title tab, body, close and the page footer.
    /// Every part is a child of the frame so the owner's QA keeps finding them.</summary>
    void BuildPaperBody(string title, string body, string style)
    {
        paperStyle = style; paperRules.Clear();
        paperSeed = (body ?? string.Empty).Length * 131 + style.Length * 17 + PaperWidth(style);

        paperSheet = SheetImage(modalFrame, "Paper");
        paperSheet.raycastTarget = true;
        var sheetHit = paperSheet.gameObject.AddComponent<OSChapter03PresentationHit>();
        sheetHit.Scrolled = e => { if (CanInteract) TurnPage(e.scrollDelta.y < 0f ? 1 : -1); };
        sheetHit.Clicked = e => { if (CanInteract && modalKind == ModalKind.Letter && letterUnfolded) TurnPage(1); };

        paperBodyRect = Rect("Content", modalFrame, Vector2.zero, modalFrame.rect.size);
        Stretch(paperBodyRect);
        paperBodyGroup = paperBodyRect.gameObject.AddComponent<CanvasGroup>();

        paperTitleTab = PixelImage(paperBodyRect, "TitleTab", Color.black, Vector2.zero, new Vector2(40f, 15f)).rectTransform;
        modalTitle = ModalText(paperBodyRect, "Title", title ?? string.Empty, Vector2.zero, new Vector2(80f, 18f), 11, Dim);
        modalTitle.alignment = TextAlignmentOptions.Left;
        modalTitle.textWrappingMode = TextWrappingModes.NoWrap;
        paperTitleRect = modalTitle.rectTransform;

        paperBody = ModalText(paperBodyRect, "Body", OSPalette.TintRichText(body ?? string.Empty), Vector2.zero,
            new Vector2(100f, 100f), PaperTextSize, Color.black);
        // Thirteen pixels with the dark-mode weight merged strokes of the
        // rounded CJK face; fourteen at the face's own weight stays legible.
        paperBody.GetComponent<OSChapter03PixelText>().UseInkWeight(0f);
        paperBody.alignment = TextAlignmentOptions.TopLeft;
        paperBody.textWrappingMode = TextWrappingModes.Normal;
        paperBody.overflowMode = TextOverflowModes.Page;
        // Keep negative side bearings (for example a French line starting in j)
        // within the reading inset at the fixed font size.
        paperBody.margin = new Vector4(2f, 0f, 2f, 1f);
        paperBody.richText = true;
        paperBody.raycastTarget = false;

        closeButton = GlyphButton(paperBodyRect, "Close", Vector2.zero, new Vector2(14f, 14f), DismissFromInput);
        DrawClose(PixelContent(closeButton), Color.black);
        paperPageLabel = ModalText(paperBodyRect, "Pages", "", Vector2.zero, new Vector2(60f, 18f), 11, Color.black);
        previousPageButton = GlyphButton(paperBodyRect, "Previous", Vector2.zero, new Vector2(14f, 14f), () => { if (CanInteract) TurnPage(-1); });
        nextPageButton = GlyphButton(paperBodyRect, "Next", Vector2.zero, new Vector2(14f, 14f), () => { if (CanInteract) TurnPage(1); });
        DrawArrow(PixelContent(previousPageButton), false, Color.black);
        DrawArrow(PixelContent(nextPageButton), true, Color.black);

        paperPage = 0; LayoutPaper();
    }

    RawImage SheetImage(RectTransform parent, string name)
    {
        var rect = Rect(name, parent, Vector2.zero, parent.rect.size);
        Stretch(rect);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.color = Color.white; image.raycastTarget = false;
        image.material = RequirePixelImageMaterial();
        return image;
    }

    /// <summary>Cuts the sheet to the text, places its parts, then repaginates.
    /// A letter keeps one size so it can rise out of its envelope.</summary>
    void LayoutPaper()
    {
        if (paperBody == null || modalFrame == null) return;
        int width = PaperWidth(paperStyle);
        int left = PaperLeftMargin(paperStyle), right = PaperSide;
        int maxHeight = Mathf.Min(PaperMaxHeight, Even(windowParent.rect.height - 14f));
        string text = paperBody.text ?? string.Empty;

        if (paperStyle == "note")
        {
            var loose = paperBody.GetPreferredValues(text, width - left - right, 0f);
            width = Mathf.Clamp(Even(loose.x + 6f) + left + right, PaperMinWidth, width);
        }
        int content = width - left - right;
        float wanted = paperBody.GetPreferredValues(text, content, 0f).y;

        int needed = Even(wanted + 4f) + PaperTop + PaperBottom;
        bool paged = modalKind == ModalKind.Letter || needed > maxHeight;
        for (int pass = 0; pass < 2; pass++)
        {
            int height = paged ? maxHeight : Mathf.Clamp(needed, PaperMinHeight(paperStyle), maxHeight);
            int bodyHeight = height - PaperTop - PaperBottom - (paged ? PaperFooterHeight : 0);
            PlacePaperParts(width, height, left, right, bodyHeight);
            if (modalKind != ModalKind.Letter) modalFrame.anchoredPosition = PixelCentre(Vector2.zero, new Vector2(width, height));
            Paginate();
            // The measurement can be a line short of the real shaping; then take the full page.
            if (paged || paperPages == 1) break;
            paged = true;
        }
    }

    void PlacePaperParts(int width, int height, int left, int right, int bodyHeight)
    {
        modalFrame.sizeDelta = new Vector2(width, height);
        float half = height * .5f, side = width * .5f;

        var bodySize = new Vector2(width - left - right, bodyHeight);
        paperBody.rectTransform.sizeDelta = bodySize;
        paperBody.rectTransform.anchoredPosition = PixelCentre(
            new Vector2(-side + left + bodySize.x * .5f, half - PaperTop - bodyHeight * .5f), bodySize);

        float room = width - left - right - 24f;
        paperTitleRect.sizeDelta = new Vector2(room, 18f);
        FitModalText(modalTitle, 8, 11);
        modalTitle.ForceMeshUpdate(true);
        float titleWidth = Mathf.Min(Mathf.Ceil(modalTitle.preferredWidth) + 2f, room);
        var titleSize = new Vector2(Mathf.Max(16f, Mathf.Ceil(titleWidth * .5f) * 2f), 18f);
        paperTitleRect.sizeDelta = titleSize;
        paperTitleRect.anchoredPosition = PixelCentre(new Vector2(-side + left + titleSize.x * .5f, half - 16f), titleSize);
        var tabSize = new Vector2(titleSize.x + 8f, 15f);
        paperTitleTab.sizeDelta = tabSize;
        paperTitleTab.anchoredPosition = PixelCentre(new Vector2(-side + left - 4f + tabSize.x * .5f, half - 16f), tabSize);

        var close = (RectTransform)closeButton.transform;
        close.anchoredPosition = PixelCentre(new Vector2(side - right - 2f, half - 16f), close.sizeDelta);

        float footer = -half + 14f;
        paperPageLabel.rectTransform.anchoredPosition = PixelCentre(new Vector2(0f, footer), paperPageLabel.rectTransform.sizeDelta);
        ((RectTransform)previousPageButton.transform).anchoredPosition = PixelCentre(new Vector2(-36f, footer), new Vector2(14f, 14f));
        ((RectTransform)nextPageButton.transform).anchoredPosition = PixelCentre(new Vector2(36f, footer), new Vector2(14f, 14f));
    }

    void Paginate()
    {
        if (paperBody == null) return;
        Canvas.ForceUpdateCanvases(); paperBody.ForceMeshUpdate(true);
        paperPages = Mathf.Max(1, paperBody.textInfo.pageCount);
        paperPage = Mathf.Clamp(paperPage, 0, paperPages - 1); ApplyPage();
    }

    /// <summary>Paints the sheet, including the ruled lines of the page on screen.</summary>
    void PaintPaper()
    {
        if (paperSheet == null || modalFrame == null) return;
        int width = Mathf.RoundToInt(modalFrame.rect.width), height = Mathf.RoundToInt(modalFrame.rect.height);
        if (width < 8 || height < 8) return;
        CollectPaperRules(height);
        paperTexture = RequireSheet(paperTexture, width, height);
        PaintSheet(paperTexture, paperStyle, paperSeed, paperRules, PaperLeftMargin(paperStyle), PaperSide);
        paperSheet.texture = paperTexture;
    }

    void CollectPaperRules(int height)
    {
        paperRules.Clear();
        if (paperStyle != "diary" || paperBody == null) return;
        float bodyCentre = paperBody.rectTransform.anchoredPosition.y;
        var info = paperBody.textInfo;
        for (int i = 0; i < info.lineCount && i < info.lineInfo.Length; i++)
        {
            var line = info.lineInfo[i];
            if (line.firstCharacterIndex < 0 || line.firstCharacterIndex >= info.characterCount) continue;
            if (info.characterInfo[line.firstCharacterIndex].pageNumber != paperPage) continue;
            int row = Mathf.RoundToInt(height * .5f - (bodyCentre + line.baseline)) + 2;
            if (row > PaperTop && row < height - PaperBottom) paperRules.Add(row);
        }
    }

    static Texture2D RequireSheet(Texture2D texture, int width, int height)
    {
        if (texture != null && texture.width == width && texture.height == height) return texture;
        if (texture != null) UnityEngine.Object.Destroy(texture);
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Chapter03 paper (owned)",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    /// <summary>One logical pixel per texel, two tones only: paper and ink.
    /// Age is dithered, never faded, so the sheet keeps the chapter's palette.</summary>
    static void PaintSheet(Texture2D texture, string style, int seed, List<int> rules, int left, int right)
    {
        int w = texture.width, h = texture.height;
        var pixels = new Color32[w * h];
        Color32 paper = Dim, ink = Color.black, nothing = new Color32(0, 0, 0, 0);
        bool note = style == "note", diary = style == "diary", letter = style == "letter";

        var top = new int[w]; var bottom = new int[w];
        for (int x = 0; x < w; x++)
        {
            top[x] = note
                ? (x / 2 % 2 == 1 ? 2 : 0) + (int)(PaperHash(seed, 9, x / 5) % 2) // torn off a pad
                : Bit(seed, 1, x, 3) + Bit(seed, 2, x / 3, 5);
            bottom[x] = Bit(seed, 3, x, 3) + Bit(seed, 4, x / 4, 6);
        }
        var near = new int[h]; var far = new int[h];
        for (int y = 0; y < h; y++)
        {
            near[y] = Bit(seed, 5, y, 3) + Bit(seed, 6, y / 3, 5);
            far[y] = Bit(seed, 7, y, 3) + Bit(seed, 8, y / 4, 6);
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int index = (h - 1 - y) * w + x;
                if (y < top[x] || y >= h - bottom[x] || x < near[y] || x >= w - far[y]) { pixels[index] = nothing; continue; }
                if (!note && (w - 1 - x) + (h - 1 - y) < 7) { pixels[index] = nothing; continue; } // dog-ear
                int depth = Mathf.Min(Mathf.Min(y - top[x], h - 1 - bottom[x] - y), Mathf.Min(x - near[y], w - 1 - far[y] - x));
                int grime = depth == 0 ? 6 : depth == 1 ? 3 : depth == 2 ? 1 : 0;
                pixels[index] = grime > 0 && PaperBayer[(y & 3) * 4 + (x & 3)] < grime ? ink : paper;
            }

        if (!note)
            for (int i = 0; i < 8; i++) Mark(pixels, w, h, w - 8 + i, h - 1 - i, ink); // the dog-ear's own fold

        // A cup ring, kept to the lower right so it never sits on the opening lines.
        int stainX = w - 22 - (int)(PaperHash(seed, 11, 0) % 30);
        int stainY = h - 12 - (int)(PaperHash(seed, 12, 0) % 10);
        float radius = 7f + PaperHash(seed, 13, 0) % 5;
        if (!letter) for (int y = Mathf.Max(0, stainY - 16); y < Mathf.Min(h, stainY + 16); y++)
            for (int x = Mathf.Max(0, stainX - 16); x < Mathf.Min(w, stainX + 16); x++)
            {
                if (!IsPaper(pixels, w, h, x, y, paper)) continue;
                float distance = Mathf.Sqrt((x - stainX) * (x - stainX) + (y - stainY) * (y - stainY) * 1.7f);
                int level = Mathf.Abs(distance - radius) < 1f ? 5 : distance < radius ? 1 : 0;
                if (level > 0 && PaperBayer[(y & 3) * 4 + (x & 3)] < level) Mark(pixels, w, h, x, y, ink);
            }

        if (letter) // where it was folded in four: worn dots, broken in places
            for (int k = 1; k <= 3; k++)
            {
                int x = w * k / 4;
                for (int y = 0; y < h; y++)
                    if (y % 3 == 0 && PaperHash(seed, 14 + k, y / 9) % 4 != 0) Dot(pixels, w, h, x, y, paper, ink);
            }

        if (diary)
        {
            for (int y = PaperTop - 4; y < h - PaperBottom; y += 2) Dot(pixels, w, h, left - 6, y, paper, ink);
            for (int k = 0; k < 4; k++)
            {
                int cy = 26 + k * Mathf.Max(18, (h - 56) / 3);
                for (int y = cy - 2; y <= cy + 2; y++)
                    for (int x = left - 20; x <= left - 14; x++)
                        if ((x - (left - 17)) * (x - (left - 17)) + (y - cy) * (y - cy) <= 5) Dot(pixels, w, h, x, y, paper, ink);
            }
        }
        if (rules != null)
            foreach (int row in rules)
                for (int x = left - 2; x < w - right; x += 2) Dot(pixels, w, h, x, row, paper, ink);

        texture.SetPixels32(pixels); texture.Apply(false, false);
    }

    static int Bit(int seed, int salt, int value, int modulo) => PaperHash(seed, salt, value) % (uint)modulo == 0 ? 1 : 0;
    static bool IsPaper(Color32[] pixels, int w, int h, int x, int y, Color32 paper)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return false;
        var pixel = pixels[(h - 1 - y) * w + x];
        return pixel.a == 255 && pixel.r == paper.r && pixel.g == paper.g && pixel.b == paper.b;
    }
    static void Mark(Color32[] pixels, int w, int h, int x, int y, Color32 ink)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int index = (h - 1 - y) * w + x;
        if (pixels[index].a == 255) pixels[index] = ink;
    }
    static void Dot(Color32[] pixels, int w, int h, int x, int y, Color32 paper, Color32 ink)
    { if (IsPaper(pixels, w, h, x, y, paper)) Mark(pixels, w, h, x, y, ink); }

    static uint PaperHash(int seed, int salt, int value)
    {
        unchecked
        {
            uint x = 2166136261u;
            x = (x ^ (uint)seed * 0x9E3779B9u) * 16777619u; x ^= x >> 15;
            x = (x ^ (uint)salt * 0x9E3779B9u) * 16777619u; x ^= x >> 15;
            x = (x ^ (uint)value * 0x9E3779B9u) * 16777619u; x ^= x >> 15;
            return x;
        }
    }

    void ReleasePaperTextures()
    {
        if (paperTexture != null) UnityEngine.Object.Destroy(paperTexture);
        if (transientTexture != null) UnityEngine.Object.Destroy(transientTexture);
        paperTexture = transientTexture = null;
    }
}
