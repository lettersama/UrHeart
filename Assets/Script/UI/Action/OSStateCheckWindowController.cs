using System;
using System.Collections.Generic;
using MrTGame.Narrative;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed partial class OSStateCheckWindowController : MonoBehaviour,
    IPointerClickHandler
{
    const int UnitCount = 16;
    const int StartIndex = 0;
    const int TargetIndex = 15;
    const int InitialPoweredNodeIndex = 1;
    const int InitialBoardMask = (1 << StartIndex) | (1 << InitialPoweredNodeIndex) | (1 << TargetIndex);
    const int ExpectedLineCount = 24;
    const string NormalTipLocalizationKey =
        "ui.os.state_check.tip.normal";
    const string NormalTipFallbackText = "一切系统正常运行中";

    const int ChapterSlotCount = 5;
    const int Chapter01RecordSequence = 1;
    const float InforCollapsedBottomPadding = 130f;
    const float SafeTipCollapsedBottomPadding = 80f;
    const string RepairPendingLocalizationKey =
        "ui.os.state_check.chapter.repair_pending";
    const string RepairPendingFallback = "待修复";
    const string StartRepairLocalizationKey =
        "ui.os.state_check.chapter.start_repair";
    const string StartRepairFallback = "开始修复";
    const string GameProcessShaderName = "UI/OS/ProcessBlocks";
    const string ChapterRecordShaderName = "UI/OS/ChapterRecord";
    const string ChapterLockSplitShaderName = "UI/OS/LockHeartSplit";
    const string NodeCheckSfxKey = "Error_Check";

    const string FaultScrambleCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    static readonly int[] FaultNumberPattern =
    {
        482731,
        105968,
        837204,
        264159,
        710436,
        391825
    };

    // Each mask includes the clicked node. Directed links are intentional.
    static readonly int[] NodeToggleMasks =
    {
        0,
        (1 << 1) | (1 << 2),
        (1 << 2) | (1 << 6),
        0,
        (1 << 4) | (1 << 5),
        (1 << 1) | (1 << 5),
        (1 << 5) | (1 << 6) | (1 << 7),
        (1 << 6) | (1 << 7),
        (1 << 4) | (1 << 8),
        (1 << 5) | (1 << 9),
        0,
        (1 << 7) | (1 << 11),
        0,
        (1 << 9) | (1 << 13),
        (1 << 13) | (1 << 14),
        0
    };

    static readonly int OffColorId = Shader.PropertyToID("_OffColor");
    static readonly int OnColorId = Shader.PropertyToID("_OnColor");
    static readonly int ChargeId = Shader.PropertyToID("_Charge");
    static readonly int StateId = Shader.PropertyToID("_State");
    static readonly int NodeModeId = Shader.PropertyToID("_NodeMode");
    static readonly int LocalRectId = Shader.PropertyToID("_LocalRect");
    static readonly int SpriteUvRectId =
        Shader.PropertyToID("_SpriteUvRect");
    static readonly int AtlasTexelSizeId =
        Shader.PropertyToID("_AtlasTexelSize");
    static readonly int ClosedUvRectId =
        Shader.PropertyToID("_ClosedUvRect");
    static readonly int OpenUvRectId =
        Shader.PropertyToID("_OpenUvRect");
    static readonly int FlowStartId = Shader.PropertyToID("_FlowStart");
    static readonly int FlowEndId = Shader.PropertyToID("_FlowEnd");
    static readonly int GlitchRateId = Shader.PropertyToID("_GlitchRate");
    static readonly int GlitchAmountId =
        Shader.PropertyToID("_GlitchAmount");
    static readonly int GameProcessProgressId =
        Shader.PropertyToID("_Progress");
    static readonly int GameProcessBlockCountId =
        Shader.PropertyToID("_BlockCount");
    static readonly int ChapterGlitchRowsId =
        Shader.PropertyToID("_GlitchRows");
    static readonly int ChapterGlitchPixelsId =
        Shader.PropertyToID("_GlitchPixels");
    static readonly int ChapterDissolveId =
        Shader.PropertyToID("_Dissolve");
    static readonly int ChapterColorRevealId =
        Shader.PropertyToID("_ColorReveal");
    static readonly int ChapterRevealFromColorId =
        Shader.PropertyToID("_RevealFromColor");
    static readonly int ChapterRevealBlockPixelsId =
        Shader.PropertyToID("_RevealBlockPixels");
    static readonly int LockSplitUvRectId = Shader.PropertyToID("_UVRect");
    static readonly int LockSplitSpriteSizeId =
        Shader.PropertyToID("_SpriteSize");
    static readonly int LockSplitYId = Shader.PropertyToID("_SplitY");
    static readonly int LockSplitPartId = Shader.PropertyToID("_Part");


    [Header("Window")]
    [SerializeField] OSWindowUnit window;
    [SerializeField] CanvasGroup inforPartGroup;
    [SerializeField] CanvasGroup errorPartGroup;
    [SerializeField] CanvasGroup recordUnitPartGroup;
    [SerializeField] TMP_Text normalTip;
    [SerializeField] OSLocalizedText normalTipLocalizedText;
    [SerializeField, Min(0f)] float contentFadeDuration = 0.1f;
    [SerializeField, Min(0.1f)] float tipDotInterval = 0.45f;

    [Header("Information")]
    [SerializeField] GameObject inforErrorObject;
    [SerializeField] GameObject inforNormalObject;
    [SerializeField] GameObject safeTipErrorObject;
    [SerializeField] GameObject safeTipNormalObject;
    [SerializeField] Image errorTipImage;
    [SerializeField] TMP_Text inforErrorText;
    [SerializeField] TMP_Text safeTipErrorText;
    [SerializeField] TMP_Text errorNumberText;
    [SerializeField] Material errorGlitchMaterialTemplate;
    [SerializeField, Range(4, 20)] int faultGlitchFramesPerSecond = 10;
    [SerializeField, Min(0.1f)] float faultScrambleDuration = 0.7f;
    [SerializeField, Min(0f)] float faultResolvedHoldDuration = 0.2f;
    [SerializeField, Min(0f)] float faultTextPhaseOffset = 0.2f;
    [SerializeField, Range(0f, 1f)] float faultBlockIntensity = 0.78f;

    [Header("Normal Information")]
    [SerializeField] TMP_Text gameTimeText;
    [SerializeField] TMP_Text gameProcessNumberText;
    [SerializeField] Image gameProcessImage;
    [SerializeField] Material gameProcessMaterialTemplate;
    [SerializeField, Min(1)] int gameProcessBlockCount = 70;
    [SerializeField, Min(0.1f)]
    float normalInformationRefreshInterval = 1f;

    [Header("Chapter Records")]
    [SerializeField] RectTransform recordPools;
    [SerializeField] RectTransform processLine;
    [SerializeField] TMP_Text chapterInformationNameText;
    [SerializeField] TMP_Text chapterInformationDetailsText;
    [SerializeField] Material chapterRecordEffectMaterialTemplate;
    [SerializeField] Color chapterAwaitingUnitColor =
        new Color32(0x8e, 0x85, 0x84, 0xff);
    [SerializeField] Color chapterLockedUnitColor =
        new Color32(0x48, 0x45, 0x44, 0xff);
    [SerializeField] Color chapterAwaitingNameColor =
        new Color32(0xa4, 0x9e, 0x98, 0xff);
    [SerializeField] Color chapterLockedNameColor =
        new Color32(0x39, 0x36, 0x33, 0xff);
    [SerializeField, Min(0f)]
    float chapterLockedNameFontSizeReduction = 1f;
    [SerializeField, Range(0f, 1f)]
    float chapterLockedGlitchAmount = 0.22f;
    [SerializeField, Min(1f)] float chapterLockedGlitchRate = 7f;
    [SerializeField, Min(1f)] float chapterLockedGlitchRows = 18f;
    [SerializeField, Min(0f)] float chapterLockedGlitchPixels = 1.25f;
    [SerializeField, Min(1f)] float awaitingLockScale = 1.4f;
    [SerializeField, Min(0f)] float lockShakeIntervalMin = 3f;
    [SerializeField, Min(0f)] float lockShakeIntervalMax = 5f;
    [SerializeField, Min(0.01f)] float lockShakeDuration = 0.45f;
    [SerializeField, Min(0f)] float lockShakeRotation = 8f;
    [FormerlySerializedAs("lockDissolveDuration")]
    [SerializeField, Min(0.05f)] float lockOpenDuration = 0.45f;
    [SerializeField, Min(0f)] float lockOpenDropDistance = 8f;
    [SerializeField, Min(0f)] float lockOpenRotation = 20f;
    [SerializeField, Min(0f)] float chapterUnitRevealDelay = 0.15f;
    [SerializeField, Min(0.05f)] float chapterUnitRevealDuration = 0.45f;
    [SerializeField, Range(1f, 8f)] float chapterUnitRevealBlockPixels = 2f;
    [SerializeField, Min(0.05f)] float chapterActivationDuration = 0.3f;
    [SerializeField, Range(0f, 0.25f)]
    float chapterActivationPulseScale = 0.08f;


    [Header("Grid")]
    [SerializeField] RectTransform lineGrid;
    [SerializeField] RectTransform unitGrid;
    [SerializeField] Sprite closedUnitSprite;
    [SerializeField] Sprite openUnitSprite;

    [Header("Target Heartbeat")]
    [SerializeField] RectTransform targetHeart;
    [SerializeField, Min(1f)] float targetHeartStrongScale = 1.16f;
    [SerializeField, Min(1f)] float targetHeartWeakScale = 1.12f;
    [SerializeField, Min(0.42f)] float targetHeartbeatInterval = 1f;

    [Header("Charge")]
    [SerializeField] Material chargeMaterialTemplate;
    [SerializeField] Color unpoweredColor =
        new Color32(0x4b, 0x48, 0x46, 0xff);
    [SerializeField] Color poweredColor =
        new Color32(0xc7, 0xb2, 0x1f, 0xff);
    [FormerlySerializedAs("chargeDuration")]
    [SerializeField, Min(0.01f)] float nodeChargeDuration = 0.15f;
    [SerializeField, Min(0.01f)] float lineChargeDuration = 0.12f;
    [SerializeField, Min(0f)] float propagationStep = 0.06f;
    [SerializeField, Min(0.01f)] float chargeSpeedRampDuration = 1f;
    [SerializeField, Min(1f)] float chargeSpeedEndMultiplier = 1.2f;
    [SerializeField, Min(0.01f)] float stateTransitionDuration = 0.22f;

    enum ChapterRecordState
    {
        Completed,
        Playing,
        AwaitingUnlock,
        Locked
    }

    struct ChapterVisualTemplate
    {
        public Sprite RecordBorderSprite;
        public Sprite LinkPointSprite;
        public Color NumberColor;
        public Color ChapterNameColor;
        public float ChapterNameFontSize;
        public float ChapterNameFontSizeMax;
    }

    sealed class LineState
    {
        public int From;
        public int To;
        public Image Image;
        public Material Material;
        public Tween Tween;
        public float TargetCharge;
        public int FlowSource = -1;
    }

    sealed class RuntimeGraphic
    {
        public Image Image;
        public Material OriginalMaterial;
        public Color OriginalColor;
        public Sprite OriginalSprite;
        public Material RuntimeMaterial;
    }

    readonly Image[] unitImages = new Image[UnitCount];
    readonly Material[] unitMaterials = new Material[UnitCount];
    readonly Tween[] unitTweens = new Tween[UnitCount];
    readonly Tween[] unitStateTweens = new Tween[UnitCount];
    readonly bool[] unitOpen = new bool[UnitCount];
    readonly bool[] unitPowered = new bool[UnitCount];
    readonly int[] powerDistance = new int[UnitCount];
    readonly int[] visualParentUnit = new int[UnitCount];
    readonly int[] visualParentLine = new int[UnitCount];
    readonly int[] unitFlowSource = new int[UnitCount];
    readonly float[] visualReadyAt = new float[UnitCount];
    readonly float[] visualChargeStart = new float[UnitCount];
    readonly bool[] visualVisited = new bool[UnitCount];
    readonly List<LineState> lines = new List<LineState>();

    readonly RectTransform[] chapterRecordUnits =
        new RectTransform[ChapterSlotCount];
    readonly Image[] chapterUnitImages = new Image[ChapterSlotCount];
    readonly Image[] chapterRecordBorders = new Image[ChapterSlotCount];
    readonly TMP_Text[] chapterNumberTexts =
        new TMP_Text[ChapterSlotCount];
    readonly Image[] chapterLinkPointImages =
        new Image[ChapterSlotCount];
    readonly TMP_Text[] chapterNameTexts =
        new TMP_Text[ChapterSlotCount];
    readonly OSLocalizedText[] chapterNameLocalizedTexts =
        new OSLocalizedText[ChapterSlotCount];
    readonly Image[] chapterLockImages = new Image[ChapterSlotCount];
    readonly Image[] chapterActiveTips = new Image[ChapterSlotCount];
    readonly Sprite[] chapterUnitFallbackSprites =
        new Sprite[ChapterSlotCount];
    readonly Material[] chapterUnitOriginalMaterials =
        new Material[ChapterSlotCount];
    readonly Material[] chapterUnitGlitchMaterials =
        new Material[ChapterSlotCount];
    readonly Material[] chapterLockOriginalMaterials =
        new Material[ChapterSlotCount];
    readonly Material[] chapterLockDissolveMaterials =
        new Material[ChapterSlotCount];
    readonly Color[] chapterUnitOriginalColors =
        new Color[ChapterSlotCount];
    readonly Color[] chapterLockOriginalColors =
        new Color[ChapterSlotCount];
    readonly Vector3[] chapterLockBaseScales =
        new Vector3[ChapterSlotCount];
    readonly Quaternion[] chapterLockBaseRotations =
        new Quaternion[ChapterSlotCount];
    readonly Vector3[] chapterRecordBaseScales =
        new Vector3[ChapterSlotCount];
    readonly bool[] chapterVisualReferencesCached =
        new bool[ChapterSlotCount];
    readonly ChapterRecordState[] chapterRecordStates =
        new ChapterRecordState[ChapterSlotCount];
    readonly Tween[] chapterLockDelayTweens =
        new Tween[ChapterSlotCount];
    readonly Tween[] chapterLockShakeTweens =
        new Tween[ChapterSlotCount];
    readonly Tween[] chapterUnlockTweens =
        new Tween[ChapterSlotCount];
    readonly bool[] chapterUnlockTransitionActive =
        new bool[ChapterSlotCount];
    readonly bool[] chapterRecordPointerHovered =
        new bool[ChapterSlotCount];
    readonly GameObject[] chapterLockOpenVisuals =
        new GameObject[ChapterSlotCount];
    readonly Material[] chapterLockOpenTopMaterials =
        new Material[ChapterSlotCount];
    readonly Material[] chapterLockOpenBottomMaterials =
        new Material[ChapterSlotCount];

    readonly List<RuntimeGraphic> runtimeGraphics =
        new List<RuntimeGraphic>();

    Sequence revealSequence;
    Sequence tipSequence;
    Sequence faultSequence;
    Sequence faultSafeTipSequence;
    Sequence faultNumberSequence;
    Sequence targetHeartbeatSequence;
    Tween chargeSpeedRampTween;
    Material errorTipOriginalMaterial;
    Material errorTipRuntimeMaterial;
    string inforErrorOriginalText;
    string safeTipErrorOriginalText;
    string errorNumberOriginalText;
    string errorNumberFormat = "000000";
    float progress;
    int stageIndex;
    bool boardResolved;
    bool materialsConfigured;
    bool initializedForOpen;
    bool gameplayReady;
    bool warnedMissingBoard;
    bool warnedMissingMaterial;
    bool chargeSpeedRampActive;
    bool faultTextCached;
    bool puzzleCompleted;
    bool puzzleCompletionNotified;
    int activeChargeTweenCount;
    float chargeSpeedBonus;
    Vector3 targetHeartBaseScale;
    bool targetHeartScaleCached;
    Material gameProcessOriginalMaterial;
    Material gameProcessRuntimeMaterial;
    float normalInformationRefreshRemaining;
    long displayedPlayTimeMinutes = -1;
    int displayedGameProcessPercent = -1;

    bool chapterVisualTemplatesCached;
    ChapterVisualTemplate completedChapterVisual;
    ChapterVisualTemplate playingChapterVisual;
    ChapterVisualTemplate lockedChapterVisual;
    int displayedChapterIndex = int.MinValue;
    int displayedChapterDataIndex = int.MinValue;
    OS_Handle osHandle;
    bool warnedMissingGameProcessMaterial;
    bool warnedMissingChapterRecordMaterial;
    OSStateCheckSectionMinButton inforMinButton;
    OSStateCheckSectionMinButton safeTipMinButton;


    public float Progress => progress;
    public int StageIndex => stageIndex;
    public bool IsGameplayEnabled => gameplayReady;
    public bool IsPuzzleCompleted => puzzleCompleted;
    public float ChargeSpeedBonus => chargeSpeedBonus;
    public float ChargeSpeedMultiplier => Mathf.LerpUnclamped(
        1f,
        2f,
        chargeSpeedBonus);


    public event Action<int> ChapterStartRequested;
    public event Action<int> ChapterRecordPointerEntered;
    public event Action<int> ChapterRecordPointerExited;
    public event Action PuzzleCompleted;

    void Awake()
    {
        ResolveReferences();
        EnsureBoard();
        EnsureRuntimeMaterials();
        EnsureErrorGlitchMaterial();
        EnsureGameProcessMaterial();
        EnsureChapterRecordMaterials();
    }

    void OnEnable()
    {
        ResolveReferences();
        SubscribeWindowEvents();

        if (initializedForOpen && window != null && window.IsOpen)
            RestoreOpenPresentation();
    }

    void Update()
    {
        UpdateFaultGuidance();
        if (stageIndex < 2
            || !initializedForOpen
            || window == null
            || !window.IsOpen)
        {
            return;
        }

        normalInformationRefreshRemaining -= Time.unscaledDeltaTime;
        if (normalInformationRefreshRemaining > 0f)
            return;

        normalInformationRefreshRemaining = Mathf.Max(
            0.1f,
            normalInformationRefreshInterval);
        RefreshNormalInformation();
    }


    void OnDisable()
    {
        PersistFaultBoardInMemory();
        StopFaultGuidance();
        UnsubscribeWindowEvents();
        KillRevealSequence();
        StopTipLoop();
        StopFaultEffects(true);
        StopTargetHeartbeat();
        StopChapterRecordEffects(true);
        KillChargeTweens(true);
        KillStateTweens(true);
        NotifyPuzzleCompleted();
        gameplayReady = false;

        if (window == null || !window.IsOpen)
            initializedForOpen = false;
    }

    void OnDestroy()
    {
        StopFaultGuidance();
        UnsubscribeWindowEvents();
        KillRevealSequence();
        StopTipLoop();
        StopFaultEffects(true);
        StopTargetHeartbeat();
        StopChapterRecordEffects(true);
        KillChargeTweens(false);
        KillStateTweens(false);
        DestroyErrorGlitchMaterial();
        DestroyGameProcessMaterial();
        DestroyChapterRecordMaterials();
        DestroyRuntimeMaterials();
    }

    void Reset()
    {
        ResolveReferences();
    }

    public void PrepareRuntime()
    {
        StopFaultGuidance();
        faultGuideSeen = false;
        faultBoardInteracted = false;
        ResolveReferences();
        EnsureGameProcessMaterial();
        EnsureChapterRecordMaterials();
        if (!EnsureBoard())
            return;

        EnsureRuntimeMaterials();
        EnsureErrorGlitchMaterial();
        ResetBoard();
        faultBoardInitialized = false;
        SetBoardInteraction(false);
    }

    public void InitializeFromSave()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        progress = dataManager != null
            ? dataManager.CurrentStateCheckProgress
            : 0f;
        stageIndex = Mathf.Max(0, Mathf.RoundToInt(progress));

        ResolveReferences();
        EnsureBoard();
        EnsureRuntimeMaterials();
        EnsureErrorGlitchMaterial();
        EnsureGameProcessMaterial();
        EnsureChapterRecordMaterials();
        ResetBoard();
        RestoreFaultBoardData(dataManager?.SaveData?.osResume);
        RefreshNormalInformation(true);
    }

    public void RefreshFromSave()
    {
        ResolveReferences();
        if (window == null || !window.IsOpen)
            return;

        InitializeFromSave();
        initializedForOpen = true;
        gameplayReady = false;
        PrepareOpenPresentation();

        if (gameObject.activeInHierarchy)
            RestoreOpenPresentation();
    }

    public void CapturePersistentBoard(OSResumeSaveData saved)
    {
        if (saved == null || !faultBoardInitialized || stageIndex != 1) return;
        saved.hasFaultBoard = true;
        saved.faultBoardCompleted = puzzleCompleted;
        saved.faultGuideSeen = faultGuideSeen;
        saved.faultBoardInteracted = faultBoardInteracted;
        saved.faultBoardMask = 0;
        for (int i = 0; i < UnitCount; i++) if (unitOpen[i]) saved.faultBoardMask |= 1 << i;
    }

    public void RestorePersistentBoard(OSResumeSaveData saved)
    {
        if (saved == null || !saved.hasFaultBoard || stageIndex != 1 || !initializedForOpen) return;
        StopFaultGuidance();
        KillRevealSequence();
        RestoreFaultBoardData(saved);
        RestoreOpenPresentation();
    }

    void RestoreFaultBoardData(OSResumeSaveData saved)
    {
        if (saved == null || !saved.hasFaultBoard || stageIndex != 1) return;
        KillChargeTweens(false);
        KillStateTweens(false);
        for (int i = 0; i < UnitCount; i++)
        {
            unitOpen[i] = (saved.faultBoardMask & (1 << i)) != 0;
            ApplyUnitState(i, false);
        }
        RecalculatePower();
        puzzleCompleted = saved.faultBoardCompleted || unitPowered[TargetIndex];
        puzzleCompletionNotified = false;
        faultBoardInitialized = true;
        // Old saves with a changed board have already discovered the interaction.
        faultBoardInteracted = saved.faultBoardInteracted
            || (saved.faultBoardMask != InitialBoardMask
                && saved.faultBoardMask != ((1 << StartIndex) | (1 << TargetIndex)));
        faultGuideSeen = saved.faultGuideSeen || faultBoardInteracted || puzzleCompleted;
        ApplyPowerVisuals(false);
    }

    public bool ToggleNode(int nodeIndex)
    {
        if (!gameplayReady || !IsNormalUnit(nodeIndex))
            return false;

        faultBoardInteracted = true;
        StopFaultHint();
        int toggleMask = NodeToggleMasks[nodeIndex];
        for (int i = 0; i < UnitCount; i++)
        {
            if ((toggleMask & (1 << i)) == 0)
                continue;

            unitOpen[i] = !unitOpen[i];
            ApplyUnitState(i, true);
        }

        RecalculatePower();
        ApplyPowerVisuals(true);
        CompletePuzzleIfPowered();
        PersistFaultBoardInMemory();
        return true;
    }

    public bool IsNodeOpen(int nodeIndex)
    {
        return IsValidIndex(nodeIndex) && unitOpen[nodeIndex];
    }

    public bool IsNodePowered(int nodeIndex)
    {
        return IsValidIndex(nodeIndex) && unitPowered[nodeIndex];
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left
            || OS_Handle.Instance?.IsSystemInputLocked == true)
        {
            return;
        }

        GameObject pointerTarget =
            eventData.pointerCurrentRaycast.gameObject;
        if (pointerTarget == null)
            pointerTarget = eventData.pointerPressRaycast.gameObject;

        if (stageIndex >= 2
            && TryGetChapterSlotIndex(
                pointerTarget,
                out int chapterSequence))
        {
            HandleChapterRecordPointerClick(chapterSequence, eventData);
            return;
        }

        if (!gameplayReady)
            return;

        if (TryGetUnitIndex(pointerTarget, out int nodeIndex)
            && ToggleNode(nodeIndex))
        {
            AudioManager.Instance?.PlaySfx(NodeCheckSfxKey);
        }
    }

    internal void HandleChapterRecordPointerEnter(int chapterSequence)
    {
        if (stageIndex < 2
            || chapterSequence < 0
            || chapterSequence >= ChapterSlotCount
            || OS_Handle.Instance?.IsSystemInputLocked == true)
        {
            return;
        }

        chapterRecordPointerHovered[chapterSequence] = true;
        RefreshAwaitingLockShakeDelay(chapterSequence);
        ChapterRecordPointerEntered?.Invoke(chapterSequence);
    }

    internal void HandleChapterRecordPointerExit(int chapterSequence)
    {
        if (chapterSequence < 0 || chapterSequence >= ChapterSlotCount)
            return;

        chapterRecordPointerHovered[chapterSequence] = false;
        RefreshAwaitingLockShakeDelay(chapterSequence);
        ChapterRecordPointerExited?.Invoke(chapterSequence);
    }

    internal void HandleChapterRecordPointerClick(
        int chapterSequence,
        PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (OS_Handle.Instance?.IsSystemInputLocked == true)
        {
            eventData.Use();
            return;
        }

        ResolveReferences();
        if (osHandle != null
            && osHandle.TryHandleChapter01RecordClick(chapterSequence))
        {
            eventData.Use();
            return;
        }

        if (osHandle?.Chapter04Entry?.TryHandleChapterRecordClick(chapterSequence) == true
            || osHandle?.Chapter03Entry?.TryHandleChapterRecordClick(chapterSequence) == true)
        {
            eventData.Use();
            return;
        }

        TryStartChapter(chapterSequence);
        eventData.Use();
    }

    public bool TryGetChapterRecordTarget(
        int chapterSequence,
        out RectTransform target)
    {
        ResolveReferences();
        target = chapterSequence >= 0
                 && chapterSequence < ChapterSlotCount
            ? chapterRecordUnits[chapterSequence]
            : null;
        return target != null;
    }

    public void UnlockChapter01InformationSections()
    {
        ResolveReferences();
        SetInformationSectionGuideLocked(false, true);
    }

    public bool TryGetChapterRecordLabelTarget(int chapterSequence, out RectTransform target)
    {
        ResolveReferences();
        target = chapterSequence >= 0 && chapterSequence < ChapterSlotCount
            ? chapterNameTexts[chapterSequence]?.rectTransform : null;
        return target != null;
    }

    public bool PlayChapter01RecordUnlockPresentation()
    {
        ResolveReferences();
        int sequence = Chapter01RecordSequence;
        if (stageIndex < 2
            || chapterUnlockTransitionActive[sequence]
            || chapterRecordStates[sequence]
                != ChapterRecordState.AwaitingUnlock)
        {
            return false;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (!IsChapter01RecordPresentationActivated(dataManager)
            || dataManager == null
            || !dataManager.TryGetOSMemoryChapterProgressRange(
                sequence,
                out OSMemoryChapterProgressRange range))
        {
            return false;
        }

        BeginChapterUnlockTransition(sequence, range);
        RefreshChapterRecords(true);
        return true;
    }

    void HandleOpenStarted(OSWindowUnit source)
    {
        InitializeFromSave();
        initializedForOpen = true;
        gameplayReady = false;
        PrepareOpenPresentation();
    }

    void HandleOpenAnimationCompleted(OSWindowUnit source)
    {
        if (!initializedForOpen || source != window)
            return;

        if (stageIndex == 0)
            RevealNormalTip();
        else
            RevealProcessContent();
    }

    void PrepareOpenPresentation()
    {
        StopFaultGuidance();
        KillRevealSequence();
        StopTipLoop();
        StopFaultEffects(true);
        StopTargetHeartbeat();
        KillChargeTweens(false);
        ConfigureMaterialRects();
        ConfigureInformationVariant();
        SetCanvasGroupState(inforPartGroup, 0f, false);
        SetCanvasGroupState(errorPartGroup, 0f, false);
        SetCanvasGroupState(recordUnitPartGroup, 0f, false);
        SetBoardInteraction(false);
        SetAllChargesImmediate(0f);
        normalInformationRefreshRemaining = 0f;
        RefreshNormalInformation(true);

        if (normalTip != null)
        {
            normalTip.gameObject.SetActive(stageIndex == 0);
            SetTipText(1);
            SetTipAlpha(0f);
        }
    }

    void RestoreOpenPresentation()
    {
        StopTargetHeartbeat();
        ConfigureInformationVariant();
        if (stageIndex == 0)
        {
            SetCanvasGroupState(inforPartGroup, 0f, false);
            SetCanvasGroupState(errorPartGroup, 0f, false);
            SetCanvasGroupState(recordUnitPartGroup, 0f, false);
            if (normalTip != null)
            {
                normalTip.gameObject.SetActive(true);
                SetTipAlpha(1f);
            }

            StartTipLoop();
            return;
        }

        if (normalTip != null)
            normalTip.gameObject.SetActive(false);

        bool isFaultStage = stageIndex == 1;
        bool enableGameplay = isFaultStage && !puzzleCompleted;
        SetCanvasGroupState(inforPartGroup, 1f, !isFaultStage);
        SetCanvasGroupState(
            errorPartGroup,
            isFaultStage ? 1f : 0f,
            enableGameplay);
        SetCanvasGroupState(
            recordUnitPartGroup,
            isFaultStage ? 0f : 1f,
            !isFaultStage);
        gameplayReady = enableGameplay;
        SetBoardInteraction(gameplayReady);
        if (puzzleCompleted)
            NotifyPuzzleCompleted();

        if (isFaultStage)
        {
            ApplyPowerVisuals(false);
            StartFaultEffects();
            StartTargetHeartbeat();
            TryStartFaultGuide();
        }
        else
        {
            RefreshNormalInformation(true);
        }
    }

    void RevealNormalTip()
    {
        if (normalTip == null)
            return;

        normalTip.gameObject.SetActive(true);
        float duration = Mathf.Max(0f, contentFadeDuration);
        Sequence sequence = DOTween.Sequence();
        revealSequence = sequence;
        sequence.Append(DOVirtual.Float(
            0f,
            1f,
            duration,
            SetTipAlpha).SetEase(Ease.Linear));
        sequence.AppendCallback(StartTipLoop);
        LinkRevealSequence(sequence);
    }

    void RevealProcessContent()
    {
        ConfigureInformationVariant();
        if (normalTip != null)
            normalTip.gameObject.SetActive(false);

        bool isFaultStage = stageIndex == 1;
        if (!isFaultStage)
            RefreshNormalInformation(true);

        float duration = Mathf.Max(0f, contentFadeDuration);
        Sequence sequence = DOTween.Sequence();
        revealSequence = sequence;
        if (inforPartGroup != null)
        {
            sequence.Join(inforPartGroup
                .DOFade(1f, duration)
                .SetEase(Ease.Linear));
        }

        CanvasGroup secondaryGroup = isFaultStage
            ? errorPartGroup
            : recordUnitPartGroup;
        if (secondaryGroup != null)
        {
            sequence.Join(secondaryGroup
                .DOFade(1f, duration)
                .SetEase(Ease.Linear));
        }

        sequence.AppendCallback(() =>
        {
            bool enableGameplay = isFaultStage && !puzzleCompleted;
            SetCanvasGroupState(
                inforPartGroup,
                1f,
                !isFaultStage);
            SetCanvasGroupState(
                errorPartGroup,
                isFaultStage ? 1f : 0f,
                enableGameplay);
            SetCanvasGroupState(
                recordUnitPartGroup,
                isFaultStage ? 0f : 1f,
                !isFaultStage);
            gameplayReady = enableGameplay;
            SetBoardInteraction(enableGameplay);
            if (isFaultStage)
            {
                ApplyPowerVisuals(true);
                StartFaultEffects();
                StartTargetHeartbeat();
                TryStartFaultGuide();
            }
        });
        LinkRevealSequence(sequence);
    }

    void RefreshNormalInformation(bool force = false)
    {

        RefreshChapterRecords(force);
        if (stageIndex < 2)
            return;

        GameDataManager dataManager = GameDataManager.Instance;
        double playTimeSeconds = dataManager != null
            ? dataManager.TotalPlayTimeSeconds
            : 0d;
        if (double.IsNaN(playTimeSeconds)
            || double.IsInfinity(playTimeSeconds)
            || playTimeSeconds < 0d)
        {
            playTimeSeconds = 0d;
        }

        double minuteValue = Math.Floor(playTimeSeconds / 60d);
        long totalMinutes = minuteValue >= long.MaxValue
            ? long.MaxValue
            : (long)minuteValue;
        if (force || displayedPlayTimeMinutes != totalMinutes)
        {
            displayedPlayTimeMinutes = totalMinutes;
            if (gameTimeText != null)
                gameTimeText.text = FormatPlayTime(totalMinutes);
        }

        float gameProgress = dataManager != null
            ? Mathf.Clamp01(dataManager.OverallGameProgress)
            : 0f;
        int percent = Mathf.RoundToInt(gameProgress * 100f);
        if (force || displayedGameProcessPercent != percent)
        {
            displayedGameProcessPercent = percent;
            if (gameProcessNumberText != null)
            {
                gameProcessNumberText.text =
                    percent.ToString(CultureInfo.InvariantCulture) + "%";
            }
        }

        EnsureGameProcessMaterial();
        if (gameProcessRuntimeMaterial != null)
        {
            gameProcessRuntimeMaterial.SetFloat(
                GameProcessProgressId,
                gameProgress);
        }
    }

    static string FormatPlayTime(long totalMinutes)
    {
        long days = totalMinutes / (24L * 60L);
        long hours = totalMinutes / 60L % 24L;
        long minutes = totalMinutes % 60L;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00}<size=6>min</size>",
            days,
            hours,
            minutes);
    }

    public bool TryStartChapter(int chapterSequence)
    {
        if (chapterSequence == 4 && OS_Handle.Instance?.Chapter04Entry?.IsEngaged == true)
            return OS_Handle.Instance.Chapter04Entry.TryUnlockChapter();
        if (chapterSequence == 3 && OS_Handle.Instance?.Chapter03Entry?.IsEngaged == true)
            return OS_Handle.Instance.Chapter03Entry.TryUnlockChapter();
        if (stageIndex < 2
            || chapterSequence < 0
            || chapterSequence >= ChapterSlotCount)
        {
            return false;
        }

        ResolveReferences();
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null
            || !dataManager.TryGetOSMemoryChapterProgressRange(
                chapterSequence,
                out OSMemoryChapterProgressRange range)
            || ResolveChapterRecordState(
                chapterSequence,
                dataManager.CurrentChapterIndex,
                dataManager.CurrentOSMemoryDataIndex,
                range) != ChapterRecordState.AwaitingUnlock
            || osHandle == null)
        {
            return false;
        }

        bool unlockAlreadyPresented = chapterSequence
                == Chapter01RecordSequence
            && IsChapter01RecordPresentationActivated(dataManager);
        if (!osHandle.StartChapter(chapterSequence))
            return false;

        if (!unlockAlreadyPresented)
            BeginChapterUnlockTransition(chapterSequence, range);
        RefreshChapterRecords(true);
        ChapterStartRequested?.Invoke(chapterSequence);
        return true;
    }

    void RefreshChapterRecords(bool force = false)
    {
        if (stageIndex < 2)
            return;

        ResolveChapterRecordReferences();
        CacheChapterVisualTemplates();
        EnsureChapterRecordMaterials();

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        int currentChapterIndex = Mathf.Max(
            0,
            dataManager.CurrentChapterIndex);
        int recordChapterIndex = IsChapter04RecordPresentationActivated(dataManager)
            ? 4 : currentChapterIndex;
        int currentDataIndex = dataManager.CurrentOSMemoryDataIndex;
        if (!force
            && displayedChapterIndex == recordChapterIndex
            && displayedChapterDataIndex == currentDataIndex)
        {
            return;
        }

        displayedChapterIndex = recordChapterIndex;
        displayedChapterDataIndex = currentDataIndex;
        for (int sequence = 0; sequence < ChapterSlotCount; sequence++)
        {
            dataManager.TryGetOSMemoryChapterProgressRange(
                sequence,
                out OSMemoryChapterProgressRange range);
            ChapterRecordState state = ResolveChapterRecordState(
                sequence,
                currentChapterIndex,
                currentDataIndex,
                range);
            if (state == ChapterRecordState.AwaitingUnlock
                && sequence == Chapter01RecordSequence
                && IsChapter01RecordPresentationActivated(dataManager))
            {
                state = ChapterRecordState.Playing;
            }
            ApplyChapterRecordVisual(sequence, state, range);
        }

        dataManager.TryGetOSMemoryChapterProgressRange(
            recordChapterIndex,
            out OSMemoryChapterProgressRange currentRange);
        RefreshChapterInformation(currentRange);
    }

    // The card click is persisted before installation. Gameplay remains in
    // chapter three until TV opens, but the record has already moved forward.
    static bool IsChapter04RecordPresentationActivated(GameDataManager dataManager) =>
        dataManager != null && dataManager.CurrentChapterIndex == 3
        && dataManager.OSProcessData.memory.chapter04EntryStep
            >= (int)OSChapter04EntryDirector.EntryStep.Prepare;

    static bool IsChapter01RecordPresentationActivated(
        GameDataManager dataManager)
    {
        OSCharacterInteractionSaveData interaction =
            dataManager?.OSCharacterInteractionState;
        return dataManager != null
            && dataManager.CurrentChapterIndex == Chapter01RecordSequence
            && interaction != null
            && interaction.HasConsumedEntry(
                OSCharacterInteractionIds.Chapter01Entries
                    .RecordStartPrompt);
    }

    static ChapterRecordState ResolveChapterRecordState(
        int chapterSequence,
        int currentChapterIndex,
        int currentDataIndex,
        OSMemoryChapterProgressRange range)
    {
        if (chapterSequence == 4 && currentChapterIndex == 3)
        {
            int step = GameDataManager.Instance?.OSProcessData.memory.chapter04EntryStep ?? 0;
            if (step >= (int)OSChapter04EntryDirector.EntryStep.Prepare) return ChapterRecordState.Playing;
            if (step >= (int)OSChapter04EntryDirector.EntryStep.OpenState) return ChapterRecordState.AwaitingUnlock;
        }
        if (chapterSequence == 4 && currentChapterIndex == 4
            && GameDataManager.Instance?.OSProcessData.memory.chapter04EntryStep >= (int)OSChapter04EntryDirector.EntryStep.Complete)
            return ChapterRecordState.Playing;
        if (chapterSequence == 3 && currentChapterIndex == 3
            && IsChapter04RecordPresentationActivated(GameDataManager.Instance))
            return ChapterRecordState.Completed;
        if (chapterSequence < currentChapterIndex)
            return ChapterRecordState.Completed;
        if (chapterSequence > currentChapterIndex)
            return ChapterRecordState.Locked;
        if (chapterSequence == 3 && currentChapterIndex == 3
            && GameDataManager.Instance?.OSProcessData.memory.chapter03EntryStep
                >= (int)OSChapter03EntryDirector.EntryStep.UpgradeOffer)
            return ChapterRecordState.Playing;
        if (range != null
            && range.HasMemoryDataRange
            && range.Contains(currentDataIndex))
        {
            return ChapterRecordState.Playing;
        }

        return ChapterRecordState.AwaitingUnlock;
    }

    public bool PlayChapter03RecordUnlockPresentation()
    {
        ResolveReferences();
        if (stageIndex < 2 || chapterUnlockTransitionActive[3]
            || GameDataManager.Instance == null
            || !GameDataManager.Instance.TryGetOSMemoryChapterProgressRange(3, out var range)) return false;
        BeginChapterUnlockTransition(3, range);
        RefreshChapterRecords(true);
        return true;
    }

    public bool PlayChapter04RecordUnlockPresentation()
    {
        ResolveReferences();
        if (stageIndex < 2 || chapterUnlockTransitionActive[4]
            || GameDataManager.Instance == null
            || !GameDataManager.Instance.TryGetOSMemoryChapterProgressRange(4, out var range)) return false;
        BeginChapterUnlockTransition(4, range);
        RefreshChapterRecords(true);
        return true;
    }

    void ApplyChapterRecordVisual(
        int sequence,
        ChapterRecordState state,
        OSMemoryChapterProgressRange range)
    {
        if (sequence < 0 || sequence >= ChapterSlotCount)
            return;

        ChapterVisualTemplate visual = state
            == ChapterRecordState.Completed
                ? completedChapterVisual
                : state == ChapterRecordState.Playing
                    ? playingChapterVisual
                    : lockedChapterVisual;
        bool deferActivatedVisual = chapterUnlockTransitionActive[sequence]
            && state == ChapterRecordState.Playing;

        if (!deferActivatedVisual)
        {
            if (chapterRecordBorders[sequence] != null)
                chapterRecordBorders[sequence].sprite =
                    visual.RecordBorderSprite;
            if (chapterNumberTexts[sequence] != null)
                chapterNumberTexts[sequence].color = visual.NumberColor;
            if (chapterLinkPointImages[sequence] != null)
                chapterLinkPointImages[sequence].sprite =
                    visual.LinkPointSprite;
            ApplyChapterNameVisual(sequence, state, visual);
        }

        Image unitImage = chapterUnitImages[sequence];
        if (unitImage != null)
        {
            unitImage.sprite = range != null
                && range.ChapterIcon != null
                    ? range.ChapterIcon
                    : chapterUnitFallbackSprites[sequence];
        }

        chapterRecordStates[sequence] = state;
        if (!deferActivatedVisual)
            ApplyChapterUnitImageVisual(sequence, state);
        ApplyChapterLockVisual(sequence, state);
        ApplyChapterActiveTipVisual(sequence, state);

        string key;
        string fallback;
        if (state == ChapterRecordState.AwaitingUnlock)
        {
            key = StartRepairLocalizationKey;
            fallback = StartRepairFallback;
        }
        else if (state == ChapterRecordState.Locked)
        {
            key = RepairPendingLocalizationKey;
            fallback = RepairPendingFallback;
        }
        else
        {
            key = range != null
                ? OSMemoryLocalizationKeys.GetChapterNameKey(
                    range.ChapterId)
                : string.Empty;
            fallback = range != null
                ? range.ChapterNameText
                : string.Empty;
        }

        if (!deferActivatedVisual)
        {
            ApplyLocalizedText(
                chapterNameTexts[sequence],
                chapterNameLocalizedTexts[sequence],
                key,
                fallback);
        }
    }

    void ApplyChapterNameVisual(
        int sequence,
        ChapterRecordState state,
        ChapterVisualTemplate visual)
    {
        TMP_Text chapterName = chapterNameTexts[sequence];
        if (chapterName == null)
            return;

        chapterName.color = state == ChapterRecordState.AwaitingUnlock
            ? chapterAwaitingNameColor
            : state == ChapterRecordState.Locked
                ? chapterLockedNameColor
                : visual.ChapterNameColor;

        float reduction = state == ChapterRecordState.Locked
            ? Mathf.Max(0f, chapterLockedNameFontSizeReduction)
            : 0f;
        if (chapterName.enableAutoSizing)
        {
            float baseMaximum = state == ChapterRecordState.Locked
                ? Mathf.Min(
                    visual.ChapterNameFontSize,
                    visual.ChapterNameFontSizeMax)
                : visual.ChapterNameFontSizeMax;
            chapterName.fontSizeMax = Mathf.Max(
                chapterName.fontSizeMin,
                baseMaximum - reduction);
            return;
        }

        chapterName.fontSize = Mathf.Max(
            1f,
            visual.ChapterNameFontSize - reduction);
    }

    void RefreshChapterInformation(
        OSMemoryChapterProgressRange range)
    {
        string nameKey = range != null
            ? OSMemoryLocalizationKeys.GetChapterNameKey(range.ChapterId)
            : string.Empty;
        string summaryKey = range != null
            ? OSMemoryLocalizationKeys.GetChapterSummaryKey(range.ChapterId)
            : string.Empty;
        ApplyLocalizedText(
            chapterInformationNameText,
            chapterInformationNameText != null
                ? chapterInformationNameText
                    .GetComponent<OSLocalizedText>()
                : null,
            nameKey,
            range != null ? range.ChapterNameText : string.Empty);
        ApplyLocalizedText(
            chapterInformationDetailsText,
            chapterInformationDetailsText != null
                ? chapterInformationDetailsText
                    .GetComponent<OSLocalizedText>()
                : null,
            summaryKey,
            range != null ? range.ChapterSummaryText : string.Empty);
    }

    void ApplyLocalizedText(
        TMP_Text target,
        OSLocalizedText localizedText,
        string key,
        string fallback)
    {
        if (target == null)
            return;

        if (localizedText != null)
        {
            localizedText.SetLocalization(
                key,
                fallback,
                osHandle);
            return;
        }

        target.text = osHandle != null
            ? osHandle.GetLocalizedText(key, fallback)
            : fallback ?? string.Empty;
    }

    void ResolveChapterRecordReferences()
    {
        if (recordPools == null)
        {
            recordPools = transform.Find(
                "RecordUnitPart/SelectPanel/RecordPools")
                as RectTransform;
        }

        if (processLine == null)
        {
            processLine = transform.Find(
                "RecordUnitPart/SelectPanel/ProcessLine")
                as RectTransform;
        }

        Transform chapterInformation = transform.Find(
            "RecordUnitPart/ChapterInfor");
        if (chapterInformationNameText == null)
        {
            chapterInformationNameText = chapterInformation
                ?.Find("ChapterName")
                ?.GetComponent<TMP_Text>();
        }

        if (chapterInformationDetailsText == null)
        {
            chapterInformationDetailsText = chapterInformation
                ?.Find("ChapterDetails")
                ?.GetComponent<TMP_Text>();
        }

        for (int i = 0; i < ChapterSlotCount; i++)
        {
            Transform recordUnit = recordPools != null
                && i < recordPools.childCount
                    ? recordPools.GetChild(i)
                    : null;
            Transform linkPoint = processLine != null
                && i < processLine.childCount
                    ? processLine.GetChild(i)
                    : null;
            chapterRecordUnits[i] = recordUnit as RectTransform;
            if (Application.isPlaying && recordUnit != null)
            {
                OSStateCheckChapterRecordRaycast relay = recordUnit
                    .GetComponent<OSStateCheckChapterRecordRaycast>();
                if (relay == null)
                {
                    relay = recordUnit.gameObject
                        .AddComponent<OSStateCheckChapterRecordRaycast>();
                }

                relay.Initialize(this, i);
            }

            chapterUnitImages[i] = recordUnit
                ?.Find("UnitImage")
                ?.GetComponent<Image>();
            chapterRecordBorders[i] = recordUnit
                ?.Find("RecordBord")
                ?.GetComponent<Image>();
            chapterNumberTexts[i] = recordUnit
                ?.Find("Num")
                ?.GetComponent<TMP_Text>();
            chapterLockImages[i] = recordUnit
                ?.Find("Lock")
                ?.GetComponent<Image>();
            Transform activeTip = recordUnit?.Find("ActiveTip")
                ?? recordUnit?.Find("AcitveTip");
            chapterActiveTips[i] = activeTip?.GetComponent<Image>();
            chapterLinkPointImages[i] =
                linkPoint?.GetComponent<Image>();
            chapterNameTexts[i] = linkPoint
                ?.Find("ChapterName")
                ?.GetComponent<TMP_Text>();
            chapterNameLocalizedTexts[i] =
                chapterNameTexts[i] != null
                    ? chapterNameTexts[i]
                        .GetComponent<OSLocalizedText>()
                    : null;

            Image unitImage = chapterUnitImages[i];
            Image lockImage = chapterLockImages[i];
            if (lockImage != null)
                lockImage.raycastTarget = false;
            if (chapterActiveTips[i] != null)
                chapterActiveTips[i].raycastTarget = false;
            if (chapterVisualReferencesCached[i]
                || unitImage == null
                || lockImage == null)
            {
                continue;
            }

            chapterUnitFallbackSprites[i] = unitImage.sprite;
            chapterUnitOriginalMaterials[i] = unitImage.material;
            chapterLockOriginalMaterials[i] = lockImage.material;
            chapterUnitOriginalColors[i] = unitImage.color;
            chapterLockOriginalColors[i] = lockImage.color;
            chapterLockBaseScales[i] =
                lockImage.rectTransform.localScale;
            chapterLockBaseRotations[i] =
                lockImage.rectTransform.localRotation;
            chapterRecordBaseScales[i] = recordUnit.localScale;
            chapterVisualReferencesCached[i] = true;
        }
    }

    void CacheChapterVisualTemplates()
    {
        if (chapterVisualTemplatesCached)
            return;

        if (!TryCaptureChapterVisual(0, out completedChapterVisual)
            || !TryCaptureChapterVisual(1, out playingChapterVisual)
            || !TryCaptureChapterVisual(2, out lockedChapterVisual))
        {
            return;
        }

        chapterVisualTemplatesCached = true;
    }

    bool TryCaptureChapterVisual(
        int sequence,
        out ChapterVisualTemplate visual)
    {
        visual = default;
        if (sequence < 0
            || sequence >= ChapterSlotCount
            || chapterRecordBorders[sequence] == null
            || chapterNumberTexts[sequence] == null
            || chapterLinkPointImages[sequence] == null
            || chapterNameTexts[sequence] == null)
        {
            return false;
        }

        visual.RecordBorderSprite =
            chapterRecordBorders[sequence].sprite;
        visual.LinkPointSprite =
            chapterLinkPointImages[sequence].sprite;
        visual.NumberColor = chapterNumberTexts[sequence].color;
        visual.ChapterNameColor =
            chapterNameTexts[sequence].color;
        visual.ChapterNameFontSize =
            chapterNameTexts[sequence].fontSize;
        visual.ChapterNameFontSizeMax =
            chapterNameTexts[sequence].fontSizeMax;
        return true;
    }

    void EnsureChapterRecordMaterials()
    {
        ResolveChapterRecordReferences();

        Material source = chapterRecordEffectMaterialTemplate;
        if (source != null
            && (source.shader == null
                || source.shader.name != ChapterRecordShaderName))
        {
            source = null;
        }

        if (source == null)
        {
            Shader shader = Shader.Find(ChapterRecordShaderName);
            if (shader != null)
                source = new Material(shader);
        }

        if (source == null)
        {
            if (!warnedMissingChapterRecordMaterial)
            {
                warnedMissingChapterRecordMaterial = true;
                Debug.LogWarning(
                    "[StateCheck] Chapter record effect material is missing.",
                    this);
            }

            return;
        }

        bool destroyTemporarySource =
            source != chapterRecordEffectMaterialTemplate;
        for (int i = 0; i < ChapterSlotCount; i++)
        {
            if (chapterUnitImages[i] != null
                && chapterUnitGlitchMaterials[i] == null)
            {
                Material material = new Material(source);
                material.name = "Chapter Unit Glitch "
                    + i
                    + " (Runtime)";
                material.hideFlags = HideFlags.HideAndDontSave;
                material.SetFloat(ChapterColorRevealId, 1f);
                chapterUnitGlitchMaterials[i] = material;
            }

            if (chapterLockImages[i] != null
                && chapterLockDissolveMaterials[i] == null)
            {
                Material material = new Material(source);
                material.name = "Chapter Lock Dissolve "
                    + i
                    + " (Runtime)";
                material.hideFlags = HideFlags.HideAndDontSave;
                material.SetFloat(GlitchAmountId, 0f);
                material.SetFloat(ChapterDissolveId, 0f);
                material.SetFloat(ChapterColorRevealId, 1f);
                chapterLockDissolveMaterials[i] = material;
                ConfigureChapterRecordMaterial(
                    material,
                    chapterLockImages[i]);
            }
        }

        if (destroyTemporarySource)
        {
            if (Application.isPlaying)
                Destroy(source);
            else
                DestroyImmediate(source);
        }

        warnedMissingChapterRecordMaterial = false;
    }

    void ApplyChapterUnitImageVisual(
        int sequence,
        ChapterRecordState state)
    {
        Image image = chapterUnitImages[sequence];
        if (image == null)
            return;

        Material effectMaterial = chapterUnitGlitchMaterials[sequence];
        if (effectMaterial != null)
            effectMaterial.SetFloat(ChapterColorRevealId, 1f);

        image.color = state == ChapterRecordState.AwaitingUnlock
            ? chapterAwaitingUnitColor
            : state == ChapterRecordState.Locked
                ? chapterLockedUnitColor
                : Color.white;

        if (state != ChapterRecordState.Locked)
        {
            image.material = chapterUnitOriginalMaterials[sequence];
            return;
        }

        EnsureChapterRecordMaterials();
        Material material = effectMaterial;
        if (material == null)
        {
            image.material = chapterUnitOriginalMaterials[sequence];
            return;
        }

        material.SetFloat(
            GlitchAmountId,
            Mathf.Clamp01(chapterLockedGlitchAmount));
        material.SetFloat(
            GlitchRateId,
            Mathf.Max(1f, chapterLockedGlitchRate));
        material.SetFloat(
            ChapterGlitchRowsId,
            Mathf.Max(1f, chapterLockedGlitchRows));
        material.SetFloat(
            ChapterGlitchPixelsId,
            Mathf.Max(0f, chapterLockedGlitchPixels));
        material.SetFloat(ChapterDissolveId, 0f);
        ConfigureChapterRecordMaterial(material, image);
        image.material = material;
    }

    void ApplyChapterLockVisual(
        int sequence,
        ChapterRecordState state)
    {
        Image image = chapterLockImages[sequence];
        if (image == null || chapterUnlockTransitionActive[sequence])
            return;

        StopChapterLockIdleEffects(sequence, true);
        Tween unlockTween = chapterUnlockTweens[sequence];
        chapterUnlockTweens[sequence] = null;
        unlockTween?.Kill(false);
        DestroyChapterLockOpenVisual(sequence);

        bool visible = state == ChapterRecordState.AwaitingUnlock
            || state == ChapterRecordState.Locked;
        RectTransform rectTransform = image.rectTransform;
        if (chapterVisualReferencesCached[sequence])
        {
            rectTransform.localScale =
                chapterLockBaseScales[sequence]
                * (state == ChapterRecordState.AwaitingUnlock
                    ? Mathf.Max(1f, awaitingLockScale)
                    : 1f);
            rectTransform.localRotation =
                chapterLockBaseRotations[sequence];
        }

        Material material = chapterLockDissolveMaterials[sequence];
        if (visible && material != null)
        {
            material.SetFloat(GlitchAmountId, 0f);
            material.SetFloat(ChapterDissolveId, 0f);
            ConfigureChapterRecordMaterial(material, image);
            image.material = material;
        }
        else
        {
            image.material = chapterLockOriginalMaterials[sequence];
        }

        image.raycastTarget = false;
        image.color = state == ChapterRecordState.AwaitingUnlock
            ? Color.white
            : state == ChapterRecordState.Locked
                ? chapterLockedUnitColor
                : new Color(1f, 1f, 1f, 0f);

        if (state == ChapterRecordState.AwaitingUnlock)
            ScheduleAwaitingLockShake(sequence);
    }

    void ApplyChapterActiveTipVisual(
        int sequence,
        ChapterRecordState state)
    {
        Image image = chapterActiveTips[sequence];
        if (image == null)
            return;

        Color color = image.color;
        color.a = state == ChapterRecordState.AwaitingUnlock ? 1f : 0f;
        image.color = color;
        image.raycastTarget = false;
    }

    void ConfigureChapterRecordMaterial(
        Material material,
        Image image)
    {
        if (material == null || image == null)
            return;

        Vector4 spriteRect = new Vector4(0f, 0f, 1f, 1f);
        Sprite sprite = image.sprite;
        if (TryGetSpriteUvRect(sprite, out Vector4 resolvedRect))
            spriteRect = resolvedRect;
        material.SetVector(SpriteUvRectId, spriteRect);

        Texture texture = sprite != null
            ? sprite.texture
            : image.mainTexture;
        if (texture == null)
            return;

        float width = Mathf.Max(1f, texture.width);
        float height = Mathf.Max(1f, texture.height);
        material.SetVector(
            AtlasTexelSizeId,
            new Vector4(1f / width, 1f / height, width, height));
        if (material.HasProperty(ChapterRevealBlockPixelsId))
        {
            material.SetFloat(
                ChapterRevealBlockPixelsId,
                Mathf.Clamp(chapterUnitRevealBlockPixels, 1f, 8f));
        }
    }

    void ScheduleAwaitingLockShake(int sequence)
    {
        if (sequence < 0
            || sequence >= ChapterSlotCount
            || chapterRecordStates[sequence]
                != ChapterRecordState.AwaitingUnlock
            || chapterUnlockTransitionActive[sequence]
            || !isActiveAndEnabled)
        {
            return;
        }

        float minimum = Mathf.Max(0f, lockShakeIntervalMin);
        float maximum = Mathf.Max(minimum, lockShakeIntervalMax);
        float intervalMultiplier = chapterRecordPointerHovered[sequence]
            ? 0.5f
            : 1f;
        minimum *= intervalMultiplier;
        maximum *= intervalMultiplier;
        float delay = UnityEngine.Random.Range(minimum, maximum);
        Tween delayTween = null;
        delayTween = DOVirtual.DelayedCall(
                delay,
                () =>
                {
                    if (chapterLockDelayTweens[sequence] != delayTween)
                        return;

                    chapterLockDelayTweens[sequence] = null;
                    PlayAwaitingLockShake(sequence);
                },
                true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        chapterLockDelayTweens[sequence] = delayTween;
    }

    void RefreshAwaitingLockShakeDelay(int sequence)
    {
        if (chapterRecordStates[sequence]
                != ChapterRecordState.AwaitingUnlock
            || chapterUnlockTransitionActive[sequence]
            || chapterLockShakeTweens[sequence] != null)
        {
            return;
        }

        Tween delayTween = chapterLockDelayTweens[sequence];
        chapterLockDelayTweens[sequence] = null;
        delayTween?.Kill(false);
        ScheduleAwaitingLockShake(sequence);
    }

    void PlayAwaitingLockShake(int sequence)
    {
        if (chapterRecordStates[sequence]
                != ChapterRecordState.AwaitingUnlock
            || chapterUnlockTransitionActive[sequence])
        {
            return;
        }

        Image image = chapterLockImages[sequence];
        if (image == null)
            return;

        RectTransform rectTransform = image.rectTransform;
        if (chapterVisualReferencesCached[sequence])
        {
            rectTransform.localRotation =
                chapterLockBaseRotations[sequence];
        }

        Tween shakeTween = null;
        shakeTween = rectTransform
            .DOShakeRotation(
                Mathf.Max(0.01f, lockShakeDuration),
                new Vector3(
                    0f,
                    0f,
                    Mathf.Max(0f, lockShakeRotation)),
                10,
                32f,
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (chapterLockShakeTweens[sequence] != shakeTween)
                    return;

                chapterLockShakeTweens[sequence] = null;
                if (chapterVisualReferencesCached[sequence])
                {
                    rectTransform.localRotation =
                        chapterLockBaseRotations[sequence];
                }

                ScheduleAwaitingLockShake(sequence);
            });
        chapterLockShakeTweens[sequence] = shakeTween;
    }

    void StopChapterLockIdleEffects(
        int sequence,
        bool restoreRotation)
    {
        Tween delayTween = chapterLockDelayTweens[sequence];
        chapterLockDelayTweens[sequence] = null;
        delayTween?.Kill(false);

        Tween shakeTween = chapterLockShakeTweens[sequence];
        chapterLockShakeTweens[sequence] = null;
        shakeTween?.Kill(false);

        if (!restoreRotation
            || !chapterVisualReferencesCached[sequence]
            || chapterLockImages[sequence] == null)
        {
            return;
        }

        chapterLockImages[sequence].rectTransform.localRotation =
            chapterLockBaseRotations[sequence];
    }

    void BeginChapterUnlockTransition(
        int sequence,
        OSMemoryChapterProgressRange range)
    {
        if (sequence < 0 || sequence >= ChapterSlotCount)
            return;

        ResolveChapterRecordReferences();
        CacheChapterVisualTemplates();
        EnsureChapterRecordMaterials();
        StopChapterLockIdleEffects(sequence, true);

        bool transitionWasActive = chapterUnlockTransitionActive[sequence];
        Tween previousTween = chapterUnlockTweens[sequence];
        chapterUnlockTweens[sequence] = null;
        previousTween?.Kill(false);
        if (transitionWasActive)
            RestoreChapterUnlockTransitionVisual(sequence, false);
        else
            DestroyChapterLockOpenVisual(sequence);

        chapterUnlockTransitionActive[sequence] = true;
        chapterRecordPointerHovered[sequence] = false;
        SetChapterActiveTipAlpha(sequence, 0f);

        float openDuration = Mathf.Max(0.05f, lockOpenDuration);
        float revealDelay = Mathf.Max(0f, chapterUnitRevealDelay);
        float revealDuration = Mathf.Max(0.05f, chapterUnitRevealDuration);
        Sequence unlockSequence = DOTween.Sequence();
        chapterUnlockTweens[sequence] = unlockSequence;

        if (!AddChapterLockOpenAnimation(
                unlockSequence,
                sequence,
                openDuration))
        {
            Image lockImage = chapterLockImages[sequence];
            if (lockImage != null)
            {
                lockImage.gameObject.SetActive(true);
                lockImage.color = Color.white;
                unlockSequence.Insert(
                    0f,
                    lockImage
                        .DOFade(0f, openDuration)
                        .SetEase(Ease.InQuad));
            }
        }

        AddChapterUnitRevealAnimation(
            unlockSequence,
            sequence,
            revealDelay,
            revealDuration);
        AddChapterActivationAnimation(
            unlockSequence,
            sequence,
            range,
            revealDelay);

        float minimumDuration = Mathf.Max(
            openDuration,
            revealDelay + revealDuration);
        if (unlockSequence.Duration() < minimumDuration)
            unlockSequence.AppendInterval(minimumDuration - unlockSequence.Duration());

        unlockSequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (chapterUnlockTweens[sequence] != unlockSequence)
                    return;

                chapterUnlockTweens[sequence] = null;
                CompleteChapterUnlockTransition(sequence);
            })
            .OnKill(() =>
            {
                if (chapterUnlockTweens[sequence] != unlockSequence)
                    return;

                chapterUnlockTweens[sequence] = null;
                RestoreChapterUnlockTransitionVisual(sequence, false);
            });
    }

    bool AddChapterLockOpenAnimation(
        Sequence sequence,
        int chapterSequence,
        float duration)
    {
        Image source = chapterLockImages[chapterSequence];
        Shader shader = Shader.Find(ChapterLockSplitShaderName);
        if (sequence == null
            || source == null
            || source.sprite == null
            || shader == null)
        {
            return false;
        }

        RectTransform sourceRect = source.rectTransform;
        RectTransform parent = sourceRect.parent as RectTransform;
        if (parent == null)
            return false;

        GameObject visualObject = new GameObject(
            $"ChapterLockOpenVisual_{chapterSequence}",
            typeof(RectTransform),
            typeof(CanvasGroup));
        visualObject.layer = source.gameObject.layer;
        RectTransform visualRect = visualObject.GetComponent<RectTransform>();
        visualRect.SetParent(parent, false);
        visualRect.anchorMin = sourceRect.anchorMin;
        visualRect.anchorMax = sourceRect.anchorMax;
        visualRect.pivot = sourceRect.pivot;
        visualRect.anchoredPosition = sourceRect.anchoredPosition;
        visualRect.sizeDelta = sourceRect.rect.size;
        visualRect.localRotation = sourceRect.localRotation;
        visualRect.localScale = sourceRect.localScale;
        visualRect.SetSiblingIndex(sourceRect.GetSiblingIndex() + 1);

        CanvasGroup group = visualObject.GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;
        chapterLockOpenVisuals[chapterSequence] = visualObject;

        Vector2 size = sourceRect.rect.size;
        if (size.x <= 0.01f || size.y <= 0.01f)
            size = sourceRect.sizeDelta;
        float topHeight = Mathf.Min(6f, Mathf.Max(1f, size.y));
        float splitY = Mathf.Clamp01(
            1f - topHeight / Mathf.Max(1f, size.y));
        float topPivotY = splitY + (1f - splitY) * 0.5f;
        float bottomPivotY = splitY * 0.5f;

        RectTransform topPiece = CreateChapterLockPiece(
            "Top",
            visualRect,
            source,
            shader,
            size,
            new Vector2(0f, topPivotY),
            splitY,
            true,
            out Material topMaterial);
        RectTransform bottomPiece = CreateChapterLockPiece(
            "Bottom",
            visualRect,
            source,
            shader,
            size,
            new Vector2(0f, bottomPivotY),
            splitY,
            false,
            out Material bottomMaterial);
        chapterLockOpenTopMaterials[chapterSequence] = topMaterial;
        chapterLockOpenBottomMaterials[chapterSequence] = bottomMaterial;
        if (topPiece == null && bottomPiece == null)
        {
            DestroyChapterLockOpenVisual(chapterSequence);
            return false;
        }

        source.gameObject.SetActive(false);
        float rotation = Mathf.Max(0f, lockOpenRotation);
        if (topPiece != null)
        {
            sequence.Insert(
                0f,
                topPiece
                    .DOLocalRotate(new Vector3(0f, 0f, rotation), duration)
                    .SetEase(Ease.OutQuad));
        }
        if (bottomPiece != null)
        {
            sequence.Insert(
                0f,
                bottomPiece
                    .DOLocalRotate(new Vector3(0f, 0f, -rotation), duration)
                    .SetEase(Ease.OutQuad));
        }

        sequence.Insert(
            0f,
            visualRect
                .DOAnchorPosY(
                    visualRect.anchoredPosition.y
                        - Mathf.Max(0f, lockOpenDropDistance),
                    duration)
                .SetEase(Ease.InQuad));
        sequence.Insert(
            0f,
            group.DOFade(0f, duration).SetEase(Ease.InQuad));
        return true;
    }

    RectTransform CreateChapterLockPiece(
        string pieceName,
        RectTransform parent,
        Image source,
        Shader shader,
        Vector2 size,
        Vector2 pivot,
        float splitY,
        bool isTop,
        out Material material)
    {
        material = null;
        if (parent == null || source == null || shader == null)
            return null;

        GameObject pieceObject = new GameObject(
            pieceName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        pieceObject.layer = source.gameObject.layer;
        RectTransform rect = pieceObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(
            (pivot.x - 0.5f) * size.x,
            (pivot.y - 0.5f) * size.y);
        rect.localScale = Vector3.one;

        Image image = pieceObject.GetComponent<Image>();
        image.sprite = source.sprite;
        image.color = source.color;
        image.type = source.type;
        image.preserveAspect = source.preserveAspect;
        image.raycastTarget = false;
        material = new Material(shader)
        {
            name = $"Chapter_{pieceName}_LockSplit_Runtime",
            hideFlags = HideFlags.HideAndDontSave
        };
        ConfigureChapterLockSplitMaterial(material, source.sprite);
        material.SetFloat(LockSplitYId, splitY);
        material.SetFloat(LockSplitPartId, isTop ? 1f : 0f);
        image.material = material;
        return rect;
    }

    static void ConfigureChapterLockSplitMaterial(
        Material material,
        Sprite sprite)
    {
        if (material == null || sprite == null || sprite.texture == null)
            return;

        Texture texture = sprite.texture;
        Rect textureRect = sprite.textureRect;
        material.SetVector(LockSplitUvRectId, new Vector4(
            textureRect.xMin / Mathf.Max(1f, texture.width),
            textureRect.yMin / Mathf.Max(1f, texture.height),
            textureRect.width / Mathf.Max(1f, texture.width),
            textureRect.height / Mathf.Max(1f, texture.height)));
        material.SetVector(LockSplitSpriteSizeId, new Vector4(
            Mathf.Max(1f, sprite.rect.width),
            Mathf.Max(1f, sprite.rect.height),
            0f,
            0f));
    }

    void AddChapterUnitRevealAnimation(
        Sequence sequence,
        int chapterSequence,
        float delay,
        float duration)
    {
        Image image = chapterUnitImages[chapterSequence];
        if (sequence == null || image == null)
            return;

        Material material = chapterUnitGlitchMaterials[chapterSequence];
        if (material == null || !material.HasProperty(ChapterColorRevealId))
        {
            sequence.Insert(
                delay,
                image.DOColor(Color.white, duration).SetEase(Ease.OutQuad));
            return;
        }

        material.SetFloat(GlitchAmountId, 0f);
        material.SetFloat(ChapterDissolveId, 0f);
        material.SetFloat(ChapterColorRevealId, 0f);
        material.SetColor(ChapterRevealFromColorId, chapterAwaitingUnitColor);
        ConfigureChapterRecordMaterial(material, image);
        image.color = Color.white;
        image.material = material;
        sequence.Insert(
            delay,
            DOVirtual.Float(
                    0f,
                    1f,
                    duration,
                    value => material.SetFloat(ChapterColorRevealId, value))
                .SetEase(Ease.Linear));
    }

    void AddChapterActivationAnimation(
        Sequence sequence,
        int chapterSequence,
        OSMemoryChapterProgressRange range,
        float delay)
    {
        if (sequence == null)
            return;

        sequence.InsertCallback(
            delay,
            () => ApplyChapterActivatedSupportingVisual(
                chapterSequence,
                range));

        float duration = Mathf.Max(0.05f, chapterActivationDuration);
        TMP_Text number = chapterNumberTexts[chapterSequence];
        if (number != null)
        {
            sequence.Insert(
                delay,
                number
                    .DOColor(playingChapterVisual.NumberColor, duration)
                    .SetEase(Ease.OutQuad));
        }

        TMP_Text chapterName = chapterNameTexts[chapterSequence];
        if (chapterName != null)
        {
            sequence.Insert(
                delay,
                chapterName
                    .DOColor(playingChapterVisual.ChapterNameColor, duration)
                    .SetEase(Ease.OutQuad));
        }

        RectTransform record = chapterRecordUnits[chapterSequence];
        if (record == null || !chapterVisualReferencesCached[chapterSequence])
            return;

        Vector3 baseScale = chapterRecordBaseScales[chapterSequence];
        Vector3 punch = new Vector3(
            baseScale.x * chapterActivationPulseScale,
            baseScale.y * chapterActivationPulseScale,
            0f);
        sequence.Insert(
            delay,
            record.DOPunchScale(punch, duration, 5, 0.3f));
    }

    void ApplyChapterActivatedSupportingVisual(
        int chapterSequence,
        OSMemoryChapterProgressRange range)
    {
        if (chapterRecordBorders[chapterSequence] != null)
        {
            chapterRecordBorders[chapterSequence].sprite =
                playingChapterVisual.RecordBorderSprite;
        }
        if (chapterLinkPointImages[chapterSequence] != null)
        {
            chapterLinkPointImages[chapterSequence].sprite =
                playingChapterVisual.LinkPointSprite;
        }

        ApplyLocalizedText(
            chapterNameTexts[chapterSequence],
            chapterNameLocalizedTexts[chapterSequence],
            range != null
                ? OSMemoryLocalizationKeys.GetChapterNameKey(range.ChapterId)
                : string.Empty,
            range != null ? range.ChapterNameText : string.Empty);
    }

    void CompleteChapterUnlockTransition(int sequence)
    {
        RestoreChapterUnlockTransitionVisual(sequence, true);
        if (isActiveAndEnabled)
            RefreshChapterRecords(true);
    }

    void RestoreChapterUnlockTransitionVisual(
        int sequence,
        bool chapterIsActivated)
    {
        chapterUnlockTransitionActive[sequence] = false;
        DestroyChapterLockOpenVisual(sequence);

        Material unitMaterial = chapterUnitGlitchMaterials[sequence];
        if (unitMaterial != null
            && unitMaterial.HasProperty(ChapterColorRevealId))
        {
            unitMaterial.SetFloat(ChapterColorRevealId, 1f);
        }

        Image unitImage = chapterUnitImages[sequence];
        if (unitImage != null)
        {
            unitImage.material = chapterUnitOriginalMaterials[sequence];
            if (chapterIsActivated)
                unitImage.color = Color.white;
        }

        Image lockImage = chapterLockImages[sequence];
        if (lockImage != null)
        {
            lockImage.gameObject.SetActive(true);
            lockImage.material = chapterLockOriginalMaterials[sequence];
            if (chapterIsActivated)
                lockImage.color = new Color(1f, 1f, 1f, 0f);
        }

        if (!chapterVisualReferencesCached[sequence])
            return;

        if (chapterRecordUnits[sequence] != null)
        {
            chapterRecordUnits[sequence].localScale =
                chapterRecordBaseScales[sequence];
        }
        if (lockImage != null)
        {
            lockImage.rectTransform.localScale =
                chapterLockBaseScales[sequence];
            lockImage.rectTransform.localRotation =
                chapterLockBaseRotations[sequence];
        }
    }

    void SetChapterActiveTipAlpha(int sequence, float alpha)
    {
        Image image = chapterActiveTips[sequence];
        if (image == null)
            return;

        Color color = image.color;
        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }

    void DestroyChapterLockOpenVisual(int sequence)
    {
        GameObject visual = chapterLockOpenVisuals[sequence];
        chapterLockOpenVisuals[sequence] = null;
        if (visual != null)
            visual.SetActive(false);
        DestroyChapterRuntimeObject(visual);

        Material topMaterial = chapterLockOpenTopMaterials[sequence];
        chapterLockOpenTopMaterials[sequence] = null;
        DestroyChapterRuntimeObject(topMaterial);

        Material bottomMaterial = chapterLockOpenBottomMaterials[sequence];
        chapterLockOpenBottomMaterials[sequence] = null;
        DestroyChapterRuntimeObject(bottomMaterial);
    }

    static void DestroyChapterRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    void StopChapterRecordEffects(bool restoreTransforms)
    {
        for (int i = 0; i < ChapterSlotCount; i++)
        {
            StopChapterLockIdleEffects(i, restoreTransforms);
            Tween unlockTween = chapterUnlockTweens[i];
            chapterUnlockTweens[i] = null;
            unlockTween?.Kill(false);
            chapterRecordPointerHovered[i] = false;
            RestoreChapterUnlockTransitionVisual(i, false);

            Material material = chapterLockDissolveMaterials[i];
            if (material != null)
                material.SetFloat(ChapterDissolveId, 0f);

            if (!restoreTransforms
                || !chapterVisualReferencesCached[i]
                || chapterLockImages[i] == null)
            {
                continue;
            }

            RectTransform rectTransform =
                chapterLockImages[i].rectTransform;
            rectTransform.localScale = chapterLockBaseScales[i];
            rectTransform.localRotation =
                chapterLockBaseRotations[i];
        }
    }

    void DestroyChapterRecordMaterials()
    {
        for (int i = 0; i < ChapterSlotCount; i++)
        {
            if (chapterVisualReferencesCached[i])
            {
                Image unitImage = chapterUnitImages[i];
                if (unitImage != null)
                {
                    unitImage.material =
                        chapterUnitOriginalMaterials[i];
                    unitImage.color = chapterUnitOriginalColors[i];
                    unitImage.sprite = chapterUnitFallbackSprites[i];
                }

                Image lockImage = chapterLockImages[i];
                if (lockImage != null)
                {
                    lockImage.material =
                        chapterLockOriginalMaterials[i];
                    lockImage.color = chapterLockOriginalColors[i];
                    lockImage.rectTransform.localScale =
                        chapterLockBaseScales[i];
                    lockImage.rectTransform.localRotation =
                        chapterLockBaseRotations[i];
                }
            }

            DestroyChapterRecordMaterial(
                chapterUnitGlitchMaterials[i]);
            DestroyChapterRecordMaterial(
                chapterLockDissolveMaterials[i]);
            chapterUnitGlitchMaterials[i] = null;
            chapterLockDissolveMaterials[i] = null;
        }
    }

    static void DestroyChapterRecordMaterial(Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }


    void EnsureGameProcessMaterial()
    {
        if (gameProcessRuntimeMaterial != null
            || gameProcessImage == null)
        {
            return;
        }

        gameProcessOriginalMaterial = gameProcessImage.material;
        Material source = gameProcessMaterialTemplate;
        if (source == null
            && gameProcessOriginalMaterial != null
            && gameProcessOriginalMaterial.shader != null
            && gameProcessOriginalMaterial.shader.name
                == GameProcessShaderName)
        {
            source = gameProcessOriginalMaterial;
        }

        if (source != null)
        {
            gameProcessRuntimeMaterial = new Material(source);
        }
        else
        {
            Shader shader = Shader.Find(GameProcessShaderName);
            if (shader != null)
                gameProcessRuntimeMaterial = new Material(shader);
        }

        if (gameProcessRuntimeMaterial == null)
        {
            if (!warnedMissingGameProcessMaterial)
            {
                warnedMissingGameProcessMaterial = true;
                Debug.LogWarning(
                    "[StateCheck] Game process material is missing.",
                    this);
            }

            return;
        }

        gameProcessRuntimeMaterial.name =
            "StateCheck Game Process (Runtime)";
        gameProcessRuntimeMaterial.hideFlags =
            HideFlags.HideAndDontSave;
        gameProcessImage.material = gameProcessRuntimeMaterial;
        warnedMissingGameProcessMaterial = false;
        ConfigureGameProcessMaterial();
    }

    void ConfigureGameProcessMaterial()
    {
        if (gameProcessRuntimeMaterial == null)
            return;

        gameProcessRuntimeMaterial.SetFloat(
            GameProcessBlockCountId,
            Mathf.Max(1, gameProcessBlockCount));
        gameProcessRuntimeMaterial.SetFloat(
            GameProcessProgressId,
            Mathf.Clamp01(displayedGameProcessPercent / 100f));
        if (TryGetSpriteUvRect(
                gameProcessImage != null
                    ? gameProcessImage.sprite
                    : null,
                out Vector4 spriteRect))
        {
            gameProcessRuntimeMaterial.SetVector(
                SpriteUvRectId,
                spriteRect);
        }
    }

    void DestroyGameProcessMaterial()
    {
        if (gameProcessImage != null
            && gameProcessImage.material == gameProcessRuntimeMaterial)
        {
            gameProcessImage.material = gameProcessOriginalMaterial;
        }

        if (gameProcessRuntimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(gameProcessRuntimeMaterial);
            else
                DestroyImmediate(gameProcessRuntimeMaterial);
        }

        gameProcessOriginalMaterial = null;
        gameProcessRuntimeMaterial = null;
    }

    void StartTargetHeartbeat()
    {
        StopTargetHeartbeat();
        if (stageIndex != 1
            || targetHeart == null
            || !targetHeart.gameObject.activeInHierarchy)
        {
            return;
        }

        CacheTargetHeartScale();
        bool healthy = faultGuideHeartPowered || unitPowered[TargetIndex];
        ApplyFaultHeartColor(healthy);
        Vector3 strongScale = ScaleTargetHeart(healthy ? targetHeartStrongScale : 1.035f);
        Vector3 weakScale = ScaleTargetHeart(healthy ? targetHeartWeakScale : 1.015f);
        float restDuration = Mathf.Max(
            0f,
            (healthy ? targetHeartbeatInterval : 2.2f) - 0.42f);

        Sequence sequence = DOTween.Sequence();
        targetHeartbeatSequence = sequence;
        sequence
            .Append(targetHeart
                .DOScale(strongScale, 0.09f)
                .SetEase(Ease.OutCubic))
            .Append(targetHeart
                .DOScale(targetHeartBaseScale, 0.1f)
                .SetEase(Ease.InOutSine))
            .AppendInterval(0.05f)
            .Append(targetHeart
                .DOScale(weakScale, 0.07f)
                .SetEase(Ease.OutCubic))
            .Append(targetHeart
                .DOScale(targetHeartBaseScale, 0.11f)
                .SetEase(Ease.OutSine))
            .AppendInterval(restDuration)
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (targetHeartbeatSequence == sequence)
                    targetHeartbeatSequence = null;
                ResetTargetHeartScale();
            });
    }

    void StopTargetHeartbeat()
    {
        if (targetHeartbeatSequence != null)
        {
            Sequence sequence = targetHeartbeatSequence;
            targetHeartbeatSequence = null;
            sequence.Kill(false);
        }

        ResetTargetHeartScale();
    }

    void CacheTargetHeartScale()
    {
        if (targetHeart == null || targetHeartScaleCached)
            return;

        targetHeartBaseScale = targetHeart.localScale;
        targetHeartScaleCached = true;
    }

    void ResetTargetHeartScale()
    {
        if (targetHeart != null && targetHeartScaleCached)
            targetHeart.localScale = targetHeartBaseScale;
    }

    Vector3 ScaleTargetHeart(float multiplier)
    {
        return new Vector3(
            targetHeartBaseScale.x * multiplier,
            targetHeartBaseScale.y * multiplier,
            targetHeartBaseScale.z);
    }

    void ConfigureInformationVariant()
    {
        bool showError = stageIndex == 1;
        bool showNormal = stageIndex >= 2;
        SetObjectActive(inforErrorObject, showError);
        SetObjectActive(safeTipErrorObject, showError);
        SetObjectActive(inforNormalObject, showNormal);
        SetObjectActive(safeTipNormalObject, showNormal);
        SetObjectActive(
            inforMinButton != null
                ? inforMinButton.gameObject
                : null,
            showNormal);
        SetObjectActive(
            safeTipMinButton != null
                ? safeTipMinButton.gameObject
                : null,
            showNormal);
        if (showNormal)
        {
            SetInformationSectionGuideLocked(
                ShouldLockChapter01InformationSections(),
                false);
        }

        if (!showError)
            StopFaultEffects(true);
    }

    bool ShouldLockChapter01InformationSections()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        OSCharacterInteractionSaveData interaction =
            dataManager?.OSCharacterInteractionState;
        return dataManager != null
            && dataManager.CurrentChapterIndex == Chapter01RecordSequence
            && dataManager.CurrentOSMemoryDataIndex ==
                OSMemoryProcessSaveData.InvalidDataIndex
            && interaction != null
            && string.Equals(
                interaction.activeSequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal)
            && !interaction.HasConsumedEntry(
                OSCharacterInteractionIds.Chapter01Entries
                    .RecordStartPrompt);
    }

    void SetInformationSectionGuideLocked(bool locked, bool animate)
    {
        inforMinButton?.SetGuideLocked(locked, animate);
        safeTipMinButton?.SetGuideLocked(locked, animate);
    }

    void StartFaultEffects()
    {
        StopFaultEffects(true);
        if (stageIndex != 1 || !gameObject.activeInHierarchy)
            return;

        EnsureErrorGlitchMaterial();
        CacheFaultText();
        if (!faultTextCached)
            return;

        int fps = Mathf.Clamp(faultGlitchFramesPerSecond, 4, 20);
        float duration = Mathf.Max(0.1f, faultScrambleDuration);
        EaseFunction stopMotion = EaseFactory.StopMotion(
            fps,
            Ease.Linear);
        float phaseOffset = Mathf.Clamp(
            faultTextPhaseOffset,
            0f,
            duration);
        Sequence inforSequence = CreateFaultTextSequence(
            inforErrorText,
            inforErrorOriginalText,
            3,
            duration,
            stopMotion);
        faultSequence = inforSequence;
        if (inforSequence != null)
        {
            inforSequence
                .SetLoops(-1, LoopType.Restart)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .OnKill(() =>
                {
                    if (faultSequence == inforSequence)
                        faultSequence = null;
                });
        }

        Sequence safeTipSequence = CreateFaultTextSequence(
            safeTipErrorText,
            safeTipErrorOriginalText,
            11,
            duration,
            stopMotion);
        faultSafeTipSequence = safeTipSequence;
        if (safeTipSequence != null)
        {
            safeTipSequence
                .SetDelay(phaseOffset, false)
                .SetLoops(-1, LoopType.Restart)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .OnKill(() =>
                {
                    if (faultSafeTipSequence == safeTipSequence)
                        faultSafeTipSequence = null;
                });
        }

        if (errorNumberText == null || FaultNumberPattern.Length == 0)
            return;

        int initialNumber = FaultNumberPattern[
            FaultNumberPattern.Length - 1];
        errorNumberText.text = initialNumber.ToString(
            errorNumberFormat,
            CultureInfo.InvariantCulture) + "%";
        Sequence numberSequence = CreateFaultNumberTween(
            duration,
            stopMotion);
        faultNumberSequence = numberSequence;
        numberSequence
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (faultNumberSequence == numberSequence)
                    faultNumberSequence = null;
            });
    }

    void StopFaultEffects(bool restoreText)
    {
        if (faultSequence != null)
        {
            Sequence sequence = faultSequence;
            faultSequence = null;
            sequence.Kill(false);
        }

        if (faultSafeTipSequence != null)
        {
            Sequence sequence = faultSafeTipSequence;
            faultSafeTipSequence = null;
            sequence.Kill(false);
        }

        if (faultNumberSequence != null)
        {
            Sequence sequence = faultNumberSequence;
            faultNumberSequence = null;
            sequence.Kill(false);
        }

        if (!restoreText || !faultTextCached)
            return;

        if (inforErrorText != null)
            inforErrorText.text = inforErrorOriginalText;
        if (safeTipErrorText != null)
            safeTipErrorText.text = safeTipErrorOriginalText;
        if (errorNumberText != null)
            errorNumberText.text = errorNumberOriginalText;
    }

    Sequence CreateFaultTextSequence(
        TMP_Text target,
        string originalText,
        int scrambleSalt,
        float duration,
        EaseFunction stopMotion)
    {
        if (target == null)
            return null;

        string scrambleSeed = BuildFaultScrambleSeed(
            originalText,
            scrambleSalt);
        Sequence sequence = DOTween.Sequence();
        sequence.Append(target
            .DOText(
                originalText,
                duration,
                false,
                ScrambleMode.Custom,
                FaultScrambleCharacters)
            .From(scrambleSeed, false, false)
            .SetEase(stopMotion));
        sequence.AppendInterval(Mathf.Max(
            0f,
            faultResolvedHoldDuration));
        return sequence;
    }

    static string BuildFaultScrambleSeed(string source, int salt)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        char[] result = source.ToCharArray();
        for (int i = 0; i < result.Length; i++)
        {
            if (char.IsWhiteSpace(result[i]))
                continue;

            int index = Mathf.Abs(i * 7 + salt * 11)
                % FaultScrambleCharacters.Length;
            result[i] = FaultScrambleCharacters[index];
        }

        return new string(result);
    }

    void FormatFaultNumberText()
    {
        if (errorNumberText == null
            || !int.TryParse(
                errorNumberText.text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            return;
        }

        errorNumberText.text = value.ToString(
            errorNumberFormat,
            CultureInfo.InvariantCulture) + "%";
    }

    void CacheFaultText()
    {
        if (faultTextCached
            || inforErrorText == null
            || safeTipErrorText == null
            || errorNumberText == null)
        {
            return;
        }

        inforErrorOriginalText = inforErrorText.text;
        safeTipErrorOriginalText = safeTipErrorText.text;
        errorNumberOriginalText = errorNumberText.text;

        string digits = string.Empty;
        for (int i = 0; i < errorNumberOriginalText.Length; i++)
        {
            char character = errorNumberOriginalText[i];
            if (char.IsDigit(character))
                digits += character;
        }

        errorNumberFormat = new string(
            '0',
            Mathf.Max(1, digits.Length));
        faultTextCached = true;
    }

    void LinkRevealSequence(Sequence sequence)
    {
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (revealSequence == sequence)
                    revealSequence = null;
            })
            .OnKill(() =>
            {
                if (revealSequence == sequence)
                    revealSequence = null;
            });
    }

    void StartTipLoop()
    {
        StopTipLoop();
        if (normalTip == null || !normalTip.gameObject.activeInHierarchy)
            return;

        float interval = Mathf.Max(0.1f, tipDotInterval);
        Sequence sequence = DOTween.Sequence();
        tipSequence = sequence;
        sequence.AppendCallback(() => SetTipText(1));
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetTipText(2));
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetTipText(3));
        sequence.AppendInterval(interval);
        sequence
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (tipSequence == sequence)
                    tipSequence = null;
            });
    }

    void StopTipLoop()
    {
        if (tipSequence == null)
            return;

        Sequence sequence = tipSequence;
        tipSequence = null;
        sequence.Kill(false);
    }

    void SetTipText(int dotCount)
    {
        if (normalTip != null)
        {
            normalTip.text = GetLocalizedNormalTipText()
                             + new string('.', dotCount);
        }
    }

    string GetLocalizedNormalTipText()
    {
        string localizationKey = normalTipLocalizedText != null
                                 && !string.IsNullOrWhiteSpace(
                                     normalTipLocalizedText.LocalizationKey)
            ? normalTipLocalizedText.LocalizationKey
            : NormalTipLocalizationKey;
        if (osHandle != null)
        {
            return osHandle.GetLocalizedText(
                localizationKey,
                NormalTipFallbackText);
        }

        GameDataManager dataManager = GameDataManager.Instance;
        return dataManager != null
            ? dataManager.GetLocalizedText(
                localizationKey,
                NormalTipFallbackText)
            : NormalTipFallbackText;
    }

    void SetTipAlpha(float alpha)
    {
        if (normalTip == null)
            return;

        Color color = normalTip.color;
        color.a = Mathf.Clamp01(alpha);
        normalTip.color = color;
    }

    void ResetBoard()
    {
        StopFaultGuidance();
        faultBoardInitialized = stageIndex == 1;
        faultGuideSeen = false;
        faultBoardInteracted = false;
        puzzleCompleted = false;
        puzzleCompletionNotified = false;
        if (!boardResolved)
            return;

        KillChargeTweens(false);
        KillStateTweens(false);
        Array.Clear(unitOpen, 0, unitOpen.Length);
        unitOpen[StartIndex] = true;
        unitOpen[InitialPoweredNodeIndex] = true;
        unitOpen[TargetIndex] = true;
        for (int i = 0; i < UnitCount; i++)
        {
            unitFlowSource[i] = -1;
            ApplyUnitState(i, false);
        }

        for (int i = 0; i < lines.Count; i++)
            lines[i].FlowSource = -1;

        RecalculatePower();
        SetAllChargesImmediate(0f);
    }

    void CompletePuzzleIfPowered()
    {
        if (puzzleCompleted || !unitPowered[TargetIndex])
            return;

        puzzleCompleted = true;
        gameplayReady = false;
        SetBoardInteraction(false);

        int targetLineIndex = visualParentLine[TargetIndex];
        Tween targetArrivalTween = targetLineIndex >= 0
            && targetLineIndex < lines.Count
            ? lines[targetLineIndex].Tween
            : null;
        if (targetArrivalTween != null
            && targetArrivalTween.IsActive()
            && !targetArrivalTween.IsComplete())
        {
            targetArrivalTween.OnComplete(NotifyPuzzleCompleted);
            return;
        }

        NotifyPuzzleCompleted();
    }

    void NotifyPuzzleCompleted()
    {
        if (!puzzleCompleted || puzzleCompletionNotified)
            return;

        puzzleCompletionNotified = true;
        StartTargetHeartbeat();
        PuzzleCompleted?.Invoke();
    }

    void RecalculatePower()
    {
        Array.Clear(unitPowered, 0, unitPowered.Length);
        for (int i = 0; i < powerDistance.Length; i++)
            powerDistance[i] = int.MaxValue;

        Queue<int> queue = new Queue<int>();
        unitPowered[StartIndex] = true;
        powerDistance[StartIndex] = 0;
        queue.Enqueue(StartIndex);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current == TargetIndex)
                continue;

            for (int i = 0; i < lines.Count; i++)
            {
                LineState line = lines[i];
                int neighbour = line.From == current
                    ? line.To
                    : line.To == current
                        ? line.From
                        : -1;
                if (!IsValidIndex(neighbour)
                    || unitPowered[neighbour]
                    || !CanReceivePower(neighbour))
                {
                    continue;
                }

                unitPowered[neighbour] = true;
                powerDistance[neighbour] = powerDistance[current] + 1;
                if (neighbour != TargetIndex)
                    queue.Enqueue(neighbour);
            }
        }
    }

    void ApplyPowerVisuals(bool animate)
    {
        KillChargeTweens(false);
        BuildPropagationPlan();
        for (int i = 0; i < UnitCount + ExpectedLineCount; i++)
        {
            if (!PrepareFlowDirections())
                break;

            BuildPropagationPlan();
        }

        for (int i = 0; i < UnitCount; i++)
        {
            if (!IsNormalUnit(i) || unitMaterials[i] == null)
                continue;

            bool charged = unitOpen[i] && unitPowered[i];
            float target = charged ? 1f : 0f;
            float delay = charged && HasVisualReadyTime(i)
                ? visualChargeStart[i]
                : 0f;
            SetUnitCharge(i, target, delay, animate && charged);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            LineState line = lines[i];
            bool charged = TryGetLinePower(
                line,
                out int source,
                out int destination);
            float delay = charged && HasVisualReadyTime(source)
                ? visualReadyAt[source]
                : 0f;
            SetLineCharge(
                line,
                charged ? 1f : 0f,
                delay,
                animate && charged);
        }

        if (animate)
            BeginChargeSpeedRamp();
    }

    bool PrepareFlowDirections()
    {
        bool resetPartialCharge = false;
        for (int i = 0; i < UnitCount; i++)
        {
            if (!IsNormalUnit(i)
                || !unitOpen[i]
                || !unitPowered[i]
                || visualParentUnit[i] < 0)
            {
                continue;
            }

            resetPartialCharge |= ConfigureNodeDirection(
                i,
                visualParentUnit[i]);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            LineState line = lines[i];
            if (!TryGetLinePower(
                    line,
                    out int source,
                    out int destination))
            {
                continue;
            }

            resetPartialCharge |= ConfigureLineDirection(
                line,
                source,
                destination);
        }

        return resetPartialCharge;
    }

    void BuildPropagationPlan()
    {
        for (int i = 0; i < UnitCount; i++)
        {
            visualParentUnit[i] = -1;
            visualParentLine[i] = -1;
            visualReadyAt[i] = float.PositiveInfinity;
            visualChargeStart[i] = 0f;
            visualVisited[i] = false;
        }

        visualReadyAt[StartIndex] = 0f;
        for (int iteration = 0; iteration < UnitCount; iteration++)
        {
            int current = FindNextVisualNode();
            if (current < 0)
                break;

            visualVisited[current] = true;
            if (current == TargetIndex)
                continue;

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                LineState line = lines[lineIndex];
                int neighbour = line.From == current
                    ? line.To
                    : line.To == current
                        ? line.From
                        : -1;
                if (!IsValidIndex(neighbour)
                    || visualVisited[neighbour]
                    || !unitPowered[neighbour])
                {
                    continue;
                }

                float lineTime = GetRemainingChargeTime(
                    line.Material,
                    1f,
                    lineChargeDuration);
                float nodeStart = visualReadyAt[current] + lineTime;
                if (lineTime > 0.0001f && IsNormalUnit(neighbour))
                    nodeStart += Mathf.Max(0f, propagationStep);

                float nodeTime = IsNormalUnit(neighbour)
                    ? GetRemainingChargeTime(
                        unitMaterials[neighbour],
                        1f,
                        nodeChargeDuration)
                    : 0f;
                float candidateReady = nodeStart + nodeTime;
                if (candidateReady + 0.0001f
                    >= visualReadyAt[neighbour])
                {
                    continue;
                }

                visualParentUnit[neighbour] = current;
                visualParentLine[neighbour] = lineIndex;
                visualChargeStart[neighbour] = nodeStart;
                visualReadyAt[neighbour] = candidateReady;
            }
        }
    }

    int FindNextVisualNode()
    {
        int result = -1;
        float earliest = float.PositiveInfinity;
        for (int i = 0; i < UnitCount; i++)
        {
            if (visualVisited[i]
                || !unitPowered[i]
                || visualReadyAt[i] >= earliest)
            {
                continue;
            }

            result = i;
            earliest = visualReadyAt[i];
        }

        return result;
    }

    float GetRemainingChargeTime(
        Material material,
        float target,
        float fullDuration)
    {
        if (material == null)
            return 0f;

        return GetChargeTweenDuration(
            material.GetFloat(ChargeId),
            target,
            fullDuration);
    }

    static float GetChargeTweenDuration(
        float current,
        float target,
        float fullDuration)
    {
        float remaining = Mathf.Abs(
            Mathf.Clamp01(target) - Mathf.Clamp01(current));
        if (remaining <= 0.0001f)
            return 0f;

        return Mathf.Max(
            0.01f,
            Mathf.Max(0.01f, fullDuration) * remaining);
    }

    bool HasVisualReadyTime(int index)
    {
        return IsValidIndex(index)
            && !float.IsInfinity(visualReadyAt[index])
            && !float.IsNaN(visualReadyAt[index]);
    }

    void BeginChargeSpeedRamp()
    {
        ResetChargeSpeedRamp();
        activeChargeTweenCount = CountActiveChargeTweens();
        if (activeChargeTweenCount <= 0)
            return;

        chargeSpeedRampActive = true;
        float maximumBonus = Mathf.Max(
            0f,
            chargeSpeedEndMultiplier - 1f);
        if (maximumBonus <= 0f)
            return;

        Tween tween = DOVirtual.Float(
                0f,
                maximumBonus,
                Mathf.Max(0.01f, chargeSpeedRampDuration),
                ApplyChargeSpeedBonus)
            .SetEase(Ease.InSine)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        chargeSpeedRampTween = tween;
        tween.OnKill(() =>
        {
            if (chargeSpeedRampTween == tween)
                chargeSpeedRampTween = null;
        });
    }

    Sequence CreateFaultNumberTween(
        float duration,
        EaseFunction stopMotion)
    {
        Sequence sequence = DOTween.Sequence();
        int segmentCount = FaultNumberPattern.Length;
        float segmentDuration = Mathf.Max(
            0.01f,
            duration / segmentCount);
        int fromValue = FaultNumberPattern[segmentCount - 1];
        for (int i = 0; i < segmentCount; i++)
        {
            int targetValue = FaultNumberPattern[i];
            sequence.Append(errorNumberText
                .DOCounter(
                    fromValue,
                    targetValue,
                    segmentDuration,
                    false,
                    CultureInfo.InvariantCulture)
                .SetEase(stopMotion)
                .OnUpdate(FormatFaultNumberText));
            fromValue = targetValue;
        }

        return sequence;
    }

    void ApplyChargeSpeedBonus(float bonus)
    {
        chargeSpeedBonus = Mathf.Max(0f, bonus);
        SetChargeTweenTimeScale(ChargeSpeedMultiplier);
    }

    void HandleChargeTweenEnded()
    {
        if (!chargeSpeedRampActive)
            return;

        activeChargeTweenCount = Mathf.Max(
            0,
            activeChargeTweenCount - 1);
        if (activeChargeTweenCount <= 0)
            ResetChargeSpeedRamp();
    }

    void ResetChargeSpeedRamp()
    {
        chargeSpeedRampActive = false;
        activeChargeTweenCount = 0;
        chargeSpeedBonus = 0f;
        SetChargeTweenTimeScale(1f);

        if (chargeSpeedRampTween == null)
            return;

        Tween tween = chargeSpeedRampTween;
        chargeSpeedRampTween = null;
        tween.Kill(false);
    }

    int CountActiveChargeTweens()
    {
        int count = 0;
        for (int i = 0; i < unitTweens.Length; i++)
        {
            Tween tween = unitTweens[i];
            if (tween != null && tween.IsActive() && !tween.IsComplete())
                count++;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            Tween tween = lines[i].Tween;
            if (tween != null && tween.IsActive() && !tween.IsComplete())
                count++;
        }

        return count;
    }

    void SetChargeTweenTimeScale(float timeScale)
    {
        float clampedTimeScale = Mathf.Max(0.01f, timeScale);
        for (int i = 0; i < unitTweens.Length; i++)
        {
            Tween tween = unitTweens[i];
            if (tween != null && tween.IsActive())
                tween.timeScale = clampedTimeScale;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            Tween tween = lines[i].Tween;
            if (tween != null && tween.IsActive())
                tween.timeScale = clampedTimeScale;
        }
    }

    bool TryGetLinePower(
        LineState line,
        out int source,
        out int destination)
    {
        bool fromPowered = unitPowered[line.From];
        bool toPowered = unitPowered[line.To];
        bool touchesTarget = line.From == TargetIndex
            || line.To == TargetIndex;
        bool charged = touchesTarget
            ? fromPowered && toPowered
            : fromPowered || toPowered;

        if (!charged)
        {
            source = line.From;
            destination = line.To;
            return false;
        }

        if (fromPowered && !toPowered)
        {
            source = line.From;
            destination = line.To;
        }
        else if (toPowered && !fromPowered)
        {
            source = line.To;
            destination = line.From;
        }
        else if (HasVisualReadyTime(line.From)
            && HasVisualReadyTime(line.To)
            && visualReadyAt[line.From] <= visualReadyAt[line.To])
        {
            source = line.From;
            destination = line.To;
        }
        else if (HasVisualReadyTime(line.From)
            && HasVisualReadyTime(line.To))
        {
            source = line.To;
            destination = line.From;
        }
        else if (powerDistance[line.From] <= powerDistance[line.To])
        {
            source = line.From;
            destination = line.To;
        }
        else
        {
            source = line.To;
            destination = line.From;
        }

        return true;
    }

    void SetUnitCharge(
        int index,
        float target,
        float delay,
        bool animate)
    {
        Material material = unitMaterials[index];
        if (material == null)
            return;

        if (unitTweens[index] != null)
        {
            unitTweens[index].Kill(false);
            unitTweens[index] = null;
        }

        float current = material.GetFloat(ChargeId);
        float duration = GetChargeTweenDuration(
            current,
            target,
            nodeChargeDuration);
        if (!animate || duration <= 0f)
        {
            material.SetFloat(ChargeId, target);
            return;
        }

        Tween tween = DOVirtual.Float(
                current,
                target,
                duration,
                value => material.SetFloat(ChargeId, value))
            .SetDelay(Mathf.Max(0f, delay))
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetAutoKill(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        unitTweens[index] = tween;
        tween.OnKill(() =>
        {
            if (unitTweens[index] != tween)
                return;

            unitTweens[index] = null;
            HandleChargeTweenEnded();
        });
    }

    void SetLineCharge(
        LineState line,
        float target,
        float delay,
        bool animate)
    {
        if (line.Material == null)
            return;

        line.TargetCharge = Mathf.Clamp01(target);

        if (line.Tween != null)
        {
            line.Tween.Kill(false);
            line.Tween = null;
        }

        float current = line.Material.GetFloat(ChargeId);
        float duration = GetChargeTweenDuration(
            current,
            target,
            lineChargeDuration);
        if (!animate || duration <= 0f)
        {
            line.Material.SetFloat(ChargeId, target);
            return;
        }

        Tween tween = DOVirtual.Float(
                current,
                target,
                duration,
                value => line.Material.SetFloat(ChargeId, value))
            .SetDelay(Mathf.Max(0f, delay))
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetAutoKill(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        line.Tween = tween;
        tween.OnKill(() =>
        {
            if (line.Tween != tween)
                return;

            line.Tween = null;
            HandleChargeTweenEnded();
        });
    }

    bool ConfigureLineDirection(
        LineState line,
        int source,
        int destination)
    {
        if (line.Material == null
            || line.Image == null
            || unitImages[source] == null
            || unitImages[destination] == null)
        {
            return false;
        }

        bool resetPartialCharge = ResetPartialChargeForDirectionChange(
            line.Material,
            line.FlowSource,
            source);
        line.FlowSource = source;

        RectTransform lineRect = line.Image.rectTransform;
        Vector3 sourceLocal = lineRect.InverseTransformPoint(
            unitImages[source].rectTransform.position);
        Vector3 destinationLocal = lineRect.InverseTransformPoint(
            unitImages[destination].rectTransform.position);
        SetFlowDirection(
            line.Material,
            lineRect.rect,
            sourceLocal,
            destinationLocal);
        return resetPartialCharge;
    }

    bool ConfigureNodeDirection(int nodeIndex, int sourceIndex)
    {
        if (!IsNormalUnit(nodeIndex)
            || !IsValidIndex(sourceIndex)
            || unitMaterials[nodeIndex] == null
            || unitImages[nodeIndex] == null
            || unitImages[sourceIndex] == null)
        {
            return false;
        }

        Material material = unitMaterials[nodeIndex];
        bool resetPartialCharge = ResetPartialChargeForDirectionChange(
            material,
            unitFlowSource[nodeIndex],
            sourceIndex);
        unitFlowSource[nodeIndex] = sourceIndex;

        RectTransform nodeRect = unitImages[nodeIndex].rectTransform;
        Vector3 sourceLocal = nodeRect.InverseTransformPoint(
            unitImages[sourceIndex].rectTransform.position);
        SetFlowDirection(
            material,
            nodeRect.rect,
            sourceLocal,
            Vector3.zero);
        return resetPartialCharge;
    }

    static bool ResetPartialChargeForDirectionChange(
        Material material,
        int previousSource,
        int source)
    {
        if (material == null
            || previousSource < 0
            || previousSource == source)
        {
            return false;
        }

        float charge = material.GetFloat(ChargeId);
        if (charge <= 0.0001f || charge >= 0.9999f)
            return false;

        material.SetFloat(ChargeId, 0f);
        return true;
    }

    static void SetFlowDirection(
        Material material,
        Rect rect,
        Vector3 sourceLocal,
        Vector3 destinationLocal)
    {
        float normalizedX = (destinationLocal.x - sourceLocal.x)
            / Mathf.Max(0.001f, rect.width);
        float normalizedY = (destinationLocal.y - sourceLocal.y)
            / Mathf.Max(0.001f, rect.height);

        Vector2 start;
        Vector2 end;
        if (Mathf.Abs(normalizedX) >= Mathf.Abs(normalizedY))
        {
            bool movesRight = normalizedX >= 0f;
            start = new Vector2(movesRight ? 0f : 1f, 0.5f);
            end = new Vector2(movesRight ? 1f : 0f, 0.5f);
        }
        else
        {
            bool movesUp = normalizedY >= 0f;
            start = new Vector2(0.5f, movesUp ? 0f : 1f);
            end = new Vector2(0.5f, movesUp ? 1f : 0f);
        }

        material.SetVector(
            FlowStartId,
            new Vector4(start.x, start.y, 0f, 0f));
        material.SetVector(
            FlowEndId,
            new Vector4(end.x, end.y, 0f, 0f));
    }

    void SetAllChargesImmediate(float amount)
    {
        float clampedAmount = Mathf.Clamp01(amount);
        for (int i = 0; i < unitMaterials.Length; i++)
            unitMaterials[i]?.SetFloat(ChargeId, clampedAmount);

        for (int i = 0; i < lines.Count; i++)
        {
            LineState line = lines[i];
            line.TargetCharge = clampedAmount;
            line.Material?.SetFloat(ChargeId, clampedAmount);
        }
    }

    void KillChargeTweens(bool applyTargets)
    {
        ResetChargeSpeedRamp();

        for (int i = 0; i < unitTweens.Length; i++)
        {
            if (unitTweens[i] != null)
            {
                unitTweens[i].Kill(false);
                unitTweens[i] = null;
            }

            if (applyTargets && unitMaterials[i] != null)
            {
                float target = unitOpen[i] && unitPowered[i] ? 1f : 0f;
                unitMaterials[i].SetFloat(ChargeId, target);
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            LineState line = lines[i];
            if (line.Tween != null)
            {
                line.Tween.Kill(false);
                line.Tween = null;
            }

            if (applyTargets)
                line.Material?.SetFloat(ChargeId, line.TargetCharge);
        }
    }

    void KillStateTweens(bool applyTargets)
    {
        for (int i = 0; i < unitStateTweens.Length; i++)
        {
            if (unitStateTweens[i] != null)
            {
                unitStateTweens[i].Kill(false);
                unitStateTweens[i] = null;
            }

            if (applyTargets && unitMaterials[i] != null)
            {
                unitMaterials[i].SetFloat(
                    StateId,
                    unitOpen[i] ? 1f : 0f);
            }
        }
    }

    void ApplyUnitState(int index, bool animate)
    {
        if (!IsNormalUnit(index) || unitImages[index] == null)
            return;

        Material material = unitMaterials[index];
        if (material == null || !material.HasProperty(StateId))
        {
            Sprite fallbackSprite = unitOpen[index]
                ? openUnitSprite
                : closedUnitSprite;
            if (fallbackSprite != null)
                unitImages[index].sprite = fallbackSprite;
            return;
        }

        if (closedUnitSprite != null)
            unitImages[index].sprite = closedUnitSprite;

        if (unitStateTweens[index] != null)
        {
            unitStateTweens[index].Kill(false);
            unitStateTweens[index] = null;
        }

        float target = unitOpen[index] ? 1f : 0f;
        float current = material.GetFloat(StateId);
        if (!animate || Mathf.Approximately(current, target))
        {
            material.SetFloat(StateId, target);
            return;
        }

        Tween tween = DOVirtual.Float(
                current,
                target,
                Mathf.Max(
                    0.01f,
                    stateTransitionDuration
                    * Mathf.Abs(target - current)),
                value => material.SetFloat(StateId, value))
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        unitStateTweens[index] = tween;
        tween.OnKill(() =>
        {
            if (unitStateTweens[index] == tween)
                unitStateTweens[index] = null;
        });
    }

    void SetBoardInteraction(bool isEnabled)
    {
        for (int i = 0; i < unitImages.Length; i++)
        {
            if (unitImages[i] != null)
                unitImages[i].raycastTarget = isEnabled && IsNormalUnit(i);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Image != null)
                lines[i].Image.raycastTarget = false;
        }
    }

    bool EnsureBoard()
    {
        if (boardResolved)
            return true;

        ResolveReferences();
        if (unitGrid == null
            || lineGrid == null
            || unitGrid.childCount != UnitCount)
        {
            WarnMissingBoard();
            return false;
        }

        for (int i = 0; i < UnitCount; i++)
        {
            unitImages[i] = unitGrid.GetChild(i).GetComponent<Image>();
            if (unitImages[i] == null)
            {
                WarnMissingBoard();
                return false;
            }
        }

        lines.Clear();
        for (int i = 0; i < lineGrid.childCount; i++)
        {
            Transform child = lineGrid.GetChild(i);
            if (!TryParseLineName(child.name, out int from, out int to))
                continue;

            Image image = child.GetComponent<Image>();
            if (image == null)
                continue;

            lines.Add(new LineState
            {
                From = from,
                To = to,
                Image = image
            });
        }

        if (lines.Count != ExpectedLineCount)
        {
            WarnMissingBoard();
            return false;
        }

        boardResolved = true;
        EnsureFaultNodePointers();
        warnedMissingBoard = false;
        return true;
    }

    void EnsureRuntimeMaterials()
    {
        if (materialsConfigured || !EnsureBoard())
            return;

        if (chargeMaterialTemplate == null)
        {
            if (!warnedMissingMaterial)
            {
                warnedMissingMaterial = true;
                Debug.LogWarning(
                    "[StateCheck] Charge material template is missing.",
                    this);
            }
            return;
        }

        for (int i = 0; i < UnitCount; i++)
        {
            if (!IsNormalUnit(i))
                continue;

            unitMaterials[i] = CreateRuntimeMaterial(
                unitImages[i],
                true,
                $"StateCheck Unit {i} Charge (Runtime)");
        }

        for (int i = 0; i < lines.Count; i++)
        {
            LineState line = lines[i];
            line.Material = CreateRuntimeMaterial(
                line.Image,
                false,
                $"StateCheck Line {line.From}->{line.To} Charge (Runtime)");
        }

        materialsConfigured = true;
        warnedMissingMaterial = false;
        ConfigureMaterialRects();
    }

    void EnsureErrorGlitchMaterial()
    {
        if (errorTipRuntimeMaterial != null
            || errorTipImage == null
            || errorGlitchMaterialTemplate == null)
        {
            return;
        }

        errorTipOriginalMaterial = errorTipImage.material;
        errorTipRuntimeMaterial = new Material(errorGlitchMaterialTemplate)
        {
            name = "StateCheck Error Blocks (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        errorTipImage.material = errorTipRuntimeMaterial;
        ConfigureErrorGlitchMaterial();
    }

    void ConfigureErrorGlitchMaterial()
    {
        if (errorTipRuntimeMaterial == null || errorTipImage == null)
            return;

        Rect rect = errorTipImage.rectTransform.rect;
        float inverseWidth = 1f / Mathf.Max(0.001f, rect.width);
        float inverseHeight = 1f / Mathf.Max(0.001f, rect.height);
        errorTipRuntimeMaterial.SetVector(
            LocalRectId,
            new Vector4(
                rect.xMin,
                rect.yMin,
                inverseWidth,
                inverseHeight));
        errorTipRuntimeMaterial.SetFloat(
            GlitchRateId,
            Mathf.Clamp(faultGlitchFramesPerSecond, 4, 20));
        errorTipRuntimeMaterial.SetFloat(
            GlitchAmountId,
            Mathf.Clamp01(faultBlockIntensity));
        if (TryGetSpriteUvRect(
                errorTipImage.sprite,
                out Vector4 spriteRect))
        {
            errorTipRuntimeMaterial.SetVector(
                SpriteUvRectId,
                spriteRect);
        }
    }

    Material CreateRuntimeMaterial(
        Image image,
        bool nodeMode,
        string materialName)
    {
        if (image == null)
            return null;

        Material material = new Material(chargeMaterialTemplate)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave
        };
        Sprite originalSprite = image.sprite;
        Sprite runtimeSprite = nodeMode && closedUnitSprite != null
            ? closedUnitSprite
            : originalSprite;
        if (runtimeSprite != null && image.sprite != runtimeSprite)
            image.sprite = runtimeSprite;

        material.SetColor(OffColorId, unpoweredColor);
        material.SetColor(OnColorId, poweredColor);
        material.SetFloat(ChargeId, 0f);
        material.SetFloat(NodeModeId, nodeMode ? 1f : 0f);
        material.SetVector(FlowStartId, new Vector4(0f, 0.5f, 0f, 0f));
        material.SetVector(FlowEndId, new Vector4(1f, 0.5f, 0f, 0f));
        if (TryGetSpriteUvRect(runtimeSprite, out Vector4 spriteRect))
            material.SetVector(SpriteUvRectId, spriteRect);

        if (nodeMode)
        {
            material.SetFloat(StateId, 0f);
            ConfigureNodeSpriteUvs(material);
        }

        runtimeGraphics.Add(new RuntimeGraphic
        {
            Image = image,
            OriginalMaterial = image.material,
            OriginalColor = image.color,
            OriginalSprite = originalSprite,
            RuntimeMaterial = material
        });
        image.material = material;
        image.color = Color.white;
        return material;
    }

    void ConfigureNodeSpriteUvs(Material material)
    {
        if (material == null)
            return;

        Texture atlasTexture = closedUnitSprite != null
            ? closedUnitSprite.texture
            : openUnitSprite != null
                ? openUnitSprite.texture
                : null;
        if (atlasTexture != null)
        {
            float width = Mathf.Max(1f, atlasTexture.width);
            float height = Mathf.Max(1f, atlasTexture.height);
            material.SetVector(
                AtlasTexelSizeId,
                new Vector4(1f / width, 1f / height, width, height));
        }

        if (TryGetSpriteUvRect(closedUnitSprite, out Vector4 closedRect))
            material.SetVector(ClosedUvRectId, closedRect);
        if (TryGetSpriteUvRect(openUnitSprite, out Vector4 openRect))
            material.SetVector(OpenUvRectId, openRect);
    }

    static bool TryGetSpriteUvRect(Sprite sprite, out Vector4 uvRect)
    {
        uvRect = new Vector4(0f, 0f, 1f, 1f);
        if (sprite == null || sprite.texture == null)
            return false;

        Rect rect = sprite.textureRect;
        float inverseWidth = 1f / Mathf.Max(1f, sprite.texture.width);
        float inverseHeight = 1f / Mathf.Max(1f, sprite.texture.height);
        uvRect = new Vector4(
            rect.xMin * inverseWidth,
            rect.yMin * inverseHeight,
            rect.width * inverseWidth,
            rect.height * inverseHeight);
        return true;
    }

    void ConfigureMaterialRects()
    {
        if (!materialsConfigured)
            return;

        if (unitGrid != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(unitGrid);

        for (int i = 0; i < runtimeGraphics.Count; i++)
        {
            RuntimeGraphic graphic = runtimeGraphics[i];
            Rect rect = graphic.Image.rectTransform.rect;
            float inverseWidth = 1f / Mathf.Max(0.001f, rect.width);
            float inverseHeight = 1f / Mathf.Max(0.001f, rect.height);
            graphic.RuntimeMaterial.SetVector(
                LocalRectId,
                new Vector4(
                    rect.xMin,
                    rect.yMin,
                    inverseWidth,
                    inverseHeight));
        }

        ConfigureErrorGlitchMaterial();
    }

    void DestroyErrorGlitchMaterial()
    {
        if (errorTipImage != null)
            errorTipImage.material = errorTipOriginalMaterial;

        if (errorTipRuntimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(errorTipRuntimeMaterial);
            else
                DestroyImmediate(errorTipRuntimeMaterial);
        }

        errorTipOriginalMaterial = null;
        errorTipRuntimeMaterial = null;
    }

    void DestroyRuntimeMaterials()
    {
        for (int i = 0; i < runtimeGraphics.Count; i++)
        {
            RuntimeGraphic graphic = runtimeGraphics[i];
            if (graphic.Image != null)
            {
                graphic.Image.material = graphic.OriginalMaterial;
                graphic.Image.color = graphic.OriginalColor;
                graphic.Image.sprite = graphic.OriginalSprite;
            }

            if (graphic.RuntimeMaterial == null)
                continue;

            if (Application.isPlaying)
                Destroy(graphic.RuntimeMaterial);
            else
                DestroyImmediate(graphic.RuntimeMaterial);
        }

        runtimeGraphics.Clear();
        materialsConfigured = false;
    }

    void ResolveReferences()
    {
        if (window == null)
            window = GetComponent<OSWindowUnit>();

        if (inforPartGroup == null)
        {
            inforPartGroup = transform.Find("InforPart")
                ?.GetComponent<CanvasGroup>();
        }

        if (errorPartGroup == null)
        {
            errorPartGroup = transform.Find("ErrorPart")
                ?.GetComponent<CanvasGroup>();
        }

        if (recordUnitPartGroup == null)
        {
            recordUnitPartGroup = transform.Find("RecordUnitPart")
                ?.GetComponent<CanvasGroup>();
        }

        if (normalTip == null)
        {
            normalTip = transform.Find("TipArea/Text (TMP)")
                ?.GetComponent<TMP_Text>();
        }
        if (normalTipLocalizedText == null && normalTip != null)
        {
            normalTipLocalizedText = normalTip
                .GetComponent<OSLocalizedText>();
        }

        Transform inforRoot = transform.Find("InforPart/Infor");
        Transform inforNormal = inforRoot?.Find("Normal");
        Transform safeTipRoot = transform.Find("InforPart/SafeTip");
        Transform inforError = FindErrorVariant(inforRoot);
        Transform safeTipError = FindErrorVariant(safeTipRoot);
        if (inforErrorObject == null)
            inforErrorObject = inforError?.gameObject;
        if (inforNormalObject == null)
            inforNormalObject = inforNormal?.gameObject;
        if (safeTipErrorObject == null)
            safeTipErrorObject = safeTipError?.gameObject;
        if (safeTipNormalObject == null)
            safeTipNormalObject = safeTipRoot?.Find("Normal")?.gameObject;

        inforMinButton = ResolveInformationSectionMinButton(
            inforRoot,
            inforNormal,
            InforCollapsedBottomPadding,
            inforMinButton);
        safeTipMinButton = ResolveInformationSectionMinButton(
            safeTipRoot,
            safeTipRoot?.Find("Normal"),
            SafeTipCollapsedBottomPadding,
            safeTipMinButton);

        if (gameTimeText == null)
        {
            gameTimeText = inforNormal?.Find("GameTime/GameTimeTMP")
                ?.GetComponent<TMP_Text>();
        }

        if (gameProcessNumberText == null)
        {
            gameProcessNumberText = inforNormal
                ?.Find("GameProcess/GameProcessNUM")
                ?.GetComponent<TMP_Text>();
        }

        if (gameProcessImage == null)
        {
            gameProcessImage = inforNormal
                ?.Find("GameProcess/GameProcessImageBK/GameProcessImage")
                ?.GetComponent<Image>();
        }
        if (errorTipImage == null)
        {
            errorTipImage = inforError?.Find("ErrorTip")
                ?.GetComponent<Image>();
        }

        if (inforErrorText == null)
        {
            inforErrorText = inforError?.Find("ErrorTMP")
                ?.GetComponent<TMP_Text>();
        }

        if (safeTipErrorText == null)
        {
            safeTipErrorText = safeTipError?.Find("ErrorTMP")
                ?.GetComponent<TMP_Text>();
        }

        if (errorNumberText == null)
        {
            errorNumberText = inforError?.Find("ErrorNum")
                ?.GetComponent<TMP_Text>();
        }

        CacheFaultText();

        Transform character = transform.Find("ErrorPart/Character");
        if (lineGrid == null)
            lineGrid = character?.Find("LineGrid") as RectTransform;
        if (unitGrid == null)
            unitGrid = character?.Find("UnitGrid") as RectTransform;

        if (targetHeart == null && unitGrid != null)
        {
            Transform targetUnit = unitGrid.childCount > TargetIndex
                ? unitGrid.GetChild(TargetIndex)
                : unitGrid.Find("GridUnit_Target");
            targetHeart = targetUnit?.Find("GridUnit_Heart")
                as RectTransform;
        }

        if (osHandle == null)
            osHandle = GetComponentInParent<OS_Handle>(true);
        ResolveChapterRecordReferences();
        CacheChapterVisualTemplates();
        CacheTargetHeartScale();
    }

    static OSStateCheckSectionMinButton ResolveInformationSectionMinButton(
        Transform sectionRoot,
        Transform normal,
        float collapsedBottomPadding,
        OSStateCheckSectionMinButton current)
    {
        Transform button = sectionRoot?.Find("MinButton");
        RectMask2D contentMask = normal?.GetComponent<RectMask2D>();
        if (button == null || contentMask == null)
            return current;

        if (current == null)
        {
            current = button
                .GetComponent<OSStateCheckSectionMinButton>();
            if (current == null && Application.isPlaying)
            {
                current = button.gameObject
                    .AddComponent<OSStateCheckSectionMinButton>();
            }
        }

        current?.Initialize(contentMask, collapsedBottomPadding);
        return current;
    }

    static Transform FindErrorVariant(Transform parent)
    {
        if (parent == null)
            return null;

        return parent.Find("Erorr") ?? parent.Find("Error");
    }

    void SubscribeWindowEvents()
    {
        if (window == null)
            return;

        window.OpenStarted -= HandleOpenStarted;
        window.OpenAnimationCompleted -= HandleOpenAnimationCompleted;
        window.OpenStarted += HandleOpenStarted;
        window.OpenAnimationCompleted += HandleOpenAnimationCompleted;
    }

    void UnsubscribeWindowEvents()
    {
        if (window == null)
            return;

        window.OpenStarted -= HandleOpenStarted;
        window.OpenAnimationCompleted -= HandleOpenAnimationCompleted;
    }

    void KillRevealSequence()
    {
        if (revealSequence == null)
            return;

        Sequence sequence = revealSequence;
        revealSequence = null;
        sequence.Kill(false);
    }

    void WarnMissingBoard()
    {
        if (warnedMissingBoard)
            return;

        warnedMissingBoard = true;
        Debug.LogWarning(
            "[StateCheck] UnitGrid must contain 16 images and LineGrid "
            + "must contain the 24 named grid edges.",
            this);
    }

    bool TryGetChapterSlotIndex(
        GameObject target,
        out int chapterSequence)
    {
        chapterSequence = -1;
        if (target == null)
            return false;

        Transform candidate = target.transform;
        while (candidate != null && candidate != transform)
        {
            Transform parent = candidate.parent;
            if (parent == recordPools || parent == processLine)
            {
                chapterSequence = candidate.GetSiblingIndex();
                return chapterSequence >= 0
                    && chapterSequence < ChapterSlotCount;
            }

            candidate = parent;
        }

        return false;
    }

    bool TryGetUnitIndex(GameObject target, out int nodeIndex)
    {
        nodeIndex = -1;
        if (target == null || unitGrid == null)
            return false;

        Transform candidate = target.transform;
        while (candidate != null
            && candidate != unitGrid
            && candidate.parent != unitGrid)
        {
            candidate = candidate.parent;
        }

        if (candidate == null || candidate.parent != unitGrid)
            return false;

        nodeIndex = candidate.GetSiblingIndex();
        return IsValidIndex(nodeIndex);
    }

    static bool TryParseLineName(
        string lineName,
        out int from,
        out int to)
    {
        from = -1;
        to = -1;
        if (string.IsNullOrEmpty(lineName))
            return false;

        int separator = lineName.IndexOf("->", StringComparison.Ordinal);
        if (separator <= 0 || separator >= lineName.Length - 2)
            return false;

        return int.TryParse(lineName.Substring(0, separator), out from)
            && int.TryParse(lineName.Substring(separator + 2), out to)
            && IsValidIndex(from)
            && IsValidIndex(to);
    }

    bool CanReceivePower(int index)
    {
        return index == StartIndex
            || index == TargetIndex
            || IsNormalUnit(index) && unitOpen[index];
    }

    static bool IsNormalUnit(int index)
    {
        return IsValidIndex(index)
            && index != StartIndex
            && index != TargetIndex
            && index != 3
            && index != 10
            && index != 12;
    }

    static bool IsValidIndex(int index)
    {
        return index >= 0 && index < UnitCount;
    }

    static void SetCanvasGroupState(
        CanvasGroup group,
        float alpha,
        bool interaction)
    {
        if (group == null)
            return;

        group.alpha = Mathf.Clamp01(alpha);
        group.interactable = interaction;
        group.blocksRaycasts = interaction;
    }

    static void SetObjectActive(GameObject target, bool isActive)
    {
        if (target != null && target.activeSelf != isActive)
            target.SetActive(isActive);
    }
}
