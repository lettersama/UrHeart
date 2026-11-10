using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Sprites;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSCloseAndReOpenPart : OSSFXPart
{
    const string CloseShaderName = "UI/OS/CloseTV";
    const string ProcessShaderName = "UI/OS/ProcessBlocks";
    const string BootRevealShaderName = "UI/OS/BootWaveReveal";
    const string StateIndex1VolumeName = "OS Volume_ForStateIndex1";
    const string ShutdownSfxKey = "Shut_Down";
    const string BootSfxKey = "Boot";
    const string LoadingFallbackText = "重启中";

    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int ReferenceResolutionId =
        Shader.PropertyToID("_ReferenceResolution");
    static readonly int SpriteUvRectId =
        Shader.PropertyToID("_SpriteUvRect");
    static readonly int BlockCountId = Shader.PropertyToID("_BlockCount");
    static readonly int OriginId = Shader.PropertyToID("_Origin");
    static readonly int HasSnapshotId = Shader.PropertyToID("_HasSnapshot");

    [Header("Close Visual")]
    [SerializeField] Image closeImage;
    [SerializeField] Material closeMaterialTemplate;
    [SerializeField]
    Vector2Int referenceResolution =
        new Vector2Int(640, 360);
    [SerializeField, Min(0.05f)] float closeDuration = 0.42f;
    [SerializeField, Range(1f, 5f)] float closeEaseOutPower = 2.4f;

    [Header("Restart Visual")]
    [SerializeField] Image restartBackground;
    [SerializeField] Image processBackground;
    [SerializeField] Image processActive;
    [SerializeField] TMP_Text processNumber;
    [SerializeField] TMP_Text loadingTip;
    [SerializeField] OSLocalizedText loadingTipLocalizedText;
    [SerializeField] Material processMaterialTemplate;
    [SerializeField, Min(1)] int processBlockCount = 22;

    [Header("Restart Timing")]
    [SerializeField, Min(0f)] float restartDelay = 1.2f;
    [SerializeField, Min(0f)] float contentFadeDuration = 0.8f;
    [SerializeField, Min(0.1f)] float loadingDuration = 8f;
    [SerializeField, Min(0.1f)] float loadingTipDotInterval = 0.4f;

    [Header("Boot Wave Reveal")]
    [SerializeField] RawImage snapshotImage;
    [FormerlySerializedAs("implodeMaterialTemplate")]
    [SerializeField] Material bootRevealMaterialTemplate;
    [FormerlySerializedAs("implodeDelay")]
    [SerializeField, Min(0f)] float bootRevealDelay;
    [FormerlySerializedAs("implodeDuration")]
    [SerializeField, Min(0.05f)] float bootRevealDuration = 2.4f;
    [FormerlySerializedAs("implodeCenter")]
    [SerializeField] Vector2 bootRevealOrigin = new Vector2(0.5f, 0f);

    Material closeRuntimeMaterial;
    Material processRuntimeMaterial;
    Material bootRevealRuntimeMaterial;
    Material originalCloseMaterial;
    Material originalProcessMaterial;
    Material originalSnapshotMaterial;
    Texture2D bootRevealSnapshot;
    Coroutine closeRoutine;
    Coroutine bootRevealRoutine;
    Sequence restartSequence;
    Sequence loadingTipSequence;
    Tween bootRevealTween;
    Tween stateIndex1VolumeTween;
    Volume stateIndex1Volume;
    float stateIndex1VolumeStartWeight;
    bool hasStateIndex1VolumeStartWeight;
    OS_Handle osHandle;
    float processBackgroundAlpha = 1f;
    float processActiveAlpha = 1f;
    float processNumberAlpha = 1f;
    float loadingTipAlpha = 1f;
    Vector3 bootRevealMaskAuthoredLocalScale = Vector3.one;
    int displayedPercent = -1;
    bool alphasCached;
    bool bootRevealMaskTransformCached;
    bool isEffectEnabled;
    Transform[] sourceChildren;
    bool[] sourceChildrenActiveStates;

    public override string PartName => "CloseAndReOpenPart";
    public override bool IsEffectEnabled => isEffectEnabled;
    public float LoadingProgress { get; private set; }

    public event Action CloseCompleted;
    public event Action LoadingCompleted;
    public event Action BootRevealCompleted;

    void Awake()
    {
        ResolveReferences();
        CacheAuthoredAlphas();
        CreateRuntimeMaterials();
        ResetVisual();
    }

    void OnDisable()
    {
        StopAnimations();
        ResetStateIndex1Volume();
        isEffectEnabled = false;
        ApplyResetVisuals();
    }

    void OnDestroy()
    {
        StopAnimations();
        RestoreAndDestroyRuntimeMaterials();
    }

    public override void SetEffectEnabled(bool enabled)
    {
        if (enabled)
            PlayClose();
        else
            ResetVisual();
    }

    public void PlayClose()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "CloseAndReOpenPart can only preview while in Play Mode.",
                this);
            return;
        }

        gameObject.SetActive(true);
        ResolveReferences();
        CacheAuthoredAlphas();
        CreateRuntimeMaterials();
        if (!ValidatePlaybackSetup())
            return;

        StopAnimations();
        ResetStateIndex1Volume();
        ApplyResetVisuals();
        isEffectEnabled = true;
        closeRoutine = StartCoroutine(PlayCloseRoutine());
    }

    void PlayShutdownAudio()
    {
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            Debug.LogWarning(
                "[OS CloseAndReOpen] AudioManager is missing.",
                this);
            return;
        }

        audioManager.StopAmbient(0f);
        audioManager.PlaySfx(ShutdownSfxKey);
    }

    public void ResetVisual()
    {
        StopAnimations();
        ResetStateIndex1Volume();
        isEffectEnabled = false;
        ApplyResetVisuals();
    }

    IEnumerator PlayCloseRoutine()
    {
        // Cache the unobscured OS frame before the shutdown mask appears. The
        // boot wave uses it only inside its narrow refraction wall.
        yield return new WaitForEndOfFrame();
        if (!isEffectEnabled)
        {
            closeRoutine = null;
            yield break;
        }

        ReplaceBootRevealSnapshot(
            ScreenCapture.CaptureScreenshotAsTexture());
        PlayShutdownAudio();
        closeImage.gameObject.SetActive(true);
        closeImage.enabled = true;
        closeImage.raycastTarget = true;
        ApplyCloseProgress(0f);
        FadeStateIndex1Volume();

        float duration = Mathf.Max(0.05f, closeDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float easePower = Mathf.Max(1f, closeEaseOutPower);
            float progress = 1f - Mathf.Pow(
                1f - normalizedTime,
                easePower);
            ApplyCloseProgress(progress);
            yield return null;
        }

        ApplyCloseProgress(1f);
        ShowRestartBackground();
        closeRoutine = null;
        BeginRestartSequence();
        CloseCompleted?.Invoke();
    }

    void BeginRestartSequence()
    {
        PrepareRestartVisuals();

        float fadeDuration = Mathf.Max(0f, contentFadeDuration);
        float totalLoadingDuration = Mathf.Max(0.1f, loadingDuration);
        Sequence sequence = DOTween.Sequence();
        restartSequence = sequence;

        Sequence loadingSequence = DOTween.Sequence();
        loadingSequence.Append(DOVirtual.Float(
                0f,
                0.4f,
                totalLoadingDuration * 0.3f,
                ApplyLoadingProgress)
            .SetEase(Ease.Linear));
        loadingSequence.Append(DOVirtual.Float(
                0.4f,
                0.72f,
                totalLoadingDuration * 0.5f,
                ApplyLoadingProgress)
            .SetEase(Ease.OutQuad));
        loadingSequence.Append(DOVirtual.Float(
                0.72f,
                1f,
                totalLoadingDuration * 0.2f,
                ApplyLoadingProgress)
            .SetEase(Ease.Linear));

        sequence.AppendInterval(Mathf.Max(0f, restartDelay));
        sequence.AppendCallback(() =>
        {
            StartLoadingTipLoop();
            processActive.enabled = true;
            ApplyLoadingProgress(0f);
            if (osHandle == null)
                ResolveReferences();
            if (osHandle != null)
            {
                osHandle.InitializeMainStory();
                osHandle.PrepareChapter01ReconnectState();
            }
        });
        sequence.Append(processBackground
            .DOFade(processBackgroundAlpha, fadeDuration)
            .SetEase(Ease.Linear));
        sequence.Join(processNumber
            .DOFade(processNumberAlpha, fadeDuration)
            .SetEase(Ease.Linear));
        sequence.Join(loadingTip
            .DOFade(loadingTipAlpha, fadeDuration)
            .SetEase(Ease.Linear));
        sequence.Join(loadingSequence);
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (restartSequence != sequence)
                    return;

                restartSequence = null;
                ApplyLoadingProgress(1f);
                bootRevealRoutine = StartCoroutine(
                    PlayBootRevealRoutine());
                LoadingCompleted?.Invoke();
            })
            .OnKill(() =>
            {
                if (restartSequence == sequence)
                    restartSequence = null;
            });
    }

    void PrepareRestartVisuals()
    {
        processBackground.gameObject.SetActive(true);
        processActive.gameObject.SetActive(true);
        processNumber.gameObject.SetActive(true);
        loadingTip.gameObject.SetActive(true);

        processBackground.raycastTarget = false;
        processActive.raycastTarget = false;
        processActive.enabled = false;
        SetGraphicAlpha(processBackground, 0f);
        SetGraphicAlpha(processActive, processActiveAlpha);
        SetGraphicAlpha(processNumber, 0f);
        SetGraphicAlpha(loadingTip, 0f);
        ApplyLoadingProgress(0f);
        SetLoadingTipDots(0);
    }

    void ShowRestartBackground()
    {
        restartBackground.gameObject.SetActive(true);
        restartBackground.enabled = true;
        restartBackground.raycastTarget = true;

        closeImage.raycastTarget = false;
        closeImage.enabled = false;
    }

    void StartLoadingTipLoop()
    {
        StopLoadingTipLoop();
        if (loadingTip == null || !loadingTip.gameObject.activeInHierarchy)
            return;

        SetLoadingTipDots(0);
        float interval = Mathf.Max(0.1f, loadingTipDotInterval);
        Sequence sequence = DOTween.Sequence();
        loadingTipSequence = sequence;
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetLoadingTipDots(1));
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetLoadingTipDots(2));
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetLoadingTipDots(3));
        sequence.AppendInterval(interval);
        sequence.AppendCallback(() => SetLoadingTipDots(0));
        sequence
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (loadingTipSequence == sequence)
                    loadingTipSequence = null;
            });
    }

    void SetLoadingTipDots(int dotCount)
    {
        if (loadingTip == null)
            return;

        string baseText = LoadingFallbackText;
        if (loadingTipLocalizedText != null)
        {
            loadingTipLocalizedText.Refresh(osHandle);
            if (!string.IsNullOrWhiteSpace(loadingTip.text))
                baseText = loadingTip.text.TrimEnd('.');
        }

        loadingTip.text = baseText
            + new string('.', Mathf.Clamp(dotCount, 0, 3));
    }

    void ResolveReferences()
    {
        closeImage = ResolveImage(closeImage, "Close");
        restartBackground = ResolveImage(restartBackground, "TestBK");
        processBackground = ResolveImage(processBackground, "ProcessBK");
        processActive = ResolveImage(processActive, "ProcessActive");
        processNumber = ResolveText(
            processNumber,
            "ProcessActive/ProcessNum");
        loadingTip = ResolveText(loadingTip, "LoadingTip");
        snapshotImage = ResolveRawImage(snapshotImage, "RawImage");

        if (loadingTipLocalizedText == null && loadingTip != null)
        {
            loadingTipLocalizedText =
                loadingTip.GetComponent<OSLocalizedText>();
        }

        if (osHandle == null)
            osHandle = GetComponentInParent<OS_Handle>(true);
    }

    Image ResolveImage(Image current, string path)
    {
        if (current != null)
            return current;

        Transform target = transform.Find(path);
        return target != null ? target.GetComponent<Image>() : null;
    }

    TMP_Text ResolveText(TMP_Text current, string path)
    {
        if (current != null)
            return current;

        Transform target = transform.Find(path);
        return target != null ? target.GetComponent<TMP_Text>() : null;
    }

    RawImage ResolveRawImage(RawImage current, string path)
    {
        if (current != null)
            return current;

        Transform target = transform.Find(path);
        return target != null ? target.GetComponent<RawImage>() : null;
    }

    void CacheAuthoredAlphas()
    {
        if (alphasCached
            || processBackground == null
            || processActive == null
            || processNumber == null
            || loadingTip == null)
        {
            return;
        }

        processBackgroundAlpha = processBackground.color.a;
        processActiveAlpha = processActive.color.a;
        processNumberAlpha = processNumber.color.a;
        loadingTipAlpha = loadingTip.color.a;
        alphasCached = true;
    }

    bool ValidatePlaybackSetup()
    {
        bool referencesReady = closeImage != null
            && restartBackground != null
            && processBackground != null
            && processActive != null
            && processNumber != null
            && loadingTip != null
            && loadingTipLocalizedText != null
            && snapshotImage != null;
        bool materialsReady = closeRuntimeMaterial != null
            && processRuntimeMaterial != null
            && bootRevealRuntimeMaterial != null;
        if (referencesReady && materialsReady)
            return true;

        Debug.LogError(
            "CloseAndReOpenPart requires Close, TestBK, ProcessBK, "
            + "ProcessActive/ProcessNum, LoadingTip with OSLocalizedText, "
            + "RawImage, and all three OS SFX shaders.",
            this);
        return false;
    }

    void CreateRuntimeMaterials()
    {
        CreateCloseRuntimeMaterial();
        CreateProcessRuntimeMaterial();
        CreateBootRevealRuntimeMaterial();
    }

    void CreateCloseRuntimeMaterial()
    {
        if (closeRuntimeMaterial != null || closeImage == null)
            return;

        originalCloseMaterial = closeImage.material;
        Material source = FindMaterialSource(
            closeMaterialTemplate,
            closeImage.material,
            CloseShaderName);
        if (source != null)
            closeRuntimeMaterial = new Material(source);
        else
            closeRuntimeMaterial = CreateMaterialFromShader(CloseShaderName);

        if (closeRuntimeMaterial == null)
            return;

        closeRuntimeMaterial.name = "M_OSCloseTV (Runtime)";
        closeRuntimeMaterial.hideFlags = HideFlags.HideAndDontSave;
        closeImage.material = closeRuntimeMaterial;
        UpdateReferenceResolution();
    }

    void CreateProcessRuntimeMaterial()
    {
        if (processRuntimeMaterial != null || processActive == null)
            return;

        originalProcessMaterial = processActive.material;
        Material source = FindMaterialSource(
            processMaterialTemplate,
            processActive.material,
            ProcessShaderName);
        if (source != null)
            processRuntimeMaterial = new Material(source);
        else
            processRuntimeMaterial = CreateMaterialFromShader(
                ProcessShaderName);

        if (processRuntimeMaterial == null)
            return;

        processRuntimeMaterial.name = "M_OSProcessBlocks (Runtime)";
        processRuntimeMaterial.hideFlags = HideFlags.HideAndDontSave;
        processActive.material = processRuntimeMaterial;
        ConfigureProcessMaterial();
    }

    void CreateBootRevealRuntimeMaterial()
    {
        if (bootRevealRuntimeMaterial != null || snapshotImage == null)
            return;

        originalSnapshotMaterial = snapshotImage.material;
        Material source = FindMaterialSource(
            bootRevealMaterialTemplate,
            snapshotImage.material,
            BootRevealShaderName);
        if (source != null)
            bootRevealRuntimeMaterial = new Material(source);
        else
            bootRevealRuntimeMaterial = CreateMaterialFromShader(
                BootRevealShaderName);

        if (bootRevealRuntimeMaterial == null)
            return;

        bootRevealRuntimeMaterial.name = "M_OSBootWaveReveal (Runtime)";
        bootRevealRuntimeMaterial.hideFlags = HideFlags.HideAndDontSave;
        snapshotImage.material = bootRevealRuntimeMaterial;
        ConfigureBootRevealMaterial();
    }

    static Material FindMaterialSource(
        Material template,
        Material assigned,
        string shaderName)
    {
        if (template != null
            && template.shader != null
            && template.shader.name == shaderName)
        {
            return template;
        }

        return assigned != null
            && assigned.shader != null
            && assigned.shader.name == shaderName
                ? assigned
                : null;
    }

    static Material CreateMaterialFromShader(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        return shader != null ? new Material(shader) : null;
    }

    void UpdateReferenceResolution()
    {
        if (closeRuntimeMaterial == null)
            return;

        float width = Mathf.Max(1, referenceResolution.x);
        float height = Mathf.Max(1, referenceResolution.y);
        closeRuntimeMaterial.SetVector(
            ReferenceResolutionId,
            new Vector4(width, height, 1f / width, 1f / height));
    }

    void ConfigureProcessMaterial()
    {
        if (processRuntimeMaterial == null)
            return;

        processRuntimeMaterial.SetFloat(
            BlockCountId,
            Mathf.Max(1, processBlockCount));
        processRuntimeMaterial.SetFloat(ProgressId, 0f);

        if (processActive == null || processActive.sprite == null)
            return;

        Vector4 outerUv = DataUtility.GetOuterUV(processActive.sprite);
        processRuntimeMaterial.SetVector(
            SpriteUvRectId,
            new Vector4(
                outerUv.x,
                outerUv.y,
                outerUv.z - outerUv.x,
                outerUv.w - outerUv.y));
    }

    void ConfigureBootRevealMaterial()
    {
        if (bootRevealRuntimeMaterial == null)
            return;

        float width = Mathf.Max(1, referenceResolution.x);
        float height = Mathf.Max(1, referenceResolution.y);
        bootRevealRuntimeMaterial.SetVector(
            ReferenceResolutionId,
            new Vector4(width, height, 1f / width, 1f / height));
        bootRevealRuntimeMaterial.SetVector(
            OriginId,
            new Vector4(
                Mathf.Clamp01(bootRevealOrigin.x),
                Mathf.Clamp(bootRevealOrigin.y, -0.25f, 0.25f),
                0f,
                0f));
        bootRevealRuntimeMaterial.SetFloat(
            HasSnapshotId,
            bootRevealSnapshot != null ? 1f : 0f);
        bootRevealRuntimeMaterial.SetFloat(ProgressId, 0f);
    }

    IEnumerator PlayBootRevealRoutine()
    {
        StopLoadingTipLoop();
        PrepareBootRevealMask();
        HideSourceChildren();

        if (bootRevealDelay > 0f)
            yield return new WaitForSecondsRealtime(bootRevealDelay);

        if (!isEffectEnabled)
        {
            bootRevealRoutine = null;
            yield break;
        }

        PlayBootAudio();

        if (snapshotImage == null)
        {
            bootRevealRoutine = null;
            FinishBootReveal();
            yield break;
        }
        bootRevealRoutine = null;

        Tween tween = DOVirtual.Float(
                0f,
                1f,
                Mathf.Max(0.05f, bootRevealDuration),
                ApplyBootRevealProgress)
            .SetEase(Ease.InOutQuad)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        bootRevealTween = tween;
        tween
            .OnComplete(() =>
            {
                if (bootRevealTween != tween)
                    return;

                bootRevealTween = null;
                ApplyBootRevealProgress(1f);
                FinishBootReveal();
            })
            .OnKill(() =>
            {
                if (bootRevealTween == tween)
                    bootRevealTween = null;
            });
    }

    void PlayBootAudio()
    {
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            Debug.LogWarning(
                "[OS CloseAndReOpen] AudioManager is missing; "
                + "Boot SFX was skipped.",
                this);
            return;
        }

        audioManager.PlaySfx(BootSfxKey);
    }

    void PrepareBootRevealMask()
    {
        CacheSnapshotHierarchy();
        if (snapshotImage == null)
            return;

        FitBootRevealMaskToRoot();
        snapshotImage.rectTransform.SetAsLastSibling();
        snapshotImage.gameObject.SetActive(true);
        snapshotImage.enabled = true;
        snapshotImage.raycastTarget = true;
        snapshotImage.texture = bootRevealSnapshot;
        snapshotImage.material = bootRevealRuntimeMaterial;
        bootRevealRuntimeMaterial?.SetFloat(
            HasSnapshotId,
            bootRevealSnapshot != null ? 1f : 0f);
        SetGraphicAlpha(snapshotImage, 1f);
        ApplyBootRevealProgress(0f);
    }

    void FitBootRevealMaskToRoot()
    {
        if (snapshotImage == null)
            return;

        RectTransform maskTransform = snapshotImage.rectTransform;
        if (!bootRevealMaskTransformCached)
        {
            bootRevealMaskAuthoredLocalScale = maskTransform.localScale;
            bootRevealMaskTransformCached = true;
        }

        Transform maskParent = maskTransform.parent;
        if (maskParent == null)
            return;

        Vector3 parentScale = maskParent.lossyScale;
        Vector3 rootScale = transform.lossyScale;
        float scaleX = Mathf.Abs(parentScale.x) > 0.0001f
            ? Mathf.Abs(rootScale.x / parentScale.x)
            : 1f;
        float scaleY = Mathf.Abs(parentScale.y) > 0.0001f
            ? Mathf.Abs(rootScale.y / parentScale.y)
            : 1f;
        maskTransform.localScale = new Vector3(
            bootRevealMaskAuthoredLocalScale.x * scaleX,
            bootRevealMaskAuthoredLocalScale.y * scaleY,
            bootRevealMaskAuthoredLocalScale.z);
    }

    void RestoreBootRevealMaskTransform()
    {
        if (!bootRevealMaskTransformCached || snapshotImage == null)
            return;

        snapshotImage.rectTransform.localScale =
            bootRevealMaskAuthoredLocalScale;
    }

    void CacheSnapshotHierarchy()
    {
        if (snapshotImage == null)
            return;

        Transform[] hierarchy =
            transform.GetComponentsInChildren<Transform>(true);
        int sourceCount = Mathf.Max(0, hierarchy.Length - 1);
        sourceChildren = new Transform[sourceCount];
        sourceChildrenActiveStates = new bool[sourceCount];
        for (int i = 0; i < sourceCount; i++)
        {
            Transform child = hierarchy[i + 1];
            sourceChildren[i] = child;
            sourceChildrenActiveStates[i] = child.gameObject.activeSelf;
        }
    }

    void HideSourceChildren()
    {
        if (sourceChildren == null)
        {
            Transform[] hierarchy =
                transform.GetComponentsInChildren<Transform>(true);
            for (int i = 1; i < hierarchy.Length; i++)
            {
                Transform child = hierarchy[i];
                if (!IsBootRevealMaskOrAncestor(child))
                    child.gameObject.SetActive(false);
            }

            return;
        }

        for (int i = 0; i < sourceChildren.Length; i++)
        {
            Transform child = sourceChildren[i];
            if (child != null && !IsBootRevealMaskOrAncestor(child))
                child.gameObject.SetActive(false);
        }
    }

    bool IsBootRevealMaskOrAncestor(Transform candidate)
    {
        if (candidate == null || snapshotImage == null)
            return false;

        Transform maskTransform = snapshotImage.transform;
        return candidate == maskTransform
            || maskTransform.IsChildOf(candidate);
    }

    void RestoreSnapshotHierarchy()
    {
        if (sourceChildren != null && sourceChildrenActiveStates != null)
        {
            int count = Mathf.Min(
                sourceChildren.Length,
                sourceChildrenActiveStates.Length);
            for (int i = 0; i < count; i++)
            {
                Transform child = sourceChildren[i];
                if (child != null)
                    child.gameObject.SetActive(sourceChildrenActiveStates[i]);
            }
        }

        sourceChildren = null;
        sourceChildrenActiveStates = null;
    }

    void ApplyBootRevealProgress(float progress)
    {
        if (bootRevealRuntimeMaterial != null)
            bootRevealRuntimeMaterial.SetFloat(
                ProgressId,
                Mathf.Clamp01(progress));
    }

    void FinishBootReveal()
    {
        if (snapshotImage != null)
        {
            snapshotImage.enabled = false;
            snapshotImage.raycastTarget = false;
            snapshotImage.texture = null;
            SetGraphicAlpha(snapshotImage, 0f);
        }

        ReleaseBootRevealSnapshot();

        BootRevealCompleted?.Invoke();
    }

    void ApplyCloseProgress(float progress)
    {
        if (closeRuntimeMaterial != null)
        {
            closeRuntimeMaterial.SetFloat(
                ProgressId,
                Mathf.Clamp01(progress));
        }
    }

    void FadeStateIndex1Volume()
    {
        StopStateIndex1VolumeTween();

        Volume volume = ResolveStateIndex1Volume();
        if (volume == null)
            return;

        stateIndex1VolumeStartWeight = volume.weight;
        hasStateIndex1VolumeStartWeight = true;
        Tween tween = DOTween.To(
                () => volume.weight,
                value => volume.weight = value,
                0f,
                Mathf.Max(0.05f, closeDuration))
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        stateIndex1VolumeTween = tween;
        tween.OnKill(() =>
        {
            if (stateIndex1VolumeTween == tween)
                stateIndex1VolumeTween = null;
        });
    }

    Volume ResolveStateIndex1Volume()
    {
        if (stateIndex1Volume != null)
            return stateIndex1Volume;

        Volume[] volumes = VolumeManager.instance.GetVolumes(
            LayerMask.GetMask("OSFX"));
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (volume != null
                && volume.gameObject.name == StateIndex1VolumeName)
            {
                stateIndex1Volume = volume;
                return stateIndex1Volume;
            }
        }

        volumes = UnityEngine.Object.FindObjectsByType<Volume>(
            FindObjectsInactive.Include);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (volume != null
                && volume.gameObject.name == StateIndex1VolumeName)
            {
                stateIndex1Volume = volume;
                return stateIndex1Volume;
            }
        }

        return null;
    }

    void ResetStateIndex1Volume()
    {
        if (!hasStateIndex1VolumeStartWeight)
            return;

        Volume volume = stateIndex1Volume;
        if (volume != null)
            volume.weight = stateIndex1VolumeStartWeight;

        hasStateIndex1VolumeStartWeight = false;
    }

    void ApplyLoadingProgress(float progress)
    {
        LoadingProgress = Mathf.Clamp01(progress);
        if (processRuntimeMaterial != null)
            processRuntimeMaterial.SetFloat(ProgressId, LoadingProgress);

        int percent = Mathf.RoundToInt(LoadingProgress * 100f);
        if (processNumber == null || percent == displayedPercent)
            return;

        displayedPercent = percent;
        processNumber.text = percent + "%";
    }

    void ApplyResetVisuals()
    {
        RestoreSnapshotHierarchy();
        RestoreBootRevealMaskTransform();
        ReleaseBootRevealSnapshot();
        if (snapshotImage != null)
        {
            snapshotImage.texture = null;
            snapshotImage.material = bootRevealRuntimeMaterial;
            snapshotImage.enabled = false;
            snapshotImage.raycastTarget = false;
            SetGraphicAlpha(snapshotImage, 0f);
        }

        ApplyCloseProgress(0f);
        ApplyBootRevealProgress(0f);
        displayedPercent = -1;
        ApplyLoadingProgress(0f);

        if (closeImage != null)
        {
            closeImage.gameObject.SetActive(true);
            closeImage.raycastTarget = false;
            closeImage.enabled = false;
        }

        if (restartBackground != null)
        {
            restartBackground.raycastTarget = false;
            restartBackground.enabled = false;
            restartBackground.gameObject.SetActive(false);
        }

        if (processBackground != null)
        {
            processBackground.gameObject.SetActive(true);
            processBackground.raycastTarget = false;
            SetGraphicAlpha(processBackground, 0f);
        }

        if (processActive != null)
        {
            processActive.gameObject.SetActive(true);
            processActive.raycastTarget = false;
            processActive.enabled = false;
            SetGraphicAlpha(processActive, processActiveAlpha);
        }

        if (processNumber != null)
        {
            processNumber.gameObject.SetActive(true);
            processNumber.raycastTarget = false;
            SetGraphicAlpha(processNumber, 0f);
        }

        if (loadingTip != null)
        {
            loadingTip.gameObject.SetActive(true);
            loadingTip.raycastTarget = false;
            SetGraphicAlpha(loadingTip, 0f);
            SetLoadingTipDots(0);
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

    void StopAnimations()
    {
        StopCloseRoutine();
        StopBootRevealRoutine();
        StopRestartSequence();
        StopLoadingTipLoop();
        StopBootRevealTween();
        StopStateIndex1VolumeTween();
    }

    void StopCloseRoutine()
    {
        if (closeRoutine == null)
            return;

        StopCoroutine(closeRoutine);
        closeRoutine = null;
    }

    void StopRestartSequence()
    {
        if (restartSequence == null)
            return;

        Sequence sequence = restartSequence;
        restartSequence = null;
        sequence.Kill(false);
    }

    void StopBootRevealRoutine()
    {
        if (bootRevealRoutine == null)
            return;

        StopCoroutine(bootRevealRoutine);
        bootRevealRoutine = null;
    }

    void StopBootRevealTween()
    {
        if (bootRevealTween == null)
            return;

        Tween tween = bootRevealTween;
        bootRevealTween = null;
        tween.Kill(false);
    }

    void StopLoadingTipLoop()
    {
        if (loadingTipSequence == null)
            return;

        Sequence sequence = loadingTipSequence;
        loadingTipSequence = null;
        sequence.Kill(false);
    }

    void StopStateIndex1VolumeTween()
    {
        if (stateIndex1VolumeTween == null)
            return;

        Tween tween = stateIndex1VolumeTween;
        stateIndex1VolumeTween = null;
        tween.Kill(false);
    }

    void RestoreAndDestroyRuntimeMaterials()
    {
        ReleaseBootRevealSnapshot();
        if (closeImage != null && closeImage.material == closeRuntimeMaterial)
            closeImage.material = originalCloseMaterial;
        if (processActive != null
            && processActive.material == processRuntimeMaterial)
        {
            processActive.material = originalProcessMaterial;
        }
        if (snapshotImage != null
            && snapshotImage.material == bootRevealRuntimeMaterial)
        {
            snapshotImage.material = originalSnapshotMaterial;
        }

        if (closeRuntimeMaterial != null)
            Destroy(closeRuntimeMaterial);
        if (processRuntimeMaterial != null)
            Destroy(processRuntimeMaterial);
        if (bootRevealRuntimeMaterial != null)
            Destroy(bootRevealRuntimeMaterial);

        closeRuntimeMaterial = null;
        processRuntimeMaterial = null;
        bootRevealRuntimeMaterial = null;
    }

    void ReplaceBootRevealSnapshot(Texture2D snapshot)
    {
        ReleaseBootRevealSnapshot();
        bootRevealSnapshot = snapshot;
        if (bootRevealSnapshot != null)
        {
            bootRevealSnapshot.name = "OS Boot Reveal Snapshot (Runtime)";
            bootRevealSnapshot.filterMode = FilterMode.Bilinear;
            bootRevealSnapshot.wrapMode = TextureWrapMode.Clamp;
        }

        bootRevealRuntimeMaterial?.SetFloat(
            HasSnapshotId,
            bootRevealSnapshot != null ? 1f : 0f);
    }

    void ReleaseBootRevealSnapshot()
    {
        if (snapshotImage != null
            && snapshotImage.texture == bootRevealSnapshot)
        {
            snapshotImage.texture = null;
        }

        if (bootRevealSnapshot != null)
            Destroy(bootRevealSnapshot);

        bootRevealSnapshot = null;
        bootRevealRuntimeMaterial?.SetFloat(HasSnapshotId, 0f);
    }

}
