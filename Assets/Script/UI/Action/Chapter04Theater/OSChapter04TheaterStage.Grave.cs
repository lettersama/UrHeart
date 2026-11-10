using System;
using UnityEngine;

/// <summary>
/// Round 8, the last section: the walk to the grave in the rain, the grave,
/// laying the flowers down and the breakdown. States of one family share
/// their layers and change without a fold (FlowerWalk -> FlowerWalkRain only
/// starts the rain; Grave -> GravePlaceFlowers -> GraveBreakdown only change
/// her). Rain never folds and keeps falling through a scene change.
/// SetGriefTier (0..3) folds her back 15/30/45 degrees with a tremor and lays
/// the grave scene down from far to near until only she, the stone and the
/// flowers stand. Every state and tier restores straight into its settled look.
/// </summary>
public sealed partial class OSChapter04TheaterStage
{
    // State names the backend adds for this section (Grave and GraveBreakdown already exist).
    public const string FlowerWalkRain = "FlowerWalkRain", GravePlaceFlowers = "GravePlaceFlowers";
    const string WalkFamily = "walk", GraveFamily = "grave";
    public const int GravePuppetX = 130, GraveFlowersX = 200;
    public const string PuppetFlowers = "puppet_flowers", PuppetKneelPlace = "puppet_kneel_place";

    static bool IsGraveSectionState(string stateId) => stateId == FlowerWalkRain || stateId == GravePlaceFlowers
        || stateId == OSChapter04PresentationBridge.Grave || stateId == OSChapter04PresentationBridge.GraveBreakdown;

    // ---- the grave scene (layout by the author, layers back to front)
    static SceneLayer[] graveLayers;
    static SceneLayer[] GraveSceneLayers() => graveLayers ?? (graveLayers = new[]
    {
        new SceneLayer { pieces = new[] {
            new ScenePiece("chapel_ruin", 28, 134, false, 6),
            // The spire FlowerWalk left at x 230: the same tower, now close.
            new ScenePiece("spire_far", SpireStopX, 128, false, 5) } },
        new SceneLayer { pieces = new[] { new ScenePiece("hill_far_a", 110, 140, false, 4) } },
        new SceneLayer { pieces = new[] {
            new ScenePiece("fence_iron", -10, 153, false, 9), new ScenePiece("fence_iron", 38, 153, false, 9),
            new ScenePiece("fence_iron", 86, 153, false, 9), new ScenePiece("fence_iron", 270, 153, false, 9),
            new ScenePiece("dead_tree", 236, 151, false, 12), new ScenePiece("brick_wall_low", 146, 155, false, 11) } },
        new SceneLayer { pieces = new[] { new ScenePiece("road_strip", -20, 169, anchorTop: true) } },
        new SceneLayer { road = true, pieces = new[] {
            new ScenePiece("lamp_post_grave", 40, 171),
            new ScenePiece("mother_grave", 196, 171) { griefKeep = true },
            new ScenePiece("flowers_vial_ground", GraveFlowersX, 172, anim: PieceAnim.Placed) { griefKeep = true } } },
        new SceneLayer { pieces = new[] {
            new ScenePiece("fg_fence", -8, 183), new ScenePiece("fg_fence", 232, 183),
            new ScenePiece("fg_grass", 110, 183), new ScenePiece("fg_grass", 180, 183) } },
    });
    const int GraveFarLayer = 0, GraveRoadLayer = 4;

    // ---- rain: sparse gray slanted dashes over everything (the chapter 3 yard rain on this stage)
    const int RainDrops = 36, RainLength = 3;
    int rainClock;

    // ---- laying the flowers down: kneel and sink 2 px, wait, the flowers stand up, wait, stand empty-handed
    const int KneelSinkFrames = 2, PlaceHoldFrames = 15;
    const int FlowersRiseAt = KneelSinkFrames + PlaceHoldFrames;
    static int PlaceDoneFrame => FlowersRiseAt + RiseFrames + PlaceHoldFrames;
    // -1: not placed yet (Grave); counts up while placing; PlaceDoneFrame when done (restores land there).
    int placeFrame = -1;

    // ---- grief (GraveBreakdown only): her back-fold angle and the scene laid flat far to near
    public const int GriefMaxTier = 3, GriefDegreesPerTier = 15;
    const int GriefDegreesPerStep = 5, GriefStepFrames = 2;
    // Tier 3 lays the scene down: hill, back row, road strip, the lamp, then the near layer.
    static readonly int[] GriefTier3Delays = { 0, 20, 40, 50, 60 };   // layer 1, 2, 3, road extras, layer 5
    const int GriefRoadExtrasIndex = 3;
    int griefTier, griefClock, griefAngleFrom, griefAngleStart;
    // Frame (on griefClock) each grave layer starts to lie down; int.MaxValue: it stands.
    readonly int[] griefFoldAt = { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };
    int griefRoadExtrasAt = int.MaxValue;
    // Idle sway of the standing puppet: -1, 0, +1, 0 degrees, 6 frames each, once every 150 frames.
    const int SwayEvery = 150;

    /// <summary>Grief tier the TV shows (0 none .. 3).</summary>
    public int GriefTier => griefTier;
    /// <summary>Her back-fold angle this frame (QA).</summary>
    public int GriefAngle => GriefTargetReached ? griefTier * GriefDegreesPerTier : CurrentGriefAngle;
    /// <summary>Rain is falling on the stage this frame.</summary>
    public bool IsRaining => IsScenePhase && ((phase == Phase.SceneOut ? sceneFrom : sceneTo)?.rain == true
        || phase == Phase.SceneIn && sceneFrom?.rain == true);
    /// <summary>The flowers and the vial stand in front of the stone.</summary>
    public bool FlowersPlaced => placeFrame >= PlaceDoneFrame;
    /// <summary>A grave layer the grief has laid flat (QA).</summary>
    public bool IsGriefLayerFlat(int layer) => !GriefLayerPose(layer).Visible;

    bool IsGraveFamily(SceneDef def) => def != null && def.family == GraveFamily;
    bool ShowsGrief => phase == Phase.Scene && sceneTo?.state == OSChapter04PresentationBridge.GraveBreakdown;
    int CurrentGriefAngle
    {
        get
        {
            int target = griefTier * GriefDegreesPerTier, steps = (griefClock - griefAngleStart) / GriefStepFrames + 1;
            int delta = Math.Min(Math.Abs(target - griefAngleFrom), Math.Max(0, steps) * GriefDegreesPerStep);
            return griefAngleFrom + Math.Sign(target - griefAngleFrom) * delta;
        }
    }
    bool GriefTargetReached => CurrentGriefAngle == griefTier * GriefDegreesPerTier;

    /// <summary>
    /// Grief tier 0..3 (the backend calls it before each line types). The same
    /// tier again is ignored; restoring lands on the tier's final pose at once;
    /// tier 0 clears it. The TV shows it in GraveBreakdown; set earlier, it waits.
    /// </summary>
    public void SetGriefTier(int tier, bool restoring)
    {
        tier = Mathf.Clamp(tier, 0, GriefMaxTier);
        if (tier == griefTier && !restoring) return;
        int angleNow = ShowsGrief || griefClock > 0 ? CurrentGriefAngle : griefTier * GriefDegreesPerTier;
        griefTier = tier;
        griefAngleFrom = restoring ? tier * GriefDegreesPerTier : angleNow;
        griefAngleStart = griefClock;
        int flat = griefClock - FoldAngles.Length - 1;
        for (int layer = 0; layer < griefFoldAt.Length; layer++)
        {
            int delay = GriefFoldDelay(layer, tier);
            if (delay < 0) griefFoldAt[layer] = int.MaxValue;
            else if (restoring) griefFoldAt[layer] = flat;
            else if (griefFoldAt[layer] == int.MaxValue) griefFoldAt[layer] = griefClock + delay;
        }
        int extras = tier >= 3 ? GriefTier3Delays[GriefRoadExtrasIndex] : -1;
        if (extras < 0) griefRoadExtrasAt = int.MaxValue;
        else if (restoring) griefRoadExtrasAt = flat;
        else if (griefRoadExtrasAt == int.MaxValue) griefRoadExtrasAt = griefClock + extras;
    }

    /// <summary>When (frames after the tier change) a grave layer starts to lie down at this tier, or -1.</summary>
    static int GriefFoldDelay(int layer, int tier)
    {
        if (layer == GraveFarLayer) return tier >= 2 ? 0 : -1;
        if (tier < 3 || layer == GraveRoadLayer) return -1;
        switch (layer)
        {
            case 1: return GriefTier3Delays[0];
            case 2: return GriefTier3Delays[1];
            case 3: return GriefTier3Delays[2];
            case 5: return GriefTier3Delays[4];
        }
        return -1;
    }

    void ResetGraveSection()
    {
        placeFrame = -1;
        griefTier = griefClock = griefAngleFrom = griefAngleStart = 0;
        for (int i = 0; i < griefFoldAt.Length; i++) griefFoldAt[i] = int.MaxValue;
        griefRoadExtrasAt = int.MaxValue;
    }

    Pose GriefLayerPose(int layer)
    {
        if (layer < 0 || layer >= griefFoldAt.Length || griefFoldAt[layer] == int.MaxValue) return Pose.Upright;
        return FoldPose(griefClock - griefFoldAt[layer]);
    }

    /// <summary>Within a family she changes on the spot: no fold, the clocks and the walk keep going.</summary>
    void SwitchWithinFamily(SceneDef next)
    {
        sceneTo = next;
        if (NeedsFlowers(next) && placeFrame < 0) placeFrame = 0;
    }

    static bool NeedsFlowers(SceneDef def) => def.state == GravePlaceFlowers || def.state == OSChapter04PresentationBridge.GraveBreakdown;

    /// <summary>Clocks of the section, advanced with every scene frame.</summary>
    void StepGraveSection()
    {
        rainClock++;
        if (phase != Phase.Scene || !IsGraveFamily(sceneTo)) return;
        if (placeFrame >= 0 && placeFrame < PlaceDoneFrame) placeFrame++;
        if (ShowsGrief) griefClock++;
    }

    /// <summary>A restore builds the placed flowers and the grief pose for the restored state.</summary>
    void RestoreGraveSection(SceneDef def)
    {
        if (!IsGraveFamily(def)) { ResetGraveSection(); return; }
        placeFrame = NeedsFlowers(def) ? PlaceDoneFrame : -1;
        if (def.state == OSChapter04PresentationBridge.GraveBreakdown) SetGriefTier(griefTier, true);
    }

    /// <summary>Per-piece pose in the grave scenes: the placed flowers, and the grief laying things down.</summary>
    Pose GravePiecePose(SceneDef def, int layer, ScenePiece piece, Pose layerPose)
    {
        if (!IsGraveFamily(def)) return layerPose;
        if (piece.anim == PieceAnim.Placed)
        {
            if (placeFrame < 0) return Pose.Flat;
            if (!layerPose.Visible || layerPose.angle != 0 || !Mathf.Approximately(layerPose.scale, 1f)) return layerPose;
            return RisePose(placeFrame - FlowersRiseAt);
        }
        if (layer == GraveRoadLayer && !piece.griefKeep && def == sceneTo && ShowsGrief && griefRoadExtrasAt != int.MaxValue)
            return FoldPose(griefClock - griefRoadExtrasAt);
        return layerPose;
    }

    /// <summary>Her art and pose in the grave scenes while the scene stands (Scene phase).</summary>
    void GravePuppet(SceneDef def, ref string artId, ref int left, ref float sx, ref float sy, ref int lift, ref float degrees)
    {
        left = GravePuppetX;
        int f = phaseFrame;
        bool kneeling = NeedsFlowers(def) && placeFrame >= 0 && placeFrame < PlaceDoneFrame;
        if (kneeling)
        {
            artId = PuppetKneelPlace;
            lift = placeFrame < KneelSinkFrames ? 1 : 2;   // sinks 2 px onto the ground
            return;
        }
        artId = NeedsFlowers(def) ? PuppetIdle : PuppetFlowers;
        if (ShowsGrief && griefTier > 0)
        {
            // Folded back about her feet, trembling: faster and wider with each tier.
            sy = new Pose { angle = GriefAngle, scale = 1f }.Height(PuppetFigureHeight) / (float)PuppetFigureHeight;
            int period = griefTier == 1 ? 6 : griefTier == 2 ? 4 : 2, beat = (griefClock / period) % 4;
            left += beat == 1 ? 1 : beat == 3 ? -1 : 0;
            if (griefTier == 3 && (griefClock / 3) % 5 == 2) lift = 1;
            return;
        }
        int sway = f % SwayEvery - (SwayEvery - SwayHold * RockDegrees.Length);
        if (sway >= 0) degrees = RockDegrees[sway / SwayHold];
    }

    /// <summary>Rain over the whole stage: it does not fold and keeps falling through a scene change.</summary>
    void DrawRain()
    {
        if (!IsRaining) return;
        for (int i = 0; i < RainDrops; i++)
        {
            uint seed = RainHash((uint)(i + 1));
            int x = (int)(seed % (Width + 20)) - 10;
            int y = ((int)((seed >> 8) % (Height + 12)) + rainClock * (2 + i % 3)) % (Height + 12) - 6;
            for (int p = 0; p < RainLength; p++)
            {
                int px = x - p / 2, py = y + p;
                if (px >= 0 && px < Width && py >= 0 && py < Height) canvas[py * Width + px] = OSChapter04TheaterPiece.Gray;
            }
        }
    }

    static uint RainHash(uint value)
    {
        value ^= 2747636419u; value *= 2654435769u; value ^= value >> 16;
        value *= 2654435769u; value ^= value >> 16; value *= 2654435769u;
        return value;
    }
}
