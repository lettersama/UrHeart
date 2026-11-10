using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// 1-bit glyph bitmaps for the 「家」 grid.
///   Wide script (Han, kana, Hangul): one character per 16 px tile, rasterised
///   from the game's own TMP font atlas, so all ten languages come for free.
///   Everything else: Baba-style small capitals (3 x 5, a few letters 4-5 wide)
///   packed into the thing's tiles, up to two lines.
/// Bitmaps are cached; call Clear() when the language or font changes.
/// </summary>
public sealed class OSHomeGlyphBook
{
    public const int Cell = OSHomeLayout.Tile;

    /// <summary>A word laid out over `width` tiles: bits[y * (width * 16) + x], y downwards.</summary>
    public sealed class Word
    {
        public int width, height;
        public bool[] bits;
        public bool Get(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && bits[y * width + x];
    }

    sealed class AtlasCopy
    {
        public byte[] alpha;
        // Borrowed only while TryRaster samples the atlas. Unity owns this view;
        // reacquire it before each glyph because a dynamic font can resize it.
        public Unity.Collections.NativeArray<byte> nativeAlpha;
        public int width, height, glyphCount;
        public uint updates;
    }

    readonly Dictionary<long, bool[]> wide = new Dictionary<long, bool[]>();
    readonly Dictionary<(int, string), Word> words = new Dictionary<(int, string), Word>();
    readonly Dictionary<Texture, AtlasCopy> atlases = new Dictionary<Texture, AtlasCopy>();

    public TMP_FontAsset Font { get; set; }
    /// <summary>Em height of a wide glyph inside its 16 px cell.</summary>
    public float EmPixels { get; set; } = 13f;
    /// <summary>Share of a pixel's 4x4 samples that must be inside the glyph to light it.</summary>
    public float Coverage { get; set; } = .34f;

    public void Clear()
    {
        wide.Clear();
        words.Clear();
        atlases.Clear();
    }

    public Word Layout(string text, int tiles)
    {
        tiles = Mathf.Max(1, tiles);
        if (text == null) text = string.Empty;
        var cacheKey = (tiles, text);
        if (words.TryGetValue(cacheKey, out Word cached)) return cached;
        var word = new Word { width = tiles * Cell, height = Cell };
        word.bits = new bool[word.width * word.height];
        if (IsWide(text)) LayoutWide(word, text, tiles);
        else LayoutSmall(word, text);
        words[cacheKey] = word;
        return word;
    }

    /// <summary>Small capitals on one line, only as wide as they are (an item's number).</summary>
    public Word LayoutTag(string text)
    {
        if (string.IsNullOrEmpty(text)) return new Word { width = 0, height = Cell, bits = new bool[0] };
        var cacheKey = (-1, text);
        if (words.TryGetValue(cacheKey, out Word cached)) return cached;
        var letters = Letters(text);
        var word = new Word { width = Mathf.Max(1, Measure(letters)), height = Cell };
        word.bits = new bool[word.width * word.height];
        int x = 0, y = (Cell - LineHeight) / 2;
        foreach (var letter in letters)
        {
            if (letter.glyph.rows == null) { x += 3; continue; }
            Stamp(word, letter, x, y);
            x += letter.glyph.Width + Gap;
        }
        words[cacheKey] = word;
        return word;
    }

    // ------------------------------------------------------------ wide script

    public static bool IsWide(string text)
    {
        foreach (char c in text)
            if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF)
                || (c >= 0x3130 && c <= 0x318F) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFF00 && c <= 0xFFEF))
                return true;
        return false;
    }

    void LayoutWide(Word word, string text, int tiles)
    {
        var elements = new List<string>();
        var it = StringInfo.GetTextElementEnumerator(text);
        while (it.MoveNext()) elements.Add(it.GetTextElement());
        int count = Mathf.Min(elements.Count, tiles);
        // Fewer characters than tiles (a translation, the mirror's 「我」): centre them.
        int left = (tiles - count) * Cell / 2;
        for (int i = 0; i < count; i++)
        {
            bool[] glyph = Wide(char.ConvertToUtf32(elements[i], 0));
            int ox = left + i * Cell;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                    if (glyph[y * Cell + x]) word.bits[y * word.width + ox + x] = true;
        }
    }

    bool[] Wide(int unicode)
    {
        TMP_FontAsset font = Font;
        long key = ((long)(font != null ? font.GetInstanceID() : 0) << 32) | (uint)unicode;
        if (wide.TryGetValue(key, out bool[] bits)) return bits;
        bits = new bool[Cell * Cell];
        if (!TryRaster(font, (uint)unicode, bits)) Placeholder(bits);
        wide[key] = bits;
        return bits;
    }

    bool TryRaster(TMP_FontAsset font, uint unicode, bool[] bits)
    {
        TMP_FontAsset owner = FindOwner(font, unicode, 0);
        if (owner == null || !owner.characterLookupTable.TryGetValue(unicode, out TMP_Character character)
            || character.glyph == null) return false;
        // TMP may cache a fallback character in the primary font's lookup table.
        owner = character.textAsset as TMP_FontAsset ?? owner;
        Glyph glyph = character.glyph;
        var textures = owner.atlasTextures;
        if (textures == null || glyph.atlasIndex < 0 || glyph.atlasIndex >= textures.Length) return false;
        AtlasCopy atlas = Copy(textures[glyph.atlasIndex], owner.glyphTable.Count);
        if (atlas == null) return false;

        FaceInfo face = owner.faceInfo;
        float points = face.pointSize > 0 ? face.pointSize : 90f;
        float scale = EmPixels / points;
        GlyphMetrics m = glyph.metrics;
        GlyphRect r = glyph.glyphRect;
        float pad = owner.atlasPadding;
        // Centre the advance horizontally and the ascent..descent band vertically.
        float left = (Cell - m.horizontalAdvance * scale) * .5f + m.horizontalBearingX * scale;
        float baseline = (Cell - (face.ascentLine - face.descentLine) * scale) * .5f + face.ascentLine * scale;
        float top = baseline - m.horizontalBearingY * scale;
        const int Samples = 4;
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < Samples; sy++)
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        float u = (x + (sx + .5f) / Samples - left) / scale;   // glyph px from its left
                        float v = (y + (sy + .5f) / Samples - top) / scale;    // glyph px from its top
                        if (u < -pad || v < -pad || u > r.width + pad || v > r.height + pad) continue;
                        float ax = r.x + u, ay = r.y + r.height - v;           // atlas rows count upwards
                        if (Sample(atlas, ax, ay) >= .5f) inside++;
                    }
                bits[y * Cell + x] = inside >= Coverage * Samples * Samples;
            }
        return true;
    }

    static TMP_FontAsset FindOwner(TMP_FontAsset font, uint unicode, int depth)
    {
        if (font == null || depth > 4) return null;
        if (!font.characterLookupTable.ContainsKey(unicode))
            font.TryAddCharacters(char.ConvertFromUtf32((int)unicode));
        if (font.characterLookupTable.ContainsKey(unicode)) return font;
        var fallbacks = font.fallbackFontAssetTable;
        if (fallbacks != null)
            foreach (var fallback in fallbacks)
            {
                var owner = FindOwner(fallback, unicode, depth + 1);
                if (owner != null) return owner;
            }
        return null;
    }

    AtlasCopy Copy(Texture2D texture, int glyphCount)
    {
        if (texture == null) return null;
        atlases.TryGetValue(texture, out AtlasCopy copy);
        if (copy == null) copy = atlases[texture] = new AtlasCopy();
        if (texture.isReadable && texture.format == TextureFormat.Alpha8)
        {
            // The game's 8192-square font atlases already have CPU storage.
            // Sampling that data avoids a second 64 MB managed copy per atlas.
            copy.nativeAlpha = texture.GetPixelData<byte>(0);
            copy.alpha = null;
            copy.width = texture.width; copy.height = texture.height;
            copy.updates = texture.updateCount; copy.glyphCount = glyphCount;
            return copy;
        }
        copy.nativeAlpha = default;
        // FontEngine can add glyphs without changing Texture.updateCount, including
        // additions made by other TMP labels. Re-read when the font grows as well.
        if (copy.alpha != null && copy.updates == texture.updateCount && copy.glyphCount == glyphCount
            && copy.width == texture.width && copy.height == texture.height) return copy;
        copy.width = texture.width; copy.height = texture.height; copy.updates = texture.updateCount;
        copy.glyphCount = glyphCount;
        int length = copy.width * copy.height;
        if (copy.alpha == null || copy.alpha.Length != length) copy.alpha = new byte[length];
        if (texture.isReadable && texture.format == TextureFormat.RGBA32)
        {
            var pixels = texture.GetPixelData<Color32>(0);
            for (int i = 0; i < length; i++) copy.alpha[i] = pixels[i].a;
            return copy;
        }
        if (texture.isReadable)
        {
            var pixels = texture.GetPixels32(0);
            for (int i = 0; i < pixels.Length && i < copy.alpha.Length; i++) copy.alpha[i] = pixels[i].a;
            return copy;
        }
        // Not CPU-readable: read it back through a temporary render texture.
        var rt = RenderTexture.GetTemporary(copy.width, copy.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D read = null;
        try
        {
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            read = new Texture2D(copy.width, copy.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, copy.width, copy.height), 0, 0, false);
            read.Apply(false);
            var data = read.GetPixelData<Color32>(0);
            for (int i = 0; i < length; i++) copy.alpha[i] = data[i].a;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (read != null) Object.Destroy(read);
        }
        return copy;
    }

    static float Sample(AtlasCopy atlas, float x, float y)
    {
        x -= .5f; y -= .5f;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        float a = At(atlas, x0, y0), b = At(atlas, x0 + 1, y0), c = At(atlas, x0, y0 + 1), d = At(atlas, x0 + 1, y0 + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float At(AtlasCopy atlas, int x, int y)
    {
        if (x < 0 || y < 0 || x >= atlas.width || y >= atlas.height) return 0f;
        int index = y * atlas.width + x;
        return (atlas.nativeAlpha.IsCreated ? atlas.nativeAlpha[index] : atlas.alpha[index]) / 255f;
    }

    static void Placeholder(bool[] bits)
    {
        for (int i = 3; i < 13; i++)
        {
            bits[3 * Cell + i] = bits[12 * Cell + i] = true;
            bits[i * Cell + 3] = bits[i * Cell + 12] = true;
        }
    }

    // ------------------------------------------------------------ small capitals

    const int LineHeight = 8, MarkRow = 0, GlyphTop = 2, CedillaRow = 7, Gap = 1;

    struct Small { public string[] rows; public int Width => rows[0].Length; }
    struct Letter { public Small glyph; public string mark; public bool cedilla; }

    static Dictionary<char, Small> smallFont;

    void LayoutSmall(Word word, string text)
    {
        var letters = Letters(text);
        // Break into at most two lines that each fit the width, as evenly as possible.
        var lines = new List<List<Letter>> { letters };
        if (Measure(letters) > word.width && letters.Count > 1)
        {
            int best = letters.Count / 2, bestWidth = int.MaxValue;
            int space = letters.FindIndex(l => l.glyph.rows == null);
            for (int split = 1; split < letters.Count; split++)
            {
                int w = Mathf.Max(Measure(letters.GetRange(0, split)), Measure(letters.GetRange(split, letters.Count - split)));
                // Prefer breaking at a space when there is one.
                if (space >= 0 && split != space) w += 3;
                if (w < bestWidth) { bestWidth = w; best = split; }
            }
            var first = letters.GetRange(0, best);
            var second = letters.GetRange(best, letters.Count - best);
            lines = new List<List<Letter>> { Trim(first), Trim(second) };
        }
        int top = (word.height - lines.Count * LineHeight) / 2;
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            int x = (word.width - Mathf.Min(word.width, Measure(line))) / 2;
            int y = top + li * LineHeight;
            foreach (var letter in line)
            {
                if (letter.glyph.rows == null) { x += 3; continue; }
                Stamp(word, letter, x, y);
                x += letter.glyph.Width + Gap;
                if (x >= word.width) break;
            }
        }
    }

    static List<Letter> Trim(List<Letter> line)
    {
        while (line.Count > 0 && line[0].glyph.rows == null) line.RemoveAt(0);
        while (line.Count > 0 && line[line.Count - 1].glyph.rows == null) line.RemoveAt(line.Count - 1);
        return line;
    }

    static int Measure(List<Letter> line)
    {
        int width = 0;
        for (int i = 0; i < line.Count; i++)
            width += (line[i].glyph.rows == null ? 3 : line[i].glyph.Width) + (i < line.Count - 1 ? Gap : 0);
        return width;
    }

    static void Stamp(Word word, Letter letter, int x, int y)
    {
        string[] rows = letter.glyph.rows;
        int w = letter.glyph.Width;
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < w; c++)
                if (rows[r][c] == '1') Set(word, x + c, y + GlyphTop + r);
        if (!string.IsNullOrEmpty(letter.mark))
        {
            int mx = x + (w - letter.mark.Length) / 2;
            for (int c = 0; c < letter.mark.Length; c++)
                if (letter.mark[c] == '1') Set(word, mx + c, y + MarkRow);
        }
        if (letter.cedilla) Set(word, x + w / 2, y + CedillaRow);
    }

    static void Set(Word word, int x, int y)
    {
        if (x >= 0 && y >= 0 && x < word.width && y < word.height) word.bits[y * word.width + x] = true;
    }

    static List<Letter> Letters(string text)
    {
        EnsureSmallFont();
        var letters = new List<Letter>();
        string upper = text.ToUpperInvariant().Replace("ß", "SS").Replace("Æ", "AE").Replace("Œ", "OE").Replace("Ø", "O");
        string decomposed = upper.Normalize(NormalizationForm.FormD);
        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                if (letters.Count == 0) continue;
                var last = letters[letters.Count - 1];
                if (c == '̧') last.cedilla = true;                 // ç
                else if (c == '́') last.mark = "001";              // é
                else if (c == '̀') last.mark = "100";              // è
                else if (c == '̂' || c == '̌') last.mark = "010"; // ê
                else if (c == '̈' || c == '̆') last.mark = "101"; // ë, й
                else if (c == '̃') last.mark = "011";              // ñ
                else if (c == '̊') last.mark = "010";              // å
                letters[letters.Count - 1] = last;
                continue;
            }
            if (char.IsWhiteSpace(c)) { letters.Add(new Letter()); continue; }
            if (smallFont.TryGetValue(c, out Small glyph)) letters.Add(new Letter { glyph = glyph });
        }
        return letters;
    }

    static void Add(string chars, params string[] rows)
    {
        foreach (char c in chars) smallFont[c] = new Small { rows = rows };
    }

    static void EnsureSmallFont()
    {
        if (smallFont != null) return;
        smallFont = new Dictionary<char, Small>();
        Add("AА", "010", "101", "111", "101", "101");
        Add("BВ", "110", "101", "110", "101", "110");
        Add("CС", "011", "100", "100", "100", "011");
        Add("D", "110", "101", "101", "101", "110");
        Add("EЕ", "111", "100", "110", "100", "111");
        Add("F", "111", "100", "110", "100", "100");
        Add("G", "011", "100", "101", "101", "011");
        Add("HН", "101", "101", "111", "101", "101");
        Add("I", "111", "010", "010", "010", "111");
        Add("J", "001", "001", "001", "101", "010");
        Add("KК", "101", "101", "110", "101", "101");
        Add("L", "100", "100", "100", "100", "111");
        Add("MМ", "10001", "11011", "10101", "10001", "10001");
        Add("N", "1001", "1101", "1011", "1001", "1001");
        Add("OО", "010", "101", "101", "101", "010");
        Add("PР", "110", "101", "110", "100", "100");
        Add("Q", "010", "101", "101", "110", "011");
        Add("R", "110", "101", "110", "101", "101");
        Add("S", "011", "100", "010", "001", "110");
        Add("TТ", "111", "010", "010", "010", "010");
        Add("U", "101", "101", "101", "101", "111");
        Add("V", "101", "101", "101", "101", "010");
        Add("W", "10001", "10001", "10101", "11011", "10001");
        Add("XХ", "101", "101", "010", "101", "101");
        Add("Y", "101", "101", "010", "010", "010");
        Add("Z", "111", "001", "010", "100", "111");
        Add("0", "111", "101", "101", "101", "111");
        Add("1", "010", "110", "010", "010", "111");
        Add("2", "110", "001", "010", "100", "111");
        Add("3", "110", "001", "010", "001", "110");
        Add("4", "101", "101", "111", "001", "001");
        Add("5", "111", "100", "110", "001", "110");
        Add("6", "011", "100", "111", "101", "111");
        Add("7", "111", "001", "010", "010", "010");
        Add("8", "111", "101", "111", "101", "111");
        Add("9", "111", "101", "111", "001", "110");
        Add("-", "000", "000", "111", "000", "000");
        Add("#", "101", "111", "101", "111", "101");
        Add("'’", "1", "1", "0", "0", "0");
        Add(".", "0", "0", "0", "0", "1");
        Add("?", "110", "001", "010", "000", "010");
        Add("!", "1", "1", "1", "0", "1");
        // Cyrillic letters that do not share a Latin shape.
        Add("Б", "111", "100", "110", "101", "110");
        Add("Г", "111", "100", "100", "100", "100");
        Add("Д", "0110", "1010", "1010", "1111", "1001");
        Add("Ж", "10101", "10101", "01110", "10101", "10101");
        Add("З", "110", "001", "010", "001", "110");
        Add("И", "1001", "1011", "1101", "1001", "1001");
        Add("Л", "011", "101", "101", "101", "101");
        Add("П", "111", "101", "101", "101", "101");
        Add("У", "101", "101", "011", "001", "110");
        Add("Ф", "01110", "10101", "10101", "01110", "00100");
        Add("Ц", "1010", "1010", "1010", "1111", "0001");
        Add("Ч", "101", "101", "011", "001", "001");
        Add("Ш", "10101", "10101", "10101", "10101", "11111");
        Add("Щ", "10101", "10101", "10101", "11111", "00001");
        Add("Ъ", "1100", "0100", "0110", "0101", "0110");
        Add("Ы", "1001", "1001", "1101", "1011", "1101");
        Add("Ь", "100", "100", "110", "101", "110");
        Add("Э", "110", "001", "011", "001", "110");
        Add("Ю", "10010", "10101", "11101", "10101", "10010");
        Add("Я", "011", "101", "011", "101", "101");
    }
}
