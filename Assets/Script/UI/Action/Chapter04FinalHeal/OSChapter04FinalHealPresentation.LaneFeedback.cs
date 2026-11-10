using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Round 11c: the lane's feedback, item by item after the first heart-heal tool
/// (OSHeartHealToolWindowController + OSHeartHealBeatUnit, the prologue's
/// treatment), redrawn in whole pixels on this layer. The old tool's cream is
/// light gray here and its bright "electric" color is red. The old tool itself
/// is not touched; its sounds are played by the same keys through AudioManager.
///   Judgement line pulse (PulseBeatLine): red on a hit or a hold tick, a
///        dotted gray flicker on a miss, 0.18 s.
///   Note hit (PlayHitFeedback): the note flashes red and pops 2 px larger, then
///        a hollow square spreads and dithers away; a word beat's framed note
///        pops 2 px more.
///   Shock burst (PlayShockEffect): a dotted red ring and 7 hollow 3 px squares
///        thrown 9-17 px from the note, 0.24 s; on tap hits and every hold tick.
///   State transfer (PlayStateTransferEffect): on a hit three 2 px red squares
///        fly from the note into the heart (0.42 s); a miss bursts gray squares
///        out of the heart (PlayMissStateBurst).
///   Heal symbols (OSHeartHealHeartPresentation.ShowTreatmentFeedback): small
///        light crosses pop around the heart on a hit.
///   Beat tip (PlayBeatTip): a light "+" rises above the lane on a hit, a red
///        "-" sinks below it on a miss.
///   Miss (PlayMissFeedback / PlayMissShatterFeedback): the note turns red, then
///        breaks into chunks that tumble off.
///   Heavy (PlayReinforcedBreakFeedback): its outer shell flies apart as a wider
///        square.
///   Hold (ReportHoldTick, hold loop): the head sparkles on the judgement line;
///        every 0.25 s held a pulse and a shock burst; the hold loop sound runs
///        while held and its end sound plays on release.
///   Fast (OSHeartHealBeatMotionState.Fast): a note crossing the lane in under a
///        second jumps there in 5 steps, flashing light at each.
///   Heartbeat panel (OS_Handle.PlayInteractionFeedback): highlighted on a hit.
///   Sounds: BeatClick (PlayPress), BeatHit (tap hit), HeartBeat_ForHeal at
///        pitch 1.1 / 0.9 (hit / miss), BeatFail (miss), BeatLoop / BeatLoopEnd
///        (hold). LaneSoundsEnabled turns them off if the backend plays its own.
/// </summary>
public sealed partial class OSChapter04FinalHealPresentation
{
    const float PopSeconds = .2f, ShellSeconds = .25f, ShatterSeconds = .35f, MissFlashSeconds = .07f;
    const float TipSeconds = .45f, JudgePulseSeconds = .18f, ShockSeconds = .24f, TransferSeconds = .42f;
    const float MissBurstSeconds = .3f, CrossSeconds = .3f, HoldTickSeconds = .25f, FastFlashFrames = 2f;
    const int FastSteps = 5;
    const string PressSfx = "BeatClick", TapHitSfx = "BeatHit", BeatSfx = "HeartBeat_ForHeal", FailSfx = "BeatFail";
    const string HoldLoopSfx = "BeatLoop", HoldLoopEndSfx = "BeatLoopEnd";
    const float HitSfxPitch = 1.1f, MissSfxPitch = .9f;

    /// <summary>Play the old tool's lane sounds (off when the backend plays its own).</summary>
    public bool LaneSoundsEnabled { get; set; } = true;

    enum LaneFx { Pop, Shell, Shatter, TipUp, TipDown, Shock, Transfer, MissBurst, Cross }
    sealed class LaneEffect { public LaneFx kind; public int x, x1, y; public float at; public bool framed; public int seed; }
    readonly List<LaneEffect> laneEffects = new List<LaneEffect>();
    float judgePulseAt = -10f;
    bool judgePulseHit;
    readonly Dictionary<string, AudioSource> holdLoops = new Dictionary<string, AudioSource>();
    readonly Dictionary<string, float> holdTicks = new Dictionary<string, float>();
    int fxSeed;

    /// <summary>Lane feedback started by hits and by misses (QA).</summary>
    public int LaneHitEffects { get; private set; }
    public int LaneMissEffects { get; private set; }
    public int HoldTicks { get; private set; }
    public int LaneEffectCount => laneEffects.Count;
    public bool IsJudgeLinePulsing => PresentationTime - judgePulseAt < JudgePulseSeconds;

    void ResetLaneFeedback()
    {
        laneEffects.Clear();
        StopAllHoldLoops();
        judgePulseAt = -10f; pressAt = -10f;
        LaneHitEffects = LaneMissEffects = HoldTicks = PressCount = 0;
    }

    /// <summary>
    /// Resolve one ordinary mother tap while keeping the large phrase separate
    /// from its lane notes. Only the final tap releases the wrecks and green lines.
    /// </summary>
    public bool ResolveMotherTap(string phraseId, string beatId, bool finalTap)
    {
        var note = FindNote(beatId);
        var phrase = FindPhrase(phraseId);
        if (note == null || note.type != TypeTap || phrase == null || phrase.side != SideHeart)
            return false;
        LaneHit(note);
        notes.Remove(note);
        if (finalTap) ResolveHit(phraseId);
        else CrackPhrase(phrase);
        return true;
    }

    /// <summary>
    /// Round 11e: a heavy beat's first hit, as in the first heart-heal tool. The
    /// note's shell breaks away (the tool's reinforced break) and the note stays
    /// on the judgement line; the line pulses and the heartbeat panel lights;
    /// no ring, no heal. Self-blame words jolt and crack; the closing 「妈妈」
    /// sends its first letter into the heart. The second hit is ResolveHit (a
    /// miss, ResolveMiss). False when there is no such heavy beat.
    /// </summary>
    public bool CrackShell(string id)
    {
        var note = FindNote(id);
        if (note == null || note.type != TypeHeavy || note.cracked) return false;
        NoteSpan(note, MusicTime, out int head, out int tail);
        note.cracked = true;
        Add(LaneFx.Shell, head, tail, LaneCenterY, PresentationTime, note.framed);
        PulseJudge(true);
        OS_Handle.Instance?.PlayInteractionFeedback();
        var phrase = FindPhrase(id);
        if (phrase != null) CrackPhrase(phrase);
        return true;
    }

    /// <summary>
    /// A press of the beat key or button, hit or not (round 11d): the judgement
    /// line bumps up 2 px and settles back over three 30 fps steps, light gray on
    /// the first; the heart's idle swells 1 px for one frame; the old tool's
    /// click at half volume. No red, no ring, no numbers: a hit adds its own.
    /// </summary>
    public void PlayPress()
    {
        pressAt = PresentationTime;
        PressCount++;
        HurryEnding();
        EpiloguePress();
        if (LaneSoundsEnabled) AudioManager.Instance?.PlaySfx(PressSfx, .5f, 1f);
    }

    float pressAt = -10f;
    const float PressFrames = 3f;
    /// <summary>Presses shown, and the line's bump this frame in px (QA).</summary>
    public int PressCount { get; private set; }
    public int JudgeBump => PressBump(PresentationTime);
    /// <summary>The heart swells 1 px in the first frame after a press.</summary>
    bool HeartPressSwell(float t) => t - pressAt >= 0f && (t - pressAt) * Fps + 1e-3f < 1f;

    int PressBump(float t)
    {
        float frames = (t - pressAt) * Fps + 1e-3f;      // a step of exactly 1/30 s must count
        if (frames < 0f || frames >= PressFrames) return 0;
        return frames < 1f ? 2 : frames < 2f ? 1 : 0;
    }

    /// <summary>The judgement line raised by the press bump (light gray on the first frame).</summary>
    void DrawPressBump(float t)
    {
        int bump = PressBump(t);
        if (bump == 0) return;
        bool first = (t - pressAt) * Fps < 1f;
        // clear the line where it stood, then draw it raised, red dot and all
        for (int y = LaneInnerTop - 3; y < LaneInnerBottom; y++)
        {
            if (y == LaneInnerTop) continue;                         // the lane's own rule stays
            Put(JudgeX, y, y == LaneCenterY ? Gray : Black);
            if (y <= LaneInnerTop - 2) Put(JudgeX + 1, y, Black);
        }
        for (int y = LaneInnerTop + 1 - bump; y < LaneInnerBottom - bump; y++) Put(JudgeX, y, first ? Light : Gray);
        for (int y = LaneInnerTop - 3 - bump; y <= LaneInnerTop - 2 - bump; y++) { Put(JudgeX, y, Red); Put(JudgeX + 1, y, Red); }
    }

    // ---- from ResolveHit / ResolveMiss / SetHoldProgress (before the note leaves the lane)

    void LaneHit(Note note)
    {
        NoteSpan(note, MusicTime, out int head, out int tail);
        float t = PresentationTime;
        bool tap = note.holdSeconds <= 0d;
        Add(LaneFx.Pop, head, tail, LaneCenterY, t, note.framed);
        if (note.type == TypeHeavy) Add(LaneFx.Shell, head, tail, LaneCenterY, t, note.framed);
        if (tap) Add(LaneFx.Shock, head, head, LaneCenterY, t, false);
        Add(LaneFx.TipUp, head, head, LaneCenterY, t, false);
        for (int k = 0; k < 3; k++) Add(LaneFx.Transfer, head, head, LaneCenterY, t + k * .04f, false);
        Add(LaneFx.Cross, HeartCenterX, HeartCenterX, HeartCenterY, t, false);
        PulseJudge(true);
        StopHoldLoop(note.id, true);
        if (tap) Sfx(TapHitSfx);
        Sfx(BeatSfx, HitSfxPitch);
        OS_Handle.Instance?.PlayInteractionFeedback();
        LaneHitEffects++;
    }

    void LaneMiss(Note note)
    {
        NoteSpan(note, MusicTime, out int head, out int tail);
        float t = PresentationTime;
        Add(LaneFx.Shatter, head, tail, LaneCenterY, t, note.framed);
        Add(LaneFx.TipDown, head, head, LaneCenterY, t, false);
        Add(LaneFx.MissBurst, HeartCenterX, HeartCenterX, HeartCenterY, t, false);
        PulseJudge(false);
        StopHoldLoop(note.id, true);
        Sfx(BeatSfx, MissSfxPitch);
        Sfx(FailSfx);
        LaneMissEffects++;
    }

    void LaneHoldStart(Note note)
    {
        if (holdLoops.ContainsKey(note.id)) return;
        holdTicks[note.id] = PresentationTime;
        holdLoops[note.id] = LaneSoundsEnabled ? AudioManager.Instance?.PlaySfx(HoldLoopSfx, 1f, 1f) : null;
        if (holdLoops[note.id] != null) { holdLoops[note.id].loop = true; if (paused) holdLoops[note.id].Pause(); }
    }

    void StopHoldLoop(string id, bool playEnd)
    {
        holdTicks.Remove(id);
        if (!holdLoops.TryGetValue(id, out var source)) return;
        holdLoops.Remove(id);
        if (source != null) { source.loop = false; source.Stop(); }
        if (playEnd) Sfx(HoldLoopEndSfx);
    }

    /// <summary>Round 11g: held notes' loop sounds pause and resume with SetPaused.</summary>
    void PauseHoldLoops(bool pause)
    {
        foreach (var source in holdLoops.Values)
        {
            if (source == null) continue;
            if (pause) source.Pause(); else source.UnPause();
        }
    }

    void StopAllHoldLoops()
    {
        foreach (var id in new List<string>(holdLoops.Keys)) StopHoldLoop(id, false);
        holdTicks.Clear();
    }

    void Sfx(string key, float pitch = 1f)
    {
        if (!LaneSoundsEnabled || string.IsNullOrEmpty(key)) return;
        AudioManager.Instance?.PlaySfx(key, 1f, pitch);
    }

    void Add(LaneFx kind, int x, int x1, int y, float at, bool framed) =>
        laneEffects.Add(new LaneEffect { kind = kind, x = x, x1 = x1, y = y, at = at, framed = framed, seed = ++fxSeed });

    void PulseJudge(bool hit) { judgePulseAt = PresentationTime; judgePulseHit = hit; }

    /// <summary>Where the note stands on the lane now (head and tail, clamped to the lane window).</summary>
    void NoteSpan(Note note, double now, out int head, out int tail)
    {
        head = note.holding || note.cracked ? JudgeX : NoteHead(note, now);
        tail = note.holdSeconds > 0d ? Mathf.Max(head, LaneX(note.hitTime + note.holdSeconds, now, note.lead)) : head;
        head = Mathf.Clamp(head, NoteClipLeft + NoteHalf, NoteClipRight - NoteHalf);
        tail = Mathf.Clamp(tail, head, NoteClipRight - NoteHalf);
    }

    /// <summary>A fast note (under a second across) jumps in 5 steps, like the old tool's Fast beats.</summary>
    static bool IsFast(Note note) => note.lead < 1d;

    int NoteHead(Note note, double now)
    {
        if (!IsFast(note) || now >= note.hitTime) return LaneX(note.hitTime, now, note.lead);
        float p = Mathf.Clamp01((float)(1d - (note.hitTime - now) / note.lead));
        float step = Mathf.Floor(p * (FastSteps - 1)) / (FastSteps - 1);
        return Mathf.RoundToInt(Mathf.Lerp(NoteClipRight - NoteHalf, JudgeX, step));
    }

    /// <summary>The first frames after each jump of a fast note show it flashing light.</summary>
    bool FastFlash(Note note, double now)
    {
        if (!IsFast(note) || now >= note.hitTime) return false;
        float p = Mathf.Clamp01((float)(1d - (note.hitTime - now) / note.lead)) * (FastSteps - 1);
        float sinceJump = (p - Mathf.Floor(p)) * (float)note.lead / (FastSteps - 1);
        return sinceJump < FastFlashFrames / Fps;
    }

    // ---- per frame

    /// <summary>Held notes tick: every 0.25 s a judgement pulse and a shock burst at the head.</summary>
    void StepHoldTicks(float t)
    {
        foreach (var note in notes)
        {
            if (!note.holding || note.state != NoteState.Waiting || !holdTicks.TryGetValue(note.id, out float last)) continue;
            if (t - last < HoldTickSeconds) continue;
            holdTicks[note.id] = t;
            Add(LaneFx.Shock, JudgeX, JudgeX, LaneCenterY, t, false);
            PulseJudge(true);
            HoldTicks++;
        }
    }

    void DrawJudgePulse(float t)
    {
        float e = (t - judgePulseAt) / JudgePulseSeconds;
        if (e < 0f || e >= 1f) return;
        for (int y = LaneInnerTop + 1; y < LaneInnerBottom; y++)
        {
            if (judgePulseHit)
            {
                // a red "current": the line and, early on, its neighbours flicker red
                Put(JudgeX, y, Red);
                if (e < .5f && ((y + Mathf.FloorToInt(t * Fps)) & 1) == 0) { Put(JudgeX - 1, y, Red); Put(JudgeX + 1, y, Red); }
            }
            else if (Bayer[(y & 3) * 4 + (JudgeX & 3)] < 8 || Mathf.FloorToInt(t * Fps) % 2 == 0) Put(JudgeX, y, Gray);
            else Put(JudgeX, y, Black);
        }
    }

    void DrawLaneFeedback(float t)
    {
        StepHoldTicks(t);
        DrawPressBump(t);
        DrawJudgePulse(t);
        DrawHoldSparkle(t);
        laneEffects.RemoveAll(fx => t - fx.at >= Duration(fx.kind));
        int top = LaneCenterY - NoteHalf, bottom = LaneCenterY + NoteHalf;
        foreach (var fx in laneEffects)
        {
            if (t < fx.at) continue;
            float e = Mathf.Clamp01((t - fx.at) / Duration(fx.kind));
            int l = fx.x - NoteHalf, r = fx.x1 + NoteHalf;
            switch (fx.kind)
            {
                case LaneFx.Pop:
                {
                    int extra = fx.framed ? 2 : 0;           // word beats pop a little bigger
                    int frame = Mathf.FloorToInt(e * 6f);
                    if (frame == 0) FxRect(l - 2 - extra, r + 2 + extra, top - 2 - extra, bottom + 2 + extra, Red, 16, true);
                    else if (frame == 1)
                    {
                        FxRect(l - 1 - extra, r + 1 + extra, top - 1 - extra, bottom + 1 + extra, Light, 16, true);
                        FxRect(l + 2, r - 2, top + 2, bottom - 2, Red, 16, false);
                    }
                    else FxFrame(l - frame - 1 - extra, r + frame + 1 + extra, top - frame - 1 - extra, bottom + frame + 1 + extra, Light, frame >= 4 ? 6 : 12);
                    break;
                }
                case LaneFx.Shell:
                {
                    int grow = 3 + Mathf.RoundToInt(e * 8f);
                    FxFrame(l - grow, r + grow, top - grow, bottom + grow, Light, Mathf.RoundToInt(16f * (1f - e)));
                    FxFrame(l - grow - 2, r + grow + 2, top - grow - 2, bottom + grow + 2, Gray, Mathf.RoundToInt(8f * (1f - e)));
                    break;
                }
                case LaneFx.Shatter:
                {
                    if (t - fx.at < MissFlashSeconds) { FxRect(l, r, top, bottom, Red, 16, true); break; }
                    // chunks of up to 6 px tumble apart and drop
                    float f = (t - fx.at - MissFlashSeconds) / (ShatterSeconds - MissFlashSeconds);
                    int keep = f > .5f ? 8 : 16;
                    int chunk = 0;
                    for (int cx = l; cx <= r; cx += 6, chunk++)
                        for (int half = 0; half < 2; half++)
                        {
                            int dir = (chunk + half) % 2 == 0 ? -1 : 1;
                            int dx = Mathf.RoundToInt(dir * (4f + (chunk % 3) * 2f) * f * 2f);
                            int dy = Mathf.RoundToInt(-6f * f + 40f * f * f) + (half == 0 ? 0 : 1);
                            int y0 = half == 0 ? top : LaneCenterY + 1, y1 = half == 0 ? LaneCenterY : bottom;
                            FxRect(cx + dx, Mathf.Min(cx + 5, r) + dx, y0 + dy, y1 + dy, Gray, keep, false);
                        }
                    break;
                }
                case LaneFx.TipUp:
                {
                    int y = LaneInnerTop - 7 - Mathf.RoundToInt(e * 10f);
                    int keep = e < .6f ? 16 : Mathf.RoundToInt(16f * (1f - e) / .4f);
                    for (int k = -2; k <= 2; k++) { FxDot(fx.x + k, y, Light, keep); FxDot(fx.x, y + k, Light, keep); }
                    break;
                }
                case LaneFx.TipDown:
                {
                    int y = LaneBottom + 4 + Mathf.RoundToInt(e * 8f);
                    int keep = e < .6f ? 16 : Mathf.RoundToInt(16f * (1f - e) / .4f);
                    for (int k = -2; k <= 2; k++) FxDot(fx.x + k, y, Red, keep);
                    break;
                }
                case LaneFx.Shock:
                {
                    // a dotted red glow ring and hollow squares thrown outward, shrinking
                    int ring = 3 + Mathf.RoundToInt(e * 6f);
                    if (e < .6f) Circle(fx.x, fx.y, ring, Red, true);
                    int keep = Mathf.RoundToInt(16f * (1f - e * e));
                    var random = new System.Random(fx.seed);
                    for (int i = 0; i < 7; i++)
                    {
                        float angle = (360f * i / 7f + (float)random.NextDouble() * 24f - 12f) * Mathf.Deg2Rad;
                        float reach = 9f + (float)random.NextDouble() * 8f;
                        float d = reach * (1f - (1f - e) * (1f - e) * (1f - e));
                        int px = fx.x + Mathf.RoundToInt(Mathf.Cos(angle) * d), py = fx.y + Mathf.RoundToInt(Mathf.Sin(angle) * d);
                        if (e < .55f) FxFrameFree(px - 1, px + 1, py - 1, py + 1, Light, keep);
                        else FxDotFree(px, py, Light, keep);
                    }
                    break;
                }
                case LaneFx.Transfer:
                {
                    // into the heart along a gentle arc
                    float g = 1f - (1f - e) * (1f - e);
                    var from = new Vector2(fx.x, fx.y - 8);
                    var to = new Vector2(HeartCenterX, HeartCenterY);
                    var control = new Vector2((from.x + to.x) * .5f + ((fx.seed % 3) - 1) * 30f, from.y - 40f);
                    Vector2 at = (1f - g) * (1f - g) * from + 2f * (1f - g) * g * control + g * g * to;
                    int sx = Mathf.RoundToInt(at.x), sy = Mathf.RoundToInt(at.y);
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) Put(sx + dx, sy + dy, Red);
                    break;
                }
                case LaneFx.MissBurst:
                {
                    var random = new System.Random(fx.seed);
                    int keep = Mathf.RoundToInt(16f * (1f - e));
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = (360f * i / 8f + (float)random.NextDouble() * 30f) * Mathf.Deg2Rad;
                        float d = 20f + e * (30f + (float)random.NextDouble() * 20f);
                        int px = fx.x + Mathf.RoundToInt(Mathf.Cos(angle) * d), py = fx.y + Mathf.RoundToInt(Mathf.Sin(angle) * d);
                        for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) FxDotFree(px + dx, py + dy, Gray, keep);
                    }
                    break;
                }
                case LaneFx.Cross:
                {
                    // a few light crosses pop up around the heart, rise a little and dither out
                    var random = new System.Random(fx.seed);
                    int keep = e < .5f ? 16 : Mathf.RoundToInt(16f * (1f - e) / .5f);
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                        int px = fx.x + Mathf.RoundToInt(Mathf.Cos(angle) * 62f);
                        int py = fx.y + Mathf.RoundToInt(Mathf.Sin(angle) * 74f) - Mathf.RoundToInt(e * 6f);
                        for (int k = -3; k <= 3; k++)
                        {
                            FxDotFree(px + k, py, Light, keep); FxDotFree(px, py + k, Light, keep);
                            if (k > -3 && k < 3) { FxDotFree(px + k, py + (k == 0 ? 0 : 0), Light, keep); }
                        }
                        FxDotFree(px - 1, py - 1, Black, keep); FxDotFree(px + 1, py + 1, Black, keep);
                        FxDotFree(px - 1, py + 1, Black, keep); FxDotFree(px + 1, py - 1, Black, keep);
                    }
                    break;
                }
            }
        }
    }

    static float Duration(LaneFx kind) => kind switch
    {
        LaneFx.Pop => PopSeconds, LaneFx.Shell => ShellSeconds, LaneFx.Shatter => ShatterSeconds,
        LaneFx.Shock => ShockSeconds, LaneFx.Transfer => TransferSeconds, LaneFx.MissBurst => MissBurstSeconds,
        LaneFx.Cross => CrossSeconds, _ => TipSeconds
    };

    /// <summary>The held note's head sparkles on the judgement line: a red core every other frame.</summary>
    void DrawHoldSparkle(float t)
    {
        bool on = Mathf.FloorToInt(t * Fps) % 2 == 0;
        foreach (var note in notes)
        {
            if (!note.holding || note.state != NoteState.Waiting) continue;
            int top = LaneCenterY - NoteHalf, bottom = LaneCenterY + NoteHalf;
            if (on) FxRect(JudgeX - NoteHalf + 2, JudgeX + NoteHalf - 2, top + 2, bottom - 2, Red, 16, false);
            else FxFrame(JudgeX - NoteHalf - 1, JudgeX + NoteHalf + 1, top - 1, bottom + 1, Light, 8);
        }
    }

    void FxRect(int left, int right, int top, int bottom, Color32 color, int keep, bool roundCorners)
    {
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                if (roundCorners && (x == left || x == right) && (y == top || y == bottom)) continue;
                FxDot(x, y, color, keep);
            }
    }

    void FxFrame(int left, int right, int top, int bottom, Color32 color, int keep)
    {
        for (int x = left + 1; x < right; x++) { FxDot(x, top, color, keep); FxDot(x, bottom, color, keep); }
        for (int y = top + 1; y < bottom; y++) { FxDot(left, y, color, keep); FxDot(right, y, color, keep); }
    }

    void FxFrameFree(int left, int right, int top, int bottom, Color32 color, int keep)
    {
        for (int x = left; x <= right; x++) { FxDotFree(x, top, color, keep); FxDotFree(x, bottom, color, keep); }
        for (int y = top + 1; y < bottom; y++) { FxDotFree(left, y, color, keep); FxDotFree(right, y, color, keep); }
    }

    /// <summary>A lane effect dot: kept over the lane's width.</summary>
    void FxDot(int x, int y, Color32 color, int keep)
    {
        if (x < LaneLeft + 1 || x > LaneRight - 1) return;
        FxDotFree(x, y, color, keep);
    }

    void FxDotFree(int x, int y, Color32 color, int keep)
    {
        if (keep <= 0 || keep < 16 && Bayer[(y & 3) * 4 + (x & 3)] >= keep) return;
        Put(x, y, color);
    }
}
