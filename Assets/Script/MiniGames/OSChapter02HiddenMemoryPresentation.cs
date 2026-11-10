using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chapter 2's hidden memory, read together with Live. The finale director owns
/// progression, input and the paused clock; this component only presents.
///
///   LightsOut  the OS falls to black in 1-bit dither steps. Live and the red
///              pointer stay lit above it: they watch with the player.
///   Signal     one line opens in the dark and parts into the frame of a file.
///   Beats      the woman and the girl, one line at a time. Older lines decay into
///              blanks instead of fading (no alpha in 1-bit). Knocks shake the table.
///   Closing    the log decays top-down, the frame folds back into a point, and the
///              OS returns through the same dither steps.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSChapter02HiddenMemoryPresentation : MonoBehaviour
{
    [Serializable] sealed class HiddenScript { public int schemaVersion; public Beat[] beats; }
    [Serializable] sealed class Beat
    {
        public int id;
        public string kind, speakerKey, localizationKey, sound;
    }
    sealed class Row
    {
        public RectTransform root;
        public TMP_Text speaker, body;
        public string speakerText, bodyText;
        public int beat, seed;
        public float height, targetY, y, decay = -1f;
        public int shownTick = -1, shownVisible = -1;
    }
    sealed class Voice { public AudioSource source; public AudioClip clip; public bool paused; }
    sealed class Lift
    {
        public RectTransform target;
        public Canvas canvas;
        public bool added, overrideSorting, enabled;
        public int order, layer;
    }
    enum Phase { None, LightsOut, Signal, Text, Sound, Closing, LightsOn }

    // Field names are kept from the first version so scene references survive.
    [SerializeField] TextAsset hiddenScript;
    [SerializeField] AudioClip tableKnock, paperFall;
    [SerializeField, Min(8f)] float charactersPerSecond = 26f;
    // Renamed from lineSize so an older serialized 12 cannot override the smaller size.
    [SerializeField, Min(6f)] float lineSize = 11f;
    [SerializeField] int overlaySortingOrder = 1200;
    // Keyboard ticks while a line types. Empty: a short click is synthesized at runtime.
    [SerializeField] AudioClip[] typeClips;
    [SerializeField, Range(0f, 1f)] float typeVolume = .28f;

    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 0xff);
    // Lines already read step one shade down, so the newest line leads the eye.
    static readonly Color InkRead = new Color32(0x55, 0x52, 0x4f, 0xff);
    static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    const string Noise = "#/\\_=+*<>01";
    const string LivePointerName = "CharacterPermanentRedPointer";
    const float DitherStep = .09f, DarkHold = .55f, DarkHoldResume = .2f;
    const float SignalGrow = .28f, SignalPart = .36f, SignalCaret = .45f;
    const float CloseDecay = 1.1f, CloseLastLine = .7f, CloseFold = .32f, CloseShrink = .18f, CloseDot = .16f, CloseDark = .35f;
    const float RowGap = 5f, GutterGap = 7f, BaselineInset = 22f;
    const int KeepIntactRows = 2, VisibleRows = 7;

    readonly List<Row> rows = new List<Row>(24);
    readonly List<Voice> voices = new List<Voice>(4);
    readonly List<Lift> lifts = new List<Lift>(2);
    readonly StringBuilder buffer = new StringBuilder(256);
    Texture2D[] ditherLevels;
    Texture2D dashTexture;
    AudioClip[] synthesizedTypeClips;
    float lastTypeSound = -1f;
    int typeSerial;
    RectTransform overlay, column, content;
    RawImage shade, ruleTop, ruleBottom;
    Image caret, dot;
    HiddenScript script;
    Action<int> lineCompleted;
    Action completed;
    Phase phase;
    LanguageState language;
    int beatIndex, visibleCharacters, nextKnock, tick = -1;
    float phaseTime, clock, reveal, readTime, gutterWidth, columnWidth, columnHeight;
    float knockPulse, fallPulse;
    bool paused, resumed;

    public bool IsPresenting => phase != Phase.None;
    public int CurrentBeatIndex => beatIndex;
    public int DisplayedLineCount => rows.Count;
    public string DebugPhase => phase.ToString();
    /// <summary>1 on a knock, decaying. The red pointer taps along with it.</summary>
    public float KnockPulse => knockPulse;
    /// <summary>1 when the paper figure falls, decaying.</summary>
    public float FallPulse => fallPulse;
    /// <summary>True once the frame is open and before it folds away.</summary>
    public bool IsReadable => phase == Phase.Text || phase == Phase.Sound || (phase == Phase.Closing && phaseTime < CloseDecay + CloseLastLine);

    public void Begin(int resumeLine, Action<int> onLineCompleted, Action onCompleted)
    {
        ResetPresentation();
        script = hiddenScript != null ? JsonUtility.FromJson<HiddenScript>(hiddenScript.text) : null;
        if (script == null || script.beats == null || script.beats.Length != 24)
        {
            Debug.LogError("[Chapter02Finale] The 24-beat hidden-memory script is missing or invalid.", this);
            return;
        }
        lineCompleted = onLineCompleted;
        completed = onCompleted;
        beatIndex = Mathf.Clamp(resumeLine, 0, script.beats.Length);
        resumed = beatIndex > 0;
        language = CurrentLanguage();
        EnsureTextures();
        BuildOverlay();
        LiftWatchers();
        phase = Phase.LightsOut;
        phaseTime = clock = 0f;
        Render();
    }

    public void AdvancePresentation(float deltaTime)
    {
        if (!IsPresenting || paused || overlay == null) return;
        float dt = Mathf.Clamp(deltaTime, 0f, .05f);
        clock += dt;
        phaseTime += dt;
        knockPulse = Mathf.Max(0f, knockPulse - dt * 6f);
        fallPulse = Mathf.Max(0f, fallPulse - dt * 3f);
        RefreshLanguage();
        LiftWatchers();
        switch (phase)
        {
            case Phase.LightsOut:
                if (phaseTime >= DitherStep * 4f + (resumed ? DarkHoldResume : DarkHold)) Enter(Phase.Signal);
                break;
            case Phase.Signal:
                if (phaseTime >= SignalGrow + SignalPart + SignalCaret)
                {
                    for (int i = 0; i < beatIndex; i++) if (IsText(script.beats[i])) AddRow(i, true);
                    SnapRows();
                    BeginBeat();
                }
                break;
            case Phase.Text: TickText(dt); break;
            case Phase.Sound: TickSound(); break;
            case Phase.Closing:
                if (phaseTime >= CloseDecay + CloseLastLine + CloseFold + CloseShrink + CloseDot + CloseDark) Enter(Phase.LightsOn);
                break;
            case Phase.LightsOn:
                if (phaseTime >= DitherStep * 4f + .05f)
                {
                    var callback = completed;
                    ResetPresentation();
                    callback?.Invoke();
                    return;
                }
                break;
        }
        foreach (var row in rows) row.y = Mathf.MoveTowards(row.y, row.targetY, dt * 140f);
        Render();
    }

    public void HandleContinue()
    {
        if (paused || phase != Phase.Text || rows.Count == 0) return;
        Row row = rows[rows.Count - 1];
        if (visibleCharacters < row.bodyText.Length)
        {
            reveal = visibleCharacters = row.bodyText.Length;
            readTime = 0f;
        }
        else if (readTime >= (beatIndex == script.beats.Length - 1 ? .6f : .12f)) CompleteBeat();
        Render();
    }

    public void SetPaused(bool value)
    {
        if (paused == value) return;
        paused = value;
        foreach (var voice in voices)
        {
            if (voice.source == null || voice.source.clip != voice.clip) continue;
            if (value && voice.source.isPlaying) { voice.source.Pause(); voice.paused = true; }
            else if (!value && voice.paused) { voice.source.UnPause(); voice.paused = false; }
        }
    }

    /// <summary>Where the red pointer's tip should rest: just after the newest revealed glyph.</summary>
    public bool TryGetReadPoint(out Vector3 world)
    {
        world = Vector3.zero;
        if (column == null) return false;
        if (rows.Count == 0 || !IsReadable)
        {
            // Before the first line: wait at the start of the reading line.
            Rect area = content.rect;
            world = content.TransformPoint(new Vector3(area.xMin + gutterWidth + GutterGap - 4f, area.yMin + BaselineInset + lineSize * .5f, 0f));
            return true;
        }
        Row row = rows[rows.Count - 1];
        var label = row.body;
        label.ForceMeshUpdate();
        var info = label.textInfo;
        int shown = Mathf.Min(row.shownVisible < 0 ? row.bodyText.Length : row.shownVisible, info.characterCount);
        if (shown <= 0)
        {
            world = label.rectTransform.TransformPoint(new Vector3(0f, -lineSize * .8f, 0f));
            return true;
        }
        var glyph = info.characterInfo[shown - 1];
        // While a finished line waits she points at the blinking caret from just below it.
        world = IsWaitingForPlayer
            ? label.rectTransform.TransformPoint(new Vector3(glyph.topRight.x + 4f, glyph.baseLine - 2f, 0f))
            : label.rectTransform.TransformPoint(new Vector3(glyph.topRight.x + 3f, glyph.descender + 1f, 0f));
        return true;
    }

    bool IsWaitingForPlayer => phase == Phase.Text && rows.Count > 0
        && visibleCharacters >= rows[rows.Count - 1].bodyText.Length;

    /// <summary>World point on the baseline just after the newest line's last glyph.</summary>
    bool TryLineEnd(out Vector3 world)
    {
        world = Vector3.zero;
        if (rows.Count == 0) return false;
        var label = rows[rows.Count - 1].body;
        label.ForceMeshUpdate();
        var info = label.textInfo;
        if (info.characterCount <= 0) return false;
        var glyph = info.characterInfo[info.characterCount - 1];
        world = label.rectTransform.TransformPoint(new Vector3(glyph.topRight.x + 2f, glyph.baseLine, 0f));
        return true;
    }

    void Enter(Phase next)
    {
        phase = next;
        phaseTime = 0f;
    }

    void BeginBeat()
    {
        reveal = readTime = 0f;
        visibleCharacters = 0;
        if (beatIndex >= script.beats.Length) { Enter(Phase.Closing); return; }
        var beat = script.beats[beatIndex];
        if (IsText(beat))
        {
            Enter(Phase.Text);
            AddRow(beatIndex, false);
        }
        else
        {
            Enter(Phase.Sound);
            nextKnock = 0;
            if (beat.sound == "paper_fall") { Play(paperFall, .32f); fallPulse = 1f; }
        }
    }

    void TickText(float dt)
    {
        Row row = rows[rows.Count - 1];
        int length = row.bodyText.Length;
        if (visibleCharacters < length)
        {
            reveal += dt * charactersPerSecond;
            int before = visibleCharacters;
            visibleCharacters = Mathf.Min(length, Mathf.FloorToInt(reveal));
            if (visibleCharacters > before && !char.IsWhiteSpace(row.bodyText[visibleCharacters - 1])) TypeTick();
            if (visibleCharacters == length) readTime = 0f;
        }
        else readTime += dt;
        // The player turns each line; nothing advances by itself.
    }

    void TickSound()
    {
        bool knocks = script.beats[beatIndex].sound == "table_knock";
        if (knocks && nextKnock < 3 && phaseTime >= .18f + nextKnock * .34f)
        {
            Play(tableKnock, .45f);
            knockPulse = 1f;
            nextKnock++;
        }
        if (phaseTime >= (knocks ? 1.45f : 1.1f)) CompleteBeat();
    }

    void CompleteBeat()
    {
        beatIndex++;
        lineCompleted?.Invoke(beatIndex);
        BeginBeat();
    }

    static bool IsText(Beat beat) => beat.kind == "text";

    static LanguageState CurrentLanguage() => GameDataManager.Instance != null
        ? GameDataManager.Instance.CurrentLanguageState : LanguageState.zh_CN;

    string Localized(string key) => string.IsNullOrEmpty(key) ? string.Empty
        : GameDataManager.Instance?.GetLocalizedText(key, string.Empty) ?? string.Empty;

    // ---------------------------------------------------------------- rows

    TMP_Text NewLabel(string name, RectTransform parent, TextAlignmentOptions alignment)
    {
        var rect = MakeRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        var source = OS_Handle.Instance?.LinkMessageController?.DisplayedTextLabel;
        if (source != null && source.font != null) { label.font = source.font; label.fontSharedMaterial = source.fontSharedMaterial; }
        OSLocalizedFontUtility.ApplyCurrent(label);
        label.fontSize = lineSize;
        label.enableAutoSizing = false;
        label.richText = false;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = alignment;
        label.color = Ink;
        label.margin = Vector4.zero;
        return label;
    }

    void MeasureGutter()
    {
        // One gutter for both speakers so the dialogue reads in a straight column.
        var probe = NewLabel("GutterProbe", content, TextAlignmentOptions.TopRight);
        float width = 0f;
        foreach (string key in new[] { "ui.os.finale_ch02.speaker_woman", "ui.os.finale_ch02.speaker_girl" })
            width = Mathf.Max(width, probe.GetPreferredValues(Localized(key), 400f, 0f).x);
        Destroy(probe.gameObject);
        gutterWidth = Mathf.Ceil(Mathf.Clamp(width, lineSize * 2f, columnWidth * .3f));
    }

    void AddRow(int index, bool alreadyRead)
    {
        Beat beat = script.beats[index];
        var root = MakeRect("HiddenLine_" + beat.id.ToString("00"), content);
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0f, 1f);
        var row = new Row
        {
            root = root, beat = index, seed = beat.id * 7919,
            speakerText = Localized(beat.speakerKey).TrimEnd(),
            bodyText = Localized(beat.localizationKey),
        };
        row.speaker = NewLabel("Speaker", root, TextAlignmentOptions.TopRight);
        row.speaker.rectTransform.anchoredPosition = Vector2.zero;
        row.speaker.rectTransform.sizeDelta = new Vector2(gutterWidth, lineSize + 4f);
        row.body = NewLabel("Line", root, TextAlignmentOptions.TopLeft);
        float bodyWidth = columnWidth - gutterWidth - GutterGap;
        row.body.rectTransform.anchoredPosition = new Vector2(gutterWidth + GutterGap, 0f);
        row.body.rectTransform.sizeDelta = new Vector2(bodyWidth, 40f);
        float height = Mathf.Ceil(row.body.GetPreferredValues(row.bodyText, bodyWidth, 0f).y);
        height = Mathf.Max(lineSize + 2f, height);
        row.body.rectTransform.sizeDelta = new Vector2(bodyWidth, height);
        row.height = height + RowGap;
        root.sizeDelta = new Vector2(columnWidth, height);
        row.speaker.text = row.speakerText;
        row.body.text = alreadyRead ? row.bodyText : string.Empty;
        row.shownVisible = alreadyRead ? row.bodyText.Length : 0;
        rows.Add(row);
        // A new line enters a few units low and settles onto the reading line.
        row.y = BaselineInset + height - 6f;
        Reflow();
    }

    void Reflow()
    {
        float y = BaselineInset;
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            y += rows[i].height - RowGap;
            rows[i].targetY = y;
            y += RowGap;
        }
    }

    void SnapRows() { foreach (var row in rows) row.y = row.targetY; }

    void RefreshLanguage()
    {
        var next = CurrentLanguage();
        if (next == language) return;
        language = next;
        float fraction = phase == Phase.Text && rows.Count > 0
            ? visibleCharacters / (float)Mathf.Max(1, rows[rows.Count - 1].bodyText.Length) : 1f;
        foreach (var row in rows) if (row.root != null) Destroy(row.root.gameObject);
        rows.Clear();
        if (content == null) return;
        MeasureGutter();
        int through = Mathf.Min(script.beats.Length, beatIndex + (phase == Phase.Text ? 1 : 0));
        if (phase == Phase.LightsOut || phase == Phase.Signal) through = 0;
        for (int i = 0; i < through; i++) if (IsText(script.beats[i])) AddRow(i, true);
        if (phase == Phase.Text && rows.Count > 0)
            reveal = visibleCharacters = Mathf.CeilToInt(rows[rows.Count - 1].bodyText.Length * fraction);
        SnapRows();
    }

    // ---------------------------------------------------------------- render

    void Render()
    {
        if (overlay == null) return;
        int nextTick = Mathf.FloorToInt(clock * 16f);
        bool newTick = nextTick != tick;
        tick = nextTick;

        // Lights: four dither steps each way. A resumed scene starts already dark.
        int level = 4;
        if (phase == Phase.LightsOut && !resumed) level = Mathf.Clamp(1 + Mathf.FloorToInt(phaseTime / DitherStep), 1, 4);
        else if (phase == Phase.LightsOn) level = Mathf.Clamp(3 - Mathf.FloorToInt(phaseTime / DitherStep), 0, 3);
        SetShade(level);

        // The table answers the knocks: everything on it hops one unit.
        float lift = knockPulse > .55f ? 1f : 0f;
        float drop = fallPulse > .6f ? -1f : 0f;
        content.anchoredPosition = new Vector2(0f, lift + drop);

        RenderFrame();
        float closeDecay = phase == Phase.Closing ? Mathf.Clamp01(phaseTime / CloseDecay) : 0f;
        bool lastLineGone = phase == Phase.Closing && phaseTime >= CloseDecay + CloseLastLine;
        bool hideRows = phase == Phase.LightsOut || phase == Phase.Signal || phase == Phase.LightsOn;
        for (int i = 0; i < rows.Count; i++)
        {
            Row row = rows[i];
            int age = rows.Count - 1 - i;
            float decay = AgeDecay(age);
            if (phase == Phase.Closing)
            {
                // Top-down: older lines go first, the final "next time" stays the longest.
                float order = rows.Count <= 1 ? 1f : i / (float)(rows.Count - 1);
                float local = Mathf.Clamp01(closeDecay * 1.6f - (1f - order) * .1f - order * .6f);
                decay = Mathf.Max(decay, i == rows.Count - 1 ? (lastLineGone ? 1f : local * .35f) : local);
            }
            bool newest = i == rows.Count - 1;
            Color color = newest && phase != Phase.Closing ? Ink : InkRead;
            row.speaker.color = row.body.color = color;
            bool typing = newest && phase == Phase.Text && visibleCharacters < row.bodyText.Length;
            int shown = newest && phase == Phase.Text ? visibleCharacters : row.bodyText.Length;
            row.root.gameObject.SetActive(!hideRows && age < VisibleRows && decay < 1f && row.y - row.height < columnHeight);
            row.root.anchoredPosition = new Vector2(0f, Mathf.Round(row.y));
            float quantized = Mathf.Round(decay * 20f) / 20f;
            if (quantized != row.decay || shown != row.shownVisible || (typing && newTick))
            {
                row.decay = quantized;
                row.shownVisible = shown;
                row.speaker.text = Decayed(row.speakerText, row.speakerText.Length, quantized, row.seed + 13, 0);
                row.body.text = Decayed(row.bodyText, shown, quantized, row.seed, typing ? 2 : 0);
            }
        }
    }

    static float AgeDecay(int age)
    {
        if (age < KeepIntactRows) return 0f;
        // 2 → .12, 3 → .3, 4 → .5, 5 → .7, 6 → .88, 7+ → gone
        return Mathf.Clamp01(.12f + (age - KeepIntactRows) * .19f);
    }

    /// <summary>
    /// Visible prefix, then a short noise tail while typing. Decay blanks glyphs by a
    /// fixed per-glyph hash, so a line wears away steadily instead of flickering.
    /// </summary>
    string Decayed(string value, int visible, float decay, int seed, int noiseTail)
    {
        buffer.Length = 0;
        visible = Mathf.Clamp(visible, 0, value.Length);
        for (int i = 0; i < visible; i++)
        {
            char c = value[i];
            if (decay > 0f && !char.IsWhiteSpace(c))
            {
                float h = Hash(i, seed);
                if (h < decay) { buffer.Append(h > decay - .06f ? '.' : ' '); continue; }
            }
            buffer.Append(c);
        }
        int tail = Mathf.Min(noiseTail, value.Length - visible);
        for (int i = 0; i < tail; i++) buffer.Append(Noise[(tick + i * 5 + seed) % Noise.Length]);
        return buffer.ToString();
    }

    static float Hash(int i, int seed)
    {
        unchecked
        {
            uint h = (uint)(i * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 1023) / 1023f;
        }
    }

    void RenderFrame()
    {
        // Rules are the edges of the opened file; the bottom one doubles as the table.
        float half = columnHeight * .5f;
        float width = 0f, gap = 0f;
        bool dotOn = false, caretOn = false;
        if (phase == Phase.Signal)
        {
            float grow = Mathf.Clamp01(phaseTime / SignalGrow);
            float part = Mathf.Clamp01((phaseTime - SignalGrow) / SignalPart);
            width = columnWidth * EaseOut(grow);
            gap = half * EaseOut(part);
            caretOn = phaseTime >= SignalGrow + SignalPart && Mathf.FloorToInt((phaseTime - SignalGrow - SignalPart) / .15f) % 2 == 0;
        }
        else if (phase == Phase.Text || phase == Phase.Sound)
        {
            width = columnWidth; gap = half;
        }
        else if (phase == Phase.Closing)
        {
            float t = phaseTime - CloseDecay - CloseLastLine;
            float fold = Mathf.Clamp01(t / CloseFold);
            float shrink = Mathf.Clamp01((t - CloseFold) / CloseShrink);
            width = columnWidth * (1f - EaseIn(shrink));
            gap = half * (1f - EaseIn(fold));
            dotOn = t >= CloseFold + CloseShrink && t < CloseFold + CloseShrink + CloseDot;
            if (t >= CloseFold + CloseShrink) width = 0f;
        }
        width = Mathf.Round(width * .5f) * 2f;
        gap = Mathf.Round(gap);
        PlaceRule(ruleTop, width, gap, 0f);
        float bump = phase == Phase.Sound && knockPulse > .55f ? 1f : 0f;
        PlaceRule(ruleBottom, width, -gap, bump);
        dot.enabled = dotOn;
        Vector2 caretAt = new Vector2(-columnWidth * .5f + gutterWidth + GutterGap, -half + BaselineInset);
        // A finished line waits for the player: the caret blinks after its last glyph.
        if (IsWaitingForPlayer && TryLineEnd(out Vector3 end))
        {
            caretOn = Mathf.FloorToInt(readTime / .45f) % 2 == 0;
            caretAt = column.InverseTransformPoint(end);
        }
        caret.enabled = caretOn;
        if (caretOn) caret.rectTransform.anchoredPosition = new Vector2(Mathf.Round(caretAt.x), Mathf.Round(caretAt.y));
    }

    void PlaceRule(RawImage rule, float width, float y, float bump)
    {
        rule.enabled = width >= 2f;
        if (!rule.enabled) return;
        rule.rectTransform.sizeDelta = new Vector2(width, 1f);
        rule.rectTransform.anchoredPosition = new Vector2(0f, y + bump);
        // Dashes stay put on the grid while the rule grows from the centre.
        rule.uvRect = new Rect(-width * .25f, 0f, width * .25f, 1f);
    }

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    static float EaseIn(float t) => t * t;

    void SetShade(int level)
    {
        level = Mathf.Clamp(level, 0, 4);
        shade.enabled = level > 0;
        if (level == 0) return;
        shade.texture = ditherLevels[level];
        Rect r = overlay.rect;
        shade.uvRect = new Rect(0f, 0f, r.width / 4f, r.height / 4f);
    }

    // ---------------------------------------------------------------- build

    void EnsureTextures()
    {
        if (ditherLevels == null)
        {
            ditherLevels = new Texture2D[5];
            for (int level = 0; level < 5; level++)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "HiddenMemoryDither" + level };
                var pixels = new Color32[16];
                for (int i = 0; i < 16; i++)
                    pixels[i] = Bayer[i] < level * 4 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                ditherLevels[level] = tex;
            }
        }
        if (dashTexture == null)
        {
            dashTexture = new Texture2D(4, 1, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "HiddenMemoryDash" };
            var on = new Color32(255, 255, 255, 255);
            var off = new Color32(255, 255, 255, 0);
            dashTexture.SetPixels32(new[] { on, on, off, off });
            dashTexture.Apply(false, true);
        }
    }

    void BuildOverlay()
    {
        var os = OS_Handle.Instance;
        overlay = MakeRect("Chapter02HiddenMemory", os != null ? os.transform : transform);
        Stretch(overlay);
        var canvas = overlay.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = overlaySortingOrder;
        overlay.gameObject.AddComponent<GraphicRaycaster>();
        var group = overlay.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;

        shade = NewRaw("Dark", overlay, Color.black);
        Stretch(shade.rectTransform);
        shade.raycastTarget = true;

        Canvas.ForceUpdateCanvases();
        Rect screen = overlay.rect;
        Rect liveRect = LiveBounds();
        // The reading column takes the larger side of the screen beside Live.
        float leftSpace = liveRect.width > 0f ? liveRect.xMin - screen.xMin : screen.width;
        float rightSpace = liveRect.width > 0f ? screen.xMax - liveRect.xMax : 0f;
        bool useLeft = leftSpace >= rightSpace;
        float spanMin = useLeft ? screen.xMin : liveRect.xMax;
        float spanMax = useLeft ? (liveRect.width > 0f ? liveRect.xMin : screen.xMax) : screen.xMax;
        columnWidth = Mathf.Floor(Mathf.Clamp(spanMax - spanMin - 48f, 180f, 330f) * .5f) * 2f;
        columnHeight = Mathf.Floor(Mathf.Clamp(screen.height * .56f, 120f, 220f) * .5f) * 2f;
        float centerX = Mathf.Round((spanMin + spanMax) * .5f);
        if (liveRect.width <= 0f) centerX = Mathf.Round(screen.center.x);

        column = MakeRect("HiddenFile", overlay);
        column.anchorMin = column.anchorMax = new Vector2(.5f, .5f);
        column.pivot = new Vector2(.5f, .5f);
        column.sizeDelta = new Vector2(columnWidth, columnHeight);
        column.anchoredPosition = new Vector2(centerX - screen.center.x, Mathf.Round(screen.height * .02f));

        var viewport = MakeRect("Viewport", column);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        content = MakeRect("Log", viewport);
        Stretch(content);

        ruleTop = NewRaw("RuleTop", column, Ink);
        ruleBottom = NewRaw("RuleBottom", column, Ink);
        foreach (var rule in new[] { ruleTop, ruleBottom })
        {
            rule.texture = dashTexture;
            rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(.5f, .5f);
            rule.rectTransform.pivot = new Vector2(.5f, .5f);
        }
        caret = NewImage("Caret", column, Ink);
        caret.rectTransform.anchorMin = caret.rectTransform.anchorMax = new Vector2(.5f, .5f);
        caret.rectTransform.pivot = new Vector2(0f, 0f);
        caret.rectTransform.sizeDelta = new Vector2(4f, Mathf.Round(lineSize * .8f));
        dot = NewImage("Point", column, Ink);
        dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(.5f, .5f);
        dot.rectTransform.sizeDelta = new Vector2(2f, 2f);
        dot.enabled = caret.enabled = false;

        MeasureGutter();
    }

    Rect LiveBounds()
    {
        var live = OS_Handle.Instance?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (live == null || !live.IsOpen || live.IsMinimized || overlay == null) return Rect.zero;
        var corners = new Vector3[4];
        live.WindowRect.GetWorldCorners(corners);
        Vector2 min = overlay.InverseTransformPoint(corners[0]), max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 p = overlay.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, p); max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // ---------------------------------------------------------------- watchers

    /// <summary>Live and the red pointer stay above the dark, as if watching with the player.</summary>
    void LiftWatchers()
    {
        if (overlay == null) return;
        var os = OS_Handle.Instance;
        var live = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (live != null)
        {
            if (!live.IsOpen && !live.IsOpening && !live.IsClosing && phase == Phase.LightsOut)
                os.OpenCharacterWindowForReconnect(.15f);
            Raise(live.transform as RectTransform, overlaySortingOrder + 1);
            var pointer = live.transform.parent != null ? live.transform.parent.Find(LivePointerName) as RectTransform : null;
            if (pointer != null) Raise(pointer, overlaySortingOrder + 2);
        }
    }

    void Raise(RectTransform target, int order)
    {
        if (target == null) return;
        foreach (var existing in lifts) if (existing.target == target) return;
        var parentCanvas = target.parent != null ? target.parent.GetComponentInParent<Canvas>() : null;
        var lift = new Lift { target = target, canvas = target.GetComponent<Canvas>() };
        lift.added = lift.canvas == null;
        if (lift.added) lift.canvas = target.gameObject.AddComponent<Canvas>();
        else
        {
            lift.overrideSorting = lift.canvas.overrideSorting; lift.order = lift.canvas.sortingOrder;
            lift.layer = lift.canvas.sortingLayerID; lift.enabled = lift.canvas.enabled;
        }
        lift.canvas.enabled = true;
        lift.canvas.overrideSorting = true;
        if (parentCanvas != null) lift.canvas.sortingLayerID = parentCanvas.sortingLayerID;
        lift.canvas.sortingOrder = order;
        lifts.Add(lift);
    }

    void LowerWatchers()
    {
        foreach (var lift in lifts)
        {
            if (lift.canvas == null) continue;
            if (lift.added) Destroy(lift.canvas);
            else
            {
                lift.canvas.overrideSorting = lift.overrideSorting; lift.canvas.sortingOrder = lift.order;
                lift.canvas.sortingLayerID = lift.layer; lift.canvas.enabled = lift.enabled;
            }
        }
        lifts.Clear();
    }

    // ---------------------------------------------------------------- misc

    void TypeTick()
    {
        // Keys are not that fast: thin the ticks out on quick localizations.
        if (clock - lastTypeSound < .045f) return;
        lastTypeSound = clock;
        var clips = typeClips != null && typeClips.Length > 0 ? typeClips : SynthesizedTypeClips();
        typeSerial++;
        var clip = clips[(typeSerial * 7 + (int)(Hash(typeSerial, 31) * 5f)) % clips.Length];
        if (clip == null || AudioManager.Instance == null) return;
        AudioManager.Instance.PlaySfx(clip, typeVolume, .92f + Hash(typeSerial, 97) * .16f);
    }

    /// <summary>
    /// Three short mechanical clicks: a bright noise transient over a small low thump,
    /// each from a different seed so a run of keys does not sound like one sample.
    /// </summary>
    AudioClip[] SynthesizedTypeClips()
    {
        if (synthesizedTypeClips != null) return synthesizedTypeClips;
        const int rate = 44100;
        synthesizedTypeClips = new AudioClip[3];
        for (int v = 0; v < synthesizedTypeClips.Length; v++)
        {
            int length = Mathf.RoundToInt(rate * (.034f + v * .004f));
            var data = new float[length];
            var random = new System.Random(1931 + v * 17);
            float low = 0f, previous = 0f;
            float thump = 140f + v * 25f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                // Crude high-pass keeps the transient crisp rather than hissy.
                float high = noise - previous; previous = noise;
                low += (noise - low) * .08f;
                float click = high * Mathf.Exp(-t * 520f) * .55f;
                float body = low * Mathf.Exp(-t * 160f) * .9f;
                float knock = Mathf.Sin(2f * Mathf.PI * thump * t) * Mathf.Exp(-t * 90f) * .35f;
                data[i] = Mathf.Clamp((click + body + knock) * .8f, -1f, 1f);
            }
            var clip = AudioClip.Create("HiddenMemoryKey" + v, length, 1, rate, false);
            clip.SetData(data, 0);
            synthesizedTypeClips[v] = clip;
        }
        return synthesizedTypeClips;
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || AudioManager.Instance == null) return;
        voices.RemoveAll(v => v.source == null || v.source.clip != v.clip || (!v.source.isPlaying && !v.paused));
        var source = AudioManager.Instance.PlaySfx(clip, volume);
        if (source != null) voices.Add(new Voice { source = source, clip = clip });
    }

    public void ResetPresentation()
    {
        foreach (var voice in voices) if (voice.source != null && voice.source.clip == voice.clip) voice.source.Stop();
        voices.Clear();
        LowerWatchers();
        if (overlay != null) { overlay.gameObject.SetActive(false); Destroy(overlay.gameObject); }
        rows.Clear();
        overlay = column = content = null;
        shade = ruleTop = ruleBottom = null; caret = dot = null;
        lineCompleted = null; completed = null;
        phase = Phase.None; paused = resumed = false;
        beatIndex = visibleCharacters = nextKnock = 0; tick = -1;
        phaseTime = clock = reveal = readTime = knockPulse = fallPulse = 0f;
    }

    void OnDisable() { ResetPresentation(); }
    void OnDestroy()
    {
        ResetPresentation();
        if (ditherLevels != null) foreach (var tex in ditherLevels) if (tex != null) Destroy(tex);
        if (dashTexture != null) Destroy(dashTexture);
        if (synthesizedTypeClips != null) foreach (var clip in synthesizedTypeClips) if (clip != null) Destroy(clip);
        ditherLevels = null; dashTexture = null; synthesizedTypeClips = null;
    }

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    static RawImage NewRaw(string name, RectTransform parent, Color color)
    {
        var rect = MakeRect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var image = rect.gameObject.AddComponent<RawImage>();
        image.color = color; image.raycastTarget = false;
        return image;
    }

    static Image NewImage(string name, RectTransform parent, Color color)
    {
        var rect = MakeRect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false;
        return image;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
