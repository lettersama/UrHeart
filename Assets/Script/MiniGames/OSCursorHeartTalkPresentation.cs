using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Frontend for the chapter-two heart-to-heart. The director owns story state and
/// calls these hooks; all motion advances on its paused, capped clock
/// (AdvancePresentation), never on Update, so focus/settings pauses freeze it.
///   Bench   - Character's red pointer settles at her own spot beside Live (it does
///             not trail the player's pointer) with a slow sway.
///   Calm    - the hidden-OS wallpaper slows down as trust grows.
///   Quake   - the screen jolts; a full-width crisis band with a red warning mark
///             opens across the middle; light aftershocks.
///   Loading - fragments complete, black sweeps in, her red pointer drags Live aside
///             by its title bar, a dotted ring turns at centre; Live stays open.
///   Return  - the black dissolves through ordered dither onto the real desktop.
/// Only black, ink #87827e and the existing red (pointer / warning) are used.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(600)]
public class OSCursorHeartTalkPresentation : MonoBehaviour
{
    enum Phase { None, Bench, Quake, Loading, Returned }
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 0xff);
    static readonly Color Red = new Color32(216, 52, 58, 255);
    const float QuakeSettleSeconds = .9f, LoadingLineReadySeconds = 1.2f;
    const float RevealSeconds = .4f, SweepStart = .45f, SweepSeconds = .4f, SpinnerStart = .9f;
    const float LiveStepAside = 150f, DissolveStepSeconds = .14f, BandHeight = 34f;
    // Drag choreography (seconds into loading): reach the title bar, press,
    // drag Live aside, let go, then settle at her spot beside it.
    const float DragReach = .25f, DragPress = .6f, DragStart = .7f, DragEnd = 1.25f, DragRelease = 1.35f;

    Phase phase;
    float elapsed, benchTime, aftershockClock;
    bool benchActive, benchPlaced, loadingDragging;
    Vector2 benchPoint, benchVelocity, shake;
    RectTransform alert, overlay, spinner;
    Image overlayImage, alertIcon;
    Image[] spinnerDots, bandStripes;
    Sprite[] ditherSprites, stripeSprites;
    Sprite dotSprite;
    TMP_Text bandTitle, bandBody;
    AudioSource warningVoice;
    AudioClip warningClip;
    bool warningShown, suppressWarningAudio;
    float returnClock = -1f;
    int dissolveStep;
    readonly Vector3[] corners = new Vector3[4];

    public float Calm { get; private set; }
    public bool IsRestoringSnapshot { get; private set; }
    public virtual bool IsQuakeSettled => phase == Phase.Quake && elapsed >= QuakeSettleSeconds;
    public virtual bool IsLoadingReadyForLine => phase == Phase.Loading && elapsed >= LoadingLineReadySeconds;
    // The desktop swap happens under our black; the dissolve continues afterwards.
    public virtual bool IsReturned => phase == Phase.Returned;

    OSCursorReleaseDirector Release => GetComponent<OSCursorReleaseDirector>();
    OSCursorExperimentInteriorPresentation Interior => OS_Handle.Instance?.CursorExperimentInterior;
    OSWindowUnit Live => OS_Handle.Instance?.CharacterWindowController?.GetComponent<OSWindowUnit>();

    // ---------------------------------------------------------------- hooks
    public virtual void BeginBench()
    {
        StopWarningAudio();
        warningShown = suppressWarningAudio = false;
        phase = Phase.Bench; elapsed = 0f;
        Release?.SuspendForHeartTalk();
        benchActive = true; benchPlaced = false; benchTime = 0f;
    }

    public virtual void SetCalm(float amount)
    {
        Calm = Mathf.Clamp01(amount);
        Interior?.Background?.SetCalm(Calm);
    }

    public virtual void TriggerQuake()
    {
        if (phase != Phase.Quake)
        {
            StopWarningAudio();
            warningShown = false;
            // Capture the restore flag now: it is cleared before the first
            // RenderAlert frame, which must never replay the saved warning.
            suppressWarningAudio = IsRestoringSnapshot || OSSessionPersistence.IsRestoring;
        }
        phase = Phase.Quake;
        elapsed = IsRestoringSnapshot ? QuakeSettleSeconds : 0f;
        EnsureAlert();
    }

    public virtual void BeginLoading()
    {
        StopWarningAudio();
        OSChapter02ActivityMusic.NotifyLoadingStarted();
        phase = Phase.Loading; elapsed = 0f;
        Interior?.Background?.RevealAll(RevealSeconds);
        EnsureOverlay();
        SetOverlayWidth(0f);
    }

    public virtual void EndLoading()
    {
        StopWarningAudio();
        phase = Phase.Returned; elapsed = 0f;
        loadingDragging = false;
        Release?.SetHeartTalkCompanionPressed(false);
        // Restored straight to the desktop: nothing to dissolve.
        if (IsRestoringSnapshot || overlay == null) { DestroyVisuals(); return; }
        returnClock = 0f; dissolveStep = 0;
        if (spinner != null) spinner.gameObject.SetActive(false);
    }

    public virtual void AdvancePresentation(float dt)
    {
        dt = Mathf.Clamp(dt, 0f, .1f);
        elapsed += dt;
        if (phase == Phase.Quake) TickQuake(dt);
        else if (phase == Phase.Loading) TickLoading();
        // After loading so the hand follows Live's shifted title bar this frame.
        if (benchActive && phase != Phase.Returned) TickBench(dt);
    }

    public virtual void ResetPresentation()
    {
        StopWarningAudio();
        warningShown = suppressWarningAudio = false;
        phase = Phase.None; elapsed = Calm = 0f;
        benchActive = false; loadingDragging = false; returnClock = -1f;
        SetShake(Vector2.zero);
        if (Release != null) Release.HeartTalkLiveShift = Vector2.zero;
        DestroyVisuals();
    }

    public virtual void RestorePresentation(int savedStep)
    {
        ResetPresentation();
        var step = (OSCursorHeartTalkDirector.HeartTalkStep)savedStep;
        IsRestoringSnapshot = true;
        try
        {
            if (step >= OSCursorHeartTalkDirector.HeartTalkStep.Return) { EndLoading(); return; }
            if (step <= OSCursorHeartTalkDirector.HeartTalkStep.Inactive) return;
            BeginBench();
            SetCalm(step >= OSCursorHeartTalkDirector.HeartTalkStep.Quake ? 1f
                : step >= OSCursorHeartTalkDirector.HeartTalkStep.IdentityReply ? .8f
                : step >= OSCursorHeartTalkDirector.HeartTalkStep.FixClose ? .5f : 0f);
            if (step == OSCursorHeartTalkDirector.HeartTalkStep.Loading) BeginLoading();
            else if (step == OSCursorHeartTalkDirector.HeartTalkStep.Quake
                || step == OSCursorHeartTalkDirector.HeartTalkStep.QuakeLine) TriggerQuake();
        }
        finally { IsRestoringSnapshot = false; }
    }

    // A short warning is consumed when shown. Pausing cuts its remaining tail;
    // returning to focus resumes the presentation without repeating the pulse.
    public virtual void PauseAudio() => StopWarningAudio();

    void StopWarningAudio()
    {
        if (warningVoice != null && warningVoice.clip == warningClip) warningVoice.Stop();
        warningVoice = null;
        warningClip = null;
    }

    void OnApplicationFocus(bool focused) { if (!focused) StopWarningAudio(); }
    void OnApplicationPause(bool paused) { if (paused) StopWarningAudio(); }
    void OnDisable() => ResetPresentation();
    void OnDestroy() => StopWarningAudio();

    // The return dissolve outlives the interior: it runs after Done on its own clock.
    void LateUpdate()
    {
        if (returnClock < 0f || overlay == null) return;
        KeepOverlayUnderLive();
        returnClock += Mathf.Min(Time.unscaledDeltaTime, .05f);
        int step = Mathf.FloorToInt(returnClock / DissolveStepSeconds);
        if (step != dissolveStep) { dissolveStep = step; ApplyDissolve(step); }
        if (step > 4) { returnClock = -1f; DestroyVisuals(); }
    }

    // ---------------------------------------------------------------- bench
    float CanvasUnit()
    {
        var live = Live;
        var canvas = live != null ? live.GetComponentInParent<Canvas>() : null;
        return canvas != null ? canvas.rootCanvas.scaleFactor : Screen.height / 360f;
    }

    bool TryLiveScreenRect(out Rect rect)
    {
        rect = default;
        var live = Live;
        if (live == null || !live.IsOpen || !live.gameObject.activeInHierarchy) return false;
        live.WindowRect.GetWorldCorners(corners);
        var canvas = live.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        var camera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return true;
    }

    /// <summary>Her own spot beside Live: right of the window, or left near the screen edge.</summary>
    Vector2 ParkPoint(float unit)
    {
        if (!TryLiveScreenRect(out Rect live)) return benchPoint;
        float y = live.center.y - 16f * unit;
        float right = live.xMax + 15f * unit;
        return right < Screen.width - 14f * unit ? new Vector2(right, y) : new Vector2(live.xMin - 22f * unit, y);
    }

    /// <summary>Grip on Live's title bar, a little in from its left end.</summary>
    Vector2 GripPoint(float unit)
    {
        if (!TryLiveScreenRect(out Rect live)) return benchPoint;
        return new Vector2(live.xMin + 42f * unit, live.yMax - 6f * unit);
    }

    void TickBench(float dt)
    {
        var release = Release;
        if (release == null) return;
        float unit = CanvasUnit();
        if (!benchPlaced) { benchPoint = release.CompanionScreenPosition; benchVelocity = Vector2.zero; benchPlaced = true; }
        benchTime += dt;
        Vector2 target = phase == Phase.Loading && elapsed >= DragReach && elapsed < DragRelease
            ? GripPoint(unit) : ParkPoint(unit);
        if (loadingDragging) { benchPoint = target; benchVelocity = Vector2.zero; }
        else benchPoint = Vector2.SmoothDamp(benchPoint, target, ref benchVelocity, .3f, 260f * unit, dt);
        Vector2 shown = benchPoint;
        // Settled at her spot: a slow one-pixel sway.
        if (!loadingDragging && (benchPoint - target).sqrMagnitude < unit * unit * 4f)
            shown.y += (Mathf.FloorToInt(benchTime / .55f) % 2 == 0 ? 0f : 1f) * unit;
        shown += shake * unit;
        release.SetHeartTalkCompanionPosition(new Vector2(Mathf.Round(shown.x / unit) * unit, Mathf.Round(shown.y / unit) * unit));
    }

    // ---------------------------------------------------------------- quake
    void SetShake(Vector2 offset)
    {
        shake = offset;
        var root = Interior?.ZoomRoot;
        if (root != null) root.anchoredPosition = new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));
    }

    void TickQuake(float dt)
    {
        // Main jolt: alternating 45ms beats with decaying amplitude.
        if (elapsed < QuakeSettleSeconds)
        {
            int beat = Mathf.FloorToInt(elapsed / .045f);
            float amplitude = 4f * (1f - elapsed / QuakeSettleSeconds) + .5f;
            SetShake(new Vector2((beat % 2 == 0 ? 1f : -1f) * amplitude, ((beat / 2) % 2 == 0 ? .5f : -.5f) * amplitude * .5f));
        }
        else
        {
            // Light aftershocks keep the tension while she speaks.
            aftershockClock += dt;
            float cycle = Mathf.Repeat(aftershockClock, 1.7f);
            SetShake(cycle < .18f ? new Vector2(Mathf.FloorToInt(cycle / .045f) % 2 == 0 ? 1f : -1f, 0f) : Vector2.zero);
        }
        RenderAlert();
    }

    void EnsureAlert()
    {
        var live = Live;
        var root = live != null ? live.WindowRect.parent as RectTransform : Interior?.ZoomRoot;
        if (root == null || alert != null) return;
        // A full-width crisis band across the middle of the screen, above every window.
        alert = MakeRect("HeartTalkCrisisBand", root, Vector2.zero);
        alert.anchorMin = new Vector2(0f, .5f); alert.anchorMax = new Vector2(1f, .5f); alert.pivot = new Vector2(.5f, .5f);
        alert.sizeDelta = new Vector2(0f, BandHeight); alert.anchoredPosition = Vector2.zero;
        Stretch(MakeImage("Back", alert, null, Color.black).rectTransform);
        stripeSprites = new[] {
            PixelSprite(new[] { "II..II..", ".II..II.", "..II..II" }, "HazardA"),
            PixelSprite(new[] { "..II..II", "I..II..I", "II..II.." }, "HazardB") };
        bandStripes = new Image[2];
        var canvas = root.GetComponentInParent<Canvas>();
        float ppu = canvas != null ? canvas.rootCanvas.referencePixelsPerUnit : 20f;
        for (int k = 0; k < 2; k++)
        {
            float edge = k == 0 ? 1f : 0f;
            var stripe = MakeImage(k == 0 ? "HazardTop" : "HazardBottom", alert, stripeSprites[0], Ink);
            stripe.type = Image.Type.Tiled; stripe.pixelsPerUnitMultiplier = ppu;
            var rt = stripe.rectTransform;
            rt.anchorMin = new Vector2(0f, edge); rt.anchorMax = new Vector2(1f, edge);
            rt.pivot = new Vector2(.5f, edge); rt.sizeDelta = new Vector2(0f, 3f); rt.anchoredPosition = Vector2.zero;
            bandStripes[k] = stripe;
            var rule = MakeImage(k == 0 ? "RuleTop" : "RuleBottom", alert, null, Ink).rectTransform;
            rule.anchorMin = new Vector2(0f, edge); rule.anchorMax = new Vector2(1f, edge);
            rule.pivot = new Vector2(.5f, edge); rule.sizeDelta = new Vector2(0f, 1f);
            rule.anchoredPosition = new Vector2(0f, k == 0 ? -4f : 4f);
        }
        // Red warning mark: filled triangle carrying a black exclamation.
        alertIcon = MakeImage("Warning", alert, PixelSprite(new[] {
            "......I......", ".....III.....", ".....III.....", "....II.II....", "....II.II....",
            "...III.III...", "...III.III...", "..IIII.IIII..", "..IIIIIIIII..", ".IIIII.IIIII.", "IIIIIIIIIIIII" }, "CrisisMark"), Red);
        var icon = alertIcon.rectTransform;
        icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(.5f, .5f); icon.sizeDelta = new Vector2(13f, 11f);
        bandTitle = MakeLabel("Title", alert, "ui.os.heart_talk.alert_title", "回忆区", TextAlignmentOptions.MidlineLeft);
        bandBody = MakeLabel("Body", alert, "ui.os.heart_talk.alert_body", "收集完成 · 检测到高强度记忆片段", TextAlignmentOptions.MidlineLeft);
        bandTitle.color = Red;
        foreach (var label in new[] { bandTitle, bandBody })
        {
            label.fontSize = 8f; label.textWrappingMode = TextWrappingModes.NoWrap;
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.pivot = new Vector2(0f, .5f);
        }
        alert.SetAsLastSibling();
        alert.gameObject.SetActive(false);
    }

    void RenderAlert()
    {
        if (alert == null) return;
        bool shown = elapsed >= .35f;
        alert.gameObject.SetActive(shown);
        if (!shown) return;
        if (!warningShown)
        {
            warningShown = true;
            // The band is first visible here, .35 s after TriggerQuake. Keep
            // the music cut and warning pulse on this actual reveal frame.
            OSChapter02ActivityMusic.NotifyWarningShown();
            if (!suppressWarningAudio && !IsRestoringSnapshot && !OSSessionPersistence.IsRestoring)
            {
                warningVoice = Chapter02Audio.Play("Chapter02_MemoryAlert", .80f);
                warningClip = warningVoice != null ? warningVoice.clip : null;
            }
        }
        // Stepped opening from a centre line, then the band holds.
        float open = elapsed - .35f;
        alert.sizeDelta = new Vector2(0f, open < .05f ? 2f : open < .1f ? BandHeight * .5f : BandHeight);
        alertIcon.enabled = Mathf.FloorToInt(open / .45f) % 2 == 0;
        var stripe = stripeSprites[Mathf.FloorToInt(open / .15f) % 2];
        foreach (var image in bandStripes) image.sprite = stripe;
        // Centre [mark  TITLE  body] as one group in any language.
        float titleWidth = bandTitle.GetPreferredValues(bandTitle.text).x;
        float bodyWidth = bandBody.GetPreferredValues(bandBody.text).x;
        bandTitle.rectTransform.sizeDelta = new Vector2(titleWidth + 2f, 12f);
        bandBody.rectTransform.sizeDelta = new Vector2(bodyWidth + 2f, 12f);
        float left = -(13f + 6f + titleWidth + 8f + bodyWidth) * .5f;
        alertIcon.rectTransform.anchoredPosition = new Vector2(Mathf.Round(left + 6.5f), 0f);
        bandTitle.rectTransform.anchoredPosition = new Vector2(Mathf.Round(left + 19f), 0f);
        bandBody.rectTransform.anchoredPosition = new Vector2(Mathf.Round(left + 27f + titleWidth), 0f);
        alert.SetAsLastSibling();
    }

    // ---------------------------------------------------------------- loading
    void TickLoading()
    {
        if (alert != null) alert.gameObject.SetActive(elapsed < .12f);
        SetShake(Vector2.zero);
        var release = Release;
        if (release != null)
        {
            // Live moves only while her pointer holds the title bar.
            float drag = Smooth((elapsed - DragStart) / (DragEnd - DragStart));
            release.HeartTalkLiveShift = new Vector2(Mathf.Round(LiveStepAside * drag), 0f);
            loadingDragging = elapsed >= DragStart && elapsed < DragRelease;
            release.SetHeartTalkCompanionPressed(elapsed >= DragPress && elapsed < DragRelease);
        }
        KeepOverlayUnderLive();
        SetOverlayWidth(Mathf.Clamp01((elapsed - SweepStart) / SweepSeconds));
        if (elapsed >= SpinnerStart) RenderSpinner(elapsed - SpinnerStart);
    }

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    void EnsureOverlay()
    {
        var live = Live;
        var parent = live != null ? live.WindowRect.parent as RectTransform : Interior?.ZoomRoot;
        if (parent == null || overlay != null) return;
        overlay = MakeRect("HeartTalkBlack", parent, Vector2.zero);
        overlay.anchorMin = Vector2.zero; overlay.anchorMax = new Vector2(0f, 1f); overlay.pivot = new Vector2(0f, .5f);
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        overlayImage = overlay.gameObject.AddComponent<Image>();
        overlayImage.color = Color.black; overlayImage.raycastTarget = false;
        spinner = MakeRect("LoadingRing", overlay, new Vector2(24, 24));
        spinner.anchorMin = spinner.anchorMax = new Vector2(.5f, .5f); spinner.pivot = new Vector2(.5f, .5f);
        spinner.gameObject.SetActive(false);
        dotSprite = PixelSprite(new[] { "II", "II" }, "RingDot");
        spinnerDots = new Image[12];
        for (int i = 0; i < spinnerDots.Length; i++)
        {
            float a = Mathf.PI * .5f - i * Mathf.PI * 2f / spinnerDots.Length;
            spinnerDots[i] = MakeImage("Dot" + i, spinner, dotSprite, Ink);
            var rt = spinnerDots[i].rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(2, 2);
            rt.anchoredPosition = new Vector2(Mathf.Round(Mathf.Cos(a) * 9f), Mathf.Round(Mathf.Sin(a) * 9f));
        }
        KeepOverlayUnderLive();
    }

    void SetOverlayWidth(float fraction)
    {
        if (overlay == null) return;
        var parent = overlay.parent as RectTransform;
        float width = parent != null ? parent.rect.width : 640f;
        // Stepped sweep (eight bands) rather than a smooth slide.
        fraction = Mathf.Ceil(Mathf.Clamp01(fraction) * 8f) / 8f;
        overlay.anchorMax = new Vector2(fraction, 1f);
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        if (spinner != null && parent != null)
            spinner.anchoredPosition = new Vector2(width * .5f - width * fraction * .5f, 0f);
    }

    void RenderSpinner(float t)
    {
        if (spinner == null) return;
        spinner.gameObject.SetActive(true);
        // Eight lit dots and a four-dot gap step round; the tremor makes it stall
        // twice before the load settles into a steady turn.
        bool stalled = (t > .9f && t < 1.25f) || (t > 2.1f && t < 2.3f);
        int head = stalled ? Mathf.FloorToInt(.9f / .09f) : Mathf.FloorToInt(t / .09f);
        for (int i = 0; i < spinnerDots.Length; i++)
            spinnerDots[i].enabled = ((i - head) % 12 + 12) % 12 < 8;
    }

    void KeepOverlayUnderLive()
    {
        var live = Live;
        if (overlay == null || live == null) return;
        var parent = live.WindowRect.parent;
        if (overlay.parent != parent) overlay.SetParent(parent, false);
        int liveIndex = live.WindowRect.GetSiblingIndex();
        int index = overlay.GetSiblingIndex();
        if (index != liveIndex - 1) overlay.SetSiblingIndex(index < liveIndex ? liveIndex - 1 : liveIndex);
    }

    // ---------------------------------------------------------------- return dissolve
    void ApplyDissolve(int step)
    {
        if (overlayImage == null) return;
        if (ditherSprites == null)
        {
            // Black with holes: 75%, 50%, 25% coverage on a 4x4 ordered grid.
            ditherSprites = new[] {
                PixelSprite(new[] { "IIII", "I.II", "IIII", "II.I" }, "Dissolve75"),
                PixelSprite(new[] { "I.I.", ".I.I", "I.I.", ".I.I" }, "Dissolve50"),
                PixelSprite(new[] { "I...", "....", "..I.", "...." }, "Dissolve25") };
        }
        overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        if (step <= 0) { overlayImage.sprite = null; overlayImage.type = Image.Type.Simple; overlayImage.color = Color.black; return; }
        if (step > 3) { overlay.gameObject.SetActive(false); return; }
        overlayImage.sprite = ditherSprites[step - 1];
        overlayImage.type = Image.Type.Tiled;
        overlayImage.color = Color.black;
        var canvas = overlay.GetComponentInParent<Canvas>();
        overlayImage.pixelsPerUnitMultiplier = canvas != null ? canvas.rootCanvas.referencePixelsPerUnit : 20f;
    }

    // ---------------------------------------------------------------- helpers
    void DestroyVisuals()
    {
        if (alert != null) Destroy(alert.gameObject);
        if (overlay != null) Destroy(overlay.gameObject);
        alert = overlay = spinner = null; overlayImage = alertIcon = null;
        spinnerDots = bandStripes = null; bandTitle = bandBody = null;
        if (Interior?.ZoomRoot != null) Interior.ZoomRoot.anchoredPosition = Vector2.zero;
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    static Image MakeImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rt = MakeRect(name, parent, sprite != null ? sprite.rect.size : Vector2.one);
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.color = color; image.raycastTarget = false;
        return image;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    TMP_Text MakeLabel(string name, Transform parent, string key, string fallback, TextAlignmentOptions alignment)
    {
        var rt = MakeRect(name, parent, Vector2.one);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false; label.richText = false;
        label.enableAutoSizing = false; label.fontSize = 7f;
        label.alignment = alignment; label.color = Ink; label.margin = Vector4.zero;
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
        var source = OS_Handle.Instance?.LinkMessageController?.DisplayedTextLabel;
        if (source != null && source.font != null)
        {
            label.font = source.font; label.fontSharedMaterial = source.fontSharedMaterial;
        }
        OSLocalizedText.SetRuntime(label, key, fallback);
        OSLocalizedFontUtility.ApplyCurrent(label);
        return label;
    }

    static Sprite PixelSprite(string[] rows, string name)
    {
        int w = rows[0].Length, h = rows.Length;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = name };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                texture.SetPixel(x, h - 1 - y, rows[y][x] == 'I' ? Color.white : Color.clear);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
    }

#if UNITY_EDITOR
    public string DebugPhase => phase.ToString();
    public RectTransform DebugAlert => alert;
    public RectTransform DebugOverlay => overlay;
#endif
}
