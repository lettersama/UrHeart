using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.OS;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class AdditionHandle : Singleton<AdditionHandle>
{
    const int PaperRecordIndex = 0;
    const string ShowPaperSfxKey = "ShowPaper";
    const string GetItemSfxKey = "GetItem";
    const string ItemAddedToRecordLocalizationKey = "ui.item_added_to_record";
    const string DefaultItemAddedToRecordText = "已置入记录";

    [Header("BK")]
    [SerializeField] GameObject bk;

    public GameObject BK => bk;

    [Header("OS Fade Transition")]
    [SerializeField] OSFadeTransitionPart osFadeTransition;

    public OSFadeTransitionPart OSFadeTransition => osFadeTransition;

    public void FadeIN()
    {
        FadeIN(null, null);
    }

    public void FadeIN(Action onComplete)
    {
        FadeIN(onComplete, null);
    }

    public void FadeIN(Action onComplete, Action onCanceled)
    {
        ResolveOSFadeTransitionReference();
        if (osFadeTransition == null)
        {
            Debug.LogWarning(
                "[AdditionHandle] OS fade transition part is missing.",
                this);
            onComplete?.Invoke();
            return;
        }

        osFadeTransition.FadeIN(onComplete, onCanceled);
    }

    public void FadeOUT()
    {
        FadeOUT(null, null);
    }

    public void FadeOUT(Action onComplete)
    {
        FadeOUT(onComplete, null);
    }

    public void FadeOUT(Action onComplete, Action onCanceled)
    {
        ResolveOSFadeTransitionReference();
        if (osFadeTransition == null)
        {
            Debug.LogWarning(
                "[AdditionHandle] OS fade transition part is missing.",
                this);
            onComplete?.Invoke();
            return;
        }

        osFadeTransition.FadeOUT(onComplete, onCanceled);
    }

    #region Load Panel

    [Header("Load Panel")]
    [SerializeField] GameObject loadPanel;
    [SerializeField] Canvas loadPanelCanvas;
    [SerializeField] GraphicRaycaster loadPanelRaycaster;
    [SerializeField] Image loadPanelBk;
    [SerializeField] TMP_Text loadPanelText;
    [SerializeField] Image loadPanelProgressImage;

    [Header("Load Panel Effect")]
    [SerializeField, Min(0f)] float loadPanelBkFadeInDuration = 1.2f;
    [SerializeField, Min(0f)] float loadPanelTextFadeInDelay = 1.2f;
    [SerializeField, Min(0f)] float loadPanelTextFadeInDuration = 1.5f;
    [SerializeField, Min(0f)] float loadPanelImageRevealDelay = 2.7f;
    [SerializeField, Min(0f)] float loadPanelImageFadeInDuration = 1f;
    [SerializeField, Min(0f)] float minimumLoadDuration = 9.5f;
    [SerializeField, Min(0f)] float loadPanelImageFadeOutDuration = 1f;
    [SerializeField, Min(0f)] float loadPanelTextFadeOutDelay = 0.3f;
    [SerializeField, Min(0f)] float loadPanelTextFadeOutDuration = 1.2f;
    [SerializeField, Min(0f)] float loadPanelBkFadeOutDelay = 1.5f;
    [SerializeField, Min(0f)] float loadPanelBkFadeOutDuration = 1.5f;
    [SerializeField] int loadPanelSortingOrder = 20;

    static readonly int LoadPanelProgressId = Shader.PropertyToID("_Progress");
    static readonly int LoadPanelRectSizeId = Shader.PropertyToID("_RectSize");
    static readonly int LoadPanelCellularTextureId =
        Shader.PropertyToID("_CellularTex");

    Sequence loadPanelShowSequence;
    Sequence loadPanelHideSequence;
    Coroutine loadPanelProgressRoutine;
    Material loadPanelProgressSourceMaterial;
    Material loadPanelProgressRuntimeMaterial;
    Texture2D loadPanelCellularTexture;
    Vector2Int loadPanelCellularTextureSize;
    Action loadPanelHiddenCallback;
    Action loadPanelBkFadeOutStartedCallback;
    float loadPanelStartedAt;
    float loadPanelBackendProgress;
    bool isLoadPanelRunning;
    bool isLoadPanelCompletionRequested;
    bool isLoadPanelExitRunning;

    public GameObject LoadPanel => loadPanel;
    public Image LoadPanelBk => loadPanelBk;
    public TMP_Text LoadPanelText => loadPanelText;
    public Image LoadPanelProgressImage => loadPanelProgressImage;
    public bool IsLoadPanelRunning => isLoadPanelRunning;

    // The visual clone belongs to the persistent session owner, while all OS
    // scene state can still be rebuilt cleanly after confirming a new game.
    public SessionLoadPanelPresentation CreateSessionLoadPresentation(Transform owner)
    {
        ResolveLoadPanelReferences();
        RefreshLoadPanelLocalization();
        if (!HasLoadPanelReferences() || !EnsureLoadPanelProgressMaterial()) return null;
        var root = new GameObject("NewGameLoadPresentation", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(SessionLoadPanelPresentation));
        root.transform.SetParent(owner, false);
        var presentation = root.GetComponent<SessionLoadPanelPresentation>();
        try
        {
            presentation.Initialize(loadPanel, GetComponent<CanvasScaler>(), loadPanelBk, loadPanelText,
                loadPanelProgressImage, new SessionLoadPanelPresentation.Timing {
                    coverIn = loadPanelBkFadeInDuration, textInDelay = loadPanelTextFadeInDelay,
                    textIn = loadPanelTextFadeInDuration, imageInDelay = loadPanelImageRevealDelay,
                    imageIn = loadPanelImageFadeInDuration, minimum = minimumLoadDuration,
                    imageOut = loadPanelImageFadeOutDuration, textOutDelay = loadPanelTextFadeOutDelay,
                    textOut = loadPanelTextFadeOutDuration, coverOutDelay = loadPanelBkFadeOutDelay,
                    coverOut = loadPanelBkFadeOutDuration
                }, loadPanelSortingOrder);
            return presentation;
        }
        catch
        {
            Destroy(root);
            throw;
        }
    }

    public bool BeginLoadPanel(Action onReadyToLoad = null)
    {
        ResolveLoadPanelReferences();
        RefreshLoadPanelLocalization();
        if (!HasLoadPanelReferences() || !EnsureLoadPanelProgressMaterial())
        {
            Debug.LogError(
                "[AdditionHandle] LoadPanel references or heartbeat material are incomplete.",
                this);
            return false;
        }

        StopLoadPanelAnimation();
        isLoadPanelRunning = true;
        isLoadPanelCompletionRequested = false;
        isLoadPanelExitRunning = false;
        loadPanelBackendProgress = 0f;
        loadPanelStartedAt = Time.realtimeSinceStartup;
        loadPanelHiddenCallback = null;
        loadPanelBkFadeOutStartedCallback = null;

        ConfigureLoadPanelVisuals();
        loadPanel.SetActive(true);

        Sequence showSequence = DOTween.Sequence();
        loadPanelShowSequence = showSequence;
        float textFadeInStart = GetLoadPanelTextFadeInStart();
        float imageRevealStart = GetLoadPanelImageRevealStart();
        Tween coverTween = loadPanelBk
            .DOFade(1f, Mathf.Max(0f, loadPanelBkFadeInDuration))
            .SetEase(Ease.Linear);
        Tween textFadeInTween = loadPanelText
            .DOFade(1f, Mathf.Max(0f, loadPanelTextFadeInDuration))
            .SetEase(Ease.Linear)
            .OnComplete(() =>
            {
                if (loadPanelShowSequence == showSequence && isLoadPanelRunning)
                    onReadyToLoad?.Invoke();
            });

        showSequence
            .Insert(0f, coverTween)
            .Insert(
                textFadeInStart,
                textFadeInTween)
            .Insert(
                imageRevealStart,
                loadPanelProgressImage
                    .DOFade(1f, Mathf.Max(0f, loadPanelImageFadeInDuration))
                    .SetEase(Ease.Linear))
            .SetUpdate(true)
            .SetLink(loadPanel, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (loadPanelShowSequence == showSequence)
                    loadPanelShowSequence = null;
            })
            .OnKill(() =>
            {
                if (loadPanelShowSequence == showSequence)
                    loadPanelShowSequence = null;
            });

        loadPanelProgressRoutine = StartCoroutine(UpdateLoadPanelProgress());
        return true;
    }

    public void SetLoadPanelBackendProgress(float progress)
    {
        if (!isLoadPanelRunning || isLoadPanelCompletionRequested)
            return;

        loadPanelBackendProgress = Mathf.Max(
            loadPanelBackendProgress,
            Mathf.Clamp01(progress));
    }

    public void CompleteLoadPanel(
        Action onHidden = null,
        Action onBkFadeOutStarted = null)
    {
        if (!isLoadPanelRunning)
        {
            onBkFadeOutStarted?.Invoke();
            onHidden?.Invoke();
            return;
        }

        loadPanelHiddenCallback += onHidden;
        loadPanelBkFadeOutStartedCallback += onBkFadeOutStarted;
        loadPanelBackendProgress = 1f;
        isLoadPanelCompletionRequested = true;
    }

    public void CancelLoadPanel(Action onHidden = null)
    {
        loadPanelBkFadeOutStartedCallback = null;
        if (!isLoadPanelRunning)
        {
            onHidden?.Invoke();
            return;
        }

        loadPanelHiddenCallback += onHidden;
        PlayLoadPanelExit();
    }

    IEnumerator UpdateLoadPanelProgress()
    {
        while (isLoadPanelRunning && !isLoadPanelExitRunning)
        {
            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - loadPanelStartedAt);
            float minimumDuration = Mathf.Max(0.0001f, minimumLoadDuration);
            float revealDelay = GetLoadPanelImageRevealStart();
            float revealDuration = Mathf.Max(0.0001f, minimumDuration - revealDelay);
            float timeProgress = Mathf.Clamp01((elapsed - revealDelay) / revealDuration);
            float visibleProgress = Mathf.Min(
                timeProgress,
                Mathf.Clamp01(loadPanelBackendProgress));

            UpdateLoadPanelMaterialSize();
            SetLoadPanelProgress(Mathf.Max(
                loadPanelProgressRuntimeMaterial.GetFloat(LoadPanelProgressId),
                visibleProgress));

            if (isLoadPanelCompletionRequested && elapsed >= minimumLoadDuration)
            {
                SetLoadPanelProgress(1f);
                loadPanelProgressRoutine = null;
                PlayLoadPanelExit();
                yield break;
            }

            yield return null;
        }

        loadPanelProgressRoutine = null;
    }

    float GetLoadPanelTextFadeInStart()
    {
        return Mathf.Max(
            Mathf.Max(0f, loadPanelBkFadeInDuration),
            Mathf.Max(0f, loadPanelTextFadeInDelay));
    }

    float GetLoadPanelImageRevealStart()
    {
        return Mathf.Max(
            Mathf.Max(0f, loadPanelImageRevealDelay),
            GetLoadPanelTextFadeInStart()
                + Mathf.Max(0f, loadPanelTextFadeInDuration));
    }

    void PlayLoadPanelExit()
    {
        if (!isLoadPanelRunning || isLoadPanelExitRunning)
            return;

        isLoadPanelExitRunning = true;
        loadPanelShowSequence?.Kill(false);
        loadPanelShowSequence = null;
        loadPanelHideSequence?.Kill(false);

        Sequence hideSequence = DOTween.Sequence();
        loadPanelHideSequence = hideSequence;
        float textFadeOutEnd = Mathf.Max(0f, loadPanelTextFadeOutDelay)
            + Mathf.Max(0f, loadPanelTextFadeOutDuration);
        float bkFadeOutStart = Mathf.Max(
            Mathf.Max(0f, loadPanelBkFadeOutDelay),
            textFadeOutEnd);
        hideSequence
            .Insert(
                0f,
                loadPanelProgressImage
                    .DOFade(0f, Mathf.Max(0f, loadPanelImageFadeOutDuration))
                    .SetEase(Ease.Linear))
            .Insert(
                Mathf.Max(0f, loadPanelTextFadeOutDelay),
                loadPanelText
                    .DOFade(0f, Mathf.Max(0f, loadPanelTextFadeOutDuration))
                    .SetEase(Ease.Linear))
            .InsertCallback(
                bkFadeOutStart,
                InvokeLoadPanelBkFadeOutStarted)
            .Insert(
                bkFadeOutStart,
                loadPanelBk
                    .DOFade(0f, Mathf.Max(0f, loadPanelBkFadeOutDuration))
                    .SetEase(Ease.Linear))
            .SetUpdate(true)
            .SetLink(loadPanel, LinkBehaviour.KillOnDisable)
            .OnComplete(() => FinishLoadPanelExit(hideSequence))
            .OnKill(() =>
            {
                if (loadPanelHideSequence == hideSequence)
                    loadPanelHideSequence = null;
            });
    }

    void FinishLoadPanelExit(Sequence hideSequence)
    {
        if (loadPanelHideSequence != hideSequence)
            return;

        InvokeLoadPanelBkFadeOutStarted();
        loadPanelHideSequence = null;
        isLoadPanelRunning = false;
        isLoadPanelCompletionRequested = false;
        isLoadPanelExitRunning = false;
        if (loadPanel != null)
            loadPanel.SetActive(false);

        Action callback = loadPanelHiddenCallback;
        loadPanelHiddenCallback = null;
        callback?.Invoke();
    }

    void InvokeLoadPanelBkFadeOutStarted()
    {
        Action callback = loadPanelBkFadeOutStartedCallback;
        loadPanelBkFadeOutStartedCallback = null;
        if (callback == null)
            return;

        try
        {
            callback.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    void ConfigureLoadPanelVisuals()
    {
        loadPanel.SetActive(true);
        loadPanelCanvas.overrideSorting = true;
        loadPanelCanvas.sortingOrder = loadPanelSortingOrder;
        loadPanelCanvas.enabled = true;
        loadPanelRaycaster.enabled = true;

        SetGraphicAlpha(loadPanelBk, 0f);
        SetGraphicAlpha(loadPanelText, 0f);
        SetGraphicAlpha(loadPanelProgressImage, 0f);
        loadPanelProgressImage.type = Image.Type.Simple;
        loadPanelProgressImage.raycastTarget = false;
        SetLoadPanelProgress(0f);
        UpdateLoadPanelMaterialSize();
    }

    void ResolveLoadPanelReferences()
    {
        if (loadPanel == null)
            loadPanel = transform.Find("LoadPanel")?.gameObject;

        if (loadPanel == null)
            return;

        if (loadPanelCanvas == null)
            loadPanelCanvas = loadPanel.GetComponent<Canvas>();

        if (loadPanelRaycaster == null)
            loadPanelRaycaster = loadPanel.GetComponent<GraphicRaycaster>();

        Transform panelBkTransform = loadPanel.transform.Find("PanelBk");
        if (loadPanelBk == null && panelBkTransform != null)
            loadPanelBk = panelBkTransform.GetComponent<Image>();

        if (loadPanelText == null && panelBkTransform != null)
        {
            loadPanelText = panelBkTransform
                .Find("Text (TMP)")
                ?.GetComponent<TMP_Text>();
        }

        if (loadPanelProgressImage == null && panelBkTransform != null)
        {
            loadPanelProgressImage = panelBkTransform
                .Find("Image")
                ?.GetComponent<Image>();
        }

    }

    bool HasLoadPanelReferences()
    {
        return loadPanel != null
            && loadPanelCanvas != null
            && loadPanelRaycaster != null
            && loadPanelBk != null
            && loadPanelText != null
            && loadPanelProgressImage != null;
    }

    bool EnsureLoadPanelProgressMaterial()
    {
        if (loadPanelProgressRuntimeMaterial != null)
        {
            loadPanelProgressImage.material = loadPanelProgressRuntimeMaterial;
            return EnsureLoadPanelCellularTexture();
        }

        Material sourceMaterial = loadPanelProgressImage.material;
        if (sourceMaterial == null
            || !sourceMaterial.HasProperty(LoadPanelProgressId)
            || !sourceMaterial.HasProperty(LoadPanelRectSizeId)
            || !sourceMaterial.HasProperty(LoadPanelCellularTextureId))
        {
            return false;
        }

        loadPanelProgressSourceMaterial = sourceMaterial;
        loadPanelProgressRuntimeMaterial = new Material(sourceMaterial)
        {
            name = sourceMaterial.name + " (Runtime)"
        };
        loadPanelProgressImage.material = loadPanelProgressRuntimeMaterial;
        return EnsureLoadPanelCellularTexture();
    }

    void SetLoadPanelProgress(float progress)
    {
        if (loadPanelProgressRuntimeMaterial != null)
        {
            loadPanelProgressRuntimeMaterial.SetFloat(
                LoadPanelProgressId,
                Mathf.Clamp01(progress));
        }
    }

    void UpdateLoadPanelMaterialSize()
    {
        if (loadPanelProgressRuntimeMaterial == null || loadPanelProgressImage == null)
            return;

        Vector2 rectSize = loadPanelProgressImage.rectTransform.rect.size;

        loadPanelProgressRuntimeMaterial.SetVector(
            LoadPanelRectSizeId,
            new Vector4(
                Mathf.Max(1f, rectSize.x),
                Mathf.Max(1f, rectSize.y),
                0f,
                0f));
        EnsureLoadPanelCellularTexture();
    }

    bool EnsureLoadPanelCellularTexture()
    {
        if (loadPanelProgressRuntimeMaterial == null
            || loadPanelProgressImage == null)
        {
            return false;
        }

        Vector2 rectSize = loadPanelProgressImage.rectTransform.rect.size;
        var requiredSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(rectSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(rectSize.y)));
        if (loadPanelCellularTexture != null
            && loadPanelCellularTextureSize == requiredSize)
        {
            loadPanelProgressRuntimeMaterial.SetTexture(
                LoadPanelCellularTextureId,
                loadPanelCellularTexture);
            return true;
        }

        ReleaseLoadPanelCellularTexture();
        loadPanelCellularTexture =
            LoadHeartbeatCellularTextureBuilder.Create(
                requiredSize.x,
                requiredSize.y);
        loadPanelCellularTextureSize = requiredSize;
        loadPanelProgressRuntimeMaterial.SetTexture(
            LoadPanelCellularTextureId,
            loadPanelCellularTexture);
        return loadPanelCellularTexture != null;
    }

    void ReleaseLoadPanelCellularTexture()
    {
        if (loadPanelCellularTexture == null)
            return;

        if (loadPanelProgressRuntimeMaterial != null)
        {
            loadPanelProgressRuntimeMaterial.SetTexture(
                LoadPanelCellularTextureId,
                null);
        }

        if (Application.isPlaying)
            Destroy(loadPanelCellularTexture);
        else
            DestroyImmediate(loadPanelCellularTexture);

        loadPanelCellularTexture = null;
        loadPanelCellularTextureSize = Vector2Int.zero;
    }

    void ReleaseLoadPanelProgressMaterial()
    {
        ReleaseLoadPanelCellularTexture();

        if (loadPanelProgressRuntimeMaterial == null)
            return;

        if (loadPanelProgressImage != null
            && loadPanelProgressImage.material == loadPanelProgressRuntimeMaterial)
        {
            loadPanelProgressImage.material = loadPanelProgressSourceMaterial;
        }

        if (Application.isPlaying)
            Destroy(loadPanelProgressRuntimeMaterial);
        else
            DestroyImmediate(loadPanelProgressRuntimeMaterial);

        loadPanelProgressRuntimeMaterial = null;
        loadPanelProgressSourceMaterial = null;
    }

    void ResetLoadPanelVisuals()
    {
        StopLoadPanelAnimation();
        isLoadPanelRunning = false;
        isLoadPanelCompletionRequested = false;
        isLoadPanelExitRunning = false;
        loadPanelHiddenCallback = null;
        loadPanelBkFadeOutStartedCallback = null;

        if (loadPanel != null)
            loadPanel.SetActive(false);
    }

    void StopLoadPanelAnimation()
    {
        loadPanelShowSequence?.Kill(false);
        loadPanelHideSequence?.Kill(false);
        loadPanelShowSequence = null;
        loadPanelHideSequence = null;
        loadPanelBkFadeOutStartedCallback = null;

        if (loadPanelProgressRoutine != null)
        {
            StopCoroutine(loadPanelProgressRoutine);
            loadPanelProgressRoutine = null;
        }
    }

    static void SetGraphicAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
            return;

        Color color = graphic.color;
        color.a = Mathf.Clamp01(alpha);
        graphic.color = color;
    }

    #endregion

    #region Heartbeat

    [Header("Heartbeat")]
    [SerializeField] CanvasGroup heartbeatPanel;
    [SerializeField] HeartbeatWaveformUI heartbeatWaveform;

    [Header("Heartbeat Effect")]
    [SerializeField, Range(0f, 1f)] float heartbeatEventTargetAlpha = 0.45f;
    [SerializeField, Min(0f)] float heartbeatPanelFadeDuration = 0.2f;

    readonly HashSet<SceneEventBase> heartbeatEventRegions =
        new HashSet<SceneEventBase>();
    bool isHeartbeatPanelSuppressed;

    public CanvasGroup HeartbeatPanel => heartbeatPanel;
    public HeartbeatWaveformUI HeartbeatWaveform => heartbeatWaveform;
    public bool IsHeartbeatPanelSuppressed => isHeartbeatPanelSuppressed;

    public void HighlightNextHeartbeat()
    {
        ResolveHeartbeatReferences();

        if (heartbeatWaveform == null)
        {
            Debug.LogWarning("[AdditionHandle] Heartbeat waveform is missing.", this);
            return;
        }

        heartbeatWaveform.HighlightNextBeat();
    }

    public void SetHeartbeatEventRegionActive(
        SceneEventBase eventRegion,
        bool isActive)
    {
        if (eventRegion == null)
            return;

        bool changed = isActive
            ? heartbeatEventRegions.Add(eventRegion)
            : heartbeatEventRegions.Remove(eventRegion);
        if (!changed)
            return;

        RefreshHeartbeatPanelVisibility();
    }

    public void ClearHeartbeatEventRegions()
    {
        heartbeatEventRegions.Clear();
        RefreshHeartbeatPanelVisibility();
    }

    public void SetHeartbeatPanelSuppressed(bool isSuppressed)
    {
        if (isHeartbeatPanelSuppressed == isSuppressed)
            return;

        isHeartbeatPanelSuppressed = isSuppressed;
        RefreshHeartbeatPanelVisibility();
    }

    void RefreshHeartbeatPanelVisibility()
    {
        bool shouldShow = !isHeartbeatPanelSuppressed
            && heartbeatEventRegions.Count > 0
            && MainGameHandle.Instance != null
            && MainGameHandle.Instance.IsInMainScene;
        FadeHeartbeatPanel(shouldShow ? heartbeatEventTargetAlpha : 0f);
    }


    void FadeHeartbeatPanel(float targetAlpha)
    {
        ResolveHeartbeatReferences();
        if (heartbeatPanel == null)
        {
            Debug.LogWarning("[AdditionHandle] Heartbeat panel CanvasGroup is missing.", this);
            return;
        }

        heartbeatWaveform?.SetBeatSfxEnabled(targetAlpha > 0f);
        heartbeatPanel.DOKill(false);
        float duration = Mathf.Max(0f, heartbeatPanelFadeDuration);
        if (duration <= 0f)
        {
            heartbeatPanel.alpha = targetAlpha;
            return;
        }

        heartbeatPanel
            .DOFade(targetAlpha, duration)
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetLink(heartbeatPanel.gameObject, LinkBehaviour.KillOnDisable);
    }

    void ResolveHeartbeatReferences()
    {
        if (heartbeatWaveform == null)
            heartbeatWaveform = GetComponentInChildren<HeartbeatWaveformUI>(true);

        if (heartbeatPanel == null && heartbeatWaveform != null)
            heartbeatPanel = heartbeatWaveform.GetComponentInParent<CanvasGroup>();
    }

    void ResetHeartbeatPanelVisuals()
    {
        heartbeatEventRegions.Clear();
        isHeartbeatPanelSuppressed = false;
        heartbeatWaveform?.SetBeatSfxEnabled(false);
        if (heartbeatPanel == null)
            return;

        heartbeatPanel.DOKill(false);
        heartbeatPanel.alpha = 0f;
    }

    #endregion

    #region Tip Text

    [Header("Tip Text")]
    [SerializeField] CanvasGroup tipTextGroup;
    [SerializeField] TMP_Text tipTextLabel;

    [Header("Tip Text Effect")]
    [SerializeField, Min(0f)] float tipTextFadeInDuration = 0.3f;
    [SerializeField, Min(0f)] float tipTextHoldDuration = 1.6f;
    [SerializeField, Min(0f)] float tipTextFadeOutDuration = 0.3f;
    [SerializeField, Min(0f)] float tipTextSfxDelay = 0.1f;

    readonly Queue<TipTextRequest> tipTextQueue =
        new Queue<TipTextRequest>();

    Sequence tipTextSequence;
    bool isTipTextPlaying;

    public CanvasGroup TipTextGroup => tipTextGroup;
    public TMP_Text TipTextLabel => tipTextLabel;

    /// <summary>
    /// Queues a general tip. A playing tip is never interrupted by a newer request.
    /// </summary>
    public void ShowTip(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("[AdditionHandle] Cannot show an empty tip.", this);
            return;
        }

        ResolveTipTextReferences();
        if (!HasTipTextReferences())
        {
            Debug.LogError("[AdditionHandle] TipText references are incomplete.", this);
            return;
        }

        tipTextQueue.Enqueue(new TipTextRequest(message, GetItemSfxKey));
        TryPlayNextTip();
    }

    /// <summary>
    /// 获取物品时调用。物品名会自动加引号，并按当前语言拼接“已置入记录”后缀。
    /// </summary>
    public void ShowItemAcquiredTip(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            Debug.LogWarning(
                "[AdditionHandle] Cannot show an item tip with an empty item name.",
                this);
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        string suffix = dataManager != null
            ? dataManager.GetLocalizedText(
                ItemAddedToRecordLocalizationKey,
                DefaultItemAddedToRecordText)
            : DefaultItemAddedToRecordText;

        ShowTip($"\"{itemName.Trim()}\" {suffix}");
    }

    void TryPlayNextTip()
    {
        if (isTipTextPlaying || tipTextQueue.Count == 0)
            return;

        ResolveTipTextReferences();
        if (!HasTipTextReferences())
        {
            tipTextQueue.Clear();
            Debug.LogError("[AdditionHandle] TipText references are incomplete.", this);
            return;
        }

        TipTextRequest request = tipTextQueue.Dequeue();
        isTipTextPlaying = true;
        tipTextLabel.text = request.Message;
        tipTextGroup.alpha = 0f;

        float fadeInDuration = Mathf.Max(0f, tipTextFadeInDuration);
        float holdDuration = Mathf.Max(0f, tipTextHoldDuration);
        float fadeOutDuration = Mathf.Max(0f, tipTextFadeOutDuration);
        float sfxDelay = Mathf.Max(0f, tipTextSfxDelay);

        Sequence sequence = DOTween.Sequence();
        tipTextSequence = sequence;
        sequence
            .Append(tipTextGroup
                .DOFade(1f, fadeInDuration)
                .SetEase(Ease.Linear))
            .InsertCallback(sfxDelay, () =>
            {
                if (tipTextSequence == sequence)
                    AudioManager.Instance?.PlaySfx(request.SfxKey);
            })
            .AppendInterval(holdDuration)
            .Append(tipTextGroup
                .DOFade(0f, fadeOutDuration)
                .SetEase(Ease.Linear))
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
            .OnComplete(() =>
            {
                if (tipTextSequence != sequence)
                    return;

                tipTextSequence = null;
                isTipTextPlaying = false;
                TryPlayNextTip();
            })
            .OnKill(() =>
            {
                if (tipTextSequence != sequence)
                    return;

                tipTextSequence = null;
                isTipTextPlaying = false;
            });
    }

    void ResolveTipTextReferences()
    {
        Transform tipRoot = tipTextGroup != null
            ? tipTextGroup.transform
            : transform.Find("TipText");

        if (tipRoot == null)
            return;

        if (tipTextGroup == null)
            tipTextGroup = tipRoot.GetComponent<CanvasGroup>();

        if (tipTextLabel == null)
            tipTextLabel = tipRoot.GetComponentInChildren<TMP_Text>(true);

        OSLocalizedFontUtility.ApplyCurrent(tipTextLabel);
    }

    bool HasTipTextReferences()
    {
        return tipTextGroup != null && tipTextLabel != null;
    }

    void ResetTipTextVisuals()
    {
        tipTextQueue.Clear();
        tipTextSequence = null;
        isTipTextPlaying = false;

        if (tipTextGroup == null)
            return;

        tipTextGroup.alpha = 0f;
        tipTextGroup.interactable = false;
        tipTextGroup.blocksRaycasts = false;
    }

    readonly struct TipTextRequest
    {
        public TipTextRequest(string message, string sfxKey)
        {
            Message = message;
            SfxKey = sfxKey;
        }

        public string Message { get; }
        public string SfxKey { get; }
    }

    #endregion

    #region Paper

    [Header("Paper")]
    [SerializeField] GameObject paper;
    [SerializeField] Image imageBk;
    [SerializeField] RectTransform paperImage;
    [SerializeField] TMP_Text closeTip;
    [SerializeField] string paperRecordDisplayName = "旧时报纸";

    [Header("Paper Effect")]
    [SerializeField, Range(0f, 1f)] float imageBkTargetAlpha = 0.75f;
    [SerializeField, Min(0f)] float paperFadeDuration = 0.1f;
    [SerializeField, Min(0f)] float paperMoveDuration = 0.35f;
    [SerializeField, Min(0f)] float closeTipDelay = 0.75f;
    [SerializeField, Min(0f)] float closeTipFadeInDuration = 0.5f;
    [SerializeField, Min(0f)] float closeTipFadeOutDuration = 0.1f;
    [SerializeField, Min(0f)] float paperFadeOutDuration = 0.2f;

    [Header("Paper State")]
    [SerializeField] bool isPaperOpen;
    [SerializeField] bool canClosePaper;
    [SerializeField] bool hasShownPaperRecordTip;

    Sequence paperShowSequence;
    Sequence paperHideSequence;
    PlayerCtrlManager paperControlOwner;
    bool paperPreviousCanMove;
    bool paperPreviousCanInteractive;
    bool paperPreviousRaycastTarget;
    bool paperOpenedFromRecord;
    int paperClosedFrame = -1;

    public bool IsPaperOpen => isPaperOpen;
    public bool CanClosePaper => canClosePaper;
    public bool BlocksPaperInput => isPaperOpen || recordLetterInputHeld || paperClosedFrame == Time.frameCount;

    #endregion

    protected override void Awake()
    {
        base.Awake();
        if (!IsSingletonInstance)
            return;

        ResolveReferences();
        ResetLoadPanelVisuals();
        ResetHeartbeatPanelVisuals();
        ResetTipTextVisuals();
        ResetPaperVisuals();
        if (paper != null)
            paper.SetActive(false);
    }

    void OnEnable()
    {
        if (!IsSingletonInstance)
            return;

        GameEvents.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= OnLanguageChanged;
        StopLoadPanelAnimation();
        if (IsSingletonInstance)
            CancelPaperPresentation();
    }

    void Update()
    {
        UpdateRecordLetterPresentation();
        if (isPaperOpen && (paper == null || !paper.activeInHierarchy || ShouldCancelRecordAttachment()))
        {
            CancelPaperPresentation();
            return;
        }

        if (!canClosePaper)
            return;

        if (WasPaperCloseInputPressed())
            UninitPaper();
    }

    protected override void OnDestroy()
    {
        StopLoadPanelAnimation();
        ReleaseLoadPanelProgressMaterial();

        if (heartbeatPanel != null)
            heartbeatPanel.DOKill(false);
        heartbeatEventRegions.Clear();

        tipTextSequence?.Kill();
        tipTextSequence = null;
        tipTextQueue.Clear();
        isTipTextPlaying = false;

        if (IsSingletonInstance)
            CancelPaperPresentation();

        DestroyRecordLetterPresentation();

        base.OnDestroy();
    }

    public void SetBKActive(bool isActive)
    {
        if (bk != null)
            bk.SetActive(isActive);
    }

    #region Paper

    public void InitPaper()
    {
        TryOpenPaper(false);
    }

    public bool TryOpenPaperFromRecord()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        OS_Handle os = OS_Handle.Instance;
        if (dataManager == null
            || !dataManager.IsRecordDataUnlocked(PaperRecordIndex)
            || os == null || !os.IsOSVisible || os.IsSystemInputLocked)
        {
            return false;
        }

        return TryOpenPaper(true);
    }

    bool TryOpenPaper(bool fromRecord, Sprite mapSprite = null)
    {
        if (!isActiveAndEnabled || BlocksPaperInput)
            return false;

        ResolveReferences();
        if (!HasPaperReferences())
        {
            Debug.LogError("[AdditionHandle] Paper references are incomplete.", this);
            return false;
        }

        PrepareRecordAttachmentContent(mapSprite);
        isPaperOpen = true;
        canClosePaper = false;
        paperOpenedFromRecord = fromRecord;
        paperControlOwner = PlayerCtrlManager.Instance;
        if (paperControlOwner != null)
        {
            paperPreviousCanMove = paperControlOwner.CanMove;
            paperPreviousCanInteractive = paperControlOwner.CanInteractive;
            paperControlOwner.SetControlState(false, false);
        }
        paperPreviousRaycastTarget = imageBk.raycastTarget;
        imageBk.raycastTarget = true;

        paperShowSequence?.Kill();
        paperHideSequence?.Kill();
        RefreshPaperLocalization();
        PreparePaperShowVisuals();
        paper.SetActive(true);

        Sequence showSequence = DOTween.Sequence();
        paperShowSequence = showSequence;

        Tween closeTipFade = closeTip
            .DOFade(1f, closeTipFadeInDuration)
            .OnComplete(() =>
            {
                if (paperShowSequence == showSequence && isPaperOpen)
                    canClosePaper = true;
            });

        showSequence
            .Insert(0f, imageBk.DOFade(imageBkTargetAlpha, paperFadeDuration))
            .Insert(0f, PresentedPaperImage
                .DOLocalMoveY(0f, paperMoveDuration)
                .SetEase(Ease.OutCubic))
            .Insert(closeTipDelay, closeTipFade)
            .SetUpdate(true)
            .SetLink(paper, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (paperShowSequence != showSequence || !isPaperOpen)
                    return;

                paperShowSequence = null;
            })
            .OnKill(() =>
            {
                if (paperShowSequence == showSequence)
                    paperShowSequence = null;
            });

        AudioManager.Instance?.PlaySfx(ShowPaperSfxKey);
        return true;
    }

    public void UninitPaper()
    {
        if (!isPaperOpen || !canClosePaper)
            return;

        canClosePaper = false;
        paperShowSequence?.Kill();
        paperHideSequence?.Kill();

        Sequence hideSequence = DOTween.Sequence();
        paperHideSequence = hideSequence;
        hideSequence
            .Join(PresentedPaperImage
                .DOLocalMoveY(-350f, paperMoveDuration)
                .SetEase(Ease.OutCubic))
            .Join(closeTip.DOFade(0f, closeTipFadeOutDuration))
            .Join(imageBk.DOFade(0f, paperFadeOutDuration))
            .SetUpdate(true)
            .SetLink(paper, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (paperHideSequence != hideSequence)
                    return;

                paperHideSequence = null;
                bool shouldCollect = !paperOpenedFromRecord;
                paper.SetActive(false);
                ReleasePaperInput();
                if (shouldCollect)
                    CollectPaperRecord();
            })
            .OnKill(() =>
            {
                if (paperHideSequence == hideSequence)
                    paperHideSequence = null;
            });
    }

    void CollectPaperRecord()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null || !dataManager.UnlockRecordData(PaperRecordIndex))
            return;

        string displayName = paperRecordDisplayName;
        if (dataManager.TryGetRecordData(
                PaperRecordIndex,
                out RecordData recordData))
        {
            displayName = dataManager.GetLocalizedRecordName(recordData);
        }

        displayName = displayName?.Trim();
        if (!string.IsNullOrEmpty(displayName)
            && displayName.Length >= 2
            && displayName[0] == '"'
            && displayName[displayName.Length - 1] == '"')
        {
            displayName = displayName.Substring(1, displayName.Length - 2);
        }

        if (string.IsNullOrWhiteSpace(displayName))
            return;

        hasShownPaperRecordTip = true;
        ShowItemAcquiredTip(displayName);
    }

    void CancelPaperPresentation()
    {
        CancelRecordLetterPresentation();
        Sequence showSequence = paperShowSequence;
        Sequence hideSequence = paperHideSequence;
        paperShowSequence = null;
        paperHideSequence = null;
        showSequence?.Kill();
        hideSequence?.Kill();
        if (!isPaperOpen)
            return;

        if (paper != null)
            paper.SetActive(false);
        ReleasePaperInput();
    }

    void ReleasePaperInput()
    {
        isPaperOpen = false;
        canClosePaper = false;
        // Keep the dismissal input consumed through later Update/LateUpdate calls.
        paperClosedFrame = Time.frameCount;
        if (imageBk != null)
            imageBk.raycastTarget = paperPreviousRaycastTarget;
        if (paperControlOwner != null)
            paperControlOwner.SetControlState(paperPreviousCanMove, paperPreviousCanInteractive);
        paperControlOwner = null;
        paperOpenedFromRecord = false;
        RestoreRecordAttachmentContent();
    }

    void OnLanguageChanged(LanguageState language)
    {
        RefreshLoadPanelLocalization();
        RefreshPaperLocalization();
        RefreshRecordLetterLocalization();
    }

    void RefreshLoadPanelLocalization()
    {
        ResolveLoadPanelReferences();
        if (loadPanelText == null)
            return;

        OSLocalizedText localizedText =
            loadPanelText.GetComponent<OSLocalizedText>();
        if (localizedText == null)
            return;

        localizedText.Initialize();
        localizedText.Refresh(OS_Handle.Instance);
    }

    void RefreshPaperLocalization()
    {
        if (paper == null)
            return;

        OSLocalizedText[] localizedTexts =
            paper.GetComponentsInChildren<OSLocalizedText>(true);
        for (int i = 0; i < localizedTexts.Length; i++)
        {
            OSLocalizedText localizedText = localizedTexts[i];
            if (localizedText == null)
                continue;

            localizedText.Initialize();
            localizedText.Refresh(OS_Handle.Instance);
        }
        bool showingMap = recordMapImage != null && recordMapImage.gameObject.activeSelf;
        OSLocalizedText.SetRuntime(closeTip,
            showingMap ? "ui.os.record.close_map_tip" : "ui.addition.paper.close_tip",
            showingMap ? "按下任意键关闭地图" : "按下任意键关闭报纸");
    }

    void PreparePaperShowVisuals()
    {
        Color backgroundColor = imageBk.color;
        backgroundColor.a = 0f;
        imageBk.color = backgroundColor;

        Vector3 imagePosition = PresentedPaperImage.localPosition;
        imagePosition.y = -350f;
        PresentedPaperImage.localPosition = imagePosition;

        Color closeTipColor = closeTip.color;
        closeTipColor.a = 0f;
        closeTip.color = closeTipColor;
        closeTip.gameObject.SetActive(true);
    }

    #endregion

    void ResolveReferences()
    {
        if (bk == null)
            bk = transform.Find("BK")?.gameObject;

        ResolveOSFadeTransitionReference();
        ResolveLoadPanelReferences();

        ResolveHeartbeatReferences();
        ResolveTipTextReferences();

        if (paper == null)
            paper = transform.Find("Paper")?.gameObject;

        if (paper == null)
            return;

        if (imageBk == null)
            imageBk = paper.transform.Find("ImageBk")?.GetComponent<Image>();

        if (paperImage == null)
            paperImage = paper.transform.Find("PaperImage") as RectTransform;

        if (closeTip == null)
            closeTip = paper.transform.Find("CloseTip")?.GetComponent<TMP_Text>();
    }

    void ResolveOSFadeTransitionReference()
    {
        if (osFadeTransition != null)
            return;

        Transform transitionRoot = transform.Find("OSFadeTransitionPart");
        if (transitionRoot != null)
        {
            osFadeTransition =
                transitionRoot.GetComponent<OSFadeTransitionPart>();
        }
    }


    bool HasPaperReferences()
    {
        return paper != null
            && imageBk != null
            && paperImage != null
            && closeTip != null;
    }

    static bool WasPaperCloseInputPressed()
    {
        bool keyboardPressed = Keyboard.current != null
            && Keyboard.current.anyKey.wasPressedThisFrame;

        Mouse mouse = Mouse.current;
        bool mousePressed = mouse != null
            && (mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame
                || mouse.middleButton.wasPressedThisFrame
                || mouse.forwardButton.wasPressedThisFrame
                || mouse.backButton.wasPressedThisFrame);

        return keyboardPressed || mousePressed;
    }

    void ResetPaperVisuals()
    {
        isPaperOpen = false;
        canClosePaper = false;

        if (imageBk != null)
        {
            Color backgroundColor = imageBk.color;
            backgroundColor.a = 0f;
            imageBk.color = backgroundColor;
        }

        if (closeTip != null)
        {
            Color closeTipColor = closeTip.color;
            closeTipColor.a = 0f;
            closeTip.color = closeTipColor;
        }
    }

}
