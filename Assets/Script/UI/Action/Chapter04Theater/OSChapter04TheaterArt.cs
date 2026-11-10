using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One paper piece as 1-bit ink, rows top-down: 0 clear, 1 black, 2 gray.
/// Dimmed copies are made on demand and cached by level (see Dimmed).
/// </summary>
public sealed class OSChapter04TheaterPiece
{
    public const byte Clear = 0, Black = 1, Gray = 2;
    // Bayer 4x4, [y % 4][x % 4].
    public static readonly byte[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    public const int Undimmed = 16;

    public readonly string Id;
    public readonly int Width, Height;
    public readonly bool IsPlaceholder;
    readonly byte[] ink;
    readonly Dictionary<int, byte[]> dimmed = new Dictionary<int, byte[]>();

    public OSChapter04TheaterPiece(string id, int width, int height, byte[] ink, bool placeholder)
    {
        Id = id; Width = width; Height = height; this.ink = ink; IsPlaceholder = placeholder;
    }

    /// <summary>
    /// Gray pixels turn black where Bayer[y % 4][x % 4] &gt;= level, in the
    /// piece's own pixel coordinates. Lower level = darker; 16 and up = as drawn.
    /// Cached per (piece, level).
    /// </summary>
    public byte[] Dimmed(int level)
    {
        if (level >= Undimmed) return ink;
        if (level < 0) level = 0;
        if (dimmed.TryGetValue(level, out byte[] cached)) return cached;
        var result = new byte[ink.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                byte value = ink[i];
                result[i] = value == Gray && Bayer[(y & 3) * 4 + (x & 3)] >= level ? Black : value;
            }
        dimmed[level] = result;
        return result;
    }

    public static OSChapter04TheaterPiece Solid(string id, int width, int height, byte value, bool placeholder = false)
    {
        var ink = new byte[width * height];
        for (int i = 0; i < ink.Length; i++) ink[i] = value;
        return new OSChapter04TheaterPiece(id, width, height, ink, placeholder);
    }
}

/// <summary>
/// Paper pieces by file name. A piece comes from, in order:
///   1. an override sprite with the same id (serialized on the TV view);
///   2. Resources/Chapter04/Theater/&lt;id&gt;.png;
///   3. a plain gray block of the authored size, so a missing file never
///      breaks the act.
/// Replacing art: overwrite the PNG with the same name and size. Pixels are
/// read once into ink and cached; shared by the TV view and the editor preview.
/// </summary>
public sealed class OSChapter04TheaterLibrary
{
    public const string Folder = "Chapter04/Theater/";

    [Serializable]
    public struct Override
    {
        public string id;
        public Sprite sprite;
    }

    // Authored sizes; used for gray placeholders when a file is missing.
    static readonly Dictionary<string, Vector2Int> AuthoredSizes = new Dictionary<string, Vector2Int>
    {
        { "building_small", new Vector2Int(48, 64) }, { "building_broken", new Vector2Int(56, 104) },
        { "lamp_post", new Vector2Int(18, 70) }, { "fence", new Vector2Int(44, 22) },
        { "fg_rubble_a", new Vector2Int(52, 18) }, { "fg_rubble_b", new Vector2Int(30, 12) },
        { "house", new Vector2Int(116, 100) }, { "door", new Vector2Int(30, 61) },
        { "cabinet_empty", new Vector2Int(52, 84) }, { "chair_toppled", new Vector2Int(34, 18) },
        { "floor_strip", new Vector2Int(360, 12) }, { "box", new Vector2Int(32, 24) },
        { "puppet_carry", new Vector2Int(70, 70) }, { "puppet_rummage", new Vector2Int(70, 70) },
        { "puppet_sit_cup", new Vector2Int(70, 84) },
        // Sections 2-3 (round 6).
        { "bucket", new Vector2Int(14, 14) }, { "campfire", new Vector2Int(26, 20) }, { "drying_rack", new Vector2Int(44, 38) },
        { "fg_grass", new Vector2Int(28, 14) }, { "fg_sign", new Vector2Int(26, 16) }, { "fg_stones", new Vector2Int(46, 10) },
        { "hill_far_a", new Vector2Int(128, 22) }, { "hill_far_b", new Vector2Int(96, 16) }, { "lantern", new Vector2Int(14, 20) },
        { "mat", new Vector2Int(120, 6) }, { "road_strip", new Vector2Int(360, 12) }, { "tent_big", new Vector2Int(70, 50) },
        { "tent_interior", new Vector2Int(300, 124) }, { "tent_small", new Vector2Int(50, 36) },
        { "puppet_backpack", new Vector2Int(70, 70) }, { "puppet_hold_vial", new Vector2Int(70, 70) },
        { "puppet_sit_ground", new Vector2Int(70, 70) }, { "puppet_mother_daughter", new Vector2Int(84, 70) },
        // Round 7: NorthSettlement, LabNight, FlowerWalk.
        { "water_jar", new Vector2Int(16, 20) }, { "desk_lamp", new Vector2Int(18, 22) },
        { "spire_far", new Vector2Int(32, 100) }, { "skyline_empty", new Vector2Int(220, 26) },
        { "night_overlay", new Vector2Int(320, 180) }, { "puppet_mother_bed", new Vector2Int(84, 70) },
        { "puppet_boy_scales", new Vector2Int(70, 70) }, { "researcher_a", new Vector2Int(84, 70) },
        { "researcher_b", new Vector2Int(70, 70) }, { "puppet_flowers", new Vector2Int(70, 70) },
        // Round 8: the grave.
        { "chapel_ruin", new Vector2Int(100, 66) }, { "dead_tree", new Vector2Int(84, 104) },
        { "fence_iron", new Vector2Int(48, 32) }, { "brick_wall_low", new Vector2Int(112, 48) },
        { "lamp_post_grave", new Vector2Int(18, 86) }, { "mother_grave", new Vector2Int(36, 64) },
        { "flowers_vial_ground", new Vector2Int(22, 16) }, { "fg_fence", new Vector2Int(96, 16) },
        { "puppet_kneel_place", new Vector2Int(70, 70) },
    };

    readonly Dictionary<string, OSChapter04TheaterPiece> pieces = new Dictionary<string, OSChapter04TheaterPiece>();
    readonly List<string> missing = new List<string>();
    readonly HashSet<string> absent = new HashSet<string>();
    readonly IList<Override> overrides;

    public OSChapter04TheaterLibrary(IList<Override> overrides = null) => this.overrides = overrides;

    /// <summary>Ids that fell back to a gray block.</summary>
    public IReadOnlyList<string> Missing => missing;

    /// <summary>
    /// A piece that may not be drawn yet: when its file is missing, quietly use
    /// the fallback id instead (e.g. puppet_walk before the art exists).
    /// </summary>
    public OSChapter04TheaterPiece Get(string id, string fallback) => Exists(id) ? pieces[id] : Get(fallback);

    /// <summary>True when the art for id is present (it is then cached for Get).</summary>
    public bool Exists(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (pieces.TryGetValue(id, out OSChapter04TheaterPiece cached)) return !cached.IsPlaceholder;
        if (absent.Contains(id)) return false;
        OSChapter04TheaterPiece piece = Load(id);
        if (piece == null) { absent.Add(id); return false; }
        pieces[id] = piece;
        return true;
    }

    OSChapter04TheaterPiece Load(string id)
    {
        Sprite sprite = null;
        if (overrides != null)
            foreach (Override entry in overrides)
                if (entry.id == id && entry.sprite != null) sprite = entry.sprite;
        if (sprite == null) sprite = Resources.Load<Sprite>(Folder + id);
        return sprite != null ? Read(id, sprite) : null;
    }

    public OSChapter04TheaterPiece Get(string id)
    {
        if (pieces.TryGetValue(id, out OSChapter04TheaterPiece piece)) return piece;
        piece = Load(id);
        if (piece == null)
        {
            Vector2Int size = AuthoredSizes.TryGetValue(id, out Vector2Int known) ? known : new Vector2Int(16, 16);
            piece = OSChapter04TheaterPiece.Solid(id, size.x, size.y, OSChapter04TheaterPiece.Gray, true);
            missing.Add(id);
            Debug.LogWarning("[Chapter04Theater] Missing paper piece '" + id + "', using a gray block.");
        }
        pieces[id] = piece;
        return piece;
    }

    public void Add(OSChapter04TheaterPiece piece) => pieces[piece.Id] = piece;

    /// <summary>
    /// Readable textures are read directly; others (a replacement imported
    /// without Read/Write) go through a GPU copy. Either way pixels are classed
    /// by alpha and brightness, so a 1-level colour drift cannot leak a third tone.
    /// </summary>
    static OSChapter04TheaterPiece Read(string id, Sprite sprite)
    {
        Rect rect = sprite.rect;
        int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
        int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
        Texture2D texture = sprite.texture;
        Color32[] source = null;
        if (texture.isReadable)
        {
            try { source = texture.GetPixels32(); }
            catch (Exception) { source = null; }
        }
        int stride = texture.width;
        if (source == null)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Point;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            copy.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            source = copy.GetPixels32();
            if (Application.isPlaying) UnityEngine.Object.Destroy(copy);
            else UnityEngine.Object.DestroyImmediate(copy);
        }
        var ink = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Texture rows are bottom-up; ink rows are top-down.
                Color32 c = source[(y0 + h - 1 - y) * stride + x0 + x];
                ink[y * w + x] = c.a < 128 ? OSChapter04TheaterPiece.Clear
                    : c.r + c.g + c.b < 3 * 64 ? OSChapter04TheaterPiece.Black : OSChapter04TheaterPiece.Gray;
            }
        return new OSChapter04TheaterPiece(id, w, h, ink, false);
    }
}
