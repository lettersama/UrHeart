using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Round 11b, the third part: the player's answers, cut like a drawn sword.
/// Round 11f: a cut no longer bursts the string or shows the line at once. A hit
/// splits the string along the slash, 1-2 px apart, and leaves it gray beside
/// the heart; a miss leaves it pressed on the heart. The closing 「妈妈」 shakes
/// them all (first hit) and knocks them all down (second hit); then the ten
/// green lines rise where their strings first hung, one after another, and
/// stand about 3 s before the ending's black closes over them.
/// The self-blame strings frozen in the air at the end of the second part are
/// the targets; each word beat of the third part is bound to one (BindAnswer).
///   Approach: the beat's note slides in fast (about 0.6 s), and at the same
///        moment its string charges straight at the heart, arriving on the beat.
///   Hit: the layer holds still for 60 ms (a hit stop; the music runs on), a
///        1 px red slash crosses the string for a few frames, the string bursts
///        into gray specks, the heart pulses with a red ring; then the player's
///        line types out where the string used to hang, 30 ms a letter, level,
///        in the player's own green. It stays: the screen fills with green.
///   Miss: the string drives on into the heart and breaks (the heart's miss
///        frame, the red miss vignette); the line still types out where
///        the string hung. No line is ever lost.
///   Hold: the cut comes on the press, the line types out while held; let go
///        early and the rest of it appears at once.
/// The closing beat is Live's own word (SpawnPhrase side 4, light gray); when it
/// is hit every green line fades out over a second.
/// </summary>
public sealed partial class OSChapter04FinalHealPresentation
{
    /// <summary>The player's own color, used only for the player's lines.</summary>
    public static readonly Color32 Green = new Color32(0x7F, 0xB0, 0x69, 255);
    /// <summary>A third-part note's default time across the lane: fast, like the first treatment's Fast notes.</summary>
    public const float AnswerLeadSeconds = .6f;
    const float AnswerLetterSeconds = .03f, AnswerFadeSeconds = 1f, CrashSeconds = .18f;
    const float HitStopSeconds = .06f, SlashSeconds = .07f;
    const int AnswerHeight = 14, HeavyAnswerHeight = 24;

    sealed class Answer
    {
        public string id, pairId, text;
        public int type;
        public double hitTime, holdSeconds;
        public bool placed, shown, holdDriven, waitingCrash, cutting, done;
        public float shownAt, burstAt, holdProgress, fadeAt = -1f;
        public int cx, top, anchorCx, anchorCy, layoutWidth, layoutHeight;
        public int Height => type == TypeHeavy ? HeavyAnswerHeight : AnswerHeight;
    }
    readonly List<Answer> answers = new List<Answer>();
    float edgeFlashAt = -10f;

    sealed class Slash { public int x0, y0, x1, y1; public float at; }
    readonly List<Slash> slashes = new List<Slash>();

    /// <summary>Player lines on screen (shown and not faded) (QA).</summary>
    public int ShownAnswerCount { get { int n = 0; foreach (var a in answers) if (a.shown && a.fadeAt < 0f) n++; return n; } }
    public bool IsAnswerShown(string id) => FindAnswer(id)?.shown == true;
    /// <summary>Letters of the answer typed so far (QA).</summary>
    public int AnswerVisibleLetters(string id) { var a = FindAnswer(id); return a != null ? VisibleLetters(a, PresentationTime) : 0; }
    /// <summary>Where the answer's line sits (center x, top y) (QA).</summary>
    public Vector2Int AnswerPlace(string id) { var a = FindAnswer(id); return a != null ? new Vector2Int(a.cx, a.top) : Vector2Int.zero; }
    public bool IsEdgeFlashing => EdgeFlashIntensity > 0f;
    public int SlashCount => slashes.Count;

    /// <summary>
    /// A third-part word beat: a framed note (id) that crosses the lane in
    /// approachTime, bound to the frozen self-blame string pairId, which charges
    /// the heart over the same time and arrives at hitTime; answered by the
    /// player's line text. type 0 a word, 1 a short sentence (hold, holdSeconds
    /// long), 2 large.
    /// </summary>
    public void BindAnswer(string id, string pairId, string text, int type, double hitTime,
        double approachTime = AnswerLeadSeconds, double holdSeconds = 0d)
    {
        answers.RemoveAll(a => a.id == id);
        AddNote(id, type, hitTime, holdSeconds, true, approachTime);
        var answer = new Answer
        {
            id = id, pairId = pairId, text = text ?? string.Empty, type = Mathf.Clamp(type, TypeTap, TypeHeavy),
            hitTime = hitTime, holdSeconds = holdSeconds
        };
        answers.Add(answer);
        var pair = FindPhrase(pairId);
        if (pair == null) return;
        PlaceAnswer(answer, pair);              // where the string hangs now: the line comes back here
        pair.chargeHit = hitTime;
        pair.chargeApproach = approachTime > 0d ? approachTime : AnswerLeadSeconds;
    }

    /// <summary>Every green line fades out over a second (also done by the closing beat's hit and by PlayEnding).</summary>
    public void FadeAnswers()
    {
        float t = PresentationTime;
        foreach (var answer in answers) if (answer.fadeAt < 0f) answer.fadeAt = t;
    }

    Answer FindAnswer(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = answers.Count - 1; i >= 0; i--) if (answers[i].id == id) return answers[i];
        return null;
    }

    // ---- called from ResolveHit / ResolveMiss / SetHoldProgress

    void AnswerHit(Answer answer)
    {
        if (!answer.done && !answer.cutting) Cut(answer);
        else if (answer.holdSeconds > 0d) { AddRing(Mathf.Lerp(PhraseRing, PhraseRingSunk, sink), RingSeconds, sink >= 1f); PlayHeartHit(); }
    }

    void AnswerHold(Answer answer, float t)
    {
        if (!answer.done && !answer.cutting) Cut(answer);     // the cut comes on the press; the line waits for 「妈妈」
        answer.holdProgress = Mathf.Max(answer.holdProgress, t);
    }

    void AnswerMiss(Answer answer)
    {
        FlashEdge();
        PlayHeartMiss();
        if (answer.done || answer.cutting) return;        // a hold let go early: already cut on the press
        var pair = FindPhrase(answer.pairId);
        if (pair == null) { answer.done = true; return; }
        // the string drives on into the heart and stays pressed on it
        if (!answer.placed) PlaceAnswer(answer, pair);
        pair.crashFrom = pair.DistanceAt(MusicTime, PresentationTime);
        // already at the heart (the usual late miss): it breaks on the next frame
        pair.crashAt = PresentationTime - (pair.crashFrom - pair.arriveDistance < 4f ? CrashSeconds : 0f);
        answer.waitingCrash = true;
    }

    /// <summary>The cut: a hit stop, the red slash across the string, then (after the slash) the burst and the line.</summary>
    void Cut(Answer answer)
    {
        var pair = FindPhrase(answer.pairId);
        if (pair != null && !answer.placed) PlaceAnswer(answer, pair);
        AddRing(Mathf.Lerp(PhraseRing, PhraseRingSunk, sink), RingSeconds, sink >= 1f);
        PlayHeartHit();
        if (pair != null)
        {
            // it stops where the cut caught it, part way to the heart
            pair.frozenDistance = pair.DistanceAt(MusicTime, PresentationTime);
            pair.chargeHit = double.NaN;
        }
        HitStop(HitStopSeconds);
        float t = PresentationTime;
        if (pair != null) AddSlash(pair, t);
        answer.cutting = true;
        // the split shows on the first frame after the hit stop (~0.06 s), on the music's "劈裂"
        answer.burstAt = t + CutSplitDelay;
    }

    /// <summary>The second hit of 「妈妈」: every stopped string drops; the green lines rise, one after another.</summary>
    void ReleaseWrecks()
    {
        float t = PresentationTime;
        AddRing(BurstRadius, BurstSeconds, false);
        double now = MusicTime;
        foreach (var phrase in phrases.ToArray()) if (phrase.Alive && phrase.wreck) Fall(phrase, now, WreckFallSeconds);
        int k = 0;
        foreach (var answer in answers)
        {
            if (answer.shown) continue;
            answer.shown = true; answer.done = true; answer.cutting = answer.waitingCrash = answer.holdDriven = false;
            if (!answer.placed) PlaceAnswerNear(answer, HeartCenterX, 16 + answer.Height / 2);
            // two lines on each music-box note, the second a hair later
            int group = Mathf.Min(k / 2, AnswerRiseTimes.Length - 1);
            answer.shownAt = t + AnswerRiseTimes[group] + (k % 2) * AnswerPairOffset;
            k++;
        }
        answersStandUntil = t + AnswersFadeAt;
    }

    // ---- round 11f timings against the music, in seconds after 「妈妈」's second hit (tunable)
    /// <summary>The ending's black (and with it the green lines' fade) waits until this; then the calm loop.</summary>
    float answersStandUntil = -10f;
    /// <summary>A cut's split shows this long after the hit, i.e. as the 60 ms hit stop ends (the music's "劈裂").</summary>
    const float CutSplitDelay = .001f;
    /// <summary>The stopped strings take about this long to fall off the screen.</summary>
    const float WreckFallSeconds = 2f;
    /// <summary>The music box's five rising notes: two green lines start on each.</summary>
    static readonly float[] AnswerRiseTimes = { 1.2f, 1.5f, 1.8f, 2.25f, 3f };
    const float AnswerPairOffset = .03f;
    /// <summary>The calm loop starts here: the green lines begin to fade into the ending's black.</summary>
    const float AnswersFadeAt = 5.25f;

    /// <summary>The first hit of 「妈妈」: every stopped string jolts.</summary>
    void JoltWrecks()
    {
        foreach (var phrase in phrases) if (phrase.Alive && phrase.wreck) phrase.shakeAt = PresentationTime;
    }

    void Reveal(Answer answer)
    {
        if (!answer.placed) PlaceAnswerNear(answer, HeartCenterX, 16 + answer.Height / 2);
        answer.shown = true;
        answer.cutting = answer.waitingCrash = false;
        answer.shownAt = PresentationTime;
    }

    /// <summary>The line goes where the string hangs: centered on its pixels, kept inside the screen.</summary>
    void PlaceAnswer(Answer answer, Phrase pair)
    {
        Rasterize(pair, pair.frozen ? pair.frozenDistance : pair.Distance(MusicTime), raster);
        if (raster.Count == 0) return;
        long sx = 0, sy = 0;
        foreach (int packed in raster) { sx += packed & 1023; sy += (packed >> 10) & 1023; }
        int cx = (int)(sx / raster.Count), cy = (int)(sy / raster.Count);
        PlaceAnswerNear(answer, cx, cy);
    }

    void PlaceAnswerNear(Answer answer, int cx, int cy)
    {
        answer.anchorCx = cx; answer.anchorCy = cy;
        MeasureAnswer(answer);
        int half = answer.layoutWidth / 2;
        answer.cx = Mathf.Clamp(cx, half + 2, Width - (answer.layoutWidth - half) - 2);
        int top = Mathf.Clamp(cy - answer.Height / 2, 2, Height - answer.layoutHeight - 2);
        // Keep each line near its original string, using its actual TMP box.
        // Search whole pixels so a long translation can use any available row.
        for (int k = 0; k < Height * 2; k++)
        {
            int offset = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);
            int tryTop = top + offset;
            if (tryTop < 2 || tryTop > Height - answer.layoutHeight - 2) continue;
            if (!OverlapsPlaced(answer, answer.cx - half, tryTop, answer.layoutWidth)) { top = tryTop; break; }
        }
        answer.top = top;
        answer.placed = true;
    }

    void ReflowAnswers()
    {
        // Settings can change the chat font while a treatment is paused. Refit
        // all occupied boxes together; updating widths alone leaves stale positions.
        var placed = new List<Answer>();
        foreach (var answer in answers)
        {
            if (answer.placed) placed.Add(answer);
            answer.placed = false;
            answer.layoutWidth = 0;
        }
        foreach (var answer in placed) PlaceAnswerNear(answer, answer.anchorCx, answer.anchorCy);
    }

    bool OverlapsPlaced(Answer self, int left, int top, int width)
    {
        foreach (var other in answers)
        {
            if (other == self || !other.placed || other.fadeAt >= 0f) continue;
            int otherLeft = other.cx - other.layoutWidth / 2;
            bool apartX = left + width + 3 <= otherLeft || otherLeft + other.layoutWidth + 3 <= left;
            bool apartY = top + self.layoutHeight + 3 <= other.top || other.top + other.layoutHeight + 3 <= top;
            if (!apartX && !apartY) return true;
        }
        return false;
    }

    int VisibleLetters(Answer answer, float t)
    {
        if (!answer.shown) return 0;
        int n = answer.text.Length;
        if (answer.holdDriven) return Mathf.Clamp(Mathf.CeilToInt(answer.holdProgress * n), 1, n);
        return Mathf.Clamp(Mathf.FloorToInt((t - answer.shownAt) / AnswerLetterSeconds) + 1, 0, n);
    }

    // ---- the slash: a 1 px red line along the string, a little longer than it

    void AddSlash(Phrase pair, float t)
    {
        Rasterize(pair, pair.DistanceAt(MusicTime, t), raster);
        if (raster.Count == 0) return;
        long sx = 0, sy = 0;
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (int packed in raster)
        {
            int x = packed & 1023, y = (packed >> 10) & 1023;
            sx += x; sy += y;
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
        }
        var center = new Vector2(sx / (float)raster.Count, sy / (float)raster.Count);
        // along the string (level strings: across), tilted a little so it reads as a cut, not an underline
        Vector2 along = pair.Crawl ? Vector2.right : (pair.side == SideRight ? pair.dir : -pair.dir);
        along = Rotate(along, -12f);
        float reach = Mathf.Max(maxX - minX, maxY - minY) * .5f + 10f;
        pair.splitCenter = center; pair.splitNormal = new Vector2(-along.y, along.x);
        slashes.Add(new Slash
        {
            x0 = Mathf.RoundToInt(center.x - along.x * reach), y0 = Mathf.RoundToInt(center.y - along.y * reach),
            x1 = Mathf.RoundToInt(center.x + along.x * reach), y1 = Mathf.RoundToInt(center.y + along.y * reach), at = t
        });
    }

    void DrawSlashes(float t)
    {
        slashes.RemoveAll(s => t - s.at >= SlashSeconds);
        foreach (var s in slashes) Line(s.x0, s.y0, s.x1, s.y1, Red);
    }

    void Line(int x0, int y0, int x1, int y1, Color32 color)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            Put(x0, y0, color);
            if (x0 == x1 && y0 == y1) return;
            int e2 = 2 * error;
            if (e2 >= dy) { error += dy; x0 += sx; }
            if (e2 <= dx) { error += dx; y0 += sy; }
        }
    }

    // ---- per frame

    /// <summary>
    /// Cut strings burst once their slash has shown; strings driving into the
    /// heart after a miss break on arrival. Either way the line then types out.
    /// </summary>
    void StepCrashes(float t)
    {
        foreach (var answer in answers)
        {
            if (!answer.cutting || t < answer.burstAt) continue;
            var pair = FindPhrase(answer.pairId);
            if (pair != null) { pair.wreck = true; pair.split = true; }
            answer.cutting = false; answer.done = true;
        }
        foreach (var phrase in phrases.ToArray())
        {
            if (!phrase.Alive || phrase.crashAt < 0f || t - phrase.crashAt < CrashSeconds) continue;
            phrase.crashAt = -1f;
            phrase.chargeHit = double.NaN;
            phrase.frozenDistance = phrase.arriveDistance;
            phrase.wreck = true;
            foreach (var answer in answers)
                if (answer.waitingCrash && answer.pairId == phrase.id) { answer.waitingCrash = false; answer.done = true; }
        }
    }

    /// <summary>Round 11i: the green lines are TMP labels (SyncAnswerLabels); here only faded ones leave.</summary>
    void DrawAnswers(float t)
    {
        answers.RemoveAll(a => a.fadeAt >= 0f && t - a.fadeAt >= AnswerFadeSeconds);
    }

    // ---- the miss vignette (a horror-game hit): the view narrows to a hollow
    // ellipse, red Bayer dots outside it, densest at the screen edge. It grows in
    // fast (~33 ms) and sinks back slower (~100 ms), a Bayer level per 30 fps
    // step; a miss while it shows continues from the current strength.
    const float VignetteRise = .033f, VignetteFall = .1f, VignetteRadius = .77f;
    /// <summary>The miss vignette's dark red (used for nothing else).</summary>
    public static readonly Color32 MissRed = new Color32(0x8A, 0x23, 0x28, 255);
    float vignetteFrom;

    void FlashEdge()
    {
        vignetteFrom = Mathf.Max(.5f, EdgeFlashIntensity);
        edgeFlashAt = PresentationTime;
    }

    /// <summary>The miss vignette's strength now, 0..1 (QA).</summary>
    public float EdgeFlashIntensity => VignetteAt(PresentationTime);

    float VignetteAt(float t)
    {
        float since = t - edgeFlashAt;
        if (since < 0f || since >= VignetteRise + VignetteFall) return 0f;
        if (since < VignetteRise) return Mathf.Lerp(vignetteFrom, 1f, since / VignetteRise);
        return 1f - (since - VignetteRise) / VignetteFall;
    }

    void DrawEdgeFlash(float t)
    {
        float strength = VignetteAt(t);
        if (strength <= 0f) return;
        // weak: the ellipse lies farther out (coming in, going out); full: 0.77 of the half screen
        float scale = VignetteRadius * (1f + .35f * (1f - strength));
        float cx = Width * .5f, cy = Height * .5f, rx = cx * scale, ry = cy * scale;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float dx = x + .5f - cx, dy = y + .5f - cy;
                float d = Mathf.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
                if (d <= 1f) continue;
                // 0 on the ellipse, 1 where this ray leaves the screen
                float edge = d * Mathf.Min(cx / Mathf.Max(1e-3f, Mathf.Abs(dx)), cy / Mathf.Max(1e-3f, Mathf.Abs(dy)));
                float ramp = edge <= 1f ? 1f : Mathf.Clamp01((d - 1f) / (edge - 1f));
                int level = Mathf.RoundToInt(16f * Mathf.Pow(ramp, .8f) * strength);
                if (Bayer[(y & 3) * 4 + (x & 3)] < level) Put(x, y, MissRed);
            }
    }
}
