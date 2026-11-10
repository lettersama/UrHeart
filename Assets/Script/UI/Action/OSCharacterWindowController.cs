using System;
using System.Collections.Generic;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Sprites;
using UnityEngine.UI;

public enum OSCharacterWindowState
{
    VoidMessage = 0,
    Anima = 1,
    WaitClick = 2
}

public enum OSCharacterExtendedAnimation
{
    None = 0,
    Expend00 = 1,
    Expend01 = 2,
    Expend02 = 3,
    OpenLive = 4,
    Nervous = 5,
    Sad = 6,
    Breakdown00 = 7,
    Breakdown01 = 8,
    Breakdown02 = 9,
    Angry00 = 10,
    Angry01 = 11,
    MagnifyingGlass00 = 12,
    MagnifyingGlass01 = 13
}

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed partial class OSCharacterWindowController : MonoBehaviour,
    IPointerClickHandler
{
    const string CameraSignalShaderName = "UI/OS/CameraSignalIn";
    const string LinkMessagePositionStateId = "character.link_message_position";
    const string ResourceAnimationPrefix = "Base Layer.Player_UI_State_";

    static readonly string[] ExtendedAnimationNames =
    {
        "", "Expend00", "Expend01", "Expend02", "OpenLive",
        "Nervous", "Sad", "Breakdown00", "Breakdown01", "Breakdown02",
        "Angry00", "Angry01", "MagnifyingGlass00", "MagnifyingGlass01"
    };
    static readonly int[] ExtendedAnimationParameterIds =
    {
        0,
        Animator.StringToHash("Expend00"),
        Animator.StringToHash("Expend01"),
        Animator.StringToHash("Expend02"),
        Animator.StringToHash("OpenLive"),
        Animator.StringToHash("Nervous"),
        Animator.StringToHash("Sad"),
        Animator.StringToHash("Breakdown00"),
        Animator.StringToHash("Breakdown01"),
        Animator.StringToHash("Breakdown02"),
        Animator.StringToHash("Angry00"),
        Animator.StringToHash("Angry01"),
        Animator.StringToHash("MagnifyingGlass00"),
        Animator.StringToHash("MagnifyingGlass01")
    };

    static readonly int CameraSignalProgressId =
        Shader.PropertyToID("_Progress");
    static readonly int CameraSignalSpriteUvRectId =
        Shader.PropertyToID("_SpriteUvRect");
    static readonly int CameraSignalRectSizeId =
        Shader.PropertyToID("_RectSize");

    [Header("Display State")]
    [SerializeField]
    OSCharacterWindowState initialState =
        OSCharacterWindowState.VoidMessage;
    [SerializeField] GameObject voidMessageState;
    [SerializeField] GameObject animaState;
    [SerializeField] GameObject waitClickState;

    [Header("Chapter 01 Reconnect")]
    [SerializeField] TMP_Text waitClickText;
    [SerializeField] RectTransform waitClickRaycastTarget;
    [SerializeField] Image waitClickIndicator;
    [SerializeField] OSNoSignalGlitch waitClickGlitch;
    [SerializeField] RectTransform chapter01ShakeTarget;
    [SerializeField] OSLinkMessageController linkMessageController;
    [SerializeField]
    string waitClickLocalizationKey =
        "ui.os.character.wait_click";
    [SerializeField] string waitClickFallbackText = "点击重连";
    [SerializeField, Min(0f)] float firstConcernDelay = 5f;
    [SerializeField, Min(0f)] float secondConcernDelay = 5f;
    [SerializeField, Min(0f)] float forcedEntryDelay = 6f;
    [SerializeField, Min(0.05f)] float waitClickDotInterval = 0.45f;
    [SerializeField, Range(0f, 1f)] float indicatorDimAlpha = 0.25f;
    [SerializeField, Min(0.05f)] float indicatorSlowPulseDuration = 0.9f;
    [SerializeField, Min(0.05f)] float indicatorFastPulseDuration = 0.22f;
    [SerializeField, Min(0.05f)] float concernShakeDuration = 0.5f;
    [SerializeField, Min(0f)] float concernShakeStrength = 3f;
    [SerializeField, Min(0.05f)] float forcedShakeDuration = 0.75f;
    [SerializeField, Min(0f)] float forcedShakeStrength = 7f;

    [Header("Chapter 01 Entry")]
    [SerializeField] Image characterImage;
    [SerializeField] Shader cameraSignalShader;
    [SerializeField, Min(0.05f)] float cameraSignalInDuration = 1.6f;
    [SerializeField, Min(0f)] float chapter01EntryMessageDelay = 0.3f;
    [SerializeField, Min(0f)] float chapter01EntryMessageAdvance = 0.3f;
    [SerializeField] string chapter01EntryAnimationTrigger;

    [Header("Live Status Indicator")]
    [SerializeField] Image animaStatusIndicator;

    [Header("Animation")]
    [SerializeField] Animator characterAnimator;
    [SerializeField] string[] sendAnimationTriggers = new string[0];
    [SerializeField] string reconnectAnimationTrigger;

    [Header("Resource Animation")]
    [SerializeField] bool followResourceAnimation;
    [Tooltip("进入资源状态时，选择 01/02 循环的总概率；未命中则播放 00。")]
    [SerializeField, Range(0f, 1f)] float resourceAnimationVariantChance = 0.2f;

    [Header("Resource Warnings")]
    [SerializeField] RectTransform resourceWarnings;
    [SerializeField] UnityEngine.UI.Image lowBatteryWarning;
    [SerializeField] UnityEngine.UI.Image lowHeartWarning;
    [SerializeField] UnityEngine.UI.Image highFaultWarning;

    [Header("Live Typing Title")]
    [SerializeField] OSLocalizedText topNameLocalizedText;
    [SerializeField] TMP_Text topNameText;
    [SerializeField]
    string typingLocalizationKey =
        "ui.os.character.live.typing";
    [SerializeField] string typingFallbackText = "Live 输入中";
    [SerializeField, Min(0.05f)] float typingDotInterval = 0.24f;

    [Header("Live Typing Voice")]
    [SerializeField] string typingVoiceSfxPrefix = "Character_00_";

    [Header("Live Message Position")]
    [SerializeField] OSLinkMessageWindowLayout linkMessageLayout;
    [SerializeField] bool hasCustomLinkMessagePosition;
    [SerializeField] Vector2 linkMessagePosition;

    OSWindowUnit characterWindow;
    string requestedStateName;
    float requestedTransitionDuration;
    int requestedLayer;
    bool hasAnimationStateRequest;
    bool stateAppliedWhileEnabled;
    bool hasManualAnimationState;
    string currentResourceAnimationGroup;
    OSCharacterExtendedAnimation currentExtendedAnimation;
    OSCharacterExtendedAnimation activityAnimation;
    bool ownsActivityAnimation;
    bool cameraSignalOwnsAnimation;
    bool isSignalInActive;
    bool isSignalOutActive;
    bool extendedAnimationParametersPending;
    Animator extendedAnimationParameterAnimator;
    RuntimeAnimatorController extendedAnimationParameterController;
    int extendedAnimationParameterMask;
    bool isTransitioning;
    bool isLinkMessageTyping;
    bool isChapter01ReconnectActive;
    string localizedTypingText;
    string localizedWaitClickText;
    int waitClickDotCount;
    Sequence typingDotsSequence;
    Sequence chapter01ReconnectSequence;
    Tween chapter01EntryDelayTween;
    Sequence waitClickDotsSequence;
    Tween waitClickIndicatorTween;
    Tween animaStatusIndicatorTween;
    Tween chapter01ShakeTween;
    AudioSource typingVoiceSource;
    AudioClip typingVoiceClip;
    AudioData[] typingVoiceSfx;
    AudioData[] activeTypingVoiceSfx;
    int lastTypingVoiceIndex = -1;
    bool typingVoiceSfxResolved;
    Color waitClickIndicatorAuthoredColor = Color.white;
    bool waitClickIndicatorColorCached;
    Color animaStatusIndicatorAuthoredColor = Color.white;
    bool animaStatusIndicatorColorCached;
    Material originalCharacterMaterial;
    Material cameraSignalRuntimeMaterial;
    Tween cameraSignalTween;
    bool missingCameraSignalShaderWasLogged;

    public OSCharacterWindowState CurrentState { get; private set; }
    public bool IsTransitioning => isTransitioning;
    public bool IsSignalInActive => isSignalInActive;
    public bool IsSignalOutActive => isSignalOutActive;
    public bool IsChapter01ReconnectActive => isChapter01ReconnectActive;
    public bool HasCustomLinkMessagePosition => hasCustomLinkMessagePosition;
    public Vector2 LinkMessagePosition => linkMessagePosition;

    public Animator CharacterAnimator => characterAnimator;
    public bool HasAnimator => characterAnimator != null;
    public bool HasPendingAnimationState =>
        (hasAnimationStateRequest && !stateAppliedWhileEnabled)
        || (currentExtendedAnimation != OSCharacterExtendedAnimation.None
            && extendedAnimationParametersPending);
    public OSCharacterExtendedAnimation CurrentExtendedAnimation =>
        currentExtendedAnimation;

    public event Action Chapter01NormalEntryRequested;
    public event Action Chapter01ForcedEntryRequested;

    void Awake()
    {
        ResolveReferences();
        ApplyStateImmediate(initialState);
    }

    void OnEnable()
    {
        GameEvents.ResourceChanged += OnAnimationResourceChanged;
        stateAppliedWhileEnabled = false;
        ResolveReferences();
        RestoreLinkMessagePosition();
        ApplyCharacterAnimation();
        RefreshResourceWarnings();
    }

    void Start()
    {
        if (!stateAppliedWhileEnabled)
            ApplyCharacterAnimation();
        RefreshResourceWarnings();
    }

    void Update()
    {
        SynchronizeExtendedAnimation();
        CompleteExtendedOneShot();
        ApplyStoryAnimation();
        ApplyActivityAnimation();
        TickPortraitPoseTransition(Time.unscaledDeltaTime);
        UpdateTypingVoicePlayback();
    }

    void LateUpdate()
    {
        // Animator swaps the Image sprite after Update. Refresh its atlas bounds
        // afterwards so a masked signal frame never samples the previous sprite.
        if (cameraSignalRuntimeMaterial != null) ConfigureCameraSignalMaterial();
    }

    void OnDisable()
    {
        bool wasSignalOutActive = isSignalOutActive;
        GameEvents.ResourceChanged -= OnAnimationResourceChanged;
        CancelChapter01Reconnect();
        StopChapter01EntryTransition();
        if (wasSignalOutActive)
            ApplyStateImmediate(OSCharacterWindowState.VoidMessage);
        SetActivityAnimation(OSCharacterExtendedAnimation.None);
        if (currentExtendedAnimation == OSCharacterExtendedAnimation.Expend00)
            PlayResourceAnimation();
        EndLinkMessageTyping();
        StopAnimaStatusIndicatorPulse(true);
        stateAppliedWhileEnabled = false;
        extendedAnimationParametersPending |=
            currentExtendedAnimation != OSCharacterExtendedAnimation.None;
    }

    void OnDestroy()
    {
        CancelChapter01Reconnect();
        StopChapter01EntryTransition();
        KillTypingDots();
        StopAnimaStatusIndicatorPulse(true);
    }

    public void SetLinkMessagePosition(Vector2 position, bool save = true)
    {
        if (!IsValidLinkMessagePosition(position))
            return;

        hasCustomLinkMessagePosition = true;
        linkMessagePosition = position;
        if (save)
            SaveLinkMessagePosition();
    }

    public void CaptureLinkMessagePosition()
    {
        ResolveReferences();
        if (linkMessageLayout == null)
            return;

        linkMessageLayout.CaptureDraggedPosition();
        SaveLinkMessagePosition();
    }

    void SaveLinkMessagePosition()
    {
        if (!hasCustomLinkMessagePosition || !IsValidLinkMessagePosition(linkMessagePosition))
            return;

        if (characterWindow == null)
            characterWindow = GetComponent<OSWindowUnit>();

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null || characterWindow == null)
            return;

        dataManager.SetOSWindowLayout(characterWindow.CaptureLayout());
        dataManager.SetOSWindowContentState(
            characterWindow.WindowId,
            LinkMessagePositionStateId,
            OSWindowLayoutSaveData.InvalidContentContextIndex,
            0f,
            0,
            linkMessagePosition);
    }

    public void RestoreLinkMessagePosition()
    {
        ResolveReferences();
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null || linkMessageLayout == null || characterWindow == null)
            return;

        hasCustomLinkMessagePosition = dataManager.TryGetOSWindowContentState(
                characterWindow.WindowId, out OSWindowLayoutSaveData state)
            && state.contentStateId == LinkMessagePositionStateId
            && IsValidLinkMessagePosition(state.contentPosition);
        linkMessagePosition = hasCustomLinkMessagePosition ? state.contentPosition : Vector2.zero;
        linkMessageLayout.RestorePosition();
    }

    static bool IsValidLinkMessagePosition(Vector2 position)
    {
        return !float.IsNaN(position.x) && !float.IsInfinity(position.x)
            && !float.IsNaN(position.y) && !float.IsInfinity(position.y);
    }

    public void SetAnimationState(
        string stateName,
        float transitionDuration = 0f,
        int layer = 0)
    {
        if (string.IsNullOrWhiteSpace(stateName) || layer < 0)
            return;

        if (layer == 0)
        {
            for (int i = 1; i < ExtendedAnimationNames.Length; i++)
            {
                if (stateName == ResourceAnimationPrefix + ExtendedAnimationNames[i]
                    || stateName == "Player_UI_State_" + ExtendedAnimationNames[i])
                {
                    SetExtendedAnimation((OSCharacterExtendedAnimation)i);
                    return;
                }
            }
        }

        ClearExtendedAnimation();
        requestedStateName = stateName;
        requestedTransitionDuration = Mathf.Max(0f, transitionDuration);
        requestedLayer = layer;
        hasAnimationStateRequest = true;
        hasManualAnimationState = true;
        stateAppliedWhileEnabled = TryApplyAnimationState();
    }

    /// <summary>
    /// 互斥开启扩展动作；仅 Expend00 播放一次后归还资源。
    /// MagnifyingGlass00 完成后转入 MagnifyingGlass01，其余保持到程序释放。重复开启不会重播。
    /// </summary>
    public bool SetExtendedAnimation(OSCharacterExtendedAnimation animation)
    {
        int index = (int)animation;
        if (index < 0 || index >= ExtendedAnimationNames.Length)
            return false;
        if (animation == OSCharacterExtendedAnimation.None)
            return StopExtendedAnimation();

        ResolveReferences();
        CacheExtendedAnimationParameters();
        if ((extendedAnimationParameterMask & (1 << index)) == 0)
            return false;

        OSCharacterExtendedAnimation previousAnimation = currentExtendedAnimation;
        ownsActivityAnimation = false;
        currentExtendedAnimation = animation;
        ClearAnimationStateRequest();
        extendedAnimationParametersPending = true;
        ApplyExtendedAnimationParameters();
        if (previousAnimation != animation) PlayPortraitPoseTransition(previousAnimation, animation);
        return true;
    }

    public bool StopExtendedAnimation()
    {
        return PlayResourceAnimation();
    }

    /// <summary>由当前可见业务派发工作/思索循环，不抢占开机和显式扩展动作。</summary>
    public void SetActivityAnimation(OSCharacterExtendedAnimation animation)
    {
        if (animation != OSCharacterExtendedAnimation.None
            && animation != OSCharacterExtendedAnimation.Expend01
            && animation != OSCharacterExtendedAnimation.Expend02)
            return;

        activityAnimation = animation;
        ApplyActivityAnimation();
    }

    void ApplyActivityAnimation()
    {
        if (activityAnimation == OSCharacterExtendedAnimation.None)
        {
            if (ownsActivityAnimation)
                PlayResourceAnimation();
            return;
        }

        if (!isActiveAndEnabled || CurrentState != OSCharacterWindowState.Anima
            || isTransitioning || cameraSignalOwnsAnimation || hasManualAnimationState
            || storyAnimationOwner != null)
            return;
        if (currentExtendedAnimation != OSCharacterExtendedAnimation.None
            && !ownsActivityAnimation)
            return;
        if (ownsActivityAnimation && currentExtendedAnimation == activityAnimation)
            return;

        if (SetExtendedAnimation(activityAnimation))
            ownsActivityAnimation = true;
    }

    /// <summary>健康且空闲时的小动作；完成后重新读取资源状态，不恢复过期的待机状态。</summary>
    public bool TryPlayHairBrush()
    {
        if (!isActiveAndEnabled || CurrentState != OSCharacterWindowState.Anima
            || isTransitioning || isLinkMessageTyping || hasManualAnimationState
            || storyAnimationOwner != null
            || activityAnimation != OSCharacterExtendedAnimation.None
            || currentExtendedAnimation != OSCharacterExtendedAnimation.None
            || characterAnimator == null || !characterAnimator.isActiveAndEnabled
            || !TryGetResourceAnimationGroup(out string group) || group != "Normal")
            return false;

        return SetExtendedAnimation(OSCharacterExtendedAnimation.Expend00);
    }

    void CompleteExtendedOneShot()
    {
        if ((currentExtendedAnimation != OSCharacterExtendedAnimation.Expend00
                && currentExtendedAnimation != OSCharacterExtendedAnimation.MagnifyingGlass00)
            || characterAnimator == null || !characterAnimator.isActiveAndEnabled
            || characterAnimator.runtimeAnimatorController == null)
            return;

        AnimatorStateInfo state = characterAnimator.GetCurrentAnimatorStateInfo(0);
        string stateName = ResourceAnimationPrefix
            + ExtendedAnimationNames[(int)currentExtendedAnimation];
        if (!state.IsName(stateName) || state.normalizedTime < 1f)
            return;

        if (currentExtendedAnimation == OSCharacterExtendedAnimation.Expend00)
            PlayResourceAnimation();
        else
        {
            if (storyAnimationOwner != null
                && storyAnimation == OSCharacterExtendedAnimation.MagnifyingGlass00)
                storyAnimation = OSCharacterExtendedAnimation.MagnifyingGlass01;
            SetExtendedAnimation(OSCharacterExtendedAnimation.MagnifyingGlass01);
        }
    }

    void ClearAnimationStateRequest()
    {
        hasManualAnimationState = false;
        currentResourceAnimationGroup = null;
        hasAnimationStateRequest = false;
        stateAppliedWhileEnabled = false;
    }

    void ClearExtendedAnimation()
    {
        ownsActivityAnimation = false;
        ResolveReferences();
        CacheExtendedAnimationParameters();
        currentExtendedAnimation = OSCharacterExtendedAnimation.None;
        extendedAnimationParametersPending = true;
        ApplyExtendedAnimationParameters();
    }

    void CacheExtendedAnimationParameters()
    {
        RuntimeAnimatorController controller = characterAnimator != null
            ? characterAnimator.runtimeAnimatorController : null;
        if (extendedAnimationParameterAnimator == characterAnimator
            && extendedAnimationParameterController == controller)
            return;

        extendedAnimationParameterAnimator = characterAnimator;
        extendedAnimationParameterController = controller;
        extendedAnimationParameterMask = 0;
        extendedAnimationParametersPending |=
            currentExtendedAnimation != OSCharacterExtendedAnimation.None;
        if (controller == null)
            return;

        // parameters 会生成数组，只在 Animator / Controller 更换时读取。
        foreach (AnimatorControllerParameter parameter in characterAnimator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Bool)
                continue;
            for (int i = 1; i < ExtendedAnimationParameterIds.Length; i++)
            {
                if (parameter.nameHash == ExtendedAnimationParameterIds[i])
                    extendedAnimationParameterMask |= 1 << i;
            }
        }
    }

    void ApplyExtendedAnimationParameters()
    {
        if (characterAnimator == null
            || !characterAnimator.isActiveAndEnabled
            || extendedAnimationParameterController == null)
            return;

        int activeIndex = (int)currentExtendedAnimation;
        if (activeIndex != 0
            && (extendedAnimationParameterMask & (1 << activeIndex)) == 0)
            return;

        for (int i = 1; i < ExtendedAnimationParameterIds.Length; i++)
        {
            if ((extendedAnimationParameterMask & (1 << i)) != 0)
                characterAnimator.SetBool(ExtendedAnimationParameterIds[i], i == activeIndex);
        }
        extendedAnimationParametersPending = false;
    }

    bool SynchronizeExtendedAnimation()
    {
        CacheExtendedAnimationParameters();
        if (extendedAnimationParametersPending)
            ApplyExtendedAnimationParameters();
        if (characterAnimator == null || !characterAnimator.isActiveAndEnabled)
        {
            extendedAnimationParametersPending |=
                currentExtendedAnimation != OSCharacterExtendedAnimation.None;
            return currentExtendedAnimation != OSCharacterExtendedAnimation.None;
        }
        if (extendedAnimationParametersPending)
            return currentExtendedAnimation != OSCharacterExtendedAnimation.None;

        OSCharacterExtendedAnimation active = OSCharacterExtendedAnimation.None;
        for (int i = 1; i < ExtendedAnimationParameterIds.Length; i++)
        {
            if ((extendedAnimationParameterMask & (1 << i)) != 0
                && characterAnimator.GetBool(ExtendedAnimationParameterIds[i]))
            {
                active = (OSCharacterExtendedAnimation)i;
                break;
            }
        }
        if (active != currentExtendedAnimation)
        {
            ownsActivityAnimation = false;
            bool wasExtended = currentExtendedAnimation != OSCharacterExtendedAnimation.None;
            currentExtendedAnimation = active;
            ClearAnimationStateRequest();
            // 也支持直接在 Animator 面板关掉开关；仅退出时重新选择资源循环。
            if (wasExtended && active == OSCharacterExtendedAnimation.None)
                PlayResourceAnimation();
        }
        return active != OSCharacterExtendedAnimation.None;
    }

    /// <summary>
    /// 外部调用入口：按当前资源选择状态，并在进入前抽取一次循环。
    /// 选中的动画持续循环，直到资源分档变化或再次显式调用。
    /// 同时解除 SetAnimationState 设置的手动动画覆盖。
    /// </summary>
    public bool PlayResourceAnimation()
    {
        if (ApplyStoryAnimation())
            return false;
        OSCharacterExtendedAnimation previousAnimation = currentExtendedAnimation;
        ClearExtendedAnimation();
        ClearAnimationStateRequest();
        bool applied = RefreshResourceAnimation();
        if (previousAnimation != OSCharacterExtendedAnimation.None)
            PlayPortraitPoseTransition(previousAnimation, OSCharacterExtendedAnimation.None);
        return applied;
    }

    void OnAnimationResourceChanged(ResourceType type, float value, float max)
    {
        if (type == ResourceType.Heart
            || type == ResourceType.Battery
            || type == ResourceType.Fault)
        {
            // Resource warnings stay accurate even when story/work animations
            // temporarily override the resource-driven animation.
            RefreshResourceWarnings();
            RefreshResourceAnimation();
        }
    }

    void ApplyCharacterAnimation()
    {
        if (ApplyStoryAnimation())
            return;
        if (SynchronizeExtendedAnimation())
            return;
        if (followResourceAnimation && !hasManualAnimationState)
            RefreshResourceAnimation();
        else if (!stateAppliedWhileEnabled)
            TryApplyAnimationState();
    }

    bool RefreshResourceAnimation()
    {
        if (ApplyStoryAnimation() || SynchronizeExtendedAnimation()
            || !followResourceAnimation || hasManualAnimationState)
            return false;

        ResolveReferences();
        if (CurrentState != OSCharacterWindowState.Anima
            || characterAnimator == null
            || !characterAnimator.isActiveAndEnabled
            || characterAnimator.runtimeAnimatorController == null)
        {
            return false;
        }

        if (!TryGetResourceAnimationGroup(out string group))
            return false;

        // 数值在同一分档内变化时，保持原来的循环和播放进度。
        if (currentResourceAnimationGroup == group && hasAnimationStateRequest)
            return stateAppliedWhileEnabled || TryApplyAnimationState();

        string baseState = ResourceAnimationPrefix + group;
        string defaultState = group == "Normal" ? baseState : baseState + "00";
        if (!characterAnimator.HasState(0, Animator.StringToHash(defaultState)))
            return false;

        string selectedState = defaultState;
        if (group != "Normal")
        {
            string variant01 = baseState + "01";
            string variant02 = baseState + "02";
            bool has01 = characterAnimator.HasState(0, Animator.StringToHash(variant01));
            bool has02 = characterAnimator.HasState(0, Animator.StringToHash(variant02));
            if ((has01 || has02)
                && (resourceAnimationVariantChance >= 1f
                    || (resourceAnimationVariantChance > 0f
                        && UnityEngine.Random.value < resourceAnimationVariantChance)))
            {
                selectedState = has01 && has02
                    ? (UnityEngine.Random.Range(0, 2) == 0 ? variant01 : variant02)
                    : has01 ? variant01 : variant02;
            }
        }

        currentResourceAnimationGroup = group;
        requestedStateName = selectedState;
        requestedTransitionDuration = 0f;
        requestedLayer = 0;
        hasAnimationStateRequest = true;
        stateAppliedWhileEnabled = TryApplyAnimationState();
        return stateAppliedWhileEnabled;
    }

    static bool TryGetResourceAnimationGroup(out string group)
    {
        group = "Normal";
        if (!TryGetResourceWarnings(out bool heartLow, out bool batteryLow, out bool faultHigh))
            return false;

        group = faultHigh ? "HighGlitch"
            : heartLow && batteryLow ? "LowBatteryLowHeart"
            : heartLow ? "LowHeart"
            : batteryLow ? "LowBattery"
            : "Normal";
        return true;
    }

    static bool TryGetResourceWarnings(out bool heartLow, out bool batteryLow, out bool faultHigh)
    {
        heartLow = batteryLow = faultHigh = false;
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            dataManager.RefreshOSResourceRuntimeState();
            OSResourceRuntimeState state = dataManager.OSResourceState;
            heartLow = state.IsHeartLow;
            batteryLow = state.IsEnergyLow;
            faultHigh = state.IsFaultHigh;
        }
        else
        {
            GameResourceSystem resources = GameResourceSystem.Instance;
            if (resources == null)
                return false;

            heartLow = resources.Get(ResourceType.Heart)
                <= OSResourceRuntimeState.LowResourceThreshold;
            batteryLow = resources.Get(ResourceType.Battery)
                <= OSResourceRuntimeState.LowResourceThreshold;
            faultHigh = resources.Get(ResourceType.Fault)
                >= OSResourceRuntimeState.HighFaultThreshold;
        }

        return true;
    }

    void RefreshResourceWarnings()
    {
        if (resourceWarnings == null)
            return;

        bool hasResources = TryGetResourceWarnings(out bool heartLow, out bool batteryLow, out bool faultHigh);
        bool visible = CurrentState == OSCharacterWindowState.Anima && hasResources;
        SetResourceWarningVisible(lowBatteryWarning, visible && batteryLow);
        SetResourceWarningVisible(lowHeartWarning, visible && heartLow);
        SetResourceWarningVisible(highFaultWarning, visible && faultHigh);
        bool anyVisible = visible && (batteryLow || heartLow || faultHigh);
        if (resourceWarnings.gameObject.activeSelf != anyVisible)
            resourceWarnings.gameObject.SetActive(anyVisible);
    }

    static void SetResourceWarningVisible(UnityEngine.UI.Image image, bool visible)
    {
        if (image != null && image.gameObject.activeSelf != visible)
            image.gameObject.SetActive(visible);
    }

    public void SetWindowState(
        OSCharacterWindowState targetState,
        bool playTransition = true)
    {
        ResolveReferences();
        if (isChapter01ReconnectActive
            && CurrentState == OSCharacterWindowState.WaitClick
            && targetState != OSCharacterWindowState.WaitClick)
        {
            CancelChapter01Reconnect();
        }

        if (!playTransition)
        {
            ApplyStateImmediate(targetState);
            return;
        }

        if (!isTransitioning && CurrentState == targetState)
        {
            ApplyStateImmediate(targetState);
            return;
        }

        OSCharacterWindowState previousState = CurrentState;
        isTransitioning = true;
        BeginStateTransition(previousState, targetState);
    }

    public void ShowVoidMessageState(bool playTransition = true)
    {
        SetWindowState(OSCharacterWindowState.VoidMessage, playTransition);
    }

    public void ShowAnimaState(bool playTransition = true)
    {
        SetWindowState(OSCharacterWindowState.Anima, playTransition);
    }

    public void ShowWaitClickState(bool playTransition = true)
    {
        SetWindowState(OSCharacterWindowState.WaitClick, playTransition);
    }

    /// <summary>沿用第一章的开机与信号闪屏接入 Live，完成后只通知调用者。</summary>
    public bool PlaySignalIn(Action onCompleted)
    {
        CancelChapter01Reconnect();
        if (isActiveAndEnabled && TryBeginCameraSignalIn(onCompleted))
            return true;

        // 无法播放时仍落实显示状态，让剧情继续；外部取消则不会走此回调。
        ApplyStateImmediate(OSCharacterWindowState.Anima);
        onCompleted?.Invoke();
        return false;
    }

    /// <summary>沿用接入闪屏关闭 Live；保留当前动作，不播放开机或触发第一章流程。</summary>
    public bool PlaySignalOut()
    {
        if (isSignalOutActive)
            return true;

        CancelChapter01Reconnect();
        ResolveReferences();
        if (!isActiveAndEnabled || CurrentState != OSCharacterWindowState.Anima
            || !TryCreateCameraSignalMaterial())
        {
            ApplyStateImmediate(OSCharacterWindowState.VoidMessage);
            return false;
        }

        isSignalOutActive = true;
        isTransitioning = true;
        ApplyCameraSignalProgress(1f);
        Tween tween = DOVirtual.Float(
                1f,
                0f,
                Mathf.Max(0.05f, cameraSignalInDuration),
                ApplyCameraSignalProgress)
            .SetEase(Ease.InCubic)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        cameraSignalTween = tween;
        tween.OnComplete(() =>
        {
            if (cameraSignalTween != tween)
                return;

            cameraSignalTween = null;
            isSignalOutActive = false;
            ApplyCameraSignalProgress(0f);
            ApplyStateImmediate(OSCharacterWindowState.VoidMessage);
        });
        tween.OnKill(() =>
        {
            if (cameraSignalTween != tween)
                return;

            cameraSignalTween = null;
            isSignalOutActive = false;
            ApplyStateImmediate(OSCharacterWindowState.VoidMessage);
        });
        return true;
    }

    public void PrepareChapter01ReconnectState()
    {
        ResolveReferences();
        CancelChapter01Reconnect();
        StopCharacterShake(true);
        ApplyStateImmediate(OSCharacterWindowState.WaitClick);
    }

    public void BeginChapter01Reconnect()
    {
        ResolveReferences();
        CancelChapter01Reconnect();
        StopCharacterShake(true);
        isChapter01ReconnectActive = true;
        if (CurrentState != OSCharacterWindowState.WaitClick)
            ApplyStateImmediate(OSCharacterWindowState.WaitClick);

        StartWaitClickEffects(false);

        Sequence sequence = DOTween.Sequence();
        chapter01ReconnectSequence = sequence;
        sequence
            .AppendInterval(Mathf.Max(0f, firstConcernDelay))
            .AppendCallback(PlayFirstConcernMessage)
            .AppendInterval(Mathf.Max(0f, secondConcernDelay))
            .AppendCallback(PlaySecondConcernBeat)
            .AppendInterval(Mathf.Max(0f, forcedEntryDelay))
            .AppendCallback(HandleForcedEntryTimeout)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (chapter01ReconnectSequence == sequence)
                    chapter01ReconnectSequence = null;
            })
            .OnComplete(() =>
            {
                if (chapter01ReconnectSequence == sequence)
                    chapter01ReconnectSequence = null;
            });
    }

    public void EnterChapter01Normally()
    {
        CompleteChapter01Reconnect(false);
    }

    public void ForceEnterChapter01()
    {
        CompleteChapter01Reconnect(true);
    }

    public void CancelChapter01Reconnect()
    {
        isChapter01ReconnectActive = false;
        StopChapter01ReconnectSequence();
        StopChapter01EntryTransition();
        StopWaitClickEffects();
        StopCharacterShake(true);
    }

    public void RefreshWaitClickLocalization()
    {
        if (!isChapter01ReconnectActive
            || CurrentState != OSCharacterWindowState.WaitClick)
        {
            return;
        }

        ResolveWaitClickText();
        SetWaitClickDots(Mathf.Clamp(waitClickDotCount, 1, 3));
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isChapter01ReconnectActive
            || CurrentState != OSCharacterWindowState.WaitClick
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        ResolveReferences();
        if (waitClickRaycastTarget == null
            || !RectTransformUtility.RectangleContainsScreenPoint(
                waitClickRaycastTarget,
                eventData.position,
                eventData.pressEventCamera))
        {
            return;
        }

        EnterChapter01Normally();
    }

    public void BeginLinkMessageTyping()
    {
        ResolveReferences();
        KillTypingDots();
        StopTypingVoice();
        isLinkMessageTyping = true;
        StartAnimaStatusIndicatorPulse(true);

        GameDataManager dataManager = GameDataManager.Instance;
        localizedTypingText = dataManager != null
            ? dataManager.GetLocalizedText(
                typingLocalizationKey,
                typingFallbackText)
            : typingFallbackText;
        SetTypingDots(1);
        PlayRandomSendAnimation();

        float interval = Mathf.Max(0.05f, typingDotInterval);
        Sequence sequence = DOTween.Sequence();
        typingDotsSequence = sequence;
        sequence
            .AppendInterval(interval)
            .AppendCallback(() => SetTypingDots(2))
            .AppendInterval(interval)
            .AppendCallback(() => SetTypingDots(3))
            .AppendInterval(interval)
            .AppendCallback(() => SetTypingDots(1))
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (typingDotsSequence == sequence)
                    typingDotsSequence = null;
            });
    }

    public void EndLinkMessageTyping()
    {
        if (!isLinkMessageTyping
            && typingDotsSequence == null
            && typingVoiceSource == null)
        {
            return;
        }

        isLinkMessageTyping = false;
        KillTypingDots();
        StopTypingVoice();
        ResolveReferences();
        if (topNameLocalizedText != null)
            topNameLocalizedText.Refresh(OS_Handle.Instance);
        else if (topNameText != null)
        {
            GameDataManager dataManager = GameDataManager.Instance;
            topNameText.text = dataManager != null
                ? dataManager.GetLocalizedText(
                    "ui.os.app.character.name",
                    "Live")
                : "Live";
        }

        StartAnimaStatusIndicatorPulse(false);
    }

    public void PlayReconnectAnimation()
    {
        TryPlayAnimatorTrigger(reconnectAnimationTrigger);
    }

    public void PlayRandomSendAnimation()
    {
        if (sendAnimationTriggers == null
            || sendAnimationTriggers.Length == 0)
        {
            return;
        }

        int startIndex = UnityEngine.Random.Range(
            0,
            sendAnimationTriggers.Length);
        for (int offset = 0; offset < sendAnimationTriggers.Length; offset++)
        {
            string triggerName = sendAnimationTriggers[
                (startIndex + offset) % sendAnimationTriggers.Length];
            if (TryPlayAnimatorTrigger(triggerName))
                return;
        }
    }

    public void ApplyStateImmediate(OSCharacterWindowState targetState)
    {
        StopChapter01EntryTransition();
        ResolveReferences();
        bool showVoidMessage = targetState
            == OSCharacterWindowState.VoidMessage;
        bool showAnima = targetState == OSCharacterWindowState.Anima;
        bool showWaitClick = targetState
            == OSCharacterWindowState.WaitClick;

        if (!showAnima)
        {
            SynchronizeExtendedAnimation();
            extendedAnimationParametersPending |=
                currentExtendedAnimation != OSCharacterExtendedAnimation.None;
            stateAppliedWhileEnabled = false;
            StopAnimaStatusIndicatorPulse(true);
        }

        if (voidMessageState != null
            && voidMessageState.activeSelf != showVoidMessage)
        {
            voidMessageState.SetActive(showVoidMessage);
        }

        if (animaState != null && animaState.activeSelf != showAnima)
            animaState.SetActive(showAnima);

        if (waitClickState != null
            && waitClickState.activeSelf != showWaitClick)
        {
            waitClickState.SetActive(showWaitClick);
        }

        if (!showWaitClick)
            StopWaitClickEffects();

        CurrentState = targetState;
        isTransitioning = false;
        RefreshResourceWarnings();

        if (targetState == OSCharacterWindowState.Anima)
        {
            ApplyCharacterAnimation();
            StartAnimaStatusIndicatorPulse(isLinkMessageTyping);
        }
    }

    void BeginStateTransition(
        OSCharacterWindowState previousState,
        OSCharacterWindowState targetState)
    {
        RestartStateGlitch(previousState);
        ApplyStateImmediate(targetState);
        RestartStateGlitch(targetState);
    }

    void EnterWaitClickState()
    {
        if (!isChapter01ReconnectActive)
            return;

        ShowWaitClickState(true);
        StartWaitClickEffects(false);
    }

    void PlayFirstConcernMessage()
    {
        if (!IsWaitingForChapter01Interaction())
            return;

        linkMessageController?.NotifyGlobalExternalSignal(
            OSCharacterInteractionIds.Chapter01Signals.LiveWaitFirst);
    }

    void PlaySecondConcernBeat()
    {
        if (!IsWaitingForChapter01Interaction())
            return;

        PlayCharacterShake(concernShakeDuration, concernShakeStrength, 18);
        StartWaitClickEffects(true);
        linkMessageController?.NotifyExternalSignal(
            OSCharacterInteractionIds.Chapter01Signals.LiveWaitSecond);
    }

    void HandleForcedEntryTimeout()
    {
        if (!IsWaitingForChapter01Interaction())
            return;

        chapter01ReconnectSequence = null;
        ForceEnterChapter01();
    }

    void CompleteChapter01Reconnect(bool forced)
    {
        if (!IsWaitingForChapter01Interaction())
            return;

        isChapter01ReconnectActive = false;
        StopChapter01ReconnectSequence();
        StopWaitClickEffects();
        StopCharacterShake(true);

        if (forced)
        {
            PlayCharacterShake(
                forcedShakeDuration,
                forcedShakeStrength,
                28);
        }

        if (!TryBeginChapter01CameraSignalIn(forced))
        {
            ApplyStateImmediate(OSCharacterWindowState.Anima);
            HandleChapter01CameraSignalCompleted(forced);
        }
    }

    bool TryBeginChapter01CameraSignalIn(bool forced)
    {
        return TryBeginCameraSignalIn(
            () => HandleChapter01CameraSignalCompleted(forced),
            settleWithHairBrush: true);
    }

    bool TryBeginCameraSignalIn(
        Action onCompleted,
        bool playOpenAnimation = true,
        bool settleWithHairBrush = false)
    {
        StopChapter01EntryTransition();
        ResolveReferences();
        if (voidMessageState == null
            || animaState == null
            || waitClickState == null
            || characterImage == null)
        {
            return false;
        }

        if (!TryCreateCameraSignalMaterial())
            return false;

        voidMessageState.SetActive(false);
        waitClickState.SetActive(false);
        animaState.SetActive(true);
        CurrentState = OSCharacterWindowState.Anima;
        isSignalInActive = true;
        isTransitioning = true;
        stateAppliedWhileEnabled = false;
        cameraSignalOwnsAnimation = playOpenAnimation
            && SetExtendedAnimation(OSCharacterExtendedAnimation.OpenLive);
        ApplyCharacterAnimation();
        // 在第一帧信号效果采样 UV 前应用开机画面，避免先闪现普通待机。
        if (cameraSignalOwnsAnimation && characterAnimator.isActiveAndEnabled)
            characterAnimator.Update(0f);
        ConfigureCameraSignalMaterial();
        StartAnimaStatusIndicatorPulse(isLinkMessageTyping);
        ApplyCameraSignalProgress(0f);

        // The boot pose is a single close-up; play it as a handled camera instead.
        if (cameraSignalOwnsAnimation)
        {
            cameraSignalTween = BuildOpenLiveCameraSequence(
                onCompleted,
                settleWithHairBrush);
            return true;
        }

        Tween tween = DOVirtual.Float(
                0f,
                1f,
                Mathf.Max(0.05f, cameraSignalInDuration),
                ApplyCameraSignalProgress)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        cameraSignalTween = tween;
        tween
            .OnComplete(() =>
            {
                if (cameraSignalTween != tween)
                    return;

                cameraSignalTween = null;
                isSignalInActive = false;
                ApplyCameraSignalProgress(1f);
                ReleaseCameraSignalMaterial();
                isTransitioning = false;
                ReleaseCameraSignalAnimation();
                onCompleted?.Invoke();
            })
            .OnKill(() =>
            {
                if (cameraSignalTween != tween)
                    return;

                cameraSignalTween = null;
                isSignalInActive = false;
                ReleaseCameraSignalMaterial();
                isTransitioning = false;
                ReleaseCameraSignalAnimation();
            });
        return true;
    }

    void HandleChapter01CameraSignalCompleted(bool forced)
    {
        TryPlayAnimatorTrigger(chapter01EntryAnimationTrigger);
        StopChapter01EntryDelay();

        float delayDuration = Mathf.Max(
            0f,
            chapter01EntryMessageDelay - chapter01EntryMessageAdvance);
        if (delayDuration <= 0f)
        {
            NotifyChapter01EntryRequested(forced);
            return;
        }

        Tween delay = null;
        delay = DOVirtual.DelayedCall(
                delayDuration,
                () =>
                {
                    if (chapter01EntryDelayTween != delay)
                        return;

                    chapter01EntryDelayTween = null;
                    NotifyChapter01EntryRequested(forced);
                },
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        chapter01EntryDelayTween = delay;
        delay.OnKill(() =>
        {
            if (chapter01EntryDelayTween == delay)
                chapter01EntryDelayTween = null;
        });
    }

    void NotifyChapter01EntryRequested(bool forced)
    {
        if (forced)
            Chapter01ForcedEntryRequested?.Invoke();
        else
            Chapter01NormalEntryRequested?.Invoke();
    }

    bool TryCreateCameraSignalMaterial()
    {
        // A real signal transition owns the material from this point onward.
        CancelPortraitPoseTransition();
        if (characterImage == null)
            return false;

        Shader shader = cameraSignalShader != null
            ? cameraSignalShader
            : Shader.Find(CameraSignalShaderName);
        if (shader == null)
        {
            if (!missingCameraSignalShaderWasLogged)
            {
                Debug.LogError(
                    $"Required shader '{CameraSignalShaderName}' "
                    + "was not found.",
                    this);
                missingCameraSignalShaderWasLogged = true;
            }

            return false;
        }

        originalCharacterMaterial = characterImage.material;
        cameraSignalRuntimeMaterial = new Material(shader)
        {
            name = "M_OSCameraSignalIn (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        characterImage.material = cameraSignalRuntimeMaterial;
        ConfigureCameraSignalMaterial();
        return true;
    }

    void ConfigureCameraSignalMaterial()
    {
        if (cameraSignalRuntimeMaterial == null || characterImage == null)
            return;

        Rect rect = characterImage.rectTransform.rect;
        Vector4 rectSize = new Vector4(
                Mathf.Max(1f, rect.width),
                Mathf.Max(1f, rect.height),
                0f,
                0f);

        Vector4 spriteUvRect = new Vector4(0f, 0f, 1f, 1f);
        if (characterImage.sprite != null)
        {
            Vector4 outerUv = DataUtility.GetOuterUV(characterImage.sprite);
            spriteUvRect = new Vector4(
                outerUv.x,
                outerUv.y,
                outerUv.z - outerUv.x,
                outerUv.w - outerUv.y);
        }

        cameraSignalRuntimeMaterial.SetVector(CameraSignalRectSizeId, rectSize);
        cameraSignalRuntimeMaterial.SetVector(CameraSignalSpriteUvRectId, spriteUvRect);
        // Masked UI can render with a cached stencil clone; it needs the newly
        // selected frame's atlas region too, especially during a pose switch.
        Material renderingMaterial = characterImage.materialForRendering;
        if (renderingMaterial != null && renderingMaterial != cameraSignalRuntimeMaterial
            && renderingMaterial.shader != null && renderingMaterial.shader.name == CameraSignalShaderName)
        {
            renderingMaterial.SetVector(CameraSignalRectSizeId, rectSize);
            renderingMaterial.SetVector(CameraSignalSpriteUvRectId, spriteUvRect);
        }
    }

    void ApplyCameraSignalProgress(float progress)
    {
        if (cameraSignalRuntimeMaterial == null)
            return;

        float clampedProgress = Mathf.Clamp01(progress);
        cameraSignalRuntimeMaterial.SetFloat(
            CameraSignalProgressId,
            clampedProgress);
        if (characterImage == null || !characterImage.isActiveAndEnabled)
            return;

        Material renderingMaterial = characterImage.materialForRendering;
        if (renderingMaterial != null
            && renderingMaterial != cameraSignalRuntimeMaterial
            && renderingMaterial.shader != null
            && renderingMaterial.shader.name == CameraSignalShaderName)
        {
            renderingMaterial.SetFloat(
                CameraSignalProgressId,
                clampedProgress);
        }
    }

    void StopChapter01EntryTransition()
    {
        StopChapter01EntryDelay();
        StopCameraSignalIn();
    }

    void StopChapter01EntryDelay()
    {
        if (chapter01EntryDelayTween == null)
            return;

        Tween tween = chapter01EntryDelayTween;
        chapter01EntryDelayTween = null;
        tween.Kill(false);
    }

    void StopCameraSignalIn()
    {
        isSignalInActive = false;
        isSignalOutActive = false;
        if (cameraSignalTween != null)
        {
            Tween tween = cameraSignalTween;
            cameraSignalTween = null;
            tween.Kill(false);
        }

        ReleaseCameraSignalMaterial();
        isTransitioning = false;
        ReleaseCameraSignalAnimation();
    }

    void ReleaseCameraSignalAnimation()
    {
        if (!cameraSignalOwnsAnimation)
            return;
        cameraSignalOwnsAnimation = false;
        if (currentExtendedAnimation == OSCharacterExtendedAnimation.OpenLive)
            PlayResourceAnimation();
    }

    void ReleaseCameraSignalMaterial()
    {
        if (cameraSignalRuntimeMaterial == null)
            return;

        if (characterImage != null
            && characterImage.material == cameraSignalRuntimeMaterial)
        {
            characterImage.material = originalCharacterMaterial;
        }

        Material materialToDestroy = cameraSignalRuntimeMaterial;
        cameraSignalRuntimeMaterial = null;
        originalCharacterMaterial = null;
        if (portraitPoseTransitionMaterial == materialToDestroy)
        { portraitPoseTransitionMaterial = null; portraitPoseTransitionElapsed = 0f; }
        if (Application.isPlaying)
            Destroy(materialToDestroy);
        else
            DestroyImmediate(materialToDestroy);
    }

    bool IsWaitingForChapter01Interaction()
    {
        return isChapter01ReconnectActive
               && CurrentState == OSCharacterWindowState.WaitClick;
    }

    void RestartStateGlitch(OSCharacterWindowState state)
    {
        GameObject stateObject = GetStateObject(state);
        OSNoSignalGlitch glitch = state == OSCharacterWindowState.WaitClick
            ? waitClickGlitch
            : stateObject?.GetComponentInChildren<OSNoSignalGlitch>(true);
        glitch?.RestartEffect();
    }

    GameObject GetStateObject(OSCharacterWindowState state)
    {
        switch (state)
        {
            case OSCharacterWindowState.VoidMessage:
                return voidMessageState;
            case OSCharacterWindowState.Anima:
                return animaState;
            case OSCharacterWindowState.WaitClick:
                return waitClickState;
            default:
                return null;
        }
    }

    void StartWaitClickEffects(bool urgent)
    {
        ResolveReferences();
        if (waitClickText == null || waitClickIndicator == null)
            return;

        if (!urgent || waitClickDotsSequence == null)
            StartWaitClickDots();
        StartWaitClickIndicatorPulse(urgent);
    }

    void StartWaitClickDots()
    {
        StopWaitClickDots();
        ResolveWaitClickText();
        SetWaitClickDots(1);

        float interval = Mathf.Max(0.05f, waitClickDotInterval);
        Sequence sequence = DOTween.Sequence();
        waitClickDotsSequence = sequence;
        sequence
            .AppendInterval(interval)
            .AppendCallback(() => SetWaitClickDots(2))
            .AppendInterval(interval)
            .AppendCallback(() => SetWaitClickDots(3))
            .AppendInterval(interval)
            .AppendCallback(() => SetWaitClickDots(1))
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(waitClickState, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (waitClickDotsSequence == sequence)
                    waitClickDotsSequence = null;
            });
    }

    void ResolveWaitClickText()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        localizedWaitClickText = dataManager != null
            ? dataManager.GetLocalizedText(
                waitClickLocalizationKey,
                waitClickFallbackText)
            : waitClickFallbackText;
        localizedWaitClickText = RemoveTrailingDots(localizedWaitClickText);
    }

    void SetWaitClickDots(int dotCount)
    {
        if (waitClickText == null)
            return;

        waitClickDotCount = Mathf.Clamp(dotCount, 1, 3);
        waitClickText.text = (localizedWaitClickText ?? string.Empty)
                             + new string('.', waitClickDotCount);
    }

    void StartWaitClickIndicatorPulse(bool urgent)
    {
        StopWaitClickIndicatorPulse(true);
        if (waitClickIndicator == null)
            return;

        CacheWaitClickIndicatorColor();
        waitClickIndicator.color = waitClickIndicatorAuthoredColor;
        float targetAlpha = waitClickIndicatorAuthoredColor.a
                            * Mathf.Clamp01(indicatorDimAlpha);
        float duration = urgent
            ? indicatorFastPulseDuration
            : indicatorSlowPulseDuration;
        Tween tween = waitClickIndicator
            .DOFade(targetAlpha, Mathf.Max(0.05f, duration))
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true)
            .SetLink(waitClickState, LinkBehaviour.KillOnDisable);
        waitClickIndicatorTween = tween;
        tween.OnKill(() =>
        {
            if (waitClickIndicatorTween == tween)
                waitClickIndicatorTween = null;
        });
    }

    public void PlayMemoryFilesFoundShake()
    {
        ResolveReferences();
        PlayCharacterShake(concernShakeDuration, concernShakeStrength, 14);
    }

    public void PlayEmptyEnergyShake()
    {
        ResolveReferences();
        PlayCharacterShake(.5f, 3f, 14);
    }

    void PlayCharacterShake(float duration, float strength, int vibrato)
    {
        StopCharacterShake(true);
        if (chapter01ShakeTarget == null || strength <= 0f)
            return;

        Vector2 restPosition = chapter01ShakeTarget.anchoredPosition;
        Tween tween = chapter01ShakeTarget
            .DOShakeAnchorPos(
                Mathf.Max(0.05f, duration),
                strength,
                Mathf.Max(1, vibrato),
                90f,
                false,
                true)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        chapter01ShakeTween = tween;
        tween.OnComplete(() =>
        {
            if (chapter01ShakeTween != tween)
                return;

            chapter01ShakeTween = null;
            if (chapter01ShakeTarget != null)
                chapter01ShakeTarget.anchoredPosition = restPosition;
        });
        tween.OnKill(() =>
        {
            if (chapter01ShakeTween == tween)
                chapter01ShakeTween = null;
        });
    }

    void StopChapter01ReconnectSequence()
    {
        if (chapter01ReconnectSequence == null)
            return;

        Sequence sequence = chapter01ReconnectSequence;
        chapter01ReconnectSequence = null;
        sequence.Kill(false);
    }

    void StopWaitClickEffects()
    {
        StopWaitClickDots();
        StopWaitClickIndicatorPulse(true);
    }

    void StopWaitClickDots()
    {
        if (waitClickDotsSequence == null)
            return;

        Sequence sequence = waitClickDotsSequence;
        waitClickDotsSequence = null;
        sequence.Kill(false);
    }

    void StopWaitClickIndicatorPulse(bool restoreColor)
    {
        if (waitClickIndicatorTween != null)
        {
            Tween tween = waitClickIndicatorTween;
            waitClickIndicatorTween = null;
            tween.Kill(false);
        }

        if (restoreColor
            && waitClickIndicator != null
            && waitClickIndicatorColorCached)
        {
            waitClickIndicator.color = waitClickIndicatorAuthoredColor;
        }
    }

    void StartAnimaStatusIndicatorPulse(bool isTyping)
    {
        StopAnimaStatusIndicatorPulse(true);
        if (animaStatusIndicator == null
            || CurrentState != OSCharacterWindowState.Anima
            || animaState == null
            || !animaState.activeInHierarchy
            || !isActiveAndEnabled)
        {
            return;
        }

        CacheAnimaStatusIndicatorColor();
        animaStatusIndicator.color = animaStatusIndicatorAuthoredColor;
        float targetAlpha = animaStatusIndicatorAuthoredColor.a
                            * Mathf.Clamp01(indicatorDimAlpha);
        float duration = isTyping
            ? indicatorFastPulseDuration
            : indicatorSlowPulseDuration;
        Tween tween = animaStatusIndicator
            .DOFade(targetAlpha, Mathf.Max(0.05f, duration))
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true)
            .SetLink(animaState, LinkBehaviour.KillOnDisable);
        animaStatusIndicatorTween = tween;
        tween.OnKill(() =>
        {
            if (animaStatusIndicatorTween == tween)
                animaStatusIndicatorTween = null;
        });
    }

    void StopAnimaStatusIndicatorPulse(bool restoreColor)
    {
        if (animaStatusIndicatorTween != null)
        {
            Tween tween = animaStatusIndicatorTween;
            animaStatusIndicatorTween = null;
            tween.Kill(false);
        }

        if (restoreColor
            && animaStatusIndicator != null
            && animaStatusIndicatorColorCached)
        {
            animaStatusIndicator.color =
                animaStatusIndicatorAuthoredColor;
        }
    }

    void StopCharacterShake(bool complete)
    {
        if (chapter01ShakeTween == null)
            return;

        Tween tween = chapter01ShakeTween;
        chapter01ShakeTween = null;
        tween.Kill(complete);
    }

    void CacheWaitClickIndicatorColor()
    {
        if (waitClickIndicatorColorCached || waitClickIndicator == null)
            return;

        waitClickIndicatorAuthoredColor = waitClickIndicator.color;
        waitClickIndicatorColorCached = true;
    }

    void CacheAnimaStatusIndicatorColor()
    {
        if (animaStatusIndicatorColorCached
            || animaStatusIndicator == null)
        {
            return;
        }

        animaStatusIndicatorAuthoredColor = animaStatusIndicator.color;
        animaStatusIndicatorColorCached = true;
    }

    static string RemoveTrailingDots(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        int length = value.Length;
        while (length > 0
               && (value[length - 1] == '.'
                   || value[length - 1] == '\u2026'))
        {
            length--;
        }

        return length == value.Length ? value : value.Substring(0, length);
    }

    public bool TryApplyAnimationState()
    {
        if (SynchronizeExtendedAnimation() || !hasAnimationStateRequest)
            return false;

        ResolveReferences();
        if (characterAnimator == null
            || !characterAnimator.isActiveAndEnabled
            || characterAnimator.runtimeAnimatorController == null
            || requestedLayer >= characterAnimator.layerCount)
        {
            stateAppliedWhileEnabled = false;
            return false;
        }

        int stateHash = Animator.StringToHash(requestedStateName);
        if (!characterAnimator.HasState(requestedLayer, stateHash))
        {
            stateAppliedWhileEnabled = false;
            return false;
        }

        if (requestedTransitionDuration <= 0f)
            characterAnimator.Play(stateHash, requestedLayer, 0f);
        else
        {
            characterAnimator.CrossFadeInFixedTime(
                stateHash,
                requestedTransitionDuration,
                requestedLayer);
        }

        stateAppliedWhileEnabled = true;
        return true;
    }

    void ResolveReferences()
    {
        if (characterWindow == null)
            characterWindow = GetComponent<OSWindowUnit>();
        if (linkMessageLayout == null)
            linkMessageLayout = transform.Find("WindowUnit_LinkMessage")
                ?.GetComponent<OSLinkMessageWindowLayout>();

        Transform characterMask = transform.Find("CharacterMask");
        if (resourceWarnings == null && characterMask != null)
            resourceWarnings = characterMask.Find("ResourceWarnings") as RectTransform;
        if (resourceWarnings != null)
        {
            if (lowBatteryWarning == null)
                lowBatteryWarning = resourceWarnings.Find("LowBattery")?.GetComponent<UnityEngine.UI.Image>();
            if (lowHeartWarning == null)
                lowHeartWarning = resourceWarnings.Find("LowHeart")?.GetComponent<UnityEngine.UI.Image>();
            if (highFaultWarning == null)
                highFaultWarning = resourceWarnings.Find("HighFault")?.GetComponent<UnityEngine.UI.Image>();
        }
        if (voidMessageState == null && characterMask != null)
        {
            voidMessageState = characterMask
                .Find("VoidMessageState")
                ?.gameObject;
        }

        if (animaState == null && characterMask != null)
            animaState = characterMask.Find("AnimaState")?.gameObject;

        if (animaStatusIndicator == null && animaState != null)
        {
            animaStatusIndicator = animaState.transform
                .Find("Image")
                ?.GetComponent<Image>();
        }

        if (waitClickState == null && characterMask != null)
        {
            waitClickState = characterMask
                .Find("WaitClickState")
                ?.gameObject;
        }

        if (waitClickText == null && waitClickState != null)
        {
            waitClickText = waitClickState
                .GetComponentInChildren<TMP_Text>(true);
        }

        if (waitClickRaycastTarget == null && waitClickState != null)
        {
            Transform target = waitClickState.transform.Find("TMPRaycast");
            waitClickRaycastTarget = target as RectTransform;
            if (waitClickRaycastTarget == null && waitClickText != null)
                waitClickRaycastTarget = waitClickText.rectTransform;
        }

        if (waitClickIndicator == null && waitClickState != null)
        {
            Image[] images = waitClickState
                .GetComponentsInChildren<Image>(true);
            if (images.Length > 0)
                waitClickIndicator = images[0];
        }

        if (waitClickGlitch == null && waitClickState != null)
        {
            waitClickGlitch = waitClickState
                .GetComponentInChildren<OSNoSignalGlitch>(true);
        }

        if (chapter01ShakeTarget == null)
            chapter01ShakeTarget = transform as RectTransform;

        if (linkMessageController == null)
        {
            linkMessageController = GetComponentInChildren<
                OSLinkMessageController>(true);
        }

        CacheWaitClickIndicatorColor();
        CacheAnimaStatusIndicatorColor();

        if (characterAnimator == null)
        {
            Transform character = transform.Find(
                "CharacterMask/AnimaState/Character");
            if (character == null)
                character = transform.Find("CharacterMask/Character");
            if (character == null)
            {
                character = transform.Find(
                    "CharacterMask/CharacterImage");
            }

            if (character != null)
                characterAnimator = character.GetComponent<Animator>();

            if (characterAnimator == null)
                characterAnimator = GetComponentInChildren<Animator>(true);
        }

        if (characterImage == null && characterAnimator != null)
            characterImage = characterAnimator.GetComponent<Image>();

        if (characterImage == null && animaState != null)
            characterImage = animaState.GetComponentInChildren<Image>(true);

        if (topNameLocalizedText == null)
        {
            Transform topName = transform.Find("TopName");
            if (topName != null)
            {
                topNameLocalizedText =
                    topName.GetComponent<OSLocalizedText>();
            }

            if (topNameLocalizedText == null)
            {
                OSLocalizedText[] localizedTexts =
                    GetComponentsInChildren<OSLocalizedText>(true);
                for (int i = 0; i < localizedTexts.Length; i++)
                {
                    OSLocalizedText candidate = localizedTexts[i];
                    if (candidate != null
                        && candidate.LocalizationKey ==
                        "ui.os.app.character.name")
                    {
                        topNameLocalizedText = candidate;
                        break;
                    }
                }
            }
        }

        if (topNameText == null && topNameLocalizedText != null)
            topNameText = topNameLocalizedText.TargetText;

        if (topNameText == null)
        {
            Transform topName = transform.Find("TopName");
            if (topName != null)
                topNameText = topName.GetComponent<TMP_Text>();
        }
    }

    // The typing title can also be shown without any words (failed_typing).
    // Only the text reveal may open the voice gate; dots and whitespace close it.
    public void NotifyLinkMessageCharacter(char character)
    {
        if (!isLinkMessageTyping)
            return;
        if (character == '.' || character == '…' || character == '⋯'
            || char.IsWhiteSpace(character) || char.IsControl(character))
        {
            StopTypingVoice();
            return;
        }
        if (activeTypingVoiceSfx == null)
            StartTypingVoice();
    }

    public void SuspendLinkMessageTypingVoice() => StopTypingVoice();

    void StartTypingVoice()
    {
        ResolveTypingVoiceSfx();
        if (typingVoiceSfx == null || typingVoiceSfx.Length == 0)
            return;

        activeTypingVoiceSfx = typingVoiceSfx;
        lastTypingVoiceIndex = -1;
        PlayNextTypingVoice();
    }

    void UpdateTypingVoicePlayback()
    {
        if (!isLinkMessageTyping || activeTypingVoiceSfx == null)
            return;

        if (typingVoiceSource != null
            && typingVoiceSource.isPlaying
            && typingVoiceSource.clip == typingVoiceClip)
        {
            return;
        }

        typingVoiceSource = null;
        typingVoiceClip = null;
        PlayNextTypingVoice();
    }

    void PlayNextTypingVoice()
    {
        if (activeTypingVoiceSfx == null
            || activeTypingVoiceSfx.Length == 0)
        {
            return;
        }

        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            activeTypingVoiceSfx = null;
            return;
        }

        int voiceIndex = GetNextTypingVoiceIndex(
            activeTypingVoiceSfx.Length);
        AudioData voiceSfx = activeTypingVoiceSfx[voiceIndex];
        AudioSource source = audioManager.PlaySfx(voiceSfx);
        if (source == null)
        {
            activeTypingVoiceSfx = null;
            return;
        }

        typingVoiceSource = source;
        typingVoiceClip = voiceSfx.audioclip;
    }

    int GetNextTypingVoiceIndex(int voiceCount)
    {
        if (voiceCount <= 1)
        {
            lastTypingVoiceIndex = 0;
            return 0;
        }

        if (lastTypingVoiceIndex < 0
            || lastTypingVoiceIndex >= voiceCount)
        {
            lastTypingVoiceIndex = UnityEngine.Random.Range(0, voiceCount);
            return lastTypingVoiceIndex;
        }

        int nextIndex = UnityEngine.Random.Range(0, voiceCount - 1);
        if (nextIndex >= lastTypingVoiceIndex)
            nextIndex++;

        lastTypingVoiceIndex = nextIndex;
        return nextIndex;
    }

    void ResolveTypingVoiceSfx()
    {
        if (typingVoiceSfxResolved)
            return;

        List<SfxAudioData> allSfx = GameDataManager.Instance
            ?.AudioData
            ?.SFX;
        if (allSfx == null)
            return;

        var matches = new List<AudioData>(8);
        for (int i = 0; i < allSfx.Count; i++)
        {
            SfxAudioData sfx = allSfx[i];
            if (sfx != null
                && sfx.audioclip != null
                && !string.IsNullOrEmpty(sfx.keyid)
                && sfx.keyid.StartsWith(
                    typingVoiceSfxPrefix,
                    System.StringComparison.Ordinal))
            {
                matches.Add(sfx);
            }
        }

        typingVoiceSfx = matches.ToArray();
        typingVoiceSfxResolved = true;
    }

    void StopTypingVoice()
    {
        AudioSource source = typingVoiceSource;
        AudioClip clip = typingVoiceClip;
        activeTypingVoiceSfx = null;
        typingVoiceSource = null;
        typingVoiceClip = null;
        lastTypingVoiceIndex = -1;

        if (source == null || source.clip != clip)
            return;

        source.Stop();
        source.clip = null;
    }

    bool TryPlayAnimatorTrigger(string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName))
            return false;

        ResolveReferences();
        if (characterAnimator == null
            || characterAnimator.runtimeAnimatorController == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters =
            characterAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type != AnimatorControllerParameterType.Trigger
                || parameter.name != triggerName)
            {
                continue;
            }

            characterAnimator.ResetTrigger(triggerName);
            characterAnimator.SetTrigger(triggerName);
            return true;
        }

        return false;
    }

    void SetTypingDots(int dotCount)
    {
        if (!isLinkMessageTyping || topNameText == null)
            return;

        if (OS_Handle.Instance?.Chapter04Story?.IsActive == true)
        {
            topNameText.text = "Live";
            return;
        }

        topNameText.text = (localizedTypingText ?? typingFallbackText)
                           + new string('.', Mathf.Clamp(dotCount, 1, 3));
    }

    void KillTypingDots()
    {
        if (typingDotsSequence == null)
            return;

        Sequence sequence = typingDotsSequence;
        typingDotsSequence = null;
        sequence.Kill(false);
    }

    void Reset()
    {
        ResolveReferences();
    }

    void OnValidate()
    {
        ResolveReferences();
        firstConcernDelay = Mathf.Max(0f, firstConcernDelay);
        secondConcernDelay = Mathf.Max(0f, secondConcernDelay);
        forcedEntryDelay = Mathf.Max(0f, forcedEntryDelay);
        cameraSignalInDuration = Mathf.Max(0.05f, cameraSignalInDuration);
        chapter01EntryMessageDelay = Mathf.Max(
            0f,
            chapter01EntryMessageDelay);
        chapter01EntryMessageAdvance = Mathf.Max(
            0f,
            chapter01EntryMessageAdvance);
        waitClickDotInterval = Mathf.Max(0.05f, waitClickDotInterval);
        indicatorDimAlpha = Mathf.Clamp01(indicatorDimAlpha);
        indicatorSlowPulseDuration = Mathf.Max(
            0.05f,
            indicatorSlowPulseDuration);
        indicatorFastPulseDuration = Mathf.Max(
            0.05f,
            indicatorFastPulseDuration);
        concernShakeDuration = Mathf.Max(0.05f, concernShakeDuration);
        concernShakeStrength = Mathf.Max(0f, concernShakeStrength);
        forcedShakeDuration = Mathf.Max(0.05f, forcedShakeDuration);
        forcedShakeStrength = Mathf.Max(0f, forcedShakeStrength);
        typingDotInterval = Mathf.Max(0.05f, typingDotInterval);
        typingVoiceSfxResolved = false;
        typingVoiceSfx = null;
    }
}
