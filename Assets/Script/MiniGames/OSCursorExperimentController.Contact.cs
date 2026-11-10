using UnityEngine;

public sealed partial class OSCursorExperimentController
{
    // Wall contact shows *where* the pointer hit: a three-frame 1-bit spark at
    // the contact point and a short checker flicker on the touched wall segment.
    // Presentation only; collision, recoil and timing stay in the controller.
    const float ContactSegmentReach = 10f;
    UnityEngine.UI.Image contactSpark, contactWallFlash;
    Sprite[] contactSparkFrames;
    Sprite[] contactCheckers;

    static readonly string[][] SparkFrames =
    {
        new[] { "..I..", ".III.", "IIIII", ".III.", "..I.." },
        new[] { "....I....", "....I....", "..I...I..", ".........", "II.....II", ".........", "..I...I..", "....I....", "....I...." },
        new[] { ".....I.....", "...........", ".I.......I.", "...........", "...........", "I.........I", "...........", "...........", ".I.......I.", "...........", ".....I....." },
    };

    static Sprite PixelSprite(string[] rows, string name)
    {
        int w = rows[0].Length, h = rows.Length;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = name };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                texture.SetPixel(x, h - 1 - y, rows[y][x] == 'I' ? Color.white : Color.clear);
        texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    void InitializeContactEffects()
    {
        contactSparkFrames = new Sprite[SparkFrames.Length];
        for (int i = 0; i < SparkFrames.Length; i++) contactSparkFrames[i] = PixelSprite(SparkFrames[i], "ContactSpark" + i);
        contactCheckers = new[] { PixelSprite(new[] { "I.", ".I" }, "ContactCheckerA"), PixelSprite(new[] { ".I", "I." }, "ContactCheckerB") };
        // Beneath the chain, weight and pointer, above the maze artwork.
        contactWallFlash = MakeImage("ContactWallFlash", dynamicRoot, contactCheckers[0]);
        contactWallFlash.type = UnityEngine.UI.Image.Type.Tiled;
        // One checker cell per native pixel: offset this project's Canvas reference PPU.
        var canvas = GetComponentInParent<Canvas>();
        contactWallFlash.pixelsPerUnitMultiplier = canvas != null ? canvas.rootCanvas.referencePixelsPerUnit : 20f;
        contactWallFlash.color = Ink;
        contactSpark = MakeImage("ContactSparkBurst", dynamicRoot, contactSparkFrames[0]);
        contactSpark.color = Ink;
        contactWallFlash.transform.SetAsFirstSibling();
        contactSpark.transform.SetSiblingIndex(1);
        contactWallFlash.gameObject.SetActive(false);
        contactSpark.gameObject.SetActive(false);
    }

    void RenderContactEffects()
    {
        if (contactSpark == null) return;
        bool colliding = phase == ExperimentPhase.Collision;
        int frame = !colliding ? -1 : phaseTime < .05f ? 0 : phaseTime < .11f ? 1 : phaseTime < .19f ? 2 : -1;
        contactSpark.gameObject.SetActive(frame >= 0);
        if (frame >= 0)
        {
            var sprite = contactSparkFrames[frame];
            contactSpark.sprite = sprite;
            contactSpark.rectTransform.sizeDelta = sprite.rect.size;
            Vector2 centre = MazeOffset + contactPoint;
            Place(contactSpark.rectTransform, centre - sprite.rect.size * .5f + new Vector2(.5f, .5f));
        }
        bool flashing = colliding && phaseTime < .2f && TryContactSegment(out Rect segment);
        contactWallFlash.gameObject.SetActive(flashing);
        if (!flashing) return;
        TryContactSegment(out segment);
        int beat = Mathf.FloorToInt(phaseTime / .045f) & 1;
        contactWallFlash.sprite = contactCheckers[beat];
        contactWallFlash.rectTransform.sizeDelta = segment.size;
        Place(contactWallFlash.rectTransform, MazeOffset + segment.position);
    }

    /// <summary>The touched wall, clipped to a short stretch around the contact.</summary>
    bool TryContactSegment(out Rect segment)
    {
        segment = default;
        var walls = layout.Walls;
        if (walls == null || walls.Length == 0) return false;
        float best = float.MaxValue; Rect hit = default;
        foreach (var wall in walls)
        {
            float dx = Mathf.Max(wall.xMin - contactPoint.x, 0f, contactPoint.x - wall.xMax);
            float dy = Mathf.Max(wall.yMin - contactPoint.y, 0f, contactPoint.y - wall.yMax);
            float distance = dx * dx + dy * dy;
            if (distance < best) { best = distance; hit = wall; }
        }
        if (best > 9f) return false;
        Rect window = Rect.MinMaxRect(contactPoint.x - ContactSegmentReach, contactPoint.y - ContactSegmentReach,
            contactPoint.x + ContactSegmentReach, contactPoint.y + ContactSegmentReach);
        float xMin = Mathf.Max(hit.xMin, window.xMin), xMax = Mathf.Min(hit.xMax, window.xMax);
        float yMin = Mathf.Max(hit.yMin, window.yMin), yMax = Mathf.Min(hit.yMax, window.yMax);
        if (xMax <= xMin || yMax <= yMin) return false;
        segment = Rect.MinMaxRect(Mathf.Floor(xMin), Mathf.Floor(yMin), Mathf.Ceil(xMax), Mathf.Ceil(yMax));
        return true;
    }
}
