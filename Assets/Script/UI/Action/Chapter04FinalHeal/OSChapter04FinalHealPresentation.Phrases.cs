using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Round 11: the words that fly at the heart, and their letters afterwards.
///
/// A string lies along its flight in perspective: small at the heart's end
/// (14 px), large at the far end (28 px; heavy beats twice that and 1 px
/// bolder). From the right the first letter is nearest the heart; from the left
/// the first letter is the far one, so nothing reads backwards. From the top,
/// the bottom and under the heart the line stays level and reads left to right,
/// narrowing toward the heart. Strings from the same side take turns at angles.
///
/// Letters are rasterized on the CPU from the chat font's own signed distance
/// field (the TMP atlas): every screen pixel is mapped back through the
/// perspective into the glyph and thresholded, so the result is 1-bit at any
/// size and tilt with no smoothing.
/// </summary>
public sealed partial class OSChapter04FinalHealPresentation
{
    const float NearHeight = 14f, HeavyScale = 2f, Ln2 = .6931472f;
    const float ArriveDistance = 30f, CrawlArriveDistance = 26f, HeartRiseStart = 96f, HeartRiseArrive = 18f;
    const float LetterInterval = .04f, FallIntervalMin = .03f, FallIntervalMax = .06f, FallGravity = 900f, HeavyFallGravity = 1500f, FlySeconds = .38f;
    const float SpeckGravity = 240f, SpeckLifeMin = .3f, SpeckLifeMax = .6f;
    static readonly float[] LineAngles = { 0f, -18f, 18f, -32f, 32f, -9f, 9f };
    static readonly float[] CrawlAngles = { 0f, -20f, 20f, -34f, 34f };

    // ================================================================ API

    /// <summary>
    /// Words flying at the heart with their framed note in the lane. type 0 a
    /// word (tap), 1 a short sentence (hold, holdSeconds long), 2 large bold
    /// (heavy). side 0 left, 1 right, 2 top, 3 bottom (the player's words), 4
    /// rising from under the heart (D2). The string leaves the screen edge at
    /// hitTime - approachTime and reaches the heart at hitTime. An id still on
    /// screen is replaced.
    /// </summary>
    public void SpawnPhrase(string id, string text, int type, double hitTime, double approachTime, int side, double holdSeconds = 0d)
    {
        RemovePhrase(id);
        AddNote(id, type, hitTime, holdSeconds, true);
        side = Mathf.Clamp(side, SideLeft, SideHeart);
        var phrase = new Phrase
        {
            id = id, text = text ?? string.Empty, type = Mathf.Clamp(type, TypeTap, TypeHeavy), side = side,
            hitTime = hitTime, approach = Math.Max(0d, approachTime)
        };
        Layout(phrase);
        int slot = sideSlots[side]++;
        float angle = side >= SideTop ? CrawlAngles[slot % CrawlAngles.Length] : LineAngles[slot % LineAngles.Length];
        if (side == SideHeart) angle = 0f;
        Vector2 outward = side == SideLeft ? Vector2.left : side == SideRight ? Vector2.right : side == SideTop ? Vector2.down : Vector2.up;
        phrase.dir = Rotate(outward, angle);           // screen y points down: Vector2.up is toward the bottom edge
        if (side == SideHeart) { phrase.startDistance = HeartRiseStart; phrase.arriveDistance = HeartRiseArrive; }
        else
        {
            phrase.arriveDistance = phrase.Crawl ? CrawlArriveDistance : ArriveDistance;
            FitArrivalDirection(phrase);
            phrase.startDistance = EdgeDistance(phrase.dir) + 2f;
        }
        phrases.Add(phrase);
    }

    /// <summary>
    /// Called just after spawning a frozen target. If its full word would still
    /// cross an edge at atTime, bring it inward sooner along the same ray. Only
    /// the visual approach changes; its spawn time, font and lane notes stay.
    /// </summary>
    public bool FitPhraseAtTime(string id, double atTime)
    {
        var phrase = FindPhrase(id);
        if (phrase == null || phrase.frozen || phrase.approach <= 0d || double.IsNaN(atTime) || double.IsInfinity(atTime)) return false;
        double spawn = phrase.hitTime - phrase.approach;
        if (atTime <= spawn) return false;
        float distance = phrase.Distance(atTime);
        var bounds = PhraseBounds(phrase, distance);
        if (FitsViewport(bounds)) return false;
        float safe = distance;
        if (phrase.dir.x > 1e-4f) safe = Mathf.Min(safe, distance + (Width - PhraseEdgePadding - bounds.xMax) / phrase.dir.x);
        else if (phrase.dir.x < -1e-4f) safe = Mathf.Min(safe, distance + (PhraseEdgePadding - bounds.xMin) / phrase.dir.x);
        if (phrase.dir.y > 1e-4f) safe = Mathf.Min(safe, distance + (Height - PhraseEdgePadding - bounds.yMax) / phrase.dir.y);
        else if (phrase.dir.y < -1e-4f) safe = Mathf.Min(safe, distance + (PhraseEdgePadding - bounds.yMin) / phrase.dir.y);
        safe = Mathf.Max(phrase.arriveDistance, safe);
        if (safe >= distance - .001f || !FitsViewport(PhraseBounds(phrase, safe))) return false;
        float travel = phrase.startDistance - phrase.arriveDistance;
        if (travel <= .001f) return false;
        float progress = Mathf.Clamp01((phrase.startDistance - safe) / travel);
        if (progress <= 0f) return false;
        phrase.approach = (atTime - spawn) / progress;
        phrase.hitTime = spawn + phrase.approach;
        return true;
    }

    /// <summary>
    /// Knock a hanging self-blame string down: its letters turn gray and fall
    /// in a random order. False when no
    /// such string is up.
    /// </summary>
    public bool KnockDown(string pairId)
    {
        var phrase = FindPhrase(pairId);
        if (phrase == null) return false;
        Fall(phrase, MusicTime);
        knockedDown.Add(pairId);
        return true;
    }

    // ================================================================ QA readouts

    readonly List<string> knockedDown = new List<string>();
    public IReadOnlyList<string> KnockedDown => knockedDown;
    public int FallingLetters { get; private set; }
    public int FallenOffscreen { get; private set; }
    public int FlownIntoHeart { get; private set; }
    public int SpecksMade { get; private set; }
    public int ParticleCount => particles.Count;

    void ResetPhraseCounters()
    {
        knockedDown.Clear();
        Array.Clear(sideSlots, 0, sideSlots.Length);
        FallingLetters = FallenOffscreen = FlownIntoHeart = SpecksMade = 0;
    }
    /// <summary>Strings on screen (flying or hanging), and of them, hanging.</summary>
    public int LivePhraseCount { get { int n = 0; foreach (var p in phrases) if (p.Alive) n++; return n; } }
    public int FrozenPhraseCount { get { int n = 0; foreach (var p in phrases) if (p.Alive && p.frozen) n++; return n; } }
    public bool IsPhraseAlive(string id) => FindPhrase(id) != null;
    public bool IsPhraseFrozen(string id) => FindPhrase(id)?.frozen == true;
    /// <summary>A third-part target stopped by its cut or its miss (QA).</summary>
    public bool IsPhraseWreck(string id) => FindPhrase(id)?.wreck == true;
    public int WreckCount { get { int n = 0; foreach (var p in phrases) if (p.Alive && p.wreck) n++; return n; } }

    /// <summary>
    /// Pixel heights of a live string's first and last letter as drawn last
    /// (QA: the end nearer the heart is the smaller). Zero when not drawn.
    /// </summary>
    public Vector2Int PhraseEndHeights(string id)
    {
        var phrase = FindPhrase(id);
        return phrase != null ? new Vector2Int(phrase.firstHeight, phrase.lastHeight) : Vector2Int.zero;
    }

    /// <summary>Pixels the string covered when drawn last (QA).</summary>
    public int PhrasePixels(string id) => FindPhrase(id)?.pixels ?? 0;

    // ================================================================ strings

    sealed class Phrase
    {
        public string id, text;
        public int type, side;
        public double hitTime, approach;
        public Glyph[] glyphs;
        public float[] starts;          // letter starts along the line, in font line units
        public float length;
        public bool[] gone;             // letters already turned into particles
        public int[] eatOrder;          // a hold eats self-blame letters in this order
        public int eaten;
        public Vector2 dir;             // outward from the heart, screen coordinates (y down)
        public float startDistance, arriveDistance, frozenDistance;
        public float crashAt = -1f, crashFrom;   // a missed answer: the string drives into the heart (presentation time)
        public double chargeHit = double.NaN, chargeApproach;   // the third part: the frozen string charges the heart (music time)
        public bool frozen, resolved;
        public float shakeAt = -10f;    // round 11e: a heavy word's first hit jolts it
        public bool wreck, split;       // round 11f: cut (split along the slash) or missed, stopped by the heart, gray
        public Vector2 splitCenter, splitNormal;
        public int crackSeed;           // and cracks it (some letter pixels go black)
        public int firstHeight, lastHeight, pixels;

        public bool Alive => !resolved;
        public bool Player => side == SideBottom || side == SideHeart;
        public bool Crawl => side >= SideTop;
        public float Near => (type == TypeHeavy ? NearHeight * HeavyScale : NearHeight) * nearScale;
        // round 11k: far/near size ratio and the near size scale that keep a long string on screen
        public float ratio = 2f, nearScale = 1f;
        // spaced scripts (Latin, Cyrillic...) lie word by word: each word one size, gaps between
        public bool wordMode;
        public int[] groupFirst;        // each letter's group (a word, a 3-letter piece of a long word, or itself)
        public float[] wordU0, wordWidth, wordR0, wordH;
        public float wordReach;
        public bool Started(double now) => frozen || now >= hitTime - approach;
        public float Progress(double now) => approach <= 0d ? 1f : Mathf.Clamp01((float)((now - (hitTime - approach)) / approach));
        public float Distance(double now) => frozen ? frozenDistance : Mathf.Lerp(startDistance, arriveDistance, Progress(now));
        public float DistanceAt(double now, float t)
        {
            if (crashAt >= 0f) return Mathf.Lerp(crashFrom, arriveDistance, Mathf.Clamp01((t - crashAt) / CrashSeconds));
            if (!double.IsNaN(chargeHit) && chargeApproach > 0d && now >= chargeHit - chargeApproach)
            {
                // straight at the heart, faster and faster, arriving on the beat
                float f = Mathf.Clamp01((float)((now - (chargeHit - chargeApproach)) / chargeApproach));
                return Mathf.Lerp(frozenDistance, arriveDistance, f * f);
            }
            return Distance(now);
        }
        public int Count => glyphs.Length;
        /// <summary>The letter at the given place counted from the heart's end.</summary>
        public int FromHeart(int k) => side == SideLeft ? Count - 1 - k : k;
    }

    readonly List<Phrase> phrases = new List<Phrase>();
    readonly int[] sideSlots = new int[5];

    Phrase FindPhrase(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = phrases.Count - 1; i >= 0; i--) if (phrases[i].id == id && phrases[i].Alive) return phrases[i];
        return null;
    }

    void RemovePhrase(string id) { if (!string.IsNullOrEmpty(id)) phrases.RemoveAll(p => p.id == id); }

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    /// <summary>How far from the heart's center the screen edge is along dir.</summary>
    static float EdgeDistance(Vector2 dir)
    {
        float best = float.MaxValue;
        if (dir.x > 1e-4f) best = Mathf.Min(best, (Width - HeartCenterX) / dir.x);
        if (dir.x < -1e-4f) best = Mathf.Min(best, -HeartCenterX / dir.x);
        if (dir.y > 1e-4f) best = Mathf.Min(best, (Height - HeartCenterY) / dir.y);
        if (dir.y < -1e-4f) best = Mathf.Min(best, -HeartCenterY / dir.y);
        return best;
    }

    const float PhraseEdgePadding = 2f;

    static bool FitsViewport(Rect bounds) => bounds.xMin >= PhraseEdgePadding - .001f
        && bounds.yMin >= PhraseEdgePadding - .001f && bounds.xMax <= Width - PhraseEdgePadding + .001f
        && bounds.yMax <= Height - PhraseEdgePadding + .001f;

    // Keep the authored angle unless a long word is clipped even at the heart.
    // Moving only toward horizontal retains its size and arrival distance.
    static void FitArrivalDirection(Phrase phrase)
    {
        if (phrase.Crawl || FitsViewport(PhraseBounds(phrase, phrase.arriveDistance))) return;
        Vector2 original = phrase.dir, horizontal = phrase.side == SideLeft ? Vector2.left : Vector2.right;
        phrase.dir = horizontal;
        if (!FitsViewport(PhraseBounds(phrase, phrase.arriveDistance))) { phrase.dir = original; return; }
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 12; i++)
        {
            float mid = (lo + hi) * .5f;
            phrase.dir = Vector2.Lerp(original, horizontal, mid).normalized;
            if (FitsViewport(PhraseBounds(phrase, phrase.arriveDistance))) hi = mid; else lo = mid;
        }
        phrase.dir = Vector2.Lerp(original, horizontal, hi).normalized;
    }

    // The unclipped drawing envelope. Placement and Rasterize share this so
    // fitting cannot silently use a narrower approximation than rendering.
    static Rect PhraseBounds(Phrase phrase, float distance)
    {
        Vector2 near = new Vector2(HeartCenterX, HeartCenterY) + phrase.dir * distance;
        float h = phrase.Near, farHalf = h * phrase.ratio * .5f;
        if (phrase.Crawl)
        {
            float half = phrase.length * farHalf, rows = (1f + phrase.ratio) * .5f * h;
            bool down = phrase.side != SideTop;
            return Rect.MinMaxRect(near.x - half, down ? near.y : near.y - rows,
                near.x + half, down ? near.y + rows : near.y);
        }
        float reach = phrase.wordMode ? phrase.wordReach : h * (phrase.ratio - 1f) / (Mathf.Log(phrase.ratio) / phrase.length);
        Vector2 axis = phrase.side == SideRight ? phrase.dir : -phrase.dir, up = new Vector2(axis.y, -axis.x);
        Vector2 far = near + phrase.dir * reach;
        float nearHalf = phrase.wordMode ? farHalf : h * .5f;
        Vector2 a = near + up * nearHalf, b = near - up * nearHalf, c = far + up * farHalf, d = far - up * farHalf;
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    void Layout(Phrase phrase)
    {
        EnsureFont();
        string text = phrase.text;
        phrase.glyphs = new Glyph[text.Length];
        phrase.starts = new float[text.Length];
        phrase.gone = new bool[text.Length];
        float at = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            var glyph = GlyphOf(text[i]);
            phrase.glyphs[i] = glyph;
            phrase.starts[i] = at;
            at += glyph.advance / lineUnit;
        }
        phrase.length = Mathf.Max(.25f, at);
        BuildGroups(phrase);
        // shape: a long string keeps less perspective and a smaller near end, so its far end stays on screen
        phrase.ratio = phrase.length > LongStringUnits ? 1.5f : 2f;
        phrase.nearScale = 1f;
        float span = Span(phrase), limit = phrase.Crawl ? MaxCrawlWidth : MaxReach;
        if (span > limit) phrase.nearScale = Mathf.Max(MinNearHeight / phrase.Near, limit / span);
        if (phrase.wordMode && !phrase.Crawl) BuildWords(phrase);
    }

    const float LongStringUnits = 10f, MaxReach = 250f, MaxCrawlWidth = 520f, MinNearHeight = 9f;

    /// <summary>Spaced writing outside the CJK and Hangul blocks lies word by word.</summary>
    static bool UsesWords(string text)
    {
        if (text.IndexOf(' ') < 0) return false;
        foreach (char c in text)
            if (c >= 0x2E80 && c <= 0x9FFF || c >= 0xAC00 && c <= 0xD7AF || c >= 0xF900 && c <= 0xFAFF || c >= 0xFF00 && c <= 0xFFEF) return false;
        return true;
    }

    static void BuildGroups(Phrase phrase)
    {
        string text = phrase.text;
        phrase.groupFirst = new int[text.Length];
        for (int i = 0; i < text.Length; i++) phrase.groupFirst[i] = i;
        phrase.wordMode = UsesWords(text);
        if (!phrase.wordMode) return;
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            int end = i;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            // a word falls as one piece; a long one in pieces of three letters
            int piece = end - i > 6 ? 3 : end - i;
            for (int j = i; j < end; j++) phrase.groupFirst[j] = i + (j - i) / piece * piece;
            i = end;
        }
    }

    /// <summary>How far the string reaches from its near end (line strings) or how wide its far row is (level strings).</summary>
    float Span(Phrase phrase)
    {
        float near = phrase.Near;
        if (phrase.Crawl) return phrase.length * near * phrase.ratio;
        if (phrase.wordMode) { BuildWords(phrase); return phrase.wordReach; }
        return near * (phrase.ratio - 1f) * phrase.length / Mathf.Log(phrase.ratio);
    }

    /// <summary>
    /// Word-by-word perspective: each word takes one size by where its middle
    /// lies between the heart's end (near size) and the far end (near x ratio),
    /// laid in reading order with its own gap after it.
    /// </summary>
    void BuildWords(Phrase phrase)
    {
        string text = phrase.text;
        var u0 = new List<float>(); var width = new List<float>(); var gapAfter = new List<float>();
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            int end = i;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            float start = phrase.starts[i], stop = end < text.Length ? phrase.starts[end] : phrase.length;
            int next = end;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
            u0.Add(start); width.Add(stop - start);
            gapAfter.Add(next < text.Length ? phrase.starts[next] - stop : 0f);
            i = next;
        }
        int count = u0.Count;
        phrase.wordU0 = u0.ToArray(); phrase.wordWidth = width.ToArray();
        phrase.wordR0 = new float[count]; phrase.wordH = new float[count];
        float near = phrase.Near, r = 0f;
        for (int k = 0; k < count; k++)
        {
            float middle = (u0[k] + width[k] * .5f) / phrase.length;
            float fromHeart = phrase.side == SideRight ? middle : 1f - middle;     // the heart's end is the small end
            phrase.wordH[k] = near * Mathf.Pow(phrase.ratio, fromHeart);
            phrase.wordR0[k] = r;
            r += width[k] * phrase.wordH[k] + gapAfter[k] * phrase.wordH[k];
        }
        phrase.wordReach = count > 0 ? phrase.wordR0[count - 1] + width[count - 1] * phrase.wordH[count - 1] : 0f;
    }

    static int WordAt(Phrase phrase, float r)
    {
        for (int k = 0; k < phrase.wordR0.Length; k++)
            if (r >= phrase.wordR0[k] && r < phrase.wordR0[k] + phrase.wordWidth[k] * phrase.wordH[k]) return k;
        return -1;
    }

    /// <summary>A whole group (word piece) leaves together.</summary>
    static void MarkGroupGone(Phrase phrase, int key)
    {
        for (int j = 0; j < phrase.Count; j++) if (phrase.groupFirst[j] == key) phrase.gone[j] = true;
    }

    // ================================================================ drawing strings

    // Rasterized pixels: x | y << 10 | letter << 20.
    readonly List<int> raster = new List<int>();
    byte[] boldScratch = new byte[0];

    void DrawPhrases(double now)
    {
        foreach (var phrase in phrases)
        {
            if (!phrase.Alive || !phrase.Started(now)) continue;
            Rasterize(phrase, phrase.DistanceAt(now, PresentationTime), raster);
            float shaken = (PresentationTime - phrase.shakeAt) * Fps;
            int jolt = shaken >= 0f && shaken < 3f ? (Mathf.FloorToInt(shaken) % 2 == 0 ? 2 : -2) : 0;
            var near = new Vector2(HeartCenterX, HeartCenterY) + phrase.dir * phrase.DistanceAt(now, PresentationTime);
            int nx = Mathf.RoundToInt(near.x), ny = Mathf.RoundToInt(near.y);
            foreach (int packed in raster)
            {
                int x = packed & 1023, y = (packed >> 10) & 1023;
                bool crack = phrase.crackSeed != 0 && Hash3(x - nx, y - ny, phrase.crackSeed) % 5 == 0;
                int ox = 0, oy = 0;
                if (phrase.split)
                {
                    // the two halves of the cut stand 1-2 px apart across the slash
                    float side = (x + .5f - phrase.splitCenter.x) * phrase.splitNormal.x + (y + .5f - phrase.splitCenter.y) * phrase.splitNormal.y;
                    ox = Mathf.RoundToInt(phrase.splitNormal.x * (side >= 0f ? 1.5f : -1.5f));
                    oy = Mathf.RoundToInt(phrase.splitNormal.y * (side >= 0f ? 1.5f : -1.5f));
                }
                Put(x + jolt + ox, y + oy, crack ? Black : phrase.wreck ? Gray : Light);
            }
        }
    }

    /// <summary>The string's pixels at a distance from the heart (letters already gone are skipped).</summary>
    void Rasterize(Phrase phrase, float distance, List<int> output)
    {
        output.Clear();
        if (phrase.Count == 0 || font == null) return;
        var center = new Vector2(HeartCenterX, HeartCenterY);
        Vector2 near = center + phrase.dir * distance;
        float h0 = phrase.Near, length = phrase.length, ratio = phrase.ratio;
        Vector2 axis = Vector2.zero, up = Vector2.zero;
        float k = Mathf.Log(ratio) / length;
        float reach = phrase.wordMode && !phrase.Crawl ? phrase.wordReach : h0 * (ratio - 1f) / k;
        float rows = (1f + ratio) * .5f * h0;
        bool down = phrase.side != SideTop;
        if (!phrase.Crawl)
        {
            axis = phrase.side == SideRight ? phrase.dir : -phrase.dir;     // reading direction
            up = new Vector2(axis.y, -axis.x);                              // the letters' up, screen y down
        }
        var bounds = PhraseBounds(phrase, distance);
        int x0 = Mathf.FloorToInt(bounds.xMin), x1 = Mathf.CeilToInt(bounds.xMax);
        int y0 = Mathf.FloorToInt(bounds.yMin), y1 = Mathf.CeilToInt(bounds.yMax);
        x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0); x1 = Mathf.Min(Width - 1, x1); y1 = Mathf.Min(Height - 1, y1);
        if (x1 < x0 || y1 < y0) { phrase.pixels = 0; return; }
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        if (boldScratch.Length < bw * bh) boldScratch = new byte[bw * bh];
        Array.Clear(boldScratch, 0, bw * bh);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + .5f - near.x, py = y + .5f - near.y;
                float textX, fontY, scale;
                if (!phrase.Crawl)
                {
                    float s = px * phrase.dir.x + py * phrase.dir.y;
                    if (s < 0f || s > reach) continue;
                    if (phrase.wordMode)
                    {
                        // one size per word; the reading order runs from the far end for strings from the left
                        float r = phrase.side == SideRight ? s : reach - s;
                        int word = WordAt(phrase, r);
                        if (word < 0) continue;
                        float wh = phrase.wordH[word];
                        float acrossWord = (px * up.x + py * up.y) / wh;
                        if (acrossWord < -.5f || acrossWord > .5f) continue;
                        textX = phrase.wordU0[word] + (r - phrase.wordR0[word]) / wh;
                        fontY = descentLine + (acrossWord + .5f) * lineUnit;
                        scale = wh;
                    }
                    else
                    {
                        float h = h0 + k * s;
                        float across = (px * up.x + py * up.y) / h;
                        if (across < -.5f || across > .5f) continue;
                        float u = Mathf.Log(1f + k * s / h0) / k;
                        textX = phrase.side == SideRight ? u : length - u;
                        fontY = descentLine + (across + .5f) * lineUnit;
                        scale = h;
                    }
                }
                else
                {
                    float v = (down ? py : -py) / rows;
                    if (v < 0f || v > 1f) continue;
                    float c = h0 * (1f + (ratio - 1f) * v);
                    textX = px / c + length * .5f;
                    if (textX < 0f || textX >= length) continue;
                    fontY = down ? ascentLine - v * lineUnit : descentLine + v * lineUnit;
                    scale = c;
                }
                int letter = LetterAt(phrase, textX);
                if (letter < 0 || phrase.gone[letter]) continue;
                if (Ink(phrase.glyphs[letter], (textX - phrase.starts[letter]) * lineUnit, fontY, scale))
                    boldScratch[(y - y0) * bw + (x - x0)] = (byte)(letter + 1);
            }
        if (phrase.type == TypeHeavy) Embolden(bw, bh);
        int first = phrase.side == SideLeft ? phrase.Count - 1 : 0, last = phrase.side == SideLeft ? 0 : phrase.Count - 1;
        int firstTop = int.MaxValue, firstBottom = -1, lastTop = int.MaxValue, lastBottom = -1;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                int value = boldScratch[y * bw + x];
                if (value == 0) continue;
                int letter = value - 1;
                output.Add((x + x0) | (y + y0) << 10 | letter << 20);
                if (letter == first) { firstTop = Mathf.Min(firstTop, y); firstBottom = Mathf.Max(firstBottom, y); }
                if (letter == last) { lastTop = Mathf.Min(lastTop, y); lastBottom = Mathf.Max(lastBottom, y); }
            }
        // "first" and "last" here are the heart's end and the far end.
        phrase.firstHeight = firstBottom >= 0 ? firstBottom - firstTop + 1 : 0;
        phrase.lastHeight = lastBottom >= 0 ? lastBottom - lastTop + 1 : 0;
        phrase.pixels = output.Count;
    }

    /// <summary>Heavy strings: every letter pixel also inks its right and lower neighbours.</summary>
    void Embolden(int bw, int bh)
    {
        for (int y = bh - 1; y >= 0; y--)
            for (int x = bw - 1; x >= 0; x--)
            {
                byte value = boldScratch[y * bw + x];
                if (value == 0) continue;
                if (x + 1 < bw && boldScratch[y * bw + x + 1] == 0) boldScratch[y * bw + x + 1] = value;
                if (y + 1 < bh && boldScratch[(y + 1) * bw + x] == 0) boldScratch[(y + 1) * bw + x] = value;
            }
    }

    static int LetterAt(Phrase phrase, float textX)
    {
        if (textX < 0f) return -1;
        for (int i = phrase.Count - 1; i >= 0; i--) if (textX >= phrase.starts[i]) return i;
        return -1;
    }

    // ================================================================ hits, misses, holds

    void HitPhrase(Phrase phrase)
    {
        if (phrase.Player) FlyIn(phrase, MusicTime);
        else Fall(phrase, MusicTime);
    }

    /// <summary>Self-blame: in a random order, one letter every 30-60 ms (random) turns gray and drops off the screen.</summary>
    /// <param name="spreadSeconds">when above 0, the letters start spread over about this long (round 11f: the wreckage)</param>
    void Fall(Phrase phrase, double now, float spreadSeconds = 0f)
    {
        var letters = Letters(phrase, now);
        phrase.resolved = true;
        var random = new System.Random(Hash(phrase.id) ^ phrase.Count);
        var order = new List<int>(letters.Keys);
        for (int i = order.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        float start = PresentationTime;
        bool heavy = phrase.type == TypeHeavy;
        float at = start;
        foreach (int letter in order)
        {
            AddFalling(letters[letter], at, heavy, random);
            at += spreadSeconds > 0f
                ? spreadSeconds * .75f / Mathf.Max(1, order.Count) * Mathf.Lerp(.6f, 1.4f, (float)random.NextDouble())
                : Mathf.Lerp(FallIntervalMin, FallIntervalMax, (float)random.NextDouble());
        }
    }

    /// <summary>The player's words: one letter every 40 ms turns red and arcs into the heart's center.</summary>
    void FlyIn(Phrase phrase, double now)
    {
        var letters = Letters(phrase, now);
        phrase.resolved = true;
        float start = PresentationTime;
        int n = 0;
        for (int i = 0; i < phrase.Count; i++)
        {
            if (!letters.TryGetValue(i, out var letter)) continue;
            AddFlying(letter, start + n * LetterInterval, n);
            n++;
        }
    }

    void AddFlying(Particle letter, float start, int turn)
    {
        // Arc: bend sideways from the straight path, alternating, half the way along.
        var to = new Vector2(HeartCenterX, HeartCenterY);
        var from = new Vector2(letter.ax, letter.ay);
        Vector2 path = to - from;
        var bend = new Vector2(-path.y, path.x).normalized * Mathf.Min(40f, path.magnitude * .35f) * (turn % 2 == 0 ? 1f : -1f);
        letter.control = from + path * .5f + bend;
        letter.mode = ParticleMode.Fly; letter.start = start; letter.life = FlySeconds;
        particles.Add(letter);
    }

    /// <summary>
    /// The string breaks into gray specks that scatter: a few on a miss, a
    /// burst (every other pixel, faster) when an answer breaks it.
    /// </summary>
    void ShatterPhrase(Phrase phrase, bool burst = false)
    {
        double now = MusicTime;
        Rasterize(phrase, phrase.Started(now) ? phrase.DistanceAt(now, PresentationTime) : phrase.arriveDistance, raster);
        phrase.resolved = true;
        var random = new System.Random(Hash(phrase.id) * 31 + raster.Count);
        float start = PresentationTime;
        foreach (int packed in raster)
        {
            if (random.Next(burst ? 2 : 5) != 0) continue;
            int x = packed & 1023, y = (packed >> 10) & 1023;
            var away = new Vector2(x - HeartCenterX, y - HeartCenterY).normalized;
            if (burst) away = (away + Random2(random) * .8f).normalized;
            float speed = burst ? 120f + (float)random.NextDouble() * 160f : 60f + (float)random.NextDouble() * 100f;
            particles.Add(new Particle
            {
                mode = ParticleMode.Speck, offsets = new[] { Pack(0, 0) }, ax = x, ay = y, start = start,
                vx = away.x * speed + ((float)random.NextDouble() - .5f) * 40f, vy = away.y * speed - 30f,
                gravity = SpeckGravity, life = Mathf.Lerp(SpeckLifeMin, SpeckLifeMax, (float)random.NextDouble())
            });
            SpecksMade++;
        }
    }

    /// <summary>
    /// A hold being held: as t runs to 1, the letters nearest the heart go one
    /// by one (self-blame drops gray, the player's words fly in red).
    /// </summary>
    void EatPhrase(Phrase phrase, float t)
    {
        int target = Mathf.Min(phrase.Count, Mathf.FloorToInt(t * phrase.Count + 1e-4f));
        if (target <= phrase.eaten) return;
        double now = MusicTime;
        var letters = Letters(phrase, now);
        var random = new System.Random(Hash(phrase.id) + target);
        float start = PresentationTime;
        while (phrase.eaten < target)
        {
            // self-blame is eaten in a random order; the player's words from the heart's end
            int letter = phrase.Player ? phrase.FromHeart(phrase.eaten) : EatOrder(phrase)[phrase.eaten];
            phrase.eaten++;
            letter = phrase.groupFirst[letter];
            if (phrase.gone[letter]) continue;
            MarkGroupGone(phrase, letter);
            if (!letters.TryGetValue(letter, out var particle)) continue;
            if (phrase.Player) AddFlying(particle, start, phrase.eaten);
            else AddFalling(particle, start, phrase.type == TypeHeavy, random);
        }
        if (phrase.eaten >= phrase.Count) phrase.resolved = true;
    }

    /// <summary>The string's letters in a fixed random order (the same for the same string).</summary>
    static int[] EatOrder(Phrase phrase)
    {
        if (phrase.eatOrder != null) return phrase.eatOrder;
        var order = new int[phrase.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        var random = new System.Random(Hash(phrase.id) ^ 0x2f6b);
        for (int i = order.Length - 1; i > 0; i--) { int j = random.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        return phrase.eatOrder = order;
    }

    /// <summary>Each letter still on the string, as a particle of its current pixels.</summary>
    Dictionary<int, Particle> Letters(Phrase phrase, double now)
    {
        var result = new Dictionary<int, Particle>();
        if (!phrase.Alive || phrase.Count == 0) return result;
        Rasterize(phrase, phrase.Started(now) ? phrase.Distance(now) : phrase.startDistance, raster);
        var groups = new Dictionary<int, List<int>>();
        foreach (int packed in raster)
        {
            int letter = phrase.groupFirst[packed >> 20];      // a word (piece) falls as one
            if (!groups.TryGetValue(letter, out var list)) groups[letter] = list = new List<int>();
            list.Add(packed);
        }
        foreach (var pair in groups)
        {
            long sx = 0, sy = 0;
            foreach (int packed in pair.Value) { sx += packed & 1023; sy += (packed >> 10) & 1023; }
            int ax = (int)(sx / pair.Value.Count), ay = (int)(sy / pair.Value.Count);
            var offsets = new int[pair.Value.Count];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = Pack((pair.Value[i] & 1023) - ax, ((pair.Value[i] >> 10) & 1023) - ay);
            result[pair.Key] = new Particle { offsets = offsets, ax = ax, ay = ay };
        }
        return result;
    }

    void AddFalling(Particle letter, float start, bool heavy, System.Random random)
    {
        letter.mode = ParticleMode.Fall;
        letter.start = start;
        letter.vx = ((float)random.NextDouble() - .5f) * (heavy ? 50f : 80f);
        letter.vy = heavy ? 40f : -20f - (float)random.NextDouble() * 30f;     // a small hop, or straight down when heavy
        letter.gravity = heavy ? HeavyFallGravity : FallGravity;
        particles.Add(letter);
        FallingLetters++;
    }

    static int Hash3(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (int)((h ^ (h >> 16)) & 0x7fffffff);
        }
    }

    /// <summary>A heavy word's first hit: self-blame jolts and cracks; the player's (or Live's) word sends one letter in.</summary>
    void CrackPhrase(Phrase phrase)
    {
        if (!phrase.Alive) return;
        if (!phrase.Player)
        {
            phrase.shakeAt = PresentationTime;
            phrase.crackSeed = Hash(phrase.id) | 1;
            return;
        }
        if (phrase.side == SideHeart) JoltWrecks();        // 「妈妈」's first hit shakes the stopped strings
        var letters = Letters(phrase, MusicTime);
        for (int i = 0; i < phrase.Count; i++)
        {
            if (phrase.gone[i] || !letters.TryGetValue(i, out var letter)) continue;
            MarkGroupGone(phrase, i);
            AddFlying(letter, PresentationTime, 0);
            break;
        }
        bool left = false;
        for (int i = 0; i < phrase.Count; i++) if (!phrase.gone[i]) left = true;
        if (!left) phrase.resolved = true;
    }

    static Vector2 Random2(System.Random random) =>
        new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);

    static int Hash(string text)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in text ?? string.Empty) hash = (hash ^ c) * 16777619;
            return hash & 0x7fffffff;
        }
    }

    // ================================================================ particles

    enum ParticleMode { Fall, Fly, Speck }
    sealed class Particle
    {
        public ParticleMode mode;
        public int[] offsets;           // packed (dx, dy) around the anchor
        public float ax, ay, vx, vy, gravity, start, life;
        public Vector2 control;
    }
    readonly List<Particle> particles = new List<Particle>();

    static int Pack(int dx, int dy) => (dx + 512) | (dy + 512) << 10;
    static int DX(int packed) => (packed & 1023) - 512;
    static int DY(int packed) => ((packed >> 10) & 1023) - 512;

    void DrawParticles(float t)
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            float e = t - p.start;
            if (e < 0f)
            {
                // waiting its turn: still the light letter where it was
                DrawOffsets(p, Mathf.RoundToInt(p.ax), Mathf.RoundToInt(p.ay), 1f, Light);
                continue;
            }
            switch (p.mode)
            {
                case ParticleMode.Fall:
                {
                    int x = Mathf.RoundToInt(p.ax + p.vx * e), y = Mathf.RoundToInt(p.ay + p.vy * e + .5f * p.gravity * e * e);
                    if (Offscreen(p, x, y)) { particles.RemoveAt(i); FallenOffscreen++; continue; }
                    DrawOffsets(p, x, y, 1f, Gray);
                    break;
                }
                case ParticleMode.Fly:
                {
                    float f = e / p.life;
                    if (f >= 1f) { particles.RemoveAt(i); FlownIntoHeart++; continue; }
                    float g = 1f - (1f - f) * (1f - f);
                    var from = new Vector2(p.ax, p.ay);
                    var to = new Vector2(HeartCenterX, HeartCenterY);
                    Vector2 at = (1f - g) * (1f - g) * from + 2f * (1f - g) * g * p.control + g * g * to;
                    DrawOffsets(p, Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y), 1f - .85f * g, Red);
                    break;
                }
                default:
                {
                    if (e >= p.life) { particles.RemoveAt(i); continue; }
                    Put(Mathf.RoundToInt(p.ax + p.vx * e), Mathf.RoundToInt(p.ay + p.vy * e + .5f * p.gravity * e * e), Gray);
                    break;
                }
            }
        }
    }

    void DrawOffsets(Particle p, int x, int y, float scale, Color32 color)
    {
        if (scale >= .999f) { foreach (int o in p.offsets) Put(x + DX(o), y + DY(o), color); return; }
        // shrinking toward the heart: nearest-pixel pull toward the anchor, still on the grid
        foreach (int o in p.offsets) Put(x + Mathf.RoundToInt(DX(o) * scale), y + Mathf.RoundToInt(DY(o) * scale), color);
    }

    static bool Offscreen(Particle p, int x, int y)
    {
        int top = int.MaxValue, left = int.MaxValue, right = int.MinValue;
        foreach (int o in p.offsets) { top = Mathf.Min(top, DY(o)); left = Mathf.Min(left, DX(o)); right = Mathf.Max(right, DX(o)); }
        return y + top >= Height || x + right < 0 || x + left >= Width;
    }

    // ================================================================ glyphs from the chat font's SDF atlas

    /// <summary>One letter's distance field (atlas alpha, rows bottom-up) and where it sits in font units.</summary>
    sealed class Glyph
    {
        public float[] field;
        public int width, height;
        public float left, bottom;      // font units at field pixel (0, 0)
        public float pixelsPerUnit;     // field pixels per font unit
        public float spread;            // field pixels from the edge to alpha 0 or 1
        public float advance;           // font units
    }

    TMP_FontAsset font;
    float ascentLine, descentLine, lineUnit = 1f, unitScale = 1f;
    readonly Dictionary<char, Glyph> glyphCache = new Dictionary<char, Glyph>();

    void EnsureFont()
    {
        var label = OS_Handle.Instance?.LinkMessageController?.DisplayedTextLabel;
        var wanted = label != null ? label.font : font;
        if (wanted == null) wanted = TMP_Settings.defaultFontAsset;
        if (wanted == font) return;
        font = wanted;
        glyphCache.Clear();
        if (font == null) return;
        var face = font.faceInfo;
        ascentLine = face.ascentLine; descentLine = face.descentLine;
        lineUnit = Mathf.Max(1f, ascentLine - descentLine);
        unitScale = face.pointSize * face.scale;
    }

    Glyph GlyphOf(char c)
    {
        if (glyphCache.TryGetValue(c, out var cached)) return cached;
        var glyph = ReadGlyph(c) ?? new Glyph { advance = lineUnit * .5f, field = new float[0] };
        glyphCache[c] = glyph;
        return glyph;
    }

    Glyph ReadGlyph(char c)
    {
        if (font == null || !FindCharacter(font, c, 0, out var character, out var owner)) return null;
        var source = character.glyph;
        var rect = source.glyphRect;
        var metrics = source.metrics;
        // Fallback fonts may be authored at another size: bring them into the main font's units.
        float toMain = unitScale / Mathf.Max(1e-4f, owner.faceInfo.pointSize * owner.faceInfo.scale);
        var glyph = new Glyph { advance = metrics.horizontalAdvance * toMain, field = new float[0] };
        if (rect.width <= 0 || rect.height <= 0 || metrics.width <= 0f) return glyph;
        var atlas = owner.atlasTextures != null && source.atlasIndex < owner.atlasTextures.Length ? owner.atlasTextures[source.atlasIndex] : null;
        if (atlas == null) return glyph;
        int pad = owner.atlasPadding;
        int x0 = Mathf.Max(0, rect.x - pad), y0 = Mathf.Max(0, rect.y - pad);
        int x1 = Mathf.Min(atlas.width, rect.x + rect.width + pad), y1 = Mathf.Min(atlas.height, rect.y + rect.height + pad);
        Color[] pixels;
        try { pixels = atlas.GetPixels(x0, y0, x1 - x0, y1 - y0); }
        catch (Exception error)
        {
            Debug.LogWarning("Final heal: the font atlas is not readable (" + owner.name + "): " + error.Message);
            return glyph;
        }
        glyph.width = x1 - x0; glyph.height = y1 - y0;
        glyph.field = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) glyph.field[i] = pixels[i].a;
        glyph.pixelsPerUnit = rect.width / (metrics.width * toMain);
        glyph.left = metrics.horizontalBearingX * toMain - (rect.x - x0) / glyph.pixelsPerUnit;
        glyph.bottom = (metrics.horizontalBearingY - metrics.height) * toMain - (rect.y - y0) / glyph.pixelsPerUnit;
        glyph.spread = Mathf.Max(1f, pad);
        return glyph;
    }

    static bool FindCharacter(TMP_FontAsset asset, char c, int depth, out TMP_Character character, out TMP_FontAsset owner)
    {
        character = null; owner = null;
        if (asset == null || depth > 3) return false;
        var table = asset.characterLookupTable;
        if (table == null || !table.TryGetValue(c, out character))
        {
            if (asset.atlasPopulationMode == AtlasPopulationMode.Dynamic) asset.TryAddCharacters(c.ToString(), out _);
            table = asset.characterLookupTable;
            if (table != null) table.TryGetValue(c, out character);
        }
        if (character != null) { owner = asset; return true; }
        if (asset.fallbackFontAssetTable != null)
            foreach (var fallback in asset.fallbackFontAssetTable)
                if (FindCharacter(fallback, c, depth + 1, out character, out owner)) return true;
        return false;
    }

    /// <summary>
    /// Is the letter's point (font units from its pen position) inked at this
    /// size? A pixel center within about a third of a pixel of the outline counts,
    /// so strokes thinner than a pixel at 14 px do not drop out.
    /// </summary>
    bool Ink(Glyph glyph, float fontX, float fontY, float lineHeightPixels)
    {
        if (glyph.field.Length == 0) return false;
        float gx = (fontX - glyph.left) * glyph.pixelsPerUnit - .5f;
        float gy = (fontY - glyph.bottom) * glyph.pixelsPerUnit - .5f;
        if (gx < -1f || gy < -1f || gx > glyph.width || gy > glyph.height) return false;
        // one screen pixel in field pixels, and the threshold widened by a third of it
        float pixel = lineUnit / Mathf.Max(1f, lineHeightPixels) * glyph.pixelsPerUnit;
        float threshold = Mathf.Clamp(.5f - .5f * (.33f * pixel) / glyph.spread, .2f, .5f);
        return Sample(glyph, gx, gy) >= threshold;
    }

    static float Sample(Glyph glyph, float x, float y)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        float a = Field(glyph, ix, iy), b = Field(glyph, ix + 1, iy), c = Field(glyph, ix, iy + 1), d = Field(glyph, ix + 1, iy + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Field(Glyph glyph, int x, int y) =>
        x < 0 || y < 0 || x >= glyph.width || y >= glyph.height ? 0f : glyph.field[y * glyph.width + x];
}
