using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Small icons react beside Live; the matching status row lights and pays
/// as the icon appears. Nothing travels between the character and the status bar.</summary>
public sealed partial class OSChapter03StoryPresentation
{
    const int DrainOrder = 1201;
    const float DrainPixelSize = 2f, DrainAppearAt = .12f;
    const float DrainLightHold = 1f, DrainLightStep = .07f;
    static readonly string[] HeartBits =
    {
        "..###.###..",
        ".#########.",
        "###########",
        "###########",
        "###########",
        ".#########.",
        "..#######..",
        "...#####...",
        "....###....",
        ".....#.....",
    };
    static readonly int[] HeartSeam = { 5, 5, 6, 5, 4, 5, 6, 5, 5, 5 };
    static readonly string[] FaultBits =
    {
        "#########",
        "#.......#",
        "#...#...#",
        "#...#...#",
        "#...#...#",
        "#.......#",
        "#...#...#",
        "#.......#",
        "#########",
    };

    sealed class DrainBit
    {
        public RectTransform rect;
        public Image image;
        public Vector2 home;
        public int row, side;
        public bool seam;
    }

    sealed class ResourceDrain
    {
        public bool heart, paid, lightVisible, iconVisible;
        public RectTransform root, iconRoot, sourceAnchor, targetIcon;
        public CanvasGroup group;
        public readonly List<DrainBit> bits = new List<DrainBit>();
        public Vector2 sourceLocal, origin, position, target;
        public float clock, delay, paidAt = -1f, crack, opacity;
        public int lightLevel;
        public Action landed, interrupted;
    }

    readonly List<ResourceDrain> drains = new List<ResourceDrain>();
    public bool IsResourceDrainPlaying => drains.Count > 0;

    public void PlayResourceDrain(bool heart, RectTransform sourceAnchor, Vector2 sourceLocal,
        RectTransform targetIcon, float delay, Action landed, Action interrupted = null)
    {
        RequireInitialized();
        if (sourceAnchor == null || targetIcon == null) { interrupted?.Invoke(); return; }
        var drain = new ResourceDrain
        {
            heart = heart, sourceAnchor = sourceAnchor, sourceLocal = sourceLocal, targetIcon = targetIcon,
            delay = Mathf.Max(0f, delay), landed = landed, interrupted = interrupted,
        };
        drain.root = Root(heart ? "Chapter03HeartDrain" : "Chapter03FaultDrain", windowParent, true);
        var canvas = drain.root.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = DrainOrder; canvas.worldCamera = EventCamera;
        drain.group = drain.root.gameObject.AddComponent<CanvasGroup>();
        drain.group.blocksRaycasts = false; drain.group.interactable = false; drain.group.alpha = 0f;

        string[] rows = heart ? HeartBits : FaultBits;
        int width = rows[0].Length, height = rows.Length;
        drain.iconRoot = Rect(heart ? "HeartIcon" : "FaultIcon", drain.root, Vector2.zero,
            new Vector2(width, height) * DrainPixelSize);
        drain.iconRoot.localScale = Vector3.one * .6f;
        var statusIcon = !heart ? targetIcon.GetComponent<UnityEngine.UI.Image>() : null;
        if (statusIcon != null && statusIcon.sprite != null)
        {
            var image = Image(drain.iconRoot, "FaultSprite", statusIcon.color, Vector2.zero,
                new Vector2(width, height) * DrainPixelSize);
            image.sprite = statusIcon.sprite;
            image.preserveAspect = true;
            drain.bits.Add(new DrainBit { image = image, rect = image.rectTransform, home = Vector2.zero });
        }
        else
        {
            Color color = heart ? (Color)OSPalette.Red : Ink;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                if (rows[y][x] != '#') continue;
                int seam = heart ? HeartSeam[y] : -1;
                var bit = new DrainBit
                {
                    home = new Vector2((x - width * .5f) * DrainPixelSize, (height * .5f - 1f - y) * DrainPixelSize),
                    row = y, side = heart ? Math.Sign(x - seam) : 0, seam = heart && x == seam,
                };
                bit.image = Image(drain.iconRoot, "IconPixel", color, bit.home, Vector2.one * DrainPixelSize);
                bit.rect = bit.image.rectTransform; bit.rect.pivot = Vector2.zero;
                drain.bits.Add(bit);
            }
        }
        drain.iconRoot.gameObject.SetActive(false);
        UpdateDrainAnchors(drain);
        drains.Add(drain);
    }

    Vector2 LocalInWindows(Vector3 world) => (Vector2)windowParent.InverseTransformPoint(world) - windowParent.rect.center;
    static Vector2 DrainRound(Vector2 point) => new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));

    void UpdateDrainAnchors(ResourceDrain drain)
    {
        if (drain.sourceAnchor != null)
            drain.origin = DrainRound(LocalInWindows(drain.sourceAnchor.TransformPoint(drain.sourceLocal)));
        if (drain.targetIcon != null)
            drain.target = LocalInWindows(drain.targetIcon.TransformPoint(drain.targetIcon.rect.center));
    }

    void TickResourceDrains(float dt)
    {
        for (int i = drains.Count - 1; i >= 0; i--)
        {
            // Payment and interruption callbacks may stop or replace presentations.
            if (i >= drains.Count) continue;
            var drain = drains[i];
            if (drain.root == null || drain.sourceAnchor == null || drain.targetIcon == null)
            { drains.RemoveAt(i); ReleaseDrain(drain, true); continue; }
            UpdateDrainAnchors(drain);
            if (paused) { HideDrain(drain); continue; }
            float advance = dt;
            if (drain.delay > 0f)
            {
                float consumed = Mathf.Min(drain.delay, advance); drain.delay -= consumed; advance -= consumed;
                if (drain.delay > 0f) { HideDrain(drain); continue; }
            }
            drain.clock += advance;
            if (!TickResourceDrain(drain)) continue;
            if (drains.Remove(drain)) ReleaseDrain(drain, false);
        }
    }

    bool TickResourceDrain(ResourceDrain drain)
    {
        Action pay = null;
        if (!drain.paid && drain.clock >= DrainAppearAt)
        {
            // Commit presentation state and clear both callbacks first. The icon
            // and status light are visible before ResourceChanged observers run.
            drain.paid = true; drain.paidAt = drain.clock;
            pay = drain.landed; drain.landed = drain.interrupted = null;
        }
        UpdateDrainIcon(drain);
        UpdateDrainLight(drain);
        pay?.Invoke();
        if (drain.root == null || !drain.root.gameObject.activeSelf) return true;
        // A resource listener can also reposition the status bar in this frame.
        if (pay != null) { UpdateDrainAnchors(drain); UpdateDrainLight(drain); }
        return drain.paid && drain.clock >= drain.paidAt + DrainLightHold + DrainLightStep * 4f;
    }

    void UpdateDrainIcon(ResourceDrain drain)
    {
        float t = drain.clock, lift;
        if (drain.heart)
        {
            // A single small hop, then the original jagged seam opens. Both
            // halves remain connected shapes; no pixels escape into particles.
            float hop = Mathf.Clamp01((t - DrainAppearAt) / .16f);
            lift = t < .28f ? Mathf.Sin(hop * Mathf.PI) * 2f : 0f;
            float opening = Mathf.Clamp01((t - .28f) / .22f);
            drain.crack = 1f - (1f - opening) * (1f - opening);
            float fade = Mathf.Clamp01((t - .55f) / .45f);
            lift -= fade * 3f;
            drain.opacity = t < DrainAppearAt ? 0f : 1f - fade;
        }
        else
        {
            // The fault rises five pixels, settles by one, then softly rises
            // another two while fading. It never leaves Live's neighbourhood.
            float rise = Mathf.Clamp01((t - DrainAppearAt) / .18f);
            lift = 5f * (1f - (1f - rise) * (1f - rise));
            if (t >= .3f && t < .5f) lift -= Mathf.Sin((t - .3f) / .2f * Mathf.PI);
            float fade = Mathf.Clamp01((t - .5f) / .45f);
            lift += fade * 2f;
            drain.crack = 0f;
            drain.opacity = t < DrainAppearAt ? 0f : 1f - fade;
        }
        drain.position = DrainRound(drain.origin + Vector2.up * lift);
        drain.iconRoot.anchoredPosition = drain.position;
        drain.iconVisible = drain.paid && drain.opacity > 0f && !paused && drain.delay <= 0f
            && drain.sourceAnchor != null && drain.sourceAnchor.gameObject.activeInHierarchy;
        if (drain.iconRoot.gameObject.activeSelf != drain.iconVisible) drain.iconRoot.gameObject.SetActive(drain.iconVisible);
        drain.group.alpha = paused || drain.delay > 0f ? 0f : 1f;
        if (!drain.iconVisible) return;
        foreach (var bit in drain.bits)
        {
            Vector2 offset = Vector2.zero;
            float alpha = drain.opacity;
            if (drain.heart)
            {
                offset.x = bit.side * Mathf.Round(drain.crack * 2f);
                if (bit.side > 0) offset.y = -Mathf.Round(drain.crack * 2f);
                if (bit.seam) alpha *= 1f - drain.crack;
            }
            bit.rect.anchoredPosition = bit.home + offset;
            bit.image.enabled = alpha > 0f;
            Color color = bit.image.color; color.a = alpha; bit.image.color = color;
        }
    }

    void UpdateDrainLight(ResourceDrain drain)
    {
        if (paused || drain.delay > 0f || !drain.paid || drain.targetIcon == null)
        { HideDrainLight(drain); return; }
        int level = 4;
        float fade = drain.clock - drain.paidAt - DrainLightHold;
        if (fade >= 0f) level = Mathf.Max(0, 3 - Mathf.FloorToInt(fade / DrainLightStep));
        if (level <= 0) { HideDrainLight(drain); return; }
        // Only the status-bar icon emits transient light. Re-resolve its world
        // point so window movement and camera zoom cannot leave a light behind.
        Vector3 world = drain.targetIcon.TransformPoint(drain.targetIcon.rect.center);
        night?.SetTransientLight(drain, RectTransformUtility.WorldToScreenPoint(EventCamera, world),
            drain.heart ? 14f : 10f, drain.heart ? 3f : 2f, level);
        drain.lightVisible = true; drain.lightLevel = level;
    }

    void HideDrainLight(ResourceDrain drain)
    { night?.ClearTransientLight(drain); drain.lightVisible = false; drain.lightLevel = 0; }

    void HideDrain(ResourceDrain drain)
    {
        if (drain.group != null) drain.group.alpha = 0f;
        drain.iconVisible = false;
        if (drain.iconRoot != null) drain.iconRoot.gameObject.SetActive(false);
        HideDrainLight(drain);
    }

    void ReleaseDrain(ResourceDrain drain, bool interrupt)
    {
        HideDrain(drain);
        var callback = interrupt ? drain.interrupted : null;
        drain.landed = drain.interrupted = null;
        if (drain.root != null) DestroyRoot(drain.root);
        callback?.Invoke();
    }

    void RefreshResourceDrainVisibility()
    {
        foreach (var drain in drains)
        {
            if (paused || drain.delay > 0f || drain.root == null) HideDrain(drain);
            else { UpdateDrainAnchors(drain); UpdateDrainIcon(drain); UpdateDrainLight(drain); }
        }
    }

    // Stopping an unpaid icon releases its in-flight marker for the director's
    // usual recovery settlement. A paid icon's fading tail never owns payment.
    void StopResourceDrains()
    {
        var stopped = drains.ToArray(); drains.Clear();
        foreach (var drain in stopped) ReleaseDrain(drain, true);
    }
}
