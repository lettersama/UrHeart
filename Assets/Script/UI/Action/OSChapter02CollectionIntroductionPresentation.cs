using UnityEngine;

// A pre-unlock visual projection. The director supplies the paused phase clock;
// this component never reads player input, unlocks an actor, or writes a save.
[DisallowMultipleComponent]
public sealed class OSChapter02CollectionIntroductionPresentation : MonoBehaviour
{
    public enum Phase { Roam, WatchPlayer, Assemble, Return }
    public const float AssembleSeconds = 3.4f;
    public const float ReturnSeconds = .65f;
    const float LiveScale = .82f;
    static readonly Color PointerRed = new Color32(216, 52, 58, 255);
    static readonly Color PointerPressed = new Color32(177, 32, 39, 255);
    static readonly Color Ink = new Color32(31, 31, 29, 255);
    static readonly Color Paper = new Color32(220, 216, 207, 255);
    static readonly Color QuietInk = new Color32(115, 112, 105, 255);
    static readonly Vector2[] AssemblyTips =
    {
        new Vector2(-25, 10), new Vector2(25, -12), new Vector2(-5, -26),
        new Vector2(16, 8), new Vector2(30, 18)
    };

    OS_Handle os;
    OSWindowUnit live, memory, settings;
    RectTransform visualRoot, pointer;
    UnityEngine.UI.Image pointerImage;
    readonly RectTransform[] fragments = new RectTransform[3];
    readonly CanvasGroup[] fragmentGroups = new CanvasGroup[3];
    readonly RectTransform[] progressBars = new RectTransform[3];
    Phase phase;
    bool hasPhase, requestedVisible = true, pointerPlaced;
    Vector2 pointerPoint, phasePointerFrom, roamStart, roamMid, roamTap, workCenter;

    bool liveSnapshot, liveHeld, scaleApplied;
    OSWindowUnit snapshotLive;
    RectTransform snapshotRect, snapshotParent;
    Vector2 originalPosition, originalSize, liveSideOffset, pointerHome;
    Vector3 originalScale, appliedScale;

    public bool IsComplete { get; private set; }

    /// <summary>
    /// Render one frame using phase-local unscaled seconds. False means the
    /// references/window lease are not ready; the caller should hold its clock.
    /// Repeated elapsed values freeze the pose, including the WatchPlayer tip.
    /// </summary>
    public bool Present(Phase nextPhase, float elapsed)
    {
        if (!Finite(elapsed) || elapsed < 0f || !isActiveAndEnabled) return false;
        Resolve();
        if (!requestedVisible || !Available || !EnsureVisuals())
        { HideAndRelease(); return false; }
        if ((nextPhase == Phase.Roam || nextPhase == Phase.WatchPlayer) && !WindowReady(memory))
        { HideAndRelease(); return false; }
        if (!hasPhase || phase != nextPhase)
        {
            if (nextPhase == Phase.Roam || nextPhase == Phase.WatchPlayer) ReleaseLive(true);
            phase = nextPhase; hasPhase = true; IsComplete = false;
            phasePointerFrom = pointerPlaced ? pointerPoint : MemorySidePoint();
            if (phase == Phase.Roam)
            {
                roamStart = LiveSidePoint();
                roamTap = MemoryTapPoint();
                roamMid = ClampPoint(new Vector2(
                    Mathf.Lerp(roamStart.x, roamTap.x, .45f), roamTap.y - 35f));
            }
            else if (phase == Phase.WatchPlayer && !pointerPlaced) PlacePointer(MemorySidePoint());
        }
        if ((phase == Phase.Assemble || phase == Phase.Return)
            && !(phase == Phase.Return && elapsed >= ReturnSeconds) && !OwnLive())
        { HideAndRelease(); return false; }

        visualRoot.gameObject.SetActive(true);
        HideFragments();
        SetPressed(false);
        switch (phase)
        {
            case Phase.Roam: RenderRoam(elapsed); break;
            case Phase.WatchPlayer: PlacePointer(phasePointerFrom); break;
            case Phase.Assemble: return RenderAssembly(elapsed);
            case Phase.Return: return RenderReturn(elapsed);
        }
        return true;
    }

    /// <summary>Hide and return Live to its base pose; the supplied phase clock can resume later.</summary>
    public void SetVisible(bool visible)
    {
        requestedVisible = visible;
        if (!visible) HideAndRelease();
    }

    /// <summary>Cancel, restore all properties owned by this view, and remove its decoration.</summary>
    public void ResetPresentation()
    {
        ReleaseLive(true);
        if (visualRoot != null)
        {
            visualRoot.gameObject.SetActive(false);
            Destroy(visualRoot.gameObject);
        }
        visualRoot = pointer = null; pointerImage = null;
        for (int i = 0; i < fragments.Length; i++)
        { fragments[i] = progressBars[i] = null; fragmentGroups[i] = null; }
        hasPhase = pointerPlaced = IsComplete = false;
        requestedVisible = true;
    }

    void Resolve()
    {
        if (os == null) os = GetComponent<OS_Handle>() ?? OS_Handle.Instance;
        var nextLive = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (live != nextLive) { ReleaseLive(true); live = nextLive; }
        memory = os?.MemoryWindowController?.GetComponent<OSWindowUnit>();
        if (settings == null && os != null)
            settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
    }

    bool Available => Application.isPlaying && Application.isFocused && os != null && os.IsOSVisible
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput
        && !(settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy);

    bool EnsureVisuals()
    {
        var parent = live != null ? live.transform.parent as RectTransform : null;
        if (parent == null || CursorHandle.Instance?.NormalSprite == null) return false;
        if (visualRoot != null && visualRoot.parent != parent) ResetPresentation();
        if (visualRoot != null) return true;
        var node = new GameObject("Chapter02CollectionIntroductionVisuals", typeof(RectTransform), typeof(CanvasGroup));
        node.layer = parent.gameObject.layer;
        visualRoot = (RectTransform)node.transform; visualRoot.SetParent(parent, false);
        visualRoot.anchorMin = Vector2.zero; visualRoot.anchorMax = Vector2.one;
        visualRoot.offsetMin = visualRoot.offsetMax = Vector2.zero;
        var group = node.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
        for (int i = 0; i < fragments.Length; i++) BuildFragment(i);
        pointerImage = NewImage("TemporaryRedPointer", visualRoot, Vector2.zero, Vector2.one, PointerRed);
        pointer = pointerImage.rectTransform; pointer.pivot = new Vector2(.05f, .95f);
        SetPressed(false);
        node.SetActive(false);
        return true;
    }

    void BuildFragment(int index)
    {
        Vector2 size = index == 0 ? new Vector2(104, 62) : index == 1 ? new Vector2(82, 50) : new Vector2(92, 56);
        var frame = NewImage("AssemblyFragment" + index, visualRoot, Vector2.zero, size, Paper).rectTransform;
        fragments[index] = frame;
        var group = frame.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false; fragmentGroups[index] = group;
        // Match the desktop's one-pixel rectangular frame and small title strip.
        Bar("Top", frame, 0, 0, size.x, 1, Ink); Bar("Bottom", frame, 0, size.y - 1, size.x, 1, Ink);
        Bar("Left", frame, 0, 1, 1, size.y - 2, Ink); Bar("Right", frame, size.x - 1, 1, 1, size.y - 2, Ink);
        Bar("Title", frame, 1, 1, size.x - 2, 10, Ink);
        Bar("TitleMark", frame, 5, 5, 14 + index * 4, 2, Paper);
        Bar("CloseMark", frame, size.x - 8, 4, 3, 3, Paper);
        Bar("Row0", frame, 7, 18, size.x - 25, 2, QuietInk);
        Bar("Row1", frame, 7, 25, size.x - 40, 2, QuietInk);
        Bar("Row2", frame, 7, 32, size.x - 32, 2, QuietInk);
        progressBars[index] = Bar("Progress", frame, 7, size.y - 10, 1, 3, PointerRed);
        frame.gameObject.SetActive(false);
    }

    void RenderRoam(float elapsed)
    {
        if (elapsed < 1.25f) PlacePointer(Curve(roamStart, roamMid, elapsed / 1.25f, 13f));
        else if (elapsed < 2.05f) PlacePointer(roamMid + new Vector2(Mathf.FloorToInt((elapsed - 1.25f) * 3f) % 2, 0));
        else if (elapsed < 2.85f) PlacePointer(Curve(roamMid, roamTap, (elapsed - 2.05f) / .8f, -9f));
        else if (elapsed < 3.12f)
        {
            PlacePointer(roamTap); SetPressed(elapsed >= 2.94f && elapsed < 3.04f);
        }
        else if (elapsed < 3.75f) PlacePointer(Curve(roamTap, roamTap + new Vector2(13, -9), (elapsed - 3.12f) / .63f, 3f));
        else
        {
            float t = (elapsed - 3.75f) % 5f;
            Vector2 edge = roamTap + new Vector2(13, -9), drift = edge + new Vector2(22, -20);
            PlacePointer(t < 2.5f ? Curve(edge, drift, t / 2.5f, 4f) : Curve(drift, edge, (t - 2.5f) / 2.5f, -4f));
        }
    }

    bool RenderAssembly(float elapsed)
    {
        float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .6f));
        if (!ApplyLive(settle)) { HideAndRelease(); return false; }
        RenderFragments(elapsed, 1f);
        float work = Mathf.Max(0f, elapsed - .55f);
        int pass = Mathf.Min(4, Mathf.FloorToInt(work / .55f));
        Vector2 target = workCenter + AssemblyTips[pass];
        Vector2 from = pass == 0 ? phasePointerFrom : workCenter + AssemblyTips[pass - 1];
        PlacePointer(Curve(from, target, Mathf.Clamp01((work - pass * .55f) / .32f), pass % 2 == 0 ? 5f : -4f));
        SetPressed(work % .55f > .34f && work % .55f < .43f && elapsed < AssembleSeconds - .25f);
        IsComplete = elapsed >= AssembleSeconds;
        return true;
    }

    bool RenderReturn(float elapsed)
    {
        float t = Mathf.Clamp01(elapsed / ReturnSeconds);
        if (t >= 1f)
        {
            if (!liveSnapshot) pointerHome = LiveSidePoint();
            ReleaseLive(true); HideFragments(); PlacePointer(pointerHome);
            IsComplete = true; return true;
        }
        if (!ApplyLive(1f - Mathf.SmoothStep(0f, 1f, t))) { HideAndRelease(); return false; }
        RenderFragments(AssembleSeconds, 1f - Mathf.Clamp01(t * 1.6f));
        PlacePointer(Curve(phasePointerFrom, pointerHome, t, -7f));
        IsComplete = false;
        return true;
    }

    void RenderFragments(float elapsed, float visibility)
    {
        for (int i = 0; i < fragments.Length; i++)
        {
            float age = elapsed - .55f - i * .42f;
            bool active = age >= 0f && visibility > 0f;
            fragments[i].gameObject.SetActive(active);
            if (!active) continue;
            Vector2 scatter = i == 0 ? new Vector2(-36, 20) : i == 1 ? new Vector2(35, -8) : new Vector2(-16, -40);
            Vector2 assembled = new Vector2(i * 5f - 5f, 5f - i * 5f);
            float join = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 2.15f) / .85f));
            fragments[i].anchoredPosition = Round(workCenter + Vector2.Lerp(scatter, assembled, join));
            // Two short local redraws, then a steady frame; no full-screen flash.
            bool redraw = age < .13f || (age >= .28f && age < .38f);
            fragmentGroups[i].alpha = visibility * (redraw ? .35f : 1f);
            progressBars[i].SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Round((fragments[i].rect.width - 14f) * Mathf.Clamp01(age / 1.35f)));
        }
    }

    bool OwnLive()
    {
        if (!WindowReady(live)) return false;
        if (liveHeld && !live.OwnsPresentationOffset(this)) { ReleaseLive(true); return false; }
        if (liveHeld) return true;
        var rect = live.WindowRect;
        if (liveSnapshot && (snapshotLive != live || rect.parent != snapshotParent
            || (rect.anchoredPosition - originalPosition).sqrMagnitude > .01f
            || (rect.localScale - originalScale).sqrMagnitude > .0001f || rect.rect.size != originalSize))
            ReleaseLive(true);
        if (!live.TrySetPresentationOffset(this, Vector2.zero)) return false;
        liveHeld = true;
        if (liveSnapshot) return true;
        snapshotLive = live; snapshotRect = rect; snapshotParent = rect.parent as RectTransform;
        originalPosition = rect.anchoredPosition; originalSize = rect.rect.size; originalScale = rect.localScale;
        pointerHome = LiveSidePoint(); liveSnapshot = true;
        Vector2 center = snapshotParent.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        bool right = center.x >= snapshotParent.rect.center.x;
        float halfWidth = rect.rect.width * Mathf.Abs(originalScale.x) * LiveScale * .5f;
        float halfHeight = rect.rect.height * Mathf.Abs(originalScale.y) * LiveScale * .5f;
        Vector2 corner = new Vector2(right ? snapshotParent.rect.xMax - halfWidth - 12f : snapshotParent.rect.xMin + halfWidth + 12f,
            snapshotParent.rect.yMin + halfHeight + 18f);
        Vector2 pivot = snapshotParent.InverseTransformPoint(rect.position);
        liveSideOffset = corner - (pivot + (center - pivot) * LiveScale);
        workCenter = ClampPoint(new Vector2(right ? visualRoot.rect.xMax - 95f : visualRoot.rect.xMin + 95f,
            visualRoot.rect.yMax - 66f), 68f);
        return true;
    }

    bool ApplyLive(float amount)
    {
        if (!liveHeld || snapshotLive == null || snapshotRect == null
            || !snapshotLive.TrySetPresentationOffset(this, liveSideOffset * amount)) return false;
        appliedScale = Vector3.Lerp(originalScale, originalScale * LiveScale, amount);
        appliedScale.z = originalScale.z;
        snapshotRect.localScale = appliedScale; scaleApplied = true;
        return true;
    }

    void ReleaseLive(bool forget)
    {
        if (snapshotLive != null) snapshotLive.ClearPresentationOffset(this);
        if (scaleApplied && snapshotRect != null && (snapshotRect.localScale - appliedScale).sqrMagnitude < .0001f)
            snapshotRect.localScale = originalScale;
        liveHeld = scaleApplied = false;
        if (!forget) return;
        liveSnapshot = false; snapshotLive = null; snapshotRect = snapshotParent = null;
    }

    void HideAndRelease()
    {
        if (visualRoot != null) visualRoot.gameObject.SetActive(false);
        ReleaseLive(false);
    }

    void HideFragments()
    {
        foreach (var fragment in fragments) if (fragment != null) fragment.gameObject.SetActive(false);
    }

    Vector2 MemoryTapPoint()
    {
        var rect = memory != null ? memory.WindowRect : null;
        return rect != null ? ClampPoint(LocalPoint(rect, new Vector2(rect.rect.xMax - 22f, rect.rect.yMax - 9f)))
            : ClampPoint(visualRoot.rect.center + new Vector2(-65, 35));
    }
    Vector2 MemorySidePoint() => MemoryTapPoint() + new Vector2(13, -9);
    Vector2 LiveSidePoint()
    {
        var rect = live != null ? live.WindowRect : null;
        return rect != null ? ClampPoint(LocalPoint(rect, new Vector2(rect.rect.xMin - 13f, rect.rect.center.y)))
            : ClampPoint(visualRoot.rect.center + new Vector2(110, -20));
    }
    Vector2 LocalPoint(RectTransform source, Vector2 local) => visualRoot.InverseTransformPoint(source.TransformPoint(local));
    Vector2 ClampPoint(Vector2 point, float margin = 12f)
    {
        Rect bounds = visualRoot.rect;
        return new Vector2(Mathf.Clamp(point.x, bounds.xMin + margin, bounds.xMax - margin),
            Mathf.Clamp(point.y, bounds.yMin + margin, bounds.yMax - margin));
    }
    void PlacePointer(Vector2 point)
    {
        pointerPoint = ClampPoint(point); pointerPlaced = true;
        if (pointer != null) pointer.anchoredPosition = Round(pointerPoint);
    }
    void SetPressed(bool pressed)
    {
        if (pointerImage == null) return;
        Sprite normal = CursorHandle.Instance?.NormalSprite, down = CursorHandle.Instance?.PressedSprite;
        pointerImage.sprite = pressed && down != null ? down : normal;
        pointerImage.color = pressed ? PointerPressed : PointerRed;
        if (pointerImage.sprite != null) pointerImage.rectTransform.sizeDelta = pointerImage.sprite.rect.size * 1.4f;
    }
    static Vector2 Curve(Vector2 from, Vector2 to, float t, float bow)
    {
        t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        return Vector2.Lerp(from, to, t) + Vector2.up * (Mathf.Sin(t * Mathf.PI) * bow);
    }
    static Vector2 Round(Vector2 point) => new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool WindowReady(OSWindowUnit window) => window != null && window.WindowRect != null
        && window.IsOpen && !window.IsMinimized && !window.IsOpening && !window.IsClosing
        && window.gameObject.activeInHierarchy;
    static UnityEngine.UI.Image NewImage(string name, RectTransform parent, Vector2 position, Vector2 size, Color color)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = node.GetComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
        return image;
    }
    static RectTransform Bar(string name, RectTransform parent, float x, float y, float width, float height, Color color)
    {
        var rect = NewImage(name, parent, Vector2.zero, new Vector2(width, height), color).rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y); return rect;
    }

    void LateUpdate()
    {
        if (!hasPhase) return;
        if (!requestedVisible || !Available) { HideAndRelease(); return; }
        if ((phase == Phase.Roam || phase == Phase.WatchPlayer) && !WindowReady(memory))
        { HideAndRelease(); return; }
        if ((phase == Phase.Assemble || phase == Phase.Return) && !IsComplete && !WindowReady(live))
        { HideAndRelease(); return; }
        if (visualRoot != null && visualRoot.gameObject.activeSelf) visualRoot.SetAsLastSibling();
    }
    void OnApplicationFocus(bool focused) { if (!focused) HideAndRelease(); }
    void OnDisable() => ResetPresentation();
    void OnDestroy() => ResetPresentation();
}
