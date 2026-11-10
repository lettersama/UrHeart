using System;
using UnityEngine;

/// <summary>
/// The chapter-4 act-1 paper theatre, without any Unity objects: a 320 x 180
/// stage of 1-bit ink, stepped in whole frames of 1/30 s (motion moves in
/// steps, never interpolated) and drawn into a Color32 buffer.
///   CityWalk   - empty stage, city pieces stand up back to front, then five
///                depth layers scroll while the puppet hops in place (loop).
///   CityArrive - the house comes in on the road layer and everything slows to
///                a stop with its door right in front of her (centre x 130).
///   DoorLocked - the door rattles.  FoldToLab - the city folds flat front to
///                back, the room stands up back to front.  LabRummage - loop.
/// The puppet never moves sideways: x 96 from the first frame to the last.
/// Stage coordinates: origin top-left, y down. The TV view owns the clock,
/// pause and bridge; this class only reports the automatic boundaries through
/// Boundary and stops when it answers false.
/// </summary>
public sealed partial class OSChapter04TheaterStage
{
    public const int Width = 320, Height = 180;
    public const float FrameSeconds = 1f / 30f;

    public enum Phase { Idle, Rise, Walk, Arrive, AtDoor, EnterDoor, DoorLocked, Fold, LabRise, Rummage, TidyFold, TidyRise, Tidy, Medicine, SceneOut, SceneIn, Scene }

    /// <summary>
    /// Locked: CityArrive runs on into the rattling door (DoorLocked), as the current bridge expects.
    /// Enter: CityArrive stops at the door (AtDoor loop) until GotoEnterDoor(); she walks in, the door
    /// closes, the city folds and she stands up again with the room (script v1, line 9).
    /// </summary>
    public enum DoorMode { Locked, Enter }

    // ---- authored numbers (30 fps frames, stage pixels)
    const int GroundY = 172, ScrollMargin = 60;
    const int PuppetFootRow = 55, PuppetCenterCol = 34, PuppetFootY = 171;
    // Every puppet sprite keeps 14 rows (the rod) under its feet; taller art
    // (puppet_sit_cup, 70x84) grows upward, so anchor on the bottom edge.
    const int PuppetBelowFeet = 14;
    /// <summary>Sprite left of the puppet for the whole act; the room is laid out around it.</summary>
    public const int PuppetLeft = 96;
    const int PuppetFigureHeight = PuppetFootRow + 1;
    const int StepFrames = 16;
    const float SquashPart = .35f, SquashAmount = .08f;
    const int HopHeight = 2;
    const int HouseEnterX = 322, HouseClearLeft = 20, HouseClearRight = 10;
    const int DoorLeft = 18, DoorWidth = 30, DoorHoleHeight = 62, DoorBottom = 170;
    /// <summary>The door stops in front of her: body column 96 + 34.</summary>
    public const int DoorStopCenter = PuppetLeft + PuppetCenterCol;
    const int DecelFrames = 36;
    const int DoorHoldFrames = 24, FlatHoldFrames = 8;
    // AtDoor (Enter mode): puppet_carry waits; sink 1 px for 6 of every 60
    // frames, and once every 120 frames a 1-frame look up (sy 1.02).
    const int AtDoorSinkPeriod = 60, AtDoorSinkFrames = 6, AtDoorLookPeriod = 120;
    const float AtDoorLookScale = 1.02f;
    // EnterDoor: the door swings open (narrows about its left edge, 6 frames),
    // she hops in twice - behind the frame once airborne, each landing 2 px
    // higher and 6 % smaller - and is gone; the door closes (6), hold 8, fold.
    const int DoorSwingFrames = 6, EnterSteps = 2, EnterHoldFrames = 8, EnterRisePerStep = 2;
    const float EnterShrinkPerStep = .06f;
    // LabRummage: sink 1 px every other 12 frames; lean right 1 px for the last 6 of every 90.
    const int RummageSinkFrames = 12, RummageLeanPeriod = 90, RummageLeanFrames = 6;
    // LabTidy (preview only): after the room changes she hops from x 96 to the
    // bench, then loops work (right) -> hop left, up onto the stool -> sit with
    // her coffee -> hop down -> hop right. Hops use the CityWalk step (16
    // frames) at about 1.1 px a frame, rounded to a whole number of steps so
    // the last step lands exactly on the spot.
    // Rest x 32 puts her body column (34) over the stool's centre (66).
    public const int TidyWorkX = 166, TidyRestX = 32;
    const int TidyWorkFrames = 180, TidyRestFrames = 150;
    const float TidyHopSpeed = 1.1f;
    // Work idle: rock about the feet 0,1,2,1,0,-1,-2,-1 degrees (8 frames), three
    // times, then hold 40 frames.
    static readonly int[] TidyRock = { 0, 1, 2, 1, 0, -1, -2, -1 };
    const int TidyRockTimes = 3, TidyRockHold = 40;
    // The walk left ends with a hop up onto the stool (peak 10 px); late in the
    // fall the art becomes puppet_sit_cup (sitting height is drawn into it, feet
    // row 55 still on the ground line), then a 2-frame landing squash. Sitting:
    // lift 1 px for 8 frames every 60 (a sip). Leaving: puppet_idle, one hop
    // down on the spot (peak 6 px) with the same squash, then the walk right.
    const int TidySitUpPeak = 10, TidySitDownPeak = 6, TidyLandFrames = 2;
    const float TidyLandSquash = .06f, TidySitSwapAt = .75f;
    const int TidySipPeriod = 60, TidySipFrames = 8;
    public const string PuppetIdle = "puppet_idle", PuppetSitCup = "puppet_sit_cup", PuppetRest = "puppet_rest";
    const int LayerStagger = 4;
    /// <summary>Empty stage before the city stands up on a fresh CityWalk.</summary>
    public static int EmptyHoldFrames = 15;

    static readonly int[] FoldAngles = { 0, 0, 15, 15, 30, 30, 45, 45, 60, 60, 72, 72, 82, 82, 90 };
    static readonly float[] Wobble = { 1.04f, 1.04f, .97f, .97f, 1f };
    static readonly int[] DoorShake = { 0, 0, 1, 1, 0, 0, -1, -1, 0, 0, 1, 1, 0, 0, 0 };
    static readonly Color32 BlackColor = new Color32(0, 0, 0, 255);
    static readonly Color32 GrayColor = new Color32(0x87, 0x82, 0x7E, 255);

    struct Piece
    {
        public string id; public int x; public bool flip; public int bottom;
        public Piece(string id, int x, bool flip, int bottom = 0) { this.id = id; this.x = x; this.flip = flip; this.bottom = bottom; }
    }

    sealed class Layer
    {
        public int bottom, level, period, delay;
        public float speed;
        public Piece[] pieces;
    }

    struct Pose
    {
        public int angle; public float scale;
        public static readonly Pose Upright = new Pose { angle = 0, scale = 1f };
        public static readonly Pose Flat = new Pose { angle = 90, scale = 1f };
        public bool Visible => angle < 90;
        public int Height(int h) => Mathf.RoundToInt(h * Mathf.Cos(angle * Mathf.Deg2Rad) * scale);
        public int Level(int layerLevel) => Math.Min(layerLevel, angle >= 82 ? 2 : angle >= 72 ? 4
            : angle >= 60 ? 6 : angle >= 45 ? 9 : angle >= 30 ? 12 : OSChapter04TheaterPiece.Undimmed);
    }

    // City: far, mid-far, mid, road (puppet layer), front. Delay = fold delay.
    static readonly Layer[] City =
    {
        new Layer { bottom = 118, speed = .10f, level = 3, period = 420, delay = 16, pieces = new[] {
            new Piece("building_small", 0, false), new Piece("building_broken", 30, true), new Piece("building_small", 74, true),
            new Piece("building_broken", 120, false), new Piece("building_broken", 170, true), new Piece("building_small", 214, false),
            new Piece("building_broken", 252, false), new Piece("building_small", 300, true), new Piece("building_broken", 338, true),
            new Piece("building_small", 380, false) } },
        new Layer { bottom = 134, speed = .22f, level = 6, period = 460, delay = 12, pieces = new[] {
            new Piece("building_broken", 10, false), new Piece("building_small", 58, false), new Piece("building_broken", 140, true),
            new Piece("building_small", 190, true), new Piece("building_broken", 262, false), new Piece("building_small", 320, false),
            new Piece("building_broken", 388, true) } },
        new Layer { bottom = 151, speed = .42f, level = 11, period = 520, delay = 8, pieces = new[] {
            new Piece("building_small", 20, true), new Piece("building_broken", 96, false), new Piece("building_broken", 214, true),
            new Piece("building_small", 282, false), new Piece("building_broken", 388, false) } },
        new Layer { bottom = 171, speed = .80f, level = OSChapter04TheaterPiece.Undimmed, period = 560, delay = 4, pieces = new[] {
            new Piece("lamp_post", 60, false), new Piece("fence", 200, false), new Piece("lamp_post", 330, true),
            new Piece("fence", 440, true) } },
        new Layer { bottom = 183, speed = 1.40f, level = OSChapter04TheaterPiece.Undimmed, period = 640, delay = 0, pieces = new[] {
            new Piece("fg_rubble_a", 40, false), new Piece("fg_rubble_b", 210, false), new Piece("fg_rubble_b", 340, true),
            new Piece("fg_rubble_a", 470, true), new Piece("fg_rubble_b", 590, true) } },
    };
    const int Road = 3, Front = 4;

    // ================================================================ room layout table
    // Fixed positions, so a restore equals a first play. Layers draw in this
    // order: LabBack behind the road slot, LabRoad behind the puppet, LabNear in
    // front of her. Rows draw in table order within a layer.
    public const int LabBack = 0, LabRoad = 1, LabNear = 2;
    const int U = OSChapter04TheaterPiece.Undimmed;

    /// <summary>Which room a row belongs to: always there, the messy lab only, or the tidied lab only.</summary>
    public enum LabSet { Both, Mess, Tidy }

    /// <summary>A room paper piece: art id, layer, left x, bottom y, mirrored, dim level (16 = none), set.</summary>
    public struct LabPiece
    {
        public string id; public int layer, x, bottom; public bool flip; public int level; public LabSet set;
        /// <summary>Optional tidied art for a Both row: when it exists, the row folds and stands up as it in LabTidy.</summary>
        public string tidyId;
        public LabPiece(string id, int layer, int x, int bottom, bool flip, int level, LabSet set = LabSet.Both, string tidyId = null)
        { this.id = id; this.layer = layer; this.x = x; this.bottom = bottom; this.flip = flip; this.level = level; this.set = set; this.tidyId = tidyId; }
    }

    /// <summary>Per room layer: stand-up delay (frames) and its sparse ground row (bottom, dim level).</summary>
    public static readonly (int delay, int groundBottom, int groundLevel)[] LabLayers =
    {
        (0, 151, 11), // back
        (4, 171, U),  // road
        (8, 183, U),  // near
    };

    /// <summary>
    /// The room the city folds into: living corner on the left, lab on the
    /// right, the puppet (x 96) between them reaching for the box. Mess rows
    /// are LabRummage only; Tidy rows are the line-9 tidied lab, kept here as
    /// data and played only by the editor preview (no bridge state exists yet).
    /// </summary>
    public static LabPiece[] LabLayout =
    {
        //            id                 layer     x    bottom  flip   level  set
        new LabPiece("wall_window",      LabBack,  14,  151,  false,  11),
        new LabPiece("sofa_old",         LabBack,  22,  151,  false,  11,   tidyId: "sofa_clean"),
        new LabPiece("coat_rack",        LabBack,  84,  151,  false,  11),
        new LabPiece("cabinet_empty",    LabBack, 200,  151,  false,  11,   LabSet.Mess),
        new LabPiece("cabinet_empty",    LabBack, 256,  151,  true,   11,   LabSet.Mess),
        new LabPiece("shelf_full",       LabBack, 196,  151,  false,  11,   LabSet.Tidy),
        new LabPiece("notes_board",      LabBack, 252,  151,  false,  11,   LabSet.Tidy),

        new LabPiece("floor_lamp",       LabRoad,   2,  171,  false,  U),
        new LabPiece("counter_coffee",   LabRoad,  18,  171,  false,  U,    tidyId: "counter_coffee_clean"),
        new LabPiece("side_table",       LabRoad,  76,  171,  false,  U),
        new LabPiece("stool",            LabRoad,  56,  171,  false,  U,    LabSet.Tidy),
        new LabPiece("lab_bench_dusty",  LabRoad, 196,  171,  false,  U,    LabSet.Mess),
        new LabPiece("chair_toppled",    LabRoad, 282,  171,  false,  U,    LabSet.Mess),
        new LabPiece("desk_lab",         LabRoad, 196,  171,  false,  U,    LabSet.Tidy),
        new LabPiece("sample_fridge",    LabRoad, 286,  171,  false,  U,    LabSet.Tidy),

        new LabPiece("floor_strip",      LabNear, -20,  183,  false,  U),
        new LabPiece("fg_plant",         LabNear,   6,  183,  false,  U,    tidyId: "fg_plant_alive"),
        new LabPiece("fg_books",         LabNear,  62,  183,  false,  U,    LabSet.Mess),
        new LabPiece("fg_cables",        LabNear, 226,  183,  false,  U,    LabSet.Mess),
        new LabPiece("box",              LabNear, 138,  176,  false,  U,    LabSet.Mess),
        new LabPiece("box",              LabNear, 236,  176,  false,  U,    LabSet.Tidy),
    };

    public const string PuppetCarry = "puppet_carry", PuppetRummage = "puppet_rummage", PuppetTidy = "puppet_shake";

    readonly OSChapter04TheaterLibrary library;
    readonly OSChapter04TheaterPiece doorHole;
    readonly byte[] canvas = new byte[Width * Height];

    Phase phase = Phase.Idle;
    int phaseFrame;
    double clock;
    float travel;
    // Arrival: 0 approach, 1 slow down, 2 finish the current step.
    int arriveStage, decelFrame, houseX, houseShift0;
    int stepFrame;
    string puppetArt = PuppetCarry;
    bool halted, tidyRestored;
    // Set when she went in through the door: she is not drawn while the city folds and stands up with the room.
    bool enteredByDoor;

    /// <summary>How CityArrive ends; the TV view sets it from its serialized doorMode.</summary>
    public DoorMode Mode = DoorMode.Locked;
    string lastPuppetArt;
    bool puppetDrawn;

    /// <summary>The last drawn frame showed the puppet (QA: she is never seen jumping between scenes).</summary>
    public bool IsPuppetDrawn => puppetDrawn;

    /// <summary>
    /// Called on entering DoorLocked, FoldToLab and LabRummage, in that order.
    /// Returning false halts the stage on the frame it already shows.
    /// </summary>
    public Func<string, bool> Boundary;

    public OSChapter04TheaterStage(OSChapter04TheaterLibrary library)
    {
        this.library = library;
        doorHole = OSChapter04TheaterPiece.Solid("door_hole", DoorWidth, DoorHoleHeight, OSChapter04TheaterPiece.Black);
    }

    public Phase CurrentPhase => phase;
    public int PhaseFrame => phaseFrame;
    /// <summary>Puppet sprite left this frame, before the 1 px lean / shake offsets. Only the tidy preview moves her.</summary>
    public float PuppetX => phase == Phase.Tidy ? CurrentTidy.x : ScenePuppetX ?? PuppetLeft;
    public string PuppetArt => puppetArt;
    /// <summary>Stage row of a puppet sprite's top edge when it stands still: bottom-anchored, so any height lines up.</summary>
    public static int PuppetSpriteTop(int artHeight) => PuppetFootY + 1 + PuppetBelowFeet - artHeight;
    /// <summary>The puppet art id the last drawn frame actually used (after fallbacks).</summary>
    public string LastPuppetArt => lastPuppetArt;
    public int HouseX => houseX;
    public int DoorCenter => houseX + DoorLeft + DoorWidth / 2;
    /// <summary>The house and every layer have come to rest (CityArrive's last part).</summary>
    public bool IsArrivalStopped => phase == Phase.Arrive && arriveStage >= 2;
    /// <summary>A boundary report was refused; the frame shown stays until the next Begin/Clear.</summary>
    public bool IsHalted => halted;

    /// <summary>The bridge state this frame belongs to; empty when idle or in the preview-only tidy scene.</summary>
    public string StateId => phase == Phase.Rise || phase == Phase.Walk ? OSChapter04PresentationBridge.CityWalk
        : phase == Phase.Arrive ? OSChapter04PresentationBridge.CityArrive
        : phase == Phase.AtDoor ? OSChapter04PresentationBridge.AtDoor
        : phase == Phase.EnterDoor ? OSChapter04PresentationBridge.EnterDoor
        : phase == Phase.DoorLocked ? OSChapter04PresentationBridge.DoorLocked
        : phase == Phase.Fold || phase == Phase.LabRise ? OSChapter04PresentationBridge.FoldToLab
        : phase == Phase.Rummage ? OSChapter04PresentationBridge.LabRummage
        : phase == Phase.Tidy || phase == Phase.TidyFold || phase == Phase.TidyRise ? OSChapter04PresentationBridge.LabTidy
        : SceneState ?? string.Empty;

    /// <summary>Back to the empty stage (ground line only).</summary>
    public void Clear()
    {
        Enter(Phase.Idle);
        enteredByDoor = false;
        queuedState = null;
        travel = 0f; stepFrame = 0; puppetArt = PuppetCarry; clock = 0d;
        ResetGraveSection();
    }

    /// <summary>
    /// Start a bridge request. Restoring builds the loop directly; a fresh
    /// CityWalk stands the city up first; a fresh CityArrive keeps the current
    /// walk going (finishing a rise at once) and plays the whole chain.
    /// </summary>
    public void Begin(string stateId, bool restoring)
    {
        // Scenes with their own picture are loops: they restore by name (the bridge may not know the newest ones yet).
        string target = restoring && !HasSceneArtwork(stateId) ? OSChapter04PresentationBridge.ResolveRestoredState(stateId) : stateId;
        if (TryBeginScene(target, restoring)) return;
        clock = 0d;
        enteredByDoor = false;
        queuedState = null;
        if (restoring)
        {
            string resolved = target;
            if (resolved == OSChapter04PresentationBridge.CityWalk) BeginWalk();
            else if (resolved == OSChapter04PresentationBridge.AtDoor) BeginAtDoor();
            else if (resolved == OSChapter04PresentationBridge.LabTidy || OSChapter04PresentationBridge.IsLaterScene(resolved)) BeginTidyPreview(true);
            else if (resolved == OSChapter04PresentationBridge.LabRummage) BeginRummage();
            else Clear();
            return;
        }
        switch (stateId)
        {
            case OSChapter04PresentationBridge.AtDoor:
                BeginAtDoor();
                break;
            case OSChapter04PresentationBridge.EnterDoor:
                if (phase != Phase.AtDoor) BeginAtDoor();
                GotoEnterDoor();
                break;
            case OSChapter04PresentationBridge.LabTidy:
                BeginTidyPreview(false);
                break;
            case OSChapter04PresentationBridge.CityWalk:
                travel = 0f; stepFrame = 0; puppetArt = PuppetCarry;
                Enter(Phase.Rise);
                break;
            case OSChapter04PresentationBridge.CityArrive:
                if (phase != Phase.Walk && phase != Phase.Rise && phase != Phase.Arrive) travel = 0f;
                if (phase != Phase.Walk) stepFrame = 0;
                puppetArt = PuppetCarry;
                houseShift0 = Mathf.FloorToInt(City[Road].speed * travel);
                houseX = HouseEnterX; arriveStage = 0; decelFrame = 0;
                Enter(Phase.Arrive);
                break;
            case OSChapter04PresentationBridge.DoorLocked:
            case OSChapter04PresentationBridge.FoldToLab:
                // Not issued by the current bridge; resume the chain at the door.
                travel = 0f; stepFrame = 0; puppetArt = PuppetCarry;
                houseX = DoorStopCenter - DoorLeft - DoorWidth / 2;
                Enter(stateId == OSChapter04PresentationBridge.DoorLocked ? Phase.DoorLocked : Phase.Fold);
                break;
            case OSChapter04PresentationBridge.LabRummage:
                BeginRummage();
                break;
            default:
                Clear();
                break;
        }
    }

    /// <summary>
    /// Enter mode, restored: the stopped street with the house's door in front
    /// of her, straight into the AtDoor loop. (The bridge cannot request this
    /// yet; a saved CityArrive still restores to LabRummage until the backend
    /// adds a state for it.)
    /// </summary>
    public void BeginAtDoor()
    {
        clock = 0d;
        enteredByDoor = false;
        travel = 0f; stepFrame = 0; puppetArt = PuppetCarry;
        houseX = DoorStopCenter - DoorLeft - DoorWidth / 2;
        Enter(Phase.AtDoor);
    }

    /// <summary>
    /// Enter mode, line 9: from AtDoor she walks in through the door and the
    /// chain continues EnterDoor -> FoldToLab -> LabRummage. Entering reports
    /// DoorLocked, the state the current bridge expects after CityArrive.
    /// Returns false when she is not waiting at the door.
    /// </summary>
    public bool GotoEnterDoor()
    {
        if (phase != Phase.AtDoor || halted) return false;
        return Cross(Phase.EnterDoor, OSChapter04PresentationBridge.EnterDoor);
    }

    /// <summary>
    /// Editor preview of the line-9 tidied lab (no bridge state yet). Fresh: from
    /// the LabRummage room the Mess rows (and the left corner's old art) fold,
    /// the Tidy rows stand up, she hops to the bench and the loop starts.
    /// Restoring: the tidied room at once, the loop from its first work frame.
    /// </summary>
    public void BeginTidyPreview(bool restoring = false)
    {
        clock = 0d;
        travel = 0f; stepFrame = 0;
        tidyRestored = restoring;
        if (restoring) { Enter(Phase.Tidy); return; }
        if (phase != Phase.Rummage) BeginRummage();
        Enter(Phase.TidyFold);
    }

    void BeginWalk()
    {
        travel = 0f; stepFrame = 0; puppetArt = PuppetCarry;
        Enter(Phase.Walk);
    }

    void BeginRummage()
    {
        travel = 0f; stepFrame = 0; puppetArt = PuppetRummage;
        Enter(Phase.Rummage);
    }

    void Enter(Phase next)
    {
        phase = next;
        halted = false;
        phaseFrame = 0;
    }

    bool Cross(Phase next, string stateId)
    {
        if (Boundary != null && !Boundary(stateId))
        {
            halted = true;
            return false;
        }
        Enter(next);
        return true;
    }

    /// <summary>Feed unpaused seconds; returns true when at least one frame stepped.</summary>
    public bool Advance(float seconds)
    {
        // A renderer first bound during a later scene can have work queued on
        // its empty stage. Let the ordinary frame clock consume that request.
        if ((phase == Phase.Idle && !HasQueuedRequest) || halted || seconds <= 0f) return false;
        clock += seconds;
        int steps = 0;
        while (clock >= FrameSeconds && steps < 600)
        {
            clock -= FrameSeconds;
            Step();
            steps++;
            if (halted) { clock = 0d; break; }
        }
        if (steps >= 600) clock = 0d;
        return steps > 0;
    }

    /// <summary>One 1/30 s frame.</summary>
    public void Step()
    {
        switch (phase)
        {
            case Phase.Rise:
                phaseFrame++;
                if (phaseFrame > EmptyHoldFrames + (City.Length - 1) * LayerStagger + RiseFrames - 1) Enter(Phase.Walk);
                break;
            case Phase.Walk:
                phaseFrame++;
                travel += 1f;
                Hop();
                break;
            case Phase.Arrive:
                phaseFrame++;
                StepArrive();
                break;
            case Phase.DoorLocked:
                phaseFrame++;
                if (phaseFrame >= DoorShake.Length + DoorHoldFrames) Cross(Phase.Fold, OSChapter04PresentationBridge.FoldToLab);
                break;
            case Phase.AtDoor:
                phaseFrame++;
                break;
            case Phase.EnterDoor:
                phaseFrame++;
                if (phaseFrame >= EnterDoorFrames)
                {
                    enteredByDoor = true;
                    Cross(Phase.Fold, OSChapter04PresentationBridge.FoldToLab);
                }
                break;
            case Phase.Fold:
                phaseFrame++;
                // Costume change once the city lies flat; her x never changes.
                if (phaseFrame >= FlatFrame) puppetArt = PuppetRummage;
                if (phaseFrame >= FlatFrame + FlatHoldFrames) Enter(Phase.LabRise);
                break;
            case Phase.LabRise:
                phaseFrame++;
                if (phaseFrame > LabLayers[LabNear].delay + RiseFrames - 1)
                    Cross(Phase.Rummage, OSChapter04PresentationBridge.LabRummage);
                break;
            case Phase.TidyFold:
                phaseFrame++;
                // She keeps rummaging while the room changes around her.
                if (phaseFrame >= TidyFlatFrame + FlatHoldFrames) Enter(Phase.TidyRise);
                break;
            case Phase.TidyRise:
                phaseFrame++;
                if (phaseFrame > LabLayers[LabNear].delay + RiseFrames - 1) Enter(Phase.Tidy);
                break;
            case Phase.Rummage:
            case Phase.Tidy:
                phaseFrame++;
                break;
            case Phase.Medicine:
            case Phase.SceneOut:
            case Phase.SceneIn:
            case Phase.Scene:
                StepScene();
                break;
        }
        ApplyQueuedRequest();
    }

    static int RiseFrames => FoldAngles.Length + Wobble.Length;
    /// <summary>EnterDoor timeline: open, hop in, close, hold.</summary>
    static int EnterStepsStart => DoorSwingFrames;
    static int DoorCloseStart => EnterStepsStart + EnterSteps * StepFrames;
    static int EnterDoorFrames => DoorCloseStart + DoorSwingFrames + EnterHoldFrames;
    /// <summary>First Fold frame on which every city layer lies flat.</summary>
    static int FlatFrame => (City.Length - 1) * LayerStagger + FoldAngles.Length - 1;
    /// <summary>Mess rows fold front to back: near 0, road 4, back 8.</summary>
    static int TidyFoldDelay(int layer) => (LabNear - layer) * LayerStagger;
    static int TidyFlatFrame => TidyFoldDelay(LabBack) + FoldAngles.Length - 1;

    void Hop() => stepFrame = (stepFrame + 1) % StepFrames;

    void StepArrive()
    {
        Layer road = City[Road];
        switch (arriveStage)
        {
            case 0:
                travel += 1f;
                Hop();
                houseX = HouseEnterX - (Mathf.FloorToInt(road.speed * travel) - houseShift0);
                // Start slowing so the door comes to rest in front of her.
                float slowDistance = road.speed * (DecelFrames - 1) * .5f;
                if (DoorCenter <= DoorStopCenter + slowDistance) { arriveStage = 1; decelFrame = 0; }
                break;
            case 1:
                decelFrame++;
                travel += Mathf.Max(0f, 1f - decelFrame / (float)DecelFrames);
                Hop();
                houseX = HouseEnterX - (Mathf.FloorToInt(road.speed * travel) - houseShift0);
                if (decelFrame >= DecelFrames) arriveStage = 2;
                break;
            case 2:
                // Everything has stopped; she lands the step she is in, then tries
                // the door (Locked) or waits in front of it for line 9 (Enter).
                if (stepFrame != 0) Hop();
                if (stepFrame != 0) break;
                if (Mode == DoorMode.Enter) Cross(Phase.AtDoor, OSChapter04PresentationBridge.AtDoor);
                else Cross(Phase.DoorLocked, OSChapter04PresentationBridge.DoorLocked);
                break;
        }
    }

    // ================================================================ poses

    static Pose FoldPose(int index)
    {
        if (index < 0) return Pose.Upright;
        if (index >= FoldAngles.Length) return Pose.Flat;
        return new Pose { angle = FoldAngles[index], scale = 1f };
    }

    static Pose RisePose(int index)
    {
        if (index < 0) return Pose.Flat;
        if (index < FoldAngles.Length) return new Pose { angle = FoldAngles[FoldAngles.Length - 1 - index], scale = 1f };
        index -= FoldAngles.Length;
        return index < Wobble.Length ? new Pose { angle = 0, scale = Wobble[index] } : Pose.Upright;
    }

    Pose CityPose(int layer)
    {
        switch (phase)
        {
            case Phase.Rise: return RisePose(phaseFrame - EmptyHoldFrames - layer * LayerStagger);
            case Phase.Walk: case Phase.Arrive: case Phase.DoorLocked:
            case Phase.AtDoor: case Phase.EnterDoor: return Pose.Upright;
            case Phase.Fold: return FoldPose(phaseFrame - City[layer].delay);
            default: return Pose.Flat;
        }
    }

    /// <summary>The pose of a room layer as a whole (its ground dots and Both rows).</summary>
    Pose LabPose(int layer)
    {
        switch (phase)
        {
            case Phase.LabRise: return RisePose(phaseFrame - LabLayers[layer].delay);
            case Phase.Rummage: case Phase.TidyFold: case Phase.TidyRise: case Phase.Tidy: case Phase.Medicine: return Pose.Upright;
            case Phase.SceneOut: return LabFoldingOut ? FoldPose(phaseFrame - TidyFoldDelay(layer)) : Pose.Flat;
            default: return Pose.Flat;
        }
    }

    Pose RowPose(LabPiece row) => RowPose(row.set, row.layer);

    Pose RowPose(LabSet set, int layer)
    {
        LabPiece row = new LabPiece { set = set, layer = layer };
        // MedicineReady keeps the tidied room; a scene change folds whichever room was showing.
        if (phase == Phase.Medicine) return set == LabSet.Mess ? Pose.Flat : Pose.Upright;
        if (LabFoldingOut)
            return set == (labFromSet == LabSet.Tidy ? LabSet.Mess : LabSet.Tidy) ? Pose.Flat
                : FoldPose(phaseFrame - TidyFoldDelay(layer));
        switch (row.set)
        {
            case LabSet.Mess:
                if (phase == Phase.TidyFold) return FoldPose(phaseFrame - TidyFoldDelay(row.layer));
                return phase == Phase.TidyRise || phase == Phase.Tidy ? Pose.Flat : LabPose(row.layer);
            case LabSet.Tidy:
                if (phase == Phase.TidyRise) return RisePose(phaseFrame - LabLayers[row.layer].delay);
                return phase == Phase.Tidy ? Pose.Upright : Pose.Flat;
            default:
                return LabPose(row.layer);
        }
    }

    // ================================================================ drawing

    /// <summary>Draws the current frame; target is Width x Height, rows bottom-up (Texture2D order).</summary>
    public void Render(Color32[] target)
    {
        Draw();
        for (int y = 0; y < Height; y++)
        {
            int row = (Height - 1 - y) * Width, src = y * Width;
            for (int x = 0; x < Width; x++)
                target[row + x] = canvas[src + x] == OSChapter04TheaterPiece.Gray ? GrayColor : BlackColor;
        }
    }

    /// <summary>The current frame as ink, rows top-down (0 black, 2 gray).</summary>
    public byte[] DrawInk() { Draw(); return canvas; }

    void Draw()
    {
        Array.Clear(canvas, 0, canvas.Length);
        puppetDrawn = false;
        if (DrawSceneFrame()) return;
        bool atDoor = phase == Phase.AtDoor || phase == Phase.EnterDoor;
        bool city = phase == Phase.Rise || phase == Phase.Walk || phase == Phase.Arrive
            || phase == Phase.DoorLocked || phase == Phase.Fold || atDoor;
        bool lab = phase == Phase.LabRise || phase == Phase.Rummage
            || phase == Phase.TidyFold || phase == Phase.TidyRise || phase == Phase.Tidy
            || phase == Phase.Medicine || LabFoldingOut;
        bool house = phase == Phase.Arrive || phase == Phase.DoorLocked || phase == Phase.Fold || atDoor;
        // Walking in: once she leaves the ground on the first step she is inside the frame.
        bool insideDoor = phase == Phase.EnterDoor && phaseFrame >= EnterStepsStart + Mathf.CeilToInt(SquashPart * StepFrames);

        if (city) for (int i = 0; i < Road; i++) DrawCityLayer(i, false);
        if (lab) DrawLabLayer(LabBack);
        if (city) DrawCityLayer(Road, house);
        if (lab) DrawLabLayer(LabRoad);
        DrawGround(city, lab);
        if (house)
        {
            Pose pose = CityPose(Road);
            DrawPiece(doorHole, OSChapter04TheaterPiece.Undimmed, houseX + DoorLeft, PuppetFootY, false, pose);
            if (insideDoor) DrawPuppet();
            DrawPiece(library.Get("house"), City[Road].level, houseX, City[Road].bottom, false, pose);
            int shake = phase == Phase.DoorLocked && phaseFrame < DoorShake.Length ? DoorShake[phaseFrame] : 0;
            DrawDoor(pose, shake);
        }
        if (!insideDoor) DrawPuppet();
        if (city) DrawCityLayer(Front, false);
        if (lab) DrawLabLayer(LabNear);
    }

    /// <summary>Door panel; while she walks in it swings open and shut about its left edge (hinge).</summary>
    void DrawDoor(Pose pose, int shake)
    {
        OSChapter04TheaterPiece door = library.Get("door");
        int width = Mathf.RoundToInt(DoorPanelWidth * door.Width / (float)DoorWidth);
        int height = pose.Height(door.Height);
        if (width <= 0 || height <= 0) return;
        Blit(door.Dimmed(pose.Level(City[Road].level)), door.Width, door.Height, 0f, door.Height,
            houseX + DoorLeft + shake, DoorBottom + 1, width / (float)door.Width, height / (float)door.Height, false);
    }

    /// <summary>Door panel width this frame (QA): full when shut, 0 while she walks through.</summary>
    public int DoorPanelWidth
    {
        get
        {
            if (phase != Phase.EnterDoor) return DoorWidth;
            int f = phaseFrame;
            if (f < DoorSwingFrames) return Mathf.RoundToInt(DoorWidth * (1f - (f + 1) / (float)DoorSwingFrames));
            if (f < DoorCloseStart) return 0;
            if (f < DoorCloseStart + DoorSwingFrames) return Mathf.RoundToInt(DoorWidth * (f - DoorCloseStart + 1) / (float)DoorSwingFrames);
            return DoorWidth;
        }
    }

    /// <summary>Whether the puppet is drawn this frame (she is gone after walking in, until the room stands her up).</summary>
    public bool IsPuppetVisible
    {
        get
        {
            if (phase == Phase.Idle) return false;
            if (phase == Phase.EnterDoor) return phaseFrame < DoorCloseStart;
            if (enteredByDoor && phase == Phase.Fold) return false;
            if (enteredByDoor && phase == Phase.LabRise)
                return RisePose(phaseFrame - LabLayers[LabNear].delay).Height(PuppetFigureHeight) > 0;
            return true;
        }
    }

    int Shift(Layer layer) => Mathf.FloorToInt(layer.speed * travel);

    /// <summary>
    /// The ground line (y 172) never folds. The dot rows below it belong to the
    /// layer whose speed they share: they lie down with the road / front city
    /// layers and stand up, unscrolled, with the room's road / near layers - so the
    /// room never shows a dot phase left over from however long the walk ran.
    /// </summary>
    void DrawGround(bool city, bool lab)
    {
        bool roadDots = city ? CityPose(Road).Visible : lab && LabPose(LabRoad).Visible;
        bool frontDots = city ? CityPose(Front).Visible : lab && LabPose(LabNear).Visible;
        int roadShift = city ? Shift(City[Road]) : 0, frontShift = city ? Shift(City[Front]) : 0;
        for (int x = 0; x < Width; x++)
        {
            canvas[GroundY * Width + x] = OSChapter04TheaterPiece.Gray;
            if (roadDots && PosMod(x + roadShift, 2) == 0) canvas[174 * Width + x] = OSChapter04TheaterPiece.Gray;
            if (frontDots && PosMod(x + frontShift, 4) == 0) canvas[177 * Width + x] = OSChapter04TheaterPiece.Gray;
            if (frontDots && PosMod(x + frontShift, 6) == 0) canvas[179 * Width + x] = OSChapter04TheaterPiece.Gray;
        }
    }

    /// <summary>The layer's own ground: one sparse row under its pieces, moving with it.</summary>
    void DrawLayerGround(int bottom, int level, int shift)
    {
        int y = bottom + 1;
        if (y < 0 || y >= Height) return;
        for (int x = 0; x < Width; x++)
        {
            int wx = x + shift;
            if (PosMod(wx, 3) != 0) continue;
            if (OSChapter04TheaterPiece.Bayer[(y & 3) * 4 + PosMod(wx, 4)] < level + 4)
                canvas[y * Width + x] = OSChapter04TheaterPiece.Gray;
        }
    }

    void DrawCityLayer(int index, bool clearForHouse)
    {
        Layer layer = City[index];
        Pose pose = CityPose(index);
        if (!pose.Visible) return;
        int shift = Shift(layer);
        DrawLayerGround(layer.bottom, layer.level, shift);
        foreach (Piece piece in layer.pieces)
        {
            OSChapter04TheaterPiece art = library.Get(piece.id);
            int x = PosMod(piece.x - shift, layer.period) - ScrollMargin;
            if (clearForHouse && x + art.Width > houseX - HouseClearLeft
                && x < houseX + library.Get("house").Width + HouseClearRight) continue;
            DrawPiece(art, layer.level, x, piece.bottom != 0 ? piece.bottom : layer.bottom, piece.flip, pose);
        }
    }

    void DrawLabLayer(int index)
    {
        Pose pose = LabPose(index);
        if (pose.Visible) DrawLayerGround(LabLayers[index].groundBottom, LabLayers[index].groundLevel, 0);
        foreach (LabPiece row in LabLayout)
        {
            if (row.layer != index) continue;
            // A Both row with tidied art present becomes Mess (old art) + Tidy (new art), same layer and x.
            bool swaps = row.set == LabSet.Both && library.Exists(row.tidyId);
            Pose rowPose = swaps ? RowPose(LabSet.Mess, row.layer) : RowPose(row);
            if (rowPose.Visible) DrawPiece(library.Get(row.id), row.level, row.x, row.bottom, row.flip, rowPose);
            if (!swaps) continue;
            Pose tidyPose = RowPose(LabSet.Tidy, row.layer);
            if (tidyPose.Visible) DrawPiece(library.Get(row.tidyId), row.level, row.x, row.bottom, row.flip, tidyPose);
        }
    }

    void DrawPiece(OSChapter04TheaterPiece art, int layerLevel, int left, int bottom, bool flip, Pose pose)
    {
        int height = pose.Height(art.Height);
        if (height <= 0) return;
        // Folds and stands about the piece's own bottom edge; width never changes.
        Blit(art.Dimmed(pose.Level(layerLevel)), art.Width, art.Height, 0f, art.Height,
            left, bottom + 1, 1f, height / (float)art.Height, flip);
    }

    void DrawPuppet()
    {
        if (!IsPuppetVisible) return;
        float sx = 1f, sy = 1f, degrees = 0f;
        int lift = 0, left = PuppetLeft;
        OSChapter04TheaterPiece art = library.Get(puppetArt);
        if (phase == Phase.Rise)
        {
            int height = RisePose(phaseFrame - EmptyHoldFrames - Road * LayerStagger).Height(PuppetFigureHeight);
            if (height <= 0) return;
            sy = height / (float)PuppetFigureHeight;
        }
        else if (phase == Phase.LabRise && enteredByDoor)
        {
            // She went in through the door: she stands up with the near layer, same frames.
            sy = RisePose(phaseFrame - LabLayers[LabNear].delay).Height(PuppetFigureHeight) / (float)PuppetFigureHeight;
        }
        else if (phase == Phase.AtDoor)
        {
            lift = phaseFrame % AtDoorSinkPeriod >= AtDoorSinkPeriod - AtDoorSinkFrames ? 1 : 0;
            if (phaseFrame % AtDoorLookPeriod == AtDoorLookPeriod - 1) sy = AtDoorLookScale;
        }
        else if (phase == Phase.EnterDoor)
        {
            int f = phaseFrame - EnterStepsStart;
            if (f >= 0)
            {
                int step = Math.Min(EnterSteps - 1, f / StepFrames);
                HopArc(f - step * StepFrames, HopHeight, out sx, out sy, out lift);
                // Each landed step takes her 2 px further up and 6 % smaller: walking in.
                float depth = 1f - EnterShrinkPerStep * step;
                sx *= depth; sy *= depth;
                lift -= EnterRisePerStep * step;
            }
        }
        else if (phase == Phase.Rummage)
        {
            lift = (phaseFrame / RummageSinkFrames) % 2;
            left += phaseFrame % RummageLeanPeriod >= RummageLeanPeriod - RummageLeanFrames ? 1 : 0;
        }
        else if (phase == Phase.Tidy)
            TidyPuppet(TidyAt(phaseFrame, tidyRestored), ref art, ref left, ref sx, ref sy, ref lift, ref degrees);
        else if (SceneLabPuppet(ref art, ref left, ref sx, ref sy, ref lift, ref degrees))
        {
            if (sy <= 0f) return;
        }
        else if (phase == Phase.Walk || phase == Phase.Arrive)
            HopPose(stepFrame, out sx, out sy, out lift);
        lastPuppetArt = art.Id;
        puppetDrawn = true;
        // Pivot: centre of the body column, bottom edge of the foot row (14 rows above the sprite bottom).
        Blit(art.Dimmed(OSChapter04TheaterPiece.Undimmed), art.Width, art.Height,
            PuppetCenterCol + .5f, art.Height - PuppetBelowFeet, left + PuppetCenterCol + .5f, PuppetFootY + 1 + lift, sx, sy, false, degrees);
    }

    /// <summary>Puppet art and pose for one moment of the tidy loop (also replayed while MedicineReady lets a step finish).</summary>
    void TidyPuppet(TidyMoment at, ref OSChapter04TheaterPiece art, ref int left, ref float sx, ref float sy, ref int lift, ref float degrees)
    {
        left = at.x;
        switch (at.mode)
        {
            case TidyMode.Work:
                art = library.Get(PuppetTidy);
                int beat = at.frame % (TidyRock.Length * TidyRockTimes + TidyRockHold);
                if (beat < TidyRock.Length * TidyRockTimes) degrees = TidyRock[beat % TidyRock.Length];
                break;
            case TidyMode.SitUp:
                // The walk's last step: a higher hop onto the stool, the seated art just before landing.
                float g = HopArc(at.frame, TidySitUpPeak, out sx, out sy, out lift);
                art = g >= TidySitSwapAt ? SitArt : library.Get(PuppetIdle);
                break;
            case TidyMode.Sit:
                art = SitArt;
                if (at.frame < TidyLandFrames) { sx = 1f + TidyLandSquash; sy = 1f - TidyLandSquash; }
                else lift = (at.frame - TidyLandFrames) % TidySipPeriod >= TidySipPeriod - TidySipFrames ? -1 : 0;
                break;
            case TidyMode.SitDown:
                art = library.Get(PuppetIdle);
                if (at.frame < StepFrames) HopArc(at.frame, TidySitDownPeak, out sx, out sy, out lift);
                else { sx = 1f + TidyLandSquash; sy = 1f - TidyLandSquash; }
                break;
            default:
                // Drawn facing front, so she is never mirrored for walking left.
                art = library.Get(PuppetIdle);
                HopPose(at.frame % StepFrames, out sx, out sy, out lift);
                break;
        }
    }

    /// <summary>Small hop: squash on the ground for the first 35 % of the step, then a 2 px arc.</summary>
    static void HopPose(int step, out float sx, out float sy, out int lift) =>
        HopArc(step, HopHeight, out sx, out sy, out lift);

    /// <summary>
    /// One hop step with a chosen peak: squash on the ground for the first 35 %,
    /// then an arc. Returns the airborne progress g (0 on the ground, then 0..1).
    /// </summary>
    static float HopArc(int step, int peak, out float sx, out float sy, out int lift)
    {
        float u = step / (float)StepFrames;
        sx = sy = 1f; lift = 0;
        if (u < SquashPart)
        {
            float k = Mathf.Sin(Mathf.PI * u / SquashPart);
            sx = 1f + SquashAmount * k;
            sy = 1f - SquashAmount * k;
            return 0f;
        }
        float g = (u - SquashPart) / (1f - SquashPart);
        lift = -Mathf.RoundToInt(4f * g * (1f - g) * peak);
        return g;
    }

    /// <summary>Sitting with the coffee; until puppet_sit_cup is drawn, the standing coffee art.</summary>
    OSChapter04TheaterPiece SitArt => library.Get(PuppetSitCup, PuppetRest);

    // ================================================================ tidy loop (preview only)

    public enum TidyMode { Work, Walk, SitUp, Sit, SitDown }

    public struct TidyMoment
    {
        public TidyMode mode; public int x, frame;
    }

    /// <summary>Whole 16-frame steps nearest to distance at TidyHopSpeed (at least one).</summary>
    static int HopFrames(int distance) =>
        Math.Max(1, Mathf.RoundToInt(Math.Abs(distance) / TidyHopSpeed / StepFrames)) * StepFrames;

    /// <summary>Even progress over the whole steps, so the last frame lands exactly on `to`.</summary>
    static int HopX(int from, int to, int frame) =>
        from + Mathf.RoundToInt((to - from) * Mathf.Min(1f, (frame + 1) / (float)HopFrames(to - from)));

    /// <summary>
    /// Where she is in the tidy loop: a pure function of the Tidy frame. Fresh:
    /// first the hop from x 96 to the bench. Restored: straight into work. Then
    /// work -> hop left (the last step up onto the stool) -> sit -> hop down ->
    /// hop right, forever.
    /// </summary>
    public static TidyMoment TidyAt(int frame, bool restored)
    {
        if (!restored)
        {
            int enter = HopFrames(TidyWorkX - PuppetLeft);
            if (frame < enter) return new TidyMoment { mode = TidyMode.Walk, x = HopX(PuppetLeft, TidyWorkX, frame), frame = frame };
            frame -= enter;
        }
        int hop = HopFrames(TidyWorkX - TidyRestX);
        int sit = TidyLandFrames + TidyRestFrames, down = StepFrames + TidyLandFrames;
        frame %= TidyWorkFrames + hop + sit + down + hop;
        if (frame < TidyWorkFrames) return new TidyMoment { mode = TidyMode.Work, x = TidyWorkX, frame = frame };
        frame -= TidyWorkFrames;
        if (frame < hop)
        {
            int x = HopX(TidyWorkX, TidyRestX, frame);
            return frame < hop - StepFrames
                ? new TidyMoment { mode = TidyMode.Walk, x = x, frame = frame }
                : new TidyMoment { mode = TidyMode.SitUp, x = x, frame = frame - (hop - StepFrames) };
        }
        frame -= hop;
        if (frame < sit) return new TidyMoment { mode = TidyMode.Sit, x = TidyRestX, frame = frame };
        frame -= sit;
        if (frame < down) return new TidyMoment { mode = TidyMode.SitDown, x = TidyRestX, frame = frame };
        frame -= down;
        return new TidyMoment { mode = TidyMode.Walk, x = HopX(TidyRestX, TidyWorkX, frame), frame = frame };
    }

    /// <summary>The tidy-loop moment this frame (preview and QA).</summary>
    public TidyMoment CurrentTidy => TidyAt(phaseFrame, tidyRestored);

    /// <summary>Preview label for the tidy loop: work / walk / rest and the frame within it.</summary>
    public string TidyActivity => phase == Phase.Tidy ? CurrentTidy.mode + " " + CurrentTidy.frame : string.Empty;

    /// <summary>
    /// Nearest-neighbour copy of ink scaled (and optionally rotated, degrees,
    /// positive = clockwise on screen) about a pivot; clear pixels skipped, stage-clipped.
    /// </summary>
    void Blit(byte[] src, int w, int h, float pivotSrcX, float pivotSrcY, float pivotX, float pivotY,
        float sx, float sy, bool flip, float degrees = 0f)
    {
        if (sx <= 0f || sy <= 0f) return;
        float minX = pivotX - pivotSrcX * sx, maxX = pivotX + (w - pivotSrcX) * sx;
        float minY = pivotY - pivotSrcY * sy, maxY = pivotY + (h - pivotSrcY) * sy;
        bool rotated = degrees != 0f;
        float cos = 1f, sin = 0f;
        if (rotated)
        {
            float r = degrees * Mathf.Deg2Rad;
            cos = Mathf.Cos(r); sin = Mathf.Sin(r);
            // A couple of degrees moves the far corners a few pixels at most.
            float grow = Mathf.Max(w * sx, h * sy) * Mathf.Abs(sin) + 1f;
            minX -= grow; maxX += grow; minY -= grow; maxY += grow;
        }
        int x0 = Math.Max(0, Mathf.CeilToInt(minX - .5f)), x1 = Math.Min(Width - 1, Mathf.CeilToInt(maxX - .5f) - 1);
        int y0 = Math.Max(0, Mathf.CeilToInt(minY - .5f)), y1 = Math.Min(Height - 1, Mathf.CeilToInt(maxY - .5f) - 1);
        for (int y = y0; y <= y1; y++)
        {
            float dy = y + .5f - pivotY;
            int dstRow = y * Width;
            for (int x = x0; x <= x1; x++)
            {
                float ox = x + .5f - pivotX, oy = dy;
                if (rotated)
                {
                    // Inverse rotation back into the piece's frame.
                    float rx = ox * cos + oy * sin;
                    oy = -ox * sin + oy * cos;
                    ox = rx;
                }
                int sxi = Mathf.FloorToInt(pivotSrcX + ox / sx), syi = Mathf.FloorToInt(pivotSrcY + oy / sy);
                if (rotated)
                {
                    if (sxi < 0 || sxi >= w || syi < 0 || syi >= h) continue;
                }
                else
                {
                    sxi = Mathf.Clamp(sxi, 0, w - 1);
                    syi = Mathf.Clamp(syi, 0, h - 1);
                }
                if (flip) sxi = w - 1 - sxi;
                byte value = src[syi * w + sxi];
                if (value != OSChapter04TheaterPiece.Clear) canvas[dstRow + x] = value;
            }
        }
    }

    static int PosMod(int value, int period)
    {
        int m = value % period;
        return m < 0 ? m + period : m;
    }
}
