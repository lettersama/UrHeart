using System;
using UnityEngine;

/// <summary>
/// Sections 2-3 of the act (round 6): MedicineReady in the tidied lab, then
/// whole-stage scene changes between WastelandWalk, SettlementWait and
/// TentCare. A change folds the current scene front to back (the act-1 fold
/// table), holds 8 frames, and stands the next one up back to front, 4 frames
/// per layer; the puppet lies down and stands up with the road layer, so she
/// never jumps. Restores build the loop directly. Requests that arrive while a
/// change (or any earlier transition) is still playing wait in queuedState and
/// start as soon as the stage is settled, so the story is never held up.
/// Round 7 adds NorthSettlement, LabNight (the tidied lab under a night
/// overlay that darkens in steps after the room stands and lifts before it
/// folds) and FlowerWalk (the walk with the spire sliding in once). Grave and
/// GraveBreakdown are not drawn here yet.
/// </summary>
public sealed partial class OSChapter04TheaterStage
{
    /// <summary>States this stage draws with their own picture (the rest still fall back to LabTidy).</summary>
    public static bool HasSceneArtwork(string stateId) => stateId == OSChapter04PresentationBridge.MedicineReady
        || stateId == OSChapter04PresentationBridge.WastelandWalk || stateId == OSChapter04PresentationBridge.SettlementWait
        || stateId == OSChapter04PresentationBridge.TentCare || stateId == OSChapter04PresentationBridge.NorthSettlement
        || stateId == OSChapter04PresentationBridge.LabNight || stateId == OSChapter04PresentationBridge.FlowerWalk
        || IsGraveSectionState(stateId);

    // ---- MedicineReady: she finishes what she is doing, hops to x 118 and holds the vial up.
    public const int MedicineX = 118;
    public const string PuppetHoldVial = "puppet_hold_vial";
    const int MedicineLookPeriod = 90, MedicineLookFrames = 10;

    // ---- scene table
    /// <summary>Her idle in a scene loop: hop on the spot, sink 1 px now and then, or a sip (only the cup moves).</summary>
    public enum SceneIdle { Hop, Sink, Sip }
    // Figures (Rock .. Scribble) are puppet-like pieces anchored on their feet;
    // Arrive is a far piece that slides in once and then stays put.
    // Placed: the grave flowers, standing up from the ground once she lays them down.
    enum PieceAnim { None, Flicker, Rock, Breathe, Shuffle, Lean, Scribble, Arrive, Placed }
    const int FlickerFrames = 10, RockHoldFrames = 15;
    const float FlickerScale = 1.04f;
    static readonly int[] RockDegrees = { -1, 0, 1, 0 };
    // Breathe: up 1 px for half of every 3 s. Shuffle: 0,+1,0,-1 px (30 frames
    // each) and a 2 px dip for 12 frames every 150 (feeding). Lean: the whole
    // figure tips 1 degree to the right (toward the desk) for 20 of every 150
    // frames, about its feet. Scribble: 1 px jitter for 12 of
    // every 60 frames, and a -1,0,+1,0 degree sway (6 frames each) every 240.
    const int BreathePeriod = 90, ShufflePeriod = 30, DipPeriod = 150, DipFrames = 12, DipDepth = 2;
    const int LeanPeriod = 150, LeanFrames = 20, ScribblePeriod = 60, ScribbleFrames = 12, SwayPeriod = 240, SwayHold = 6;
    static readonly int[] ShuffleSteps = { 0, 1, 0, -1 };
    // Sip in LabNight: the cup (and the hands on it) lifts 1 px for 8 of every 60 frames.
    const int SipPeriod = 60, SipFrames = 8;
    // night_overlay steps in over 5 Bayer thresholds, 6 frames each (1 s), and out the same way before a fold.
    const int OverlaySteps = 5, OverlayStepFrames = 6;
    const int OverlayFrames = OverlaySteps * OverlayStepFrames;

    /// <summary>Part of a sprite that moves on its own: columns x0..x1, rows y0..y1 (inclusive).</summary>
    struct InkRegion { public int x0, x1, y0, y1; }
    static readonly System.Collections.Generic.Dictionary<string, InkRegion> MovingParts =
        new System.Collections.Generic.Dictionary<string, InkRegion>
    {
        { PuppetSitCup, new InkRegion { x0 = 24, x1 = 47, y0 = 33, y1 = 40 } },   // cup and the hands on it
    };

    struct ScenePiece
    {
        public string id; public int x, bottom; public bool flip; public int level; public bool anchorTop; public PieceAnim anim;
        public int pivotCol, footRow, stopX;
        /// <summary>Drawn above a night overlay with the figures (what she sits on must not vanish under her).</summary>
        public bool aboveNight;
        /// <summary>Still standing when grief lays the grave scene down (the stone, the flowers).</summary>
        public bool griefKeep;
        public ScenePiece(string id, int x, int bottom, bool flip = false, int level = U, bool anchorTop = false,
            PieceAnim anim = PieceAnim.None, int pivotCol = 0, int footRow = 0, int stopX = 0)
        {
            this.id = id; this.x = x; this.bottom = bottom; this.flip = flip; this.level = level;
            this.anchorTop = anchorTop; this.anim = anim; this.pivotCol = pivotCol; this.footRow = footRow; this.stopX = stopX;
            aboveNight = false; griefKeep = false;
        }
        public bool IsFigure => anim == PieceAnim.Rock || anim == PieceAnim.Breathe || anim == PieceAnim.Shuffle
            || anim == PieceAnim.Lean || anim == PieceAnim.Scribble;

        /// <summary>A puppet-like figure on the y 171 floor: feet on row 55, moving about its rod column.</summary>
        public static ScenePiece Figure(string id, int x, PieceAnim anim, int pivotCol) =>
            new ScenePiece(id, x, PuppetFootY + PuppetBelowFeet, anim: anim, pivotCol: pivotCol, footRow: PuppetFootRow);
    }

    sealed class SceneLayer
    {
        public float speed; public int period; public bool road; public ScenePiece[] pieces;
        /// <summary>The layer's own sparse ground row (the room's floor lines), or -1 for none.</summary>
        public int groundBottom = -1, groundLevel = U;
    }

    sealed class SceneDef
    {
        public string state; public SceneLayer[] layers; public string puppet; public int puppetX;
        public SceneIdle idle; public int sinkPeriod, sinkFrames;
        /// <summary>A full-stage dither drawn over every piece and under the figures (LabNight).</summary>
        public string overlay;
        /// <summary>Draw the room's dotted floor rows (y 174, 177, 179) like the lab does.</summary>
        public bool labFloor;
        /// <summary>States of one family share their layers and change without a fold.</summary>
        public string family;
        /// <summary>Rain falls over this scene.</summary>
        public bool rain;
        public bool Scrolls { get { foreach (SceneLayer l in layers) if (l.speed > 0f) return true; return false; } }
        public int RoadLayer { get { for (int i = 0; i < layers.Length; i++) if (layers[i].road) return i; return layers.Length - 1; } }
    }

    // Layers are listed back to front: that is draw order, stand-up order (+4
    // frames each) and, reversed, fold order. The puppet draws right after the
    // road layer. road_strip is anchored by its top row (y 169).
    static readonly SceneDef[] Scenes =
    {
        new SceneDef
        {
            state = OSChapter04PresentationBridge.WastelandWalk, puppet = "puppet_backpack", puppetX = PuppetLeft, idle = SceneIdle.Hop,
            layers = new[]
            {
                new SceneLayer { speed = .10f, period = 420, pieces = new[] {
                    new ScenePiece("building_small", 40, 130, false, 3), new ScenePiece("building_broken", 150, 128, true, 3),
                    new ScenePiece("building_small", 252, 129, false, 3) } },
                new SceneLayer { speed = .18f, period = 460, pieces = new[] {
                    new ScenePiece("hill_far_b", 0, 134, false, 4), new ScenePiece("hill_far_a", 180, 138, false, 5) } },
                new SceneLayer { speed = .42f, period = 520, pieces = new[] {
                    new ScenePiece("hill_far_a", 60, 152, false, 8), new ScenePiece("hill_far_b", 230, 154, false, 8) } },
                new SceneLayer { speed = .80f, period = 360, road = true, pieces = new[] {
                    new ScenePiece("road_strip", -20, 169, anchorTop: true) } },
                new SceneLayer { speed = 1.40f, period = 640, pieces = new[] {
                    new ScenePiece("fg_grass", 20, 183), new ScenePiece("fg_sign", 200, 183),
                    new ScenePiece("fg_stones", 250, 183), new ScenePiece("fg_grass", 150, 183) } },
            },
        },
        new SceneDef
        {
            state = OSChapter04PresentationBridge.SettlementWait, puppet = "puppet_sit_ground", puppetX = 24,
            idle = SceneIdle.Sink, sinkPeriod = 120, sinkFrames = 8,
            layers = new[]
            {
                new SceneLayer { pieces = new[] {
                    new ScenePiece("hill_far_a", 10, 134, false, 4), new ScenePiece("hill_far_b", 200, 136, false, 4) } },
                new SceneLayer { pieces = new[] {
                    new ScenePiece("tent_small", 150, 151, false, 10), new ScenePiece("tent_big", 250, 151, false, 10) } },
                new SceneLayer { pieces = new[] { new ScenePiece("road_strip", -20, 169, anchorTop: true) } },
                new SceneLayer { road = true, pieces = new[] {
                    new ScenePiece("drying_rack", 112, 171), new ScenePiece("tent_big", 168, 171), new ScenePiece("bucket", 232, 171),
                    new ScenePiece("tent_small", 246, 171), new ScenePiece("campfire", 292, 171, anim: PieceAnim.Flicker) } },
                new SceneLayer { pieces = new[] { new ScenePiece("fg_stones", 100, 183), new ScenePiece("fg_grass", 290, 183) } },
            },
        },
        new SceneDef
        {
            state = OSChapter04PresentationBridge.TentCare, puppet = "puppet_sit_ground", puppetX = 176,
            idle = SceneIdle.Sink, sinkPeriod = 120, sinkFrames = 8,
            layers = new[]
            {
                new SceneLayer { pieces = new[] { new ScenePiece("tent_interior", 10, 171) } },
                new SceneLayer { road = true, pieces = new[] {
                    new ScenePiece("mat", 52, 172),
                    // 84 x 70 figure, feet on row 55 at y 171 (sprite bottom 171 + 14), sways about body column 42.
                    new ScenePiece("puppet_mother_daughter", 58, PuppetFootY + PuppetBelowFeet, anim: PieceAnim.Rock, pivotCol: 42, footRow: 55),
                    new ScenePiece("lantern", 160, 171) } },
            },
        },
        // Line 27: the boy (half covered in scales) by his feverish mother. Live is
        // only watching: no puppet of hers on this stage.
        new SceneDef
        {
            state = OSChapter04PresentationBridge.NorthSettlement, puppet = null,
            layers = new[]
            {
                new SceneLayer { pieces = new[] {
                    new ScenePiece("hill_far_b", 20, 134, false, 4), new ScenePiece("hill_far_a", 170, 137, false, 4) } },
                new SceneLayer { pieces = new[] {
                    new ScenePiece("tent_small", 4, 151, false, 10), new ScenePiece("drying_rack", 150, 151, false, 10),
                    new ScenePiece("tent_big", 236, 151, true, 10) } },
                new SceneLayer { pieces = new[] { new ScenePiece("road_strip", -20, 169, anchorTop: true) } },
                new SceneLayer { road = true, pieces = new[] {
                    new ScenePiece("water_jar", 10, 171), new ScenePiece("campfire", 168, 171, anim: PieceAnim.Flicker),
                    new ScenePiece("tent_big", 196, 171),
                    ScenePiece.Figure("puppet_mother_bed", 28, PieceAnim.Breathe, 42),
                    ScenePiece.Figure("puppet_boy_scales", 104, PieceAnim.Shuffle, 34) } },
                new SceneLayer { pieces = new[] {
                    new ScenePiece("fg_grass", 2, 183), new ScenePiece("fg_stones", 140, 183), new ScenePiece("fg_grass", 290, 183) } },
            },
        },
        // Line 36: the tidied lab at night with the two researchers; she sits on
        // the stool with her cup. Layers come from LabLayout (see FindScene).
        new SceneDef
        {
            state = OSChapter04PresentationBridge.LabNight, puppet = PuppetSitCup, puppetX = TidyRestX,
            idle = SceneIdle.Sip, overlay = "night_overlay", labFloor = true,
        },
        // Line 43: the walk again, slower in mood: an empty skyline, no sign, and
        // the spire that the grave scene stands under slides in once and stays.
        new SceneDef
        {
            state = OSChapter04PresentationBridge.FlowerWalk, puppet = PuppetFlowers, puppetX = PuppetLeft, idle = SceneIdle.Hop,
            family = WalkFamily,
            layers = new[]
            {
                new SceneLayer { speed = .10f, period = 220, pieces = new[] {
                    new ScenePiece("skyline_empty", 50, 131, false, 6),
                    new ScenePiece("spire_far", Width, 128, false, 5, anim: PieceAnim.Arrive, stopX: SpireStopX) } },
                new SceneLayer { speed = .18f, period = 460, pieces = new[] {
                    new ScenePiece("hill_far_b", 0, 136, false, 4), new ScenePiece("hill_far_a", 180, 139, false, 5) } },
                new SceneLayer { speed = .42f, period = 520, pieces = new[] {
                    new ScenePiece("hill_far_a", 60, 152, false, 8), new ScenePiece("hill_far_b", 230, 154, false, 8) } },
                new SceneLayer { speed = .80f, period = 360, road = true, pieces = new[] {
                    new ScenePiece("road_strip", -20, 169, anchorTop: true) } },
                new SceneLayer { speed = 1.40f, period = 640, pieces = new[] {
                    new ScenePiece("fg_grass", 20, 183), new ScenePiece("fg_grass", 150, 183), new ScenePiece("fg_stones", 250, 183) } },
            },
        },
        // Round 8 (layers filled in by FindScene): line 3 starts the rain on the
        // same walk; lines 4, 6 and 7 are the grave (see the Grave partial).
        new SceneDef
        {
            state = FlowerWalkRain, puppet = PuppetFlowers, puppetX = PuppetLeft, idle = SceneIdle.Hop,
            family = WalkFamily, rain = true,
        },
        new SceneDef { state = OSChapter04PresentationBridge.Grave, puppet = PuppetFlowers, puppetX = GravePuppetX, family = GraveFamily, rain = true },
        new SceneDef { state = GravePlaceFlowers, puppet = PuppetFlowers, puppetX = GravePuppetX, family = GraveFamily, rain = true },
        new SceneDef { state = OSChapter04PresentationBridge.GraveBreakdown, puppet = PuppetIdle, puppetX = GravePuppetX, family = GraveFamily, rain = true },
    };

    /// <summary>Where the FlowerWalk spire comes to rest (and where a restore puts it).</summary>
    public const int SpireStopX = 230;
    // LabNight additions to the tidied room.
    const int NightBoxX = 290, NightLampX = 214, NightLampBottom = 144, ResearcherAX = 144, ResearcherBX = 232;

    static SceneDef FindScene(string state)
    {
        foreach (SceneDef def in Scenes)
        {
            if (def.state != state) continue;
            if (def.layers == null)
                def.layers = def.family == GraveFamily ? GraveSceneLayers()
                    : def.family == WalkFamily ? FindScene(OSChapter04PresentationBridge.FlowerWalk).layers
                    : LabNightLayers();
            return def;
        }
        return null;
    }

    /// <summary>
    /// LabNight is the LabTidy room (tidied art for the Both rows) plus the desk
    /// lamp and the two researchers, with the box moved in front of the fridge.
    /// Built on first use so it always follows LabLayout.
    /// </summary>
    static SceneLayer[] LabNightLayers()
    {
        var layers = new SceneLayer[LabLayers.Length];
        for (int layer = 0; layer < layers.Length; layer++)
        {
            var pieces = new System.Collections.Generic.List<ScenePiece>();
            foreach (LabPiece row in LabLayout)
            {
                if (row.layer != layer || row.set == LabSet.Mess) continue;
                int x = row.id == "box" ? NightBoxX : row.x;
                pieces.Add(new ScenePiece(row.tidyId ?? row.id, x, row.bottom, row.flip, row.level) { aboveNight = row.id == "stool" });
                if (row.id == "desk_lab") pieces.Add(new ScenePiece("desk_lamp", NightLampX, NightLampBottom));
            }
            if (layer == LabRoad)
            {
                pieces.Add(ScenePiece.Figure("researcher_a", ResearcherAX, PieceAnim.Lean, 42));
                pieces.Add(ScenePiece.Figure("researcher_b", ResearcherBX, PieceAnim.Scribble, 34));
            }
            layers[layer] = new SceneLayer
            {
                road = layer == LabRoad, pieces = pieces.ToArray(),
                groundBottom = LabLayers[layer].groundBottom, groundLevel = LabLayers[layer].groundLevel,
            };
        }
        return layers;
    }

    // ---- state
    SceneDef sceneFrom, sceneTo;   // sceneFrom == null: folding out of the lab
    LabSet labFromSet;
    string freezeArt, queuedState;
    int freezeX;
    // MedicineReady plan: keep playing the tidy loop for medWait frames (from
    // medTidyFrame), optionally hop down off the stool, then hop from medFromX to 118.
    int medWait, medTidyFrame, medFromX;
    bool medSitDown, medTidyRestored;
    // The scene shown (to) / folding away (from) was built by a restore: it is
    // already in its settled look (overlay fully dark, spire at rest).
    bool toRestored, fromRestored;

    /// <summary>A scene request is waiting for the current transition to finish.</summary>
    public bool HasQueuedRequest => queuedState != null;
    public string QueuedState => queuedState;
    /// <summary>The scene being shown or stood up (null outside sections 2-3).</summary>
    public string SceneState => phase == Phase.Medicine ? OSChapter04PresentationBridge.MedicineReady
        : phase == Phase.SceneOut || phase == Phase.SceneIn || phase == Phase.Scene ? sceneTo?.state : null;

    bool IsScenePhase => phase == Phase.SceneOut || phase == Phase.SceneIn || phase == Phase.Scene;
    bool LabFoldingOut => phase == Phase.SceneOut && sceneFrom == null;

    /// <summary>Begin() hook for the four section 2-3 states; false for anything else.</summary>
    bool TryBeginScene(string stateId, bool restoring)
    {
        if (!HasSceneArtwork(stateId)) return false;
        queuedState = null;
        if (restoring) { BuildSceneDirect(stateId); return true; }
        if (CanStartScene(stateId)) StartScene(stateId);
        else queuedState = stateId;
        return true;
    }

    /// <summary>A restore (or a jump) straight into the loop of the state.</summary>
    void BuildSceneDirect(string stateId)
    {
        clock = 0d; travel = 0f; stepFrame = 0; enteredByDoor = false;
        if (stateId == OSChapter04PresentationBridge.MedicineReady)
        {
            medWait = 0; medSitDown = false; medFromX = MedicineX;
            Enter(Phase.Medicine);
            return;
        }
        sceneFrom = null; sceneTo = FindScene(stateId);
        toRestored = true;
        RestoreGraveSection(sceneTo);
        Enter(Phase.Scene);
    }

    bool CanStartScene(string stateId)
    {
        if (stateId == OSChapter04PresentationBridge.MedicineReady) return phase == Phase.Tidy;
        return phase == Phase.Tidy || phase == Phase.Medicine || phase == Phase.Scene || phase == Phase.Rummage;
    }

    void StartScene(string stateId)
    {
        clock = 0d; enteredByDoor = false;
        if (stateId == OSChapter04PresentationBridge.MedicineReady) { StartMedicine(); return; }
        SceneDef next = FindScene(stateId);
        if (phase == Phase.Scene && next.family != null && next.family == sceneTo?.family)
        { SwitchWithinFamily(next); return; }
        if (IsGraveFamily(next)) placeFrame = NeedsFlowers(next) ? 0 : -1;
        else ResetGraveSection();
        // Freeze whatever she is showing now; she folds with the road layer.
        freezeX = Mathf.RoundToInt(PuppetX);
        DrawInk();
        // A scene without her (NorthSettlement) folds away without her too.
        freezeArt = puppetDrawn ? lastPuppetArt ?? puppetArt : null;
        if (phase == Phase.Scene) { sceneFrom = sceneTo; fromRestored = toRestored; }
        else { sceneFrom = null; labFromSet = phase == Phase.Rummage ? LabSet.Mess : LabSet.Tidy; }
        sceneTo = next;
        toRestored = false;
        Enter(Phase.SceneOut);
    }

    void StartMedicine()
    {
        int tf = phaseFrame;
        TidyMoment at = TidyAt(tf, tidyRestored);
        medTidyFrame = tf; medTidyRestored = tidyRestored;
        medSitDown = false; medWait = 0; medFromX = at.x;
        switch (at.mode)
        {
            case TidyMode.Walk:
                medWait = (StepFrames - at.frame % StepFrames) % StepFrames;
                if (medWait > 0) medFromX = TidyAt(tf + medWait - 1, tidyRestored).x;
                break;
            case TidyMode.SitUp:
                medWait = StepFrames - at.frame + TidyLandFrames;
                medSitDown = true; medFromX = TidyRestX;
                break;
            case TidyMode.Sit:
                medSitDown = true; medFromX = TidyRestX;
                break;
            case TidyMode.SitDown:
                medWait = StepFrames + TidyLandFrames - at.frame;
                medFromX = TidyRestX;
                break;
        }
        Enter(Phase.Medicine);
    }

    /// <summary>Start a waiting request once the stage has settled (called every frame).</summary>
    void ApplyQueuedRequest()
    {
        if (queuedState == null || halted) return;
        switch (phase)
        {
            case Phase.AtDoor:
                // Already past line 9: walk in, the queued state follows the lab.
                GotoEnterDoor();
                return;
            case Phase.Rummage:
                if (queuedState == OSChapter04PresentationBridge.MedicineReady) { BeginTidyPreview(false); return; }
                break;
            case Phase.Idle: case Phase.Rise: case Phase.Walk:
                // The street is long gone in the story: jump to the tidied lab.
                BeginTidyPreview(true);
                break;
            case Phase.Tidy: case Phase.Medicine: case Phase.Scene:
                break;
            default:
                return; // a transition is still playing
        }
        if (!CanStartScene(queuedState)) return;
        string next = queuedState;
        queuedState = null;
        StartScene(next);
    }

    // ================================================================ frames

    static int SceneFoldDelay(int layer, int count) => (count - 1 - layer) * LayerStagger;
    static int SceneRiseDelay(int layer) => layer * LayerStagger;
    int OutLayerCount => sceneFrom != null ? sceneFrom.layers.Length : LabLayers.Length;
    // A night scene first lifts its overlay, then folds.
    int OutLead => sceneFrom != null && sceneFrom.overlay != null ? OverlayFrames : 0;
    int OutFlatFrame => OutLead + (OutLayerCount - 1) * LayerStagger + FoldAngles.Length - 1;
    int InDoneFrame => (sceneTo.layers.Length - 1) * LayerStagger + RiseFrames - 1;
    int MedicineHopStart => medWait + (medSitDown ? StepFrames + TidyLandFrames : 0);
    int MedicineHopFrames => medFromX == MedicineX ? 0 : HopFrames(MedicineX - medFromX);

    void StepScene()
    {
        if (IsScenePhase) StepGraveSection();
        switch (phase)
        {
            case Phase.Medicine:
                phaseFrame++;
                break;
            case Phase.SceneOut:
                phaseFrame++;
                if (phaseFrame >= OutFlatFrame + FlatHoldFrames) { travel = 0f; Enter(Phase.SceneIn); }
                break;
            case Phase.SceneIn:
                phaseFrame++;
                if (phaseFrame > InDoneFrame) Enter(Phase.Scene);
                break;
            case Phase.Scene:
                phaseFrame++;
                if (sceneTo.Scrolls) travel += 1f;
                break;
        }
    }

    Pose SceneLayerPose(SceneDef def, int layer)
    {
        if (phase == Phase.SceneOut && def == sceneFrom) return FoldPose(phaseFrame - OutLead - SceneFoldDelay(layer, def.layers.Length));
        if (phase == Phase.SceneIn && def == sceneTo) return RisePose(phaseFrame - SceneRiseDelay(layer));
        if (def == sceneTo && ShowsGrief) return GriefLayerPose(layer);
        return Pose.Upright;
    }

    // ================================================================ drawing

    /// <summary>Draws scene frames; false when the lab is folding out (the lab drawing handles that).</summary>
    bool DrawSceneFrame()
    {
        if (!IsScenePhase || LabFoldingOut) return false;
        SceneDef def = phase == Phase.SceneOut ? sceneFrom : sceneTo;
        // The stage ground line never folds; road_strip covers it where a scene has one.
        for (int x = 0; x < Width; x++) canvas[GroundY * Width + x] = OSChapter04TheaterPiece.Gray;
        int road = def.RoadLayer;
        // Under a night overlay the figures, her stool and she stay above the dark:
        // draw them after every layer and the overlay. Otherwise they stand in the
        // road layer, in front of it and behind the layers nearer the audience.
        bool lifted = def.overlay != null;
        for (int i = 0; i < def.layers.Length; i++)
        {
            DrawSceneLayer(def, i, lifted ? FigureMode.Skip : FigureMode.Inline);
            if (def.labFloor && i == road) DrawSceneFloor(def);
            if (!lifted && i == road) DrawScenePuppet(def);
        }
        if (lifted)
        {
            DrawOverlay(def);
            DrawSceneLayer(def, road, FigureMode.Only);
            DrawScenePuppet(def);
        }
        DrawRain();
        return true;
    }

    enum FigureMode { Inline, Skip, Only }

    /// <summary>The room's dotted floor rows, lying down and standing up with the road and near layers.</summary>
    void DrawSceneFloor(SceneDef def)
    {
        int road = def.RoadLayer, near = road + 1 < def.layers.Length ? road + 1 : road;
        bool roadDots = SceneLayerPose(def, road).Visible, nearDots = SceneLayerPose(def, near).Visible;
        for (int x = 0; x < Width; x++)
        {
            if (roadDots && x % 2 == 0) canvas[174 * Width + x] = OSChapter04TheaterPiece.Gray;
            if (nearDots && x % 4 == 0) canvas[177 * Width + x] = OSChapter04TheaterPiece.Gray;
            if (nearDots && x % 6 == 0) canvas[179 * Width + x] = OSChapter04TheaterPiece.Gray;
        }
    }

    /// <summary>How many of the overlay's Bayer steps show (0 none .. OverlaySteps fully dark).</summary>
    public int OverlayLevel
    {
        get
        {
            if (phase == Phase.Scene && sceneTo?.overlay != null)
                return toRestored ? OverlaySteps : Math.Min(OverlaySteps, phaseFrame / OverlayStepFrames + 1);
            if (phase == Phase.SceneOut && sceneFrom?.overlay != null && phaseFrame < OutLead)
                return OverlaySteps - 1 - phaseFrame / OverlayStepFrames;
            return 0;
        }
    }

    /// <summary>The overlay's black dots where Bayer is under the current step: it darkens in even steps.</summary>
    void DrawOverlay(SceneDef def)
    {
        int level = OverlayLevel;
        if (level <= 0) return;
        OSChapter04TheaterPiece art = library.Get(def.overlay);
        byte[] ink = art.Dimmed(OSChapter04TheaterPiece.Undimmed);
        int threshold = level * 16 / OverlaySteps;
        int w = Math.Min(Width, art.Width), h = Math.Min(Height, art.Height);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte value = ink[y * art.Width + x];
                if (value == OSChapter04TheaterPiece.Clear) continue;
                if (OSChapter04TheaterPiece.Bayer[(y & 3) * 4 + (x & 3)] < threshold) canvas[y * Width + x] = value;
            }
    }

    void DrawSceneLayer(SceneDef def, int index, FigureMode figures)
    {
        SceneLayer layer = def.layers[index];
        Pose pose = SceneLayerPose(def, index);
        if (!pose.Visible) return;
        if (figures != FigureMode.Only && layer.groundBottom >= 0) DrawLayerGround(layer.groundBottom, layer.groundLevel, 0);
        int shift = layer.speed > 0f ? Mathf.FloorToInt(layer.speed * travel) : 0;
        foreach (ScenePiece piece in layer.pieces)
        {
            if (piece.IsFigure || piece.aboveNight ? figures == FigureMode.Skip : figures == FigureMode.Only) continue;
            OSChapter04TheaterPiece art = library.Get(piece.id);
            int bottom = piece.anchorTop ? piece.bottom + art.Height - 1 : piece.bottom;
            if (piece.anim == PieceAnim.Arrive)
            {
                // Slides in from piece.x with its layer, once; it rests at stopX and leaves the loop.
                bool settled = def == sceneFrom && phase == Phase.SceneOut ? fromRestored : toRestored;
                int ax = settled ? piece.stopX : Math.Max(piece.stopX, piece.x - shift);
                if (ax < Width) DrawScenePiece(piece, art, ax, bottom, pose);
                continue;
            }
            if (layer.speed <= 0f)
            {
                Pose piecePose = GravePiecePose(def, index, piece, pose);
                if (piecePose.Visible) DrawScenePiece(piece, art, piece.x, bottom, piecePose);
                continue;
            }
            int x = PosMod(piece.x - shift, layer.period) - ScrollMargin;
            // Wrap copies so a piece as wide as its period (road_strip) never leaves a gap.
            for (int copy = -1; copy <= 1; copy++)
            {
                int cx = x + copy * layer.period;
                if (cx < Width && cx + art.Width > 0) DrawScenePiece(piece, art, cx, bottom, pose);
            }
        }
    }

    void DrawScenePiece(ScenePiece piece, OSChapter04TheaterPiece art, int left, int bottom, Pose pose)
    {
        bool upright = pose.angle == 0 && Mathf.Approximately(pose.scale, 1f) && phase == Phase.Scene;
        if (piece.anim == PieceAnim.Flicker && upright)
        {
            // A campfire flame: 1.04 wide every other 10 frames, about its centre.
            float sx = (phaseFrame / FlickerFrames) % 2 == 1 ? FlickerScale : 1f;
            Blit(art.Dimmed(piece.level), art.Width, art.Height, art.Width * .5f, art.Height,
                left + art.Width * .5f, bottom + 1, sx, 1f, piece.flip);
            return;
        }
        if (piece.IsFigure)
        {
            // A puppet-like figure: anchored on its feet, lies down and stands up like her.
            int f = phaseFrame, dx = 0, dy = 0;
            float degrees = 0f;
            byte[] ink = art.Dimmed(piece.level);
            if (upright)
                switch (piece.anim)
                {
                    case PieceAnim.Rock: // -1, 0, +1, 0 degrees, 15 frames each, no easing
                        degrees = RockDegrees[(f / RockHoldFrames) % RockDegrees.Length];
                        break;
                    case PieceAnim.Breathe:
                        dy = f % BreathePeriod >= BreathePeriod / 2 ? -1 : 0;
                        break;
                    case PieceAnim.Shuffle:
                        dx = ShuffleSteps[(f / ShufflePeriod) % ShuffleSteps.Length];
                        dy = f % DipPeriod >= DipPeriod - DipFrames ? DipDepth : 0;
                        break;
                    case PieceAnim.Lean: // +1 degree leans her top to the right, toward the desk
                        if (f % LeanPeriod >= LeanPeriod - LeanFrames) degrees = 1f;
                        break;
                    case PieceAnim.Scribble:
                        dx = f % ScribblePeriod < ScribbleFrames ? (f / 2) % 2 : 0;
                        int sway = f % SwayPeriod - (SwayPeriod - SwayHold * RockDegrees.Length);
                        if (sway >= 0) degrees = RockDegrees[sway / SwayHold];
                        break;
                }
            int above = piece.footRow + 1;
            float sy = pose.Height(above) / (float)above;
            if (sy <= 0f) return;
            int footY = bottom - (art.Height - 1 - piece.footRow);
            Blit(ink, art.Width, art.Height, piece.pivotCol + .5f, piece.footRow + 1,
                left + dx + piece.pivotCol + .5f, footY + 1 + dy, 1f, sy, piece.flip, degrees);
            return;
        }
        DrawPiece(art, piece.level, left, bottom, piece.flip, pose);
    }

    readonly System.Collections.Generic.Dictionary<string, byte[]> movedParts = new System.Collections.Generic.Dictionary<string, byte[]>();

    /// <summary>The art with its MovingParts region shifted dy rows (down when positive); cached.</summary>
    byte[] MovedPart(OSChapter04TheaterPiece art, int level, int dy)
    {
        string key = art.Id + "/" + level + "/" + dy;
        if (movedParts.TryGetValue(key, out byte[] cached)) return cached;
        byte[] src = art.Dimmed(level), result = (byte[])src.Clone();
        if (MovingParts.TryGetValue(art.Id, out InkRegion r))
        {
            int x1 = Math.Min(r.x1, art.Width - 1), y1 = Math.Min(r.y1, art.Height - 1);
            // Clear the region, then paint it back moved: rows it uncovers stay clear, rows it lands on are overdrawn.
            for (int y = r.y0; y <= y1; y++)
                for (int x = r.x0; x <= x1; x++) result[y * art.Width + x] = OSChapter04TheaterPiece.Clear;
            for (int y = r.y0; y <= y1; y++)
            {
                int ty = y + dy;
                if (ty < 0 || ty >= art.Height) continue;
                for (int x = r.x0; x <= x1; x++)
                {
                    byte value = src[y * art.Width + x];
                    if (value != OSChapter04TheaterPiece.Clear) result[ty * art.Width + x] = value;
                }
            }
            // Lifted up, the part leaves its bottom rows: keep the art there so no hole opens in the body.
            for (int y = Math.Max(r.y0, y1 + dy + 1); dy < 0 && y <= y1; y++)
                for (int x = r.x0; x <= x1; x++)
                    if (result[y * art.Width + x] == OSChapter04TheaterPiece.Clear) result[y * art.Width + x] = src[y * art.Width + x];
        }
        movedParts[key] = result;
        return result;
    }

    void DrawScenePuppet(SceneDef def)
    {
        string artId; int left; float sx = 1f, sy = 1f, degrees = 0f; int lift = 0;
        bool sipping = false;
        if (phase == Phase.SceneOut)
        {
            artId = freezeArt; left = freezeX;
            sy = SceneLayerPose(def, def.RoadLayer).Height(PuppetFigureHeight) / (float)PuppetFigureHeight;
        }
        else
        {
            artId = def.puppet; left = def.puppetX;
            if (phase == Phase.SceneIn)
                sy = SceneLayerPose(def, def.RoadLayer).Height(PuppetFigureHeight) / (float)PuppetFigureHeight;
            else if (IsGraveFamily(def)) GravePuppet(def, ref artId, ref left, ref sx, ref sy, ref lift, ref degrees);
            else if (def.idle == SceneIdle.Hop) HopPose(phaseFrame % StepFrames, out sx, out sy, out lift);
            else if (def.idle == SceneIdle.Sip) sipping = phaseFrame % SipPeriod >= SipPeriod - SipFrames;
            else lift = phaseFrame % def.sinkPeriod >= def.sinkPeriod - def.sinkFrames ? 1 : 0;
        }
        if (artId == null || sy <= 0f) return;
        OSChapter04TheaterPiece art = library.Get(artId);
        lastPuppetArt = art.Id;
        puppetDrawn = true;
        byte[] ink = sipping ? MovedPart(art, OSChapter04TheaterPiece.Undimmed, -1) : art.Dimmed(OSChapter04TheaterPiece.Undimmed);
        Blit(ink, art.Width, art.Height,
            PuppetCenterCol + .5f, art.Height - PuppetBelowFeet, left + PuppetCenterCol + .5f, PuppetFootY + 1 + lift, sx, sy, false, degrees);
    }

    /// <summary>Medicine and lab fold-out puppet poses (called from DrawPuppet).</summary>
    bool SceneLabPuppet(ref OSChapter04TheaterPiece art, ref int left, ref float sx, ref float sy, ref int lift, ref float degrees)
    {
        if (LabFoldingOut)
        {
            art = library.Get(freezeArt ?? puppetArt);
            left = freezeX;
            int height = FoldPose(phaseFrame - TidyFoldDelay(LabRoad)).Height(PuppetFigureHeight);
            if (height <= 0) { sy = 0f; return true; }
            sy = height / (float)PuppetFigureHeight;
            return true;
        }
        if (phase != Phase.Medicine) return false;
        int f = phaseFrame, hopStart = MedicineHopStart, holdStart = hopStart + MedicineHopFrames;
        if (f < medWait)
        {
            TidyPuppet(TidyAt(medTidyFrame + f, medTidyRestored), ref art, ref left, ref sx, ref sy, ref lift, ref degrees);
        }
        else if (f < hopStart)
        {
            TidyPuppet(new TidyMoment { mode = TidyMode.SitDown, x = TidyRestX, frame = f - medWait },
                ref art, ref left, ref sx, ref sy, ref lift, ref degrees);
        }
        else if (f < holdStart)
        {
            art = library.Get(PuppetIdle);
            left = HopX(medFromX, MedicineX, f - hopStart);
            HopPose((f - hopStart) % StepFrames, out sx, out sy, out lift);
        }
        else
        {
            art = library.Get(PuppetHoldVial);
            left = MedicineX;
            lift = (f - holdStart) % MedicineLookPeriod >= MedicineLookPeriod - MedicineLookFrames ? -1 : 0;
        }
        return true;
    }

    /// <summary>Puppet x this frame in sections 2-3 (QA and preview), or null when not in them.</summary>
    int? ScenePuppetX
    {
        get
        {
            if (phase == Phase.SceneOut) return freezeX;
            if (phase == Phase.SceneIn || phase == Phase.Scene) return sceneTo.puppet != null ? sceneTo.puppetX : (int?)null;
            if (phase != Phase.Medicine) return null;
            int f = phaseFrame, hopStart = MedicineHopStart, holdStart = hopStart + MedicineHopFrames;
            if (f < medWait) return TidyAt(medTidyFrame + f, medTidyRestored).x;
            if (f < hopStart) return TidyRestX;
            if (f < holdStart) return HopX(medFromX, MedicineX, f - hopStart);
            return MedicineX;
        }
    }

    /// <summary>Left x of the scene's arriving piece (the FlowerWalk spire) this frame, or null (QA).</summary>
    public int? SpireX
    {
        get
        {
            if (phase != Phase.Scene && phase != Phase.SceneIn) return null;
            foreach (SceneLayer layer in sceneTo.layers)
                foreach (ScenePiece piece in layer.pieces)
                    if (piece.anim == PieceAnim.Arrive)
                        return toRestored ? piece.stopX : Math.Max(piece.stopX, piece.x - Mathf.FloorToInt(layer.speed * travel));
            return null;
        }
    }

    /// <summary>MedicineReady has settled on x 118 holding the vial (QA).</summary>
    public bool IsHoldingVial => phase == Phase.Medicine && phaseFrame >= MedicineHopStart + MedicineHopFrames;
}
