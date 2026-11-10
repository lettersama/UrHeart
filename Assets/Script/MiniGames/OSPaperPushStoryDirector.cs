using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// The small persisted FIFO feeds the shared Live system only when its slot is free.
// This component belongs to the OS root; closing the game does not discard reactions.
[DisallowMultipleComponent]
public sealed class OSPaperPushStoryDirector : MonoBehaviour
{
    [SerializeField] OSLinkMessageController linkMessages;
    [SerializeField] OSWindowUnit window;
    [SerializeField] RectTransform closeButton;
    [SerializeField] DesktopProcessUnit desktopIcon;
    [SerializeField] RectTransform sealSticker;
    [SerializeField] RectTransform presentationLayer;
    [SerializeField] OSPaperPushWindowController game;
    [SerializeField] RectTransform startButton;
    bool chapter02SealHidden;
    Graphic[] sealPaletteGraphics;
    Color[] sealOriginalColors;
    bool chapter02SealPalette;
    public RectTransform SealSticker => sealSticker;
    public DesktopProcessUnit DesktopIcon => desktopIcon;
    public OSWindowUnit Window => window;
    public RectTransform CloseButton => closeButton;
    public RectTransform PresentationLayer => presentationLayer;
    public void SetChapter02SealVisible(bool visible)
    {
        ApplyChapter02SealPalette(GameDataManager.Instance?.CurrentChapterIndex == 2);
        chapter02SealHidden = !visible;
        if (sealSticker != null) sealSticker.gameObject.SetActive(visible);
    }

    void ApplyChapter02SealPalette(bool monochrome)
    {
        if (sealPaletteGraphics == null)
        {
            if (!monochrome || sealSticker == null) return;
            // Only the original sticker is cached. The torn copy lives under
            // Windows and keeps its independent animation and palette.
            sealPaletteGraphics = sealSticker.GetComponentsInChildren<Graphic>(true);
            sealOriginalColors = new Color[sealPaletteGraphics.Length];
            for (int i = 0; i < sealPaletteGraphics.Length; i++)
                sealOriginalColors[i] = sealPaletteGraphics[i].color;
        }
        if (chapter02SealPalette == monochrome) return;
        for (int i = 0; i < sealPaletteGraphics.Length; i++)
        {
            if (sealPaletteGraphics[i] == null) continue;
            Color original = sealOriginalColors[i];
            Color tint = monochrome
                ? original.grayscale >= .5f ? (Color)new Color32(0x87, 0x82, 0x7e, 255) : Color.black
                : original;
            tint.a = original.a;
            sealPaletteGraphics[i].color = tint;
        }
        chapter02SealPalette = monochrome;
    }
    public bool IsTakingOver { get; private set; }
    public bool IsWaitingForMatchComment
    {
        get
        {
            var s = GameDataManager.Instance?.OSEnergyLoopState;
            return GameDataManager.Instance?.CurrentChapterIndex == 1 && s != null
                && (s.paperPushFirstRematchPending || s.paperPushReactionQueue.Exists(id =>
                    id.EndsWith("_defeat", System.StringComparison.Ordinal)
                    || id.EndsWith("_reopen", System.StringComparison.Ordinal)
                    || id.EndsWith("_last_chance", System.StringComparison.Ordinal)));
        }
    }
    // Chapter 2 picks the game up as if chapter 1 sealed it. Players who never
    // found it, or stopped before the third win, finish it between the bug
    // repair and the confirmation that opens chapter 2.
    public bool HoldsChapter01Ending
    {
        get
        {
            var data = GameDataManager.Instance;
            var s = data?.OSEnergyLoopState;
            return data?.CurrentChapterIndex == 1 && s != null && (!s.paperPushSealed || IsTakingOver)
                && data.OSProcessData?.chapter01Ending?.phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation;
        }
    }
    const string GateEntryPrefix = "paper_push_ch01_0_gate_";
    const float GateIdleSeconds = 8f;
    float gateIdleSince = -1f;
    float nextDispatch;
    Coroutine takeover;
    Image dragImage;
    OSEnergyLoopSaveData observedSave;
    bool wasMinimized;

    void OnEnable()
    {
        ApplyChapter02SealPalette(GameDataManager.Instance?.CurrentChapterIndex == 2);
        if (window == null) return;
        window.OpenStarted += OnOpened;window.Closed += OnClosed;window.LayoutChanged += OnLayoutChanged;
        wasMinimized = window.IsMinimized;
    }

    void OnOpened(OSWindowUnit source)
    {
        if (OSSessionPersistence.IsRestoring) return;
        OS_Handle.Instance?.SetDesktopProcessNew(OSDesktopProcessKeys.PaperPush, false);
        desktopIcon?.SetNewState(false, false);
        var data = GameDataManager.Instance;
        if (data?.CurrentChapterIndex != 1) return;
        var s = data.OSEnergyLoopState;
        if (s.paperPushFinalInvitationPending && s.chapter01PaperPushWins == 2)
        { s.paperPushFinalInvitationPending = false;Queue(2, "last_chance"); }
    }

    void OnClosed(OSWindowUnit source)
    {
        var data = GameDataManager.Instance;
        if (IsTakingOver || data?.CurrentChapterIndex != 1) return;
        var s = data.OSEnergyLoopState;
        if (s.paperPushMatch.active) return;
        if (s.chapter01PaperPushWins == 1 && !s.paperPushFirstRematchDone)
        { s.paperPushFirstRematchPending = true;Queue(1, "reopen"); }
        else OfferFinalMatch(s);
    }

    void OnLayoutChanged(OSWindowUnit source)
    {
        bool minimized = source.IsMinimized;
        if (OSSessionPersistence.IsRestoring) { wasMinimized = minimized;return; }
        if (minimized && !wasMinimized && !IsTakingOver && GameDataManager.Instance?.CurrentChapterIndex == 1)
            OfferFinalMatch(GameDataManager.Instance.OSEnergyLoopState);
        if (!minimized && wasMinimized && source.IsOpen) OnOpened(source);
        wasMinimized = minimized;
    }

    void OfferFinalMatch(OSEnergyLoopSaveData s)
    {
        if (s.chapter01PaperPushWins != 2 || s.paperPushMatch.active || s.paperPushFinalInvitationPending
            || s.paperPushReactionHistory.Contains(EntryId(2, "last_chance"))) return;
        s.paperPushFinalInvitationPending = true;
        OS_Handle.Instance?.SetDesktopProcessNew(OSDesktopProcessKeys.PaperPush, true);
    }

    public static string EntryId(int stage, string reaction) => "paper_push_ch01_" + stage + "_" + reaction;
    public static string Signal(int stage, string reaction) => "paper_push.story." + stage + "." + reaction;

    public void ObserveRound(int chapter, int round, int winner, float playerX, float characterX, bool resolved)
    {
        var s = GameDataManager.Instance?.OSEnergyLoopState;
        if (chapter != 1 || s == null || s.chapter01PaperPushWins >= 3) return;
        int stage = s.chapter01PaperPushWins + 1;
        if (!resolved)
        {
            if (winner > 0 && (stage == 1 || round >= 2)) Queue(stage, stage == 1 ? "counter" : "setback");
            return;
        }
        float center = (playerX + characterX) * .5f;
        if (center < -25f) s.paperPushMatch.characterHadLead = true;
        if (stage < 3 && playerX <= -68f) Queue(stage, "near_win");
        if (stage < 3 && s.paperPushMatch.characterHadLead && center >= 7f) Queue(stage, "reversal");
        if (characterX >= 72f) Queue(stage, "near_loss");
    }

    public void OnChapter01Victory()
    {
        var s = GameDataManager.Instance?.OSEnergyLoopState;
        if (GameDataManager.Instance?.CurrentChapterIndex != 1 || s == null) return;
        Queue(s.chapter01PaperPushWins, "defeat");
        if (s.chapter01PaperPushWins == 3)
        {
            // Own the cursor while Live is still talking; choreography waits below.
            observedSave = s;HoldCursor();
        }
    }

    void Queue(int stage, string reaction)
    {
        var s = GameDataManager.Instance.OSEnergyLoopState;
        string id = EntryId(stage, reaction);
        if (s.paperPushReactionHistory.Contains(id)) return;
        s.paperPushReactionHistory.Add(id);
        s.paperPushReactionQueue.Add(id);
    }

    void Update()
    {
        var data = GameDataManager.Instance;
        if (data == null) return;
        ApplyChapter02SealPalette(data.CurrentChapterIndex == 2);
        var s = data.OSEnergyLoopState;
        if (observedSave != s)
        {
            CancelTakeover();observedSave = s;nextDispatch = 0f;
        }
        if (GameSessionFlow.Instance?.IsBusy == true || OSSessionPersistence.IsRestoring) return;
        OSStoryRecordUnlocks.Refresh(data, IsTakingOver);
        if (data.CurrentChapterIndex != 1)
        {
            if (IsTakingOver) CancelTakeover();
            bool locked = data.CurrentChapterIndex == 2 && s.chapter02PaperPushStep < 2
                && OS_Handle.Instance?.Chapter02PaperPush?.UnsealIconReady != true;
            if (sealSticker != null) sealSticker.gameObject.SetActive(locked
                && (s.chapter02PaperPushStep == 0 || !chapter02SealHidden));
            if (desktopIcon != null && desktopIcon.IsLocked != locked)
                OS_Handle.Instance?.SetDesktopProcessLocked(OSDesktopProcessKeys.PaperPush, locked);
            if (locked && s.chapter02PaperPushStep == 0 && window != null && window.IsOpen)
                window.CloseFromScript();
            return;
        }
        if (sealSticker != null && !IsTakingOver) sealSticker.gameObject.SetActive(s.paperPushSealed);
        if (s.paperPushSealed)
        {
            if (desktopIcon != null && !desktopIcon.IsLocked) OS_Handle.Instance?.SetDesktopProcessLocked(OSDesktopProcessKeys.PaperPush, true);
            if (window != null && window.IsOpen) window.Close();
            return;
        }
        if (OS_Handle.Instance?.IsOSVisible != true)
        { if (IsTakingOver) CancelTakeover();return; }
        if (s.paperPushSealPending) { Queue(3, "defeat");HoldCursor(); }
        bool gate = HoldsChapter01Ending;
        if (gate) QueueGate(data, s);
        if (takeover != null || Time.unscaledTime < nextDispatch) return;
        nextDispatch = Time.unscaledTime + .2f;
        var live = data.OSCharacterInteractionState;
        while (s.paperPushReactionQueue.Count > 0)
        {
            string id = s.paperPushReactionQueue[0];
            // Consumed is set when text starts. Keep it until that presentation ends,
            // including a resumed global presentation after loading a save.
            if (live.consumedGlobalEntryIds.Contains(id) && live.presentingGlobalEntryId != id
                && live.pendingGlobalEntryId != id && linkMessages?.DisplayedEntryId != id)
            { s.paperPushReactionQueue.RemoveAt(0);continue; }
            if (linkMessages == null || linkMessages.IsPresenting || !string.IsNullOrEmpty(live.presentingGlobalEntryId)
                || !string.IsNullOrEmpty(live.pendingGlobalEntryId)) return;
            string suffix = id.Substring("paper_push_ch01_".Length);
            int divider = suffix.IndexOf('_');
            string signal = "paper_push.story." + suffix.Substring(0, divider) + "." + suffix.Substring(divider + 1);
            linkMessages.NotifyGlobalExternalSignal(signal);
            return;
        }
        bool quiet = linkMessages?.IsPresenting != true
            && string.IsNullOrEmpty(live.pendingGlobalEntryId) && string.IsNullOrEmpty(live.presentingGlobalEntryId);
        if (quiet && HoldCursorIfPending(s))
            takeover = StartCoroutine(s.paperPushSealPending ? TakeOver(s) : ReopenMatch(s));
        else if (gate && GateIdle(quiet, s) && HoldCursor())
            takeover = StartCoroutine(OpenForGate());
    }

    // One invitation per save, worded for how far the player got in chapter 1.
    void QueueGate(GameDataManager data, OSEnergyLoopSaveData s)
    {
        if (s.paperPushSealPending || s.paperPushReactionHistory.Exists(id => id.StartsWith(GateEntryPrefix, System.StringComparison.Ordinal)))
            return;
        string reaction = !data.IsPaperPushUnlocked ? "gate_found"
            : s.chapter01PaperPushWins == 0 ? "gate_invite" : "gate_unfinished";
        if (!data.IsPaperPushUnlocked)
        { data.DiscoverPaperPush();data.UnlockPaperPush(); }
        Queue(0, reaction);
    }

    // Counts only while Live is quiet and the game is out of sight; Live opens it herself after that.
    bool GateIdle(bool quiet, OSEnergyLoopSaveData s)
    {
        bool visible = window != null && window.IsOpen && !window.IsMinimized;
        if (!quiet || visible || s.paperPushMatch.active || s.paperPushReactionQueue.Count > 0)
        { gateIdleSince = -1f;return false; }
        if (gateIdleSince < 0f) gateIdleSince = Time.unscaledTime;
        return Time.unscaledTime - gateIdleSince >= GateIdleSeconds;
    }

    IEnumerator OpenForGate()
    {
        var cursor = CursorHandle.Instance;
        gateIdleSince = -1f;
        if (desktopIcon == null) { CancelTakeover();yield break; }
        if (OS_Handle.Instance.RevealPaperPushForSeal()) yield return new WaitForSecondsRealtime(.35f);
        yield return MoveCursor(ScreenCenter(desktopIcon.ProcessIcon.rectTransform), .65f);
        cursor.SetScriptPressed(this, true);
        yield return new WaitForSecondsRealtime(.13f);
        bool opened = OS_Handle.Instance.OpenPaperPushWindow();
        cursor.SetScriptPressed(this, false);
        OS_Handle.Instance.RestorePaperPushSealFolder();
        if (opened) OS_Handle.Instance.PlayInteractionFeedback();
        cursor.ReleaseScriptControlAtDisplayedPosition(this);
        IsTakingOver = false;takeover = null;
    }

    bool HoldCursorIfPending(OSEnergyLoopSaveData s)
        => (s.paperPushSealPending || s.paperPushFirstRematchPending) && HoldCursor();

    bool HoldCursor()
    {
        var cursor = CursorHandle.Instance;
        if (cursor == null || OS_Handle.Instance?.IsOSVisible != true) return false;
        if (IsTakingOver && cursor.IsScriptControlledBy(this)) return true;
        if (OS_Handle.Instance?.IsSystemInputLocked == true || !cursor.AcquireScriptControl(this)) return false;
        IsTakingOver = true;
        cursor.SetScriptAppearance(this, 1.65f, new Color32(216, 52, 58, 255));
        return true;
    }

    IEnumerator ReopenMatch(OSEnergyLoopSaveData state)
    {
        var cursor = CursorHandle.Instance;
        if (desktopIcon == null || game == null || startButton == null) { CancelTakeover();yield break; }
        if (OS_Handle.Instance.RevealPaperPushForSeal()) yield return new WaitForSecondsRealtime(.35f);
        yield return MoveCursor(ScreenCenter(desktopIcon.ProcessIcon.rectTransform), .65f);
        cursor.SetScriptPressed(this, true);
        yield return new WaitForSecondsRealtime(.13f);
        bool opened = OS_Handle.Instance.OpenPaperPushWindow();
        cursor.SetScriptPressed(this, false);
        OS_Handle.Instance.RestorePaperPushSealFolder();
        if (!opened) { CancelTakeover();yield break; }
        OS_Handle.Instance.PlayInteractionFeedback();
        yield return new WaitForSecondsRealtime(.5f);
        yield return MoveCursor(ScreenCenter(startButton), .55f);
        cursor.SetScriptPressed(this, true);
        yield return new WaitForSecondsRealtime(.13f);
        if (game.BeginScriptedRematch(this))
        { state.paperPushFirstRematchDone = true;state.paperPushFirstRematchPending = false; }
        cursor.SetScriptPressed(this, false);
        cursor.ReleaseScriptControlAtDisplayedPosition(this);
        IsTakingOver = false;takeover = null;
    }

    IEnumerator TakeOver(OSEnergyLoopSaveData state)
    {
        var cursor = CursorHandle.Instance;
        if (cursor == null || desktopIcon == null || presentationLayer == null || !HoldCursor())
        { CancelTakeover();yield break; }
        OS_Handle.Instance.SetDesktopProcessNew(OSDesktopProcessKeys.PaperPush, false);
        desktopIcon.SetNewState(false, false);
        if (window != null && window.IsOpen)
        {
            window.RestoreAndBringToFront();
            yield return MoveCursor(ScreenCenter(closeButton != null ? closeButton : window.WindowRect), .6f);
            cursor.SetScriptPressed(this, true);
            yield return new WaitForSecondsRealtime(.13f);
            window.CloseAnimated(.25f);
            cursor.SetScriptPressed(this, false);
            OS_Handle.Instance?.PlayInteractionFeedback();
            yield return new WaitForSecondsRealtime(.3f);
        }
        if (OS_Handle.Instance.RevealPaperPushForSeal()) yield return new WaitForSecondsRealtime(.35f);
        Vector2 start = ScreenCenter(desktopIcon.ProcessIcon.rectTransform);
        yield return MoveCursor(start, .65f);
        cursor.SetScriptPressed(this, true);
        CreateDragImage();
        desktopIcon.SetDragGhosted(true, 0f);
        OS_Handle.Instance.RestorePaperPushSealFolder();
        Vector2 destination;
        if (OS_Handle.Instance.PlacePaperPushForSeal(out destination))
            yield return MoveCursor(destination, 1.05f, true);
        else
        {
            // Keep the operation pending if the grid is not ready, instead of losing its placement.
            CancelTakeover();yield break;
        }
        cursor.SetScriptPressed(this, false);
        desktopIcon.SetDragGhosted(false);
        DestroyDragImage();
        OS_Handle.Instance.SetDesktopProcessLocked(OSDesktopProcessKeys.PaperPush, true);
        if (sealSticker != null)
        {
            sealSticker.gameObject.SetActive(true);
            sealSticker.localScale = Vector3.one * 1.65f;
            sealSticker.DOScale(1f, .3f).SetEase(Ease.OutBack).SetUpdate(true);
        }
        OS_Handle.Instance?.PlayInteractionFeedback();
        yield return new WaitForSecondsRealtime(.5f);
        cursor.ReleaseScriptControlAtDisplayedPosition(this);
        // Commit only after the seal settles and control returns. An interrupted
        // stamp retains its pending receipt and resumes before giving the record.
        state.paperPushSealed = true;state.paperPushSealPending = false;state.paperPushMatch.active = false;
        IsTakingOver = false;takeover = null;
        OSStoryRecordUnlocks.Refresh(GameDataManager.Instance);
    }

    IEnumerator MoveCursor(Vector2 target, float duration, bool dragging = false)
    {
        var cursor = CursorHandle.Instance;
        Vector2 start = cursor.DisplayedScreenPosition;
        Vector2 delta = target - start;
        Vector2 bend = new Vector2(-delta.y, delta.x).normalized * Mathf.Min(50f, delta.magnitude * .16f);
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / duration), v = 1f - u;
            Vector2 position = v * v * start + 2f * v * u * ((start + target) * .5f + bend) + u * u * target;
            cursor.SetScriptScreenPosition(this, position);
            if (dragging) PlaceDragImage(position);
            yield return null;
        }
        cursor.SetScriptScreenPosition(this, target);
        if (dragging) PlaceDragImage(target);
    }

    void CreateDragImage()
    {
        var source = desktopIcon.ProcessIcon;
        var go = new GameObject("PaperPushDraggedIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = presentationLayer.gameObject.layer;
        dragImage = go.GetComponent<Image>();dragImage.raycastTarget = false;
        dragImage.sprite = source.sprite;dragImage.color = source.color;dragImage.preserveAspect = true;
        dragImage.rectTransform.SetParent(presentationLayer, false);
        dragImage.rectTransform.sizeDelta = source.rectTransform.rect.size;
        presentationLayer.SetAsLastSibling();PlaceDragImage(CursorHandle.Instance.DisplayedScreenPosition);
    }

    void PlaceDragImage(Vector2 screen)
    {
        if (dragImage == null) return;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(presentationLayer, screen, CameraFor(presentationLayer), out Vector3 world);
        dragImage.rectTransform.position = world;
    }

    static Camera CameraFor(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    }
    static Vector2 ScreenCenter(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(CameraFor(rect), rect.TransformPoint(rect.rect.center));
    void DestroyDragImage() { if (dragImage != null) Destroy(dragImage.gameObject);dragImage = null; }
    void CancelTakeover()
    {
        if (takeover != null) StopCoroutine(takeover);
        takeover = null;IsTakingOver = false;
        CursorHandle.Instance?.ReleaseScriptControl(this);
        OS_Handle.Instance?.RestorePaperPushSealFolder();
        desktopIcon?.SetDragGhosted(false);DestroyDragImage();
        if (sealSticker != null) { sealSticker.DOKill();sealSticker.localScale = Vector3.one; }
    }
    void OnDisable()
    {
        if (window != null)
        { window.OpenStarted -= OnOpened;window.Closed -= OnClosed;window.LayoutChanged -= OnLayoutChanged; }
        CancelTakeover();
        ApplyChapter02SealPalette(false);
    }
}
