using System;
using DG.Tweening;
using System.Collections.Generic;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed partial class OSMemoryWindowController : MonoBehaviour, IPointerUpHandler, IScrollHandler
{
    const int Chapter02MakeupChoiceIndex = 7180;
    const int Chapter02MakeupCatIndex = 7190;
    const int Chapter02MakeupHeartIndex = 7200;

    [Header("Pools")]
    [SerializeField] Transform textPool;
    [SerializeField] RectTransform memoryMask;
    [SerializeField] OSMemorySubwindowPlayer subwindowPlayer;
    [SerializeField] OSWindowUnit interactivePicWindow;
    [SerializeField] OSChapter03HomeController chapter03Home;

    [SerializeField] List<OSMemoryTextUnit> textUnits =
        new List<OSMemoryTextUnit>();
    [SerializeField] List<OSMemorySelectUnit> selectUnits =
        new List<OSMemorySelectUnit>();

    [Header("Continue Tip")]
    [SerializeField] Image continueTip;
    [SerializeField] RectTransform continueTipRect;
    [SerializeField] TMPro.TMP_Text repairRequiredTip;
    [SerializeField, Min(0f)] float continueTipFadeDuration = 0.1f;
    [SerializeField, Min(0f)] float continueTipMoveDuration = 0.7f;
    [SerializeField] float continueTipTopY = 3.5f;
    [SerializeField] float continueTipBottomY = 1.5f;

    [Header("Option Effects")]
    [SerializeField] bool applyResourceChanges = true;

    [Header("Ordinary Memory Layout")]
    [SerializeField] Vector2 ordinaryWindowPosition = new Vector2(-38.6f, 12f);
    [SerializeField] Vector2 ordinaryWindowSize = new Vector2(340f, 280f);

    OSMemoryReadData currentData;
    OSMemoryTextUnit currentTextUnit;
    Tween continueTipFadeTween;
    Tween continueTipMoveTween;
    bool isCurrentTextComplete;
    bool isContinueTipVisible;
    bool hasWarnedMissingDataManager;
    RectMask2D memoryRectMask;
    OSMemoryTopFade topFade;
    readonly List<OSMemoryReadData> historyEntries =
        new List<OSMemoryReadData>();
    readonly Vector3[] rectCorners = new Vector3[4];
    int renderedHistoryStart;
    int renderedHistoryCount;
    bool isFollowingLatest = true;
    readonly OSWheelScrollMotion historyWheel = new OSWheelScrollMotion();
    bool hasLayoutSnapshot;
    Vector2 lastMemoryMaskSize;
    Vector2 lastTextPoolSize;
    GameDataManager boundDataManager;
    OSWindowUnit window;
    bool windowEventsBound;
    bool breakfastTextLayoutApplied;
    bool breakfastImageLayoutApplied;
    bool chapter03TextLayoutApplied;
    float interactiveEntryReadyTime = -1f;
    bool isBindingData;
    bool presentationFailed;
    int completedPresentationIndex = OSMemoryData.InvalidDataIndex;
    int playingPresentationIndex = OSMemoryData.InvalidDataIndex;
    int presentationCompletedFrame = -1;
    int pendingDebugDataIndex = OSMemoryData.InvalidDataIndex;
    int debugPresentationIndex = OSMemoryData.InvalidDataIndex;
    string debugPresentationProgramId;
    bool queuedMapExit;
    bool queuedWarehouseDoorExit;
    bool warehouseDoorCompleted;
    bool chapter04OutroFrozen;
    readonly List<OSMemoryTextUnit> chapter04FrozenTextUnits = new List<OSMemoryTextUnit>();


    public OSMemoryReadData CurrentData => currentData;
    public Sprite ContinueTipSprite => continueTip != null ? continueTip.sprite
        : transform.Find("TextArea/ContinueTip")?.GetComponent<Image>()?.sprite;
    public Vector2 ContinueTipNativeSize => ContinueTipSprite != null ? ContinueTipSprite.rect.size : Vector2.zero;
    public IReadOnlyList<OSMemoryTextUnit> TextUnits => textUnits;
    public bool IsCurrentTextComplete => isCurrentTextComplete;
    public bool IsChapter04OutroFrozen => chapter04OutroFrozen;
    public bool IsSubwindowSequencePlaying => subwindowPlayer != null && subwindowPlayer.IsPlaying;
    public bool IsSubwindowInputLocked => subwindowPlayer != null && subwindowPlayer.IsBlockingInput;
    public OSMemoryMapPresentation MapPresentation => subwindowPlayer != null
        && subwindowPlayer.CurrentContent != null
        ? subwindowPlayer.CurrentContent.GetComponent<OSMemoryMapPresentation>() : null;
    public bool IsMapExitQueued => queuedMapExit;
    public OSMemoryWarehouseDoorPresentation WarehouseDoorPresentation => subwindowPlayer != null
        && subwindowPlayer.CurrentContent != null
        ? subwindowPlayer.CurrentContent.GetComponent<OSMemoryWarehouseDoorPresentation>() : null;
    public bool IsWarehouseDoorExitQueued => queuedWarehouseDoorExit;
    public bool IsWarehouseDoorCompleted => warehouseDoorCompleted;
    public bool IsSubwindowPresentationPending => currentData != null
        && currentData.SubwindowSequence != null
        && !currentData.SubwindowSequence.KeepOpenUntilCancelled
        && completedPresentationIndex != currentData.DataIndex;
    public bool IsWaitingForToolRepair => currentData != null
        && GameDataManager.Instance != null
        && GameDataManager.Instance.IsMemoryWaitingForToolRepair(currentData.DataIndex);
    bool IsAwaitingInteractiveEntry => currentData?.DataIndex == OSMemoryData.Chapter01Upgrade02DataIndex
        && GameDataManager.Instance?.UsesMemoryInteractiveLayout == true;
    bool IsAwaitingChapter02Finale => currentData?.DataIndex == OSChapter02FinaleDirector.MemoryEndDataIndex
        && OS_Handle.Instance?.GetComponent<OSChapter02FinaleDirector>()?.IsWaitingForMemoryAdvance == true;
    public bool CanContinue =>
        !chapter04OutroFrozen && currentData != null
        && isCurrentTextComplete
        && !IsSubwindowInputLocked
        && !IsSubwindowPresentationPending
        && !IsWaitingForToolRepair
        && !IsAwaitingInteractiveEntry
        && !queuedMapExit
        && !queuedWarehouseDoorExit
        && currentData.Options.Count == 0
        && (currentData.NextDataIndex != OSMemoryData.InvalidDataIndex || IsAwaitingChapter02Finale);
    public IReadOnlyList<OSMemorySelectUnit> SelectUnits => selectUnits;

    public event Action<OSMemoryReadData> CurrentTextCompleted;
    public event Action<OSMemoryReadData> DataShown;
    public event Action<OSMemoryActionData> ActionRequested;
    public event Action<OSMemoryResourceChange> ResourceChangeRequested;

    /// <summary>Hold the final chapter-3 text and layout independently of desktop input locks.</summary>
    public void SetChapter04OutroFrozen(bool frozen)
    {
        chapter04OutroFrozen = frozen;
        if (frozen)
        {
            historyWheel.Clear();
            FreezeChapter04TextUnits();
            HideContinueTip();
            if (repairRequiredTip != null) repairRequiredTip.gameObject.SetActive(false);
            return;
        }
        foreach (var unit in chapter04FrozenTextUnits)
            if (unit != null) unit.enabled = true;
        chapter04FrozenTextUnits.Clear();
    }

    void FreezeChapter04TextUnits()
    {
        // This handoff starts after the last line has finished. Disabling only
        // its presenter also holds waiting dots and scramble without hiding the text.
        foreach (var unit in textUnits)
            if (unit != null && unit.enabled)
            {
                chapter04FrozenTextUnits.Add(unit);
                unit.enabled = false;
            }
    }

    void Awake()
    {
        ResolveReferences();
        if (subwindowPlayer == null)
            subwindowPlayer = GetComponent<OSMemorySubwindowPlayer>();
        if (subwindowPlayer != null)
        {
            subwindowPlayer.ContinuationReady += HandleSubwindowContinuationReady;
            subwindowPlayer.Completed += HandleSubwindowSequenceCompleted;
            subwindowPlayer.ShotOpened += HandleSubwindowShotOpened;
        }
        HideContinueTip();
    }

    void OnEnable()
    {
        ResolveReferences();
        HideContinueTip();
        boundDataManager = GameDataManager.Instance;
        if (boundDataManager != null)
            boundDataManager.ToolProgramInstalled += HandleToolProgramInstalled;
        RefreshContinueTip();

        if (currentData == null)
            return;

        if (currentTextUnit == null || !currentTextUnit.IsTextComplete)
            InitializeFromSave();
        else
            RefreshContinueTip();
    }

    void OnDisable()
    {
        historyWheel.Clear();
        chapter03Home?.Suspend();
        SuspendChapter02Audio();
        queuedMapExit = false;
        queuedWarehouseDoorExit = false;
        interactiveEntryReadyTime = -1f;
        subwindowPlayer?.Cancel();
        interactivePicWindow?.CloseSilently();
        if (boundDataManager != null)
            boundDataManager.ToolProgramInstalled -= HandleToolProgramInstalled;
        boundDataManager = null;
        HideContinueTip();
    }

    void OnDestroy()
    {
        DisposeChapter02Audio();
        if (windowEventsBound && window != null)
        {
            window.OpenStarted -= HandleWindowOpenStarted;
            window.OpenAnimationCompleted -= HandleWindowOpenAnimationCompleted;
            window.AlwaysOnTopToggled -= HandleAlwaysOnTopToggled;
        }
        if (subwindowPlayer != null)
        {
            subwindowPlayer.ContinuationReady -= HandleSubwindowContinuationReady;
            subwindowPlayer.Completed -= HandleSubwindowSequenceCompleted;
            subwindowPlayer.ShotOpened -= HandleSubwindowShotOpened;
        }
        HideContinueTip();
        if (textUnits == null)
            return;

        for (int i = 0; i < textUnits.Count; i++)
        {
            if (textUnits[i] != null)
            {
                textUnits[i].PlaybackCompleted -=
                    HandleTextPlaybackCompleted;
            }
        }
    }

    void Update()
    {
        if (chapter04OutroFrozen)
        {
            FreezeChapter04TextUnits();
            HideContinueTip();
            return;
        }
        TryApplyDebugJump();
        RefreshInteractiveWindowLayout();
        TryAutoEnterInteractiveScene();
        RefreshInteractivePicWindow();
        chapter03Home?.RefreshPresentation();
        if (window == null || !window.IsOpen || window.IsMinimized || window.IsClosing)
        {
            historyWheel.Clear();
            queuedMapExit = false;
            queuedWarehouseDoorExit = false;
            if (IsSubwindowSequencePlaying) subwindowPlayer.Cancel();
        }
        TryStartSubwindowSequence();
        TryFinishQueuedMapExit();
        TryFinishWarehouseDoor();
        TickChapter02Audio();
        if (currentData?.DataIndex == OSChapter02FinaleDirector.MemoryEndDataIndex)
            RefreshContinueTip();
        OSStoryRecordUnlocks.Refresh(GameDataManager.Instance,
            OS_Handle.Instance?.GetComponent<OSPaperPushStoryDirector>()?.IsTakingOver == true);
        if (CanReceiveInput() && Keyboard.current != null
            && Keyboard.current.eKey.wasPressedThisFrame)
        {
            TryAdvance();
        }
    }

    void LateUpdate()
    {
        if (chapter04OutroFrozen)
        {
            historyWheel.Clear();
            return;
        }
        if (OSSessionPersistence.IsRestoring
            || window == null || !window.IsOpen || window.IsMinimized
            || window.IsOpening || window.IsClosing)
            historyWheel.Clear();
        if (renderedHistoryCount <= 0
            || memoryMask == null
            || TextPoolRect == null)
        {
            historyWheel.Clear();
            return;
        }

        bool textIsPlaying =
            currentTextUnit != null && currentTextUnit.IsPlaying;
        bool layoutChanged = textIsPlaying || HaveLayoutDimensionsChanged();
        if (!layoutChanged && !historyWheel.IsMoving)
            return;

        if (layoutChanged)
        {
            ForceTextPoolLayout();
            if (isFollowingLatest && !historyWheel.IsMoving)
                AlignLatestToBottom();
            else
                ClampHistoryPosition();
        }

        if (historyWheel.IsMoving)
            AdvanceHistoryWheel();

        CaptureLayoutSnapshot();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (chapter04OutroFrozen || eventData == null
            || OSSessionPersistence.IsRestoring || window == null || !window.IsOpen
            || window.IsMinimized || window.IsOpening || window.IsClosing
            || !isCurrentTextComplete
            || renderedHistoryCount <= 0
            || TextPoolRect == null
            || Mathf.Approximately(eventData.scrollDelta.y, 0f)
            || !IsPointerInsideMemoryMask(eventData))
        {
            return;
        }

        ForceTextPoolLayout();
        historyWheel.AddWheel(eventData.scrollDelta.y, TextPoolRect.parent as RectTransform);
        // Detach before LateUpdate can align new layout back to the bottom.
        if (historyWheel.IsMoving) isFollowingLatest = false;
        eventData.Use();
    }

    void AdvanceHistoryWheel()
    {
        if (!isCurrentTextComplete)
        {
            historyWheel.Clear();
            return;
        }
        float delta = historyWheel.Step(Time.unscaledDeltaTime);
        if (Mathf.Approximately(delta, 0f)) return;
        ShiftTextPoolY(delta);
        // Recycled rows compensate the pool position. Smooth only the remaining
        // displacement, never an absolute Y target that becomes stale here.
        if (delta < 0f)
            RecycleTowardOlderHistory();
        else
            RecycleTowardNewerHistory();

        float beforeClamp = TextPoolRect.anchoredPosition.y;
        ClampHistoryPosition();
        if (Mathf.Abs(TextPoolRect.anchoredPosition.y - beforeClamp) > 0.001f)
            historyWheel.Clear();
        UpdateCurrentTextUnitReference();
        isFollowingLatest = !historyWheel.IsMoving && IsLatestAlignedToBottom();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left
            || eventData.dragging
            || IsAdvanceInputBlocked(eventData.rawPointerPress)
            || IsAdvanceInputBlocked(
                eventData.pointerCurrentRaycast.gameObject))
        {
            return;
        }

        TryAdvance();
    }


    public void PrepareRuntime()
    {
        historyWheel.Clear();
        SetChapter04OutroFrozen(false);
        chapter03Home?.ResetRuntime();
        ResetChapter02Audio();
        queuedMapExit = false;
        queuedWarehouseDoorExit = false;
        warehouseDoorCompleted = false;
        interactiveEntryReadyTime = -1f;
        pendingDebugDataIndex = OSMemoryData.InvalidDataIndex;
        debugPresentationIndex = OSMemoryData.InvalidDataIndex;
        debugPresentationProgramId = null;
        subwindowPlayer?.Cancel();
        completedPresentationIndex = OSMemoryData.InvalidDataIndex;
        playingPresentationIndex = OSMemoryData.InvalidDataIndex;
        presentationFailed = false;
        ResolveReferences();
        interactivePicWindow?.PrepareRuntimeClosed();
        window.ClearTemporaryOpeningLayout();
        breakfastTextLayoutApplied = false;
        breakfastImageLayoutApplied = false;
        chapter03TextLayoutApplied = false;
        HideContinueTip();
        currentData = null;
        currentTextUnit = null;
        isCurrentTextComplete = false;
        historyEntries.Clear();
        renderedHistoryStart = 0;
        renderedHistoryCount = 0;
        isFollowingLatest = true;
        hasLayoutSnapshot = false;
        ClearTextUnits();
        ClearSelectUnits();

        RectTransform poolRect = TextPoolRect;
        if (poolRect != null)
        {
            Vector2 position = poolRect.anchoredPosition;
            position.y = 0f;
            poolRect.anchoredPosition = position;
        }
    }

    public void InitializeFromSave()
    {
        historyWheel.Clear();
        // Reopening the released chapter-3 window keeps its held final text.
        // ShowData deliberately rejects rebinding while frozen; that refusal is
        // not a missing save and must not fall through to PrepareRuntime().
        if (chapter04OutroFrozen && currentData != null)
            return;
        // Opening callbacks must not overwrite an explicitly queued test node.
        if (pendingDebugDataIndex != OSMemoryData.InvalidDataIndex)
            return;
        ResolveReferences();

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
        {
            WarnMissingDataManager();
            return;
        }

        hasWarnedMissingDataManager = false;
        OSMemoryProcessSaveData memory = dataManager.OSProcessData.memory;
        memory.EnsureData();

        int currentIndex = memory.currentDataIndex;
        if (currentIndex == OSMemoryProcessSaveData.InvalidDataIndex)
            currentIndex = FindLatestValidHistoryIndex(dataManager, memory);

        if (currentIndex == OSMemoryProcessSaveData.InvalidDataIndex)
        {
            PrepareRuntime();
            return;
        }

        bool recordAsTriggered =
            !memory.triggeredDataIndices.Contains(currentIndex);
        reconstructingChapter02Audio = true;
        try
        {
            if (!ShowData(currentIndex, recordAsTriggered))
                PrepareRuntime();
        }
        finally { reconstructingChapter02Audio = false; }
    }

    public void RefreshLocalizedData()
    {
        if (currentData == null || pendingDebugDataIndex != OSMemoryData.InvalidDataIndex)
            return;
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        // ReplaceSaveData can raise LanguageChanged before the window is
        // reconstructed. The saved cursor already belongs to the new session;
        // rebinding the old visible paragraph would write its index back into it.
        int savedIndex = dataManager.CurrentOSMemoryDataIndex;
        if (!dataManager.TryGetOSMemoryData(savedIndex, out _))
        {
            PrepareRuntime();
            return;
        }
        ShowData(savedIndex, false);
    }

    public bool DebugJumpToData(int dataIndex)
    {
        GameDataManager data = GameDataManager.Instance;
        if (!Application.isPlaying || data == null
            || !data.TryGetOSMemoryData(dataIndex, out OSMemoryReadData targetData))
            return false;

        PrepareRuntime();
        // Keep this previewed prerequisite for later shots in the same reading session.
        // Other upgrades and option prerequisites still use the actual installation state.
        debugPresentationProgramId = targetData.SubwindowSequence?.RequiredToolProgram;
        pendingDebugDataIndex = dataIndex;
        // Apply in Update after GameTest opens/focuses Memory. Starting a sequence here
        // would let the subsequent BringToFront cover its sibling subwindow pool.
        return true;
    }

    void TryApplyDebugJump()
    {
        if (pendingDebugDataIndex == OSMemoryData.InvalidDataIndex
            || !isActiveAndEnabled || window == null || !window.IsOpen
            || window.IsMinimized || window.IsOpening)
            return;

        int dataIndex = pendingDebugDataIndex;
        pendingDebugDataIndex = OSMemoryData.InvalidDataIndex;
        // Preview state is temporary; never install a tool or spend resources.
        debugPresentationIndex = dataIndex;
        ShowData(dataIndex);
    }

    public bool ShowData(
        int dataIndex,
        bool recordAsTriggered = true)
        => ShowData(dataIndex, recordAsTriggered, false);

    // Picture buttons may be clicked while a previous line is still typing.
    // Finish that line before reusing its text unit so history never keeps a fragment.
    public bool ShowInteractionData(int dataIndex)
    {
        if (GameDataManager.Instance == null
            || !GameDataManager.Instance.TryGetOSMemoryData(dataIndex, out _))
            return false;
        if (currentTextUnit != null && !isCurrentTextComplete)
            currentTextUnit.CompleteImmediately();
        return ShowData(dataIndex);
    }

    // Only the completed mainline releases 5400. Its authored node remains a
    // wait with no clickable next link, so the experiment cannot be skipped.
    public bool CompleteChapter02Collection()
    {
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 2
            || data.OSProcessData.memory.chapter02HeartTalkStep != (int)OSCursorHeartTalkDirector.HeartTalkStep.Done)
            return false;
        if (data.CurrentOSMemoryDataIndex != OSMemoryData.Chapter02CollectionWaitingDataIndex)
            return data.CurrentOSMemoryDataIndex >= OSMemoryData.Chapter02PostHeartTalkDataIndex;
        return ShowInteractionData(OSMemoryData.Chapter02PostHeartTalkDataIndex);
    }

    bool ShowData(int dataIndex, bool recordAsTriggered, bool preserveSubwindowSequence)
    {
        if (chapter04OutroFrozen && currentData != null) return false;
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
        {
            WarnMissingDataManager();
            return false;
        }

        if (!dataManager.TryGetOSMemoryData(
                dataIndex,
                out OSMemoryReadData data))
        {
            Debug.LogWarning(
                $"[OS Memory] Data index {dataIndex} does not exist.",
                this);
            return false;
        }

        if (data.IsInteractiveSceneEntry && !dataManager.IsMemoryRepair02Installed)
            return false;

        historyWheel.Clear();
        // A real story transition supersedes a queued console preview. The
        // preview may still be waiting for its hidden/opening window when the
        // heart-talk handoff advances 5400 -> 5410.
        pendingDebugDataIndex = OSMemoryData.InvalidDataIndex;

        if (recordAsTriggered || currentData == null || currentData.DataIndex != dataIndex)
        {
            if (!preserveSubwindowSequence)
            {
                subwindowPlayer?.Cancel();
                playingPresentationIndex = OSMemoryData.InvalidDataIndex;
            }
            completedPresentationIndex = OSMemoryData.InvalidDataIndex;
            presentationFailed = false;
        }
        if (debugPresentationIndex != dataIndex)
            debugPresentationIndex = OSMemoryData.InvalidDataIndex;
        HideContinueTip();
        ClearSelectUnits();
        if (currentData == null || currentData.DataIndex != dataIndex)
        {
            queuedMapExit = false;
            queuedWarehouseDoorExit = false;
        }
        // Only a new visit resets this finite insert. Rebinding localization or
        // restoring the same finished paragraph must not start it again.
        if (!OSMemoryWarehouseDoorPresentation.ContainsNode(dataIndex)
            || currentData == null
            || !OSMemoryWarehouseDoorPresentation.ContainsNode(currentData.DataIndex)
            || (!preserveSubwindowSequence && recordAsTriggered))
            warehouseDoorCompleted = false;
        int previousAudioNode = currentData != null ? currentData.DataIndex : -1;
        currentData = data;
        currentTextUnit = null;
        isCurrentTextComplete = false;

        if (recordAsTriggered)
            dataManager.RecordTriggeredOSMemoryData(dataIndex);
        else
            dataManager.SetCurrentOSMemoryData(dataIndex);

        if (data.IsInteractiveSceneEntry)
            dataManager.EnterMemoryInteractiveScene(dataIndex);
        if (interactivePicWindow != null)
            interactivePicWindow.GetComponent<OSMemoryPicController>()?.NotifyMemoryNode(dataIndex);
        RefreshInteractiveWindowLayout();

        isBindingData = true;
        try { RenderHistory(dataManager, recordAsTriggered); }
        finally { isBindingData = false; }
        if (isCurrentTextComplete)
            BindSelectUnits(data);
        RefreshContinueTip();
        ObserveChapter02AudioNode(dataIndex, previousAudioNode, recordAsTriggered);
        DataShown?.Invoke(data);

        if (recordAsTriggered)
            RequestActions(data.OnEnterActions);

        if (currentData == data)
            TryStartSubwindowSequence();

        return true;
    }

    public bool TryAdvance()
    {
        if (OS_Handle.Instance?.Chapter03Story?.BlocksMemoryAdvance == true) return false;
        if (!CanReceiveInput() || presentationCompletedFrame == Time.frameCount
            || IsSubwindowInputLocked)
            return false;

        if (!isCurrentTextComplete && currentTextUnit != null)
        {
            currentTextUnit.CompleteImmediately();
            return true;
        }
        if (!CanContinue)
            return false;

        if (IsAwaitingChapter02Finale)
        {
            bool continued = OS_Handle.Instance.GetComponent<OSChapter02FinaleDirector>().TryContinueFromMemory();
            RefreshContinueTip();
            return continued;
        }

        // A deliberate reading advance returns a resident illustration to Memory.
        // Its own unfinished gesture can keep the temporary foreground until done.
        subwindowPlayer?.NotifyReadingAdvanced();

        // Only the boundary after the map queues a continuation. Repeated clicks
        // never advance additional lines or skip the final route/arrival hold.
        if (currentData.DataIndex == OSMemoryMapPresentation.LastMapNode
            && currentData.NextDataIndex == OSMemoryMapPresentation.ExitNode
            && MapPresentation != null && !MapPresentation.IsReadyToLeave)
        {
            queuedMapExit = true;
            HideContinueTip();
            return true;
        }
        if (currentData.DataIndex == OSMemoryWarehouseDoorPresentation.LastNode
            && currentData.NextDataIndex == OSMemoryWarehouseDoorPresentation.ExitNode
            && WarehouseDoorPresentation != null && !warehouseDoorCompleted)
        {
            queuedWarehouseDoorExit = true;
            HideContinueTip();
            return true;
        }

        int nextDataIndex = currentData.NextDataIndex;
        HideContinueTip();
        isCurrentTextComplete = false;
        return ShowData(nextDataIndex, true, true);
    }

    void TryFinishQueuedMapExit()
    {
        if (!queuedMapExit || currentData == null
            || currentData.DataIndex != OSMemoryMapPresentation.LastMapNode
            || window == null || !window.IsOpen || window.IsOpening || window.IsMinimized || window.IsClosing)
            return;
        OS_Handle os = OS_Handle.Instance;
        if (GameTestConsole.BlocksGameplayInput
            || (os != null && (!os.IsOSVisible || os.IsSystemInputLocked)))
            return;
        var presentation = MapPresentation;
        if (presentation == null || !presentation.IsReadyToLeave)
            return;
        queuedMapExit = false;
        presentationCompletedFrame = Time.frameCount;
        ShowData(OSMemoryMapPresentation.ExitNode, true, true);
    }

    void TryFinishWarehouseDoor()
    {
        if (window == null || !window.IsOpen || window.IsOpening || window.IsMinimized || window.IsClosing)
            return;
        var presentation = WarehouseDoorPresentation;
        if (presentation != null && presentation.IsReadyToClose)
            subwindowPlayer.ClosePersistentAnimated();
        if (!queuedWarehouseDoorExit || !warehouseDoorCompleted || currentData == null
            || currentData.DataIndex != OSMemoryWarehouseDoorPresentation.LastNode)
            return;
        OS_Handle os = OS_Handle.Instance;
        if (GameTestConsole.BlocksGameplayInput
            || (os != null && (!os.IsOSVisible || os.IsSystemInputLocked)))
            return;
        queuedWarehouseDoorExit = false;
        presentationCompletedFrame = Time.frameCount;
        ShowData(OSMemoryWarehouseDoorPresentation.ExitNode, true, true);
    }

    public void SelectOption(OSMemoryReadOptionData option)
    {
        if (option == null || !isCurrentTextComplete || IsSubwindowPresentationPending
            || !IsOptionAvailable(option) || !CanReceiveInput())
            return;

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null
            || option.TargetDataIndex == OSMemoryData.InvalidDataIndex
            || !dataManager.TryGetOSMemoryData(
                option.TargetDataIndex,
                out OSMemoryReadData _))
        {
            Debug.LogWarning(
                $"[OS Memory] Option target {option.TargetDataIndex} is invalid.",
                this);
            return;
        }

        subwindowPlayer?.NotifyReadingAdvanced();
        int energyBefore = dataManager.GetOSResourceValue(ResourceType.Battery);
        ApplyResourceChange(option.ResourceChange);
        if (dataManager.GetOSResourceValue(ResourceType.Battery) < energyBefore)
        {
            for (int i = 0; i < selectUnits.Count; i++)
            {
                if (selectUnits[i] != null && selectUnits[i].Option == option)
                {
                    OSUIEnergySpendPresenter.Show(this, selectUnits[i].transform as RectTransform,
                        outsideRight: true, textAnchor: selectUnits[i].Label);
                    break;
                }
            }
        }
        RequestActions(option.Actions);

        bool makeupChoice = dataManager.CurrentChapterIndex == 2
            && currentData?.DataIndex == Chapter02MakeupChoiceIndex
            && ((option.OptionId == 1 && option.TargetDataIndex == Chapter02MakeupCatIndex)
                || (option.OptionId == 2 && option.TargetDataIndex == Chapter02MakeupHeartIndex));
        if (!makeupChoice)
        {
            ShowData(option.TargetDataIndex, true, true);
            return;
        }

        // Only a validated option click records the pattern. Showing a branch
        // for a debug preview, restoring a save or rebinding language does not.
        // Publish before DataShown runs; undo if the destination cannot open.
        OSMemoryProcessSaveData memory = dataManager.OSProcessData.memory;
        int previousPattern = memory.chapter02MakeupPattern;
        bool advanced = false;
        memory.chapter02MakeupPattern = option.OptionId;
        try
        {
            advanced = ShowData(option.TargetDataIndex, true, true);
        }
        finally
        {
            if (!advanced)
                memory.chapter02MakeupPattern = previousPattern;
        }
    }

    public bool IsOptionAvailable(OSMemoryReadOptionData option)
    {
        if (currentData == null || option == null || GameDataManager.Instance == null)
            return false;
        for (int i = 0; i < currentData.Options.Count; i++)
        {
            if (currentData.Options[i] == option)
                return string.IsNullOrEmpty(GameDataManager.Instance.GetMissingOSMemoryToolProgram(option.Actions));
        }
        return false;
    }

    void RenderHistory(
        GameDataManager dataManager,
        bool animateLatest)
    {
        historyWheel.Clear();
        int previousHistoryCount = historyEntries.Count;
        bool canAppendLatest =
            animateLatest
            && isFollowingLatest
            && renderedHistoryCount > 0
            && renderedHistoryStart + renderedHistoryCount
                == previousHistoryCount;

        RebuildHistoryEntries(dataManager);
        if (canAppendLatest
            && historyEntries.Count == previousHistoryCount + 1)
        {
            AppendLatestHistoryEntry();
        }
        else
        {
            RenderLatestHistoryWindow(animateLatest);
        }

        isCurrentTextComplete =
            currentData?.IsSilentEntry == true
            || (currentTextUnit != null && currentTextUnit.IsTextComplete);
        RefreshContinueTip();
    }

    RectTransform TextPoolRect => textPool as RectTransform;

    void RebuildHistoryEntries(GameDataManager dataManager)
    {
        historyEntries.Clear();
        IReadOnlyList<int> history =
            dataManager.TriggeredOSMemoryDataIndices;

        int historyEnd = history.Count - 1;
        if (currentData != null)
        {
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i] != currentData.DataIndex)
                    continue;
                historyEnd = i;
                break;
            }
        }

        for (int i = 0; i <= historyEnd; i++)
        {
            if (dataManager.TryGetOSMemoryData(
                    history[i],
                    out OSMemoryReadData data) && !data.IsSilentEntry)
            {
                historyEntries.Add(data);
            }
        }
        if (currentData != null && !currentData.IsSilentEntry && (historyEntries.Count == 0
            || historyEntries[historyEntries.Count - 1].DataIndex != currentData.DataIndex))
            historyEntries.Add(currentData);
    }

    void RenderLatestHistoryWindow(bool animateLatest)
    {
        ClearTextUnits();
        currentTextUnit = null;
        isCurrentTextComplete = false;
        renderedHistoryCount = Mathf.Min(
            historyEntries.Count,
            textUnits.Count);
        renderedHistoryStart =
            historyEntries.Count - renderedHistoryCount;

        for (int i = 0; i < textUnits.Count; i++)
            textUnits[i].transform.SetSiblingIndex(i);

        for (int i = 0; i < renderedHistoryCount; i++)
        {
            int historyIndex = renderedHistoryStart + i;
            bool isLatest =
                historyEntries[historyIndex].DataIndex == currentData.DataIndex
                && historyIndex == historyEntries.Count - 1;
            OSMemoryTextUnit unit = textUnits[i];
            if (isLatest)
                currentTextUnit = unit;

            unit.Bind(
                historyEntries[historyIndex],
                isLatest,
                isLatest && animateLatest);
        }

        isFollowingLatest = true;
        ForceTextPoolLayout();
        if (renderedHistoryCount > 0)
            AlignLatestToBottom();

        UpdateCurrentTextUnitReference();
        CaptureLayoutSnapshot();
    }

    void AppendLatestHistoryEntry()
    {
        if (historyEntries.Count == 0 || textUnits.Count == 0)
        {
            RenderLatestHistoryWindow(true);
            return;
        }

        if (renderedHistoryCount > 0)
        {
            textUnits[renderedHistoryCount - 1]
                .SetLatestVisual(false);
        }

        OSMemoryTextUnit unit;
        if (renderedHistoryCount < textUnits.Count)
        {
            unit = textUnits[renderedHistoryCount];
            renderedHistoryCount++;
        }
        else
        {
            unit = textUnits[0];
            textUnits.RemoveAt(0);
            textUnits.Insert(renderedHistoryCount - 1, unit);
            renderedHistoryStart++;
        }

        unit.transform.SetSiblingIndex(renderedHistoryCount - 1);
        currentTextUnit = unit;
        isCurrentTextComplete = false;
        isFollowingLatest = true;
        unit.Bind(historyEntries[historyEntries.Count - 1], true, true);

        ForceTextPoolLayout();
        AlignLatestToBottom();
        CaptureLayoutSnapshot();
    }

    void RecycleTowardOlderHistory()
    {
        if (renderedHistoryCount <= 1)
            return;

        int guard = 0;
        while (renderedHistoryStart > 0
            && GetRectTop(textUnits[0].transform as RectTransform)
                < GetVisibleTop() - 0.01f
            && guard++ < 64)
        {
            OSMemoryTextUnit stableUnit = textUnits[0];
            float stableTopBefore = GetRectTop(
                stableUnit.transform as RectTransform);
            OSMemoryTextUnit recycled =
                textUnits[renderedHistoryCount - 1];

            if (recycled == currentTextUnit)
                currentTextUnit = null;

            textUnits.RemoveAt(renderedHistoryCount - 1);
            textUnits.Insert(0, recycled);
            recycled.transform.SetSiblingIndex(0);
            renderedHistoryStart--;
            recycled.Bind(
                historyEntries[renderedHistoryStart],
                false,
                false);

            ForceTextPoolLayout();
            float stableTopAfter = GetRectTop(
                stableUnit.transform as RectTransform);
            ShiftTextPoolY(stableTopBefore - stableTopAfter);
        }
    }

    void RecycleTowardNewerHistory()
    {
        if (renderedHistoryCount <= 1)
            return;

        int guard = 0;
        while (renderedHistoryStart + renderedHistoryCount
                < historyEntries.Count
            && GetRectBottom(
                textUnits[renderedHistoryCount - 1]
                    .transform as RectTransform)
                > GetVisibleBottom() + 0.01f
            && guard++ < 64)
        {
            OSMemoryTextUnit stableUnit =
                textUnits[renderedHistoryCount - 1];
            float stableBottomBefore = GetRectBottom(
                stableUnit.transform as RectTransform);
            OSMemoryTextUnit recycled = textUnits[0];
            int nextHistoryIndex =
                renderedHistoryStart + renderedHistoryCount;

            if (recycled == currentTextUnit)
                currentTextUnit = null;

            textUnits.RemoveAt(0);
            textUnits.Insert(renderedHistoryCount - 1, recycled);
            recycled.transform.SetSiblingIndex(
                renderedHistoryCount - 1);
            renderedHistoryStart++;
            bool isLatest =
                nextHistoryIndex == historyEntries.Count - 1;
            recycled.Bind(
                historyEntries[nextHistoryIndex],
                isLatest,
                false);

            ForceTextPoolLayout();
            float stableBottomAfter = GetRectBottom(
                stableUnit.transform as RectTransform);
            ShiftTextPoolY(
                stableBottomBefore - stableBottomAfter);
        }
    }

    void ForceTextPoolLayout()
    {
        RectTransform poolRect = TextPoolRect;
        if (poolRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(poolRect);
        Canvas.ForceUpdateCanvases();
    }

void AlignLatestToBottom()
    {
        if (renderedHistoryCount <= 0)
            return;

        RectTransform poolRect = TextPoolRect;
        float visibleHeight =
            GetVisibleTop() - GetVisibleBottom();
        if (poolRect != null
            && poolRect.rect.height <= visibleHeight + 0.01f)
        {
            Vector2 position = poolRect.anchoredPosition;
            position.y = 0f;
            poolRect.anchoredPosition = position;
            return;
        }

        RectTransform latestRect =
            textUnits[renderedHistoryCount - 1]
                .transform as RectTransform;
        ShiftTextPoolY(
            GetVisibleBottom() - GetRectBottom(latestRect));
    }

    void ClampHistoryPosition()
    {
        if (renderedHistoryCount <= 0)
            return;

        bool isAtOldest = renderedHistoryStart == 0;
        bool isAtLatest =
            renderedHistoryStart + renderedHistoryCount
            >= historyEntries.Count;
        float visibleHeight =
            GetVisibleTop() - GetVisibleBottom();
        RectTransform poolRect = TextPoolRect;

        if (isAtLatest
            && poolRect != null
            && poolRect.rect.height <= visibleHeight + 0.01f)
        {
            AlignLatestToBottom();
            return;
        }

        if (isAtOldest)
        {
            float firstTop = GetRectTop(
                textUnits[0].transform as RectTransform);
            if (firstTop < GetVisibleTop())
                ShiftTextPoolY(GetVisibleTop() - firstTop);
        }

        if (isAtLatest)
        {
            float lastBottom = GetRectBottom(
                textUnits[renderedHistoryCount - 1]
                    .transform as RectTransform);
            if (lastBottom > GetVisibleBottom())
            {
                ShiftTextPoolY(
                    GetVisibleBottom() - lastBottom);
            }
        }
    }

    void UpdateCurrentTextUnitReference()
    {
        if (renderedHistoryCount > 0
            && renderedHistoryStart + renderedHistoryCount
                == historyEntries.Count
            && historyEntries[historyEntries.Count - 1].DataIndex == currentData?.DataIndex)
        {
            currentTextUnit =
                textUnits[renderedHistoryCount - 1];
        }
        else
        {
            currentTextUnit = null;
        }
    }

bool IsLatestAlignedToBottom()
    {
        if (renderedHistoryCount <= 0
            || renderedHistoryStart + renderedHistoryCount
                != historyEntries.Count)
        {
            return false;
        }

        RectTransform poolRect = TextPoolRect;
        float visibleHeight =
            GetVisibleTop() - GetVisibleBottom();
        if (poolRect != null
            && poolRect.rect.height <= visibleHeight + 0.01f)
        {
            return Mathf.Abs(poolRect.anchoredPosition.y) <= 0.5f;
        }

        float lastBottom = GetRectBottom(
            textUnits[renderedHistoryCount - 1]
                .transform as RectTransform);
        return Mathf.Abs(lastBottom - GetVisibleBottom()) <= 0.5f;
    }

    bool IsPointerInsideMemoryMask(PointerEventData eventData)
    {
        return memoryMask != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                memoryMask,
                eventData.position,
                eventData.enterEventCamera);
    }

    float GetVisibleTop()
    {
        if (memoryMask == null)
            return 0f;

        float topPadding = memoryRectMask != null
            ? Mathf.Max(0f, memoryRectMask.padding.w)
            : 0f;
        return memoryMask.rect.yMax - topPadding;
    }

    float GetVisibleBottom()
    {
        if (memoryMask == null)
            return 0f;

        float bottomPadding = memoryRectMask != null
            ? Mathf.Max(0f, memoryRectMask.padding.y)
            : 0f;
        return memoryMask.rect.yMin + bottomPadding;
    }

    float GetRectTop(RectTransform rectTransform)
    {
        if (rectTransform == null || memoryMask == null)
            return 0f;

        rectTransform.GetWorldCorners(rectCorners);
        return memoryMask
            .InverseTransformPoint(rectCorners[1]).y;
    }

    float GetRectBottom(RectTransform rectTransform)
    {
        if (rectTransform == null || memoryMask == null)
            return 0f;

        rectTransform.GetWorldCorners(rectCorners);
        return memoryMask
            .InverseTransformPoint(rectCorners[0]).y;
    }

    void ShiftTextPoolY(float deltaY)
    {
        RectTransform poolRect = TextPoolRect;
        if (poolRect == null || Mathf.Approximately(deltaY, 0f))
            return;

        Vector2 position = poolRect.anchoredPosition;
        position.y += deltaY;
        poolRect.anchoredPosition = position;
    }

    bool HaveLayoutDimensionsChanged()
    {
        if (memoryMask == null || TextPoolRect == null)
            return false;

        if (!hasLayoutSnapshot)
            return true;

        Vector2 maskSize = memoryMask.rect.size;
        Vector2 poolSize = TextPoolRect.rect.size;
        return (maskSize - lastMemoryMaskSize).sqrMagnitude > 0.01f
            || (poolSize - lastTextPoolSize).sqrMagnitude > 0.01f;
    }

    void CaptureLayoutSnapshot()
    {
        if (memoryMask == null || TextPoolRect == null)
        {
            hasLayoutSnapshot = false;
            return;
        }

        lastMemoryMaskSize = memoryMask.rect.size;
        lastTextPoolSize = TextPoolRect.rect.size;
        hasLayoutSnapshot = true;
    }

    void BindSelectUnits(OSMemoryReadData data)
    {
        if (IsSubwindowPresentationPending)
        {
            ClearSelectUnits();
            return;
        }
        ClearSelectUnits();

        int availableCount = Mathf.Min(3, selectUnits.Count);
        int count = Mathf.Min(data.Options.Count, availableCount);
        for (int i = 0; i < count; i++)
            selectUnits[i].Bind(this, data.Options[i], i);

        if (data.Options.Count > availableCount)
        {
            Debug.LogWarning(
                $"[OS Memory] Data {data.DataIndex} has {data.Options.Count} options, but only {availableCount} can be displayed.",
                this);
        }
    }

    void ApplyResourceChange(OSMemoryResourceChange change)
    {
        if (change == null)
            return;

        ResourceChangeRequested?.Invoke(change);
        if (!applyResourceChanges)
            return;

        GameResourceSystem resources = GameResourceSystem.Instance;
        if (resources == null)
            return;

        if (!Mathf.Approximately(change.Heart, 0f))
            resources.Modify(ResourceType.Heart, change.Heart);

        if (!Mathf.Approximately(change.Battery, 0f))
            resources.Modify(ResourceType.Battery, change.Battery);

        if (!Mathf.Approximately(change.Fault, 0f))
            resources.Modify(ResourceType.Fault, change.Fault);
    }

    void RequestActions(IReadOnlyList<OSMemoryActionData> actions)
    {
        if (actions == null)
            return;

        for (int i = 0; i < actions.Count; i++)
        {
            OSMemoryActionData action = actions[i];
            if (action != null && action.ActionId != OSMemoryActionIds.RequireToolProgram
                && action.ActionId != OSMemoryActionIds.PlaySubwindowsAfterText)
                ActionRequested?.Invoke(action);
        }
    }

    int FindLatestValidHistoryIndex(
        GameDataManager dataManager,
        OSMemoryProcessSaveData memory)
    {
        for (int i = memory.triggeredDataIndices.Count - 1; i >= 0; i--)
        {
            int dataIndex = memory.triggeredDataIndices[i];
            if (dataManager.TryGetOSMemoryData(
                    dataIndex,
                    out OSMemoryReadData _))
            {
                return dataIndex;
            }
        }

        return OSMemoryProcessSaveData.InvalidDataIndex;
    }

    void ResolveReferences()
    {
        if (chapter03Home == null)
            chapter03Home = GetComponent<OSChapter03HomeController>();
        if (window == null)
            window = GetComponent<OSWindowUnit>();
        if (window != null && !windowEventsBound)
        {
            window.OpenStarted += HandleWindowOpenStarted;
            window.OpenAnimationCompleted += HandleWindowOpenAnimationCompleted;
            window.AlwaysOnTopToggled += HandleAlwaysOnTopToggled;
            windowEventsBound = true;
        }
        if (interactivePicWindow == null)
            interactivePicWindow = transform.Find("SubWindow_InterActivePicWindow")?.GetComponent<OSWindowUnit>();
        if (memoryMask == null)
        {
            memoryMask = transform.Find(
                "TextArea/MemoryMask") as RectTransform;
        }

        if (textPool == null && memoryMask != null)
            textPool = memoryMask.Find("MemoryArea_TextPool");

        if (memoryRectMask == null && memoryMask != null)
            memoryRectMask = memoryMask.GetComponent<RectMask2D>();

        if (topFade == null && memoryMask != null)
            topFade = OSMemoryTopFade.Ensure(memoryMask, memoryRectMask, textPool as RectTransform);

        Transform continueTipTransform =
            transform.Find("TextArea/ContinueTip");
        if (continueTip == null)
            continueTip =
                continueTipTransform?.GetComponent<Image>();

        if (continueTipRect == null)
        {
            continueTipRect =
                continueTipTransform as RectTransform;
        }

        ResolveTextUnits();
        ResolveSelectUnits();
    }

    void ResolveTextUnits()
    {
        if (textUnits == null)
            textUnits = new List<OSMemoryTextUnit>();

        textUnits.RemoveAll(unit => unit == null);
        if (textPool != null)
        {
            OSMemoryTextUnit[] resolved =
                textPool.GetComponentsInChildren<OSMemoryTextUnit>(true);
            for (int i = 0; i < resolved.Length; i++)
            {
                if (!textUnits.Contains(resolved[i]))
                    textUnits.Add(resolved[i]);
            }
        }

        textUnits.Sort(CompareSiblingOrder);
        for (int i = 0; i < textUnits.Count; i++)
        {
            OSMemoryTextUnit unit = textUnits[i];
            unit.PlaybackCompleted -= HandleTextPlaybackCompleted;
            unit.PlaybackCompleted += HandleTextPlaybackCompleted;
        }
    }

    void ResolveSelectUnits()
    {
        if (selectUnits == null)
            selectUnits = new List<OSMemorySelectUnit>();

        selectUnits.RemoveAll(unit => unit == null);
        Transform selectArea = transform.Find("SelectArea");
        if (selectArea != null)
        {
            OSMemorySelectUnit[] resolved =
                selectArea.GetComponentsInChildren<OSMemorySelectUnit>(true);
            for (int i = 0; i < resolved.Length; i++)
            {
                if (!selectUnits.Contains(resolved[i]))
                    selectUnits.Add(resolved[i]);
            }
        }

        selectUnits.Sort(CompareSiblingOrder);
    }

    void ClearTextUnits()
    {
        for (int i = 0; i < textUnits.Count; i++)
            textUnits[i]?.Clear();
    }

    void ClearSelectUnits()
    {
        for (int i = 0; i < selectUnits.Count; i++)
            selectUnits[i]?.Clear();
    }


    void HandleTextPlaybackCompleted(
        OSMemoryTextUnit unit,
        OSMemoryReadData data)
    {
        if (unit != currentTextUnit || data == null)
            return;

        isCurrentTextComplete = true;
        ForceTextPoolLayout();
        if (isFollowingLatest)
            AlignLatestToBottom();

        CaptureLayoutSnapshot();
        if (currentData != null)
            BindSelectUnits(currentData);

        CurrentTextCompleted?.Invoke(currentData);
        TryStartSubwindowSequence();
        RefreshContinueTip();
    }

    void TryStartSubwindowSequence()
    {
        RefreshChapter02Display();
        if (isBindingData || !isActiveAndEnabled || !isCurrentTextComplete
            || !IsSubwindowPresentationPending || presentationFailed
            || window == null || !window.IsOpen || window.IsMinimized)
            return;
        OSMemorySubwindowSequence sequence = currentData.SubwindowSequence;
        GameDataManager data = GameDataManager.Instance;
        if (debugPresentationIndex != currentData.DataIndex
            && !string.IsNullOrEmpty(sequence.RequiredToolProgram)
            && !string.Equals(debugPresentationProgramId, sequence.RequiredToolProgram,
                StringComparison.Ordinal)
            && (data == null || !data.IsToolProgramInstalled(sequence.RequiredToolProgram)))
            return;

        // Upgrade 3 illustrations accompany the prose. Fast reading can reach
        // the next shot before the previous one closes; replace a released shot
        // instead of making the next paragraph wait for its remaining animation.
        // Earlier cinematic sequences keep their existing completion flow.
        if (IsSubwindowSequencePlaying
            && (sequence.RequiredToolProgram != OSToolProgramIds.MemoryRepair03
                || sequence.AutoAdvance || sequence.ContinueAtProgress > 0f
                || IsSubwindowInputLocked))
            return;

        ClearSelectUnits();
        HideContinueTip();
        playingPresentationIndex = currentData.DataIndex;
        if (subwindowPlayer == null || !subwindowPlayer.TryPlay(sequence))
        {
            playingPresentationIndex = OSMemoryData.InvalidDataIndex;
            presentationFailed = true;
            Debug.LogError("[OS Memory] Cannot start subwindow sequence for node "
                + currentData.DataIndex + ". Check its asset and scene pool.", this);
        }
    }

    int GetChapter02DisplayEntry(int index)
    {
        if (index >= 4320 && index <= 4350)
            return 4320;
        if (OSMemoryMapPresentation.ContainsNode(index))
            return OSMemoryMapPresentation.EntryNode;
        if (OSMemoryWarehouseDoorPresentation.ContainsNode(index) && !warehouseDoorCompleted)
            return OSMemoryWarehouseDoorPresentation.EntryNode;
        if (index == OSMemoryData.Chapter02CollectionWaitingDataIndex)
            return OSMemoryData.Chapter02CollectionWaitingDataIndex;
        // Cart pause: she stops at 5970 and walks on at 6010, so the still stays up in between.
        if (index >= 5970 && index <= 6000)
            return 5970;
        if (OSMemoryFaceRepairPresentation.ContainsNode(index))
            return OSMemoryFaceRepairPresentation.EntryNode;
        return OSMemoryData.InvalidDataIndex;
    }

    void RefreshChapter02Display()
    {
        GameDataManager data = GameDataManager.Instance;
        int index = pendingDebugDataIndex != OSMemoryData.InvalidDataIndex
            ? pendingDebugDataIndex : data != null ? data.CurrentOSMemoryDataIndex : OSMemoryData.InvalidDataIndex;
        int entryIndex = GetChapter02DisplayEntry(index);
        bool inSection = data != null && data.CurrentChapterIndex == 2 && data.IsMemoryRepair03Installed
            && entryIndex != OSMemoryData.InvalidDataIndex;
        OSMemorySubwindowSequence sequence = null;
        if (inSection && data.TryGetOSMemoryData(entryIndex, out OSMemoryReadData entry))
            sequence = entry.SubwindowSequence;

        if (sequence == null || !sequence.KeepOpenUntilCancelled
            || !isActiveAndEnabled || window == null || !window.IsOpen || window.IsMinimized || window.IsClosing)
        {
            if (subwindowPlayer != null && subwindowPlayer.IsPersistentDisplay)
                subwindowPlayer.Cancel();
            return;
        }
        if (subwindowPlayer == null || presentationFailed
            || subwindowPlayer.CurrentSequence == sequence
            || (subwindowPlayer.IsPlaying && !subwindowPlayer.IsPersistentDisplay))
            return;
        if (!string.IsNullOrEmpty(sequence.RequiredToolProgram)
            && !data.IsToolProgramInstalled(sequence.RequiredToolProgram))
            return;

        // Resolve each section's authored content for reopen/load at any cursor
        // inside it. Content owns looping or holding its final animation frame.
        // No history flag, minimum reading time or additional save field is needed.
        // Do not clear options, mark a presentation pending, or wait for text completion.
        playingPresentationIndex = OSMemoryData.InvalidDataIndex;
        if (!subwindowPlayer.TryPlay(sequence))
        {
            presentationFailed = true;
            Debug.LogError("[OS Memory] Cannot open the chapter 2 illustration from node " + entryIndex + ".", this);
        }
    }

    void HandleSubwindowShotOpened(OSMemorySubwindowSequence sequence, int shotIndex)
    {
        if (shotIndex != 0 || playingPresentationIndex != 1420
            || currentData == null || currentData.SubwindowSequence != sequence)
            return;
        if (GameDataManager.Instance?.TryApplyChapter01MirrorHeartCost() == true)
            OS_Handle.Instance?.PlayHeartResourceShake();
    }

    void HandleSubwindowContinuationReady(OSMemorySubwindowSequence sequence)
    {
        if (currentData == null || currentData.DataIndex != playingPresentationIndex
            || currentData.SubwindowSequence != sequence
            || completedPresentationIndex == currentData.DataIndex)
            return;
        completedPresentationIndex = currentData.DataIndex;
        presentationCompletedFrame = Time.frameCount;
        if (sequence.AutoAdvance && currentData.Options.Count == 0
            && currentData.NextDataIndex != OSMemoryData.InvalidDataIndex)
        {
            ShowData(currentData.NextDataIndex, true, true);
            return;
        }
        BindSelectUnits(currentData);
        RefreshContinueTip();
    }

    void RefreshContinueTip()
    {
        if (chapter04OutroFrozen)
        {
            HideContinueTip();
            if (repairRequiredTip != null) repairRequiredTip.gameObject.SetActive(false);
            return;
        }
        if (repairRequiredTip != null)
        {
            repairRequiredTip.gameObject.SetActive(isCurrentTextComplete && IsWaitingForToolRepair
                && currentData.Options.Count > 0);
            if (repairRequiredTip.gameObject.activeSelf)
            {
                OSLocalizedFontUtility.ApplyCurrent(repairRequiredTip);
                repairRequiredTip.transform.SetAsFirstSibling();
                GameDataManager data = GameDataManager.Instance;
                foreach (OSMemoryReadOptionData option in currentData.Options)
                {
                    string missingId = data.GetMissingOSMemoryToolProgram(option.Actions);
                    if (string.IsNullOrEmpty(missingId))
                        continue;
                    string programName = data.OSToolProgramData != null
                        && data.OSToolProgramData.TryGetProgram(missingId, out var program)
                        ? program.DisplayName : missingId;
                    repairRequiredTip.text = string.Format(data.GetLocalizedText(
                        "ui.os.memory.option_requires_program", "\u9700\u8981\u5b89\u88c5 {0} \u624d\u53ef\u9009\u62e9"), programName);
                    break;
                }
            }
        }
        if (CanContinue)
            ShowContinueTip();
        else
            HideContinueTip();
    }

    void HandleToolProgramInstalled(string programId)
    {
        if (currentData != null && isCurrentTextComplete)
            BindSelectUnits(currentData);
        RefreshContinueTip();
    }

    void HandleWindowOpenStarted(OSWindowUnit opened)
    {
        historyWheel.Clear();
        interactiveEntryReadyTime = -1f;
        RefreshInteractiveWindowLayout(true);
        // Prepare persistent content before saved child-window shells can reopen.
        RefreshChapter02Display();
    }

    void RefreshInteractiveWindowLayout(bool beforeOpeningAnimation = false)
    {
        if (chapter04OutroFrozen) return;
        GameDataManager data = GameDataManager.Instance;
        if (window == null || data == null || (!beforeOpeningAnimation && window.IsOpening))
            return;

        // Progress can change while Memory is closed, before its content initializes again.
        interactivePicWindow?.GetComponent<OSMemoryPicController>()?.NotifyMemoryNode(data.CurrentOSMemoryDataIndex);
        if (data.IsChapter03HomeActive)
        {
            bool entering = !chapter03TextLayoutApplied;
            if (entering)
            {
                window.ClearTemporaryOpeningLayout();
                window.SetTemporaryOpeningLayout(new Vector2(175f, 280f),
                    new Vector2(-222f, 12f), new Vector2(175f, 280f));
                window.ConfigureInteraction(true, false);
                chapter03TextLayoutApplied = true;
                breakfastTextLayoutApplied = breakfastImageLayoutApplied = false;
            }
            interactivePicWindow?.CloseSilently();
            if (entering || beforeOpeningAnimation)
            {
                window.SetAlwaysOnTop(data.OSProcessData.memory.interactiveWindowPinned);
                chapter03Home?.PrepareForOpen();
            }
            return;
        }
        if (chapter03TextLayoutApplied)
        {
            window.ClearTemporaryOpeningLayout();
            chapter03TextLayoutApplied = false;
            chapter03Home?.Suspend();
        }
        if (data.UsesMemoryInteractiveLayout)
        {
            if (breakfastTextLayoutApplied)
            {
                window.ClearTemporaryOpeningLayout();
                breakfastTextLayoutApplied = false;
            }
            bool entering = !window.HasTemporaryOpeningLayout;
            if (entering)
            {
                window.SetTemporaryOpeningLayout(new Vector2(175f, 280f),
                    new Vector2(-222f, 12f), new Vector2(175f, 280f));
                window.ConfigureInteraction(true, false);
            }
            if (entering || beforeOpeningAnimation)
                window.SetAlwaysOnTop(data.OSProcessData.memory.interactiveWindowPinned);
        }
        else
        {
            interactivePicWindow?.CloseSilently();
            // Only authored illustration ranges shift the prose aside. Other
            // chapter 2 lines stay centered, including the list after the map.
            bool breakfast = data.CurrentChapterIndex == 2 && data.IsMemoryRepair03Installed
                && ((data.CurrentOSMemoryDataIndex >= 4320 && data.CurrentOSMemoryDataIndex <= 4760)
                    || OSMemoryWarehouseDoorPresentation.ContainsNode(data.CurrentOSMemoryDataIndex)
                    || data.CurrentOSMemoryDataIndex == OSMemoryData.Chapter02CollectionWaitingDataIndex
                    || (data.CurrentOSMemoryDataIndex >= 5970 && data.CurrentOSMemoryDataIndex <= 6000)
                    || OSMemoryFaceRepairPresentation.ContainsNode(data.CurrentOSMemoryDataIndex));
            if (breakfast)
            {
                bool showImage = GetChapter02DisplayEntry(data.CurrentOSMemoryDataIndex) != OSMemoryData.InvalidDataIndex;
                bool entering = !breakfastTextLayoutApplied || breakfastImageLayoutApplied != showImage;
                if (entering)
                {
                    window.ClearTemporaryOpeningLayout();
                    window.SetTemporaryOpeningLayout(new Vector2(200f, 120f),
                        showImage ? new Vector2(-88f, 12f) : ordinaryWindowPosition,
                        showImage ? new Vector2(340f, 280f) : ordinaryWindowSize);
                    window.ConfigureInteraction(true, true);
                    breakfastTextLayoutApplied = true;
                    breakfastImageLayoutApplied = showImage;
                }
                if (entering || beforeOpeningAnimation)
                    window.SetAlwaysOnTop(data.OSProcessData.memory.interactiveWindowPinned);
            }
            else
            {
                window.ClearTemporaryOpeningLayout();
                breakfastTextLayoutApplied = false;
                breakfastImageLayoutApplied = false;
            }
        }
    }

    void HandleWindowOpenAnimationCompleted(OSWindowUnit opened)
    {
        RefreshInteractiveWindowLayout();
        if (IsAwaitingInteractiveEntry)
            interactiveEntryReadyTime = Time.unscaledTime + 0.2f;
    }

    void TryAutoEnterInteractiveScene()
    {
        OS_Handle os = OS_Handle.Instance;
        if (!IsAwaitingInteractiveEntry || window == null || !window.IsOpen
            || window.IsOpening || window.IsClosing || window.IsMinimized
            || (os != null && (!os.IsOSVisible || os.IsSystemInputLocked)))
        {
            interactiveEntryReadyTime = -1f;
            return;
        }

        // A minimized window restores without playing its opening animation again.
        if (interactiveEntryReadyTime < 0f)
            interactiveEntryReadyTime = Time.unscaledTime + 0.2f;
        if (Time.unscaledTime < interactiveEntryReadyTime || !isCurrentTextComplete)
            return;

        interactiveEntryReadyTime = -1f;
        ShowData(currentData.NextDataIndex, true, true);
    }

    void HandleAlwaysOnTopToggled(OSWindowUnit source, bool pinned)
    {
        GameDataManager data = GameDataManager.Instance;
        if (source.IsOpen && data != null
            && (data.UsesMemoryInteractiveLayout || breakfastTextLayoutApplied || chapter03TextLayoutApplied))
            data.OSProcessData.memory.interactiveWindowPinned = pinned;
    }

    void RefreshInteractivePicWindow()
    {
        if (window == null || !window.IsOpen
            || window.IsOpening || window.IsMinimized)
            return;

        GameDataManager data = GameDataManager.Instance;
        if (interactivePicWindow == null)
            return;
        if (data?.IsChapter03HomeActive == true)
        {
            interactivePicWindow.CloseSilently();
            return;
        }
        if (data?.IsMemoryInteractiveSceneActive == true)
        {
            if (!interactivePicWindow.IsOpen)
                interactivePicWindow.OpenStandalone();
        }
        else if (interactivePicWindow.IsOpen)
            interactivePicWindow.CloseSilently();
    }

    void HandleSubwindowSequenceCompleted(OSMemorySubwindowSequence sequence)
    {
        if (sequence.SequenceId == OSMemoryWarehouseDoorPresentation.SequenceId)
        {
            warehouseDoorCompleted = true;
            // The image is fully closed before the prose returns to center.
            RefreshInteractiveWindowLayout();
        }
        // A continuation callback can already have started another sequence (progress=1).
        if (IsSubwindowSequencePlaying)
            return;
        playingPresentationIndex = OSMemoryData.InvalidDataIndex;
        // Fast reading may reach another illustration while the previous one is exiting.
        TryStartSubwindowSequence();
        RefreshContinueTip();
    }

    bool CanReceiveInput()
    {
        if (chapter04OutroFrozen || GameTestConsole.BlocksGameplayInput)
            return false;
        OS_Handle os = OS_Handle.Instance;
        if (!isActiveAndEnabled || window == null || !window.IsOpen || window.IsMinimized
            || window.IsOpening || window.IsClosing || os?.Chapter03Entry?.BlocksMemory == true
            || (os != null && (!os.IsOSVisible || os.IsSystemInputLocked || !os.IsWindowMouseActive(window))))
            return false;
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        TMPro.TMP_InputField input = selected != null ? selected.GetComponentInParent<TMPro.TMP_InputField>() : null;
        return input == null || !input.isFocused;
    }

    void ShowContinueTip()
    {
        if (chapter04OutroFrozen || isContinueTipVisible
            || continueTip == null
            || continueTipRect == null)
        {
            return;
        }

        KillContinueTipTweens();
        isContinueTipVisible = true;
        SetContinueTipAlpha(0f);
        SetContinueTipAnchoredY(continueTipTopY);

        continueTipFadeTween = continueTip
            .DOFade(1f, Mathf.Max(0f, continueTipFadeDuration))
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);

        continueTipMoveTween = continueTipRect
            .DOAnchorPosY(
                continueTipBottomY,
                Mathf.Max(0.01f, continueTipMoveDuration))
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    void HideContinueTip()
    {
        KillContinueTipTweens();
        isContinueTipVisible = false;
        SetContinueTipAlpha(0f);
        SetContinueTipAnchoredY(continueTipTopY);
    }

    void KillContinueTipTweens()
    {
        if (continueTipFadeTween != null)
        {
            continueTipFadeTween.Kill(false);
            continueTipFadeTween = null;
        }

        if (continueTipMoveTween != null)
        {
            continueTipMoveTween.Kill(false);
            continueTipMoveTween = null;
        }
    }

    void SetContinueTipAlpha(float alpha)
    {
        if (continueTip == null)
            return;

        Color color = continueTip.color;
        color.a = Mathf.Clamp01(alpha);
        continueTip.color = color;
    }

    void SetContinueTipAnchoredY(float anchoredY)
    {
        if (continueTipRect == null)
            return;

        Vector2 position = continueTipRect.anchoredPosition;
        position.y = anchoredY;
        continueTipRect.anchoredPosition = position;
    }

    static bool IsAdvanceInputBlocked(GameObject target)
    {
        if (target == null)
            return false;

        if (target.GetComponentInParent<OSWindowButton>(true) != null
            || target.GetComponentInParent<OSMemoryPicController>(true) != null
            || target.GetComponentInParent<OSHomeView>(true) != null
            || target.GetComponentInParent<OSMemorySelectUnit>(true)
                != null)
        {
            return true;
        }

        OSWindowUnit window =
            target.GetComponentInParent<OSWindowUnit>(true);
        return window != null && window.IsDragAreaTarget(target);
    }

    void WarnMissingDataManager()
    {
        if (hasWarnedMissingDataManager)
            return;

        hasWarnedMissingDataManager = true;
        Debug.LogWarning(
            "[OS Memory] GameDataManager is missing; memory progress cannot be initialized.",
            this);
    }

    static int CompareSiblingOrder(Component left, Component right)
    {
        return left.transform.GetSiblingIndex()
            .CompareTo(right.transform.GetSiblingIndex());
    }

    void Reset()
    {
        ResolveReferences();
        HideContinueTip();
    }
}
