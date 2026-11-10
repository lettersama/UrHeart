using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class MainGameHandle : Singleton<MainGameHandle>
{
    const string RainAmbientKey = "Rain";
    const float OSAudioFadeDuration = 0.2f;

    [Header("New Game")]
    [SerializeField] Vector3 newGamePlayerPosition = new Vector3(-4f, -1.5f, 0f);

    [Header("Scene State")]
    [SerializeField] bool isInMainScene;
    [SerializeField] AyaFlyAwayTrigger ayaFlyAwayTrigger;
    [SerializeField] Aya[] ayas = new Aya[0];

    [Header("NPC Animation")]
    [SerializeField] Animator npc00Animator;

    static readonly int NpcLowHeartParam = Animator.StringToHash("LowHeart");

    [Header("Camera")]
    [SerializeField] PolygonCollider2D cameraBounds;

    [Header("NPC Collapse Shake")]
    [Tooltip("Vertical thud, in scene pixels, when the old man drops to his knees. 0 turns it off.")]
    [SerializeField, Min(0)] int npcCollapseShakePixels = 2;
    [SerializeField, Min(0.01f)] float npcCollapseShakeStepSeconds = 0.05f;
    [SerializeField, Min(1f)] float scenePixelsPerUnit = 20f;

    const string NpcLowHeartStatePath = "Base Layer.Old_LowHeart";
    const float NpcCollapseWaitLimit = 3f;
    static readonly float[] NpcCollapseShakeSteps = { -1f, .5f, -.5f };

    CinemachineCameraOffset cameraShakeOffset;
    Coroutine npcCollapseShakeRoutine;

    [Header("Movement Tutorial")]
    [SerializeField] SpriteRenderer aTip;
    [SerializeField] SpriteRenderer dTip;
    [SerializeField, Min(0.01f)] float movementTipFadeDuration = 0.5f;
    [SerializeField] bool movementTutorialCompleted;

    Tween aTipFadeTween;
    Tween dTipFadeTween;
    bool movementTutorialRunning;
    bool sceneAmbientSuspendedForOS;

    public bool IsInMainScene => isInMainScene;
    public bool MovementTutorialCompleted => movementTutorialCompleted;

    void OnEnable()
    {
        if (IsSingletonInstance)
            isInMainScene = true;
    }

    void OnDisable()
    {
        if (!IsSingletonInstance)
            return;

        isInMainScene = false;
        StopMovementTutorialAnimation();
        AdditionHandle.Instance?.ClearHeartbeatEventRegions();
        npcCollapseShakeRoutine = null;
        ClearCameraShake();
    }

    void Start()
    {
        ConfigureCameraBounds();
    }

    void Update()
    {
        if (!movementTutorialRunning || Keyboard.current == null)
            return;

        if (Keyboard.current.aKey.wasPressedThisFrame
            || Keyboard.current.dKey.wasPressedThisFrame)
        {
            DismissMovementTips();
        }
    }

    public void InitializeNewGame()
    {
        SetNpcLowHeart(false, true);
        PlayerCtrlManager.Instance?.PlayerController?.SetLowHeart(false, true);
        if (PlayerCtrlManager.Instance != null)
            PlayerCtrlManager.Instance.SetPlayerPosition(newGamePlayerPosition);
        else
            Debug.LogWarning("[MainGame] PlayerCtrlManager is missing.", this);

        for (int i = 0; i < ayas.Length; i++)
        {
            if (ayas[i] != null)
                ayas[i].ResetToInitialState();
        }

        if (ayaFlyAwayTrigger != null)
            ayaFlyAwayTrigger.ResetState();

        InitializeMovementTutorial();
        ConfigureCameraBounds();
        StartRainAmbient();
    }

    public void LoadFromSave()
    {
        InitializeMovementTutorial();
        RestoreEventStatesFromSave();
        RestoreCharacterAnimations();
        ConfigureCameraBounds();
    }

    public void CaptureResumeState(GameFlowSaveData state)
    {
        if (state != null && ayaFlyAwayTrigger != null)
            state.ayaFlewAway = ayaFlyAwayTrigger.HasTriggered;
    }

    public void RestoreResumeState(GameFlowSaveData state)
    {
        if (state == null) return;
        ayaFlyAwayTrigger?.RestoreState(state.ayaFlewAway);
        if (state.phase == MrTGame.Core.GamePhase.OSMain)
            SuspendSceneAmbientForOS();
        else
            StartRainAmbient(.2f);
    }

    public void SetNpcLowHeart(bool isLowHeart, bool immediate = false)
    {
        if (npc00Animator == null)
        {
            Animator[] animators = Object.FindObjectsByType<Animator>(
                FindObjectsInactive.Include);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i].name == "Npc_00"
                    && animators[i].gameObject.scene == gameObject.scene)
                {
                    npc00Animator = animators[i];
                    break;
                }
            }
        }

        if (npc00Animator == null)
        {
            Debug.LogWarning("[MainGame] Npc_00 Animator is missing.", this);
            return;
        }

        npc00Animator.SetBool(NpcLowHeartParam, isLowHeart);
        if (immediate && npc00Animator.isActiveAndEnabled)
        {
            npc00Animator.Play(isLowHeart
                ? "Base Layer.Old_LowHeart" : "Base Layer.Old_Idle", 0, 0f);
        }
    }

    /// <summary>A light camera thud on the frame the old man hits his knees.</summary>
    public void PlayNpcCollapseShake()
    {
        if (!isActiveAndEnabled || npcCollapseShakePixels <= 0)
            return;

        if (npcCollapseShakeRoutine != null)
            StopCoroutine(npcCollapseShakeRoutine);
        npcCollapseShakeRoutine = StartCoroutine(NpcCollapseShakeRoutine());
    }

    IEnumerator NpcCollapseShakeRoutine()
    {
        // Old_EnterLowHeart bends him over; the drop is the handover to Old_LowHeart.
        float waited = 0f;
        while (npc00Animator != null
            && !npc00Animator.GetCurrentAnimatorStateInfo(0).IsName(NpcLowHeartStatePath))
        {
            waited += Time.deltaTime;
            if (waited > NpcCollapseWaitLimit)
            {
                npcCollapseShakeRoutine = null;
                yield break;
            }

            yield return null;
        }

        CinemachineCameraOffset offset = EnsureCameraShakeOffset();
        if (offset != null)
        {
            float pixel = 1f / scenePixelsPerUnit;
            for (int i = 0; i < NpcCollapseShakeSteps.Length; i++)
            {
                float y = Mathf.Round(NpcCollapseShakeSteps[i] * npcCollapseShakePixels) * pixel;
                offset.Offset = new Vector3(0f, y, 0f);
                yield return new WaitForSeconds(npcCollapseShakeStepSeconds);
            }

            ClearCameraShake();
        }

        npcCollapseShakeRoutine = null;
    }

    CinemachineCameraOffset EnsureCameraShakeOffset()
    {
        if (cameraShakeOffset != null)
            return cameraShakeOffset;

        CinemachineConfiner2D confiner =
            Object.FindFirstObjectByType<CinemachineConfiner2D>(FindObjectsInactive.Include);
        CinemachineCamera vcam = confiner != null
            ? confiner.GetComponent<CinemachineCamera>()
            : Object.FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);
        if (vcam == null)
        {
            Debug.LogWarning("[MainGame] CinemachineCamera is missing; camera shake skipped.", this);
            return null;
        }

        // Applied after the confiner, so the thud is not clamped away at the bounds.
        cameraShakeOffset = vcam.GetComponent<CinemachineCameraOffset>();
        if (cameraShakeOffset == null)
            cameraShakeOffset = vcam.gameObject.AddComponent<CinemachineCameraOffset>();
        return cameraShakeOffset;
    }

    void ClearCameraShake()
    {
        if (cameraShakeOffset != null)
            cameraShakeOffset.Offset = Vector3.zero;
    }

    void RestoreCharacterAnimations()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        bool firstTreatmentCompleted = dataManager.TryGetEventState(
            "Event_02", out bool isActive, out bool isCompleted)
            && (isActive || isCompleted);
        bool firstTreatmentSucceeded = dataManager.TryGetOSWindowContentState(
            "WindowUnit_HeartHealTool", out OSWindowLayoutSaveData finishState)
            && finishState.contentContextIndex == 1
            && finishState.contentStateId == "heart_heal.success";
        SetNpcLowHeart(dataManager.IsEventCompleted("Event_00")
            && !firstTreatmentCompleted && !firstTreatmentSucceeded, true);
        PlayerCtrlManager.Instance?.PlayerController?.SetLowHeart(
            dataManager.IsEventCompleted("Event_05"), true);
    }

    public bool SetEventActive(string eventId, bool isActive)
    {
        if (string.IsNullOrEmpty(eventId))
        {
            Debug.LogWarning("[MainGame] Cannot activate an empty event ID.", this);
            return false;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
        {
            Debug.LogWarning(
                $"[MainGame] GameDataManager is missing; {eventId} was not changed.",
                this);
            return false;
        }

        SceneEventBase sceneEvent = FindSceneEvent(eventId);
        if (sceneEvent == null)
        {
            Debug.LogWarning(
                $"[MainGame] Scene event '{eventId}' was not found.",
                this);
            return false;
        }

        bool isCompleted = isActive
            ? false
            : dataManager.IsEventCompleted(eventId);
        dataManager.SetEventState(eventId, isActive, isCompleted);
        sceneEvent.ApplySavedState(isActive, isCompleted);
        return true;
    }

    public void SuspendSceneAmbientForOS(
        float fadeDuration = OSAudioFadeDuration)
    {
        sceneAmbientSuspendedForOS = true;
        StopRainAmbient(fadeDuration);
    }

    internal static void StopRainAmbient(float fadeDuration = OSAudioFadeDuration)
    {
        // Do not gate cleanup on the cached suspended flag: debug reloads can
        // start the rain again, and the shared channel outlives Game_Main.
        AudioClip rain = GameDataManager.Instance?.AudioData?.AMBIENT?.Find(
            item => item != null && item.keyid == RainAmbientKey)?.audioclip;
        AudioManager.Instance?.StopAmbientClip(rain, fadeDuration);
    }

    public void RestoreSceneAmbientAfterOS(
        float fadeDuration = OSAudioFadeDuration)
    {
        if (!sceneAmbientSuspendedForOS)
            return;

        sceneAmbientSuspendedForOS = false;
        StartRainAmbient(Mathf.Max(0f, fadeDuration));
    }

    void RestoreEventStatesFromSave()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
        {
            Debug.LogWarning(
                "[MainGame] GameDataManager is missing; scene events were not restored.",
                this);
            return;
        }

        SceneEventBase[] sceneEvents = GetSceneEvents();
        var eventIds = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < sceneEvents.Length; i++)
        {
            SceneEventBase sceneEvent = sceneEvents[i];
            if (sceneEvent == null || sceneEvent.gameObject.scene != gameObject.scene)
                continue;

            string eventId = sceneEvent.EventId;
            if (string.IsNullOrEmpty(eventId))
            {
                Debug.LogWarning(
                    $"[MainGame] Scene event '{sceneEvent.name}' has no event ID.",
                    sceneEvent);
                continue;
            }

            if (!eventIds.Add(eventId))
            {
                Debug.LogWarning(
                    $"[MainGame] Duplicate scene event ID '{eventId}'.",
                    sceneEvent);
                continue;
            }

            dataManager.GetOrInitializeEventState(
                eventId,
                sceneEvent.gameObject.activeSelf,
                out bool isActive,
                out bool isCompleted);
            sceneEvent.ApplySavedState(isActive, isCompleted);
        }
    }

    SceneEventBase FindSceneEvent(string eventId)
    {
        SceneEventBase[] sceneEvents = GetSceneEvents();
        for (int i = 0; i < sceneEvents.Length; i++)
        {
            SceneEventBase sceneEvent = sceneEvents[i];
            if (sceneEvent != null
                && sceneEvent.gameObject.scene == gameObject.scene
                && string.Equals(
                    sceneEvent.EventId,
                    eventId,
                    System.StringComparison.Ordinal))
            {
                return sceneEvent;
            }
        }

        return null;
    }

    static SceneEventBase[] GetSceneEvents()
    {
        return Object.FindObjectsByType<SceneEventBase>(
            FindObjectsInactive.Include);
    }

    void InitializeMovementTutorial()
    {
        ResolveMovementTutorialReferences();
        StopMovementTutorialAnimation();

        GameDataManager dataManager = GameDataManager.Instance;
        movementTutorialCompleted = dataManager != null
            && dataManager.MainGameData.movementTutorialCompleted;

        bool shouldShow = !movementTutorialCompleted;
        SetTipAlpha(aTip, shouldShow ? 1f : 0f);
        SetTipAlpha(dTip, shouldShow ? 1f : 0f);

        movementTutorialRunning = shouldShow
            && (aTip != null || dTip != null);

        if (shouldShow && (aTip == null || dTip == null))
        {
            Debug.LogWarning(
                "[MainGame] ATip and DTip are required for the movement tutorial.",
                this);
        }
    }

    void DismissMovementTips()
    {
        if (!movementTutorialRunning)
            return;

        movementTutorialRunning = false;
        movementTutorialCompleted = true;
        aTipFadeTween?.Kill(false);
        dTipFadeTween?.Kill(false);
        aTipFadeTween = FadeOutTip(aTip);
        dTipFadeTween = FadeOutTip(dTip);

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            dataManager.SetMainGameMovementTutorialCompleted();
        }
        else
        {
            Debug.LogWarning(
                "[MainGame] GameDataManager is missing; tutorial completion was not saved.",
                this);
        }
    }

    Tween FadeOutTip(SpriteRenderer tip)
    {
        if (tip == null)
            return null;

        return tip
            .DOFade(0f, Mathf.Max(0.01f, movementTipFadeDuration))
            .SetEase(Ease.Linear)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    void StopMovementTutorialAnimation()
    {
        aTipFadeTween?.Kill(false);
        dTipFadeTween?.Kill(false);
        aTipFadeTween = null;
        dTipFadeTween = null;
        movementTutorialRunning = false;
    }

    void ResolveMovementTutorialReferences()
    {
        if (aTip == null)
            aTip = transform.Find("ATip")?.GetComponent<SpriteRenderer>();

        if (dTip == null)
            dTip = transform.Find("DTip")?.GetComponent<SpriteRenderer>();
    }

    static void SetTipAlpha(SpriteRenderer tip, float alpha)
    {
        if (tip == null)
            return;

        Color color = tip.color;
        color.a = Mathf.Clamp01(alpha);
        tip.color = color;
    }

    void StartRainAmbient(float fadeDuration = 1f)
    {
        var audioData = GameDataManager.Instance?.AudioData;
        var rain = audioData?.AMBIENT?.Find(
            item => item != null && item.keyid == RainAmbientKey);

        if (rain?.audioclip == null)
        {
            Debug.LogWarning($"[MainGame] Ambient '{RainAmbientKey}' is missing.", this);
            return;
        }

        if (AudioManager.Instance == null)
        {
            Debug.LogWarning("[MainGame] AudioManager is missing.", this);
            return;
        }

        AudioManager.Instance.PlayAmbient(rain.audioclip, fadeDuration);
        sceneAmbientSuspendedForOS = false;
    }

    void ConfigureCameraBounds()
    {
        if (cameraBounds == null)
        {
            Debug.LogWarning("[MainGame] Camera bounds are missing.", this);
            return;
        }

        CinemachineConfiner2D confiner =
            Object.FindFirstObjectByType<CinemachineConfiner2D>(FindObjectsInactive.Include);

        if (confiner == null)
        {
            Debug.LogWarning("[MainGame] CinemachineConfiner2D is missing.", this);
            return;
        }

        if (confiner.BoundingShape2D == cameraBounds)
            return;

        confiner.BoundingShape2D = cameraBounds;
        confiner.InvalidateBoundingShapeCache();
    }
}
