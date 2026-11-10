using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSChapter03StoryPresentation
{
    RectTransform mirrorRoot, reasoningRoot, thoughtRoot, rainRoot;
    Canvas mirrorCanvas;
    TMP_Text mirrorAttention, reasoningText, reasoningBeat, thoughtText;
    sealed class MirrorAttentionGlyphShake
    {
        public int index;
        public Tween tween;
        public float delay;
    }
    readonly List<MirrorAttentionGlyphShake> mirrorAttentionGlyphShakes = new List<MirrorAttentionGlyphShake>();
    readonly System.Random mirrorAttentionRandom = new System.Random();
    DOTweenTMPAnimator mirrorAttentionAnimator;
    bool mirrorAttentionShaking;
    static readonly Vector2 MirrorAttentionPosition = new Vector2(0f, -82f);
    CanvasGroup thoughtGroup;
    Image thoughtStrip; // set when the thoughts run on the close-up map's white strip
    const float ThoughtStripHeight = 26f, ThoughtStripSeconds = .28f;
    RawImage mirrorRipple, rainImage;
    Image mirrorFace, mirrorEyes;
    Texture2D rippleTexture, rainTexture;
    Color32[] ripplePixels, rainPixels;
    float rippleClock = -1f, rippleDuration;
    float mirrorRevealClock = -1f;
    float reasoningClock, reasoningDuration, reasoningNextLine;
    int reasoningLine;
    string[] reasoningLines, thoughtLines;
    readonly Queue<string> reasoningHistory = new Queue<string>();
    Action reasoningDone, thoughtsDone;
    OSHomeView reasoningRules; // the 「家」 view while it plays the reasoning as Baba rules
    float thoughtClock, thoughtLineSeconds;
    int thoughtIndex;
    float rainClock, rainPaintClock, rainStrength, rainTarget;
    const int RainWidth = 640, RainHeight = 360, RippleSize = 176;
    const float MirrorEyeSeconds = .4f, MirrorFaceDelay = .45f, MirrorFaceSeconds = .9f;
    RectTransform transientNote;
    CanvasGroup transientNoteGroup;
    float transientNoteTime;

    public void FlashRememberedNote(string title, string body)
    {
        RequireInitialized(); ClearTransientNote();
        transientNote = Root("Chapter03RememberedNote", windowParent);
        transientNote.sizeDelta = new Vector2(PaperWidth("note"), 96f);
        var canvas = transientNote.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = ModalOrder;
        transientNoteGroup = transientNote.gameObject.AddComponent<CanvasGroup>();
        transientNoteGroup.blocksRaycasts = false; transientNoteGroup.interactable = false;
        // The same torn note as the real one, flashed without its buttons.
        var size = transientNote.sizeDelta;
        transientTexture = RequireSheet(transientTexture, (int)size.x, (int)size.y);
        PaintSheet(transientTexture, "note", (body ?? string.Empty).Length * 131 + 7, null, PaperSide, PaperSide);
        SheetImage(transientNote, "Paper").texture = transientTexture;
        var label = ModalText(transientNote, "Title", title, new Vector2(-size.x * .5f + PaperSide + 40f, size.y * .5f - 16f),
            new Vector2(80f, 18f), 11, Dim);
        label.alignment = TextAlignmentOptions.Left; label.textWrappingMode = TextWrappingModes.NoWrap;
        FitModalText(label, 8, 11);
        label.ForceMeshUpdate(true);
        float tab = Mathf.Ceil(label.preferredWidth) + 10f;
        var tabImage = PixelImage(transientNote, "TitleTab", Color.black,
            new Vector2(-size.x * .5f + PaperSide - 4f + tab * .5f, size.y * .5f - 16f), new Vector2(tab, 15f));
        tabImage.transform.SetSiblingIndex(label.transform.GetSiblingIndex());
        var text = ModalText(transientNote, "RememberedText", body, new Vector2(0f, -9f),
            new Vector2(size.x - PaperSide * 2f, size.y - PaperTop - PaperBottom + 4f), PaperTextSize, Color.black);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.GetComponent<OSChapter03PixelText>().UseInkWeight(0f);
        FitModalText(text, 8, PaperTextSize);
        transientNoteTime = 1.1f; Glow(transientNote);
    }
    void TickTransientNote(float dt)
    {
        if (transientNote == null) return;
        transientNoteTime -= dt;
        if (transientNoteTime <= 0f) { ClearTransientNote(); return; }
        transientNoteGroup.alpha = paused ? 0f : Mathf.Clamp01(transientNoteTime / .25f);
    }
    void ClearTransientNote()
    {
        DestroyRoot(transientNote); transientNote = null; transientNoteGroup = null; transientNoteTime = 0f;
        if (transientTexture != null) Destroy(transientTexture);
        transientTexture = null;
    }

    public bool IsMirrorForeground => mirrorRoot != null && mirrorRoot.gameObject.activeInHierarchy
        && !paused && isActiveAndEnabled && os != null && os.IsOSVisible && !OSSessionPersistence.IsRestoring;

    // Link's component sleeps when its message window closes. Keep its existing
    // Live lift running through those gaps while the mirror remains on screen.
    void LateUpdate()
    {
        if (mirrorCanvas != null) RefreshMirrorForeground();
        LateTickCloseup();
        RefreshMirrorPlaybackPresentation();
    }

    void RefreshMirrorForeground()
    {
        if (mirrorCanvas != null)
        {
            var parentCanvas = windowParent.GetComponentInParent<Canvas>();
            // Settings uses the ordinary stack. Inherit it while paused, then
            // return above pinned Memory/Home without changing their saved pins.
            // Enable independent sorting before setting its order; inherited
            // canvases can otherwise keep the parent's order for the first frame.
            mirrorCanvas.overrideSorting = parentCanvas != null && IsMirrorForeground;
            if (mirrorCanvas.overrideSorting)
            {
                mirrorCanvas.sortingLayerID = parentCanvas.sortingLayerID;
                mirrorCanvas.sortingOrder = parentCanvas.sortingOrder + 1;
                mirrorCanvas.worldCamera = rootCanvas.worldCamera;
            }
        }
        os?.LinkMessageController?.RefreshChapter03MirrorFrontLift();
    }

    /// <summary>Above Memory/Home, below Live and its message, including message gaps.</summary>
    public void ShowMirror(string title, string identity, bool revealEyesFirst = true, string attentionText = null)
    {
        // Load both authored layers before replacing an existing presentation.
        // The eyes share the full-face source canvas, keeping their registration
        // exact at any authored pixel resolution within the 144 by 176 UI viewport.
        var faceSprite = RequireArtSprite("mirror-face");
        var eyesSprite = RequireArtSprite("mirror-eyes");
        RequireInitialized(); HideMirror();
        var container = Root("Chapter03Mirror", windowParent);
        container.sizeDelta = new Vector2(252f, 300f);
        var parentRect = windowParent.rect;
        Vector2 half = container.sizeDelta * .5f;
        container.anchoredPosition = new Vector2(
            parentRect.width >= container.sizeDelta.x + 14f
                ? Mathf.Clamp(38f, parentRect.xMin + half.x + 7f, parentRect.xMax - half.x - 7f) : parentRect.center.x,
            parentRect.height >= container.sizeDelta.y + 14f
                ? Mathf.Clamp(10f, parentRect.yMin + half.y + 7f, parentRect.yMax - half.y - 7f) : parentRect.center.y);
        mirrorRoot = container;
        mirrorCanvas = container.gameObject.AddComponent<Canvas>();
        RefreshMirrorForeground();
        var frame = Frame(container, "Frame", title, container.sizeDelta, Vector2.zero);
        frame.GetComponent<Image>().raycastTarget = false;
        Vector2 facePosition = new Vector2(0f, 26f), faceSize = new Vector2(144f, 176f);
        Image(frame, "MirrorGlass", Color.black, facePosition, faceSize);
        mirrorFace = Image(frame, "MirrorFace", Color.white, facePosition, faceSize);
        mirrorFace.sprite = faceSprite; mirrorFace.preserveAspect = true;
        mirrorEyes = Image(frame, "MirrorEyes", Color.white, facePosition, faceSize);
        mirrorEyes.sprite = eyesSprite; mirrorEyes.preserveAspect = true;
        mirrorAttention = Text(frame, "Attention", attentionText, MirrorAttentionPosition, new Vector2(220f, 18f), 12f, OSPalette.Red);
        mirrorAttention.textWrappingMode = TextWrappingModes.NoWrap;
        mirrorAttentionShaking = true;
        mirrorRevealClock = revealEyesFirst ? 0f : -1f;
        ApplyMirrorReveal();
        mirrorRipple = Rect("Ripple", frame, facePosition, new Vector2(RippleSize, RippleSize)).gameObject.AddComponent<RawImage>();
        mirrorRipple.raycastTarget = false;
        rippleTexture = new Texture2D(RippleSize, RippleSize, TextureFormat.RGBA32, false)
        { name = "Chapter03MirrorRipple", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        ripplePixels = new Color32[RippleSize * RippleSize];
        rippleTexture.SetPixels32(ripplePixels); rippleTexture.Apply(false, false);
        mirrorRipple.texture = rippleTexture; mirrorRipple.enabled = false;
        Glow(mirrorRoot);
    }

    void ApplyMirrorReveal()
    {
        if (mirrorFace == null || mirrorEyes == null) return;
        float faceAlpha = mirrorRevealClock < 0f ? 1f : Mathf.Clamp01((mirrorRevealClock - MirrorFaceDelay) / MirrorFaceSeconds);
        float eyesAlpha = faceAlpha >= 1f ? 0f : Mathf.Clamp01(mirrorRevealClock / MirrorEyeSeconds);
        mirrorFace.color = new Color(1f, 1f, 1f, faceAlpha);
        mirrorEyes.color = new Color(1f, 1f, 1f, eyesAlpha);
        RefreshMirrorAttention();
    }

    // These story receipts still exist in older saves; the mirror now has a
    // single attention caption instead of identity/spoken/reflected copies.
    public void SetMirrorIdentity(string identity) { }
    public void SetMirrorDialogue(string text, bool reflected = true) { }

    public void SetMirrorAttentionText(string text)
    {
        if (mirrorAttention == null) return;
        string next = text ?? string.Empty;
        if (mirrorAttention.text != next)
        {
            StopMirrorAttentionShake();
            mirrorAttention.text = next;
        }
        RefreshMirrorAttention();
    }

    /// <summary>Pause the caption's glyphs with the reflection; keep the caption visible.</summary>
    public void SetMirrorAttention(bool shaking)
    {
        mirrorAttentionShaking = shaking;
        RefreshMirrorAttention();
    }

    void RefreshMirrorAttention()
    {
        if (mirrorAttention == null) return;
        bool visible = mirrorRevealClock < 0f && !string.IsNullOrEmpty(mirrorAttention.text);
        mirrorAttention.enabled = visible;
        if (!visible || !mirrorAttentionShaking) { StopMirrorAttentionShake(); return; }
        if (mirrorAttentionAnimator != null) return;
        mirrorAttention.rectTransform.anchoredPosition = MirrorAttentionPosition;
        // Each glyph owns short, freshly generated tremors. Replaying one
        // looping sequence makes the whole caption repeat the same gesture.
        mirrorAttentionAnimator = new DOTweenTMPAnimator(mirrorAttention);
        for (int i = 0; i < mirrorAttentionAnimator.textInfo.characterCount; i++)
        {
            if (!mirrorAttentionAnimator.textInfo.characterInfo[i].isVisible) continue;
            mirrorAttentionGlyphShakes.Add(new MirrorAttentionGlyphShake
                { index = i, delay = MirrorAttentionRange(0f, .12f) });
        }
    }

    float MirrorAttentionRange(float min, float max)
        => Mathf.Lerp(min, max, (float)mirrorAttentionRandom.NextDouble());

    void BeginMirrorAttentionTremor(MirrorAttentionGlyphShake glyph)
    {
        float duration = MirrorAttentionRange(.18f, .36f);
        var strength = new Vector3(MirrorAttentionRange(.4f, .7f), MirrorAttentionRange(.3f, .55f), 0f);
        // DOTween generates shake paths through Unity's random source. Give
        // each new path a private seed without changing exploration randomness.
        var random = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(mirrorAttentionRandom.Next());
            glyph.tween = mirrorAttentionAnimator.DOShakeCharOffset(glyph.index, duration, strength,
                mirrorAttentionRandom.Next(3, 6), 90f, true)
                .SetEase(Ease.Linear).SetUpdate(UpdateType.Manual).SetRecyclable(false);
            glyph.tween.ForceInit();
        }
        finally { UnityEngine.Random.state = random; }
    }

    void TickMirrorAttentionShake(float dt)
    {
        if (mirrorAttentionAnimator == null) return;
        foreach (var glyph in mirrorAttentionGlyphShakes)
        {
            if (glyph.tween != null)
            {
                glyph.tween.ManualUpdate(dt, dt);
                if (!glyph.tween.IsActive())
                {
                    glyph.tween = null;
                    glyph.delay = MirrorAttentionRange(.02f, .09f);
                }
                continue;
            }
            glyph.delay -= dt;
            if (glyph.delay <= 0f) BeginMirrorAttentionTremor(glyph);
        }
    }

    void StopMirrorAttentionShake()
    {
        foreach (var glyph in mirrorAttentionGlyphShakes) glyph.tween?.Kill(false);
        mirrorAttentionGlyphShakes.Clear();
        if (mirrorAttentionAnimator != null)
        {
            if (mirrorAttention != null)
            {
                mirrorAttentionAnimator.Reset();
                DOTweenTMPAnimator.DisposeInstanceFor(mirrorAttention);
            }
            else mirrorAttentionAnimator.Dispose();
            mirrorAttentionAnimator = null;
        }
        if (mirrorAttention != null) mirrorAttention.rectTransform.anchoredPosition = MirrorAttentionPosition;
    }

    /// <summary>Language refresh preserves the mirror's reveal, ripple and shake clocks.</summary>
    public void RefreshMirrorText(string title, string identity, string dialogue = null, string attentionText = null)
    {
        if (mirrorRoot == null) return;
        var titleLabel = mirrorRoot.Find("Frame/Title")?.GetComponent<TMP_Text>();
        if (titleLabel != null && title != null) titleLabel.text = title;
        if (attentionText != null) SetMirrorAttentionText(attentionText);
    }

    /// <summary>Replace translations inside active effects while keeping their current time and line.</summary>
    public void RefreshEffectText(string[] localizedReasoningLines, string heartbeatGlyph, string[] localizedThoughtLines)
    {
        if (reasoningRoot != null)
        {
            if (localizedReasoningLines != null)
            {
                int historyCount = reasoningHistory.Count;
                reasoningLines = localizedReasoningLines;
                reasoningHistory.Clear();
                if (reasoningLines.Length > 0)
                    for (int i = Mathf.Max(0, reasoningLine - historyCount); i < reasoningLine; i++)
                        reasoningHistory.Enqueue(reasoningLines[i % reasoningLines.Length]);
                reasoningText.text = string.Join("\n", reasoningHistory);
            }
            if (reasoningBeat != null && heartbeatGlyph != null) reasoningBeat.text = heartbeatGlyph;
        }
        if (thoughtRoot != null && localizedThoughtLines != null)
        {
            thoughtLines = localizedThoughtLines;
            if (thoughtIndex >= 0 && thoughtIndex < thoughtLines.Length)
            {
                thoughtText.text = OSPalette.TintRichText(thoughtLines[thoughtIndex]);
                if (thoughtStrip != null) FitModalText(thoughtText, 8, 13);
                thoughtText.ForceMeshUpdate(true);
                float p = thoughtClock / thoughtLineSeconds - thoughtIndex;
                thoughtText.maxVisibleCharacters = Mathf.CeilToInt(thoughtText.textInfo.characterCount * Mathf.Clamp01(p / .5f));
                JitterRedWords(thoughtText, thoughtClock);
            }
        }
    }

    public void PulseMirror(float duration = .8f)
    {
        if (mirrorRoot == null) return;
        rippleDuration = Mathf.Max(.1f, duration); rippleClock = 0f; mirrorRipple.enabled = true;
    }
    public void HideMirror()
    {
        StopMirrorAttentionShake(); mirrorAttentionShaking = false;
        DestroyRoot(mirrorRoot); mirrorRoot = null;
        mirrorCanvas = null;
        os?.LinkMessageController?.RefreshChapter03MirrorFrontLift();
        mirrorFace = mirrorEyes = null; mirrorRevealClock = -1f;
        mirrorAttention = null; mirrorRipple = null;
        if (rippleTexture != null) Destroy(rippleTexture);
        rippleTexture = null; ripplePixels = null; rippleClock = -1f;
    }

    public void PlayReasoning(string[] localizedLines, float seconds, Action done, string heartbeatGlyph = null)
    {
        RequireInitialized(); StopReasoning();
        var home = os?.MemoryWindowController?.GetComponent<OSChapter03HomeController>()?.View;
        if (home != null)
        {
            // Baba rules on the 「家」 grid (pancake 10-23); the hex line closes the show untranslated.
            reasoningRules = home;
            string hex = localizedLines != null && localizedLines.Length > 6 ? localizedLines[6] : null;
            home.PlayReasoningRules(seconds, hex, () => { reasoningRules = null; done?.Invoke(); });
            return;
        }
        var host = windowParent;
        reasoningRoot = Root("Chapter03Reasoning", host, true);
        Stretch(Image(reasoningRoot, "Black", Color.black, Vector2.zero, Vector2.zero).rectTransform);
        reasoningText = Text(reasoningRoot, "Stream", "", Vector2.zero,
            host.rect.size - new Vector2(10f, 8f), 11f, Dim);
        reasoningText.alignment = TextAlignmentOptions.BottomLeft;
        reasoningText.textWrappingMode = TextWrappingModes.NoWrap;
        reasoningText.overflowMode = TextOverflowModes.Truncate;
        if (!string.IsNullOrEmpty(heartbeatGlyph))
            reasoningBeat = Text(reasoningRoot, "Heartbeat", heartbeatGlyph, new Vector2(0f, -30f), new Vector2(70f, 22f), 14f, OSPalette.Red);
        reasoningLines = localizedLines ?? Array.Empty<string>(); reasoningDuration = Mathf.Max(.2f, seconds);
        reasoningClock = reasoningNextLine = 0f; reasoningLine = 0; reasoningDone = done;
        reasoningHistory.Clear(); Glow(reasoningRoot);
    }

    /// <param name="strip">The 「家」 map: the lines run on a white strip drawn across its middle
    /// (pancake 10-25), which opens before the first line and closes after the last.</param>
    public void PlayThoughts(string[] localizedRichLines, float secondsPerLine, Action done, RectTransform strip = null)
    {
        RequireInitialized(); StopThoughts();
        thoughtLines = localizedRichLines ?? Array.Empty<string>(); thoughtLineSeconds = Mathf.Max(.8f, secondsPerLine);
        thoughtClock = 0f; thoughtIndex = -1; thoughtsDone = done;
        if (strip != null)
        {
            float width = Mathf.Round(strip.rect.width);
            thoughtRoot = Root("Chapter03ThoughtStrip", strip);
            thoughtRoot.sizeDelta = new Vector2(width, ThoughtStripHeight);
            thoughtStrip = Image(thoughtRoot, "Strip", Ink, Vector2.zero, new Vector2(0f, ThoughtStripHeight));
            thoughtText = Text(thoughtRoot, "Thought", "", Vector2.zero, new Vector2(width - 16f, ThoughtStripHeight), 13f, Color.black);
            thoughtText.richText = true; thoughtText.alpha = 0f;
            thoughtGroup = thoughtRoot.gameObject.AddComponent<CanvasGroup>();
            thoughtGroup.blocksRaycasts = false; thoughtGroup.interactable = false;
            thoughtClock = -ThoughtStripSeconds; Glow(thoughtRoot);
            return;
        }
        thoughtRoot = Root("Chapter03Thoughts", windowParent);
        thoughtRoot.anchoredPosition = new Vector2(0f, -Mathf.Min(135f, windowParent.rect.height * .37f));
        thoughtRoot.sizeDelta = new Vector2(Mathf.Max(120f, windowParent.rect.width - 36f), 46f);
        thoughtGroup = thoughtRoot.gameObject.AddComponent<CanvasGroup>();
        thoughtGroup.blocksRaycasts = false; thoughtGroup.interactable = false;
        thoughtText = Text(thoughtRoot, "Thought", "", Vector2.zero, thoughtRoot.sizeDelta, 16f, Ink);
        thoughtText.richText = true; Glow(thoughtRoot);
    }

    public void SetRain(bool visible, float strength = 1f)
    {
        RequireInitialized(); rainTarget = visible ? Mathf.Clamp01(strength) : 0f;
        if (rainTarget <= 0f || rainRoot != null) return;
        rainRoot = Root("Chapter03DesktopRain", windowParent, true);
        rainRoot.SetAsFirstSibling(); // Entire rain is behind the ordinary window stack.
        rainImage = rainRoot.gameObject.AddComponent<RawImage>(); rainImage.raycastTarget = false;
        rainTexture = new Texture2D(RainWidth, RainHeight, TextureFormat.RGBA32, false)
        { name = "Chapter03DesktopRain", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        rainPixels = new Color32[RainWidth * RainHeight];
        rainTexture.SetPixels32(rainPixels); rainTexture.Apply(false, false); rainImage.texture = rainTexture;
        rainClock = rainPaintClock = 0f; rainStrength = 0f;
    }

    void TickEffects(float dt)
    {
        if (dt <= 0f) return;
        if (mirrorRevealClock >= 0f)
        {
            mirrorRevealClock += dt;
            if (mirrorRevealClock >= MirrorFaceDelay + MirrorFaceSeconds) mirrorRevealClock = -1f;
            ApplyMirrorReveal();
        }
        if (IsMirrorForeground) TickMirrorAttentionShake(dt);
        TickRipple(dt); TickReasoning(dt); TickThoughts(dt); TickRain(dt); TickCloseup(dt);
    }
    void TickRipple(float dt)
    {
        if (rippleClock < 0f || mirrorRipple == null) return;
        rippleClock += dt;
        if (rippleClock >= rippleDuration) { rippleClock = -1f; mirrorRipple.enabled = false; return; }
        Array.Clear(ripplePixels, 0, ripplePixels.Length);
        float t = rippleClock / rippleDuration;
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = t * (RippleSize * .56f) - ring * 13f; if (radius < 1f) continue;
            int samples = Mathf.CeilToInt(radius * 7f);
            for (int point = 0; point < samples; point++)
            {
                float angle = point * Mathf.PI * 2f / samples;
                int x = Mathf.RoundToInt(RippleSize * .5f + Mathf.Cos(angle) * radius);
                int y = Mathf.RoundToInt(RippleSize * .5f + Mathf.Sin(angle) * radius * .72f);
                if (x >= 0 && y >= 0 && x < RippleSize && y < RippleSize && ((x + y + ring) & 1) == 0)
                    ripplePixels[y * RippleSize + x] = new Color32(220, 216, 212, (byte)(255f * (1f - t)));
            }
        }
        rippleTexture.SetPixels32(ripplePixels); rippleTexture.Apply(false, false);
    }
    void TickReasoning(float dt)
    {
        if (reasoningRoot == null) return;
        reasoningClock += dt;
        if (reasoningClock >= reasoningDuration)
        {
            var done = reasoningDone; StopReasoning(); done?.Invoke(); return;
        }
        if (reasoningLines.Length > 0 && reasoningClock >= reasoningNextLine)
        {
            reasoningHistory.Enqueue(reasoningLines[reasoningLine++ % reasoningLines.Length]);
            while (reasoningHistory.Count > 12) reasoningHistory.Dequeue();
            reasoningText.text = string.Join("\n", reasoningHistory);
            reasoningNextLine = reasoningClock + Mathf.Lerp(.22f, .025f, reasoningClock / reasoningDuration);
        }
        if (reasoningBeat != null)
        {
            float pulse = Mathf.Sin(reasoningClock * Mathf.Lerp(10f, 30f, reasoningClock / reasoningDuration));
            reasoningBeat.rectTransform.localScale = Vector3.one * (pulse > .65f ? 1.08f : 1f);
        }
    }
    void TickThoughts(float dt)
    {
        if (thoughtRoot == null) return;
        thoughtClock += dt;
        if (thoughtStrip != null)
        {
            // The strip opens from the middle and closes back into it, 2 px at a time.
            float total = thoughtLines.Length * thoughtLineSeconds;
            float open = thoughtClock < 0f ? 1f + thoughtClock / ThoughtStripSeconds
                : thoughtClock > total ? 1f - (thoughtClock - total) / ThoughtStripSeconds : 1f;
            open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(open));
            thoughtStrip.rectTransform.sizeDelta = new Vector2(
                Mathf.Round(thoughtRoot.sizeDelta.x * open * .5f) * 2f, ThoughtStripHeight);
            if (thoughtClock < 0f) return;
            if (thoughtClock >= total)
            {
                thoughtText.alpha = 0f;
                if (thoughtClock < total + ThoughtStripSeconds) return;
                var finished = thoughtsDone; StopThoughts(); finished?.Invoke(); return;
            }
        }
        int index = Mathf.FloorToInt(thoughtClock / thoughtLineSeconds);
        if (index >= thoughtLines.Length)
        {
            var done = thoughtsDone; StopThoughts(); done?.Invoke(); return;
        }
        if (index != thoughtIndex)
        {
            thoughtIndex = index; thoughtText.text = OSPalette.TintRichText(thoughtLines[index]);
            // Long translations must fit the fixed-height band. Keep integer
            // sizes and refit each line, so shorter thoughts retain 13 px.
            if (thoughtStrip != null) FitModalText(thoughtText, 8, 13);
            thoughtText.maxVisibleCharacters = 0; thoughtText.ForceMeshUpdate();
        }
        float p = thoughtClock / thoughtLineSeconds - index;
        float shown = Mathf.Min(Mathf.Clamp01(p / .1f), Mathf.Clamp01((1f - p) / .2f));
        if (thoughtStrip != null) thoughtText.alpha = shown; else thoughtGroup.alpha = shown;
        thoughtText.maxVisibleCharacters = Mathf.CeilToInt(thoughtText.textInfo.characterCount * Mathf.Clamp01(p / .5f));
        JitterRedWords(thoughtText, thoughtClock);
    }
    static void JitterRedWords(TMP_Text label, float clock)
    {
        label.ForceMeshUpdate();
        Color32 red = OSPalette.Red;
        int phase = (int)(clock * 12f);
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            var c = label.textInfo.characterInfo[i]; if (!c.isVisible) continue;
            if (Mathf.Abs(c.color.r - red.r) > 2 || Mathf.Abs(c.color.g - red.g) > 2 || Mathf.Abs(c.color.b - red.b) > 2) continue;
            var vertices = label.textInfo.meshInfo[c.materialReferenceIndex].vertices;
            var delta = new Vector3(((i + phase) % 3) - 1, ((i * 3 + phase * 7) % 3) - 1, 0f);
            for (int v = 0; v < 4; v++) vertices[c.vertexIndex + v] += delta;
        }
        label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
    void TickRain(float dt)
    {
        if (rainRoot == null) return;
        rainStrength = Mathf.MoveTowards(rainStrength, rainTarget, dt / .8f);
        if (rainStrength <= 0f && rainTarget <= 0f) { StopRain(); return; }
        rainImage.color = new Color(1f, 1f, 1f, rainStrength);
        rainClock += dt; rainPaintClock -= dt; if (rainPaintClock > 0f) return;
        rainPaintClock = .05f; Array.Clear(rainPixels, 0, rainPixels.Length);
        for (int i = 0; i < 95; i++)
        {
            int speed = 100 + (i * 31 % 120);
            int y = (int)(i * 73 + rainClock * speed) % (RainHeight + 12) - 6;
            int x = (i * 137 - (int)(rainClock * 32f)) % RainWidth; if (x < 0) x += RainWidth;
            for (int k = 0; k < 6; k++)
            {
                int xx = x - k / 3, yy = y + k;
                if (xx >= 0 && yy >= 0 && xx < RainWidth && yy < RainHeight)
                    rainPixels[(RainHeight - 1 - yy) * RainWidth + xx] = new Color32(135, 130, 126, 255);
            }
        }
        rainTexture.SetPixels32(rainPixels); rainTexture.Apply(false, false);
    }
    void StopReasoning()
    {
        if (reasoningRules != null) { reasoningRules.StopRules(); reasoningRules = null; }
        DestroyRoot(reasoningRoot); reasoningRoot = null; reasoningText = reasoningBeat = null;
        reasoningDone = null; reasoningLines = null; reasoningHistory.Clear();
    }
    void StopThoughts()
    {
        DestroyRoot(thoughtRoot); thoughtRoot = null; thoughtText = null; thoughtGroup = null; thoughtStrip = null;
        thoughtLines = null; thoughtsDone = null;
    }
    void StopRain()
    {
        DestroyRoot(rainRoot); rainRoot = null; rainImage = null;
        if (rainTexture != null) Destroy(rainTexture);
        rainTexture = null; rainPixels = null; rainStrength = rainTarget = 0f;
    }
    void ResetEffects() { HideCloseup(); HideMirror(); StopReasoning(); StopThoughts(); StopRain(); StopResourceDrains(); }
}
