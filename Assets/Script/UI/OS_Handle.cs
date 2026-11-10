using System;
using System.Collections.Generic;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed partial class OS_Handle : Singleton<OS_Handle>
{
    const int MainStoryGiftRecordIndex = 1;
    const int Chapter01RecordSequence = 1;
    const string GramophoneInstalledSignal = "gramophone.installed";
    const string GramophoneInstalledMessage = "gramophone_first_open"; // Keep existing save receipts.
    const string HeartHealEndingTakeKeyOptionId = "take_key";
    const string HeartHealEndingKeepSafeOptionId = "keep_safe";
    const string HeartHealEndingCloseOSOptionId = "close_os";
    const string HeartHealEndingAmbientKey = "SecretPart";
    const string HeartHealEndingOpenSfxKey = "Secret_Open";
    const string HeartHealEndingLiveOpenSfxKey = "SecretLive_Open";
    const string HeartHealEndingMouseSfxKey = "Secret_Mouse";
    const string HeartHealEndingShutdownSfxKey = "ShutDown";
    const string HeartHealEndingOpenLockSfxKey = "OpenLock";
    const string OpenUnitSfxKey = "OpenUnit";
    const float HeartHealEndingInspectionDotsInterval = 0.24f;
    const float HeartHealEndingInspectionCompletionPause = 0.5f;
    const string LockHeartGlitchShaderName =
        "UI/OS/LockHeartPersistentGlitch";
    const string LockHeartSplitShaderName =
        "UI/OS/LockHeartSplit";
    const string Chapter01KeySourceContainerId =
        "chapter01_key_source";

    [Header("Presentation")]
    [SerializeField] Canvas osCanvas;
    [SerializeField] CanvasGroup osCanvasGroup;
    [SerializeField] GraphicRaycaster graphicRaycaster;

    [Header("Heartbeat")]
    [SerializeField] RectTransform heartbeatPanel;
    [SerializeField] HeartbeatWaveformUI heartbeatWaveform;
    [SerializeField] OSUICablePoolController cablePool;

    [Header("Main Story Intro")]
    [SerializeField] DesktopProcessUnit mainStoryGiftDesktopProcess;
    [SerializeField] RectTransform mainStoryGift;
    [SerializeField] RectTransform mainStoryGiftFallTip;
    [SerializeField] OSGiftLandingDust mainStoryGiftLandingDust;
    [SerializeField] OSWindowUnit mainStoryGiftWindow;
    [SerializeField] OSGiftWindowController mainStoryGiftWindowController;
    [SerializeField] float mainStoryGiftStartY = 180f;
    [SerializeField, Min(0.05f)] float mainStoryGiftDropDuration = 4.125f;
    [SerializeField, Min(0f)] float mainStoryGiftSwayDistance = 4.8f;
    [SerializeField, Range(0f, 20f)] float mainStoryGiftSwayAngle = 7f;
    [SerializeField] float mainStoryGiftFallTipStartY = -9.5f;
    [SerializeField] float mainStoryGiftFallTipEndY = -30f;
    [SerializeField, Min(0.05f)] float mainStoryGiftFallTipDuration = 0.9f;

    [Header("Desktop Processes")]
    [SerializeField]
    List<DesktopProcessUnit> desktopProcessUnits =
        new List<DesktopProcessUnit>();
    [SerializeField] DesktopProcessUnit memoryDesktopProcess;
    [SerializeField] OSWindowUnit memoryWindow;
    [SerializeField] OSMemoryWindowController memoryWindowController;
    [SerializeField] DesktopProcessUnit recordDesktopProcess;
    [SerializeField] OSWindowUnit recordWindow;
    [SerializeField] OSRecordWindowController recordWindowController;
    [SerializeField] DesktopProcessUnit toolDesktopProcess;
    [SerializeField] OSWindowUnit toolWindow;
    [SerializeField] OSToolWindowController toolWindowController;
    [SerializeField] DesktopProcessUnit gramophoneDesktopProcess;
    [SerializeField] OSWindowUnit gramophoneWindow;
    [SerializeField] DesktopProcessUnit energyLoopDesktopProcess;
    [SerializeField] OSWindowUnit energyLoopWindow;
    [SerializeField] DesktopProcessUnit paperPushDesktopProcess;
    [SerializeField] OSWindowUnit paperPushWindow;
    [SerializeField] Sprite memoryRepair01Icon;
    [SerializeField] Sprite memoryRepair02Icon;
    Sprite _originalMemoryIcon;
    [SerializeField] DesktopProcessUnit lockHeartDesktopProcess;
    [SerializeField] DesktopProcessUnit fileDesktopProcess;
    [SerializeField] OSWindowUnit folderWindow;
    [SerializeField] DesktopProcessUnit characterDesktopProcess;
    [SerializeField] OSWindowUnit characterWindow;
    [SerializeField] OSCharacterWindowController characterWindowController;
    [SerializeField] OSLinkMessageController linkMessageController;
    [SerializeField] DesktopProcessUnit heartHealToolDesktopProcess;
    [SerializeField] OSWindowUnit heartHealToolWindow;
    [SerializeField]
    OSHeartHealToolWindowController heartHealToolWindowController;
    [SerializeField] DesktopProcessUnit stateCheckDesktopProcess;
    [SerializeField] OSWindowUnit stateCheckWindow;
    [SerializeField]
    OSStateCheckWindowController stateCheckWindowController;

    [Header("Desktop Process Layout")]
    [SerializeField] OSDesktopProcessGrid desktopProcessGrid;
    [SerializeField] OSDesktopProcessGrid folderProcessGrid;
    [SerializeField] OSDesktopProcessDragProxy desktopProcessDragProxy;
    [SerializeField, Range(0f, 1f)] float draggedProcessSourceAlpha = 0.35f;

    [Header("Error Window Effect")]
    [SerializeField] RectTransform errorPoolRoot;
    [SerializeField]
    OSErrorWindowPoolController errorWindowPoolController;

    [Header("OS SFX")]
    [SerializeField] OSSFXAction osSfxAction;
    [SerializeField] OSCloseAndReOpenPart closeAndReOpenPart;
    [SerializeField, Min(0f)] float stateCheckRestartDelay = 0.3f;

    [Header("Chapter 01 Record Guidance")]
    [SerializeField, Min(0f)] float chapter01RecordHoverTimeout = 15f;
    [SerializeField, Min(0.05f)] float chapter01CursorTakeoverDuration = 0.3f;
    [SerializeField, Min(1f)] float chapter01CursorTakeoverScale = 1.5f;
    [SerializeField]
    Color chapter01CursorTakeoverColor =
        new Color32(0xD8, 0x39, 0x3E, 0xFF);
    [SerializeField, Min(0f)] float chapter01CursorReactionDelay = 0.3f;
    [SerializeField, Min(0f)] float chapter01CursorCheckDistance = 18f;
    [SerializeField, Min(0.05f)] float chapter01CursorCheckMoveDuration = 0.16f;
    [SerializeField, Min(0.05f)] float chapter01CursorTargetMoveDuration = 0.55f;
    [SerializeField, Min(0f)] float chapter01CursorAimPause = 0.12f;
    [SerializeField, Min(0.05f)] float chapter01CursorPressDuration = 0.11f;

    [Header("Chapter 01 Key Retrieval")]
    [SerializeField] OSWindowUnit chapter01KeyWindow;
    [SerializeField] OSDesktopProcessGrid chapter01KeySourceGrid;
    [SerializeField] DesktopProcessUnit chapter01KeyDesktopProcess;
    [SerializeField, Min(0f)] float chapter01KeyWindowOpenDelay = 0.3f;
    [SerializeField, Min(0f)] float chapter01KeyWindowGap = 20f;

    [Header("First Heart Heal Ending")]
    [SerializeField] CanvasGroup firstHeartHealBlackBK;
    [SerializeField] Image firstHeartHealDimFilter;
    [SerializeField, Min(0f)] float firstHeartHealBlackHoldDuration = 3f;
    [SerializeField, Min(0.05f)] float firstHeartHealBlackFadeDuration = 1f;
    [SerializeField, Min(0f)] float firstHeartHealLiveOpenDelay = 1f;
    [SerializeField, Min(0f)] float firstHeartHealAmbientFadeDuration = 2f;
    [SerializeField, Min(0f)]
    float firstHeartHealReturnAmbientFadeDuration =
        1f;
    [SerializeField, Range(0f, 1f)] float firstHeartHealDimAlpha = 0.75f;
    [SerializeField]
    Vector2 firstHeartHealCharacterPosition =
        new Vector2(100f, 20f);
    [SerializeField]
    Vector2 firstHeartHealCharacterSize =
        new Vector2(150f, 180f);
    [SerializeField] OSWindowUnit firstHeartHealSpecialWindow;
    [SerializeField] OSWindowUnit firstHeartHealSpecialWindow01;
    [SerializeField] RectTransform firstHeartHealSpecialKey;
    [SerializeField, Min(0.05f)]
    float firstHeartHealCursorTakeoverDuration =
        0.3f;
    [SerializeField, Min(1f)] float firstHeartHealCursorTakeoverScale = 1.5f;
    [SerializeField]
    Color firstHeartHealCursorTakeoverColor =
        new Color32(0xD8, 0x39, 0x3E, 0xFF);
    [SerializeField, Min(0f)] float firstHeartHealAutoClickDelay = 1.7f;
    [SerializeField, Min(0.05f)] float firstHeartHealCursorMoveDuration = 0.65f;
    [SerializeField, Min(0f)] float firstHeartHealCursorAimPause = 0.18f;
    [SerializeField, Min(0.05f)]
    float firstHeartHealCursorPressDuration =
        0.11f;
    [SerializeField, Min(0f)] float firstHeartHealKeyObserveDelay = 0.45f;
    [SerializeField, Min(0.05f)]
    float firstHeartHealKeyExtractDuration =
        0.48f;
    [SerializeField, Min(0f)] float firstHeartHealKeyExtractPause = 0.18f;
    [SerializeField, Min(0f)]
    float firstHeartHealKeyReleaseDriftDistance =
        3.5f;
    [SerializeField, Min(0.05f)]
    float firstHeartHealKeyReleaseDriftDuration =
        0.22f;
    [SerializeField, Min(0.05f)]
    float firstHeartHealKeyRegrabDuration =
        0.16f;
    [SerializeField, Min(0f)] float firstHeartHealKeyExitPadding = 14f;
    [SerializeField, Min(0.05f)] float firstHeartHealKeyDragDuration = 1.05f;
    [SerializeField, Min(0f)]
    float firstHeartHealCursorReleasePause =
        0.16f;
    [SerializeField, Min(0.05f)] float firstHeartHealLockOpenDuration = 0.45f;
    [SerializeField, Min(0f)] float firstHeartHealLockDropDistance = 8f;
    [SerializeField, Min(0f)]
    float firstHeartHealNewWindowReactionDelay =
        1.2f;
    [SerializeField]
    Vector2 firstHeartHealSpecialWindow01Position =
        new Vector2(-170f, 20f);
    [SerializeField]
    Vector2 firstHeartHealArrangedCharacterPosition =
        new Vector2(220f, 20f);
    [SerializeField, Min(0.05f)]
    float firstHeartHealWindowDragDuration =
        0.9f;
    [SerializeField, Min(0f)]
    float firstHeartHealBetweenWindowDragsDelay =
        0.35f;
    [SerializeField, Min(0f)]
    float firstHeartHealArrangedMessageDelay =
        0.5f;
    [SerializeField, Min(0f)]
    float firstHeartHealInspectionNudgeDistance =
        22f;
    [SerializeField, Min(0.05f)]
    float firstHeartHealInspectionNudgeDuration =
        0.34f;
    [SerializeField, Min(0.05f)]
    float firstHeartHealInspectionSweepDuration =
        0.3f;
    [SerializeField, Min(0f)]
    float firstHeartHealInspectionSweepPause =
        0.09f;
    [SerializeField, Min(0f)]
    float firstHeartHealCheckNormalReadDelay =
        3.6f;
    [SerializeField, Min(0f)]
    float firstHeartHealSpecial01CloseDelay =
        0.3f;
    [SerializeField, Min(0f)]
    float firstHeartHealBeforeRelockMoveDelay =
        0.2f;
    [SerializeField, Min(0f)]
    float firstHeartHealRelockedMessageDelay =
        0.4f;
    [SerializeField, Min(0f)]
    float firstHeartHealPostRelockReadDelay =
        1.8f;

    [SerializeField]
    List<OSWindowUnit> windowUnits =
        new List<OSWindowUnit>();
    [SerializeField]
    List<ProcessUnit> processUnits =
        new List<ProcessUnit>();

    [Header("Localization")]
    [SerializeField]
    OSLocalizedText[] localizedTexts =
        new OSLocalizedText[0];
    [SerializeField]
    OSResourceStateView[] resourceViews =
        new OSResourceStateView[0];

    public bool IsOSVisible { get; private set; }
    public OSWindowUnit MouseActiveWindow => _mouseActiveWindow;
    public bool IsEvent01DesktopTutorialSuppressed =>
        _isEvent01DesktopTutorialSuppressed;
    public bool IsEvent01HeartHealTutorialSuppressed =>
        _isEvent01HeartHealTutorialSuppressed;
    public bool IsSystemInputLocked => IsSystemInputLockedWithoutClawTrap
        || Chapter04Story?.FinalHeal?.IsRunning == true
        || ClawMachineGame?.IsCursorTrapControlling == true;
    public bool IsSystemInputLockedWithoutClawTrap => IsSystemInputLockedWithoutCursorExperiment
        || CursorExperiment?.IsBlockingInput == true;
    public bool IsSystemInputLockedWithoutCursorExperiment => IsSystemInputLockedWithoutChapter02Finale
        || IsChapter02FinaleHiddenTextActive;
    public bool IsSystemInputLockedWithoutChapter02Finale => IsSystemInputLockedWithoutChapter02PaperPush
        || Chapter02PaperPush?.IsBlockingInput == true;
    public bool IsSystemInputLockedWithoutChapter02PaperPush => IsSystemInputLockedWithoutChapter02Nightfall
        || Chapter02FinalTalk?.IsNightfallTransitionBlockingInput == true;
    // The thunder locks player interaction while the scripted red pointer and
    // its light finish their choreography. Keep every other session/UI lock.
    public bool IsSystemInputLockedWithoutChapter02Nightfall => GameSessionFlow.Instance?.IsBusy == true
        || AdditionHandle.Instance?.BlocksPaperInput == true
        || OSSessionPersistence.IsRestoring || _isFirstHeartHealEndingActive
        || Chapter01Ending?.IsPresenting == true
        || Chapter02DemoEnding?.IsPresenting == true
        || _isChapter01RecordAutomationActive
        || _chapter01KeyUnlockSequence != null
        || paperPushStory?.IsTakingOver == true
        || memoryWindowController?.IsSubwindowInputLocked == true;
    public bool IsFirstHeartHealEndingActive =>
        _isFirstHeartHealEndingActive;
    public OSChapter01EndingDirector Chapter01Ending => GetComponent<OSChapter01EndingDirector>();

    public IReadOnlyList<DesktopProcessUnit> DesktopProcessUnits => desktopProcessUnits;
    public IReadOnlyList<ProcessUnit> ProcessUnits => processUnits;
    public IReadOnlyList<OSWindowUnit> WindowUnits => windowUnits;
    public OSMemoryWindowController MemoryWindowController =>
        memoryWindowController;
    public OSRecordWindowController RecordWindowController =>
        recordWindowController;
    public OSToolWindowController ToolWindowController =>
        toolWindowController;
    public OSCharacterWindowController CharacterWindowController =>
        characterWindowController;
    public OSLinkMessageController LinkMessageController =>
        linkMessageController;
    public OSHeartHealToolWindowController HeartHealToolWindowController =>
        heartHealToolWindowController;
    public OSStateCheckWindowController StateCheckWindowController =>
        stateCheckWindowController;
    public OSErrorWindowPoolController ErrorWindowPoolController =>
        errorWindowPoolController;
    public OSSFXAction SFXAction => osSfxAction;
    public bool IsMainStoryGiftLanded { get; private set; }

    public event Action<DesktopProcessUnit> DesktopProcessClicked;
    public event Action<DesktopProcessUnit> DesktopProcessPointerEntered;
    public event Action MainStoryGiftWindowClosedOnce;
    public event Action<int> ChapterStarted;
    public event Action<OSWindowUnit> MouseActiveWindowChanged;

    public event Action<OSWindowUnit> WindowContentInitializationRequested;
    public event Action<OSMemoryReadData> OSMemoryDataShown;
    public event Action<OSMemoryActionData> OSMemoryActionRequested;
    public event Action<OSMemoryResourceChange>
        OSMemoryResourceChangeRequested;
    public HeartbeatWaveformUI HeartbeatWaveform => heartbeatWaveform;

    public void SetCharacterAnimationState(
        string stateName,
        float transitionDuration = 0f,
        int layer = 0)
    {
        characterWindowController?.SetAnimationState(
            stateName,
            transitionDuration,
            layer);
    }

    public bool PlayCharacterResourceAnimation()
    {
        ResolveReferences();
        return characterWindowController != null
            && characterWindowController.PlayResourceAnimation();
    }

    public bool SetCharacterExtendedAnimation(OSCharacterExtendedAnimation animation)
    {
        ResolveReferences();
        return characterWindowController != null
            && characterWindowController.SetExtendedAnimation(animation);
    }

    public bool StopCharacterExtendedAnimation()
    {
        ResolveReferences();
        return characterWindowController != null
            && characterWindowController.StopExtendedAnimation();
    }

    public void SetCharacterWindowState(
        OSCharacterWindowState state,
        bool playTransition = true)
    {
        ResolveReferences();
        characterWindowController?.SetWindowState(state, playTransition);
    }

    public void PrepareChapter01ReconnectState()
    {
        ResolveReferences();
        ProtectChapter01LiveIntro();
        characterWindowController?.PrepareChapter01ReconnectState();
    }

    void ProtectChapter01LiveIntro()
    {
        // Restore before starting any tween linked to the Live GameObject.
        OpenCharacterWindowForReconnect(0f);
        SetChapter01LiveIntroProtected(true);
    }

    void SetChapter01LiveIntroProtected(bool isProtected)
    {
        _isChapter01LiveIntroProtected = isProtected;
        RefreshEvent01LinkCloseSuppression();
    }

    public bool IsWindowDismissalBlocked(OSWindowUnit window)
    {
        // Mandatory story messages also guard taskbar and Escape dismissal.
        // Their internal dialogue transitions use the explicit scripted close path.
        return window != null
            && (Chapter04Entry?.BlocksDismissal(window) == true
                || Chapter03Entry?.BlocksDismissal(window) == true
                || Chapter03Story?.BlocksDismissal(window) == true
                || (_isChapter01LiveIntroProtected && window == characterWindow)
                || (linkMessageController?.IsProgressionChoicePending == true
                    && (window == characterWindow || window == linkMessageController.LinkWindow))
                || (IsChapter02CollectionChoiceProtected
                    && (window == characterWindow || window == linkMessageController?.LinkWindow))
                || (IsCursorExperimentStoryProtected
                    && (window == characterWindow || window == linkMessageController?.LinkWindow))
                || (IsChapter02FinaleStoryProtected
                    && (window == characterWindow || window == linkMessageController?.LinkWindow))
                || (IsChapter02PaperPushStoryProtected
                    && (window == characterWindow || window == linkMessageController?.LinkWindow))
                || (IsChapter02FinalTalkStoryProtected
                    && (window == characterWindow || window == linkMessageController?.LinkWindow))
                || (Chapter02PaperPush?.IsBlockingInput == true && window == paperPushWindow)
                || (IsChapter02ClawOfferProtected && window == clawMachineWindow)
                || (window == cursorExperimentWindow && IsCursorExperimentPending));
    }

    public bool ActivateCharacterInteractionSequence(string sequenceId)
    {
        ResolveReferences();
        return linkMessageController != null
            && linkMessageController.ActivateSequence(sequenceId);
    }

    public void SetEvent01DesktopTutorialSuppressed(bool isSuppressed)
    {
        ResolveReferences();
        _isEvent01DesktopTutorialSuppressed = isSuppressed;
        fileDesktopProcess?.SetTutorialSuppressed(isSuppressed);
        stateCheckDesktopProcess?.SetTutorialSuppressed(isSuppressed);
        RefreshEvent01LinkCloseSuppression();
    }

    public void SetEvent01HeartHealTutorialSuppressed(bool isSuppressed)
    {
        ResolveReferences();
        _isEvent01HeartHealTutorialSuppressed = isSuppressed;
        RefreshEvent01LinkCloseSuppression();
    }

    void RefreshEvent01LinkCloseSuppression()
    {
        bool isSuppressed = _isEvent01DesktopTutorialSuppressed
                            || _isEvent01HeartHealTutorialSuppressed
                            || _isFirstHeartHealEndingActive
                            || linkMessageController?.IsProgressionChoicePending == true;
        SetWindowCloseButtonSuppressed(characterWindow,
            isSuppressed || _isChapter01LiveIntroProtected || IsChapter02CollectionChoiceProtected
                || IsCursorExperimentStoryProtected || IsChapter02FinaleStoryProtected || IsChapter02PaperPushStoryProtected
                || IsChapter02FinalTalkStoryProtected || IsChapter03EntryStoryProtected || Chapter04Entry?.BlocksDismissal(characterWindow) == true || Chapter03Story?.BlocksDismissal(characterWindow) == true);
        SetWindowCloseButtonSuppressed(characterWindow,
            linkMessageController?.IsProgressionChoicePending == true
                || _isChapter01LiveIntroProtected || IsChapter02CollectionChoiceProtected
                || IsCursorExperimentStoryProtected || IsChapter02FinaleStoryProtected || IsChapter02PaperPushStoryProtected
                || IsChapter02FinalTalkStoryProtected || IsChapter03EntryStoryProtected || Chapter04Entry?.BlocksDismissal(characterWindow) == true || Chapter03Story?.BlocksDismissal(characterWindow) == true, OSWindowButton.WindowAction.Minimize);
        SetWindowCloseButtonSuppressed(
            linkMessageController != null
                ? linkMessageController.LinkWindow
                : null,
            isSuppressed || _isChapter01LiveIntroProtected
                || IsChapter02CollectionChoiceProtected
                || IsCursorExperimentStoryProtected
                || IsChapter02FinaleStoryProtected
                || IsChapter02PaperPushStoryProtected
                || IsChapter02FinalTalkStoryProtected
                || IsChapter03EntryStoryProtected
                || Chapter04Entry?.BlocksDismissal(linkMessageController?.LinkWindow) == true || Chapter03Story?.BlocksDismissal(linkMessageController?.LinkWindow) == true
                || linkMessageController?.IsWaitingForExternalCompletion == true);
        SetWindowCloseButtonSuppressed(linkMessageController?.LinkWindow,
            linkMessageController?.IsProgressionChoicePending == true
                || IsChapter02CollectionChoiceProtected || IsCursorExperimentStoryProtected
                || IsChapter02FinaleStoryProtected || IsChapter02PaperPushStoryProtected
                || IsChapter02FinalTalkStoryProtected || IsChapter03EntryStoryProtected || Chapter04Entry?.BlocksDismissal(linkMessageController?.LinkWindow) == true || Chapter03Story?.BlocksDismissal(linkMessageController?.LinkWindow) == true, OSWindowButton.WindowAction.Minimize);
        SetWindowCloseButtonSuppressed(paperPushWindow, Chapter02PaperPush?.IsBlockingInput == true);
        SetWindowCloseButtonSuppressed(paperPushWindow, Chapter02PaperPush?.IsBlockingInput == true,
            OSWindowButton.WindowAction.Minimize);
        SetWindowCloseButtonSuppressed(clawMachineWindow, IsChapter02ClawOfferProtected);
        SetWindowCloseButtonSuppressed(clawMachineWindow, IsChapter02ClawOfferProtected,
            OSWindowButton.WindowAction.Minimize);
    }

    void SetWindowCloseButtonSuppressed(
        OSWindowUnit window,
        bool isSuppressed,
        OSWindowButton.WindowAction action = OSWindowButton.WindowAction.Close)
    {
        if (window == null)
            return;

        OSWindowButton[] buttons =
            window.GetComponentsInChildren<OSWindowButton>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            OSWindowButton button = buttons[i];
            if (button != null
                && button.Action == action
                && button.LinkedWindow == window)
            {
                button.SetTutorialSuppressed(isSuppressed);
            }
        }
    }

    void PlayFolderTutorialHintBurst()
    {
        ResolveReferences();
        if (fileDesktopProcess == null
            || fileDesktopProcess.ProcessIcon == null)
        {
            return;
        }

        OSDesktopProcessHintBurst burst =
            fileDesktopProcess.GetComponent<OSDesktopProcessHintBurst>();
        if (burst == null)
            burst = fileDesktopProcess.gameObject
                .AddComponent<OSDesktopProcessHintBurst>();

        burst.Play(
            fileDesktopProcess.ProcessIcon.rectTransform,
            new Color32(0x9A, 0x98, 0x91, 0xFF));
    }

    public void InitializeMainStory()
    {
        ResolveReferences();
        RefreshLocalization();
        RefreshStateValues();

        if (_isMainStoryInitialized || _hasMainStoryStarted)
            return;

        _isMainStoryInitialized = ResetMainStoryGiftToWaitingState();
        if (_isMainStoryInitialized)
            mainStoryGiftDesktopProcess.gameObject.SetActive(true);
        else
            WarnMissingMainStoryGiftReferences();
    }

    public void StartMainStory()
    {
        if (_hasMainStoryStarted)
            return;

        if (!_isMainStoryInitialized)
            InitializeMainStory();
        if (!_isMainStoryInitialized)
            return;

        PlayMainStoryGiftIntro();
    }

    bool _hasStarted;
    bool _hasWarnedMissingOSCamera;
    bool _isAdditionBKSuppressed;
    bool _additionBKWasActive;
    bool _isEvent01DesktopTutorialSuppressed;
    bool _isEvent01HeartHealTutorialSuppressed;
    bool _isRestoringWindowLayouts;
    bool _isMainStoryInitialized;
    bool _hasMainStoryStarted;
    bool _mainStoryGiftPoseCached;
    bool _mainStoryGiftInteractionCached;
    bool _mainStoryGiftDesktopProcessWasEnabled;
    bool _mainStoryGiftRaycastWasEnabled;
    bool _hasWarnedMissingMainStoryGift;
    bool _hasMainStoryGiftWindowClosed;
    bool _hasChapter01EntryConfirmed;
    bool _isChapter01LiveIntroProtected;
    bool _hasChapter01StateCheckMessageShown;
    bool _hasChapter01RecordHoverMessageShown;
    bool _isChapter01RecordHovered;
    bool _isChapter01RecordPromptRequested;
    bool _isChapter01RecordAutomationActive;
    bool _isClosingStateCheckForChapter01RecordPrompt;
    Vector2 _mainStoryGiftRestPosition;
    Vector3 _mainStoryGiftRestEulerAngles;
    float _mainStoryGiftFallTipRestX;
    Sequence _mainStoryGiftSequence;
    Tween _stateCheckRestartDelayTween;
    Tween _chapter01StateCheckMessageDelayTween;
    Tween _chapter01RecordHoverDelayTween;
    Tween _chapter01KeyWindowOpenDelayTween;
    Sequence _chapter01RecordAutomationSequence;
    Sequence _chapter01KeyUnlockSequence;
    Sequence _firstHeartHealEndingSequence;
    Sequence _firstHeartHealEndingInteractionSequence;
    GameObject _firstHeartHealLockBreakVisual;
    readonly List<Material> _firstHeartHealLockBreakMaterials =
        new List<Material>(2);
    Transform _firstHeartHealKeyOriginalParent;
    int _firstHeartHealKeyOriginalSiblingIndex;
    Vector2 _firstHeartHealKeyOriginalAnchorMin;
    Vector2 _firstHeartHealKeyOriginalAnchorMax;
    Vector2 _firstHeartHealKeyOriginalPivot;
    Vector2 _firstHeartHealKeyOriginalAnchoredPosition;
    Vector2 _firstHeartHealKeyOriginalSizeDelta;
    Quaternion _firstHeartHealKeyOriginalLocalRotation;
    Vector3 _firstHeartHealKeyOriginalLocalScale;
    bool _firstHeartHealKeyWasActive;
    bool _firstHeartHealKeyPoseCached;
    Image _lockHeartGlitchImage;
    Material _lockHeartOriginalMaterial;
    Material _lockHeartGlitchMaterial;
    Image _firstHeartHealAnimatedLockTip;
    Color _firstHeartHealLockTipOriginalColor;
    bool _firstHeartHealLockTipWasActive;
    bool _firstHeartHealLockTipStateCached;
    bool _hasWarnedMissingHeartHealEndingReference;
    bool _hasWarnedMissingLockHeartShader;
    GameDataManager _desktopNewStateDataManager;
    DesktopProcessUnit _draggedDesktopProcess;
    OSDesktopProcessGrid _dragSourceGrid;
    int _dragSourceIndex = -1;
    bool _desktopProcessLayoutInitialized;
    bool _chapter01KeyRuntimeInitialized;
    readonly List<DesktopProcessUnit> _desktopLayoutUnits =
        new List<DesktopProcessUnit>(8);
    OSWindowUnit _mouseActiveWindow;
    OSWindowUnit _lastPointerFocusedWindow;
    int _lastPointerFocusFrame = -1;
    bool _isFirstHeartHealEndingActive;
    bool _suppressCharacterWindowClosedLinkMessage;
    readonly List<RaycastResult> _mouseRaycastResults =
        new List<RaycastResult>(16);

    protected override void Awake()
    {
        base.Awake();
        if (!IsSingletonInstance)
            return;

        ResolveReferences();
        InitializeDesktopProcessNewStates();
        InitializeDesktopProcessLayouts();
        ResetMainStoryGiftToWaitingState();
        InitializeLocalizedTexts();
        InitializeResourceViews();
        InitializeWindowSystem();
        RestoreWindowLayouts();
        IsOSVisible = (osCanvas == null || osCanvas.enabled)
            && (osCanvasGroup == null || osCanvasGroup.alpha > 0f);
        if (GetComponent<OSWindowActivityDimming>() == null)
            gameObject.AddComponent<OSWindowActivityDimming>();
        if (GetComponent<OSChapter03StoryDirector>() == null)
            gameObject.AddComponent<OSChapter03StoryDirector>();
        if (GetComponent<OSChapter04EntryDirector>() == null)
            gameObject.AddComponent<OSChapter04EntryDirector>();
        if (GetComponent<OSChapter04StoryDirector>() == null)
            gameObject.AddComponent<OSChapter04StoryDirector>();
        if (GetComponent<OSChapter04Music>() == null)
            gameObject.AddComponent<OSChapter04Music>();
    }

    void OnEnable()
    {
        if (!IsSingletonInstance)
            return;

        GameEvents.PhaseChanged += OnPhaseChanged;
        GameEvents.ResourceChanged += OnResourceChanged;
        GameEvents.EmptyEnergyUsed += OnEmptyEnergyUsed;
        GameEvents.LanguageChanged += OnLanguageChanged;
        BindDesktopProcessNewStateEvents();
        BindDesktopProcessUnits();
        BindWindows();
        BindChapter01ReconnectEvents();
        InitializeDesktopProcessNewStates();
        InitializeDesktopProcessLayouts();
        RefreshMouseActiveWindowPresentation();
        if (_hasStarted)
        {
            ApplyOSCameraPresentation(IsOSVisible);
            SyncErrorWindowEffect();
            if (IsOSVisible)
                linkMessageController?.NotifyOSBecameVisible();
        }
    }

    void OnDisable()
    {
        ResetChapter01StoryAnimation();
        ResetChapter02StoryAnimation();
        ResetChapter02CollectionIntroduction();
        SetChapter01LiveIntroProtected(false);
        ReleaseCharacterActivityAnimation();
        CancelDesktopProcessDrag();
        CaptureDesktopProcessLayouts();
        CancelChapter01RecordGuidance();
        StopChapter01KeyWindowOpenDelay();
        StopChapter01KeyUnlockAnimation(true);
        CancelFirstHeartHealEnding();
        SetMouseActiveWindow(null);
        StopMainStoryGiftSequence(_hasMainStoryStarted);
        StopStateCheckRestartDelay();
        errorWindowPoolController?.StopEffect();
        CaptureWindowLayouts();
        SetEvent01DesktopTutorialSuppressed(false);
        SetEvent01HeartHealTutorialSuppressed(false);
        SetAdditionBackgroundSuppressed(false);

        GameEvents.PhaseChanged -= OnPhaseChanged;
        GameEvents.ResourceChanged -= OnResourceChanged;
        GameEvents.EmptyEnergyUsed -= OnEmptyEnergyUsed;
        ResetEmptyEnergyReminder();
        GameEvents.LanguageChanged -= OnLanguageChanged;
        UnbindDesktopProcessNewStateEvents();
        UnbindDesktopProcessUnits();
        UnbindWindows();
        UnbindChapter01ReconnectEvents();

        if (IsSingletonInstance)
            ApplyOSCameraPresentation(false);
    }

    protected override void OnDestroy()
    {
        ResetChapter02CollectionIntroduction();
        CancelDesktopProcessDrag();
        CancelChapter01RecordGuidance();
        StopChapter01KeyWindowOpenDelay();
        StopChapter01KeyUnlockAnimation(true);
        CancelFirstHeartHealEnding();
        ReleaseLockHeartIconGlitch();
        StopMainStoryGiftSequence(false);
        StopStateCheckRestartDelay();
        UnbindChapter01ReconnectEvents();
        if (IsSingletonInstance)
            ApplyOSCameraPresentation(false);
        base.OnDestroy();
    }

    void Start()
    {
        _hasStarted = true;
        BindDesktopProcessNewStateEvents();
        BindChapter01ReconnectEvents();
        InitializeDesktopProcessNewStates();
        InitializeDesktopProcessLayouts();
        RestoreWindowLayouts();
        OpenCharacterWindow();
        if (characterWindow != null && characterWindow.IsOpen)
            characterWindow.SetAlwaysOnTop(true);
        RefreshLocalization();
        RefreshStateValues();
        ApplyOSCameraPresentation(IsOSVisible);
        SyncErrorWindowEffect();
        if (IsOSVisible)
            linkMessageController?.NotifyOSBecameVisible();
    }

    void LateUpdate()
    {
        RefreshChapter02CollectionIntroduction();
        RefreshCursorExperiment();
        RefreshChapter02FinaleProtection();
        RefreshChapter02PaperPushProtection();
        RefreshChapter02FinalTalkProtection();
        RefreshChapter03EntryProtection();
        RefreshChapter04EntryProtection();
        RefreshMemoryUpgrade03InstalledMessage();
        RefreshChapter02MemoryMessages();
        linkMessageController?.RefreshNextEntryCountdown();
        linkMessageController?.RefreshOptionCountdownQueue();
        RefreshGramophoneInstalledMessage();
        RefreshChapter02ReadAlongMessages();
        RefreshMemoryUpgrade02InstalledMessage();
        RefreshMotherHeartAccess();
        RefreshChapter01StoryAnimation();
        RefreshChapter02StoryAnimation();
        RefreshCharacterActivityAnimation();
        RefreshStoryFault();
        TickEmptyEnergyReminder();
        TrackMouseWindowFocus();
        Keyboard keyboard = Keyboard.current;
        if (!GameTestConsole.BlocksGameplayInput && keyboard != null && keyboard.rKey.wasPressedThisFrame
            && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
        {
            TryOpenToolFromShortcut();
        }

        if (!_desktopProcessLayoutInitialized
            && GameDataManager.Instance != null)
        {
            InitializeDesktopProcessLayouts();
        }

        if (!_chapter01KeyRuntimeInitialized
            && GameDataManager.Instance != null)
        {
            InitializeChapter01KeyRuntime();
        }

        RefreshChapter02CollectionMessageLayering();

        if (_desktopNewStateDataManager != null
            || GameDataManager.Instance == null)
        {
            return;
        }

        BindDesktopProcessNewStateEvents();
        InitializeDesktopProcessNewStates();
    }

    public void RefreshLocalization()
    {
        GameDataManager dataManager = GameDataManager.Instance;

        for (int i = 0; i < localizedTexts.Length; i++)
            localizedTexts[i]?.Refresh(this);

        for (int i = 0; i < resourceViews.Length; i++)
            resourceViews[i]?.RefreshName(dataManager);
    }

    public string GetLocalizedText(string key, string fallback = null)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
            return OSPalette.TintRichText(dataManager.GetLocalizedText(key, fallback));

        if (!string.IsNullOrEmpty(fallback))
            return OSPalette.TintRichText(fallback);

        return key ?? string.Empty;
    }

    public void RefreshStateValues()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        GameResourceSystem resources = GameResourceSystem.Instance;
        if (dataManager == null && resources == null)
            return;

        for (int i = 0; i < resourceViews.Length; i++)
        {
            OSResourceStateView view = resourceViews[i];
            if (view == null)
                continue;

            ResourceType type = view.ResourceType;
            float value = dataManager != null
                ? dataManager.GetOSResourceValue(type)
                : resources.Get(type);
            float max = resources != null
                ? resources.GetMax(type)
                : OSResourceSaveData.MaxValue;
            view.RefreshValue(value, max);
        }
    }

    /// <summary>
    /// Applies a heart delta immediately to save data and queues its presentation.
    /// </summary>
    public int ChangeHeart(int delta)
    {
        return ApplyResourceChange(ResourceType.Heart, delta);
    }

    /// <summary>
    /// Applies an energy delta immediately to save data and queues its presentation.
    /// </summary>
    public int ChangeEnergy(int delta)
    {
        int before = GameDataManager.Instance != null
            ? GameDataManager.Instance.GetOSResourceValue(ResourceType.Battery)
            : GameResourceSystem.Instance != null
                ? Mathf.RoundToInt(GameResourceSystem.Instance.Get(ResourceType.Battery)) : 0;
        int after = ApplyResourceChange(ResourceType.Battery, delta);
        if (after < before)
            OSUIEnergySpendPresenter.Show(this);
        return after;
    }

    /// <summary>
    /// Applies an error delta immediately to save data and queues its presentation.
    /// </summary>
    public int ChangeError(int delta)
    {
        return ApplyResourceChange(ResourceType.Fault, delta);
    }

    public void PlayInteractionFeedback()
    {
        heartbeatWaveform?.HighlightNextBeat();
    }

    public void PlayHeartResourceShake()
    {
        if (!IsOSVisible || heartbeatPanel == null) return;
        foreach (OSResourceStateView view in resourceViews)
            if (view != null && view.ResourceType == ResourceType.Heart
                && view.transform.IsChildOf(heartbeatPanel))
                view.PlaySubtleShake();
    }

    public void HighlightNextHeartbeat()
    {
        PlayInteractionFeedback();
    }

    public void NotifyWindowPointerPressed(OSWindowUnit window)
    {
        OSWindowUnit topLevelWindow = ResolveTopLevelWindow(window);
        if (!IsWindowMouseFocusable(topLevelWindow))
            return;

        bool alreadyProcessedThisFrame =
            _lastPointerFocusFrame == Time.frameCount
            && _lastPointerFocusedWindow == topLevelWindow;
        if (!alreadyProcessedThisFrame)
        {
            if (window != null && window != topLevelWindow)
                window.BringToFront();

            topLevelWindow.BringToFront();
            _lastPointerFocusFrame = Time.frameCount;
            _lastPointerFocusedWindow = topLevelWindow;
        }

        if (alreadyProcessedThisFrame
            && _mouseActiveWindow == topLevelWindow)
        {
            return;
        }

        SetMouseActiveWindow(topLevelWindow);
    }

    public void NotifyWindowLauncherPressed(OSWindowUnit window)
    {
        if (window == toolWindow)
            TryShowMemoryUpgrade02ToolMessage();
        OSWindowUnit topLevelWindow = ResolveTopLevelWindow(window);
        if (!IsWindowMouseFocusable(topLevelWindow)
            || topLevelWindow.IsAlwaysOnTop
            || topLevelWindow.IsForceTopmost)
        {
            return;
        }

        SetMouseActiveWindow(topLevelWindow);
        CloseErrorWindowsOverlappingStateCheckCore(topLevelWindow);
    }

    public bool IsWindowMouseActive(OSWindowUnit window)
    {
        OSWindowUnit topLevelWindow = ResolveTopLevelWindow(window);
        return IsWindowMouseFocusable(topLevelWindow)
            && topLevelWindow == _mouseActiveWindow;
    }

    public void NotifyTaskbarProcessPressed(OSWindowUnit window)
    {
        if (window == characterWindow && characterWindow.IsOpen)
            Chapter04Story?.ResumeDialogue();
        if (window == toolWindow)
            TryShowMemoryUpgrade02ToolMessage();
        OSWindowUnit topLevelWindow = ResolveTopLevelWindow(window);
        if (!IsWindowMouseFocusable(topLevelWindow))
            return;

        topLevelWindow.BringToFront();
        SetMouseActiveWindow(topLevelWindow);
        CloseErrorWindowsOverlappingStateCheckCore(topLevelWindow);
    }

    public void NotifyWindowUnavailable(OSWindowUnit window)
    {
        if (_mouseActiveWindow == null)
            return;

        if (window == _mouseActiveWindow
            || !IsWindowMouseFocusable(_mouseActiveWindow))
        {
            SetMouseActiveWindow(null);
        }
    }

    void TrackMouseWindowFocus()
    {
        if (GameTestConsole.BlocksGameplayInput || CursorHandle.Instance?.IsScriptControlled == true
            || AdditionHandle.Instance?.BlocksPaperInput == true)
            return;

        Mouse mouse = Mouse.current;
        EventSystem eventSystem = EventSystem.current;
        if (!IsOSVisible
            || mouse == null
            || eventSystem == null
            || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        var pointerData = new PointerEventData(eventSystem)
        {
            position = mouse.position.ReadValue()
        };
        _mouseRaycastResults.Clear();
        eventSystem.RaycastAll(pointerData, _mouseRaycastResults);

        GameObject topHit = _mouseRaycastResults.Count > 0
            ? _mouseRaycastResults[0].gameObject
            : null;
        if (topHit == null || !topHit.transform.IsChildOf(transform))
        {
            SetMouseActiveWindow(null);
            return;
        }

        ProcessUnit hitProcessUnit = topHit.GetComponentInParent<
            ProcessUnit>(true);
        if (hitProcessUnit != null
            && hitProcessUnit.transform.IsChildOf(transform))
        {
            // Preserve the pre-click focus until ProcessUnit decides whether
            // this press activates or minimizes its linked window.
            return;
        }

        OSWindowUnit hitWindow = topHit.GetComponentInParent<
            OSWindowUnit>(true);
        if (hitWindow == null || !hitWindow.transform.IsChildOf(transform))
        {
            SetMouseActiveWindow(null);
            return;
        }

        NotifyWindowPointerPressed(hitWindow);
    }

    void SetMouseActiveWindow(OSWindowUnit window)
    {
        OSWindowUnit nextWindow = ResolveTopLevelWindow(window);
        if (!IsWindowMouseFocusable(nextWindow))
            nextWindow = null;

        bool changed = _mouseActiveWindow != nextWindow;
        _mouseActiveWindow = nextWindow;
        RefreshMouseActiveWindowPresentation();
        if (changed)
            MouseActiveWindowChanged?.Invoke(_mouseActiveWindow);
    }

    void RefreshMouseActiveWindowPresentation()
    {
        if (processUnits != null)
        {
            for (int i = 0; i < processUnits.Count; i++)
            {
                ProcessUnit unit = processUnits[i];
                if (unit == null)
                    continue;

                OSWindowUnit linkedTopLevel =
                    ResolveTopLevelWindow(unit.LinkedWindow);
                unit.SetLinkedWindowTaskbarState(
                    linkedTopLevel != null
                    && linkedTopLevel == _mouseActiveWindow,
                    linkedTopLevel != null
                    && linkedTopLevel.IsAlwaysOnTop);
            }
        }

        if (cablePool == null && heartbeatPanel != null)
        {
            cablePool = heartbeatPanel.GetComponentInChildren<
                OSUICablePoolController>(true);
        }

        cablePool?.SetMouseActiveWindow(_mouseActiveWindow);
    }

    OSWindowUnit ResolveTopLevelWindow(OSWindowUnit window)
    {
        if (window == null)
            return null;

        OSWindowUnit topLevelWindow = window;
        Transform current = window.transform.parent;
        while (current != null && current != transform)
        {
            OSWindowUnit parentWindow = current.GetComponent<OSWindowUnit>();
            if (parentWindow != null)
                topLevelWindow = parentWindow;

            current = current.parent;
        }

        return topLevelWindow;
    }

    bool IsWindowMouseFocusable(OSWindowUnit window)
    {
        return window != null
            && window.transform.IsChildOf(transform)
            && window.IsOpen
            && !window.IsMinimized
            && window.gameObject.activeInHierarchy;
    }

    public bool BeginFirstHeartHealEnding()
    {
        ResolveReferences();
        ResolveFirstHeartHealEndingReferences();
        EnsureFirstHeartHealEndingOverlays();
        if (firstHeartHealBlackBK == null
            || firstHeartHealDimFilter == null)
        {
            Debug.LogError(
                "[OS] First heart-heal ending overlays are missing.",
                this);
            return false;
        }

        if (_isFirstHeartHealEndingActive)
            return false;

        GameDataManager.Instance?.SetCurrentHeartHealProcessIndex(0);
        PrepareFirstHeartHealEndingInteraction();
        EnsureLockHeartIconGlitch();
        _isFirstHeartHealEndingActive = true;
        RefreshEvent01LinkCloseSuppression();
        linkMessageController?.SetGlobalMessagesSuppressed(true);
        PrepareFirstHeartHealEndingOverlays();
        PlayFirstHeartHealSecretAmbient();
        SetHeartbeatResourceDisplayOverride("???");
        CloseOpenWindowsForFirstHeartHealEnding();

        bool sequenceActivated = linkMessageController != null
            && linkMessageController.ActivateSequence(
                OSCharacterInteractionIds
                    .PrologueEvent01HeartHealEnding);
        if (!sequenceActivated)
        {
            Debug.LogWarning(
                "[OS] First heart-heal ending LinkMessage sequence "
                + "could not be activated.",
                this);
        }

        PlayFirstHeartHealEndingTimeline();
        return true;
    }

    public void ReleaseFirstHeartHealEndingPresentation()
    {
        CancelFirstHeartHealEnding();
    }

    void PlayFirstHeartHealSecretAmbient()
    {
        AudioData ambient = GameDataManager.Instance?.AudioData?.BGM?.Find(
            item => item != null
                && item.keyid == HeartHealEndingAmbientKey);
        if (ambient?.audioclip == null)
        {
            Debug.LogWarning(
                $"[OS] Ambient '{HeartHealEndingAmbientKey}' is missing.",
                this);
            return;
        }

        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            Debug.LogWarning(
                "[OS] AudioManager is missing; secret ambient was not played.",
                this);
            return;
        }

        audioManager.StopAmbient(Mathf.Max(0f, firstHeartHealAmbientFadeDuration));
        audioManager.PlayMusic(
            ambient.audioclip,
            Mathf.Max(0f, firstHeartHealAmbientFadeDuration), true, .7f);
    }

    static void PlayFirstHeartHealSfx(string keyId)
    {
        AudioManager.Instance?.PlaySfx(keyId);
    }

    void PlayFirstHeartHealEndingTimeline()
    {
        StopFirstHeartHealEndingTimeline();
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingSequence = sequence;
        sequence.AppendInterval(
            Mathf.Max(0f, firstHeartHealBlackHoldDuration));
        sequence.Append(firstHeartHealBlackBK
            .DOFade(
                0f,
                Mathf.Max(0.05f, firstHeartHealBlackFadeDuration))
            .SetEase(Ease.Linear));
        sequence.AppendCallback(() =>
        {
            if (firstHeartHealBlackBK != null)
                firstHeartHealBlackBK.gameObject.SetActive(false);
        });
        sequence.AppendInterval(
            Mathf.Max(0f, firstHeartHealLiveOpenDelay));
        sequence.AppendCallback(OpenFirstHeartHealEndingCharacter);
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingSequence == sequence)
                    _firstHeartHealEndingSequence = null;
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingSequence == sequence)
                    _firstHeartHealEndingSequence = null;
            });
    }

    void OpenFirstHeartHealEndingCharacter()
    {
        if (!_isFirstHeartHealEndingActive || characterWindow == null)
            return;

        bool wasOpen = characterWindow.IsOpen;
        characterWindow.OpenAnimationCompleted -=
            HandleFirstHeartHealEndingCharacterOpened;
        characterWindow.OpenAnimationCompleted +=
            HandleFirstHeartHealEndingCharacterOpened;
        characterWindowController?.ApplyStateImmediate(
            OSCharacterWindowState.VoidMessage);
        characterWindow.SetOpeningLayout(
            firstHeartHealCharacterPosition,
            firstHeartHealCharacterSize);

        bool opened = OpenWindow(
            characterWindow,
            characterDesktopProcess,
            "WindowUnit_Character");
        if (characterWindow.IsOpen)
        {
            characterWindow.SetAlwaysOnTop(true);
            characterWindow.BringToFront();
        }

        if (opened || characterWindow.IsOpen)
        {
            PlayFirstHeartHealSfx(HeartHealEndingOpenSfxKey);
            if (!wasOpen && characterWindow.IsOpen)
            {
                PlayFirstHeartHealSfx(
                    HeartHealEndingLiveOpenSfxKey);
            }
            return;
        }

        characterWindow.OpenAnimationCompleted -=
            HandleFirstHeartHealEndingCharacterOpened;
        Debug.LogWarning(
            "[OS] Character Live could not open for the first "
            + "heart-heal ending.",
            this);
    }

    void HandleFirstHeartHealEndingCharacterOpened(OSWindowUnit source)
    {
        if (source != characterWindow)
            return;

        characterWindow.OpenAnimationCompleted -=
            HandleFirstHeartHealEndingCharacterOpened;
        if (!_isFirstHeartHealEndingActive)
            return;

        linkMessageController?.NotifyExternalSignal(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealEndingLiveOpened);
    }

    void CloseCharacterWindowWithoutLinkMessage()
    {
        _suppressCharacterWindowClosedLinkMessage = true;
        try
        {
            if (linkMessageController != null)
                linkMessageController.CloseOwnerWindowSilently();
            else if (characterWindow != null && characterWindow.IsOpen)
                characterWindow.Close();
        }
        finally
        {
            _suppressCharacterWindowClosedLinkMessage = false;
        }
    }

    void CloseOpenWindowsForFirstHeartHealEnding()
    {
        CloseCharacterWindowWithoutLinkMessage();

        OSWindowUnit linkWindow = linkMessageController != null
            ? linkMessageController.LinkWindow
            : null;
        if (windowUnits != null)
        {
            for (int i = 0; i < windowUnits.Count; i++)
            {
                OSWindowUnit window = windowUnits[i];
                if (window == null
                    || window == characterWindow
                    || window == linkWindow
                    || !window.IsOpen)
                {
                    continue;
                }

                window.CloseSilently();
            }
        }

        SetMouseActiveWindow(null);
    }

    void PrepareFirstHeartHealEndingOverlays()
    {
        Transform windowsRoot = transform.Find("Windows");
        if (windowsRoot != null)
        {
            Transform dimTransform = firstHeartHealDimFilter.transform;
            int windowsIndex = windowsRoot.GetSiblingIndex();
            int dimIndex = dimTransform.GetSiblingIndex();
            dimTransform.SetSiblingIndex(
                dimIndex < windowsIndex
                    ? Mathf.Max(0, windowsIndex - 1)
                    : windowsIndex);
        }

        firstHeartHealDimFilter.raycastTarget = true;
        firstHeartHealDimFilter.color = new Color(
            0f,
            0f,
            0f,
            Mathf.Clamp01(firstHeartHealDimAlpha));
        firstHeartHealDimFilter.gameObject.SetActive(true);

        firstHeartHealBlackBK.transform.SetAsLastSibling();
        Image blackImage = firstHeartHealBlackBK.GetComponent<Image>();
        if (blackImage != null)
        {
            blackImage.color = Color.black;
            blackImage.raycastTarget = true;
        }

        firstHeartHealBlackBK.alpha = 1f;
        firstHeartHealBlackBK.interactable = false;
        firstHeartHealBlackBK.blocksRaycasts = true;
        firstHeartHealBlackBK.gameObject.SetActive(true);
    }

    void SetHeartbeatResourceDisplayOverride(string displayText)
    {
        if (heartbeatPanel == null || resourceViews == null)
            return;

        for (int i = 0; i < resourceViews.Length; i++)
        {
            OSResourceStateView view = resourceViews[i];
            if (view != null && view.transform.IsChildOf(heartbeatPanel))
                view.SetValueDisplayOverride(displayText);
        }
    }

    void ClearHeartbeatResourceDisplayOverride()
    {
        if (heartbeatPanel == null || resourceViews == null)
            return;

        for (int i = 0; i < resourceViews.Length; i++)
        {
            OSResourceStateView view = resourceViews[i];
            if (view != null && view.transform.IsChildOf(heartbeatPanel))
                view.ClearValueDisplayOverride();
        }
    }

    void CancelFirstHeartHealEnding()
    {
        if (!_isFirstHeartHealEndingActive
            && _firstHeartHealEndingSequence == null
            && _firstHeartHealEndingInteractionSequence == null
            && CursorHandle.Instance?.IsScriptControlledBy(this) != true)
        {
            return;
        }

        if (characterWindow != null)
        {
            characterWindow.OpenAnimationCompleted -=
                HandleFirstHeartHealEndingCharacterOpened;
        }

        StopFirstHeartHealEndingTimeline();
        StopFirstHeartHealEndingInteractionSequence();
        UnbindFirstHeartHealEndingTransientEvents();
        linkMessageController?.StopDisplayedTrailingDotsLoop(false);
        CursorHandle.Instance?.ReleaseScriptControl(this);
        RestoreFirstHeartHealEndingKey();
        DestroyFirstHeartHealLockBreakVisual(true);
        if (firstHeartHealSpecialWindow != null)
            firstHeartHealSpecialWindow.PrepareRuntimeClosed();
        if (firstHeartHealSpecialWindow01 != null)
            firstHeartHealSpecialWindow01.PrepareRuntimeClosed();
        if (firstHeartHealBlackBK != null)
        {
            firstHeartHealBlackBK.alpha = 0f;
            firstHeartHealBlackBK.gameObject.SetActive(false);
        }
        if (firstHeartHealDimFilter != null)
            firstHeartHealDimFilter.gameObject.SetActive(false);

        ClearHeartbeatResourceDisplayOverride();
        RefreshStateValues();
        linkMessageController?.SetGlobalMessagesSuppressed(false);
        _isFirstHeartHealEndingActive = false;
        RefreshEvent01LinkCloseSuppression();
    }

    void StopFirstHeartHealEndingTimeline()
    {
        if (_firstHeartHealEndingSequence == null)
            return;

        Sequence sequence = _firstHeartHealEndingSequence;
        _firstHeartHealEndingSequence = null;
        sequence.Kill(false);
    }

    void PrepareFirstHeartHealEndingInteraction()
    {
        StopFirstHeartHealEndingInteractionSequence();
        UnbindFirstHeartHealEndingTransientEvents();
        CursorHandle.Instance?.ReleaseScriptControl(this);
        RestoreFirstHeartHealEndingKey();
        DestroyFirstHeartHealLockBreakVisual(true);
        firstHeartHealSpecialWindow?.PrepareRuntimeClosed();
        firstHeartHealSpecialWindow01?.PrepareRuntimeClosed();
    }

    void OpenFirstHeartHealEndingSpecialWindow()
    {
        if (!_isFirstHeartHealEndingActive
            || firstHeartHealSpecialWindow == null)
        {
            WarnMissingFirstHeartHealEndingReference(
                "WindowUnit_Special_00");
            return;
        }

        RestoreFirstHeartHealEndingKey();
        bool wasOpen = firstHeartHealSpecialWindow.IsOpen
            && firstHeartHealSpecialWindow.gameObject.activeInHierarchy;
        firstHeartHealSpecialWindow.OpenAnimationCompleted -=
            HandleFirstHeartHealEndingSpecialWindowOpened;
        if (!wasOpen)
        {
            firstHeartHealSpecialWindow.OpenAnimationCompleted +=
                HandleFirstHeartHealEndingSpecialWindowOpened;
        }

        firstHeartHealSpecialWindow.OpenStandalone();
        firstHeartHealSpecialWindow.SetAlwaysOnTop(true);
        firstHeartHealSpecialWindow.BringToFront();
        if (wasOpen)
            NotifyFirstHeartHealEndingSpecialWindowOpened();
    }

    void HandleFirstHeartHealEndingSpecialWindowOpened(OSWindowUnit source)
    {
        if (source != firstHeartHealSpecialWindow)
            return;

        firstHeartHealSpecialWindow.OpenAnimationCompleted -=
            HandleFirstHeartHealEndingSpecialWindowOpened;
        NotifyFirstHeartHealEndingSpecialWindowOpened();
    }

    void NotifyFirstHeartHealEndingSpecialWindowOpened()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        linkMessageController?.NotifyExternalSignal(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealEndingSpecialOpened);
    }

    void BeginFirstHeartHealCursorTakeover()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        StopFirstHeartHealEndingInteractionSequence();
        CursorHandle cursor = CursorHandle.Instance;
        if (cursor == null || !cursor.AcquireScriptControl(this))
        {
            WarnMissingFirstHeartHealEndingReference("CursorHandle");
            NotifyFirstHeartHealCursorReady();
            return;
        }

        PlayFirstHeartHealSfx(HeartHealEndingMouseSfxKey);
        float duration = Mathf.Max(
            0.05f,
            firstHeartHealCursorTakeoverDuration);
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.Join(DOTween.To(
            () => cursor.ScriptScaleMultiplier,
            value => cursor.SetScriptAppearance(
                this,
                value,
                cursor.ScriptTint),
            Mathf.Max(1f, firstHeartHealCursorTakeoverScale),
            duration));
        sequence.Join(DOTween.To(
            () => cursor.ScriptTint,
            value => cursor.SetScriptAppearance(
                this,
                cursor.ScriptScaleMultiplier,
                value),
            firstHeartHealCursorTakeoverColor,
            duration));
        sequence
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
                NotifyFirstHeartHealCursorReady();
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
                cursor.SetScriptPressed(this, false);
            });
    }

    void NotifyFirstHeartHealCursorReady()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        linkMessageController?.NotifyExternalSignal(
            OSCharacterInteractionIds.Event01Signals
                .HeartHealEndingCursorReady);
    }

    void ScheduleFirstHeartHealTakeKeySelection()
    {
        ScheduleFirstHeartHealOptionSelection(
            HeartHealEndingTakeKeyOptionId,
            firstHeartHealAutoClickDelay);
    }

    void ScheduleFirstHeartHealCheckResultSelection()
    {
        ScheduleFirstHeartHealOptionSelection(
            HeartHealEndingKeepSafeOptionId,
            firstHeartHealCheckNormalReadDelay);
    }

    void ScheduleFirstHeartHealReturnMessage()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        StopFirstHeartHealEndingInteractionSequence();
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealPostRelockReadDelay));
        sequence.AppendCallback(() =>
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;
            linkMessageController?.NotifyExternalSignal(
                OSCharacterInteractionIds.Event01Signals
                    .HeartHealEndingReturnReady);
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            });
    }

    void ScheduleFirstHeartHealOptionSelection(
        string optionId,
        float delay)
    {
        if (!_isFirstHeartHealEndingActive
            || linkMessageController == null)
        {
            return;
        }

        CursorHandle cursor = CursorHandle.Instance;
        if (cursor == null
            || !cursor.IsScriptControlledBy(this)
            || !linkMessageController.TryGetActiveOptionTarget(
                optionId,
                out RectTransform optionTarget))
        {
            WarnMissingFirstHeartHealEndingReference(
                $"{optionId} LinkMessage option");
            return;
        }

        StopFirstHeartHealEndingInteractionSequence();
        Vector2 targetScreenPosition = GetRectScreenCenter(optionTarget);
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.AppendInterval(Mathf.Max(0f, delay));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            targetScreenPosition,
            firstHeartHealCursorMoveDuration,
            1f));
        sequence.AppendCallback(() =>
        {
            linkMessageController.TrySetScriptOptionHover(
                this,
                optionId,
                true);
        });
        sequence.AppendInterval(Mathf.Max(0f, firstHeartHealCursorAimPause));
        sequence.AppendCallback(() =>
        {
            cursor.SetScriptPressed(this, true);
        });
        sequence.AppendInterval(Mathf.Max(
            0.05f,
            firstHeartHealCursorPressDuration));
        sequence.AppendCallback(() =>
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;
            cursor.SetScriptPressed(this, false);
            linkMessageController.TrySelectOptionFromScript(
                this,
                optionId);
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            });
    }

    void HandleFirstHeartHealTakeKeyOptionSelected()
    {
        if (!_isFirstHeartHealEndingActive
            || linkMessageController?.LinkWindow == null)
        {
            return;
        }

        OSWindowUnit linkWindow = linkMessageController.LinkWindow;
        linkWindow.Closed -= HandleFirstHeartHealEndingLinkWindowClosed;
        linkWindow.Closed += HandleFirstHeartHealEndingLinkWindowClosed;
    }

    void HandleFirstHeartHealEndingLinkWindowClosed(OSWindowUnit source)
    {
        if (linkMessageController == null
            || source != linkMessageController.LinkWindow)
        {
            return;
        }

        source.Closed -= HandleFirstHeartHealEndingLinkWindowClosed;
        if (_isFirstHeartHealEndingActive)
            PlayFirstHeartHealKeyDrag();
    }

    void HandleFirstHeartHealCheckResultOptionSelected()
    {
        if (!_isFirstHeartHealEndingActive
            || linkMessageController?.LinkWindow == null)
        {
            return;
        }

        OSWindowUnit linkWindow = linkMessageController.LinkWindow;
        linkWindow.Closed -=
            HandleFirstHeartHealCheckResultLinkWindowClosed;
        linkWindow.Closed +=
            HandleFirstHeartHealCheckResultLinkWindowClosed;
    }

    void HandleFirstHeartHealCheckResultLinkWindowClosed(
        OSWindowUnit source)
    {
        if (linkMessageController == null
            || source != linkMessageController.LinkWindow)
        {
            return;
        }

        source.Closed -= HandleFirstHeartHealCheckResultLinkWindowClosed;
        if (_isFirstHeartHealEndingActive)
            PlayFirstHeartHealRelockSequence();
    }

    void HandleFirstHeartHealReturnOptionSelected()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        PlayFirstHeartHealSfx(HeartHealEndingShutdownSfxKey);
        AudioManager.Instance?.StopMusic(firstHeartHealReturnAmbientFadeDuration);
        MainGameHandle.Instance?.RestoreSceneAmbientAfterOS(
            firstHeartHealReturnAmbientFadeDuration);
        CloseOSInterface();
        CancelFirstHeartHealEnding();
    }

    void PlayFirstHeartHealKeyDrag()
    {
        CursorHandle cursor = CursorHandle.Instance;
        Image lockTip = lockHeartDesktopProcess != null
            ? lockHeartDesktopProcess.ImageLockTip
            : null;
        if (cursor == null
            || !cursor.IsScriptControlledBy(this)
            || firstHeartHealSpecialKey == null
            || lockTip == null)
        {
            WarnMissingFirstHeartHealEndingReference(
                "Special Key or LockHeart LockTip");
            return;
        }

        StopFirstHeartHealEndingInteractionSequence();
        Vector2 keyScreenPosition = GetRectScreenCenter(
            firstHeartHealSpecialKey);
        Vector2 keyExtractionScreenPosition =
            GetFirstHeartHealKeyExtractionScreenPosition(
                keyScreenPosition);
        Vector2 keyReleaseDriftScreenPosition =
            keyExtractionScreenPosition
            + Vector2.left
            * Mathf.Max(0f, firstHeartHealKeyReleaseDriftDistance);
        Vector2 lockScreenPosition = GetRectScreenCenter(
            lockTip.rectTransform);
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.AppendInterval(Mathf.Max(0f, firstHeartHealKeyObserveDelay));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            keyScreenPosition,
            firstHeartHealCursorMoveDuration,
            -1f));
        sequence.AppendInterval(Mathf.Max(0f, firstHeartHealCursorAimPause));
        sequence.AppendCallback(() =>
        {
            BeginDraggingFirstHeartHealKey(cursor);
            cursor.SetScriptPressed(this, true);
        });
        sequence.AppendInterval(Mathf.Max(
            0.05f,
            firstHeartHealCursorPressDuration));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            keyExtractionScreenPosition,
            firstHeartHealKeyExtractDuration,
            -1f,
            screenPosition => SetRectScreenCenter(
                firstHeartHealSpecialKey,
                screenPosition)));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, false));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            keyReleaseDriftScreenPosition,
            firstHeartHealKeyReleaseDriftDuration,
            -1f));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealKeyExtractPause));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            keyExtractionScreenPosition,
            firstHeartHealKeyRegrabDuration,
            1f));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, true));
        sequence.AppendInterval(Mathf.Max(
            0.05f,
            firstHeartHealCursorPressDuration));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            lockScreenPosition,
            firstHeartHealKeyDragDuration,
            1f,
            screenPosition => SetRectScreenCenter(
                firstHeartHealSpecialKey,
                screenPosition)));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealCursorReleasePause));
        sequence.AppendCallback(() =>
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;
            cursor.SetScriptPressed(this, false);
            if (firstHeartHealSpecialKey != null)
                firstHeartHealSpecialKey.gameObject.SetActive(false);
            PlayFirstHeartHealLockOpenAnimation(lockTip);
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
                cursor.SetScriptPressed(this, false);
            });
    }

    Vector2 GetFirstHeartHealKeyExtractionScreenPosition(
        Vector2 keyScreenPosition)
    {
        RectTransform windowRect = firstHeartHealSpecialWindow != null
            ? firstHeartHealSpecialWindow.WindowRect
            : null;
        if (windowRect == null)
            return keyScreenPosition - new Vector2(72f, 0f);

        Camera eventCamera = GetRectEventCamera(windowRect);
        var corners = new Vector3[4];
        windowRect.GetWorldCorners(corners);
        float leftEdge = float.MaxValue;
        for (int i = 0; i < corners.Length; i++)
        {
            leftEdge = Mathf.Min(
                leftEdge,
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    corners[i]).x);
        }

        float keyHalfWidth = 0f;
        var keyCorners = new Vector3[4];
        firstHeartHealSpecialKey.GetWorldCorners(keyCorners);
        float keyLeft = float.MaxValue;
        float keyRight = float.MinValue;
        Camera keyCamera = GetRectEventCamera(firstHeartHealSpecialKey);
        for (int i = 0; i < keyCorners.Length; i++)
        {
            float x = RectTransformUtility.WorldToScreenPoint(
                keyCamera,
                keyCorners[i]).x;
            keyLeft = Mathf.Min(keyLeft, x);
            keyRight = Mathf.Max(keyRight, x);
        }

        if (keyRight >= keyLeft)
            keyHalfWidth = (keyRight - keyLeft) * 0.5f;

        float targetX = Mathf.Min(
            keyScreenPosition.x - 36f,
            leftEdge
            - keyHalfWidth
            - Mathf.Max(0f, firstHeartHealKeyExitPadding));
        return new Vector2(targetX, keyScreenPosition.y);
    }

    void BeginDraggingFirstHeartHealKey(CursorHandle cursor)
    {
        if (firstHeartHealSpecialKey == null || cursor == null)
            return;

        CacheFirstHeartHealEndingKeyPose();
        Transform sfxRoot = FindOSEffectsRoot();
        if (sfxRoot != null)
        {
            firstHeartHealSpecialKey.SetParent(sfxRoot, true);
            firstHeartHealSpecialKey.SetAsLastSibling();
        }

        SetRectScreenCenter(
            firstHeartHealSpecialKey,
            cursor.DisplayedScreenPosition);
    }

    Tween CreateFirstHeartHealCursorMoveTween(
        CursorHandle cursor,
        Vector2 targetScreenPosition,
        float duration,
        float curveDirection,
        Action<Vector2> positionChanged = null)
    {
        Vector2 start = Vector2.zero;
        Vector2 controlA = Vector2.zero;
        Vector2 controlB = Vector2.zero;
        return DOTween.To(
                () => 0f,
                progress =>
                {
                    Vector2 position = EvaluateCubicBezier(
                        start,
                        controlA,
                        controlB,
                        targetScreenPosition,
                        progress);
                    cursor.SetScriptScreenPosition(this, position);
                    positionChanged?.Invoke(position);
                },
                1f,
                Mathf.Max(0.05f, duration))
            .SetEase(Ease.InOutSine)
            .OnStart(() =>
            {
                start = cursor.DisplayedScreenPosition;
                CalculateHumanCursorControls(
                    start,
                    targetScreenPosition,
                    curveDirection,
                    out controlA,
                    out controlB);
            });
    }

    static void CalculateHumanCursorControls(
        Vector2 start,
        Vector2 target,
        float curveDirection,
        out Vector2 controlA,
        out Vector2 controlB)
    {
        Vector2 delta = target - start;
        float distance = delta.magnitude;
        if (distance <= 0.01f)
        {
            controlA = start;
            controlB = target;
            return;
        }

        Vector2 direction = delta / distance;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        float curveSign = Mathf.Approximately(curveDirection, 0f)
            ? 1f
            : Mathf.Sign(curveDirection);
        float bend = Mathf.Clamp(distance * 0.14f, 8f, 55f) * curveSign;
        float overshoot = Mathf.Clamp(distance * 0.025f, 2f, 8f);
        controlA = start + delta * 0.32f + perpendicular * bend;
        controlB = target
                   + direction * overshoot
                   - perpendicular * bend * 0.12f;
    }

    static Vector2 EvaluateCubicBezier(
        Vector2 start,
        Vector2 controlA,
        Vector2 controlB,
        Vector2 end,
        float progress)
    {
        float t = Mathf.Clamp01(progress);
        float inverse = 1f - t;
        return inverse * inverse * inverse * start
               + 3f * inverse * inverse * t * controlA
               + 3f * inverse * t * t * controlB
               + t * t * t * end;
    }

    void StopFirstHeartHealEndingInteractionSequence()
    {
        if (_firstHeartHealEndingInteractionSequence == null)
            return;

        Sequence sequence = _firstHeartHealEndingInteractionSequence;
        _firstHeartHealEndingInteractionSequence = null;
        sequence.Kill(false);
    }

    void UnbindFirstHeartHealEndingTransientEvents()
    {
        if (firstHeartHealSpecialWindow != null)
        {
            firstHeartHealSpecialWindow.OpenAnimationCompleted -=
                HandleFirstHeartHealEndingSpecialWindowOpened;
        }

        if (firstHeartHealSpecialWindow01 != null)
        {
            firstHeartHealSpecialWindow01.OpenAnimationCompleted -=
                HandleFirstHeartHealEndingSpecialWindow01Opened;
        }

        if (linkMessageController?.LinkWindow != null)
        {
            linkMessageController.LinkWindow.Closed -=
                HandleFirstHeartHealEndingLinkWindowClosed;
            linkMessageController.LinkWindow.Closed -=
                HandleFirstHeartHealCheckResultLinkWindowClosed;
        }
    }

    void CacheFirstHeartHealEndingKeyPose()
    {
        if (_firstHeartHealKeyPoseCached || firstHeartHealSpecialKey == null)
            return;

        _firstHeartHealKeyOriginalParent = firstHeartHealSpecialKey.parent;
        _firstHeartHealKeyOriginalSiblingIndex =
            firstHeartHealSpecialKey.GetSiblingIndex();
        _firstHeartHealKeyOriginalAnchorMin = firstHeartHealSpecialKey.anchorMin;
        _firstHeartHealKeyOriginalAnchorMax = firstHeartHealSpecialKey.anchorMax;
        _firstHeartHealKeyOriginalPivot = firstHeartHealSpecialKey.pivot;
        _firstHeartHealKeyOriginalAnchoredPosition =
            firstHeartHealSpecialKey.anchoredPosition;
        _firstHeartHealKeyOriginalSizeDelta = firstHeartHealSpecialKey.sizeDelta;
        _firstHeartHealKeyOriginalLocalRotation =
            firstHeartHealSpecialKey.localRotation;
        _firstHeartHealKeyOriginalLocalScale =
            firstHeartHealSpecialKey.localScale;
        _firstHeartHealKeyWasActive =
            firstHeartHealSpecialKey.gameObject.activeSelf;
        _firstHeartHealKeyPoseCached = true;
    }

    void RestoreFirstHeartHealEndingKey()
    {
        if (!_firstHeartHealKeyPoseCached || firstHeartHealSpecialKey == null)
            return;

        if (_firstHeartHealKeyOriginalParent != null)
        {
            firstHeartHealSpecialKey.SetParent(
                _firstHeartHealKeyOriginalParent,
                false);
            firstHeartHealSpecialKey.SetSiblingIndex(Mathf.Clamp(
                _firstHeartHealKeyOriginalSiblingIndex,
                0,
                Mathf.Max(0, _firstHeartHealKeyOriginalParent.childCount - 1)));
        }

        firstHeartHealSpecialKey.anchorMin =
            _firstHeartHealKeyOriginalAnchorMin;
        firstHeartHealSpecialKey.anchorMax =
            _firstHeartHealKeyOriginalAnchorMax;
        firstHeartHealSpecialKey.pivot = _firstHeartHealKeyOriginalPivot;
        firstHeartHealSpecialKey.anchoredPosition =
            _firstHeartHealKeyOriginalAnchoredPosition;
        firstHeartHealSpecialKey.sizeDelta =
            _firstHeartHealKeyOriginalSizeDelta;
        firstHeartHealSpecialKey.localRotation =
            _firstHeartHealKeyOriginalLocalRotation;
        firstHeartHealSpecialKey.localScale =
            _firstHeartHealKeyOriginalLocalScale;
        firstHeartHealSpecialKey.gameObject.SetActive(
            _firstHeartHealKeyWasActive);
    }

    void PlayFirstHeartHealLockOpenAnimation(Image lockTip)
    {
        PlayDesktopProcessLockOpenAnimation(
            lockTip,
            true,
            CompleteFirstHeartHealLockOpen);
    }

    void PlayChapter01KeyUnlockAnimation(DesktopProcessUnit lockTarget)
    {
        Image lockTip = lockTarget?.ImageLockTip;
        if (lockTarget == null || lockTip == null)
            return;

        StopChapter01KeyUnlockAnimation(true);
        PlayDesktopProcessLockOpenAnimation(
            lockTip,
            false,
            () =>
            {
                SetDesktopProcessLocked(lockTarget, false);
                DiscardCachedLockTipState();
            });
    }

    void PlayDesktopProcessLockOpenAnimation(
        Image lockTip,
        bool isFirstHeartHealSequence,
        Action completed)
    {
        if (lockTip == null)
        {
            completed?.Invoke();
            return;
        }

        PlayFirstHeartHealSfx(HeartHealEndingOpenLockSfxKey);
        DestroyFirstHeartHealLockBreakVisual(false);
        CacheFirstHeartHealLockTipState(lockTip);
        Shader splitShader = Shader.Find(LockHeartSplitShaderName);
        if (splitShader == null)
        {
            WarnMissingLockHeartShader(LockHeartSplitShaderName);
            Sequence fallbackSequence = DOTween.Sequence();
            SetLockOpenSequence(
                fallbackSequence,
                isFirstHeartHealSequence);
            fallbackSequence.Append(lockTip
                .DOFade(0f, firstHeartHealLockOpenDuration)
                .SetEase(Ease.OutQuad));
            fallbackSequence
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .OnComplete(() =>
                {
                    ClearLockOpenSequence(
                        fallbackSequence,
                        isFirstHeartHealSequence);
                    completed?.Invoke();
                })
                .OnKill(() =>
                {
                    ClearLockOpenSequence(
                        fallbackSequence,
                        isFirstHeartHealSequence);
                });
            return;
        }

        RectTransform lockRect = lockTip.rectTransform;
        Transform visualParent = FindOSEffectsRoot()
            ?? lockRect.parent;
        RectTransform visualRoot = CreateMatchingRectTransform(
            "LockOpenVisual",
            lockRect,
            visualParent);
        if (visualRoot == null)
        {
            completed?.Invoke();
            return;
        }

        visualRoot.SetAsLastSibling();
        CanvasGroup group = visualRoot.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;
        _firstHeartHealLockBreakVisual = visualRoot.gameObject;

        Vector2 size = lockRect.rect.size;
        if (size.x <= 0.01f || size.y <= 0.01f)
            size = lockRect.sizeDelta;
        float topHeight = Mathf.Min(6f, Mathf.Max(1f, size.y));
        float splitY = Mathf.Clamp01(1f - topHeight / Mathf.Max(1f, size.y));
        float topPivotY = splitY + (1f - splitY) * 0.5f;
        float bottomPivotY = splitY * 0.5f;
        RectTransform topPiece = CreateFirstHeartHealLockPiece(
            "Top",
            visualRoot,
            lockTip,
            splitShader,
            size,
            new Vector2(0f, topPivotY),
            splitY,
            true);
        RectTransform bottomPiece = CreateFirstHeartHealLockPiece(
            "Bottom",
            visualRoot,
            lockTip,
            splitShader,
            size,
            new Vector2(0f, bottomPivotY),
            splitY,
            false);
        lockTip.gameObject.SetActive(false);

        float duration = Mathf.Max(0.05f, firstHeartHealLockOpenDuration);
        Sequence sequence = DOTween.Sequence();
        SetLockOpenSequence(sequence, isFirstHeartHealSequence);
        if (topPiece != null)
        {
            sequence.Join(topPiece
                .DOLocalRotate(new Vector3(0f, 0f, 20f), duration)
                .SetEase(Ease.OutQuad));
        }
        if (bottomPiece != null)
        {
            sequence.Join(bottomPiece
                .DOLocalRotate(new Vector3(0f, 0f, -20f), duration)
                .SetEase(Ease.OutQuad));
        }
        sequence.Join(visualRoot
            .DOAnchorPosY(
                visualRoot.anchoredPosition.y
                    - Mathf.Max(0f, firstHeartHealLockDropDistance),
                duration)
            .SetEase(Ease.InQuad));
        sequence.Join(group
            .DOFade(0f, duration)
            .SetEase(Ease.InQuad));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                ClearLockOpenSequence(
                    sequence,
                    isFirstHeartHealSequence);
                DestroyFirstHeartHealLockBreakVisual(false);
                completed?.Invoke();
            })
            .OnKill(() =>
            {
                ClearLockOpenSequence(
                    sequence,
                    isFirstHeartHealSequence);
            });
    }

    void SetLockOpenSequence(
        Sequence sequence,
        bool isFirstHeartHealSequence)
    {
        if (isFirstHeartHealSequence)
            _firstHeartHealEndingInteractionSequence = sequence;
        else
            _chapter01KeyUnlockSequence = sequence;
    }

    void ClearLockOpenSequence(
        Sequence sequence,
        bool isFirstHeartHealSequence)
    {
        if (isFirstHeartHealSequence)
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;
            return;
        }

        if (_chapter01KeyUnlockSequence == sequence)
            _chapter01KeyUnlockSequence = null;
    }

    void StopChapter01KeyUnlockAnimation(bool restoreLockVisual)
    {
        if (_chapter01KeyUnlockSequence == null)
            return;

        Sequence sequence = _chapter01KeyUnlockSequence;
        _chapter01KeyUnlockSequence = null;
        sequence.Kill(false);
        DestroyFirstHeartHealLockBreakVisual(restoreLockVisual);
    }

    void CompleteFirstHeartHealLockOpen()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        if (firstHeartHealSpecialWindow != null)
        {
            if (firstHeartHealSpecialWindow.IsOpen)
                firstHeartHealSpecialWindow.Close();
            else
                firstHeartHealSpecialWindow.PrepareRuntimeClosed();
        }

        OpenFirstHeartHealEndingSpecialWindow01();
    }

    void OpenFirstHeartHealEndingSpecialWindow01()
    {
        if (firstHeartHealSpecialWindow01 == null)
        {
            WarnMissingFirstHeartHealEndingReference(
                "WindowUnit_Special_01");
            return;
        }

        RectTransform windowRect = firstHeartHealSpecialWindow01.WindowRect;
        if (windowRect != null)
        {
            Vector2 authoredSize = windowRect.rect.size;
            if (authoredSize.x > 0.01f && authoredSize.y > 0.01f)
            {
                firstHeartHealSpecialWindow01.SetOpeningLayout(
                    windowRect.anchoredPosition,
                    authoredSize);
            }
        }

        EnsureFirstHeartHealQuestionGlitch();
        bool wasOpen = firstHeartHealSpecialWindow01.IsOpen
            && firstHeartHealSpecialWindow01.gameObject.activeInHierarchy;
        firstHeartHealSpecialWindow01.OpenAnimationCompleted -=
            HandleFirstHeartHealEndingSpecialWindow01Opened;
        if (!wasOpen)
        {
            firstHeartHealSpecialWindow01.OpenAnimationCompleted +=
                HandleFirstHeartHealEndingSpecialWindow01Opened;
        }

        firstHeartHealSpecialWindow01.OpenStandalone();
        firstHeartHealSpecialWindow01.SetAlwaysOnTop(true);
        firstHeartHealSpecialWindow01.BringToFront();
        if (wasOpen)
            PlayFirstHeartHealEndingWindowArrangement();
    }

    void EnsureFirstHeartHealQuestionGlitch()
    {
        if (firstHeartHealSpecialWindow01 == null)
            return;

        TextMeshProUGUI questionText = firstHeartHealSpecialWindow01
            .transform.Find("Content/Text (TMP)")
            ?.GetComponent<TextMeshProUGUI>();
        if (questionText == null)
        {
            TextMeshProUGUI[] texts = firstHeartHealSpecialWindow01
                .GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TextMeshProUGUI candidate = texts[i];
                if (candidate != null
                    && string.Equals(
                        candidate.text?.Trim(),
                        "?",
                        StringComparison.Ordinal))
                {
                    questionText = candidate;
                    break;
                }
            }
        }

        if (questionText == null)
        {
            WarnMissingFirstHeartHealEndingReference(
                "WindowUnit_Special_01 question TMP");
            return;
        }

        OSNoSignalGlitch glitch =
            questionText.GetComponent<OSNoSignalGlitch>();
        if (glitch == null && Application.isPlaying)
            glitch = questionText.gameObject.AddComponent<OSNoSignalGlitch>();
        if (glitch == null)
            return;

        glitch.ConfigureScrambledText(true, 5, 0.09f);
        glitch.enabled = true;
        glitch.RestartEffect();
    }

    void HandleFirstHeartHealEndingSpecialWindow01Opened(
        OSWindowUnit source)
    {
        if (source != firstHeartHealSpecialWindow01)
            return;

        firstHeartHealSpecialWindow01.OpenAnimationCompleted -=
            HandleFirstHeartHealEndingSpecialWindow01Opened;
        PlayFirstHeartHealEndingWindowArrangement();
    }

    void PlayFirstHeartHealEndingWindowArrangement()
    {
        CursorHandle cursor = CursorHandle.Instance;
        if (!_isFirstHeartHealEndingActive
            || cursor == null
            || !cursor.IsScriptControlledBy(this)
            || firstHeartHealSpecialWindow01 == null
            || characterWindow == null)
        {
            WarnMissingFirstHeartHealEndingReference(
                "scripted window arrangement");
            return;
        }

        RectTransform specialDragArea = ResolveWindowDragArea(
            firstHeartHealSpecialWindow01);
        RectTransform characterDragArea = ResolveWindowDragArea(
            characterWindow);
        if (specialDragArea == null || characterDragArea == null)
        {
            WarnMissingFirstHeartHealEndingReference(
                "window DragArea");
            return;
        }

        StopFirstHeartHealEndingInteractionSequence();
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealNewWindowReactionDelay));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            GetRectScreenCenter(specialDragArea),
            firstHeartHealCursorMoveDuration,
            -1f));
        sequence.AppendInterval(Mathf.Max(0f, firstHeartHealCursorAimPause));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, true));
        sequence.AppendInterval(Mathf.Max(
            0.05f,
            firstHeartHealCursorPressDuration));
        sequence.Append(CreateFirstHeartHealWindowDragTween(
            cursor,
            firstHeartHealSpecialWindow01,
            firstHeartHealSpecialWindow01Position,
            firstHeartHealWindowDragDuration,
            1f));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealCursorReleasePause));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, false));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealBetweenWindowDragsDelay));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            GetRectScreenCenter(characterDragArea),
            firstHeartHealCursorMoveDuration,
            1f));
        sequence.AppendInterval(Mathf.Max(0f, firstHeartHealCursorAimPause));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, true));
        sequence.AppendInterval(Mathf.Max(
            0.05f,
            firstHeartHealCursorPressDuration));
        sequence.Append(CreateFirstHeartHealWindowDragTween(
            cursor,
            characterWindow,
            firstHeartHealArrangedCharacterPosition,
            firstHeartHealWindowDragDuration,
            -1f));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealCursorReleasePause));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, false));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealArrangedMessageDelay));
        sequence.AppendCallback(() =>
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;
            linkMessageController?.NotifyExternalSignal(
                OSCharacterInteractionIds.Event01Signals
                    .HeartHealEndingWindowsArranged);
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
                cursor.SetScriptPressed(this, false);
            });
    }

    void PlayFirstHeartHealWindowInspection()
    {
        CursorHandle cursor = CursorHandle.Instance;
        RectTransform windowRect = firstHeartHealSpecialWindow01 != null
            ? firstHeartHealSpecialWindow01.WindowRect
            : null;
        RectTransform dragArea = ResolveWindowDragArea(
            firstHeartHealSpecialWindow01);
        if (!_isFirstHeartHealEndingActive
            || cursor == null
            || !cursor.IsScriptControlledBy(this)
            || windowRect == null
            || dragArea == null)
        {
            WarnMissingFirstHeartHealEndingReference(
                "scripted Special_01 inspection");
            return;
        }

        Vector2 targetWindowPosition = windowRect.anchoredPosition
                                       + Vector2.right
                                       * Mathf.Max(
                                           0f,
                                           firstHeartHealInspectionNudgeDistance);
        Vector2 firstSweepTarget = GetProjectedRectScreenPoint(
            windowRect,
            targetWindowPosition,
            new Vector2(0.32f, 0.58f));
        Vector2 secondSweepTarget = GetProjectedRectScreenPoint(
            windowRect,
            targetWindowPosition,
            new Vector2(0.68f, 0.43f));
        Vector2 thirdSweepTarget = GetProjectedRectScreenPoint(
            windowRect,
            targetWindowPosition,
            new Vector2(0.44f, 0.72f));

        StopFirstHeartHealEndingInteractionSequence();
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            GetRectScreenCenter(dragArea),
            firstHeartHealCursorMoveDuration,
            1f));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealCursorAimPause));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, true));
        sequence.AppendInterval(Mathf.Max(
            0.05f,
            firstHeartHealCursorPressDuration));
        sequence.Append(CreateFirstHeartHealWindowDragTween(
            cursor,
            firstHeartHealSpecialWindow01,
            targetWindowPosition,
            firstHeartHealInspectionNudgeDuration,
            -1f));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealCursorReleasePause * 0.5f));
        sequence.AppendCallback(() => cursor.SetScriptPressed(this, false));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealInspectionSweepPause * 2f));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            firstSweepTarget,
            firstHeartHealInspectionSweepDuration,
            -1f));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealInspectionSweepPause));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            secondSweepTarget,
            firstHeartHealInspectionSweepDuration,
            1f));
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealInspectionSweepPause));
        sequence.Append(CreateFirstHeartHealCursorMoveTween(
            cursor,
            thirdSweepTarget,
            firstHeartHealInspectionSweepDuration,
            -1f));
        sequence.AppendInterval(HeartHealEndingInspectionCompletionPause);
        sequence.AppendCallback(() =>
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;
            linkMessageController?.StopDisplayedTrailingDotsLoop(false);
            linkMessageController?.NotifyExternalSignal(
                OSCharacterInteractionIds.Event01Signals
                    .HeartHealEndingInspectionComplete);
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
                cursor.SetScriptPressed(this, false);
            });
    }

    void PlayFirstHeartHealRelockSequence()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        CursorHandle cursor = CursorHandle.Instance;
        Image lockTip = lockHeartDesktopProcess != null
            ? lockHeartDesktopProcess.ImageLockTip
            : null;
        RectTransform lockProcessTarget =
            lockHeartDesktopProcess?.ProcessIcon != null
                ? lockHeartDesktopProcess.ProcessIcon.rectTransform
                : lockTip != null
                    ? lockTip.rectTransform
                    : null;

        StopFirstHeartHealEndingInteractionSequence();
        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealSpecial01CloseDelay));
        sequence.AppendCallback(() =>
        {
            if (firstHeartHealSpecialWindow01 == null)
                return;

            if (firstHeartHealSpecialWindow01.IsOpen)
                firstHeartHealSpecialWindow01.Close();
            else
                firstHeartHealSpecialWindow01.PrepareRuntimeClosed();
        });
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealBeforeRelockMoveDelay));
        if (cursor != null
            && cursor.IsScriptControlledBy(this)
            && lockProcessTarget != null)
        {
            sequence.Append(CreateFirstHeartHealCursorMoveTween(
                cursor,
                GetRectScreenCenter(lockProcessTarget),
                firstHeartHealCursorMoveDuration,
                -1f));
            sequence.AppendInterval(Mathf.Max(
                0f,
                firstHeartHealCursorAimPause));
        }
        else
        {
            WarnMissingFirstHeartHealEndingReference(
                "scripted relock cursor or LockHeart process");
        }

        sequence.AppendCallback(() =>
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;

            if (lockTip != null)
                PlayFirstHeartHealLockCloseAnimation(lockTip);
            else
                CompleteFirstHeartHealLockClose();
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
                cursor?.SetScriptPressed(this, false);
            });
    }

    void PlayFirstHeartHealLockCloseAnimation(Image lockTip)
    {
        if (lockTip == null)
        {
            CompleteFirstHeartHealLockClose();
            return;
        }

        DestroyFirstHeartHealLockBreakVisual(false);
        CacheFirstHeartHealLockTipState(lockTip);
        float duration = Mathf.Max(0.05f, firstHeartHealLockOpenDuration);
        Shader splitShader = Shader.Find(LockHeartSplitShaderName);
        if (splitShader == null)
        {
            WarnMissingLockHeartShader(LockHeartSplitShaderName);
            Color targetColor = _firstHeartHealLockTipStateCached
                ? _firstHeartHealLockTipOriginalColor
                : lockTip.color;
            Color transparentColor = targetColor;
            transparentColor.a = 0f;
            lockTip.color = transparentColor;
            lockTip.gameObject.SetActive(true);

            Sequence fallbackSequence = DOTween.Sequence();
            _firstHeartHealEndingInteractionSequence = fallbackSequence;
            fallbackSequence.Append(lockTip
                .DOColor(targetColor, duration)
                .SetEase(Ease.OutQuad));
            fallbackSequence
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .OnComplete(() =>
                {
                    if (_firstHeartHealEndingInteractionSequence
                        == fallbackSequence)
                    {
                        _firstHeartHealEndingInteractionSequence = null;
                    }
                    DestroyFirstHeartHealLockBreakVisual(true);
                    CompleteFirstHeartHealLockClose();
                })
                .OnKill(() =>
                {
                    if (_firstHeartHealEndingInteractionSequence
                        == fallbackSequence)
                    {
                        _firstHeartHealEndingInteractionSequence = null;
                    }
                });
            return;
        }

        RectTransform lockRect = lockTip.rectTransform;
        Transform visualParent = FindOSEffectsRoot()
            ?? lockRect.parent;
        RectTransform visualRoot = CreateMatchingRectTransform(
            "LockCloseVisual",
            lockRect,
            visualParent);
        if (visualRoot == null)
        {
            DestroyFirstHeartHealLockBreakVisual(true);
            CompleteFirstHeartHealLockClose();
            return;
        }

        visualRoot.SetAsLastSibling();
        float targetY = visualRoot.anchoredPosition.y;
        visualRoot.anchoredPosition = new Vector2(
            visualRoot.anchoredPosition.x,
            targetY - Mathf.Max(0f, firstHeartHealLockDropDistance));
        CanvasGroup group = visualRoot.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        _firstHeartHealLockBreakVisual = visualRoot.gameObject;

        Vector2 size = lockRect.rect.size;
        if (size.x <= 0.01f || size.y <= 0.01f)
            size = lockRect.sizeDelta;
        float topHeight = Mathf.Min(6f, Mathf.Max(1f, size.y));
        float splitY = Mathf.Clamp01(1f - topHeight / Mathf.Max(1f, size.y));
        float topPivotY = splitY + (1f - splitY) * 0.5f;
        float bottomPivotY = splitY * 0.5f;
        RectTransform topPiece = CreateFirstHeartHealLockPiece(
            "Top",
            visualRoot,
            lockTip,
            splitShader,
            size,
            new Vector2(0f, topPivotY),
            splitY,
            true);
        RectTransform bottomPiece = CreateFirstHeartHealLockPiece(
            "Bottom",
            visualRoot,
            lockTip,
            splitShader,
            size,
            new Vector2(0f, bottomPivotY),
            splitY,
            false);
        if (topPiece != null)
            topPiece.localRotation = Quaternion.Euler(0f, 0f, 20f);
        if (bottomPiece != null)
            bottomPiece.localRotation = Quaternion.Euler(0f, 0f, -20f);
        lockTip.gameObject.SetActive(false);

        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        if (topPiece != null)
        {
            sequence.Join(topPiece
                .DOLocalRotate(Vector3.zero, duration)
                .SetEase(Ease.OutQuad));
        }
        if (bottomPiece != null)
        {
            sequence.Join(bottomPiece
                .DOLocalRotate(Vector3.zero, duration)
                .SetEase(Ease.OutQuad));
        }
        sequence.Join(visualRoot
            .DOAnchorPosY(targetY, duration)
            .SetEase(Ease.OutQuad));
        sequence.Join(group
            .DOFade(1f, duration)
            .SetEase(Ease.OutQuad));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
                DestroyFirstHeartHealLockBreakVisual(true);
                CompleteFirstHeartHealLockClose();
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            });
    }

    void CompleteFirstHeartHealLockClose()
    {
        if (!_isFirstHeartHealEndingActive)
            return;

        CursorHandle cursor = CursorHandle.Instance;
        if (cursor?.IsScriptControlledBy(this) == true)
        {
            cursor.SetScriptPressed(this, false);
            cursor.ReleaseScriptControlAtDisplayedPosition(this);
        }

        Sequence sequence = DOTween.Sequence();
        _firstHeartHealEndingInteractionSequence = sequence;
        sequence.AppendInterval(Mathf.Max(
            0f,
            firstHeartHealRelockedMessageDelay));
        sequence.AppendCallback(() =>
        {
            if (_firstHeartHealEndingInteractionSequence == sequence)
                _firstHeartHealEndingInteractionSequence = null;
            linkMessageController?.NotifyExternalSignal(
                OSCharacterInteractionIds.Event01Signals
                    .HeartHealEndingRelocked);
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            })
            .OnKill(() =>
            {
                if (_firstHeartHealEndingInteractionSequence == sequence)
                    _firstHeartHealEndingInteractionSequence = null;
            });
    }

    static RectTransform ResolveWindowDragArea(OSWindowUnit window)
    {
        if (window == null)
            return null;

        return window.transform.Find("DragArea") as RectTransform
               ?? window.WindowRect;
    }

    Tween CreateFirstHeartHealWindowDragTween(
        CursorHandle cursor,
        OSWindowUnit window,
        Vector2 targetAnchoredPosition,
        float duration,
        float curveDirection)
    {
        RectTransform windowRect = window != null
            ? window.WindowRect
            : null;
        RectTransform parentRect = windowRect != null
            ? windowRect.parent as RectTransform
            : null;
        Vector2 startWindowPosition = Vector2.zero;
        Vector2 startCursorPosition = Vector2.zero;
        Vector2 startCursorLocal = Vector2.zero;
        Vector2 targetCursorPosition = Vector2.zero;
        Vector2 controlA = Vector2.zero;
        Vector2 controlB = Vector2.zero;
        Camera eventCamera = GetRectEventCamera(parentRect);
        bool hasParentCoordinates = false;

        return DOTween.To(
                () => 0f,
                progress =>
                {
                    Vector2 cursorPosition = EvaluateCubicBezier(
                        startCursorPosition,
                        controlA,
                        controlB,
                        targetCursorPosition,
                        progress);
                    cursor.SetScriptScreenPosition(this, cursorPosition);

                    if (windowRect == null)
                        return;

                    if (hasParentCoordinates
                        && RectTransformUtility
                            .ScreenPointToLocalPointInRectangle(
                                parentRect,
                                cursorPosition,
                                eventCamera,
                                out Vector2 cursorLocal))
                    {
                        windowRect.anchoredPosition =
                            startWindowPosition
                            + cursorLocal
                            - startCursorLocal;
                    }
                    else
                    {
                        windowRect.anchoredPosition = Vector2.LerpUnclamped(
                            startWindowPosition,
                            targetAnchoredPosition,
                            progress);
                    }
                },
                1f,
                Mathf.Max(0.05f, duration))
            .SetEase(Ease.InOutSine)
            .OnStart(() =>
            {
                if (windowRect == null || parentRect == null)
                    return;

                startWindowPosition = windowRect.anchoredPosition;
                startCursorPosition = cursor.DisplayedScreenPosition;
                hasParentCoordinates = RectTransformUtility
                    .ScreenPointToLocalPointInRectangle(
                        parentRect,
                        startCursorPosition,
                        eventCamera,
                        out startCursorLocal);
                if (hasParentCoordinates)
                {
                    Vector2 targetCursorLocal = startCursorLocal
                        + targetAnchoredPosition
                        - startWindowPosition;
                    targetCursorPosition =
                        RectTransformUtility.WorldToScreenPoint(
                            eventCamera,
                            parentRect.TransformPoint(targetCursorLocal));
                }
                else
                {
                    targetCursorPosition = startCursorPosition;
                }

                CalculateHumanCursorControls(
                    startCursorPosition,
                    targetCursorPosition,
                    curveDirection,
                    out controlA,
                    out controlB);
            })
            .OnComplete(() =>
            {
                if (windowRect != null)
                    windowRect.anchoredPosition = targetAnchoredPosition;
                cursor.SetScriptScreenPosition(this, targetCursorPosition);
            });
    }

    RectTransform CreateFirstHeartHealLockPiece(
        string pieceName,
        RectTransform parent,
        Image source,
        Shader shader,
        Vector2 size,
        Vector2 pivot,
        float splitY,
        bool isTop)
    {
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
        Material material = new Material(shader)
        {
            name = $"Runtime_{pieceName}_LockSplit",
            hideFlags = HideFlags.DontSave
        };
        ConfigureSpriteUV(material, source.sprite);
        material.SetFloat("_SplitY", splitY);
        material.SetFloat("_Part", isTop ? 1f : 0f);
        image.material = material;
        _firstHeartHealLockBreakMaterials.Add(material);
        return rect;
    }

    static RectTransform CreateMatchingRectTransform(
        string objectName,
        RectTransform source,
        Transform parent)
    {
        if (source == null || parent == null)
            return null;

        GameObject rootObject = new GameObject(
            objectName,
            typeof(RectTransform));
        rootObject.layer = source.gameObject.layer;
        RectTransform rect = rootObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = source.pivot;
        Vector2 sourceSize = source.rect.size;
        rect.sizeDelta = sourceSize.x > 0.01f && sourceSize.y > 0.01f
            ? sourceSize
            : source.sizeDelta;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        SetRectScreenCenter(rect, GetRectScreenCenter(source));
        return rect;
    }

    void CacheFirstHeartHealLockTipState(Image lockTip)
    {
        if (_firstHeartHealLockTipStateCached || lockTip == null)
            return;

        _firstHeartHealAnimatedLockTip = lockTip;
        _firstHeartHealLockTipOriginalColor = lockTip.color;
        _firstHeartHealLockTipWasActive = lockTip.gameObject.activeSelf;
        _firstHeartHealLockTipStateCached = true;
    }

    void DiscardCachedLockTipState()
    {
        _firstHeartHealLockTipStateCached = false;
        _firstHeartHealAnimatedLockTip = null;
    }

    void DestroyFirstHeartHealLockBreakVisual(bool restoreOriginal)
    {
        if (_firstHeartHealLockBreakVisual != null)
        {
            Destroy(_firstHeartHealLockBreakVisual);
            _firstHeartHealLockBreakVisual = null;
        }

        for (int i = 0; i < _firstHeartHealLockBreakMaterials.Count; i++)
        {
            Material material = _firstHeartHealLockBreakMaterials[i];
            if (material != null)
                Destroy(material);
        }
        _firstHeartHealLockBreakMaterials.Clear();

        if (!restoreOriginal
            || !_firstHeartHealLockTipStateCached
            || _firstHeartHealAnimatedLockTip == null)
        {
            return;
        }

        _firstHeartHealAnimatedLockTip.color =
            _firstHeartHealLockTipOriginalColor;
        _firstHeartHealAnimatedLockTip.gameObject.SetActive(
            _firstHeartHealLockTipWasActive);
        _firstHeartHealLockTipStateCached = false;
        _firstHeartHealAnimatedLockTip = null;
    }

    public void SetLockHeartIconGlitch(bool active)
    {
        ResolveReferences();
        if (active)
            EnsureLockHeartIconGlitch();
        else
            ReleaseLockHeartIconGlitch();
    }

    void EnsureLockHeartIconGlitch()
    {
        if (IsMotherHeartUnlocked) { ReleaseLockHeartIconGlitch(); return; }
        if (!Application.isPlaying
            || lockHeartDesktopProcess?.ProcessIcon == null)
        {
            return;
        }

        Image icon = lockHeartDesktopProcess.ProcessIcon;
        if (_lockHeartGlitchMaterial != null)
        {
            if (icon.material != _lockHeartGlitchMaterial)
                icon.material = _lockHeartGlitchMaterial;
            return;
        }

        Shader shader = Shader.Find(LockHeartGlitchShaderName);
        if (shader == null)
        {
            WarnMissingLockHeartShader(LockHeartGlitchShaderName);
            return;
        }

        _lockHeartGlitchImage = icon;
        _lockHeartOriginalMaterial = icon.material;
        _lockHeartGlitchMaterial = new Material(shader)
        {
            name = "Runtime_LockHeartPersistentGlitch",
            hideFlags = HideFlags.DontSave
        };
        ConfigureSpriteUV(_lockHeartGlitchMaterial, icon.sprite);
        int seedHash = Animator.StringToHash(
            icon.name + "/" + lockHeartDesktopProcess.ProcessKey);
        _lockHeartGlitchMaterial.SetFloat(
            "_Seed",
            Mathf.Abs(seedHash % 997) / 997f);
        icon.material = _lockHeartGlitchMaterial;
    }

    void ReleaseLockHeartIconGlitch()
    {
        if (_lockHeartGlitchImage != null
            && _lockHeartGlitchImage.material == _lockHeartGlitchMaterial)
        {
            _lockHeartGlitchImage.material = _lockHeartOriginalMaterial;
        }

        if (_lockHeartGlitchMaterial != null)
        {
            if (Application.isPlaying) Destroy(_lockHeartGlitchMaterial);
            else DestroyImmediate(_lockHeartGlitchMaterial);
        }
        _lockHeartGlitchMaterial = null;
        _lockHeartOriginalMaterial = null;
        _lockHeartGlitchImage = null;
    }

    static void ConfigureSpriteUV(Material material, Sprite sprite)
    {
        if (material == null || sprite == null || sprite.texture == null)
            return;

        Texture texture = sprite.texture;
        Rect textureRect = sprite.textureRect;
        material.SetVector("_UVRect", new Vector4(
            textureRect.xMin / Mathf.Max(1f, texture.width),
            textureRect.yMin / Mathf.Max(1f, texture.height),
            textureRect.width / Mathf.Max(1f, texture.width),
            textureRect.height / Mathf.Max(1f, texture.height)));
        material.SetVector("_SpriteSize", new Vector4(
            Mathf.Max(1f, sprite.rect.width),
            Mathf.Max(1f, sprite.rect.height),
            0f,
            0f));
    }

    static Vector2 GetRectScreenCenter(RectTransform rect)
    {
        if (rect == null)
            return Vector2.zero;

        Camera eventCamera = GetRectEventCamera(rect);
        Vector3 worldCenter = rect.TransformPoint(rect.rect.center);
        return RectTransformUtility.WorldToScreenPoint(
            eventCamera,
            worldCenter);
    }

    static Vector2 GetProjectedRectScreenPoint(
        RectTransform rect,
        Vector2 targetAnchoredPosition,
        Vector2 normalizedPosition)
    {
        if (rect == null)
            return Vector2.zero;

        Rect localRect = rect.rect;
        Vector2 normalized = new Vector2(
            Mathf.Clamp01(normalizedPosition.x),
            Mathf.Clamp01(normalizedPosition.y));
        Vector3 localPoint = new Vector3(
            Mathf.Lerp(localRect.xMin, localRect.xMax, normalized.x),
            Mathf.Lerp(localRect.yMin, localRect.yMax, normalized.y),
            0f);
        Vector3 worldPoint = rect.TransformPoint(localPoint);
        if (rect.parent is RectTransform parentRect)
        {
            Vector2 anchoredDelta = targetAnchoredPosition
                                    - rect.anchoredPosition;
            worldPoint += parentRect.TransformVector(
                new Vector3(anchoredDelta.x, anchoredDelta.y, 0f));
        }

        return RectTransformUtility.WorldToScreenPoint(
            GetRectEventCamera(rect),
            worldPoint);
    }

    static void SetRectScreenCenter(
        RectTransform rect,
        Vector2 screenPosition)
    {
        if (rect == null || !(rect.parent is RectTransform parentRect))
            return;

        Camera eventCamera = GetRectEventCamera(parentRect);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                screenPosition,
                eventCamera,
                out Vector2 localPosition))
        {
            return;
        }

        Vector3 nextPosition = rect.localPosition;
        nextPosition.x = localPosition.x;
        nextPosition.y = localPosition.y;
        rect.localPosition = nextPosition;
    }

    static Camera GetRectEventCamera(RectTransform rect)
    {
        Canvas canvas = rect != null
            ? rect.GetComponentInParent<Canvas>()
            : null;
        return canvas != null
            && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
    }

    void WarnMissingFirstHeartHealEndingReference(string referenceName)
    {
        if (_hasWarnedMissingHeartHealEndingReference)
            return;

        _hasWarnedMissingHeartHealEndingReference = true;
        Debug.LogWarning(
            "[OS] First heart-heal ending is missing "
            + $"'{referenceName}'.",
            this);
    }

    void WarnMissingLockHeartShader(string shaderName)
    {
        if (_hasWarnedMissingLockHeartShader)
            return;

        _hasWarnedMissingLockHeartShader = true;
        Debug.LogWarning(
            $"[OS] Shader '{shaderName}' could not be found.",
            this);
    }

    void ResolveFirstHeartHealEndingReferences()
    {
        Transform effectsRoot = FindOSEffectsRoot();
        if (firstHeartHealBlackBK == null)
        {
            firstHeartHealBlackBK = effectsRoot
                ?.Find("HeartHealEndBlackBK")
                ?.GetComponent<CanvasGroup>();
        }

        if (firstHeartHealDimFilter == null)
        {
            firstHeartHealDimFilter = transform
                .Find("HeartHealEndDimFilter")
                ?.GetComponent<Image>();
        }

        if (firstHeartHealSpecialWindow == null)
        {
            Transform specialWindow = effectsRoot
                ?.Find("WindowUnit_Special_00")
                ?? effectsRoot?.Find("WindowUnit_Special");
            firstHeartHealSpecialWindow = specialWindow
                ?.GetComponent<OSWindowUnit>();
        }

        if (firstHeartHealSpecialWindow01 == null)
        {
            firstHeartHealSpecialWindow01 = effectsRoot
                ?.Find("WindowUnit_Special_01")
                ?.GetComponent<OSWindowUnit>();
        }

        if (firstHeartHealSpecialKey == null
            && firstHeartHealSpecialWindow != null)
        {
            firstHeartHealSpecialKey = firstHeartHealSpecialWindow.transform
                .Find("Content/Key") as RectTransform;
        }
    }

    void ResolveChapter01KeyReferences()
    {
        Transform effectsRoot = FindOSEffectsRoot();
        if (chapter01KeyWindow == null)
        {
            chapter01KeyWindow = effectsRoot
                ?.Find("WindowUnit_Special_02")
                ?.GetComponent<OSWindowUnit>();
        }

        Transform content = chapter01KeyWindow != null
            ? chapter01KeyWindow.transform.Find("Content")
            : null;
        if (chapter01KeySourceGrid == null && content != null)
        {
            chapter01KeySourceGrid =
                content.GetComponent<OSDesktopProcessGrid>();
            if (chapter01KeySourceGrid == null && Application.isPlaying)
            {
                chapter01KeySourceGrid =
                    content.gameObject.AddComponent<OSDesktopProcessGrid>();
            }
        }

        if (chapter01KeyDesktopProcess == null && content != null)
        {
            chapter01KeyDesktopProcess = content
                .Find("DesktopProcessUnit_KeyX")
                ?.GetComponent<DesktopProcessUnit>();
        }

        if (chapter01KeySourceGrid != null
            && !string.Equals(
                chapter01KeySourceGrid.ContainerId,
                Chapter01KeySourceContainerId,
                StringComparison.Ordinal))
        {
            chapter01KeySourceGrid.Configure(
                Chapter01KeySourceContainerId,
                new RectOffset(7, 7, 7, 0),
                new Vector2(35f, 35.5f),
                Vector2.zero,
                GridLayoutGroup.Corner.UpperLeft,
                GridLayoutGroup.Axis.Horizontal);
        }

        TMP_Text keyName = chapter01KeyDesktopProcess != null
            ? chapter01KeyDesktopProcess.ProcessName
            : null;
        if (keyName != null)
            keyName.gameObject.SetActive(false);

        OSLocalizedText topName = chapter01KeyWindow != null
            ? chapter01KeyWindow.transform.Find("TopName")
                ?.GetComponent<OSLocalizedText>()
            : null;
        topName?.SetLocalization(
            "ui.os.window.key_box.name",
            "小盒子",
            this);
    }

    Transform FindOSEffectsRoot()
    {
        return transform.Find("OS_SFX")
               ?? transform.Find("OS_VFX");
    }

    void EnsureFirstHeartHealEndingOverlays()
    {
        if (!Application.isPlaying)
            return;

        Transform sfxRoot = FindOSEffectsRoot();
        if (firstHeartHealBlackBK == null && sfxRoot != null)
        {
            Image image = CreateFirstHeartHealEndingOverlay(
                "HeartHealEndBlackBK",
                sfxRoot,
                Color.black);
            if (image != null)
                firstHeartHealBlackBK = image.gameObject.AddComponent<CanvasGroup>();
        }

        if (firstHeartHealDimFilter == null)
        {
            firstHeartHealDimFilter = CreateFirstHeartHealEndingOverlay(
                "HeartHealEndDimFilter",
                transform,
                new Color(0f, 0f, 0f, firstHeartHealDimAlpha));
        }
    }

    static Image CreateFirstHeartHealEndingOverlay(
        string objectName,
        Transform parent,
        Color color)
    {
        if (parent == null)
            return null;

        GameObject overlayObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        overlayObject.layer = parent.gameObject.layer;
        RectTransform rect = overlayObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        Image image = overlayObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        overlayObject.SetActive(false);
        return image;
    }

    public void CloseOSInterface()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogError(
                "[OS] GameManager is missing; cannot return to side-scroll.",
                this);
            return;
        }

        gameManager.ReturnToSideScroll();
    }

    public bool TryCloseFrontmostNormalWindow()
    {
        ResolveReferences();

        if (AdditionHandle.Instance?.BlocksPaperInput == true)
            return true;

        if (_isFirstHeartHealEndingActive || Chapter01Ending?.IsPresenting == true
            || Chapter02DemoEnding?.IsPresenting == true
            || Chapter02FinalTalk?.IsNightfallTransitionBlockingInput == true)
            return true;

        // Consume Escape while an Event01 modal interaction is in progress.
        if (_isEvent01DesktopTutorialSuppressed
            || _isEvent01HeartHealTutorialSuppressed)
        {
            return true;
        }

        OSWindowUnit frontmostWindow = null;
        int frontmostSiblingIndex = int.MinValue;
        for (int i = 0; i < windowUnits.Count; i++)
        {
            OSWindowUnit window = windowUnits[i];
            // Chapter four keeps this child alive only as a hidden typewriter.
            // Escape targets the visible Live host, never its playback carrier.
            if (linkMessageController?.IsChapter04ChatVisible == true
                && window == linkMessageController.LinkWindow) continue;
            if (window == null
                || !window.IsOpen
                || window.IsMinimized
                || !window.gameObject.activeInHierarchy
                || window.IsAlwaysOnTop
                || window.IsForceTopmost)
            {
                continue;
            }

            int siblingIndex = window.transform.GetSiblingIndex();
            if (frontmostWindow != null
                && siblingIndex <= frontmostSiblingIndex)
            {
                continue;
            }

            frontmostWindow = window;
            frontmostSiblingIndex = siblingIndex;
        }

        if (frontmostWindow == null)
            return false;

        if (frontmostWindow == linkMessageController?.LinkWindow
            && linkMessageController.IsWaitingForExternalCompletion)
        {
            return true;
        }

        frontmostWindow.Close();
        return true;
    }


    int ApplyResourceChange(ResourceType type, int delta)
    {
        GameResourceSystem resources = GameResourceSystem.Instance;
        if (resources != null)
        {
            resources.Modify(type, delta);
            return Mathf.RoundToInt(resources.Get(type));
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return OSResourceSaveData.MinValue;

        int previousValue = dataManager.GetOSResourceValue(type);
        int nextValue = dataManager.ModifyOSResourceValue(type, delta);
        if (previousValue != nextValue)
        {
            GameEvents.RaiseResourceChanged(
                type,
                nextValue,
                OSResourceSaveData.MaxValue);
        }

        return nextValue;
    }

    void OnPhaseChanged(GamePhase phase)
    {
        bool visible = phase == GamePhase.OSMain;
        if (!visible)
        {
            ResetEmptyEnergyReminder();
            Chapter02Finale?.ResetPresentation();
            Chapter02PaperPush?.ResetPresentation();
            Chapter02FinalTalk?.ResetPresentation();
            linkMessageController?.NotifyOSHidden();
        }

        // Return-to-menu has already flushed the active session. Release the old
        // presentation now; Continue reconstructs its saved entrance in a new scene.
        if (phase == GamePhase.Boot)
        {
            SetChapter01LiveIntroProtected(false);
            characterWindowController?.CancelChapter01Reconnect();
            CancelFirstHeartHealEnding();
            StopStateCheckRestartDelay();
            osSfxAction?.SetCloseAndReOpenPartEnabled(false);
        }

        ApplyPresentation(visible);
        toolWindowController?.SetPresentationVisible(visible);
        SyncErrorWindowEffect();
        SyncFaultPhaseVolume(phase);
        if (!visible)
            return;

        RefreshLocalization();
        RefreshStateValues();
        linkMessageController?.NotifyOSBecameVisible();
    }

    void SyncFaultPhaseVolume(GamePhase phase)
    {
        // This event-specific volume lives in GameSetUp alongside the menu camera.
        // Preserve the ordinary OS post-processing and only remove the fault layer.
        bool faultVisible = phase == GamePhase.OSMain
            && GameDataManager.Instance != null
            && Mathf.RoundToInt(GameDataManager.Instance.CurrentStateCheckProgress) == 1;
        foreach (Volume volume in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            if (volume != null && volume.name == "OS Volume_ForStateIndex1")
                volume.weight = faultVisible ? 1f : 0f;
    }

    void OnResourceChanged(ResourceType type, float value, float max)
    {
        bool canAnimate = IsOSVisible
            && heartbeatPanel != null
            && heartbeatPanel.gameObject.activeInHierarchy;
        for (int i = 0; i < resourceViews.Length; i++)
        {
            OSResourceStateView view = resourceViews[i];
            if (view != null && view.ResourceType == type)
            {
                bool isHeartbeatView = heartbeatPanel != null
                    && view.transform.IsChildOf(heartbeatPanel);
                if (canAnimate
                    && isHeartbeatView
                    && view.isActiveAndEnabled)
                    view.EnqueueValue(value, max);
                else
                    view.RefreshValue(value, max);
            }
        }
    }

    void OnLanguageChanged(LanguageState language)
    {
        RefreshLocalization();
        characterWindowController?.RefreshWaitClickLocalization();
        memoryWindowController?.RefreshLocalizedData();
        recordWindowController?.RefreshLocalizedData();
        linkMessageController?.RefreshCurrentMessageLocalization();
    }



    void ApplyPresentation(bool visible)
    {
        if (!visible && IsOSVisible)
            CaptureWindowLayouts();

        if (!visible)
            SetMouseActiveWindow(null);

        IsOSVisible = visible;
        SetAdditionBackgroundSuppressed(visible);

        ApplyOSCameraPresentation(visible);

        if (osCanvas != null)
            osCanvas.enabled = visible;

        if (graphicRaycaster != null)
            graphicRaycaster.enabled = visible;

        if (osCanvasGroup != null)
        {
            osCanvasGroup.alpha = visible ? 1f : 0f;
            osCanvasGroup.interactable = visible;
            osCanvasGroup.blocksRaycasts = visible;
        }

        if (heartbeatWaveform != null)
        {
            heartbeatWaveform.SetBeatSfxEnabled(visible);
            heartbeatWaveform.enabled = visible;
        }

        AdditionHandle.Instance?.SetHeartbeatPanelSuppressed(visible);
    }

    void ApplyOSCameraPresentation(bool visible)
    {
        OSCameraHandle cameraHandle = BindOSCamera();
        Camera osCamera = cameraHandle != null
            ? cameraHandle.Camera
            : null;

        CursorHandle.Instance?.SetCameraPresentation(
            this,
            osCamera,
            visible && osCamera != null);
        cameraHandle?.SetPresentationActive(this, visible);
    }

    OSCameraHandle BindOSCamera()
    {
        if (osCanvas != null)
            osCanvas.renderMode = RenderMode.ScreenSpaceCamera;

        OSCameraHandle cameraHandle = OSCameraHandle.Instance;
        Camera osCamera = cameraHandle != null ? cameraHandle.Camera : null;
        if (cameraHandle == null || osCamera == null)
        {
            if (_hasStarted && !_hasWarnedMissingOSCamera)
            {
                _hasWarnedMissingOSCamera = true;
                Debug.LogWarning(
                    "[OS] OSCameraHandle or its Camera is missing; Main_OS_Canvs cannot use Screen Space - Camera.",
                    this);
            }

            return null;
        }

        _hasWarnedMissingOSCamera = false;
        if (osCanvas != null)
            osCanvas.worldCamera = osCamera;

        return cameraHandle;
    }

    void ResolveReferences()
    {
        if (osCanvas == null)
            osCanvas = GetComponent<Canvas>();

        if (osCanvasGroup == null)
            osCanvasGroup = GetComponent<CanvasGroup>();

        if (graphicRaycaster == null)
            graphicRaycaster = GetComponent<GraphicRaycaster>();

        if (heartbeatPanel == null)
            heartbeatPanel = transform.Find("HeartBeatPanel") as RectTransform;

        if (heartbeatWaveform == null)
        {
            Transform heartbeat = heartbeatPanel
                ?.Find("Mask/HeartBit");
            heartbeatWaveform = heartbeat?.GetComponent<HeartbeatWaveformUI>();
        }

        if (cablePool == null && heartbeatPanel != null)
        {
            cablePool = heartbeatPanel.GetComponentInChildren<
                OSUICablePoolController>(true);
        }

        Transform giftUnit = mainStoryGiftDesktopProcess != null
            ? mainStoryGiftDesktopProcess.transform
            : transform.Find("DesktopProcessUnit_Gift");
        if (giftUnit == null)
            giftUnit = FindOSEffectsRoot()?.Find("DesktopProcessUnit_Gift");
        if (mainStoryGiftDesktopProcess == null && giftUnit != null)
        {
            mainStoryGiftDesktopProcess =
                giftUnit.GetComponent<DesktopProcessUnit>();
        }

        if (mainStoryGift == null && giftUnit != null)
            mainStoryGift = giftUnit.Find("Gift") as RectTransform;

        if (mainStoryGiftFallTip == null && mainStoryGift != null)
        {
            RectTransform[] giftChildren =
                mainStoryGift.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < giftChildren.Length; i++)
            {
                RectTransform child = giftChildren[i];
                if (child != null && child.name == "ProcessFallTip")
                {
                    mainStoryGiftFallTip = child;
                    break;
                }
            }
        }

        if (mainStoryGiftLandingDust == null && giftUnit != null)
        {
            mainStoryGiftLandingDust = giftUnit
                .Find("GiftLandingDust")
                ?.GetComponent<OSGiftLandingDust>();
        }

        Transform giftWindowsRoot = transform.Find("Windows");
        if (mainStoryGiftWindow == null && giftWindowsRoot != null)
        {
            mainStoryGiftWindow = giftWindowsRoot
                .Find("WindowUnit_Gift")
                ?.GetComponent<OSWindowUnit>();
        }

        if (mainStoryGiftWindowController == null
            && mainStoryGiftWindow != null)
        {
            mainStoryGiftWindowController =
                mainStoryGiftWindow.GetComponent<OSGiftWindowController>();
        }

        CacheMainStoryGiftAuthoredState();

        localizedTexts = GetComponentsInChildren<OSLocalizedText>(true);
        resourceViews = GetComponentsInChildren<OSResourceStateView>(true);

        if (desktopProcessUnits == null)
            desktopProcessUnits = new List<DesktopProcessUnit>();

        desktopProcessUnits.RemoveAll(unit => unit == null);
        Transform desktopRoot = transform.Find("DesktopProcessGroup");
        if (desktopRoot != null)
        {
            if (desktopProcessGrid == null)
            {
                desktopProcessGrid =
                    desktopRoot.GetComponent<OSDesktopProcessGrid>();
            }

            if (desktopProcessDragProxy == null)
            {
                desktopProcessDragProxy = desktopRoot
                    .Find("DesktopProcessDragProxy")
                    ?.GetComponent<OSDesktopProcessDragProxy>();
            }

            DesktopProcessUnit[] resolvedUnits =
                desktopRoot.GetComponentsInChildren<DesktopProcessUnit>(true);
            for (int i = 0; i < resolvedUnits.Length; i++)
            {
                DesktopProcessUnit unit = resolvedUnits[i];
                if (!desktopProcessUnits.Contains(unit))
                    desktopProcessUnits.Add(unit);
            }

            if (memoryDesktopProcess == null)
            {
                Transform memoryDesktop =
                    desktopRoot.Find("DesktopProcessUnit_Memory");
                memoryDesktopProcess =
                    memoryDesktop?.GetComponent<DesktopProcessUnit>();
            }

            if (recordDesktopProcess == null)
            {
                Transform recordDesktop =
                    desktopRoot.Find("DesktopProcessUnit_Record");
                recordDesktopProcess =
                    recordDesktop?.GetComponent<DesktopProcessUnit>();
            }

            if (toolDesktopProcess == null)
            {
                Transform toolDesktop =
                    desktopRoot.Find("DesktopProcessUnit_Tool");
                toolDesktopProcess =
                    toolDesktop?.GetComponent<DesktopProcessUnit>();
            }

            if (gramophoneDesktopProcess == null)
                gramophoneDesktopProcess = desktopRoot.Find("DesktopProcessUnit_Gramophone")
                    ?.GetComponent<DesktopProcessUnit>();
            if (energyLoopDesktopProcess == null)
                energyLoopDesktopProcess = desktopRoot.Find("DesktopProcessUnit_EnergyLoop")
                    ?.GetComponent<DesktopProcessUnit>();
            if (paperPushDesktopProcess == null)
                paperPushDesktopProcess = desktopRoot.Find("DesktopProcessUnit_PaperPush")
                    ?.GetComponent<DesktopProcessUnit>();
            if (clawMachineDesktopProcess == null)
                clawMachineDesktopProcess = desktopRoot.Find("DesktopProcessUnit_ClawMachine")
                    ?.GetComponent<DesktopProcessUnit>();

            if (lockHeartDesktopProcess == null)
            {
                Transform lockHeartDesktop =
                    desktopRoot.Find("DesktopProcessUnit_LockHreat")
                    ?? desktopRoot.Find("DesktopProcessUnit_LockHeart");
                lockHeartDesktopProcess =
                    lockHeartDesktop?.GetComponent<DesktopProcessUnit>();
            }

            if (fileDesktopProcess == null)
            {
                Transform fileDesktop =
                    desktopRoot.Find("DesktopProcessUnit_File");
                fileDesktopProcess =
                    fileDesktop?.GetComponent<DesktopProcessUnit>();
            }

            if (stateCheckDesktopProcess == null)
            {
                Transform stateCheckDesktop =
                    desktopRoot.Find("DesktopProcessUnit_Fix");
                stateCheckDesktopProcess =
                    stateCheckDesktop?.GetComponent<DesktopProcessUnit>();
            }
        }

        if (windowUnits == null)
            windowUnits = new List<OSWindowUnit>();

        Transform windowsRoot = transform.Find("Windows");
        Transform sfxRoot = FindOSEffectsRoot();
        if (osSfxAction == null && sfxRoot != null)
            osSfxAction = sfxRoot.GetComponent<OSSFXAction>();
        if (closeAndReOpenPart == null && osSfxAction != null)
        {
            closeAndReOpenPart = osSfxAction
                .GetComponentInChildren<OSCloseAndReOpenPart>(true);
        }

        if (errorPoolRoot == null && sfxRoot != null)
            errorPoolRoot = sfxRoot.Find("ErrorPool") as RectTransform;
        if (errorPoolRoot == null && windowsRoot != null)
            errorPoolRoot = windowsRoot.Find("ErrorPool") as RectTransform;

        if (errorWindowPoolController == null && errorPoolRoot != null)
        {
            errorWindowPoolController =
                errorPoolRoot.GetComponent<OSErrorWindowPoolController>();
        }

        if (windowsRoot != null)
        {
            windowUnits.RemoveAll(window =>
                window == null || IsErrorPoolWindow(window) || IsMemoryPresentationWindow(window));

            OSWindowUnit[] resolvedWindows =
                windowsRoot.GetComponentsInChildren<OSWindowUnit>(true);
            for (int i = 0; i < resolvedWindows.Length; i++)
            {
                OSWindowUnit window = resolvedWindows[i];
                if (!IsErrorPoolWindow(window)
                    && !IsMemoryPresentationWindow(window)
                    && !windowUnits.Contains(window))
                {
                    windowUnits.Add(window);
                }
            }

            if (memoryWindow == null)
            {
                Transform memoryWindowTransform =
                    windowsRoot.Find("WindowUnit_Memory");
                memoryWindow =
                    memoryWindowTransform?.GetComponent<OSWindowUnit>();
            }

            if (recordWindow == null)
            {
                Transform recordWindowTransform =
                    windowsRoot.Find("WindowUnit_Record");
                recordWindow =
                    recordWindowTransform?.GetComponent<OSWindowUnit>();
            }

            if (toolWindow == null)
            {
                Transform toolWindowTransform =
                    windowsRoot.Find("WindowUnit_Tool");
                toolWindow =
                    toolWindowTransform?.GetComponent<OSWindowUnit>();
            }

            if (gramophoneWindow == null)
                gramophoneWindow = windowsRoot.Find("WindowUnit_Gramophone")
                    ?.GetComponent<OSWindowUnit>();
            if (energyLoopWindow == null)
                energyLoopWindow = windowsRoot.Find("WindowUnit_EnergyLoop")
                    ?.GetComponent<OSWindowUnit>();
            if (paperPushWindow == null)
                paperPushWindow = windowsRoot.Find("WindowUnit_PaperPush")
                    ?.GetComponent<OSWindowUnit>();

            if (folderWindow == null)
            {
                Transform folderWindowTransform =
                    windowsRoot.Find("WindowUnit_Folder");
                folderWindow =
                    folderWindowTransform?.GetComponent<OSWindowUnit>();
            }

            if (characterWindow == null)
            {
                Transform characterWindowTransform =
                    windowsRoot.Find("WindowUnit_Character");
                characterWindow =
                    characterWindowTransform?.GetComponent<OSWindowUnit>();
            }

            if (stateCheckWindow == null)
            {
                Transform stateCheckWindowTransform =
                    windowsRoot.Find("WindowUnit_StateCheck");
                stateCheckWindow =
                    stateCheckWindowTransform?.GetComponent<OSWindowUnit>();
            }
        }
        else
        {
            windowUnits.RemoveAll(window => window == null);
        }

        Transform folderGridRoot = folderWindow != null
            ? folderWindow.transform.Find("FolderGridArea")
            : null;
        if (folderProcessGrid == null && folderGridRoot != null)
        {
            folderProcessGrid =
                folderGridRoot.GetComponent<OSDesktopProcessGrid>();
        }

        if (folderGridRoot != null)
        {
            DesktopProcessUnit[] folderUnits = folderGridRoot
                .GetComponentsInChildren<DesktopProcessUnit>(true);
            for (int i = 0; i < folderUnits.Length; i++)
            {
                DesktopProcessUnit unit = folderUnits[i];
                if (!desktopProcessUnits.Contains(unit))
                    desktopProcessUnits.Add(unit);
            }
        }

        if (characterDesktopProcess == null && folderWindow != null)
        {
            Transform characterDesktop = folderGridRoot?.Find(
                    "DesktopProcessUnit_Character")
                ?? desktopRoot?.Find("DesktopProcessUnit_Character");
            characterDesktopProcess =
                characterDesktop?.GetComponent<DesktopProcessUnit>();
        }

        if (characterDesktopProcess != null
            && !desktopProcessUnits.Contains(characterDesktopProcess))
        {
            desktopProcessUnits.Add(characterDesktopProcess);
        }

        if (heartHealToolDesktopProcess == null && folderWindow != null)
        {
            Transform heartHealDesktop = folderGridRoot?.Find(
                    "DesktopProcessUnit_HeartHealTool")
                ?? desktopRoot?.Find("DesktopProcessUnit_HeartHealTool");
            heartHealToolDesktopProcess =
                heartHealDesktop?.GetComponent<DesktopProcessUnit>();
        }

        if (heartHealToolDesktopProcess != null
            && !desktopProcessUnits.Contains(heartHealToolDesktopProcess))
        {
            desktopProcessUnits.Add(heartHealToolDesktopProcess);
        }

        if (heartHealToolWindow == null && windowsRoot != null)
        {
            Transform heartHealWindowTransform =
                windowsRoot.Find("WindowUnit_HeartHealTool");
            heartHealToolWindow =
                heartHealWindowTransform?.GetComponent<OSWindowUnit>();
        }

        if (characterWindowController == null && characterWindow != null)
        {
            characterWindowController =
                characterWindow.GetComponent<OSCharacterWindowController>();
        }

        if (linkMessageController == null && characterWindow != null)
        {
            Transform linkMessageWindow = characterWindow.transform.Find(
                "WindowUnit_LinkMessage");
            linkMessageController = linkMessageWindow
                ?.GetComponent<OSLinkMessageController>();
        }

        if (memoryWindowController == null && memoryWindow != null)
        {
            memoryWindowController =
                memoryWindow.GetComponent<OSMemoryWindowController>();
        }

        if (recordWindowController == null && recordWindow != null)
        {
            recordWindowController =
                recordWindow.GetComponent<OSRecordWindowController>();
        }

        if (toolWindowController == null && toolWindow != null)
        {
            toolWindowController =
                toolWindow.GetComponent<OSToolWindowController>();
        }

        if (heartHealToolWindowController == null
            && heartHealToolWindow != null)
        {
            heartHealToolWindowController = heartHealToolWindow
                .GetComponent<OSHeartHealToolWindowController>();
        }

        if (stateCheckWindowController == null && stateCheckWindow != null)
        {
            stateCheckWindowController = stateCheckWindow
                .GetComponent<OSStateCheckWindowController>();
        }

        ResolveChapter01KeyReferences();
        ResolveClawMachineReferences();
        ResolveCursorExperimentReferences();
        ResolveChapter02FinaleReferences();
        ResolveChapter04TVReferences();
        ResolveChapter04MotherHeartReferences();
        if (clawMachineDesktopProcess != null && !desktopProcessUnits.Contains(clawMachineDesktopProcess))
            desktopProcessUnits.Add(clawMachineDesktopProcess);

        if (processUnits == null)
            processUnits = new List<ProcessUnit>();

        processUnits.RemoveAll(unit => unit == null);
        Transform groupPool = transform.Find("ProcessGroup/GroupPool");
        if (groupPool != null)
        {
            ProcessUnit[] resolvedProcessUnits =
                groupPool.GetComponentsInChildren<ProcessUnit>(true);
            for (int i = 0; i < resolvedProcessUnits.Length; i++)
            {
                ProcessUnit unit = resolvedProcessUnits[i];
                if (!processUnits.Contains(unit))
                    processUnits.Add(unit);
            }
        }
    }

    bool ResetMainStoryGiftToWaitingState()
    {
        CacheMainStoryGiftAuthoredState();
        if (!HasMainStoryGiftReferences())
            return false;

        StopMainStoryGiftSequence(false);
        mainStoryGift.DOKill(false);
        mainStoryGiftFallTip.DOKill(false);
        mainStoryGiftLandingDust?.ResetVisual();
        IsMainStoryGiftLanded = false;
        SetMainStoryGiftInteraction(false);

        Vector2 giftPosition = _mainStoryGiftRestPosition;
        giftPosition.y = mainStoryGiftStartY;
        mainStoryGift.anchoredPosition = giftPosition;
        mainStoryGift.localEulerAngles = _mainStoryGiftRestEulerAngles;

        Vector2 fallTipPosition = mainStoryGiftFallTip.anchoredPosition;
        fallTipPosition.x = _mainStoryGiftFallTipRestX;
        fallTipPosition.y = mainStoryGiftFallTipStartY;
        mainStoryGiftFallTip.anchoredPosition = fallTipPosition;
        mainStoryGift.gameObject.SetActive(false);
        _hasWarnedMissingMainStoryGift = false;
        return true;
    }

    void PlayMainStoryGiftIntro()
    {
        if (!HasMainStoryGiftReferences())
        {
            WarnMissingMainStoryGiftReferences();
            return;
        }

        StopMainStoryGiftSequence(false);
        IsMainStoryGiftLanded = false;
        SetMainStoryGiftInteraction(false);
        mainStoryGift.gameObject.SetActive(true);

        Vector2 giftPosition = _mainStoryGiftRestPosition;
        giftPosition.y = mainStoryGiftStartY;
        mainStoryGift.anchoredPosition = giftPosition;

        float dropDuration = Mathf.Max(0.05f, mainStoryGiftDropDuration);
        float swayDuration = dropDuration / 5f;
        float swayDistance = Mathf.Max(0f, mainStoryGiftSwayDistance);
        float swayAngle = Mathf.Max(0f, mainStoryGiftSwayAngle);
        Vector3 initialRotation = _mainStoryGiftRestEulerAngles;
        initialRotation.z -= swayAngle * 0.7f;
        mainStoryGift.localEulerAngles = initialRotation;

        Sequence sway = DOTween.Sequence();
        sway.Append(mainStoryGift
            .DOAnchorPosX(
                _mainStoryGiftRestPosition.x + swayDistance,
                swayDuration,
                true)
            .SetEase(Ease.InOutSine));
        sway.Append(mainStoryGift
            .DOAnchorPosX(
                _mainStoryGiftRestPosition.x - swayDistance * 0.8f,
                swayDuration,
                true)
            .SetEase(Ease.InOutSine));
        sway.Append(mainStoryGift
            .DOAnchorPosX(
                _mainStoryGiftRestPosition.x + swayDistance * 0.55f,
                swayDuration,
                true)
            .SetEase(Ease.InOutSine));
        sway.Append(mainStoryGift
            .DOAnchorPosX(
                _mainStoryGiftRestPosition.x - swayDistance * 0.25f,
                swayDuration,
                true)
            .SetEase(Ease.InOutSine));
        sway.Append(mainStoryGift
            .DOAnchorPosX(
                _mainStoryGiftRestPosition.x,
                swayDuration,
                true)
            .SetEase(Ease.InOutSine));

        Sequence tilt = DOTween.Sequence();
        tilt.Append(CreateMainStoryGiftTiltTween(
            swayAngle,
            swayDuration));
        tilt.Append(CreateMainStoryGiftTiltTween(
            -swayAngle * 0.75f,
            swayDuration));
        tilt.Append(CreateMainStoryGiftTiltTween(
            swayAngle * 0.5f,
            swayDuration));
        tilt.Append(CreateMainStoryGiftTiltTween(
            -swayAngle * 0.25f,
            swayDuration));
        tilt.Append(CreateMainStoryGiftTiltTween(0f, swayDuration));

        Sequence sequence = DOTween.Sequence();
        _mainStoryGiftSequence = sequence;
        _hasMainStoryStarted = true;
        Tween dropTween = mainStoryGift
            .DOAnchorPosY(
                _mainStoryGiftRestPosition.y,
                dropDuration,
                true)
            .SetEase(Ease.InSine)
            .OnComplete(HandleMainStoryGiftLanded);
        sequence.Append(dropTween);
        sequence.Join(sway);
        sequence.Join(tilt);
        sequence.Append(mainStoryGiftFallTip
            .DOAnchorPosY(
                mainStoryGiftFallTipEndY,
                Mathf.Max(0.05f, mainStoryGiftFallTipDuration),
                true)
            .SetEase(Ease.InSine));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_mainStoryGiftSequence != sequence)
                    return;

                _mainStoryGiftSequence = null;
                ApplyMainStoryGiftFinalState();
            })
            .OnKill(() =>
            {
                if (_mainStoryGiftSequence == sequence)
                    _mainStoryGiftSequence = null;
            });
    }

    Tween CreateMainStoryGiftTiltTween(float angle, float duration)
    {
        Vector3 targetRotation = _mainStoryGiftRestEulerAngles;
        targetRotation.z += angle;
        return mainStoryGift
            .DOLocalRotate(targetRotation, duration, RotateMode.Fast)
            .SetEase(Ease.InOutSine);
    }

    void CacheMainStoryGiftAuthoredState()
    {
        if (!_mainStoryGiftPoseCached
            && mainStoryGift != null
            && mainStoryGiftFallTip != null)
        {
            _mainStoryGiftRestPosition = mainStoryGift.anchoredPosition;
            _mainStoryGiftRestEulerAngles = mainStoryGift.localEulerAngles;
            _mainStoryGiftFallTipRestX =
                mainStoryGiftFallTip.anchoredPosition.x;
            _mainStoryGiftPoseCached = true;
        }

        if (_mainStoryGiftInteractionCached
            || mainStoryGiftDesktopProcess == null)
        {
            return;
        }

        Image hitArea = ResolveMainStoryGiftHitArea();
        _mainStoryGiftDesktopProcessWasEnabled =
            mainStoryGiftDesktopProcess.enabled;
        _mainStoryGiftRaycastWasEnabled =
            hitArea != null && hitArea.raycastTarget;
        _mainStoryGiftInteractionCached = true;
    }

    bool HasMainStoryGiftReferences()
    {
        return mainStoryGiftDesktopProcess != null
            && mainStoryGift != null
            && mainStoryGiftFallTip != null
            && _mainStoryGiftPoseCached;
    }

    void SetMainStoryGiftInteraction(bool enabled)
    {
        if (mainStoryGiftDesktopProcess == null)
            return;

        bool shouldEnable = enabled && !_hasMainStoryGiftWindowClosed;
        Image hitArea = ResolveMainStoryGiftHitArea();
        if (shouldEnable)
        {
            mainStoryGiftDesktopProcess.enabled =
                !_mainStoryGiftInteractionCached
                || _mainStoryGiftDesktopProcessWasEnabled;
            if (hitArea != null)
            {
                hitArea.raycastTarget = !_mainStoryGiftInteractionCached
                    || _mainStoryGiftRaycastWasEnabled;
            }

            return;
        }

        mainStoryGiftDesktopProcess.enabled = false;
        if (hitArea != null)
            hitArea.raycastTarget = false;
    }

    Image ResolveMainStoryGiftHitArea()
    {
        if (mainStoryGiftDesktopProcess == null)
            return null;

        Image hitArea = mainStoryGiftDesktopProcess.HoverTip;
        return hitArea != null
            ? hitArea
            : mainStoryGiftDesktopProcess.GetComponent<Image>();
    }

    void ApplyMainStoryGiftFinalState()
    {
        if (mainStoryGift != null)
        {
            mainStoryGift.gameObject.SetActive(true);
            mainStoryGift.anchoredPosition = _mainStoryGiftRestPosition;
            mainStoryGift.localEulerAngles = _mainStoryGiftRestEulerAngles;
        }

        if (mainStoryGiftFallTip != null)
        {
            Vector2 fallTipPosition = mainStoryGiftFallTip.anchoredPosition;
            fallTipPosition.x = _mainStoryGiftFallTipRestX;
            fallTipPosition.y = mainStoryGiftFallTipEndY;
            mainStoryGiftFallTip.anchoredPosition = fallTipPosition;
        }

        IsMainStoryGiftLanded = true;
        SetMainStoryGiftInteraction(true);
    }

    void HandleMainStoryGiftLanded()
    {
        IsMainStoryGiftLanded = true;
        SetMainStoryGiftInteraction(true);
        mainStoryGiftLandingDust?.Play();
    }

    void StopMainStoryGiftSequence(bool complete)
    {
        if (_mainStoryGiftSequence != null)
        {
            Sequence sequence = _mainStoryGiftSequence;
            _mainStoryGiftSequence = null;
            sequence.Kill(false);
        }

        if (complete)
            ApplyMainStoryGiftFinalState();
    }

    void WarnMissingMainStoryGiftReferences()
    {
        if (_hasWarnedMissingMainStoryGift)
            return;

        _hasWarnedMissingMainStoryGift = true;
        Debug.LogWarning(
            "[OS] Main story Gift references are incomplete; intro was skipped.",
            this);
    }

    bool IsErrorPoolWindow(OSWindowUnit window)
    {
        return window != null
            && errorPoolRoot != null
            && window.transform.IsChildOf(errorPoolRoot);
    }

    static bool IsMemoryPresentationWindow(OSWindowUnit window)
    {
        return window != null && (window.GetComponent<OSMemorySubwindow>() != null
            || window.GetComponentInParent<OSMemorySubwindowPreview>(true) != null
            || (window.GetComponent<OSMemoryWindowController>() == null
                && window.GetComponentInParent<OSMemoryWindowController>(true) != null));
    }

    public void InitializeDesktopProcessUnit(DesktopProcessUnit unit)
    {
        if (unit == null)
            return;

        if (unit == mainStoryGiftDesktopProcess)
        {
            bool giftIsNew = unit.IsStateInitialized
                ? unit.HasNewContent
                : true;
            unit.InitializeState(
                OSDesktopProcessKeys.Gift,
                false,
                giftIsNew,
                false);
            return;
        }

        if (!TryGetPersistentDesktopProcessKey(unit, out string key))
        {
            unit.InitializeState(
                unit == null ? string.Empty : unit.name,
                false,
                false,
                false);
            return;
        }

        if (unit == chapter01KeyDesktopProcess)
        {
            unit.InitializeState(
                OSDesktopProcessKeys.KeyX,
                true,
                false,
                false);
            if (_desktopProcessLayoutInitialized
                && GameDataManager.Instance?.IsChapter01KeyXAcquired == true)
            {
                EnsureDesktopProcessLayout(unit);
            }
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        bool initialIsNew = !string.Equals(
            key,
            OSDesktopProcessKeys.StateCheck,
            StringComparison.Ordinal);
        bool initialIsLocked = IsDesktopProcessInitiallyLocked(key);
        bool isNew = initialIsNew;
        bool isLocked = initialIsLocked;
        if (dataManager != null)
        {
            dataManager.GetOrInitializeDesktopProcessState(
                key,
                initialIsNew,
                initialIsLocked,
                out isNew,
                out isLocked);
        }

        unit.InitializeState(key, true, isNew, isLocked);
        if (unit == toolDesktopProcess)
            unit.gameObject.SetActive(dataManager != null && dataManager.IsToolUnlocked);
        else if (unit == gramophoneDesktopProcess)
            unit.gameObject.SetActive(dataManager != null && dataManager.IsGramophoneInstalled);
        else if (unit == energyLoopDesktopProcess)
            unit.gameObject.SetActive(dataManager != null && dataManager.IsEnergyLoopInstalled);
        else if (unit == tvDesktopProcess)
            unit.gameObject.SetActive(dataManager != null && dataManager.IsTVInstalled);
        else if (unit == paperPushDesktopProcess)
            unit.gameObject.SetActive(dataManager != null && dataManager.IsPaperPushUnlocked);
        else if (unit == clawMachineDesktopProcess)
            unit.gameObject.SetActive(IsChapter02ClawMachineDiscovered());
        else if (unit == memoryDesktopProcess)
            RefreshMemoryInstallationIcon();
        if (_desktopProcessLayoutInitialized)
            EnsureDesktopProcessLayout(unit);
    }

    public bool SetDesktopProcessNew(
        string processKey,
        bool isNew = true)
    {
        ResolveReferences();
        if (string.IsNullOrWhiteSpace(processKey))
            return false;

        string key = processKey.Trim();
        DesktopProcessUnit unit = FindDesktopProcessUnit(key);
        if (unit == mainStoryGiftDesktopProcess
            || string.Equals(
                key,
                OSDesktopProcessKeys.Gift,
                StringComparison.Ordinal))
        {
            mainStoryGiftDesktopProcess?.SetNewState(isNew, !isNew);
            return mainStoryGiftDesktopProcess != null;
        }

        if (!IsPersistentDesktopProcessKey(key))
            return false;

        if (unit != null && !unit.UsesPersistentNewState)
            return false;

        BindDesktopProcessNewStateEvents();
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return false;

        bool changed = dataManager.SetDesktopProcessIsNew(key, isNew);
        if (!changed && unit != null)
            unit.SetNewState(isNew, !isNew);
        return changed || unit != null;
    }

    public bool SetDesktopProcessNew(
        DesktopProcessUnit unit,
        bool isNew = true)
    {
        if (unit == null)
            return false;

        InitializeDesktopProcessUnit(unit);
        if (!unit.UsesPersistentNewState
            && unit != mainStoryGiftDesktopProcess)
        {
            return false;
        }

        return SetDesktopProcessNew(unit.ProcessKey, isNew);
    }

    public bool SetDesktopProcessLocked(
        string processKey,
        bool isLocked = true)
    {
        ResolveReferences();
        if (string.IsNullOrWhiteSpace(processKey))
            return false;

        string key = processKey.Trim();
        DesktopProcessUnit unit = FindDesktopProcessUnit(key);
        if (!IsPersistentDesktopProcessKey(key)
            || (unit != null && !unit.UsesPersistentState))
        {
            return false;
        }

        BindDesktopProcessNewStateEvents();
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return false;

        bool changed = dataManager.SetDesktopProcessIsLocked(key, isLocked);
        if (!changed && unit != null)
            unit.SetLockedState(isLocked);
        return changed || unit != null;
    }

    public bool SetDesktopProcessLocked(
        DesktopProcessUnit unit,
        bool isLocked = true)
    {
        if (unit == null)
            return false;

        InitializeDesktopProcessUnit(unit);
        if (!unit.UsesPersistentState)
            return false;

        return SetDesktopProcessLocked(unit.ProcessKey, isLocked);
    }

    public void MarkRecordDesktopProcessNew()
    {
        SetDesktopProcessNew(OSDesktopProcessKeys.Record, true);
    }

    public void MarkStateCheckDesktopProcessNew()
    {
        SetDesktopProcessNew(OSDesktopProcessKeys.StateCheck, true);
    }

    void InitializeDesktopProcessNewStates()
    {
        ResolveReferences();
        for (int i = 0; i < desktopProcessUnits.Count; i++)
            InitializeDesktopProcessUnit(desktopProcessUnits[i]);

        if (mainStoryGiftDesktopProcess != null
            && !desktopProcessUnits.Contains(mainStoryGiftDesktopProcess))
        {
            InitializeDesktopProcessUnit(mainStoryGiftDesktopProcess);
        }
    }

    void InitializeDesktopProcessLayouts()
    {
        ResolveReferences();
        if (desktopProcessGrid == null || folderProcessGrid == null)
        {
            _desktopProcessLayoutInitialized = false;
            return;
        }

        if (_desktopProcessLayoutInitialized)
        {
            desktopProcessGrid.NormalizeSlotsToCapacity();
            folderProcessGrid.NormalizeSlotsToCapacity();
            desktopProcessGrid.ApplyLayout();
            folderProcessGrid.ApplyLayout();
            desktopProcessDragProxy?.transform.SetAsLastSibling();
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        desktopProcessGrid.ClearRuntimeSlots();
        folderProcessGrid.ClearRuntimeSlots();
        _desktopLayoutUnits.Clear();

        var defaultContainers =
            new Dictionary<DesktopProcessUnit, string>();
        var defaultIndices = new Dictionary<DesktopProcessUnit, int>();
        int nextDesktopIndex = CollectDesktopProcessLayoutDefaults(
            desktopProcessGrid,
            OSDesktopProcessContainerIds.Desktop,
            0,
            defaultContainers,
            defaultIndices);
        int nextFolderIndex = CollectDesktopProcessLayoutDefaults(
            folderProcessGrid,
            OSDesktopProcessContainerIds.Folder,
            0,
            defaultContainers,
            defaultIndices);

        for (int i = 0; i < desktopProcessUnits.Count; i++)
        {
            DesktopProcessUnit unit = desktopProcessUnits[i];
            if (unit == null || unit == mainStoryGiftDesktopProcess
                || IsUninstalledDesktopProcess(unit))
                continue;

            if (!_desktopLayoutUnits.Contains(unit))
                _desktopLayoutUnits.Add(unit);
            if (defaultContainers.ContainsKey(unit))
                continue;

            bool authoredInFolder = unit.transform.IsChildOf(
                folderProcessGrid.transform);
            string containerId = authoredInFolder
                ? OSDesktopProcessContainerIds.Folder
                : OSDesktopProcessContainerIds.Desktop;
            int gridIndex = authoredInFolder
                ? nextFolderIndex++
                : nextDesktopIndex++;
            defaultContainers.Add(unit, containerId);
            defaultIndices.Add(unit, gridIndex);
        }

        for (int i = 0; i < _desktopLayoutUnits.Count; i++)
        {
            DesktopProcessUnit unit = _desktopLayoutUnits[i];
            if (!TryGetPersistentDesktopProcessKey(unit, out string key))
                continue;

            bool hasSavedLayout = dataManager.TryGetDesktopProcessLayout(
                key,
                out OSDesktopProcessLayoutSaveData savedLayout);
            string containerId = hasSavedLayout
                ? OSDesktopProcessContainerIds.Normalize(
                    savedLayout.containerId)
                : null;
            if (containerId == null)
            {
                containerId = defaultContainers[unit];
                hasSavedLayout = false;
            }

            if (unit == fileDesktopProcess
                && containerId == OSDesktopProcessContainerIds.Folder)
            {
                containerId = OSDesktopProcessContainerIds.Desktop;
                hasSavedLayout = false;
            }

            OSDesktopProcessGrid targetGrid =
                GetDesktopProcessGrid(containerId);
            int requestedIndex = hasSavedLayout
                ? savedLayout.gridIndex
                : defaultIndices[unit];
            if (targetGrid == null
                || !targetGrid.TryPlace(
                    unit,
                    requestedIndex,
                    out _))
            {
                Debug.LogWarning(
                    $"[OS] No grid slot is available for desktop process '{key}'.",
                    this);
            }
        }

        desktopProcessGrid.ApplyLayout();
        folderProcessGrid.ApplyLayout();
        desktopProcessDragProxy?.transform.SetAsLastSibling();
        _desktopProcessLayoutInitialized = true;
        CaptureDesktopProcessLayouts();
    }

    int CollectDesktopProcessLayoutDefaults(
        OSDesktopProcessGrid grid,
        string containerId,
        int nextIndex,
        Dictionary<DesktopProcessUnit, string> defaultContainers,
        Dictionary<DesktopProcessUnit, int> defaultIndices)
    {
        if (grid == null)
            return nextIndex;

        for (int i = 0; i < grid.transform.childCount; i++)
        {
            DesktopProcessUnit unit = grid.transform
                .GetChild(i)
                .GetComponent<DesktopProcessUnit>();
            if (unit == null
                || unit == mainStoryGiftDesktopProcess
                || IsUninstalledDesktopProcess(unit)
                || defaultContainers.ContainsKey(unit))
            {
                continue;
            }

            _desktopLayoutUnits.Add(unit);
            defaultContainers.Add(unit, containerId);
            defaultIndices.Add(unit, nextIndex++);
        }

        return nextIndex;
    }

    void EnsureDesktopProcessLayout(DesktopProcessUnit unit)
    {
        if (unit == null
            || unit == mainStoryGiftDesktopProcess
            || IsUninstalledDesktopProcess(unit)
            || desktopProcessGrid == null
            || folderProcessGrid == null
            || desktopProcessGrid.Contains(unit)
            || folderProcessGrid.Contains(unit)
            || !TryGetPersistentDesktopProcessKey(unit, out string key))
        {
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        OSDesktopProcessLayoutSaveData savedLayout = null;
        bool hasSavedLayout = dataManager != null
            && dataManager.TryGetDesktopProcessLayout(
                key,
                out savedLayout);
        string containerId = hasSavedLayout
            ? OSDesktopProcessContainerIds.Normalize(savedLayout.containerId)
            : null;
        if (containerId == null)
        {
            containerId = unit.transform.IsChildOf(folderProcessGrid.transform)
                ? OSDesktopProcessContainerIds.Folder
                : OSDesktopProcessContainerIds.Desktop;
            hasSavedLayout = false;
        }

        if (unit == fileDesktopProcess
            && containerId == OSDesktopProcessContainerIds.Folder)
        {
            containerId = OSDesktopProcessContainerIds.Desktop;
            hasSavedLayout = false;
        }

        OSDesktopProcessGrid targetGrid = GetDesktopProcessGrid(containerId);
        if (targetGrid == null)
            return;

        int requestedIndex = hasSavedLayout
            ? savedLayout.gridIndex
            : targetGrid.FindFirstAvailableSlot();
        if (!targetGrid.TryPlace(unit, requestedIndex, out _))
            return;

        if (!_desktopLayoutUnits.Contains(unit))
            _desktopLayoutUnits.Add(unit);
        targetGrid.ApplyLayout();
        desktopProcessDragProxy?.transform.SetAsLastSibling();
        CaptureDesktopProcessLayout(unit, dataManager);
    }

    public void CaptureDesktopProcessLayouts()
    {
        if (!_desktopProcessLayoutInitialized)
            return;

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        for (int i = 0; i < _desktopLayoutUnits.Count; i++)
            CaptureDesktopProcessLayout(_desktopLayoutUnits[i], dataManager);
    }

    void CaptureDesktopProcessLayout(
        DesktopProcessUnit unit,
        GameDataManager dataManager)
    {
        if (unit == null
            || dataManager == null
            || (unit == clawMachineDesktopProcess && !IsChapter02ClawMachineOnDesktop())
            || !TryGetPersistentDesktopProcessKey(unit, out string key))
        {
            return;
        }

        OSDesktopProcessGrid grid = GetDesktopProcessGridContaining(unit);
        if (grid == null || !grid.TryGetSlot(unit, out int gridIndex))
            return;

        dataManager.SetDesktopProcessLayout(
            key,
            grid.ContainerId,
            gridIndex);
    }

    public bool TryBeginDesktopProcessDrag(
        DesktopProcessUnit unit,
        PointerEventData eventData)
    {
        if (unit == null || eventData == null || !unit.IsInteractable)
            return false;

        if (!unit.IsStateInitialized)
            InitializeDesktopProcessUnit(unit);
        if (!_desktopProcessLayoutInitialized)
            InitializeDesktopProcessLayouts();
        if (!unit.UsesPersistentState
            || unit == mainStoryGiftDesktopProcess
            || !_desktopProcessLayoutInitialized)
        {
            return false;
        }

        OSDesktopProcessGrid sourceGrid =
            GetDesktopProcessGridContaining(unit);
        if (sourceGrid == null
            || !sourceGrid.TryGetSlot(unit, out int sourceIndex))
        {
            return false;
        }

        CancelDesktopProcessDrag();
        ResolveReferences();
        if (desktopProcessDragProxy == null
            || !desktopProcessDragProxy.Show(unit, eventData))
        {
            Debug.LogWarning(
                "[OS] DesktopProcessDragProxy is missing or could not be shown.",
                this);
            return false;
        }

        _draggedDesktopProcess = unit;
        _dragSourceGrid = sourceGrid;
        _dragSourceIndex = sourceIndex;
        unit.SetDragGhosted(true, draggedProcessSourceAlpha);
        return true;
    }

    public void UpdateDesktopProcessDrag(
        DesktopProcessUnit unit,
        PointerEventData eventData)
    {
        if (unit != null && unit == _draggedDesktopProcess)
            desktopProcessDragProxy?.Move(eventData);
    }

    public void EndDesktopProcessDrag(
        DesktopProcessUnit unit,
        PointerEventData eventData)
    {
        if (unit == null || unit != _draggedDesktopProcess)
            return;

        OSDesktopProcessGrid sourceGrid = _dragSourceGrid;
        int sourceIndex = _dragSourceIndex;
        unit.SetDragGhosted(false);
        desktopProcessDragProxy?.Hide();
        _draggedDesktopProcess = null;
        _dragSourceGrid = null;
        _dragSourceIndex = -1;

        if (eventData == null || sourceGrid == null)
            return;

        if (TryHandleChapter01KeyLockDrop(
                unit,
                sourceGrid,
                sourceIndex,
                eventData))
        {
            return;
        }

        if (!TryResolveDesktopProcessDrop(
                unit,
                sourceGrid,
                sourceIndex,
                eventData,
                out OSDesktopProcessGrid targetGrid,
                out int targetIndex))
        {
            return;
        }

        bool moved = MoveDesktopProcessToGrid(
            unit,
            sourceGrid,
            sourceIndex,
            targetGrid,
            targetIndex);
        if (moved
            && unit == chapter01KeyDesktopProcess
            && sourceGrid == chapter01KeySourceGrid)
        {
            CompleteChapter01KeyAcquisition();
        }
    }

    public void CancelDesktopProcessDrag(DesktopProcessUnit unit = null)
    {
        if (_draggedDesktopProcess == null
            || (unit != null && unit != _draggedDesktopProcess))
        {
            return;
        }

        DesktopProcessUnit draggedUnit = _draggedDesktopProcess;
        _draggedDesktopProcess = null;
        _dragSourceGrid = null;
        _dragSourceIndex = -1;
        desktopProcessDragProxy?.Hide();
        draggedUnit?.SetDragGhosted(false);
    }

    bool TryResolveDesktopProcessDrop(
        DesktopProcessUnit unit,
        OSDesktopProcessGrid sourceGrid,
        int sourceIndex,
        PointerEventData eventData,
        out OSDesktopProcessGrid targetGrid,
        out int targetIndex)
    {
        targetGrid = null;
        targetIndex = -1;
        Camera eventCamera = eventData.enterEventCamera != null
            ? eventData.enterEventCamera
            : eventData.pressEventCamera;
        GameObject raycastObject =
            eventData.pointerCurrentRaycast.gameObject;
        OSWindowUnit pointerWindow = raycastObject != null
            ? raycastObject.GetComponentInParent<OSWindowUnit>()
            : null;
        bool isUnacquiredChapter01Key =
            unit == chapter01KeyDesktopProcess
            && sourceGrid == chapter01KeySourceGrid;

        bool folderIsDropTarget = folderWindow != null
            && folderWindow.IsOpen
            && !folderWindow.IsMinimized
            && folderProcessGrid != null
            && folderProcessGrid.gameObject.activeInHierarchy;
        if (folderIsDropTarget
            && !isUnacquiredChapter01Key
            && (pointerWindow == null || pointerWindow == folderWindow)
            && folderProcessGrid.TryGetGridIndex(
                eventData.position,
                eventCamera,
                out targetIndex))
        {
            if (unit == fileDesktopProcess)
                return false;

            targetGrid = folderProcessGrid;
            return true;
        }

        if (pointerWindow != null)
            return false;

        RectTransform fileRect = fileDesktopProcess != null
            ? fileDesktopProcess.transform as RectTransform
            : null;
        bool isOverFileProcess = unit != fileDesktopProcess
            && fileDesktopProcess != null
            && fileDesktopProcess.gameObject.activeInHierarchy
            && fileRect != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                fileRect,
                eventData.position,
                eventCamera);
        if (isOverFileProcess)
        {
            if (isUnacquiredChapter01Key)
                return false;

            targetGrid = folderProcessGrid;
            if (sourceGrid == folderProcessGrid)
            {
                targetIndex = sourceIndex;
                return true;
            }

            targetIndex = folderProcessGrid.FindFirstAvailableSlot();
            return targetIndex >= 0;
        }

        if (desktopProcessGrid != null
            && desktopProcessGrid.TryGetGridIndex(
                eventData.position,
                eventCamera,
                out targetIndex))
        {
            targetGrid = desktopProcessGrid;
            return true;
        }

        return false;
    }

    bool MoveDesktopProcessToGrid(
        DesktopProcessUnit unit,
        OSDesktopProcessGrid sourceGrid,
        int sourceIndex,
        OSDesktopProcessGrid targetGrid,
        int targetIndex)
    {
        if (unit == null
            || sourceGrid == null
            || targetGrid == null
            || (unit == fileDesktopProcess
                && targetGrid == folderProcessGrid))
        {
            return false;
        }

        if (sourceGrid == targetGrid && sourceIndex == targetIndex)
            return true;

        sourceGrid.Remove(unit);
        if (!targetGrid.TryInsert(unit, targetIndex))
        {
            sourceGrid.TryPlace(unit, sourceIndex, out _);
            sourceGrid.ApplyLayout();
            return false;
        }

        sourceGrid.ApplyLayout();
        if (targetGrid != sourceGrid)
            targetGrid.ApplyLayout();

        if (unit == clawMachineDesktopProcess
            && (targetGrid == desktopProcessGrid || targetGrid == folderProcessGrid))
        {
            GameDataManager.Instance.OSProcessData.memory.chapter02ClawMachineOnDesktop = true;
            if (!_desktopLayoutUnits.Contains(unit)) _desktopLayoutUnits.Add(unit);
            OnChapter02ClawMachineMovedToDesktop();
        }
        desktopProcessDragProxy?.transform.SetAsLastSibling();
        CaptureDesktopProcessLayouts();
        return true;
    }

    bool TryHandleChapter01KeyLockDrop(
        DesktopProcessUnit unit,
        OSDesktopProcessGrid sourceGrid,
        int sourceIndex,
        PointerEventData eventData)
    {
        if (unit != chapter01KeyDesktopProcess || eventData == null)
            return false;

        GameObject raycastObject =
            eventData.pointerCurrentRaycast.gameObject;
        DesktopProcessUnit lockTarget = raycastObject != null
            ? raycastObject.GetComponentInParent<DesktopProcessUnit>()
            : null;
        bool isSupportedLock = lockTarget == memoryDesktopProcess
            || lockTarget == recordDesktopProcess;
        if (!isSupportedLock || !lockTarget.IsLocked)
            return false;

        Camera eventCamera = eventData.enterEventCamera != null
            ? eventData.enterEventCamera
            : eventData.pressEventCamera;
        RectTransform targetRect = lockTarget.transform as RectTransform;
        Vector2 targetScreenPosition = targetRect != null
            ? GetRectScreenCenter(targetRect)
            : eventData.position;
        if (desktopProcessGrid == null
            || !desktopProcessGrid.TryFindNearestAvailableSlot(
                targetScreenPosition,
                eventCamera,
                chapter01KeyDesktopProcess,
                out int targetIndex))
        {
            Debug.LogWarning(
                "[OS] No desktop grid slot is available beside the "
                + "Chapter01 key target.",
                this);
            return true;
        }

        if (!MoveDesktopProcessToGrid(
                unit,
                sourceGrid,
                sourceIndex,
                desktopProcessGrid,
                targetIndex))
        {
            return true;
        }

        CompleteChapter01KeyAcquisition();
        PlayChapter01KeyUnlockAnimation(lockTarget);
        return true;
    }

    void CompleteChapter01KeyAcquisition()
    {
        StopChapter01KeyWindowOpenDelay();
        GameDataManager.Instance?.SetChapter01KeyXAcquired();
        if (chapter01KeyDesktopProcess != null
            && !_desktopLayoutUnits.Contains(chapter01KeyDesktopProcess))
        {
            _desktopLayoutUnits.Add(chapter01KeyDesktopProcess);
        }

        CaptureDesktopProcessLayouts();
        if (chapter01KeyWindow != null)
        {
            if (chapter01KeyWindow.IsOpen)
                chapter01KeyWindow.Close();
            else
                chapter01KeyWindow.PrepareRuntimeClosed();
        }
    }

    OSDesktopProcessGrid GetDesktopProcessGrid(string containerId)
    {
        if (string.Equals(
            containerId,
            OSDesktopProcessContainerIds.Desktop,
            StringComparison.Ordinal))
        {
            return desktopProcessGrid;
        }

        if (string.Equals(
            containerId,
            OSDesktopProcessContainerIds.Folder,
            StringComparison.Ordinal))
        {
            return folderProcessGrid;
        }

        return null;
    }

    OSDesktopProcessGrid GetDesktopProcessGridContaining(
        DesktopProcessUnit unit)
    {
        if (desktopProcessGrid != null && desktopProcessGrid.Contains(unit))
            return desktopProcessGrid;
        if (folderProcessGrid != null && folderProcessGrid.Contains(unit))
            return folderProcessGrid;
        if (chapter01KeySourceGrid != null
            && chapter01KeySourceGrid.Contains(unit))
        {
            return chapter01KeySourceGrid;
        }
        if (clawMachineSourceGrid != null && clawMachineSourceGrid.Contains(unit))
            return clawMachineSourceGrid;
        return null;
    }

    bool TryGetPersistentDesktopProcessKey(
        DesktopProcessUnit unit,
        out string key)
    {
        if (unit == memoryDesktopProcess)
            key = OSDesktopProcessKeys.Memory;
        else if (unit == recordDesktopProcess)
            key = OSDesktopProcessKeys.Record;
        else if (unit == toolDesktopProcess)
            key = OSDesktopProcessKeys.Tool;
        else if (unit == gramophoneDesktopProcess)
            key = OSDesktopProcessKeys.Gramophone;
        else if (unit == energyLoopDesktopProcess)
            key = OSDesktopProcessKeys.EnergyLoop;
        else if (unit == tvDesktopProcess)
            key = OSDesktopProcessKeys.TV;
        else if (unit == paperPushDesktopProcess)
            key = OSDesktopProcessKeys.PaperPush;
        else if (unit == clawMachineDesktopProcess)
            key = OSDesktopProcessKeys.ClawMachine;
        else if (unit == lockHeartDesktopProcess)
            key = OSDesktopProcessKeys.LockHeart;
        else if (unit == fileDesktopProcess)
            key = OSDesktopProcessKeys.File;
        else if (unit == stateCheckDesktopProcess)
            key = OSDesktopProcessKeys.StateCheck;
        else if (unit == characterDesktopProcess)
            key = OSDesktopProcessKeys.Character;
        else if (unit == heartHealToolDesktopProcess)
            key = OSDesktopProcessKeys.HeartHealTool;
        else if (unit == chapter01KeyDesktopProcess)
            key = OSDesktopProcessKeys.KeyX;
        else
        {
            key = null;
            return false;
        }

        return true;
    }

    static bool IsPersistentDesktopProcessKey(string processKey)
    {
        return processKey == OSDesktopProcessKeys.TV || processKey == OSDesktopProcessKeys.PaperPush
            || processKey == OSDesktopProcessKeys.ClawMachine || string.Equals(
                processKey,
                OSDesktopProcessKeys.Memory,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.Record,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.Tool,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.Gramophone,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.EnergyLoop,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.LockHeart,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.File,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.StateCheck,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.Character,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.HeartHealTool,
                StringComparison.Ordinal);
    }

    static bool IsDesktopProcessInitiallyLocked(string processKey)
    {
        return string.Equals(
                processKey,
                OSDesktopProcessKeys.Memory,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.Record,
                StringComparison.Ordinal)
            || string.Equals(
                processKey,
                OSDesktopProcessKeys.LockHeart,
                StringComparison.Ordinal);
    }

    DesktopProcessUnit FindDesktopProcessUnit(string processKey)
    {
        if (string.IsNullOrWhiteSpace(processKey))
            return null;

        string key = processKey.Trim();
        if (string.Equals(
            key,
            OSDesktopProcessKeys.Gift,
            StringComparison.Ordinal))
        {
            return mainStoryGiftDesktopProcess;
        }

        if (string.Equals(
            key,
            OSDesktopProcessKeys.KeyX,
            StringComparison.Ordinal))
        {
            return chapter01KeyDesktopProcess;
        }

        for (int i = 0; i < desktopProcessUnits.Count; i++)
        {
            DesktopProcessUnit unit = desktopProcessUnits[i];
            if (unit == null)
                continue;

            if (TryGetPersistentDesktopProcessKey(unit, out string unitKey)
                && string.Equals(key, unitKey, StringComparison.Ordinal))
            {
                return unit;
            }
        }

        return null;
    }

    void HandleDesktopProcessNewStateChanged(
        string processKey,
        bool isNew)
    {
        FindDesktopProcessUnit(processKey)?.SetNewState(isNew, !isNew);
    }

    void HandleDesktopProcessLockStateChanged(
        string processKey,
        bool isLocked)
    {
        FindDesktopProcessUnit(processKey)?.SetLockedState(isLocked);
    }

    void BindDesktopProcessNewStateEvents()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (ReferenceEquals(_desktopNewStateDataManager, dataManager))
            return;

        UnbindDesktopProcessNewStateEvents();
        _desktopNewStateDataManager = dataManager;
        if (_desktopNewStateDataManager == null)
            return;

        _desktopNewStateDataManager.DesktopProcessNewStateChanged +=
            HandleDesktopProcessNewStateChanged;
        _desktopNewStateDataManager.DesktopProcessLockStateChanged +=
            HandleDesktopProcessLockStateChanged;
        _desktopNewStateDataManager.ToolProgramInstalled += HandleToolProgramInstalled;
        _desktopNewStateDataManager.PaperPushUnlocked += HandlePaperPushUnlocked;
    }

    void UnbindDesktopProcessNewStateEvents()
    {
        if (_desktopNewStateDataManager == null)
            return;

        _desktopNewStateDataManager.DesktopProcessNewStateChanged -=
            HandleDesktopProcessNewStateChanged;
        _desktopNewStateDataManager.DesktopProcessLockStateChanged -=
            HandleDesktopProcessLockStateChanged;
        _desktopNewStateDataManager.ToolProgramInstalled -= HandleToolProgramInstalled;
        _desktopNewStateDataManager.PaperPushUnlocked -= HandlePaperPushUnlocked;
        _desktopNewStateDataManager = null;
    }

    void InitializeResourceViews()
    {
        for (int i = 0; i < resourceViews.Length; i++)
            resourceViews[i]?.Initialize();
    }

    void InitializeLocalizedTexts()
    {
        for (int i = 0; i < localizedTexts.Length; i++)
            localizedTexts[i]?.Initialize();
    }

    void BindDesktopProcessUnits()
    {
        UnbindDesktopProcessUnits();
        for (int i = 0; i < desktopProcessUnits.Count; i++)
        {
            DesktopProcessUnit unit = desktopProcessUnits[i];
            if (unit != null)
            {
                unit.PointerEntered += HandleDesktopProcessPointerEntered;
                unit.Clicked += HandleDesktopProcessClicked;
            }
        }

        if (mainStoryGiftDesktopProcess != null
            && !desktopProcessUnits.Contains(mainStoryGiftDesktopProcess))
        {
            mainStoryGiftDesktopProcess.PointerEntered +=
                HandleDesktopProcessPointerEntered;
            mainStoryGiftDesktopProcess.Clicked +=
                HandleDesktopProcessClicked;
        }

        if (chapter01KeyDesktopProcess != null
            && !desktopProcessUnits.Contains(chapter01KeyDesktopProcess))
        {
            chapter01KeyDesktopProcess.PointerEntered +=
                HandleDesktopProcessPointerEntered;
            chapter01KeyDesktopProcess.Clicked +=
                HandleDesktopProcessClicked;
        }
    }

    void UnbindDesktopProcessUnits()
    {
        if (desktopProcessUnits == null)
            return;

        for (int i = 0; i < desktopProcessUnits.Count; i++)
        {
            DesktopProcessUnit unit = desktopProcessUnits[i];
            if (unit != null)
            {
                unit.PointerEntered -= HandleDesktopProcessPointerEntered;
                unit.Clicked -= HandleDesktopProcessClicked;
            }
        }

        if (mainStoryGiftDesktopProcess != null
            && !desktopProcessUnits.Contains(mainStoryGiftDesktopProcess))
        {
            mainStoryGiftDesktopProcess.PointerEntered -=
                HandleDesktopProcessPointerEntered;
            mainStoryGiftDesktopProcess.Clicked -=
                HandleDesktopProcessClicked;
        }

        if (chapter01KeyDesktopProcess != null
            && !desktopProcessUnits.Contains(chapter01KeyDesktopProcess))
        {
            chapter01KeyDesktopProcess.PointerEntered -=
                HandleDesktopProcessPointerEntered;
            chapter01KeyDesktopProcess.Clicked -=
                HandleDesktopProcessClicked;
        }
    }

    void HandleDesktopProcessPointerEntered(DesktopProcessUnit unit)
    {
        if (unit == null)
            return;

        if (!unit.IsStateInitialized)
            InitializeDesktopProcessUnit(unit);

        bool linkMessageTriggered = linkMessageController != null
            && linkMessageController.NotifyDesktopProcessPointerEnter(
                unit.ProcessKey);
        if (linkMessageTriggered)
            ConsumeDesktopProcessNewState(unit);

        DesktopProcessPointerEntered?.Invoke(unit);
    }

    void HandleDesktopProcessClicked(DesktopProcessUnit unit)
    {
        if (unit == chapter01KeyDesktopProcess)
            return;

        // Chapter 2's final night: 状态检查 answers with the thunder instead of its window.
        if (Chapter02FinalTalk?.TryHandleDesktopProcessClick(unit) == true)
            return;

        OSWindowUnit launchedWindow = GetDesktopProcessWindow(unit);
        bool wasWindowOpen = launchedWindow != null
            && launchedWindow.IsOpen;
        bool interactionSucceeded = false;
        if (unit == memoryDesktopProcess)
            interactionSucceeded = OpenMemoryWindow();
        else if (unit == recordDesktopProcess)
            interactionSucceeded = OpenRecordWindow();
        else if (unit == toolDesktopProcess)
            interactionSucceeded = OpenToolWindow();
        else if (unit == tvDesktopProcess)
            interactionSucceeded = OpenTVWindow();
        else if (unit == lockHeartDesktopProcess)
            interactionSucceeded = OpenMotherHeartWindow();
        else if (unit == paperPushDesktopProcess)
            interactionSucceeded = OpenPaperPushWindow();
        else if (unit == clawMachineDesktopProcess && IsChapter02ClawMachineDiscovered())
        {
            interactionSucceeded = RequestChapter02ClawMachineLaunch();
        }
        else if (unit == gramophoneDesktopProcess || unit == energyLoopDesktopProcess)
        {
            if (!IsUninstalledDesktopProcess(unit))
                interactionSucceeded = OpenWindow(launchedWindow, unit, launchedWindow != null
                    ? launchedWindow.name : unit.name);
        }
        else if (unit == fileDesktopProcess)
            interactionSucceeded = OpenFolderWindow();
        else if (unit == characterDesktopProcess)
        {
            interactionSucceeded = OpenCharacterWindow();
            // A private chat can be closed while its Live owner stays open.
            // The explicit launcher is also its reopen action in that case.
            if (characterWindow != null && characterWindow.IsOpen)
                Chapter04Story?.ResumeDialogue();
        }
        else if (unit == heartHealToolDesktopProcess)
            interactionSucceeded = OpenHeartHealToolWindow();
        else if (unit == stateCheckDesktopProcess)
        {
            TryConfirmChapter01EntryFromStateCheckLaunch();
            interactionSucceeded = OpenStateCheckWindow();
        }
        else if (unit == mainStoryGiftDesktopProcess
            && IsMainStoryGiftLanded)
        {
            interactionSucceeded = OpenMainStoryGiftWindow();
        }

        if (!wasWindowOpen
            && launchedWindow != null
            && launchedWindow.IsOpen)
        {
            AudioManager.Instance?.PlaySfx(OpenUnitSfxKey);
        }

        if (interactionSucceeded)
            PlayInteractionFeedback();

        if (launchedWindow != null
            && launchedWindow.IsOpen
            && !launchedWindow.IsMinimized)
        {
            NotifyWindowLauncherPressed(launchedWindow);
        }

        if (interactionSucceeded || IsDesktopProcessWindowOpen(unit))
            ConsumeDesktopProcessNewState(unit);

        linkMessageController?.NotifyDesktopProcessClicked(unit.ProcessKey);
        DesktopProcessClicked?.Invoke(unit);
    }

    OSWindowUnit GetDesktopProcessWindow(DesktopProcessUnit unit)
    {
        if (unit == lockHeartDesktopProcess) return motherHeartController != null ? motherHeartController.Window : null;
        if (unit == tvDesktopProcess) return tvWindow;
        if (unit == memoryDesktopProcess)
            return memoryWindow;
        if (unit == recordDesktopProcess)
            return recordWindow;
        if (unit == toolDesktopProcess)
            return toolWindow;
        if (unit == gramophoneDesktopProcess)
            return gramophoneWindow;
        if (unit == energyLoopDesktopProcess)
            return energyLoopWindow;
        if (unit == paperPushDesktopProcess)
            return paperPushWindow;
        if (unit == clawMachineDesktopProcess)
            return clawMachineGameWindow;
        if (unit == fileDesktopProcess)
            return folderWindow;
        if (unit == characterDesktopProcess)
            return characterWindow;
        if (unit == heartHealToolDesktopProcess)
            return heartHealToolWindow;
        if (unit == stateCheckDesktopProcess)
            return stateCheckWindow;
        if (unit == mainStoryGiftDesktopProcess)
            return mainStoryGiftWindow;

        return null;
    }

    bool IsDesktopProcessWindowOpen(DesktopProcessUnit unit)
    {
        if (unit == lockHeartDesktopProcess) return motherHeartController != null && motherHeartController.Window.IsOpen;
        if (unit == tvDesktopProcess) return tvWindow != null && tvWindow.IsOpen;
        if (unit == memoryDesktopProcess)
            return memoryWindow != null && memoryWindow.IsOpen;
        if (unit == recordDesktopProcess)
            return recordWindow != null && recordWindow.IsOpen;
        if (unit == toolDesktopProcess)
            return toolWindow != null && toolWindow.IsOpen;
        if (unit == gramophoneDesktopProcess)
            return gramophoneWindow != null && gramophoneWindow.IsOpen;
        if (unit == energyLoopDesktopProcess)
            return energyLoopWindow != null && energyLoopWindow.IsOpen;
        if (unit == paperPushDesktopProcess)
            return paperPushWindow != null && paperPushWindow.IsOpen;
        if (unit == clawMachineDesktopProcess)
            return clawMachineGameWindow != null && clawMachineGameWindow.IsOpen;
        if (unit == fileDesktopProcess)
            return folderWindow != null && folderWindow.IsOpen;
        if (unit == characterDesktopProcess)
            return characterWindow != null && characterWindow.IsOpen;
        if (unit == heartHealToolDesktopProcess)
        {
            return heartHealToolWindow != null
                && heartHealToolWindow.IsOpen;
        }
        if (unit == stateCheckDesktopProcess)
            return stateCheckWindow != null && stateCheckWindow.IsOpen;
        if (unit == mainStoryGiftDesktopProcess)
            return mainStoryGiftWindow != null && mainStoryGiftWindow.IsOpen;

        return false;
    }

    void ConsumeDesktopProcessNewState(DesktopProcessUnit unit)
    {
        if (unit == null || !unit.HasNewContent)
            return;

        if (unit == mainStoryGiftDesktopProcess)
        {
            unit.SetNewState(false, true);
            return;
        }

        if (unit.UsesPersistentState)
            SetDesktopProcessNew(unit.ProcessKey, false);
    }



    public bool OpenMemoryWindow()
    {
        ResolveReferences();
        if (Chapter04Entry?.BlocksMemoryOpen == true) return false;
        if (Chapter03Entry?.BlocksMemory == true) return false;
        if (Chapter03Entry?.IsAwaitingMemoryOpen == true && !Chapter03Entry.TryEnterMemory()) return false;
        bool opened = OpenWindow(
            memoryWindow,
            memoryDesktopProcess,
            "WindowUnit_Memory");
        if (memoryWindow != null && memoryWindow.IsOpen) Chapter03Entry?.NotifyMemoryOpened();
        return opened;
    }

    bool OpenRecordWindow()
    {
        return OpenWindow(
            recordWindow,
            recordDesktopProcess,
            "WindowUnit_Record");
    }

    public bool OpenToolWindow()
    {
        ResolveReferences();
        if (GameDataManager.Instance?.IsToolUnlocked != true)
            return false;

        return OpenWindow(
            toolWindow,
            toolDesktopProcess,
            "WindowUnit_Tool");
    }

    public bool OpenPaperPushWindow()
    {
        ResolveReferences();
        bool opened = GameDataManager.Instance?.IsPaperPushPlayable == true
            && OpenWindow(paperPushWindow, paperPushDesktopProcess, "WindowUnit_PaperPush");
        if (opened) SetDesktopProcessNew(OSDesktopProcessKeys.PaperPush, false);
        return opened;
    }

    void HandlePaperPushUnlocked()
    {
        InitializeDesktopProcessUnit(paperPushDesktopProcess);
        SetDesktopProcessNew(OSDesktopProcessKeys.PaperPush, true);
    }

    void HandleToolProgramInstalled(string programId)
    {
        if (programId == OSToolProgramIds.TV)
        { RefreshChapter04TV(); SetDesktopProcessNew(OSDesktopProcessKeys.TV, true); return; }
        if (programId == OSToolProgramIds.Gramophone)
        {
            InitializeDesktopProcessUnit(gramophoneDesktopProcess);
            RefreshGramophoneInstalledMessage();
        }
        else if (programId == OSToolProgramIds.EnergyLoop)
            InitializeDesktopProcessUnit(energyLoopDesktopProcess);
        else if (programId == OSToolProgramIds.MemoryRepair01)
            RefreshMemoryInstallationIcon();
        else if (programId == OSToolProgramIds.MemoryRepair02
            || programId == OSToolProgramIds.MemoryRepair03)
        {
            RefreshMemoryInstallationIcon();
            RestartMemoryWindowAfterUpgrade();
        }
        else if (programId == OSToolProgramIds.MemoryRepair04)
            RefreshMemoryInstallationIcon();
    }

    void RefreshGramophoneInstalledMessage()
    {
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 1 || !data.IsGramophoneInstalled
            || !IsOSVisible || IsSystemInputLocked || OSSessionPersistence.IsRestoring
            || GameSessionFlow.Instance?.IsBusy == true || linkMessageController == null)
            return;
        var state = data.OSCharacterInteractionState;
        if (state.HasConsumedGlobalEntry(GramophoneInstalledMessage)
            || state.pendingGlobalEntryId == GramophoneInstalledMessage
            || state.optionCountdownQueue.Exists(item => item != null
                && item.entryId == GramophoneInstalledMessage)) return;
        // An install during a protected dialogue is delivered at the next free
        // reading slot, including after loading, without requiring a window open.
        linkMessageController.NotifyGlobalExternalSignal(GramophoneInstalledSignal);
    }

    void RefreshMemoryInstallationIcon()
    {
        Image icon = memoryDesktopProcess != null ? memoryDesktopProcess.ProcessIcon : null;
        if (icon == null)
            return;

        if (_originalMemoryIcon == null)
            _originalMemoryIcon = icon.sprite;
        GameDataManager data = GameDataManager.Instance;
        icon.sprite = data?.IsMemoryRepair03Installed == true
            && data.OSToolProgramData != null
            && data.OSToolProgramData.TryGetProgram(OSToolProgramIds.MemoryRepair03, out var upgrade03)
            && upgrade03.Icon != null ? upgrade03.Icon
            : data?.IsMemoryRepair02Installed == true
            && memoryRepair02Icon != null ? memoryRepair02Icon
            : data?.IsMemoryRepair01Installed == true
            && memoryRepair01Icon != null ? memoryRepair01Icon : _originalMemoryIcon;

        Image taskbarIcon = memoryWindow != null
            ? memoryWindow.LinkedProcessUnit?.ProcessIcon : null;
        if (taskbarIcon != null)
            taskbarIcon.sprite = icon.sprite;
    }

    bool IsUninstalledDesktopProcess(DesktopProcessUnit unit)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (unit == tvDesktopProcess) return dataManager?.IsTVInstalled != true;
        if (unit == toolDesktopProcess)
            return dataManager?.IsToolUnlocked != true;
        if (unit == gramophoneDesktopProcess)
            return dataManager?.IsGramophoneInstalled != true;
        if (unit == energyLoopDesktopProcess)
            return dataManager?.IsEnergyLoopInstalled != true;
        if (unit == paperPushDesktopProcess)
            return dataManager?.IsPaperPushUnlocked != true;
        if (unit == clawMachineDesktopProcess)
            // Discovery first exposes an embedded application entry. It only
            // participates in persistent desktop/folder grids after placement.
            return !IsChapter02ClawMachineOnDesktop();
        return false;
    }

    public bool TryOpenToolFromShortcut()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (!IsOSVisible || IsSystemInputLocked || dataManager == null)
            return false;

        ResolveReferences();
        bool awaitingShortcut = linkMessageController != null
            && linkMessageController.IsAwaitingExternalCompletion(
                OSCharacterInteractionIds.Chapter01MemoryRepair,
                OSCharacterInteractionIds.Chapter01Tool.AwaitShortcut);
        if ((!dataManager.IsToolUnlocked && !awaitingShortcut)
            || toolWindow == null || toolDesktopProcess == null)
        {
            return false;
        }

        bool wasOpen = toolWindow.IsOpen;
        OpenWindow(toolWindow, toolDesktopProcess, "WindowUnit_Tool");
        if (!toolWindow.IsOpen)
            return false;

        dataManager.UnlockTool();
        toolDesktopProcess.gameObject.SetActive(true);
        EnsureDesktopProcessLayout(toolDesktopProcess);
        SetDesktopProcessNew(OSDesktopProcessKeys.Tool, false);
        if (awaitingShortcut)
        {
            linkMessageController.TryCompleteAwaitedEntry(
                OSCharacterInteractionIds.Chapter01MemoryRepair,
                OSCharacterInteractionIds.Chapter01Tool.AwaitShortcut);
        }
        if (!wasOpen)
            AudioManager.Instance?.PlaySfx(OpenUnitSfxKey);
        NotifyWindowLauncherPressed(toolWindow);
        return true;
    }

    bool TryActivateChapter01MemoryRepairSequence()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null || dataManager.CurrentChapterIndex != 1
            || dataManager.CurrentOSMemoryDataIndex == OSMemoryProcessSaveData.InvalidDataIndex
            || !dataManager.IsChapter01KeyXAcquired
            || memoryDesktopProcess == null || memoryDesktopProcess.IsLocked
            || linkMessageController == null)
        {
            return false;
        }

        string sequenceId = dataManager.OSCharacterInteractionState.activeSequenceId;
        if (sequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair)
            return true;
        if (sequenceId != OSCharacterInteractionIds.Chapter01LiveReconnect)
            return false;

        return linkMessageController.ActivateSequence(OSCharacterInteractionIds.Chapter01MemoryRepair);
    }

    public bool BeginChapter01ToolIntroduction()
    {
        return IsOSVisible && TryActivateChapter01MemoryRepairSequence()
            && linkMessageController.TryPresentEntry(OSCharacterInteractionIds.Chapter01Tool.FilesFound);
    }

    void HandleToolInitializationCompleted()
    {
        if (GameDataManager.Instance?.OSCharacterInteractionState.activeSequenceId
            == OSCharacterInteractionIds.Chapter01MemoryRepair)
        {
            linkMessageController?.TryNotifyExternalSignal(
                OSCharacterInteractionIds.Chapter01Tool.InitializedSignal);
        }
    }

    void TryShowMemoryUpgrade02ToolMessage()
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || !data.IsMemoryRepair02Available || data.IsMemoryRepair02Installed
            || toolWindow == null || !toolWindow.IsOpen || toolWindow.IsMinimized
            || !TryActivateChapter01MemoryRepairSequence())
            return;
        linkMessageController.TryNotifyExternalSignal(
            OSCharacterInteractionIds.Chapter01Tool.MemoryUpgrade02OpenedSignal);
    }

    void HandleToolProgramLibraryOpened()
    {
        linkMessageController?.TryCompleteAwaitedEntry(
            OSCharacterInteractionIds.Chapter01MemoryRepair,
            OSCharacterInteractionIds.Chapter01Tool.FollowUp);
        TryOfferToolDecoration();
        HandleToolInstallationHintRequested();
    }

    void TryOfferToolDecoration()
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || data.ToolDecorationState != OSToolDecorationState.None
            || data.OSCharacterInteractionState.activeSequenceId != OSCharacterInteractionIds.Chapter01MemoryRepair
            || !data.OSCharacterInteractionState.HasCompletedEntry(OSCharacterInteractionIds.Chapter01Tool.FollowUp))
            return;

        if (characterWindow != null && (!characterWindow.IsOpen || characterWindow.IsMinimized))
            OpenCharacterWindowForReconnect(0.15f);
        linkMessageController?.TryNotifyExternalSignal(OSCharacterInteractionIds.Chapter01Tool.LibraryOpenedSignal);
    }

    void HandleToolDecorationCompleted()
    {
        linkMessageController?.TryNotifyExternalSignal(OSCharacterInteractionIds.Chapter01Tool.DecorationDoneSignal);
    }

    void HandleToolInstallationHintRequested()
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || data.OSCharacterInteractionState.activeSequenceId != OSCharacterInteractionIds.Chapter01MemoryRepair
            || toolWindowController?.ProgramLibrary == null
            || !toolWindowController.ProgramLibrary.IsInstallationHintRequested
            || !data.OSCharacterInteractionState.HasCompletedEntry(OSCharacterInteractionIds.Chapter01Tool.FollowUp))
            return;
        linkMessageController?.TryNotifyExternalSignal(OSCharacterInteractionIds.Chapter01Tool.InstallHintSignal);
    }

    bool OpenFolderWindow()
    {
        return OpenWindow(
            folderWindow,
            fileDesktopProcess,
            "WindowUnit_Folder");
    }

    bool OpenCharacterWindow()
    {
        return OpenWindow(
            characterWindow,
            characterDesktopProcess,
            "WindowUnit_Character");
    }

    public bool OpenCharacterWindowForReconnect(float openDuration)
    {
        ResolveReferences();
        bool opened = OpenWindow(
            characterWindow,
            characterDesktopProcess,
            "WindowUnit_Character",
            Mathf.Max(0f, openDuration));
        if (characterWindow != null && characterWindow.IsOpen)
        {
            characterWindow.SetAlwaysOnTop(true);
            characterWindow.BringToFront();
        }

        return opened || (characterWindow != null && characterWindow.IsOpen);
    }

    public bool OpenHeartHealTool(int heartProcessIndex)
    {
        ResolveReferences();
        heartHealToolWindowController?.ConfigureTreatment(heartProcessIndex);
        return OpenHeartHealToolWindow();
    }

    public void ConfigureHeartHealTreatment(int heartProcessIndex)
    {
        ResolveReferences();
        heartHealToolWindowController?.ConfigureTreatment(heartProcessIndex);
    }

    bool OpenHeartHealToolWindow()
    {
        return OpenWindow(
            heartHealToolWindow,
            heartHealToolDesktopProcess,
            "WindowUnit_HeartHealTool");
    }

    public bool OpenMainStoryGiftWindow()
    {
        ResolveReferences();
        if (!IsMainStoryGiftLanded
            || _hasMainStoryGiftWindowClosed
            || mainStoryGiftWindow == null)
        {
            return false;
        }

        mainStoryGiftWindow.Closed -= HandleMainStoryGiftWindowClosed;
        mainStoryGiftWindow.Closed += HandleMainStoryGiftWindowClosed;
        bool wasOpen = mainStoryGiftWindow.IsOpen;
        mainStoryGiftWindow.OpenStandalone();
        mainStoryGiftWindow.SetAlwaysOnTop(true);
        mainStoryGiftWindow.BringToFront();
        return !wasOpen && mainStoryGiftWindow.IsOpen;
    }

    public bool OpenStateCheck(float progress)
    {
        ConfigureStateCheckProgress(progress);
        return OpenStateCheckWindow();
    }

    public void ConfigureStateCheckProgress(float progress)
    {
        ResolveReferences();
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        float savedProgress =
            dataManager.SetCurrentStateCheckProgress(progress);

        if (stateCheckWindow != null
            && stateCheckWindow.IsOpen
            && stateCheckWindowController != null
            && !Mathf.Approximately(
                stateCheckWindowController.Progress,
                savedProgress))
        {
            stateCheckWindowController?.RefreshFromSave();
        }

        SyncErrorWindowEffect();
    }

    public bool StartChapter(int chapterSequence)
    {
        ResolveReferences();
        if (chapterSequence == 4) return OpenTVWindow();
        if (chapterSequence == 3 && Chapter03Entry?.CanStartMemory != true) return false;
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null
            || chapterSequence != dataManager.CurrentChapterIndex
            || !dataManager.TryGetOSMemoryChapterProgressRange(
                chapterSequence,
                out OSMemoryChapterProgressRange range)
            || range == null
            || !range.HasMemoryDataRange
            || range.Contains(dataManager.CurrentOSMemoryDataIndex)
            || !dataManager.TryGetOSMemoryData(
                range.FirstDataIndex,
                out OSMemoryReadData _))
        {
            return false;
        }

        // Future chapter-specific startup actions stay behind this entry point.
        dataManager.SetCurrentOSMemoryData(range.FirstDataIndex);
        ChapterStarted?.Invoke(chapterSequence);
        return true;
    }

    bool OpenStateCheckWindow()
    {
        bool opened = OpenWindow(
            stateCheckWindow,
            stateCheckDesktopProcess,
            "WindowUnit_StateCheck");
        SyncErrorWindowEffect();
        return opened;
    }

    void TryConfirmChapter01EntryFromStateCheckLaunch()
    {
        if (_hasChapter01EntryConfirmed
            || linkMessageController == null)
            return;

        linkMessageController.TrySelectDisplayedOptionImmediately(
            OSCharacterInteractionIds.Chapter01LiveReconnect,
            OSCharacterInteractionIds.Chapter01Entries.LiveEntryConfirm,
            OSCharacterInteractionIds.Chapter01Options.Confirm);
    }

    [Button("Start Error Pool Effect", ButtonSizes.Medium)]
    public void StartErrorWindowEffect()
    {
        ResolveReferences();
        if (errorWindowPoolController == null)
        {
            Debug.LogWarning(
                "[OS] ErrorPool controller is missing.",
                this);
            return;
        }

        errorWindowPoolController.StartEffect();
    }

    [Button("Stop Error Pool Effect", ButtonSizes.Medium)]
    public void StopErrorWindowEffect()
    {
        ResolveReferences();
        errorWindowPoolController?.StopEffect();
    }

    void HandleStateCheckWindowOpenStarted(OSWindowUnit source)
    {
        if (source != stateCheckWindow)
            return;

        CloseErrorWindowsOverlappingStateCheckCore(source);
        SyncErrorWindowEffect();
        TryShowChapter01StateCheckMessage();
    }

    void CloseErrorWindowsOverlappingStateCheckCore(OSWindowUnit window)
    {
        if (window != stateCheckWindow
            || !window.IsOpen
            || window.IsMinimized)
        {
            return;
        }

        errorWindowPoolController
            ?.CloseWindowsOverlappingProtectedCharacterArea();
    }

    void SyncErrorWindowEffect()
    {
        if (!_hasStarted || errorWindowPoolController == null)
            return;

        GameDataManager dataManager = GameDataManager.Instance;
        bool shouldRun = IsOSVisible
            && dataManager != null
            && Mathf.RoundToInt(
                dataManager.CurrentStateCheckProgress) == 1;
        if (shouldRun)
            errorWindowPoolController.StartEffect();
        else
            errorWindowPoolController.StopEffect();
    }

    void HandleStateCheckPuzzleCompleted()
    {
        StopStateCheckRestartDelay();
        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                Mathf.Max(0f, stateCheckRestartDelay),
                () =>
                {
                    if (_stateCheckRestartDelayTween != delay)
                        return;

                    _stateCheckRestartDelayTween = null;
                    ResolveReferences();
                    GameDataManager.Instance
                        ?.SetCurrentStateCheckProgress(2f);
                    if (stateCheckWindow != null && stateCheckWindow.IsOpen)
                        stateCheckWindow.Close();
                    errorWindowPoolController?.StopEffect();
                    osSfxAction?.PlayCloseAndReOpenPart();
                },
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        _stateCheckRestartDelayTween = delay;
        delay.OnKill(() =>
        {
            if (_stateCheckRestartDelayTween == delay)
                _stateCheckRestartDelayTween = null;
        });
    }

    void StopStateCheckRestartDelay()
    {
        if (_stateCheckRestartDelayTween == null)
            return;

        Tween delay = _stateCheckRestartDelayTween;
        _stateCheckRestartDelayTween = null;
        delay.Kill(false);
    }

    bool OpenWindow(
        OSWindowUnit window,
        DesktopProcessUnit desktopProcess,
        string windowName,
        float? openDurationOverride = null)
    {
        // Session restoration and taskbar routes share this gate with the desktop launcher.
        if (window != null && window == memoryWindow
            && (Chapter03Entry?.BlocksMemory == true || Chapter04Entry?.BlocksMemoryOpen == true))
            return false;
        if (window != null && motherHeartController != null && window == motherHeartController.Window
            && !IsMotherHeartUnlocked) return false;
        if (window == null)
        {
            Debug.LogWarning($"[OS] {windowName} is missing.", this);
            return false;
        }

        if (window.IsOpen)
        {
            bool wasMinimized = window.IsMinimized;
            bool wasActive = window.gameObject.activeSelf;
            int previousSiblingIndex = window.transform.GetSiblingIndex();
            window.RestoreAndBringToFront();
            return wasMinimized
                || !wasActive
                || previousSiblingIndex
                    != window.transform.GetSiblingIndex();
        }

        ProcessUnit processUnit = AcquireProcessUnit();
        if (processUnit == null)
        {
            Debug.LogWarning("[OS] No available ProcessUnit exists in GroupPool.", this);
            return false;
        }

        processUnit.Initialize(window, desktopProcess?.ProcessIcon);
        if (openDurationOverride.HasValue)
            window.Open(processUnit, openDurationOverride.Value);
        else
            window.Open(processUnit);
        return window.IsOpen;
    }


    ProcessUnit AcquireProcessUnit()
    {
        for (int i = 0; i < processUnits.Count; i++)
        {
            ProcessUnit unit = processUnits[i];
            if (unit != null && unit.IsAvailable)
                return unit;
        }

        return null;
    }

    void InitializeWindowSystem()
    {
        ResolveFirstHeartHealEndingReferences();
        ResolveChapter01KeyReferences();
        mainStoryGiftWindowController?.PrepareRuntime();
        memoryWindowController?.PrepareRuntime();
        recordWindowController?.PrepareRuntime();
        toolWindowController?.PrepareRuntime();
        heartHealToolWindowController?.PrepareRuntime();
        stateCheckWindowController?.PrepareRuntime();
        errorWindowPoolController?.PrepareRuntime();

        for (int i = 0; i < processUnits.Count; i++)
            processUnits[i]?.PrepareForPool();

        for (int i = 0; i < windowUnits.Count; i++)
            windowUnits[i]?.PrepareRuntimeClosed();

        CacheFirstHeartHealEndingKeyPose();
        firstHeartHealSpecialWindow?.PrepareRuntimeClosed();
        firstHeartHealSpecialWindow01?.PrepareRuntimeClosed();
        InitializeChapter01KeyRuntime();
        EnsureLockHeartIconGlitch();

        SetMouseActiveWindow(null);
    }

    void InitializeChapter01KeyRuntime()
    {
        ResolveChapter01KeyReferences();
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null
            || chapter01KeyWindow == null
            || chapter01KeyDesktopProcess == null
            || chapter01KeySourceGrid == null)
        {
            _chapter01KeyRuntimeInitialized = false;
            return;
        }

        chapter01KeyWindow.PrepareRuntimeClosed();
        InitializeDesktopProcessUnit(chapter01KeyDesktopProcess);
        chapter01KeyDesktopProcess.gameObject.SetActive(true);

        if (dataManager.IsChapter01KeyXAcquired)
        {
            chapter01KeySourceGrid.Remove(chapter01KeyDesktopProcess);
            EnsureDesktopProcessLayout(chapter01KeyDesktopProcess);
        }
        else
        {
            desktopProcessGrid?.Remove(chapter01KeyDesktopProcess);
            folderProcessGrid?.Remove(chapter01KeyDesktopProcess);
            _desktopLayoutUnits.Remove(chapter01KeyDesktopProcess);
            chapter01KeySourceGrid.ClearRuntimeSlots();
            chapter01KeySourceGrid.TryPlace(
                chapter01KeyDesktopProcess,
                0,
                out _);
            chapter01KeySourceGrid.ApplyLayout();
        }

        _chapter01KeyRuntimeInitialized = true;
    }

    void BindChapter01ReconnectEvents()
    {
        if (closeAndReOpenPart == null
            || characterWindowController == null)
        {
            ResolveReferences();
        }

        if (closeAndReOpenPart != null)
        {
            closeAndReOpenPart.BootRevealCompleted -=
                HandleChapter01BootRevealCompleted;
            closeAndReOpenPart.BootRevealCompleted +=
                HandleChapter01BootRevealCompleted;
        }

        if (characterWindowController == null)
            return;

        characterWindowController.Chapter01NormalEntryRequested -=
            HandleChapter01NormalEntryRequested;
        characterWindowController.Chapter01NormalEntryRequested +=
            HandleChapter01NormalEntryRequested;
        characterWindowController.Chapter01ForcedEntryRequested -=
            HandleChapter01ForcedEntryRequested;
        characterWindowController.Chapter01ForcedEntryRequested +=
            HandleChapter01ForcedEntryRequested;
    }

    void UnbindChapter01ReconnectEvents()
    {
        if (closeAndReOpenPart != null)
        {
            closeAndReOpenPart.BootRevealCompleted -=
                HandleChapter01BootRevealCompleted;
        }

        if (characterWindowController == null)
            return;

        characterWindowController.Chapter01NormalEntryRequested -=
            HandleChapter01NormalEntryRequested;
        characterWindowController.Chapter01ForcedEntryRequested -=
            HandleChapter01ForcedEntryRequested;
    }

    void HandleChapter01BootRevealCompleted()
    {
        if (GameDataManager.Instance?.OSCharacterInteractionState.activeSequenceId
            == OSCharacterInteractionIds.Chapter01MemoryRepair)
        {
            return;
        }

        ProtectChapter01LiveIntro();
        bool sequenceActivated = linkMessageController != null
            && linkMessageController.ActivateSequence(
                OSCharacterInteractionIds.Chapter01LiveReconnect);
        if (!sequenceActivated)
        {
            Debug.LogWarning(
                "[OS] Chapter01 Live reconnect LinkMessage sequence "
                + "could not be activated.",
                this);
        }

        characterWindowController?.BeginChapter01Reconnect();
    }

    void HandleChapter01NormalEntryRequested()
    {
        StartMainStory();
        linkMessageController?.NotifyExternalSignal(
            OSCharacterInteractionIds.Chapter01Signals.LiveEntryNormal);
    }

    void HandleChapter01ForcedEntryRequested()
    {
        StartMainStory();
        linkMessageController?.NotifyExternalSignal(
            OSCharacterInteractionIds.Chapter01Signals.LiveEntryForced);
    }

    void TryShowChapter01StateCheckMessage()
    {
        if (_hasChapter01StateCheckMessageShown
            || !IsChapter01RecordGuidanceAvailable())
        {
            return;
        }

        _hasChapter01StateCheckMessageShown =
            linkMessageController != null
            && linkMessageController.TryNotifyExternalSignal(
                OSCharacterInteractionIds.Chapter01Signals
                    .StateCheckOpened);
    }

    void QueueChapter01StateCheckMessage()
    {
        StopChapter01StateCheckMessageDelay();
        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                0f,
                () =>
                {
                    if (_chapter01StateCheckMessageDelayTween != delay)
                        return;

                    _chapter01StateCheckMessageDelayTween = null;
                    TryShowChapter01StateCheckMessage();
                },
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        _chapter01StateCheckMessageDelayTween = delay;
        delay.OnKill(() =>
        {
            if (_chapter01StateCheckMessageDelayTween == delay)
                _chapter01StateCheckMessageDelayTween = null;
        });
    }

    void StopChapter01StateCheckMessageDelay()
    {
        if (_chapter01StateCheckMessageDelayTween == null)
            return;

        Tween delay = _chapter01StateCheckMessageDelayTween;
        _chapter01StateCheckMessageDelayTween = null;
        delay.Kill(false);
    }

    void HandleChapter01RecordPointerEntered(int chapterSequence)
    {
        if (chapterSequence != Chapter01RecordSequence
            || !IsChapter01RecordGuidanceAvailable()
            || _isChapter01RecordPromptRequested
            || _isChapter01RecordAutomationActive)
        {
            return;
        }

        _isChapter01RecordHovered = true;
        if (!_hasChapter01RecordHoverMessageShown)
        {
            _hasChapter01RecordHoverMessageShown =
                linkMessageController != null
                && linkMessageController.TryNotifyExternalSignal(
                    OSCharacterInteractionIds.Chapter01Signals
                        .RecordHovered);
        }
        StartChapter01RecordHoverTimer();
    }

    void HandleChapter01RecordPointerExited(int chapterSequence)
    {
        if (chapterSequence != Chapter01RecordSequence
            || _isChapter01RecordAutomationActive)
        {
            return;
        }

        StopChapter01RecordHoverTracking();
    }

    public bool TryHandleChapter01RecordClick(int chapterSequence)
    {
        if (chapterSequence != Chapter01RecordSequence
            || !IsChapter01RecordGuidanceAvailable())
        {
            return false;
        }

        if (_isChapter01RecordAutomationActive)
            return true;

        StopChapter01RecordHoverTracking();
        RequestChapter01RecordStartPrompt();
        return true;
    }

    bool IsChapter01RecordGuidanceAvailable()
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
                StringComparison.Ordinal);
    }

    void StartChapter01RecordHoverTimer()
    {
        StopChapter01RecordHoverTimer();
        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                Mathf.Max(0f, chapter01RecordHoverTimeout),
                () =>
                {
                    if (_chapter01RecordHoverDelayTween != delay)
                        return;

                    _chapter01RecordHoverDelayTween = null;
                    if (!_isChapter01RecordHovered
                        || _isChapter01RecordPromptRequested
                        || !IsChapter01RecordGuidanceAvailable())
                    {
                        return;
                    }

                    bool presented = linkMessageController != null
                        && linkMessageController.TryNotifyExternalSignal(
                            OSCharacterInteractionIds.Chapter01Signals
                                .RecordHoverTimedOut);
                    if (!presented)
                    {
                        _isChapter01RecordAutomationActive = false;
                        Debug.LogWarning(
                            "[OS] Chapter01 Record hover-timeout message "
                            + "could not be presented.",
                            this);
                    }
                },
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        _chapter01RecordHoverDelayTween = delay;
        delay.OnKill(() =>
        {
            if (_chapter01RecordHoverDelayTween == delay)
                _chapter01RecordHoverDelayTween = null;
        });
    }

    void StopChapter01RecordHoverTracking()
    {
        _isChapter01RecordHovered = false;
        StopChapter01RecordHoverTimer();
    }

    void StopChapter01RecordHoverTimer()
    {
        if (_chapter01RecordHoverDelayTween == null)
            return;

        Tween delay = _chapter01RecordHoverDelayTween;
        _chapter01RecordHoverDelayTween = null;
        delay.Kill(false);
    }

    void BeginChapter01RecordCursorTakeover()
    {
        StopChapter01RecordAutomationSequence();

        CursorHandle cursor = CursorHandle.Instance;
        if (cursor == null || !cursor.AcquireScriptControl(this))
        {
            Debug.LogWarning(
                "[OS] CursorHandle could not begin the Chapter01 "
                + "Record guidance takeover.",
                this);
            return;
        }

        // A queued message or a failed cursor acquisition must not silently
        // block the chapter card before the visible takeover begins.
        _isChapter01RecordAutomationActive = true;
        PlayFirstHeartHealSfx(HeartHealEndingMouseSfxKey);
        float duration = Mathf.Max(0.05f, chapter01CursorTakeoverDuration);
        Sequence sequence = DOTween.Sequence();
        _chapter01RecordAutomationSequence = sequence;
        sequence.Join(DOTween.To(
            () => cursor.ScriptScaleMultiplier,
            value => cursor.SetScriptAppearance(
                this,
                value,
                cursor.ScriptTint),
            Mathf.Max(1f, chapter01CursorTakeoverScale),
            duration));
        sequence.Join(DOTween.To(
            () => cursor.ScriptTint,
            value => cursor.SetScriptAppearance(
                this,
                cursor.ScriptScaleMultiplier,
                value),
            chapter01CursorTakeoverColor,
            duration));
        sequence
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_chapter01RecordAutomationSequence == sequence)
                    _chapter01RecordAutomationSequence = null;
            })
            .OnKill(() =>
            {
                if (_chapter01RecordAutomationSequence == sequence)
                    _chapter01RecordAutomationSequence = null;
                cursor.SetScriptPressed(this, false);
            });
    }

    void ScheduleChapter01RecordAutoClick()
    {
        StopChapter01RecordAutomationSequence();
        CursorHandle cursor = CursorHandle.Instance;
        bool hasCursor = cursor != null
            && cursor.IsScriptControlledBy(this);
        RectTransform target = null;
        bool hasTarget = stateCheckWindowController != null
            && stateCheckWindowController.TryGetChapterRecordTarget(
                Chapter01RecordSequence,
                out target);

        Sequence sequence = DOTween.Sequence();
        _chapter01RecordAutomationSequence = sequence;
        sequence.AppendInterval(Mathf.Max(
            0f,
            chapter01CursorReactionDelay));

        if (hasCursor)
        {
            Vector2 origin = cursor.DisplayedScreenPosition;
            float distance = Mathf.Max(0f, chapter01CursorCheckDistance);
            float checkDuration = Mathf.Max(
                0.05f,
                chapter01CursorCheckMoveDuration);
            sequence.Append(CreateFirstHeartHealCursorMoveTween(
                cursor,
                ClampChapter01CursorPoint(
                    origin + new Vector2(distance, distance * 0.35f)),
                checkDuration,
                1f));
            sequence.Append(CreateFirstHeartHealCursorMoveTween(
                cursor,
                ClampChapter01CursorPoint(
                    origin + new Vector2(-distance * 0.75f, -distance * 0.3f)),
                checkDuration,
                -1f));
            sequence.Append(CreateFirstHeartHealCursorMoveTween(
                cursor,
                ClampChapter01CursorPoint(
                    origin + new Vector2(distance * 0.45f, -distance * 0.65f)),
                checkDuration,
                1f));
            sequence.Append(CreateFirstHeartHealCursorMoveTween(
                cursor,
                ClampChapter01CursorPoint(origin),
                checkDuration,
                -1f));
        }

        if (hasCursor && hasTarget)
        {
            sequence.Append(CreateFirstHeartHealCursorMoveTween(
                cursor,
                GetRectScreenCenter(target),
                chapter01CursorTargetMoveDuration,
                1f));
            sequence.AppendInterval(Mathf.Max(0f, chapter01CursorAimPause));
            sequence.AppendCallback(() =>
                cursor.SetScriptPressed(this, true));
            sequence.AppendInterval(Mathf.Max(
                0.05f,
                chapter01CursorPressDuration));
        }

        sequence.AppendCallback(() =>
        {
            if (_chapter01RecordAutomationSequence == sequence)
                _chapter01RecordAutomationSequence = null;
            if (hasCursor)
                cursor.SetScriptPressed(this, false);

            RequestChapter01RecordStartPrompt();
            FinishChapter01RecordAutomation(hasCursor && hasTarget);
        });
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (_chapter01RecordAutomationSequence == sequence)
                    _chapter01RecordAutomationSequence = null;
                if (hasCursor)
                    cursor.SetScriptPressed(this, false);
            });
    }

    static Vector2 ClampChapter01CursorPoint(Vector2 screenPoint)
    {
        return new Vector2(
            Mathf.Clamp(screenPoint.x, 0f, Mathf.Max(0f, Screen.width)),
            Mathf.Clamp(screenPoint.y, 0f, Mathf.Max(0f, Screen.height)));
    }

    void RequestChapter01RecordStartPrompt()
    {
        bool promptVisible = linkMessageController != null
            && linkMessageController.IsPresenting
            && linkMessageController.DisplayedSequenceId
                == OSCharacterInteractionIds.Chapter01LiveReconnect
            && linkMessageController.DisplayedEntryId
                == OSCharacterInteractionIds.Chapter01Entries.RecordStartPrompt
            && linkMessageController.LinkWindow != null
            && linkMessageController.LinkWindow.IsOpen
            && !linkMessageController.LinkWindow.IsMinimized
            && characterWindow != null
            && characterWindow.IsOpen
            && !characterWindow.IsMinimized;
        if (_isChapter01RecordPromptRequested && promptVisible)
            return;

        StopChapter01RecordHoverTracking();
        // The player can close or minimize Live after the first request.
        // Reopen the actual prompt instead of consuming all subsequent clicks.
        if (characterWindow != null
            && (!characterWindow.IsOpen || characterWindow.IsMinimized))
        {
            OpenCharacterWindow();
        }
        _isChapter01RecordPromptRequested = true;
        bool presented = linkMessageController != null
            && linkMessageController.TryNotifyExternalSignal(
                OSCharacterInteractionIds.Chapter01Signals
                    .RecordStartPrompt);
        if (presented)
        {
            // Repeatable entries are not consumed automatically. Keep this
            // flag so the StateCheck section state survives save/load.
            linkMessageController.SetSequenceMessageConsumed(
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                OSCharacterInteractionIds.Chapter01Entries
                    .RecordStartPrompt,
                true);
            stateCheckWindowController
                ?.PlayChapter01RecordUnlockPresentation();
            stateCheckWindowController
                ?.UnlockChapter01InformationSections();
            return;
        }

        _isChapter01RecordPromptRequested = false;
        Debug.LogWarning(
            "[OS] Chapter01 Record start prompt could not be presented.",
            this);
    }

    void BeginChapter01KeySearchFromPrompt()
    {
        StopChapter01RecordHoverTracking();
        FinishChapter01RecordAutomation(false);
        _isChapter01RecordPromptRequested = false;
        EnsureChapter01MemoryCursor();
    }

    void EnsureChapter01MemoryCursor()
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 1
            || data.CurrentOSMemoryDataIndex != OSMemoryProcessSaveData.InvalidDataIndex
            || !data.TryGetOSMemoryChapterProgressRange(1, out OSMemoryChapterProgressRange range)
            || range == null || !range.HasMemoryDataRange)
            return;

        // Older saves can have unlocked Memory without ever setting a reading cursor.
        // Preserve any recorded reading position before falling back to the chapter start.
        IReadOnlyList<int> history = data.TriggeredOSMemoryDataIndices;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            int index = history[i];
            if (range.Contains(index) && data.TryGetOSMemoryData(index, out OSMemoryReadData _))
            {
                data.SetCurrentOSMemoryData(index);
                return;
            }
        }
        StartChapter(1);
    }

    void TryAdvanceChapter01RecordPromptFromStateCheckClose()
    {
        if (_isClosingStateCheckForChapter01RecordPrompt
            || linkMessageController == null)
        {
            return;
        }

        linkMessageController.TrySelectDisplayedOptionImmediately(
            OSCharacterInteractionIds.Chapter01LiveReconnect,
            OSCharacterInteractionIds.Chapter01Entries.RecordStartPrompt,
            OSCharacterInteractionIds.Chapter01Options.StartRepair);
    }

    void CloseStateCheckForChapter01RecordPrompt()
    {
        if (stateCheckWindow == null || !stateCheckWindow.IsOpen)
            return;

        _isClosingStateCheckForChapter01RecordPrompt = true;
        try
        {
            stateCheckWindow.Close();
        }
        finally
        {
            _isClosingStateCheckForChapter01RecordPrompt = false;
        }
    }

    void ScheduleChapter01KeyWindowOpen()
    {
        StopChapter01KeyWindowOpenDelay();
        if (GameDataManager.Instance?.IsChapter01KeyXAcquired == true)
            return;

        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                Mathf.Max(0f, chapter01KeyWindowOpenDelay),
                () =>
                {
                    if (_chapter01KeyWindowOpenDelayTween != delay)
                        return;

                    _chapter01KeyWindowOpenDelayTween = null;
                    OpenChapter01KeyWindow();
                },
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        _chapter01KeyWindowOpenDelayTween = delay;
        delay.OnKill(() =>
        {
            if (_chapter01KeyWindowOpenDelayTween == delay)
                _chapter01KeyWindowOpenDelayTween = null;
        });
    }

    void StopChapter01KeyWindowOpenDelay()
    {
        if (_chapter01KeyWindowOpenDelayTween == null)
            return;

        Tween delay = _chapter01KeyWindowOpenDelayTween;
        _chapter01KeyWindowOpenDelayTween = null;
        delay.Kill(false);
    }

    void OpenChapter01KeyWindow()
    {
        if (!_chapter01KeyRuntimeInitialized)
            InitializeChapter01KeyRuntime();

        if (GameDataManager.Instance?.IsChapter01KeyXAcquired == true
            || chapter01KeyWindow == null
            || chapter01KeyDesktopProcess == null
            || chapter01KeySourceGrid == null)
        {
            return;
        }

        desktopProcessGrid?.Remove(chapter01KeyDesktopProcess);
        folderProcessGrid?.Remove(chapter01KeyDesktopProcess);
        _desktopLayoutUnits.Remove(chapter01KeyDesktopProcess);
        if (!chapter01KeySourceGrid.Contains(chapter01KeyDesktopProcess))
        {
            chapter01KeySourceGrid.ClearRuntimeSlots();
            chapter01KeySourceGrid.TryPlace(
                chapter01KeyDesktopProcess,
                0,
                out _);
        }
        chapter01KeySourceGrid.ApplyLayout();
        chapter01KeyDesktopProcess.gameObject.SetActive(true);

        PositionChapter01KeyWindowBesideLive();
        chapter01KeyWindow.OpenStandalone();
        chapter01KeyWindow.SetAlwaysOnTop(true);
        chapter01KeyWindow.BringToFront();
    }

    void PositionChapter01KeyWindowBesideLive()
    {
        RectTransform windowRect = chapter01KeyWindow?.WindowRect;
        RectTransform parentRect = windowRect != null
            ? windowRect.parent as RectTransform
            : null;
        RectTransform liveRect = characterWindow?.WindowRect;
        if (windowRect == null
            || parentRect == null
            || liveRect == null
            || !TryGetRectInParent(liveRect, parentRect, out Rect liveBounds))
        {
            return;
        }

        Vector2 windowSize = chapter01KeyWindow.RecordedSize;
        chapter01KeyWindow.SetOpeningLayout(
            windowRect.anchoredPosition,
            windowSize);
        Rect visualBounds = windowRect.rect;
        Vector2 visualSize = new Vector2(
            Mathf.Max(0.01f, visualBounds.width),
            Mathf.Max(0.01f, visualBounds.height));
        Vector2 visualOffset = visualBounds.center;

        RectTransform messageRect = linkMessageController?.LinkWindow
            ?.WindowRect;
        Rect messageBounds = default;
        bool hasMessageBounds = messageRect != null
            && messageRect.gameObject.activeInHierarchy
            && TryGetRectInParent(
                messageRect,
                parentRect,
                out messageBounds);
        float gap = Mathf.Max(0f, chapter01KeyWindowGap);
        Vector2 halfSize = visualSize * 0.5f;
        float sideUpperY = liveBounds.yMax
            - halfSize.y
            - gap * 0.5f;
        float sideLowerY = liveBounds.yMin
            + halfSize.y
            + gap * 0.5f;
        float leftAlignedX = liveBounds.xMin + halfSize.x;
        float rightAlignedX = liveBounds.xMax - halfSize.x;
        var candidateCenters = new[]
        {
            new Vector2(
                liveBounds.xMin - gap - halfSize.x,
                sideUpperY),
            new Vector2(
                liveBounds.xMin - gap - halfSize.x,
                liveBounds.center.y),
            new Vector2(
                liveBounds.xMin - gap - halfSize.x,
                sideLowerY),
            new Vector2(leftAlignedX, liveBounds.yMax + gap + halfSize.y),
            new Vector2(
                liveBounds.center.x,
                liveBounds.yMax + gap + halfSize.y),
            new Vector2(rightAlignedX, liveBounds.yMax + gap + halfSize.y),
            new Vector2(
                liveBounds.xMax + gap + halfSize.x,
                sideUpperY),
            new Vector2(
                liveBounds.xMax + gap + halfSize.x,
                liveBounds.center.y),
            new Vector2(
                liveBounds.xMax + gap + halfSize.x,
                sideLowerY),
            new Vector2(leftAlignedX, liveBounds.yMin - gap - halfSize.y),
            new Vector2(
                liveBounds.center.x,
                liveBounds.yMin - gap - halfSize.y),
            new Vector2(rightAlignedX, liveBounds.yMin - gap - halfSize.y)
        };

        float preferredHorizontalSide;
        if (hasMessageBounds
            && !Mathf.Approximately(
                messageBounds.center.x,
                liveBounds.center.x))
        {
            preferredHorizontalSide = Mathf.Sign(
                messageBounds.center.x - liveBounds.center.x);
        }
        else
        {
            float leftSpace = liveBounds.xMin - parentRect.rect.xMin;
            float rightSpace = parentRect.rect.xMax - liveBounds.xMax;
            preferredHorizontalSide = rightSpace >= leftSpace ? 1f : -1f;
        }

        Vector2 selectedPivot = windowRect.anchoredPosition;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < candidateCenters.Length; i++)
        {
            Vector2 unclampedPivot = candidateCenters[i] - visualOffset;
            Vector2 candidatePivot = ClampVisualPivotToRect(
                unclampedPivot,
                visualOffset,
                visualSize,
                parentRect.rect,
                gap);
            Rect candidateBounds = CreateVisualRect(
                candidatePivot,
                visualOffset,
                visualSize);
            float overlapArea = CalculateOverlapArea(
                candidateBounds,
                liveBounds);
            if (hasMessageBounds)
            {
                overlapArea += CalculateOverlapArea(
                    candidateBounds,
                    messageBounds);
            }

            float candidateSide = Mathf.Sign(
                candidateBounds.center.x - liveBounds.center.x);
            float sidePenalty = Mathf.Approximately(candidateSide, 0f)
                || candidateSide != preferredHorizontalSide
                ? 100000f
                : 0f;
            float clampPenalty = (candidatePivot - unclampedPivot)
                .sqrMagnitude * 100f;
            float distanceScore = (candidateBounds.center - liveBounds.center)
                .sqrMagnitude;
            float score = overlapArea * 1000000f
                + sidePenalty
                + clampPenalty
                + distanceScore
                + i * 0.01f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            selectedPivot = candidatePivot;
        }

        Vector2 anchorReference = new Vector2(
            Mathf.Lerp(
                parentRect.rect.xMin,
                parentRect.rect.xMax,
                windowRect.anchorMin.x),
            Mathf.Lerp(
                parentRect.rect.yMin,
                parentRect.rect.yMax,
                windowRect.anchorMin.y));
        chapter01KeyWindow.SetOpeningLayout(
            selectedPivot - anchorReference,
            windowSize);
    }

    static bool TryGetRectInParent(
        RectTransform source,
        RectTransform parent,
        out Rect rect)
    {
        rect = default;
        if (source == null || parent == null)
            return false;

        var corners = new Vector3[4];
        source.GetWorldCorners(corners);
        Vector3 first = parent.InverseTransformPoint(corners[0]);
        float xMin = first.x;
        float xMax = first.x;
        float yMin = first.y;
        float yMax = first.y;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 point = parent.InverseTransformPoint(corners[i]);
            xMin = Mathf.Min(xMin, point.x);
            xMax = Mathf.Max(xMax, point.x);
            yMin = Mathf.Min(yMin, point.y);
            yMax = Mathf.Max(yMax, point.y);
        }

        rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    static Vector2 ClampVisualPivotToRect(
        Vector2 pivot,
        Vector2 visualOffset,
        Vector2 visualSize,
        Rect container,
        float margin)
    {
        Vector2 halfSize = visualSize * 0.5f;
        pivot.x = Mathf.Clamp(
            pivot.x,
            container.xMin + margin - visualOffset.x + halfSize.x,
            container.xMax - margin - visualOffset.x - halfSize.x);
        pivot.y = Mathf.Clamp(
            pivot.y,
            container.yMin + margin - visualOffset.y + halfSize.y,
            container.yMax - margin - visualOffset.y - halfSize.y);
        return pivot;
    }

    static Rect CreateVisualRect(
        Vector2 pivot,
        Vector2 visualOffset,
        Vector2 visualSize)
    {
        Vector2 center = pivot + visualOffset;
        return new Rect(center - visualSize * 0.5f, visualSize);
    }

    static float CalculateOverlapArea(Rect first, Rect second)
    {
        float width = Mathf.Max(
            0f,
            Mathf.Min(first.xMax, second.xMax)
                - Mathf.Max(first.xMin, second.xMin));
        float height = Mathf.Max(
            0f,
            Mathf.Min(first.yMax, second.yMax)
                - Mathf.Max(first.yMin, second.yMin));
        return width * height;
    }

    void FinishChapter01RecordAutomation(bool keepDisplayedPosition)
    {
        StopChapter01RecordAutomationSequence();
        CursorHandle cursor = CursorHandle.Instance;
        if (cursor?.IsScriptControlledBy(this) == true)
        {
            if (keepDisplayedPosition)
                cursor.ReleaseScriptControlAtDisplayedPosition(this);
            else
                cursor.ReleaseScriptControl(this);
        }

        _isChapter01RecordAutomationActive = false;
    }

    void StopChapter01RecordAutomationSequence()
    {
        if (_chapter01RecordAutomationSequence == null)
            return;

        Sequence sequence = _chapter01RecordAutomationSequence;
        _chapter01RecordAutomationSequence = null;
        sequence.Kill(false);
    }

    void CancelChapter01RecordGuidance()
    {
        StopChapter01StateCheckMessageDelay();
        StopChapter01RecordHoverTracking();
        FinishChapter01RecordAutomation(false);
        _isChapter01RecordPromptRequested = false;
        _hasChapter01StateCheckMessageShown = false;
        _hasChapter01RecordHoverMessageShown = false;
    }

    void BindWindows()
    {
        UnbindWindows();
        if (toolWindowController != null)
        {
            toolWindowController.InitializationCompleted -= HandleToolInitializationCompleted;
            toolWindowController.InitializationCompleted += HandleToolInitializationCompleted;
            if (toolWindowController.Decoration != null)
                toolWindowController.Decoration.DecorationCompleted += HandleToolDecorationCompleted;
            if (toolWindowController.ProgramLibrary != null)
            {
                toolWindowController.ProgramLibrary.LibraryOpened += HandleToolProgramLibraryOpened;
                toolWindowController.ProgramLibrary.InstallationHintRequested += HandleToolInstallationHintRequested;
            }
        }
        for (int i = 0; i < windowUnits.Count; i++)
        {
            OSWindowUnit window = windowUnits[i];
            if (window == null)
                continue;

            window.ContentInitializationRequested +=
                HandleWindowContentInitializationRequested;
            window.OpenStarted += HandleWindowOpenStarted;
            window.Closed += HandleWindowClosed;
            window.LayoutChanged += HandleWindowLayoutChanged;
            window.AlwaysOnTopChanged += HandleWindowAlwaysOnTopChanged;
        }

        if (stateCheckWindow != null)
        {
            stateCheckWindow.OpenStarted -=
                HandleStateCheckWindowOpenStarted;
            stateCheckWindow.OpenStarted +=
                HandleStateCheckWindowOpenStarted;
        }

        if (stateCheckWindowController != null)
        {
            stateCheckWindowController.PuzzleCompleted -=
                HandleStateCheckPuzzleCompleted;
            stateCheckWindowController.PuzzleCompleted +=
                HandleStateCheckPuzzleCompleted;
            stateCheckWindowController.ChapterRecordPointerEntered -=
                HandleChapter01RecordPointerEntered;
            stateCheckWindowController.ChapterRecordPointerEntered +=
                HandleChapter01RecordPointerEntered;
            stateCheckWindowController.ChapterRecordPointerExited -=
                HandleChapter01RecordPointerExited;
            stateCheckWindowController.ChapterRecordPointerExited +=
                HandleChapter01RecordPointerExited;
        }

        if (mainStoryGiftWindow != null)
        {
            mainStoryGiftWindow.Closed -=
                HandleMainStoryGiftWindowClosed;
            if (!_hasMainStoryGiftWindowClosed)
            {
                mainStoryGiftWindow.Closed +=
                    HandleMainStoryGiftWindowClosed;
            }
        }

        if (linkMessageController != null)
        {
            linkMessageController.EntryPresentationStarted -=
                HandleLinkMessagePresentationStarted;
            linkMessageController.EntryPresentationStarted +=
                HandleLinkMessagePresentationStarted;
            linkMessageController.EntryPresentationCompleted -=
                HandleLinkMessagePresentationCompleted;
            linkMessageController.EntryPresentationCompleted +=
                HandleLinkMessagePresentationCompleted;
            linkMessageController.EntryTextPlaybackCompleted -=
                HandleLinkMessageTextPlaybackCompleted;
            linkMessageController.EntryTextPlaybackCompleted +=
                HandleLinkMessageTextPlaybackCompleted;
            linkMessageController.OptionSelected -=
                HandleLinkMessageOptionSelected;
            linkMessageController.OptionSelected +=
                HandleLinkMessageOptionSelected;
        }

        if (memoryWindowController == null)
            return;

        memoryWindowController.DataShown += HandleOSMemoryDataShown;
        memoryWindowController.CurrentTextCompleted += HandleOSMemoryTextCompleted;
        memoryWindowController.ActionRequested +=
            HandleOSMemoryActionRequested;
        memoryWindowController.ResourceChangeRequested +=
            HandleOSMemoryResourceChangeRequested;
    }

    void UnbindWindows()
    {
        if (toolWindowController != null)
        {
            toolWindowController.InitializationCompleted -= HandleToolInitializationCompleted;
            if (toolWindowController.ProgramLibrary != null)
            {
                toolWindowController.ProgramLibrary.LibraryOpened -= HandleToolProgramLibraryOpened;
                toolWindowController.ProgramLibrary.InstallationHintRequested -= HandleToolInstallationHintRequested;
            }
            if (toolWindowController.Decoration != null)
                toolWindowController.Decoration.DecorationCompleted -= HandleToolDecorationCompleted;
        }
        if (windowUnits != null)
        {
            for (int i = 0; i < windowUnits.Count; i++)
            {
                OSWindowUnit window = windowUnits[i];
                if (window == null)
                    continue;

                window.ContentInitializationRequested -=
                    HandleWindowContentInitializationRequested;
                window.OpenStarted -= HandleWindowOpenStarted;
                window.Closed -= HandleWindowClosed;
                window.LayoutChanged -= HandleWindowLayoutChanged;
                window.AlwaysOnTopChanged -=
                    HandleWindowAlwaysOnTopChanged;
            }
        }

        if (stateCheckWindow != null)
        {
            stateCheckWindow.OpenStarted -=
                HandleStateCheckWindowOpenStarted;
        }

        if (stateCheckWindowController != null)
        {
            stateCheckWindowController.PuzzleCompleted -=
                HandleStateCheckPuzzleCompleted;
            stateCheckWindowController.ChapterRecordPointerEntered -=
                HandleChapter01RecordPointerEntered;
            stateCheckWindowController.ChapterRecordPointerExited -=
                HandleChapter01RecordPointerExited;
        }

        if (mainStoryGiftWindow != null)
        {
            mainStoryGiftWindow.Closed -=
                HandleMainStoryGiftWindowClosed;
        }

        if (linkMessageController != null)
        {
            linkMessageController.EntryPresentationStarted -=
                HandleLinkMessagePresentationStarted;
            linkMessageController.EntryPresentationCompleted -=
                HandleLinkMessagePresentationCompleted;
            linkMessageController.EntryTextPlaybackCompleted -=
                HandleLinkMessageTextPlaybackCompleted;
            linkMessageController.OptionSelected -=
                HandleLinkMessageOptionSelected;
        }
        UnbindFirstHeartHealEndingTransientEvents();

        if (memoryWindowController == null)
            return;

        memoryWindowController.DataShown -= HandleOSMemoryDataShown;
        memoryWindowController.CurrentTextCompleted -= HandleOSMemoryTextCompleted;
        memoryWindowController.ActionRequested -=
            HandleOSMemoryActionRequested;
        memoryWindowController.ResourceChangeRequested -=
            HandleOSMemoryResourceChangeRequested;
    }

    void HandleWindowContentInitializationRequested(OSWindowUnit window)
    {
        if (window == memoryWindow)
        {
            if (GameDataManager.Instance?.IsChapter01KeyXAcquired == true
                && memoryDesktopProcess != null && !memoryDesktopProcess.IsLocked)
                EnsureChapter01MemoryCursor();
            TryActivateChapter01MemoryRepairSequence();
            memoryWindowController?.InitializeFromSave();
        }
        else if (window == recordWindow)
            recordWindowController?.InitializeFromSave();

        WindowContentInitializationRequested?.Invoke(window);
    }

    void HandleWindowOpenStarted(OSWindowUnit window)
    {
        if (OSSessionPersistence.IsRestoring) return;
        if (window == toolWindow)
            TryShowMemoryUpgrade02ToolMessage();
        if (window != null)
            linkMessageController?.NotifyWindowOpened(window.WindowId);

        RefreshMouseActiveWindowPresentation();
    }

    void HandleWindowClosed(OSWindowUnit window)
    {
        if (OSSessionPersistence.IsRestoring) return;
        if (window == stateCheckWindow)
        {
            StopChapter01RecordHoverTracking();
            TryAdvanceChapter01RecordPromptFromStateCheckClose();
        }

        bool suppressLinkMessage =
            _suppressCharacterWindowClosedLinkMessage
            && window == characterWindow;
        if (window != null && !suppressLinkMessage)
            linkMessageController?.NotifyWindowClosed(window.WindowId);

        NotifyWindowUnavailable(window);
        RefreshMouseActiveWindowPresentation();
    }

    void HandleLinkMessagePresentationCompleted(
        string sequenceId,
        OSCharacterInteractionEntryData entry)
    {
        RefreshEvent01LinkCloseSuppression();
        if (entry == null)
            return;

        if (string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHeal,
                StringComparison.Ordinal)
            && entry.PresentationCueId ==
                OSCharacterInteractionIds.PresentationCues
                    .RestoreEvent01Desktop)
        {
            SetEvent01DesktopTutorialSuppressed(false);
            PlayFolderTutorialHintBurst();
            return;
        }

        if (!string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHealEnding,
                StringComparison.Ordinal))
        {
            return;
        }

        if (entry.PresentationCueId ==
            OSCharacterInteractionIds.PresentationCues
                .HeartHealEndingTransition)
        {
            OpenFirstHeartHealEndingSpecialWindow();
        }
        else if (entry.PresentationCueId ==
                 OSCharacterInteractionIds.PresentationCues
                     .HeartHealEndingCursorTakeover)
        {
            BeginFirstHeartHealCursorTakeover();
        }
    }

    void HandleLinkMessagePresentationStarted(
        string sequenceId,
        OSCharacterInteractionEntryData entry)
    {
        RefreshEvent01LinkCloseSuppression();
        HandleChapter02CollectionPresentationStarted(sequenceId, entry);
        if (sequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair
            && entry != null && entry.EntryId == OSCharacterInteractionIds.Chapter01Tool.FilesFound)
            characterWindowController?.PlayMemoryFilesFoundShake();
        if (sequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair
            && entry != null && entry.EntryId == OSCharacterInteractionIds.Chapter01Tool.DecorationPrompt)
            GameDataManager.Instance?.MarkToolDecorationOffered();
        bool isChapter01KeySearch = entry != null
            && string.Equals(
                sequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal)
            && string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Chapter01Entries.FindKey,
                StringComparison.Ordinal);
        if (isChapter01KeySearch)
        {
            ScheduleChapter01KeyWindowOpen();
            return;
        }

        bool isChapter01RecordTimeout = entry != null
            && string.Equals(
                sequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal)
            && string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Chapter01Entries
                    .RecordHoverTimedOut,
                StringComparison.Ordinal);
        if (isChapter01RecordTimeout)
        {
            BeginChapter01RecordCursorTakeover();
            return;
        }

        if (!_isFirstHeartHealEndingActive
            || entry == null
            || !string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHealEnding,
                StringComparison.Ordinal)
            || !string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Event01EndingEntries.CheckNormal,
                StringComparison.Ordinal))
        {
            return;
        }

        PlayFirstHeartHealWindowInspection();
    }

    void HandleLinkMessageTextPlaybackCompleted(
        string sequenceId,
        OSCharacterInteractionEntryData entry)
    {
        if (entry == null)
            return;

        if (sequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair
            && entry.EntryId == OSCharacterInteractionIds.Chapter01Tool.FollowUp
            && toolWindowController?.ProgramLibrary != null
            && toolWindowController.ProgramLibrary.HasUnpackedThisSession)
        {
            HandleToolProgramLibraryOpened();
            return;
        }

        bool isChapter01RecordTimeout = string.Equals(
                sequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal)
            && string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Chapter01Entries
                    .RecordHoverTimedOut,
                StringComparison.Ordinal);
        if (isChapter01RecordTimeout)
        {
            ScheduleChapter01RecordAutoClick();
            return;
        }

        bool isChapter01EntryMessage = string.Equals(
                sequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal)
            && (string.Equals(
                    entry.EntryId,
                    OSCharacterInteractionIds.Chapter01Entries
                        .LiveEntryNormal,
                    StringComparison.Ordinal)
                || string.Equals(
                    entry.EntryId,
                    OSCharacterInteractionIds.Chapter01Entries
                        .LiveEntryForced,
                    StringComparison.Ordinal));
        if (isChapter01EntryMessage)
        {
            linkMessageController?.NotifyExternalSignal(
                OSCharacterInteractionIds.Chapter01Signals
                    .LiveEntryConfirm);
            return;
        }

        if (!_isFirstHeartHealEndingActive
            || !string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHealEnding,
                StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Event01EndingEntries.KeyRecovered,
                StringComparison.Ordinal))
        {
            ScheduleFirstHeartHealTakeKeySelection();
        }
        else if (string.Equals(
                     entry.EntryId,
                     OSCharacterInteractionIds.Event01EndingEntries
                         .CheckNormal,
                     StringComparison.Ordinal))
        {
            linkMessageController?.StartDisplayedTrailingDotsLoop(
                sequenceId,
                entry.EntryId,
                HeartHealEndingInspectionDotsInterval);
        }
        else if (string.Equals(
                     entry.EntryId,
                     OSCharacterInteractionIds.Event01EndingEntries
                         .CheckResult,
                     StringComparison.Ordinal))
        {
            ScheduleFirstHeartHealCheckResultSelection();
        }
        else if (string.Equals(
                     entry.EntryId,
                     OSCharacterInteractionIds.Event01EndingEntries
                         .PostRelock,
                     StringComparison.Ordinal))
        {
            ScheduleFirstHeartHealReturnMessage();
        }
    }

    void HandleLinkMessageOptionSelected(
        string sequenceId,
        OSCharacterInteractionEntryData entry,
        OSCharacterInteractionOptionData option)
    {
        if (entry == null || option == null)
            return;

        if (RecordChapter02ReadAlongAnswer(sequenceId, entry, option))
            return;

        if (sequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair
            && entry.EntryId == OSCharacterInteractionIds.Chapter01Tool.DecorationPrompt)
        {
            GameDataManager.Instance?.ChooseToolDecoration(option.OptionId == OSCharacterInteractionIds.Chapter01Tool.Decorate);
            return;
        }

        bool confirmedChapter01Entry = string.Equals(
                sequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal)
            && string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Chapter01Entries
                    .LiveEntryConfirm,
                StringComparison.Ordinal)
            && string.Equals(
                option.OptionId,
                OSCharacterInteractionIds.Chapter01Options.Confirm,
                StringComparison.Ordinal);
        if (confirmedChapter01Entry)
        {
            RunChapter01EntryConfirmedOnce();
            return;
        }

        bool confirmedChapter01RecordStart = string.Equals(
                sequenceId,
                OSCharacterInteractionIds.Chapter01LiveReconnect,
                StringComparison.Ordinal)
            && string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Chapter01Entries
                    .RecordStartPrompt,
                StringComparison.Ordinal)
            && string.Equals(
                option.OptionId,
                OSCharacterInteractionIds.Chapter01Options.StartRepair,
                StringComparison.Ordinal);
        if (confirmedChapter01RecordStart)
        {
            BeginChapter01KeySearchFromPrompt();
            CloseStateCheckForChapter01RecordPrompt();
            return;
        }

        if (!_isFirstHeartHealEndingActive
            || !string.Equals(
                sequenceId,
                OSCharacterInteractionIds.PrologueEvent01HeartHealEnding,
                StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(
                entry.EntryId,
                OSCharacterInteractionIds.Event01EndingEntries.KeyRecovered,
                StringComparison.Ordinal)
            && string.Equals(
                option.OptionId,
                HeartHealEndingTakeKeyOptionId,
                StringComparison.Ordinal))
        {
            HandleFirstHeartHealTakeKeyOptionSelected();
        }
        else if (string.Equals(
                     entry.EntryId,
                     OSCharacterInteractionIds.Event01EndingEntries
                         .CheckResult,
                     StringComparison.Ordinal)
                 && string.Equals(
                     option.OptionId,
                     HeartHealEndingKeepSafeOptionId,
                     StringComparison.Ordinal))
        {
            HandleFirstHeartHealCheckResultOptionSelected();
        }
        else if (string.Equals(
                     entry.EntryId,
                     OSCharacterInteractionIds.Event01EndingEntries.Return,
                     StringComparison.Ordinal)
                 && string.Equals(
                     option.OptionId,
                     HeartHealEndingCloseOSOptionId,
                     StringComparison.Ordinal))
        {
            HandleFirstHeartHealReturnOptionSelected();
        }
    }

    void HandleMainStoryGiftWindowClosed(OSWindowUnit source)
    {
        if (source != mainStoryGiftWindow)
            return;

        RunMainStoryGiftWindowClosedOnce();
    }

    public void RunMainStoryGiftWindowClosedOnce()
    {
        if (_hasMainStoryGiftWindowClosed)
            return;

        _hasMainStoryGiftWindowClosed = true;
        if (mainStoryGiftWindow != null)
        {
            mainStoryGiftWindow.Closed -=
                HandleMainStoryGiftWindowClosed;
        }

        SetMainStoryGiftInteraction(false);
        mainStoryGiftWindowController?.HandleWindowClosedOnce();
        if (mainStoryGiftDesktopProcess != null)
            mainStoryGiftDesktopProcess.gameObject.SetActive(false);

        GrantMainStoryGiftRecord();

        MainStoryGiftWindowClosedOnce?.Invoke();
    }

    void GrantMainStoryGiftRecord()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null
            || !dataManager.UnlockRecordData(MainStoryGiftRecordIndex))
        {
            return;
        }

        if (recordWindow != null && recordWindow.IsOpen)
            recordWindowController?.InitializeFromSave();

        if (!dataManager.TryGetRecordData(
                MainStoryGiftRecordIndex,
                out MrTGame.OS.RecordData recordData))
        {
            return;
        }

        string displayName = dataManager
            .GetLocalizedRecordName(recordData)
            ?.Trim();
        if (!string.IsNullOrEmpty(displayName)
            && displayName.Length >= 2
            && displayName[0] == '"'
            && displayName[displayName.Length - 1] == '"')
        {
            displayName = displayName.Substring(1, displayName.Length - 2);
        }

        if (!string.IsNullOrWhiteSpace(displayName))
            AdditionHandle.Instance?.ShowItemAcquiredTip(displayName);
    }

    void RunChapter01EntryConfirmedOnce()
    {
        SetChapter01LiveIntroProtected(false);
        if (_hasChapter01EntryConfirmed)
            return;

        _hasChapter01EntryConfirmed = true;
        CancelChapter01RecordGuidance();
        _hasChapter01StateCheckMessageShown = false;
        _hasChapter01RecordHoverMessageShown = false;
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null
            && dataManager.CurrentChapterIndex < 1)
        {
            dataManager.SetCurrentChapterIndex(1);
            dataManager.SetCurrentOSMemoryData(
                OSMemoryProcessSaveData.InvalidDataIndex);
        }

        MarkStateCheckDesktopProcessNew();
        if (stateCheckWindow != null && stateCheckWindow.IsOpen)
        {
            stateCheckWindowController?.RefreshFromSave();
            QueueChapter01StateCheckMessage();
        }

    }

    public void CaptureWindowLayouts()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null || windowUnits == null)
            return;

        for (int i = 0; i < windowUnits.Count; i++)
        {
            OSWindowUnit window = windowUnits[i];
            if (window == null)
                continue;

            dataManager.SetOSWindowLayout(window.CaptureLayout());
        }

        characterWindowController?.CaptureLinkMessagePosition();
        CaptureAlwaysOnTopWindowStack(dataManager);
    }

    public void RestoreWindowLayouts()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null || windowUnits == null)
            return;

        bool wasRestoring = _isRestoringWindowLayouts;
        _isRestoringWindowLayouts = true;
        try
        {
            for (int i = 0; i < windowUnits.Count; i++)
            {
                OSWindowUnit window = windowUnits[i];
                if (window != null
                    && dataManager.TryGetOSWindowLayout(
                        window.WindowId,
                        out OSWindowLayoutSaveData layout))
                {
                    window.ApplyLayout(layout);
                }
            }

            characterWindowController?.RestoreLinkMessagePosition();
            RestoreAlwaysOnTopWindowStack(
                dataManager.OSAlwaysOnTopWindowStack);
            desktopProcessGrid?.NormalizeSlotsToCapacity();
            folderProcessGrid?.NormalizeSlotsToCapacity();
            desktopProcessGrid?.ApplyLayout();
            folderProcessGrid?.ApplyLayout();
            CaptureDesktopProcessLayouts();
        }
        finally
        {
            _isRestoringWindowLayouts = wasRestoring;
        }
    }

    void HandleWindowLayoutChanged(OSWindowUnit window)
    {
        if (window == null || _isRestoringWindowLayouts)
            return;

        if (window == _mouseActiveWindow && !IsWindowMouseFocusable(window))
            SetMouseActiveWindow(null);
        else
            RefreshMouseActiveWindowPresentation();

        if (window == folderWindow)
        {
            folderProcessGrid?.NormalizeSlotsToCapacity();
            folderProcessGrid?.ApplyLayout();
            CaptureDesktopProcessLayouts();
        }

        CaptureWindowLayouts();
    }

    void HandleWindowAlwaysOnTopChanged(
        OSWindowUnit window,
        bool isAlwaysOnTop)
    {
        if (window == null || _isRestoringWindowLayouts)
            return;

        RefreshMouseActiveWindowPresentation();

        CaptureWindowLayouts();
    }

    void CaptureAlwaysOnTopWindowStack(GameDataManager dataManager)
    {
        var stack = new List<OSWindowUnit>();
        for (int i = 0; i < windowUnits.Count; i++)
        {
            OSWindowUnit window = windowUnits[i];
            if (window != null && window.IsAlwaysOnTop)
                stack.Add(window);
        }

        stack.Sort((left, right) => right.transform.GetSiblingIndex()
            .CompareTo(left.transform.GetSiblingIndex()));

        var windowIds = new List<string>(stack.Count);
        for (int i = 0; i < stack.Count; i++)
            windowIds.Add(stack[i].WindowId);

        dataManager.SetOSAlwaysOnTopWindowStack(windowIds);
    }

    void RestoreAlwaysOnTopWindowStack(
        IReadOnlyList<string> savedStack)
    {
        for (int i = 0; i < windowUnits.Count; i++)
            windowUnits[i]?.SetAlwaysOnTop(false);

        if (savedStack == null)
            return;

        var restoredIds = new HashSet<string>();
        for (int i = savedStack.Count - 1; i >= 0; i--)
        {
            string windowId = savedStack[i];
            if (string.IsNullOrWhiteSpace(windowId)
                || !restoredIds.Add(windowId))
            {
                continue;
            }

            OSWindowUnit window = windowUnits.Find(candidate =>
                candidate != null && candidate.WindowId == windowId);
            if (window == null)
                continue;

            window.SetAlwaysOnTop(true);
            window.transform.SetAsLastSibling();
        }
    }

    void HandleOSMemoryDataShown(OSMemoryReadData data)
    {
        if (data?.DataIndex == OSMemoryData.Chapter01Upgrade02DataIndex)
            toolWindowController?.ProgramLibrary?.RefreshPrograms();
        OSMemoryDataShown?.Invoke(data);
        // Restoring an already rendered node can recover an unconsumed signal.
        if (memoryWindowController != null && memoryWindowController.IsCurrentTextComplete)
            HandleOSMemoryTextCompleted(data);
    }

    void HandleOSMemoryTextCompleted(OSMemoryReadData data)
    {
        if (data != null && TryActivateChapter01MemoryRepairSequence())
            linkMessageController.TryNotifyExternalSignal("memory.text_completed." + data.DataIndex);
        if (data != null)
            QueueChapter02MemoryMessage(data.DataIndex);
        if (data != null)
            QueueChapter02ReadAlongMessage(data.DataIndex);
    }

    void HandleOSMemoryActionRequested(OSMemoryActionData action)
    {
        OSMemoryActionRequested?.Invoke(action);
    }

    void HandleOSMemoryResourceChangeRequested(
        OSMemoryResourceChange change)
    {
        OSMemoryResourceChangeRequested?.Invoke(change);
    }

    public void SetAdditionBackgroundSuppressed(bool isSuppressed)
    {
        AdditionHandle addition = AdditionHandle.Instance;
        if (isSuppressed)
        {
            if (_isAdditionBKSuppressed
                || addition == null
                || addition.BK == null)
            {
                return;
            }

            _additionBKWasActive = addition.BK.activeSelf;
            _isAdditionBKSuppressed = true;
            addition.SetBKActive(false);
            return;
        }

        if (!_isAdditionBKSuppressed)
            return;

        bool restoreActive = _additionBKWasActive;
        _isAdditionBKSuppressed = false;
        _additionBKWasActive = false;
        addition?.SetBKActive(restoreActive);
    }

    void Reset()
    {
        ResolveReferences();
        InitializeLocalizedTexts();
        InitializeResourceViews();
    }
}
