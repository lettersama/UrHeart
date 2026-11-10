using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A flat colour drawn through a 4x4 ordered dither in logical pixels:
/// level 0 is clear, 16 is solid. Every fill samples one screen-wide grid,
/// so neighbouring fills line up without seams. Never blocks raycasts unless
/// the owner turns <see cref="RawImage.raycastTarget"/> on.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)] // Align after owners finish positioning their dither bands.
[RequireComponent(typeof(RawImage))]
public sealed class OSDitherFill : MonoBehaviour
{
    public const int MaxLevel = 16;
    const int LogicalHeight = 360;
    static readonly byte[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    static Texture2D[] levels;

    readonly Vector3[] corners = new Vector3[4];
    RawImage image;
    int level = -1;

    public RawImage Image => image;
    public int Level => Mathf.Max(0, level);

    public static OSDitherFill Create(string name, Transform parent, Color color)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var fill = node.AddComponent<OSDitherFill>();
        // Awake waits while the parent is inactive; the fill must work right away.
        fill.image = node.GetComponent<RawImage>();
        fill.image.raycastTarget = false;
        fill.image.color = color;
        fill.SetLevel(0);
        return fill;
    }

    void Awake()
    {
        if (image != null) return;
        image = GetComponent<RawImage>();
        image.raycastTarget = false;
    }

    public void SetLevel(int value)
    {
        value = Mathf.Clamp(value, 0, MaxLevel);
        if (value == level) return;
        level = value;
        image.texture = LevelTexture(level);
        image.enabled = level > 0;
        Align();
    }

    void LateUpdate()
    {
        if (level > 0) Align();
    }

    // Tile the 4x4 pattern once per four logical pixels, anchored to the root canvas.
    void Align()
    {
        var canvas = image.canvas != null ? image.canvas.rootCanvas : null;
        if (canvas == null) return;
        var root = (RectTransform)canvas.transform;
        Rect r = root.rect;
        if (r.height <= 0f) return;
        ((RectTransform)transform).GetWorldCorners(corners);
        Vector2 min = root.InverseTransformPoint(corners[0]);
        Vector2 max = root.InverseTransformPoint(corners[2]);
        float unit = LogicalHeight / r.height / 4f;
        image.uvRect = new Rect((min.x - r.xMin) * unit, (min.y - r.yMin) * unit,
            (max.x - min.x) * unit, (max.y - min.y) * unit);
    }

    static Texture2D LevelTexture(int value)
    {
        if (levels == null) levels = new Texture2D[MaxLevel + 1];
        if (levels[value] != null) return levels[value];
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "OSDither" + value };
        var pixels = new Color32[16];
        for (int i = 0; i < 16; i++)
            pixels[i] = new Color32(255, 255, 255, Bayer[i] < value ? (byte)255 : (byte)0);
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        levels[value] = texture;
        return texture;
    }
}
