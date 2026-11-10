using System;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Round 8, lines 7-16 at the grave: everything outside the TV that tells her
/// grief. The backend calls SetGriefTier (0..3) before each line types and
/// ShowFailureWarning two seconds after line 16; this component only presents.
///   Tier 1: the typewriter hesitates (OSLinkMessageController reads TypingTier),
///           the chat shakes 1 px on every heart cue, the red pointer trembles,
///           the TV folds her back 15 degrees.
///   Tier 2: plus one typo per line, comma pauses, the whole screen dims one
///           dither step after each line and comes back, an irregular heartbeat,
///           the pointer trembles harder and sinks, the TV lays the far layer down.
///   Tier 3: squeezed-out letters and dot-by-dot ellipses with a heartbeat each,
///           a misregistered scan line on the chat edge, a darker screen after
///           every line that no longer comes back, the pointer slides down to the
///           Live portrait, the TV leaves only her, the stone and the flowers.
/// Restoring lands on the final look of the tier at once; tier 0 clears all.
/// The failure warning is modal: it cannot be closed or dragged, only confirmed.
/// </summary>
public sealed class OSChapter04GriefPresentation : MonoBehaviour
{
    public const int MaxTier = 3, MaxDarkLevel = 4;
    // Localization keys of the failure warning (Chinese fallback until translated).
    public const string WarningTitleKey = "chapter04.failure_warning.title";
    public const string WarningBodyKey = "chapter04.failure_warning.body";
    public const string WarningConfirmKey = "chapter04.failure_warning.confirm";
    const string WarningTitleZh = "警告", WarningBodyZh = "机体故障：心脏负荷异常", WarningConfirmZh = "开始治疗";

    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 255);
    // Dither thresholds (of 16) for dark levels 1..4: at most half the dots, so the chat stays readable.
    static readonly int[] DarkThresholds = { 0, 2, 4, 6, 8 };
    const float ShakeSeconds = .1f, PulseHoldSeconds = .45f, PulseFadeSeconds = .55f;
    const float GlitchMin = 1.2f, GlitchMax = 2.6f, GlitchSeconds = .07f;

    public static OSChapter04GriefPresentation Current { get; private set; }
    /// <summary>The tier the chapter-4 typewriter uses for its rhythm (0 when there is no presentation).</summary>
    public static int TypingTier => Current != null ? Current.tier : 0;

    /// <summary>The presentation on the OS handle, created on first use.</summary>
    public static OSChapter04GriefPresentation Ensure()
    {
        if (Current != null) return Current;
        var host = OS_Handle.Instance;
        if (host == null) return null;
        var found = host.GetComponent<OSChapter04GriefPresentation>();
        return found != null ? found : host.gameObject.AddComponent<OSChapter04GriefPresentation>();
    }

    public event Action FailureWarningConfirmed;

    int tier, step, darkBase;
    float pulseUntil = -1f, shakeUntil = -1f, nextGlitch, glitchUntil = -1f;
    bool paused;
    float pauseStartedAt, pausedSeconds;
    // Every effect deadline uses this clock. Pausing preserves remaining time
    // instead of letting unscaled wall time expire its hold/jitter/glitch timers.
    float PresentationTime => (paused ? pauseStartedAt : Time.unscaledTime) - pausedSeconds;
    public int Tier => tier;
    public bool IsPaused => paused;
    /// <summary>Screen dither level this frame (0 clear .. 4).</summary>
    public int DarkLevel => Mathf.Clamp(darkBase + (PulseLevel > 0 ? PulseLevel : 0), 0, MaxDarkLevel);
    public bool IsFailureWarningVisible => warningRoot != null && warningRoot.gameObject.activeSelf;
    public bool IsChatShaking => PresentationTime < shakeUntil;
    public bool IsGlitchVisible => glitch != null && glitch.gameObject.activeSelf;
    public bool OwnsPointer => pointer != null && pointer.OwnsScriptActor(this);
    public Button WarningButton => warningButton;
    public RectTransform WarningPanel => warningPanel;

    // Tier 2: one dither step after each line, gone again within a second.
    int PulseLevel => pulseUntil > 0f && PresentationTime < pulseUntil ? 1 : 0;

    void Awake() => Current = this;
    void OnEnable() => GameEvents.LanguageChanged += OnLanguageChanged;
    void OnDisable() => GameEvents.LanguageChanged -= OnLanguageChanged;
    void OnLanguageChanged(LanguageState language)
    {
        if (IsFailureWarningVisible) RefreshWarningText();
    }
    void OnDestroy()
    {
        GameEvents.LanguageChanged -= OnLanguageChanged;
        if (Current == this) Current = null;
        Unsubscribe();
        RestoreShake();
        ReleasePointer();
        RestoreHeartbeat();
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
        if (warningCanvas != null) Destroy(warningCanvas.gameObject);
        if (ditherTexture != null) Destroy(ditherTexture);
    }

    // ================================================================ API

    /// <summary>
    /// Grief tier 0..3, called before each line types. The same tier again is
    /// ignored. Restoring places the tier's final state at once (step: index of
    /// the current line inside the tier, 0-based, for the tier-3 darkness and
    /// the pointer's slide). Tier 0 clears every effect.
    /// </summary>
    public void SetGriefTier(int tier, bool restoring) => SetGriefTier(tier, restoring, -1);

    public void SetGriefTier(int value, bool restoring, int lineStep)
    {
        // Ensure() can be followed by a restore before our first Update.
        // Read the real state and arm resource callbacks immediately.
        Subscribe();
        var state = ObserveLineState();
        if (restoring)
            typedKey = state?.currentTextCompleted == true ? LineKey(state) : null;
        value = Mathf.Clamp(value, 0, MaxTier);
        bool changed = value != tier;
        if (!changed && !restoring)
        {
            // Same tier, next line: the backend supplies its zero-based step.
            if (lineStep >= 0) step = lineStep;
            return;
        }
        tier = value;
        step = lineStep >= 0 ? lineStep : changed ? 0 : step;
        ForwardToTheater(restoring);
        if (tier == 0) { ClearEffects(); return; }
        if (restoring)
        {
            // Tier 3's darkness is one step per finished line; tier 2 always comes back to clear.
            darkBase = tier >= 3 ? Mathf.Clamp(step + (CurrentLineTyped ? 1 : 0), 0, MaxDarkLevel) : 0;
            pulseUntil = -1f;
        }
        if (tier < 3) darkBase = 0;
        ApplyHeartbeat();
        AcquirePointer(restoring);
        RefreshOverlay();
    }

    /// <summary>Freeze screen effects without clearing the tier, warning or local progress.</summary>
    public void SetPaused(bool value)
    {
        if (paused == value) return;
        if (value) pauseStartedAt = Time.unscaledTime;
        else pausedSeconds += Mathf.Max(0f, Time.unscaledTime - pauseStartedAt);
        paused = value;
        if (pointer != null && pointer.OwnsScriptActor(this)) pointer.ScriptActorSetPaused(this, paused);
        if (warningButton != null) warningButton.interactable = !paused;
    }

    /// <summary>
    /// The modal machine-failure warning (two seconds after line 16). Restoring
    /// shows it without the skipped heartbeat. Only its button closes it.
    /// </summary>
    public void ShowFailureWarning(bool restoring)
    {
        BuildWarning();
        if (warningRoot == null) return;
        warningButton.interactable = !paused;
        if (!IsFailureWarningVisible)
        {
            warningRoot.gameObject.SetActive(true);
            warningRoot.SetAsLastSibling();
            RefreshWarningText();
            if (!restoring) SkipOneHeartbeat();
        }
    }

    /// <summary>Hide the warning without confirming (leaving the chapter, a reload, QA).</summary>
    public void HideFailureWarning()
    {
        if (warningRoot != null) warningRoot.gameObject.SetActive(false);
    }

    /// <summary>The warning's button: closes it and raises FailureWarningConfirmed once.</summary>
    public void ConfirmFailureWarning()
    {
        if (paused || !IsFailureWarningVisible) return;
        HideFailureWarning();
        // The treatment opens next and must be seen: the dark, glitch and pointer let go.
        darkBase = 0; pulseUntil = -1f;
        RefreshOverlay();
        ReleasePointer();
        FailureWarningConfirmed?.Invoke();
    }

    /// <summary>Tier 0 and no warning (chapter cleared or left).</summary>
    public void ClearAll()
    {
        HideFailureWarning();
        SetPaused(false);
        tier = 0; step = 0;
        ForwardToTheater(true);
        ClearEffects();
    }

    /// <summary>A line finished typing (polled from the story; QA may call it directly).</summary>
    public void NotifyLineTyped()
    {
        if (tier == 2) pulseUntil = PresentationTime + PulseHoldSeconds + PulseFadeSeconds;
        else if (tier >= 3) darkBase = Mathf.Min(MaxDarkLevel, darkBase + 1);
        RefreshOverlay();
    }

    /// <summary>A heart cue landed (the 「咚」): the chat window jolts 1 px.</summary>
    public void NotifyHeartCue()
    {
        if (tier < 1) return;
        shakeUntil = PresentationTime + ShakeSeconds;
    }

    // ================================================================ frame

    OSChapter04StoryDirector observedStory;
    OSChapter04StoryState typedState;
    string typedKey;
    Vector2 shakeOffset;
    RectTransform shakenRoot;

    void Update()
    {
        Subscribe();
        if (!paused)
        {
            PollLineTyped();
            TickShake();
            TickPointer();
            TickHeartbeat();
            TickGlitch();
        }
        RefreshOverlay();
        // A TV window opened later picks the tier up as a restore.
        var view = TheaterView;
        if (view != null && view.Stage.GriefTier != tier) view.SetGriefTier(tier, true);
    }

    void Subscribe()
    {
        var story = OS_Handle.Instance?.Chapter04Story;
        if (ReferenceEquals(story, observedStory)) return;
        Unsubscribe();
        observedStory = story;
        if (observedStory != null)
        {
            observedStory.ResourceCueCommitted += OnResourceCue;
            observedStory.ProgressChanged += OnStoryProgressChanged;
        }
    }

    void Unsubscribe()
    {
        if (observedStory != null)
        {
            observedStory.ResourceCueCommitted -= OnResourceCue;
            observedStory.ProgressChanged -= OnStoryProgressChanged;
        }
        observedStory = null;
        typedState = null; typedKey = null;
    }

    void OnStoryProgressChanged()
    {
        // A quick Continue can replace the line before the next Update.
        // The completion receipt is synchronous; PollLineTyped remains a fallback.
        if (!paused && !OSSessionPersistence.IsRestoring) PollLineTyped();
    }

    void OnResourceCue(OSChapter04StoryDirector.ResourceCue cue)
    {
        if (cue.HeartDelta != 0 || cue.HeartWeight != 0) NotifyHeartCue();
    }

    bool CurrentLineTyped => observedStory?.State?.currentTextCompleted == true;

    static string LineKey(OSChapter04StoryState state) => state.currentVisit + ":" + state.currentEntryId;

    OSChapter04StoryState ObserveLineState()
    {
        var state = observedStory?.State;
        if (!ReferenceEquals(state, typedState))
        {
            typedState = state;
            typedKey = null;
        }
        return state;
    }

    void PollLineTyped()
    {
        var state = ObserveLineState();
        if (state == null || tier < 2) { typedKey = null; return; }
        string key = LineKey(state);
        if (!state.currentTextCompleted || key == typedKey) return;
        typedKey = key;
        NotifyLineTyped();
    }

    OSChapter04ChatView ChatView => observedStory != null ? observedStory.GetComponent<OSChapter04ChatView>() : null;

    void TickShake()
    {
        var root = ChatView != null && ChatView.OwnsLayout ? ChatView.Root : null;
        bool on = root != null && IsChatShaking;
        if (shakenRoot != null && (!on || shakenRoot != root))
            RestoreShake();
        if (!on || shakenRoot != null) return;
        shakenRoot = root; shakeOffset = new Vector2(1f, 0f);
        root.anchoredPosition += shakeOffset;
    }

    void RestoreShake()
    {
        if (shakenRoot != null) shakenRoot.anchoredPosition -= shakeOffset;
        shakenRoot = null; shakeOffset = Vector2.zero;
    }

    // ---- red pointer
    OSCursorReleaseDirector pointer;
    Vector2 pointerAnchor;
    float pointerSink, pointerSlide, nextJitter;
    Vector2 pointerJitter;
    int jitterBeat;

    void AcquirePointer(bool restoring)
    {
        if (tier < 1) { ReleasePointer(); return; }
        if (pointer == null) pointer = OS_Handle.Instance?.CursorRelease;
        if (pointer == null) return;
        if (!pointer.OwnsScriptActor(this))
        {
            if (!pointer.TryAcquireScriptActor(this)) return;
            pointer.ScriptActorSetVisible(this, true);
            pointer.ScriptActorSetPaused(this, paused);
            // Keep room to tremble and sink: a pointer parked at a screen edge would be clamped still.
            float margin = 24f * PixelScale;
            Vector2 at = pointer.CompanionScreenPosition;
            pointerAnchor = new Vector2(Mathf.Clamp(at.x, margin, Screen.width - margin), Mathf.Clamp(at.y, margin * 3f, Screen.height - margin));
            pointerSink = 0f; pointerSlide = 0f;
        }
        if (restoring)
        {
            pointerSink = tier >= 2 ? MaxSinkPixels * PixelScale : 0f;
            pointerSlide = tier >= 3 ? SlideTarget : 0f;
        }
    }

    void ReleasePointer()
    {
        if (pointer != null && pointer.OwnsScriptActor(this)) pointer.ReleaseScriptActor(this);
    }

    const float MaxSinkPixels = 10f, SinkPixelsPerSecond = 2f;
    float SlideTarget => tier >= 3 ? Mathf.Clamp01(step / 3f) : 0f;
    float PixelScale
    {
        get
        {
            var canvas = ChatView != null && ChatView.Root != null ? ChatView.Root.GetComponentInParent<Canvas>() : null;
            return canvas != null ? Mathf.Max(1f, canvas.rootCanvas.scaleFactor) : 1f;
        }
    }

    /// <summary>Beside the Live portrait in the chat window (screen point), or the anchor if it is not there.</summary>
    Vector2 PortraitEdge
    {
        get
        {
            var portrait = ChatView != null && ChatView.OwnsLayout ? ChatView.PortraitRect : null;
            if (portrait == null) return pointerAnchor;
            var corners = new Vector3[4];
            portrait.GetWorldCorners(corners);
            var canvas = portrait.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 lowRight = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);
            return lowRight + new Vector2(6f, 24f) * PixelScale;
        }
    }

    void TickPointer()
    {
        if (tier < 1 || pointer == null || !pointer.OwnsScriptActor(this)) return;
        float px = PixelScale;
        if (tier >= 2) pointerSink = Mathf.Min(MaxSinkPixels * px, pointerSink + SinkPixelsPerSecond * px * Time.unscaledDeltaTime);
        if (tier >= 3) pointerSlide = Mathf.MoveTowards(pointerSlide, SlideTarget, Time.unscaledDeltaTime / 2.5f);
        bool settled = tier >= 3 && step >= 3 && Mathf.Approximately(pointerSlide, 1f);
        if (PresentationTime >= nextJitter)
        {
            // A steady left-right shiver (never still two beats running) with a random vertical twitch.
            int reach = tier == 1 ? 1 : 2;
            jitterBeat = (jitterBeat + 1) % 4;
            int sideways = jitterBeat == 1 ? reach : jitterBeat == 3 ? -reach : 0;
            pointerJitter = settled ? Vector2.zero : new Vector2(sideways, UnityEngine.Random.Range(-reach, reach + 1)) * px;
            nextJitter = PresentationTime + (tier == 1 ? .09f : tier == 2 ? .06f : .04f);
        }
        Vector2 sunk = pointerAnchor + new Vector2(0f, -pointerSink);
        Vector2 at = Vector2.Lerp(sunk, PortraitEdge, pointerSlide) + pointerJitter;
        pointer.ScriptActorSetPosition(this, at);
    }

    /// <summary>Where the pointer is placed this frame (QA).</summary>
    public float PointerSlide => pointerSlide;

    // ---- heartbeat panel
    float savedInterval = -1f, nextIrregular;
    float skipUntil = -1f;

    void ApplyHeartbeat()
    {
        var wave = OS_Handle.Instance?.HeartbeatWaveform;
        if (wave == null) return;
        if (tier >= 2 && savedInterval < 0f) savedInterval = wave.NaturalBeatInterval;
        if (tier < 2) RestoreHeartbeat();
    }

    void TickHeartbeat()
    {
        var wave = OS_Handle.Instance?.HeartbeatWaveform;
        if (wave == null) return;
        if (skipUntil > 0f && PresentationTime >= skipUntil) { wave.SetBeatSfxEnabled(true); skipUntil = -1f; }
        if (tier < 2 || savedInterval < 0f || PresentationTime < nextIrregular) return;
        // An irregular rhythm: every beat interval is redrawn, faster at tier 3.
        float lo = tier >= 3 ? .38f : .5f, hi = tier >= 3 ? 1.05f : 1.3f;
        wave.SetNaturalBeatInterval(savedInterval * UnityEngine.Random.Range(lo, hi));
        nextIrregular = PresentationTime + UnityEngine.Random.Range(.35f, .9f);
    }

    void RestoreHeartbeat()
    {
        var wave = OS_Handle.Instance?.HeartbeatWaveform;
        if (wave != null && savedInterval > 0f) wave.SetNaturalBeatInterval(savedInterval);
        savedInterval = -1f;
        if (wave != null && skipUntil > 0f) wave.SetBeatSfxEnabled(true);
        skipUntil = -1f;
    }

    /// <summary>The heartbeat sound holds its breath for one beat when the warning appears.</summary>
    void SkipOneHeartbeat()
    {
        var wave = OS_Handle.Instance?.HeartbeatWaveform;
        if (wave == null) return;
        wave.SetBeatSfxEnabled(false);
        skipUntil = PresentationTime + Mathf.Max(.6f, wave.NaturalBeatInterval) * 1.1f;
    }

    // ---- tier-3 scan line on the chat edge
    Image glitch;

    void TickGlitch()
    {
        var chat = ChatView;
        var root = chat != null && chat.OwnsLayout ? chat.Root : null;
        if (tier < 3 || root == null)
        {
            if (glitch != null) glitch.gameObject.SetActive(false);
            return;
        }
        if (glitch == null || glitch.transform.parent != root)
        {
            if (glitch != null) Destroy(glitch.gameObject);
            var rect = new GameObject("GriefScanLine", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = root.gameObject.layer;
            rect.SetParent(root, false);
            glitch = rect.gameObject.AddComponent<Image>();
            glitch.color = Ink; glitch.raycastTarget = false;
            rect.gameObject.SetActive(false);
            nextGlitch = PresentationTime + UnityEngine.Random.Range(GlitchMin, GlitchMax);
        }
        if (glitch.gameObject.activeSelf && PresentationTime >= glitchUntil) glitch.gameObject.SetActive(false);
        if (PresentationTime < nextGlitch) return;
        // One edge row slips sideways for a couple of frames: a misregistered scan line.
        var line = (RectTransform)glitch.transform;
        bool left = UnityEngine.Random.value < .5f;
        float height = root.rect.height, width = UnityEngine.Random.Range(24f, 70f);
        line.anchorMin = line.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        line.pivot = new Vector2(left ? 0f : 1f, 1f);
        line.sizeDelta = new Vector2(width, 1f);
        line.anchoredPosition = new Vector2(left ? -3f : 3f, -Mathf.Round(UnityEngine.Random.Range(4f, height - 4f)));
        line.SetAsLastSibling();
        glitch.gameObject.SetActive(true);
        glitchUntil = PresentationTime + GlitchSeconds;
        nextGlitch = PresentationTime + UnityEngine.Random.Range(GlitchMin, GlitchMax);
    }

    // ---- full-screen dither
    Canvas overlayCanvas;
    RawImage overlay;
    Texture2D ditherTexture;
    int shownDark = -1;

    Transform OverlayParent
    {
        get
        {
            var window = OS_Handle.Instance?.CharacterWindowController;
            var canvas = window != null ? window.GetComponentInParent<Canvas>() : null;
            return canvas != null ? canvas.rootCanvas.transform : null;
        }
    }

    void RefreshOverlay()
    {
        int level = tier > 0 ? DarkLevel : 0;
        if (level <= 0 && overlayCanvas == null) { shownDark = 0; return; }
        if (overlayCanvas == null)
        {
            var parent = OverlayParent;
            if (parent == null) return;
            var node = new GameObject("Chapter04GriefDither", typeof(RectTransform));
            node.layer = parent.gameObject.layer;
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            overlayCanvas = node.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true; overlayCanvas.sortingOrder = 32000;
            overlay = node.AddComponent<RawImage>();
            overlay.raycastTarget = false;
            ditherTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            overlay.texture = ditherTexture;
            shownDark = -1;
        }
        var area = (RectTransform)overlay.transform;
        overlay.uvRect = new Rect(0f, 0f, Mathf.Max(1f, area.rect.width) / 4f, Mathf.Max(1f, area.rect.height) / 4f);
        if (level == shownDark) return;
        shownDark = level;
        overlay.enabled = level > 0;
        if (level <= 0) return;
        int threshold = DarkThresholds[Mathf.Clamp(level, 0, MaxDarkLevel)];
        var pixels = new Color32[16];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                pixels[y * 4 + x] = OSChapter04TheaterPiece.Bayer[y * 4 + x] < threshold
                    ? new Color32(0, 0, 0, 255) : new Color32(0, 0, 0, 0);
        ditherTexture.SetPixels32(pixels);
        ditherTexture.Apply(false);
    }

    void ClearEffects()
    {
        darkBase = 0; pulseUntil = -1f; shakeUntil = -1f;
        typedState = null; typedKey = null;
        glitchUntil = -1f;
        nextGlitch = PresentationTime + UnityEngine.Random.Range(GlitchMin, GlitchMax);
        nextJitter = nextIrregular = PresentationTime;
        RestoreShake();
        RefreshOverlay();
        if (glitch != null) glitch.gameObject.SetActive(false);
        ReleasePointer();
        RestoreHeartbeat();
    }

    OSChapter04TheaterView TheaterView
    {
        get
        {
            var tv = OS_Handle.Instance?.TVController;
            return tv != null && tv.Screen != null ? tv.Screen.GetComponent<OSChapter04TheaterView>() : null;
        }
    }

    void ForwardToTheater(bool restoring)
    {
        var view = TheaterView;
        if (view != null) view.SetGriefTier(tier, restoring);
    }

    // ---- failure warning (modal, OS system-window style)
    Canvas warningCanvas;
    RectTransform warningRoot, warningPanel;
    TMP_Text warningTitle, warningBody, warningLabel;
    Button warningButton;

    void BuildWarning()
    {
        if (warningCanvas != null) return;
        var parent = OverlayParent;
        var template = OS_Handle.Instance?.LinkMessageController?.DisplayedTextLabel;
        if (parent == null) return;
        var node = new GameObject("Chapter04FailureWarning", typeof(RectTransform));
        node.layer = parent.gameObject.layer;
        warningRoot = (RectTransform)node.transform;
        warningRoot.SetParent(parent, false);
        Stretch(warningRoot);
        warningCanvas = node.AddComponent<Canvas>();
        warningCanvas.overrideSorting = true; warningCanvas.sortingOrder = 32100;
        node.AddComponent<GraphicRaycaster>();
        // A clear blocker: nothing behind the warning can be clicked or dragged.
        var blocker = node.AddComponent<Image>(); blocker.color = new Color(0f, 0f, 0f, 0f); blocker.raycastTarget = true;

        warningPanel = Rect("Panel", warningRoot);
        warningPanel.anchorMin = warningPanel.anchorMax = new Vector2(.5f, .5f);
        warningPanel.sizeDelta = new Vector2(176f, 80f);
        Fill(warningPanel, Ink);                                     // 1 px border
        var inner = Rect("Inner", warningPanel); Stretch(inner, 1f); Fill(inner, Color.black);
        var titleBar = Rect("TitleBar", inner);
        titleBar.anchorMin = new Vector2(0f, 1f); titleBar.anchorMax = Vector2.one; titleBar.pivot = new Vector2(.5f, 1f);
        titleBar.sizeDelta = new Vector2(0f, 15f);
        var rule = Rect("Rule", titleBar);
        rule.anchorMin = Vector2.zero; rule.anchorMax = new Vector2(1f, 0f); rule.sizeDelta = new Vector2(0f, 1f); Fill(rule, Ink);
        var icon = Rect("WarningIcon", titleBar);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, .5f); icon.pivot = new Vector2(0f, .5f);
        icon.anchoredPosition = new Vector2(5f, 0f); icon.sizeDelta = new Vector2(9f, 8f);
        var iconImage = icon.gameObject.AddComponent<RawImage>(); iconImage.texture = WarningIcon(); iconImage.raycastTarget = false;
        warningTitle = Label("Title", titleBar, template, OSPalette.Red);
        Place(warningTitle.rectTransform, new Vector2(17f, 0f), new Vector2(-4f, -1f));
        warningTitle.alignment = TextAlignmentOptions.MidlineLeft;
        warningBody = Label("Body", inner, template, Ink);
        Place(warningBody.rectTransform, new Vector2(8f, 26f), new Vector2(-8f, -18f));
        warningBody.alignment = TextAlignmentOptions.Center; warningBody.enableWordWrapping = true;

        var buttonRect = Rect("Confirm", inner);
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.5f, 0f); buttonRect.pivot = new Vector2(.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 6f); buttonRect.sizeDelta = new Vector2(70f, 16f);
        var frame = Fill(buttonRect, Ink); frame.raycastTarget = true;
        var face = Rect("Face", buttonRect); Stretch(face, 1f);
        var faceImage = Fill(face, Color.black);
        warningLabel = Label("Label", face, template, Ink);
        Stretch(warningLabel.rectTransform); warningLabel.alignment = TextAlignmentOptions.Center;
        warningButton = buttonRect.gameObject.AddComponent<Button>();
        warningButton.transition = Selectable.Transition.None;
        warningButton.targetGraphic = frame;
        warningButton.interactable = !paused;
        warningButton.onClick.AddListener(ConfirmFailureWarning);
        var hover = buttonRect.gameObject.AddComponent<OSChapter04WarningButtonHover>();
        hover.Initialize(faceImage, warningLabel);
        warningRoot.gameObject.SetActive(false);
    }

    void RefreshWarningText()
    {
        var data = GameDataManager.Instance;
        warningTitle.text = data != null ? data.GetLocalizedText(WarningTitleKey, WarningTitleZh) : WarningTitleZh;
        warningBody.text = data != null ? data.GetLocalizedText(WarningBodyKey, WarningBodyZh) : WarningBodyZh;
        warningLabel.text = data != null ? data.GetLocalizedText(WarningConfirmKey, WarningConfirmZh) : WarningConfirmZh;
        foreach (var text in new[] { warningTitle, warningBody, warningLabel }) OSLocalizedFontUtility.ApplyCurrent(text);
    }

    /// <summary>A small red warning triangle with an ink exclamation mark, 9 x 8, 1-bit plus red.</summary>
    static Texture2D WarningIcon()
    {
        string[] rows =
        {
            "....r....",
            "...rrr...",
            "...r.r...",
            "..rr.rr..",
            "..rr.rr..",
            ".rrrrrrr.",
            ".rrr.rrr.",
            "rrrrrrrrr",
        };
        var texture = new Texture2D(9, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 9; x++)
                texture.SetPixel(x, 7 - y, rows[y][x] == 'r' ? (Color)OSPalette.Red : new Color(0f, 0f, 0f, 0f));
        texture.Apply(false);
        return texture;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var node = new GameObject(name, typeof(RectTransform)); node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform; rect.SetParent(parent, false); return rect;
    }
    static Image Fill(RectTransform rect, Color color)
    { var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
    static void Stretch(RectTransform rect, float inset = 0f)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset); }
    static void Place(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
    static TMP_Text Label(string name, Transform parent, TMP_Text template, Color color)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (template != null) { text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; }
        text.fontSize = 10f; text.color = color; text.raycastTarget = false; text.enableWordWrapping = false;
        return text;
    }
}

/// <summary>Hover fill for the warning button, like the chat replies: gray face, black label.</summary>
public sealed class OSChapter04WarningButtonHover : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 255);
    Image face; TMP_Text label;
    public void Initialize(Image face, TMP_Text label) { this.face = face; this.label = label; Set(false); }
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data) => Set(true);
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data) => Set(false);
    void OnDisable() => Set(false);
    void Set(bool hover)
    {
        if (face != null) face.color = hover ? Ink : Color.black;
        if (label != null) label.color = hover ? Color.black : Ink;
    }
}
