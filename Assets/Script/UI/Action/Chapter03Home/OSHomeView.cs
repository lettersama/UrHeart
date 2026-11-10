using System;
using System.Collections.Generic;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The chapter-3 「家」 view, frontend only: a Baba Is You-style room in the dark.
///   Grid    - 15 x 10 tiles of 16 px; things are words, the floor is dots, walls
///             are hand-drawn wobbly lines; every glyph "boils" (bands of rows
///             slip by 1 px, three frames).
///   Dark    - nothing is seen except around 「我」 and inside lamp light. Lights
///             follow OSChapter02NightfallPresentation.LightCircle: a full core,
///             then three Bayer steps of `band` px (wider = softer). Hover labels
///             only show on tiles some light reaches.
///   Walk    - hover shows a dotted box, a click turns it solid; 「我」 walks tile
///             by tile (BFS), 「咚」 follows one tile behind in red.
///   Effects - pixel water (crests drift, stepping in inverts 「我」's tile and
///             spreads ripples), a dim reversed reflection in the wall mirror,
///             yard rain and mud footprints, 咚 beating with the heartbeat panel
///             and walking into 「我」.
/// It never writes story, flags or save data: the backend listens to
/// ThingActivated / DoorReached / RoomEntered and calls the public methods.
/// One RGBA texture, one logical pixel per texel, redrawn in LateUpdate.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage))]
public sealed partial class OSHomeView : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerDownHandler, IPointerClickHandler
{
    const int W = OSHomeLayout.PixelWidth, H = OSHomeLayout.PixelHeight, T = OSHomeLayout.Tile;
    const int LogicalHeight = 360;
    static readonly byte[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    static readonly int[] WaterRows = { 3, 8, 13 };

    public enum DongMode { Follow, Hidden }

    /// <summary>Where everyone stands; enough to put the view back after a reload.</summary>
    [Serializable]
    public struct State
    {
        public string room;
        public Vector2Int me, dong;
        public bool dongHidden;
    }

    [Header("Data")]
    [Tooltip("Rooms to show. Empty: the chapter-3 map draft (OSHomeChapter03Draft).")]
    [SerializeField] OSHomeLayout layout;
    [Tooltip("Enter the layout's first room on enable. Off when the backend calls EnterRoom itself.")]
    [SerializeField] bool startOnEnable;

    [Header("Glyphs")]
    [Tooltip("Font the words are rasterised from. Empty: the memory window's text font.")]
    [SerializeField] TMP_FontAsset glyphFont;
    [SerializeField, Range(9f, 16f)] float glyphEm = 13f;
    [SerializeField, Range(.1f, .9f)] float glyphCoverage = .34f;
    [SerializeField] string meGlyphKey = "os.home.me.glyph";
    [SerializeField] string dongGlyphKey = "os.home.dong.glyph";

    [Header("Light (logical px)")]
    [SerializeField] float meCore = 20f;
    [SerializeField] float meBand = 4f;
    [Tooltip("Multiplies 「我」's light, core and bands alike (pancake 10-24: 1.5).")]
    [SerializeField, Min(0f)] float meLightScale = 1.5f;
    [SerializeField, Min(.2f)] float breathSeconds = 1.4f;
    [SerializeField, Range(0f, 1f)] float breathDepth = .12f;
    [Tooltip("Show a label only while some light reaches the thing (see MarkLitTiles).")]
    [SerializeField] bool labelsNeedLight = true;
    [Tooltip("A Faulty lamp cuts out once every this many seconds, picked at random each time.")]
    [SerializeField] Vector2 faultEverySeconds = new Vector2(6f, 10f);

    [Header("Motion (seconds, unscaled)")]
    [SerializeField, Min(.04f)] float secondsPerTile = .16f;
    [SerializeField, Min(.05f)] float boilSeconds = .2f;
    [SerializeField, Min(.02f)] float fadeStepSeconds = .06f;
    [SerializeField, Min(.05f)] float rippleSeconds = 2.2f;
    [SerializeField, Min(.05f)] float clickFlashSeconds = .25f;

    [Header("Palette")]
    [SerializeField] Color32 gray = new Color32(0x87, 0x82, 0x7E, 0xFF);
    [SerializeField] Color32 yellow = new Color32(0xC8, 0xB0, 0x48, 0xFF);
    [SerializeField] Color32 blue = new Color32(0x58, 0x88, 0xC0, 0xFF);
    [SerializeField] Color32 hoverInk = new Color32(0x87, 0x82, 0x7E, 0xFF);
    [SerializeField] Color32 clickInk = new Color32(0xDC, 0xD8, 0xD4, 0xFF);

    [Header("Hover label (optional; created when empty)")]
    [SerializeField] RectTransform tooltip;
    [SerializeField] TMP_Text tooltipLabel;
    [SerializeField] float tooltipFontSize = 12f;

    [Header("OS")]
    [Tooltip("Ask the OS night overlay to leave this window's body alone; the room makes its own dark.")]
    [SerializeField] bool registerNightGlow = true;
    [Tooltip("Bump 「咚」 whenever the heartbeat panel starts a beat.")]
    [SerializeField] bool followHeartbeat = true;

    // Palette indices in the ink buffer.
    const byte Black = 0, Gray = 1, Red = 2, Yellow = 3, Blue = 4, Hover = 5, ClickInk = 6, VisitedInk = 7;

    sealed class Retext { public OSHomeGlyphBook.Word from; public float start, duration; }
    /// <summary>A paper lying on or in a thing: its word in red over the thing's own until it is taken.</summary>
    sealed class PaperMark { public string text, label; public int left, width; public Vector2Int? extra; }
    readonly Dictionary<string, PaperMark> paperMarks = new Dictionary<string, PaperMark>();
    float MeScale => playerLightScale * meLightScale;
    struct Ripple { public Vector2 centre; public float start; }

    public event Action<OSHomeThing> ThingActivated;
    public event Action<OSHomeDoor> DoorReached;
    public event Action<OSHomeRoom> RoomEntered;
    /// <summary>A player click accepted a route to ordinary floor, before walking begins.</summary>
    public event Action<Vector2Int> FloorClicked;
    /// <summary>「我」 arrived on a tile; true when it is water (for a splash sound).</summary>
    public event Action<Vector2Int, bool> Stepped;

    readonly OSHomeGlyphBook glyphs = new OSHomeGlyphBook();
    readonly List<Vector2Int> path = new List<Vector2Int>();
    readonly List<Ripple> ripples = new List<Ripple>();
    readonly Dictionary<string, Retext> retexts = new Dictionary<string, Retext>();
    // Tiles some light reaches, marked generously from each light's footprint; hover labels read it.
    readonly bool[] litTiles = new bool[OSHomeLayout.Columns * OSHomeLayout.Rows];
    // Faulty lamps: when each one's next cut-out starts.
    readonly Dictionary<string, float> faultAt = new Dictionary<string, float>();
    readonly HashSet<string> visited = new HashSet<string>();
    readonly Queue<Vector2Int> frontier = new Queue<Vector2Int>();
    readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
    readonly List<int> wallJitter = new List<int>();

    RawImage image;
    Texture2D texture;
    Color32[] pixels;
    byte[] ink, lit;
    Color32[] palette;
    OSHomeLayout runtime;
    OSHomeRoom room;
    float now, dt;
    float lightningStart = -10f;
    int boilFrame, bayerX, bayerY;
    float lightScale = 1f;
    float playerLightScale = 1f;
    float presentationBlackout;
    bool allLightsOn = true, playerVisible = true, rainEnabled;
    bool scriptWalking;

    Vector2Int me, meFrom, dong, dongFrom;
    float stepClock = -1f;
    bool rippleSpawned;
    DongMode dongMode = DongMode.Follow;
    float dongBumpUntil = -1f, absorbStart = -1f, absorbSeconds;
    readonly HashSet<string> pulsingLights = new HashSet<string>();
    readonly Dictionary<string, float> warmingLights = new Dictionary<string, float>();
    sealed class Pop { public Vector2 from; public float start, duration; }
    readonly Dictionary<string, Pop> pops = new Dictionary<string, Pop>();
    Action absorbDone, walkDone;
    OSHomeThing pendingThing;
    OSHomeDoor pendingDoor;

    // Room changes run as a dithered fade: out, swap, in.
    int fadePhase; // 0 none, 1 out, 2 in
    float fadeClock;
    string fadeRoom;
    Vector2Int fadeTile, fadeDong;
    bool fadeHasDong;

    bool pointerInside;
    Vector2 pointerScreen;
    Camera pointerCamera;
    Vector2Int hoverTile = new Vector2Int(-1, -1), clickTile = new Vector2Int(-1, -1);
    float clickUntil = -1f;
    string tooltipFor;
    float lastBeatAge = float.MaxValue;
    OSChapter02NightfallPresentation nightfall;
    OSWindowUnit window;

    public OSHomeLayout Layout => runtime;
    /// <summary>Glyph cache shared with the item bar under the view.</summary>
    public OSHomeGlyphBook Glyphs => glyphs;
    public OSHomeRoom CurrentRoom => room;
    public Vector2Int PlayerTile => me;
    public Vector2Int DongTile => dong;
    public DongMode Dong => dongMode;
    public bool IsWalking => stepClock >= 0f || path.Count > 0;
    public bool IsScriptWalking => scriptWalking && IsWalking;
    public bool IsTransitioning => fadePhase != 0;
    public bool IsDissolving => retexts.Count > 0;
    public float PlayerLightScale => playerLightScale;
    public float PresentationBlackout => presentationBlackout;
    public bool PlayerVisible => playerVisible;
    public bool AllLightsOn => allLightsOn;
    public bool RainEnabled => rainEnabled;
    /// <summary>Backend gate for clicks. Scripted walking continues while this is false.</summary>
    public bool InputEnabled { get; set; } = true;
    /// <summary>Pause animation and movement without losing a scripted route or its callback.</summary>
    public bool MotionPaused { get; set; }
    public string CurrentRoomTitle => room != null ? Localize(room.titleKey, room.title) : string.Empty;

    // ================================================================ backend API

    /// <summary>Use these rooms. A runtime copy is kept, so the asset is never modified.</summary>
    public void Load(OSHomeLayout source)
    {
        if (runtime != null && runtime != layout) Destroy(runtime);
        runtime = source != null ? Instantiate(source) : OSHomeChapter03Draft.Create();
        room = null;
        path.Clear(); stepClock = -1f; pendingThing = null; pendingDoor = null;
        retexts.Clear(); ripples.Clear(); faultAt.Clear(); visited.Clear(); paperMarks.Clear();
        warmingLights.Clear(); pops.Clear(); bloodPath = null; bloodPathRoom = null;
        fadePhase = 0; lightScale = 1f;
        playerLightScale = 1f; allLightsOn = true; playerVisible = true; rainEnabled = false;
        presentationBlackout = 0f;
        walkDone = null; absorbDone = null; absorbStart = -1f;
        dongMode = DongMode.Follow;
        scriptWalking = false; MotionPaused = false;
        SetZoom(false, true);
    }

    /// <summary>Show a room with 「我」 on `tile`. fade: the dithered blackout used by doors.</summary>
    public bool EnterRoom(string roomId, Vector2Int tile, bool fade = false)
    {
        return EnterRoom(roomId, tile, null, fade);
    }

    public bool EnterRoom(string roomId, Vector2Int tile, Vector2Int? dongTile, bool fade = false)
    {
        if (runtime == null) Load(layout);
        if (runtime.FindRoom(roomId) == null)
        {
            Debug.LogError("[Home] Missing room: " + roomId, this);
            return false;
        }
        if (fade && room != null)
        {
            fadePhase = 1; fadeClock = 0f; fadeRoom = roomId; fadeTile = tile;
            fadeHasDong = dongTile.HasValue; fadeDong = dongTile.GetValueOrDefault();
            path.Clear(); pendingThing = null; pendingDoor = null; walkDone = null; scriptWalking = false;
            return true;
        }
        SwapRoom(roomId, tile, dongTile);
        return true;
    }

    /// <summary>Walk 「我」 to a tile by script (ignores InputEnabled). Done is called on arrival.</summary>
    public bool WalkTo(Vector2Int tile, Action done = null)
    {
        if (room == null || !FindPath(me, t => t == tile)) return false;
        pendingThing = null; pendingDoor = null; walkDone = done;
        scriptWalking = true;
        if (path.Count == 0 && stepClock < 0f)
        {
            walkDone = null;
            scriptWalking = false;
            done?.Invoke();
        }
        return true;
    }

    public void StopWalking()
    {
        path.Clear(); pendingThing = null; pendingDoor = null; walkDone = null;
        scriptWalking = false;
    }

    public void SetThingVisible(string thingId, bool visible)
    {
        var thing = FindThingAnywhere(thingId);
        if (thing != null) thing.hidden = !visible;
    }

    public void SetThingInk(string thingId, OSHomeInk value)
    {
        var thing = FindThingAnywhere(thingId);
        if (thing != null) thing.ink = value;
    }

    /// <summary>
    /// Replace a thing's word: a localization key or literal text. With dissolve > 0
    /// the old word melts into the new one through the 4x4 dither (the mirror).
    /// </summary>
    public void SetThingText(string thingId, string keyOrText, float dissolveSeconds = 0f)
    {
        var thing = FindThingAnywhere(thingId);
        if (thing == null) return;
        if (thing.width <= 0) thing.width = thing.Width; // the word may change length, the furniture does not
        var before = glyphs.Layout(ThingText(thing), thing.Width);
        thing.glyphKey = keyOrText;
        thing.glyph = keyOrText;
        if (dissolveSeconds > 0f) retexts[thingId] = new Retext { from = before, start = now, duration = dissolveSeconds };
        else retexts.Remove(thingId);
    }

    /// <summary>
    /// Show a paper on a thing: `text` (「日记」, 「便条」, 「信」) in red over the thing's own word,
    /// `label` as its hover name. Null or empty takes it away and the thing's word shows again.
    /// A word one tile longer than the thing takes a free tile beside it, which then clicks the thing.
    /// </summary>
    public void SetThingPaper(string thingId, string text, string label = null)
    {
        var thing = FindThingAnywhere(thingId);
        if (thing == null) return;
        if (paperMarks.TryGetValue(thingId, out var old))
        {
            if (old.extra.HasValue) thing.hitTiles?.Remove(old.extra.Value);
            paperMarks.Remove(thingId);
        }
        if (string.IsNullOrEmpty(text)) return;
        var mark = new PaperMark { text = text, label = label, left = thing.tile.x, width = thing.Width };
        int need = OSHomeGlyphBook.IsWide(text) ? new System.Globalization.StringInfo(text).LengthInTextElements : 2;
        if (need == thing.Width + 1)
        {
            OSHomeRoom home = null;
            foreach (var r in AllRooms()) if (r.FindThing(thingId) == thing) { home = r; break; }
            var right = new Vector2Int(thing.tile.x + thing.Width, thing.tile.y);
            var left = new Vector2Int(thing.tile.x - 1, thing.tile.y);
            Vector2Int? cell = FreeFloor(home, right) ? right : FreeFloor(home, left) ? left : (Vector2Int?)null;
            if (cell.HasValue)
            {
                mark.extra = cell; mark.width = need; mark.left = Mathf.Min(thing.tile.x, cell.Value.x);
                if (thing.hitTiles == null) thing.hitTiles = new List<Vector2Int>();
                thing.hitTiles.Add(cell.Value);
            }
        }
        paperMarks[thingId] = mark;
    }

    static bool FreeFloor(OSHomeRoom r, Vector2Int t)
    {
        if (r == null || !r.floor.Contains(t) || r.empties.Contains(t)) return false;
        foreach (var other in r.things) if (other != null && !other.hidden && other.Covers(t)) return false;
        foreach (var door in r.doors) if (door != null && door.tile == t) return false;
        return true;
    }

    /// <summary>Let a word show in the dark, e.g. the mirror while 「主人」 looks back.</summary>
    public void SetThingGlow(string thingId, bool glow)
    {
        var thing = FindThingAnywhere(thingId);
        if (thing != null) thing.glow = glow;
    }

    public void SetDoorLocked(string doorId, bool locked)
    {
        foreach (var r in AllRooms()) { var door = r.FindDoor(doorId); if (door != null) door.locked = locked; }
    }

    public void SetLightOn(string lightId, bool on)
    {
        foreach (var r in AllRooms()) { var light = r.FindLight(lightId); if (light != null) light.on = on; }
    }

    /// <summary>Switch a lamp on the way an old tube starts: a few stutters, then steady (the lab's bed lamp).</summary>
    public void WarmLight(string lightId)
    {
        SetLightOn(lightId, true);
        if (!string.IsNullOrEmpty(lightId)) warmingLights[lightId] = now;
    }

    /// <summary>A hidden thing hops out of another thing's tile onto its own (the box opening).</summary>
    public void PopThing(string thingId, string fromThingId, float seconds)
    {
        var thing = FindThingAnywhere(thingId);
        var from = FindThingAnywhere(fromThingId);
        if (thing == null || from == null) return;
        thing.hidden = false;
        pops[thingId] = new Pop { from = new Vector2(from.tile.x * T, from.tile.y * T), start = now, duration = Mathf.Max(.05f, seconds) };
    }

    /// <summary>Scales the player's light radius, including its dither bands. Zero removes it.</summary>
    public void SetPlayerLightScale(float scale) => playerLightScale = Mathf.Clamp(scale, 0f, 4f);

    public void SetPlayerVisible(bool visible) => playerVisible = visible;

    /// <summary>Master switch for room lamps. Individual SetLightOn values are preserved.</summary>
    public void SetAllLightsOn(bool on) => allLightsOn = on;

    /// <summary>Final map-output fade, including self-lit words and rule glyphs.</summary>
    public void SetPresentationBlackout(float amount)
    {
        presentationBlackout = Mathf.Clamp01(amount);
        if (presentationBlackout >= 1f) HideTooltip();
    }

    /// <summary>Rain is painted only in outdoor rooms. Does not change lights or story state.</summary>
    public void SetRainEnabled(bool enabled) => rainEnabled = enabled;

    /// <summary>Visited markers come from the story's saved flags, never from merely seeing an object.</summary>
    public void SetThingVisited(string thingId, bool value)
    {
        if (string.IsNullOrEmpty(thingId)) return;
        if (value) visited.Add(thingId);
        else visited.Remove(thingId);
    }

    public bool IsThingVisited(string thingId) => !string.IsNullOrEmpty(thingId) && visited.Contains(thingId);

    public void SetVisitedThings(IEnumerable<string> thingIds)
    {
        visited.Clear();
        if (thingIds == null) return;
        foreach (string id in thingIds) if (!string.IsNullOrEmpty(id)) visited.Add(id);
    }

    /// <summary>Restore presentation-only footprints; the backend decides whether to save this trail.</summary>
    public void SetFootprints(string roomId, IEnumerable<Vector2Int> tiles)
    {
        var target = runtime != null ? runtime.FindRoom(roomId) : null;
        if (target == null || ReferenceEquals(tiles, target.footprints)) return;
        target.footprints.Clear();
        if (tiles == null) return;
        foreach (var tile in tiles)
            if (target.OnFloor(tile)) target.footprints.Add(tile);
    }

    public void ClearFootprints(string roomId) => SetFootprints(roomId, null);

    public void SetFootprintsVisible(string roomId, bool visible)
    {
        var r = runtime != null ? runtime.FindRoom(roomId) : null;
        if (r != null) r.footprintsVisible = visible;
    }

    public void SetDongVisible(bool visible)
    {
        absorbStart = -1f;
        dongMode = visible ? DongMode.Follow : DongMode.Hidden;
        if (visible && dong == me) dong = BehindMe();
        dongFrom = dong;
    }

    /// <summary>「咚」 walks into 「我」's tile and is gone (after the final diary).</summary>
    public void AbsorbDong(float seconds, Action done = null)
    {
        if (dongMode == DongMode.Hidden) { done?.Invoke(); return; }
        absorbStart = now; absorbSeconds = Mathf.Max(.05f, seconds); absorbDone = done;
        dongFrom = dong;
    }

    /// <summary>One heartbeat: 「咚」 hops by a pixel. Called automatically when followHeartbeat is on.</summary>
    public void BeatDong() => dongBumpUntil = now + .12f;

    /// <summary>A light that breathes slowly (the bed door's slit once the evidence is in).</summary>
    public void SetLightPulse(string lightId, bool pulse)
    {
        if (string.IsNullOrEmpty(lightId)) return;
        if (pulse) pulsingLights.Add(lightId); else pulsingLights.Remove(lightId);
    }

    public void Teleport(Vector2Int tile)
    {
        path.Clear(); stepClock = -1f;
        pendingThing = null; pendingDoor = null; walkDone = null;
        scriptWalking = false;
        me = meFrom = tile;
        dong = dongFrom = BehindMe();
    }

    public OSHomeThing FindThing(string thingId) => FindThingAnywhere(thingId);

    /// <summary>
    /// Room and positions only. Visibility, locks, lights and words are the backend's
    /// flags: re-apply them after RestoreState (or Load) from the save.
    /// </summary>
    public State CaptureState() => new State
    {
        room = room != null ? room.id : null,
        me = me, dong = dong, dongHidden = dongMode == DongMode.Hidden
    };

    public bool RestoreState(State state)
    {
        if (string.IsNullOrEmpty(state.room)) return false;
        dongMode = state.dongHidden ? DongMode.Hidden : DongMode.Follow;
        absorbStart = -1f;
        return EnterRoom(state.room, state.me, (Vector2Int?)state.dong);
    }

    /// <summary>Add a 240 x 160 view under `parent` (a window body), centred, one texel per logical px.</summary>
    public static OSHomeView CreateIn(RectTransform parent, OSHomeLayout source = null)
    {
        var node = new GameObject("Chapter03Home", typeof(RectTransform), typeof(RawImage));
        var rect = (RectTransform)node.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(W, H);
        rect.anchoredPosition = Vector2.zero;
        node.layer = parent.gameObject.layer;
        var view = node.AddComponent<OSHomeView>();
        view.Load(source);
        return view;
    }

#if UNITY_EDITOR
    [ContextMenu("Preview: wake up in the study")]
    void PreviewStudy()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Home] Enter Play Mode first.", this); return; }
        Load(layout);
        EnterRoom(runtime.firstRoom, runtime.firstTile, (Vector2Int?)runtime.firstDongTile);
    }
#endif

    // ================================================================ lifecycle

    void Awake()
    {
        image = GetComponent<RawImage>();
        RefreshPalette();
    }

    void OnEnable()
    {
        GameEvents.LanguageChanged += HandleLanguageChanged;
        EnsureTexture();
        if (runtime == null) Load(layout);
        if (startOnEnable && room == null)
            EnterRoom(runtime.firstRoom, runtime.firstTile, (Vector2Int?)runtime.firstDongTile);
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        if (nightfall != null && window != null) nightfall.RemoveGlow(window.WindowRect);
        nightfall = null;
        pointerInside = false;
        HideTooltip();
    }

    void OnDestroy()
    {
        if (texture != null) Destroy(texture);
        if (runtime != null && runtime != layout) Destroy(runtime);
    }

    void OnValidate()
    {
        RefreshPalette();
    }

    void HandleLanguageChanged(LanguageState language)
    {
        glyphs.Clear();
        retexts.Clear();
        tooltipFor = null;
        RelayoutRules();
    }

    void LateUpdate()
    {
        OS_Handle os = OS_Handle.Instance;
        bool frozen = MotionPaused || (os != null && !os.IsOSVisible);
        dt = frozen ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        now += dt;
        boilFrame = Mathf.FloorToInt(now / boilSeconds) % 3;
        EnsureTexture();
        EnsureGlyphFont();
        RegisterNight(os);
        if (followHeartbeat) WatchHeartbeat(os);
        TickRules();
        if (room == null) { Clear(); Upload(); HideTooltip(); return; }

        TickFade();
        TickWalk();
        TickDong();
        UpdateBayerPhase();
        Paint();
        TickZoom();
        Upload();
        UpdateHover();
    }

    // ================================================================ input

    public void OnPointerEnter(PointerEventData e) { pointerInside = true; Track(e); }
    public void OnPointerExit(PointerEventData e) { pointerInside = false; hoverTile = new Vector2Int(-1, -1); HideTooltip(); }
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
        if (!AcceptsInput() || !ToTile(e.position, e.pressEventCamera, out Vector2Int tile)) return;
        clickTile = tile; clickUntil = now + clickFlashSeconds;
        Click(tile);
    }

    void Track(PointerEventData e)
    {
        pointerScreen = e.position;
        pointerCamera = e.enterEventCamera != null ? e.enterEventCamera : e.pressEventCamera;
    }

    bool AcceptsInput()
    {
        if (!InputEnabled || room == null || fadePhase != 0 || absorbStart >= 0f || Zoomed) return false;
        OS_Handle os = OS_Handle.Instance;
        if (os != null && (!os.IsOSVisible || os.IsSystemInputLocked)) return false;
        return window == null || (window.IsOpen && !window.IsMinimized && !window.IsOpening && !window.IsClosing);
    }

    void Click(Vector2Int tile)
    {
        walkDone = null;
        scriptWalking = false;
        var door = DoorAt(tile);
        if (door != null)
        {
            if (me == tile) { ArriveAtDoor(door); return; }
            if (FindPath(me, t => t == tile)) { pendingThing = null; pendingDoor = door; }
            return;
        }
        var thing = ThingAt(tile);
        if (thing != null && thing.interactive)
        {
            bool perimeterHit = thing.id == room.boundaryThingId && IsBoundaryTile(tile) && !thing.Covers(tile);
            Func<Vector2Int, bool> near = perimeterHit
                ? t => Mathf.Abs(t.x - tile.x) + Mathf.Abs(t.y - tile.y) == 1
                : t => Adjacent(t, thing);
            if (near(me) && stepClock < 0f)
            {
                path.Clear(); pendingThing = null; pendingDoor = null;
                Activate(thing);
                return;
            }
            if (FindPath(me, near)) { pendingDoor = null; pendingThing = thing; }
            return;
        }
        if (thing == null && Walkable(tile) && FindPath(me, t => t == tile))
        {
            pendingThing = null; pendingDoor = null;
            FloorClicked?.Invoke(tile);
        }
    }

    void Activate(OSHomeThing thing)
    {
        ThingActivated?.Invoke(thing);
    }

    void ArriveAtDoor(OSHomeDoor door)
    {
        DoorReached?.Invoke(door);
        if (door.locked || string.IsNullOrEmpty(door.targetRoom) || runtime.FindRoom(door.targetRoom) == null) return;
        // 咚 comes in through the same doorway behind 「我」.
        var target = runtime.FindRoom(door.targetRoom);
        Vector2Int? behind = null;
        foreach (var back in target.doors)
            if (back != null && back.targetRoom == room.id) { behind = back.tile; break; }
        EnterRoom(door.targetRoom, door.arriveTile, behind, true);
    }

    // ================================================================ walking

    bool FindPath(Vector2Int start, Func<Vector2Int, bool> goal)
    {
        // While mid-step, plan from the tile being entered.
        Vector2Int origin = stepClock >= 0f ? me : start;
        frontier.Clear(); cameFrom.Clear();
        frontier.Enqueue(origin); cameFrom[origin] = origin;
        Vector2Int found = origin;
        bool ok = goal(origin);
        while (!ok && frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var step in Steps)
            {
                var next = current + step;
                if (cameFrom.ContainsKey(next) || !InGrid(next)) continue;
                bool target = goal(next);
                if (!Walkable(next) && !(target && DoorAt(next) != null)) continue;
                cameFrom[next] = current;
                if (target) { found = next; ok = true; break; }
                if (DoorAt(next) == null) frontier.Enqueue(next); // doors are ends, not corridors
            }
        }
        if (!ok) return false;
        path.Clear();
        for (var t = found; t != origin; t = cameFrom[t]) path.Add(t);
        path.Reverse();
        return true;
    }

    void TickWalk()
    {
        if (dt <= 0f || fadePhase != 0) return;
        if (stepClock < 0f && path.Count > 0)
        {
            meFrom = me; me = path[0]; path.RemoveAt(0);
            stepClock = 0f; rippleSpawned = false;
            if (dongMode == DongMode.Follow && absorbStart < 0f && dong != meFrom) { dongFrom = dong; dong = meFrom; }
        }
        if (stepClock < 0f) return;
        stepClock += dt;
        float progress = stepClock / secondsPerTile;
        if (!rippleSpawned && progress >= .5f && IsWater(me))
        {
            rippleSpawned = true;
            ripples.Add(new Ripple { centre = TileCentre(me), start = now });
        }
        if (progress < 1f) return;
        stepClock = -1f; meFrom = me; dongFrom = dong;
        if (room.recordFootprints && room.OnFloor(me))
        {
            var trail = room.footprints;
            if (trail.Count == 0 || trail[trail.Count - 1] != me) trail.Add(me);
            if (trail.Count > 512) trail.RemoveAt(0);
        }
        Stepped?.Invoke(me, IsWater(me));
        if (path.Count > 0) return;
        scriptWalking = false;
        if (pendingThing != null) { var thing = pendingThing; pendingThing = null; Activate(thing); }
        else if (pendingDoor != null) { var door = pendingDoor; pendingDoor = null; ArriveAtDoor(door); }
        else if (walkDone != null) { var done = walkDone; walkDone = null; done(); }
    }

    void TickDong()
    {
        if (absorbStart < 0f) return;
        if (now - absorbStart < absorbSeconds) return;
        absorbStart = -1f;
        dongMode = DongMode.Hidden;
        dong = dongFrom = me;
        var done = absorbDone; absorbDone = null;
        done?.Invoke();
    }

    void TickFade()
    {
        if (fadePhase == 0) { lightScale = 1f; return; }
        fadeClock += dt;
        int step = Mathf.Min(4, Mathf.FloorToInt(fadeClock / fadeStepSeconds));
        if (fadePhase == 1)
        {
            lightScale = 1f - step * .25f;
            if (step >= 4)
            {
                SwapRoom(fadeRoom, fadeTile, fadeHasDong ? (Vector2Int?)fadeDong : null);
                fadePhase = 2; fadeClock = 0f;
            }
        }
        else
        {
            lightScale = step * .25f;
            if (step >= 4) { fadePhase = 0; lightScale = 1f; }
        }
    }

    void SwapRoom(string roomId, Vector2Int tile, Vector2Int? dongTile)
    {
        room = runtime.FindRoom(roomId);
        rules = null; // a reasoning show holds the view black until the next room
        path.Clear(); stepClock = -1f; pendingThing = null; pendingDoor = null; walkDone = null; scriptWalking = false;
        ripples.Clear();
        me = meFrom = tile;
        dong = dongFrom = dongTile.HasValue ? dongTile.Value : BehindMe();
        if (dongMode == DongMode.Hidden) dong = dongFrom = me;
        wallJitter.Clear();
        HideTooltip();
        RoomEntered?.Invoke(room);
    }

    Vector2Int BehindMe()
    {
        foreach (var step in Steps)
            if (Walkable(me + step)) return me + step;
        return me;
    }

    // ================================================================ grid queries

    static bool InGrid(Vector2Int t) => t.x >= 0 && t.y >= 0 && t.x < OSHomeLayout.Columns && t.y < OSHomeLayout.Rows;

    bool Walkable(Vector2Int t)
    {
        if (room == null || !InGrid(t)) return false;
        if (!room.OnFloor(t) && DoorAt(t) == null) return false;
        var thing = ThingAt(t);
        return thing == null || !thing.blocksWalk;
    }

    OSHomeDoor DoorAt(Vector2Int t)
    {
        if (room == null) return null;
        foreach (var door in room.doors) if (door != null && door.tile == t) return door;
        return null;
    }

    OSHomeThing ThingAt(Vector2Int t)
    {
        if (room == null) return null;
        foreach (var thing in room.things) if (thing != null && !thing.hidden && thing.Covers(t)) return thing;
        if (IsBoundaryTile(t) && !string.IsNullOrEmpty(room.boundaryThingId))
        {
            var boundary = room.FindThing(room.boundaryThingId);
            if (boundary != null && !boundary.hidden) return boundary;
        }
        return null;
    }

    bool IsBoundaryTile(Vector2Int t)
    {
        if (room == null || string.IsNullOrEmpty(room.boundaryGlyph)) return false;
        int left = room.floor.xMin - 1, right = room.floor.xMax;
        int top = room.floor.yMin - 1, bottom = room.floor.yMax;
        if (room.houseWallTop && t.y == top) return false;
        return t.x >= left && t.x <= right && t.y >= top && t.y <= bottom
            && (t.x == left || t.x == right || t.y == top || t.y == bottom);
    }

    static bool Adjacent(Vector2Int t, OSHomeThing thing)
    {
        if (!thing.blocksWalk && thing.Covers(t)) return true;
        int reach = Mathf.Clamp(thing.approachDistance, 1, 3);
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
                if (Mathf.Abs(dx) + Mathf.Abs(dy) <= reach && thing.Covers(t + new Vector2Int(dx, dy))) return true;
        return false;
    }

    bool IsWater(Vector2Int t) => room != null && room.water.Contains(t);

    static Vector2 TileCentre(Vector2Int t) => new Vector2(t.x * T + T * .5f, t.y * T + T * .5f);

    IEnumerable<OSHomeRoom> AllRooms()
    {
        if (runtime == null) yield break;
        foreach (var r in runtime.rooms) if (r != null) yield return r;
    }

    OSHomeThing FindThingAnywhere(string thingId)
    {
        foreach (var r in AllRooms()) { var thing = r.FindThing(thingId); if (thing != null) return thing; }
        return null;
    }

    bool ToTile(Vector2 screen, Camera camera, out Vector2Int tile)
    {
        tile = new Vector2Int(-1, -1);
        var rect = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, camera, out Vector2 local)) return false;
        Rect r = rect.rect;
        if (r.width <= 0f || r.height <= 0f) return false;
        // The close-up shows only part of the texture (OSHomeView.Zoom).
        Rect uv = image != null ? image.uvRect : new Rect(0f, 0f, 1f, 1f);
        float px = (uv.x + (local.x - r.xMin) / r.width * uv.width) * W;
        float py = (1f - uv.y - uv.height + (r.yMax - local.y) / r.height * uv.height) * H;
        tile = new Vector2Int(Mathf.FloorToInt(px / T), Mathf.FloorToInt(py / T));
        return InGrid(tile);
    }

    // ================================================================ painting

    void Paint()
    {
        Clear();
        PaintLights();
        DimLightsForRules();
        MarkLitTiles();

        // Floor: one dot per free tile; water; footprints; 主人's empty places.
        for (int y = room.floor.yMin; y < room.floor.yMax; y++)
            for (int x = room.floor.xMin; x < room.floor.xMax; x++)
            {
                var t = new Vector2Int(x, y);
                if (IsWater(t)) { PaintWater(t); continue; }
                if (ThingAt(t) != null || room.empties.Contains(t) || t == me || t == meFrom
                    || (dongMode == DongMode.Follow && (t == dong || t == dongFrom))) continue;
                if (room.stones.Contains(t)) PaintStone(t);
                else if (room.garden.Contains(t)) PaintSprouts(t);
                else if (room.blood.Contains(t)) continue; // the smear is drawn over these below
                else Put(x * T + 8, y * T + 8, Gray);
            }
        PaintRipples();
        PaintBloodTrail();
        if (room.footprintsVisible) PaintFootprints();
        foreach (var t in room.empties)
        {
            var thing = ThingAt(t);
            PaintEmpty(t, thing != null && visited.Contains(thing.id) ? VisitedInk : Gray);
        }
        PaintWalls();
        foreach (var door in room.doors)
            if (door != null && !Occupied(door.tile))
                PaintWord(glyphs.Layout(Localize(door.glyphKey, door.glyph), 1), door.tile.x * T, door.tile.y * T,
                    door.locked && visited.Contains(door.id) ? VisitedInk : InkOf(door.ink), Hash(door.id), null, 0f);
        foreach (var thing in room.things)
        {
            if (thing == null || thing.hidden) continue;
            if (thing.style == OSHomeThingStyle.DashedEmpty)
            {
                if (!room.empties.Contains(thing.tile)) PaintEmpty(thing.tile, ThingInk(thing));
                continue;
            }
            if (thing.style == OSHomeThingStyle.WallMirror)
            {
                PaintMirror(thing);
                continue;
            }
            if (paperMarks.TryGetValue(thing.id, out var paper))
            {
                PaintWord(glyphs.Layout(paper.text, paper.width), paper.left * T, thing.tile.y * T,
                    Red, Hash(thing.id), null, 0f, thing.glow);
                continue;
            }
            Retext melt = null;
            float progress = 0f;
            if (retexts.TryGetValue(thing.id, out melt))
            {
                progress = (now - melt.start) / melt.duration;
                if (progress >= 1f) { retexts.Remove(thing.id); melt = null; }
            }
            Vector2Int at = thing.tile * T;
            if (pops.TryGetValue(thing.id, out var pop))
            {
                float t = (now - pop.start) / pop.duration;
                if (t >= 1f) pops.Remove(thing.id);
                else
                {
                    // Straight out of the box with a small hop, landing on whole pixels.
                    t = Mathf.Clamp01(t);
                    var p = Vector2.Lerp(pop.from, at, 1f - (1f - t) * (1f - t)) + Vector2.down * Mathf.Sin(t * Mathf.PI) * 6f;
                    at = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                }
            }
            PaintWord(glyphs.Layout(ThingText(thing), thing.Width), at.x, at.y,
                ThingInk(thing), Hash(thing.id), melt != null ? melt.from : null, progress, thing.glow);
            if (room.showVisitedMarkers && thing.interactive && !visited.Contains(thing.id)) PaintUnreadMarker(thing);
        }
        if (rainEnabled && room.outdoors) PaintRain();
        PaintRules();
        if (!RulesHideDong) PaintDong();
        if (playerVisible && !RulesHideMe) PaintMe();
        if (rainEnabled && room.outdoors && playerVisible) PaintRainSplash();
        if (AcceptsInput()) PaintHoverBox();
    }

    void Clear()
    {
        Array.Clear(ink, 0, ink.Length);
        Array.Clear(lit, 0, lit.Length);
    }

    void PaintLights()
    {
        // At the brightest of a flash the whole room shows for a moment, half dithered away.
        if (allLightsOn && LightningScale() >= 1f && room.lights.Exists(l => l != null && l.kind == OSHomeLightKind.Lightning))
            for (int i = 0; i < lit.Length; i++) if (lit[i] < 8) lit[i] = 8;
        foreach (var light in room.lights)
        {
            if (!allLightsOn || light == null || !light.on) continue;
            float scale = 1f, core = light.core;
            if (light.kind == OSHomeLightKind.Breathing)
                core *= 1f - breathDepth + breathDepth * Mathf.Sin(now / breathSeconds * Mathf.PI * 2f);
            if (light.kind == OSHomeLightKind.Flicker && Flickering()) scale = .25f;
            if (light.kind == OSHomeLightKind.Faulty) scale = FaultScale(light);
            if (light.kind == OSHomeLightKind.Lightning) scale = LightningScale();
            OS_Handle.Instance?.Chapter03Story?.ObserveLampSfx(this, light, core / Mathf.Max(.1f, light.core));
            if (pulsingLights.Contains(light.id)) scale *= .35f + .65f * (.5f + .5f * Mathf.Sin(now * Mathf.PI * 2f / 1.6f));
            if (warmingLights.TryGetValue(light.id, out float warmStart)) scale *= WarmScale(light.id, now - warmStart);
            if (scale <= 0f) continue;
            if (light.kind == OSHomeLightKind.Strip || light.kind == OSHomeLightKind.Lightning)
                LightRect(light.strip, light.band, scale);
            else LightCircle(light.centre, core, light.band, scale);
        }
        if (playerLightScale > 0f)
            LightCircle(MePixel() + new Vector2(8f, 8f), meCore * MeScale, meBand * MeScale, 1f);
    }

    /// <summary>A lightning flash: light leaks round every curtain in the room. The thunder is the caller's.</summary>
    public void Lightning() => lightningStart = now;

    // Flash, out, a longer flash, then it drains away through the dither.
    float LightningScale()
    {
        float age = now - lightningStart;
        if (age < 0f || age >= 1.1f) return 0f;
        if (age < .07f) return 1f;
        if (age < .14f) return 0f;
        if (age < .32f) return 1f;
        return 1f - (age - .32f) / .78f;
    }

    // On, out, two weak catches, out, then it holds; about 0.9 s from the switch.
    static readonly float[] WarmSteps = { 0f, 1f, 0f, 0f, .3f, 0f, .3f, 1f, 0f, 1f, 1f, 0f, 1f };
    float WarmScale(string lightId, float age)
    {
        int step = Mathf.FloorToInt(age / .07f);
        if (age < 0f || step >= WarmSteps.Length) { warmingLights.Remove(lightId); return 1f; }
        return WarmSteps[step];
    }

    bool Flickering()
    {
        // The tube's own rhythm: a few short drop-outs, then a quiet stretch.
        int frame = Mathf.FloorToInt(now / .1f);
        int cycle = frame / 11, inCycle = frame % 11;
        if ((Hash("tube", cycle) & 3) == 0) return false;
        return inCycle == 4 || inCycle == 5 || inCycle == 8;
    }

    /// <summary>
    /// A faulty lamp burns steadily, then once every 6-10 s stutters for a third of a second or so:
    /// out, a weak or full flash or two, out, back on.
    /// </summary>
    float FaultScale(OSHomeLight light)
    {
        string key = light.id ?? string.Empty;
        if (!faultAt.TryGetValue(key, out float start)) faultAt[key] = start = now + NextFaultDelay();
        float age = now - start;
        if (age < 0f) return 1f;
        OS_Handle.Instance?.Chapter03Story?.PlayLampFaultSfx(this, key, start);
        uint h = Hash(key, Mathf.RoundToInt(start * 100f));
        int steps = 6 + (int)(h % 4), step = Mathf.FloorToInt(age / .05f);
        if (step >= steps) { faultAt[key] = now + NextFaultDelay(); return 1f; }
        if (step == 0 || step == steps - 1) return 0f;
        switch (Hash(h, step) % 3)
        {
            case 0: return 0f;
            case 1: return .3f;
            default: return 1f;
        }
    }

    float NextFaultDelay() => UnityEngine.Random.Range(Mathf.Min(faultEverySeconds.x, faultEverySeconds.y),
        Mathf.Max(faultEverySeconds.x, faultEverySeconds.y));

    void LightCircle(Vector2 centre, float core, float band, float scale)
    {
        float outer = core + band * 3f;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(centre.x - outer)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(centre.x + outer));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(centre.y - outer)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(centre.y + outer));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + .5f - centre.x, dy = y + .5f - centre.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                int level = d <= core ? 4 : d <= core + band ? 3 : d <= core + band * 2f ? 2 : d <= outer ? 1 : 0;
                Light(x, y, level, scale);
            }
    }

    void LightRect(RectInt strip, float band, float scale)
    {
        float outer = band * 3f;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(strip.xMin - outer)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(strip.xMax + outer));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(strip.yMin - outer)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(strip.yMax + outer));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(strip.xMin - (x + .5f), x + .5f - strip.xMax));
                float dy = Mathf.Max(0f, Mathf.Max(strip.yMin - (y + .5f), y + .5f - strip.yMax));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                int level = d <= 0f ? 4 : d <= band ? 3 : d <= band * 2f ? 2 : d <= outer ? 1 : 0;
                Light(x, y, level, scale);
            }
    }

    void Light(int x, int y, int level, float scale)
    {
        if (level <= 0) return;
        int value = Mathf.RoundToInt(level * 4 * scale);
        int i = y * W + x;
        if (value > lit[i]) lit[i] = (byte)value;
    }

    /// <summary>Same test as the OS night: a texel stays dark while Bayer &lt; shade.</summary>
    bool Visible(int x, int y)
    {
        int value = Mathf.RoundToInt(lit[y * W + x] * lightScale);
        if (value <= 0) return false;
        int shade = 16 - value;
        return Bayer[(((H - 1 - y) + bayerY) & 3) * 4 + ((x + bayerX) & 3)] >= shade;
    }

    void Put(int x, int y, byte color, int strength = 16)
    {
        if (x < 0 || y < 0 || x >= W || y >= H || !Visible(x, y)) return;
        if (strength < 16 && Bayer[(y & 3) * 4 + (x & 3)] >= strength) return;
        ink[y * W + x] = color;
    }

    /// <summary>
    /// Which tiles any light reaches, worked out from each light's shape instead of its pixels. A tile
    /// counts as soon as the outermost dither step touches it. Flicker and faults are ignored, so a label
    /// does not blink while its lamp stutters; the rules scene shows no labels.
    /// </summary>
    void MarkLitTiles()
    {
        Array.Clear(litTiles, 0, litTiles.Length);
        if (rules != null) return;
        if (allLightsOn)
            foreach (var light in room.lights)
            {
                if (light == null || !light.on || light.kind == OSHomeLightKind.Lightning) continue;
                if (light.kind == OSHomeLightKind.Strip)
                {
                    float o = light.band * 3f;
                    MarkLitRect(light.strip.xMin - o, light.strip.yMin - o, light.strip.xMax + o, light.strip.yMax + o);
                }
                else MarkLitCircle(light.centre, light.core + light.band * 3f);
            }
        if (playerLightScale > 0f)
            MarkLitCircle(MePixel() + new Vector2(8f, 8f), (meCore + meBand * 3f) * MeScale);
    }

    void MarkLitCircle(Vector2 centre, float radius)
    {
        for (int ty = 0; ty < OSHomeLayout.Rows; ty++)
            for (int tx = 0; tx < OSHomeLayout.Columns; tx++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(tx * T - centre.x, centre.x - (tx + 1) * T));
                float dy = Mathf.Max(0f, Mathf.Max(ty * T - centre.y, centre.y - (ty + 1) * T));
                if (dx * dx + dy * dy < radius * radius) litTiles[ty * OSHomeLayout.Columns + tx] = true;
            }
    }

    void MarkLitRect(float x0, float y0, float x1, float y1)
    {
        for (int ty = 0; ty < OSHomeLayout.Rows; ty++)
            for (int tx = 0; tx < OSHomeLayout.Columns; tx++)
                if (x0 < (tx + 1) * T && x1 > tx * T && y0 < (ty + 1) * T && y1 > ty * T)
                    litTiles[ty * OSHomeLayout.Columns + tx] = true;
    }

    bool TileLit(Vector2Int t) => InGrid(t) && litTiles[t.y * OSHomeLayout.Columns + t.x];

    /// <summary>Any of a thing's tiles in some light, or the tile under the pointer.</summary>
    bool ThingLit(OSHomeThing thing, Vector2Int hovered)
    {
        if (thing.glow || TileLit(hovered)) return true;
        for (int i = 0; i < thing.Width; i++)
            if (TileLit(new Vector2Int(thing.tile.x + i, thing.tile.y))) return true;
        return false;
    }

    void PaintWord(OSHomeGlyphBook.Word word, int ox, int oy, byte color, uint seed, OSHomeGlyphBook.Word from, float melt,
        bool glow = false, int strength = 16)
    {
        int tiles = word.width / T;
        int threshold = Mathf.Clamp(Mathf.RoundToInt(melt * 16f), 0, 16);
        for (int tile = 0; tile < tiles; tile++)
        {
            uint boil = Hash(seed + (uint)tile * 7919u, boilFrame);
            for (int y = 0; y < T; y++)
            {
                int shift = BoilShift(boil, y);
                for (int x = 0; x < T; x++)
                {
                    int wx = tile * T + x;
                    bool on = word.Get(wx, y);
                    // Dissolve: each pixel switches from the old word once the 4x4 threshold passes it.
                    if (from != null && Bayer[(y & 3) * 4 + (wx & 3)] >= threshold) on = from.Get(wx, y);
                    if (!on) continue;
                    if (strength < 16 && Bayer[(y & 3) * 4 + (wx & 3)] >= strength) continue;
                    if (glow) Raw(ox + wx + shift, oy + y, color);
                    else Put(ox + wx + shift, oy + y, color);
                }
            }
        }
    }

    /// <summary>1-2 bands of 3-6 rows slip by one pixel; three frames, procedural.</summary>
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

    void PaintWalls()
    {
        if (!string.IsNullOrEmpty(room.boundaryGlyph))
        {
            PaintWordBoundary();
            return;
        }
        int x0 = room.floor.xMin * T - T / 2, x1 = room.floor.xMax * T + T / 2 - 1;
        int y0 = room.floor.yMin * T - T / 2, y1 = room.floor.yMax * T + T / 2 - 1;
        uint seed = Hash(room.id, boilFrame);
        for (int x = x0; x <= x1; x++)
        {
            if (!WallGap(x, y0)) Put(x, y0 + Jitter(seed, x / 5), Gray);
            if (!WallGap(x, y1)) Put(x, y1 + Jitter(seed, x / 5 + 500), Gray);
        }
        for (int y = y0; y <= y1; y++)
        {
            if (!WallGap(x0, y)) Put(x0 + Jitter(seed, y / 5 + 1000), y, Gray);
            if (!WallGap(x1, y)) Put(x1 + Jitter(seed, y / 5 + 1500), y, Gray);
        }
    }

    void PaintWordBoundary()
    {
        var word = glyphs.Layout(Localize(room.boundaryGlyphKey, room.boundaryGlyph), 1);
        byte color = visited.Contains(room.boundaryThingId) ? VisitedInk : Gray;
        int left = room.floor.xMin - 1, right = room.floor.xMax;
        int top = room.floor.yMin - 1, bottom = room.floor.yMax;
        if (room.houseWallTop)
        {
            // The house: one wall line, open at its door and window.
            int wy = room.floor.yMin * T - T / 2;
            uint seed = Hash(room.id, boilFrame);
            for (int x = left * T; x < (right + 1) * T; x++)
                if (!WallGap(x, wy)) Put(x, wy + Jitter(seed, x / 5), Gray);
            top++;
        }
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                var tile = new Vector2Int(x, y);
                if (!IsBoundaryTile(tile) || DoorAt(tile) != null) continue;
                bool named = false;
                foreach (var thing in room.things)
                    if (thing != null && !thing.hidden && thing.Covers(tile)) { named = true; break; }
                if (!named) PaintWord(word, x * T, y * T, color, Hash(room.id, x + y * 17), null, 0f);
            }
    }

    byte ThingInk(OSHomeThing thing) => visited.Contains(thing.id)
        ? VisitedInk : InkOf(thing.ink);

    void PaintUnreadMarker(OSHomeThing thing)
    {
        // Beside the word; edge objects put it on their inner side.
        int x = (thing.tile.x + thing.Width) * T + 1;
        if (x + 3 >= W) x = thing.tile.x * T - 4;
        x = Mathf.Clamp(x, 1, W - 4);
        int y = Mathf.Clamp(thing.tile.y * T + 6, 1, H - 4);
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++) Put(x + dx, y + dy, Gray);
    }

    void PaintMirror(OSHomeThing mirror)
    {
        int left = mirror.tile.x * T, top = mirror.tile.y * T;
        int width = mirror.Width * T;
        bool seen = visited.Contains(mirror.id);
        byte frameInk = seen ? VisitedInk : mirror.interactive ? Red : Gray;
        for (int x = 1; x < width - 1; x++) { Put(left + x, top, frameInk); Put(left + x, top + T - 1, frameInk); }
        for (int y = 1; y < T - 1; y++) { Put(left, top + y, frameInk); Put(left + width - 1, top + y, frameInk); }
        int distance = int.MaxValue;
        for (int x = 0; x < mirror.Width; x++)
            distance = Mathf.Min(distance, Mathf.Abs(me.x - mirror.tile.x - x) + Mathf.Abs(me.y - mirror.tile.y));
        if (!playerVisible || distance > 3) return;

        var reflection = glyphs.Layout(Localize(meGlyphKey, "我"), 1);
        // Keep the reflection aligned with the moving player; only its glyph is reversed.
        int ox = Mathf.Clamp((int)MePixel().x, left + 1, left + width - T - 1);
        uint boil = Hash(Hash("mirror_me"), boilFrame);
        for (int y = 1; y < T - 1; y++)
        {
            int shift = BoilShift(boil, y);
            for (int x = 0; x < T; x++)
            {
                int px = ox + x + shift;
                if (px <= left || px >= left + width - 1 || !reflection.Get(T - 1 - x, y)) continue;
                Put(px, top + y, seen ? VisitedInk : Gray, 8);
            }
        }
    }

    void PaintRain()
    {
        int frame = Mathf.FloorToInt(now / .05f);
        for (int i = 0; i < 42; i++)
        {
            uint seed = Hash((uint)(i + 1), 739);
            int x = (int)(seed % W);
            int y = ((int)((seed >> 8) % H) + frame * (3 + i % 3)) % H;
            for (int p = 0; p < 3; p++) Put(x - p / 2, y + p, Gray, 8);
        }
    }

    void PaintRainSplash()
    {
        int frame = Mathf.FloorToInt(now / .1f);
        if ((frame & 1) != 0) return;
        Vector2 p = MePixel();
        int x = (int)p.x + 3 + (int)(Hash("rain_splash", frame) % 10);
        int y = (int)p.y + 3;
        Put(x - 2, y - 1, Gray); Put(x + 2, y - 1, Gray);
        Put(x - 3, y + 1, Gray); Put(x + 3, y + 1, Gray);
    }

    static int Jitter(uint seed, int segment)
    {
        uint h = Hash(seed, segment) % 5;
        return h == 3 ? 1 : h == 4 ? -1 : 0;
    }

    bool WallGap(int x, int y)
    {
        foreach (var door in room.doors)
            if (door != null && Near(door.tile, x, y)) return true;
        foreach (var thing in room.things)
        {
            if (thing == null || thing.hidden) continue;
            for (int i = 0; i < thing.Width; i++)
            {
                var t = new Vector2Int(thing.tile.x + i, thing.tile.y);
                if (!room.OnFloor(t) && Near(t, x, y)) return true;
            }
        }
        return false;
    }

    static bool Near(Vector2Int tile, int x, int y) =>
        Mathf.Abs(x - (tile.x * T + 8)) < 9 && Mathf.Abs(y - (tile.y * T + 8)) < 9;

    // A slab, a little uneven per tile: outline 11 x 9 with clipped corners.
    void PaintStone(Vector2Int t)
    {
        uint h = Hash((uint)(t.x * 131 + t.y * 7919), 7);
        int x0 = t.x * T + 2 + (int)(h % 2), y0 = t.y * T + 3 + (int)((h >> 1) % 2);
        int x1 = x0 + 10, y1 = y0 + 8;
        for (int x = x0 + 1; x < x1; x++) { Put(x, y0, Gray); Put(x, y1, Gray); }
        for (int y = y0 + 1; y < y1; y++) { Put(x0, y, Gray); Put(x1, y, Gray); }
    }

    // Two short rows of sprouts, each a tiny V.
    void PaintSprouts(Vector2Int t)
    {
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < 3; i++)
            {
                int x = t.x * T + 2 + i * 4 + row * 2, y = t.y * T + 4 + row * 7;
                Put(x, y, Gray); Put(x + 2, y, Gray); Put(x + 1, y + 1, Gray);
            }
    }

    void PaintWater(Vector2Int t)
    {
        int frame = Mathf.FloorToInt(now / .2f);
        int ox = t.x * T, oy = t.y * T;
        for (int ri = 0; ri < WaterRows.Length; ri++)
        {
            int shift = (frame + ri * 3 + t.x * 7) % 16;
            for (int b = 0; b < 16; b += 8)
            {
                Put(ox + (b + shift) % 16, oy + WaterRows[ri] + 1, Gray);
                Put(ox + (b + shift + 1) % 16, oy + WaterRows[ri], Gray);
                Put(ox + (b + shift + 2) % 16, oy + WaterRows[ri], Gray);
                Put(ox + (b + shift + 3) % 16, oy + WaterRows[ri] + 1, Gray);
            }
        }
    }

    void PaintRipples()
    {
        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            float age = now - ripples[i].start;
            if (age >= rippleSeconds) { ripples.RemoveAt(i); continue; }
            float frames = age / .1f;
            int strength = 16 - Mathf.FloorToInt(frames / 2f);
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = frames * 1.2f - ring * 6f;
                if (radius <= 1f) continue;
                int points = Mathf.FloorToInt(radius * 6f);
                for (int p = 0; p < points; p += 2)
                {
                    float a = Mathf.PI * 2f * p / points;
                    int x = Mathf.RoundToInt(ripples[i].centre.x + radius * Mathf.Cos(a));
                    int y = Mathf.RoundToInt(ripples[i].centre.y + radius * Mathf.Sin(a) * .6f);
                    if (IsWater(new Vector2Int(Mathf.FloorToInt(x / (float)T), Mathf.FloorToInt(y / (float)T))))
                        Put(x, y, Gray, strength);
                }
            }
        }
    }

    // pancake 10-27: a dragged smear, not drops. One stroke from the bench edge through the trail tiles
    // to the foot of the bed, rounded at the turns; wide by the bench, thinning and breaking up toward
    // the bed. Placed by position so it never boils, and like the floor it shows only where light falls.
    List<Vector2> bloodPath;
    OSHomeRoom bloodPathRoom;

    void PaintBloodTrail()
    {
        if (room.blood.Count == 0) return;
        if (bloodPathRoom != room || bloodPath == null) { bloodPath = BloodPath(room.blood); bloodPathRoom = room; }
        float total = 0f;
        for (int i = 1; i < bloodPath.Count; i++) total += Vector2.Distance(bloodPath[i - 1], bloodPath[i]);
        if (total <= 0f) return;
        float walked = 0f;
        for (int i = 1; i < bloodPath.Count; i++)
        {
            Vector2 a = bloodPath[i - 1], b = bloodPath[i];
            float length = Vector2.Distance(a, b);
            if (length <= 0f) continue;
            Vector2 along = (b - a) / length, across = new Vector2(-along.y, along.x);
            for (float d = 0f; d < length; d += .5f)
            {
                float s = walked + d, u = s / total;
                int step = Mathf.FloorToInt(s / 2f);
                uint h = Hash("smear", step);
                if (u > .62f && h % 5 == 0) continue; // dried and broken near the bed
                int width = u < .3f ? 3 : u < .7f ? 2 : 1;
                float wobble = Mathf.Sin(s * .31f) * .8f + Mathf.Sin(s * .113f + 1.7f) * .6f;
                Vector2 p = a + along * d;
                for (int k = 0; k < width; k++)
                {
                    Vector2 q = p + across * (wobble + k - (width - 1) * .5f);
                    Put(Mathf.RoundToInt(q.x - .5f), Mathf.RoundToInt(q.y - .5f), Red);
                }
                // A frayed edge here and there where the smear was dragged.
                if (u < .7f && (h >> 3) % 7 == 0)
                {
                    Vector2 q = p + across * (wobble + ((h & 1u) == 0 ? width * .5f + 1f : -width * .5f - 1f));
                    Put(Mathf.RoundToInt(q.x - .5f), Mathf.RoundToInt(q.y - .5f), Red);
                }
            }
            walked += length;
        }
    }

    // Tile centres, starting at the left edge of the first tile (against the bench) and ending at the top
    // edge of the last (the foot of the bed), cut twice at the corners so the turns are curves.
    static List<Vector2> BloodPath(List<Vector2Int> tiles)
    {
        var points = new List<Vector2> { new Vector2(tiles[0].x * T + 1f, tiles[0].y * T + 9f) };
        foreach (var t in tiles) points.Add(new Vector2(t.x * T + 8f, t.y * T + 9f));
        var last = tiles[tiles.Count - 1];
        points.Add(new Vector2(last.x * T + 8f, last.y * T + 1f));
        for (int pass = 0; pass < 2; pass++)
        {
            var cut = new List<Vector2> { points[0] };
            for (int i = 0; i < points.Count - 1; i++)
            {
                cut.Add(Vector2.Lerp(points[i], points[i + 1], .25f));
                cut.Add(Vector2.Lerp(points[i], points[i + 1], .75f));
            }
            cut.Add(points[points.Count - 1]);
            points = cut;
        }
        return points;
    }

    void PaintFootprints()
    {
        for (int i = 0; i < room.footprints.Count; i++)
        {
            var t = room.footprints[i];
            if (IsWater(t) || ThingAt(t) != null) continue;
            int x = t.x * T + (i % 2 == 1 ? 4 : 9), y = t.y * T + (i % 2 == 1 ? 5 : 9);
            Put(x, y, Gray); Put(x + 1, y, Gray); Put(x, y + 1, Gray); Put(x + 1, y + 1, Gray);
            Put(x, y + 3, Gray); Put(x + 1, y + 3, Gray);
        }
    }

    void PaintEmpty(Vector2Int t, byte color)
    {
        int x0 = t.x * T + 1, y0 = t.y * T + 1;
        for (int i = 0; i < 14; i += 2)
        {
            uint h = Hash((uint)(t.x * 31 + t.y * 977 + i), boilFrame);
            Put(x0 + i, y0 + (int)(h & 1), color);
            Put(x0 + i, y0 + 13 - (int)((h >> 1) & 1), color);
            Put(x0 + (int)((h >> 2) & 1), y0 + i, color);
            Put(x0 + 13 - (int)((h >> 3) & 1), y0 + i, color);
        }
    }

    Vector2 MePixel()
    {
        float progress = stepClock >= 0f ? Mathf.Clamp01(stepClock / secondsPerTile) : 1f;
        Vector2 a = new Vector2(meFrom.x * T, meFrom.y * T), b = new Vector2(me.x * T, me.y * T);
        Vector2 p = Vector2.Lerp(a, b, progress);
        return new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
    }

    void PaintMe()
    {
        Vector2 p = MePixel();
        int ox = (int)p.x, oy = (int)p.y;
        var word = glyphs.Layout(Localize(meGlyphKey, "我"), 1);
        var under = new Vector2Int(Mathf.FloorToInt((p.x + 8f) / T), Mathf.FloorToInt((p.y + 8f) / T));
        if (!IsWater(under))
        {
            PaintWord(word, ox, oy, Gray, Hash("me"), null, 0f);
            return;
        }
        // In the water: a light tile with a dark 我.
        uint boil = Hash(Hash("me"), boilFrame);
        for (int y = 1; y < T - 1; y++)
            for (int x = 1; x < T - 1; x++) Put(ox + x, oy + y, Gray);
        for (int y = 0; y < T; y++)
        {
            int shift = BoilShift(boil, y);
            for (int x = 0; x < T; x++)
            {
                int X = ox + x + shift, Y = oy + y;
                if (word.Get(x, y) && X >= 0 && Y >= 0 && X < W && Y < H) ink[Y * W + X] = Black;
            }
        }
    }

    void PaintDong()
    {
        if (dongMode == DongMode.Hidden) return;
        Vector2 a = new Vector2(dongFrom.x * T, dongFrom.y * T), b = new Vector2(dong.x * T, dong.y * T);
        float progress = stepClock >= 0f ? Mathf.Clamp01(stepClock / secondsPerTile) : 1f;
        if (absorbStart >= 0f)
        {
            b = MePixel();
            progress = Mathf.Clamp01((now - absorbStart) / absorbSeconds);
        }
        Vector2 p = Vector2.Lerp(a, b, progress);
        int bump = now < dongBumpUntil ? -1 : 0;
        var word = glyphs.Layout(Localize(dongGlyphKey, "咚"), 1);
        PaintWord(word, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y) + bump, Red, Hash("dong"), null, 0f,
            true, Mathf.Clamp(Mathf.RoundToInt(lightScale * 16f), 0, 16));
    }

    void PaintHoverBox()
    {
        bool click = now < clickUntil;
        Vector2Int tile = click ? clickTile : hoverTile;
        if (!InGrid(tile) || !Hoverable(tile)) return;
        int x0 = tile.x * T, y0 = tile.y * T;
        byte color = click ? ClickInk : Hover;
        for (int i = 0; i < T; i++)
        {
            if (!click && (i & 1) == 1) continue;
            Raw(x0 + i, y0, color); Raw(x0 + i, y0 + T - 1, color);
            Raw(x0, y0 + i, color); Raw(x0 + T - 1, y0 + i, color);
        }
    }

    /// <summary>A doorway with 「我」 or 「咚」 standing in it shows them, not the 门.</summary>
    bool Occupied(Vector2Int t)
    {
        bool moving = stepClock >= 0f && stepClock < secondsPerTile * .5f;
        if (playerVisible && (t == me || (moving && t == meFrom))) return true;
        if (dongMode != DongMode.Follow) return false;
        return t == dong || (moving && t == dongFrom);
    }

    bool Hoverable(Vector2Int t) => Walkable(t) || DoorAt(t) != null || ThingAt(t)?.interactive == true;

    void Raw(int x, int y, byte color)
    {
        if (x >= 0 && y >= 0 && x < W && y < H) ink[y * W + x] = color;
    }

    void Upload()
    {
        int blackout = Mathf.RoundToInt(presentationBlackout * 16f);
        for (int y = 0; y < H; y++)
        {
            int src = y * W, dst = (H - 1 - y) * W;
            for (int x = 0; x < W; x++)
            {
                bool dark = Bayer[(((H - 1 - y) + bayerY) & 3) * 4 + ((x + bayerX) & 3)] < blackout;
                pixels[dst + x] = palette[dark ? Black : ink[src + x]];
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
    }

    // ================================================================ hover label

    void UpdateHover()
    {
        if (!pointerInside || !AcceptsInput() || !ToTile(pointerScreen, pointerCamera, out Vector2Int tile))
        {
            if (!pointerInside) hoverTile = new Vector2Int(-1, -1);
            HideTooltip();
            return;
        }
        hoverTile = tile;
        string text = null, owner = null;
        var door = DoorAt(tile);
        var thing = door == null ? ThingAt(tile) : null;
        if (door != null && !string.IsNullOrEmpty(door.label) && (!labelsNeedLight || TileLit(door.tile)))
        { owner = door.id; text = Localize(door.labelKey, door.label); }
        else if (thing != null && thing.interactive && (!labelsNeedLight || ThingLit(thing, tile)))
        {
            owner = thing.id;
            text = paperMarks.TryGetValue(thing.id, out var paper) && !string.IsNullOrEmpty(paper.label)
                ? paper.label : Localize(thing.labelKey, thing.label);
        }
        if (string.IsNullOrEmpty(text)) { HideTooltip(); return; }
        ShowTooltip(owner, text, thing != null ? thing.tile : door.tile, thing != null ? thing.Width : 1);
    }

    void ShowTooltip(string owner, string text, Vector2Int tile, int width)
    {
        if (!EnsureTooltip()) return;
        if (tooltipFor != owner || tooltipLabel.text != text)
        {
            tooltipFor = owner;
            OSLocalizedFontUtility.ApplyCurrent(tooltipLabel);
            tooltipLabel.text = text;
            Vector2 size = tooltipLabel.GetPreferredValues(text);
            tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Ceil(size.x) + 8f);
            tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(13f, Mathf.Ceil(size.y) + 3f));
        }
        var rect = ((RectTransform)transform).rect;
        float sx = rect.width / W, sy = rect.height / H;
        float cx = rect.xMin + (tile.x + width * .5f) * T * sx;
        float top = rect.yMax - tile.y * T * sy + 2f;
        float half = tooltip.rect.width * .5f;
        cx = Mathf.Clamp(cx, rect.xMin + half, rect.xMax - half);
        if (top + tooltip.rect.height > rect.yMax) top = rect.yMax - (tile.y + 1) * T * sy - tooltip.rect.height - 2f;
        tooltip.anchoredPosition = new Vector2(Mathf.Round(cx), Mathf.Round(top));
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
        if (glyphs.Font == null) return false;
        var frame = new GameObject("HoverLabel", typeof(RectTransform), typeof(Image));
        tooltip = (RectTransform)frame.transform;
        tooltip.SetParent(transform, false);
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(.5f, .5f);
        tooltip.pivot = new Vector2(.5f, 0f);
        var border = frame.GetComponent<Image>();
        border.color = gray; border.raycastTarget = false;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        var fillRect = (RectTransform)fill.transform;
        fillRect.SetParent(tooltip, false);
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.one; fillRect.offsetMax = -Vector2.one;
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = Color.black; fillImage.raycastTarget = false;
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = (RectTransform)label.transform;
        labelRect.SetParent(tooltip, false);
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        tooltipLabel = label.GetComponent<TextMeshProUGUI>();
        tooltipLabel.font = glyphs.Font;
        tooltipLabel.fontSize = tooltipFontSize;
        tooltipLabel.color = gray;
        tooltipLabel.alignment = TextAlignmentOptions.Center;
        tooltipLabel.textWrappingMode = TextWrappingModes.NoWrap;
        tooltipLabel.raycastTarget = false;
        frame.SetActive(false);
        return true;
    }

    // ================================================================ setup

    void EnsureTexture()
    {
        if (image == null) image = GetComponent<RawImage>();
        if (texture != null) return;
        texture = new Texture2D(W, H, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Chapter03Home" };
        pixels = new Color32[W * H];
        ink = new byte[W * H];
        lit = new byte[W * H];
        image.texture = texture;
        image.color = Color.white;
        image.uvRect = new Rect(0f, 0f, 1f, 1f);
        image.raycastTarget = true;
        window = GetComponentInParent<OSWindowUnit>();
    }

    void EnsureGlyphFont()
    {
        TMP_FontAsset font = glyphFont;
        if (font == null)
        {
            var memory = OS_Handle.Instance != null ? OS_Handle.Instance.MemoryWindowController : null;
            var text = memory != null ? memory.GetComponentInChildren<TMP_Text>(true) : null;
            font = text != null ? text.font : TMP_Settings.defaultFontAsset;
        }
        var data = GameDataManager.Instance;
        if (data != null && data.CurrentLanguageState == LanguageState.ko && data.LocalizationData != null
            && data.LocalizationData.KoreanFont != null)
            font = data.LocalizationData.KoreanFont;
        if (glyphs.Font != font || !Mathf.Approximately(glyphs.EmPixels, glyphEm) || !Mathf.Approximately(glyphs.Coverage, glyphCoverage))
        {
            glyphs.Clear();
            glyphs.Font = font; glyphs.EmPixels = glyphEm; glyphs.Coverage = glyphCoverage;
            retexts.Clear();
        }
    }

    void RegisterNight(OS_Handle os)
    {
        if (!registerNightGlow || nightfall != null || os == null) return;
        if (window == null) window = GetComponentInParent<OSWindowUnit>();
        var talk = os.Chapter02FinalTalk;
        nightfall = talk != null ? talk.Nightfall : os.GetComponent<OSChapter02NightfallPresentation>();
        if (nightfall != null && window != null) nightfall.AddGlowWindow(window);
        else nightfall = null;
    }

    void WatchHeartbeat(OS_Handle os)
    {
        var heart = os != null ? os.HeartbeatWaveform : null;
        if (heart == null || !heart.isActiveAndEnabled) return;
        float age = heart.TimeSinceLastBeatStart;
        if (age < lastBeatAge) BeatDong();
        lastBeatAge = age;
    }

    void UpdateBayerPhase()
    {
        // Line the room's dither up with the OS night overlay (anchored to the root canvas).
        var canvas = GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        if (root == null || root.rect.height <= 0f) return;
        var rect = (RectTransform)transform;
        Vector2 min = root.InverseTransformPoint(rect.TransformPoint(rect.rect.min));
        float unit = LogicalHeight / root.rect.height;
        bayerX = Mathf.RoundToInt((min.x - root.rect.xMin) * unit) & 3;
        bayerY = Mathf.RoundToInt((min.y - root.rect.yMin) * unit) & 3;
    }

    void RefreshPalette()
    {
        if (palette == null || palette.Length != 8) palette = new Color32[8];
        palette[Black] = new Color32(0, 0, 0, 255);
        palette[Gray] = gray;
        palette[Red] = OSPalette.Red;
        palette[Yellow] = yellow;
        palette[Blue] = blue;
        palette[Hover] = hoverInk;
        palette[ClickInk] = clickInk;
        palette[VisitedInk] = new Color32((byte)(gray.r * .52f), (byte)(gray.g * .52f), (byte)(gray.b * .52f), 255);
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

    string ThingText(OSHomeThing thing) => Localize(thing.glyphKey, thing.glyph);

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
