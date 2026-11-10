using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Round 11h, after the treatment (pancake, 11-03). Replaces the black screen
/// with thank-you lines; the postcard, logo and staff stay.
///   1. As the calm loop comes in (5.25 s after the second 「妈」): the heart
///      bursts into 1-bit particles (its own pixels, 1 px and 2 px squares,
///      thrown out with drag and gravity, gone within about 2 s) while the
///      whole screen sinks to black one Bayer level at a time (2.5 s).
///   2. Half a second into the black, Live's small window dithers in (0.6 s):
///      her chat avatar at twice its size, her lines typed with the chat's
///      rhythm and typing voice; the red pointer (the player's cursor in red,
///      drawn here) rests by her. Where the script
///      asks, 1-3 reply buttons sit under her line; the player clicks one with
///      their own cursor (lifted above this layer for the dialogue), and she may
///      answer it before going on.
///   3. The words and the pointer dither away; the window closes like an OS
///      window (it shrinks a little and fades, 0.32 s).
///   4. The postcard slides up; under it a small hint breathes until a click or
///      Space; then the postcard slides away and the logo and staff scroll;
///      EndingFinished.
/// Everything is drawn on this layer's 640 x 360 grid like the rest of it.
/// </summary>
public sealed partial class OSChapter04FinalHealPresentation
{
    /// <summary>One step of the epilogue script: a line of Live's, or a set of the player's replies.</summary>
    public sealed class EpilogueStep
    {
        public string text;
        public float holdSeconds = DefaultLineHold;
        public string[] options;
        public string[] replies;

        public bool IsChoice => options != null && options.Length > 0;
        /// <summary>A line Live types, then holds for holdSeconds before the next step.</summary>
        public static EpilogueStep Line(string text, float holdSeconds = DefaultLineHold) =>
            new EpilogueStep { text = text ?? string.Empty, holdSeconds = holdSeconds };
        /// <summary>1-3 replies under Live's current line; replies[i] (optional) is what Live types after option i.</summary>
        public static EpilogueStep Choice(string[] options, string[] replies = null) =>
            new EpilogueStep { options = options, replies = replies };
    }

    /// <summary>The player picked a reply: (step index in the script, option index).</summary>
    public event Action<int, int> EpilogueChoice;

    public const string PostcardHintKey = "chapter04.final_heal.ending.postcard_hint";
    const string PostcardHintZh = "点击结束游戏";
    const float DefaultLineHold = 1.2f, EpilogueDimSeconds = 2.5f, WindowAppearDelay = .5f, WindowRevealSeconds = .6f;
    const float WordsFadeSeconds = .4f, WindowCloseSeconds = .32f, ReplyHold = 1.2f, LiveTypeRate = 40f;
    const float DotSeconds = .12f, DotRunHold = .45f, CommaHold = .15f;
    float epRate = LiveTypeRate;

    /// <summary>
    /// Round 11k, the epilogue only: letters a second by language (Chinese 40,
    /// Japanese and Korean 50, spaced scripts 70), quicker still for a long line
    /// so its letters take at most LetterSecondsCap and every language ends a
    /// line in about the same 2-3 s. Dot and comma pauses are unchanged.
    /// </summary>
    static float LineTypeRate(string text)
    {
        var language = GameDataManager.Instance != null ? GameDataManager.Instance.CurrentLanguageState : LanguageState.zh_CN;
        float rate = language == LanguageState.zh_CN || language == LanguageState.zh_TW ? 40f
            : language == LanguageState.ja || language == LanguageState.ko ? 50f : 70f;
        int letters = 0;
        foreach (char c in text ?? string.Empty) if (c != '.' && c != '…' && c != '⋯') letters++;
        return Mathf.Max(rate, letters / LetterSecondsCap);
    }
    const float LetterSecondsCap = 1.3f;
    const int WindowWidth = 300, WindowTop = 112, TitleHeight = 13, AvatarScale = 2, EpilogueTextHeight = 13, LineGap = 3;
    const int ButtonHeight = 16, ButtonGap = 8, OptionTextHeight = 11;

    enum EpiloguePhase { None, Sinking, Revealing, Talking, WordsFading, Closing, Postcard }
    EpiloguePhase epPhase;
    float epStart, epPhaseAt, epLineTyped, epHoldLeft, epCharWait, epClock = -1f;
    List<EpilogueStep> epScript;
    int epStep;
    string epText = string.Empty;          // Live's line in the window
    string[] epOptions;                    // replies shown now (null: none)
    int epHover = -1, epPressFrame = -1;
    bool epAwaitingReply, epTyping;
    readonly List<RectInt> epButtons = new List<RectInt>();

    /// <summary>Which part of the epilogue is showing: 0 none, 1 sinking, 2 window appearing, 3 talking, 4 words fading, 5 window closing, 6 postcard and after (QA).</summary>
    public int EpiloguePhaseIndex => (int)epPhase;
    /// <summary>How far the screen has sunk to black, 0..1 (QA).</summary>
    public float EpilogueSinkProgress => epPhase == EpiloguePhase.None ? 0f : Mathf.Clamp01((PresentationTime - epStart) / EpilogueDimSeconds);
    public int EpilogueChoicesMade { get; private set; }
    public bool IsEpilogueAwaitingChoice => epPhase == EpiloguePhase.Talking && epAwaitingReply && !epTyping;
    public int EpilogueStepIndex => epStep;
    public string EpilogueText => epText;
    /// <summary>The ending's postcard waits for a click or Space (QA).</summary>
    public bool IsPostcardWaiting => waitCardClick && cardLeaveAt < 0f && cardAt >= 0f && PresentationTime - endingAt >= cardAt + TossSeconds;

    /// <summary>
    /// The epilogue (see the class notes) in place of PlayEnding. Called after
    /// the second 「妈」 as PlayEnding was; it waits until the green lines have
    /// stood (5.25 s), then bursts the heart. Restoring (a save in the ending):
    /// straight to the black with Live's window open at the first step.
    /// EndingFinished comes after the staff, as before.
    /// </summary>
    public void PlayEpilogue(IList<EpilogueStep> script, bool restoring = false)
    {
        if (epPhase != EpiloguePhase.None || endingAt >= 0f) return;
        epScript = script != null ? new List<EpilogueStep>(script) : new List<EpilogueStep>();
        epStep = -1; epText = string.Empty; epOptions = null; epAwaitingReply = false; epTyping = false; EpilogueChoicesMade = 0;
        double now = MusicTime;
        notes.Clear();
        foreach (var phrase in phrases.ToArray()) if (phrase.Alive) Fall(phrase, now);
        if (restoring)
        {
            heartGone = true;
            epStart = PresentationTime - EpilogueDimSeconds - WindowAppearDelay - WindowRevealSeconds;
            answers.Clear();
            EnterPhase(EpiloguePhase.Talking);
            LiftCursors(true);
            NextEpilogueStep();
            return;
        }
        epStart = Mathf.Max(PresentationTime, answersStandUntil);
        EnterPhase(EpiloguePhase.Sinking);
    }

    /// <summary>Pick reply i of the replies on screen (QA; the player clicks).</summary>
    public bool ChooseEpilogueOption(int index)
    {
        if (!IsEpilogueAwaitingChoice || epOptions == null || index < 0 || index >= epOptions.Length) return false;
        var step = epScript[epStep];
        int stepIndex = epStep;
        epOptions = null; epAwaitingReply = false; epButtons.Clear(); epHover = -1;
        EpilogueChoicesMade++;
        EpilogueChoice?.Invoke(stepIndex, index);
        string reply = step.replies != null && index < step.replies.Length ? step.replies[index] : null;
        if (!string.IsNullOrEmpty(reply)) StartLine(reply, ReplyHold);
        else NextEpilogueStep();
        return true;
    }

    void EnterPhase(EpiloguePhase phase) { epPhase = phase; epPhaseAt = PresentationTime; }

    void ResetEpilogue()
    {
        if (epTyping) OS_Handle.Instance?.CharacterWindowController?.EndLinkMessageTyping();
        epPhase = EpiloguePhase.None; epScript = null; epStep = -1; epText = string.Empty; epOptions = null;
        epAwaitingReply = epTyping = false; epButtons.Clear(); epHover = -1; EpilogueChoicesMade = 0;
        heartGone = celebrated = false; burst.Clear(); epClock = -1f;
        HideTexts();
        waitCardClick = false; cardLeaveAt = -1f;
        LiftCursors(false);
    }

    // ---- the script

    void NextEpilogueStep()
    {
        epStep++;
        if (epScript == null || epStep >= epScript.Count) { EndTalk(); return; }
        var step = epScript[epStep];
        if (step.IsChoice)
        {
            epOptions = step.options.Length > 3 ? new[] { step.options[0], step.options[1], step.options[2] } : step.options;
            epAwaitingReply = true;
        }
        else StartLine(step.text, step.holdSeconds);
    }

    void StartLine(string text, float hold)
    {
        epText = text ?? string.Empty;
        epLineTyped = 0f; epCharWait = 0f; epHoldLeft = Mathf.Max(0f, hold); epTyping = true;
        epRate = LineTypeRate(epText);
        OS_Handle.Instance?.CharacterWindowController?.BeginLinkMessageTyping();
    }

    void EndTalk()
    {
        EnterPhase(EpiloguePhase.WordsFading);
    }

    /// <summary>Advance typing, holds and phases (from Render).</summary>
    void StepEpilogue(float t)
    {
        float dt = epClock < 0f ? 0f : Mathf.Max(0f, t - epClock);
        epClock = t;
        if (epPhase == EpiloguePhase.None) return;
        float e = t - epStart;
        switch (epPhase)
        {
            case EpiloguePhase.Sinking:
                if (e >= 0f && !celebrated) CelebrateHeart();
                if (e >= EpilogueDimSeconds + WindowAppearDelay) { EnterPhase(EpiloguePhase.Revealing); LiftCursors(true); }
                break;
            case EpiloguePhase.Revealing:
                if (t - epPhaseAt >= WindowRevealSeconds) { EnterPhase(EpiloguePhase.Talking); NextEpilogueStep(); }
                break;
            case EpiloguePhase.Talking:
                if (epTyping)
                {
                    // the chat's rhythm; dots come slowly and a run of them holds, like the grave's typewriter
                    var window = OS_Handle.Instance?.CharacterWindowController;
                    epCharWait -= dt;
                    while (epCharWait <= 0f && epLineTyped < epText.Length)
                    {
                        int i = Mathf.FloorToInt(epLineTyped);
                        char c = epText[i];
                        epLineTyped = i + 1;
                        window?.NotifyLinkMessageCharacter(c);
                        bool dot = c == '.' || c == '…' || c == '⋯';
                        bool runEnds = dot && (i + 1 >= epText.Length || !(epText[i + 1] == '.' || epText[i + 1] == '…' || epText[i + 1] == '⋯'));
                        epCharWait += dot ? DotSeconds : 1f / epRate;
                        if (runEnds) epCharWait += DotRunHold;
                        if (c == '，' || c == ',' || c == '、') epCharWait += CommaHold;
                    }
                    if (epLineTyped >= epText.Length) { epTyping = false; window?.EndLinkMessageTyping(); }
                }
                else if (!epAwaitingReply)
                {
                    epHoldLeft -= dt;
                    if (epHoldLeft <= 0f) NextEpilogueStep();
                }
                break;
            case EpiloguePhase.WordsFading:
                if (t - epPhaseAt >= WordsFadeSeconds) EnterPhase(EpiloguePhase.Closing);
                break;
            case EpiloguePhase.Closing:
                if (t - epPhaseAt >= WindowCloseSeconds) StartPostcard();
                break;
        }
    }

    /// <summary>After the window: the existing ending from the postcard on, which now waits for a click.</summary>
    void StartPostcard()
    {
        EnterPhase(EpiloguePhase.Postcard);
        thankLines = new[] { string.Empty }; thankHolds = new[] { 0f };
        endingAt = PresentationTime - TypeStart;          // already under the black
        endingFinished = false;
        lineIndex = 0; lineTyped = 0f; lineState = LineState.Done; hurry = false; EndingLinesTyped = 0;
        lastEndingClock = PresentationTime;
        cardAt = TypeStart;
        waitCardClick = true; cardLeaveAt = -1f; postcardCollected = false;
    }

    /// <summary>A press (PlayPress, or polled here): finish typing, or let the postcard go.</summary>
    void EpiloguePress()
    {
        if (epPressFrame == Time.frameCount) return;
        epPressFrame = Time.frameCount;
        if (epPhase == EpiloguePhase.Talking && epTyping) { epLineTyped = epText.Length; return; }
        if (IsPostcardWaiting) cardLeaveAt = PresentationTime - endingAt;
    }

    /// <summary>The player's own cursor and clicks during the dialogue and the postcard.</summary>
    void PollEpilogueInput()
    {
        if (epPhase != EpiloguePhase.Talking && epPhase != EpiloguePhase.Postcard) return;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        epHover = -1;
        if (mouse != null && epButtons.Count > 0 && TryOsPixel(mouse.position.ReadValue(), out Vector2Int p))
            for (int i = 0; i < epButtons.Count; i++)
                if (epButtons[i].Contains(p)) epHover = i;
        bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
        if (click && epHover >= 0 && IsEpilogueAwaitingChoice) { ChooseEpilogueOption(epHover); epPressFrame = Time.frameCount; return; }
        if (click || keyboard != null && keyboard.spaceKey.wasPressedThisFrame) EpiloguePress();
    }

    bool TryOsPixel(Vector2 screen, out Vector2Int pixel)
    {
        pixel = default;
        if (layerRoot == null) return false;
        var canvas = layerRoot.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layerRoot, screen, cam, out var local)) return false;
        pixel = new Vector2Int(Mathf.FloorToInt(local.x + Width / 2f), Mathf.FloorToInt(Height / 2f - local.y));
        return true;
    }

    Vector2 ScreenOf(int x, int y)
    {
        var canvas = layerRoot.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector3 world = layerRoot.TransformPoint(new Vector3(x - Width / 2f, Height / 2f - y, 0f));
        return RectTransformUtility.WorldToScreenPoint(cam, world);
    }

    // ---- the celebration (round 11i): the heart stays whole and pulses once; a ring of
    // "pass" sparkles - small crosses and diamonds, red and light gray - flies out of it,
    // slows, twinkles and is gone in about a second. The black then swallows the heart.

    bool heartGone, celebrated;            // heartGone: only when restoring straight into the black
    struct Sparkle { public float angle, speed, life; public byte shape; public Color32 color; }
    readonly List<Sparkle> burst = new List<Sparkle>();
    float burstAt;
    const float SparkleDrag = 2.6f, SparkleStart = 10f;
    // 5 x 5 cross, 3 x 3 cross, 3 x 3 hollow diamond
    static readonly string[][] SparkleShapes =
    {
        new[] { "..#..", "..#..", "#####", "..#..", "..#.." },
        new[] { ".#.", "###", ".#." },
        new[] { ".#.", "#.#", ".#." },
    };

    void CelebrateHeart()
    {
        celebrated = true;
        burstAt = PresentationTime;
        PlayHeartHit();
        AddRing(170f, .6f, false);
        burst.Clear();
        var random = new System.Random(1103);
        const int count = 28;
        for (int i = 0; i < count; i++)
            burst.Add(new Sparkle
            {
                angle = (i + (float)random.NextDouble() * .35f) / count * Mathf.PI * 2f,
                speed = 150f + (float)random.NextDouble() * 50f,
                life = .8f + (float)random.NextDouble() * .4f,
                shape = (byte)(i % 3),
                color = i % 2 == 0 ? Red : Light
            });
    }

    void DrawBurst(float t)
    {
        if (burst.Count == 0) return;
        float e = t - burstAt;
        float travel = (1f - Mathf.Exp(-SparkleDrag * e)) / SparkleDrag;      // fast out, slowing down
        bool frameOn = Mathf.FloorToInt(t * Fps) % 2 == 0;
        bool any = false;
        foreach (var s in burst)
        {
            if (e >= s.life) continue;
            any = true;
            if (e > s.life * .45f && !frameOn) continue;                     // twinkle: every other frame
            int keep = e < s.life * .75f ? 16 : Mathf.RoundToInt(16f * (s.life - e) / (s.life * .25f));
            float r = SparkleStart + s.speed * travel;
            int cx = Mathf.RoundToInt(HeartCenterX + Mathf.Cos(s.angle) * r), cy = Mathf.RoundToInt(HeartCenterY + Mathf.Sin(s.angle) * r);
            var shape = SparkleShapes[s.shape];
            int half = shape.Length / 2;
            for (int y = 0; y < shape.Length; y++)
                for (int x = 0; x < shape[y].Length; x++)
                    if (shape[y][x] == '#') Dot(cx - half + x, cy - half + y, s.color, keep);
        }
        if (!any) burst.Clear();
    }

    // ---- drawing

    /// <summary>The sinking black over everything, then Live's window (from Render, before the ending).</summary>
    void DrawEpilogue(float t)
    {
        if (epPhase == EpiloguePhase.None || epPhase == EpiloguePhase.Postcard) { HideDialogueLabels(); return; }
        float e = t - epStart;
        if (e < 0f) return;
        DrawBurst(t);
        int level = Mathf.Clamp(Mathf.FloorToInt(16f * e / EpilogueDimSeconds) + 1, 0, 16);
        if (level >= 16) { for (int i = 0; i < buffer.Length; i++) buffer[i] = Black; }
        else
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Bayer[(y & 3) * 4 + (x & 3)] < level) buffer[(Height - 1 - y) * Width + x] = Black;
        if (epPhase == EpiloguePhase.Sinking)
        {
            MaskDither(0, 0, Width - 1, Height - 1, level);        // the green lines go under the same black
            HideDialogueLabels();
            return;
        }

        // the window: dithered in, talking, words fading, closing
        int keep = 16, wordsKeep = 16;
        float shrinkX = 1f, shrinkY = 1f;
        float since = t - epPhaseAt;
        if (epPhase == EpiloguePhase.Revealing) keep = Mathf.Clamp(Mathf.FloorToInt(16f * since / WindowRevealSeconds) + 1, 1, 16);
        if (epPhase == EpiloguePhase.WordsFading) wordsKeep = Mathf.Clamp(16 - Mathf.FloorToInt(16f * since / WordsFadeSeconds), 0, 16);
        if (epPhase == EpiloguePhase.Closing)
        {
            wordsKeep = 0;
            float f = Mathf.Clamp01(since / WindowCloseSeconds);
            float ease = f * f * f;                              // the OS window close: InCubic to (0.94, 0.86), fading
            shrinkX = Mathf.Lerp(1f, .94f, ease); shrinkY = Mathf.Lerp(1f, .86f, ease);
            keep = Mathf.Clamp(16 - Mathf.FloorToInt(16f * f * f), 0, 16);
        }
        DrawLiveWindow(keep, wordsKeep, shrinkX, shrinkY);
    }

    void HideDialogueLabels()
    {
        Show(titleLabel, false); Show(lineLabel, false);
        foreach (var label in optionLabels) Show(label, false);
    }

    readonly List<Vector2Int> epOptionSizes = new List<Vector2Int>(3);

    void DrawLiveWindow(int keep, int wordsKeep, float shrinkX, float shrinkY)
    {
        if (lineLabel == null) return;
        bool closing = epPhase == EpiloguePhase.Closing, revealing = epPhase == EpiloguePhase.Revealing;
        int textWidth = WindowWidth - 64 - 12;
        int lineHeight = LabelHeight(lineLabel, epText, textWidth);
        int textHeight = Mathf.Max(AvatarScale * 24, lineHeight);
        bool options = epOptions != null && epPhase == EpiloguePhase.Talking && !epTyping;
        // replies: each wraps inside the window; a row when they fit side by side, else one under another
        var sizes = epOptionSizes;
        sizes.Clear();
        int rowWidth = -ButtonGap, rowHeight = 0, stackHeight = -ButtonGap;
        if (options)
            foreach (var option in epOptions)
            {
                int wide = Mathf.Min(LabelWidth(optionLabels[0], option), WindowWidth - 40);
                var size = new Vector2Int(wide + 14, LabelHeight(optionLabels[0], option, wide + 1) + 6);
                sizes.Add(size);
                rowWidth += size.x + ButtonGap; rowHeight = Mathf.Max(rowHeight, size.y); stackHeight += size.y + ButtonGap;
            }
        bool inRow = rowWidth <= WindowWidth - 16;
        int optionsHeight = options ? (inRow ? rowHeight : stackHeight) : 0;
        int height = TitleHeight + 8 + textHeight + (options ? 10 + optionsHeight : 0) + 10;
        int w = Mathf.RoundToInt(WindowWidth * shrinkX), h = Mathf.RoundToInt(height * shrinkY);
        int left = Width / 2 - w / 2, top = WindowTop + (height - h) / 2;
        // frame, title bar and a close box, black body (the words are TMP labels over it)
        for (int y = top; y < top + h; y++)
            for (int x = left; x < left + w; x++)
            {
                bool frame = x == left || x == left + w - 1 || y == top || y == top + h - 1 || y == top + TitleHeight;
                Dot(x, y, frame ? Gray : Black, keep);
            }
        int bx = left + w - 11, by = top + 3;
        for (int k = 0; k < 7; k++) { Dot(bx + k, by + k, Gray, keep); Dot(bx + 6 - k, by + k, Gray, keep); }
        epButtons.Clear();
        if (revealing || closing) { HideDialogueLabels(); return; }
        titleLabel.text = "Live";
        PlaceLabel(titleLabel, left + 6, top + 1, 80, TitleHeight);
        Show(titleLabel, true);
        // avatar (the chat's Live icon, 2x) with the red pointer resting by it, then her line, typed
        int avatarLeft = left + 8, avatarTop = top + TitleHeight + 8;
        DrawAvatar(avatarLeft, avatarTop, wordsKeep);
        DrawRedPointer(avatarLeft + AvatarScale * 24 - 6, avatarTop + AvatarScale * 24 - 14, wordsKeep);
        lineLabel.text = epText;
        lineLabel.maxVisibleCharacters = epTyping ? Mathf.FloorToInt(epLineTyped) : 99999;
        PlaceLabel(lineLabel, left + 64, avatarTop + Mathf.Max(0, (AvatarScale * 24 - lineHeight) / 2), textWidth, lineHeight + 4);
        Show(lineLabel, true);
        for (int i = 0; i < optionLabels.Length; i++) Show(optionLabels[i], options && i < epOptions.Length);
        if (options)
        {
            int ox = Width / 2 - rowWidth / 2, oy = avatarTop + textHeight + 10;
            for (int i = 0; i < epOptions.Length; i++)
            {
                var size = sizes[i];
                var rect = inRow ? new RectInt(ox, oy, size.x, size.y) : new RectInt(Width / 2 - size.x / 2, oy, size.x, size.y);
                epButtons.Add(rect);
                bool hover = i == epHover;
                // the frame lights up under the player's cursor
                for (int x = rect.xMin; x < rect.xMax; x++) { Dot(x, rect.yMin, hover ? Light : Gray, wordsKeep); Dot(x, rect.yMax - 1, hover ? Light : Gray, wordsKeep); }
                for (int y = rect.yMin; y < rect.yMax; y++) { Dot(rect.xMin, y, hover ? Light : Gray, wordsKeep); Dot(rect.xMax - 1, y, hover ? Light : Gray, wordsKeep); }
                if (hover)
                    for (int x = rect.xMin + 1; x < rect.xMax - 1; x++) { Dot(x, rect.yMin + 1, Gray, wordsKeep); Dot(x, rect.yMax - 2, Gray, wordsKeep); }
                optionLabels[i].text = epOptions[i];
                PlaceLabel(optionLabels[i], rect.xMin + 7, rect.yMin + 3, rect.width - 14, rect.height - 4);
                if (inRow) ox += size.x + ButtonGap; else oy += size.y + ButtonGap;
            }
        }
        // the words dissolve through the mask as the window empties
        if (wordsKeep < 16) MaskDither(left + 1, top + 1, left + w - 2, top + h - 2, 16 - wordsKeep);
    }

    // ---- the red pointer: the player's cursor sprite in red (as OSCursorReleaseDirector tints it), drawn here
    Color32[] pointerPixels;
    int pointerWidth, pointerHeight;
    bool pointerRead;

    void DrawRedPointer(int tipX, int tipY, int keep)
    {
        if (!pointerRead)
        {
            pointerRead = true;
            var sprite = CursorHandle.Instance != null ? CursorHandle.Instance.NormalSprite : null;
            if (sprite != null) pointerPixels = ReadSprite(sprite, out pointerWidth, out pointerHeight);
        }
        if (pointerPixels == null) return;
        for (int row = 0; row < pointerHeight; row++)
            for (int x = 0; x < pointerWidth; x++)
            {
                var c = pointerPixels[row * pointerWidth + x];
                if (c.a < 128) continue;
                // light pixels take the red, dark ones stay as the outline
                Dot(tipX + x, tipY + (pointerHeight - 1 - row), c.r + c.g + c.b > 3 * 110 ? Red : Black, keep);
            }
    }

    /// <summary>A sprite's pixels (Unity rows, bottom-up), read once through the GPU (UI sprites are not readable).</summary>
    static Color32[] ReadSprite(Sprite sprite, out int width, out int height)
    {
        var rect = sprite.textureRect;
        width = Mathf.RoundToInt(rect.width); height = Mathf.RoundToInt(rect.height);
        var texture = sprite.texture;
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
            copy.Apply(false);
            var pixels = copy.GetPixels32();
            Destroy(copy);
            return pixels;
        }
        catch (Exception error) { Debug.LogWarning("Final heal: a sprite could not be read: " + error.Message); return null; }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
    }

    void Dot(int x, int y, Color32 color, int keep)
    {
        if (keep <= 0 || keep < 16 && Bayer[(y & 3) * 4 + (x & 3)] >= keep) return;
        Put(x, y, color);
    }

    /// <summary>Split a line where it would pass maxWidth pixels (letter by letter; spaces kept).</summary>
    List<string> Wrap(string text, int height, int maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(string.Empty); return lines; }
        EnsureFont();
        var current = new System.Text.StringBuilder();
        float width = 0f;
        foreach (char c in text)
        {
            float advance = GlyphOf(c).advance / lineUnit * height;
            if (current.Length > 0 && width + advance > maxWidth) { lines.Add(current.ToString()); current.Clear(); width = 0f; }
            current.Append(c); width += advance;
        }
        lines.Add(current.ToString());
        return lines;
    }

    // ---- Live's chat avatar (record 3, the icon the chat shows for her), read once from the GPU
    Color32[] avatarPixels;
    int avatarWidth, avatarHeight;
    bool avatarRead;

    void DrawAvatar(int left, int top, int keep)
    {
        if (!avatarRead) ReadAvatar();
        if (avatarPixels == null) return;
        for (int row = 0; row < avatarHeight; row++)
            for (int x = 0; x < avatarWidth; x++)
            {
                var c = avatarPixels[row * avatarWidth + x];
                if (c.a < 128) continue;
                var color = Nearest(c);
                int y = avatarHeight - 1 - row;
                for (int dy = 0; dy < AvatarScale; dy++)
                    for (int dx = 0; dx < AvatarScale; dx++)
                        Dot(left + x * AvatarScale + dx, top + y * AvatarScale + dy, color, keep);
            }
    }

    void ReadAvatar()
    {
        avatarRead = true;
        var records = GameDataManager.Instance?.RecordSO;
        if (records == null || !records.TryGetRecordData(3, out var record) || record.RecordIcon == null) return;
        avatarPixels = ReadSprite(record.RecordIcon, out avatarWidth, out avatarHeight);
    }

    // ---- the red pointer and the player's cursor

    Canvas liftedCursorCanvas;
    int cursorOrderBefore;
    const int LiftedCursorOrder = SortingOrder + 10;

    /// <summary>The player's cursor canvas sits at 1500, under this layer (32050): lift it for the dialogue and the postcard.</summary>
    void LiftCursors(bool lift)
    {
        if (lift)
        {
            if (liftedCursorCanvas != null) return;
            var cursor = FindAnyObjectByType<CursorHandle>();
            var canvas = cursor != null ? cursor.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;
            liftedCursorCanvas = canvas.rootCanvas;
            cursorOrderBefore = liftedCursorCanvas.sortingOrder;
            liftedCursorCanvas.sortingOrder = LiftedCursorOrder;
            return;
        }
        if (liftedCursorCanvas != null) liftedCursorCanvas.sortingOrder = cursorOrderBefore;
        liftedCursorCanvas = null;
    }
}
