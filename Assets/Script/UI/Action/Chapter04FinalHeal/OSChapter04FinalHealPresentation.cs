using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Round 11, the last heart treatment: the screen only. The backend owns the
/// chart, the input, the judgement, the numbers and saving; it calls this to
/// show what happened. Times of notes and words are on the caller's music clock
/// (SetMusicClock, AudioSettings.dspTime by default); the presentation's own
/// animations (pulse, rings, falling letters, ending) run on a pausable clock.
///
/// The whole layer is one 640 x 360 point texture laid 1:1 over the OS canvas
/// and drawn on the CPU at 30 fps: every pixel lands on the OS grid, nothing is
/// anti-aliased or scaled, and only black, gray, light gray and red are used.
/// Back to front: the Bayer dimming over the OS, the lane, the heart, the hit
/// rings, the flying words and letters, the ending.
/// </summary>
public sealed partial class OSChapter04FinalHealPresentation : MonoBehaviour
{
    public const int Width = 640, Height = 360;
    public const int HeartCenterX = 320, HeartCenterY = 132, HeartWidth = 140, HeartHeight = 172;
    public const int HeartLeft = HeartCenterX - HeartWidth / 2, HeartTop = HeartCenterY - HeartHeight / 2;
    public const int StageCount = 4;
    // Above the grave grief dither (32000), below the grief failure warning (32100).
    public const int SortingOrder = 32050;
    // Beat types and the sides words fly in from (SideHeart: rises from under the heart, D2).
    public const int TypeTap = 0, TypeHold = 1, TypeHeavy = 2;
    public const int SideLeft = 0, SideRight = 1, SideTop = 2, SideBottom = 3, SideHeart = 4;

    public static readonly Color32 Clear = new Color32(0, 0, 0, 0), Black = new Color32(0, 0, 0, 255);
    public static readonly Color32 Gray = new Color32(0x87, 0x82, 0x7e, 255), Light = new Color32(0xB5, 0xAE, 0xA5, 255);
    public static readonly Color32 Red = new Color32(0xD8, 0x34, 0x3A, 255);
    // Bayer 4x4, [y % 4][x % 4] (the theater's).
    static byte[] Bayer => OSChapter04TheaterPiece.Bayer;

    const string HeartFolder = "Chapter04/FinalHeal/Heart/heart_";
    static readonly string[] StageNames = { "0_shattered", "1_weak", "2_steady", "3_whole" };
    // Frame indices: idle 0-2, then the pulse and the miss.
    static readonly string[] FrameNames = { "idle0", "idle1", "idle2", "hit0_flash", "hit1", "hit2", "hit3_in", "miss" };
    const int FrameHit0 = 3, FrameHit1 = 4, FrameHit2 = 5, FrameHitIn = 6, FrameMiss = 7;
    const float Fps = 30f, IdleFps = 9f, FrozenIdleFps = 4f, MissFrames = 5f, StageSeconds = .4f, BeginSeconds = .4f;
    // Dither thresholds (of 16): one step darker than the grave's tier 3 (8), one more while frozen.
    const int BaseDim = 10, FrozenDim = 12;
    // Hit rings: from 1 px to the radius in a quarter second; the sinking second part shrinks them.
    const float RingSeconds = .25f, PhraseRing = 90f, PhraseRingSunk = 30f, BeatRing = 60f, BeatRingSunk = 20f;

    public static OSChapter04FinalHealPresentation Current { get; private set; }

    /// <summary>The presentation on the OS handle, created on first use.</summary>
    public static OSChapter04FinalHealPresentation Ensure()
    {
        if (Current != null) return Current;
        var host = OS_Handle.Instance;
        if (host == null) return null;
        var found = host.GetComponent<OSChapter04FinalHealPresentation>();
        return found != null ? found : host.gameObject.AddComponent<OSChapter04FinalHealPresentation>();
    }

    /// <summary>The ending's last line has scrolled by (PlayEnding).</summary>
    public event Action EndingFinished;
    /// <summary>A press on the treatment screen, in OS pixels from the top left (the layer blocks the OS below).</summary>
    public event Action<Vector2> Pressed;

    bool visible, paused, frozen;
    float pauseStartedAt, pausedSeconds, beganAt = -10f;
    Func<double> musicClock;
    Func<float> presentationClock;

    public bool IsVisible => visible;
    public bool IsPaused => paused;
    public bool IsFrozen => frozen;
    public int HeartStage => stage;
    /// <summary>The caller's music time (the clock hitTime is on).</summary>
    public double MusicTime => musicClock != null ? musicClock() : AudioSettings.dspTime;
    float Now => presentationClock != null ? presentationClock() : Time.unscaledTime;
    /// <summary>The presentation's own clock: stops while paused and during a hit stop.</summary>
    public float PresentationTime
    {
        get
        {
            float raw = RawTime;
            if (stopStart < 0f) return raw;
            if (raw < stopEnd) return stopStart;
            pausedSeconds += stopEnd - stopStart;      // the stop is over: from now on it is simply skipped
            stopStart = stopEnd = -1f;
            return RawTime;
        }
    }
    float RawTime => (paused ? pauseStartedAt : Now) - pausedSeconds;
    float stopStart = -1f, stopEnd = -1f;

    /// <summary>Hold this layer's animation still for a moment (a hit stop); the music clock runs on.</summary>
    void HitStop(float seconds)
    {
        float now = PresentationTime;                  // settles any stop already over
        stopStart = now; stopEnd = now + seconds;
    }

    /// <summary>A hit stop is holding the layer (QA).</summary>
    public bool IsHitStopped => stopStart >= 0f && RawTime < stopEnd;

    void Awake() => Current = this;
    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (layerRoot != null) Destroy(layerRoot.gameObject);
        DestroyTextLayers();
        if (texture != null) Destroy(texture);
    }

    // ================================================================ API

    /// <summary>The music clock note and word times are on (null: AudioSettings.dspTime).</summary>
    public void SetMusicClock(Func<double> clock) => musicClock = clock;

    /// <summary>QA: replace the presentation clock (null: unscaled time). Resets the pause bookkeeping.</summary>
    public void SetPresentationClock(Func<float> clock)
    {
        presentationClock = clock;
        paused = false; pausedSeconds = 0f; pauseStartedAt = Now; stopStart = stopEnd = -1f;
        lastFrame = -1;
    }

    /// <summary>Dim the OS, place the heart and the lane. Restoring lands on the dimmed look at once.</summary>
    public void Begin(bool restoring)
    {
        Build();
        if (layerRoot == null) return;
        visible = true;
        layerRoot.gameObject.SetActive(true);
        layerRoot.SetAsLastSibling();
        SetTextLayersActive(true);
        textRoot?.SetAsLastSibling(); maskRoot?.SetAsLastSibling();
        beganAt = restoring ? PresentationTime - BeginSeconds : PresentationTime;
        lastFrame = -1;
        Render();
    }

    /// <summary>Hide the treatment screen (after the ending, or leaving it). State stays until ClearAll.</summary>
    public void End()
    {
        visible = false;
        StopAllHoldLoops();
        LiftCursors(false);
        if (layerRoot != null) layerRoot.gameObject.SetActive(false);
        SetTextLayersActive(false);
    }

    /// <summary>Hide and forget everything: notes, words, letters, stage, ending.</summary>
    public void ClearAll()
    {
        End();
        notes.Clear(); phrases.Clear(); particles.Clear(); rings.Clear(); answers.Clear();
        edgeFlashAt = -10f; slashes.Clear(); answersStandUntil = -10f;
        ResetLaneFeedback();
        stopStart = stopEnd = -1f;
        stage = 0; fromStage = -1; stageAt = -10f;
        hitAt = missAt = -10f;
        frozen = false; sink = 0f;
        ResetEnding();
        ResetEpilogue();
        ClearAnswerLabels();
        ResetPhraseCounters();
        shownStages.Clear();
        SetPaused(false);
    }

    /// <summary>Freeze the presentation clock (pause menu); the caller pauses its music clock.</summary>
    public void SetPaused(bool value)
    {
        if (paused == value) return;
        if (value) pauseStartedAt = Now;
        else pausedSeconds += Mathf.Max(0f, Now - pauseStartedAt);
        paused = value;
        PauseHoldLoops(value);       // round 11g: the hold loop sound pauses with the show
    }

    /// <summary>
    /// Heart stage 0 shattered .. 3 whole. Animate: a 0.4 s Bayer dissolve,
    /// outward from the heart's center going up, inward going down.
    /// </summary>
    public void SetHeartStage(int value, bool animate)
    {
        value = Mathf.Clamp(value, 0, StageCount - 1);
        if (value == stage) return;
        fromStage = animate ? stage : -1;
        stageAt = PresentationTime;
        stage = value;
    }

    /// <summary>The heart's pulse (also played by every hit).</summary>
    public void PlayHeartHit() { hitAt = PresentationTime; missAt = -10f; }
    /// <summary>The heart's miss frame.</summary>
    public void PlayHeartMiss() { missAt = PresentationTime; hitAt = -10f; }

    /// <summary>
    /// A plain beat in the lane (no words): type 0 tap, 1 hold (holdSeconds long),
    /// 2 heavy. An id still on the lane is replaced.
    /// </summary>
    public void SpawnBeat(string id, int type, double hitTime, double holdSeconds = 0d) =>
        AddNote(id, type, hitTime, holdSeconds, false);

    void AddNote(string id, int type, double hitTime, double holdSeconds, bool framed, double leadSeconds = 0d)
    {
        RemoveNote(id);
        type = Mathf.Clamp(type, TypeTap, TypeHeavy);
        notes.Add(new Note
        {
            id = id, type = type, hitTime = hitTime, framed = framed,
            lead = leadSeconds > 0d ? leadSeconds : LaneLeadSeconds,
            holdSeconds = type == TypeHold ? Math.Max(0d, holdSeconds) : 0d
        });
    }

    /// <summary>
    /// The sinking second part, 0..1: hit rings shrink from 90 to 30 px (plain
    /// beats 60 to 20); at 1 only a dotted ring is left.
    /// </summary>
    public void SetSinkProgress(float value) => sink = Mathf.Clamp01(value);
    public float SinkProgress => sink;
    float sink;

    /// <summary>
    /// The end of the second part: every word string stops where it is, every
    /// lane note still waiting is cleared, the screen goes a step darker and the
    /// heart's idle slows to 4 fps. Strings stay until KnockDown, ResolveHit/Miss
    /// or ClearAll.
    /// </summary>
    public void FreezeAll()
    {
        if (frozen) return;
        frozen = true;
        double now = MusicTime;
        foreach (var phrase in phrases)
            if (!phrase.frozen && phrase.Alive) { phrase.frozenDistance = phrase.Distance(now); phrase.frozen = true; }
        notes.RemoveAll(note => note.state != NoteState.Missed);
    }

    /// <summary>The third part starts: the screen and the idle come back; frozen strings keep hanging.</summary>
    public void ReleaseFreeze() => frozen = false;

    /// <summary>
    /// A hit (the note and/or the words with this id): the heart pulses, a red
    /// ring spreads, self-blame letters turn gray and fall one by one, the
    /// player's words turn red and fly into the heart one by one.
    /// </summary>
    public void ResolveHit(string id)
    {
        var note = FindNote(id);
        if (note != null) { LaneHit(note); notes.Remove(note); }
        var answer = FindAnswer(id);
        if (answer != null) { AnswerHit(answer); return; }
        var phrase = FindPhrase(id);
        if (phrase != null) HitPhrase(phrase);
        if (phrase != null && phrase.side == SideHeart) ReleaseWrecks();    // 「妈妈」: the stopped strings drop, the green lines rise
        float radius = phrase != null ? Mathf.Lerp(PhraseRing, PhraseRingSunk, sink) : Mathf.Lerp(BeatRing, BeatRingSunk, sink);
        AddRing(radius, RingSeconds, sink >= 1f);
        PlayHeartHit();
    }

    /// <summary>
    /// A miss: the note dims and slides on, the words break on the heart into
    /// gray specks, the heart misses, the screen edge flashes red.
    /// </summary>
    public void ResolveMiss(string id)
    {
        var note = FindNote(id);
        if (note != null) { LaneMiss(note); notes.Remove(note); }       // the note breaks (the old tool's miss shatter)
        var answer = FindAnswer(id);
        if (answer != null) { AnswerMiss(answer); return; }
        FlashEdge();
        var phrase = FindPhrase(id);
        if (phrase != null) ShatterPhrase(phrase);
        PlayHeartMiss();
    }

    /// <summary>
    /// A hold being held, t 0..1: the hold note's head stays on the judgement
    /// line and the words' letters nearest the heart are eaten one by one. A
    /// release too early is a ResolveMiss (the rest breaks on the heart).
    /// </summary>
    public void SetHoldProgress(string id, float t)
    {
        var note = FindNote(id);
        if (note != null && note.state == NoteState.Waiting) { note.holding = true; LaneHoldStart(note); }
        var answer = FindAnswer(id);
        if (answer != null) { AnswerHold(answer, Mathf.Clamp01(t)); return; }
        var phrase = FindPhrase(id);
        if (phrase != null) EatPhrase(phrase, Mathf.Clamp01(t));
    }

    // ================================================================ frame

    int lastFrame = -1;

    void Update()
    {
        if (!visible || texture == null) return;
        if (!paused) PollEpilogueInput();
        int frame = Mathf.FloorToInt(PresentationTime * Fps);
        if (frame == lastFrame) return;
        lastFrame = frame;
        Render();
    }

    /// <summary>Draw the layer now (Update does it 30 times a second; QA after moving its clocks).</summary>
    public void Render()
    {
        if (texture == null) return;
        float t = PresentationTime;
        double now = MusicTime;
        AdvanceEnding(t);
        StepEpilogue(t);
        BeginMask();
        SyncLabelFonts();
        FillDim(DimLevel(t));
        StepCrashes(t);
        if (!EndingCoversScreen(t))
        {
            DrawLane(now);
            DrawHeart(t);
            DrawRings(t);
            DrawPhrases(now);
            DrawAnswers(t);
            DrawSlashes(t);
        }
        // Letters keep falling (and leaving) under the ending's black too.
        DrawParticles(t);
        if (!EndingCoversScreen(t)) DrawEdgeFlash(t);
        DrawEpilogue(t);
        DrawEnding(t);
        // the green lines leave with the black (the dialogue's own black, or the ending's)
        SyncAnswerLabels(t, EndingCoversScreen(t) || epPhase > EpiloguePhase.Sinking);
        EndMask();
        texture.SetPixels32(buffer);
        texture.Apply(false);
        renderedFrames++;
    }

    int DimLevel(float t)
    {
        int target = frozen ? FrozenDim : BaseDim;
        float fade = Mathf.Clamp01((t - beganAt) / BeginSeconds);
        return Mathf.RoundToInt(target * fade / 2f) * 2;
    }

    // ================================================================ canvas

    RectTransform layerRoot;
    Canvas layerCanvas;
    RawImage image;
    Texture2D texture;
    readonly Color32[] buffer = new Color32[Width * Height];
    int renderedFrames;

    public Texture2D LayerTexture => texture;
    public RectTransform LayerRect => layerRoot;
    public Canvas LayerCanvas => layerCanvas;
    public int RenderedFrames => renderedFrames;

    /// <summary>The pixel at OS (x, y), top-left origin, as last drawn (QA).</summary>
    public Color32 GetPixel(int x, int y) =>
        (uint)x < Width && (uint)y < Height ? buffer[(Height - 1 - y) * Width + x] : Clear;

    void Put(int x, int y, Color32 color)
    {
        if ((uint)x < Width && (uint)y < Height) buffer[(Height - 1 - y) * Width + x] = color;
    }

    Transform LayerParent
    {
        get
        {
            var window = OS_Handle.Instance?.CharacterWindowController;
            var canvas = window != null ? window.GetComponentInParent<Canvas>() : null;
            return canvas != null ? canvas.rootCanvas.transform : null;
        }
    }

    void Build()
    {
        if (layerRoot != null) return;
        var parent = LayerParent;
        if (parent == null) return;
        var node = new GameObject("Chapter04FinalHeal", typeof(RectTransform));
        node.layer = parent.gameObject.layer;
        layerRoot = (RectTransform)node.transform;
        layerRoot.SetParent(parent, false);
        // Exactly the OS canvas: 640 x 360 reference pixels, centered, 1:1.
        layerRoot.anchorMin = layerRoot.anchorMax = layerRoot.pivot = new Vector2(.5f, .5f);
        layerRoot.anchoredPosition = Vector2.zero;
        layerRoot.sizeDelta = new Vector2(Width, Height);
        layerCanvas = node.AddComponent<Canvas>();
        layerCanvas.overrideSorting = true; layerCanvas.sortingOrder = SortingOrder;
        node.AddComponent<GraphicRaycaster>();
        image = node.AddComponent<RawImage>();
        image.raycastTarget = true;
        texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave, name = "Chapter04FinalHeal" };
        image.texture = texture;
        node.AddComponent<OSChapter04FinalHealInput>().Initialize(this);
        node.SetActive(false);
        BuildTextLayers(parent);
        SetTextLayersActive(false);
    }

    internal void NotifyPressed(Vector2 osPixel) => Pressed?.Invoke(osPixel);

    // ---- dimming: black where Bayer < level, clear elsewhere
    int filledDim = -1;
    Color32[] dimPattern;

    void FillDim(int level)
    {
        if (dimPattern == null || filledDim != level)
        {
            dimPattern = dimPattern ?? new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    dimPattern[(Height - 1 - y) * Width + x] = Bayer[(y & 3) * 4 + (x & 3)] < level ? Black : Clear;
            filledDim = level;
        }
        Array.Copy(dimPattern, buffer, buffer.Length);
    }

    // ================================================================ heart

    int stage, fromStage = -1;
    float stageAt = -10f, hitAt = -10f, missAt = -10f;
    readonly Dictionary<int, Color32[]> frames = new Dictionary<int, Color32[]>();
    readonly HashSet<int> shownStages = new HashSet<int>();
    float[] riseKeys, fallKeys;

    /// <summary>Stages the heart has been drawn in (QA).</summary>
    public IReadOnlyCollection<int> ShownStages => shownStages;
    /// <summary>The heart frame drawn last, like "2_steady_hit1" (QA).</summary>
    public string LastHeartFrame { get; private set; }
    /// <summary>Pixels of the old stage still shown by the last dissolve frame (QA).</summary>
    public int LastDissolveOldPixels { get; private set; }

    /// <summary>Frame pixels (Unity rows, bottom-up), loaded once; null when missing.</summary>
    Color32[] Frame(int stageIndex, int frame)
    {
        int key = stageIndex * 16 + frame;
        if (frames.TryGetValue(key, out var cached)) return cached;
        var source = Resources.Load<Texture2D>(HeartFolder + StageNames[stageIndex] + "_" + FrameNames[frame]);
        Color32[] pixels = null;
        if (source != null && source.width == HeartWidth && source.height == HeartHeight)
        {
            try { pixels = source.GetPixels32(); }
            catch (Exception error) { Debug.LogWarning("Final heal heart frame is not readable: " + source.name + " " + error.Message); }
        }
        frames[key] = pixels;
        return pixels;
    }

    int HeartFrameIndex(float t)
    {
        float hit = (t - hitAt) * Fps;
        if (hit >= 0f && hit < 4.5f) return hit < 1f ? FrameHit0 : hit < 2f ? FrameHit1 : hit < 3f ? FrameHit2 : FrameHitIn;
        float miss = (t - missAt) * Fps;
        if (miss >= 0f && miss < MissFrames) return FrameMiss;
        return Mathf.FloorToInt(t * (frozen ? FrozenIdleFps : IdleFps)) % 3;
    }

    void DrawHeart(float t)
    {
        if (heartGone) return;                       // round 11h: burst into particles
        int frame = HeartFrameIndex(t);
        var current = Frame(stage, frame);
        if (current == null) return;
        float progress = fromStage >= 0 ? (t - stageAt) / StageSeconds : 1f;
        Color32[] before = null;
        float[] keys = null;
        if (progress < 1f)
        {
            before = Frame(fromStage, frame);
            BuildDissolveKeys();
            keys = stage > fromStage ? riseKeys : fallKeys;
            shownStages.Add(fromStage);
        }
        else fromStage = -1;
        shownStages.Add(stage);
        LastHeartFrame = StageNames[stage] + "_" + FrameNames[frame];
        if (before == null && frame <= 2 && HeartPressSwell(t))
        {
            DrawHeartSwollen(current);
            LastHeartFrame += "+press";
            LastDissolveOldPixels = 0;
            return;
        }
        int old = 0;
        for (int row = 0; row < HeartHeight; row++)
        {
            int y = HeartTop + (HeartHeight - 1 - row);
            for (int x = 0; x < HeartWidth; x++)
            {
                int i = row * HeartWidth + x;
                bool keepOld = before != null && keys[i] >= progress;
                var pixel = keepOld ? before[i] : current[i];
                if (keepOld && pixel.a >= 128) old++;
                if (pixel.a < 128) continue;
                Put(HeartLeft + x, y, pixel);
            }
        }
        LastDissolveOldPixels = old;
    }

    /// <summary>
    /// A press's light beat: the idle frame's halves moved 1 px apart each way,
    /// the two middle columns and rows repeated, so nothing is scaled.
    /// </summary>
    void DrawHeartSwollen(Color32[] pixels)
    {
        int midX = HeartWidth / 2, midY = HeartHeight / 2;
        for (int y = -1; y <= HeartHeight; y++)
        {
            int sy = y <= midY - 2 ? y + 1 : y >= midY + 1 ? y - 1 : y;
            for (int x = -1; x <= HeartWidth; x++)
            {
                int sx = x <= midX - 2 ? x + 1 : x >= midX + 1 ? x - 1 : x;
                var pixel = pixels[(HeartHeight - 1 - sy) * HeartWidth + sx];
                if (pixel.a >= 128) Put(HeartLeft + x, HeartTop + y, pixel);
            }
        }
    }

    /// <summary>
    /// Per-pixel switch times of the dissolve (0..1): three quarters by distance
    /// from the heart's center, one quarter by the Bayer cell, so the new stage
    /// spreads in dither from the center (rising) or from the rim (falling).
    /// </summary>
    void BuildDissolveKeys()
    {
        if (riseKeys != null) return;
        int n = HeartWidth * HeartHeight;
        riseKeys = new float[n]; fallKeys = new float[n];
        float cx = HeartWidth / 2f, cy = HeartHeight / 2f;
        float far = Mathf.Sqrt(cx * cx + cy * cy);
        for (int row = 0; row < HeartHeight; row++)
            for (int x = 0; x < HeartWidth; x++)
            {
                int y = HeartHeight - 1 - row;
                float d = Mathf.Sqrt((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy)) / far;
                float dither = (Bayer[((HeartTop + y) & 3) * 4 + ((HeartLeft + x) & 3)] + .5f) / 16f;
                riseKeys[row * HeartWidth + x] = .75f * d + .25f * dither;
                fallKeys[row * HeartWidth + x] = .75f * (1f - d) + .25f * dither;
            }
    }

    // ================================================================ rings

    sealed class Ring { public float at, radius, seconds; public bool dotted; }
    readonly List<Ring> rings = new List<Ring>();

    /// <summary>Hit rings still spreading (QA).</summary>
    public int RingCount => rings.Count;

    void AddRing(float radius, float seconds, bool dotted) =>
        rings.Add(new Ring { at = PresentationTime, radius = radius, seconds = seconds, dotted = dotted });

    void DrawRings(float t)
    {
        rings.RemoveAll(ring => t - ring.at >= ring.seconds);
        foreach (var ring in rings)
        {
            float e = Mathf.Clamp01((t - ring.at) / ring.seconds);
            Circle(HeartCenterX, HeartCenterY, Mathf.RoundToInt(1f + (ring.radius - 1f) * e), Red, ring.dotted);
        }
    }

    /// <summary>A 1 px midpoint circle; dotted keeps half the points (Bayer &lt; 8).</summary>
    void Circle(int cx, int cy, int r, Color32 color, bool dotted)
    {
        int x = r, y = 0, error = 1 - r;
        while (x >= y)
        {
            Plot8(cx, cy, x, y, color, dotted);
            y++;
            if (error < 0) error += 2 * y + 1;
            else { x--; error += 2 * (y - x) + 1; }
        }
    }

    void Plot8(int cx, int cy, int x, int y, Color32 color, bool dotted)
    {
        PlotDot(cx + x, cy + y, color, dotted); PlotDot(cx - x, cy + y, color, dotted);
        PlotDot(cx + x, cy - y, color, dotted); PlotDot(cx - x, cy - y, color, dotted);
        PlotDot(cx + y, cy + x, color, dotted); PlotDot(cx - y, cy + x, color, dotted);
        PlotDot(cx + y, cy - x, color, dotted); PlotDot(cx - y, cy - x, color, dotted);
    }

    void PlotDot(int x, int y, Color32 color, bool dotted)
    {
        if (dotted && Bayer[(y & 3) * 4 + (x & 3)] >= 8) return;
        Put(x, y, color);
    }

    // ================================================================ lane

    // Copied from the first heart-heal tool's track (OSList_02_1): a 1 px gray
    // frame on a dotted black panel, an inner lane bracketed top and bottom,
    // a center line; notes are the tool's 11 px rounded squares, right to left.
    public const int LaneLeft = 100, LaneRight = 540, LaneTop = 290, LaneBottom = 340;
    public const int LaneInnerTop = 300, LaneInnerBottom = 330, LaneCenterY = 315, JudgeX = 320;
    public const float LanePixelsPerSecond = 107f;
    /// <summary>Seconds a note normally takes from the lane's right end to the judgement line.</summary>
    public const float LaneLeadSeconds = (NoteClipRight - JudgeX) / LanePixelsPerSecond;
    const int NoteHalf = 5, NoteClipLeft = LaneLeft + 6, NoteClipRight = LaneRight - 6;

    enum NoteState { Waiting, Missed }
    sealed class Note
    {
        public string id;
        public int type;
        public double hitTime, holdSeconds, lead;
        public bool framed, holding, cracked;   // cracked: a heavy beat's shell broken by its first hit
        public NoteState state;
    }
    readonly List<Note> notes = new List<Note>();

    /// <summary>Lane notes on the lane (QA).</summary>
    public int LaneNoteCount => notes.Count;

    Note FindNote(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = notes.Count - 1; i >= 0; i--) if (notes[i].id == id) return notes[i];
        return null;
    }

    void RemoveNote(string id) { if (!string.IsNullOrEmpty(id)) notes.RemoveAll(note => note.id == id); }

    /// <summary>Where a time sits on the lane for a note that crosses it in lead seconds.</summary>
    static int LaneX(double time, double now, double lead) =>
        JudgeX + (int)Math.Round((time - now) * (NoteClipRight - JudgeX) / Math.Max(.05d, lead));

    void DrawLane(double now)
    {
        // panel: frame and dot grid on black
        for (int y = LaneTop; y <= LaneBottom; y++)
            for (int x = LaneLeft; x <= LaneRight; x++)
            {
                bool edge = y == LaneTop || y == LaneBottom || x == LaneLeft || x == LaneRight;
                bool inner = y > LaneInnerTop && y < LaneInnerBottom && x > LaneLeft + 3 && x < LaneRight - 3;
                bool dot = !inner && (x & 3) == 2 && (y & 3) == 2;
                Put(x, y, edge || dot ? Gray : Black);
            }
        // inner lane: top and bottom rules with corner ticks, a dotted center line
        for (int x = LaneLeft + 4; x <= LaneRight - 4; x++) { Put(x, LaneInnerTop, Gray); Put(x, LaneInnerBottom, Gray); }
        for (int k = 1; k <= 2; k++)
        {
            Put(LaneLeft + 4, LaneInnerTop + k, Gray); Put(LaneRight - 4, LaneInnerTop + k, Gray);
            Put(LaneLeft + 4, LaneInnerBottom - k, Gray); Put(LaneRight - 4, LaneInnerBottom - k, Gray);
        }
        for (int x = LaneLeft + 8; x <= LaneRight - 8; x += 2) Put(x, LaneCenterY, Gray);
        // judgement line with a 2 x 2 red dot above it
        for (int y = LaneInnerTop + 1; y < LaneInnerBottom; y++) Put(JudgeX, y, Gray);
        for (int y = LaneInnerTop - 3; y <= LaneInnerTop - 2; y++) { Put(JudgeX, y, Red); Put(JudgeX + 1, y, Red); }

        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var note = notes[i];
            int head = note.holding || note.cracked ? JudgeX : NoteHead(note, now);
            int tail = note.holdSeconds > 0d ? Math.Max(head, LaneX(note.hitTime + note.holdSeconds, now, note.lead)) : head;
            if (tail + NoteHalf + 3 < NoteClipLeft) { notes.RemoveAt(i); continue; }   // slid off the left end
            if (head - NoteHalf - 3 > NoteClipRight) continue;
            DrawNote(note, head, tail);
            if (FastFlash(note, now)) FxRect(head - NoteHalf, tail + NoteHalf, LaneCenterY - NoteHalf, LaneCenterY + NoteHalf, Light, 16, true);
        }
        DrawLaneFeedback(PresentationTime);
    }

    void DrawNote(Note note, int head, int tail)
    {
        bool dotted = note.state == NoteState.Missed;
        int top = LaneCenterY - NoteHalf, bottom = LaneCenterY + NoteHalf;
        // the tool's rounded square (corner pixels cut), stretched for a hold
        NoteRect(head - NoteHalf, tail + NoteHalf, top, bottom, Gray, dotted, true);
        if (note.type == TypeHeavy && !note.cracked && !IsFast(note))
        {
            // a heavy beat wears a shell (a black ring inside, a gray core) until its first hit;
            // the third part's fast cuts are one hit each and draw no shell
            NoteRect(head - NoteHalf + 2, tail + NoteHalf - 2, top + 2, bottom - 2, Black, false, false);
            NoteRect(head - NoteHalf + 4, tail + NoteHalf - 4, top + 4, bottom - 4, Gray, dotted, false);
        }
        if (note.framed)
        {
            // a beat that carries words: one more 1 px frame outside
            int l = head - NoteHalf - 2, r = tail + NoteHalf + 2, t = top - 2, b = bottom + 2;
            for (int x = l + 1; x < r; x++) { NotePixel(x, t, Light, dotted); NotePixel(x, b, Light, dotted); }
            for (int y = t + 1; y < b; y++) { NotePixel(l, y, Light, dotted); NotePixel(r, y, Light, dotted); }
        }
    }

    void NoteRect(int left, int right, int top, int bottom, Color32 color, bool dotted, bool roundCorners)
    {
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                if (roundCorners && (x == left || x == right) && (y == top || y == bottom)) continue;
                NotePixel(x, y, color, dotted);
            }
    }

    void NotePixel(int x, int y, Color32 color, bool dotted)
    {
        if (x < NoteClipLeft || x > NoteClipRight) return;
        if (dotted && Bayer[(y & 3) * 4 + (x & 3)] >= 8) color = Black;
        Put(x, y, color);
    }
}

/// <summary>Forwards presses on the final heal layer, in OS pixels from the top left.</summary>
public sealed class OSChapter04FinalHealInput : MonoBehaviour, IPointerDownHandler
{
    OSChapter04FinalHealPresentation owner;
    public void Initialize(OSChapter04FinalHealPresentation owner) => this.owner = owner;
    public void OnPointerDown(PointerEventData data)
    {
        if (owner == null) return;
        var rect = (RectTransform)transform;
        var canvas = GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, cam, out var local)) return;
        owner.NotifyPressed(new Vector2(Mathf.Floor(local.x + OSChapter04FinalHealPresentation.Width / 2f),
            Mathf.Floor(OSChapter04FinalHealPresentation.Height / 2f - local.y)));
    }
}
