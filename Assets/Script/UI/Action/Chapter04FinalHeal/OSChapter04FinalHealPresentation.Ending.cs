using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Round 11, the ending after the heart is whole:
///   1. a last red ring spreads past the screen; every string left drops (0.6 s);
///   2. the screen dithers to black (1.5 s);
///   3. the thank-you lines type out in the middle, letter by letter, one after
///      another in the same place (each held, then dithered away), the last held 2 s;
///   4. the authored 340 x 200 postcard slides up from the bottom, drawn 1:1;
///      the epilogue waits for a click, while the legacy ending holds it 4 s;
///   5. everything scrolls up into the logo and the staff list; when the last
///      line reaches the middle and has stood a moment, EndingFinished.
/// The screen stays black afterwards until End or ClearAll.
/// </summary>
public sealed partial class OSChapter04FinalHealPresentation
{
    const float BurstSeconds = .6f, FadeSeconds = 1.5f, TypeInterval = .12f, TypedHold = 2f;
    const float CardSlideSeconds = .8f, CardHold = 4f, ScrollSpeed = 24f, CreditsHold = 3f;
    const float CreditsHurryFactor = 1.75f;
    const int BurstRadius = 370, CardWidth = 340, CardHeight = 200, CardLeft = (Width - CardWidth) / 2, CardRestTop = (Height - CardHeight) / 2;
    const string PostcardResource = "Chapter04/FinalHeal/EndingPostcard";
    const int TextHeight = 14, LogoTextHeight = 28, CreditsGap = 28;
    const float CreditsLogoScale = .6f;
    // QA placeholders until the real logo and staff list are handed in (SetEndingCredits).
    const string LogoPlaceholder = "（Logo 占位）";
    static readonly string[] StaffPlaceholder = { "Staff（占位）", "", "企划　（占位）", "剧本　（占位）", "美术　（占位）", "程序　（占位）", "音乐　（占位）" };

    float endingAt = -1f;
    bool endingFinished;
    bool menuReturnRetryVisible;
    string[] thankLines = new string[0];
    float[] thankHolds = new float[0];
    Texture2D logo;
    string[] staff;
    Color32[] logoPixels;
    RectInt logoBounds;
    int logoWidth, logoHeight;
    string studio;
    RectTransform creditsRoot;
    TMP_Text studioLabel;
    readonly List<TMP_Text> creditLabels = new List<TMP_Text>();
    readonly List<int> creditHeights = new List<int>();
    int creditsTextHeight;
    Color32[] postcardPixels;
    bool postcardLoadAttempted;

    // ---- the thank-you lines (round 11d): typed one after another in the middle
    enum LineState { Typing, Holding, Fading, Done }
    LineState lineState;
    int lineIndex;
    float lineTyped, lineLeft, lastEndingClock, cardAt = -1f;
    bool hurry;
    // round 11h: after the epilogue the postcard waits for a click or Space, then leaves
    bool waitCardClick;
    bool postcardCollected;
    float cardLeaveAt = -1f;
    float creditsExtraSeconds;

    public bool IsEndingPlaying => endingAt >= 0f;
    public bool IsEndingFinished => endingFinished;
    public bool IsPostcardCollected => postcardCollected;
    public event Action PostcardCollected;
    /// <summary>Where the ending is: 0 none, 1 burst, 2 fade, 3 typing, 4 card, 5 scroll, 6 finished (QA).</summary>
    public int EndingPhase { get; private set; }
    /// <summary>Top of the postcard on screen when drawn last, or int.MinValue (QA).</summary>
    public int PostcardTop { get; private set; } = int.MinValue;
    /// <summary>How far the ending has scrolled when drawn last (QA).</summary>
    public int CreditsScroll { get; private set; }
    /// <summary>The thank-you line on screen (0-based) and how many have been typed out in full (QA).</summary>
    public int EndingLineIndex => lineIndex;
    public int EndingLinesTyped { get; private set; }

    public void SetMenuReturnRetryVisible(bool visible)
    {
        menuReturnRetryVisible = visible;
        if (visible) LiftCursors(true);
        lastFrame = -1;
    }

    /// <summary>Crop transparent margins and fit the logo with nearest-neighbor sampling; staff entries are localized blocks.</summary>
    public void SetEndingCredits(Texture2D logoTexture, IList<string> staffLines, string studioName = null, int logoPixelSize = 1)
    {
        logo = logoTexture;
        staff = staffLines != null ? new List<string>(staffLines).ToArray() : null;
        studio = studioName;
        CacheCreditLogo(Mathf.Max(1, logoPixelSize));
        ClearCreditLayout();
    }

    /// <summary>The heart is whole: play the ending with one thank-you line.</summary>
    public void PlayEnding(string thankYou) => PlayEnding(new[] { thankYou ?? string.Empty });

    /// <summary>
    /// The heart is whole: play the ending (see the class notes). The lines type
    /// out one after another in the middle; after each, holdSeconds[i] (when
    /// missing: 1.5 s, the last 2 s) and a short dithered fade; after the last,
    /// its hold and the postcard. A press (PlayPress) while a line types speeds it up, never
    /// skipping it. Raises EndingFinished once.
    /// </summary>
    public void PlayEnding(IList<string> lines, IList<float> holdSeconds = null)
    {
        if (endingAt >= 0f) return;
        var kept = new List<string>();
        if (lines != null) foreach (var line in lines) if (!string.IsNullOrEmpty(line)) kept.Add(line);
        if (kept.Count == 0) kept.Add(string.Empty);
        thankLines = kept.ToArray();
        thankHolds = new float[thankLines.Length];
        for (int i = 0; i < thankHolds.Length; i++)
            thankHolds[i] = holdSeconds != null && i < holdSeconds.Count && holdSeconds[i] >= 0f ? holdSeconds[i]
                : i == thankHolds.Length - 1 ? TypedHold : LineHold;      // the last line holds 2 s unless set
        // round 11f: the green lines risen on 「妈妈」 stand ~3 s before the black closes over them
        endingAt = Mathf.Max(PresentationTime, answersStandUntil - BurstSeconds);
        endingFinished = false;
        lineIndex = 0; lineTyped = 0f; lineState = LineState.Typing; hurry = false; cardAt = -1f; EndingLinesTyped = 0;
        lastEndingClock = endingAt;
        creditsExtraSeconds = 0f;
        if (answersStandUntil < PresentationTime) AddRing(BurstRadius, BurstSeconds, false);   // 「妈妈」 already spread one
        double now = MusicTime;
        foreach (var phrase in phrases.ToArray()) if (phrase.Alive) Fall(phrase, now);
        notes.Clear();
    }

    /// <summary>A press while a thank-you line types: the rest of it comes four times as fast.</summary>
    void HurryEnding()
    {
        if (endingAt >= 0f && lineState == LineState.Typing && PresentationTime - endingAt >= TypeStart) hurry = true;
    }

    void ResetEnding()
    {
        endingAt = -1f; endingFinished = false; EndingPhase = 0;
        menuReturnRetryVisible = false;
        if (hintLabel != null) hintLabel.enableWordWrapping = false;
        PostcardTop = int.MinValue; CreditsScroll = 0;
        lineIndex = 0; lineTyped = 0f; lineState = LineState.Typing; hurry = false; cardAt = -1f; EndingLinesTyped = 0;
        waitCardClick = false; cardLeaveAt = -1f; postcardCollected = false;
        creditsExtraSeconds = 0f;
        postcardPixels = null; postcardLoadAttempted = false;
        if (creditsRoot != null) creditsRoot.gameObject.SetActive(false);
    }

    // ---- timeline (seconds after PlayEnding); the card waits for the last line
    const float LineHold = 1.5f, LineFadeSeconds = .4f, HurryFactor = 4f;
    float FadeStart => BurstSeconds;
    float TypeStart => BurstSeconds + FadeSeconds;
    float ScrollStart => waitCardClick
        ? (cardLeaveAt < 0f ? float.MaxValue : cardLeaveAt + CardSlideSeconds)
        : cardAt + CardSlideSeconds + CardHold;
    int CreditsTop => Height + CreditsGap;
    int ScrollTarget => CreditsTop + CreditsHeight - Height / 2 - LastCreditHeight / 2;
    float FinishTime => ScrollStart + ScrollTarget / ScrollSpeed + CreditsHold - creditsExtraSeconds;

    bool EndingCoversScreen(float t) => endingAt >= 0f && t - endingAt >= TypeStart;

    void AdvanceEnding(float t)
    {
        if (endingAt < 0f) { EndingPhase = 0; return; }
        float e = Mathf.Max(0f, t - endingAt);           // a delayed start reads as the burst until it begins
        float dt = Mathf.Max(0f, t - lastEndingClock);
        if (e >= TypeStart && cardAt < 0f) StepLines(t, dt);
        if (waitCardClick && cardLeaveAt >= 0f && e >= cardLeaveAt + CardSlideSeconds && !postcardCollected)
        {
            postcardCollected = true;
            PostcardCollected?.Invoke();
        }
        if (cardAt >= 0f && e >= ScrollStart && !endingFinished)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                // Only the scrolling part gets faster; use the pausable presentation clock
                // and cap at the last line so its final hold still lasts CreditsHold seconds.
                float from = Mathf.Max(lastEndingClock, endingAt + ScrollStart);
                float elapsed = from - endingAt - ScrollStart + creditsExtraSeconds;
                float remaining = Mathf.Max(0f, ScrollTarget / ScrollSpeed - elapsed);
                creditsExtraSeconds += Mathf.Min(Mathf.Max(0f, t - from), remaining / CreditsHurryFactor)
                    * (CreditsHurryFactor - 1f);
            }
        }
        lastEndingClock = t;
        EndingPhase = e < FadeStart ? 1 : e < TypeStart ? 2 : cardAt < 0f ? 3 : e < ScrollStart ? 4 : e < FinishTime ? 5 : 6;
        if (EndingPhase == 6 && !endingFinished)
        {
            endingFinished = true;
            LiftCursors(false);                          // the player's cursor goes back under the OS layers
            EndingFinished?.Invoke();
        }
    }

    void StepLines(float t, float dt)
    {
        string line = thankLines[lineIndex];
        switch (lineState)
        {
            case LineState.Typing:
                lineTyped += dt / TypeInterval * (hurry ? HurryFactor : 1f);
                if (lineTyped >= line.Length) { lineTyped = line.Length; lineState = LineState.Holding; lineLeft = thankHolds[lineIndex]; hurry = false; EndingLinesTyped++; }
                break;
            case LineState.Holding:
                lineLeft -= dt;
                if (lineLeft > 0f) break;
                if (lineIndex == thankLines.Length - 1) { lineState = LineState.Done; cardAt = t - endingAt; }
                else { lineState = LineState.Fading; lineLeft = LineFadeSeconds; }
                break;
            case LineState.Fading:
                lineLeft -= dt;
                if (lineLeft > 0f) break;
                lineIndex++; lineTyped = 0f; lineState = LineState.Typing;
                break;
        }
    }

    void DrawEnding(float t)
    {
        Show(hintLabel, false);
        if (endingAt < 0f) return;
        float e = t - endingAt;
        if (e < FadeStart) return;
        if (e < TypeStart)
        {
            // the dither closes over everything, one Bayer step at a time
            int level = Mathf.Clamp(Mathf.RoundToInt(16f * (e - FadeStart) / FadeSeconds), 0, 16);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Bayer[(y & 3) * 4 + (x & 3)] < level) buffer[(Height - 1 - y) * Width + x] = Black;
            return;
        }
        for (int i = 0; i < buffer.Length; i++) buffer[i] = Black;
        int scroll = cardAt < 0f || e < ScrollStart ? 0 : Mathf.Min(ScrollTarget, Mathf.FloorToInt((e - ScrollStart + creditsExtraSeconds) * ScrollSpeed));
        CreditsScroll = scroll;

        int cardTop = int.MinValue, cardShift = 0;
        float cardAngle = 0f;
        if (cardAt >= 0f)
        {
            if (waitCardClick)
            {
                // round 11j: tossed onto the table from below - tilted, a little off to the side,
                // on a slight arc, sliding to a stop, a small rebound, resting a touch askew
                float f = Mathf.Clamp01((e - cardAt) / TossSeconds);
                float slide = 1f - (1f - f) * (1f - f) * (1f - f);
                cardTop = Mathf.RoundToInt(Mathf.Lerp(Height + 24, CardRestTop, slide));
                cardShift = Mathf.RoundToInt(Mathf.Lerp(TossOffsetX, 0f, slide) + TossArc * Mathf.Sin(Mathf.PI * slide));
                cardAngle = Mathf.Lerp(TossStartAngle, PostcardRestAngle, slide);
                if (f > .78f)
                {
                    float settle = Mathf.Sin((f - .78f) / .22f * Mathf.PI);
                    cardAngle += TossOvershoot * settle;
                    cardTop -= Mathf.RoundToInt(2f * settle);
                }
            }
            else
            {
                float slide = Mathf.Clamp01((e - cardAt) / CardSlideSeconds);
                slide = 1f - (1f - slide) * (1f - slide) * (1f - slide);
                cardTop = Mathf.RoundToInt(Mathf.Lerp(Height, CardRestTop, slide));
            }
            if (waitCardClick && cardLeaveAt >= 0f)
            {
                // clicked: the postcard slides back down and away before the logo comes up
                float leave = Mathf.Clamp01((e - cardLeaveAt) / CardSlideSeconds);
                cardTop = leave >= 1f ? int.MinValue : Mathf.RoundToInt(Mathf.Lerp(CardRestTop, Height, leave * leave * leave));
                cardShift = 0; cardAngle = Mathf.Lerp(PostcardRestAngle, PostcardRestAngle + 8f, leave);
            }
        }
        PostcardAngle = cardAngle;
        // the line sits in the middle until the card comes up under it, then rides above it
        int textTop = Height / 2 - TextHeight / 2;
        if (cardTop != int.MinValue) textTop = Mathf.Min(textTop, cardTop - 20 - TextHeight);
        string line = thankLines[lineIndex];
        int typed = Mathf.Clamp(Mathf.FloorToInt(lineTyped) + (lineState == LineState.Typing ? 1 : 0), 0, line.Length);
        int keep = lineState == LineState.Fading ? Mathf.Clamp(Mathf.RoundToInt(16f * lineLeft / LineFadeSeconds), 0, 16) : 16;
        DrawFlatText(line, Width / 2, textTop - scroll, TextHeight, Light, typed, keep);
        PostcardTop = cardTop != int.MinValue ? cardTop - scroll : int.MinValue;
        if (cardTop != int.MinValue) DrawPostcard(CardLeft + cardShift, cardTop - scroll, cardAngle);
        if (waitCardClick && cardLeaveAt < 0f && cardAt >= 0f && e >= cardAt + TossSeconds)
        {
            // a small hint under the card, breathing between two dither levels until the press
            var data = GameDataManager.Instance;
            string hint = data != null ? data.GetLocalizedText(PostcardHintKey, PostcardHintZh) : PostcardHintZh;
            if (hintLabel != null)
            {
                int hintTop = CardRestTop + CardHeight + 8;
                hintLabel.text = hint;
                PlaceLabel(hintLabel, Width / 2 - 120, hintTop, 240, 16);
                Show(hintLabel, true);
                // breathing: every other beat half its pixels go under the mask's dither
                if (Mathf.FloorToInt(t * 1.6f) % 2 == 1) MaskDither(Width / 2 - 120, hintTop, Width / 2 + 120, hintTop + 15, 8);
            }
        }
        if (scroll > 0) DrawCredits(CreditsTop - scroll);
        if (endingFinished && menuReturnRetryVisible && hintLabel != null)
        {
            var data = GameDataManager.Instance;
            const string error = "自动存档写入失败，尚未退出游戏。请检查磁盘后重试。";
            const string retry = "点击或按空格重试";
            hintLabel.text = (data?.GetLocalizedText("ui.save.write_failed", error) ?? error)
                + "\n\n" + (data?.GetLocalizedText("ui.os.chapter04.return_to_menu_retry", retry) ?? retry);
            hintLabel.enableWordWrapping = true;
            PlaceLabel(hintLabel, 80, Height - 88, Width - 160, 76);
            Show(hintLabel, true);
        }
    }

    // ---- postcard
    // Round 11j, the toss: start tilt, sideways start offset and arc (px), length, the
    // rebound past the rest angle, and the rest angle itself (0 lays it square).
    public const float PostcardRestAngle = -3f;
    const float TossStartAngle = -17f, TossOffsetX = -26f, TossArc = 9f, TossSeconds = .55f, TossOvershoot = 1.5f;
    /// <summary>The postcard's tilt as drawn last, degrees (QA).</summary>
    public float PostcardAngle { get; private set; }
    /// <summary>The postcard is flying in or leaving (QA).</summary>
    public bool IsPostcardMoving => cardAt >= 0f && endingAt >= 0f
        && (PresentationTime - endingAt < cardAt + TossSeconds || cardLeaveAt >= 0f && PresentationTime - endingAt < cardLeaveAt + CardSlideSeconds);
    readonly bool[] postcardCover = new bool[Width * Height];
    bool postcardCoverUsed;
    /// <summary>A pixel the postcard drew last frame (QA: its own colors are allowed there).</summary>
    public bool IsPostcardPixel(int x, int y) => (uint)x < Width && (uint)y < Height && postcardCover[y * Width + x];

    /// <summary>
    /// The authored card turned by angle degrees about its center: every screen
    /// pixel samples the nearest card pixel, so it stays crisp 1-bit art with no
    /// smoothing; a faint dither shadow lies 2 px down and right of it.
    /// </summary>
    void DrawPostcard(int left, int top, float angle)
    {
        if (postcardCoverUsed) { System.Array.Clear(postcardCover, 0, postcardCover.Length); postcardCoverUsed = false; }
        if (Mathf.Abs(angle) < .05f) { DrawPostcard(left, top); return; }
        LoadPostcard();
        if (postcardPixels == null) return;
        float r = angle * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
        float cx = left + CardWidth / 2f, cy = top + CardHeight / 2f;
        int reach = Mathf.CeilToInt(Mathf.Sqrt(CardWidth * CardWidth + CardHeight * CardHeight) / 2f) + 3;
        for (int pass = 0; pass < 2; pass++)
            for (int y = Mathf.FloorToInt(cy) - reach; y <= Mathf.CeilToInt(cy) + reach; y++)
            {
                if ((uint)y >= Height) continue;
                for (int x = Mathf.FloorToInt(cx) - reach; x <= Mathf.CeilToInt(cx) + reach; x++)
                {
                    if ((uint)x >= Width) continue;
                    // pass 0: the shadow, sampled 2 px up-left; pass 1: the card
                    float dx = x + .5f - cx - (pass == 0 ? 2f : 0f), dy = y + .5f - cy - (pass == 0 ? 2f : 0f);
                    int sx = Mathf.FloorToInt(cos * dx + sin * dy + CardWidth / 2f);
                    int sy = Mathf.FloorToInt(-sin * dx + cos * dy + CardHeight / 2f);
                    if ((uint)sx >= CardWidth || (uint)sy >= CardHeight) continue;
                    var color = postcardPixels[(CardHeight - 1 - sy) * CardWidth + sx];
                    if (color.a < 128) continue;
                    if (pass == 0) { if (Bayer[(y & 3) * 4 + (x & 3)] < 6) Put(x, y, Gray); continue; }
                    Put(x, y, color);
                    postcardCover[y * Width + x] = true; postcardCoverUsed = true;
                }
            }
    }

    void LoadPostcard()
    {
        if (postcardLoadAttempted) return;
        postcardLoadAttempted = true;
        var artwork = Resources.Load<Texture2D>(PostcardResource);
        if (artwork != null && artwork.width == CardWidth && artwork.height == CardHeight && artwork.isReadable)
            postcardPixels = artwork.GetPixels32();
        else Debug.LogError("Final heal postcard requires a readable 340x200 texture: " + PostcardResource);
    }

    void DrawPostcard(int left, int top)
    {
        LoadPostcard();
        if (postcardPixels == null) return;
        for (int y = 0; y < CardHeight; y++)
            for (int x = 0; x < CardWidth; x++)
            {
                var color = postcardPixels[(CardHeight - 1 - y) * CardWidth + x];
                if (color.a < 128) continue;
                Put(left + x, top + y, color);
                if ((uint)(left + x) < Width && (uint)(top + y) < Height) { postcardCover[(top + y) * Width + left + x] = true; postcardCoverUsed = true; }
            }
    }

    // ---- logo and staff
    string[] StaffLines => staff ?? StaffPlaceholder;
    int LogoHeight => logoPixels != null ? logoHeight : LogoTextHeight;
    int StudioHeight => string.IsNullOrEmpty(studio) ? 0 : 16 + 16;
    int CreditsHeight { get { EnsureCreditLayout(); return LogoHeight + StudioHeight + CreditsGap + creditsTextHeight; } }
    int LastCreditHeight => creditHeights.Count > 0 ? creditHeights[creditHeights.Count - 1] : TextHeight;
    // Let the preceding names leave the frame before the final thank-you settles at center.
    int CreditGapAfter(int index) => index == StaffLines.Length - 2 ? Height / 2 : CreditsGap;

    void CacheCreditLogo(int sourcePixelSize)
    {
        logoPixels = null;
        if (logo == null) return;
        try { logoPixels = logo.GetPixels32(); }
        catch (UnityException) { return; }
        int minX = logo.width, minY = logo.height, maxX = -1, maxY = -1;
        for (int y = 0; y < logo.height; y++)
            for (int x = 0; x < logo.width; x++)
                if (logoPixels[y * logo.width + x].a >= 128)
                { minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y); maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y); }
        if (maxX < 0) { logoPixels = null; return; }
        logoBounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        int nativeWidth = Mathf.Max(1, logoBounds.width / sourcePixelSize);
        int nativeHeight = Mathf.Max(1, logoBounds.height / sourcePixelSize);
        float fit = Mathf.Min(520f / nativeWidth, 248f / nativeHeight);
        float scale = fit >= 1f ? Mathf.Floor(fit) : fit;
        logoWidth = Mathf.Max(1, Mathf.RoundToInt(nativeWidth * scale * CreditsLogoScale));
        logoHeight = Mathf.Max(1, Mathf.RoundToInt(nativeHeight * scale * CreditsLogoScale));
    }

    void ClearCreditLayout()
    {
        if (creditsRoot != null) { creditsRoot.gameObject.SetActive(false); Destroy(creditsRoot.gameObject); }
        creditsRoot = null; studioLabel = null;
        creditLabels.Clear(); creditHeights.Clear(); creditsTextHeight = 0;
    }

    void EnsureCreditLayout()
    {
        if (creditsRoot != null || textRoot == null) return;
        var node = new GameObject("EndingCredits", typeof(RectTransform), typeof(RectMask2D));
        node.layer = textRoot.gameObject.layer;
        creditsRoot = (RectTransform)node.transform;
        creditsRoot.SetParent(textRoot, false);
        creditsRoot.anchorMin = Vector2.zero; creditsRoot.anchorMax = Vector2.one;
        creditsRoot.offsetMin = creditsRoot.offsetMax = Vector2.zero;
        if (!string.IsNullOrEmpty(studio))
        {
            studioLabel = Label("Studio", 12f, Light, false);
            studioLabel.rectTransform.SetParent(creditsRoot, false);
            studioLabel.alignment = TextAlignmentOptions.Top;
            studioLabel.text = studio;
        }
        foreach (string block in StaffLines)
        {
            var label = Label("CreditBlock" + creditLabels.Count, 10f, Light, true);
            label.rectTransform.SetParent(creditsRoot, false);
            label.alignment = TextAlignmentOptions.Top;
            label.lineSpacing = 2f;
            string text = block ?? string.Empty;
            int headingEnd = text.IndexOf('\n');
            label.text = headingEnd > 0
                ? "<size=9><color=#999999>" + text.Substring(0, headingEnd) + "</color></size>" + text.Substring(headingEnd)
                : text;
            int height = Mathf.Max(TextHeight, LabelHeight(label, label.text, 560));
            creditLabels.Add(label); creditHeights.Add(height);
            creditsTextHeight += height + CreditGapAfter(creditLabels.Count - 1);
        }
        if (creditLabels.Count > 0) creditsTextHeight -= CreditsGap;
        creditsRoot.gameObject.SetActive(false);
    }

    void DrawCredits(int top)
    {
        EnsureCreditLayout();
        if (logoPixels != null)
        {
            int left = (Width - logoWidth) / 2;
            for (int y = Mathf.Max(0, -top); y < Mathf.Min(logoHeight, Height - top); y++)
                for (int x = 0; x < logoWidth; x++)
                {
                    int sx = logoBounds.x + x * logoBounds.width / logoWidth;
                    int sy = logoBounds.y + logoBounds.height - 1 - y * logoBounds.height / logoHeight;
                    var c = logoPixels[sy * logo.width + sx];
                    if (c.a >= 128) Put(left + x, top + y, c);
                }
        }
        else DrawFlatText(LogoPlaceholder, Width / 2, top, LogoTextHeight, Light, int.MaxValue);
        if (creditsRoot == null) return;
        creditsRoot.gameObject.SetActive(true);
        if (studioLabel != null) PlaceLabel(studioLabel, 40, top + LogoHeight + 16, 560, 16);
        int textTop = top + LogoHeight + StudioHeight + CreditsGap;
        for (int i = 0; i < creditLabels.Count; i++)
        {
            PlaceLabel(creditLabels[i], 40, textTop, 560, creditHeights[i]);
            textTop += creditHeights[i] + CreditGapAfter(i);
        }
    }

    /// <summary>The palette color nearest to c (a logo drawn in other colors still lands in 1-bit plus red).</summary>
    static Color32 Nearest(Color32 c)
    {
        Color32 best = Black; int bestDistance = int.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            var p = i == 0 ? Black : i == 1 ? Gray : i == 2 ? Light : Red;
            int d = (c.r - p.r) * (c.r - p.r) + (c.g - p.g) * (c.g - p.g) + (c.b - p.b) * (c.b - p.b);
            if (d < bestDistance) { bestDistance = d; best = p; }
        }
        return best;
    }

    /// <summary>Width of level text in line heights.</summary>
    float MeasureFlat(string text)
    {
        EnsureFont();
        float length = 0f;
        foreach (char c in text ?? string.Empty) length += GlyphOf(c).advance / lineUnit;
        return length;
    }

    // ---- level text (no perspective), centered on cx, the first `visible` letters.
    // keep < 16: only pixels with Bayer < keep (a dithered fade); bold: one more pixel to the right.
    Glyph[] flatGlyphs = Array.Empty<Glyph>();
    float[] flatStarts = Array.Empty<float>();

    void DrawFlatText(string text, int cx, int top, int height, Color32 color, int visible, int keep = 16, bool bold = false)
    {
        if (string.IsNullOrEmpty(text) || top >= Height || top + height < 0) return;
        EnsureFont();
        if (font == null) return;
        if (flatGlyphs.Length < text.Length)
        {
            flatGlyphs = new Glyph[text.Length];
            flatStarts = new float[text.Length];
        }
        var glyphs = flatGlyphs;
        var starts = flatStarts;
        float length = 0f;
        for (int i = 0; i < text.Length; i++) { glyphs[i] = GlyphOf(text[i]); starts[i] = length; length += glyphs[i].advance / lineUnit; }
        int width = Mathf.CeilToInt(length * height);
        int left = cx - width / 2;
        for (int y = Mathf.Max(0, top); y < Mathf.Min(Height, top + height); y++)
            for (int x = Mathf.Max(0, left); x < Mathf.Min(Width, left + width); x++)
            {
                float textX = (x + .5f - left) / height;
                float fontY = ascentLine - (y + .5f - top) / height * lineUnit;
                int letter = -1;
                for (int i = text.Length - 1; i >= 0; i--) if (textX >= starts[i]) { letter = i; break; }
                if (letter < 0 || letter >= visible) continue;
                if (!Ink(glyphs[letter], (textX - starts[letter]) * lineUnit, fontY, height)) continue;
                if (keep < 16 && Bayer[(y & 3) * 4 + (x & 3)] >= keep) continue;
                Put(x, y, color);
                if (bold) Put(x + 1, y, color);
            }
    }
}
