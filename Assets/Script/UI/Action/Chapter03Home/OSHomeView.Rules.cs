using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The final-knock reasoning as Baba Is You rules on the 「家」 grid
/// (pancake 10-23; mock 第三章/Baba式场景/示意图/08_推理规则字块对比.gif).
///   「主人 在 [卧室] ？」 assembles, the evidence stacks up ever faster, the
///   卧室 block is knocked out and shatters into dither, 「主人 在 ？？？」,
///   then 「主人」 floods every free slot, flips to hex bytes, and goes black.
/// Nouns are bright, verbs gray, properties are gray blocks with the word cut
/// out, the question mark never stops boiling. 「咚」 stays and rings red on
/// every heartbeat; the backend reads RulesBeatInterval to drive the panel.
/// Times are authored for 8.6 s and scale to the length the story asks for.
/// After the show the view stays black until the next room swap.
/// </summary>
public sealed partial class OSHomeView
{
    const float RulesAuthoredSeconds = 8.6f;
    const float RulesDimEnd = .8f, RulesBreak = 5.6f, RulesQuestions = 5.95f, RulesQuestionStep = .12f;
    const float RulesFloodStart = 6.3f, RulesFloodSeconds = 2f, RulesHex = 8.3f, RulesBlack = 8.5f;
    const float RulesPop = .12f, RulesBreakPixelSeconds = .03f, RulesBreakDissolve = .4f;
    const string RuleKeyPrefix = "os.chapter03.ui.rule.";
    const byte RuleBright = ClickInk;

    enum RuleKind { Noun, Verb, Property, Question }

    sealed class RuleBlock
    {
        public string id, fallback;
        public RuleKind kind;
        public Vector2Int tile;
        public int tiles;
        public float appear; // authored seconds
    }

    sealed class RuleShow
    {
        public float clock, seconds;
        public Action done;
        public bool finished;
        public string[] hex;
        public RuleBlock bedroom, question;
        public readonly List<RuleBlock> blocks = new List<RuleBlock>();
        public readonly HashSet<Vector2Int> taken = new HashSet<Vector2Int>();
    }

    // id, Chinese fallback, kind. Translations live under os.chapter03.ui.rule.<id>.
    static readonly (string id, string zh, RuleKind kind)
        RuleMaster = ("master", "主人", RuleKind.Noun), RuleIn = ("in", "在", RuleKind.Verb),
        RuleHere = ("here", "在", RuleKind.Verb),
        RuleBedroom = ("bedroom", "卧室", RuleKind.Property), RuleIs = ("is", "是", RuleKind.Verb),
        RuleShoes = ("shoes", "鞋", RuleKind.Noun), RuleUmbrella = ("umbrella", "伞", RuleKind.Noun),
        RuleCoat = ("coat", "外套", RuleKind.Noun), RuleCoffee = ("coffee", "咖啡", RuleKind.Noun),
        RuleCold = ("cold", "凉", RuleKind.Property), RuleMedicine = ("medicine", "药", RuleKind.Noun),
        RuleEmpty = ("empty", "空", RuleKind.Property), RuleDoor = ("door", "门", RuleKind.Noun),
        RuleNo = ("no", "无", RuleKind.Verb), RuleAnswer = ("answer", "回应", RuleKind.Noun);

    RuleShow rules;

    public bool IsPlayingRules => rules != null && !rules.finished;
    /// <summary>Seconds between heartbeats while the rules run (speeding up); -1 otherwise.</summary>
    public float RulesBeatInterval => IsPlayingRules ? Mathf.Max(.22f, 1.1f - .14f * RulesTime) : -1f;

    float RulesTime => rules == null ? 0f : rules.clock / rules.seconds * RulesAuthoredSeconds;
    bool RulesHideDong => rules != null && RulesTime >= RulesBlack;
    bool RulesHideMe => rules != null && RulesTime >= RulesDimEnd;

    /// <summary>Play the reasoning over `seconds`; `hexLine` is the untranslated byte line for the end.</summary>
    public void PlayReasoningRules(float seconds, string hexLine, Action done)
    {
        rules = new RuleShow { seconds = Mathf.Max(.5f, seconds), done = done, hex = HexTokens(hexLine) };
        BuildRules(rules);
    }

    public void StopRules() => rules = null;

    /// <summary>Word widths change with the script, so a language switch mid-show lays the rules out again.</summary>
    void RelayoutRules()
    {
        if (rules == null || rules.finished) return;
        var old = rules;
        rules = new RuleShow { clock = old.clock, seconds = old.seconds, done = old.done, hex = old.hex };
        BuildRules(rules);
    }

    // ================================================================ layout

    void BuildRules(RuleShow show)
    {
        // 「咚」 beats alone in a clearing; rules next to it would read as part of them.
        if (dongMode == DongMode.Follow)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++) show.taken.Add(new Vector2Int(dong.x + dx, dong.y + dy));
        // The first rule is centred and keeps room for 「？？？」 after 卧室 breaks.
        int master = RuleWidth(RuleMaster), verb = RuleWidth(RuleIn);
        int tail = Mathf.Max(RuleWidth(RuleBedroom) + 1, 3);
        int first = master + verb + tail;
        var at = Fit(show, first, 3, (OSHomeLayout.Columns - first) / 2);
        if (at.x >= 0)
        {
            Reserve(show, at, first);
            int x = at.x;
            Add(show, RuleMaster, ref x, at.y, 1.2f);
            Add(show, RuleIn, ref x, at.y, 1.44f);
            show.bedroom = Add(show, RuleBedroom, ref x, at.y, 1.68f);
            show.question = AddQuestion(show, new Vector2Int(x, at.y), 1.92f);
            for (int i = 0; i < 3; i++)
                AddQuestion(show, new Vector2Int(show.bedroom.tile.x + i, at.y), RulesQuestions + i * RulesQuestionStep);
        }

        var cursor = new Vector2Int(1, 5);
        Rule(show, ref cursor, 2.4f, .12f, RuleShoes, RuleHere);
        Rule(show, ref cursor, 2.75f, .1f, RuleUmbrella, RuleHere);
        Rule(show, ref cursor, 3.05f, .09f, RuleCoat, RuleHere);
        cursor = new Vector2Int(1, 6);
        Rule(show, ref cursor, 3.5f, .08f, RuleCoffee, RuleIs, RuleCold);
        Rule(show, ref cursor, 3.9f, .07f, RuleMedicine, RuleIs, RuleEmpty);
        cursor = new Vector2Int(1, 7);
        Rule(show, ref cursor, 4.3f, .05f, RuleDoor, RuleNo, RuleAnswer);
        cursor = new Vector2Int(6, 8);
        Rule(show, ref cursor, 4.6f, .05f, RuleDoor, RuleNo, RuleAnswer);
        cursor = new Vector2Int(10, 7);
        Rule(show, ref cursor, 4.9f, .05f, RuleDoor, RuleNo, RuleAnswer);

        // 「主人」 fills every whole free slot, faster and faster: slot i of n lands
        // when ((t - start) / length)^1.6 reaches (i + 1) / n.
        var slots = new List<Vector2Int>();
        for (int y = 1; y < OSHomeLayout.Rows - 1; y++)
            for (int x = 1; x + master <= OSHomeLayout.Columns - 1;)
            {
                if (Free(show, new Vector2Int(x, y), master)) { slots.Add(new Vector2Int(x, y)); x += master; }
                else x++;
            }
        var random = new System.Random(7);
        for (int i = slots.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            var swap = slots[i]; slots[i] = slots[j]; slots[j] = swap;
        }
        for (int i = 0; i < slots.Count; i++)
        {
            float appear = RulesFloodStart + RulesFloodSeconds * Mathf.Pow((i + 1f) / slots.Count, 1f / 1.6f);
            int x = slots[i].x;
            Add(show, RuleMaster, ref x, slots[i].y, appear);
        }
    }

    void Rule(RuleShow show, ref Vector2Int cursor, float start, float step,
        params (string id, string zh, RuleKind kind)[] words)
    {
        int width = 0;
        foreach (var word in words) width += RuleWidth(word);
        var at = Fit(show, width, cursor.y, cursor.x);
        if (at.x < 0) return;
        Reserve(show, at, width);
        int x = at.x;
        for (int i = 0; i < words.Length; i++) Add(show, words[i], ref x, at.y, start + i * step);
        cursor = new Vector2Int(x + 1, at.y);
    }

    RuleBlock Add(RuleShow show, (string id, string zh, RuleKind kind) word, ref int x, int y, float appear)
    {
        var block = new RuleBlock
        {
            id = word.id, fallback = word.zh, kind = word.kind,
            tile = new Vector2Int(x, y), tiles = RuleWidth(word), appear = appear
        };
        show.blocks.Add(block);
        x += block.tiles;
        return block;
    }

    RuleBlock AddQuestion(RuleShow show, Vector2Int tile, float appear)
    {
        var block = new RuleBlock { id = "question", fallback = "？", kind = RuleKind.Question, tile = tile, tiles = 1, appear = appear };
        show.blocks.Add(block);
        return block;
    }

    /// <summary>First free run of `width` tiles from (x, row), reading on and wrapping round the inner grid.</summary>
    static Vector2Int Fit(RuleShow show, int width, int row, int x)
    {
        int rows = OSHomeLayout.Rows - 2;
        for (int r = 0; r < rows; r++)
        {
            int y = 1 + (row - 1 + r) % rows;
            for (int cx = r == 0 ? Mathf.Max(1, x) : 1; cx + width <= OSHomeLayout.Columns - 1; cx++)
                if (Free(show, new Vector2Int(cx, y), width)) return new Vector2Int(cx, y);
        }
        return new Vector2Int(-1, -1);
    }

    static bool Free(RuleShow show, Vector2Int at, int width)
    {
        for (int i = 0; i < width; i++)
            if (show.taken.Contains(new Vector2Int(at.x + i, at.y))) return false;
        return true;
    }

    static void Reserve(RuleShow show, Vector2Int at, int width)
    {
        for (int i = 0; i < width; i++) show.taken.Add(new Vector2Int(at.x + i, at.y));
    }

    /// <summary>Wide scripts take a tile per character; small capitals fit up to two lines in one tile.</summary>
    static int RuleWidth((string id, string zh, RuleKind kind) word)
    {
        string text = Localize(RuleKeyPrefix + word.id, word.zh);
        return OSHomeGlyphBook.IsWide(text) ? Mathf.Max(1, new StringInfo(text).LengthInTextElements) : 1;
    }

    static string[] HexTokens(string line)
    {
        var tokens = new List<string>();
        if (!string.IsNullOrEmpty(line))
            foreach (var part in line.Split(' '))
                if (part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && part.Length > 2) tokens.Add(part.Substring(2));
        if (tokens.Count == 0) tokens.AddRange(new[] { "4D", "0A", "FF" });
        return tokens.ToArray();
    }

    // ================================================================ playback

    void TickRules()
    {
        if (rules == null || rules.finished) return;
        rules.clock += dt;
        if (rules.clock < rules.seconds) return;
        rules.clock = rules.seconds; rules.finished = true;
        var done = rules.done; rules.done = null;
        done?.Invoke();
    }

    /// <summary>The room's own light dies out over the first 0.8 s.</summary>
    void DimLightsForRules()
    {
        if (rules == null) return;
        float keep = 1f - Mathf.Clamp01(RulesTime / RulesDimEnd);
        if (keep >= 1f) return;
        for (int i = 0; i < lit.Length; i++) lit[i] = (byte)Mathf.RoundToInt(lit[i] * keep);
    }

    void PaintRules()
    {
        if (rules == null) return;
        float t = RulesTime;
        if (t >= RulesDimEnd) Array.Clear(ink, 0, ink.Length);
        if (t >= RulesBlack) return;
        bool hex = t >= RulesHex;
        foreach (var block in rules.blocks)
        {
            if (t < block.appear) continue;
            if (block == rules.question && t >= RulesBreak) continue;
            if (block == rules.bedroom && t >= RulesBreak) { PaintBrokenRule(block, t - RulesBreak); continue; }
            PaintRuleBlock(block, t - block.appear, hex);
        }
        if (now < dongBumpUntil && dongMode == DongMode.Follow) PaintBeatRing();
    }

    void PaintRuleBlock(RuleBlock block, float age, bool hex)
    {
        int ox = block.tile.x * T, oy = block.tile.y * T, width = block.tiles * T;
        for (int y = 0; y < T; y++)
            for (int x = 0; x < width; x++) Raw(ox + x, oy + y, Black);
        int fast = Mathf.FloorToInt(now / .05f);
        if (hex)
        {
            for (int i = 0; i < block.tiles; i++)
            {
                string token = rules.hex[Hash((uint)((block.tile.x + i) * 31 + block.tile.y), fast) % (uint)rules.hex.Length];
                PaintRuleGlyph(glyphs.Layout(token, 1), ox + i * T, oy, RuleBright, Hash(token, fast), false);
            }
            return;
        }
        string text = block.kind == RuleKind.Question ? block.fallback : Localize(RuleKeyPrefix + block.id, block.fallback);
        var word = glyphs.Layout(text, block.tiles);
        // Thickening a 3x5 small capital smears it, so only wide script pops in bold.
        bool bold = OSHomeGlyphBook.IsWide(text) && (block.kind == RuleKind.Question || age < RulesPop);
        // The question mark re-boils every frame; everything else keeps the room's three-frame boil.
        uint seed = block.kind == RuleKind.Question
            ? Hash((uint)(block.tile.x * 31 + block.tile.y), fast)
            : Hash(block.id, block.tile.x * 31 + block.tile.y);
        if (block.kind == RuleKind.Property)
        {
            for (int y = 1; y < T - 1; y++)
                for (int x = 0; x < width; x++) Raw(ox + x, oy + y, Gray);
            PaintRuleGlyph(word, ox, oy, Black, seed, bold);
            return;
        }
        PaintRuleGlyph(word, ox, oy, block.kind == RuleKind.Verb ? Gray : RuleBright, seed, bold);
    }

    /// <summary>卧室 is pushed out of the rule: it slides right a pixel at a time and dithers away.</summary>
    void PaintBrokenRule(RuleBlock block, float since)
    {
        int level = Mathf.Clamp(Mathf.RoundToInt(16f * (1f - since / RulesBreakDissolve)), 0, 16);
        if (level <= 0) return;
        int ox = block.tile.x * T + Mathf.FloorToInt(since / RulesBreakPixelSeconds), oy = block.tile.y * T;
        int width = block.tiles * T;
        var word = glyphs.Layout(Localize(RuleKeyPrefix + block.id, block.fallback), block.tiles);
        uint boil = Hash(block.id, Mathf.FloorToInt(now / .05f));
        for (int y = 0; y < T; y++)
        {
            int shift = BoilShift(boil, y);
            for (int x = 0; x < width; x++)
            {
                int X = ox + x, Y = oy + y;
                if (Bayer[(Y & 3) * 4 + (X & 3)] >= level) continue;
                bool cut = word.Get(x - shift, y);
                if (y >= 1 && y < T - 1 && !cut) Raw(X, Y, Gray);
                else if (cut) Raw(X, Y, Black);
            }
        }
    }

    void PaintRuleGlyph(OSHomeGlyphBook.Word word, int ox, int oy, byte color, uint seed, bool bold)
    {
        int tiles = word.width / T;
        for (int tile = 0; tile < tiles; tile++)
        {
            uint boil = Hash(seed + (uint)tile * 7919u, boilFrame);
            for (int y = 0; y < T; y++)
            {
                int shift = BoilShift(boil, y);
                for (int x = 0; x < T; x++)
                {
                    if (!word.Get(tile * T + x, y)) continue;
                    int X = ox + tile * T + x + shift, Y = oy + y;
                    Raw(X, Y, color);
                    if (bold) { Raw(X + 1, Y, color); Raw(X, Y + 1, color); }
                }
            }
        }
    }

    /// <summary>A dithered red ring round 「咚」 for the frames its beat lasts.</summary>
    void PaintBeatRing()
    {
        float cx = dong.x * T + 8f, cy = dong.y * T + 8f;
        for (int y = Mathf.FloorToInt(cy - 14f); y <= Mathf.CeilToInt(cy + 14f); y++)
            for (int x = Mathf.FloorToInt(cx - 14f); x <= Mathf.CeilToInt(cx + 14f); x++)
            {
                float dx = x + .5f - cx, dy = y + .5f - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < 11.5f || d > 13f || Bayer[(y & 3) * 4 + (x & 3)] >= 10) continue;
                Raw(x, y, Red);
            }
    }
}
