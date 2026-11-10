using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The end of chapter 2: night falls over the OS and stays.
///   Nightfall - from the final talk's "天好像已经黑透了" the whole OS steps down to
///               black through a 4x4 ordered dither, and the prologue rain fades in.
///   Light     - the player's pointer and her red pointer each carry a circle of
///               light with a three-step dithered edge; icons and frames are only
///               seen when one of them passes by.
///   Glow      - content that lights itself: Live's portrait, her messages, the
///               memory window, any registered window (chapter 3's Baba view).
///               Frames and title bars stay dark; a one-texel half-lit rim hints
///               at the window edge. The heartbeat glows faintly; the resource
///               rows glow only for a moment when their value changes.
///   Thunder   - clicking 状态检查 at the end: one flash and the main-menu thunder.
///               The demo puts the lights out; the full game keeps local lights
///               and rain for the chapter-3 entry after EndReached.
/// One Alpha8 texture at 640x360-ish logical pixels covers the OS. It never blocks
/// raycasts, so the desktop stays usable in the dark. Presentation only: never
/// writes story, save or resources.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)] // Paint after windows apply their final topmost/drag order.
public sealed class OSChapter02NightfallPresentation : MonoBehaviour
{
    const int OverlayOrder = 1200, PointerOrder = 1202, LogicalHeight = 360;
    const float StepSeconds = .07f, PointerCore = 28f, PointerBand = 4f;
    const float HeartGlow = .5f, ResourceGlowSeconds = 1.6f;
    const float RainVolume = .4f, RainFadeSeconds = 3f;
    const string LivePointerName = "CharacterPermanentRedPointer";
    const string ThunderSfxKey = "Thunder_ForMainCore", RainAmbientKey = "Rain";
    static readonly byte[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    // One short flash, with the clap slightly behind it. The state-check window
    // opens after the dither has settled; its own light survives the flash.
    const float FlashEnd = .12f, FlashSettleEnd = .38f;
    const float ThunderSoundAt = .08f, EndAt = .65f;
    const float DemoFlashEnd = .12f, DemoLightsOutSeconds = .2f, DemoEndAt = .65f;

    sealed class Lift { public RectTransform target; public Canvas canvas; public bool added, overrideSorting, enabled; public int order, layer; }
    sealed class Glow { public RectTransform target; public OSWindowUnit window; public float light; }
    struct LightSurface
    {
        public RectTransform space;
        public Transform order;
        public Rect local;
        public int level, layer, sortingOrder, sequence;
        public bool occludes, rim;
    }
    struct TransientLight
    {
        public object owner;
        public Vector2 screen;
        public float core, band;
        public int level;
    }

    OS_Handle os;
    OSMemoryProcessSaveData observed;
    RectTransform overlay;
    RawImage image;
    Texture2D texture;
    byte[] alpha, light;
    int width, height;
    float dark, darkClock, thunderClock = -1f, lightScale = 1f;
    bool hasFocus = true, thunderPlayed, endRaised, wasRestoring, demoThunder;
    AudioSource rain;
    AudioLowPassFilter rainFilter;
    // Chapter 3: silent until the home map opens, then fades in. It plays its own brighter,
    // louder take of the rain (the shared clip sits about -31 dBFS RMS, the home one about -19);
    // indoors only a light low-pass makes it sound like a room, the yard is open.
    const string Chapter03RainResource = "Chapter03Story/Audio/RainHome";
    const float Chapter03IndoorRain = .33f, Chapter03OutdoorRain = .6f, Chapter03RainFadePerSecond = .4f;
    const float Chapter03IndoorCutoff = 2200f;
    float chapter03RainGain = 1f;
    bool chapter03Rain, chapter03Outdoors, chapter03HomeOpened, homeRainLoaded;
    AudioClip sharedRainClip, homeRainClip;
    public void SetChapter03Rain(bool outdoors, bool active, bool homeOpened = true)
    {
        // Inactive is sent every frame; preserve the fade after swapping back to shared rain.
        if (!active && !rainStarted) chapter03RainGain = 1f;
        chapter03Rain = active; chapter03Outdoors = outdoors; chapter03HomeOpened = homeOpened;
        if (rain == null) return;
        if (rainFilter == null) rainFilter = rain.gameObject.GetComponent<AudioLowPassFilter>();
        if (rainFilter == null) rainFilter = rain.gameObject.AddComponent<AudioLowPassFilter>();
        rainFilter.enabled = active && !outdoors;
        rainFilter.cutoffFrequency = Chapter03IndoorCutoff;
    }
    AudioClip WantedRainClip
    {
        get
        {
            if (chapter03Rain && !homeRainLoaded)
            { homeRainLoaded = true; homeRainClip = Resources.Load<AudioClip>(Chapter03RainResource); }
            return chapter03Rain && homeRainClip != null ? homeRainClip : sharedRainClip;
        }
    }
    // The two clips differ by about 9 dB, so they only change places while the rain is silent.
    float Chapter03RainTick()
    {
        var wanted = WantedRainClip;
        bool swapping = wanted != null && rain.clip != wanted;
        if (swapping && (!rainStarted || rainGain * chapter03RainGain < .02f))
        {
            rain.clip = wanted; swapping = false;
            if (rainStarted) { rain.Play(); if (rainPaused) rain.Pause(); }
        }
        return swapping ? 0f : !chapter03Rain ? 1f : !chapter03HomeOpened ? 0f
            : chapter03Outdoors ? Chapter03OutdoorRain : Chapter03IndoorRain;
    }
    float rainGain;
    bool rainStarted, rainPaused;
    readonly List<Lift> lifts = new List<Lift>(1);
    readonly List<Glow> extraGlows = new List<Glow>(2);
    readonly List<OSWindowUnit> visibleWindows = new List<OSWindowUnit>(32);
    readonly List<LightSurface> lightSurfaces = new List<LightSurface>(48);
    readonly List<TransientLight> transientLights = new List<TransientLight>(4);
    readonly Dictionary<OSWindowUnit, float> titleHeights = new Dictionary<OSWindowUnit, float>();
    readonly Dictionary<OSResourceStateView, int> resourceValues = new Dictionary<OSResourceStateView, int>();
    readonly Dictionary<OSResourceStateView, float> resourceGlow = new Dictionary<OSResourceStateView, float>();
    OSResourceStateView[] resourceViews;
    readonly Vector3[] corners = new Vector3[4];

    public bool IsNight => isActiveAndEnabled && WantsNight && DaylightProgress < 1f;
    public bool IsThundering => thunderClock >= 0f;
    public bool HasEnded => endRaised;
    /// <summary>Fired once after the flash settles, ready for the demo card or chapter entry.</summary>
    public event Action EndReached;

    // The chapter handoff owns this saved clock. Keep the settled night state
    // intact underneath it so a restore/checkpoint can reconstruct either view.
    float DaylightProgress => os?.Chapter04Entry?.DaylightProgress ?? 0f;

    bool WantsNight
    {
        get
        {
            var data = GameDataManager.Instance;
            if (!GameSetup.IsDemo && data != null)
            {
                int entry = data.OSProcessData.memory.chapter03EntryStep;
                if (data.CurrentChapterIndex == 2 && entry >= (int)OSChapter03EntryDirector.EntryStep.Thunder
                    || data.CurrentChapterIndex == 3 && entry >= (int)OSChapter03EntryDirector.EntryStep.Important)
                    return true;
            }
            var talk = os != null ? os.Chapter02FinalTalk : null;
            if (talk == null || data?.CurrentChapterIndex != 2) return false;
            var step = talk.Step;
            return step >= OSChapter02FinalTalkDirector.FinalTalkStep.Night
                && step <= OSChapter02FinalTalkDirector.FinalTalkStep.Chapter03Ready;
        }
    }

    bool Hidden
    {
        get
        {
            if (os == null || !os.IsOSVisible || GameTestConsole.BlocksGameplayInput) return true;
            var settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
            return settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy;
        }
    }

    bool Frozen => !hasFocus || Hidden || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true;

    bool HasSavedEntryNight
    {
        get
        {
            var data = GameDataManager.Instance;
            return !GameSetup.IsDemo && data != null
                && data.CurrentChapterIndex >= 2 && data.CurrentChapterIndex <= 3
                && data.OSProcessData.memory.chapter03EntryStep >= (int)OSChapter03EntryDirector.EntryStep.Important;
        }
    }

    void RestoreSavedEntryNight()
    {
        // Continue reveals the OS while GameSessionFlow still owns its curtain.
        // The entry director deliberately waits for that transaction to finish;
        // the renderer must already be dark before the curtain uncovers it.
        if (HasSavedEntryNight && (!endRaised || demoThunder || dark < 16f || lightScale < 1f))
            RestoreEntryNight();
    }

    /// <summary>Discard transient night/thunder state when the owning session changes.</summary>
    public void ResetPresentation()
    {
        ResetNight(0f);
        observed = GameDataManager.Instance?.OSProcessData.memory;
        wasRestoring = OSSessionPersistence.IsRestoring;
    }

    /// <summary>Restore an already-finished ending without flashing or replaying the thunder.</summary>
    public void RestoreEnded()
    {
        if (!GameSetup.IsDemo) { RestoreEntryNight(); return; }
        if (os == null) os = GetComponent<OS_Handle>();
        ResetPresentation();
        dark = 16f; darkClock = 0f;
        demoThunder = GameSetup.IsDemo;
        thunderClock = demoThunder ? DemoEndAt : EndAt; thunderPlayed = endRaised = true;
        lightScale = 0f;
        // Paint immediately: the end card can appear this frame without first
        // exposing a bright desktop underneath it.
        if (os != null && EnsureOverlay())
        {
            Paint(dark, 0f);
            image.enabled = os.IsOSVisible;
        }
    }

    /// <summary>
    /// Restore the production entry's settled night without another flash or
    /// thunder. Repeated calls preserve the live rain loop and registered glows.
    /// </summary>
    public void RestoreEntryNight()
    {
        if (GameSetup.IsDemo) return;
        if (os == null) os = GetComponent<OS_Handle>();
        var state = GameDataManager.Instance?.OSProcessData.memory;
        if (!ReferenceEquals(observed, state)) ResetNight(0f);
        observed = state;
        wasRestoring = OSSessionPersistence.IsRestoring;
        dark = 16f; darkClock = 0f;
        demoThunder = false;
        thunderClock = EndAt; thunderPlayed = endRaised = true;
        lightScale = 1f;
        TickRain(0f);
        if (os == null || !EnsureOverlay()) return;
        Paint(dark, 0f);
        image.enabled = !Hidden && DaylightProgress < 1f;
        if (!image.enabled) LowerPointer(); else RaisePointer();
    }

    /// <summary>A window whose content should light itself in the dark (chapter 3's Baba view).</summary>
    public void AddGlowWindow(OSWindowUnit window, float strength = 1f)
    {
        if (window == null) return;
        SetGlow(window.WindowRect, window, strength);
    }

    public void AddGlowRect(RectTransform target, float strength = 1f)
    {
        if (target == null) return;
        SetGlow(target, null, strength);
    }

    void SetGlow(RectTransform target, OSWindowUnit window, float strength)
    {
        if (target == null) return;
        for (int i = extraGlows.Count - 1; i >= 0; i--)
        {
            var glow = extraGlows[i];
            if (glow.target == null) { extraGlows.RemoveAt(i); continue; }
            if (glow.target != target) continue;
            glow.window = window;
            glow.light = Mathf.Clamp01(strength);
            return;
        }
        extraGlows.Add(new Glow { target = target, window = window, light = Mathf.Clamp01(strength) });
    }

    public void RemoveGlow(RectTransform target) => extraGlows.RemoveAll(glow => glow.target == null || glow.target == target);

    /// <summary>Illuminate the visible foreground at a screen position. Radii use logical OS pixels.</summary>
    public void SetTransientLight(object owner, Vector2 screen, float core, float band, int level = 4)
    {
        if (ReferenceEquals(owner, null)) return;
        if (level <= 0 || float.IsNaN(screen.x) || float.IsNaN(screen.y)
            || float.IsInfinity(screen.x) || float.IsInfinity(screen.y)
            || float.IsNaN(core) || float.IsNaN(band) || float.IsInfinity(core) || float.IsInfinity(band))
        { ClearTransientLight(owner); return; }
        var value = new TransientLight
        {
            owner = owner, screen = screen, core = Mathf.Max(0f, core), band = Mathf.Max(0f, band),
            level = Mathf.Clamp(level, 1, 4)
        };
        for (int i = 0; i < transientLights.Count; i++)
            if (ReferenceEquals(transientLights[i].owner, owner)) { transientLights[i] = value; return; }
        transientLights.Add(value);
    }

    public void ClearTransientLight(object owner)
    {
        for (int i = transientLights.Count - 1; i >= 0; i--)
            if (ReferenceEquals(transientLights[i].owner, owner)) transientLights.RemoveAt(i);
    }

    public int TransientLightCount => transientLights.Count;

    /// <summary>Read a registered light without exposing mutable presentation state.</summary>
    public bool TryGetTransientLight(object owner, out Vector2 screen, out float core, out float band, out int level)
    {
        foreach (var value in transientLights)
            if (ReferenceEquals(value.owner, owner))
            {
                screen = value.screen; core = value.core; band = value.band; level = value.level;
                return true;
            }
        screen = default; core = band = 0f; level = 0;
        return false;
    }

    /// <summary>Start the thunder ending. Ignored unless night has fully fallen.</summary>
    public bool BeginThunder()
    {
        if (!IsNight || IsThundering || endRaised) return false;
        thunderClock = 0f; thunderPlayed = false;
        demoThunder = GameSetup.IsDemo;
        return true;
    }

    void Awake() => os = GetComponent<OS_Handle>();
    void OnEnable()
    {
        hasFocus = Application.isFocused;
        RestoreSavedEntryNight();
    }
    void OnApplicationFocus(bool focused) => hasFocus = focused;
    void OnDisable() => ResetNight(0f);
    void OnDestroy()
    {
        ResetNight(0f);
        if (texture != null) Destroy(texture);
        if (rain != null) Destroy(rain.gameObject);
    }

    void LateUpdate()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        var state = GameDataManager.Instance?.OSProcessData.memory;
        bool restoring = OSSessionPersistence.IsRestoring;
        if (!ReferenceEquals(observed, state) || restoring && !wasRestoring)
        {
            ResetNight(0f);
            observed = state;
        }
        wasRestoring = restoring;
        // A restore may start after the director has already restored its night,
        // or finish before the director's next Available tick. Reconstruct the
        // settled production state here immediately after any transient reset.
        RestoreSavedEntryNight();
        if (restoring) return;
        bool night = WantsNight;
        if (!night && dark <= 0f && overlay == null) return;
        if (!night)
        {
            ResetNight(.5f);
            return;
        }
        float dt = Frozen ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        TickRain(dt);
        if (Hidden || DaylightProgress >= 1f)
        {
            if (image != null) image.enabled = false;
            LowerPointer();
            return;
        }
        if (!EnsureOverlay()) return;
        image.enabled = true;

        // Night falls one dither level per step and never climbs back by itself.
        darkClock += dt;
        while (darkClock >= StepSeconds && dark < 16f) { darkClock -= StepSeconds; dark++; }
        if (dark >= 16f) darkClock = 0f;
        float shown = TickThunder(dt);

        // Once her light is gone, the red pointer belongs under the black as
        // well; an overriding Canvas would otherwise leave a bright red dot.
        if (lightScale > 0f) RaisePointer(); else LowerPointer();

        Paint(shown, dt);
    }

    // ---------------------------------------------------------------- thunder

    float TickThunder(float dt)
    {
        if (thunderClock < 0f) return dark;
        thunderClock += dt;
        float t = thunderClock;
        if (!thunderPlayed && t >= (demoThunder ? 0f : ThunderSoundAt))
        {
            thunderPlayed = true;
            AudioManager.Instance?.PlaySfx(ThunderSfxKey);
        }
        // Only the demo extinguishes the OS. In the full game its windows and
        // both pointers must remain usable while Live leads the upgrade.
        lightScale = !demoThunder || t < DemoFlashEnd ? 1f
            : 1f - Mathf.Min(4, Mathf.FloorToInt((t - DemoFlashEnd) / (DemoLightsOutSeconds / 4f)) + 1) * .25f;
        if (t >= (demoThunder ? DemoEndAt : EndAt) && !endRaised)
        {
            endRaised = true;
            EndReached?.Invoke();
        }
        // A single bright beat, a quick dither fade, then a short fully black
        // hold before the demo card.
        if (demoThunder)
            return t < DemoFlashEnd ? 0f : Mathf.Min(16f, Mathf.Floor((t - DemoFlashEnd) / DemoLightsOutSeconds * 16f));
        // A single flash, then back to the same local pools of light.
        if (t < FlashEnd) return Mathf.Min(dark, 3f);
        if (t < FlashSettleEnd) return Mathf.Min(dark, 3f + Mathf.Floor((t - FlashEnd) / (FlashSettleEnd - FlashEnd) * 13f));
        return dark;
    }

    // ---------------------------------------------------------------- painting

    void Paint(float shown, float dt)
    {
        Array.Clear(light, 0, light.Length);
        if (lightScale > 0f)
        {
            lightSurfaces.Clear();
            // A window owns its emission; it is not a spotlight through whatever
            // happens to be on top. Include nested/dynamic windows (e.g. ErrorPool),
            // which are intentionally absent from OS_Handle.WindowUnits.
            os.GetComponentsInChildren(false, visibleWindows);
            foreach (var window in visibleWindows)
                if (WindowVisible(window) && window.WindowRect != null)
                    AddLightSurface(window.WindowRect, window.WindowRect.rect, 0, false, true, window.transform);
            GlowWindow(os.CharacterWindowController != null ? os.CharacterWindowController.GetComponent<OSWindowUnit>() : null, 4);
            GlowWindow(os.LinkMessageController != null ? os.LinkMessageController.LinkWindow : null, 4);
            GlowWindow(MemoryWindow(), 4);
            foreach (var glow in extraGlows)
            {
                if (glow.target == null || !glow.target.gameObject.activeInHierarchy) continue;
                int level = Mathf.RoundToInt(glow.light * 4f);
                if (glow.window != null) GlowWindow(glow.window, level);
                else AddLightSurface(glow.target, glow.target.rect, level, false);
            }
            var heart = os.HeartbeatWaveform;
            if (heart != null && heart.isActiveAndEnabled)
            {
                var rect = (RectTransform)heart.transform;
                AddLightSurface(rect, rect.rect, Mathf.RoundToInt(HeartGlow * 4f), false);
            }
            GlowResources(dt);
            lightSurfaces.Sort(CompareLightSurfaces);
            foreach (var surface in lightSurfaces)
            {
                if (surface.occludes) OccludeGlow(surface.space, surface.local);
                else GlowRect(surface.space, surface.local, surface.level, surface.rim);
            }

            // Pointer illumination falls on the visible foreground, independent
            // of which window owns it, so window occlusion must not erase it.
            if (CursorHandle.Instance != null) LightCircle(CursorHandle.Instance.DisplayedScreenPosition, PointerCore, PointerBand, 4);
            var actor = os.CursorRelease;
            var pointer = PointerRect();
            if (actor != null && pointer != null && pointer.gameObject.activeInHierarchy)
                LightCircle(actor.CompanionScreenPosition, PointerCore, PointerBand, 4);
            foreach (var transient in transientLights)
                LightCircle(transient.screen, transient.core, transient.band, transient.level);
        }

        // Daylight removes the whole OS veil, including unlit frames and desktop.
        // Reading saved stage/elapsed here also covers RestoreEntryNight's
        // immediate paint, before the session curtain uncovers the interface.
        int dark16 = Mathf.RoundToInt(Mathf.Clamp(shown, 0f, 16f) * (1f - DaylightProgress));
        for (int y = 0; y < height; y++)
        {
            int row = y * width, bayerRow = (y & 3) * 4;
            for (int x = 0; x < width; x++)
            {
                int level = light[row + x];
                float lit = lightScale * level * .25f;
                int shade = level == 0 ? dark16 : Mathf.RoundToInt(dark16 * (1f - lit));
                alpha[row + x] = Bayer[bayerRow + (x & 3)] < shade ? (byte)255 : (byte)0;
            }
        }
        texture.SetPixelData(alpha, 0);
        texture.Apply(false, false);
    }

    void LightCircle(Vector2 screen, float core, float band, int level)
    {
        if (level <= 0 || !ToTexel(screen, out Vector2 centre)) return;
        float unit = height / (float)LogicalHeight;
        core *= unit; band *= unit;
        float outer = core + band * 3f;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(centre.x - outer)), x1 = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + outer));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(centre.y - outer)), y1 = Mathf.Min(height - 1, Mathf.CeilToInt(centre.y + outer));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), centre);
                int value = d <= core ? level : d <= core + band ? level - 1
                    : d <= core + band * 2f ? level - 2 : d <= outer ? level - 3 : 0;
                int i = y * width + x;
                if (value > light[i]) light[i] = (byte)value;
            }
    }

    void GlowWindow(OSWindowUnit window, int level)
    {
        if (!WindowVisible(window)) return;
        var rect = window.WindowRect;
        if (rect == null) return;
        Rect local = rect.rect;
        float title = TitleHeight(window);
        // Frames and the title bar stay dark: only the body lights up.
        var body = Rect.MinMaxRect(local.xMin + 1f, local.yMin + 1f, local.xMax - 1f, local.yMax - title - 1f);
        if (body.width > 0f && body.height > 0f)
            AddLightSurface(rect, body, level, true, false, window.transform);
    }

    static bool WindowVisible(OSWindowUnit window) => window != null && window.IsOpen
        && !window.IsMinimized && window.gameObject.activeInHierarchy;

    void AddLightSurface(RectTransform space, Rect local, int level, bool rim,
        bool occludes = false, Transform order = null)
    {
        if (space == null || !space.gameObject.activeInHierarchy || (!occludes && level <= 0)) return;
        Canvas sortingCanvas = null;
        bool ignoreGroups = false;
        for (Transform node = space; node != null; node = node.parent)
        {
            if (!ignoreGroups && node.TryGetComponent(out CanvasGroup group))
            {
                if (group.alpha <= 0f) return;
                ignoreGroups = group.ignoreParentGroups;
            }
            if (sortingCanvas == null && node.TryGetComponent(out Canvas canvas)
                && canvas.enabled && (canvas.isRootCanvas || canvas.overrideSorting))
                sortingCanvas = canvas;
        }
        lightSurfaces.Add(new LightSurface
        {
            space = space, order = order != null ? order : space, local = local,
            level = level, rim = rim, occludes = occludes, sequence = lightSurfaces.Count,
            layer = sortingCanvas != null ? SortingLayer.GetLayerValueFromID(sortingCanvas.sortingLayerID) : 0,
            sortingOrder = sortingCanvas != null ? sortingCanvas.sortingOrder : 0
        });
    }

    static int CompareLightSurfaces(LightSurface a, LightSurface b)
    {
        int comparison = a.layer.CompareTo(b.layer);
        if (comparison == 0) comparison = a.sortingOrder.CompareTo(b.sortingOrder);
        if (comparison == 0) comparison = CompareHierarchy(a.order, b.order);
        // Clear the window's full surface before adding its own body emission.
        if (comparison == 0) comparison = b.occludes.CompareTo(a.occludes);
        return comparison != 0 ? comparison : a.sequence.CompareTo(b.sequence);
    }

    static int CompareHierarchy(Transform a, Transform b)
    {
        if (a == b) return 0;
        int aDepth = 0, bDepth = 0;
        for (var node = a; node != null; node = node.parent) aDepth++;
        for (var node = b; node != null; node = node.parent) bDepth++;
        var left = a; var right = b;
        for (int i = aDepth; i > bDepth; i--) left = left.parent;
        for (int i = bDepth; i > aDepth; i--) right = right.parent;
        if (left == right) return aDepth.CompareTo(bDepth);
        while (left.parent != right.parent) { left = left.parent; right = right.parent; }
        return left.GetSiblingIndex().CompareTo(right.GetSiblingIndex());
    }

    void OccludeGlow(RectTransform space, Rect local)
    {
        if (!ToTexelRect(space, local, out RectInt area)) return;
        for (int y = area.yMin; y < area.yMax; y++)
            Array.Clear(light, y * width + area.xMin, area.width);
    }

    void GlowRect(RectTransform space, Rect local, int level, bool rim)
    {
        if (level <= 0 || !ToTexelRect(space, local, out RectInt area)) return;
        for (int y = area.yMin - (rim ? 1 : 0); y < area.yMax + (rim ? 1 : 0); y++)
        {
            if (y < 0 || y >= height) continue;
            bool rimRow = y < area.yMin || y >= area.yMax;
            for (int x = area.xMin - (rim ? 1 : 0); x < area.xMax + (rim ? 1 : 0); x++)
            {
                if (x < 0 || x >= width) continue;
                bool edge = rimRow || x < area.xMin || x >= area.xMax;
                int value = edge ? Mathf.Min(level, 2) : level;
                int i = y * width + x;
                if (value > light[i]) light[i] = (byte)value;
            }
        }
    }

    void GlowResources(float dt)
    {
        if (resourceViews == null) resourceViews = os.GetComponentsInChildren<OSResourceStateView>(true);
        foreach (var view in resourceViews)
        {
            if (view == null || !view.isActiveAndEnabled) continue;
            int value = view.DisplayedValue;
            bool changed = resourceValues.TryGetValue(view, out int last) && last != value;
            resourceValues[view] = value;
            resourceGlow.TryGetValue(view, out float glow);
            if (changed || view.IsAnimating) glow = ResourceGlowSeconds;
            else glow = Mathf.Max(0f, glow - dt);
            resourceGlow[view] = glow;
            if (glow <= 0f) continue;
            // The last half second steps down 4 -> 1.
            int level = glow > .5f ? 4 : Mathf.Max(1, Mathf.CeilToInt(glow / .125f));
            var rect = (RectTransform)view.transform;
            AddLightSurface(rect, rect.rect, level, false);
        }
    }

    float TitleHeight(OSWindowUnit window)
    {
        if (titleHeights.TryGetValue(window, out float cached)) return cached;
        var rect = window.WindowRect;
        float title = 12f;
        foreach (var candidate in window.GetComponentsInChildren<RectTransform>(true))
        {
            if (candidate == rect || !window.IsDragAreaTarget(candidate.gameObject)) continue;
            candidate.GetWorldCorners(corners);
            float minY = float.PositiveInfinity;
            foreach (var corner in corners) minY = Mathf.Min(minY, rect.InverseTransformPoint(corner).y);
            float height = rect.rect.yMax - minY;
            if (height > 0f && height < rect.rect.height * .5f) { title = height; break; }
        }
        titleHeights[window] = title;
        return title;
    }

    bool ToTexel(Vector2 screen, out Vector2 texel)
    {
        texel = default;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screen, OverlayCamera(), out Vector2 local)) return false;
        Rect r = overlay.rect;
        texel = new Vector2((local.x - r.xMin) / r.width * width, (local.y - r.yMin) / r.height * height);
        return true;
    }

    bool ToTexelRect(RectTransform space, Rect local, out RectInt area)
    {
        area = default;
        Rect r = overlay.rect;
        if (r.width <= 0f || r.height <= 0f) return false;
        Vector2 a = overlay.InverseTransformPoint(space.TransformPoint(local.min));
        Vector2 b = overlay.InverseTransformPoint(space.TransformPoint(local.max));
        Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
        int x0 = Mathf.RoundToInt((min.x - r.xMin) / r.width * width), x1 = Mathf.RoundToInt((max.x - r.xMin) / r.width * width);
        int y0 = Mathf.RoundToInt((min.y - r.yMin) / r.height * height), y1 = Mathf.RoundToInt((max.y - r.yMin) / r.height * height);
        x0 = Mathf.Clamp(x0, 0, width); x1 = Mathf.Clamp(x1, 0, width);
        y0 = Mathf.Clamp(y0, 0, height); y1 = Mathf.Clamp(y1, 0, height);
        if (x1 <= x0 || y1 <= y0) return false;
        area = new RectInt(x0, y0, x1 - x0, y1 - y0);
        return true;
    }

    Camera OverlayCamera()
    {
        var canvas = overlay.GetComponentInParent<Canvas>();
        canvas = canvas != null ? canvas.rootCanvas : null;
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    }

    // ---------------------------------------------------------------- setup

    bool EnsureOverlay()
    {
        if (overlay == null)
        {
            var node = new GameObject("Chapter02Nightfall", typeof(RectTransform));
            node.layer = os.gameObject.layer;
            overlay = (RectTransform)node.transform;
            overlay.SetParent(os.transform, false);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            var canvas = node.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = OverlayOrder;
            image = node.AddComponent<RawImage>();
            image.color = Color.black;
            image.raycastTarget = false;
        }
        overlay.SetAsLastSibling();
        Rect r = overlay.rect;
        if (r.width <= 0f || r.height <= 0f) return false;
        int h = LogicalHeight, w = Mathf.Max(1, Mathf.RoundToInt(LogicalHeight * r.width / r.height));
        if (texture == null || width != w || height != h)
        {
            if (texture != null) Destroy(texture);
            width = w; height = h;
            texture = new Texture2D(width, height, TextureFormat.Alpha8, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Chapter02Nightfall" };
            alpha = new byte[width * height];
            light = new byte[width * height];
        }
        // ResetNight can rebuild the RawImage while retaining the same-sized
        // texture. Rebind it on every new overlay, otherwise its white fallback
        // texture makes the entire night mask opaque after a session restore.
        image.texture = texture;
        return true;
    }

    OSWindowUnit MemoryWindow()
    {
        var memory = os.MemoryWindowController;
        if (memory == null) return null;
        var window = memory.GetComponent<OSWindowUnit>();
        return window != null ? window : memory.GetComponentInParent<OSWindowUnit>(true);
    }

    RectTransform PointerRect()
    {
        var live = os.CharacterWindowController != null ? os.CharacterWindowController.transform : null;
        return live != null && live.parent != null ? live.parent.Find(LivePointerName) as RectTransform : null;
    }

    // Her red pointer is a user like the player's: it stays above the dark.
    void RaisePointer()
    {
        var target = PointerRect();
        if (target == null || !target.gameObject.activeInHierarchy) return;
        var parentCanvas = target.parent != null ? target.parent.GetComponentInParent<Canvas>() : null;
        Lift lift = null;
        foreach (var existing in lifts) if (existing.target == target) { lift = existing; break; }
        if (lift == null)
        {
            lift = new Lift { target = target, canvas = target.GetComponent<Canvas>() };
            lift.added = lift.canvas == null;
            if (lift.added) lift.canvas = target.gameObject.AddComponent<Canvas>();
            else
            {
                lift.overrideSorting = lift.canvas.overrideSorting; lift.order = lift.canvas.sortingOrder;
                lift.layer = lift.canvas.sortingLayerID; lift.enabled = lift.canvas.enabled;
            }
            lifts.Add(lift);
        }
        // Settings hides this shared Canvas; another lift can restore its parent
        // order while hidden. Reapply the night layer without recapturing its
        // original state, so closing settings cannot leave her behind a modal.
        lift.canvas.enabled = true;
        lift.canvas.overrideSorting = true;
        if (parentCanvas != null) lift.canvas.sortingLayerID = parentCanvas.sortingLayerID;
        lift.canvas.sortingOrder = PointerOrder;
    }

    void LowerPointer()
    {
        foreach (var lift in lifts)
        {
            if (lift.canvas == null) continue;
            if (lift.added)
            {
                // Live's message lift can reuse this pointer Canvas and add a
                // GraphicRaycaster later in the same frame. Keep it inheriting
                // its parent instead of scheduling destruction beneath that user.
                var parent = lift.target != null && lift.target.parent != null
                    ? lift.target.parent.GetComponentInParent<Canvas>() : null;
                lift.canvas.overrideSorting = true;
                lift.canvas.sortingLayerID = parent != null ? parent.sortingLayerID : 0;
                lift.canvas.sortingOrder = parent != null ? parent.sortingOrder : 0;
                lift.canvas.overrideSorting = false;
                lift.canvas.enabled = true;
            }
            else
            {
                lift.canvas.overrideSorting = true;
                lift.canvas.sortingLayerID = lift.layer; lift.canvas.sortingOrder = lift.order;
                lift.canvas.overrideSorting = lift.overrideSorting; lift.canvas.enabled = lift.enabled;
            }
        }
        lifts.Clear();
    }

    void TickRain(float dt)
    {
        if (rain == null)
        {
            var clip = GameDataManager.Instance?.AudioData?.AMBIENT?.Find(item => item != null && item.keyid == RainAmbientKey)?.audioclip;
            if (clip == null) return;
            var rainObject = new GameObject("NightfallRain");
            rainObject.transform.SetParent(transform, false);
            rain = rainObject.AddComponent<AudioSource>();
            rain.clip = sharedRainClip = clip; rain.loop = true; rain.playOnAwake = false;
            rain.spatialBlend = 0f; rain.volume = 0f;
            // The heart-talk lease silences the shared ambient channel, so the rain
            // keeps its own voice but still follows the player's ambient volume.
            rain.outputAudioMixerGroup = AudioManager.Instance != null ? AudioManager.Instance.AmbientMixerGroup : null;
            rainGain = 0f; rainStarted = rainPaused = false;
        }
        if (dt > 0f) rainGain = Mathf.MoveTowards(rainGain, 1f, dt / RainFadeSeconds);
        float chapter03Target = Chapter03RainTick();
        if (dt > 0f) chapter03RainGain = Mathf.MoveTowards(chapter03RainGain, chapter03Target, dt * Chapter03RainFadePerSecond);
        rain.volume = RainVolume * rainGain * chapter03RainGain;
        if (Frozen)
        {
            if (rainStarted && !rainPaused) { rain.Pause(); rainPaused = true; }
            return;
        }
        // isPlaying is false while paused; using it to decide Play would rewind
        // the loop every time focus/settings/console temporarily suspends us.
        if (!rainStarted) { rain.Play(); rainStarted = true; rainPaused = false; }
        else if (rainPaused) { rain.UnPause(); rainPaused = false; }
    }

    void ResetNight(float fade)
    {
        LowerPointer();
        if (image != null) image.enabled = false;
        dark = darkClock = 0f;
        thunderClock = -1f; thunderPlayed = endRaised = demoThunder = false; lightScale = 1f;
        resourceValues.Clear(); resourceGlow.Clear(); titleHeights.Clear();
        resourceViews = null;
        visibleWindows.Clear(); lightSurfaces.Clear(); transientLights.Clear();
        if (rain != null)
        {
            // Let the rain thin out rather than cut.
            rainGain = Mathf.MoveTowards(rainGain, 0f, fade > 0f ? Mathf.Min(Time.unscaledDeltaTime, .05f) / fade : 1f);
            rain.volume = RainVolume * rainGain * chapter03RainGain;
            if (rainGain <= 0f) { rain.Stop(); rainStarted = rainPaused = false; }
        }
        if (overlay != null && (rain == null || rainGain <= 0f))
        {
            Destroy(overlay.gameObject);
            overlay = null; image = null;
        }
    }

#if UNITY_EDITOR
    public float DebugDark => dark;
    public float DebugDaylightProgress => DaylightProgress;
    public float DebugThunderClock => thunderClock;
#endif
}
