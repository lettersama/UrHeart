using MrTGame.Narrative;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chapter-two warehouse quake (memory nodes 5450–5800) leaking into the OS.
///   5450        one faint tremor ("脚下的地板轻轻颤了一下").
///   5460–5480   faint irregular jitter of the whole OS screen, a little dust.
///   5490–5770   light continuous jitter of the whole screen; pixel dust drifts down
///               and small 1-bit error windows drop in batches across the whole width,
///               each at a random depth: in front of everything, or behind an open window.
///   5490, 5610  a stronger jolt on arrival with a heavier fall; 5610 also a brief grey flash.
///   5780        "那阵摇晃才终于慢慢停了": nothing new falls, the jitter fades out.
///   5800        the line appears after half a second of empty silence.
/// Presentation only: reads the memory cursor, never writes story or resources.
/// Freezes with focus loss, hidden OS, settings, session flow or the test console.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSMemoryQuakePresentation : MonoBehaviour
{
    const int TremorNode = 5450, FaintStart = 5460, QuakeStart = 5490, CollapseNode = 5610, QuakeEnd = 5770, SettleNode = 5780, AliveNode = 5800;
    const float AliveDelay = .5f, FadeSeconds = 2f;
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 0xff);
    // Whole-screen shake: the same top-level OS groups the focus presentation zooms.
    static readonly string[] ScreenRoots = { "Bk", "DesktopProcessGroup", "ProcessGroup", "HeartBeatPanel", "Windows", "OS_VFX" };

    OSMemoryWindowController memory;
    OSWindowUnit window, settings;
    int shownIndex = -1, pendingBurst, pendingClumps, errorCursor;
    float clock, jolt, joltTime = -1f, flash = -1f, fade = -1f, nextSpawn, tremor = -1f, dustClock, clumpClock = 1.5f;
    bool hasFocus = true;
    readonly System.Random random = new System.Random(5490);
    RectTransform flashRect;
    Image flashImage;
    Sprite[] flashDither, errorSprites;

    sealed class Falling { public RectTransform rect; public float vy, vx, spin, angle, lo, hi, delay; }
    readonly System.Collections.Generic.List<Falling> falling = new System.Collections.Generic.List<Falling>();
    sealed class ShakeRoot { public RectTransform rect; public Vector2 basePosition, applied; }
    readonly System.Collections.Generic.List<ShakeRoot> shakeRoots = new System.Collections.Generic.List<ShakeRoot>();
    sealed class Dust { public Image image; public float vy, sway, phase, x, y; }
    readonly System.Collections.Generic.List<Dust> dust = new System.Collections.Generic.List<Dust>();
    readonly System.Collections.Generic.Stack<Image> dustPool = new System.Collections.Generic.Stack<Image>();

    void OnEnable()
    {
        hasFocus = Application.isFocused;
        OSMemoryTextUnit.PlaybackDelayProvider = DelayFor;
    }
    void OnDisable()
    {
        if (OSMemoryTextUnit.PlaybackDelayProvider == (System.Func<int, float>)DelayFor) OSMemoryTextUnit.PlaybackDelayProvider = null;
        Unbind(); ClearAll();
    }
    void OnApplicationFocus(bool focused) => hasFocus = focused;

    static float DelayFor(int dataIndex) =>
        dataIndex == AliveNode && GameDataManager.Instance?.CurrentChapterIndex == 2 ? AliveDelay : 0f;

    void Bind()
    {
        var next = OS_Handle.Instance?.MemoryWindowController;
        if (next == memory) return;
        Unbind();
        memory = next;
        if (memory != null) memory.DataShown += OnDataShown;
        window = null;
    }

    // The memory window may be inactive when we bind; resolve it lazily (inactive-safe).
    void ResolveWindow()
    {
        if (window != null || memory == null) return;
        window = memory.GetComponent<OSWindowUnit>();
        if (window == null) window = memory.GetComponentInParent<OSWindowUnit>(true);
    }

    void Unbind()
    {
        if (memory != null) memory.DataShown -= OnDataShown;
        memory = null;
    }

    void OnDataShown(OSMemoryReadData data)
    {
        if (data == null || GameDataManager.Instance?.CurrentChapterIndex != 2) return;
        int index = data.DataIndex;
        if (index == shownIndex) return;
        int previous = shownIndex;
        shownIndex = index;
        if (index == TremorNode) tremor = 0f;
        if (index == QuakeStart || index == CollapseNode)
        {
            jolt = index == CollapseNode ? 5f : 4f; joltTime = 0f;
            pendingBurst = index == CollapseNode ? 3 : 2;   // a heavier fall on each jolt
            pendingClumps = index == CollapseNode ? 3 : 2;
        }
        if (index == CollapseNode) flash = 0f;
        if (index >= SettleNode && previous >= FaintStart && previous < SettleNode) fade = 0f;
        if (index >= QuakeStart && index <= QuakeEnd && falling.Count == 0) nextSpawn = .4f;
    }

    bool Available
    {
        get
        {
            var os = OS_Handle.Instance;
            if (settings == null && os != null) settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
            return hasFocus && os != null && os.IsOSVisible && !OSSessionPersistence.IsRestoring
                && GameSessionFlow.Instance?.IsBusy != true && !GameTestConsole.BlocksGameplayInput
                && !(settings != null && settings.IsOpen && !settings.IsMinimized)
                && window != null && window.IsOpen && !window.IsMinimized && window.gameObject.activeInHierarchy;
        }
    }

    int CurrentIndex => GameDataManager.Instance?.CurrentChapterIndex == 2 && memory?.CurrentData != null
        ? memory.CurrentData.DataIndex : -1;

    void Update()
    {
        Bind(); ResolveWindow();
        int index = CurrentIndex;
        bool inRange = index >= TremorNode && index < AliveNode + 10;
        if (!inRange && falling.Count == 0 && dust.Count == 0 && flash < 0f && fade < 0f) { ClearShake(); return; }
        if (!Available) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        clock += dt;

        // Whole-screen jitter: integer native-pixel offsets on an irregular beat.
        float amplitude = 0f;
        if (index >= FaintStart && index < QuakeStart) amplitude = .6f;
        else if (index >= QuakeStart && index <= QuakeEnd) amplitude = 1f;
        if (fade >= 0f) { fade += dt; amplitude = Mathf.Max(amplitude, Mathf.Lerp(1f, 0f, fade / FadeSeconds)); if (fade > FadeSeconds) fade = -1f; }
        if (tremor >= 0f) { tremor += dt; if (tremor < .3f) amplitude = Mathf.Max(amplitude, 1f); else tremor = -1f; }
        if (joltTime >= 0f)
        {
            joltTime += dt;
            amplitude = Mathf.Max(amplitude, jolt * (1f - joltTime / .6f));
            if (joltTime > .6f) joltTime = -1f;
        }
        int beat = Mathf.FloorToInt(clock / (joltTime >= 0f ? .045f : .11f));
        // Light jitter only on some beats so it reads as tremor rather than vibration.
        bool active = joltTime >= 0f || tremor >= 0f || Hash(beat) < .55f;
        Vector2 offset = active && amplitude > .05f
            ? new Vector2(Mathf.Round((Hash(beat * 3 + 1) * 2f - 1f) * amplitude), Mathf.Round((Hash(beat * 5 + 2) * 2f - 1f) * amplitude * .5f))
            : Vector2.zero;
        ApplyScreenShake(offset);

        bool quaking = index >= QuakeStart && index <= QuakeEnd && fade < 0f;
        if (quaking)
        {
            nextSpawn -= dt;
            if (nextSpawn <= 0f || pendingBurst > 0)
            {
                // Windows come down in small batches, staggered a beat apart.
                int count = pendingBurst > 0 ? pendingBurst + 1 : 2 + random.Next(2);
                for (int i = 0; i < count; i++) SpawnError(i * (.08f + (float)random.NextDouble() * .14f));
                pendingBurst = 0;
                nextSpawn = 1.8f + (float)random.NextDouble() * 1.6f;
            }
        }
        // Pixel dust: a light drizzle while the ground moves, clumps on the jolts.
        bool dusty = (index >= FaintStart && index <= QuakeEnd) || fade >= 0f;
        if (dusty)
        {
            float rate = quaking ? 9f : 2.5f;
            dustClock += dt * rate;
            while (dustClock >= 1f) { dustClock -= 1f; SpawnDust(null); }
            if (quaking)
            {
                clumpClock -= dt;
                if (clumpClock <= 0f || pendingClumps > 0)
                {
                    SpawnClump();
                    if (pendingClumps > 0) pendingClumps--; else clumpClock = 2.5f + (float)random.NextDouble() * 2f;
                }
            }
        }
        TickDust(dt);
        TickFalling(dt);
        TickFlash(dt);
    }

    static float Hash(int n)
    {
        unchecked { uint h = (uint)n * 2654435761u; h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; return (h & 0xffff) / 65535f; }
    }

    // ---------------------------------------------------------------- screen shake
    void ApplyScreenShake(Vector2 offset)
    {
        var os = OS_Handle.Instance;
        if (os == null) return;
        if (shakeRoots.Count == 0)
            foreach (string path in ScreenRoots)
            {
                var rect = os.transform.Find(path) as RectTransform;
                if (rect != null && rect.parent == os.transform)
                    shakeRoots.Add(new ShakeRoot { rect = rect, basePosition = rect.anchoredPosition, applied = rect.anchoredPosition });
            }
        foreach (var root in shakeRoots)
        {
            if (root.rect == null) continue;
            // Another presentation moved this group: adopt its value as the new base.
            if ((root.rect.anchoredPosition - root.applied).sqrMagnitude > .0001f) root.basePosition = root.rect.anchoredPosition;
            root.applied = root.basePosition + offset;
            root.rect.anchoredPosition = root.applied;
        }
    }

    void ClearShake()
    {
        foreach (var root in shakeRoots)
            if (root.rect != null && (root.rect.anchoredPosition - root.applied).sqrMagnitude < .0001f)
                root.rect.anchoredPosition = root.basePosition;
        shakeRoots.Clear();
    }

    RectTransform FallParent => window != null ? window.WindowRect.parent as RectTransform : null;

    // ---------------------------------------------------------------- dust
    void SpawnDust(Vector2? at)
    {
        var parent = FallParent;
        if (parent == null || dust.Count > 90) return;
        Image image = dustPool.Count > 0 ? dustPool.Pop() : null;
        if (image == null)
        {
            var go = new GameObject("QuakeDust", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            image = go.GetComponent<Image>(); image.raycastTarget = false; image.color = Ink;
            var rt0 = image.rectTransform; rt0.SetParent(parent, false);
            rt0.anchorMin = rt0.anchorMax = new Vector2(.5f, 1f); rt0.pivot = new Vector2(.5f, .5f);
        }
        image.gameObject.SetActive(true);
        var rt = image.rectTransform;
        int kind = random.Next(6);
        rt.sizeDelta = kind == 0 ? new Vector2(2, 2) : kind == 1 ? new Vector2(2, 1) : Vector2.one;
        float half = parent.rect.width * .5f;
        Vector2 start = at ?? new Vector2(((float)random.NextDouble() * 2f - 1f) * half, 4f);
        dust.Add(new Dust { image = image, x = start.x, y = start.y, vy = 22f + (float)random.NextDouble() * 38f,
            sway = 3f + (float)random.NextDouble() * 5f, phase = (float)random.NextDouble() * 6.28f });
        rt.SetAsLastSibling();
    }

    void SpawnClump()
    {
        var parent = FallParent;
        if (parent == null) return;
        float half = parent.rect.width * .5f;
        Vector2 origin = new Vector2(((float)random.NextDouble() * 1.6f - .8f) * half, 2f);
        int count = 6 + random.Next(6);
        for (int i = 0; i < count; i++)
            SpawnDust(origin + new Vector2(((float)random.NextDouble() - .5f) * 14f, -(float)random.NextDouble() * 10f));
    }

    void TickDust(float dt)
    {
        var parent = FallParent;
        float height = parent != null ? parent.rect.height : 360f;
        for (int i = dust.Count - 1; i >= 0; i--)
        {
            var d = dust[i];
            if (d.image == null) { dust.RemoveAt(i); continue; }
            d.vy += 20f * dt; d.y -= d.vy * dt; d.phase += dt * 3f;
            d.image.rectTransform.anchoredPosition = new Vector2(Mathf.Round(d.x + Mathf.Sin(d.phase) * d.sway), Mathf.Round(d.y));
            if (-d.y > height + 6f) { d.image.gameObject.SetActive(false); dustPool.Push(d.image); dust.RemoveAt(i); }
        }
    }

    // ---------------------------------------------------------------- error windows
    void SpawnError(float delay)
    {
        var parent = FallParent;
        if (parent == null) return;
        if (errorSprites == null)
        {
            // A fixed pool of distinct windows: icon, title style, body and damage all vary.
            errorSprites = new Sprite[14];
            for (int i = 0; i < errorSprites.Length; i++) errorSprites[i] = ErrorWindowArt.Build(9100 + i * 37, i);
        }
        // Cycle through the pool so one batch never repeats the same window.
        var sprite = errorSprites[errorCursor++ % errorSprites.Length];
        float half = parent.rect.width * .5f, margin = sprite.rect.width * .5f + 4f;
        // Anywhere across the screen; the stacking depth decides what it hides or hides behind.
        float lo = -half + margin, hi = half - margin;
        float x = Mathf.Round(Mathf.Lerp(lo, hi, (float)random.NextDouble()));
        var go = new GameObject("QuakeErrorWindow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f); rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = sprite.rect.size;
        rt.anchoredPosition = new Vector2(x, sprite.rect.height);
        var image = go.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false;
        PlaceDepth(rt);
        go.SetActive(delay <= 0f);
        falling.Add(new Falling { rect = rt, vy = 10f + (float)random.NextDouble() * 15f, lo = lo, hi = hi, delay = delay,
            vx = ((float)random.NextDouble() - .5f) * 10f, spin = ((float)random.NextDouble() - .5f) * 40f });
    }

    /// <summary>
    /// Random stacking among the open windows: about half fall in front of everything,
    /// the rest slip behind one of the open windows (most often the memory window),
    /// so the debris reads as falling through layers rather than painted on top.
    /// </summary>
    void PlaceDepth(RectTransform rt)
    {
        double roll = random.NextDouble();
        if (roll < .5) { rt.SetAsLastSibling(); return; }
        var parent = rt.parent;
        Transform target = null;
        if (roll < .8 && window != null && window.WindowRect.parent == parent) target = window.WindowRect;
        else
        {
            // Behind some other open window (Live, tools...), or the memory window again.
            int seen = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var unit = child.GetComponent<OSWindowUnit>();
                if (unit == null || !unit.IsOpen || unit.IsMinimized || !child.gameObject.activeInHierarchy) continue;
                if (random.Next(++seen) == 0) target = child;   // reservoir pick
            }
        }
        if (target == null) { rt.SetAsLastSibling(); return; }
        rt.SetSiblingIndex(target.GetSiblingIndex());
    }

    void TickFalling(float dt)
    {
        for (int i = falling.Count - 1; i >= 0; i--)
        {
            var f = falling[i];
            if (f.rect == null) { falling.RemoveAt(i); continue; }
            if (f.delay > 0f) { f.delay -= dt; if (f.delay > 0f) continue; f.rect.gameObject.SetActive(true); }
            f.vy += 140f * dt;
            f.angle += f.spin * dt;
            Vector2 p = f.rect.anchoredPosition + new Vector2(f.vx * dt, -f.vy * dt);
            // Drift stays on screen.
            if (p.x < f.lo || p.x > f.hi) { f.vx = -f.vx; p.x = Mathf.Clamp(p.x, f.lo, f.hi); }
            f.rect.anchoredPosition = p;
            // Stepped rotation keeps the one-bit edges readable.
            f.rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Round(f.angle / 3f) * 3f);
            var parent = f.rect.parent as RectTransform;
            if (parent == null || -p.y > parent.rect.height + 60f) { Destroy(f.rect.gameObject); falling.RemoveAt(i); }
        }
    }

    // ---------------------------------------------------------------- grey flash
    void TickFlash(float dt)
    {
        if (flash < 0f) { if (flashRect != null) flashRect.gameObject.SetActive(false); return; }
        flash += dt;
        EnsureFlash();
        if (flashRect == null) { flash = -1f; return; }
        flashRect.gameObject.SetActive(true);
        flashRect.SetAsLastSibling();
        // Solid ink grey, then 50% and 25% ordered dither, then gone (~0.3s).
        int step = Mathf.FloorToInt(flash / .07f);
        if (step == 0 || step == 1) { flashImage.sprite = null; flashImage.type = Image.Type.Simple; flashImage.color = Ink; }
        else if (step <= 3)
        {
            flashImage.sprite = flashDither[step - 2]; flashImage.type = Image.Type.Tiled; flashImage.color = Ink;
            var canvas = flashRect.GetComponentInParent<Canvas>();
            flashImage.pixelsPerUnitMultiplier = canvas != null ? canvas.rootCanvas.referencePixelsPerUnit : 20f;
        }
        else { flash = -1f; flashRect.gameObject.SetActive(false); }
    }

    void EnsureFlash()
    {
        if (flashRect != null) return;
        var parent = FallParent;
        if (parent == null) return;
        var go = new GameObject("QuakeGreyFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        flashRect = (RectTransform)go.transform; flashRect.SetParent(parent, false);
        flashRect.anchorMin = Vector2.zero; flashRect.anchorMax = Vector2.one; flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
        flashImage = go.GetComponent<Image>(); flashImage.raycastTarget = false;
        flashDither = new[] { Pattern(new[] { "I.", ".I" }), Pattern(new[] { "I...", "....", "..I.", "...." }) };
    }

    static Sprite Pattern(string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, h - 1 - y, rows[y][x] == 'I' ? Color.white : Color.clear);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
    }

    void ClearAll()
    {
        ClearShake();
        foreach (var f in falling) if (f.rect != null) Destroy(f.rect.gameObject);
        falling.Clear();
        foreach (var d in dust) if (d.image != null) Destroy(d.image.gameObject);
        dust.Clear();
        while (dustPool.Count > 0) { var img = dustPool.Pop(); if (img != null) Destroy(img.gameObject); }
        if (flashRect != null) Destroy(flashRect.gameObject);
        flashRect = null; flash = fade = tremor = joltTime = -1f;
    }

#if UNITY_EDITOR
    public int DebugFallingCount => falling.Count;
    public int DebugDustCount => dust.Count;
    public bool DebugFlashActive => flash >= 0f;
    public Vector2 DebugOffset => shakeRoots.Count > 0 && shakeRoots[0].rect != null ? shakeRoots[0].applied - shakeRoots[0].basePosition : Vector2.zero;
#endif
}
