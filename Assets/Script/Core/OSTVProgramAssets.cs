using UnityEngine;

/// <summary>The Resources sprite is the artist-facing replacement point for the TV icon.</summary>
public static class OSTVProgramAssets
{
    public const string IconResourcePath = "Chapter04/TVIcon";
    static Sprite cachedIcon;

    public static Sprite Icon
    {
        get
        {
            if (cachedIcon != null) return cachedIcon;
            cachedIcon = Resources.Load<Sprite>(IconResourcePath);
            if (cachedIcon != null) return cachedIcon;
            // Temporary pixel placeholder; no authored asset or import settings are changed.
            var texture = new Texture2D(24, 24, TextureFormat.RGBA32, false)
            {
                name = "TV Icon Placeholder",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[24 * 24];
            Color32 frame = new Color32(159, 155, 151, 255);
            Color32 edge = new Color32(70, 68, 66, 255);
            Color32 screen = new Color32(38, 33, 35, 255);
            Color32 curtain = new Color32(105, 53, 59, 255);
            for (int y = 4; y <= 18; y++)
                for (int x = 1; x <= 22; x++)
                {
                    bool border = x <= 2 || x >= 21 || y <= 5 || y >= 17;
                    pixels[y * 24 + x] = border ? edge : frame;
                }
            for (int y = 7; y <= 15; y++)
                for (int x = 4; x <= 17; x++)
                    pixels[y * 24 + x] = x == 10 || x == 11 || x % 4 == 0 ? screen : curtain;
            pixels[14 * 24 + 19] = frame;
            pixels[10 * 24 + 19] = edge;
            for (int x = 7; x <= 16; x++) pixels[2 * 24 + x] = edge;
            for (int y = 3; y <= 4; y++)
                for (int x = 10; x <= 13; x++) pixels[y * 24 + x] = frame;
            pixels[19 * 24 + 11] = edge;
            pixels[20 * 24 + 10] = frame;
            pixels[21 * 24 + 9] = frame;
            pixels[20 * 24 + 12] = frame;
            pixels[21 * 24 + 13] = frame;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            cachedIcon = Sprite.Create(texture, new Rect(0, 0, 24, 24), new Vector2(.5f, .5f), 24f);
            cachedIcon.name = "TV Icon Placeholder";
            cachedIcon.hideFlags = HideFlags.HideAndDontSave;
            return cachedIcon;
        }
    }
}
