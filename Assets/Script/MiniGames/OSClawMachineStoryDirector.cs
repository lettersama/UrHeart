using UnityEngine;
using UnityEngine.UI;

// Lives on the OS root. The red pointer is a second, non-interactive UI image;
// it never takes ownership of the player's cursor or changes its position.
[DisallowMultipleComponent]
public sealed class OSClawMachineStoryDirector : MonoBehaviour
{
    const float PickupDelaySeconds = 2.5f;
    public const string IntroEntryId = "claw_machine_ch02_first_desire";
    public const string ThanksEntryId = "claw_machine_ch02_thanks";
    public const string NextEntryId = "claw_machine_ch02_next_desire";
    public const string IntroSignal = "claw_machine.story.first_desire";
    public const string ThanksSignal = "claw_machine.story.thanks";
    public const string NextSignal = "claw_machine.story.next_desire";

    [SerializeField] OSLinkMessageController linkMessages;
    [SerializeField] OSWindowUnit characterWindow;
    [SerializeField] RectTransform presentationLayer;
    [SerializeField] Sprite normalCursor;
    [SerializeField] Sprite pressedCursor;
    [SerializeField] Color cursorColor = new Color32(216, 52, 58, 255);
    [SerializeField] Vector2 cursorHotspot = new Vector2(.05f, .95f);
    [SerializeField, Min(.1f)] float cursorScale = 1.4f;

    enum PickupPhase { Waiting, Approaching, Pressing, Dragging, Releasing }
    OSClawMachineGameWindowController game;
    OSMemoryProcessSaveData observedSave;
    PickupPhase phase;
    float outletElapsed, phaseElapsed, messageHold;
    double lastVisibleTime = -1d;
    double messageVisibleTime = -1d;
    string messageId;
    bool messageTyped, deliveryObserved;
    int deliveredPrizeIndex = -1;
    RectTransform visualRoot;
    Image cursorImage, draggedPrize, originalPrize;
    bool originalPrizeEnabled;
    Vector2 pickupOrigin, pickupPoint, pickupTarget, pickupTargetVelocity, pickupFollowVelocity;
    float pickupHiddenHold;

    // Include this in LinkMessage replacement guards. The game itself remains
    // closable/minimizable; hiding it pauses and hides only this director's message.
    public bool IsMessageProtected => isActiveAndEnabled && OwnMessageDisplayed;
    public bool IsPickupInProgress => phase != PickupPhase.Waiting;

    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    bool OwnMessageDisplayed => linkMessages != null && IsOwnEntry(linkMessages.DisplayedEntryId)
        && linkMessages.LinkWindow != null && linkMessages.LinkWindow.IsOpen;

    void OnEnable()
    {
        ResolveReferences();
        if (linkMessages != null) linkMessages.EntryTextPlaybackCompleted += OnTextCompleted;
        lastVisibleTime = -1d;
    }

    void OnDisable()
    {
        if (linkMessages != null) linkMessages.EntryTextPlaybackCompleted -= OnTextCompleted;
        PauseMessage();
        SetVisualsVisible(false);
        lastVisibleTime = -1d;
    }

    void OnDestroy() => ResetPresentation();

    void LateUpdate()
    {
        // LinkMessage is a child of Live. Clicking the game otherwise raises
        // its entire window over the left half of this attached speech window.
        if (OwnMessageDisplayed && game != null && game.IsPresentationVisible
            && characterWindow != null && characterWindow.transform.parent == game.transform.parent
            && characterWindow.transform.GetSiblingIndex() < game.transform.GetSiblingIndex())
            characterWindow.transform.SetSiblingIndex(game.transform.GetSiblingIndex());

        if (visualRoot != null && visualRoot.gameObject.activeSelf)
            KeepPickupBehindLive();
    }

    void ResolveReferences()
    {
        var os = OS_Handle.Instance;
        if (linkMessages == null) linkMessages = os != null ? os.LinkMessageController : null;
        if (characterWindow == null && os != null && os.CharacterWindowController != null)
            characterWindow = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        if (presentationLayer == null) presentationLayer = transform as RectTransform;
    }

    void ObserveSave()
    {
        if (ReferenceEquals(observedSave, State)) return;
        ResetPresentation();
        observedSave = State;
    }

    public void NotifyGameOpened(OSClawMachineGameWindowController source)
    {
        ObserveSave();
        game = source;
        lastVisibleTime = -1d;
    }

    public void NotifyPrizeDelivered(OSClawMachineGameWindowController source, int prizeIndex, RectTransform outletPrize)
    {
        ObserveSave();
        game = source;
        if (prizeIndex < 0 || prizeIndex >= 4 || observedSave == null
            || (deliveryObserved && deliveredPrizeIndex == prizeIndex)) return;
        BeginDelivery(prizeIndex);
    }

    void BeginDelivery(int prizeIndex)
    {
        DestroyVisuals();
        deliveredPrizeIndex = prizeIndex;
        deliveryObserved = true;
        outletElapsed = phaseElapsed = 0f;
        phase = PickupPhase.Waiting;
        lastVisibleTime = -1d;
        QueueDeliveryThanks(prizeIndex);
    }

    void QueueDeliveryThanks(int prizeIndex)
    {
        if (prizeIndex != 0 || observedSave == null || observedSave.chapter02ClawFirstPrizeTaken
            || observedSave.chapter02ClawThanksShown) return;
        // Receiving the requested toy supersedes the opening request's remaining
        // tail. The pickup clock continues independently from this short thanks.
        observedSave.chapter02ClawIntroShown = true;
        if (messageId == IntroEntryId) FinishOwnMessage();
        if (messageId != null) return;
        messageId = ThanksEntryId;
        messageTyped = false;
        messageHold = 0f;
        messageVisibleTime = -1d;
    }

    void Update()
    {
        ObserveSave();
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 2 || observedSave == null)
        {
            if (messageId != null || visualRoot != null) ResetPresentation();
            return;
        }
        if (game == null) game = OS_Handle.Instance?.ClawMachineGame;
        bool visible = game != null && game.IsPresentationVisible && OS_Handle.Instance?.IsOSVisible == true
            && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
            && !GameTestConsole.BlocksGameplayInput && OS_Handle.Instance?.IsSystemInputLocked != true;
        if (!visible)
        {
            PauseMessage();
            SetVisualsVisible(false);
            lastVisibleTime = -1d;
            return;
        }
        ResolveReferences();
        double now = Time.realtimeSinceStartupAsDouble;
        float delta = lastVisibleTime < 0d ? 0f : (float)(now - lastVisibleTime);
        lastVisibleTime = now;
        // A caught prize is saved before the lift/eject animation. Restored
        // delivery timing starts only after the controller actually exposes it.
        if (!deliveryObserved && observedSave.chapter02ClawOutletPrize > 0
            && game.IsOutletPrizeReady
            && game.OutletPrize != null && game.OutletPrize.gameObject.activeInHierarchy)
            BeginDelivery(observedSave.chapter02ClawOutletPrize - 1);
        if (deliveryObserved) outletElapsed += delta;

        if (messageId == null)
        {
            if (!observedSave.chapter02ClawIntroShown) messageId = IntroEntryId;
            else if (!observedSave.chapter02ClawNextRequestShown
                && observedSave.chapter02ClawFirstPrizeTaken)
                messageId = NextEntryId;
        }
        TickMessage();

        if (!deliveryObserved || game.OutletPrize == null) return;
        if (phase == PickupPhase.Waiting)
        {
            if (outletElapsed < PickupDelaySeconds || !EnsureLiveVisible() || normalCursor == null || presentationLayer == null) return;
            CreateVisuals();
            phase = PickupPhase.Approaching;
            phaseElapsed = 0f;
        }
        if (!EnsureLiveVisible()) { SetVisualsVisible(false); return; }
        SetVisualsVisible(true);
        TickPickup(delta);
    }

    public bool IsSecondRequestReady => observedSave != null
        && (observedSave.chapter02ClawNextRequestShown || (messageId == NextEntryId && messageTyped));

    public void AcceptSecondRequestForTrap()
    {
        if (!IsSecondRequestReady) return;
        observedSave.chapter02ClawNextRequestShown = true;
        if (messageId != NextEntryId) return;
        CloseOwnMessage();
        messageId = null;
        messageTyped = false;
        messageHold = 0f;
        messageVisibleTime = -1d;
    }

    void TickMessage()
    {
        if (messageId == null || linkMessages == null) return;
        if (OwnMessageDisplayed && linkMessages.DisplayedEntryId == messageId)
        {
            if (linkMessages.LinkWindow.IsMinimized || !linkMessages.LinkWindow.gameObject.activeInHierarchy)
            {
                messageVisibleTime = -1d;
                return;
            }
            if (!IsLiveVisible()) { PauseMessage(); return; }
            if (!messageTyped) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (messageVisibleTime >= 0d) messageHold += (float)(now - messageVisibleTime);
            messageVisibleTime = now;
            if (messageHold < 5f) return;
            if (messageId == IntroEntryId) observedSave.chapter02ClawIntroShown = true;
            else if (messageId == ThanksEntryId) observedSave.chapter02ClawThanksShown = true;
            else if (messageId == NextEntryId) observedSave.chapter02ClawNextRequestShown = true;
            FinishOwnMessage();
            return;
        }
        var state = GameDataManager.Instance.OSCharacterInteractionState;
        if (linkMessages.IsPresenting || linkMessages.IsWaitingForOption || linkMessages.IsWaitingForExternalCompletion
            || linkMessages.GlobalMessagesSuppressed || !string.IsNullOrEmpty(state.pendingGlobalEntryId)
            || !string.IsNullOrEmpty(state.presentingGlobalEntryId)) return;
        // Do not cut off the five-second tail of another ordinary message.
        if (linkMessages.LinkWindow != null && (linkMessages.LinkWindow.IsOpen || linkMessages.LinkWindow.IsClosing)) return;
        if (!EnsureLiveVisible()) return;
        state.SetGlobalEntryConsumed(messageId, false);
        messageTyped = false;
        linkMessages.NotifyGlobalExternalSignal(messageId == IntroEntryId ? IntroSignal
            : messageId == ThanksEntryId ? ThanksSignal : NextSignal);
    }

    void OnTextCompleted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (entry != null && entry.EntryId == messageId && ReferenceEquals(observedSave, State)
            && GameDataManager.Instance != null && GameDataManager.Instance.CurrentChapterIndex == 2)
        {
            messageTyped = true;
            messageVisibleTime = Time.realtimeSinceStartupAsDouble;
            if (entry.EntryId == IntroEntryId)
                game?.RevealTargetHint();
            else if (entry.EntryId == NextEntryId)
                game?.RevealSecondTargetHint();
        }
    }

    void PauseMessage()
    {
        if (!OwnMessageDisplayed) return;
        CloseOwnMessage();
        messageTyped = false;
        messageVisibleTime = -1d;
        // Preserve the remaining visible hold in memory. A save reload replays an
        // unfinished line; its completed receipt is written only after the hold.
        if (ReferenceEquals(observedSave, State) && messageId != null)
            GameDataManager.Instance?.OSCharacterInteractionState.SetGlobalEntryConsumed(messageId, false);
    }

    void CloseOwnMessage()
    {
        if (OwnMessageDisplayed) linkMessages.LinkWindow.CloseFromScript();
    }

    void FinishOwnMessage()
    {
        CloseOwnMessage();
        messageId = null;
        messageTyped = false;
        messageHold = 0f;
        messageVisibleTime = -1d;
    }

    bool IsLiveVisible() => characterWindow != null && characterWindow.IsOpen && !characterWindow.IsMinimized
        && !characterWindow.IsOpening && !characterWindow.IsClosing && characterWindow.gameObject.activeInHierarchy;

    bool EnsureLiveVisible()
    {
        if (IsLiveVisible()) return true;
        if (characterWindow != null && !characterWindow.IsOpening && !characterWindow.IsClosing)
            OS_Handle.Instance?.OpenCharacterWindowForReconnect(.15f);
        return false;
    }

    void CreateVisuals()
    {
        var go = new GameObject("ClawStoryPresentation", typeof(RectTransform), typeof(CanvasGroup));
        go.layer = presentationLayer.gameObject.layer;
        visualRoot = (RectTransform)go.transform;
        // The doll must pass under Live's actual window, rather than fading on
        // top of it. Use the same window stack so its opaque panel occludes us.
        var windowStack = characterWindow != null ? characterWindow.transform.parent as RectTransform : null;
        visualRoot.SetParent(windowStack != null ? windowStack : presentationLayer, false);
        visualRoot.anchorMin = Vector2.zero; visualRoot.anchorMax = Vector2.one;
        visualRoot.offsetMin = visualRoot.offsetMax = Vector2.zero;
        var group = go.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;
        cursorImage = NewImage("CharacterRedPointer");
        cursorImage.rectTransform.pivot = cursorHotspot;
        cursorImage.color = cursorColor;
        SetPressed(false);
        KeepPickupBehindLive();
    }

    Image NewImage(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = visualRoot.gameObject.layer;
        var image = go.GetComponent<Image>();
        image.rectTransform.SetParent(visualRoot, false);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, .5f);
        image.raycastTarget = false; image.preserveAspect = true;
        return image;
    }

    void TickPickup(float delta)
    {
        if (cursorImage == null) return;
        KeepPickupBehindLive();
        delta = Mathf.Min(delta, .1f);
        phaseElapsed += delta;
        Vector2 origin = PrizePoint();
        Vector2 live = LivePoint();
        switch (phase)
        {
            case PickupPhase.Approaching:
                PlaceCursor(Curve(live + new Vector2(16, -6), origin, phaseElapsed / .7f));
                if (phaseElapsed >= .7f) { phase = PickupPhase.Pressing; phaseElapsed = 0f; SetPressed(true); }
                break;
            case PickupPhase.Pressing:
                PlaceCursor(origin);
                if (phaseElapsed >= .18f)
                {
                    CreateDraggedPrize();
                    PlaceCursor(origin);
                    pickupOrigin = pickupPoint = origin;
                    pickupTarget = LivePoint();
                    pickupTargetVelocity = pickupFollowVelocity = Vector2.zero;
                    pickupHiddenHold = 0f;
                    phase = PickupPhase.Dragging; phaseElapsed = 0f;
                }
                break;
            case PickupPhase.Dragging:
                // Go straight from the outlet into Live. Recompute its current
                // interior every frame so dragging the window moves the handoff.
                if (delta > 0f)
                    pickupTarget = Vector2.SmoothDamp(pickupTarget, live, ref pickupTargetVelocity,
                        .08f, Mathf.Infinity, delta);
                pickupPoint = Curve(pickupOrigin, pickupTarget, phaseElapsed / 1.2f);
                PlaceCursor(pickupPoint);
                if (phaseElapsed >= 1.2f)
                {
                    phase = PickupPhase.Releasing; phaseElapsed = 0f;
                    pickupFollowVelocity = Vector2.zero;
                    SetPressed(false);
                }
                break;
            case PickupPhase.Releasing:
                // A moved Live window may still be ahead of the doll. Follow it
                // continuously, and only finish after the entire doll is hidden.
                if (delta > 0f)
                    pickupPoint = Vector2.SmoothDamp(pickupPoint, live, ref pickupFollowVelocity,
                        .08f, Mathf.Infinity, delta);
                PlaceCursor(pickupPoint);
                bool hidden = PickupIsInsideLive() && (pickupPoint - live).sqrMagnitude <= 1f;
                pickupHiddenHold = hidden ? pickupHiddenHold + delta : 0f;
                if (pickupHiddenHold >= .22f)
                {
                    game.CompleteCharacterPickup(deliveredPrizeIndex);
                    DestroyVisuals();
                    deliveryObserved = false; deliveredPrizeIndex = -1; outletElapsed = 0f;
                    phase = PickupPhase.Waiting;
                    // The next request is selected on the following Update,
                    // after the old prize and the complete drag visual are gone.
                    observedSave.chapter02ClawFirstPrizeTaken = true;
                    // A 2.5-second wait plus the handoff is slightly shorter than
                    // the normal five-second message hold. End this acknowledgement
                    // here so it cannot hold the next request behind its protection.
                    observedSave.chapter02ClawThanksShown = true;
                    if (messageId == ThanksEntryId) FinishOwnMessage();
                }
                break;
        }
    }

    void CreateDraggedPrize()
    {
        game.SetPrizeHovered(false);
        originalPrize = game.OutletPrize.GetComponent<Image>() ?? game.OutletPrize.GetComponentInChildren<Image>();
        if (originalPrize == null) return;
        originalPrizeEnabled = originalPrize.enabled;
        draggedPrize = NewImage("CharacterDraggedPrize");
        draggedPrize.sprite = originalPrize.sprite;
        draggedPrize.color = originalPrize.color;
        var rect = originalPrize.rectTransform;
        Vector2 min = LocalPoint(rect, rect.rect.min), max = LocalPoint(rect, rect.rect.max);
        draggedPrize.rectTransform.sizeDelta = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
        draggedPrize.transform.SetAsFirstSibling();
        originalPrize.enabled = false;
    }

    void SetPressed(bool pressed)
    {
        cursorImage.sprite = pressed && pressedCursor != null ? pressedCursor : normalCursor;
        cursorImage.rectTransform.sizeDelta = cursorImage.sprite.rect.size * cursorScale;
    }

    void PlaceCursor(Vector2 point)
    {
        cursorImage.rectTransform.anchoredPosition = point;
        if (draggedPrize != null)
            draggedPrize.rectTransform.anchoredPosition = point - new Vector2(0f, draggedPrize.rectTransform.rect.height * .2f);
    }

    Vector2 PrizePoint()
    {
        var rect = game.OutletPrize;
        return LocalPoint(rect, rect.rect.center + new Vector2(0f, rect.rect.height * .2f));
    }

    Vector2 LivePoint()
    {
        Rect bounds = LiveOcclusionBounds();
        Vector2 prize = draggedPrize != null ? draggedPrize.rectTransform.rect.size : Vector2.zero;
        Vector2 cursor = cursorImage != null ? cursorImage.rectTransform.rect.size : Vector2.zero;
        // PlaceCursor carries the prize 20% below its pointer. Clamp their whole
        // combined silhouette inside the panel, around Character's torso.
        float left = Mathf.Max(prize.x * .5f, cursor.x * cursorHotspot.x);
        float right = Mathf.Max(prize.x * .5f, cursor.x * (1f - cursorHotspot.x));
        float bottom = Mathf.Max(prize.y * .7f, cursor.y * cursorHotspot.y);
        float top = Mathf.Max(prize.y * .3f, cursor.y * (1f - cursorHotspot.y));
        return new Vector2(
            Mathf.Clamp(bounds.center.x, bounds.xMin + left, bounds.xMax - right),
            Mathf.Clamp(bounds.yMin + bounds.height * .42f, bounds.yMin + bottom, bounds.yMax - top));
    }

    Rect LiveOcclusionBounds()
    {
        var rect = characterWindow.WindowRect;
        Vector2 min = LocalPoint(rect, rect.rect.min);
        Vector2 max = LocalPoint(rect, rect.rect.max);
        return Rect.MinMaxRect(min.x + 6f, min.y + 6f, max.x - 6f, max.y - 20f);
    }

    bool PickupIsInsideLive()
    {
        Rect bounds = LiveOcclusionBounds();
        return ImageIsInside(cursorImage, bounds) && ImageIsInside(draggedPrize, bounds);
    }

    bool ImageIsInside(Image image, Rect bounds)
    {
        if (image == null) return true;
        var rect = image.rectTransform;
        return bounds.Contains(LocalPoint(rect, rect.rect.min))
            && bounds.Contains(LocalPoint(rect, rect.rect.max));
    }

    void KeepPickupBehindLive()
    {
        if (visualRoot == null || characterWindow == null
            || visualRoot.parent != characterWindow.transform.parent) return;
        // Bring the performance above ordinary windows but leave Live in front.
        // Only our non-interactive images move; the real/virtual cursor is untouched.
        if (characterWindow.transform.GetSiblingIndex() < visualRoot.parent.childCount - 1)
            characterWindow.transform.SetAsLastSibling();
        int slot = characterWindow.transform.GetSiblingIndex() - 1;
        if (visualRoot.GetSiblingIndex() != slot)
            visualRoot.SetSiblingIndex(Mathf.Max(0, slot));
    }

    Vector2 LocalPoint(RectTransform source, Vector2 point)
    {
        var sourceCanvas = source.GetComponentInParent<Canvas>();
        var targetCanvas = visualRoot.GetComponentInParent<Canvas>();
        var sourceCamera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? sourceCanvas.worldCamera : null;
        var targetCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? targetCanvas.worldCamera : null;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(sourceCamera, source.TransformPoint(point));
        RectTransformUtility.ScreenPointToLocalPointInRectangle(visualRoot, screen, targetCamera, out Vector2 local);
        return local;
    }

    static Vector2 Curve(Vector2 from, Vector2 to, float progress)
    {
        float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        Vector2 bend = new Vector2(-(to - from).y, (to - from).x).normalized * 12f;
        return Vector2.Lerp(from, to, u) + bend * (4f * u * (1f - u));
    }

    void SetVisualsVisible(bool visible)
    {
        if (visualRoot != null) visualRoot.gameObject.SetActive(visible);
        if (originalPrize != null) originalPrize.enabled = visible ? false : originalPrizeEnabled;
    }

    void RestoreOriginalPrize()
    {
        if (originalPrize != null) originalPrize.enabled = originalPrizeEnabled;
        originalPrize = null;
    }

    void DestroyVisuals()
    {
        RestoreOriginalPrize();
        if (visualRoot != null)
        {
            visualRoot.gameObject.SetActive(false);
            Destroy(visualRoot.gameObject);
        }
        visualRoot = null; cursorImage = null; draggedPrize = null;
        pickupTargetVelocity = pickupFollowVelocity = Vector2.zero;
        pickupHiddenHold = 0f;
    }

    public void ResetPresentation()
    {
        PauseMessage();
        DestroyVisuals();
        game = null;
        messageId = null; messageTyped = false; messageHold = 0f; messageVisibleTime = -1d;
        deliveryObserved = false; deliveredPrizeIndex = -1; outletElapsed = phaseElapsed = 0f;
        phase = PickupPhase.Waiting; lastVisibleTime = -1d;
    }

    static bool IsOwnEntry(string id) => id == IntroEntryId || id == ThanksEntryId || id == NextEntryId;
}
