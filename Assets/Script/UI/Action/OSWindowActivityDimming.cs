using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Run after the window, Live-message and memory-sequence presentation layers.
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public sealed class OSWindowActivityDimming : MonoBehaviour
{
    const string OverlayName = "ActivityDimOverlay";
    const float DimAlpha = 0.75f;
    public static Color DimColor => new Color(0f, 0f, 0f, DimAlpha);

    struct DrawOrder
    {
        public int layer;
        public int order;
        public Transform transform;
    }

    struct WindowGroup
    {
        public bool pinned;
        public DrawOrder front;
    }

    OS_Handle os;
    readonly List<OSWindowUnit> windows = new List<OSWindowUnit>();
    readonly HashSet<OSWindowUnit> visibleWindows = new HashSet<OSWindowUnit>();
    readonly Dictionary<OSWindowUnit, WindowGroup> groups = new Dictionary<OSWindowUnit, WindowGroup>();
    readonly Dictionary<OSWindowUnit, Image> overlays = new Dictionary<OSWindowUnit, Image>();
    readonly List<OSWindowUnit> staleWindows = new List<OSWindowUnit>();

    public OSWindowUnit FrontmostWindow { get; private set; }

    void Awake() => os = GetComponent<OS_Handle>();

    void LateUpdate() => RefreshPresentation();

    void OnDisable()
    {
        FrontmostWindow = null;
        HideAllOverlays();
    }

    void OnDestroy()
    {
        foreach (var overlay in overlays.Values)
            if (overlay != null) Destroy(overlay.gameObject);
        overlays.Clear();
    }

    public bool IsDimmed(OSWindowUnit window) => window != null
        && overlays.TryGetValue(window, out Image overlay)
        && overlay != null && overlay.gameObject.activeInHierarchy;

    public void RefreshPresentation()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        FrontmostWindow = null;
        if (!isActiveAndEnabled || os == null || !os.IsOSVisible)
        {
            HideAllOverlays();
            return;
        }

        windows.Clear();
        visibleWindows.Clear();
        groups.Clear();
        os.GetComponentsInChildren(false, windows);
        Transform errorPool = os.ErrorWindowPoolController != null
            ? os.ErrorWindowPoolController.transform : null;

        // Parent and auxiliary windows share one activity state. A foreground
        // Canvas lift is a temporary pin, not a change to the ordinary window
        // stack: Live/Message must not dim the Memory window being read.
        foreach (var window in windows)
        {
            if (!IsVisible(window)) continue;
            // Fault popups are a screen effect, not desktop applications.
            // They neither take activity from the repair board nor receive dimming.
            if (errorPool != null && window.transform.IsChildOf(errorPool)) continue;
            visibleWindows.Add(window);
            OSWindowUnit root = ResolveGroupRoot(window);
            DrawOrder stackOrder = GetDrawOrder(root.transform.parent, root.transform);
            DrawOrder drawOrder = GetDrawOrder(window.WindowRect, window.transform);
            bool pinned = window.IsAlwaysOnTop || window.IsForceTopmost
                || root.IsAlwaysOnTop || root.IsForceTopmost
                || CompareCanvasOrder(drawOrder, stackOrder) > 0;
            if (!groups.TryGetValue(root, out WindowGroup group))
                group = new WindowGroup { front = stackOrder, pinned = pinned };
            else
                group.pinned |= pinned;
            groups[root] = group;
        }

        DrawOrder frontmostOrder = default;
        foreach (var pair in groups)
        {
            if (pair.Value.pinned) continue;
            if (FrontmostWindow == null || CompareDrawOrder(pair.Value.front, frontmostOrder) > 0)
            {
                FrontmostWindow = pair.Key;
                frontmostOrder = pair.Value.front;
            }
        }

        foreach (var window in windows)
        {
            if (!visibleWindows.Contains(window)) continue;
            OSWindowUnit root = ResolveGroupRoot(window);
            // Progress installers remain legible even when their initial
            // background placement (or another active window) puts them below
            // the ordinary stack. This is a window-type exception, including
            // normal Tool installs and the detached chapter-four installer.
            bool dimmed = !window.TryGetComponent<OSToolInstallWindowController>(out _)
                && root != FrontmostWindow && !groups[root].pinned;
            if (!overlays.TryGetValue(window, out Image overlay) || overlay == null)
            {
                if (!dimmed) continue;
                overlay = CreateOverlay(window);
                if (overlay == null) continue;
                overlays[window] = overlay;
            }

            if (dimmed) PlaceOverlayBeforeChildWindows(window, overlay.rectTransform);
            SetOverlayVisible(overlay, dimmed);
        }

        staleWindows.Clear();
        foreach (var pair in overlays)
        {
            if (pair.Key == null || pair.Value == null)
            {
                if (pair.Value != null) Destroy(pair.Value.gameObject);
                staleWindows.Add(pair.Key);
                continue;
            }
            if (!visibleWindows.Contains(pair.Key)) SetOverlayVisible(pair.Value, false);
        }
        foreach (var window in staleWindows) overlays.Remove(window);
    }

    void HideAllOverlays()
    {
        foreach (var overlay in overlays.Values) SetOverlayVisible(overlay, false);
    }

    static void SetOverlayVisible(Image overlay, bool visible)
    {
        if (overlay != null && overlay.gameObject.activeSelf != visible)
            overlay.gameObject.SetActive(visible);
    }

    static bool IsVisible(OSWindowUnit window) => window != null && window.IsOpen
        && !window.IsMinimized && window.gameObject.activeInHierarchy;

    static OSWindowUnit ResolveGroupRoot(OSWindowUnit window)
    {
        OSWindowUnit root = window;
        for (Transform node = window.transform.parent; node != null; node = node.parent)
            if (node.TryGetComponent(out OSWindowUnit ancestor)) root = ancestor;
        return root;
    }

    static DrawOrder GetDrawOrder(Transform start, Transform windowTransform)
    {
        var result = new DrawOrder { transform = windowTransform };
        for (Transform node = start; node != null; node = node.parent)
        {
            if (!node.TryGetComponent(out Canvas canvas) || !canvas.isActiveAndEnabled
                || (!canvas.isRootCanvas && !canvas.overrideSorting)) continue;
            result.layer = SortingLayer.GetLayerValueFromID(canvas.sortingLayerID);
            result.order = canvas.sortingOrder;
            break;
        }
        return result;
    }

    static int CompareCanvasOrder(DrawOrder left, DrawOrder right)
    {
        int comparison = left.layer.CompareTo(right.layer);
        return comparison != 0 ? comparison : left.order.CompareTo(right.order);
    }

    static int CompareDrawOrder(DrawOrder left, DrawOrder right)
    {
        int comparison = CompareCanvasOrder(left, right);
        return comparison != 0 ? comparison : CompareHierarchy(left.transform, right.transform);
    }

    static int CompareHierarchy(Transform left, Transform right)
    {
        if (left == right) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        int leftDepth = 0, rightDepth = 0;
        for (var node = left; node != null; node = node.parent) leftDepth++;
        for (var node = right; node != null; node = node.parent) rightDepth++;
        Transform a = left, b = right;
        for (int depth = leftDepth; depth > rightDepth; depth--) a = a.parent;
        for (int depth = rightDepth; depth > leftDepth; depth--) b = b.parent;
        if (a == b) return leftDepth.CompareTo(rightDepth);
        while (a.parent != b.parent) { a = a.parent; b = b.parent; }
        return a.GetSiblingIndex().CompareTo(b.GetSiblingIndex());
    }

    static Image CreateOverlay(OSWindowUnit window)
    {
        RectTransform windowRect = window.WindowRect;
        if (windowRect == null) return null;
        var node = new GameObject(OverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        node.layer = windowRect.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(windowRect, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var overlay = node.GetComponent<Image>();
        overlay.color = DimColor;
        overlay.raycastTarget = false;
        return overlay;
    }

    void PlaceOverlayBeforeChildWindows(OSWindowUnit window, RectTransform overlay)
    {
        Transform parent = overlay.parent;
        int firstChildIndex = parent.childCount;
        foreach (var childWindow in windows)
        {
            if (childWindow == window || !visibleWindows.Contains(childWindow)) continue;
            Transform branch = childWindow.transform;
            while (branch != null && branch.parent != parent) branch = branch.parent;
            if (branch != null) firstChildIndex = Mathf.Min(firstChildIndex, branch.GetSiblingIndex());
        }

        // Each child supplies its own mask. Drawing this parent's mask before
        // their subtrees prevents the black alpha from accumulating to 0.9375.
        int currentIndex = overlay.GetSiblingIndex();
        int targetIndex = firstChildIndex == parent.childCount
            ? parent.childCount - 1
            : firstChildIndex - (currentIndex < firstChildIndex ? 1 : 0);
        if (currentIndex != targetIndex) overlay.SetSiblingIndex(targetIndex);
    }
}
