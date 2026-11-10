using UnityEngine;
using UnityEngine.UI;

// While Live has a message on screen, her portrait window, the message window and
// her red pointer draw above every ordinary window, pinned ones included, the same
// way the memory illustrations temporarily draw above Live (+1). Only drawing order
// changes: no focus, sibling order, pinning or saved layout. A story that has
// already lifted one of these (hidden memory, nightfall) keeps it; this waits.
public sealed partial class OSLinkMessageController
{
    const int FrontLiftAboveParent = 2;
    const string FrontLiftPointerName = "CharacterPermanentRedPointer";

    sealed class FrontLift
    {
        public RectTransform target;
        public int offset;
        public bool captured, hadCanvas, overrideSorting, enabled;
        public int order, layer, appliedOrder;
        public Canvas addedCanvas;
        public GraphicRaycaster addedRaycaster;
    }

    FrontLift liveLift, linkLift, pointerLift, motherKeyLift, motherHeartLift;

    void LateUpdate() => TickFrontLift();

    // The mirror owner also ticks this while the message window is inactive.
    public void RefreshChapter03MirrorFrontLift() => TickFrontLift();

    bool WantsMirrorFrontLift => ownerCharacterWindow != null && ownerCharacterWindow.IsOpen
        && !ownerCharacterWindow.IsMinimized && ownerCharacterWindow.gameObject.activeInHierarchy
        && OS_Handle.Instance?.Chapter03Story?.Presentation?.IsMirrorForeground == true;

    // A closed message releases its own lift. Mirror still owns Live's layer
    // during the gap, and explicitly refreshes this when that ownership ends.
    void ReleaseFrontLifts()
    {
        if (liveLift != null && !WantsMirrorFrontLift) ReleaseFrontLift(liveLift);
        if (linkLift != null) ReleaseFrontLift(linkLift);
        if (pointerLift != null) ReleaseFrontLift(pointerLift);
        if (motherKeyLift != null) ReleaseFrontLift(motherKeyLift);
        if (motherHeartLift != null) ReleaseFrontLift(motherHeartLift);
    }

    bool WantsFrontLift
    {
        get
        {
            OSWindowUnit displayWindow = IsChapter04ChatVisible ? ownerCharacterWindow : linkWindow;
            if ((!hasDisplayedMessage && !IsChapter04ChatVisible) || !isOSVisible || displayWindow == null || !displayWindow.IsOpen
                || displayWindow.IsMinimized || displayWindow.IsClosing || !displayWindow.gameObject.activeInHierarchy
                || OSSessionPersistence.IsRestoring || GameTestConsole.BlocksGameplayInput)
                return false;
            var os = OS_Handle.Instance;
            var treatment = IsChapter04ChatVisible && os?.HeartHealToolWindowController != null
                ? os.HeartHealToolWindowController.GetComponent<OSWindowUnit>() : null;
            if (treatment != null && treatment.IsOpen && !treatment.IsMinimized
                && treatment.gameObject.activeInHierarchy) return false;
            var settings = os != null ? os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>() : null;
            return settings == null || !settings.IsOpen || settings.IsMinimized || !settings.gameObject.activeInHierarchy;
        }
    }

    void TickFrontLift()
    {
        bool wants = WantsFrontLift;
        Transform live = ownerCharacterWindow != null ? ownerCharacterWindow.transform : null;
        Transform pointer = live != null && live.parent != null ? live.parent.Find(FrontLiftPointerName) : null;
        // The attached conversation and portrait share Live's canvas. The old
        // message window is only a hidden typewriter source in chapter four.
        bool liftPortrait = wants;
        UpdateFrontLift(ref liveLift, live as RectTransform, 0, liftPortrait || WantsMirrorFrontLift);
        UpdateFrontLift(ref linkLift, linkWindow != null ? linkWindow.transform as RectTransform : null,
            1, wants && !IsChapter04ChatVisible);
        // The offered key is the current story interaction, so its whole window
        // (including its raycaster) must sit above Live and the pinned TV.
        var keyWindow = OS_Handle.Instance?.MotherKeyController?.Window;
        UpdateFrontLift(ref motherKeyLift, keyWindow != null ? keyWindow.WindowRect : null, 1,
            wants && IsChapter04ChatVisible && keyWindow != null && keyWindow.IsOpen
                && !keyWindow.IsMinimized && keyWindow.gameObject.activeInHierarchy);
        var heartWindow = OS_Handle.Instance?.MotherHeartController?.Window;
        var giftStage = OS_Handle.Instance?.Chapter04Story?.MotherGiftStage;
        bool showingGiftHeart = giftStage >= OSChapter04MotherGiftStage.Unlocked
            && giftStage <= OSChapter04MotherGiftStage.CloseRequested;
        UpdateFrontLift(ref motherHeartLift, heartWindow != null ? heartWindow.WindowRect : null, 1,
            wants && IsChapter04ChatVisible && showingGiftHeart && heartWindow != null && heartWindow.IsOpen
                && !heartWindow.IsMinimized && heartWindow.gameObject.activeInHierarchy);
        UpdateFrontLift(ref pointerLift, pointer as RectTransform, 2,
            liftPortrait && pointer != null && pointer.gameObject.activeInHierarchy);
    }

    void UpdateFrontLift(ref FrontLift lift, RectTransform target, int offset, bool wants)
    {
        if (lift != null && lift.target != target)
        {
            ReleaseFrontLift(lift);
            lift = null;
        }
        if (target == null) return;
        if (lift == null) lift = new FrontLift { target = target, offset = offset };
        if (wants) ApplyFrontLift(lift);
        else ReleaseFrontLift(lift);
    }

    int FrontLiftOrder(FrontLift lift, out Canvas parentCanvas)
    {
        parentCanvas = lift.target.parent != null ? lift.target.parent.GetComponentInParent<Canvas>() : null;
        return parentCanvas != null ? parentCanvas.sortingOrder + FrontLiftAboveParent + lift.offset : int.MinValue;
    }

    // Another lift on the target right now. It may have captured ours and will
    // hand our order back when it lets go, so recognise ours by its order.
    static bool IsForeignLift(Canvas canvas, int ourOrder) =>
        canvas != null && canvas.overrideSorting && canvas.sortingOrder != ourOrder;

    void ApplyFrontLift(FrontLift lift)
    {
        int order = FrontLiftOrder(lift, out Canvas parentCanvas);
        if (parentCanvas == null) return;
        lift.target.TryGetComponent(out Canvas canvas);
        if (IsForeignLift(canvas, lift.captured ? lift.appliedOrder : order)) return;
        if (!lift.captured)
        {
            lift.captured = true;
            // A canvas already at our order is one we left behind: we added it.
            lift.hadCanvas = canvas != null && canvas != lift.addedCanvas
                && !(canvas.overrideSorting && canvas.sortingOrder == order);
            if (lift.hadCanvas)
            {
                lift.overrideSorting = canvas.overrideSorting; lift.order = canvas.sortingOrder;
                lift.layer = canvas.sortingLayerID; lift.enabled = canvas.enabled;
            }
        }
        if (canvas == null) canvas = lift.target.gameObject.AddComponent<Canvas>();
        if (!lift.hadCanvas) lift.addedCanvas = canvas;
        // A nested canvas needs its own raycaster, or Live's options stop taking clicks.
        if (!lift.target.TryGetComponent<GraphicRaycaster>(out _))
            lift.addedRaycaster = lift.target.gameObject.AddComponent<GraphicRaycaster>();
        if (lift.addedRaycaster != null) lift.addedRaycaster.enabled = true;
        canvas.enabled = true;
        canvas.overrideSorting = true;
        canvas.sortingLayerID = parentCanvas.sortingLayerID;
        canvas.sortingOrder = order;
        lift.appliedOrder = order;
    }

    void ReleaseFrontLift(FrontLift lift)
    {
        if (lift.target == null) { lift.captured = false; return; }
        lift.target.TryGetComponent(out Canvas canvas);
        int order = FrontLiftOrder(lift, out Canvas parentCanvas);
        // Live is Link's parent; releasing Live changes the newly calculated
        // order before Link is released. Recognise the order actually applied.
        if (lift.captured) order = lift.appliedOrder;
        if (IsForeignLift(canvas, order)) return;
        bool ours = canvas != null && canvas.overrideSorting && canvas.sortingOrder == order;
        if (!lift.captured && !ours) return;
        if (canvas != null)
        {
            if (!lift.captured || !lift.hadCanvas)
            {
                // OnDisable can run inside SetActive. Keep our components for
                // reuse: destroying them here is unsafe, and deferred destruction
                // can remove a canvas that has already been lifted again.
                lift.addedCanvas = canvas;
                canvas.overrideSorting = false;
                canvas.sortingLayerID = parentCanvas != null ? parentCanvas.sortingLayerID : 0;
                canvas.sortingOrder = parentCanvas != null ? parentCanvas.sortingOrder : 0;
                canvas.enabled = true;
            }
            else
            {
                // Canvas ignores local order changes while inheriting sorting.
                // Restore its stored values before returning to inheritance.
                canvas.overrideSorting = true;
                canvas.sortingLayerID = lift.layer; canvas.sortingOrder = lift.order;
                canvas.overrideSorting = lift.overrideSorting; canvas.enabled = lift.enabled;
            }
        }
        // An inherited nested canvas still needs its own raycaster. A canvas
        // that existed before this lift gets its original input behavior back.
        if (lift.addedRaycaster != null)
            lift.addedRaycaster.enabled = canvas != null && canvas == lift.addedCanvas;
        // Unity treats an inactive nested canvas as a root canvas and ignores
        // overrideSorting=false. Keep the restoration pending until it is active;
        // a restore can then either finish releasing or lift it again immediately.
        lift.captured = canvas != null && !canvas.gameObject.activeInHierarchy;
        if (lift.captured) lift.appliedOrder = canvas.sortingOrder;
    }
}
