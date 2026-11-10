using MrTGame.Core;
using TMPro;
using UnityEngine;

/// <summary>
/// Owns the chapter-3 Home window's lifetime and view save state. Story progression
/// remains with Memory; this component only enables the existing room frontend.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(OSMemoryWindowController))]
public sealed class OSChapter03HomeController : MonoBehaviour
{
    [SerializeField] OSMemoryWindowController memory;
    [SerializeField] OSWindowUnit homeWindow;
    [SerializeField] OSHomeView homeView;
    [SerializeField] OSHomeItemBar homeItems;
    [SerializeField] TMP_Text windowTitle;

    OSWindowUnit memoryWindow;
    OSHomeView subscribedView;
    OSMemoryProcessSaveData boundState;
    OSChapter03StoryDirector story;
    bool viewPrepared;
    bool applyingState;
    bool chapter04OutroHeld;

    public OSWindowUnit HomeWindow => homeWindow;
    public OSHomeView View => homeView;
    public OSHomeItemBar Items => homeItems;
    public bool IsRestoringView => applyingState;
    public bool IsChapter04OutroHeld => chapter04OutroHeld;

    /// <summary>The chapter handoff owns the existing window until it releases this hold.</summary>
    public void SetChapter04OutroHeld(bool held)
    {
        chapter04OutroHeld = held;
        if (!held) return;
        SetInputEnabled(false);
        homeView?.StopWalking();
        if (homeView != null) homeView.MotionPaused = true;
    }

    /// <summary>Restore the saved room for a resumed fade without the ordinary auto-open loop.</summary>
    public bool PrepareChapter04Outro(bool showWindow)
    {
        ResolveReferences();
        ObserveSave(GameDataManager.Instance);
        SetChapter04OutroHeld(true);
        if (homeWindow == null || homeView == null) return false;
        if (showWindow)
        {
            if (!PrepareView()) return false;
            ConfigureHomeLayout();
            if (!homeWindow.IsOpen) homeWindow.OpenStandalone(0f);
            else if (homeWindow.IsMinimized) homeWindow.RestoreAndBringToFront();
            homeWindow.CompleteOpeningFromScript();
            ConfigureHomeLayout();
            homeView.MotionPaused = true;
            CaptureViewState();
        }
        return true;
    }

    void OnEnable()
    {
        GameEvents.LanguageChanged += HandleLanguageChanged;
        ResolveReferences();
        SetInputEnabled(false);
    }

    void OnDisable()
    {
        Suspend();
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        UnbindView();
    }

    void OnDestroy() => UnbindView();

    /// <summary>Called after Memory has chosen its layout, before its opening tween.</summary>
    public void PrepareForOpen()
    {
        ResolveReferences();
        SetInputEnabled(false);
        var data = GameDataManager.Instance;
        ObserveSave(data);
        if (chapter04OutroHeld) return;
        if (data == null || !data.IsChapter03HomeActive)
        {
            Suspend();
            return;
        }

        PrepareView();
    }

    /// <summary>Called by Memory's Update; does not advance a memory node.</summary>
    public void RefreshPresentation()
    {
        ResolveReferences();
        var data = GameDataManager.Instance;
        ObserveSave(data);
        if (chapter04OutroHeld)
        {
            SetInputEnabled(false);
            if (homeView != null) homeView.MotionPaused = true;
            return;
        }
        if (data == null || !data.IsChapter03HomeActive || !IsWindowVisible(memoryWindow))
        {
            Suspend();
            return;
        }

        if (memoryWindow.IsOpening)
        {
            SetInputEnabled(false);
            return;
        }

        if (!PrepareView())
        {
            SetInputEnabled(false);
            return;
        }

        ConfigureHomeLayout();

        if (!homeWindow.IsOpen)
            homeWindow.OpenStandalone();
        else if (homeWindow.IsMinimized)
            homeWindow.RestoreAndBringToFront();

        homeView.MotionPaused = story != null && story.IsActive && !story.IsAvailable;
        SetInputEnabled(CanUseView(data));
    }

    /// <summary>Hide with the parent, keeping the current room and tile in its save.</summary>
    public void Suspend()
    {
        SetInputEnabled(false);
        homeView?.StopWalking();
        if (homeView != null) homeView.MotionPaused = true;
        CaptureViewState();
        viewPrepared = false;
        if (homeWindow != null && homeWindow.IsOpen)
            homeWindow.CloseSilently();
    }

    /// <summary>Discard runtime bindings without copying an old view into a new save.</summary>
    public void ResetRuntime()
    {
        chapter04OutroHeld = false;
        SetInputEnabled(false);
        homeView?.StopWalking();
        viewPrepared = false;
        boundState = null;
        homeWindow?.PrepareRuntimeClosed();
    }

    public void CaptureViewState()
    {
        var data = GameDataManager.Instance;
        // ReplaceSaveData can happen before this component gets its next Update.
        // A close/disable callback from the old session must not write to the new one.
        if (applyingState || !viewPrepared || boundState == null || data == null
            || !ReferenceEquals(boundState, data.OSProcessData.memory)
            || homeView == null || homeView.CurrentRoom == null)
            return;

        boundState.chapter03HomeView = homeView.CaptureState();
        boundState.chapter03HomeInitialized = true;
    }

    void ResolveReferences()
    {
        if (memory == null) memory = GetComponent<OSMemoryWindowController>();
        if (memoryWindow == null && memory != null)
            memoryWindow = memory.GetComponent<OSWindowUnit>();
        if (homeWindow == null && homeView != null)
            homeWindow = homeView.GetComponentInParent<OSWindowUnit>();
        if (homeView == null && homeWindow != null)
            homeView = homeWindow.GetComponentInChildren<OSHomeView>(true);
        if (homeItems == null && homeWindow != null)
            homeItems = homeWindow.GetComponentInChildren<OSHomeItemBar>(true);
        if (story == null && OS_Handle.Instance != null)
            story = OS_Handle.Instance.GetComponent<OSChapter03StoryDirector>();

        if (!isActiveAndEnabled || subscribedView == homeView) return;
        UnbindView();
        subscribedView = homeView;
        if (subscribedView != null)
        {
            subscribedView.RoomEntered += HandleRoomEntered;
            subscribedView.Stepped += HandleStepped;
        }
    }

    void UnbindView()
    {
        if (subscribedView == null) return;
        subscribedView.RoomEntered -= HandleRoomEntered;
        subscribedView.Stepped -= HandleStepped;
        subscribedView = null;
    }

    void ObserveSave(GameDataManager data)
    {
        var next = data != null ? data.OSProcessData.memory : null;
        if (ReferenceEquals(boundState, next)) return;
        chapter04OutroHeld = false;
        SetInputEnabled(false);
        homeView?.StopWalking();
        viewPrepared = false;
        boundState = next;
        if (homeWindow != null && homeWindow.IsOpen)
            homeWindow.CloseSilently();
    }

    bool PrepareView()
    {
        if (homeWindow == null || homeView == null || boundState == null) return false;
        if (viewPrepared) return true;

        ConfigureHomeLayout();

        SetInputEnabled(false);
        applyingState = true;
        bool restored = false;
        try
        {
            // Rebuild on reopen to discard an interrupted walk or doorway fade.
            // The frontend's State deliberately contains only room and positions.
            homeView.Load(null);
            if (boundState.chapter03HomeInitialized
                && homeView.Layout.FindRoom(boundState.chapter03HomeView.room) != null)
                restored = homeView.RestoreState(boundState.chapter03HomeView);

            if (!restored)
            {
                var layout = homeView.Layout;
                homeView.SetDongVisible(true);
                restored = homeView.EnterRoom(layout.firstRoom, layout.firstTile, layout.firstDongTile);
            }
            if (restored && story != null && story.IsActive) story.RebuildHomeState();
            else homeItems?.Clear();
        }
        finally { applyingState = false; }

        viewPrepared = restored;
        RefreshTitle();
        CaptureViewState();
        return viewPrepared;
    }

    void ConfigureHomeLayout()
    {
        if (homeWindow == null || homeView == null) return;
        homeWindow.ConfigureInteraction(false, false);
        const float border = 4f, titleHeight = 18f, windowGap = 4f;
        // Frame, a margin, then the map: doors and the yard fence sit on the map's outer tiles.
        const float margin = 8f, barGap = 4f, bottom = 4f;
        float bodyHeight = OSHomeLayout.PixelHeight + barGap + OSHomeItemBar.Height;
        float totalHeight = titleHeight + margin + bodyHeight + bottom;
        Vector2 size = new Vector2(OSHomeLayout.PixelWidth + 2f * margin + border, totalHeight);
        RectTransform frame = homeWindow.WindowRect;
        if (frame != null && !homeWindow.IsOpening)
        {
            Vector2 position = frame.anchoredPosition;
            if (memoryWindow != null && memoryWindow.WindowRect != null)
            {
                Rect parent = memoryWindow.WindowRect.rect;
                position = new Vector2(parent.width * .5f + size.x * .5f + windowGap,
                    parent.height * .5f - size.y * .5f);
            }
            if ((homeWindow.RecordedSize - size).sqrMagnitude > .01f
                || (frame.anchoredPosition - position).sqrMagnitude > .01f)
                homeWindow.SetOpeningLayout(position, size);
        }

        var map = homeView.transform as RectTransform;
        var body = map != null ? map.parent as RectTransform : null;
        if (map == null || body == null) return;
        body.anchorMin = body.anchorMax = body.pivot = new Vector2(.5f, .5f);
        // The title bar takes 16 px of the window's top (as the old -7 offset assumed).
        body.anchoredPosition = new Vector2(0f, totalHeight * .5f - 16f - margin - bodyHeight * .5f);
        body.sizeDelta = new Vector2(OSHomeLayout.PixelWidth, bodyHeight);
        map.anchorMin = map.anchorMax = map.pivot = new Vector2(.5f, .5f);
        map.sizeDelta = new Vector2(OSHomeLayout.PixelWidth, OSHomeLayout.PixelHeight);
        map.anchoredPosition = new Vector2(0f, (bodyHeight - OSHomeLayout.PixelHeight) * .5f);
        if (homeItems == null) homeItems = body.GetComponentInChildren<OSHomeItemBar>(true);
        if (homeItems == null) homeItems = OSHomeItemBar.CreateBelow(homeView);
        var bar = homeItems.transform as RectTransform;
        if (bar != null)
        {
            bar.anchorMin = bar.anchorMax = new Vector2(.5f, .5f);
            bar.pivot = new Vector2(.5f, 1f);
            bar.sizeDelta = new Vector2(OSHomeLayout.PixelWidth, OSHomeItemBar.Height);
            bar.anchoredPosition = new Vector2(0f, bodyHeight * .5f - OSHomeLayout.PixelHeight - barGap);
        }
    }

    bool CanUseView(GameDataManager data)
    {
        OS_Handle os = OS_Handle.Instance;
        if (!isActiveAndEnabled || !viewPrepared || os == null || !os.IsOSVisible || data == null
            || !data.IsChapter03HomeActive || !ReferenceEquals(boundState, data.OSProcessData.memory)
            || !IsWindowVisible(memoryWindow) || memoryWindow.IsOpening
            || !IsWindowVisible(homeWindow) || homeWindow.IsOpening
            || os.IsSystemInputLocked || GameTestConsole.BlocksGameplayInput
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || memory == null)
            return false;

        if (story != null && story.IsActive)
            return story.IsAvailable && !story.BlocksHomeInput;

        if (memory.CurrentData == null
            || memory.CurrentData.DataIndex != data.CurrentOSMemoryDataIndex
            || !memory.IsCurrentTextComplete || memory.CanContinue || memory.CurrentData.Options.Count != 0
            || memory.IsSubwindowSequencePlaying || memory.IsSubwindowInputLocked
            || memory.IsSubwindowPresentationPending || memory.IsWaitingForToolRepair)
            return false;

        var messages = os.LinkMessageController;
        // IsPresenting becomes false during a completed message's hold time.
        return messages == null || (!messages.IsPresenting && !messages.IsWaitingForOption
            && (messages.LinkWindow == null
                || (!messages.LinkWindow.IsOpen && !messages.LinkWindow.IsClosing)));
    }

    static bool IsWindowVisible(OSWindowUnit window) => window != null
        && window.IsOpen && !window.IsMinimized && !window.IsClosing
        && window.gameObject.activeInHierarchy;

    void SetInputEnabled(bool enabled)
    {
        if (homeView == null) return;
        if (!enabled && homeView.InputEnabled && !homeView.IsScriptWalking) homeView.StopWalking();
        homeView.InputEnabled = enabled;
    }

    void HandleRoomEntered(OSHomeRoom room)
    {
        RefreshTitle();
        CaptureViewState();
    }

    void HandleStepped(Vector2Int tile, bool onWater) => CaptureViewState();
    void HandleLanguageChanged(LanguageState language) => RefreshTitle();

    void RefreshTitle()
    {
        if (windowTitle == null || homeView == null) return;
        OSLocalizedFontUtility.ApplyCurrent(windowTitle);
        windowTitle.text = homeView.CurrentRoomTitle;
    }
}
