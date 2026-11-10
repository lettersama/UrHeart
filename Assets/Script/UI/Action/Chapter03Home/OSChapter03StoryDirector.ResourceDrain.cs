using System.Collections.Generic;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;

/// <summary>
/// Small heart losses and fault gains while Live grieves (pancake 10-28: "少量多扣", an atmosphere
/// device; the resources carry on to chapter 4's final heart treatment). Each cue plays once per
/// save, after its line has finished typing: a small icon reacts beside Live,
/// while the matching heartbeat slot lights and its value changes.
/// New saves reach the mirror with heart 55-63 and fault 42; the warnings are heart 20 and fault 80.
/// Every cue below adds up to heart -21, fault +12, and chapter 3 never pushes heart under
/// DrainHeartFloor or fault over DrainFaultCeiling whatever the save held.
/// Saves: drain.KEY.heart / drain.KEY.fault when a cue starts, plus .paid when its value has
/// changed. A cue started but unpaid (loaded mid-flight) is paid without its animation.
/// </summary>
public sealed partial class OSChapter03StoryDirector
{
    const int DrainHeartFloor = 30, DrainFaultCeiling = 70;
    const float DrainFaultLag = .35f, DrainSettleSeconds = .5f;

    sealed class DrainCue
    {
        public readonly string key;
        public readonly int heart, fault;
        public readonly string heartFlag, heartPaid, faultFlag, faultPaid;

        public DrainCue(string key, int heart, int fault)
        {
            this.key = key; this.heart = heart; this.fault = fault;
            heartFlag = "drain." + key + ".heart"; heartPaid = heartFlag + ".paid";
            faultFlag = "drain." + key + ".fault"; faultPaid = faultFlag + ".paid";
        }
    }

    // Tune here. Keys are Live lines (after their text finishes) or the failed-typing effect.
    static readonly DrainCue[] DrainCues =
    {
        new DrainCue("live.breakdown.01", -2, 0),
        new DrainCue("live.breakdown.02", -2, 2),
        new DrainCue("live.breakdown.03", -3, 0),
        new DrainCue("live.breakdown.04_yes", 0, 2),
        new DrainCue("live.breakdown.04_no", 0, 2),
        new DrainCue("live.breakdown.05", -2, 0),
        new DrainCue("live.breakdown.06", -3, 2),
        new DrainCue("live.breakdown.07", -4, 3),
        new DrainCue("failed_typing", 0, 3), // the second time "typing..." comes and goes
        // Offline, the sobs that leak through on the way to the lab.
        new DrainCue("live.offline.east", -1, 0),
        new DrainCue("live.offline.diary13", -2, 0),
        new DrainCue("live.offline.bed", -2, 0),
    };

    readonly HashSet<string> drainsInFlight = new HashSet<string>();
    OSResourceStateView[] drainViews;
    float drainSettleClock;
    Vector2 previousDrainSpot;
    int previousDrainSpotFrame = -1;

    void OnDrainLine(string id)
    {
        foreach (var cue in DrainCues)
            if (id == LiveId(cue.key)) { OnDrainCue(cue.key); return; }
    }

    void OnDrainCue(string key)
    {
        if (!IsActive || rebinding || OSSessionPersistence.IsRestoring) return;
        foreach (var cue in DrainCues)
        {
            if (cue.key != key) continue;
            if (cue.heart != 0 && Once(cue.heartFlag)) StartDrain(cue, true, 0f);
            if (cue.fault != 0 && Once(cue.faultFlag)) StartDrain(cue, false, cue.heart != 0 ? DrainFaultLag : 0f);
            return;
        }
    }

    void StartDrain(DrainCue cue, bool heart, float delay)
    {
        string paid = heart ? cue.heartPaid : cue.faultPaid;
        var target = DrainTarget(heart ? ResourceType.Heart : ResourceType.Fault);
        // Nothing to show it on, or nothing left to take: settle at once.
        if (presentation == null || target == null || liveWindow == null || !liveWindow.IsOpen || liveWindow.IsMinimized
            || liveWindow.IsClosing || !liveWindow.gameObject.activeInHierarchy
            || ClipDrain(heart ? ResourceType.Heart : ResourceType.Fault, heart ? cue.heart : cue.fault) == 0
            || liveWindow.WindowRect == null)
        { PayDrain(cue, heart); return; }
        drainsInFlight.Add(paid);
        int started = generation;
        var icon = target.IconRect;
        presentation.PlayResourceDrain(heart, liveWindow.WindowRect, DrainSourceSpot(), icon, delay,
            () => { if (started == generation) PayDrain(cue, heart); },
            () => { if (started == generation) drainsInFlight.Remove(paid); });
    }

    void PayDrain(DrainCue cue, bool heart)
    {
        string paid = heart ? cue.heartPaid : cue.faultPaid;
        drainsInFlight.Remove(paid);
        if (!IsActive || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true || !Once(paid)) return;
        var type = heart ? ResourceType.Heart : ResourceType.Fault;
        int delta = ClipDrain(type, heart ? cue.heart : cue.fault);
        if (delta == 0) return;
        if (heart) os.ChangeHeart(delta); else os.ChangeError(delta);
    }

    // Started cues whose presentation was lost to a load or a reset are paid quietly.
    void TickDrains(float dt)
    {
        drainSettleClock += dt;
        if (drainSettleClock < DrainSettleSeconds || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true) return;
        drainSettleClock = 0f;
        foreach (var cue in DrainCues)
        {
            if (Has(cue.heartFlag) && !Has(cue.heartPaid) && !drainsInFlight.Contains(cue.heartPaid)) PayDrain(cue, true);
            if (Has(cue.faultFlag) && !Has(cue.faultPaid) && !drainsInFlight.Contains(cue.faultPaid)) PayDrain(cue, false);
        }
    }

    static int ClipDrain(ResourceType type, int delta)
    {
        int now = GameResourceSystem.Instance != null ? Mathf.RoundToInt(GameResourceSystem.Instance.Get(type))
            : GameDataManager.Instance != null ? GameDataManager.Instance.GetOSResourceValue(type) : 0;
        return type == ResourceType.Heart ? Mathf.Min(0, Mathf.Max(delta, DrainHeartFloor - now))
            : Mathf.Max(0, Mathf.Min(delta, DrainFaultCeiling - now));
    }

    // The heartbeat panel's slot: the bottom-right-most visible view of that resource.
    OSResourceStateView DrainTarget(ResourceType type)
    {
        if (drainViews == null || drainViews.Length == 0) drainViews = os.GetComponentsInChildren<OSResourceStateView>(true);
        OSResourceStateView best = null; float bestScore = float.NegativeInfinity;
        foreach (var view in drainViews)
        {
            if (view == null || view.ResourceType != type || !view.isActiveAndEnabled) continue;
            Vector3 p = view.IconRect.position;
            if (p.x - p.y > bestScore) { bestScore = p.x - p.y; best = view; }
        }
        return best;
    }

    // Pick a small patch beside her shoulders/chest. Store window-local coordinates
    // so the reaction follows Live's scripted movement instead of being left behind.
    // Two costs on the same line get separate spots; neither one depends on a message.
    Vector2 DrainSourceSpot()
    {
        RectTransform anchor = liveWindow.WindowRect;
        Rect bounds = anchor.rect;
        Vector2 centre = bounds.center + Vector2.down * 10f;
        var character = os.CharacterWindowController;
        var portrait = character != null ? character.Chapter03Portrait : null;
        if (portrait != null && portrait.isActiveAndEnabled
            && character.TryGetChapter03FaceWorld(out Vector3 face))
            centre = anchor.InverseTransformPoint(face);

        float left = bounds.xMin + Mathf.Min(16f, bounds.width * .25f);
        float right = bounds.xMax - Mathf.Min(16f, bounds.width * .25f);
        float bottom = bounds.yMin + Mathf.Min(18f, bounds.height * .25f);
        float top = bounds.yMax - Mathf.Min(32f, bounds.height * .25f);
        Vector2 point = centre;
        bool sameCue = previousDrainSpotFrame == Time.frameCount;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float side = UnityEngine.Random.value < .5f ? -1f : 1f;
            if (attempt == 7 && sameCue) side = previousDrainSpot.x >= centre.x ? -1f : 1f;
            point = centre + new Vector2(side * UnityEngine.Random.Range(25f, 43f), UnityEngine.Random.Range(-22f, 6f));
            point = new Vector2(Mathf.Round(Mathf.Clamp(point.x, left, right)), Mathf.Round(Mathf.Clamp(point.y, bottom, top)));
            if (!sameCue || Vector2.Distance(point, previousDrainSpot) >= 26f) break;
        }
        previousDrainSpot = point; previousDrainSpotFrame = Time.frameCount;
        return point;
    }
}
