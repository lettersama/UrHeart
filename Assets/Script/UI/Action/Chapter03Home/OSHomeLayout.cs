using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data for the chapter-3 「家」 view: one screen per room, a 15 x 10 grid of
/// 16 px tiles, y counted downwards from the top-left tile (as in the map draft).
/// Presentation data only. Story flags, items and memory text stay with the
/// backend, which drives the view through OSHomeView's methods and events.
/// </summary>
public enum OSHomeInk { Gray, Red, Yellow, Blue }

public enum OSHomeThingStyle { Word, DashedEmpty, WallMirror }

public enum OSHomeLightKind
{
    /// <summary>A lamp: full core, then three dithered steps of `band` px.</summary>
    Steady,
    /// <summary>The hall's ceiling lamp: the core grows and shrinks slowly.</summary>
    Breathing,
    /// <summary>The storage tube: drops to a quarter now and then, irregularly.</summary>
    Flicker,
    /// <summary>A narrow band (fridge door, door slit): `strip` is the lit rect.</summary>
    Strip,
    /// <summary>The study's desk lamp: steady, but cuts out for a moment every few seconds.</summary>
    Faulty,
    /// <summary>Lightning round a drawn curtain: `strip` is lit only while OSHomeView.Lightning flashes.</summary>
    Lightning
}

[Serializable]
public sealed class OSHomeThing
{
    [Tooltip("Stable id the backend uses (events, SetThingVisible, ...).")]
    public string id;
    [Tooltip("Leftmost tile; the word runs to the right over `width` tiles.")]
    public Vector2Int tile;
    [Tooltip("Tiles the thing occupies. 0 = length of the Chinese glyph text.")]
    public int width;
    [Tooltip("Short word drawn on the grid (localization key). Missing key: `glyph`.")]
    public string glyphKey;
    [Tooltip("Chinese fallback for the short word, one character per tile.")]
    public string glyph;
    [Tooltip("Full name for the hover label (localization key). Missing key: `label`.")]
    public string labelKey;
    public string label;
    public OSHomeInk ink;
    public OSHomeThingStyle style;
    [Tooltip("Extra tiles that activate this same object, such as the empty seat beside a sofa.")]
    public List<Vector2Int> hitTiles = new List<Vector2Int>();
    [Tooltip("Decorative furniture still blocks walking, but does not offer an interaction.")]
    public bool interactive = true;
    [Tooltip("Distance from which this thing can be reached; wall objects behind a counter use two tiles.")]
    public int approachDistance = 1;
    [Tooltip("Furniture blocks walking; a thing lying on the floor (paper) may not.")]
    public bool blocksWalk = true;
    [Tooltip("Hidden things are neither drawn nor clickable until shown by the backend.")]
    public bool hidden;
    [Tooltip("Drawn even in the dark (the mirror while it shows a face).")]
    public bool glow;

    [NonSerialized] string measuredGlyph;
    [NonSerialized] int measuredGlyphWidth;

    public int Width
    {
        get
        {
            if (width > 0) return width;
            // Hit tests and wall pixels ask repeatedly. Recount only when the
            // authored glyph changes; keep the cache out of assets and saves.
            if (measuredGlyphWidth == 0 || measuredGlyph != glyph)
            {
                measuredGlyph = glyph;
                measuredGlyphWidth = Mathf.Max(1, string.IsNullOrEmpty(glyph)
                    ? 1 : new System.Globalization.StringInfo(glyph).LengthInTextElements);
            }
            return measuredGlyphWidth;
        }
    }
    public bool Covers(Vector2Int t) => (t.y == tile.y && t.x >= tile.x && t.x < tile.x + Width)
        || (hitTiles != null && hitTiles.Contains(t));
}

[Serializable]
public sealed class OSHomeDoor
{
    public string id;
    [Tooltip("The door tile, on the wall line just outside the floor rect.")]
    public Vector2Int tile;
    [Tooltip("Room entered through this door, and the tile 「我」 arrives on.")]
    public string targetRoom;
    public Vector2Int arriveTile;
    public string glyphKey = "os.home.glyph.door";
    public string glyph = "门";
    public string labelKey;
    public string label;
    public OSHomeInk ink;
    [Tooltip("A locked door is reached but not passed; the backend answers DoorReached.")]
    public bool locked;
}

[Serializable]
public sealed class OSHomeLight
{
    public string id;
    public OSHomeLightKind kind;
    [Tooltip("Centre in room pixels (tile * 16 + 8 is a tile's centre), y downwards.")]
    public Vector2 centre;
    [Tooltip("Fully lit radius in logical px.")]
    public float core = 30f;
    [Tooltip("Width of each of the three dithered steps = how soft the light is.")]
    public float band = 4f;
    [Tooltip("Strip lights only: the fully lit rect in room pixels, y downwards.")]
    public RectInt strip;
    public bool on = true;
}

[Serializable]
public sealed class OSHomeRoom
{
    public string id;
    [Tooltip("Room name (localization key) for the window title, if the backend wants one.")]
    public string titleKey;
    public string title;
    [Tooltip("Walkable floor, in tiles, inclusive (x0, y0) .. (x0 + w - 1, y0 + h - 1).")]
    public RectInt floor = new RectInt(1, 1, 13, 8);
    public List<OSHomeDoor> doors = new List<OSHomeDoor>();
    public List<OSHomeThing> things = new List<OSHomeThing>();
    public List<OSHomeLight> lights = new List<OSHomeLight>();
    public List<Vector2Int> water = new List<Vector2Int>();
    [Tooltip("Dashed empty tiles: where 主人 usually is.")]
    public List<Vector2Int> empties = new List<Vector2Int>();
    [Tooltip("Footprints in walking order; drawn alternately left/right.")]
    public List<Vector2Int> footprints = new List<Vector2Int>();
    /// <summary>Dried blood on the floor, in order along the trail: red marks in place of the floor dot.</summary>
    public List<Vector2Int> blood = new List<Vector2Int>();
    public bool footprintsVisible = true;
    [Tooltip("Outdoor rooms can display rain; indoor rooms never receive it.")]
    public bool outdoors;
    [Tooltip("Append completed player steps to this room's presentation-only footprint trail.")]
    public bool recordFootprints;
    [Tooltip("Show a square beside unvisited interactive objects, dim visited objects.")]
    public bool showVisitedMarkers;
    [Tooltip("Optional perimeter word instead of a wall line (the yard's fence).")]
    public string boundaryGlyphKey;
    public string boundaryGlyph;
    [Tooltip("Clicks on the word perimeter activate this thing, except at doors and named objects.")]
    public string boundaryThingId;
    [Tooltip("With a perimeter word: the top edge is the house's wall line instead (the yard below the front door).")]
    public bool houseWallTop;
    [Tooltip("Stone slabs drawn instead of the floor dot (the yard's path).")]
    public List<Vector2Int> stones = new List<Vector2Int>();
    [Tooltip("Planted ground: little sprouts instead of the floor dot (the yard's vegetable plot).")]
    public List<Vector2Int> garden = new List<Vector2Int>();

    public bool OnFloor(Vector2Int t) => t.x >= floor.xMin && t.x < floor.xMax && t.y >= floor.yMin && t.y < floor.yMax;

    public OSHomeThing FindThing(string thingId)
    {
        foreach (var thing in things) if (thing != null && thing.id == thingId) return thing;
        return null;
    }

    public OSHomeDoor FindDoor(string doorId)
    {
        foreach (var door in doors) if (door != null && door.id == doorId) return door;
        return null;
    }

    public OSHomeLight FindLight(string lightId)
    {
        foreach (var light in lights) if (light != null && light.id == lightId) return light;
        return null;
    }
}

[CreateAssetMenu(menuName = "MrTGame/Chapter 3/Home Layout", fileName = "Chapter03HomeLayout")]
public sealed class OSHomeLayout : ScriptableObject
{
    public const int Columns = 15, Rows = 10, Tile = 16;
    public const int PixelWidth = Columns * Tile, PixelHeight = Rows * Tile;

    public string firstRoom = "study";
    public Vector2Int firstTile = new Vector2Int(5, 7);
    public Vector2Int firstDongTile = new Vector2Int(6, 7);
    public List<OSHomeRoom> rooms = new List<OSHomeRoom>();

    public OSHomeRoom FindRoom(string roomId)
    {
        foreach (var room in rooms) if (room != null && room.id == roomId) return room;
        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("Fill with the chapter-3 map draft")]
    void FillWithDraft()
    {
        UnityEditor.Undo.RecordObject(this, "Fill chapter-3 home layout");
        OSHomeChapter03Draft.Fill(this);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
